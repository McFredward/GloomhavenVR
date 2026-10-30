using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Session, tracked input, native scene/controller readiness and unrelated town
// services are explicit inputs. All geometry, source camera draws, raycast,
// pointer enter/down/up/click/drag dispatch and original UI filters execute.
public sealed class Character3DDisplayManager : MonoBehaviour
{
    private Transform character3DHolder = null!;
    private bool isHidden;
    private NativeCharacter character3D = null!;
    private sealed class NativeCharacter
    {
        private readonly List<NativeModel> models;
        internal NativeCharacter(Transform main) => models = new() { new NativeModel { CharacterManager = main } };
    }
    private sealed class NativeModel { public Component CharacterManager { get; set; } = null!; }
    internal void Bind(Transform holder, Transform main) { character3DHolder=holder; character3D=new(main); }
    internal void Replace(Transform main) => character3D=new(main);
    internal bool Hidden { get=>isHidden; set=>isHidden=value; }
}
namespace UnityEngine.UI
{ public sealed class UIWindow : MonoBehaviour { public bool IsOpen=true, HasGoneToStartingState=true; } }
namespace GloomhavenVR
{
    internal sealed class Setting { internal bool Value; internal Setting(bool value) => Value=value; }
    internal static class Plugin { internal static readonly Setting ScrollWithStickOnly = new(false); }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning=true; }
    internal static class PerfConfig { internal static bool Quiet=true; }
    internal static class VRLog
    {
        internal static readonly List<string> Messages=new();
        internal static bool WantsDebug=>false;
        internal static void Warn(string scope,string message)=>Messages.Add(message);
        internal static void Info(string scope,string message)=>Messages.Add(message);
        internal static void Note(string scope,string message)=>Messages.Add(message);
        internal static void Debug(string scope,string message)=>Messages.Add(message);
    }
    internal static class PerfMonitor
    {
        internal readonly struct Measure : IDisposable { public void Dispose() { } }
        internal static Measure Scope(string name)=>default;
        internal static void Count(string name) { }
    }
}
namespace GloomhavenVR.Cards
{
    internal sealed class VRCard : MonoBehaviour { }
    internal static class CardsConfig { internal static readonly Setting FaceMipBake=new(true); }
    internal static class CardFace { internal static void Offer(Component owner) { } }
    internal static class CardFxBounds { internal static void Reseat(Component owner) { } }
}
namespace GloomhavenVR.Rig
{ internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.Hands
{
    internal enum HandSide { Left, Right }
    internal enum HapticPreset { HoverTick, ClickPulse, GrabPulse }
    internal static class VRHaptics
    {
        // The unchanged production sender reaches this device-delivery boundary.
        internal static void Play(object device,HapticPreset preset)=>((VRHand)device).Pulses.Add(preset);
    }
    internal sealed class GrabInput
    {
        internal object? Held;
        internal bool FigureGrabOffered, TriggerGrabOffered;
        internal bool TriggerGrabOfferedFor(Cards.VRCard card)=>false;
    }
    internal sealed partial class VRHand
    {
        internal static float Now;
        private static class Time { internal static float unscaledTime=>Now; }
        private readonly HoverHapticGate _hoverHaptics=new();
        private float _lastHoverHapticTime=float.NegativeInfinity,_lastSemanticHoverHapticTime=float.NegativeInfinity;
        private const float HoverHapticMinInterval=.05f;
        private readonly object _device;
        internal readonly List<HapticPreset> Pulses=new();
        internal VRHand()=>_device=this;
        internal HandSide Side;
        internal readonly Interact.RayInteractor Ray=new();
        internal readonly Interact.RayGrabDriver RayGrab=new();
        internal readonly GrabInput Grabber=new();
        internal bool TriggerDown, TriggerPressed;
        internal float WorldScale=1f;
    }
    internal static class VRHands
    {
        internal static VRHand? Primary;
        internal static VRHand? Get(HandSide side)=>Primary;
    }
}
namespace GloomhavenVR.Hands.Interact
{
    internal readonly struct PickPose
    {
        internal readonly Vector3 Origin,Direction;
        internal readonly bool HasHit;
        internal readonly float HitDistance;
        internal PickPose(Vector3 origin,Vector3 direction,bool hasHit=false,float hitDistance=float.PositiveInfinity)
        { Origin=origin;Direction=direction;HasHit=hasHit;HitDistance=hitDistance; }
    }
    internal sealed class RayInteractor
    {
        internal bool Active=true;
        internal PickPose Current;
        internal float SolidOccluderDistance=float.PositiveInfinity, FanOccluderDistance=float.PositiveInfinity,
            BoardOccluderDistance=float.PositiveInfinity;
        internal bool SolidOccluderIsBoard=false,FanOccluderHeld=false;
        internal LayerMask Mask=~0;
        internal Vector3 WorldUiHit;
        internal static bool WantFanOcclusionNote=>false;
        internal bool TryGetPick(out PickPose pick) {pick=Current;return Active;}
        internal void SetPanelUiHit(Vector3 point,string reason)=>WorldUiHit=point;
        internal void SetWorldUiHit(Vector3 point,string reason)=>WorldUiHit=point;
        internal void ClaimTrigger(string reason) { }
        internal void NoteFanOcclusion(string reason,float distance) { }
    }
    internal static class VRInteractables
    {
        internal static readonly List<(object Target, Collider Collider)> Grabbables=new();
    }
    internal sealed partial class RayGrabDriver
    {
        internal bool TryPickBar(out WorldUI.PanelGrabHandle? handle,out Vector3 point,out float distance)
        {handle=null;point=default;distance=float.PositiveInfinity;return false;}
        internal static bool PickBarGeometry(Ray ray,float limit,out WorldUI.PanelGrabHandle? handle,out Vector3 point,out float distance)
            =>TryPickBarGeometry(ray,limit,true,out handle,out point,out distance);
    }
    internal sealed class PokeOnlyTarget : MonoBehaviour { }
    internal static class UguiPokeSurfaces
    {
        internal static readonly List<Canvas> Surfaces=new();
        internal static readonly Dictionary<Canvas,List<Canvas>> Nested=new();
        internal static List<Canvas>? NestedOf(Canvas? canvas)=>canvas!=null&&Nested.TryGetValue(canvas,out var nested)?nested:null;
        internal static void Prune()=>Surfaces.RemoveAll(x=>x==null);
    }
    internal sealed partial class RayUguiDriver
    {
        private readonly VRHand _hand;
        private readonly UguiPointer _pointer;
        private Canvas? _canvas;
        private bool _pressing;
        private static readonly Vector3[] Corners=new Vector3[4];
        private const float MaxDistanceMeters=20f, OcclusionEpsilonMeters=.005f;
        internal bool HasHit {get;private set;}
        internal float HitDistance {get;private set;}=float.PositiveInfinity;
        internal bool IsPressing=>_pressing;
        internal GameObject? Hovered=>_pointer.Hovered;
        internal RayUguiDriver(VRHand hand) {_hand=hand;_pointer=new(hand.Side,true);}
        private static void LogCanvasOnce(Canvas canvas) { }
        private static bool OfferedAreaClear(float a,float b,float c,float d,float e,float f,float g,float h,float i)=>false;
        private bool TrySettingsFallThrough(Canvas canvas,Vector3 point,float distance,in PickPose pick,float scale)=>false;
        private static void LogSettingsExemption(Canvas a,float b,Canvas c,float d) { }
        private void TickStickScroll() { }
        private void LogSettingsClickTrace(bool hit,in RaycastResult top) { }
        private void LogPressYielded() { }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class PanelGrabHandle : MonoBehaviour
    { internal bool CanGrab=true;internal Collider? BarCollider; }
    internal static class CanvasConversion
    {
        internal static bool BaseSortingOrderOf(GameObject owner,out int order){order=0;return false;}
        internal static bool TryGetHitRect(Canvas canvas,out Rect rect){rect=((RectTransform)canvas.transform).rect;return true;}
    }
    internal static class ModalFallback
    {
        internal static bool ShouldSwallowMenuTabClick(GameObject target)=>false;
        internal static bool IsSettingsSurface(Canvas canvas)=>false;
    }
    internal static class WindowMaterialise
    {
        internal static bool Blind;
        internal static bool IsPointerBlind(Canvas canvas)=>Blind;
        internal static void ProbePointerHit(string side,string reason,Transform target) { }
    }
    internal static class UiSoundEar
    { internal static void BeforeUiEvent() { } internal static void NoticeHoveredWidget(GameObject target) { } }
    internal static class VRKeyboard {internal static void NoticeClick(GameObject? target) { }}
    internal static class NativeVideoWindow
    {internal static UIWindow? Window=>null;internal static bool OwnsClick(GameObject target)=>false;}
    internal static class TownServiceEnhancementHandoff
    {
        internal static bool TryNativeAreaCanvas(Canvas canvas,out Cards.VRCard? card){card=null;return false;}
        internal static bool TryNativeArea(Canvas canvas,GameObject target,out Cards.VRCard? card){card=null;return false;}
    }
    internal static class TownServicePhysicalRay
    {
        internal static bool TryOfferedCardDistance(Cards.VRCard card,Vector3 a,Vector3 b,float c,out float d){d=0;return false;}
        internal static bool TryPick(Hands.VRHand hand,out object? owner,out Vector3 point,out float distance){owner=null;point=default;distance=0;return false;}
        internal static float OtherOccludingDistance(Cards.VRCard card,Vector3 a,Vector3 b,float c)=>float.PositiveInfinity;
        internal static float OtherPhysicsOccludingDistance(Cards.VRCard card,Vector3 a,Vector3 b,float c,LayerMask mask)=>float.PositiveInfinity;
    }
    internal static class TownServiceVisitTarget
    {internal static float OccludingDistance(Vector3 a,Vector3 b,float c)=>float.PositiveInfinity;}
    namespace Patches
    {internal static class PartyPreviewStorm {internal static void NoteHoverHeld() { } internal static void NoteHoverDispatched() { }}}
    namespace Surfaces
    {internal static class InitiativePortraitClickSound {internal static void NoticeClick(GameObject target) { }}}
}
