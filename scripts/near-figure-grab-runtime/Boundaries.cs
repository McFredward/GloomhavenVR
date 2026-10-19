// Deliberate portable boundaries: deterministic time, tracking/input, AABB geometry,
// native eligibility inputs, haptic/log/control sinks. No Unity or game renderer runs.
using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;

namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        public string name = "fixture";
        public static bool operator ==(Object? a, Object? b) =>
            ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed));
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? value) => ReferenceEquals(this, value);
        public override int GetHashCode() => base.GetHashCode();
    }
    internal class MonoBehaviour : Object { public bool isActiveAndEnabled = true; }
    internal sealed class GameObject : Object { public bool activeInHierarchy = true; public readonly Transform transform = new(); }
    internal sealed class Transform
    {
        public Vector3 position, forward = new(0, 0, 1);
        public Vector3 TransformPoint(Vector3 point) => position + point;
        public Vector3 InverseTransformPoint(Vector3 point) => point - position;
    }
    internal readonly struct Vector2
    {
        public readonly float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    internal struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public readonly float sqrMagnitude => x * x + y * y + z * z;
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static float Distance(Vector3 a, Vector3 b)
        { Vector3 d = a - b; return MathF.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z); }
    }
    internal class Collider : Object
    {
        public bool enabled = true;
        public GameObject gameObject = new();
        internal object? NativeOwner;
        public T? GetComponentInParent<T>() where T : class => NativeOwner as T;
        internal Vector3 Min = new(-.01f, 0, -.01f), Max = new(.01f, .08f, .01f);
        // Exact AABB distance boundary, deliberately not a Unity collider implementation.
        public Vector3 ClosestPoint(Vector3 point) => !enabled || !gameObject.activeInHierarchy
            ? point : new(Math.Clamp(point.x, Min.x, Max.x), Math.Clamp(point.y, Min.y, Max.y), Math.Clamp(point.z, Min.z, Max.z));
    }
    internal sealed class MeshCollider : Collider { public bool convex = true; }
    internal static class Mathf
    {
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Abs(float value) => MathF.Abs(value);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }
    internal static class Time { public static int frameCount = 20; public static float unscaledTime = 1f; }
}

namespace GloomhavenVR.Core
{
    internal static class PerfMonitor
    {
        internal readonly struct Timing : IDisposable { public void Dispose() { } }
        internal static Timing Scope(string _) => new();
    }
    internal static class VRLog
    {
        internal static readonly List<string> Entries = new();
        internal static void Note(string _, string text) => Entries.Add(text);
        internal static void Debug(string _, string text) => Entries.Add(text);
        internal static void Error(string _, string text) => Entries.Add(text);
        internal static void Warn(string _, string text) => Entries.Add(text);
        internal static void Info(string _, string text) => Entries.Add(text);
    }
}
namespace GloomhavenVR.Core.Events
{
    internal static class VRModeStateMachine { internal static string CurrentMode = "Scenario"; }
}
namespace GloomhavenVR.Compat
{
    internal enum ControlAction { ProximityGrab, LaserClick }
    internal static class ControlsProgress { internal static int Count; internal static void Notify(ControlAction _) => Count++; }
}
namespace GloomhavenVR.Hands
{
    internal enum HandSide { Left, Right }
    internal enum HapticPreset { GrabPulse, ClickPulse }
    internal sealed class RigHand
    { internal readonly UnityEngine.Transform PalmCenter = new(), GrabAnchor = new(), IndexTip = new(); }
    internal sealed class PokeState { internal bool Enabled = true; internal object? HoveredUi; }
    internal sealed class RayGrabState { internal bool OwnsPointerFrame; }
    internal sealed class VRHand
    {
        internal bool HasPose = true, GripDown, TriggerDown, GripPressed, TriggerPressed;
        internal float WorldScale = 1;
        internal HandSide Side;
        internal UnityEngine.Vector3 PalmVelocity;
        internal readonly RigHand Rig = new();
        internal RayInteractor Ray = new();
        internal RayUguiDriver RayUgui = null!;
        internal ProximityGrabber Grabber = null!;
        internal readonly PokeState Poke = new();
        internal readonly RayGrabState RayGrab = new();
        internal int GrabHaptics, HoverHaptics;
        internal VRHand(HandSide side = HandSide.Left) { Side = side; Grabber = new(this); RayUgui = new(this); }
        internal void SendHoverHaptic(object _) => HoverHaptics++;
        internal void SendHaptic(HapticPreset _) => GrabHaptics++;
    }
    internal static class VRHands { internal static VRHand? Left, Right, Primary; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class ModalFallback { internal static bool HardCommitLockActive; }
    internal static class WorldUIConfig { internal static readonly BoolConfig PokeClick = new(); }
    internal static class VirtualMouse
    {
        internal static readonly UnityEngine.Vector2 ParkPixel = new(-1, -1);
        internal static int Warps;
        internal static void WarpTo(UnityEngine.Vector2 _) => Warps++;
    }
    internal sealed class TextureBoundary { internal int width = 100, height = 100; }
    internal sealed partial class FlatScreen
    {
        private readonly UnityEngine.GameObject? _quad = new();
        private readonly TextureBoundary? _rt = new();
        internal bool _pressing, _pokePressing;
        private bool _pokeLatched, _screenDragActive;
        private VRHand? _pokeHand;
        private UnityEngine.Vector3 _pokePressPoint;
        private UnityEngine.Vector2 _pokePressPixel;
        private float _nextPokeGripLogAt;
        internal int Clicks, PointerContinuations, ReticleHides;
        private bool TickHandednessSwitch() => false;
        private void HideReticle() => ReticleHides++;
        private void BeginScreenDrag(UnityEngine.Vector2 _) => _screenDragActive = true;
        private void UpdateScreenDrag(UnityEngine.Vector2 _) { }
        private void EndScreenDrag() => _screenDragActive = false;
        private void DirectClick(UnityEngine.Vector2 _) => Clicks++;
        private void LogUnderPointer(UnityEngine.Vector2 _) { }
    }
}
namespace GloomhavenVR.Board
{
    internal static class BoardPick { internal static VRHand? SourceHand; internal static bool HasHit = true; }
    internal static partial class BoardClickDriver
    {
        private static bool _pending;
        internal static int Arms;
        private static void ArmPlacementTile() => Arms++;
    }
}
namespace GloomhavenVR.Hands.Interact
{
    internal interface IPokeable { }
    internal sealed class NativeActor : UnityEngine.Object
    {
        internal UnityEngine.GameObject? m_RootGameObject = new();
        internal bool Busy, HoldMustEnd, RemoteHeld;
    }
    internal sealed class NativeCharacter { internal bool IsDead; }
    internal sealed class BoolConfig { internal bool Value = true; }
    internal static class FigureGrabConfig
    {
        internal static readonly BoolConfig GrabFigures = new();
        internal static float PickRadiusRealMeters = .04f;
        internal static UnityEngine.Vector3 HeldOffsetFor(HandSide _) => UnityEngine.Vector3.zero;
    }
    internal static class FigureBusy
    {
        internal static bool IsBusy(NativeActor actor) => actor.Busy;
        internal static bool IsBusy(NativeActor actor, out string why) { why = "fixture native busy"; return actor.Busy; }
        internal static bool HoldMustEnd(NativeActor actor) => actor.HoldMustEnd;
    }
    internal static class NetHeldFigures { internal static bool Owns(NativeActor actor) => actor.RemoteHeld; }
    internal static class FigureStretch
    {
        internal static readonly bool[] Captured = new bool[2];
        internal static bool Engaged(HandSide side) => Captured[(int)side];
    }
    internal readonly struct PickPose
    {
        internal readonly bool HasHit;
        internal readonly UnityEngine.Collider? HitCollider;
        internal readonly UnityEngine.Vector3 HitPoint;
        internal PickPose(UnityEngine.Collider shape) { HasHit = true; HitCollider = shape; HitPoint = UnityEngine.Vector3.zero; }
    }
    internal sealed partial class RayInteractor
    {
        internal bool Enabled = true;
        internal PickPose Pick;
        internal bool TryGetPick(out PickPose pick) { pick = Pick; return pick.HasHit; }
    }
    internal sealed class PointerSink { internal int Presses; internal void Press(UnityEngine.Vector2 _) => Presses++; }
    internal sealed partial class RayUguiDriver
    {
        private readonly VRHand _hand;
        private readonly PointerSink _pointer = new();
        private bool _pressing;
        internal bool IsPressing => _pressing;
        internal int Presses => _pointer.Presses;
        internal RayUguiDriver(VRHand hand) => _hand = hand;
        private void LogPressYielded() { }
        internal void EndPress() => _pressing = false;
    }
    internal sealed partial class FigureGrabDriver
    {
        internal sealed class Adopted
        {
            internal readonly Figure Grabbable;
            internal readonly UnityEngine.Collider Collider;
            internal UnityEngine.Collider? GameCollider;
            internal object? Reach;
            internal Adopted(Figure figure, UnityEngine.Collider shape) { Grabbable = figure; Collider = shape; GameCollider = shape; }
        }
        private readonly Dictionary<NativeActor, Adopted> _adoptions = new();
        private readonly Figure?[] _electedBySide = new Figure?[2], _pendingBySide = new Figure?[2];
        private readonly int[] _dwellBySide = new int[2];
        private int _leftClampFrame = -10, _rightClampFrame = -10;
        internal Adopted Add(Figure figure, UnityEngine.Collider shape)
        {
            shape.NativeOwner = figure._actor;
            var adopted = new Adopted(figure, shape); _adoptions.Add(figure._actor, adopted); return adopted;
        }
        private void LogElection(VRHand hand, Figure? winner, float distance) { }
        private void LogPickVolume(VRHand hand, float distance) { }
        private void NoteBusyRefusal(VRHand hand, Figure figure, string why) { }
        private void NoteReachMiss(VRHand hand, Adopted figure, float distance, UnityEngine.Vector3 anchor) { }
    }
    internal class Target : IGrabbable, IGrabHighlight, IGrabbableHandFilter, IGrabCancellation
    {
        internal bool Eligible = true, HandAllowed = true, Glow;
        internal int Grabs, Releases, Cancels, GlowStarts, GlowEnds;
        public virtual bool CanGrab => Eligible;
        public virtual bool GrabWithGrip { get; set; }
        public virtual bool AllowsHand(VRHand hand) => HandAllowed;
        public virtual void OnGrab(VRHand hand) => Grabs++;
        public void OnRelease(VRHand hand, UnityEngine.Vector3 velocity) => Releases++;
        public void OnGrabCancelled(VRHand hand) => Cancels++;
        public void OnGrabHighlight(VRHand hand, bool value)
        { Glow = value; if (value) GlowStarts++; else GlowEnds++; }
    }
    internal sealed partial class Figure : Target, IBoardGrabTarget, ITriggerOnlyGrabbable
    {
        internal NativeActor _actor = null!;
        internal NativeActor Actor => _actor;
        internal NativeCharacter? Character = new();
        internal UnityEngine.Collider? _gameCollider;
        internal bool _hasGameCollider;
        private UnityEngine.Collider? _collider;
        internal Figure() : this(new NativeActor()) { }
        internal VRHand? _holder;
        internal bool _suppressLeft, _suppressRight;
        private UnityEngine.GameObject? Root => _actor != null ? _actor.m_RootGameObject : null;
        public BoardGrabKind BoardKind => BoardGrabKind.Figure;
        public string BoardLabel => "Elementalist";
        public string DescribeBoardReach(UnityEngine.Vector3 point, float handWorldScale) => "fixture AABB";
        public override bool GrabWithGrip { get => false; set { } }
        public override void OnGrab(VRHand hand) { base.OnGrab(hand); _holder = hand; }
    }
    internal sealed class Prop : Target, IBoardGrabTarget
    {
        public BoardGrabKind BoardKind => BoardGrabKind.Prop;
        public string BoardLabel => "Obstacle";
        public string DescribeBoardReach(UnityEngine.Vector3 point, float handWorldScale) => "fixture AABB";
    }
}
namespace GloomhavenVR.Cards
{
    internal sealed class VRCard : Target { }
    internal static class ItemsPile { internal sealed class ItemChip : Target { } }
}
