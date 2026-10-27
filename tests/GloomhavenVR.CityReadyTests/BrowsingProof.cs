using System;
using GloomhavenVR.WorldUI.MapRoom;

internal static partial class Program
{
    private static AdventureMapUIManager Adventure=>Singleton<AdventureMapUIManager>.Instance;
    private static void BrowsingProof()
    {
        Fresh(false);var marker=new MapLocation();Adventure.LocationToTravel=marker;ClientOffer();
        Check(MapCityEventReadyUp.OwnsMapDecision&&MapCityEventReadyUp.BlocksQuestBrowsing,"exact native city decision owns logical and 3D browsing input");
        var log=new QuestManager();log.OnSelectedLogQuest();marker.OnPointerClick();
        Check(log.Selections==0&&marker.Selections==0,"city proposal blocks both native user quest entry points");
        MapCityEventReadyUp.Tick();
        Check(Adventure.CancelCalls==1&&Adventure.LocationToTravel==null&&marker.Unselections==1,"city preflight clears only native unaccepted local browsing once");
        Check(Ready.Window.IsOpen&&Controller.ShowRequests==1,"native city presentation refreshes once after local browse cleanup");
        for(int i=0;i<5;i++)MapCityEventReadyUp.Tick();
        Check(Adventure.CancelCalls==1&&Controller.ShowRequests==1,"steady city presentation does not repeat native browse cleanup");
        Check(Ready.InputVotes==0&&Ready.AutomaticVotes==0,"browse cleanup never casts or withdraws a native vote");
        Ready._allPlayersReady=true;MapCityEventReadyUp.Tick();
        Check(!MapCityEventReadyUp.Active&&MapCityEventReadyUp.OwnsMapDecision&&MapCityEventReadyUp.BlocksQuestBrowsing,"hidden native ACK wait retains public input ownership");
        log.OnSelectedLogQuest();marker.OnPointerClick();Check(log.Selections==0&&marker.Selections==0,"ACK wait blocks new native private browsing");
        marker.Select();marker.Deselect();Check(marker.Selections==1&&marker.Unselections==2,"native scripted selection and cleanup remain unpatched");
        MapRoomDriver.Active=false;
        Check(MapCityEventReadyUp.OwnsMapDecision&&!MapCityEventReadyUp.BlocksQuestBrowsing,"logical city ownership survives 2D participation flip without changing 2D input");
        log.OnSelectedLogQuest();Check(log.Selections==1,"2D native quest input remains original");
        Ready.Reset();MapRoomDriver.Active=true;Check(!MapCityEventReadyUp.OwnsMapDecision&&!MapCityEventReadyUp.BlocksQuestBrowsing,"native city reset releases all browsing ownership");

        Fresh(false);marker=new();Adventure.LocationToTravel=marker;Controller.HostSelectedQuest=new();ClientOffer();MapCityEventReadyUp.Tick();
        Check(Adventure.CancelCalls==0&&ReferenceEquals(Adventure.LocationToTravel,marker),"city cleanup preserves a native host-selected quest");
        Fresh(false);marker=new();Adventure.LocationToTravel=marker;Adventure.IsLocked=true;ClientOffer();MapCityEventReadyUp.Tick();
        Check(Adventure.CancelCalls==0,"native map lock forbids cosmetic browsing cleanup");
        Adventure.IsLocked=false;MapCityEventReadyUp.Tick();Check(Adventure.CancelCalls==1,"native unlock can safely complete pending unaccepted browsing cleanup");
        Fresh(false);marker=new();Adventure.LocationToTravel=marker;Adventure.CanCancel=false;ClientOffer();MapCityEventReadyUp.Tick();MapCityEventReadyUp.Tick();
        Check(Adventure.CancelCalls==1&&ReferenceEquals(Adventure.LocationToTravel,marker),"original native cancellation permission is retained without repeated attempts");

        Fresh(false);marker=new();Adventure.LocationToTravel=marker;MapRoomDriver.Active=false;ClientOffer();MapCityEventReadyUp.Tick();
        Check(Adventure.CancelCalls==0&&Adventure.LocationToTravel!=null,"map-off city capture does not cancel private 2D browsing");
        MapRoomDriver.Active=true;MapCityEventReadyUp.Tick();Check(Adventure.CancelCalls==1&&Adventure.LocationToTravel==null,"3D reentry clears unaccepted private browsing before showing city readiness");

        Fresh(false);var story=new UIEventPanel();Singleton<UIEventPanel>.Instance=story;
        Check(!MapCityEventReadyUp.OwnsMapDecision,"empty native event singleton is not a public decision");
        story.eventData=new();Check(MapCityEventReadyUp.OwnsMapDecision,"native event content owns the decision independently of its window visibility");
        story.myWindow.IsOpen=true;Check(MapCityEventReadyUp.OwnsMapDecision&&MapCityEventReadyUp.BlocksQuestBrowsing,"exact native story window blocks new quest browsing");
        marker=new();Adventure.LocationToTravel=marker;ClientOffer();MapCityEventReadyUp.Tick();
        Check(Adventure.CancelCalls==0&&Adventure.LocationToTravel!=null,"live event never triggers automatic local cancel that could steal story focus");
        log=new();log.OnSelectedLogQuest();marker.OnPointerClick();Check(log.Selections==0&&marker.Selections==0,"actual native event blocks both native browse input callbacks");
        story.myWindow.IsOpen=false;story.myWindow.IsVisible=true;
        Check(MapCityEventReadyUp.OwnsMapDecision,"native event closing animation remains protected while visible");
        MapCityEventReadyUp.Reset();Check(MapCityEventReadyUp.OwnsMapDecision,"actual native event ownership does not depend on city preflight lease");
        story.myWindow.IsVisible=false;Check(MapCityEventReadyUp.OwnsMapDecision,"fully hidden native event retains ownership through reward distribution");
        story.eventData=null;Check(!MapCityEventReadyUp.OwnsMapDecision,"event completion releases ownership even if its old frame still fades");
        story.eventData=new();
        story.myWindow.IsVisible=true;MapRoomDriver.Active=false;
        Check(MapCityEventReadyUp.OwnsMapDecision&&!MapCityEventReadyUp.BlocksQuestBrowsing,"raw live event ownership remains mode independent");

        foreach(string fault in new[]{"host-proposal","browse-lock","browse-location","cancel-travel"})
        {
            FaultCase(capture:false);marker=new();Adventure.LocationToTravel=marker;ClientOffer();Faults.Arm(fault);
            Check(Continues(MapCityEventReadyUp.Tick)&&Faults.Triggered==1,"native city browsing cleanup fault stays inside presentation boundary: "+fault);
            Check(Ready.InputVotes==0&&Ready.AutomaticVotes==0,"failed browsing cleanup cannot alter native consent");
        }
        FaultCase();Faults.Arm("map-phase");
        Check(MapCityEventReadyUp.BlocksQuestBrowsing&&Faults.Triggered==1,"unknown exact city ownership cannot authorize private browsing");
        Fresh(false);story=new(){eventData=new()};Singleton<UIEventPanel>.Instance=story;story.myWindow.IsVisible=true;Faults.Arm("event-visible");
        Check(MapCityEventReadyUp.OwnsMapDecision&&Faults.Triggered==0,"native window visibility cannot revoke hidden reward ownership");
    }
}
