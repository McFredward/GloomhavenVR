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
