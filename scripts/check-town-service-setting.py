#!/usr/bin/env python3
"""Run production floor and station lifecycle methods, including author handover regressions."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "Layout.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceLayout.cs",
    "Placement.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServicePlacement.cs",
    "Station.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceStation.cs",
    "Grounding.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceGrounding.cs",
}


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Production binding drift: expected one occurrence of {before!r}, got {source.count(before)}")
    return source.replace(before, after, 1)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/town-service-setting")
    parser.add_argument("--case", action="append", help="Resume only named controls plus production.")
    args = parser.parse_args()
    sources = {name: (args.source_root / source).read_text() for name, source in FILES.items()}
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    env = dict(os.environ, DOTNET_ROOT=str(Path(dotnet).resolve().parent),
               DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1", MSBUILDDISABLENODEREUSE="1")
    anchor = '        InteractionAnchor = root.transform.Find("InteractionAnchor")\n            ?? throw new InvalidOperationException("Town station has no InteractionAnchor");\n'
    variants = [
        ("baseline", {}),
        ("legacy NPC still changes detail", {"Station.cs": sources["Station.cs"].replace('group.enabled = false;', 'group.enabled = true;')}),
        ("legacy NPC lower surface overlaps skin", {"Station.cs": sources["Station.cs"].replace('renderer.enabled = Array.IndexOf(levels[0].renderers, renderer) >= 0;', 'renderer.enabled = true;')}),
        ("facial failure blocks native station", {"Station.cs": sources["Station.cs"].replace('catch (Exception error) { FaceFailure(error); return remote; }', 'catch (Exception error) { throw new InvalidOperationException("facial failure escaped", error); }')}),
        ("unchanged followers dirty geometry every frame", {"Grounding.cs": sources["Grounding.cs"].replace('if (_applied && actorOffset == _actorOffset && furnitureBottom == _furnitureBottom) return;', 'if (_applied && actorOffset == _actorOffset && furnitureBottom == _furnitureBottom && false) return;')}),
        ("handover leaves old ground geometry", {"Station.cs": sources["Station.cs"].replace('SetGrounding(actorFloor, furnitureBottom);', '// SetGrounding intentionally omitted by negative control')}),
        ("actor correction overwrites authored soles", {"Grounding.cs": sources["Grounding.cs"].replace('_actorRest + Vector3.up * actorOffset', 'Vector3.up * actorOffset')}),
        ("grounding samples only station root", {"Grounding.cs": sources["Grounding.cs"].replace('Height(floor, new Vector3(-.12f, 0f, .65f))', 'Height(floor, Vector3.zero)').replace('Height(floor, new Vector3(.12f, 0f, .65f))', 'Height(floor, Vector3.zero)')}),
        ("furniture top moves with its bottom", {"Grounding.cs": sources["Grounding.cs"].replace('Part.position += anchor - Part.TransformPoint(TopPoint);', '// Deliberately skip top-anchor restoration' )}),
        ("furniture does not reach lowest foot", {"Grounding.cs": sources["Grounding.cs"].replace('furnitureBottom = float.PositiveInfinity;', 'furnitureBottom = float.NegativeInfinity;').replace('furnitureBottom = Mathf.Min(furnitureBottom, Height(floor, point))', 'furnitureBottom = Mathf.Max(furnitureBottom, Height(floor, point))')}),
        ("tracking floor regression", {"Placement.cs": replace_once(sources["Placement.cs"],
            'position.y = CanonicalFloor(seat.FloorPosition.y);', 'position.y = seat.FloorPosition.y;')}),
        ("unsafe outer station ring", {"Layout.cs": sources["Layout.cs"].replace("radius = ResidentRadius;", "radius = 9f;")}),
        ("ignored terrain relief", {"Placement.cs": replace_once(sources["Placement.cs"],
            'return float.IsNegativeInfinity(best) ? room.position.y : ground.TransformPoint(new Vector3(p.x, best, p.z)).y;', 'return room.position.y;')}),
        ("handover does not replace peer floor", {"Station.cs": sources["Station.cs"].replace('(authorPose && !_authorPose)', '(authorPose && !_authorPose && false)')}),
        ("remote scale misses light update", {"Station.cs": replace_once(sources["Station.cs"],
            'changed || environmentChanged || lightScale != _lightScale', 'changed || environmentChanged || false && lightScale != _lightScale')}),
        ("late card property block corruption", {"Station.cs": sources["Station.cs"].replace('foreach (Renderer renderer in _renderers)', 'foreach (Renderer renderer in _root.GetComponentsInChildren<Renderer>(true))')}),
        ("constructor resource acquisition before anchor validation", {"Station.cs": sources["Station.cs"].replace(anchor, '').replace(
            '            _skinning.Dispose(); _lighting.Dispose(); _grounding.Dispose(); throw;\n        }',
            '            _skinning.Dispose(); _lighting.Dispose(); _grounding.Dispose(); throw;\n        }\n' + anchor)}),
        ("local scenery replaces shared native floor", {"Placement.cs": replace_once(sources["Placement.cs"],
            'position.y = CanonicalFloor(seat.FloorPosition.y);',
            'position.y = SkyAlternative.PlacedRoomRoot != null ? SkyAlternative.PlacedRoomRoot.position.y : seat.FloorPosition.y;')}),
        ("author scenery change republishes station pose", {"Station.cs": replace_once(sources["Station.cs"],
            'bool changed = !_placed || (authorPose && !_authorPose);',
            'bool changed = !_placed || environmentChanged || (authorPose && !_authorPose);')}),
        ("native parchment yaw does not refresh placement", {"Station.cs": replace_once(sources["Station.cs"],
            ' || layoutYaw != _layoutYaw', '')}),
    ]
    expected = {
        "legacy NPC still changes detail": "legacy NPC never performs distance LOD switching",
        "legacy NPC lower surface overlaps skin": "legacy NPC retains full skin and shared eyes only",
        "facial failure blocks native station": "facial failure escaped",
        "unchanged followers dirty geometry every frame": "steady follower does not dirty transforms",
        "handover leaves old ground geometry": "new author replaces old sole correction",
        "actor correction overwrites authored soles": "canonical floor delta preserves authored sole correction",
        "grounding samples only station root": "stable soles use the lower canonical plane contact",
        "furniture top moves with its bottom": "support upper edge remains fixed",
        "furniture does not reach lowest foot": "actual support corners determine lowest floor",
        "tracking floor regression": "native furniture plane overrides map seat floor and local terrain",
        "unsafe outer station ring": "default station uses validated original-room layout radius",
        "ignored terrain relief": "read-only local terrain helper preserves actual relief",
        "handover does not replace peer floor": "follower to author resolves common native floor with unchanged room/frame",
        "remote scale misses light update": "remote scale change refreshes light range without local pose",
        "late card property block corruption": "late workspace card receives no station property block",
        "constructor resource acquisition before anchor validation": "invalid anchor does not acquire lighting/decor resources",
        "local scenery replaces shared native floor": "native furniture plane overrides map seat floor and local terrain",
        "author scenery change republishes station pose": "author scenery change cannot publish another station pose",
        "native parchment yaw does not refresh placement": "native parchment yaw invalidates the author placement cache",
    }
    if args.case:
        unknown = set(args.case) - {name for name, _ in variants}
        if unknown: parser.error("Unknown controls: " + ", ".join(sorted(unknown)))
        variants = [variant for variant in variants if variant[0] == "baseline" or variant[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    evidence = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    completion = evidence / "completion.log"
    completion.write_text(f"Evidence: {evidence}\n")
    print(f"Binding production from {args.source_root.resolve()}; evidence: {evidence}", flush=True)
    fixture = ROOT / "scripts/town-service-setting-runtime"
    pins = {"production": {FILES[name]: hashlib.sha256(source.encode()).hexdigest() for name, source in sources.items()},
            "fixture": {str(file.relative_to(ROOT)): hashlib.sha256(file.read_bytes()).hexdigest() for file in sorted(fixture.glob("*")) if file.is_file()},
            "runner": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            "dotnet": dotnet, "dotnet_version": subprocess.check_output([dotnet, "--version"], env=env, text=True).strip()}
    (evidence / "source-hashes.json").write_text(json.dumps(pins, indent=2) + "\n")
    status, results = 1, []
    started = time.monotonic()
    try:
        for index, (label, edits) in enumerate(variants):
            if edits and all(sources[name] == source for name, source in edits.items()):
                raise SystemExit("Production binding changed: " + label)
            folder = evidence / f"case-{index:02d}"
            folder.mkdir()
            project = folder / "Test.csproj"
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>')
            for file in fixture.glob("*.cs"):
                shutil.copyfile(file, folder / file.name)
            for name, source in (sources | edits).items(): (folder / name).write_text(source)
            build = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet", "-p:UseSharedCompilation=false"], env=env, capture_output=True, text=True)
            (folder / "build.log").write_text(build.stdout + build.stderr)
            if build.returncode:
                raise SystemExit("Variant did not compile (not a successful control): " + label + "\n" + build.stdout + build.stderr)
            run = subprocess.run([dotnet, str(folder / "bin/Release/net8.0/Test.dll")], env=env, capture_output=True, text=True)
            output = run.stdout + run.stderr
            (folder / "runtime.log").write_text(output)
            passed = run.returncode == 0 if not edits else run.returncode != 0 and expected[label] in output
            results.append({"case": label, "passed": passed, "exit_code": run.returncode, "expected": expected.get(label), "log": str(folder / "runtime.log")})
            if not passed:
                raise SystemExit("Unexpected runtime result: " + label + "\n" + output)
            if not edits: print(output, end="", flush=True)
            else: print(f"PASS negative control {label}: {expected[label]}", flush=True)
        status = 0
        print(f"Town setting: {len(variants)-1} compiled negative controls passed")
    finally:
        (evidence / "results.json").write_text(json.dumps({"exit_code": status, "duration_seconds": time.monotonic()-started, "cases": results}, indent=2) + "\n")
        (evidence / "cleanup.json").write_text(json.dumps(cleanup_completed_run("town-service-setting", status, completion, ROOT, process_reaped=True), indent=2) + "\n")

if __name__ == "__main__": main()
