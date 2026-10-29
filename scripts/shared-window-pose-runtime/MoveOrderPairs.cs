using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

// Two isolated copies of the actual static runtime, scheduled on one thread. The snapshot port
// changes no production decisions: it swaps process-local statics just as two game processes do.
internal static class MoveOrderPairs
{
    private const SharedWindowKind Kind = SharedWindowKind.QuestConfirm;
    private static readonly Type[] Types = {typeof(SharedWindowMoves),typeof(RemoteMapStory),
        typeof(RemoteStorySync),typeof(SharedWindowSpawnAuthority),typeof(SharedWindows),typeof(PanelLayout),
        typeof(MapRoomDriver),typeof(FFSNetwork),typeof(NetPlayerActors),typeof(NetSession),typeof(Time),
        typeof(VersionGuard),typeof(MapStoryLifecycle)};
    private sealed class Fields
    {
        private readonly List<(FieldInfo Field, object? Value)> _values = new();
        internal Fields(Type type, object? target)
        {
            foreach (var field in type.GetFields(BindingFlags.Public|BindingFlags.NonPublic|
                (target==null?BindingFlags.Static:BindingFlags.Instance)))
            {
                if(field.IsLiteral)continue;object? value=field.GetValue(target);
                if(value is IDictionary dict) {var copy=new List<DictionaryEntry>();foreach(DictionaryEntry row in dict)copy.Add(row);value=copy;}
                else if(value is Array a)value=a.Clone();
                else if(value is IList list) {var copy=new List<object?>();foreach(var row in list)copy.Add(row);value=copy;}
                else if(field.IsInitOnly && value!=null && (value.GetType()==typeof(SharedWindowPoseTrack)
                    || value.GetType().Name=="Local"))value=new Fields(value.GetType(),value);
                _values.Add((field,value));
            }
        }
        internal void Restore(object? target)
        {
            foreach(var row in _values)
            {
                object? current=row.Field.GetValue(target);
                if(row.Value is List<DictionaryEntry> dict && current is IDictionary destination)
                {destination.Clear();foreach(var entry in dict)destination.Add(entry.Key,entry.Value);}
                else if(row.Value is Array a && row.Field.IsInitOnly && current is Array destinationArray)Array.Copy(a,destinationArray,a.Length);
                else if(row.Value is List<object?> list && current is IList destinationList)
                {destinationList.Clear();foreach(var entry in list)destinationList.Add(entry);}
                else if(row.Value is Fields nested)nested.Restore(current);
                else if(!row.Field.IsInitOnly)row.Field.SetValue(target,row.Value);
            }
        }
    }
    private sealed class Node
    {
        private readonly int _id;
        private Fields[] _state=Array.Empty<Fields>();
        internal Node(int id)
        {
            _id=id;Proof.Configure(id,id==1);RemoteMapStory.Fresh();RemoteStorySync.Fresh();SharedWindowMoves.Reset();
            SharedWindowMoves.Bind(()=>true);MapRoomDriver.Active=true;
            Proof.Snap(SharedWindows.Grabs[Kind],Proof.HostPose.Position,Proof.HostPose.Rotation,1);
            RemoteMapStory.SetNativeKey(Kind);RemoteMapStory.Publish(Kind);Time.frameCount+=3;RemoteMapStory.Publish(Kind);Save();
        }
        private void Save(){_state=new Fields[Types.Length];for(int i=0;i<Types.Length;i++)_state[i]=new Fields(Types[i],null);}
        internal void Do(Action action){for(int i=0;i<Types.Length;i++)_state[i].Restore(null);action();Save();}
        internal PresenceState Publish()
        {
            PresenceState result=default;Do(()=>{
                var entry=RemoteMapStory.Publish(Kind);
                result=new PresenceState{HasSharedWindow=true,SharedWindowEntries=new[]{entry},SharedWindowCount=1};
                result.HasMapRoom=true;result.MapRoomFlags=(byte)(NetProtocol.MapRoomInRoomBit|(_id==1?NetProtocol.MapRoomHostBit:0));
                RemoteMapStory.SampleMotionForProof(ref result);
                SharedWindowSpawnAuthority.SampleContext(ref result);SharedWindowMoves.Sample(ref result);
                if(result.WindowMoves!=null)
                {
                    var bytes=new byte[183];int at=0;
                    Proof.Check(SharedWindowMoveOrder.Write(bytes,ref at,result.WindowMoves)
                        &&SharedWindowMoveOrder.TryRead(bytes,2,bytes[1],out result.WindowMoves),"paired packet executes actual123 writer and reader");
                }
                result.WindowPoseContext=Proof.Wire(result.WindowPoseContext);result.WindowScales=Proof.WireScales(result.WindowScales);
            });return result;
        }
        internal void Receive(int sender,PresenceState packet,float delay=0)
        {Do(()=>{Time.unscaledTime+=delay;Time.frameCount++;SharedWindowMoves.Observe(sender,in packet);RemoteMapStory.Observe(sender,in packet);RemoteMapStory.Apply(Kind);});}
        internal void Grab(Vector3 position)
        {Do(()=>{Proof.Check(SharedWindowInteractionBridge.CanBegin!(Kind),"actual shared admission grants a fresh gesture for node "+_id);SharedWindowInteractionBridge.Began!(Kind);var grab=SharedWindows.Grabs[Kind];grab.IsGrabbed=true;grab.Root.position=position;});}
        internal void Move(Vector3 position){Do(()=>SharedWindows.Grabs[Kind].Root.position=position);}
        internal void Drop(){Do(()=>{SharedWindows.Grabs[Kind].IsGrabbed=false;SharedWindowInteractionBridge.Released!(Kind);});}
        internal void Settle(){Do(()=>{Time.unscaledTime+=.3f;Time.frameCount+=3;RemoteMapStory.Apply(Kind);});}
        internal Vector3 Position(){Vector3 p=default;Do(()=>p=SharedWindows.Grabs[Kind].Root.position);return p;}
        internal void Automatic(Vector3 position)
        {Do(()=>{RemoteMapStory.CanArrangeSharedWindows();Time.unscaledTime+=.4f;Time.frameCount+=4;
            Proof.Check(RemoteMapStory.BeginSharedReflow(Kind),"released manual pose still permits later opening-time room making");
            SharedWindows.Grabs[Kind].Root.position=position;});}
        internal bool Reflow(){bool active=false;Do(()=>active=RemoteMapStory.ReflowForProof(Kind));return active;}
        internal void EndAutomatic(){Do(()=>RemoteMapStory.EndSharedReflow(Kind));}
        internal bool MayArrange(){bool allowed=false;Do(()=>{RemoteMapStory.CanArrangeSharedWindows();Time.unscaledTime+=.4f;allowed=RemoteMapStory.CanArrangeSharedWindows();});return allowed;}
        internal void NewNativeOpening(uint key)
        {Do(()=>{SharedWindowMoves.Close(Kind);RemoteMapStory.CurrentQuest=new UIQuestPopup{Key=key};RemoteMapStory.SetNativeKey(Kind);
            RemoteMapStory.Publish(Kind);Time.frameCount+=3;RemoteMapStory.Publish(Kind);});}
    }
    internal static void Run()
    {
        for(int schedule=0;schedule<6;schedule++)
        {
            var a=new Node(1);var b=new Node(2);var c=new Node(3);var initial=a.Publish();
            b.Receive(1,initial);b.Settle();c.Receive(1,initial);c.Settle();
            a.Grab(new Vector3(1,3,5));b.Grab(new Vector3(7,11,13));
            var first=a.Publish();var second=b.Publish();c.Receive(2,second);
            if((schedule&1)==0){a.Receive(2,second,5);b.Receive(1,first,.01f);}
            else {b.Receive(1,first,5);a.Receive(2,second,.01f);}
            a.Settle();b.Settle();
            Proof.Check(Proof.Same(a.Position(),b.Position()),"crossed simultaneous real samplers and resolvers converge despite opposite arrival clocks");
            a.Drop();var loserDrop=a.Publish();b.Receive(1,loserDrop);
            b.Move(new Vector3(41,43,47));var newerHeld=b.Publish();a.Receive(2,newerHeld);c.Receive(2,newerHeld);
            b.Drop();var earlyRelease=b.Publish();a.Receive(2,earlyRelease);
            b.Move(new Vector3(53,59,61));var released=b.Publish();a.Receive(2,released);a.Settle();
            a.Receive(2,earlyRelease);a.Settle();
            Proof.Check(Proof.Same(a.Position(),b.Position()),"reordered release tween samples never rewind the final shared pose");
            // A duplicate and a delayed held sample must not undo the accepted release.
            a.Receive(2,second);a.Receive(2,released);a.Settle();b.Settle();
            Proof.Check(Proof.Same(a.Position(),b.Position()),"losing drop, duplicate and reordered held cannot take a released shared pose back");
            a.Automatic(new Vector3(17,19,23));var automatic=a.Publish();
            a.Receive(2,second);Proof.Check(a.Reflow(),"stale rejected77 held and byte-stamp cannot cancel a later accepted real reflow");
            // The third real receiver sees the causally later Auto before its parent's Release.
            c.Receive(1,automatic);c.Settle();c.Receive(2,released);c.Settle();
            b.Receive(1,automatic);b.Settle();a.Settle();
            Proof.Check(Proof.Same(a.Position(),c.Position()),"auto carries its observed release basis even when that release arrives later");
            Proof.Check(Proof.Same(a.Position(),b.Position()),"actual post-manual room making converges through123 and the live resolver");a.EndAutomatic();
            b.Grab(new Vector3(29,31,37));var regrab=b.Publish();a.Receive(2,regrab);
            a.Receive(2,released);b.Receive(1,automatic);a.Settle();b.Settle();
            Proof.Check(Proof.Same(a.Position(),b.Position()),"fresh regrab outranks delayed automatic motion and the preceding release");
        }
        AutomaticCrossing();LiveHeldScope();RegisterEdges();
    }
    private static void AutomaticCrossing()
    {
        var a=new Node(1);var b=new Node(2);var initial=a.Publish();b.Receive(1,initial);b.Settle();
        b.Grab(new Vector3(11,13,17));b.Drop();var priorRelease=b.Publish();a.Receive(2,priorRelease);a.Settle();
        a.Automatic(new Vector3(19,23,29));var firstAuto=a.Publish();a.EndAutomatic();
        a.Automatic(new Vector3(31,37,41));var secondAuto=a.Publish();
        // Neither automatic operation has reached the other player. This genuine new grip
        // is causally after the manual release, although its own counter is below Auto's.
        var destination=new Vector3(43,47,53);b.Grab(destination);var fresh=b.Publish();
        Proof.Check(secondAuto.WindowMoves!.TryGet(3,303,out var automatic)&&fresh.WindowMoves!.TryGet(3,303,out var manual)
            &&automatic.Claim>manual.Claim,"crossed reflow control really has a higher automatic local clock");
        a.Receive(2,fresh);b.Receive(1,secondAuto);b.Receive(1,firstAuto);a.Receive(2,priorRelease);a.Settle();b.Settle();
        Proof.Check(Proof.Same(a.Position(),destination)&&Proof.Same(b.Position(),destination),
            "fresh manual claim outranks two delayed automatic operations on an older release basis");
        Proof.Check(!a.Reflow(),"a genuine newer manual winner interrupts the existing real reflow");
    }
    private static void LiveHeldScope()
    {
        var a=new Node(1);var b=new Node(2);var c=new Node(3);var initial=a.Publish();b.Receive(1,initial);c.Receive(1,initial);
        b.Grab(new Vector3(3,5,7));var oldHolder=b.Publish();a.Receive(2,oldHolder);
        c.Grab(new Vector3(11,13,17));c.Drop();var newRelease=c.Publish();a.Receive(3,newRelease);a.Settle();
        Proof.Check(a.MayArrange(),"old holder cannot block room making after another accepted releaser without another old-holder packet");
        // Reproduce a fresh cached Held receipt, then replace the native opening without any
        // further packet from that sender. The new opening must not inherit its old grip mask.
        var d=new Node(1);var e=new Node(2);initial=d.Publish();e.Receive(1,initial);
        e.Grab(new Vector3(19,23,29));d.Receive(2,e.Publish());d.NewNativeOpening(909);
        Proof.Check(d.MayArrange(),"closed or replaced native opening cannot inherit a cached77 held mask");
    }
    private static void RegisterEdges()
    {
        for(byte kind=1;kind<=6;kind++)
        {
            var a=new SharedWindowMoveRegister(1,kind);var b=new SharedWindowMoveRegister(2,kind);
            a.Open(99,1);b.Open(99,1);a.Sample(out var opening);
            var forged=opening;forged.OpeningAuthor=3;
            Proof.Check(!b.Observe(1,in forged,1,0),"placement sender cannot invent a different opening author");
            b.Observe(1,in opening,1,0);
            for(int i=0;i<260;i++)
            {
                Proof.Check(a.Begin(i*2),"genuine later gesture advances past the observed peer clock");a.Sample(out var held);
                b.Observe(1,in held,1,i*2);a.Release();a.Sample(out var release);b.Observe(1,in release,1,i*2+.1f);
                Proof.Check(b.Begin(i*2+.2f),"released peer never remains a permanent high-counter winner");b.Sample(out var newer);
                a.Observe(2,in newer,1,i*2+.2f);b.Release();b.Sample(out var done);a.Observe(2,in done,1,i*2+.3f);
            }
            a.Close();b.Close();a.Open(99,1);b.Open(99,1);a.Sample(out var reopened);b.Observe(1,in reopened,1,600);
            Proof.Check(!b.Observe(1,in opening,1,601),"same-key native reopening rejects a delayed previous opening");
            Proof.Check(b.Begin(602),"new opening has independent gesture ownership");b.Sample(out var fresh);
            a.Observe(2,in fresh,1,602);b.Release();b.Sample(out var lateRelease);
            Proof.Check(a.Begin(606),"owner loss permits a new grip after the bounded stationary lease");a.Sample(out var takeover);
            b.Observe(1,in takeover,1,606);a.Observe(2,in lateRelease,1,607);
            Proof.Check(a.LocalWins&&b.Winner==1,"owner-loss takeover and late prior release converge");
        }
        var wrapA=new SharedWindowMoveRegister(1,3);var wrapB=new SharedWindowMoveRegister(2,3);
        wrapA.Open(88,1);wrapB.Open(88,1);wrapA.Sample(out var start);wrapB.Observe(1,in start,1,0);
        var high=start;high.Phase=SharedWindowMovePhase.Held;high.Claim=uint.MaxValue-1;high.Sequence=uint.MaxValue;
        wrapA.Observe(2,in high,1,0);Proof.Check(wrapA.Begin(4),"real expired grip advances a near-wrap claim clock");
        wrapA.Release();wrapA.Sample(out var end);wrapB.Observe(1,in end,1,4);
        Proof.Check(wrapB.Begin(5),"real second controller may regrab through clock wrap");wrapB.Sample(out var wrapped);
        wrapA.Observe(2,in wrapped,1,5);
        Proof.Check(wrapped.Claim==1&&wrapA.Winner==2&&wrapB.LocalWins,"actual controller register accepts the wrapped new gesture and sample");
        Proof.Check(SharedWindowMoveRegister.Newer(1,uint.MaxValue)&&!SharedWindowMoveRegister.Newer(uint.MaxValue,1),
            "wide sample and claim comparison survives unsigned wrap");
        Proof.Check(SharedWindowMoveRegister.Compare(0,0,uint.MaxValue,2)<0,"zero automatic basis stays older across wrap");
    }
}
