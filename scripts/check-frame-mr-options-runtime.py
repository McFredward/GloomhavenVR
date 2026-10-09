#!/usr/bin/env python3
"""Execute production Frame MR option guards and painting with real Unity UI.

OpenXR lifecycle/configuration are explicit inputs; production policy, clicks,
painting, bool rows and original game tooltip attachment execute unchanged.
This proves native control hit/availability behavior, not headset passthrough.
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
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/frame-mr-options-runtime")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--case", action="append", help="Partial development run; repeat for selected causal controls")
    args = parser.parse_args()
    row_path = args.source_root / "src/GloomhavenVR/WorldUI/Options/VROptionsTab.2.Rows.cs"
    loc_path = args.source_root / "src/GloomhavenVR/Core/Loc/Loc.cs"
    rows, loc = row_path.read_text(), loc_path.read_text()
    base = args.source_root / "src/GloomhavenVR/WorldUI/Options"
    policy = (base / "VROptionsTab.MixedReality.cs").read_text()
    tiles = (base / "VariantTiles.cs").read_text()
    table = (base / "VariantTilesTable.cs").read_text()
    dependencies = (base / "VROptionsTab.8.Dependencies.cs").read_text()
    table_members = ["private static bool MixedRealityOn", "private static SkyStyle EffectiveSky(",
        "private static bool ChooseSky(", "private static bool ChooseMixedReality(",
        "internal const int MixedRealityEnvironmentIndex", "internal static int EnvironmentIndex(", "internal static bool ChooseEnvironment("]
    # Expression members terminate at the first semicolon; methods/classes retain nested bodies.
    def member(text, signature):
        start = text.index(signature)
        brace, semi = text.find("{", start), text.find(";", start)
        return text[start:semi + 1] if semi >= 0 and (brace < 0 or semi < brace) else method(text, signature)
    environment = "\n".join(member(table, name) for name in table_members)
    environment += "\n" + member(dependencies, "private static bool IsRowVisible(")
    tile_members = ["private sealed class VariantTile", "private sealed class BuiltTile",
        "private static void RepaintVariantTiles(", "private static void OnVariantTileClicked("]
    colors = tiles[tiles.index("    private static readonly Color TileBorderOn ="):tiles.index("    /// <summary>Built sprites,")]
    drawing = colors + "\n".join(member(tiles, name) for name in tile_members)
    row_members = ["private static void BuildPresetRow(", "private static bool BuildBoolRow(", "private static void Apply(",
        "private static void AttachTooltip(", "private static void AttachHoverHint(TMP_Text",
        "private static void AttachHoverHint(GameObject", "private static string HintFor(",
        "private static void PaintToggleState("]
    controls = "\n".join(member(rows, name) for name in row_members)
    literal = r'("(?:[^"\\]|\\.)*")'
    labels = []
    for key in ["mr_frame_available", "mr_frame_checking", "mr_frame_update_required", "vr_on", "vr_off"]:
        match = re.search(r'\["' + key + r'"\]\s*=\s*Pair\(\s*' + literal + r'\s*,\s*' + literal + r'\s*\)', loc)
        assert match, "Original translated MR choice missing: " + key
        labels.append('["' + key + '"] = new[] { ' + ", ".join(match.groups()) + ' },')
    label_source = "using System.Collections.Generic; namespace GloomhavenVR.Core { internal static class OriginalMrLabels { internal static readonly Dictionary<string,string[]> All = new() { " + "\n".join(labels) + " }; } }"
    wrapper = "using System; using System.Collections.Generic; using GloomhavenVR.Core; using TMPro; using UnityEngine; using UnityEngine.UI; namespace GloomhavenVR.WorldUI { internal static partial class VROptionsTab {\n"
    generated = {"Policy.cs": policy, "Environment.cs": wrapper + environment + "\n}}", "Drawing.cs": wrapper + drawing + "\n}}", "Rows.cs": wrapper + controls + "\n}}", "OriginalMrLabels.cs": label_source}
    variants = [("production", None, None, None, "")]
    changes = [
        ("tile-availability", "Drawing.cs", "made.Button.interactable = available;", "made.Button.interactable = true;", "checking capability disables both MR controls"),
        ("generic-guard", "Rows.cs", "if (IsMixedRealitySwitch(item) && !CanEditMixedRealitySwitch(item))", "if (bool.Parse(\"false\"))", "tile and generic Advanced callback cannot enable unavailable MR"),
        ("compatibility-refresh", "Policy.cs", "MixedRealityAvailabilityRefreshers[i]();", "GC.KeepAlive(i);", "original game tooltip receives the current compatibility explanation"),
        ("saved-off-escape", "Policy.cs", "MixedRealityCanEnable || item.Entry.BoxedValue is true", "MixedRealityCanEnable", "incompatible saved-on MR can still be switched off"),
        ("tooltip-disabled", "Rows.cs", "target.TooltipEnabled = true;", "target.TooltipEnabled = false;", "disabled MR control retains a live original game tooltip target"),
        ("pc-capability-gate", "Policy.cs", "!FrameNativePassthrough.Required || FrameNativePassthrough.IsAvailable", "FrameNativePassthrough.IsAvailable", "PC streaming MR remains available without Frame capabilities"),
    ]
    for name, filename, old, new, expected in changes:
        assert generated[filename].count(old) == 1, "Causal binding drift: " + name
        variants.append((name, filename, old, new, expected))
    if args.case:
        unknown = set(args.case) - {entry[0] for entry in variants}
        if unknown:
            parser.error("Unknown cases: " + str(sorted(unknown)))
        variants = [entry for entry in variants if entry[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = ROOT / "scripts/frame-mr-options-runtime"
    managed = args.source_root / "ressources/GH_Data/Managed"
    tick = (base / "VROptionsTab.1.Inject.cs").read_text()
    assert "RefreshMixedRealityAvailability();" in method(tick, "internal static void Tick()"), "Live options tick must refresh capability controls"
    assert "ClearMixedRealityAvailabilityRefreshers();" in method(rows, "private static void ClearRows()"), "Page teardown must release native compatibility callbacks"
    assert "ClearMixedRealityAvailabilityRefreshers();" in method(tick, "private static void Forget()"), "Shutdown and stale-pane teardown must release native compatibility callbacks"
    assert "RegisterMixedRealityAvailabilityRefresh(() => RepaintVariantTiles(built));" in tiles, "Actual environment tile strip must subscribe to native compatibility changes"
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    manifest = {"result": str(run / "results.txt"), "evidence": str(run), "cases": []}
    for name, filename, old, new, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        for path, body in generated.items():
            (production / path).write_text(body.replace(old, new, 1) if path == filename else body)
        project = build / "FrameMrOptions.csproj"
        shutil.copyfile(fixture / "FrameMrOptions.csproj", project)
        assembly = "FrameMrOptions_" + name.replace("-", "_")
        built = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet", "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production), "-p:UnityManaged=" + str(args.unity.parent / "Data/Managed"), "-p:GameManaged=" + str(managed.resolve())], capture_output=True, text=True)
        (build / "build.log").write_text(built.stdout + built.stderr)
        if built.returncode:
            raise SystemExit(built.stdout + built.stderr + "\nCompilation failure is not a valid causal control")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
    project = run / "unity"
    for name in ("Assets/Editor", "Assets/Plugins", "Packages", "ProjectSettings"):
        (project / name).mkdir(parents=True)
    shutil.copyfile(fixture / "Editor/FrameMrOptionsRunner.cs", project / "Assets/Editor/FrameMrOptionsRunner.cs")
    # Original game tooltip and rectangular-raycast classes; gameplay controllers stay dormant.
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
    bound = [Path(__file__).resolve(), row_path, loc_path, base / "VariantTiles.cs", base / "VariantTilesTable.cs", base / "VROptionsTab.MixedReality.cs", base / "VROptionsTab.8.Dependencies.cs", base / "VROptionsTab.1.Inject.cs", *fixture.glob("*.cs"), fixture / "Editor/FrameMrOptionsRunner.cs", fixture / "FrameMrOptions.csproj", managed / "GH.Runtime.dll"]
    (run / "source-hashes.json").write_text(json.dumps({"sha256": {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in bound}, "coverage": "partial" if args.case else "production-and-six-causal-controls", "limits": ["Production MR policy, original tile callback/painting, bool row, generic Apply guard and native tooltip attachment execute unchanged.", "OpenXR lifecycle/capability, configuration storage, row donor/skin and whole-menu rebuild are explicit boundaries.", "Actual Unity/uGUI/TMP and original GH UITextTooltipTarget/raycast filter execute. No headset camera image or complete in-game menu is asserted."]}, indent=2) + "\n")
    command = [str(args.unity), "-batchmode", "-force-glcore", "-projectPath", str(project), "-executeMethod", "FrameMrOptionsRunner.Start", "-frameMrOptionsManifest", str(path), "-logFile", str(run / "unity.log")]
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
    # Preserve hashes, source excerpts, reports and screenshots rather than a
    # redundant imported game copy in every focused run.
    shutil.rmtree(project, ignore_errors=True)
    report = Path(manifest["result"])
    if report.is_file():
        print(report.read_text(), end="")
    if result.returncode or not report.is_file():
        raise SystemExit("FAIL Unity: " + str(run / "unity.log"))
    print(("PARTIAL PASS" if args.case else "PASS") + ": " + str(len(variants)) + " native MR option variants; " + str(run))


if __name__ == "__main__":
    main()
