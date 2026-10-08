"""Retain exact native ComputeShader identities when .asset becomes .compute.

Unity's NativeFormatImporter uses external PPtr type2; ComputeShaderImporter
uses type3 even when the native class72 GUID/local file ID is unchanged. Only
actual serialized PPtr type tokens targeting the recovered class72 identities
are changed. The owner's other bytes and every compute source/meta stay intact.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

if __package__:
    from .native import ComputeRecoveryError
else:
    from native import ComputeRecoveryError

RECEIPT = "QuestCampaignEvidence/compute-reference-types.json"
IDENTITIES = "QuestRecovery/original-asset-identities.json"
MANIFEST = "Assets/QuestOriginalCampaign/campaign-computes.json"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def asset(project, name):
    if (not isinstance(name, str) or not name.startswith("Assets/") or "\\" in name
            or any(value in ("", ".", "..") for value in name.split("/"))):
        raise ComputeRecoveryError("Compute reference target escapes generated Assets.")
    path = project / name
    if path.is_symlink() or not path.is_file():
        raise ComputeRecoveryError("Compute reference target is missing or linked: " + name)
    return path


def repair(project, manifest, *, expected_count=13, apply=True):
    """Return/save exact owner-byte and original native identity evidence.

    The complete overlay caller supplies its already checked manifest. This API
    also supports the retained imported project: .compute bytes/metas are checked
    against that same manifest and no import/cache/player output is rewritten.
    All plans validate before the first owner is changed.
    """
    project = Path(project).resolve()
    recovery = Path(__file__).resolve().parents[1] / "quest-recovery"
    if str(recovery) not in sys.path:
        sys.path.append(str(recovery))
    from export_identity import POINTER
    from recover import is_unity_yaml, serialized_pointer_tokens

    identity_path = project / IDENTITIES
    identities = json.loads(identity_path.read_text(encoding="utf-8"))
    if identities.get("schema") != 1 or not isinstance(identities.get("identities"), list):
        raise ComputeRecoveryError("Native compute reference identity manifest is invalid.")
    by_guid = {}
    for row in identities["identities"]:
        by_guid.setdefault(row["guid"], []).append(row)
    rows = manifest.get("shaders", [])
    if len(rows) != expected_count:
        raise ComputeRecoveryError("Compute reference target census differs from the complete overlay.")
    targets, immutable = {}, {}
    for row in rows:
        guid, local_id = row.get("guid"), row.get("localFileId")
        name, original = row.get("assetPath"), row.get("originalPath")
        if (not isinstance(guid, str) or not re.fullmatch(r"[0-9a-f]{32}", guid)
                or guid in targets or row.get("classId") != 72 or local_id != 7200000
                or not isinstance(original, str) or not original.endswith(".asset")
                or name != original[:-6] + ".compute" or manifest.get("pathMap", {}).get(original) != name):
            raise ComputeRecoveryError("Compute reference target loses its original class72 identity.")
        source, meta = asset(project, name), asset(project, name + ".meta")
        source_hash, meta_hash = digest(source.read_bytes()), digest(meta.read_bytes())
        if source_hash != row.get("sourceSha256") or meta_hash != row.get("metaSha256"):
            raise ComputeRecoveryError("Native compute source/meta changed before reference repair.")
        text = meta.read_text(encoding="utf-8")
        if re.findall(r"^guid: ([0-9a-f]{32})$", text, re.M) != [guid] or "\nComputeShaderImporter:\n" not in text:
            raise ComputeRecoveryError("Compute target is not its witnessed ComputeShaderImporter.")
        native = by_guid.get(guid, [])
        if len(native) != 1 or native[0]["path"] not in (original, name):
            raise ComputeRecoveryError("Compute target has no unique captured original native identity.")
        objects = native[0].get("objects", [])
        if (len(objects) != 1 or objects[0].get("classId") != 72 or objects[0].get("fileId") != local_id
                or not objects[0].get("collection") or not objects[0].get("pathId")):
            raise ComputeRecoveryError("Compute target native CAB/pathID/type/localID is not retained.")
        targets[guid] = {"guid": guid, "fileId": local_id, "assetPath": name, "type": 3,
            "originalCollection": objects[0]["collection"], "originalPathId": objects[0]["pathId"],
            "classId": 72, "sourceSha256": source_hash, "metaSha256": meta_hash}
        immutable[name], immutable[name + ".meta"] = source_hash, meta_hash

    # The literal GUID prefix makes this scan cheap even for native mesh payloads;
    # YAML parsing is restricted to documents containing a target GUID.
    candidate = re.compile(rb"guid:\s*(?:" + b"|".join(guid.encode() for guid in sorted(targets)) + rb")")
    plans, owners = [], []
    for path in sorted((project / "Assets").rglob("*")):
        if not path.is_file() or not is_unity_yaml(path):
            continue
        raw = path.read_bytes()
        if not candidate.search(raw):
            continue
        if path.is_symlink():
            raise ComputeRecoveryError("Compute reference owner must not be linked.")
        text = raw.decode("utf-8")
        spans = {(left, right): guid for guid, left, right in serialized_pointer_tokens(text) if guid in targets}
        if not spans:  # A plain scalar containing a GUID is not an object pointer.
            continue
        matches = [match for match in POINTER.finditer(text) if (match.start(2), match.end(2)) in spans]
        if {(match.start(2), match.end(2)) for match in matches} != set(spans):
            raise ComputeRecoveryError("Compute owner has an unwitnessed serialized PPtr representation.")
        replacements, references = [], []
        for match in matches:
            target = targets[match[2]]
            if int(match[1]) != target["fileId"] or int(match[3]) not in (2, 3):
                raise ComputeRecoveryError("Compute owner changes the captured native localID/reference type.")
            if match[3] == "2":
                replacements.append((match.start(3), match.end(3), "3"))
            references.append({"guid": match[2], "fileId": int(match[1]), "beforeType": int(match[3]),
                "type": 3, "typeTokenOffset": len(text[:match.start(3)].encode("utf-8"))})
        after = text
        for left, right, value in reversed(replacements):
            after = after[:left] + value + after[right:]
        restored = after
        for left, right, _ in reversed(replacements):
            restored = restored[:left] + text[left:right] + restored[right:]
        if restored.encode("utf-8") != raw:
            raise ComputeRecoveryError("Compute repair changed bytes outside the exact PPtr type tokens.")
        relative = path.relative_to(project).as_posix()
        owner_asset = relative[:-5] if relative.endswith(".meta") else relative
        owner_meta = asset(project, owner_asset + ".meta").read_text(encoding="utf-8")
        owner_guids = re.findall(r"^guid: ([0-9a-f]{32})$", owner_meta, re.M)
        owner_native = by_guid.get(owner_guids[0], []) if len(owner_guids) == 1 else []
        if len(owner_native) != 1 or owner_native[0]["path"] != owner_asset:
            raise ComputeRecoveryError("Compute reference owner has no unique captured original identity.")
        owners.append({"assetPath": relative, "beforeSha256": digest(raw), "sha256": digest(after.encode("utf-8")),
            "changedReferenceCount": len(replacements), "references": references,
            "originalGuid": owner_guids[0], "originalObjects": owner_native[0]["objects"],
            "unchangedOtherOwnerBytes": True})
        if replacements:
            plans.append((path, raw, after.encode("utf-8")))

    # Check every target/owner again before committing a validated byte-only plan.
    if any(digest(asset(project, name).read_bytes()) != checksum for name, checksum in immutable.items()):
        raise ComputeRecoveryError("Compute source changed while reference repair was planned.")
    if any(path.read_bytes() != before for path, before, _ in plans):
        raise ComputeRecoveryError("Compute owner changed while reference repair was planned.")
    if apply:
        for path, _, after in plans:
            path.write_bytes(after)
    receipt = {"schema": 1, "scope": "native-class72-ComputeShaderImporter-PPtr-types",
        "targetCount": len(targets), "ownerCount": len(owners),
        "changedReferenceCount": sum(row["changedReferenceCount"] for row in owners),
        "originalIdentityManifestSha256": digest(identity_path.read_bytes()),
        "generatorSha256": digest(Path(__file__).read_bytes()), "targets": list(targets.values()), "owners": owners,
        "unchangedComputeSourcesAndMetas": True, "unchangedOtherOwnerBytes": True,
        "applied": apply, "unityImportVerified": False, "hardwareVerified": False}
    receipt_path = project / RECEIPT
    if apply:
        receipt_path.parent.mkdir(parents=True, exist_ok=True)
        receipt_path.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--receipt", type=Path)
    args = parser.parse_args()
    manifest_path = args.project / MANIFEST
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["nativeComputeReferenceTypes"] = repair(args.project, manifest, apply=not args.dry_run)
    if not args.dry_run:
        manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if args.receipt:
        args.receipt.parent.mkdir(parents=True, exist_ok=True)
        args.receipt.write_text(json.dumps(manifest["nativeComputeReferenceTypes"], indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({key: manifest["nativeComputeReferenceTypes"][key] for key in
        ("targetCount", "ownerCount", "changedReferenceCount", "unchangedOtherOwnerBytes")}, indent=2))


if __name__ == "__main__":
    main()
