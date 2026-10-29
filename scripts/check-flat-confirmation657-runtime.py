#!/usr/bin/env python3
"""Native map confirmation lifecycle with the installed game's unchanged methods.

This bounded suite does not emulate laser arbitration: the independent conversion
transfer suite owns that path. Here the production seat, actual Unity components
and native Show/Hide/confirm/cancel methods establish continuation ordering.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 1
    cursor = brace + 1
    while depth:
        depth += (source[cursor] == "{") - (source[cursor] == "}")
        cursor += 1
    return source[start:cursor]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/flat-confirmation657")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--case", action="append")
    args = parser.parse_args()
    def terminated(signum, frame): raise SystemExit(128 + signum)
    signal.signal(signal.SIGTERM, terminated)
    root = args.source_root.resolve()
    fixture = root / "scripts/flat-confirmation657-runtime"
    managed = root / "ressources/GH_Data/Managed"
    runtime = managed / "GH.Runtime.dll"
    env = os.environ.copy(); env["DOTNET_ROOT"] = str(Path.home() / ".dotnet")
    decompiled = {}
    for name in ("UnityEngine.UI.UIWindow", "UIEnhancementConfirmationBox", "UIItemConfirmationBox"):
        decompiled[name] = subprocess.run([str(Path.home()/".dotnet/tools/ilspycmd"), "-t", name, str(runtime)], check=True, capture_output=True, text=True, env=env).stdout
    window = decompiled["UnityEngine.UI.UIWindow"]
    native_window = "#nullable disable\nusing System;using UnityEngine;using UnityEngine.UI;namespace UnityEngine.UI {public partial class UIWindow {\n"
    for signature in ("public virtual void Show(bool instant)", "public virtual void Hide(bool instant)", "protected virtual void EvaluateAndTransitionToVisualState(VisualState state, bool instant)", "protected virtual void OnTweenFinished()"):
        native_window += method(window, signature) + "\n"
    native_window += "}}\n"
    box = decompiled["UIEnhancementConfirmationBox"]
    native_box = "#nullable disable\nusing System;using UnityEngine;using UnityEngine.UI;using Script.GUI.SMNavigation.States.CampaignMapStates;public partial class UIEnhancementConfirmationBox {\n"
    for signature in ("public void ShowConfirmation(string title, string information, Sprite elementIcon", "public void Hide()", "private void ToPreviousState()", "private void ResetConfirmationBox()", "private void OnConfirm()"):
        native_box += method(box, signature) + "\n"
    native_box += "}\n"
    item = decompiled["UIItemConfirmationBox"]
    native_item = "#nullable disable\nusing System;using UnityEngine;using UnityEngine.UI;using ScenarioRuleLibrary;public partial class UIItemConfirmationBox {\n"
    for signature in ("public void ShowConfirmation(BoxConfirmationType boxConfirmationType", "private void OnCancel()", "private void OnConfirm()", "public void Hide()", "private void ResetConfirmationBox()", "private void OnHidden()"):
        native_item += method(item, signature) + "\n"
    native_item += "}\n"
    path = "src/GloomhavenVR/WorldUI/Composites/MapDialogSeat.cs"
    source = (root/path).read_text()
    old = subprocess.run(["git", "show", "bf3444cb5:"+path], cwd=root, check=True, capture_output=True, text=True).stdout
    owned_before = subprocess.run(["git", "show", "38bef39fd:"+path], cwd=root, check=True, capture_output=True, text=True).stdout
    item_before = subprocess.run(["git", "show", "bdb9206d9:"+path], cwd=root, check=True, capture_output=True, text=True).stdout
    observation_fault = source.replace("MapDialogSeat.ObserveItemBox(__instance);", "Probe.ThrowObservation(__instance);")
    unguarded_fault = observation_fault.replace(method(observation_fault, "private static void Prefix(UIItemConfirmationBox __instance)"),
        "private static void Prefix(UIItemConfirmationBox __instance) { _ = _failureReported; Probe.ThrowObservation(__instance); }")
    if observation_fault == source or unguarded_fault == observation_fault: raise RuntimeError("Prefix failure-path control binding drift")
    mask_source = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceWindowMask.cs").read_text()
    variants = [("production", source, ""),
        ("owned-mask-production", source, ""),
        ("owned-mask-release", owned_before, "owned tick cannot pull the native root out of its zero-alpha mask"),
        ("published656", old, "destination change does not reidentify an existing temple request"),
        ("without-host-hide", source.replace("host.OnHide += seat.HostHidden;", "// Causal control: omitted original host cancellation edge."), "native prompt cancels before next destination enters"),
        ("slow-host-cancel", source.replace("seat.NativeDialog.Hide(instant: true);", "seat.NativeDialog.Hide();"), "departing host completes native cancellation before another request"),
        ("lost-cancel-fade", source.replace("seat.NativeDialog.StartAlphaTween(0f, 0f, ignoreTimeScale: true);", "{ } // Causal control: lost chosen-outcome tween completion."), "pending cancelled fade completes before singleton reuse"),
        ("lost-confirm-fade", source.replace("seat.NativeDialog.StartAlphaTween(0f, 0f, ignoreTimeScale: true);", "{ } // Causal control: lost chosen-outcome tween completion."), "pending confirmed fade completes before singleton reuse"),
        ("item-production", source, ""),
        ("item-published669", item_before, "replacement merchant request before first tick seats on exact new host"),
        ("item-without-request-edge", source.replace("MapDialogSeat.ObserveItemBox(__instance);", "_ = __instance;"), "first native request before any modal tick seats on merchant"),
        ("item-subscribe-once", source.replace("if (ReferenceEquals(_itemBox, box)) return;", "if (_itemBox != null) return;"), "replacement merchant request before first tick seats on exact new host"),
        ("item-stale-event", source.replace("_itemBox!.ConfirmationBoxRequested -= OnItemConfirmationRequested;", "{ } // Causal control: retained obsolete request target."), "replacement detaches the exact old native event target"),
        ("item-stale-provenance", source.replace("        _itemBox = box;\n        ItemSeat.RequestedAs = null;", "        _itemBox = box;"), "unknown request on replacement cannot inherit old merchant provenance"),
        ("item-without-ownership", source.replace("if (window != null && TownServicePresentation.OwnsWindow(window))", "if (window != null && !ReferenceEquals(window, window))", 1), "item ownership leaves native presentation pose and layer untouched"),
        ("item-prefix-guarded-fault", observation_fault, ""),
        ("item-prefix-unguarded-fault", unguarded_fault, "observation failure leaves native Show continuation intact")]
    if any(before == source for name, before, expected in variants if name in ("without-host-hide", "slow-host-cancel")):
        raise RuntimeError("Host cancellation control binding drift")
    if any(before == source for name, before, expected in variants if name.startswith("item-") and expected and name != "item-published669"):
        raise RuntimeError("Item lifecycle control binding drift")
    if args.case:
        if set(args.case)-{entry[0] for entry in variants}: parser.error("Unknown case")
        variants = [entry for entry in variants if entry[0] in args.case]
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-",dir=args.output_dir.resolve()))
    compiler_temp=Path(tempfile.mkdtemp(prefix="ghvr-confirm-",dir="/tmp"));env['TMPDIR']=str(compiler_temp)
    (run/'compiler-temp.json').write_text(json.dumps({'TMPDIR':str(compiler_temp)})+'\n')
    completion=run/'completion.log';completion.write_text('Evidence: '+str(run)+'\n')
    spec=importlib.util.spec_from_file_location('confirmation_project_tools',ROOT/'scripts/menu-depth-character/project_compilers.py')
    project_tools=importlib.util.module_from_spec(spec);spec.loader.exec_module(project_tools)
    spec=importlib.util.spec_from_file_location('confirmation_run_tools',ROOT/'scripts/check-environment-menu-scope.py')
    run_tools=importlib.util.module_from_spec(spec);spec.loader.exec_module(run_tools)
    process=None;project=None;reaped=True;code=1
    try:
        shutil.copytree(fixture,run/"fixture"); fixture=run/"fixture"
        provenance = {"GH.Runtime.dll": hashlib.sha256(runtime.read_bytes()).hexdigest(),
            **{name:hashlib.sha256(value.encode()).hexdigest() for name,value in decompiled.items()},
            "NativeWindowMethods.cs":hashlib.sha256(native_window.encode()).hexdigest(),
            "NativeBoxMethods.cs":hashlib.sha256(native_box.encode()).hexdigest(),
            "NativeItemMethods.cs":hashlib.sha256(native_item.encode()).hexdigest()}
        dependencies = [Path.home()/".nuget/packages"/path for path in (
            "harmonyx/2.7.0/lib/net45/0Harmony.dll", "monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll",
            "monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll", "mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll")]
        hashes={}; cases=[]
        for name, seat, expected in variants:
            build=run/name; production=build/"production"; production.mkdir(parents=True)
            sources={"MapDialogSeat.cs":seat,"NativeWindowMethods.cs":native_window,"NativeBoxMethods.cs":native_box,"NativeItemMethods.cs":native_item,"TownServiceWindowMask.cs":mask_source}
            hashes[name]={filename:hashlib.sha256(value.encode()).hexdigest() for filename,value in sources.items()}
            for filename,value in sources.items(): (production/filename).write_text(value)
            shutil.copyfile(fixture/"Confirmation.csproj",build/"Confirmation.csproj")
            assembly="Confirmation657_"+name.replace("-","_")
            result=subprocess.run([str(Path.home()/".dotnet/dotnet"),"build",str(build/"Confirmation.csproj"),"-c","Debug","--nologo","--verbosity","quiet",
                "-p:CaseName="+assembly,"-p:FixtureDir="+str(fixture),"-p:ProductionDir="+str(production),
                "-p:UnityManaged="+str(args.unity.parent/"Data/Managed"),"-p:GameManaged="+str(managed.resolve()),"-p:HarmonyPath="+str(dependencies[0])],capture_output=True,text=True,env=env)
            (build/"build.log").write_text(result.stdout+result.stderr)
            if result.returncode: raise SystemExit(result.stdout+result.stderr+"\nCompilation failure is not a causal pass")
            binary=build/"bin/Debug/netstandard2.1"/(assembly+".dll")
            executed=run/"executed-assemblies";executed.mkdir(exist_ok=True)
            retained=executed/binary.name;shutil.copyfile(binary,retained)
            cases.append({"name":name,"dll":str(retained),"expected":expected,"sha256":hashlib.sha256(retained.read_bytes()).hexdigest()})
        manifest={"result":str(run/"results.txt"),"cases":cases}
        manifest_path=run/"manifest.json";manifest_path.write_text(json.dumps(manifest,indent=2)+"\n")
        (run/"source-hashes.json").write_text(json.dumps(hashes,indent=2)+"\n")
        (run/"native-provenance.json").write_text(json.dumps(provenance,indent=2)+"\n")
        (run/"fixture-hashes.json").write_text(json.dumps({str(p.relative_to(fixture)):hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.rglob("*") if p.is_file()},indent=2)+"\n")
        print("Evidence: "+str(run),flush=True)
        # Native Unity scripts and Harmony detour caches share type names. Each item
        # variant therefore receives a fresh process with only its own patched DLL.
        # The original enhancement variants retain their unchanged shared-process proof.
        groups = [("enhancement", [entry for entry in cases if not entry["name"].startswith("item-")])]
        groups += [(entry["name"], [entry]) for entry in cases if entry["name"].startswith("item-")]
        reports=[]; failed=False
        for group, entries in groups:
            if not entries: continue
            project=run/("unity-"+group)
            for dirname in ("Assets/Editor","Assets/Plugins","Packages","ProjectSettings"): (project/dirname).mkdir(parents=True)
            for dependency in dependencies: shutil.copyfile(dependency, project/"Assets/Plugins"/dependency.name)
            process_cases=[]
            for entry in entries:
                destination=project/"Assets/Plugins"/Path(entry["dll"]).name
                shutil.copyfile(entry["dll"], destination)
                process_cases.append({**entry,"dll":str(destination)})
            shutil.copyfile(fixture/"Editor/ConfirmationRunner.cs",project/"Assets/Editor/ConfirmationRunner.cs")
            (project/"Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
            (project/"ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
            report=run/(group+".results.txt")
            process_manifest=run/(group+".manifest.json")
            process_manifest.write_text(json.dumps({"result":str(report),"cases":process_cases},indent=2)+"\n")
            process=subprocess.Popen(["xvfb-run","-a",str(args.unity),"-batchmode","-force-glcore","-projectPath",str(project),"-executeMethod","ConfirmationRunner.Start","-confirmationManifest",str(process_manifest),"-logFile",str(run/(group+".unity.log"))],start_new_session=True,env=env)
            reaped=False
            try: result=process.wait(timeout=180)
            finally:
                try: os.killpg(process.pid,signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait();reaped=True
                editor=project_tools.cleanup_project_editors(project,args.unity,launcher_reaped=True)
                compiler=run_tools.cleanup_run_compilers(run,editor_reaped=True)
                (run/(group+".process-cleanup.json")).write_text(json.dumps({'editor':editor,'compiler':compiler},indent=2)+"\n")
                if editor['remaining'] or compiler['remaining'] or compiler['refused']:
                    (run/'.keep').write_text('Private process cleanup requires review.\n')
                    raise RuntimeError('Private native confirmation processes still active')
                receipt=cleanup_completed_run('flat-confirmation-lifecycle',result if 'result' in locals() else 1,completion,root,process_reaped=True)
                (run/(group+".cache-cleanup.json")).write_text(json.dumps(receipt,indent=2)+"\n")
            if report.is_file(): reports.append(report.read_text());print(reports[-1],end="",flush=True)
            failed |= result != 0 or not report.is_file()
        Path(manifest["result"]).write_text("".join(reports))
        if failed: raise SystemExit("FAIL native confirmation lifecycle: "+str(run))
        print(("PARTIAL PASS" if args.case else "PASS")+": native confirmation lifecycle; "+str(run))

        code=0
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid,signal.SIGKILL);process.wait();reaped=True
        if project is not None:
            editor=project_tools.cleanup_project_editors(project,args.unity,launcher_reaped=reaped)
            if editor['remaining']:
                (run/'.keep').write_text('Private editor cleanup requires review.\n')
                raise RuntimeError('Private editor cleanup incomplete')
        compiler=run_tools.cleanup_run_compilers(run,editor_reaped=reaped)
        (run/'completion-compiler-cleanup.json').write_text(json.dumps(compiler,indent=2)+'\n')
        if compiler['remaining'] or compiler['refused']:
            (run/'.keep').write_text('Private compiler ownership requires review.\n')
            raise RuntimeError('Private compiler cleanup incomplete')
        shutil.rmtree(compiler_temp)
        receipt=cleanup_completed_run('flat-confirmation-lifecycle',code,completion,root,process_reaped=reaped)
        (run/'cache-cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')


if __name__ == "__main__": main()
