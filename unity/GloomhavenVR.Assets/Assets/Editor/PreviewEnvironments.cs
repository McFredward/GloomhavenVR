// GloomhavenVR companion project — ambient-environment preview renderer.
//
// Batch: Unity -batchmode -projectPath <this> -buildTarget Win64
//        -executeMethod GloomhavenVR.EnvironmentsPreview.RenderAll -logFile env-preview.log
//   IMPORTANT: run WITHOUT -nographics (rendering needs a graphics device); on a
//   headless box wrap in `xvfb-run -a`.
//
// For each Env_*.prefab (FX-only shells): loads it into an empty temp scene,
// fast-forwards every particle system 6 s (so fog banks, fireflies and — with
// luck — a shooting star are populated), then renders 5 views from the seated player position
// (0, 1.4, 0): N/E/S/W at 60° FOV horizontal plus one 30°-up view. 1280x720 PNGs go
// to $ENV_PREVIEW_OUT (or ./env-previews under the project when unset).
using System;
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
        private static readonly Vector3 Eye = new Vector3(0f, 1.4f, 0f); // seated player head

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
            // ...and the ONE new one: the door of light at the top of the stair.
            // Both from inside the room, because that is the only place it can be
            // seen from — the shaft's own walls crop it to the doorway.
            ("HauntDoor", new Vector3(-2.20f, 1.40f, 2.45f), new Vector3(4, 271, 0), false, 50f),
            ("HauntDoorOff", new Vector3(-1.60f, 1.45f, 4.10f), new Vector3(3, 244, 0), false, 50f),
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
            // the prints moved down to 0.85 m, so this looks DOWN at the wall
            ("HauntHands", new Vector3(-3.20f, 1.40f, 1.30f), new Vector3(12, 272, 0), false, 45f),
            // ...and the head on the floor is at the edge of the moon pool now
            // THE BOOKSHELF GOING OVER. Two viewpoints and, unlike everything else
            // here, four PHASES rather than three, because the event is a
            // trajectory: standing, falling, down, and back up again.
            ("HauntShelf", new Vector3(0.60f, 2.10f, -1.30f), new Vector3(14, 48, 0), false, 62f),
            ("HauntShelfOff", new Vector3(2.30f, 1.75f, 3.60f), new Vector3(8, 150, 0), false, 58f),
            // ...and the tremble draws nothing at all: it is judged on the WEBS,
            // so its frames are the two web close-ups, shot at its own instants.
            ("HauntWeb", new Vector3(2.90f, 2.05f, 1.35f), new Vector3(-25, 91, 0), false, 38f),
            // FOREST. Cards: Face, Eyes, Watcher, Cross, Loom, Hang. Aimed off the
            // same bearings the builder places them on (BuildForestRoom's haunt
            // block), from the seated eye at the middle of the clearing — which is
            // where the player is, and therefore the only place the framing of a
            // background easter egg can honestly be judged.
            ("HauntEyes", Eye, new Vector3(-1, 288, 0), false, 16f),
            // ...and one wide frame per room at the same instants: the brief is
            // "eher im Hintergrund", and the only way to check that an apparition
            // is NOT intrusive is to look at the room the way a player would and
            // see whether it pulls the eye.
            ("HauntWideC", Eye, new Vector3(-4, 320, 0), false, 78f),
            // ...and the cellar's shelf face from the BOARD, which is the distance
            // a player actually meets it at.
            // ...from 5.5 m, on the side the face comes out on. NOT from the
            // middle of the board: the card sits at the shelf's BACK panel and
            // emerges toward +z, so from dead centre the shelf's own front hides
            // it completely and the frame is a picture of an empty shelf. Where an
            // apparition can be seen from is part of its placement, and a preview
            // that does not show that is a preview that cannot check it.
            ("HauntFarC", new Vector3(0f, 1.45f, 0.0f), new Vector3(-3, 96, 0), false, 62f),
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
            ("FireShelf", new Vector3(2.70f, 1.85f, 0.90f), new Vector3(6, 84, 0), false, 42f),
            ("FireShelfLow", new Vector3(2.85f, 1.05f, 1.05f), new Vector3(19, 82, 0), false, 42f),
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
            ("HauntHands", 1, new Vector3(2.8f, 2.2f, 3.0f)),
            // the NEW one: light at the top of the stair, seen from the room
            ("HauntDoor", 2, new Vector3(2.2f, 3.0f, 1.6f)),
            ("HauntDoorOff", 2, new Vector3(2.2f, 3.0f, 1.6f)),
            ("HauntWeb", 3, new Vector3(0.0f, 1.1f, 0.9f)),
            // the bookshelf: 0.001 + 26 + 0.001 s, the whole of it hold
            ("HauntShelf", 5, new Vector3(0.001f, 26f, 0.001f)),
            ("HauntShelfOff", 5, new Vector3(0.001f, 26f, 0.001f)),
            ("HauntWideC", 5, new Vector3(0.001f, 26f, 0.001f)),
            // ...and the shelf from the BOARD, which is where a player meets it
            ("HauntFarC", 5, new Vector3(0.001f, 26f, 0.001f)),
        };

        // The wood draws exactly ONE haunt now — the pair of eyeshines. Its other
        // two cards (Watcher, Cross) are placeholders for runtime monsters and its
        // other three (Face, Loom, Hang) are retired outright.
        private static readonly (string view, int card, Vector3 env)[] ForestHaunts =
        {
            ("HauntEyes", 0, new Vector3(1.6f, 3.0f, 0.8f)),
        };

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
          "FireRoom", "FireSpillGraze" };
        private static readonly string[] ForestElementViews =
        { "TreeLine", "FloorToMoon", "Fireflies", "SkyBand", "N", "ShaftMoon",
          "FireSnag", "FireSnagWide", "FireLog", "FireBrush", "FireWood",
          "FireLogGraze", "FireLogGrazeB", "FireSnagGraze", "FireSnagGrazeLow" };

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
        // 0.30 down just after the rebound (it must be lying with them). The
        // wall wash deliberately does NOT ride and must stay put across all
        // three — that is an authored asymmetry, not a fault, and this series is
        // where it can be seen.
        private static readonly float[] FireShelfRidePhases = { 0.08f, 0.16f, 0.30f };

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
                    int cards = 6;
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
                        // gone (ModBuild 146). The readability-under-Light/Dark
                        // question is still real, so it moves to the two events
                        // that are still DRAWN and are still nearly black: the
                        // cellar's handprints and the wood's eyeshines.
                        bool moodShot = (env == "Env_Cellar" && card == 1)
                                        || (env != "Env_Cellar" && card == 0);
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
                                // milliseconds in the tag, so the sequence sorts
                                // in time order in a directory listing and the
                                // STEP is readable off the file names — which is
                                // what makes an aliasing claim checkable.
                                Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov,
                                      $"_{tagPrefix}{Mathf.RoundToInt(t * 1000f):D4}");
                            }
                        }
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
                        var v = Array.Find(Views, x => x.name == "HauntShelf");
                        if (v.name == null)
                            throw new Exception("The fire shelf-rider series needs the "
                                                + "'HauntShelf' view and it is gone.");
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
                                Shoot(v.name, v.pos, v.euler, v.skyOnly, v.fov,
                                      $"_pride{Mathf.RoundToInt(ph * 100f):D2}");
                            }
                            Shader.SetGlobalVector("_GhvrHaunt", Vector4.zero);
                        }
                    }

                    Shader.SetGlobalVector("_GhvrElemA", Vector4.zero);
                    Shader.SetGlobalVector("_GhvrElemB", Vector4.zero);
                    Shader.SetGlobalFloat("_GhvrTimeOfs", 0f);
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
