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
        "QuestGameModLifecycle.cs": root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/QuestGameModLifecycle.cs",
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
    cases = [("production", original, "")]
    controls = (
        ("unobserved-handover-completed", "QuestLoadingView.cs",
         "Math.Max(0, Math.Min(99, percent))", "Math.Max(0, Math.Min(100, percent))", "unobserved handover claimed 100 percent"),
        ("phase-rewinds-total", "QuestLoadingView.cs",
         "overallPercent = Math.Max(overallPercent, bounded);",
         "overallPercent = bounded;", "single total rewound on file/phase change"),
        ("startup-label-is-markup", "QuestLoadingView.cs",
         "text.supportRichText = false;", "text.supportRichText = true;", "startup label became markup"),
        ("warm-start-shows-canvas", "QuestGameModLifecycle.cs",
         "startupAnchor.enabled = true;", "startupAnchor.enabled = true;\n            loadingView = QuestLoadingView.Create(startupAnchor, 31);", "warm preparation created a canvas"),
        ("plugin-retires-cold-view", "QuestGameModLifecycle.cs",
         "plugin = gameObject.AddComponent<Plugin>();", "StopLoadingView();\n                plugin = gameObject.AddComponent<Plugin>();", "plugin activation retired or recreated cold view"),
        ("real-head-not-adopted", "QuestGameModLifecycle.cs",
         "loadingView.Retarget(head, QuestStandalonePlatform.PresentationLayer);", "/* Missing same-owner retarget */", "real rig did not adopt same artwork"),
        ("head-handover-recreates-canvas", "QuestGameModLifecycle.cs",
         "loadingView.Retarget(head, QuestStandalonePlatform.PresentationLayer);", "loadingView = QuestLoadingView.Create(head, QuestStandalonePlatform.PresentationLayer);", "plugin activation retired or recreated cold view"),
        ("destroyed-xr-owner-not-recovered", "QuestGameModLifecycle.cs",
         "if (deliveryViewRequested && loadingView == null && QuestStandalonePlatform.HeadCamera != null) BeginDeliveryView();", "/* Missing genuinely destroyed owner recovery */", "destroyed XR child did not recover on observed head"),
        ("quiet-warm-failure-hidden", "QuestGameModLifecycle.cs",
         "if (state == \"failed\" && !deliveryViewRequested) BeginDeliveryView();", "/* Missing explicit failure presentation */", "quiet warm failure was hidden"),
        ("developer-file-rows-return", "QuestLoadingView.cs",
         'view.UpdateOverall("", 0);', 'AddText(panel.transform, layer, "Current file name", Vector2.zero, Vector2.zero, font, 23);\n            view.UpdateOverall("", 0);', "developer file/stage rows leaked into startup"),
        ("original-scenes-suppressed", "QuestStandalonePlatform.cs",
         'Enabled && string.Equals(activeSceneName, "QuestOriginalStartup", StringComparison.Ordinal);',
         "Enabled;", "genuine original scene suppressed"),
        ("synthetic-scene-allowed", "QuestStandalonePlatform.cs",
         'Enabled && string.Equals(activeSceneName, "QuestOriginalStartup", StringComparison.Ordinal);',
         "false;", "synthetic Quest scene captured empty anchor"),
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
    print("Quest loading view: actual lifecycle/artwork checks and " + str(len(controls)) + " defect controls passed; evidence " + str(run), flush=True)


if __name__ == "__main__":
    main()
