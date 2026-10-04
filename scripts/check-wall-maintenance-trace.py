#!/usr/bin/env python3
"""Execute the actual bounded ceiling-summary helper and bind its native phase inputs.

The phase arrays, logger and Mathf.Max are explicit boundaries. This proves report
selection, limits and source integration, not native collector cost or pixel output.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def block(source, signature):
    assert source.count(signature) == 1, "Binding drift: " + signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    for end in range(opening + 1, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return source[start:end+1]
    raise AssertionError("Unclosed binding: " + signature)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=ROOT/".planning/debug/wall-maintenance-trace")
    args = parser.parse_args()
    phases_path = ROOT/"src/GloomhavenVR/Core/WallFade/WallSegmentFade.CommitPhases.cs"
    core_path = ROOT/"src/GloomhavenVR/Core/WallFade/WallSegmentFade.cs"
    prepare_path = ROOT/"src/GloomhavenVR/Core/WallFade/WallSegmentFade.Prepare.cs"
    phases, core, prepare = phases_path.read_text(), core_path.read_text(), prepare_path.read_text()
    enum = block(phases,"private enum CommitPhase")
    labels = block(phases,"private static readonly string[] CommitPhaseNames") + ";"
    names = re.findall(r'"(\w+)"', labels)
    native = block(core,"private void RescanCore(TilesOcclusionGenerator gen)")
    order = re.findall(r"using \(Phase\(CommitPhase\.(\w+)\)\)", native)
    assert order == names, "Every original atomic phase must retain its literal measured boundary and order"
    assert len(names) == 25, "Review new collector phases before changing maintenance evidence"
    assert "NoteCeilingCommitTrace(nativeReads, reusedReads, completed);" in native
    assert native.index("EndCommitPhases();") < native.index("NoteCeilingCommitTrace(")
    assert "long nativeReads = WallCommitGeometryReads.NativeReads;" in native
    assert "long reusedReads = WallCommitGeometryReads.ReusedReads;" in native
    decision = block(prepare,"private bool CommitWouldChangeNothing(TilesOcclusionGenerator gen, float now)")
    ceiling = block(decision,"if (_skipRun >= MaxSkippedCyclesInARow)")
    assert "_cycleForcedByCeiling = true;" in ceiling and "return false;" in ceiling
    assert "_cycleForcedByCeiling = false;" in decision[:decision.index("if (")]
    assert "ClearCeilingCommitTrace();" in block(core,"private void OnSceneLoaded(Scene scene, LoadSceneMode mode)")
    report = block(phases,"private void NoteCeilingCommitTrace(long nativeReads, long reusedReads, bool completed)")
    assert not any(token in report for token in ("Stopwatch", "SetPropertyBlock", ".bounds", "FindObjects", ".enabled ="))
    build_names = block(phases,"private static string[] BuildCommitPhaseScopeNames()")
    clear = block(phases,"private void ClearCeilingCommitTrace()")
    limit = re.search(r"private const int CeilingCommitReportLimit = \d+;", phases).group(0)
    scaffold = """
using System; using System.Collections.Generic; using UnityEngine;
namespace UnityEngine { internal static class Mathf { internal static float Max(float a,float b)=>Math.Max(a,b); } }
enum VRLogLevel { Debug }
static class VRLog { internal static bool Enabled; internal static readonly List<string> Lines=new();
internal static bool Wants(VRLogLevel level)=>Enabled; internal static void Debug(string name,string text)=>Lines.Add(text); }
class Driver {
private const string Name="WallSegmentFade";
private const int CommitPhaseCount=(int)CommitPhase.Count;
private const int CeilingCommitReportLimit=8;
private readonly float[] _phaseCycleMillis=new float[CommitPhaseCount];
private readonly float[] _phaseTotalMillis=new float[CommitPhaseCount];
private bool _cycleForcedByCeiling; private int _ceilingCommitReports, _skipRun=30;
public void Set(bool forced) { _cycleForcedByCeiling=forced;
_phaseCycleMillis[Array.IndexOf(CommitPhaseNames,"Mounted")]=120f;
_phaseCycleMillis[Array.IndexOf(CommitPhaseNames,"WallCache")]=44f;
_phaseCycleMillis[Array.IndexOf(CommitPhaseNames,"PropUnits")]=11f;
_phaseCycleMillis[Array.IndexOf(CommitPhaseNames,"TileAnchors")]=5.5f; }
public float[] Values=>(float[])_phaseCycleMillis.Clone();
public void Report(bool completed=true)=>NoteCeilingCommitTrace(4294967296L,901L,completed);
public void Reset()=>ClearCeilingCommitTrace();
"""
    program = """
static class Program {
static int count;
static void Check(bool condition,string message) { count++; if(!condition) throw new Exception(message); }
static void Main() {
var d=new Driver(); d.Set(true); VRLog.Enabled=false; d.Report();
Check(VRLog.Lines.Count==0,"normal tier must not sample ceiling summaries");
VRLog.Enabled=true; d.Set(false); d.Report();
Check(VRLog.Lines.Count==0,"only an actual ceiling refusal emits this diagnostic");
d.Set(true); var before=d.Values; d.Report();
Check(VRLog.Lines.Count==1 && VRLog.Lines[0].Contains("phaseTotal=180.500ms")
 && VRLog.Lines[0].Contains("top=[WallFade.Commit.Mounted=120.000ms; WallFade.Commit.WallCache=44.000ms; WallFade.Commit.PropUnits=11.000ms; WallFade.Commit.TileAnchors=5.500ms]"),
 "rank actual per-cycle phases in descending order");
Check(VRLog.Lines[0].Contains("nativeBounds=4294967296 reusedBounds=901")
 && VRLog.Lines[0].Contains("completed=True") && VRLog.Lines[0].Contains("remainder=0.000ms"),"exact collector read counts and completion are retained");
Check(string.Join(",",before)==string.Join(",",d.Values),"report never mutates collector accumulators");
for(int i=0;i<20;i++) d.Report();
Check(VRLog.Lines.Count==8,"ceiling report stays bounded for a scene");
d.Reset(); d.Set(true); d.Report(false);
Check(VRLog.Lines.Count==9 && VRLog.Lines[8].StartsWith("CEILING COMMIT: 1/8")
 && VRLog.Lines[8].Contains("completed=False"),"scene reset re-arms one bounded incomplete-commit summary");
Console.WriteLine("PASS original ceiling-summary helper: "+count+" assertions; 25 actual measured phase bindings; fail-safe retained");
} }
"""
    scaffold = scaffold.replace("private const int CeilingCommitReportLimit=8;",limit)
    variants = [("production",report,"")]
    for name, before, after, expected in (
        ("normal-tier", "!VRLog.Wants(VRLogLevel.Debug)", "false", "normal tier must not sample"),
        ("unbounded", "_ceilingCommitReports >= CeilingCommitReportLimit", "false", "ceiling report stays bounded"),
        ("wrong-rank", "_phaseCycleMillis[i] > _phaseCycleMillis[best]", "_phaseCycleMillis[i] < _phaseCycleMillis[best]", "rank actual per-cycle phases"),
        ("window-not-cycle", "_phaseCycleMillis", "_phaseTotalMillis", "rank actual per-cycle phases"),
    ):
        assert report.count(before) >= 1, "Mutation binding drift: " + name
        variants.append((name,report.replace(before,after),expected))
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-",dir=args.output_dir.resolve()))
    dotnet = shutil.which("dotnet") or str(Path.home()/".dotnet/dotnet")
    outcomes = []
    for name, value, expected in variants:
        case = run/name; case.mkdir()
        (case/"Check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>\n')
        (case/"Program.cs").write_text(scaffold+enum+labels
            +"private static readonly string[] CommitPhaseScopeNames=BuildCommitPhaseScopeNames();\n"
            +build_names+clear+value+"}\n"+program)
        command=[dotnet,"run","--project",str(case/"Check.csproj"),"-c","Release","--verbosity","quiet"]
        result=subprocess.run(command,capture_output=True,text=True,env={**__import__('os').environ,"LANG":"C.UTF-8"})
        output=result.stdout+result.stderr; (case/"result.log").write_text(output)
        assert not "error CS" in output, "Compilation errors cannot count as causal evidence: " + output
        if expected:
            assert result.returncode and expected in output, "Negative control failed for an unrelated reason: " + output
            print("PASS negative control " + name + ": " + expected)
        else:
            assert result.returncode == 0, output
            print(output,end="")
        outcomes.append({"case":name,"exit":result.returncode,"expected":expected})
        for cache in ("bin","obj"): shutil.rmtree(case/cache,ignore_errors=True)
    (run/"evidence.json").write_text(json.dumps({"sources":{str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in (phases_path,core_path,prepare_path)},"literal_native_phases":names,"variants":outcomes,"limits":"Native collector work and shader pixels are not executed; the exact summary helper executes against explicit phase/logger/Mathf.Max boundaries. Staleness safety remains unchanged."},indent=2)+"\n")
    print("PASS: evidence " + str(run))


if __name__ == "__main__": main()
