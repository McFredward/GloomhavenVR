#!/usr/bin/env python3
"""Characterize production pointer material on native and explicit sphere GPU meshes."""
import hashlib
import json
import os
import signal
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
    color_space = os.environ.get("QUEST_POINTER_TEST_COLOR_SPACE", "linear")
    if color_space not in ("linear", "gamma"):
        raise ValueError("Unknown fixture color space")
    (editor / "PointerProductionMaterialFactory.cs").write_text("using UnityEngine;\nnamespace GloomhavenVR.Hands.Interact { internal sealed class PointerProductionMaterialFactory {\n"
        + "public Material Build(bool quest, bool missing=false) { Core.QuestStandalonePlatform.Enabled=quest; GloomhavenVR.Cards.PlayTray.Missing=missing; return CreateBeamMaterial(out _); }\n"
        + "public void Apply(Renderer renderer, bool quest) { Core.QuestStandalonePlatform.Enabled=quest; ApplyQuestPointerRendererDefaults(renderer); }\n"
        + "public Color VertexColor(Material material, Color color, bool quest) { Core.QuestStandalonePlatform.Enabled=quest; return BeamVertexColor(material, color); }\n"
        + factory + "} }\n"
        + "namespace GloomhavenVR.Core { internal static class QuestStandalonePlatform { internal static bool Enabled; } internal static class VRLog { internal static bool WantsDebug=true; } }\n"
        # The actual game has a global conditional Debug facade. Keep that name
        # present so a Debug alias cannot silently compile only in this fixture.
        + "public static class Debug { public static void Log(object value) => UnityEngine.Debug.Log(value); public static void LogException(System.Exception value) => UnityEngine.Debug.LogException(value); }\n"
        + 'namespace GloomhavenVR.Cards { internal static class PlayTray { internal static bool Missing; internal static Shader OverlayShader() => Missing ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/PointerOverlay.shader"); } }\n'
        + "public static class QuestPointerProbeConfig { public const bool Linear=" + str(color_space == "linear").lower() + "; }\n")
    evidence_source = (ROOT / "src/GloomhavenVR/Hands/Interact/QuestPointerEvidence.cs").read_text()
    control = os.environ.get("QUEST_POINTER_EVIDENCE_CONTROL", "")
    controls = {
        "desktop": ("!QuestStandalonePlatform.Enabled ||", "false ||", "Pointer observation must stay idle outside Quest Debug real hit context"),
        "repeat": ("if ((_observedKinds & kind) != 0) return;", "if (false) return;", "Pointer evidence must retain two contexts across repeated hits and objects"),
    }
    if control:
        if control not in controls: raise ValueError("Unknown pointer evidence control")
        before, after, _ = controls[control]
        assert evidence_source.count(before) == 1, "Pointer evidence control binding drift"
        evidence_source = evidence_source.replace(before, after)
    (editor / "QuestPointerEvidence.cs").write_text("#nullable enable\n" + evidence_source.replace(
        "namespace GloomhavenVR.Hands.Interact;", "namespace GloomhavenVR.Hands.Interact {") + "\n}\n")
    shader_source = ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader"
    shutil.copyfile(shader_source, project / "Assets/PointerOverlay.shader")
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
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT,
                               start_new_session=os.name == "posix")
    try:
        code = process.wait(timeout=180)
        if control:
            if not code or controls[control][2] not in (run / "unity.log").read_text():
                raise AssertionError("Pointer evidence control was not rejected by its behavioral assertion")
            print("PASS expected pointer evidence rejection: " + control)
            return
        if code:
            raise RuntimeError("Native pointer fixture failed: " + str(run / "unity.log"))
        report = json.loads((project / "ProbeOutput/results.json").read_text())
        assert len(report["cases"]) == 14
        (run / "source-hashes.json").write_text(json.dumps({"productionFactory": hashlib.sha256(factory.encode()).hexdigest(),
            "pointerEvidence": hashlib.sha256(evidence_source.encode()).hexdigest(),
            "probe": hashlib.sha256((ROOT / "tests/QuestPointerRender.Tests/Probe.cs").read_bytes()).hexdigest(),
            "ownedShader": hashlib.sha256(shader_source.read_bytes()).hexdigest(),
            "graphics": graphics, "colorSpace": color_space, "hardwareVerified": False}, indent=2) + "\n")
        print("PASS native pointer GPU material/mesh characterization: " + graphics)
    finally:
        if os.name == "posix":
            try: os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError: pass
        if process.poll() is None:
            process.kill(); process.wait()
        cleanup = cleanup_completed_run("quest-pointer-render", code, log, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")


if __name__ == "__main__":
    main()
