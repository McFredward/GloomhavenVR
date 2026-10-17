#!/usr/bin/env python3
"""Characterize production pointer material on native and explicit sphere GPU meshes."""
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
    output = ROOT / ".planning/debug/quest-pointer-render"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    project = run / "unity"
    editor = project / "Assets/Editor"
    editor.mkdir(parents=True)
    source = (ROOT / "src/GloomhavenVR/Hands/Interact/RayInteractor.cs").read_text()
    start = source.index("    private Material CreateBeamMaterial(out Color color)")
    end = source.index("    // ---- effective-state sync", start)
    factory = source[start:end]
    (editor / "PointerProductionMaterialFactory.cs").write_text("using UnityEngine;\nnamespace GloomhavenVR.Hands.Interact { internal sealed class PointerProductionMaterialFactory {\n"
        + "public Material Build(bool quest) { Core.QuestStandalonePlatform.Enabled=quest; return CreateBeamMaterial(out _); }\n"
        + "public void Apply(Renderer renderer, bool quest) { Core.QuestStandalonePlatform.Enabled=quest; ApplyQuestPointerRendererDefaults(renderer); }\n"
        + factory + "} }\n"
        + "namespace GloomhavenVR.Core { internal static class QuestStandalonePlatform { internal static bool Enabled; } }\n")
    shutil.copyfile(ROOT / "tests/QuestPointerRender.Tests/Probe.cs", editor / "Probe.cs")
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    graphics = os.environ.get("QUEST_POINTER_TEST_GRAPHICS_API", "vulkan")
    if graphics not in ("glcore", "vulkan"):
        raise ValueError("Unknown native GPU backend")
    log = run / "runner.log"
    log.write_text("Quest pointer evidence: " + str(run) + "\n")
    print(log.read_text(), end="", flush=True)
    command = ["/home/claw/unity-2021.3.5/Editor/Unity", "-batchmode", "-force-" + graphics,
        "-projectPath", str(project), "-executeMethod", "QuestPointerRenderProbe.Run", "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    code = 1
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    try:
        code = process.wait(timeout=180)
        if code:
            raise RuntimeError("Native pointer fixture failed: " + str(run / "unity.log"))
        report = json.loads((project / "ProbeOutput/results.json").read_text())
        assert len(report["cases"]) == 12
        (run / "source-hashes.json").write_text(json.dumps({"productionFactory": hashlib.sha256(factory.encode()).hexdigest(),
            "probe": hashlib.sha256((ROOT / "tests/QuestPointerRender.Tests/Probe.cs").read_bytes()).hexdigest(),
            "graphics": graphics, "hardwareVerified": False}, indent=2) + "\n")
        print("PASS native pointer GPU material/mesh characterization: " + graphics)
    finally:
        if process.poll() is None:
            process.kill(); process.wait()
        cleanup = cleanup_completed_run("quest-pointer-render", code, log, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")


if __name__ == "__main__":
    main()
