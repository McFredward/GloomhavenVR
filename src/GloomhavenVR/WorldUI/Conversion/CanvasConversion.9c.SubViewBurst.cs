using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 9c (THE SUB-VIEW SETTLE BURST — the character screen's tab change).
// A NEW part file for the reason parts 6, 8 and 9b give: the refactor guard tracks the partial
// class's member and static-initializer order, and the filename sort ('.9.' < '.9b.' < '.9c.')
// appends this part AFTER every existing one, so nothing existing moves. No static field
// declared here carries an initializer — the whole of this part's state lives on the existing
// per-panel FixedFitState, so no static constructor entry is added and the initializer order the
// guard tracks is untouched.

internal static partial class CanvasConversion
{
    // ==========================================================================================
    // WHY THIS EXISTS — USER REPORT 2026-09-03, THE PRE-SCENARIO LOADOUT SCREEN
    // ==========================================================================================
    //
    // Verbatim, translated:
    //   (b) "Every time I pick a personal quest, SOMETHING ELSE flashes up for a few frames. Too
    //       short to make out what. I would like that suppressed."
    //   (c) "After the window with the personal quest appears, the quests are first shown SLIGHTLY
    //       HIGHER and then suddenly POP DOWN a bit. I would not like that either."
    //
    // WHAT (c) IS, MEASURED RATHER THAN INFERRED, AND IT IS THIS FILE'S SUBJECT. The character
    // screen ('New Party display') is the FIXED-SIZE window: ApplyFixedFitCore pins one host size
    // and SEATS whatever sub-view the game opens beside the character column instead of resizing
    // the window around it. The seat is a WRITE, and the ModBuild 385 log prints the exact number
    // for the battle-goal picker — the sub-view the personal quest is chosen in:
    //
    //     FIXED FIT … sub-view 'UI Battle Goal Picker Window' scale 1.000 -> 1.000 and
    //     shifted by 14,-381 px onto the column seam x=-654
    //
    // -381 px. Until that write lands the picker is drawn at its AUTHORED HOME, i.e. 381 px too
    // HIGH, and the write is what drops it. That is (c), quantitatively: "erst etwas höher …
    // und ploppen dann plötzlich etwas nach unten".
    //
    // HOW LONG THE WRONG POSITION IS ON SCREEN, AND WHY THE ANSWER IS A CADENCE AND NOT A BUG IN
    // THE SETTLE GATE. Two independent numbers multiply:
    //
    //   * FixedFitSettleChecks = 2 — a candidate seat must REPEAT before it is written. That gate
    //     is correct and this file does not touch it: it is what stops the fit chasing a show
    //     animation (ModBuild 201 — "a ramp never repeats a value; a real dislocation does").
    //   * FitCheckIntervalFrames = 30 — the fit is SAMPLED every 30 frames.
    //
    // Two checks 30 frames apart is up to 60 frames = 0.67 s at 90 Hz (up to ~1.0 s counting the
    // wait for the first check) with the picker sitting 381 px high, in full view. The gate was
    // designed as "the candidate must repeat"; nothing about it asks for a THIRD of a second
    // between the two samples. THE SPACING IS THE DEFECT, NOT THE GATE.
    //
    // WHAT THIS PART DOES. When the set of sub-views the game has open inside a fixed-size window
    // CHANGES — a tab press, the battle-goal picker opening, a personal quest being picked — the
    // fixed fit is asked for a small, bounded run of EXTRA checks at close spacing instead of
    // waiting out the 30-frame cadence. The settle gate then reaches its two checks in
    // SubViewBurstCheckIntervalFrames * FixedFitSettleChecks = 6 frames (67 ms) rather than 60.
    //
    // WHAT IT DELIBERATELY DOES NOT DO, because each of these is a rule this repository has paid
    // for:
    //
    //   * IT DOES NOT WEAKEN THE SETTLE GATE. FixedFitSettleChecks, the candidate comparison and
    //     the "home is re-read until the first write" rule in MeasureFixedFitParts are untouched.
    //     A sub-view whose show animation is still ramping produces a different candidate every
    //     sample and is STILL never written — the burst simply discovers that in 12 frames instead
    //     of 120. [[dont-win-a-write-war]]
    //   * IT DOES NOT SKIP A FIT. Re-fits stay REQUIRED and stay exactly as frequent as before
    //     once the burst ends; the burst only ever makes a check happen SOONER. Nothing here can
    //     make the supersample's capture frame disagree with what the camera projects into it.
    //   * IT DOES NOT HIDE A LIVE WINDOW. The reveal gate's complete render hide (part 6) is for a
    //     window that has never been seen; blinking an on-screen window for six frames to conceal
    //     a six-frame correction trades a small pop for a large one.
    //   * IT DOES NOT RUN A PER-FRAME WALK. See THE COST below.
    //
    // ==========================================================================================
    // (b) — WHAT ACTUALLY FLASHES, AND HOW MUCH OF IT THIS PART CAN REACH
    // ==========================================================================================
    //
    // The flash is NOT a stray window. It is this same panel drawing its WHOLE canvas for ~3
    // frames, and the mod's own hit-rect instrument already measures it in the ModBuild 385 log —
    // four times, each within a few frames of a battle-goal click:
    //
    //     HIT RECT '…New Party display' (commit #5)  … DRAWN CONTENT 1964x1453 px at (0,-187)
    //                                                  from 597 visible graphic(s)
    //     HIT RECT '…New Party display' (commit #6)  … DRAWN CONTENT  659x1080 px at (-653,0)
    //                                                  from  85 visible graphic(s)
    //
    // 597 and 696 graphics against a settled 85-125, with the FIXED FIT line naming what they are:
    // "BACKDROP CENSUS: 5 full-frame plate(s) inside it, the largest 'UI Character Confirmation
    // Box' at 1920x1080 px". That is a real, painted state — the user's own video shows the delete
    // -character confirmation plate, the three character columns and their unpopulated "New Text"
    // placeholders, all at full opacity, for one 30 fps frame. It is not a measurement ghost.
    //
    // TWO CONTRIBUTORS, AND ONLY THE SECOND IS THE MOD'S:
    //   1. The game itself has several sub-view roots active at once for the frames in which it
    //      swaps them. Nothing in the conversion asked for that and nothing here can un-ask it.
    //   2. The children the game creates during that repopulation are born on the GAME's UI layer.
    //      ApplyModLayer moves a converted subtree onto the mod layer precisely so the game's UI
    //      Camera cannot double-draw it, and it re-sweeps on the SAME 30-frame cadence — so a
    //      repopulation is followed by up to a third of a second in which the new children are the
    //      one part of the window the game's UI Camera still draws. The 385 log states the size of
    //      that hole for this window in its own words: "1358 pooled/late transform(s) … joined
    //      capture layer 23 (a repopulating window brings children on the game's UI layer; until
    //      they are swept they would be MISSING from the capture and drawn straight into the eye
    //      instead) … This sweep ran because the window reached its PERIODIC CADENCE."
    //
    // So the burst is the SAME remedy for both halves and it is one mechanism, not two: an open-set
    // change is exactly the event after which the panel must be re-measured AND re-swept, and it is
    // the event neither the fit cadence nor the layer cadence has ever been told about.
    // CanvasConversion.4.Lifecycle asks <see cref="SubViewBurstRunning"/> for the second half.
    // (b) and (c) are one event seen twice; this is one fix, not two.
    //
    // HONESTLY: contributor 1 is NOT fixed by this. If the next hardware round still reports a
    // flash, the remaining cause is the game's own multi-root transient and the only mod-side
    // answer left is to veil a sub-view root the fit has not placed yet — which is a hide, which
    // needs its own deadline so a picker can never latch invisible ([[gated-remedy-never-ran]],
    // and the ModBuild 230 ruling that there must never be an empty window). That is deliberately
    // NOT built here on a guess. The instrument below is what decides whether it is needed.
    //
    // ==========================================================================================
    // THE COST, AND WHY IT IS SPREAD RATHER THAN PACKED
    // ==========================================================================================
    //
    // A fixed-fit check is not free: MeasureFixedFitParts walks every active Graphic under the
    // window (861 of them on this panel, per the 385 log's own EMPTY WINDOW line) and transforms
    // four corners of each into host space. The 385 log prices a whole CanvasConversion Update
    // step on this window at 0.55-5.55 ms and CanvasConversion.Late at 2.22-21.96 ms.
    //
    // Running those checks BACK TO BACK would therefore add several milliseconds to each of six
    // consecutive frames — at the exact moment the log already shows 43-44 ms spikes. So the burst
    // does NOT run every frame. It runs one check every SubViewBurstCheckIntervalFrames frames,
    // at most SubViewBurstMaxChecks times:
    //
    //   * NO FRAME EVER CARRIES MORE THAN ONE FIXED-FIT CHECK — the same cost the ordinary cadence
    //     already pays on its own check frames. The per-frame worst case is UNCHANGED.
    //   * The whole added work per open-set change is at most SubViewBurstMaxChecks extra checks,
    //     against the ~0.4 checks the 30-frame cadence would have run over the same 12 frames.
    //   * Between bursts the added cost is ONE DICTIONARY LOOKUP per converted panel per frame
    //     (TryGetLiveFixedFit), and for the one panel that lookup finds, ONE INTEGER SIGNATURE:
    //     six property reads on the game's own NewPartyDisplayUI singleton plus a shallow parent
    //     walk each. There is no Graphic walk, no GetComponent, no GetComponentsInChildren and no
    //     allocation on the idle path, and no other window in the game gets past the lookup.
    //     [[findobjectsoftype-is-the-default-suspect]]
    // ==========================================================================================

    /// <summary>
    /// Frames between two checks of a burst. THREE, and the number is a trade, not a taste: the
    /// visible wrongness lasts <see cref="FixedFitSettleChecks"/> times this, and every frame in
    /// between is a frame that does NOT pay for a fixed-fit measurement. At 3 the settle gate is
    /// satisfied 6 frames (67 ms at 90 Hz) after the sub-view opens, against 60 frames on the
    /// bare <see cref="FitCheckIntervalFrames"/> cadence — and two thirds of those frames cost
    /// nothing.
    /// </summary>
    private const int SubViewBurstCheckIntervalFrames = 3;

    /// <summary>
    /// Checks one burst may force. FOUR: <see cref="FixedFitSettleChecks"/> to satisfy the settle
    /// gate, one for the pass that actually writes, and one spare for a measurement that arrives a
    /// frame late. It is a CAP and not a target — a burst that settles earlier ends earlier (see
    /// <see cref="NoticeFixedFitSettled"/>), and the overwhelming majority do. A burst that spends
    /// all four without settling has met a sub-view whose seat never repeats, i.e. an animation
    /// still ramping, and handing it back to the ordinary cadence is the correct answer: the gate
    /// that refused to write is the ModBuild 201 protection doing its job.
    /// </summary>
    private const int SubViewBurstMaxChecks = 4;

    /// <summary>
    /// THE PER-FRAME GATE, and it is a dictionary lookup rather than
    /// <see cref="IsFixedSizeWindow"/> ON PURPOSE. That predicate is the right ANSWER to "is this
    /// the character screen" and the wrong INSTRUMENT to ask 90 times a second per panel: it does
    /// a <c>GetComponent&lt;UIWindow&gt;</c>, reads the game's singleton and drives the
    /// <c>FIXED FIT GATE</c> log line, all of which are priced for the 30-frame fit cadence and
    /// none of which change between two frames. A <see cref="FixedFitState"/> that has CAPTURED
    /// its size is proof the fixed fit has already claimed this panel — same answer, one hash
    /// lookup, no component search and no extra log traffic.
    ///
    /// <para>False for every other window in the game, so nothing else in the mod pays for this
    /// part at all.</para>
    /// </summary>
    private static bool TryGetLiveFixedFit(ConvertedPanel? panel, out FixedFitState fx)
    {
        fx = OrphanFixedFit;
        if (panel == null || panel.HostGo == null)
            return false;
        if (!FixedFits.TryGetValue(panel.HostGo.GetInstanceID(), out FixedFitState? entry)
            || entry == null || !entry.SizeCaptured)
            return false;
        fx = entry;
        return true;
    }

    /// <summary>
    /// Is a settle burst about to force a check on <paramref name="panel"/> THIS FRAME? Read by
    /// <c>CanvasConversion.Tick</c> to run the pooled/late-child sweeps (AdoptNestedCanvases +
    /// ApplyModLayer) on the burst's check frames as well as on the periodic ones — contributor 2
    /// of (b) in this part's header.
    ///
    /// <para>"THIS FRAME", not "while a burst is running", and the difference is the whole cost
    /// argument. <c>ApplyModLayer</c> walks the converted subtree — 2114 transforms on this window
    /// in the ModBuild 385 log — so answering "a burst is in flight" would run that walk on all
    /// ~12 frames of a burst instead of the 4 it has anything to do on. The due-check test is
    /// evaluated BEFORE <see cref="TickSubViewBurst"/> advances the schedule (Tick runs the sweep
    /// block above <c>TickFit</c>), so it names exactly the frames on which a check is forced.
    /// </para>
    ///
    /// <para>Cheap on every other frame and every other window: one dictionary lookup on a table
    /// that holds at most the character screen (see <see cref="TryGetLiveFixedFit"/>).</para>
    /// </summary>
    internal static bool SubViewBurstRunning(ConvertedPanel? panel) =>
        TryGetLiveFixedFit(panel, out FixedFitState fx)
        && fx.BurstChecksLeft > 0
        && Time.frameCount >= fx.BurstNextCheckFrame;

    /// <summary>
    /// IS THIS WINDOW'S CONTENT MID-TRANSITION? True from the frame the set of open sub-views
    /// changed until the fixed fit reports it settled (or the burst spends its cap) — i.e. exactly
    /// the frames on which this window's painted union is a TRANSIENT and not its steady state.
    ///
    /// <para>OFFERED FOR THE CONSUMERS THAT SOLVE SOMETHING FROM THAT UNION, and there is at least
    /// one: <c>LoadoutConfirmPark</c> seats the pre-scenario continue control against the window's
    /// painted union, and in the ModBuild 385 log that seat swings by 733-1285 px and lands
    /// <c>SEAT LANE: OVER</c> four times — each time on a union of 595-696 graphics, i.e. each time
    /// on a frame of the transient the user photographed. The seat is not wrong; the union it was
    /// handed was. A consumer that asks this first can hold its last good answer instead.</para>
    ///
    /// <para>Distinct from <see cref="SubViewBurstRunning"/> ON PURPOSE — that one names the frames
    /// a CHECK is forced (so a sweep runs once per check and not once per frame), this one names
    /// the whole transition. Two questions, two names; conflating them is [[two-fans-one-name]].
    /// </para>
    /// </summary>
    internal static bool SubViewSetInFlight(ConvertedPanel? panel) =>
        TryGetLiveFixedFit(panel, out FixedFitState fx) && fx.BurstActive;

    /// <summary>
    /// THE OPEN-SET SIGNATURE, and it is DELIBERATELY NOT
    /// <see cref="FixedFitState.OpenSignature"/>.
    ///
    /// <para>The two answer different questions and are never compared with each other.
    /// <c>OpenSignature</c> is computed inside <c>SolveSubViewPlacement</c>, downstream of the
    /// per-graphic measurement walk, and it identifies the set a frozen SOLUTION was solved for.
    /// This one is computed before any walk at all and only has to CHANGE when the open set
    /// changes; it may be coarser, and it is (it skips the nested-candidate drop
    /// <c>CollectActiveSubViews</c> does, because dropping an inner root cannot change whether the
    /// set changed). Writing one number and reading it as the other is
    /// [[two-fans-one-name]]; they carry different names here for that reason.</para>
    ///
    /// <para>Asked of the game's own serialized references, exactly as
    /// <c>CollectActiveSubViews</c> asks — never of the hierarchy and never by name. Returns 0
    /// when nothing is open or the game's singleton is unavailable, and 0 is a legitimate value
    /// (every tab closed) rather than an error code: a transition to or from it is an open-set
    /// change like any other.</para>
    /// </summary>
    private static int SubViewOpenSetSignature(ConvertedPanel panel)
    {
        if (panel.Target == null)
            return 0;
        NewPartyDisplayUI? display;
        try
        {
            display = NewPartyDisplayUI.PartyDisplay;
        }
        catch (System.Exception)
        {
            return 0;
        }
        if (display == null)
            return 0;

        int signature = 0;
        int members = 0;
        try
        {
            MixOpenSubView(panel, display.AbilityCardsDisplay, ref signature, ref members);
            MixOpenSubView(panel, display.EnhancementCardsDisplay, ref signature, ref members);
            MixOpenSubView(panel, display.PerkManager, ref signature, ref members);
            MixOpenSubView(panel, display.CharacterSelector, ref signature, ref members);
            MixOpenSubView(panel, display.ItemInventoryDisplay, ref signature, ref members);
            MixOpenSubView(panel, display.BattleGoalWindow, ref signature, ref members);
        }
        catch (System.Exception)
        {
            return 0;
        }
        return members == 0 ? 0 : signature * 31 + members;
    }

    /// <summary>One member of <see cref="SubViewOpenSetSignature"/>: order-independent (XOR of
    /// instance IDs) plus a count, so "the picker alone" and "the picker plus the perks view" are
    /// different numbers. Same membership test as <c>AddSubViewCandidate</c> — active in the
    /// hierarchy AND a strict descendant of the conversion target.</summary>
    private static void MixOpenSubView(ConvertedPanel panel, Component? c,
        ref int signature, ref int members)
    {
        if (c == null || panel.Target == null)
            return;
        Transform t = c.transform;
        if (ReferenceEquals(t, panel.Target) || !c.gameObject.activeInHierarchy)
            return;
        if (FixedFitLevelsUp(t, panel.Target) <= 0)
            return;
        signature ^= t.GetInstanceID();
        members++;
    }

    /// <summary>
    /// Arm and drive the settle burst. Called from <c>TickFit</c> immediately BEFORE its
    /// <see cref="FitCheckIntervalFrames"/> gate and AFTER every one-shot / pre-reveal early
    /// return, so the reveal gate and the one-shot menus are provably out of reach of it: the
    /// only thing this method ever writes is <c>panel.FitNextCheckFrame</c>, and only for a
    /// window that is already <c>FitMeasuredOnce</c> and on the fixed-fit path.
    /// </summary>
    private static void TickSubViewBurst(ConvertedPanel panel)
    {
        if (!panel.FitMeasuredOnce || !TryGetLiveFixedFit(panel, out FixedFitState fx))
            return;

        int signature = SubViewOpenSetSignature(panel);

        if (!fx.BurstSigValid)
        {
            // THE FIRST OBSERVATION IS A BASELINE, NOT AN EVENT. Arming here would spend four
            // checks on a window that has not changed and would put a burst in the session count
            // that no tab press produced — a counter whose first entry is an artefact of its own
            // startup is a counter nobody can read.
            fx.BurstSigValid = true;
            fx.BurstSignature = signature;
            return;
        }

        if (signature != fx.BurstSignature)
        {
            fx.BurstSignature = signature;
            // A change DURING a burst re-arms the same burst rather than stacking a second one:
            // the game swapping two sub-views over three frames is ONE event to the player, and
            // counting it twice would make the report below read as churn that is not there.
            if (!fx.BurstActive)
            {
                fx.BurstActive = true;
                fx.Bursts++;
                fx.BurstStartFrame = Time.frameCount;
                fx.BurstChecksRun = 0;
                fx.BurstSettled = false;
            }
            fx.BurstChecksLeft = SubViewBurstMaxChecks;
            fx.BurstNextCheckFrame = Time.frameCount;
        }

        // EXPIRY IS DECIDED ONE TICK LATE, AND THAT IS THE POINT. The final forced check runs
        // BELOW this line, after the counter that pays for it has already reached zero — so a
        // burst is only spent once a whole tick has passed with no NoticeFixedFitSettled. Deciding
        // it at the decrement would have reported "never settled" for the majority of bursts,
        // which settle on exactly that last check. Placed after the re-arm above so a sub-view set
        // that changes again on this frame extends the burst instead of ending it.
        if (fx.BurstActive && fx.BurstChecksLeft <= 0)
        {
            fx.BurstActive = false;
            fx.BurstsExpired++;
            ReportSubViewBurst(panel, fx, settled: false);
            return;
        }

        if (fx.BurstChecksLeft <= 0 || Time.frameCount < fx.BurstNextCheckFrame)
            return;

        fx.BurstChecksLeft--;
        fx.BurstChecksRun++;
        fx.BurstNextCheckFrame = Time.frameCount + SubViewBurstCheckIntervalFrames;
        panel.FitNextCheckFrame = Time.frameCount; // let TickFit's gate through THIS frame
    }

    /// <summary>
    /// COUNT WHAT IS PAINTING, ON THE FRAMES IT PAINTS. Called from <c>ApplyFixedFitCore</c> right
    /// after <c>MeasureFixedFitParts</c>, and only while a burst is in flight — which is exactly
    /// the handful of frames after the open set changed, i.e. the frames the user's report (b)
    /// happens on.
    ///
    /// <para>WHY THIS IS WORTH A FIELD. The 385 log caught the flash state four times in a whole
    /// session, and only because the 30-frame hit-rect cadence happened to sample it; the four
    /// catches are the four <c>LOADOUT CONFIRM SEAT LANE: OVER</c> verdicts in the same log. A
    /// census taken on the burst's own frames does not depend on that luck. It reads the counts the
    /// measurement walk has already filled in and writes nothing anywhere else, so it costs an add
    /// and a compare.</para>
    /// </summary>
    private static void NoticeBurstCensus(FixedFitState fx, Vector2 size)
    {
        if (!fx.BurstActive)
            return;
        int graphics = fx.BaseGraphics;
        for (int i = 0; i < fx.Views.Count; i++)
        {
            SubViewFit v = fx.Views[i];
            if (v.Visible)
                graphics += v.Graphics;
        }
        if (fx.BurstChecksRun <= 1)
        {
            fx.BurstFirstGraphics = graphics;
            fx.BurstPeakGraphics = 0;
            fx.BurstPeakUnion = Vector2.zero;
        }
        if (graphics > fx.BurstPeakGraphics)
        {
            fx.BurstPeakGraphics = graphics;
            fx.BurstPeakUnion = size;
        }
    }

    /// <summary>
    /// The fixed fit found nothing left to write. Ends any burst in flight — there is no reason to
    /// spend the remaining checks measuring a window that has stopped moving, and stopping here is
    /// what keeps the ordinary case at one or two extra checks rather than the
    /// <see cref="SubViewBurstMaxChecks"/> cap. Called from <c>ApplyFixedFitCore</c>'s settled
    /// branch, which is the one place that knows.
    /// </summary>
    private static void NoticeFixedFitSettled(ConvertedPanel panel, FixedFitState fx)
    {
        if (!fx.BurstActive)
            return;
        fx.BurstActive = false;
        fx.BurstChecksLeft = 0;
        fx.BurstSettled = true;
        ReportSubViewBurst(panel, fx, settled: true);
    }

    /// <summary>
    /// One line per DISTINCT burst outcome, so a loadout screen of tab presses that all behave the
    /// same way says so once. The change gate is the outcome itself — settled/expired plus the
    /// number of frames it took — because that pair is the whole finding; repeating it for every
    /// identical burst is the flood ModBuild 331 removed, and a counter that only ever grows is
    /// [[a-held-instrument-reads-as-dead]]. The SESSION totals ride along on whichever line does
    /// print, so a run whose outcomes never vary still states how many bursts there were.
    /// </summary>
    private static void ReportSubViewBurst(ConvertedPanel panel, FixedFitState fx, bool settled)
    {
        int frames = Mathf.Max(0, Time.frameCount - fx.BurstStartFrame);
        if (frames > fx.BurstWorstFrames)
            fx.BurstWorstFrames = frames;
        int outcome = (settled ? 1 : -1) * (frames + 1);
        if (outcome == fx.BurstLastReported)
            return;
        fx.BurstLastReported = outcome;

        string name = panel.HostGo != null ? panel.HostGo.name : "?";
        // HW-VERIFY: this is the line that says whether the 2026-09-03 report (b)+(c) is fixed.
        // The number to read is FRAMES: the seat the user sees pop was landing up to 60 frames
        // (0.67 s) after the sub-view opened, and the whole of this change is to make that a
        // handful. A run of this screen with no such line at all means the burst never armed —
        // the open-set signature never moved — and the remedy is inert rather than ineffective.
        VRLog.Note("WorldUI", $"SUB-VIEW SETTLE BURST '{name}': the set of open sub-views changed, "
            + "so the fixed fit was checked at close spacing instead of waiting out its "
            + $"{FitCheckIntervalFrames}-frame cadence. THIS BURST: "
            + (settled
                ? $"SETTLED after {frames} frame(s) and {fx.BurstChecksRun} check(s)"
                : $"SPENT ALL {SubViewBurstMaxChecks} check(s) over {frames} frame(s) WITHOUT "
                  + "settling, and is handed back to the ordinary cadence — a seat that never "
                  + "repeats is a sub-view still animating, and the settle gate refusing to write "
                  + "it is the ModBuild 201 protection working, not a failure of this burst")
            + $". PAINTED DURING THE BURST: {fx.BurstFirstGraphics} visible graphic(s) on the "
            + $"first check, PEAK {fx.BurstPeakGraphics} over a union of "
            + $"{fx.BurstPeakUnion.x:F0}x{fx.BurstPeakUnion.y:F0} px. THAT PAIR IS REPORT (b). The "
            + "user photographed one frame of the character screen with its DELETE-CHARACTER "
            + "confirmation, the mercenary-create screen and dozens of un-populated TextMeshPro "
            + "labels all painting at once; the settled state of this window is 85-128 graphic(s) "
            + "and the 385 log measured 597 and 696 in that state. A PEAK near the settled count "
            + "means the tree no longer lights up; a PEAK in the hundreds means it still does. "
            + "WHY IT LIGHTS UP, read out of the game's own source rather than guessed: those "
            + "graphics are pooled/repopulated UIWindow subtrees drawing ONE FRAME AT THEIR "
            + "AUTHORED STATE, before their own Start() has run. UIWindow.m_CurrentVisualState is "
            + "a FIELD INITIALISER (UIWindow.cs:123) that only becomes real in Start() "
            + "(UIWindow.cs:358), and it is Start() that drives the alpha to 0 and blocksRaycasts "
            + "to false. Unity runs Start() at the top of the frame AFTER activation, so the frame "
            + "in between renders the subtree at its prefab alpha with its labels still reading "
            + "the TextMeshPro default 'New Text' — which is exactly what the user photographed. "
            + "Nothing was activated by the mod and no inherited alpha was written by anyone; the "
            + "content simply has not been initialised yet. THE TERM THAT NAMES IT is "
            + "UIWindow.IsOpen (UIWindow.cs:317, m_CurrentVisualState == Shown), false for every "
            + "one of those subtrees in that frame. AND THE TRAP FOR WHOEVER ACTS ON IT: IsOpen "
            + "goes false at the START of a hide transition (the state is assigned before the "
            + "tween, UIWindow.cs:548), so a veil keyed on IsOpen alone would also cut off every "
            + "close animation. The condition wanted is 'has never been shown', which IsOpen does "
            + "not distinguish from 'is being hidden right now'"
            + $". LAST SEAT SOLVED: sub-view '{fx.ViewName}' at scale {fx.ViewScale:F3}, group "
            + $"shift {fx.ViewShift.x:F0},{fx.ViewShift.y:F0} px; the base column has been "
            + $"re-aligned {fx.BaseWrites} time(s) and sub-views re-seated {fx.Shifts} time(s) "
            + $"this session. SESSION: {fx.Bursts} burst(s), {fx.BurstsExpired} of them spent to "
            + $"the cap, worst {fx.BurstWorstFrames} frame(s) from the open-set change to the "
            + "seat landing. HOW TO READ THIS. The user's report (c) — 'the quests are shown "
            + "slightly higher and then suddenly pop down' — IS this seat arriving late: the 385 "
            + "log has the battle-goal picker being shifted by -381 px onto the column seam, and "
            + "until that write lands the picker is drawn 381 px too high. WORST is therefore the "
            + "number that bounds how long the wrong position was on screen. If WORST is small "
            + "and he still reports the pop, the seat is not what moves and the next round must "
            + "measure the picker's own rect instead. If bursts are spent to the cap, the seat is "
            + "chasing an animation and the answer is to place a root the game does not drive — "
            + "never to lower the settle gate, which is the only thing standing between this "
            + "window and a placement solved out of one frame of a tween.");
    }
}
