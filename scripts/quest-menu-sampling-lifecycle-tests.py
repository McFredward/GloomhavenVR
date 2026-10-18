#!/usr/bin/env python3
"""Exercise full production loading Tick through real native scene and camera lifetimes."""
import argparse
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from town_run_retention import cleanup_completed_run


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--owned-project", type=Path, required=True)
    parser.add_argument("--control", choices=("retire-at-controller", "copy-boot-rt", "skip-rig-rebuild", "signed-spin"))
    args = parser.parse_args()
    base = ROOT / ".planning/debug/quest-menu-lifecycle"; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=base.resolve()))
    log = run / "runner.log"; log.write_text("Full production loading Tick evidence: " + str(run) + "\n")
    code, process, reaped = 1, None, True
    owned = {}
    try:
        fixture = ROOT / "tests/QuestMenuSampling.Lifecycle.Tests"
        production = run / "production"; production.mkdir()
        loading = (ROOT / "src/GloomhavenVR/WorldUI/LoadingIndicator.cs").read_text()
        controls = {
            "retire-at-controller": ("if (ordinary)", "if (sc != null)", "through SceneController construction before native loading begins"),
            "copy-boot-rt": ("!QuestStandalonePlatform.Enabled && (!AdoptOwnedCopy(baseArt)", "(!AdoptOwnedCopy(baseArt)", "Quest boot uses the hardware-qualified native Sprite consumer without a copied RT"),
            "skip-rig-rebuild": ("if (_root == null)\n            BuildVisual();", "if (_baseArt == null)\n            BuildVisual();", "Actual Tick keeps the spinner attached and visible through native head-rig reconstruction"),
            "signed-spin": ("QuestStandalonePlatform.Enabled && value != 0f", "false && value != 0f", "Actual Tick preserves the original signed native spinner tuning"),
        }
        expected = ""
        if args.control:
            before, after, expected = controls[args.control]
            assert loading.count(before) == 1, "Control binding drift: " + args.control
            loading = loading.replace(before, after, 1)
        rows = {"LoadingIndicator.cs": loading}
        for path in ("Core/QuestTextureCopy.cs", "Core/QuestPrimitiveMesh.cs", "WorldUI/Sharpness/LoadingIconGeometry.cs"):
            rows[Path(path).name] = (ROOT / "src/GloomhavenVR" / path).read_text()
        for name, text in rows.items(): (production / name).write_text(text)
        source_hashes = {name: hashlib.sha256(text.encode()).hexdigest() for name, text in rows.items()}
        source_hashes.update({str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in fixture.glob("*") if path.is_file()})
        (run / "source-hashes.json").write_text(json.dumps(source_hashes, indent=2) + "\n")
        unity = Path("/home/claw/unity-2021.3.5/Editor/Unity")
        managed = ROOT / "ressources/GH_Data/Managed"
        harmony = Path.home() / ".nuget/packages/harmonyx/2.7.0/lib/net45/0Harmony.dll"
        project = run / "Runtime.csproj"; shutil.copyfile(fixture / "Runtime.csproj", project)
        result = subprocess.run([shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet"), "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            "-p:CaseName=QuestLoadingLifecycle", "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production),
            "-p:UnityManaged=" + str(unity.parent / "Data/Managed"), "-p:GameManaged=" + str(managed), "-p:Harmony=" + str(harmony),
            "-p:UseSharedCompilation=false", "-nodeReuse:false"], capture_output=True, text=True)
        (run / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode: raise RuntimeError(result.stdout + result.stderr + "\nCompilation failure is not a passing control")
        project = run / "unity"; editor = project / "Assets/Editor"; editor.mkdir(parents=True)
        (project / "Assets/Resources").mkdir(); (project / "Assets/Sprite").mkdir(); (project / "Assets/Texture2D").mkdir()
        # Match runtime plugin loading: establish the real-game resolver before
        # loading this assembly. Importing it as a Unity project plugin would
        # cache missing GH.Runtime type resolutions before that boundary exists.
        dll = run / "bin/Release/netstandard2.1/QuestLoadingLifecycle.dll"
        shutil.copyfile(ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestTextureCopy.shader", project / "Assets/Resources/QuestTextureCopy.shader")
        wanted = set()
        source = args.owned_project.resolve(strict=True)
        def copy(path, target):
            target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(path, target)
            owned[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
        for name in ("LoadingBase.asset", "LoadingOverlay.asset"):
            path = source / "Assets/Sprite" / name
            wanted.update(re.findall(r"texture: \{fileID: 2800000, guid: ([a-f0-9]{32}),", path.read_text()))
            for original in (path, Path(str(path) + ".meta")): copy(original, project / "Assets/Sprite" / original.name)
        for meta in (source / "Assets/Texture2D").glob("*.meta"):
            if not wanted: break
            match = re.search(r"^guid: ([a-f0-9]{32})$", meta.read_text(), re.M)
            if match and match[1] in wanted:
                for original in (meta, Path(str(meta)[:-5])): copy(original, project / original.relative_to(source))
                wanted.remove(match[1])
        assert not wanted, "Native spinner texture closure incomplete"
        dependencies = run / "dependencies"; dependencies.mkdir()
        shutil.copyfile(harmony, dependencies / harmony.name)
        manifest = {"dll": str(dll), "result": str(run / "result.json"), "managed": str(managed.resolve()), "dependencies": str(dependencies), "expected": expected}
        (run / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
        (run / "owned-inputs.json").write_text(json.dumps(owned, indent=2) + "\n")
        (editor / "LoadingLifecycleRunner.cs").write_text(r'''using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class LoadingLifecycleRunner {
    [Serializable] private class Input { public string dll, result, managed, dependencies, expected; }
    private static Input input;
    private static bool started;
    private static Assembly fixture;
    private static double deadline;
    public static void Start() {
        string[] args=Environment.GetCommandLineArgs(); input=JsonUtility.FromJson<Input>(File.ReadAllText(args[Array.IndexOf(args,"-lifecycleManifest")+1]));
        AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=> { string name=new AssemblyName(e.Name).Name+".dll"; string path=Path.Combine(input.dependencies,name); if(File.Exists(path)) return Assembly.LoadFrom(path); path=Path.Combine(input.managed,name); return File.Exists(path)&&!name.StartsWith("UnityEngine")&&!name.StartsWith("System.")?Assembly.LoadFrom(path):null; };
        foreach(string name in new[]{"GH.Runtime.dll","GH.Runtime.FirstPass.dll","ThirdParty.dll"}) Assembly.LoadFrom(Path.Combine(input.managed,name));
        var scenes=new EditorBuildSettingsScene[3]; int i=0;
        foreach(string name in new[]{"Intro","Gloomhaven_unified","MainMenu"}) { var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); string path="Assets/"+name+".unity"; EditorSceneManager.SaveScene(scene,path); scenes[i++]=new EditorBuildSettingsScene(path,true); }
        EditorBuildSettings.scenes=scenes; EditorSceneManager.OpenScene("Assets/Intro.unity");
        EditorSettings.enterPlayModeOptionsEnabled=true; EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
        deadline=EditorApplication.timeSinceStartup+120; EditorApplication.update+=Observe; EditorApplication.EnterPlaymode();
    }
    private static void Observe() {
        if(EditorApplication.timeSinceStartup>deadline) { Finish(0,new TimeoutException("Full loading Tick fixture did not complete")); return; }
        if(!EditorApplication.isPlaying||started)return; started=true;
        try { fixture=Assembly.LoadFile(input.dll); fixture.GetType("LoadingLifecycleProgram").GetMethod("Start").Invoke(null,new object[]{new Action<int,Exception>(Finish)}); }
        catch(Exception e) { while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException; Finish(0,e); }
    }
    private static void Finish(int checks,Exception error) {
        EditorApplication.update-=Observe;
        bool control=!String.IsNullOrEmpty(input.expected), passed=control?error!=null&&error.Message.Contains(input.expected):error==null;
        Type program=fixture==null?null:fixture.GetType("LoadingLifecycleProgram"); int boundary=program==null?0:(int)program.GetProperty("ObservedBoundary").GetValue(null); int samples=program==null?0:(int)program.GetProperty("RenderedSamples").GetValue(null);
        string value="{\"passed\":"+passed.ToString().ToLowerInvariant()+",\"assertions\":"+checks+",\"actualPlayerLoop\":true,\"observedBoundary\":"+boundary+",\"renderedSamples\":"+samples+",\"actualSingleSceneLoad\":"+(boundary>=2).ToString().ToLowerInvariant()+",\"actualAssetCollection\":"+(boundary>=3).ToString().ToLowerInvariant()+",\"fullProductionTick\":true,\"hardwareVerified\":false,\"control\":"+control.ToString().ToLowerInvariant()+"}";
        File.WriteAllText(input.result,value+"\n"); if(error!=null) Debug.LogException(error); EditorApplication.Exit(passed?0:1);
    }
}''')
        (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
        (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
        (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
        if sys.platform.startswith("linux"):
            if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0): raise OSError("Could not own fixture descendants")
        command = [str(unity), "-batchmode", "-force-vulkan", "-projectPath", str(project), "-executeMethod", "LoadingLifecycleRunner.Start", "-lifecycleManifest", str(run / "manifest.json"), "-logFile", str(run / "unity.log")]
        if not os.environ.get("DISPLAY"): command = ["xvfb-run", "-a"] + command
        print("Full loading Tick evidence: " + str(run), flush=True)
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True); reaped=False
        code = process.wait(timeout=240)
    finally:
        if process is not None:
            if process.poll() is None: process.kill(); process.wait()
            spec=importlib.util.spec_from_file_location("sampling_cleanup", ROOT / "scripts/quest-menu-sampling-tests.py")
            helper=importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
            children=helper.reap_fixture_group(process.pid); reaped=True
            (run / "process-cleanup.json").write_text(json.dumps(children,indent=2)+"\n")
        for path, expected_hash in owned.items(): assert hashlib.sha256(Path(path).read_bytes()).hexdigest()==expected_hash, "Read-only input changed"
        receipt=cleanup_completed_run("quest-menu-lifecycle",code,log,ROOT,process_reaped=reaped)
        (run / "cleanup.json").write_text(json.dumps(receipt,indent=2)+"\n")
    result = json.loads((run / "result.json").read_text()) if (run / "result.json").is_file() else {}
    if code or not result.get("passed"): raise SystemExit("FAIL full loading Tick fixture; inspect " + str(run / "unity.log"))
    print("PASS full loading Tick " + json.dumps(result))


if __name__ == "__main__": main()
