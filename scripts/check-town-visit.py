#!/usr/bin/env python3
"""Exercise actual resident input and bound early pointer arbitration in isolated real Unity.

Only native permissions/callbacks and VR device state are boundary doubles. Collider shape,
raycasts, object lifetime, transforms and target/pointer policies execute production code.
The RayInteractor contribution and uGUI rejection block are extracted verbatim, rather
than testing a hand-maintained equivalent of the early-dispatch arbitration.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def replace_once(text, before, after):
    if text.count(before) != 1:
        raise RuntimeError(f"Expected exactly one source binding: {before}")
    return text.replace(before, after, 1)


def main():
    repo = Path(__file__).resolve().parent.parent
    fixture = repo / "scripts/town-visit-runtime"
    base = repo / "src/GloomhavenVR"
    out = Path(tempfile.mkdtemp(prefix="town-visit-", dir=os.environ.get("TMPDIR", "/tmp")))
    paths = {
        "TownServiceVisitTarget.cs": base / "WorldUI/TownServices/TownServiceVisitTarget.cs",
        "LaserPointerPolicy.cs": base / "Hands/Interact/LaserPointerPolicy.cs",
        "RayInteractor.cs": base / "Hands/Interact/RayInteractor.cs",
        "RayUguiDriver.cs": base / "Hands/Interact/RayUguiDriver.cs",
    }
    originals = {name: path.read_text() for name, path in paths.items()}
    (out / "source-hashes.json").write_text(json.dumps({str(paths[name].relative_to(repo)): hashlib.sha256(text.encode()).hexdigest() for name, text in originals.items()}, indent=2))
    ray = re.search(r"        float resident = WorldUI\.TownServiceVisitTarget\.OccludingDistance\(origin, direction, maxDistance\);\n.*?\n        SolidOccluderIsBoard = .*?;", originals["RayInteractor.cs"], re.S)
    ui = re.search(r"        Canvas\? solidOccluded = null;\n.*?\n        }", originals["RayUguiDriver.cs"], re.S)
    epsilon = re.search(r"private const float OcclusionEpsilonMeters = .*?;", originals["RayUguiDriver.cs"])
    if ray is None or ui is None or epsilon is None:
        raise RuntimeError("Early ray/uGUI integration changed: review the explicit source bindings")
    source = {name: originals[name] for name in ("TownServiceVisitTarget.cs", "LaserPointerPolicy.cs")}
    source["EarlyPointer.cs"] = """using UnityEngine;
namespace GloomhavenVR.Hands.Interact {
internal sealed partial class RayInteractor {
 internal void ComputeResidentOcclusion(Vector3 origin, Vector3 direction, float maxDistance, float liveBoard) {
""" + ray.group() + """
 }
}
internal static class BoundUiArbitration {
 """ + epsilon.group() + """
 internal static Canvas? Pick(VRHand _hand, Canvas? best, float bestDist) {
 float scale = _hand.WorldScale;
""" + ui.group() + """
 return best;
 }
}
}
"""
    variants = [
        ("production", None, None, None, ""),
        ("merchant-button-restored", "TownServiceVisitTarget.cs", "&& _mode != EGuildmasterMode.Merchant", "", "merchant never opens a native destination or plays button feedback"),
        ("no-native-commit-gate", "TownServiceVisitTarget.cs", "&& !StoryComposite.PointOfNoReturn", "", "commit resident cannot invoke native destination"),
        ("no-touch-ray-cooldown", "TownServiceVisitTarget.cs", "Time.unscaledTime - _pressedAt < ButtonTuning.PokePressCooldownSeconds", "false", "touch and ray open guarded destination only once within cooldown"),
        ("disabled-resident-opens", "TownServiceVisitTarget.cs", "private bool Available => _visible && WorldUIConfig.ImmersiveTownServices.Value", "private bool Available => _visible", "disabled resident cannot invoke native destination"),
        ("no-early-resident-occlusion", "EarlyPointer.cs", "SolidOccluderDistance = Mathf.Min(Mathf.Min(FanOccluderDistance, liveBoard), resident);", "SolidOccluderDistance = Mathf.Min(FanOccluderDistance, liveBoard);", "resident participates in early ray arbitration"),
    ]
    manifest = {"result": str(out / "results.txt"), "cases": []}
    unity = Path(os.environ.get("UNITY_EDITOR", "/home/claw/unity-2021.3.5/Editor/Unity"))
    dotnet = os.environ.get("DOTNET", str(Path.home() / ".dotnet/dotnet"))
    for name, target, before, after, expected in variants:
        run = out / name
        production = run / "production"
        production.mkdir(parents=True)
        for filename, text in source.items():
            if filename == target:
                text = replace_once(text, before, after)
            (production / filename).write_text(text)
        project = run / "Visit.csproj"
        shutil.copyfile(repo / "scripts/town-service-interaction-runtime/Interaction.csproj", project)
        assembly = "Visit_" + name.replace("-", "_")
        command = [dotnet, "build", str(project), "-c", "Release", "-v", "quiet", "--nologo", "-p:CaseName=" + assembly,
                   f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
                   f"-p:UnityManaged={unity.parent / 'Data/Managed'}", f"-p:UnityUi={repo / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'}"]
        done = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (run / "build.log").write_text(done.stdout)
        if done.returncode:
            raise RuntimeError(done.stdout)
        manifest["cases"].append({"name": name, "dll": str(run / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
        print("Compiled", name, flush=True)
    project = out / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    shutil.copyfile(repo / "scripts/town-service-interaction-runtime/Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = out / "manifest.json"
    manifest_path.write_text(json.dumps(manifest))
    print("Evidence:", out, flush=True)
    result = subprocess.run([str(unity), "-batchmode", "-nographics", "-projectPath", str(project), "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(out / "unity.log")], timeout=240, stdout=subprocess.DEVNULL)
    print((out / "results.txt").read_text() if (out / "results.txt").exists() else "No results")
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
