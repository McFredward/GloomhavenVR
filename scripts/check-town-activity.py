#!/usr/bin/env python3
"""Compile production town occupation timelines, imported arm IK and shared playback cases and run them inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files.
Explicit fixture boundaries are documented in town-activity-runtime/Boundaries.cs.
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
    names = ["TownServiceActivityHandover.cs", "TownServiceActivityMotion.cs", "TownServiceActivityRig.cs", "TownServiceActivityProps.cs", "TownServiceGrounding.cs", "TownServiceFaceAttention.cs", "TownServiceFaceMotion.cs", "TownServiceFaceRig.cs"]
    bound = {name: (base / name).read_text() for name in names}
    bound["RemoteTownActivities.cs"] = (root / "src/GloomhavenVR/Net/Remote/RemoteTownActivities.cs").read_text()
    bound["TownActivityTypes.cs"] = (root / "src/GloomhavenVR/Net/TownActivityState.cs").read_text().split("/// <summary>Additive81:")[0]
    bound["RemoteTownFaces.cs"] = (root / "src/GloomhavenVR/Net/Remote/RemoteTownFaces.cs").read_text()
    bound["RemoteTownPerformance.cs"] = (root / "src/GloomhavenVR/Net/Remote/RemoteTownPerformance.cs").read_text()
    bound["FaceTypes.cs"] = (root / "src/GloomhavenVR/Net/TownFaceState.cs").read_text().split("/// <summary>Additive80:")[0]
    return bound, {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}


def mutations():
    return [
        ("phase-jump", "TownServiceActivityMotion.cs", "state.FromBlend = Blend(in state);", "state.FromBlend = state.Engaged ? 1f : 0f;", "interrupted transition keeps current pose"),
        ("work-runs-while-engaged", "TownServiceActivityMotion.cs", "dt - Integral(in state, state.TransitionAge + dt) + Integral(in state, state.TransitionAge)", "dt", "engaged occupation remains paused"),
        ("ignore-ik", "TownServiceActivityRig.cs", "if (!Ready) return;", "if (Ready) return;", "actual hand reaches occupation target"),
        ("thumb-overcurl", "TownServiceActivityRig.cs", "? 5f : 65f", "? 30f : 65f", "approximate thumb stays within supported deformation range"),
        ("prayer-snap", "TownServiceActivityRig.cs", "Quaternion.Slerp(Quaternion.LookRotation(_root.up, -side * _root.right), table, attention)", "(attention < .5f ? Quaternion.LookRotation(_root.up, -side * _root.right) : table)", "hand orientation remains smooth through prayer interruption"),
        ("no-writing-reach", "TownServiceActivityRig.cs", "18f * visual.Writing", "0f * visual.Writing", "writing contact survives resolved terrain offsets"),
        ("returning-author-snap", "TownServiceActivityHandover.cs", "_age = 0f;", "_age = Duration;", "returning authority keeps displayed hands at first frame"),
        ("unpaired-sequences", "RemoteTownPerformance.cs", "if (!TownActivityCodec.Matches(in activity, in face)", "if (false", "mismatched sequence cannot partially advance pair"),
        ("stale-sequence", "RemoteTownActivities.cs", "!Newer(state.Sequence, peer.Latest.Sequence)", "false", "older occupation cannot replace current phase"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--portable", action="store_true", help="Run managed phase/network cases in .NET, without Editor or asset assertions")
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-activity")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--render", type=Path, help="Optional output folder for actual rig/tool contact images")
    parser.add_argument("--book-obj", type=Path, help="Read-only original-game open book OBJ (Blender Z-up export)")
    parser.add_argument("--bundle", type=Path, help="Optional Linux final-asset bundle for actual prefab binding checks")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-activity-runtime"
    if args.portable and (args.bundle or args.render): parser.error("--portable cannot load/render Unity assets")
    if not args.portable and not args.unity.is_file():
        parser.error("Unity 2021.3.5 is required; pass --unity")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    bound, hashes = sources(args.source_root)
    if args.portable:
        wanted = {"TownServiceActivityMotion.cs", "TownServiceActivityHandover.cs", "TownActivityTypes.cs",
                  "RemoteTownActivities.cs", "RemoteTownFaces.cs", "RemoteTownPerformance.cs", "FaceTypes.cs", "TownServiceFaceMotion.cs"}
        bound = {name: code for name, code in bound.items() if name in wanted}
        face = bound["TownServiceFaceMotion.cs"]
        marker = "    internal static TownFacePose Interpolate("
        if face.count(marker) != 1: raise SystemExit("Production face interpolation binding drift")
        bound["TownServiceFaceMotion.cs"] = "using GloomhavenVR.Net; using UnityEngine; namespace GloomhavenVR.WorldUI;\ninternal static class TownServiceFaceMotion {\n" + face[face.index(marker):]
        hashes = {name: hashlib.sha256(code.encode()).hexdigest() for name, code in bound.items()}
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes}, indent=2) + "\n")
    bundle_hash = None
    if args.bundle:
        if not args.bundle.is_file(): parser.error("--bundle must name the completed Linux validation bundle")
        bundle_hash = hashlib.sha256(args.bundle.read_bytes()).hexdigest()
        (run / "bundle-evidence.json").write_text(json.dumps({"path": str(args.bundle.resolve()),
            "bytes": args.bundle.stat().st_size, "sha256": bundle_hash}, indent=2) + "\n")
    manifest = {"result": str(run / "results.txt"), "cases": []}
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls: variants += [v for v in mutations() if args.bundle or v[0] not in ("ignore-ik", "thumb-overcurl", "prayer-snap", "no-writing-reach")]
    print(f"Binding production from {args.source_root.resolve()}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                text = text.replace(before, after) if name == "stale-sequence" else replace_once(text, before, after)
            (production / path).write_text(text.replace("Time.unscaledTime", "FaceClock.Now").replace("Time.unscaledDeltaTime", "FaceClock.Delta"))
        project = build / "Interaction.csproj"
        shutil.copyfile(fixture / "Activity.csproj", project)
        managed = args.unity.parent / "Data/Managed"
        framework = "netstandard2.1"
        if args.portable:
            framework = "net8.0"
            portable = build / "portable"
            portable.mkdir()
            program = (fixture / "Program.cs").read_text().split("    private static void Actual(")[0] + "}\n"
            program = program.replace('        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-faceBundle");\n        if(at>=0)Actual(args[at+1]);\n', '')
            (portable / "Program.cs").write_text(program)
            shutil.copyfile(fixture / "PortableBoundaries.cs.in", portable / "Boundaries.cs")
            project.write_text(project.read_text().replace("<TargetFramework>netstandard2.1</TargetFramework>",
                "<TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType>")
                .replace('$(FixtureDir)/*.cs', str(portable / '*.cs'))
                .replace('    <Reference Include="$(UnityManaged)/UnityEngine/*.dll" />\n', ''))
        assembly = "TownInteraction_" + name.replace("-", "_")
        command = [dotnet, "build", str(project), "--configuration", "Release", "--nologo", "--verbosity", "quiet",
                   f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
                   f"-p:UnityManaged={managed}"]
        compiled = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(compiled.stdout)
        if compiled.returncode:
            print(compiled.stdout)
            raise SystemExit(f"FAIL: {name} did not compile (not a successful negative control)")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release" / framework / (assembly + ".dll")), "expected": expected})
        print(f"Compiled {name}", flush=True)
    if args.portable:
        results = []
        for case in manifest["cases"]:
            tested = subprocess.run([dotnet, case["dll"]], capture_output=True, text=True)
            result = tested.stdout + tested.stderr
            if case["expected"]:
                okay = tested.returncode != 0 and case["expected"] in result
            else: okay = tested.returncode == 0
            results.append(("PASS " if okay else "FAIL ") + case["name"] + ": " + result.strip())
            if not okay:
                (run / "results.txt").write_text("\n".join(results))
                raise SystemExit(results[-1])
        (run / "results.txt").write_text("\n".join(results) + "\n")
        print("\n".join(results))
        print(f"PASS: portable phase/network variants; no game assemblies or Unity scene/rig claim; evidence: {run}")
        return
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
    if args.render:
        args.render.mkdir(parents=True, exist_ok=True)
        command.remove("-nographics")
        command += ["-activityRender", str(args.render.resolve())]
        if args.book_obj: command += ["-activityBook", str(args.book_obj.resolve())]
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
