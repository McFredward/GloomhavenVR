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
