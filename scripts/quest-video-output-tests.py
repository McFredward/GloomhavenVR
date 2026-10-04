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
    world_shader = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestWorldScreen.shader"
    world_original = world_shader.read_text()
    platform = (ROOT / "src/GloomhavenVR/Core/QuestStandalonePlatform.cs").read_text()
    shader_start = platform.index('    internal const string FlatScreenShaderName =')
    shader_end = platform.index('    /// <summary>Quest output adapters', shader_start)
    material_bridge = "using System; using UnityEngine;\nnamespace GloomhavenVR.Core { internal static partial class QuestStandalonePlatform {\n" + platform[shader_start:shader_end] + "\n} }\n"
    flat = ROOT / "src/GloomhavenVR/WorldUI/FlatScreen"
    bridge = (flat / "FlatScreen.6.VideoCapture.cs").read_text()
    stack = (flat / "FlatScreen.2.CameraStack.cs").read_text()
    start = stack.index("    private RenderTexture? TargetFor(CapturedCamera c) =>")
    end = stack.index(";", start) + 1
    routing = "using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\n" + stack[start:end] + "\n}\n"
    variants = [
        ("production", "", "", ""),
        ("world-screen-array-sampler", "", "", "actual instanced left eye renders owned Tex2D screen pixels"),
        ("world-screen-mono-reset-omitted", "", "", "same persistent screen clears stale right capture on Intro suspension"),
        ("world-screen-left-only", "", "", "actual instanced right eye samples its distinct owned capture"),
        ("world-screen-alpha-blended", "", "", "actual instanced left eye renders owned Tex2D screen pixels"),
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
    selected = os.environ.get("QUEST_VIDEO_TEST_CASES")
    if selected:
        requested = set(selected.split(",")) | {"production"}
        if not requested.issubset({entry[0] for entry in variants}):
            raise RuntimeError("Unknown diagnostic case selection.")
        variants = [entry for entry in variants if entry[0] in requested]
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
        screen_source = world_original
        if name == "world-screen-array-sampler":
            screen_source = screen_source.replace("sampler2D _MainTex, _RightTex;", "UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex); UNITY_DECLARE_SCREENSPACE_TEXTURE(_RightTex);")
            screen_source = screen_source.replace("tex2D(_MainTex, input.uv)", "UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, input.uv)").replace("tex2D(_RightTex, input.uv)", "UNITY_SAMPLE_SCREENSPACE_TEXTURE(_RightTex, input.uv)")
        elif name == "world-screen-left-only":
            screen_source = screen_source.replace("eye = unity_StereoEyeIndex * _StereoCapture;", "eye = 0.0;")
        elif name == "world-screen-alpha-blended":
            screen_source = screen_source.replace("Blend One Zero", "Blend SrcAlpha OneMinusSrcAlpha").replace("tex2D(_RightTex, input.uv).rgb, eye), 1.0)", "tex2D(_RightTex, input.uv).rgb, eye), tex2D(_RightTex, input.uv).a)")
        # Each compiled fixture has its own shader identity so controls cannot
        # accidentally reuse the production variant loaded by a previous case.
        screen_source = screen_source.replace("Hidden/GloomhavenVR/QuestWorldScreen", "Hidden/GloomhavenVR/QuestWorldScreen/" + name)
        (case / "QuestWorldScreen.shader").write_text(screen_source)
        case_material_bridge = material_bridge
        if name == "world-screen-mono-reset-omitted":
            mono_start = case_material_bridge.index("    internal static void SetFlatScreenMono(")
            mono_end = case_material_bridge.index("    internal static void SetFlatScreenEyes(", mono_start)
            mono_body = case_material_bridge[mono_start:mono_end]
            no_reset = mono_body[:mono_body.index("        if (!Enabled")] + "        return;\n    }\n\n"
            case_material_bridge = case_material_bridge[:mono_start] + no_reset + case_material_bridge[mono_end:]
        bridge_source = case_material_bridge.replace('Resources.Load<Shader>("QuestWorldScreen")', 'Resources.Load<Shader>("QuestWorldScreen_' + name + '")').replace('Hidden/GloomhavenVR/QuestWorldScreen"', 'Hidden/GloomhavenVR/QuestWorldScreen/' + name + '"')
        (production / "MaterialBridge.cs").write_text(bridge_source)
        (production / "Bridge.cs").write_text(bridge)
        (production / "Routing.cs").write_text(routing)
        per_eye = (flat / "FlatScreenStereo.4.PerEye.cs").read_text()
        consumer_start = per_eye.index("        QuestStandalonePlatform.PrepareFlatScreenVideoSample();")
        consumer_end = per_eye.index("        Camera.MonoOrStereoscopicEye eye", consumer_start)
        consumer = per_eye[consumer_start:consumer_end]
        if name == "stereo-copy-before-composition":
            consumer = consumer.replace("        QuestStandalonePlatform.PrepareFlatScreenVideoSample();", "") + "        QuestStandalonePlatform.PrepareFlatScreenVideoSample();\n"
        bind_start = per_eye.index("        RenderTexture? target;", consumer_end)
        bind_end = per_eye.index("        QuestStandalonePlatform.ObserveFlatScreenVideoSample();", bind_start)
        eye_binding = per_eye[bind_start:bind_end]
        compositor = (flat / "FlatScreenStereo.2.Compositor.cs").read_text()
        reset_start = compositor.index("        if (_quadMaterial != null && _leftRt != null && _quadMaterial.mainTexture != _leftRt)")
        reset_end = compositor.index("        ReleaseRightRt();", reset_start)
        reset_binding = compositor[reset_start:reset_end]
        (production / "EyeBinding.cs").write_text("using UnityEngine; using GloomhavenVR.Core;\ninternal static class SharedEyeBindingFixture {\n"
            + "static bool _mapBaseCapture,_mapFirstFrameRendered,_mapDrivenValid,MapRevealHoldElapsed,_mapRevealLogged,_videoSuspended,_videoShift; static RenderTexture? _mapRt,_leftRt,_rtRight,_rtLeftShifted; static int _mapEngageFrame; static float _mapEngageTime;\n"
             + "static Material? _quadMaterial; internal static void Deactivate(Material material, RenderTexture left) { _quadMaterial=material; _leftRt=left;\n" + reset_binding + "}\n"
            + "internal static void Bind(Material mat, bool right, RenderTexture left, RenderTexture other, bool suspended, bool shifted, bool map=false, bool revealed=true) { _mapBaseCapture=map; _mapFirstFrameRendered=revealed; _mapDrivenValid=revealed; MapRevealHoldElapsed=false; _mapRevealLogged=false; _mapRt=map?other:null; _mapEngageFrame=0; _mapEngageTime=0; _videoSuspended=suspended; _videoShift=shifted; _leftRt=left; _rtRight=other; _rtLeftShifted=left;\n" + eye_binding + "}\n}\n")
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
    for case in manifest["cases"]:
        name = case["name"]
        shutil.copyfile(run / name / "QuestWorldScreen.shader", resources / ("QuestWorldScreen_" + name + ".shader"))
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2))
    (run / "source-hashes.json").write_text(json.dumps({"runtime": hashlib.sha256(runtime.read_bytes()).hexdigest(),
        "shader": hashlib.sha256(shader.read_bytes()).hexdigest(),
        "worldShader": hashlib.sha256(world_shader.read_bytes()).hexdigest(),
        "worldMaterialBridge": hashlib.sha256(material_bridge.encode()).hexdigest(),
        "captureOwnership": hashlib.sha256(bridge.encode()).hexdigest(), "actualCaptureRouting": hashlib.sha256(routing.encode()).hexdigest(),
        "sharedStereoSampling": hashlib.sha256(per_eye[consumer_start:consumer_end].encode()).hexdigest(),
        "sharedEyeBinding": hashlib.sha256(eye_binding.encode()).hexdigest(),
        "sharedDeactivateBinding": hashlib.sha256(reset_binding.encode()).hexdigest(),
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
