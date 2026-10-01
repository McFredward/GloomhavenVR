#!/usr/bin/env python3
"""Execute the real town light owner and LightStabiliser adoption/exclusion methods."""
import argparse
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
    end = brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=ROOT)
    args = parser.parse_args()
    owner = (args.source_root / "src/GloomhavenVR/WorldUI/TownServices/TownServiceLighting.cs").read_text()
    activity = (args.source_root / "src/GloomhavenVR/Net/TownActivityState.cs").read_text().split("/// <summary>Additive81", 1)[0]
    stabilizer = (args.source_root / "src/GloomhavenVR/Rig/LightStabiliser.cs").read_text()
    methods = [method(stabilizer, signature) for signature in (
        "private static int AdoptLights(Light[] found)",
        "private static string ClassifyExclusion(List<string> ownScripts, Transform t)",
        "private static int IndexOfLight(Light l)")]
    adoption = "using System; using System.Collections.Generic; using UnityEngine; namespace GloomhavenVR.Rig; internal static partial class LightStabiliser {\n" + "\n".join(methods) + "\n}"
    sources = {"Owner.cs": owner, "Adoption.cs": adoption,
               "Activity.cs": "#pragma warning disable CS0649\n" + activity}
    variants = [
        ("baseline", {}),
        ("stabilizer adopts town lights", {"Adoption.cs": adoption.replace("WorldUI.TownServiceLighting.Owns(l) || ", "")}),
        ("broad layer exclusion", {"Adoption.cs": adoption.replace("WorldUI.TownServiceLighting.Owns(l)", "l.gameObject.layer == 27")}),
        ("practical never registered", {"Owner.cs": owner.replace("ClaimPractical(_stand);", "")}),
        ("destroyed practical leaks registry", {"Owner.cs": owner.replace("ForgetPractical(_stand);", "if (_stand != null) ForgetPractical(_stand);")}),
        ("ownership released after deferred destroy", {"Owner.cs": owner.replace("ForgetPractical(_stand);", "UnityEngine.Object.Destroy(_stand.gameObject); ForgetPractical(_stand);")}),
        ("observer samples its own moon", {"Owner.cs": owner.replace("authorEnvironment && SkyAlternative.TryRoomMoonDirection", "SkyAlternative.TryRoomMoonDirection")}),
        ("observer fails to apply author colour", {"Owner.cs": owner.replace("_roomLight.color = new Color(colour.x, colour.y, colour.z);", "_roomLight.color = new Color(1f, 1f, 1f);")}),
        ("fill ignores shared map rotation", {"Owner.cs": owner.replace("frame.TransformDirection(state.EnvironmentLightDirection)", "state.EnvironmentLightDirection")}),
        ("ambient sampling drops an L2 coefficient", {"Owner.cs": owner.replace("ambient[2, n]", "0f")}),
        ("surface drops shared ambient binding", {"Owner.cs": owner.replace("_surfaceProperties.SetVector(AmbientIds[0], new Vector4(p.C3.x, p.C1.x, p.C2.x, p.C0.x - p.C6.x));", "_surfaceProperties.SetVector(AmbientIds[0], new Vector4(0f, 0f, 0f, 0f));")}),
        ("eligible native sun removed", {"Owner.cs": owner.replace("Light? key = EligibleKey(RenderSettings.sun) ? RenderSettings.sun", "Light? key = false ? RenderSettings.sun")}),
        ("same count hides replacement surface", {"Owner.cs": owner.replace("sameSurfaces &= ReferenceEquals(_boundSurfaces[n], _surfaces[n]);", "sameSurfaces &= true;")}),
    ]
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    env = dict(os.environ, DOTNET_ROOT=str(Path(dotnet).resolve().parent))
    with tempfile.TemporaryDirectory(prefix="ghvr-townlight-") as scratch:
        folder = Path(scratch)
        (folder / "Test.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>')
        for file in (ROOT / "scripts/town-service-lighting-runtime").glob("*.cs"):
            shutil.copyfile(file, folder / file.name)
        for label, edits in variants:
            if edits and all(sources[name] == source for name, source in edits.items()):
                raise SystemExit("Production binding changed: " + label)
            for name, source in (sources | edits).items(): (folder / name).write_text(source)
            run = subprocess.run([dotnet, "run", "--project", str(folder / "Test.csproj"), "-c", "Release"], env=env, capture_output=True, text=True)
            if not edits:
                print(run.stdout, end="")
                if run.returncode: raise SystemExit(run.stdout + run.stderr)
            elif run.returncode == 0 or "error CS" in run.stdout:
                raise SystemExit("Negative control did not fail at runtime: " + label + "\n" + run.stdout + run.stderr)
        print(f"Town lighting: {len(variants)-1} compiled negative controls passed")

if __name__ == "__main__": main()
