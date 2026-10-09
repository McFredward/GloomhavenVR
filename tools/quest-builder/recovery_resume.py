"""Preserve witnessed raw exports across audited post-export repairs.

Derived Android assets remain keyed by the complete transformation recipe. An
exact, reviewed parser/native byte repair retains its preceding key; all other source
changes receive a new derived workspace. The recovery process still verifies
source, tool, core, batch and committed checkpoint receipts before replay.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import stat

from storage import BuildError, _ordinary_owned, digest, value_hash, write_json, build_progress


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
# Published 24ea5928b / B640 reached native audit after completing copy, runtime,
# GUID and layout restoration. Its Windows CRLF parser repair changes only YAML
# observation and error context. Preserve that exact workspace/Journal owner,
# rather than repeat the already committed transformations under a new path.
# This is an exact byte-reviewed alias, not a wildcard for later recover.py edits.
# Every other recovery/derived file remains part of the complete recipe hash.
CRLF_OBSERVER_PREVIOUS = {
    "path": OBSERVER_FILE, "size": 47202,
    "sha256": "ac15d03682290541f6271926b989eec084b5b35746e7915fe1e77ec75ef5c45a",
}
CRLF_OBSERVER_FIXED = {
    "path": OBSERVER_FILE, "size": 47667,
    "sha256": "c0e738efa56b4c6c29cea965eb94a6e274e0ed19bd3802a42206cf343339365f",
}
# Published B642 reached native overlay application after the same six
# committed transformations. These two consumers restored original pointers
# and packed-Sprite fields, but text-mode I/O normalized CRLF before hashing.
# The exact reviewed pair now retains UTF-8 bytes, YAML offsets and line endings.
# Keep both paths in the recipe/raw contract and normalize only the complete
# pair: excluding them from the raw contract would invalidate existing bound raw
# receipts, and accepting either helper independently could hide a later edit.
NATIVE_BYTES_PREVIOUS = {
    "tools/quest-recovery/pointer_recovery.py": {
        "path": "tools/quest-recovery/pointer_recovery.py", "size": 21460,
        "sha256": "bfa225a6657012c784215dc07214ea695852bdbc23d8a9e113e45b8c9ea4ebd6"},
    "tools/quest-recovery/packed_sprites.py": {
        "path": "tools/quest-recovery/packed_sprites.py", "size": 9806,
        "sha256": "a11ec97298a274886cf73e873558f695787c8a733ce930d5e683c37c019bc609"},
}
NATIVE_BYTES_FIXED = {
    "tools/quest-recovery/pointer_recovery.py": {
        "path": "tools/quest-recovery/pointer_recovery.py", "size": 23171,
        "sha256": "346f1c48627bac95a03501d7061d351589c009295e3f39907ce419758696ed41"},
    "tools/quest-recovery/packed_sprites.py": {
        "path": "tools/quest-recovery/packed_sprites.py", "size": 10254,
        "sha256": "add7402c47346647beed79bf07f103e0c7631e8bbdf933b9e44ee6ac52b320eb"},
}
# B643's first Windows completion retained all fourteen derived transformations
# and four preparation steps. The following exact observer/cache update changes
# no generated original asset bytes. Preserve those owners only for the complete
# reviewed profile; an unrelated or partial producer update retains a new key.
OBSERVATION_PREVIOUS = {
    "tools/quest-builder/storage.py": {
        "path": "tools/quest-builder/storage.py", "size": 32239,
        "sha256": "62a0c3a50579a2505ff33ac8abd9d17d8d71b76c6ce9c6b2aca0ccf48837fc25"},
    "tools/quest-builder/full_assets.py": {
        "path": "tools/quest-builder/full_assets.py", "size": 31808,
        "sha256": "70356fc0af07ba66deae7a60359e7a20794e5d51596f40b05643c42cca039abb"},
    "tools/quest-builder/staging_resume.py": {
        "path": "tools/quest-builder/staging_resume.py", "size": 20847,
        "sha256": "5f4f803decb96a49eaf1e00801e6d4325684666f19b19c3769be6f06e12e4d89"},
    "tools/quest-builder/prepare_resume.py": {
        "path": "tools/quest-builder/prepare_resume.py", "size": 27384,
        "sha256": "ad5b12ad8a51756b1f0dee4609a903fe8380feab69f35de8724355c622d3750d"},
}
OBSERVATION_BYTE_WITNESSES = {
    "tools/quest-builder/storage.py": {
        "path": "tools/quest-builder/storage.py", "size": 50272,
        "sha256": "d747a4413138c3140d2a1bbfb48fa424165c99853e49e5e5e73b5dcfe000daf2"},
    "tools/quest-builder/full_assets.py": {
        "path": "tools/quest-builder/full_assets.py", "size": 32663,
        "sha256": "871186d12af12ab23dd59746acd9a0ade6d87feee03f6b654d4b6451e874206e"},
    "tools/quest-builder/staging_resume.py": {
        "path": "tools/quest-builder/staging_resume.py", "size": 21425,
        "sha256": "9a8f392029555564018f46357df7c64cd4350601e8ba41b8d99a98107a0ab5eb"},
    "tools/quest-builder/prepare_resume.py": {
        "path": "tools/quest-builder/prepare_resume.py", "size": 34048,
        "sha256": "abebf9fc07e2ff2137f82b55bde0ab8fa1e69f8ee0eda227689bc602a7a8f7f1"},
}
# Exact observer-only follow-up: the existing movie undo writer now reports
# streamed byte counts. Copy/hash/undo contracts and original output bytes are
# unchanged. Preserve both complete published witness profiles, never mixes.
OBSERVATION_FIXED = {
    **OBSERVATION_BYTE_WITNESSES,
    "tools/quest-builder/prepare_resume.py": {
        "path": "tools/quest-builder/prepare_resume.py", "size": 35121,
        "sha256": "9e685ebd51d0d99dc6967f4c93f35d632e228172dd03c5fe430a662821d40355"},
}
# Persistent source inventory reads each unchanged original once, and the
# invocation observer reuses only already qualified frozen source stamps. These
# two helpers change no recovered or prepared asset bytes.
OBSERVATION_INPUT_INDEX = {
    **OBSERVATION_FIXED,
    "tools/quest-builder/storage.py": {
        "path": "tools/quest-builder/storage.py", "size": 54297,
        "sha256": "321461243ec5c89044a9bbef8904a094f69dfc1c4a580012525ae2c147e47a58"},
    "tools/quest-builder/prepare_resume.py": {
        "path": "tools/quest-builder/prepare_resume.py", "size": 35360,
        "sha256": "c9b9961b434446566946314e40c8a5be2b9007c04ce78b318dad682a47dfc20e"},
}
# Restore the historical case-sensitive relative-path order on Windows. Only
# input enumeration changes: the indexed byte proofs and asset producers remain
# the complete preceding profile. Preserve old original-conversion owners.
OBSERVATION_INPUT_ORDER = {
    **OBSERVATION_INPUT_INDEX,
    "tools/quest-builder/storage.py": {
        "path": "tools/quest-builder/storage.py", "size": 54724,
        "sha256": "40ccd9aa5df2598e2a17471a60992b49261bf80302ab63070285030912d830ff"},
}
# 070011 already reuses 191,415 recovery files by true metadata, but repeated
# per-file Windows opens/ancestor walks/SQLite calls still take minutes. This
# exact batch-observer profile changes no producer bytes, checkpoint order,
# rollback ownership or ordinary copied outputs. Alias only the complete set;
# unknown observer edits still receive distinct preparation/derived identities.
OBSERVATION_DIRECTORY_BATCH = {
    "tools/quest-builder/storage.py": {
        "path": "tools/quest-builder/storage.py", "size": 63570,
        "sha256": "fd62c43f346dd7673ff153c7e8cd2221a4a91400351e8285de7eed0fb3d604fe"},
    "tools/quest-builder/full_assets.py": {
        "path": "tools/quest-builder/full_assets.py", "size": 32663,
        "sha256": "871186d12af12ab23dd59746acd9a0ade6d87feee03f6b654d4b6451e874206e"},
    "tools/quest-builder/staging_resume.py": {
        "path": "tools/quest-builder/staging_resume.py", "size": 21916,
        "sha256": "1adbfa212166bc7266bb3da6a6750cb91f3547fad2806d69f59116a38f17b3d4"},
    "tools/quest-builder/prepare_resume.py": {
        "path": "tools/quest-builder/prepare_resume.py", "size": 36948,
        "sha256": "d62d02196cf330e960c076781bcf3f59a029de9e812fd636431648f4139592cf"},
}
OBSERVATION_PROFILES = (OBSERVATION_BYTE_WITNESSES, OBSERVATION_FIXED,
                        OBSERVATION_INPUT_INDEX, OBSERVATION_INPUT_ORDER, OBSERVATION_DIRECTORY_BATCH)
# Shipped d4cc44eeb adds an exporter-log observer and invocation-local proof
# caches. The current reference-audit update changes no raw export identities.
# Preserve this exact whole profile as well as the previous shipped profiles.
TIMED_RECOVERY_FILES = {
    'tools/quest-recovery/bundle_recovery.py': 'd9acd494323b080785e868d6809b4935053fd852ece92b257d0ea64fcca3eada',
    'tools/quest-recovery/full_recovery.py': '7c50355e648109232182f84ea271017603c12c87f70380cce76dbdca5ed23858',
    'tools/quest-recovery/native_evidence.py': 'f272fd2db64f6b59a2d7e4c2fcc9eb130bc0996b9fb21fa20f7c82cb12ec8d62',
    'tools/quest-recovery/recover.py': 'ff0e086e45e8ed1978f69bd0fea5d738aabe7ea59d260c28dbf69b08ef914b7f',
}
ORCHESTRATION_FILES = set(LEGACY_MERGE_FILES) | {OBSERVER_FILE}
# native_stage runs only on the fresh derived project, after all raw export
# checkpoints. Its deferred duplicate audit cannot alter an original export.
DERIVED_FILES = {"tools/quest-builder/full_assets.py", "tools/quest-builder/full_shaders.py",
                 "tools/quest-recovery/native_stage.py", "tools/quest-builder/staging_resume.py"}
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


def _native_byte_rows(rows):
    """Canonicalize one exact native consumer pair, never source qualification."""
    by_path = {}
    for row in rows:
        by_path.setdefault(row["path"], []).append(row)
    if (NATIVE_BYTES_FIXED and all(by_path.get(name) == [fixed]
                                  for name, fixed in NATIVE_BYTES_FIXED.items())):
        return [NATIVE_BYTES_PREVIOUS.get(row["path"], row) for row in rows]
    return rows


def preparation_source_rows(rows):
    """Return reviewed output-equivalent rows; actual source proof stays exact."""
    rows = _native_byte_rows(rows)
    by_path = {}
    for row in rows:
        by_path.setdefault(row["path"], []).append(row)
    if any(profile and all(by_path.get(name) == [fixed] for name, fixed in profile.items())
           for profile in OBSERVATION_PROFILES):
        return [OBSERVATION_PREVIOUS.get(row["path"], row) for row in rows]
    return rows


def recipe_key(inputs, recipe):
    selected = {"mod": {"files": preparation_source_rows(inputs["mod"]["files"])}}
    rows = [CRLF_OBSERVER_PREVIOUS if row == CRLF_OBSERVER_FIXED else row
            for row in recipe_files(selected)]
    return value_hash({"game": inputs["game"]["key"], "recoveryRecipe": rows, "recipe": recipe})


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
    old = _records(_native_byte_rows(recipe_files(previous)), "size")
    new = _records(_native_byte_rows(recipe_files(current)), "size")
    required = ORCHESTRATION_FILES | {"tools/quest-recovery/QuestExportIdentity.cs",
               "tools/quest-recovery/export_identity.py", "tools/quest-recovery/tool-lock.json"}
    # New derived-only journals do not change the raw exporter contract. Their
    # complete bytes still key the new derived stage; never require an older
    # shipped raw manifest to contain a helper which runs only after export.
    old_raw = {name: row for name, row in old.items() if name not in DERIVED_FILES}
    new_raw = {name: row for name, row in new.items() if name not in DERIVED_FILES}
    if old_raw.keys() != new_raw.keys() or not required <= old_raw.keys(): return False
    changed = []
    for name in old:
        if name in DERIVED_FILES: continue  # Their new stage is never adopted.
        if old[name] == new[name]: continue
        if (name == OBSERVER_FILE
                and old[name] == {"path": name, "bytes": CRLF_OBSERVER_PREVIOUS["size"], "sha256": CRLF_OBSERVER_PREVIOUS["sha256"]}
                and new[name] == {"path": name, "bytes": CRLF_OBSERVER_FIXED["size"], "sha256": CRLF_OBSERVER_FIXED["sha256"]}):
            continue  # The same reviewed observer-only repair also keeps a bound raw tree eligible.
        if name not in ORCHESTRATION_FILES: return False
        changed.append(name)
    if not changed: return True
    # Every exporter/config/capture source outside these exact four is still
    # identical. The child retains the full input/core/batch/journal hash gates.
    profiles = [{**base, OBSERVER_FILE: PREVIOUS_OBSERVER_SHA256} for base in
                (LEGACY_MERGE_FILES, PREVIOUS_PROGRESS_FILES, NESTED_PROGRESS_FILES, MINIMAL_RECOVERY_FILES)]
    profiles.append(TIMED_RECOVERY_FILES)
    return any(all(old[name]["sha256"] == expected for name, expected in profile.items()) for profile in profiles)


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


def select_workspace(output, inputs, source, game, recipe, *, qualifications=None):
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
    # This binding predates the exact consumer repair. Preserve its witnessed
    # contract while the current immutable source was independently qualified
    # above against the actual fixed bytes, never these canonical recipe rows.
    contract_records = _records(_native_byte_rows(recipe_files(inputs)), "size")
    contract = value_hash({"schema": 1, "files": [row for name, row in sorted(contract_records.items())
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
    if qualifications is not None: qualifications[str(workspace)] = proof
    expected = {"schema": 1, "owner": "Quest raw recovery resume", "currentRecipeKey": key,
                "originalWorkspaceKey": original_key, "originalManifestKey": selected_manifest["inputKey"],
                "gameKey": inputs["game"]["key"], "exportContract": contract, **proof}
    if binding.exists() and bound != expected:
        raise BuildError("Raw recovery resume evidence changed; retained exports were preserved.")
    if not binding.exists(): write_json(binding, expected)
    print("recovery resume: retaining original workspace " + original_key[:12]
          + " for current recipe " + key[:12] + "; raw tool/core/batch/journal verification follows", flush=True)
    return workspace


def completed_raw(workspace, inputs, game, *, qualifications=None):
    """Adopt a genuinely closed raw result; qualify bytes at their consumer.

    The 2026-10-07/440b Windows witness re-exported no packages but spent 1,012 s
    rehashing 189,737 raw files (both rotated logs), before staging would hash the same bytes during
    copying. A successful full-recovery result and its complete, source/core/
    catalog-bound checkpoint can retain those exports without launching the
    exporter orchestration again. Staging must still qualify EVERY consumed
    original file against this checkpoint (or its already accepted target).
    A pending merge takes the ordinary journal-recovery path instead. This is
    never a success receipt for derived assets, Unity, Addressables or a Player.
    """
    workspace, game = _ordinary_owned(workspace), _ordinary_owned(game)
    result_path = _ordinary_owned(workspace / "full-recovery.json")
    pending = _ordinary_owned(workspace / "BundleRecovery/merge-pending.json")
    if not result_path.is_file() or pending.exists(): return None
    raw = _read(result_path)
    if raw.get("fullOriginalCatalogRecovered") is not True: return None
    counter = build_progress.Counter("recovery-retained-result-verify", 8, "receipts")
    try:
        if raw.get("schema") != 1:
            raise BuildError("Completed raw recovery result has an unsupported schema.")
        expected_paths = {
            "recoveryProject": workspace / "RecoveredProject",
            "coreProject": workspace / "CoreExport/ExportedProject",
            "managedTypes": workspace / "RecoveredProject/QuestRecovery/managed-types.json",
            "cabBundles": workspace / "original-cab-bundles.json",
        }
        for name, path in expected_paths.items():
            if raw.get(name) != str(path):
                raise BuildError("Completed raw recovery result lost its owned " + name + " path.")
            _ordinary_owned(path)
        counter.add(1, "Closed raw recovery result")
        proof = (qualifications or {}).get(str(workspace))
        if proof is None: proof = _qualification(workspace, inputs, game)
        if raw.get("sourceFingerprint") != proof["sourceFingerprint"]:
            raise BuildError("Completed raw recovery result belongs to different original inputs.")
        counter.add(1, "Original source and core identities")
        plan = _read(workspace / "BundleRecovery/bundle-plan.json")
        groups = plan.get("groups")
        if not isinstance(groups, list) or not 1 <= len(groups) <= 4096:
            raise BuildError("Completed raw recovery has no bounded original package schedule.")
        original_files = _records(inputs["game"]["files"], "size")
        for group in groups:
            if not isinstance(group, dict): raise BuildError("Completed original package plan is invalid.")
            for name, row in _records(group.get("bundles"), "bytes").items():
                if original_files.get(name) != row:
                    raise BuildError("Completed original package plan differs from its source: " + name)
        counter.add(1, "Completed original package schedule")
        catalog = game / "StreamingAssets/aa/catalog.json"
        if digest(_ordinary_owned(catalog)) != plan.get("catalogSha256"):
            raise BuildError("Completed original package catalog changed.")
        counter.add(1, "Original catalog identity")
        project = expected_paths["recoveryProject"]
        checkpoint = _read(project / "quest-full-recovery-progress.json", max_bytes=MAX_CORE_RECEIPT_BYTES)
        completed = checkpoint.get("completedGroups")
        if (checkpoint.get("schema") != 1 or checkpoint.get("assetsRecovered") is not True
                or checkpoint.get("sourceFingerprint") != proof["sourceFingerprint"]
                or checkpoint.get("coreIdentitySha256") != proof["coreIdentitySha256"]
                or checkpoint.get("catalogSha256") != plan["catalogSha256"]
                or not isinstance(completed, list) or any(type(index) is not int for index in completed)
                or sorted(completed) != list(range(len(groups)))):
            raise BuildError("Closed original recovery checkpoint is incomplete or belongs to different inputs.")
        recorded_groups = checkpoint.get("groups")
        if (not isinstance(recorded_groups, list) or len(recorded_groups) != len(groups)
                or any(not isinstance(row, dict) or type(row.get("index")) is not int
                       for row in recorded_groups)
                or {row["index"] for row in recorded_groups} != set(completed)
                or any(not isinstance(row, dict) or row.get("input") != groups[row["index"]]
                       for row in recorded_groups)):
            raise BuildError("Completed original package receipts disagree with their schedule.")
        inventory = _records(checkpoint.get("files"), "bytes")
        if not isinstance(checkpoint.get("assetReferences"), dict):
            raise BuildError("Completed raw recovery lost its reference audit.")
        counter.add(1, "Closed checkpoint and retained reference audit")
        managed = expected_paths["managedTypes"]
        row = inventory.get(managed.relative_to(project).as_posix())
        if row is None or managed.stat().st_size != row["bytes"] or digest(managed) != row["sha256"]:
            raise BuildError("Completed raw recovery managed metadata changed.")
        counter.add(1, "Original managed metadata")
        owners = _read(expected_paths["cabBundles"])
        bundles = {row["path"] for group in groups for row in group["bundles"]}
        if not owners or any(not isinstance(name, str) or not name or not isinstance(path, str)
                             or path not in bundles for name, path in owners.items()):
            raise BuildError("Completed raw recovery physical bundle ownership is invalid.")
        counter.add(1, "Physical bundle ownership")
        source_inventory = _records(checkpoint.get("sourceInventory"), "bytes")
        original = _read(workspace / "original-source.json")
        if source_inventory != _records(original.get("sourceInventory"), "bytes"):
            raise BuildError("Closed original checkpoint lost its qualified source inventory.")
        counter.add(1, "Final original source guard")
        counter.finish()
    except BaseException as error:
        counter.fail(error)
        raise
    raw["_completedRaw"] = {"packages": len(groups), "files": len(inventory)}
    print("recovery resume: reusing all " + str(len(groups)) + " completed original packages; "
          + "exact copied/consumed bytes remain qualified during staging", flush=True)
    return raw


def announce_completed_raw(raw):
    """Publish witnessed export boundaries, never derived/project completion."""
    count = raw["_completedRaw"]["packages"]
    build_progress.event("recovery-plan", count, count, "batches", "Reusing the complete original package schedule", status="reuse")
    for section in ("source", "core", "batches", "references", "source-recheck", "checkpoint", "cab-index"):
        build_progress.event("recovery-section:" + section, 1, 1, "sections",
                             "Completed original export retained", status="reuse")
        if section == "batches":
            build_progress.event("recovery-batches", count, count, "batches", "Completed original packages reused", status="reuse")
