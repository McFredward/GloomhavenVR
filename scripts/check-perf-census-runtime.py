#!/usr/bin/env python3
"""Exercise production incremental census and pickup timing in Unity 2021.3.5.

Binds complete original census/texture sources and original scope, gate, hover,
reach and pickup methods. Only external game loading context/hand input is a seam;
scene roots, hierarchy, components, colliders and materials are real Unity objects.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FILES = {name: "src/GloomhavenVR/Core/Perf/" + name for name in (
    "PerfSceneProfile.cs", "PerfSceneProfile.Incremental.cs", "PerfTextureCensus.cs", "LodGroupCensus.cs")}
FILES["PerfNativeLoopProbe.cs"] = "src/GloomhavenVR/Core/Perf/PerfNativeLoopProbe.cs"
FILES["PerfSpikeDetails.cs"] = "src/GloomhavenVR/Core/Perf/PerfSpikeDetails.cs"
FILES["ScenarioInteractionPreparation.cs"] = "src/GloomhavenVR/Core/ScenarioInteractionPreparation.cs"
FILES["ScenarioRoomLoading.cs"] = "src/GloomhavenVR/Core/ScenarioRoomLoading.cs"
FILES["IGrabbable.cs"] = "src/GloomhavenVR/Hands/Interact/IGrabbable.cs"
CONTROLS = (
    ("calibration-as-native-frame", "PerfNativeLoopProbe.cs", "if (enabled && !_installed) Install();\n        _spikeCapture = enabled;", "_spikeCapture = enabled;\n        if (enabled && !_installed) Install();", "first native frame excludes synthetic hook calibration"),
    ("stop-spike-at-summary-cap", "PerfNativeLoopProbe.cs", "if (!_sampling && !_spikeCapture)", "if (!_sampling)", "callbacks after summary cap still reach exact frame attribution"),
    ("retain-native-previous-frame", "PerfNativeLoopProbe.cs", "target.FrameCalls = 0;", "// retain prior frame", "callbacks after summary cap still reach exact frame attribution"),
    ("omit-native-message-ledger", "PerfNativeLoopProbe.cs", "message.Calls++;", "// omit original message", "original message labels survive nested callbacks after summary cap"),
    ("retain-native-message-ledger", "PerfNativeLoopProbe.cs", "message.Calls = message.Exceptions = 0;", "// retain original message", "native message ledgers clear before the next frame"),
    ("swallow-native-message-failure", "PerfNativeLoopProbe.cs", "return __exception;", "return null;", "message finalizer observes failures without changing original callbacks or exceptions"),
    ("charge-background-native-message", "PerfNativeLoopProbe.cs", "if (Thread.CurrentThread.ManagedThreadId != _mainThread)", "if (false)", "off-main native dispatch preserves callbacks without charging the main-thread ledger"),
    ("ignore-spike-debug", "PerfSpikeDetails.cs", "on &= VRLog.WantsDebug;", "on &= true;", "ordinary logging leaves spike resources disabled"),
    ("preparation-gates-native-load", "ScenarioInteractionPreparation.cs", "if (loading)", "if (false)", "preparation waits for native loading without writing native flags"),
    ("restart-completed-preparation", "ScenarioInteractionPreparation.cs", "if (ScenarioCardPreparation.Failures > 0)", "_begun = false; if (ScenarioCardPreparation.Failures > 0)", "native reveal event immediately publishes the loading indicator state"),
    ("skip-native-wall-mask-preparation", "ScenarioInteractionPreparation.cs", "WallSegmentFade.PreparePresentationMasks();", "/* omit fixed native mask preparation */", "native wall masks prepare once under the spinner before heavy card or figure jobs"),
    ("room-ignores-native-busy", "ScenarioRoomLoading.cs", "&& entity.IsBusy)", "&& false)", "room spinner follows native generation before preparing new actors"),
    ("room-uses-enabled-as-loading", "ScenarioRoomLoading.cs", "if (LoadedMaterials?.GetValue(request) is Material[] loaded)", "if (!request.Renderer.enabled && LoadedMaterials?.GetValue(request) is Material[] loaded)", "room readiness uses original pending materials even when renderer is enabled"),
    ("refresh-cancels-census", "ScopeMethods.cs", "invalidateSceneCensus: false", "invalidateSceneCensus: true", "adaptive refresh preserves the same in-progress census"),
    ("setting-keeps-census", "ScopeMethods.cs", "if (invalidateSceneCensus)", "if (false && invalidateSceneCensus)", "graphics changes still cancel incremental inventories"),
    ("refresh-keeps-pacing-window", "ScopeMethods.cs", "if (changed)\n            MarkChange", "if (false && changed)\n            MarkChange", "refresh closes the old pacing window before adopting the new rate"),
    ("omit-profile-fault-cancel", "ScopeMethods.cs", "PerfSceneProfile.Cancel();\n            _sceneProfileFaulted = true;", "_sceneProfileFaulted = true;", "profile summary fault cancels pending job and completed roster"),
    ("queue-after-profile-fault", "ScopeMethods.cs", "if (_sceneProfileFaulted)", "if (false)", "profile summary fault cannot schedule a new census"),
    ("unsliced-textures", "PerfTextureCensus.cs", "yield return null;\n            Surf s = Surfaces[i];", "Surf s = Surfaces[i];", "texture native population is sliced per surface"),
    ("missing-lightweight-roster", "PerfSceneProfile.Incremental.cs", "bool full = PerfConfig.SceneProfileOn;", "bool full = PerfConfig.SceneProfileOn; if (!full) return;", "SceneProfile off still schedules lightweight census"),
    ("retain-zoom-roster", "PerfSceneProfile.Incremental.cs", "PerfFrameSplit.ClearCensusRoster();", "// negative control: retain old epoch roster", "graphics changes drop the completed Zoom roster"),
    ("unbounded-pump", "PerfSceneProfile.Incremental.cs", "i < _objectsPerFrame", "i < 10000000", "object-count budget limits a native traversal slice"),
    ("missing-persistent-scene", "PerfSceneProfile.Incremental.cs", ": _persistentScene;", ": default;", "persistent scene renderer participates"),
    ("count-inactive", "PerfSceneProfile.Incremental.cs", "if (!active) continue;", "if (false) continue;", "active renderer population agrees with original Unity census"),
    ("ignore-debug", "PerfSceneProfile.Incremental.cs", "!VRLog.WantsDebug ||", "false ||", "Debug off cancels inventory references"),
    ("omit-hover-timing", "GrabMethods.cs", '"Hands.NearGrip.HoverCallbacks"', '"NegativeControl.OmittedHoverTiming"', "hover callback stage exists and is priced"),
    ("duplicate-gate", "GrabMethods.cs", "return target.CanGrab;", "bool ignored = target.CanGrab; return target.CanGrab;", "eligibility callback executes once"),
)


def braced(source, marker):
    if source.count(marker) != 1:
        raise RuntimeError("Production extraction marker drift: " + marker)
    start = source.index(marker)
    opening = source.index("{", start)
    depth, at = 1, opening + 1
    while depth:
        depth += (source[at] == "{") - (source[at] == "}")
        at += 1
    return source[start:at]


def bound_sources(root):
    bound = {name: (root / path).read_text() for name, path in FILES.items()}
    monitor = (root / "src/GloomhavenVR/Core/Perf/PerfMonitor.cs").read_text()
    sample = braced(monitor, "    private static void Sample()")
    assert sample.index("PerfSpikeDetails.RollFrame(") < sample.index("RefreshBudget();"), "Spike snapshot precedes window changes"
    assert "PerfSpikeDetails.Append(sb);" in braced(monitor, "    private static void LogSpike("), "Only the existing bounded SPIKE emitter formats details"
    assert "PerfSpikeDetails.Shutdown();" in braced(monitor, "    internal static void Shutdown()"), "Shutdown releases continuous diagnostic recorders"
    assert "ScenarioInteractionPreparation.Install(_hostGo);" in (root / "src/GloomhavenVR/Core/CoreModule.cs").read_text()
    loading = (root / "src/GloomhavenVR/WorldUI/LoadingIndicator.cs").read_text()
    assert "(nativeGameLoading || ScenarioInteractionPreparation.IsPreparing)" in loading
    assert "TickLoadPriority(nativeGameLoading &&" in loading, "post-load preparation restores ordinary native async priority"
    members = [braced(monitor, m) for m in (
        "    private sealed class Step", "    internal static long BeginStep()", "    internal static void EndStep(",
        "    internal readonly struct Measure", "    private static void LogSceneProfile()", "    private static void LogSplit(",
        "    internal static void MarkChange(", "    private static void RefreshBudget()")]
    bound["ScopeMethods.cs"] = "using System; using System.Collections.Generic; using System.Diagnostics; using System.Text; using UnityEngine; namespace GloomhavenVR.Core; internal static partial class PerfMonitor { internal static bool StepsActive=true; private static int _depth; private static double _frameModSeconds; private const string Scope0=\"Perf\"; private static bool _sceneProfileFaulted,_figureBoundarySummary; private static readonly System.Text.StringBuilder Sb=new(); internal static void ProfileSummary()=>LogSceneProfile(); internal static void SplitSummary()=>LogSplit(30,10); internal static void ResetFault()=>_sceneProfileFaulted=false; private static readonly Dictionary<string,Step> Steps=new(); private static readonly List<Step> StepOrder=new(); internal static Measure Scope(string name)=>new(name); internal static int Depth=>_depth; internal static int Calls(string name)=>Steps.TryGetValue(name,out Step step)?step.FrameCalls:0; internal static double Ms(string name)=>Steps.TryGetValue(name,out Step step)?step.FrameSeconds*1000:0; internal static void Reset(){Steps.Clear();StepOrder.Clear();_frameModSeconds=0;}\n" + "\n".join(members) + "}\n"
    split = (root / "src/GloomhavenVR/Core/Perf/PerfFrameSplit.cs").read_text()
    zoom = (root / "src/GloomhavenVR/Core/Perf/PerfFrameSplit.Zoom.cs").read_text()
    # Expression-bodied endpoints are copied exactly; state adoption uses the original
    # SeedRoster/DropRoster methods, rather than a test-only replacement algorithm.
    expression = []
    for name in ("AppendSceneCensus", "AdoptCensusRoster", "ClearCensusRoster"):
        match = re.search(r"    internal static void " + name + r"\([^;]+;", split)
        if not match: raise RuntimeError("Endpoint marker drift: "+name)
        expression.append(match[0])
    members = [braced(zoom,m) for m in ("    private static void SeedRoster(", "    private static void DropRoster()")]
    bound["RosterMethods.cs"] = "using UnityEngine; namespace GloomhavenVR.Core; internal static class PerfFrameSplit { internal static bool HasWindow=>true; internal static bool NativeProbeComplete=>true; internal static int NativeProbeCapturedFrames=>0; internal static void AppendSplit(System.Text.StringBuilder sb,float seconds,float mean,bool capture){sb.Append(\"SPLIT fixture external render window\");} private static Renderer[]? _roster; private static bool[] _rosterSeen=System.Array.Empty<bool>(); private static int _rosterCursor,_rosterVisible;private static bool _rosterReady; internal static Renderer[]? Roster=>_roster; internal static bool[] Shadow=>_rosterSeen; internal static int Visible=>_rosterVisible;\n"+"\n".join(expression+members)+"}\n"
    grab = (root / "src/GloomhavenVR/Hands/Interact/ProximityGrabber.cs").read_text()
    members = [braced(grab, m) for m in (
        "    private void BeginGrab(", "    private static float ReachDistance(",
        "    private void SetHighlighted(", "    private static bool CanGrabNow(", "    private static bool AllowsHandNow(")]
    bound["GrabMethods.cs"] = (root / "scripts/perf-census-runtime/GrabBoundary.cs").read_text().replace("// ORIGINAL_METHODS", "\n".join(members))
    return bound, {"PerfMonitor.cs": hashlib.sha256(monitor.encode()).hexdigest(),
                   "ProximityGrabber.cs": hashlib.sha256(grab.encode()).hexdigest(),
                   "PerfFrameSplit.cs": hashlib.sha256(split.encode()).hexdigest(),
                   "PerfFrameSplit.Zoom.cs": hashlib.sha256(zoom.encode()).hexdigest(),
                   "FigureOverlay.cs": hashlib.sha256((root / "src/GloomhavenVR/Board/FigureGrab/FigureOverlay.cs").read_bytes()).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--no-negative-controls", action="store_true")
    parser.add_argument("--control", action="append", choices=[c[0] for c in CONTROLS], help="Run production and only selected causal controls")
    args = parser.parse_args()
    if args.control and args.no_negative_controls: parser.error("--control and --no-negative-controls are exclusive")
    out = ROOT / ".planning/debug/perf-census-runtime"
    out.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=out))
    bound, originals = bound_sources(args.source_root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()),
        "sha256": {n: hashlib.sha256(t.encode()).hexdigest() for n,t in bound.items()},
        "original_complete_sources": originals}, indent=2) + "\n")
    native = ROOT / "ressources/GH_Data/Managed"
    refs = "".join('<Reference Include="' + p.stem + '"><HintPath>' + str(p) + '</HintPath><Private>false</Private></Reference>' for p in native.glob("UnityEngine*.dll"))
    harmony = Path.home() / ".nuget/packages/harmonyx/2.7.0/lib/net45/0Harmony.dll"
    refs += '<Reference Include="0Harmony"><HintPath>' + str(harmony) + '</HintPath><Private>false</Private></Reference>'
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    variants = [("production", None)] + ([] if args.no_negative_controls else [(c[0],c) for c in CONTROLS if not args.control or c[0] in args.control])
    manifest = {"result": str(run / "results.txt"), "cases": []}
    for name, control in variants:
        case = run / name
        case.mkdir()
        for filename, source in bound.items():
            if control and filename == control[1]:
                if control[2] not in source: raise RuntimeError("Control marker drift: " + name)
                source = source.replace(control[2],control[3])
            (case / filename).write_text(source)
        for filename in ("Boundaries.cs", "Program.cs", "WindowBoundary.cs", "SpikePreparationFixture.cs"):
            shutil.copyfile(ROOT / "scripts/perf-census-runtime" / filename, case / filename)
        project = case / "Fixture.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework><LangVersion>latest</LangVersion><Nullable>enable</Nullable><NoWarn>0649;0414;0162</NoWarn><AssemblyName>PerfCensus_' + name.replace('-','_') + '</AssemblyName><GenerateDocumentationFile>false</GenerateDocumentationFile><EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems></PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>')
        result = subprocess.run([dotnet,"build",str(project),"-c","Release","--nologo"],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        (case / "build.log").write_text(result.stdout)
        if result.returncode:
            print(result.stdout)
            return result.returncode
        manifest["cases"].append({"name":name,"dll":str(case / "bin/Release/net472" / ("PerfCensus_" + name.replace('-','_') + '.dll')),"expected":control[-1] if control else ""})
        print("Compiled", name, flush=True)
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    runner = (ROOT / "scripts/town-service-interaction-runtime/Editor/InteractionRunner.cs").read_text()
    # Harmony's native detour dependencies are already restored. Load them before fixtures;
    # native Unity hooks are exercised, not replaced with test-specific observer bodies.
    runtime_deps = [Path.home()/'.nuget/packages/mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll',
                    Path.home()/'.nuget/packages/monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll',
                    Path.home()/'.nuget/packages/monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll', harmony]
    loader = "\n".join('        Assembly.LoadFile(@"' + str(p) + '");' for p in runtime_deps)
    runner = runner.replace("ran = true; bool passed = true;", "ran = true; bool passed = true;\n" + loader)
    runner = runner.replace("EditorSettings.enterPlayModeOptionsEnabled = true;", '''
        var first = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(first, "Assets/Prelude.unity");
        var test = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(test, "Assets/Census.unity");
        EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene("Assets/Prelude.unity",true),new EditorBuildSettingsScene("Assets/Census.unity",true)};
        EditorSettings.enterPlayModeOptionsEnabled = true;''')
    runner = runner.replace("EditorApplication.Exit(passed ? 0 : 1);", 'SpikeMarkerRunner.Start(Assembly.LoadFile(manifest.cases[0].dll), manifest.result, passed);')
    (project / "Assets/Editor/InteractionRunner.cs").write_text(runner)
    shutil.copyfile(ROOT / "scripts/perf-census-runtime/SpikeMarkerRunner.cs", project / "Assets/Editor/SpikeMarkerRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest,indent=2)+"\n")
    unity = os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")
    result = subprocess.run([unity,"-batchmode","-nographics","-projectPath",str(project),"-executeMethod","InteractionRunner.Start","-interactionManifest",str(manifest_path),"-logFile",str(run / "unity.log")],timeout=240,stdout=subprocess.DEVNULL)
    print((run / "results.txt").read_text() if (run / "results.txt").exists() else "No Unity results")
    print("Evidence:",run)
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
