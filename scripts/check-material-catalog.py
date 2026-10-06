#!/usr/bin/env python3
"""Verify the tracked native material census and optional immutable source hashes."""
import argparse
from collections import Counter
import importlib.util
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("native_material_audit", ROOT / "tools/material-audit/audit.py")
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


def check(directory, native_output=None, game_data=None):
    assertions = 0
    def require(condition, description):
        nonlocal assertions
        assertions += 1
        if not condition:
            raise AssertionError(description)
    d = json.loads((directory / "catalog.json").read_text())
    require(d["schema"] == 1, "catalog schema")
    sources = {s["path"]: s for s in d["sources"]}
    require(len(sources) == d["sourceCount"], "all source identities unique and counted")
    require(sum(s["bytes"] for s in sources.values()) == d["sourceBytes"], "all source bytes accounted")
    require(sum(s["path"].endswith(".bundle") for s in sources.values()) == d["bundleCount"], "all bundle sources counted")
    require(sum(len(s["serializedFiles"]) for s in sources.values()) == d["serializedFileCount"], "all serialized files counted")
    require(d["errorCount"] == len(d["errors"]), "errors never silently dropped")
    require(d["objectScanComplete"] == (d["errorCount"] == 0), "object completion claim agrees with errors")
    require(d["shaderResolutionComplete"] == (set(d["shaderResolution"]) <= {"resolved"}), "shader completion does not hide unresolved/null references")
    require(AUDIT.sha256(directory / "material-index.jsonl") == d["compactMaterialIndexSha256"], "material index provenance")
    types = Counter()
    for source in sources.values():
        types.update(source["types"])
    require(types["Material"] == d["materialCount"], "no native Material object skipped")
    require(types["Shader"] == d["shaderObjectCount"], "no native Shader object skipped")
    counts, status, ids = Counter(), Counter(), set()
    with (directory / "material-index.jsonl").open() as stream:
        for line in stream:
            m = json.loads(line)
            require(m["id"] not in ids, "material source/file/path identity unique")
            ids.add(m["id"])
            source = m["id"].split("::", 1)[0]
            require(source in sources, "material source present in immutable manifest")
            counts[m["shader"]] += 1
            status[m["shaderResolution"]["status"]] += 1
            if m["shaderResolution"]["status"] != "resolved":
                require(bool(m["exclusions"]), "unresolved materials excluded")
    require(dict(status) == d["shaderResolution"], "every shader-resolution status counted")
    require(sum(counts.values()) == d["materialCount"], "every material index row counted")
    require(dict(counts) == {name: f["materialCount"] for name, f in d["families"].items()}, "every native material family counted")
    require(not d["catalogMissingSources"], "all catalog bundle locations present")
    require(not d["catalog"]["nonLocalBundleIds"], "catalog has no silently ignored nonlocal bundle location")
    for label, dlc in d["catalog"]["dlc"].items():
        require(bool(dlc["entryCount"]), "DLC group has native catalog entries: " + label)
        require(all(p in sources for p in dlc["bundlePaths"]), "DLC dependency closure fully scanned: " + label)
    if native_output:
        for name, expected in d["privateEvidenceSha256"].items():
            require(AUDIT.sha256(native_output / name) == expected, "private scan proof matches: " + name)
    if game_data:
        actual = {p.relative_to(game_data).as_posix() for p in AUDIT.source_paths(game_data)}
        require(actual == set(sources), "native source discovery matches complete catalog manifest")
        require(AUDIT.sha256(game_data / d["catalog"]["path"]) == d["catalog"]["sha256"], "actual addressable catalog hash")
        for path, source in sources.items():
            require(AUDIT.sha256(game_data / path) == source["sha256"], "actual immutable native source hash: " + path)
    print(f"Material catalog: {assertions} assertions; {len(ids)} materials, {len(sources)} sources, {len(counts)} families; errors={d['errorCount']}, shaderResolutionComplete={d['shaderResolutionComplete']}")
    return assertions


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--catalog-dir", type=Path, default=ROOT / "tools/material-audit")
    p.add_argument("--native-output", type=Path)
    p.add_argument("--game-data", type=Path)
    args = p.parse_args()
    subprocess.run([sys.executable, str(ROOT / "tools/material-audit/test_audit.py")], check=True)
    check(args.catalog_dir, args.native_output, args.game_data)


if __name__ == "__main__":
    main()
