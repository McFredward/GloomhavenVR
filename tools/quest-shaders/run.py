#!/usr/bin/env python3
"""Compile exact Campaign shader banks and compare actual original D3D11 pixels.

The generated Quest project must already contain the production Editor gate.
The native reference executable is built separately, with no game callbacks.
Original asset bundles are read only and selected by captured native addresses.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from manifest import ValidationError, load, sha256, validate_receipt


ROOT = Path(__file__).resolve().parents[2]
METHOD = "GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation."


def _private_output(output: Path, project: Path | None = None):
    output = output.resolve()
    forbidden = (ROOT / "ressources", Path("/home/claw/gloomhaven_vr/ressources"))
    for path in forbidden:
        if output == path.resolve() or path.resolve() in output.parents:
            raise ValidationError("Shader validation may not write original game references.")
    if project is not None and (output == project.resolve() or output in project.resolve().parents):
        raise ValidationError("Shader output cannot contain its input project.")
    output.mkdir(parents=True, exist_ok=True)
    return output


def _unity(unity: Path, project: Path, output: Path, method: str, target: str, env=None):
    command = [str(unity.resolve()), "-batchmode", "-quit", "-buildTarget", target,
               "-projectPath", str(project.resolve()), "-executeMethod", method, "-logFile", str(output / (target + ".log"))]
    if sys.platform.startswith("linux"):
        command += ["-force-glcore"]
        if not (env or os.environ).get("DISPLAY"):
            launcher = shutil.which("xvfb-run")
            if launcher is None:
                raise ValidationError("Actual native shader coverage requires a graphics host; install Xvfb or provide DISPLAY.")
            command = [launcher, "-a", *command]
    result = subprocess.run(command, env=env, capture_output=True, text=True, timeout=86400 if method.endswith("Validate") else 1800)
    (output / (target + "-console.log")).write_text(result.stdout + result.stderr)
    if result.returncode:
        raise ValidationError("Actual Unity shader validation failed: " + str(output / (target + ".log")))


def compile_android(unity: Path, project: Path, manifest_path: Path, output: Path):
    manifest = load(manifest_path, project)
    output = _private_output(output, project)
    implementation = project / "Assets/Quest/Editor/QuestCampaignShaderValidation.cs"
    if not implementation.is_file():
        raise ValidationError("Prepared Quest project must contain the Campaign shader Editor gate.")
    environment = dict(os.environ, GHVR_QUEST_SHADER_MANIFEST=str(manifest_path.resolve()), GHVR_QUEST_SHADER_OUTPUT=str(output))
    _unity(unity, project, output, METHOD + "Validate", "Android", environment)
    receipt = json.loads((output / "android-compiler.json").read_text())
    validate_receipt(manifest, sha256(manifest_path), receipt)
    for bank in receipt["programs"]:
        name = bank.get("file")
        if not name or Path(name).name != name or sha256(output / name) != bank["glesSha256"]:
            raise ValidationError("Actual compiler bank bytes differ from its native receipt.")
    (output / "compiler-source.json").write_text(json.dumps({"gateSha256": sha256(implementation), "manifestSha256": sha256(manifest_path)}, indent=2) + "\n")
    return receipt


def build_candidates(unity: Path, project: Path, manifest_path: Path, output: Path):
    load(manifest_path, project)
    output = _private_output(output, project)
    environment = dict(os.environ, GHVR_QUEST_SHADER_MANIFEST=str(manifest_path.resolve()), GHVR_QUEST_SHADER_OUTPUT=str(output))
    _unity(unity, project, output, METHOD + "BuildReferenceCandidateBundle", "Win64", environment)
    bundle = output / "quest-campaign-shader-candidates"
    if not bundle.is_file() or bundle.stat().st_size == 0:
        raise ValidationError("Native candidate bundle is missing after actual build.")
    return bundle


def build_host(unity: Path, output: Path, color_space="Linear"):
    output = _private_output(output)
    project = output / "host-project"
    assets = project / "Assets"
    (assets / "Editor").mkdir(parents=True, exist_ok=True)
    (project / "Packages").mkdir(exist_ok=True)
    (project / "ProjectSettings").mkdir(exist_ok=True)
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
        "com.unity.modules.assetbundle": "1.0.0", "com.unity.modules.jsonserialize": "1.0.0",
        "com.unity.modules.animation": "1.0.0", "com.unity.modules.imageconversion": "1.0.0"}}) + "\n")
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    source = Path(__file__).parent / "UnityHost"
    host_sources = ("Editor/QuestShaderHostBuild.cs", "Runtime/QuestShaderReferenceProbe.cs", "Runtime/QuestSpriteReferenceOracle.cs")
    for path in host_sources:
        destination = assets / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source / path, destination)
    environment = dict(os.environ, GHVR_QUEST_SHADER_COLOR_SPACE=color_space)
    _unity(unity, project, output, "QuestShaderHostBuild.Build", "Win64", environment)
    executable = project / "Windows/QuestShaderReference.exe"
    if not executable.is_file():
        raise ValidationError("Native shader reference executable is missing.")
    (output / "host-source.json").write_text(json.dumps({"unityVersion": "2021.3.5f1", "colorSpace": color_space,
        "sources": {path: sha256(source / path) for path in host_sources},
        "executableSha256": sha256(executable)}, indent=2) + "\n")
    return executable


def _native_path(path: Path):
    absolute = path.resolve()
    return str(absolute) if os.name == "nt" else "Z:" + str(absolute).replace("/", "\\")


def validate_pixels(manifest, manifest_sha, receipt, output: Path, backend="Direct3D11"):
    if (receipt.get("schema") != 1 or receipt.get("sourceManifestSha256") != manifest_sha or
            receipt.get("unityVersion") != "2021.3.5f1" or receipt.get("graphicsDeviceType") != backend or
            not receipt.get("originalWindowsDxbcPixelsCompared") or not receipt.get("allCasesPassed") or receipt.get("errors") or
            receipt.get("androidMultiviewPixelsVerified") is not False or receipt.get("headsetPictureVerified") is not False):
        raise ValidationError("Original Windows/D3D11 pixel receipt is invalid or overstates its evidence.")
    expected = {(case["id"], feature) for case in manifest["renderCases"] for feature in ["baseline", *case["features"]]}
    actual = set()
    for picture in receipt.get("pictures", []):
        identity = (picture.get("caseId"), picture.get("feature"))
        if identity in actual or not picture.get("passed") or picture.get("differingPixels") != 0 or identity[1] == "baseline" and picture.get("referenceForegroundPixels", 0) < 16:
            raise ValidationError("Original native pixel hypothesis failed or is duplicated.")
        if identity[1] != "baseline" and picture.get("observedInputChangePixels", 0) < 4:
            raise ValidationError("Native feature fixture does not observe its claimed picture hypothesis.")
        actual.add(identity)
        for key in ("referenceFile", "candidateFile"):
            name = picture.get(key)
            if not isinstance(name, str) or Path(name).name != name or not (output / name).is_file():
                raise ValidationError("Native pixel proof bytes are missing or unsafe.")
    if actual != expected:
        raise ValidationError("Native pixel receipt lacks full declared shader/feature coverage.")
    controls = {(row.get("caseId"), row.get("defect")) for row in receipt.get("negativeControls", []) if row.get("rejected")}
    expected_controls = {(case["id"], "native-error-shader") for case in manifest["renderCases"]}
    expected_controls |= {(case["id"], "one-sided-texture-binding") for case in manifest["renderCases"] if "texture" in case["features"]}
    if controls != expected_controls:
        raise ValidationError("Native pixel proof lacks executable negative controls.")
    return receipt


def run_reference(executable: Path, manifest_path: Path, candidate_bundle: Path, output: Path, wine: Path | None = None, color_space="Linear"):
    manifest = load(manifest_path)
    output = _private_output(output)
    cases = []
    before = {}
    for row in manifest["renderCases"]:
        case = dict(row)
        for key in ("originalMaterial", "originalMesh"):
            original = row[key]
            paths = []
            for bundle in original["bundlePaths"]:
                path = Path(bundle["path"]).resolve()
                if sha256(path) != bundle["sha256"]:
                    raise ValidationError("Actual original reference bundle differs from captured provenance.")
                before[str(path)] = bundle["sha256"]
                paths.append(_native_path(path))
            case[key] = {**original, "bundlePaths": paths}
        cases.append(case)
    configuration = {"schema": 1, "sourceManifestSha256": sha256(manifest_path), "outputRoot": _native_path(output),
                     "candidateBundlePath": _native_path(candidate_bundle), "colorSpace": color_space,
                     "width": 256, "height": 256, "allowedByteError": 3, "cases": cases}
    config_path = output / "reference-config.json"
    config_path.write_text(json.dumps(configuration, indent=2) + "\n")
    command = [str(executable.resolve()), "-batchmode", "-force-d3d11", "-screen-fullscreen", "0", "-logFile", _native_path(output / "windows-player.log"), "--quest-shader-config", _native_path(config_path)]
    environment = dict(os.environ)
    if os.name != "nt":
        if wine is None:
            wine_path = shutil.which("wine64") or "/usr/lib/wine/wine64"
            wine = Path(wine_path)
        if not wine.is_file():
            raise ValidationError("Native original D3D11 reference requires Windows or local Wine.")
        environment.update(WINEPREFIX=str(output / "wine-prefix"), WINEDEBUG="-all")
        command.insert(0, str(wine))
        if not environment.get("DISPLAY"):
            xvfb = shutil.which("xvfb-run")
            if xvfb is None:
                raise ValidationError("Native original D3D11 reference requires an X display or xvfb-run.")
            command = [xvfb, "-a", *command]
    result = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=1800)
    (output / "windows-console.log").write_text(result.stdout + result.stderr)
    if result.returncode:
        raise ValidationError("Actual original D3D11 reference renderer failed: " + str(output / "windows-player.log"))
    receipt = json.loads((output / "windows-pixels.json").read_text())
    validate_pixels(manifest, sha256(manifest_path), receipt, output)
    if any(sha256(Path(path)) != hash_value for path, hash_value in before.items()):
        raise ValidationError("Original reference bundle bytes changed during native validation.")
    (output / "native-inputs.json").write_text(json.dumps({"originalBundles": before, "candidateBundleSha256": sha256(candidate_bundle),
                                                         "executableSha256": sha256(executable)}, indent=2) + "\n")
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("compile", "candidates", "host", "reference"))
    parser.add_argument("--unity", type=Path, default=Path("/home/claw/unity-2021.3.5/Editor/Unity"))
    parser.add_argument("--project", type=Path)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--candidate-bundle", type=Path)
    parser.add_argument("--executable", type=Path)
    parser.add_argument("--wine", type=Path)
    parser.add_argument("--color-space", choices=("Linear", "Gamma"), default="Linear")
    args = parser.parse_args()
    if args.mode in ("compile", "candidates") and (args.project is None or args.manifest is None):
        parser.error("This mode requires --project and --manifest.")
    if args.mode == "reference" and (args.executable is None or args.manifest is None or args.candidate_bundle is None):
        parser.error("Reference mode requires --executable, --manifest and --candidate-bundle.")
    try:
        if args.mode == "compile":
            receipt = compile_android(args.unity, args.project, args.manifest, args.output)
            print("Actual Android native banks: " + str(len(receipt["programs"])))
        elif args.mode == "candidates":
            print(build_candidates(args.unity, args.project, args.manifest, args.output))
        elif args.mode == "host":
            print(build_host(args.unity, args.output, args.color_space))
        else:
            receipt = run_reference(args.executable, args.manifest, args.candidate_bundle, args.output, args.wine, args.color_space)
            print("Original Windows/D3D11 pixel hypotheses: " + str(len(receipt["pictures"])))
    except (ValidationError, subprocess.TimeoutExpired, OSError, ValueError) as error:
        raise SystemExit(str(error)) from error


if __name__ == "__main__":
    main()
