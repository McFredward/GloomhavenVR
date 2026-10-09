using System;
using System.Collections.Generic;
using UnityEngine;

// Only tracked device input, game mode/config and notification sinks are fixture
// ports. Production locomotion, scroll arbitration, Unity Time and transforms run
// unchanged. No fixture implements a rig movement or chooses a smoothing result.
namespace GloomhavenVR.Core.Events { }
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static bool WantsDebug = true;
        internal static readonly List<string> Messages = new();
        internal static void Debug(string tag, string message) { Messages.Add(message); }
        internal static void Info(string tag, string message) { }
    }
    internal static class VRSession { internal static bool? InputFocus; }
    internal static class SkyAlternative
    { internal static void NotifyRigScaled(Vector3 pivot, float oldScale, float newScale) { } }
}
namespace GloomhavenVR
{
    internal enum VRMode { Menu2D, TableIdle, ModalUI, BoardTargeting }
    internal static class VRModeStateMachine { internal static VRMode CurrentMode; }
}
namespace GloomhavenVR.Hands
{
    internal enum HandSide { Left, Right }
    internal enum HapticPreset { ClickPulse }
    internal sealed class GrabPort { internal object? Held; }
    internal sealed class VRHand : MonoBehaviour
    {
        internal HandSide Side;
        internal bool HasPose = true, ThumbstickClick, ThumbstickClickDown;
        internal Vector2 Thumbstick;
        internal readonly GrabPort Grabber = new();
        internal void SendHaptic(HapticPreset preset) { }
    }
    internal static class VRHands
    {
        internal static VRHand? Left, Right;
        internal static VRHand? Get(HandSide side) => side == HandSide.Left ? Left : Right;
    }
}
namespace GloomhavenVR.Rig
{
    internal sealed class Dial<T> { internal T Value; internal Dial(T value) { Value = value; } }
    internal enum TurnMode { Off, Snap, Smooth }
    internal enum TurnHandChoice { Left, Right }
    internal static class ComfortSettings
    {
        internal static bool IsBound = true, EffectiveVerticalDrag = true;
        internal static float EffectiveScaleMin = .1f, EffectiveScaleMax = 12f;
        internal static readonly Dial<bool> WorldGrabEnabled = new(true), ScaleEnabled = new(true), RotateEnabled = new(true);
        internal static readonly Dial<bool> FlightEnabled = new(true);
        internal static readonly Dial<TurnMode> Turn = new(TurnMode.Smooth);
        internal static readonly Dial<TurnHandChoice> TurnHand = new(TurnHandChoice.Right);
        internal static readonly Dial<float> SmoothTurnSpeed = new(90f), SnapTurnDegrees = new(45f);
        internal static void PersistScaleMultiplier(float value) { }
    }
    internal static class RigTarget { internal static Transform? Current; internal static bool IsDevProxy; internal static float BaseScale = 1f; }
    internal static class VRRigDriver
    {
        internal static Camera? HeadCamera;
        internal static Quaternion YawOnly(Quaternion rotation) => Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
        internal static Quaternion CurrentTiltSwing(Quaternion yaw) => Quaternion.identity;
        internal static void NotifyTiltAxisSnap(string reason) { }
        internal static void NotifyPlayerLocomotion(string reason) { }
        internal static void NotifyWorldGrabMotion(float drag, float turn, float zoom) { }
    }
    internal static class LocalTurnControl
    {
        internal static bool TargetingOwnsStick;
        internal static GloomhavenVR.Hands.HandSide Resolve(TurnHandChoice hand) => hand == TurnHandChoice.Left
            ? GloomhavenVR.Hands.HandSide.Left : GloomhavenVR.Hands.HandSide.Right;
    }
    internal static class RigClamp { internal static void Apply(Transform rig) { } }
    internal sealed partial class Comfort : MonoBehaviour { }
    internal sealed class Flight { internal static Flight? Instance; internal bool IsFlying; }
}
namespace GloomhavenVR.WorldUI
{ internal static class LaserCarryReel { internal static bool OwnsStick(GloomhavenVR.Hands.VRHand hand) => false; } }
namespace GloomhavenVR.Compat
{
    internal enum ControlAction { WorldDrag, WorldRotate, WorldZoom, SnapTurn }
    internal static class TutorialVR { internal static void NotifyLocomotion(float drag, float turn, float zoom) { } }
    internal static class ControlsProgress { internal static void Notify(ControlAction action, float amount) { } }
}
