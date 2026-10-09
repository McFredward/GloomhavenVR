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
from collections import deque

import campaign_shaders
from full_textures import remap_manifests
from storage import BuildError, digest, write_json, build_progress

_RETAINED_EYE_OPENGL_BANK_SHA256 = "244ef1137bd77afbb478218c5fee4ecb191e5675d10b825d751ad6281b40287d"

# The actual 2021.3.5f1 full Player's native postprocessdata bank also
# retains desktop OpenGL imports. These twelve exact ancillary banks are
# qualified against unchanged original source, kernel/DXBC/interfaces and all
# 36 active Vulkan kernels' byte-identical independent Android cook. They are
# not Android backends. Unknown imports remain errors (including D3D imports
# from an as-yet unqualified Windows host).
_RETAINED_PP_OPENGL_BANKS = {
    'AutoExposure': ('52e89a7b063f39d47a40ba4708a54eb7', 'Assets/QuestRecoveredBundles/52e89a7b063f39d47a40ba4708a54eb7/AutoExposure.compute', '7b66c8732d83fef31430a24734894cc6e56aa38180ead4d3e719106867a7c812', '28941c651d43f644210b2bdd390150e8c950e9bd769a24406545934c3d30eb43', '89e64071c6911c0df8417f6abbdf9d6dfad0206bdaac4f9798980bbf32c895a6'),
    'ExposureHistogram': ('3b9a1fdf34dd2fb4b8d7885acb58f31b', 'Assets/QuestRecoveredBundles/3b9a1fdf34dd2fb4b8d7885acb58f31b/ExposureHistogram.compute', 'e9f487afbac34ed1f50430ce3f1c61c89af55e534ef1e2f0a3c05ef6b803024e', 'b255e67422a219ce18bdd9f2cfb78b5e1c21a0570a8f7978a01d38d362ade0c5', 'e3d5a4dcb3925ce0b5beb4054fda5a520bbb755e2ce4e09f89da1daeebd7fdc4'),
    'GaussianDownsample': ('f16b5ef93858adc49a13756a300c896d', 'Assets/QuestRecoveredBundles/f16b5ef93858adc49a13756a300c896d/GaussianDownsample.compute', 'f400c70a613129f9dfdac605eb7a8f4c74e6e1e3cbbbf80d609c950857d3ad41', 'ed5b60f49b286db62cd61d2b2cd9c6b85e6466ae419a869cd2c2527d71db5257', 'db70599e93d21964fcbe8b9c1135bfe3b7c02db67fd5590fd87b335ead8ba16d'),
    'Histogram': ('2a84007819ee05b4e858f8a70c4b95a4', 'Assets/QuestRecoveredBundles/2a84007819ee05b4e858f8a70c4b95a4/Histogram.compute', '721962626de7c473de4eed3887bec942422975dd7e7e36baa3361c49ad18a6b9', '516c1a99ef63efa261f29774dbc61e8fddfaefb2e2b5c07cfdd177f4ec36655e', 'abc6a52311e45883b9e2dbcd1e8ee0da0ce9401be2778787bd55b0394eb40c27'),
    'Lut3DBaker': ('b2f558168df2fc24782d3f56b1f86b82', 'Assets/QuestRecoveredBundles/b2f558168df2fc24782d3f56b1f86b82/Lut3DBaker.compute', '8ca65d5b27a7b2920e45d52f1e72c74f1f49db28f7b94586bb287779e7c73af2', '5c0bf92227804c70f9070c35f3ad339fc26f08a4c7f3bae446de479076ebb3f0', '897c6213fbb8885fd5705c0009e64177b2914cdd3e7653b2710e1c0fd3a93caf'),
    'MultiScaleVODownsample1': ('a22d1b6c430505847a78d79682dd4aab', 'Assets/QuestRecoveredBundles/a22d1b6c430505847a78d79682dd4aab/MultiScaleVODownsample1.compute', 'f46eda70fc70114cc25404b3b6d2eee111208db383abc6c7c0fbf1d1aac7b6df', '836ef390a26ffac0ca5f1ae05b235fa063e6f668fdd543541538234aeedfee76', 'a86679e669fea3bda4c1fa7272e214e603478994b2def8a27664bab410941505'),
    'MultiScaleVODownsample2': ('1448f10d86ba0ad409379172e4858b49', 'Assets/QuestRecoveredBundles/1448f10d86ba0ad409379172e4858b49/MultiScaleVODownsample2.compute', '9201d129e299df3fc722ffc1b6da5ba487e3b0edf1e01bbefe23210a2e051f2d', '32f2c22a4e35a33da155634361adf75a869973419be34252090c8a3f377a3495', '546ac4fd0de84f21fcbf8ee03a9f7c1c42d9e3ed7fb54b7c857199b4c6094bd1'),
    'MultiScaleVORender': ('d52ad211894ef144c9ccf180f91d6b94', 'Assets/QuestRecoveredBundles/d52ad211894ef144c9ccf180f91d6b94/MultiScaleVORender.compute', '9c93b8897e3993c3f60d3e833fc8dbaa5ab2f63db674aebb6767ef1a19a40758', '4d8f9097733904fb1d049050a99f69f91888534787d47d0385224b0089368e42', '8f3d636a9928b417c5fead8acce370215795d3d3fd0d3308faefe691da3690de'),
    'MultiScaleVOUpsample': ('b20b6467527ecbf4085d3d19e987cc28', 'Assets/QuestRecoveredBundles/b20b6467527ecbf4085d3d19e987cc28/MultiScaleVOUpsample.compute', 'bb987e02e55ed8d3efbded5448267f3033ae53a731cf5f6b5b4c2b5e16b9dbe6', '97cdedd399938cb836aba2d7472ba995c56b566bc013d81bdcdaa1e826530395', '1490f9ecd7dc9a19df527600fd89a1bf1521a83c70c2b1b1983ea7c3e10f0bb6'),
    'Texture3DLerp': ('a6dd916fb826bff45a8909ebdc745cf1', 'Assets/QuestRecoveredBundles/a6dd916fb826bff45a8909ebdc745cf1/Texture3DLerp.compute', '9695de597dd3b31001d6a702b307ebf295ab1ec5e4674c6e6c08dbe67dace059', '43446a7fa011a07f5f1338493787658e5d2ae19b68e0cfcb5bc17f6ee94228a4', '0aabfb564a54dbaa7b78a7f6de94812637f40e8426cb7993935910f8b14faf8e'),
    'Vectorscope': ('a6a869a2754fb264dbe0c61453160903', 'Assets/QuestRecoveredBundles/a6a869a2754fb264dbe0c61453160903/Vectorscope.compute', '122d1fba72bb1576fe732e7287c4748450872dc9c6ba019fc4d3c565fa54501f', '48ba323f4f582ce729c40d626b5623682d3988374fe7d570f690d371a118664c', '477047f36ba192328f5d6a4f7c81311e6017c8adbdab012b0feb854011758d58'),
    'Waveform': ('a36f64a94b9405c469c3ff9b6714cb37', 'Assets/QuestRecoveredBundles/a36f64a94b9405c469c3ff9b6714cb37/Waveform.compute', '588993061844c9c9b932250384294086dcf7d0ffe1452d02a18afb1e834cd936', 'f5e4ae0f7b088d164985a96288f88f162fa7f958a227c3d2139fc1ba631483db', '697f2702fe6b72296ea075416436d6910e409f06b875ad004cfa4b86a2a370e1'),
}


def original_kernel_contract_hash(contract):
    keys = ("name", "originalDxbcSha256", "threadGroups", "interface", "outputBindings")
    return hashlib.sha256(json.dumps([{key: kernel.get(key) for key in keys}
        for kernel in contract["kernels"]], sort_keys=True, separators=(",", ":")).encode()).hexdigest()



def asset_path(value):
    if not isinstance(value, str) or not value.startswith("Assets/") or "\\" in value or any(part in ("", ".", "..") for part in value.split("/")):
        raise BuildError("Compute overlay path escapes the generated Assets tree.")
    return value


def recovery_process(arguments, log_path):
    """Retain the complete child log and forward only validated measured events.

    The recovery child used to write exclusively to recovery.log, making its
    original census and 36 kernel conversions invisible to the running Wizard.
    These observations do not change converter inputs, generated bytes or counts.
    """
    tail = deque(maxlen=8)
    with Path(log_path).open("wb") as log:
        with subprocess.Popen(arguments, stdout=subprocess.PIPE, stderr=subprocess.STDOUT) as child:
            try:
                while block := child.stdout.readline(65536):
                    log.write(block)
                    log.flush()
                    decoded = block.decode("utf-8", errors="replace").rstrip()
                    if not decoded.startswith(build_progress.PREFIX):
                        if decoded: tail.append(decoded[-1024:])
                        continue
                    try:
                        value = json.loads(decoded[len(build_progress.PREFIX):])
                        if not isinstance(value, dict) or value.get("schema") != 1:
                            continue
                        build_progress.event(value["phase"], value.get("done"), value.get("total"),
                            value.get("unit"), value.get("detail"), status=value.get("status", "progress"),
                            operation=value.get("operation"))
                    except (KeyError, TypeError, ValueError):
                        continue  # An invalid diagnostic remains in the log, never becomes progress.
                return_code = child.wait()
            finally:
                if child.poll() is None:
                    child.terminate()
                    try: child.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        child.kill()
                        child.wait()
    if return_code:
        reason = tail[-1] if tail else "No converter error was emitted."
        raise BuildError("Complete original compute recovery failed (exit " + str(return_code) + "): " +
            reason + "; see " + str(log_path))


def stage(source, project, cache):
    source, project, cache = map(Path, (source, project, cache))
    cache.mkdir(parents=True, exist_ok=True)
    overlay = cache / "overlay"
    if overlay.exists(): shutil.rmtree(overlay)
    converter = campaign_shaders.load(source, "tools/quest-shaders/converters.py")
    tools = converter.ensure(cache / "tools", source / "prebuilt/quest-converters-win64-v1.zip")
    arguments = [sys.executable, "-X", "utf8", "-u", str(source / "tools/quest-compute/recovery.py"),
        "--source-project", str(project), "--overlay", str(overlay),
        "--graphics-module", str(source / "tools/quest-builder/full_shaders.py"),
        "--vkd3d", str(tools["vkd3d"]), "--spirv-cross", str(tools["spirv_cross"])]
    recovery_process(arguments, cache / "recovery.log")
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
    verify = build_progress.Counter("prepare-items:campaign-compute-verify",
        len(manifest["originalInputs"]) + len(manifest["files"]), "files")
    for name, checksum in manifest["originalInputs"].items():
        if digest(project / asset_path(name)) != checksum:
            raise BuildError("Original compute input changed before applying its overlay.")
        verify.add(1, name)
    for name, row in manifest["files"].items():
        path = overlay / asset_path(name)
        if path.is_symlink() or not path.is_file() or path.stat().st_size != row["bytes"] or digest(path) != row["sha256"]:
            raise BuildError("Recovered compute source differs from its exact native proof.")
        verify.add(1, name)
    verify.finish()
    # Validate the entire small overlay before changing the generated copy.
    apply = build_progress.Counter("prepare-items:campaign-compute-apply",
        len(manifest["removePaths"]) + len(manifest["files"]), "files")
    for name in manifest["removePaths"]:
        (project / asset_path(name)).unlink()
        apply.add(1, name)
    for name in manifest["files"]:
        path = project / asset_path(name)
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(overlay / name, path)
        apply.add(1, name)
    apply.finish()
    manifest["updatedManifests"] = remap_manifests(project, manifest["pathMap"], progress_scope="campaign-compute")
    references = campaign_shaders.load(source, "tools/quest-compute/references.py", "tools/quest-compute")
    manifest["nativeComputeReferenceTypes"] = references.repair(project, manifest)
    publish = build_progress.Counter("prepare-items:campaign-compute-publish", 2, "receipts")
    write_json(project / "Assets/QuestOriginalCampaign/campaign-computes.json", manifest)
    publish.add(1, "campaign-computes.json")
    write_json(project / "QuestStartupEvidence/compute-source-restoration.json", {
        "schema": 1, "scope": "complete-original-campaign-compute", "shaderCount": 13, "kernelCount": 36,
        "manifestSha256": digest(project / "Assets/QuestOriginalCampaign/campaign-computes.json"),
        "androidCompiled": False, "hardwareVerified": False})
    publish.add(1, "compute-source-restoration.json")
    publish.finish()
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

    The signed EyeHistogram and exact original postprocessing objects also
    contain desktop OpenGL banks from Unity's imported resources. Its precise serialized identity is separately
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
            eye_owner = (obj["m_Name"] == "EyeHistogram" and
                contract.get("guid") == "14b830dd8a5381e4399cfe161a01662f" and
                len(contract["kernels"]) == 1 and
                contract["kernels"][0].get("originalDxbcSha256") == "26239d6030173bc084c84cd719c28e261b31987e1423ebc98cbeded5bd164533" and
                bank_hash == _RETAINED_EYE_OPENGL_BANK_SHA256)
            original = _RETAINED_PP_OPENGL_BANKS.get(obj["m_Name"])
            pp_owner = (original is not None and contract.get("classId") == 72 and
                contract.get("localFileId") == 7200000 and
                (contract.get("guid"), contract.get("assetPath"), contract.get("sourceSha256"),
                    original_kernel_contract_hash(contract), bank_hash) == original)
            if (not (eye_owner or pp_owner) or
                    (bank.get("targetRenderer"), bank.get("targetLevel")) != (17, 11)):
                raise BuildError("Runtime compute contains an unrecognized additional native bank: " +
                    str(obj["m_Name"])[:64] + " renderer=" + str(bank.get("targetRenderer"))[:32] +
                    " level=" + str(bank.get("targetLevel"))[:32] + " sha256=" + bank_hash)
            if [kernel["name"] for kernel in bank["kernels"]] != [kernel["name"] for kernel in contract["kernels"]]:
                raise BuildError("Retained desktop compute kernel identity differs from its original owner.")
            kernel_evidence = []
            for actual_kernel, expected_kernel in zip(bank["kernels"], contract["kernels"]):
                programs = actual_kernel["variantMap"]
                if (len(programs) != 1 or programs[0][0] != "" or
                        programs[0][1]["threadGroupSize"] != expected_kernel["threadGroups"]):
                    raise BuildError("Retained desktop compute kernel identity differs from its original owner.")
                program = programs[0][1]
                code = bytes(program["code"])
                if (not code.startswith(b"#version 430\n") or not
                        re.search(rb"\bvoid main\s*\(", code)):
                    raise BuildError("Retained desktop compute bank is not the witnessed OpenGL executable.")
                kernel_evidence.append({"kernelName": actual_kernel["name"],
                    "threadGroups": list(program["threadGroupSize"]),
                    "actualProgramSha256": hashlib.sha256(code).hexdigest()})
            row = {"shader": obj["m_Name"], "guid": contract["guid"],
                "assetPath": contract["assetPath"], "targetRenderer": 17, "targetLevel": 11,
                "backend": "desktop-OpenGL", "actualBankSha256": bank_hash,
                "selectedForAndroidRuntimeAudit": False, "kernels": kernel_evidence}
            if len(kernel_evidence) == 1:
                # Preserve the existing EyeHistogram evidence API.
                row.update(kernel_evidence[0])
            additional.append(row)
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
