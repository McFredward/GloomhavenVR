"""Apply the complete exact-source ComputeShader overlay before Unity import."""
import json
import hashlib
import gc
import re
import struct
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

import campaign_shaders
from full_textures import remap_manifests
from storage import BuildError, digest, write_json

_RETAINED_EYE_OPENGL_BANK_SHA256 = "244ef1137bd77afbb478218c5fee4ecb191e5675d10b825d751ad6281b40287d"


def asset_path(value):
    if not isinstance(value, str) or not value.startswith("Assets/") or "\\" in value or any(part in ("", ".", "..") for part in value.split("/")):
        raise BuildError("Compute overlay path escapes the generated Assets tree.")
    return value


def stage(source, project, cache):
    source, project, cache = map(Path, (source, project, cache))
    cache.mkdir(parents=True, exist_ok=True)
    overlay = cache / "overlay"
    if overlay.exists(): shutil.rmtree(overlay)
    converter = campaign_shaders.load(source, "tools/quest-shaders/converters.py")
    tools = converter.ensure(cache / "tools", source / "prebuilt/quest-converters-win64-v1.zip")
    arguments = [sys.executable, str(source / "tools/quest-compute/recovery.py"),
        "--source-project", str(project), "--overlay", str(overlay),
        "--graphics-module", str(source / "tools/quest-builder/full_shaders.py"),
        "--vkd3d", str(tools["vkd3d"]), "--spirv-cross", str(tools["spirv_cross"])]
    with (cache / "recovery.log").open("w", encoding="utf-8") as log:
        result = subprocess.run(arguments, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode:
        raise BuildError("Complete original compute recovery failed; see " + str(cache / "recovery.log"))
    manifest = json.loads((overlay / "QuestCampaignEvidence/compute-recovery.json").read_text())
    if (manifest.get("schema") != 1 or manifest.get("graphicsApi") != "Vulkan"
            or manifest.get("shaderCount") != 13 or manifest.get("kernelCount") != 36
            or manifest.get("originalInputsUnchanged") is not True or len(manifest.get("files", {})) != 26
            or len(manifest.get("pathMap", {})) != 13 or len(manifest.get("shaders", [])) != 13
            or len(manifest.get("removePaths", [])) != 26
            or set(manifest["removePaths"]) != set(manifest["originalInputs"])):
        raise BuildError("Compute overlay does not contain every exact original kernel.")
    for original, generated in manifest["pathMap"].items():
        if (not original.endswith(".asset") or generated != original[:-6] + ".compute"
                or generated not in manifest["files"] or generated + ".meta" not in manifest["files"]):
            raise BuildError("Compute overlay changed an unproven native asset path.")
    for name, checksum in manifest["originalInputs"].items():
        if digest(project / asset_path(name)) != checksum:
            raise BuildError("Original compute input changed before applying its overlay.")
    for name, row in manifest["files"].items():
        path = overlay / asset_path(name)
        if path.is_symlink() or not path.is_file() or path.stat().st_size != row["bytes"] or digest(path) != row["sha256"]:
            raise BuildError("Recovered compute source differs from its exact native proof.")
    # Validate the entire small overlay before changing the generated copy.
    for name in manifest["removePaths"]:
        (project / asset_path(name)).unlink()
    for name in manifest["files"]:
        path = project / asset_path(name)
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(overlay / name, path)
    manifest["updatedManifests"] = remap_manifests(project, manifest["pathMap"])
    references = campaign_shaders.load(source, "tools/quest-compute/references.py", "tools/quest-compute")
    manifest["nativeComputeReferenceTypes"] = references.repair(project, manifest)
    write_json(project / "Assets/QuestOriginalCampaign/campaign-computes.json", manifest)
    write_json(project / "QuestStartupEvidence/compute-source-restoration.json", {
        "schema": 1, "scope": "complete-original-campaign-compute", "shaderCount": 13, "kernelCount": 36,
        "manifestSha256": digest(project / "Assets/QuestOriginalCampaign/campaign-computes.json"),
        "androidCompiled": False, "hardwareVerified": False})
    return manifest


def player_payload_name(name):
    """Recognize Unity player asset identities, excluding engine-owned defaults.

    Unity 2021 writes individual game objects under their serialized GUID, not
    necessarily a .assets suffix. Raw .resource/.resS data and Unity's default
    compute programs are outside the original game ComputeShader contract.
    """
    prefix = "assets/bin/Data/"
    if not isinstance(name, str) or not name.startswith(prefix):
        return False
    leaf = name[len(prefix):]
    return ("/" not in leaf and (leaf.endswith(".assets")
        or re.fullmatch(r"[0-9a-f]{32}", leaf) is not None
        or leaf == "globalgamemanagers" or re.fullmatch(r"level\d+", leaf) is not None))


def serialized_metadata(archive, info):
    """Read only a validated SerializedFile header and bounded metadata table."""
    with archive.open(info) as stream:
        header = stream.read(48)
        if len(header) < 20:
            raise BuildError("Delivered player asset has a truncated native serialized header.")
        metadata, size, version, offset = struct.unpack_from(">IIII", header)
        if not 9 <= version <= 22 or header[16] not in (0, 1) or header[17:20] != b"\0\0\0":
            raise BuildError("Delivered player asset has an unsupported native serialized header.")
        header_size = 20
        if version >= 22:
            if len(header) < 48:
                raise BuildError("Delivered player asset has a truncated extended serialized header.")
            metadata, size, offset = struct.unpack_from(">IQQ", header, 20)
            header_size = 48
        if (size != info.file_size or not 0 < metadata < size
                or not header_size + metadata <= offset <= size
                or offset > 16 * 1024 * 1024):
            raise BuildError("Delivered player asset has inconsistent or oversized native metadata.")
        prefix = header[:offset]
        prefix += stream.read(offset - len(prefix))
        if len(prefix) != offset:
            raise BuildError("Delivered player asset native metadata is truncated.")
        return prefix


def collect_delivered(apk, banks, manifest, decode, metadata_has_compute=None):
    """Read the signed player and delivered banks, retaining only native class72."""
    expected = {row["name"] for row in manifest["shaders"]}
    objects, hashes, origins, payloads, decoded, player_metadata = {}, {}, [], 0, 0, 0
    for index, path in enumerate([Path(apk), *map(Path, banks)]):
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)):
                raise BuildError("Delivered compute archive contains duplicate entry identities.")
            selected = [name for name in names if (
                (index == 0 and player_payload_name(name))
                or (index > 0 and name.startswith("StreamingAssets/aa/") and name.endswith(".bundle")))]
            for name in selected:
                payloads += 1
                if index == 0:
                    metadata = serialized_metadata(archive, archive.getinfo(name))
                    player_metadata += 1
                    if metadata_has_compute is not None and not metadata_has_compute(metadata):
                        continue
                decoded += 1
                for obj in decode(archive.read(name)):
                    identity = obj.get("m_Name")
                    if identity not in expected:
                        raise BuildError("Delivered player contains an unowned compute identity: " + str(identity))
                    checksum = hashlib.sha256(json.dumps(obj, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
                    if identity in hashes and hashes[identity] != checksum:
                        raise BuildError("Delivered compute duplicates have different compiled programs: " + identity)
                    objects[identity], hashes[identity] = obj, checksum
                    origins.append({"archive": path.name, "entry": name, "shader": identity, "objectSha256": checksum})
    if objects.keys() != expected:
        raise BuildError("The delivered APK and data bank omit original compute objects: " + ", ".join(sorted(expected - objects.keys())))
    return list(objects.values()), {"actualSerializedPayloadCount": payloads,
        "actualDecodedPayloadCount": decoded, "actualPlayerMetadataPayloadCount": player_metadata,
        "actualObjectOrigins": origins}


def select_vulkan_runtime_banks(manifest, objects):
    """Audit Vulkan without hiding the exact retained Editor resource bank.

    The signed EyeHistogram Resources object also contains a desktop OpenGL bank
    from Unity's imported resource. Its precise serialized identity is separately
    recognized and reported. Every delivered object's raw hash remains in the
    origin ledger; the original executable validator receives only the selected
    Vulkan bank. Unknown or duplicate banks fail before that validator runs.
    """
    if manifest.get("graphicsApi") != "Vulkan":
        return objects, {"additionalNativeBanks": []}
    contracts = {row["name"]: row for row in manifest["shaders"]}
    selected, additional = [], []
    for obj in objects:
        contract = contracts.get(obj.get("m_Name"))
        if contract is None:
            raise BuildError("Runtime compute bank has no original owner.")
        native = obj.get("variants", [])
        vulkan = [bank for bank in native if (bank.get("targetRenderer"), bank.get("targetLevel")) == (21, 0)]
        if len(vulkan) != 1:
            raise BuildError("Runtime compute requires one exact Vulkan native bank.")
        extras = [bank for bank in native if bank is not vulkan[0]]
        if len(extras) > 1:
            raise BuildError("Runtime compute contains duplicate additional native banks.")
        for bank in extras:
            bank_hash = hashlib.sha256(json.dumps(bank, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
            if (obj["m_Name"] != "EyeHistogram" or contract.get("guid") != "14b830dd8a5381e4399cfe161a01662f"
                    or (bank.get("targetRenderer"), bank.get("targetLevel")) != (17, 11)
                    or len(contract["kernels"]) != 1
                    or contract["kernels"][0].get("originalDxbcSha256") != "26239d6030173bc084c84cd719c28e261b31987e1423ebc98cbeded5bd164533"
                    or bank_hash != _RETAINED_EYE_OPENGL_BANK_SHA256):
                raise BuildError("Runtime compute contains an unrecognized additional native bank.")
            actual_kernel = bank["kernels"][0]
            expected_kernel = contract["kernels"][0]
            programs = actual_kernel["variantMap"]
            if (actual_kernel["name"] != expected_kernel["name"] or len(programs) != 1
                    or programs[0][0] != "" or programs[0][1]["threadGroupSize"] != expected_kernel["threadGroups"]):
                raise BuildError("Retained desktop compute kernel identity differs from its original owner.")
            program = programs[0][1]
            code = bytes(program["code"])
            if not code.startswith(b"#version 430\n"):
                raise BuildError("Retained desktop compute bank is not the witnessed OpenGL executable.")
            additional.append({"shader": obj["m_Name"], "guid": contract["guid"],
                "assetPath": contract["assetPath"],
                "targetRenderer": 17, "targetLevel": 11, "backend": "desktop-OpenGL",
                "actualBankSha256": bank_hash, "selectedForAndroidRuntimeAudit": False,
                "kernelName": actual_kernel["name"], "threadGroups": list(program["threadGroupSize"]),
                "actualProgramSha256": hashlib.sha256(code).hexdigest()})
        selected.append({**obj, "variants": vulkan})
    return selected, {"additionalNativeBanks": additional}


def validate_delivered(source, project, apk, banks, receipt):
    """Audit actual cooked Android programs after the signed full player is built."""
    import UnityPy
    from UnityPy.files import SerializedFile
    from UnityPy.streams import EndianBinaryReader
    def metadata_has_compute(data):
        try:
            native = SerializedFile(EndianBinaryReader(data))
            return any(obj.type.name == "ComputeShader" for obj in native.objects.values())
        except Exception as exc:
            raise BuildError("Delivered player native object metadata could not be read.") from exc
    def decode(data):
        env = UnityPy.load(data)
        try:
            return [obj.read_typetree() for obj in env.objects if obj.type.name == "ComputeShader"]
        finally:
            del env
            gc.collect()
    manifest_path = Path(project) / "Assets/QuestOriginalCampaign/campaign-computes.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    compiled = campaign_shaders.load(Path(source), "tools/quest-compute/compiled.py", "tools/quest-compute")
    objects, evidence = collect_delivered(apk, banks, manifest, decode, metadata_has_compute)
    runtime_objects, bank_evidence = select_vulkan_runtime_banks(manifest, objects)
    result = compiled.validate_objects(manifest, runtime_objects)
    result.update(bank_evidence)
    result.update(evidence)
    result.update({"recoveryManifestSha256": digest(manifest_path), "actualApkSha256": digest(Path(apk)),
        "actualContentBanks": [{"file": Path(path).name, "sha256": digest(Path(path))} for path in banks]})
    write_json(Path(receipt), result)
    return result
