"""Stage all recovered Campaign assets with exact original object provenance.

Recovery itself is bounded and resumable. This step accepts only its completed,
hashed checkpoint, writes a fresh private Unity project, preserves witnessed
B614 GUID contracts, and emits the complete original catalog association. It
does not call an asset export an Android build or a playable game.
"""
import importlib.util
import json
from pathlib import Path
import re
import shutil
import sys

from storage import BuildError, write_json

RECOVERY_TOOLS = Path(__file__).resolve().parents[1] / "quest-recovery"


def _modules():
    if str(RECOVERY_TOOLS) not in sys.path:
        sys.path.append(str(RECOVERY_TOOLS))
    import canonical_guids
    import full_catalog
    import export_identity
    import serialized_repairs
    import tmp_shaders
    import recover
    spec = importlib.util.spec_from_file_location("quest_recovery_full_startup", RECOVERY_TOOLS / "startup.py")
    startup = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(startup)
    return canonical_guids, full_catalog, export_identity, serialized_repairs, tmp_shaders, recover, startup


def _catalog_roots(canonical_startup, full_manifest):
    """Existing typed original catalog aliases witness old bundle roots."""
    manifest = Path(canonical_startup) / "Assets/QuestOriginalStartup/startup-addressables.json"
    old = json.loads(manifest.read_text())
    if old["catalogSha256"] != full_manifest["catalogSha256"]:
        raise BuildError("Canonical startup and full recovery use different original catalogs.")
    by_location = {}
    for row in full_manifest["entries"]:
        if row["status"] == "associated":
            by_location.setdefault(row["originalLocationIndex"], []).append(row)
    roots = []
    for row in old["entries"]:
        if row["status"] != "associated":
            continue
        incoming = [target for target in by_location.get(row["entryIndex"], [])
                    if target["resourceTypeName"] == row["resourceTypeName"] and
                    target["originalAssetPath"].casefold() == row["originalAssetPath"].casefold()]
        if len(incoming) == 1:
            roots.append((incoming[0]["recoveredGuid"], row["recoveredGuid"],
                          "original-typed-catalog-location:" + str(row["entryIndex"])))
    return roots


def stage(source, game_data, output, tmp_archive, *, canonical_project=None,
          canonical_startup=None, managed_types, cab_bundles, unitypy=None):
    """Return a full asset manifest/report; modify only the fresh output tree.

    ``source`` is bundle_recovery.py's completed project. ``canonical_project``
    is its previous read-only full core export; ``canonical_startup`` is B614's
    verified staging tree. Managed metadata and CAB bundle mapping are paths to
    actual locally recovered inventories. All 13 original scene indices remain.
    """
    canonical, catalogs, identities_module, layouts, tmp, recover, startup = _modules()
    source, game_data, output = [Path(path).resolve() for path in (source, game_data, output)]
    read_only = [source, game_data]
    read_only.extend(Path(path).resolve() for path in (canonical_project, canonical_startup) if path is not None)
    if output.exists() or any(root == output or root in output.parents or output in root.parents for root in read_only):
        raise BuildError("Full asset staging requires fresh private output outside read-only recovery/game inputs.")
    checkpoint = source / "quest-full-recovery-progress.json"
    progress = json.loads(checkpoint.read_text())
    if progress.get("schema") != 1 or progress.get("assetsRecovered") is not True:
        raise BuildError("Full Campaign staging requires all original catalog bundle batches to finish.")
    catalog_path = game_data / "StreamingAssets/aa/catalog.json"
    if recover.sha256(catalog_path) != progress["catalogSha256"]:
        raise BuildError("Original game catalog changed after full recovery.")
    types = json.loads(Path(managed_types).read_text())
    owners = json.loads(Path(cab_bundles).read_text())
    rows = progress["identities"]
    original_catalog = catalogs.associate(catalog_path, source, rows, owners, types)
    if canonical_project is not None:
        if canonical_startup is None:
            raise BuildError("A previous canonical project requires its actual startup provenance report.")
        roots = _catalog_roots(canonical_startup, original_catalog)
        guid_proof = canonical.witness(canonical_project, source, rows, roots)
    else:
        import canonical_contracts
        guid_proof = canonical_contracts.witness(game_data, source, rows, unitypy)
    output.mkdir(parents=True)
    copied = []
    for row in progress["files"]:
        relative = row["path"]
        if relative.startswith("Assets/Resources/srdebugger/"):
            continue
        original, target = startup.safe_path(source, relative), startup.safe_path(output, relative)
        startup.verify_copy(original, target, row["sha256"])
        copied.append({"path": relative, "sha256": row["sha256"]})
    # Retain managed type metadata for exact DLL script identities; the original
    # assemblies themselves are verified against the player's installed files.
    for path in (output / "Assets/Plugins").glob("*.dll"):
        original = game_data / "Managed" / path.name
        if not original.is_file() or recover.sha256(path) != recover.sha256(original):
            raise BuildError("Recovered full project lost original managed assembly bytes: " + path.name)
    (output / "QuestRecovery").mkdir(exist_ok=True)
    shutil.copyfile(managed_types, output / "QuestRecovery/managed-types.json")
    original_metadata = Path(canonical_project) if canonical_project is not None else source
    old_identity = original_metadata / "QuestRecovery/original-script-identities.json"
    if old_identity.is_file():
        shutil.copyfile(old_identity, output / "QuestRecovery/original-script-identities.json")
    # All runtime-loaded rule/text inputs stay on disk; desktop Addressables
    # bundles are replaced by Unity's actual Android content build downstream.
    rules = game_data / "StreamingAssets/Rulebase"
    if rules.is_dir():
        shutil.copytree(rules, output / "Assets/StreamingAssets/Rulebase")
    for name in ("GloomData.dat", "Apparance", "Procedures"):
        original = game_data / "StreamingAssets" / name
        target = output / "Assets/StreamingAssets" / name
        if original.is_file():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, target)
        elif original.is_dir():
            shutil.copytree(original, target)
    rows = canonical.apply(output, rows, guid_proof)
    original_objects = identities_module.object_index(rows)
    layout_report = layouts.restore(game_data, output, original_objects, unitypy)
    manifest = catalogs.associate(catalog_path, output, rows, owners, types)
    folder = output / "Assets/QuestOriginalCampaign"
    folder.mkdir(parents=True)
    write_json(folder / "campaign-addressables.json", manifest)
    index, paths = startup.asset_index(output)
    scripts, plugins = startup.script_index(output, types)
    selected = set(paths)
    restored_tmp = tmp.restore(output, original_metadata, selected, tmp_archive)
    restored_paths = {row["assetPath"] for row in restored_tmp}
    bindings = startup.binding_manifest(output, selected, scripts, plugins)
    write_json(folder / "script-bindings.json", bindings)
    script_origins = json.loads(old_identity.read_text()) if old_identity.is_file() else []
    if isinstance(script_origins, dict):
        script_origins = script_origins.get("identities", [])
    script_audit = recover.audit_script_bindings(output, types, script_origins)
    references = recover.audit_asset_references(output)
    scene_settings = (output / "ProjectSettings/EditorBuildSettings.asset").read_text()
    scenes = re.findall(r"^\s+path: (Assets/.+\.unity)\s*$", scene_settings, re.M)
    scene_objects = identities_module.object_index(rows)
    scene_rows = []
    for index, relative in enumerate(scenes):
        original = [obj for obj in scene_objects.values() if obj["path"] == relative]
        if not original or {obj["collection"] for obj in original} != {"level" + str(index)} or len({obj["guid"] for obj in original}) != 1:
            raise BuildError("Original Campaign scene identity/order is unproven: " + relative)
        scene_rows.append({"index": index, "path": relative, "guid": original[0]["guid"], "originalCollection": "level" + str(index)})
    write_json(folder / "campaign-scenes.json", {"schema": 1, "scenes": scene_rows})
    shader_rows = [{"path": relative, "name": re.search(r'Shader\s+"([^\"]+)"', (output / relative).read_text())[1],
                    "status": "official-compatible-TMP-source" if relative in restored_paths else "unresolved-original-dummy",
                    "originalShaderFidelity": False}
                   for relative in sorted(selected) if relative.endswith(".shader")]
    if canonical_startup is not None:
        old_report = json.loads((Path(canonical_startup) / "quest-startup-report.json").read_text())
    else:
        old_report = {"sourceFingerprint": progress["sourceFingerprint"],
                      "sourceBuilderFingerprint": startup.builder_fingerprint(progress["sourceInventory"]),
                      "originalBuildScenes": [{"index": index, "path": path} for index, path in enumerate(scenes)]}
    report = {"schema": 1, "target": "campaign", "fullGameReady": False,
              "sourceFingerprint": old_report["sourceFingerprint"],
              "sourceBuilderFingerprint": old_report["sourceBuilderFingerprint"],
              "recoveryReceiptSha256": recover.sha256(checkpoint), "selectedScenes": scenes,
              "originalBuildScenes": old_report["originalBuildScenes"],
              "startupAddressablesManifest": "Assets/QuestOriginalCampaign/campaign-addressables.json",
              "campaignAddressablesManifest": "Assets/QuestOriginalCampaign/campaign-addressables.json",
              "scriptBindingsManifest": "Assets/QuestOriginalCampaign/script-bindings.json",
              "managedScriptBindings": script_audit, "missingReferences": references,
              "unresolvedAddressables": [row for row in manifest["entries"] if row["status"] not in ("associated", "serialized-value-location-excluded")],
              "shaders": shader_rows, "officialTmpRestorations": restored_tmp, "originalDerivedFiles": copied,
              "serializedLayoutRestoration": layout_report, "canonicalGuidRestoration": {
                  "assetCount": len(guid_proof["mappings"]), "originalObjectCount": guid_proof["mappedOriginalObjectCount"],
                  "rejectedWitnessCount": len(guid_proof["rejected"])},
              "readiness": {"originalSceneClosureStaged": len(scenes) == 13,
                            "fullOriginalCatalogRecovered": True, "unityImportVerified": False,
                            "androidPlayerBuilt": False, "faithfulGraphicsVerified": False,
                            "playableCampaignVerified": False},
              "limits": ["Asset recovery is not a hardware or rendering validation.",
                         "Original custom shader instruction streams require complete portable shader reconstruction."]}
    report["files"] = [{"path": path.relative_to(output).as_posix(), "sha256": recover.sha256(path), "size": path.stat().st_size}
                       for path in sorted(output.rglob("*")) if path.is_file()]
    write_json(output / "quest-startup-report.json", report)
    write_json(output / "quest-campaign-report.json", report)
    return report
