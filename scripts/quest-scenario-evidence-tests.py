#!/usr/bin/env python3
"""Run bounded Quest scenario evidence against real Unity cameras and meshes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from town_run_retention import cleanup_completed_run


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--control", choices=("desktop-gate", "launch-cap", "gpu-transform", "renderer-state"))
    parser.add_argument("--gpu", action="store_true")
    args = parser.parse_args()
    base = ROOT / ".planning/debug/quest-scenario-evidence"
    base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=base))
    project = run / "unity"
    editor = project / "Assets/Editor"
    editor.mkdir(parents=True)
    path = ROOT / "src/GloomhavenVR/Rig/VRRigDriver.QuestScenario.cs"
    source = path.read_text()
    if args.control == "desktop-gate":
        source = source.replace("!QuestStandalonePlatform.Enabled || !VRLog.Wants", "!VRLog.Wants", 1)
    elif args.control == "launch-cap":
        source = source.replace("|| _questScenarioRecords >= 8 || _questScenarioSamples >= 2", "|| _questScenarioSamples >= 2", 1)
        source = source.replace("if (_questScenarioRecords >= 8) return;", "/* injected launch cap removal */", 1)
    elif args.control == "renderer-state":
        source = source.replace("if (renderer.enabled) componentEnabled++;", "if (on) componentEnabled++;", 1)
    (editor / "Scenario.cs").write_text("#nullable enable\n" + source.replace("namespace GloomhavenVR.Rig;", "namespace GloomhavenVR.Rig {") + "\n}\n")
    gpu_source = (ROOT / "src/GloomhavenVR/Rig/VRRigDriver.QuestScenarioGpu.cs").read_text()
    if args.control == "gpu-transform":
        gpu_source = gpu_source.replace("renderer.localToWorldMatrix, material", "Matrix4x4.Translate(Vector3.one * 1000f) * renderer.localToWorldMatrix, material", 1)
    (editor / "ScenarioGpu.cs").write_text("#nullable enable\n" + gpu_source.replace("namespace GloomhavenVR.Rig;", "namespace GloomhavenVR.Rig {") + "\n}\n")
    (project / "Assets/HeadUnlit.shader").write_bytes((ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/Head/HeadUnlit.shader").read_bytes())
    (editor / "Probe.cs").write_bytes((ROOT / "tests/QuestScenarioEvidence.Tests/Probe.cs").read_bytes())
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    command = ["/home/claw/unity-2021.3.5/Editor/Unity", "-batchmode", "-force-vulkan" if args.gpu else "-nographics", "-projectPath", str(project),
               "-executeMethod", "QuestScenarioEvidenceProbe.RunGpu" if args.gpu else "QuestScenarioEvidenceProbe.Run", "-logFile", str(run / "unity.log")]
    if args.gpu and not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    log = run / "runner.log"
    log.write_text("Quest scenario evidence: " + str(run) + "\n")
    print(log.read_text(), end="", flush=True)
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    code = 1
    try:
        code = process.wait(timeout=180)
        if args.control:
            expected = {"desktop-gate": "Desktop does not observe native scene", "launch-cap": "Recreated rig cannot reset launch cap",
                        "gpu-transform": "Actual unreadable native mesh produces visible GPU pixels",
                        "renderer-state": "Inactive and component-disabled native renderers are counted independently"}[args.control]
            if code == 0 or expected not in (run / "unity.log").read_text():
                raise AssertionError("Native negative control was not rejected by its expected assertion")
            (run / "control.json").write_text(json.dumps({"control": args.control, "rejected": True, "expected": expected, "processExit": code}, indent=2) + "\n")
            print("PASS native negative control rejected: " + args.control, flush=True)
            return
        if code:
            raise RuntimeError("Native scenario evidence fixture failed: " + str(run / "unity.log"))
        report = json.loads((project / "ProbeOutput/result.json").read_text())
        if report.get("assertions", 0) < (9 if args.gpu else 20):
            raise AssertionError("Incomplete scenario evidence fixture")
        (run / "source-hashes.json").write_text(json.dumps({"source": hashlib.sha256(source.encode()).hexdigest(),
            "probe": hashlib.sha256((ROOT / "tests/QuestScenarioEvidence.Tests/Probe.cs").read_bytes()).hexdigest(),
            "gpuSource": hashlib.sha256(gpu_source.encode()).hexdigest(),
            "graphics": "vulkan" if args.gpu else "null",
            "assertions": report["assertions"], "hardwareVerified": False}, indent=2) + "\n")
        print("PASS native scenario evidence: assertions=" + str(report["assertions"]), flush=True)
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        cleanup = cleanup_completed_run("quest-scenario-evidence", code, log, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")


if __name__ == "__main__":
    main()
