using GloomhavenVR.Core;

namespace GloomhavenVR.WireTests;

/// <summary>
/// A FLOOR TILE CAN NEVER OCCLUDE ANYTHING AND MUST THEREFORE NEVER BE FADED.
///
/// <para>THE PHOTOGRAPH is <c>.planning/debug/fehlende_boden_tiles.jpg</c>: you look through a
/// doorway at grass and void where the hex floor should be, the surrounding hexes intact. The
/// ModBuild-429 log names the mechanism on its UNION RULE line —
/// <c>'CR_EXT_Stone_Floor_01_Half_02'[mesh, prop-unit dressing] owner 'ThickDoor : (ee3cc9b8-…)'
/// fade 0.00 → rides 'Wall 2' fade 1.00</c> — and the same door's FADE WRITE row reads
/// <c>TORN 'HexDoor(Clone)' 6/22 written</c> with four of the six named as pillars and wall tops.
/// The two it dropped are those floor halves.</para>
///
/// <para>WHY THIS IS PINNED. The rule fails in both directions in silence, exactly like
/// <see cref="WallStandingProp"/> beside it. Too tight and the doorway hexes stay missing. Too
/// loose and a pillar's foot, a step or a low wall goes permanently solid — which does not read as
/// a bug, it reads as wall see-through having been switched off, and the user has already reported
/// that from the other side (<c>säulen.jpg</c>, <i>"faden die Säulen nicht, obwohl sie die Sicht
/// versperren"</i>). Every number below is a measurement from the ModBuild-429 hardware log, and
/// each case says which line it was read off.</para>
/// </summary>
internal static class WallFloorTileVectors
{
    /// <summary>The session's floor plane. Every FLOOR CENSUS line of the ModBuild-429 log reads
    /// <c>floorY 0.00</c>.</summary>
    private const float FloorY = 0.00f;

    internal static void Run(Harness t)
    {
        // ---- the photograph: the hexes the doors stand on --------------------------------------
        // 'CR_EXT_Stone_Floor_01_Half_02' and 'CR_TC_Floor_Basic_Half_02' (ModBuild 429 UNION RULE
        // line, both owned by ThickDoor ee3cc9b8- and both riding 'Wall 2' at fade 1.00). Height
        // from the FLOOR CENSUS row for the full tile, 'CR_EXT_Stone_Floor_01' y[-0.3..0.0]; the
        // footprint is a HALF of the ~1.7 wu hex pitch read off the MAPTILE line
        // 'D21 …' bounds s(8.6,0.3,8.0) over about five hex columns.
        t.Case("floor/doorway-half-hex");
        var half = new WallFloorTile.Plate(minY: -0.30f, maxY: 0.00f, spanX: 0.85f, spanZ: 1.70f);
        bool ok = WallFloorTile.IsFloorTile(half, FloorY, out string why);
        t.True(ok, "the half-hex a door stands on is a floor tile and never fades — " + why);
        t.True(why.Contains("FLOOR TILE"), "and the census says which term protected it: " + why);

        // The full hex, same kit: 'CV_Floor_Basic_01' y[-0.4..0.0] (FLOOR CENSUS), hex pitch 1.7.
        t.Case("floor/full-hex");
        var hex = new WallFloorTile.Plate(minY: -0.40f, maxY: 0.00f, spanX: 1.70f, spanZ: 1.50f);
        t.True(WallFloorTile.IsFloorTile(hex, FloorY, out why), "a whole floor hex: " + why);

        // The thin damaged plates the map tile ships, from the same census rows:
        // 'EN_CR_FloorTiles_Damaged_03' y[-0.2..-0.1], 'Simple Tile' y[-0.4..-0.1],
        // 'EN_Unseen_FloorHex_Edge_Damage_03_PR' y[-0.4..-0.1].
        t.Case("floor/thin-damaged-plates");
        t.True(WallFloorTile.IsFloorTile(
                   new WallFloorTile.Plate(-0.20f, -0.10f, 1.70f, 1.50f), FloorY, out why),
               "EN_CR_FloorTiles_Damaged_03: " + why);
        t.True(WallFloorTile.IsFloorTile(
                   new WallFloorTile.Plate(-0.40f, -0.10f, 1.70f, 1.50f), FloorY, out why),
               "Simple Tile / EN_Unseen_FloorHex_Edge_Damage_03_PR: " + why);

        // The tallest row of the family: 'FR_Floor_Detail_Medium_01_Grass' y[-0.2..0.4]. It is the
        // reason FloorBandWU is 0.45 and PlateHeightMaxWU is 0.60 and not tighter.
        t.Case("floor/grass-detail-hex");
        t.True(WallFloorTile.IsFloorTile(
                   new WallFloorTile.Plate(-0.20f, 0.40f, 1.70f, 1.50f), FloorY, out why),
               "the grass floor detail is still a floor tile at 0.6 wu: " + why);

        // ---- what must still fade --------------------------------------------------------------
        // THE PILLAR. säulen.jpg is the report from the other side, so this is the case that
        // decides whether the ruling can ship at all. Both halves of the doorway pillar, from the
        // ModBuild-429 FADE WRITE row for TORN 'HexDoor(Clone)': 'polySurface2' AABB s(1.3,2.0,1.2)
        // anchor 3.08 over floor, 'polySurface1' s(1.1,2.5,1.1) anchor 3.54.
        t.Case("floor/doorway-pillar-still-fades");
        var pillarLower = new WallFloorTile.Plate(3.08f, 5.08f, 1.30f, 1.20f);
        t.True(!WallFloorTile.IsFloorTile(pillarLower, FloorY, out why),
               "a doorway pillar is not a floor tile: " + why);
        t.True(why.Contains("stands"), "…and is refused for standing over the floor band: " + why);

        // A PILLAR'S FOOT, which is the case the height cap exists for: 'EN_CR_Pillar_Large_02 (1)'
        // y[-3.6..-0.1] (FLOOR CENSUS, room 3) — its TOP is under the floor plane, so the band term
        // passes and only the thickness separates it from a plate.
        t.Case("floor/pillar-foot-still-fades");
        var pillarFoot = new WallFloorTile.Plate(-3.60f, -0.10f, 1.20f, 1.10f);
        t.True(!WallFloorTile.IsFloorTile(pillarFoot, FloorY, out why),
               "a pillar whose top sits under the floor plane is still a pillar: " + why);
        t.True(why.Contains("thick"), "…refused on thickness, not on the band: " + why);

        // A LOW WALL: 'TO_Fort_LowWall_01 (1)' AABB s(1.6,1.7,2.2), anchor 3.39 over floor
        // (ModBuild 429 FADE WRITE, the same doorway). Broad in both axes and thin enough in one
        // to look plate-like if only the aspect were checked — it is the height that refuses it.
        t.Case("floor/low-wall-still-fades");
        t.True(!WallFloorTile.IsFloorTile(
                   new WallFloorTile.Plate(3.39f, 5.09f, 1.60f, 2.20f), FloorY, out why),
               "a low wall on a doorway keeps fading: " + why);
        // The same low wall standing ON the floor rather than up on the shell: still refused, and
        // refused TWICE OVER — its top is 1.70 wu over the floor (the band term, which is the one
        // that gets there first) and it is 1.70 wu thick (the height cap). This is the case that
        // must not regress into a protected parapet, and it is the reason the band term is stated
        // first: a wall is refused by a number that does not depend on its footprint at all.
        var lowWallOnFloor = new WallFloorTile.Plate(0.00f, 1.70f, 1.60f, 2.20f);
        t.True(!WallFloorTile.IsFloorTile(lowWallOnFloor, FloorY, out why),
               "…and at floor level too: " + why);
        t.True(WallFloorTile.Judge(lowWallOnFloor, FloorY) == WallFloorTile.Verdict.AboveFloorBand,
               "…on the band term, which is evaluated first");
        // …and the height cap would refuse it on its own, which is what makes the pair a belt and
        // braces rather than one term doing all the work. Same wall, sunk until its top is inside
        // the band: still not a floor tile.
        t.True(WallFloorTile.Judge(new WallFloorTile.Plate(-1.40f, 0.30f, 1.60f, 2.20f), FloorY)
                   == WallFloorTile.Verdict.TooTall,
               "…and the height cap refuses the same wall on its own");

        // A WALL'S BASE TRIM: 'CR_Dungeon_Wall_Base_Metal (1)' y[-0.3..-0.1] (FLOOR CENSUS, room 4)
        // is only 0.2 wu thick and sits under the floor plane — it passes the band, the height and
        // the flatness terms. It runs the LENGTH of its wall, and the measured wall course this
        // repository already pins is spanX 3.40 x spanZ 0.62 (WallStandingPropVectors). The aspect
        // term is what refuses it, and the shader term in the driver refuses it a second time
        // (that row reads sh='Amp_Basic_WallFade' while every floor row reads Amp_Basic_N_MRAO /
        // Amp_Basic_Unseen / Amp_Basic_Foliage).
        t.Case("floor/wall-base-trim-still-fades");
        var baseTrim = new WallFloorTile.Plate(-0.30f, -0.10f, 3.40f, 0.62f);
        t.True(!WallFloorTile.IsFloorTile(baseTrim, FloorY, out why),
               "a wall's base trim is a strip, not a tile: " + why);
        t.True(why.Contains("aspect"), "…refused on aspect: " + why);
        t.True(WallFloorTile.Judge(baseTrim, FloorY) == WallFloorTile.Verdict.Strip,
               "…and the verdict names the term");

        // A RAISED PLATFORM EDGE / A STEP'S UPPER LANDING. A plate of exactly floor-tile shape,
        // lifted one step up. This is the term that keeps the ruling from protecting anything that
        // stands proud of the floor and therefore CAN occlude.
        t.Case("floor/raised-platform-still-fades");
        t.True(!WallFloorTile.IsFloorTile(
                   new WallFloorTile.Plate(0.50f, 0.80f, 1.70f, 1.50f), FloorY, out why),
               "a plate lifted clear of the room floor is not a floor tile: " + why);
        t.True(WallFloorTile.Judge(new WallFloorTile.Plate(0.50f, 0.80f, 1.70f, 1.50f), FloorY)
                   == WallFloorTile.Verdict.AboveFloorBand,
               "…and it is the band term that says so, at any footprint");
        // The boundary is the band and nothing else: the SAME plate resting at the floor plane is
        // a floor tile. Stated as a pair so a future edit cannot move one without moving the other.
        t.True(WallFloorTile.IsFloorTile(
                   new WallFloorTile.Plate(0.10f, 0.40f, 1.70f, 1.50f), FloorY, out why),
               "…while the same plate at the floor plane is one: " + why);

        // A TERRACED ROOM. floorY is the NEAREST ANCHORED plane, never the lowest in the scene —
        // the same rule NearestAnchoredFloorY exists for. A hex on an upper terrace is a floor
        // tile against ITS floor and a raised platform against the one below.
        t.Case("floor/terraced-room");
        var upperHex = new WallFloorTile.Plate(2.60f, 3.00f, 1.70f, 1.50f);
        t.True(WallFloorTile.IsFloorTile(upperHex, 3.00f, out why),
               "a hex on the upper terrace, judged against the upper floor: " + why);
        t.True(!WallFloorTile.IsFloorTile(upperHex, FloorY, out why),
               "…and the very same hex judged against the floor below is not: " + why);

        // ---- the anti-catastrophe caps ---------------------------------------------------------
        // A ROOM IS NOT A TILE. The report's rooms measure 6.7 x 6.9 wu and up (FLOOR CENSUS
        // 'Room_1' … 'Room_5'); a room-sized flat union reaching this rule would turn a whole
        // tileset permanently solid, which is the same catastrophe WallStandingProp.MaxSpanWU
        // guards against and it is guarded here with the same number.
        t.Case("floor/room-sized-refused");
        t.True(!WallFloorTile.IsFloorTile(
                   new WallFloorTile.Plate(-0.30f, 0.00f, 6.70f, 6.90f), FloorY, out why),
               "a room-sized flat union is a room, not a tile: " + why);
        t.True(why.Contains("room, not a tile"), "and the line says so: " + why);

        // SCATTER IS NOT A TILE. 'CV_Floor_Scatter_06' foot -0.14 / top 0.62 and
        // 'CV_Floor_Scatter_04' foot -0.17 / top 0.57 (ModBuild 429 WALL-MOUNTED DRESSING,
        // ground-band refusals) are floor COVER, handled by the existing ground band, and this
        // rule deliberately does not widen to them: a rule that protects everything protects
        // nothing.
        t.Case("floor/scatter-not-a-tile");
        t.True(!WallFloorTile.IsFloorTile(
                   new WallFloorTile.Plate(-0.14f, 0.30f, 0.35f, 0.40f), FloorY, out why),
               "floor scatter is under the tile floor and is not claimed here: " + why);
        t.True(why.Contains("short axis"), "…refused on the short axis: " + why);

        // A degenerate AABB (a zero-width plane, a renderer with no mesh) must never read as a
        // tile — the aspect guard's divide-by-zero path.
        t.Case("floor/degenerate-aabb");
        t.True(!WallFloorTile.IsFloorTile(
                   new WallFloorTile.Plate(0.00f, 0.00f, 0.00f, 0.00f), FloorY, out why),
               "a zero-extent AABB is not a floor tile: " + why);

        // ---- the constants are the shipped ones -------------------------------------------------
        // The six shape constants are the whole rule. Pinned so a "tidy the numbers" pass has to
        // come through this file and read why each one is what it is.
        t.Case("floor/constants");
        t.True(WallFloorTile.FloorBandWU == 0.45f, "FloorBandWU 0.45 wu");
        t.True(WallFloorTile.PlateHeightMaxWU == 0.60f, "PlateHeightMaxWU 0.60 wu");
        t.True(WallFloorTile.PlateMinSpanWU == 0.60f, "PlateMinSpanWU 0.60 wu");
        t.True(WallFloorTile.PlateAspectMax == 3.0f, "PlateAspectMax 3.0");
        t.True(WallFloorTile.PlateFlatnessMax == 0.50f, "PlateFlatnessMax 0.50");
        t.True(WallFloorTile.PlateMaxSpanWU == WallStandingProp.MaxSpanWU,
               "the room-sized cap is the SAME number as the standing-prop rule's, and must stay "
               + "the same number — they guard against the same catastrophe");
    }
}
