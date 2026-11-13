#!/usr/bin/env python3
"""Exercise original-channel clip substitution and conservative source/pool ownership."""
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from town_run_retention import cleanup_completed_run


def main():
    if sys.platform.startswith("linux"):
        assert ctypes.CDLL(None).prctl(36, 1, 0, 0, 0) == 0
    base = ROOT / ".planning/debug/quest-combat-bank"
    base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=base.resolve()))
    log = run / "runner.log"
    log.write_text("Native audio bank evidence: " + str(run) + "\n")
    print(log.read_text().strip(), flush=True)
    source = (ROOT / "src/GloomhavenVR/Core/QuestCombatAudioBank.cs").read_text()
    controls = (
        ("production", "", "", ""),
        ("desktop-write", "!QuestStandalonePlatform.Enabled", "false", "disabled-scope"),
        ("same-name-foreign-rate", "original.frequency != entry.Rate", "false", "exact-native-identity"),
        ("bad-native-bank", "corrected.channels != entry.Channels", "false", "bad-bank-original-retained"),
        ("foreign-owner", "if (source.clip == lease.Applied)", "if (true)", "retarget-lease-owner"),
        ("pool-not-restored", "source.clip = lease.Original;", "source.clip = lease.Applied;", "pool-restoration"),
        ("stale-lease", "        Restore(source);", "        // Retained stale pool lease", "native-retarget-without-stop"),
        ("lost-start-offset", "if (position > 0 && position < corrected.samples) source.timeSamples = position;", ";", "playback-properties"),
    )
    results = []
    code, process, reaped = 1, None, True
    try:
        for name, before, after, expected in controls:
            folder = run / name
            shutil.copytree(ROOT / "tests/QuestCombatAudioBank.Tests", folder, ignore=shutil.ignore_patterns("bin", "obj"))
            if before:
                assert source.count(before) == 1, name
            (folder / "Bank.cs").write_text(source.replace(before, after, 1) if before else source)
            process = subprocess.Popen([shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet"), "run", "--project", str(folder), "-c", "Release",
                "-p:BankSource=" + str(folder / "Bank.cs"), "-p:UseSharedCompilation=false"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, start_new_session=True, env={**os.environ, "MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0"})
            reaped = False
            try:
                output, _ = process.communicate(timeout=90)
            finally:
                if process.poll() is None:
                    process.kill(); process.wait()
                spec = importlib.util.spec_from_file_location("sampling_cleanup", ROOT / "scripts/quest-menu-sampling-tests.py")
                helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
                receipt = helper.reap_fixture_group(process.pid); reaped = True
                (folder / "process-cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")
            (folder / "console.log").write_text(output)
            passed = process.returncode == 0 if not expected else process.returncode != 0 and expected in output and "Build FAILED" not in output
            results.append({"name": name, "exitCode": process.returncode, "expected": expected, "passed": passed})
            (run / "results.json").write_text(json.dumps({"sourceSha256": hashlib.sha256(source.encode()).hexdigest(), "cases": results}, indent=2) + "\n")
            if not passed:
                raise RuntimeError("FAIL " + name + ": " + str(folder / "console.log"))
            print(output.strip() if not expected else "PASS rejected " + name + " at " + expected, flush=True)
        code = 0
    finally:
        receipt = cleanup_completed_run("quest-combat-bank", code, log, ROOT, process_reaped=reaped)
        (run / "cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
