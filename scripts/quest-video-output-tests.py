#!/usr/bin/env python3
"""Exercise the production Quest movie output using real Unity camera/texture pixels.

The fixture supplies decoded asymmetric pixels, without replacing or emulating a
codec. Native VideoPlayer property/callback preservation is separately covered by
quest-startup-video-tests.py. Desktop GL pixels do not prove a Quest GPU outcome.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    output = ROOT / ".planning/debug/quest-video-output"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    runtime = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestCameraVideoOutput.cs"
    original = runtime.read_text()
    flat = ROOT / "src/GloomhavenVR/WorldUI/FlatScreen"
    bridge = (flat / "FlatScreen.6.VideoCapture.cs").read_text()
    stack = (flat / "FlatScreen.2.CameraStack.cs").read_text()
    start = stack.index("    private RenderTexture? TargetFor(CapturedCamera c) =>")
    end = stack.index(";", start) + 1
    routing = "using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\n" + stack[start:end] + "\n}\n"
    variants = [
        ("production", "", "", ""),
        ("capture-ownership-ignored", "return GloomhavenVR.Core.QuestStandalonePlatform.IsFlatScreenVideoTarget(candidate);", "return true;", "desktop movie output remains untouched"),
        ("aspect-mapping-discarded", "return new Vector4(x, y, (1f - x) * .5f, (1f - y) * .5f);", "return new Vector4(1, 1, 0, 0);", "native fit-inside letterbox bars preserved"),
        ("alpha-discarded", 'material.SetFloat("_Alpha", Mathf.Clamp01(alpha));', 'material.SetFloat("_Alpha", 1f);', "completed near output follows native stack without changing alpha"),
        ("far-plane-overwrites-foreground", "CompareFunction.GreaterEqual : CompareFunction.LessEqual", "CompareFunction.Always : CompareFunction.Always", "native foreground is present before output adaptation"),
        ("near-plane-behind-foreground", "bool far = mode == VideoRenderMode.CameraFarPlane;", "bool far = true;", "completed near output follows native stack without changing alpha"),
        ("completed-output-omitted", "Graphics.ExecuteCommandBuffer(completion);", "{ /* missing final output */ }", "native decoded red pixels reach captured target"),
        ("alpha-blended-twice", "if (completedGeneration == captureGeneration) return;", "if (completedGeneration == captureGeneration && captureGeneration < 0) return;", "repeated eye consumers never blend movie alpha twice"),
        ("disposed-output-reused", "disposed = true;", "disposed = false;", "disposed output never writes another capture"),
        ("b620-camera-event-only", "", "", "completed near output follows native stack without changing alpha"),
        ("stereo-copy-before-composition", "", "", "current completed video reaches left shifted eye"),
        ("unrendered-snapshot-shown", "if (farOutput && snapshotGeneration < 0) return;", "if (farOutput && snapshotGeneration < -1) return;", "head before producer never samples an unrendered retained target"),
    ]
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    unity = Path("/home/claw/unity-2021.3.5/Editor/Unity")
    fixture = ROOT / "tests/QuestVideoOutput.Tests"
    manifest = {"result": str(run / "results.txt"), "cases": []}
    print("Quest camera video output evidence: " + str(run), flush=True)
    for name, before, after, expected in variants:
        case = run / name
        production = case / "production"
        production.mkdir(parents=True)
        source = original
        if name == "b620-camera-event-only":
            baseline = fixture / "B620CameraEventOutput.cs.txt"
            if hashlib.sha256(baseline.read_bytes()).hexdigest() != "36ed0fcfbb904c3b9ede3d0788da5d21baf1efe2d51ee07ca9bb7c172dae783b":
                raise RuntimeError("Historical hardware source baseline drift.")
            source = baseline.read_text() + "\nnamespace GloomhavenVR.Quest { static class BaselineConsumer { internal static void CompleteCapture(this QuestCameraVideoOutput output) { } internal static void Probe(this QuestCameraVideoOutput output, Texture source, string stage, bool asynchronous) { } } }\n"
        if before:
            if source.count(before) != 1:
                raise RuntimeError("Mutation binding drift: " + name)
            source = source.replace(before, after)
        (production / "QuestCameraVideoOutput.cs").write_text(source)
        (production / "Bridge.cs").write_text(bridge)
        (production / "Routing.cs").write_text(routing)
        per_eye = (flat / "FlatScreenStereo.4.PerEye.cs").read_text()
        consumer_start = per_eye.index("        QuestStandalonePlatform.PrepareFlatScreenVideoSample();")
        consumer_end = per_eye.index("        Camera.MonoOrStereoscopicEye eye", consumer_start)
        consumer = per_eye[consumer_start:consumer_end]
        if name == "stereo-copy-before-composition":
            consumer = consumer.replace("        QuestStandalonePlatform.PrepareFlatScreenVideoSample();", "") + "        QuestStandalonePlatform.PrepareFlatScreenVideoSample();\n"
        state = (flat / "FlatScreenStereo.1.State.cs").read_text()
        overscan = next(line.strip() for line in state.splitlines() if "const float VideoOverscan =" in line)
        (production / "StereoSampling.cs").write_text("using UnityEngine; using GloomhavenVR.Core;\ninternal static class SharedStereoFixture {\n"
            + overscan + "\nstatic int _shiftBlitFrame=-1; static bool _videoShift=true; static RenderTexture _leftRt=null!,_rtRight=null!,_rtLeftShifted=null!; static float _videoShiftUv;\n"
            + "internal static void Sample(RenderTexture source, RenderTexture left, RenderTexture right, float requestedShift) { _leftRt=source; _rtLeftShifted=left; _rtRight=right; _videoShiftUv=requestedShift; _shiftBlitFrame=-1;\n" + consumer + "}\n}\n")
        project = case / "Runtime.csproj"
        shutil.copyfile(fixture / "Runtime.csproj", project)
        assembly = "QuestVideoOutput_" + name.replace("-", "_")
        result = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production),
            "-p:UnityManaged=" + str(unity.parent / "Data/Managed")], capture_output=True, text=True)
        (case / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr + "Compilation errors cannot pass a negative control.")
        manifest["cases"].append({"name": name, "dll": str(case / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
    project = run / "unity"
    editor = project / "Assets/Editor"
    resources = project / "Assets/Resources"
    editor.mkdir(parents=True); resources.mkdir()
    for case in manifest["cases"]:
        imported = editor / Path(case["dll"]).name
        shutil.copyfile(case["dll"], imported)
        case["dll"] = str(imported)
    shutil.copyfile(ROOT / "scripts/desktop-render-runtime/Editor/InteractionRunner.cs", editor / "InteractionRunner.cs")
    shader = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestCameraVideo.shader"
    shutil.copyfile(shader, resources / shader.name)
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2))
    (run / "source-hashes.json").write_text(json.dumps({"runtime": hashlib.sha256(runtime.read_bytes()).hexdigest(),
        "shader": hashlib.sha256(shader.read_bytes()).hexdigest(),
        "captureOwnership": hashlib.sha256(bridge.encode()).hexdigest(), "actualCaptureRouting": hashlib.sha256(routing.encode()).hexdigest(),
        "sharedStereoSampling": hashlib.sha256(per_eye[consumer_start:consumer_end].encode()).hexdigest(),
        "fixture": hashlib.sha256((fixture / "Program.cs").read_bytes()).hexdigest(),
        "boundary": "Production completed-capture and camera-depth snapshot, actual shared eye-copy body, real head/world shader/mip/foreground pixels and bounded sync readback; controlled late write reproduces B620 but does not prove Android native ordering"}, indent=2))
    command = [str(unity), "-batchmode", "-force-glcore", "-projectPath", str(project),
        "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    report = Path(manifest["result"])
    if report.is_file():
        print(report.read_text(), end="", flush=True)
    if result.returncode or not report.is_file():
        raise SystemExit("FAIL real Unity camera video output fixture: " + str(run / "unity.log"))
    for cache in ("Library", "Temp"):
        shutil.rmtree(project / cache, ignore_errors=True)
    print("PASS production camera video output and " + str(len(variants) - 1) + " defect controls")


if __name__ == "__main__":
    main()
