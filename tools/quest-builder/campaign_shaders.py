"""Rebuild every owned native Shader bank in the generated Campaign project."""
import importlib.util
import json
from pathlib import Path
import shutil
import sys

import full_shaders
import shaders
import ui_assets
import ui_blur
from storage import BuildError, digest, write_json, build_progress


def load(source, relative, extra=None):
    previous = list(sys.path)
    try:
        if extra: sys.path.insert(0, str(source / extra))
        spec = importlib.util.spec_from_file_location("quest_campaign_" + Path(relative).stem, source / relative)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    finally:
        sys.path[:] = previous


def preserve_sources(project):
    """Retain the six existing audited UI/post-effect source restorations."""
    result = {}
    for receipt_name in (shaders.RECEIPT, ui_assets.RECEIPT, ui_blur.RECEIPT):
        path = project / receipt_name
        receipt = json.loads(path.read_text())
        for row in receipt.get("shaders", [receipt]):
            source = project / row["assetPath"]
            if digest(source) != row["sourceSha256"]:
                raise BuildError("Audited retained shader source changed: " + row["assetPath"])
            if row["guid"] in result:
                raise BuildError("Duplicate retained original shader identity.")
            result[row["guid"]] = {"sourceSha256": row["sourceSha256"], "originalProvenance": {
                "receipt": receipt_name, "receiptSha256": digest(path), "shader": row}}
    if len(result) != 6:
        raise BuildError("Full Campaign requires all six audited UI/post-effect source contracts.")
    return result


def original_cab_bundles(source, game, *, observe=False):
    """Read every owned UnityFS container, including nested PCG asset banks."""
    members = load(source, "tools/quest-recovery/bundle_members.py", "tools/quest-recovery")
    owners = {}
    bundles = sorted((game / "StreamingAssets/aa/StandaloneWindows64").rglob("*.bundle"))
    counter = build_progress.Counter("prepare-items:campaign-shaders-bundles", len(bundles), "bundles") if observe else None
    for bundle in bundles:
        if counter: counter.update(counter.done, bundle.name)
        relative = bundle.relative_to(game).as_posix()
        for member in members.serialized_members(bundle):
            name = member["name"].casefold()
            if name in owners and owners[name] != relative:
                raise BuildError("An original Shader CAB has conflicting native bundle ownership.")
            owners[name] = relative
        if counter: counter.add(1, bundle.name)
    if not owners:
        raise BuildError("Full Shader recovery has no original owned bundle identities.")
    if counter: counter.finish()
    return owners


def cache_overlay(cache):
    """Keep generated Windows paths short without deleting older cache trees."""
    return cache / "o"


def stage(source, project, game, cache, tool_archive=None, *, cab_bundles=None):
    source, project, game, cache = map(Path, (source, project, game, cache))
    cache.mkdir(parents=True, exist_ok=True)
    converter = load(source, "tools/quest-shaders/converters.py")
    tools = converter.ensure(cache / "tools", tool_archive or source / "prebuilt/quest-converters-win64-v1.zip")
    # The parent already captures these exact owned CABs for original sprites,
    # audio and textures. Reuse that immutable invocation-local result when given.
    if cab_bundles is None:
        owners = original_cab_bundles(source, game, observe=True)
    else:
        owners = dict(cab_bundles)
        if not owners or any(not isinstance(key, str) or not isinstance(value, str) for key, value in owners.items()):
            raise BuildError("Retained original Shader CAB ownership is empty or invalid.")
        build_progress.event("prepare-items:campaign-shaders-bundles", len(owners), len(owners), "CABs",
            "Original owned bundle identities retained from the same preparation", status="reuse")
    write_json(cache / "original-cab-bundles.json", owners)
    print("shaders: recovering every original instruction bank and binding", flush=True)
    inventory = full_shaders.inventory(project, game, project / "QuestRecovery/original-asset-identities.json",
        owners, cache, bind_programs=True, **tools)
    if inventory.get("blockedShaderCount") or inventory.get("errors"):
        raise BuildError("Full native Shader recovery has unresolved banks; inspect " + str(cache / "original-shader-inventory.json"))
    producer = load(source, "tools/quest-shaders/produce.py", "tools/quest-shaders")
    overlay = cache_overlay(cache)
    if overlay.exists(): shutil.rmtree(overlay)
    manifest = producer.restore_project(project, cache / "original-shader-inventory.json", cache,
        overlay, preserved_sources=preserve_sources(project))
    if manifest.get("graphicsApi") != "Vulkan" or manifest.get("compilerPlatform") != "Vulkan":
        raise BuildError("Complete Campaign shaders do not target the player's Vulkan backend.")
    # Copy the same overlay files/metadata once, with a real existing-file count.
    def copy(source_path, target_path):
        result = shutil.copy2(source_path, target_path)
        copy_counter.add(1, Path(source_path).name)
        return result
    copy_count = len(manifest["programs"]) + 2 * len(manifest["shaders"])
    copy_counter = build_progress.Counter("prepare-items:campaign-shaders-copy", copy_count, "files")
    shutil.copytree(overlay / "Assets", project / "Assets", dirs_exist_ok=True, copy_function=copy)
    copy_counter.finish()
    publish = build_progress.Counter("prepare-items:campaign-shaders-publish", 2, "receipts")
    write_json(project / "Assets/QuestOriginalCampaign/campaign-shaders.json", manifest)
    publish.add(1, "campaign-shaders.json")
    write_json(project / "QuestCampaignEvidence/shader-reconstruction.json", {
        "schema": 1, "scope": "complete-original-shader-instructions", "shaderCount": len(manifest["shaders"]),
        "materialCount": len(manifest["materials"]), "nativeInventorySha256": digest(cache / "original-shader-inventory.json"),
        "compilerManifestSha256": digest(project / "Assets/QuestOriginalCampaign/campaign-shaders.json"),
        "androidShaderCompiled": False, "headsetPictureVerified": False})
    publish.add(1, "shader-reconstruction.json")
    publish.finish()
    print("shaders: staged " + str(len(manifest["shaders"])) + " native Shaders; Android compilation remains required", flush=True)
    return manifest
