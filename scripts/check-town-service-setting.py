#!/usr/bin/env python3
"""Run production floor and station lifecycle methods, including author handover regressions."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "Layout.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceLayout.cs",
    "Placement.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServicePlacement.cs",
    "Station.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceStation.cs",
    "Grounding.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceGrounding.cs",
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=ROOT)
    args = parser.parse_args()
    sources = {name: (args.source_root / source).read_text() for name, source in FILES.items()}
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    env = dict(os.environ, DOTNET_ROOT=str(Path(dotnet).resolve().parent))
    anchor = '        InteractionAnchor = root.transform.Find("InteractionAnchor")\n            ?? throw new InvalidOperationException("Town station has no InteractionAnchor");\n'
    variants = [
        ("baseline", {}),
        ("facial failure blocks native station", {"Station.cs": sources["Station.cs"].replace('catch (Exception error) { FaceFailure(error); return remote; }', 'catch (Exception error) { throw new InvalidOperationException("facial failure escaped", error); }')}),
        ("unchanged followers dirty geometry every frame", {"Grounding.cs": sources["Grounding.cs"].replace('if (_applied && actorOffset == _actorOffset && furnitureBottom == _furnitureBottom) return;', 'if (_applied && actorOffset == _actorOffset && furnitureBottom == _furnitureBottom && false) return;')}),
        ("handover leaves old ground geometry", {"Station.cs": sources["Station.cs"].replace('SetGrounding(actorFloor, furnitureBottom);', '// SetGrounding intentionally omitted by negative control')}),
        ("actor correction overwrites authored soles", {"Grounding.cs": sources["Grounding.cs"].replace('_actorRest + Vector3.up * actorOffset', 'Vector3.up * actorOffset')}),
        ("grounding samples only station root", {"Grounding.cs": sources["Grounding.cs"].replace('Height(room, new Vector3(-.12f, 0f, .65f))', 'Height(room, Vector3.zero)').replace('Height(room, new Vector3(.12f, 0f, .65f))', 'Height(room, Vector3.zero)')}),
        ("furniture top moves with its bottom", {"Grounding.cs": sources["Grounding.cs"].replace('position.y = top - scale.y * .5f;', 'position.y = support.Position.y + bottom;')}),
        ("furniture does not reach lowest foot", {"Grounding.cs": sources["Grounding.cs"].replace('furnitureBottom = float.PositiveInfinity;', 'furnitureBottom = float.NegativeInfinity;').replace('furnitureBottom = Mathf.Min(furnitureBottom, Height(room, point))', 'furnitureBottom = Mathf.Max(furnitureBottom, Height(room, point))')}),
        ("tracking floor regression", {"Placement.cs": sources["Placement.cs"].replace(
            'position.y = room != null ? room.position.y : seat.FloorPosition.y;', 'position.y = seat.FloorPosition.y;').replace(
            'if (room != null) position.y = GroundHeight(room, position);', '')}),
        ("unsafe outer station ring", {"Layout.cs": sources["Layout.cs"].replace("radius = service == 3 ? 2.4f : 2.3f;", "radius = 9f;")}),
        ("ignored terrain relief", {"Placement.cs": sources["Placement.cs"].replace('if (room != null) position.y = GroundHeight(room, position);', '')}),
        ("handover does not replace peer floor", {"Station.cs": sources["Station.cs"].replace('(authorPose && !_authorPose)', '(authorPose && !_authorPose && false)')}),
        ("remote scale misses light update", {"Station.cs": sources["Station.cs"].replace('changed || lightScale != _lightScale', 'changed || false && lightScale != _lightScale')}),
        ("late card property block corruption", {"Station.cs": sources["Station.cs"].replace('foreach (Renderer renderer in _renderers)', 'foreach (Renderer renderer in _root.GetComponentsInChildren<Renderer>(true))')}),
        ("constructor resource acquisition before anchor validation", {"Station.cs": sources["Station.cs"].replace(anchor, '').replace(
            '        catch { _lighting.Dispose(); throw; }', '        catch { _lighting.Dispose(); throw; }\n' + anchor)}),
    ]
    with tempfile.TemporaryDirectory(prefix="ghvr-setting-") as scratch:
        folder = Path(scratch)
        (folder / "Test.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>')
        for file in (ROOT / "scripts/town-service-setting-runtime").glob("*.cs"):
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
        print(f"Town setting: {len(variants)-1} compiled negative controls passed")

if __name__ == "__main__": main()
