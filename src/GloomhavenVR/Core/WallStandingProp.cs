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
/// <para>A THIRD ARM — THE FREE-STANDING TREE. 2026-08-24, hardware, ModBuild 256,
/// <c>wand_problem2.jpg</c>, verbatim: <i>"Das Problem hat sich verbessert: Eine der drei grünen
/// Wände verhält sich nun wie ich es erwarten würde, aber die zwei sich gegenüberliegende grüne
/// Wände immer noch nicht. […] Die Wand gegenüber sollte wieder sichtbar sein, sie verdeckt nichts
/// von der Fläche."</i> The ModBuild-256 <c>PER-WALL</c> line names the piece that accepted each
/// wall's FIRST blocking ray, and it splits the scene exactly along his report: 'Wall 3' (the gate)
/// is decided by <c>Blocks</c> / <c>EN_CR_Pillar_Large_02</c> / <c>Wall</c> — masonry, 37 of 37
/// samples — and he calls it correct; 'Wall 1', 'Wall 2' and 'Wall 4' are decided by
/// <c>FR_Pillar_Tree_Trunk_0*</c>, <c>FR_Tree_02 (3)</c> and <c>FR_Tree_05 (2)</c> in 113 of 172
/// attributions across the session, and he calls all three broken. The walls that behave are
/// decided by masonry; the walls that latch are decided by TREES.</para>
///
/// <para>WHAT THE TREES DO TO THE NUMERATOR, in that log's own numbers. 'Wall 4' reports
/// <c>blk 4/16 cells #0,#1,#4,#8</c> in 24 separate samples and 'Wall 2' reports
/// <c>blk 4/16 cells #7,#11,#14,#15</c> in 21 — the SAME four cells every time, opposite corners
/// of the same room, regardless of where the head is. A head-independent cell set is geometry
/// standing ON those cells, not geometry between the eye and them. 4/16 = 0.25 and the exit bar is
/// 0.20, so a wall pinned there can never fall back out: that is <i>"egal welche Position ich
/// einnehme"</i> and <i>"drehe ich mich einmal im Kreis ist alles gefaded … mach ich das nochmal
/// bleibt alles gefaded"</i>, in arithmetic.</para>
///
/// <para>WHY THIS IS A MEMBERSHIP QUESTION AND NOT AN EXEMPTION. <c>RayHitsWallMesh</c>'s governing
/// rule is deliberate and correct — <i>a renderer counts as this wall's occluding geometry exactly
/// when it RIDES this wall's fade</i> — so the question is not whether the trunk belongs in the
/// numerator, it is whether a free-standing tree should ride a wall's fade at all. It should not: a
/// tree standing on the room floor is scenery, exactly like the skeleton this file was written for
/// and like the floor plants the FLOOR arm already protects. Taken out of the membership it leaves
/// the numerator by construction, with no second heuristic anywhere near the ray test.</para>
///
/// <para>WHY THE FLOOR ARM DID NOT ALREADY COVER IT, and why the fix is TWO terms and not one. The
/// ModBuild-256 <c>STANDING PROP</c> census prints the refusal verbatim:
/// <c>'PCG_FR_Pillar_Tree_Trunk_02_PR' height 4.6 wu — architecture, not a floor prop (cap 2.5
/// wu)</c>. The trunk unit reaches the floor band, passes the span cap and the renderer-count cap,
/// and is refused by <see cref="MaxHeightWU"/> alone. Height cannot be the discriminator: the same
/// census lists <c>'PCG_FR_Wall_Space_03_PR' height 4.1 wu</c> — a genuine wall — half a metre
/// SHORTER than the tree. So:</para>
/// <list type="number">
/// <item>SLENDER — <see cref="Unit.SlendernessHW"/> ≥ <see cref="TreeSlendernessRatio"/>. A wall
///   tile is about as wide as it is tall; a trunk is tall and narrow. THE MEASURED NUMBER ON THE
///   WALL SIDE, and the only unit in that whole log whose height and widest span are both stated:
///   the <c>FADE WRITE</c> census's <c>'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' … unit
///   y[-0.3..2.7] over floor 0.0, widest 3.0 wu</c> → 3.0 wu tall over 3.0 wu wide = ratio
///   <b>1.00</b>, i.e. the tileset's own scrub wall sits a full factor of two under the bar. THE
///   MEASURED NUMBER ON THE TREE SIDE IS INCOMPLETE AND THIS IS STATED ON PURPOSE: the census
///   printed the trunk unit's height (4.6 wu) and not its span, because it only ever printed the
///   term a unit failed on. At a bar of 2.0 the escape fires for that unit exactly when its widest
///   XZ is under 2.3 wu. THE FALSIFIER: the census now prints <c>h/w</c> for EVERY unit it reports,
///   verdict and near-miss alike, so one grep of the next hardware log either shows the trunk unit
///   above 2.0 (the bar is right) or at/under it (the bar is wrong, and the discriminator is not
///   shape — do not nudge this constant, change the term).</item>
/// <item>VEGETATION — at least one renderer under the unit root is on a Foliage-family shader.
///   Slenderness ALONE is not safe and the hard constraint of this round says so: the user has
///   explicitly confirmed the gate wall behaves correctly, and 'Wall 3' is first-blocked twice by
///   <c>EN_CR_Pillar_Large_02</c> — a stone pillar is tall and narrow in exactly the way a trunk
///   is. The vegetation term is what makes this round PROVABLY unable to touch that wall: every
///   <c>fade ON 'Wall 3'</c> line in the log reads <c>+0 foliage</c>, so no unit under that anchor
///   can carry a Foliage-shaded renderer and no unit under it can reach this arm. And the term is
///   not sufficient on its own either — the scrub wall's <c>..._Bushes_01</c>, <c>..._Ivy_Grass_01</c>
///   and <c>..._Plants_01</c> attachments are Foliage-shaded too, so vegetation alone would protect
///   the Gestrüpp-Wand and hand back the report that <i>"Die anderen 'gestrüpp-wände' versperren
///   mir nun auch manchmal die Sicht. Das darf niemals passieren."</i> It is the CONJUNCTION that
///   selects trees: the scrub wall is vegetation but not slender (1.00), the pillar is slender but
///   not vegetation (+0 foliage).</item>
/// </list>
///
/// <para>NOT THE RATIO ModBuild 255 RETIRED. That was <c>IsStandingPiece</c>, a PER-RENDERER
/// aspect test applied INSIDE the occlusion numerator, and it was retired because it excluded zero
/// pieces on the two wrongly-faded walls and its only actual exclusions were real capstones
/// (<c>'Wall 3' 10 admitted / 4 excluded, widest excluded 'WallTop' 2.4 wu</c>). This is a
/// PER-PROP-UNIT test in the MEMBERSHIP classifier: different data (a unit's union, not one
/// renderer's box), a different place (before collection, not inside the ray walk), a different
/// direction (it ADMITS a unit to protection, it never removes a piece from a wall's numerator on
/// its own), and it is paired with a vegetation term the retired one had no equivalent of. The
/// retired test still lives in <c>WallSegmentFade.cs</c> as the shape statistic the admission
/// census reports, and it stays retired.</para>
///
/// <para>THE SPAN AND RENDERER CAPS STILL APPLY TO THIS ARM, unchanged, which is what stops it
/// being the catastrophe the height cap was added against: a whole wall RUN is over 6.0 wu across
/// or over 24 renderers and can never reach the slenderness question at all.</para>
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

    /// <summary>How many times taller than wide a floor-standing VEGETATION unit must be before it
    /// is a free-standing tree rather than a course of wall. See the file header's third arm for
    /// both sides of this number: the tileset's own scrub wall measures 3.0 wu tall over 3.0 wu
    /// widest = <b>1.00</b> (ModBuild-256 <c>FADE WRITE</c> census, the one unit whose height and
    /// span that log states together), so the bar sits a full factor of two above the only
    /// architecture it has to clear. The tree side of the bar is NOT yet measured — the census
    /// printed <c>'PCG_FR_Pillar_Tree_Trunk_02_PR' height 4.6 wu</c> and no span — so this arm
    /// fires for that unit exactly when its widest XZ is under 2.3 wu, and the census now prints
    /// <c>h/w</c> for every unit so the next log settles it in one grep. If the trunk unit comes
    /// back at or under 2.0 the discriminator is not shape and this constant must not be nudged to
    /// paper over that.</summary>
    internal const float TreeSlendernessRatio = 2.0f;

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

        /// <summary>Height over WIDEST horizontal extent — how many times taller than wide this
        /// unit is. The WIDEST span and not the narrower one on purpose: a wall RUN is wide in one
        /// axis and thin in the other, and dividing by the thin axis would score it as slender as a
        /// trunk (that choice is exactly what made the retired per-renderer test call a capstone a
        /// standing piece). A unit with no measurable footprint scores 0 — never slender — because
        /// "no width" must not read as "infinitely tall and thin".</summary>
        internal float SlendernessHW => WidestSpanXZ > 0.001f ? Height / WidestSpanXZ : 0f;
    }

    /// <summary>
    /// Is this unit a FREE-STANDING TREE — vegetation that stands on the room floor and is
    /// therefore scenery, never a course of wall? All THREE terms are required and the file header
    /// sets out why neither of the first two is sufficient: the scrub wall is vegetation at ratio
    /// 1.00, a stone pillar is slender with no vegetation at all.
    ///
    /// <para>THE HEIGHT TERM IS THE THIRD ONE AND IT IS NOT DECORATION. This arm exists to LIFT
    /// <see cref="MaxHeightWU"/> for a tree, so it must never fire below that cap.
    /// <c>WallSegmentFade.Standing.cs</c> hands this verdict to the FOLIAGE paths as well as to
    /// the wall path, and a grass tuft or a narrow bush unit clears 2.0 h/w easily — protecting one
    /// of those from a foliage list is exactly the Gestrüpp-Wand report, <i>"Die anderen
    /// 'gestrüpp-wände' versperren mir nun auch manchmal die Sicht. Das darf niemals
    /// passieren."</i> Under the cap the ordinary FLOOR arm already decides, and the foliage paths
    /// deliberately do not get that arm.</para>
    /// </summary>
    /// <param name="vegetation">At least one renderer under the unit root is on a Foliage-family
    /// shader — measured over the whole UNIT, so a bare trunk still counts as a tree when its own
    /// canopy is part of the same prop.</param>
    internal static bool IsFreeStandingTree(in Unit unit, bool vegetation)
        => vegetation
           && unit.Height > MaxHeightWU
           && unit.SlendernessHW >= TreeSlendernessRatio;

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
        => StandsOnFloor(unit, floorY, figureAncestry, vegetation: false, out why);

    /// <inheritdoc cref="StandsOnFloor(in Unit, float, bool, out string)"/>
    /// <param name="vegetation">Does any renderer under the unit root sit on a Foliage-family
    /// shader — the second half of the TREE arm (see the file header). Passing false is exactly
    /// the ModBuild-167 rule, which is why the four-argument overload above still exists and still
    /// means what it meant.</param>
    internal static bool StandsOnFloor(in Unit unit, float floorY, bool figureAncestry,
                                       bool vegetation, out string why)
    {
        // SHAPE, printed on EVERY line this method produces — verdict and refusal alike. The
        // ModBuild-256 census printed only the term a unit failed on, which is why this round had
        // to place a ratio bar with one measured wall unit and zero measured tree units. One
        // number per line ends that: the next log carries the whole distribution.
        string shape = $"h {unit.Height:0.0} wu / w {unit.WidestSpanXZ:0.0} wu = "
                       + $"{unit.SlendernessHW:0.00} h/w"
                       + (vegetation ? ", vegetation" : ", no vegetation");
        if (unit.RendererCount <= 0 || unit.RendererCount > MaxRenderers)
        {
            why = $"{unit.RendererCount} renderer(s) — not prop-sized (cap {MaxRenderers}) "
                  + $"[{shape}]";
            return false;
        }
        if (unit.WidestSpanXZ > MaxSpanWU)
        {
            why = $"span {unit.SpanX:0.0}x{unit.SpanZ:0.0} wu — not prop-sized "
                  + $"(cap {MaxSpanWU:0.0} wu) [{shape}]";
            return false;
        }
        float foot = unit.MinY - floorY;
        if (foot > FootBandWU)
        {
            why = $"foot {foot:0.0} wu over floor {floorY:0.0} — wall-MOUNTED dressing, keeps "
                  + $"fading with its masonry (band {FootBandWU:0.0} wu) [{shape}]";
            return false;
        }
        bool tree = IsFreeStandingTree(unit, vegetation);
        if (!figureAncestry && !tree && unit.Height > MaxHeightWU)
        {
            why = $"height {unit.Height:0.0} wu — architecture, not a floor prop "
                  + $"(cap {MaxHeightWU:0.0} wu; not a tree either — the TREE arm wants "
                  + $"vegetation AND ≥ {TreeSlendernessRatio:0.0} h/w) [{shape}]";
            return false;
        }
        why = $"foot {foot:0.0} wu over floor {floorY:0.0}, height {unit.Height:0.0} wu, span "
              + $"{unit.SpanX:0.0}x{unit.SpanZ:0.0} wu, {unit.RendererCount} renderer(s) — "
              + (figureAncestry ? "figure/actor prop"
                 : tree ? "free-standing TREE"
                 : "floor prop")
              + $" [{shape}]";
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
