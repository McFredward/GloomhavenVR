using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI; // the game's UIWindow lives here (decompiled/GH.Runtime/UnityEngine.UI/UIWindow.cs)

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Makes a floated modal window (<see cref="ModalFallback"/>) a GRABBABLE + SCALABLE
/// world element — exactly like the control board / combat log — by reusing the SHARED
/// grab core (<see cref="PanelGrabHandle"/> + <see cref="IPanelGrabOwner"/>): one hand
/// grips the rod under the panel to MOVE it, two hands RESIZE it (the SHARED range
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
    // ---- THE ROD'S DIMENSIONS NOW LIVE IN GrabBarLayout, AND THAT IS THE WHOLE OF THE CHANGE ----
    //
    // Every constant that used to be declared here was ALSO declared, by value and by name, in
    // SurfaceGrabBar — whose class doc says so in as many words — and the combat log was about to
    // become the third copy. They moved to WorldUI.GrabBarLayout verbatim, together with the eight
    // lines of derivation SyncBar spent on them, and the three owners now call GrabBarLayout.Solve.
    // The aliases below keep this file's own prose (and its falsifier's arithmetic) reading the way
    // it did; nothing about any shipped bar's size, thickness, gap or zone moved by a float.
    //
    // The doc that stood on BarRadius is preserved on GrabBarLayout.BarRadius, including the
    // ergonomics exchange behind 0.014 and the note that this measures the SHAFT rather than the
    // knobs — the same distinction every consumer in this file depends on.

    /// <summary>Gap below the panel's bottom edge to the bar centre (frame-local, scale-1 metres).</summary>
    private const float BarGapMeters = GrabBarLayout.BarGapMeters;

    /// <summary>
    /// THE ROD'S NOMINAL RADIUS, in the same frame-local scale-1 metres <see cref="BarGapMeters"/>
    /// is in. It is <see cref="GrabBarMesh.DefaultRadius"/> rather than a literal of this file's
    /// own, because the board handle and the window handle are now ONE drawn object and two
    /// hand-kept copies of its thickness is exactly how they drift apart.
    ///
    /// <para><b>WHAT THIS REPLACED, AND THE ONE NUMBER THAT CHANGED.</b> The bar was a unit cube
    /// scaled to <c>barWidth x BarThickness x BarThickness</c> with <c>BarThickness = 0.024</c> —
    /// a 24 mm strip at proportion 1 and diorama scale 1. The rod is 28 mm thick there
    /// (<c>2 x 0.014</c>), which is the design sheet's 12.5 : 1 length-to-thickness ratio and the
    /// value the user approved when it was raised as an ergonomics dial rather than a cosmetic one
    /// (<see cref="GrabBarMesh.DefaultRadius"/> carries that exchange). NOTHING ELSE about the
    /// number changed: <see cref="SyncBar"/> still multiplies it by the short-panel proportion and
    /// by the diorama's world scale, in the same two places and the same order, so a short window
    /// still gets a slimmer handle and a zoomed-out diorama a smaller one, in the ratios that
    /// shipped.</para>
    ///
    /// <para>The SHAFT is what this measures. The two end knobs swell to <c>1.28 R</c> and the
    /// shaft tapers to <c>0.89 R</c> at its own ends (<see cref="GrabBarVisual"/>'s laser-target
    /// note states both), so the rod's silhouette is not one constant radius and no single number
    /// here could be. Every consumer of this value in this file — the drawn thickness, and the top
    /// gap the falsifier reports — is about the long uniform run, which is the shaft.</para>
    /// </summary>
    private const float BarRadius = GrabBarLayout.BarRadius;
    private const float BarWidthFraction = GrabBarLayout.BarWidthFraction;
    private const float ZoneWidthFraction = GrabBarLayout.ZoneWidthFraction;

    /// <summary>See <see cref="GrabBarLayout.MinBarWidth"/> for the arithmetic behind the 0.06.
    /// It is only reachable with very narrow ink and it moves in the safe direction: the floor
    /// exists so the grab and laser targets survive on a tiny window. The two quantities are not in
    /// the same frame — this is frame-local metres and the rod's minimum is root-local — so at a
    /// heavily shrunken world scale the inequality can still bite; what it cannot do any more is
    /// bite at the ordinary scale, where it did.</summary>
    private const float MinBarWidth = GrabBarLayout.MinBarWidth;

    /// <summary>Panel height (real metres) at or above which the bar keeps its full thickness/gap —
    /// see <see cref="GrabBarLayout.BarFullSizePanelHeightMeters"/> for the torbogen screenshot
    /// that established it. Taller panels (ESC/Options, results, tutorial boxes) are numerically
    /// unchanged by it.</summary>
    private const float BarFullSizePanelHeightMeters = GrabBarLayout.BarFullSizePanelHeightMeters;

    /// <summary>Floor of the short-panel bar proportion — the visible rod (and the laser capsule,
    /// which rides the same uniform root scale) must stay a comfortable target.</summary>
    private const float MinBarProportion = GrabBarLayout.MinBarProportion;

    // ---- WHAT BECAME OF BarColliderPad, AND WHAT DID NOT GO WITH IT --------------------------
    //
    // IT WAS 1.5: the cross-section pad of the LASER-only bar collider, in bar-local units. The bar
    // was a unit cube scaled to barWidth x BarThickness x BarThickness, so 1.5 made the BoxCollider
    // HALF AGAIN AS THICK AS THE DRAWN BAR IN BOTH cross-section axes — a 3.6 cm box around a 2.4 cm
    // strip. A beam could therefore grab the handle while visibly missing it by a third of its own
    // width, which is the complaint this round set out to end.
    //
    // The pad is gone with the cube. The rod's laser target is GrabBarVisual.AttachLaserTarget's
    // CapsuleCollider: round like the rod, down the rod's own axis, kept in step by SetLength, and
    // padded by GrabBarVisual.LaserPad = 1.10 — a number documented over there AGAINST this one.
    //
    // WHAT DID NOT CHANGE IS THE REASON THE SPLIT EXISTS: the LOST-MENU FIX. RayGrabDriver ray-tests
    // ONLY the collider handed to PanelGrabHandle.SetBarCollider and never the palm zone, because the
    // palm ZONE collider (5 cm tall, 62 % of the window's width) HAD been the laser target too — and
    // since a floated menu sits between the player and the board, every trigger aimed at the cards hit
    // it and dragged the (possibly off-view) menu instead. The palm zone below is UNCHANGED, down to
    // its literals, and deliberately stays that generous: a hand reaching for a handle should not have
    // to be accurate; a beam being aimed at one should.

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
    private const int BarOrderOffset = GrabBarLayout.BarOrderOffset;

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
    //
    // ---- ModBuild 243: THE LATENCY WAS THE SPACING, NOT THE COUNT ------------------------------
    //
    // USER REPORT, verbatim (2026-08-24): "Die Reaktionszeit wenn man ein Sub-Menü z.B. im
    // Optionsmenü öffnet von dem 'X' Button und dem Greifbalken ist zu gering. Man sieht immer wie es
    // erst nach einer kurzen Zeit seinen Zustand verändert. Ich möchte dass das sofort passiert mit
    // der Änderung. Auch dauert es immer kurz bis dass der Balken verschwindet wenn das Fenster leer
    // ist, ich möchte gerne dass es instant reagiert." ("zu gering" = the responsiveness is too low,
    // i.e. the latency is too high.)
    //
    // WHAT HE MEASURED, IN THE ModBuild 242 LOG, ON THE WINDOW HE NAMED. 'UI Options Window_unified'
    // opens with a 110 px half-width handle under its category column and jumps to 415 px once a tab
    // is open — a 3.8x change in the visible handle, which is why the delay is impossible to miss.
    // Two episodes can be anchored to absolute frames off neighbouring [Perf] SPIKE / MIP BAKE lines:
    //   * click 'GloomhavenVR.OptionsTab' at line 2675 (frame ~17269) -> committed at line 2685
    //     (frame ~17333, 'held 724 frame(s)' after a commit at frame ~16609): 64 frames = 717 ms.
    //   * click 'GloomhavenVR.OptionsTab' at line 2797 (frame ~18383) -> committed at line 2810
    //     (frame ~18484, 'held 308 frame(s)' after a commit at frame ~18176): 101 frames = 1131 ms.
    // At the session's own measured 89.3 Hz ([Perf] SPLIT 30.0s n=2679). Both lines read
    // '1 growth(s) deferred by the repeat gate', which is the whole shape: 1-60 frames of waiting for
    // the next VERIFY sample, then 60 more for the repeat gate to see the same rectangle twice.
    //
    // AND THE SIGNATURE NEVER FIRED ONCE. Every options-window commit in that log reads
    // 'generation 1', through four tab presses (three mod tabs and the game's own 'Audio' tab) and up
    // to 'sample 198 of that generation'. The one generation event the window ever saw was fired by a
    // 'Class Toggle' click in ANOTHER window through part two of the signature, and it changed
    // nothing. The cause is in PanelInkBounds' own ModBuild 243 block and it is one word: DEPTH. The
    // tab windows live at Target/Tabs/<tab> and the signature hashed direct children only. It is now
    // hashed to depth two, so a sub-menu press IS a generation event again — which is the ONE change
    // that turns "sofort" into 11 ms instead of 717.
    //
    // THE THREE SPACINGS, AND WHY EVERY ONE OF THEM COLLAPSES SAFELY.
    //   * A GENERATION EVENT NOW SAMPLES ON ITS OWN FRAME (it waited InkSettleStrideFrames). The
    //     sample REPLACES the union, the settle burst is exempt from the repeat gate, so the bar and
    //     the X commit in the frame the tab was pressed. What is measured then is what is DRAWN then:
    //     a tab window whose fade has not started is under the alpha floor and simply does not count,
    //     and the burst below grows the union as it arrives. That is the intended behaviour, not a
    //     compromise — the handle tracks the picture, and the picture is still fading.
    //   * THE SETTLE BURST NOW EXTENDS ITSELF while consecutive samples keep moving the union, to a
    //     HARD CAP of InkSettleMaxFrames. The fixed 24 frames was 0.40 s at 60 Hz and is 0.27 s at
    //     90 Hz — shorter than the game's own window fade — so the burst was expiring one sample
    //     before the tab finished arriving and the last growth fell back onto the 60-frame gate. The
    //     cap is what keeps this a settle and not a gate that never opens
    //     ([[settle-gate-vs-external-writer]]).
    //   * THE REPEAT GATE AND THE RELEASE RUN KEEP THEIR COUNTS AND LOSE THEIR SPACING. A growth
    //     still has to be seen twice and a recession still three times, exactly as before; what
    //     changes is that the confirming samples are taken InkConfirmStrideFrames apart instead of
    //     InkVerifyStrideFrames apart the moment a sample disagrees with the committed envelope.
    //     WHY THAT IS SAFE, term by term, because this is where a future round will be tempted to
    //     put the 60 frames back:
    //       - THE HOVER TOOLTIP WAS THE ONLY TERM THAT NEEDED WALL-CLOCK SPACING, and it is gone.
    //         The gate was written so that a widget the game re-parents onto the window could not pin
    //         the bar away for a whole generation; a hover lasts seconds, so it had to be outlasted.
    //         ModBuild 242 took hover subtrees out of the union AND out of the signature, so there is
    //         nothing left for the spacing to outlast. The log proves the input is clean: all 35 GRAB
    //         BAR lines read '0 transient graphic(s) refused from the union this sample'.
    //       - A LAYOUT REBUILD IS SETTLED WITHIN ONE FRAME, so two samples four frames apart both see
    //         the finished layout. Closer spacing is STRICTLY BETTER here than 60 frames, which can
    //         put both samples inside one long animation and agree on a wrong rectangle.
    //       - A WINDOW MID-FADE changes its effective alpha every frame, so two samples four frames
    //         apart DISAGREE and the gate correctly refuses. At 60 frames they can straddle the fade
    //         and agree. Again strictly better.
    //       - A POOLED ROW ACTIVE BUT NOT YET POPULATED is the one case where tight spacing is worse:
    //         both confirming samples can land inside the same short unpopulated window. It is bounded
    //         by the same rule that has always bounded it — growth commits on sight, so the union is
    //         corrected on the very next sample, and the settle burst is re-armed after a release
    //         precisely so that correction costs four frames and not sixty.
    //     NOTHING WAS TRADED AWAY: InkReleaseConsecutive is still 3 and InkReleaseDeadBandPx is still
    //     32 px, so quest_überlap.jpg's protection is intact to the pixel.
    //
    // ---- ModBuild 243: "DER BALKEN VERSCHWINDET WENN DAS FENSTER LEER IST" ----------------------
    //
    // The second half of the report is the SAME defect and it is worth writing down why, because the
    // two halves look unrelated. "The window got smaller" and "the window went empty" are both the
    // SHRINK direction, and shrinking is the only direction this class was ever slow in.
    //
    // Through ModBuild 242 an ink walk that found nothing simply returned: the committed rectangle
    // stayed, so the brass handle kept hanging at the full width of content that is no longer on the
    // screen, for as long as the float lived. That is not a slow reaction, it is NO reaction — the
    // handle only went away when ModalFallback's liveness rule released the whole float, which through
    // ModBuild 290 was deliberately a 2 s dwell (the unlock flow blanks its own popup for a ~1 s camera
    // focus between two announcements and a shorter dwell would have dropped the second one). ModBuild
    // 291 turned that release into a reversible HIDE and the dwell into EmptyHideDwellSeconds = 0.35 s;
    // this block is UNCHANGED by that and still earns its place, because it takes the handle off on its
    // own confirm run rather than waiting for any of those numbers.
    //
    // SO THE HANDLE IS TAKEN OFF THE SCREEN HERE, ON ITS OWN, AS PRESENTATION. A confirmed empty ink
    // gives up the committed rectangle, hides the bar and its laser collider, and puts the close X
    // back on the frame's own corner. It comes back on the first sample that measures ink again,
    // through the ordinary growth path. THREE THINGS BOUND IT:
    //   * It needs InkEmptyConfirmSamples agreeing empty walks at the confirm stride, so one bad walk
    //     (a window caught between a fade-out and a fade-in) cannot blink the handle.
    //   * It only applies to a window that HAS committed an ink rectangle. A window that has never
    //     measured one keeps the frame-based handle and the existing NOT ACHIEVED line, byte for
    //     byte — that is the shipped answer for 'UI Event Window' and it is not this round's business.
    //   * IT NEVER TOUCHES THE X, and that is a deliberate asymmetry rather than an oversight. The X
    //     is the rescue for a window the player can no longer see; taking it away as well would turn
    //     "the window went empty" into a window that cannot be closed if the liveness rule ever
    //     disagrees with this walk about what "drawing nothing" means. The bar is what he named; the
    //     bar is what goes.
    // AND IT NEVER FIRES MID-GRAB. A hand holding the handle when the content vanishes keeps it: the
    // collider is not pulled out from under a live grab.
    private const int InkSettleFrames = 24;
    private const int InkSettleStrideFrames = 4;
    private const int InkVerifyStrideFrames = 60;
    private const float InkReportThrottleSeconds = 1f;

    /// <summary>ModBuild 243 — frames between the CONFIRMING samples of a repeat gate, a release run
    /// or an empty run. It is the settle stride's value for the settle stride's reason (a sample every
    /// four frames is fast enough to be invisible and rare enough to be free), stated separately
    /// because the two are answering different questions and a future round may want to move one
    /// without the other. See the ModBuild 243 block for why the 60-frame spacing this replaces was
    /// buying nothing once the hover subtrees left the union.</summary>
    private const int InkConfirmStrideFrames = 4;

    /// <summary>ModBuild 243 — the hard ceiling on a SELF-EXTENDING settle burst, in frames from the
    /// event that started it. The burst extends itself while consecutive samples keep moving the
    /// union so that a window fade longer than <see cref="InkSettleFrames"/> is followed to its end;
    /// this is what stops a window whose content is rewritten every frame from holding the burst (and
    /// its 4-frame sampling cost) open forever. ~1.33 s at 90 Hz, comfortably past every window
    /// animation the game runs.</summary>
    private const int InkSettleMaxFrames = 120;

    /// <summary>
    /// ModBuild 243 — <b>THE FAST CONFIRM IS A BUDGET, NOT A MODE.</b> Consecutive samples that may be
    /// taken at <see cref="InkConfirmStrideFrames"/> before the cadence falls back to
    /// <see cref="InkVerifyStrideFrames"/> until something actually commits.
    ///
    /// <para>WHY IT HAS TO EXIST. The confirm stride is armed by "this sample disagrees with the
    /// committed envelope", and a window whose content genuinely never settles disagrees on every
    /// sample — so without a budget the ink walk (a subtree walk of up to MaxNodes transforms, per
    /// floated window) would run 15x more often, forever, on exactly the window that can least
    /// afford it. That is this project's own recurring shape: a limiter removed, and the thing it was
    /// capping explodes ([[fuse-was-hiding-a-loop]]). Eight is comfortably more than any real
    /// confirmation needs (a growth needs 1, a release 2, an empty 1) and small enough that a
    /// churning window costs one extra third of a second of fast sampling and then goes quiet.</para>
    /// </summary>
    private const int InkConfirmMaxSamples = 8;

    /// <summary>ModBuild 243 — agreeing empty ink walks before the brass handle is taken off the
    /// screen. TWO, at <see cref="InkConfirmStrideFrames"/>: one is a single bad sample, and three
    /// would put a visible handle on an invisible window for longer than the user can already see.</summary>
    private const int InkEmptyConfirmSamples = 2;

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
    /// <summary>
    /// ModBuild 447 — <b>THE COMMITTED ANSWER TO "DOES THIS WINDOW PAINT ITS WHOLE FRAME?"</b>, and
    /// the one term that decides whether the rod's width and centre come from the frame or from the
    /// union. The rule, the user's ruling behind it and the four windows it was measured against are
    /// all on <see cref="GrabBarLayout.SolveSpan"/>; this field is only its storage.
    ///
    /// <para><b>IT IS COMMITTED, NOT SAMPLED,</b> and that is deliberate: it moves through exactly
    /// the gates <see cref="_inkRect"/> moves through — monotone inside a generation (a plate seen
    /// once holds until the generation ends), a growth outside the settle burst must repeat before
    /// it is taken (the repeat gate), and only a confirmed RELEASE may drop it. Read on a raw sample
    /// instead it would be a boolean that can flip on any single frame, and it moves the rod's
    /// CENTRE, which is the one term <see cref="SyncBar"/> deliberately does not damp.</para>
    ///
    /// <para>Cleared wherever <see cref="_inkValid"/> is cleared — a window that has stopped drawing
    /// has no verdict about its frame either, and the next window to arrive in the same holder must
    /// not inherit this one's.</para>
    /// </summary>
    private bool _inkFullFrame;

    /// <summary>
    /// ModBuild 449 — <b>THE COMMITTED BOTTOM EDGE OF THIS WINDOW'S FULL-FRAME PLATE</b>, in the
    /// host's own authored px, or <c>float.PositiveInfinity</c> when it paints none. It rides
    /// exactly the gates <see cref="_inkRect"/> rides — monotone DOWNWARD inside a generation, and
    /// only a confirmed release may lift it — for the same reason <see cref="_inkFullFrame"/> does:
    /// it moves the rod, and a term that moves the rod may not flip on one frame's walk.
    ///
    /// <para><b>THE ONE CONSUMER IS THE ROD'S VERTICAL SEAT</b>, and the whole argument (the
    /// co-player's merchant, the 16:9 plate stretched across an ultrawide frame, the 371 px of
    /// picture that hung below the union's own bottom) is on
    /// <see cref="PanelInkBounds.Ink.PlateBottom"/>. The close X, the badge, the re-face pivot and
    /// <c>GrabBarLayout.SolveSpan</c>'s horizontal answer all read <see cref="_inkRect"/> and are
    /// untouched by it.</para>
    ///
    /// <para><b>IT IS AN INFINITY, NOT A ZERO,</b> and every read of it is a <c>Min</c> against
    /// <c>hostRect.yMin</c>, so a window with no plate and a window whose plate ends at its own
    /// frame — which is every plate the host client measured all round — are bit-identical to what
    /// shipped in ModBuild 448.</para>
    /// </summary>
    private float _inkPlateBottom = float.PositiveInfinity;

    /// <summary>The plate that set <see cref="_inkPlateBottom"/>, for the falsifier line. Empty when
    /// this window paints no plate.</summary>
    private string _inkPlateBottomName = string.Empty;

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
    /// <summary>ModBuild 243 — the frame past which the SELF-EXTENDING settle burst may not be
    /// extended again, whatever the samples say (<see cref="InkSettleMaxFrames"/>).</summary>
    private int _inkSettleHardStopFrame = -1;
    /// <summary>Burst extensions granted over this window's life. A number that keeps climbing on a
    /// window nobody is touching is content that never stops moving, and the falsifier for it.</summary>
    private int _inkBurstExtensions;
    /// <summary>Agreeing EMPTY walks so far (<see cref="InkEmptyConfirmSamples"/>).</summary>
    private int _inkEmptyRun;
    /// <summary>Confirm-stride samples spent since the last commit (<see cref="InkConfirmMaxSamples"/>).</summary>
    private int _inkConfirmRun;
    /// <summary>Times the confirm budget ran out and the cadence fell back to the verify stride, over
    /// this window's life. Non-zero means a window whose content does not settle; it is the falsifier
    /// for "the fast path made the sampling expensive".</summary>
    private int _inkConfirmBudgetSpent;
    /// <summary>Times the handle was taken off the screen for an empty window, over its life.</summary>
    private int _inkEmpties;
    /// <summary>The bar is hidden because this window's ink is confirmed empty. Its own flag and not
    /// <c>!_inkValid</c>, because a window that has NEVER measured ink keeps the frame-based handle —
    /// that is the shipped answer and this round does not touch it.</summary>
    private bool _barHiddenForEmpty;

    /// <summary>
    /// ModBuild 378 — the rod is WITHHELD because this float still owes its materialise appear, i.e.
    /// it has never once been measured drawing anything. Distinct from
    /// <see cref="_barHiddenForEmpty"/> on purpose: that flag is the ModBuild 243/251 rule taking a
    /// handle OFF a window that has gone dark, this one is the ModBuild 378 rule never putting one
    /// ON a window that has not arrived. Two different edges, both live, and neither may be folded
    /// into the other — a window that draws, goes empty and draws again has to run all of them.
    /// </summary>
    private bool _barWithheldForOwedAppear;

    /// <summary>The withholding term's own words, kept from the RISING edge so the falling edge can
    /// name what had held the rod back. See <see cref="NoteWithholdChange"/>.</summary>
    private string _barWithholdWhy = string.Empty;

    /// <summary>Times the rod was WITHHELD (rising edge of <see cref="_barWithheldForOwedAppear"/>)
    /// over this window's life. Paired with <see cref="_barWithholdReleases"/> on the log line: a
    /// change-gated line whose reason never varies prints once and then reads like a dead
    /// instrument, and these two counts are what keep it alive.</summary>
    private int _barWithholds;

    /// <summary>Times a withheld rod was RELEASED because the window started drawing, over its
    /// life. <c>withholds - releases</c> is 1 exactly while a rod is being withheld right now.</summary>
    private int _barWithholdReleases;

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
    /// <summary>ModBuild 243 — active nodes the depth-2 signature walk actually hashed on the latest
    /// tick, and whether its node budget bit. Printed on the falsifier line for one reason: a
    /// signature that has gone blind (0 nodes, or truncated before it reached the tab container)
    /// looks EXACTLY like a window nobody is touching, and that mistake cost this round.</summary>
    private int _inkSigNodes;
    private bool _inkSigTruncated;
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
    private GrabBarVisual? _bar;                // the drawn rod: three pieces, ONE material
    private GrabBarTween? _barTween;            // THE ONE WRITER of the rod's presented pose — see GrabBarTween
    private Transform? _badge;                  // the shared-window mark, null until built
    private Material? _badgeMaterial;           // the badge's OWN instance — the pulse's ONE writer

    private BoxCollider? _grabZone;
    private PanelGrabHandle? _handle;

    /// <summary>
    /// THE SHARED SETTLE RULE, one instance per size term and per window — the answer to the
    /// 2026-09-03 report, which asked for the handle's size flicker to be suppressed <i>"allgemein"</i>
    /// and not on the one panel it was noticed on. <c>SurfaceGrabBar</c> holds exactly this pair and
    /// feeds it exactly these two quantities, so a modal window's handle and a decision panel's
    /// handle behave identically. The full derivation, the settle window and the shrink ruling all
    /// live on <see cref="BarSizeSettle"/>; nothing about them is restated here, because two copies
    /// of a rule is how two handles drift.
    /// </summary>
    private readonly BarSizeSettle _widthSettle = new("width");
    private readonly BarSizeSettle _heightSettle = new("height");

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
    // TWO SEPARATE USER STATEMENTS BOUND IT — a shared window never re-faces on any client and that
    // is not configurable (7b), and for local windows the three-way [WorldUI] WindowFacing dial
    // decides (8). BOTH GATES, THEIR VERBATIM RULINGS AND THEIR ORDER NOW LIVE IN ONE PLACE:
    // WindowReFacePolicy (WorldUI/Grab/WindowReFacePolicy.cs), which this owner, SurfaceGrabBar and
    // every future re-face owner consult. They were written out here and only here, and a second
    // re-face site shipped in ModBuild 428 without reading the dial at all — the survey's row R1.
    // The policy is shared; the PIVOT ARITHMETIC below is not, and deliberately so: this owner turns
    // about the ink union's centre for the reason the paragraph above spends a build explaining,
    // and a surface turns about its frame origin because that IS its drawn centre.
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

    /// <summary>Below this the released panel already faces the player — no snap, no log.
    /// <see cref="WindowReFacePolicy.ReFaceEpsilonDeg"/> rather than a local 0.5f: this file and
    /// <c>SurfaceGrabBar</c> each declared the same constant under the same name, which is the
    /// copy-by-value the policy class was extracted to end.</summary>
    private const float ReFaceEpsilonDeg = WindowReFacePolicy.ReFaceEpsilonDeg;

    /// <summary>
    /// Does THIS release re-derive the facing? Both gates — the non-negotiable shared-window rule
    /// first, the player's <c>[WorldUI] WindowFacing</c> dial second — now live in
    /// <see cref="WindowReFacePolicy"/>, which every re-face owner in the mod consults. The gates,
    /// their user rulings and the reason each refusal is logged rather than silent are written out
    /// there, once. THE VERDICT IS UNCHANGED for a modal window: the same two tests in the same
    /// order over the same two inputs, and the refusal wordings are byte-identical.
    /// </summary>
    private bool WantsReFaceOnRelease()
        => WindowReFacePolicy.WantsReFaceOnRelease(
               _shared,
               _handle != null && _handle.LastGrabWasLaser,
               "MODAL WINDOW",
               _logName);

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

        // FRAME-ORDER GrabbableModal.LateSyncHost [SyncHostToFrame, SeatBadge]
        //   Locked (.planning/refactor/FRAME-ORDER.lock). The badge is seated in the HOST's own
        //   authored pixels, so it can only be placed once the host's final pose for this frame has
        //   been written — and that write is the very next line, in this phase, for the reason
        //   this whole method exists. Putting the seat anywhere earlier is the 2026-09-04 "zieht nach"
        //   defect ("Zusätzlich zieht es immer etwas nach wenn man das Fenster bewegt statt fix
        //   auf der Ebene zu sein"); see THE BADGE'S SEAT block.
        SyncHostToFrame(host, metersPerPixel, _spawnWorldScale);
        SeatBadge(host);
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

    // ---- THE SHARED-WINDOW MARK -------------------------------------------------------------
    //
    // ORIGINAL USER REQUEST (2026-08-22, verbatim):
    //
    //   "3) Die Fenster die für alle Spieler sichtbar sind sollen eine andere Farbe beim dem
    //    Greifbalken haben (zB Blau) um anzuzeigen, dass es ein Fenster ist das alle sehen."
    //
    // AND HIS RULING ON WHAT SHIPPED (2026-08-28, verbatim):
    //
    //   "Mach stattdessen rechts oben in der Ecke ein kleines (nicht aufdringliches)
    //    Netzwerksymbol in das Fenster."
    //
    // SO THE BLUE BAR IS GONE, MECHANISM AND ALL. It is not left wired to a value nobody sets: the
    // two-colour choice, the PrivateBarColor literal, the _barTint cache and the
    // PanelGrabHandle.SetBarBaseColor call that carried it are all deleted, and SharedWindows.BarTint
    // is deleted with them (it had exactly one reader — the line that used to be here). A shared
    // window's rod is now drawn from the same texture, at the same resting tint, as a private one:
    // there is no code path left that can make one look different from the other.
    //
    // WHY THE BAR WAS THE WRONG SURFACE FOR IT, recorded because the first answer was a reasonable
    // one and the correction is about a constraint the first answer could not see. The rod is now a
    // TEXTURED object (dark oiled walnut, aged-brass knobs) and its colour is a TINT MULTIPLIED onto
    // that strip — so "paint the bar blue" is no longer painting, it is filtering a wood grain
    // through blue, which reads as a fault rather than as a signal. The handle is also the one part
    // of the window furniture the hand is meant to reach for, and the hover highlight already owns
    // its colour; a status meaning stacked on top of a hover meaning on one 24 mm strip is two
    // languages in one place. A badge is a mark whose ONLY job is to say something.
    //
    // WHERE THE ANSWER IS DECIDED: in <see cref="SyncSharedState"/> below, and nowhere else. It asks
    // <see cref="SharedWindows"/> — which owns the DEFINITION of "shared" and its whole rationale —
    // and turns the answer into one bool. This class contributes no policy: it does not know which
    // windows are shared, only how to show a mark.
    //
    // WHERE THE BADGE GOES: BELOW the close X, sharing its right edge, seated from the SAME committed
    // rectangle the bar and the X are seated from. The full argument is on <see cref="SyncBadge"/>.
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
    // WHAT A PRIVATE WINDOW COSTS: nothing that is drawn. The badge object exists on every floated
    // window (one 4-vertex quad, built once), but it is INACTIVE unless the window is shared, so a
    // single-player session draws exactly what it drew before this feature — and, unlike the tint it
    // replaces, there is no material and no colour anywhere that a private window could be caught
    // wearing. That includes the map story window and the quest popup for a player with the 3D map
    // switched off, for whom those windows really are private (moving one moves nothing for anybody).
    //
    // WHAT WAS REJECTED:
    //   * KEEPING THE BLUE BAR AS WELL AS THE BADGE. Refused by the ruling itself — "stattdessen".
    //   * A uGUI Image on the host canvas, the way the close X is built. It would have been less
    //     code (no mesh, no material, no order follower — the X's canvas already rides the ladder).
    //     Rejected because the host canvas is GAME-OWNED and is released and re-converted under a
    //     live GrabbableModal; the X survives that only because ModalCloseButton RE-FINDS its plate
    //     every half second and this class stores nothing across a rebuild. The badge belongs to the
    //     mod's own holder, which is destroyed and rebuilt as one object, so it has no such seam.
    //   * DRAWING IT ON THE ROD (a coloured band, a second cap). Rejected: the rod is a shared
    //     drawn object with ONE material across three renderers, and per-window state on it means
    //     per-window materials — the exact trap GrabBarVisual's class doc names.
    //   * A CONFIG DIAL for the badge. Not asked for, and it would be a per-sub-feature sync
    //     setting in all but name.

    /// <summary>The badge's manifest resource name. 256x256 RGBA with a keyed alpha and a warm
    /// gold "two players" glyph; loaded (and cached, nulls included) by
    /// <see cref="EmbeddedTexture"/>. It ships INSIDE the plugin DLL rather than in
    /// <c>gloomhavenvr.bundle</c> — that bundle has been byte-identical since ModBuild 296 and every
    /// install since is DLL-only, so a badge in it would cost the user a full re-install.</summary>
    private const string BadgeResource = "GloomhavenVR.Assets.net_shared.png";

    /// <summary>
    /// The badge's edge length as a MULTIPLE OF THE CLOSE X'S PLATE — not a magic number, and not a
    /// world metre. The plate is the one piece of chrome that already stands in this corner, and its
    /// height is read LIVE off its own <c>RectTransform</c> (see <see cref="SyncBadge"/>), never
    /// copied from <c>ModalCloseButton.ButtonSizePx</c>, so a change over there moves the badge with
    /// it. Being a multiple of a HOST-PX quantity it rides the two-hand resize (0.5x-2x) and the
    /// diorama scale exactly as the rod and the X already do.
    ///
    /// <para>USER REPORT (2026-09-04, verbatim): <i>"Das Symbol, dass anzeigt, dass es sich um ein
    /// Multiplayer-Fenster handelt ist zu klein"</i>. It shipped at 24 px against a 34 px plate —
    /// 0.7 of it — under a "nicht aufdringlich" argument this report overrules. 1.5x is 51 px at
    /// today's plate: a little over DOUBLE the drawn area, about 39 mm at the 0.773 mm/px the
    /// ModBuild 241 log reports for the options window, i.e. roughly 3.7 degrees at arm's length
    /// against the 1.8 degrees it had. It is deliberately LARGER than the close X now, and the old
    /// block's reasoning survives inverted: the X is a CONTROL and stays the size a finger needs,
    /// the badge is a STATEMENT and has to be read.</para>
    ///
    /// <para><b>IT STILL CLEARS BOTH NEIGHBOURS, and the growth direction is the whole argument.</b>
    /// The badge is seated by its TOP-RIGHT corner one <see cref="BadgeGapPx"/> BELOW the plate's
    /// bottom edge, so every pixel it gains goes DOWN and LEFT, away from the X — the gap to the
    /// plate is a constant and cannot shrink with size. Downward it is floored at
    /// <c>hostRect.yMin</c> by the survival clamp that was already there, and the rod is placed a
    /// further <c>gap</c> BELOW that same edge, so the two cannot meet. Neither clearance is left as
    /// an argument: both are MEASURED and printed on the SHARED WINDOW BADGE line, in the same host
    /// px this constant is in, the way GRAB BAR CLEARS THE INK reports the rod's.</para>
    /// </summary>
    private const float BadgePlateFraction = 1.5f;

    /// <summary>The badge's edge in host px when the window has NO close X to measure against —
    /// which is not the rare case but the IMPORTANT one: the story box is explicitly excluded from
    /// the X and is the most frequently SHARED window there is (see <see cref="SyncBadge"/>). 51 px
    /// is <see cref="BadgePlateFraction"/> x the 34 px the plate path resolves to today, so the two
    /// paths draw the SAME mark and a window that gains or loses an X does not change size.</summary>
    private const float BadgeFallbackPx = 51f;

    /// <summary>How far the badge is inset from the committed rectangle's own top-right corner, in
    /// host px. Used ONLY on the degenerate path where the window carries no close X — see
    /// <see cref="SyncBadge"/>, which seats it off the plate whenever there is one.</summary>
    private const float BadgeInsetPx = 6f;

    /// <summary>Clear space between the close X's bottom edge and the badge's top edge, in host px
    /// (~5 mm at the 0.773 mm/px the ModBuild 241 log reports for the options window). Small enough
    /// that the two read as one column of window chrome, large enough that they never touch.</summary>
    private const float BadgeGapPx = 6f;

    /// <summary>
    /// The badge's offset on its panel's live draw-order ladder, the same mechanism and the same
    /// constraint as <see cref="BarOrderOffset"/>: it must draw OVER its own window's content (which
    /// is depthless, so ORDER is what decides), and it must stay under
    /// <c>CanvasConversion.PanelOrderStep</c> = 16 so a panel that is genuinely NEARER still
    /// outranks it. One above the bar's 4 so the two pieces of mod chrome have a deterministic order
    /// between them; they do not overlap today, and this is what keeps that from mattering if a
    /// window ever brings them within a pixel of each other.
    /// </summary>
    private const int BadgeOrderOffset = 5;

    // ---- THE BLUE PULSE (2026-09-04) --------------------------------------------------------
    //
    // USER REQUEST, verbatim: "außerdem will ich das es blau blinkt (nicht zu extrem)".
    //
    // "NICHT ZU EXTREM" IS A CONSTRAINT ON TWO NUMBERS, and it is the reason both of them are
    // stated with their reasoning rather than dialled. There is no config key: the user did not ask
    // for one and a per-window presentation dial on a SHARED window would be a per-sub-feature sync
    // setting in all but name — the same refusal the SHARED-WINDOW MARK block already records.
    //
    // WHY A MULTIPLY CANNOT SIMPLY BE MADE BLUE, and what is done instead. The glyph in the texture
    // is WARM GOLD and this material multiplies: gold's own blue channel is about 0.3, and a
    // multiply can only ever take a channel DOWN, so a plain blue tint would produce a dark olive,
    // not a blue. That is exactly the trap the block above records for the wooden rod ("filtering a
    // wood grain through blue, which reads as a fault"). So BadgeTint's blue channel is deliberately
    // ABOVE 1: the shader multiplies in float and only the final fragment is clamped, so gold
    // (1.00, 0.80, 0.30) x this lands at about (0.45, 0.56, 1.00) — a clear blue at full brightness
    // instead of a darkened gold. It is this badge's OWN material instance (BuildBadge news one per
    // window), so nothing else in the frame can be tinted by it, and the pulse is two float
    // operations and a colour write per frame — never a material per frame.
    //
    // THE CLOCK IS UNSCALED, and that is not a preference. A floated window keeps standing while
    // the game's own clock is stopped (a halted ActionProcessor, a pause, a modal the game itself
    // froze behind), and a pulse driven by Time.deltaTime would simply stop there and read as a
    // dead badge — the same argument AdvanceVisual's glide already makes one block up. None of this
    // is game state.

    /// <summary>
    /// The pulse's PERIOD in seconds on the unscaled clock. 2.4 s is 0.42 Hz: one slow breath, far
    /// from anything that reads as a strobe, and far above the ~0.03 Hz floor below which a
    /// modulation stops being seen as movement at all and no amount of amplitude buys it back
    /// ([[measure-the-product-not-one-factor]]).
    ///
    /// <para>IT IS A CONSTANT AND IT IS MULTIPLIED BY THE CLOCK, which is why nothing scales it.
    /// A strength term on a FREQUENCY is wrong at every instant except zero and drifts the phase
    /// whenever it changes ([[frequency-scrub-bug-class]]); the amplitude below therefore scales the
    /// oscillator's OUTPUT and only its output.</para>
    /// </summary>
    private const float BadgePulsePeriodSeconds = 2.4f;

    /// <summary>How far the tint dims at the trough, as a fraction of its resting brightness: the
    /// drawn brightness sweeps 1.00 -> 0.70 -> 1.00 over one <see cref="BadgePulsePeriodSeconds"/>.
    /// A 30 % swing on a mark 39 mm across is plainly a pulse and plainly not a flash; a hard on/off
    /// is what "nicht zu extrem" rules out. The wave is written so that phase 0 gives exactly 1.00 —
    /// the resting value — so a badge that has just appeared is never caught mid-dim.</summary>
    private const float BadgePulseDepth = 0.30f;

    /// <summary>The badge's resting tint, at the top of the pulse. See the block above for why the
    /// blue channel is above 1 and why that is the only way to turn a gold glyph blue through a
    /// multiply.</summary>
    private static readonly Color BadgeTint = new(0.45f, 0.70f, 3.40f, 1f);

    /// <summary>The pulse's phase in radians, wrapped to [0, 2*PI). Integrated from
    /// <c>Time.unscaledDeltaTime</c> rather than read from <c>Time.unscaledTime</c>: that clock
    /// grows without bound and a float's resolution at a few hours of session is coarser than this
    /// period needs, which would show as the pulse quantising rather than as drift.</summary>
    private float _badgePulsePhase;

    // ---- THE BADGE'S SEAT, and where it is written ------------------------------------------
    //
    // 2026-09-04, verbatim: "Zusätzlich zieht es immer etwas nach wenn man das Fenster bewegt statt
    // fix auf der Ebene zu sein."
    //
    // WHAT WAS ACTUALLY WRONG, stated as the structure rather than as a guess. The badge is seated
    // ENTIRELY IN THE HOST'S OWN AUTHORED PIXELS — against the host rect, against the committed ink
    // union, against the close X's plate — and every one of those quantities lives in the HOST
    // transform's frame. But the badge itself hung under `_visual`, which is a DIFFERENT transform:
    // a scene-root SIBLING of the grab frame that the REMOTE POSE EASING block builds precisely so
    // that it CAN lag ("A SIBLING of the frame, not a child: it must be able to lag behind it, which
    // a child cannot"). The two are kept equal only because two separate statements write them from
    // one field — and they are not written the same way: `Tick` writes `_visual` and then the host
    // in UPDATE, while `LateSyncHost` writes the host in LATEUPDATE unconditionally but re-pins
    // `_visual` only `if (!_easing || !_visualValid)`. `_easing` is true for exactly one class of
    // window: a SHARED one a peer is moving — i.e. exactly the windows that have a badge. So the
    // mark whose whole placement is expressed in host pixels was riding a PROXY of the host kept in
    // step by two writes in two phases, one of them conditional, and the seat itself was computed a
    // phase early (in `SyncBar`, from `Tick`, in Update) while the pose it is measured in is
    // finalised in LateUpdate. That is the "positioned in Update while the host moves in LateUpdate"
    // shape and the "parented outside the host and re-derives its pose" shape at once.
    //
    // WHAT IS DONE: `SyncBadge` no longer writes a transform at all. It RESOLVES the seat (in host
    // px) and the size, and `SeatBadge` — called from `LateSyncHost`, in LateUpdate, IMMEDIATELY
    // AFTER the one statement that writes the host's final pose — puts the badge on the host's own
    // plane by `host.TransformPoint`. Same phase, after the writer, and no term of the badge's world
    // pose comes from `_visual` any more. The badge is reparented to `_holder` for the same reason:
    // the holder is the identity-pose, identity-scale root this class asserts every tick, so the
    // badge inherits NOTHING that can glide underneath it.
    //
    // WHY NOT SIMPLY PARENT IT UNDER THE HOST, which would be rigid by construction and needs no
    // per-frame write at all. Three separate refusals, each already paid for elsewhere in this
    // module:
    //   * PanelSupersample.ApplyCaptureLayer walks the HOST SUBTREE and moves everything it finds
    //     onto that window's private capture layer. A badge under the host would be rendered INTO
    //     the window's render target and resampled with it instead of being drawn into the eye.
    //   * CanvasConversion.HideTree also walks the host subtree, so the reveal gate would own the
    //     badge's renderer — and SyncBarVisibility is the one owner of whether this mark is on the
    //     screen ([[dont-win-a-write-war]]). Today it cannot be recorded there at all, because it is
    //     an INACTIVE GameObject when it is off.
    //   * The host canvas is GAME-OWNED and is released and re-converted under a live
    //     GrabbableModal — the same reason the SHARED-WINDOW MARK block gives for not building this
    //     as a uGUI Image on it. A child of the host is destroyed with the host, behind this class's
    //     back, and `_badge` would be a dangling reference `BuildBadge` never runs again to repair.
    // So a per-frame follow it is — and, per the standing rule for that, it runs in the same phase
    // and after the writer of the pose it follows, and the adjacency is locked
    // (.planning/refactor/FRAME-ORDER.lock, GrabbableModal.LateSyncHost).

    /// <summary>The badge's centre in the HOST's own authored px, resolved by
    /// <see cref="SyncBadge"/> and consumed by <see cref="SeatBadge"/>.</summary>
    private Vector2 _badgeSeatPx;

    /// <summary>The badge's resolved edge length in host px — <see cref="BadgePlateFraction"/> x the
    /// live plate, or <see cref="BadgeFallbackPx"/> where there is no plate.</summary>
    private float _badgeSizePx;

    /// <summary>False until <see cref="SyncBadge"/> has resolved a seat this window can be placed
    /// from; <see cref="SeatBadge"/> writes nothing while it is false rather than guessing.</summary>
    private bool _badgeSeatValid;

    /// <summary>Clear space measured between the close X plate's bottom edge and the badge's top
    /// edge, in host px, or a negative number where the two overlap. <c>float.NaN</c> when the window
    /// has no plate to clear. Reported, never acted on.</summary>
    private float _badgeGapToPlatePx = float.NaN;

    /// <summary>Clear space measured between the badge's bottom edge and the rod's TOP edge, in host
    /// px, or a negative number where the two overlap. Reported, never acted on.</summary>
    private float _badgeGapToRodPx = float.NaN;

    // ---- THE BADGE'S OWN FALSIFIER ----------------------------------------------------------
    //
    // "Zieht nach" has to be answerable from the log rather than from the eye next round, and it has
    // to be answerable in a way that cannot subtract a value from itself — the pose-gap probe two
    // blocks up shipped once doing exactly that and "proved" a defect out of existence with 84,647
    // samples of 0.00 ([[one-step-too-early]]). So TWO different distances are sampled, both in the
    // window's own authored pixels and both while the window is MOVING, which is the only interval
    // the complaint is about:
    //
    //   SEAT — how far the badge was from its seat at the instant the frame's final host pose
    //          landed, sampled BEFORE this frame's write. It is the residual the badge carried into
    //          the frame; on the fixed path it is one frame of window travel and nothing else, so a
    //          number much larger than the drag rate means something is still moving the host after
    //          SeatBadge ran.
    //   PROXY — how far `_visual` (the transform the badge USED to hang under) sits from the host
    //          (the transform its seat is measured in). This is the term the diagnosis above names,
    //          and it is now measured instead of argued: 0.00 across a drag says the pose chain was
    //          never the cause and the next round must look at the DRAW path (the supersample
    //          display quad is posed in Camera.onPreCull, a phase later than everything here);
    //          anything else says the proxy was the cause and the reparent removed it.
    //
    // BOUNDED BY CONSTRUCTION: at most BadgeSampleCap moving samples per window, then ONE line, and
    // never again for that window. Nothing here is a per-frame line.

    /// <summary>How many MOVING frames are sampled before the one line is written. ~2 s of drag at
    /// 90 Hz — long enough that a mean is not one lucky frame, short enough that it closes during
    /// the first real drag of a session.</summary>
    private const int BadgeSampleCap = 180;

    /// <summary>A host that moved less than this between two LateUpdates is not "being moved", and
    /// its sample would only dilute the mean with stillness. 0.1 mm at diorama scale 1.</summary>
    private const float BadgeMovingEpsilonMeters = 0.0001f;

    private int _badgeSamples;
    private float _badgeSeatSumPx;
    private float _badgeSeatWorstPx;
    private float _badgeProxySumPx;
    private float _badgeProxyWorstPx;
    private Vector3 _badgeLastHostPos;
    private bool _badgeLastHostPosValid;
    private bool _badgeReported;

    /// <summary>Is this window SHARED for this client right now — <see cref="SharedWindows.IsShared"/>
    /// as of the last tick. False for every window in a single-player session and for every private
    /// window in a multiplayer one, so both behaviours it gates (the release re-face and the remote
    /// pose easing) are inert there, along with the badge. Written only by
    /// <see cref="SyncSharedState"/>; see the note there for why it is cached rather than
    /// asked.</summary>
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
    /// Re-evaluate whether <paramref name="window"/> is shared FOR THIS CLIENT right now. Called
    /// once per tick per floated window; see the block above.
    ///
    /// <para>RENAMED FROM <c>SyncSharedBarTint</c> when the blue bar was dropped: it paints nothing
    /// any more. It writes <see cref="_shared"/>, which the badge's visibility, the release re-face
    /// gate and the remote pose easing all read.</para>
    /// </summary>
    internal void SyncSharedState(UIWindow? window)
    {
        if (_handle == null)
            return; // not built yet (or already torn down) — nothing to answer for
        // This IS SharedWindows.IsShared(window), expanded only because the LOG LINE has to name the
        // kind: a hardware report saying "the quest window showed no badge" must be readable against
        // a log that says which kind that window was and whether this client took part in its sync.
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
        //
        // THE FIELD IS WRITTEN EVERY TICK; ONLY THE LOG IS CHANGE-GATED. The badge's own SetActive is
        // change-gated in SyncBarVisibility, which is the one place that decides whether any piece of
        // this window's furniture is on the screen — putting a second SetActive here would be two
        // owners writing one flag ([[dont-win-a-write-war]]), and this one cannot see the empty-window
        // rule that method enforces.
        bool was = _shared;
        _shared = shared;
        if (shared == was)
            return;
        // HW-VERIFY
        VRLog.Note("WorldUI", $"SHARED WINDOW BADGE: '{_logName}' (game window '{window?.name ?? "?"}', " +
                              $"kind {kind}) now {(shared ? "SHOWS" : "hides")} the network badge in its " +
                              $"top-right corner — {(shared
                                  ? "every player in this room sees this window's state, so moving it is a shared act"
                                  : "this window is private to this client right now (its sync is off, or it is not a shared kind)")}."
                              + " SIZE: the mark resolves to "
                              + (_badgeSeatValid
                                  ? $"{_badgeSizePx:F0} host px on an edge, "
                                    + (float.IsNaN(_badgeGapToPlatePx)
                                        ? "scaled from BadgeFallbackPx because this window has no "
                                          + "close X to measure against - which is the STORY BOX case, "
                                          + "i.e. the badge's most frequent customer"
                                        : $"scaled as {BadgePlateFraction:F2} x the close X plate's LIVE "
                                          + "height, read off that plate's own RectTransform and never "
                                          + "copied from ModalCloseButton")
                                    + ". CLEARANCES, measured and not asserted: "
                                    + (float.IsNaN(_badgeGapToPlatePx)
                                        ? "no plate to clear"
                                        : $"{_badgeGapToPlatePx:F1} px to the close X plate's bottom edge")
                                    + $", {_badgeGapToRodPx:F1} px to the grab rod's top edge - a NEGATIVE "
                                    + "figure on either is an overlap this round created and neither is "
                                    + "acted on here, exactly as GRAB BAR CLEARS THE INK reports the rod's."
                                  : "no seat yet - SyncBadge has not run on this window since it was built, "
                                    + "so nothing is drawn and the size below is not yet resolved")
                              + " USER REPORT (2026-09-04): 'Das Symbol, dass anzeigt, dass es sich um "
                              + "ein Multiplayer-Fenster handelt ist zu klein - außerdem will ich das es "
                              + "blau blinkt (nicht zu extrem). Zusätzlich zieht es immer etwas nach wenn "
                              + "man das Fenster bewegt statt fix auf der Ebene zu sein.' The PULSE is "
                              + $"{BadgePulsePeriodSeconds:F1} s per breath ({1f / BadgePulsePeriodSeconds:F2} "
                              + $"Hz) at a {BadgePulseDepth * 100f:F0} % dim on the UNSCALED clock, so it "
                              + "keeps running behind a paused game; the LAG is answered by the separate "
                              + "SHARED WINDOW BADGE MOTION line, once per window.");
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

        // ModBuild 376 — A NEW ROD STARTS WITH NO SETTLED SIZE. This class is REUSED across a
        // window's closes and re-opens (Destroy drops the holder, EnsureFrame builds a new one), so
        // a settler that kept its value would hold the previous float's rod size for a whole settle
        // window against a rect it was never measured on. Reset here as well as in Destroy: the
        // build path is the one that matters, because it is the one a re-open takes.
        _widthSettle.Reset();
        _heightSettle.Reset();

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

        // THE ROD. Under the DRAWN pose, not the frame: the visible handle and the window it
        // belongs to must move as one object, and a glide moves the window. Its local numbers are
        // unchanged from the cube's — _visual carries the same localScale (the user grab factor) the
        // frame does, so SyncBar's frame-local metres still mean what they meant.
        //
        // overlay:true is what makes this the WINDOW rod rather than a board one: GrabBarVisual then
        // builds it on the bundled GloomhavenVR/Overlay shader (through Cards.PlayTray's accessor and
        // Core.BundleShaders — a bare Shader.Find on a GloomhavenVR/* name fails the build gate) and
        // forces _ZWrite ON there, so the handle still draws SOLID and occludes the menu behind it
        // while ZTest stays at the default LEqual and a hand held physically in front still occludes
        // it. That set is NOT repeated here; one owner, over there.
        //
        // Style Generic is the neutral window rod — dark oiled walnut with small aged-brass knobs,
        // chosen to sit on parchment without competing with the text on it. It is enum member 0, so
        // this call could not accidentally have picked up a board's material.
        GrabBarVisual bar = GrabBarVisual.Build(_visual, "Bar", GrabBarStyle.Generic,
                                                BarRadius, overlay: true);

        // ALL THREE RENDERERS RIDE THE ORDER LADDER, not one of them. The rod is a shaft and two
        // caps; registering only the shaft would sort it over the menu and leave both knobs behind
        // it, which is a bar with its ends bitten off. Registered after the renderers exist; the
        // order pass seats them immediately, so there is no unordered frame.
        for (int i = 0; i < bar.Renderers.Count; i++)
            CanvasConversion.RegisterOrderFollower(_panel, bar.Renderers[i], BarOrderOffset);
        if (!bar.Textured)
            VRLog.Warn("WorldUI", $"MODAL GRAB: '{_logName}' built its handle WITHOUT the wood strip " +
                                  "(the embedded texture did not decode — EmbeddedTexture has already " +
                                  "named it). The rod falls back to the flat brass the cube wore, so " +
                                  "the handle works and only the grain is lost.");
        _bar = bar;

        // LOST-MENU FIX, now round. The rod's laser target is a CapsuleCollider down its own axis
        // that SetLength keeps in step — see the BarColliderPad block for the box it replaces and
        // for why the split between laser and palm exists at all. It is a trigger on the mod render
        // layer, so the physics ray (RayInteractor) still ignores it, and it is NOT registered with
        // VRInteractables: the palm grab keeps using the generous frame zone below.
        Collider barCollider = bar.AttachLaserTarget();

        // Grab zone + shared grab core (collider BEFORE the handle: its OnEnable registers it).
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.isTrigger = true;
        _grabZone.size = new Vector3(0.25f, 0.05f, 0.05f);
        // THE TWEEN OWNS THE ROD'S POSE FROM HERE ON (2026-09-03, "es ploppt"): SyncBar and
        // SyncBarVisibility set TARGETS on it and WorldUIModule's GrabBarTween.Late step eases the
        // drawn rod, its laser capsule and this palm zone toward them. Built visible — the rod is
        // active from construction exactly as before, and the first SyncBarVisibility decides.
        _barTween = new GrabBarTween(bar, _grabZone, _logName, visible: true);
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        // THE SHAFT'S RENDERER, and the three pieces share ONE Material — so PanelGrabHandle's single
        // sharedMaterial.color write in OnGrabHighlight still lights the WHOLE rod, and its Init
        // seeds _barBaseColor from that same material, which is GrabBarVisual.RestingTint (white) for
        // a textured rod and the historic brass for an untextured one. Nothing in this class writes
        // that base any more: the resting colour is now a TINT MULTIPLIED onto the strip, so it must
        // stay white or the rod is drawn through a filter, and white is exactly what the handle
        // already read off the material it was handed.
        _handle.Init(this, bar.Renderer, "WorldUI", $"{_logName} menu");
        // LOST-MENU FIX: split laser vs palm — the far ray grabs ONLY the visible bar strip.
        _handle.SetBarCollider(barCollider);

        // THE SHARED-WINDOW MARK, built beside the rod but under the HOLDER, because its whole
        // placement is expressed in the HOST's authored pixels and SeatBadge puts it on the host's
        // own plane in LateUpdate (see THE BADGE'S SEAT block — this is the 2026-09-04 "zieht nach"
        // report). It travels with the window through a remote glide for the same reason it always
        // did: the host itself is written from the drawn pose.
        // Built for EVERY floated window and shown for none of them
        // until SyncBarVisibility sees _shared — participation can flip while the window stands (the
        // player may toggle the 3D world map with a story box open), so a badge decided at build time
        // would be a false statement for the rest of that window's life.
        BuildBadge();

        // Render-only mod layer — grabs/pokes route through the registries, not layers. AFTER the rod
        // and the badge exist, because it walks the tree it is given.
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
        //
        // ModBuild 376 — THE HEIGHT TERM GOES THROUGH THE SHARED SETTLE RULE (BarSizeSettle), the
        // same one SurfaceGrabBar runs and for the same reported artefact: a host rect that moves
        // and moves straight back changes the rod's thickness and its gap without the window really
        // changing. The clamp is applied AFTER the settle, so the rule owns the measurement and the
        // comfort bound above still owns the answer.
        //
        // THE VISIBILITY TERM. A change made while the rod is not drawn is adopted at once — see
        // BarSizeSettle's OFF-SCREEN CHANGES ARE FREE block for the closed bug (the post-reveal
        // jump) that depends on this, since every window is fitted BEFORE the reveal gate lets it
        // through. Both halves are read: the reveal gate's own flags on the panel, and the rod's own
        // GameObject, which SyncBarVisibility switches off for an empty window on the tick below.
        // (_bar is non-null from this method's own first line, and GrabBarVisual.Root is a
        // non-nullable Transform the visual owns for its whole life — the placement writes below
        // dereference it unguarded for the same reason.)
        bool onScreen = _panel != null && !_panel.RenderHidden && !_panel.OwnerRenderHidden
                        && _bar.Root.gameObject.activeInHierarchy;
        float panelHeight = _heightSettle.Apply(halfHeight * 2f / Mathf.Max(worldScale, 1e-4f),
            onScreen, _logName);
        // THE WIDTH THE ROD IS A FRACTION OF, AND WHERE ITS MIDDLE GOES — one call, one rule,
        // GrabBarLayout.SolveSpan, which carries the whole argument and the user's ruling that
        // wrote it (händlerbalken.jpg, 2026-09-05).
        //
        // ModBuild 447 — WHAT CHANGED, STATED AGAINST WHAT IT REPLACED. This method used to take
        // Min(inkWidth, frameWidth) and the ink's centre WHENEVER a union existed, and that is the
        // merchant's defect: 'UI Shop Item Window' paints a 1920x1080 shopkeeper plate and its union
        // spans x 461..977, because PanelInkBounds excludes full-frame plates by construction. The
        // rod came out 284 px long under the right-hand edge of a window the player sees as 1920 px
        // wide. SolveSpan asks the one question that separates that window from 'New Party display'
        // (a 1988 px TRANSPARENT frame around a 328 px column, which is the window the union was
        // written for): does this window paint a plate across its own frame? The frame's width and
        // centre if it does, the union's if it does not. Every window in the ModBuild 446 log then
        // lands on the content fit's own DRAWN CONTENT answer for the same window.
        //
        // THE VERTICAL TERM IS UNTOUCHED AND STAYS THE UNION'S — see the placement block below.
        //
        // ModBuild 376 — AND THE WIDTH STILL GOES THROUGH THE SHARED SETTLE RULE (BarSizeSettle).
        // One settled width feeds BOTH fractions, so the drawn rod and the palm zone can never
        // disagree about how wide the window is, and both of SolveSpan's branches are covered by
        // that one gate rather than by a second one: whichever branch answers, it is this scalar
        // that moves.
        bool inkUsable = _inkValid && unit > 1e-9f;
        GrabBarLayout.Span span = GrabBarLayout.SolveSpan(
            frameWidth: width,
            inkValid: inkUsable,
            inkWidth: _inkRect.width * unit,
            inkCentre: _inkRect.center.x * unit,
            framePainted: _inkFullFrame);
        float sourceWidth = _widthSettle.Apply(span.Width, onScreen, _logName);

        // ONE CALL FOR EVERY DIMENSION THE ROD HAS, shared with SurfaceGrabBar and CombatLogSurface
        // (GrabBarLayout). The proportion, the gap, the uniform root scale, the drawn shaft diameter,
        // the two fractions and the MinBarWidth floor were eight lines here and eight identical lines
        // over there; they are one function now and no number changed. The settle rule still runs
        // BEFORE it — the rule decides WHICH size the bar should be, this decides what that size
        // means for the rod — and the floor still lives outside the settle rule for the reason
        // GrabBarLayout.Solve states.
        GrabBarLayout.Rod rod = GrabBarLayout.Solve(sourceWidth, panelHeight, worldScale);
        float gap = rod.Gap;
        float rodScale = rod.Scale;
        float thickness = rod.Thickness;
        float zoneDepth = rod.ZoneDepth;
        float barWidth = rod.BarWidth;
        float zoneWidth = rod.ZoneWidth;

        // THE FRAME-BASED PLACEMENT — byte-for-byte what shipped through ModBuild 235, and still the
        // answer whenever the ink cannot be measured (see the degenerate branch below).
        float x = 0f;
        float y = -(halfHeight + gap);

        if (inkUsable)
        {
            // BELOW THE LOWEST DRAWN GRAPHIC. hostRect.yMin x unit is exactly -halfHeight for a
            // pivot-centred host, so a window whose ink stays inside its frame is unchanged; the Min
            // is what keeps the bar from ever RISING into the frame when the ink is short.
            //
            // ModBuild 447 — THIS TERM IS STILL THE UNION'S, not the frame's. The horizontal rule
            // above may now answer with the frame; this one may not, and the reason is the window
            // that wrote it: 'New Party display' draws a Rewards row at host-local y=-913 against a
            // frame that ends at -540, and a bar placed on the frame lands on top of it
            // (quest_überlap.jpg, ModBuild 236).
            //
            // ModBuild 449 — AND THE PLATE'S OWN BOTTOM IS THE THIRD TERM, because 447's last
            // sentence here was WRONG. It read: "A full-frame plate cannot change this number
            // anyway — its bottom edge IS hostRect.yMin, which the Min already covers — so the two
            // rules cannot fight over it." That is a property of a plate whose aspect matches its
            // frame, not of a plate. The plate test is a GREATER-OR-EQUAL on both axes, so on the
            // co-player's 2580x1080 canvas the merchant's 16:9 shopkeeper artwork is stretched to
            // the frame's WIDTH, stands 1451 px tall and hangs 371 px BELOW a 1080 px frame — the
            // fit measured it as DRAWN CONTENT 2597x1451 px at (8,-186), y -911..540, on the same
            // tick this union reported its bottom at -540 and this line seated the rod at -574.
            // 337 px of merchant were still drawn under the handle: item 15, "der Greifbalken mitten
            // im Händlerbild ... das tritt bei mir (Host) nicht auf", and the reason it did not
            // happen on the host is that his 1920x1080 canvas is the artwork's own aspect, so his
            // plate ends exactly at hostRect.yMin and this Min changes nothing for him.
            //
            // POSITION IS UNGATED, and that is deliberate — see BarSizeSettle. A handle that lags
            // its own window hangs off the side of it, which is a worse artefact than the one the
            // settle rule was built for and is not the one that was reported.
            y = Mathf.Min(Mathf.Min(hostRect.yMin, _inkRect.yMin), _inkPlateBottom) * unit - gap;
        }
        // CENTRED WHERE SolveSpan SAYS: on the frame for a window that paints its whole frame, on
        // the ink for a window whose frame is transparent around what it draws. Zero in both of the
        // cases that used to reach here with zero (no union at all, and a union that fills its
        // frame), so the windows this round is not about do not move by a float.
        x = span.Centre;

        // 2026-09-03 ("es ploppt") — THESE ARE TARGETS NOW, NOT WRITES. The rod's root position,
        // its uniform scale, its length (barWidth is in frame-local metres; SetLength wants the rod's
        // OWN local metres under a root scaled by rodScale, so it is divided back out and the drawn
        // end-to-end length is barWidth exactly) and the palm zone's box all go to GrabBarTween,
        // which eases the drawn rod toward them in LateUpdate. The palm zone still rides WITH the
        // visible handle — it is not the hit rect (that contract, "always contains the host rect",
        // belongs to the conversion and is untouched) — it just rides with the PRESENTED handle.
        //
        // TWO REASONS TO SNAP INSTEAD, both printed on the tween's line: a hand on the bar (the
        // tween is for the mod's own re-seats, never a lag against the player's carry) and a rod
        // that is off the screen (nobody can see the transition, and holding the old value only
        // makes the presented pose untrue when the rod appears — the same argument BarSizeSettle
        // makes for its off-screen branch above).
        bool carried = _handle != null && _handle.IsGrabbed;
        string? snapWhy = carried
            ? "a hand is carrying the window"
            : !onScreen ? "the rod is off the screen (behind the reveal gate or withheld)" : null;
        _barTween?.SetTarget(new Vector3(x, y, 0f), rodScale, barWidth / rodScale,
                             new Vector3(zoneWidth, zoneDepth, zoneDepth), snapWhy,
                             "GrabbableModal.SyncBar — width and centre from " + span.Source);

        SyncBarVisibility();

        // THE CLOSE X RIDES THE SAME UNION AS THE BAR, on the same tick, from the same committed
        // rectangle — so the two pieces of chrome can never disagree about where the window is.
        SyncCloseX(hostRect);
        // AFTER the X, because the badge is seated off the plate whenever there is one, and off the
        // answer SyncCloseX just stored rather than off a second derivation of it. The rod's drawn
        // TOP EDGE goes with it, in host px, so the badge's clearance to the handle is MEASURED from
        // the same two numbers that placed the handle rather than re-derived beside them — the
        // badge grew this round (2026-09-04, "zu klein") and this is the check that says it still
        // clears, in the same shape as GRAB BAR CLEARS THE INK.
        SyncBadge(hostRect, unit, (y + thickness * 0.5f) / unit);

        if (_inkReportDue || _inkFallbackDue)
        {
            // The falsifier is handed the two derived numbers rather than the terms to re-derive them
            // from, so a report can never disagree with the placement it is describing. The intended
            // gap is to the bar's TOP EDGE: BarGapMeters is documented as the gap to the bar's CENTRE,
            // and half the thickness of the bar lies above that centre.
            //
            // ModBuild 447 — AND THE SPAN GOES WITH THEM, for exactly the same reason. The judged
            // horizontal term used to be "the bar's centre must be the ink's centre"; that claim is
            // no longer the one the code makes, and an instrument left asserting it would print NOT
            // ACHIEVED for every window this round repaired ([[instrument-shipped-and-lying]]).
            // Handing over the ANSWER rather than the terms is what stops the report and the
            // placement from being two derivations of one rule.
            ReportBarPlacement(hostRect, unit, barWidth, thickness,
                intendedTopGapPx: (gap - thickness * 0.5f) / Mathf.Max(unit, 1e-9f),
                mmPerPx: unit / Mathf.Max(worldScale, 1e-4f) * 1000f,
                span: span);
        }
    }

    /// <summary>
    /// ModBuild 243 — <b>THE HANDLE IS ON THE SCREEN EXACTLY WHILE THE WINDOW DRAWS SOMETHING.</b>
    /// User report, verbatim: <i>"Auch dauert es immer kurz bis dass der Balken verschwindet wenn das
    /// Fenster leer ist, ich möchte gerne dass es instant reagiert."</i> The whole rule, and why the X
    /// is deliberately NOT included in it, is on the <see cref="InkSettleFrames"/> block.
    ///
    /// <para>THE GAMEOBJECT AND NOT THE RENDERER, and that is not a style choice.
    /// <c>CanvasConversion.HideTree</c> records only ENABLED renderers under a render root and
    /// <c>GetComponentsInChildren(false, …)</c> skips inactive GameObjects entirely — so a bar this
    /// method has switched off is invisible to the reveal gate's restore set and cannot be switched
    /// back on behind this class's back. Driving <c>MeshRenderer.enabled</c> instead would be two
    /// owners writing one flag ([[dont-win-a-write-war]]). The bar object carries the laser collider
    /// (<c>PanelGrabHandle.SetBarCollider</c>), so it goes with it; the palm zone lives on the frame
    /// object and is disabled here beside it.</para>
    ///
    /// <para>THE ROD IS ONE GAMEOBJECT WITH THREE UNDER IT, and this switches the ROOT — so the
    /// shaft, both caps and the laser capsule (which lives on the root) go together. There is no
    /// state in which two thirds of a handle is on the screen.</para>
    ///
    /// <para>AND THE SHARED-WINDOW BADGE RIDES THE SAME ANSWER, ANDed with
    /// <see cref="_shared"/>. It is the same rule for the same reason: a mark on a window that is
    /// drawing nothing is a mark floating in the room, and the user's complaint that started this
    /// method ("ich möchte gerne dass es instant reagiert") was about exactly that. The X is
    /// deliberately still exempt — it is the rescue for a window the player can no longer see —
    /// and the badge is not a rescue.</para>
    ///
    /// <para>NEVER MID-GRAB. Pulling a collider out from under a live grab strands the hand's claim on
    /// an object it can no longer let go of, so a held handle stays until it is released.</para>
    ///
    /// <para>ModBuild 378 — <b>AND THE SECOND TERM, WHICH IS A DIFFERENT EDGE.</b> The rule above
    /// takes a handle OFF a window that has gone dark. It cannot answer the window that was NEVER
    /// on the screen: the rod is built at the reveal edge and drawn the instant the reveal gate
    /// unhides the panel, and the ink walk needs a sample plus a confirming sample to catch up.
    /// User report 2026-09-03, in the map room's give-away-an-item flow: <i>"für unter eine Sekunde
    /// ein lange greifbalken erschienen und sofort wieder verschwunden. Keine Animation wie früher
    /// aber der greifbalken. Wenn kein Fenster inhalt hat soll neben der Animation auch kein
    /// Greifbalken erscheinen."</i> The dust was already withheld by ModBuild 374; the rod now rides
    /// the SAME stored verdict (<c>ModalFallback.AppearStillOwed</c>), so the two can never
    /// disagree. It is a separate flag from <see cref="_barHiddenForEmpty"/> and both stay live: a
    /// window that never draws is withheld, a window that draws and then stops is taken off, and a
    /// window that does both in turn runs both.</para>
    /// </summary>
    private void SyncBarVisibility()
    {
        if (_bar == null || _grabZone == null)
            return;
        if (_inkValid)
            _barHiddenForEmpty = false; // the window is drawing again — the handle comes straight back

        // ModBuild 378 — THE ROD IS WITHHELD ON THE SAME TERM THAT WITHHOLDS THE DUST. The whole
        // argument, and why the flag rather than a test of this class's own, is on
        // ModalFallback.AppearStillOwed. It is read every tick and it measures nothing: it is a
        // stored verdict the liveness rule owns, so the two pieces of chrome cannot disagree about
        // whether this window has content.
        bool owed = ModalFallback.AppearStillOwed(_panel, out string owedWhy);
        NoteWithholdChange(owed, owedWhy);

        // The GRAB SUPPRESSES THE HIDE, it does not CANCEL it: clearing the intent here would leave a
        // handle standing on an empty window for the rest of its life, because the empty run that set
        // it only counts while a committed rectangle still exists.
        //
        // THE WITHHOLD IS UNDER THE SAME GRAB EXEMPTION, and that costs nothing: a rod that has never
        // been on the screen cannot be in a hand, so this clause can only ever be reached by the
        // going-dark rule it was written for. It is ANDed rather than given a branch of its own so
        // there is exactly one expression in this class that decides whether the handle is drawn.
        bool visible = (!_barHiddenForEmpty && !owed) || (_handle != null && _handle.IsGrabbed);
        // 2026-09-03 ("es ploppt") — THE VISIBILITY IS A TARGET TOO. A rod that returns grows from
        // zero at its place; a rod that goes dark shrinks to zero and only THEN leaves the screen.
        // The GameObject switch below is still this class's, on the tween's Shown answer, for the
        // reasons the doc comment above gives (the reveal gate's restore set cannot see an inactive
        // root). Three edges are NOT eased, each printed as the reason on the tween's line: a hand
        // on the bar; a window behind the reveal gate (nobody can see it); and the ModBuild 378
        // withhold of a window that has NEVER drawn — CompleteReveal asks for that one in LateUpdate
        // on the very frame the renderers come back, and a 150 ms shrink there would draw the rod on
        // an empty window for exactly the frames that round removed. Only a window that drew and
        // then went dark (_barHiddenForEmpty) shrinks.
        bool shown = visible;
        if (_barTween != null)
        {
            bool carried = _handle != null && _handle.IsGrabbed;
            bool gateHidden = _panel != null && (_panel.RenderHidden || _panel.OwnerRenderHidden);
            string? snapWhy = carried
                ? "a hand is carrying the window"
                : gateHidden ? "the window is behind the reveal gate"
                : (!visible && owed) ? "the window has never drawn (withheld at its reveal edge)"
                : null;
            _barTween.SetVisible(visible, snapWhy,
                visible ? "GrabbableModal.SyncBarVisibility (the window draws again)"
                        : owed ? "GrabbableModal.SyncBarVisibility (withheld: appear still owed)"
                               : "GrabbableModal.SyncBarVisibility (the window went dark)");
            shown = _barTween.Shown;
        }
        if (_bar.Root.gameObject.activeSelf != shown)
            _bar.Root.gameObject.SetActive(shown);
        if (_grabZone.enabled != visible)
            _grabZone.enabled = visible;

        if (_badge != null)
        {
            bool badgeVisible = visible && _shared;
            if (_badge.gameObject.activeSelf != badgeVisible)
                _badge.gameObject.SetActive(badgeVisible);
        }
    }

    /// <summary>
    /// ModBuild 378 — RE-DECIDE WHETHER THE ROD IS DRAWN, RIGHT NOW, from outside the follow tick.
    ///
    /// <para>There is exactly one caller and it exists to close a one-frame seam.
    /// <c>CanvasConversion.CompleteReveal</c> runs in LATE UPDATE: it re-enables every renderer it
    /// recorded — the rod's three among them, because the grab holder is registered as an extra
    /// render root — and only then asks <c>ModalFallback.PlayAppearOrDefer</c> whether this float
    /// has anything drawable. The follow tick that would notice the answer is the NEXT frame's
    /// Update, and the frame in between is rendered. Without this call the rod is drawn for that one
    /// frame on a window that has nothing in it, which is a smaller version of the very artefact
    /// this round removes.</para>
    ///
    /// <para>It re-runs the ONE expression that decides the rod's visibility rather than writing the
    /// GameObject itself, so there is still a single owner of that flag and no second rule to keep in
    /// step. Safe before the frame is built (the method returns on its own null guard) and safe to
    /// call twice in a frame (it is idempotent — it writes only on a change).</para>
    /// </summary>
    internal void RefreshBarVisibility() => SyncBarVisibility();

    /// <summary>
    /// ModBuild 378 — the falsifier for the withhold, on the EDGE and never per frame.
    ///
    /// <para>It reports the two counts as well as the verdict, deliberately. A change-gated line
    /// whose reason is a constant string prints once for a window and then looks exactly like an
    /// instrument that stopped running; the running totals are what let a reader tell "withheld once
    /// and released" from "withheld once and never released" without correlating two log lines by
    /// eye. <c>withholds - releases == 1</c> is a rod that is off the screen right now.</para>
    ///
    /// <para>It asserts only what it can see: the flag it was handed, the reason string the owner of
    /// that flag wrote, and its own counters. It does not claim the window is empty — that is the
    /// liveness rule's verdict and the liveness rule's line.</para>
    /// </summary>
    private void NoteWithholdChange(bool owed, string owedWhy)
    {
        if (owed == _barWithheldForOwedAppear)
            return;
        _barWithheldForOwedAppear = owed;
        if (owed)
        {
            _barWithholds++;
            // KEPT FOR THE RELEASE LINE. AppearStillOwed writes `why` only when it answers TRUE, so
            // on the falling edge the caller hands us an EMPTY string and the release half would
            // read "it was ." — a sentence that names no term at all, on the one line whose whole
            // job is to name the term. The reason a rod came BACK is the reason it went away, so it
            // is held here rather than re-derived from a state that has already cleared.
            _barWithholdWhy = owedWhy;
        }
        else
        {
            _barWithholdReleases++;
            if (owedWhy.Length > 0)
                _barWithholdWhy = owedWhy;
        }
        owedWhy = _barWithholdWhy.Length > 0
            ? _barWithholdWhy
            : "(the withholding term was not recorded — this instance was rebuilt while the rod "
              + "was off the screen)";
        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"MODAL GRAB BAR WITHHELD: '{_logName}' — the grab bar is "
            + (owed
                ? "NOT BEING DRAWN, and neither is its laser capsule, its palm grab zone or its "
                  + "shared-window badge. THE TERM THAT WITHHELD IT: "
                : "BACK ON THE SCREEN with its laser capsule, its palm grab zone and its "
                  + "shared-window badge. THE TERM THAT WITHHELD IT HAS CLEARED: it was ")
            + owedWhy
            + (owed
                ? ". It comes back on the same edge that releases the owed materialise APPEAR — the "
                  + "first paint, or the wake from dormancy — so the rod and the dust arrive "
                  + "together. USER REPORT (2026-09-03): 'Wenn kein Fenster inhalt hat soll neben "
                  + "der Animation auch kein Greifbalken erscheinen.'"
                : ". The window is drawing, so it is movable again.")
            + $" COUNTS OVER THIS WINDOW'S LIFE: {_barWithholds} withhold(s), "
            + $"{_barWithholdReleases} release(s) — a difference of 1 is a rod withheld right now, "
            + "and a difference that never returns to 0 is a float that never drew at all, which "
            + "the liveness rule's own verdict then owns. THE GOING-DARK RULE IS A SEPARATE EDGE "
            + "and is unchanged. NOTHING WAS WRITTEN TO THE GAME: the only objects switched are the "
            + "mod's own chrome.");
    }

    /// <summary>
    /// Build the shared-window badge under the HOLDER, beside the rod. Called once, from
    /// <see cref="EnsureFrame"/>; the whole design and the user's ruling are on the SHARED-WINDOW
    /// MARK block, and why it hangs under the holder rather than under the drawn pose is on THE
    /// BADGE'S SEAT block. Never throws: a window that cannot have a badge must still be a window.
    /// </summary>
    private void BuildBadge()
    {
        if (_holder == null || _panel == null)
            return;

        // Clamp, not Repeat: a badge is a single tile and Repeat would let bilinear filtering fetch
        // the opposite edge at the border. EmbeddedTexture caches the answer INCLUDING a null, so a
        // missing resource costs one warning for the process and not one per window.
        Texture2D? tex = EmbeddedTexture.Get(BadgeResource, linear: false, TextureWrapMode.Clamp);
        if (tex == null)
            return; // already named over there. A white square in every corner is not an improvement.

        // Through the accessor, never a bare Shader.Find on a GloomhavenVR/* name — that fails the
        // build gate, and for good reason: Shader.Find only sees shaders something has already
        // LOADED, and GloomhavenVR/Overlay is referenced by runtime C# alone. The two fallbacks are
        // built-in names, which Shader.Find CAN resolve; both are alpha-blended and unlit, so a badge
        // drawn through either is the same picture with a fixed ZWrite it does not need to change.
        Shader? shader = Cards.PlayTray.OverlayShader()
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("UI/Default");
        if (shader == null)
        {
            VRLog.Warn("WorldUI", $"MODAL GRAB: '{_logName}' has no usable shader for the shared-window "
                                  + "badge; the window is unaffected and the badge is skipped.");
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "SharedBadge";
        // Inert by construction. DestroyImmediate rather than Destroy so a collider sweep running
        // later in THIS SAME FRAME can never meet it — a deferred destroy is still visible to one
        // (the pattern RemoteEmptyFanHint.BuildPlate records for the same reason).
        Collider? primitiveCollider = go.GetComponent<Collider>();
        if (primitiveCollider != null)
            Object.DestroyImmediate(primitiveCollider);
        // UNDER THE HOLDER, NOT UNDER `_visual` (2026-09-04 — see THE BADGE'S SEAT block). The
        // holder is the identity-pose, identity-scale root this class re-asserts every tick, so the
        // badge inherits nothing at all: its whole world pose is written by SeatBadge from the HOST,
        // in the same LateUpdate step and immediately after the statement that finalises the host.
        // `_visual` is deliberately allowed to lag the grab frame, which is exactly what a mark
        // seated in host pixels must not do. It is still inside the holder tree, so the render root
        // registration, the layer pass and the orphan sweep all still see it, unchanged.
        go.transform.SetParent(_holder, worldPositionStays: false);

        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        // ZWRITE STAYS OFF, AND THIS IS THE ONE PLACE THE BADGE DELIBERATELY DOES NOT COPY THE BAR.
        // The rod forces _ZWrite = 1 because it is opaque and must READ solid against the menu behind
        // it. The badge is a keyed-alpha glyph drawn ON TOP OF the window's own content: writing
        // depth through its transparent margin would stamp a 24 px box of "everything behind this is
        // deleted" into whatever lies behind that corner. That is not a hypothetical — it is exactly
        // the defect ModalCloseButton's XOrderOffset block records having removed from the X, one
        // element smaller. GloomhavenVR/Overlay already defaults to _ZWrite 0 with SrcAlpha /
        // OneMinusSrcAlpha and ZTest LEqual, which is precisely what is wanted, so nothing is set
        // here at all and the default is the documented behaviour rather than an accident.
        // BUILT AT THE PULSE'S RESTING TINT, which is the value phase 0 produces — a badge that has
        // just been built and a badge at the top of its breath are the same picture, so there is no
        // first frame in another colour. PulseBadge owns this material's colour from here on, and it
        // is this window's OWN instance: nothing else can be tinted by it.
        var material = new Material(shader) { color = BadgeTint };
        if (material.HasProperty("_MainTex"))
            material.mainTexture = tex;
        mr.sharedMaterial = material;
        _badgeMaterial = material;

        // Over the depthless menu canvas by ORDER, the same way the bar and the X are (see
        // BadgeOrderOffset). A MeshRenderer and a Canvas both sort by sortingLayer then sortingOrder,
        // which is why one ladder serves both.
        CanvasConversion.RegisterOrderFollower(_panel, mr, BadgeOrderOffset);

        // OFF until SyncBarVisibility says otherwise. A private window never draws it, and because it
        // is an INACTIVE GameObject the reveal gate's HideTree cannot record it either, so there is
        // no path by which it comes back on behind this class's back.
        go.SetActive(false);
        _badge = go.transform;
    }

    /// <summary>
    /// Seat the shared-window badge against the SAME committed rectangle the rod and the close X are
    /// seated from, on the same tick, so the three pieces of chrome can never disagree about where
    /// the window is. Placement only — <see cref="SyncBarVisibility"/> owns whether it is drawn.
    ///
    /// <para><b>BELOW THE X, NOT INBOARD OF IT, AND WHY.</b> The user asked for the mark "rechts oben
    /// in der Ecke ... in das Fenster", which is the corner the close X has always occupied. Inboard
    /// (to the X's LEFT) is the one placement that is actively wrong: <c>ModalCloseButton</c> puts
    /// the plate's whole width CLEAR of the ink's right edge on purpose, because the ink is a tight
    /// box around what the window paints and anything inset into it lands on a drawn row. The space
    /// immediately to the X's left is therefore the window's content. Directly BELOW the plate, on
    /// the plate's own right edge, is the same clear column the X already stands in — outside the ink
    /// for the whole height of the window when the ink is narrow, and inside the frame's right margin
    /// when the clamp bites, which is the identical compromise the X itself makes and no worse.</para>
    ///
    /// <para><b>AND THEY REALLY WOULD HAVE COLLIDED.</b> This is not a precaution taken on principle.
    /// When the ink reaches the frame's right edge — the ordinary case for a window whose content
    /// fills it — <c>PlaceAgainstInk</c> CLAMPS the plate to the frame's own corner, and a badge
    /// seated at the ink's top-right corner clamps to the same place. The two rectangles then
    /// overlap. Only the narrow-ink case separates them by itself.</para>
    ///
    /// <para><b>THE PLATE'S SIZE IS READ, NEVER COPIED.</b> <c>ModalCloseButton.ButtonSizePx</c> is
    /// private to that class and it owns the plate's geometry; this reads the height off the plate's
    /// own <c>RectTransform</c>, so a change over there moves the badge with it. Two hand-kept copies
    /// of one boundary is how the caps end up painted in shaft material, and this project has paid
    /// for that shape of drift often enough. Since 2026-09-04 that read decides the badge's SIZE as
    /// well as its seat: the edge is <see cref="BadgePlateFraction"/> times the live plate height
    /// (<see cref="BadgeFallbackPx"/> where there is no plate), because "zu klein" is a judgement
    /// against the chrome standing beside it and not against a number.</para>
    ///
    /// <para><b>IT RESOLVES A SEAT; IT DOES NOT WRITE A TRANSFORM.</b> This runs in Update and every
    /// quantity it computes is in the host's authored pixels, whose world meaning is not settled
    /// until <see cref="LateSyncHost"/> has written the host. <see cref="SeatBadge"/> does the
    /// placing, in that same LateUpdate step and immediately after that write. THE BADGE'S SEAT
    /// block has the user report this answers and the three reasons the badge is not simply
    /// parented under the host.</para>
    ///
    /// <para>THE DEGENERATE PATH IS NOT DEAD CODE, and that is worth stating because the brief for
    /// this change assumed it was. A converted window has a close X only if
    /// <c>ModalFallback.8.Convert</c> attached one, and the STORY BOX is explicitly excluded from
    /// that ("Weiterhin darf dieses Story-Fenster kein 'x' haben, da man durchklicken muss") — while
    /// being <c>SharedWindowKind.ScenarioStory</c>, i.e. the most frequently SHARED window there is.
    /// The badge's most important customer is precisely a window with no X. That branch therefore
    /// seats it at the committed rectangle's own top-right corner, inset by
    /// <see cref="BadgeInsetPx"/>, with the same upward clamp the X uses and for the same reason: an
    /// ink union may reach far ABOVE the frame (the ModBuild 241 options window measures
    /// <c>y -540..1287</c> against a frame that ends at 540), and a mark seated at <c>ink.yMax</c>
    /// would hang off in the room.</para>
    /// </summary>
    private void SyncBadge(Rect hostRect, float unit, float rodTopPx)
    {
        if (_badge == null || unit <= 1e-9f)
        {
            _badgeSeatValid = false;
            return;
        }

        // THE SIZE IS READ OFF THE PLATE, NEVER COPIED FROM IT (see BadgePlateFraction). The HEIGHT
        // rather than the width, because the plate is square today and the height is the dimension
        // the badge's own downward growth is measured against; a plate that ever became a rectangle
        // would move the badge with its short axis, which is the safe direction.
        float sizePx = BadgeFallbackPx;
        if (_closeXPlaced && _closeX != null && _closeX.rect.height > 1f)
            sizePx = _closeX.rect.height * BadgePlateFraction;

        // The badge's own TOP-RIGHT corner, in the host's authored px.
        float cornerX = hostRect.xMax - BadgeInsetPx;
        float cornerY = hostRect.yMax - BadgeInsetPx;
        if (_inkValid && _inkRect.width > 0f && _inkRect.height > 0f)
        {
            cornerX = Mathf.Clamp(_inkRect.xMax - BadgeInsetPx, hostRect.xMin + sizePx, cornerX);
            cornerY = Mathf.Clamp(_inkRect.yMax - BadgeInsetPx, hostRect.yMin + sizePx, cornerY);
        }
        _badgeGapToPlatePx = float.NaN;
        if (_closeXPlaced && _closeX != null)
        {
            cornerX = _closeXPlacement.Corner.x;
            // The lower clamp is a survival floor for a SHORT window, the same one PlaceAgainstInk
            // gives the plate: a window barely taller than its own close button would otherwise have
            // its badge hanging below its bottom edge, out in the room. It never bites on a window
            // tall enough to hold both marks, which is every window this has been reasoned about on.
            float underPlate = _closeXPlacement.Corner.y - _closeX.rect.height - BadgeGapPx;
            cornerY = Mathf.Max(underPlate, hostRect.yMin + sizePx);
            // MEASURED, NOT ASSERTED. It is BadgeGapPx whenever the survival clamp does not bite,
            // and a NEGATIVE number if that clamp ever pushes the badge back up into the plate --
            // which is the one way this placement could collide, and the growth this round adds is
            // the one thing that could make it bite. The SHARED WINDOW BADGE line says which.
            _badgeGapToPlatePx = (_closeXPlacement.Corner.y - _closeX.rect.height) - cornerY;
        }

        // AND THE CLEARANCE TO THE ROD, on the same terms and in the same units. rodTopPx is the
        // drawn shaft's TOP edge in these host px: the rod is placed BELOW hostRect.yMin (or the
        // ink's, whichever is lower) minus a gap, and the badge is floored AT hostRect.yMin, so this
        // is expected to be comfortably positive — but "expected to be" is what the ink round cost
        // a build for, so it is measured and printed instead of argued.
        _badgeGapToRodPx = (cornerY - sizePx) - rodTopPx;

        // RESOLVED HERE, WRITTEN IN LATEUPDATE. This method runs in Update (SyncBar, from Tick) and
        // every quantity above is in the HOST's authored pixels; the host's final world pose is not
        // known until LateSyncHost has run. So the seat is STORED and SeatBadge places it — see THE
        // BADGE'S SEAT block for the "zieht nach" report this answers.
        _badgeSeatPx = new Vector2(cornerX - sizePx * 0.5f, cornerY - sizePx * 0.5f);
        _badgeSizePx = sizePx;
        _badgeSeatValid = true;
    }

    /// <summary>
    /// <b>PUT THE BADGE ON THE HOST'S OWN PLANE — the LateUpdate half of the seat.</b> Called from
    /// <see cref="LateSyncHost"/> immediately after <see cref="SyncHostToFrame"/>, i.e. in the same
    /// phase as, and after, the one statement that writes the pose this mark is measured in. The
    /// full argument, the user's words and the three refusals of the obvious "just parent it under
    /// the host" are on THE BADGE'S SEAT block.
    ///
    /// <para>Never throws and never writes game state: the only objects touched are the mod's own
    /// quad and the mod's own material.</para>
    /// </summary>
    private void SeatBadge(Transform host)
    {
        // A private window's badge is an INACTIVE GameObject: it needs no pose and no pulse, and
        // skipping it here is what keeps a single-player session paying nothing for this feature.
        if (_badge == null || !_badge.gameObject.activeSelf)
            return;

        // The pulse runs on the SEAT's cadence and not on the follow tick's, for one reason: this is
        // the step that is guaranteed to run every frame for every live holder, and a mark that
        // breathed at the follow tick's cadence would stutter whenever that tick did.
        PulseBadge();

        if (!_badgeSeatValid)
            return;
        // World metres per authored host px — the host's own lossy scale, which is exactly what
        // SyncHostToFrame wrote one line up and therefore the same unit the seat is expressed in.
        float pxToWorld = host.lossyScale.x;
        if (pxToWorld <= 1e-9f)
            return;

        Vector3 want = host.TransformPoint(new Vector3(_badgeSeatPx.x, _badgeSeatPx.y, 0f));
        SampleBadgeSeat(host, want, pxToWorld);

        // Z = 0 IN THE HOST'S FRAME, exactly as before: coplanar with the window's content, with the
        // draw decided by ORDER (BadgeOrderOffset) and not by a depth offset — the ruling
        // ModalCloseButton arrived at after a viewer nudge alone failed to decide it. "Fix auf der
        // Ebene" is now true by construction instead of by two transforms happening to agree.
        _badge.SetPositionAndRotation(want, host.rotation);
        // A quad is 1x1 in its own local units, so this is a size and not a distortion; z stays 1
        // because there is no z to scale. The parent is the holder, whose identity scale this class
        // re-asserts every tick, so a LOCAL scale here IS a world size.
        float sizeWorld = _badgeSizePx * pxToWorld;
        _badge.localScale = new Vector3(sizeWorld, sizeWorld, 1f);
    }

    /// <summary>
    /// Advance the blue pulse one frame and write the badge's tint. Two float operations and one
    /// colour write on a material this window owns outright — never a material per frame, and never
    /// a shader keyword (a MaterialPropertyBlock cannot set one in this codebase). Both numbers, and
    /// why the clock is unscaled, are on THE BLUE PULSE block.
    /// </summary>
    private void PulseBadge()
    {
        if (_badgeMaterial == null)
            return;
        const float twoPi = Mathf.PI * 2f;
        _badgePulsePhase += Mathf.Max(Time.unscaledDeltaTime, 0f) * (twoPi / BadgePulsePeriodSeconds);
        if (_badgePulsePhase >= twoPi)
            _badgePulsePhase -= twoPi * Mathf.Floor(_badgePulsePhase / twoPi);
        // 0 at phase 0 and 1 at the trough, so the DEPTH scales this OUTPUT and never the phase step
        // above it, and the resting tint is exactly what a freshly built badge already wears.
        float dim = BadgePulseDepth * (0.5f - 0.5f * Mathf.Cos(_badgePulsePhase));
        float b = 1f - dim;
        _badgeMaterial.color = new Color(BadgeTint.r * b, BadgeTint.g * b, BadgeTint.b * b, BadgeTint.a);
    }

    /// <summary>
    /// Sample the two distances THE BADGE'S OWN FALSIFIER block defines, while the window is moving,
    /// and emit the one line when the budget closes. Called from <see cref="SeatBadge"/> BEFORE the
    /// write, because a measurement taken after it would be subtracting a value from itself.
    /// </summary>
    private void SampleBadgeSeat(Transform host, Vector3 want, float pxToWorld)
    {
        Vector3 hostPos = host.position;
        bool moving = _badgeLastHostPosValid
                      && (hostPos - _badgeLastHostPos).sqrMagnitude
                         > BadgeMovingEpsilonMeters * BadgeMovingEpsilonMeters;
        _badgeLastHostPos = hostPos;
        _badgeLastHostPosValid = true;
        if (!moving || _badgeReported || _badge == null)
            return;

        float seatPx = Vector3.Distance(_badge.position, want) / pxToWorld;
        float proxyPx = _visual != null ? Vector3.Distance(_visual.position, hostPos) / pxToWorld : 0f;
        _badgeSamples++;
        _badgeSeatSumPx += seatPx;
        _badgeProxySumPx += proxyPx;
        if (seatPx > _badgeSeatWorstPx)
            _badgeSeatWorstPx = seatPx;
        if (proxyPx > _badgeProxyWorstPx)
            _badgeProxyWorstPx = proxyPx;
        if (_badgeSamples < BadgeSampleCap)
            return;
        // THE LATCH IS WRITTEN HERE, IN THE MECHANISM, and not inside the report: a diagnostic that
        // writes a field the mechanism reads is a diagnostic that cannot be switched off
        // ([[a-write-inside-a-logger]]).
        _badgeReported = true;
        ReportBadgeSeat();
    }

    /// <summary>Write the one SHARED WINDOW BADGE MOTION line. Reads only — the latch that stops the
    /// sampling is set by its caller, for the reason stated there.</summary>
    private void ReportBadgeSeat()
    {
        float seatMean = _badgeSamples > 0 ? _badgeSeatSumPx / _badgeSamples : 0f;
        float proxyMean = _badgeSamples > 0 ? _badgeProxySumPx / _badgeSamples : 0f;
        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"SHARED WINDOW BADGE MOTION: '{_logName}' over {_badgeSamples} MOVING frame(s) - "
            + $"SEAT offset mean {seatMean:F2} px, worst {_badgeSeatWorstPx:F2} px (how far the "
            + "badge was from its seat when the frame's final host pose landed, sampled BEFORE this "
            + "frame's write; on the fixed path that is one frame of window travel and nothing "
            + $"else). PROXY offset mean {proxyMean:F2} px, worst {_badgeProxyWorstPx:F2} px (how "
            + "far '_visual' - the transform this mark used to hang under - sat from the HOST, the "
            + "transform its seat is measured in). USER REPORT (2026-09-04): 'Zusätzlich zieht es "
            + "immer etwas nach wenn man das Fenster bewegt statt fix auf der Ebene zu sein.' A "
            + "PROXY of 0.00 says the pose chain was never the cause and the next round must look at "
            + "the DRAW path - the supersample display quad is posed in Camera.onPreCull, a phase "
            + "later than every writer here; anything else says the proxy WAS the cause and the "
            + "reparent removed it. Both figures are in this window's own authored pixels. ONE LINE "
            + $"PER WINDOW: the sampling latches off after {BadgeSampleCap} moving frames, so "
            + "nothing here is written per frame.");
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
    /// ModBuild 243 — take the next sample at <see cref="InkConfirmStrideFrames"/> while the budget
    /// lasts, and at <see cref="InkVerifyStrideFrames"/> once it is spent. Every fast re-check in
    /// <see cref="ServiceInkCapture"/> goes through here so there is exactly one place that can turn
    /// "confirm a change quickly" into "walk this window's subtree fifteen times a second forever";
    /// see <see cref="InkConfirmMaxSamples"/>.
    /// </summary>
    private void ScheduleConfirmSample(int now)
    {
        if (_inkConfirmRun < InkConfirmMaxSamples)
        {
            _inkConfirmRun++;
            _inkNextSampleFrame = now + InkConfirmStrideFrames;
            return;
        }
        _inkConfirmBudgetSpent++;
        _inkNextSampleFrame = now + InkVerifyStrideFrames;
    }

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
            sig = PanelInkBounds.ActiveSetSignature(_panel, out sigTransient,
                out _inkSigNodes, out _inkSigTruncated);
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
            _inkEmptyRun = 0;
            _inkConfirmRun = 0;
            // ModBuild 243: SAMPLE ON THE EVENT'S OWN FRAME. This waited InkSettleStrideFrames, and
            // those four frames were pure latency on the one path that is already exempt from every
            // gate below (the first sample of a generation REPLACES the union and the settle burst
            // skips the repeat gate). The sample that used to be taken at now+4 is still taken — it
            // is simply the second one of the burst now.
            _inkNextSampleFrame = now;
            _inkSettleUntilFrame = now + InkSettleFrames;
            _inkSettleHardStopFrame = now + InkSettleMaxFrames;
            _inkCause = frameChanged && sigChanged
                ? "the host rect resized AND the set of open sub-views changed"
                : frameChanged ? "the host rect resized" : "the set of open sub-views changed";
        }
        else if (_inkNextSampleFrame < 0)
        {
            _inkNextSampleFrame = now;
            _inkSettleUntilFrame = now + InkSettleFrames;
            _inkSettleHardStopFrame = now + InkSettleMaxFrames;
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
            // DEGENERATE: zero drawn graphics, or zero size — "das Fenster ist leer".
            //
            // NOTHING WAS EVER CAPTURED: the frame-based handle is already on the screen and it is
            // the right answer for a window that has not laid out yet. Say so once and leave it —
            // a silent fall back to the very geometry this round replaced is the one outcome nobody
            // could diagnose. Byte-for-byte the ModBuild 242 behaviour.
            if (!_inkValid)
            {
                // ModBuild 251 — "HAS NEVER DRAWN" AND "HAS STOPPED DRAWING" LOOK IDENTICAL TO THE
                // PLAYER, AND ONLY ONE OF THEM WAS ANSWERED.
                //
                // USER REPORT (ModBuild 250 hardware), verbatim: "Nach der bestätigung ist ein
                // Greifbalken ohne sichtbaren Inhalt kurz erschienen, kannst du in den logs lesen was
                // das war? Ich hab ihn auch kurz mit dem laser gegrabbed. Das darf nicht passieren."
                //
                // THE TRACE, and it is one window from one float. 'New Party display' converts at
                // Player.log:6032, takes its handle at :6042 and reports THE INK UNION COULD NOT BE
                // MEASURED at :6044 — and is then REVEALED ANYWAY at :6059: "MODAL REVEAL:
                // 'GloomhavenVR.Panel_Modal_New Party display' FORCED after 608 ms (deadline 600 ms;
                // still waiting on first content fit)". That deadline branch is RIGHT and is not being
                // touched here — a window must never stay invisible — but what it put in front of the
                // player was a brass bar with a laser collider and nothing behind it. Two further
                // instruments agree with him and both arrive too late to help: ":6079 MODAL LIVENESS
                // ARMED: 'New Party display' … it has NEVER been measured drawing" and ":6120 EMPTY
                // WINDOW RELEASED: 'New Party display' (ID PartyPanel) — DARK — not one of 720
                // Graphic(s) under it passes". The bar therefore stood, and was grabbed, for the whole
                // liveness grace between :6059 and :6120.
                //
                // ModBuild 243 BUILT THE ENTIRE REMEDY BELOW AND COULD NOT REACH IT, and its own
                // ledger says so on every GRAB BAR line of that log: "0 empty-window handle hide(s)".
                // The empty run it counts was started only for a window that ALREADY HAD a committed
                // rectangle, so the one shape that produces a bar with no window in its whole life was
                // the one shape exempt from the rule [[ask-the-extent-of-the-symptom]].
                //
                // THE GUARD IS THE REVEAL GATE, NOT A TIMER, and that is what keeps the ModBuild 242
                // sentence below true where it was true. While the panel is render-hidden the bar is
                // hidden with it (GrabbableModal registers the holder as an extra render root, so
                // CanvasConversion.HideTree walks it), there is nothing on the screen to take off, and
                // "the frame-based handle is the right answer for a window that has not laid out yet"
                // describes exactly that interval. The instant the panel is DRAWN, the same two
                // agreeing empty walks that answer "stopped drawing" answer "never drew" — and
                // SyncBarVisibility puts the handle straight back on the first sample that measures
                // ink, because it clears the flag from _inkValid and nothing else.
                bool behindTheRevealGate = _panel == null || _panel.RevealPending
                                           || _panel.RenderHidden || _panel.OwnerRenderHidden;
                if (behindTheRevealGate || _barHiddenForEmpty)
                {
                    _inkEmptyRun = 0;
                    if (!_inkFallbackReported)
                    {
                        _inkFallbackReported = true;
                        _inkFallbackDue = true;
                    }
                    return;
                }
                // Fall through: the panel is on the screen and it is drawing nothing. Same confirm
                // run, same hide, same instant return of the handle when content arrives.
            }

            // A COMMITTED RECTANGLE EXISTS AND THE WINDOW HAS STOPPED DRAWING (ModBuild 243). Through
            // ModBuild 242 this returned, so the handle kept the full width of content that is no
            // longer on the screen until ModalFallback's 2 s liveness dwell released the whole float.
            // Confirm it and take the handle off; the whole policy is on the InkSettleFrames block.
            bool neverDrew = !_inkValid;   // ModBuild 251 — see the block above
            _inkEmptyRun++;
            if (_inkEmptyRun < InkEmptyConfirmSamples)
            {
                ScheduleConfirmSample(now);
                return;
            }
            _inkEmptyRun = 0;
            _inkEmpties++;
            _barHiddenForEmpty = true; // SyncBarVisibility takes the handle off on this same tick
            _inkValid = false;
            // ModBuild 447 — and with it the full-frame verdict. A window that draws nothing paints
            // no plate either, and leaving the verdict standing would hand the NEXT content to
            // arrive in this holder a frame-wide rod it never earned.
            _inkFullFrame = false;
            // ModBuild 449 — and the plate floor with it, for the same reason: a window that draws
            // nothing has no backdrop hanging below its frame either, and the NEXT content to arrive
            // in this holder must not inherit a floor measured against the last one's artwork.
            _inkPlateBottom = float.PositiveInfinity;
            _inkPlateBottomName = string.Empty;
            _inkGenSeeded = false;
            _inkPendingValid = false;
            _inkReleaseValid = false;
            _inkReleaseRun = 0;
            _inkBottomName = string.Empty;
            _inkCause = neverDrew
                ? $"the window has NEVER drawn anything and it is past the reveal gate "
                  + $"({InkEmptyConfirmSamples} agreeing empty walk(s)), so the handle was taken off "
                  + "the screen"
                : $"the window stopped drawing anything ({InkEmptyConfirmSamples} agreeing "
                  + "empty walk(s)), so the handle was taken off the screen";
            // THE FALSIFIER, and it reports what it MEASURED rather than what the rule intends. The
            // two states share this line and are told apart by the middle clause, because only one of
            // them is new in ModBuild 251 and the next reader must be able to see which fired.
            VRLog.Warn("WorldUI",
                $"EMPTY GRAB BAR TAKEN OFF: '{_logName}' — MEASURED THIS TICK: "
                + $"{InkEmptyConfirmSamples} agreeing ink walk(s) at the confirm stride found ZERO "
                + "drawn graphic(s) under this window's own root (or a zero-size union) while the "
                + $"panel was ON THE SCREEN (RevealPending={(_panel != null && _panel.RevealPending)}, "
                + $"RenderHidden={(_panel != null && _panel.RenderHidden)}, "
                + $"OwnerRenderHidden={(_panel != null && _panel.OwnerRenderHidden)}), and this window "
                + (neverDrew
                    ? "HAS NEVER MEASURED INK IN ITS LIFE — the ModBuild 251 case, which is the "
                      + "shape of the user's report: a window that is force-revealed at the gate's "
                      + "600 ms deadline without ever having laid out leaves a brass bar with "
                      + "nothing behind it, and ModBuild 243's rule could not see it because the run "
                      + "it counts was started only for a window that already had a committed "
                      + "rectangle"
                    : "HAD a committed ink rectangle and then stopped drawing — the ModBuild 243 "
                      + "case, unchanged")
                + $". THE BAR'S GameObject IS NOW INACTIVE, which takes its laser collider with it, "
                + "and the palm grab zone is disabled; a bar that is being HELD is exempt until it is "
                + "let go. The close X deliberately stays on the frame's own corner — it is the "
                + "rescue for a window the player can no longer see. Both come back on the first "
                + $"sample that measures ink again. This is hide {_inkEmpties} over this window's "
                + "life. NOTHING WAS WRITTEN TO THE GAME: no Hide, no Escape, no SetActive on any "
                + "game object and no CanvasGroup — the only objects switched are the mod's own "
                + "chrome. ModBuild 378: the rod may already have been off the screen when this "
                + "line was written — a float that still owes its materialise appear is WITHHELD "
                + $"before it is ever drawn (withheld right now: {_barWithheldForOwedAppear}), so "
                + "on the never-drew shape this line is a second, later statement of the same fact "
                + "and NOT evidence that anything flashed.");
            // KEEP SAMPLING FAST. Content that comes back must push the handle out again immediately,
            // which is the same reason a committed release re-arms the burst.
            _inkSettleUntilFrame = now + InkSettleFrames;
            _inkSettleHardStopFrame = now + InkSettleMaxFrames;
            ScheduleConfirmSample(now);
            _inkFallbackReported = true;
            _inkFallbackDue = true;
            return;
        }

        // The window is drawing again: any empty run in progress is broken, and SyncBar puts the
        // handle back on the next line it runs (the flag is cleared there, from _inkValid).
        _inkEmptyRun = 0;
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

        // ModBuild 447 — THE FULL-FRAME VERDICT RIDES THE ENVELOPE, term for term. It is monotone
        // inside the generation for the same reason the rectangle is: a growth is believed on sight
        // and a shrink has to prove itself, so a plate that is momentarily missed by one walk (a
        // fade, a one-frame CanvasGroup dip) cannot snap the rod's CENTRE across the window and back.
        // The release branch below overwrites it with the raw reading, because a release IS the
        // proven shrink — see the assignment there.
        bool fullFrame = ink.Plates > 0 || (_inkValid && _inkGenSeeded && _inkFullFrame);

        // ModBuild 449 — AND SO DOES THE PLATE'S BOTTOM EDGE, term for term with the verdict above
        // and with the rectangle's own yMin: monotone DOWNWARD inside the generation, so a plate
        // momentarily missed by one walk cannot snap the rod back up into the picture and down
        // again. The release branch below overwrites it with the raw reading for the same reason it
        // overwrites the verdict — a release IS the proven shrink.
        float plateBottom = ink.PlateBottom;
        if (_inkValid && _inkGenSeeded && _inkPlateBottom < plateBottom)
            plateBottom = _inkPlateBottom;

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
                // ModBuild 447 — AND THE FULL-FRAME VERDICT COMES BACK TO THE RAW READING. A release
                // is the one path that has PROVEN a recession (InkReleaseConsecutive agreeing
                // samples against the dead band), so it is the one path allowed to say "the plate is
                // gone" — the monotone term above is bypassed here exactly as it is for the rect.
                fullFrame = ink.Plates > 0;
                plateBottom = ink.PlateBottom;   // ModBuild 449 — same path, same reason
            }
            else
            {
                // ModBuild 243 — TAKE THE NEXT MEMBER OF THE RUN AT THE CONFIRM STRIDE. The COUNT is
                // untouched (InkReleaseConsecutive is still 3); only the spacing between the members
                // moves from InkVerifyStrideFrames to InkConfirmStrideFrames. See the ModBuild 243
                // block for the term-by-term argument that the 60-frame spacing was buying nothing
                // once hover subtrees left the union — and was actively WORSE against a fade, which
                // it could straddle.
                ScheduleConfirmSample(now);
            }
        }
        else
        {
            _inkReleaseCandidate = ink.Rect;
            _inkReleaseValid = true;
            _inkReleaseRun = 1;
            // A recession has just been SEEN for the first time. Ask again in four frames, not sixty.
            ScheduleConfirmSample(now);
        }

        // ModBuild 447 — THE FULL-FRAME VERDICT IS PART OF "MOVED", and it has to be. It changes the
        // rod's width and its centre on its own, with no change to the rectangle at all: a window
        // whose backdrop plate arrives after its list has settled would otherwise be a placement
        // nothing ever commits, because every gate below is keyed on `moved` and the early return
        // three lines down would take every such sample. It is also what puts the verdict through
        // the repeat gate, so a plate that appears once outside the settle burst has to appear twice
        // before the handle jumps.
        // ModBuild 449 — THE PLATE FLOOR IS PART OF "MOVED" for exactly the reason the full-frame
        // verdict is: it moves the rod on its own, with no change to the union's rectangle at all.
        // A merchant whose artwork finishes loading after its item list has settled would otherwise
        // be a placement nothing ever commits. The 0.5 px dead band is the one SameRect uses; two
        // infinities compare equal, so a window that never paints a plate never trips this term.
        bool plateFloorMoved = !(Mathf.Abs(plateBottom - _inkPlateBottom) <= 0.5f)
                               && !(float.IsPositiveInfinity(plateBottom)
                                    && float.IsPositiveInfinity(_inkPlateBottom));
        bool moved = !_inkValid || !SameRect(grown, _inkRect) || fullFrame != _inkFullFrame
                     || plateFloorMoved;
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
            // THE ENVELOPE DID NOT MOVE. The confirm budget is a per-EPISODE allowance, and restoring
            // it here (and at every commit below) is what makes it a limit on one burst of fast
            // sampling rather than a lifetime quota a long session would silently exhaust
            // ([[sentinel-overflow-and-silent-scans]]).
            //
            // BUT NOT WHILE A RELEASE RUN IS BUILDING, and that exception is the whole point of the
            // budget. A release candidate never moves the envelope by construction — it is contained
            // in it — so every member of a run arrives here, and restoring the budget on each one
            // would let a recession that keeps disagreeing with itself sample this window's whole
            // subtree every four frames for as long as it churns. The run keeps spending; abandoning
            // it (_inkReleaseRun back to 0, three lines up) is what pays the budget back.
            if (_inkReleaseRun == 0)
                _inkConfirmRun = 0;
            _inkPendingValid = false;
            return;
        }

        // THE REPEAT GATE — a growth found OUTSIDE the settle burst must be seen twice before it is
        // committed. The hole it closes is a TRANSIENT: the game re-parents hover tooltips onto the
        // window itself (TooltipOnWindow's `SetParent(target, …)`), and a monotone envelope would take
        // one such sighting and hold the bar away from the window until the next sub-view change.
        // ModBuild 242 took hover subtrees out of the union outright, so the SPACING that clause paid
        // for is gone and ModBuild 243 spends the confirming sample four frames later instead of
        // sixty — the COUNT is untouched, a growth is still proven twice. Inside the settle burst
        // there is no gate at all: a view that is still arriving must be followed immediately, and
        // the burst is the one interval in which every reading is expected to differ.
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
                ScheduleConfirmSample(now);
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
            _inkCause = "the ink receded and held for " + InkReleaseConsecutive + " agreeing sample(s)";
            _inkSettleUntilFrame = now + InkSettleFrames;
            _inkSettleHardStopFrame = now + InkSettleMaxFrames;
            _inkNextSampleFrame = now + InkSettleStrideFrames;
        }
        else if (settling && now < _inkSettleHardStopFrame)
        {
            // THE SELF-EXTENDING BURST (ModBuild 243). This sample MOVED the union inside the burst,
            // which means the window is still arriving — a game window fade is longer than the fixed
            // InkSettleFrames at 90 Hz, so the burst used to expire one sample before the tab finished
            // and hand the last growth to the 60-frame gate. Extended, never past the hard stop, so
            // content that is rewritten every frame cannot hold the burst open
            // ([[settle-gate-vs-external-writer]]).
            int extended = Mathf.Min(now + InkSettleFrames, _inkSettleHardStopFrame);
            if (extended > _inkSettleUntilFrame)
            {
                _inkSettleUntilFrame = extended;
                _inkBurstExtensions++;
            }
        }

        // ModBuild 243 — THE CAUSE FIELD USED TO GO STALE ON A GROWTH, and it read as a lie. It was
        // only ever written by a generation event or a committed release, so a growth commit printed
        // whatever had caused the LAST re-capture: ModBuild 242's log line 2295 reads
        // 'cause: the ink receded and held for 3 verify sample(s)' on a commit where the union GREW
        // from 1301 to 1495 px, with the release counter unchanged at 1. Any reading of that line
        // that trusts `cause:` on a growth is wrong, and one did.
        //
        // ModBuild 447 — AND IT MUST NOT SAY "GROWTH" WHEN THE RECTANGLE DID NOT MOVE. The
        // full-frame verdict commits on its own (see the `moved` block above), and a commit that
        // reports a growth which the printed union does not show is the same lie one build later.
        if (!released && _inkGenSeeded)
        {
            bool rectMoved = !SameRect(grown, _inkRect);
            _inkCause = !rectMoved
                ? "the FULL-FRAME VERDICT changed with the union standing still — the window "
                  + (fullFrame ? "started" : "stopped")
                  + " painting a plate across its own frame, which is what decides whether the rod's "
                  + "width and centre come from the frame or from the ink (GrabBarLayout.SolveSpan)"
                : settling
                    ? "the settle burst followed the window as it finished arriving (a GROWTH inside "
                      + "the burst, no sub-view change and no host resize)"
                    : "the verify poll found ink outside the held envelope and the growth repeated (a "
                      + "GROWTH, no sub-view change and no host resize)";
        }

        _inkRect = grown;
        _inkFullFrame = fullFrame;
        // ModBuild 449 — commit the plate floor beside the verdict it belongs to. The NAME follows
        // the number: whichever plate owns the COMMITTED edge is the one the falsifier must quote,
        // and a committed floor of infinity has no plate to name. Mathf.Approximately is deliberately
        // not used here — Abs(inf - inf) is NaN and every comparison against it is false, so it would
        // report "the floor moved" on every sample of a window that has no plate at all.
        bool sampleOwnsFloor = !float.IsPositiveInfinity(ink.PlateBottom)
                               && Mathf.Abs(plateBottom - ink.PlateBottom) <= 0.5f;
        if (float.IsPositiveInfinity(plateBottom))
            _inkPlateBottomName = string.Empty;
        else if (sampleOwnsFloor || _inkPlateBottomName.Length == 0)
            _inkPlateBottomName = ink.PlateBottomName;
        _inkPlateBottom = plateBottom;
        _inkValid = true;
        _inkGenSeeded = true;
        _inkConfirmRun = 0; // the episode committed; the next one starts with a full budget
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
    /// ModBuild 243 — <b>THE SIGNATURE'S OWN LEDGER, because a blind signature reads exactly like a
    /// window nobody is touching.</b> That is not a hypothetical: through ModBuild 242 the options
    /// window's generation stayed at 1 across four tab presses and up to sample 198, and the line said
    /// nothing about it. These are the terms that tell "no sub-menu was opened" apart from "the walk
    /// could not see the sub-menu": how many active nodes went into the hash, and whether the node
    /// budget cut the walk short ([[sentinel-overflow-and-silent-scans]]).
    /// </summary>
    private string SignatureLedger() =>
        $"SIGNATURE LEDGER: the depth-2 active-set walk hashed {_inkSigNodes} node(s) this tick"
        + (_inkSigTruncated
            ? " and WAS TRUNCATED at its node budget, so part of this window's tree is NOT in the "
              + "signature and a sub-view opening there would fire no generation event — this is the "
              + "first thing to check if the bar is late again"
            : " and was not truncated")
        + $"; {_inkBurstExtensions} settle-burst extension(s), {_inkEmpties} empty-window handle "
        + $"hide(s) and {_inkConfirmBudgetSpent} confirm-budget exhaustion(s) over this window's "
        + "life — a non-zero exhaustion count is a window whose content never settles, which is the "
        + "one thing the fast confirm path could make expensive. A generation number that stays at 1 while the user "
        + "opens sub-menus, with a node count that never changes, is this walk being too shallow — "
        + "which is exactly what ModBuild 242 shipped and what depth 2 repaired";

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
            // APPENDED, ModBuild 440 (never reworded): the parenthesis was written when the commit
            // really was Content ∪ Host. It has had a narrowing under it since ModBuild 242 and a
            // mod-chrome floor since 440, and a reader taking this line at its word would conclude
            // the rect cannot be smaller than the frame — which is the belief that cost round 2.
            hitSource = "CanvasConversion.TryGetHitRect (Content ∪ Host, the fit's own commit)"
                        + " — and since ModBuild 242 that union may also be NARROWED to the padded"
                        + " drawn content, then floored back out to contain the mod's own chrome"
                        + " (ModBuild 440); grep HIT RECT CHROME FLOOR for which of the two decided"
                        + " this rectangle";
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

    /// <summary>
    /// ModBuild 449 — <b>ONE GREP ACROSS TWO LOGS SETTLES ITEM 15.</b> The round that shipped
    /// ModBuild 447 left the merchant's handle right on the host and in the middle of the picture on
    /// the co-player, and the first hypotheses about why were all about the PLATE COUNT — that the
    /// peer measured a different number of plates, that its artwork had not loaded when the census
    /// ran, that its window arrived through a different fit path. Every one of them was wrong: both
    /// clients read <c>2 full-frame plate(s)</c>, both took the FRAME branch, and both printed
    /// <c>CONFIRMED</c>. The term that differed was one neither instrument reported — how far BELOW
    /// its own frame the excluded plate hangs — and it differed because the two players' canvases
    /// have different aspect ratios.
    ///
    /// <para><b>WHAT THE LINE SAYS, AND THE FALSIFIER.</b> The plate count, the branch
    /// <c>GrabBarLayout.SolveSpan</c> took, the three candidate bottoms (frame, ink union, plate) and
    /// which of them the rod was actually seated under — in one place, on every client.
    /// <list type="bullet">
    /// <item><c>PLATE FLOOR: none</c> — this window paints no full-frame plate. That is the CORRECT
    /// reading for <c>New Party display</c>, whose 328 px column inside a 1988 px transparent frame
    /// legitimately gets a narrow bar taken from the UNION. A narrow bar on a line reading
    /// <c>none</c> is ModBuild 447's rule working, NOT this defect, and must never be "fixed".</item>
    /// <item><c>PLATE FLOOR: level with the frame</c> — a plate whose aspect matches its canvas.
    /// This is every plate the HOST client measured on 2026-09-05, and on it this whole build is
    /// arithmetically a no-op: the seat, the judged gap and the rod's y are bit-for-bit ModBuild
    /// 448's.</item>
    /// <item><c>PLATE FLOOR: N px BELOW the frame</c> with <c>SEAT: the PLATE</c> — the defect's own
    /// shape, repaired. The peer's merchant would have read 371 px on ModBuild 448 with the rod
    /// seated on the FRAME, which is the whole bug in one clause.</item>
    /// </list>
    /// <para><b>THE THIRD WINDOW, NAMED IN ADVANCE BECAUSE IT MOVES ON BOTH CLIENTS.</b> The two
    /// windows the user reported (<c>UI Shop Item Window</c>, <c>UI Temple Window</c>) overflow on
    /// the PEER only, so the fix is invisible on the host there. <c>UI Loadout Window</c> is
    /// different: its <c>Holder/Paper</c> is the same 16:9-scaled-to-width shape and it overflows on
    /// BOTH clients — the ModBuild 448 fit measured its content as <c>2461x1385 px at (0,0)</c>
    /// (y -692..692) against a 1920x1080 frame on the host and <c>2580x1451 px</c> (y -726..726)
    /// against 2580x1080 on the peer, while its ink union bottomed out at -591 and -549. So its rod
    /// drops about 101 px (63 mm) on the host and 177 px (82 mm) on the peer, which is the SAME
    /// defect at a smaller amplitude and was never reported. THIS LINE IS HOW THAT IS AUDITED rather
    /// than assumed: if the loadout's own <c>PLATE FLOOR</c> reads <c>level with the frame</c>, the
    /// -692 was set by something other than a plate and nothing moved there at all.</para>
    ///
    /// THE FIX IS INERT if a peer line still reads a non-zero overhang while <c>SEAT: the FRAME</c>
    /// or <c>SEAT: the INK</c> — the floor was measured and the seat ignored it. THE ROUND PROVED
    /// NOTHING if every client reads <c>level with the frame</c> or <c>none</c>, which would mean the
    /// co-player's canvas aspect changed; the frame's own size is on this line so that can be told
    /// apart from a fix that worked.</para>
    /// </summary>
    private void ReportPlateFloor(Rect hostRect, float barTopPx)
    {
        bool hasPlate = !float.IsPositiveInfinity(_inkPlateBottom);
        float overhang = hasPlate ? hostRect.yMin - _inkPlateBottom : 0f;
        string floor = !hasPlate
            ? "PLATE FLOOR: none (this window paints no full-frame plate, so the union is its whole "
              + "vertical answer — the correct reading for a transparent frame around a column, and a "
              + "narrow bar beside it is ModBuild 447's rule, not a defect)"
            : overhang <= 0.5f
                ? $"PLATE FLOOR: level with the frame ('{_inkPlateBottomName}' bottoms out at "
                  + $"y={_inkPlateBottom:F0} px against the frame's y={hostRect.yMin:F0} px), so every "
                  + "term of ModBuild 449 is arithmetically a no-op on this window"
                : $"PLATE FLOOR: {overhang:F0} px BELOW the frame ('{_inkPlateBottomName}' bottoms out "
                  + $"at y={_inkPlateBottom:F0} px against the frame's y={hostRect.yMin:F0} px) — the "
                  + "plate is BIGGER than the window it backs, which is what an aspect-preserving "
                  + "artwork does on a canvas that is not the artwork's own aspect";

        float frameBottom = hostRect.yMin;
        float inkBottom = _inkValid ? _inkRect.yMin : float.PositiveInfinity;
        float seat = Mathf.Min(Mathf.Min(frameBottom, inkBottom), _inkPlateBottom);
        string seatName = seat >= frameBottom - 0.5f
            ? "the FRAME"
            : hasPlate && seat >= _inkPlateBottom - 0.5f ? "the PLATE" : "the INK";

        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"GRAB BAR PLATE FLOOR for '{_logName}': {_inkPlates} full-frame plate(s) on the latest "
            + "walk, committed verdict "
            + (_inkFullFrame ? "PAINTS ITS FRAME" : "does NOT paint its frame")
            + ", so the rod's WIDTH and CENTRE came from "
            + (_inkFullFrame ? "the FRAME" : "the UNION")
            + $"; {floor}; THE THREE CANDIDATE BOTTOMS in this window's own authored px — frame "
            + $"y={frameBottom:F0}, ink union "
            + (_inkValid ? $"y={inkBottom:F0}" : "NOT MEASURED")
            + ", plate " + (hasPlate ? $"y={_inkPlateBottom:F0}" : "none")
            + $" — SEAT: {seatName} at y={seat:F0} px, and the rod's top edge landed at "
            + $"y={barTopPx:F0} px; the frame is {hostRect.width:F0}x{hostRect.height:F0} px (aspect "
            + $"{hostRect.width / Mathf.Max(hostRect.height, 1e-3f):F2}), which is the term that "
            + "differs between two players and the reason this line prints it. HOW TO READ IT. ONE "
            + "GREP OVER BOTH CLIENTS' LOGS: same window name, compare the SEAT clause. Item 15 "
            + "(2026-09-05, 'der Greifbalken mitten im Haendlerbild ... das tritt bei mir (Host) "
            + "nicht auf') was TWO clients agreeing on the plate COUNT and disagreeing on the plate's "
            + "EXTENT — the host's 1920x1080 frame is the shopkeeper artwork's own 16:9, so his plate "
            + "ends at his frame; the peer's 2580x1080 frame is not, so the same artwork stretched to "
            + "his width stands 1451 px tall and hung 371 px below it, with the rod seated one gap "
            + "under the FRAME and 337 px of merchant still drawn beneath it. FALSIFIER: a line "
            + "reading 'PLATE FLOOR: none' is a window with no backdrop at all ('New Party display'), "
            + "and its short bar is CORRECT — never read that as this defect. A line reading a "
            + "non-zero overhang with SEAT anything but 'the PLATE' is this fix measured and then "
            + "ignored, which is the one reading that means it is inert.");
    }

    private void ReportBarPlacement(Rect hostRect, float unit, float barWidth, float thickness,
                                    float intendedTopGapPx, float mmPerPx, GrabBarLayout.Span span)
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
        // THE DRAWN NUMBERS ARE HANDED IN, NOT READ BACK OFF THE TRANSFORM, and since the cube became
        // a rod that is no longer a style preference: the rod's root carries a UNIFORM scale (see
        // SyncBar), so its localScale says nothing at all about how wide or how thick the handle is.
        // barWidth and thickness are the two numbers SyncBar actually spent.
        //
        // WHAT thickness MEANS HERE: the SHAFT's diameter. The two end knobs stand proud of it at
        // 1.28 R (GrabBarVisual's laser-target note), so at the bar's two extremities the drawn top
        // edge is about a quarter of a shaft radius higher than this line reports. The claim being
        // tested is about the long run clearing the ink, which is the shaft.
        //
        // THE TARGET, NOT THE TRANSFORM (2026-09-03). The rod now EASES toward its place over
        // GrabBarTweenMs, and this report fires on the very tick the target moved — reading the
        // transform here would judge a rod that is still travelling and print NOT ACHIEVED for a
        // placement that lands 150 ms later. The claim under test is where the bar is SEATED.
        Vector3 pos = _barTween != null ? _barTween.TargetPosition : _bar.Root.localPosition;
        float barTopPx = (pos.y + thickness * 0.5f) / unit;
        float barCentrePx = pos.x / unit;
        float barHalfPx = barWidth * 0.5f / unit;
        string suppressed = _inkReportsSuppressed > 0
            ? $" ({_inkReportsSuppressed} earlier line(s) suppressed by the {InkReportThrottleSeconds:F0} s rate limit)"
            : string.Empty;
        _inkReportsSuppressed = 0;
        string frame = $"the HOST RECT for comparison spans x {hostRect.xMin:F0}..{hostRect.xMax:F0} "
                       + $"and y {hostRect.yMin:F0}..{hostRect.yMax:F0}, {hostRect.width:F0}x{hostRect.height:F0} px";
        ReportPlateFloor(hostRect, barTopPx);

        if (fallback || !_inkValid)
        {
            // ModBuild 243 — TWO DIFFERENT STATES SHARE THIS LINE and the next reader must be able to
            // tell them apart in one grep: a window that has NEVER measured ink (the handle is the
            // frame-based one, unchanged since ModBuild 235) and a window that HAD ink and stopped
            // drawing (the handle has been taken off the screen, which is the user's second report).
            string handle = _barHiddenForEmpty
                ? "THE HANDLE HAS BEEN TAKEN OFF THE SCREEN (ModBuild 243/251): this window drew "
                  + $"nothing for {InkEmptyConfirmSamples} agreeing walk(s) while its panel was on "
                  + "the screen, so the rod and its laser capsule are switched off and the "
                  + "close X is back on the frame's own corner. It all comes back on the first "
                  + "sample that measures ink again. The X was deliberately NOT hidden — it is the "
                  + "rescue for a window the player can no longer see. GREP EMPTY GRAB BAR TAKEN OFF "
                  + "for which of the two shapes it was (never drew / stopped drawing)"
                // ModBuild 251 — this branch is now reached ONLY while the panel is still behind the
                // reveal gate, where the bar is render-hidden with it and the frame-based placement
                // costs nothing. A window that is drawn and drawing nothing takes the branch above.
                : "the bar keeps the frame-based placement this round exists to replace: bar "
                  + $"top edge y={barTopPx:F0} px, centre x={barCentrePx:F0} px, half-width {barHalfPx:F0} px"
                  + " (this reading is only reachable while the panel is still render-hidden behind "
                  + "the reveal gate — a REVEALED window that measures no ink loses the handle "
                  + "outright, ModBuild 251)";
            VRLog.Warn("WorldUI",
                $"GRAB BAR CLEARS THE INK: NOT ACHIEVED for '{_logName}' — failing term: THE INK UNION "
                + "COULD NOT BE MEASURED (zero drawn graphic(s) under the window's own root, or zero "
                + $"size), so {handle}; "
                + $"{frame}; generation {_inkGeneration}, {_inkSamples} sample(s) taken, re-capture cause "
                + $"was {_inkCause}; {MouseoverLedger()} — IF THAT REFUSED COUNT IS NON-ZERO AND THE "
                + "UNIONED COUNT ON the last successful line was small, the ModBuild 241 mouseover "
                + $"exemption has emptied this window's bucket and is the first thing to look at; "
                + $"{SignatureLedger()}.{suppressed}");
            return;
        }

        float inkBottom = _inkRect.yMin;
        float inkCentre = _inkRect.center.x;
        bool clearsVertically = barTopPx <= inkBottom + 0.5f;
        // ModBuild 447 — THE HORIZONTAL CLAIM IS NOW "THE BAR IS CENTRED ON WHATEVER SolveSpan
        // CHOSE", and the choice is printed beside the verdict so the two can be read together. It
        // used to be "centred on the ink" flat, which was the right claim only while the ink union
        // was the only source the placement had; leaving it would have marked the merchant's
        // REPAIRED handle as the failure ([[instrument-shipped-and-lying]]).
        float claimedCentrePx = span.Centre / unit;
        bool centredAsClaimed = Mathf.Abs(barCentrePx - claimedCentrePx) <= 1f;
        string spanTerm = $"the rod's width and centre were taken from {span.Source} — that rectangle "
                          + $"is {span.Width / unit:F0} px wide with its centre at "
                          + $"x={claimedCentrePx:F0} px, and the rod is "
                          + $"{GrabBarLayout.BarWidthFraction:F2} of it (the width, and only the "
                          + "width, then passes through the BarSizeSettle damp, so the half-width "
                          + "above may lag this number by one settle window on a tick the host "
                          + "resized; the centre does not and is written straight through)";

        // THE GAP, both ways. See this method's own comment for which of the two is judged and why.
        //
        // ModBuild 449 — THE JUDGED TERM IS THE DROP BELOW THE WINDOW'S OWN PAINTED BOTTOM, which is
        // the frame's yMin for every window that does not hang a backdrop below its frame and is
        // therefore bit-identical to ModBuild 448 on every window in both of that round's logs
        // EXCEPT the two the fix is about. It has to move with the seat: SyncBar now seats the rod
        // under a plate that overflows the frame, and a term still measuring the drop from the FRAME
        // would print THE GAP IS LARGER THAN INTENDED for exactly the windows this build repaired
        // ([[instrument-shipped-and-lying]]). The ink's own overflow is deliberately NOT in this
        // term — 'UI Quest Popup' fails it today because its content hangs below its frame, and that
        // reading belongs to the lane that owns that window, unchanged.
        float gapToInkPx = inkBottom - barTopPx;
        float paintedBottomPx = Mathf.Min(hostRect.yMin, _inkPlateBottom);
        float dropBelowPaintedPx = paintedBottomPx - barTopPx;
        bool gapWithinIntent = dropBelowPaintedPx <= intendedTopGapPx + InkReleaseDeadBandPx;
        string gaps =
            $"THE GAP: to the ink {gapToInkPx:F0} px = {gapToInkPx * mmPerPx:F0} mm, "
            + $"below the window's own painted bottom (y={paintedBottomPx:F0} px, "
            + (float.IsPositiveInfinity(_inkPlateBottom) || _inkPlateBottom >= hostRect.yMin
                ? "the FRAME — this window hangs no plate below it"
                : $"the PLATE '{_inkPlateBottomName}', which overflows the frame's "
                  + $"y={hostRect.yMin:F0} px by {hostRect.yMin - _inkPlateBottom:F0} px")
            + $") {dropBelowPaintedPx:F0} px = {dropBelowPaintedPx * mmPerPx:F0} mm, "
            + $"against an INTENDED {intendedTopGapPx:F0} px = {intendedTopGapPx * mmPerPx:F0} mm "
            + $"(BarGapMeters {BarGapMeters:F3} m to the bar's centre, less half its thickness, x the "
            + $"short-panel proportion, at {mmPerPx:F3} mm per authored px on the live rig)";

        // ModBuild 447 — THE CENSUS IS THE LATEST SAMPLE, THE VERDICT IS THE COMMITTED ONE, and they
        // are printed side by side because they are ALLOWED to disagree: the verdict is monotone
        // inside a generation and only a confirmed release may drop it, so "0 full-frame plate(s)"
        // beside "committed verdict: the window PAINTS its frame" is one walk that missed a plate
        // being correctly ignored — not a contradiction. A STANDING disagreement across many lines
        // is the release side failing to reach its run length, and that is the lead.
        string plateVerdict = _inkFullFrame
            ? "committed verdict: this window PAINTS a plate across its own frame, so the rod's "
              + "width and centre are the FRAME's"
            : "committed verdict: this window does NOT paint its frame, so the rod's width and "
              + "centre are the UNION's";
        string census = $"{_inkGraphics} graphic(s) unioned, {_inkPlates} full-frame plate(s) on this "
                        + $"sample ({plateVerdict}), "
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
            + (float.IsPositiveInfinity(_inkPlateBottom)
                ? "this window paints NO full-frame plate, so the union is the whole vertical answer"
                : $"the FULL-FRAME PLATE '{_inkPlateBottomName}' reaches down to y={_inkPlateBottom:F0} px")
            + "; "
            + $"{spanTerm}; {gaps}; {frame}; {census}; FRESH capture, generation {_inkGeneration}, sample {_inkSamples} "
            + $"of that generation, held {_inkHeldFrames} frame(s) before it, {_inkGrowthsDeferred} "
            + $"growth(s) deferred by the repeat gate and {_inkReleases} release(s) committed over this "
            + $"window's life, cause: {_inkCause}; {SignatureLedger()}";

        if (clearsVertically && centredAsClaimed && gapWithinIntent)
        {
            VRLog.Info("WorldUI",
                $"GRAB BAR CLEARS THE INK: CONFIRMED for '{_logName}' — {measurement}.{suppressed} HOW TO "
                + "READ IT. The claim is that the brass handle hangs BELOW everything the window draws "
                + "and is as long and as centred as the WINDOW, and the THREE numbers that would "
                + "falsify it are on this line: the bar's top edge must be at or below the lowest drawn "
                + "graphic's bottom edge, the bar's centre must be the centre of the rectangle named in "
                + "the 'width and centre were taken from' clause, and — new in ModBuild 239 — the "
                + "handle must not hang more than one dead band below the window's OWN bottom edge. "
                + "That third term is the user's 'zu grosser Abstand' report, and it is JUDGED rather "
                + "than merely printed because the first two were both true of the window he "
                + "photographed the handle 390 px under. WHICH RECTANGLE THE SECOND TERM NAMES IS "
                + "ModBuild 447's WHOLE CHANGE, and the full-frame plate count on this line is what "
                + "decides it: a window that paints a plate across its own frame IS a window of that "
                + "size and takes the FRAME's width and centre (the merchant, whose union spans only "
                + "its item list at x 461..977 inside a 1920 px window — händlerbalken.jpg), while a "
                + "window whose frame is transparent around what it draws takes the UNION's ('New "
                + "Party display', 0 plates, a 328 px column inside a 1988 px frame). If those two "
                + "windows ever read the same way, this term has stopped separating them. A window "
                + "whose ink fills its frame reads centre 0 and a bar top one gap under the host rect "
                + "— unchanged from ModBuild 235 by construction, which is what makes an unchanged "
                + "reading on those windows evidence rather than an absence of evidence. "
                + "The ink union GROWS on sight and "
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
            : !centredAsClaimed
                ? $"HORIZONTAL CENTRE — the bar's centre x={barCentrePx:F0} px is off the centre this "
                  + $"placement CLAIMS, x={claimedCentrePx:F0} px, by "
                  + $"{Mathf.Abs(barCentrePx - claimedCentrePx):F0} px, and {spanTerm}. THIS TERM IS "
                  + "READ OFF THE TWEEN'S TARGET, which SyncBar sets to the claimed centre on the "
                  + "same line, so it can only fail if a SECOND owner is writing the rod's target — "
                  + "grep the rod's tween source clause for who"
                : $"THE GAP IS LARGER THAN INTENDED — the handle's top edge hangs "
                  + $"{dropBelowPaintedPx:F0} px = {dropBelowPaintedPx * mmPerPx:F0} mm below the window's "
                  + $"own PAINTED bottom edge y={paintedBottomPx:F0} px "
                  + (float.IsPositiveInfinity(_inkPlateBottom) || _inkPlateBottom >= hostRect.yMin
                      ? $"(the frame's own y={hostRect.yMin:F0} px — no plate hangs below it)"
                      : $"(the PLATE '{_inkPlateBottomName}', {hostRect.yMin - _inkPlateBottom:F0} px "
                        + $"below the frame's own y={hostRect.yMin:F0} px)")
                  + $", which is {dropBelowPaintedPx / Mathf.Max(intendedTopGapPx, 1e-3f):F1}x "
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
        _barTween?.Release(); // leaves the LateUpdate tick list; the rod it presented is gone
        _barTween = null;
        _badge = null;
        // The material died with the holder; the seat and the pulse describe a window that no longer
        // has a mark on it, and this class is REUSED across a window's closes and re-opens, so a
        // phase kept here would make the next float's badge appear mid-dim.
        _badgeMaterial = null;
        _badgeSeatValid = false;
        _badgeSizePx = 0f;
        _badgePulsePhase = 0f;
        _badgeGapToPlatePx = float.NaN;
        _badgeGapToRodPx = float.NaN;
        _badgeLastHostPosValid = false;
        _grabZone = null;
        _handle = null;
        _visualValid = false;
        _easing = false;
        // ModBuild 376 — the settled sizes describe a host rect that no longer has a rod on it.
        _widthSettle.Reset();
        _heightSettle.Reset();
        // The ink capture describes furniture that no longer exists; a rebuilt holder must measure
        // again from scratch rather than inherit a union taken against the old host rect.
        _inkValid = false;
        _inkFullFrame = false;   // ModBuild 447 — the frame it was a verdict about is gone too
        _inkPlateBottom = float.PositiveInfinity;   // ModBuild 449 — and the floor under it
        _inkPlateBottomName = string.Empty;
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
        // ModBuild 243: the empty-window hide is cleared with the holder it hid. A rebuilt bar is a
        // NEW GameObject and is born visible, so a stale true here would take a working handle off
        // the screen for a window that has simply not measured yet.
        _inkSettleHardStopFrame = -1;
        _inkBurstExtensions = 0;
        _inkEmptyRun = 0;
        _inkEmpties = 0;
        _inkConfirmRun = 0;
        _inkConfirmBudgetSpent = 0;
        _barHiddenForEmpty = false;
        // ModBuild 378: same argument for the withhold, and the counts go with it so "over this
        // window's life" means the same thing on the withhold line as _inkEmpties does on the
        // take-off line — the life of the holder that carried the rod. A rebuilt rod that is still
        // owed an appear is withheld again on its first tick, from a clean edge, and says so.
        _barWithheldForOwedAppear = false;
        _barWithholdWhy = string.Empty;
        _barWithholds = 0;
        _barWithholdReleases = 0;
        _inkSigNodes = 0;
        _inkSigTruncated = false;
        // The plate belongs to the game-owned host, which the caller releases separately; dropping
        // the reference is all this class may do with it. A re-converted window re-finds its own.
        _closeX = null;
        _closeXPlaced = false;
        _closeXNextProbeFrame = -1;
        _closeXPlacement = default;
    }
}
