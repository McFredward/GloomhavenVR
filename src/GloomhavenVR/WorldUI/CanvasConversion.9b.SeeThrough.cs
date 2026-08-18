using UnityEngine;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 9b (where a FOREIGN transparent surface belongs on the distance ladder).
// A NEW part file rather than new members in part 8/9 for the reason part 8's own header gives:
// the refactor guard tracks the partial class's member and static-initializer order, and the
// filename sort ('.9.' < '.9b.') appends this part AFTER part 9, so nothing existing moves. The
// two static fields this part declares carry NO initializer on purpose (the array is allocated
// lazily in BuildSeenThroughLadder) — a field initializer would emit a static constructor entry
// and perturb exactly the initializer order the guard tracks.

internal static partial class CanvasConversion
{
    // ---- ranking a surface the mod does NOT own against the ladder ----------------------------
    //
    // USER REPORT (2026-08-09, verbatim): "Wenn nicht der Mixed-Reality-Modus an ist, sind die
    // noch-nicht-entdeckten Tiles durchsichtig. Das ist auch so gewollt. Allerdings ist nicht
    // alles des Boards dahinter sichtbar. Die Entscheidungssymbole, die Initiativreihenfolge und
    // mancher Text des Boards verschwindet dahinter. Wenn ich da durch sehen kann, will ich auch
    // alles dahinter durchsehen können, nicht nur manche Elemente."
    //
    // THE MECHANISM, and why it is the SAME defect part 9 was written for, one level further out.
    // The game's fog-of-war hex kit (Amp_Basic_Unseen, and the 'Simple Tile'/'EN_CR_FloorTiles…'
    // pieces that ride with it under a map tile's 'Generated Content/Preview' node) draws at
    // renderQueue 3000 with RenderType 'Overlay', ZTest LEqual — and its FIRST pass hardcodes
    // ZWrite ON (ShaderOcclusionPatcher's scan of the shipped bundles: 'Amp_Basic_Unseen | 0/0,
    // 0/1 FORWARD | 4 = LEqual | On/Off'; MixedReality.cs:403-413 already records this hazard for
    // the MR backings). So an undiscovered tile is a TRANSLUCENT surface that WRITES DEPTH, at
    // the default sortingOrder 0 — i.e. it is painted BEFORE every converted panel (>= 100) and
    // before the control board's furniture band (>= 95), and everything painted after it that
    // lies behind it fails the depth test and disappears. That is exactly the split the user
    // reports, and it splits along the SAME line part 9 found:
    //   * the board SLAB, its lip, the keycap bodies and the fan/tray card slabs are opaque or
    //     AlphaTest (queue <= 2500). They are already in the framebuffer when the tile blends
    //     over them, so they show through it — "das Board ist dahinter sichtbar";
    //   * the initiative track, the decision dock and every other converted panel are depthless
    //     uGUI at ~3000 on the ladder, and the board's own transparent text (status placard,
    //     round readout, engraved keycap labels, pile captions) is the furniture band. Both are
    //     painted AFTER the tile and are erased by its depth — "die Entscheidungssymbole, die
    //     Initiativreihenfolge und mancher Text verschwindet dahinter".
    //
    // WHY THE FIX CANNOT BE "STOP THE TILE WRITING DEPTH", which is what depth-honesty would
    // normally ask for: that ZWrite is a hardcoded pass state. The in-game probe
    // (MixedReality.cs:2129-2140, the properties _ZWrite/_ZTest/_SrcBlend/_DstBlend/_Cull) came
    // back 'hardcoded' for all five — the material exposes no _ZWrite, so neither a
    // MaterialPropertyBlock nor a material write can clear it. Clearing it would need a patched
    // shader in a bundle, and this repo's standing rules forbid both touching game data and
    // shipping a bundle for a fix that has a mod-side answer. The mod-side lever that remains is
    // DRAW ORDER — and once the order is depth-correct the depth write is harmless: a surface
    // that is painted LAST cannot erase anything, because everything behind it is already there.
    //
    // WHAT THIS PART ANSWERS. Given a foreign transparent surface at a measured eye distance,
    // where must it sit so that painter's order holds in BOTH directions - over everything the
    // ladder puts behind it, under everything the ladder puts in front of it?
    //   behindTop  = the highest order among ladder panels (and furniture bands) measurably
    //                FARTHER than the surface. The surface must beat all of it.
    //   frontFloor = the lowest order among panels (and bands) measurably NEARER. The surface
    //                must not reach it.
    //   result     = min(behindTop + lift, frontFloor), never below behindTop.
    // The lift clears the FOLLOWERS of the farthest-behind panel (close X +2, grab bar +4, the
    // game tooltip laid on a menu plane +10 — see PanelOrderStep's doc), which belong to that
    // panel and are just as much "behind" as it is. The clamp to frontFloor is what keeps the
    // step-16 budget honest in the one geometry where the lift would overshoot: a board whose
    // furniture band tops out at 99 with the next panel already at 100 leaves no integer room, so
    // the surface TIES with that panel — and a tie is the correct answer there, because Unity
    // resolves a sortingOrder tie by renderQueue and then by distance, i.e. by exactly the thing
    // this whole ladder is a proxy for.
    //
    // A TIE INSIDE <see cref="OrderSwapMarginMeters"/> COUNTS AS NEITHER. Two surfaces the player
    // cannot tell apart in depth must not push each other around; the margin is the same dead
    // band the ladder's own swap gate uses, so a foreign surface cannot chatter against a panel
    // it is level with.
    //
    // RETURNS <see cref="NoSurfaceBehind"/> WHEN NOTHING IS BEHIND. That is not a fallback, it is
    // the conservative half of the contract: with no panel and no furniture band behind it, the
    // surface has nothing of ours to reveal, and lifting it anyway would only buy the one
    // regression this manoeuvre can cause — a foreign transparent surface climbing over OTHER
    // order-0 art that happens to be in front of it and writes no depth of its own. Callers park
    // such a surface at its authored order, which keeps the untouched scene bit-identical to the
    // shipped build.
    //
    // ---- ROUND 2 (2026-08-09): ONE SNAPSHOT PER FRAME, NOT ONE WALK PER SURFACE ---------------
    //
    // The first cut of this part answered every query by walking OrderedPanels and calling
    // IFurnitureOrderAnchor.FurnitureEyeDistance per furniture group, per CALLER. With the fog-of-
    // war kit's 333 live renderers in the shipped hardware scene that is ~5000 ladder iterations
    // (each one a Unity-object liveness test) plus 333-666 anchor distance computations, every
    // frame, inside a step that measured p50 0.17 ms before any of this existed. The perf pass
    // flagged it and could not price it; this round removes the question instead of measuring it.
    //
    // The answer is a property of the FUNCTION, not of the caller: behindTop/frontFloor are step
    // functions of the query distance with at most one breakpoint per ladder entry (13-16 panels
    // plus one furniture group in the hardware log). So the ladder is snapshotted ONCE per frame
    // into a small array sorted far -> near, with the prefix-maximum of the "top" order and the
    // suffix-minimum of the "base" order folded in; a query is then two binary searches over
    // ~17 floats and TWO array reads, with no Unity call and no allocation at all. Same numbers,
    // same tie rules, ~300x fewer iterations.
    //
    // THE PREFIX-MAX IS NOT AN OPTIMISATION DETAIL — it is what keeps the answer monotone while
    // the ladder is mid-swap. The ladder's own hysteresis deliberately lets two panels sit in the
    // sequence "wrong way round" for up to OrderSwapStableFrames, so the raw order values are NOT
    // monotone in distance during a swap. Taking the running maximum of everything farther (and
    // the running minimum of everything nearer) restores "beat everything behind me / stay under
    // everything in front of me" as a statement about SETS, which is what the contract above
    // actually says, rather than about the one entry that happens to be adjacent.
    //
    // ---- ROUND 3 (2026-08-09): THE TWO SIDES CAN STILL CROSS, AND THAT IS A STATE, NOT A NUMBER
    //
    // The folds above make each side monotone SEPARATELY. They cannot stop the two sides from
    // CROSSING, because a mid-swap ladder really is contradictory: for up to OrderSwapStableFrames
    // frames a panel that is measurably FARTHER carries the HIGHER order, and a foreign surface
    // whose distance falls between that pair is then asked to be at once above 'behindTop' and
    // below a 'frontFloor' that is lower than it. <see cref="ResolveSeenThrough"/> has to answer
    // something, and its clamp order answers 'behindTop' — i.e. it lifts the surface OVER a panel
    // that is genuinely in front of it, for the length of the swap window, and drops it back
    // afterwards. That is a six-frame excursion produced by nothing but the ladder's own
    // hysteresis, repeated for every swap, and with nine ActorBars sitting within 0.6 m of each
    // other in the shipped scene (hardware log, ModBuild 102: every 'PANEL DRAW ORDER' line reads
    // RESORTED) the swaps never stop while the head moves.
    //
    // The number is not the fix; NOT ACTING is. <see cref="SeenThroughContradiction"/> lets a
    // caller that keeps a decision RECOGNISE the state and hold what it already has until the
    // ladder is self-consistent again — and because a completed swap does not change how MANY
    // panels are behind a given distance, the decision it holds is the same one it would take
    // afterwards. See Core.UnseenTileOrder's round-3 header for the full argument.

    /// <summary>Sentinel from <see cref="SeenThroughBounds"/> / <see cref="ResolveSeenThrough"/>:
    /// the ladder has nothing behind this surface, so the caller must leave the surface's authored
    /// order alone.</summary>
    internal const int NoSurfaceBehind = int.MinValue;

    /// <summary>
    /// One rung of the per-frame ladder snapshot: a converted panel's slot, or a furniture group's
    /// band. After <see cref="BuildSeenThroughLadder"/> the array is sorted FAR -> NEAR and the two
    /// order fields no longer hold that rung's own value but the running aggregate that a query
    /// needs: <see cref="BehindTop"/> is the maximum "top" order over rungs 0..i (everything at
    /// least this far away), <see cref="FrontBase"/> the minimum "base" order over rungs i..n-1
    /// (everything at most this far away).
    /// </summary>
    private struct LadderStep
    {
        public float Distance;
        public int BehindTop;
        public int FrontBase;
    }

    /// <summary>The snapshot. Deliberately WITHOUT a field initializer (see the file header):
    /// allocated on first use and grown in place, so a steady scene allocates nothing.</summary>
    private static LadderStep[]? s_seenThroughLadder;

    private static int s_seenThroughCount;

    /// <summary>True when the ladder carries nothing at all this frame — no live converted panel
    /// and no seated furniture group. A foreign surface then has nothing of ours to composite
    /// against in EITHER direction, and its caller must hand the authored order back rather than
    /// hold a lift that no longer stands for anything.</summary>
    internal static bool SeenThroughLadderEmpty => s_seenThroughCount == 0;

    /// <summary>
    /// Snapshot the ladder for this frame. Called from <see cref="TickPanelOrder"/> AFTER the
    /// panel orders and the furniture bands are assigned, so the snapshot is the CURRENT frame's
    /// ladder rather than the previous one's, and BEFORE any consumer of
    /// <see cref="SeenThroughBounds"/> runs.
    ///
    /// <para>Cost: one pass over the (13-16) live panels, one <c>FurnitureEyeDistance</c> per
    /// furniture group (one board in the shipped scene), an insertion sort over that array — it
    /// is nearly sorted every frame, because the panel list is ALREADY maintained far-to-near and
    /// only the furniture rung has to find its place — and two linear folds. That is the entire
    /// per-frame Unity-touching cost of the whole see-through manoeuvre, no matter how many
    /// foreign surfaces query it afterwards.</para>
    /// </summary>
    internal static void BuildSeenThroughLadder(Vector3 eye)
    {
        int need = OrderedPanels.Count + FurnitureGroups.Count;
        if (s_seenThroughLadder == null || s_seenThroughLadder.Length < need)
            s_seenThroughLadder = new LadderStep[Mathf.Max(32, need * 2)];
        LadderStep[] rungs = s_seenThroughLadder;

        int n = 0;
        for (int i = 0; i < OrderedPanels.Count; i++)
        {
            ConvertedPanel p = OrderedPanels[i];
            if (p == null || !p.IsAlive)
                continue;
            // Hidden panels stay ranked, exactly as the ladder itself keeps ranking them: both
            // hides are transient, and a panel must be composited correctly the INSTANT it comes
            // back rather than six settle frames later.
            rungs[n].Distance = p.OrderDistance;
            rungs[n].BehindTop = p.DrawSortingOrder;
            rungs[n].FrontBase = p.DrawSortingOrder;
            n++;
        }

        // The board's non-canvas transparent furniture is ranked as a BAND, not a slot (part 9),
        // so "behind" means clear its TOP and "in front" means stay under its BASE. Whenever the
        // board carries any docked panel the panel rungs above already dominate this — the docked
        // panels sit at or above the band's own rank — but a board whose panels are all gone (no
        // scenario UI up, everything closed) still has a placard and engraved labels to reveal,
        // and that case exists only here.
        for (int g = 0; g < FurnitureGroups.Count; g++)
        {
            FurnitureGroup group = FurnitureGroups[g];
            if (!group.Anchor.FurnitureOrderAlive || group.AppliedRank < 0)
                continue;
            int bandBase = FurnitureBandBase(group.AppliedRank);
            rungs[n].Distance = group.Anchor.FurnitureEyeDistance(eye);
            rungs[n].BehindTop = bandBase + FurnitureBandWidth - 1;
            rungs[n].FrontBase = bandBase;
            n++;
        }

        // Insertion sort, far -> near. Chosen over Array.Sort deliberately: n is ~17, the array is
        // already nearly sorted (the panel rungs arrive in ladder order), and Array.Sort on a
        // struct array would need a comparer and therefore an allocation on the per-frame path.
        for (int i = 1; i < n; i++)
        {
            LadderStep step = rungs[i];
            int j = i - 1;
            while (j >= 0 && rungs[j].Distance < step.Distance)
            {
                rungs[j + 1] = rungs[j];
                j--;
            }
            rungs[j + 1] = step;
        }

        for (int i = 1; i < n; i++)
        {
            if (rungs[i - 1].BehindTop > rungs[i].BehindTop)
                rungs[i].BehindTop = rungs[i - 1].BehindTop;
        }
        for (int i = n - 2; i >= 0; i--)
        {
            if (rungs[i + 1].FrontBase < rungs[i].FrontBase)
                rungs[i].FrontBase = rungs[i + 1].FrontBase;
        }

        s_seenThroughCount = n;
    }

    /// <summary>
    /// The two constraints a foreign transparent surface at <paramref name="eyeDistance"/> is
    /// under, read off this frame's snapshot: it must be painted at or above
    /// <paramref name="behindTop"/> (the highest order among everything measurably farther) and at
    /// or below <paramref name="frontFloor"/> (the lowest order among everything measurably
    /// nearer). <see cref="NoSurfaceBehind"/> / <see cref="int.MaxValue"/> mean "no such side".
    ///
    /// <para>The bounds, not a single number, are what callers ask for: a caller keeping a STICKY
    /// decision wants the constraints themselves, because the cheapest and least twitchy
    /// question a driver can ask is not "what would I choose now" but "is what I already chose
    /// still correct" — an order that still satisfies both bounds needs no write, and a ladder
    /// reshuffle entirely on one side of the surface therefore costs nothing and pops nothing.</para>
    /// </summary>
    internal static void SeenThroughBounds(float eyeDistance, out int behindTop, out int frontFloor)
    {
        behindTop = NoSurfaceBehind;
        frontFloor = int.MaxValue;

        LadderStep[]? rungs = s_seenThroughLadder;
        int n = s_seenThroughCount;
        if (rungs == null || n == 0)
            return;

        // Everything measurably FARTHER is the prefix [0..behindCount-1] of the far->near array.
        int behindCount = FirstRungNearerThan(rungs, n, eyeDistance + OrderSwapMarginMeters, strict: false);
        if (behindCount > 0)
            behindTop = rungs[behindCount - 1].BehindTop;

        // Everything measurably NEARER is the suffix [frontFrom..n-1].
        int frontFrom = FirstRungNearerThan(rungs, n, eyeDistance - OrderSwapMarginMeters, strict: true);
        if (frontFrom < n)
            frontFloor = rungs[frontFrom].FrontBase;
    }

    /// <summary>
    /// True when this frame's snapshot puts a surface at these constraints under a pair that NO
    /// order can satisfy — something measurably farther already carries a HIGHER order than
    /// something measurably nearer. The only producer of that state is the panel ladder's own swap
    /// hysteresis (see the ROUND 3 note in the header), so it is transient by construction and
    /// lasts at most <see cref="OrderSwapStableFrames"/> frames.
    ///
    /// <para>A caller that keeps a STICKY decision must treat this as "no answer this frame" and
    /// hold, rather than let <see cref="ResolveSeenThrough"/> resolve the contradiction in favour
    /// of one side: resolving it moves the surface, and the move is undone the moment the ladder
    /// finishes the swap, which is a visible flicker with no cause in the scene at all.</para>
    /// </summary>
    internal static bool SeenThroughContradiction(int behindTop, int frontFloor) =>
        behindTop != NoSurfaceBehind && frontFloor != int.MaxValue && behindTop > frontFloor;

    /// <summary>The order that satisfies a pair of <see cref="SeenThroughBounds"/> constraints:
    /// <c>min(behindTop + lift, frontFloor)</c>, never below <paramref name="behindTop"/> — a
    /// nearer slot sitting BELOW a farther one (which the ladder's hysteresis permits mid-swap)
    /// ties with what is behind, and Unity's own distance tie-break does the rest.
    ///
    /// <para>MONOTONICITY — the property a caller with MANY surfaces depends on, stated here
    /// because it is a property of this function and of <see cref="SeenThroughBounds"/> together.
    /// Read as a function of the query distance, <c>behindTop</c> is a prefix-maximum over a
    /// prefix that only grows as the surface gets nearer, and <c>frontFloor</c> a suffix-minimum
    /// over a suffix that only shrinks; both are therefore non-decreasing as the surface
    /// approaches the eye, and <c>max(behindTop, min(behindTop + lift, frontFloor))</c> is a
    /// composition of non-decreasing maps. The <see cref="NoSurfaceBehind"/> branch keeps that
    /// true rather than breaking it: it is returned only for the FARTHEST surfaces (nothing of
    /// ours is behind them), and its callers park those at an authored order below
    /// <see cref="PanelOrderBase"/>. So for any set of surfaces resolved against the SAME
    /// snapshot, a farther surface never receives a higher order than a nearer one — two such
    /// surfaces can tie, and a tie falls to renderQueue and then to Unity's own back-to-front
    /// distance sort, which is the same answer. That is what lets a caller paint a whole field of
    /// co-planar foreign surfaces without them ever fighting each other, PROVIDED it applies one
    /// snapshot's answers to all of them at once.</para></summary>
    internal static int ResolveSeenThrough(int behindTop, int frontFloor, int lift)
    {
        if (behindTop == NoSurfaceBehind)
            return NoSurfaceBehind;
        int want = behindTop + lift;
        if (want > frontFloor)
            want = frontFloor;
        if (want < behindTop)
            want = behindTop;
        return want;
    }

    /// <summary>First index of the far-to-near snapshot whose rung is nearer than
    /// <paramref name="value"/> (<paramref name="strict"/>) or not farther than it. The array is
    /// sorted descending, so the predicate is monotone and a binary search is exact.</summary>
    private static int FirstRungNearerThan(LadderStep[] rungs, int n, float value, bool strict)
    {
        int lo = 0;
        int hi = n;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            bool hit = strict ? rungs[mid].Distance < value : rungs[mid].Distance <= value;
            if (hit)
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo;
    }
}
