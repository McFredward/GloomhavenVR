#!/usr/bin/env python3
"""Check bilingual, player-directed VR setting help against the actual bound key surface.

Uses the existing options census for literal and hand/board bindings. The two unresolved
pile-loop bindings are expanded from their original finite pileNames array. Stored keys
remain included even when hidden, so a later menu change cannot expose diagnostic prose.
This is a content/coverage check, not evidence of headset text geometry or rendering.
"""
from __future__ import annotations

import argparse
import copy
import importlib.util
import json
from pathlib import Path
import re
import shutil
import sys
import hashlib
import os
import subprocess
import tempfile
from concurrent.futures import ThreadPoolExecutor

ROOT = Path(__file__).resolve().parent.parent
LOC = ROOT / "src/GloomhavenVR/Core/Loc"
LITERAL = r'"(?:[^"\\]|\\.)*"'
EXPRESSION = rf'(?:{LITERAL}\s*(?:\+\s*)?)+'
PAIR = re.compile(rf'\[\s*"([^"\n]+)"\s*\]\s*=\s*Pair\(\s*({EXPRESSION})\s*,\s*({EXPRESSION})\s*\)')
DEVELOPER_PROSE = re.compile(
    r"\b(?:ModBuild|hardware[ -]test|source-proven|maintainer|user requested|you requested|"
    r"by ruling|by request|as requested|the user asked|previous build|earlier build|"
    r"single biggest performance gain|implementation details|plumbing|"
    r"Entwickler|Hardwaretest|Nutzerwunsch|auf deinen Wunsch|auf Wunsch des|"
    r"per Entscheidung|vorherigen Build|früheren Build)\b", re.IGNORECASE)


def resolve_dotnet():
    """Respect explicit hosts and find hosted CI's SDK before the local fallback."""
    return os.environ.get("DOTNET") or shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")


def load_options_census():
    spec = importlib.util.spec_from_file_location("options_coverage", ROOT / "scripts/check-options-coverage.py")
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_pairs(path: Path, census):
    text = census.strip_comments(path.read_text(encoding="utf-8"))
    pairs = {}
    for key, en, de in PAIR.findall(text):
        if key in pairs:
            raise ValueError(f"Duplicate help entry: {key}")
        pairs[key] = tuple("".join(json.loads(s) for s in re.findall(LITERAL, expr)) for expr in (en, de))
    return pairs


def bound_keys(census):
    keys, unresolved = census.key_surface()
    keys = set(keys)
    expected = {
        ("Cards", "FanRadiusFactor_.*"), ("Cards", "FanStepDegrees_.*"),
        ("Cards", r"^FanRadiusFactor_pileNames\[p\]$"),
        ("Cards", r"^FanStepDegrees_pileNames\[p\]$"),
    }
    if unresolved != expected:
        raise ValueError(f"Unreviewed dynamic setting families: {sorted(unresolved - expected)}")
    source = census.strip_comments((ROOT / "src/GloomhavenVR/Cards/CardsConfig.cs").read_text(encoding="utf-8"))
    match = re.search(r'\bstring\[\]\s+pileNames\s*=\s*\{([^}]+)\}', source)
    if match is None:
        raise ValueError("Original finite pileNames array was not found")
    piles = re.findall(r'"([^"\n]+)"', match.group(1))
    if set(piles) != {"Items", "Discard", "Burnt"} or len(piles) != 3:
        raise ValueError(f"Unreviewed pile family: {piles}")
    for prefix in ("FanRadiusFactor_", "FanStepDegrees_"):
        keys.update(("Cards", prefix + name) for name in piles)
    return keys


def issues(pairs, keys, hints, census):
    failures = []
    for section, key in sorted(keys):
        family = census.family_key(key)
        if section + "/" + key not in pairs and section + "/" + str(family) not in pairs:
            failures.append(f"Missing EN/DE player help: {section}/{key}")
    for key in sorted(hints):
        if key not in pairs:
            failures.append(f"Missing player heading/action help: {key}")
    for key, values in sorted(pairs.items()):
        for language, text in zip(("English", "German"), values):
            if not text.strip():
                failures.append(f"Empty {language} help: {key}")
            if len(text) > 300:
                failures.append(f"Overlong {language} help ({len(text)}): {key}")
            if DEVELOPER_PROSE.search(text):
                failures.append(f"Developer-facing {language} help: {key}")
    return failures


def self_test(pairs, keys, hints, census):
    controls = []
    missing = copy.deepcopy(pairs)
    del missing["RenderQuality/MsaaLevel"]
    controls.append(("missing exact setting", missing, "Missing EN/DE player help: RenderQuality/MsaaLevel"))
    missing_family = copy.deepcopy(pairs)
    del missing_family["Cards/FanStepDegrees_*"]
    controls.append(("missing dynamic pile family", missing_family, "Missing EN/DE player help: Cards/FanStepDegrees_Items"))
    old_prose = copy.deepcopy(pairs)
    old_prose["RenderQuality/MsaaLevel"] = ("Enabled because the user asked for it.", old_prose["RenderQuality/MsaaLevel"][1])
    controls.append(("developer instruction", old_prose, "Developer-facing English help: RenderQuality/MsaaLevel"))
    untranslated = copy.deepcopy(pairs)
    untranslated["RenderQuality/MsaaLevel"] = (untranslated["RenderQuality/MsaaLevel"][0], "")
    controls.append(("missing German explanation", untranslated, "Empty German help: RenderQuality/MsaaLevel"))
    heading = copy.deepcopy(pairs)
    del heading["h_vr_ct_anim_fx"]
    controls.append(("missing heading", heading, "Missing player heading/action help: h_vr_ct_anim_fx"))
    failures = []
    for name, changed, expected in controls:
        if expected not in issues(changed, keys, hints, census):
            failures.append(f"Negative control did not detect {name}")
        else:
            print(f"PASS negative control: {name}")
    return failures


def production_lookup(keys, hints, output: Path, negative_controls: bool):
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output.resolve()))
    coverage = run / "coverage.json"
    coverage.write_text(json.dumps({"keys": sorted(section + "/" + key for section, key in keys),
                                    "hints": sorted(hints)}, indent=2), encoding="utf-8")
    fixture = ROOT / "scripts/player-settings-help-runtime"
    originals = {name: (LOC / name).read_text(encoding="utf-8") for name in
                 ("Loc.PlayerHelp.cs", "Loc.ConfigDescriptions.cs", "Loc.ConfigDescriptions.German.cs")}
    mutations = {
        "original": (None, None, None),
        "no-family": ("FamilyKey(key)", "(string?)null", "card pile family uses the original resolver"),
        "force-english": ("value.TryGetValue(CurrentLanguage", 'value.TryGetValue("English"',
                          "German help uses the selected language"),
        "german-fallback": ('value["English"]', 'value["German"]', "unsupported language returns English"),
    }
    if not negative_controls:
        mutations = {"original": mutations["original"]}
    dotnet = resolve_dotnet()

    def execute(case):
        search, replacement, expected = mutations[case]
        directory = run / case
        directory.mkdir()
        for name, text in originals.items():
            if name == "Loc.PlayerHelp.cs" and search is not None:
                if text.count(search) != 1:
                    raise ValueError(f"Mutation anchor changed: {case}")
                text = text.replace(search, replacement, 1)
            (directory / name).write_text(text, encoding="utf-8")
        command = [dotnet, "build", str(fixture / "Help.csproj"), "-c", "Release",
                   "-p:ProductionDir=" + str(directory), "-p:FixtureDir=" + str(fixture),
                   "-p:BaseIntermediateOutputPath=" + str(directory / "obj") + "/",
                   "-p:OutputPath=" + str(directory / "bin") + "/"]
        build = subprocess.run(command, capture_output=True, text=True, timeout=90)
        (directory / "build.txt").write_text(build.stdout + build.stderr, encoding="utf-8")
        if build.returncode:
            raise ValueError("Production help compilation failed: " + case + "\n" + build.stdout + build.stderr)
        result = subprocess.run([dotnet, str(directory / "bin/Help.dll"), str(coverage)],
                                capture_output=True, text=True, timeout=30)
        (directory / "result.txt").write_text(result.stdout + result.stderr, encoding="utf-8")
        if case == "original":
            if result.returncode:
                raise ValueError(result.stdout + result.stderr)
            print(result.stdout, end="")
        elif result.returncode == 0 or expected not in result.stderr:
            raise ValueError("Production lookup negative control did not fail causally: " + case + "\n" + result.stderr)
        else:
            print("PASS production lookup negative control: " + case)
        return {"case": case, "exit": result.returncode, "expected_failure": expected}

    # Controls are independent, each with its own copied source and compiler outputs.
    with ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(execute, mutations))
    (run / "evidence.json").write_text(json.dumps({
        "source_sha256": {name: hashlib.sha256(text.encode()).hexdigest() for name, text in originals.items()},
        "bound_addresses": len(keys), "original_hint_ids": len(hints), "results": results,
    }, indent=2), encoding="utf-8")
    print("Evidence: " + str(run))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true", help="also run content and production lookup negative controls")
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/player-settings-help")
    args = parser.parse_args()
    census = load_options_census()
    try:
        pairs = read_pairs(LOC / "Loc.PlayerHelp.cs", census)
        old_pairs = read_pairs(LOC / "Loc.cs", census)
        hints = {key for key in old_pairs if key.startswith("h_")}
        keys = bound_keys(census)
        failures = issues(pairs, keys, hints, census)
        if args.self_test:
            failures.extend(self_test(pairs, keys, hints, census))
    except (ValueError, OSError) as error:
        failures = [str(error)]
    if failures:
        for failure in failures:
            print("FAIL " + failure, file=sys.stderr)
        return 1
    try:
        production_lookup(keys, hints, args.output_dir, args.self_test)
    except (ValueError, OSError, subprocess.TimeoutExpired) as error:
        print("FAIL " + str(error), file=sys.stderr)
        return 1
    config_count = sum(not key.startswith("h_") for key in pairs)
    hint_count = len(pairs) - config_count
    print(f"PASS player settings help: {len(keys)} bound keys, {config_count} exact/family entries, "
          f"{hint_count} heading/action entries, English and German; {len(hints)} original hints covered")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
