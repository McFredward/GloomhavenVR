"""Recover three original legacy image effects from a pinned Unity package.

Only generated Quest projects are modified. The official installer is read as
an archive, never executed; its sources stay in the user's private build cache.
No legacy Unity source is distributed with this repository.
"""
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import tarfile
import urllib.request
import uuid
import xml.etree.ElementTree as ET
import zlib

from storage import BuildError, write_json

VERSION = "5.3.5f1"
CHANGESET = "960ebf59018a"
SOURCE_URL = ("https://download.unity3d.com/download_unity/" + CHANGESET +
              "/MacStandardAssetsInstaller/StandardAssets.pkg")
SOURCE_BYTES = 190062868
SOURCE_SHA256 = "302ee984abf9c55134a0fa83ac1c43939f62720bf7dc3fc681a21f3cbb42ae96"
PACKAGE_BYTES = 2765985
PACKAGE_SHA256 = "9ff6288ad3f1fb8d9289b7108c4e9881924dabb05409288e2cba152ebdf172b1"
CACHE_NAME = "unity-standard-assets-" + VERSION
RECEIPT = "QuestStartupEvidence/legacy-post-effects.json"
PACKAGE_PREFIX = "Assets/Standard Assets/Effects/ImageEffects/Shaders/_BloomAndFlares/"
GUID = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)
CHUNK = 1024 * 1024
# These identities come from the original game's recovered shader objects. The
# canonical recipe hashes bind this review to the original compiled pass banks;
# they do not assert pixel parity on an Android GPU.
SHADERS = {
    "BlendForBloom": {
        "sourceSha256": "84f4f797c3f77fca2797806b388d50bdbac7492118212ded78370a6d9d636ae5",
        "sourceBytes": 4770,
        "importUpgradeSha256": "d22461e93e8d3bd6801fe12a8ea8a12632d870fd54afc4d34dca839337838abf", "objectToClipPosReplacements": 2,
        "dummySha256": "0d91348b5dd5f9fb8c34ba658b1c1a6e7e85aef2f379e2af212b2a6c8aeee7c1",
        "guid": "30881e480b10c1b46a3d99ec13496f5e",
        "recipeSha256": "263f263605bc9ce88c366f33e895b696b32faf4aa812979f41e508e23fe48653",
        "originalPathId": 135, "properties": ["_MainTex", "_ColorBuffer"],
        "fragments": ["fragScreen", "fragAdd", "fragMultiTapMax", "fragVignetteMul",
                      "fragScreenCheap", "fragAddCheap", "fragMultiTapBlur",
                      "fragVignetteBlend", "fragClear", "fragAddOneOne", "frag1Tap"],
        "uniforms": ["_Intensity", "_ColorBuffer"],
    },
    "BrightPassFilter2": {
        "sourceSha256": "26e81ea437fbb5ffb45cb8fc4556bdb2ab6c1c50d5396af5c2c8bc1562b8e424",
        "sourceBytes": 1063,
        "importUpgradeSha256": "8a19269566f8fd692a44eb607c44f114abbc0a555d11c06995de22e2112e8dc1", "objectToClipPosReplacements": 1,
        "dummySha256": "a367285593319cbab3e51ecae6578e3594e36d7cc05bbb75ca784bafe68365a3",
        "guid": "93f40d5ea0c0a7945a5782e2dcd23833",
        "recipeSha256": "feba6bb83a31a7dc0f7388eb0aa7c9af897f76f1c17b1b5750f2e1710ebe854a",
        "originalPathId": 132, "properties": ["_MainTex"],
        "fragments": ["fragScalarThresh", "fragColorThresh"], "uniforms": ["_Threshhold"],
    },
    "BlurAndFlares": {
        "sourceSha256": "63283e8f5e60b6a7c2771b175c1c82fe62813648306ccf185c56379f75b196eb",
        "sourceBytes": 5171,
        "importUpgradeSha256": "343d875aed4f5f221ce7d5e33df24ffc90459d9533448d7ba9edca80fa40d390", "objectToClipPosReplacements": 4,
        "dummySha256": "d7cb8a5b5f34cf885f827ed8e6161d94e71c44f643fe0e57863751df69d616e6",
        "guid": "29d4384c2ae952c4597a9d894d381163",
        "recipeSha256": "4c6e89d84f8d16d060162186390ead698e0e528c805dfd1e4de75359a457cb17",
        "originalPathId": 137, "properties": ["_MainTex", "_NonBlurredTex"],
        "fragments": ["fragPostNoBlur", "fragStretch", "fragPreAndCut", "fragPost", "fragGaussBlur"],
        "uniforms": ["_Offsets", "_Threshhold", "_TintColor", "_Saturation", "_StretchWidth"],
    },
}


def _sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(CHUNK):
            digest.update(chunk)
    return digest.hexdigest()


def _safe_path(path):
    path = Path(os.path.abspath(path))
    if any(parent.is_symlink() for parent in (path, *path.parents)):
        raise BuildError("Symlinked shader recovery path: " + str(path))
    return path


def _require(path, expected, size=None):
    _safe_path(path)
    if not path.is_file() or (size is not None and path.stat().st_size != size) or _sha(path) != expected:
        raise BuildError("Pinned legacy shader source/provenance mismatch: " + str(path))


def _read_exact(stream, count):
    result = stream.read(count)
    if len(result) != count:
        raise BuildError("Truncated official Standard Assets archive")
    return result


class _Slice:
    """Limit a gzip reader to the selected XAR member without buffering it."""
    def __init__(self, stream, length):
        self.stream, self.remaining = stream, length

    def read(self, count=-1):
        count = self.remaining if count < 0 else min(count, self.remaining)
        chunk = self.stream.read(count)
        self.remaining -= len(chunk)
        return chunk


def _extract_effects(installer, destination):
    """Read only Payload/gzip/odc-cpio; never run or extract installer scripts."""
    with installer.open("rb") as stream:
        magic, header, version, compressed, expanded, algorithm = struct.unpack(">IHHQQI", _read_exact(stream, 28))
        if (magic, header, version, compressed, expanded, algorithm) != (0x78617221, 28, 1, 4740, 16196, 1):
            raise BuildError("Official Standard Assets XAR header differs")
        decoder = zlib.decompressobj()
        toc = decoder.decompress(_read_exact(stream, compressed), expanded + 1)
        if len(toc) != expanded or not decoder.eof or decoder.unused_data or b"<!DOCTYPE" in toc:
            raise BuildError("Invalid official Standard Assets XAR table")
        root = ET.fromstring(toc)
        payloads = [item for item in root.findall("./toc/file")
                    if item.findtext("name") == "StandardAssets.pkg.tmp"]
        candidates = [item for group in payloads for item in group.findall("file")
                      if item.findtext("name") == "Payload" and item.findtext("type") == "file"]
        if len(candidates) != 1:
            raise BuildError("Expected exactly one official Standard Assets Payload")
        data = candidates[0].find("data")
        encoding = data.find("encoding") if data is not None else None
        if data is None or encoding is None or (data.findtext("offset"), data.findtext("length"), data.findtext("size"),
                            encoding.get("style")) != (
                                "10592", "189993086", "189993086", "application/octet-stream"):
            raise BuildError("Official Standard Assets Payload bounds differ")
        stream.seek(header + compressed + 10592)
        found, uncompressed = 0, 0
        with gzip.GzipFile(fileobj=_Slice(stream, 189993086)) as payload:
            for _ in range(64):
                entry = _read_exact(payload, 76)
                if entry[:6] != b"070707" or not re.fullmatch(rb"[0-7]{70}", entry[6:]):
                    raise BuildError("Unexpected official Standard Assets cpio format")
                mode, links = int(entry[18:24], 8), int(entry[36:42], 8)
                name_size, size = int(entry[59:65], 8), int(entry[65:76], 8)
                if not 1 <= name_size <= 1024 or size > 256 * CHUNK:
                    raise BuildError("Official Standard Assets cpio bounds differ")
                raw_name = _read_exact(payload, name_size)
                if not raw_name.endswith(b"\0") or b"\0" in raw_name[:-1]:
                    raise BuildError("Invalid official Standard Assets cpio filename")
                name = raw_name[:-1].decode("utf-8")
                if name == "TRAILER!!!":
                    padding = payload.read(512)
                    if size != 0 or found != 1 or len(padding) > 511 or padding.strip(b"\0") or payload.read(1):
                        raise BuildError("Invalid official Standard Assets cpio completion")
                    break
                uncompressed += size
                if uncompressed > 256 * CHUNK:
                    raise BuildError("Official Standard Assets payload exceeds reviewed bounds")
                selected = name == "./Effects.unitypackage"
                if selected:
                    found += 1
                    if found != 1 or size != PACKAGE_BYTES or mode & 0o170000 != 0o100000 or links != 1:
                        raise BuildError("Invalid/duplicate official Effects package member")
                output = destination.open("xb") if selected else None
                try:
                    remaining = size
                    while remaining:
                        chunk = _read_exact(payload, min(CHUNK, remaining))
                        if output:
                            output.write(chunk)
                        remaining -= len(chunk)
                finally:
                    if output:
                        output.close()
            else:
                raise BuildError("Official Standard Assets payload has too many entries")
    _require(destination, PACKAGE_SHA256, PACKAGE_BYTES)


def _extract_sources(package):
    wanted = {PACKAGE_PREFIX + name + ".shader": name for name in SHADERS}
    result = {}
    with tarfile.open(package, "r:gz") as archive:
        members = archive.getmembers()
        if len(members) > 4096 or sum(item.size for item in members) > 32 * CHUNK:
            raise BuildError("Official Effects package exceeds reviewed bounds")
        names = [item.name for item in members]
        if len(names) != len(set(names)) or any(not item.isfile() and not item.isdir() for item in members):
            raise BuildError("Duplicate or nonregular official Effects package member")
        for member in members:
            if not member.name.endswith("/pathname"):
                continue
            if not member.isfile() or member.size > 1024:
                raise BuildError("Invalid official Effects pathname")
            pathname = archive.extractfile(member).read().decode("utf-8").strip()
            if pathname not in wanted:
                continue
            name = wanted[pathname]
            if name in result:
                raise BuildError("Duplicate official shader source: " + name)
            source = archive.getmember(member.name[:-len("pathname")] + "asset")
            spec = SHADERS[name]
            if not source.isfile() or source.size != spec["sourceBytes"]:
                raise BuildError("Official shader source length differs: " + name)
            content = archive.extractfile(source).read()
            if hashlib.sha256(content).hexdigest() != spec["sourceSha256"]:
                raise BuildError("Official shader source hash differs: " + name)
            result[name] = content
    if set(result) != set(SHADERS):
        raise BuildError("Missing official legacy image-effect shader source")
    return result


def _validate_source(name, content):
    spec = SHADERS[name]
    if len(content) != spec["sourceBytes"] or hashlib.sha256(content).hexdigest() != spec["sourceSha256"]:
        raise BuildError("Pinned official shader source changed: " + name)
    text = content.decode("utf-8")
    code = re.sub(r"//[^\n]*|/\*.*?\*/", "", text, flags=re.S)
    if re.findall(r'\bShader\s+"([^"]+)"', code) != ["Hidden/" + name]:
        raise BuildError("Official shader name differs: " + name)
    if re.findall(r"#pragma\s+fragment\s+(\w+)", code) != spec["fragments"]:
        raise BuildError("Official shader pass order differs: " + name)
    if len(re.findall(r"\bPass\s*{", code)) != len(spec["fragments"]):
        raise BuildError("Official shader pass count differs: " + name)
    if re.findall(r'#include\s+["<]([^">]+)[">]', code) != ["UnityCG.cginc"]:
        raise BuildError("Unaudited official shader include boundary: " + name)
    for uniform in spec["uniforms"]:
        if not re.search(r"\b" + re.escape(uniform) + r"\b", code):
            raise BuildError("Original Bloom shader uniform missing: " + uniform)


def acquire(cache):
    """Acquire private sources; an already verified small cache works offline."""
    cache = _safe_path(cache) / CACHE_NAME
    paths = {name: _safe_path(cache / (name + ".shader")) for name in SHADERS}
    if all(path.exists() for path in paths.values()):
        for name, path in paths.items():
            _require(path, SHADERS[name]["sourceSha256"], SHADERS[name]["sourceBytes"])
            _validate_source(name, path.read_bytes())
        return paths
    # Never silently repair a cache whose existing source differs from its pin.
    for name, path in paths.items():
        if path.exists():
            _require(path, SHADERS[name]["sourceSha256"], SHADERS[name]["sourceBytes"])
    cache.mkdir(parents=True, exist_ok=True)
    installer = cache / "StandardAssets.pkg"
    if installer.exists():
        _require(installer, SOURCE_SHA256, SOURCE_BYTES)
    else:
        temporary = cache / ("StandardAssets.pkg." + uuid.uuid4().hex + ".download")
        try:
            digest, count = hashlib.sha256(), 0
            with urllib.request.urlopen(SOURCE_URL, timeout=60) as response, temporary.open("xb") as output:
                while chunk := response.read(CHUNK):
                    count += len(chunk)
                    if count > SOURCE_BYTES:
                        raise BuildError("Official Standard Assets download exceeds pinned size")
                    digest.update(chunk)
                    output.write(chunk)
            if count != SOURCE_BYTES or digest.hexdigest() != SOURCE_SHA256:
                raise BuildError("Official Standard Assets download differs from pinned SHA-256")
            temporary.replace(installer)
        finally:
            temporary.unlink(missing_ok=True)
    package = cache / ("Effects." + uuid.uuid4().hex + ".unitypackage")
    try:
        _extract_effects(installer, package)
        sources = _extract_sources(package)
        for name, content in sources.items():
            _validate_source(name, content)
        for name, content in sources.items():
            if not paths[name].exists():
                temporary = cache / (name + "." + uuid.uuid4().hex + ".download")
                try:
                    temporary.write_bytes(content)
                    temporary.replace(paths[name])
                finally:
                    temporary.unlink(missing_ok=True)
    finally:
        package.unlink(missing_ok=True)
    return paths


def _entry(name, spec, metadata):
    blends = [{"source": 1, "destination": 0, "operation": 0} for _ in spec["fragments"]]
    if name == "BlendForBloom":
        blends[7] = {"source": 0, "destination": 5, "operation": 0}
        blends[9] = {"source": 1, "destination": 1, "operation": 0}
        blends[10] = {"source": 1, "destination": 1, "operation": 4}
    return {"assetPath": "Assets/Shader/Hidden_" + name + ".shader", "name": "Hidden/" + name,
            "guid": spec["guid"], "metaSha256": hashlib.sha256(metadata).hexdigest(),
            "originalDummySha256": spec["dummySha256"], "sourceSha256": spec["sourceSha256"],
            "sourceBytes": spec["sourceBytes"], "sourcePackagePath": PACKAGE_PREFIX + name + ".shader",
            "importUpgrade": {"kind": "UnityObjectToClipPos", "sha256": spec["importUpgradeSha256"],
                              "replacements": spec["objectToClipPosReplacements"]},
            "canonicalRecipeSha256": spec["recipeSha256"], "originalPathId": spec["originalPathId"],
            "passCount": len(spec["fragments"]), "passFragments": spec["fragments"],
            "properties": spec["properties"], "nativeUniforms": spec["uniforms"],
            "originalPassStates": [{"cull": 0, "zTest": 8, "zWrite": 0, "blend": blend} for blend in blends]}


def restore_post_effects(project, cache):
    """Replace only audited dummy exports; retain each original .meta verbatim."""
    project = _safe_path(project)
    if not (project / "Assets/Quest").is_dir():
        raise BuildError("Legacy post-effects restoration requires a generated Quest project")
    _safe_path(project / "Assets/Quest")
    receipt_path = _safe_path(project / RECEIPT)
    before, entries = {}, []
    restored = receipt_path.exists()
    for name, spec in SHADERS.items():
        target = _safe_path(project / ("Assets/Shader/Hidden_" + name + ".shader"))
        metadata_path = _safe_path(Path(str(target) + ".meta"))
        if restored:
            # Unity upgrades this exact legacy vertex expression at first import.
            # Accept only its independently audited full-file fingerprint, never
            # arbitrary edits or the mere presence of an upgrader comment.
            if not target.is_file() or _sha(target) not in (spec["sourceSha256"], spec["importUpgradeSha256"]):
                raise BuildError("Restored legacy shader differs from official source/Unity upgrade: " + name)
        else:
            _require(target, spec["dummySha256"])
        if not metadata_path.is_file():
            raise BuildError("Missing original shader metadata: " + name)
        metadata = metadata_path.read_bytes()
        if len(metadata) > 65536 or GUID.findall(metadata.decode("utf-8")) != [spec["guid"]] or b"ShaderImporter:" not in metadata:
            raise BuildError("Original shader GUID/importer differs: " + name)
        before[target] = target.read_bytes()
        entries.append(_entry(name, spec, metadata))
    receipt = {"schema": 1, "target": "startup", "unityVersion": VERSION, "changeset": CHANGESET,
               "sourceUrl": SOURCE_URL, "officialReleaseUrl": "https://unity.com/releases/editor/whats-new/5.3.5f1",
               "installerBytes": SOURCE_BYTES, "installerSha256": SOURCE_SHA256,
               "effectsPackageBytes": PACKAGE_BYTES, "effectsPackageSha256": PACKAGE_SHA256,
               "acquisition": "official-download-private-build-cache", "installerExecuted": False,
               "sourceRedistributedByRepository": False, "shaderIncludeBoundary": ["UnityCG.cginc"],
               "shaders": entries, "androidShaderCompiled": False, "originalPixelParityVerified": False}
    if restored:
        if json.loads(receipt_path.read_text(encoding="utf-8")) != receipt:
            raise BuildError("Previous legacy post-effects receipt differs from the verified assets")
        return receipt
    sources = acquire(cache)  # Every source and all target identities pass before project mutation.
    contents = {name: path.read_bytes() for name, path in sources.items()}
    for name, content in contents.items():
        _validate_source(name, content)
    written = []
    try:
        for name in SHADERS:
            target = project / ("Assets/Shader/Hidden_" + name + ".shader")
            temporary = target.with_name(target.name + "." + uuid.uuid4().hex + ".restore")
            try:
                temporary.write_bytes(contents[name])
                temporary.replace(target)
                written.append(target)
            finally:
                temporary.unlink(missing_ok=True)
        write_json(receipt_path, receipt)
    except BaseException:
        receipt_path.unlink(missing_ok=True)
        for target in reversed(written):
            target.write_bytes(before[target])
        raise
    return receipt
