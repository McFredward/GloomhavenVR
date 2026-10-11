#!/usr/bin/env python3
"""Verify production physical-card fades and all inert clone registration boundaries in real Unity."""
import importlib.util
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

repo = Path(__file__).resolve().parent.parent
out = Path(tempfile.mkdtemp(prefix="town-card-body-", dir=os.environ.get("TMPDIR", "/tmp")))
base = repo / "src/GloomhavenVR"
spec = importlib.util.spec_from_file_location("bindings", repo / "scripts/check-town-service-mirror.py")
bindings = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bindings)
source, _ = bindings.sources(repo)
# Use the maintained complete production mirror binder. The legacy fixture's
# silhouette assertions now observe the actual CardMesh consumer registry;
# its external final mesh assignment remains explicit, not an ability factory.
fixture = out / "fixture"
shutil.copytree(repo / "scripts/town-service-mirror-runtime", fixture)
shutil.copyfile(repo / "scripts/town-card-body-runtime/Program.cs", fixture / "MerchantBodyProgram.cs")
shutil.copyfile(repo / "scripts/town-card-body-runtime/Boundaries.cs", fixture / "MerchantBodyBoundaries.cs")
lazy = fixture / "LazyTemplate.cs"
text = lazy.read_text()
noop = "    internal static class TownServiceCardBody\n    { internal static void RebindClone(string key,GameObject clone) { } }\n"
if text.count(noop) != 1: raise RuntimeError("Merchant body compile boundary drift")
lazy.write_text(text.replace(noop, "", 1))
raw_mesh = source["CardMesh.cs"]
if raw_mesh.count("internal static class CardMesh\n") != 1: raise RuntimeError("Actual CardMesh shape drift")
source["CardMesh.cs"] = raw_mesh.replace("internal static class CardMesh\n", "internal static partial class CardMesh\n", 1)
source["TownServiceCardBody.cs"] = (base / "WorldUI/TownServices/TownServiceCardBody.cs").read_text()
native = (base / "WorldUI/TownServices/NativeTemplates.cs").read_text()
freeze = bindings.method(native, "private static void Freeze(string key, Entry entry)")
frozen_registration = next(line.strip() for line in freeze.splitlines()
    if line.strip().startswith("TownServiceCardBody.RebindClone("))
source["FreezeProbe.cs"] = """using System; using UnityEngine;
namespace GloomhavenVR.WorldUI {
internal static partial class LazyTemplateProbe {
 internal static Transform FixtureFreezeMerchant(Transform original, GameObject bank) {
  Open(bank); var entry = new Entry { Original = original };
  Freeze("merchant.cardbody", entry); return entry.Copy.transform;
 }
}
internal static partial class NativeTemplates {
 private static GameObject? _merchantFixtureBank;
 internal static Transform TestFreeze(Transform original) {
  _merchantFixtureBank = new GameObject("merchant fixture inactive bank"); _merchantFixtureBank.SetActive(false);
  return LazyTemplateProbe.FixtureFreezeMerchant(original, _merchantFixtureBank);
 }
 internal static void Clean() {
  LazyTemplateProbe.Close(); if (_merchantFixtureBank != null) UnityEngine.Object.DestroyImmediate(_merchantFixtureBank);
 }
}}
"""
(out / "source-hashes.json").write_text(json.dumps({
    "production": {name: hashlib.sha256(value.encode()).hexdigest() for name, value in source.items()},
    "actual_unmodified_cardmesh": hashlib.sha256(raw_mesh.encode()).hexdigest(),
    "fixture": {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in fixture.glob("*.cs")},
}, indent=2) + "\n")
variants = [("production", None, None, None, ""),
    ("no-body-fade", "TownServiceCardBody.cs", "color.a *= value;", "color.a *= 1f;", "body disappears with zero-opacity original face"),
    ("no-frozen-registration", "LazyNativeTemplates.cs", frozen_registration, "", "native frozen body joins silhouette updates"),
    ("no-template-registration", "TownServiceMirror.cs", "PrepareInertGeometry?.Invoke(address, clone);", "", "transport template joins silhouette updates"),
    ("no-observer-registration", "TownServiceMirror.cs", "PrepareInertGeometry?.Invoke(frame.TemplateAddress, clone);", "", "every inert observer body joins silhouette updates")]
manifest = {"result": str(out / "results.txt"), "cases": []}
unity = Path("/home/claw/unity-2021.3.5/Editor/Unity")
managed = repo / "ressources/GH_Data/Managed"
for name, target, before, after, expected in variants:
    run = out / name; production = run / "production"; production.mkdir(parents=True)
    for filename, text in source.items():
        if filename == target:
            if text.count(before) != 1: raise RuntimeError("Exact merchant body control drift: " + name)
            text = text.replace(before, after, 1)
        (production / filename).write_text(text)
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
