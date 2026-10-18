#!/usr/bin/env python3
"""Run bounded Quest scenario evidence against real Unity cameras and meshes."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import signal
import time
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from town_run_retention import cleanup_completed_run


def reap_fixture_group(group):
    """Stop/reap this dedicated subreaper's fixture descendants before cleanup.

    Roslyn may create another session. A process-group-only wait would miss it;
    after Unity exits Linux reparents that orphan to this runner, not PID1.
    """
    reaped, adopted = [], set()
    for signum in (signal.SIGTERM, signal.SIGKILL):
        try: os.killpg(group, signum)
        except ProcessLookupError: pass
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            # This process launches only the one fixture; every adopted child
            # belongs to it. Recheck PPid immediately before signalling so no
            # shared compiler or other agent's process can become a target.
            for entry in Path("/proc").iterdir():
                if not entry.name.isdigit(): continue
                try:
                    stat = (entry / "stat").read_text().rsplit(")", 1)[1].split()
                    if int(stat[1]) != os.getpid(): continue
                    adopted.add(int(entry.name))
                    os.kill(int(entry.name), signum)
                except (FileNotFoundError, ProcessLookupError, PermissionError):
                    pass
            try:
                child, status = os.waitpid(-1, os.WNOHANG)
            except ChildProcessError:
                return {"ownedProcessGroup": group, "adoptedChildren": sorted(adopted),
                        "reapedChildren": reaped, "remainingChildren": 0}
            if child:
                reaped.append({"pid": child, "status": status})
            else:
                time.sleep(.05)
    raise RuntimeError("Owned Unity fixture descendants could not be reaped before cleanup")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--control", choices=("desktop-gate", "launch-cap", "gpu-transform", "gpu-native-light", "gpu-private-cleanup", "gpu-emission-clone", "renderer-state", "material-admission", "entity-parameter-property",
        "lifecycle-desktop", "lifecycle-inactive", "lifecycle-native-handle", "lifecycle-unrelated-handle", "lifecycle-preview-content", "lifecycle-native-loading", "lifecycle-repeat", "lifecycle-native-content",
        "loading-boundary", "loading-visible-scope", "loading-busy", "loading-material-assignment", "loading-occlusion-object", "loading-occlusion-tile", "loading-material-room", "loading-blocker-evidence", "loading-original-hidden-wait", "lifecycle-playerloop-focus", "lifecycle-playerloop-window", "lifecycle-playerloop-pause", "lifecycle-playerloop-install", "lifecycle-playerloop-failure"))
    parser.add_argument("--gpu", action="store_true")
    parser.add_argument("--playerloop", action="store_true")
    parser.add_argument("--loading", action="store_true")
    args = parser.parse_args()
    if sum((args.gpu, args.playerloop, args.loading)) > 1:
        parser.error("GPU, PlayerLoop and original loading fixtures have separate execution modes")
    if args.control and args.control.startswith("loading-") and not args.loading:
        parser.error("Loading controls require --loading")
    if args.control and args.control.startswith("lifecycle-playerloop-") and not args.playerloop:
        parser.error("PlayerLoop controls require --playerloop")
    base = ROOT / ".planning/debug/quest-scenario-evidence"
    base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=base))
    project = run / "unity"
    editor = project / ("Assets/Runtime" if args.playerloop or args.loading else "Assets/Editor")
    editor.mkdir(parents=True)
    path = ROOT / "src/GloomhavenVR/Rig/VRRigDriver.QuestScenario.cs"
    source = path.read_text()
    if args.control == "desktop-gate":
        source = source.replace("!QuestStandalonePlatform.Enabled || !VRLog.Wants", "!VRLog.Wants", 1)
    elif args.control == "launch-cap":
        source = source.replace("|| _questScenarioRecords >= 8 || _questScenarioSamples >= 2", "|| _questScenarioSamples >= 2", 1)
        source = source.replace("if (_questScenarioRecords >= 8) return;", "/* injected launch cap removal */", 1)
    elif args.control == "renderer-state":
        source = source.replace("if (renderer.enabled) componentEnabled++;", "if (on) componentEnabled++;", 1)
    elif args.control == "entity-parameter-property":
        source = source.replace('GetProperty("PartialParameterOverride")', 'GetField("PartialParameterOverride")', 1)
    elif args.control == "material-admission":
        source = source.replace("if (shown) unsupportedShown++;", "unsupportedShown++;", 1)
    (editor / "Scenario.cs").write_text("#nullable enable\n" + source.replace("namespace GloomhavenVR.Rig;", "namespace GloomhavenVR.Rig {") + "\n}\n")
    gpu_source = (ROOT / "src/GloomhavenVR/Rig/VRRigDriver.QuestScenarioGpu.cs").read_text()
    if args.control == "gpu-transform":
        gpu_source = gpu_source.replace("renderer.localToWorldMatrix, material", "Matrix4x4.Translate(Vector3.one * 1000f) * renderer.localToWorldMatrix, material", 1)
    elif args.control == "gpu-native-light":
        before = "light.intensity = original.intensity;"
        if gpu_source.count(before) != 1: raise AssertionError("Native copied-light binding drift")
        gpu_source = gpu_source.replace(before, "light.intensity = 0;", 1)
    elif args.control == "gpu-private-cleanup":
        before = "if (copy != null) copy.enabled = false;"
        if gpu_source.count(before) != 1: raise AssertionError("Private native renderer cleanup binding drift")
        gpu_source = gpu_source.replace(before, "/* injected private renderer cleanup omission */", 1)
    elif args.control == "gpu-emission-clone":
        before = 'copies[index].SetFloat("_EmissionMap", 1f);'
        if gpu_source.count(before) != 1: raise AssertionError("Native emission clone binding drift")
        gpu_source = gpu_source.replace(before, 'copies[index].SetFloat("_EmissionMap", 0f);', 1)
    (editor / "ScenarioGpu.cs").write_text("#nullable enable\n" + gpu_source.replace("namespace GloomhavenVR.Rig;", "namespace GloomhavenVR.Rig {") + "\n}\n")
    lifecycle_source = (ROOT / "src/GloomhavenVR/Core/QuestProceduralLifecycle.cs").read_text()
    lifecycle_controls = {
        "lifecycle-desktop": ('!QuestStandalonePlatform.Enabled ? "desktop"', 'false ? "desktop"',
                              "Desktop cannot recover a native component"),
        "lifecycle-inactive": ('!engine.gameObject.activeInHierarchy ? "inactive-owner"', 'false ? "inactive-owner"',
                               "Inactive native owner is never activated"),
        "lifecycle-native-handle": ("&& entity.m_EntityHandle == 0 && !entity.IsBusy", "&& !entity.IsBusy",
                                    "An existing native handle prevents initial-generation recovery"),
        "lifecycle-unrelated-handle": ("bool requested = false;", 'foreach (ApparanceEntity registered in engine.EntityComponents)\n            if (registered != null && registered.m_EntityHandle != 0) { admission = "existing-native-handle"; return false; }\n        bool requested = false;',
                                       "An unrelated registered native handle cannot block the visible ungenerated room"),
        "lifecycle-preview-content": ("if (tile == null || tile.visibility != ProceduralMapTile.Visibility.All || !tile.isActiveAndEnabled) continue;", "if (tile == null) continue;",
                                      "Existing native preview content cannot block the visible ungenerated room"),
        "lifecycle-native-loading": (': loading == null ? "scene-controller-absent"', ': loading == null ? "scene-controller-absent" : loading.IsLoading || loading.ScenarioIsLoading ? "native-loading"',
                                     "Original generation resumes during loading without changing native loading ownership"),
        "lifecycle-repeat": ('Attempted.Contains(engine.GetInstanceID()) ? "already-attempted"', 'false ? "already-attempted"',
                             "A second disable owner is never fought or retried"),
        "lifecycle-native-content": ("if (tile.IsPlacementCompleted || (generated != null && generated.childCount != 0))",
                                     "if (tile.IsPlacementCompleted)",
                                     "Generated native content prevents initial-generation recovery"),
        "lifecycle-playerloop-window": ("try { Tick(now, _paused); }", "try { if (Application.isFocused) Tick(now, _paused); }",
                                        "Actual PlayerLoop reports its live driver and pause admission"),
        "lifecycle-playerloop-focus": ("try { Tick(now, _paused); }", "try { if (VRSession.InputFocus != false) Tick(now, _paused); }",
                                       "Actual PlayerLoop reports its live driver and pause admission"),
        "lifecycle-playerloop-pause": ("try { Tick(now, _paused); }", "try { Tick(now, false); }",
                                       "Actual OS pause messages prevent native Start and Update"),
        "lifecycle-playerloop-failure": ('enabled = false;\n                VRLog.Alert', '/* injected observer keeps throwing */\n                VRLog.Alert',
                                         "Observer failure disables only its owned driver and reports one normal-level anomaly"),
        "lifecycle-playerloop-install": ('_driver = owner.AddComponent<Driver>();', '_driver = null;',
                                         "Real install creates the actual lifecycle driver"),
    }
    if args.control in lifecycle_controls:
        before, after, _ = lifecycle_controls[args.control]
        if lifecycle_source.count(before) != 1:
            raise AssertionError("Lifecycle mutation binding drift: " + args.control)
        lifecycle_source = lifecycle_source.replace(before, after, 1)
    (editor / "Lifecycle.cs").write_text("#nullable enable\n" + lifecycle_source.replace("namespace GloomhavenVR.Core;", "namespace GloomhavenVR.Core {") + "\n}\n")
    requests_source = (ROOT / "src/GloomhavenVR/Core/QuestProceduralRequests.cs").read_text()
    (editor / "Requests.cs").write_text("#nullable enable\n" + requests_source.replace("namespace GloomhavenVR.Core;", "namespace GloomhavenVR.Core {") + "\n}\n")
    loading_source = (ROOT / "src/GloomhavenVR/Core/QuestProceduralLoading.cs").read_text()
    loading_controls = {
        "loading-boundary": ("while (HasPendingContent(maps));", "while (false);",
                             "Original load continuation cannot close before requested placement completion"),
        "loading-visible-scope": ("room.visibility != ProceduralMapTile.Visibility.All", "false",
                                  "Hidden and preview requests do not keep the native loader open"),
        "loading-busy": ("!placement.IsPlacementCompleted || entity.IsBusy", "!placement.IsPlacementCompleted",
                         "Original busy state keeps the loading boundary open after an early placement callback"),
        "loading-material-assignment": ("if (!found)", "if (false && !found)",
                                        "Completed material handles cannot close before original renderer assignment"),
        "loading-occlusion-object": ("renderer.GetComponent<ObjectOcclusionVolume>() == null", "true",
                                     "Native object occlusion inputs do not wait for their unused Addressables material"),
        "loading-occlusion-tile": ("renderer.GetComponentInParent<TilesOcclusionVolume>() == null", "true",
                                   "Native tile occlusion ownership keeps auxiliary materials outside world readiness"),
        "loading-material-room": ("renderer.GetComponentInParent<ProceduralMapTile>() != room", "false",
                                  "Cross-room material references cannot make an unrevealed preview block visible readiness"),
        "loading-blocker-evidence": ("return detail;", 'return "none";',
                                     "Loading evidence names the actual pending room before placement completion"),
        "loading-original-hidden-wait": ("(native as System.IDisposable)?.Dispose();", "yield return native;",
                                         "Quest loading completes from actual requested readiness without hidden preview frame waits"),
    }
    if args.control in loading_controls:
        before, after, _ = loading_controls[args.control]
        if loading_source.count(before) != 1: raise AssertionError("Loading mutation binding drift")
        loading_source = loading_source.replace(before, after, 1)
    (editor / "Loading.cs").write_text("#nullable enable\n" + loading_source.replace("namespace GloomhavenVR.Core;", "namespace GloomhavenVR.Core {") + "\n}\n")
    # Exercise the actual retained original coroutine text; only engine/assets,
    # networking and placement callbacks are controlled fixture boundaries.
    original_root = ROOT / "ressources"
    original_root = original_root.resolve().parent / "decompiled/GH.Runtime"
    runtime_source = (original_root / "UnityGameEditorRuntime.cs").read_text()
    def original_method(signature):
        start = runtime_source.index(signature)
        opening = runtime_source.index("{", start)
        depth = 1; end = opening + 1
        while depth:
            if runtime_source[end] == "{": depth += 1
            elif runtime_source[end] == "}": depth -= 1
            end += 1
        return runtime_source[start:end]
    load_contract = original_method("public static IEnumerator WaitForLoad()")
    proc_contract = original_method("private static IEnumerator WaitForProcGen()")
    if load_contract.index("yield return WaitForProcGen();") > load_contract.index("DisableLoadingScreen();"):
        raise AssertionError("Original load readiness contract drift")
    (editor / "OriginalLoadContract.cs").write_text("using System.Collections;\nusing UnityEngine;\npublic class UnityGameEditorRuntime {\n" + load_contract + "\n" + proc_contract + "\n}\n")
    for original_name in ("ProceduralPlacementNotifierHandler.cs", "ProceduralPlacementLoadingModel.cs"):
        (editor / original_name).write_bytes((original_root / original_name).read_bytes())
    (editor / "LoadingContractProbe.cs").write_bytes((ROOT / "tests/QuestScenarioEvidence.Tests/LoadingContractProbe.cs").read_bytes())
    plugins = project / "Assets/Plugins"; plugins.mkdir()
    packages = Path.home() / ".nuget/packages"
    deps = (("harmonyx/2.7.0/lib/net45", "0Harmony.dll"),
            ("monomod.runtimedetour/21.12.13.1/lib/net452", "MonoMod.RuntimeDetour.dll"),
            ("monomod.utils/21.12.13.1/lib/net452", "MonoMod.Utils.dll"))
    for folder, name in deps: (plugins / name).write_bytes((packages / folder / name).read_bytes())
    for binary in (packages / "mono.cecil/0.11.4/lib/net40").glob("Mono.Cecil*.dll"):
        (plugins / binary.name).write_bytes(binary.read_bytes())
    (run / "original-contract.json").write_text(json.dumps({
        "source": str(original_root / "UnityGameEditorRuntime.cs"),
        "sha256": hashlib.sha256(runtime_source.encode()).hexdigest(),
        "loadCoroutineSha256": hashlib.sha256(load_contract.encode()).hexdigest(),
        "procGenCoroutineSha256": hashlib.sha256(proc_contract.encode()).hexdigest(),
        "originalCoroutineExecuted": args.loading,
        "nativeEngineExecuted": False,
    }, indent=2) + "\n")
    (project / "Assets/HeadUnlit.shader").write_bytes((ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/Head/HeadUnlit.shader").read_bytes())
    (project / "Assets/NativeEmission.shader").write_bytes((ROOT / "tests/QuestScenarioEvidence.Tests/NativeEmission.shader").read_bytes())
    (editor / "Probe.cs").write_bytes((ROOT / "tests/QuestScenarioEvidence.Tests/Probe.cs").read_bytes())
    if args.playerloop:
        (editor / "PlayerLoopProbe.cs").write_bytes((ROOT / "tests/QuestScenarioEvidence.Tests/PlayerLoopProbe.cs").read_bytes())
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {"com.unity.modules.vr": "1.0.0"} if args.playerloop or args.loading else {}}) + "\n")
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    command = ["/home/claw/unity-2021.3.5/Editor/Unity", "-batchmode", "-force-vulkan" if args.gpu else "-nographics", "-projectPath", str(project),
               "-executeMethod", "QuestProceduralLoadingContractProbe.Run" if args.loading else "QuestProceduralPlayerLoopProbe.Run" if args.playerloop else "QuestScenarioEvidenceProbe.RunGpu" if args.gpu else "QuestScenarioEvidenceProbe.Run", "-logFile", str(run / "unity.log")]
    if args.gpu and not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    log = run / "runner.log"
    log.write_text("Quest scenario evidence: " + str(run) + "\n")
    print(log.read_text(), end="", flush=True)
    # Own one process group and adopt its orphaned grandchildren on Linux.
    # Unity can exit before its Bee/Roslyn children; reaping only the editor
    # process would leave deleted cache files pinned after completion cleanup.
    if sys.platform.startswith("linux"):
        if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0:
            raise OSError(ctypes.get_errno(), "Could not own Unity fixture child cleanup")
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
    code = 1
    try:
        code = process.wait(timeout=180)
        if args.control:
            expected = {"desktop-gate": "Desktop does not observe native scene", "launch-cap": "Recreated rig cannot reset launch cap",
                        "gpu-transform": "Actual unreadable native mesh produces visible GPU pixels",
                        "gpu-native-light": "Private native renderer receives original zero-pixel-cap room lighting",
                        "gpu-private-cleanup": "Private copied geometry is disabled before ordinary rendering resumes",
                        "gpu-emission-clone": "Private native emission arm draws actual texture color without scene lights",
                        "renderer-state": "Inactive and component-disabled native renderers are counted independently",
                        "material-admission": "Excluded native placeholder materials cannot be diagnosed as shown shader failures",
                        "entity-parameter-property": "Native hierarchy and generation admission distinguish a dormant entity from a completed world"}.get(args.control)
            if args.control in lifecycle_controls: expected = lifecycle_controls[args.control][2]
            if args.control in loading_controls: expected = loading_controls[args.control][2]
            if code == 0 or expected is None or ("InvalidOperationException: " + expected) not in (run / "unity.log").read_text():
                raise AssertionError("Native negative control was not rejected by its exact failure assertion")
            (run / "control.json").write_text(json.dumps({"control": args.control, "rejected": True, "expected": expected, "processExit": code}, indent=2) + "\n")
            print("PASS native negative control rejected: " + args.control, flush=True)
            return
        if code:
            raise RuntimeError("Native scenario evidence fixture failed: " + str(run / "unity.log"))
        report = json.loads((project / "ProbeOutput/result.json").read_text())
        if report.get("assertions", 0) < (9 if args.playerloop or args.gpu else 20):
            raise AssertionError("Incomplete scenario evidence fixture")
        (run / "source-hashes.json").write_text(json.dumps({"source": hashlib.sha256(source.encode()).hexdigest(),
            "probe": hashlib.sha256((editor / "Probe.cs").read_bytes()).hexdigest(),
            "gpuSource": hashlib.sha256(gpu_source.encode()).hexdigest(),
            "lifecycleSource": hashlib.sha256(lifecycle_source.encode()).hexdigest(),
            "loadingSource": hashlib.sha256(loading_source.encode()).hexdigest(),
            "requestsSource": hashlib.sha256(requests_source.encode()).hexdigest(),
            "graphics": "vulkan" if args.gpu else "null", "actualPlayerLoop": args.playerloop or args.loading,
            "engineBoundary": "controlled Unity component callbacks; original managed/native engine not executed",
            "playerLoopProbe": hashlib.sha256((editor / "PlayerLoopProbe.cs").read_bytes()).hexdigest() if args.playerloop else None,
            "loadingProbe": hashlib.sha256((editor / "LoadingContractProbe.cs").read_bytes()).hexdigest() if args.loading else None,
            "originalLoadCoroutinesExecuted": args.loading, "actualHarmonyPatch": args.loading,
            "assertions": report["assertions"], "hardwareVerified": False}, indent=2) + "\n")
        print("PASS native scenario evidence: assertions=" + str(report["assertions"]), flush=True)
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        children = reap_fixture_group(process.pid)
        (run / "process-cleanup.json").write_text(json.dumps(children, indent=2) + "\n")
        cleanup = cleanup_completed_run("quest-scenario-evidence", code, log, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")


if __name__ == "__main__":
    main()
