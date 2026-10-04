#!/usr/bin/env python3
"""Execute the production Quest option policy and shared row filter.

This checks live default-key coverage, desktop parity and the existing generic
menu folds. Managed seams do not prove native headset layout or hover rendering.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, help="Source checkout to validate, default this checkout.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    source = (args.source or root).resolve()
    options = source / "src/GloomhavenVR/WorldUI/Options"
    policy = (options / "QuestOptionVisibility.cs").read_text()
    dependencies = (options / "VROptionsTab.8.Dependencies.cs").read_text()
    matches = re.findall(r"    private static bool IsRowVisible\(ConfigCatalog.ConfigItem item\) =>.*?;", dependencies, re.S)
    assert len(matches) == 1, "shared row-filter source contract changed"
    fixture = ("using GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI;\n"
               "internal static partial class VROptionsTab {\n" + matches[0] + "\n}\n")
    # Every current generic view reaches the same filter; add new entries through
    # the unchanged live catalog, not through a copied Quest settings tree.
    bindings = {
        "VROptionsTab.3.Content.cs": ["item == null || !IsRowVisible(item)", "IsRowVisible(group.Items[i])", "!IsRowVisible(entry)"],
        "VROptionsTab.6.BoardTopic.cs": ["!IsRowVisible(resolved[i])", "!IsRowVisible(item)"],
        "VROptionsTab.7.TopicTrees.cs": ["!IsRowVisible(resolved[i])", "!IsRowVisible(item)"],
    }
    for filename, snippets in bindings.items():
        text = (options / filename).read_text()
        for snippet in snippets:
            assert snippet in text, "generic menu visibility binding changed: " + filename
    keys = set()
    for path in (source / "src/GloomhavenVR/Defaults").glob("*.cs"):
        keys.update(re.findall(r"//\s*=>\s*\[([^\]]+)\]\s+([A-Za-z0-9_]+)", path.read_text()))
    assert ("MixedReality", "KeyColor") in keys and ("WorldUI", "DesktopMirrorLeftEye") in keys
    assert '_file.Bind("MixedReality", "KeyColor", Defaults.KeyColor,' in (source / "src/GloomhavenVR/Core/MixedReality/MixedReality.cs").read_text()
    assert '_file.Bind("WorldUI", "DesktopMirrorLeftEye",' in (source / "src/GloomhavenVR/WorldUI/WorldUIConfig.cs").read_text()
    controls = [
        ("quest-key-color-leaks", "QuestOptionVisibility.cs", '"KeyColor"', '"NeverMatch"', "Quest exact exclusions match live keys"),
        ("quest-monitor-eye-leaks", "QuestOptionVisibility.cs", '"DesktopMirrorLeftEye"', '"NeverMatch"', "Quest exact exclusions match live keys"),
        ("desktop-platform-gate-inverted", "QuestOptionVisibility.cs", "!questStandalone", "questStandalone", "desktop all live options remain visible"),
        ("own-page-fold-lost", "Options.fixture", "!HasItsOwnPage(item)", "true", "platform policy retains own-page fold"),
        ("variant-fold-lost", "Options.fixture", "IsShownForCurrentVariant(item)", "true", "platform policy retains variant fold"),
        ("dependency-fold-lost", "Options.fixture", "DependencyMet(item)", "true", "platform policy retains dependency fold"),
    ]
    output = root / ".planning/debug/quest-options"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    sources = {"QuestOptionVisibility.cs": policy, "Options.fixture": fixture}
    proof = {"sources": {name: hashlib.sha256(value.encode()).hexdigest() for name, value in sources.items()},
             "liveDefaultKeys": len(keys), "menuBindings": bindings, "cases": [],
             "limit": "Production policy/shared row filter with managed catalog/platform seams; native headset layout remains unverified."}
    print("Quest option visibility proof: " + str(run), flush=True)
    for name, filename, before, after, expected in [("production", "", "", "", "")] + controls:
        case = run / name
        case.mkdir()
        for item, value in sources.items():
            if item == filename:
                assert value.count(before) == 1, "mutation binding drift: " + name
                value = value.replace(before, after, 1)
            (case / item).write_text(value)
        (case / "keys.tsv").write_text("".join(section + "\t" + key + "\n" for section, key in sorted(keys)))
        test = case / "fixture"
        shutil.copytree(root / "tests/QuestOptions.Tests", test, ignore=shutil.ignore_patterns("bin", "obj"))
        result = subprocess.run([dotnet, "run", "--project", str(test / "QuestOptions.Tests.csproj"), "-c", "Release",
                                 "-p:RuntimeSource=" + str(case), "--", str(case / "keys.tsv")],
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        compiled = "error CS" not in result.stdout and "error MSB" not in result.stdout
        passed = compiled and ((result.returncode == 0 and "PASS Quest option visibility:" in result.stdout)
                               if not expected else (result.returncode != 0 and "FAIL " + expected in result.stdout))
        proof["cases"].append({"name": name, "exitCode": result.returncode, "passed": passed, "expected": expected})
        (run / "proof.json").write_text(json.dumps(proof, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        print(next(line for line in result.stdout.splitlines() if line.startswith("PASS ")) if not expected
              else "PASS rejected " + name + " at " + expected, flush=True)


if __name__ == "__main__":
    main()
