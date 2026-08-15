// GloomhavenVR companion project — ambient-environment preview renderer.
//
// Batch: Unity -batchmode -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.EnvironmentsPreview.RenderAll -logFile env-preview.log
//   IMPORTANT: run WITHOUT -nographics (rendering needs a graphics device); on a
//   headless box wrap in `xvfb-run -a`.
//
// For each Env_*.prefab (FX-only shells): loads it into an empty temp scene,
// fast-forwards every particle system 6 s (so fog banks, fireflies and — with
// luck — a shooting star are populated), then renders the Views table below.
// Most of it hangs off a fixed 1.4 m station; the HEAD SET at the end of the
// table is shot from where the player's eye really is relative to the floating
// board (2.02/1.66 m in the cellar, 2.80/2.30 m in the wood — see THE PLAYER'S
// HEAD). 1280x720 PNGs go to $ENV_PREVIEW_OUT (or ./env-previews when unset).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloomhavenVR
{
    public static class EnvironmentsPreview
    {
        private const string Root = "Assets/Bundle/Environments";
        private const int W = 1280, H = 720;

        // NOTE: declared BEFORE Views — C# initialises static fields in textual
        // order, so a Views table that referenced Eye from above would capture
        // (0,0,0) and render every view from inside the floor.
        //
        // ---- WHAT THIS CONSTANT IS, AND WHAT IT IS NOT (ModBuild 148) --------
        // 1.4 m is a FIXED STATION, not the player's head. It predates the
        // board-anchored placement, and against the board's own plane it reads
        // very differently in the two rooms:
        //   cellar  board underside 1.08 m authored — 1.4 is 0.32 m ABOVE it
        //   wood    board underside 1.50 m authored — 1.4 is 0.10 m BELOW it,
        //           i.e. every forest frame in the review set is shot from under
        //           the table, and no judgement about what the board looks like
        //           from the player's seat can be made from one.
        // It is NOT moved, and that is deliberate: fifty views hang off it and
        // moving it would silently invalidate every frame previous lanes have
        // already reasoned from. What is added instead is the head heights the
        // player really has (HeadY below) and a small set of views shot from
        // them — see THE HEAD SET at the end of the Views table.
        private static readonly Vector3 Eye = new Vector3(0f, 1.4f, 0f); // fixed 1.4 m station

        // ---- THE PLAYER'S HEAD, DERIVED FROM THE BOARD ----------------------
        // src/GloomhavenVR/Core/SkyAlternative.cs anchors the room to the board:
        //     roomScale = (PlaySpaceToBoardRatio * boardWorldExtent) / authoredPlayExtent
        //     floorY    = boardUndersideY - FloatGapToBoardRatio * boardWorldExtent
        // so in AUTHORED metres the board's underside is always
        //     FloatGapToBoardRatio * playDia / PlaySpaceToBoardRatio
        // (1.50 m in the wood, 1.08 m in the cellar — the same expression
        // EnvRoomBuilder.BoardUndersideY evaluates), and one authored metre is
        // (playDia / PlaySpaceToBoardRatio) / boardWidth of a board width.
        //
        // The head follows from the SAME ratio and is therefore zoom-invariant
        // like everything else in this placement: if the player's eye is `u`
        // BOARD WIDTHS above the board's underside, then in authored metres
        //     eyeY = (FloatGapToBoardRatio + u) * playDia / PlaySpaceToBoardRatio.
        // Nothing here depends on how big the board happens to be measured, which
        // is the property that makes it safe to write down as a constant at all.
        //
        // u IS AN ASSUMPTION AND IS WRITTEN AS ONE. A board floating like a
        // tabletop puts its underside somewhere around 0.75-0.95 m over the real
        // floor and is 1.0-1.4 m across, against a 1.55-1.70 m standing eye and a
        // 1.15-1.30 m seated one — i.e. u lands in 0.43..0.95 standing and around
        // 0.4 seated. The middles of those are what is used; change these two
        // numbers and every head view moves together, which is the point of
        // spending a function on it.
        private const float PlaySpaceToBoardRatio = 4.5f;   // SkyAlternative.cs
        private const float FloatGapToBoardRatio = 0.75f;   // SkyAlternative.cs
        private const float StandOverBoard = 0.65f;         // board widths, standing
        private const float SeatOverBoard = 0.40f;          // board widths, seated
        private static float HeadY(float playDia, float overBoard) =>
            (FloatGapToBoardRatio + overBoard) * (playDia / PlaySpaceToBoardRatio);
        private static readonly float HeadCellar =
            HeadY(EnvRoomBuilder.CellarPlaySpaceDia, StandOverBoard);   // 2.02 m
        private static readonly float SeatCellar =
            HeadY(EnvRoomBuilder.CellarPlaySpaceDia, SeatOverBoard);    // 1.66 m
        private static readonly float HeadForest =
            HeadY(EnvRoomBuilder.ForestPlaySpaceDia, StandOverBoard);   // 2.80 m
        private static readonly float SeatForest =
            HeadY(EnvRoomBuilder.ForestPlaySpaceDia, SeatOverBoard);    // 2.30 m
        // ...and where he stands: at the board's edge with a little standoff, so
        // the down-view has the board's footprint in front of him rather than
        // under his chin. 1.20 m in the cellar and 1.70 m in the wood are 0.83
        // and 0.85 board widths from the centre against a worst board corner at
        // 0.71 — i.e. just outside the board, which is where a player reaching
        // it stands.
        private const float StandOffCellar = 1.20f;
        private const float StandOffForest = 1.70f;
        private const float Diag = 0.70710678f;

        // The mandatory self-review set. 4 yaws at seated eye height, the most
        // detailed corner of each room, two upward views (canopy / zenith), a
        // LOW pass that is the only way to catch a prop hovering a centimetre
        // over the floor, a shot from the clearing edge looking back across the
        // play space, and a sky-only frame with the room hidden.
        //
        // The moon bears 40.0 deg azimuth at 40.0 deg altitude
        // (EnvironmentsBuilder.MoonDir) — the Moon/MoonWide/TreeLine views are
        // aimed off that constant, so they follow it if it ever moves.
        private static readonly float MoonAz =
            Mathf.Atan2(EnvironmentsBuilder.MoonDir.x, EnvironmentsBuilder.MoonDir.z) * Mathf.Rad2Deg;
        private static readonly float MoonAlt =
            Mathf.Asin(EnvironmentsBuilder.MoonDir.y) * Mathf.Rad2Deg;

        // ==================== THE BOOKCASE'S OWN FRAME (ModBuild 153) ============
        // A PREVIEW STATION THAT POINTS AT NOTHING DOES NOT FAIL. It renders, and
        // it agrees with you. This project has now paid for that twice at this one
        // prop: the two FireShelf stations spent several builds photographing a
        // bare wall after the bookcase moved 3.85 m south (fixed at ModBuild 149,
        // and it is why a fire hanging 18.6 cm over a board had to be found on
        // hardware), and "HauntShelf" was still doing it at ModBuild 152 — aimed in
        // 143, 65 degrees off the prop, framing the candle TABLE in the opposite
        // corner. Every _pride and _shelf frame taken through it since 147 is a
        // photograph of the wrong corner of the room.
        //
        // Typed camera numbers are what did that, so the shelf stations are DERIVED
        // — from EnvRoomBuilder's own two constants, exactly as the Moon views are
        // derived from EnvironmentsBuilder.MoonDir. A prop move re-aims the cameras
        // that watch it. And because "derived" is still only an argument, the
        // harness MEASURES it at render time as well: AssertShelfStations finds the
        // real bookcase in the loaded prefab, projects its bounds into each of
        // these cameras, and FAILS THE RUN if it is not in the frame — with the
        // pixel rectangle it occupies printed for every one that is.
        private static readonly Vector3 ShelfAt = EnvRoomBuilder.CellarShelfAt;
        /// The way it faces, and therefore the way it falls: its prop forward.
        private static readonly Vector3 ShelfFall =
            Quaternion.Euler(0f, EnvRoomBuilder.CellarShelfYaw, 0f) * Vector3.forward;
        /// ...and the hinge axis, i.e. along the wall it has its back to.
        private static readonly Vector3 ShelfSide =
            new Vector3(ShelfFall.z, 0f, -ShelfFall.x);

        /// <summary>Aim at the standing carcass from an arbitrary station: the
        /// Euler a camera at <paramref name="from"/> needs to have the bookcase at
        /// the middle of its frame, <paramref name="aim"/> metres up it.</summary>
        private static Vector3 LookAtShelf(Vector3 from, float aim)
        {
            var to = ShelfAt + Vector3.up * aim - from;
            return new Vector3(-Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg,
                               Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
        }
        /// <summary>A station in the BOOKCASE's own frame: <paramref name="outM"/>
        /// metres along the way it falls, <paramref name="side"/> metres along the
        /// wall it stands against, <paramref name="up"/> metres off the floor —
        /// aimed at a point <paramref name="aim"/> metres up the standing
        /// carcass.</summary>
        private static (Vector3 pos, Vector3 euler) ShelfStand(float outM, float side,
                                                               float up, float aim)
        {
            var p = ShelfAt + ShelfFall * outM + ShelfSide * side + Vector3.up * up;
            return (p, LookAtShelf(p, aim));
        }
        // THE THREE-QUARTER VIEW, which is what "HauntShelf" was aimed to be in
        // ModBuild 143 and has not been since 147. 3.40 m out along the fall and
        // 1.20 m along the wall, from 2.10 m: at fov 62 that holds the standing
        // carcass (top corner 12 deg off axis), the whole 2.35 m sweep, and the
        // fallen carcass's far end (29 deg off axis, inside the 47 deg half-width)
        // — i.e. the same frame before, during and after, which is the whole point
        // of a station that watches an event rather than a pose.
        private static readonly (Vector3 pos, Vector3 euler) VShelf = ShelfStand(3.40f, 1.20f, 2.10f, 0.90f);

        private static readonly (string name, Vector3 pos, Vector3 euler, bool skyOnly, float fov)[] Views =
        {
            ("N", Eye, new Vector3(0, 0, 0), false, 60f),
            ("E", Eye, new Vector3(0, 90, 0), false, 60f),
            ("S", Eye, new Vector3(0, 180, 0), false, 60f),
            ("W", Eye, new Vector3(0, 270, 0), false, 60f),
            ("Corner", Eye, new Vector3(8, 48, 0), false, 60f),   // cellar: candle table NE; forest: toward the moon
            ("Up", Eye, new Vector3(-30, 45, 0), false, 60f),
            ("Canopy", Eye, new Vector3(-58, 20, 0), false, 60f),
            ("Zenith", Eye, new Vector3(-88, 0, 0), false, 60f),
            // low camera: floaters are invisible from 1.4 m and obvious from 0.45 m
            ("LowS", new Vector3(0f, 0.45f, 0f), new Vector3(-4, 195, 0), false, 60f),
            ("LowN", new Vector3(0f, 0.45f, 0f), new Vector3(-4, 25, 0), false, 60f),
            ("LowW", new Vector3(0f, 0.45f, 0f), new Vector3(-4, 285, 0), false, 60f),
            // from the edge of the clearing, looking back over the play space
            ("Edge", new Vector3(3.6f, 1.4f, -3.6f), new Vector3(2, 315, 0), false, 60f),
            ("SkyOnly", Eye, new Vector3(-34, 40, 0), true, 60f),
            // ---- ModBuild 134 review set ----
            // sky-only wide, straight at the Milky Way's own half of the dome
            ("SkyBand", Eye, new Vector3(-42, 200, 0), true, 75f),
            // the moon, close: does the disc OCCLUDE the stars behind it?
            ("Moon", Eye, new Vector3(-MoonAlt, MoonAz, 0), false, 14f),
            ("MoonWide", Eye, new Vector3(-MoonAlt, MoonAz, 0), false, 45f),
            // ...and the same close-up with the room hidden, which is the only
            // frame in which the occlusion can actually be judged: from inside
            // the clearing the crowns cover most of the moon.
            ("MoonSky", Eye, new Vector3(-MoonAlt, MoonAz, 0), true, 11f),
            // straight INTO the tree line on the side AWAY from the moon — this
            // is the frame the "so dark you would not dare walk there" ruling is
            // judged on, and the one that shows a lifted horizon band if any is left
            ("TreeLine", Eye, new Vector3(-2, MoonAz + 180f, 0), false, 60f),
            ("TreeLineLow", new Vector3(0f, 0.9f, 0f), new Vector3(3, MoonAz + 140f, 0), false, 60f),
            // cellar: the two corners furthest from any candle (SW and the stair
            // alcove behind the W doorway)
            ("DarkCornerSW", Eye, new Vector3(6, 232, 0), false, 60f),
            ("DarkCornerNW", Eye, new Vector3(4, 300, 0), false, 60f),
            // ---- ModBuild 135 review set ----
            // cellar: the window and its moonlight, from the middle of the room
            ("Window", Eye, new Vector3(-6, 344, 0), false, 60f),
            ("WindowClose", new Vector3(-0.9f, 1.55f, 2.2f), new Vector3(-8, 350, 0), false, 34f),
            // the beam where it lands, and the puddle it lands in
            ("Puddle", new Vector3(-1.6f, 1.10f, 0.9f), new Vector3(22, 318, 0), false, 55f),
            ("PuddleLow", new Vector3(-2.35f, 0.42f, 1.35f), new Vector3(9, 305, 0), false, 55f),
            // ceiling to floor at the drip, so the whole fall is in one frame
            ("DripColumn", new Vector3(-0.6f, 1.5f, 0.2f), new Vector3(0, 306, 0), false, 55f),
            // the rat's route: across the moonbeam, then away into the crate
            // candle's pool (RatRun looks north-west at the beam, RatLow follows
            // the second half of the run down the dark west side)
            ("RatRun", new Vector3(0.7f, 1.25f, 0.2f), new Vector3(13, 295, 0), false, 62f),
            // where the route crosses the beam — the shot the rat exists for
            ("RatBeam", new Vector3(-1.70f, 0.45f, 0.90f), new Vector3(10, 308, 0), false, 50f),
            ("RatLow", new Vector3(-1.10f, 0.80f, -0.60f), new Vector3(9, 292, 0), false, 55f),
            ("RatEnd", new Vector3(1.2f, 0.85f, -1.4f), new Vector3(11, 215, 0), false, 55f),
            // ---- the two MOUTHS, close and low (ModBuild 143) ----------------
            // The old set had no frame in which the hole filled more than forty
            // pixels, which is exactly how a rat that shrank to nothing instead
            // of going in shipped twice. These look at each mouth from about
            // where a kneeling player's head would be: near enough that the ring
            // and the pocket are legible, and off to one side, because a mouth
            // photographed dead-on cannot show that its bore leans.
            ("RatHoleN", new Vector3(-3.75f, 0.32f, 3.92f), new Vector3(22, 336, 0), false, 40f),
            // The SOUTH mouth is behind the crates (Crate0 at x=-1.55, Crate1 at
            // x=-0.45), which is the authored ending — "it disappears under the
            // crates" — so it can only be looked at down the 50 cm gap between
            // them, and that is also the only line a player has on it.
            ("RatHoleS", new Vector3(-1.05f, 0.42f, -2.90f), new Vector3(13, 176, 0), false, 30f),
            // ...and one at standing height, which is the distance and angle the
            // entry is really met at: a hole in a skirting seen from 1.3 m.
            ("RatHoleSWide", new Vector3(-1.00f, 1.30f, -2.20f), new Vector3(29, 179, 0), false, 45f),
            // (THE TWO THAT WATCHED THE MOON POOL are gone with what they
            // watched. They were called HauntDoor/HauntDoorOff — a name that had
            // already outlived one event, since it was framed on the door of light
            // at the top of the stair and then re-aimed at the SWELL that replaced
            // it. ModBuild 149 deleted the swell as well, on the user's order
            // ("Der 'Oben an eine Treppe geht eine Tür auf' Effekt ist kaputt,
            // stattdessen kommt eine Art Zylinder aus der Pfütze. Lösch diesen
            // Effekt komplett"), and cellar card 2 now draws nothing at all. A
            // station aimed at a card that draws nothing renders a frame that is
            // indistinguishable from a regression, which is the one thing this
            // harness must never produce — so the stations go rather than being
            // re-aimed a third time.)

            // (EIGHTEEN HAUNT VIEWS WERE DELETED HERE, ModBuild 146. Every one of
            // them was aimed at an imported apparition FIGURE — the bust at the
            // window and its shadow, the head on the flagstones, the strider in the
            // stair doorway, the face behind the trunk, the watcher, the crossing
            // figure, the hunched mass, the hanged body — and the figures are gone
            // (user: "Entferne die alten 3D assets komplett"). A view aimed at
            // nothing renders a frame that is indistinguishable from a regression,
            // which is worse than no frame at all. What the four surviving
            // placeholder cards do is spawn REAL GAME MONSTERS at runtime, and this
            // harness renders the prefab: it cannot photograph them at all, and
            // saying so here is more use than an empty PNG.)
            // the cobwebs: the one the shelf candle reaches, and the one over
            // the stair door (the only two that are ever lit enough to judge)
            ("Web", new Vector3(2.60f, 2.20f, 1.20f), new Vector3(-6, 87, 0), false, 34f),
            ("WebCorner", new Vector3(-2.20f, 1.60f, -1.90f), new Vector3(-33, 232, 0), false, 45f),
            // the candle pools: does the wall behind them stay dark?
            ("CandleTable", new Vector3(1.0f, 1.30f, 0.4f), new Vector3(6, 42, 0), false, 55f),
            ("CandleCrate", new Vector3(0.6f, 1.20f, -1.0f), new Vector3(10, 212, 0), false, 55f),
            // forest: the axe in the stump, close
            ("Axe", new Vector3(3.2f, 1.05f, -2.6f), new Vector3(16, 137, 0), false, 34f),
            ("AxeLow", new Vector3(3.5f, 0.62f, -2.9f), new Vector3(6, 139, 0), false, 30f),
            // forest: the firefly swarm (both from the clearing, where it is
            // judged, and close, where its SIZE is judged) and sky for meteors
            ("Fireflies", new Vector3(0f, 1.4f, 0f), new Vector3(2, 217, 0), false, 55f),
            ("FirefliesClose", new Vector3(-2.4f, 1.2f, -3.2f), new Vector3(3, 217, 0), false, 32f),
            ("MeteorSky", Eye, new Vector3(-46, 250, 0), false, 70f),
            // ---- ModBuild 136 review set ----
            // THE MOONLIGHT, from the angles that expose a slab. The old five
            // slats read as lasers, so the new volume is judged on: (a) can you
            // see an edge from the side, (b) does it brighten when you look
            // along it the way real light-filled air does, (c) is there any
            // grazing angle at which a face shows.
            ("BeamSide", new Vector3(1.20f, 1.50f, 1.20f), new Vector3(3, 302, 0), false, 55f),
            ("BeamEdge", new Vector3(-0.60f, 1.45f, 2.00f), new Vector3(9, 294, 0), false, 50f),
            ("BeamAlong", new Vector3(-4.20f, 0.60f, 1.30f), new Vector3(-24, 42, 0), false, 55f),
            ("BeamLow", new Vector3(-1.00f, 0.35f, 0.90f), new Vector3(-3, 300, 0), false, 60f),
            ("BeamGraze", new Vector3(-3.05f, 1.62f, 3.05f), new Vector3(1, 262, 0), false, 60f),
            ("PoolClose", new Vector3(-2.20f, 1.05f, 0.90f), new Vector3(31, 321, 0), false, 50f),
            // ---- ModBuild 144 review set --------------------------------
            // THE PUDDLE, ON ITS OWN AND FILLING THE FRAME. Every existing
            // puddle view also contains the moon pool, which is drawn
            // additively on top of it and swamps everything the multiply pass
            // does — that is written down in the builder next to _DraftWave as
            // the reason the wind ripple could not be judged from a preview at
            // all. "Es sollte mehr wie Eis rüberkommen" cannot be judged from a
            // frame where the puddle is eighty pixels across either. This is
            // aimed at PuddleAt (-3.60, 0, 2.35) from 1.8 m at kneeling height,
            // off to one side so the sheet's relief has a grazing angle to
            // catch and its dome is not looked at dead-on.
            ("IceClose", new Vector3(-2.30f, 0.85f, 1.05f), new Vector3(25, 315, 0), false, 45f),
            // ...and from ABOVE, which is the pose the whole ice rebuild turns
            // on: water seen from overhead is a dark hole in the floor and ice
            // is bright, so this is where "solid, not a puddle" is decided.
            ("IceTop", new Vector3(-3.55f, 1.55f, 1.95f), new Vector3(68, 350, 0), false, 45f),
            ("SillClose", new Vector3(-1.35f, 2.00f, 2.60f), new Vector3(-6, 0, 0), false, 40f),
            // right under the aperture, looking back into it: the only frame in
            // which the bar shadows are supposed to be visible at all
            ("BarStripe", new Vector3(-2.00f, 2.00f, 3.30f), new Vector3(-21, 28, 0), false, 45f),
            // THE WEBS, close enough to judge the threads and in context
            ("WebShelfClose", new Vector3(2.90f, 2.05f, 1.35f), new Vector3(-25, 91, 0), false, 38f),
            ("WebCornerNew", new Vector3(-2.30f, 1.90f, -1.80f), new Vector3(-15, 228, 0), false, 45f),
            ("StrandClose", new Vector3(0.00f, 1.90f, -1.20f), new Vector3(-22, 223, 0), false, 42f),
            // THE FOREST FLOOR, which is the thing that was "viel zu hell":
            // from eye height toward the moon and away from it, and from a low
            // angle where a lit floor betrays itself worst.
            ("FloorToMoon", Eye, new Vector3(34, MoonAz, 0), false, 60f),
            ("FloorAway", Eye, new Vector3(34, MoonAz + 180f, 0), false, 60f),
            ("FloorLow", new Vector3(0f, 0.35f, 0f), new Vector3(6, MoonAz + 90f, 0), false, 65f),
            ("FloorEdge", new Vector3(5.0f, 1.40f, -3.0f), new Vector3(14, 300, 0), false, 60f),
            // ---- ModBuild 137 review set ----
            // (2) THE BEAM FROM INSIDE. User finding: "im Keller wenn man nah in
            // den Mondschein am Fenster geht verschwindet er plötzlich."
            // The camera positions below are ON the beam axis and to either side
            // of it: winMid (-1.352, 2.515, 4.500) + dir * s with
            // dir = -MoonDir = (-0.4926, -0.6427, -0.5868), so
            //   s=0.60 -> (-1.65, 2.13, 4.15)   just inside the embrasure
            //   s=1.58 -> (-2.13, 1.50, 3.58)   eye height, the middle of the beam
            //   s=2.60 -> (-2.63, 0.84, 2.97)   knee height, near the pool
            // and 'across' = (0.7933, 0, -0.6087) is the horizontal perpendicular.
            // Looking ALONG the axis is euler(-40, 40, 0) (up toward the window)
            // and euler(40, 220, 0) (down toward the pool) — 40 deg is the moon's
            // own altitude and azimuth, so these follow MoonDir if it moves.
            ("BeamInsideL", new Vector3(-2.13f, 1.50f, 3.58f), new Vector3(-4, 130, 0), false, 70f),
            ("BeamInsideR", new Vector3(-2.13f, 1.50f, 3.58f), new Vector3(-4, 310, 0), false, 70f),
            ("BeamInsideUp", new Vector3(-2.13f, 1.50f, 3.58f), new Vector3(-MoonAlt, MoonAz, 0), false, 70f),
            ("BeamInsideDown", new Vector3(-2.13f, 1.50f, 3.58f), new Vector3(MoonAlt, MoonAz + 180f, 0), false, 70f),
            ("BeamMouthUp", new Vector3(-1.65f, 2.13f, 4.15f), new Vector3(-MoonAlt, MoonAz, 0), false, 70f),
            ("BeamKneeUp", new Vector3(-2.63f, 0.84f, 2.97f), new Vector3(-MoonAlt, MoonAz, 0), false, 70f),
            // walking THROUGH it: five stations 0.6 m apart ALONG the horizontal
            // perpendicular (0.7933, 0, -0.6087), with one fixed head
            // orientation looking up the beam's own bearing, so the shaft has to
            // sweep across the frame and stay continuous. C is dead centre, on
            // the axis; A and E are 1.2 m out, past the hull's own rim.
            ("BeamThruA", new Vector3(-3.082f, 1.50f, 4.303f), new Vector3(-10, MoonAz, 0), false, 70f),
            ("BeamThruB", new Vector3(-2.606f, 1.50f, 3.938f), new Vector3(-10, MoonAz, 0), false, 70f),
            ("BeamThruC", new Vector3(-2.130f, 1.50f, 3.573f), new Vector3(-10, MoonAz, 0), false, 70f),
            ("BeamThruD", new Vector3(-1.654f, 1.50f, 3.208f), new Vector3(-10, MoonAz, 0), false, 70f),
            ("BeamThruE", new Vector3(-1.178f, 1.50f, 2.842f), new Vector3(-10, MoonAz, 0), false, 70f),
            // ...and the same walk seen from OUTSIDE, which is the control: the
            // beam must look the same from here whatever the previous frames did.
            ("BeamFromRoom", new Vector3(1.60f, 1.50f, 0.60f), new Vector3(-2, 315, 0), false, 70f),
            // (16a) THE FOREST SHAFTS AND THE MOON IN ONE FRAME. If a shaft is
            // parallel to MoonDir its image must converge on the moon's image, so
            // a wide frame containing both is a PROOF, not an impression.
            ("ShaftMoon", Eye, new Vector3(-26, MoonAz, 0), false, 78f),
            ("ShaftMoonLow", new Vector3(0f, 0.45f, 0f), new Vector3(-30, MoonAz, 0), false, 78f),
            // four yaws, same pitch: the shafts' lean must reverse as you turn
            ("ShaftYaw0", Eye, new Vector3(-18, MoonAz, 0), false, 75f),
            ("ShaftYaw90", Eye, new Vector3(-18, MoonAz + 90f, 0), false, 75f),
            ("ShaftYaw180", Eye, new Vector3(-18, MoonAz + 180f, 0), false, 75f),
            ("ShaftYaw270", Eye, new Vector3(-18, MoonAz + 270f, 0), false, 75f),
            // from above and behind the clearing, looking down the moon bearing:
            // the shafts' AZIMUTH is measurable in this frame
            ("ShaftPlan", new Vector3(-9.2f, 12.0f, -11.9f), new Vector3(30, MoonAz, 0), false, 75f),
            // ...and straight down over the clearing: the ground pools and the
            // shafts must all run along the same compass bearing
            ("ShaftTop", new Vector3(0f, 13.5f, 0f), new Vector3(80, MoonAz, 0), false, 80f),
            // standing under the middle shaft, looking up along it at the moon
            ("ShaftUnder", new Vector3(3.35f, 1.40f, 4.36f), new Vector3(-MoonAlt, MoonAz, 0), false, 78f),
            // ---- HAUNT review set (rebuilt after the ModBuild 141 verdict) ----
            // One frame per apparition, aimed at the card the builder placed, plus
            // a WIDE frame per room at the same instants. These are only ever shot
            // inside the HAUNT time series below (the schedule solved for an
            // instant at which the event is really running); in the ordinary set
            // the feature is off and every card is collapsed to a point, so
            // shooting them there would produce six empty rooms.
            //
            // TWO DISTANCES FOR EVERY EVENT, and that is a lesson rather than a
            // preference. The first pass judged the apparitions on close frames
            // only, reported intentions, and shipped a smiley: a face that is
            // "unsettling" at 1.5 m can be an emoji at 5 m, and the wide frame is
            // the only place "eher im Hintergrund" can be checked at all.
            //
            // CELLAR. Cards, in order: Window, Hands, Floor, Tremble, Stair, Shelf.
            //
            // AND EVERY APPARITION IS SHOT FROM TWO ANGLES, which is new this
            // round and is the whole point of it. A flat card and a solid are
            // indistinguishable head-on; the standee reads as a standee the moment
            // you are off its axis, which is what the user was doing when he
            // caught all seven of them. A "*Off" view is the same event from 35-60
            // degrees round, and if a picture still looks like a picture there,
            // the fix did not work.
            // ---- THE HANDPRINTS, five frames ---------------------------------
            // Rebuilt this round with the marks themselves: three LIFE-SIZE prints
            // on the west wall at z 1.36 / 0.94 / 0.50, y 1.34 / 1.08 / 0.80. The
            // old station framed a 1.24 m card at 0.85 m and is 1.7 m off the new
            // group's centre.
            //
            // The close read: the whole group, square on, from 1.66 m.
            ("HauntHands", new Vector3(-3.55f, 1.32f, 0.84f), new Vector3(4, 270, 0), false, 34f),
            // THE GRAZING FRAME, and it is the one that answers "es schwebt über
            // den Mauern". The camera is 0.25 m off the wall looking ALONG it, so
            // the sightline meets the stone at 5 degrees: at that angle a mark
            // standing 30 mm proud shows 0.34 m of parallax against the stone
            // behind it, and one lying 2 mm off it shows 23 mm. If the fix did not
            // work, this is the frame it fails in.
            ("HauntHandsGraze", new Vector3(-5.00f, 1.25f, 2.10f), new Vector3(6, 186, 0), false, 40f),
            // SCALE, with a known reference IN FRAME: the stair opening is 1.6 m
            // wide and 2.35 m tall and is cut out of this same wall, so a frame
            // holding both it and the prints settles "are these life size?"
            // without a single number. (It is also the frame fault (d) is visible
            // in: the card this replaces spanned z 0.68..1.92 and the opening
            // starts at 1.66.)
            ("HauntHandsWide", new Vector3(-1.60f, 1.55f, 1.20f), new Vector3(5, 278, 0), false, 28f),
            // ...from the player's real EYE HEIGHT, standing at the middle of the
            // room: 5.4 m away, which is how far the west wall actually is.
            ("HauntHandsHead", new Vector3(0f, HeadCellar, 0f), new Vector3(9, 279, 0), false, 40f),
            // ...and from the DIORAMA posture, at the board's east edge looking
            // down and across. If the prints cannot be found from here they are
            // not in the game most of the time.
            ("HauntHandsBoard", new Vector3(2.30f, 1.95f, 0.30f), new Vector3(6, 274, 0), false, 30f),
            // ...and the head on the floor is at the edge of the moon pool now
            // THE BOOKSHELF GOING OVER. Two viewpoints and, unlike everything else
            // here, four PHASES rather than three, because the event is a
            // trajectory: standing, falling, down, and back up again.
            // ---- ModBuild 153: RE-AIMED, AND DERIVED SO IT CANNOT DRIFT AGAIN --
            // This station was (0.60, 2.10, -1.30) on yaw 48 with a comment three
            // lines below saying it no longer sees the bookshelf. MEASURED: the
            // bookcase bears 113.5 deg from there and the camera looked at 48, i.e.
            // 65 deg off against a 47 deg half-width — the prop was not in the
            // frame at all. What WAS in it, at 11.7 deg off axis and 5.2 m away, is
            // the candle table in the opposite corner of the room. Every haunt and
            // element frame taken through this station since ModBuild 147 is a
            // photograph of that table.
            //
            // It is not repointed by eye: ShelfStand puts it in the bookcase's own
            // frame, so it follows CellarShelfAt. The three-quarter geometry is
            // kept (a station out along the fall and to one side, from above head
            // height, looking slightly down), and so are the height and the fov, so
            // it is the same KIND of frame the 143 comment describes — it is now
            // pointed at the thing that frame was always about.
            ("HauntShelf", VShelf.pos, VShelf.euler, false, 62f),
            // ...and this one is aimed from where it already stood: the "watching
            // from across the room" viewpoint is a property of the ROOM, not of the
            // prop, so its position stays typed and only its AIM is derived. It was
            // 9.2 deg off (yaw 150 against a bearing of 159.2) — in frame, but with
            // the bookcase off toward the edge for no reason anybody wrote down.
            ("HauntShelfOff", new Vector3(2.30f, 1.75f, 3.60f),
             LookAtShelf(new Vector3(2.30f, 1.75f, 3.60f), 1.00f), false, 58f),
            // ...and a THIRD, square on the plane the carcass actually sweeps —
            // ModBuild 152, and it exists because "HauntShelf" above did not see
            // the bookshelf. (That station is re-aimed at ModBuild 153 and this one
            // is deliberately NOT folded into it: they answer different questions.
            // HauntShelf looks AT the carcass; this one looks ALONG the plane it
            // sweeps, which is the only way to judge what is left behind in the air
            // it vacates — the sparks in 152, the wall wash in 153.)
            //
            // ITS NUMBERS ARE TYPED AND STAY TYPED, which is the one exception in
            // this block and is deliberate: it is not aimed at the prop, it is
            // aimed along a PLANE, so LookAtShelf would turn it 20 degrees and
            // destroy exactly the framing it exists for. AssertShelfStations
            // measures it against the real prop like every other station here, so
            // "typed" does not mean "unchecked".
            //
            // DERIVED FROM THE BAKE, not aimed by eye: the hinge is at
            // (4.57, -0.02, -3.15) with the axis (0,0,1), so the fall is entirely
            // in the plane z = -3.15, and the standing carcass at x = 4.86 lands
            // its top board at x = 2.51. The camera stands 2.8 m off that plane
            // on the room side, on the MIDPOINT of that sweep (x = 3.69, hence
            // 3.75), and looks along it. Yaw 178 means screen-right is -X, so the
            // bookcase starts left of centre and falls INTO the frame. At 60 deg
            // and 6 deg of pitch it holds x = 0.9..6.6 and y = -0.4..2.9 — the
            // standing carcass, the whole arc, the fallen carcass, and 1.2 m of
            // air above the top fire, which is where the sparks are and is the
            // only part of the picture this series is really about.
            ("HauntShelfRide", new Vector3(3.75f, 1.55f, -0.35f), new Vector3(6, 178, 0), false, 60f),
            // ...and the tremble draws nothing at all: it is judged on the WEBS,
            // so its frames are the two web close-ups, shot at its own instants.
            ("HauntWeb", new Vector3(2.90f, 2.05f, 1.35f), new Vector3(-25, 91, 0), false, 38f),
            // (FOREST — AND THERE IS NOTHING LEFT TO AIM AT. The station here was
            // "HauntEyes", pointed at bearing 288 from the seated eye in the middle
            // of the clearing. All three of the wood's cards are schedule
            // placeholders since ModBuild 149 deleted the eyeshines ("Entferne den
            // 'Augen' Effekt im Wald komplett inklusive aller sounds und assets"),
            // so this room contributes no geometry to EnvHaunt at all and there is
            // no apparition in it for a preview to photograph. What happens in the
            // wood now is two REAL GAME MONSTERS, spawned at runtime by
            // HauntFigures against the live scene, and this harness renders the
            // prefab: it cannot photograph them, and saying so here is more use
            // than an empty PNG.)

            // ...and one wide frame per room at the same instants: the brief is
            // "eher im Hintergrund", and the only way to check that an apparition
            // is NOT intrusive is to look at the room the way a player would and
            // see whether it pulls the eye.
            // ...and it faced the WRONG WAY, ModBuild 153. It is listed in
            // CellarHaunts for card 5 and card 5 only — i.e. the bookshelf is the
            // one thing it has ever been asked to photograph — and yaw 320 is
            // north-west while the bookcase bears 123 deg from the eye. 197 deg
            // off: the event was directly behind the camera in every frame of it.
            // The station itself is right and is not moved (a wide frame from the
            // player's own station is exactly how "eher im Hintergrund" is judged);
            // its AIM is derived now, from the prop.
            ("HauntWideC", Eye, LookAtShelf(Eye, 1.00f), false, 78f),
            // ...and the cellar's shelf face from the BOARD, which is the distance
            // a player actually meets it at.
            // ...from 5.5 m, on the side the face comes out on. NOT from the
            // middle of the board: the card sits at the shelf's BACK panel and
            // emerges toward +z, so from dead centre the shelf's own front hides
            // it completely and the frame is a picture of an empty shelf. Where an
            // apparition can be seen from is part of its placement, and a preview
            // that does not show that is a preview that cannot check it.
            // (ModBuild 153: 27 deg off the prop — in frame, but out at the edge of
            // it, and the paragraph above is about an apparition CARD that has not
            // existed since 146. Card 5 is the bookcase now, so the aim is derived
            // from the bookcase; the station, which is the whole point of the
            // paragraph, is untouched.)
            ("HauntFarC", new Vector3(0f, 1.45f, 0.0f),
             LookAtShelf(new Vector3(0f, 1.45f, 0.0f), 1.00f), false, 62f),
            // THE SHADOW. User, cellar 7: "gruselig wäre auch wenn sie beim
            // Mondlicht einen Schatten wirft wenn sie durchs Fenster schaut."
            // It lands at (-3.19, 2.76) — computed from the bust's own vertices
            // along MoonDir — so this looks DOWN at the moon pool from the board
            // and has the window in the top of the frame at the same time.
            // ---- FIRE REAL (ModBuild 145) --------------------------------
            // THE FIRE, CLOSE ENOUGH TO ANSWER THE QUESTION. Two rounds of this
            // feature were reported on from wide room shots in which each fire is
            // eighty pixels tall, and in a frame that size a bed, a tongue and a
            // detached puff are all the same orange smudge — which is exactly how
            // "viele Kerzenflammen" survived two builds. Every frame below puts
            // ONE fire across a third of the picture at the distance a player
            // meets it from, and the question asked of each is the same one:
            // is this one flame, or is this a fire?
            //
            // ...and each site is shot from TWO heights, because the bed is the
            // thing that decides it and a bed is invisible from directly above.
            ("FireCrate", new Vector3(-0.60f, 1.30f, -2.55f), new Vector3(11, 218, 0), false, 40f),
            ("FireCrateLow", new Vector3(-0.35f, 0.62f, -2.40f), new Vector3(-2, 214, 0), false, 40f),
            ("FireSpill", new Vector3(-1.85f, 1.15f, -2.05f), new Vector3(17, 231, 0), false, 45f),
            ("FireSpillLow", new Vector3(-1.90f, 0.45f, -2.10f), new Vector3(1, 231, 0), false, 45f),
            // ModBuild 149: BOTH SHELF STATIONS WERE AIMED AT WHERE THE SHELF USED TO BE.
            // They looked +X from z = +0.90 on yaw 84, and the bookcase has stood at
            // CellarShelfAt (4.86, -3.15) since the round that moved it — so every preview
            // either lane has taken of "the shelf fire" for several builds was a picture of a
            // BARE WALL, and that is why a fire floating 18.6 cm above a shelf board survived a
            // whole round of previews and had to be found on hardware by the user. A preview
            // station that points at nothing does not fail; it renders, and it agrees with you.
            // RE-MEASURED AT ModBuild 153 AND LEFT ALONE, which is a finding and not
            // an omission: against CellarShelfAt the bookcase bears 107.0 deg from
            // the first station (aimed 108, so 1.0 deg off, 2.05 m away) and
            // 114.2 deg from the second (aimed 117, so 2.8 deg off, 1.71 m away).
            // Both are on the prop. They are NOT re-derived, because deriving them
            // would turn each by a degree or two and silently invalidate the fire
            // lane's whole comparison set for no gain — and the drift these numbers
            // are exposed to is caught now rather than tolerated: AssertShelfStations
            // measures every station in this block against the real prop in the
            // loaded prefab and FAILS THE RUN if one stops seeing it.
            ("FireShelf", new Vector3(2.90f, 1.50f, -2.55f), new Vector3(-4, 108, 0), false, 42f),
            ("FireShelfLow", new Vector3(3.30f, 0.95f, -2.45f), new Vector3(-15, 117, 0), false, 42f),
            // ...and the room WITH the fires in it, from the board: "bedrohlich"
            // is a property of a room, not of a sprite.
            ("FireRoom", new Vector3(0f, 1.40f, 0f), new Vector3(2, 232, 0), false, 75f),
            // FOREST. The burning snag is at (-7.14,-0.60), the deadfall at
            // (6.75,4.88) and the brushwood at (-6.90,-5.00) — all outside the
            // 4.5 m PlaySpace, so these look OUT of the clearing at them.
            ("FireSnag", new Vector3(-3.20f, 1.40f, -0.35f), new Vector3(-4, 275, 0), false, 45f),
            ("FireSnagWide", Eye, new Vector3(-8, 275, 0), false, 70f),
            ("FireLog", new Vector3(3.10f, 1.40f, 2.30f), new Vector3(4, 47, 0), false, 45f),
            ("FireBrush", new Vector3(-3.00f, 1.30f, -2.10f), new Vector3(2, 226, 0), false, 45f),
            // 250 deg, which is between the snag (265) and the brushwood (234):
            // the frame the "peripheral, never over the board" rule is judged in,
            // with two of the three burning things in it at once.
            ("FireWood", Eye, new Vector3(0, 250, 0), false, 78f),
            // ================= THE GRAZING SET — ModBuild 148 ==================
            // USER VERDICT, ModBuild 147: "Es sitzt nicht direkt auf den assets,
            // schwebt daneben oder darüber" and "Es sind mehrere sichtbare
            // 'Striche' auf den assets drauf" — with two screenshots,
            // .planning/debug/feuer1.jpg and feuer2.jpg.
            //
            // NEITHER FAULT IS VISIBLE IN ANY FRAME THIS HARNESS HAD, and that
            // is the reason both survived a whole round of previews. Every fire
            // view above looks DOWN at its fire from 1.05-1.85 m — the FireXLow
            // pair are the low ones and they are still 0.45-1.15 m up and pitched
            // down. From above:
            //   * a card seen at 30-60 degrees of elevation projects to most of
            //     its width, so no quad is a stroke and the Striche cannot
            //     appear;
            //   * a fire that hangs off the flank of a log or crosses a trunk
            //     hides behind the thing it is standing on, because you are
            //     looking at the TOP of that thing.
            // Both are only visible from a head that is at or below the height of
            // the fire, which is where the two screenshots were taken from and
            // where a seated VR player's head actually is relative to a fire on
            // the forest floor.
            //
            // So these are the user's own two framings, reconstructed from the
            // seats the bake log prints: low, near, and looking ALONG the burning
            // thing rather than down onto it. They are the frames the "no
            // individual quad may be identifiable" claim has to be made in, and
            // they are in the fire PHASE series as well (ForestFireViews below),
            // because a still cannot show a dissolve.
            //
            // Log0/1/2 are at (6.00,0.14,3.96), (6.75,0.14,4.88), (7.50,0.16,5.80)
            // — a deadfall running 39 degrees east of north. The eye is 0.55 m up,
            // i.e. below the top of the flames, and 2.6 m out.
            ("FireLogGraze", new Vector3(4.60f, 0.55f, 3.40f), new Vector3(9, 56, 0), false, 45f),
            // ...and from the far side, so a card that is edge-on in one of these
            // is broadside in the other and the pair covers the rosette.
            ("FireLogGrazeB", new Vector3(8.90f, 0.60f, 3.05f), new Vector3(10, 312, 0), false, 45f),
            // The SNAG, from inside the clearing and looking UP the trunk: this is
            // feuer2.jpg's framing, and it is the one that showed the fire
            // plastered diagonally across the bark. The mid fire is at
            // (-7.11,1.80,-0.64) and its cards now stand on a 150-degree sector of
            // the bark facing exactly this way.
            ("FireSnagGraze", new Vector3(-4.05f, 1.05f, -0.25f), new Vector3(-11, 267, 0), false, 56f),
            // ...and its foot, from a head that is lower than the flames are tall.
            ("FireSnagGrazeLow", new Vector3(-5.30f, 0.38f, -1.70f), new Vector3(6, 298, 0), false, 40f),
            // The CELLAR gets one too. The complaint was about the wood, but the
            // burning spill is the widest, flattest fire in either room and is the
            // one that would show a stroke first if the fade were wrong.
            ("FireSpillGraze", new Vector3(-1.55f, 0.34f, -2.55f), new Vector3(2, 226, 0), false, 42f),
            // ================== THE HEAD SET — ModBuild 148 ====================
            // Every view above is shot from the 1.4 m station or lower. In the
            // WOOD that is 10 cm UNDER the board's own underside plane, so no
            // frame this harness has ever produced shows the room from where the
            // player's eye is, and "is the board still readable / does the art
            // intrude on it" has been judged from beneath the table for as long
            // as the board-anchored placement has existed. See THE PLAYER'S HEAD
            // above for the derivation; the numbers are 2.02/1.66 m in the cellar
            // and 2.80/2.30 m in the wood, standing and seated.
            //
            // NOTHING ABOVE MOVED. These are additions, so every previous lane's
            // frame is still reproducible pixel for pixel.
            //
            // Two per room across the room (the yaws of FireRoom and FireWood, so
            // each has a same-aim 1.4 m twin to be read against), and one per room
            // looking DOWN at the board's footprint from the board's edge — the
            // only frame in which an intrusion into the sightline to a figure can
            // be seen at all.
            ("HeadC", new Vector3(0f, HeadCellar, 0f), new Vector3(4, 232, 0), false, 75f),
            ("HeadSeatC", new Vector3(0f, SeatCellar, 0f), new Vector3(4, 232, 0), false, 75f),
            ("HeadBoardC", new Vector3(StandOffCellar * Diag, HeadCellar, StandOffCellar * Diag),
                           new Vector3(29, 225, 0), false, 60f),
            ("HeadS", new Vector3(0f, HeadForest, 0f), new Vector3(2, 250, 0), false, 78f),
            ("HeadSeatS", new Vector3(0f, SeatForest, 0f), new Vector3(2, 250, 0), false, 78f),
            ("HeadBoardS", new Vector3(StandOffForest * Diag, HeadForest, StandOffForest * Diag),
                           new Vector3(28, 225, 0), false, 60f),
        };

        // ================================================================ HAUNT
        // The creepy easter eggs are on screen for a few seconds out of every few
        // minutes, so the ordinary preview set can never contain one. This series
        // SOLVES the shipped schedule for an instant at which a given event is
        // really running (EnvRoomBuilder.HauntPreviewClock) and shoots it there —
        // no debug flag, no forced-visible code path, and therefore no risk of
        // photographing a path that is not the one that ships.
        //
        // Three phases per event, because half of this catalogue is ABOUT its
        // envelope: 0.25 is the reveal (has it come out from behind the trunk
        // yet?), 0.55 the hold (what does it actually look like?), 0.88 the
        // withdrawal (does it leave the way it came, or vanish?).
        private static readonly float[] HauntPhases = { 0.25f, 0.55f, 0.88f };

        // ...except the bookshelf, whose event is a TRAJECTORY and not an
        // envelope. GhvrShelfTip() puts the topple in 0.000-0.180, the landing
        // (one ballistic rebound of 1.9 deg over 0.62 s) at 0.180-0.204, the
        // lie-down in 0.204-0.620 (10.8 s) and the recovery in 0.620-1.000, so
        // three evenly spaced phases would photograph "down, down, up" and miss
        // the fall entirely.
        //
        // THE BOUNDARIES ABOVE ARE THE ONES COMMIT e0c50ce SHIPPED — the old
        // comment here still said "0.18-0.24 the landing", which was the
        // pre-separatrix schedule, and a stale segment table in the harness is
        // how a phase list ends up photographing the wrong thing (see below).
        //
        // 0.16 IS NEW, AND IT IS THE ONLY FRAME THAT SHOWS THE FALL. The curve is
        // now the pendulum's exact separatrix, which is very nearly still for the
        // first half of its window and then dumps 27 of its 88 degrees in the
        // last half second: at ph = 0.08 the shelf has moved about 2 degrees off
        // plumb, i.e. that frame is indistinguishable from the room at rest. The
        // list was chosen against the OLD quadratic, where 0.08 was already a
        // quarter of the way over. 0.16 lands at ~0.89 of the fall window, which
        // is where the thing is visibly going over — the frame the user's "mir
        // gefällt wie das Regal fällt" is actually about.
        //   0.08  barely off plumb (kept: it is the proof that the creep is slow)
        //   0.16  the fast part of the topple
        //   0.30  down, just after the rebound — THE POSE FRAME (finding 3)
        //   0.72  lying there, mid lie-down
        //   0.97  nearly back up
        private static readonly float[] HauntShelfPhases = { 0.08f, 0.16f, 0.30f, 0.72f, 0.97f };

        // view name, card index, and that card's authored (reveal, hold, fade) —
        // which must match the builder's catalogue, because the phase is a
        // fraction of the whole run.
        // ---- ModBuild 146: MOST OF THIS SERIES NO LONGER HAS ANYTHING TO SHOOT.
        // The imported apparition FIGURES are deleted (user: "Entferne die alten
        // 3D assets komplett"), so every view that existed to photograph one is
        // gone with it. Four of the ten events survive as SCHEDULE PLACEHOLDERS
        // whose content is a real game monster spawned at runtime — and a bake
        // preview cannot photograph those AT ALL, because the harness renders the
        // prefab and the monsters are not in it. Retaining their views would have
        // produced empty frames that look exactly like a regression.
        //
        // WHAT IS LEFT is what the BAKE still draws: the handprints, the cobweb
        // tremble and the bookshelf going over. The card indices below are the new
        // ones (cellar 0 Window, 1 Hands, 2 Tremble, 3 Stair, 4 Shelf).
        private static readonly (string view, int card, Vector3 env)[] CellarHaunts =
        {
            // THE HANDPRINTS. 0.10 + 5.10 + 2.80, and the reveal is 0.10 because a
            // hand hitting a wall is instantaneous — the 2.8 s bloom it replaces is
            // the grammar of a photograph developing, which is the register the
            // user called "kein echter Horror". The three marks carry three
            // different RISE RATES off this same start (0.10 / 0.55 / 1.10) and
            // all three sum to 8.00 s, so `phase` is common and this one triple
            // solves the clock for all of them. The 0.25 phase therefore lands at
            // t = 2.0 s, by which time all three are up: the ORDER is only visible
            // in the first second, which no fixed phase can photograph, so it is
            // argued from the envelope in the builder rather than claimed here.
            ("HauntHands", 1, new Vector3(0.10f, 5.10f, 2.80f)),
            ("HauntHandsGraze", 1, new Vector3(0.10f, 5.10f, 2.80f)),
            ("HauntHandsWide", 1, new Vector3(0.10f, 5.10f, 2.80f)),
            ("HauntHandsHead", 1, new Vector3(0.10f, 5.10f, 2.80f)),
            ("HauntHandsBoard", 1, new Vector3(0.10f, 5.10f, 2.80f)),
            // (CARD 2 HAS NO SHOTS. It held the stair-top door and then the swell
            // in the moon pool, and both were deleted on the user's order; it is a
            // schedule placeholder now and draws nothing. See the station list.)
            ("HauntWeb", 3, new Vector3(0.0f, 1.1f, 0.9f)),
            // the bookshelf: 0.001 + 26 + 0.001 s, the whole of it hold
            ("HauntShelf", 5, new Vector3(0.001f, 26f, 0.001f)),
            ("HauntShelfOff", 5, new Vector3(0.001f, 26f, 0.001f)),
            ("HauntWideC", 5, new Vector3(0.001f, 26f, 0.001f)),
            // ...and the shelf from the BOARD, which is where a player meets it
            ("HauntFarC", 5, new Vector3(0.001f, 26f, 0.001f)),
            // THE PLAYER'S OWN EYE, during an event. Every other frame in this
            // series is shot from 1.4 m or lower — i.e. from under the table — so
            // "does an easter egg intrude on the board" has never once been judged
            // from where the head is. These two reuse the ModBuild 148 head
            // stations unchanged (nothing is renamed or moved, so every previous
            // lane's evidence is still reproducible) on the biggest event in the
            // room.
            ("HeadC", 5, new Vector3(0.001f, 26f, 0.001f)),
            ("HeadBoardC", 5, new Vector3(0.001f, 26f, 0.001f)),
        };

        // THE WOOD DRAWS NO HAUNTS AT ALL, so this table is empty and that is the
        // finished state rather than a gap. Its three cards are all placeholders:
        // card 0 held the eyeshines and was deleted outright in ModBuild 149
        // ("Entferne den 'Augen' Effekt im Wald komplett inklusive aller sounds und
        // assets"), and cards 1 and 2 are played at runtime by real game monsters
        // that this harness cannot spawn. The array and its loop are kept rather
        // than special-cased away: the day the wood gets a drawn apparition again,
        // one row here is the whole change.
        private static readonly (string view, int card, Vector3 env)[] ForestHaunts =
            Array.Empty<(string, int, Vector3)>();

        // THE ELEMENT COMPENSATION, which is a requirement and therefore has to be
        // photographed rather than asserted: under full LIGHT a dark apparition
        // must gain contrast instead of washing out, and under full DARK it must
        // gain a rim instead of disappearing into a black room. Two extra frames
        // per room, on the event whose whole content is a silhouette.
        //   _GhvrElemA = (Fire, Ice, Air, Earth), _GhvrElemB = (Light, Dark, Master, Peak)
        private static readonly (string tag, Vector4 a, Vector4 b)[] HauntMoods =
        {
            ("light", Vector4.zero, new Vector4(1f, 0f, 1f, 1f)),
            ("dark", Vector4.zero, new Vector4(0f, 1f, 1f, 1f)),
            ("fire", new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 0f, 1f, 1f)),
            ("earth", new Vector4(0f, 0f, 0f, 1f), new Vector4(0f, 0f, 1f, 1f)),
        };

        // ============================================================ ELEMENT ART
        // The element response cannot be reviewed from the ordinary set: with the
        // channel unset every element term is skipped by construction, which is
        // exactly what makes that set the ZERO-STATE BASELINE and exactly why it
        // shows nothing. So the moods below are driven straight onto the two
        // globals ElementMood publishes at runtime — no debug path, no forced
        // material, the same uniforms the mod writes — and the room is shot again
        // at each of them.
        //
        // WHAT EACH ROW IS FOR:
        //  *S    Strong: the element ramped all the way to 1.0.
        //  *W    Waning: ElementMood's 0.40 plateau. The published value BREATHES
        //        0.28..0.52 on a 2.4 s cycle; a still frame cannot show a breath,
        //        so these are shot at the plateau itself and the breath is what
        //        the hardware round has to judge.
        //  split Light AND Dark at once — the user's own example, and the one
        //        mixture that is a requirement rather than a nicety.
        //  fice  Fire+Ice, aiend Air+Earth: the two layered mixtures.
        //  moff  THE ACCEPTANCE TEST. All six at full strength with the MASTER at
        //        0. It must come out pixel-identical to the plain frame of the
        //        same view: one uniform switches the whole feature off, and if it
        //        does not, one of the shaders has forgotten to multiply.
        //  live0 All six INERT with the master UP — the other half of the same
        //        proof: the feature is on and nothing is up, so nothing may move.
        //   _GhvrElemA = (Fire, Ice, Air, Earth), _GhvrElemB = (Light, Dark, Master, Peak)
        private static readonly (string tag, Vector4 a, Vector4 b)[] ElementMoods =
        {
            ("fireS", new Vector4(1, 0, 0, 0), new Vector4(0, 0, 1, 1)),
            ("fireW", new Vector4(0.40f, 0, 0, 0), new Vector4(0, 0, 1, 0.40f)),
            ("iceS", new Vector4(0, 1, 0, 0), new Vector4(0, 0, 1, 1)),
            ("iceW", new Vector4(0, 0.40f, 0, 0), new Vector4(0, 0, 1, 0.40f)),
            ("airS", new Vector4(0, 0, 1, 0), new Vector4(0, 0, 1, 1)),
            ("airW", new Vector4(0, 0, 0.40f, 0), new Vector4(0, 0, 1, 0.40f)),
            ("earthS", new Vector4(0, 0, 0, 1), new Vector4(0, 0, 1, 1)),
            ("earthW", new Vector4(0, 0, 0, 0.40f), new Vector4(0, 0, 1, 0.40f)),
            ("lightS", Vector4.zero, new Vector4(1, 0, 1, 1)),
            ("lightW", Vector4.zero, new Vector4(0.40f, 0, 1, 0.40f)),
            ("darkS", Vector4.zero, new Vector4(0, 1, 1, 1)),
            ("darkW", Vector4.zero, new Vector4(0, 0.40f, 1, 0.40f)),
            ("split", Vector4.zero, new Vector4(1, 1, 1, 1)),
            ("fice", new Vector4(1, 1, 0, 0), new Vector4(0, 0, 1, 1)),
            ("aiend", new Vector4(0, 0, 1, 1), new Vector4(0, 0, 1, 1)),
            // ---- THE FIVE FIRE PAIRINGS (ModBuild 145) -------------------
            // USER: "Schau dir auch jede mögliche Kombination der Elemente an
            // und schau das jede der Effekte in beiden Umgebungen entsprechend
            // sinnvoll miteinander interagiert. So zB das das Feuer der
            // brennenden Bäume noch mehr Glut wirft und flackert wenn Wind an
            // ist etc."
            //
            // Each row is FIRE AT FULL plus one other at full, which is the
            // state the pair term is defined at — and each has to be judged
            // against BOTH of its own single-element rows above (fireS and the
            // other one), because the whole claim is that the pair does
            // something neither element does alone. `fice` above is already
            // Fire+Ice and is kept under its old name so the previous round's
            // frames remain comparable; the four new ones follow its pattern.
            //   _GhvrElemA = (Fire, Ice, Air, Earth)
            //   _GhvrElemB = (Light, Dark, Master, Peak)
            ("fair", new Vector4(1, 0, 1, 0), new Vector4(0, 0, 1, 1)),
            ("fdark", new Vector4(1, 0, 0, 0), new Vector4(0, 1, 1, 1)),
            ("flight", new Vector4(1, 0, 0, 0), new Vector4(1, 0, 1, 1)),
            ("fearth", new Vector4(1, 0, 0, 1), new Vector4(0, 0, 1, 1)),
            // ...and the composition test, which is the OTHER half of the
            // requirement: three elements at once must read as ONE fire that is
            // windblown and alone, not as three effects stacked. If the
            // composition rule (see EnvFire.cginc) is doing its job, this frame
            // is recognisably fair and fdark at the same time and nothing else.
            ("fairdark", new Vector4(1, 0, 1, 0), new Vector4(0, 1, 1, 1)),
            // ...and everything at once, with Fire in it: the worst case for
            // legibility, and the frame that says whether fifteen pairs can be
            // up together without becoming mud.
            ("fall", new Vector4(1, 1, 1, 1), new Vector4(1, 1, 1, 1)),
            ("moff", new Vector4(1, 1, 1, 1), new Vector4(1, 1, 0, 1)),
            ("live0", Vector4.zero, new Vector4(0, 0, 1, 0)),
        };

        // Four frames per room, chosen for what they contain rather than for
        // coverage: a lit corner (the candles / the moonlit clearing edge), the
        // darkest corner there is (where Dark has to be visible AS a change), the
        // one wet surface / the tree line, and a sky frame. Rendering the whole
        // 40-view set at 17 moods would be 1400 PNGs nobody reads.
        // ...plus one plain eye-height view straight across each room: the gated
        // EMITTERS live in a ring at the periphery, and none of the four framed
        // views above looks along that ring. The first element pass could not
        // tell "the embers are too small" from "the embers are behind me".
        //
        // ModBuild 144 adds four, and each of them is a frame in which a
        // specific line of the last verdict is either honoured or not:
        //   IceClose/IceTop  "es sollte mehr wie Eis rüberkommen" — the puddle
        //                    filling the frame, from the side and from above.
        //   BeamSide         "bei Licht viel intensiver / bei Dunkelheit extrem
        //                    reduziert" and the draught inside the lit volume.
        //   ShaftMoon        "die Lichtstrahlen verschwinden" — the shafts and
        //                    the moon that casts them, in one picture, so the
        //                    cause and the effect are judged together.
        //
        // ModBuild 145 adds the FIRE frames, and they belong here rather than in
        // the plain set for a structural reason: the fires exist only under the
        // Fire infusion, so at the zero state every one of them is collapsed to a
        // point and a plain frame of them is a picture of an empty corner.
        private static readonly string[] CellarElementViews =
        { "Corner", "DarkCornerSW", "Puddle", "Window", "S", "IceClose", "IceTop", "BeamSide",
          "FireCrate", "FireCrateLow", "FireSpill", "FireSpillLow", "FireShelf", "FireShelfLow",
          "FireRoom", "FireSpillGraze",
          // ...and the room under Fire from the head the player actually has
          // (THE HEAD SET): the fires are peripheral by design, and whether that
          // reads is a property of the eye height it is judged from.
          "HeadC", "HeadBoardC" };
        private static readonly string[] ForestElementViews =
        { "TreeLine", "FloorToMoon", "Fireflies", "SkyBand", "N", "ShaftMoon",
          "FireSnag", "FireSnagWide", "FireLog", "FireBrush", "FireWood",
          "FireLogGraze", "FireLogGrazeB", "FireSnagGraze", "FireSnagGrazeLow",
          // ...and the wood under Fire from the head the player actually has
          // (THE HEAD SET). This is the room whose 1.4 m station is BELOW the
          // board, so it is the one where the difference is structural.
          "HeadS", "HeadBoardS" };

        // The animated things only exist in motion, so the review set below is
        // ALSO rendered at these offsets of the shared shader clock
        // (_GhvrTimeOfs, see EnvRoom.shader). The values are picked against the
        // cellar's own constants, not round numbers — the drip cycle is 2.85 s
        // with the drop released at 1.55 s and landing at 2.363 s, and the rat
        // runs 4.6 s of every 31 starting at t=0:
        //   t0 0.00  drop starts forming;      rat leaving its hole
        //   t1 0.85  drop hanging, half grown; rat u=0.18
        //   t2 1.15  drop hanging, nearly full;rat u=0.25, IN THE MOONBEAM
        //   t3 1.75  drop falling (0.20 s);    rat u=0.38, out of it again
        //   t4 2.20  drop falling (0.65 s);    rat u=0.48
        //   t5 2.55  JUST LANDED (+0.19 s):    splash up, first ring running out
        //   t6 4.20  next drop hanging;        rat u=0.91, going into the hole
        private static readonly (string tag, float t)[] TimeSteps =
        {
            ("t0", 0.00f), ("t1", 0.85f), ("t2", 1.15f), ("t3", 1.75f),
            ("t4", 2.20f), ("t5", 2.55f), ("t6", 4.20f),
        };
        // ...and the frames worth repeating across those offsets. Rendering all
        // 30-odd views six times over is 200 PNGs nobody reads.
        private static readonly string[] TimeViews =
        {
            "Corner", "CandleTable", "CandleCrate", "Puddle", "PuddleLow",
            "DripColumn", "WindowClose", "RatRun", "RatBeam", "RatLow", "RatEnd", "DarkCornerSW",
        };

        // ====================================================== FIRE PHASE ======
        // ModBuild 147. THE HARNESS HAD NO WAY TO SEE A FIRE MOVE, and that gap
        // is not academic: it is how a purely TEMPORAL fire fault — every band
        // driving the wrong size of structure, 19 % of the brightness parked at
        // the one frequency the eye is most sensitive to — survived a whole
        // review round and had to be caught on hardware ("das Feuer zappelt viel
        // zu schnell", ModBuild 145). Until now a fire appeared in the element
        // series at ONE instant (_GhvrTimeOfs 3.7) and nowhere else, so every
        // claim ever made about it from a preview was a claim about a still.
        //
        // WHY TWO SERIES AND NOT ONE, and the sampling is arithmetic rather than
        // taste. A fire has no single rate; EnvFire.cginc's own table gives the
        // four bands at FireHz 4.6 as 4.60 / 2.81 / 7.96 / 1.08 Hz, and on top of
        // that a puff runs its whole birth-rise-redden-die cycle at _PuffHz 0.55
        // Hz, i.e. once per 1.82 s. One evenly spaced series cannot cover a
        // 7.4:1 span: at a step short enough to resolve 8 Hz it spans a fifth of
        // the swell, and at a step long enough to span the puff cycle it aliases
        // everything above 2 Hz into nonsense.
        //
        //   FAST  8 frames, 55 ms apart, spanning 0.385 s.
        //         55 ms is 2.27 samples per cycle of the 7.96 Hz band (just above
        //         Nyquist, which is the whole point — this is the band the
        //         "zappeln" verdict was about, and it must be possible to SEE it
        //         alias) and 3.95 per cycle of the 4.60 Hz one. This is the
        //         series in which a tongue's tip and the texture wrinkle move.
        //   SLOW  8 frames, 240 ms apart, spanning 1.68 s.
        //         3.86 samples per cycle of the 1.08 Hz swell and 92 % of one
        //         puff cycle, so a detached piece can be followed from the frame
        //         it leaves the body to the frame it has gone out. Nothing below
        //         2 Hz is resolvable in the FAST series at all.
        //
        // Both are shot with FIRE AT FULL, because with the channel unset every
        // flame card is collapsed to a point in the vertex shader and the frames
        // would be pictures of an empty corner.
        private static readonly float[] FirePhasesFast =
        { 0.000f, 0.055f, 0.110f, 0.165f, 0.220f, 0.275f, 0.330f, 0.385f };
        private static readonly float[] FirePhasesSlow =
        { 0.00f, 0.24f, 0.48f, 0.72f, 0.96f, 1.20f, 1.44f, 1.68f };

        // Four cellar sites and three forest ones — one per burning THING rather
        // than one per fire, and each already framed so that one fire fills a
        // third of the picture (see the ModBuild 145 block above for why a wide
        // room shot cannot settle anything about a fire). Sixteen offsets over
        // seven views is 112 PNGs; the whole fire view list at both series would
        // have been 176 that nobody reads.
        // ...plus the ModBuild 148 GRAZING set (see the block where they are
        // defined). They are in the phase series and not only in the plain one
        // because two of the three faults being fixed are temporal: a card that
        // fades as the view crosses its plane and a mask that dissolves are both
        // claims about a sequence, and a still cannot carry either.
        private static readonly string[] CellarFireViews =
        { "FireCrate", "FireCrateLow", "FireSpill", "FireShelf", "FireSpillGraze" };
        private static readonly string[] ForestFireViews =
        { "FireSnag", "FireLog", "FireBrush",
          "FireLogGraze", "FireLogGrazeB", "FireSnagGraze", "FireSnagGrazeLow" };

        // ...and the WIND, which is the user's own second question this round
        // ("Kann es dann trotzdem mit dem wind reagieren?"). Fire+Air is a pair
        // term that lives entirely in RATE and AMPLITUDE — GhvrFireHz multiplies
        // the clock by 1.55 and GhvrFireDepth the flicker depth by 1.70 — so it
        // is invisible in a still by construction, and a claim that it works
        // cannot be read off one frame. The FAST series is repeated at full
        // Fire+Air on one view per room: against the plain fireS frames at the
        // same eight offsets, the flames must be visibly further through their
        // cycle at every step, and the wash on the stone must swing wider.
        private static readonly string[] CellarWindViews = { "FireCrate" };
        private static readonly string[] ForestWindViews = { "FireSnag" };

        // ...and THE SHELF RIDERS, which until now nothing in this harness could
        // photograph at all — a hole exactly as big as the fire phase one it sits
        // next to. Two of the cellar's six fires and one of its halos are seated
        // ON the bookshelf that topples, and they ride it rigidly
        // (EnvShelfTip/_FireRide, EnvRoomBuilder.RideShelf). But the haunt series
        // runs with the element channel UNSET, so in every frame of it the fire
        // is collapsed to a point; and the fire series runs with the haunt unset,
        // so in every frame of that the shelf is standing. The one state that has
        // to be checked — a burning bookshelf going over, still burning — was in
        // neither. A rider that came loose would have rendered as a fire hanging
        // in the air where the shelf used to be, and nothing here would have
        // caught it.
        //
        // Three phases off HauntShelfPhases, chosen for the trajectory rather
        // than the envelope: 0.08 barely off plumb (the fire is where it was),
        // 0.16 the fast part of the topple (it must be leaning WITH the boards),
        // 0.30 down just after the rebound (it must be lying with them).
        //
        // THE WALL WASH IS THE OTHER HALF OF WHAT THIS SERIES IS FOR, and the
        // ModBuild 152 version of this comment said the wrong thing about it: "the
        // wall wash deliberately does NOT ride and must stay put across all three
        // — that is an authored asymmetry, not a fault." It was a fault, the user
        // filed it off hardware the same round ("Da wo die Funken waren und
        // deaktiviert wurden ist aber immer noch eine Lichtquelle die dort
        // scheint"), and this series is where it was visible all along: at phase
        // 0.30 the frame is a bookcase on the floor under a metre-wide oval of
        // firelight still burning on the wall above it. It still does not RIDE —
        // that would swing a sphere of light through masonry — it FADES, so what
        // these frames must now show is the oval going to nothing by the arrival
        // (0.18) and coming back over the righting (0.62 onward).
        //
        // ---- AND THE WHOLE EVENT SINCE ModBuild 152 -------------------------
        // The three phases above are enough to judge a RIDER (does the fire lean
        // with the boards?) and cannot judge a FADE, which is a claim about a
        // curve: it needs the standing state, the lean, the arrival, the lie-down
        // and both ends of the righting in one comparable series. USER: "Um es
        // einfach zu halten: Deaktivier die Funken einfach (ausfaden) wenn das
        // Regal kippt." — so the sparks off the burning bookcase now fade with
        // the pose, and the frames that prove it are these.
        //
        // The added instants are the landmarks EnvShelfTip and EnvSound already
        // name, not new ones: 0.00 upright, 0.12 the lean where the fade begins,
        // 0.180 THE ARRIVAL (the sparks must be gone in this frame), 0.204 the
        // rebound, 0.45 the middle of the lie-down, 0.620 THE RIGHTING (still
        // gone), 0.80 and 0.90 on the way back up, 1.00 upright again — which
        // must be the same picture as 0.00.
        private static readonly float[] FireShelfRidePhases =
            { 0f, 0.08f, 0.12f, 0.16f, 0.18f, 0.204f, 0.30f, 0.45f, 0.62f, 0.70f,
              0.80f, 0.90f, 1f };

        // =============== THE INSTRUMENT CHECKS ITSELF (ModBuild 153) ============
        // Every station in this harness that is supposed to see the bookcase, and
        // the smallest share of the frame the bookcase may occupy in it before the
        // run FAILS. `minPct` is deliberately generous — this is not a framing
        // policy, it is the difference between "photographing the prop" and
        // "photographing the wall where the prop used to be", which are 15 % and
        // 0 % and have nothing in between.
        //
        // WHY IT IS MEASURED AND NOT ARGUED. The stations above are derived from
        // EnvRoomBuilder.CellarShelfAt, which is a good argument and not a
        // measurement: it is right about where the prop is ANCHORED and says
        // nothing about how big it is, whether it imported, or whether something
        // was placed in front of it. This finds the real Renderer in the loaded
        // prefab and projects its real world bounds through the real camera. The
        // rectangle it prints is the answer to "name the pixel region the bookcase
        // occupies", which is the question a reviewer has to be able to answer
        // before citing a frame at all.
        private static readonly (string view, float minPct)[] ShelfStations =
        {
            ("HauntShelf", 1.0f),      // the three-quarter review station
            ("HauntShelfOff", 0.2f),   // from across the room: small on purpose
            ("HauntShelfRide", 1.0f),  // along the sweep plane
            ("HauntWideC", 0.3f),      // the player's own wide frame
            ("HauntFarC", 0.3f),       // from the board
            ("FireShelf", 3.0f),       // the fire close-ups
            ("FireShelfLow", 3.0f),
        };

        /// <summary>Fail the run if a station that is supposed to be looking at the
        /// tipping bookcase is not, and print the pixel rectangle it covers in the
        /// ones that are. Uses the caller's camera, so it is checking the exact
        /// projection Shoot() will use.</summary>
        private static void AssertShelfStations(GameObject inst, Camera cam)
        {
            MeshFilter shelf = null;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                if (mf.gameObject.name == "Shelf") { shelf = mf; break; }
            if (shelf == null || shelf.sharedMesh == null)
                throw new Exception("The cellar prefab has no mesh called 'Shelf'. Either the "
                    + "tipping bookcase failed to build, or it was renamed — in which case every "
                    + "station below is now aimed at a hole in the room and would have gone on "
                    + "rendering happily.");
            // THE VERTICES AND NOT Renderer.bounds, and this cost a run: the
            // bookcase's fall is a VERTEX ROTATION, so its mesh bounds are inflated
            // to the volume it sweeps (4.70 x 6.19 x 5.50 m) to keep it out of the
            // frustum culler mid-topple. Measured against those, every station
            // below "covered 100 % of the frame" — including the one that was
            // 65 degrees off the prop. An instrument that measures a culling volume
            // agrees with you exactly as enthusiastically as one that measures
            // nothing. This is the STANDING CARCASS, built the way the bake builds
            // it (EnvRoomBuilder's WorldVerts): the authored vertices through the
            // object's own transform.
            var xf = shelf.transform;
            var verts = shelf.sharedMesh.vertices;
            if (verts == null || verts.Length == 0)
                throw new Exception("The 'Shelf' mesh has no vertices to measure.");
            var b = new Bounds(xf.TransformPoint(verts[0]), Vector3.zero);
            for (int i = 1; i < verts.Length; i++) b.Encapsulate(xf.TransformPoint(verts[i]));
            var log = new System.Text.StringBuilder();
            log.Append("[GloomhavenVR][EnvPreview] SHELF STATIONS — measured against the real "
                       + $"prop, whose standing bounds are ({b.min.x:F2},{b.min.y:F2},{b.min.z:F2})"
                       + $"..({b.max.x:F2},{b.max.y:F2},{b.max.z:F2}). A station that points at "
                       + "nothing does not fail; it renders, and it agrees with you — so this "
                       + $"fails instead. Frame is {W}x{H}, pixel rows counted from the TOP.\n");
            // THE ASPECT HAS TO BE SAID OUT LOUD. Camera.fieldOfView is VERTICAL, so
            // the horizontal extent comes from cam.aspect — and in batch mode an
            // un-driven camera's aspect is the phantom 640x480 screen's 1.333, not
            // the 1280x720 render target's 1.778. Left alone, this check measured a
            // frame 33 % narrower than the one Shoot() actually writes: it put the
            // bookcase at x 469..815 in a PNG where it really spans 511..772. The
            // vertical numbers were right all along, which is exactly what makes
            // that kind of error survive a read-through.
            cam.aspect = W / (float)H;
            foreach (var (vn, minPct) in ShelfStations)
            {
                var v = Array.Find(Views, x => x.name == vn);
                if (v.name == null)
                    throw new Exception($"ShelfStations names an unknown view '{vn}'.");
                cam.fieldOfView = v.fov;
                cam.transform.position = v.pos;
                cam.transform.rotation = Quaternion.Euler(v.euler);
                float x0 = float.MaxValue, x1 = float.MinValue;
                float y0 = float.MaxValue, y1 = float.MinValue;
                int behind = 0;
                for (int k = 0; k < 8; k++)
                {
                    var c = new Vector3((k & 1) == 0 ? b.min.x : b.max.x,
                                        (k & 2) == 0 ? b.min.y : b.max.y,
                                        (k & 4) == 0 ? b.min.z : b.max.z);
                    var vp = cam.WorldToViewportPoint(c);
                    if (vp.z <= 0f) { behind++; continue; }
                    x0 = Mathf.Min(x0, vp.x * W); x1 = Mathf.Max(x1, vp.x * W);
                    y0 = Mathf.Min(y0, (1f - vp.y) * H); y1 = Mathf.Max(y1, (1f - vp.y) * H);
                }
                if (behind == 8)
                    throw new Exception($"Preview station '{vn}' has the whole bookcase BEHIND it. "
                        + "It is not photographing the prop it is listed for.");
                var ctr = cam.WorldToViewportPoint(b.center);
                float cx = ctr.x * W, cy = (1f - ctr.y) * H;
                float cw = Mathf.Max(Mathf.Min(x1, W) - Mathf.Max(x0, 0f), 0f);
                float ch = Mathf.Max(Mathf.Min(y1, H) - Mathf.Max(y0, 0f), 0f);
                float pct = 100f * cw * ch / (W * (float)H);
                bool ctrIn = ctr.z > 0f && ctr.x >= 0f && ctr.x <= 1f && ctr.y >= 0f && ctr.y <= 1f;
                if (!ctrIn || pct < minPct)
                    throw new Exception($"Preview station '{vn}' at ({v.pos.x:F2},{v.pos.y:F2},"
                        + $"{v.pos.z:F2}) yaw {v.euler.y:F1} fov {v.fov:F0} does not see the "
                        + $"bookcase: its centre projects to ({cx:F0},{cy:F0}) which is "
                        + $"{(ctrIn ? "in" : "OUTSIDE")} the frame, and the carcass covers "
                        + $"{pct:F2} % of it against a floor of {minPct:F2} %. This is the "
                        + "ModBuild 149 lesson and the ModBuild 152 one: a station that points at "
                        + "nothing renders happily and agrees with whatever you already believed. "
                        + "Re-derive it from EnvRoomBuilder.CellarShelfAt (see LookAtShelf / "
                        + "ShelfStand) rather than nudging the numbers.");
                log.Append($"    {vn,-14} centre at pixel ({cx,4:F0},{cy,4:F0}), carcass covers "
                           + $"x {Mathf.Max(x0, 0f),4:F0}..{Mathf.Min(x1, W),4:F0}, y "
                           + $"{Mathf.Max(y0, 0f),4:F0}..{Mathf.Min(y1, H),4:F0} = {pct,5:F2} % of "
                           + $"the frame ({Vector3.Distance(v.pos, b.center):F2} m away"
                           + (behind > 0 ? $", {behind}/8 corners behind the camera)" : ")") + "\n");
            }
            // ...and hand the camera back exactly as it was found: Shoot() relies
            // on the aspect being driven by the render target, and an aspect that
            // is set once stays set.
            cam.ResetAspect();
            Debug.Log(log.ToString());
        }

        // =========== THE NEAR APPROACH TO THE FALLEN CANDLE (ModBuild 154) =====
        // The three nodes the shelf candle is made of. They are three renderers
        // with three separate culling volumes, which is the whole of the ModBuild
        // 153 fault: the carcass's box had been padded and theirs had not.
        private static readonly string[] ShelfCandleNodes =
            { "CandlesShelf", "FlameShelf0", "CandleGlowShelf" };

        /// The phases this series is shot at, and the shelf angle at each — and the
        /// angle is not sampled from a curve, it is READ OFF THE SCHEDULE. Anything
        /// in 0.204..0.620 is the LIE-DOWN, where GhvrShelfTip is pinned at exactly
        /// 1 (GhvrTipArc is flat past ARC and the rebound window has closed), so the
        /// angle there is exactly _TipAxis.w. 0.00 is the rest state, exactly 0.
        private static readonly (float phase, bool fallen)[] ShelfNearPhases =
            { (0.00f, false), (0.30f, true), (0.50f, true) };

        /// How far along the walk each station stands: 0 is a player's head at the
        /// far end, 1 is leaning right over the thing. Nine of them, closing up
        /// toward the end, because that is where the frustum gets narrow and where
        /// the report says the candle goes.
        private static readonly float[] ShelfNearSteps =
            { 0f, 0.25f, 0.45f, 0.60f, 0.72f, 0.82f, 0.90f, 0.96f, 1.00f };

        /// <summary>Walk a camera in to the shelf candle and COUNT THE PIXELS it
        /// covers at each distance, at the pose it is upright in and at the pose it
        /// is lying on the floor in.
        ///
        /// <para>USER, hardware, ModBuild 153: "Wenn das Bücherregal umkippt und man
        /// dann nah an die Kerze herangeht verschwindet sie! Wenn ich eine
        /// bestimmte Distanz erreiche sogar nur auf einem Auge."</para>
        ///
        /// <para>THE POSE IS READ OFF THE SHIPPED MATERIAL and not re-derived: the
        /// wax carries _TipPivot and _TipAxis in its own object space, written by
        /// the one writer (EnvRoomBuilder.WriteShelfTip), so this camera cannot
        /// disagree with the shader about where the candle ends up however the prop
        /// moves. That is the ModBuild 149/153 lesson — a station aimed by hand
        /// photographs the wall where the prop used to be, and renders happily.</para>
        ///
        /// <para>WHAT THE NUMBERS MEAN. Each frame's window is the projection of a
        /// 0.60 m box on the candle; inside it the harness counts pixels above a
        /// fixed threshold and sums their luminance. Walking towards something can
        /// only make it bigger, so in a series where nothing is culled the count
        /// rises monotonically. A count that COLLAPSES between two stations of one
        /// phase — while the candle is still in front of the camera — is a renderer
        /// leaving the frustum on a box its geometry has already left, i.e. exactly
        /// the reported fault. The harness cannot see the ONE-EYE half of the
        /// report: it renders one monoscopic camera, and the band in which one
        /// eye's frustum contains the stale box and the other's does not is a
        /// property of two frusta. What it CAN settle is whether the box is stale at
        /// all, and that is the same cause.</para></summary>
        private static void ShelfNearApproach(GameObject inst, Camera cam, Texture2D tex,
            Action<string, Vector3, Vector3, bool, float, string> shoot, Action<float> stepEmitters)
        {
            // ---- THE GROUP UNDER TEST IS DISCOVERED, NOT LISTED: every renderer in
            // the room whose material declares _TipUse.x = 1, i.e. everything whose
            // geometry the shelf's vertex rotation moves. That is the carcass, the
            // candle's wax, its flame, its halo, the two bay fires and the near
            // fire halo — seven renderers with seven separate culling volumes, and
            // the ModBuild 153 fault was that ONE of them (the carcass) had been
            // padded and the other six had not. Listing them here by name would be
            // a second copy of the bake's own answer sheet; reading the flag off the
            // shipped material cannot drift from it.
            var parts = new List<(string name, Renderer r, Bounds vb)>();
            Material poseMat = null; Transform poseXf = null;
            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mat = mr.sharedMaterial;
                if (mat == null || !mat.HasProperty("_TipUse") || !mat.HasProperty("_TipPivot")) continue;
                if (mat.GetVector("_TipPivot").w < 0.5f || mat.GetVector("_TipUse").x < 0.5f) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var v = mf.sharedMesh.vertices;
                var b = new Bounds(mr.transform.TransformPoint(v[0]), Vector3.zero);
                for (int i = 1; i < v.Length; i++) b.Encapsulate(mr.transform.TransformPoint(v[i]));
                parts.Add((mr.gameObject.name, mr, b));
                if (mr.gameObject.name == "CandlesShelf") { poseMat = mat; poseXf = mr.transform; }
            }
            // ...and the three the report is literally about must be among them, by
            // name, because a renamed one takes the default (0, litSlot, 0, 0) and
            // silently stops riding — which looks like nothing at all.
            foreach (var n in ShelfCandleNodes)
                if (parts.FindIndex(p => p.name == n) < 0)
                    throw new Exception($"No riding renderer called '{n}' in the cellar prefab. "
                        + "The shelf candle is three renderers (wax, flame, halo) and this series "
                        + "exists to prove all three survive being walked up to; a renamed one "
                        + "drops out of the count and the series goes on agreeing with whatever "
                        + "was already believed.");
            if (poseMat == null || !poseMat.HasProperty("_TipPivot"))
                throw new Exception("The shelf candle's wax carries no _TipPivot, so this harness "
                    + "cannot know where the candle ends up. Either WriteShelfTip stopped being "
                    + "called for it or EnvRoom lost the property — both of which mean the wax "
                    + "does not fall with the shelf at all.");
            var pv = poseMat.GetVector("_TipPivot");
            var ax = poseMat.GetVector("_TipAxis");
            if (pv.w < 0.5f)
                throw new Exception("The shelf candle's wax carries a DEAD pose (_TipPivot.w = 0): "
                    + "it does not ride the bookshelf, so there is no fallen candle to walk up to.");
            var pivotW = poseXf.TransformPoint(new Vector3(pv.x, pv.y, pv.z));
            var axisW = poseXf.TransformDirection(new Vector3(ax.x, ax.y, ax.z)).normalized;
            float maxAng = ax.w;

            // ---- ENV_PREVIEW_STALEBOUNDS=1: THE CONTROL RUN, and the only thing
            // that makes the numbers below evidence rather than a photograph of a
            // room that happens to look fine. It puts the three candle meshes back
            // on the box their own VERTICES occupy — i.e. exactly the state the bake
            // shipped before the swept volume existed — so the series can be run
            // twice and the two tables compared. An instrument that cannot produce
            // the fault on demand cannot be said to have found it absent.
            if (Environment.GetEnvironmentVariable("ENV_PREVIEW_STALEBOUNDS") == "1")
            {
                foreach (var (n, r, _) in parts)
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    var v = mesh.vertices;
                    var vb = new Bounds(v[0], Vector3.zero);
                    for (int i = 1; i < v.Length; i++) vb.Encapsulate(v[i]);
                    mesh.bounds = vb;
                    Debug.Log($"[GloomhavenVR][EnvPreview] STALEBOUNDS: '{n}' put back on its own "
                              + $"vertex box {vb.size.x:F2} x {vb.size.y:F2} x {vb.size.z:F2} m — "
                              + "the pre-ModBuild-154 culling volume.");
                }
            }

            // WHERE THE CAMERA LOOKS is the CANDLE's own three renderers and not the
            // whole rider set: the report is "man geht nah an die Kerze heran", and
            // a frame centred on the 2 m carcass would put the candle in a corner
            // of it. What is MEASURED is still the whole rider set.
            var standing = new Bounds();
            bool first = true;
            foreach (var (n, _, vb) in parts)
                if (Array.IndexOf(ShelfCandleNodes, n) >= 0)
                { if (first) { standing = vb; first = false; } else standing.Encapsulate(vb); }
            // the halo's own world radius, measured off its vertices and its
            // transform — see the stand-off below.
            float haloR = 0f;
            foreach (var (n, _, vb) in parts)
                if (n == "CandleGlowShelf")
                    haloR = Mathf.Max(vb.extents.x, Mathf.Max(vb.extents.y, vb.extents.z));
            var log = new System.Text.StringBuilder();
            log.Append("[GloomhavenVR][EnvPreview] SHELF CANDLE — THE NEAR APPROACH. User, "
                + "hardware, ModBuild 153: \"Wenn das Bücherregal umkippt und man dann nah an die "
                + "Kerze herangeht verschwindet sie! Wenn ich eine bestimmte Distanz erreiche "
                + "sogar nur auf einem Auge.\" A fixed station cannot show this — the fault is a "
                + "function of DISTANCE — so this walks the camera in and counts pixels. The "
                + $"candle stands at ({standing.center.x:F2},{standing.center.y:F2},"
                + $"{standing.center.z:F2}) and hinges at ({pivotW.x:F2},{pivotW.y:F2},"
                + $"{pivotW.z:F2}) about ({axisW.x:F2},{axisW.y:F2},{axisW.z:F2}) through "
                + $"{maxAng * Mathf.Rad2Deg:F1} deg — all four read off the SHIPPED material, not "
                + "typed here. Walking towards a thing can only make it bigger, so a count that "
                + "collapses between two stations is a renderer being culled on a box its own "
                + "vertex program has already left.\n"
                + $"    {parts.Count} renderers ride this pose and all {parts.Count} are measured, "
                + "BY DIFFERENCE — the same station rendered with them on and off — because the "
                + "total light in a window is mostly the wall behind it and rises as the camera "
                + "closes whether they are drawn or not. Measuring the CANDLE alone would not "
                + "settle anything either: it is designed to gutter out while the shelf is over, "
                + "so its own three renderers are legitimately dark at the fallen poses and a "
                + "difference on them reads zero whether they are culled or not. The walk stops "
                + $"{haloR + 0.20f:F2} m out: the halo is a {haloR:F2} m additive shell with Cull "
                + "Back, so a camera inside it sees only its far side and the glow goes out. That "
                + "is a real thing a player can do to this candle and it is filed here as a "
                + "SEPARATE finding — it happens with the bookshelf standing, so it is not the "
                + "'nur beim umgekippten Bücherregal' report.\n");

            foreach (var (phase, fallen) in ShelfNearPhases)
            {
                float ang = fallen ? maxAng : 0f;
                var at = Rodrigues(standing.center, pivotW, axisW, ang);
                // the walk: from a standing head 2.6 m off, in to a head leaning
                // right over it. Both ends are in the candle's own frame, so they
                // follow the prop.
                var away = new Vector3(-axisW.z, 0f, axisW.x);   // out of the wall, horizontally
                if (Vector3.Dot(away, Vector3.zero - at) < 0f) away = -away;  // toward the room
                var from = at + away * 2.60f + Vector3.up * (1.55f - at.y);
                // ...and the walk STOPS OUTSIDE THE HALO, which is a measured
                // property of the shipped candle and not a fudge. The halo is an
                // additive shell of radius `haloR` drawn with Cull Back: a camera
                // INSIDE it sees only its far side, which is back-facing, so the
                // glow goes out. That is a real thing a player can do and it is
                // worth its own line (see the log below), but it is a different
                // mechanism from frustum culling, it happens whether the shelf is
                // over or not, and mixing the two into one series would make the
                // number this series exists to produce unreadable. The stand-off is
                // the halo's own radius read off the renderer, plus 0.20 m.
                var to = at + away * (haloR + 0.20f) + Vector3.up * 0.22f;

                // ---- FIRE IS INFUSED FOR THIS SERIES, and that is a measurement
                // decision with a reason. The shelf candle is DESIGNED to gutter
                // out while the shelf is over (GhvrTipFlameLife: fully out past
                // 35.5 degrees of tilt), so at the fallen poses the flame and the
                // halo draw nothing and the wax is an unlit dark cylinder on a dark
                // flagstone floor — a difference measurement on that reads zero
                // whether the renderer is culled or not, which would make this
                // instrument agree with whatever was already believed. The burning
                // bookcase beside it is what puts light on the wax, and it is also
                // the state the report is about: every term at this site belongs to
                // a shelf that is on fire.
                Shader.SetGlobalVector("_GhvrElemA", new Vector4(1f, 0f, 0f, 0f));
                Shader.SetGlobalVector("_GhvrElemB", new Vector4(0f, 0f, 1f, 1f));
                Shader.SetGlobalVector("_GhvrHaunt", new Vector4(1f, 1f, 0f, 0f));
                Shader.SetGlobalFloat("_GhvrTimeOfs",
                    EnvRoomBuilder.HauntPreviewClock(5, 6, 0.001f, 26f, 0.001f, phase));
                stepEmitters(phase * 26.002f);
                log.Append($"    phase {phase:F2} ({(fallen ? "ON THE FLOOR" : "upright")}), the "
                    + $"candle at ({at.x:F2},{at.y:F2},{at.z:F2}):\n");
                // one column per rider: the distance at which it stopped being drawn
                // at all, and how many of the frame's pixels it changed at its best.
                var gone = new float[parts.Count];
                var culled = new float[parts.Count];
                var best = new int[parts.Count];
                for (int i = 0; i < gone.Length; i++) { gone[i] = -1f; culled[i] = -1f; }
                foreach (float s in ShelfNearSteps)
                {
                    var pos = Vector3.Lerp(from, to, s);
                    var dir = (at - pos).normalized;
                    var euler = new Vector3(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg,
                                            Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
                    float dist = Vector3.Distance(pos, at);
                    shoot("ShelfNear", pos, euler, false, 60f,
                          $"_p{Mathf.RoundToInt(phase * 100f):D2}_d{Mathf.RoundToInt(dist * 100f):D3}");
                    // THE ASPECT HAS TO BE SAID OUT LOUD (ModBuild 153): in batch
                    // mode an un-driven camera reports the phantom 640x480 screen's
                    // 1.333, not the render target's 1.778, and a window measured
                    // with the wrong one is 33 % too narrow.
                    cam.aspect = W / (float)H;
                    var win = Project(cam, at, 0.30f);
                    cam.ResetAspect();
                    // ---- ONE RIDER AT A TIME, BY DIFFERENCE. The aggregate is no
                    // use: half of what rides is EMISSIVE (the flames, the halos)
                    // and half is a dark OCCLUDER (the carcass, the wax), the
                    // carcass covers most of the window whatever else happens, and
                    // the candle is DESIGNED to be out at these poses. Toggling one
                    // renderer at a time and counting the pixels it CHANGES — in
                    // either direction, because being drawn and being bright are
                    // different claims — is the only reading that says which of the
                    // seven is in the frame. Raw linear, not the PNG's gamma.
                    var on = ReadWindow(cam, tex, win);
                    // ...AND WHETHER THE CULLER CAN STILL SEE IT, which is the
                    // question the pixel count on its own cannot answer. A rider
                    // that changes no pixels may be culled (the fault) or may be
                    // legitimately dark or hidden behind the fallen carcass (the
                    // candle flame is DESIGNED to be out at these poses). The two
                    // are told apart by testing the same box Unity culls on against
                    // the same frustum it culls with. `cam.aspect` is pinned first
                    // for the ModBuild 153 reason: batch mode reports the phantom
                    // 640x480 screen's 1.333 and the frustum would be 33 % narrow.
                    cam.aspect = W / (float)H;
                    var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    cam.ResetAspect();
                    var line = new System.Text.StringBuilder();
                    for (int i = 0; i < parts.Count; i++)
                    {
                        bool inF = GeometryUtility.TestPlanesAABB(planes, parts[i].r.bounds);
                        parts[i].r.enabled = false;
                        var off = ReadWindow(cam, tex, win);
                        parts[i].r.enabled = true;
                        int px = 0;
                        for (int k = 0; k < on.px.Length; k++)
                            if (Mathf.Abs(on.px[k] - off.px[k]) > 0.004f) px++;
                        best[i] = Mathf.Max(best[i], px);
                        if (!inF && culled[i] < 0f) culled[i] = dist;
                        if (px == 0 && best[i] > 0 && gone[i] < 0f) gone[i] = dist;
                        if (px > 0 && gone[i] >= 0f) gone[i] = -2f;   // came back: not a clean cut
                        line.Append($"{parts[i].name} {px}{(inF ? "" : "/CULLED")}, ");
                    }
                    log.Append($"        {dist,5:F2} m away, window {win.width,4:F0}x{win.height,3:F0}"
                        + $" px at ({win.xMin,4:F0},{win.yMin,4:F0}): " + line.ToString().TrimEnd(' ', ',')
                        + "\n");
                }
                int faults = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (culled[i] > 0f)
                    {
                        faults++;
                        log.Append($"        -> {parts[i].name}: FRUSTUM-CULLED from {culled[i]:F2} m "
                            + "in, while its drawn geometry is straight ahead. That is the "
                            + "ModBuild 153 fault: the box Unity culls on is not where the vertex "
                            + "program put the pixels.\n");
                    }
                    else if (gone[i] > 0f)
                    {
                        faults++;
                        log.Append($"        -> {parts[i].name}: STOPPED BEING DRAWN at "
                            + $"{gone[i]:F2} m having covered {best[i]} px further out, with its "
                            + "box still inside the frustum. Not culling, then — look at ZWrite, "
                            + "the queue and the near plane.\n");
                    }
                    else if (best[i] == 0)
                        log.Append($"        -> {parts[i].name}: drew nothing at any distance, "
                            + "and its box was inside the frustum at every station — so it is "
                            + "not culled. At the fallen poses the candle's flame and halo are "
                            + "OUT by design (GhvrTipFlameLife is 0 past 35.5 deg of tilt) and "
                            + "the two bay fires are behind the carcass they are lying under. "
                            + "Stated rather than counted as a pass.\n");
                }
                if (faults == 0)
                    log.Append("        -> nothing in this pose is culled at any station on the "
                        + "way in. Every rider's box contains the pixels its vertex program "
                        + "draws.\n");
            }
            Shader.SetGlobalVector("_GhvrHaunt", Vector4.zero);
            Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
            Debug.Log(log.ToString());
        }

        /// Re-render the camera where it stands and read one window back RAW —
        /// linear, un-gamma'd, because what is being measured is drawn energy and
        /// not what a PNG looks like.
        private static (float sum, float[] px) ReadWindow(Camera cam, Texture2D tex, Rect win)
        {
            cam.Render();
            RenderTexture.active = cam.targetTexture;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            int x0 = (int)win.xMin, x1 = (int)win.xMax, y0 = (int)win.yMin, y1 = (int)win.yMax;
            int n = Mathf.Max((x1 - x0) * (y1 - y0), 0);
            var px = new float[n];
            float sum = 0f;
            int k = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var c = tex.GetPixel(x, H - 1 - y);
                    float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    px[k++] = l; sum += l;
                }
            return (sum, px);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// The SAME map EnvShelfTip's GhvrTipRot applies, written out for the same
        /// reason EnvRoomBuilder's landing gate writes it out: two libraries agreeing
        /// about handedness is a thing to check, not to assume.
        private static Vector3 Rodrigues(Vector3 p, Vector3 pivot, Vector3 axis, float ang)
        {
            var q = p - pivot;
            float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
            return pivot + q * c + Vector3.Cross(axis, q) * s + axis * (Vector3.Dot(axis, q) * (1f - c));
        }

        /// The pixel window a `r`-radius box about `at` occupies, clamped to frame.
        private static Rect Project(Camera cam, Vector3 at, float r)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            int behind = 0;
            for (int k = 0; k < 8; k++)
            {
                var c = at + new Vector3((k & 1) == 0 ? -r : r, (k & 2) == 0 ? -r : r,
                                         (k & 4) == 0 ? -r : r);
                var vp = cam.WorldToViewportPoint(c);
                if (vp.z <= 0f) { behind++; continue; }
                x0 = Mathf.Min(x0, vp.x * W); x1 = Mathf.Max(x1, vp.x * W);
                y0 = Mathf.Min(y0, (1f - vp.y) * H); y1 = Mathf.Max(y1, (1f - vp.y) * H);
            }
            if (behind == 8) return new Rect(0, 0, 0, 0);
            x0 = Mathf.Clamp(x0, 0f, W - 1f); x1 = Mathf.Clamp(x1, 0f, W - 1f);
            y0 = Mathf.Clamp(y0, 0f, H - 1f); y1 = Mathf.Clamp(y1, 0f, H - 1f);
            return new Rect(x0, y0, Mathf.Max(x1 - x0, 0f), Mathf.Max(y1 - y0, 0f));
        }

        // ---- THE RAT'S TWO MOUTHS, over the entry itself (ModBuild 143) -------
        // A crossing lasts 2.4-9.2 s of a 26 s slot and the entry is 0.45 s of
        // THAT, so the offsets above cannot land on it except by luck — which is
        // how "it shrinks instead of going in" survived two rounds of previews.
        // EnvRoomBuilder.RatPreviewClock SOLVES the shipped schedule for a given
        // point of a given hole's entry, the same way the haunt series solves for
        // an apparition, so there is no forced-visible code path here either.
        //
        // q is run progress: below 0 the animal is still in the hole, 1.0 is the
        // mouth, above that it is going down the burrow (2.0 = parked).
        // The north mouth gets BOTH series, and that is not redundancy: 43% of
        // crossings run the route backwards and 30% turn round, so the north hole
        // is walked into about as often as it is walked out of — and it is the
        // one of the two that is not behind a crate.
        private static readonly (string view, int hole, bool emerge)[] RatMouthViews =
        {
            ("RatHoleN", 0, true), ("RatHoleN", 0, false),
            ("RatHoleS", 1, false), ("RatHoleSWide", 1, false),
        };
        private static readonly (string tag, float q)[] RatEnterSteps =
        {
            ("q90", 0.90f), ("q100", 1.00f), ("q112", 1.12f), ("q125", 1.25f),
            ("q140", 1.40f), ("q160", 1.60f), ("q200", 2.00f),
        };
        private static readonly (string tag, float q)[] RatLeaveSteps =
        {
            ("e100", -1.00f), ("e060", -0.60f), ("e035", -0.35f), ("e015", -0.15f),
            ("e000", 0.00f), ("e010", 0.10f),
        };

        [MenuItem("GloomhavenVR/Render Environment Previews")]
        public static void RenderFromMenu() => Render();

        /// <summary>Batch entry — exits the editor 0/1.</summary>
        public static void RenderAll()
        {
            try
            {
                Render();
                Debug.Log("[GloomhavenVR][EnvPreview] RenderAll OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GloomhavenVR][EnvPreview] RenderAll FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static void Render()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new Exception("No graphics device — run WITHOUT -nographics (use xvfb-run on headless).");

            string outDir = Environment.GetEnvironmentVariable("ENV_PREVIEW_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = "env-previews";
            Directory.CreateDirectory(outDir);

            // Iteration filters (the FULL set is what a review is judged on —
            // these exist so that chasing one shaft does not cost 120 PNGs).
            //   ENV_PREVIEW_ENVS=Env_Cellar      only that prefab
            //   ENV_PREVIEW_VIEWS=Beam,Shaft     only views whose name starts
            //                                    with one of these prefixes
            //   ENV_PREVIEW_NOTIME=1             skip the cellar time series
            //   ENV_PREVIEW_NOFIRE=1             skip the fire phase series
            string envFilter = Environment.GetEnvironmentVariable("ENV_PREVIEW_ENVS");
            string viewFilter = Environment.GetEnvironmentVariable("ENV_PREVIEW_VIEWS");
            bool noTime = Environment.GetEnvironmentVariable("ENV_PREVIEW_NOTIME") == "1";
            string[] viewPrefixes = string.IsNullOrEmpty(viewFilter)
                ? null : viewFilter.Split(',');
            bool WantView(string n)
            {
                if (viewPrefixes == null) return true;
                foreach (var p in viewPrefixes)
                    if (n.StartsWith(p.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }

            foreach (var env in new[] { "Env_Cellar", "Env_Swamp" })
            {
                if (!string.IsNullOrEmpty(envFilter) && !envFilter.Contains(env)) continue;
                string prefabPath = $"{Root}/{env}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[GloomhavenVR][EnvPreview] {prefabPath} missing — skipped.");
                    continue;
                }

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

                // ---- WHICH ROOM THIS IS, told to the shaders (ModBuild 146) ----
                // `_GhvrIndoor` is written at RUNTIME by
                // src/GloomhavenVR/Core/SkyAlternative.ApplyIndoor — 1 in the
                // cellar, 0 in the wood — and the shading lane branches real
                // decisions on it: indoors, Light no longer lifts the room's
                // ambient at all (it lifts the MOON), the candle gains are the
                // exact identity, and the surface growth is a pale lichen/fungus
                // biology instead of the outdoor moss palette.
                //
                // The harness never wrote it, and an UNSET global reads as 0 —
                // i.e. every cellar preview ever taken after that lane landed was
                // a render of a room that does not ship: outdoor light response
                // AND outdoor growth colour. That is not a cosmetic difference in
                // a review harness whose entire job is to settle structural
                // claims; a claim read off such a frame is a claim about the
                // wrong room. It is written HERE, once per prefab, before any
                // Shoot() call, for the same reason the element globals are
                // written by hand: the harness must drive exactly the uniforms
                // the mod drives, and nothing else.
                Shader.SetGlobalFloat("_GhvrIndoor", env == "Env_Cellar" ? 1f : 0f);

                // ENV_PREVIEW_DEBUG=1: log every mesh renderer — separates
                // "geometry missing/culled" from "material/lighting wrong".
                // (The old bright-material override died with the EnvLit shader.)
                if (Environment.GetEnvironmentVariable("ENV_PREVIEW_DEBUG") == "1")
                {
                    foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mf = mr.GetComponent<MeshFilter>();
                        Debug.Log($"[EnvPreview][DBG] {mr.transform.root.name}/{mr.name}: active={mr.gameObject.activeInHierarchy} enabled={mr.enabled} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name : "NULL")} bounds={mr.bounds.center:F1}/{mr.bounds.size:F1} lossyScale={mr.transform.lossyScale:F2} mat={(mr.sharedMaterial ? mr.sharedMaterial.name : "NULL")}");
                    }
                }

                // Fast-forward the particle systems so the still frame shows them alive.
                void FastForward(Transform under)
                {
                    foreach (var ps in under.GetComponentsInChildren<ParticleSystem>(true))
                        if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null)
                            ps.Simulate(6f, true, true);
                }
                FastForward(inst.transform);

                var camGo = new GameObject("PreviewCam");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.01f, 0.01f, 0.015f);
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 300f;
                var roomGeo = inst.transform.Find("RoomGeo");

                // Project is LINEAR color space: take the readback as raw linear and
                // gamma-encode manually, otherwise the PNG comes out ~2.2x too dark
                // (iteration-2 lesson — mid-tones crushed to black).
                //
                // ARGBHalf, not ARGB32 (ModBuild 136). An 8-bit LINEAR target
                // quantises at 1/255 = 0.0039 linear, which after the gamma
                // encode is a FIRST STEP OF 18/255 — so every soft gradient in a
                // dark room arrived in the PNG as five or six hard contour bands
                // that do not exist on the headset (whose target is 8-bit sRGB,
                // i.e. ~0.0006 linear near black). A whole review pass was spent
                // chasing "hard edges" in the moonbeam that were this.
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                var tex = new Texture2D(W, H, TextureFormat.RGBAFloat, false);

                // A SKY-ONLY FRAME COSTS THE ROOM ITS PARTICLES, and until this
                // round nothing noticed because RoomGeo held no emitters. Hiding
                // the room deactivates its whole subtree; on re-activation every
                // ParticleSystem under it restarts empty, and in batch mode there
                // is no game loop to refill it — so EVERY frame after the first
                // 'SkyOnly' view rendered the element emitters as if they emitted
                // nothing. Two rounds of "the snow is too small" were this.
                // The room is therefore fast-forwarded again whenever it comes
                // back. It is a harness artefact and not a shipped one: nothing at
                // runtime ever toggles RoomGeo (SkyAlternative places it once and
                // never touches it again).
                bool roomHidden = false;
                void Shoot(string name, Vector3 pos, Vector3 euler, bool skyOnly, float fov, string suffix)
                {
                    if (roomGeo != null)
                    {
                        roomGeo.gameObject.SetActive(!skyOnly);
                        if (roomHidden && !skyOnly) FastForward(roomGeo);
                        roomHidden = skyOnly;
                    }
                    cam.fieldOfView = fov;
                    cam.transform.position = pos;
                    cam.transform.rotation = Quaternion.Euler(euler);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    var px = tex.GetPixels();
                    for (int i = 0; i < px.Length; i++) { var c = px[i].gamma; c.a = 1f; px[i] = c; }
                    tex.SetPixels(px);
                    tex.Apply();
                    string png = Path.Combine(outDir, $"{env.ToLowerInvariant()}_{name}{suffix}.png");
                    File.WriteAllBytes(png, tex.EncodeToPNG());
                    Debug.Log($"[GloomhavenVR][EnvPreview] wrote {Path.GetFullPath(png)}");
                }

                // ---- BEFORE ANY FRAME IS TAKEN: does the harness point at the
                // thing it says it points at? See AssertShelfStations. It runs
                // whatever ENV_PREVIEW_VIEWS says, because a filtered run that
                // skipped the check would be exactly the run in which a mis-aim
                // gets past.
                if (env == "Env_Cellar") AssertShelfStations(inst, cam);

                // ---- the still set, at the shader clock's origin ----
                // HAUNT OFF for this pass, which is also the shipped default state
                // of an unset global: every haunt card collapses to a point, the
                // beam does not dim, the webs do not shiver and the rat does not
                // look up. So every frame below is comparable, pixel for pixel,
                // with the previous round's — which is the point of a review set.
                Shader.SetGlobalVector("_GhvrHaunt", Vector4.zero);
                Shader.SetGlobalVector("_GhvrHauntForce", Vector4.zero);
                Shader.SetGlobalVector("_GhvrElemA", Vector4.zero);
                Shader.SetGlobalVector("_GhvrElemB", Vector4.zero);
                Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
                foreach (var (name, pos, euler, skyOnly, fov) in Views)
                    if (WantView(name) && !name.StartsWith("Haunt", StringComparison.Ordinal))
                        Shoot(name, pos, euler, skyOnly, fov, "");

                // ---- and the time series ----
                // Flicker, the drip, the ripples, the rat and the cobwebs only
                // EXIST in motion; a still frame cannot show that any of them
                // move, let alone that they move together. Every Env* shader
                // reads a global clock offset for exactly this (see
                // EnvRoom.shader/_GhvrTimeOfs), so the whole room can be stepped
                // to the same instant and the sequence read like a flipbook.
                if (env == "Env_Cellar" && !noTime)
                {
                    foreach (var (tag, ofs) in TimeSteps)
                    {
                        Shader.SetGlobalFloat("_GhvrTimeOfs", ofs);
                        foreach (var vn in TimeViews)
                        {
                            var v = Array.Find(Views, x => x.name == vn);
                            if (v.name == null)
                                throw new Exception($"TimeViews names an unknown view '{vn}'.");
                            if (WantView(v.name)) Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov, "_" + tag);
                        }
                    }
                    Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
                }

                // ---- ...and the rat going into (and out of) its hole ----
                if (env == "Env_Cellar" && !noTime)
                {
                    foreach (var (vn, hole, emerge) in RatMouthViews)
                    {
                        var v = Array.Find(Views, x => x.name == vn);
                        if (v.name == null)
                            throw new Exception($"RatMouthViews names an unknown view '{vn}'.");
                        if (!WantView(v.name)) continue;
                        foreach (var (tag, q) in emerge ? RatLeaveSteps : RatEnterSteps)
                        {
                            Shader.SetGlobalFloat("_GhvrTimeOfs",
                                                  EnvRoomBuilder.RatPreviewClock(hole, q));
                            Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov, "_" + tag);
                        }
                    }
                    Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
                }

                // ---- HAUNT: the easter eggs, photographed mid-event ----
                // ENV_PREVIEW_NOHAUNT=1 skips it.
                if (Environment.GetEnvironmentVariable("ENV_PREVIEW_NOHAUNT") != "1")
                {
                    var shots = env == "Env_Cellar" ? CellarHaunts : ForestHaunts;
                    // THE ROOM'S OWN COUNT, not the maximum. The two rooms stopped
                    // agreeing in ModBuild 147 (six in the cellar, three in the
                    // wood) and this had said 6 for both ever since — which would
                    // solve the schedule for the wrong slot in the wood. It is
                    // moot while ForestHaunts is empty and it must not be wrong
                    // when it stops being.
                    int cards = env == "Env_Cellar" ? 6 : 3;
                    // dial 1.0: every scheduled slot fires. That is a real shipped
                    // setting, not a debug mode — the frames below are what a
                    // player with the frequency slider at maximum sees.
                    Shader.SetGlobalVector("_GhvrHaunt", new Vector4(1f, 1f, 0f, 0f));
                    foreach (var (vn, card, envv) in shots)
                    {
                        var v = Array.Find(Views, x => x.name == vn);
                        if (v.name == null)
                            throw new Exception($"A haunt shot names an unknown view '{vn}'.");
                        if (!WantView(v.name)) continue;
                        // the bookshelf gets its own four, because its event is a
                        // trajectory rather than an envelope
                        bool shelf = env == "Env_Cellar" && card == 5;
                        foreach (float ph in shelf ? HauntShelfPhases : HauntPhases)
                        {
                            float ofs = EnvRoomBuilder.HauntPreviewClock(
                                card, cards, envv.x, envv.y, envv.z, ph);
                            Shader.SetGlobalFloat("_GhvrTimeOfs", ofs);
                            Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov,
                                  $"_p{Mathf.RoundToInt(ph * 100f):D2}");
                        }
                        // ...and the element compensation, at the hold, on the two
                        // silhouette events (one per room) whose readability is the
                        // thing the user warned could be lost.
                        // The silhouette events those two indices used to name are
                        // gone (ModBuild 146), and so is the wood's own last drawn
                        // card (ModBuild 149 — the eyeshines). The
                        // readability-under-Light/Dark question is still real and
                        // there is exactly ONE event left in either room that is
                        // both drawn and nearly black: the cellar's handprints.
                        bool moodShot = env == "Env_Cellar" && card == 1;
                        if (!moodShot) continue;
                        float hold = EnvRoomBuilder.HauntPreviewClock(
                            card, cards, envv.x, envv.y, envv.z, 0.55f);
                        Shader.SetGlobalFloat("_GhvrTimeOfs", hold);
                        foreach (var (tag, ea, eb) in HauntMoods)
                        {
                            Shader.SetGlobalVector("_GhvrElemA", ea);
                            Shader.SetGlobalVector("_GhvrElemB", eb);
                            Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov, "_" + tag);
                        }
                        Shader.SetGlobalVector("_GhvrElemA", Vector4.zero);
                        Shader.SetGlobalVector("_GhvrElemB", Vector4.zero);
                    }

                    // ---- and the ON-DEMAND channel, photographed rather than
                    // asserted. A parallel lane is adding the Advanced-menu
                    // buttons; this is the reading half proving it works, and it
                    // is a real requirement rather than a debug aid: the tester
                    // must see EXACTLY the apparition they asked for, with the
                    // room's normal schedule suppressed.
                    //
                    // The clock is parked at an instant where the SCHEDULE would
                    // fire a DIFFERENT card, so a frame that shows the forced one
                    // is proof that the override wins and that nothing else is
                    // drawn beside it.
                    foreach (var (vn, card, envv) in shots)
                    {
                        var v = Array.Find(Views, x => x.name == vn);
                        if (!WantView(v.name)) continue;
                        float dur = envv.x + envv.y + envv.z;
                        // a slot in which some OTHER card is this slot's event
                        float other = EnvRoomBuilder.HauntPreviewClock(
                            (card + 1) % cards, cards, envv.x, envv.y, envv.z, 0.55f);
                        Shader.SetGlobalFloat("_GhvrTimeOfs", other);
                        Shader.SetGlobalVector("_GhvrHauntForce",
                            new Vector4(card + 1, other - dur * 0.55f, 0f, 0f));
                        Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov, "_forced");
                        Shader.SetGlobalVector("_GhvrHauntForce", Vector4.zero);
                    }

                    Shader.SetGlobalVector("_GhvrHaunt", Vector4.zero);
                    Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
                }

                // ---- FIRE PHASE: the one thing this harness could never show ----
                // See the FIRE PHASE block above. ENV_PREVIEW_NOFIRE=1 skips it.
                if (Environment.GetEnvironmentVariable("ENV_PREVIEW_NOFIRE") != "1")
                {
                    bool cellar = env == "Env_Cellar";
                    var fireViews = cellar ? CellarFireViews : ForestFireViews;
                    var windViews = cellar ? CellarWindViews : ForestWindViews;

                    // ---- THE EMITTERS HAVE TO MOVE TOO — ModBuild 151 ---------
                    // The fire series steps `_GhvrTimeOfs`, which is the clock
                    // every Env* SHADER reads. A Shuriken system reads none of
                    // it: it is simulated on the CPU, and in batch mode there is
                    // no game loop, so until now every frame of every fire series
                    // showed the sparks FROZEN at the one state FastForward left
                    // them in. That was a hole exactly as big as the fire-phase
                    // one it sits inside — and this round it would have been
                    // fatal, because the round's whole answer to "the wind draws
                    // streaks" is that the wind is now told by the SPARKS, and a
                    // series in which the sparks cannot move cannot show it.
                    //
                    // Stepped by a FULL RESTART to (prewarm + t) rather than by
                    // advancing the previous frame: Simulate(t, ..., restart:
                    // true) with the seeds pinned (ElemPS's useAutoRandomSeed =
                    // false) is a pure function of t, so frame k of a series does
                    // not depend on whether frames 0..k-1 were rendered — which
                    // is what makes ENV_PREVIEW_VIEWS-filtered runs comparable
                    // with full ones, and what makes a frame-to-frame difference
                    // measurement mean what it says.
                    const float FirePrewarm = 6f;   // the same 6 s FastForward uses
                    void StepEmitters(float t)
                    {
                        if (roomGeo == null) return;
                        foreach (var ps in roomGeo.GetComponentsInChildren<ParticleSystem>(true))
                            if (ps.transform.parent == null
                                || ps.transform.parent.GetComponent<ParticleSystem>() == null)
                                ps.Simulate(FirePrewarm + t, true, true);
                    }

                    void FireSeries(string[] vns, float[] phases, string tagPrefix,
                                    Vector4 ea, Vector4 eb)
                    {
                        Shader.SetGlobalVector("_GhvrElemA", ea);
                        Shader.SetGlobalVector("_GhvrElemB", eb);
                        foreach (var vn in vns)
                        {
                            var v = Array.Find(Views, x => x.name == vn);
                            if (v.name == null)
                                throw new Exception($"A fire phase view names an unknown view '{vn}'.");
                            if (!WantView(v.name)) continue;
                            foreach (float t in phases)
                            {
                                Shader.SetGlobalFloat("_GhvrTimeOfs", t);
                                StepEmitters(t);
                                // milliseconds in the tag, so the sequence sorts
                                // in time order in a directory listing and the
                                // STEP is readable off the file names — which is
                                // what makes an aliasing claim checkable.
                                Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov,
                                      $"_{tagPrefix}{Mathf.RoundToInt(t * 1000f):D4}");
                            }
                        }
                    }

                    // ---- THE LOOP HUNT, ModBuild 150 -----------------------
                    // USER, hardware, ModBuild 149: "Das Feuer zieht in einem
                    // Loop in eine Richtung, glitcht dann zurueck und beginnt
                    // diesen Loop von vorne."
                    //
                    // Neither shipped series can settle that claim. FAST spans
                    // 0.385 s and SLOW steps 240 ms, so a wrap whose period is
                    // anywhere between them is either invisible or aliased —
                    // and the erosion scroll's period is 1/(FireHz*ErodeScroll)
                    // = 0.275 s, i.e. exactly in that hole. A DISCONTINUITY is
                    // a claim about consecutive frames and can only be settled
                    // by a series that is uniform in time and long enough to
                    // contain at least two wraps of whatever is being hunted.
                    //
                    // ENV_PREVIEW_FIRELOOP="count,step[,air]" renders `count`
                    // frames `step` seconds apart, tagged _pl<ms>, on the fire
                    // views. It is OFF by default (it is a diagnostic, not a
                    // review set: 60 frames x 7 views is 420 PNGs) and it is
                    // the instrument the frame-to-frame displacement numbers in
                    // .planning/fire-loop-measurement.md were taken with.
                    string loopSpec = Environment.GetEnvironmentVariable("ENV_PREVIEW_FIRELOOP");
                    if (!string.IsNullOrEmpty(loopSpec))
                    {
                        var pc = loopSpec.Split(',');
                        int n = int.Parse(pc[0], CultureInfo.InvariantCulture);
                        float step = float.Parse(pc[1], CultureInfo.InvariantCulture);
                        float air = pc.Length > 2
                            ? float.Parse(pc[2], CultureInfo.InvariantCulture) : 0f;
                        var ph = new float[n];
                        for (int k = 0; k < n; k++) ph[k] = k * step;
                        // ...and the AIR LEVEL IS IN THE TAG — ModBuild 151. The
                        // measurement this round is judged on is a COMPARISON of
                        // two series at the same offsets with Air down and Air
                        // up, and with one tag the second run silently overwrote
                        // the first. `pl` is Air 0, `pla` is Air > 0.
                        FireSeries(fireViews, ph, air > 0f ? "pla" : "pl",
                                   new Vector4(1f, 0f, air, 0f), new Vector4(0f, 0f, 1f, 1f));
                    }

                    // Fire alone, fast then slow. _GhvrElemA = (Fire, Ice, Air,
                    // Earth), _GhvrElemB = (Light, Dark, Master, Peak) — the same
                    // two uniforms ElementMood publishes at runtime, driven by
                    // hand for the same reason the element series drives them:
                    // the harness must move exactly what the mod moves.
                    var fireA = new Vector4(1f, 0f, 0f, 0f);
                    var fireB = new Vector4(0f, 0f, 1f, 1f);
                    FireSeries(fireViews, FirePhasesFast, "pf", fireA, fireB);
                    FireSeries(fireViews, FirePhasesSlow, "ps", fireA, fireB);
                    // ...and the same fast series under full Fire+Air, which is
                    // the only way the wind answer can be photographed at all.
                    FireSeries(windViews, FirePhasesFast, "pw",
                               new Vector4(1f, 0f, 1f, 0f), fireB);

                    // ...and the burning bookshelf going over WHILE it burns —
                    // the one state neither the haunt series nor the fire series
                    // can reach, because each of them leaves the other feature's
                    // channel unset. See FireShelfRidePhases.
                    if (cellar)
                    {
                        // 'HauntShelfRide' and NOT 'HauntShelf' — see the view
                        // table: the latter has not pointed at the bookshelf
                        // since the prop moved in ModBuild 147.
                        var v = Array.Find(Views, x => x.name == "HauntShelfRide");
                        if (v.name == null)
                            throw new Exception("The fire shelf-rider series needs the "
                                                + "'HauntShelfRide' view and it is gone.");
                        if (WantView(v.name))
                        {
                            Shader.SetGlobalVector("_GhvrElemA", fireA);
                            Shader.SetGlobalVector("_GhvrElemB", fireB);
                            // dial 1.0, i.e. every scheduled slot fires — a real
                            // shipped setting, not a debug mode, exactly as the
                            // haunt series uses it.
                            Shader.SetGlobalVector("_GhvrHaunt", new Vector4(1f, 1f, 0f, 0f));
                            foreach (float ph in FireShelfRidePhases)
                            {
                                // the bookshelf is cellar haunt card 5 of 6, and
                                // its whole 26 s is hold (0.001 + 26 + 0.001) —
                                // the same three numbers CellarHaunts passes, and
                                // they have to stay in step with the builder's
                                // catalogue because the phase is a fraction of
                                // the whole run.
                                Shader.SetGlobalFloat("_GhvrTimeOfs",
                                    EnvRoomBuilder.HauntPreviewClock(5, 6, 0.001f, 26f, 0.001f, ph));
                                // ...AND THE EMITTERS, ModBuild 152. Until now
                                // this series left them frozen wherever the fire
                                // series had put them, which was harmless while
                                // the only claim being made was about the flame
                                // CARDS and is fatal now that the sparks are the
                                // thing under test: a frozen population cannot
                                // show a fade, and worse, it would show the same
                                // sparks in every frame and read as proof that
                                // nothing happened.
                                //
                                // SECONDS INTO THE EVENT, not the absolute clock
                                // offset: a Shuriken system knows nothing of the
                                // haunt schedule (it is CPU-simulated and reads no
                                // global), so the honest argument is the one the
                                // shader gets — the event is 26.002 s long and
                                // this frame is `ph` of the way through it. It is
                                // also what keeps the series affordable, since
                                // Simulate(restart: true) replays from zero.
                                StepEmitters(ph * 26.002f);
                                Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov,
                                      $"_pride{Mathf.RoundToInt(ph * 100f):D2}");
                            }
                            Shader.SetGlobalVector("_GhvrHaunt", Vector4.zero);
                        }
                    }

                    // ---- WALKING UP TO THE FALLEN CANDLE — ModBuild 153 -------
                    // USER, hardware (verbatim): "Wenn das Bücherregal umkippt und
                    // man dann nah an die Kerze herangeht verschwindet sie! Wenn
                    // ich eine bestimmte Distanz erreiche sogar nur auf einem Auge."
                    //
                    // NO EXISTING STATION CAN SHOW THIS AND NONE EVER COULD. A
                    // preview station is a fixed camera and the report is about
                    // APPROACHING: the fault is frustum culling on a box the
                    // shader's own vertex rotation has left behind, so it appears
                    // as a function of DISTANCE and only once the shelf is over.
                    // A single frame at 3.4 m is a frame in which the stale box is
                    // still comfortably inside the frustum and everything looks
                    // right — which is exactly why thirteen confident PNGs shipped
                    // the bug.
                    //
                    // So this walks a camera in along the line to the candle, at
                    // the two poses that matter, and COUNTS THE PIXELS the candle
                    // covers in each frame. The reading is not "does it look
                    // right": it is that the count must RISE as the camera closes,
                    // because a candle you walk towards only gets bigger. A count
                    // that collapses between two stations of one series is the
                    // fault, reproduced.
                    if (cellar) ShelfNearApproach(inst, cam, tex, Shoot, StepEmitters);

                    Shader.SetGlobalVector("_GhvrElemA", Vector4.zero);
                    Shader.SetGlobalVector("_GhvrElemB", Vector4.zero);
                    Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
                    // ...and put the emitters back where the rest of the harness
                    // expects to find them, so that the element series after this
                    // one is comparable with the bakes taken before the fire
                    // series ever touched a particle system.
                    StepEmitters(0f);
                }

                // ---- ELEMENT ART: the six elements, their two levels and the
                // mixtures (see ElementMoods). ENV_PREVIEW_NOELEM=1 skips it.
                if (Environment.GetEnvironmentVariable("ENV_PREVIEW_NOELEM") != "1")
                {
                    var elemViews = env == "Env_Cellar" ? CellarElementViews : ForestElementViews;
                    // A FIXED, NON-ZERO CLOCK. Half of what the elements do is
                    // animated (the ember breath, the flames' gust, the twinkle
                    // on the sparks), and at the clock's origin several of those
                    // sines are at a zero crossing — a series shot there would
                    // systematically under-report the effect. 3.7 s is not a
                    // round number on purpose: it is not a period or a half
                    // period of anything in either room.
                    Shader.SetGlobalFloat("_GhvrTimeOfs", 3.7f);
                    foreach (var (tag, ea, eb) in ElementMoods)
                    {
                        Shader.SetGlobalVector("_GhvrElemA", ea);
                        Shader.SetGlobalVector("_GhvrElemB", eb);
                        foreach (var vn in elemViews)
                        {
                            var v = Array.Find(Views, x => x.name == vn);
                            if (v.name == null)
                                throw new Exception($"An element view names an unknown view '{vn}'.");
                            if (WantView(v.name)) Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov, "_e" + tag);
                        }
                    }
                    // ...and the same four frames with the channel UNSET, at the
                    // same instant. This is the frame the 'moff' and 'live0'
                    // shots above are compared against, pixel for pixel: three
                    // identical images are the whole zero-state proof.
                    Shader.SetGlobalVector("_GhvrElemA", Vector4.zero);
                    Shader.SetGlobalVector("_GhvrElemB", Vector4.zero);
                    foreach (var vn in elemViews)
                    {
                        var v = Array.Find(Views, x => x.name == vn);
                        if (WantView(v.name)) Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov, "_ebase");
                    }
                    Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
                }

                RenderTexture.active = null;
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(inst);
            }
        }
    }
}
