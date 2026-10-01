using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private static Dictionary<ushort,TownRackState> LocalRacks => _local.Racks;
    private static Dictionary<ushort,TownRackStamp> LocalRackMembers => _local.Members;
    private static Dictionary<ushort,CanvasGroup> LocalRackGates => _local.Gates;
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
        internal TownRackState? Outgoing, Handoff, HandoffSource;
        internal ushort FromPage;
        internal ulong Sequence;
        internal float LastTick, WaitingSince, Elapsed, ReceivedTime;
        internal ushort DisplayPage;
        internal bool Turning, Waiting;
        internal int IndicatorPage = -1, IndicatorCount;
        internal string IndicatorText = "";
        internal void Start(TownRackState state,float now,bool joining=false)
        {
            // Retain the last complete page only while actual native dependencies
            // are unavailable. Once ready, seek this owner's current epoch/age;
            // replaying missed revolutions invented a different public animation.
            Outgoing=State??state;
            FromPage=state.From;
            if(joining||State==null)DisplayPage=TownRackState.Progress(state.Elapsed)<.5f?state.From:state.To;
            State=state;Elapsed=state.Elapsed;
            Turning=state.Turn!=0&&Elapsed<TownRackState.TurnDuration;
            Waiting=Turning;WaitingSince=LastTick=now;
            if(!Turning)DisplayPage=state.Page;
        }
        internal void Observe(TownServiceFrame frame,float now)
        {
            if(frame.Rack==null||frame.Sequence<=Sequence)return;
            TownRackState next=frame.Rack;
            if(Latest!=null&&next.Turn<Latest.Turn)return;
            Latest=next;Sequence=frame.Sequence;ReceivedTime=now;
            if(State==null){Start(next,now,true);return;}
            if(next.Turn==State.Turn){State=next;return;}
            Start(next,now);
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
            if(!modules.TryGetValue(member.Id,out var module)||!module.Alive||module.LastFrame==null
                ||module.LastFrame.RackMember==null||module.LastFrame.RackMember.Rack!=rackId||module.LastFrame.RackMember.Page!=page)return false;
        }
        return true;
    }
    private static void TickRackClocks(int peer,Dictionary<ushort,RemoteModule> modules,float now)
    {
        if(!RemoteRacks.TryGetValue(peer,out var clocks))return;
        foreach(var pair in clocks)
        {
            try
            {
            RackPlayback clock=pair.Value;TownRackState state=clock.State;
            if(state==null||!modules.TryGetValue(pair.Key,out var rack)||!rack.Alive||rack.LastFrame?.Rack==null)continue;
            // The explicit per-card epoch travels with its own held/returning pose. It can
            // overtake the rack clock without ever replaying that card on an obsolete tray.
            bool handSupersedes=false;
            foreach(RemoteModule moving in modules.Values)
                if(moving.LastFrame?.RackMember is TownRackStamp stamp&&stamp.Rack==pair.Key
                    &&stamp.Detached&&stamp.Turn>=state.Turn)
                { handSupersedes=true;clock.DisplayPage=stamp.Page; }
            if(handSupersedes)
            { clock.State=state=clock.Latest;clock.Turning=false;clock.Waiting=false;clock.Elapsed=TownRackState.TurnDuration; }
            bool fromReady=RackPageReady(pair.Key,state,state.From,modules),toReady=RackPageReady(pair.Key,state,state.To,modules);
            if(!handSupersedes)
            {
                float ownerAge=Mathf.Clamp(state.Elapsed+Mathf.Max(0f,now-clock.ReceivedTime),0f,TownRackState.TurnDuration);
                bool completeOwner=state.Turn==0||ownerAge>=TownRackState.TurnDuration;
                bool ready=toReady&&(completeOwner||fromReady);
                clock.Waiting=!ready;
                if(ready)
                {
                    // Same-epoch heartbeats cannot rewind analytic motion that
                    // already advanced between packets. A newer epoch seeks its
                    // authored age immediately instead of queueing old animations.
                    clock.Elapsed=Mathf.Max(clock.Elapsed,ownerAge);
                    clock.FromPage=state.From;clock.Outgoing=state;
                    clock.Turning=state.Turn!=0&&clock.Elapsed<TownRackState.TurnDuration;
                    clock.DisplayPage=clock.Turning&&TownRackState.Progress(clock.Elapsed)<.5f?state.From:state.To;
                    if(state.Turn==0)clock.DisplayPage=state.Page;
                }
                else clock.Turning=state.Turn!=0;
            }
            bool replaying=clock.Turning&&!clock.Waiting;
            clock.LastTick=now;
            bool complete=RackPageReady(pair.Key,clock.DisplayPage==clock.FromPage?clock.Outgoing??state:state,clock.DisplayPage,modules);
            // A cold page appears as one dependency group, never face/price/body fragments.
            foreach(RemoteModule child in modules.Values)
            {
                TownRackStamp? stamp=child.LastFrame?.RackMember;
                if(stamp==null||stamp.Rack!=pair.Key)continue;
                if(!child.Alive)continue;
                bool detached=stamp.Detached;
                if(detached)child.Motion.Reset();
                bool shown=detached||complete&&stamp.Page==clock.DisplayPage;
                child.Host.SetActive(child.LastFrame!.Visible);
                child.Host.GetComponent<CanvasGroup>().alpha=shown?stamp.Alpha:0f;
                // Clear only our explicit physical-body page gate; original material
                // visibility and renderer enablement remain the owner's native output.
                if(child.Address.StartsWith("merchant.cardbody|",StringComparison.Ordinal))
                {
                    bool lostRenderer=false;
                    foreach(Renderer renderer in child.RackBodyRenderers ??= child.Binding.Root.GetComponentsInChildren<Renderer>(true))
                        if(renderer != null) renderer.forceRenderingOff=!shown;
                        else lostRenderer=true;
                    if(lostRenderer)child.RackBodyRenderers=null;
                }
            }
            // Full unwrapped revolution: quaternion interpolation alone aliases 0 -> 360
            // when packets coalesce. The owner explicitly supplies its clock and curve.
            TownServiceFrame authored=rack.LastFrame;
            float ownerProgress=TownRackState.Progress(authored.Rack!.Elapsed);
            Quaternion rest = authored.Rack.Cassette ? Rotation(authored.Pose)
                : Rotation(authored.Pose)*Quaternion.Inverse(Quaternion.Euler(0f,ownerProgress*360f,0f));
            float displayed=replaying?TownRackState.Progress(clock.Elapsed):1f;
            rack.Motion.Reset();
            Transform rackPose = rack.AddedCanvas != null && !authored.HasCanvasFrame ? rack.Host.transform : rack.Binding.Root;
            if (authored.Rack.Cassette)
                TownCassetteMotion.Apply(rack.Binding.Root, replaying ? clock.Elapsed / TownRackState.TurnDuration : 1f, state.ScrollDirection);
            else rackPose.localRotation=rest*Quaternion.Euler(0f,displayed*360f,0f);
            Transform? indicator = rack.Binding.Root.Find("PageIndicator/Caption");
            if (indicator != null && indicator.GetComponent<TMPro.TMP_Text>() is TMPro.TMP_Text label)
            {
                if (clock.IndicatorPage != clock.DisplayPage || clock.IndicatorCount != state.PageCount)
                {
                    clock.IndicatorPage = clock.DisplayPage; clock.IndicatorCount = state.PageCount;
                    clock.IndicatorText = "↑\n" + (clock.DisplayPage % 256 + 1) + " / " + state.PageCount + "\n↓";
                }
                // A later owner's captured caption must not jump ahead of the displayed
                // physical revolution while its incoming card dependencies are still loading.
                if (label.text != clock.IndicatorText) label.text = clock.IndicatorText;
            }
            if(modules.TryGetValue(state.Crank,out var crank)&&crank.Alive&&replaying)
            {
                crank.Motion.Reset();
                Transform crankPose = crank.AddedCanvas != null && crank.LastFrame?.HasCanvasFrame != true ? crank.Host.transform : crank.Binding.Root;
                float angle = -(state.ScrollDirection < 0 ? -1f : 1f) * (state.LeadAngle+(360f-state.LeadAngle)*displayed);
                crankPose.localRotation=rest*(authored.Rack.Cassette ? Quaternion.Euler(angle,0f,0f) : Quaternion.Euler(0f,0f,angle));
            }
            }
            catch(Exception e)
            {
                // A destroyed body or transient Unity hierarchy change may invalidate
                // this cosmetic rack only. Other services, cards and the remaining
                // network presentation must still advance in the same frame.
                Report("remote rack " + peer + "/" + pair.Key, e);
            }
        }
    }
}
