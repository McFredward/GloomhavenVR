#!/usr/bin/env python3
"""Execute production Quest exclusions through managed native-component seams.

This checks interaction/tooltip ownership and pooled native rebindings. It does
not simulate Unity colors, actual pointer events, store services or headset UI.
Actual original Tooltip/API evidence is a separate editor/SDK fixture.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, help="Source checkout to validate, default this checkout.")
    parser.add_argument("--game-managed", type=Path, help="Optional owned GH.Runtime/UGUI directory for the native SDK ABI probe.")
    parser.add_argument("--unity-managed", type=Path, help="Exact Unity editor managed module directory for the optional SDK ABI probe.")
    args = parser.parse_args()
    if bool(args.game_managed) != bool(args.unity_managed):
        parser.error("The native SDK probe needs both --game-managed and --unity-managed.")
    root = Path(__file__).resolve().parents[1]
    source = (args.source or root).resolve()
    paths = {"QuestGameScope.cs": source / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestGameScope.cs",
             "QuestText.cs": source / "src/GloomhavenVR/Core/Loc/QuestText.cs"}
    original = {name: path.read_text() for name, path in paths.items()}
    controls = [
        ("private-base-field-ignored", "QuestGameScope.cs",
         "type != null && field == null; type = type.BaseType", "type != null && field == null; type = null",
         "inherited private promotion button disabled"),
        ("load-identity-inverted", "QuestGameScope.cs", 'as string == "Consoles/LEARN_MORE";', 'as string == "GUI_LOAD";',
         "ordinary load ignores display name spoof"),
        ("pooled-native-interaction-not-restored", "QuestGameScope.cs", "selectable.interactable = entry.previousInteractable;", "selectable.interactable = false;",
         "pooled load row restores captured native interaction immediately"),
        ("added-tooltip-not-retired", "QuestGameScope.cs", "UnityEngine.Object.Destroy(entry.tooltip);", "",
         "pooled load removes only added tooltip"),
        ("native-tooltip-destroyed", "QuestGameScope.cs", "if (entry.tooltipCreated)", "if (entry.tooltip != null)",
         "native tooltip never destroyed"),
        ("pooled-purchase-gate-retained", "QuestGameScope.cs", "entry.purchaseMode != 0 && !IsPurchase(entry.owner, entry.purchaseMode)", "entry.purchaseMode < 0 && !IsPurchase(entry.owner, entry.purchaseMode)",
         "pooled load row restores captured native interaction immediately"),
        ("original-purchase-callback-cleared", "QuestGameScope.cs", "selectable.interactable = false;", "selectable.interactable = false;\n                if (selectable is Button button) button.onClick = null;",
         "native promo callback retained"),
        ("native-tooltip-disabled", "QuestGameScope.cs", "enabled.SetValue(entry.tooltip, true);", "enabled.SetValue(entry.tooltip, false);",
         "native purchase tooltip attached"),
        ("german-tooltip-forced-english", "QuestGameScope.cs", "Application.systemLanguage == SystemLanguage.German", "false",
         "purchase tooltip German text"),
        ("purchase-panel-tooltip-misrouted", "QuestGameScope.cs", "entry.tooltipHost != null ? entry.tooltipHost : entry.button.gameObject", "entry.button.gameObject",
         "native tooltip attaches to active purchase panel"),
    ]
    output = root / ".planning/debug/quest-game-scope"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    proof = {"sources": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in paths.items()},
             "limit": "Production source with managed component/scene/tooltip seams; no headset color or native pointer claim.", "cases": []}
    print("Quest native purchase scope proof: " + str(run), flush=True)
    for name, filename, before, after, expected in [("production", "", "", "", "")] + controls:
        case = run / name
        case.mkdir()
        for name_source, value in original.items():
            if name_source == filename:
                assert value.count(before) == 1, "mutation binding drift: " + name
                value = value.replace(before, after, 1)
            (case / name_source).write_text(value)
        fixture = case / "fixture"
        shutil.copytree(root / "tests/QuestGameScope.Tests", fixture, ignore=shutil.ignore_patterns("bin", "obj"))
        project = fixture / "QuestGameScope.Tests.csproj"
        result = subprocess.run([dotnet, "run", "--project", str(project), "-c", "Release",
                                 "-p:RuntimeSource=" + str(case)], text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        compiled = "error CS" not in result.stdout and "error MSB" not in result.stdout
        passed = compiled and ((result.returncode == 0 and "PASS Quest native purchase scope:" in result.stdout)
                               if not expected else (result.returncode != 0 and "FAIL " + expected in result.stdout))
        proof["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
        (run / "proof.json").write_text(json.dumps(proof, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        print(next(line for line in result.stdout.splitlines() if line.startswith("PASS ")) if not expected
              else "PASS rejected " + name + " at " + expected, flush=True)
    print("PASS production Quest native purchase scope + " + str(len(controls)) + " defect controls", flush=True)
    if args.game_managed:
        case = run / "native-sdk"
        case.mkdir()
        for name, value in original.items():
            (case / name).write_text(value)
        fixture = case / "fixture"
        shutil.copytree(root / "tests/QuestGameScope.Tests", fixture, ignore=shutil.ignore_patterns("bin", "obj"))
        result = subprocess.run([dotnet, "run", "--project", str(fixture / "QuestGameScope.NativeSDK.csproj"), "-c", "Release",
                                 "-p:RuntimeSource=" + str(case), "-p:UnityManaged=" + str(args.unity_managed.resolve()),
                                 "-p:GameManaged=" + str(args.game_managed.resolve()), "--", str(args.game_managed.resolve())],
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        passed = result.returncode == 0 and "PASS Quest native scope SDK reflection:" in result.stdout
        references = {"GH.Runtime.dll": args.game_managed / "GH.Runtime.dll",
                      "UnityEngine.UI.dll": args.game_managed / "UnityEngine.UI.dll",
                      "UnityEngine.CoreModule.dll": args.unity_managed / "UnityEngine.CoreModule.dll"}
        proof["nativeSdk"] = {"exitCode": result.returncode, "passed": passed,
                              "references": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in references.items()},
                              "limit": "Production scope compiled against actual Unity/UGUI; original GH.Runtime reflection without constructing engine objects or invoking native callbacks."}
        (run / "proof.json").write_text(json.dumps(proof, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL native-sdk; see " + str(case / "console.log"))
        print(next(line for line in result.stdout.splitlines() if line.startswith("PASS ")), flush=True)


if __name__ == "__main__":
    main()
