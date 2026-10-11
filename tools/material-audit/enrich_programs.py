#!/usr/bin/env python3
"""Reparse every discovered Shader object for program identity metadata only."""
import argparse
from collections import defaultdict
import gc
import importlib.util
import json
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("native_material_audit", Path(__file__).with_name("audit.py"))
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


def enrich(root, output):
    import UnityPy
    original = json.loads((output / "shaders.json").read_text())
    groups = defaultdict(list)
    for row in original:
        groups[row["serializedFile"].split("::", 1)[0]].append(row)
    result = []
    for index, (source, rows) in enumerate(sorted(groups.items()), 1):
        env = UnityPy.load(str(root / source))
        exact_files = {str(file.name): file for file in env.assets}
        for previous in rows:
            name = previous["serializedFile"].split("::", 1)[1]
            obj = exact_files[name].objects[previous["pathId"]]
            current = AUDIT.shader_metadata(obj, previous["serializedFile"])
            assert all(current[k] == previous[k] for k in previous), "Shader source/metadata changed during program enrichment"
            result.append(current)
        env = None
        exact_files = None
        obj = None
        gc.collect()
        if index % 10 == 0 or index == len(groups):
            print(f"program identities {index}/{len(groups)} sources", flush=True)
    assert len(result) == len(original)
    AUDIT.write_json(output / "shaders-with-programs.json", result)
    # The original extraction remains immutable. Compact publication explicitly
    # prefers this additional signature-only evidence file.
    AUDIT.write_json(output / "program-enrichment.json", {
        "result": "PASS", "sourceCount": len(groups), "shaderObjectCount": len(result),
        "originalShadersSha256": AUDIT.sha256(output / "shaders.json"),
        "enrichedShadersSha256": AUDIT.sha256(output / "shaders-with-programs.json"),
        "programSourceSha256": AUDIT.sha256(Path(__file__)),
        "limits": "Compressed programblob identity is not semantic equivalence across differing blobs or pixel/FPS proof."})


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--game-data", type=Path, required=True)
    p.add_argument("--native-output", type=Path, required=True)
    args = p.parse_args()
    enrich(args.game_data, args.native_output)
