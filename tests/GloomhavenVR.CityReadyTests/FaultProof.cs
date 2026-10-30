using System;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;

internal static partial class Program
{
    private static void FaultCase(bool host=false,bool participant=true,bool capture=true)
    {
        Fresh(host,participant);
        foreach(string field in new[]{"_warned","_actionWarned"})
            typeof(MapCityEventReadyUp).GetField(field,BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,false);
        VRLog.Warnings.Clear();VRLog.WarningAttempts=0;
        if(capture){if(host)HostOffer();else ClientOffer();MapCityEventReadyUp.Tick();}
    }
    private static bool Continues(Action action)
    {try{action();return true;}catch(Exception){return false;}}
    private static void FaultProof()
    {
        foreach(string fault in new[]{"map-run","log-info","singleton","online","host"})
        {
            FaultCase(capture:false);Faults.Arm(fault);int continued=0;
            Check(Continues(()=>{ClientOffer();continued++;})&&continued==1,"native client proposal continues after failed city capture: "+fault);
            Check(Faults.Triggered==1,"client capture actually exercises failure: "+fault);
            Check(Ready.AutomaticVotes==0&&Ready.InputVotes==0,"failed city capture cannot cast a vote");
            Check(VRLog.WarningAttempts==1,"failed capture records one bounded diagnostic");
        }
        foreach(string fault in new[]{"my-player","action-counter","online","host"})
        {
            FaultCase(true,capture:false);Faults.Arm(fault);object?[] args={null};
            Check(Continues(()=>Hooks.Invoke("BeforeHostProposal",args)),"native host proposal prefix continues after failed counter probe");
            Check(Faults.Triggered==1&&(int)args[0]! == -1,"failed host prefix preserves unknown action provenance");
        }
        foreach(string fault in new[]{"map-state","map-phase","singleton","online","host","my-player","action-counter"})
        {
            FaultCase(true,capture:false);PlayerRegistry.MyPlayer!.state.LatestProcessedActionID=17;Faults.Arm(fault);
            Check(Continues(()=>Hooks.Invoke("AfterHostProposal",16)),"native host proposal postfix continues after failed native evidence: "+fault);
            Check(Faults.Triggered==1,"host postfix actually exercises failure: "+fault);
            Check(!MapCityEventReadyUp.Active,"failed host evidence cannot invent a shared occurrence");
        }
        foreach(string fault in new[]{"map-state","map-phase","online","singleton","room-active"})
        {
            FaultCase(participant:false);var native=Ready;Faults.Arm(fault);object?[] args={native,false};
            Check(Continues(()=>Hooks.Invoke("ObserveDisabledCityOffer",args)),"native visibility getter survives failed city observation");
            Check(Faults.Triggered==1&&!(bool)args[1]!,"failed city observation preserves original native getter result");
            Check(Ready.ShouldBeVisible,"unknown observation preserves its exact lease for a valid retry");
            Faults.Arm(fault);Check(!MapCityEventReadyUp.Active,"failed public city identity safely reports unavailable");
            Check(Faults.Triggered==1&&MapCityEventReadyUp.Active,"public identity failure is not a manufactured cancellation");
        }
        foreach(string fault in new[]{"map-state","map-phase","online","singleton","room-active","permission"})
        {
            FaultCase();var native=Ready;Faults.Arm(fault);bool accepted=true;
            Check(Continues(()=>accepted=native.InputToggle()),"native input dispatcher survives failed city permission probe");
            Check(Faults.Triggered==1&&!accepted&&Ready.InputVotes==0,"failed exact-city observer permission must deny input");
            Check(Ready.InputToggle()&&Ready.InputVotes==1,"a valid subsequent explicit city input keeps native permission");
        }
        FaultCase();Ready.readyUpToggleState=EReadyUpToggleStates.Quests;Faults.Arm("map-phase");
        Check(Ready.InputToggle()&&Faults.Triggered==0,"unrelated native readiness input never inherits a city probe");

        foreach(string fault in new[]{"map-state","map-phase","update-visibility","permission-refresh","show-request"})
        {
            FaultCase(capture:false);ClientOffer();Faults.Arm(fault);
            Check(Continues(MapCityEventReadyUp.Tick),"city presentation tick survives native failure: "+fault);
            Check(Faults.Triggered==1,"city presentation tick actually exercises failure: "+fault);
            MapCityEventReadyUp.Tick();Check(Ready.Window.IsOpen&&Ready.IsInteractable,"failed city presentation retries without replaying native consent");
            Check(Ready.InputVotes==0&&Ready.AutomaticVotes==0,"presentation retry sends no vote");
        }
        FaultCase();MapRoomDriver.Active=false;Faults.Arm("update-visibility");
        Check(Continues(MapCityEventReadyUp.Tick)&&Faults.Triggered==1,"map exit visibility recompute cannot escape the coordinator");
        FaultCase(capture:false);ClientOffer();Map.DuringPermissionRefresh=MapCityEventReadyUp.Reset;
        MapCityEventReadyUp.Tick();
        Check(!MapCityEventReadyUp.Active&&VRLog.WarningAttempts==0,"native permission cancellation retires lease without a stale follow-up write");

        foreach(string seam in new[]{"BeforeReadyReset","BeforeControllerReset","BeforeHostWithdrawal","BeforeHostCancellation","BeforeEventCard"})
        {
            FaultCase();Faults.Arm("update-visibility");int nativeBody=0;
            Check(Continues(()=>{Hooks.Invoke(seam);nativeBody++;})&&nativeBody==1,"native retirement continues after failed visibility recompute: "+seam);
            Check(Faults.Triggered==1&&!MapCityEventReadyUp.Active,"failed retirement forgets only mod lease before native recompute");
        }
        FaultCase();Faults.Arm("update-visibility");
        Check(Continues(Ready.Reset)&&Faults.Triggered==1,"original native Reset body still executes after failed mod recompute");
        Check(!Ready.Window.IsOpen&&!MapCityEventReadyUp.Active,"original native Reset completes its own visibility cleanup");
        FaultCase(true);Faults.Arm("host");
        Check(Continues(()=>Hooks.Invoke("BeforeCancelPreview"))&&Faults.Triggered==1,"native Cancel proceeds when host-role probe fails");
        Check(!MapCityEventReadyUp.Active,"uncertain native cancellation cannot be resurrected by the mod");

        foreach(string fault in new[]{"action-phase","online"})
        {
            FaultCase(capture:false);Faults.Arm(fault);object?[] args={false,UIReadyToggle.EReadyUpType.Participant,EReadyUpToggleStates.CityEvents};
            Check(Continues(()=>Hooks.Invoke("BeforeReadyInitialize",args)),"native Initialize survives failed city departure probe");
            Check(Faults.Triggered==1&&!(bool)args[0]!,"failed city departure probe preserves incoming caller option");
        }
        FaultCase(participant:false);Faults.Arm("map-phase","log-warn");object?[] visible={Ready,false};
        Check(Continues(()=>Hooks.Invoke("ObserveDisabledCityOffer",visible)),"failed city diagnostic logger cannot escape native getter");
        Check(Faults.Triggered==2&&!(bool)visible[1]!,"native result survives both city probe and diagnostic failure");
        Faults.Arm("map-phase");Hooks.Invoke("ObserveDisabledCityOffer",visible);
        Check(VRLog.WarningAttempts==1,"failed diagnostic logger is not retried each frame");
        FaultCase(capture:false);Faults.Arm("map-run","log-warn");
        Check(Continues(()=>ClientOffer())&&Faults.Triggered==2,"failed capture and logger cannot escape native network callback");
        FaultCase();Faults.Arm("component");
        Check(MapCityEventReadyUp.Window==null&&Faults.Triggered==1,"native window lookup failure stays inside presentation boundary");
        Check(MapCityEventReadyUp.Window!=null,"native window lookup retries without discarding valid occurrence");
    }
}
