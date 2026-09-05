// HELD-FIGURE SIZE — the round trip of extension record 30, and the interaction volume that has
// to grow with it.
//
// WHY THESE TWO THINGS SHARE A FILE: they are the two halves of one user report (3-player hardware
// session, 2026-08-15, verbatim):
//
//   1) "Die Größen der Figuren synchronisieren nicht richtig. Die Größe einer Figur MUSS zwingend
//      immer 1:1 genau die sein die der Spieler auch in der Hand hat - hier gab es oft einen
//      Desync."
//   2) "(eventuell hat das was mit den Figuren Größen zu tun), man kann man vereinzelend Figuren
//      die man größer gezogen hat nicht mehr so einfach Kleiner machen weil die Area zu
//      interagieren nicht mit gewachsen ist."
//
// He was right that they are related: both are the SAME number — the size a held mini is actually
// rendered at — used in two places that had each re-derived it locally instead of reading it.
//
// WHAT ONLY A TEST CAN CATCH HERE. Record 30's numbers are quantized u16 milli-factors with a
// fail-closed envelope, and that envelope had to WIDEN by two orders of magnitude when the record
// started carrying the whole held size instead of the gesture factor alone. A too-narrow envelope
// does not throw and does not log: it silently renders the mini at board size, i.e. it looks like
// "the stretch did not sync" — the exact symptom of the bug being fixed, produced by the fix. And
// the capture ceiling is observed ONLY by feel, from inside a headset, one hardware round at a
// time; a ceiling that stops growing produces a player who cannot reach their own figure and
// nothing else. Both are driven here value by value.

using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class HeldSizeVectors
{
    /// <summary>The tolerance the round trip is asserted to. The wire step is an ABSOLUTE 0.001 of
    /// board size, so a value that survives quantization is within half a step — 0.0005 — of what
    /// went in. Chosen as the quantizer's own bound rather than a comfortable slack: a looser
    /// number would pass a codec that had silently lost a factor of ten at one end.</summary>
    private const float HalfStep = 0.0005f;

    private static float Lo => NetProtocol.HeldStretchCodeMin / 1000f;
    private static float Hi => NetProtocol.HeldStretchCodeMax / 1000f;

    public static void Run(Harness t)
    {
        Codec(t);
        Envelope(t);
        RecordRoundTrip(t);
        CaptureCeiling(t);
        InteractionRadius(t);
    }

    // ---------------------------------------------------------------------------------------
    //  1. THE CODEC — encode → decode across the whole range, including both clamps.
    // ---------------------------------------------------------------------------------------
    private static void Codec(Harness t)
    {
        t.Case("30a. held-size codec, round trip across the full range");

        // Every size the feature can legitimately produce, from a mini shrunk at a deep zoom-in to
        // one grabbed at the far end of a zoom-out and then stretched. The two endpoints are the
        // envelope itself; the rest are the sizes the 2026-08-15 logs actually contain.
        float[] inside =
        {
            Lo, 0.05f, 0.1f, 0.25f, 0.443f, 0.487f, 0.5f, 0.9f, 0.999f,
            1f, 1.001f, 1.048f, 1.072f, 1.129f, 1.5f, 1.605f, 1.762f,
            2f, 2.463f, 3f, 4.751f, 6f, 6.77f, 8f, 8.584f, 10f, 19.7f, 30f, 45f, Hi,
        };
        foreach (float f in inside)
        {
            ushort code = NetProtocol.EncodeHeldStretch(f);
            float back = NetProtocol.DecodeHeldStretch(code);
            t.True(back > 0f && Abs(back - f) <= HalfStep,
                   $"{f:0.####}x survives the round trip (code {code} -> {back:0.####}x, "
                   + $"|delta| {Abs(back - f):0.######} <= {HalfStep})");
            t.True(code >= NetProtocol.HeldStretchCodeMin && code <= NetProtocol.HeldStretchCodeMax,
                   $"{f:0.####}x encodes INSIDE the envelope (code {code})");
        }

        // MONOTONE. A codec that inverts anywhere would make a player's pull outward shrink their
        // mini on a peer's screen, which no single-value assertion above can see.
        ushort prev = 0;
        bool monotone = true;
        for (float f = 0.001f; f <= 70f; f *= 1.05f)
        {
            ushort code = NetProtocol.EncodeHeldStretch(f);
            if (code < prev)
                monotone = false;
            prev = code;
        }
        t.True(monotone, "the encoder is monotone non-decreasing over 0.001x .. 70x");

        // BELOW AND ABOVE THE ENVELOPE: the ENCODER clamps to the bound (a legitimate sender is
        // pulled onto the edge, never rejected), while the DECODER fails closed to board size (a
        // corrupt or foreign code renders the pre-record picture, never an extreme).
        t.Case("30b. held-size codec, clamps and fail-closed decode");
        t.Equal(NetProtocol.HeldStretchCodeMin, (int)NetProtocol.EncodeHeldStretch(0f),
                "0x encodes to the envelope floor, not to 0");
        t.Equal(NetProtocol.HeldStretchCodeMin, (int)NetProtocol.EncodeHeldStretch(0.0001f),
                "a sub-floor size clamps UP to the floor");
        t.Equal(NetProtocol.HeldStretchCodeMax, (int)NetProtocol.EncodeHeldStretch(1000f),
                "an absurd size clamps DOWN to the ceiling");
        t.Equal(NetProtocol.HeldStretchCodeMax, (int)NetProtocol.EncodeHeldStretch(float.MaxValue),
                "and so does float.MaxValue (no u16 wrap)");
        t.Equal(NetProtocol.HeldStretchCodeNeutral, (int)NetProtocol.EncodeHeldStretch(float.NaN),
                "NaN degrades to neutral");
        t.Equal(NetProtocol.HeldStretchCodeNeutral,
                (int)NetProtocol.EncodeHeldStretch(float.PositiveInfinity),
                "+Inf degrades to neutral, NOT to the ceiling");
        t.Equal(NetProtocol.HeldStretchCodeNeutral,
                (int)NetProtocol.EncodeHeldStretch(float.NegativeInfinity),
                "-Inf degrades to neutral");
        t.Equal(NetProtocol.HeldStretchCodeNeutral, (int)NetProtocol.EncodeHeldStretch(1f),
                "1.0x is exactly the neutral code, so 'board size' and 'record absent' agree");
        t.True(NetProtocol.DecodeHeldStretch(NetProtocol.HeldStretchCodeNeutral) == 1f,
               "and the neutral code decodes to exactly 1.0");

        t.True(NetProtocol.DecodeHeldStretch(0) == 1f,
               "code 0 — a zeroed or truncated buffer — decodes to board size");
        t.True(NetProtocol.DecodeHeldStretch(NetProtocol.HeldStretchCodeMin - 1) == 1f,
               "one below the floor fails closed to board size, not to the floor");
        t.True(NetProtocol.DecodeHeldStretch(NetProtocol.HeldStretchCodeMax + 1) == 1f,
               "one above the ceiling fails closed to board size, not to the ceiling");
        t.True(NetProtocol.DecodeHeldStretch(65535) == 1f,
               "and so does a saturated u16");
        t.True(NetProtocol.DecodeHeldStretch(NetProtocol.HeldStretchCodeMin) == Lo
               && NetProtocol.DecodeHeldStretch(NetProtocol.HeldStretchCodeMax) == Hi,
               "while the bounds themselves are believed exactly");
    }

    // ---------------------------------------------------------------------------------------
    //  2. THE ENVELOPE must enclose every size the feature can produce.
    // ---------------------------------------------------------------------------------------
    private static void Envelope(Harness t)
    {
        t.Case("30c. held-size envelope encloses the sizes the game can produce");

        // The measured span of the 2026-08-15 session. `[Size] ... grab-time size CLAMP` lines on
        // the three machines report the zoom a grab stood at in default-zoom units: 0.152x at the
        // low end (remote2) and 10x at the high end (host, remote1, remote2). The wire factor is
        // board-relative, so the reachable band is roughly
        //     [StretchScaleMin x (1/10) .. StretchScaleMax x (1/0.152)]
        // with the shipped dials 0.5 .. 3, i.e. about 0.05 .. 19.7.
        const float worstSmall = 0.5f / 10f;      // 0.05x
        const float worstLarge = 3f / 0.152f;     // 19.7x
        t.True(Lo <= worstSmall,
               $"the floor {Lo:0.###}x is at or below the smallest measured reachable size "
               + $"{worstSmall:0.###}x — a legitimate sender must never be rejected");
        t.True(Hi >= worstLarge,
               $"the ceiling {Hi:0.###}x is at or above the largest measured reachable size "
               + $"{worstLarge:0.###}x");
        t.True(Lo > 0f && Hi < 65.535f,
               "and both bounds stay strictly inside the u16 milli range, so 0 and a saturated "
               + "word both remain OUTSIDE the envelope and keep failing closed");
        t.True(NetProtocol.HeldStretchCodeMin < NetProtocol.HeldStretchCodeNeutral
               && NetProtocol.HeldStretchCodeNeutral < NetProtocol.HeldStretchCodeMax,
               "neutral is strictly inside the envelope (it has to be reachable)");
    }

    // ---------------------------------------------------------------------------------------
    //  3. THE RECORD — the extremes really survive the serializer, not just the codec.
    // ---------------------------------------------------------------------------------------
    private static void RecordRoundTrip(Harness t)
    {
        t.Case("30d. held-size record, the envelope extremes survive the serializer");
        var ext = new byte[PresenceSerializer.MaxSize];

        (ushort P, ushort S, string What)[] cases =
        {
            ((ushort)NetProtocol.HeldStretchCodeMin, (ushort)NetProtocol.HeldStretchCodeMax,
             "floor in the primary slot, ceiling in the secondary"),
            ((ushort)NetProtocol.HeldStretchCodeMax, (ushort)NetProtocol.HeldStretchCodeMin,
             "and the same pair the other way round"),
            (2463, (ushort)NetProtocol.HeldStretchCodeNeutral,
             "a stretched primary beside an untouched secondary"),
            (300, 19700, "the two ends of the measured session band"),
        };

        foreach ((ushort p, ushort s, string what) in cases)
        {
            int m = PresenceSerializer.Write(new PresenceState
            {
                HasHeldStretch = true,
                HeldStretchPrimaryCode = p,
                HeldStretchSecondaryCode = s,
            }, ext);
            t.True(PresenceSerializer.TryRead(ext, m, out PresenceState got), $"{what}: it parses");
            t.True(got.HasHeldStretch, $"{what}: the record is delivered");
            t.Equal((int)p, (int)got.HeldStretchPrimaryCode, $"{what}: primary code intact");
            t.Equal((int)s, (int)got.HeldStretchSecondaryCode, $"{what}: secondary code intact");

            // The whole point of the round trip: what the receiver RENDERS from the delivered code
            // must equal what the sender meant, to the quantizer's own tolerance.
            float wantP = NetProtocol.DecodeHeldStretch(p);
            float wantS = NetProtocol.DecodeHeldStretch(s);
            t.True(NetProtocol.DecodeHeldStretch(got.HeldStretchPrimaryCode) == wantP
                   && NetProtocol.DecodeHeldStretch(got.HeldStretchSecondaryCode) == wantS,
                   $"{what}: both slots decode to the sender's own values, bit for bit");
        }

        // A POISONED SLOT MUST NOT COST THE SOUND ONE ITS SIZE — the same guarantee the reader
        // already gives, re-stated at the NEW envelope so a future narrowing of the bounds cannot
        // quietly make a legitimate large size read as garbage.
        t.Case("30e. held-size record, per-slot sanitation at the new envelope");
        int n = PresenceSerializer.Write(new PresenceState
        {
            HasHeldStretch = true,
            HeldStretchPrimaryCode = 65535,                                  // garbage
            HeldStretchSecondaryCode = (ushort)NetProtocol.HeldStretchCodeMax, // legitimate
        }, ext);
        t.True(PresenceSerializer.TryRead(ext, n, out PresenceState mixed), "the mixed record parses");
        t.Equal(NetProtocol.HeldStretchCodeNeutral, (int)mixed.HeldStretchPrimaryCode,
                "the garbage slot reads as board size");
        t.Equal(NetProtocol.HeldStretchCodeMax, (int)mixed.HeldStretchSecondaryCode,
                "and the legitimate ceiling-sized slot keeps its value");
    }

    // ---------------------------------------------------------------------------------------
    //  4. THE CAPTURE CEILING — item 2. Monotone, bounded, and never tighter than it was.
    // ---------------------------------------------------------------------------------------
    private static void CaptureCeiling(Harness t)
    {
        t.Case("30f. stretch capture ceiling grows with the figure");

        t.True(FigureStretchMath.CaptureCeilingRealMeters(1f)
               == FigureStretchMath.CeilingAtUnitSizeRealMeters,
               "at board size the ceiling is EXACTLY the constant every build through 156 used — "
               + "nothing that already worked can tighten");

        // MONOTONE NON-DECREASING. This is the property the bug was the absence of: the ceiling was
        // constant while the figure grew, so past a certain size it began excluding the figure's own
        // renderers and the capture test fell back to the centre distance.
        bool monotone = true;
        float prev = -1f;
        for (float r = 0.01f; r <= 40f; r *= 1.07f)
        {
            float c = FigureStretchMath.CaptureCeilingRealMeters(r);
            if (c < prev)
                monotone = false;
            prev = c;
        }
        t.True(monotone, "the ceiling is monotone non-decreasing over 0.01x .. 40x total size");

        // BOUNDED. [FigureGrab] StretchLimits can be switched off entirely, so the INPUT is not
        // bounded; the output must be, or one broken renderer on a huge mini captures the room.
        t.True(FigureStretchMath.CaptureCeilingRealMeters(1e6f)
               == FigureStretchMath.CeilingHardCapRealMeters,
               "an unbounded input saturates at the hard cap");
        t.True(FigureStretchMath.CaptureCeilingRealMeters(float.MaxValue)
               <= FigureStretchMath.CeilingHardCapRealMeters,
               "and float.MaxValue cannot exceed it");
        for (float r = 0.001f; r <= 1e5f; r *= 3f)
            t.True(FigureStretchMath.CaptureCeilingRealMeters(r)
                   <= FigureStretchMath.CeilingHardCapRealMeters,
                   $"the ceiling stays under the hard cap at total size {r:0.###}x");

        // THE SHRINK-SIDE FLOOR — the symmetry. A mini pulled DOWN must not have its renderers
        // judged by a proportionally smaller ceiling; that would be the same bug with the sign
        // flipped (a figure excluded for being small).
        t.Case("30g. stretch capture ceiling never tightens when the figure shrinks");
        foreach (float r in new[] { 0.001f, 0.01f, 0.05f, 0.1f, 0.167f, 0.5f, 0.9f, 0.999f, 1f })
            t.True(FigureStretchMath.CaptureCeilingRealMeters(r)
                   == FigureStretchMath.CeilingAtUnitSizeRealMeters,
                   $"a hold at total size {r:0.###}x keeps the full unit-size ceiling");

        // DEGENERATE INPUT must read as 1x — a bounds feature may never be the thing that breaks a
        // gesture.
        t.Case("30h. stretch capture ceiling, degenerate input");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity,
                                      0f, -1f, -1e9f })
            t.True(FigureStretchMath.CaptureCeilingRealMeters(bad)
                   == FigureStretchMath.CeilingAtUnitSizeRealMeters,
                   "a degenerate total size falls back to the unit-size ceiling, never to 0");

        // THE MEASURED CASES. These radii are read out of the 2026-08-15 logs by renderer name, on
        // figures the player was mid-gesture on; every one of them was EXCLUDED by the old fixed
        // 0.5 m ceiling, which is what collapsed the capture zone back onto the mini's centre.
        t.Case("30i. stretch capture ceiling admits the renderers the session logs show excluded");
        (string Renderer, float RadiusReal, float TotalRatio, string Where)[] measured =
        {
            ("MO_DeepTerror_Mesh", 0.79f, 3f, "host, DeepTerrorID at the top of its gesture"),
            ("MO_DeepTerror_Mesh", 0.82f, 3f, "remote1, same figure"),
            ("WP_Berserker_Axe",   0.53f, 3f, "host, BerserkerID grabbed at a clamped 3x"),
            ("WP_Berserker_Axe",   0.52f, 3f, "remote1, same figure"),
            ("BitsEffect",         0.71f, 3f, "host, DeepTerrorEliteID"),
        };
        foreach ((string rend, float radius, float total, string where) in measured)
            t.True(FigureStretchMath.CaptureCeilingRealMeters(total) >= radius,
                   $"'{rend}' at {radius:0.##} m real is INSIDE the ceiling for a {total:0.#}x hold "
                   + $"({FigureStretchMath.CaptureCeilingRealMeters(total):0.##} m) — {where}");

        // …and the ceiling still throws out what it exists to throw out: the Elementalist's idle FX
        // implied a 2.65 m figure radius on the host at a hold of total size 1x.
        t.True(FigureStretchMath.CaptureCeilingRealMeters(1f) < 2.65f,
               "'Elementalist_Idle_FX' at 2.65 m real on a 1x hold is still EXCLUDED — the ceiling "
               + "grew with the figure, it did not stop guarding");
    }

    // ---------------------------------------------------------------------------------------
    //  5. THE INTERACTION RADIUS — same input, same answer, on every machine.
    // ---------------------------------------------------------------------------------------
    private static void InteractionRadius(Harness t)
    {
        t.Case("30j. stretch interaction radius is monotone, bounded and side-independent");

        const float reach = 0.08f; // the shipped [FigureGrab] StretchReachMillimeters

        // STRICTLY INCREASING in the body radius: a bigger figure is a bigger target, always.
        float prev = -1f;
        bool increasing = true;
        for (float body = 0f; body <= 3f; body += 0.01f)
        {
            float r = FigureStretchMath.InteractionRadiusRealMeters(body, reach);
            if (r <= prev)
                increasing = false;
            prev = r;
        }
        t.True(increasing, "the radius strictly increases with the figure's body radius");

        // BOUNDED whenever its inputs are — it is a sum, so the bound is the sum of the bounds; the
        // thing that must not happen is a radius that runs away while the body radius does not.
        t.True(FigureStretchMath.InteractionRadiusRealMeters(
                   FigureStretchMath.CeilingHardCapRealMeters, reach)
               <= FigureStretchMath.CeilingHardCapRealMeters + reach,
               "at the largest body the ceiling can admit, the radius is capped + reach and no more");

        // SIDE-INDEPENDENT: a function of nothing but its two arguments, so the owner and any
        // mirror asking with the same numbers get the same answer bit for bit. (The peer never
        // needs a grab volume of its own — a remotely-held figure is not grabbable at all,
        // FigureGrabbable.CanGrab consults NetHeldFigures.Owns — but the number it would compute
        // must still agree, or the two machines disagree about what the player can reach.)
        foreach (float body in new[] { 0f, 0.01f, 0.26f, 0.53f, 0.79f, 0.82f, 3f })
        {
            float a = FigureStretchMath.InteractionRadiusRealMeters(body, reach);
            float b = FigureStretchMath.InteractionRadiusRealMeters(body, reach);
            t.True(a == b && a == body + reach,
                   $"body {body:0.##} m + reach {reach:0.##} m = {a:0.###} m, identically on both sides");
        }

        // Garbage in must not produce a negative or non-finite reach.
        t.Case("30k. stretch interaction radius, degenerate input");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            t.True(FigureStretchMath.InteractionRadiusRealMeters(bad, reach) == reach,
                   "a degenerate body radius contributes 0, leaving the tuned reach alone");
            t.True(FigureStretchMath.InteractionRadiusRealMeters(0.5f, bad) == 0.5f,
                   "a degenerate reach contributes 0, leaving the body alone");
        }
        t.True(FigureStretchMath.InteractionRadiusRealMeters(float.NaN, float.NaN) == 0f,
               "and both degenerate gives 0, never NaN");
    }

    // -------------------------------------------------------------------------------------------
    //  30l-30n. THE TWO SIZE-BOUND HALVES, now that they are ONE copy each (2026-09-05).
    // -------------------------------------------------------------------------------------------
    //
    // GrabTimeSizeClamp and StretchFactorBounds were, until this round, twenty and ten
    // statement-for-statement identical lines in BOTH FigureGrabbable and GrabbableProp — the prop
    // copies' own doc comments said so ("the figure's, on a prop"; "line for line"). Nothing held
    // them together: neither pair was registered with scripts/check-mirrors.sh, and they had
    // already drifted once (only the figure clamp logged its trim). They are one function each now,
    // and these are the vectors that keep the shared one honest.
    internal static void RunBounds(Harness t)
    {
        t.Case("30l. grab-time size clamp");

        // The ordinary case: a grab-zoom ratio inside the bound is reported and NOT trimmed.
        t.True(!FigureStretchMath.GrabTimeSizeClamp(2f, 1f, true, 0.5f, 4f,
                                                   out float ratio, out float clamped)
               && Abs(ratio - 2f) < 1e-5f,
               "a ratio inside [Min..Max] is measured and reported, and no trim is asked for");

        // Above the bound: trimmed exactly ONTO it, never past it — "solle sie die Maximalgroesse
        // in der Hand haben".
        t.True(FigureStretchMath.GrabTimeSizeClamp(8f, 1f, true, 0.5f, 4f, out ratio, out clamped)
               && Abs(ratio - 8f) < 1e-5f && Abs(clamped - 4f) < 1e-5f,
               "a ratio above Max is trimmed to land exactly ON Max, and the raw ratio is still "
               + "reported so the log can say what was measured");
        t.True(FigureStretchMath.GrabTimeSizeClamp(0.25f, 1f, true, 0.5f, 4f, out ratio, out clamped)
               && Abs(clamped - 0.5f) < 1e-5f,
               "…and symmetrically at the bottom: a ratio below Min lands exactly on Min");

        // LIMITS OFF keeps the true grab-zoom size, whatever it is — the latch ratio is still
        // measured and reported, because the capture ceiling scales by it either way.
        t.True(!FigureStretchMath.GrabTimeSizeClamp(8f, 1f, false, 0.5f, 4f, out ratio, out clamped)
               && Abs(ratio - 8f) < 1e-5f,
               "with StretchLimits off nothing is trimmed, but the ratio is still measured");

        // DEGENERATE INPUT LEAVES THE LATCH ALONE and reads as 1x. A bounds feature must never be
        // the thing that breaks a grab — that sentence is in both call sites' doc comments and this
        // is what makes it true.
        t.Case("30m. grab-time size clamp, degenerate input");
        foreach (var pair in new[]
                 {
                     new[] { 0f, 1f }, new[] { 1f, 0f }, new[] { -1f, 1f }, new[] { 1f, -1f },
                     new[] { 1e-9f, 1f }, new[] { 1f, 1e-9f },
                     new[] { float.NaN, 1f }, new[] { 1f, float.NaN },
                     new[] { float.PositiveInfinity, 1f },
                 })
        {
            t.True(!FigureStretchMath.GrabTimeSizeClamp(pair[0], pair[1], true, 0.5f, 4f,
                                                       out ratio, out clamped)
                   && ratio == 1f,
                   $"baseScale {pair[0]} / anchorScale {pair[1]} asks for no trim and reads as 1x");
        }

        t.Case("30n. stretch factor bounds");
        // total = ratio * factor, so the factor envelope is the total bound divided by the ratio.
        FigureStretchMath.StretchFactorBounds(true, 0.01f, 0.5f, 4f, 2f,
                                             out float min, out float max);
        t.True(Abs(min - 0.25f) < 1e-5f && Abs(max - 2f) < 1e-5f,
               "at a latch ratio of 2x the factor envelope is the total bound halved — the product "
               + "of the two is what [FigureGrab] StretchScaleMin/Max actually bounds");

        FigureStretchMath.StretchFactorBounds(false, 0.01f, 0.5f, 4f, 2f, out min, out max);
        t.True(min == 0.01f && max == float.MaxValue,
               "with limits off only the technical floor remains: the scale must stay positive and "
               + "finite, and nothing else is asserted");

        // The divide guard is a guard, not a policy: a zero latch ratio must not hand the gesture
        // an infinite envelope.
        FigureStretchMath.StretchFactorBounds(true, 0.01f, 0.5f, 4f, 0f, out min, out max);
        t.True(!float.IsInfinity(min) && !float.IsInfinity(max) && !float.IsNaN(min)
               && !float.IsNaN(max),
               "a zero latch ratio is floored before the divide, so the envelope stays finite");
    }

    private static float Abs(float v) => v < 0f ? -v : v;
}
