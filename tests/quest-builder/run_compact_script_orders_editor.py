#!/usr/bin/env python3
"""Exercise exact compact script-order preparation in the real pinned Editor.

Creates one tiny 128-class plugin and four conditionally disabled source assets,
runs the actual production helper twice, plus malformed/missing/conflicting
roster rejection. No original game data,
Android APK build, native code compilation or game asset conversion is involved.
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


def stop_owned_process_group(process):
    """Reap this fixture and only its own compiler/import children."""
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


def validate_progress(result):
    events = [json.loads(row) for row in result["progress"]]
    groups = []
    for event in events:
        assert event["schema"] == 1 and event["operation"] == "update-code", event
        assert event["total"] >= event["done"] >= 0 and event["unit"] == "tasks", event
        if not groups or event["phase"] != groups[-1][0]["phase"]:
            groups.append([])
        groups[-1].append(event)
    # The first two complete production preparations precede failure controls.
    expected = ["update-code-catalog", "update-code-roster", "update-code-orders"] * 2
    assert [group[0]["phase"] for group in groups[:6]] == expected, groups
    for group in groups[:6]:
        assert group[0]["done"] == 0 and group[0]["status"] == "progress", group
        assert group[-1]["done"] == group[-1]["total"] and group[-1]["status"] == "complete", group
        assert all(a["done"] <= b["done"] and a["total"] == b["total"] for a, b in zip(group, group[1:])), group
        assert len(group) >= 5, group  # Actual finite row counts, not heartbeat percentages.
    assert groups[1][-1]["total"] == 135 and groups[4][-1]["total"] == 135
    assert groups[2][-1]["total"] == 136  # 3 changes + one batch import +132 exact checks.
    assert groups[5][-1]["total"] == 132  # Retry only checks retained exact metadata.
    assert result["inactiveSourceIdentities"] and len(result["inactiveIdentities"]) == 4
    return {"events": len(events), "successfulCountedPhases": 6, "failureControls": 3,
        "inactiveSourceIdentities": result["inactiveIdentities"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--production-source", type=Path, help="Explicit helper revision for a negative control; defaults to this checkout.")
    parser.add_argument("--timeout", type=int, default=240)
    args = parser.parse_args()
    args.evidence.mkdir(parents=True, exist_ok=True)
    run = ROOT / ".planning/debug/compact-orders" / ("run-" + uuid.uuid4().hex[:8])
    project = run / "project"
    editor_sources = project / "Assets/Editor"
    plugins = project / "Assets/Plugins"
    editor_sources.mkdir(parents=True)
    plugins.mkdir(parents=True)
    (project / "ProjectSettings").mkdir()
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    (project / "Packages").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies": {}}\n')
    inactive = project / "Assets/Inactive"
    (inactive / "Other").mkdir(parents=True)
    (inactive / "Unity.InputSystem.ForUI.asmdef").write_text('{"name":"Unity.InputSystem.ForUI"}\n')
    (inactive / "InputSystemProvider.cs").write_text("#if UNITY_2023_2_OR_NEWER\nnamespace UnityEngine.InputSystem.Plugins.InputForUI { internal class InputSystemProvider {} }\n#endif\n")
    (inactive / "Other/InputSystemProvider.cs").write_text("#if UNITY_2023_2_OR_NEWER\nnamespace CompactOrderFixture.Other { internal class InputSystemProvider {} }\n#endif\n")
    (inactive / "NoNamespaceScript.cs").write_text("#if UNITY_2023_2_OR_NEWER\ninternal class NoNamespaceScript {}\n#endif\n")
    (inactive / "AbsentClass.cs").write_text("#if UNITY_2023_2_OR_NEWER\nnamespace CompactOrderFixture.Disabled { internal class AbsentClass {} }\n#endif\n")
    production = args.production_source or ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs"
    fixture = ROOT / "tests/quest-builder/fixtures/CompactScriptOrdersFixture.cs"
    shutil.copyfile(production, editor_sources / production.name)
    shutil.copyfile(fixture, editor_sources / fixture.name)
    source = run / "source/OrderFixture.cs"
    source.parent.mkdir()
    source.write_text("using UnityEngine; namespace OrderFixture {\n" + "\n".join(
        "public sealed class Script%03d : MonoBehaviour { public int value; }" % index for index in range(128)) + "\n}\n")
    data = args.unity.parent / "Data"
    mono = data / "MonoBleedingEdge/bin/mono"
    mcs = data / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"
    result_file = run / "result.json"
    log = run / "run.log"
    log.write_text("evidence: " + str(run) + "\n")
    summary = {"run": str(run), "scope": "Actual Unity2021.3.5f1 compact roster/order preparation; synthetic 128-class plugin and four inactive source assets; no game data or APK build", "success": False,
        "productionSource": str(production), "productionSha256": hashlib.sha256(production.read_bytes()).hexdigest()}
    process = None
    code = 1
    started = time.monotonic()
    try:
        compiled = subprocess.run([str(mono), str(mcs), "-target:library", "-out:" + str(plugins / "OrderFixture.dll"),
            "-r:" + str(data / "Managed/UnityEngine/UnityEngine.CoreModule.dll"), str(source)], capture_output=True, text=True)
        (run / "plugin-compile.log").write_text(compiled.stdout + compiled.stderr)
        if compiled.returncode:
            raise RuntimeError("Synthetic plugin compilation failed: " + compiled.stderr)
        env = dict(os.environ, GHVR_QUEST_ORDER_FIXTURE_RESULT=str(result_file))
        command = [str(args.unity), "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
            "-executeMethod", "CompactScriptOrdersFixture.Run", "-logFile", str(run / "unity.log")]
        summary["command"] = command
        with (run / "launcher.log").open("w") as output:
            process = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, env=env, start_new_session=os.name != "nt")
            try:
                observed_code = process.wait(timeout=args.timeout)
            finally:
                stop_owned_process_group(process)
        summary["unityExitCode"] = observed_code
        if result_file.is_file():
            summary["result"] = json.loads(result_file.read_text())
        if observed_code != 0 or not summary.get("result", {}).get("success"):
            raise RuntimeError("Actual Editor fixture failed; see retained Unity log and result.")
        summary["progressValidation"] = validate_progress(summary["result"])
        summary["success"] = True
        code = 0
    except BaseException as error:
        summary["error"] = repr(error)
    finally:
        if process is not None:
            stop_owned_process_group(process)
        summary["elapsedSeconds"] = time.monotonic() - started
        spec = importlib.util.spec_from_file_location("fixture_retention", ROOT / "scripts/town_run_retention.py")
        retention = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(retention)
        summary["cleanup"] = retention.cleanup_completed_run("compact-script-orders", code, log, ROOT, process_reaped=True)
        (run / "validation.json").write_text(json.dumps(summary, indent=2) + "\n")
        shutil.copytree(run, args.evidence / run.name, dirs_exist_ok=True,
            ignore=shutil.ignore_patterns(".cache-cleanup.lock", "*.dll", "*.dll.meta"))
        (args.evidence / "validation.json").write_text(json.dumps(summary, indent=2) + "\n")
        print(json.dumps(summary, indent=2))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
