using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;

internal static partial class Program
{
    private static void RoleProof()
    {
        Fresh();HostOffer();MapCityEventReadyUp.Tick();uint key=MapCityEventReadyUp.ContentKey;
        foreach(bool show in new[]{false,true})
        {
            Controller.ToggleReadyUpUI(show,EReadyUpToggleStates.Quests);
            Check(Controller.m_State==EReadyUpToggleStates.CityEvents&&Ready.Window.IsOpen,
                "incidental quest focus refresh cannot steal a native city proposal");
            Check(MapCityEventReadyUp.ContentKey==key,"quest focus refresh preserves exact city opening identity");
        }
        Controller.m_State=EReadyUpToggleStates.Quests;Ready._requestVisible=true;
        Check(MapCityEventReadyUp.Active,"mutable native presentation state is not a city withdrawal");
        MapCityEventReadyUp.Tick();
        Check(Controller.m_State==EReadyUpToggleStates.CityEvents&&MapCityEventReadyUp.ContentKey==key,
            "city reentry repairs controller role even when visibility request already true");
        Check(Ready.InputVotes==0&&Ready.AutomaticVotes==0,"city presentation role repair sends no vote");

        Ready._allPlayersReady=true;Ready.UpdateVisiblity();MapCityEventReadyUp.Tick();
        Check(!Ready.Window.IsOpen&&!MapCityEventReadyUp.Active,"native city ACK wait hides preflight without reopening it");
        Controller.ToggleReadyUpUI(true,EReadyUpToggleStates.Quests);
        Check(Controller.m_State==EReadyUpToggleStates.CityEvents,"native city role stays protected throughout ACK wait");
        Ready._allPlayersReady=false;MapCityEventReadyUp.Tick();
        Check(Ready.Window.IsOpen&&MapCityEventReadyUp.ContentKey==key,"native ACK interruption retains the same proposal without fabricated reopening");
        Ready.Reset();Controller.ToggleReadyUpUI(true,EReadyUpToggleStates.Quests);
        Check(Controller.m_State==EReadyUpToggleStates.Quests&&!MapCityEventReadyUp.Active,"native city Reset releases original quest presentation");

        Fresh();HostOffer();MapCityEventReadyUp.Tick();Ready.Initialize(EReadyUpToggleStates.Quests);
        Ready.IsInteractable=true;Controller.ToggleReadyUpUI(true,EReadyUpToggleStates.Quests);
        Check(Controller.m_State==EReadyUpToggleStates.Quests&&Ready.Window.IsOpen&&!MapCityEventReadyUp.Active,
            "genuine native quest initialization clears city role before its original show");
        Fresh();HostOffer();MapCityEventReadyUp.Tick();key=MapCityEventReadyUp.ContentKey;
        MapRoomDriver.Active=false;Controller.ToggleReadyUpUI(false,EReadyUpToggleStates.Quests);MapCityEventReadyUp.Tick();
        Check(Controller.m_State==EReadyUpToggleStates.Quests&&!Ready.Window.IsOpen,"2D map retains original native quest focus behavior");
        MapRoomDriver.Active=true;MapCityEventReadyUp.Tick();
        Check(Controller.m_State==EReadyUpToggleStates.CityEvents&&MapCityEventReadyUp.ContentKey==key,
            "3D map reentry repairs its retained native city proposal");
        MapRoomDriver.Active=false;Ready.Initialize(EReadyUpToggleStates.CityEvents);
        Controller.ToggleReadyUpUI(true,EReadyUpToggleStates.CityEvents);Ready.IsInteractable=true;Ready.InputToggle();
        MapRoomDriver.Active=true;MapCityEventReadyUp.Tick();
        Check(MapCityEventReadyUp.ContentKey==key&&Ready.InputVotes==1,
            "same native city reinitialization preserves original occurrence across participation flip");
        for(int i=0;i<4;i++)MapCityEventReadyUp.Tick();
        Check(Ready.InputVotes==1&&Ready.AutomaticVotes==0,"participation reentry cannot replay native city consent");
        Ready.Initialize(EReadyUpToggleStates.CityEvents);Controller.m_State=EReadyUpToggleStates.CityEvents;
        GloomhavenVR.Net.SharedMapRunIdentity.Key=8193;FFSNet.FFSNetwork.IsHost=false;ClientOffer(22);
        Check(MapCityEventReadyUp.ContentKey!=key,"new native city action replaces a retained same-role occurrence");
        foreach(EReadyUpToggleStates next in new[]{EReadyUpToggleStates.NotSet,EReadyUpToggleStates.Reward,EReadyUpToggleStates.Retirement,EReadyUpToggleStates.TownRecords})
        {
            Fresh();HostOffer();MapCityEventReadyUp.Tick();Ready.Initialize(next);Controller.ToggleReadyUpUI(true,next);
            Check(Controller.m_State==next&&!MapCityEventReadyUp.Active,"native non-city role handoff remains unchanged");
        }
        Fresh();HostOffer();MapCityEventReadyUp.Tick();Faults.Arm("map-phase");Controller.ToggleReadyUpUI(false,EReadyUpToggleStates.Quests);
        Check(Faults.Triggered==1&&Controller.m_State==EReadyUpToggleStates.CityEvents&&Ready.Window.IsOpen,
            "unknown native phase cannot let unrelated quest refresh steal city role");
    }
}
