#!/usr/bin/env python3
"""Exercise the production scene-census gate with menu/load/scenario transitions."""

from pathlib import Path
import re
import shutil
import subprocess
import tempfile


root = Path(__file__).resolve().parents[1]
source = (root / "src/GloomhavenVR/Core/Perf/PerfSceneProfile.cs").read_text()
match = re.search(r"(?ms)^internal enum CensusDecision .*?(?=^/// <summary>)", source)
if match is None:
    raise SystemExit("Could not isolate production census gate")

dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
if not Path(dotnet).is_file():
    raise SystemExit(".NET SDK required for scene-profile-gate-tests")

checks = r'''
internal static class Program
{
    private static void Check(CensusDecision actual, CensusDecision expected, string where)
    {
        if (actual != expected) throw new Exception(where + ": " + actual + " != " + expected);
    }

    private static void Main()
    {
        var gate = new CensusRationer();
        Check(gate.Decide(true, 10, 0), CensusDecision.Sample, "first menu census");
        gate.RecordSample(10, 0, 77.9, 4, 8);
        if (gate.SkipWindows != 8) throw new Exception("cold menu cooldown");
        Check(gate.Decide(true, 10, 0), CensusDecision.Skipped, "same menu");
        Check(gate.Decide(false, 20, 0), CensusDecision.Deferred, "scene loading");
        if (gate.SkipWindows != 7) throw new Exception("loading consumed cooldown");
        Check(gate.Decide(true, 20, 0), CensusDecision.NewScene, "finished scenario");
        gate.RecordSample(20, 0, 36, 4, 8);
        Check(gate.Decide(true, 20, 0), CensusDecision.Skipped, "same scenario activation");
        Check(gate.Decide(true, 20, 21), CensusDecision.NewScene, "new procgen scene");
        gate.RecordSample(20, 21, 2, 4, 8);
        Check(gate.Decide(true, 20, 21), CensusDecision.Sample, "cheap repeat census");

        if (CensusVisibility.CountVisible(true, true))
            throw new Exception("forceRenderingOff remained visible");
        if (CensusVisibility.EstimateSubmitted(true,
                CensusVisibility.CountVisible(true, true), true))
            throw new Exception("forceRenderingOff entered submission estimate");
        if (!CensusVisibility.EstimateSubmitted(true,
                CensusVisibility.CountVisible(true, false), true))
            throw new Exception("ordinary visible renderer was lost");
        if (CensusVisibility.EstimateSubmitted(false, true, true)
            || CensusVisibility.EstimateSubmitted(true, true, false))
            throw new Exception("disabled/out-of-mask renderer entered estimate");
        Console.WriteLine("scene profile gate: lifecycle and visibility transitions passed");
    }
}
'''

with tempfile.TemporaryDirectory(prefix="ghvr-scene-profile-") as tmp:
    path = Path(tmp)
    (path / "Gate.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
        '</PropertyGroup></Project>'
    )
    (path / "Program.cs").write_text(
        "using System;\nnamespace GloomhavenVR.Core;\n"
        + match.group(0) + checks
    )
    subprocess.run([dotnet, "run", "--project", str(path / "Gate.csproj"),
                    "--configuration", "Release", "--no-launch-profile"], check=True)
