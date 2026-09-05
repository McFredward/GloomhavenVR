using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE ONE ARITHMETIC EVERY GRAB BAR IN THIS MOD IS SIZED BY.</b> How long the drawn rod is,
/// how thick, how far below its panel it hangs and how generous the palm zone around it is — one
/// function, three owners: <see cref="GrabbableModal"/> (a floated menu window),
/// <c>SurfaceGrabBar</c> (a floated decision panel) and <c>CombatLogSurface</c> (the combat log).
///
/// <para><b>WHY IT EXISTS.</b> The first two owners already carried the same seven constants and
/// the same eight lines of derivation, copied — <c>SurfaceGrabBar</c>'s own class doc says so in
/// as many words ("every constant below is GrabbableModal's, by value and by name, so the two
/// handles read as one piece of furniture"). The combat log would have been the THIRD copy, and
/// the user's ruling that started this round is exactly that the combat log must not look like a
/// different kind of object: <i>"er sieht anders aus als die anderen Fenster … ich will das du es
/// angleichst"</i>. Two hand-kept copies of a look are how two handles drift; three is how they
/// drift twice as fast. So the numbers moved here and the three owners call
/// <see cref="Solve"/>.</para>
///
/// <para><b>AND SINCE ModBuild 447, THE OTHER HALF OF THE SAME QUESTION.</b> <see cref="Solve"/>
/// answers "how long is the rod, given a width" — it never owned WHICH width, and that is where the
/// merchant's handle went wrong (<c>händlerbalken.jpg</c>: a 284 px rod under the right-hand edge of
/// a 1920 px window). <see cref="SolveSpan"/> is that decision, stated once, with its own doc
/// comment: the frame's width and centre for a window that paints its whole frame, the ink union's
/// for a window whose frame is transparent around what it draws.</para>
///
/// <para><b>IT IS A REWRITE OF THE SAME ARITHMETIC, NOT A NEW RULE.</b> Every line of
/// <see cref="Solve"/> is byte-for-byte what <c>GrabbableModal.SyncBar</c> and
/// <c>SurfaceGrabBar.SyncBar</c> computed before it, in the same order, with the same clamps and
/// the same epsilons — so no shipped bar changes size, thickness, gap or zone by a float. The
/// derivations themselves (why 0.55/0.62, why the 0.06 floor is arithmetic rather than taste, why
/// a short panel gets a slimmer bar) are documented on the constants below, where they were.</para>
///
/// <para><b>THE TWO FRAMES THE INPUTS ARE IN, AND WHY THEY DIFFER.</b> This trips every reader
/// once, so it is stated here rather than at three call sites:
/// <list type="bullet">
/// <item>the source WIDTH is in the grab FRAME's own local units — i.e. it already carries whatever
/// scale the holder above the frame applies.</item>
/// <item>the panel HEIGHT is in REAL metres, because it is compared against
/// <see cref="BarFullSizePanelHeightMeters"/>, a comfort bound about how big a physical handle may
/// be next to its window. A bound in metres must be fed metres.</item>
/// <item>the WORLD SCALE is the diorama scale the FIXED constants must carry themselves. It is 1
/// for an owner whose holder already carries the diorama scale (the combat log) and the live
/// diorama scale for an owner whose holder sits at identity (both the others) — which is not an
/// inconsistency but the same statement twice: the fixed metre constants must end up in the same
/// units the frame is in.</item>
/// </list></para>
///
/// <para><b>MULTIPLAYER:</b> nothing here goes on the wire. It is a pure function of a rect, a
/// scale and seven constants; a peer's mirror of a shared window runs the same function over its
/// own copy of the same rect, which is why the mirrored handle needs no wire field.</para>
/// </summary>
internal static class GrabBarLayout
{
    /// <summary>Gap from the panel's bottom edge to the bar CENTRE (frame-local, scale-1 metres),
    /// before the short-panel proportion and the diorama scale are applied.</summary>
    internal const float BarGapMeters = 0.03f;

    /// <summary>The rod's nominal SHAFT radius — <see cref="GrabBarMesh.DefaultRadius"/> rather
    /// than a literal, because the board handle and every window handle are ONE drawn object and
    /// two hand-kept copies of its thickness is exactly how they drift apart. The two end knobs
    /// swell to 1.28 R and the shaft tapers to 0.89 R at its own ends, so the rod's silhouette is
    /// not one constant radius and no single number here could be; this measures the long uniform
    /// run, which is what every consumer of it is about.</summary>
    internal const float BarRadius = GrabBarMesh.DefaultRadius;

    /// <summary>Drawn bar length as a fraction of the panel width.</summary>
    internal const float BarWidthFraction = 0.55f;

    /// <summary>Palm grab zone width as a fraction of the panel width — wider than the drawn rod,
    /// because a palm grab is a generous gesture and the laser has its own capsule down the rod's
    /// axis. A hand reaching for a handle should not have to be accurate; a beam being aimed at
    /// one should.</summary>
    internal const float ZoneWidthFraction = 0.62f;

    /// <summary>
    /// Floor on the drawn length, in scale-1 metres. RAISED 0.04 -> 0.06 WHEN THE BAR BECAME A ROD,
    /// and the reason is arithmetic rather than taste: a rod cannot be drawn shorter than its own
    /// two end caps (2 x CapLengthInRadii x DefaultRadius = 2 x 2.0 x 0.014 = 0.056 m). Below that
    /// <c>GrabBarVisual.SetLength</c> collapses the shaft to nothing and the two knobs meet in the
    /// middle — an honest picture of "as short as this bar gets", but a bar clamped to the old 0.04
    /// would have been drawn ~40 % WIDER than it asked for, and the laser capsule (sized on the
    /// requested length) would have been shorter than the thing it is a target for.
    /// </summary>
    internal const float MinBarWidth = 0.06f;

    /// <summary>Floor of the short-panel bar proportion — the visible rod (and the laser capsule,
    /// which rides the same uniform root scale) must stay a comfortable target.</summary>
    internal const float MinBarProportion = 0.5f;

    /// <summary>
    /// EMPTY-GOLD-PLATE FIX (torbogen screenshot 2026-08-02): panel height (real metres) at or
    /// above which the bar keeps its full thickness/gap. The fixed 2.4 cm bar + 3 cm gap were
    /// sized for board-scale menus; under the ~6 cm level-message ACTION STRIP the same bar
    /// rendered nearly as tall as the strip itself and a full strip-height away from it — on the
    /// flat mirror it read as a detached EMPTY GOLD RECTANGLE floating below the hint. Panels
    /// shorter than this reference get a proportionally slimmer, closer bar so the handle visually
    /// attaches to its window; taller panels are numerically unchanged.
    /// </summary>
    internal const float BarFullSizePanelHeightMeters = 0.30f;

    /// <summary>The palm zone's height and depth, in scale-1 metres.</summary>
    internal const float ZoneDepthMeters = 0.05f;

    /// <summary>Draw-order offset for the rod's three renderers: the handle is foreground and must
    /// paint OVER its own panel's content, while staying under <c>CanvasConversion.PanelOrderStep</c>
    /// so a genuinely nearer panel still outranks it.</summary>
    internal const int BarOrderOffset = 4;

    /// <summary>
    /// <b>WHICH RECTANGLE A BAR'S WIDTH AND CENTRE COME FROM.</b> The answer <see cref="SolveSpan"/>
    /// returns: one horizontal span, in the grab frame's own local units, plus the prose that says
    /// which of the two rules produced it.
    /// </summary>
    internal struct Span
    {
        /// <summary>The width <see cref="Solve"/>'s <c>sourceWidth</c> wants — the frame's, or the
        /// ink union's when the ink union is the honest size of the window.</summary>
        internal float Width;

        /// <summary>Where the rod's midpoint goes, as an offset from the frame's own centre. Zero
        /// whenever the frame itself is the answer.</summary>
        internal float Centre;

        /// <summary>Which rule fired, spelled out — the tween prints it and so does the
        /// <c>GRAB BAR CLEARS THE INK</c> falsifier, so a placed bar and its own report can never
        /// disagree about what they were derived from.</summary>
        internal string Source;
    }

    /// <summary>
    /// <b>HOW WIDE THE WINDOW IS, AND WHERE ITS MIDDLE IS — the one rule, for every handle.</b>
    ///
    /// <para><b>THE USER'S RULING THAT WROTE IT</b> (2026-09-05, <c>händlerbalken.jpg</c>):
    /// <i>"Die Handles für den Händler und co. reagieren so als wäre das Fenster ganz klein. Ich
    /// möchte dass die Grabbalken dort mittig so ausgerichtet sind wie für ein Fenster dieser
    /// Größe."</i> The merchant's rod was 284 px long and hung under the RIGHT-HAND EDGE of a
    /// 1920 px window, because the ink union it was sized from spanned <c>x 461..977</c> — the item
    /// list alone. The shopkeeper artwork, which is the other two thirds of everything that window
    /// paints, was not in that union: it is a FULL-FRAME PLATE, and <c>PanelInkBounds</c> excludes
    /// those by construction.</para>
    ///
    /// <para><b>THE RULE, AND WHY IT IS A RULE RATHER THAN A CORRECTION FACTOR.</b> The ink union
    /// exists to answer one question — <i>is this window's frame bigger than the picture inside
    /// it?</i> — and there are exactly two ways a frame can be bigger:
    /// <list type="bullet">
    /// <item><b>The frame is TRANSPARENT around the content.</b> <c>New Party display</c> is the
    /// case the union was written for: a 1988 px frame with a 328 px character column at x -818 and
    /// nothing else painted at all (its own reading in the ModBuild 446 log: <c>0 full-frame
    /// plate(s)</c>). Here the frame is not a window, it is empty air, and a rod as wide as it would
    /// hang over nothing. The ink union IS the window, and the bar takes its width and its
    /// centre.</item>
    /// <item><b>The frame is PAINTED and the union merely excluded the paint.</b> <c>UI Shop Item
    /// Window</c> and <c>UI Temple Window</c> both report <c>2 full-frame plate(s)</c>: a graphic
    /// covering at least 0.80 of the frame's width and 0.95 of its height, at an effective alpha the
    /// content fit itself calls visible, is a surface the player is looking at. A window that paints
    /// a plate across its whole frame IS a window of that size — that is the user's sentence
    /// restated as geometry — so the bar takes the FRAME's width and the frame's centre.</item>
    /// </list>
    /// No fudge factor, no blend, no threshold tuned until the picture looked right: one boolean,
    /// and it is the plate count <c>PanelInkBounds</c> has printed on every <c>GRAB BAR CLEARS THE
    /// INK</c> line since ModBuild 235 without anybody ever acting on it.</para>
    ///
    /// <para><b>IT AGREES WITH THE INSTRUMENT THAT ALREADY KNEW.</b> The content fit measures the
    /// same windows and counts a plate as content, and on every window in the ModBuild 446 log this
    /// rule lands exactly on the fit's own <c>DRAWN CONTENT</c> answer: shop <c>1920x1080 px at
    /// (0,0)</c> (:5949), <c>Quest Log Manager</c> <c>390x880 px at (0,0)</c> (:3758),
    /// <c>New Party display</c> <c>328x1080 px at (-818,0)</c> (:4281). That is the whole point of
    /// it — the laser already treats a painted plate as the window (the hit rect is
    /// <c>Content ∪ Host</c> by contract) and the handle was the one piece of the window that
    /// disagreed.</para>
    ///
    /// <para><b>WHAT THIS RULE MUST NOT BE ASKED.</b> It answers the HORIZONTAL question only. How
    /// far DOWN the bar hangs is <c>GrabbableModal.SyncBar</c>'s and nothing here touches it — but
    /// the reason given for that separation in ModBuild 447 was FALSE and cost the co-player a round.
    /// It read: "a plate's bottom edge IS the frame's bottom edge, so a plate can never lift a bar
    /// off content the bar was moved to clear (<c>quest_überlap.jpg</c>, ModBuild 236)". The plate
    /// test is a GREATER-OR-EQUAL on both axes, so a plate may be BIGGER than its frame; an
    /// aspect-preserving artwork on a canvas that is not the artwork's aspect always is. On the
    /// co-player's 2580x1080 canvas <c>UI Shop Item Window/ShopKeeper_Art</c> hangs 371 px below the
    /// frame (the fit names it in as many words: <i>FURTHEST OUTSIDE the frame:
    /// 'UI Shop Item Window/ShopKeeper_Art' by 371 px</i>) and the rod, seated one gap under the
    /// FRAME, sat in the middle of the merchant. ModBuild 449 gave the vertical seat its own term,
    /// <c>PanelInkBounds.Ink.PlateBottom</c>; the two rules still cannot fight over it, because the
    /// vertical one takes a <c>Min</c> and can only ever push the rod further DOWN. Nor does this
    /// rule decide whether a
    /// window has content at all — that verdict is the plate-EXCLUDING graphic count, which is why
    /// the exclusion stays exactly where it is: a window painting nothing but its own backdrop is an
    /// empty window and still loses its handle ("Wenn kein Fenster inhalt hat soll neben der
    /// Animation auch kein Greifbalken erscheinen").</para>
    ///
    /// <para><b>THE OTHER TWO OWNERS PASS <paramref name="inkValid"/> FALSE</b> and get the frame,
    /// which is byte-for-byte what <c>SurfaceGrabBar</c> and <c>CombatLogSurface</c> already did —
    /// a decision surface's rect IS its content, so it has no union to consult. They call this
    /// anyway, so that the day either of them grows one there is a rule to grow into rather than a
    /// second copy of this argument.</para>
    /// </summary>
    /// <param name="frameWidth">The host/frame width, in the grab frame's own local units.</param>
    /// <param name="inkValid">Whether a committed ink union exists for this window at all.</param>
    /// <param name="inkWidth">The committed union's width, in the same units as
    /// <paramref name="frameWidth"/>.</param>
    /// <param name="inkCentre">The committed union's centre, as an offset from the frame's centre,
    /// in the same units.</param>
    /// <param name="framePainted">Whether the window paints a full-frame plate — i.e. whether the
    /// committed ink walk counted one. See the rule above.</param>
    internal static Span SolveSpan(float frameWidth, bool inkValid, float inkWidth, float inkCentre,
                                   bool framePainted)
    {
        var span = default(Span);
        if (!inkValid)
        {
            span.Width = frameWidth;
            span.Centre = 0f;
            span.Source = "the host rect (no ink union has been committed for this window, so its "
                          + "frame is the only thing there is to measure)";
            return span;
        }
        if (framePainted)
        {
            span.Width = frameWidth;
            span.Centre = 0f;
            span.Source = "the host rect (this window paints a plate across its whole frame, so the "
                          + "frame IS the window — the ink union still owns how far DOWN the rod "
                          + "hangs)";
            return span;
        }
        // THE MIN IS THE OLD CAP, NOT A NEW ONE. Before this the caller computed
        //   frameBar = Max(width * F, min); bar = Clamp(inkW * F, min, frameBar)
        // and that upper clamp is exactly "the bar can only ever get NARROWER than the frame-based
        // one". Keeping it here means content drawing a little outside its own frame (the temple
        // reaches x=978 against a frame that ends at 960) lengthens nothing.
        span.Width = Mathf.Min(inkWidth, frameWidth);
        span.Centre = inkCentre;
        span.Source = "the ink union (this window's frame is transparent around what it draws, so "
                      + "the frame is empty air and a frame-wide rod would hang over nothing)";
        return span;
    }

    /// <summary>Every number a caller needs to place, size and grip one rod. A plain value struct:
    /// <see cref="Solve"/> allocates nothing and may be called per frame.</summary>
    internal struct Rod
    {
        /// <summary>The short-panel factor, clamped to [<see cref="MinBarProportion"/>, 1].</summary>
        internal float Proportion;

        /// <summary>Panel bottom edge to bar CENTRE, in the frame's units.</summary>
        internal float Gap;

        /// <summary>The UNIFORM scale the rod's root takes. A rod cannot take a non-uniform write:
        /// stretching along its axis smears the domed caps into ellipsoids and flattens the beaded
        /// rings, which is the whole reason <c>GrabBarVisual</c> exists — so the two factors a
        /// stretched cube used to carry in its localScale become one uniform scale here and the
        /// LENGTH is handed in separately through <c>GrabBarVisual.SetLength</c>.</summary>
        internal float Scale;

        /// <summary>The drawn shaft DIAMETER, in the frame's units. Only consumers that must reason
        /// about the rod's top edge (a badge's clearance, a falsifier's gap report) need it.</summary>
        internal float Thickness;

        /// <summary>The drawn end-to-end length, in the frame's units. Divide by
        /// <see cref="Scale"/> before handing it to <c>GrabBarVisual.SetLength</c>, which wants the
        /// rod's OWN local metres under a root scaled by <see cref="Scale"/>.</summary>
        internal float BarWidth;

        /// <summary>The palm zone box's width, in the frame's units.</summary>
        internal float ZoneWidth;

        /// <summary>The palm zone box's height and depth, in the frame's units.</summary>
        internal float ZoneDepth;
    }

    /// <summary>
    /// Size one rod. See the class doc for the two frames <paramref name="sourceWidth"/> and
    /// <paramref name="panelHeight"/> are in — they are deliberately different and mixing them up
    /// is the one way to misuse this function.
    /// </summary>
    /// <param name="sourceWidth">The width the bar is a fraction of, in the grab frame's own local
    /// units. A caller with an ink union hands in the NARROWER of the ink and the frame; a caller
    /// without one hands in the host rect's width.</param>
    /// <param name="panelHeight">The panel's height in REAL metres (the diorama scale divided back
    /// out), because it is measured against a comfort bound stated in metres.</param>
    /// <param name="worldScale">The scale the fixed metre constants must be expressed in to land
    /// in the frame's units. 1 when the frame's own holder already carries the diorama scale.</param>
    internal static Rod Solve(float sourceWidth, float panelHeight, float worldScale)
    {
        var rod = default(Rod);
        rod.Proportion = Mathf.Clamp(panelHeight / BarFullSizePanelHeightMeters,
                                     MinBarProportion, 1f);
        rod.Gap = BarGapMeters * rod.Proportion * worldScale;
        rod.Scale = Mathf.Max(rod.Proportion * worldScale, 1e-4f);
        rod.Thickness = BarRadius * 2f * rod.Scale;
        rod.ZoneDepth = ZoneDepthMeters * worldScale;
        // The MinBarWidth floor is deliberately applied HERE and not inside the callers' settle
        // rules: it is a hard invariant about a rod shorter than its own two caps, not a size a
        // settle window may own.
        float minWidth = MinBarWidth * worldScale;
        rod.BarWidth = Mathf.Max(sourceWidth * BarWidthFraction, minWidth);
        rod.ZoneWidth = Mathf.Max(sourceWidth * ZoneWidthFraction, minWidth);
        return rod;
    }
}
