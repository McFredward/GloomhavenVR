#!/usr/bin/env python3
"""Recover exact original object -> exported GUID/fileID provenance.

AssetRipper's project GUIDs are random. Independent bundle exports therefore
cannot be combined by copying similarly named files. The pinned open-source
exporter is instrumented locally, and every remap is backed by its actual
serialized collection name and pathID. No game DLL/source is distributed.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import urllib.request

from recover import RecoveryError, safe_extract, sha256, write_json

HERE = Path(__file__).resolve().parent
REVISION = "1ac666f47d8e9dedf96afb0b914c70d7656151ea"
SOURCE_URL = "https://github.com/AssetRipper/AssetRipper/archive/1ac666f.tar.gz"
SOURCE_SHA256 = "2f1a9fb64ee7ac561c38227f55860a3333b32ba78294e9923ab947488acf8341"
POINTER = re.compile(r"\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32}),\s*type:\s*(\d+)\}")
GUID = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)


def patch_source(source):
    """Apply one audited source seam, failing if upstream layout has changed."""
    source = Path(source)
    path = source / "Source/AssetRipper.Export.UnityProjects/ExportCollection.cs"
    text = path.read_text(encoding="utf-8-sig")
    needle = '\t\tstring metaPath = $"{filePath}{MetaExtension}";'
    seam = '\t\tQuestExportIdentity.Record(container, meta.GUID.ToString(), filePath);\n'
    if seam not in text:
        if text.count(needle) != 1:
            raise RecoveryError("Pinned AssetRipper export metadata seam changed.")
        path.write_text(text.replace(needle, seam + needle), encoding="utf-8")
    target = path.parent / "QuestExportIdentity.cs"
    content = (HERE / "QuestExportIdentity.cs").read_bytes()
    if target.exists() and target.read_bytes() != content:
        raise RecoveryError("Local AssetRipper identity instrumentation was modified.")
    target.write_bytes(content)
    exporter = path.parent / "ProjectExporter.cs"
    exporter_text = exporter.read_text(encoding="utf-8-sig")
    original = "\t\t\tif (collection.Exportable)"
    replacement = "\t\t\tif (QuestExportIdentity.ShouldExport(container) && collection.Exportable)"
    if replacement not in exporter_text:
        if exporter_text.count(original) != 1:
            raise RecoveryError("Pinned AssetRipper collection export seam changed.")
        exporter.write_text(exporter_text.replace(original, replacement), encoding="utf-8")
    sprite = path.parent / "Textures/YamlSpriteExporter.cs"
    sprite_text = sprite.read_text(encoding="utf-8-sig")
    sprite_seam = "\t\tQuestExportIdentity.CaptureNativeRecipe(asset);\n"
    sprite_original = "\t\texportCollection = asset switch"
    if sprite_seam not in sprite_text:
        if sprite_text.count(sprite_original) != 1:
            raise RecoveryError("Pinned AssetRipper native atlas seam changed.")
        sprite.write_text(sprite_text.replace(sprite_original, sprite_seam + sprite_original), encoding="utf-8")
    return {"sourceRevision": REVISION, "sourceArchiveSha256": SOURCE_SHA256,
            "instrumentationSha256": hashlib.sha256(content).hexdigest(),
            "patchedExportCollectionSha256": sha256(path), "patchedProjectExporterSha256": sha256(exporter),
            "patchedNativeAtlasExporterSha256": sha256(sprite)}


def acquire_source(cache):
    cache = Path(cache).resolve()
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / "assetripper-source-1ac666f.tar.gz"
    if not archive.exists():
        partial = archive.with_suffix(".download")
        urllib.request.urlretrieve(SOURCE_URL, partial)
        if sha256(partial) != SOURCE_SHA256:
            partial.unlink()
            raise RecoveryError("Pinned open-source AssetRipper archive hash differs.")
        partial.replace(archive)
    if sha256(archive) != SOURCE_SHA256:
        raise RecoveryError("Cached AssetRipper source archive changed.")
    source = cache / ("AssetRipper-" + REVISION)
    if not source.exists():
        safe_extract(archive, cache)
    return source


def build_tool(cache, dotnet):
    """Compile a private framework-dependent host with the .NET 10 SDK.

    The original precompiled Free host remains unmodified. Unlike its NativeAOT
    executable, the local host includes the above source instrumentation. Tool
    warnings are retained in the receipt; they are not mod build warnings.
    """
    # Instrumentation evolves independently of the mod and upstream archive.
    # Preserve older witnessed tool builds; never patch them in place.
    cache = Path(cache).resolve()
    generation = hashlib.sha256((HERE / "QuestExportIdentity.cs").read_bytes()).hexdigest()[:16]
    current = cache / ("identity-" + generation)
    current.mkdir(parents=True, exist_ok=True)
    pinned_archive = cache / "assetripper-source-1ac666f.tar.gz"
    if pinned_archive.is_file() and not (current / pinned_archive.name).exists():
        if sha256(pinned_archive) != SOURCE_SHA256:
            raise RecoveryError("Cached AssetRipper source archive changed.")
        shutil.copyfile(pinned_archive, current / pinned_archive.name)
    source = acquire_source(current)
    proof = patch_source(source)
    project = source / "Source/AssetRipper.GUI.Free/AssetRipper.GUI.Free.csproj"
    output = source / "Source/0Bins/AssetRipper.GUI.Free/Release"
    dll = output / "AssetRipper.GUI.Free.dll"
    receipt = current / "quest-identity-tool.json"
    previous = json.loads(receipt.read_text()) if receipt.exists() else None
    if previous and previous.get("source") == proof and previous.get("files"):
        for row in previous["files"]:
            path = output / row["path"]
            if not path.is_file() or sha256(path) != row["sha256"]:
                raise RecoveryError("Identity exporter executable/dependency cache changed: " + row["path"])
    else:
        version = subprocess.run([str(dotnet), "--version"], capture_output=True, text=True, check=True).stdout.strip()
        if not version.startswith("10."):
            raise RecoveryError("Pinned AssetRipper source requires a .NET 10 SDK; no runtime substitution is made.")
        log = current / "quest-identity-tool-build.log"
        with log.open("w", encoding="utf-8") as stream:
            result = subprocess.run([str(dotnet), "build", str(project), "-c", "Release",
                                     "-p:PublishAot=false", "-p:IsAotCompatible=false", "--nologo", "-v", "quiet"],
                                    stdout=stream, stderr=subprocess.STDOUT)
        if result.returncode or not dll.is_file():
            raise RecoveryError("Instrumented AssetRipper build failed; see " + str(log))
        rows = [{"path": p.relative_to(output).as_posix(), "sha256": sha256(p), "bytes": p.stat().st_size}
                for p in sorted(output.rglob("*")) if p.is_file()]
        write_json(receipt, {"schema": 1, "source": proof, "sdkVersion": version, "files": rows,
                             "buildLog": str(log), "originalPrecompiledToolModified": False})
    return [str(dotnet), str(dll)], json.loads(receipt.read_text())


def read_identities(path, exported_project):
    root = Path(exported_project).resolve()
    rows = []
    for line in Path(path).read_text().splitlines():
        value = json.loads(line)
        if value.get("skippedCore"):
            if value.get("path") != "" or not value.get("objects"):
                raise RecoveryError("Invalid skipped-core identity evidence.")
            rows.append(value)
            continue
        exported = Path(value["path"]).resolve()
        if root not in exported.parents or not exported.is_file():
            raise RecoveryError("Export identity points outside actual project: " + str(exported))
        metadata = exported.with_name(exported.name + ".meta")
        match = GUID.search(metadata.read_text())
        if match is None or match[1] != value["guid"]:
            raise RecoveryError("Export identity GUID differs from actual metadata: " + str(exported))
        value["path"] = exported.relative_to(root).as_posix()
        if not re.fullmatch("[0-9a-f]{32}", value["guid"]):
            raise RecoveryError("Exporter identity contains a malformed GUID.")
        rows.append(value)
    if not rows:
        raise RecoveryError("Exporter returned no original-object identity evidence.")
    return rows


def object_index(rows):
    result = {}
    for row in rows:
        for obj in row["objects"]:
            key = (obj["collection"], int(obj["pathId"]))
            if key in result:
                previous = result[key]
                if (previous["guid"], previous["fileId"]) != (row["guid"], obj["fileId"]):
                    raise RecoveryError("Original object was exported with two identities: " + repr(key))
                continue
            result[key] = {**obj, "guid": row["guid"], "path": row["path"]}
    return result


def identity_remaps(incoming_rows, canonical_objects):
    """Map complete actual exported collections through shared original objects.

    A filename, display name or matching material property never counts as an
    identity. Every collision must agree on all shared object pointers.
    """
    remaps, duplicate_paths = {}, {}
    incoming = object_index(incoming_rows)
    for row in incoming_rows:
        known = [(obj, canonical_objects[(obj["collection"], int(obj["pathId"]))])
                 for obj in row["objects"] if (obj["collection"], int(obj["pathId"])) in canonical_objects]
        if not known:
            continue
        targets = {(target["guid"], target["path"]) for _, target in known}
        if len(targets) != 1:
            raise RecoveryError("Export collection crosses distinct canonical assets: " + row["path"])
        guid, path = next(iter(targets))
        duplicate_paths[row["path"]] = path
        for obj, target in known:
            remaps[(row["guid"], int(obj["fileId"]))] = (guid, int(target["fileId"]))
        # Exported single-object assets conventionally use a main fileID, and
        # compound assets may add importer-only local IDs. Only observed IDs
        # are remapped here; unseen compound references fail at merge closure.
    return remaps, duplicate_paths, incoming


def remap_yaml(text, pointers):
    def replace(match):
        key = (match[2], int(match[1]))
        target = pointers.get(key)
        if target is None:
            return match[0]
        return "{fileID: " + str(target[1]) + ", guid: " + target[0] + ", type: " + match[3] + "}"
    return POINTER.sub(replace, text)
