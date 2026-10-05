#!/usr/bin/env python3
"""Retain distinct recovered assets at case-safe GENERATED Unity paths.

Run after immutable source validation and template overlay, before Unity import.
Only physical paths and owned manifest path fields change. Original asset bytes,
metadata GUIDs, Addressables keys/labels and recovery receipts remain unchanged.
"""
import argparse
import collections
import copy
import hashlib
import json
from pathlib import Path
import re
import sys

ADDRESSABLES = "Assets/QuestOriginalStartup/startup-addressables.json"
BINDINGS = "Assets/QuestOriginalStartup/script-bindings.json"
RECEIPT = "QuestStartupEvidence/case-path-migration.json"
VARIANTS = "Assets/QuestOriginalStartup/CaseVariants"
GUID = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)


class CasePathError(RuntimeError):
    pass


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def relative_path(value):
    if not isinstance(value, str) or "\\" in value or Path(value).is_absolute():
        raise CasePathError("Invalid generated asset path: " + str(value))
    if ".." in Path(value).parts or not value.startswith("Assets/"):
        raise CasePathError("Generated asset path escapes Assets: " + value)
    return value


def mapped(path, moves):
    for old, new in moves.items():
        if path == old or path.startswith(old + "/"):
            return new + path[len(old):]
    return path


def asset_guid(project, path):
    metadata = project / (path + ".meta")
    if not metadata.is_file() or metadata.is_symlink():
        raise CasePathError("Missing original asset metadata: " + path)
    match = GUID.search(metadata.read_text(encoding="utf-8"))
    if not match:
        raise CasePathError("Missing original asset GUID: " + path)
    return match[1]


def resource_path(path):
    return any(part.casefold() == "resources" for part in Path(path).parts)


def nodes(project):
    result = {}
    for p in sorted((project / "Assets").rglob("*")):
        if p.is_symlink():
            raise CasePathError("Symlink in generated Assets: " + str(p))
        relative = p.relative_to(project).as_posix()
        if not (p.is_file() or p.is_dir()):
            raise CasePathError("Unsupported generated asset node: " + relative)
        if p.name.endswith(".meta"):
            owner = p.with_name(p.name[:-5])
            if not owner.exists():
                raise CasePathError("Orphan metadata: " + relative)
            if not GUID.search(p.read_text(encoding="utf-8")):
                raise CasePathError("Invalid original metadata GUID: " + relative)
        result[relative] = "directory" if p.is_dir() else "file"
    return result


def load_manifests(project):
    addressables = json.loads((project / ADDRESSABLES).read_text())
    bindings = json.loads((project / BINDINGS).read_text())
    if addressables.get("schema") != 1 or not isinstance(addressables.get("entries"), list):
        raise CasePathError("Unsupported startup Addressables manifest")
    if bindings.get("schema") != 1 or not isinstance(bindings.get("assetPaths"), list):
        raise CasePathError("Unsupported startup script bindings manifest")
    associated = set()
    for row in addressables["entries"]:
        path = row.get("assetPath")
        if path is None:
            continue
        relative_path(path)
        if not (project / path).is_file():
            raise CasePathError("Declared startup asset is absent: " + path)
        if asset_guid(project, path) != row.get("recoveredGuid"):
            raise CasePathError("Addressables original GUID mismatch: " + path)
        if "BundledAssetProvider" in row.get("provider", ""):
            associated.add(path)
    for path in bindings["assetPaths"]:
        relative_path(path)
        if not (project / path).is_file():
            raise CasePathError("Declared script binding asset is absent: " + path)
    campaign_path = project / "Assets/QuestOriginalCampaign/campaign-addressables.json"
    if campaign_path.is_file():
        campaign = json.loads(campaign_path.read_text())
        if campaign.get("schema") != 1 or not isinstance(campaign.get("entries"), list):
            raise CasePathError("Unsupported Campaign Addressables manifest")
        for row in campaign["entries"]:
            path = row.get("assetPath")
            if path is None: continue
            relative_path(path)
            if not (project / path).is_file() or asset_guid(project, path) != row.get("recoveredGuid"):
                raise CasePathError("Campaign original asset identity mismatch: " + path)
            if "BundledAssetProvider" in row.get("provider", ""):
                bundled_path = row["assetPath"]
                associated.add(bundled_path)
    return addressables, bindings, associated


def campaign_manifest_updates(project, moves):
    """Migrate generated paths while retaining all original keys and identities."""
    updates = {}
    root = "Assets/QuestOriginalCampaign/"
    for name in ("campaign-addressables.json", "campaign-scenes.json", "script-bindings.json", "campaign-shaders.json"):
        path = root + name
        if not (project / path).is_file(): continue
        document = json.loads((project / path).read_text())
        if document.get("schema") != 1:
            raise CasePathError("Unsupported Campaign path manifest: " + path)
        if name == "campaign-addressables.json":
            for row in document["entries"]:
                if row.get("assetPath") is not None: row["assetPath"] = mapped(row["assetPath"], moves)
        elif name == "campaign-scenes.json":
            for row in document["scenes"]: row["path"] = mapped(row["path"], moves)
        elif name == "script-bindings.json":
            document["assetPaths"] = [mapped(path, moves) for path in document["assetPaths"]]
        else:
            for key in ("shaders", "materials", "programs"):
                for row in document.get(key, []): row["assetPath"] = mapped(row["assetPath"], moves)
        updates[path] = document
    return updates


def plan(nodes_by_path, bundled):
    groups = collections.defaultdict(list)
    for path in nodes_by_path:
        if not path.endswith(".meta"):
            groups[path.casefold()].append(path)
    moves, reasons = {}, {}
    for key, group in sorted(groups.items(), key=lambda item: (item[0].count("/"), item[0])):
        if len(group) < 2:
            continue
        # Relocating a colliding ancestor already separates its descendants.
        effective = collections.defaultdict(list)
        for path in group:
            effective[mapped(path, moves).casefold()].append(path)
        for collision in effective.values():
            if len(collision) < 2:
                continue
            collision.sort()
            canonical = collision[0]
            reason = "ordinal-canonical-distinct-asset-retained"
            if any(resource_path(p) for p in collision):
                # A built-in Resources copy and a separately bundled copy can
                # coexist in a player. Rebuilding both beneath Resources cannot.
                # Keep the unaddressed copy at its original Resources key; move
                # only fully source-associated bundled variants outside Resources.
                classified = {}
                for path in collision:
                    leaves = {p for p, kind in nodes_by_path.items()
                              if kind == "file" and not p.endswith(".meta")
                              and (p == path or p.startswith(path + "/"))}
                    classified[path] = ("bundled" if leaves and leaves <= bundled else
                                        "resources" if leaves and not leaves.intersection(bundled) else "mixed")
                canonical_candidates = [p for p in collision if classified[p] == "resources"]
                if len(canonical_candidates) != 1 or any(classified[p] != "bundled" for p in collision
                                                       if p not in canonical_candidates):
                    raise CasePathError("Ambiguous original Resources collision; no safe key choice: "
                                        + ", ".join(collision))
                canonical = canonical_candidates[0]
                reason = "retain-unaddressed-Resources-copy-relocate-catalog-proven-bundled-copy"
            for path in collision:
                if path == canonical:
                    continue
                if mapped(path, moves) != path:
                    raise CasePathError("Overlapping migration plan: " + path)
                token = hashlib.sha256(path.encode("utf-8")).hexdigest()[:16]
                name = Path(path).name
                if name.casefold() == "resources":
                    name = "source_" + name
                destination = VARIANTS + "/case_" + token + "/" + name
                moves[path] = destination
                if path + ".meta" in nodes_by_path:
                    moves[path + ".meta"] = destination + ".meta"
                reasons[path] = reason
    for old, new in moves.items():
        if any(new.casefold() == p.casefold() or new.casefold().startswith(p.casefold() + "/")
               and nodes_by_path[p] == "file" for p in nodes_by_path):
            raise CasePathError("Migration destination is occupied: " + new)
        for parent in Path(new).parents:
            if parent.as_posix() in (".", "Assets"):
                continue
            conflicts = [p for p in nodes_by_path if p.casefold() == parent.as_posix().casefold()
                         and p != parent.as_posix()]
            if conflicts:
                raise CasePathError("Migration parent has conflicting casing: " + str(parent))
        if resource_path(old):
            for path in nodes_by_path:
                if (path == old or path.startswith(old + "/")) and resource_path(mapped(path, moves)):
                    raise CasePathError("Bundled variant would retain a conflicting Resources key: " + path)
    output = collections.defaultdict(list)
    for path in nodes_by_path:
        output[mapped(path, moves).casefold()].append(path)
    if any(len(values) > 1 for values in output.values()):
        raise CasePathError("Migration leaves a case collision")
    return moves, reasons


def verify_receipt(project, receipt):
    if receipt.get("schema") != 1 or receipt.get("target") != "startup":
        raise CasePathError("Unsupported generated case migration receipt")
    for row in receipt.get("files", []):
        path = relative_path(row["assetPath"])
        actual = project / path
        if not actual.is_file() or digest(actual) != row["sha256"]:
            raise CasePathError("Previously migrated asset changed: " + path)
    for path, expected in receipt.get("manifestSha256", {}).items():
        relative_path(path)
        if digest(project / path) != expected:
            raise CasePathError("Previously migrated manifest changed: " + path)


def migrate(project):
    project = Path(project).resolve()
    # A raw owned-game export/stage has no Quest template. This tool belongs
    # only to the generated player pipeline, after validating the raw receipt.
    if not (project / "Assets/Quest").is_dir():
        raise CasePathError("Refusing raw recovery/source project: generated Quest template is required")
    if (project / "Assets").is_symlink() or (project / "Assets/Quest").is_symlink():
        raise CasePathError("Generated project contains a symlinked asset root")
    if (project / "QuestStartupEvidence").is_symlink() or (project / RECEIPT).is_symlink():
        raise CasePathError("Generated evidence must not be symlinked")
    all_nodes = nodes(project)
    addressables, bindings, bundled = load_manifests(project)
    receipt_path = project / RECEIPT
    if receipt_path.exists():
        receipt = json.loads(receipt_path.read_text())
        verify_receipt(project, receipt)
        if plan(all_nodes, bundled)[0]:
            raise CasePathError("New case collision after a completed migration")
        return receipt
    moves, reasons = plan(all_nodes, bundled)
    rows = []
    for path, kind in all_nodes.items():
        destination = mapped(path, moves)
        if path == destination or kind != "file":
            continue
        # Metadata follows its owner; every moved native asset retains its GUID.
        owner = path[:-5] if path.endswith(".meta") else path
        guid = asset_guid(project, owner)
        rows.append({"originalPath": path, "assetPath": destination, "guid": guid,
                     "sha256": digest(project / path), "size": (project / path).stat().st_size})
    new_addressables, new_bindings = copy.deepcopy(addressables), copy.deepcopy(bindings)
    for row in new_addressables["entries"]:
        if row.get("assetPath") is not None:
            row["assetPath"] = mapped(row["assetPath"], moves)
    new_bindings["assetPaths"] = [mapped(p, moves) for p in bindings["assetPaths"]]
    updates = {ADDRESSABLES: new_addressables, BINDINGS: new_bindings}
    updates.update(campaign_manifest_updates(project, moves))
    previous = {path: (project / path).read_bytes() for path in updates}
    operations = sorted(moves.items(), key=lambda item: (item[0].count("/"), item[0]))
    completed = []
    try:
        for old, new in operations:
            target = project / new
            target.parent.mkdir(parents=True, exist_ok=True)
            (project / old).rename(target)
            completed.append((old, new))
        for path, document in updates.items():
            (project / path).write_text(json.dumps(document, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        if plan(nodes(project), {mapped(p, moves) for p in bundled})[0]:
            raise CasePathError("Generated asset paths remain unsafe")
        receipt = {"schema": 1, "target": "startup", "fullGameReady": False,
                   "assetContentChanged": False, "serializedReferencesChanged": False,
                   "addressableKeysChanged": False, "pathMappings": moves,
                   "moves": [{"originalPath": p, "assetPath": moves[p], "reason": reasons[p]}
                             for p in sorted(reasons)], "files": rows,
                   "manifestSha256": {path: digest(project / path) for path in updates},
                   "beforeManifestSha256": {path: hashlib.sha256(data).hexdigest()
                                            for path, data in previous.items()}}
        verify_receipt(project, receipt)
        receipt_path.parent.mkdir(parents=True, exist_ok=True)
        receipt_path.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        return receipt
    except BaseException:
        for path, data in previous.items():
            (project / path).write_bytes(data)
        for old, new in reversed(completed):
            (project / old).parent.mkdir(parents=True, exist_ok=True)
            (project / new).rename(project / old)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, type=Path)
    args = parser.parse_args()
    try:
        report = migrate(args.project)
    except (CasePathError, OSError, ValueError, KeyError, TypeError) as error:
        print("Startup path migration rejected: " + str(error), file=sys.stderr)
        return 2
    print(json.dumps({"movedUnits": len(report["moves"]), "retainedFiles": len(report["files"]),
                      "receipt": str(args.project / RECEIPT), "fullGameReady": False}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
