using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// STICK FLIGHT (user request 2026-08-03: "Ich moechte das es moeglich ist mit dem linken Joystick
/// nach vorne zu bewegen. Dabei soll die Direktion entweder dem Kopf (HMD) oder der dominanten Hand
/// folgen (einstellbar). Die Maximalgeschwindigkeit soll auch einstellbar sein.").
///
/// <para>Push the flight hand's thumbstick FORWARD and the rig flies along the chosen direction,
/// pull it back to fly backwards, push it SIDEWAYS to strafe level left/right. Everything about it
/// is live-configurable in the in-VR options menu under "Bewegung &amp; Drehen":
/// <c>[Comfort] FlightEnabled</c>, <c>FlightDirection</c> (head vs. dominant hand),
/// <c>FlightMaxSpeed</c> and <c>FlightHand</c>.</para>
///
/// <para>THE STICK IS SHARED, so this class arbitrates twice. Sideways with TURNING (below), and
/// FORWARD with MENU SCROLLING — see <see cref="ScrollAllowed"/>: while the flight hand's own
/// pointer rests on a list that can actually scroll, that hand does not fly at all, because both
/// features read the same forward axis and the player pushing it there means "scroll". Note what
/// that rule is NOT keyed on: an open menu. A floating dialog must never cost the player their
/// movement — the same call the mode gate below makes, and the same one turning makes.</para>
///
/// <para>THE SIDEWAYS AXIS IS SHARED — with TURNING (<see cref="SnapTurn"/> reads
/// <c>Thumbstick.x</c>) and, since ModBuild 138, with AOE PATTERN ROTATION, which deliberately
/// takes the hand <c>[Comfort] TurnHand</c> does NOT use (TURN NEVER: the user's ruling that
/// turning may never be blocked is only satisfiable by un-sharing the axis rather than arbitrating
/// it — so with the shipped turn-Right default the rotation lands on the LEFT stick, which is also
/// the default <c>FlightHand</c>). STRAFE is the one that yields, hand-accurately and only while a
/// claim is real; forward/backward flight is never affected, because nothing else reads that axis.
/// The arbitration and its reasons live at <see cref="StrafeAllowed"/>.</para>
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
/// <para>VERTICAL LIFT RIDES THE *TURN* STICK, and it is the one part of this class that reads a
/// controller this class does not otherwise own (user request 2026-08-15: "Ich will es auch
/// Optional einstelbar machen, dass in der Hand mit der man dreht auch beim Joystick hoch und
/// runter entsprechend nach oben und unten fährt mit der Fluggeschwindigkeit."). It is off by
/// default (<c>[Comfort] TurnStickVertical</c>), it uses THIS class's speed dial and THIS class's
/// step guards rather than a second vertical-motion path with its own numbers, and it can never
/// take a degree of turning away — see <see cref="TickVerticalLift"/> for the axis-separation rule
/// and <see cref="LiftAllowed"/> for who outranks it.</para>
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

    /// <summary>Apparent metres flown this frame (magnitude, any direction). Zero when idle.</summary>
    internal float LastStepMeters { get; private set; }

    /// <summary>True on any frame the TURN stick's vertical axis lifted the rig (gizmos / diagnostics).</summary>
    internal bool IsLifting { get; private set; }

    /// <summary>Signed apparent metres/second of vertical lift this frame (+up). Zero when idle.</summary>
    internal float LastLiftSpeed { get; private set; }

    private bool _loggedNoDirection;

    /// <summary>The reason stick flight is currently doing nothing, or null while it is live.
    /// Change-gated — see <see cref="ReportIdle"/>.</summary>
    private string? _idleReason = "not evaluated yet";

    /// <summary>
    /// SAY WHY THE STICK DOES NOTHING (ModBuild 180). Every early-out above this point used to
    /// return in silence, so "Ich kann mich nicht mit dem Joystick fortbewegen trotz richtiger
    /// Einstellung" produced a log in which flight simply did not appear — indistinguishable from
    /// a setting being off, a hand not tracked, and the world grab owning the stick. The scroll
    /// suppression a few lines below already had exactly this line and it is what made THAT class
    /// of report solvable in one round; the other four gates did not.
    ///
    /// <para>Change-gated to one line per transition, so a session in which flight is simply on
    /// costs one line and a session in which it is refused says which gate refused it.</para>
    /// </summary>
    private void ReportIdle(string? reason)
    {
        if (reason == _idleReason)
            return;
        _idleReason = reason;
        VRLog.Info("Comfort", reason == null
            ? "stick flight: LIVE — past every gate; the stick now moves the player."
            : $"stick flight: doing nothing because {reason}. (Mode {VRModeStateMachine.CurrentMode}, "
              + $"FlightEnabled={(ComfortSettings.IsBound ? ComfortSettings.FlightEnabled.Value.ToString() : "unbound")}, "
              + $"FlightHand={(ComfortSettings.IsBound ? ComfortSettings.FlightHand.Value.ToString() : "unbound")}, "
              + $"TurnHand={(ComfortSettings.IsBound ? ComfortSettings.TurnHand.Value.ToString() : "unbound")}.)");
    }

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
        IsLifting = false;
        LastLiftSpeed = 0f;

        Transform? rig = RigTarget.Current;
        if (rig == null || !ComfortSettings.IsBound)
        {
            ReportIdle(rig == null ? "no rig transform yet" : "ComfortSettings not bound yet");
            ReleaseLift();
            return;
        }
        if (!ComfortSettings.FlightEnabled.Value)
        {
            ReportIdle("[Comfort] FlightEnabled is OFF in the config");
            ReleaseLift();
            return;
        }

        // MODE GATING. Menu2D has no scene to fly through. ModalUI is deliberately NOT excluded —
        // the player must keep full movement while a dialog floats, the same call turning makes.
        //
        // BOARDTARGETING IS NO LONGER A BLANKET BLOCK (user, hardware test 2026-08-03: "Obwohl
        // kontinuierliche Bewegung aktiviert ist, funktioniert sie nicht direkt nach dem
        // Szenariostart"). It was copied from SnapTurn's rule as "targeting owns the thumbstick",
        // but that is not what targeting does: AoeControl reads Thumbstick.x ALONE (one 60-degree
        // step per horizontal flick, see AoeControl.Tick) and never touches the forward axis. Meanwhile
        // WaitingForTileSelected is a targeting state — which is exactly what the player sits in
        // right after a scenario starts, while placing their figure — so the blanket block made
        // flight look broken during the first minutes of every scenario, precisely when a player
        // most wants to fly around and look at the map. Forward/back flight runs here; only STRAFE
        // stands down, because that is the axis AoE really owns (see StrafeAllowed, same rule as
        // the turning contest).
        // …AND "Menu2D has no scene to fly through" IS NOW EXACT RATHER THAN APPROXIMATE. Since
        // ModBuild 178 the 3D map room resolves to TableIdle, not Menu2D (VRModeStateMachine's
        // TableInFrontOfPlayer feeds the composition), so this line no longer stands the player
        // still in a room that DOES have a scene to fly through. No test is needed here for that:
        // the mode itself carries the fact, and one mechanism is the point.
        VRMode mode = VRModeStateMachine.CurrentMode;
        if (mode == VRMode.Menu2D && !RigTarget.IsDevProxy)
        {
            ReportIdle("the mode is Menu2D (the flat 2D menu — no scene to fly through)");
            ReleaseLift();
            return;
        }

        // VERTICAL LIFT FIRST, and on a DIFFERENT STICK — it reads [Comfort] TurnHand, the
        // forward/strafe block below reads [Comfort] FlightHand. It runs before the block's own
        // early-outs (no hand, world grab, menu scroll on the FLIGHT hand) because none of those
        // say anything about the turn hand: a player scrolling with the flight hand must still be
        // able to lift with the other one. It has its own copies of exactly those gates, asked of
        // its own hand.
        TickVerticalLift(rig);

        VRHand? hand = ResolveFlightHand();
        if (hand == null || !hand.HasPose)
        {
            ReportIdle($"the [Comfort] FlightHand ({ComfortSettings.FlightHand.Value}) has no tracked pose");
            return;
        }
        // A hand that is dragging the world is already moving the player with that drag; letting the
        // same hand fly at the same time would apply two locomotion sources to one gesture.
        if (WorldGrab.Instance != null && WorldGrab.Instance.IsHandGrabbing(hand))
        {
            ReportIdle($"the {hand.Side} hand is world-grabbing (thumbstick CLICK held) — the drag "
                       + "already moves the player, so the same stick may not also fly");
            return;
        }
        ReportIdle(null); // past every gate: flight is live on this hand
        // MENU SCROLLING OWNS THIS STICK while the same hand is pointing at a live list — see
        // ScrollAllowed for why scrolling wins and why "a menu is open" is deliberately not the test.
        if (!ScrollAllowed(hand))
            return;

        // BOTH AXES (user, 2026-08-03: "Man soll auch mit dem joystick links und rechts seitwaerts
        // fliegen koennen"). Forward/back rides y, strafe rides x — but x is also TURNING's axis,
        // so strafe is only taken when turning is not listening to this same stick. See
        // StrafeAllowed for why that arbitration goes turning's way.
        Vector2 stick = hand.Thumbstick;
        float sideways = StrafeAllowed(hand) ? stick.x : 0f;
        var raw = new Vector2(sideways, stick.y);

        // The deadzone is applied to the stick's MAGNITUDE, not per axis: a per-axis deadzone makes
        // a diagonal push start moving on one axis before the other, which reads as the stick
        // snapping to the cardinal directions.
        float mag = raw.magnitude;
        if (mag <= Deadzone)
            return;

        // Deadzone-compensated response, then SQUARED: fine control near the centre (nudging into
        // position over a hex) while a full push still reaches exactly FlightMaxSpeed — squaring
        // maps 1 to 1, so the configured maximum stays literally the maximum, which is how the
        // setting is described to the player ("bei voll durchgedruecktem Stick"). Taken from the
        // magnitude, so a diagonal is capped at the SAME top speed as a straight push instead of
        // being sqrt(2) faster.
        float response = (mag - Deadzone) / (1f - Deadzone);
        response = Mathf.Min(response, 1f);
        response *= response;

        if (!TryDirection(out Vector3 dir))
            return;

        // Strafe axis: horizontal-plane right of the flight direction. Perpendicular to WORLD UP by
        // construction, so flying sideways stays LEVEL — looking up while strafing must not make
        // the player climb sideways, which a fully 3D perpendicular would do.
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        if (right.sqrMagnitude < 1e-6f)
        {
            // Looking (or pointing) straight up/down: the horizontal right is undefined there.
            // Fall back to the head's own right, which is well defined at any pitch.
            Camera? head = VRRigDriver.HeadCamera;
            right = head != null ? Vector3.ProjectOnPlane(head.transform.right, Vector3.up) : Vector3.zero;
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.zero; // give up on strafe this tick; forward still flies
        }
        if (right.sqrMagnitude > 1e-6f)
            right.Normalize();

        Vector2 unit = raw / mag;
        // dir and right are orthonormal (right is perpendicular to dir by the cross product), so
        // this composite is itself unit length — the response above is the whole speed story.
        Vector3 heading = dir * unit.y + right * unit.x;
        if (heading.sqrMagnitude < 1e-8f)
            return;
        heading.Normalize();

        // UNSCALED time: the game pauses (timeScale 0) behind menus and dialogs, and a player who
        // cannot reposition while a dialog is up would read that as flight being broken.
        float meters = response * ComfortSettings.FlightMaxSpeed.Value * Time.unscaledDeltaTime;

        ApplyStep(rig, heading, meters, "stick flight");
    }

    /// <summary>
    /// Translate the rig by <paramref name="meters"/> APPARENT metres along a unit
    /// <paramref name="heading"/>, and tell everyone who has to know that the player moved.
    ///
    /// <para>THE ONE PLACE A FLIGHT STEP IS WRITTEN. Both the forward/strafe push and the turn
    /// stick's vertical lift come through here, so there is exactly one scale conversion, one
    /// minimum-step guard, one NaN guard and one pair of locomotion notifications for the whole
    /// feature. The lift was specified as "mit der Fluggeschwindigkeit" — with the flight speed —
    /// and sharing the tail is how that stays literally true instead of true-until-someone-edits-
    /// one-of-two-copies.</para>
    ///
    /// <para><paramref name="meters"/> is UNSIGNED distance for the readouts; the sign lives in
    /// <paramref name="heading"/>, which is why <see cref="LastStepMeters"/> accumulates a
    /// magnitude and can hold both contributions of one frame.</para>
    /// </summary>
    private bool ApplyStep(Transform rig, Vector3 heading, float meters, string what)
    {
        // Apparent metres -> world units through the LIVE rig scale (see the class doc). lossyScale
        // because the rig may sit under a scaled parent; guarded because a degenerate scale would
        // otherwise silently freeze or explode the step.
        float scale = rig.lossyScale.x;
        if (!(scale > 0f) || float.IsInfinity(scale))
            scale = 1f;

        Vector3 step = heading * (meters * scale);
        if (step.sqrMagnitude < MinStepWorld * MinStepWorld)
            return false;
        if (float.IsNaN(step.x) || float.IsNaN(step.y) || float.IsNaN(step.z))
            return false; // a NaN here would fling the rig out of the world and never come back

        rig.position += step;
        IsFlying = true;
        LastStepMeters += Mathf.Abs(meters);

        // Tutorial camera step: flying is camera familiarization exactly like a stick turn is, and
        // the bridge measures it in metres (cold path = two static reads outside scripted levels).
        Compat.TutorialVR.NotifyLocomotion(Mathf.Abs(meters), 0f, 0f);
        // Spawn ring: the player moving THEMSELVES closes the join-placement window, so a late
        // multiplayer seat correction can never yank a flying player back to the ring.
        VRRigDriver.NotifyPlayerLocomotion(what);
        return true;
    }

    // ==== VERTICAL LIFT ON THE TURN STICK ===============================================
    //
    // USER REQUEST 2026-08-15, verbatim: "Ich will es auch Optional einstelbar machen, dass in der
    // Hand mit der man dreht auch beim Joystick hoch und runter entsprechend nach oben und unten
    // fährt mit der Fluggeschwindigkeit."
    //
    // THE DESIGN IS THE AXIS SEPARATION, and it lives in Rig/LiftWedge.cs — one pure function and
    // the four numbers it is made of, written free of Unity beyond Mathf so it can be driven vector
    // by vector on the test harness (tests/GloomhavenVR.WireTests/LiftWedgeVectors.cs). Read that
    // file's doc for the rule itself, what it does at 45 degrees, why it is a RATIO rather than a
    // per-axis threshold, why the hysteresis is there and why Snap and Smooth need no separate
    // handling. None of it is restated here. What IS here is the arbitration (LiftAllowed), the
    // response curve, and the diagnostics.
    //
    // TURNING IS UNTOUCHED BY ALL OF IT. SnapTurn does not call LiftWedge, does not know it exists,
    // and reads its axis exactly as it did before this feature. The wedge can only ever decide when
    // the LIFT stands down — that is the only shape a feature on this stick may take (TURN NEVER,
    // user ruling ModBuild 138).

    /// <summary>True while the wedge is satisfied and the lift is running (hysteresis state).</summary>
    private bool _lifting;

    /// <summary>Last logged stand-down verdict, so the diagnostic is edge-only (this file's contract).
    /// Null = nothing logged yet, so the first verdict of a session is always written.</summary>
    private bool? _liftBlocked;

    /// <summary>Throttle for the axis-decision attribution line (unscaled seconds).</summary>
    private const float LiftDiagSeconds = 5f;
    private float _lastLiftDiagAt = float.NegativeInfinity;

    /// <summary>
    /// Drop the lift latch. Called from every path that leaves <see cref="Update"/> before the lift
    /// is evaluated, so a held stick can never resume a climb across a mode change or a config flip
    /// without passing the (stricter) engage test again.
    /// </summary>
    private void ReleaseLift() => _lifting = false;

    /// <summary>
    /// The TURN hand's forward axis, as world-vertical travel at the flight speed.
    ///
    /// <para>The axis-separation rule, the two turn modes and why the deadzone is larger than
    /// turning's are all argued in <see cref="LiftWedge"/> — read that first; this method only
    /// executes it.</para>
    ///
    /// <para>WORLD UP, never the head's up: nothing in this mod may re-orient with head movement,
    /// and "up" is the one direction a player is never confused about. It also means this path
    /// needs no direction vector at all, so unlike forward flight it keeps working on a frame where
    /// the head camera or the aim hand is missing.</para>
    /// </summary>
    private void TickVerticalLift(Transform rig)
    {
        if (!ComfortSettings.TurnStickVertical.Value)
        {
            ReleaseLift();
            return;
        }

        VRHand? hand = VRHands.Get(LocalTurnControl.Resolve(ComfortSettings.TurnHand.Value));
        if (hand == null || !hand.HasPose)
        {
            ReleaseLift();
            return;
        }
        if (!LiftAllowed(hand))
        {
            ReleaseLift();
            return;
        }

        Vector2 stick = hand.Thumbstick;
        float ax = Mathf.Abs(stick.x);
        float ay = Mathf.Abs(stick.y);

        bool inWedge = LiftWedge.Evaluate(stick.x, stick.y, _lifting);

        // WHICH AXIS WON, once per interval and only while the thumb is actually somewhere — the
        // diagnostic the report asks for. Throttled rather than edge-only because the interesting
        // reading is the NUMBERS (the stick vector against the wedge), which an edge line taken at
        // the moment of the flip would only ever show at the boundary.
        if (ay >= LiftWedge.SustainDeflection && Time.unscaledTime - _lastLiftDiagAt >= LiftDiagSeconds)
        {
            _lastLiftDiagAt = Time.unscaledTime;
            float ratio = ax > 1e-4f ? ay / ax : float.PositiveInfinity;
            VRLog.Info("Comfort", $"stick lift: {hand.Side} turn stick ({stick.x:F2}, {stick.y:F2}) — "
                                  + $"|y|/|x| = {(float.IsInfinity(ratio) ? "inf" : ratio.ToString("F2"))}, "
                                  + $"wedge needs {LiftWedge.Ratio(_lifting):F2} at "
                                  + $"|y| >= {LiftWedge.Deflection(_lifting):F2} -> "
                                  + (inWedge ? "VERTICAL wins" : "TURN axis wins, no lift")
                                  + ". Turning reads x regardless and is never suppressed by this feature; "
                                  + "at 45 degrees the turn axis always wins by design.");
        }

        if (!inWedge)
        {
            _lifting = false;
            return;
        }
        _lifting = true;

        // THE SAME RESPONSE SHAPE AND THE SAME DIAL as forward flight — deadzone-compensated then
        // squared, so a full push is exactly FlightMaxSpeed and small pushes creep. Measured from
        // the SUSTAIN deadzone rather than the engage one so the curve is continuous while held:
        // the first lifting frame starts at ((0.50-0.35)/0.65)² ≈ 5 % of full speed instead of
        // stepping straight to 23 %.
        float response = (ay - LiftWedge.SustainDeflection) / (1f - LiftWedge.SustainDeflection);
        response = Mathf.Clamp01(response);
        response *= response;

        // UNSCALED time, like every other step in this class: the game pauses behind dialogs and a
        // player who cannot reposition there would read that as the feature being broken.
        float speed = response * ComfortSettings.FlightMaxSpeed.Value;
        float meters = speed * Time.unscaledDeltaTime;
        float sign = Mathf.Sign(stick.y);

        if (!ApplyStep(rig, Vector3.up * sign, meters, "stick vertical lift"))
            return;

        IsLifting = true;
        LastLiftSpeed = speed * sign;
    }

    /// <summary>
    /// May the turn stick's forward axis be read as vertical lift this tick?
    ///
    /// <para>Three claimants outrank it, and all three are asked of the TURN hand specifically:</para>
    /// <list type="number">
    /// <item><b>FORWARD FLIGHT</b>, when <c>[Comfort] FlightHand</c> and <c>TurnHand</c> resolve to
    /// the same physical controller. Then one forward axis has two claimants and there is no signal
    /// left to tell them apart — pitch, hand, mode, nothing differs — so it must be arbitrated, and
    /// it goes the same way <see cref="StrafeAllowed"/> goes: THE OLDER, LOAD-BEARING CONTROL KEEPS
    /// THE AXIS. Losing forward flight to gain vertical is a strictly worse trade (there is only one
    /// flight hand, so the player would have no way left to fly at all), and the fix is one dropdown
    /// away. NOTE THAT THIS IS THE SHIPPED DEFAULT CONFIGURATION — both dials read Right — so the
    /// log line below is the first thing a player who switches this feature on will need, and it
    /// names the remedy rather than only the verdict.</item>
    /// <item><b>MENU SCROLLING</b>, which reads this very axis: a scroll push IS a forward push, so
    /// unlike the sideways contest there is nothing to arbitrate and scrolling simply wins, exactly
    /// as it does for forward flight (<see cref="ScrollAllowed"/>). Deliberately NOT
    /// <see cref="ScrollTurnGate"/>: that gate's escape hatch is a deliberate SIDEWAYS flick, which
    /// says nothing about whether the player meant to climb.</item>
    /// <item><b>THE WORLD GRAB</b> on that same hand, which is already moving the player with the
    /// drag — the same rule forward flight and turning both apply.</item>
    /// </list>
    ///
    /// <para>Logged on every CHANGE of the verdict, never per frame — this file's standing contract
    /// — and the suppression line names its suppressor, which is the lesson the 2026-08-04
    /// sentinel-latch hunt cost three hardware rounds to learn.</para>
    /// </summary>
    private bool LiftAllowed(VRHand hand)
    {
        bool flightOwnsForward = ComfortSettings.FlightEnabled.Value
                                 && SameHand(ComfortSettings.FlightHand.Value,
                                             ComfortSettings.TurnHand.Value);
        bool scrolling = UiScrollFocus.IsScrolling(hand);
        bool grabbing = WorldGrab.Instance != null && WorldGrab.Instance.IsHandGrabbing(hand);
        bool allowed = !flightOwnsForward && !scrolling && !grabbing;

        if (_liftBlocked != !allowed)
        {
            _liftBlocked = !allowed;
            if (allowed)
            {
                VRLog.Info("Comfort", $"stick vertical lift: ACTIVE on the {hand.Side} turn stick — "
                                      + "push it forward to rise, back to sink, at "
                                      + $"[Comfort] FlightMaxSpeed ({ComfortSettings.FlightMaxSpeed.Value:F2} "
                                      + "apparent m/s at full deflection). Turning is unaffected.");
            }
            else
            {
                string why = flightOwnsForward
                    ? "forward/backward FLIGHT already owns this stick's forward axis — [Comfort] "
                      + $"FlightHand ({ComfortSettings.FlightHand.Value}) and TurnHand "
                      + $"({ComfortSettings.TurnHand.Value}) resolve to the same controller. Put them "
                      + "on different hands (the arrangement this mod is built around is turn right / "
                      + "fly left), or switch [Comfort] FlightEnabled off, and the lift is yours"
                    : scrolling
                        ? $"menu scrolling owns it — [{UiScrollFocus.Describe(hand)}]. It comes back "
                          + "by itself the moment the beam leaves the list"
                        : "this hand is dragging the world, which is already moving the player";
                VRLog.Info("Comfort", $"stick vertical lift: STANDING DOWN on the {hand.Side} turn "
                                      + $"stick — {why}. Turning is untouched either way.");
            }
        }
        return allowed;
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

    /// <summary>
    /// May the stick's SIDEWAYS axis be read as strafe this tick?
    ///
    /// <para>Only when turning is not already listening to that same axis on that same stick. If a
    /// player points <c>[Comfort] FlightHand</c> and <c>TurnHand</c> at one controller and leaves
    /// turning on, the x axis has two claimants, and TURNING WINS: it is the older, load-bearing
    /// control (a player who cannot turn is stuck facing one way, a player who cannot strafe simply
    /// flies a curve), and silently stealing it would break a control the player already relies on.
    /// Turning being set to Off frees the axis, and so does putting the two features on different
    /// hands — which is the shipped default (turn right, fly left), so out of the box both work.</para>
    ///
    /// <para>Logged on every change of the verdict, not once ever: a player who moves the hands
    /// together and finds strafe gone deserves to see why in the log rather than wonder.</para>
    /// </summary>
    private bool StrafeAllowed(VRHand hand)
    {
        bool turningOnThisStick = ComfortSettings.Turn.Value != TurnMode.Off
                                  && SameHand(ComfortSettings.FlightHand.Value,
                                              ComfortSettings.TurnHand.Value);
        // AoE pattern rotation reads the sideways axis too (AoeControl — a horizontal flick rotates
        // the pattern one 60-degree step). It never touches the forward axis, which is why the mode
        // gate in Update does not refuse flight outright: the player keeps flying while aiming, they
        // just cannot strafe with the same flick that turns the pattern.
        //
        // The claim is asked of the CONSUMER (AoeControl via LocalTurnControl) and scoped to THIS
        // hand — both matter, and both were wrong before ModBuild 138: VRMode.BoardTargeting is true
        // on every peer AND throughout movement/waypoint selection, where AoeControl rotates
        // nothing, and since the hand split the rotation really does land on the default flight
        // stick, so a hand-blind test would punish the wrong controller. Full argument and the
        // rejected alternatives: LocalTurnControl's class doc.
        // Turning is deliberately NOT part of this arbitration (TURN NEVER); it keeps its own stick
        // unconditionally, and `turningOnThisStick` above is the unrelated, older contest between
        // flight and turning when the player points both dials at one controller.
        bool aoeOwnsSideways = LocalTurnControl.AoeOwnsStick(hand.Side);
        bool allowed = !turningOnThisStick && !aoeOwnsSideways;
        if (_strafeAllowed != allowed)
        {
            _strafeAllowed = allowed;
            VRLog.Info("Comfort", allowed
                ? "stick flight: sideways strafe ON — the flight stick's sideways axis is free."
                : $"stick flight: sideways strafe OFF — the {hand.Side} stick's sideways axis is " +
                  "claimed (a live AoE pattern is rotating on this hand, or turning is on this same " +
                  "hand). Put flight and turning on different hands ([Comfort] FlightHand / TurnHand) " +
                  "to settle the second case; the first lasts only as long as the AoE aim. Forward " +
                  "and backward flight are unaffected either way.");
        }
        return allowed;
    }

    /// <summary>
    /// May this hand's stick be read as FLIGHT this tick, or has menu scrolling taken it?
    ///
    /// <para>User 2026-08-03: "Wenn man die rechte Hand eingestellt hat zum Fliegen und dann aber im
    /// Menue scrollen will, passiert beides. Scrollen soll mehr dominant sein und das Fliegen
    /// ueberschreiben." Both features read the stick's FORWARD axis — scrolling as list travel,
    /// flight as forward/back — so a player walking a settings list down also flew across the room
    /// while reading it. Unlike the strafe/turn contest above, this one does NOT go to the older
    /// control: SCROLLING WINS. A scroll is an aimed, deliberate act on a surface the player is
    /// pointing at and the stick is the only way to perform it; flight is ambient locomotion that is
    /// available again the moment the beam leaves the list. Losing the scroll costs the player the
    /// thing they came for, losing a fraction of a second of flight costs nothing.</para>
    ///
    /// <para>THE TEST IS NOT "IS A MENU OPEN". That would contradict the standing ruling this class
    /// already honours in its mode gate — <c>VRMode.ModalUI</c> keeps flight (and turning) alive
    /// precisely so a floating dialog never takes the player's movement away. The signal used here
    /// is <c>Hands.Interact.UiScrollFocus</c>: this hand's own pointer resting on a scrollable whose
    /// content really overflows its viewport, published by the very code that delivers the wheel, so
    /// there is no second opinion to drift out of sync.</para>
    ///
    /// <para>PER HAND: the query is scoped to the FLIGHT hand only, so a player scrolling with the
    /// off hand keeps flying with the other. TURNING now makes the same query for its own hand
    /// (<see cref="ScrollTurnGate"/>, user 2026-08-11) — the clause that used to stand here,
    /// "turning is untouched in every case (it reads the sideways axis, which no scroll path
    /// claims)", was true about the AXES and wrong about the THUMB: a scroll push carries a
    /// sideways component well past Smooth turn's 0.2 deadzone. Turning's gate is stricter than
    /// this one because losing turning matters more than losing flight: a deliberate sideways
    /// flick is still honoured there, and the block does not lift until the axis re-centres.</para>
    ///
    /// <para>NON-LATCHING: the underlying hover stamp expires by itself, so the frame the beam
    /// leaves the list, flight is back. The only hold is the short grace UiScrollFocus arms from an
    /// ACTUALLY delivered scroll, which exists so a one-frame hover dropout mid-push cannot fire a
    /// single flight step (felt as a lurch); merely hovering never arms it.</para>
    ///
    /// <para>Logged on every change of the verdict, not per frame — same contract as
    /// <see cref="StrafeAllowed"/>: a player whose stick stopped flying deserves the reason in the
    /// log, and a per-frame line during a long scroll would bury it. Additionally, while the
    /// verdict is "suppressed" AND the stick is actually pushed, a throttled attribution line
    /// names the stamping surface/producer and the stamp age (<see cref="UiScrollFocus.Describe"/>)
    /// — the diagnostic the 2026-08-04 sentinel-latch hunt lacked.</para>
    /// </summary>
    private bool ScrollAllowed(VRHand hand)
    {
        bool scrolling = UiScrollFocus.IsScrolling(hand);
        if (_scrollBlocked != scrolling)
        {
            _scrollBlocked = scrolling;
            VRLog.Info("Comfort", scrolling
                ? $"stick flight: SUSPENDED on the {hand.Side} hand — its pointer is on a scrollable " +
                  "menu list, and scrolling owns the stick's forward axis while it is. Flight returns " +
                  "by itself the moment the beam leaves the list; the other hand is unaffected, and " +
                  $"turning never was. [{UiScrollFocus.Describe(hand)}]"
                : $"stick flight: RESUMED on the {hand.Side} hand — its pointer is no longer on a " +
                  "scrollable menu list.");
        }
        // ATTRIBUTION WHILE IT HURTS (2026-08-04 sentinel latch, see UiScrollFocus.NeverStamped):
        // that hardware round burned because "SUSPENDED" named no suppressor — the log could not
        // say WHICH surface stamped the hover, so the dead virgin-slot path was indistinguishable
        // from a real hover. This line repeats the attribution, throttled, exactly while the
        // player is EXPERIENCING the suppression: the stick is pushed past the flight deadzone on
        // a hand whose flight is refused. During a deliberate menu scroll it names the scrolled
        // list once per interval (harmless, even useful); if the suppression is ever wrong again,
        // the very first push writes the culprit surface, producer and stamp age into the log.
        if (scrolling && Mathf.Abs(hand.Thumbstick.y) > Deadzone
            && Time.unscaledTime - _lastScrollBlockDiagAt >= ScrollBlockDiagSeconds)
        {
            _lastScrollBlockDiagAt = Time.unscaledTime;
            VRLog.Info("Comfort", $"stick flight: suppressed push on the {hand.Side} hand — " +
                                  $"{UiScrollFocus.Describe(hand)}.");
        }
        return !scrolling;
    }

    private bool _scrollBlocked;

    /// <summary>Throttle for the suppressed-push attribution line (unscaled seconds).</summary>
    private const float ScrollBlockDiagSeconds = 5f;
    private float _lastScrollBlockDiagAt = float.NegativeInfinity;

    /// <summary>
    /// Do two hand choices resolve to the same physical controller? Dominant is not a hand, it is a
    /// pointer to one — resolve both before comparing, or "Dominant vs Right" would read as
    /// different hands on a right-handed rig. The resolver itself moved to
    /// <see cref="LocalTurnControl.Resolve"/> when AoeControl became a third party that has to
    /// agree with it about which stick is which.
    /// </summary>
    private static bool SameHand(TurnHandChoice a, TurnHandChoice b) =>
        LocalTurnControl.Resolve(a) == LocalTurnControl.Resolve(b);

    private bool _strafeAllowed = true;

    private static VRHand? ResolveFlightHand() =>
        ComfortSettings.FlightHand.Value switch
        {
            TurnHandChoice.Left => VRHands.Left,
            TurnHandChoice.Right => VRHands.Right,
            _ => VRHands.Primary,
        };
}
