// Explicit ports: native main-menu/window/camera visibility, OpenXR hand input,
// captured native menu artwork, native callback sinks and unrelated gameplay.
// Menu admission, placement/follow, recenter, masks, near-grab election, carry,
// scale, bar geometry/timing and pointer projection execute production code.
using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEngine.UI { internal sealed class UIWindow:MonoBehaviour { internal bool IsVisible=false; } }
namespace UnityEngine.EventSystems { internal interface IDragHandler { } }
internal sealed class MapChoreographer:MonoBehaviour { }
internal sealed class Choreographer:MonoBehaviour
{internal static Choreographer? s_Choreographer=null;internal bool ThisPlayerHasTurnControl=false;}
internal sealed class CameraController:MonoBehaviour
{internal static CameraController s_CameraController=null!;internal Vector3 FocusPoint=Vector3.zero;}
internal static class Singleton<T> where T:class
{ internal static bool IsInitialized=false;internal static T Instance=null!; }
namespace GLOOM.MainMenu
{
    internal sealed class UIMainOptionsMenu:MonoBehaviour { }
    internal sealed class MainMenuUIManager:MonoBehaviour
    {
        internal static MainMenuUIManager? Instance;
        internal GameObject? MainMenuCamera;
        internal UIMainOptionsMenu? mainMenu;
        internal bool Initialised=false;
    }
}
namespace BepInEx.Configuration
{ internal sealed class ConfigEntry<T>{internal T Value;internal ConfigEntry(T value){Value=value;}} }
namespace TMPro { }
namespace GloomhavenVR.Core
{
    internal static class VRSession{internal static bool IsRunning=true;}
    internal static class VRLayers
    {
        internal const int ModLayer=27,ModLayerMask=1<<ModLayer;
        internal static void Apply(GameObject go){foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=ModLayer;}
    }
    internal static class VRLog
    {
        internal static readonly List<string> Lines=new();
        internal static void Info(string _,string line)=>Lines.Add(line);
        internal static void Note(string _,string line)=>Lines.Add(line);
        internal static void Debug(string _,string line)=>Lines.Add(line);
        internal static void Warn(string _,string line)=>Lines.Add(line);
        internal static void Error(string _,string line)=>Lines.Add(line);
    }
    internal static class TickGuard{internal static void Run(string _,Action action)=>action();}
    internal static class PerfMonitor
    {internal readonly struct Timing:IDisposable{public void Dispose(){}}internal static Timing Scope(string _)=>new();}
    internal static class EmbeddedTexture{internal static Texture2D? Get(string name,bool linear)=>null;}
    internal static class Defaults{internal const WindowFaceMode WindowFacing=WindowFaceMode.LaserOnly;}
    internal static class PerfConfig{internal static bool CacheDelegates=>true;}
    internal static class SkyAlternative{internal static void NotifyRigScaled(Vector3 pivot,float from,float to){}}
}
namespace GloomhavenVR
{
    internal static class Plugin
    {internal static readonly BepInEx.Configuration.ConfigEntry<bool> DevMode=new(false),SimulateHands=new(false);}
}
namespace GloomhavenVR.Board
{internal static class AoeControl{internal static bool ClaimsStick(HandSide side)=>false;}}
namespace GloomhavenVR.Rig
{
    internal sealed partial class VRRigDriver:MonoBehaviour
    {
        internal static VRRigDriver? Instance;
        internal static Transform? RigRoot=>Instance?._rigRoot?.transform;
        internal static Camera? HeadCamera=>Instance?._camera;
        internal static int RigPoseVersion=0;
        internal static float BaseWorldScale=1;
        internal static Quaternion YawOnly(Quaternion value)=>Quaternion.Euler(0,value.eulerAngles.y,0);
        internal static Quaternion CurrentTiltSwing(Quaternion value)=>Quaternion.identity;
        internal static void NotifyPlayerLocomotion(string what){}
        internal static void NotifyTiltAxisSnap(string what){}
        internal static void NotifyWorldGrabMotion(float meters,float yaw,float zoom){}
        private GameObject? _rigRoot;
        private Camera? _camera;
        private Vector3 _menuAnchorPos;
        private Quaternion _menuAnchorYaw=Quaternion.identity;
        private RigKind _kind=RigKind.Menu;
        private bool _pendingRecenter=true,_ringPlaced=false;
        private readonly ScenarioStartPort _scenarioStart=new();
        private sealed class ScenarioStartPort{internal bool RestorePending=false,HasPose=false;}
        private float _ringWindowEnd=0,_ringNextLogTime=0;
        private int _circleReseatCountdown=0;
        private Action? _tickSpawnRingSettle=null;
        private const float SpawnRingSettleSeconds=3f;
        private const int CircleReseatIntervalFrames=15;
        private void RestoreScenarioStart()=>throw new InvalidOperationException("Scenario restore is outside this menu fixture");
        private void TickSpawnRingSettle()=>throw new InvalidOperationException("Scenario seat is outside this menu fixture");
        private void RememberScenarioStart()=>throw new InvalidOperationException("Scenario history is outside this menu fixture");
        internal void Configure(GameObject root,Camera head,Vector3 anchor,Quaternion yaw)
        {_rigRoot=root;_camera=head;_menuAnchorPos=anchor;_menuAnchorYaw=yaw;Instance=this;}
    }
    internal static class ComfortSettings
    {
        internal static bool IsBound=true;
        internal static readonly BepInEx.Configuration.ConfigEntry<bool> LaserCarryReel=new(true);
        internal static readonly BepInEx.Configuration.ConfigEntry<float> LaserCarryReelSpeed=new(.6f);
        internal static readonly BepInEx.Configuration.ConfigEntry<bool> FlightEnabled=new(true),TurnStickVertical=new(false),WorldGrabEnabled=new(true),ScaleEnabled=new(true),RotateEnabled=new(true);
        internal static readonly BepInEx.Configuration.ConfigEntry<float> FlightMaxSpeed=new(.8f),SnapTurnDegrees=new(15),SmoothTurnSpeed=new(45);
        internal static readonly BepInEx.Configuration.ConfigEntry<TurnHandChoice> FlightHand=new(TurnHandChoice.Left),TurnHand=new(TurnHandChoice.Right);
        internal static readonly BepInEx.Configuration.ConfigEntry<FlightDirectionSource> FlightDirection=new(FlightDirectionSource.Head);
        internal static readonly BepInEx.Configuration.ConfigEntry<TurnMode> Turn=new(TurnMode.Snap);
        // This scenario selects the existing free-movement config. Config storage
        // is a declared port; original motion/scale and RigClamp execute unchanged.
        internal static readonly BepInEx.Configuration.ConfigEntry<bool> FreeMovement=new(true);
        internal static bool EffectiveVerticalDrag=>FreeMovement.Value;
        internal static bool PositionalClampsDisabled=>FreeMovement.Value;
        internal static float EffectiveScaleMin=>.1f;
        internal static float EffectiveScaleMax=>12f;
        internal static void PersistScaleMultiplier(float value){}
    }
}
namespace GloomhavenVR.Hands
{
    internal enum HandSide{Left,Right}
    internal enum HapticPreset{GrabPulse,ClickPulse}
    internal sealed class HandRig
    {internal Transform PalmCenter=null!,GrabAnchor=null!,IndexTip=null!;}
    internal sealed class EnablePort{internal bool Enabled=true;}
    internal sealed partial class VRHand:MonoBehaviour
    {
        internal HandSide Side;
        internal HandRig Rig=new();
        internal bool HasPose=true,GripDown=false,TriggerDown=false,GripPressed=false,TriggerPressed=false,TriggerUp=false;
        internal bool ThumbstickClick=false,ThumbstickClickDown=false;
        internal float WorldScale=>GloomhavenVR.Rig.VRRigDriver.RigRoot?.lossyScale.x??1;
        internal Vector3 PalmVelocity=Vector3.zero;
        internal Vector2 Thumbstick=Vector2.zero;
        internal readonly EnablePort Poke=new(),PalmGate=new();
        internal readonly RayInteractor Ray=new();
        internal readonly RayUguiDriver RayUgui=new();
        internal readonly RayGrabDriver RayGrab=new();
        internal ProximityGrabber Grabber=null!;
        internal void Configure(HandSide side)
        {Side=side;Rig.PalmCenter=Rig.GrabAnchor=Rig.IndexTip=transform;Grabber=new ProximityGrabber(this);}
        internal void GetAimRay(out Vector3 origin,out Vector3 direction){origin=transform.position;direction=transform.forward;}
        internal void SendHoverHaptic(object _){ }
        internal void SendHaptic(HapticPreset _){ }
    }
    internal static class VRHands
    {
        internal static VRHand? Left,Right,Primary;internal static IPickProvider? PrimaryPick;
        internal static VRHand? Get(HandSide side)=>side==HandSide.Left?Left:Right;
        internal static bool Ready=>Left!=null&&Right!=null;
    }
    internal sealed partial class HandsDriver
    {
        private VRHand? _left,_right;
        private bool _menuWorldReady=false,_simActive=false;
        private readonly Action _tickRig=()=>{},_tickSim=()=>{},_tickGrip=()=>{},_tickGhost=()=>{},_tickLesson=()=>{};
        internal HandsDriver(VRHand left,VRHand right){_left=left;_right=right;ApplyMode(Core.Events.VRModeStateMachine.CurrentMode);}
    }
}
namespace GloomhavenVR.Hands.Interact
{
    internal interface IPokeable{ }
    internal interface IPickProvider{bool TryGetPick(out PickPose pose);}
    internal readonly struct PickPose
    {
        internal readonly Vector3 Origin,Direction;
        internal PickPose(Vector3 origin,Vector3 direction){Origin=origin;Direction=direction;}
    }
    internal sealed class RayInteractor:IPickProvider
    {
        internal bool Enabled=true,HasFreshUiHit=false,FreshUiHitIsPanelHoverOnly=false,HasFreshTriggerClaim=false;
        internal string FreshUiHitSource="native input port",FreshTriggerClaimSource="native input port";
        internal Vector3 WorldUiHit;
        internal PickPose Pick;
        public bool TryGetPick(out PickPose pose){pose=Pick;return Enabled;}
        internal void ClaimTrigger(string _){ }
        internal void SetWorldUiHit(Vector3 point,string _)=>WorldUiHit=point;
    }
    internal sealed class RayUguiDriver
    {internal bool IsPressing=false,HasHit=false;internal float HitDistance=0;}
    internal sealed class RayGrabDriver
    {
        internal const float MaxDistanceMeters=5f;
        internal bool OwnsPointerFrame=false;
        internal static float OccludingBarDistance(Vector3 origin,Vector3 direction,float distance)=>float.PositiveInfinity;
    }
    internal static class UiScrollFocus
    {internal static bool IsScrolling(VRHand hand)=>false;internal static string Describe(VRHand hand)=>"native UI claim port: no active list";}
}
namespace GloomhavenVR.Compat
{
    internal enum ControlAction{None,BoardCarry,BoardScale,PanelReel,ProximityGrab,Fly,SnapTurn,WorldDrag,WorldRotate,WorldZoom,Recenter}
    internal static class TutorialVR{internal static void NotifyLocomotion(float meters,float yaw,float zoom){}}
    internal static class ControlsProgress
    {
        internal static ControlAction Waiting=ControlAction.None;
        internal static float BoardCarryGripSeconds=0,BoardScaleStart=0,BoardScaleLive=0,BoardScalePinchSeconds=0,Accumulated=0;
        internal static void Notify(ControlAction _,float amount=1){ }
    }
}
namespace GloomhavenVR.Cards
{
    internal enum ControlBoard{Oak,Steel,Bronze}
    internal class VRCard:MonoBehaviour,IGrabbable
    {public bool CanGrab=>false;public bool GrabWithGrip=>false;public void OnGrab(VRHand _){}public void OnRelease(VRHand _,Vector3 velocity){}}
    internal static class ItemsPile{internal sealed class ItemChip:VRCard{}}
    internal sealed class PlayTray:IPanelGrabOwner
    {
        public Transform? GrabRoot=>null;public bool GrabVisible=>false;
        public PanelCarryMode CarryMode=>PanelCarryMode.Level;
        public Quaternion GrabLevelFrame=>Quaternion.identity;public Vector2 GrabPitchLimits=>new(-180,180);
        public Vector2 GrabScaleLimits=>new(.15f,2);public void OnGrabFinished(){}
        internal static Shader? OverlayShader()=>Shader.Find("GloomhavenVR/Overlay");
        internal static Shader? BoardLitShader()=>Shader.Find("Unlit/Color");
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class LoadingIndicator{internal static bool FlatScreenSuppressed=false;}
    internal static class PanelLayout{internal static float WorldScale=>Rig.VRRigDriver.RigRoot?.lossyScale.x??1;}
    internal static class WorldUIConfig
    {
        internal static readonly BepInEx.Configuration.ConfigEntry<float> ScreenDistance=new(1.6f),ScreenWidth=new(2f),CanvasScaleMm=new(.3f),GrabBarTweenMs=new(0),DragUnlockDegrees=new(3),DragUnlockSeconds=new(.12f);
        internal static readonly BepInEx.Configuration.ConfigEntry<WindowFaceMode> WindowFacing=new(WindowFaceMode.Never);
    }
    internal sealed class ConvertedPanel
    {
        internal RectTransform Target=null!,HostRect=null!;
        internal GameObject HostGo=null!;internal Canvas? HostCanvas=null;
        internal bool IsAlive=>Target!=null;
        internal bool RenderHidden=false,OwnerRenderHidden=false,GuardHostHeld=false,GuardHostMoving=false;
        internal int DrawSortingOrder=0;
        internal readonly List<OrderFollower> OrderFollowers=new();internal readonly List<Transform> ExtraRenderRoots=new();
    }
    internal struct OrderFollower{internal Canvas? Canvas;internal Renderer? Renderer;internal int Offset;}
    internal static partial class CanvasConversion
    {
        internal static Camera? WorldCamera=>Rig.VRRigDriver.HeadCamera;
        private static int s_orderWrites=0;
        private static void CheckFollowerOffset(int value,string name,bool allowBehind){if(value<0||value>=16)throw new InvalidOperationException(name);}
        private static void HideTree(ConvertedPanel panel,Transform root,out int canvases,out int renderers){canvases=renderers=0;}
    }
    internal static class ModalFallback{internal static bool HardCommitLockActive=false;}
    internal static class VirtualMouse{internal static Vector2 Pixel;internal static void WarpTo(Vector2 value)=>Pixel=value;}
    internal static class LaserPointerPolicy{internal static bool TargetBeforeBlocker(float target,float blocker)=>target<blocker;}
    internal sealed class StereoPort
    {internal bool MapActive=false;internal void BeginMapPan(Vector2 _){}internal void UpdateMapPan(Vector2 _){}internal void EndMapPan(){}}
    internal sealed partial class FlatScreen
    {
        private const float ScreenAspect=16f/9f,BackplaneGapMeters=.06f,FollowAngleDegrees=45,FollowDwellSeconds=1,FollowSettledDegrees=5,MapPanStartPixels=8;
        private GameObject? _quad,_backQuad;
        private Renderer? _quadRenderer;
        private Material? _screenMaterial=null;
        private RenderTexture? _rt,_uiRt;
        private bool _visible=true,_splitRouting=false;
        private bool SplitActive=>_backQuad!=null;
        private Camera? _placedHead;
        private Vector3 _placedRigPos;
        private float _offGazeSince=-1,_dragOverSince=-1;
        private bool _gliding=false,_pressing=false,_latched=false,_mapPanGesture=false,_screenDragActive=false;
        private Vector3 _pressDirection;
        private Vector2 _latchedPixel,_latchedLocal;
        private readonly StereoPort _stereo=new();
        private static FlatScreen? _coverageOwner;
        private readonly struct CapturedCamera{internal readonly bool IsUi;internal CapturedCamera(bool ui){IsUi=ui;}}
        private static readonly Dictionary<Camera,CapturedCamera> CapturedSet=new();
        private bool _pokePressing=false;
        private void TickPoke(){}
        private bool TickHandednessSwitch()=>false;
        private void EnrollCamera(Camera camera)=>CapturedSet[camera]=new CapturedCamera(true);
        private bool PointerOverUiHandler<T>(Vector2 pixel)=>false;
        private void TickStickScroll(Vector2 pixel,VRHand hand,bool active){}
        private void BeginScreenDrag(Vector2 pixel)=>_screenDragActive=true;
        private void UpdateScreenDrag(Vector2 pixel){}
        private void EndScreenDrag()=>_screenDragActive=false;
        private void DirectClick(Vector2 pixel){ }
        private void LogUnderPointer(Vector2 pixel){ }
        private void HideReticle(){ }
        private void YieldScreenPointer(){ }
    }
}
