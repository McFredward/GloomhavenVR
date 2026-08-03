using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// STICK FLIGHT (user request 2026-08-03: "Ich moechte das es moeglich ist mit dem linken Joystick
/// nach vorne zu bewegen. Dabei soll die Direktion entweder dem Kopf (HMD) oder der dominanten Hand
/// folgen (einstellbar). Die Maximalgeschwindigkeit soll auch einstellbar sein.").
///
/// <para>Push the flight hand's thumbstick FORWARD and the rig flies along the chosen direction;
/// pull it back and it flies backwards. Everything about it is live-configurable in the in-VR
/// options menu under "Bewegung &amp; Drehen": <c>[Comfort] FlightEnabled</c>,
/// <c>FlightDirection</c> (head vs. dominant hand), <c>FlightMaxSpeed</c> and <c>FlightHand</c>.</para>
///
/// <para>WHY THIS DOES NOT COLLIDE WITH TURNING, which owns the same stick hardware:
/// <see cref="SnapTurn"/> reads the stick's SIDEWAYS axis (<c>Thumbstick.x</c>) and nothing else;
/// flight reads the FORWARD axis (<c>Thumbstick.y</c>) and nothing else. The two are orthogonal by
/// construction, so even with both features pointed at one and the same stick a diagonal push turns
/// AND flies rather than one starving the other. With the shipped defaults they are not even on the
/// same stick: <c>[Comfort] TurnHand</c> is Right, <c>FlightHand</c> is Left — the layout the
/// request describes and the one most VR titles use.</para>
///
/// <para>SPEED IS IN APPARENT METRES, NOT WORLD UNITS, and that is the one non-obvious decision
/// here. The rig root is SCALED (the diorama runs at ~12x, and the player re-scales it by pinching),
/// so a world unit is not what the player perceives as a metre — at 12x, one apparent metre of
/// movement is twelve world units. Translating by a raw world-unit speed would therefore make
/// flight crawl when the player zooms out and bolt when they zoom in, i.e. the dial would mean
/// something different after every pinch. The step is scaled by the live rig scale, so
/// "3 m/s" means three metres per second AS SEEN, at every table size, and the setting keeps its
/// meaning for the whole session.</para>
///
/// <para>PITCH IS INCLUDED — it is FLIGHT, not walking. Look (or point) up and you climb; the
/// direction vector is used whole. That is what makes it useful for looking over a big scenario
/// map, and it is why the head option is the default: the head cannot aim somewhere you are not
/// already looking, which is the steadier of the two.</para>
///
/// <para>MULTIPLAYER: NOTHING TO SEND. This is deliberate, not an omission. Every avatar pose on
/// the wire is sampled in the SHARED BOARD FRAME (<c>LocalRigSampler.TrySample</c> runs the head and
/// both hands through <c>IBoardAnchor.ToAnchor</c>), so moving the rig root changes exactly what is
/// already transmitted 15 times a second and eased on the receiving side by the same interpolation
/// that smooths every other motion. Flying is, on the wire, indistinguishable from walking across
/// the room — which is the requirement ("wie jede andere auch smooth im MP uebertragen"). A
/// dedicated flight message would be a SECOND source of truth for one position and could only ever
/// disagree with the first.</para>
///
/// <para>WHAT IT DELIBERATELY LEAVES ALONE. The rig ROOT moves; no game object is ever touched
/// (the house rule for every comfort feature). The board keeps its own contract: in FOLGEN it comes
/// along because the player moved, in FIXIERT it stays exactly where it was pinned — flight never
/// writes the board transform, so the "das Controllboard darf sich niemals von selbst bewegen"
/// ruling is untouched. And the origin guard cannot mistake this for a runtime re-origin: that
/// guard watches the head's TRACKING-SPACE pose (<c>_camera.transform.localPosition</c>), which
/// moving the rig root does not change by definition. The LateUpdate world-tilt pass does write
/// <c>rig.position</c>, but RELATIVELY — it rotates whatever position the rig currently has around
/// a pivot — so it composes with a flight step instead of discarding it (and it is inert at tilt 0,
/// which is where the parked tilt leaves it).</para>
/// </summary>
internal sealed class Flight : MonoBehaviour
{
    /// <summary>
    /// Stick deflection below which nothing happens. Matches <see cref="SnapTurn"/>'s smooth-turn
    /// deadzone: the same physical stick, so the same resting slop has to be ignored, and a player
    /// who has calibrated their thumb to one axis should not find the other one twitchier.
    /// </summary>
    private const float Deadzone = 0.2f;

    /// <summary>
    /// Below this the step is dropped entirely rather than applied. Guards two things at once: a
    /// direction vector that came back degenerate (a hand pose that collapsed to zero), and the
    /// pointless float churn of writing the rig transform every frame with nothing in it.
    /// </summary>
    private const float MinStepWorld = 1e-6f;

    internal static Flight? Instance { get; private set; }

    /// <summary>True on any frame the stick actually moved the rig (comfort gizmos / diagnostics).</summary>
    internal bool IsFlying { get; private set; }

    /// <summary>Apparent metres flown this frame, signed (+ forward). Zero when idle.</summary>
    internal float LastStepMeters { get; private set; }

    private bool _loggedNoDirection;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        IsFlying = false;
        LastStepMeters = 0f;

        Transform? rig = RigTarget.Current;
        if (rig == null || !ComfortSettings.IsBound)
            return;
        if (!ComfortSettings.FlightEnabled.Value)
            return;

        // MODE GATING, mirroring SnapTurn's documented stick-contention rule: BoardTargeting owns
        // the thumbstick outright (Phase-3a rotates AoE patterns with it), and Menu2D has no scene
        // to fly through. ModalUI is deliberately NOT excluded — the player must keep full movement
        // while a dialog floats, which is the same call turning already makes.
        VRMode mode = VRModeStateMachine.CurrentMode;
        if (mode == VRMode.BoardTargeting || (mode == VRMode.Menu2D && !RigTarget.IsDevProxy))
            return;

        VRHand? hand = ResolveFlightHand();
        if (hand == null || !hand.HasPose)
            return;
        // A hand that is dragging the world is already moving the player with that drag; letting the
        // same hand fly at the same time would apply two locomotion sources to one gesture.
        if (WorldGrab.Instance != null && WorldGrab.Instance.IsHandGrabbing(hand))
            return;

        float y = hand.Thumbstick.y;
        float ay = Mathf.Abs(y);
        if (ay <= Deadzone)
            return;

        // Deadzone-compensated response, then SQUARED: fine control near the centre (nudging into
        // position over a hex) while a full push still reaches exactly FlightMaxSpeed — squaring
        // maps 1 to 1, so the configured maximum stays literally the maximum, which is how the
        // setting is described to the player ("bei voll durchgedruecktem Stick").
        float response = (ay - Deadzone) / (1f - Deadzone);
        response *= response;

        if (!TryDirection(out Vector3 dir))
            return;

        // UNSCALED time: the game pauses (timeScale 0) behind menus and dialogs, and a player who
        // cannot reposition while a dialog is up would read that as flight being broken.
        float meters = Mathf.Sign(y) * response
                       * ComfortSettings.FlightMaxSpeed.Value * Time.unscaledDeltaTime;

        // Apparent metres -> world units through the LIVE rig scale (see the class doc). lossyScale
        // because the rig may sit under a scaled parent; guarded because a degenerate scale would
        // otherwise silently freeze or explode the step.
        float scale = rig.lossyScale.x;
        if (!(scale > 0f) || float.IsInfinity(scale))
            scale = 1f;

        Vector3 step = dir * (meters * scale);
        if (step.sqrMagnitude < MinStepWorld * MinStepWorld)
            return;
        if (float.IsNaN(step.x) || float.IsNaN(step.y) || float.IsNaN(step.z))
            return; // a NaN here would fling the rig out of the world and never come back

        rig.position += step;
        IsFlying = true;
        LastStepMeters = meters;

        // Tutorial camera step: flying is camera familiarization exactly like a stick turn is, and
        // the bridge measures it in metres (cold path = two static reads outside scripted levels).
        Compat.TutorialVR.NotifyLocomotion(Mathf.Abs(meters), 0f, 0f);
        // Spawn ring: the player moving THEMSELVES closes the join-placement window, so a late
        // multiplayer seat correction can never yank a flying player back to the ring.
        VRRigDriver.NotifyPlayerLocomotion("stick flight");
    }

    /// <summary>
    /// The direction the step is taken along, world space and normalized.
    ///
    /// <para>Head: the HMD's forward. Hand: the DOMINANT hand's aim ray — deliberately
    /// <see cref="VRHand.GetAimRay"/> rather than the hand transform's forward, because that is the
    /// very ray the laser draws, so "fly where I point" means the line the player can SEE. It also
    /// stays readable while that hand is holding something, which the raw pick does not.</para>
    ///
    /// <para>Note the asymmetry, and it is intended: which hand FLIES is <c>[Comfort] FlightHand</c>
    /// (left by default), which hand AIMS is the dominant hand — the one that has the laser. A
    /// left-handed player flying with the left stick still steers with the laser they actually see.</para>
    /// </summary>
    private bool TryDirection(out Vector3 dir)
    {
        dir = Vector3.zero;

        if (ComfortSettings.FlightDirection.Value == FlightDirectionSource.Hand)
        {
            VRHand? aim = VRHands.Primary;
            if (aim != null && aim.HasPose)
            {
                aim.GetAimRay(out _, out Vector3 rayDir);
                dir = rayDir;
            }
        }
        else
        {
            Camera? head = VRRigDriver.HeadCamera;
            if (head != null)
                dir = head.transform.forward;
        }

        if (dir.sqrMagnitude < 1e-8f)
        {
            // No usable frame this tick (head camera not up yet, or the aim hand lost tracking
            // mid-push). Refusing to move is the only safe answer — a fallback direction would fly
            // the player somewhere they never aimed. Logged once so it cannot spam a held stick.
            if (!_loggedNoDirection)
            {
                _loggedNoDirection = true;
                VRLog.Info("Comfort", "stick flight: no usable direction this tick " +
                                      $"({ComfortSettings.FlightDirection.Value}) — the step is " +
                                      "skipped rather than guessed. Flight resumes by itself the " +
                                      "frame the head camera or the aim hand is back.");
            }
            return false;
        }

        _loggedNoDirection = false;
        dir.Normalize();
        return true;
    }

    private static VRHand? ResolveFlightHand() =>
        ComfortSettings.FlightHand.Value switch
        {
            TurnHandChoice.Left => VRHands.Left,
            TurnHandChoice.Right => VRHands.Right,
            _ => VRHands.Primary,
        };
}
