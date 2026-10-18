#!/usr/bin/env python3
"""Exercise the production Quest movie output using real Unity camera/texture pixels.

The fixture supplies decoded asymmetric pixels, without replacing or emulating a
codec. Native VideoPlayer property/callback preservation is separately covered by
quest-startup-video-tests.py. Desktop GL pixels do not prove a Quest GPU outcome.
"""
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from town_run_retention import cleanup_completed_run
_pending_run = None
_unity_process = None


def main():
    global _pending_run, _unity_process
    output = ROOT / ".planning/debug/quest-video-output"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    _pending_run = run
    (run / "runner.log").write_text("Quest camera video output evidence: " + str(run) + "\n")
    runtime = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestCameraVideoOutput.cs"
    original = runtime.read_text()
    camera_callback = original[original.index("        void BeforeCamera(Camera rendering)"):
        original.index("        // Only presentation state is changed.")]
    queue_callback = original[original.index("        void QueueConsumerEvidence()"):
        original.index("        void SampleConsumer()")]
    late_gpu_operations = ("Graphics.", "RenderTexture.active", "QuestTextureCopy.",
        "PrepareFlatScreenVideoSample(", "SampleConsumer(", "Probe(")
    if any(token in camera_callback or token in queue_callback for token in late_gpu_operations):
        raise RuntimeError("Production head pre-render/queue callback performs a target-changing GPU operation.")
    world_shader = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestWorldScreen.shader"
    world_original = world_shader.read_text()
    glass_shader = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestWorldScreenGlass.shader"
    glass_original = glass_shader.read_text()
    platform = (ROOT / "src/GloomhavenVR/Core/QuestStandalonePlatform.cs").read_text()
    shader_start = platform.index('    internal const string FlatScreenShaderName =')
    shader_end = platform.rindex('    /// <summary>', shader_start,
        platform.index('    public static event Action? FlatScreenVideoSampling;', shader_start))
    material_bridge = "using System; using UnityEngine;\nnamespace GloomhavenVR.Core { internal static partial class QuestStandalonePlatform {\n" + platform[shader_start:shader_end] + "\n} }\n"
    flat = ROOT / "src/GloomhavenVR/WorldUI/FlatScreen"
    bridge = (flat / "FlatScreen.6.VideoCapture.cs").read_text()
    stack = (flat / "FlatScreen.2.CameraStack.cs").read_text()
    start = stack.index("    private RenderTexture? TargetFor(CapturedCamera c) =>")
    end = stack.index(";", start) + 1
    routing = "using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\n" + stack[start:end] + "\n}\n"
    variants = [
        ("production", "", "", ""),
        ("game-debug-shadows-output", "    using Debug = UnityEngine.Debug;", "", "movie pixel evidence survives the original game's conditional global Debug"),
        ("snapshot-provenance-stale", '+ " snapshotGeneration=" + snapshotGeneration', '+ " snapshotGeneration=" + -1', "completed native snapshot reports matching producer and snapshot generations"),
        ("head-capture-view-leak", "            RenderTexture previous = RenderTexture.active;\n            try\n            {\n                if (farOutput)", "            RenderTexture previous = RenderTexture.active;\n            if (GloomhavenVR.Core.QuestStandalonePlatform.HeadCamera != null) GloomhavenVR.Core.QuestStandalonePlatform.HeadCamera.worldToCameraMatrix = camera.worldToCameraMatrix;\n            try\n            {\n                if (farOutput)", "unequal native capture and eye targets preserve actual head projection viewport and tracked geometry"),
        ("quest-eye-copy-in-pre-render", "", "", "Quest head pre-render preserves the prepared partial viewport"),
        ("consumer-readback-in-pre-render", "            if (consumerEvidencePending) consumerEvidenceReady = true;", "            if (consumerEvidencePending) SampleConsumer();", "queued consumer evidence performs no target-changing draw in head pre-render"),
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
        ("render-target-projection-ignored", "if (gpuProjection.m11 * projection.m11 < 0f)", "if (gpuProjection.m11 * projection.m11 < -100000f)", "camera-plane samples retain Vulkan camera orientation"),
        ("automatic-mips-generated-explicitly", "if (captured.useMipMap && !captured.autoGenerateMips) captured.GenerateMips();", "if (captured.useMipMap) captured.GenerateMips();", "automatic mip chains never invoke rejected explicit generation"),
        ("glass-alpha-multiplied-twice", "", "", "glass consumes native captured RGB without multiplying alpha twice"),
        ("glass-forced-opaque", "", "", "untouched glass pixels retain native background transparency"),
    ]
    graphics = os.environ.get("QUEST_VIDEO_TEST_GRAPHICS_API", "glcore")
    if graphics not in ("glcore", "vulkan"):
        raise RuntimeError("Unknown real Unity graphics API fixture selection.")
    vulkan_controls = {"game-debug-shadows-output", "head-capture-view-leak", "quest-eye-copy-in-pre-render", "render-target-projection-ignored", "automatic-mips-generated-explicitly",
        "glass-alpha-multiplied-twice", "glass-forced-opaque"}
    variants = [entry for entry in variants if entry[0] in ("production", "head-capture-view-leak", "game-debug-shadows-output", "quest-eye-copy-in-pre-render", "consumer-readback-in-pre-render")
        or (entry[0] in vulkan_controls) == (graphics == "vulkan")]
    selected = os.environ.get("QUEST_VIDEO_TEST_CASES")
    if selected:
        requested = set(selected.split(",")) | {"production"}
        if not requested.issubset({entry[0] for entry in variants}):
            raise RuntimeError("Unknown diagnostic case selection.")
        variants = [entry for entry in variants if entry[0] in requested]
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    unity = Path("/home/claw/unity-2021.3.5/Editor/Unity")
    fixture = ROOT / "tests/QuestVideoOutput.Tests"
    # The Player compiles this adapter in QuestGame.Campaign, separately from
    # GloomhavenVR.dll. A single-assembly pixel fixture cannot detect an internal
    # helper that becomes inaccessible only in that real compilation boundary.
    boundary = run / "assembly-boundary"
    boundary.mkdir()
    mod_project = ROOT / "src/GloomhavenVR/GloomhavenVR.csproj"
    mod_build = subprocess.run([dotnet, "build", str(mod_project), "-c", "Release",
        "--nologo", "--verbosity", "quiet", "--disable-build-servers", "-p:UseSharedCompilation=false"], capture_output=True, text=True)
    (boundary / "mod-build.log").write_text(mod_build.stdout + mod_build.stderr)
    if mod_build.returncode:
        raise SystemExit("Current mod compilation failed: " + str(boundary / "mod-build.log"))
    boundary_project = boundary / "Boundary.csproj"
    shutil.copyfile(fixture / "AssemblyBoundary.csproj", boundary_project)
    boundary_build = subprocess.run([dotnet, "build", str(boundary_project), "-c", "Release", "--nologo",
        "--verbosity", "quiet", "--disable-build-servers", "-p:UseSharedCompilation=false", "-p:QuestRuntime=" + str(runtime.parent),
        "-p:ModAssembly=" + str(mod_project.parent / "bin/Release/net472/GloomhavenVR.dll"),
        "-p:UnityManaged=" + str(unity.parent / "Data/Managed")], capture_output=True, text=True)
    (boundary / "build.log").write_text(boundary_build.stdout + boundary_build.stderr)
    if boundary_build.returncode:
        raise SystemExit("Actual Quest/mod assembly boundary failed: " + str(boundary / "build.log"))
    manifest = {"result": str(run / "results.txt"),
        "managed": str((ROOT / "ressources/GH_Data/Managed").resolve()),
        "dependencies": str(ROOT / "src/GloomhavenVR/bin/Release/net472"), "cases": []}
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
            source = baseline.read_text() + "\nnamespace GloomhavenVR.Quest { static class BaselineConsumer { internal static void CompleteCapture(this QuestCameraVideoOutput output) { } internal static void Probe(this QuestCameraVideoOutput output, Texture source, string stage, bool asynchronous) { } internal static string CaptureStateFacts(this QuestCameraVideoOutput output) => \"\"; } }\n"
        if before:
            if source.count(before) != 1:
                raise RuntimeError("Mutation binding drift: " + name)
            source = source.replace(before, after)
        (production / "QuestCameraVideoOutput.cs").write_text(source)
        shutil.copyfile(ROOT / "src/GloomhavenVR/Core/QuestTextureCopy.cs", production / "QuestTextureCopy.cs")
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
        glass_source = glass_original
        if name == "glass-alpha-multiplied-twice":
            glass_source = glass_source.replace("return tex2D(_MainTex, input.uv);", "float4 color = tex2D(_MainTex, input.uv); color.rgb *= color.a; return color;")
        elif name == "glass-forced-opaque":
            glass_source = glass_source.replace("return tex2D(_MainTex, input.uv);", "return float4(tex2D(_MainTex, input.uv).rgb, 1.0);")
        glass_source = glass_source.replace("Hidden/GloomhavenVR/QuestWorldScreenGlass", "Hidden/GloomhavenVR/QuestWorldScreenGlass/" + name)
        (case / "QuestWorldScreenGlass.shader").write_text(glass_source)
        case_material_bridge = material_bridge
        if name == "world-screen-mono-reset-omitted":
            mono_start = case_material_bridge.index("    internal static void SetFlatScreenMono(")
            mono_end = case_material_bridge.index("    internal static void SetFlatScreenEyes(", mono_start)
            mono_body = case_material_bridge[mono_start:mono_end]
            no_reset = mono_body[:mono_body.index("        if (!Enabled")] + "        return;\n    }\n\n"
            case_material_bridge = case_material_bridge[:mono_start] + no_reset + case_material_bridge[mono_end:]
        bridge_source = case_material_bridge.replace('Resources.Load<Shader>("QuestWorldScreen")', 'Resources.Load<Shader>("QuestWorldScreen_' + name + '")').replace('Hidden/GloomhavenVR/QuestWorldScreen"', 'Hidden/GloomhavenVR/QuestWorldScreen/' + name + '"')
        bridge_source = bridge_source.replace('Resources.Load<Shader>("QuestWorldScreenGlass")', 'Resources.Load<Shader>("QuestWorldScreenGlass_' + name + '")').replace('Hidden/GloomhavenVR/QuestWorldScreenGlass"', 'Hidden/GloomhavenVR/QuestWorldScreenGlass/' + name + '"')
        (production / "MaterialBridge.cs").write_text(bridge_source)
        (production / "Bridge.cs").write_text(bridge)
        (production / "Routing.cs").write_text(routing)
        per_eye = (flat / "FlatScreenStereo.4.PerEye.cs").read_text()
        consumer_method = per_eye.index("    private void PrepareVideoEyes()")
        consumer_start = per_eye.index("        QuestStandalonePlatform.PrepareFlatScreenVideoSample();", consumer_method)
        consumer_end = per_eye.index("\n    }", consumer_start)
        consumer = per_eye[consumer_start:consumer_end]
        if name == "stereo-copy-before-composition":
            consumer = consumer.replace("        QuestStandalonePlatform.PrepareFlatScreenVideoSample();", "") + "        QuestStandalonePlatform.PrepareFlatScreenVideoSample();\n"
        pre_cull_start = per_eye.index("        if (!_active)", per_eye.index("    private void OnPreCullCamera(Camera cam)"))
        pre_cull_end = per_eye.index("        // Drive the real game map camera", pre_cull_start)
        pre_cull = per_eye[pre_cull_start:pre_cull_end]
        pre_render_start = per_eye.index("        Camera? head = Rig.VRRigDriver.HeadCamera;", per_eye.index("    private void OnPreRenderCamera(Camera cam)"))
        pre_render_end = per_eye.index("        Camera.MonoOrStereoscopicEye eye", pre_render_start)
        pre_render = per_eye[pre_render_start:pre_render_end]
        if name == "quest-eye-copy-in-pre-render":
            before = "        if (!QuestStandalonePlatform.Enabled)\n            PrepareVideoEyes();"
            if pre_render.count(before) != 1:
                raise RuntimeError("Quest pre-render mutation binding drift.")
            pre_render = pre_render.replace(before, "        PrepareVideoEyes();")
            # The historical late path also performed its first copy here.
            pre_cull = pre_cull.replace("            PrepareVideoEyes();", "            return;")
        bind_start = per_eye.index("        RenderTexture? target;", pre_render_end)
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
            + "static bool _active=true; static Material? _quadMaterial;\n"
            + "internal static void Setup(RenderTexture source, RenderTexture left, RenderTexture right, float requestedShift, Material? material=null) { _leftRt=source; _rtLeftShifted=left; _rtRight=right; _videoShiftUv=requestedShift; _shiftBlitFrame=-1; _quadMaterial=material; }\n"
            + "internal static void BeforeCull(Camera cam) {\n" + pre_cull + "}\n"
            + "internal static void BeforeRender(Camera cam) {\n" + pre_render + "}\n"
            + "static void PrepareVideoEyes() {\n" + consumer + "}\n"
            + "internal static void Sample(RenderTexture source, RenderTexture left, RenderTexture right, float requestedShift) { Setup(source,left,right,requestedShift); PrepareVideoEyes(); }\n}\n"
            + "namespace Rig { internal static class VRRigDriver { internal static Camera? HeadCamera => QuestStandalonePlatform.HeadCamera; } }\n")
        project = case / "Runtime.csproj"
        shutil.copyfile(fixture / "Runtime.csproj", project)
        assembly = "QuestVideoOutput_" + name.replace("-", "_")
        result = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            "--disable-build-servers", "-p:UseSharedCompilation=false",
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
    shutil.copyfile(ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestTextureCopy.shader", resources / "QuestTextureCopy.shader")
    for case in manifest["cases"]:
        imported = editor / Path(case["dll"]).name
        shutil.copyfile(case["dll"], imported)
        case["dll"] = str(imported)
    shutil.copyfile(ROOT / "scripts/desktop-render-runtime/Editor/InteractionRunner.cs", editor / "InteractionRunner.cs")
    shader = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestCameraVideo.shader"
    shutil.copyfile(shader, resources / shader.name)
    shutil.copyfile(fixture / "UiRgbFixture.shader", resources / "UiRgbFixture.shader")
    for case in manifest["cases"]:
        name = case["name"]
        shutil.copyfile(run / name / "QuestWorldScreen.shader", resources / ("QuestWorldScreen_" + name + ".shader"))
        shutil.copyfile(run / name / "QuestWorldScreenGlass.shader", resources / ("QuestWorldScreenGlass_" + name + ".shader"))
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2))
    (run / "source-hashes.json").write_text(json.dumps({"runtime": hashlib.sha256(runtime.read_bytes()).hexdigest(),
        "shader": hashlib.sha256(shader.read_bytes()).hexdigest(),
        "worldShader": hashlib.sha256(world_shader.read_bytes()).hexdigest(),
        "glassShader": hashlib.sha256(glass_shader.read_bytes()).hexdigest(),
        "worldMaterialBridge": hashlib.sha256(material_bridge.encode()).hexdigest(),
        "captureOwnership": hashlib.sha256(bridge.encode()).hexdigest(), "actualCaptureRouting": hashlib.sha256(routing.encode()).hexdigest(),
        "sharedStereoSampling": hashlib.sha256(per_eye[consumer_start:consumer_end].encode()).hexdigest(),
        "sharedStereoPreCull": hashlib.sha256(per_eye[pre_cull_start:pre_cull_end].encode()).hexdigest(),
        "sharedStereoPreRender": hashlib.sha256(per_eye[pre_render_start:pre_render_end].encode()).hexdigest(),
        "sharedEyeBinding": hashlib.sha256(eye_binding.encode()).hexdigest(),
        "sharedDeactivateBinding": hashlib.sha256(reset_binding.encode()).hexdigest(),
        "fixture": hashlib.sha256((fixture / "Program.cs").read_bytes()).hexdigest(),
        "graphicsApi": graphics,
        "boundary": "Production capture/snapshot and shared eye-copy/lifecycle gates; actual GL/Vulkan partial-viewport pixels identify target-switch state changes, and live pre-cull/pre-render callbacks prove source ordering. Desktop cameras do not emulate OpenXR stereo setup or prove headset distortion resolution."}, indent=2))
    command = [str(unity), "-batchmode", "-force-" + graphics, "-projectPath", str(project),
        "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    _unity_process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
    exit_code = _unity_process.wait(timeout=300)
    report = Path(manifest["result"])
    if report.is_file():
        print(report.read_text(), end="", flush=True)
    if exit_code or not report.is_file():
        raise SystemExit("FAIL real Unity camera video output fixture: " + str(run / "unity.log"))
    print("PASS production camera video output and " + str(len(variants) - 1) + " defect controls")


if __name__ == "__main__":
    code = 1
    try:
        if sys.platform.startswith("linux"):
            if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0):
                raise OSError("Could not own Unity fixture descendants")
        main()
        code = 0
    finally:
        if _unity_process is not None and _unity_process.poll() is None:
            _unity_process.kill(); _unity_process.wait()
        if _pending_run is not None:
            # The established fixture helper only signals this subreaper's
            # owned descendants, including orphaned editor compiler processes.
            spec = importlib.util.spec_from_file_location("movie_fixture_cleanup", ROOT / "scripts/quest-menu-sampling-tests.py")
            helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
            children = helper.reap_fixture_group(_unity_process.pid if _unity_process is not None else None)
            (_pending_run / "process-cleanup.json").write_text(json.dumps(children, indent=2) + "\n")
            receipt = cleanup_completed_run("quest-video-output", code, _pending_run / "runner.log", ROOT, process_reaped=True)
            (_pending_run / "cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")
