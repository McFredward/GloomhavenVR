using GloomhavenVR.Core;

namespace GloomhavenVR.WireTests;

/// <summary>
/// WHEN A PROP STANDS ON THE FLOOR AND MUST NEVER FADE WITH A WALL.
///
/// <para>WHY THIS IS PINNED. The failure this arithmetic ships is not an exception and not a log
/// line, and it fails in BOTH directions with the same silence. Too tight, and the skeleton in
/// <c>.planning/debug/skelet.jpg</c> keeps losing its head — the report has now arrived four times
/// (2026-08-15, twice on 2026-08-19, most recently <i>"Der Schädel ist immer noch nicht
/// sichtbar."</i>). Too loose, and a course of masonry becomes permanently solid, which does not
/// look like a bug at all: it looks like wall see-through having been switched off. Both verdicts
/// are seen only by eye, from inside a headset, one photograph per round, and nothing else in this
/// repository can notice either. <c>WallStandingProp.cs</c> was written free of Unity — floats and
/// counts — precisely so the case in the photograph could be driven here, unit by unit.</para>
///
/// <para>THE FIRST CASE BELOW IS THE PHOTOGRAPH, read off the image: a skeleton slumped on a
/// wooden deck at floor level, ribcage/arms/pelvis/legs solid, THE SKULL GONE, and the low masonry
/// wall directly behind it mid-dissolve with the ragged noise pattern on its top edge. Modelled as
/// a prop root with a low body and a separate skull renderer roughly a metre up. The assertion is
/// that the WHOLE unit is protected — and, in the case right after it, that the skull judged ON ITS
/// OWN is not, which is exactly why the rule measures the unit's union and never the renderer that
/// happened to be reported.</para>
///
/// <para>THE REST ARE THE THINGS THAT MUST STILL FADE, because a rule that protects everything
/// protects nothing: a wall course (which also stands on the floor — this is the case the new
/// height term exists for), a sconce hanging a metre up (the shipped WALL-MOUNTED DRESSING rule),
/// a room-sized container and an over-large renderer group (the two caps against turning a whole
/// room into one protected prop).</para>
///
/// <para>THE ModBuild-257 BLOCK IS THE FOUR CORNERS OF THE TREE ARM'S CONJUNCTION, and every one
/// of them is a report the user has already made or a ruling he has already given. Slender AND
/// vegetation frees the trunk that latched 'Wall 2' and 'Wall 4' (wand_problem2.jpg). Vegetation
/// WITHOUT slenderness must leave the scrub wall fading, or "Die anderen 'gestrüpp-wände'
/// versperren mir nun auch manchmal die Sicht" comes straight back. Slenderness WITHOUT vegetation
/// must leave a masonry pillar alone, because he has confirmed the gate wall now behaves and
/// 'Wall 3' is first-blocked twice by <c>EN_CR_Pillar_Large_02</c>. And neither term may lift the
/// span or renderer caps, which are the only thing between this arm and a wall run turning into one
/// protected prop. The ratio bar itself is 2.0 h/w against a measured 1.00 on the wall side and an
/// UNMEASURED span on the tree side — see <c>WallStandingProp.TreeSlendernessRatio</c>, which says
/// so and names the grep that settles it.</para>
/// </summary>
internal static class WallStandingPropVectors
{
    /// <summary>The session's floor plane, as the hardware logs report it
    /// (<c>sampY[0.05..0.05]</c>).</summary>
    private const float FloorY = 0.05f;

    internal static void Run(Harness t)
    {
        // ---- the photograph: skelet.jpg -------------------------------------------------------
        // One prop root. The body/ribcage/legs lie on the deck; the skull sits about a metre up,
        // at the top of the spine. The union is what the rule judges.
        t.Case("standing/skelet-jpg-whole-unit");
        var skeleton = new WallStandingProp.Unit(
            minY: 0.06f,   // pelvis and legs on the planks, a finger above the floor plane
            maxY: 1.78f,   // top of the skull
            spanX: 1.45f,
            spanZ: 1.20f,
            rendererCount: 3); // body, skull, and the loose bones beside it
        bool ok = WallStandingProp.StandsOnFloor(skeleton, FloorY, figureAncestry: false,
                                                 out string why);
        t.True(ok, "a scenery skeleton lying on a deck stands on the floor and never fades "
                   + "with a wall — with NO figure/actor ancestry, which is the whole widening");
        t.True(why.Contains("floor prop"), "and the census says which arm protected it: " + why);

        // The consequence the photograph turns on: the verdict belongs to the UNIT, so the skull
        // rides it. Judged alone the very same skull is airborne and would keep fading — which is
        // the picture the user sent, twice.
        t.Case("standing/skelet-jpg-skull-alone");
        var skullOnly = new WallStandingProp.Unit(
            minY: 1.32f, maxY: 1.78f, spanX: 0.42f, spanZ: 0.40f, rendererCount: 1);
        t.True(!WallStandingProp.StandsOnFloor(skullOnly, FloorY, figureAncestry: false, out why),
               "a skull judged on its own looks like wall-mounted dressing — " + why);
        t.True(why.Contains("MOUNTED"),
               "…and is refused on the foot term, i.e. for hanging over the floor: " + why);

        // ---- what must still fade -------------------------------------------------------------
        // A WALL COURSE STANDS ON THE FLOOR TOO. This is the case the height term exists for, and
        // without it the widening would make masonry permanently solid — wall see-through silently
        // stopping, which is indistinguishable from the mod being switched off. Numbers from the
        // ModBuild-167 log's fade ON lines: 'CR_OS_Wall_01_Main@3.3' + 'CR_OS_Wall_01_Skulls@3.1'
        // are a two-renderer group whose AABB tops sit at 3.3 wu.
        t.Case("standing/wall-course-still-fades");
        var wallCourse = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 3.30f, spanX: 3.40f, spanZ: 0.62f, rendererCount: 2);
        t.True(!WallStandingProp.StandsOnFloor(wallCourse, FloorY, figureAncestry: false, out why),
               "a course of masonry reaches the floor, is prop-sized horizontally and holds two "
               + "renderers — only its HEIGHT separates it from a floor prop: " + why);
        t.True(why.Contains("architecture"), "and the line says so: " + why);

        // WALL-MOUNTED DRESSING (shipped, confirmed): a sconce/shelf hanging a metre over the floor
        // keeps dissolving with the masonry it hangs on. Numbers from the same log's near-miss
        // census: 'CR_OS_DoorSign' y[2.7..3.4].
        t.Case("standing/mounted-dressing-still-fades");
        var doorSign = new WallStandingProp.Unit(
            minY: 2.70f, maxY: 3.40f, spanX: 0.90f, spanZ: 0.30f, rendererCount: 1);
        t.True(!WallStandingProp.StandsOnFloor(doorSign, FloorY, figureAncestry: false, out why),
               "wall-mounted dressing never reaches the floor and keeps fading with its wall");
        t.True(!WallStandingProp.StandsOnFloor(doorSign, FloorY, figureAncestry: true, out why),
               "…on the figure arm too — the foot term is what discriminates, not the ancestry");

        // THE CATASTROPHE THE TWO CAPS EXIST FOR: an ancestor that reaches a room container would
        // turn every wall renderer under it into protected geometry. The report's rooms measure
        // 6.7 x 6.9 wu and up; its revealed map tiles carry 30-520 renderers.
        t.Case("standing/room-container-refused");
        var room = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 2.20f, spanX: 6.70f, spanZ: 6.90f, rendererCount: 12);
        t.True(!WallStandingProp.StandsOnFloor(room, FloorY, figureAncestry: false, out why),
               "a room-sized union is not a prop, however low and however few renderers: " + why);
        var crowd = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 2.20f, spanX: 2.00f, spanZ: 2.00f, rendererCount: 30);
        t.True(!WallStandingProp.StandsOnFloor(crowd, FloorY, figureAncestry: false, out why),
               "nor is a 30-renderer subtree, however small: " + why);
        t.True(!WallStandingProp.StandsOnFloor(room, FloorY, figureAncestry: true, out why),
               "and both caps hold on the figure arm too — they are the anti-catastrophe pair");

        // ---- ModBuild 157 is not regressed ----------------------------------------------------
        // The FIGURE arm keeps no height cap: it is shipped, confirmed behaviour, and a
        // figure/actor prop is by construction not masonry. The crypt statue ModBuild 157 was
        // written around measures y[0.0..3.6] with a 1.2 wu-class footprint and four renderers.
        t.Case("standing/figure-arm-unchanged");
        var statue = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 3.60f, spanX: 1.20f, spanZ: 1.10f, rendererCount: 4);
        t.True(WallStandingProp.StandsOnFloor(statue, FloorY, figureAncestry: true, out why),
               "a floor-standing figure/actor prop is protected at any height — ModBuild 157, "
               + "unchanged: " + why);
        t.True(why.Contains("figure/actor prop"), "and names its arm: " + why);
        t.True(!WallStandingProp.StandsOnFloor(statue, FloorY, figureAncestry: false, out why),
               "the same geometry WITHOUT figure ancestry is refused on height — the widening "
               + "adds floor props, it does not add architecture: " + why);

        // ---- ModBuild 257: the TREE arm --------------------------------------------------------
        // THE REPORT (2026-08-24, hardware, wand_problem2.jpg): "Eine der drei grünen Wände verhält
        // sich nun wie ich es erwarten würde, aber die zwei sich gegenüberliegende grüne Wände
        // immer noch nicht. […] Die Wand gegenüber sollte wieder sichtbar sein, sie verdeckt nichts
        // von der Fläche." The wall he calls correct is decided by masonry in 37 of 37 first-blocker
        // samples; the three he calls broken are decided by FR_Pillar_Tree_Trunk_0* / FR_Tree_02 /
        // FR_Tree_05 in 113 of 172. This arm lifts the height cap for a free-standing tree and for
        // nothing else, on TWO terms — and the four cases below are the four corners of that
        // conjunction, because either term alone breaks something the user has already signed off.
        t.Case("standing/tree-arm-frees-a-trunk");
        // The trunk unit as the ModBuild-256 STANDING PROP census measured it:
        // 'PCG_FR_Pillar_Tree_Trunk_02_PR' height 4.6 wu, refused by the 2.5 wu height cap alone.
        // ITS SPAN IS NOT IN THAT LOG — the census only ever printed the term a unit failed on —
        // so this vector pins the SHAPE the bar assumes, and the census now prints h/w for every
        // unit so the next hardware log either confirms it or falsifies it in one grep.
        var trunk = new WallStandingProp.Unit(
            minY: -0.40f, maxY: 4.20f, spanX: 1.10f, spanZ: 0.95f, rendererCount: 3);
        t.True(WallStandingProp.StandsOnFloor(trunk, FloorY, figureAncestry: false,
                                              vegetation: true, out why),
               "a slender vegetation unit standing on the room floor is a free-standing TREE — "
               + "scenery, never a course of wall: " + why);
        t.True(why.Contains("free-standing TREE"), "and the census names the arm: " + why);
        t.True(!WallStandingProp.StandsOnFloor(trunk, FloorY, figureAncestry: false,
                                               vegetation: false, out why),
               "the SAME geometry with no vegetation under it is refused — this is the term that "
               + "makes the round unable to touch a masonry pillar: " + why);

        // THE SCRUB WALL MUST STILL FADE. This is the measured number the bar was placed against:
        // the ModBuild-256 FADE WRITE census reads 'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' …
        // unit y[-0.3..2.7] over floor 0.0, widest 3.0 wu — 3.0 wu tall over 3.0 wu wide, h/w 1.00,
        // a full factor of two under the 2.0 bar. It is vegetation (its _Bushes/_Ivy_Grass/_Plants
        // attachments are Foliage-shaded), so vegetation ALONE would protect it and hand back
        // "Die anderen 'gestrüpp-wände' versperren mir nun auch manchmal die Sicht. Das darf
        // niemals passieren."
        t.Case("standing/tree-arm-leaves-the-scrub-wall");
        var scrubWall = new WallStandingProp.Unit(
            minY: -0.30f, maxY: 2.70f, spanX: 3.00f, spanZ: 2.40f, rendererCount: 6);
        t.True(!WallStandingProp.StandsOnFloor(scrubWall, FloorY, figureAncestry: false,
                                               vegetation: true, out why),
               "the tileset's own scrub wall is vegetation standing on the floor and must keep "
               + "fading — 3.0 wu tall over 3.0 wu wide is not a tree: " + why);
        t.True(!WallStandingProp.IsFreeStandingTree(scrubWall, vegetation: true),
               "h/w 1.00 is a full factor of two under the 2.0 bar, which is the whole margin");

        // THE HARD CONSTRAINT OF THE ROUND: the wall next to the gate behaves correctly and must
        // not change. 'Wall 3' is first-blocked twice by EN_CR_Pillar_Large_02 — a stone pillar is
        // tall and narrow in exactly the way a trunk is, so SLENDERNESS ALONE would strip it out of
        // that wall's numerator. It carries no vegetation (every 'fade ON Wall 3' line in the log
        // reads +0 foliage), and that is what keeps this round off it.
        t.Case("standing/tree-arm-leaves-a-masonry-pillar");
        var pillar = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 3.50f, spanX: 1.20f, spanZ: 1.10f, rendererCount: 2);
        t.True(WallStandingProp.IsFreeStandingTree(pillar, vegetation: true),
               "shape alone cannot tell a stone pillar from a trunk — it is slender by the same "
               + "measurement, which is why shape alone is not the rule");
        t.True(!WallStandingProp.StandsOnFloor(pillar, FloorY, figureAncestry: false,
                                               vegetation: false, out why),
               "…so the pillar is held by the VEGETATION term, and the gate wall's numerator is "
               + "untouched: " + why);

        // AND THE ANTI-CATASTROPHE CAPS STILL BIND THIS ARM. A whole wall RUN of vegetation must
        // never become one protected prop, however slender the union happens to score.
        t.Case("standing/tree-arm-keeps-the-caps");
        var hedgerow = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 14.00f, spanX: 6.50f, spanZ: 1.20f, rendererCount: 8);
        t.True(WallStandingProp.IsFreeStandingTree(hedgerow, vegetation: true),
               "a 14 wu union over a 6.5 wu footprint scores slender…");
        t.True(!WallStandingProp.StandsOnFloor(hedgerow, FloorY, figureAncestry: false,
                                               vegetation: true, out why),
               "…and is still refused on the span cap — the TREE arm lifts the HEIGHT term only, "
               + "never the two caps that stop a wall run becoming a prop: " + why);
        var thicket = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 9.00f, spanX: 2.00f, spanZ: 2.00f, rendererCount: 31);
        t.True(!WallStandingProp.StandsOnFloor(thicket, FloorY, figureAncestry: false,
                                               vegetation: true, out why),
               "nor is a 31-renderer subtree, however tall and thin: " + why);

        // A NARROW BUSH IS NOT A SAPLING. This arm LIFTS the height cap, so it must not fire below
        // it: WallSegmentFade.Standing.cs hands the verdict to the FOLIAGE paths too, and a grass
        // tuft or a thin bush unit clears 2.0 h/w without trying. Protecting one of those from a
        // foliage list is the Gestrüpp-Wand report with extra steps.
        t.Case("standing/tree-arm-is-not-a-bush");
        var tuft = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 1.10f, spanX: 0.35f, spanZ: 0.30f, rendererCount: 2);
        t.True(tuft.SlendernessHW >= 2.0f, "a tuft of grass is slender by shape alone…");
        t.True(!WallStandingProp.IsFreeStandingTree(tuft, vegetation: true),
               "…and is still not a tree, because it is under the height cap this arm exists to "
               + "lift — the FLOOR arm decides there, and the foliage paths do not get it");

        // THE FOUR-ARGUMENT OVERLOAD IS THE ModBuild-167 RULE, BIT FOR BIT. Everything above it in
        // this file calls it, and it must keep meaning what it meant: no vegetation, no tree arm.
        t.Case("standing/tree-arm-is-opt-in");
        t.True(!WallStandingProp.StandsOnFloor(trunk, FloorY, figureAncestry: false, out why),
               "the shipped four-argument call is vegetation-free and therefore tree-free: " + why);

        // A UNIT WITH NO FOOTPRINT IS NOT INFINITELY SLENDER. Degenerate bounds (a subtree of
        // renderers with empty meshes) must score 0 and never fall through as "tall and thin".
        t.Case("standing/tree-arm-degenerate-footprint");
        var degenerate = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 5.00f, spanX: 0.00f, spanZ: 0.00f, rendererCount: 2);
        t.True(degenerate.SlendernessHW == 0f,
               "no width does not mean infinitely slender — that is how a divide-by-zero becomes "
               + "a protected wall");
        t.True(!WallStandingProp.IsFreeStandingTree(degenerate, vegetation: true),
               "…so a degenerate unit is never a tree");

        // ---- the census's two derived facts ---------------------------------------------------
        // TORN is the shape of the photograph, and the comparison no previous census printed.
        t.Case("standing/torn-is-the-photograph");
        t.True(WallStandingProp.IsTorn(written: 1, total: 3),
               "one renderer of a three-piece skeleton faded and two left solid = TORN, which is "
               + "exactly skull-gone/ribcage-and-legs-there");
        t.True(!WallStandingProp.IsTorn(written: 3, total: 3),
               "a prop that fades WHOLE is not torn — that is the prop-unit pass working");
        t.True(!WallStandingProp.IsTorn(written: 0, total: 3),
               "and a prop nothing touched is not torn either");

        // SMALLEST FIRST, because a skull is small and the census must never silently drop it.
        t.Case("standing/census-orders-small-first");
        float skull = WallStandingProp.SizeRank(0.42f, 0.46f, 0.40f);
        float wall = WallStandingProp.SizeRank(3.40f, 3.30f, 0.62f);
        t.True(skull < wall, "a skull ranks ahead of a wall course, so the cap cannot drop it");
        t.True(WallStandingProp.SizeRank(0.1f, 0.2f, 5.0f) > wall,
               "the rank is the LARGEST extent, not a volume — a long flat sheet is not 'small'");
    }
}
