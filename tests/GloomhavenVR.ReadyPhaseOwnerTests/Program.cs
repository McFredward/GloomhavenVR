using System;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition,string message){++_assertions;if(!condition)throw new InvalidOperationException("Ready phase owner: "+message);}
    private static UIWindow Window(string name,Transform? parent=null)
    { var go=new GameObject(name);go.transform.SetParent(parent,false);return go.AddComponent<UIWindow>(); }
    private sealed class Fixture
    {
        internal readonly UIWindow Canvas,Quest,Character,Ready;
        internal readonly UIReadyToggle Toggle;
        internal readonly UILoadoutManager Loadout;
        internal Fixture()
        {
            LoadoutConfirmPark.Fresh();ModalFallback.Fresh();MapRoomDriver.Active=true;FFSNetwork.IsOnline=true;
            Canvas=Window("Native canvas");Quest=Window("Older quest card");Quest.gameObject.AddComponent<UIQuestPopup>();
            Character=Window("Character window");ModalFallback.Float(Quest);ModalFallback.Float(Character);
            Ready=Window("Native pooled ready",Canvas.transform);Toggle=Ready.gameObject.AddComponent<UIReadyToggle>();Toggle.window=Ready;Toggle.Interactable=true;
            Ready.transform.localScale=new(.75f,.75f,.75f);((RectTransform)Ready.transform).anchoredPosition=new(12,34);
            Singleton<UIReadyToggle>.Instance=Toggle;
            Loadout=Window("Old loadout still open").gameObject.AddComponent<UILoadoutManager>();Loadout.IsOpen=true;
            Loadout.confirmationButton=new GameObject("Offline confirm").AddComponent<Button>();Loadout.confirmationButton.transform.SetParent(Canvas.transform,false);
            LoadoutConfirmPark.NativeManager=Loadout;LoadoutConfirmPark.NativeHost=Character;
        }
        internal void Phase(EReadyUpToggleStates phase){Toggle.readyUpToggleState=phase;Ready.Show();}
    }
    private static void Main()
    {
        var f=new Fixture();
        foreach(EReadyUpToggleStates phase in Enum.GetValues<EReadyUpToggleStates>())
        {
            f.Phase(phase);bool grouped=ModalFallback.Group(f.Ready,out UIWindow? leader);
            Check(grouped==(phase==EReadyUpToggleStates.Quests),"only Quests groups with a visible quest card");
            Check(!grouped||ReferenceEquals(leader,f.Quest),"quest readiness uses exact live quest leader");
        }
        f.Phase(EReadyUpToggleStates.Quests);MapRoomDriver.Active=false;
        Check(!ModalFallback.Group(f.Ready,out _),"2D map has no absent quest parker suppression");MapRoomDriver.Active=true;
        ModalFallback.Converted[0].UserClosing=true;
        Check(!ModalFallback.Group(f.Ready,out _),"closing quest host cannot suppress ready control");
        ModalFallback.Converted[0].UserClosing=false;ModalFallback.Converted[0].Panel.IsAlive=false;
        Check(!ModalFallback.Group(f.Ready,out _),"dead quest host cannot suppress ready control");

        // An old loadout manager can remain open while the singleton is reinitialized
        // for another map phase. Execute the actual complete TickCore for each case.
        foreach(EReadyUpToggleStates phase in Enum.GetValues<EReadyUpToggleStates>())
        {
            f=new Fixture();f.Phase(phase);LoadoutConfirmPark.Reconcile();
            bool expected=phase==EReadyUpToggleStates.NotSet;
            Check(ReferenceEquals(f.Ready.transform.parent,expected?f.Character.transform:f.Canvas.transform),"loadout cannot steal another native ready phase");
            Check(LoadoutConfirmPark.Claimed==expected,"only original loadout state holds loadout parking claim");
            Check(f.Toggle.Requests==0&&f.Toggle.Interactable,"presentation preserves manual native permission and votes");
            Check(f.Ready.NativeShowCalls==1&&f.Ready.NativeHideCalls==0,"ownership cannot show or dismiss a native offer");
        }

        f=new Fixture();f.Phase(EReadyUpToggleStates.NotSet);LoadoutConfirmPark.Reconcile();
        Check(ReferenceEquals(f.Ready.transform.parent,f.Character.transform),"loadout adopts its original ready control");
        f.Toggle.Press();Check(f.Toggle.Requests==1,"parked native control still reaches its original ready callback");
        f.Toggle.Interactable=false;LoadoutConfirmPark.Reconcile();f.Toggle.Press();
        Check(f.Toggle.Requests==1&&!f.Toggle.Interactable,"native disabled permission remains in force while parked");
        foreach(EReadyUpToggleStates phase in new[]{EReadyUpToggleStates.CityEvents,EReadyUpToggleStates.Quests,EReadyUpToggleStates.Rewards,EReadyUpToggleStates.Retirement})
        {
            f.Phase(phase);LoadoutConfirmPark.Reconcile();
            Check(ReferenceEquals(f.Ready.transform.parent,f.Canvas.transform)&&!LoadoutConfirmPark.Claimed,"native phase reuse restores original control before shared offer");
            Check(!LoadoutConfirmPark._standDown,"ordinary native phase reuse does not disable later loadout parking");
            f.Phase(EReadyUpToggleStates.NotSet);LoadoutConfirmPark.Reconcile();
            Check(ReferenceEquals(f.Ready.transform.parent,f.Character.transform),"return to original loadout works without leaving map");
        }

        for(int dying=0;dying<2;++dying)
        {
            f=new Fixture();f.Phase(EReadyUpToggleStates.CityEvents);
            ConvertedPanel previous=ModalFallback.Float(f.Ready,move:true,dying:dying==1);
            f.Phase(EReadyUpToggleStates.NotSet);LoadoutConfirmPark.Reconcile();
            Check(!previous.IsAlive,"old standalone ready capture retires before loadout adoption");
            Check(ReferenceEquals(LoadoutConfirmPark._home,f.Canvas.transform),"loadout home is native canvas, never preceding float host");
            for(int frame=0;frame<50;++frame)LoadoutConfirmPark.Reconcile();
            Check(ReferenceEquals(f.Ready.transform.parent,f.Character.transform)&&!LoadoutConfirmPark._standDown,"no old restoration callback removes adopted loadout control");
            LoadoutConfirmPark.NativeManager=null;LoadoutConfirmPark.Reconcile();
            Check(ReferenceEquals(f.Ready.transform.parent,f.Canvas.transform)&&f.Ready.transform.localScale.x==.75f,"loadout close restores original home scale and parent");
        }
        f=new Fixture();f.Phase(EReadyUpToggleStates.NotSet);
        ConvertedPanel closing=ModalFallback.Float(f.Ready,move:true,closing:true);LoadoutConfirmPark.Reconcile();
        Check(ReferenceEquals(f.Ready.transform.parent,closing.HostGo.transform),"user close retains native ownership until it finishes");
        Check(!LoadoutConfirmPark._standDown,"deferred handoff must retry rather than latch stand-down");
        ModalFallback.Converted[^1].UserClosing=false;LoadoutConfirmPark.Reconcile();
        Check(ReferenceEquals(f.Ready.transform.parent,f.Character.transform),"deferred handoff adopts control when previous close completes");

        f=new Fixture();f.Phase(EReadyUpToggleStates.CityEvents);FFSNetwork.IsOnline=false;LoadoutConfirmPark.Reconcile();
        Check(ReferenceEquals(f.Loadout.confirmationButton!.transform.parent,f.Character.transform),"offline loadout preserves ordinary confirmation button path");
        Check(ReferenceEquals(f.Ready.transform.parent,f.Canvas.transform),"offline loadout never takes pooled city ready control");
        LoadoutConfirmPark.NativeManager=null;LoadoutConfirmPark.Reconcile();
        Check(ReferenceEquals(f.Loadout.confirmationButton.transform.parent,f.Canvas.transform),"offline close restores native button home");
        Console.WriteLine($"Ready phase owner: {_assertions} assertions; production group selection and loadout TickCore/Park/Unpark passed. No headset/Photon claim.");
    }
}
