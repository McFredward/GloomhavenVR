#!/usr/bin/env python3
"""Exercise actual mod publishers/coordinator with isolated failing observers.

Native payload types, clocks and the log sink are declared fixture ports; native
gameplay, Photon and head pixels are outside this portable test's proof.
"""
from pathlib import Path
import hashlib
import importlib.util
import json
import os
import re
import signal
import shutil
import subprocess
import sys
import tempfile
import time


def execute(command, env):
    child = subprocess.Popen(command, env=env, stdout=subprocess.PIPE,
                             stderr=subprocess.STDOUT, text=True, start_new_session=True)
    try:
        stdout, _ = child.communicate(timeout=120)
        return subprocess.CompletedProcess(command, child.returncode, stdout)
    finally:
        try:
            os.killpg(child.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        child.wait()


def main():
    root = Path(__file__).resolve().parents[1]
    output = root / ".planning/debug/event-dispatch"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    project = run / "project"
    project.mkdir()
    source = run / "source"
    source.mkdir()
    fixture = root / "tests/GloomhavenVR.EventDispatchTests"
    bindings = []
    for item in fixture.iterdir():
        if item.is_file():
            shutil.copy2(item, project / item.name)
            bindings.append({"path": str(item.relative_to(root)), "sha256": hashlib.sha256(item.read_bytes()).hexdigest()})
    for name in ("Core/Events/EventDispatch.cs", "Core/Events/VREvents.cs",
                 "Core/Events/VRModeStateMachine.cs", "Core/Perf/TickGuard.cs",
                 "Core/Events/ProgressWaitNotice.cs"):
        path = root / "src/GloomhavenVR" / name
        shutil.copy2(path, source / path.name)
        bindings.append({"path": str(path.relative_to(root)), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    bindings.append({"path": str(Path(__file__).relative_to(root)), "sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()})
    (run / "source-bindings.json").write_text(json.dumps(bindings, indent=2) + "\n")
    command = ["dotnet", "run", "--project", str(project / "GloomhavenVR.EventDispatchTests.csproj"),
               "-c", "Release", "--property:SourceDir=" + str(source)]
    env = dict(os.environ, UseSharedCompilation="false", MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0")
    env["PATH"] = str(Path(env.get("DOTNET_ROOT", Path.home() / ".dotnet"))) + os.pathsep + env["PATH"]
    log = run / "console.log"
    log.write_text("Evidence: " + str(run) + "\n")
    print("Evidence:", run, flush=True)
    status = 1
    started = time.monotonic()
    try:
        result = execute(command, env)
        with log.open("a") as stream:
            stream.write(result.stdout)
        print(result.stdout, flush=True)
        if result.returncode:
            return result.returncode
        assertions = int(re.search(r"Event dispatch: (\d+) assertions", result.stdout)[1])
        assembly = run / "executed-assembly"
        assembly.mkdir()
        for item in (project / "bin/Release/net8.0").iterdir():
            if item.is_file():
                shutil.copy2(item, assembly / item.name)
        originals = {item: item.read_text() for item in source.iterdir()}
        controls = [
            ("legacy-event-abort", "VREvents.cs",
             "EventDispatch<T>.Invoke(handler, payload, eventName);",
             "try { handler?.Invoke(payload); } catch (Exception ex) { VRLog.Error(\"Events\", ex.ToString()); }",
             "later event observers receive original edge"),
            ("legacy-mode-abort", "VRModeStateMachine.cs",
             "EventDispatch<VRModeChange>.Invoke(ModeChanged, new VRModeChange(from, effective), nameof(ModeChanged));",
             "try { ModeChanged?.Invoke(new VRModeChange(from, effective)); } catch (Exception ex) { VRLog.Error(\"Mode\", ex.ToString()); }",
             "actual mode edge reaches later input observer"),
            ("uncached-notifications", "EventDispatch.cs",
             "Entries.GetValue(handler, Snapshot)", "Snapshot(handler)",
             "stable subscriptions allocate no per-notification list"),
            ("missing-wait-guidance", "ProgressWaitNotice.cs",
             "now - _since >= 20f", "now - _since >= float.PositiveInfinity",
             "prolonged turn closure gets specific guidance"),
        ]
        controls.append(("instant-wait-side-effect", "ProgressWaitNotice.cs",
             "? HintKey(owner, wait) : null;", "? (CurrentHint == null ? null : HintKey(owner, wait)) : null;",
             "immediate help accessor describes an actual wait without its diagnostic delay"))
        for name, file, needle, replacement, expected in controls:
            for path, text in originals.items():
                path.write_text(text)
            path = source / file
            text = path.read_text()
            if text.count(needle) != 1:
                raise RuntimeError("Control source drift: " + name)
            path.write_text(text.replace(needle, replacement))
            shutil.copy2(path, run / (name + ".cs"))
            result = execute(command, env)
            (run / (name + ".log")).write_text(result.stdout)
            if not result.returncode or expected not in result.stdout or "error CS" in result.stdout:
                raise RuntimeError("Control did not fail at intended assertion: " + name + "\n" + result.stdout)
            message = "Event dispatch compiling control rejected: " + name
            print(message, flush=True)
            with log.open("a") as stream:
                stream.write(message + "\n")
        for path, text in originals.items():
            path.write_text(text)
        (run / "result.json").write_text(json.dumps({
            "status": "pass", "source_bindings": bindings,
            "assertions": assertions,
            "controls": [row[0] for row in controls], "elapsed_seconds": time.monotonic() - started,
            "hardware_verified": False,
        }, indent=2) + "\n")
        status = 0
        return 0
    finally:
        spec = importlib.util.spec_from_file_location("retention", root / "scripts/town_run_retention.py")
        retention = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(retention)
        receipt = retention.cleanup_completed_run("event-dispatch", status, log, root, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")


if __name__ == "__main__":
    sys.exit(main())
