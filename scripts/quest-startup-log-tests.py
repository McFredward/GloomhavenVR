#!/usr/bin/env python3
"""Exercise production startup callback/file persistence, with explicit defect controls."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from town_run_retention import cleanup_completed_run


def main():
    root = Path(__file__).resolve().parents[1]
    runtime = root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime"
    logger = (runtime / "QuestGameStartupLog.cs").read_text()
    bootstrap = (runtime / "QuestGameBootstrap.cs").read_text()
    frames = (runtime / "QuestFrameEvidence.cs").read_text()
    output = root / ".planning/debug/quest-startup-log"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    runner_log = run / "runner.log"
    runner_log.write_text("Quest startup logging evidence: " + str(run) + "\n")
    harness = run / "fixture"
    shutil.copytree(root / "tests/QuestStartupLog.Tests", harness, ignore=shutil.ignore_patterns("bin", "obj"))
    cases = [("production", logger, bootstrap, frames, "")]
    mutations = (
        ("startup-reserve-missing", "logger", "if (startupCapped) return;", "if (startupCapped || capped) return;", 1, "startup-reserve"),
        ("main-thread-only", "bootstrap", "Application.logMessageReceivedThreaded", "Application.logMessageReceived", 2, "worker-callback"),
        ("duplicate-budget", "logger", "if (errorsSeen.Contains(key)) return;", "if (errorsSeen.Contains(key)) { Volatile.Write(ref originalErrors, originalErrors + 1); return; }", 1, "distinct-budget"),
        ("same-message-lost-stack", "logger", 'string key = clippedMessage + "\\n" + clippedStack;', "string key = clippedMessage;", 1, "stack-identity"),
        ("lifecycle-consumes-error-reserve", "logger", "if (errorsCapped) return;", "if (errorsCapped || capped) return;", 1, "lifecycle-reserve"),
        ("original-stack-discarded", "logger", 'Line(clippedMessage, clippedStack.Length == 0 ? "[stack not supplied by Unity]" : clippedStack)', 'Line(clippedMessage, "")', 1, "first-stack"),
        ("unbounded-utf8-errors", "logger", " || errorBytes + length + Utf8.GetByteCount(ErrorLimit) > ErrorBytes", "", 1, "utf8-bound"),
        ("spent-error-before-io", "logger", "File.AppendAllText(path, line, Utf8); errorBytes += length; errorsSeen.Add(key);", "errorsSeen.Add(key); File.AppendAllText(path, line, Utf8); errorBytes += length;", 1, "io-retry"),
        ("threaded-listener-leak", "bootstrap", "Application.logMessageReceivedThreaded -= CaptureLog;", "// Missing threaded unsubscription defect\n", 1, "unsubscribe"),
        ("presentation-spends-startup-reserve", "logger", "if (IsPresentationDetail(message))", "if (IsPresentationDetail(message) && message.Length < 0)", 1, "detail-reserve"),
        ("menu-spends-general-detail-reserve", "logger", "if (IsMenuPresentationDetail(message))", "if (IsMenuPresentationDetail(message) && message.Length < 0)", 1, "menu-partition"),
        ("primitive-spends-general-detail-reserve", "logger", '\n                || message.StartsWith("[Quest startup] presentation primitive-upload ", StringComparison.Ordinal)', "", 1, "menu-partition"),
        ("consumer-spends-general-detail-reserve", "logger", '\n                || message.StartsWith("[Quest startup] presentation menu-consumer-draw ", StringComparison.Ordinal)', "", 1, "menu-partition"),
        ("tutorial-spends-filled-detail-reserve", "logger", 'return message.StartsWith("[Quest startup] original movie state ", StringComparison.Ordinal)', 'return message.StartsWith("[Quest startup] scenario ", StringComparison.Ordinal) || message.StartsWith("[Quest startup] original movie state ", StringComparison.Ordinal)', 1, "tutorial-reserve"),
        ("spinner-spends-filled-detail-reserve", "logger", 'if (IsLoadingPresentationDetail(message))', 'if (IsLoadingPresentationDetail(message) && message.IndexOf(" presentation spinner ", StringComparison.Ordinal) < 0)', 1, "spinner-reserve"),
        ("loading-continuity-spends-filled-detail", "logger", '\n                || message.StartsWith("[Quest startup] presentation spinner-continuity ", StringComparison.Ordinal)', '', 1, "loading-window-reserve"),
        ("loading-owner-spends-filled-detail", "logger", '\n                || message.StartsWith("[Quest startup] presentation spinner-intro-owner ", StringComparison.Ordinal)', '', 1, "loading-window-reserve"),
        ("loading-final-spends-filled-detail", "logger", '\n                || message.StartsWith("[Quest startup] presentation spinner-final ", StringComparison.Ordinal)', '', 1, "loading-window-reserve"),
        ("loading-unbounded-utf8", "logger", ' || loadingDetailBytes + loadingLength + Utf8.GetByteCount(LoadingDetailLimit) > LoadingDetailBytes', '', 1, "all-lanes-byte-bound"),
        ("loading-unknown-stream-consumes-reserve", "logger", 'return message.StartsWith("[Quest startup] presentation spinner ", StringComparison.Ordinal)', 'return message.StartsWith("[Quest startup] presentation spinner", StringComparison.Ordinal)', 1, "loading-window-reserve"),
        ("menu-producer-spends-filled-detail", "logger", '\n                || message.StartsWith("[Quest startup] presentation menu-producer ", StringComparison.Ordinal)', '', 1, "menu-chain"),
        ("ambient-movie-spends-filled-detail", "logger", 'bool nativeMenuMovie = message.IndexOf(" Gloomhaven_unified/MainMenuVideo ", StringComparison.Ordinal) >= 0;', 'bool nativeMenuMovie = false;', 1, "menu-chain"),
        ("early-spikes-only", "frames", "int index = Snapshot.spikeCursor;", "if (Snapshot.spikeCount == Snapshot.spikeTimes.Length) return null; int index = Snapshot.spikeCursor;", 1, "frame-window"),
    )
    for name, kind, before, after, count, expected in mutations:
        source = {"logger": logger, "bootstrap": bootstrap, "frames": frames}[kind]
        if source.count(before) != count:
            raise RuntimeError("Mutation binding drift: " + name)
        replacement = source.replace(before, after)
        cases.append((name, replacement if kind == "logger" else logger,
                      replacement if kind == "bootstrap" else bootstrap,
                      replacement if kind == "frames" else frames, expected))
    evidence = {"sources": {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                             for path in (runtime / "QuestGameStartupLog.cs", runtime / "QuestGameBootstrap.cs", runtime / "QuestFrameEvidence.cs")}, "cases": []}
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    print("Quest startup logging evidence: " + str(run), flush=True)
    for name, logger_source, bootstrap_source, frame_source, expected in cases:
        case = run / name
        case.mkdir()
        case_harness = case / "fixture"
        shutil.copytree(harness, case_harness)
        (case / "QuestGameStartupLog.cs").write_text(logger_source)
        (case / "QuestGameBootstrap.cs").write_text(bootstrap_source)
        (case / "QuestFrameEvidence.cs").write_text(frame_source)
        shutil.copy2(runtime / "QuestSnapshotWriter.cs", case / "QuestSnapshotWriter.cs")
        result = None
        try:
            result = subprocess.run([dotnet, "run", "--project", str(case_harness / "QuestStartupLog.Tests.csproj"),
                                     "--configuration", "Release", "--property:RuntimeSource=" + str(case),
                                     "--property:UseSharedCompilation=false", "--disable-build-servers",
                                     "--", str(case / "files")], text=True, stdout=subprocess.PIPE,
                                    stderr=subprocess.STDOUT, timeout=120,
                                    env={**os.environ, "MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0"})
        finally:
            # The bounded console fixture disables all reusable build servers.
            # Its synchronous process has exited before cleanup is permitted;
            # a timeout has no such proof and leaves the evidence intact.
            receipt = cleanup_completed_run("quest-startup-log", result.returncode if result else 1,
                runner_log, root, process_reaped=result is not None)
            (case / "cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")
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
    print("PASS production startup callback and " + str(len(mutations)) + " diagnostic defect controls")


if __name__ == "__main__":
    main()
