#!/usr/bin/env python3
"""Native map confirmation lifecycle with the installed game's unchanged methods.

This bounded suite does not emulate laser arbitration: the independent conversion
transfer suite owns that path. Here the production seat, actual Unity components
and native Show/Hide/confirm/cancel methods establish continuation ordering.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

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
    root = args.source_root.resolve()
    fixture = root / "scripts/flat-confirmation657-runtime"
    managed = root / "ressources/GH_Data/Managed"
    runtime = managed / "GH.Runtime.dll"
    env = os.environ.copy(); env["DOTNET_ROOT"] = str(Path.home() / ".dotnet")
    decompiled = {}
    for name in ("UnityEngine.UI.UIWindow", "UIEnhancementConfirmationBox"):
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
    path = "src/GloomhavenVR/WorldUI/Composites/MapDialogSeat.cs"
    source = (root/path).read_text()
    old = subprocess.run(["git", "show", "bf3444cb5:"+path], cwd=root, check=True, capture_output=True, text=True).stdout
    owned_before = subprocess.run(["git", "show", "526957a40:"+path], cwd=root, check=True, capture_output=True, text=True).stdout
    mask_source = (root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceWindowMask.cs").read_text()
    variants = [("production", source, ""),
        ("owned-mask-production", source, ""),
        ("owned-mask-release", owned_before, "owned tick cannot pull the native root out of its zero-alpha mask"),
        ("published656", old, "destination change does not reidentify an existing temple request"),
        ("without-host-hide", source.replace("host.OnHide += seat.HostHidden;", "// Causal control: omitted original host cancellation edge."), "native prompt cancels before next destination enters"),
        ("slow-host-cancel", source.replace("seat.NativeDialog.Hide(instant: true);", "seat.NativeDialog.Hide();"), "departing host completes native cancellation before another request"),
        ("lost-cancel-fade", source.replace("seat.NativeDialog.StartAlphaTween(0f, 0f, ignoreTimeScale: true);", "{ } // Causal control: lost chosen-outcome tween completion."), "pending cancelled fade completes before singleton reuse"),
        ("lost-confirm-fade", source.replace("seat.NativeDialog.StartAlphaTween(0f, 0f, ignoreTimeScale: true);", "{ } // Causal control: lost chosen-outcome tween completion."), "pending confirmed fade completes before singleton reuse")]
    if any(before == source for name, before, expected in variants if name in ("without-host-hide", "slow-host-cancel")):
        raise RuntimeError("Host cancellation control binding drift")
    if args.case:
        if set(args.case)-{entry[0] for entry in variants}: parser.error("Unknown case")
        variants = [entry for entry in variants if entry[0] in args.case]
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-",dir=args.output_dir.resolve()))
    shutil.copytree(fixture,run/"fixture"); fixture=run/"fixture"
    provenance = {"GH.Runtime.dll": hashlib.sha256(runtime.read_bytes()).hexdigest(),
        **{name:hashlib.sha256(value.encode()).hexdigest() for name,value in decompiled.items()},
        "NativeWindowMethods.cs":hashlib.sha256(native_window.encode()).hexdigest(),
        "NativeBoxMethods.cs":hashlib.sha256(native_box.encode()).hexdigest()}
    hashes={}; cases=[]
    for name, seat, expected in variants:
        build=run/name; production=build/"production"; production.mkdir(parents=True)
        sources={"MapDialogSeat.cs":seat,"NativeWindowMethods.cs":native_window,"NativeBoxMethods.cs":native_box,"TownServiceWindowMask.cs":mask_source}
        hashes[name]={filename:hashlib.sha256(value.encode()).hexdigest() for filename,value in sources.items()}
        for filename,value in sources.items(): (production/filename).write_text(value)
        shutil.copyfile(fixture/"Confirmation.csproj",build/"Confirmation.csproj")
        assembly="Confirmation657_"+name.replace("-","_")
        result=subprocess.run([str(Path.home()/".dotnet/dotnet"),"build",str(build/"Confirmation.csproj"),"-c","Release","--nologo","--verbosity","quiet",
            "-p:CaseName="+assembly,"-p:FixtureDir="+str(fixture),"-p:ProductionDir="+str(production),
            "-p:UnityManaged="+str(args.unity.parent/"Data/Managed"),"-p:GameManaged="+str(managed.resolve())],capture_output=True,text=True,env=env)
        (build/"build.log").write_text(result.stdout+result.stderr)
        if result.returncode: raise SystemExit(result.stdout+result.stderr+"\nCompilation failure is not a causal pass")
        cases.append({"name":name,"dll":str(build/"bin/Release/netstandard2.1"/(assembly+".dll")),"expected":expected})
    project=run/"unity"
    for dirname in ("Assets/Editor","Packages","ProjectSettings"): (project/dirname).mkdir(parents=True)
    shutil.copyfile(fixture/"Editor/ConfirmationRunner.cs",project/"Assets/Editor/ConfirmationRunner.cs")
    (project/"Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project/"ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest={"result":str(run/"results.txt"),"cases":cases}
    manifest_path=run/"manifest.json";manifest_path.write_text(json.dumps(manifest,indent=2)+"\n")
    (run/"source-hashes.json").write_text(json.dumps(hashes,indent=2)+"\n")
    (run/"native-provenance.json").write_text(json.dumps(provenance,indent=2)+"\n")
    (run/"fixture-hashes.json").write_text(json.dumps({str(p.relative_to(fixture)):hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.rglob("*") if p.is_file()},indent=2)+"\n")
    print("Evidence: "+str(run),flush=True)
    try:
        result=subprocess.run(["xvfb-run","-a",str(args.unity),"-batchmode","-force-glcore","-projectPath",str(project),"-executeMethod","ConfirmationRunner.Start","-confirmationManifest",str(manifest_path),"-logFile",str(run/"unity.log")],timeout=180)
    finally:
        shutil.rmtree(project,ignore_errors=True)
    report=Path(manifest["result"])
    if report.is_file(): print(report.read_text(),end="")
    if result.returncode or not report.is_file(): raise SystemExit("FAIL native confirmation lifecycle: "+str(run/"unity.log"))
    print(("PARTIAL PASS" if args.case else "PASS")+": native confirmation lifecycle; "+str(run))


if __name__ == "__main__": main()
