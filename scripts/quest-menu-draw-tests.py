#!/usr/bin/env python3
"""Exercise the real bounded menu-consumer GPU draw, without an XR surrogate."""
import hashlib
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
    output = ROOT / ".planning/debug/quest-menu-draw"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    project = run / "unity"
    editor = project / "Assets/Editor"
    resources = project / "Assets/Resources"
    editor.mkdir(parents=True)
    resources.mkdir(parents=True)
    source = ROOT / "src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.7.QuestDrawProbe.cs"
    (editor / "Consumer.cs").write_text(source.read_text().replace(
        "namespace GloomhavenVR.WorldUI;", "namespace GloomhavenVR.WorldUI {") + "\n}\n")
    shutil.copyfile(ROOT / "tests/QuestMenuDraw.Tests/Probe.cs", editor / "Probe.cs")
    shutil.copyfile(ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader", resources / "Overlay.shader")
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    graphics = os.environ.get("QUEST_MENU_TEST_GRAPHICS_API", "vulkan")
    if graphics not in ("glcore", "vulkan"):
        raise ValueError("Unknown native GPU backend")
    log = run / "runner.log"
    log.write_text("Quest menu consumer evidence: " + str(run) + "\n")
    print(log.read_text(), end="", flush=True)
    command = ["/home/claw/unity-2021.3.5/Editor/Unity", "-batchmode", "-force-" + graphics,
        "-projectPath", str(project), "-executeMethod", "QuestMenuDrawProbe.Run", "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    code = 1
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    try:
        code = process.wait(timeout=180)
        if code:
            raise RuntimeError("Native menu consumer fixture failed: " + str(run / "unity.log"))
        report = json.loads((project / "ProbeOutput/results.json").read_text())
        assert report["comparisons"] == 6 and report["stateComparisons"] == 2 and report["defectControls"] == 2 and not report["hardwareVerified"]
        (run / "source-hashes.json").write_text(json.dumps({"consumer": hashlib.sha256(source.read_bytes()).hexdigest(),
            "probe": hashlib.sha256((ROOT / "tests/QuestMenuDraw.Tests/Probe.cs").read_bytes()).hexdigest(),
            "graphics": graphics, "hardwareVerified": False}, indent=2) + "\n")
        print("PASS Quest menu consumer GPU draw: 6 RGBA + 2 foreground state comparisons, 2 live controls, " + graphics)
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        cleanup = cleanup_completed_run("quest-menu-draw", code, log, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")


if __name__ == "__main__":
    main()
