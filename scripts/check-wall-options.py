#!/usr/bin/env python3
"""Compile original wall config/UI paths with real BepInEx and explicit Unity/TMP boundaries."""
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


def member(source, signature):
    assert source.count(signature) == 1, "Production binding drift: " + signature
    start = source.index(signature)
    brace, semi = source.find("{", start), source.find(";", start)
    if semi >= 0 and (brace < 0 or semi < brace):
        return source[start:semi + 1]
    end, depth = brace + 1, 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def generate(source):
    base = "src/GloomhavenVR/"
    catalog = source[base + "WorldUI/Options/ConfigCatalog.cs"]
    catalog_members = [
        "internal enum ConfigKind", "internal sealed class ConfigItem",
        "private static void Classify(", "private static void ReadAcceptable(",
        "private static object[] EnumValues(", "private static bool IsNumeric(",
        "private static bool IsIntegral(", "internal static string ChoiceText(",
        "private static int IndexOf(", "private static bool SameValue(",
    ]
    output = {
        "PerfConfig.cs": source[base + "Core/Perf/PerfConfig.FrameRendering.cs"],
        "Defaults.cs": source[base + "Defaults/Defaults.FrameRendering.cs"],
        "FrameDefaults.cs": source[base + "Core/Startup/FrameDefaults.cs"],
        "ConfigSteps.cs": source[base + "WorldUI/Options/ConfigSteps.cs"],
        "Catalog.cs": "#pragma warning disable CS0649\nusing System; using System.Globalization; using System.Reflection; using BepInEx.Configuration; using GloomhavenVR.Core; using UnityEngine; namespace GloomhavenVR.WorldUI; internal static partial class ConfigCatalog {\n"
        + "\n".join(member(catalog, name) for name in catalog_members) + "\n}",
    }
    dependencies = source[base + "WorldUI/Options/VROptionsTab.8.Dependencies.cs"]
    start = dependencies.index("    private readonly struct DependencyRule")
    end = dependencies.index("    /// Rows a PAGE OF THIS MENU")
    end = dependencies.rfind("    /// <summary>", start, end)
    dropdown = member(source[base + "WorldUI/Options/VROptionsTab.2.Rows.cs"], "private static bool BuildChoiceRow(")
    output["Options.cs"] = "using System; using System.Collections.Generic; using UnityEngine; using TMPro; namespace GloomhavenVR.WorldUI; internal static partial class VROptionsTab {\n" + dependencies[start:end] + dropdown + "\n}"
    labels = source[base + "Core/Loc/Loc.cs"]
    literal = r'("(?:[^"\\]|\\.)*")'
    entries = []
    for key in ["wall_visibility_regular", "wall_visibility_hidden", "wall_visibility_auto"]:
        match = re.search(r'\["' + key + r'"\]\s*=\s*Pair\(\s*' + literal + r'\s*,\s*' + literal + r'\s*\)', labels)
        assert match, "Original translated choice missing: " + key
        entries.append('["' + key + '"] = new[] { ' + ", ".join(match.groups()) + " },")
    output["OriginalWallLabels.cs"] = "using System.Collections.Generic; namespace GloomhavenVR.Core; internal static class OriginalWallLabels { internal static readonly Dictionary<string,string[]> All = new() { " + "\n".join(entries) + " }; }"
    return output


def static_contract(source):
    base = "src/GloomhavenVR/"
    for key in ["WallVisibilityModeCount", "WallAutoHideBelowFpsCount"]:
        curated = source[base + "WorldUI/Options/VROptionsTab.4.Curated.cs"]
        assert curated.count('new("Optimize", "' + key + '", "")') == 1, "Each new wall setting has exactly one curated door"
        visibility = curated.index('LocKey = "sec_visibility"')
        assert visibility < curated.index('new("Optimize", "' + key + '", "")') < curated.index('new("Compat", "WallFade", "wall_see_through")'), "Wall modes sit beside the existing visibility controls"
        catalog = source[base + "WorldUI/Options/ConfigCatalog.cs"]
        topic = member(catalog, "private static ConfigTopic TopicOf(")
        assert '|| key == "' + key + '"' in topic, "Wall settings remain reachable in Advanced graphics"
        assert '["Optimize/' + key + '"] = Pair(' in source[base + "Core/Loc/Loc.ConfigNames.cs"], "Wall setting caption has EN/DE names"
        assert '["Optimize/' + key + '"] = Pair(' in source[base + "Core/Loc/Loc.PlayerHelp.cs"], "Wall setting has EN/DE player help"
        assert '["Optimize/' + key + '"] =' in source[base + "Core/Loc/Loc.ConfigDescriptions.German.cs"], "Wall setting config help is localized"
    profiles = source[base + "Rig/GraphicsProfiles.cs"]
    assert not re.search(r"Set\(PerfConfig\.Wall(?:VisibilityMode|AutoHideBelowFps)Count", profiles), "Graphics presets preserve explicit wall choices"
    assert 'return ChoiceText(item, v);' in member(source[base + "WorldUI/Options/ConfigCatalog.cs"], "internal static string ValueText("), "Fallback choice readouts use named values"
    assert 'ChoiceText(item, item.Entry.DefaultValue)' in member(source[base + "WorldUI/Options/ConfigCatalog.cs"], "internal static string Tooltip("), "Default tooltip also uses named choices"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/wall-options")
    parser.add_argument("--case", action="append", help="Run only named variants; this is a partial development check")
    args = parser.parse_args()
    paths = [
        "Core/Perf/PerfConfig.FrameRendering.cs", "Defaults/Defaults.FrameRendering.cs", "Core/Startup/FrameDefaults.cs",
        "Core/Loc/Loc.cs", "Core/Loc/Loc.ConfigNames.cs", "Core/Loc/Loc.PlayerHelp.cs", "Core/Loc/Loc.ConfigDescriptions.German.cs",
        "WorldUI/Options/ConfigCatalog.cs", "WorldUI/Options/ConfigSteps.cs", "WorldUI/Options/VROptionsTab.2.Rows.cs",
        "WorldUI/Options/VROptionsTab.4.Curated.cs", "WorldUI/Options/VROptionsTab.8.Dependencies.cs", "Rig/GraphicsProfiles.cs",
    ]
    source = {"src/GloomhavenVR/" + path: (args.source_root / "src/GloomhavenVR" / path).read_text() for path in paths}
    static_contract(source)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    hashes = {path: hashlib.sha256((args.source_root / path).read_bytes()).hexdigest() for path in source}
    fixture = ROOT / "tests/wall-options"
    for path in [Path(__file__).resolve(), *sorted(fixture.glob("*"))]:
        if path.is_file(): hashes[str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
    (run / "source-hashes.json").write_text(json.dumps(hashes, indent=2) + "\n")
    generated = generate(source)
    cases = [("production", None, None, None, "")]
    mutations = [
        ("default-mode", "Defaults.cs", "WallVisibilityModeCount = 2;", "WallVisibilityModeCount = 0;", "unbound wall defaults are automatic"),
        ("default-fps", "Defaults.cs", "WallAutoHideBelowFpsCount = 15;", "WallAutoHideBelowFpsCount = 25;", "unbound wall defaults are automatic"),
        ("mode-slider", "PerfConfig.cs", "new AcceptableValueList<int>(0, 1, 2)", "new AcceptableValueRange<int>(0, 2)", "three wall modes use a dropdown"),
        ("mode-index", "PerfConfig.cs", "new AcceptableValueList<int>(0, 1, 2)", "new AcceptableValueList<int>(2, 1, 0)", "mode indexes map to exact stored integers"),
        ("threshold-range", "PerfConfig.cs", "new AcceptableValueRange<int>(5, 90)", "new AcceptableValueRange<int>(10, 90)", "automatic threshold is an editable bounded FPS integer"),
        ("choice-label", "Catalog.cs", 'case 1: return Loc.Mod("wall_visibility_hidden");', 'case 1: return "1";', "each wall choice reads in the selected language"),
        ("dropdown-label", "Options.cs", "ConfigCatalog.ChoiceText(item, choices[i])", '(choices[i].ToString() ?? string.Empty)', "each wall choice reads in the selected language"),
        ("localized-index", "Options.cs", 'string.Equals(choices[i]?.ToString() ?? string.Empty, now, StringComparison.Ordinal)', 'string.Equals(ConfigCatalog.ChoiceText(item, choices[i]), now, StringComparison.Ordinal)', "localized labels do not change selected index"),
        ("dropdown-write", "Options.cs", "item.Entry.BoxedValue = choices[index]", "item.Entry.BoxedValue = choices[(index + 1) % choices.Length]", "dropdown selection writes the exact wall policy"),
        ("threshold-dependency", "Options.cs", "mode == 2)", "mode == 1)", "threshold is visible only in automatic mode"),
        ("fade-dependency", "Options.cs", "mode != 1)", "mode == 1)", "regular fade control folds only for permanent hiding"),
        ("legacy-master", "Options.cs", '["Optimize/WallAutoHideBelowFpsCount"] =', '["Optimize/WallVisibilityModeCount"] = new("Compat", "WallFade", On),\n        ["Optimize/WallAutoHideBelowFpsCount"] =', "wall policy stays reachable when regular fading is disabled"),
        ("fps-step", "ConfigSteps.cs", '["Optimize/WallAutoHideBelowFpsCount"] = 1d', '["Optimize/WallAutoHideBelowFpsCount"] = 5d', "FPS threshold steps by one"),
    ]
    cases += mutations
    if args.case:
        unknown = set(args.case) - {case[0] for case in cases}
        if unknown: raise SystemExit("Unknown variants: " + ", ".join(sorted(unknown)))
        cases = [case for case in cases if case[0] in args.case]
    dotnet = os.environ.get("DOTNET") or shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    records = []
    for name, filename, old, new, expected in cases:
        case = run / name
        case.mkdir()
        for path, text in generated.items():
            if path == filename:
                assert text.count(old) == 1, "Causal control binding drift: " + name
                text = text.replace(old, new, 1)
            (case / path).write_text(text)
        command = [dotnet, "build", str(fixture / "WallOptions.csproj"), "-c", "Release", "-p:ProductionDir=" + str(case), "-p:FixtureDir=" + str(fixture), "-p:BaseIntermediateOutputPath=" + str(case / "obj") + "/", "-p:OutputPath=" + str(case / "bin") + "/"]
        build = subprocess.run(command, capture_output=True, text=True, timeout=90)
        (case / "build.txt").write_text(build.stdout + build.stderr)
        if build.returncode: raise SystemExit(build.stdout + build.stderr)
        result = subprocess.run([dotnet, str(case / "bin/WallOptions.dll"), str(case)], capture_output=True, text=True, timeout=30)
        output = result.stdout + result.stderr
        (case / "result.txt").write_text(output)
        passed = result.returncode == 0 if name == "production" else result.returncode != 0 and "FAIL: " + expected in output
        records.append({"variant": name, "exit": result.returncode, "passed": passed, "expected": expected})
        print(("PASS: " if passed else "FAIL: ") + name, flush=True)
        if not passed:
            print(output, end="")
            (run / "results.json").write_text(json.dumps(records, indent=2) + "\n")
            raise SystemExit(1)
    stable = all(hashlib.sha256((args.source_root / path if path in source else ROOT / path).read_bytes()).hexdigest() == expected for path, expected in hashes.items())
    (run / "source-stability.json").write_text(json.dumps({"unchanged": stable}, indent=2) + "\n")
    (run / "results.json").write_text(json.dumps(records, indent=2) + "\n")
    assert stable, "Bound source changed during the run"
    print("PASS: original wall config/UI paths; " + str(len(records)) + " runtime variants; evidence: " + str(run))


if __name__ == "__main__":
    main()
