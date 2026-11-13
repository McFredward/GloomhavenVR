#!/usr/bin/env python3
"""Probe compact OpenXR preparation in the pinned real Unity Editor.

Uses package sources only, never game inputs. Both synthetic Android pipeline
calls stop at the first preprocessor, before native code or APK output. The
negative control reproduces the supplied package exception with isBuildingPlayer
true; production early admission then lets that exact package callback succeed.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]


def stop_owned(process):
    if os.name != "nt":
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
    elif process.poll() is None:
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True)
    if process.poll() is None:
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            if os.name != "nt":
                os.killpg(process.pid, signal.SIGKILL)
            else:
                process.kill()
            process.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--package-project", type=Path, required=True,
        help="Existing package-only project used as read-only pinned package source.")
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=480)
    args = parser.parse_args()
    args.evidence.mkdir(parents=True, exist_ok=True)
    run = ROOT / ".planning/debug/compact-openxr" / ("run-" + uuid.uuid4().hex[:8])
    project = run / "project"
    editor = project / "Assets/Editor"
    editor.mkdir(parents=True)
    (project / "ProjectSettings").mkdir()
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    (project / "Packages").mkdir()
    dependencies = {}
    borrowed = {}
    lock = json.loads((args.package_project / "Packages/packages-lock.json").read_text())
    needed = {"com.unity.xr.openxr", "com.unity.xr.management", "com.unity.xr.core-utils",
        "com.unity.xr.legacyinputhelpers", "com.unity.inputsystem"}
    for name, row in lock["dependencies"].items():
        if row["source"] == "builtin":
            dependencies[name] = row["version"]
        elif name in needed:
            package = args.package_project / "Library/PackageCache" / (name + "@" + row["version"])
            if not package.is_dir():
                raise RuntimeError("Missing read-only pinned package: " + str(package))
            dependencies[name] = "file:" + package.resolve().as_posix()
            borrowed[name] = {"path": str(package), "packageManifestSha256": hashlib.sha256((package / "package.json").read_bytes()).hexdigest()}
    assert needed <= dependencies.keys()
    (project / "Packages/manifest.json").write_text(json.dumps({"dependencies": dependencies}, indent=2) + "\n")
    production = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs"
    fixture = ROOT / "tests/quest-builder/fixtures/CompactOpenXRPreparationFixture.cs"
    shutil.copyfile(production, editor / production.name)
    shutil.copyfile(fixture, editor / fixture.name)
    result_path = run / "result.json"
    log = run / "run.log"
    log.write_text("evidence: " + str(run) + "\n")
    summary = {"success": False, "run": str(run), "scope": "Real Unity2021.3.5f1 OpenXR1.13 package/preprocessor boundary; no original game assets, native compilation or APK output",
        "borrowedPackages": borrowed, "productionSha256": hashlib.sha256(production.read_bytes()).hexdigest()}
    process = None
    code = 1
    start = time.monotonic()
    try:
        env = dict(os.environ, GHVR_QUEST_OPENXR_FIXTURE_RESULT=str(result_path))
        command = [str(args.unity), "-batchmode", "-nographics", "-quit", "-buildTarget", "Android", "-projectPath", str(project),
            "-executeMethod", "CompactOpenXRPreparationFixture.Run", "-logFile", str(run / "unity.log")]
        summary["command"] = command
        with (run / "launcher.log").open("w") as output:
            process = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, env=env, start_new_session=os.name != "nt")
            try:
                summary["unityExitCode"] = process.wait(timeout=args.timeout)
            finally:
                stop_owned(process)
        if result_path.is_file():
            summary["result"] = json.loads(result_path.read_text())
        if summary["unityExitCode"] != 0 or not summary.get("result", {}).get("success"):
            raise RuntimeError("Actual OpenXR preprocessor fixture failed; inspect retained logs/result.")
        unity_log = (run / "unity.log").read_text(errors="replace")
        summary["noNativeCompilerEntered"] = "il2cpp.exe" not in unity_log and "Invoking il2cpp" not in unity_log
        if not summary["noNativeCompilerEntered"]:
            raise RuntimeError("Fixture unexpectedly entered native compilation.")
        summary["borrowedManifestsUnchanged"] = all(hashlib.sha256((Path(row["path"]) / "package.json").read_bytes()).hexdigest()
            == row["packageManifestSha256"] for row in borrowed.values())
        assert summary["borrowedManifestsUnchanged"]
        summary["success"] = True
        code = 0
    except BaseException as error:
        summary["error"] = repr(error)
    finally:
        if process is not None:
            stop_owned(process)
        summary["elapsedSeconds"] = time.monotonic() - start
        spec = importlib.util.spec_from_file_location("fixture_retention", ROOT / "scripts/town_run_retention.py")
        retention = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(retention)
        summary["cleanup"] = retention.cleanup_completed_run("compact-openxr-preparation", code, log, ROOT, process_reaped=True)
        (run / "validation.json").write_text(json.dumps(summary, indent=2) + "\n")
        # Keep compact source/manifests/results, never copy compiler caches or borrowed package trees.
        shutil.copytree(run, args.evidence / run.name, dirs_exist_ok=True,
            ignore=shutil.ignore_patterns(".cache-cleanup.lock", "Library", "Temp", "Obj", "Logs", "ProbeOutput"))
        (args.evidence / "validation.json").write_text(json.dumps(summary, indent=2) + "\n")
        print(json.dumps(summary, indent=2))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
