#!/usr/bin/env python3
"""Create a diagnostic native-asset slice from a genuinely recovered model.

This strips gameplay callbacks and physics exclusively for the labelled probe.
It is not a playable or presentation-equivalent game export. Original geometry,
skin, rig, avatar, animation clips and available textures stay byte-identical.
"""
import argparse
from pathlib import Path
import re
import shutil
import sys

from recover import RecoveryError, GUID_PATTERN, YAML_EXTENSIONS, sha256, write_json

POINTER = re.compile(r"\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32}),\s*type:\s*\d+\}")
BLOCK = re.compile(r"(?m)^--- !u!(\d+) &(-?\d+)[^\n]*\n")
VISUAL_COMPONENTS = {1, 4, 23, 33, 95, 111, 137, 205, 224}


def strip_callbacks(text, prefab):
    matches = list(BLOCK.finditer(text))
    if not matches:
        return text, []
    removed = []
    retained = [text[:matches[0].start()]]
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        kind, identity = int(match[1]), match[2]
        if kind == 114 or (prefab and kind not in VISUAL_COMPONENTS):
            removed.append({"classId": kind, "fileId": identity})
        else:
            retained.append(text[match.start():end])
    result = "".join(retained)
    for entry in removed:
        identity = re.escape(entry["fileId"])
        result = re.sub(r"(?m)^\s+- component: \{fileID: " + identity + r"\}\n", "", result)
        result = re.sub(r"(?m)^\s+- \{fileID: " + identity + r"\}\n", "", result)
        result = re.sub(r"\{fileID: " + identity + r"\}", "{fileID: 0}", result)
    # Unity native controllers expect an empty array, not an empty scalar.
    result = re.sub(r"(?m)^(  m_StateMachineBehaviours):\s*\n(?=  \S)", r"\1: []\n", result)
    return result, removed


def create_slice(project, prefab, output):
    project = Path(project).resolve()
    prefab = (project / prefab).resolve()
    output = Path(output).resolve()
    if not prefab.is_file() or project not in prefab.parents or prefab.suffix != ".prefab":
        raise RecoveryError("Select an existing recovered visual prefab inside --project.")
    if output == project or project in output.parents or output in project.parents:
        raise RecoveryError("Probe slice output must be separate from the immutable recovered project.")
    if output.exists() and any(output.iterdir()):
        raise RecoveryError("Probe slice output must be empty; choose a fresh output.")
    guids = {}
    for metadata in (project / "Assets").rglob("*.meta"):
        match = GUID_PATTERN.search(metadata.read_text(encoding="utf-8", errors="replace"))
        if match:
            asset = metadata.with_name(metadata.name[:-5])
            if asset.is_file():
                if match[1] in guids and guids[match[1]] != asset:
                    raise RecoveryError("Recovered project has duplicate dependency GUID: " + match[1])
                guids[match[1]] = asset
    output.mkdir(parents=True, exist_ok=True)
    pending = [prefab]
    handled = set()
    records, changes, missing, shaders = [], [], [], []
    while pending:
        source = pending.pop()
        if source in handled:
            continue
        handled.add(source)
        if source.suffix.lower() in (".cs", ".dll", ".asmdef", ".asmref"):
            raise RecoveryError("Portable probe attempted to include managed game code: " + str(source))
        relative = Path("Resources/quest-original-model.prefab") if source == prefab else Path("NativeDependencies") / source.relative_to(project / "Assets")
        destination = output / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        transformed = False
        if source.suffix in YAML_EXTENSIONS:
            text = source.read_text(encoding="utf-8")
            text, removed = strip_callbacks(text, source.suffix == ".prefab")
            if removed:
                transformed = True
                changes.append({"asset": relative.as_posix(), "removedNativeOrGameplayComponents": removed})
            for match in list(POINTER.finditer(text)):
                fid, guid = int(match[1]), match[2]
                if guid.startswith("0000000000000000"):
                    continue
                dependency = guids.get(guid)
                if dependency is None:
                    if abs(fid) in (4300000, 9000000, 9100000):
                        raise RecoveryError("Essential mesh/avatar/controller dependency is missing: " + guid)
                    missing.append({"asset": relative.as_posix(), "fileId": fid, "guid": guid,
                                    "probeDisposition": "null-unavailable-optional-reference"})
                    text = text.replace(match[0], "{fileID: 0}")
                    transformed = True
                else:
                    pending.append(dependency)
            destination.write_text(text, encoding="utf-8")
        else:
            shutil.copy2(source, destination)
        metadata = source.with_name(source.name + ".meta")
        if not metadata.is_file():
            raise RecoveryError("Dependency is missing its original GUID metadata: " + str(source))
        meta_text = metadata.read_text(encoding="utf-8")
        meta_text = re.sub(r"(?m)^(  assetBundleName:).*$", r"\1", meta_text)
        destination.with_name(destination.name + ".meta").write_text(meta_text, encoding="utf-8")
        if source.suffix == ".shader" and "DummyShaderTextExporter" in source.read_text():
            shaders.append(relative.as_posix())
        records.append({"source": source.relative_to(project).as_posix(), "destination": relative.as_posix(),
                        "sourceSha256": sha256(source), "destinationSha256": sha256(destination),
                        "transformedForProbe": transformed})
    model = output / "Resources/quest-original-model.prefab"
    native = model.read_text()
    if "SkinnedMeshRenderer:" not in native or "Animator:" not in native:
        raise RecoveryError("Selected prefab does not contain the required original animated skin/rig.")
    if "m_Script:" in native:
        raise RecoveryError("Probe still contains a gameplay MonoBehaviour.")
    report = {"schema": 1, "scope": "diagnostic-owned-animated-asset-only", "playableGame": False,
              "originalPrefab": prefab.relative_to(project).as_posix(), "resourceName": "quest-original-model",
              "files": sorted(records, key=lambda x: x["destination"]), "probeChanges": changes,
              "missingOriginalReferences": missing, "shaderPlaceholders": sorted(shaders),
              "originalGeometrySkinRigAvatarAndClipsRetained": True,
              "gameplayCallbacksIncluded": False, "physicsIncluded": False,
              "requiresProbeMaterialConversion": bool(shaders),
              "originalShaderFidelity": False, "unityImportVerified": False,
              "animationEvents": "Original clip events retained; probe Animator must set fireEvents=false."}
    write_json(output / "quest-probe-assets-report.json", report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True)
    parser.add_argument("--prefab", required=True)
    parser.add_argument("--output-assets", required=True)
    args = parser.parse_args()
    try:
        value = create_slice(args.project, args.prefab, args.output_assets)
        print("[Quest probe assets] Original animated asset closure:", len(value["files"]), "files.")
        print("[Quest probe assets] Shader conversion gaps:", len(value["shaderPlaceholders"]),
              "optional unresolved references:", len(value["missingOriginalReferences"]))
        return 0
    except (RecoveryError, OSError, ValueError) as error:
        print("[Quest probe assets] FAILED:", error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
