#!/usr/bin/env python3
"""Measure the production multiline note with real Unity layout and TMP glyphs.

Only StampRow/the external caption style and authored row skeleton are explicit
boundaries. BuildNote executes verbatim, original EN/DE strings are source-bound,
and original game extended-layout classes execute in the compatible Unity editor.
This is a native geometry/render receipt, not a game-template/headset image claim.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    assert source.count(signature) == 1, "Production binding drift: " + signature
    start = source.index(signature)
    end = source.index("{", start) + 1
    depth = 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/options-notes-runtime")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--case", action="append", help="Partial development run; repeat for selected causal controls")
    args = parser.parse_args()
    row_path = args.source_root / "src/GloomhavenVR/WorldUI/Options/VROptionsTab.2.Rows.cs"
    loc_path = args.source_root / "src/GloomhavenVR/Core/Loc/Loc.cs"
    rows, loc = row_path.read_text(), loc_path.read_text()
    note = method(rows, "private static void BuildNote(")
    literal = r'("(?:[^"\\]|\\.)*")'
    match = re.search(r'\["graphics_profile_note"\]\s*=\s*Pair\(\s*' + literal + r'\s*,\s*' + literal + r'\s*\)', loc)
    assert match, "Original EN/DE note binding drift"
    english, german = (json.loads(value) for value in match.groups())
    original = "internal static class OriginalNotes { internal const string English=" + json.dumps(english) + "; internal const string German=" + json.dumps(german) + "; }\n"
    variants = [("production", note, "")]
    changes = [
        ("multiline-disabled", "if (multiline)", 'if (bool.Parse("false"))', "multiline note occupies the full available native row width"),
        ("narrow-title-retained", "rect.anchorMax = Vector2.one;", "rect.anchorMax = new Vector2(.55f, 1f);", "multiline note occupies the full available native row width"),
        ("fixed-height-retained", "layout.minHeight = layout.preferredHeight = height;", "layout.minHeight = layout.preferredHeight = 44f;", "multiline note grows native extended layout instead of retaining a fixed height"),
    ]
    for name, before, after, expected in changes:
        assert note.count(before) == 1, "Negative binding drift: " + name
        variants.append((name, note.replace(before, after), expected))
    if args.case:
        unknown = set(args.case) - {entry[0] for entry in variants}
        if unknown:
            parser.error("Unknown cases: " + str(sorted(unknown)))
        variants = [entry for entry in variants if entry[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = ROOT / "scripts/options-notes-runtime"
    managed = args.source_root / "ressources/GH_Data/Managed"
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    manifest = {"result": str(run / "results.txt"), "evidence": str(run), "cases": []}
    for name, body, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        (production / "BuildNote.cs").write_text("using TMPro; using UnityEngine; using UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI { internal static partial class VROptionsTab {\n" + body + "\n}}\n")
        (production / "OriginalNotes.cs").write_text(original)
        project = build / "Notes.csproj"
        shutil.copyfile(fixture / "Notes.csproj", project)
        assembly = "OptionsNotes_" + name.replace("-", "_")
        built = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet", "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production), "-p:UnityManaged=" + str(args.unity.parent / "Data/Managed"), "-p:GameManaged=" + str(managed.resolve())], capture_output=True, text=True)
        (build / "build.log").write_text(built.stdout + built.stderr)
        if built.returncode:
            raise SystemExit(built.stdout + built.stderr + "\nCompilation failure is not a valid causal control")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
    project = run / "unity"
    for name in ("Assets/Editor", "Assets/Plugins", "Packages", "ProjectSettings"):
        (project / name).mkdir(parents=True)
    shutil.copyfile(fixture / "Editor/NotesRunner.cs", project / "Assets/Editor/NotesRunner.cs")
    # Original GH extended layout types; native gameplay controllers remain dormant.
    for dll in managed.glob("*.dll"):
        if dll.name.startswith(("UnityEngine", "Unity.", "System", "Microsoft", "Mono.", "mscorlib", "netstandard")):
            continue
        shutil.copyfile(dll, project / "Assets/Plugins" / dll.name)
    (project / "Assets/Plugins/Unity.InputSystem.dll").unlink(missing_ok=True)
    for name in ("Unity.Addressables.dll", "Unity.ResourceManager.dll", "Unity.Timeline.dll", "Unity.Postprocessing.Runtime.dll", "UnityEngine.SpatialTracking.dll", "System.Runtime.CompilerServices.Unsafe.dll"):
        shutil.copyfile(managed / name, project / "Assets/Plugins" / name)
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {"com.unity.ugui": "1.0.0", "com.unity.textmeshpro": "3.0.6", "com.unity.inputsystem": "1.4.4", **{"com.unity.modules." + name: "1.0.0" for name in ("ui", "imgui", "jsonserialize", "physics", "imageconversion", "animation", "audio", "particlesystem", "director", "terrain", "cloth", "video", "assetbundle", "physics2d", "unitywebrequest", "unitywebrequestassetbundle", "unitywebrequestaudio", "unitywebrequesttexture", "unitywebrequestwww", "ai", "xr")}}}) + "\n")
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    path = run / "manifest.json"
    path.write_text(json.dumps(manifest, indent=2) + "\n")
    bound = [row_path, loc_path, *fixture.glob("*.cs"), fixture / "Editor/NotesRunner.cs", fixture / "Notes.csproj", managed / "GH.Runtime.dll", managed / "GH.Shared.dll"]
    (run / "source-hashes.json").write_text(json.dumps({"sha256": {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in bound}, "original_note": {"en": english, "de": german}, "coverage": "partial" if args.case else "production-and-three-causal-controls", "limits": ["Complete production BuildNote executes unchanged; authored clone and external caption-style seams are explicit and perform no note sizing.", "Actual original GH extended LayoutElement/LayoutGroup classes and actual Unity/TMP glyph layout execute; editor-compatible uGUI/TMP 3.0.6 essentials font/rendering replaces the game row art and original Windows font shader.", "No game menu prefab, VR headset, multiplayer image or FPS is asserted."]}, indent=2) + "\n")
    command = [str(args.unity), "-batchmode", "-force-glcore", "-projectPath", str(project), "-executeMethod", "NotesRunner.Start", "-notesManifest", str(path), "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    try:
        result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    finally:
        for name in ("Library", "Temp"):
            shutil.rmtree(project / name, ignore_errors=True)
    native_layout = {}
    for name in ("GH.Runtime.dll", "GH.Shared.dll"):
        source_hash = hashlib.sha256((managed / name).read_bytes()).hexdigest()
        loaded_hash = hashlib.sha256((project / "Assets/Plugins" / name).read_bytes()).hexdigest()
        native_layout[name] = {"original_sha256": source_hash, "loaded_sha256": loaded_hash}
        if source_hash != loaded_hash:
            raise SystemExit("FAIL: loaded native layout assembly changed during fixture import: " + name)
    (run / "native-layout-provenance.json").write_text(json.dumps(native_layout, indent=2) + "\n")
    report = Path(manifest["result"])
    if report.is_file():
        print(report.read_text(), end="")
    if result.returncode or not report.is_file():
        raise SystemExit("FAIL Unity: " + str(run / "unity.log"))
    print(("PARTIAL PASS" if args.case else "PASS") + ": " + str(len(variants)) + " native note variants; " + str(run))


if __name__ == "__main__":
    main()
