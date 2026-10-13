"""Bind native bank checkpoints to actual cooking inputs and closed legacy builds.

This never certifies an APK or headset result. The standalone content helper
owns native output byte proofs. A first legacy adoption requires the exact
retained compiler invocation and the captured post-cooking packer failure.
"""
from __future__ import annotations

from pathlib import Path
import hashlib
import json
import re
import stat

from storage import BuildError, _ordinary_owned, _file_witness_stamp, record_file, value_hash
from import_workspace import workspace_key

HEX = re.compile(r"[0-9a-f]{64}\Z")
CONTROLS = ("Assets/QuestOriginalCampaign/campaign-addressables.json",
            "Assets/QuestOriginalCampaign/campaign-shaders.json",
            "Assets/QuestOriginalCampaign/campaign-computes.json",
            "Assets/QuestOriginalCampaign/script-bindings.json",
            "Packages/manifest.json",
            "QuestStartupEvidence/PlayerSdk/compilation.json")
NATIVE_RECEIPT_BYTES = 64 * 1024 * 1024  # Match the isolated checkpoint reader's metadata bound.


def _read(path, limit=4 * 1024 * 1024):
    path = _ordinary_owned(path)
    if not path.is_file() or path.stat().st_size > limit:
        raise BuildError("Native bank legacy provenance is missing or oversized.")
    before = record_file(path, path.name)
    with path.open("rb") as stream:
        raw = stream.read(limit + 1)
    if len(raw) > limit:
        raise BuildError("Native bank legacy provenance grew beyond its bound while read.")
    value = json.loads(raw.decode("utf-8"))
    if not isinstance(value, dict):
        raise BuildError("Native bank provenance must be a JSON object.")
    if before != record_file(path, path.name):
        raise BuildError("Native bank legacy provenance changed while read.")
    return value


def _prepared_controls(output, project, preparation):
    """Legacy cooking requires the retained complete producers' actual controls.

    This one bounded JSON journal read is needed only for pre-checkpoint builds.
    It never walks or rehashes the tens of thousands of original asset files.
    """
    journal = _read(output / "cache/prepare-resume" / project.name / "journal.json", preparation.MAX_JOURNAL_BYTES)
    if (journal.get("schema") != 1 or journal.get("owner") != "Quest preparation substage journal"
            or journal.get("project") != project.relative_to(output).as_posix()
            or not preparation._completed_game_preparation(journal, "game")):
        return False
    latest = {}
    for step in journal["steps"]:
        outputs = step.get("outputs", [])
        if not isinstance(outputs, list): return False
        for row in outputs:
            if not isinstance(row, dict) or not isinstance(row.get("path"), str): return False
            latest[row["path"]] = row
    for name in CONTROLS:
        if name == "QuestStartupEvidence/PlayerSdk/compilation.json": continue
        row = latest.get(name)
        if not row or row.get("absent") or record_file(project / name, name) != row:
            return False
    sdk = _read(project / "QuestStartupEvidence/PlayerSdk/compilation.json")
    return sdk == {"schema": 1, "target": "Android", "backend": "IL2CPP", "compilation": "Player",
                   "options": "DevelopmentBuild|Assertions", "unityVersion": "2021.3.5f1"}


def _runtime_rows(inputs, preparation):
    """Retain exact code/native/type inputs, apart from the reviewed downloader."""
    selected = []
    prefixes = ("src/", "tools/QuestWeaver/", "tools/quest-native/", "tools/quest-network/",
                "tools/quest-procedural-runtime/", "libs/RuntimeDeps/", "libs/Natives/",
                "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/")
    for row in inputs["mod"]["files"]:
        name = row["path"]
        if not name.startswith(prefixes) or name.endswith(".md") or "/tests/" in name:
            continue
        if (inputs.get("proceduralBackend") == "proton-arm64ec-fex"
                and row == preparation.PROTON_DOWNLOAD_FIXED):
            row = preparation.PROTON_DOWNLOAD_PREVIOUS
        selected.append(row)
    return sorted(selected, key=lambda row: row["path"])


def identity(inputs, source, project, tools, preparation, recovery):
    """Bind closed preparation/source scopes, streamed controls and real compiler.

    Capture20261013T191013 contains a valid prepared 74,621,329-byte shader
    manifest. The previous 64 MiB JSON-style guard rejected that existing file,
    although identity never parses it. Keep the exact record_file digest/size
    contract and its cached native change-stamp proofs; do not impose a JSON
    allocation bound on a one-MiB streaming hash or recook valid large controls.
    """
    scope = preparation._scope(inputs, source, recovery, original_prefix=True, completed=True)
    controls = []
    for name in CONTROLS:
        path = _ordinary_owned(Path(project) / name)
        try:
            value = path.stat()
        except FileNotFoundError as error:
            raise BuildError("Native bank prepared control is missing: " + name) from error
        if not stat.S_ISREG(value.st_mode):
            raise BuildError("Native bank prepared control is not a regular file: " + name)
        controls.append(record_file(path, name))
    return {"schema": 1, "scope": "quest-native-bank-cooking-inputs",
            "preparation": value_hash(scope), "runtimeSources": _runtime_rows(inputs, preparation),
            "controls": controls, "target": "Android", "graphicsApi": "Vulkan",
            "compiler": {name: tools[name] for name in (
                "unityVersion", "editorSha256", "ndkPropertiesSha256")}}


def _tail_proves_completed_bank(path):
    """Bounded read of a closed compiler log, not a size scan of game content."""
    path = _ordinary_owned(path)
    if not path.is_file(): return False
    stamp = path.stat()
    before = _file_witness_stamp(path, stamp)
    with path.open("rb") as stream:
        stream.seek(max(0, stamp.st_size - 256 * 1024))
        text = stream.read(256 * 1024).decode("utf-8", errors="replace")
    after = path.stat()
    witness = lambda item: (item.st_dev, item.st_ino, item.st_size, item.st_mtime_ns, item.st_ctime_ns)
    if witness(stamp) != witness(after): return False
    if before is not None:
        if _file_witness_stamp(path, after) != before: return False
    else:
        # Unsupported native metadata cannot certify even a same-size rewrite.
        # Re-read only the bounded tail, rather than hashing a huge Editor log.
        with path.open("rb") as stream:
            stream.seek(max(0, stamp.st_size - 256 * 1024))
            if stream.read(256 * 1024).decode("utf-8", errors="replace") != text: return False
    success = text.rfind("Addressable content successfully built (duration : ")
    failure = text.rfind("Native content packer stopped: No module named 'storage'")
    return (0 <= success < failure and "executeMethod method " in text[failure:]
            and "threw exception" in text[failure:]
            and not re.search(r"(?im)^\s*(?:Shader error in|Compute shader error in|Build failed)", text))


def _actual_sources_match(provenance, previous):
    """A folder hash cannot prove the actual sources used by the old compiler.

    Production provenance records all staged Editor scripts and live Python
    drivers, including drift from the frozen snapshot. Admit only complete
    exact sets whose actual bytes matched that immutable invocation inventory.
    Unknown local Editor/driver repairs must use ordinary content compilation.
    """
    if provenance.get("schema") != 1 or not isinstance(provenance.get("toolchain"), dict): return False
    rows = previous["mod"]["files"]
    editor_prefix = "unity/GloomhavenVR.Quest/Assets/Quest/Editor/"
    drivers = {row["path"]: row for row in rows
               if row["path"].startswith("tools/quest-builder/") and row["path"].endswith(".py")
               and row["path"].count("/") == 2}
    editors = {row["path"]: row for row in rows
               if row["path"].startswith(editor_prefix) and row["path"].endswith(".cs")}
    if "tools/quest-builder/builder.py" not in drivers or not editors: return False
    for field, expected in (("buildDriverModules", drivers), ("stagedEditorSources", editors)):
        actual = provenance.get(field)
        if not isinstance(actual, list) or len(actual) != len(expected): return False
        seen = set()
        for row in actual:
            if not isinstance(row, dict): return False
            name = row.get("snapshotPath"); baseline = expected.get(name)
            if baseline is None or name in seen: return False
            seen.add(name)
            public = name.removeprefix("unity/GloomhavenVR.Quest/") if field == "stagedEditorSources" else name
            if (row.get("path") != public or row.get("size") != baseline["size"]
                    or row.get("sha256") != baseline["sha256"]
                    or row.get("snapshotSha256") != baseline["sha256"]
                    or row.get("changedFromSnapshot") is not False): return False
    return provenance["toolchain"].get("buildDriverSha256") == drivers["tools/quest-builder/builder.py"]["sha256"]


def legacy_adoption(output, project, inputs, source, tools, preparation, recovery, *, recipe):
    """Authorize only an exact compatible old invocation in this same workspace."""
    output, project = _ordinary_owned(output), _ordinary_owned(project)
    try:
        scope = identity(inputs, source, project, tools, preparation, recovery)
        if not _prepared_controls(output, project, preparation): return None
    except (OSError, ValueError, TypeError, KeyError, RecursionError, BuildError): return None
    try:
        candidates = _ordinary_owned(output / "builds")
        if not candidates.is_dir(): return None
        folders = [path for path in candidates.iterdir() if HEX.fullmatch(path.name)
                   and path.is_dir() and not path.is_symlink()]
        folders = sorted(folders, key=lambda path: path.stat().st_mtime_ns, reverse=True)[:32]
    except (OSError, BuildError):
        return None  # Optional old evidence cannot prevent the ordinary compiler path.
    for folder in folders:
        try:
            provenance = _read(folder / "build-provenance.json")
            runtime = provenance["runtime"]
            old_key = runtime["inputKey"]
            if not HEX.fullmatch(str(old_key)): continue
            previous = recovery._manifest(output / "manifests" / (old_key + ".json"))
            if not _actual_sources_match(provenance, previous): continue
            if runtime != {"inputKey": old_key, "sourceCommit": previous["mod"]["commit"],
                           "modBuild": previous["mod"]["modBuild"]}: continue
            if previous.get("target") != "game": continue
            if workspace_key(previous, "game") != project.name: continue
            old_source = _ordinary_owned(output / "inputs/mod" / previous["mod"]["key"])
            # Controls have already been retained/repaired by completed preparation.
            # Compare the real previous source scope and code inventory, not merely
            # matching game/profile names or a log found elsewhere on disk.
            old_scope = identity(previous, old_source, project, provenance["toolchain"], preparation, recovery)
            if old_scope != scope: continue
            known = False
            for shader_qa in (False, True):
                base = {"input": old_key, "toolchain": provenance["toolchain"]["key"], "recipe": recipe,
                        "buildProvenance": value_hash(provenance), "validateCampaignShaders": shader_qa}
                known |= value_hash(base) == folder.name
                known |= value_hash({**base, "fullAssetValidation": True}) == folder.name
            if not known: continue
            log = output / "logs" / ("unity-build-" + folder.name[:12] + ".log")
            if not _tail_proves_completed_bank(log): continue
            return {"schema": 1, "scope": "qualified-legacy-native-bank-adoption",
                    "compilerKey": value_hash(scope), "previousInputKey": old_key,
                    "previousBuildKey": folder.name, "log": log.relative_to(output).as_posix(),
                    "reason": "exact-closed-addressables-with-downstream-isolated-packer-import-failure",
                    "hardwareVerified": False}
        except (OSError, ValueError, TypeError, KeyError, RecursionError, BuildError):
            continue  # Unknown/changed old work cannot authorize cache adoption.
    return None


def environment(base, inputs, source, project, output, tools, preparation, recovery, *, recipe, progress):
    result = dict(base)
    result.pop("GHVR_QUEST_NATIVE_BANK_KEY", None)
    result.pop("GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT", None)
    if inputs.get("target") != "game": return result
    selected = identity(inputs, source, project, tools, preparation, recovery)
    result["GHVR_QUEST_NATIVE_BANK_KEY"] = value_hash(selected)
    # A modern closed receipt is independently qualified by the isolated
    # helper before reuse. Its presence with this exact compiler owner makes
    # legacy authorization unnecessary; skip that much larger journal read.
    try:
        receipt = _read(Path(project) / "QuestStartupEvidence/native-addressables-build.json", NATIVE_RECEIPT_BYTES)
        sealed = dict(receipt); proof = sealed.pop("checkpointSha256", None)
        owner = receipt.get("owner", {})
        if (isinstance(owner, dict) and receipt.get("schema") == 1 and receipt.get("scope") == "closed-native-addressables"
                and receipt.get("compilerCompleted") is True
                and proof == hashlib.sha256(json.dumps(sealed, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()
                and owner.get("compilerKey") == result["GHVR_QUEST_NATIVE_BANK_KEY"]
                and owner.get("projectRoot") == str(Path(project).resolve())):
            return result
    except (OSError, ValueError, TypeError, KeyError, RecursionError, BuildError):
        pass
    adopted = legacy_adoption(output, project, inputs, source, tools, preparation, recovery, recipe=recipe)
    if adopted:
        result["GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT"] = "1"
        progress.event("native-bank-resume", 1, 1, "receipts",
                       "Retaining completed Android content compilation; qualifying the existing bank before packing.",
                       status="complete")
    return result
