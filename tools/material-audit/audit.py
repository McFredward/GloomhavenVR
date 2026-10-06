#!/usr/bin/env python3
"""Read-only native material census. Never export textures, meshes or shader code.

Use the private UnityPy interpreter. One source environment is loaded at a time;
shader references resolve from explicit serialized-file identities after scanning,
without UnityPy loading an unbounded dependency graph.
"""
import argparse
from collections import Counter, defaultdict
import gc
import hashlib
import json
import math
from pathlib import Path
import re
import sys

REPO = Path(__file__).resolve().parents[2]
SCHEMA = 1
REVIEWED_FAMILIES = {
    "Amp_Basic_N_MRAO": "HIGH", "Amp_Low/Amp_Basic_N_MRAO_Low": "LOW",
    "Amp_Basic_WallFade": "HIGH_WALL", "Amp_Low/Amp_Basic_WallFade_Low": "LOW_WALL",
    "Amp_Basic": "HIGH_BASIC", "Amp_Low/Amp_Basic_Low": "LOW_BASIC",
    "Standard": "STANDARD", "Legacy Shaders/Diffuse": "LEGACY_DIFFUSE"}


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            h.update(block)
    return h.hexdigest()


def normalized_file(name):
    # Addressable externals use archive:/CAB-X/CAB-X. Retain the actual
    # serialized basename, not a bundle filename or a globally unique pathID.
    return str(name).replace("\\", "/").rsplit("/", 1)[-1].casefold()


def source_paths(root):
    root_files = [p for p in root.iterdir() if p.is_file() and
                  (p.suffix == ".assets" or p.name == "globalgamemanagers" or
                   re.fullmatch(r"level\d+", p.name))]
    builtins = [p for p in (root / "Resources").rglob("*") if p.is_file()]
    bundles = list((root / "StreamingAssets" / "aa").rglob("*.bundle"))
    return sorted(set(root_files + builtins + bundles))


def resolve_shader(material, files, shaders):
    ptr = material["shaderPPtr"]
    pid, fid = ptr["m_PathID"], ptr["m_FileID"]
    if pid == 0:
        return {"status": "null-shader", "targets": []}
    origin = files[material["serializedFile"]]
    if fid == 0:
        targets = shaders.get((material["serializedFile"], pid), [])
    elif 1 <= fid <= len(origin["externals"]):
        external = origin["externals"][fid - 1]
        target_name = normalized_file(external["path"])
        targets = [s for (key, pathid), rows in shaders.items()
                   if pathid == pid and files[key]["normalizedName"] == target_name
                   for s in rows]
    else:
        return {"status": "invalid-file-id", "targets": []}
    if not targets:
        return {"status": "unresolved", "targets": []}
    # Repeated exact copies in different bundles are equivalent. A collision
    # with different bytes/name is ambiguous and must never become an allowlist.
    signatures = {(s["rawSha256"], s["name"]) for s in targets}
    if len(signatures) != 1:
        return {"status": "ambiguous", "targets": [s["id"] for s in targets]}
    return {"status": "resolved", "name": targets[0]["name"],
            "rawSha256": targets[0]["rawSha256"], "targets": [s["id"] for s in targets]}


def queue_value(material, shader):
    q = material["customRenderQueue"]
    if q >= 0:
        return q
    queues = {s["tags"].get("QUEUE", s["tags"].get("Queue", ""))
              for s in shader["subShaders"]}
    if len(queues) != 1:
        return None
    value = queues.pop() or "Geometry"
    match = re.fullmatch(r"(Background|Geometry|AlphaTest|Transparent|Overlay)([+-]\d+)?", value)
    if not match:
        return None
    return {"Background": 1000, "Geometry": 2000, "AlphaTest": 2450,
            "Transparent": 3000, "Overlay": 4000}[match[1]] + int(match[2] or 0)


def exclusion_reasons(material, shader):
    if material.get("nonFiniteValues"):
        return ["non-finite-material-value"]
    if material["shaderResolution"]["status"] != "resolved":
        return ["shader-" + material["shaderResolution"]["status"]]
    name = shader["name"]
    if name not in REVIEWED_FAMILIES:
        return ["unreviewed-native-family"]
    reasons = []
    queue = queue_value(material, shader)
    if queue is None:
        reasons.append("unknown-render-queue")
    elif queue > 2500:
        reasons.append("transparent-or-overlay-queue")
    floats = dict(material["properties"].get("m_Floats", []))
    floats.update(dict(material["properties"].get("m_Ints", [])))
    keywords = set(material["validKeywords"])
    for keyword in ("_ENABLE_ANIM", "_ADDVERTEXANIM_ON", "_USE_TEXTURE_EMISSION",
                    "_USEEMISSIVEMAP_ON", "_DIFFUSE_EMISSIVE_ON_ON", "_FRESNEL_ON_ON",
                    "_ADVANCED_EMISSION", "_MOSSTEXTURE_ON_ON", "_MOSSTEXTURE_NOISE_ON_ON"):
        if keyword in keywords:
            reasons.append("active-keyword-" + keyword)
    for key in ("_AddVertexAnim", "_UseEmissiveMap", "_Diffuse_Emissive_On",
                "_EmissionMap", "_UseTextureEmission", "_Fresnel_On", "_AdvancedEmission",
                "_MossTexture_ON", "_MossTexture_Noise_ON"):
        if floats.get(key, 0) != 0:
            reasons.append("active-" + key)
    if material["disabledShaderPasses"]:
        reasons.append("disabled-native-passes")
    if name == "Amp_Basic":
        for key in ("_ToggleDissolve", "_Cutout_VertexPos_Influence"):
            if floats.get(key, 0) != 0:
                reasons.append("active-" + key)
    if name == "Standard":
        defaults = {"_Mode": 0, "_SrcBlend": 1, "_DstBlend": 0, "_ZWrite": 1}
        for key, default in defaults.items():
            value = floats.get(key, default)
            if (key == "_Mode" and value != 0) or (key != "_Mode" and value != default):
                reasons.append("unsupported-standard-" + key)
        for keyword in ("_EMISSION", "_DETAIL_MULX2", "_PARALLAXMAP", "_ALPHATEST_ON", "_ALPHABLEND_ON", "_ALPHAPREMULTIPLY_ON"):
            if keyword in keywords:
                reasons.append("active-keyword-" + keyword)
        emission = dict(material["properties"].get("m_Colors", [])).get("_EmissionColor", {})
        if any(emission.get(c, 0) != 0 for c in ("r", "g", "b")):
            reasons.append("nonzero-emission-color")
    return reasons


def catalog_coverage(root):
    # Reuse the project's exact compact Addressables decoder. No filename-only
    # association, guessing DLC ownership, or recovered project is required.
    sys.path.insert(0, str(REPO / "tools/quest-recovery"))
    from catalog import decode_catalog, bundle_closure
    path = root / "StreamingAssets/aa/catalog.json"
    decoded = decode_catalog(json.loads(path.read_text(encoding="utf-8-sig")))
    prefix = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/"
    bundle_paths = set()
    other_bundle_ids = []
    dlc_assets = defaultdict(list)
    for loc in decoded["locations"]:
        internal = loc["internalId"].replace("\\", "/")
        if internal.endswith(".bundle"):
            if internal.startswith(prefix):
                bundle_paths.add("StreamingAssets/aa/" + internal[len(prefix):])
            else:
                other_bundle_ids.append(internal)
        # Retain actual catalog path groups, not an inferred owned licence.
        parts = internal.split("/")
        directories = [x for x in parts[:-1] if re.fullmatch(r"DLC[_-][A-Za-z0-9]+", x, re.IGNORECASE)]
        if internal.startswith("Assets/") and "dlc" in internal.casefold():
            label = directories[0] if directories else "DLC-labelled-paths-outside-product-root"
            dlc_assets[label].append(loc["index"])
    dlc = {}
    for label, indices in sorted(dlc_assets.items()):
        paths = bundle_closure(decoded, indices)
        dlc[label] = {"entryCount": len(indices), "bundlePaths": paths}
    return {"path": str(path.relative_to(root)), "sha256": sha256(path),
            "keyCount": len(decoded["keys"]), "locationCount": len(decoded["locations"]),
            "bundlePaths": sorted(bundle_paths), "nonLocalBundleIds": other_bundle_ids, "dlc": dlc}


def shader_metadata(obj, file_key):
    d = obj.read()
    parsed = getattr(d, "m_ParsedForm", None)
    if parsed is None:
        raise ValueError("Shader has no parsed form; do not infer name from binary strings")
    props = [{"name": p.m_Name, "type": int(p.m_Type), "flags": int(p.m_Flags),
              "attributes": list(p.m_Attributes or []),
              "default": [getattr(p, f"m_DefValue_{i}_") for i in range(4)],
              "defaultTexture": p.m_DefTexture.m_DefaultName,
              "textureDimension": int(p.m_DefTexture.m_TexDim)}
             for p in parsed.m_PropInfo.m_Props]
    blob = bytes(d.compressedBlob)
    return {"id": file_key + ":" + str(obj.path_id), "serializedFile": file_key,
            "pathId": obj.path_id, "rawSha256": hashlib.sha256(obj.get_raw_data()).hexdigest(),
            "name": parsed.m_Name, "properties": props,
            "programBlobSha256": hashlib.sha256(blob).hexdigest(), "programBlobBytes": len(blob),
            "programPlatforms": list(d.platforms), "programOffsets": d.offsets,
            "programCompressedLengths": d.compressedLengths,
            "programDecompressedLengths": d.decompressedLengths,
            "keywordNames": list(parsed.m_KeywordNames), "keywordFlags": list(parsed.m_KeywordFlags),
            "fallbackName": parsed.m_FallbackName,
            "subShaders": [{"tags": dict(s.m_Tags.tags), "passCount": len(s.m_Passes),
                             "passes": [{"name": p.m_Name, "useName": p.m_UseName,
                                         "type": int(p.m_Type), "tags": dict(p.m_Tags.tags)}
                                        for p in s.m_Passes]} for s in parsed.m_SubShaders]}


def material_metadata(obj, file_key):
    d = obj.read_typetree()
    nonfinite = []
    def normalize(value, path):
        if isinstance(value, float) and not math.isfinite(value):
            nonfinite.append(path)
            return "NaN" if math.isnan(value) else "+Infinity" if value > 0 else "-Infinity"
        if isinstance(value, dict):
            return {k: normalize(v, path + "/" + str(k)) for k, v in value.items()}
        if isinstance(value, (tuple, list)):
            return [normalize(v, path + "/" + str(i)) for i, v in enumerate(value)]
        return value
    properties = normalize(d["m_SavedProperties"], "m_SavedProperties")
    return {"id": file_key + ":" + str(obj.path_id), "serializedFile": file_key,
            "pathId": obj.path_id, "rawSha256": hashlib.sha256(obj.get_raw_data()).hexdigest(),
            "name": d["m_Name"], "shaderPPtr": d["m_Shader"],
            "validKeywords": d.get("m_ValidKeywords", d.get("m_ShaderKeywords", "").split()),
            "invalidKeywords": d.get("m_InvalidKeywords", []),
            "customRenderQueue": d.get("m_CustomRenderQueue", -1),
            "lightmapFlags": d.get("m_LightmapFlags"), "doubleSidedGI": d.get("m_DoubleSidedGI"),
            "enableInstancingVariants": d.get("m_EnableInstancingVariants"),
            "stringTagMap": d.get("stringTagMap", []),
            "disabledShaderPasses": d.get("disabledShaderPasses", []),
            "properties": properties, "nonFiniteValues": nonfinite}


def write_json(path, value):
    path.write_text(json.dumps(value, sort_keys=True, indent=2, allow_nan=False) + "\n")


def scan(root, out):
    import UnityPy
    program_hash = sha256(Path(__file__))
    out.mkdir(parents=True, exist_ok=True)
    paths = source_paths(root)
    catalog = catalog_coverage(root)
    write_json(out / "catalog-coverage.json", catalog)
    sources, files, shaders, materials, errors = [], {}, defaultdict(list), [], []
    total = len(paths)
    for index, path in enumerate(paths, 1):
        relative = path.relative_to(root).as_posix()
        source = {"path": relative, "bytes": path.stat().st_size, "sha256": sha256(path),
                  "status": "scanned", "serializedFiles": [], "types": {}}
        env = None
        try:
            env = UnityPy.load(str(path))
            assets = env.assets
            if not assets:
                raise ValueError("Content source contains no serialized files")
            for file in assets:
                key = relative + "::" + str(file.name)
                if key in files:
                    raise ValueError("Duplicate serialized file identity: " + key)
                files[key] = {"source": relative, "name": str(file.name),
                              "normalizedName": normalized_file(file.name),
                              "externals": [{"path": x.path, "guid": x.guid.hex(), "type": x.type}
                                            for x in file.externals]}
                source["serializedFiles"].append(key)
                types = Counter(o.type.name for o in file.objects.values())
                source["types"] = dict(Counter(source["types"]) + types)
                for obj in file.objects.values():
                    try:
                        if obj.type.name == "Shader":
                            shaders[(key, obj.path_id)].append(shader_metadata(obj, key))
                        elif obj.type.name == "Material":
                            materials.append(material_metadata(obj, key))
                    except Exception as error:
                        source["status"] = "partial-error"
                        errors.append({"source": relative, "serializedFile": key, "pathId": obj.path_id,
                                       "type": obj.type.name, "error": type(error).__name__ + ": " + str(error)[:1000]})
        except Exception as error:
            source["status"] = "load-error"
            errors.append({"source": relative, "error": type(error).__name__ + ": " + str(error)[:1000]})
        sources.append(source)
        # Loop variables otherwise keep the final serialized reader alive after
        # deleting env. Clear those references as well before collection.
        env = None
        assets = None
        file = None
        obj = None
        # Serialized files/readers contain cycles; collect before the next source.
        gc.collect()
        if index % 100 == 0 or index == total:
            print(f"scanned {index}/{total}: materials={len(materials)} shaders={sum(map(len, shaders.values()))} errors={len(errors)}", flush=True)
    shader_rows = [s for rows in shaders.values() for s in rows]
    shader_by_id = {s["id"]: s for s in shader_rows}
    family = defaultdict(lambda: {"materialCount": 0, "shaderObjects": set(), "sources": set(),
                                  "propertyKeys": set(), "keywords": set(), "queues": Counter(),
                                  "exclusions": Counter(), "candidateCount": 0})
    statuses = Counter()
    for material in materials:
        resolved = resolve_shader(material, files, shaders)
        material["shaderResolution"] = resolved
        statuses[resolved["status"]] += 1
        shader = shader_by_id[resolved["targets"][0]] if resolved["status"] == "resolved" else None
        reasons = exclusion_reasons(material, shader)
        material["exclusionReasons"] = reasons
        material["candidateStatus"] = "native-family-review-candidate" if not reasons else "excluded"
        name = resolved.get("name", "<" + resolved["status"] + ">")
        f = family[name]
        f["materialCount"] += 1
        f["candidateCount"] += not reasons
        f["shaderObjects"].update(resolved["targets"])
        f["sources"].add(files[material["serializedFile"]]["source"])
        for rows in material["properties"].values():
            f["propertyKeys"].update(pair[0] for pair in rows)
        f["keywords"].update(material["validKeywords"])
        f["queues"][str(queue_value(material, shader) if shader else None)] += 1
        f["exclusions"].update(reasons)
    families = {}
    for name, f in sorted(family.items()):
        families[name] = {key: (sorted(value) if isinstance(value, set) else dict(value) if isinstance(value, Counter) else value)
                          for key, value in f.items()}
    actual_bundles = {s["path"] for s in sources if s["path"].endswith(".bundle")}
    summary = {"schema": SCHEMA, "unitypy": UnityPy.__version__, "scanProgramSha256": program_hash,
               "sourceRoot": str(root.resolve()), "sourceCount": len(sources), "bundleCount": len(actual_bundles),
               "sourceBytes": sum(s["bytes"] for s in sources), "serializedFileCount": len(files),
               "materialCount": len(materials), "shaderObjectCount": len(shader_rows),
               "shaderResolution": dict(statuses), "objectScanComplete": not errors,
               "shaderResolutionComplete": set(statuses) <= {"resolved"},
               "errorCount": len(errors), "errors": errors,
               "catalogMissingSources": sorted(set(catalog["bundlePaths"]) - actual_bundles),
               "uncataloguedBundles": sorted(actual_bundles - set(catalog["bundlePaths"])),
               "catalog": catalog, "sources": sources, "families": families,
               "limits": "Material/shader source census only; not scene reachability, renderer/static eligibility, pixel parity or FPS proof. Runtime MPBs, global state and scripts remain independent vetoes."}
    write_json(out / "summary.json", summary)
    write_json(out / "serialized-files.json", files)
    write_json(out / "shaders.json", shader_rows)
    with (out / "materials.jsonl").open("w") as stream:
        for material in materials:
            stream.write(json.dumps(material, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n")
    print(json.dumps({k: summary[k] for k in ("sourceCount", "bundleCount", "serializedFileCount", "materialCount", "shaderObjectCount", "shaderResolution", "errorCount")}), flush=True)
    return 0 if not errors and statuses.keys() <= {"resolved", "null-shader"} and not summary["catalogMissingSources"] else 2


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--game-data", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    return scan(args.game_data.resolve(), args.output.resolve())


if __name__ == "__main__":
    sys.exit(main())
