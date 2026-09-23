using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private static readonly Dictionary<ushort,TownRackState> LocalRacks = new();
    private static readonly Dictionary<ushort,TownRackStamp> LocalRackMembers = new();
    private static readonly Dictionary<ushort,CanvasGroup> LocalRackGates = new();
    private static readonly Dictionary<int,Dictionary<ushort,RackPlayback>> RemoteRacks = new();
    private static readonly List<ushort> RetiredRacks = new();
    internal static void SetRack(ushort module,TownRackState state)
    { state.Validate(module);if(Local.ContainsKey(module))LocalRacks[module]=state; }
    internal static void SetRackMember(ushort module,TownRackStamp state,CanvasGroup? pageGate = null)
    { if(Local.ContainsKey(module)){LocalRackMembers[module]=state;if(pageGate!=null)LocalRackGates[module]=pageGate;} }
    internal static void SetRackMember(ushort module,ushort rack,ushort page,uint turn,bool detached,CanvasGroup? gate)
    {
        if(LocalRackMembers.TryGetValue(module,out var old)&&old.Rack==rack&&old.Page==page&&old.Turn==turn&&old.Detached==detached)
        {if(gate!=null)LocalRackGates[module]=gate;return;}
        SetRackMember(module,new TownRackStamp{Rack=rack,Page=page,Turn=turn,Detached=detached},gate);
    }
    private static float ReadRackAlpha(LocalModule module)
    {
        LocalRackGates.TryGetValue(module.Id,out CanvasGroup? gate);float alpha=1f;
        for(Transform? parent=module.Binding.Root.parent;parent!=null;parent=parent.parent)
        {
            if(SourceParents.TryGetValue(parent,out ParentLink link)&&link.Module!=module.Id)break;
            parent.GetComponents(ParentGroups);
            foreach(CanvasGroup group in ParentGroups)
            {
                if(!group.enabled||ReferenceEquals(group,gate))continue;
                alpha*=group.alpha;if(group.ignoreParentGroups)return alpha;
            }
        }
        return alpha;
    }

    private sealed class RackPlayback
    {
        internal TownRackState State = null!, Latest = null!;
        internal TownRackState? Outgoing;
        internal ushort FromPage;
        internal readonly List<TownRackState> Queue = new();
        internal ulong Sequence;
        internal float LastTick, WaitingSince, Elapsed;
        internal ushort DisplayPage;
        internal bool Turning, Waiting;
        internal void Start(TownRackState state,float now,bool joining=false)
        {
            // Keep what the observer actually sees until the opaque midpoint, including
            // skipped owner epochs. A later turn's declared From page may never have arrived.
            if(joining||State==null)Outgoing=state;
            else if(DisplayPage==State.To||Outgoing==null)Outgoing=State;
            FromPage=joining?state.From:DisplayPage;
            State=state;Elapsed=joining?state.Elapsed:0f;
            DisplayPage=TownRackState.Progress(Elapsed)<.5f?FromPage:state.To;
            Turning=state.Turn!=0&&Elapsed<TownRackState.TurnDuration;
            Waiting=Turning;WaitingSince=LastTick=now;
            if(!Turning)DisplayPage=state.Page;
        }
        internal void Observe(TownServiceFrame frame,float now)
        {
            if(frame.Rack==null||frame.Sequence<=Sequence)return;
            TownRackState next=frame.Rack;
            if(Latest!=null&&next.Turn<Latest.Turn)return;
            Latest=next;Sequence=frame.Sequence;
            if(State==null){Start(next,now,true);return;}
            if(next.Turn==State.Turn){State=next;return;}
            if(!Turning&&Queue.Count==0){Start(next,now);return;}
            int existing=Queue.FindIndex(value=>value.Turn==next.Turn);
            if(existing>=0)Queue[existing]=next;
            else
            {
                // Bound receiver latency/memory. If several complete cycles were lost,
                // finish the displayed revolution, then catch up to the newest one.
                if(Queue.Count>=4)Queue.RemoveAt(Queue.Count-1);
                Queue.Add(next);
            }
        }
    }
    private static bool RackRetains(int peer,ushort module)
    {
        if(!RemoteRacks.TryGetValue(peer,out var clocks))return false;
        foreach(var clock in clocks.Values)
        {
            if(clock.Outgoing!=null)foreach(var member in clock.Outgoing.Members)
                if(member.Id==module&&member.Page==clock.FromPage)return true;
            if(clock.State!=null)foreach(var member in clock.State.Members)
                if(member.Id==module&&(member.Page==clock.State.From||member.Page==clock.State.To))return true;
            foreach(var queued in clock.Queue)foreach(var member in queued.Members)
                if(member.Id==module&&(member.Page==queued.From||member.Page==queued.To))return true;
        }
        return false;
    }
    private static void UpdateRackClocks(int peer,Dictionary<ushort,TownServiceFrame> pending,float now)
    {
        if(!RemoteRacks.TryGetValue(peer,out var clocks))
        { clocks=new Dictionary<ushort,RackPlayback>();RemoteRacks.Add(peer,clocks); }
        foreach(var frame in pending.Values)
        {
            if(frame.Rack==null||!Sessions.TryGetValue(peer,out var session)||frame.Session!=session.Session
                ||Array.BinarySearch(session.Modules,frame.Module)<0)continue;
            if(!clocks.TryGetValue(frame.Module,out var clock))
            { if(clocks.Count>=2)continue;clock=new RackPlayback();clocks.Add(frame.Module,clock); }
            clock.Observe(frame,now);
        }
        RetiredRacks.Clear();
        foreach(ushort id in clocks.Keys)
            if(!Sessions.TryGetValue(peer,out var session)||Array.BinarySearch(session.Modules,id)<0)RetiredRacks.Add(id);
        foreach(ushort id in RetiredRacks)clocks.Remove(id);
    }
    private static bool RackPageReady(ushort rackId,TownRackState state,ushort page,Dictionary<ushort,RemoteModule> modules)
    {
        foreach(TownRackMember member in state.Members)
        {
            if(member.Page!=page||member.Detached)continue;
            if(!modules.TryGetValue(member.Id,out var module)||module.LastFrame==null
                ||module.LastFrame.RackMember==null||module.LastFrame.RackMember.Rack!=rackId||module.LastFrame.RackMember.Page!=page)return false;
        }
        return true;
    }
    private static void TickRackClocks(int peer,Dictionary<ushort,RemoteModule> modules,float now)
    {
        if(!RemoteRacks.TryGetValue(peer,out var clocks))return;
        foreach(var pair in clocks)
        {
            RackPlayback clock=pair.Value;TownRackState state=clock.State;
            if(state==null||!modules.TryGetValue(pair.Key,out var rack)||rack.LastFrame?.Rack==null)continue;
            // The explicit per-card epoch travels with its own held/returning pose. It can
            // overtake the rack clock without ever replaying that card on an obsolete tray.
            bool handSupersedes=false;
            foreach(RemoteModule moving in modules.Values)
                if(moving.LastFrame?.RackMember is TownRackStamp stamp&&stamp.Rack==pair.Key
                    &&stamp.Detached&&stamp.Turn>=state.Turn)
                { handSupersedes=true;clock.DisplayPage=stamp.Page; }
            if(handSupersedes)
            { clock.State=state=clock.Latest;clock.Queue.Clear();clock.Turning=false;clock.Waiting=false;clock.Elapsed=TownRackState.TurnDuration; }
            bool replaying=clock.Turning;
            bool fromReady=RackPageReady(pair.Key,clock.Outgoing??state,clock.FromPage,modules),toReady=RackPageReady(pair.Key,state,state.To,modules);
            if(clock.Turning&&clock.Waiting&&fromReady&&toReady)
            {clock.Waiting=false;clock.LastTick=now;}
            if(clock.Turning&&clock.Waiting&&now-clock.WaitingSince>3f)
            {
                // Bounded cosmetic recovery only. Ordinary baseline heartbeats keep trying;
                // native services and other town windows never wait for this rack's artwork.
                clock.State=state=clock.Latest;clock.Queue.Clear();clock.Elapsed=0f;clock.WaitingSince=now;
            }
            if(clock.Turning&&!clock.Waiting)
            {
                clock.Elapsed=Mathf.Min(TownRackState.TurnDuration,clock.Elapsed+Mathf.Max(0f,now-clock.LastTick));
                clock.DisplayPage=TownRackState.Progress(clock.Elapsed)<.5f?clock.FromPage:state.To;
                if(clock.Elapsed>=TownRackState.TurnDuration)clock.Turning=false;
            }
            if(!clock.Turning&&!handSupersedes&&(state.Turn==0||state.Elapsed>=TownRackState.TurnDuration)&&RackPageReady(pair.Key,state,state.Page,modules))clock.DisplayPage=state.Page;
            clock.LastTick=now;
            bool complete=RackPageReady(pair.Key,clock.DisplayPage==clock.FromPage?clock.Outgoing??state:state,clock.DisplayPage,modules);
            // A cold page appears as one dependency group, never face/price/body fragments.
            foreach(RemoteModule child in modules.Values)
            {
                TownRackStamp? stamp=child.LastFrame?.RackMember;
                if(stamp==null||stamp.Rack!=pair.Key)continue;
                bool detached=stamp.Detached;
                if(detached)child.Motion.Reset();
                bool shown=detached||complete&&stamp.Page==clock.DisplayPage;
                child.Host.SetActive(child.LastFrame!.Visible);
                child.Host.GetComponent<CanvasGroup>().alpha=shown?stamp.Alpha:0f;
                // Clear only our explicit physical-body page gate; original material
                // visibility and renderer enablement remain the owner's native output.
                if(child.Address.StartsWith("merchant.cardbody|",StringComparison.Ordinal))
                    foreach(Renderer renderer in child.RackBodyRenderers ??= child.Binding.Root.GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=!shown;
            }
            // Full unwrapped revolution: quaternion interpolation alone aliases 0 -> 360
            // when packets coalesce. The owner explicitly supplies its clock and curve.
            TownServiceFrame authored=rack.LastFrame;
            float ownerProgress=TownRackState.Progress(authored.Rack!.Elapsed);
            Quaternion rest=Rotation(authored.Pose)*Quaternion.Inverse(Quaternion.Euler(0f,ownerProgress*360f,0f));
            float displayed=clock.Turning?TownRackState.Progress(clock.Elapsed):1f;
            rack.Motion.Reset();
            Transform rackPose = rack.AddedCanvas != null && !authored.HasCanvasFrame ? rack.Host.transform : rack.Binding.Root;
            rackPose.localRotation=rest*Quaternion.Euler(0f,displayed*360f,0f);
            if(modules.TryGetValue(state.Crank,out var crank)&&replaying)
            {
                crank.Motion.Reset();
                Transform crankPose = crank.AddedCanvas != null && crank.LastFrame?.HasCanvasFrame != true ? crank.Host.transform : crank.Binding.Root;
                crankPose.localRotation=rest*Quaternion.Euler(0f,0f,-(state.LeadAngle+(360f-state.LeadAngle)*displayed));
            }
            if(!clock.Turning&&!handSupersedes&&clock.Queue.Count>0)
            {TownRackState queued=clock.Queue[0];clock.Queue.RemoveAt(0);clock.Start(queued,now);}
        }
    }
}
