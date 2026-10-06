#!/usr/bin/env python3
"""Re-export all known native materials using explicit IEEE non-finite metadata.

The complete source census remains immutable. This pass repairs an incomplete
JSON export without rescanning irrelevant sources or dropping malformed values.
"""
import argparse
from collections import Counter, defaultdict
import gc
import importlib.util
import json
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("native_material_audit", Path(__file__).with_name("audit.py"))
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


def export(root, output):
    import UnityPy
    summary = json.loads((output / "summary.json").read_text())
    files = json.loads((output / "serialized-files.json").read_text())
    shader_map = defaultdict(list)
    for s in json.loads((output / "shaders.json").read_text()):
        shader_map[(s["serializedFile"], s["pathId"])].append(s)
    sources = [s for s in summary["sources"] if s["types"].get("Material", 0)]
    counts, statuses, nonfinite = 0, Counter(), []
    temporary = output / "materials-complete.jsonl"
    with temporary.open("w") as stream:
        for index, source in enumerate(sources, 1):
            path = root / source["path"]
            assert AUDIT.sha256(path) == source["sha256"], "Native source changed during material export"
            env = UnityPy.load(str(path))
            per_source = 0
            for file in env.assets:
                key = source["path"] + "::" + str(file.name)
                assert key in files
                for obj in file.objects.values():
                    if obj.type.name != "Material": continue
                    m = AUDIT.material_metadata(obj, key)
                    m["shaderResolution"] = AUDIT.resolve_shader(m, files, shader_map)
                    statuses[m["shaderResolution"]["status"]] += 1
                    stream.write(json.dumps(m, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n")
                    counts += 1
                    per_source += 1
                    if m["nonFiniteValues"]:
                        nonfinite.append({"id": m["id"], "name": m["name"], "paths": m["nonFiniteValues"]})
            assert per_source == source["types"]["Material"], "Every material object in this source was exported"
            env = None
            file = None
            obj = None
            gc.collect()
            if index % 100 == 0 or index == len(sources):
                print(f"material metadata {index}/{len(sources)} sources; exported={counts}", flush=True)
    assert counts == summary["materialCount"]
    assert dict(statuses) == summary["shaderResolution"]
    # Keep the failed export as private evidence rather than erasing its status.
    original = output / "materials.jsonl"
    if original.exists(): original.rename(output / "materials-incomplete-first-export.jsonl")
    temporary.rename(original)
    AUDIT.write_json(output / "material-export.json", {"result": "PASS", "sourceCount": len(sources),
        "materialCount": counts, "shaderResolution": dict(statuses), "nonFiniteMaterials": nonfinite,
        "materialsSha256": AUDIT.sha256(original), "programSourceSha256": AUDIT.sha256(Path(__file__)),
        "metadataSourceSha256": AUDIT.sha256(Path(__file__).with_name("audit.py")),
        "note": "Explicit +Infinity/-Infinity/NaN metadata preserves malformed native values; raw object hashes retain exact IEEE provenance. Original whole-source census is unchanged."})
    print(f"exported {counts} actual materials; nonfinite={len(nonfinite)}")


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--game-data", type=Path, required=True)
    p.add_argument("--native-output", type=Path, required=True)
    args = p.parse_args()
    export(args.game_data, args.native_output)
