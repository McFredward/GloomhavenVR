"""Preserve witnessed raw exports across the audited recovery-merge update.

Derived Android assets remain keyed by the complete current recipe. Only raw
core/bundle exports can keep their original absolute workspace: exporter identity
rows and the merge journal refer to that path. The recovery process still verifies
its source, tool, core, batch and committed checkpoint receipts before replay.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import stat

from storage import BuildError, _ordinary_owned, digest, value_hash, write_json


LEGACY_MERGE_FILES = {
    "tools/quest-recovery/bundle_recovery.py": "5196337cbbe1858f2fd982ad71fc20f8fd000128463609fe065aa6fbee98ff56",
    "tools/quest-recovery/full_recovery.py": "29702e3356cd17e786e3d3e57ebee37f11c8201a03aaecc3a86137a3c8447e39",
    "tools/quest-recovery/native_evidence.py": "e711d1017ecd3b5b98dcec91896a4dedafe31ee9a087fdded76da226fbb61951",
}
# Immediately preceding B627 Builder (a22376f8c): subsequent changes add only
# scoped progress boundaries. Adopt the entire reviewed profile, not a guessed
# version or a mixture of independently unknown orchestration implementations.
PREVIOUS_PROGRESS_FILES = {
    "tools/quest-recovery/bundle_recovery.py": "6931dc03f4e19de3c50a5248ca7ef6cea2a2100ca1de86903617ac80b98470d2",
    "tools/quest-recovery/full_recovery.py": "e2c9ccc1c170126c3204a1f0ea69db84d1fd35c01118a4b076e083f065e6b5a3",
    "tools/quest-recovery/native_evidence.py": "ec675fd7506f42c0af3877c63f4f31eeb81fddd48dfc9e9a6e4636f75fbdf076",
}
# Reviewed nested-progress Builder (70ca45264), immediately preceding the
# invocation-owned verification repair. Keep this whole shipped orchestration
# profile eligible; its exporter/config/instrumentation contract is unchanged.
NESTED_PROGRESS_FILES = {
    "tools/quest-recovery/bundle_recovery.py": "41a1507d7c973593419c40745beed0bfff61f3b6a41f118730a60a786674ae9a",
    "tools/quest-recovery/full_recovery.py": "793f13e432a860febc4754a6b0c07ce0ca5e3eedc0e32ed857ff3131b024435a",
    "tools/quest-recovery/native_evidence.py": "d774d94dc0333b5f1c1c68d4498fe15c95f4e23436565ef05930c0eeeafc7abe",
}
# Shipped Builder 5dce06841: the subsequent observer reads existing exporter
# logs and minimizes repeated Python index/path work. The C# capture, pinned
# exporter/config and native object contract remain byte-identical. Accept this
# complete reviewed profile, never a mix of individually recognized files.
MINIMAL_RECOVERY_FILES = {
    "tools/quest-recovery/bundle_recovery.py": "5657aa6e87ab3a17afb3d09afb21b78a89256b90536dad1caeed6bc0f317aa02",
    "tools/quest-recovery/full_recovery.py": "cb9a909c2cfd7372ada13a4ff5102bd3ce13472684264dcfa9d10eff3a22ed07",
    "tools/quest-recovery/native_evidence.py": "092139fb8a478d21f114a0e989262c709037a092eec05ff3e8970bdb1aca95f4",
}
OBSERVER_FILE = "tools/quest-recovery/recover.py"
PREVIOUS_OBSERVER_SHA256 = "8502923f2cfc7d85c4f9fca29356c5b5f3bacf66b2def2379a01c3f68f52c9d1"
ORCHESTRATION_FILES = set(LEGACY_MERGE_FILES) | {OBSERVER_FILE}
DERIVED_FILES = {"tools/quest-builder/full_assets.py", "tools/quest-builder/full_shaders.py"}
MAX_MANIFESTS = 128
MAX_JSON_BYTES = 16 * 1024 * 1024
# Full core exports list every asset and .meta file. The 2026-10-06 Windows
# capture failed before child recovery because this inventory inherited the
# small control-manifest cap. Only this receipt receives the larger bound;
# eligibility still checks all records and the child independently hashes the
# retained exports. No asset copying/re-export is required to read the receipt.
MAX_CORE_RECEIPT_BYTES = 256 * 1024 * 1024
HEX = re.compile(r"[0-9a-f]{64}\Z")


def recipe_files(inputs):
    return [row for row in inputs["mod"]["files"]
            if row["path"].startswith("tools/quest-recovery/") or row["path"] in DERIVED_FILES]


def recipe_key(inputs, recipe):
    return value_hash({"game": inputs["game"]["key"], "recoveryRecipe": recipe_files(inputs), "recipe": recipe})


def _read(path, *, max_bytes=MAX_JSON_BYTES):
    path = _ordinary_owned(path)
    label = path.name + " (limit " + str(max_bytes) + " bytes)"
    try:
        observed = path.stat()
    except FileNotFoundError as exc:
        raise BuildError("Recovery resume evidence is missing: " + label) from exc
    except OSError as exc:
        raise BuildError("Recovery resume evidence is unreadable: " + label + "; " + type(exc).__name__) from exc
    label = path.name + " (" + str(observed.st_size) + " bytes; limit " + str(max_bytes) + " bytes)"
    if not stat.S_ISREG(observed.st_mode):
        raise BuildError("Recovery resume evidence is not a regular file: " + label)
    if observed.st_size > max_bytes:
        raise BuildError("Recovery resume evidence is oversized: " + label)
    try:
        # Keep the actual read bounded too, even if a file grows after stat.
        with path.open("rb") as stream:
            raw = stream.read(max_bytes + 1)
        if len(raw) > max_bytes:
            raise BuildError("Recovery resume evidence grew beyond its read limit: " + label
                             + "; observed at least " + str(len(raw)) + " bytes")
        value = json.loads(raw.decode("utf-8"))
    except (OSError, ValueError) as exc:
        raise BuildError("Recovery resume evidence is unreadable: " + label + "; " + type(exc).__name__) from exc
    if not isinstance(value, dict):
        raise BuildError("Recovery resume evidence is not an object: " + path.name)
    return value


def _records(rows, size_key):
    if not isinstance(rows, list) or not rows:
        raise BuildError("Recovery resume evidence has no file inventory.")
    result = {}
    for row in rows:
        if not isinstance(row, dict): raise BuildError("Recovery resume inventory is invalid.")
        name, size, hashed = row.get("path"), row.get(size_key), row.get("sha256")
        if (not isinstance(name, str) or not name or "\\" in name or ":" in name or "\0" in name
                or PurePosixPath(name).is_absolute() or any(part in ("", ".", "..") for part in name.split("/"))
                or name in result or not isinstance(size, int) or isinstance(size, bool) or size < 0
                or not isinstance(hashed, str) or HEX.fullmatch(hashed) is None):
            raise BuildError("Recovery resume inventory has an unsafe or invalid file record.")
        result[name] = {"path": name, "bytes": size, "sha256": hashed}
    return result


def _compatible(previous, current):
    old, new = _records(recipe_files(previous), "size"), _records(recipe_files(current), "size")
    required = ORCHESTRATION_FILES | {"tools/quest-recovery/QuestExportIdentity.cs",
               "tools/quest-recovery/export_identity.py", "tools/quest-recovery/tool-lock.json"}
    if old.keys() != new.keys() or not required <= old.keys(): return False
    changed = []
    for name in old:
        if name in DERIVED_FILES: continue  # Their new stage is never adopted.
        if old[name] == new[name]: continue
        if name not in ORCHESTRATION_FILES: return False
        changed.append(name)
    if not changed: return True
    # Every exporter/config/capture source outside these exact four is still
    # identical. The child retains the full input/core/batch/journal hash gates.
    return any(all(old[name]["sha256"] == expected for name, expected in profile.items())
               for profile in ({**base, OBSERVER_FILE: PREVIOUS_OBSERVER_SHA256} for base in
                               (LEGACY_MERGE_FILES, PREVIOUS_PROGRESS_FILES, NESTED_PROGRESS_FILES, MINIMAL_RECOVERY_FILES)))


def _manifest(path):
    value = _read(path)
    identity = value.get("inputKey")
    if (not isinstance(identity, str) or HEX.fullmatch(identity) is None
            or path.name != identity + ".json"
            or value_hash({key: item for key, item in value.items() if key != "inputKey"}) != identity):
        raise BuildError("Recovery resume input manifest identity changed: " + path.name)
    for name in ("game", "mod"):
        member = value.get(name)
        if (not isinstance(member, dict) or not isinstance(member.get("key"), str)
                or HEX.fullmatch(member["key"]) is None or not isinstance(member.get("files"), list)
                or value_hash({"files": member["files"]}) != member["key"]):
            raise BuildError("Recovery resume input inventory identity changed: " + path.name)
    if value.get("schema") != 1:
        raise BuildError("Recovery resume input manifest schema is unsupported.")
    return value


def _qualification(workspace, inputs, game):
    """Cheap eligibility proof; the child retains full independent hash gates."""
    original = _read(workspace / "original-source.json")
    snapshot = _ordinary_owned(game / ".snapshot.json")
    expected = _records(inputs["game"]["files"], "size")
    if snapshot.is_file():
        proof = _read(snapshot)
        if proof != {"schema": 1, "files": inputs["game"]["files"]}:
            raise BuildError("The retained game snapshot belongs to different inputs.")
        expected[snapshot.name] = {"path": snapshot.name, "bytes": snapshot.stat().st_size, "sha256": digest(snapshot)}
    actual = _records(original.get("sourceInventory"), "bytes")
    fingerprint = hashlib.sha256(json.dumps(original["sourceInventory"], sort_keys=True,
                                            separators=(",", ":")).encode()).hexdigest()
    if actual != expected or original.get("schema") != 1 or original.get("sourceFingerprint") != fingerprint:
        raise BuildError("Retained raw recovery source differs from the selected original game.")
    core = _read(workspace / "core-recovery.json", max_bytes=MAX_CORE_RECEIPT_BYTES)
    instrumentation = next(row["sha256"] for row in recipe_files(inputs)
                           if row["path"] == "tools/quest-recovery/QuestExportIdentity.cs")
    source_proof = core.get("exporterSource")
    if (core.get("schema") != 1 or core.get("sourceFingerprint") != fingerprint
            or not isinstance(source_proof, dict) or source_proof.get("instrumentationSha256") != instrumentation):
        raise BuildError("Retained core export does not match the current original/exporter identity.")
    _records(core.get("files"), "bytes")
    identities = _ordinary_owned(workspace / "core-identities.jsonl")
    if not identities.is_file() or digest(identities) != core.get("identitiesSha256"):
        raise BuildError("Retained core native identity evidence changed; raw exports were preserved.")
    return {"sourceFingerprint": fingerprint, "coreIdentitySha256": core["identitiesSha256"],
            "exporterSource": source_proof}


def select_workspace(output, inputs, source, game, recipe):
    """Bind the current derived recipe to one compatible owner-qualified raw tree.

    Caller holds the builder output lock. No raw export or journal is rewritten,
    moved, copied or declared complete here. Full recovery, using this unchanged
    absolute tree, independently qualifies cached tools and exported bytes and
    restores its pending merge before replay. Corruption therefore fails visibly
    instead of deleting evidence or silently re-exporting completed work.
    """
    key = recipe_key(inputs, recipe)
    parent = _ordinary_owned(output / "cache/full-original-recovery")
    current = _ordinary_owned(parent / key)
    binding = _ordinary_owned(output / "cache/raw-recovery-resume" / (key + ".json"))
    current_records = _records(recipe_files(inputs), "size")
    for row in current_records.values():
        path = _ordinary_owned(source / row["path"])
        if not path.is_file() or path.stat().st_size != row["bytes"] or digest(path) != row["sha256"]:
            raise BuildError("Current recovery source differs from its immutable manifest: " + row["path"])
    contract = value_hash({"schema": 1, "files": [row for name, row in sorted(current_records.items())
                                                  if name not in ORCHESTRATION_FILES and name not in DERIVED_FILES]})
    selected_manifest = None
    if binding.exists():
        bound = _read(binding)
        prior = bound.get("originalManifestKey")
        if (bound.get("schema") != 1 or bound.get("owner") != "Quest raw recovery resume"
                or bound.get("currentRecipeKey") != key or bound.get("exportContract") != contract
                or bound.get("gameKey") != inputs["game"]["key"] or not isinstance(prior, str) or HEX.fullmatch(prior) is None):
            raise BuildError("Raw recovery resume ownership changed; retained exports were preserved.")
        selected_manifest = _manifest(output / "manifests" / (prior + ".json"))
    elif current.exists():
        return current  # Same complete recipe retains its established hash gates.
    else:
        manifests = _ordinary_owned(output / "manifests")
        if not manifests.is_dir(): return current
        candidates = []
        for count, path in enumerate(manifests.iterdir(), 1):
            if count > MAX_MANIFESTS:
                raise BuildError("Too many retained input manifests for bounded recovery migration; existing exports were preserved.")
            if re.fullmatch(r"[0-9a-f]{64}\.json", path.name): candidates.append(_ordinary_owned(path))
        for path in sorted(candidates, key=lambda item: item.stat().st_mtime_ns, reverse=True):
            previous = _manifest(path)
            if (previous.get("target") != "game" or previous.get("recipe") != recipe
                    or previous.get("game") != inputs["game"] or not _compatible(previous, inputs)): continue
            workspace = _ordinary_owned(parent / recipe_key(previous, recipe))
            if not (workspace / "core-recovery.json").is_file(): continue
            selected_manifest = previous
            break
    if selected_manifest is None: return current
    if (selected_manifest.get("target") != "game" or selected_manifest.get("recipe") != recipe
            or selected_manifest.get("game") != inputs["game"] or not _compatible(selected_manifest, inputs)):
        raise BuildError("The bound raw recovery workspace no longer has compatible export inputs.")
    original_key = recipe_key(selected_manifest, recipe)
    workspace = _ordinary_owned(parent / original_key)
    if binding.exists() and bound.get("originalWorkspaceKey") != original_key:
        raise BuildError("The bound raw recovery workspace identity changed.")
    proof = _qualification(workspace, inputs, game)
    expected = {"schema": 1, "owner": "Quest raw recovery resume", "currentRecipeKey": key,
                "originalWorkspaceKey": original_key, "originalManifestKey": selected_manifest["inputKey"],
                "gameKey": inputs["game"]["key"], "exportContract": contract, **proof}
    if binding.exists() and bound != expected:
        raise BuildError("Raw recovery resume evidence changed; retained exports were preserved.")
    if not binding.exists(): write_json(binding, expected)
    print("recovery resume: retaining original workspace " + original_key[:12]
          + " for current recipe " + key[:12] + "; raw tool/core/batch/journal verification follows", flush=True)
    return workspace
