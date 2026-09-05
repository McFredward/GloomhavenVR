using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 9g (THE SUB-VIEW SEAT VEIL — a sub-view is not drawn before it is
// seated). A NEW part file for the reason parts 6, 8, 9b, 9c, 9d, 9e and 9f give: the refactor
// guard tracks the partial class's member and static-initializer order, and the filename sort
// ('.9.' < '.9b.' < … < '.9f.' < '.9g.') appends this part AFTER every existing one, so nothing
// existing moves. Every static field declared here is a scratch buffer or the veil's own hold
// table, and not one of them reaches a static field of another part, so check-partial-order.py's
// property still holds; all per-window state lives on the existing per-panel FixedFitState.

internal static partial class CanvasConversion
{
    // ==========================================================================================
    // WHY THIS EXISTS — USER REPORT 2026-09-05, AND HE HAD REPORTED IT BEFORE
    // ==========================================================================================
    //
    // Verbatim: "Kleinigkeit, die ich schon einmal angesprochen hatte: In der 3D-Map-Umgebung,
    // wenn man die private Quest auswählen muss, sieht man kurz, wenn das Fenster geladen ist, wie
    // die Quest an einer anderen Stelle steht (weiter oben) und dann runter ploppt nach ca. 1 s.
    // Das möchte ich nicht — die auswählbare Quest soll direkt an der richtigen Stelle auftauchen."
    //
    // "DIE PRIVATE QUEST" IS HIS OWN NAME FOR THE BATTLE-GOAL PICKER, and it is not a guess: he
    // used the same words for the same window on 2026-08-23, quoted at the head of
    // SubViewRevival.cs — "Sobald die private Quest für die Charactere ausgewählt wird, bloppt das
    // Character-UI Fenster wieder auf". It is 'UI Battle Goal Picker Window', a sub-view the game
    // opens INSIDE the character screen ('New Party display'), which is the map room's one
    // FIXED-SIZE window.
    //
    // ==========================================================================================
    // WHAT THE ModBuild 433 LOG PROVES (.planning/debug/LogOutput.log — line numbers are its own)
    // ==========================================================================================
    //
    //   :5836  FIXED FIT 'New Party display' APPLIED — "no sub-view is open, so nothing is scaled,
    //          no seat is written"; "fixed fit: 1 comparison(s) made"; the pass writes the host
    //          size and re-aligns the character column. THE PICKER IS NOT OPEN YET.
    //   :5849  EMPTY WINDOW BACK: 'New Party display' … "is visible again after 16.2 s dormant".
    //   :5853  MODAL DIAG … child 'UI Battle Goal Picker Window' enabled=True  ⇒ IT IS DRAWING.
    //   :5882  WINDOW MATERIALISE APPEAR … ended (completed) after 0.36s over 27 frame(s).
    //   :5884  FIXED FIT APPLIED — "wrote sub-view 'UI Battle Goal Picker Window' scale 1.000 →
    //          1.000 and SHIFTED BY 14,-168 px onto the column seam x=-654"; and, in the same
    //          line, "fixed fit: 3 comparison(s) made, 3 deviation(s) found, 1 DEFERRED BY THE
    //          SETTLE GATE".
    //   :5889  uGUI hover ENTER: 'UI Battle Goal Picker Slot'  ⇒ he is already pointing at it.
    //
    // THE WRITER IS OURS AND IT IS NAMED: CanvasConversion.3.Fit.cs, the sub-view seat inside
    // ApplyFixedFitCore ("v.View.localScale = …; vr.anchoredPosition = v.HomeAnchored +
    // v.WantShift / …"). -168 px at the 1.050 mm per authored px that same line prints is 176 mm
    // of drop, in full view. WHAT IT WAS WAITING FOR is two things multiplied, and the log states
    // both: FixedFitSettleChecks = 2 (a candidate seat must repeat before it is written) times
    // FitCheckIntervalFrames = 30 (the fit is sampled every 30 frames) = 60 frames, plus the wait
    // for the first of those checks — up to ~90 frames, i.e. ~1.0 s at 90 Hz. That is his "ca.
    // 1 s", to the number.
    //
    // ==========================================================================================
    // WHY THE ModBuild 389 REMEDY DID NOT FIRE — AND ITS OWN INSTRUMENT SAID SO
    // ==========================================================================================
    //
    // Part 9c exists for this exact report (its header quotes the 2026-09-03 round: "the quests
    // are first shown SLIGHTLY HIGHER and then suddenly POP DOWN a bit") and compresses those 60
    // frames to 6. It never ran. Every FIXED FIT line in the 433 session ends "SUB-VIEW BURST
    // STATE: 0 burst(s) armed", and `SUB-VIEW SETTLE BURST` occurs ZERO times in 16 562 lines —
    // which is the falsifier that part's own HW-VERIFY note asked for, in its own words: "A run of
    // this screen with no such line at all means the burst never armed … the remedy is inert
    // rather than ineffective."
    //
    // The cause is fixed in part 9c and stated there: both halves of TickSubViewBurst sat behind
    // `panel.FitMeasuredOnce`, and for this window that flag is false for as long as the window is
    // DARK — which is until the sub-view arrives. The first observation the burst could ever take
    // was therefore the changed state, recorded as its BASELINE. [[a-claim-must-not-measure-itself]]
    //
    // ==========================================================================================
    // WHY THE BURST ALONE IS STILL NOT THE ANSWER HE ASKED FOR
    // ==========================================================================================
    //
    // With 9c repaired the seat lands 6 frames after the picker opens instead of ~90. SIX FRAMES
    // IS STILL SIX FRAMES OF THE WRONG POSITION, and what he asked for is not a shorter pop:
    // "die auswählbare Quest soll direkt an der richtigen Stelle auftauchen". A pop cannot be
    // removed by measuring faster; it can only be removed by not drawing the wrong thing. So this
    // part withholds the sub-view until its seat exists.
    //
    // AND LOWERING THE SETTLE GATE IS NOT AVAILABLE. Writing the seat on the first candidate would
    // remove the wait and re-open ModBuild 201: the placement is FROZEN once written ("no later
    // measurement may re-scale or re-seat a view that is already on screen"), so a seat solved out
    // of one frame of a show animation would stay wrong for the window's whole life. Part 9c's own
    // closing sentence rules it out — "never to lower the settle gate" — and this part obeys it.
    //
    // ==========================================================================================
    // WHAT THE PLAYER SEES DURING THE HOLD, AND WHY THIS IS NOT AN EMPTY WINDOW
    // ==========================================================================================
    //
    // The standing ruling ("Es darf niemals leere Fenster geben", ModBuild 226/230) is about a
    // FLOATED WINDOW with nothing on it. Nothing here touches a window, a float, a grab bar or the
    // reveal gate: the veil is scoped to the sub-view ROOTS the game opened inside an already
    // standing, already visible character screen. Throughout the hold the player sees the window
    // exactly as it was a frame earlier — the character column (85-128 drawn graphics, per the
    // FIXED FIT line's own census), its frame, its grab bar and its backing plate — with the
    // arriving picker simply not yet painted. Then the picker appears, once, at its final seat.
    //
    // AND IT CANNOT LATCH. The hold is released by ONE named condition — the fixed fit has written
    // a seat for the open set that is standing right now — plus a bounded backstop that releases
    // it anyway and says so at a tier the default log level prints. The backstop is DERIVED and
    // not chosen: SubViewSeatVeilMaxFrames is the un-bursted cadence's own worst case, so reaching
    // it means neither the burst nor the ordinary cadence produced a seat, which is a fault to
    // report and not a budget to tune. [[gated-remedy-never-ran]], [[escape-never-measured-its-premise]]
    //
    // ==========================================================================================
    // THE LEVER, AND WHY IT IS THE ONE PART 9d ALREADY PROVED
    // ==========================================================================================
    //
    // CanvasRenderer.SetAlpha(0), recorded per renderer and handed back one for one — the ALPHA
    // HALF of the pre-Start flash veil's lever (part 9d) and deliberately NOT its cull half. A
    // CanvasGroup alpha was never available: that is exactly the state UIWindow's own show/hide
    // tween drives, and writing it would be a write war with the game over a value it re-derives
    // every frame [[dont-win-a-write-war]]. And `cull` is not available either, for a reason
    // specific to this consumer and spelled out on ApplySubViewSeatVeil: the fixed fit's own
    // visibility verdict SKIPS a culled graphic, so culling the sub-view would blind the
    // measurement that has to solve its seat and the hold would never be released by anything but
    // its backstop.
    //
    // The two veils cannot fight: each records ONLY renderers it found drawing (alpha > 0), so
    // whichever writes the zero first owns the record and the other passes over it; each restores
    // ONLY renderers that still carry the zero it wrote. If 9d lifts while this one still holds,
    // the next re-assert takes the restored alpha back and records it correctly.
    //
    // FRAME PHASE: LateUpdate, and it has to be. The game activates a sub-view root from an Update
    // (a Button.onClick dispatched by the EventSystem), Unity runs every Update before any
    // LateUpdate and every LateUpdate before the render loop, so LateUpdate is the last phase in
    // which the frame that would draw the un-seated picker can still be withheld — the same
    // argument parts 9d and 9e are placed on. It is called from CanvasConversion.LateTick's
    // per-panel loop directly after the hidden-window veil and BEFORE the reveal flip, so a panel
    // that becomes visible this frame is already covered.
    //
    // THE COST. One dictionary lookup per converted panel per frame (TryGetLiveFixedFit), which is
    // false for every window in the game except the character screen, and for that one an integer
    // signature: six property reads on the game's own NewPartyDisplayUI singleton plus a shallow
    // parent walk each — the same idle path part 9c already pays and shares. The CanvasRenderer
    // walk runs ONLY while a veil stands, i.e. the handful of frames between a sub-view opening
    // and its seat landing, over that sub-view's own subtree and not the window's. No allocation
    // on any path: both scratch buffers are reused. [[findobjectsoftype-is-the-default-suspect]]

    /// <summary>
    /// Frames a seat veil may stand before it is released anyway. DERIVED, not chosen: it is the
    /// worst case of the ORDINARY (un-bursted) fit cadence for a sub-view seat — up to
    /// <see cref="FitCheckIntervalFrames"/> frames of waiting for the first check, then
    /// <see cref="FixedFitSettleChecks"/> more checks at that spacing to satisfy the settle gate,
    /// then one more because a pass that re-aligns the character column defers the sub-view seat to
    /// the next check by construction (ApplyFixedFitCore's `else if (viewWrong …)`). Reaching it
    /// means neither the settle burst nor the cadence produced a seat at all, which is a falsifier
    /// and prints as one.
    /// </summary>
    private const int SubViewSeatVeilMaxFrames = FitCheckIntervalFrames * (FixedFitSettleChecks + 2);

    /// <summary>The open sub-view roots of the current set. Reused; never returned to a caller.</summary>
    private static readonly List<Transform> SeatVeilMembers = new(8);

    /// <summary>Scratch for one root's CanvasRenderers. Reused; never returned to a caller.</summary>
    private static readonly List<CanvasRenderer> SeatVeilScratch = new(256);

    /// <summary>One renderer this veil holds: the state it belongs to, and the alpha to hand back.
    /// The alpha is LEARNED, i.e. it is the last non-zero value anybody wrote and not necessarily
    /// the one the veil first took.</summary>
    private struct SeatVeilHold
    {
        internal FixedFitState? Owner;
        internal float Alpha;
    }

    /// <summary>
    /// EXACTLY the renderers the seat veil is holding, mapped to the alpha each must be handed
    /// back. One table for the whole mod rather than one per window, for the reason the hidden-
    /// window veil keeps one: <see cref="PreSeatVeilAlpha"/> is asked by another writer that has a
    /// renderer and no window, and a per-window table could not answer it.
    /// </summary>
    private static readonly Dictionary<CanvasRenderer, SeatVeilHold> SeatVeilHolds = new(256);

    /// <summary>Scratch for one lift's key set — a Dictionary may not be mutated while it is being
    /// enumerated, and a lift removes exactly the entries it restores.</summary>
    private static readonly List<CanvasRenderer> SeatVeilLiftScratch = new(256);

    /// <summary>
    /// THE ALPHA A RENDERER HAD BEFORE THIS VEIL TOOK IT, or <paramref name="own"/> when this veil
    /// is not holding it. The exact counterpart of <c>PreVeilAlpha</c> for the hidden-window veil,
    /// and it exists for the same one consumer: the materialise runner captures its original alpha
    /// through it, so an appear that starts over a seat-veiled renderer neither captures this
    /// veil's zero nor restores it as the window's own value. Without it the picker would dissolve
    /// IN to zero and stay invisible — the failure [[a-hide-saved-a-foreign-value]] records, with
    /// the two writers swapped.
    /// </summary>
    internal static float PreSeatVeilAlpha(CanvasRenderer cr, float own)
    {
        if (cr != null && SeatVeilHolds.TryGetValue(cr, out SeatVeilHold h))
            return h.Alpha;
        return own;
    }

    /// <summary>
    /// Is this veil holding anything at all right now? ONE BOOL, and it is what keeps the clamp
    /// below off every frame but the handful this part exists for.
    /// </summary>
    internal static bool SeatVeilStanding => SeatVeilHolds.Count > 0;

    /// <summary>
    /// THE ALPHA ANOTHER MOD WRITER MAY PUT ON <paramref name="cr"/> THIS FRAME: its own value, or
    /// ZERO while the seat veil holds it.
    ///
    /// <para>WHY A CLAMP AND NOT A RE-ASSERT. This veil re-asserts from
    /// <c>WorldUIModule.LateUpdate</c> and the materialise runner writes the same channel from its
    /// OWN <c>LateUpdate</c> on a MonoBehaviour at execution order 0 — and so is WorldUIModule, so
    /// which of the two runs first in a frame is undefined. A re-assert that loses that coin flip
    /// hands the eye exactly the frame this part exists to withhold. Asking the writer to clamp
    /// removes the race instead of trying to win it, which is the same choice
    /// [[dont-win-a-write-war]] records: concede the ordering, own the number.</para>
    ///
    /// <para>The runner's own restore is UNAFFECTED and must be: it writes the pre-veil value it
    /// captured through <see cref="PreSeatVeilAlpha"/>, this veil zeroes it again on the next
    /// re-assert and LEARNS it as the value to hand back, and the lift then puts back exactly what
    /// the appear finished on.</para>
    /// </summary>
    internal static float SeatVeilClamp(CanvasRenderer cr, float alpha)
    {
        if (alpha > 0f && cr != null && SeatVeilHolds.ContainsKey(cr))
            return 0f;
        return alpha;
    }

    /// <summary>
    /// THE PER-FRAME SERVICE, in LateUpdate. Raises a veil the moment the game has a sub-view open
    /// that the fixed fit has not seated, re-asserts it while it stands, and lifts it the moment
    /// the seat exists.
    ///
    /// <para>Cheap on every other window: <see cref="TryGetLiveFixedFit"/> is a dictionary lookup
    /// on a table that holds at most the character screen.</para>
    /// </summary>
    private static void TickSubViewSeatVeil(ConvertedPanel panel)
    {
        if (!TryGetLiveFixedFit(panel, out FixedFitState fx))
            return;

        // A FIT THAT IS NO LONGER RUNNING CANNOT PRODUCE THE RELEASE CONDITION, so nothing may be
        // held waiting for it. Checked before anything else: a hold whose releaser has stopped is
        // the shape of every latch this project has paid for. [[gated-remedy-never-ran]]
        if (!panel.FitEnabled)
        {
            if (fx.SeatVeilActive)
                LiftSubViewSeatVeil(panel, fx, overdue: false, silent: true);
            return;
        }

        int signature = SubViewOpenSetSignature(panel);
        // SEATED, or nothing open at all. 0 is a legitimate signature (every sub-view closed) and
        // there is nothing to withhold in that state. "Seated" is written by BOTH of the fixed
        // fit's terminal branches — the pass that writes a seat and the pass that finds nothing
        // left to write — so a sub-view that was already in the right place is never held.
        bool seated = signature == 0 || (fx.SeatedValid && fx.SeatedSignature == signature);
        if (seated)
        {
            if (fx.SeatVeilActive)
                LiftSubViewSeatVeil(panel, fx, overdue: false, silent: false);
            return;
        }

        // THE SET CHANGED WHILE A VEIL STOOD. Hand back exactly what this veil holds and raise a
        // new one keyed on the new set, rather than letting one veil's record span two sets — the
        // record is what makes the restore exact, and a record that outlives its subject is how a
        // hide comes to put back a foreign value. [[a-hide-saved-a-foreign-value]]
        if (fx.SeatVeilActive && fx.SeatVeilSignature != signature)
            LiftSubViewSeatVeil(panel, fx, overdue: false, silent: true);

        if (!fx.SeatVeilActive)
        {
            // An open set the backstop already released once is never veiled again: a window whose
            // seat can never be solved must not strobe between held and shown.
            if (fx.SeatVeilGaveUpValid && fx.SeatVeilGaveUpSignature == signature)
                return;
            fx.SeatVeilActive = true;
            fx.SeatVeilSignature = signature;
            fx.SeatVeilStartFrame = Time.frameCount;
            fx.SeatVeilStartTime = Time.unscaledTime;
            fx.SeatVeilRaised++;
        }

        fx.SeatVeilRenderers += ApplySubViewSeatVeil(panel, fx);

        if (Time.frameCount - fx.SeatVeilStartFrame >= SubViewSeatVeilMaxFrames)
        {
            fx.SeatVeilGaveUpSignature = signature;
            fx.SeatVeilGaveUpValid = true;
            LiftSubViewSeatVeil(panel, fx, overdue: true, silent: false);
        }
    }

    /// <summary>
    /// Withhold every currently-drawing <see cref="CanvasRenderer"/> under the open sub-view roots
    /// and record exactly those. Inactive children are included deliberately: the game repopulates
    /// these subtrees, and one it activates later in the same frame must not get a frame of its own
    /// at the un-seated pose.
    ///
    /// <para><b>ONE BIT, AND NOT THE ONE PART 9d USES — <c>CanvasRenderer.SetAlpha</c> ONLY, NEVER
    /// <c>cull</c>.</b> This is the difference between a hold and a deadlock, and it is read out of
    /// this file's own consumer rather than assumed. The fixed fit's visibility verdict
    /// (<c>CanvasConversion.3.Fit.cs</c>, the graphic test: <c>g.canvasRenderer.cull</c> ⇒ skip)
    /// SKIPS a culled graphic, so culling the sub-view would blind the very measurement that has to
    /// solve its seat: the fit would find nothing to place, would never write a seat, and the hold
    /// would run to its backstop on every single open — a remedy that switches off its own
    /// releaser. [[gated-remedy-never-ran]]</para>
    ///
    /// <para>The alpha is safe from that by construction, because the same verdict reads
    /// <c>GetInheritedAlpha()</c> — the ancestors' CanvasGroup product — and never the renderer's
    /// own <c>SetAlpha</c>. So the fit measures the sub-view exactly as if it were painted, while
    /// the eye receives nothing. It is also the half of part 9d's pair that a RectMask2D cannot take
    /// back between LateUpdate and the draw, which is why that part keys ITS record on the alpha
    /// too; anything that does overwrite it is corrected by the next re-assert, one LateUpdate
    /// later and still before any frame renders.</para>
    ///
    /// <para>A renderer already at zero is left completely alone and never recorded: it is either
    /// one this veil parked on an earlier pass or one that was authored transparent, and in both
    /// cases there is nothing to withhold and nothing to give back.</para>
    /// </summary>
    /// <returns>How many renderers this call newly took hold of.</returns>
    private static int ApplySubViewSeatVeil(ConvertedPanel panel, FixedFitState fx)
    {
        AssertNotInRenderPhase("sub-view seat veil");
        CollectSubViewSeatVeilMembers(panel);
        int taken = 0;
        for (int m = 0; m < SeatVeilMembers.Count; m++)
        {
            Transform root = SeatVeilMembers[m];
            if (root == null)
                continue;
            root.GetComponentsInChildren(true, SeatVeilScratch);
            for (int i = 0; i < SeatVeilScratch.Count; i++)
            {
                CanvasRenderer cr = SeatVeilScratch[i];
                if (cr == null)
                    continue;
                float alpha = cr.GetAlpha();
                if (SeatVeilHolds.TryGetValue(cr, out SeatVeilHold hold))
                {
                    // ALREADY OURS — re-assert the zero, and LEARN whatever wrote over it. The
                    // materialise runner drives this channel every LateUpdate of an appear, and the
                    // value it left is the one a lift must hand back; treating it as noise and
                    // restoring a stale "authored" alpha over the runner's work is exactly
                    // [[a-hide-saved-a-foreign-value]]. Only a NON-ZERO write is learned: a zero is
                    // this veil's own value coming back at us.
                    if (alpha > 0f)
                    {
                        hold.Alpha = alpha;
                        SeatVeilHolds[cr] = hold;
                        fx.SeatVeilLearned++;
                        cr.SetAlpha(0f);
                    }
                    continue;
                }
                if (alpha <= 0f)
                    continue; // authored transparent — nothing to withhold and nothing to give back
                cr.SetAlpha(0f);
                SeatVeilHolds[cr] = new SeatVeilHold { Owner = fx, Alpha = alpha };
                taken++;
            }
            SeatVeilScratch.Clear();
        }
        SeatVeilMembers.Clear();
        return taken;
    }

    /// <summary>
    /// Hand EXACTLY the recorded set back and clear it. Safe on a dead panel and on a released fit:
    /// the renderers are held by reference and belong drawing wherever they now live, so a subtree
    /// restored into its 2D home is never stranded invisible.
    ///
    /// <para>Both writes are value-checked, which is what makes the restore exact rather than merely
    /// opposite — a writer that took either over during the hold keeps it.</para>
    /// </summary>
    private static void LiftSubViewSeatVeil(ConvertedPanel panel, FixedFitState fx, bool overdue,
        bool silent)
    {
        if (!fx.SeatVeilActive && SeatVeilHolds.Count == 0)
            return;
        AssertNotInRenderPhase("sub-view seat veil lift");
        SeatVeilLiftScratch.Clear();
        foreach (KeyValuePair<CanvasRenderer, SeatVeilHold> pair in SeatVeilHolds)
        {
            // OURS, or DEAD. The second half is the table's only pruning and a lift is the only
            // pass that walks the whole table, so it is where it belongs: a renderer whose
            // GameObject the game destroyed can never be handed anything back, and leaving its
            // entry would let this table grow across a session of character screens.
            // The `!` is about Unity's overloaded ==, not about nullability: a DESTROYED
            // CanvasRenderer compares equal to null while still being a perfectly good dictionary
            // key, which is exactly why the entry can be found and removed at all.
            if (ReferenceEquals(pair.Value.Owner, fx) || pair.Key == null)
                SeatVeilLiftScratch.Add(pair.Key!);
        }
        int held = 0;
        for (int i = 0; i < SeatVeilLiftScratch.Count; i++)
        {
            CanvasRenderer cr = SeatVeilLiftScratch[i];
            SeatVeilHolds.TryGetValue(cr, out SeatVeilHold hold);
            SeatVeilHolds.Remove(cr);
            if (cr == null)
                continue; // destroyed under the veil — pruned above, nothing to hand back
            held++;
            // Value-checked, which is what makes the restore EXACT rather than merely opposite: the
            // alpha goes back only if the renderer still carries the zero this veil wrote. A writer
            // that took it over between the last re-assert and here keeps it.
            if (cr.GetAlpha() <= 0f)
                cr.SetAlpha(hold.Alpha);
        }
        SeatVeilLiftScratch.Clear();

        int frames = Mathf.Max(0, Time.frameCount - fx.SeatVeilStartFrame);
        int millis = Mathf.RoundToInt((Time.unscaledTime - fx.SeatVeilStartTime) * 1000f);
        bool stood = fx.SeatVeilActive;
        fx.SeatVeilActive = false;
        if (!stood)
            return;
        if (overdue)
            fx.SeatVeilOverdue++;
        else if (!silent)
            fx.SeatVeilLiftedOnSeat++;
        if (frames > fx.SeatVeilWorstFrames)
        {
            fx.SeatVeilWorstFrames = frames;
            fx.SeatVeilWorstMillis = millis;
        }
        if (!silent)
            ReportSubViewSeatVeil(panel, fx, overdue, frames, millis, held);
    }

    /// <summary>
    /// The open sub-view roots of the current set, collected with EXACTLY the membership test
    /// <see cref="SubViewOpenSetSignature"/> and <c>CollectActiveSubViews</c> use — the game's own
    /// serialized references, active in the hierarchy, strict descendants of the conversion target.
    /// A root nested inside another is NOT dropped here on purpose: this is a hide, not a placement,
    /// and hiding an inner root twice costs nothing while missing one would show it.
    /// </summary>
    private static void CollectSubViewSeatVeilMembers(ConvertedPanel panel)
    {
        SeatVeilMembers.Clear();
        if (panel.Target == null)
            return;
        NewPartyDisplayUI? display;
        try
        {
            display = NewPartyDisplayUI.PartyDisplay;
        }
        catch (System.Exception)
        {
            return;
        }
        if (display == null)
            return;
        try
        {
            AddSeatVeilMember(panel, display.AbilityCardsDisplay);
            AddSeatVeilMember(panel, display.EnhancementCardsDisplay);
            AddSeatVeilMember(panel, display.PerkManager);
            AddSeatVeilMember(panel, display.CharacterSelector);
            AddSeatVeilMember(panel, display.ItemInventoryDisplay);
            AddSeatVeilMember(panel, display.BattleGoalWindow);
        }
        catch (System.Exception)
        {
            SeatVeilMembers.Clear();
        }
    }

    /// <summary>One member of <see cref="CollectSubViewSeatVeilMembers"/>.</summary>
    private static void AddSeatVeilMember(ConvertedPanel panel, Component? c)
    {
        if (c == null || panel.Target == null)
            return;
        Transform t = c.transform;
        if (ReferenceEquals(t, panel.Target) || !c.gameObject.activeInHierarchy)
            return;
        if (FixedFitLevelsUp(t, panel.Target) <= 0)
            return;
        for (int i = 0; i < SeatVeilMembers.Count; i++)
        {
            if (ReferenceEquals(SeatVeilMembers[i], t))
                return;
        }
        SeatVeilMembers.Add(t);
    }

    /// <summary>
    /// One line per DISTINCT outcome. The change gate is the outcome itself — held-to-the-seat or
    /// released-by-the-backstop, plus the gap in frames — because that pair is the whole finding;
    /// repeating it for every identical tab press is the flood ModBuild 331 removed, and a counter
    /// that only ever grows is [[a-held-instrument-reads-as-dead]]. The session totals ride along on
    /// whichever line does print, so a run whose outcomes never vary still states how many holds
    /// there were.
    ///
    /// <para>Writes nothing any non-diagnostic reads — every counter it prints is written by the
    /// veil itself. [[a-write-inside-a-logger]]</para>
    /// </summary>
    private static void ReportSubViewSeatVeil(ConvertedPanel panel, FixedFitState fx, bool overdue,
        int frames, int millis, int held)
    {
        int outcome = (overdue ? -1 : 1) * (frames + 1);
        if (outcome == fx.SeatVeilLastReported)
            return;
        fx.SeatVeilLastReported = outcome;
        string name = panel.HostGo != null ? panel.HostGo.name : "?";
        // HW-VERIFY: this is the line that decides the 2026-09-05 report. Read the last clause
        // first — it is a YES or a NO and nothing has to be interpreted to get it.
        VRLog.Note("WorldUI", $"SUB-VIEW SEAT VEIL '{name}': the game opened a sub-view "
            + $"('{fx.ViewName}') that the fixed fit had not seated yet, so its own subtree was "
            + "withheld at the CanvasRenderer until the seat existed — the window itself was never "
            + "touched and the character column, its frame, its grab bar and its backing plate were "
            + "drawn throughout. "
            + $"BECAME VISIBLE (the veil went up, i.e. the first frame the sub-view could have been "
            + $"drawn): frame {fx.SeatVeilStartFrame}. SEATED (the veil came down): frame "
            + $"{fx.SeatVeilStartFrame + frames}. THE GAP: {frames} frame(s) = {millis} ms, over "
            + $"{held} renderer(s) held at the lift ({fx.SeatVeilRenderers} taken and "
            + $"{fx.SeatVeilLearned} foreign alpha write(s) learned over this window's life). "
            + "WHAT THE HOLD WAITED ON: "
            + (overdue
                ? "NOTHING EVER ARRIVED — the fixed fit wrote no seat for this set of open "
                  + $"sub-views within {SubViewSeatVeilMaxFrames} frame(s), which is the ORDINARY "
                  + $"cadence's own worst case ({FitCheckIntervalFrames}-frame sampling x "
                  + $"{FixedFitSettleChecks + 2} checks), so neither the settle burst nor the "
                  + "cadence produced one. The sub-view was released anyway, because a control the "
                  + "player must press may never latch invisible — grep SUB-VIEW SETTLE BURST on "
                  + "the same window to see whether the burst armed at all"
                : "the fixed fit writing a seat for the set of sub-views that is open right now — "
                  + "a named condition, not a frame count and not a timer; the frame count above is "
                  + "what it COST, never what it waited for")
            + ". DID THE FIRST VISIBLE FRAME OF THE SUB-VIEW CARRY ITS FINAL SEAT? "
            + (overdue
                ? "NO — this hold was released by the backstop with no seat written, so the "
                  + "sub-view was drawn at the pose the game gave it and the fit may still move it. "
                  + "THIS IS THE FAILING READING and it is the one the next round must chase"
                : "YES — the subtree was withheld from the frame the set changed until the frame "
                  + "the seat was written, so no frame of it was ever drawn at the authored pose. "
                  + "The 2026-09-05 report ('die Quest steht weiter oben und ploppt dann runter') "
                  + "cannot be produced by this path while this reading holds")
            + $". LAST SEAT WRITTEN: group shift {fx.ViewShift.x:F0},{fx.ViewShift.y:F0} px — the "
            + "ModBuild 433 log had this at 14,-168 px for the battle-goal picker, i.e. 176 mm of "
            + "drop at that session's 1.050 mm per authored px, and THAT is the distance he was "
            + $"watching it fall. SESSION: {fx.SeatVeilRaised} hold(s), "
            + $"{fx.SeatVeilLiftedOnSeat} of them released BY THE SEAT and {fx.SeatVeilOverdue} by "
            + $"the backstop, worst {fx.SeatVeilWorstFrames} frame(s) = {fx.SeatVeilWorstMillis} ms. "
            + "A backstop count of 0 across a session of opening sub-views is the whole claim; any "
            + "other number names how often it was not kept.");
    }
}
