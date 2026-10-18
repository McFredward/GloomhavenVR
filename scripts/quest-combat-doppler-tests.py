#!/usr/bin/env python3
"""Exercise production Quest combat Doppler scopes and reversible pooled-source state."""
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
    base = ROOT / ".planning/debug/quest-combat-doppler"; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=base.resolve()))
    log = run / "runner.log"; log.write_text("Combat Doppler evidence: " + str(run) + "\n")
    print(log.read_text().strip(), flush=True)
    source = (ROOT / "src/GloomhavenVR/Core/QuestCombatAudio.cs").read_text()
    (run / "source-sha256.json").write_text(json.dumps({"QuestCombatAudio.cs": hashlib.sha256(source.encode()).hexdigest()}, indent=2) + "\n")
    controls = (
        ("production", "", "", ""),
        ("native-doppler-retained", "source.dopplerLevel = 0f;", "source.dopplerLevel = 1f;", "before-first-buffer"),
        ("desktop-gate", "!QuestStandalonePlatform.Enabled ||", "false ||", "scope-gate"),
        ("ambience-gate", "source.loop ||", "false ||", "scope-gate"),
        ("pool-restore", "if (source.dopplerLevel == 0f) source.dopplerLevel = original.Doppler;", "if (source.dopplerLevel == 0f) source.dopplerLevel = 0f;", "pool-stop"),
        ("secondary-clip-write", "QuestCombatAudioBank.Prepare(owner.primaryAudioSource);", "QuestCombatAudioBank.Prepare(owner.primaryAudioSource); QuestCombatAudioBank.Prepare(owner.secondaryAudioSource);", "native-crossfade"),
    )
    results = []; code = 1; process = None; reaped = True
    try:
        for name, before, after, expected in controls:
            folder = run / name
            shutil.copytree(ROOT / "tests/QuestCombatDoppler.Tests", folder, ignore=shutil.ignore_patterns("bin", "obj"))
            if before: assert source.count(before) == 1, "Control binding drift: " + name
            (folder / "Combat.cs").write_text(source.replace(before, after) if before else source)
            process = subprocess.Popen([shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet"), "run", "--project", str(folder), "-c", "Release",
                "-p:CombatSource=" + str(folder / "Combat.cs"), "-p:UseSharedCompilation=false"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                text=True, start_new_session=True, env={**os.environ, "MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0"})
            reaped = False
            try:
                output, _ = process.communicate(timeout=90)
            finally:
                if process.poll() is None: process.kill(); process.wait()
                spec = importlib.util.spec_from_file_location("sampling_cleanup", ROOT / "scripts/quest-menu-sampling-tests.py")
                helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
                children = helper.reap_fixture_group(process.pid); reaped = True
                (folder / "process-cleanup.json").write_text(json.dumps(children, indent=2) + "\n")
            (folder / "console.log").write_text(output)
            passed = process.returncode == 0 if not expected else process.returncode != 0 and expected in output and "Build FAILED" not in output
            results.append({"name": name, "exitCode": process.returncode, "expected": expected, "passed": passed})
            (run / "results.json").write_text(json.dumps(results, indent=2) + "\n")
            if not passed: raise RuntimeError("FAIL " + name + ": " + str(folder / "console.log"))
            print(output.strip() if not expected else "PASS rejected " + name + " at " + expected, flush=True)
        code = 0
    finally:
        receipt = cleanup_completed_run("quest-combat-doppler", code, log, ROOT, process_reaped=reaped)
        (run / "cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return code


if __name__ == "__main__": raise SystemExit(main())
