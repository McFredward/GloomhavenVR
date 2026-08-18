using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// Thumbstick turning: flick the configured hand's stick left/right to yaw the rig
/// around the CURRENT HMD position (the head stays put; the table pivots around you).
/// <c>[Comfort] TurnMode</c>: Snap (default, <c>SnapTurnDegrees</c> per flick with
/// engage/re-arm hysteresis) / Smooth (<c>SmoothTurnSpeed</c> °/s) / Off.
///
/// BOARD STATE NEVER BLOCKS TURNING (user, hardware ModBuild 138: "Wenn ich die
/// Bewegung bei einem Character ausgewählt habe und das Feld auswählen soll, kann ich
/// immer noch nicht mit dem joystick drehen. Die drehung soll nie blockiert sein!").
/// This class used to hard-disable turning throughout <see cref="VRMode.BoardTargeting"/>
/// because "targeting owns the thumbstick" — Phase-3a rotates AoE patterns with it. Two
/// hardware reports proved the MODE is the wrong question: it is derived from the SHARED
/// Choreographer wait-state (so a peer's pending move froze everyone's turning), and it is
/// also true where the AoE rotation it protected declines to run. The ruling is now
/// unconditional: turning is never suppressed for what the BOARD is doing — only for a
/// consumer that would really read this same physical axis this frame, which
/// <see cref="LocalTurnControl.TargetingOwnsStick"/> asks the consumer itself. And since
/// AoE rotation now lives on the hand <c>[Comfort] TurnHand</c> does not use, even that
/// answer is structurally "no". Full reasoning: <see cref="LocalTurnControl"/>.
///
/// The suppressions that REMAIN and must not be "finished off" by a later cleanup:
/// <see cref="VRMode.Menu2D"/> (the flat 2D menu — there is no board in front of you to
/// turn around; dev-proxy runs exempt), the world grab (the turn hand is already moving
/// the player with that drag) and menu scrolling (below — the user's own ruling). Test
/// #13: turning is ACTIVE in <see cref="VRMode.ModalUI"/> — nothing modal reads the
/// stick, and the player must keep full diorama movement while a dialog floats.
///
/// MENU SCROLLING OWNS THIS STICK TOO (user, hardware 2026-08-11: "Während dessen man
/// in einem menu scrollt soll auch die Drehung blockiert sein, das passiert mir immer
/// wieder versehentlich ungewollt."). Scrolling reads the stick's y axis, turning its x,
/// and on his rig both sit on the RIGHT controller — so the incidental sideways component
/// of a thumb pushing a list up yawed the world, continuously, because the shipped turn
/// mode is Smooth with a 0.2 deadzone. The suppression, its release edge and the two ways
/// out of it live in <see cref="ScrollTurnGate"/>; this class only feeds it. Note what the
/// condition is NOT keyed on: an open menu. That would contradict the ModalUI ruling
/// directly above — the signal is <c>UiScrollFocus</c>, i.e. THIS hand's own pointer on a
/// scrollable whose content really overflows, published by the code that delivers the
/// wheel, which is the same authority <c>Flight.ScrollAllowed</c> already answers to.
/// </summary>
internal sealed class SnapTurn : MonoBehaviour
{
    private const float SnapEngageThreshold = 0.7f;
    private const float SnapRearmThreshold = 0.3f;
    private const float SmoothDeadzone = 0.2f;

    internal static SnapTurn? Instance { get; private set; }

    private bool _armed = true;

    /// <summary>Scroll-vs-turn arbitration for the turn stick (see <see cref="ScrollTurnGate"/>).</summary>
    private ScrollTurnGate _scrollGate;

    /// <summary>
    /// Which hand the gate's state belongs to. A live <c>[Comfort] TurnHand</c> switch — which the
    /// player performs IN the scrollable options list this feature blocks on — must not carry a
    /// latch built from one stick over to the other, so the gate is reset when this changes.
    /// </summary>
    private HandSide? _gateHand;

    /// <summary>Last logged scroll-block verdict, so the diagnostic is edge-only (Flight's contract).</summary>
    private bool _loggedScrollBlock;

    /// <summary>True while a snap flick has fired and the stick hasn't re-centered (gizmos).</summary>
    internal bool WaitingForRearm => !_armed;

    /// <summary>True while menu scrolling is holding the turn axis down (gizmos / diagnostics).</summary>
    internal bool ScrollBlocked => _scrollGate.IsBlocking;

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
        {
            _scrollGate.Reset(); // fail open: no latch survives turning being switched off
            return;
        }

        VRMode vrMode = VRModeStateMachine.CurrentMode;
        // Test #13: ModalUI no longer suppresses turning (see class doc).
        //
        // TURN NEVER (user, hardware ModBuild 138 — class doc). Note what is NOT tested here any
        // more: the MODE. The question is asked of the consumer instead, and TargetingOwnsStick is
        // true only while an AoE pattern would REALLY rotate on this very stick — which, since
        // AoeControl moved to the non-turn hand, it never is. Rejected alternatives: LocalTurnControl.
        //
        // Menu2D STAYS, and is not an oversight: it is the flat 2D menu, where there is no board in
        // front of the player to turn around at all (dev-proxy runs exempt). The menu-scroll gate
        // further down stays too — that one is the user's own ruling ("Scrollen soll mehr dominant
        // sein", 2026-08-11). Neither is board state, so neither is touched by TURN NEVER.
        if (LocalTurnControl.TargetingOwnsStick
            || (vrMode == VRMode.Menu2D && !RigTarget.IsDevProxy))
        {
            _armed = true; // never fire a stale flick when the stick is handed back
            _scrollGate.Reset(); // …and no stale scroll latch either: this mode owns the stick
            return;
        }

        VRHand? hand = ResolveTurnHand();
        if (hand == null || !hand.HasPose)
        {
            // Fail open. !HasPose here is a REAL loss, not a blip — VRHand.HoldPoseThroughGap
            // keeps the pose (and the last input) through short dropouts, so reaching this line
            // means the controller has been gone past that grace and whatever the stick was doing
            // when it left is no longer information.
            _scrollGate.Reset();
            _gateHand = null;
            return;
        }
        if (_gateHand != hand.Side)
        {
            // Turn hand switched (or first frame): the latch describes one physical stick and
            // means nothing on the other. The player performs this switch INSIDE the scrollable
            // options list this feature blocks on, so it is not a hypothetical path.
            _gateHand = hand.Side;
            _scrollGate.Reset();
        }
        if (WorldGrab.Instance != null && WorldGrab.Instance.IsHandGrabbing(hand))
            return;

        float x = hand.Thumbstick.x;

        // SCROLLING OWNS THIS STICK (user 2026-08-11 — see the class doc). The verdict, its
        // release edge and its two exits are ScrollTurnGate's; the re-arm threshold handed to it
        // is the deflection THIS mode already treats as no input, so "the axis is back at rest"
        // never means something the mode itself would have turned on.
        float rearm = mode == TurnMode.Snap ? SnapRearmThreshold : SmoothDeadzone;
        bool scrollOwnsStick = UiScrollFocus.IsScrolling(hand);
        if (!_scrollGate.Evaluate(scrollOwnsStick, x, hand.Thumbstick.y, rearm))
        {
            LogScrollBlock(hand, blocked: true);
            return;
        }
        LogScrollBlock(hand, blocked: false);

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

    /// <summary>
    /// Edge-only diagnostic for the scroll block, same contract as <c>Flight.ScrollAllowed</c>'s:
    /// one line per change of verdict, never per frame. The suppression line carries
    /// <c>UiScrollFocus.Describe</c> — WHICH surface stamped the hover, by which producer, how
    /// old the stamp is. That attribution is not decoration: the 2026-08-04 flight-latch hunt
    /// cost three hardware rounds precisely because the log said "SUSPENDED" and named no
    /// suppressor, and this gate reads the very same signal.
    /// </summary>
    private void LogScrollBlock(VRHand hand, bool blocked)
    {
        if (blocked == _loggedScrollBlock)
            return;
        _loggedScrollBlock = blocked;
        VRLog.Info("Comfort", blocked
            ? $"stick turn: SUSPENDED on the {hand.Side} hand — its pointer is on a scrollable menu " +
              "list and scrolling owns this stick, so the sideways drift of a scroll push no longer " +
              "yaws the world (user 2026-08-11). It comes back when the stick's sideways axis returns " +
              $"to rest, and a deliberate sideways flick (|x| ≥ {ScrollTurnGate.DeliberateDeflection:F2} " +
              $"and more sideways than forward) turns even now. [{UiScrollFocus.Describe(hand)}]"
            : $"stick turn: RESUMED on the {hand.Side} hand.");
    }

    /// <summary>
    /// Through <see cref="LocalTurnControl.Resolve"/>, not a local copy of the same switch: the
    /// turn hand is now load-bearing for a THIRD party — <c>AoeControl</c> takes the opposite of
    /// it — and two copies of "which stick is the turn stick" would be two places to disagree.
    /// </summary>
    private static VRHand? ResolveTurnHand() =>
        VRHands.Get(LocalTurnControl.Resolve(ComfortSettings.TurnHand.Value));
}
