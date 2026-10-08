"""Read-only recovered Unity script-reference census; no game data is copied."""
from __future__ import annotations

import argparse
import collections
import json
import re
from pathlib import Path

SCRIPT = re.compile(r"m_Script:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32})", re.I)
GUID = re.compile(r"^guid:\s*([0-9a-f]{32})$", re.M)


def concerned(row: dict) -> bool:
    return (
        row["assembly"] == "GH.Runtime.FirstPass.dll" and row["namespace"].startswith("RenderHeads.Media.AVProMovieCapture")
        or row["assembly"] == "Unity.Formats.Alembic.Runtime.dll"
        or row["assembly"] == "PhotonVoice.dll" and row["name"] == "WebRtcAudioDsp"
        or row["assembly"] == "InControl.dll" and row["name"] == "InControlManager"
        or row["assembly"] == "GH.Runtime.dll" and row["name"] in ("BoltVoiceBridge", "StandaloneLabelProvider", "HighShadersLabelProvider", "AssetBundleManager")
    )


def census(recovered: Path) -> dict:
    identities = json.loads((recovered / "QuestRecovery/original-script-identities.json").read_text(encoding="utf-8"))
    guids = {}
    for metadata in (recovered / "Assets/Plugins").glob("*.dll.meta"):
        found = GUID.search(metadata.read_text(encoding="utf-8"))
        if found:
            guids[metadata.name.removesuffix(".meta")] = found.group(1).lower()
    targets = {}
    for row in identities:
        if concerned(row):
            if row["assembly"] not in guids:
                raise ValueError("Relevant original script assembly has no recovered GUID: " + row["assembly"])
            key = (str(row["fileId"]), guids[row["assembly"]])
            if key in targets:
                raise ValueError("Ambiguous original script identity: " + str(key))
            targets[key] = row
    counts = collections.Counter()
    samples: dict = collections.defaultdict(list)
    inspected = 0
    for asset in sorted((recovered / "Assets").rglob("*")):
        if asset.suffix not in (".prefab", ".unity", ".asset"):
            continue
        inspected += 1
        for file_id, guid in SCRIPT.findall(asset.read_text(encoding="utf-8", errors="strict")):
            key = file_id, guid.lower()
            if key in targets:
                counts[key] += 1
                path = asset.relative_to(recovered).as_posix()
                if path not in samples[key] and len(samples[key]) < 20:
                    samples[key].append(path)
    return {
        "schema": 1,
        "scope": "exact-original-script-fileID-and-DLL-GUID-in-recovered-YAML",
        "evidenceLimit": "Serialized reference counts; native API/platform guards and dynamic construction require independent IL review.",
        "inspectedAssets": inspected,
        "targets": [dict(row, recoveredAssemblyGuid=key[1], serializedReferences=counts[key], sampleAssets=samples[key])
                    for key, row in sorted(targets.items(), key=lambda pair: (pair[1]["assembly"], pair[1]["namespace"], pair[1]["name"]))],
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recovered", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    source, output = args.recovered.resolve(), args.output.resolve()
    if output == source or source in output.parents:
        parser.error("Output must be outside the read-only recovered tree.")
    result = census(source)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"inspectedAssets": result["inspectedAssets"],
                      "serializedTargets": {row["namespace"] + "." + row["name"]: row["serializedReferences"]
                                             for row in result["targets"] if row["serializedReferences"]}}))


if __name__ == "__main__":
    main()
