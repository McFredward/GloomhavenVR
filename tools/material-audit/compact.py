#!/usr/bin/env python3
"""Publish compact source metadata; retain complete property values privately."""
import argparse
from collections import Counter, defaultdict
import importlib.util
import json
from pathlib import Path

SPEC = importlib.util.spec_from_file_location("native_material_audit", Path(__file__).with_name("audit.py"))
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


def compact(source, output):
    summary = json.loads((source / "summary.json").read_text())
    catalog_path = source / "catalog-coverage-normalized.json"
    if catalog_path.exists(): summary["catalog"] = json.loads(catalog_path.read_text())
    shader_path = source / "shaders-with-programs.json"
    if not shader_path.exists(): shader_path = source / "shaders.json"
    shaders = json.loads(shader_path.read_text())
    by_id = {s["id"]: s for s in shaders}
    output.mkdir(parents=True, exist_ok=True)
    counts, gates, signatures = Counter(), defaultdict(Counter), defaultdict(Counter)
    distributions = defaultdict(lambda: defaultdict(Counter))
    tex_st = defaultdict(lambda: defaultdict(Counter))
    root_counts = defaultdict(Counter)
    candidates = Counter()
    index_path = output / "material-index.jsonl"
    with (source / "materials.jsonl").open() as stream, index_path.open("w") as out:
        for line in stream:
            m = json.loads(line)
            resolved = m["shaderResolution"]
            family = resolved.get("name", "<" + resolved["status"] + ">")
            s = by_id[resolved["targets"][0]] if resolved["status"] == "resolved" else None
            # Reclassify using the current review policy, without rereading any
            # game source or pretending this is new object-scan evidence.
            reasons = AUDIT.exclusion_reasons(m, s)
            counts[family] += 1
            gates[family].update(reasons)
            candidates[family] += not reasons
            source_name = m["serializedFile"].split("::", 1)[0]
            root_counts[family]["addressableBundle" if source_name.endswith(".bundle") else "rootOrBuiltIn"] += 1
            signature = {kind: sorted(key for key, _ in rows) for kind, rows in m["properties"].items()}
            signatures[family][json.dumps(signature, sort_keys=True, separators=(",", ":"))] += 1
            for kind in ("m_Floats", "m_Ints"):
                for key, value in m["properties"].get(kind, []):
                    if isinstance(value, (int, float)):
                        distributions[family][key][value] += 1
            for key, value in m["properties"].get("m_TexEnvs", []):
                st = tuple(value[field][axis] for field in ("m_Scale", "m_Offset") for axis in ("x", "y"))
                tex_st[family][key][st] += 1
            out.write(json.dumps({"id": m["id"], "name": m["name"], "rawSha256": m["rawSha256"],
                                  "shader": family, "shaderPPtr": m["shaderPPtr"],
                                  "shaderResolution": resolved, "exclusions": reasons},
                                 sort_keys=True, separators=(",", ":")) + "\n")
    for name, f in summary["families"].items():
        f["exclusions"] = dict(gates[name])
        f["candidateCount"] = candidates[name]
        f["sourceKindCounts"] = dict(root_counts[name])
        f["savedPropertySchemas"] = [{"keys": json.loads(key), "count": count}
                                     for key, count in signatures[name].most_common()]
        f["numericProperties"] = {key: {"count": sum(values.values()), "min": min(values), "max": max(values),
                                        "distinctValues": len(values), "nonzeroCount": sum(n for v, n in values.items() if v != 0),
                                        "frequentValues": values.most_common(8)}
                                  for key, values in sorted(distributions[name].items())}
        f["textureST"] = {key: {"count": sum(values.values()), "distinctValues": len(values),
                                "componentMin": [min(v[i] for v in values) for i in range(4)],
                                "componentMax": [max(v[i] for v in values) for i in range(4)],
                                "frequentValues": values.most_common(4)}
                           for key, values in sorted(tex_st[name].items())}
        variants = defaultdict(list)
        for shader in shaders:
            if shader["name"] == name:
                variants[shader["rawSha256"]].append(shader)
        f["shaderVariants"] = [{"rawSha256": raw_hash,
                                 "programBlobSha256": rows[0].get("programBlobSha256"),
                                 "programBlobBytes": rows[0].get("programBlobBytes"),
                                 "keywordNames": rows[0].get("keywordNames"),
                                 "objects": [s["id"] for s in rows],
                                 "properties": rows[0]["properties"], "subShaders": rows[0]["subShaders"]}
                                for raw_hash, rows in sorted(variants.items())]
    summary.pop("sourceRoot", None)
    summary["objectScanComplete"] = summary["errorCount"] == 0
    summary["shaderResolutionComplete"] = set(summary["shaderResolution"]) <= {"resolved"}
    summary["privateEvidenceSha256"] = {name: AUDIT.sha256(source / name) for name in
                                         ("summary.json", "serialized-files.json", "shaders.json", "materials.jsonl", "catalog-coverage.json")}
    if shader_path.name != "shaders.json":
        for name in ("shaders-with-programs.json", "program-enrichment.json"):
            summary["privateEvidenceSha256"][name] = AUDIT.sha256(source / name)
    if (source / "material-export.json").exists():
        summary["privateEvidenceSha256"]["material-export.json"] = AUDIT.sha256(source / "material-export.json")
        summary["nonFiniteMaterials"] = json.loads((source / "material-export.json").read_text())["nonFiniteMaterials"]
    if catalog_path.exists():
        summary["privateEvidenceSha256"][catalog_path.name] = AUDIT.sha256(catalog_path)
    summary["compactMaterialIndexSha256"] = AUDIT.sha256(index_path)
    summary["classificationSourceSha256"] = AUDIT.sha256(Path(__file__).with_name("audit.py"))
    summary["candidateMeaning"] = "Native family/serialized feature candidate only. Not permission to replace a runtime renderer; unsupported active MPB/global features, world provenance, gameplay/dynamic state and source ownership remain independent vetoes."
    AUDIT.write_json(output / "catalog.json", summary)
    print("compact material index", sum(counts.values()), "families", len(counts), "native candidates", sum(candidates.values()))


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--native-output", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    compact(args.native_output, args.output)


if __name__ == "__main__":
    main()
