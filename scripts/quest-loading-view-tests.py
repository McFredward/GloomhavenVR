#!/usr/bin/env python3
"""Run actual Quest loading view/policy logic with bounded production defect controls."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    paths = {
        "QuestLoadingView.cs": root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestLoadingView.cs",
        "QuestStandalonePlatform.cs": root / "src/GloomhavenVR/Core/QuestStandalonePlatform.cs",
        "Loc/QuestText.cs": root / "src/GloomhavenVR/Core/Loc/QuestText.cs",
    }
    original = {name: path.read_text() for name, path in paths.items()}
    # Bind the separately compiled pure policy to the real screen visibility
    # authority. Original Hide/ReleaseStack remains its sole restore owner.
    flat = root / "src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.1.Core.cs"
    lifecycle = root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestGameModLifecycle.cs"
    assert flat.read_text().count("bool want = !questPreparation && !preMenu && WantVisible();") == 1
    assert flat.read_text().count("QuestStandalonePlatform.SuppressStartupScreen(SceneManager.GetActiveScene().name)") == 1
    life = lifecycle.read_text()
    for field in ("OverallProcessedBytes", "OverallTotalBytes", "FileIndex", "FileCount"):
        assert "progress." + field in life, "actual lifecycle packet binding missing: " + field
    assert "loadingView.UpdateProgress(" in life and "QuestLoadingView.Basename(progress.File)" in life
    cases = [("production", original, "")]
    controls = (
        ("unobserved-gate-completed", "QuestLoadingView.cs",
         "return Math.Min(nextBoundary, (int)decimal.Floor(100m * (completed + fraction) / totalSteps));",
         "return (int)decimal.Floor(100m * (completed + fraction) / totalSteps);", "byte packet completed unobserved gate"),
        ("phase-rewinds-total", "QuestLoadingView.cs",
         "lastPreparationPercent = Math.Max(lastPreparationPercent, percent);",
         "lastPreparationPercent = percent;", "file reset rewound persistent total"),
        ("storage-path-displayed", "QuestLoadingView.cs",
         "int start = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\\\')) + 1;",
         "int start = 0;", "Windows storage path leaked"),
        ("filename-is-markup", "QuestLoadingView.cs",
         "text.supportRichText = false;", "text.supportRichText = true;", "original filename became UI markup"),
        ("original-scenes-suppressed", "QuestStandalonePlatform.cs",
         'Enabled && string.Equals(activeSceneName, "QuestOriginalStartup", StringComparison.Ordinal);',
         "Enabled;", "genuine original scene suppressed"),
        ("synthetic-scene-allowed", "QuestStandalonePlatform.cs",
         'Enabled && string.Equals(activeSceneName, "QuestOriginalStartup", StringComparison.Ordinal);',
         "false;", "synthetic Quest scene allowed empty capture"),
    )
    for name, target, before, after, expected in controls:
        if original[target].count(before) != 1:
            raise RuntimeError("Production mutation binding drift: " + name)
        sources = dict(original)
        sources[target] = sources[target].replace(before, after, 1)
        cases.append((name, sources, expected))
    output = root / ".planning/debug/quest-loading-view"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    evidence = {"schema": 1, "scope": "production UI/policy controls; Unity/XR layout and headset appearance unverified",
                "sources": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in paths.items()},
                "flatScreenBindingSha256": hashlib.sha256(flat.read_bytes()).hexdigest(),
                "lifecycleBindingSha256": hashlib.sha256(lifecycle.read_bytes()).hexdigest(), "cases": []}
    for name, sources, expected in cases:
        case = run / name
        case.mkdir()
        harness = case / "fixture"
        shutil.copytree(root / "tests/QuestLoadingView.Tests", harness, ignore=shutil.ignore_patterns("bin", "obj"))
        for filename, source in sources.items():
            destination = case / filename
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(source)
        result = subprocess.run([dotnet, "run", "--project", str(harness / "QuestLoadingView.Tests.csproj"),
                                 "-c", "Release", "-p:QuestRuntimeSource=" + str(case), "-p:PlatformSource=" + str(case)],
                                text=True, capture_output=True, cwd=root)
        combined = result.stdout + result.stderr
        (case / "output.txt").write_text(combined)
        passed = result.returncode == 0 if not expected else result.returncode != 0 and expected in combined
        evidence["cases"].append({"name": name, "exitCode": result.returncode, "passed": passed,
                                  "expectedFailure": expected})
        if not passed:
            print(combined[-5000:])
            raise SystemExit("FAIL " + name + "; evidence " + str(case))
        print(name + ": " + ("PASS" if not expected else "defect rejected"), flush=True)
    (run / "proof.json").write_text(json.dumps(evidence, indent=2) + "\n")
    print("Quest loading view: 51 production checks and 6 defect controls passed; evidence " + str(run), flush=True)


if __name__ == "__main__":
    main()
