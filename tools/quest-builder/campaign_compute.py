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

# Capture092443 contains the exact already witnessed EyeHistogram desktop bank,
# but its owner was rejected by a sandbox export GUID. AssetRipper's generated
# GUIDs are random (export_identity.py); case_paths may relocate their paths, and
# recovery.py writes/hashes platform-native LF/CRLF source bytes. None is a
# cross-build original identity. references.repair separately proves each local
# GUID/path/meta against its captured native collection/pathID/class72 owner.
# Qualify these ancillary imports by the complete original DXBC/kernel/interface/
# output contract and exact serialized desktop bank instead. The required Vulkan
# executable validator still receives every active original kernel unchanged.
_RETAINED_EYE_KERNEL_CONTRACT_SHA256 = "ec8d074d9f5d56590cbeafd410345d8ab3849a2b82357bdf6f23afdc30e92de4"
_RETAINED_PP_OPENGL_BANKS = {
    'AutoExposure': ('28941c651d43f644210b2bdd390150e8c950e9bd769a24406545934c3d30eb43', '89e64071c6911c0df8417f6abbdf9d6dfad0206bdaac4f9798980bbf32c895a6'),
    'ExposureHistogram': ('b255e67422a219ce18bdd9f2cfb78b5e1c21a0570a8f7978a01d38d362ade0c5', 'e3d5a4dcb3925ce0b5beb4054fda5a520bbb755e2ce4e09f89da1daeebd7fdc4'),
    'GaussianDownsample': ('ed5b60f49b286db62cd61d2b2cd9c6b85e6466ae419a869cd2c2527d71db5257', 'db70599e93d21964fcbe8b9c1135bfe3b7c02db67fd5590fd87b335ead8ba16d'),
    'Histogram': ('516c1a99ef63efa261f29774dbc61e8fddfaefb2e2b5c07cfdd177f4ec36655e', 'abc6a52311e45883b9e2dbcd1e8ee0da0ce9401be2778787bd55b0394eb40c27'),
    'Lut3DBaker': ('5c0bf92227804c70f9070c35f3ad339fc26f08a4c7f3bae446de479076ebb3f0', '897c6213fbb8885fd5705c0009e64177b2914cdd3e7653b2710e1c0fd3a93caf'),
    'MultiScaleVODownsample1': ('836ef390a26ffac0ca5f1ae05b235fa063e6f668fdd543541538234aeedfee76', 'a86679e669fea3bda4c1fa7272e214e603478994b2def8a27664bab410941505'),
    'MultiScaleVODownsample2': ('32f2c22a4e35a33da155634361adf75a869973419be34252090c8a3f377a3495', '546ac4fd0de84f21fcbf8ee03a9f7c1c42d9e3ed7fb54b7c857199b4c6094bd1'),
    'MultiScaleVORender': ('4d8f9097733904fb1d049050a99f69f91888534787d47d0385224b0089368e42', '8f3d636a9928b417c5fead8acce370215795d3d3fd0d3308faefe691da3690de'),
    'MultiScaleVOUpsample': ('97cdedd399938cb836aba2d7472ba995c56b566bc013d81bdcdaa1e826530395', '1490f9ecd7dc9a19df527600fd89a1bf1521a83c70c2b1b1983ea7c3e10f0bb6'),
    'Texture3DLerp': ('43446a7fa011a07f5f1338493787658e5d2ae19b68e0cfcb5bc17f6ee94228a4', '0aabfb564a54dbaa7b78a7f6de94812637f40e8426cb7993935910f8b14faf8e'),
    'Vectorscope': ('48ba323f4f582ce729c40d626b5623682d3988374fe7d570f690d371a118664c', '477047f36ba192328f5d6a4f7c81311e6017c8adbdab012b0feb854011758d58'),
    'Waveform': ('f5e4ae0f7b088d164985a96288f88f162fa7f958a227c3d2139fc1ba631483db', '697f2702fe6b72296ea075416436d6910e409f06b875ad004cfa4b86a2a370e1'),
}


def original_kernel_contract_hash(contract):
    keys = ("name", "originalDxbcSha256", "threadGroups", "interface", "outputBindings")
    return hashlib.sha256(json.dumps([{key: kernel.get(key) for key in keys}
        for kernel in contract["kernels"]], sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def qualify_local_compute_sources(project, manifest):
    """Check only current source/meta links; never rescan the recovery project.

    The completed producer already proves the original native CAB/pathID. This
    incomplete final gate still needs the current export's actual source/meta
    bytes and GUID, rather than accepting another well-formed local link. The
    complete game has only 13 small source/meta pairs; successful final receipts
    reuse these proofs without any new read or Unity import.
    """
    project = Path(project).resolve()
    def read_local(name, maximum):
        path = project / asset_path(name)
        if (not path.is_file() or path.is_symlink() or project not in path.resolve().parents or
                any(parent.is_symlink() for parent in path.parents if parent != project and project in parent.parents)):
            raise BuildError("Delivered compute source/meta is missing, linked or outside its project: " + name)
        with path.open("rb") as stream:
            data = stream.read(maximum + 1)
        if not data or len(data) > maximum:
            raise BuildError("Delivered compute source/meta has invalid bounded size: " + name)
        return data
    names, guids, paths = set(), set(), set()
    for row in manifest["shaders"]:
        name = row.get("assetPath")
        if (not isinstance(name, str) or not name.endswith(".compute") or row.get("classId") != 72 or
                row.get("localFileId") != 7200000 or not isinstance(row.get("guid"), str) or
                re.fullmatch(r"[0-9a-f]{32}", row["guid"]) is None or
                not isinstance(row.get("name"), str) or not row["name"]):
            raise BuildError("Delivered compute has an invalid current original-source owner.")
        if row["name"] in names or row["guid"] in guids or name in paths:
            raise BuildError("Delivered compute contains duplicate current original-source owners.")
        names.add(row["name"]); guids.add(row["guid"]); paths.add(name)
        source = read_local(name, 1024 * 1024)
        meta = read_local(name + ".meta", 64 * 1024)
        if (hashlib.sha256(source).hexdigest() != row.get("sourceSha256") or
                hashlib.sha256(meta).hexdigest() != row.get("metaSha256")):
            raise BuildError("Delivered compute source/meta differs from its retained original owner: " + name)
        try:
            text = meta.decode("utf-8")
        except UnicodeError as exc:
            raise BuildError("Delivered compute metadata is not UTF-8: " + name) from exc
        if (re.findall(r"^guid: ([0-9a-f]{32})\r?$", text, re.M) != [row["guid"]] or
                re.findall(r"^(ComputeShaderImporter:)\r?$", text, re.M) != ["ComputeShaderImporter:"]):
            raise BuildError("Delivered compute metadata loses its current GUID/importer: " + name)
    return {"actualLocalComputeSourceCount": len(manifest["shaders"]),
        "actualLocalComputeMetadataCount": len(manifest["shaders"])}


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
    contain desktop OpenGL banks from Unity's imported resources. Their exact
    original native contracts and serialized banks are separately reported.
    Every delivered object's raw hash remains in the origin ledger; the original
    executable validator receives only the selected Vulkan bank. Unknown or
    duplicate banks fail before that validator runs.
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
            kernel_hash = original_kernel_contract_hash(contract)
            # Validate the current export link without mistaking it for a native
            # identity shared by independent exports or host newline formats.
            local_owner = (contract.get("classId") == 72 and
                contract.get("localFileId") == 7200000 and
                isinstance(contract.get("guid"), str) and
                re.fullmatch(r"[0-9a-f]{32}", contract["guid"]) is not None and
                isinstance(contract.get("sourceSha256"), str) and
                re.fullmatch(r"[0-9a-f]{64}", contract["sourceSha256"]) is not None and
                isinstance(contract.get("assetPath"), str) and
                contract["assetPath"].endswith(".compute"))
            if local_owner:
                asset_path(contract["assetPath"])
            original = ((_RETAINED_EYE_KERNEL_CONTRACT_SHA256, _RETAINED_EYE_OPENGL_BANK_SHA256)
                if obj["m_Name"] == "EyeHistogram" else _RETAINED_PP_OPENGL_BANKS.get(obj["m_Name"]))
            if (not local_owner or original != (kernel_hash, bank_hash) or
                    (bank.get("targetRenderer"), bank.get("targetLevel")) != (17, 11)):
                raise BuildError("Runtime compute contains an unrecognized additional native bank: " +
                    str(obj["m_Name"])[:64] + " renderer=" + str(bank.get("targetRenderer"))[:32] +
                    " level=" + str(bank.get("targetLevel"))[:32] + " sha256=" + bank_hash +
                    " originalKernelSha256=" + kernel_hash +
                    " localOwner=" + str(local_owner))
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
                "originalKernelContractSha256": kernel_hash,
                "selectedForAndroidRuntimeAudit": False, "kernels": kernel_evidence}
            if len(kernel_evidence) == 1:
                # Preserve the existing EyeHistogram evidence API.
                row.update(kernel_evidence[0])
            additional.append(row)
        selected.append({**obj, "variants": vulkan})
    return selected, {"additionalNativeBanks": additional}


def validate_delivered(source, project, apk, banks, receipt, *, artifact_hash=None):
    """Audit actual cooked Android programs after the signed full player is built."""
    manifest_path = Path(project) / "Assets/QuestOriginalCampaign/campaign-computes.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    local_evidence = qualify_local_compute_sources(project, manifest)
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
    compiled = campaign_shaders.load(Path(source), "tools/quest-compute/compiled.py", "tools/quest-compute")
    objects, evidence = collect_delivered(apk, banks, manifest, decode, metadata_has_compute)
    runtime_objects, bank_evidence = select_vulkan_runtime_banks(manifest, objects)
    result = compiled.validate_objects(manifest, runtime_objects)
    result.update(bank_evidence)
    result.update(evidence)
    result.update(local_evidence)
    # The host already verified the signed APK/bank and may supply its exact
    # unchanged-file witness. Standalone audits still stream their own hashes.
    artifact_hash = digest if artifact_hash is None else artifact_hash
    result.update({"recoveryManifestSha256": digest(manifest_path), "actualApkSha256": artifact_hash(Path(apk)),
        "actualContentBanks": [{"file": Path(path).name, "sha256": artifact_hash(Path(path))} for path in banks]})
    write_json(Path(receipt), result)
    return result
