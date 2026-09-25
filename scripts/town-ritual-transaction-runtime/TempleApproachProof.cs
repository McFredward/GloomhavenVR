using System;
using System.Reflection;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object=UnityEngine.Object;

public enum EGuildmasterMode { None, Merchant, Temple, Enchantress }
public static class GuildmasterDestinations
{
    public static EGuildmasterMode Mode;
    public static EGuildmasterMode CurrentDestinationMode()=>Mode;
}
public static class MapRoomDriver
{
    public static bool Active=true;
    public static int TemplePresses;
    public static bool CanVisitTownService(EGuildmasterMode mode)=>true;
    public static void PressGuildmasterMode(EGuildmasterMode mode,string reason)
    { if(mode==EGuildmasterMode.Temple)TemplePresses++;ModeEntered=mode; }
    public static EGuildmasterMode ModeEntered;
}
public sealed class ToggleFlag { public bool Value=true; }
public static class WorldUIConfig { public static ToggleFlag ImmersiveTownServices=new(); }
public static class TownServiceEnhancementHandoff { public static bool Enabled=true; }
public static class StoryComposite { public static bool PointOfNoReturn; }
public static class VRHands
{
    public static FakeHand? Left,Right;
}
public sealed class FakeHand { public FakeGrabber Grabber=new(); }
public sealed class FakeGrabber { public object? Held; }
public sealed class VRCard { }
public sealed class TownServiceStation
{
    public Transform Root=null!;
    public bool Near=true;
    public bool IsLocalVisitorNear(bool alreadyNear)=>Near;
}
public static class TownServicePopulation
{
    public static TownServiceStation? Station;
    public static bool Available(int service)=>Station!=null;
    public static TownServiceStation? Acquire(int service)=>Station;
}
namespace GloomhavenVR.Core.Events
{
    public enum VRMode { TableIdle, ModalUI }
    public static class VRModeStateMachine { public static VRMode CurrentMode=VRMode.TableIdle; }
}
internal static class TempleApproachProof
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        var root=new GameObject("Priestess station");
        var head=new GameObject("Approaching head",typeof(Camera));
        head.transform.position=root.transform.position;
        VRRigDriver.HeadCamera=head.GetComponent<Camera>();
        TownServicePopulation.Station=new TownServiceStation{Root=root.transform};
        VRHands.Left=new FakeHand();VRHands.Right=new FakeHand();
        MapRoomHand.Selected=new FakeCharacter();
        MapRoomDriver.TemplePresses=0;
        GuildmasterDestinations.Mode=EGuildmasterMode.Merchant;
        typeof(BoundTempleApproach).GetField("_approachInside",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,false);
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==0,"merchant owns native destination while player is in priestess radius");
        GuildmasterDestinations.Mode=EGuildmasterMode.None;
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==1,"merchant departure reopens priestess without leaving her radius");
        GuildmasterDestinations.Mode=EGuildmasterMode.Temple;
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==1,"active priestess does not repeatedly press its native toggle");

        // The resident's attention/exit radius is deliberately larger than the physical approach
        // core. Leaving only the 1.4 m core must rearm a later visit; the old 1.65 m latch stayed
        // armed while walking between the closely spaced stands and suppressed every later purse.
        GuildmasterDestinations.Mode=EGuildmasterMode.None;
        head.transform.position=root.transform.TransformPoint(Vector3.forward*1.5f);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==1,"leaving approach core does not reopen while still outside it");
        head.transform.position=root.transform.TransformPoint(Vector3.forward*1.3f);
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==2,"return from larger attention radius creates a fresh priestess approach");

        GuildmasterDestinations.Mode=EGuildmasterMode.Enchantress;
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==2,"enchantress context cannot retain the temple approach latch");
        GuildmasterDestinations.Mode=EGuildmasterMode.None;
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==3,"return from enchantress opens one fresh temple session");
        TownServicePopulation.Station.Near=false;
        BoundTempleApproach.TickApproach();
        TownServicePopulation.Station.Near=true;
        head.transform.position=root.transform.position;
        GuildmasterDestinations.Mode=EGuildmasterMode.None;
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==4,"leaving and returning permits a fresh physical approach");
        Object.DestroyImmediate(root);Object.DestroyImmediate(head);
        TownServicePopulation.Station=null;VRRigDriver.HeadCamera=null;MapRoomHand.Selected=null;
        return checks;
    }
}
