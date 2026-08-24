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
/// <para>THE ModBuild-258 BLOCK IS THE OTHER HALF OF THE SAME SENTENCE: a unit fades WHOLE or not
/// at all. ModBuild 257's TREE arm is gone (its tombstone, with the h/w distribution that killed
/// it, is in <c>WallStandingProp</c>) and what replaced it is structural rather than numeric —
/// <c>UnitFadesAsOne</c>, the exact negation of <c>StandsOnFloor</c>. The vectors below pin BOTH
/// directions on the two units the hardware log names, because getting either wrong is a shipped
/// regression: the scrub wall must fade base and all (105 log lines of
/// <c>TORN 'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' 4/6 written … LEFT SOLID: FR_Stones_06 (1),
/// FR_Stones_02 (2)</c>), and the skeleton and the floor-grass hex must stay protected as whole
/// units or <c>skelet.jpg</c> and the Gestrüpp-Wand come back.</para>
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

        // ---- ModBuild 258: THE TREE ARM IS RETIRED ---------------------------------------------
        // ModBuild 257 added a third arm: vegetation AND ≥ 2.0 h/w lifts the height cap for a
        // "free-standing tree". The ModBuild-257 hardware log falsified it on its own terms.
        // Every trunk unit in the session measures UNDER the bar ('PCG_FR_Pillar_Tree_Trunk_01_PR'
        // 1.39 h/w, _02_PR 1.46, _03_PR 1.79) because a trunk's union AABB contains its own vines
        // and floor bushes; the ONLY unit in the whole session at or over 2.0 is
        // 'PCG_FR_Wall_Space_04_PR' at 2.11 h/w — a WALL, which the arm then protected on every
        // path and which appears in no FADE WRITE line at all, i.e. never faded once. That is
        // wandproblem3.jpg: "ein Teil der Wand bleibt nun stehen und faded garnicht mehr".
        // These two vectors pin the retirement so it cannot be quietly re-added: VEGETATION IS
        // REPORTED AND DECIDES NOTHING, and no shape frees a unit from the height cap.
        t.Case("standing/tree-arm-retired-vegetation-decides-nothing");
        // The trunk the ModBuild-257 arm was written for, at the shape the arm assumed.
        var trunk = new WallStandingProp.Unit(
            minY: -0.40f, maxY: 4.20f, spanX: 1.10f, spanZ: 0.95f, rendererCount: 3);
        t.True(!WallStandingProp.StandsOnFloor(trunk, FloorY, figureAncestry: false,
                                               vegetation: true, out why),
               "a slender vegetation unit is architecture again — the TREE arm is retired: " + why);
        t.True(!WallStandingProp.StandsOnFloor(trunk, FloorY, figureAncestry: false,
                                               vegetation: false, out string whyNoVeg),
               "…and so is the same unit with no vegetation: " + whyNoVeg);
        t.True(why.Substring(0, why.IndexOf('[')) == whyNoVeg.Substring(0, whyNoVeg.IndexOf('[')),
               "the vegetation flag changes the VERDICT not at all and the TERM not at all — it "
               + "survives only inside the reported [shape] column, which is the whole retirement");
        t.True(why.Contains(", vegetation]") && whyNoVeg.Contains(", no vegetation]"),
               "…and it does still survive there, because that column is what retired the arm");

        // THE ONE UNIT THE ARM ACTUALLY FIRED ON, at its logged measurements. It must fade.
        t.Case("standing/tree-arm-retired-wall-space-04");
        var wallSpace04 = new WallStandingProp.Unit(
            minY: -0.30f, maxY: 4.20f, spanX: 2.10f, spanZ: 2.10f, rendererCount: 4);
        t.True(wallSpace04.SlendernessHW >= 2.0f,
               "'PCG_FR_Wall_Space_04_PR' measures h 4.5 / w 2.1 = 2.11 h/w — over the retired "
               + "2.0 bar, which is exactly why the arm fired on it");
        t.True(!WallStandingProp.StandsOnFloor(wallSpace04, FloorY, figureAncestry: false,
                                               vegetation: true, out why),
               "…and it is a WALL, so it must fade: " + why);
        t.True(WallStandingProp.UnitFadesAsOne(wallSpace04, FloorY, figureAncestry: false, out why),
               "…whole, base included: " + why);

        // ---- ModBuild 258: A UNIT FADES WHOLE OR NOT AT ALL ------------------------------------
        // THE SCRUB WALL, at the measurements the ModBuild-257 FADE WRITE census printed 105 times:
        // 'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' unit y[-0.3..2.7] over floor 0.0, widest
        // 3.0 wu, 6 renderers, of which 4 were written and FR_Stones_06/FR_Stones_02 were LEFT
        // SOLID by the 1.0 wu ground band. The standing rule has already called this unit
        // architecture ('height 3.0 wu — architecture, not a floor prop'), so the whole unit fades
        // and the band gets no vote inside it. This is the photograph, in one assertion.
        t.Case("unit/scrub-wall-fades-base-and-all");
        var scrubWall = new WallStandingProp.Unit(
            minY: -0.30f, maxY: 2.70f, spanX: 3.00f, spanZ: 2.40f, rendererCount: 6);
        t.True(WallStandingProp.UnitFadesAsOne(scrubWall, FloorY, figureAncestry: false, out why),
               "the tileset's scrub wall is architecture, so its two ground-hugging stones fade "
               + "with the rest of it — 'ein Teil der Wand bleibt nun stehen und faded garnicht "
               + "mehr' (wandproblem3.jpg): " + why);
        t.True(why.Contains("architecture") && why.Contains("base included"),
               "and the line states the term AND the consequence, so the next reader does not "
               + "have to infer it: " + why);

        // THE SKULL MUST STILL NOT VANISH. The same predicate, the other way round: the skeleton
        // is a floor prop, so it does NOT fade as one — it does not fade at all.
        t.Case("unit/skeleton-does-not-fade-at-all");
        t.True(!WallStandingProp.UnitFadesAsOne(skeleton, FloorY, figureAncestry: false, out why),
               "a protected unit is never fed to the whole-unit recruit, so skelet.jpg cannot "
               + "come back through this door: " + why);
        t.True(!WallStandingProp.UnitFadesAsOne(statue, FloorY, figureAncestry: true, out why),
               "…nor can a figure/actor prop, at any height (ModBuild 157, untouched): " + why);

        // THE COUNTER-EXAMPLE THE ROUND HAD TO KEEP. The ModBuild-257 census protects
        // 'PCG_FR_Floor_Grass_Hex_Split_PR' — foot -0.3 wu over floor 0.0, height 0.5 wu, span
        // 1.7x2.0 wu, 2 renderers — as a WHOLE unit. Its members are therefore never claimed by a
        // wall, the unit never gets an owner, and the ground-band lift can never reach it. If this
        // ever flips, the Gestrüpp-Wand report and the jungle floor come back together.
        t.Case("unit/floor-grass-hex-stays-protected");
        var grassHex = new WallStandingProp.Unit(
            minY: -0.30f, maxY: 0.20f, spanX: 1.70f, spanZ: 2.00f, rendererCount: 2);
        t.True(WallStandingProp.StandsOnFloor(grassHex, FloorY, figureAncestry: false,
                                              vegetation: true, out why),
               "a floor-grass hex is a floor prop and stays protected as a whole unit: " + why);
        t.True(!WallStandingProp.UnitFadesAsOne(grassHex, FloorY, figureAncestry: false, out why),
               "…so it never fades as one either — the two answers are one verdict: " + why);

        // THE TREE TRUNK AS THE HARDWARE ACTUALLY MEASURED IT, which is the vector the retired arm
        // never had. ModBuild 257's FADE WRITE census: 'PCG_FR_Pillar_Tree_Trunk_01_PR' unit
        // y[-0.3..3.6], 17 renderers; its STANDING PROP near-miss line: [h 3.9 wu / w 2.8 wu =
        // 1.39 h/w, vegetation]. A trunk is not slender once its own vines and floor bushes are
        // inside the union — which is why shape could never have been the discriminator, and why
        // this unit is architecture that fades whole, base bushes included.
        t.Case("unit/tree-trunk-unit-fades-whole");
        var trunkUnit = new WallStandingProp.Unit(
            minY: -0.30f, maxY: 3.60f, spanX: 2.80f, spanZ: 2.10f, rendererCount: 17);
        t.True(trunkUnit.SlendernessHW > 1.38f && trunkUnit.SlendernessHW < 1.40f,
               "the logged trunk unit measures 1.39 h/w — under the retired 2.0 bar, so the arm "
               + "could never have freed the thing it was written for");
        t.True(!WallStandingProp.StandsOnFloor(trunkUnit, FloorY, figureAncestry: false,
                                               vegetation: true, out why),
               "it is architecture by height, exactly as ModBuild 256 already had it: " + why);
        t.True(WallStandingProp.UnitFadesAsOne(trunkUnit, FloorY, figureAncestry: false, out why),
               "…so its FR_Floor_LargeBush_0* and FR_Floor_Detail_Grass_05_PR members fade with "
               + "it instead of standing there (7/17 written, 28 log lines): " + why);

        // A UNIT WITH NO FOOTPRINT. Degenerate bounds must not produce an infinite ratio in the
        // REPORTED column — it decides nothing now, but a NaN or an Infinity in a log line is
        // still a log line nobody can read.
        t.Case("unit/degenerate-footprint-scores-zero");
        var degenerate = new WallStandingProp.Unit(
            minY: 0.00f, maxY: 5.00f, spanX: 0.00f, spanZ: 0.00f, rendererCount: 2);
        t.True(degenerate.SlendernessHW == 0f,
               "no width does not mean infinitely slender, even for a column that only prints");
        t.True(WallStandingProp.UnitFadesAsOne(degenerate, FloorY, figureAncestry: false, out why),
               "and a 5 wu unit is architecture whatever its footprint reads: " + why);

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
