#!/usr/bin/env python3
"""Observe one retained native floor through actual Unity material/light selection.

This imports only the named floor mesh, its original two catalog materials and
their exact shader/texture closure. It neither imports the game nor compiles an
exhaustive variant collection. The evidence does not certify Android XR output.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from town_run_retention import cleanup_completed_run

MESH = "Assets/QuestRecoveredBundles/220b503c4884acf498f380a37435cd4c/EN_CR_FloorHex_Edge_Even2.asset"
MATERIALS = (
    "Assets/QuestRecoveredBundles/35c54e421df936d4d9a2494084dab2ea/EN_Crypt_Blocks 3.mat",
    "Assets/QuestRecoveredBundles/5381b44227d733f439b9b5313b5ed80a/EN_Crypt_Floor_Blocks.mat",
)
CAPTURED_TEXTURE = "Assets/QuestRecoveredBundles/21dd9d35059f61f47aaf11a14a67a6c4/GH_Crypt_Blocks_5.png"


def reap_owned(group):
    """Reap this dedicated subreaper's Unity/Roslyn children before cache cleanup."""
    adopted, reaped = set(), []
    for signum in (signal.SIGTERM, signal.SIGKILL):
        try:
            os.killpg(group, signum)
        except ProcessLookupError:
            pass
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            for path in Path("/proc").iterdir():
                if not path.name.isdigit():
                    continue
                try:
                    state = (path / "stat").read_text().rsplit(")", 1)[1].split()
                    if int(state[1]) != os.getpid():
                        continue
                    adopted.add(int(path.name))
                    os.kill(int(path.name), signum)
                except (FileNotFoundError, ProcessLookupError, PermissionError):
                    pass
            try:
                child, status = os.waitpid(-1, os.WNOHANG)
            except ChildProcessError:
                return {"ownedProcessGroup": group, "adoptedChildren": sorted(adopted),
                        "reapedChildren": reaped, "remainingChildren": 0}
            if child:
                reaped.append({"pid": child, "status": status})
            else:
                time.sleep(.05)
    raise RuntimeError("Owned Unity descendants remain; cache cleanup withheld")


def retain_closure(original, project):
    pending, copied, hashes = [MESH, *MATERIALS, CAPTURED_TEXTURE], set(), {}
    total = 0
    while pending:
        relative = pending.pop()
        if relative in copied:
            continue
        if not relative.startswith("Assets/") or ".." in Path(relative).parts:
            raise ValueError("Native fixture closure left its retained Assets owner")
        path = original / relative
        if not path.is_file() or path.is_symlink():
            raise ValueError("Native fixture input is absent or borrowed: " + relative)
        data = path.read_bytes()
        total += len(data)
        if total > 64 * 1024 * 1024 or len(copied) > 512:
            raise ValueError("Named native material closure exceeded its small-fixture budget")
        target = project / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        hashes[relative] = hashlib.sha256(data).hexdigest()
        copied.add(relative)
        meta = Path(str(path) + ".meta")
        if meta.is_file():
            shutil.copyfile(meta, str(target) + ".meta")
            hashes[relative + ".meta"] = hashlib.sha256(meta.read_bytes()).hexdigest()
        if path.suffix in (".mat", ".shader", ".hlsl", ".cginc"):
            source = data.decode("utf-8")
            for guid in set(re.findall(r"guid: ([0-9a-f]{32})", source)):
                if guid == "0" * 32:
                    continue
                folder = original / "Assets/QuestRecoveredBundles" / guid
                candidates = [item for item in folder.iterdir() if item.is_file() and not item.name.endswith(".meta")]
                if len(candidates) != 1:
                    raise ValueError("Native material dependency is ambiguous: " + guid)
                pending.append(str(candidates[0].relative_to(original)))
            pending.extend(re.findall(r'#include "(Assets/[^"\r\n]+)"', source))
    return {"files": len(copied), "bytes": total, "sourceHashes": hashes}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-project", type=Path, required=True)
    parser.add_argument("--control", choices=("hide-world", "omit-lighting"))
    parser.add_argument("--color-space", choices=("Linear", "Gamma"), default="Linear")
    args = parser.parse_args()
    base = ROOT / ".planning/debug/quest-world-material"
    base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=base))
    project = run / "unity"
    closure = retain_closure(args.native_project.resolve(), project)
    editor = project / "Assets/Editor"
    editor.mkdir(parents=True)
    probe = ROOT / "tests/QuestWorldMaterial.Tests/Probe.cs"
    shutil.copyfile(probe, editor / "Probe.cs")
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    (project / "ProjectSettings/ProjectSettings.asset").write_text("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 23\n  m_ActiveColorSpace: " + ("0" if args.color_space == "Gamma" else "1") + "\n")
    (run / "inputs.json").write_text(json.dumps(closure, indent=2) + "\n")
    (project / "ProbeControl.txt").write_text(args.control or "none")
    (project / "ProbeColorSpace.txt").write_text(args.color_space)
    runner = run / "runner.log"
    runner.write_text("Quest native floor material evidence: " + str(run) + "\n")
    print(runner.read_text(), end="", flush=True)
    command = ["/home/claw/unity-2021.3.5/Editor/Unity", "-batchmode", "-force-vulkan", "-projectPath", str(project),
               "-executeMethod", "QuestWorldMaterialProbe.Run", "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a", *command]
    if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0):
        raise OSError(ctypes.get_errno(), "Could not own Unity fixture child cleanup")
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
    code = 1
    try:
        code = process.wait(timeout=240)
        if args.control:
            expected = "The original native floor receives actual per-object lighting and draws colored pixels"
            if code == 0 or ("InvalidOperationException: " + expected) not in (run / "unity.log").read_text():
                raise AssertionError("Native material causal control was not rejected by its exact assertion")
            (run / "control.json").write_text(json.dumps({"control": args.control, "rejected": True,
                "expected": expected, "processExit": code}, indent=2) + "\n")
            print("PASS native floor control rejected: " + args.control, flush=True)
            return
        if code:
            raise RuntimeError("Native floor fixture failed: " + str(run / "unity.log"))
        result = json.loads((project / "ProbeOutput/results.json").read_text())
        if result.get("assertions", 0) < 20 or result.get("hardwareVerified") is not False:
            raise ValueError("Incomplete or overstated native floor evidence")
        sources = {str(probe.relative_to(ROOT)): hashlib.sha256(probe.read_bytes()).hexdigest(),
                   "scripts/quest-world-material-tests.py": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
        (run / "source-hashes.json").write_text(json.dumps({"sourceHashes": sources, "graphics": "vulkan",
            "hardwareVerified": False, "colorSpace": args.color_space}, indent=2) + "\n")
        print("PASS native floor material: " + str(result["assertions"]) + " assertions", flush=True)
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        (run / "process-cleanup.json").write_text(json.dumps(reap_owned(process.pid), indent=2) + "\n")
        cleanup = cleanup_completed_run("quest-world-material", code, runner, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")


if __name__ == "__main__":
    main()
