#!/usr/bin/env python3
"""Restore audited legacy compute SOURCE in a generated startup project only."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import sys
import urllib.request

COMMIT = "933df236f509ed64ae5763ed57af33f2342cd1c2"
UPSTREAM = "https://raw.githubusercontent.com/Unity-Technologies/PostProcessing/" + COMMIT + "/"
SHADERS = "PostProcessing/Resources/Shaders/"
FILES = {
    SHADERS + "EyeHistogram.compute": "b78ca95c693107cc9a281a87d7519e9bb40c40d944ac579042cf8a0233ad8a02",
    SHADERS + "Common.cginc": "433a8b934ee9ee0cb37fb800bf15076c355c15df98957a0be54b1c7751a8e324",
    SHADERS + "EyeAdaptation.cginc": "b73daa93a9bc8fdc3b6444378081dd79dc33c36a6b35c20bec35c785a9251748",
    "LICENSE": "855825c368e58714d1b66ca4735525e58249e154c8178818be211463721bb855",
}
ORIGINAL = "Assets/Resources/shaders/EyeHistogram.asset"
TARGET = "Assets/Resources/shaders/EyeHistogram.compute"
ORIGINAL_SHA256 = "18348db1beec6ab43f07ded89f3dcc9815d196d8d87d6527ba6365afd04bd0de"
ORIGINAL_GUID = "14b830dd8a5381e4399cfe161a01662f"
RUNTIME = "Assets/Plugins/GH.Runtime.FirstPass.dll"
# This owned assembly was decompiled/audited: EyeAdaptationComponent creates a
# 64*4-byte buffer and dispatches the named kernel with ceil(width/16,height/16).
RUNTIME_SHA256 = "32938372ce7a964c176e488d6a6c32de8f8a11ed878a7fa6e8090dc399d7ba40"
RECEIPT = "QuestStartupEvidence/compute-source-restoration.json"
BACKUP = "QuestStartupEvidence/OriginalComputeShader/EyeHistogram.asset"
BINDINGS = "Assets/QuestOriginalStartup/script-bindings.json"
INCLUDE = re.compile(r'^\s*#include\s+["<]([^">]+)[">]', re.M)
GUID = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)


class ComputeSourceError(RuntimeError):
    pass


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def require_hash(path, expected):
    if not path.is_file() or path.is_symlink() or sha(path) != expected:
        raise ComputeSourceError("Pinned source/provenance hash mismatch: " + str(path))


def acquire(cache):
    cache = Path(cache).resolve() / ("postprocessing-v1-" + COMMIT)
    for relative, expected in FILES.items():
        destination = cache / relative
        if destination.exists():
            require_hash(destination, expected)
            continue
        data = urllib.request.urlopen(UPSTREAM + relative, timeout=60).read()
        if hashlib.sha256(data).hexdigest() != expected:
            raise ComputeSourceError("Official source download hash mismatch: " + relative)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
    validate_sources(cache)
    return cache


def validate_sources(cache):
    for path, expected in FILES.items():
        require_hash(cache / path, expected)
    known = {Path(path).name for path in FILES}
    used_builtins = set()
    for relative in FILES:
        for include in INCLUDE.findall((cache / relative).read_text()):
            if include == "UnityCG.cginc":
                used_builtins.add(include)
            elif include not in known or Path(include).name != include:
                raise ComputeSourceError("Unpinned source include: " + include)
    if used_builtins != {"UnityCG.cginc"}:
        raise ComputeSourceError("Unexpected Unity compiler include boundary")
    source = (cache / (SHADERS + "EyeHistogram.compute")).read_text()
    adaptation = (cache / (SHADERS + "EyeAdaptation.cginc")).read_text()
    for pattern in [r"RWStructuredBuffer<uint>\s+_Histogram\s*;", r"Texture2D<float4>\s+_Source\s*;",
                    r"CBUFFER_START\(Params\)\s+float4\s+_ScaleOffsetRes\s*;",
                    r"#pragma kernel KEyeHistogram\s", r"numthreads\(HISTOGRAM_THREAD_X,HISTOGRAM_THREAD_Y,1\)"]:
        if not re.search(pattern, source):
            raise ComputeSourceError("Official compute ABI mismatch: " + pattern)
    for name, value in [("HISTOGRAM_BINS", 64), ("HISTOGRAM_THREAD_X", 16), ("HISTOGRAM_THREAD_Y", 16)]:
        if not re.search(r"#define\s+" + name + r"\s+" + str(value) + r"\b", adaptation):
            raise ComputeSourceError("Official histogram dimension mismatch: " + name)
    license_text = (cache / "LICENSE").read_text()
    if "The MIT License (MIT)" not in license_text or "Unity Technologies" not in license_text:
        raise ComputeSourceError("Official source license is not the audited MIT license")


def validate_original(text):
    if not re.search(r"^--- !u!72 &7200000$", text, re.M) or "  m_Name: EyeHistogram\n" not in text:
        raise ComputeSourceError("Original object is not the audited EyeHistogram ComputeShader")
    if re.findall(r"^      name: (\S+)$", text, re.M) != ["KEyeHistogram"]:
        raise ComputeSourceError("Original compute kernel ABI differs")
    groups = re.findall(r"threadGroupSize: ([0-9a-f]+)", text)
    if len(groups) != 1 or len(groups[0]) != 24 or struct.unpack("<3I", bytes.fromhex(groups[0])) != (16, 16, 1):
        raise ComputeSourceError("Original compute thread group ABI differs")
    for name in ["_Source", "_Histogram"]:
        if not re.search(r"^          - name: " + name + r"$", text, re.M):
            raise ComputeSourceError("Original compute binding missing: " + name)
    if not re.search(r"constantBuffers:\s+- name: Params\s+byteSize: 16\s+params:\s+- name: _ScaleOffsetRes"
                     r"\s+type: 0\s+offset: 0\s+arraySize: 0\s+rowCount: 1\s+colCount: 4", text):
        raise ComputeSourceError("Original Params/float4 layout differs")


def native_compute_paths(project):
    found = []
    for path in sorted((project / "Assets").rglob("*")):
        if path.is_symlink():
            raise ComputeSourceError("Symlinked generated asset: " + str(path))
        if path.is_file() and path.suffix.lower() == ".asset":
            with path.open("rb") as stream:
                header = stream.read(512)
            if re.search(rb"^--- !u!72 ", header, re.M):
                found.append(path.relative_to(project).as_posix())
        if path.is_file() and path.suffix.lower() == ".compute" and path.relative_to(project).as_posix() != TARGET:
            raise ComputeSourceError("Unknown compute source requires explicit audit: " + str(path))
    return found


def restore(project, cache):
    project = Path(project).resolve()
    if not (project / "Assets/Quest").is_dir() or (project / "Assets").is_symlink():
        raise ComputeSourceError("Refusing raw recovery project; generated Quest template required")
    if (project / "QuestStartupEvidence").is_symlink():
        raise ComputeSourceError("Generated evidence directory must not be symlinked")
    recovered = native_compute_paths(project)
    if any(path != ORIGINAL for path in recovered):
        raise ComputeSourceError("Unknown recovered ComputeShader requires explicit audit: " + str(recovered))
    require_hash(project / RUNTIME, RUNTIME_SHA256)
    source_root = acquire(cache)  # All network/source/license validation precedes project writes.
    outputs = {"Assets/Resources/shaders/" + Path(path).name: expected
               for path, expected in FILES.items() if path != "LICENSE"}
    outputs["QuestStartupEvidence/ComputeSourceLicense/LICENSE"] = FILES["LICENSE"]
    receipt_path = project / RECEIPT
    if receipt_path.exists():
        receipt = json.loads(receipt_path.read_text())
        expected = {"schema": 1, "target": "startup", "upstreamCommit": COMMIT,
                    "guid": ORIGINAL_GUID, "assetPath": TARGET, "originalPath": ORIGINAL,
                    "originalSha256": ORIGINAL_SHA256, "ownedRuntimeSha256": RUNTIME_SHA256,
                    "sourceFiles": outputs, "resourcesKey": "Shaders/EyeHistogram", "localFileId": 7200000,
                    "fullGameReady": False, "originalPixelParityVerified": False}
        if any(receipt.get(key) != value for key, value in expected.items()) or recovered:
            raise ComputeSourceError("Invalid previous compute restoration receipt")
        require_hash(project / BACKUP, ORIGINAL_SHA256)
        require_hash(project / (BACKUP + ".meta"), receipt["originalMetaSha256"])
        for path, expected in receipt["sourceFiles"].items():
            require_hash(project / path, expected)
        metadata = (project / (TARGET + ".meta")).read_text()
        match = GUID.search(metadata)
        if not match or match[1] != ORIGINAL_GUID or "ComputeShaderImporter:" not in metadata:
            raise ComputeSourceError("Restored compute importer/GUID changed")
        require_hash(project / (TARGET + ".meta"), receipt["restoredMetaSha256"])
        if (project / (ORIGINAL + ".meta")).exists():
            raise ComputeSourceError("Original compute importer remains inside Assets")
        require_hash(project / BINDINGS, receipt["bindingsSha256"])
        return receipt
    if recovered != [ORIGINAL]:
        raise ComputeSourceError("Expected the exact recovered EyeHistogram object")
    require_hash(project / ORIGINAL, ORIGINAL_SHA256)
    validate_original((project / ORIGINAL).read_text())
    metadata_path = project / (ORIGINAL + ".meta")
    metadata = metadata_path.read_text()
    match = GUID.search(metadata)
    if not match or match[1] != ORIGINAL_GUID or "NativeFormatImporter:" not in metadata or "mainObjectFileID: 7200000" not in metadata:
        raise ComputeSourceError("Original compute metadata identity differs")
    guid = match[1]
    # Resources is the only proven runtime association, not an AA location.
    for path in (project / "Assets").rglob("*"):
        if path.is_file() and path.suffix in (".unity", ".prefab", ".asset", ".mat", ".controller") and path != project / ORIGINAL:
            if guid.encode() in path.read_bytes():
                raise ComputeSourceError("Compute GUID has a serialized dependency requiring explicit audit: " + str(path))
    catalog_path = project / "Assets/QuestOriginalStartup/startup-addressables.json"
    if catalog_path.exists():
        for row in json.loads(catalog_path.read_text())["entries"]:
            if row.get("recoveredGuid") == guid or row.get("assetPath") == ORIGINAL:
                raise ComputeSourceError("Compute catalog dependency requires explicit audit")
    bindings_path = project / BINDINGS
    bindings_before = bindings_path.read_bytes()
    bindings = json.loads(bindings_before)
    if bindings.get("schema") != 1 or not isinstance(bindings.get("assetPaths"), list):
        raise ComputeSourceError("Unknown script bindings sidecar")
    bindings_changed = ORIGINAL in bindings["assetPaths"]
    bindings["assetPaths"] = [TARGET if path == ORIGINAL else path for path in bindings["assetPaths"]]
    for path in outputs:
        if (project / path).exists() or (project / (path + ".meta")).exists():
            raise ComputeSourceError("Restored source destination is occupied: " + path)
    if (project / BACKUP).exists() or (project / (BACKUP + ".meta")).exists():
        raise ComputeSourceError("Original compute backup destination is occupied")
    new_metadata = ("fileFormatVersion: 2\nguid: " + guid + "\nComputeShaderImporter:\n"
                    "  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    written = []
    moved = []
    try:
        for old, new in [(ORIGINAL, BACKUP), (ORIGINAL + ".meta", BACKUP + ".meta")]:
            (project / new).parent.mkdir(parents=True, exist_ok=True)
            (project / old).rename(project / new)
            moved.append((old, new))
        for relative in FILES:
            output = ("QuestStartupEvidence/ComputeSourceLicense/LICENSE" if relative == "LICENSE" else
                      "Assets/Resources/shaders/" + Path(relative).name)
            (project / output).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source_root / relative, project / output)
            written.append(project / output)
        (project / (TARGET + ".meta")).write_text(new_metadata)
        written.append(project / (TARGET + ".meta"))
        if bindings_changed:
            bindings_path.write_text(json.dumps(bindings, indent=2, sort_keys=True) + "\n")
        receipt = {"schema": 1, "target": "startup", "upstreamCommit": COMMIT, "license": "MIT",
                   "guid": guid, "localFileId": 7200000, "originalPath": ORIGINAL, "assetPath": TARGET,
                   "resourcesKey": "Shaders/EyeHistogram", "originalSha256": ORIGINAL_SHA256,
                   "originalMetaSha256": sha(project / (BACKUP + ".meta")),
                   "restoredMetaSha256": sha(project / (TARGET + ".meta")),
                   "ownedRuntimeSha256": RUNTIME_SHA256, "sourceFiles": outputs,
                   "sourceIncludeClosure": ["Common.cginc", "EyeAdaptation.cginc"],
                   "unityBuiltinIncludeBoundary": ["UnityCG.cginc"],
                   "abi": {"usedKernel": "KEyeHistogram", "threadGroup": [16, 16, 1], "histogramBins": 64,
                           "histogramStride": 4, "paramsBytes": 16, "float4": "_ScaleOffsetRes",
                           "bindings": ["_Source", "_Histogram"]},
                   "additiveUnusedSourceKernels": ["KEyeHistogramClear"],
                   "bindingsBeforeSha256": hashlib.sha256(bindings_before).hexdigest(),
                   "bindingsSha256": sha(bindings_path), "originalPixelParityVerified": False,
                   "androidShaderCompiled": False, "fullGameReady": False}
        receipt_path.parent.mkdir(parents=True, exist_ok=True)
        receipt_path.write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n")
        return receipt
    except BaseException:
        receipt_path.unlink(missing_ok=True)
        for path in reversed(written):
            path.unlink(missing_ok=True)
        bindings_path.write_bytes(bindings_before)
        for old, new in reversed(moved):
            (project / new).rename(project / old)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--cache", required=True, type=Path)
    args = parser.parse_args()
    try:
        report = restore(args.project, args.cache)
    except (ComputeSourceError, OSError, ValueError, KeyError, TypeError, struct.error) as error:
        print("Compute source restoration rejected: " + str(error), file=sys.stderr)
        return 2
    print(json.dumps({"assetPath": report["assetPath"], "guid": report["guid"],
                      "receipt": str(args.project / RECEIPT), "fullGameReady": False}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
