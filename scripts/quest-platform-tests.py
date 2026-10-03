#!/usr/bin/env python3
"""Execute original CoreModule with Android XR/IO bridge source and bounded defect controls."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    core = root / "src/GloomhavenVR/Core"
    sources = {
        "QuestStandalonePlatform.cs": core / "QuestStandalonePlatform.cs",
        "RuntimeDepsLoader.cs": core / "Startup/RuntimeDepsLoader.cs",
        "OpenXRBootstrap.cs": core / "Startup/OpenXRBootstrap.cs",
        "CoreModule.cs": core / "CoreModule.cs",
        "IVRModule.cs": core / "IVRModule.cs",
    }
    original = {name: path.read_text() for name, path in sources.items()}
    output = root / ".planning/debug/quest-platform"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    cases = [("production", original, "")]
    for name, target, before, after, expected in (
        ("android-dynamic-deps", "RuntimeDepsLoader.cs", "if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.Android)", "if (QuestStandalonePlatform.Enabled && UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsPlayer)", "android-deps"),
        ("owner-session-not-checked", "OpenXRBootstrap.cs", " || !QuestStandalonePlatform.SessionRunning", "", "owner-session-gate"),
        ("display-running-not-checked", "OpenXRBootstrap.cs", " || !displays.Any(d => d.running)", "", "display-running-gate"),
        ("input-running-not-checked", "OpenXRBootstrap.cs", " || !inputs.Any(i => i.running)", "", "input-running-gate"),
        ("greenscreen-quest-clear", "QuestStandalonePlatform.cs", "return Color.clear;", "return new Color(0, 1, 0, 1);", "native-alpha"),
        ("per-frame-native-request", "QuestStandalonePlatform.cs", "if (_lastPassthroughRequest != wanted)", "if (true)", "native-dedupe"),
        ("false-disable-result", "QuestStandalonePlatform.cs", "bool matched = _isPassthroughActive!() == wanted;", "bool matched = _isPassthroughActive!();", "native-disable"),
        ("external-manager-stop", "OpenXRBootstrap.cs", "QuestStandalonePlatform.SetPassthrough(false);", "{ QuestStandalonePlatform.SetPassthrough(false); XRGeneralSettings.Instance.Manager.StopSubsystems(); }", "external-teardown"),
        ("android-guard-removed", "QuestStandalonePlatform.cs", "Application.platform == RuntimePlatform.Android && _resourceDirectory != null", "_resourceDirectory != null", "configured-desktop-gate"),
        ("session-epoch-not-invalidated", "QuestStandalonePlatform.cs", "if (!_havePassthroughSessionGeneration || generation != _lastPassthroughSessionGeneration)", "if (!_havePassthroughSessionGeneration)", "native-session-recreate"),
    ):
        if original[target].count(before) != 1:
            raise RuntimeError("Mutation binding drift: " + name)
        mutated = dict(original)
        mutated[target] = mutated[target].replace(before, after, 1)
        cases.append((name, mutated, expected))
    # This binding proves the tested clear-color implementation is the actual MR
    # camera authority, while full rendering/backing parity remains a Unity/HW gate.
    mixed_reality = core / "MixedReality/MixedReality.cs"
    if "Color key = QuestStandalonePlatform.MixedRealityClearColor(KeyColor.Value);" not in mixed_reality.read_text():
        raise SystemExit("FAIL actual MR Tick is not bound to the tested Quest clear policy")
    evidence = {"sources": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in sources.items()},
                "mixedRealitySource": hashlib.sha256(mixed_reality.read_bytes()).hexdigest(), "cases": []}
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    print("Quest platform lifecycle evidence: " + str(run), flush=True)
    for name, contents, expected in cases:
        case = run / name
        case.mkdir()
        harness = case / "fixture"
        shutil.copytree(root / "tests/QuestPlatform.Tests", harness, ignore=shutil.ignore_patterns("bin", "obj"))
        for filename, source in contents.items():
            (case / filename).write_text(source)
        result = subprocess.run([dotnet, "run", "--project", str(harness / "QuestPlatform.Tests.csproj"),
                                 "--configuration", "Release", "--property:RuntimeSource=" + str(case),
                                 "--", str(case / "resources")], text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        passed = (result.returncode == 0 and "PASS Quest platform/core lifecycle:" in result.stdout) if not expected else (result.returncode != 0 and expected in result.stdout)
        evidence["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
        (run / "results.json").write_text(json.dumps(evidence, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        if not expected:
            print(next(line for line in result.stdout.splitlines() if line.startswith("PASS Quest platform/core lifecycle:")), flush=True)
        else:
            print("PASS rejected " + name + " at " + expected, flush=True)
    print("PASS production core lifecycle and 10 platform defect controls")


if __name__ == "__main__":
    main()
