using System.Collections.Generic;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Index-finger ray for far interaction (FROZEN Phase-2 API). Implements
/// <see cref="IPickProvider"/> — Phase-3a consumes the pick for board targeting.
///
/// Ray pose (P1, hardware test #4): the OpenXR AIM ("pointer") pose when the device
/// delivers it (<see cref="VRHand.HasPointerPose"/>) — the runtime's authored
/// "where this controller points", unaffected by grip-pose tilt or the visual hand
/// offset. Fallback (simulated hands / no aim pose): origin at the index knuckle,
/// direction = hand forward (+Z, along the fingers). Visual: a subtle LineRenderer
/// laser plus a reticle dot, shown while the effective state (<see cref="Active"/>)
/// is on — for the DOMINANT hand that is EVERY mode while the hand has a pose, holds
/// nothing and its GRIP IS OPEN (test #19; the grip term is the 2026-08-24 physical-press
/// posture, see <see cref="GripSuppressed"/>). Test #14: the visible beam is a
/// STRAIGHT segment of the aim ray — hits clamp its length, the reticle sits at
/// ray ∩ surface on that line, and nothing may re-aim it (see UpdateVisuals).
///
/// Physics only — uGUI far pointing goes through the virtual mouse bridge instead
/// (GloomhavenVR.WorldUI.VirtualMouse), matching UI-ARCH §4.4 strategy 1.
/// </summary>
internal sealed class RayInteractor : IPickProvider
{
    /// <summary>Max ray length in meters (scale 1) — spans the whole diorama when scaled.</summary>
    private const float MaxDistanceMeters = 20f;

    private readonly VRHand _hand;

    private LineRenderer? _laser;
    private Transform? _reticle;
    private bool _enabled = true;
    private PickPose _current;

    /// <summary>The collider the LASER HIT PARTICLE probe last classified, so the probe runs on a
    /// change of hit and not per frame (<see cref="WorldUI.WindowMaterialise.ProbePointerHit"/>).</summary>
    private Collider? _lastProbedCollider;

    /// <summary>Layers the pick ray tests. Phase-3a sets the game's selection mask here.</summary>
    public LayerMask Mask = Physics.DefaultRaycastLayers;

    // Test #14 item 2: the former ReticleOverride (Board hex-snap moved the visible
    // dot to the hex center) is GONE — any override that moves the end point off the
    // aim line visibly re-aims the beam ('zaps' onto elements). The snapped hex is
    // communicated by the game's own hex hover highlight (HoverRegisterer/star
    // display via the projected cursor: BoardPick.ResolveCursorWorld feeds
    // TryGetCursorScreenPoint), never by bending the beam or dot.
    //
    // DO NOT RESURRECT — INVARIANTS §7 makes "a reticle override is reintroduced in any
    // form" a break condition. This comment names a symbol that no longer exists ON
    // PURPOSE; a "clean up references to non-existent symbols" pass must leave it alone.

    /// <summary>
    /// World point where the ray hits a code-intersected UI surface (the WorldUI flat
    /// screen has no physics collider — FlatScreen sets this every tick with its
    /// plane-intersection point, latched while a press is frozen). While fresh, the
    /// visible beam's LENGTH is clamped to this point's distance along the aim ray
    /// and the reticle shows at that ray point — the beam never passes THROUGH a
    /// menu (hardware test #7) and never changes direction (test #14: only the
    /// projection onto the aim line is used, so a latched press point slightly off
    /// the current aim cannot bend the beam). One-frame latch; pick data unaffected.
    /// </summary>
    public Vector3? UiHitOverride
    {
        get => _uiHitOverride;
        set
        {
            _uiHitOverride = value;
            _uiHitOverrideFrame = Time.frameCount;
            _uiHitFromPanel = false;
            _uiHitLabel = UnlabelledUiHitSource;
        }
    }

    private Vector3? _uiHitOverride;
    private int _uiHitOverrideFrame = -1;

    // ---- WHO raised the beam's UI hit (ModBuild 359, user 2026-09-03) -------------------
    //
    // User, verbatim: "Im Test während das Menu offen war konnte ich mit der rechten Hand kaum
    // mehr Figuren greifen, mit der linken ging es und das Problem verschwand als ich das Menu
    // geschlossen habe - das Menu soll keinerlei Einfluss nehmen darauf ob und wie ich die
    // Figuren nehmen kann!"
    //
    // HasFreshUiHit is a shared bus with two very different kinds of producer, and until now it
    // could not tell them apart:
    //
    //   (a) A UI PANEL the beam merely HOVERS — RayUguiDriver's canvas-plane hit and
    //       RayGrabDriver's window drag bar. No press is required, and the surface is the host
    //       CANVAS RECT, which is contractually never narrower than the visible window: his own
    //       ModBuild 356 log measured the open options window's hit rect at 1164x2700 px against
    //       a 1164x1080 canvas ("GROWN — this window draws past its own rect"), and 36 of his
    //       right-hand trigger pulls landed on that invisible apron with "NO uGUI hit under the
    //       beam (raycast miss: the press dies on the canvas plane, no widget, no handler)".
    //       Each of those did NOTHING as a click and still vetoed the near-hand figure grab.
    //
    //   (b) A WORLD OBJECT that is genuinely claiming THIS trigger for itself — the fan/board
    //       card laser, FigureGrabDriver's far-pluck clamp, FigureStretch, the flat screen, the
    //       pile browser, the combat-log cap, the map rail. Those are real rivals for the pull.
    //
    // Deferring to (b) is right and is untouched. Deferring to (a) is the defect: a beam's
    // opinion about a panel metres away must not outrank a hand that is physically inside a
    // figure. So the raise now carries its PROVENANCE. HasFreshUiHit itself is unchanged for
    // every consumer (BoardClickDriver's far click, PickingPatches, FigureGrabDriver's foreignUi
    // test all read exactly what they read before); only ProximityGrabber's near-field
    // arbitration consults the narrower signal below.
    //
    // The plain property setter is the DEFAULT and is deliberately pessimistic: a producer that
    // does not label itself is treated as (b), so no unlabelled call site can be weakened by
    // accident. Only the two drivers in this folder label themselves as panel hovers.
    private const string UnlabelledUiHitSource =
        "an unlabelled beam claim (a world laser: the card fan/board laser, FigureGrabDriver's "
        + "far-pluck clamp, FigureStretch, the flat screen, the pile browser, the combat-log cap "
        + "or the map rail)";

    private bool _uiHitFromPanel;
    private string _uiHitLabel = UnlabelledUiHitSource;

    /// <summary>
    /// Raise <see cref="UiHitOverride"/> and record that it was a UI PANEL HOVER that raised it
    /// (see the provenance block above). <paramref name="label"/> is what the refusal
    /// instrumentation prints, so it must name the surface a human can point at.
    /// </summary>
    internal void SetPanelUiHit(Vector3 point, string label)
    {
        _uiHitOverride = point;
        _uiHitOverrideFrame = Time.frameCount;
        _uiHitFromPanel = true;
        _uiHitLabel = label;
    }

    /// <summary>
    /// True while the FRESH ui hit is nothing but a UI panel hover — no world object is claiming
    /// this trigger. This is the term a near-field proximity grab is allowed to outrank; a false
    /// reading means a real rival owns the pull and the grab must still defer.
    ///
    /// <para>The <see cref="SuppressFarClick"/> half is excluded explicitly: it is the pure
    /// "I own this trigger" claim with no beam clamp, and every caller of it is a world laser.</para>
    /// </summary>
    internal bool FreshUiHitIsPanelHoverOnly =>
        _uiHitFromPanel
        && _uiHitOverride.HasValue && Time.frameCount - _uiHitOverrideFrame <= 1
        && Time.frameCount - _farClickFrame > 1;

    /// <summary>
    /// In words, what raised the fresh UI hit — for the refusal instrumentation. Reports the LIVE
    /// fields, so a line built from it says what actually happened rather than what the caller
    /// expected (see <c>VRHand.TickGripLaserFalsifier</c> for the same discipline).
    /// </summary>
    internal string FreshUiHitSource
    {
        get
        {
            bool clamp = _uiHitOverride.HasValue && Time.frameCount - _uiHitOverrideFrame <= 1;
            bool farClick = Time.frameCount - _farClickFrame <= 1;
            if (!clamp && !farClick)
                return "nothing (the beam is not claiming this frame)";
            if (!clamp)
                return "SuppressFarClick() — a world laser claimed the trigger without clamping the beam";
            return farClick ? _uiHitLabel + ", plus a SuppressFarClick() claim the same frame" : _uiHitLabel;
        }
    }

    /// <summary>
    /// Claim the trigger for this frame WITHOUT moving the beam — the far-click half of
    /// <see cref="UiHitOverride"/> on its own.
    ///
    /// The two duties used to be welded together: a driver that wanted only "don't let this
    /// trigger also fire a board click" had to publish a world point, and the beam then
    /// clamped to it. Where that point was not an actual beam hit the result was a phantom
    /// surface — the beam ends at the point's PROJECTION onto the aim ray, so publishing an
    /// object's CENTRE makes the reticle stick to the plane through that centre perpendicular
    /// to the beam, at every aim direction. That is precisely the "invisible wall drawn
    /// orthogonally through the middle of the card" the tester kept hitting instead of the
    /// card (see CardsDriver's lift-priority branch). Callers that own the trigger but have no
    /// real hit point call this instead and leave the beam to the physics pick.
    /// </summary>
    public void SuppressFarClick() => _farClickFrame = Time.frameCount;

    private int _farClickFrame = -1;

    // ---- card-contact stand-down (user report 2026-08-08) -------------------------------

    /// <summary>
    /// How long (s, unscaled) a card-contact stand-down survives its last assertion. The
    /// producer (<c>CardsDriver.UpdateLaserContactStandDown</c>) re-asserts it every frame the
    /// hand is in a card, so this is a RELEASE grace, not a timeout: it is what keeps the beam
    /// from blinking back on for the one frame a contact election drops out — most importantly
    /// the TRIGGER-PULL frame, where curling the index finger moves the fingertip off the card
    /// it was measured against. Deliberately the same window as
    /// <see cref="FanOccluderGraceSeconds"/> (and <c>CardsDriver.FanHoverGraceSeconds</c>):
    /// all three bridge the identical pull-jerk, and a laser that came back exactly ON the
    /// press frame is the accident this whole feature exists to prevent.
    /// </summary>
    private const float CardContactGraceSeconds = FanOccluderGraceSeconds;

    private float _cardContactUntil;
    private string _cardContactZone = "";
    private string _cardContactCard = "";
    private CardContact _cardContactGeometry = new("no probe", 0f, 0f, 0f);
    private bool _loggedCardContact;

    /// <summary>
    /// The MEASURED geometry of the contact that stood the beam down — everything the next
    /// hardware log needs to check the threshold itself instead of taking "the hand was in a
    /// card" on trust.
    ///
    /// <para>WHY IT IS CARRIED HERE AT ALL (user report 2026-08-08, round 2: "Sei strenger mit
    /// dem Deaktivieren des Lasers — das will ich wirklich nur, wenn die Hand die Karte physisch
    /// berührt; aktuell ist es immer wenn auch eine Karte nur gehighlighted ist, das führt dazu
    /// dass der Laser auch nicht da ist obwohl die Hand weiter über der Karte ist"). The first
    /// round stood the beam down on the mere HOVER ELECTION, which is a proximity/reach verdict
    /// ("which card would this hand take", true from several centimetres out), so the log line
    /// naming the card could not distinguish "touching it" from "hovering a hand's breadth above
    /// it". The producer now runs a real geometric test and hands its two numbers over: with them
    /// in the line, one grep says whether a stand-down the player felt was early was a millimetre
    /// away or a centimetre, and whether the tolerance is the thing to move.</para>
    ///
    /// All distances are REAL millimetres (world distance ÷ the hand's world scale), so they read
    /// the same at any rig zoom or board scale. A plain struct, copied by value — nothing here
    /// allocates.
    /// </summary>
    public readonly struct CardContact
    {
        /// <summary>Which hand point made contact — a literal ("index tip" / "palm").</summary>
        public readonly string Probe;

        /// <summary>Signed distance from that point to the card's PLANE, real mm (0 = dead on the
        /// face; positive is behind the card, negative in front of it).</summary>
        public readonly float DepthMm;

        /// <summary>How far INSIDE the card's face the point projected, real mm — the smaller of the
        /// two edge clearances. Never negative for an accepted contact.</summary>
        public readonly float MarginMm;

        /// <summary>The depth tolerance that accepted it, real mm — printed alongside so the line
        /// carries its own yardstick.</summary>
        public readonly float LimitMm;

        public CardContact(string probe, float depthMm, float marginMm, float limitMm)
        {
            Probe = probe;
            DepthMm = depthMm;
            MarginMm = marginMm;
            LimitMm = limitMm;
        }
    }

    /// <summary>
    /// TRUE while this hand is physically inside a grabbable card of a fan/pile and its laser
    /// therefore stands down completely (see <see cref="StandDownForCardContact"/>). Read as
    /// ONE of the level inputs of <see cref="Active"/>, never latched: it is a DEADLINE in
    /// unscaled time that only an actively re-asserted contact can extend.
    /// </summary>
    public bool CardContactStandDown => Time.unscaledTime <= _cardContactUntil;

    /// <summary>
    /// USER REPORT 2026-08-08 ("Während die Hand physisch in einer Karte von einem Pile steckt,
    /// deaktiviere den Laser — aktuell greife ich versehentlich mit dem Laser dahinter
    /// irgendwas"): while the hand is IN a card, its beam necessarily points straight THROUGH
    /// that card at whatever stands behind it (a board button, a hex, a figure, a menu), and the
    /// trigger that means "take this card" was landing there instead.
    ///
    /// This is the one seam that turns the whole beam off for that hand: <see cref="Active"/>
    /// goes false, so the physics pick stops (BoardPick/BoardClickDriver, FigureGrabDriver),
    /// <see cref="RayUguiDriver"/> and <see cref="RayGrabDriver"/> cancel their hover/press,
    /// the Cards laser paths clear their own hovers at their guards, and the visuals hide —
    /// the laser being visibly gone is the honest affordance for "this hand is grabbing, not
    /// pointing". The hand's PROXIMITY grab is untouched (that is how the card is taken), and
    /// the OTHER hand's ray never sees this call.
    ///
    /// NO DEADLOCK BY CONSTRUCTION — this is why it is a re-asserted deadline and not a flag:
    /// every way the contact can end (card destroyed or re-parked, fan/pile closed, mode or
    /// phase change, the driver itself dying, the hand teleporting or losing tracking) ends
    /// with NOBODY CALLING THIS, and the beam is back <see cref="CardContactGraceSeconds"/>
    /// later without anyone having to remember to clear anything. There is no "off" call to
    /// miss.
    /// PHYSICAL TOUCH ONLY (user report 2026-08-08, round 2 — see <see cref="CardContact"/>): the
    /// caller must have proved actual OVERLAP with the card's face, not merely that the card is the
    /// one this hand has elected/highlighted. The election is a reach verdict and answers "which
    /// card would this hand take" from several centimetres away; standing the beam down on it took
    /// the laser away while the hand was still hovering well ABOVE the card.
    ///
    /// <paramref name="zone"/> must be a literal (it is logged, never per-frame formatted);
    /// <paramref name="card"/> is read for its name ONLY on the stand-down edge.
    /// <paramref name="contact"/> is the measurement that justified the call, kept for that same
    /// edge so the log line can print the threshold it cleared.
    /// </summary>
    public void StandDownForCardContact(string zone, Object? card, in CardContact contact)
    {
        _cardContactUntil = Time.unscaledTime + CardContactGraceSeconds;
        // Frozen together on the logging edge (see TickCardContactLog): the name, the zone and the
        // geometry must describe ONE frame — the frame the stand-down actually began — or the line
        // would report a card from one moment and a distance from another. Reading card.name
        // allocates, which is the other reason this is edge-gated.
        //
        // THE ZONE IS PART OF THAT FREEZE (2026-08-09). It used to be written on EVERY call while
        // the card and the geometry were written only on the edge, so a hand that swept from one
        // pool into another inside a single contact episode — item fan into the board's use recess,
        // browse arc into a slot — printed the LAST pool it touched next to the FIRST card it
        // touched, and the RESTORED line contradicted its own STAND-DOWN line. Now that the zone
        // vocabulary distinguishes the discard arc from the burnt arc from the item fan
        // (CardsDriver's Zone* literals), that mismatch would land exactly on the field the next
        // hardware log is meant to be grepped by.
        if (!_loggedCardContact)
        {
            _cardContactCard = card != null ? card.name : "a card";
            _cardContactZone = zone;
            _cardContactGeometry = contact;
        }
    }

    /// <summary>
    /// ONE Info line per hand when the beam stands down for a card contact and one when it
    /// comes back — change-gated on the state itself, so sweeping the hand from card to card
    /// inside the same contact episode adds nothing and a resting hand costs nothing.
    /// Grep: "laser STAND-DOWN" / "laser RESTORED".
    /// </summary>
    private void TickCardContactLog()
    {
        bool standDown = CardContactStandDown;
        if (standDown == _loggedCardContact)
            return;
        _loggedCardContact = standDown;
        if (standDown)
        {
            // The MEASUREMENT is the point of this line (user report 2026-08-08, round 2 — the
            // stand-down fired while the hand was still hovering above the card). Naming the card
            // proves nothing; depth-to-plane against the tolerance that accepted it, plus how far
            // inside the face the point landed, proves whether this was a touch or a hover.
            Core.VRLog.Info("Hands", $"{_hand.Side} laser STAND-DOWN — the hand is physically in " +
                                     $"'{_cardContactCard}' ({_cardContactZone}): {_cardContactGeometry.Probe} " +
                                     $"{_cardContactGeometry.DepthMm:F1} mm off the card plane " +
                                     $"(tolerance ±{_cardContactGeometry.LimitMm:F1} mm), " +
                                     $"{_cardContactGeometry.MarginMm:F1} mm inside its face — real mm at the " +
                                     "hand's own world scale. The beam points THROUGH that card, so it is " +
                                     "switched off for this hand: no hover, no press, no grab on anything " +
                                     "behind it. The proximity grab still takes the card.");
        }
        else
        {
            Core.VRLog.Info("Hands", $"{_hand.Side} laser RESTORED — no card contact for " +
                                     $"{CardContactGraceSeconds:F2}s (last '{_cardContactCard}', " +
                                     $"{_cardContactZone}, {_cardContactGeometry.Probe} " +
                                     $"{_cardContactGeometry.DepthMm:F1} mm off the plane); " +
                                     "hover/press/grab are live again.");
        }
    }

    /// <summary>
    /// True while <see cref="UiHitOverride"/> is fresh (set this frame or the last) —
    /// i.e. the beam is clamped to a code-intersected UI surface (world panel, fan
    /// card, flat screen) — or while a driver has claimed the trigger via
    /// <see cref="SuppressFarClick"/> without clamping the beam. Far-click consumers
    /// (BoardClickDriver) skip the trigger while this is set so a UI point-and-click
    /// never doubles as a board click.
    /// </summary>
    public bool HasFreshUiHit =>
        (_uiHitOverride.HasValue && Time.frameCount - _uiHitOverrideFrame <= 1)
        || Time.frameCount - _farClickFrame <= 1;

    /// <summary>
    /// Distance along the aim ray to the nearest card in the OPEN hand fan, or +inf when
    /// the fan is closed or the ray misses it. Recomputed each <see cref="Tick"/> from
    /// <see cref="Cards.CardFan.Current"/>'s geometric card rects (the same test the fan
    /// pluck uses). The board pick below, <see cref="RayUguiDriver"/> and
    /// <see cref="RayGrabDriver"/> treat it as an OCCLUDER: a board/hex/menu target farther
    /// than this sits BEHIND the hand of cards and is rejected (user issue: the laser
    /// selected things visible THROUGH the fan — hex tiles, a floated window's grab bar).
    /// The fan's OWN card interactions are unaffected — they route through CardsDriver's
    /// independent fan raycast, never this physics pick.
    ///
    /// STICKY (pull-jerk hold, see Tick): for <see cref="FanOccluderGraceSeconds"/> after the
    /// ray leaves the fan the value holds at its last live distance instead of snapping to
    /// +inf, so the trigger-pull jerk cannot open a one-frame window in which the panel
    /// behind the fan becomes hoverable/pressable. <see cref="FanOccluderHeld"/> tells the
    /// suppression logs apart from a live geometric hit.
    /// </summary>
    public float FanOccluderDistance { get; private set; } = float.PositiveInfinity;

    /// <summary>True while <see cref="FanOccluderDistance"/> is the post-fan pull-jerk HOLD
    /// (grace window) rather than a live fan-card intersection this frame.</summary>
    public bool FanOccluderHeld { get; private set; }

    /// <summary>How long (s, unscaled) the fan occluder holds after the beam leaves the fan —
    /// mirrors <c>CardsDriver.FanHoverGraceSeconds</c>: both bridge the same trigger-pull jerk.</summary>
    private const float FanOccluderGraceSeconds = 0.15f;

    private float _fanOccluderHeldDistance = float.PositiveInfinity;
    private float _fanOccluderHoldUntil;

    /// <summary>
    /// Distance along the aim ray to the nearest SOLID MOD-OWNED surface of ANY kind - the
    /// minimum of <see cref="FanOccluderDistance"/> (all three open fans) and the control
    /// board's own solid surfaces (board mesh, keycaps, pile stacks, rest discs, slotted
    /// cards - <c>PlayTray.RaycastSolidDistance</c>); +inf when the ray crosses none of them.
    ///
    /// ROOT CAUSE of the generalisation (user report 2026-08-04): with the control board
    /// pushed in front of the OPTIONS MENU, the beam visibly collided with the board and
    /// STILL selected the menu tabs behind it. The board, like the fan cards, is mod-layer
    /// trigger geometry the physics pick <see cref="Mask"/> never sees, and it is scanned by
    /// CardsDriver's board laser only AFTER RayUguiDriver has already delivered hover/press
    /// to the canvases (VRHand tick order) - so the "a nearer physics hit blocks the UI hit"
    /// rule had a blind spot exactly the size of the board. This value closes it at the one
    /// arbitration seam every far consumer already honours: RayUguiDriver and RayGrabDriver
    /// reject any UI/grab-bar target FARTHER than it (with the shared 0.005 m epsilon, so the
    /// board's own docked/converted surfaces - initiative track, control dock, decision rows,
    /// slot-card face canvases, all seated proud of or coplanar with the board colliders -
    /// are never self-occluded), and the physics pick below drops board/hex targets behind
    /// it. UI NEARER than the board (a floated window in front of it) is untouched: the
    /// rejection is strictly one-directional. Desktop/flat mode never runs this interactor.
    ///
    /// STICKY like the fan value: the board contribution holds its last live distance for
    /// <see cref="FanOccluderGraceSeconds"/> after the beam slips off the board, so the
    /// trigger-pull jerk cannot open a one-frame window in which a menu tab behind the board
    /// edge receives the press (the exact leak the fan hold was built for).
    /// </summary>
    public float SolidOccluderDistance { get; private set; } = float.PositiveInfinity;

    /// <summary>True while <see cref="SolidOccluderDistance"/> comes from the control board
    /// rather than an open fan - drives the occlusion log's culprit naming only.</summary>
    public bool SolidOccluderIsBoard { get; private set; }

    private float _boardOccluderHeldDistance = float.PositiveInfinity;
    private float _boardOccluderHoldUntil;

    // Constant ANGULAR size for the ray visuals (P6, hardware test #8): the reticle
    // used to scale with the rig's WorldScale — zooming the diorama out grew the dot
    // enormously (and doubly so: localScale under an already rig-scaled parent).
    // Angular sizing keeps it a fixed apparent size from the HMD regardless of rig
    // scale or distance. tan(0.45°) ≈ 0.00785, tan(0.06°) ≈ 0.00105.
    //
    // THE CLAMPS ARE REAL METRES AND MUST BE MULTIPLIED BY THE RIG SCALE (ModBuild 178 —
    // "der Laser wurde nicht angezeigt" in the 3D map room). The angular product
    // headDist × factor is in WORLD units, because headDist is; these four bounds are named
    // …Meters and are real-metre intentions. While the rig scale is ~1 — every scenario
    // diorama, which is the only place this had ever run — the two coincide and the bug is
    // invisible. The map room seats the player at a rig scale of ~198 game units per metre, so
    // the head sits ~200 world units from what it points at, the angular width comes out at
    // 0.21 world units, and BeamWidthMaxMeters CLAMPED IT TO 0.03 — a beam 0.15 mm wide in
    // perceived size, i.e. a laser that is genuinely being drawn and genuinely cannot be seen.
    // The reticle clamp did the same to the hit dot. Scale the bounds and the whole thing is
    // exactly what its own comment always promised: a fixed apparent size at any rig scale.
    private const float ReticleAngularFactor = 0.00785f;
    private const float BeamWidthAngularFactor = 0.00105f;
    private const float ReticleMinMeters = 0.003f;
    private const float ReticleMaxMeters = 0.25f;
    private const float BeamWidthMinMeters = 0.0008f;
    private const float BeamWidthMaxMeters = 0.03f;

    // Test (user #1): the hit dot vanished ON the floated dialog/modal window. The
    // renderQueue-4600 material (see CreateBeamMaterial) only beats canvases at the
    // SAME sorting order — but a floated modal host is raised to sortingOrder=1000
    // (WorldUI.ModalFallback.ModalHostSortingOrder), and Unity sorts every renderer
    // by sortingLayer → SORTINGORDER first and only then by renderQueue. At the
    // reticle's default order 0 the modal painted straight over the dot. The laser
    // LineRenderer and reticle MeshRenderer get an order comfortably above the modal
    // so they draw last; the shader still ZTest-LEquals against the opaque depth
    // buffer, so solid furniture/board geometry keeps occluding them correctly (UI
    // shaders write no depth, so the depthless modal never does).
    private const int RayVisualSortingOrder = 5000;

    // ---- P5 (MISSION A.5) — RETIRED: ModalUI visual constraint ----------------------------
    // The ModalUI cone gate (visuals only near a known UI surface, so the laser would not
    // sweep the room during a dialog) is retired by the 2026-08 user ruling: the beam
    // renders unconditionally in every mode (see VisualsAllowed). The registry below stays
    // populated (FlatScreen registers its quad) purely for reversibility of that policy.

    private static readonly List<Transform> UiTargets = new(4);

    /// <summary>Register a world transform the ModalUI-constrained ray may point at (e.g. the flat screen quad).</summary>
    public static void RegisterUiTarget(Transform target)
    {
        if (target != null && !UiTargets.Contains(target))
            UiTargets.Add(target);
    }

    public static void UnregisterUiTarget(Transform target) => UiTargets.Remove(target);

    /// <summary>Hot-reload hygiene (HandsModule.Shutdown).</summary>
    internal static void ClearUiTargets() => UiTargets.Clear();

    internal RayInteractor(VRHand hand) => _hand = hand;

    /// <summary>Latest pick (updated once per frame while enabled).</summary>
    public PickPose Current => _current;

    /// <summary>
    /// Mode-policy input (<see cref="VRHand.SetInteractorMask"/>). ONE input into the
    /// effective state — <see cref="Active"/> re-derives on/off from live facts every
    /// frame and <see cref="Tick"/> syncs the visuals (test #19: never edge-latched).
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// LASER PERSISTENCE TRUTH TABLE (hardware test #19: the dominant laser silently
    /// vanished mid-scenario and never returned). Every path that can turn this ray
    /// or its visuals off — each must be a LEVEL (re-read from live state every
    /// frame), never an edge-latched flag, so a missed release/mode event can never
    /// strand the laser off:
    ///
    ///   input (re-read per Tick)      | turns off      | can it latch?
    ///   ------------------------------+----------------+----------------------------------
    ///   mode mask (Enabled=false)     | pick + visuals | WAS THE #19 LATCH: TableIdle and
    ///                                 |                | HalfSelection carried no Ray, and
    ///                                 |                | a single-target attack waits in
    ///                                 |                | Choreographer state
    ///                                 |                | WaitingForCardSelection — NOT a
    ///                                 |                | TargetingStates member — so the
    ///                                 |                | mode stayed HalfSelection and the
    ///                                 |                | laser was policy-off for the whole
    ///                                 |                | attack. Fixed: InteractorsFor ORs
    ///                                 |                | Ray into the DOMINANT role in
    ///                                 |                | every mode.
    ///   !VRHand.HasPose               | pick + visuals | no — device tracking level; the
    ///                                 |                | visuals return the frame the pose
    ///                                 |                | returns.
    ///   Grabber.Held != null          | pick + visuals | no — Held is itself level-derived
    ///                                 |                | (release re-checks the live button
    ///                                 |                | state every Tick; CancelAll on
    ///                                 |                | mode disable and tracking loss).
    ///   ModalUI cone (VisualsAllowed) | NOTHING any    | no — the cone gate is retired
    ///                                 | more (retired) | (user ruling 2026-08: beam always
    ///                                 |                | renders; VisualsAllowed ≡ true).
    ///   UiHitOverride                 | nothing        | no — clamps beam LENGTH only,
    ///                                 |                | one-frame freshness window.
    ///   dominance switch              | via mode mask  | no — HandsDriver reapplies masks
    ///                                 |                | on PrimaryHand.SettingChanged and
    ///                                 |                | on every hands rebuild.
    ///   rig/hands rebuild             | visuals die    | no — Build → ApplyMode recreates
    ///                                 |                | hand, ray and visuals together.
    ///   CardContactStandDown          | pick + visuals | no — a DEADLINE in unscaled time that
    ///                                 |                | only a live contact election can
    ///                                 |                | re-assert (StandDownForCardContact);
    ///                                 |                | nothing has to switch it OFF, so
    ///                                 |                | nothing can forget to.
    ///   VRHand.GripPressed (THIS hand)| pick + visuals | no — it IS the live analog button
    ///                                 |                | level, re-read from the device every
    ///                                 |                | frame in ApplyAnalog, and zeroed by
    ///                                 |                | ClearInput on tracking loss. There is
    ///                                 |                | no latch, no deadline and no edge to
    ///                                 |                | miss: the beam returns on the frame
    ///                                 |                | the grip crosses ReleaseThreshold.
    ///
    /// <para>The stand-down row is the ONE exception to the 2026-08 "der Laser ist ausnahmslos
    /// da" ruling, and it is not a phase/mode policy: it lasts exactly as long as the player's
    /// hand is inside a card they are reaching for (fractions of a second), and it exists
    /// because the beam THROUGH that card was grabbing and pressing whatever stood behind
    /// it (user report 2026-08-08).</para>
    /// </summary>
    public bool Active => _enabled && _hand.HasPose && !IsHolding && !GripSuppressed && !CardContactStandDown;

    /// <summary>Transient suppression: the hand is actually holding a grabbable RIGHT NOW.</summary>
    private bool IsHolding => _hand.Grabber != null && _hand.Grabber.Held != null;

    /// <summary>
    /// Transient suppression: the player is HOLDING THE GRIP on this hand, which is what arms
    /// the physical fingertip press (<c>PokeInteractor.PressAllowed</c>). User request
    /// 2026-08-24: "Wenn ich die Greiftaste gedrückt halte und somit im 'Physischen drücken
    /// Modus' bin, will ich das der Laser deaktiviert ist. Aber wirklich NUR während die
    /// greiftaste gedrückt gehalten wird."
    ///
    /// <para>PER HAND, and that is the whole point: this reads THIS hand's button, so poking
    /// with the left hand leaves the right hand's beam exactly where it was. Nothing here is
    /// a mode, a setting or a latch — it is the analog grip level itself, so there is no state
    /// to get stuck in and no config dial to add (the user asked for a behaviour, not an
    /// option, and a dial would only create a second way for the beam to disappear silently —
    /// the exact failure the truth table above exists to prevent).</para>
    ///
    /// <para>NOT GATED ON <see cref="IsHolding"/>, deliberately. The obvious-looking mirror of
    /// <c>PressAllowed</c> (<c>GripPressed &amp;&amp; Grabber.Held == null</c>) would be wrong here:
    /// while the hand carries something the ray is ALREADY off through <see cref="IsHolding"/>,
    /// so the extra term would change nothing except make the beam come back for the frames
    /// where the grip is held and the carry has just ended. The asymmetry with
    /// <c>PressAllowed</c> is real and intended — that gate answers "may a fingertip commit?",
    /// this one answers "is the player in the poke posture?", and holding something does not
    /// leave that posture.</para>
    ///
    /// <para>WHAT THIS DOES NOT TOUCH: a window the laser is CARRYING. A laser carry runs
    /// through <c>ProximityGrabber.ForceGrab(…, releaseOnTriggerUp: true)</c>, i.e. it is owned
    /// by the Grabber and released by the TRIGGER; the ray is a bystander (already inactive via
    /// <see cref="IsHolding"/>) and <c>PanelGrabHandle.Update</c> carries the window. Squeezing
    /// the grip mid-carry therefore cannot drop it — see ProximityGrabber's hold-button doc.</para>
    /// </summary>
    internal bool GripSuppressed => _hand.GripPressed;

    /// <summary>Falsifier readback: is the beam GameObject actually enabled right now?</summary>
    internal bool BeamDrawn => _laser != null && _laser.gameObject.activeSelf;

    /// <summary>Falsifier readback: the reason <see cref="SyncActiveState"/> last logged.</summary>
    internal string StateReason => _lastReason;

    public bool TryGetPick(out PickPose pick)
    {
        pick = _current;
        return Active;
    }

    internal void Tick()
    {
        TickCardContactLog();
        bool active = Active;
        SyncActiveState(active);
        if (!active)
        {
            // CARD-CONTACT STAND-DOWN, clean release (requirement 3: nothing stale left
            // behind): the beam can go down mid-hover, and the LAST thing it did may have been
            // to clamp itself onto a surface (UiHitOverride) or claim the trigger without one
            // (SuppressFarClick). Both live in a two-frame freshness window, and both are read
            // as "the laser owns this pull" — HasFreshUiHit makes ProximityGrabber DEFER and
            // BoardClickDriver skip. Left standing, that residue would swallow exactly the
            // grab this stand-down exists to deliver, for the frame after it engages. Drop it
            // at the moment the beam goes down; the hover objects themselves are released by
            // their own owners (RayUguiDriver.Cancel / RayGrabDriver.ClearHover / the Cards
            // laser paths' Clear*Hover), all of which already run off this same !Active edge.
            //
            // THE GRIP SUPPRESSION TAKES THE SAME CLEAR, for the same reason and one stronger
            // one. The residue is read as "the laser owns this pull" (HasFreshUiHit), and the
            // grip is pressed precisely to hand the pull to the FINGERTIP instead — leaving a
            // hover clamp from the frame before standing would make ProximityGrabber defer and
            // BoardClickDriver skip a trigger for the two frames after the beam went down, on
            // behalf of a beam that no longer exists. Frame order makes the clear land in time:
            // Ray ticks before RayUgui/RayGrab/Grabber (VRHand.UpdateBody.interactors), so
            // everything downstream sees the residue gone on the very GripDown frame.
            if (CardContactStandDown || GripSuppressed)
            {
                _uiHitOverride = null;
                _uiHitOverrideFrame = -1;
                _farClickFrame = -1;
                // The provenance goes with the residue it describes — a stale "it was only a
                // panel hover" left standing would answer a question about a beam that is down.
                _uiHitFromPanel = false;
                _uiHitLabel = UnlabelledUiHitSource;
            }
            _current.HasHit = false;
            FanOccluderDistance = float.PositiveInfinity;
            // State hygiene: the hold is meaningless across an inactive gap (grabbing the
            // plucked card is exactly what turns the ray off) — never let a stale hold from
            // before a grab suppress UI after the release.
            _fanOccluderHoldUntil = 0f;
            FanOccluderHeld = false;
            SolidOccluderDistance = float.PositiveInfinity;
            SolidOccluderIsBoard = false;
            _boardOccluderHoldUntil = 0f;
            return;
        }

        float scale = _hand.WorldScale;
        Vector3 origin;
        Vector3 direction;
        // Direction ALWAYS comes from the controller's OpenXR aim pose, never from the visual
        // hand. Deriving it from the hand was tried (so the beam would be collinear with the
        // finger at any seat) and it aimed worse: the aim pose is what the runtime tuned for
        // pointing, and coupling the ray to a cosmetic setting made aiming move whenever the
        // hand was tuned. The visible beam still STARTS at the knuckle (LaserFingerOrigin) —
        // that part reads correctly and costs only a small angular difference near the hand.
        if (_hand.HasPointerPose)
        {
            // OpenXR aim pose — see class doc.
            origin = _hand.PointerOrigin;
            direction = _hand.PointerDirection;
        }
        else
        {
            origin = _hand.Rig.GetFinger(Finger.Index).Root.position;
            direction = _hand.Rig.Root.forward;
        }
        float maxDistance = MaxDistanceMeters * scale;

        _current.Origin = origin;
        _current.Direction = direction;

        // Fan occlusion (user issue): the off-hand's raised card fan sits between the
        // pointing hand and the board/menu. Its cards live on the mod render layer — which
        // the physics pick Mask ignores — and the geometric fan pluck is CardsDriver's job,
        // so the board/hex/menu pick would sail straight THROUGH the hand of cards and
        // select whatever is visible behind it (a hex tile, a floated window's grab bar).
        // Measure the nearest fan-card hit along THIS ray up front; the board pick below and
        // the two far drivers (RayUgui, RayGrab) reject any target that sits BEHIND it. +inf
        // when the fan is closed or the ray misses it, so pointing over/around the fan (or
        // with no fan raised) never blocks.
        //
        // PULL-JERK HOLD (user round 2: the item-fan TRIGGER still pressed the initiative
        // portraits behind the fan). The per-frame geometric test above is exact but has zero
        // memory, and the trigger PULL itself jerks the aim ray (the documented fan-grab T2
        // mechanism, CardsDriver.FanHoverGraceSeconds): on the very TriggerDown frame the beam
        // often slips off the narrow card strip — through a chip gap or past the fan edge —
        // so the occluder read +inf for exactly that frame and RayUguiDriver (which ticks
        // BEFORE the Cards fan paths, see VRHand.UpdateBody.interactors) delivered a fresh
        // hover + pointer-down to the converted panel behind the fan in the same frame. The
        // fix is at this single arbitration seam every consumer already honours: after the ray
        // leaves the fan, the occluder HOLDS its last live distance for a short grace window,
        // so a press born of the pull jerk is consumed by the fan side, never by uGUI/board
        // behind it. Deliberately mirrors FanHoverGraceSeconds (long enough to bridge the
        // jerk, short enough that deliberately pointing away frees the UI near-instantly);
        // nearer panels still win — the hold is a distance, not a blanket veto.
        float liveFan = ComputeFanOccluder(origin, direction, maxDistance);
        if (!float.IsPositiveInfinity(liveFan))
        {
            _fanOccluderHeldDistance = liveFan;
            _fanOccluderHoldUntil = Time.unscaledTime + FanOccluderGraceSeconds;
            FanOccluderHeld = false;
            FanOccluderDistance = liveFan;
        }
        else if (Time.unscaledTime <= _fanOccluderHoldUntil)
        {
            FanOccluderHeld = true;
            FanOccluderDistance = _fanOccluderHeldDistance;
        }
        else
        {
            FanOccluderHeld = false;
            FanOccluderDistance = float.PositiveInfinity;
        }

        // SOLID occluder = fans MIN the control board (see the SolidOccluderDistance doc).
        // The board contribution gets the same pull-jerk hold as the fan value: the geometric
        // scan is exact but memoryless, and the trigger pull jerks the aim - a press born on
        // the frame the beam slips off the board edge must still belong to the board side,
        // never to a menu tab behind it.
        float liveBoard = ComputeBoardOccluder(origin, direction, maxDistance);
        if (!float.IsPositiveInfinity(liveBoard))
        {
            _boardOccluderHeldDistance = liveBoard;
            _boardOccluderHoldUntil = Time.unscaledTime + FanOccluderGraceSeconds;
        }
        else if (Time.unscaledTime <= _boardOccluderHoldUntil)
        {
            liveBoard = _boardOccluderHeldDistance;
        }
        SolidOccluderDistance = Mathf.Min(FanOccluderDistance, liveBoard);
        SolidOccluderIsBoard = liveBoard < FanOccluderDistance;

        // The physics pick ALWAYS runs — in every mode, under every modal (user ruling
        // 2026-08: the laser must exist and collide without exception). The former modal
        // input-block suppressed this raycast so nothing behind a floating menu was
        // pickable; that duty is now carried entirely by the COMMIT layer (see
        // UpdateCommitSuppression + ModalFallback.HardCommitLockActive for the decision
        // table): the modal's own uGUI still wins the trigger wherever the beam is on it
        // (nearest-hit arbitration; a physics hit NEARER than the panel occludes it
        // honestly, exactly as it visually occludes the panel via depth test), and
        // board/card/tray commit seams each apply their own per-target policy.
        UpdateCommitSuppression();
        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, Mask))
        {
            if (hit.distance > SolidOccluderDistance + FanOcclusionEpsilonMeters * scale)
            {
                // The raised card fan / control board is clearly nearer along the ray — this
                // game hex/figure/furniture target is behind it; drop it (no pick-through).
                _current.HasHit = false;
                _current.HitCollider = null;
                // [Optimize] LeanLogStrings: only pay for the interpolation + the allocating
                // .name read when the throttled note would actually be emitted.
                if (WantFanOcclusionNote)
                    NoteFanOcclusion($"board target '{hit.collider.name}'", hit.distance);
            }
            else
            {
                _current.HasHit = true;
                _current.HitPoint = hit.point;
                _current.HitDistance = hit.distance;
                _current.HitCollider = hit.collider;
                // EDGE-GATED PROBE (2026-09-03, "the dust collides with the laser"): on every
                // CHANGE of hit collider, ask whether the physics pick has resolved onto a
                // materialise effect's object or a ParticleSystemRenderer. The dust carries no
                // collider, so this can only ever print if a path this fix did not see exists;
                // the line it prints (LASER HIT PARTICLE) must be absent from the next log.
                if (!ReferenceEquals(hit.collider, _lastProbedCollider))
                {
                    _lastProbedCollider = hit.collider;
                    WorldUI.WindowMaterialise.ProbePointerHit(_hand.Side.ToString(),
                                                              "physics pick (RayInteractor)",
                                                              hit.collider.transform);
                }
            }
        }
        else
        {
            _current.HasHit = false;
            _current.HitCollider = null;
            _lastProbedCollider = null;
        }

        UpdateVisuals(origin, direction, maxDistance, scale);
    }

    /// <summary>
    /// Ray visuals policy — ALWAYS true, in every mode (user ruling 2026-08: "Ich möchte,
    /// dass der Laser ausnahmslos da ist und collidet, egal in welcher Phase sich das Spiel
    /// aktuell befindet."). This method used to be the MISSION A.5 ModalUI cone gate (beam
    /// hidden unless pointing within [Hands] ModalRayConeDegrees of a registered UI surface,
    /// so the laser would not sweep the room while a dialog was up) — and exactly that gate
    /// was the tutorial's "no laser at all on the playfield while the instruction box is
    /// open". The ruling overrules the sweep-the-room concern outright: the beam renders
    /// wherever the hand points, in every phase. The [Hands] RayAlwaysOn /
    /// ModalRayConeDegrees entries that once configured the gate are deleted (2026-08
    /// dead-settings sweep); UiTargets/RegisterUiTarget stay registered by FlatScreen for
    /// reversibility, they are just no longer consulted.
    /// The signature is kept so a future policy change slots back in at this one seam.
    /// </summary>
    private bool VisualsAllowed(Vector3 origin, Vector3 direction) => true;

    // ---- commit suppression: the beam always picks; only COMMITS are modal-gated -------

    /// <summary>
    /// Commit-suppression state (user ruling 2026-08, see
    /// <see cref="WorldUI.ModalFallback.HardCommitLockActive"/> for the full per-target
    /// decision table). The former "modal input-block" here SUPPRESSED THE PHYSICS PICK
    /// while a blocking modal floated / ModalUI was asserted, so nothing behind the menu
    /// (board hexes / cards / tray buttons) was pickable — which also killed the beam's
    /// collision and every hover behind the menu (the reported "laser appears but collides
    /// only with the grab bar"). The pick now ALWAYS runs; the anti-click-through duty
    /// moved entirely to the COMMIT layer: the modal's own uGUI wins the trigger wherever
    /// the beam is on it (nearest-hit arbitration + HasFreshUiHit already do this
    /// structurally), board clicks self-gate through the game's own CommonLoop/LateUpdate
    /// seams except under the hard lock (results/error families —
    /// BoardClickDriver.RequestClick), and card/tray commits stay gated on the Cards side
    /// (CardsDriver TickInteractionsAndStatus). This method only TRACKS the two commit
    /// flags once per frame (shared by both hands) and logs the policy on state change so
    /// hardware logs always show why a trigger did or did not commit.
    /// </summary>
    private static bool _cardTrayCommitsSuppressed;
    private static bool _boardClickCommitsSuppressed;
    private static int _commitPolicyFrame = -1;

    /// <summary>Recompute the commit-suppression flags once per frame and log each transition.</summary>
    private static void UpdateCommitSuppression()
    {
        if (Time.frameCount == _commitPolicyFrame)
            return;
        _commitPolicyFrame = Time.frameCount;
        // Item 4 (user): a NON-blocking reachable menu (pause/ESC, Options, Multiplayer,
        // Compendium) and action-dismissed level messages impose ZERO restrictions. Only
        // BLOCKING floated windows gate the card/tray commit layer, and only the hard
        // families (results screens / error box) gate board clicks.
        bool cardTray = WorldUI.ModalFallback.BlockingWindowModalActive;
        bool board = WorldUI.ModalFallback.HardCommitLockActive;
        if (cardTray == _cardTrayCommitsSuppressed && board == _boardClickCommitsSuppressed)
            return;
        _cardTrayCommitsSuppressed = cardTray;
        _boardClickCommitsSuppressed = board;
        string families = !cardTray && !board
            ? "none — all commit targets live"
            : (cardTray ? "cards+tray (blocking modal)" : "")
              + (cardTray && board ? ", " : "")
              + (board ? "board clicks (results/error family)" : "");
        Core.VRLog.Info("Hands", $"laser gating: beam+collision always on; commit suppression active for [{families}].");
    }

    // ---- visuals -----------------------------------------------------------------------

    private void UpdateVisuals(Vector3 origin, Vector3 direction, float maxDistance, float scale)
    {
        if (_laser == null)
            CreateVisuals();

        // Visuals policy: always shown (user ruling 2026-08 — VisualsAllowed doc). The
        // change-deduped log below (test #19) stays so any future visual flip remains
        // attributable from the log.
        bool show = VisualsAllowed(origin, direction);
        if (_laser!.gameObject.activeSelf != show)
        {
            _laser.gameObject.SetActive(show);
            Core.VRLog.Debug("Hands", $"{_hand.Side} ray visuals {(show ? "shown" : "hidden")} — " +
                                      "ModalUI UI-surface cone gate.");
        }
        if (!show)
        {
            if (_reticle!.gameObject.activeSelf)
                _reticle.gameObject.SetActive(false);
            return;
        }

        // Test #14 item 2 — the beam is ALWAYS the straight aim ray. Both endpoints
        // lie on (origin, direction); hits clamp the LENGTH only, so the controller
        // alone controls the beam angle. The old geometry started at the knuckle and
        // converged on the hit POINT — when the hit jumped onto a canvas plane
        // (UiHitOverride) or a snapped hex (ReticleOverride, now removed) the beam
        // visibly changed angle ('zapped' onto elements).
        //
        // Length priority: UI-surface hit (code-intersected canvas/flat screen —
        // never pass THROUGH a menu, test #7) → physics hit → open-ended segment.
        // The UI point is projected onto the aim line: RayUguiDriver points are on
        // it by construction; FlatScreen's latched press point may drift off it, and
        // only its along-ray distance may influence the visuals.
        bool uiHit = _uiHitOverride.HasValue && Time.frameCount - _uiHitOverrideFrame <= 1;
        float length = uiHit
            ? Mathf.Max(0.02f * scale, Vector3.Dot(_uiHitOverride!.Value - origin, direction))
            : _current.HasHit
                ? _current.HitDistance
                : maxDistance * 0.25f;
        Vector3 end = origin + direction * length;

        // Visual origin (test #7 + #14): the beam reads as leaving the pointing finger. It now
        // starts AT the knuckle rather than at the knuckle PROJECTED ONTO THE AIM LINE.
        //
        // The projection existed so the drawn beam's direction was exactly the aim direction. That
        // held only while the visual hand sat ON the aim line — and it stopped holding the moment
        // the hands became freely seatable (roll, yaw, spread): the finger moves off the aim line,
        // the projected start stays on it, and the beam visibly leaves a point in mid-air beside
        // the finger. Starting at the real knuckle costs a small angular difference close to the
        // hand and keeps what actually matters: the END point is unchanged, so the beam still
        // points at exactly what a click will hit, and it comes out of the finger at any hand
        // tuning. (The knuckle, not the tip: the tip curls with the trigger pull.)
        Vector3 start = origin + direction * (0.03f * scale);
        if (Plugin.LaserFingerOrigin.Value)
        {
            Transform anchor = _hand.Rig.IndexKnuckle ?? _hand.Rig.IndexTip;
            if (anchor != null)
            {
                Vector3 toEnd = end - anchor.position;
                float span = toEnd.magnitude;
                start = span > 1e-4f
                    ? anchor.position + toEnd * Mathf.Clamp01(
                        Plugin.LaserFingerOffsetMeters.Value * scale / span)
                    : anchor.position;
            }
        }

        // Head-to-end distance drives BOTH the beam width and the reticle size —
        // constant angular size, independent of rig scale (see const block above).
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        float headDist = head != null ? Vector3.Distance(head.transform.position, end) : 1f;

        // Bounds × scale: the factor product is world units, the named bounds are real metres
        // (see the const block — this is what made the beam invisible in the 3D map room).
        _laser.widthMultiplier = Mathf.Clamp(headDist * BeamWidthAngularFactor,
                                             BeamWidthMinMeters * scale, BeamWidthMaxMeters * scale);
        _laser.SetPosition(0, start);
        _laser.SetPosition(1, end);

        if (_current.HasHit || uiHit)
        {
            if (!_reticle!.gameObject.activeSelf)
                _reticle.gameObject.SetActive(true);
            _reticle.position = end;
            // localScale sits under the rig-scaled hand — divide the world-space
            // target size by the parent's lossy scale.
            float worldSize = Mathf.Clamp(headDist * ReticleAngularFactor,
                                          ReticleMinMeters * scale, ReticleMaxMeters * scale);
            float parentScale = Mathf.Max(1e-4f, _hand.transform.lossyScale.x);
            _reticle.localScale = Vector3.one * (worldSize / parentScale);
        }
        else if (_reticle!.gameObject.activeSelf)
        {
            _reticle.gameObject.SetActive(false);
        }
    }

    private void CreateVisuals()
    {
        var laserGo = new GameObject($"GloomhavenVR.Laser_{_hand.Side}");
        laserGo.transform.SetParent(_hand.transform, worldPositionStays: false);
        _laser = laserGo.AddComponent<LineRenderer>();
        _laser.useWorldSpace = true;
        _laser.positionCount = 2;
        _laser.material = CreateBeamMaterial(out Color color);
        _laser.startColor = color;
        _laser.endColor = new Color(color.r, color.g, color.b, 0.05f);
        _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _laser.receiveShadows = false;
        // Draw after the floated modal (sortingOrder 1000) so the beam stays visible
        // on dialogs; depth test still occludes it behind solid geometry.
        _laser.sortingOrder = RayVisualSortingOrder;

        GameObject reticleGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        reticleGo.name = $"GloomhavenVR.Reticle_{_hand.Side}";
        Object.Destroy(reticleGo.GetComponent<Collider>());
        reticleGo.transform.SetParent(_hand.transform, worldPositionStays: true);
        Renderer reticleRenderer = reticleGo.GetComponent<Renderer>();
        reticleRenderer.sharedMaterial = _laser.material;
        // Same as the laser: draw the hit dot above the modal host (sortingOrder 1000)
        // so it never vanishes on a dialog; ZTest LEqual still hides it behind solids.
        reticleRenderer.sortingOrder = RayVisualSortingOrder;
        _reticle = reticleGo.transform;
        _reticle.gameObject.SetActive(false);

        // Lazily created AFTER HandsDriver's tree-wide VRLayers.Apply — layer them here.
        // Only reached from UpdateVisuals, i.e. while Active — the new laser GO's
        // default-active state is already correct; SyncActiveState keeps it so.
        Core.VRLayers.Apply(laserGo);
        Core.VRLayers.Apply(reticleGo);
    }

    private Material CreateBeamMaterial(out Color color)
    {
        color = new Color(0.45f, 0.8f, 1f, 0.35f);
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        var material = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
        material.color = color;
        // Test #14 item 3: reticle/beam partially vanished ON dialogs — world-space
        // canvas graphics (UI/Default, TMP) draw in the transparent queue (~3000)
        // and within one queue transparents sort by depth, so canvas geometry at the
        // same plane could draw OVER the dot/beam. Render queue 4600 draws after
        // every canvas graphic unconditionally; real scene occlusion is preserved
        // because the shader still depth-TESTS (ZTest LEqual) against the opaque
        // scene's depth buffer while UI shaders write no depth at all. This is the
        // robust variant vs. a camera-facing plane offset, which would need per-
        // surface tuning and can still lose to TMP sub-mesh sorting.
        material.renderQueue = 4600;
        return material;
    }

    // ---- effective-state sync ----------------------------------------------------------

    private bool _wasActive;
    private string _lastReason = "";

    /// <summary>
    /// Applies the level-derived state to the visuals and emits ONE Debug line per
    /// state/reason change naming the cause (test #19: a future silent disappearance
    /// must be attributable from the log). Change-deduped — nothing per-frame.
    /// </summary>
    private void SyncActiveState(bool active)
    {
        string reason = active ? "active"
            : !_enabled ? $"mode policy — no Ray in the {VRModeStateMachine.CurrentMode} mask"
            : !_hand.HasPose ? "no pose (tracking lost)"
            : IsHolding ? "hand is holding a grabbable (level-derived, releases with it)"
            : GripSuppressed ? $"grip HELD on this hand (grip={_hand.GripValue:0.00}) — physical " +
              "press posture, the beam stands down for THIS hand only and returns on release"
            // Keyed on the ZONE, not the card: sweeping from card to card inside one contact
            // episode must not re-log. The card itself is named by the STAND-DOWN line.
            : $"physical card contact ({_cardContactZone}) — the hand is in a card, so the beam " +
              "stands down (deadline-derived, returns on its own)";
        if (active == _wasActive && reason == _lastReason)
            return;
        _wasActive = active;
        _lastReason = reason;
        Core.VRLog.Debug("Hands", $"{_hand.Side} ray {(active ? "ON" : "OFF")} — {reason}.");

        if (_laser != null && _laser.gameObject.activeSelf != active)
            _laser.gameObject.SetActive(active);
        if (_reticle != null && !active)
            _reticle.gameObject.SetActive(false);
    }

    // ---- fan occlusion -----------------------------------------------------------------

    /// <summary>A fan card nearer than a target by more than this occludes it (meters, scale 1).</summary>
    private const float FanOcclusionEpsilonMeters = 0.005f;

    private static float s_nextFanOcclusionLogAt;

    /// <summary>
    /// Nearest MOD-FAN card hit along the ray, or +inf (all fans closed / ray misses them).
    /// Same geometric card-rect tests the fan plucks use (no sticky bias) — the nearest card
    /// along the ray is the topmost by construction. NOT a physics query: fan cards are
    /// trigger colliders on the mod layer, invisible to the pick Mask.
    ///
    /// COVERS EVERY OPEN FAN SURFACE, not just the hand fan (requirement B — laser clicked
    /// THROUGH the item fan onto the initiative track): the hand fan (<see cref="Cards.CardFan.Current"/>),
    /// the discard/burnt browse arc (<see cref="Cards.PileBrowser.Current"/>) and the ITEM fan
    /// (<see cref="Cards.ItemsPile.Current"/>). The reverse arbitration already existed — each
    /// fan-laser path in CardsDriver.3.Laser yields to a NEARER uGUI hit
    /// (<c>RayUgui.HasHit &amp;&amp; RayUgui.HitDistance &lt; dist</c>) — but the forward direction only
    /// consulted the hand fan here, so a hovered item-fan chip popped while RayUguiDriver kept
    /// hovering/clicking the converted initiative-track panel BEHIND it at greater distance.
    /// Taking the min over all three fans closes that gap at the single arbitration seam every
    /// consumer (board physics pick above, RayUguiDriver, RayGrabDriver) already honours:
    /// "the closer game UI wins", in BOTH directions, for every fan alike.
    ///
    /// ANCHORING COVERAGE (user round 2 verification): <c>ItemsPile.TryLaserRaycast</c> gates on
    /// <c>IsOpen</c> + live chip transforms only — it covers the item fan in EVERY open state
    /// alike (board-anchored poke-toggle wall, palm-held FLOATING fan, head-relative fallback,
    /// and the demand/surrender re-raised fan), so "the floating fan is invisible to the
    /// occluder" is ruled out; the round-2 leak was temporal (the pull-jerk hold above), not
    /// spatial.
    /// </summary>
    private static float ComputeFanOccluder(Vector3 origin, Vector3 direction, float maxDistance)
    {
        float best = float.PositiveInfinity;

        Cards.CardFan? fan = Cards.CardFan.Current;
        if (fan != null
            && fan.TryRaycast(origin, direction, null, out _, out _, out float fanDist)
            && fanDist <= maxDistance && fanDist < best)
            best = fanDist;

        Cards.PileBrowser? browse = Cards.PileBrowser.Current;
        if (browse != null
            && browse.TryRaycast(origin, direction, null, out _, out _, out float browseDist)
            && browseDist <= maxDistance && browseDist < best)
            best = browseDist;

        Cards.ItemsPile? items = Cards.ItemsPile.Current;
        if (items != null
            && items.TryLaserRaycast(origin, direction, null, out _, out _, out float itemDist)
            && itemDist <= maxDistance && itemDist < best)
            best = itemDist;

        return best;
    }

    /// <summary>
    /// Nearest CONTROL-BOARD solid hit along the ray, or +inf (no board / board hidden / ray
    /// misses it). Same pattern as <see cref="ComputeFanOccluder"/>: the board is mod-layer
    /// trigger geometry the physics pick <see cref="Mask"/> never sees, so its occlusion must
    /// be measured geometrically up front — the scan itself lives with the board
    /// (<c>PlayTray.RaycastSolidDistance</c>: registered laser-target colliders + slotted
    /// cards, the exact surfaces the board laser hovers). See the
    /// <see cref="SolidOccluderDistance"/> root-cause doc.
    /// </summary>
    private static float ComputeBoardOccluder(Vector3 origin, Vector3 direction, float maxDistance)
    {
        Cards.PlayTray? tray = Cards.PlayTray.Current;
        return tray != null
            ? tray.RaycastSolidDistance(origin, direction, maxDistance)
            : float.PositiveInfinity;
    }

    /// <summary>
    /// [Optimize] LeanLogStrings gate (2026-07 perf pass). <see cref="NoteFanOcclusion"/> throttles
    /// itself to one line per second INSIDE the method — but its callers had already interpolated
    /// the message (and read <c>UnityEngine.Object.name</c>, which allocates a fresh managed string
    /// on every single access) before the call. That is two heap allocations per frame per hand for
    /// a line printed once a second. Callers now ask this first and skip the string work entirely
    /// when nothing would be logged; with the optimization off it answers true and the old
    /// unconditional behaviour returns.
    /// </summary>
    public static bool WantFanOcclusionNote =>
        !Core.PerfConfig.LeanStrings || Time.unscaledTime >= s_nextFanOcclusionLogAt;

    /// <summary>
    /// Throttled note (shared across both hands and every target kind) that a solid mod-owned
    /// occluder — the raised card fan or the control board, per
    /// <see cref="SolidOccluderIsBoard"/> — blocked a would-be pick BEHIND it; names the
    /// blocked target so a hardware log shows the fix engaging. Called from the board pick
    /// here and the far uGUI/grab drivers. (Name kept from the fan-only era; the board joined
    /// the same seam 2026-08-04.)
    /// </summary>
    public void NoteFanOcclusion(string blockedTarget, float targetDistance)
    {
        if (Time.unscaledTime < s_nextFanOcclusionLogAt)
            return;
        s_nextFanOcclusionLogAt = Time.unscaledTime + 1f;
        string culprit = SolidOccluderIsBoard ? "the control board" : "the raised card fan";
        Core.VRLog.Info("Hands", $"{_hand.Side} ray occluded by {culprit} — blocked {blockedTarget} " +
                                 $"(sits {targetDistance:F2} m out, behind a solid surface at {SolidOccluderDistance:F2} m" +
                                 $"{(!SolidOccluderIsBoard && FanOccluderHeld ? ", pull-jerk HOLD — beam just left the fan" : "")}).");
    }

    internal void DestroyVisuals()
    {
        if (_laser != null)
        {
            Object.Destroy(_laser.gameObject);
            _laser = null;
        }
        if (_reticle != null)
        {
            Object.Destroy(_reticle.gameObject);
            _reticle = null;
        }
    }
}
