#!/usr/bin/env python3
"""Compile the actual tooltip driver/codec against deterministic boundary substitutes."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = Path(os.environ.get("REPO_ROOT", HERE.parents[1])).resolve()
NET = ROOT / "src/GloomhavenVR/Net"


def append_method(source):
    start = source.index("    internal static void Append<T>(")
    opening = source.index("{", start)
    depth = 1
    at = opening + 1
    while depth:
        if source[at] == "{":
            depth += 1
        elif source[at] == "}":
            depth -= 1
        at += 1
    return source[start:at]


env = dict(os.environ)
env["PATH"] = str(Path(env.get("DOTNET_ROOT", Path.home() / ".dotnet"))) + os.pathsep + env["PATH"]
with tempfile.TemporaryDirectory(prefix="map-tooltip-driver-") as folder:
    generated = Path(folder)
    for name, path in {
        "Driver.cs": NET / "Avatar/NetAvatarDriver.MapButtonTooltip.cs",
        "Codec.cs": NET / "MapButtonTooltipCodec.cs",
        "NetPacket.cs": NET / "NetPacket.cs",
    }.items():
        shutil.copyfile(path, generated / name)
    pending = append_method((NET / "PresentationPending.cs").read_text())
    (generated / "Pending.cs").write_text(
        "using System; using System.Collections.Generic;\n"
        "namespace GloomhavenVR.Net;\ninternal static class PresentationPending\n{\n"
        + pending + "\n}\n"
    )
    command = [
        "dotnet", "run", "--project", str(HERE / "GloomhavenVR.MapTooltipTransportTests.csproj"),
        "--configuration", "Release", f"--property:ProductionDir={generated}",
    ]
    subprocess.run(command, cwd=ROOT, env=env, check=True)
    driver_path = generated / "Driver.cs"
    original_driver = driver_path.read_text()
    mutations = {
        "duplicate": (
            "snapshot!.SampleTime <= last", "snapshot!.SampleTime < last",
            "duplicate and out-of-order packets cannot replace pending picture"),
        "peer-bound": (
            "_pendingMapTooltips.Count >= 8", "_pendingMapTooltips.Count >= 80",
            "receive peer memory must remain bounded"),
        "forget-watermark": (
            "_lastMapTooltipTime.Remove(sender);", "// omitted source clock cleanup",
            "forget clears pending data, source time and rendered peer output"),
        "reset-watermark": (
            "_lastMapTooltipTime.Clear();", "// omitted session clock cleanup",
            "session reset clears every peer and presentation cache"),
        "send-cadence": (
            "if (!changed && now < _nextMapTooltipRefresh) return;", "// missing cadence guard",
            "unchanged hidden state must not send before refresh"),
        "clear-boundary": (
            "PresentationPending.Append(samples, snapshot, MapButtonTooltipSnapshot.SameIdentity);",
            "PresentationPending.Append(samples, snapshot, static (a, b) => true);",
            "clear survives a rapid close and reopen burst"),
        "tick-isolation": (
            'try { MapButtonTooltipPresentation.Tick(); }\n        catch (Exception e) { LogPhaseError("Update map button tooltip", e); }',
            "MapButtonTooltipPresentation.Tick();",
            "tooltip tick failure must not abort subsequent native presentation"),
    }
    for name, (old, new, expected) in mutations.items():
        assert original_driver.count(old) == 1, f"production mutation seam moved: {name}"
        driver_path.write_text(original_driver.replace(old, new))
        result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)
        output = result.stdout + result.stderr
        if result.returncode == 0 or f"Unhandled exception. System.Exception: {expected}" not in output:
            raise RuntimeError(f"mutation {name} did not fail for its expected behavior:\n{output}")
        print(f"Map tooltip transport negative rejected: {name}")
    driver_path.write_text(original_driver)
