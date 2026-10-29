"""Resume a completed Android Player at its unfinished host-delivery boundary.

The 065619 Windows capture completed Unity/Gradle and then failed in zipalign
selection. Repeating source conversion, weaving, import or Player compilation
cannot repair that failure. This selector reads bounded receipts, ZIP metadata
and three existing original-file metadata witnesses. Final signature/content
validation remains the delivery owner's responsibility; this is not APK approval.
An older completed Player keeps its frozen mod/profile even with a newer builder.
"""
from __future__ import annotations

import json
import hashlib
import os
from pathlib import Path
import re
import sqlite3
import stat
import struct
import zipfile

MAX_JSON = 8 * 1024 * 1024
MAX_DIRECTORY = 16 * 1024 * 1024
MAX_MEMBERS = 65536
MAX_BUILDS = 256
CHECKPOINT = "player-completed.json"
POINTER = "latest-player.json"
_HASH = re.compile(r"[0-9a-f]{64}")
_ORIGINALS = ("Managed/GH.Runtime.dll", "Managed/GH.Shared.dll", "globalgamemanagers")


def _hash(value):
    return isinstance(value, str) and _HASH.fullmatch(value) is not None


def _ordinary(builder, path, *, directory=False):
    path = builder._ordinary_owned(Path(path))
    value = path.lstat()
    if (getattr(value, "st_file_attributes", 0) & 0x400
            or not (stat.S_ISDIR(value.st_mode) if directory else stat.S_ISREG(value.st_mode))
            or (not directory and value.st_nlink != 1)):
        raise builder.BuildError("Completed Player contains a linked or non-regular owned path: " + path.name)
    return path


def _within(builder, output, relative, *, directory=False):
    if (not isinstance(relative, str) or not relative or "\\" in relative
            or any(part in ("", ".", "..") for part in relative.split("/"))
            or Path(relative).is_absolute() or ":" in relative):
        raise builder.BuildError("Completed Player has an unsafe owned path.")
    return _ordinary(builder, output / relative, directory=directory)


def _json(builder, path, maximum=MAX_JSON):
    path = _ordinary(builder, path)
    before = path.stat()
    if not 0 < before.st_size <= maximum:
        raise builder.BuildError("Completed Player receipt exceeds its bounded size: " + path.name)
    try:
        value = json.loads(path.read_bytes())
    except (ValueError, UnicodeError) as exc:
        raise builder.BuildError("Completed Player receipt is unreadable: " + path.name) from exc
    after = path.stat()
    if (before.st_size, before.st_mtime_ns, before.st_ctime_ns) != (after.st_size, after.st_mtime_ns, after.st_ctime_ns):
        raise builder.BuildError("Completed Player receipt changed while selected: " + path.name)
    if not isinstance(value, dict):
        raise builder.BuildError("Completed Player receipt must be an object: " + path.name)
    return value


def _zip_directory(builder, path):
    """Bound allocation before ZipFile parses its central directory (including ZIP64)."""
    size = path.stat().st_size
    if size < 22:
        raise builder.BuildError("Completed Player APK is truncated.")
    with path.open("rb") as stream:
        stream.seek(max(0, size - 65557)); tail = stream.read(65557)
        marker = tail.rfind(b"PK\x05\x06")
        if marker < 0 or marker + 22 > len(tail):
            raise builder.BuildError("Completed Player APK has no final ZIP directory.")
        row = struct.unpack_from("<4s4H2IH", tail, marker)
        if marker + 22 + row[7] != len(tail) or row[1:3] != (0, 0):
            raise builder.BuildError("Completed Player APK ZIP directory is incomplete or spans disks.")
        count, directory_bytes, offset = row[4:7]
        if count == 65535 or directory_bytes == 0xFFFFFFFF or offset == 0xFFFFFFFF:
            eocd = max(0, size - 65557) + marker
            if eocd < 20:
                raise builder.BuildError("Completed Player APK ZIP64 locator is missing.")
            stream.seek(eocd - 20); locator = stream.read(20)
            if len(locator) != 20 or locator[:4] != b"PK\x06\x07":
                raise builder.BuildError("Completed Player APK ZIP64 locator is invalid.")
            _, disk, address, disks = struct.unpack("<4sIQI", locator)
            if disk != 0 or disks != 1 or not 0 <= address <= eocd - 56:
                raise builder.BuildError("Completed Player APK ZIP64 position is invalid.")
            stream.seek(address); raw = stream.read(56)
            if len(raw) != 56 or raw[:4] != b"PK\x06\x06":
                raise builder.BuildError("Completed Player APK ZIP64 record is invalid.")
            record = struct.unpack("<4sQ2H2I4Q", raw)
            if record[4:6] != (0, 0) or record[6] != record[7]:
                raise builder.BuildError("Completed Player APK ZIP64 disk scope is invalid.")
            count, directory_bytes, offset = record[7:10]
        if not 1 <= count <= MAX_MEMBERS or not 0 < directory_bytes <= MAX_DIRECTORY or offset + directory_bytes > size:
            raise builder.BuildError("Completed Player APK ZIP directory is excessive or invalid.")


def _member_json(builder, archive, names, name, maximum=MAX_JSON):
    rows = [row for row in names if row.filename == name]
    if len(rows) != 1 or rows[0].is_dir() or not 0 < rows[0].file_size <= maximum:
        raise builder.BuildError("Completed Player lacks one bounded APK identity: " + name)
    try:
        value = json.loads(archive.read(rows[0]))
    except (ValueError, UnicodeError, zipfile.BadZipFile) as exc:
        raise builder.BuildError("Completed Player APK identity is unreadable: " + name) from exc
    if not isinstance(value, dict):
        raise builder.BuildError("Completed Player APK identity is not an object: " + name)
    return value


def _apk_identity(builder, apk, inputs, metadata):
    _zip_directory(builder, apk)
    try:
        with zipfile.ZipFile(apk) as archive:
            rows = archive.infolist()
            names = [row.filename for row in rows]
            if len(rows) > MAX_MEMBERS or len(set(names)) != len(names):
                raise builder.BuildError("Completed Player APK has excessive or duplicate members.")
            manifest = _member_json(builder, archive, rows, "assets/Quest/input-manifest.json")
            if manifest != inputs:
                raise builder.BuildError("Completed Player APK differs from its frozen input manifest.")
            # Unity's first Player stores this profile in its Resources bank.
            # The host capability writer adds the separate signed JSON member.
            # The exact embedded input plus report profile-SHA binds the former;
            # validate the latter whenever it has already been added.
            if "assets/Quest/offline-profile.json" in names:
                profile = _member_json(builder, archive, rows, "assets/Quest/offline-profile.json", 65536)
                if profile != inputs["profile"]:
                    raise builder.BuildError("Completed Player APK differs from its frozen profile.")
            required = ("AndroidManifest.xml", "lib/arm64-v8a/libil2cpp.so", "lib/arm64-v8a/libunity.so",
                        "lib/arm64-v8a/libghvr_quest_passthrough.so", "lib/arm64-v8a/libUnityOpenXR.so",
                        "lib/arm64-v8a/libopenxr_loader.so")
            if any(name not in names for name in required) or not any(name.endswith("/global-metadata.dat") for name in names):
                raise builder.BuildError("Completed Player APK lacks its ARM64 IL2CPP/OpenXR payload.")
            metadata_names = [name for name in names if name.endswith("/global-metadata.dat")]
            if len(metadata_names) != 1:
                raise builder.BuildError("Completed Player APK has an ambiguous IL2CPP metadata payload.")
            with archive.open(metadata_names[0]) as stream:
                header = stream.read(8)
            if len(header) != 8 or struct.unpack_from("<I", header)[0] != 0xFAB11BAF:
                raise builder.BuildError("Completed Player APK has an invalid IL2CPP metadata header.")
            for name in names:
                if name.startswith("lib/") and name.endswith(".so"):
                    if not name.startswith("lib/arm64-v8a/"):
                        raise builder.BuildError("Completed Player APK has an unexpected native architecture.")
                    with archive.open(name) as stream:
                        header = stream.read(64)
                    if (len(header) != 64 or header[:7] != b"\x7fELF\x02\x01\x01"
                            or struct.unpack_from("<HHI", header, 16) != (3, 183, 1)):
                        raise builder.BuildError("Completed Player APK has a non-ARM64 native library.")
            installation = _member_json(builder, archive, rows, "assets/Quest/installation-manifest.json")
            delivery = _member_json(builder, archive, rows, "assets/Quest/content-delivery.json", 65536)
            native = _member_json(builder, archive, rows, "assets/Quest/procedural-native.json", 65536)
            if (installation.get("schema") != 1 or installation.get("inputKey") != inputs["inputKey"]
                    or delivery.get("schema") != 1 or delivery.get("inputKey") != inputs["inputKey"]
                    or delivery.get("package") != builder.PACKAGE
                    or native.get("backend") != inputs.get("proceduralBackend")
                    or metadata.get("proceduralBackend") != native.get("backend")
                    or metadata.get("stagedProceduralNativeFiles") != native.get("files")):
                raise builder.BuildError("Completed Player content/native installation identity differs.")
            files = delivery.get("files")
            if (not isinstance(files, list) or len(files) != 1 or not isinstance(files[0], dict)
                    or files[0].get("file") != "GloomhavenVR-Quest-content.zip"
                    or files[0].get("archive") != "quest-startup-content.zip"
                    or not _hash(files[0].get("sha256")) or type(files[0].get("size")) is not int
                    or files[0]["size"] < 22):
                raise builder.BuildError("Completed Player external-content identity is invalid.")
            bank = _ordinary(builder, apk.parent / files[0]["file"])
            game = installation.get("game")
            if (bank.stat().st_size != files[0]["size"] or not isinstance(game, dict)
                    or game.get("inputKey") != inputs["inputKey"] or game.get("archiveSha256") != files[0]["sha256"]):
                raise builder.BuildError("Completed Player external-content bank is missing or changed.")
            return {"memberCount": len(rows), "externalBankBytes": files[0]["size"],
                    "apkMetadataOnly": True, "finalValidationRequired": True}
    except (zipfile.BadZipFile, KeyError, EOFError, struct.error) as exc:
        raise builder.BuildError("Completed Player APK is incomplete or unreadable; its files were preserved.") from exc


def _original_witnesses(builder, output, data, inputs):
    """Read three indexed prior metadata proofs, never enumerate/hash original files."""
    rows = {row["path"]: row for row in inputs["game"]["files"] if row.get("path") in _ORIGINALS}
    if len(rows) != len(_ORIGINALS):
        raise builder.BuildError("Completed Player lacks its original game identity guards.")
    database = _ordinary(builder, output / "cache/input-inventories/game.sqlite3")
    storage = builder._local_helper("storage")
    owner = builder.value_hash({"schema": 1, "root": str(data),
                                "owner": {"schema": 1, "owner": "Quest original input inventory"}})
    with sqlite3.connect(database.as_uri() + "?mode=ro", uri=True, timeout=1) as connection:
        for relative in _ORIGINALS:
            path = _ordinary(builder, data / relative)
            observed = storage._file_witness_stamp(path, path.stat())
            prior = connection.execute("SELECT size,sha,stamp,check_hash FROM validated_file_witnesses WHERE owner=? AND path=?",
                                       (owner, relative)).fetchone()
            if prior is None or observed is None:
                raise builder.BuildError("Completed Player original-file metadata proof is unavailable: " + relative)
            stamp = json.loads(prior[2])
            expected = rows[relative]
            if (list(observed) != stamp or prior[:2] != (expected["size"], expected["sha256"])
                    or prior[3] != builder.value_hash([owner, relative, prior[0], prior[1], stamp])):
                raise builder.BuildError("Selected PC installation changed since this Player completed: " + relative)


def _context(builder, output, inputs, key, provenance, tools, source, project):
    runtime = {"sourceCommit": inputs["mod"]["commit"], "modBuild": inputs["mod"]["modBuild"], "inputKey": inputs["inputKey"]}
    if (provenance.get("schema") != 1 or provenance.get("runtime") != runtime
            or not isinstance(provenance.get("toolchain"), dict)
            or not {"unityVersion", "buildDriverSha256", "key"} <= provenance["toolchain"].keys()):
        raise builder.BuildError("Completed Player provenance differs from its frozen runtime.")
    for name, value in provenance.get("toolchain", {}).items():
        if tools.get(name) != value:
            raise builder.BuildError("Completed Player toolchain receipt changed: " + name)
    accepted = [builder.value_hash({"input": inputs["inputKey"], "toolchain": tools["key"], "recipe": builder.RECIPE,
                                   "buildProvenance": builder.value_hash(provenance), "validateCampaignShaders": audit})
                for audit in (False, True)]
    if key not in accepted:
        raise builder.BuildError("Completed Player build directory differs from its proven input/toolchain.")
    folder = _within(builder, output, "builds/" + key, directory=True)
    apk = _ordinary(builder, folder / "GloomhavenVR-Quest.apk")
    report = _ordinary(builder, folder / "GloomhavenVR-Quest.apk.build.json")
    metadata = _json(builder, report)
    identity_path = _within(builder, output, "identities/" + inputs["profileKey"] + "/quest-profile.json")
    if not 0 < identity_path.stat().st_size <= 65536:
        raise builder.BuildError("Completed Player frozen profile receipt exceeds its bounded size.")
    identity_bytes = identity_path.read_bytes()
    if len(identity_bytes) > 65536 or json.loads(identity_bytes) != inputs["profile"]:
        raise builder.BuildError("Completed Player frozen profile receipt changed.")
    expected = {"schema": 1, "target": "game", "inputKey": inputs["inputKey"], "package": builder.PACKAGE,
                "profileSha256": None, "unityVersion": tools["unityVersion"],
                "buildResult": "Succeeded", "graphicsApi": "Vulkan"}
    expected["profileSha256"] = hashlib.sha256(identity_bytes).hexdigest()
    if any(metadata.get(field) != value for field, value in expected.items()):
        raise builder.BuildError("Android Player report lacks matching successful Unity evidence.")
    if "buildProvenance" in metadata and metadata["buildProvenance"] != provenance:
        raise builder.BuildError("Completed Player report provenance changed.")
    proof = _apk_identity(builder, apk, inputs, metadata)
    return {"inputs": inputs, "inputKey": inputs["inputKey"], "modBuild": inputs["mod"]["modBuild"],
            "key": key, "apk": apk, "report": report, "provenance": provenance,
            "source": source, "project": project, "tools": tools, "proof": proof}


def record(builder, output, inputs, source, project, tools, key, provenance):
    """Publish a small independent checkpoint before any fallible host delivery."""
    output = _ordinary(builder, output, directory=True)
    source = _ordinary(builder, source, directory=True)
    project = _ordinary(builder, project, directory=True)
    context = _context(builder, output, inputs, key, provenance, tools, source, project)
    value = {"schema": 1, "scope": "completed-unity-player-awaiting-delivery", "inputKey": inputs["inputKey"],
             "key": key, "modBuild": inputs["mod"]["modBuild"], "source": source.relative_to(output).as_posix(),
             "project": project.relative_to(output).as_posix(), "proof": context["proof"],
             "finalValidationRequired": True, "hardwareVerified": False}
    builder.write_json(context["apk"].parent / CHECKPOINT, value)
    builder.write_json(output / POINTER, {"schema": 1, "inputKey": inputs["inputKey"], "key": key})
    return context


def record_completed(builder, inputs, output, source, project, tools, key, provenance):
    """Compatibility entry point for the host's successful-Unity callback."""
    return record(builder, output, inputs, source, project, tools, key, provenance)


def select(builder, output, game_root, *, profile_path=None, input_key=None):
    """Return a frozen delivery context, or None when Unity has not completed yet.

    A matching completed artifact with inconsistent identity raises an actionable
    error and is preserved. It must never silently become a cold rebuild.
    """
    output = Path(output).absolute()
    latest = output / "latest-input.json"
    if not latest.is_file():
        return None
    output = _ordinary(builder, output, directory=True)
    pointer = _json(builder, latest, 65536)
    manifest_path = _within(builder, output, pointer.get("manifest"))
    inputs = _json(builder, manifest_path)
    if inputs.get("target") != "game":
        return None
    if (not _hash(inputs.get("inputKey")) or not _hash(inputs.get("profileKey"))
            or not _hash(inputs.get("mod", {}).get("key")) or not _hash(inputs.get("game", {}).get("key"))
            or manifest_path != output / "manifests" / (inputs["inputKey"] + ".json")):
        raise builder.BuildError("Completed Player input pointer is inconsistent.")
    if (builder.value_hash({name: value for name, value in inputs.items() if name != "inputKey"}) != inputs["inputKey"]
            or builder.value_hash(inputs.get("profile")) != inputs["profileKey"]):
        raise builder.BuildError("Completed Player immutable input/profile identity changed.")
    if input_key is not None and input_key != inputs["inputKey"]:
        raise builder.BuildError("Completed Player selection differs from the requested retained input.")
    tools_path = output / "toolchain.json"
    if not tools_path.is_file():
        return None
    tools = _json(builder, tools_path, 65536)
    candidates = []
    checkpoint_pointer = output / POINTER
    if checkpoint_pointer.is_file():
        saved = _json(builder, checkpoint_pointer, 65536)
        if saved.get("schema") == 1 and saved.get("inputKey") == inputs["inputKey"] and _hash(saved.get("key")):
            candidates.append(saved["key"])
    if not candidates:
        folder = output / "builds"
        if not folder.is_dir():
            return None
        _ordinary(builder, folder, directory=True)
        with os.scandir(folder) as entries:
            for index, entry in enumerate(entries):
                if index >= MAX_BUILDS:
                    raise builder.BuildError("Completed Player selection exceeds its bounded build directory list.")
                if _hash(entry.name):
                    if entry.is_symlink():
                        raise builder.BuildError("Completed Player build directory must not be linked.")
                    if entry.is_dir(follow_symlinks=False):
                        candidates.append(entry.name)
        candidates.sort(reverse=True)
    for key in candidates:
        folder = _within(builder, output, "builds/" + key, directory=True)
        report_path = folder / "GloomhavenVR-Quest.apk.build.json"
        if not report_path.is_file() or not (folder / "GloomhavenVR-Quest.apk").is_file():
            continue
        metadata = _json(builder, report_path)
        if metadata.get("inputKey") != inputs["inputKey"] or metadata.get("buildResult") != "Succeeded":
            continue
        source = _within(builder, output, "inputs/mod/" + inputs["mod"]["key"], directory=True)
        project_key = builder.import_workspace.workspace_key(inputs, "game")
        project = _within(builder, output, "projects/" + project_key, directory=True)
        provenance = _json(builder, folder / "build-provenance.json")
        data = _ordinary(builder, builder.game_data(Path(game_root)), directory=True)
        if not isinstance(pointer.get("sourceGameRoot"), str) or os.path.normcase(str(data)) != os.path.normcase(str(Path(pointer["sourceGameRoot"]).absolute())):
            raise builder.BuildError("Select the same locally owned PC installation used for this completed Player.")
        if profile_path is not None:
            selected = _json(builder, profile_path, 65536)
            # The Wizard's profile file excludes the separately captured local
            # DLC inventory; all fields it does declare must match frozen input.
            if (not isinstance(selected.get("displayName"), str) or not selected["displayName"].strip()
                    or not any(selected.get(name) for name in ("steamId", "providerId"))
                    or selected.get("provider", "steam") != inputs["profile"].get("provider")
                    or any(name != "source" and inputs["profile"].get(name) != value for name, value in selected.items())):
                raise builder.BuildError("Selected account/profile differs from this completed Player; its frozen build was preserved.")
        try:
            _original_witnesses(builder, output, data, inputs)
        except sqlite3.Error as exc:
            raise builder.BuildError("Completed Player original metadata receipt is unreadable; preserve its build folder.") from exc
        context = _context(builder, output, inputs, key, provenance, tools, source, project)
        context["resumeKind"] = "checkpoint" if (folder / CHECKPOINT).is_file() else "legacy-completed-player"
        return context
    return None
