#!/usr/bin/env python3
"""Run production startup coroutine/file delivery with bounded component seams.

This is an ordering and managed-IO fixture, not a Unity/XR/Android player proof.
The separate startup-log fixture executes the actual bounded logging sink.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime-source", type=Path, default=root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime")
    args = parser.parse_args()
    names = ("QuestGameBootstrap.cs", "QuestGameContent.cs", "QuestGameArchiveDelivery.cs")
    sources = {name: (args.runtime_source / name).read_text() for name in names}
    bootstrap = sources["QuestGameBootstrap.cs"]
    mutations = (
        ("late-loading-view", "modLifecycle.PrepareStartupView();", "// Missing early loading view", "early-view"),
        ("mod-before-files", 'yield return EnsureContent(modManifest, modRoot, "quest-mod-content.zip", "mod-content");',
         "yield return null;", "mod-before-content"),
        ("missing-mod-error-gate", 'yield return EnsureContent(modManifest, modRoot, "quest-mod-content.zip", "mod-content");\n            if (State == "failed") yield break;',
         'yield return EnsureContent(modManifest, modRoot, "quest-mod-content.zip", "mod-content");', "mod-before-content"),
        ("duplicate-mod-owner", "yield return modLifecycle.Activate(modRoot);",
         "yield return modLifecycle.Activate(modRoot);\n            yield return modLifecycle.Activate(modRoot);", "one-owner"),
        ("synchronous-main-hash", "Task.Run(() => QuestGameContent.IsReady(manifest, root, ReportContentProgress))",
         "Task.FromResult(QuestGameContent.IsReady(manifest, root, ReportContentProgress))", "main-hash"),
        ("worker-unity-path", "void ReportContentProgress(QuestGameContentProgress progress)\n        {",
         "void ReportContentProgress(QuestGameContentProgress progress)\n        {\n            string forbiddenWorkerPath = Application.persistentDataPath;", "worker-api"),
        ("wrong-apk-source", "sourceIsApk ? Application.dataPath :", "sourceIsApk ? Application.streamingAssetsPath :", "startup-completes"),
        ("false-main-heartbeat", "mainThreadFrames++;", "// Missing main thread heartbeat", "main-frame-state"),
        ("progress-log-io-blocks-load", "catch (IOException) { contentLogWriteFailed = true; }",
         "catch (IOException) { throw; }", "startup-completes"),
        ("wait-blocks-main-thread", "while (!delivery.IsCompleted) yield return null;",
         "while (!delivery.IsCompleted) { Thread.Sleep(1); }", "yield-main-pump"),
    )
    cases = [("production", bootstrap, ""),
             ("commented-defects-are-inert", "/* Missing early loading view; mod-before-content;\n"
              "while (!delivery.IsCompleted) { Thread.Sleep(1); }\n"
              "string forbiddenWorkerPath = Application.persistentDataPath; */\n" + bootstrap, "")]
    for name, before, after, expected in mutations:
        if bootstrap.count(before) != 1:
            raise RuntimeError("Mutation binding drift: " + name)
        cases.append((name, bootstrap.replace(before, after), expected))
    output = root / ".planning/debug/quest-startup-loading"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    evidence = {"runtimeSource": str(args.runtime_source.resolve()),
                "sources": {name: hashlib.sha256(source.encode()).hexdigest() for name, source in sources.items()},
                "boundary": "actual Bootstrap/content/delivery; Unity, logger and downstream-component seams; no native rendering or throughput proof",
                "cases": []}
    print("Quest startup loading evidence: " + str(run), flush=True)
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    for name, modified, expected in cases:
        case = run / name
        case.mkdir()
        harness = case / "fixture"
        shutil.copytree(root / "tests/QuestStartupLoading.Tests", harness, ignore=shutil.ignore_patterns("bin", "obj"))
        for filename, source in sources.items():
            (case / filename).write_text(modified if filename == "QuestGameBootstrap.cs" else source)
        result = subprocess.run([dotnet, "run", "--project", str(harness / "QuestStartupLoading.Tests.csproj"),
                                 "--configuration", "Release", "--property:RuntimeSource=" + str(case), "--", str(case / "files")],
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=90)
        (case / "console.log").write_text(result.stdout)
        passed = (result.returncode == 0 and "PASS Quest startup loading:" in result.stdout) if not expected else (result.returncode != 0 and expected in result.stdout)
        evidence["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
        (run / "results.json").write_text(json.dumps(evidence, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        if not expected:
            print(next(line for line in result.stdout.splitlines() if line.startswith("PASS Quest startup loading:")), flush=True)
        else:
            print("PASS rejected " + name + " at " + expected, flush=True)
    print("PASS production startup coroutine and " + str(len(mutations)) + " defect controls")


if __name__ == "__main__":
    main()
