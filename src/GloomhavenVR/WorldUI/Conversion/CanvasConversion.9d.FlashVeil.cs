using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 9d (THE PRE-START FLASH VEIL — the one frame a pooled UIWindow paints
// its prefab). A NEW part file for the reason parts 6, 8, 9b and 9c give: the refactor guard
// tracks the partial class's member and static-initializer order, and the filename sort
// ('.9.' < '.9b.' < '.9c.' < '.9d.') appends this part AFTER every existing one, so nothing
// existing moves. No static field declared here reaches a static field of another part, so
// check-partial-order.py's property still holds and no compile-order dependency is created.

internal static partial class CanvasConversion
{
    // ==========================================================================================
    // THE DEFECT, WHICH IS DIAGNOSED AND NOT UNDER INVESTIGATION
    // ==========================================================================================
    //
    // USER REPORT 2026-09-03, verbatim, and it is a ruling on PRIORITY as much as on the symptom:
    //   "Und wie gehst du jetzt weiter vor das Ein-Frame-Blitz Problem zu lösen? Wenn das garnicht
    //    vorkäme bräuchtest du auch keinen Schutz vor einem Klick auf den ein-frame-button. Ich
    //    will das dieser Blitz erst gar nicht vorkommt."
    //
    // He steps through his own recordings frame by frame and caught ONE frame of the CHARAKTER
    // LÖSCHEN confirmation — the destructive one — painting at full opacity on the character
    // screen, together with the mercenary-create screen and dozens of TextMeshPro labels still
    // reading the default "New Text". His words for that: "also irgendwas wurde hier noch nicht
    // richtig gebaut". The screenshot is .planning/debug/aufblitzen_frame.png.
    //
    // THE MECHANISM, READ OUT OF THE GAME'S OWN SOURCE (ModBuild 389 named it; this part acts on
    // it). UIWindow.m_CurrentVisualState is a FIELD INITIALISER — `= VisualState.Hidden`,
    // UIWindow.cs:123. It only becomes real in Start() (UIWindow.cs:358-370), and it is Start()
    // that drives the CanvasGroup alpha to 0 through EvaluateAndTransitionToVisualState. So:
    //
    //   * Hide(bool) is guarded by `m_CurrentVisualState != VisualState.Hidden` (UIWindow.cs:526).
    //     On a window whose Start() has not run that test is FALSE, so Hide() returns having done
    //     nothing at all — no tween, no alpha write, no blocksRaycasts write.
    //   * Unity runs Start() at the top of the frame AFTER activation. A pooled / repopulated
    //     UIWindow subtree therefore renders EXACTLY ONE FRAME at its prefab-authored state: full
    //     alpha, authored blocksRaycasts, every TMP label at the "New Text" default.
    //
    // That accounts for every observed property at once — full opacity, un-populated labels,
    // exactly one frame, four occurrences in one session — and it needs no mod write anywhere in
    // the chain, which is why no mod write was ever found. The trigger chain is corroborated:
    // UIBattleGoalPickerSlot.button.onClick -> NewPartyDisplayUI.OnBattleGoalSelected (:1174-1182)
    // -> TryHideCurrentDisplay -> CharacterSelector.Hide(...), where CharacterSelector IS
    // UICampaignAdventurePartyAssemblyWindow: the prefab holding the mercenary-create screen, the
    // delete button and the confirmation box.
    //
    // ==========================================================================================
    // THE DISCRIMINATOR, AND THE TRAP IT EXISTS TO WALK AROUND
    // ==========================================================================================
    //
    // THE TERM THAT LOOKS RIGHT AND IS AMBIGUOUS: UIWindow.IsOpen (UIWindow.cs:317,
    // m_CurrentVisualState == Shown). ModBuild 389 proved it cannot carry a veil, and wrote the
    // refutation into the SUB-VIEW SETTLE BURST line rather than shipping a remedy on it:
    // EvaluateAndTransitionToVisualState assigns m_CurrentVisualState BEFORE it starts the fade
    // tween (UIWindow.cs:548), so `!IsOpen` is equally true for a window that is legitimately
    // CLOSING. A veil keyed on it would cut off every close animation in the game — including the
    // encounter window's dissolve that ModBuild 387 restored the same day, on this user's own
    // request. The condition actually wanted is "HAS NEVER BEEN SHOWN", and IsOpen does not
    // distinguish it from "is being hidden right now".
    //
    // THE TERM THAT IS NOT AMBIGUOUS: UIWindow.HasGoneToStartingState (UIWindow.cs:279,
    // `public bool HasGoneToStartingState { get; private set; }`). It is false from construction
    // and set TRUE, unconditionally, on the statement immediately after Start() drives the window
    // to its starting state (UIWindow.cs:368-370). It is never set back.
    //
    // WHY IT IS EXACTLY THE COMPLEMENT OF THE DEFECT, rather than a proxy for it: the flash exists
    // BECAUSE Start() has not run, and this field IS "Start() has run". Same event, same edge,
    // read from the game's own state instead of inferred from a symptom.
    //
    // THE PROOF THAT A FADE-OUT CANNOT SATISFY IT — closed, from the source, not from a sample:
    //
    //   1. A fade-out is EvaluateAndTransitionToVisualState(Hidden, instant:false), reachable only
    //      from Hide(bool), which is guarded by `m_CurrentVisualState != VisualState.Hidden`. So a
    //      window can only fade out if it currently reads Shown.
    //   2. m_CurrentVisualState has exactly two writers: the field initialiser (Hidden) and
    //      EvaluateAndTransitionToVisualState. So reaching Shown requires an earlier
    //      EvaluateAndTransitionToVisualState(Shown, ...) — from Start() or from Show(bool).
    //   3. Start() sets HasGoneToStartingState = true two statements later, unconditionally. Any
    //      window that reached Shown through Start therefore has the flag TRUE.
    //   4. Show(bool) called BEFORE Start would be the one theoretical hole — and the game itself
    //      refuses to take it: ShowOrUpdateStartingState (UIWindow.cs:512-520) branches on
    //      HasGoneToStartingState and writes m_StartingState instead of showing. HideOrUpdate-
    //      StartingState (:500-508) is the same shape. THE GAME USES THIS FIELD FOR THIS EXACT
    //      DISTINCTION, which is the strongest evidence available that it carries that meaning.
    //   5. And the hole is self-closing even where it is taken: Start() then slams the window to
    //      m_StartingState with instant:true, so a pre-Start Show() does not survive to become a
    //      fade-out either.
    //
    //   => fade-out  ==>  HasGoneToStartingState == true  ==>  NEVER VEILED.
    //
    // THE PROOF IS ALSO SHIPPED AS A RUNNING MEASUREMENT, not only as this comment — see
    // s_flashVeilSpared. Every frame, the scan counts the windows that are `!IsOpen && IsVisible`
    // — i.e. not open and still painting, which is what a fade-out looks like and what a naive
    // IsOpen veil would have caught — and reports them SEPARATELY from the ones it veiled. The
    // two populations are disjoint by construction (HasGoneToStartingState splits them), so a
    // session in which the user watched a window dissolve and the SPARED count stayed at zero is
    // a session in which this reasoning is wrong, and the log says so in those terms.
    //
    // ==========================================================================================
    // WHERE THE SUPPRESSION ACTS, AND WHY THAT POINT IS EARLY ENOUGH
    // ==========================================================================================
    //
    // The trigger is a uGUI Button.onClick, which the EventSystem dispatches from its own
    // MonoBehaviour Update. Unity runs EVERY Update before ANY LateUpdate, and every LateUpdate
    // before the render loop. WorldUIModule.LateUpdate runs CanvasConversion.LateTick (verified in
    // WorldUIModule.BuildTickSteps: "CanvasConversion.Late" sits in the _lateSteps array), inside
    // the marked frame phase that part 6's AssertNotInRenderPhase checks. So a subtree the game
    // activated during Update is reachable from here BEFORE the frame it would flash in is ever
    // drawn, and the write lands in the main-thread phase — identical for both MultiPass eye
    // passes, so it can never produce the one-eye artefact round 6 chased.
    //
    // This is PREVENTION, not mitigation: the frame the user photographed does not get drawn.
    //
    // THE LIFT RUNS ONE PHASE EARLIER, from Tick() in Update, and that is what keeps the veil from
    // costing a frame of its own. Unity runs Start() for objects activated in frame N at the top
    // of frame N+1, BEFORE any Update — so by the time our Update tick runs in N+1 the flag is
    // already true and the veil is lifted before N+1 renders. Applied in the LateUpdate of frame
    // N, gone in the Update of frame N+1: the window is withheld for exactly the frame it would
    // have painted its prefab in, and for no other.
    //
    // ==========================================================================================
    // THE LEVER, AND WHY IT IS CanvasRenderer.cull AND NOT ONE OF THE OTHER FOUR
    // ==========================================================================================
    //
    // The mod may withhold its own drawing of a subtree. It may NOT call SetActive, Show or Hide,
    // and may not touch the game's alpha, its event wiring or its state. That rules out the two
    // obvious levers immediately: GameObject.SetActive is forbidden outright, and the CanvasGroup
    // on the window root is the GAME's — UIWindow carries [RequireComponent(typeof(CanvasGroup))]
    // and m_CurrentVisualState's whole job is to drive that alpha.
    //
    // Canvas.enabled — the lever parts 6's reveal gate and the surface-owned hide both use — was
    // measured and does not reach here. It only works where a Canvas exists, and these sub-views
    // have none: across the whole ModBuild 388 session the adoption sweep reports four nested
    // canvases in total (AttackModBar, BackgroundMask, InitiativeTrack, UI Scenario Esc Menu) and
    // NOT ONE inside the character screen. A canvas-level hide on that window would have to be
    // taken at the panel HOST, which is the whole window — a one-frame blink of the screen the
    // player is looking at, and a direct hit on "Es darf niemals leere Fenster geben".
    //
    // Graphic.enabled is the lever the background hide uses (HideFullScreenBackground /
    // ConvertedPanel.HiddenBackgrounds) and it WOULD reach every drawable here. It is rejected for
    // two reasons, both concrete. First, it runs managed component code: Graphic.OnDisable
    // unregisters from the canvas and dirties it, so veiling ~600 graphics and restoring them a
    // frame later buys two full canvas rebuilds on the two frames the log already shows 43-44 ms
    // spikes on. Second, TextMeshProUGUI regenerates its geometry and re-decides its own
    // TMP_SubMeshUI children's enabled state on OnEnable — so a restore pass that walks parents
    // before children can hand a sub-mesh back a state TMP had just taken away from it. That is
    // the [[empty-override-breaks-the-contract]] family and it is not worth entering.
    //
    // CanvasRenderer.cull is the one that wins, and every reason is a property, not a preference:
    //
    //   * IT IS THE ENGINE'S OWN "DO NOT DRAW" BIT. RectMask2D.PerformClipping -> Maskable-
    //     Graphic.Cull -> UpdateCull -> canvasRenderer.cull is how uGUI itself withholds a
    //     graphic. Using the sanctioned bit means no new drawing semantics are invented here.
    //   * IT RUNS NO MANAGED CODE AT ALL. It is a native property on CanvasRenderer; setting it
    //     raises no OnEnable/OnDisable, dirties no canvas geometry and regenerates no text. This
    //     is a STRICTLY smaller footprint than the Canvas.enabled the reveal gate already ships.
    //   * IT IS UNIFORM. Every uGUI drawable has exactly one CanvasRenderer — Image, RawImage,
    //     TextMeshProUGUI, TMP_SubMeshUI and every game subclass alike — so one flat
    //     GetComponentsInChildren covers the subtree with no per-type special cases, and no
    //     3D Renderer is reachable through it (a CanvasRenderer is not a Renderer, and the live
    //     character rig inside these windows belongs to the game's preview camera —
    //     [[canvasrenderer-is-not-a-renderer]], and IsForeignRenderSubtree's whole reason).
    //   * THE MOD'S OWN INSTRUMENTS ALREADY READ IT as the authority on whether a graphic draws:
    //     MeasureFixedFitParts, LoadoutConfirmPark's union, the supersample census, MrBacking and
    //     EnemyRevealSurface all test `!canvasRenderer.cull`. So a veiled sub-view is invisible to
    //     the measurement machinery too, for free and without touching a file this lane does not
    //     own. That is not a side effect to be tolerated — it is the correct answer: a subtree
    //     that is not drawn must not be measured either.
    //   * IT DOES NOT MUTATE THE COLLECTION THE SCAN IS ITERATING, and that is a correctness
    //     property and not a nicety. The candidate scan enumerates UIWindow's own live
    //     `_uiWindows` HashSet, which UIWindow adds to in OnEnable and removes from in OnDisable
    //     (UIWindow.cs:400/406). SetActive on a veiled subtree would remove entries from that set
    //     mid-enumeration and throw; Graphic.enabled would not, but only by accident. Culling a
    //     CanvasRenderer raises no component callback at all, so the set cannot change under the
    //     loop no matter what is veiled.
    //   * IT FAILS TOWARD DRAWING. If a RectMask2D or TMP re-decides the bit while a veil is up,
    //     the only value it can write is the one it computed for itself, and the graphic draws.
    //     Every absolute ruling that bounds this work — "Es darf niemals leere Fenster geben",
    //     "es MUSS immer möglich sein das Optionsmenu zu öffnen", and a click on an exit control
    //     must end in a usable confirmation or the exit happening — points the same way.
    //
    // AND THE SECOND BIT, WHICH IS NOT BELT-AND-BRACES BUT A RACE THIS PART WOULD OTHERWISE LOSE.
    // `cull` HAS ANOTHER WRITER, and it writes every frame: RectMask2D. UnmaskedUiGraphics.cs
    // states the mechanism from the shipped uGUI 1.0.0 source in this repository's own words —
    // a RectMask2D "CULLS whole renderers that leave the rect (CanvasRenderer.cull)", and
    // MaskableGraphic.UpdateCull compares against `canvasRenderer.cull` itself, so a value this
    // part wrote is simply overwritten with the clipper's own verdict. That verdict is computed in
    // ClipperRegistry.Cull, which runs from Canvas.willRenderCanvases — AFTER every LateUpdate and
    // BEFORE the draw. So for every graphic registered with a clipper, a cull-only veil would be
    // re-opened in the gap between this part's last write and the frame it was trying to prevent.
    // On the character screen that is not a hypothetical subset: EnsureScrollClipping puts a
    // RectMask2D on every scroll viewport in these windows.
    //
    // So the veil also sets `CanvasRenderer.SetAlpha(0)`, and the two bits are chosen because
    // NOTHING WRITES BOTH. RectMask2D writes cull and the per-renderer clip rect; it never touches
    // the renderer's alpha. A CanvasGroup fade is applied natively through the INHERITED alpha,
    // which is a different value again (PanelInkBounds reads them separately, and
    // [[inherited-alpha-is-not-the-group]] is the record of that distinction being load-bearing).
    // For a veiled frame to leak, a clipper and an alpha writer would have to both act on the same
    // renderer inside the same frame. Both writes are native CanvasRenderer state, both raise no
    // managed callback, and both are exactly restorable.
    //
    // EXACT RESTORE BY CONSTRUCTION, the same discipline part 6 states for its own sets: only
    // renderers whose `cull` was FALSE at veil time are recorded, so the lift can never un-cull
    // something uGUI had culled on purpose — and the alpha is put back to the value that was read
    // off the renderer at veil time, and ONLY if the renderer still carries the zero this part
    // wrote, so a writer that took the alpha over meanwhile keeps it.
    //
    // ==========================================================================================
    // WHAT THIS DELIBERATELY REFUSES TO DO
    // ==========================================================================================
    //
    //   * IT NEVER VEILS A WHOLE CONVERTED WINDOW. A candidate must be a STRICT descendant of a
    //     panel's conversion target (see FindFlashVeilOwner); the target itself is excluded. The
    //     panel's own root window is the reveal gate's business (part 6), and veiling it here
    //     would be the empty window the 2026-08 ruling forbids.
    //   * IT NEVER TOUCHES A PANEL THAT IS ALREADY RENDER-HIDDEN, by the reveal gate
    //     (ConvertedPanel.RenderHidden) or by a surface (OwnerRenderHidden). Nothing is drawing
    //     there, so there is nothing to prevent, and two owners writing visibility on one subtree
    //     is the write war [[dont-win-a-write-war]] names.
    //   * IT HAS A DEADLINE, AND THE DEADLINE FAILS OPEN. FlashVeilMaxFrames frames after a veil
    //     goes up, a window whose Start() still has not run is released and never veiled again
    //     this session. A remedy that can latch a window invisible forever is worse than the
    //     defect it removes; this one cannot outlive 3 frames on any window.
    //   * IT DOES NOT REPLACE THE CLICK GUARD. A separate lane is building defence-in-depth
    //     against a click on the one-frame button. This part is the user's actual instruction —
    //     that the flash not happen — and the two are independent.
    //
    // ==========================================================================================
    // WHAT WAS CHECKED FOR REGRESSION, BY THE NAME EACH ONE IS GREPPED BY
    // ==========================================================================================
    //
    //   * THE REVEAL GATE and the post-reveal-jump ruling (2026-08-02) — `MODAL REVEAL`. A panel
    //     that has not been revealed is RenderHidden, and this scan skips those outright. The
    //     ordering makes that read a SETTLED value rather than a racing one: the LateTick panel
    //     loop re-applies SetPanelRenderVisible(false) for every RevealPending panel, and
    //     TickFlashVeilScan is called BELOW that loop, so RenderHidden is this frame's final
    //     answer when it is read here. A panel is also RenderHidden from Convert itself
    //     (CanvasConversion.1.Core.cs:406), so there is no window between conversion and the
    //     first gate tick either. Nothing in this part reads or writes RevealPending,
    //     RevealArmed, HostCanvas.enabled or any Renderer.
    //   * MODBUILD 374's OWED APPEAR — `MODAL APPEAR RELEASED`, ModalFallback.AppearStillOwed —
    //     and MODBUILD 378's GRAB-BAR WITHHOLD, which rides the same stored verdict. Both are
    //     decided from PanelInkBounds, whose own visibility predicate is
    //     `cr != null && !cr.cull` (PanelInkBounds.cs:553). A veiled subtree is therefore
    //     invisible to the ink walk in the SAME way it is invisible to the eye — one answer, not
    //     two — so neither rule can be handed a measurement of something the player cannot see.
    //     That is the correct direction for both: a window whose only drawing content is an
    //     un-populated prefab has not arrived, and withholding its dust and its rod is exactly
    //     what 374 and 378 are for.
    //   * MODBUILD 387's ENCOUNTER-WINDOW DISSOLVE — `WINDOW MATERIALISE PLAYOUT`,
    //     `WINDOW MATERIALISE VANISH`, `MANDATORY DECISION ANSWERED`. This is THE case the
    //     discriminator must not catch, and it cannot: that window has been on the screen, so its
    //     Start() ran long ago and HasGoneToStartingState is true. It is counted in the LET
    //     THROUGH total on this part's own log line, which is what turns "must not catch it" from
    //     a claim into a reading. Nothing in Materialise is touched, and the dust the effect
    //     emits is filtered on the CanvasRenderer's OWN alpha, which this part never writes.
    //   * MODBUILD 389's SETTLE BURST and SCROLL-CLIP FIX — `SUB-VIEW SETTLE BURST`,
    //     `SCROLL CLIP`. Untouched. They compose: the burst's PAINTED-DURING-THE-BURST census
    //     counts graphics through the same `!cull` test, so a prevented flash shows up there as a
    //     PEAK near the settled 85-128 instead of the 597/696 the 385 log measured. Those two
    //     lines and this one are read together, and they must agree.
    // ==========================================================================================

    /// <summary>
    /// Frames a veil may stand before it is released regardless of the window's state. THREE, and
    /// the number is derived rather than tuned: Unity runs Start() for an object activated in
    /// frame N at the top of frame N+1, so ONE frame is the whole expected life of a veil and
    /// three leaves room for a subtree that is activated, deactivated and re-activated across a
    /// frame boundary. Past it the veil is lifted and the window is blacklisted — see
    /// <see cref="FlashVeilGaveUp"/>. 3 frames is 33 ms at 90 Hz: the absolute upper bound on how
    /// long this part can withhold anything from the player, on any window, ever.
    /// </summary>
    private const int FlashVeilMaxFrames = 3;

    /// <summary>
    /// Windows that may be veiled at once. The observed event repopulates one sub-view tree; the
    /// screenshot shows several roots painting together, so this is not 1. It is a CAP on blast
    /// radius, not a target: past it the scan stops veiling and the extra windows draw.
    /// </summary>
    private const int FlashVeilMaxWindows = 8;

    /// <summary>
    /// Windows this part may give up on. A HARD CEILING, not a hint: when the set is full the
    /// whole part stands down for the rest of the session rather than growing without limit or
    /// re-veiling a window it has already failed on. Reaching it at all means the discriminator is
    /// wrong about 128 different windows, and the correct behaviour then is to draw everything.
    /// </summary>
    private const int FlashVeilGiveUpCap = 128;

    /// <summary>One veiled subtree and the exact set of renderers this part withheld on it,
    /// each with the alpha it was carrying when the veil went up.</summary>
    private sealed class FlashVeilEntry
    {
        public UIWindow? Window;
        public ConvertedPanel? Owner;
        public string Name = "?";
        public int StartFrame;
        public readonly List<CanvasRenderer> Held = new(64);
        public readonly List<float> HeldAlpha = new(64);
    }

    /// <summary>Live veils. Never larger than <see cref="FlashVeilMaxWindows"/>.</summary>
    private static readonly List<FlashVeilEntry> FlashVeils = new(4);

    /// <summary>Entries returned to <see cref="FlashVeils"/> after a lift, so a burst of veils
    /// does not allocate a new entry (and a new held-list) every time. Single-threaded ticks.</summary>
    private static readonly List<FlashVeilEntry> FlashVeilPool = new(4);

    /// <summary>Instance IDs of windows the deadline released — never veiled again this session.
    /// See <see cref="FlashVeilGiveUpCap"/>.</summary>
    private static readonly HashSet<int> FlashVeilGaveUp = new();

    /// <summary>Sweep scratch (single-threaded ticks; reused, no per-frame allocation). Its own
    /// buffer rather than part 6's: the two hides are independent passes over different roots and
    /// must never be able to alias each other's in-flight buffer.</summary>
    private static readonly List<CanvasRenderer> FlashVeilScratch = new(256);

    /// <summary>Flash frames this part actually prevented (rising edges, i.e. veils raised).</summary>
    private static int s_flashVeilSuppressed;

    /// <summary>CanvasRenderers taken hold of across every veil this session — the SIZE of what
    /// was withheld, against the 597/696-graphic transients the 385 log measured.</summary>
    private static int s_flashVeilRenderers;

    /// <summary>
    /// THE PROOF TERM, and the falsifier for the whole discriminator. Windows seen inside a
    /// converted panel that were NOT open and still painting — i.e. exactly what a close animation
    /// looks like, and exactly what a veil keyed on <c>UIWindow.IsOpen</c> alone would have cut
    /// off — which this part LET THROUGH because their Start() had run. Counted per distinct
    /// window, not per frame, so one long dissolve is one.
    /// </summary>
    private static int s_flashVeilSpared;

    /// <summary>Instance ID of the last window counted into <see cref="s_flashVeilSpared"/>. The
    /// counter advances on the EDGE where that id changes, so a dissolve spanning 80 frames is one
    /// entry and not eighty. Two windows dissolving in alternation would each be counted more than
    /// once; that is a known and deliberate imprecision in a proof counter whose only job is to be
    /// non-zero when close animations happen.</summary>
    private static int s_flashVeilSparedLast;

    /// <summary>Name of the last window let through, for the report.</summary>
    private static string s_flashVeilSparedName = "none";

    /// <summary>Veils released by the deadline instead of by Start() — the over-firing counter.</summary>
    private static int s_flashVeilGaveUp;

    /// <summary>Worst wall time, in microseconds, any single scan of this part has cost.</summary>
    private static int s_flashVeilWorstMicros;

    /// <summary>Change gate for <see cref="ReportFlashVeil"/> — the outcome signature.</summary>
    private static int s_flashVeilLastReported;

    /// <summary>Frame the scan last ran, so Update-phase and LateUpdate-phase entries cannot
    /// double-count a single frame's work into the cost figure.</summary>
    private static int s_flashVeilScanFrame;

    // ---- the two entry points -----------------------------------------------------------------

    /// <summary>
    /// UPDATE-PHASE HALF: service the live veils, and NOTHING ELSE — no window scan, no new veil.
    /// Unity has already run Start() for everything activated last frame by the time any Update
    /// runs, so this is the earliest moment a veil can be released, and releasing it here is what
    /// makes the veil cost exactly the frame it prevented and no second frame. A veil that still
    /// stands is re-asserted on the way past, which costs one flat component fetch on a subtree
    /// that is by definition small and by definition mid-repopulation.
    ///
    /// <para>Returns immediately when nothing is veiled, which is essentially every frame of a
    /// session: one integer compare, no allocation, no hierarchy access.</para>
    /// </summary>
    private static void TickFlashVeilLift()
    {
        if (FlashVeils.Count == 0)
            return;
        ServiceFlashVeils();
    }

    /// <summary>
    /// LATEUPDATE-PHASE HALF: service the live veils, then look for new ones. This is the pass
    /// that must be in LateUpdate — see the header: it is the last phase before the render loop
    /// and it runs after the EventSystem's Update has dispatched the click that activated the
    /// subtree, so the flash frame is suppressed before it is drawn.
    /// </summary>
    private static void TickFlashVeilScan()
    {
        // Nothing converted -> nothing of the mod's is being drawn, so there is nothing to
        // withhold. Release anything still standing rather than leaving it culled.
        if (Active.Count == 0)
        {
            if (FlashVeils.Count > 0)
                ServiceFlashVeils(releaseAll: true);
            return;
        }

        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        ServiceFlashVeils();
        ScanForFlashCandidates();
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

        // One cost figure per frame, taken on the LateUpdate pass (the expensive one — the Update
        // half does no scan at all). Recorded as a WORST rather than a mean because a mean over a
        // 90 Hz idle path would read as zero and say nothing about the frames that matter
        // [[worst-field-is-the-tail]].
        if (s_flashVeilScanFrame != Time.frameCount)
        {
            s_flashVeilScanFrame = Time.frameCount;
            int micros = (int)((t1 - t0) * 1000000L / System.Diagnostics.Stopwatch.Frequency);
            if (micros > s_flashVeilWorstMicros)
                s_flashVeilWorstMicros = micros;
        }
    }

    // ---- servicing live veils -----------------------------------------------------------------

    /// <summary>
    /// Decide, for every live veil, whether it stands, is lifted, or has run out of deadline.
    ///
    /// <para>THE LIFT CONDITION IS THE DISCRIMINATOR GOING TRUE — the window's Start() has run, so
    /// the game has driven its alpha and it may draw whatever it now is. Everything else here is a
    /// failsafe: a destroyed or deactivated window, a released panel, or the deadline.</para>
    /// </summary>
    /// <param name="releaseAll">Lift every veil regardless of state (conversion stood down).</param>
    private static void ServiceFlashVeils(bool releaseAll = false)
    {
        for (int i = FlashVeils.Count - 1; i >= 0; i--)
        {
            FlashVeilEntry e = FlashVeils[i];
            UIWindow? w = e.Window;
            bool gone = w == null || !w.gameObject.activeInHierarchy
                        || e.Owner == null || !e.Owner.IsAlive;
            bool started = !gone && w!.HasGoneToStartingState;
            bool overdue = !gone && !started && Time.frameCount - e.StartFrame > FlashVeilMaxFrames;

            if (releaseAll || gone || started || overdue)
            {
                LiftFlashVeil(e);
                FlashVeils.RemoveAt(i);
                if (FlashVeilPool.Count < 8)
                    FlashVeilPool.Add(e);
                if (overdue)
                {
                    s_flashVeilGaveUp++;
                    if (FlashVeilGaveUp.Count < FlashVeilGiveUpCap && w != null)
                        FlashVeilGaveUp.Add(w.GetInstanceID());
                    ReportFlashVeil(e.Name, overdue: true);
                }
                continue;
            }

            // The veil stands. RE-APPLY, because the game repopulates this subtree while it is up
            // and a child created this frame would otherwise get the very frame this part exists
            // to remove. Idempotent: a renderer already withheld is skipped and never re-recorded,
            // so a steady veil costs one flat component fetch and no writes. Children the game
            // created THIS frame are new hold, and count toward the session total like any other —
            // a total that only counted the first pass would understate exactly the repopulating
            // case this part exists for.
            s_flashVeilRenderers += ApplyFlashVeil(e);
        }
    }

    // ---- finding candidates -------------------------------------------------------------------

    /// <summary>
    /// THE PER-FRAME SCAN. Walks the game's OWN registry of currently-enabled windows
    /// (<c>UIWindow.GetWindows()</c>, the HashSet UIWindow maintains from its OnEnable/OnDisable,
    /// UIWindow.cs:69/400/406) and asks each one two cheap property questions.
    ///
    /// <para>WHY THE GAME'S REGISTRY AND NOT A SWEEP. <c>FindObjectsOfType</c> is this repository's
    /// default suspect for a reason [[findobjectsoftype-is-the-default-suspect]], and a walk of a
    /// converted subtree is explicitly out of budget: the character screen carries 2114 transforms
    /// and 861 graphics. The registry is already maintained by the game for its own escape-key
    /// handling, it is exactly the set that can be drawing, and enumerating a <c>HashSet&lt;T&gt;</c>
    /// through its struct enumerator allocates nothing.</para>
    ///
    /// <para>THE FAST REJECT IS THE WHOLE COST ARGUMENT. Every window that has started AND is
    /// either open or not painting leaves after at most three property reads (two bool fields and
    /// one float compare) — which is every window in the game on essentially every frame. Only the
    /// two tiny populations that survive it — a window whose Start() has not run, or a window that
    /// is not open and still painting — pay for the ancestry walk. In a steady frame both are
    /// empty and this method is a bounded loop of field reads with no allocation, no GetComponent
    /// and no hierarchy access at all.</para>
    /// </summary>
    private static void ScanForFlashCandidates()
    {
        // THE STAND-DOWN. 128 different windows released by the deadline means the discriminator
        // has been wrong 128 times, and a remedy that has been wrong that often has no business
        // still running. Checked first so the part costs one integer compare from then on.
        if (FlashVeilGaveUp.Count >= FlashVeilGiveUpCap)
            return;

        HashSet<UIWindow>? windows;
        try
        {
            windows = UIWindow.GetWindows();
        }
        catch (System.Exception)
        {
            return; // the game's registry is not available (teardown) — draw everything
        }
        if (windows == null || windows.Count == 0)
            return;

        foreach (UIWindow w in windows)
        {
            if (w == null)
                continue;

            // THE DISCRIMINATOR. False here means Start() has run: the window's alpha, its
            // blocksRaycasts and its visual state are all the game's considered values, whatever
            // they are — open, closed, or MID-CLOSE. Those are never this part's business.
            bool preStart = !w.HasGoneToStartingState;

            // IsVisible is `m_CanvasGroup != null && m_CanvasGroup.alpha > 0` (UIWindow.cs:302).
            // Not open and still painting is what a close animation looks like from outside.
            bool notOpenAndPainting = !w.IsOpen && w.IsVisible;

            if (!preStart && !notOpenAndPainting)
                continue; // the fast reject: every window in the game, on almost every frame
            if (preStart && !w.IsVisible)
                continue; // a prefab authored at alpha 0 paints nothing; there is no flash to stop
            if (!w.gameObject.activeInHierarchy)
                continue;
            if (preStart && IsFlashVeiled(w))
                continue; // already held; do not pay the ancestry walk again while it stands

            ConvertedPanel? owner = FindFlashVeilOwner(w.transform);
            if (owner == null)
                continue; // not inside anything the mod draws — not the mod's to withhold

            if (!preStart)
            {
                // THE PROOF, MEASURED. This window is exactly what a veil keyed on IsOpen alone
                // would have cut off, and it is being let through. Counted once per window so a
                // 0.9 s dissolve is one entry and not eighty.
                int id = w.GetInstanceID();
                if (id != s_flashVeilSparedLast)
                {
                    s_flashVeilSparedLast = id;
                    s_flashVeilSpared++;
                    s_flashVeilSparedName = w.gameObject.name;
                }
                continue;
            }

            if (owner.RenderHidden || owner.OwnerRenderHidden)
                continue; // already withheld whole, by the reveal gate or by a surface
            if (ReferenceEquals(w.transform, owner.Target))
                continue; // never the converted window itself — see the header's refusals
            if (FlashVeils.Count >= FlashVeilMaxWindows)
                continue; // blast-radius cap reached: the rest draw
            if (FlashVeilGaveUp.Contains(w.GetInstanceID()))
                continue; // the deadline already released this one; it is never veiled again

            RaiseFlashVeil(w, owner);
        }
    }

    /// <summary>Is this window already carrying a veil? Linear over a list that holds at most
    /// <see cref="FlashVeilMaxWindows"/> entries and is empty on almost every frame.</summary>
    private static bool IsFlashVeiled(UIWindow w)
    {
        for (int i = 0; i < FlashVeils.Count; i++)
        {
            if (ReferenceEquals(FlashVeils[i].Window, w))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Which converted panel, if any, DRAWS this transform? Walks up to the conversion target of
    /// a live panel. Membership is decided by real ancestry and never by a name or a layer — a
    /// pooled child spends its first frames on the GAME's UI layer precisely because the mod-layer
    /// sweep has not reached it yet, so the layer is not a membership test here
    /// [[hierarchy-path-is-not-membership]].
    ///
    /// <para>Reached only by a candidate that survived the fast reject, i.e. approximately never.
    /// Bounded by the hierarchy depth and by <c>Active.Count</c>, neither of which is large.</para>
    /// </summary>
    private static ConvertedPanel? FindFlashVeilOwner(Transform t)
    {
        Transform? node = t;
        for (int depth = 0; node != null && depth < 64; depth++, node = node.parent)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                ConvertedPanel p = Active[i];
                if (!p.IsAlive)
                    continue;
                if (ReferenceEquals(node, p.Target))
                    return p;
            }
        }
        return null;
    }

    // ---- raising, applying and lifting --------------------------------------------------------

    /// <summary>Put a veil up and report it. Reports only when the veil actually withheld
    /// something: a subtree whose renderers were all culled already was never going to flash, and
    /// a line claiming a prevention that did not happen is an instrument asserting a mechanism it
    /// cannot observe.</summary>
    private static void RaiseFlashVeil(UIWindow w, ConvertedPanel owner)
    {
        FlashVeilEntry e;
        if (FlashVeilPool.Count > 0)
        {
            e = FlashVeilPool[FlashVeilPool.Count - 1];
            FlashVeilPool.RemoveAt(FlashVeilPool.Count - 1);
            e.Held.Clear();
            e.HeldAlpha.Clear();
        }
        else
        {
            e = new FlashVeilEntry();
        }
        e.Window = w;
        e.Owner = owner;
        e.Name = w.gameObject.name;
        e.StartFrame = Time.frameCount;

        int held = ApplyFlashVeil(e);
        if (held == 0)
        {
            // Nothing was drawing under it after all. Do not hold an entry (and do not claim a
            // prevention) for a subtree that had no visible renderer.
            e.Window = null;
            e.Owner = null;
            if (FlashVeilPool.Count < 8)
                FlashVeilPool.Add(e);
            return;
        }

        FlashVeils.Add(e);
        s_flashVeilSuppressed++;
        s_flashVeilRenderers += held;
        ReportFlashVeil(e.Name, overdue: false);
    }

    /// <summary>
    /// Withhold every currently-drawing <see cref="CanvasRenderer"/> under the veiled window and
    /// record exactly those. Inactive children are included deliberately: the game is repopulating
    /// this subtree and one it activates later in the same frame must not get a frame of its own.
    ///
    /// <para>BOTH BITS, and the second one is the one that survives a clipper — see the header's
    /// lever section for why <c>cull</c> alone loses to RectMask2D in the gap between LateUpdate
    /// and the draw. A renderer already withheld by an earlier pass is skipped and never
    /// re-recorded; a renderer the CLIPPER has un-culled since is re-culled without being recorded
    /// twice, because the record is keyed on the alpha this part parked, not on the cull bit.</para>
    /// </summary>
    /// <returns>How many renderers this call newly took hold of.</returns>
    private static int ApplyFlashVeil(FlashVeilEntry e)
    {
        UIWindow? w = e.Window;
        if (w == null)
            return 0;
        AssertNotInRenderPhase("pre-Start flash veil");

        w.transform.GetComponentsInChildren(true, FlashVeilScratch);
        int taken = 0;
        for (int i = 0; i < FlashVeilScratch.Count; i++)
        {
            CanvasRenderer cr = FlashVeilScratch[i];
            if (cr == null)
                continue;
            float alpha = cr.GetAlpha();
            if (alpha <= 0f)
            {
                // Already at zero. Either this part parked it on an earlier pass — in which case
                // re-assert the cull the clipper may have taken back, and do NOT record it again —
                // or it was authored transparent, in which case there is nothing to withhold and
                // nothing to restore. Both cases are handled by touching no record.
                if (!cr.cull)
                    cr.cull = true;
                continue;
            }
            if (cr.cull)
                continue; // uGUI is already not drawing it; leave its state entirely alone
            cr.cull = true;
            cr.SetAlpha(0f);
            e.Held.Add(cr);
            e.HeldAlpha.Add(alpha);
            taken++;
        }
        FlashVeilScratch.Clear();
        return taken;
    }

    /// <summary>
    /// Hand EXACTLY the recorded set back and clear it. Safe on a dead window and on a released
    /// panel — the renderers are held by reference and belong drawing wherever they now live, so a
    /// subtree restored into its 2D home is never stranded invisible.
    ///
    /// <para>Both writes are value-checked, which is what makes the restore exact rather than
    /// merely opposite: the cull is only cleared if this part's value is still on the renderer,
    /// and the alpha is only put back if the renderer still carries the zero this part wrote. A
    /// writer that took either over during the veil keeps it.</para>
    /// </summary>
    private static void LiftFlashVeil(FlashVeilEntry e)
    {
        AssertNotInRenderPhase("pre-Start flash veil lift");
        for (int i = 0; i < e.Held.Count; i++)
        {
            CanvasRenderer cr = e.Held[i];
            if (cr == null)
                continue;
            if (cr.cull)
                cr.cull = false;
            if (cr.GetAlpha() <= 0f)
                cr.SetAlpha(e.HeldAlpha[i]);
        }
        e.Held.Clear();
        e.HeldAlpha.Clear();
        e.Window = null;
        e.Owner = null;
    }

    // ---- the instrument -----------------------------------------------------------------------

    /// <summary>
    /// One line per DISTINCT finding. The change gate is the window name PLUS a doubling ladder on
    /// the prevention count — 1st, 2nd, 4th, 8th, 16th — and the ladder is the half that matters:
    /// a gate on the name alone would print once for a loadout screen of forty identical tab
    /// presses and then look like a stopped tick, which is exactly the failure
    /// [[a-held-instrument-reads-as-dead]] records. The ladder re-prints rarely enough never to
    /// re-create the flood ModBuild 331 removed, and often enough that the session totals on the
    /// line are never stale by more than a factor of two.
    ///
    /// <para>THE FALSIFIER PATH IS NOT GATED AT ALL. A deadline release is bounded by construction
    /// — a released window is blacklisted and never veiled again, and the whole part stands down
    /// once <see cref="FlashVeilGiveUpCap"/> windows have been released — so every one of them can
    /// print. Suppressing the line that says this part misfired to save log volume would be
    /// choosing not to hear the one thing worth hearing.</para>
    ///
    /// <para>Writes nothing any non-diagnostic reads — the counters it prints are written by the
    /// veil itself, never here [[a-write-inside-a-logger]].</para>
    /// </summary>
    private static void ReportFlashVeil(string name, bool overdue)
    {
        if (!overdue)
        {
            int rung = 0;
            for (int n = s_flashVeilSuppressed; n > 0; n >>= 1)
                rung++;
            int outcome = (name.GetHashCode() | 1) ^ (rung * 8191);
            if (outcome == s_flashVeilLastReported)
                return;
            s_flashVeilLastReported = outcome;
        }

        string body =
            $"'{name}': a UIWindow inside a converted panel was about to draw ONE FRAME of its "
            + "prefab — full alpha, un-populated labels — because Unity had not run its Start() "
            + "yet, and this frame was withheld before it was drawn. VERDICT: "
            + "UIWindow.HasGoneToStartingState == false, i.e. the window has NEVER BEEN SHOWN. "
            + "That is not the same question as UIWindow.IsOpen, which is equally false for a "
            + "window that is being HIDDEN right now — this part never touches one of those, and "
            + "the count of them stands beside the totals below. SESSION: "
            + $"PREVENTED {s_flashVeilSuppressed} flash frame(s) over {s_flashVeilRenderers} "
            + $"CanvasRenderer(s) held; LET THROUGH {s_flashVeilSpared} not-open-but-painting "
            + $"window(s) (last '{s_flashVeilSparedName}'); DEADLINE RELEASED {s_flashVeilGaveUp} "
            + $"veil(s) after {FlashVeilMaxFrames} frame(s). Worst scan cost this session "
            + $"{s_flashVeilWorstMicros} us.";

        if (overdue)
        {
            // THE FALSIFIER PATH. A veil that had to be released by its deadline is a veil that
            // was up on a window whose Start() never ran — which is exactly the shape of a window
            // the player was supposed to see being withheld by mistake. It is a WARNING, and the
            // number to watch is the deadline count: one is a pooled subtree the game parked, a
            // rising count means this part is firing on something it has misread and the answer is
            // to stand it down, not to tune the deadline.
            VRLog.Alert("WorldUI", "FLASH VEIL " + body
                + " THIS LINE IS THE FALSIFIER, NOT A STATUS: the veil above was lifted by its "
                + "deadline rather than by the game starting the window, so for up to "
                + $"{FlashVeilMaxFrames} frame(s) this part withheld a subtree the game had not "
                + "finished with. If the user reports a window that appeared late, appeared "
                + "partially, or did not appear at all, THIS is the line that names it and this "
                + "part is the cause.");
            return;
        }

        // HW-VERIFY: this is the line that says whether the 2026-09-03 report — one frame of the
        // CHARAKTER LÖSCHEN confirmation painting at full opacity when a personal quest is picked
        // — is PREVENTED rather than merely measured. Read three numbers. PREVENTED > 0 with no
        // FLASH VEIL warning line anywhere means the flash frames were withheld and nothing was
        // held past its deadline. LET THROUGH must be non-zero in any session containing a window
        // close animation (the encounter window's dissolve is the one the user asked for on this
        // same day): it counts the windows a veil keyed on IsOpen would have cut off, and a zero
        // there next to a session of closing windows means the discriminator is not discriminating
        // and the fade-out proof in this file's header is wrong. PREVENTED at zero across a
        // session in which he still sees the flash means the scan never reached the subtree, i.e.
        // the remedy is inert rather than ineffective, and the next round measures the candidate
        // population instead of the veil.
        VRLog.Note("WorldUI", "FLASH VEIL " + body);
    }
}
