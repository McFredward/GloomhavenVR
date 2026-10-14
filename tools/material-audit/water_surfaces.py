#!/usr/bin/env python3
"""Read-only census of native renderers using the complete native water family.

Uses the existing complete catalog to select renderer-bearing sources and resolve
material pointers by serialized-file identity, never names or texture contents.
The output contains metadata only; no game meshes, textures or shader payloads
are exported. The game references and the tracked catalog are never modified.
"""
import argparse
from collections import Counter, defaultdict
import gc
import hashlib
import json
from pathlib import Path
import re
import sys


WATER_SHADERS = {
    "VFX/Water_Shd", "VFX/Water_Shd_Low",
    "VFX/Water_Shd_Trans", "VFX/Water_Shd_Trans_Low",
}
CONSUMED_PROPERTIES = {
    "_Color_Tint", "_Normal_Map", "_NormalTilings", "_Smoothness", "_DetailOpacityBaseNormalStr",
}


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


def normalized_file(path):
    return path.replace("\\", "/").rsplit("/", 1)[-1].casefold()


def game_object_path(assets_file, pointer):
    if pointer.m_FileID != 0:
        raise ValueError("Renderer GameObject has an unexpected external owner")
    names, seen = [], set()
    game_object = assets_file.objects[pointer.m_PathID].read()
    while game_object is not None:
        names.append(game_object.m_Name)
        transforms = [c.component for c in game_object.m_Component
                      if c.component.m_FileID == 0 and c.component.m_PathID != 0
                      and assets_file.objects[c.component.m_PathID].type.name
                      in ("Transform", "RectTransform")]
        if len(transforms) != 1:
            raise ValueError("Water renderer ancestry has no exact local Transform")
        transform = assets_file.objects[transforms[0].m_PathID].read()
        parent = transform.m_Father
        if parent.m_PathID == 0:
            break
        if parent.m_FileID != 0 or parent.m_PathID in seen:
            raise ValueError("Water renderer ancestry has an external parent or cycle")
        seen.add(parent.m_PathID)
        transform = assets_file.objects[parent.m_PathID].read()
        owner = transform.m_GameObject
        if owner.m_FileID != 0:
            raise ValueError("Water renderer ancestor has an unexpected external owner")
        game_object = assets_file.objects[owner.m_PathID].read()
    return list(reversed(names))


def verify_water_materials(game_data, catalog, by_source, property_output=None):
    import UnityPy
    source_identity = {s["path"]: s for s in catalog["sources"]}
    verified_materials = []
    for source, material_rows in by_source.items():
        expected = source_identity[source]
        path = game_data / source
        if path.stat().st_size != expected["bytes"] or sha256(path) != expected["sha256"]:
            raise ValueError("Native water material source changed: " + source)
        environment = UnityPy.load(str(path))
        for material in material_rows:
            serialized, path_id = material["id"].rsplit("::", 1)[1].rsplit(":", 1)
            files = [f for f in environment.assets if f.name == serialized]
            if len(files) != 1:
                raise ValueError("Native water material has no exact serialized owner")
            obj = files[0].objects[int(path_id)]
            data = obj.read()
            if (obj.type.name != "Material" or data.m_Name != material["name"]
                    or hashlib.sha256(obj.get_raw_data()).hexdigest() != material["rawSha256"]
                    or data.m_Shader.m_FileID != material["shaderPPtr"]["m_FileID"]
                    or data.m_Shader.m_PathID != material["shaderPPtr"]["m_PathID"]):
                raise ValueError("Native water material object changed: " + material["id"])
            verified_materials.append(material["id"])
            if property_output is not None:
                raw = obj.read_typetree()
                props = raw["m_SavedProperties"]
                saved = dict(props.get("m_Colors", [])) | dict(props.get("m_Floats", []))
                property_output[material["id"]] = {
                    "customRenderQueue": raw["m_CustomRenderQueue"],
                    "savedNative": {k: v for k, v in saved.items() if k in CONSUMED_PROPERTIES},
                }
        del environment
    return verified_materials


def native_shader_declarations(game_data, catalog):
    import UnityPy
    source_identity = {s["path"]: s for s in catalog["sources"]}
    result = {}
    for shader in catalog["shaderIdentityTable"]:
        if shader["name"] not in WATER_SHADERS:
            continue
        source, object_key = shader["id"].rsplit("::", 1)
        serialized, path_id = object_key.rsplit(":", 1)
        path = game_data / source
        if sha256(path) != source_identity[source]["sha256"]:
            raise ValueError("Native water shader source changed: " + source)
        environment = UnityPy.load(str(path))
        files = [f for f in environment.assets if f.name == serialized]
        if len(files) != 1:
            raise ValueError("Native water shader has no exact serialized owner")
        obj = files[0].objects[int(path_id)]
        parsed = obj.read().m_ParsedForm
        if (parsed.m_Name != shader["name"]
                or hashlib.sha256(obj.get_raw_data()).hexdigest() != shader["rawSha256"]):
            raise ValueError("Native water shader identity changed")
        result[shader["name"]] = sorted(p.m_Name for p in parsed.m_PropInfo.m_Props
                                        if p.m_Name in CONSUMED_PROPERTIES)
        del environment
    if set(result) != WATER_SHADERS:
        raise ValueError("Incomplete native water shader declaration census")
    return result


def compact_coverage(census):
    """Keep exact native inputs suitable for runtime coverage fixtures, not an allowlist."""
    surfaces = []
    for row in census["waterRenderers"]:
        kind = "terrain" if row["terrainRoot"] else (
            "dlc-sluice" if row["ancestry"][0] == "DLC_TH_SC08_SluiceGate_BAY" else "decorative-pool")
        surfaces.append({
            k: row[k] for k in ("source", "sourceSha256", "serializedFile", "rendererPathId",
                                "rendererType", "ancestry", "slot", "material", "materialName", "shader",
                                "binding", "materialAssetGUID", "materialAssetPath") if k in row
        } | {"classification": kind, "replaceWaterFilm": True})
    return {
        "schema": 1, "nativeSourceCatalogSha256": census["catalogSha256"],
        "nativeMaterialIndexSha256": census["materialIndexSha256"],
        "nativeAddressablesCatalogSha256": census["addressablesCatalogSha256"],
        "census": {k: census[k] for k in ("sourceCount", "rendererCount", "materialLoaderCount", "waterSlotCount")},
        "waterShaderFamilies": sorted(WATER_SHADERS),
        "materials": [m | census.get("nativeMaterialProperties", {}).get(m["id"], {})
                      | {"declaredConsumedProperties": census.get("nativeShaderDeclarations", {}).get(m["shader"], [])}
                      for m in census["waterMaterials"]], "surfaces": surfaces,
        "scope": "All exact native water-film shaders, including terrain, decorative pools and DLC sluice water. Solid basin/stone materials are not film candidates.",
        "limits": census["limits"],
    }


def scan(game_data, catalog_dir, output, coverage_output=None):
    import UnityPy
    catalog_path = catalog_dir / "catalog.json"
    index_path = catalog_dir / "material-index.jsonl"
    catalog = json.loads(catalog_path.read_text())
    if not catalog["objectScanComplete"] or catalog["errorCount"]:
        raise ValueError("Complete error-free source census required")
    if sha256(index_path) != catalog["compactMaterialIndexSha256"]:
        raise ValueError("Material identity index no longer matches its source catalog")
    materials = [json.loads(line) for line in index_path.open()]
    water = [m for m in materials if m["shader"] in WATER_SHADERS]
    if not water or any(m["shaderResolution"]["status"] != "resolved" for m in water):
        raise ValueError("Every water-family material must have an exact resolved shader")
    actual_families = {m["shader"] for m in materials if "Water_Sh" in (m["shader"] or "")}
    if actual_families != WATER_SHADERS:
        raise ValueError("Native water shader family changed; inspect the new exact identities")
    by_pointer = defaultdict(list)
    by_source = defaultdict(list)
    for material in water:
        source, obj = material["id"].rsplit("::", 1)
        serialized, path_id = obj.rsplit(":", 1)
        by_pointer[(normalized_file(serialized), int(path_id))].append(material)
        by_source[source].append(material)
    # Native terrain prefabs deliberately serialize no renderer materials. Their
    # MaterialLoader supplies AssetGUIDs asynchronously. Resolve those original
    # GUIDs through the catalog's exact Material location and dependency closure.
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "quest-recovery"))
    from catalog import decode_catalog, bundle_closure
    addressables_path = game_data / "StreamingAssets/aa/catalog.json"
    addressables = decode_catalog(json.loads(addressables_path.read_text(encoding="utf-8-sig")))
    guid_materials = {}
    for key, indices in zip(addressables["keys"], addressables["buckets"]):
        if not isinstance(key, str) or not re.fullmatch(r"[0-9a-f]{32}", key):
            continue
        matches = []
        for index in indices:
            location = addressables["locations"][index]
            if location["resourceType"].get("m_ClassName") != "UnityEngine.Material":
                continue
            materials_in_closure = [m for source in bundle_closure(addressables, [index])
                                    for m in by_source.get(source, [])]
            if not materials_in_closure:
                continue
            if len(materials_in_closure) != 1:
                raise ValueError("Water material AssetGUID has an ambiguous exact bundle closure")
            matches.append({"material": materials_in_closure[0], "assetPath": location["internalId"],
                            "locationIndex": index})
        if matches:
            if len(matches) != 1:
                raise ValueError("Water material AssetGUID has multiple Material locations")
            guid_materials[key] = matches[0]
    if {r["material"]["id"] for r in guid_materials.values()} != {m["id"] for m in water}:
        raise ValueError("Not every native water material has an exact original Addressables GUID")
    material_properties = {}
    verified_materials = verify_water_materials(game_data, catalog, by_source, material_properties)
    shader_declarations = native_shader_declarations(game_data, catalog)
    source_rows = [s for s in catalog["sources"]
                   if s["types"].get("MeshRenderer", 0) + s["types"].get("SkinnedMeshRenderer", 0)]
    renderers, checked, records, loader_count = 0, [], [], 0
    for ordinal, source in enumerate(source_rows, 1):
        path = game_data / source["path"]
        if path.stat().st_size != source["bytes"] or sha256(path) != source["sha256"]:
            raise ValueError("Native renderer source changed: " + source["path"])
        environment = UnityPy.load(str(path))
        counts = Counter()
        loader_script_ids = defaultdict(set)
        for obj in environment.objects:
            if obj.type.name == "MonoScript" and obj.read().m_Name == "MaterialLoader":
                loader_script_ids[obj.assets_file.name].add(obj.path_id)
        for obj in environment.objects:
            if obj.type.name != "MonoBehaviour" or not loader_script_ids[obj.assets_file.name]:
                continue
            header = obj.read()
            script = header.m_Script
            if script.m_FileID != 0 or script.m_PathID not in loader_script_ids[obj.assets_file.name]:
                continue
            loader_count += 1
            data = obj.read_typetree()
            for loader in data["LoadersData"]:
                renderer_pointer = loader["Renderer"]
                if renderer_pointer["m_FileID"] != 0:
                    raise ValueError("MaterialLoader renderer is not an exact local object")
                renderer = obj.assets_file.objects.get(renderer_pointer["m_PathID"])
                if renderer is None:
                    # Empty native loader entries cannot introduce a water surface.
                    if any(m.get("m_AssetGUID") in guid_materials for m in loader["MaterialReferences"]):
                        raise ValueError("Water MaterialLoader has a missing exact Renderer")
                    continue
                for slot, reference in enumerate(loader["MaterialReferences"]):
                    joined = guid_materials.get(reference.get("m_AssetGUID"))
                    if joined is None:
                        continue
                    material = joined["material"]
                    ancestry = game_object_path(obj.assets_file, renderer.read().m_GameObject)
                    records.append({
                        "source": source["path"], "sourceSha256": source["sha256"],
                        "serializedFile": obj.assets_file.name, "rendererPathId": renderer.path_id,
                        "rendererType": renderer.type.name, "ancestry": ancestry, "slot": slot,
                        "binding": "MaterialLoader.Addressables.AssetGUID", "loaderPathId": obj.path_id,
                        "materialAssetGUID": reference["m_AssetGUID"], "materialAssetPath": joined["assetPath"],
                        "materialLocationIndex": joined["locationIndex"], "material": material["id"],
                        "materialName": material["name"], "materialRawSha256": material["rawSha256"],
                        "shader": material["shader"],
                        "oldTerrainNameMatch": "terrain_water" in ancestry[-1].casefold(),
                        "terrainRoot": any(n.casefold().startswith("terrain_") for n in ancestry),
                    })
        for obj in environment.objects:
            if obj.type.name not in ("MeshRenderer", "SkinnedMeshRenderer"):
                continue
            counts[obj.type.name] += 1
            renderers += 1
            data = obj.read()
            for slot, pointer in enumerate(data.m_Materials):
                if pointer.m_PathID == 0:
                    continue
                if pointer.m_FileID == 0:
                    name = obj.assets_file.name
                elif 1 <= pointer.m_FileID <= len(obj.assets_file.externals):
                    name = obj.assets_file.externals[pointer.m_FileID - 1].path
                else:
                    raise ValueError("Native renderer has an invalid material external index")
                matches = by_pointer.get((normalized_file(name), pointer.m_PathID), [])
                if not matches:
                    continue
                if len({(m["rawSha256"], m["shader"]) for m in matches}) != 1:
                    raise ValueError("Ambiguous water material pointer; no filename-only fallback")
                ancestry = game_object_path(obj.assets_file, data.m_GameObject)
                records.append({
                    "source": source["path"], "sourceSha256": source["sha256"],
                    "serializedFile": obj.assets_file.name, "rendererPathId": obj.path_id,
                    "rendererType": obj.type.name, "ancestry": ancestry, "slot": slot,
                    "binding": "Renderer.materialPPtr",
                    "material": matches[0]["id"], "materialName": matches[0]["name"],
                    "materialRawSha256": matches[0]["rawSha256"], "shader": matches[0]["shader"],
                    "oldTerrainNameMatch": "terrain_water" in ancestry[-1].casefold(),
                    "terrainRoot": any(n.casefold().startswith("terrain_") for n in ancestry),
                })
        if any(counts[k] != source["types"].get(k, 0) for k in ("MeshRenderer", "SkinnedMeshRenderer")):
            raise ValueError("Native renderer source object census changed: " + source["path"])
        checked.append({"path": source["path"], "sha256": source["sha256"], "renderers": sum(counts.values())})
        del environment, counts
        gc.collect()
        if ordinal % 20 == 0 or ordinal == len(source_rows):
            print(f"Water census: {ordinal}/{len(source_rows)} sources; {renderers} renderers; {len(records)} water slots", flush=True)
    result = {
        "schema": 1, "result": "PASS", "unitypy": UnityPy.__version__,
        "catalogSha256": sha256(catalog_path), "materialIndexSha256": sha256(index_path),
        "sourceCount": len(checked), "rendererCount": renderers,
        "materialLoaderCount": loader_count, "addressablesCatalogSha256": sha256(addressables_path),
        "addressableWaterMaterialCount": len(guid_materials),
        "verifiedNativeMaterials": verified_materials,
        "nativeMaterialProperties": material_properties, "nativeShaderDeclarations": shader_declarations,
        "waterMaterials": water, "waterSlotCount": len(records), "sources": checked,
        "shaderCounts": dict(sorted(Counter(r["shader"] for r in records).items())),
        "waterRenderers": records,
        "limits": "Read-only installed native renderer/material provenance; not live procedural placement, headset pixels, or FPS proof.",
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n")
    if coverage_output is not None:
        coverage_output.parent.mkdir(parents=True, exist_ok=True)
        coverage_output.write_text(json.dumps(compact_coverage(result), indent=2, sort_keys=True) + "\n")
    print(f"Water census PASS: {len(checked)} exact sources/{renderers} renderers/{len(water)} materials/{len(records)} water slots")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", type=Path, required=True)
    parser.add_argument("--catalog-directory", type=Path, default=Path(__file__).parent)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--coverage-output", type=Path)
    args = parser.parse_args()
    scan(args.game_data, args.catalog_directory, args.output, args.coverage_output)


if __name__ == "__main__":
    main()
