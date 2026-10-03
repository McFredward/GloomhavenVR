#!/usr/bin/env python3
"""Exercise production startup callback/file persistence, with explicit defect controls."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    runtime = root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime"
    logger = (runtime / "QuestGameStartupLog.cs").read_text()
    bootstrap = (runtime / "QuestGameBootstrap.cs").read_text()
    output = root / ".planning/debug/quest-startup-log"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    harness = run / "fixture"
    shutil.copytree(root / "tests/QuestStartupLog.Tests", harness, ignore=shutil.ignore_patterns("bin", "obj"))
    cases = [("production", logger, bootstrap, "")]
    mutations = (
        ("main-thread-only", "bootstrap", "Application.logMessageReceivedThreaded", "Application.logMessageReceived", 2, "worker-callback"),
        ("duplicate-budget", "logger", "if (errorsSeen.Contains(key)) return;", "if (errorsSeen.Contains(key)) { Volatile.Write(ref originalErrors, originalErrors + 1); return; }", 1, "distinct-budget"),
        ("same-message-lost-stack", "logger", 'string key = clippedMessage + "\\n" + clippedStack;', "string key = clippedMessage;", 1, "stack-identity"),
        ("lifecycle-consumes-error-reserve", "logger", "if (errorsCapped) return;", "if (errorsCapped || capped) return;", 1, "lifecycle-reserve"),
        ("original-stack-discarded", "logger", 'Line(clippedMessage, clippedStack.Length == 0 ? "[stack not supplied by Unity]" : clippedStack)', 'Line(clippedMessage, "")', 1, "first-stack"),
        ("unbounded-utf8-errors", "logger", " || errorBytes + length + Utf8.GetByteCount(ErrorLimit) > ErrorBytes", "", 1, "utf8-bound"),
        ("spent-error-before-io", "logger", "File.AppendAllText(path, line, Utf8); errorBytes += length; errorsSeen.Add(key);", "errorsSeen.Add(key); File.AppendAllText(path, line, Utf8); errorBytes += length;", 1, "io-retry"),
        ("threaded-listener-leak", "bootstrap", "Application.logMessageReceivedThreaded -= CaptureLog;", "// Missing threaded unsubscription defect\n", 1, "unsubscribe"),
    )
    for name, kind, before, after, count, expected in mutations:
        source = logger if kind == "logger" else bootstrap
        if source.count(before) != count:
            raise RuntimeError("Mutation binding drift: " + name)
        replacement = source.replace(before, after)
        cases.append((name, replacement if kind == "logger" else logger,
                      replacement if kind == "bootstrap" else bootstrap, expected))
    evidence = {"sources": {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                             for path in (runtime / "QuestGameStartupLog.cs", runtime / "QuestGameBootstrap.cs")}, "cases": []}
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    print("Quest startup logging evidence: " + str(run), flush=True)
    for name, logger_source, bootstrap_source, expected in cases:
        case = run / name
        case.mkdir()
        case_harness = case / "fixture"
        shutil.copytree(harness, case_harness)
        (case / "QuestGameStartupLog.cs").write_text(logger_source)
        (case / "QuestGameBootstrap.cs").write_text(bootstrap_source)
        result = subprocess.run([dotnet, "run", "--project", str(case_harness / "QuestStartupLog.Tests.csproj"),
                                 "--configuration", "Release", "--property:RuntimeSource=" + str(case),
                                 "--", str(case / "files")], text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        passed = (result.returncode == 0 and "PASS Quest startup logging:" in result.stdout) if not expected else (result.returncode != 0 and expected in result.stdout)
        evidence["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
        (run / "results.json").write_text(json.dumps(evidence, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        if not expected:
            print(next(line for line in result.stdout.splitlines() if line.startswith("PASS Quest startup logging:")), flush=True)
        else:
            print("PASS rejected " + name + " at " + expected, flush=True)
    print("PASS production startup callback and 8 diagnostic defect controls")


if __name__ == "__main__":
    main()
