namespace GloomhavenVR.Core;

/// <summary>
/// WHEN A PROP STANDS ON THE FLOOR AND IS THEREFORE NEVER WALL GEOMETRY — the whole geometric
/// test, and nothing else. Free of Unity on purpose (see the bottom of this header).
///
/// <para>THE REPORT, fourth round. 2026-08-19, hardware, ModBuild 167, verbatim: <i>"Der Schädel
/// ist immer noch nicht sichtbar."</i> The photograph is <c>.planning/debug/skelet.jpg</c>, and
/// this is what is in it, read off the image rather than off a log: a full human skeleton is
/// slumped on a WOODEN DECK at floor level, leaning back against a low masonry wall, legs splayed
/// forward across the planks, with burning hexes and scattered parchment around it. Ribcage,
/// clavicles, both arms, pelvis and both legs are solid and fully drawn. THE SKULL IS GONE — above
/// the ribcage there is a stub of vertebrae and nothing else. The low wall directly behind the
/// skeleton is MID-DISSOLVE: its top edge carries the ragged noise pattern of the masonry fade,
/// not an authored broken edge. So: a floor-level prop, one renderer of which vanished together
/// with a fading wall.</para>
///
/// <para>WHAT THE THREE PREVIOUS ROUNDS FIXED, and why none of them was this. ModBuild 157
/// (<c>WallSegmentFade.Standing.cs</c>) refuses a floor-standing FIGURE/ACTOR prop on the wall
/// path. ModBuild 167 (<c>WallSegmentFade.PropUnit.cs</c> + <c>WallPropUnit.cs</c>) gives a
/// multi-part prop torn between two wall units one single owner. Both are real and both stay. But
/// in the ModBuild-167 hardware log the <c>PROP UNIT</c> pass printed ZERO lines — it never fired
/// once — and the <c>CR_OS_Skeleton_Statue_*</c> renderers those rounds were written around are
/// anchored at 3.5 and 5.1 wu above the floor and agree on their owners in that same log. They are
/// wall statues somewhere else in the level, not the thing in the photograph.</para>
///
/// <para>WHAT BLOCKS THE ONE RULE THAT SHOULD HAVE COVERED IT. The ModBuild-157 rule prints its
/// own three terms in the log (<c>LogOutput.log</c>, the <c>STANDING PROP</c> line, verbatim):
/// <i>"figure/actor ancestry AND the unit's union AABB reaches within 1.0 wu of its room's floor
/// AND the unit is prop-sized (span ≤ 6.0 wu, ≤ 24 renderers)"</i>. A scenery skeleton lying on a
/// deck carries no <c>Animator</c>, no <c>ActorBehaviour</c> and no <c>CInteractableActor</c>, so
/// the first term refuses it however well the other two fit. And that term is doing no work the
/// others are not: the same log line states the geometric test's own justification —
/// <i>"wall-MOUNTED dressing never reaches the floor and keeps fading with its masonry"</i>. That
/// is the discriminator. Figure ancestry was only ever the way the first candidate was found.</para>
///
/// <para>THE RULE, therefore, in two arms. The FIGURE arm is ModBuild 157 bit-for-bit and is left
/// alone. The FLOOR arm is new and is what the photograph asks for:</para>
/// <list type="number">
/// <item>THE UNIT reaches the floor: its union AABB's LOWEST point sits within
///   <see cref="FootBandWU"/> of the room's floor plane. Deliberately the union of the whole prop
///   and not of the reported renderer — a skull one metre up looks airborne on its own and does
///   not once it is judged as part of the skeleton it belongs to. That is exactly the split the
///   photograph shows.</item>
/// <item>PROP-SIZED HORIZONTALLY: <see cref="MaxSpanWU"/> across and at most
///   <see cref="MaxRenderers"/> renderers, the caps ModBuild 157 introduced against the one
///   catastrophe this rule can cause — a unit that swallows a room turns every wall renderer
///   under it into protected geometry and wall see-through silently stops working.</item>
/// <item>PROP-SIZED VERTICALLY, and this term is NEW, because without it the floor arm is that
///   catastrophe. A wall course stands on the floor too. The ModBuild-167 hardware log prints the
///   AABB top of every renderer a wall fades (<c>fade ON … 87 toggle-native: Blocks@2.8,
///   Pillar@3.3, Wall@3.3, WallTop@3.4, CR_OS_Pillar_Large_03@3.7 …</c>): in that whole session
///   the LOWEST-topped piece of masonry or architecture reaches 2.8 wu, while the tallest thing
///   the same lists carry that is dressing rather than structure — <c>CR_ST_Shelf_Books_Sparse_02
///   @2.5</c>, <c>EN_CR_LBSkull@2.4</c>, <c>SB_AncCaverns_DemonHead@1.9</c>,
///   <c>CR_ST_Shelf_Alchemy_Balance@1.3</c>, <c>EN_CR_Candle_06@1.2</c> — tops out at 2.5. So
///   <see cref="MaxHeightWU"/> = 2.5 wu sits in a real gap in this tileset's own numbers rather
///   than in an argument. It is a FIRST CUT and it is meant to be moved with evidence: the
///   <c>FADE WRITE</c> census (<c>WallSegmentFade.FadeCensus.cs</c>) now prints every unit's
///   measured height next to its verdict, so the next hardware log states the real number for the
///   skeleton in the photograph instead of leaving it to be guessed a fifth time.</item>
/// </list>
///
/// <para>WHY THE FIGURE ARM KEEPS NO HEIGHT CAP: it is shipped, confirmed behaviour with two
/// hardware rounds behind it, and a figure/actor prop is by construction not masonry. Adding a cap
/// there could only take protection away from something that currently has it.</para>
///
/// <para>WHAT THE RULE DOES NOT DECIDE, and this is the reason the blast radius is small: it
/// decides only whether a renderer may be collected as WALL geometry (and as a wall's foliage
/// dressing), plus — new with this round — whether it may be adopted as wall-MOUNTED dressing.
/// Without that second half the floor arm would be theatre: a skull refused by the wall path is
/// unclaimed, and the mounted sweep runs afterwards, is geometric, and would simply pick it up and
/// fade it anyway. The MOUNTED rule itself is untouched and keeps its census: a prop whose UNIT
/// hangs a metre over the floor is not a unit that reaches the floor, so nothing that rule
/// protects can meet this one.</para>
///
/// <para>FREE OF UNITY, for the reason the wire-test project's header sets out. The failure this
/// arithmetic ships is not an exception and not a log line. Too tight and the skull stays missing —
/// a fifth identical report. Too loose and a wall course becomes permanently solid, i.e. wall
/// see-through stops working in a way that looks like the mod being switched off. Both are seen
/// only by eye, from inside a headset, one photograph per round, and this defect has now cost
/// four. Every input here is a float and a count, so the case in the photograph — a prop root with
/// a low body and a separate skull renderer a metre up, one wall mid-dissolve — is driven through
/// it on the build machine (<c>tests/GloomhavenVR.WireTests/WallStandingPropVectors.cs</c>).</para>
///
/// <para>MULTIPLAYER: decides nothing that goes on the wire. It removes renderers from a LOCAL
/// wall's own lists; every peer runs the identical floats against the identical scene.</para>
///
/// <para>STEREO: no per-eye term exists here and none can — there is no camera, no screen and no
/// eye in this file. <c>.planning/wall-fade-stereo-rivalry.md</c> (parked) is about the masonry
/// shader discarding the same wall differently in each eye; nothing here touches
/// <c>_Cutoff</c>, the dissolve, the occlusion map or the vignette.</para>
/// </summary>
internal static class WallStandingProp
{
    /// <summary>How far above its room's floor plane a unit's LOWEST point may sit and still count
    /// as standing ON that floor. The same 1.0 wu as <c>GroundExclusionHeightWU</c>: that constant
    /// already encodes this tileset's "within this band of the floor is ground, never wall", and a
    /// prop whose foot is inside the ground band is standing on the ground by the same
    /// measurement.</summary>
    internal const float FootBandWU = 1.0f;

    /// <summary>Largest horizontal extent a unit may have and still be a PROP — the
    /// anti-catastrophe cap. An Animator (or a shared ancestor) sitting on a room-sized container
    /// must never turn that whole container into a protected prop. 6.0 wu is several times a
    /// statue's footprint and well under this tileset's wall runs and room containers (the
    /// report's rooms measure 6.7 x 6.9 wu and up, walls tens of wu long).</summary>
    internal const float MaxSpanWU = 6.0f;

    /// <summary>Most renderers a unit may own and still be a PROP — the second half of the same
    /// cap. The crypt statue has 4; the report's revealed map tiles carry 30-520 renderers each,
    /// so nothing structural can slip under this.</summary>
    internal const int MaxRenderers = 24;

    /// <summary>Tallest a unit may be and still be a floor-standing PROP rather than a course of
    /// masonry — the FLOOR arm only, and the term without which that arm would protect walls. See
    /// the file header for the two columns of hardware numbers this sits between (architecture
    /// from 2.8 wu upward, floor-band dressing to 2.5 wu). A first cut, printed with every verdict
    /// by the FADE WRITE census so the next log can move it on evidence.</summary>
    internal const float MaxHeightWU = 2.5f;

    /// <summary>A prop unit's measured extent — the union AABB of every renderer under its root,
    /// which is the whole point: a skull is judged as part of its skeleton, never on its own.</summary>
    internal readonly struct Unit
    {
        /// <summary>Lowest point of the union AABB (world units).</summary>
        internal readonly float MinY;
        /// <summary>Highest point of the union AABB (world units).</summary>
        internal readonly float MaxY;
        internal readonly float SpanX;
        internal readonly float SpanZ;
        /// <summary>How many renderers the unit root owns (inactive included — an inactive piece
        /// is still part of the prop).</summary>
        internal readonly int RendererCount;

        internal Unit(float minY, float maxY, float spanX, float spanZ, int rendererCount)
        {
            MinY = minY;
            MaxY = maxY;
            SpanX = spanX;
            SpanZ = spanZ;
            RendererCount = rendererCount;
        }

        internal float Height => MaxY - MinY;
        internal float WidestSpanXZ => SpanX > SpanZ ? SpanX : SpanZ;
    }

    /// <summary>
    /// Does this unit STAND ON THE FLOOR of the room whose plane is <paramref name="floorY"/> —
    /// and is it therefore never wall geometry, on any path, whatever shader or material slot its
    /// renderers carry?
    /// </summary>
    /// <param name="unit">The union of the WHOLE prop, never of the one renderer that happened to
    /// be reported. This is the term the photograph turns on.</param>
    /// <param name="floorY">The nearest ANCHORED room floor plane. Rooms can be terraced, so the
    /// lowest floor in the scene is the wrong reference for a prop on an upper terrace.</param>
    /// <param name="figureAncestry">True = the ModBuild-157 FIGURE arm (shipped, confirmed, no
    /// height cap). False = the FLOOR arm added for skelet.jpg, which must also be prop-sized
    /// vertically or it protects masonry.</param>
    /// <param name="why">The measured reason, printed verbatim by the STANDING PROP census — for
    /// a refusal as well as for a verdict, because "the rule did not fire" without "and by which
    /// number" is what cost this defect its fourth round.</param>
    internal static bool StandsOnFloor(in Unit unit, float floorY, bool figureAncestry,
                                       out string why)
    {
        if (unit.RendererCount <= 0 || unit.RendererCount > MaxRenderers)
        {
            why = $"{unit.RendererCount} renderer(s) — not prop-sized (cap {MaxRenderers})";
            return false;
        }
        if (unit.WidestSpanXZ > MaxSpanWU)
        {
            why = $"span {unit.SpanX:0.0}x{unit.SpanZ:0.0} wu — not prop-sized "
                  + $"(cap {MaxSpanWU:0.0} wu)";
            return false;
        }
        float foot = unit.MinY - floorY;
        if (foot > FootBandWU)
        {
            why = $"foot {foot:0.0} wu over floor {floorY:0.0} — wall-MOUNTED dressing, keeps "
                  + $"fading with its masonry (band {FootBandWU:0.0} wu)";
            return false;
        }
        if (!figureAncestry && unit.Height > MaxHeightWU)
        {
            why = $"height {unit.Height:0.0} wu — architecture, not a floor prop "
                  + $"(cap {MaxHeightWU:0.0} wu)";
            return false;
        }
        why = $"foot {foot:0.0} wu over floor {floorY:0.0}, height {unit.Height:0.0} wu, span "
              + $"{unit.SpanX:0.0}x{unit.SpanZ:0.0} wu, {unit.RendererCount} renderer(s) — "
              + (figureAncestry ? "figure/actor prop" : "floor prop");
        return true;
    }

    /// <summary>How big a unit is for the FADE WRITE census's ordering. The largest of the three
    /// AABB extents, and the census sorts ASCENDING on it: a skull is small, and four rounds have
    /// now been spent on a census that could truncate the one interesting row. Deliberately not a
    /// volume — a flat sheet of parchment has almost none and is not what is being looked
    /// for.</summary>
    internal static float SizeRank(float spanX, float spanY, float spanZ)
    {
        float m = spanX > spanY ? spanX : spanY;
        return m > spanZ ? m : spanZ;
    }

    /// <summary>A prop is TORN when a fade path wrote SOME of its renderers and left the rest
    /// solid. That is the exact shape of skelet.jpg — skull gone, ribcage and legs still there —
    /// and it is the comparison no previous census printed. One definition, here, so the census
    /// and any future guard cannot drift apart on what "torn" means.</summary>
    internal static bool IsTorn(int written, int total) => written > 0 && written < total;
}
