#!/usr/bin/env python3
"""Execute bounded wall reports and fresh maintenance CPU read/membership seams.

Phase arrays, logger, native discovery/bounds/ownership and Mathf.Max are explicit
boundaries. This proves report limits, CPU operation counts and source integration,
not actual hardware timing, geometric adoption or native pixel output.
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


def verify_cpu_reads(root, run, dotnet):
    """Execute the production sweep prefix and census lifetime/membership seams.

    Native discovery, enabled/bounds reads and ownership are counted boundaries.
    The unchanged geometric election and actual native pixels are outside this proof.
    """
    wall = root/"src/GloomhavenVR/Core/WallFade"
    stacked_path = wall/"WallSegmentFade.Stacked.cs"
    census_path = wall/"WallSegmentFade.FadeCensus.cs"
    dissolve_path = wall/"WallSegmentFade.Dissolve.cs"
    dissolve = dissolve_path.read_text()
    stacked, census = stacked_path.read_text(), census_path.read_text()
    sweep = block(stacked, "private void FastReclaimSweep(float now)")
    prefix = sweep[:sweep.index("                Segment? best = null;")]
    assert prefix.count("foreach (MeshRenderer r in all)") == 1
    prefix = prefix.replace("            int claimed = 0;\n", "")
    prefix += "                Selected.Add(r);\n            }\n        }\n"
    cadence = block(stacked, "private void FastReclaimRegeneratedShell(float now)")
    assert "private const float FastReclaimIntervalSeconds = 0.25f;" in stacked
    assert "BeginFigureMemo();" in cadence and "finally { EndFigureMemo(); }" in cadence
    assert "ScenarioSceneryBudget.IsOwnedHidden(r)" in prefix
    assert "IsArchProtected(b, r.name)" in prefix and "IsWaterProtected(b)" in prefix
    assert prefix.index("_fastOwnedScratch.Clear();") < prefix.index("FindObjectsOfType<MeshRenderer>()")
    assert "WallCommitGeometryReads.Read(r)" in prefix
    remainder = sweep[sweep.index("                Segment? best = null;"):]
    for guard in ("IsFigureOrActorRenderer(r)", "HasGameLogicAncestry(r)",
                  "RendererUsesWallFade(r)", "RendererUsesFoliage(r)",
                  "r.GetComponent<TMPro.TMP_Text>() != null", "StackMaxPerSegment",
                  "SegmentBelongsToWall(best, homeWall)"):
        assert guard in (remainder + prefix), "Original adoption guard disappeared: " + guard
    lifetime = block(census, "private void LogFadeWriteCensus(float now)")
    owner = block(census, "private bool CensusIsModObject(Renderer renderer)")
    reference = block(census, "private sealed class RendererReferenceComparer")
    index = block(census, "private void IndexCensusWrittenRenderers(FadeUnit unit)")
    note = block(census, "private void NoteFadeWrite(")
    dissolve_report = block(dissolve,"private void LogDissolveCensus(Segment seg)")
    count_start = census.index("            // DISTINCT renderers written")
    count_end = census.index("            // The owner split, once per unit", count_start)
    counts = "private void CensusCounts() {\n" + census[count_start:count_end] + "}\n"
    selector = census[census.index("private bool CountsTowardFadeUnit("):]
    selector = selector[:selector.index(";")+1]
    assert "CensusIsModObject(r)" in block(census, "private void NoteFadeWrite(")
    assert "try { CollectFadeWriteCensus(); }" in lifetime and "finally" in lifetime
    assert "_fadeCensusSharedReads = PerfConfig.SharedEnvironmentMaterialReadsOn;" in lifetime
    assert "_fadeCensusModFacts.Clear();" in lifetime and "_fadeCensusWrittenScratch.Clear();" in lifetime
    assert not any(token in owner+index+counts for token in ("SetPropertyBlock", ".enabled =", "Destroy("))
    scaffolding = r"""
#nullable enable
using System; using System.Collections.Generic; using UnityEngine;
namespace UnityEngine {
class Component { }
class Transform { public readonly List<Renderer> Renderers=new(); }
struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c) {x=a;y=b;z=c;} }
struct Bounds { public Vector3 min,max; public Bounds(float a,float b,float c,float d,float e,float f) {min=new(a,b,c);max=new(d,e,f);} }
class Renderer:Component {
 public static int NativeQueries,NativeEquality; public int Id;
 public bool Mod,Hidden,Arch,Water,Floor; public bool enabled=true; public string name="native";
 public Bounds Geometry=new(-.1f,1.7f,-.1f,.1f,2f,.1f);
 public override bool Equals(object? other) {NativeEquality++;return other is Renderer r && r.Id==Id;}
 public override int GetHashCode()=>Id;
}
class MeshRenderer:Renderer { }
static class Time { public static float unscaledTime; }
static class Object { public static int Finds; public static MeshRenderer[] Scene=Array.Empty<MeshRenderer>();
 public static T[] FindObjectsOfType<T>() {Finds++;return (T[])(object)Scene;} }
static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
}
static class VRLog {public static readonly List<string> Lines=new();public static void Info(string name,string text)=>Lines.Add(text);}
static class PerfConfig { public static bool SharedEnvironmentMaterialReadsOn; }
static class WallFadeTuning { public static bool StackedShells=true; }
static class ScenarioSceneryBudget { public static bool IsOwnedHidden(Renderer r)=>r.Hidden; }
static class WallCommitGeometryReads { public static int NativeReads;
 public static Bounds Read(Renderer r) {NativeReads++;return r.Geometry;} }
static class PerfMonitor { sealed class ScopeValue:IDisposable {public void Dispose() {}}
 public static IDisposable Scope(string label)=>new ScopeValue(); }
class MountedProp { public Renderer Renderer=null!; public int Channel; }
class CornerPiece {public Segment A=null!;public MountedProp Prop=null!;}
class Segment {
 public bool HasBounds=true; public float Fade=1,StackOrigTop=2; public int RoomIndex;
 public Bounds Bounds=new(-1,1,-1,1,2,1);
 public bool DissolveCensusLogged,IsGateColumn;public int DissolveCensusEnabledOnly=-1;public float NextDissolveCensus;public Renderer? Anchor;
 public readonly Dictionary<Renderer,MountedProp> SiblingProps=new();
 public readonly List<MeshRenderer> Renderers=new(),Foliage=new(),Siblings=new();
 public readonly List<MountedProp> Stacked=new(),Body=new(),Mounted=new();
}
class Live { public readonly List<CornerPiece> CornerPieces=new();public readonly Dictionary<int,Segment> Segments=new(); public readonly float[] RoomFloorY={0}; }
class FadeWrite {public Renderer R; public FadeWrite(Renderer r){R=r;} public FadeWrite(Renderer r,string path,string owner,float fade){R=r;} }
class FadeUnit {
 public Transform? Root; public bool NodeScoped; public int DistinctWritten,TotalRenderers,SolidTotal;
 public readonly List<FadeWrite> Written=new(); public readonly List<string> Solid=new();
}
class Driver {
 public static int ReferenceComparisons;
 private new static bool ReferenceEquals(object? a,object? b) {ReferenceComparisons++;return object.ReferenceEquals(a,b);}
 private const float FastReclaimIntervalSeconds=.25f,FoliageHideFade=.9f,GroundExclusionHeightWU=.2f,
 StackMaxOverlapDownWU=.4f,StackMaxRiseWU=1,StackLinkMaxXZ=1,FadeCensusIntervalSeconds=2;
 private const int StackMaxPerSegment=999,FadeCensusSolidNamesPerUnit=6;
 private const string Name="WallSegmentFade";private const float DissolveCensusIntervalSeconds=5;
 private int _nativeTotal=0,_swapTotal=0;private readonly List<string> _dissolveWhyScratch=new();public int Tallies;
 private void TallyPiece(MountedProp p,ref int native,ref int swapped,ref int own,ref int enabledOnly){Tallies++;switch(p.Channel){case 0:native++;break;case 1:swapped++;break;case 2:own++;break;case 3:enabledOnly++;_dissolveWhyScratch.Add(p.Renderer.name);break;}}
 public void Dissolve(float now){Time.unscaledTime=now;LogDissolveCensus(Segment);}
 private readonly Live _live=new(); private float _nextFastReclaim;
 private readonly List<Segment> _fastSegScratch=new(); private readonly HashSet<Renderer> _fastOwnedScratch=new();
 private readonly Dictionary<Renderer,MountedProp> _mountedTouched=new();
 public readonly List<MeshRenderer> Selected=new(); public int HomeBuilds,MemoBegins,MemoEnds,ClassifiedSolids;
 private void EnsureWallHomes(){HomeBuilds++;}
 private void BeginFigureMemo(){MemoBegins++;} private void EndFigureMemo(){MemoEnds++;}
 private bool StackEligible(Segment seg)=>true;
 private static bool IsModObject(Renderer r){Renderer.NativeQueries++;return r.Mod;}
 private bool IsArchProtected(Bounds b,string name)=>name=="arch";
 private bool IsWaterProtected(Bounds b)=>b.min.x==.555f;
 public Segment Segment { get {if(!_live.Segments.ContainsKey(0)) _live.Segments[0]=new Segment();return _live.Segments[0];} }
 public void Owned(Renderer r)=>_mountedTouched[r]=new MountedProp{Renderer=r};
 public void Disown(Renderer r)=>_mountedTouched.Remove(r);
 public void Sweep(float now){Selected.Clear();FastReclaimRegeneratedShell(now);}
 private float _nextFadeCensus; private bool _fadeCensusSharedReads;
 private readonly Dictionary<Renderer,bool> _fadeCensusModFacts=new(256,RendererReferenceComparer.Instance);
 private readonly HashSet<Renderer> _fadeCensusWrittenScratch=new(RendererReferenceComparer.Instance);
 private readonly List<FadeUnit> _fadeUnits=new(); private readonly List<Renderer> _fadeSolidScratch=new();
 private readonly List<FadeWrite> _fadeWrites=new();private readonly HashSet<Renderer> _propUnitTouched=new();private int _floorHeldInFadingSegments;
 private bool FloorNeverFades(Renderer r)=>r.Floor;
 public int Collects; public bool ThrowDuringCollect; public Renderer? CensusProbe;
 private void EmitShowEdgeAudit(float now) {}
 private void CollectFadeWriteCensus() {
 Collects++; if(CensusProbe!=null) for(int i=0;i<10;i++) CensusIsModObject(CensusProbe);
 foreach(FadeUnit unit in _fadeUnits)foreach(FadeWrite write in unit.Written)NoteFadeWrite(write.R,"wall renderer","owner",1);
 CensusCounts(); if(ThrowDuringCollect) throw new InvalidOperationException("census interrupted"); }
 private static void CollectFadeUnitRenderers(Transform root,bool scoped,List<Renderer> into) {into.Clear();into.AddRange(root.Renderers);}
 private void ClassifySolidPiece(Renderer piece){ClassifiedSolids++;}
 private string DescribeSolidPiece(Renderer piece)=>piece.Id.ToString();
 public void AddUnit(FadeUnit unit)=>_fadeUnits.Add(unit);
 public void Census(float now){LogFadeWriteCensus(now);}
 public int Retained=>_fadeCensusModFacts.Count+_fadeCensusWrittenScratch.Count;
 public bool Active=>_fadeCensusSharedReads;
 public bool Probe(Renderer r)=>CensusIsModObject(r);
}
"""
    closing = scaffolding.rindex("}\n")
    members = prefix+cadence+lifetime+owner+reference+index+selector+counts+note+dissolve_report
    source = scaffolding[:closing]+members+scaffolding[closing:]
    program = r"""
static class Program {
 static int assertions;
 static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception(why);}
 static MeshRenderer R(int id)=>new MeshRenderer{Id=id};
 static void Main(){
 var scene=new List<MeshRenderer>(); for(int i=0;i<700;i++)scene.Add(R(i+1));
 var d=new Driver(); for(int i=0;i<400;i++)d.Segment.Renderers.Add(scene[i]);
 d.Segment.Body.Add(new MountedProp{Renderer=scene[400]});
 for(int i=401;i<500;i++)d.Owned(scene[i]);
 scene[500].enabled=false;scene[501].Hidden=true;scene[502].Mod=true;scene[503].name="arch";
 scene[504].Geometry=new Bounds(.555f,1.7f,-.1f,.6f,2,.1f);
 scene[505].Geometry=new Bounds(100,1.7f,100,101,2,101);
 UnityEngine.Object.Scene=scene.ToArray();
 PerfConfig.SharedEnvironmentMaterialReadsOn=false;WallCommitGeometryReads.NativeReads=0;d.Sweep(0);
 var original=string.Join(",",d.Selected.ConvertAll(r=>r.Id));int independentBounds=WallCommitGeometryReads.NativeReads;
 Check(d.Selected.Count==194,"original guard population remains exact");
 var optimized=new Driver();for(int i=0;i<400;i++)optimized.Segment.Renderers.Add(scene[i]);
 optimized.Segment.Body.Add(new MountedProp{Renderer=scene[400]});for(int i=401;i<500;i++)optimized.Owned(scene[i]);
 PerfConfig.SharedEnvironmentMaterialReadsOn=true;WallCommitGeometryReads.NativeReads=0;optimized.Sweep(0);
 Check(string.Join(",",optimized.Selected.ConvertAll(r=>r.Id))==original,"fresh owned guards preserve candidates and native exclusions");
 Check(WallCommitGeometryReads.NativeReads==198 && independentBounds==698,"owned-first guard must avoid500 native bounds reads");
 int optimizedBounds=WallCommitGeometryReads.NativeReads;
 Check(optimized.HomeBuilds==1,"one eligible sweep rebuilds the current home map once");
 optimized.Sweep(.24f);Check(optimized.Selected.Count==0,"native regeneration cadence is not accelerated");
 optimized.Segment.Renderers.Remove(scene[0]);optimized.Disown(scene[401]);scene[505].Geometry=scene[506].Geometry;
 optimized.Sweep(.25f);
 Check(optimized.Selected.Contains(scene[0])&&optimized.Selected.Contains(scene[401])&&optimized.Selected.Contains(scene[505]),
 "membership removals and new live bounds are visible on the next quarter-second sweep");
 Check(optimized.MemoBegins==optimized.MemoEnds,"figure window closes for every scheduled sweep");
 var empty=new Driver();empty.Segment.Fade=0;empty.Sweep(0);
 Check(empty.HomeBuilds==0,"no faded candidate must not rebuild unused native wall homes");
 PerfConfig.SharedEnvironmentMaterialReadsOn=false;empty.Sweep(.25f);
 Check(empty.HomeBuilds==1,"Off retains the original home-map work for comparison");
 var dissolveDriver=new Driver();var prop=new MountedProp{Renderer=R(8000),Channel=0};dissolveDriver.Segment.Mounted.Add(prop);
 PerfConfig.SharedEnvironmentMaterialReadsOn=true;VRLog.Lines.Clear();dissolveDriver.Dissolve(0);
 Check(dissolveDriver.Tallies==1&&VRLog.Lines.Count==1,"first fade episode retains immediate normal-level native census");
 prop.Channel=3;dissolveDriver.Dissolve(4.999f);
 Check(dissolveDriver.Tallies==1&&VRLog.Lines.Count==1,"closed native census deadline must avoid attachment tallies");
 dissolveDriver.Dissolve(5);
 Check(dissolveDriver.Tallies==2&&VRLog.Lines.Count==2&&VRLog.Lines[1].Contains("1 still ENABLED-ONLY"),"first eligible native census observes current regenerated channel anomaly");
 PerfConfig.SharedEnvironmentMaterialReadsOn=false;dissolveDriver.Dissolve(5.1f);
 Check(dissolveDriver.Tallies==3&&VRLog.Lines.Count==2,"Off retains independent native tally while output cadence is equal");
 dissolveDriver.Segment.DissolveCensusLogged=false;PerfConfig.SharedEnvironmentMaterialReadsOn=true;dissolveDriver.Dissolve(5.2f);
 Check(VRLog.Lines.Count==3,"a new native fade episode reports immediately despite the preceding deadline");
 var census=new Driver(); var unit=new FadeUnit{Root=new Transform(),TotalRenderers=122};
 for(int i=0;i<120;i++){var r=R(1000+i);unit.Root.Renderers.Add(r);if(i<100){unit.Written.Add(new FadeWrite(r));unit.Written.Add(new FadeWrite(r));}}
 var aliasA=R(9999);var aliasB=R(9999);unit.Root.Renderers.Add(aliasA);unit.Root.Renderers.Add(aliasB);
 unit.Written.Add(new FadeWrite(aliasA));unit.Written.Add(new FadeWrite(aliasB));
 census.AddUnit(unit);census.CensusProbe=unit.Root.Renderers[0];
 PerfConfig.SharedEnvironmentMaterialReadsOn=true;Renderer.NativeQueries=0;Driver.ReferenceComparisons=0;census.Census(0);
 int sharedNative=Renderer.NativeQueries,sharedComparisons=Driver.ReferenceComparisons;
 Check(unit.DistinctWritten==102&&unit.SolidTotal==20&&census.ClassifiedSolids==20,
 "reference identity preserves distinct native wrappers and actual solid siblings");
 Check(sharedNative==122,"one ownership read per current census renderer including duplicate references");
 Check(census.Retained==0&&!census.Active,"successful census releases all new shared references");
 census.Census(1);Check(census.Collects==1,"existing two-second report sampling cadence remains bounded");
 census.CensusProbe.Mod=true;Check(census.Probe(census.CensusProbe),"live ownership outside a census is never cached");
 census.CensusProbe.Mod=false;unit.SolidTotal=0;census.ClassifiedSolids=0;
 Renderer.NativeQueries=0;Driver.ReferenceComparisons=0;PerfConfig.SharedEnvironmentMaterialReadsOn=false;census.Census(2);
 Check(unit.DistinctWritten==102&&unit.SolidTotal==20,"On and Off retain the same whole-unit measurements");
 Check(Renderer.NativeQueries==334,"Off retains independent native ownership reads");
 Check(Driver.ReferenceComparisons>10000&&sharedComparisons<1000,"shared membership replaces quadratic reference scans with linear indexing");
 census.CensusProbe.Mod=true;PerfConfig.SharedEnvironmentMaterialReadsOn=true;census.ThrowDuringCollect=true;
 try{census.Census(4);}catch(InvalidOperationException){}
 Check(census.Retained==0&&!census.Active,"interrupted census releases all new shared references");
 census.ThrowDuringCollect=false;census.CensusProbe.Mod=false;
 Check(!census.Probe(census.CensusProbe),"next census/scene ownership cannot reuse an interrupted read");
 Console.WriteLine("PASS actual wall maintenance CPU seams: "+assertions+" assertions; bounds698->"+optimizedBounds+
 "; ownership334->"+sharedNative+"; quadratic comparisons>10000->"+sharedComparisons+
 "; native election/pixels not executed");
 }
}
"""
    variants=[("production",source,"")]
    controls=(
        ("late-owned-bounds", "if (sharedReads && (_mountedTouched.ContainsKey(r) || _fastOwnedScratch.Contains(r)))", 'if (bool.Parse("false") && (_mountedTouched.ContainsKey(r) || _fastOwnedScratch.Contains(r)))', "owned-first guard must avoid500"),
        ("unused-home-walk", "if (!sharedReads)\n                EnsureWallHomes();", "if (true)\n                EnsureWallHomes();", "one eligible sweep rebuilds"),
        ("stale-owned-index", "_fastOwnedScratch.Clear();", "// injected retained sweep membership", "membership removals and new live bounds"),
        ("census-owner-no-share", "_fadeCensusSharedReads && _fadeCensusModFacts.TryGetValue", 'bool.Parse("false") && _fadeCensusModFacts.TryGetValue', "one ownership read per current census"),
        ("census-retained", "_fadeCensusModFacts.Clear();", "// injected retained census references", "successful census releases"),
        ("unit-native-equality", "new(RendererReferenceComparer.Instance)", "new()", "reference identity preserves distinct"),
        ("dissolve-deadline-ignored", "&& seg.DissolveCensusLogged && now < seg.NextDissolveCensus)", '&& bool.Parse("false") && seg.DissolveCensusLogged && now < seg.NextDissolveCensus)', "closed native census deadline must avoid"),
        ("census-switch-ignored", "_fadeCensusSharedReads = PerfConfig.SharedEnvironmentMaterialReadsOn;", "_fadeCensusSharedReads = true;", "Off retains independent native ownership"),
    )
    for name,before,after,expected in controls:
        assert source.count(before)==1, "Wall CPU mutation binding drift: "+name
        mutant=source.replace(before,after)
        if name=="late-owned-bounds":
            # Preserve the late ownership rejection: this control changes cost only.
            mutant=mutant.replace("if (!sharedReads && (_mountedTouched.ContainsKey(r) || _fastOwnedScratch.Contains(r)))", "if (_mountedTouched.ContainsKey(r) || _fastOwnedScratch.Contains(r))")
        variants.append((name,mutant,expected))
    outcomes=[]
    for name,value,expected in variants:
        case=run/("cpu-"+name);case.mkdir()
        (case/"Check.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><TreatWarningsAsErrors>true</TreatWarningsAsErrors><NoWarn>CS0649</NoWarn></PropertyGroup></Project>\n')
        (case/"Program.cs").write_text(value+program)
        result=subprocess.run([dotnet,"run","--project",str(case/"Check.csproj"),"-c","Release","--verbosity","quiet"],capture_output=True,text=True)
        output=result.stdout+result.stderr;(case/"result.log").write_text(output)
        assert "error CS" not in output,"Compilation is not causal evidence: "+output
        if expected:
            assert result.returncode and expected in output,"Wall CPU control failed for another reason: "+output
            print("PASS wall CPU negative control "+name+": "+expected)
        else:
            assert result.returncode==0,output
            print(output,end="")
        outcomes.append({"case":name,"exit":result.returncode,"expected":expected})
        for cache in ("bin","obj"):shutil.rmtree(case/cache,ignore_errors=True)
    (run/"cpu-evidence.json").write_text(json.dumps({"sources":{str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in (stacked_path,census_path,dissolve_path)},"variants":outcomes,"boundaries":"Actual pre-election discovery/eligibility prefix, two-second lifetime and distinct/solid membership loops execute against counted Unity ownership/enabled/bounds/discovery boundaries. Geometric election, native fade writes and hardware pixels/timing are outside this fixture."},indent=2)+"\n")

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
    verify_cpu_reads(ROOT,run,dotnet)
    print("PASS: evidence " + str(run))


if __name__ == "__main__": main()
