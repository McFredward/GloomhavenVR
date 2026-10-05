"""Stage and witness the original full-game native ABIs without store services."""
import importlib.util
import json
from pathlib import Path
import shutil

from storage import BuildError, digest, record_file, write_json


def load(source, relative):
    spec = importlib.util.spec_from_file_location("quest_campaign_" + Path(relative).parent.name.replace("-", "_"), source / relative)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def stage(source, project, game, cache, ndk):
    plugins = project / "Assets/Quest/Plugins/Android"
    streaming = project / "Assets/StreamingAssets"
    runtime = load(source, "tools/quest-procedural-runtime/runtime.py")
    procedural = runtime.stage(cache, ndk, plugins, streaming, game / "Plugins/x86_64/ApparanceEngine.dll")
    original = game / "Plugins/x86_64/ApparanceEngine.dll"
    manifest = json.loads(Path(procedural["manifest"]).read_text())
    if (manifest.get("schema") != 1 or manifest.get("originalEngineSha256") != digest(original)
            or set(runtime.EXPORTS) != {"ApparanceInitialise", "ApparanceIsRunning", "ApparanceUpdate", "ApparanceSave", "ApparanceShutdown",
                "ApparanceCreateEntity", "ApparanceDestroyEntity", "ApparanceEntityBuild", "ApparancePopEntityTask", "ApparancePopEngineTask", "ApparanceUpdateAsset", "ApparanceGetNextAssetRequest"}):
        raise BuildError("Staged procedural bridge differs from its original owned native ABI.")
    payload = Path(procedural["payload"])
    for row in manifest["files"]:
        path = payload / row["path"]
        if not path.is_file() or path.stat().st_size != row["size"] or digest(path) != row["sha256"]:
            raise BuildError("Staged original engine payload changed: " + row["path"])
    voice = load(source, "tools/quest-network/native.py").stage(cache, ndk, plugins / "arm64-v8a", streaming / "ThirdPartyNotices")
    resources = project / "Assets/Quest/Resources"
    resources.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(procedural["manifest"], resources / "quest-procedural-runtime.json")
    result = {"schema": 1, "scope": "original-campaign-native-abis", "originalEngineSha256": digest(original),
              "proceduralPayloadManifestSha256": procedural["manifestSha256"], "payloadFiles": procedural["files"],
              "voice": voice, "androidDeviceExecutionVerified": False,
              "nativeFiles": [record_file(plugins / "arm64-v8a" / name, "Assets/Quest/Plugins/Android/arm64-v8a/" + name)
                  for name in ("libQuestApparance.so", "libquest_box64.so", "libquest_wineserver.so", "libopus_egpv.so")]}
    write_json(project / "QuestCampaignEvidence/native-runtime.json", result)
    return result


def build_contract(project, inputs):
    """Certify staged implementations; device readiness remains a separate result."""
    resources = project / "Assets/Quest/Resources"
    native = json.loads((project / "QuestCampaignEvidence/native-runtime.json").read_text())
    platform = json.loads((resources / "quest-standalone-report.json").read_text())
    if (native.get("schema") != 1 or native.get("scope") != "original-campaign-native-abis"
            or platform.get("scope") != "campaign-local-platform" or not platform.get("startupAdapterComplete")
            or not platform.get("bepInExAdapterGenerated") or platform.get("issues")
            or "lifecycle-mod.dll" not in platform.get("inputAssemblies", {})):
        raise BuildError("Full-game contract requires the original native bridge and complete current mod lifecycle adaptation.")
    modifications = "\n".join(platform.get("modifications", []))
    if "bind exactly12 original native exports" not in modifications or "atomic native file bytes" not in modifications:
        raise BuildError("Full-game contract is missing its procedural or original save-write adaptation.")
    content = json.loads((resources / "quest-startup-report.json").read_text())
    if (content.get("target") != "campaign" or len(content.get("selectedScenes", [])) != 13
            or content.get("unresolvedAddressables") or content.get("missingReferences", {}).get("missingGuidCount", 0)
            or content.get("readiness", {}).get("fullOriginalCatalogRecovered") is not True):
        raise BuildError("Full-game contract requires complete original scene and catalog recovery.")
    paths = [project / row["path"] for row in native["nativeFiles"]]
    mod = list((project / "Assets").rglob("GloomhavenVR.dll"))
    if len(mod) != 1:
        raise BuildError("Full-game contract requires exactly one actual current mod assembly.")
    paths += mod
    paths += [resources / name for name in ("quest-procedural-runtime.json", "quest-standalone-report.json",
              "quest-startup-report.json", "quest-startup-movies.json", "quest-mod-content.json", "quest-dlc-ownership.json")]
    for row in native["nativeFiles"]:
        if record_file(project / row["path"], row["path"]) != row:
            raise BuildError("Campaign native plugin changed after staging: " + row["path"])
    contract = {"schema": 1, "scope": "complete-campaign-package", "inputKey": inputs["inputKey"],
                "completeOriginalContent": True, "completeCurrentModAot": True, "originalDynamicProceduralAbi": True,
                "localNativeSaves": True, "originalSessionCodeTransport": True, "androidDeviceExecutionVerified": False,
                "files": [record_file(path, path.relative_to(project).as_posix()) for path in paths]}
    write_json(resources / "quest-campaign-build-contract.json", contract)
    return contract
