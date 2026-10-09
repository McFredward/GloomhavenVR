using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.Rig;

internal sealed partial class Comfort
{
    private Transform? _motionSampleRig;
    private Vector3 _motionSamplePosition;
    private Quaternion _motionSampleRotation;
    private Vector3 _motionWindowPosition;
    private Quaternion _motionWindowRotation;
    private float _motionSampleScale;
    private float _motionSampleAt;
    private float _nextMotionSampleAt;
    private float _motionTravelMeters;
    private float _motionTurnDegrees;
    private int _motionRequestFrames;
    private int _motionZeroScaledRequestFrames;
    private float _motionMinRequestedTimeScale = float.PositiveInfinity;

    // Build653's remote log keeps the map rig, TableIdle and tracked hands alive, but
    // contains no axis/click readings or final rig displacement. It cannot distinguish
    // an input gap, an aimed scroll/reel, a held world-grab, or a motion writer fighting
    // another writer. Sample those facts together in this component's LateUpdate;
    // this is not a claim of the last writer or a render-final pose. Window net motion
    // and accumulated sampled travel are separate so reversals cannot masquerade as
    // net progress. This is observation
    // only: no watchdog unlock, game-time write, pose correction or scroll probe.
    private void LateUpdate()
    {
        if (!VRLog.WantsDebug || !ComfortSettings.IsBound || RigTarget.Current is not { } rig)
        {
            _motionSampleRig = null;
            return;
        }
        VRMode mode = VRModeStateMachine.CurrentMode;
        if (mode == VRMode.Menu2D)
        {
            _motionSampleRig = null;
            return;
        }

        float now = Time.unscaledTime;
        Vector3 position = rig.position;
        Quaternion rotation = rig.rotation;
        float scale = rig.lossyScale.x;
        VRHand? left = VRHands.Left, right = VRHands.Right;
        bool requested = RequestsMotion(left) || RequestsMotion(right);
        if (_motionSampleRig != rig)
        {
            _motionSampleRig = rig;
            _motionSampleAt = now;
            _motionWindowPosition = position;
            _motionWindowRotation = rotation;
            _nextMotionSampleAt = now + 5f;
            _motionTravelMeters = _motionTurnDegrees = 0f;
            _motionRequestFrames = _motionZeroScaledRequestFrames = 0;
            _motionMinRequestedTimeScale = float.PositiveInfinity;
        }
        else
        {
            if (scale > 0f)
                _motionTravelMeters += Vector3.Distance(position, _motionSamplePosition) / scale;
            _motionTurnDegrees += Quaternion.Angle(rotation, _motionSampleRotation);
        }
        _motionSamplePosition = position;
        _motionSampleRotation = rotation;
        _motionSampleScale = scale;
        if (requested)
        {
            _motionRequestFrames++;
            if (Time.deltaTime <= 0f) _motionZeroScaledRequestFrames++;
            _motionMinRequestedTimeScale = Mathf.Min(_motionMinRequestedTimeScale, Time.timeScale);
        }
        if (now < _nextMotionSampleAt) return;

        // Idle samples distinguish a runtime delivering zeros from a mod suppression.
        // Active samples are at most one per5s; idle samples at most one per15s.
        _nextMotionSampleAt = now + (requested ? 5f : 15f);
        float netMeters = scale > 0f ? Vector3.Distance(position, _motionWindowPosition) / scale : 0f;
        VRLog.Debug("Comfort", $"LOCOMOTION SAMPLE phase=Comfort.LateUpdate frame={Time.frameCount} mode={mode} "
            + $"window={now - _motionSampleAt:F2}s requestFrames={_motionRequestFrames} "
            + $"zeroScaledRequestFrames={_motionZeroScaledRequestFrames} "
            + $"minRequestedTimeScale={(_motionRequestFrames > 0 ? _motionMinRequestedTimeScale.ToString("F3") : "none")} "
            + $"net={netMeters:F3}m netTurn={Quaternion.Angle(rotation, _motionWindowRotation):F1}deg "
            + $"sampledTravel={_motionTravelMeters:F3}m sampledTurn={_motionTurnDegrees:F1}deg scale={_motionSampleScale:F2} "
            + $"unityTimeScale={Time.timeScale:F3} dt={Time.deltaTime:F4}/{Time.unscaledDeltaTime:F4} "
            + $"focus={VRSession.InputFocus?.ToString() ?? "unknown"} "
            + $"flight={ComfortSettings.FlightEnabled.Value}/{Flight.Instance?.IsFlying} "
            + $"grab={ComfortSettings.WorldGrabEnabled.Value}/{WorldGrab.Instance?.IsGrabbing} "
            + $"turnMode={ComfortSettings.Turn.Value} turnScrollBlocked={SnapTurn.Instance?.ScrollBlocked} "
            + $"L[{MotionHand(left)}] R[{MotionHand(right)}]");
        _motionSampleAt = now;
        _motionWindowPosition = position;
        _motionWindowRotation = rotation;
        _motionTravelMeters = _motionTurnDegrees = 0f;
        _motionRequestFrames = _motionZeroScaledRequestFrames = 0;
        _motionMinRequestedTimeScale = float.PositiveInfinity;
    }

    private static bool RequestsMotion(VRHand? hand) => hand != null && hand.HasPose
        && (hand.Thumbstick.sqrMagnitude > .04f || hand.ThumbstickClick);

    private static string MotionHand(VRHand? hand)
    {
        if (hand == null) return "missing";
        return $"pose={hand.HasPose} axis={hand.Thumbstick:F3} "
            + $"click={hand.ThumbstickClick}/{hand.ThumbstickClickDown} "
            + $"worldGrab={WorldGrab.Instance?.IsHandGrabbing(hand)} "
            + $"reel={WorldUI.LaserCarryReel.OwnsStick(hand)} scroll={UiScrollFocus.Describe(hand)}";
    }
}
