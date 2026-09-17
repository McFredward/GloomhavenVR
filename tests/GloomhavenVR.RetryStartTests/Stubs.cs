using System;
using System.Reflection;
using UnityEngine;

namespace UnityEngine
{
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        internal static Vector3 one => new(1, 1, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        public override string ToString() => $"({x}, {y}, {z})";
    }
    internal struct Quaternion
    {
        internal System.Numerics.Quaternion Value;
        internal static Quaternion Yaw(float angle) => new() { Value = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, angle * MathF.PI / 180) };
        internal static Quaternion Inverse(Quaternion q) => new() { Value = System.Numerics.Quaternion.Inverse(q.Value) };
        public static Quaternion operator *(Quaternion a, Quaternion b) => new() { Value = a.Value * b.Value };
        public static Vector3 operator *(Quaternion a, Vector3 b)
        {
            var v = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(b.x, b.y, b.z), a.Value);
            return new Vector3(v.X, v.Y, v.Z);
        }
        internal Vector3 eulerAngles => new(0, MathF.Atan2(2 * Value.W * Value.Y, 1 - 2 * Value.Y * Value.Y) * 180 / MathF.PI, 0);
    }
    internal sealed class Transform
    {
        internal Transform? parent;
        internal Vector3 localPosition;
        internal Quaternion localRotation = Quaternion.Yaw(0);
        internal Vector3 localScale = Vector3.one;
        internal bool IsChildOf(Transform ancestor) => ReferenceEquals(this, ancestor) || (parent?.IsChildOf(ancestor) ?? false);
        internal Vector3 lossyScale => parent == null ? localScale : localScale * parent.lossyScale.x;
        internal Vector3 position
        {
            get => parent == null ? localPosition : parent.position + parent.rotation * (localPosition * parent.lossyScale.x);
            set => localPosition = parent == null ? value : Quaternion.Inverse(parent.rotation) * (value - parent.position) * (1 / parent.lossyScale.x);
        }
        internal Quaternion rotation
        {
            get => parent == null ? localRotation : parent.rotation * localRotation;
            set => localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value;
        }
    }
    internal static class Mathf { internal static float Max(float a, float b) => MathF.Max(a, b); }
    internal sealed class GameObject { internal Transform transform = new(); }
    internal sealed class Camera { internal Transform transform = new(); }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    {
        internal HarmonyPatch() { }
        internal HarmonyPatch(Type type) { }
        internal HarmonyPatch(Type type, string name) { }
    }
    internal static class AccessTools
    {
        internal static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    }
}
internal static class Choreographer { internal static object s_Choreographer = new(); }
internal sealed class SceneController
{
    internal enum ESceneType { None, Scenario, MainMenu, CampaignMap }
    public void RestartScenario() { }
    public void RestartScenarioFromInitial() { }
    public void RegenerateAndRestartScenario() { }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class TickGuard { internal static void Run(string label, Action action) => action(); }
    internal static class VRLog { internal static void Note(string section, string text) { } }
}
namespace GloomhavenVR.Cards
{
    internal static class CardsDriver { internal static bool NativeSceneLoadInProgress; }
    internal sealed partial class PlayTray
    {
        internal static PlayTray? Current;
        private Transform? _root = new();
        private Transform? _anchorParent;
        internal PlayTray() { Bind(); }
        internal void Bind() { _retryScenarioOwner = Choreographer.s_Choreographer; _anchorParent = Rig.VRRigDriver.RigRoot; }
        private bool _placed = true, _everPlaced = true;
        private sealed class Handle { internal bool IsGrabbed = false; }
        private Handle? _handle = new();
        private string? _pinHousekeepingMove;
        private bool _arrivalArmed = true, _arrivalPendingSeen = true;
        internal sealed class Anchor
        {
            internal int Version;
            internal Vector3 Position;
            internal void ReauthorOrigin() => Version = Rig.VRRigDriver.RigPoseVersion;
            internal void RecacheRigLocal(Transform root) => Position = root.position;
        }
        private readonly Anchor _anchor = new();
        internal bool Pinned;
        internal readonly Transform PinParent = new() { localScale = Vector3.one * 53 };
        private void ApplyFollowMode()
        {
            _root!.parent = Pinned ? PinParent : Rig.VRRigDriver.RigRoot;
        }
        internal static int Calls;
        internal static Vector3 HeadAtReset;
        internal static float ScaleAtReset;
        internal static int VersionAtReset;
        private void NotePinnedWrite(string reason)
        {
            Calls++;
            HeadAtReset = Rig.VRRigDriver.Instance!.HeadPosition;
            ScaleAtReset = Rig.VRRigDriver.Instance!.Scale;
            VersionAtReset = Rig.VRRigDriver.RigPoseVersion;
        }
        internal Transform Root => _root!;
        internal Anchor Pin => _anchor;
        internal bool ArrivalDisarmed => !_arrivalArmed && !_arrivalPendingSeen;
        internal bool Placed => _placed && _everPlaced && _pinHousekeepingMove != null;
        internal void Capture() => RememberRetryStart();
        internal bool Restore() => TryRestoreRetryStart();
        internal void SetGrabbed(bool grabbed) => _handle!.IsGrabbed = grabbed;
    }
}
namespace GloomhavenVR.WorldUI.Surfaces
{
    internal static class CombatLogSurface { internal static void RequestRecenterReset(string reason) { } }
}
namespace GloomhavenVR.Rig
{
    internal static class RigClamp { internal static void Apply(Transform root) { } }
    internal sealed partial class VRRigDriver
    {
        private enum RigKind { Menu, Scenario }
        internal static VRRigDriver? Instance;
        internal static Transform? RigRoot => Instance?._rigRoot?.transform;
        internal static Camera? HeadCamera => Instance?._camera;
        private RigKind _kind = RigKind.Scenario;
        private GameObject? _rigRoot = new();
        private Camera? _camera = new();
        private Quaternion _scenarioBaseYaw = Quaternion.Yaw(37);
        private bool _ringSeatAngleValid = true;
        private float _ringSeatAngleDegrees = 123;
        private string? _axisSnapReason;
        private bool _ringPlaced;
        internal static int RigPoseVersion;
        private static Quaternion YawOnly(Quaternion q) => q;
        internal int Closed;
        private void CloseRingWindow(string reason) => Closed++;
        internal Vector3 HeadPosition => _camera!.transform.position;
        internal Quaternion HeadYaw => _camera!.transform.rotation;
        internal float Scale => _rigRoot!.transform.localScale.x;
        internal bool Pending => _scenarioStart.RestorePending;
        internal bool HasPose => _scenarioStart.HasPose;
        internal bool Placed => _ringPlaced;
        internal string? SnapReason => _axisSnapReason;
        internal void Enter(object scenario) { Choreographer.s_Choreographer = scenario; _scenarioStart.EnterScenario(scenario); }
        internal void Capture() => RememberScenarioStart();
        internal void Restore() => RestoreScenarioStart();
        internal void Pose(Vector3 rootPosition, float rootYaw, float scale, Vector3 localHead, float localYaw)
        {
            _rigRoot!.transform.position = rootPosition;
            _rigRoot.transform.rotation = Quaternion.Yaw(rootYaw);
            _rigRoot.transform.localScale = Vector3.one * scale;
            _camera!.transform.parent = _rigRoot.transform;
            _camera.transform.localPosition = localHead;
            _camera.transform.localRotation = Quaternion.Yaw(localYaw);
        }
    }
}
