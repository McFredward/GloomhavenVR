#!/usr/bin/env python3
"""Run linked production tracking observations, with no Unity player or GPU build."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
import uuid


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    checkout = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    source_path = checkout / "src/GloomhavenVR/Rig/VRRigDriver.QuestTracking.cs"
    source = source_path.read_text()
    head = (source_path.parent / "VRRigDriver.HeadCamera.cs").read_text()
    driver = (source_path.parent / "VRRigDriver.cs").read_text()
    assert head.count("StartQuestTrackingEvidence();") == 1
    assert driver.count("NoteQuestTrackingScene(e.Scene.name);") == 1
    assert driver.count("StopQuestTrackingEvidence();") == 1
    teardown = driver[driver.index("    private void TearDownRig("):]
    assert teardown.index("StopQuestTrackingEvidence();") < teardown.index("Destroy(_poseDriver)")
    assert "Debug.Log(text.ToString());" in source and '"[Quest startup] presentation tracking ' in source
    controls = [
        ("production", None, None, None),
        ("game-debug-facade", "UnityEngine.Debug.Log(text.ToString())", "global::Debug.Log(text.ToString())", "first actual head render sample"),
        ("desktop-subscribed", "if (!QuestStandalonePlatform.Enabled)\n            return;", "if (QuestStandalonePlatform.Enabled && !QuestStandalonePlatform.Enabled)\n            return;", "desktop has no diagnostic subscription or queries"),
        ("normal-level-queried", " || !VRLog.Wants(VRLogLevel.Debug)", "", "normal level performs no device clock matrix or transform queries"),
        ("right-eye-aliased", "rendering.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)", "rendering.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)", "rightProjectionSlopes=(-0.5500,0.4500,-0.3750,0.1250)"),
        ("sample-cap-removed", " || _questTrackingSamples >= QuestTrackingSampleLimit", "", "six sample lifetime cap survives scene changes"),
        ("device-space-corrupted", "Vector3.Distance(_rigRoot!.transform.InverseTransformPoint(observed.position), position)", "Vector3.Distance(observed.position, position)", "headDevicePositionDelta=0.0000"),
    ]
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    spec = importlib.util.spec_from_file_location("tracking_retention", checkout / "scripts/town_run_retention.py")
    retention = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(retention)
    result = {"sourceSha256": hashlib.sha256(source_path.read_bytes()).hexdigest(), "cases": [],
              "scope": "Linked production Debug observations and callbacks; managed device/camera seams, no hardware picture claim."}
    for name, before, after, expected in controls:
        run = checkout / ".planning/debug/quest-tracking" / ("run-" + uuid.uuid4().hex[:8])
        fixture = run / "fixture"
        shutil.copytree(Path(__file__).parent, fixture, ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
        sources = run / "source"
        sources.mkdir()
        actual = source
        if before is not None:
            assert before in actual, "mutation source drift: " + name
            actual = actual.replace(before, after, 1)
        (sources / source_path.name).write_text(actual)
        log = run / "console.log"
        started = time.monotonic()
        with log.open("w") as stream:
            stream.write("evidence: " + str(run) + "\n")
            stream.flush()
            process = subprocess.Popen([dotnet, "run", "--project", str(fixture / "QuestTrackingEvidence.Tests.csproj"),
                                        "-c", "Release", "-p:RuntimeSource=" + str(sources)], cwd=checkout,
                                       stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = process.wait(timeout=120)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait()
                code = 124
        # Fixture output may itself mention an "evidence:" field. Give retention
        # one explicit owned-run reference instead of interpreting arbitrary output.
        cleanup_log = run / "cleanup.log"
        cleanup_log.write_text("evidence: " + str(run) + "\nexitCode=" + str(code) + "\n")
        cleanup = retention.cleanup_completed_run("quest-tracking", code, cleanup_log, checkout, process_reaped=True)
        text = log.read_text()
        (output / (name + ".log")).write_text(text)
        passed = "error CS" not in text and "error MSB" not in text and (
            code == 0 and "PASS Quest tracking evidence:" in text if expected is None
            else code != 0 and "FAIL " + expected in text)
        result["cases"].append({"name": name, "exitCode": code, "passed": passed, "expected": expected,
                                "elapsedSeconds": round(time.monotonic() - started, 3), "cleanup": cleanup})
        (output / "validation.json").write_text(json.dumps(result, indent=2) + "\n")
        print(("PASS " if passed else "FAIL ") + name, flush=True)
        if not passed:
            print(text[-4000:])
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
