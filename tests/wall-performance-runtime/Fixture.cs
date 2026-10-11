using System;
using System.Collections.Generic;
using UnityEngine;
using GloomhavenVR.Board.FigureGrab;

namespace GloomhavenVR.Core;
internal static partial class WallSegmentFade
{
    private const string Name="WallSegmentFade";
    private static FadeDriver? _driver;
    private sealed class Segment
    {
        internal Transform? DoorRoot;
        internal Component? Anchor;
        internal int RoomIndex=0;
        internal bool IsGateColumn,State,PendingRaw,SmoothInit,HasBlock;
        internal float Fade,Smooth;
        internal uint WireKey;
        internal readonly List<MeshRenderer> Renderers=new(),Foliage=new(),Siblings=new();
        internal readonly List<MountedProp> Body=new(),Stacked=new(),Mounted=new(),UnitDressing=new();
    }
    private sealed class MountedProp { internal Renderer Renderer=null!; }
    private sealed class CornerPiece { internal MountedProp Prop=null!; internal Segment A=null!; internal Segment? B; }
    private sealed class Table
    {
        internal readonly Dictionary<int,Segment> Segments=new();
        internal readonly List<CornerPiece> CornerPieces=new();
        internal readonly List<Bounds> ArchRects=new();
        internal readonly List<WaterRect> WaterRects=new();
        internal int BuiltRoomCount;
        internal readonly List<string> RoomLabels=new(){"Original room"};
    }
    private readonly struct WaterRect
    {
        internal readonly float MinX,MaxX,MinZ,MaxZ,TopY;
        internal WaterRect(Bounds bounds){MinX=bounds.min.x;MaxX=bounds.max.x;MinZ=bounds.min.z;MaxZ=bounds.max.z;TopY=bounds.max.y;}
    }
    private sealed partial class FadeDriver
    {
        private enum RescanStage { Idle,Classify,Survey,Prepare,Commit }
        private enum TickPhase { Total }
        private Table _live=new();
        private RescanStage _rescanStage;
        private bool _committedSigValid=true,_rescanUrgent,_pathAuditRunning,_failureLogged,_drawTraceFailed;
        private readonly List<object> _pathAuditWalls=new();
        private readonly Dictionary<int,object> _peerFades=new();
        private readonly List<uint> _keySampleScratch=new();
        private float _nextRescan,_nextEvalTime,_lastEvalTime,_nextTruncateLog;
        private int _wireSampled,_wireSent;
        private const float WaterContainmentMin=.5f;
        internal int FixtureWireRebuilds;
        internal int Ticks,Begins,Steps,TickBegins,TickEnds,DrawWrites,TraceClears,InsideClears,Restores;
        internal Action? DuringBegin,DuringStep;
        internal Action<Fixture>? CollectorCommit;
        internal Fixture Owner=null!;
        private static PerfMonitor.Quiet Phase(TickPhase phase)=>new();
        private void BeginTickFrame()=>TickBegins++;
        private void EndTickFrame(float now)=>TickEnds++;
        private void Tick()=>Ticks++;
        private void ClearWallDrawTrace(){TraceClears++;_drawTraceFailed=false;}
        private void ResetInsideBoardState()=>InsideClears++;
        // The staged native collector is an explicit boundary. Real production methods are
        // source-bound by the script; this model exposes staged commit/throw/reentrant events.
        private void AbandonRescanCycle()=>_rescanStage=RescanStage.Idle;
        private void BeginRescanCycle(TilesOcclusionGenerator gen,float now,bool urgent)
        {Begins++;_rescanStage=RescanStage.Classify;DuringBegin?.Invoke();}
        private void StepRescanCycle(TilesOcclusionGenerator gen,float now)
        {Steps++;DuringStep?.Invoke();CollectorCommit?.Invoke(Owner);_live.BuiltRoomCount=gen.m_RoomRenderers.Count;_rescanStage=RescanStage.Idle;_committedSigValid=true;}
        // Broad original actor/floor/mod classifiers remain boundaries. Actual hierarchy
        // identities (components and current held ancestry) provide independent test oracles.
        private static bool IsModObject(Renderer renderer)=>renderer.name.StartsWith("GloomhavenVR.",StringComparison.Ordinal);
        private static bool IsFigureOrActorRenderer(Renderer renderer)=>renderer.GetComponentInParent<ActorBehaviour>(true)!=null||renderer.GetComponentInParent<CInteractableActor>(true)!=null;
        private static bool FloorNeverFades(Renderer renderer)=>renderer.GetComponentInParent<FloorMarker>(true)!=null;
        private static bool HeldNeverFades(Renderer renderer)=>HeldProps.Owns(renderer.transform);
        // Persistent arch-rectangle construction is a boundary; the complete actual native
        // arch-effect hierarchy classifier executes against these committed native bounds.
        private bool IsArchProtected(Bounds bounds,string name)
        {foreach(Bounds arch in _live.ArchRects)if(arch.Contains(bounds.center))return true;return false;}
        private void RestoreSegmentFoliage(Segment seg)=>Restores++;
        private void RestoreSegmentSiblings(Segment seg)=>Restores++;
        private void RestoreSegmentMounted(Segment seg)=>Restores++;
        private void RestoreSegmentStacked(Segment seg)=>Restores++;
        private void RestoreSegmentBody(Segment seg)=>Restores++;
        private void RestoreAllMountedProps()=>Restores++;
        private void RecordWallDrawWrite(Segment seg,MeshRenderer renderer,MaterialPropertyBlock? block)=>DrawWrites++;
        private void StopWallDrawTrace(Exception error)=>_drawTraceFailed=true;
        internal void TouchUnused()=>GC.KeepAlive((_rescanUrgent,_pathAuditRunning,_nextRescan,_nextEvalTime,_lastEvalTime,_wireSent,_wireSampled));

        internal string RecoveryContext=>"scene="+_performanceScene+", actual="+SceneController.Instance.GetCurrentScene.handle+", valid="+SceneController.Instance.GetCurrentScene.IsValid()+", loaded="+SceneController.Instance.GetCurrentScene.isLoaded+", current="+PerformanceScenarioStillCurrent()+", gen="+(_performanceGenerator==TilesOcclusionGenerator.s_Instance);
        internal int KeyRebuilds=>FixtureWireRebuilds;
        internal void ClearKeys(){foreach(Segment seg in _live.Segments.Values)seg.WireKey=0;}
        internal void Init(Fixture owner){Owner=owner;_live.BuiltRoomCount=TilesOcclusionGenerator.s_Instance.m_RoomRenderers.Count;}
        internal int Masks=>_performanceMasks.Count;
        internal bool Latched=>_performanceAutoLatched;
        internal int PeerCount=>_peerFades.Count;
        internal bool Guarded=>TickBegins==TickEnds;
        internal void Frame(float delta=.016f){WallFixtureClock.frameCount++;WallFixtureClock.unscaledDeltaTime=delta;WallFixtureClock.unscaledTime+=delta;LateUpdate();}
        internal void Entry()=>LateUpdate();
        internal void SetCommitted(bool valid)=>_committedSigValid=valid;
        internal void SetCollector(Action<Fixture>? commit,Action? begin=null,Action? step=null){CollectorCommit=commit;DuringBegin=begin;DuringStep=step;}
        internal void SetArch(Bounds bounds)=>_live.ArchRects.Add(bounds);
        internal void SetWater(Bounds bounds)=>_live.WaterRects.Add(new WaterRect(bounds));
        internal int Add(MeshRenderer wall,bool gate=false,Transform? door=null)
        {int key=_live.Segments.Count+1;var seg=new Segment{IsGateColumn=gate,DoorRoot=door,WireKey=(uint)key,Anchor=wall};seg.Renderers.Add(wall);_live.Segments.Add(key,seg);return key;}
        internal void Remove(int key)=>_live.Segments.Remove(key);
        internal void Attach(int key,Renderer renderer,string kind)
        {var seg=_live.Segments[key];switch(kind){case "foliage":seg.Foliage.Add((MeshRenderer)renderer);break;case "sibling":seg.Siblings.Add((MeshRenderer)renderer);break;case "body":seg.Body.Add(new MountedProp{Renderer=renderer});break;case "stacked":seg.Stacked.Add(new MountedProp{Renderer=renderer});break;case "mounted":seg.Mounted.Add(new MountedProp{Renderer=renderer});break;default:seg.UnitDressing.Add(new MountedProp{Renderer=renderer});break;}}
        internal void Corner(int a,int? b,Renderer renderer)=>_live.CornerPieces.Add(new CornerPiece{A=_live.Segments[a],B=b.HasValue?_live.Segments[b.Value]:null,Prop=new MountedProp{Renderer=renderer}});
        internal void Fade(int key,float amount,bool block)
        {var seg=_live.Segments[key];seg.State=seg.PendingRaw=true;seg.Fade=seg.Smooth=amount;seg.SmoothInit=true;seg.HasBlock=block;}
        internal bool FadesCleared(int key){var seg=_live.Segments[key];return !seg.State&&!seg.PendingRaw&&!seg.SmoothInit&&!seg.HasBlock&&seg.Fade==0&&seg.Smooth==0;}
        internal void Peer()=>_peerFades[1]=new object();
        internal int Sample(uint[] dest)=>SampleFadedKeys(dest);
        internal void Draw(int key,MeshRenderer renderer)=>NoteWallDrawWrite(_live.Segments[key],renderer,null);
        internal void Reset()=>ResetPerformanceVisibility("fixture scenario end");
        internal void Close(){ResetPerformanceVisibility("fixture disposed");TouchUnused();}
    }
    internal sealed class Fixture : IDisposable
    {
        private readonly FadeDriver driver;
        internal string RecoveryContext=>driver.RecoveryContext;
        internal int KeyRebuilds=>driver.KeyRebuilds;
        internal void ClearKeys()=>driver.ClearKeys();
        internal Fixture(){_driver=driver=new FadeDriver();driver.Init(this);}
        internal int Ticks=>driver.Ticks;
        internal int Begins=>driver.Begins;
        internal int Steps=>driver.Steps;
        internal int DrawWrites=>driver.DrawWrites;
        internal int Masks=>driver.Masks;
        internal bool Latched=>driver.Latched;
        internal int PeerCount=>driver.PeerCount;
        internal bool Guarded=>driver.Guarded;
        internal void Frame(float delta=.016f) => driver.Frame(delta);
        internal void Entry() => driver.Entry();
        internal void SetCommitted(bool valid) => driver.SetCommitted(valid);
        internal void SetCollector(Action<Fixture>? commit,Action? begin=null,Action? step=null) => driver.SetCollector(commit,begin,step);
        internal void SetArch(Bounds bounds) => driver.SetArch(bounds);
        internal void SetWater(Bounds bounds) => driver.SetWater(bounds);
        internal int Add(MeshRenderer wall,bool gate=false,Transform? door=null) => driver.Add(wall,gate,door);
        internal void Remove(int key) => driver.Remove(key);
        internal void Attach(int key,Renderer renderer,string kind) => driver.Attach(key,renderer,kind);
        internal void Corner(int a,int? b,Renderer renderer) => driver.Corner(a,b,renderer);
        internal void Fade(int key,float amount,bool block) => driver.Fade(key,amount,block);
        internal bool FadesCleared(int key) => driver.FadesCleared(key);
        internal void Peer() => driver.Peer();
        internal int Sample(uint[] dest) => driver.Sample(dest);
        internal void Draw(int key,MeshRenderer renderer) => driver.Draw(key,renderer);
        internal void Reset() => driver.Reset();
        public void Dispose() => driver.Close();
    }
}
