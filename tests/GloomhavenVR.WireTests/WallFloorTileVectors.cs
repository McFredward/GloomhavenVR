using System;
using System.Diagnostics;
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

        // ---- "never fades" must mean "always visible", not merely "never written" ---------------
        //
        // THE SECOND PHOTOGRAPH is .planning/debug/boden_tiles_ausgeblendet.jpg (user, 2026-09-05:
        // "Die Bodentiles faden nicht mehr mit den Wänden sehr gut. Allerdings sind jetzt zwei
        // Boden tiles unter den ersten Türen dauerhaft ausgeblendet"). ModBuild 430 made all four
        // write primitives REFUSE a floor renderer but restituted ONCE PER RENDERER PER SCENE and
        // restored unconditionally, so the one shot was spent on the first commit that saw the
        // tile — healthy or not — and a tile faded afterwards had nothing left to rescue it.
        //
        // The ModBuild-430 log names the two tiles and the channel on ONE line:
        //   SHOW EDGE: 61 piece(s) became visible again … 2 of them NOT as authored …
        //     'CR_EXT_Stone_Floor_01_Half_02' [cutoff] at fade 0.41: its prop unit 'HexDoor(Clone)'
        //     returned IN PIECES … ; 'CR_TC_Floor_Basic_Half_02' [cutoff] at fade 0.41: …
        // while the FLOOR NEVER FADES line of that same window reads "REFUSED 19 … HANDED BACK to
        // solid 0" and names both of them as protected floor tiles. Refused AND invisible.
        //
        // So the rescue latch is now the PICTURE, and this is the arithmetic behind it.

        // THE DEFECT ITSELF. A door half stuck in the cutoff channel: DriveProp ramps
        // Lerp(BaseCutoff, FoliageCutoffEnd = 1.2, fade), so an authored 0.50 at fade 0.41 reads
        // back 0.79 off the block. That must be a rescue.
        t.Case("floor/audit-the-two-tiles-under-the-door");
        var stuck = new WallFloorTile.Appearance(
            heldDisabled: false, wearingSwapCopies: false, hasBlock: true, nativeRamp: false,
            authoredAlpha: float.NaN, blockAlpha: float.NaN,
            authoredCutoff: 0.50f, blockCutoff: 0.787f,
            authoredDissolve: float.NaN, blockDissolve: float.NaN);
        t.True(WallFloorTile.ChannelOf(stuck) == WallFloorTile.Channel.Cutoff,
               "CR_EXT_Stone_Floor_01_Half_02 / CR_TC_Floor_Basic_Half_02 at [cutoff] fade 0.41 "
               + "are not as authored and must be rescued");
        t.True(WallFloorTile.DescribeChannel(stuck, WallFloorTile.Channel.Cutoff).Contains("0.79"),
               "and the census line carries the NUMBER that decided it, not just the channel: "
               + WallFloorTile.DescribeChannel(stuck, WallFloorTile.Channel.Cutoff));

        // THE CHURN TRAP, and the reason the audit reads VALUES and never block PRESENCE.
        // DriveProp(p, 0f) is the write that puts a piece back to authored, and it leaves a
        // property block behind carrying the authored numbers. If "has a block" were the test,
        // every solid floor tile would be rescued on every commit — ~30 RestoreProp calls a
        // minute into the ownership-churn tripwire, which is exactly the failure the ModBuild-430
        // one-shot existed to prevent and which this round may not re-create.
        t.Case("floor/audit-authored-block-is-not-a-fade");
        var authoredBlock = new WallFloorTile.Appearance(
            false, false, hasBlock: true, nativeRamp: false,
            authoredAlpha: 1.00f, blockAlpha: 1.00f,
            authoredCutoff: 0.50f, blockCutoff: 0.50f,
            authoredDissolve: 0.00f, blockDissolve: 0.00f);
        t.True(WallFloorTile.ChannelOf(authoredBlock) == WallFloorTile.Channel.AsAuthored,
               "a block carrying the AUTHORED values is authored — DriveProp(p, 0f) writes one, "
               + "and rescuing it every commit would be the churn the one-shot was guarding");

        // No block at all: drawn with the material's own values, nothing to do.
        t.Case("floor/audit-no-block-is-authored");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   false, false, hasBlock: false, nativeRamp: false,
                   float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN))
               == WallFloorTile.Channel.AsAuthored,
               "a floor renderer with no property block is drawing as authored");

        // AN ABSENT CHANNEL IS NOT A CHANNEL AT ZERO. GetFloat on an id a block never set returns
        // 0, and 0 is a plausible fade value — so a piece with a colour channel and NO cutoff
        // channel must not read as "cutoff 0.00 against authored 0.00" or, worse, as faded.
        t.Case("floor/audit-absent-channel-never-fires");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   false, false, hasBlock: true, nativeRamp: false,
                   authoredAlpha: 1.00f, blockAlpha: 1.00f,
                   authoredCutoff: float.NaN, blockCutoff: float.NaN,
                   authoredDissolve: float.NaN, blockDissolve: float.NaN))
               == WallFloorTile.Channel.AsAuthored,
               "NaN on both sides of a channel the piece does not have can never read as faded");

        // THE ORDER OF THE TERMS. A renderer we hold disabled is invisible whatever its block
        // says, and a renderer wearing our swap copies is not as authored whatever its numbers
        // say — both outrank the block, and disabled outranks the swap.
        t.Case("floor/audit-outright-hides-come-first");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   heldDisabled: true, wearingSwapCopies: true, hasBlock: true, nativeRamp: false,
                   1f, 1f, 0.5f, 0.5f, 0f, 0f)) == WallFloorTile.Channel.Enabled,
               "held disabled outranks everything: the block is irrelevant to a renderer that "
               + "is not drawn at all");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   heldDisabled: false, wearingSwapCopies: true, hasBlock: false, nativeRamp: false,
                   1f, 1f, 0.5f, 0.5f, 0f, 0f)) == WallFloorTile.Channel.SwapCopies,
               "a piece wearing our dissolve-swap copies is not as authored even with a clean "
               + "block — it is one property block away from gone");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   heldDisabled: false, wearingSwapCopies: false, hasBlock: true, nativeRamp: true,
                   float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN))
               == WallFloorTile.Channel.NativeRamp,
               "a native ramp's block IS the fade (_ToggleWallFade + occlusion map), so there is "
               + "nothing to compare and nothing to keep");

        // THE THREE BLOCK CHANNELS, each in the direction its ramp actually writes.
        t.Case("floor/audit-block-channels-and-their-directions");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   false, false, true, false,
                   authoredAlpha: 1.00f, blockAlpha: 0.30f,
                   authoredCutoff: float.NaN, blockCutoff: float.NaN,
                   authoredDissolve: float.NaN, blockDissolve: float.NaN))
               == WallFloorTile.Channel.ColourAlpha,
               "colour alpha goes DOWN with fade (c.a *= 1 - fade)");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   false, false, true, false,
                   float.NaN, float.NaN, 0.35f, 1.20f, float.NaN, float.NaN))
               == WallFloorTile.Channel.Cutoff,
               "the clip value goes UP with fade (Lerp(BaseCutoff, 1.2, fade))");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   false, false, true, false,
                   float.NaN, float.NaN, float.NaN, float.NaN, 0.20f, 1.00f))
               == WallFloorTile.Channel.DissolveControl,
               "the Amp dissolve control sweeps toward 1 (Lerp(BaseDissolveControl, 1, fade))");
        // …and either way, because BaseDissolveControl is read off the material and a piece whose
        // authored control is above the ramp's end would move DOWN.
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   false, false, true, false,
                   float.NaN, float.NaN, float.NaN, float.NaN, 0.80f, 0.20f))
               == WallFloorTile.Channel.DissolveControl,
               "the dissolve term is symmetric — it is a difference, not a threshold");

        // A FLOOR TILE MADE BRIGHTER IS NOT A FADE. The rescue exists to make an invisible tile
        // visible, and only that; a block that pushes alpha UP or the clip value DOWN is
        // somebody else's business and must not start a write.
        t.Case("floor/audit-only-the-fading-direction-counts");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   false, false, true, false,
                   authoredAlpha: 0.50f, blockAlpha: 1.00f,
                   authoredCutoff: 0.60f, blockCutoff: 0.10f,
                   authoredDissolve: float.NaN, blockDissolve: float.NaN))
               == WallFloorTile.Channel.AsAuthored,
               "more alpha and less clipping than authored is not a piece being hidden");

        // THE TOLERANCE. A float that has been through a property block round-trip must not read
        // as faded, or a solid tile starts a rescue every commit — churn again, by rounding.
        t.Case("floor/audit-epsilon");
        t.True(WallFloorTile.ChannelOf(new WallFloorTile.Appearance(
                   false, false, true, false,
                   1.000f, 0.995f, 0.500f, 0.505f, 0.000f, 0.005f))
               == WallFloorTile.Channel.AsAuthored,
               "half a hundredth off in every channel is a round-trip, not a fade");
        t.True(WallFloorTile.AuthoredEpsilon == 0.01f, "AuthoredEpsilon 0.01");

        // ---- COST, MEASURED — the arithmetic half -----------------------------------------------
        // The standing rule here is that a claimed performance property is measured, not asserted.
        // The audit's cost splits in two and only one half can be measured off a headset:
        //
        //   * THE ARITHMETIC (this case). ChannelOf over a struct of ten fields, on the population
        //     the ModBuild-430 log actually reports — its FLOOR NEVER FADES lines name at most 21
        //     distinct floor renderers in a window, so 32 is already generous.
        //   * THE UNITY READS (not here). One HashSet lookup, one `renderer.enabled`, one
        //     `HasPropertyBlock()` per floor renderer, plus a `GetPropertyBlock` only for a piece
        //     that HAS a block and a channel of ours to compare it against. Those are engine calls
        //     with no CI stand-in, so the shipped instrument times the whole sweep with
        //     Stopwatch.GetTimestamp and prints "the last sweep took N µs and the worst of the
        //     session M µs" on the FLOOR NEVER FADES line. That number is the hardware answer;
        //     this one is its floor.
        //
        // The sweep runs ONCE PER COMMIT (~2 s) inside the existing CommitPhase.Figures bucket,
        // never on the per-frame write path.
        t.Case("floor/audit-cost-at-logged-population");
        const int Population = 32;
        var pop = new WallFloorTile.Appearance[Population];
        for (int i = 0; i < Population; i++)
        {
            // The common case by a wide margin, and the one the cost has to be cheap in: a floor
            // tile that is perfectly fine, carrying the authored block DriveProp(p, 0f) leaves.
            pop[i] = new WallFloorTile.Appearance(
                false, false, hasBlock: true, nativeRamp: false,
                1.00f, 1.00f, 0.50f, 0.50f, 0.00f, 0.00f);
        }
        double perSweepUs = TimeAudit(pop);
        Console.WriteLine(
            $"      floor audit cost: {perSweepUs:F2} µs of arithmetic per sweep over "
            + $"{Population} floor renderer(s) — the widest population the ModBuild-430 log "
            + "reports is 21 in a window. One sweep per commit (~2 s), inside the existing "
            + "CommitPhase.Figures bucket. THIS IS A DESKTOP CI BOX AND ONLY THE ARITHMETIC: the "
            + "Unity reads beside it are timed on hardware by the µs field appended to the FLOOR "
            + "NEVER FADES line.");
        t.True(perSweepUs < 100.0,
               $"the audit's arithmetic must be nowhere near a frame (measured {perSweepUs:F2} µs "
               + $"per sweep over {Population} renderers)");
        // …and the timing is not measuring an early-out: the not-as-authored path is the longer
        // one and must be timed too, or "fast because it stops looking" would pass.
        for (int i = 0; i < Population; i++)
        {
            pop[i] = new WallFloorTile.Appearance(
                false, false, true, false, 1.00f, 1.00f, 0.50f, 0.787f, 0.00f, 0.00f);
        }
        t.True(WallFloorTile.ChannelOf(pop[0]) == WallFloorTile.Channel.Cutoff,
               "the cost case's second population really is the stuck one");
        t.True(TimeAudit(pop) < 100.0, "…and judging it costs the same order");

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

    /// <summary>Microseconds of ChannelOf arithmetic per sweep over one population. Reps chosen so
    /// the loop dominates the clock's resolution rather than the other way round.</summary>
    private static double TimeAudit(WallFloorTile.Appearance[] pop)
    {
        const int Reps = 2000;
        // Warm: the first call through a struct-taking static is JIT, not cost.
        for (int i = 0; i < pop.Length; i++)
            WallFloorTile.ChannelOf(pop[i]);
        var sw = Stopwatch.StartNew();
        int sink = 0;
        for (int r = 0; r < Reps; r++)
        {
            for (int i = 0; i < pop.Length; i++)
                sink += (int)WallFloorTile.ChannelOf(pop[i]);
        }
        sw.Stop();
        // Read the sink so the loop cannot be optimised away entirely.
        if (sink == int.MinValue)
            Console.WriteLine("unreachable");
        return sw.Elapsed.TotalMilliseconds * 1000.0 / Reps;
    }
}
