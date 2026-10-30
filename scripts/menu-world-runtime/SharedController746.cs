using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.Net
{
    internal static class NetProtocol { internal const byte ExtIdSharedWindowMoveOrder=123; }
    internal static class AvatarSerializer
    {
        internal static void WriteU32(byte[] b,ref int at,uint v){for(int i=0;i<4;i++)b[at++]=(byte)(v>>(i*8));}
        internal static uint ReadU32(byte[] b,ref int at){uint v=0;for(int i=0;i<4;i++)v|=(uint)b[at++]<<(i*8);return v;}
    }
}
namespace GloomhavenVR.WorldUI
{
    internal enum SharedWindowKind {None,ScenarioStory,MapStory,QuestConfirm,Encounter,Video,RewardShowcase}
    // The rendering/lifecycle fixture supplies an already-converted, visible native owner.
    // Actual authority callbacks, real handle/grabber, policy and release tween run unchanged.
    internal sealed partial class GrabbableModal:IPanelGrabOwner,IPanelGrabAuthority
    {
        private bool _shared=true,_inkValid=true;
        private SharedWindowKind _sharedKind=SharedWindowKind.QuestConfirm;
        private Transform? _frame;
        private PanelGrabHandle? _handle;
        private ConvertedPanel? _panel;
        private Rect _inkRect=new(-100,-100,200,200);
        private readonly WindowReFaceTween _reFaceTween=new();
        private const float ReFaceEpsilonDeg=WindowReFacePolicy.ReFaceEpsilonDeg;
        private string _logName="shared controller746";
        public Transform? GrabRoot=>_frame;public bool GrabVisible=>true;
        public PanelCarryMode CarryMode=>PanelCarryMode.Level;
        public Quaternion GrabLevelFrame=>Quaternion.identity;public Vector2 GrabPitchLimits=>new(-85,85);
        public Vector2 GrabScaleLimits=>new(.3f,3);
        internal void Bind(Transform frame,PanelGrabHandle handle,ConvertedPanel panel){_frame=frame;_handle=handle;_panel=panel;}
        internal bool Turning=>_reFaceTween.Active;
        internal void Advance(){if(_frame!=null)_reFaceTween.Advance(_frame);}
        internal void Tick(){if(_frame!=null&&_panel!=null)_panel.HostRect.SetPositionAndRotation(_frame.position,_frame.rotation);}
    }
}
public static partial class MenuWorldProgram
{
    private static IEnumerable<int> SharedController746(VRHand left,VRHand right,Camera head,string output)
    {
        var first=new SharedWindowMoveRegister(1,3);var second=new SharedWindowMoveRegister(2,3);
        first.Open(1234,1);second.Open(1234,1);first.Sample(out var opening);second.Observe(1,in opening,1,Time.unscaledTime);
        SharedWindowInteractionBridge.CanBegin=_=>first.CanBegin(Time.unscaledTime);
        SharedWindowInteractionBridge.MayWrite=_=>first.LocalWins;
        SharedWindowInteractionBridge.Began=_=>first.Begin(Time.unscaledTime);
        SharedWindowInteractionBridge.Released=_=>first.Release();
        SharedWindowInteractionBridge.Canceled=_=>first.Abandon();
        var frame=Keep(new GameObject("real shared grab frame"));frame.transform.position=head.transform.position+head.transform.forward*2;
        frame.transform.rotation=PanelPlacement.Facing(frame.transform.position,head.transform.position)*Quaternion.Euler(0,80,0);
        var panelGo=Keep(new GameObject("already-converted shared native canvas",typeof(RectTransform),typeof(Canvas)));
        var rect=(RectTransform)panelGo.transform;rect.sizeDelta=new Vector2(200,200);rect.position=frame.transform.position;rect.localScale=Vector3.one*.002f;
        var panel=new ConvertedPanel{Target=rect,HostRect=rect,HostGo=panelGo};
        var bar=Keep(GameObject.CreatePrimitive(PrimitiveType.Cube));bar.transform.SetParent(frame.transform,false);
        var handle=bar.AddComponent<PanelGrabHandle>();
        var owner=new GrabbableModal();owner.Bind(frame.transform,handle,panel);handle.Init(owner,bar.GetComponent<MeshRenderer>(),"WorldUI","shared746");
        WorldUIConfig.WindowFacing.Value=WindowFaceMode.LaserOnly;WorldUIConfig.GrabBarTweenMs.Value=150;
        left.Grabber.CancelAll();right.Grabber.CancelAll();right.TriggerPressed=true;right.GripPressed=false;
        handle.BeginLaserCarry(right,2,bar.transform.position);
        Check(right.Grabber.ForceGrab(handle,true),"actual shared laser handle accepts the local causal claim");
        first.Sample(out var held);Check(held.Claim!=0&&held.Phase==SharedWindowMovePhase.Held,"stationary actual grip publishes a held claim before motion");
        right.TriggerPressed=false;right.Grabber.Tick();
        Check(owner.Turning,"accepted shared laser release starts the original modal release tween");
        Vector3 pivot=rect.TransformPoint(Vector3.zero);Quaternion target=PanelPlacement.Facing(pivot,head.transform.position);
        Vector3 headStart=head.transform.position;
        foreach(var step in PlayerSteps(.25f,()=>owner.Advance(),output,"shared-release746",()=>frame.transform.rotation.ToString()))yield return 0;
        Near(Quaternion.Angle(frame.transform.rotation,target),0,"shared release reaches the captured upright releaser target");
        head.transform.position+=Vector3.right*.8f;owner.Advance();
        Near(Quaternion.Angle(frame.transform.rotation,target),0,"moving the head after release never follows a shared settled window");head.transform.position=headStart;
        // A mode/tracking cancellation is not a deliberate laser drop. It closes the existing
        // claim but may not run the modal's release-facing body.
        right.TriggerPressed=true;handle.BeginLaserCarry(right,2,bar.transform.position);
        Check(right.Grabber.ForceGrab(handle,true),"actual shared laser may be regrabbed after release");
        frame.transform.rotation*=Quaternion.Euler(0,60,0);
        right.Grabber.CancelAll();Check(!owner.Turning&&!handle.IsGrabbed,
            "external cancellation closes the actual shared grip without release-facing");
        first.Sample(out var firstReleased);second.Observe(1,in firstReleased,1,Time.unscaledTime);
        // Same-player two-hand resize stays one claim. The other player begins concurrently
        // before observing this first held packet; its equal revision/id tie must revoke both slots.
        Vector3 originalLeft=left.transform.position,originalRight=right.transform.position;
        left.transform.position=head.transform.position-head.transform.right*.25f*left.WorldScale;
        right.transform.position=head.transform.position+head.transform.right*.25f*right.WorldScale;
        right.GripPressed=left.GripPressed=true;Check(right.Grabber.ForceGrab(handle),"first shared pinch hand engages");first.Sample(out var start);
        Check(left.Grabber.ForceGrab(handle),"second shared pinch hand engages");first.Sample(out var joined);
        Check(handle.IsGrabbed&&start.Claim==joined.Claim,"same-player two-hand resize retains exactly one causal gesture");
        Vector3 leftStart=left.transform.position,rightStart=right.transform.position;
        Vector3 pinch=(rightStart-leftStart).normalized;float beforeScale=frame.transform.localScale.x;
        left.transform.position-=pinch*.3f*left.WorldScale;right.transform.position+=pinch*.3f*right.WorldScale;
        foreach(var step in PlayerSteps(.12f,()=>Invoke(handle,"Update"),output,"shared-resize746",()=>frame.transform.localScale.ToString()))yield return 0;
        Check(frame.transform.localScale.x>beforeScale+.01f,"actual same-player two-hand shared gesture still resizes its world frame");
        Check(second.Begin(Time.unscaledTime),"unobserved second player may enter a crossed simultaneous grip");second.Sample(out var competing);
        // second already observed the first release, so its real new claim must observe that clock.
        first.Observe(2,in competing,1,Time.unscaledTime);Invoke(handle,"Update");
        Check(!handle.IsGrabbed&&left.Grabber.Held==null&&right.Grabber.Held==null&&!owner.Turning,
            "losing actual shared handle force-drops both grabbers without a release or facing tween");
        left.transform.position=originalLeft;right.transform.position=originalRight;
        first.Release();Check(!first.LocalWins,"a delayed losing drop cannot publish a fresh winning release");
        Check(!handle.CanGrab,"fresh remote grip blocks another local physical grab");
        second.Release();second.Sample(out var released);first.Observe(2,in released,1,Time.unscaledTime);
        Check(first.Begin(Time.unscaledTime,true),"later opening-time room making remains possible after actual manual release");
        SharedWindowInteractionBridge.CanBegin=null;SharedWindowInteractionBridge.MayWrite=null;SharedWindowInteractionBridge.Began=null;
        SharedWindowInteractionBridge.Released=null;SharedWindowInteractionBridge.Canceled=null;
        WorldUIConfig.WindowFacing.Value=WindowFaceMode.Never;WorldUIConfig.GrabBarTweenMs.Value=0;
        right.GripPressed=left.GripPressed=right.TriggerPressed=false;
        UnityEngine.Object.DestroyImmediate(bar);UnityEngine.Object.DestroyImmediate(frame);UnityEngine.Object.DestroyImmediate(panelGo);
    }
}
