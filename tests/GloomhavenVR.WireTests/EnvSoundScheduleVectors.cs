using GloomhavenVR.Core;

namespace GloomhavenVR.WireTests;

/// <summary>
/// THE BURST-TRAIN SCHEDULE (<see cref="EnvSoundSchedule"/>), driven burst by burst.
///
/// <para>WHY IT IS ON THIS HARNESS, and it is the sharpest case on it. ModBuild 145 shipped the
/// creak as <c>while (t &lt; 1.20f)</c> with a step multiplied by 0.90 every pass. The times it
/// produced are a geometric series, that series sums to about 1.09 s, and 1.09 is less than 1.20 —
/// so the condition was never false. The step underflowed to a denormal after ~800 passes,
/// <c>t</c> stopped moving entirely, and the main thread spun forever inside the first
/// <c>EnvSoundBank.Build()</c>, which runs on the frame the environment room is first placed. The
/// user's report was "vollständig beim Ladescreen aufgehängt", and the log gave nothing: no
/// exception (a spin throws none, so the generator's own try/catch was blind), no warning, and no
/// line after the environment's "ROOM placed" a few statements earlier.
///
/// <para>THREE PROPERTIES ARE ASSERTED, and only the first is about the bug that happened:</para>
/// <list type="number">
///   <item>It TERMINATES — structurally, because the loop is a <c>for</c> over the caller's count.
///   Every case in this file is itself that assertion: a regression here does not fail, it hangs
///   the test run, which is a louder failure than a red line.</item>
///   <item>It SPANS THE WINDOW — the last burst lands on <c>last</c> for EVERY shrink factor. This
///   is the property the shipped code lacked: it left the span to be an outcome of the series
///   rather than an input, and a series that undershoots is exactly the freeze above.</item>
///   <item>It stays MONOTONIC and IN BOUNDS — the callers index a sample buffer with these times.</item>
/// </list>
/// </summary>
internal static class EnvSoundScheduleVectors
{
    internal static void Run(Harness t)
    {
        TheCreakThatFroze(t);
        SpansTheWindowAtEveryShrink(t);
        MonotonicAndInBounds(t);
        AcceleratesWhenAsked(t);
        TheFrostBurst(t);
        Deterministic(t);
        DegenerateInputs(t);

        PoissonGapIsBounded(t);
        PoissonGapSurvivesEveryInput(t);
        PoissonGapClusters(t);
        PoissonGapCannotWoodpecker(t);
    }

    /// <summary>
    /// THE SHIPPED DEFECT, as a vector. These are the creak's own numbers — window 0.05..1.20 s,
    /// shrink 0.90 — and the assertion is the one the 145 loop failed: the train REACHES 1.20.
    /// </summary>
    private static void TheCreakThatFroze(Harness t)
    {
        t.Case("slip train: the creak reaches the end of its window");
        var slips = new float[16];
        int n = EnvSoundSchedule.SlipTrain(slips, 0.05f, 1.20f, 0.90f, 0.25f, 0xC2EA00u);

        t.Equal(16, n, "all 16 slips written");
        t.True(slips[0] == 0.05f, "first slip at the start of the window");
        t.True(slips[15] == 1.20f, "last slip ON the end of the window — the 145 loop never got here");

        // And the sum-of-the-series arithmetic that made 145 hang, stated directly: an unnormalised
        // geometric train with this shrink FALLS SHORT of this window. The schedule is only correct
        // because it normalises; this asserts the premise, so a future "simplify away the scale
        // factor" reads as the regression it would be.
        float unnormalised = 0.05f;
        float gap = 0.115f;
        for (int i = 0; i < 4000; i++)
        {
            gap *= 0.90f;
            unnormalised += gap;
        }
        t.True(unnormalised < 1.20f,
               $"the raw geometric series converges to {unnormalised:F3} s, SHORT of the 1.20 s window "
               + "— which is why the shipped `while (t < 1.20f)` could never end");
    }

    /// <summary>
    /// The span is an INPUT. Whatever the shrink does to the rhythm, the first and last bursts sit
    /// on the window — including shrink values no caller uses today, because the next caller is the
    /// one this protects.
    /// </summary>
    private static void SpansTheWindowAtEveryShrink(Harness t)
    {
        t.Case("slip train: the window is an input, not an outcome");
        float[] shrinks = { 0.5f, 0.75f, 0.90f, 1f, 1.15f, 1.6f };
        foreach (float s in shrinks)
        {
            var v = new float[12];
            EnvSoundSchedule.SlipTrain(v, 0.2f, 2.0f, s, 0.2f, 0x1234u);
            t.True(v[0] == 0.2f, $"shrink {s}: first burst at 0.2");
            t.True(v[11] == 2.0f, $"shrink {s}: last burst at 2.0");
        }
    }

    /// <summary>Callers turn these into sample indices, so a non-increasing or out-of-window time
    /// is a buffer overwrite waiting to happen.</summary>
    private static void MonotonicAndInBounds(Harness t)
    {
        t.Case("slip train: strictly increasing, inside the window");
        uint[] seeds = { 1u, 0x5C177E2u, 0xC2EA00u, 0xDEADBEEFu, 0xFFFFFFFFu };
        foreach (uint seed in seeds)
        {
            var v = new float[20];
            EnvSoundSchedule.SlipTrain(v, 0.01f, 0.52f, 0.9f, 0.28f, seed);
            bool rising = true, inside = true;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] < 0.01f || v[i] > 0.52f)
                    inside = false;
                if (i > 0 && !(v[i] > v[i - 1]))
                    rising = false;
            }
            t.True(rising, $"seed {seed:X}: every burst is later than the one before it");
            t.True(inside, $"seed {seed:X}: every burst is inside [0.01, 0.52]");
        }
    }

    /// <summary>The shrink has to actually DO something — a creak whose slips do not come closer
    /// together is a knock, and that is the content half of this function.</summary>
    private static void AcceleratesWhenAsked(Harness t)
    {
        t.Case("slip train: shrink < 1 closes the gaps up");
        var v = new float[16];
        EnvSoundSchedule.SlipTrain(v, 0.05f, 1.20f, 0.90f, 0f, 0xC2EA00u);   // no jitter: pure envelope
        float firstGap = v[1] - v[0];
        float lastGap = v[15] - v[14];
        t.True(lastGap < firstGap * 0.5f,
               $"the last gap ({lastGap:F4} s) is under half the first ({firstGap:F4} s)");

        t.Case("slip train: shrink == 1 is an even beat");
        var even = new float[9];
        EnvSoundSchedule.SlipTrain(even, 0f, 0.8f, 1f, 0f, 7u);
        float g0 = even[1] - even[0];
        bool flat = true;
        for (int i = 2; i < even.Length; i++)
            if (System.Math.Abs((even[i] - even[i - 1]) - g0) > 1e-4f)
                flat = false;
        t.True(flat, "every gap is the same without jitter");
    }

    /// <summary>
    /// THE FROST BURST (ModBuild 148), driven with the generator's own numbers —
    /// <c>EnvSoundBank.MakeFrost</c>'s seven cracks over 0.006..0.360 s at shrink 1.35.
    ///
    /// <para>IT IS THE FIRST CALLER THAT GOES THE OTHER WAY, and that is the reason it is on this
    /// harness rather than trusted to the generic cases above. A creak ACCELERATES because the load
    /// keeps building; a crazing DECELERATES because every crack relieves the stress that drove it,
    /// so the surface has to reload before the next. Both are one function and one shrink factor —
    /// which is only true as long as shrink &gt; 1 really opens the gaps up, and nothing asserted
    /// that until now.</para>
    /// </summary>
    private static void TheFrostBurst(Harness t)
    {
        t.Case("slip train: shrink > 1 opens the gaps up (the frost's crazing)");
        var pure = new float[7];
        EnvSoundSchedule.SlipTrain(pure, 0.006f, 0.360f, 1.35f, 0f, 0x1CE0u);   // no jitter: envelope
        float firstGap = pure[1] - pure[0];
        float lastGap = pure[6] - pure[5];
        t.True(lastGap > firstGap * 2f,
               $"the last gap ({lastGap * 1000f:F1} ms) is over twice the first "
               + $"({firstGap * 1000f:F1} ms) — the burst thins out and stops instead of ending on "
               + "a beat");

        t.Case("slip train: the frost burst as the bank actually calls it");
        var burst = new float[7];
        int n = EnvSoundSchedule.SlipTrain(burst, 0.006f, 0.360f, 1.35f, 0.85f, 0x1CE0u);
        t.Equal(7, n, "all seven cracks written");
        t.True(burst[0] == 0.006f, "the first crack opens the window");
        t.True(burst[6] == 0.360f, "the last crack closes it");

        bool rising = true, inside = true, distinct = true;
        for (int i = 0; i < burst.Length; i++)
        {
            if (burst[i] < 0.006f || burst[i] > 0.360f)
                inside = false;
            if (i > 0)
            {
                if (!(burst[i] > burst[i - 1]))
                    rising = false;
                // The generator turns these into sample indices and ADDS a 14 ms burst at each; two
                // cracks closer together than a millisecond would be one louder crack, which is the
                // one thing the power-law size distribution is there to prevent.
                if (burst[i] - burst[i - 1] < 0.001f)
                    distinct = false;
            }
        }
        t.True(rising, "every crack is later than the one before it");
        t.True(inside, "every crack is inside the burst window");
        t.True(distinct, "no two cracks land within a millisecond of each other");

        // ...and the whole point of jitter 0.85: at that setting the rhythm must NOT be the pure
        // envelope any more, or the burst is a ritardando and the ear hears a ritardando.
        bool jittered = false;
        for (int i = 1; i < 6; i++)
            if (System.Math.Abs(burst[i] - pure[i]) > 1e-4f)
                jittered = true;
        t.True(jittered, "jitter 0.85 actually moves the cracks off the geometric envelope");
    }

    /// <summary>Same seed, same train — the bank is synthesized on every client and an environment
    /// event that sounded different per machine would break the shared-clock contract the whole
    /// feature rests on.</summary>
    private static void Deterministic(Harness t)
    {
        t.Case("slip train: deterministic");
        var a = new float[14];
        var b = new float[14];
        EnvSoundSchedule.SlipTrain(a, 0.05f, 1.2f, 0.9f, 0.25f, 0xC2EA00u);
        EnvSoundSchedule.SlipTrain(b, 0.05f, 1.2f, 0.9f, 0.25f, 0xC2EA00u);
        bool same = true;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                same = false;
        t.True(same, "two calls with one seed produce identical times");

        var c = new float[14];
        EnvSoundSchedule.SlipTrain(c, 0.05f, 1.2f, 0.9f, 0.25f, 0xC2EA01u);
        bool differs = false;
        for (int i = 1; i < a.Length - 1; i++)
            if (a[i] != c[i])
                differs = true;
        t.True(differs, "a different seed produces a different rhythm (the ends still pin)");
    }

    /// <summary>Nothing a caller can pass may hang or overrun — including the shapes no caller
    /// passes today.</summary>
    private static void DegenerateInputs(Harness t)
    {
        t.Case("slip train: degenerate inputs are bounded, not fatal");
        t.Equal(0, EnvSoundSchedule.SlipTrain(null!, 0f, 1f, 0.9f, 0.2f, 1u), "null buffer writes nothing");
        t.Equal(0, EnvSoundSchedule.SlipTrain(new float[0], 0f, 1f, 0.9f, 0.2f, 1u), "empty buffer writes nothing");

        var one = new float[1];
        t.Equal(1, EnvSoundSchedule.SlipTrain(one, 0.3f, 1f, 0.9f, 0.2f, 1u), "a single burst is written");
        t.True(one[0] == 0.3f, "and it sits at the start of the window");

        var backwards = new float[5];
        EnvSoundSchedule.SlipTrain(backwards, 1f, 0.5f, 0.9f, 0.2f, 1u);   // last before first
        bool allFirst = true;
        for (int i = 0; i < backwards.Length; i++)
            if (backwards[i] != 1f)
                allFirst = false;
        t.True(allFirst, "an inverted window collapses onto its start rather than running away");

        var zeroShrink = new float[6];
        EnvSoundSchedule.SlipTrain(zeroShrink, 0f, 1f, 0f, 0f, 1u);
        t.True(zeroShrink[5] == 1f, "shrink 0 still lands the last burst on the window");

        var seedZero = new float[6];
        EnvSoundSchedule.SlipTrain(seedZero, 0f, 1f, 0.9f, 0.3f, 0u);
        t.True(seedZero[5] == 1f, "seed 0 (the xorshift's fixed point) is remapped, not stuck");
    }

    // =============================================================================================
    //  THE WAITING TIME (EnvSoundSchedule.PoissonGap), ModBuild 148.
    // =============================================================================================
    //
    //  WHY IT IS ON THIS HARNESS AT ALL, given that it contains no loop and therefore cannot spin.
    //  Because its CALLER does. EnvSound.TickFrost writes `_nextFrost = clock + PoissonGap(...)`
    //  and then returns every frame until the clock reaches it, which turns two arithmetic results
    //  into the same class of defect the burst train's `while` was:
    //    * A GAP OF ZERO makes the frost fire on every single frame — 90 one-shots a second through
    //      a three-voice pool, which is the loudest failure this feature is capable of and would
    //      arrive without a single log line.
    //    * A GAP OF NaN OR INFINITY makes it fire never, silently. `clock < NaN` is false, so the
    //      "not yet" guard falls through and the "schedule the next" line runs every frame; and the
    //      30-second re-anchor that would otherwise rescue it (`clock < _nextFrost - 30f`) is also
    //      false against NaN. The feature would simply be gone with no way to tell it apart from a
    //      missing node.
    //  `Mathf.Log(0)` is -Infinity and `Mathf.Log(-1)` is NaN, and the input is a HASH — a value
    //  that is documented never to reach 1.0 and is one refactor away from reaching it. So the
    //  function's contract is a BOUNDED, FINITE, STRICTLY POSITIVE result for every float there is,
    //  and these cases pass it every float that has ever broken anything.

    /// <summary>The advertised bound, over the whole legal input range. This is the assertion the
    /// caller actually leans on: whatever the hash says, the next event is between 0.28 and 2.60 of
    /// the mean and there is no third possibility.</summary>
    private static void PoissonGapIsBounded(Harness t)
    {
        t.Case("poisson gap: always inside [0.28, 2.60] x mean");
        float[] means = { 0.05f, 0.5f, 2.6f, 11f, 60f, 999f };
        bool inside = true, positive = true, finite = true;
        foreach (float m in means)
        {
            for (int i = 0; i <= 1000; i++)
            {
                float u = i / 1000f;
                float g = EnvSoundSchedule.PoissonGap(m, u);
                if (float.IsNaN(g) || float.IsInfinity(g))
                    finite = false;
                if (!(g > 0f))
                    positive = false;
                // The tolerance is one part in ten thousand and is for the float multiply alone —
                // the clamp itself is exact.
                if (g < m * EnvSoundSchedule.PoissonGapMin * 0.9999f
                    || g > m * EnvSoundSchedule.PoissonGapMax * 1.0001f)
                    inside = false;
            }
        }
        t.True(finite, "never NaN and never Infinity — either one is an event that never comes");
        t.True(positive, "never zero — a zero gap is a one-shot on every frame");
        t.True(inside, "never outside the advertised window, for any mean or any draw");

        t.Case("poisson gap: the ends land ON the clamps rather than past them");
        t.True(System.Math.Abs(EnvSoundSchedule.PoissonGap(10f, 1f) - 10f * EnvSoundSchedule.PoissonGapMin) < 1e-3f,
               "u = 1 gives -ln(1) = 0, which the floor lifts to 0.28 x mean");
        t.True(System.Math.Abs(EnvSoundSchedule.PoissonGap(10f, 1e-9f) - 10f * EnvSoundSchedule.PoissonGapMax) < 1e-3f,
               "a vanishing u would run away to 207 x mean and is held at 2.60");
    }

    /// <summary>
    /// EVERY INPUT, including the ones a comparison does not catch. NaN is the sharp one: `u &lt; 0`
    /// and `u &gt; 1` are BOTH false for NaN, so the obvious pair of rejecting guards would pass it
    /// straight into the logarithm. The implementation tests positively (`!(u &gt; 0 &amp;&amp; u
    /// &lt;= 1)`) for exactly that reason, and this case is what stops anyone tidying it back.
    /// </summary>
    private static void PoissonGapSurvivesEveryInput(Harness t)
    {
        t.Case("poisson gap: every degenerate input is bounded, not fatal");
        float[] draws =
        {
            0f, 1f, -1f, 2f, 1e30f, -1e30f,
            float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon,
        };
        foreach (float u in draws)
        {
            float g = EnvSoundSchedule.PoissonGap(2.6f, u);
            t.True(g > 0f && !float.IsNaN(g) && !float.IsInfinity(g)
                   && g >= 2.6f * EnvSoundSchedule.PoissonGapMin * 0.9999f
                   && g <= 2.6f * EnvSoundSchedule.PoissonGapMax * 1.0001f,
                   $"draw {u} produced {g}, which must be a finite gap inside the window");
        }

        t.Case("poisson gap: a broken MEAN cannot produce a broken schedule");
        float[] badMeans = { 0f, -1f, -1e30f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e30f };
        foreach (float m in badMeans)
        {
            float g = EnvSoundSchedule.PoissonGap(m, 0.4f);
            t.True(g > 0f && !float.IsNaN(g) && !float.IsInfinity(g) && g < 1f,
                   $"mean {m} produced {g}, which must fall back to the 0.05 s floor rather than to "
                   + "an infinite or negative wait");
        }

        t.Case("poisson gap: NaN lands on the median draw, not on the floor");
        t.True(System.Math.Abs(EnvSoundSchedule.PoissonGap(4f, float.NaN)
                               - EnvSoundSchedule.PoissonGap(4f, 0.5f)) < 1e-6f,
               "an unusable draw is replaced by 0.5, so the schedule keeps its own rate instead of "
               + "quietly collapsing to the fastest one it is allowed");
    }

    /// <summary>
    /// The CONTENT half, and the reason the function exists rather than "mean plus or minus 40%".
    /// An exponential's mode is at zero, so it produces genuine clusters — two events almost
    /// together and then a long nothing — and a distribution that did not would be a metronome with
    /// a wobble, which is what the shipped frost's fixed 0.45 s beat was without even the wobble.
    /// </summary>
    private static void PoissonGapClusters(Harness t)
    {
        t.Case("poisson gap: the draw actually spreads, and reaches both clamps");
        const float mean = 2.6f;
        int atFloor = 0, atCeiling = 0, between = 0;
        float sum = 0f;
        const int n = 2000;
        for (int i = 1; i <= n; i++)
        {
            float g = EnvSoundSchedule.PoissonGap(mean, i / (float)n);
            sum += g;
            if (g <= mean * EnvSoundSchedule.PoissonGapMin * 1.0001f)
                atFloor++;
            else if (g >= mean * EnvSoundSchedule.PoissonGapMax * 0.9999f)
                atCeiling++;
            else
                between++;
        }
        t.True(atFloor > n / 10, $"a real share of draws ({atFloor}/{n}) land on the floor — those "
                                 + "are the CLUSTERS, and a jittered constant has none");
        t.True(atCeiling > 0, $"and some ({atCeiling}/{n}) on the ceiling — the long silences");
        t.True(between > n / 2, $"but most ({between}/{n}) are somewhere in between, so this is a "
                                + "distribution and not a two-state switch");

        // The truncation moves the realised mean to 0.962 of the nominal; PoissonGap's own doc
        // derives that number, and this is it measured over a uniform sweep of the draw.
        float realised = sum / n;
        t.True(System.Math.Abs(realised / mean - 0.962f) < 0.01f,
               $"the realised mean is {realised / mean:F3} of the nominal, against the 0.962 the "
               + "clamp arithmetic predicts — a mismatch here means one of the two clamps moved "
               + "without its documentation");
    }

    /// <summary>
    /// THE USER'S COMPLAINT, AS A BOUND. "Das was aktuell drin ist ist super nervig" was in large
    /// part a 0.45 s repeat — inside the 0.2-2 s band the ear reads as a RHYTHM rather than as
    /// separate events. With the frost's own means (2.6 s at full Ice, 11 s at the threshold) the
    /// floor clamp alone puts the SHORTEST possible gap at 0.73 s, so the schedule cannot return to
    /// a woodpecker even if every draw came back at its minimum.
    /// </summary>
    private static void PoissonGapCannotWoodpecker(Harness t)
    {
        t.Case("poisson gap: the frost can never beat again");
        const float fullIce = 2.6f;
        float shortest = float.MaxValue;
        for (int i = 0; i <= 1000; i++)
        {
            float g = EnvSoundSchedule.PoissonGap(fullIce, i / 1000f);
            if (g < shortest)
                shortest = g;
        }
        t.True(shortest > 0.7f,
               $"the shortest gap the full-Ice schedule can produce is {shortest:F3} s, which is "
               + "clear of the 0.45 s beat the user reported and clear of the top of the band the "
               + "ear reads as rhythm");
    }
}
