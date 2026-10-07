using System;
using System.Reflection;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object=UnityEngine.Object;

public enum EGuildmasterMode { None, Merchant, Temple, Enchantress, WorldMap }
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
public static class ItemsPile { public sealed class ItemChip { } }
public static class CardsConfig { public static bool RevealAlways; }
public static class TownServiceRitualLayout { public static readonly Vector3 Origin=Vector3.zero; }
public static class TownServiceTempleBowl { public static readonly Vector3 Center=new(0f,.16f,.26f); }
public enum PartySlotState { Empty, Assigned }
public sealed class NewPartyCharacterUI
{
    public PartySlotState State=PartySlotState.Assigned;
    public FakeCharacter Data=null!;public FakeCharacter Service=>Data;
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
    public BoundTempleFace? Face;
    public bool IsLocalVisitorNear(bool alreadyNear)=>Face?.IsLocalVisitorNear(alreadyNear)??Near;
}
public sealed class TempleEyeBoundary
{
    public Transform Eye=null!;
    public Vector3 EyePosition=>Eye.position;
    public Quaternion OpticalRotation=>Eye.rotation;
}
namespace GloomhavenVR.Core { public static class VRLayers { public const int ModLayer=29; } }
public static class TownServicePopulation
{
    public static TownServiceStation? Station;
    public static TownServiceStation? MerchantStation;
    public static TownServiceStation? EnchantressStation;
    public static bool Available(int service)=>Acquire(service)!=null;
    public static TownServiceStation? Acquire(int service)=>service==1?MerchantStation:service==3?EnchantressStation:Station;
}
public static class TownServicePresentation
{
    public static TownServiceRitual? Ritual;public static UIWindow? QuietWindow;public static bool ThrowQuietGate;
    public static bool IsQuietTemple(UIWindow window)
    {if(ThrowQuietGate)throw new InvalidOperationException("injected quiet gate failure");return window!=null&&ReferenceEquals(window,QuietWindow);}
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
        VRRigDriver.HeadCamera=head.GetComponent<Camera>();
        TownServicePopulation.Station=new TownServiceStation{Root=root.transform};
        VRHands.Left=new FakeHand();VRHands.Right=new FakeHand();VRHands.Primary=VRHands.Left;
        var first=new NewPartyCharacterUI{Data=new FakeCharacter{CharacterID="first"}};
        var selected=new NewPartyCharacterUI{Data=new FakeCharacter{CharacterID="selected"}};
        NewPartyDisplayUI.PartyDisplay=new NewPartyDisplayUI{Slots=new[]{first,selected},SelectedUISlot=selected};
        MapRoomHand.Selected=selected.Data;
        MapRoomDriver.TemplePresses=0;
        // This is the paired-log failure: the native Temple closes while the priestess
        // still watches the visitor. Actual head geometry, not the current flat mode,
        // must retain the purse at 2.1m (the old exit threshold was only 1.65m).
        head.transform.position=root.transform.TransformPoint(Vector3.forward*2.1f);
        VRHands.Right.PalmGate.IsOpen=false;
        VRHands.Right.Rig.PalmCenter.position=head.transform.position;
        GuildmasterDestinations.Mode=EGuildmasterMode.Temple;
        BoundTempleApproach.TickApproach();
        Check(BoundTempleApproach.WantsPurseFocus && MapRoomHand.TempleInspection,
            "attention-range visitor retains purse when original temple closes");
        GuildmasterDestinations.Mode=EGuildmasterMode.WorldMap;
        BoundTempleApproach.TickApproach();
        Check(BoundTempleApproach.WantsPurseFocus && MapRoomHand.TempleInspection,
            "native five-frame close cannot restore the normal ability fan at priestess");
        GuildmasterDestinations.Mode=EGuildmasterMode.Enchantress;
        BoundTempleApproach.TickApproach();
        Check(MapRoomHand.TempleInspection && ReferenceEquals(MapRoomHand.Selected,selected.Data)
            && ReferenceEquals(NewPartyDisplayUI.PartyDisplay.SelectedUISlot,selected),
            "foreign native mode cannot switch the visitor's selected character or temple hand");
        Check(MapRoomDriver.TemplePresses==0,"temple inspection never opens the old native window");

        var merchant=new GameObject("Merchant counter");merchant.transform.position=Vector3.right*2f;
        TownServicePopulation.MerchantStation=new TownServiceStation{Root=merchant.transform};
        head.transform.position=merchant.transform.position+Vector3.forward*.6f;
        VRHands.Right.Rig.PalmCenter.position=merchant.transform.position;
        VRHands.Right.PalmGate.IsOpen=true;
        BoundTempleApproach.TickApproach();
        Check(!MapRoomHand.TempleInspection && !BoundTempleApproach.WantsPurseFocus,
            "nearer merchant retains the accepted item fan in overlapping attention volumes");
        TownServicePresentation.Ritual=new TownServiceRitual{HasTemplePurseInHand=true};
        Check(BoundTempleApproach.WantsPurseFocus,"actual held purse retains its independent temple context");
        TownServicePresentation.Ritual=null;
        head.transform.position=root.transform.position+Vector3.forward*1f;
        VRHands.Right.Rig.PalmCenter.position=head.transform.position;
        VRHands.Right.PalmGate.IsOpen=false;
        VRHands.Right.Grabber.Held=new ItemsPile.ItemChip();
        Check(!BoundTempleApproach.WantsPurseFocus,"held item prevents priestess from stealing merchant inspection");
        VRHands.Right.Grabber.Held=null;
        BoundTempleApproach.TickApproach();
        Check(MapRoomHand.TempleInspection,"returning from merchant immediately restores the purse context");

        // Bind the production eye-origin/optical-space attention gate, rather than the
        // old constant Near=true station stub. Another visitor owns the shared gaze;
        // this local wrist remains immediately usable on its very first reveal.
        var eye=new GameObject("Shared priestess eye");
        eye.transform.position=root.transform.position+Vector3.up*1.5f;
        eye.transform.rotation=Quaternion.Euler(0f,180f,0f);
        TownServicePopulation.Station!.Face=new BoundTempleFace(root.transform,new TempleEyeBoundary{Eye=eye.transform});
        head.transform.position=root.transform.position+new Vector3(0f,1.5f,1f);
        BoundTempleApproach.ResetFocus();
        MapRoomHand.TempleInspection=false;
        CardsDriver.OffScenarioFanCards=new object();CardsDriver.OffScenarioFanIsOpen=false;
        VRHands.Right.PalmGate.IsOpen=true;
        Check(!TownServicePopulation.Station.IsLocalVisitorNear(false),
            "production eye gate rejects local visitor when shared eye faces another visitor");
        BoundTempleApproach.TickApproach();
        Check(MapRoomHand.TempleInspection&&CardsDriver.OffScenarioFanCards==null,
            "first wrist reveal selects purse independently of another visitor's animated eye");
        Check(BoundTempleRenderedFocus.InspectionNear(),
            "rendered purse and approach use the same local focus despite shared eye rejection");
        int published=MapRoomHand.AbilityPublishes;
        using(var old=new BoundTempleWristCleanup())old.Dispose();
        Check(MapRoomHand.TempleInspection&&CardsDriver.OffScenarioFanCards==null
            &&MapRoomHand.AbilityPublishes==published,
            "ritual replacement cannot republish ability cards midway through first wrist reveal");
        CardsDriver.OffScenarioFanCards=new object();
        BoundTempleApproach.TickApproach();
        Check(CardsDriver.OffScenarioFanCards==null,
            "late native hand refresh is suppressed again while local purse focus remains active");
        MapRoomHand.Selected=null;BoundTempleApproach.TickApproach();
        Check(!MapRoomHand.TempleInspection,"unassigned online visitor has no private purse hand");
        MapRoomHand.Selected=selected.Data;BoundTempleApproach.TickApproach();
        Check(MapRoomHand.TempleInspection,"assigned character obtains purse on first following reveal");
        WorldUIConfig.ImmersiveTownServices.Value=false;
        using(var old=new BoundTempleWristCleanup())old.Dispose();
        Check(!MapRoomHand.TempleInspection&&CardsDriver.OffScenarioFanCards!=null,
            "disabling immersive NPCs restores normal ability fan during ritual cleanup");
        WorldUIConfig.ImmersiveTownServices.Value=true;BoundTempleApproach.TickApproach();
        Check(MapRoomHand.TempleInspection,"reenabling immersive NPCs elects purse without native window");
        Object.DestroyImmediate(eye);TownServicePopulation.Station.Face=null;

        head.transform.position=root.transform.position+Vector3.forward*2.8f;
        BoundTempleApproach.TickApproach();
        Check(!MapRoomHand.TempleInspection,"leaving priestess attention restores ordinary map hand");
        head.transform.position=root.transform.position+Vector3.forward*2.1f;
        BoundTempleApproach.TickApproach();
        Check(MapRoomHand.TempleInspection && MapRoomDriver.TemplePresses==0,
            "repeated priestess visits need no flat destination reopen or cooldown");
        StoryComposite.PointOfNoReturn=true;
        BoundTempleApproach.TickApproach();
        Check(!MapRoomHand.TempleInspection,"story commitment retires local purse inspection");
        StoryComposite.PointOfNoReturn=false;
        TownServiceGrantSync.CanUseImmersive=false;
        BoundTempleApproach.TickApproach();
        Check(!MapRoomHand.TempleInspection,"incompatible peer preserves the native fallback hand");
        TownServiceGrantSync.CanUseImmersive=true;
        Object.DestroyImmediate(root);Object.DestroyImmediate(head);Object.DestroyImmediate(merchant);
        TownServicePopulation.Station=TownServicePopulation.MerchantStation=TownServicePopulation.EnchantressStation=null;
        VRRigDriver.HeadCamera=null;MapRoomHand.Selected=null;NewPartyDisplayUI.PartyDisplay=null;
        Object.DestroyImmediate(VRHands.Left!.Rig.PalmCenter.gameObject);Object.DestroyImmediate(VRHands.Right!.Rig.PalmCenter.gameObject);
        VRHands.Left=VRHands.Right=VRHands.Primary=null;
        return checks + TempleQuietControllerProof.Run();
    }
}
