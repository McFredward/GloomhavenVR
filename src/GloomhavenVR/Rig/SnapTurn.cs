using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// Thumbstick turning: flick the configured hand's stick left/right to yaw the rig
/// around the CURRENT HMD position (the head stays put; the table pivots around you).
/// <c>[Comfort] TurnMode</c>: Snap (default, <c>SnapTurnDegrees</c> per flick with
/// engage/re-arm hysteresis) / Smooth (<c>SmoothTurnSpeed</c> °/s) / Off.
///
/// STICK CONTENTION (documented rule): <see cref="VRMode.BoardTargeting"/> owns the
/// thumbstick — Phase-3a rotates AoE patterns with it — so turning is hard-disabled
/// there (and re-armed, so leaving targeting never fires a stale flick). Turning is
/// also disabled in <see cref="VRMode.Menu2D"/> (no table exists; dev-proxy runs
/// exempt). Test #13: turning is ACTIVE in <see cref="VRMode.ModalUI"/> — nothing
/// modal reads the stick, and the player must keep full diorama movement while a
/// dialog floats. It is further suppressed while the turn hand participates in a
/// world grab.
/// </summary>
internal sealed class SnapTurn : MonoBehaviour
{
    private const float SnapEngageThreshold = 0.7f;
    private const float SnapRearmThreshold = 0.3f;
    private const float SmoothDeadzone = 0.2f;

    internal static SnapTurn? Instance { get; private set; }

    private bool _armed = true;

    /// <summary>True while a snap flick has fired and the stick hasn't re-centered (gizmos).</summary>
    internal bool WaitingForRearm => !_armed;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        Transform? rig = RigTarget.Current;
        if (rig == null || !ComfortSettings.IsBound)
            return;

        TurnMode mode = ComfortSettings.Turn.Value;
        if (mode == TurnMode.Off)
            return;

        VRMode vrMode = VRModeStateMachine.CurrentMode;
        // Test #13: ModalUI no longer suppresses turning (see class doc).
        if (vrMode == VRMode.BoardTargeting
            || (vrMode == VRMode.Menu2D && !RigTarget.IsDevProxy))
        {
            _armed = true; // never fire a stale flick when the stick is handed back
            return;
        }

        VRHand? hand = ResolveTurnHand();
        if (hand == null || !hand.HasPose)
            return;
        if (WorldGrab.Instance != null && WorldGrab.Instance.IsHandGrabbing(hand))
            return;

        float x = hand.Thumbstick.x;

        if (mode == TurnMode.Snap)
        {
            float ax = Mathf.Abs(x);
            if (_armed && ax >= SnapEngageThreshold)
            {
                _armed = false;
                Turn(rig, Mathf.Sign(x) * ComfortSettings.SnapTurnDegrees.Value);
                hand.SendHaptic(HapticPreset.ClickPulse);
            }
            else if (!_armed && ax <= SnapRearmThreshold)
            {
                _armed = true;
            }
        }
        else // Smooth
        {
            float ax = Mathf.Abs(x);
            if (ax <= SmoothDeadzone)
                return;
            float response = (ax - SmoothDeadzone) / (1f - SmoothDeadzone);
            Turn(rig, Mathf.Sign(x) * response * ComfortSettings.SmoothTurnSpeed.Value * Time.deltaTime);
        }
    }

    /// <summary>Yaw the rig around the HMD world position — the head never translates.</summary>
    private static void Turn(Transform rig, float degrees)
    {
        Camera? head = VRRigDriver.HeadCamera;
        Vector3 pivot = head != null ? head.transform.position : rig.position;
        rig.RotateAround(pivot, Vector3.up, degrees);
        // World tilt: the whole scene just yawed under the player, so the tilt-toward
        // axis must co-rotate THIS frame — an eased catch-up would read as the horizon
        // slowly rolling right after every turn (comfort: see VRRigDriver.TickWorldTilt).
        VRRigDriver.NotifyTiltAxisSnap("stick turn");
        // Tutorial camera step: a stick turn (snap = one full step, smooth = per-frame
        // degrees, both arrive here) is camera familiarization for the tutorial bridge
        // (Compat.TutorialVR; cold path = two static reads outside tutorials).
        Compat.TutorialVR.NotifyLocomotion(0f, Mathf.Abs(degrees), 0f);
        // Spawn ring: a stick turn is the player choosing their own facing — the multiplayer
        // join placement must never override that afterwards (VRRigDriver.NotifyPlayerLocomotion).
        VRRigDriver.NotifyPlayerLocomotion("stick turn");
    }

    private static VRHand? ResolveTurnHand() =>
        ComfortSettings.TurnHand.Value switch
        {
            TurnHandChoice.Left => VRHands.Left,
            TurnHandChoice.Right => VRHands.Right,
            _ => VRHands.Primary,
        };
}
