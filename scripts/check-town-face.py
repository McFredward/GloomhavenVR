#!/usr/bin/env python3
"""Compile production facial binding, anatomical gaze, shared playback and lifecycle cases and run them inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files.
Explicit fixture boundaries are documented in town-face-runtime/Boundaries.cs.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Production binding drift: expected one occurrence of {before!r}, got {source.count(before)}")
    return source.replace(before, after, 1)


def sources(root):
    base = root / "src/GloomhavenVR/WorldUI/TownServices"
    names = ["TownServiceFace.cs", "TownServiceFaceMotion.cs", "TownServiceFaceRig.cs", "TownServiceFaceAttention.cs", "TownServiceFaceSpeech.cs", "TownServiceActivityMotion.cs"]
    names += ["TownServiceMotionClips.cs", "TownServiceMotionClips.Data.cs"]
    bound = {name: (base / name).read_text() for name in names}
    bound["RemoteTownFaces.cs"] = (root / "src/GloomhavenVR/Net/Remote/RemoteTownFaces.cs").read_text()
    bound["TownFaceTypes.cs"] = (root / "src/GloomhavenVR/Net/TownFaceState.cs").read_text().split("/// <summary>Additive80:")[0]
    bound["TownActivityTypes.cs"] = (root / "src/GloomhavenVR/Net/TownActivityState.cs").read_text().split("internal struct TownActivityState")[0]
    return bound, {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}


def mutations():
    return [
        ("no-rig-apply", "TownServiceFaceRig.cs", "if (!Ready) return;", "if (Ready) return;", "head rotation follows bounded pose"),
        ("unchanged-weights", "TownServiceFaceRig.cs", "_weightsApplied && weight == _lastWeights[shape]", "_weightsApplied && weight < -1f", "unchanged weights do not dirty every LOD"),
        ("head-accumulation", "TownServiceFaceRig.cs", "_head.localRotation = _sampledHead", "_head.localRotation = _head.localRotation", "body base restored before next facial sample"),
        ("no-blink", "TownServiceFaceMotion.cs", "return t < .075f ? Mathf.SmoothStep(0f, 1f, t / .075f) : 1f - Mathf.SmoothStep(0f, 1f, (t - .075f) / .145f);", "return 0f;", "complete blink reaches closed lids"),
        ("wrong-eye-convergence", "TownServiceFaceMotion.cs", "focus - right", "focus - left", "eyes converge on common nearby target"),
        ("silent-mouth", "TownServiceFaceMotion.cs", "state.Cue == 0 ? 0f : Weight(mouth.x)", "Weight(mouth.x)", "silent NPC cannot jaw flap"),
        ("stale-reorder", "RemoteTownFaces.cs", "if (peer.HasHistory && (!Newer(state.Sequence, peer.Latest.Sequence) || state.Clock < peer.Latest.Clock)) return;\n        TownFaceState visible", "if (peer.HasHistory && (false || state.Clock < peer.Latest.Clock)) return;\n        TownFaceState visible", "older samples cannot replace new cue"),
        ("retire-network-stall", "RemoteTownFaces.cs", "peer.HasSample = false; peer.Suspended = true;", "peer.Retire(peer.Latest.Epoch); peer.Latest = default; peer.HasHistory = false; peer.HasSample = false; peer.Suspended = true;", "same live epoch resumes after temporary network stall"),
        ("fast-resurrection", "RemoteTownFaces.cs", " || peer.Suspended ||", " ||", "fast packet cannot revive suspended peer"),
        ("tombstone-capacity", "RemoteTownFaces.cs", "if (oldest == 0) return;", "if (oldest >= 0) return;", "retired tombstone capacity permits new participant"),
        ("retired-presence", "RemoteTownFaces.cs", "if (peer.Retired(state.Epoch)) return;", "", "retired recovery presence cannot restore old process"),
        ("no-target-scan", "TownServiceFaceAttention.cs", "if (Time.unscaledTime < _nextChoice) return null;", "", "no-target retry does not scan or raycast at render frequency"),
        ("epoch-crossing", "RemoteTownFaces.cs", "peer.Latest.Epoch != state.Epoch) return;", "false) return;", "old process cannot mutate new epoch voice"),
        ("stale-face", "RemoteTownFaces.cs", "elapsed > NetProtocol.StaleTimeoutSeconds", "elapsed > float.MaxValue", "stale facial stream expires"),
        ("gaze-through-wall", "TownServiceFaceAttention.cs", " || !Unobstructed(root, eye, point)", "", "native scenery blocks gaze election"),
        ("observer-elects-viewer", "TownServiceFace.cs", "if (author)", "if (author || !author)", "remote ignores observer headset"),
        ("fixed-work-gaze", "TownServiceFace.cs", "_workFocus != null ? _workFocus.position : _root.TransformPoint(TownServiceActivityMotion.RestFocus(_service))", "_root.TransformPoint(TownServiceActivityMotion.RestFocus(_service))", "work gaze uses current post-activity contact"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-face")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--bundle-only", action="store_true", help="Run only actual-prefab binding checks and their focused negatives")
    parser.add_argument("--bundle", type=Path, help="Optional Linux final-asset bundle for actual prefab binding checks")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    args = parser.parse_args()
    if args.bundle_only and not args.bundle: parser.error("--bundle-only requires --bundle")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-face-runtime"
    if not args.unity.is_file():
        parser.error("Unity 2021.3.5 is required; pass --unity")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    bound, hashes = sources(args.source_root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes}, indent=2) + "\n")
    bundle_hash = None
    if args.bundle:
        if not args.bundle.is_file(): parser.error("--bundle must name the completed Linux validation bundle")
        bundle_hash = hashlib.sha256(args.bundle.read_bytes()).hexdigest()
        (run / "bundle-evidence.json").write_text(json.dumps({"path": str(args.bundle.resolve()),
            "bytes": args.bundle.stat().st_size, "sha256": bundle_hash}, indent=2) + "\n")
    manifest = {"result": str(run / "results.txt"), "cases": []}
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls:
        if args.bundle_only:
            variants += [
                ("asset-optical-frame", "TownServiceFaceRig.cs", "Quaternion frame = OpticalRotation;", "Quaternion frame = _root.rotation;", "actual moving head and eyes converge on elevated side target"),
                ("asset-head-reset", "TownServiceFaceRig.cs", "_head.localRotation = _sampledHead", "_head.localRotation = _head.localRotation", "reset restores sampled imported Head before body clip"),
                ("asset-blink-binding", "TownServiceFaceRig.cs", "binding.Shape == shape && binding.Renderer != null", "binding.Shape == shape && shape != 0 && binding.Renderer != null", "runtime weight reaches"),
            ]
        else: variants += mutations()
    print(f"Binding production from {args.source_root.resolve()}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                text = replace_once(text, before, after)
            (production / path).write_text(text.replace("Time.unscaledTime", "FaceClock.Now").replace("Time.unscaledDeltaTime", "FaceClock.Delta").replace("binding.Renderer.SetBlendShapeWeight(binding.Index, weight * 100f)", "FaceWrites.Set(binding.Renderer, binding.Index, weight * 100f)"))
        project = build / "Interaction.csproj"
        shutil.copyfile(fixture / "Face.csproj", project)
        assembly = "TownInteraction_" + name.replace("-", "_")
        command = [dotnet, "build", str(project), "--configuration", "Release", "--nologo", "--verbosity", "quiet",
                   f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
                   f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}"]
        compiled = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(compiled.stdout)
        if compiled.returncode:
            print(compiled.stdout)
            raise SystemExit(f"FAIL: {name} did not compile (not a successful negative control)")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
        print(f"Compiled {name}", flush=True)
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    shutil.copyfile(fixture / "Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.physics":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    log = run / "unity.log"
    command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(log)]
    if args.bundle:
        command += ["-faceBundle", str(args.bundle.resolve()), "-faceEvidence", str(run / "actual-prefabs.json")]
    if args.bundle_only: command += ["-faceBundleOnly"]
    completed = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    if args.bundle and hashlib.sha256(args.bundle.read_bytes()).hexdigest() != bundle_hash:
        raise SystemExit("FAIL: bundle changed during validation; rerun against the completed artifact")
    result = Path(manifest["result"])
    if result.exists():
        print(result.read_text(), end="")
    if completed.returncode or not result.exists():
        print(f"FAIL: Unity exit {completed.returncode}; log: {log}")
        raise SystemExit(1)
    print(f"PASS: {len(variants)} production/negative variants; evidence: {run}")


if __name__ == "__main__":
    main()
