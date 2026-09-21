#!/usr/bin/env python3
"""Run the production orphan sweep against live and retired presentation owners."""
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / "src/GloomhavenVR/WorldUI/Modal/ModalFallback.9.Spawn.cs").read_text()
start = source.index("    private static void SweepOrphanChrome()")
end = source.index("\n    private static void LogPollTransition", start)
sweep = source[start:end]
fixture = r'''
using System;
using System.Collections.Generic;
class GrabbableModal {
    internal static List<GrabbableModal> LiveHolders = new();
    internal string LogName = "fixture";
    internal bool Destroyed;
    internal void Destroy() { Destroyed = true; LiveHolders.Remove(this); }
}
class Panel { internal bool IsAlive = true; }
class Window { internal GrabbableModal Grab; }
static class NativeVideoWindow {
    internal static GrabbableModal Owner;
    internal static bool OwnsGrab(GrabbableModal holder) => ReferenceEquals(holder, Owner);
}
static class TownServicePresentation {
    internal static GrabbableModal Owner;
    internal static bool OwnsGrab(GrabbableModal holder) => ReferenceEquals(holder, Owner);
}
static class VRLog { internal static void Warn(string scope, string message) {} }
static class Probe {
    static int _chromeLive, _chromeOrphansSinceCensus;
    static List<Window> Converted = new();
    static Panel _errorPanel;
    static GrabbableModal _errorGrab;
    // PRODUCTION
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
    static int Main() {
        var modal = new GrabbableModal(); var town = new GrabbableModal();
        var video = new GrabbableModal(); var error = new GrabbableModal();
        var abandoned = new GrabbableModal();
        GrabbableModal.LiveHolders.AddRange(new[] { modal, town, video, error, abandoned });
        Converted.Add(new Window { Grab = modal });
        TownServicePresentation.Owner = town; NativeVideoWindow.Owner = video;
        _errorGrab = error; _errorPanel = new Panel();
        for (int pass = 0; pass < 12; pass++) SweepOrphanChrome();
        Check(!town.Destroyed, "live town handle survives repeated sweep");
        Check(!error.Destroyed, "live error handle survives repeated sweep");
        Check(!video.Destroyed && !modal.Destroyed, "existing video and modal owners survive");
        Check(abandoned.Destroyed && _chromeOrphansSinceCensus == 1, "abandoned handle still collected once");
        TownServicePresentation.Owner = null; _errorPanel.IsAlive = false;
        SweepOrphanChrome();
        Check(town.Destroyed && error.Destroyed, "retired owners do not exempt abandoned handles");
        Check(GrabbableModal.LiveHolders.Count == 2 && _chromeOrphansSinceCensus == 3, "retirement preserves unrelated owners");
        Console.WriteLine("PASS: 6 ownership assertions across 13 production sweeps");
        return 0;
    }
}
'''
mutants = [
    ("town", " || TownServicePresentation.OwnsGrab(holder)", "", "live town handle survives repeated sweep"),
    ("error", "|| (_errorPanel != null && _errorPanel.IsAlive && ReferenceEquals(_errorGrab, holder))", "", "live error handle survives repeated sweep"),
    ("retired", "_errorPanel.IsAlive && ", "", "retired owners do not exempt abandoned handles"),
]
env = dict(os.environ)
env["DOTNET_ROOT"] = env.get("DOTNET_ROOT", str(Path.home() / ".dotnet"))
dotnet = str(Path(env["DOTNET_ROOT"]) / "dotnet")
with tempfile.TemporaryDirectory(prefix="gvr-town-chrome-") as temporary:
    work = Path(temporary)
    (work / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>true</EnableDefaultCompileItems><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup></Project>')
    for name, before, after, expected in [("production", None, None, None)] + mutants:
        code = sweep
        if before is not None:
            assert code.count(before) == 1, (name, "binding drift")
            code = code.replace(before, after, 1)
        (work / "Program.cs").write_text(fixture.replace("    // PRODUCTION", code))
        build = subprocess.run([dotnet, "build", str(work / "Probe.csproj"), "-v:q", "--nologo"], env=env, capture_output=True, text=True)
        if build.returncode:
            raise RuntimeError(build.stdout + build.stderr)
        run = subprocess.run([dotnet, str(work / "bin/Debug/net8.0/Probe.dll")], env=env, capture_output=True, text=True)
        output = run.stdout + run.stderr
        if expected is None:
            assert run.returncode == 0, output
            print(output.strip())
        else:
            assert run.returncode != 0 and expected in output, (name, output)
            print("PASS negative control:", name)
