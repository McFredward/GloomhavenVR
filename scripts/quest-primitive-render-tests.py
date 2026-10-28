#!/usr/bin/env python3
"""Exercise actual Quest primitive input reconstruction with Unity's native GPU."""
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
    root = ROOT / ".planning/debug/quest-primitive-render"
    root.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=root))
    project = run / "unity"
    editor = project / "Assets/Editor"
    editor.mkdir(parents=True)
    helper_path = ROOT / "src/GloomhavenVR/Core/QuestPrimitiveMesh.cs"
    helper_bytes = helper_path.read_bytes()
    helper = helper_bytes.decode()
    helper = helper.replace("namespace GloomhavenVR.Core;", "namespace GloomhavenVR.Core {") + "\n}\n"
    (editor / "QuestPrimitiveMesh.cs").write_text("#nullable enable\n" + helper)
    (editor / "FixturePlatform.cs").write_text("""namespace GloomhavenVR.Core {
internal static class QuestStandalonePlatform { internal static bool Enabled; }
internal enum VRLogLevel { Debug }
internal static class VRLog {
 internal static bool Wants(VRLogLevel level) => true;
 internal static void Debug(string scope, string text) => UnityEngine.Debug.Log(text);
 internal static void Warn(string scope, string text) => UnityEngine.Debug.Log(text);
} }
""")
    shader = ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader"
    shader_bytes = shader.read_bytes()
    (project / "Assets/PrimitiveOverlay.shader").write_bytes(shader_bytes)
    probe = ROOT / "tests/QuestPrimitiveRender.Tests/Probe.cs"
    probe_bytes = probe.read_bytes()
    (editor / "Probe.cs").write_bytes(probe_bytes)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    backend = os.environ.get("QUEST_PRIMITIVE_TEST_GRAPHICS_API", "vulkan")
    if backend not in ("vulkan", "glcore"):
        raise ValueError("Unknown native graphics backend")
    log = run / "runner.log"
    log.write_text("Quest primitive evidence: " + str(run) + "\n")
    print(log.read_text(), end="", flush=True)
    command = ["/home/claw/unity-2021.3.5/Editor/Unity", "-batchmode", "-force-" + backend,
               "-projectPath", str(project), "-executeMethod", "QuestPrimitiveRenderProbe.Run",
               "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    code = 1
    try:
        code = process.wait(timeout=180)
        if code:
            raise RuntimeError("Native primitive fixture failed: " + str(run / "unity.log"))
        report = json.loads((project / "ProbeOutput/results.json").read_text())
        if report.get("assertions", 0) < 100 or len(report.get("gpu", [])) != 6:
            raise AssertionError("Incomplete primitive CPU/GPU fixture evidence")
        (run / "source-hashes.json").write_text(json.dumps({
            "helper": hashlib.sha256(helper_bytes).hexdigest(),
            "probe": hashlib.sha256(probe_bytes).hexdigest(),
            "shader": hashlib.sha256(shader_bytes).hexdigest(),
            "graphics": backend, "hardwareVerified": False}, indent=2) + "\n")
        print("PASS Quest primitive CPU/GPU reconstruction: " + backend + " assertions=" + str(report["assertions"]), flush=True)
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        cleanup = cleanup_completed_run("quest-primitive-render", code, log, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")


if __name__ == "__main__":
    main()
