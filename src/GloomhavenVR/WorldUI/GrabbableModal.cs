using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI; // the game's UIWindow lives here (decompiled/GH.Runtime/UnityEngine.UI/UIWindow.cs)

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Makes a floated modal window (<see cref="ModalFallback"/>) a GRABBABLE + SCALABLE
/// world element — exactly like the control board / combat log — by reusing the SHARED
/// grab core (<see cref="PanelGrabHandle"/> + <see cref="IPanelGrabOwner"/>): one hand
/// grips the brass bar under the panel to MOVE it, two hands RESIZE it (the SHARED range
/// <see cref="PanelGrabHandle.MinScale"/>–<see cref="PanelGrabHandle.MaxScale"/> = 0.15×–2×;
/// the floor was lowered from 0.5 and a local re-clamp at 0.5 would re-cap the pinch). No new
/// grab mechanism is invented; this only owns a small mod-owned holder/frame the same way
/// <see cref="Surfaces.CombatLogSurface"/> does, and lets the game-owned world-space host
/// FOLLOW that frame each tick.
///
/// TRANSFORM LAYOUT (mirrors CombatLogSurface): holder (identity pose, localScale =
/// diorama WorldScale) → frame (grab ROOT at the PANEL CENTER; localScale = user size
/// factor, clamped to PanelGrabHandle's [MinScale, MaxScale] = 0.15–2) → bar visual (a
/// child just under the panel's bottom edge). The
/// grab-zone collider lives on the frame with its centre offset down to the bar, so the
/// grip lands on the visible handle while the frame origin stays at the panel centre.
///
/// HOST FOLLOW: the game-owned host is never re-parented (mount-seam reversibility rule);
/// each <see cref="Tick"/> its world pose is copied from the frame and its scale is
/// metersPerPixel × WorldScale × <c>extraScale</c> × factor — the SAME convention
/// <see cref="ModalFallback"/> places it with, plus the live user grab factor. When the
/// user is not gripping, the frame is static, so the host is static too (no drift).
///
/// INPUT: poke/laser clicks on the menu widgets are unaffected — they drive the real
/// uGUI through the host's raycaster (UguiPokeSurfaces / RayUguiDriver), a different path
/// than the grip-grab, and the bar sits BELOW the content so it never overlaps a widget.
/// The world-grab yields any grip that starts on a highlighted/held grabbable, so gripping
/// the bar moves the menu instead of the diorama (PanelGrabHandle's documented arbitration).
/// </summary>
internal sealed class GrabbableModal : IPanelGrabOwner
{
    /// <summary>Gap below the panel's bottom edge to the bar centre (frame-local, scale-1 metres).</summary>
    private const float BarGapMeters = 0.03f;
    private const float BarThickness = 0.024f;
    private const float BarWidthFraction = 0.55f;
    private const float ZoneWidthFraction = 0.62f;
    private const float MinBarWidth = 0.04f;

    /// <summary>
    /// EMPTY-GOLD-PLATE FIX (torbogen screenshot 2026-08-02): panel height (real metres) at or
    /// above which the bar keeps its full thickness/gap. The fixed 2.4 cm bar + 3 cm gap were
    /// sized for board-scale menus; under the ~6 cm level-message ACTION STRIP the same bar
    /// rendered nearly as tall as the strip itself and a full strip-height away from it — on
    /// the flat mirror it read as a detached EMPTY GOLD RECTANGLE floating below the hint
    /// (identified in the screenshot by its brass colour, 55 % width and centred position one
    /// gap below the strip). Panels shorter than this reference get a proportionally slimmer,
    /// closer bar so the handle visually attaches to its window; taller panels (ESC/Options,
    /// results, tutorial boxes) are numerically unchanged.
    /// </summary>
    private const float BarFullSizePanelHeightMeters = 0.30f;

    /// <summary>Floor of the short-panel bar proportion — the visible strip (and its padded
    /// laser collider, which scales with it) must stay a comfortable target.</summary>
    private const float MinBarProportion = 0.5f;

    /// <summary>
    /// LOST-MENU FIX: cross-section pad of the LASER-only bar collider, in bar-local units
    /// (the bar cube is unit-sized, scaled to barWidth × BarThickness × BarThickness — so
    /// 1.5 ≈ a 3.6 cm strip). Just enough slack to point at the 2.4 cm visible bar
    /// comfortably, WITHOUT re-growing the swallow-everything zone the incident showed:
    /// the palm ZONE collider (5 cm, 62% width) had been the laser target too, and since
    /// the floated menu sits between the user and the board, every trigger aimed at the
    /// cards hit it and dragged the (possibly off-view) menu instead.
    /// </summary>
    private const float BarColliderPad = 1.5f;

    /// <summary>
    /// Item 3: the brass grab bar must OCCLUDE the menu content behind it (foreground is
    /// foreground). Unity sorts EVERY renderer by sortingLayer -> SORTINGORDER first (only then by
    /// renderQueue / distance - see <c>RayInteractor.RayVisualSortingOrder = 5000</c>, which is
    /// exactly how the laser dot draws over a floated modal). An opaque (ZWrite-on) brass material
    /// makes the bar read solid rather than semi-transparent; the order below is what lifts it over
    /// the depthless menu canvas.
    ///
    /// <para>TRANSPARENCY ROUND: this is no longer an absolute value (it was 1100, one hundred over
    /// the old fixed modal tier). The menu's own order now moves every frame with its eye distance
    /// (CanvasConversion.8.Order.cs), so the bar rides it as an ORDER FOLLOWER at this OFFSET -
    /// always over its own window, always under a panel that is genuinely nearer than the window.
    /// A fixed 1100 would have made the bar pierce every nearer panel, which is precisely the
    /// "order beats distance" defect the whole round is about. The offset stays below
    /// <c>CanvasConversion.PanelOrderStep</c>, which is what guarantees the second half.</para>
    /// </summary>
    private const int BarOrderOffset = 4;

    private ConvertedPanel _panel = null!;
    private float _extraScale = 1f;             // ModalFallback.WindowScaleFactor (host shrink)

    /// <summary>
    /// Item 2: the diorama scale CAPTURED ONCE at spawn. The menu SIZE is derived from this
    /// fixed reference instead of the live <see cref="PanelLayout.WorldScale"/>, so zooming the
    /// diorama after the menu opens no longer grows/shrinks it (position stays a fixed world
    /// point regardless). Only the user's two-hand grab factor still resizes it on top.
    /// </summary>
    private float _spawnWorldScale = 1f;
    private string _logName = "Menu";

    /// <summary>DIAG-throttle (spam fix): seconds between MODAL DIAG snapshot lines while the host moves.</summary>
    private const float DiagThrottleSeconds = 1f;

    // DIAG-throttle state: last host pose (movement detection) + per-panel next-allowed stamp.
    private Vector3 _diagLastPos;
    private Quaternion _diagLastRot = Quaternion.identity;
    private float _diagNextAllowed;

    // ---- THE INK CAPTURE (ModBuild 236) ---------------------------------------------------------
    //
    // WHAT THE USER PHOTOGRAPHED (.planning/debug/quest_überlap.jpg, ModBuild 235): the brass bar lay
    // across the REWARD row of the last battle goal, so its text could not be read; and in
    // .planning/debug/story_fertig.jpg the same bar runs off to the lower right, far past anything the
    // window draws. His ruling was explicit — "Ich will nicht das sich die Größe des Fensters
    // dynamisch verändert wie es früher war" — so the window may NOT be resized. The bar moves instead.
    //
    // THE TWO GEOMETRY FACTS BEHIND IT, both measured, not inferred:
    //   1. THE BAR SAT BELOW THE FRAME, NOT BELOW THE INK. The hardware log's capture line for
    //      'New Party display' in the battle-goal phase reads
    //        host rect 1988x1080 + HELD overspill L0 R32 D384 U32 px
    //      and names the graphic that sets the bottom edge: BOTTOM (yMin) -913 set by 'Rewards' at
    //      (-255,-913)-(284,-851). The host rect's own bottom is y=-540. The window therefore draws
    //      373 px BELOW its own frame, and SyncBar placed the bar at -540 minus one gap — on top of
    //      the row called 'Rewards', which is the row called 'Belohnungen' in his screenshot.
    //   2. THE BAR WAS CENTRED ON THE FRAME, NOT ON THE INK. The same session's fit line reports what
    //      that window actually draws in that phase: a CHARACTER COLUMN of 328x1080 px seated at
    //      x=-982, and one open sub-view, 'UI Battle Goal Picker Window', at x -654..-93. So the ink
    //      spans x -982..-93 — 889 px of a 1988 px frame, centred at x=-537 — while the bar was
    //      centred at x=0 with a half-width of 0.55/2 x 1988 = 547 px. Its left end landed INSIDE the
    //      picker and its right end ran ~640 px past the rightmost thing the window draws.
    //
    // THE POLICY, and why it is this one. A SEAM IS A CAPTURE, NEVER A PER-FRAME READING: a bar
    // re-derived every tick would visibly slide ("man sieht wie es dahin springt"). So:
    //   * A GENERATION is reset by a genuine EVENT and by nothing else — the set of active sub-view
    //     roots changed (PanelInkBounds.ActiveSetSignature, which is what a tab press moves), or the
    //     host rect itself resized. On a reset the envelope starts empty and the bar KEEPS ITS OLD
    //     PLACE until the first sample of the new generation lands, so a transition costs one move.
    //   * WITHIN a generation the union is MONOTONE OUTWARD — down, left and right only. It can never
    //     creep back toward the content, so it cannot oscillate, and the settle burst (a sample every
    //     InkSettleStrideFrames for InkSettleFrames) lets a view that is still sliding in push the bar
    //     further away without ever pulling it back.
    //   * Monotone for the WINDOW'S WHOLE LIFE was rejected. That is the detached-bar defect
    //     BarFullSizePanelHeightMeters already exists to prevent: switch from the tall equipment view
    //     to a short one and the bar would hang a view-height below nothing at all.
    //   * A LOW-RATE VERIFY (InkVerifyStrideFrames, ~1 s) keeps sampling after the burst, and because
    //     it is monotone outward its only power is to notice that content now hangs BELOW the bar. It
    //     is what covers the case the signature cannot see: the battle-goal picker is re-seated next
    //     to a DIFFERENT character without any sub-view opening or closing, which is precisely the
    //     "Questinfo des letzten Characters" the user is complaining about. Outside the settle burst
    //     a growth must REPEAT before it commits, so a hover tooltip — which the game re-parents onto
    //     the window itself — cannot pin the bar away from the window for the rest of the generation.
    // The bar is never raised above the frame's own bottom edge, whatever the ink says — moving it UP
    // would change every already-accepted window in the mod for no reported reason.
    //
    // ---- THE RELEASE SIDE (ModBuild 239) --------------------------------------------------------
    //
    // WHAT WAS MISSING. Everything above describes how the envelope GROWS and when it is thrown away
    // wholesale (a generation event). There was no third state: an envelope could not shrink under
    // its own steam, so any content that ever reached low held the bar low until the SIGNATURE moved.
    // The signature is the set of active sub-view ROOTS, and a popup that opens and closes by
    // animating its CanvasGroup from 0 to 1 and back changes not one bit of it. That is a real hole
    // and it is the one the coordinator predicted; it was NOT the cause of grosser_abstand.jpg (the
    // ModBuild 238 log reads y=-913 on `sample 1 of generation 1, held 0 frame(s)`, so the envelope
    // had contributed nothing — the MEASUREMENT was wrong, see PanelInkBounds' class comment). But
    // repairing that measurement is exactly what opens this hole for real: from ModBuild 239 a popup
    // fading in genuinely pushes the ink down, and something has to bring it back.
    //
    // THE POLICY: GROW FAST, SHRINK SLOW, IN ONE STEP, AND NEVER FROM THE SAMPLE THAT IS ARGUING.
    //   * Only OUTSIDE the settle burst, and only on a sample where the monotone envelope did not
    //     grow. A sample that grows any edge cancels a pending release outright: growth is the safe
    //     direction and it always wins.
    //   * The candidate is the RAW measurement, which is contained in the held envelope by
    //     construction on such a sample. It must sit more than InkReleaseDeadBandPx inside the held
    //     rect on at least one edge — a 1 px layout settle is not a recession.
    //   * It must then REPEAT for InkReleaseConsecutive verify samples, i.e. hold for ~3 s of wall
    //     clock, agreeing to within InkReleaseStabilityPx each time; the candidate carried forward is
    //     the OUTERMOST rect of the run, so a run that wobbles commits the most conservative member
    //     of itself and never something tighter than was actually measured.
    //   * WHY IT CANNOT FLAP. A flap needs two readings alternating. Growth commits on sight, a
    //     release needs three consecutive agreeing readings a second apart — an alternating signal
    //     never assembles three, so it latches on the OUTER extent, which is the side that cannot
    //     cut content. The cost of the asymmetry is that a genuine recession is honoured ~3 s late,
    //     and 3 s of the bar being too far away is the failure mode the user did not report.
    //   * WHY IT CANNOT CUT CONTENT. The held placement is never given up until the smaller rect has
    //     been measured three times; while a release is pending the bar stays exactly where it is.
    //     After a release commits, the settle burst is RE-ARMED (the two lines at the commit site):
    //     without that, re-appearing content would have to pass the repeat gate at the 1 s verify
    //     stride and could be cut for two whole seconds, which is quest_überlap.jpg with a delay on
    //     it. Re-armed, the next sample is 4 frames away and growth from it is immediate.
    // The repeat gate is deliberately BYPASSED for a release — it exists to make a growth prove
    // itself twice, and a release has already proven itself three times against a stricter test.
    //
    // ---- ModBuild 241: THE INPUT WAS WRONG, AND THE DAMPING STAYS AS IT IS ----------------------
    //
    // User report, verbatim: "Mouseovers sollen den greifbar nicht vergrößeren, sonst kommt es
    // ständig dazu, dass der Balken sich hektisch verändert wenn man mit dem Laser durch Elemente mit
    // mouseovers zB der Kartenliste geht." The whole mechanism is in PanelInkBounds (its class comment
    // carries the report, the root cause and the log numbers); what belongs HERE is what it means for
    // the policy above, because everything above was written to damp churn and the user is reporting
    // churn anyway.
    //
    // IT WAS NEVER A DAMPING FAILURE. Two of the three gates above were being bypassed at the source:
    //   * A raised mouseover changed PanelInkBounds.ActiveSetSignature, which is a GENERATION EVENT.
    //     A generation reset is the one path that legitimately discards the envelope wholesale, so the
    //     repeat gate, the monotone union and the release run were all reset by every hover and every
    //     un-hover. The ModBuild 239 log has 'New Party display' at generation 51 in one session.
    //   * On the sample after such a reset the envelope is re-SEEDED rather than grown, and the
    //     repeat gate is exempt for the first commit of a generation, so a hover widget went straight
    //     into the union with nothing standing in its way.
    // WITH THE INPUT CLEAN, EVERY CONSTANT ABOVE KEEPS ITS VALUE, deliberately. They were derived
    // against a different failure — content that genuinely appears and disappears inside a window
    // (a rewards popup fading its CanvasGroup, a sub-view sliding in) — and that failure has not
    // changed. Lowering InkReleaseDeadBandPx or InkReleaseConsecutive now would trade away the
    // protection quest_überlap.jpg bought for a symptom that no longer has a cause; raising them
    // would slow a genuine recession that the user has never complained about. The honest test of
    // whether they are still right is the falsifier: a session whose GRAB BAR lines show a stable
    // generation number and a MOUSEOVER LEDGER with a non-zero refused count is the policy doing
    // nothing because there is nothing to do, which is the intended steady state.
    private const int InkSettleFrames = 24;
    private const int InkSettleStrideFrames = 4;
    private const int InkVerifyStrideFrames = 60;
    private const float InkReportThrottleSeconds = 1f;

    /// <summary>Verify samples a recession must survive, unbroken, before the envelope gives up the
    /// ground. Three at <see cref="InkVerifyStrideFrames"/> is ~3 s at 60 Hz.</summary>
    private const int InkReleaseConsecutive = 3;

    /// <summary>How far inside the held envelope the raw measurement must sit before it counts as a
    /// recession at all, in the window's authored px. One 32 px quantum — the same dead band the
    /// capture frame's own shrink hysteresis uses, so the two agree about what "smaller" means.</summary>
    private const float InkReleaseDeadBandPx = 32f;

    /// <summary>Per-edge tolerance for "the same recession again" across the run. Looser than
    /// <see cref="SameRect"/>'s half pixel on purpose: the run must survive a breathing layout, and
    /// the rect carried forward is the run's OUTER union, so slack here can only ever commit a
    /// LARGER rect than was measured.</summary>
    private const float InkReleaseStabilityPx = 16f;

    private bool _inkValid;                     // a committed rectangle exists (survives a generation reset)
    private bool _inkGenSeeded;                 // this generation has contributed a sample to it yet
    private Rect _inkRect;                      // host-local uGUI px — the COMMITTED, monotone union
    private int _inkGraphics;
    private int _inkPlates;
    private int _inkEmptyText;
    private int _inkModChrome;
    private bool _inkTruncated;
    private string _inkBottomName = string.Empty;
    private int _inkSignature;
    private bool _inkSignatureValid;
    private Rect _inkHostRect;
    private bool _inkHostRectValid;
    private int _inkGeneration;
    private int _inkSamples;
    private int _inkNextSampleFrame = -1;
    private int _inkSettleUntilFrame = -1;
    private int _inkCommitFrame = -1;
    private string _inkCause = "the window was built";
    private bool _inkReportDue;
    private bool _inkFallbackDue;
    private bool _inkFallbackReported;
    private float _inkNextReportAllowed;
    private int _inkReportsSuppressed;
    private int _inkHeldFrames;
    private Rect _inkPending;
    private bool _inkPendingValid;
    private int _inkGrowthsDeferred;
    private int _inkFaint;
    private int _inkModChromeMask;

    // ---- THE CLOSE X ON THE INK (ModBuild 242) ---------------------------------------------------
    //
    // User report, verbatim (2026-08-24): "Wenn der Balken klein ist weil die Länge des Fensters klein
    // ist muss auch das 'x' zum Schließen an neuen Rand oben links. Aktuell haben wir die Situation,
    // dass das 'x' weit rechts, der Balken klein und zwischen dem linken Teil und dem X unsichtbare
    // Collider für den Laser ist. Wenn kleineres Fenster, dann voll mit verschobenem X und ohne
    // unsichtbaren Collider."
    //
    // WHY THE X IS DRIVEN FROM HERE AND NOT FROM ModalCloseButton. The plate is built once, at
    // conversion time, by a caller that has no ink; the ink is an EVENT-DRIVEN CAPTURE owned by this
    // class and it changes when a tab is pressed. This is the only object in the mod that holds both
    // the committed union and a per-tick hook on the window, so it is the only place the two can be
    // brought together without a second instrument. The geometry itself stays in ModalCloseButton
    // (ModalCloseButton.PlaceAgainstInk), which owns the plate's size and inset — this class passes
    // the rectangle and stores the answer for the falsifier, exactly as it does for the brass bar.
    //
    // THE PLATE IS RE-FOUND, NOT REMEMBERED ACROSS A REBUILD. It is a child of the game-owned host,
    // which ModalFallback may release and re-convert under a live GrabbableModal; a cached reference
    // would be Unity-null after that and the X would silently stop following. The probe is a
    // direct-child Find on a host with a handful of children, gated to once every
    // CloseXProbeStrideFrames while the plate is missing — a window that legitimately has no X (the
    // scenario-end windows are excluded by design) therefore costs one Find every half second and
    // never a scene sweep ([[findobjectsoftype-is-the-default-suspect]]).
    private const int CloseXProbeStrideFrames = 30;
    private RectTransform? _closeX;
    private int _closeXNextProbeFrame = -1;
    private ModalCloseButton.XPlacement _closeXPlacement;
    private bool _closeXPlaced;

    // ---- THE MOUSEOVER EXEMPTION'S LEDGER (ModBuild 241) ----------------------------------------
    // Counted and named for the same reason Ink.Faint and Ink.Plates are: the next round will ask
    // "did the mouseover exclusion do anything", and a falsifier that cannot answer that costs a
    // build. _inkTransientLife and _inkSigTransientLife are the two halves of the defect — graphics
    // refused from the UNION, and hover subtrees refused from the GENERATION SIGNATURE — so a session
    // in which the bar still twitches can be read against which of the two, if either, ever fired.
    private int _inkTransient;                  // refused on the LATEST sample
    private int _inkTransientLife;              // refused over this window's life
    private int _inkTransientMask;              // families seen over this window's life
    private int _inkSigTransientChildren;       // raised mouseovers hidden from the LATEST signature
    private int _inkSigTransientLife;
    private Rect _inkReleaseCandidate;          // the OUTER union of the current recession run
    private bool _inkReleaseValid;
    private int _inkReleaseRun;                 // agreeing verify samples so far
    private int _inkReleases;                   // committed releases over this window's life

    /// <summary>Two host-local rectangles equal to within half a uGUI pixel.</summary>
    private static bool SameRect(Rect a, Rect b) =>
        Mathf.Abs(a.xMin - b.xMin) <= 0.5f && Mathf.Abs(a.xMax - b.xMax) <= 0.5f
        && Mathf.Abs(a.yMin - b.yMin) <= 0.5f && Mathf.Abs(a.yMax - b.yMax) <= 0.5f;

    /// <summary>Two host-local rectangles equal to within <paramref name="tol"/> px on every edge.</summary>
    private static bool NearRect(Rect a, Rect b, float tol) =>
        Mathf.Abs(a.xMin - b.xMin) <= tol && Mathf.Abs(a.xMax - b.xMax) <= tol
        && Mathf.Abs(a.yMin - b.yMin) <= tol && Mathf.Abs(a.yMax - b.yMax) <= tol;

    /// <summary>Does <paramref name="now"/> sit more than <see cref="InkReleaseDeadBandPx"/> inside
    /// <paramref name="held"/> on at least one edge? Containment itself is not tested because the only
    /// caller reaches this on a sample where the monotone union did not move, which IS containment.</summary>
    private static bool Receded(Rect held, Rect now) =>
        now.yMin > held.yMin + InkReleaseDeadBandPx
        || now.xMin > held.xMin + InkReleaseDeadBandPx
        || now.xMax < held.xMax - InkReleaseDeadBandPx
        || now.yMax < held.yMax - InkReleaseDeadBandPx;

    /// <summary>
    /// EVERY GrabbableModal THAT HAS ACTUALLY BUILT ITS HOLDER (ModBuild 230).
    ///
    /// <para>The holder is a SCENE-ROOT tree — <c>EnsureFrame</c> creates
    /// <c>GloomhavenVR.ModalGrab_*</c> with no parent, because the game-owned host FOLLOWS the frame
    /// rather than hanging off it. That inversion is what makes this the one piece of window chrome
    /// that does not die when its panel is released: destroying the host cannot reach it. Every
    /// release path in <see cref="ModalFallback"/> calls <see cref="Destroy"/> first and is correct
    /// today, so this list is not a fix — it is the only way anything could NOTICE a holder that
    /// outlived its window, which is the artefact the user photographed three of in one frame
    /// (.planning/debug/leeres_fenster2.jpg). <c>ModalFallback.SweepOrphanChrome</c> reads it.</para>
    ///
    /// <para>Registration is in <see cref="EnsureFrame"/>, NOT in the constructor, on purpose: an
    /// un-built GrabbableModal owns nothing visible, and listing one would make the sweep's
    /// "holders against windows" comparison mean something else than it says.</para>
    /// </summary>
    internal static readonly List<GrabbableModal> LiveHolders = new(8);

    /// <summary>The name this modal reports itself under in the log — the window name
    /// <see cref="Build"/> was given. Exposed so the orphan sweep can NAME what it destroyed; the
    /// ModBuild 225 round cost a build precisely because the stray bars carried no identity.</summary>
    internal string LogName => _logName;

    private Transform? _holder;                 // identity pose, localScale = diorama WorldScale
    private Transform? _frame;                  // grab root at the panel centre; localScale = user factor
    private Transform? _visual;                 // THE DRAWN pose — see the REMOTE POSE EASING block
    private Transform? _bar;

    private BoxCollider? _grabZone;
    private PanelGrabHandle? _handle;

    /// <summary>True while a hand grips the bar (owner skips no writes — the host just follows).</summary>
    internal bool IsGrabbed => _handle != null && _handle.IsGrabbed;

    /// <summary>
    /// USER-OWNED POSE (user report 2026-08-04: "Sie sollen dort fix bleiben, wo sie stehen,
    /// nicht springen"): latched TRUE the first time the player grips this window (and never
    /// cleared for the lifetime of the float). From that moment its pose belongs to the
    /// player - the presence-regain refloat (ModalFallback.RefloatOpenWindows) skips a
    /// user-moved window, because the user deliberately parks windows OUT of the view
    /// ("manchmal schiebe ich sie absichtlich zur Seite"). The lost-menu recall that used to
    /// share this exemption is GONE as of ModBuild 149: its 6 s out-of-view timer kept yanking
    /// windows back to the gaze (hardware log: repeated "MODAL RECALL: 'UI Options
    /// Window_unified' ... out of view for 6s" lines) and the user ruled that a window stays
    /// where it spawned unless it is actively moved, grabbed or not. So this flag now protects
    /// against ONE mover rather than two. ANY grip counts as the claim - even a grab released
    /// in place: the player
    /// touched it, so the mod stops second-guessing where it belongs. The X close button and
    /// the modal escape chord remain the rescue for a window the player genuinely loses.
    /// </summary>
    internal bool UserMoved { get; private set; }

    /// <summary>
    /// Item 1 (pause-menu size): re-seat the board-relative host shrink AFTER a full-screen menu's
    /// one-shot content fit shrank the host rect. The fit runs a few frames after Build, so the
    /// extraScale first derived from the pre-fit (full 1920) rect would leave the fitted panel
    /// mis-sized; the owner recomputes it from the fitted width and pushes it here. The next
    /// <see cref="Tick"/> applies it (host localScale = mpp × worldScale × extraScale × factor).
    /// </summary>
    /// <para>IT ALSO ENDS ANY IN-FLIGHT REMOTE GLIDE. A content re-fit is a SIZE change, and the
    /// user requirement is that a resize snaps rather than crawls (a glide would additionally be
    /// measured by <c>PanelPoseWatch</c>'s size-change assertion as the resize DISPLACING the
    /// window, which it did not).</para>
    internal void SetExtraScale(float extraScale)
    {
        if (!Mathf.Approximately(extraScale, _extraScale))
        {
            _easing = false;
            _visualValid = false;
        }
        _extraScale = extraScale;
    }

    /// <summary>
    /// Build the grab affordance for a freshly floated, freshly placed modal host. The
    /// frame is seeded at the host's CURRENT world pose (the host was just placed at the
    /// HMD), so the first follow tick keeps the panel exactly where it spawned — no jump.
    /// </summary>
    /// <para>TRANSPARENCY ROUND: the <c>depthMask</c> parameter is gone with the mask itself. Its
    /// job — "transparent HUD behind the floated menu must be occluded by it" — is now done by the
    /// draw ladder (CanvasConversion.8.Order.cs): the menu is simply painted after everything it is
    /// in front of, and before everything it is behind. The mask could only ever express that as a
    /// per-QUAD depth stamp, which is what cut the reported hard-edged holes into the panels behind
    /// a menu's transparent regions.</para>
    internal void Build(ConvertedPanel panel, float extraScale, string logName)
    {
        _panel = panel;
        _extraScale = extraScale;
        _logName = logName;
        // Item 2: snapshot the diorama scale now — the menu keeps THIS size regardless of later zoom.
        _spawnWorldScale = Mathf.Max(PanelLayout.WorldScale, 0.01f);
        EnsureFrame();
        if (_frame != null && panel.HostGo != null)
        {
            Transform h = panel.HostGo.transform;
            _frame.SetPositionAndRotation(h.position, h.rotation);
            _frame.localScale = Vector3.one; // user factor 1x
        }
        Tick(); // place host from the frame + size the bar immediately
    }

    /// <summary>
    /// Re-seat the frame (and thus the whole panel) at a fresh pose — the ONE entry point every
    /// external pose writer uses: the spawn / presence-regain refloat / pre-reveal re-place
    /// (<c>ModalFallback.ComputeHmdPose</c>'s three callers) and the two multiplayer pose appliers
    /// (<c>Net.RemoteStorySync</c> record 19, <c>Net.RemoteMapStory</c> record 21).
    ///
    /// <para><b>THE FRAME IS ALWAYS WRITTEN IMMEDIATELY AND EXACTLY.</b> What may be eased is the
    /// DRAWN pose, and only for a remote-driven shared window — see the REMOTE POSE EASING block for
    /// the whole design, and in particular for why easing the frame itself would have been the
    /// wrong lever.</para>
    /// </summary>
    internal void PlaceFrameAt(Vector3 position, Quaternion rotation)
    {
        EnsureFrame();
        if (_frame == null)
            return;
        // THE EXACT DISCRIMINATOR, not a distance heuristic. Every LOCAL placement path funnels
        // through ModalFallback.ComputeHmdPose, which announces itself to the pose lock
        // (PanelPoseWatch.Announce(Writer.Placement)) in the same frame it hands the pose to the
        // caller that writes it. The two NET appliers announce nothing. So "a mod placement is
        // happening this frame" is a fact already recorded next door, and asking it is what makes
        // "opened / re-seated ⇒ snap" and "a peer dragged it ⇒ glide" separable WITHOUT a magnitude
        // threshold — which would have been exactly wrong here, since today a whole drag arrives as
        // ONE large jump (see the block below) and a threshold would snap the one case that must
        // glide.
        bool ease = _shared
                    && !IsGrabbed
                    && _visualValid
                    && _panel != null && _panel.IsAlive && !_panel.RenderHidden && !_panel.RevealPending
                    && !PanelPoseWatch.PlacementAnnounced(_panel);
        _frame.SetPositionAndRotation(position, rotation);
        _easing = ease;
        if (ease)
            PeerPlaced = true;
        Tick();
    }

    /// <summary>
    /// Re-seat the frame AND the drawn pose in one step, with no easing under any circumstances —
    /// the pose lock's restore path (<see cref="PanelPoseWatch"/>'s <c>Refuse</c>).
    ///
    /// <para>A restore is a CORRECTION of a write the ruling refuses, so it has to be instant: a
    /// glide would put the window visibly somewhere the lock has already decided it may not be, and
    /// the lock re-measures against its own locked pose on the very next frame, which an in-flight
    /// glide would read as a further unattributed write.</para>
    /// </summary>
    internal void SnapFrameTo(Vector3 position, Quaternion rotation)
    {
        EnsureFrame();
        if (_frame == null)
            return;
        _frame.SetPositionAndRotation(position, rotation);
        _easing = false;
        _visualValid = false;   // the next Tick pins the drawn pose to the frame outright
        Tick();
    }

    // ---- IPanelGrabOwner --------------------------------------------------------------------

    Transform? IPanelGrabOwner.GrabRoot => _frame;

    // User ruling 2026-08-02 round 2: a window that is still render-hidden behind the reveal gate
    // must not be grabbable either — "grabbing/poking an invisible panel" is impossible by
    // construction here, because PanelGrabHandle.CanGrab reads GrabVisible and BOTH grab paths
    // consult it (RayGrabDriver's bar-collider ray test and ProximityGrabber's palm candidate
    // scan). The uGUI side needs nothing extra: the poke and the laser skip surfaces whose Canvas
    // is not isActiveAndEnabled (PokeInteractor.TickCanvases / RayUguiDriver), and the hide
    // disables the host canvas — nested X hit-canvases are only ever consulted through a winning
    // host, so they cannot be reached either.
    // OwnerRenderHidden gets the SAME treatment, by the same argument: a panel its own surface
    // render-hid (the character focus — a decision row / use bar that belongs to somebody the
    // player is not looking at) is just as invisible as one behind the reveal gate, and grabbing or
    // resizing an invisible window is exactly the "grabbing something that is not there" the ruling
    // above forbids. It is also the safe direction for the focus feature itself: the row must come
    // back at the geometry it left with, and a grab is the one thing that would move it meanwhile.
    bool IPanelGrabOwner.GrabVisible =>
        _panel != null && _panel.IsAlive && !_panel.RenderHidden && !_panel.OwnerRenderHidden
        && _holder != null && _holder.gameObject.activeInHierarchy;

    // Carry the yaw with the hand like the combat log — nothing else authors the
    // modal's rotation, so there is no two-writer jitter. Level in the plain WORLD
    // frame: floated windows are not part of the item-11 "board stays level for its
    // owner" contract, so they keep their historic behavior under a world tilt.
    PanelCarryMode IPanelGrabOwner.CarryMode => PanelCarryMode.Level;
    Quaternion IPanelGrabOwner.GrabLevelFrame => Quaternion.identity;
    Vector2 IPanelGrabOwner.GrabPitchLimits => new(-180f, 180f);

    /// <summary>Modal windows have no apparent-size ruling — the handle's generic factor range
    /// IS their resize window (see <see cref="IPanelGrabOwner.GrabScaleLimits"/>).</summary>
    Vector2 IPanelGrabOwner.GrabScaleLimits => new(PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);

    // Free placement: the menu stays wherever the user left it while open; a re-open
    // re-floats it at the HMD (ModalFallback), so there is nothing to persist here.
    // (Level-message chains are the one family whose pose DOES persist across the game's
    // brief close/reopen gaps — but that lives in ModalFallback's shared chain store,
    // which simply reads the live host pose at its update edges; still nothing to do here.)
    //
    // RE-FACE ON RELEASE (user request): the one-hand carry yaws the panel with the HAND
    // (CarryMode Level), so dragging a window to the side leaves it turned to wherever the wrist
    // happened to point — readable only edge-on. The moment the LAST hand lets go (this is the
    // release edge: PanelGrabHandle.OnRelease calls it once _handA and _handB are both gone),
    // snap the rotation back to facing the player. THE DRAWN WINDOW STAYS EXACTLY WHERE IT WAS PUT —
    // only the orientation is re-derived, through the same PanelPlacement.Facing the spawn placement
    // uses, so a moved window reads identically to a freshly floated one.
    //
    // ── 2026-08-24: "THE DRAWN WINDOW", AND WHY THAT WORD HAD TO CHANGE ──────────────────────────
    //
    // User report, verbatim: "Das automatische drehen des Fensters dreht das Fenster anscheinend noch
    // mit einem anderen Rotationspunkt des vollen Fensters - es soll es immer so drehen wie es
    // aktuell dargstellt ist in der Größe."
    //
    // THE ROOT CAUSE IS THE PIVOT, AND IT IS THE SAME PREMISE ModBuild 239 FIXED FOR THE BAR. Through
    // ModBuild 239 this block wrote `_frame.rotation = facing`, which turns the frame about the FRAME
    // TRANSFORM'S OWN ORIGIN, and derived `facing` from `_frame.position`. PanelInkBounds exists
    // because that origin is usually nowhere near what the window draws: for 'New Party display' the
    // ModBuild 239 hardware log measures the ink union at x -982..-654 px, centre x = -818 px, inside
    // a host rect that spans -994..994 px. At the 1.050 mm per authored px that same log reports for
    // that window, the drawn centre sits 859 mm to the LEFT of the point the window was being turned
    // about. So:
    //   * THE TARGET ANGLE WAS COMPUTED FOR A POINT THE USER CANNOT SEE. Facing() yaws the panel along
    //     the panel-to-head vector, and taking that vector from the frame origin instead of the drawn
    //     centre mis-aims the result by the angle the offset subtends at the head — atan(0.859/1.2) =
    //     36° at a typical 1.2 m reading distance. The window ends up NOT facing the player.
    //   * AND THE VISIBLE WINDOW SWUNG SIDEWAYS THROUGH AN ARC while the invisible frame turned neatly
    //     on the spot. The same log's largest release re-face on that window is `turned 43.7°`; a point
    //     859 mm off the pivot moves 2 x 0.859 x sin(43.7/2) = 639 mm on such a turn. Two thirds of a
    //     metre, for a change the comment above promised was orientation-only.
    // Both terms now use the DRAWN centre: the ink union's centre, in host-local uGUI px, mapped
    // through the host RectTransform (`_panel.HostRect`, NOT the grab frame — the ink rect is measured
    // in the host's local space by PanelInkBounds.TryHostLocalBounds, and the frame is a separate,
    // mod-owned transform the host merely follows). The rotation is applied ABOUT that world point, so
    // the drawn centre is a fixed point of the whole operation. For a window whose ink fills its frame
    // the centre is 0,0 and the position write is a no-op — every already-accepted window is
    // numerically unchanged, which is what makes an unchanged reading on them evidence.
    //
    // WHAT WAS REJECTED. (a) Rotating about the HOST RECT's centre rather than the ink's: that is the
    // frame again, and it is exactly the 88°-of-arc-to-draw-14° error ModBuild 234 wrote down. (b)
    // Rotating about the head-facing point and then re-running ModalFallback.ComputeHmdPose to
    // re-place the window: a placement is a POSE WRITE the pose lock classifies separately, and the
    // user's standing ruling is that a window stays where he put it. (c) Leaving the position alone
    // and only correcting the facing DIRECTION: that fixes the aim and leaves the 639 mm swing, which
    // is the half he actually photographed. (d) Measuring the ink fresh here instead of using the
    // committed envelope: the committed rect is the one the brass bar is placed from, so using it
    // makes the pivot and the handle agree by construction; a fresh measurement could differ from what
    // the user is looking at by a whole settle burst.
    //
    // ── 2026-08-22: THAT PARAGRAPH IS NOW THE "Always" MODE, NOT THE RULE ────────────────────────
    //
    // TWO SEPARATE USER STATEMENTS BOUND IT, and they are gated in that order below.
    //
    // (7b) A SHARED WINDOW NEVER RE-FACES, ON ANY CLIENT, AND THIS IS NOT CONFIGURABLE. Verbatim:
    //      "Da es ein Fenster für alle ist, sollen diese Fenster nach dem Greifen auch nicht die
    //      Orientierung nach dem Spieler ändern, wie es die anderen Fenster tun." … "Das gilt wie
    //      gesagt nur für die lokalen Fenster, Remote-Fenster (blau) sollen das gar nicht haben."
    //      The rotation on a window that belongs to everybody is a shared fact: correcting it toward
    //      the person who last moved it turns it AWAY from everyone else. And it is worse than
    //      cosmetic on the SENDER — the pose that record 19/21 published is corrected locally one
    //      frame later, so the two clients no longer agree about a pose that is supposed to be 1:1.
    //      This gate therefore sits FIRST and outranks the dial.
    //
    // (8) FOR LOCAL WINDOWS IT IS A THREE-WAY SETTING, [WorldUI] WindowFacing (see WindowFaceMode
    //     for the request verbatim and why the axis is the grab MODALITY): LaserOnly (the user's own
    //     default), Always, Never. The modality comes from PanelGrabHandle.LastGrabWasLaser, which
    //     is latched from the grabber's own identity at the gesture start — never guessed from how
    //     far away the hand was.
    //
    // WHAT IS NOT TOUCHED: this is a ONE-SHOT ON RELEASE in every mode. Nothing here makes a window
    // follow the head, and the standing project rule that nothing re-orients with head movement is
    // unaffected.
    void IPanelGrabOwner.OnGrabFinished()
    {
        if (_frame == null)
            return;
        if (!WantsReFaceOnRelease())
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        // SYNC THE HOST TO THE FRAME BEFORE ANYTHING IS MEASURED OFF IT. PanelGrabHandle moves the
        // frame from its own Update in undefined order against this module's, and LateSyncHost only
        // re-pins the host in LateUpdate — so at this instant the host can still be carrying LAST
        // frame's pose, and a pivot read through a stale transform is a pivot that is wrong by one
        // frame of hand motion. One extra Tick makes it exact by construction. It cannot double-
        // advance a remote glide: only a SHARED window ever eases, and WantsReFaceOnRelease has
        // already returned false for every shared window before this line is reached.
        Tick();

        Vector3 pivot = _frame.position;
        string pivotNote = "the FRAME ORIGIN (fallback — see the warning above)";
        RectTransform? host = _panel != null && _panel.IsAlive ? _panel.HostRect : null;
        bool inkPivot = _inkValid && host != null;
        if (inkPivot && host != null)   // the second test is for the nullable analyser, not for logic
        {
            Vector2 c = _inkRect.center;
            pivot = host.TransformPoint(new Vector3(c.x, c.y, 0f));
            pivotNote = $"the DRAWN centre (ink union centre {c.x:F0},{c.y:F0} px in the host's own "
                        + "authored pixels)";
        }

        Quaternion facing = PanelPlacement.Facing(pivot, head.transform.position);
        // Nothing to say (and nothing to write) when the drag already left it facing the player.
        // The fallback warning is deliberately BELOW this line: a release that turns nothing has not
        // used the wrong pivot for anything, and a warning on every such release would be the noise
        // that gets the real one skipped.
        if (Quaternion.Angle(_frame.rotation, facing) < ReFaceEpsilonDeg)
            return;

        if (!inkPivot)
        {
            // NEVER SILENTLY. A fallback here reproduces the exact defect this change fixes, and it
            // would look identical to the fix not working at all.
            VRLog.Warn("WorldUI",
                $"MODAL WINDOW: '{_logName}' released after a move — RE-FACING ABOUT THE FRAME ORIGIN, "
                + "which is the pre-ModBuild-240 behaviour and the thing the user reported: "
                + (host == null
                    ? "the window has no host RectTransform to map the ink through"
                    : "PanelInkBounds has no committed ink union for this window yet")
                + ". CONSEQUENCE: if what this window draws is not centred in its frame, the visible "
                + "window swings sideways through an arc as it turns, and the facing angle itself is "
                + "computed for a point that is not on the screen. The GRAB BAR CLEARS THE INK line "
                + "for this same window says why there is no union — look for its 'THE INK UNION "
                + "COULD NOT BE MEASURED' variant, which names the sample count and the cause.");
        }

        float turned = Quaternion.Angle(_frame.rotation, facing);
        // ROTATE THE RIGID BODY ABOUT THE PIVOT: turn, then translate so the pivot maps to itself.
        // Scale is untouched, so the drawn centre — a fixed point of the frame's local space — is a
        // fixed point of the whole operation, exactly. When the ink fills the frame the pivot IS the
        // frame origin and the position write below is the identity, which is how every window that
        // was already correct stays bit-for-bit where it was.
        Quaternion delta = facing * Quaternion.Inverse(_frame.rotation);
        Vector3 was = _frame.position;
        _frame.rotation = facing;
        _frame.position = pivot + delta * (was - pivot);
        float frameMovedMm = Vector3.Distance(was, _frame.position) * 1000f;
        // Push the new frame pose onto the game-owned host in the same frame, so the panel
        // does not visibly hang at the drag rotation until the next Tick.
        Tick();
        VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' released after a move — re-faced the player " +
                              $"(turned {turned:F1}°, yaw now {_frame.eulerAngles.y:F1}°) about " +
                              $"{pivotNote}. THE DRAWN WINDOW DID NOT MOVE: the pivot is a fixed point " +
                              $"of the turn by construction, and the mod-owned frame origin was carried " +
                              $"{frameMovedMm:F0} mm around it to keep it there. A frame travel of 0 mm " +
                              "means this window's ink is centred in its frame, which is the case the " +
                              "old frame-origin behaviour also got right.");
    }

    /// <summary>Below this the released panel already faces the player — no snap, no log.</summary>
    private const float ReFaceEpsilonDeg = 0.5f;

    /// <summary>
    /// Does THIS release re-derive the facing? The two gates of the block above, in order:
    /// the non-negotiable shared-window rule first, the player's dial second.
    ///
    /// <para>Logged once per refused release rather than silently, because "my window did not turn"
    /// and "my window turned" are the same complaint from opposite directions and the log has to say
    /// which rule decided it — the shared-window rule reads identically to the Never mode from the
    /// outside, and confusing the two would send the next round looking at the wrong file.</para>
    /// </summary>
    private bool WantsReFaceOnRelease()
    {
        if (_shared)
        {
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' released after a move and NOT re-faced " +
                                  "— it is a SHARED (blue-bar) window, whose orientation belongs to " +
                                  "the whole room. Turning it toward the player who moved it would " +
                                  "turn it away from everyone else, and it would silently disagree " +
                                  "with the pose this client just published on the wire. This is the " +
                                  "user's own rule ('Remote-Fenster (blau) sollen das gar nicht " +
                                  "haben') and it is NOT what [WorldUI] WindowFacing configures.");
            return false;
        }

        WindowFaceMode mode = WorldUIConfig.WindowFacing != null
            ? WorldUIConfig.WindowFacing.Value
            : Defaults.WindowFacing;   // a release before Bind completed (scene load): ship the default
        if (mode == WindowFaceMode.Always)
            return true;
        if (mode == WindowFaceMode.Never)
        {
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' released after a move and NOT re-faced " +
                                  "— [WorldUI] WindowFacing is Never, so a released window keeps " +
                                  "exactly the orientation it was let go at.");
            return false;
        }

        // LaserOnly (default). A laser carry translates only — PanelGrabHandle's laser branch writes
        // position and returns — so the window arrives still facing the way it used to and would be
        // read edge-on; that is the case the snap exists for. A HAND carry has already yawed the
        // window with the wrist for the whole drag, so the player aimed it themselves.
        bool laser = _handle != null && _handle.LastGrabWasLaser;
        if (!laser)
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' released after a HAND move and NOT " +
                                  "re-faced — [WorldUI] WindowFacing is LaserOnly (the default): a " +
                                  "hand carry yaws the window with your wrist for the whole drag, so " +
                                  "the orientation you let go at is the one you aimed. A LASER drag " +
                                  "on the same window still snaps round, because that carry never " +
                                  "rotates it at all.");
        return laser;
    }

    // ---- per-frame follow -------------------------------------------------------------------

    /// <summary>The game-owned host follows the mod-owned grab frame (position, rotation, scale).</summary>
    internal void Tick()
    {
        if (_panel == null || !_panel.IsAlive || _panel.HostGo == null || _panel.HostRect == null)
            return;
        EnsureFrame();
        if (_holder == null || _frame == null)
            return;

        // Item 2 (no auto-scale with world zoom): the menu SIZE uses the diorama scale CAPTURED
        // ONCE at spawn (_spawnWorldScale), NOT the live PanelLayout.WorldScale — so zooming the
        // diorama after the menu opens no longer grows/shrinks it. POSITION is the frame's world
        // point (copied below), independent of worldScale.
        //
        // CRITICAL (deadlock fix): the holder MUST stay at identity scale. It used to be scaled by
        // worldScale, which tied the frame's WORLD position to worldScale — frame.position =
        // holder.scale(worldScale) × frame.localPosition. worldScale settles/animates at scenario
        // start, so the modal's position (and size) collapsed toward the origin in lock-step and the
        // start dialog flew away, undismissable. With the holder at identity, frame.position is a
        // true world point — grab-stable and immune to worldScale drift.
        float worldScale = _spawnWorldScale;
        _holder.localScale = Vector3.one;
        if (!_holder.gameObject.activeSelf)
            _holder.gameObject.SetActive(true);

        // USER-OWNED POSE: the first grip claims the window for the player (see UserMoved).
        // Latched here, on the grip's first follow tick, so both grab paths (palm zone and
        // laser bar) and the two-hand resize all count - they run through this same follow.
        if (!UserMoved && _handle != null && _handle.IsGrabbed)
        {
            UserMoved = true;
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' grabbed - its pose is now PLAYER-OWNED: " +
                                  "no out-of-view recall and no presence-regain refloat will move it " +
                                  "while it stays open, and a re-open this scenario reuses the " +
                                  "player's last pose/size (X + escape chord remain the rescue).");
        }

        // Item 4: the user grab factor rides the SAME [MinScale, MaxScale] range the shared handle
        // clamps to — a higher local floor here would silently re-cap what the two-hand pinch shrank.
        float factor = Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;

        // REMOTE POSE EASING: advance (or pin) the DRAWN pose before anything reads it this frame.
        // Exactly ONE advance per frame, here, so the LateUpdate re-sync below cannot double the
        // rate. See the REMOTE POSE EASING block.
        AdvanceVisual(factor);

        Transform host = _panel.HostGo.transform;
        SyncHostToFrame(host, metersPerPixel, worldScale);
        // POSE-GAP INSTRUMENT (see LateSyncHost): remember what UPDATE published, so the LateUpdate
        // re-sync can say how far the window moved in between — i.e. how stale the pose is for every
        // consumer that samples it during Update.
        _updatePos = _visualPos;
        _updatePosValid = true;

        // DIAG SPAM FIX: while the host is being carried/moved, its position changes every
        // frame, so CanvasConversion's change-gated MODAL DIAG snapshot (host pos rounded to
        // cm) emitted one line PER FRAME for the whole drag (hundreds of lines in the incident
        // log). Throttle it to ~1 line/s per panel by gating the panel's Diagnostic opt-in.
        ThrottleDiagWhileMoving(host);

        // Bar/zone track the live host rect. The holder is now identity, so these frame-local
        // metres must carry worldScale themselves to reach the host's world size (the frame's
        // own localScale contributes the user grab factor). unit = world metres per host pixel.
        Rect rect = _panel.HostRect.rect;
        float unit = metersPerPixel * _extraScale * worldScale;
        float halfHeight = rect.height * unit * 0.5f;
        float width = rect.width * unit;
        // THE INK SEAM (ModBuild 236) — service the held capture BEFORE the bar is placed from it.
        // Event-driven and bounded, never a per-frame re-derivation; the whole policy is on the
        // InkSettleFrames block.
        ServiceInkCapture(rect);
        SyncBar(halfHeight, width, worldScale, rect, unit);
    }

    /// <summary>
    /// Copy the DRAWN pose/scale onto the game-owned host (shared by the Update-time
    /// <see cref="Tick"/> and the LateUpdate re-sync in <see cref="HostLateSync"/>).
    ///
    /// <para>The drawn pose is the grab frame's for every window that is not gliding — see
    /// <see cref="AdvanceVisual"/>, which is what makes <c>_visual*</c> and the frame identical in
    /// every other case, so this method's output is unchanged for every private window.</para>
    /// </summary>
    private void SyncHostToFrame(Transform host, float metersPerPixel, float worldScale)
    {
        host.SetPositionAndRotation(_visualPos, _visualRot);
        host.localScale = Vector3.one * (metersPerPixel * worldScale * _extraScale * _visualScale);
    }

    /// <summary>
    /// DRAG-FLICKER FIX (tabs blink / submenu pane vanishes ONLY while moving the window):
    /// re-sync the host from the frame in LateUpdate, AFTER every Update-time frame writer ran.
    ///
    /// Root cause: <see cref="Tick"/> (the frame→host copy) runs from
    /// <c>ModalFallback.Tick</c> inside the WorldUI module's <c>Update</c>, while
    /// <see cref="PanelGrabHandle"/> moves the FRAME from its OWN MonoBehaviour
    /// <c>Update</c> — the relative script order is undefined. Whenever the handle's Update
    /// runs after the module's, the frame (and every rigid CHILD of it: the DEPTH MASK, the
    /// bar) renders at the NEW pose while the host — synced earlier from the STALE pose —
    /// renders one frame behind. A fast drag moves the frame 5–30 cm per frame (hardware
    /// log), dwarfing the mask's 2 mm behind-plane offset: dragging toward the viewer puts
    /// the mask plane IN FRONT of the (lagging) menu content, the menu fails its own
    /// ZTest-LEqual under every mask quad, and the content blinks out — exactly the
    /// "tabs flicker / pane briefly vanishes while moving" report. Re-copying the pose here
    /// in LateUpdate (after ALL Updates, before rendering) makes host, mask and bar agree
    /// at render time every frame, regardless of script order; a static (ungrabbed) frame
    /// makes it a change-free no-op write.
    /// </summary>
    internal void LateSyncHost()
    {
        if (_panel == null || !_panel.IsAlive || _panel.HostGo == null || _frame == null)
            return;
        float factor = Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        Transform host = _panel.HostGo.transform;
        // THE RE-PIN, and NOT a second easing step. The whole reason this method exists is that
        // PanelGrabHandle moves the FRAME from its own Update in undefined order against ours, so
        // the drawn pose has to be re-derived after every Update ran. For a window that is not
        // gliding that means "copy the frame" — byte-for-byte what this method did before the
        // easing existed. For one that IS gliding the drawn pose was already advanced in Tick and
        // must be left exactly as it is: advancing it again here would run the glide at twice the
        // intended rate and make it frame-order dependent, which is the very class of bug this
        // method was written to close.
        if (!_easing || !_visualValid)
            PinVisualToFrame(factor);

        // POSE-GAP INSTRUMENT (ModBuild 199, CORRECTED in ModBuild 200). The re-sync below exists
        // because the frame can move AFTER the module's Update. This measures how much it actually
        // does, in the window's OWN AUTHORED PIXELS — which is the unit the complaint is in: a gap of
        // N px means every consumer that sampled the host pose during Update (PanelSupersample.Tick
        // runs at the end of CanvasConversion.Tick, in Update) is working from a pose N authored
        // pixels behind what the eye will be shown this frame.
        //
        // WHAT ModBuild 199 SHIPPED WAS ZERO BY CONSTRUCTION, AND THE LOG PROVES IT: 95 report lines,
        // 84,647 samples, mean 0.00 px, WORST 0.00 px, 0 over threshold — across a session containing
        // 225 frames the guard budget independently counted as MOVING. It compared
        // host.position against _updatePos, and _updatePos IS host.position as Tick left it: nothing
        // between Update and here writes the host, so the instrument was subtracting a value from
        // itself. A flawless measurement of the wrong stage looks exactly like proof, and this one
        // "proved" the pose gap does not exist.
        //
        // THE STALE QUANTITY IS THE FRAME, NOT THE HOST. PanelGrabHandle moves _frame from its OWN
        // MonoBehaviour Update, in undefined order against the module's Update. So the real staleness
        // is how far the FRAME has travelled since Update published the host from it — i.e. the pose
        // the host is about to be given on the next line, minus the pose it has been carrying all
        // frame. Still windows still read 0 (the frame did not move); only a drag can move the
        // needle, which is exactly the interval under suspicion. Two floats and a subtract.
        //
        // ModBuild 226: the subject is the DRAWN pose on both ends of the subtraction, not the frame.
        // The quantity the instrument names is "how far the window moved between Update publishing
        // it and the eye being shown it", and since the easing landed that is the drawn pose by
        // definition — the frame can now be ahead of the picture on purpose (a remote glide), which
        // is not staleness and must not be reported as it. For every window that is not gliding the
        // drawn pose IS the frame (PinVisualToFrame ran two lines up), so the number is unchanged.
        float unit = metersPerPixel * _spawnWorldScale * _extraScale * _visualScale;
        if (_updatePosValid && unit > 1e-9f)
        {
            float gapPx = Vector3.Distance(_visualPos, _updatePos) / unit;
            _panel.PoseGapSamples++;
            _panel.PoseGapSumPx += gapPx;
            if (gapPx > _panel.PoseGapWorstPx)
                _panel.PoseGapWorstPx = gapPx;
            if (gapPx > PoseGapThresholdPx)
                _panel.PoseGapOverOnePx++;
        }
        _updatePosValid = false;

        SyncHostToFrame(host, metersPerPixel, _spawnWorldScale);
    }

    // ---- REMOTE POSE EASING -----------------------------------------------------------------
    //
    // USER REQUEST 7a (2026-08-22, verbatim):
    //
    //   "Die Bewegungen der 'blauen' MP-Fenster, die 1:1 synchronisiert werden sollen, sollen auch
    //    die Bewegung und die Position voll übertragen (flüssig, wie bei der Position des Boards
    //    auch)!"
    //
    // WHAT THE BOARD DOES THAT THIS WINDOW DID NOT. The remote control board is smooth because of
    // TWO mechanisms, and the window had NEITHER:
    //   1. THE RECEIVER EASES. Net.RemoteControlBoard.Tick does not assign the synced pose; it
    //      Lerp/Slerps toward it every frame at k = 1 − exp(−NetProtocol.InterpolationSharpness·dt)
    //      and snaps only on the first apply (_poseInit), so a fresh board never flies in from the
    //      origin. Its scale rides the SAME k — defect (e) of that round was exactly a board that
    //      "glides while it moves and stutters while it zooms", because the scale was assigned while
    //      the pose was eased.
    //   2. THE SENDER RAISES ITS CADENCE WHILE THE THING MOVES. NetAvatarDriver.TickExtrasSend's
    //      boardMoving/poseDue pair puts the whole extras packet on the rig rate (15 Hz) for as long
    //      as the board's pose keeps changing, and back on 5 Hz the moment it settles.
    // The shared window ASSIGNED its pose (this class's PlaceFrameAt, called straight from
    // Net.RemoteMapStory / Net.RemoteStorySync) at whatever rate the record arrived. Half of the fix
    // is here; the sender half is in NetAvatarDriver, keyed on SharedWindows.AnyGrabbedHere().
    //
    // WHY THE DRAWN POSE AND NOT THE FRAME — this is the load-bearing decision of the whole block.
    // The obvious implementation is to ease the grab FRAME toward the received pose. It would have
    // broken the sync outright, for two independent reasons, both in a file this lane does not own:
    //   * Net.RemoteMapStory.TrackFrame decides "a hand here moved this window" by watching the grab
    //     frame drift away from a baseline it records when it applies a remote pose — and the
    //     baseline it records is the FINAL pose. An eased frame is not at that pose for the next
    //     ~200 ms, so every remote apply would have been read back as a LOCAL user move: it sets
    //     local.Moving, and ResolvePose's first line is `if (local.Moving) return;`. The client
    //     would apply one pose and then refuse every following one for the rest of the drag.
    //   * When the (self-inflicted) move "settled", the same path bumps the pose stamp and this
    //     client would become the room's LAST MOVER — publishing a pose nobody made, which the
    //     original dragger then follows. A stamp war built out of an animation.
    // Easing the drawn pose leaves the frame exactly where the sender's arithmetic expects it: the
    // frame is the AUTHORITY (it is what TryReadFrame samples, what PanelPoseWatch locks, what the
    // grab handle carries) and _visual is the PICTURE. The bar hangs under _visual for the same
    // reason — a bar that stepped while its window glided would be worse than either alone.
    //
    // WHAT THE SPLIT COSTS, stated so the next reader does not have to find it: the palm grab ZONE
    // sits on the frame, so while a glide is in flight the near-grab volume is up to the glide error
    // ahead of the visible bar (tens of milliseconds, centimetres at most, and only on a window a
    // remote player is dragging out from under you). The LASER bar collider is on the bar itself and
    // therefore always agrees with the picture, which is the one that matters — you aim at what you
    // can see.
    //
    // CONVERGENCE. k = 1 − exp(−λ·dt) is the frame-rate-independent exponential: the error decays by
    // a factor e every 1/λ = 66 ms whatever the frame rate, monotonically, and never overshoots
    // (k ∈ (0,1) ⇒ the result is strictly between the current pose and the target). Against a
    // CONTINUOUS stream it does not fall behind without bound either — the steady-state lag of a
    // first-order filter tracking a constant velocity v is v/λ, i.e. 3 cm at a brisk 0.5 m/s — and
    // the instant the drag stops that residue decays to nothing. It also TERMINATES rather than
    // crawling: below one authored window pixel of position error (the smallest gap that can move a
    // rendered texel — the same unit and the same threshold the pose-gap instrument above reports
    // in) plus a tenth of a degree, the glide is ended and the drawn pose pinned to the frame
    // outright. And it never runs at all for the cases the requirement calls out — opened, resized
    // or re-seated all pin instead (see PlaceFrameAt, SetExtraScale, SnapFrameTo and Build).
    //
    // IT DOES NOT FIGHT PanelPoseWatch. The lock's subject is the grab frame, which this never
    // touches; the frame is written once, exactly, by the same external writer as before, and that
    // writer is already announced (ModalFallback's placements announce Writer.Placement, the two net
    // appliers are attributed Writer.Peer through ModalFallback.9.Spawn's peerOwned predicate).
    //
    // REJECTED, and why:
    //   * EASE THE FRAME. The two-paragraph reason above. This is the trap.
    //   * A DISTANCE THRESHOLD to tell "a remote drag step" from "a re-seat" ("snap if it jumped
    //     more than X"). Rejected: with today's sender a whole drag arrives as ONE large jump (see
    //     the note in PlaceFrameAt), so a threshold would snap precisely the case that must glide.
    //     The announce token is an exact answer where the threshold was a guess.
    //   * RAISING ExtrasSendRateHz for everybody. Rejected — bandwidth is a shared budget and the
    //     board already showed the right shape: raise the cadence only while something is moving.
    //   * AN INTERPOLATION BUFFER (hold the last two samples and play them back one interval late).
    //     Rejected for the reason the board rejected it: it buys exactness at the price of a fixed
    //     added latency on a pose a human is dragging, and the project already has one accepted
    //     answer to this exact question.

    /// <summary>
    /// Advance the DRAWN pose one frame — or pin it to the grab frame, which is what happens for
    /// every window that is not gliding and therefore in every session without multiplayer.
    /// </summary>
    private void AdvanceVisual(float targetFactor)
    {
        if (_frame == null)
            return;

        // A HAND ON THE BAR IS ALWAYS 1:1. A carry that lagged the palm would feel like rubber, and
        // the sender must publish exactly the pose the dragging player is looking at.
        if (IsGrabbed)
            _easing = false;

        if (!_easing || !_visualValid)
        {
            PinVisualToFrame(targetFactor);
            return;
        }

        // Unscaled: a floated window must keep gliding while the game's own clock is stopped (a
        // halted ActionProcessor, a pause), and none of this is game state.
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
        float k = 1f - Mathf.Exp(-Net.NetProtocol.InterpolationSharpness * dt);
        _visualPos = Vector3.Lerp(_visualPos, _frame.position, k);
        _visualRot = Quaternion.Slerp(_visualRot, _frame.rotation, k);
        // SCALE RIDES THE SAME k — the remote board's defect (e) ("das Bewegen ist jetzt flüssig,
        // aber das Skalieren/Zoomen des Bretts nicht") was exactly an eased pose beside an assigned
        // scale. A remote resize arrives in the same record as the pose; move and zoom are one
        // motion here too.
        _visualScale = Mathf.Lerp(_visualScale, targetFactor, k);

        // TERMINATION — one authored window pixel and a tenth of a degree. Below that there is
        // nothing left for the eye, so the glide ENDS instead of crawling toward a limit it never
        // reaches. metersPerPixel × the panel's own scale chain is the world size of one authored
        // pixel, i.e. the same unit the pose-gap instrument reports in.
        float unit = WorldUIConfig.CanvasScaleMm.Value * 0.001f
                     * _spawnWorldScale * _extraScale * Mathf.Max(_visualScale, 1e-4f);
        bool arrived = Vector3.Distance(_visualPos, _frame.position) <= unit * PoseGapThresholdPx
                       && Quaternion.Angle(_visualRot, _frame.rotation) <= ArrivedDegrees
                       && Mathf.Abs(_visualScale - targetFactor) <= ArrivedScale;
        if (arrived)
        {
            PinVisualToFrame(targetFactor);
            return;
        }
        WriteVisual();
    }

    /// <summary>A residual rotation error below this cannot be seen at reading distance — matches
    /// <see cref="PanelPoseWatch.TurnEpsilonDeg"/>'s order of magnitude, deliberately tighter so the
    /// glide can never end ON the lock's own noise floor.</summary>
    private const float ArrivedDegrees = 0.1f;

    /// <summary>A residual size-factor error below this is under the wire's own quantization step
    /// (<c>NetProtocol.EncodeStorySize</c>), so it cannot describe a size any peer actually sent.</summary>
    private const float ArrivedScale = 0.002f;

    /// <summary>Drawn pose := grab frame, glide over. The state every private window is in on every
    /// frame of its life.</summary>
    private void PinVisualToFrame(float factor)
    {
        if (_frame == null)
            return;
        _visualPos = _frame.position;
        _visualRot = _frame.rotation;
        _visualScale = factor;
        _visualValid = true;
        _easing = false;
        WriteVisual();
    }

    /// <summary>Push the drawn pose onto the transform the bar hangs under. The holder is at
    /// identity pose and identity scale (the deadlock fix), so world and local agree here.</summary>
    private void WriteVisual()
    {
        if (_visual == null)
            return;
        _visual.SetPositionAndRotation(_visualPos, _visualRot);
        _visual.localScale = Vector3.one * _visualScale;
    }

    // REMOTE POSE EASING state. _visualValid false = "nothing drawn yet", which pins on the next
    // tick; _easing is armed ONLY by PlaceFrameAt, and only for a revealed shared window whose write
    // no local placement announced.
    private bool _visualValid;
    private bool _easing;
    private Vector3 _visualPos;
    private Quaternion _visualRot = Quaternion.identity;
    private float _visualScale = 1f;

    /// <summary>One authored window pixel — the smallest gap that can move a rendered texel.</summary>
    internal const float PoseGapThresholdPx = 1f;

    // POSE-GAP INSTRUMENT state: the host position Update published this frame.
    private Vector3 _updatePos;
    private bool _updatePosValid;

    /// <summary>
    /// Mod-owned holder component whose ONLY job is the LateUpdate host re-sync (see
    /// <see cref="LateSyncHost"/>). Lives on the holder GameObject, so it is destroyed with
    /// it in <see cref="GrabbableModal.Destroy"/> — no explicit lifecycle management.
    /// </summary>
    private sealed class HostLateSync : MonoBehaviour
    {
        internal GrabbableModal? Owner;

        private void LateUpdate() => Owner?.LateSyncHost();
    }

    /// <summary>
    /// DIAG SPAM FIX: gate <c>ConvertedPanel.Diagnostic</c> so the change-gated MODAL DIAG
    /// snapshot fires at most ~1/s per panel WHILE the host pose is actually changing (a
    /// carry/laser-drag/recall); a static host keeps Diagnostic permanently ON, so every
    /// state CHANGE (open/close/adopt, canvas/order flips, the settle line after a drag
    /// ends) still logs immediately and unthrottled.
    ///
    /// <para><b>ModBuild 199 — THE THROTTLE NOW OWNS THE LOG AND NOTHING ELSE.</b> Until ModBuild 198
    /// <c>ConvertedPanel.Diagnostic</c> was named for LOG VERBOSITY but was ALSO the gate
    /// <c>CanvasConversion.4.Lifecycle.cs</c> used for three pieces of REAL PER-FRAME WORK: the
    /// every-frame <c>AdoptNestedCanvases</c> path, <c>ReassertConversionFrame</c>, and
    /// <c>ReassertAdoptedSorting</c>, whose own comment at the call site reads <i>"FLICKER FIX (modal
    /// hosts only) ... Re-assert every frame for the (few) modal hosts"</i>. Clearing the flag below
    /// therefore ran that every-frame fix at roughly 1 Hz <b>for exactly as long as the player held
    /// the window</b> — the one interval the user reports the flicker under. The two concerns are
    /// separate fields now (<c>ConvertedPanel.PerFrameGuards</c> is the work gate and nothing
    /// throttles it), so a log throttle can never again throttle a fix. This method may turn the log
    /// off as aggressively as it likes.</para>
    ///
    /// <para><b>AND THE COUPLING IS NOT THE SAME CLAIM AS THE CAUSE.</b> The guard can only have
    /// caused a visible defect if it CORRECTS something while a window moves.
    /// <c>CanvasConversion.TickGuardBudget</c> counts exactly that and prints the MOVING correction
    /// rate; the ModBuild 198 hardware log already suggests the answer is "almost never" (the
    /// overrideSorting re-clear branch fired 3 times in a whole session, all on one canvas, which
    /// then conceded permanently and never entered that branch again). Read the MODAL GUARD BUDGET
    /// line before building anything else on this.</para>
    /// </summary>
    private void ThrottleDiagWhileMoving(Transform host)
    {
        // Movement epsilon: 5 mm at diorama scale — below the snapshot's own cm rounding,
        // so anything smaller never spammed in the first place. Rotation guards a pure spin.
        float eps = 0.005f * _spawnWorldScale;
        bool moving = (host.position - _diagLastPos).sqrMagnitude > eps * eps
                      || Quaternion.Angle(host.rotation, _diagLastRot) > 0.5f;
        _diagLastPos = host.position;
        _diagLastRot = host.rotation;

        // Publish the motion state for the guard-budget instrument. This is the SAME epsilon the log
        // throttle uses, so "MOVING" in the budget line means exactly what "throttled" used to mean —
        // otherwise the instrument would be measuring a different interval than the one under
        // suspicion. HELD is narrower and is reported alongside: a window can move without a hand on
        // it (a recall, a presence-regain refloat), and only the HELD case is the user's complaint.
        _panel.GuardHostMoving = moving;
        _panel.GuardHostHeld = _handle != null && _handle.IsGrabbed;

        if (!moving)
        {
            _panel.Diagnostic = true; // static host: change-gated DIAG stays fully live
            return;
        }
        float now = Time.unscaledTime;
        if (now >= _diagNextAllowed)
        {
            _diagNextAllowed = now + DiagThrottleSeconds;
            _panel.Diagnostic = true; // one snapshot line for this second of movement
        }
        else
        {
            // LOG ONLY. ConvertedPanel.PerFrameGuards stays true through this write (Diagnostic's
            // setter latches the work gate on and never off), so the per-frame modal maintenance
            // keeps running at full rate for the whole drag.
            _panel.Diagnostic = false; // swallow the per-frame pos-churn lines
        }
    }

    // ---- SHARED-WINDOW BAR COLOUR -----------------------------------------------------------
    //
    // USER REQUEST (2026-08-22, verbatim):
    //
    //   "3) Die Fenster die für alle Spieler sichtbar sind sollen eine andere Farbe beim dem
    //    Greifbalken haben (zB Blau) um anzuzeigen, dass es ein Fenster ist das alle sehen."
    //
    // WHERE THE COLOUR IS DECIDED: in <see cref="SyncSharedBarTint"/> below, and nowhere else. It
    // asks <see cref="SharedWindows"/> — which owns the DEFINITION of "shared" and its whole
    // rationale — and turns the answer into exactly one of two colours. This class contributes no
    // policy: it does not know which windows are shared, only how to paint a bar.
    //
    // WHY IT IS RE-EVALUATED EVERY TICK RATHER THAN DECIDED AT BUILD. "Shared" is not a property of
    // the window CLASS, it is "shared FOR THIS CLIENT, RIGHT NOW" (SharedWindows' class doc), and
    // participation can FLIP WHILE THE WINDOW STANDS: the map story window and the quest popup are
    // synced only among players with the 3D world map on, MapRoomDriver reads that config live, and
    // the player may toggle it with the window open. A colour written once at Build would then be a
    // false statement for the rest of that window's life. The cost of being right is one Color
    // comparison per floated window per frame; the write itself is change-gated, so a standing
    // window costs the comparison and nothing else.
    //
    // WHAT A PRIVATE BAR COSTS: nothing at all. _barTint starts at PrivateBarColor, which is the
    // literal the bar material was constructed with, so the gate never opens for a private window —
    // no material write, no log line, no behavioural change. That is the proof that today's picture
    // is preserved for every window outside the shared set, INCLUDING the map story window and the
    // quest popup for a player with the 3D map switched off, for whom those windows really are
    // private (moving one moves nothing for anybody).
    //
    // WHAT WAS REJECTED:
    //   * ONE SHARED BLUE MATERIAL for every shared bar. Rejected — and it is the obvious trap here.
    //     WorldUIAssets.CreateFlatMaterial constructs a new Material per call, so every bar already
    //     owns its own; the hover/held highlight then writes sharedMaterial.color on exactly one
    //     bar. Hand two bars the same Material instance and a single hover would turn EVERY floated
    //     window's bar gold, and this tint would turn every bar blue.
    //   * WRITING THE MATERIAL FROM HERE. Rejected: the highlight re-derives the bar colour from
    //     PanelGrabHandle's own base field whenever it goes out, so a write from outside would be
    //     reverted to brass by the next un-highlight. The base colour is handed to the handle
    //     instead (PanelGrabHandle.SetBarBaseColor), which is the single writer of that material.
    //   * TINTING BY WINDOW CLASS ("a story box is always blue"). Rejected in SharedWindows' doc,
    //     recorded here so it is not re-litigated at the paint end either.
    //   * A CONFIG DIAL for the colour. Not asked for; the user named blue and the mod picks it.

    /// <summary>
    /// The bar's resting brass — the colour of a PRIVATE window's grab bar, unchanged since the
    /// handle was introduced and deliberately still expressed as the same literal, so a private bar
    /// is byte-identical to the one that shipped before the shared tint existed.
    /// </summary>
    private static readonly Color PrivateBarColor = new(0.62f, 0.5f, 0.28f);

    /// <summary>The bar's current RESTING colour (the highlight paints over it and falls back to
    /// it). Seeded with the colour <see cref="EnsureFrame"/> builds the material with, so the
    /// change gate below is closed for every window that is not shared.</summary>
    private Color _barTint = PrivateBarColor;

    /// <summary>Is this window SHARED for this client right now — <see cref="SharedWindows.IsShared"/>
    /// as of the last tick. False for every window in a single-player session and for every private
    /// window in a multiplayer one, so both behaviours it gates (the release re-face and the remote
    /// pose easing) are inert there. Written only by <see cref="SyncSharedBarTint"/>; see the note
    /// there for why it is cached rather than asked.</summary>
    private bool _shared;

    /// <summary>
    /// Has a REMOTE player's pose ever been applied to this window? Latched by
    /// <see cref="PlaceFrameAt"/> on the easing path and never cleared while the window floats — the
    /// peer analogue of <see cref="UserMoved"/>.
    ///
    /// <para>NOTHING IN THIS LANE READS IT YET, and that is deliberate rather than an oversight. It
    /// exists for the presence-regain refloat (<c>ModalFallback.RefloatOpenWindows</c>, a file this
    /// lane does not own), which skips a window the LOCAL player moved ("parked windows stay put")
    /// but not one a REMOTE player placed — so a doff/don currently yanks a shared window back to
    /// this player's gaze and, being a real user act, then publishes that pose to the room. The
    /// argument for the skip is identical to <see cref="UserMoved"/>'s, with "a user" widened to
    /// "any user"; the one-line condition is <c>wp.Grab.UserMoved || wp.Grab.PeerPlaced</c>.</para>
    /// </summary>
    internal bool PeerPlaced { get; private set; }

    /// <summary>
    /// Re-evaluate whether <paramref name="window"/> is shared FOR THIS CLIENT right now, and paint
    /// the grab bar accordingly. Called once per tick per floated window; see the block above.
    /// </summary>
    internal void SyncSharedBarTint(UIWindow? window)
    {
        if (_handle == null)
            return; // not built yet (or already torn down) — nothing to paint
        // This IS SharedWindows.IsShared(window), expanded only because the LOG LINE has to name the
        // kind: a hardware report saying "the quest window was brass" must be readable against a log
        // that says which kind that window was and whether this client took part in its sync.
        SharedWindowKind kind = SharedWindows.KindOf(window);
        bool shared = kind != SharedWindowKind.None && SharedWindows.ParticipatesHere(kind);
        // THE SAME ANSWER, CACHED FOR THE TWO CONSUMERS THAT CANNOT SEE THE UIWindow (2026-08-22,
        // requests 7a and 7b): the release re-face gate (OnGrabFinished) and the remote pose easing
        // (PlaceFrameAt). Both are called from paths that hold the mod-owned grab and NOT the game
        // window — the grab handle's release edge and the two net appliers — so neither can ask
        // SharedWindows itself. This is the one place per tick that knows both halves (it is called
        // from ModalFallback.Tick immediately before Tick(), with the window in hand), and the cached
        // answer is therefore at most one frame old — which is the same staleness the BAR COLOUR
        // already carries, so a window whose grab bar is blue is exactly a window that will not
        // re-face. Recomputing it per release rather than caching would need this class to store the
        // UIWindow, i.e. a second reference to a game object whose lifetime ModalFallback owns.
        _shared = shared;
        Color wanted = shared ? SharedWindows.BarTint : PrivateBarColor;
        if (wanted == _barTint)
            return;
        _barTint = wanted;
        _handle.SetBarBaseColor(wanted);
        VRLog.Info("WorldUI", $"SHARED WINDOW BAR: '{_logName}' (game window '{window?.name ?? "?"}', " +
                              $"kind {kind}) now wears the {(shared ? "SHARED BLUE" : "private brass")} " +
                              $"grab bar — {(shared
                                  ? "every player in this room sees this window's state, so moving it is a shared act"
                                  : "this window is private to this client right now (its sync is off, or it is not a shared kind)")}.");
    }

    // ---- build ------------------------------------------------------------------------------

    private void EnsureFrame()
    {
        if (_holder != null && _frame != null)
            return;

        // NOTHING IS BUILT FOR AN UNBUILT MODAL (ModBuild 226), and this guard is the belt to the
        // ModBuild-226 braces. The user, verbatim: "Als mein Mitspieler gejoint ist, kam ein LEERES
        // FENSTER auf - sowas soll per se niemals passieren." Those were grab bars with no window:
        // ModalFallback.8.Convert used to construct a GrabbableModal for every float and skip
        // Build() only for hover cards — then store the unbuilt object in wp.Grab anyway. Two
        // callers reach PlaceFrameAt on it, PlaceFrameAt calls this, and this built a holder, a
        // brass bar and a collider around `_panel == null`: never sized (Tick returns before
        // SyncBar), never ordered, never render-hidden, and never moved with the card the bar was
        // supposed to belong to. The log named all 204 of them by the field initialiser they still
        // carried — `MODAL GRAB: 'Menu'`, `_logName`'s value when Build never ran.
        //
        // The real repair is at the source (no GrabbableModal is constructed for a hover card any
        // more), so this branch is expected to be DEAD. It is here because the failure it prevents
        // is invisible: an unbuilt modal produces furniture that looks exactly like a real window's
        // and behaves like nothing at all, and there is no other place in the class that could
        // notice. Cheap, and it turns a silent absurdity into a refusal with a name.
        if (_panel == null)
        {
            VRLog.Warn("WorldUI", $"MODAL GRAB REFUSED for '{_logName}': EnsureFrame was reached on a "
                                  + "GrabbableModal whose Build() never ran, so there is no panel for "
                                  + "a frame to carry. Nothing is created. This is the empty-grab-bar "
                                  + "defect of ModBuild 225 and it should be UNREACHABLE since 226 — "
                                  + "if this line is in the log, a caller is constructing a modal it "
                                  + "does not build and then placing it.");
            return;
        }

        var holderGo = new GameObject($"GloomhavenVR.ModalGrab_{_logName}");
        _holder = holderGo.transform;
        // DRAG-FLICKER FIX: LateUpdate re-sync of the host from the frame — see LateSyncHost.
        holderGo.AddComponent<HostLateSync>().Owner = this;

        var frameGo = new GameObject("Frame");
        _frame = frameGo.transform;
        _frame.SetParent(_holder, worldPositionStays: false);

        // THE DRAWN POSE (see the REMOTE POSE EASING block). A SIBLING of the frame, not a child:
        // it must be able to lag behind it, which a child cannot. Identical to the frame in every
        // frame of every window that is not gliding, which is every window outside multiplayer.
        var visualGo = new GameObject("Visual");
        _visual = visualGo.transform;
        _visual.SetParent(_holder, worldPositionStays: false);

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        // LOST-MENU FIX: keep the primitive's BoxCollider as the LASER-only drag-bar target
        // instead of destroying it. The unit box scaled by the bar transform matches the
        // VISIBLE brass strip exactly (padded slightly via BarColliderPad); handed to the
        // shared handle as BarCollider so RayGrabDriver ray-tests ONLY this strip. It is a
        // trigger on the mod render layer, so the physics ray (RayInteractor) still ignores
        // it, and it is NOT registered with VRInteractables — the palm grab keeps using the
        // generous frame zone below (near-grab is deliberate; the laser was the problem).
        var barCollider = bar.GetComponent<BoxCollider>();
        barCollider.isTrigger = true;
        barCollider.size = new Vector3(1f, BarColliderPad, BarColliderPad);
        // Under the DRAWN pose, not the frame: the visible bar and the window it belongs to must
        // move as one object, and a glide moves the window. Its local numbers are unchanged —
        // _visual carries the same localScale (the user grab factor) the frame does, so SyncBar's
        // frame-local metres still mean what they meant.
        bar.transform.SetParent(_visual, worldPositionStays: false);
        bar.transform.localScale = new Vector3(0.2f, BarThickness, BarThickness);
        var mr = bar.GetComponent<MeshRenderer>();
        // Item 3: opaque brass that OCCLUDES the menu. The bundled GloomhavenVR/Overlay shader
        // (overlay:true) exposes _ZWrite/_ZTest; force ZWrite ON so the bar draws solid (not the
        // Sprites/Default alpha-blend that read semi-transparent), while leaving ZTest at the
        // default LEqual so a hand held physically in front still occludes the solid handle. The
        // sortingOrder below is what actually lifts it OVER the depthless menu canvas.
        Material barMat = WorldUIAssets.CreateFlatMaterial(PrivateBarColor, overlay: true);
        if (barMat.HasProperty("_ZWrite"))
            barMat.SetInt("_ZWrite", 1);
        mr.sharedMaterial = barMat;
        // Rides the panel's ladder order at a fixed offset (see BarOrderOffset). Registered after
        // the renderer exists; the order pass seats it immediately, so there is no unordered frame.
        CanvasConversion.RegisterOrderFollower(_panel, mr, BarOrderOffset);
        _bar = bar.transform;

        // Grab zone + shared grab core (collider BEFORE the handle: its OnEnable registers it).
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.isTrigger = true;
        _grabZone.size = new Vector3(0.25f, 0.05f, 0.05f);
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        _handle.Init(this, mr, "WorldUI", $"{_logName} menu");
        // LOST-MENU FIX: split laser vs palm — the far ray grabs ONLY the visible bar strip.
        _handle.SetBarCollider(barCollider);

        // Render-only mod layer — grabs/pokes route through the registries, not layers.
        VRLayers.Apply(holderGo);

        // User ruling 2026-08-02 round 2 ("ein Aufploppen der Greifbar ... woanders"): this holder
        // is a SCENE-ROOT tree, not a child of the host — the host follows the frame, not the other
        // way round — so the reveal gate's host-canvas hide could never touch the bar's MeshRenderer
        // or the modal depth mask. Register it as an extra render root: the panel's render hide now
        // walks this tree too, and because the panel is already render-hidden when Build runs, the
        // registration hides the bar in the very frame it was created (it is built at the PRE-FIT
        // rect/scale, which is exactly the wrong place the user saw it pop in at).
        CanvasConversion.AddRenderRoot(_panel, _holder);
        // ModBuild 230: the holder exists from here, so from here it is sweepable (see LiveHolders).
        if (!LiveHolders.Contains(this))
            LiveHolders.Add(this);
        VRLog.Info("WorldUI", $"MODAL GRAB: '{_logName}' is now a grabbable/scalable world element " +
                              "(grip the bar to move, two hands to resize 0.5x-2x).");
    }

    private void SyncBar(float halfHeight, float width, float worldScale, Rect hostRect, float unit)
    {
        if (_bar == null || _grabZone == null)
            return;
        // All dims are frame-local metres. With the holder now at identity scale (deadlock
        // fix) the fixed constants must carry worldScale themselves so the bar/zone keep the
        // same WORLD size relative to the (worldScale-sized) panel as before.
        //
        // Empty-gold-plate fix: proportion the bar to SHORT panels. halfHeight arrives in
        // frame-local metres (worldScale included), so divide it back out for the real panel
        // height; a panel shorter than the full-size reference slims the bar thickness AND
        // pulls it closer (smaller gap) by the same factor, floored at MinBarProportion so
        // the grab/laser target never vanishes. Board-scale menus land at proportion 1 —
        // numerically identical to the previous fixed constants. The proportion is deliberately
        // still taken from the HOST RECT and not from the ink: it is a comfort rule about how big
        // the physical handle may be next to its window, and the window is the frame.
        float panelHeight = halfHeight * 2f / Mathf.Max(worldScale, 1e-4f);
        float proportion = Mathf.Clamp(panelHeight / BarFullSizePanelHeightMeters,
            MinBarProportion, 1f);
        float gap = BarGapMeters * proportion * worldScale;
        float thickness = BarThickness * proportion * worldScale;
        float minWidth = MinBarWidth * worldScale;
        float zoneDepth = 0.05f * worldScale;

        // THE FRAME-BASED PLACEMENT — byte-for-byte what shipped through ModBuild 235, and still the
        // answer whenever the ink cannot be measured (see the degenerate branch below).
        float frameBarWidth = Mathf.Max(width * BarWidthFraction, minWidth);
        float frameZoneWidth = Mathf.Max(width * ZoneWidthFraction, minWidth);
        float x = 0f;
        float y = -(halfHeight + gap);
        float barWidth = frameBarWidth;
        float zoneWidth = frameZoneWidth;

        if (_inkValid && unit > 1e-9f)
        {
            // BELOW THE LOWEST DRAWN GRAPHIC. hostRect.yMin x unit is exactly -halfHeight for a
            // pivot-centred host, so a window whose ink stays inside its frame is unchanged; the Min
            // is what keeps the bar from ever RISING into the frame when the ink is short.
            y = Mathf.Min(hostRect.yMin, _inkRect.yMin) * unit - gap;
            // CENTRED ON THE INK. For a window whose ink fills its frame this is 0 and nothing moved.
            x = _inkRect.center.x * unit;
            // A FRACTION OF WHAT IS DRAWN, floored at MinBarWidth so the grab/laser target survives
            // and capped at the frame-based width so the bar can only ever get NARROWER than the one
            // the user has already accepted on every other window.
            barWidth = Mathf.Clamp(_inkRect.width * unit * BarWidthFraction, minWidth, frameBarWidth);
            zoneWidth = Mathf.Clamp(_inkRect.width * unit * ZoneWidthFraction, minWidth, frameZoneWidth);
        }

        _bar.localPosition = new Vector3(x, y, 0f);
        _bar.localScale = new Vector3(barWidth, thickness, thickness);
        // The palm grab zone rides WITH the visible handle, as it always has — it is not the hit rect
        // (that contract, "always contains the host rect", belongs to the conversion and is untouched).
        _grabZone.center = new Vector3(x, y, 0f);
        _grabZone.size = new Vector3(zoneWidth, zoneDepth, zoneDepth);

        // THE CLOSE X RIDES THE SAME UNION AS THE BAR, on the same tick, from the same committed
        // rectangle — so the two pieces of chrome can never disagree about where the window is.
        SyncCloseX(hostRect);

        if (_inkReportDue || _inkFallbackDue)
        {
            // The falsifier is handed the two derived numbers rather than the terms to re-derive them
            // from, so a report can never disagree with the placement it is describing. The intended
            // gap is to the bar's TOP EDGE: BarGapMeters is documented as the gap to the bar's CENTRE,
            // and half the thickness of the bar lies above that centre.
            ReportBarPlacement(hostRect, unit,
                intendedTopGapPx: (gap - thickness * 0.5f) / Mathf.Max(unit, 1e-9f),
                mmPerPx: unit / Mathf.Max(worldScale, 1e-4f) * 1000f);
        }
    }

    /// <summary>
    /// Keep the mod's close X seated against the committed ink (see the CLOSE X block). Never
    /// throws and never writes game state: the plate is mod-owned chrome on the mod's own host, and
    /// the only writes are its anchors and its anchored position.
    /// </summary>
    private void SyncCloseX(Rect hostRect)
    {
        if (_panel == null || !_panel.IsAlive || _panel.HostRect == null)
            return;
        if (_closeX == null)
        {
            int now = Time.frameCount;
            if (now < _closeXNextProbeFrame)
                return;
            _closeXNextProbeFrame = now + CloseXProbeStrideFrames;
            _closeX = ModalCloseButton.FindPlate(_panel);
            if (_closeX == null)
            {
                _closeXPlaced = false;
                return;
            }
        }
        _closeXPlacement = ModalCloseButton.PlaceAgainstInk(_closeX, hostRect, _inkValid, _inkRect);
        _closeXPlaced = true;
    }

    // ---- the ink capture ------------------------------------------------------------------------

    /// <summary>
    /// Keep the held ink union current. The policy — what counts as an event, why the union is
    /// monotone outward inside a generation, and why it is NOT monotone across the window's life — is
    /// written out in full on the <see cref="InkSettleFrames"/> block; this method is only its
    /// mechanism. Never throws: a throw here would stand down the window's follow tick, and the
    /// consequence of a missed capture is merely that the bar keeps the placement it already had.
    /// </summary>
    private void ServiceInkCapture(Rect hostRect)
    {
        if (_panel == null || !_panel.IsAlive)
            return;

        int sig;
        int sigTransient;
        try
        {
            sig = PanelInkBounds.ActiveSetSignature(_panel, out sigTransient);
        }
        catch (System.Exception)
        {
            return;
        }
        // COUNT EPISODES, NOT FRAMES. This runs every tick, so summing the per-tick count would
        // report a held hover once per frame and the number would say nothing about how often the
        // generation WOULD have been reset. A rising edge is one hover EPISODE, and each episode is
        // two avoided resets — the mouse-in that raised the widget and the mouse-out that took it
        // away were both a change in the set of direct children before this round.
        if (sigTransient > 0 && _inkSigTransientChildren == 0)
            _inkSigTransientLife++;
        _inkSigTransientChildren = sigTransient;

        bool sigChanged = !_inkSignatureValid || sig != _inkSignature;
        bool frameChanged = !_inkHostRectValid
                            || Mathf.Abs(hostRect.xMin - _inkHostRect.xMin) > 0.5f
                            || Mathf.Abs(hostRect.xMax - _inkHostRect.xMax) > 0.5f
                            || Mathf.Abs(hostRect.yMin - _inkHostRect.yMin) > 0.5f
                            || Mathf.Abs(hostRect.yMax - _inkHostRect.yMax) > 0.5f;
        _inkSignature = sig;
        _inkSignatureValid = true;
        _inkHostRect = hostRect;
        _inkHostRectValid = true;

        int now = Time.frameCount;
        if (sigChanged || frameChanged)
        {
            // A NEW GENERATION. The envelope restarts — the next sample REPLACES the union instead of
            // growing it, which is how a switch from a tall view to a short one is allowed to bring
            // the bar back up. What is NOT reset is the COMMITTED rectangle: the bar keeps the place
            // it already has until that first new sample lands, so a tab change costs it one move and
            // not two (and never a flash back to the frame-based placement in between).
            _inkGeneration++;
            _inkSamples = 0;
            _inkGenSeeded = false;
            _inkPendingValid = false;
            _inkReleaseValid = false;
            _inkReleaseRun = 0;
            _inkFallbackReported = false;
            _inkNextSampleFrame = now + InkSettleStrideFrames;
            _inkSettleUntilFrame = now + InkSettleFrames;
            _inkCause = frameChanged && sigChanged
                ? "the host rect resized AND the set of open sub-views changed"
                : frameChanged ? "the host rect resized" : "the set of open sub-views changed";
        }
        else if (_inkNextSampleFrame < 0)
        {
            _inkNextSampleFrame = now;
            _inkSettleUntilFrame = now + InkSettleFrames;
        }

        if (now < _inkNextSampleFrame)
            return;
        bool settling = now <= _inkSettleUntilFrame;
        _inkNextSampleFrame = now + (settling ? InkSettleStrideFrames : InkVerifyStrideFrames);

        bool measured = PanelInkBounds.TryMeasure(_panel, out PanelInkBounds.Ink ink) && ink.Valid;
        // THE MOUSEOVER LEDGER IS TAKEN ON EVERY SAMPLE, including one that could not be measured —
        // "every drawn graphic in this window turned out to be a hover widget" is precisely the
        // failure the exclusion could cause, and it must be readable on the line that reports it.
        _inkTransient = ink.Transient;
        _inkTransientLife += ink.Transient;
        _inkTransientMask |= ink.TransientMask;
        if (!measured)
        {
            // DEGENERATE: zero drawn graphics, or zero size. Keep whatever placement is already
            // committed (frame-based when nothing was ever captured) and SAY SO — a silent fall back
            // to the very geometry this round replaced is the one outcome nobody could diagnose.
            if (!_inkValid && !_inkFallbackReported)
            {
                _inkFallbackReported = true;
                _inkFallbackDue = true;
            }
            return;
        }

        _inkSamples++;
        Rect grown = ink.Rect;
        if (_inkValid && _inkGenSeeded)
        {
            // MONOTONE OUTWARD inside the generation — down, left and right only.
            grown = Rect.MinMaxRect(Mathf.Min(_inkRect.xMin, ink.Rect.xMin),
                                    Mathf.Min(_inkRect.yMin, ink.Rect.yMin),
                                    Mathf.Max(_inkRect.xMax, ink.Rect.xMax),
                                    Mathf.Max(_inkRect.yMax, ink.Rect.yMax));
        }

        // ---- THE RELEASE SIDE. Whole policy on the InkSettleFrames block; this is its mechanism.
        bool released = false;
        if (settling || !_inkValid || !_inkGenSeeded || !SameRect(grown, _inkRect))
        {
            // Inside the burst, before the first commit, or on a sample that GREW the envelope: a
            // recession is not even a question, and any run in progress is abandoned. Growth wins.
            _inkReleaseValid = false;
            _inkReleaseRun = 0;
        }
        else if (!Receded(_inkRect, ink.Rect))
        {
            // The raw sample agrees with the held envelope to within the dead band — nothing to give
            // back, and a run that was building is broken by this disagreement with itself.
            _inkReleaseValid = false;
            _inkReleaseRun = 0;
        }
        else if (_inkReleaseValid && NearRect(_inkReleaseCandidate, ink.Rect, InkReleaseStabilityPx))
        {
            // THE SAME RECESSION AGAIN. Carry the OUTERMOST rect of the run forward, so a wobbling
            // run can only ever commit the most conservative member of itself.
            _inkReleaseCandidate = Rect.MinMaxRect(
                Mathf.Min(_inkReleaseCandidate.xMin, ink.Rect.xMin),
                Mathf.Min(_inkReleaseCandidate.yMin, ink.Rect.yMin),
                Mathf.Max(_inkReleaseCandidate.xMax, ink.Rect.xMax),
                Mathf.Max(_inkReleaseCandidate.yMax, ink.Rect.yMax));
            _inkReleaseRun++;
            if (_inkReleaseRun >= InkReleaseConsecutive)
            {
                grown = _inkReleaseCandidate;
                released = true;
            }
        }
        else
        {
            _inkReleaseCandidate = ink.Rect;
            _inkReleaseValid = true;
            _inkReleaseRun = 1;
        }

        bool moved = !_inkValid || !SameRect(grown, _inkRect);
        // The census fields always describe the LATEST sample; only the rectangle is the envelope.
        _inkGraphics = ink.Graphics;
        _inkPlates = ink.Plates;
        _inkEmptyText = ink.EmptyText;
        _inkModChrome = ink.ModChrome;
        _inkModChromeMask = ink.ModChromeMask;
        _inkTruncated = ink.Truncated;
        _inkFaint = ink.Faint;
        // Name the graphic that sets the COMMITTED envelope's bottom, which is only this sample's
        // bottom-setter when this sample is the one that owns that edge. A release always renames:
        // its rect IS this run's measurements, within InkReleaseStabilityPx of this one.
        if (_inkBottomName.Length == 0 || released || Mathf.Abs(grown.yMin - ink.Rect.yMin) <= 0.5f)
            _inkBottomName = ink.BottomName;
        if (!moved)
        {
            _inkPendingValid = false;
            return;
        }

        // THE REPEAT GATE — a growth found OUTSIDE the settle burst must be seen twice before it is
        // committed. The hole it closes is a TRANSIENT: the game re-parents hover tooltips onto the
        // window itself (TooltipOnWindow's `SetParent(target, …)`), and a monotone envelope would take
        // one such sighting and hold the bar away from the window until the next sub-view change. Two
        // consecutive verify samples an InkVerifyStrideFrames apart is not a hover. Inside the settle
        // burst there is no gate at all: a view that is still arriving must be followed immediately,
        // and the burst is the one interval in which every reading is expected to differ.
        // The FIRST commit of a window's life is exempt: there is no held placement to protect, and
        // the only alternative is the frame-based geometry this round exists to replace.
        // A RELEASE IS EXEMPT: this gate exists to make a GROWTH prove itself twice, and a release has
        // already proven itself InkReleaseConsecutive times against a stricter test.
        if (!settling && _inkValid && !released)
        {
            if (!_inkPendingValid || !SameRect(_inkPending, grown))
            {
                _inkPending = grown;
                _inkPendingValid = true;
                _inkGrowthsDeferred++;
                return;
            }
        }
        _inkPendingValid = false;

        if (released)
        {
            // RE-ARM THE SETTLE BURST. The bar has just moved UP; content that comes back must be able
            // to push it down again immediately rather than through the 1 s repeat gate, or the window
            // between a release and a re-growth is exactly the quest_überlap.jpg overlap with a delay.
            _inkReleases++;
            _inkReleaseValid = false;
            _inkReleaseRun = 0;
            _inkCause = "the ink receded and held for " + InkReleaseConsecutive + " verify sample(s)";
            _inkSettleUntilFrame = now + InkSettleFrames;
            _inkNextSampleFrame = now + InkSettleStrideFrames;
        }

        _inkRect = grown;
        _inkValid = true;
        _inkGenSeeded = true;
        _inkFallbackDue = false;
        _inkFallbackReported = false;
        _inkReportDue = true;
        _inkHeldFrames = _inkCommitFrame < 0 ? 0 : now - _inkCommitFrame;
        _inkCommitFrame = now;
    }

    /// <summary>
    /// THE FALSIFIER, read back off the transform that was just written — not off the intent that
    /// produced it. One greppable line per window per (re-)capture, rate-limited.
    ///
    /// <para><b>ModBuild 239 ADDED THE GAP ITSELF, and made a gap far above intent a NAMED FAILURE.</b>
    /// Through ModBuild 238 this line could read CONFIRMED on the very window the user was
    /// photographing (<c>grosser_abstand.jpg</c>): it asserted only that the bar CLEARS the ink and is
    /// CENTRED on it, and both were true of an ink union that was itself wrong by 373 px. Two terms
    /// now carry the distance, in the window's authored px and in millimetres at the live rig scale,
    /// against the intended <see cref="BarGapMeters"/>:</para>
    /// <list type="bullet">
    /// <item><b>TO THE INK</b> — lowest drawn graphic to the bar's top edge. Legitimately larger than
    /// intent whenever the ink stops ABOVE the frame's bottom, because the bar is never raised into
    /// the frame; that is why it is reported and not judged.</item>
    /// <item><b>BELOW THE FRAME</b> — the host rect's own bottom edge to the bar's top edge. This is
    /// the user's complaint expressed as one number, and it is the term that is judged. It is
    /// independent of whether the ink measurement is right, which is precisely the property the
    /// ModBuild 238 verdict lacked ([[a-claim-must-not-measure-itself]]).</item>
    /// </list>
    /// <para>It fires whenever the handle hangs more than one dead band below its own window — which
    /// includes the case where it is CORRECT to (the battle-goal picker really does draw 373 px below
    /// the frame while it is open, and cutting across it is quest_überlap.jpg). That is deliberate: the
    /// line names the graphic holding the bar down and the count of drawn-but-invisible graphics that
    /// were excluded, so one reading adjudicates it. Silence on this cost another build.</para>
    /// </summary>
    /// <summary>
    /// THE MOUSEOVER LEDGER (ModBuild 241) — deliberately worded like the one
    /// <c>CanvasConversion.3.Fit.cs</c> prints, because the two now answer the same question from the
    /// same table (<see cref="TransientFamilies"/>) and a future round must be able to lay the two
    /// lines side by side. That comparison is exactly what settled ModBuild 239: the fit's ledger read
    /// "320 over this window's life" for <c>New Party display</c> while this instrument had never
    /// heard of the concept and was moving the handle by up to 33 px on an item hint.
    ///
    /// <para>Both halves of the defect are on the line. REFUSED graphics are the ones that would have
    /// been unioned and no longer are; HOVER EPISODES are the ones that would have reset the
    /// generation (and with it the monotone envelope and any release run) because
    /// <c>TooltipOnWindow</c> re-parents the widget onto the window root for the duration of a hover.
    /// A session in which the handle still twitches and BOTH numbers are zero means the widget is not
    /// one of the named families — the fix then is to add it to that table by TYPE, never to widen the
    /// test into a property test, which would also catch the personal-quest rows the user explicitly
    /// ruled must keep moving the bar.</para>
    /// </summary>
    private string MouseoverLedger() =>
        $"MOUSEOVER LEDGER: {_inkTransient} transient graphic(s) refused from the union this sample, "
        + $"{_inkTransientLife} over this window's life, from "
        + (_inkTransientMask != 0
            ? TransientFamilies.Describe(_inkTransientMask)
            : "no hover/tooltip family (none has been seen inside this window yet)")
        + $"; {_inkSigTransientChildren} raised mouseover(s) hidden from the generation signature right "
        + $"now and {_inkSigTransientLife} hover episode(s) hidden over this window's life (each of "
        + "which would otherwise have reset the ink envelope twice, once on the mouse-in that raises "
        + "the widget onto the window root and once on the mouse-out that takes it away). Transient "
        + "content is still DRAWN, and the hit rect and the capture frame still grow to cover it — it "
        + "is only barred from deciding where the brass handle goes";

    /// <summary>
    /// <b>THE ITEM-3 FALSIFIER: the frame, the ink, where the X landed, and how much of the laser's
    /// own target rectangle is over nothing.</b> One greppable line per window per accepted report,
    /// for EVERY window and not only the one that was photographed — item 3 is a general rule, and
    /// item 2 is about to make the options window stop being its example. A round that only knew the
    /// options window would read "fixed" off a window that no longer has the defect.
    ///
    /// <para><b>THE INTERACTIVE EXTENT IS NOT A COLLIDER, AND THIS LINE HAS TO SAY SO</b>, because
    /// the report says "unsichtbare Collider für den Laser" and looking for a collider finds two
    /// innocent ones. The laser's verdict on a converted window is a pure PLANE TEST:
    /// <c>RayUguiDriver.TryIntersect</c> takes the HOST CANVAS RectTransform's four world corners of
    /// <c>CanvasConversion.TryGetHitRect</c> (falling back to <c>rect.rect</c>) and wins the pick on
    /// that rectangle alone, BEFORE any graphic is raycast — so a beam crossing empty transparent
    /// frame ends there, draws its reticle there, and shadows everything behind it. Neither
    /// <see cref="_grabZone"/> (the palm zone, which already rides the bar and therefore the ink) nor
    /// the close plate's <c>HitPlane</c> (34x34 px, and it now rides the ink too) is that surface.
    /// The number below is the part of that rectangle that lies outside what the window draws.</para>
    ///
    /// <para>Narrowing the hit rect itself is NOT in this class and not in this lane: it is
    /// <c>CanvasConversion.3.Fit.cs</c>'s <c>HitRects</c> commit, whose stated contract is
    /// <c>Content ∪ Host</c> — "never NARROWER than the frame … the correct contract for a ray test".
    /// Changing a contract needs the lane that owns it; the proposed patch is written out in
    /// <c>.planning/debug/laneI-out-of-lane.diff</c>. What this line does is make the cost of the
    /// current contract a number rather than an impression.</para>
    /// </summary>
    private void ReportClosePlacement(Rect hostRect, float mmPerPx)
    {
        Rect hit = hostRect;
        string hitSource = "the HOST RECT (this window has no committed hit rect, so the laser tests "
                           + "the frame itself)";
        if (_panel != null && _panel.IsAlive && _panel.HostCanvas != null
            && CanvasConversion.TryGetHitRect(_panel.HostCanvas, out Rect committed)
            && committed.width > 0f && committed.height > 0f)
        {
            hit = committed;
            hitSource = "CanvasConversion.TryGetHitRect (Content ∪ Host, the fit's own commit)";
        }

        string x = !_closeXPlaced
            ? "THE X: this window has none (the scenario-end windows are excluded by design, and a "
              + "window whose plate has not been built yet reports the same thing)"
            : $"THE X: its top-right corner is at ({_closeXPlacement.Corner.x:F0},"
              + $"{_closeXPlacement.Corner.y:F0}) px, seated against "
              + (_closeXPlacement.OnInk
                  ? "THE INK"
                  : _inkValid
                      ? "THE FRAME because the ink reaches the frame's own corner (both axes clamped) "
                        + "— which is the case that is numerically unchanged from ModBuild 241"
                      : "THE FRAME as a FALLBACK, because there is no committed ink union for this "
                        + "window; see the GRAB BAR line's 'THE INK UNION COULD NOT BE MEASURED' "
                        + "variant on this same tick for why")
              + (_closeXPlacement.ClampedRight ? ", x clamped to the frame" : string.Empty)
              + (_closeXPlacement.ClampedTop ? ", y clamped to the frame" : string.Empty);

        string dead;
        if (!_inkValid)
        {
            dead = "DEAD INTERACTIVE AREA: not computable without an ink union — every pixel of the "
                   + "hit rect is treated as live, which is the pre-ModBuild-242 behaviour";
        }
        else
        {
            float left = Mathf.Max(0f, _inkRect.xMin - hit.xMin);
            float right = Mathf.Max(0f, hit.xMax - _inkRect.xMax);
            float below = Mathf.Max(0f, _inkRect.yMin - hit.yMin);
            float above = Mathf.Max(0f, hit.yMax - _inkRect.yMax);
            float hitArea = Mathf.Max(hit.width * hit.height, 1f);
            float liveW = Mathf.Max(0f, Mathf.Min(hit.xMax, _inkRect.xMax) - Mathf.Max(hit.xMin, _inkRect.xMin));
            float liveH = Mathf.Max(0f, Mathf.Min(hit.yMax, _inkRect.yMax) - Mathf.Max(hit.yMin, _inkRect.yMin));
            float deadPct = 100f * (1f - liveW * liveH / hitArea);
            dead = $"DEAD INTERACTIVE AREA: {deadPct:F0}% of the hit rect lies outside the ink "
                   + $"— margins L {left:F0} px = {left * mmPerPx:F0} mm, R {right:F0} px = "
                   + $"{right * mmPerPx:F0} mm, D {below:F0} px = {below * mmPerPx:F0} mm, U "
                   + $"{above:F0} px = {above * mmPerPx:F0} mm. A laser crossing any of those strips "
                   + "lands on this window, ends there and shadows whatever is behind it";
        }

        VRLog.Info("WorldUI",
            $"MODAL CLOSE X ON THE INK for '{_logName}': the FRAME spans x {hostRect.xMin:F0}.."
            + $"{hostRect.xMax:F0} and y {hostRect.yMin:F0}..{hostRect.yMax:F0} "
            + $"({hostRect.width:F0}x{hostRect.height:F0} px); the INK "
            + (_inkValid
                ? $"spans x {_inkRect.xMin:F0}..{_inkRect.xMax:F0} and y {_inkRect.yMin:F0}.."
                  + $"{_inkRect.yMax:F0} ({_inkRect.width:F0}x{_inkRect.height:F0} px)"
                : "IS NOT MEASURED")
            + $"; the LASER TARGET is x {hit.xMin:F0}..{hit.xMax:F0} and y {hit.yMin:F0}..{hit.yMax:F0} "
            + $"({hit.width:F0}x{hit.height:F0} px) from {hitSource}. {x}. {dead}. HOW TO READ IT. "
            + "Three rectangles that agree means a window whose picture fills its frame, and every "
            + "term of this round is a no-op on it BY CONSTRUCTION — an unchanged reading there is "
            + "evidence, not an absence of it. A LASER TARGET much wider than the INK is the user's "
            + "'unsichtbare Collider' as one number, and it is NOT a collider: it is the host canvas "
            + "plane RayUguiDriver.TryIntersect tests, whose rectangle is CanvasConversion's "
            + "Content ∪ Host commit and is contractually never narrower than the frame. An X seated "
            + "against THE FRAME while the ink is much smaller than the frame is this round's own "
            + "failure and nothing else's.");
    }

    private void ReportBarPlacement(Rect hostRect, float unit, float intendedTopGapPx, float mmPerPx)
    {
        _inkReportDue = false;
        bool fallback = _inkFallbackDue;
        _inkFallbackDue = false;
        if (_bar == null || unit <= 1e-9f)
            return;
        if (Time.realtimeSinceStartup < _inkNextReportAllowed)
        {
            _inkReportsSuppressed++;
            return;
        }
        _inkNextReportAllowed = Time.realtimeSinceStartup + InkReportThrottleSeconds;

        // ONE LINE PER WINDOW PER ACCEPTED REPORT, on the SAME throttle as the bar's, whether or not
        // the ink measured. It is emitted BEFORE the bar's own line and before any of that method's
        // three exits, so a window that cannot measure its ink still reports where its X went.
        ReportClosePlacement(hostRect, mmPerPx);

        // Back out of frame-local metres into the window's own authored px — the unit the complaint
        // is in, and the unit the capture log quotes 'Rewards' at (-255,-913)-(284,-851) in.
        Vector3 pos = _bar.localPosition;
        Vector3 scale = _bar.localScale;
        float barTopPx = (pos.y + scale.y * 0.5f) / unit;
        float barCentrePx = pos.x / unit;
        float barHalfPx = scale.x * 0.5f / unit;
        string suppressed = _inkReportsSuppressed > 0
            ? $" ({_inkReportsSuppressed} earlier line(s) suppressed by the {InkReportThrottleSeconds:F0} s rate limit)"
            : string.Empty;
        _inkReportsSuppressed = 0;
        string frame = $"the HOST RECT for comparison spans x {hostRect.xMin:F0}..{hostRect.xMax:F0} "
                       + $"and y {hostRect.yMin:F0}..{hostRect.yMax:F0}, {hostRect.width:F0}x{hostRect.height:F0} px";

        if (fallback || !_inkValid)
        {
            VRLog.Warn("WorldUI",
                $"GRAB BAR CLEARS THE INK: NOT ACHIEVED for '{_logName}' — failing term: THE INK UNION "
                + "COULD NOT BE MEASURED (zero drawn graphic(s) under the window's own root, or zero "
                + "size), so the bar keeps the frame-based placement this round exists to replace: bar "
                + $"top edge y={barTopPx:F0} px, centre x={barCentrePx:F0} px, half-width {barHalfPx:F0} px; "
                + $"{frame}; generation {_inkGeneration}, {_inkSamples} sample(s) taken, re-capture cause "
                + $"was {_inkCause}; {MouseoverLedger()} — IF THAT REFUSED COUNT IS NON-ZERO AND THE "
                + "UNIONED COUNT ON the last successful line was small, the ModBuild 241 mouseover "
                + "exemption has emptied this window's bucket and is the first thing to look at."
                + $"{suppressed}");
            return;
        }

        float inkBottom = _inkRect.yMin;
        float inkCentre = _inkRect.center.x;
        bool clearsVertically = barTopPx <= inkBottom + 0.5f;
        bool centredOnInk = Mathf.Abs(barCentrePx - inkCentre) <= 1f;

        // THE GAP, both ways. See this method's own comment for which of the two is judged and why.
        float gapToInkPx = inkBottom - barTopPx;
        float dropBelowFramePx = hostRect.yMin - barTopPx;
        bool gapWithinIntent = dropBelowFramePx <= intendedTopGapPx + InkReleaseDeadBandPx;
        string gaps =
            $"THE GAP: to the ink {gapToInkPx:F0} px = {gapToInkPx * mmPerPx:F0} mm, "
            + $"below the window's own frame {dropBelowFramePx:F0} px = {dropBelowFramePx * mmPerPx:F0} mm, "
            + $"against an INTENDED {intendedTopGapPx:F0} px = {intendedTopGapPx * mmPerPx:F0} mm "
            + $"(BarGapMeters {BarGapMeters:F3} m to the bar's centre, less half its thickness, x the "
            + $"short-panel proportion, at {mmPerPx:F3} mm per authored px on the live rig)";

        string census = $"{_inkGraphics} graphic(s) unioned, {_inkPlates} full-frame plate(s), "
                        + $"{_inkFaint} drawn-but-invisible graphic(s) (effective alpha under the fit's "
                        + $"floor), {_inkEmptyText} empty text(s) and {_inkModChrome} mod chrome object(s) "
                        + $"excluded — the chrome was {PanelInkBounds.DescribeChrome(_inkModChromeMask)}"
                        + (_inkTruncated ? ", WALK TRUNCATED at the node budget" : string.Empty)
                        + "; " + MouseoverLedger();
        string measurement =
            $"bar top edge y={barTopPx:F0} px, centre x={barCentrePx:F0} px, half-width {barHalfPx:F0} px, "
            + $"all in the window's own authored px; the LOWEST drawn graphic '{_inkBottomName}' ends at "
            + $"y={inkBottom:F0} px; the ink union spans x {_inkRect.xMin:F0}..{_inkRect.xMax:F0} "
            + $"(width {_inkRect.width:F0} px, centre {inkCentre:F0}) and y {inkBottom:F0}..{_inkRect.yMax:F0}; "
            + $"{gaps}; {frame}; {census}; FRESH capture, generation {_inkGeneration}, sample {_inkSamples} "
            + $"of that generation, held {_inkHeldFrames} frame(s) before it, {_inkGrowthsDeferred} "
            + $"growth(s) deferred by the repeat gate and {_inkReleases} release(s) committed over this "
            + $"window's life, cause: {_inkCause}";

        if (clearsVertically && centredOnInk && gapWithinIntent)
        {
            VRLog.Info("WorldUI",
                $"GRAB BAR CLEARS THE INK: CONFIRMED for '{_logName}' — {measurement}.{suppressed} HOW TO "
                + "READ IT. The claim is that the brass handle is placed against what the window DRAWS "
                + "rather than what it FRAMES, and the THREE numbers that would falsify it are on this "
                + "line: the bar's top edge must be at or below the lowest drawn graphic's bottom edge, "
                + "the bar's centre must be the ink's centre, and — new in ModBuild 239 — the handle "
                + "must not hang more than one dead band below the window's OWN bottom edge. That third "
                + "term is the user's 'zu grosser Abstand' report, and it is JUDGED rather than merely "
                + "printed because the first two were both true of the window he photographed the "
                + "handle 390 px under. A window whose ink fills its frame "
                + "reads centre 0 and a bar top one gap under the host rect — unchanged from ModBuild "
                + "235 by construction, which is what makes an unchanged reading on those windows "
                + "evidence rather than an absence of evidence. The ink union GROWS on sight and "
                + "SHRINKS only after a recession has held for several verify samples, so a bar that "
                + "never moves while the generation number climbs means the events fire and the content "
                + "genuinely did not move; a generation number stuck at 1 across a session in which the "
                + "user opened and closed sub-views means the signature is blind, and a release count "
                + "stuck at 0 on a window whose popups come and go means the release side is not "
                + "reaching its run length. THE DRAWN-BUT-INVISIBLE COUNT IS THE OTHER LEAD: it is the "
                + "graphics that pass enabled/active/colour/cull and still paint nothing because a "
                + "CanvasGroup above them is at alpha 0, and before ModBuild 239 every one of them was "
                + "counted as ink.");
            return;
        }

        string term = !clearsVertically
            ? $"VERTICAL — the bar's top edge y={barTopPx:F0} px is ABOVE the lowest drawn graphic's "
              + $"bottom edge y={inkBottom:F0} px by {inkBottom - barTopPx:F0} px, so it is drawn over "
              + "content"
            : !centredOnInk
                ? $"HORIZONTAL CENTRE — the bar's centre x={barCentrePx:F0} px is off the ink's centre "
                  + $"x={inkCentre:F0} px by {Mathf.Abs(barCentrePx - inkCentre):F0} px"
                : $"THE GAP IS LARGER THAN INTENDED — the handle's top edge hangs "
                  + $"{dropBelowFramePx:F0} px = {dropBelowFramePx * mmPerPx:F0} mm below the window's "
                  + $"own bottom edge y={hostRect.yMin:F0} px, which is {dropBelowFramePx / Mathf.Max(intendedTopGapPx, 1e-3f):F1}x "
                  + $"the intended {intendedTopGapPx:F0} px = {intendedTopGapPx * mmPerPx:F0} mm. THIS IS "
                  + "THE USER'S 'zu grosser Abstand' COMPLAINT AS ONE NUMBER, and it is held down by the "
                  + $"graphic named as the lowest above, '{_inkBottomName}' at y={inkBottom:F0} px. TWO "
                  + "READINGS SETTLE WHETHER IT IS A FAULT. If that graphic really is painted there "
                  + "(the battle-goal picker draws 373 px below this frame while it is open) the bar is "
                  + "CORRECT and cutting across it is quest_überlap.jpg. If it is not on the screen, "
                  + "the ink union is measuring a ghost — compare this line's graphic count against the "
                  + "HIT RECT line's 'visible graphic(s)' for the SAME window, which applies the content "
                  + $"fit's stricter verdict, and against this line's own {_inkFaint} "
                  + "drawn-but-invisible exclusion(s)";
        VRLog.Warn("WorldUI",
            $"GRAB BAR CLEARS THE INK: NOT ACHIEVED for '{_logName}' — failing term: {term}. {measurement}."
            + suppressed);
    }

    // ---- teardown ---------------------------------------------------------------------------

    /// <summary>Destroy the mod-owned holder (the game host is released separately by the caller).</summary>
    internal void Destroy()
    {
        LiveHolders.Remove(this); // ModBuild 230 — leaves the sweep's live set with the holder itself
        if (_holder != null)
            Object.Destroy(_holder.gameObject);
        _holder = null;
        _frame = null;
        _visual = null;
        _bar = null;
        _grabZone = null;
        _handle = null;
        _visualValid = false;
        _easing = false;
        // The ink capture describes furniture that no longer exists; a rebuilt holder must measure
        // again from scratch rather than inherit a union taken against the old host rect.
        _inkValid = false;
        _inkGenSeeded = false;
        _inkPendingValid = false;
        _inkReleaseValid = false;
        _inkReleaseRun = 0;
        _inkSignatureValid = false;
        _inkHostRectValid = false;
        _inkNextSampleFrame = -1;
        _inkCommitFrame = -1;
        _inkReportDue = false;
        _inkFallbackDue = false;
        _inkFallbackReported = false;
        _inkTransient = 0;
        _inkTransientLife = 0;
        _inkTransientMask = 0;
        _inkSigTransientChildren = 0;
        _inkSigTransientLife = 0;
        _inkModChromeMask = 0;
        // The plate belongs to the game-owned host, which the caller releases separately; dropping
        // the reference is all this class may do with it. A re-converted window re-finds its own.
        _closeX = null;
        _closeXPlaced = false;
        _closeXNextProbeFrame = -1;
        _closeXPlacement = default;
    }
}
