using System;
using GloomhavenVR.Core;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE APPARITION'S DARKNESS AND ITS MATERIALISE WINDOW (<see cref="HauntFigures"/>), driven
/// instant by instant.
///
/// <para><b>WHY IT IS ON THIS HARNESS.</b> Five rounds of user reports have been spent on one
/// constant. Through ModBuild 152 the haunt figures' darkening multiplier was written into a
/// material colour property that the game's character shader ignores, so the four numbers behind it
/// were re-fitted FOUR TIMES against photographs in which the lever did nothing — each fit solving
/// for the wrong unknown, each shipped, each reported back as "immer noch viel zu hell". Nothing in
/// the repository could have caught that, because a figure's brightness is observed only by FEEL,
/// from inside a headset, one photograph per round.</para>
///
/// <para>These vectors cannot see a pixel either, and they do not pretend to. What they CAN pin is
/// everything about the two functions that is a property rather than a preference: that the two
/// rooms stay coupled by ONE rule and cannot drift apart into two constants; that the fallback for
/// an unmeasurable room is DIMMER than the darker room rather than brighter; that the materialise
/// window is bounded, monotone on each half, exactly solid in the steady middle, and can never
/// outlast the apparition it belongs to; and that both are pure functions of the shared clock, which
/// is the multiplayer guarantee written as an assertion instead of as a promise.</para>
///
/// <para><b>THEY DRIVE THE SHIPPED CODE, NOT A COPY OF IT.</b> HauntFigures.Math.cs is compiled into
/// this assembly verbatim (see the .csproj). A mirrored re-implementation would have passed happily
/// through all four of the rounds above.</para>
/// </summary>
internal static class HauntFigureVectors
{
    // The two rooms' measured rig luminance at the figure's chest — the inputs the mapping was
    // fitted against. Cellar from BuildEnvironmentRooms' own constants, wood from the ModBuild 152
    // hardware log ("the room delivers luminance 0.2126 at the figure's chest").
    private const float CellarLum = 0.0425f;
    private const float WoodLum = 0.2126f;

    // What the fit says those two must come out at. See THE DARKENING in HauntFigures.Math.cs for
    // the three photographs these were solved from.
    private const float CellarLevel = 0.1200f;
    private const float WoodLevel = 0.1999f;

    // The figure luminances the two levels are supposed to produce, measured off the ModBuild 152
    // photographs (Rec.709 luma on the gamma-encoded bytes; figure separated from surround by a
    // luminance mask that was rendered back out and looked at).
    private const float CellarFigureP90 = 0.3870f * CellarLevel;   // Figur_hell7.jpg
    private const float WoodFigureP90 = 0.2257f * WoodLevel;       // Figur_hell9.jpg

    // The two apparitions the events actually author, from the ModBuild 152 log.
    private const float CellarWindowSeconds = 5.50f;
    private const float WalkingHoundSeconds = 2.85f;

    internal static void Run(Harness t)
    {
        TheTwoRoomsStayCoupled(t);
        TheMappingIsMonotoneAndClamped(t);
        TheUnmeasurableRoomIsDimmerThanTheDarkestReal(t);
        TheMappingRefusesGarbage(t);

        TheWindowIsBoundedAndSolidInTheMiddle(t);
        TheWindowIsMonotoneOnEachHalf(t);
        TheWindowNeverOutlastsTheApparition(t);
        TheWindowSurvivesEveryDuration(t);
        TheWindowIsGoneOutsideTheRun(t);
        TwoClientsAgree(t);

        TheBurnEdgeCannotOutshineTheFigure(t);
        TheBurnEdgeTracksTheRoom(t);
    }

    /// <summary>The harness asserts EXACT equality, which is the right default for a byte stream and
    /// the wrong one for a fit: these values come out of a chain of Mathf operations and are compared
    /// against numbers written down to four decimal places. The tolerance is passed in per call and
    /// is deliberately tight — 0.002 on a level of 0.12 is under two percent, i.e. far smaller than
    /// any change a tuning drop would make and far larger than float noise.</summary>
    private static void Near(Harness t, string what, float actual, float expected, float tol)
        => t.True(Math.Abs(actual - expected) <= tol,
                  $"{what}: expected {expected:F6} +/- {tol:F6}, got {actual:F6}");

    // ---- THE DARKENING ---------------------------------------------------------------------------

    /// <summary>
    /// THE ONE ASSERTION THAT WOULD HAVE CAUGHT THE SHAPE OF FOUR WASTED ROUNDS, if not their cause:
    /// the two rooms are one straight line and not two constants. Every earlier round tuned "the
    /// cellar" and "the forest" as if they were separate answers, and the block that records those
    /// rounds is full of arithmetic reconciling them after the fact. They are now a single
    /// <c>DarkFloor + LightGain x lum</c>, and this pins BOTH endpoints and their ratio — so a drop
    /// that moves one room without the other fails here rather than on hardware.
    /// </summary>
    private static void TheTwoRoomsStayCoupled(Harness t)
    {
        t.Case("darkening: the cellar and the wood are one rule, not two constants");

        float cellar = HauntFigures.RoomLevel(CellarLum);
        float wood = HauntFigures.RoomLevel(WoodLum);

        Near(t, "cellar level", cellar, CellarLevel, 0.002f);
        Near(t, "wood level", wood, WoodLevel, 0.002f);

        // The ratio is the coupling itself. It is pinned rather than derived because "the two rooms
        // must stay coupled" is a requirement and not an outcome: if a future drop lands the two
        // endpoints by moving DarkFloor and LightGain in opposite directions, the endpoints above
        // still pass and this is what notices.
        Near(t, "wood/cellar ratio", wood / cellar, WoodLevel / CellarLevel, 0.01f);

        // ...and the line is straight, which is what makes it ONE rule. A room halfway between the
        // two in luminance must be halfway between them in level.
        float mid = HauntFigures.RoomLevel(0.5f * (CellarLum + WoodLum));
        Near(t, "the mapping is linear between the two rooms", mid, 0.5f * (cellar + wood), 0.002f);
    }

    /// <summary>Monotone and clamped, over the whole range a room could ever deliver. The clamp
    /// matters in one direction only and the vectors say which: a figure that walks into a candle
    /// pool must not climb back toward full albedo, which is the picture the user rejected five
    /// times.</summary>
    private static void TheMappingIsMonotoneAndClamped(Harness t)
    {
        t.Case("darkening: monotone in room luminance and clamped at both ends");

        float prev = -1f;
        for (int i = 0; i <= 400; i++)
        {
            float lum = i * 0.01f;                       // 0 .. 4.0, far past anything real
            float level = HauntFigures.RoomLevel(lum);
            t.True(level > 0f && level <= 1f, $"level in 0..1 exclusive of 0 at lum {lum:F2}");
            t.True(level >= prev - 1e-6f, $"level never decreases at lum {lum:F2}");
            prev = level;
        }

        // The floor is the value a pitch-black room gets, and it must be the SAME value as an
        // impossible negative one — a creature is never fully black while it is present.
        Near(t, "a black room gets the floor", HauntFigures.RoomLevel(0f), 0.100f, 1e-4f);

        // The ceiling bites, and it bites well below full albedo. If this ever passes at 1.0 the
        // apparition has stopped being a thing in the dark.
        float bright = HauntFigures.RoomLevel(100f);
        Near(t, "a blinding room is clamped", bright, 0.28f, 1e-4f);
        t.True(bright < 0.5f, "the clamp is far below full albedo");

        // ...and the ceiling is ABOVE the brighter of the two real rooms, or it would silently
        // re-flatten them into one constant — which is exactly what MaxLevel 0.30 did in ModBuild
        // 151 and what the 152 block had to notice by hand.
        t.True(bright > HauntFigures.RoomLevel(WoodLum) + 1e-4f, "the ceiling does not clamp the wood");
    }

    /// <summary>
    /// THE DIRECTION OF THIS INEQUALITY IS THE DIFFERENCE BETWEEN "dim" AND "fully lit". When the
    /// room's rig cannot be read the mapping falls back to a fixed constant, and the one value it
    /// must never fall back to is bright. It is asserted against the DARKER room's real answer
    /// rather than against a literal, so the fallback tracks the fit instead of drifting behind it.
    /// </summary>
    private static void TheUnmeasurableRoomIsDimmerThanTheDarkestReal(Harness t)
    {
        t.Case("darkening: the unreadable-rig fallback is dimmer than the darker real room");
        float fallback = HauntFigures.UnlitRoomLevel;
        t.True(fallback > 0f, "fallback is positive");
        t.True(fallback < HauntFigures.RoomLevel(CellarLum), "fallback sits under the cellar's own answer");
        t.True(fallback > 0.02f, "fallback is not a fade to black");
    }

    /// <summary>A NaN or a negative luminance is a rig that could not be read, and the honest answer
    /// to that is the dim fallback — never a NaN multiplier, which would propagate into a texture
    /// blit and paint an undefined figure.</summary>
    private static void TheMappingRefusesGarbage(Harness t)
    {
        t.Case("darkening: NaN and negative luminance fall back rather than propagate");
        Near(t, "NaN -> fallback", HauntFigures.RoomLevel(float.NaN), HauntFigures.UnlitRoomLevel, 1e-6f);
        Near(t, "negative -> fallback", HauntFigures.RoomLevel(-1f), HauntFigures.UnlitRoomLevel, 1e-6f);
        t.True(!float.IsNaN(HauntFigures.RoomLevel(float.NaN)), "no NaN escapes");
    }

    // ---- THE MATERIALISE AND THE DISSOLVE --------------------------------------------------------

    /// <summary>
    /// Bounded to [0,1], EXACTLY gone at both ends, and EXACTLY solid through the middle. The
    /// "exactly" on the middle is the load-bearing one: a cutout that is merely near zero during the
    /// hold is a figure that spends its whole apparition slightly eaten, and _Toggle_Dissolve would
    /// stay on with it — which is how ModBuild 151's burn edge became the "schwarze Flecken" report.
    /// </summary>
    private static void TheWindowIsBoundedAndSolidInTheMiddle(Harness t)
    {
        t.Case("dissolve: bounded, gone at the ends, exactly solid in the middle");

        foreach (float len in new[] { CellarWindowSeconds, WalkingHoundSeconds, 1.05f, 4.0f, 10.0f })
        {
            float e = MathF.Min(HauntFigures.EdgeSeconds, len / 3f);

            for (int i = 0; i <= 1000; i++)
            {
                float u = i / 1000f;
                float c = HauntFigures.DissolveCutout(u * len, len);
                t.True(c >= 0f && c <= 1f, $"cutout in [0,1] at u {u:F3} len {len:F2}");
                t.True(!float.IsNaN(c), $"cutout is finite at u {u:F3} len {len:F2}");
            }

            // The steady middle is EXACTLY 0 — solid — everywhere between the two windows.
            for (int i = 0; i <= 200; i++)
            {
                float tt = e + (len - 2f * e) * (i / 200f);
                t.True(HauntFigures.DissolveCutout(tt, len) == 0f, $"solid in the hold at t {tt:F3} len {len:F2}");
                t.True(!HauntFigures.Dissolving(tt, len), $"toggle off in the hold at t {tt:F3} len {len:F2}");
            }

            // ...and both ends are fully gone, so the renderer switch can never be caught showing a
            // solid figure for the frame it flips.
            Near(t, $"gone at t=0 (len {len:F2})", HauntFigures.DissolveCutout(0f, len), 1f, 1e-6f);
            Near(t, $"gone at t=len (len {len:F2})", HauntFigures.DissolveCutout(len, len), 1f, 1e-6f);
        }
    }

    /// <summary>Monotone on each half — the materialise only ever solidifies, the dissolve only ever
    /// eats. A non-monotone sweep is a figure that flickers back into existence mid-appearance, which
    /// no amount of eyeballing a 0.35 s window would reliably catch.</summary>
    private static void TheWindowIsMonotoneOnEachHalf(Harness t)
    {
        t.Case("dissolve: monotone materialising and monotone dissolving");

        foreach (float len in new[] { CellarWindowSeconds, WalkingHoundSeconds, 0.6f, 8.0f })
        {
            float e = MathF.Min(HauntFigures.EdgeSeconds, len / 3f);

            float prev = 2f;
            for (int i = 1; i <= 500; i++)     // from just after 0: t=0 is the "outside the run" case
            {
                float tt = e * (i / 500f);
                float c = HauntFigures.DissolveCutout(tt, len);
                t.True(c <= prev + 1e-6f, $"materialise never un-solidifies at t {tt:F4} len {len:F2}");
                prev = c;
            }
            Near(t, $"materialise finishes solid (len {len:F2})", prev, 0f, 1e-5f);

            prev = -1f;
            for (int i = 0; i < 500; i++)
            {
                float tt = len - e + e * (i / 500f);
                float c = HauntFigures.DissolveCutout(tt, len);
                t.True(c >= prev - 1e-6f, $"dissolve never un-eats at t {tt:F4} len {len:F2}");
                prev = c;
            }
            t.True(prev > 0.98f, $"dissolve gets most of the way gone (len {len:F2})");
        }
    }

    /// <summary>
    /// IT CAN NEVER OUTLAST THE APPARITION, and this is the assertion the brief asked for by name.
    /// The two windows together take at most two thirds of the run, so they can never meet in the
    /// middle and a figure can never be caught dissolving before it has finished materialising —
    /// which on the 2.85 s hound would be a creature that never becomes solid at all.
    /// </summary>
    private static void TheWindowNeverOutlastsTheApparition(Harness t)
    {
        t.Case("dissolve: the two windows never overlap, at any duration");

        for (int i = 1; i <= 2000; i++)
        {
            float len = i * 0.01f;                       // 0.01 .. 20 s
            float e = MathF.Min(HauntFigures.EdgeSeconds, len / 3f);
            t.True(2f * e <= len + 1e-6f, $"window fits three times over at len {len:F2}");

            // The midpoint of ANY run is solid. That is the overlap test stated as a picture rather
            // than as an inequality, and it is what actually fails if the clamp is ever loosened.
            t.True(HauntFigures.DissolveCutout(0.5f * len, len) == 0f, $"the midpoint is solid at len {len:F2}");
        }
    }

    /// <summary>Every duration an event could author, including the degenerate ones. A zero or
    /// negative length is a bug upstream, and the safe answer to it is "gone" — an apparition that
    /// does not draw is a missing effect; one that draws solid at full albedo is the complaint this
    /// whole lane exists to close.</summary>
    private static void TheWindowSurvivesEveryDuration(Harness t)
    {
        t.Case("dissolve: degenerate durations fail to GONE, not to solid");

        Near(t, "len 0 -> gone", HauntFigures.DissolveCutout(0.5f, 0f), 1f, 1e-6f);
        Near(t, "negative len -> gone", HauntFigures.DissolveCutout(0.5f, -3f), 1f, 1e-6f);
        Near(t, "NaN len -> gone", HauntFigures.DissolveCutout(0.5f, float.NaN), 1f, 1e-6f);
        Near(t, "NaN t -> gone", HauntFigures.DissolveCutout(float.NaN, 5f), 1f, 1e-6f);

        // A run far shorter than one window still resolves rather than spending the whole thing
        // half-materialised: e = len/3 shrinks with it.
        float tiny = 0.03f;
        t.True(HauntFigures.DissolveCutout(0.5f * tiny, tiny) == 0f, "a 30 ms run still reaches solid");
    }

    /// <summary>Before it starts and after it ends the figure is GONE, not solid. The presence
    /// envelope already switches the renderers off there; this is the second, independent guard, and
    /// it is what makes a lost race on the renderer switch invisible rather than a pop.</summary>
    private static void TheWindowIsGoneOutsideTheRun(Harness t)
    {
        t.Case("dissolve: outside its own run the figure is gone");
        const float len = CellarWindowSeconds;
        foreach (float tt in new[] { -10f, -1f, -0.001f, 0f, len, len + 0.001f, len + 1f, len + 60f })
        {
            Near(t, $"gone at t {tt:F3}", HauntFigures.DissolveCutout(tt, len), 1f, 1e-6f);
            t.True(HauntFigures.Dissolving(tt, len), $"toggle on at t {tt:F3}");
        }
    }

    /// <summary>
    /// THE MULTIPLAYER GUARANTEE, WRITTEN AS AN ASSERTION. The standing requirement is that an
    /// apparition is a pure function of the SHARED environment clock with zero wire traffic, so two
    /// clients in the same room with the same switches on must dissolve in lockstep. That holds only
    /// while nothing in the path is stateful, frame-rate dependent or seeded from local time — and
    /// the cheap way to notice a regression is to evaluate the same instants in a DIFFERENT ORDER
    /// and demand bit equality, because every one of those defects makes the answer depend on how
    /// you got there.
    /// </summary>
    private static void TwoClientsAgree(Harness t)
    {
        t.Case("dissolve: two clients on the same clock get bit-identical answers");

        foreach (float len in new[] { CellarWindowSeconds, WalkingHoundSeconds })
        {
            // Client A walks the run forwards, one frame at a time, at 90 Hz.
            var a = new float[541];
            for (int i = 0; i < a.Length; i++)
                a[i] = HauntFigures.DissolveCutout(i / 90f, len);

            // Client B is a slower machine that renders every third frame, in reverse, and it
            // evaluated an unrelated apparition in between. Same clock, same answers.
            var b = new float[a.Length];
            for (int i = a.Length - 1; i >= 0; i -= 3)
            {
                _ = HauntFigures.DissolveCutout(i / 37f, 9.5f);        // someone else's figure
                b[i] = HauntFigures.DissolveCutout(i / 90f, len);
            }

            for (int i = a.Length - 1; i >= 0; i -= 3)
                t.True(BitConverter.SingleToInt32Bits(a[i]) == BitConverter.SingleToInt32Bits(b[i]), $"bit-identical at frame {i} (len {len:F2})");
        }
    }

    // ---- THE BURN EDGE ---------------------------------------------------------------------------

    /// <summary>
    /// THE COMPLAINT THIS ONE EXISTS TO STOP COMING BACK. <c>_CindersGlow</c> is an emissive term the
    /// character shader ADDS after lighting, so no albedo lever can reach it, and at its authored 2.0
    /// in a room this dark it is a bright orange band — ModBuild 152 switched the whole dissolve off
    /// because of it. The dissolve is now back, so the edge needs a bound rather than a switch, and
    /// the bound has to be stated against the FIGURE and not against the room: an edge that outshines
    /// the body it is eating is the "voll angestrahlt" report arriving through a new door.
    ///
    /// <para>The figure luminances here are the ones measured off the ModBuild 152 photographs times
    /// the level this build fits, so if either the fit or the cinder share moves, this notices.</para>
    /// </summary>
    private static void TheBurnEdgeCannotOutshineTheFigure(Harness t)
    {
        t.Case("cinders: the burn edge stays inside a small multiple of the figure's own p90");

        float woodEdge = HauntFigures.CinderGlow(WoodLevel) * HauntFigures.CinderColourLuma;
        float cellarEdge = HauntFigures.CinderGlow(CellarLevel) * HauntFigures.CinderColourLuma;

        t.True(woodEdge > WoodFigureP90, $"wood edge {woodEdge:F4} is visible against figure p90 {WoodFigureP90:F4}");
        t.True(woodEdge < 3f * WoodFigureP90, $"wood edge {woodEdge:F4} does not outshine the figure by more than 3x");
        t.True(cellarEdge < 3f * CellarFigureP90, $"cellar edge {cellarEdge:F4} does not outshine the figure by more than 3x");

        // ...and it is nowhere near the authored 2.0, which is what the picture looked like before.
        float authored = 2.0f * HauntFigures.CinderColourLuma;
        t.True(authored > 10f * WoodFigureP90, "the authored glow really would have outshone the figure");
        t.True(woodEdge * 5f < authored, "the driven glow is at least 5x under the authored one");
    }

    /// <summary>The edge rides the room, so a dissolving figure in the cellar glows a fraction of
    /// what one in the wood does — from the same one constant, which is what stops the two rooms
    /// needing two numbers again.</summary>
    private static void TheBurnEdgeTracksTheRoom(Harness t)
    {
        t.Case("cinders: the edge scales with the room and is off in the dark");

        t.True(HauntFigures.CinderGlow(CellarLevel) < HauntFigures.CinderGlow(WoodLevel), "the cellar's edge is dimmer than the wood's");
        Near(t, "the edge ratio IS the room ratio",
               HauntFigures.CinderGlow(WoodLevel) / HauntFigures.CinderGlow(CellarLevel),
               WoodLevel / CellarLevel, 0.01f);

        float prev = -1f;
        for (int i = 0; i <= 200; i++)
        {
            float g = HauntFigures.CinderGlow(i * 0.005f);
            t.True(g >= 0f, $"glow is never negative at level {i * 0.005f:F3}");
            t.True(g >= prev - 1e-6f, $"glow is monotone at level {i * 0.005f:F3}");
            prev = g;
        }
        Near(t, "no light, no ember", HauntFigures.CinderGlow(0f), 0f, 1e-6f);
    }
}
