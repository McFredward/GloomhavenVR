using System;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value,string why) { _checks++;if(!value)throw new InvalidOperationException("Map ready handoff: "+why); }
    private static RectTransform Rect(string name,Transform? parent=null)
    { var rect=(RectTransform)new GameObject(name).transform;rect.SetParent(parent,false);rect.sizeDelta=new(350,90);return rect; }
    private sealed class Fixture
    {
        internal readonly RectTransform Home,Quest,Ready;
        internal readonly UIWindow QuestWindow,ReadyWindow;
        internal readonly UIReadyToggle Toggle;
        internal readonly Vector2 Anchors=new(.13f,.7f),Position=new(27,-12);
        internal Fixture(bool online=true)
        {
            MapTravelConfirm.Fresh();ModalFallback.Fresh();MapRoomDriver.Active=true;FFSNetwork.IsOnline=online;
            Home=Rect("Campaign canvas");MapTravelConfirm.NativeManager.travelOptions.transform.SetParent(Home,false);Quest=Rect("Quest card");QuestWindow=Quest.gameObject.AddComponent<UIWindow>();
            Ready=Rect("Pooled Multiplayer Ready Toggle",Home);ReadyWindow=Ready.gameObject.AddComponent<UIWindow>();Toggle=Ready.gameObject.AddComponent<UIReadyToggle>();Toggle.window=ReadyWindow;
            Ready.anchorMin=Anchors;Ready.anchorMax=Anchors;Ready.anchoredPosition=Position;
            Ready.localScale=new(.8f,.8f,.8f);Ready.localRotation=Quaternion.Euler(0,17,0);
            Singleton<UIReadyToggle>.Instance=Toggle;Time.unscaledTime+=1;
        }
        internal void Offer(EReadyUpToggleStates state,bool participant=true)
        { Toggle.Initialize(state,participant);if(participant){ReadyWindow.Show();if(!ModalFallback.OpenWindows.Contains(ReadyWindow))ModalFallback.OpenWindows.Add(ReadyWindow);} }
        internal void Tick() { MapTravelConfirm.Reconcile(QuestWindow);Time.unscaledTime+=.02f; }
        internal void AssertStable(string why)
        {
            Check(ReferenceEquals(Ready.parent,Quest),why);
            Check(!MapTravelConfirm._parkStandDown,"presentation must not disable future travel parking");
            Check(ReferenceEquals(MapTravelConfirm._optionsHome,Home),why+": home snapshot must use restored native canvas, never old modal host");
        }
    }
    private static void Main()
    {
        // Both native ordering possibilities: city readiness previously had its own
        // physical window; Quests reuses the exact singleton before the modal's next
        // release pass. This calls the full production Reconcile entry, Park and the
        // production composite-transfer kernel, then checks the following frames.
        for(int order=0;order<2;++order)
        {
            var f=new Fixture();f.Offer(EReadyUpToggleStates.CityEvents);
            ConvertedPanel old=ModalFallback.Float(f.ReadyWindow);
            f.Offer(EReadyUpToggleStates.Quests);
            if(order==0)ModalFallback.TickFloats();
            f.Tick();
            // The refusal callback occurs after map-room Update in the failing
            // schedule. It must have nothing left to restore once adopted.
            ModalFallback.TickFloats();
            for(int frame=0;frame<40;++frame)f.Tick();
            f.AssertStable("city-to-quest handoff remains parked after modal release");
            Check(!old.IsAlive&&CanvasConversion.ActivePanels.Count==0,"prior modal capture and fit writer retire synchronously");
            Check(f.Toggle.Requests==0,"presentation never readies or assigns a character");
            f.Toggle.Press();Check(f.Toggle.Requests==1,"native manual ready action still reaches exact offered callback");
            MapTravelConfirm.Reset();
            Check(ReferenceEquals(f.Ready.parent,f.Home),"room exit restores native parent, not removed modal host");
            Check(f.Ready.anchoredPosition.x==f.Position.x&&f.Ready.anchoredPosition.y==f.Position.y,"home anchors and offset survive transfer");
            Check(f.Ready.localScale.x==.8f,"native home scale survives transfer");
        }
        var current=new Fixture();current.Offer(EReadyUpToggleStates.CityEvents);
        var previous=ModalFallback.Float(current.ReadyWindow,dying:true);
        current.Offer(EReadyUpToggleStates.Quests);current.Tick();
        current.AssertStable("pending native-target dissolve retires before adoption");
        Check(!WindowMaterialise.Pending.ContainsKey(previous)&&!previous.IsAlive,"pending exact-target dissolve callback is cancelled safely");
        for(int frame=0;frame<40;++frame)current.Tick();
        current.AssertStable("no delayed callback takes adopted ready toggle out of quest");

        current=new Fixture();current.Offer(EReadyUpToggleStates.Quests);
        previous=ModalFallback.Float(current.ReadyWindow,userClosing:true);current.Tick();
        Check(ReferenceEquals(current.Ready.parent,previous.HostGo.transform),"real user close defers adoption instead of bypassing native cancellation");
        Check(!MapTravelConfirm._parkStandDown,"temporarily closing predecessor does not permanently disable parking");
        ModalFallback.Converted[0].UserClosing=false;current.Tick();current.AssertStable("same offer retries handoff after user-close ownership ends");

        foreach(var phase in new[]{EReadyUpToggleStates.CityEvents,EReadyUpToggleStates.Quests,EReadyUpToggleStates.Rewards,EReadyUpToggleStates.Retirement})
        {
            current=new Fixture();current.Offer(phase);previous=ModalFallback.Float(current.ReadyWindow);
            current.ReadyWindow.Hide();ModalFallback.OpenWindows.Clear();ModalFallback.TickFloats();
            Check(!previous.IsAlive&&ModalFallback.Converted.Count==0,"native ready Hide must not leave a sticky inert button");
            Check(current.Toggle.Requests==0,"native phase-end cleanup cannot choose a mandatory answer");
        }
        current=new Fixture();
        var destination=Rect("Ordinary merchant window").gameObject.AddComponent<UIWindow>();
        Check(ModalFallback.StickyFor(destination),"ordinary map destination still survives native sibling hide");

        // Native nonparticipant refusal: the video explicitly names an unassigned
        // player. A render repair must not invent ownership or reveal the button.
        current=new Fixture();current.Offer(EReadyUpToggleStates.Quests,participant:false);
        for(int frame=0;frame<40;++frame)current.Tick();
        Check(ReferenceEquals(current.Ready.parent,current.Home)&&!current.ReadyWindow.IsOpen&&current.Toggle.Requests==0,"unassigned player retains original native refusal");
        current.Offer(EReadyUpToggleStates.Quests);current.Tick();current.AssertStable("native assignment/late initialization reveals existing request without room rejoin");

        current=new Fixture();current.Offer(EReadyUpToggleStates.Quests);current.Tick();
        current.ReadyWindow.Hide();current.Tick();
        Check(ReferenceEquals(current.Ready.parent,current.Home),"native quest close immediately restores adopted toggle");
        current.Offer(EReadyUpToggleStates.Quests);current.Tick();current.AssertStable("next native proposal is reachable without rejoining");
        var next=Rect("Replacement quest").gameObject.AddComponent<UIWindow>();
        MapTravelConfirm.Reconcile(next);Check(ReferenceEquals(current.Ready.parent,next.transform),"new quest owner receives exact original toggle");
        MapTravelConfirm.Reconcile(null);Check(ReferenceEquals(current.Ready.parent,current.Home),"no quest owner restores original native parent");

        current=new Fixture(online:false);current.Tick();
        Check(ReferenceEquals(MapTravelConfirm.NativeManager.travelOptions.transform.parent,current.Quest),"offline travel keeps original options path");
        Check(ReferenceEquals(current.Ready.parent,current.Home),"offline path does not take unrelated native ready toggle");
        MapTravelConfirm.Reset();Check(ReferenceEquals(MapTravelConfirm.NativeManager.travelOptions.transform.parent,current.Home),"offline teardown restores native options home");
        MapTravelConfirm.Reset();
        System.Console.WriteLine($"Map ready handoff: {_checks} assertions; production reconciliation, native-target handoff and lifetime expressions passed. No headset/Photon outcome claimed.");
    }
}
