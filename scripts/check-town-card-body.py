#!/usr/bin/env python3
"""Verify production physical-card fades and all inert clone registration boundaries in real Unity."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

repo = Path(__file__).resolve().parent.parent
fixture = repo / "scripts/town-card-body-runtime"
out = Path(tempfile.mkdtemp(prefix="town-card-body-", dir=os.environ.get("TMPDIR", "/tmp")))
base = repo / "src/GloomhavenVR"
spec = importlib.util.spec_from_file_location("bindings", repo / "scripts/check-town-service-catalog.py")
bindings = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bindings)
native = (base / "WorldUI/TownServices/NativeTemplates.cs").read_text()
freeze = bindings.method(native, "private static void Freeze(string key, Entry entry)")
scaffold = """using System; using System.Collections.Generic; using GloomhavenVR.Net.TownServices; using UnityEngine; using Object = UnityEngine.Object;
namespace GloomhavenVR.WorldUI { internal static class TownServiceNativeAssets { internal static void PrepareRoot(Transform root) { } }
internal static class NativeTemplates {
 private sealed class Entry { internal Transform Original = null!; internal GameObject Copy = null!; internal List<int> Parts = new(); }
 private static GameObject? _bank;
 private static void Prune(Transform source, Transform clone) { }
 private static void Partition(Transform root, string address, List<int> parts) { }
 internal static Transform TestFreeze(Transform original) { _bank = new GameObject("bank"); _bank.SetActive(false); var entry = new Entry { Original = original }; Freeze("merchant.cardbody", entry); return entry.Copy.transform; }
 internal static void Clean() { if (_bank != null) Object.DestroyImmediate(_bank); }
""" + freeze + "\n}}\n"
source = {name + ".cs": (base / "Net/TownServices" / (name + ".cs")).read_text() for name in
          ("TownServiceAssets", "TownServiceBinding", "TownServiceCodec", "TownServiceDelta", "TownServiceFrame", "TownRackState", "TownCassetteMotion", "TownServiceFlameClock", "TownServiceMirror.Racks", "TownServiceMirror.Offerings", "TownServiceMaterial", "TownServiceMirror", "TownServiceMotion", "TownServiceNeutralize")}
source["TownServiceCardBody.cs"] = (base / "WorldUI/TownServices/TownServiceCardBody.cs").read_text()
source["Freeze.cs"] = scaffold
variants = [("production", None, None, None, ""),
    ("no-body-fade", "TownServiceCardBody.cs", "color.a *= value;", "color.a *= 1f;", "body disappears with zero-opacity original face"),
    ("no-frozen-registration", "Freeze.cs", "TownServiceCardBody.RebindClone(key, entry.Copy);", "", "native frozen body joins silhouette updates"),
    ("no-template-registration", "TownServiceMirror.cs", "PrepareInertGeometry?.Invoke(address, clone);", "", "transport template joins silhouette updates"),
    ("no-observer-registration", "TownServiceMirror.cs", "PrepareInertGeometry?.Invoke(frame.TemplateAddress, clone);", "", "every inert observer body joins silhouette updates")]
manifest = {"result": str(out / "results.txt"), "cases": []}
unity = Path("/home/claw/unity-2021.3.5/Editor/Unity")
managed = repo / "ressources/GH_Data/Managed"
for name, target, before, after, expected in variants:
    run = out / name; production = run / "production"; production.mkdir(parents=True)
    for filename, text in source.items():
        if filename == target: text = bindings.replace_once(text, before, after)
        (production / filename).write_text(text)
    shutil.copyfile(repo / "scripts/town-service-mirror-runtime/Boundaries.cs", production / "ExternalBoundaries.cs")
    project = run / "Body.csproj"; shutil.copyfile(repo / "scripts/town-service-mirror-runtime/Mirror.csproj", project)
    command = [str(Path.home() / ".dotnet/dotnet"), "build", str(project), "-c", "Release", "-v", "quiet", "--nologo",
               "-p:CaseName=Body_" + name.replace("-", "_"), f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
               f"-p:UnityManaged={unity.parent / 'Data/Managed'}", f"-p:UnityUi={managed / 'UnityEngine.UI.dll'}", f"-p:UnityTmp={managed / 'Unity.TextMeshPro.dll'}"]
    done = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (run / "build.log").write_text(done.stdout)
    if done.returncode: raise SystemExit(done.stdout)
    manifest["cases"].append({"name": name, "dll": str(run / "bin/Release/netstandard2.1" / ("Body_" + name.replace("-", "_") + ".dll")), "expected": expected})
    print("Compiled", name, flush=True)
project = out / "unity"; (project / "Assets/Editor").mkdir(parents=True)
(project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
shutil.copyfile(repo / "scripts/town-service-interaction-runtime/Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
(project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}')
(project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
manifest_path = out / "manifest.json"; manifest_path.write_text(json.dumps(manifest))
result = subprocess.run([str(unity), "-batchmode", "-nographics", "-projectPath", str(project), "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(out / "unity.log")], timeout=240, stdout=subprocess.DEVNULL)
print((out / "results.txt").read_text() if (out / "results.txt").exists() else "No results")
print("Evidence:", out)
raise SystemExit(result.returncode)
