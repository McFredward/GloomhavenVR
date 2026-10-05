#!/usr/bin/env python3
"""Execute WorldUI's actual registered Late list through the original town publisher.

The complete production registration, Late dispatcher, use-bar Late method,
native sampler, town final entrypoint, town send and town Late method are bound
verbatim. Unrelated native samplers and visual writers remain explicit adapters;
town publication, capture, codec, completion and observer playback are real.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def method(text, signature):
    match = re.search(r"^([ \t]*)" + re.escape(signature), text, re.MULTILINE)
    if match is None: raise RuntimeError("Production method binding drift: " + signature)
    end = re.search(r"^" + re.escape(match.group(1)) + r"}\s*$", text[match.end():], re.MULTILINE)
    if end is None: raise RuntimeError("Production method end drift: " + signature)
    return text[match.start():match.end() + end.end()]


def load_binding(root):
    spec = importlib.util.spec_from_file_location("mirror_binding", root / "scripts/check-town-service-mirror.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    bound, hashes = helper.sources(root)
    base = root / "src/GloomhavenVR"
    ui = (base / "WorldUI/WorldUIModule.cs").read_text()
    registration = method(ui, "private void BuildTickSteps()")
    dispatcher = method(ui, "private void LateUpdate()")
    fields = sorted(set(re.findall(r"(_\w+)\.(?:Tick|LateTick)", registration)))
    scaffold = "using System; using System.Collections.Generic; namespace GloomhavenVR.WorldUI; internal sealed class WorldUIModule {\n"
    scaffold += "private (string name,Action fn)[] _updateSteps=Array.Empty<(string,Action)>(),_lateSteps=Array.Empty<(string,Action)>();\n"
    slots = helper.expression(ui, "private readonly WorldSurface[] _slotSurfaces")
    scaffold += slots + "\n"
    scaffold += "\n".join("private readonly " + ("UseBarsSurface" if field == "_useBars" else "ScheduledWidget") + " " + field + "=new();" for field in fields)
    scaffold += "\n" + registration + "\n" + dispatcher
    scaffold += "\ninternal void FixtureBuild()=>BuildTickSteps(); internal void FixtureLate()=>LateUpdate();"
    scaffold += "\ninternal string[] FixtureUpdate=>Array.ConvertAll(_updateSteps,x=>x.name); internal string[] FixtureLateNames=>Array.ConvertAll(_lateSteps,x=>x.name);\n}\n"
    scaffold += "\n".join("internal sealed class " + name + " : WorldSurface { }" for name in re.findall(r"new (\w+)\(\)", slots))
    bound["ScheduledWorldUI.cs"] = scaffold
    avatar = (base / "Net/Avatar/NetAvatarDriver.cs").read_text()
    native = (base / "Net/Avatar/NetAvatarDriver.NativePresentation.cs").read_text()
    town = (base / "Net/Avatar/NetAvatarDriver.TownServices.cs").read_text()
    members = native[native.index("    private readonly byte[] _boardBuffer"):native.index("    internal static void PublishTownServicesFinal()")]
    bound["ScheduledAvatar.cs"] = "using System; using System.Collections.Generic; using UnityEngine; using GloomhavenVR.Core; using GloomhavenVR.WorldUI; using GloomhavenVR.Net.TownServices; namespace GloomhavenVR.Net; internal sealed partial class NetAvatarDriver {\n" + members + "\n" + "\n".join(helper.expression(town, declaration) for declaration in ("private float _nextFaceSend", "private Func<byte[], int, bool, bool>? _merchantControlSender")) + "\n" + helper.expression(town, "private bool SendMerchantControl") + "\n" + "\n".join((
        method(avatar, "internal static void PublishUseBarAnimations("),
        method(native, "internal static void PublishTownServicesFinal()"),
        method(native, "private void TickNativePresentationSend("),
        method(town, "private void SendTownServices()"),
        method(native, "private void TickNativeBoardSend(float now)"),
        method(native, "private void SendNativeBoard(NativeBoardState state)"))) + "\n}\n"
    protocol = (base / "Net/NetProtocol.cs").read_text()
    bound["UseBarProtocolBit.cs"] = "namespace GloomhavenVR.Net; internal static partial class NetProtocol {\n" + helper.expression(protocol, "public const byte UseBarActiveBonusBit") + "\n}\n"
    usebars = (base / "WorldUI/Surfaces/UseBarsSurface.cs").read_text()
    bound["ScheduledUseBars.cs"] = "using System; using UnityEngine; namespace GloomhavenVR.WorldUI; internal sealed partial class UseBarsSurface {\n" + method(usebars, "internal void LateTick()") + "\n}\n"
    presentation = (base / "WorldUI/TownServices/TownServicePresentation.cs").read_text()
    bound["ScheduledTown.cs"] = "using UnityEngine; namespace GloomhavenVR.WorldUI; internal static partial class TownServicePresentation {\n" + method(presentation, "internal static void LateTick()") + "\n}\n"
    publisher = (base / "WorldUI/TownServices/TownServiceSync.cs").read_text()
    bound["FinalDynamicPool.cs"] = "using UnityEngine; namespace GloomhavenVR.WorldUI; internal sealed partial class TownServiceSync {\n" + helper.method(publisher, "private void CollectDynamic(Transform root)") + "\n}\n"
    # All unrelated registered static steps are no-ops. The four visual seams
    # below are declared by the dedicated fixture and write real Unity objects.
    static = re.findall(r'\("[^"\n]+",\s*([A-Z]\w*(?:\.\w+)+)\)', registration)
    methods = {}
    for target in static:
        if target.startswith("Net."): continue
        kind, name = target.rsplit(".", 1)
        methods.setdefault(kind, set()).add(name)
    custom = {"TownServicePresentation.LateTick", "GrabBarTween.TickAll", "CanvasConversion.TickPanelOrder"}
    stubs = []
    for kind, names in sorted(methods.items()):
        namespace, short = ("GloomhavenVR.WorldUI." + kind.rsplit(".", 1)[0], kind.rsplit(".", 1)[1]) if "." in kind else ("GloomhavenVR.WorldUI", kind)
        declaration = "sealed" if kind == "TownServiceSync" else "static"
        content = " ".join(f"internal static void {name}() {{ }}" for name in sorted(names) if kind + "." + name not in custom)
        stubs.append(f"namespace {namespace} {{ internal {declaration} partial class {short} {{ {content} }} }}")
    bound["UnrelatedRegisteredSteps.cs"] = "\n".join(stubs) + "\n"
    full = {"WorldUI/WorldUIModule.cs": ui, "Net/Avatar/NetAvatarDriver.cs": avatar,
        "Net/Avatar/NetAvatarDriver.NativePresentation.cs": native, "Net/Avatar/NetAvatarDriver.TownServices.cs": town,
        "WorldUI/Surfaces/UseBarsSurface.cs": usebars, "WorldUI/TownServices/TownServicePresentation.cs": presentation}
    hashes.update({"full/" + name: hashlib.sha256(value.encode()).hexdigest() for name, value in full.items()})
    hashes.update({name: hashlib.sha256(value.encode()).hexdigest() for name, value in bound.items()})
    return bound, hashes


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo)
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-final-capture")
    parser.add_argument("--no-negative-controls", action="store_true", help="Production-only continuation; reuse unchanged controls explicitly, not a complete gate")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    args = parser.parse_args()
    root = args.source_root.resolve(); args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = run / "fixture"; shutil.copytree(repo / "scripts/town-service-mirror-runtime", fixture)
    for path in (fixture / "Boundaries.cs", fixture / "Publisher.cs"):
        content = path.read_text()
        for kind in ("NetProtocol", "PanelMipBake", "TownServicePopulation", "TownServiceGrantSync", "TownServiceAssets", "TownServicePresentation", "TownServiceMerchantHandoff", "TownServicePalmConfirmation"):
            content = content.replace("static class " + kind, "static partial class " + kind)
        for kind in ("TownServiceEnhancementHandoff", "TownServiceSurface", "TownServiceCatalog", "TownServiceTray"):
            content = content.replace("sealed class " + kind, "sealed partial class " + kind)
        content = content.replace("#if !PUBLISHER622", "#if !PUBLISHER622 && !TOWN_FINAL_CAPTURE")
        content = content.replace("#if PUBLISHER622", "#if PUBLISHER622 || TOWN_FINAL_CAPTURE")
        path.write_text(content)
    bound, hashes = load_binding(root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(root), "sha256": hashes}, indent=2) + "\n")
    (run / "fixture-hashes.json").write_text(json.dumps({str(path.relative_to(fixture)): hashlib.sha256(path.read_bytes()).hexdigest() for path in fixture.rglob("*") if path.is_file()}, indent=2) + "\n")
    final = 'late.Add(("TownServices.PublishFinal", Net.NetAvatarDriver.PublishTownServicesFinal));'
    order = 'late.Add(("CanvasConversion.Order", CanvasConversion.TickPanelOrder));'
    tween = 'late.Add(("GrabBarTween.Late", GrabBarTween.TickAll));'
    variants = [("production", None, None, None, ""),
        ("old-usebar-capture", "ScheduledAvatar.cs", "float now = Time.unscaledTime;", "float now = Time.unscaledTime; SendTownServices();", "capture includes the current offered card plane"),
        ("duplicate-usebar-capture", "ScheduledAvatar.cs", "float now = Time.unscaledTime;", "float now = Time.unscaledTime; SendTownServices();", "registered Late frame captures town exactly once"),
        ("before-panel-order", "ScheduledWorldUI.cs", order + "\n            // Read-only capture follows every visual writer, including native town handoff/Sync\n            // and panel sorting. Capturing in UseBarsSurface.Late sampled their previous frame.\n            " + final, final + "\n            " + order, "capture includes the final canvas sorting state"),
        ("before-grab-tween", "ScheduledWorldUI.cs", tween, final + "\n            " + tween, "capture includes the final drawn grab-bar geometry"),
        ("no-final-publisher", "ScheduledWorldUI.cs", final, "// missing final publication", "registered Late frame captures town exactly once")]
    if args.no_negative_controls:
        variants = variants[:1]
    manifest = {"result": str(run / "results.txt"), "evidence": str(run), "suite": "town-final-capture", "cases": []}
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    print(f"Production binding: {root}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name; production = build / "production"; production.mkdir(parents=True)
        for path, content in bound.items():
            if path == "ScheduledWorldUI.cs" and name in ("old-usebar-capture", "before-grab-tween"):
                if content.count(final) != 1: raise RuntimeError("Final publisher removal binding drift")
                content = content.replace(final, "// final publisher moved by causal control", 1)
            if path == filename:
                if content.count(before) != 1: raise RuntimeError("Production mutation binding drift: " + name)
                content = content.replace(before, after, 1)
            (production / path).write_text(content)
        project = build / "Mirror.csproj"; shutil.copyfile(fixture / "Mirror.csproj", project)
        assembly = "TownFinalCapture_" + name.replace("-", "_")
        result = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}", "-p:DefineConstants=TOWN_FINAL_CAPTURE",
            f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}", f"-p:UnityUi={root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'}",
            f"-p:UnityTmp={root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll'}"], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(result.stdout)
        if result.returncode: print(result.stdout); raise SystemExit("FAIL compilation: " + name)
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
        print("Compiled " + name, flush=True)
    project = run / "unity"; (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    runner = (fixture / "Editor/MirrorRunner.cs").read_text().replace('GetMethod("Run")', 'GetMethod("RunFinalCapture")')
    (project / "Assets/Editor/MirrorRunner.cs").write_text(runner)
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"; manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    result = subprocess.run(["xvfb-run", "-a", str(args.unity), "-batchmode", "-force-glcore", "-projectPath", str(project),
        "-executeMethod", "MirrorRunner.Start", "-mirrorManifest", str(manifest_path), "-logFile", str(run / "unity.log")], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    evidence = Path(manifest["result"])
    if evidence.exists(): print(evidence.read_text(), end="")
    for directory in ("Library", "Temp", "Logs"): shutil.rmtree(project / directory, ignore_errors=True)
    for case in manifest["cases"]:
        owned = Path(case["dll"])
        for output in owned.parent.glob("*"):
            if output.suffix in (".dll", ".pdb", ".xml") and output.stem != owned.stem: output.unlink()
        shutil.rmtree(run / case["name"] / "obj", ignore_errors=True)
    if result.returncode or not evidence.exists(): raise SystemExit(f"FAIL Unity exit {result.returncode}; see {run / 'unity.log'}")
    print(f"PASS actual scheduled town final-capture suite; evidence: {run}")


if __name__ == "__main__": main()
