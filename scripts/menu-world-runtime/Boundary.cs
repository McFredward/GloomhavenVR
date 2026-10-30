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

namespace UnityEngine.UI
{
    internal sealed class UIWindow:MonoBehaviour { internal bool IsVisible=false; }
    // Type-only boundary: this fixture declares no spatial/native raised member.
    // Any attempt to inspect its visual ink is outside the menu/handle scope.
    internal class Graphic:MonoBehaviour
    {
        internal Color color=>throw new InvalidOperationException("No depth graphic in menu world fixture");
        internal CanvasRenderer canvasRenderer=>throw new InvalidOperationException("No depth graphic in menu world fixture");
    }
}
namespace UnityEngine.EventSystems { internal interface IDragHandler { } internal struct RaycastResult {internal GameObject gameObject;}}
internal sealed class MapChoreographer:MonoBehaviour { }
internal sealed class Choreographer:MonoBehaviour
{internal static Choreographer? s_Choreographer=null;internal bool ThisPlayerHasTurnControl=false;}
internal sealed class Controller{internal static Controller? Instance;internal LayerMask m_ActiveSelectionRaycastLayer;}
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
        internal static bool WantsDebug=false;
        internal static void Info(string _,string line)=>Lines.Add(line);
        internal static void Note(string _,string line)=>Lines.Add(line);
        internal static void Debug(string _,string line)=>Lines.Add(line);
        internal static void Warn(string _,string line)=>Lines.Add(line);
        internal static void Alert(string _,string line)=>Lines.Add(line);
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
{internal sealed partial class BoardDriver{} internal static class AoeControl{internal static bool ClaimsStick(HandSide side)=>false;}}
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
        internal RayInteractor Ray=null!;
        internal RayUguiDriver RayUgui=null!;
        internal RayGrabDriver RayGrab=null!;
        internal ProximityGrabber Grabber=null!;
        internal void Configure(HandSide side)
        {Side=side;Rig.PalmCenter=Rig.GrabAnchor=Rig.IndexTip=transform;Grabber=new ProximityGrabber(this);Ray=new RayInteractor(this);RayGrab=new RayGrabDriver(this);RayUgui=new RayUguiDriver(this);}
        internal void GetAimRay(out Vector3 origin,out Vector3 direction){origin=transform.position;direction=transform.forward;}
        internal void SendHoverHaptic(object? _){ }
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
        internal readonly bool HasHit;internal readonly float HitDistance;internal readonly Collider? HitCollider;
        internal PickPose(Vector3 origin,Vector3 direction,bool hit=false,float distance=float.PositiveInfinity,Collider? collider=null){Origin=origin;Direction=direction;HasHit=hit;HitDistance=distance;HitCollider=collider;}
    }
    internal sealed partial class RayInteractor:IPickProvider
    {
        private readonly VRHand _hand;internal RayInteractor(VRHand hand)=>_hand=hand;
        internal bool _enabled=true;internal bool Enabled{get=>_enabled;set=>_enabled=value;}
        internal bool IsHolding=>_hand.Grabber.Held!=null;internal bool GripSuppressed=>_hand.GripPressed;
        internal bool CardContactStandDown=false;
        internal bool HasFreshUiHit=false,FreshUiHitIsPanelHoverOnly=false,HasFreshTriggerClaim=false;
        internal static bool WantFanOcclusionNote=false;
        internal float SolidOccluderDistance=float.PositiveInfinity,FanOccluderDistance=float.PositiveInfinity,BoardOccluderDistance=float.PositiveInfinity;internal bool SolidOccluderIsBoard=false,FanOccluderHeld=false;internal PickPose Current=>Pick;
        internal void NoteFanOcclusion(string _,float distance){}
        internal void SetPanelUiHit(Vector3 point,string _){WorldUiHit=point;HasFreshUiHit=FreshUiHitIsPanelHoverOnly=true;}
        internal string FreshUiHitSource="native input port",FreshTriggerClaimSource="native input port";
        internal Vector3 WorldUiHit;
        internal PickPose Pick;
        public bool TryGetPick(out PickPose pose){pose=Pick;return Active;}
        internal void ClaimTrigger(string _){HasFreshTriggerClaim=true;FreshTriggerClaimSource=_;}
        internal void SetWorldUiHit(Vector3 point,string _)=>WorldUiHit=point;
    }
    // Native callback/widget ports. Canvas plane geometry and arbitration execute extracted production code.
    internal sealed class UguiPointer
    {
        internal static int Presses=0,Releases=0;internal GameObject? Hovered;
        internal static readonly Dictionary<Canvas,GameObject> Widgets=new();
        internal UguiPointer(HandSide side,bool farRay){}
        internal bool TryRaycast(Canvas canvas,Vector2 point,out UnityEngine.EventSystems.RaycastResult result)
        {result=default;if(!Widgets.TryGetValue(canvas,out var widget))return false;result.gameObject=widget;return true;}
        internal bool TryRaycastSurface(Canvas canvas,Vector2 point,Graphic graphic,bool preferGraphic,
            Vector3 physicalPoint,float physicalDistance,out UnityEngine.EventSystems.RaycastResult result,
            Transform? nativeRoot=null)
            => throw new InvalidOperationException("No spatial menu source is registered in the menu world fixture");
        internal void Cancel()=>Hovered=null;internal void SetHovered(GameObject? target)=>Hovered=target;
        internal void Press(Vector2 point)=>Presses++;internal void Release(Vector2 point)=>Releases++;
        internal void Drag(Vector2 point){}
    }
    internal sealed partial class RayUguiDriver
    {
        private const float MaxDistanceMeters=20f,OcclusionEpsilonMeters=.005f;
        private readonly VRHand _hand;private readonly UguiPointer _pointer;
        private Canvas? _canvas;private bool _pressing;
        private static readonly Vector3[] Corners=new Vector3[4];
        internal RayUguiDriver(VRHand hand){_hand=hand;_pointer=new UguiPointer(hand.Side,true);}
        internal bool IsPressing=>_pressing;internal bool HasHit{get;private set;}internal float HitDistance{get;private set;}=float.PositiveInfinity;
        private static void LogCanvasOnce(Canvas canvas){}
        private static bool OfferedAreaClear(float a,float b,float c,float d,float e,float f,float g,float h,float i)=>false;
        private bool TrySettingsFallThrough(Canvas canvas,Vector3 point,float distance,in PickPose pick,float scale)=>false;
        private static void LogSettingsExemption(Canvas a,float b,Canvas c,float d){}
        private void TickStickScroll(){}private void LogSettingsClickTrace(bool hit,in UnityEngine.EventSystems.RaycastResult top){}
        private void LogPressYielded(){}
    }
    internal static class UguiPokeSurfaces
    {
        internal static readonly List<Canvas> Surfaces=new();
        internal static void Prune()=>Surfaces.RemoveAll(x=>x==null);
        internal static List<Canvas>? NestedOf(Canvas canvas)=>null;
    }
    internal static class VisibleUiSurface{internal static bool Contains(Canvas c,Vector2 p,Camera? camera)=>true;}
    internal static class HoverHapticTargets{internal static object? Ui(GameObject? target)=>target;}
    internal static class UiScrollFocus
    {internal static bool IsScrolling(VRHand hand)=>false;internal static string Describe(VRHand hand)=>"native UI claim port: no active list";}
}
namespace GloomhavenVR.WorldUI.MenuDepth
{
    // These menu/handle/rig cases contain no spatial source or raised native member.
    // Real depth picking and original raycaster mapping execute in menu-depth-laser.
    internal static class MenuDepthSources
    {
        internal static bool TryRaycast(Canvas canvas,Vector3 origin,Vector3 direction,float maximum,out MenuDepthRayHit hit)
        {hit=default;return false;}
        internal static bool TryNativeRaycast(Canvas host,Canvas canvas,Vector3 origin,Vector3 direction,float maximum,out MenuDepthRayHit hit)
        {hit=default;return false;}
    }
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
        internal static bool TryGetHitRect(Canvas canvas,out Rect result){result=default;return false;}
        internal static Camera? WorldCamera=>Rig.VRRigDriver.HeadCamera;
        private static int s_orderWrites=0;
        private static void CheckFollowerOffset(int value,string name,bool allowBehind){if(value<0||value>=16)throw new InvalidOperationException(name);}
        private static void HideTree(ConvertedPanel panel,Transform root,out int canvases,out int renderers){canvases=renderers=0;}
    }
    internal static class ModalFallback{internal static bool HardCommitLockActive=false;internal static bool IsSettingsSurface(Canvas canvas)=>false;}
    internal static class UiSoundEar{internal static void BeforeUiEvent(){}}
    internal static class WindowMaterialise{internal static bool IsPointerBlind(Canvas canvas)=>false;internal static void ProbePointerHit(string side,string source,Transform target){}}
    internal static class TownServiceEnhancementHandoff{internal static bool TryNativeAreaCanvas(Canvas canvas,out Cards.VRCard? card){card=null;return false;}internal static bool TryNativeArea(Canvas canvas,GameObject? target,out Cards.VRCard? card){card=null;return false;}}
    internal static class TownServiceVisitTarget{internal static float OccludingDistance(Vector3 a,Vector3 b,float c)=>float.PositiveInfinity;}
    internal static class TownServicePhysicalRay{internal static bool TryPick(VRHand hand,out object? a,out object? b,out float distance){a=b=null;distance=float.PositiveInfinity;return false;}internal static bool TryOfferedCardDistance(Cards.VRCard card,Vector3 a,Vector3 b,float c,out float distance){distance=float.PositiveInfinity;return false;}internal static float OtherOccludingDistance(Cards.VRCard card,Vector3 a,Vector3 b,float c)=>float.PositiveInfinity;internal static float OtherPhysicsOccludingDistance(Cards.VRCard card,Vector3 a,Vector3 b,float c,LayerMask mask)=>float.PositiveInfinity;}
    internal static class VirtualMouse{internal static readonly Vector2 ParkPixel=new(-4096f,-4096f);internal static Vector2 Pixel;internal static void WarpTo(Vector2 value)=>Pixel=value;}
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
    }
}

namespace GloomhavenVR.WorldUI.MapRoom
{
    internal sealed partial class MapLocationInteractor
    {
        private const string Scope="map native mask owner";
        private bool _maskTaken=false;
        private int _maskWasLeft=0,_maskWasRight=0,_maskInForce=1<<15;
        private readonly List<int> _locations=new();
    }
}
