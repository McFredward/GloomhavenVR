namespace GloomhavenVR.Core;

/// <summary>
/// A FLOOR TILE CAN NEVER OCCLUDE ANYTHING AND MUST THEREFORE NEVER BE FADED.
///
/// <para><b>THE RULING</b> (user, 2026-09-05, <c>.planning/debug/fehlende_boden_tiles.jpg</c>):
/// <i>"Wie man in fehlende_boden_tiles.jpg sehen kann, verschwinden die Tiles auf dem die Türen
/// stehen. Bei näherer Betrachtung habe ich rausgefunden, dass sie zusammen mit benachbarten
/// Wänden gefaded werden und dadurch verschwinden! Wenn ich in den 'inside' Modus gehe sind alle
/// wieder da. Wichtige Regel: Boden-tiles können per definition NIEMALS die Sicht auf irgendetwas
/// verdecken und dürfen daher niemals ausgeblendet werden."</i> This ranks with the project's
/// other standing "never fades" rulings — light shafts and non-occluders, arches and doors, the
/// fountain — and like them it is enforced at the WRITE, not on the lane that happened to be
/// caught.</para>
///
/// <para><b>WHY THIS FILE HAS NO UNITY IN IT.</b> Same reason
/// <see cref="WallStandingProp"/> has none: the rule fails in both directions in silence. Too
/// tight and the doorway hexes keep vanishing (the photograph). Too loose and a pillar's foot, a
/// step or a low wall goes permanently solid — which does not read as a bug at all, it reads as
/// wall see-through having been switched off, and the user has already reported the opposite
/// defect once (<c>säulen.jpg</c>, <i>"faden die Säulen nicht, obwohl sie die Sicht
/// versperren"</i>). Both verdicts are seen only by eye, from inside a headset. So the arithmetic
/// is floats and nothing else, and it is pinned by golden vectors in
/// <c>tests/GloomhavenVR.WireTests/WallFloorTileVectors.cs</c>.</para>
///
/// <para><b>WHY GEOMETRY AND NOT THE GAME'S OWN ANSWER.</b> The game's occlusion model was read
/// first and it does NOT separate floor from occluder.
/// <c>decompiled/GH.Runtime/TilesOcclusionVolume.cs</c> holds a <c>CentralTile</c> and a
/// <c>MeshRenderer[] Renderers</c>, and <c>TilesOcclusionGenerator</c> draws those into the
/// <c>_TilesOcclusionMap</c> — but they are INVISIBLE proxy boxes spanning the whole room column,
/// not floor plates: the ModBuild-429 FLOOR CENSUS names them
/// <c>'Volume_1' y[-1.0..9.0] sh='Standard' q2000 OFF</c>. <c>ObjectOcclusionVolume</c> marks
/// props, <c>TileBehaviour</c> is the click/tooltip surface of a hex and is carried by walls and
/// doors alike, and <c>ProceduralMapTile</c> knows only "Walls", "Doors" and "Generated Content".
/// Nothing in the game names the floor. So the test is geometry, derived below from measurements
/// printed in this repository's own hardware log, plus ONE game-owned exclusion that is real
/// identity rather than naming: a mesh the game put on its OWN wall-fade shader family is a mesh
/// the game itself calls wall.</para>
///
/// <para><b>ALL SIX SHAPE CONSTANTS COME FROM ModBuild 429's LOG</b>
/// (<c>.planning/debug/LogOutput.log</c>), and each one names its rows at its declaration. The
/// short version: every floor plate the FLOOR CENSUS prints is 0.1–0.4 wu thick with its top at or
/// under the room floor plane, the one exception being a grass detail at y[-0.2..0.4]; the
/// doorway pieces that MUST keep fading, printed in the same FADE WRITE row, are 1.7 wu and 2.0 /
/// 2.5 wu tall.</para>
/// </summary>
internal static class WallFloorTile
{
    /// <summary>How far a floor tile's AABB TOP may stand over its room's floor plane.
    ///
    /// <para>ModBuild 429 FLOOR CENSUS, over <c>floorY 0.00</c>: <c>'CV_Floor_Basic_01'
    /// y[-0.4..0.0]</c>, <c>'CR_EXT_Stone_Floor_01' y[-0.3..0.0]</c>, <c>'Simple Tile'
    /// y[-0.4..-0.1]</c>, <c>'EN_Unseen_FloorHex_Edge_Damage_03_PR' y[-0.4..-0.1]</c>,
    /// <c>'EN_CR_FloorTiles_Damaged_03' y[-0.2..-0.1]</c>, <c>'TO_INT_Floor_Clutter_Pages_03'
    /// y[-0.1..0.0]</c>. The highest-topped row of the family is
    /// <c>'FR_Floor_Detail_Medium_01_Grass' y[-0.2..0.4]</c>. 0.45 clears that and stays well
    /// under the subsystem's existing ground band (<c>GroundExclusionHeightWU = 1.0</c>), which is
    /// deliberate: this rule must be NARROWER than the ground strip, not a second copy of it.</para>
    ///
    /// <para>THIS IS ALSO THE TERM THAT REFUSES A RAISED PLATFORM EDGE. A platform whose top
    /// stands over the room floor by more than 0.45 wu is not at the floor plane and is not
    /// covered here, whatever its footprint.</para></summary>
    internal const float FloorBandWU = 0.45f;

    /// <summary>How thick a floor tile may be. Same rows: the stone plates measure 0.1–0.4 wu,
    /// the grass detail 0.6. The doorway assembly that must keep fading measures, in the ModBuild
    /// 429 FADE WRITE row for <c>TORN 'HexDoor(Clone)' 6/22</c>: <c>'TO_Fort_LowWall_01 (1)'
    /// AABB s(1.6,1.7,2.2)</c> and the two pillar pieces <c>'polySurface2' s(1.3,2.0,1.2)</c> /
    /// <c>'polySurface1' s(1.1,2.5,1.1)</c> — 1.7, 2.0 and 2.5 wu tall. This is the term that
    /// refuses a PILLAR'S FOOT: <c>'EN_CR_Pillar_Large_02 (1)' y[-3.6..-0.1]</c> is 3.5 wu of
    /// pillar and fails here even though its top sits under the floor plane.</summary>
    internal const float PlateHeightMaxWU = 0.60f;

    /// <summary>How narrow a floor tile's SHORT horizontal axis may be. The hex pitch is read off
    /// the ModBuild 429 MAPTILE line <c>'D21 : (…)' child[0] 'Preview' … renderers 57 … bounds
    /// c(-0.9,-0.2,-1.5) s(8.6,0.3,8.0)</c> — 8.6 wu across about five hex columns, so ~1.7 wu per
    /// hex. The pieces the photograph is about are HALVES (<c>'CR_EXT_Stone_Floor_01_Half_02'</c>,
    /// <c>'CR_TC_Floor_Basic_Half_02'</c>, ModBuild 429 UNION RULE line), i.e. roughly 0.85 × 1.7.
    /// 0.60 leaves margin under the smallest half-hex while still excluding scatter and
    /// clutter, which is neither a tile nor load-bearing for this ruling.</summary>
    internal const float PlateMinSpanWU = 0.60f;

    /// <summary>How elongated a floor tile may be — the term that separates a PLATE from a wall's
    /// BASE TRIM. A hex is 1.7 × 1.5 (1.13) and a half-hex 1.7 × 0.85 (2.0); the measured wall
    /// course this repository's own wire vectors pin is spanX 3.40 × spanZ 0.62, i.e. 5.5. A strip
    /// that runs the length of a wall is that wall's foundation, and the flat game keeps its own
    /// foundation band solid — but it is not a tile and this rule does not claim it.</summary>
    internal const float PlateAspectMax = 3.0f;

    /// <summary>How thick a floor tile may be RELATIVE to its short span — the plate test proper.
    /// A half-hex is 0.3 over 0.85 (0.35); a low wall is 1.7 over 1.6 (1.06). Held at one half so
    /// the verdict does not turn on the absolute cap alone at small footprints.</summary>
    internal const float PlateFlatnessMax = 0.50f;

    /// <summary>Anti-catastrophe cap, the same number and the same reason as
    /// <see cref="WallStandingProp.MaxSpanWU"/>: a room-sized flat thing is a ROOM (the report's
    /// rooms measure 6.7 × 6.9 wu and up), and protecting one would turn a whole tileset solid.
    /// Nothing in this file may ever raise it without that argument being answered.</summary>
    internal const float PlateMaxSpanWU = 6.0f;

    /// <summary>Which term decided. Exactly one of these is the answer for any plate, and the
    /// caller prints it — a floor tile that ever fades again must name itself in the log.</summary>
    internal enum Verdict
    {
        /// <summary>A floor tile. Never fades, on any path.</summary>
        FloorTile,
        /// <summary>Its top stands over the room floor plane by more than
        /// <see cref="FloorBandWU"/> — a raised platform, a step's upper landing, a wall.</summary>
        AboveFloorBand,
        /// <summary>Thicker than <see cref="PlateHeightMaxWU"/> — a pillar, a low wall, a
        /// pillar's foot, a course of masonry.</summary>
        TooTall,
        /// <summary>Its short horizontal axis is under <see cref="PlateMinSpanWU"/> — scatter,
        /// clutter, a bone, a plant.</summary>
        TooNarrow,
        /// <summary>Wider than <see cref="PlateMaxSpanWU"/> — a room, not a tile.</summary>
        RoomSized,
        /// <summary>Longer than <see cref="PlateAspectMax"/> times its own short axis — a wall's
        /// base trim or a kerb, not a plate.</summary>
        Strip,
        /// <summary>Thicker than <see cref="PlateFlatnessMax"/> of its own short axis — a block,
        /// not a plate.</summary>
        NotFlat,
        /// <summary>The GAME put this mesh on its own wall-fade shader family, so the game itself
        /// calls it wall. Decided by the caller, which is the half that needs Unity; it is a
        /// member of this enum so the log speaks one vocabulary.</summary>
        GameCallsItWall,
    }

    /// <summary>One renderer's world AABB, reduced to the four numbers the rule turns on.</summary>
    internal readonly struct Plate
    {
        internal Plate(float minY, float maxY, float spanX, float spanZ)
        {
            MinY = minY;
            MaxY = maxY;
            SpanX = spanX;
            SpanZ = spanZ;
        }

        internal float MinY { get; }
        internal float MaxY { get; }
        internal float SpanX { get; }
        internal float SpanZ { get; }

        internal float Height => MaxY - MinY;
        internal float MinSpan => SpanX < SpanZ ? SpanX : SpanZ;
        internal float MaxSpan => SpanX > SpanZ ? SpanX : SpanZ;
        /// <summary>Guarded so a degenerate (zero-width) AABB reads as maximally elongated
        /// rather than dividing by zero — a zero-span renderer is not a tile.</summary>
        internal float Aspect => MinSpan > 0.0001f ? MaxSpan / MinSpan : float.MaxValue;
    }

    /// <summary>
    /// WHICH TERM DECIDES — the verdict without its sentence.
    ///
    /// <para>Split from <see cref="Describe"/> for the same reason
    /// <see cref="WallStandingProp.Judge"/> is: this runs on the per-frame write path over
    /// hundreds of renderers, and on net472 every <c>$"…"</c> is a
    /// <c>string.Format(string, object[])</c> — an array allocation plus a box per float. The
    /// sentence is built only for the rows a capped census will actually print.</para>
    ///
    /// <para>The order of the terms is the order of the enum and it is not arbitrary: the two
    /// ABSOLUTE caps come first so a pillar and a wall are refused by a number that does not
    /// depend on their footprint, and the two RELATIVE terms come last so a plate is judged
    /// against its own size.</para>
    /// </summary>
    /// <param name="p">The renderer's world AABB.</param>
    /// <param name="floorY">The nearest ANCHORED room floor plane — never the lowest floor in
    /// the scene, because rooms can be terraced. See <c>NearestAnchoredFloorY</c>.</param>
    internal static Verdict Judge(in Plate p, float floorY)
    {
        if (p.MaxY - floorY > FloorBandWU)
            return Verdict.AboveFloorBand;
        if (p.Height > PlateHeightMaxWU)
            return Verdict.TooTall;
        if (p.MinSpan < PlateMinSpanWU)
            return Verdict.TooNarrow;
        if (p.MaxSpan > PlateMaxSpanWU)
            return Verdict.RoomSized;
        if (p.Aspect > PlateAspectMax)
            return Verdict.Strip;
        if (p.Height > PlateFlatnessMax * p.MinSpan)
            return Verdict.NotFlat;
        return Verdict.FloorTile;
    }

    /// <summary>Is this a floor tile, and by which measured term. The bool and the sentence are
    /// the same decision — <see cref="Judge"/> makes it once.</summary>
    internal static bool IsFloorTile(in Plate p, float floorY, out string term)
    {
        Verdict v = Judge(p, floorY);
        term = Describe(p, floorY, v);
        return v == Verdict.FloorTile;
    }

    /// <summary>The measured reason, printed verbatim by the FLOOR NEVER FADES census — for a
    /// REFUSAL as well as for a verdict, because "the rule did not fire" without "and by which
    /// number" is what has cost this project rounds before.</summary>
    internal static string Describe(in Plate p, float floorY, Verdict v)
    {
        string shape = $"top {p.MaxY - floorY:0.00} / h {p.Height:0.00} / span "
                       + $"{p.MinSpan:0.00}x{p.MaxSpan:0.00} wu over floor {floorY:0.00}";
        return v switch
        {
            Verdict.FloorTile =>
                shape + $" — FLOOR TILE: flat at the floor plane (top ≤ {FloorBandWU:0.00}, "
                      + $"h ≤ {PlateHeightMaxWU:0.00}, h ≤ {PlateFlatnessMax:0.00}×short span, "
                      + $"aspect ≤ {PlateAspectMax:0.0}), and a floor tile occludes nothing",
            Verdict.AboveFloorBand =>
                shape + $" — NOT FLOOR: its top stands {p.MaxY - floorY:0.00} wu over the room "
                      + $"floor, past the {FloorBandWU:0.00} wu band (raised platform / step "
                      + "landing / wall)",
            Verdict.TooTall =>
                shape + $" — NOT FLOOR: {p.Height:0.00} wu thick, past the {PlateHeightMaxWU:0.00} "
                      + "wu cap (pillar, pillar foot, low wall, course of masonry)",
            Verdict.TooNarrow =>
                shape + $" — NOT FLOOR: short axis {p.MinSpan:0.00} wu, under the "
                      + $"{PlateMinSpanWU:0.00} wu tile floor (scatter, clutter, plant)",
            Verdict.RoomSized =>
                shape + $" — NOT FLOOR: {p.MaxSpan:0.00} wu across, past the {PlateMaxSpanWU:0.0} "
                      + "wu cap — that is a room, not a tile",
            Verdict.Strip =>
                shape + $" — NOT FLOOR: aspect {p.Aspect:0.0}, past {PlateAspectMax:0.0} — a strip "
                      + "running the length of something (wall base trim, kerb), not a plate",
            Verdict.NotFlat =>
                shape + $" — NOT FLOOR: {p.Height:0.00} wu thick over a {p.MinSpan:0.00} wu short "
                      + $"axis, past {PlateFlatnessMax:0.00}× — a block, not a plate",
            Verdict.GameCallsItWall =>
                shape + " — NOT FLOOR: the GAME put this mesh on its own wall-fade shader family, "
                      + "so the game itself calls it wall (ModBuild 429 FLOOR CENSUS: "
                      + "'CR_Dungeon_Wall_Base_Metal' sh='Amp_Basic_WallFade', while every floor "
                      + "row reads Amp_Basic_N_MRAO / Amp_Basic_Unseen / Amp_Basic_Foliage)",
            _ => shape + " — NOT FLOOR",
        };
    }
}
