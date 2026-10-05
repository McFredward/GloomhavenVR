"""Apply the complete exact-source ComputeShader overlay before Unity import."""
import json
import hashlib
import gc
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

import campaign_shaders
from full_textures import remap_manifests
from storage import BuildError, digest, write_json


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


def collect_delivered(apk, banks, manifest, decode):
    """Read the signed player and delivered banks, retaining only native class72."""
    expected = {row["name"] for row in manifest["shaders"]}
    objects, hashes, origins, payloads = {}, {}, [], 0
    for index, path in enumerate([Path(apk), *map(Path, banks)]):
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)):
                raise BuildError("Delivered compute archive contains duplicate entry identities.")
            selected = [name for name in names if (
                (index == 0 and name.startswith("assets/bin/Data/") and name.endswith(".assets"))
                or (index > 0 and name.startswith("StreamingAssets/aa/") and name.endswith(".bundle")))]
            for name in selected:
                payloads += 1
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
    return list(objects.values()), {"actualSerializedPayloadCount": payloads, "actualObjectOrigins": origins}


def validate_delivered(source, project, apk, banks, receipt):
    """Audit actual cooked Android programs after the signed full player is built."""
    import UnityPy
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
    objects, evidence = collect_delivered(apk, banks, manifest, decode)
    result = compiled.validate_objects(manifest, objects)
    result.update(evidence)
    result.update({"recoveryManifestSha256": digest(manifest_path), "actualApkSha256": digest(Path(apk)),
        "actualContentBanks": [{"file": Path(path).name, "sha256": digest(Path(path))} for path in banks]})
    write_json(Path(receipt), result)
    return result
