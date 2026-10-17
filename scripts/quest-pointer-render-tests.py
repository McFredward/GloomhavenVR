#!/usr/bin/env python3
"""Characterize production pointer material on native and explicit sphere GPU meshes."""
import hashlib
import ctypes
import json
import os
import signal
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from town_run_retention import cleanup_completed_run


def own_native_children():
    # Unity's Roslyn server survives the editor and ignores SIGTERM on this
    # image. Adopt only this invocation's descendants so they can be reaped
    # before deleting its project caches; never shut down shared compilers.
    if sys.platform.startswith("linux"):
        if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0):
            raise OSError(ctypes.get_errno(), "Cannot own native fixture children")


def reap_native_children(process):
    if os.name != "posix":
        if process.poll() is None:
            process.kill()
        process.wait()
        return []
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    if process.poll() is None:
        process.kill()
    process.wait()
    reaped = []
    deadline = time.monotonic() + 8
    while True:
        if sys.platform.startswith("linux"):
            for entry in Path("/proc").iterdir():
                if not entry.name.isdecimal():
                    continue
                try:
                    fields = (entry / "stat").read_text().rsplit(")", 1)[1].split()
                    owned = int(fields[1]) == os.getpid() or int(fields[2]) == process.pid
                    if owned:
                        os.kill(int(entry.name), signal.SIGKILL)
                except (FileNotFoundError, ProcessLookupError):
                    pass
        try:
            pid, _ = os.waitpid(-1, os.WNOHANG)
        except ChildProcessError:
            return reaped
        if pid:
            reaped.append(pid)
            continue
        if time.monotonic() >= deadline:
            raise RuntimeError("Native fixture still has live owned children; retain its caches")
        time.sleep(.05)


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
        + "namespace GloomhavenVR.Core { internal static class QuestStandalonePlatform { internal static bool Enabled; } internal enum VRLogLevel { Debug } internal static class VRLog { internal static bool WantsDebug=true; internal static bool Wants(VRLogLevel level) => WantsDebug; } }\n"
        # The actual game has a global conditional Debug facade. Keep that name
        # present so a Debug alias cannot silently compile only in this fixture.
        + "public static class Debug { [System.Diagnostics.Conditional(\"ENABLE_LOGS\")] public static void Log(object value) => throw new System.Exception(\"Original Debug facade was called\"); [System.Diagnostics.Conditional(\"ENABLE_LOGS\")] public static void LogException(System.Exception value) => throw new System.Exception(\"Original Debug facade was called\"); }\n"
        + 'namespace GloomhavenVR.Cards { internal static class PlayTray { internal static bool Missing; internal static Shader OverlayShader() => Missing ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/PointerOverlay.shader"); } }\n'
        + "public static class QuestPointerProbeConfig { public const bool Linear=" + str(color_space == "linear").lower() + "; }\n")
    evidence_source = (ROOT / "src/GloomhavenVR/Hands/Interact/QuestPointerEvidence.cs").read_text()
    control = os.environ.get("QUEST_POINTER_EVIDENCE_CONTROL", "")
    controls = {
        "desktop": ("!QuestStandalonePlatform.Enabled ||", "false ||", "Pointer observation must stay idle outside Quest Debug real hit context"),
        "repeat": ("if ((_observedKinds & kind) != 0) return;", "if (false) return;", "Pointer evidence must retain two contexts across repeated hits and objects"),
        "hide-marker": ("try { UnityEngine.Debug.Log(Describe(own, marker, head, primary, scenario)); }", "try { marker.gameObject.SetActive(false); UnityEngine.Debug.Log(Describe(own, marker, head, primary, scenario)); }", "Pointer evidence must preserve the visible hit marker"),
        "native-debug": ("UnityEngine.Debug.Log(Describe(own, marker, head, primary, scenario))", "Debug.Log(Describe(own, marker, head, primary, scenario))", "Pointer evidence must retain two contexts across repeated hits and objects"),
        "native-bake": ("try { UnityEngine.Debug.Log(Describe(own, marker, head, primary, scenario)); }", "try { own.BakeMesh(new Mesh(), head, true); UnityEngine.Debug.Log(Describe(own, marker, head, primary, scenario)); }", "Pointer evidence must not allocate native mesh objects"),
    }
    if control:
        if control not in controls: raise ValueError("Unknown pointer evidence control")
        before, after, _ = controls[control]
        assert evidence_source.count(before) == 1, "Pointer evidence control binding drift"
        evidence_source = evidence_source.replace(before, after)
    (editor / "QuestPointerEvidence.cs").write_text("#nullable enable\n" + evidence_source.replace(
        "namespace GloomhavenVR.Hands.Interact;", "namespace GloomhavenVR.Hands.Interact {") + "\n}\n")
    copy_source = (ROOT / "src/GloomhavenVR/Core/QuestTextureCopy.cs").read_text()
    (editor / "QuestTextureCopy.cs").write_text("#nullable enable\n" + copy_source.replace(
        "namespace GloomhavenVR.Core;", "namespace GloomhavenVR.Core {") + "\n}\n")
    primitive_source = (ROOT / "src/GloomhavenVR/Core/QuestPrimitiveMesh.cs").read_text()
    (editor / "QuestPrimitiveMesh.cs").write_text("#nullable enable\n" + primitive_source.replace(
        "namespace GloomhavenVR.Core;", "namespace GloomhavenVR.Core {") + "\n}\n")
    resources = project / "Assets/Resources"
    resources.mkdir()
    copy_shader = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestTextureCopy.shader"
    shutil.copyfile(copy_shader, resources / "QuestTextureCopy.shader")
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
    own_native_children()
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
        assert len(report["cases"]) == 20
        (run / "source-hashes.json").write_text(json.dumps({"productionFactory": hashlib.sha256(factory.encode()).hexdigest(),
            "pointerEvidence": hashlib.sha256(evidence_source.encode()).hexdigest(),
            "textureCopy": hashlib.sha256(copy_source.encode()).hexdigest(),
            "primitiveMesh": hashlib.sha256(primitive_source.encode()).hexdigest(),
            "textureCopyShader": hashlib.sha256(copy_shader.read_bytes()).hexdigest(),
            "probe": hashlib.sha256((ROOT / "tests/QuestPointerRender.Tests/Probe.cs").read_bytes()).hexdigest(),
            "ownedShader": hashlib.sha256(shader_source.read_bytes()).hexdigest(),
            "graphics": graphics, "colorSpace": color_space, "hardwareVerified": False}, indent=2) + "\n")
        print("PASS native pointer GPU material/mesh characterization: " + graphics)
    finally:
        reaped = reap_native_children(process)
        (run / "child-processes-reaped.json").write_text(json.dumps({"reaped": reaped,
            "allOwnedChildrenReaped": True}, indent=2) + "\n")
        cleanup = cleanup_completed_run("quest-pointer-render", code, log, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")


if __name__ == "__main__":
    main()
