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
    public static bool LastSuppressed;
    public static bool CanVisitTownService(EGuildmasterMode mode)=>true;
    public static void PressGuildmasterMode(EGuildmasterMode mode,string reason, bool suppressNativeSound = false)
    {
        if(mode==EGuildmasterMode.Temple)
        {
            TemplePresses++;
            // UITempleWindow.EnterTemple enables selection mode, which immediately chooses
            // the first assigned slot whenever the preceding mode cleared the tab.
            NewPartyDisplayUI.PartyDisplay!.SelectFirst();
        }
        ModeEntered=mode;LastSuppressed=suppressNativeSound;
    }
    public static EGuildmasterMode ModeEntered;
}
public sealed class ToggleFlag { public bool Value=true; }
public static class WorldUIConfig { public static ToggleFlag ImmersiveTownServices=new(); }
public static class TownServiceEnhancementHandoff
{
    public static bool Enabled=true,HasCurrentOffering;
}
public static class TownServiceGrantSync { public static bool CanUseImmersive=true; }
public static class TownServiceMerchantHandoff { public static bool WantsOffering; }
public static class StoryComposite { public static bool PointOfNoReturn; }
public static class VRHands
{
    public static FakeHand? Left,Right,Primary;
}
public sealed class FakeHand
{ public FakeGrabber Grabber=new(); public FakePalmGate PalmGate=new();public FakeRig Rig=new();public bool HasPose=true; }
public sealed class FakePalmGate { public bool IsOpen; }
public sealed class FakeRig { public Transform PalmCenter=new GameObject("PalmCenter").transform; }
public sealed class FakeGrabber { public object? Held; }
public sealed class VRCard { }
public static class CardsConfig { public static bool RevealAlways; }
public static class TownServiceRitualLayout { public static readonly Vector3 Origin=Vector3.zero; }
public static class TownServiceTempleBowl { public static readonly Vector3 Center=new(0f,.16f,.26f); }
public enum PartySlotState { Empty, Assigned }
public sealed class NewPartyCharacterUI
{
    public PartySlotState State=PartySlotState.Assigned;
    public FakeCharacter Data=null!;
    public void OnClick(){NewPartyDisplayUI.PartyDisplay!.SelectedUISlot=this;MapRoomHand.Selected=Data;}
}
public sealed class NewPartyDisplayUI
{
    public static NewPartyDisplayUI? PartyDisplay;
    public NewPartyCharacterUI? SelectedUISlot;
    public NewPartyCharacterUI[] Slots=Array.Empty<NewPartyCharacterUI>();
    public void SelectFirst(){if(Slots.Length>0){SelectedUISlot=Slots[0];MapRoomHand.Selected=Slots[0].Data;}}
}
public sealed class TownServiceStation
{
    public Transform Root=null!;
    public bool Near=true;
    public bool IsLocalVisitorNear(bool alreadyNear)=>Near;
}
public static class TownServicePopulation
{
    public static TownServiceStation? Station;
    public static TownServiceStation? MerchantStation;
    public static TownServiceStation? EnchantressStation;
    public static bool Available(int service)=>Acquire(service)!=null;
    public static TownServiceStation? Acquire(int service)=>service==1?MerchantStation:service==3?EnchantressStation:Station;
}
public static class TownServicePresentation
{ public static TownServiceRitual? Ritual; }
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
        VRHands.Left=new FakeHand();VRHands.Right=new FakeHand();VRHands.Primary=VRHands.Left;
        VRHands.Right.PalmGate.IsOpen=true;
        VRHands.Right.Rig.PalmCenter.position=root.transform.TransformPoint(TownServiceTempleBowl.Center);
        var first=new NewPartyCharacterUI{Data=new FakeCharacter{CharacterID="first"}};
        var selected=new NewPartyCharacterUI{Data=new FakeCharacter{CharacterID="selected"}};
        NewPartyDisplayUI.PartyDisplay=new NewPartyDisplayUI{Slots=new[]{first,selected},SelectedUISlot=selected};
        MapRoomHand.Selected=selected.Data;
        MapRoomDriver.TemplePresses=0;
        GuildmasterDestinations.Mode=EGuildmasterMode.Merchant;
        typeof(BoundTempleApproach).GetField("_approachInside",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,true);
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode=GloomhavenVR.Core.Events.VRMode.ModalUI;
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==0,"merchant confirmation blocks a physical priestess switch");
        GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode=GloomhavenVR.Core.Events.VRMode.TableIdle;
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==1,"blocked foreign service cannot preserve a stale temple latch");
        Check(ReferenceEquals(NewPartyDisplayUI.PartyDisplay.SelectedUISlot,selected),"temple entry preserves the exact previously selected native slot");
        Check(MapRoomDriver.LastSuppressed,"automatic priestess entry suppresses the flat button sound");
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
        Check(ReferenceEquals(NewPartyDisplayUI.PartyDisplay.SelectedUISlot,selected),"repeated temple entry cannot fall back to the first native slot");

        var enchantressRoot=new GameObject("Enchantress competitor");
        enchantressRoot.transform.position=root.transform.position+Vector3.forward*.8f;
        TownServicePopulation.EnchantressStation=new TownServiceStation{Root=enchantressRoot.transform};
        GuildmasterDestinations.Mode=EGuildmasterMode.Enchantress;
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        VRHands.Right.Rig.PalmCenter.position=root.transform.TransformPoint(Vector3.forward*2f);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==2,
            "head-only overlap cannot steal the enchantress destination");
        VRHands.Right.Rig.PalmCenter.position=root.transform.TransformPoint(TownServiceTempleBowl.Center);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==3,"revealed offhand at bowl intentionally selects Temple from a foreign service");
        GuildmasterDestinations.Mode=EGuildmasterMode.Enchantress;
        TownServiceEnhancementHandoff.HasCurrentOffering=true;
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==3,"live enchantress offering is never displaced by temple approach");
        TownServiceEnhancementHandoff.HasCurrentOffering=false;
        TownServicePopulation.Station.Near=false;
        BoundTempleApproach.TickApproach();
        TownServicePopulation.Station.Near=true;
        head.transform.position=root.transform.position;
        GuildmasterDestinations.Mode=EGuildmasterMode.None;
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==4,"leaving and returning permits a fresh physical approach");
        var merchantRoot=new GameObject("Merchant counter");merchantRoot.transform.position=root.transform.position+Vector3.right*2f;
        TownServicePopulation.MerchantStation=new TownServiceStation{Root=merchantRoot.transform};
        GuildmasterDestinations.Mode=EGuildmasterMode.Temple;
        VRHands.Right!.Rig.PalmCenter.position=root.transform.TransformPoint(TownServiceTempleBowl.Center);
        Check(BoundTempleApproach.WantsPurseFocus,"revealed offhand at priestess bowl selects purse focus");
        VRHands.Right.Rig.PalmCenter.position=merchantRoot.transform.position;
        Check(!BoundTempleApproach.WantsPurseFocus,
            "moving free fan hand to merchant counter releases Temple focus inside overlapping head volumes");
        TownServicePresentation.Ritual=new TownServiceRitual{HasParkedTempleOffer=true};
        Check(BoundTempleApproach.WantsPurseFocus,
            "a parked purse retains Temple hand focus through its native confirmation");
        TownServicePresentation.Ritual=null;
        GuildmasterDestinations.Mode=EGuildmasterMode.Merchant;
        Check(!BoundTempleApproach.WantsPurseFocus,
            "mere nearby priestess head gaze does not replace the merchant item fan");
        // Build 587 peer: Merchant remains the native mode after its private fan has
        // reset on departure. Merely walking to the priestess must then open Temple;
        // the player cannot put a purse into a bowl before that native visit exists.
        head.transform.position=root.transform.position+Vector3.right*1.1f;
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==4 && GuildmasterDestinations.Mode==EGuildmasterMode.Merchant,
            "overlapping head near the merchant cannot steal its idle native destination");
        head.transform.position=root.transform.position;
        TownServiceMerchantHandoff.WantsOffering=true;
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==4 && GuildmasterDestinations.Mode==EGuildmasterMode.Merchant,
            "a parked merchant offer cannot be interrupted by walking to the priestess");
        TownServiceMerchantHandoff.WantsOffering=false;
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==5 && MapRoomDriver.ModeEntered==EGuildmasterMode.Temple,
            "nearest priestess opens native Temple from stale Merchant mode without a bowl-hand gesture");
        Check(ReferenceEquals(NewPartyDisplayUI.PartyDisplay.SelectedUISlot,selected),
            "walking from merchant to priestess preserves the selected character");
        GuildmasterDestinations.Mode=EGuildmasterMode.None;
        TownServiceGrantSync.CanUseImmersive=false;
        typeof(BoundTempleApproach).GetField("_approachInside",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,false);
        typeof(BoundTempleApproach).GetField("_approachAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
        Check(!BoundTempleApproach.WantsPurseFocus,
            "incompatible host cannot replace the original Temple window with a purse");
        BoundTempleApproach.TickApproach();
        Check(MapRoomDriver.TemplePresses==5,
            "incompatible host leaves the original Temple entry path available");
        TownServiceGrantSync.CanUseImmersive=true;
        Object.DestroyImmediate(root);Object.DestroyImmediate(head);
        Object.DestroyImmediate(merchantRoot);
        Object.DestroyImmediate(enchantressRoot);
        TownServicePopulation.Station=TownServicePopulation.MerchantStation=TownServicePopulation.EnchantressStation=null;VRRigDriver.HeadCamera=null;MapRoomHand.Selected=null;NewPartyDisplayUI.PartyDisplay=null;
        Object.DestroyImmediate(VRHands.Left!.Rig.PalmCenter.gameObject);Object.DestroyImmediate(VRHands.Right!.Rig.PalmCenter.gameObject);
        VRHands.Left=VRHands.Right=VRHands.Primary=null;
        return checks;
    }
}
