#!/usr/bin/env python3
"""Reparse real root, base and DLC materials and exercise their reference vetoes."""
import argparse
import copy
from collections import defaultdict
import importlib.util
import json
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("native_material_audit", Path(__file__).with_name("audit.py"))
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


def verify(game_data, scan_output, report):
    import UnityPy
    files = json.loads((scan_output / "serialized-files.json").read_text())
    shader_path = scan_output / "shaders-with-programs.json"
    if not shader_path.exists(): shader_path = scan_output / "shaders.json"
    shaders = json.loads(shader_path.read_text())
    by_id = {s["id"]: s for s in shaders}
    shader_map = defaultdict(list)
    for shader in shaders:
        shader_map[(shader["serializedFile"], shader["pathId"])].append(shader)
    selected = {}
    with (scan_output / "materials.jsonl").open() as stream:
        for line in stream:
            row = json.loads(line)
            source = row["serializedFile"].split("::", 1)[0]
            family = row["shaderResolution"].get("name")
            buckets = []
            if source == "resources.assets": buckets.append("root-resources")
            if source.startswith("sharedassets"): buckets.append("root-sharedassets")
            if "dlc" in source.casefold(): buckets.append("direct-dlc-bundle")
            if family == "Amp_Basic_N_MRAO": buckets.append("native-high")
            if family == "Amp_Low/Amp_Basic_N_MRAO_Low": buckets.append("native-low")
            if "floor" in row["name"].casefold(): buckets.append("floor-material")
            for bucket in buckets:
                selected.setdefault(bucket, row)
    required = {"root-resources", "root-sharedassets", "direct-dlc-bundle", "native-high", "native-low", "floor-material"}
    assert set(selected) == required, "Real source sample missing; no portable replacement is allowed"
    assertions, controls, samples = 0, 0, []
    def require(ok, message):
        nonlocal assertions
        assertions += 1
        if not ok: raise AssertionError(message)
    def native_object(key, pathid):
        entry = files[key]
        env = UnityPy.load(str(game_data / entry["source"]))
        exact = [file for file in env.assets if str(file.name) == entry["name"]]
        require(len(exact) == 1, "Exact serialized file identity reparsed")
        return env, exact[0].objects[pathid]
    for label, m in selected.items():
        env, obj = native_object(m["serializedFile"], m["pathId"])
        actual = AUDIT.material_metadata(obj, m["serializedFile"])
        require(all(actual[k] == m[k] for k in actual), "Every real saved material field and raw hash reproduced")
        resolved = AUDIT.resolve_shader(actual, files, shader_map)
        require(resolved == m["shaderResolution"], "Every actual shaderPPtr resolves identically")
        require(resolved["status"] == "resolved", "Native sample shader resolved")
        target = by_id[resolved["targets"][0]]
        shader_env, shader_obj = native_object(target["serializedFile"], target["pathId"])
        reparsed = AUDIT.shader_metadata(shader_obj, target["serializedFile"])
        require(reparsed == target, "Actual native shader metadata/raw source object reproduced")
        bad = copy.deepcopy(actual)
        bad["shaderPPtr"]["m_FileID"] = len(files[bad["serializedFile"]]["externals"]) + 1
        require(AUDIT.resolve_shader(bad, files, shader_map)["status"] == "invalid-file-id", "Real invalid external index vetoed")
        controls += 1
        bad = copy.deepcopy(actual)
        bad["shaderPPtr"]["m_PathID"] = 2**63 - 1
        require(AUDIT.resolve_shader(bad, files, shader_map)["status"] == "unresolved", "Real missing shader object vetoed")
        controls += 1
        samples.append({"scope": label, "material": m["id"], "materialRawSha256": m["rawSha256"],
                        "shader": target["id"], "shaderRawSha256": target["rawSha256"]})
        del env, shader_env, obj, shader_obj
    AUDIT.write_json(report, {"result": "PASS", "assertions": assertions, "causalControls": controls,
                              "unitypy": UnityPy.__version__, "samples": samples,
                              "limits": "Actual native source/read/reference checks, not pixel/FPS or live material ownership proof."})
    print(f"Native material controls: {assertions} assertions/{controls} causal controls/{len(samples)} actual source scopes")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--game-data", type=Path, required=True)
    p.add_argument("--native-output", type=Path, required=True)
    p.add_argument("--report", type=Path, required=True)
    args = p.parse_args()
    verify(args.game_data, args.native_output, args.report)


if __name__ == "__main__":
    main()
