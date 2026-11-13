#!/usr/bin/env python3
"""Run the existing VR keyboard with managed input seams and Quest-only field leases.

Optional SDK checks compile the lease against the owned TMP and inspect the
original editing call relationships. Neither check establishes headset input.
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
    parser.add_argument("--source", type=Path)
    parser.add_argument("--game-managed", type=Path)
    parser.add_argument("--unity-managed", type=Path)
    args = parser.parse_args()
    if bool(args.game_managed) != bool(args.unity_managed):
        parser.error("Both --game-managed and --unity-managed are needed for native SDK checks.")
    root = Path(__file__).resolve().parents[1]
    source = (args.source or root).resolve()
    base = source / "src/GloomhavenVR/WorldUI/Grab"
    paths = {name: base / name for name in ("VRKeyboard.cs", "QuestKeyboardInputLease.cs")}
    original = {name: path.read_text() for name, path in paths.items()}
    controls = [
        ("quest-admission-flag-missing", "QuestKeyboardInputLease.cs", "field.shouldHideSoftKeyboard = true;", "field.shouldHideSoftKeyboard = false;",
         "Quest software keyboard hidden before original activation"),
        ("quest-lease-affects-desktop", "QuestKeyboardInputLease.cs", "QuestStandalonePlatform.Enabled ?", "true ?",
         "desktop flags unchanged on attach"),
        ("quest-lease-not-released", "VRKeyboard.cs", "_questInput?.Release();", "_ = _questInput;",
         "authored Quest software flag restored on detach"),
        ("authored-hidden-flag-overwritten", "QuestKeyboardInputLease.cs", "field != null && !_previousHideSoftKeyboard && field.shouldHideSoftKeyboard", "field != null",
         "already hidden native field remains hidden"),
        ("native-flag-change-overwritten", "QuestKeyboardInputLease.cs", "field != null && !_previousHideSoftKeyboard && field.shouldHideSoftKeyboard", "field != null && !_previousHideSoftKeyboard",
         "native software flag change is not overwritten on detach"),
        ("original-key-listener-not-parked", "VRKeyboard.cs", "        ParkNativeTyping();", "",
         "desktop existing writer and sentence case retained"),
        ("native-editing-path-bypassed", "VRKeyboard.cs", "field.ProcessEvent(new Event { character = c });", "field.text += c;",
         "one native event and change per character"),
        ("field-switch-leaks-ownership", "VRKeyboard.cs", 'if (IsShowing)\n            Detach("a different field was clicked");', 'if (!IsShowing)\n            Detach("a different field was clicked");',
         "switching fields releases only the previous Quest field"),
    ]
    output = root / ".planning/debug/quest-keyboard-input"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    proof = {"sources": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in paths.items()},
             "limit": "Full production VR keyboard with managed scene/events/TMP admission seams; no engine or headset input claim.", "cases": []}
    print("Quest keyboard input proof: " + str(run), flush=True)
    for name, filename, before, after, expected in [("production", "", "", "", "")] + controls:
        case = run / name
        case.mkdir()
        for source_name, value in original.items():
            if source_name == filename:
                assert value.count(before) == 1, "mutation binding drift: " + name
                value = value.replace(before, after, 1)
            (case / source_name).write_text(value)
        fixture = case / "fixture"
        shutil.copytree(root / "tests/QuestKeyboardInput.Tests", fixture, ignore=shutil.ignore_patterns("bin", "obj"))
        result = subprocess.run([dotnet, "run", "--project", str(fixture / "QuestKeyboardInput.Tests.csproj"), "-c", "Release",
                                 "-p:RuntimeSource=" + str(case)], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        compiled = "error CS" not in result.stdout and "error MSB" not in result.stdout
        passed = compiled and ((result.returncode == 0 and "PASS Quest keyboard input:" in result.stdout)
                               if not expected else (result.returncode != 0 and "FAIL " + expected in result.stdout))
        proof["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
        (run / "proof.json").write_text(json.dumps(proof, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        print(next(line for line in result.stdout.splitlines() if line.startswith("PASS ")) if not expected
              else "PASS rejected " + name + " at " + expected, flush=True)
    if args.game_managed:
        case = run / "native-sdk"
        case.mkdir()
        for name, value in original.items():
            (case / name).write_text(value)
        fixture = case / "fixture"
        shutil.copytree(root / "tests/QuestKeyboardInput.Tests", fixture, ignore=shutil.ignore_patterns("bin", "obj"))
        result = subprocess.run([dotnet, "run", "--project", str(fixture / "QuestKeyboardInput.NativeSDK.csproj"), "-c", "Release",
                                 "-p:RuntimeSource=" + str(case), "-p:UnityManaged=" + str(args.unity_managed.resolve()),
                                 "-p:GameManaged=" + str(args.game_managed.resolve()), "--", str(args.game_managed.resolve())],
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        references = {name: args.game_managed / name for name in ("GH.Runtime.dll", "Unity.TextMeshPro.dll", "UnityEngine.UI.dll")}
        references["UnityEngine.CoreModule.dll"] = args.unity_managed / "UnityEngine.CoreModule.dll"
        passed = result.returncode == 0 and "PASS Quest keyboard native SDK:" in result.stdout
        proof["nativeSdk"] = {"passed": passed, "exitCode": result.returncode,
                              "references": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in references.items()},
                              "limit": "Production flag lease compiles against exact TMP API; original game CIL/API inspected without native engine callbacks."}
        (run / "proof.json").write_text(json.dumps(proof, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL native-sdk; see " + str(case / "console.log"))
        print(next(line for line in result.stdout.splitlines() if line.startswith("PASS ")), flush=True)
    print("PASS production Quest keyboard + " + str(len(controls)) + " defect controls", flush=True)


if __name__ == "__main__":
    main()
