#!/usr/bin/env python3
"""Execute original CoreModule with Android XR/IO bridge source and bounded defect controls."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    core = root / "src/GloomhavenVR/Core"
    sources = {
        "QuestStandalonePlatform.cs": core / "QuestStandalonePlatform.cs",
        "QuestEyeResolution.cs": root / "src/GloomhavenVR/Rig/QuestEyeResolution.cs",
        "FrameDefaults.cs": core / "Startup/FrameDefaults.cs",
        "RuntimeDepsLoader.cs": core / "Startup/RuntimeDepsLoader.cs",
        "OpenXRBootstrap.cs": core / "Startup/OpenXRBootstrap.cs",
        "CoreModule.cs": core / "CoreModule.cs",
        "IVRModule.cs": core / "IVRModule.cs",
    }
    original = {name: path.read_text() for name, path in sources.items()}
    render_quality = (root / "src/GloomhavenVR/Rig/RenderQuality.cs").read_text()
    methods = []
    for signature in ("private static void ApplyMsaa(bool canWriteRenderResources)", "private static void ApplyEyeScale(bool canWriteRenderResources)"):
        start = render_quality.index("    " + signature)
        end = render_quality.index("\n    }", start) + len("\n    }")
        methods.append(render_quality[start:end])
    # Execute the actual current controller methods, not a second implementation.
    original["RenderQualityAllocation.fixture"] = """using System.Collections.Generic;
using GloomhavenVR.Core; using UnityEngine; using UnityEngine.XR;
namespace GloomhavenVR.Rig;
internal static class RenderQualityAllocationFixture {
    internal sealed class Entry<T> { internal T Value; internal Entry(T value) { Value = value; } }
    private static Entry<int> MsaaLevel = new(8);
    private static Entry<float> EyeResolutionScale = new(1.5f);
    internal static int SavedMsaa => MsaaLevel.Value;
    private const float MinEyeScale = 0.5f, MaxEyeScale = 2f;
    private static int _baseEyeWidth, _lastPushedDisplayMsaa = -1;
    private static int _pendingMsaa = -1, _committedMsaa = -1;
    private static float _pendingMsaaSince, _nextMsaaApplyTime;
    private static float _viewportScaleApplied = 1f, _lastLoggedEyeScale = -1f;
    private static float _sessionAllocationScale = 1f, _pendingEyeScale = 1.5f;
    private static float _pendingEyeScaleSince, _lastEyeRefusal = -1f, _nextViewportRepairTime;
    private const float SliderQuietSeconds = .35f, MsaaResourceQuietSeconds = 1f, ViewportRepairIntervalSeconds = 1f;
    private static bool _eyeScaleAnnounced;
    private static bool _sessionPrepared = true, _viewportScaleAccepted;
    private static readonly List<XRDisplaySubsystem> Displays = new();
    private static int Sanitize(int value) => value;
    private static float WantedEyeScale() => Mathf.Clamp(EyeResolutionScale.Value, MinEyeScale, MaxEyeScale);
    private static void AdoptRunningSession() { _sessionPrepared = true; }
    private static void RequestEyeTargetDiagnostics(string reason) { }
    private static void ReleaseViewportScale() { XRSettings.renderViewportScale = 1f; _viewportScaleApplied = 1f; }
    private static bool ApplyViewportScale(float wanted, string reason) { XRSettings.renderViewportScale = wanted; _viewportScaleApplied = wanted; return true; }
    internal static void Reset() { _pendingMsaa = _committedMsaa = _lastPushedDisplayMsaa = -1; }
    internal static void Tick() { Time.unscaledTime += 1f; ApplyMsaa(true); ApplyEyeScale(true); }
""" + "\n".join(methods) + "\n}\n"
    output = root / ".planning/debug/quest-platform"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    cases = [("production", original, "")]
    for name, target, before, after, expected in (
        ("vulkan-live-msaa-restored", "RenderQualityAllocation.fixture", "\n            || QuestStandalonePlatform.FixedEyeTextureAllocation) return;", ") return;", "vulkan-render-controller"),
        ("quest-standalone-defaults-removed", "FrameDefaults.cs", "Core.QuestStandalonePlatform.Enabled\n        || ", "", "quest-mobile-defaults"),
        ("vulkan-eye-cap-removed", "QuestEyeResolution.cs", "Mathf.Clamp(requested, 0.5f, 1f)", "requested", "vulkan-eye-cap"),
        ("vulkan-eye-allocation-restored", "QuestEyeResolution.cs", "XRSettings.renderViewportScale = effective;", "XRSettings.eyeTextureResolutionScale = effective;", "vulkan-eye-viewport"),
        ("vulkan-eye-log-unbounded", "QuestEyeResolution.cs", "if (changed || Mathf.Abs(effective - _lastViewport) > 0.0005f)", "if (true)", "vulkan-eye-bounded"),
        ("world-screen-desktop-gate-removed", "QuestStandalonePlatform.cs",
         'if (!Enabled) return desktopShader;\n        Shader shader = Resources.Load<Shader>("QuestWorldScreen");',
         'if (Enabled && false) return desktopShader;\n        Shader shader = Resources.Load<Shader>("QuestWorldScreen");', "desktop-screen"),
        ("android-dynamic-deps", "RuntimeDepsLoader.cs", "if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.Android)", "if (QuestStandalonePlatform.Enabled && UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsPlayer)", "android-deps"),
        ("owner-session-not-checked", "OpenXRBootstrap.cs", " || !QuestStandalonePlatform.SessionRunning", "", "owner-session-gate"),
        ("display-running-not-checked", "OpenXRBootstrap.cs", " || !displays.Any(d => d.running)", "", "display-running-gate"),
        ("input-running-not-checked", "OpenXRBootstrap.cs", " || !inputs.Any(i => i.running)", "", "input-running-gate"),
        ("greenscreen-quest-clear", "QuestStandalonePlatform.cs", "return Color.clear;", "return new Color(0, 1, 0, 1);", "native-alpha"),
        ("per-frame-native-request", "QuestStandalonePlatform.cs", "if (_lastPassthroughRequest != wanted)", "if (true)", "native-dedupe"),
        ("false-disable-result", "QuestStandalonePlatform.cs", "bool matched = _isPassthroughActive!() == wanted;", "bool matched = _isPassthroughActive!();", "native-disable"),
        ("external-manager-stop", "OpenXRBootstrap.cs", "QuestStandalonePlatform.SetPassthrough(false);", "{ QuestStandalonePlatform.SetPassthrough(false); XRGeneralSettings.Instance.Manager.StopSubsystems(); }", "external-teardown"),
        ("android-guard-removed", "QuestStandalonePlatform.cs", "Application.platform == RuntimePlatform.Android && _resourceDirectory != null", "_resourceDirectory != null", "configured-desktop-gate"),
        ("session-epoch-not-invalidated", "QuestStandalonePlatform.cs", "if (!_havePassthroughSessionGeneration || generation != _lastPassthroughSessionGeneration)", "if (!_havePassthroughSessionGeneration)", "native-session-recreate"),
        ("disabled-head-reported-ready", "QuestStandalonePlatform.cs", "VRRigDriver.HeadCamera != null && VRRigDriver.HeadCamera.isActiveAndEnabled", "VRRigDriver.HeadCamera != null", "active-head-gate"),
    ):
        if original[target].count(before) != 1:
            raise RuntimeError("Mutation binding drift: " + name)
        mutated = dict(original)
        mutated[target] = mutated[target].replace(before, after, 1)
        cases.append((name, mutated, expected))
    # This binding proves the tested clear-color implementation is the actual MR
    # camera authority, while full rendering/backing parity remains a Unity/HW gate.
    mixed_reality = core / "MixedReality/MixedReality.cs"
    render_quality = (root / "src/GloomhavenVR/Rig/RenderQuality.cs").read_text()
    eye_apply = render_quality.split("private static void ApplyEyeScale(bool canWriteRenderResources)", 1)[1].split("private static string VerifyEyeScaleBound()", 1)[0]
    guard = "if (QuestEyeResolution.TryApply(wanted))\n            return;"
    if guard not in eye_apply or "XRSettings.eyeTextureResolutionScale =" in eye_apply or eye_apply.index(guard) > eye_apply.index("float viewport ="):
        raise SystemExit("FAIL actual Quest controller must precede the desktop viewport path without live native allocation")
    if "Color key = QuestStandalonePlatform.MixedRealityClearColor(KeyColor.Value);" not in mixed_reality.read_text():
        raise SystemExit("FAIL actual MR Tick is not bound to the tested Quest clear policy")
    evidence = {"sources": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in sources.items()},
                "mixedRealitySource": hashlib.sha256(mixed_reality.read_bytes()).hexdigest(), "cases": []}
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    print("Quest platform lifecycle evidence: " + str(run), flush=True)
    for name, contents, expected in cases:
        case = run / name
        case.mkdir()
        harness = case / "fixture"
        shutil.copytree(root / "tests/QuestPlatform.Tests", harness, ignore=shutil.ignore_patterns("bin", "obj"))
        for filename, source in contents.items():
            (case / filename).write_text(source)
        result = subprocess.run([dotnet, "run", "--project", str(harness / "QuestPlatform.Tests.csproj"),
                                 "--configuration", "Release", "--property:RuntimeSource=" + str(case),
                                 "--", str(case / "resources")], text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        passed = (result.returncode == 0 and "PASS Quest platform/core lifecycle:" in result.stdout) if not expected else (result.returncode != 0 and expected in result.stdout)
        evidence["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
        (run / "results.json").write_text(json.dumps(evidence, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        if not expected:
            print(next(line for line in result.stdout.splitlines() if line.startswith("PASS Quest platform/core lifecycle:")), flush=True)
        else:
            print("PASS rejected " + name + " at " + expected, flush=True)
    print("PASS production core lifecycle and " + str(len(cases) - 1) + " platform defect controls")


if __name__ == "__main__":
    main()
