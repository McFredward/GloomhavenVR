using System;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition,string message){++_assertions;if(!condition)throw new InvalidOperationException("Shared city identity: "+message);}
    private static UIWindow Window(string name)=>new GameObject(name).AddComponent<UIWindow>();
    private sealed class Fixture
    {
        internal readonly UIWindow Ready=Window("Native ready"),Quest=Window("Quest proposal"),Event=Window("Event story");
        internal readonly UIWindow OtherReady=Window("Unrelated native ready"),Private=Window("Merchant"),MapStory=Window("Map story"),ScenarioStory=Window("Scenario story");
        internal readonly UIReadyToggle Toggle;
        internal readonly UIEventPanel Panel;
        internal Fixture()
        {
            MapCityEventReadyUp.Window=null;MapCityEventReadyUp.ContentKey=0;ModalFallback.Floated.Clear();
            FFSNetwork.IsOnline=true;MapRoomDriver.Active=true;SharedWindowIdentity.MapStoryHost=null;
            Singleton<StoryController>.Instance=new(){window=ScenarioStory};Singleton<MapStoryController>.Instance=new(){window=MapStory};
            NativeMapQuestSelection.ConfirmationWindow=Quest;RewardShowcase.Window=null;NativeVideoWindow.Window=null;
            Toggle=Ready.gameObject.AddComponent<UIReadyToggle>();OtherReady.gameObject.AddComponent<UIReadyToggle>();
            Panel=Event.gameObject.AddComponent<UIEventPanel>();Panel.eventData=new(){ID="city-event-original-yml-id"};Singleton<UIEventPanel>.Instance=Panel;
            Event.Hide();ModalFallback.Floated[Ready]=new();ModalFallback.Floated[Quest]=new();
        }
        internal void Propose(uint key,string localCaption)
        { MapCityEventReadyUp.Window=Ready;MapCityEventReadyUp.ContentKey=key;Toggle.Caption=localCaption;Ready.Show(); }
    }
    private static void Main()
    {
        var f=new Fixture();uint proposal=0x39a182u;
        f.Propose(proposal,"Draw City Event");
        Check(ReferenceEquals(SharedWindows.WindowOf(SharedWindowKind.Encounter),f.Ready),"city preflight is the encounter pose subject");
        Check(SharedWindows.KindOf(f.Ready)==SharedWindowKind.Encounter&&SharedWindows.IsShared(f.Ready),"exact city readiness participates as a shared map window");
        Check(ReferenceEquals(RemoteMapStory.OfferedWindow(),f.Ready),"transport resolves the same floated preflight window");
        Check(RemoteMapStory.KeyOf(f.Ready)==proposal,"city preflight retains exact native proposal key");
        Check(SharedWindows.KindOf(f.Event)==SharedWindowKind.None,"preflight and actual event never both own encounter identity");
        Check(SharedWindows.KindOf(f.OtherReady)==SharedWindowKind.None&&RemoteMapStory.KeyOf(f.OtherReady)==0,"another pooled-ready component cannot inherit city identity");
        Check(SharedWindows.KindOf(f.Private)==SharedWindowKind.None,"private map services stay outside shared set");
        Check(SharedWindows.KindOf(f.Quest)==SharedWindowKind.QuestConfirm&&ReferenceEquals(SharedWindows.WindowOf(SharedWindowKind.QuestConfirm),f.Quest),"old quest keeps its own separate identity");

        // Four native clients can translate captions independently. The actual
        // transport resolver must preserve the supplied native occurrence key.
        foreach(string caption in new[]{"Draw City Event","Stadtereignis ziehen","Annuler","市内イベント"})
        {
            f.Toggle.Caption=caption;Check(RemoteMapStory.KeyOf(f.Ready)==proposal,"proposal address is independent of participant language");
        }
        f.Propose(proposal+1,"Draw City Event");
        Check(RemoteMapStory.KeyOf(f.Ready)==proposal+1&&RemoteMapStory.KeyOf(f.Ready)!=proposal,"new proposal occurrence cannot reuse preceding pose key");
        f.Ready.Hide();Check(RemoteMapStory.OfferedWindow()==null,"hidden native preflight has no transport pose");
        f.Ready.Show();ModalFallback.Floated.Remove(f.Ready);Check(RemoteMapStory.OfferedWindow()==null,"unfloated native preflight has no transport pose");
        ModalFallback.Floated[f.Ready]=null;Check(RemoteMapStory.OfferedWindow()==null,"missing live grab cannot publish an encounter pose");
        ModalFallback.Floated[f.Ready]=new();

        // Native all-ready ends the public proposal before opening the full event.
        // Reuse the unchanged wire kind with a different exact source and content key.
        MapCityEventReadyUp.Window=null;MapCityEventReadyUp.ContentKey=0;f.Ready.Hide();f.Event.Show();ModalFallback.Floated[f.Event]=new();
        Check(SharedWindows.KindOf(f.Ready)==SharedWindowKind.None,"pooled ready relinquishes identity at native story transition");
        Check(SharedWindows.KindOf(f.Event)==SharedWindowKind.Encounter&&ReferenceEquals(SharedWindows.WindowOf(SharedWindowKind.Encounter),f.Event),"actual event becomes the sole encounter identity");
        Check(ReferenceEquals(RemoteMapStory.OfferedWindow(),f.Event),"transport follows actual native event successor");
        uint eventKey=RemoteMapStory.KeyOf(f.Event);Check(eventKey==NetProtocol.HashMapKey(f.Panel.eventData!.ID)&&eventKey!=0&&eventKey!=proposal,"actual event uses original content identity rather than old proposal");
        f.Panel.LocalisedTitle="City Event";uint first=RemoteMapStory.KeyOf(f.Event);f.Panel.LocalisedTitle="Stadtereignis";
        Check(first==RemoteMapStory.KeyOf(f.Event),"native event address uses untranslated original ID");
        f.Panel.eventData.ID="different-original-yml-id";Check(RemoteMapStory.KeyOf(f.Event)!=eventKey,"different native event cannot accept preceding content pose");
        f.Panel.eventData.ID="";Check(RemoteMapStory.KeyOf(f.Event)==0,"unidentified event cannot guess transport identity");
        f.Event.Hide();Check(RemoteMapStory.OfferedWindow()==null,"native event close retires transport subject");

        // The singleton can next become quest readiness, without lending the old
        // city identity to that component or replacing the quest's exact own window.
        f.Ready.Show();f.Toggle.Caption="Select quest";
        Check(SharedWindows.KindOf(f.Ready)==SharedWindowKind.None&&RemoteMapStory.KeyOf(f.Ready)==0,"quest reuse cannot inherit city proposal address");
        Check(SharedWindows.KindOf(f.Quest)==SharedWindowKind.QuestConfirm,"quest confirmation remains independently synchronized");
        f.Propose(proposal+2,"Draw City Event");
        Check(SharedWindows.KindOf(f.Ready)==SharedWindowKind.Encounter&&RemoteMapStory.KeyOf(f.Ready)==proposal+2,"subsequent city proposal recovers exact shared identity");

        MapRoomDriver.Active=false;Check(!SharedWindows.IsShared(f.Ready)&&!SharedWindows.ParticipatesHere(SharedWindowKind.QuestConfirm),"2D map retains native content without unsupported shared world pose");
        Check(SharedWindows.IsShared(f.ScenarioStory),"scenario story has no accidental map-room participation gate");
        MapRoomDriver.Active=true;FFSNetwork.IsOnline=false;
        Check(!SharedWindows.IsShared(f.Ready)&&!SharedWindows.IsShared(f.Quest)&&!SharedWindows.IsShared(f.ScenarioStory),"offline windows never advertise shared behavior");
        Check(f.Toggle.ReadyCalls==0,"identity and transport lookup never consent to a native offer");
        Check(f.Ready.NativeShowCalls==5&&f.Ready.NativeHideCalls==2,"identity lookup does not show or dismiss native windows");
        Check(SharedWindows.KindOf(null)==SharedWindowKind.None&&RemoteMapStory.KeyOf(null)==0,"absent native subjects fail closed");
        Console.WriteLine($"Shared city identity: {_assertions} assertions; production shared-window and encounter-address methods passed. No Photon/headset claim.");
    }
}
