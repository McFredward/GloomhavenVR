#!/usr/bin/env python3
"""Compare actual Quest MultiPass screen materials against their original GPU pixels."""
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
    output = ROOT / ".planning/debug/quest-menu-material"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    project = run / "unity"
    editor = project / "Assets/Editor"
    resources = project / "Assets/Resources"
    editor.mkdir(parents=True); resources.mkdir(parents=True)
    path = ROOT / "src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.7.QuestMaterial.cs"
    source = path.read_text()
    start = source.index("    private Material? _questOpaqueUnderlay;")
    method = source[start:source.rindex("}")]
    (editor / "Factory.cs").write_text("using UnityEngine; using UnityEngine.Rendering; using GloomhavenVR.Core;\n"
        + "namespace GloomhavenVR.WorldUI { internal sealed partial class FlatScreen {\n"
        + method + "\npublic static Material Build(Shader shader,Texture capture,bool glass,bool quest,bool shared) { "
        + "QuestStandalonePlatform.Enabled=quest; QuestStandalonePlatform.SharedStereoEyeRouting=shared; "
        + "return CreateScreenMaterial(shader,capture,glass); } public void Apply(Renderer renderer,Material material,bool glass) { QuestStandalonePlatform.Enabled=true; QuestStandalonePlatform.SharedStereoEyeRouting=false; SetScreenMaterial(renderer,material,glass); } public void MenuQueue(Material material,int queue) { material.renderQueue=queue; SyncScreenUnderlayQueue(queue); } } }\n"
        + "namespace GloomhavenVR.Core { static class QuestStandalonePlatform { internal static bool Enabled, SharedStereoEyeRouting; } }\n"
        + "namespace GloomhavenVR.Cards { static class PlayTray { internal static Shader OverlayShader() => Resources.Load<Shader>(\"Overlay\"); } }\n")
    for name in ("QuestWorldScreen", "QuestWorldScreenGlass"):
        shutil.copyfile(ROOT / ("unity/GloomhavenVR.Quest/Assets/Quest/Resources/" + name + ".shader"), resources / (name + ".shader"))
    shutil.copyfile(ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader", resources / "Overlay.shader")
    shutil.copyfile(ROOT / "tests/QuestMenuMaterial.Tests/Probe.cs", editor / "Probe.cs")
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    color_space = os.environ.get("QUEST_MENU_TEST_COLOR_SPACE", "Linear")
    if color_space not in ("Linear", "Gamma"):
        raise ValueError("Unknown production color space")
    (project / "ProjectSettings/ProjectSettings.asset").write_text("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ActiveColorSpace: " + ("1" if color_space == "Linear" else "0") + "\n")
    graphics = os.environ.get("QUEST_MENU_TEST_GRAPHICS_API", "vulkan")
    if graphics not in ("glcore", "vulkan"):
        raise ValueError("Unknown native GPU backend")
    log = run / "runner.log"
    log.write_text("Quest menu material evidence: " + str(run) + "\n")
    print(log.read_text(), end="", flush=True)
    command = ["/home/claw/unity-2021.3.5/Editor/Unity", "-batchmode", "-force-" + graphics,
        "-projectPath", str(project), "-executeMethod", "QuestMenuMaterialProbe.Run", "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    code = 1
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    try:
        code = process.wait(timeout=180)
        if code:
            raise RuntimeError("Native menu material fixture failed: " + str(run / "unity.log"))
        report = json.loads((project / "ProbeOutput/results.json").read_text())
        assert report["colorSpace"] == color_space
        assert report["comparisons"] == 12 and report["defectControls"] == 3 and not report["hardwareVerified"]
        (run / "source-hashes.json").write_text(json.dumps({"productionFactory": hashlib.sha256(path.read_bytes()).hexdigest(),
            "probe": hashlib.sha256((ROOT / "tests/QuestMenuMaterial.Tests/Probe.cs").read_bytes()).hexdigest(),
            "graphics": graphics, "colorSpace": color_space, "hardwareVerified": False}, indent=2) + "\n")
        print("PASS Quest menu material GPU parity: 12 comparisons, 3 live defect controls, " + graphics)
    finally:
        if process.poll() is None:
            process.kill(); process.wait()
        cleanup = cleanup_completed_run("quest-menu-material", code, log, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")


if __name__ == "__main__":
    main()
