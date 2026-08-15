using GloomhavenVR.Core;
// The game's own UnityEngine.CoreModule, exactly as the csproj explains: Mathf's rounding and its
// MoveTowards are the ones the shipped build runs, so the gate driver below walks the same floats
// the headset does.
using UnityEngine;

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
        TheWideningTrain(t);
        TheShelfScatter(t);
        Deterministic(t);
        DegenerateInputs(t);

        PoissonGapIsBounded(t);
        PoissonGapSurvivesEveryInput(t);
        PoissonGapClusters(t);
        PoissonGapCannotWoodpecker(t);

        TheFireCrackleCannotWoodpecker(t);
        TheFireCrackleRateIsDezent(t);
        TheFireBurstsFitTheirBuffers(t);

        TheShippedCandleBedWasAWind(t);
        TheWindClipIsSilentWithoutAir(t);
        FireRaisesOnlyWhatIsOnFire(t);
        TheCandleAndTheWindAreIndependent(t);
        TheGatesLeaveAndArriveWithoutAStep(t);
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
    /// THE WIDENING TRAIN — a burst whose gaps OPEN UP, which is the direction the creak does not go.
    ///
    /// <para><b>THE CALLER THESE NUMBERS CAME FROM IS DELETED, AND THE VECTORS ARE NOT.</b> They are
    /// the ice sound's: seven cracks over 0.006..0.360 s at shrink 1.35, and ModBuild 149 removed
    /// that whole cue on the user's ruling ("Entferne das Geräusch für Eis komplett"). The numbers
    /// stay because what they hold is a property of <see cref="EnvSoundSchedule.SlipTrain"/> and not
    /// of any one sound: that shrink &gt; 1 really does open the gaps up, that the window is still
    /// spanned exactly when it does, and that a large jitter moves the train off its own geometric
    /// envelope instead of merely scaling it. The shelf's scatter (below) is the live caller that
    /// depends on all three; deleting the vectors with the sound would have removed the only
    /// coverage the NEW caller has, on the day it arrived.</para>
    /// </summary>
    private static void TheWideningTrain(Harness t)
    {
        t.Case("slip train: shrink > 1 opens the gaps up");
        var pure = new float[7];
        EnvSoundSchedule.SlipTrain(pure, 0.006f, 0.360f, 1.35f, 0f, 0x1CE0u);   // no jitter: envelope
        float firstGap = pure[1] - pure[0];
        float lastGap = pure[6] - pure[5];
        t.True(lastGap > firstGap * 2f,
               $"the last gap ({lastGap * 1000f:F1} ms) is over twice the first "
               + $"({firstGap * 1000f:F1} ms) — the burst thins out and stops instead of ending on "
               + "a beat");

        t.Case("slip train: the widening burst with its full jitter");
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

    /// <summary>
    /// THE BOOKSHELF'S SCATTER (ModBuild 149) — the LIVE caller of a widening train, driven with
    /// <c>EnvSoundBank.MakeFall</c>'s own numbers: nine objects over 0.035..0.42 s at shrink 1.30
    /// with jitter 0.70.
    ///
    /// <para>WHY IT IS ON THIS HARNESS AT ALL, when the generic cases above already cover the
    /// contract. Because the caller INDEXES A SAMPLE BUFFER with these times and adds a 12 ms tick
    /// at each — so "monotonic and inside the window" is not a nicety here, it is what keeps a
    /// <c>for</c> loop inside an array. And because this is the sound the user has been unable to
    /// hear twice: a scatter that collapsed onto one instant would put nine ticks on top of each
    /// other and turn the term that says "a full bookcase went over" into a single click.</para>
    /// </summary>
    private static void TheShelfScatter(Harness t)
    {
        t.Case("slip train: the bookshelf's scatter as MakeFall actually calls it");
        var thrown = new float[9];
        int n = EnvSoundSchedule.SlipTrain(thrown, 0.035f, 0.42f, 1.30f, 0.70f, 0xC7ACu);

        t.Equal(9, n, "all nine objects written");
        t.True(thrown[0] == 0.035f, "the first object lands 35 ms behind the crack");
        t.True(thrown[8] == 0.42f, "the last one closes the window — the clip is 0.9 s, so it fits");

        bool rising = true, inside = true, distinct = true;
        for (int i = 0; i < thrown.Length; i++)
        {
            if (thrown[i] < 0.035f || thrown[i] > 0.42f)
                inside = false;
            if (i == 0)
                continue;
            if (!(thrown[i] > thrown[i - 1]))
                rising = false;
            // Two objects inside 4 ms are one object as far as the ear is concerned, and the
            // scatter's whole job is to be a COUNT of arrivals rather than one thicker transient.
            if (thrown[i] - thrown[i - 1] < 0.004f)
                distinct = false;
        }
        t.True(rising, "every object lands after the one before it");
        t.True(inside, "every object is inside the window MakeFall sized its buffer for");
        t.True(distinct, "no two objects land within 4 ms of each other");

        // The heavy things go first and together; what is left tumbles further and arrives later.
        // Asserted on the pure envelope, because with jitter this is a tendency and not a rule.
        var pure = new float[9];
        EnvSoundSchedule.SlipTrain(pure, 0.035f, 0.42f, 1.30f, 0f, 0xC7ACu);
        t.True(pure[8] - pure[7] > (pure[1] - pure[0]) * 2f,
               $"the scatter thins out: the last gap ({(pure[8] - pure[7]) * 1000f:F1} ms) is over "
               + $"twice the first ({(pure[1] - pure[0]) * 1000f:F1} ms)");

        // ...and the whole train has to be OVER before the clip is, or the generator would be
        // writing a tick that the buffer bound silently truncates to nothing.
        t.True(thrown[8] + 0.012f < 0.9f,
               "the last object's 12 ms tick still ends inside the 0.9 s clip");
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
    //  THE FUNCTION HAS NO CALLER IN THE MOD SINCE ModBuild 149, AND THESE VECTORS STAY. Its one
    //  caller was EnvSound.TickFrost, deleted with the whole ice sound on the user's ruling
    //  ("Entferne das Geräusch für Eis komplett"). What is being held here was never a sound: it is
    //  a bound on an arithmetic result, and the next scheduler that wants a Poisson interval will
    //  reach for this function precisely because it already has one. Vectors that were deleted with
    //  their caller would leave that next caller writing `-mean * Log(u)` from scratch, which is
    //  where the defect below comes from every time.
    //
    //  WHY IT IS ON THIS HARNESS AT ALL, given that it contains no loop and therefore cannot spin.
    //  Because a CALLER does. A scheduler writes `next = clock + PoissonGap(...)` and then returns
    //  every frame until the clock reaches it, which turns two arithmetic results into the same
    //  class of defect the burst train's `while` was:
    //    * A GAP OF ZERO makes the cue fire on every single frame — 90 one-shots a second through
    //      a three-voice pool, which is the loudest failure this feature is capable of and would
    //      arrive without a single log line.
    //    * A GAP OF NaN OR INFINITY makes it fire never, silently. `clock < NaN` is false, so the
    //      "not yet" guard falls through and the "schedule the next" line runs every frame; and a
    //      re-anchor of the form `clock < next - 30f` is also false against NaN. The feature would
    //      simply be gone with no way to tell it apart from a missing node.
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
    /// a wobble, which is what the ice sound's fixed 0.45 s beat was without even the wobble.
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
    /// separate events. With the mean that cue used (2.6 s) the floor clamp alone puts the SHORTEST
    /// possible gap at 0.73 s, so a schedule built on this function cannot return to a woodpecker
    /// even if every draw came back at its minimum. The cue itself is deleted; the BOUND is what the
    /// next caller inherits, and it is the reason to reach for this function rather than a log.
    /// </summary>
    private static void PoissonGapCannotWoodpecker(Harness t)
    {
        t.Case("poisson gap: a 2.6 s mean can never beat like a metronome");
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

    // =============================================================================================
    //  THE FIRE'S CRACKLE (EnvSound.TickFire), ModBuild 152 — the caller PoissonGap was kept for.
    // =============================================================================================
    //
    //  The four constants below MIRROR EnvSound's and EnvSoundBank's, restated here for the reason
    //  TheCreakThatFroze restates the creak's window: this project is a plugin against Unity and
    //  EnvSound.cs cannot be compiled into this harness (it needs AudioSource, AudioClip and the
    //  whole audio module), while EnvSoundSchedule.cs deliberately can. What is being held is not
    //  the constants but the CONSEQUENCE of them — that no value the schedule can produce lands the
    //  fire back in the band the ear reads as a rhythm, which is the failure the ice cue was deleted
    //  for and the single largest risk in adding a repeating cue to this feature.

    /// <summary>Mirror of <c>EnvSound.FireGapCalm</c> / <c>FireGapFull</c>: the mean seconds between
    /// crackles at one site, just-caught and fully alight, lerped on the Fire element.</summary>
    private const float FireGapCalm = 4.0f;
    private const float FireGapFull = 2.2f;

    /// <summary>Mirror of <c>EnvSound.FireSites</c>.</summary>
    private const int FireSites = 3;

    /// <summary>
    /// THE ICE CUE'S DEFECT, ASSERTED AGAINST THE CUE THAT REPLACED IT. "Das was aktuell drin ist ist
    /// super nervig" was a 0.45 s repeat, inside the 0.2-2 s band the ear reads as a RHYTHM rather
    /// than as separate events — and the fire's crackle is the first cue since to repeat at all.
    ///
    /// <para>The bound is checked at EVERY Fire strength, not only at the extremes, because the mean
    /// is LERPED on a live element value: a schedule that was safe at 0 and at 1 and dipped in the
    /// middle would be a defect nobody would think to look for. The floor clamp alone puts the
    /// shortest possible gap at 0.62 s even at full Fire and even if every draw came back at its
    /// minimum.</para>
    /// </summary>
    private static void TheFireCrackleCannotWoodpecker(Harness t)
    {
        t.Case("fire crackle: no Fire strength can make it beat like a metronome");
        float worst = float.MaxValue;
        float worstAt = 0f;
        for (int f = 0; f <= 100; f++)
        {
            float fire = f / 100f;
            float mean = FireGapCalm + (FireGapFull - FireGapCalm) * fire;
            for (int i = 0; i <= 200; i++)
            {
                float g = EnvSoundSchedule.PoissonGap(mean, i / 200f);
                if (g >= worst)
                    continue;
                worst = g;
                worstAt = fire;
            }
        }
        t.True(worst > 0.6f,
               $"the shortest gap ANY Fire strength can produce is {worst:F3} s (at Fire {worstAt:F2}), "
               + "which must stay clear of the 0.45 s beat the user called \"super nervig\"");

        t.Case("fire crackle: and it cannot go silent either");
        float longest = 0f;
        for (int i = 1; i <= 200; i++)
            longest = System.Math.Max(longest, EnvSoundSchedule.PoissonGap(FireGapCalm, i / 200f));
        t.True(longest < 11f,
               $"the longest gap a just-caught fire can produce is {longest:F2} s — the ceiling clamp "
               + "is what stops an exponential's unbounded tail leaving a lit fire silent for half a "
               + "minute, which reads as the feature having broken");
    }

    /// <summary>
    /// THE STANDING RULE, AS A NUMBER. "Auch hier sollen die sounds eher dezent sein und nie
    /// aufdringlich überlagernd" is the acceptance criterion for the whole feature, and for a
    /// REPEATING cue the thing that decides it is the rate across the ROOM rather than at one site —
    /// there are three lit sites in each room and the ear counts all of them.
    ///
    /// <para>Both ends are asserted, because both are failures. Too fast is the user's complaint;
    /// too slow is a fire that ticks instead of crackling, which is the layer not doing its job. The
    /// 0.962 is <see cref="EnvSoundSchedule.PoissonGap"/>'s own published truncation factor, and it
    /// is MEASURED here off a uniform sweep rather than quoted, so a clamp that moved without its
    /// documentation fails this case as well as the one above.</para>
    /// </summary>
    private static void TheFireCrackleRateIsDezent(Harness t)
    {
        t.Case("fire crackle: the room's rate stays inside the \"dezent\" band");
        const int n = 4000;
        float fullSum = 0f, calmSum = 0f;
        for (int i = 1; i <= n; i++)
        {
            fullSum += EnvSoundSchedule.PoissonGap(FireGapFull, i / (float)n);
            calmSum += EnvSoundSchedule.PoissonGap(FireGapCalm, i / (float)n);
        }
        float fullMean = fullSum / n;
        float calmMean = calmSum / n;

        float fullRate = FireSites / fullMean;
        float calmRate = FireSites / calmMean;
        t.True(fullRate > 1.0f && fullRate < 2.0f,
               $"a fully alight room crackles {fullRate:F2} times a second across its {FireSites} "
               + "sites — under 1 is a tick rather than a fire, over 2 is the drip's mistake made "
               + "worse");
        t.True(calmRate > 0.5f && calmRate < fullRate,
               $"and a just-caught one {calmRate:F2} times a second, which must be SLOWER — the mean "
               + "is lerped on the element so that a fire being fed sounds like one");

        t.True(System.Math.Abs(fullMean / FireGapFull - 0.962f) < 0.01f,
               $"the realised mean is {fullMean / FireGapFull:F3} of the nominal, against the 0.962 "
               + "the clamp arithmetic predicts — the rate above is only meaningful while that holds");
    }

    /// <summary>
    /// THE BURSTS INSIDE THE CLIPS. A crackle is not one pop and an ember settle is not one thud:
    /// both are short trains laid into a fixed buffer by <see cref="EnvSoundSchedule.SlipTrain"/>,
    /// and the generator turns each time into a sample index and writes a decaying tail from it.
    ///
    /// <para>So this is the MonotonicAndInBounds property again, aimed at the two newest callers and
    /// at the thing that actually bounds them: the last event plus its own TAIL has to fit inside the
    /// buffer, or the generator silently truncates the end of the last pop — which is a click, and a
    /// click at the end of a clip that fires every couple of seconds is precisely the artefact this
    /// feature cannot afford. The numbers mirror <c>EnvSoundBank</c>'s Crackle* and Ember*
    /// constants.</para>
    /// </summary>
    private static void TheFireBurstsFitTheirBuffers(Harness t)
    {
        t.Case("fire crackle: every pop, plus its tail, is inside the 55 ms buffer");
        int[] pops = { 3, 4, 5, 4 };
        uint[] crackleSeeds = { 0xC7AC1E00u, 0xC7AC1E01u, 0xC7AC1E02u, 0xC7AC1E03u };
        for (int v = 0; v < pops.Length; v++)
        {
            var train = new float[pops[v]];
            EnvSoundSchedule.SlipTrain(train, 0.0004f, 0.034f, 1.25f, 0.75f, crackleSeeds[v]);
            bool rising = true;
            for (int i = 1; i < train.Length; i++)
            {
                if (!(train[i] > train[i - 1]))
                    rising = false;
            }
            t.True(rising, $"crackle {v}: the pops are strictly ordered, so none overwrites another");
            // 6 ms of pop tail on top of the last time, against a 55 ms buffer.
            t.True(train[train.Length - 1] + 0.006f < 0.055f,
                   $"crackle {v}: the last pop ends at {(train[train.Length - 1] + 0.006f) * 1000f:F1} ms, "
                   + "inside the 55 ms clip — a truncated tail is a click");
        }

        t.Case("fire ember: every thud, plus its tail, is inside the 220 ms buffer");
        int[] ticks = { 4, 3 };
        uint[] emberSeeds = { 0xE0BE0000u, 0xE0BE0001u };
        for (int v = 0; v < ticks.Length; v++)
        {
            var train = new float[ticks[v]];
            EnvSoundSchedule.SlipTrain(train, 0.001f, 0.115f, 1.45f, 0.65f, emberSeeds[v]);
            t.True(train[0] == 0.001f && train[train.Length - 1] == 0.115f,
                   $"ember {v}: the train spans its authored window exactly");
            // 50 ms of thud tail, against a 220 ms buffer.
            t.True(train[train.Length - 1] + 0.05f < 0.22f,
                   $"ember {v}: the last thud ends at {(train[train.Length - 1] + 0.05f) * 1000f:F1} ms, "
                   + "inside the 220 ms clip");
        }
    }

    // =============================================================================================
    //  THE BED LEVELS (EnvSound.WindBed / CandleBed / FireBed), ModBuild 153 — the regression that
    //  would have caught the shipped bug.
    // =============================================================================================
    //
    //  THE USER REPORT THIS SECTION EXISTS FOR, ModBuild 152 hardware, verbatim:
    //
    //      "Beim Feuer Geräusch ist auch immer das Wind geräusch mit dabei. Das soll nicht sein.
    //       Das Wind gEräusch soll nur dann kommen wenn Wind auch aktiv ist."
    //
    //  ...and it is the SECOND time he has had to say it. ModBuild 147: "Wind Geräusch nur wenn auch
    //  Wind aktiv ist, sonst kein Geräusch". ModBuild 148 built THE WIND GATE, put the window
    //  Draught and the swamp Leaves behind it, and left the cellar's three candle beds playing
    //  EnvSoundClip.Bed — the wind buffer itself — with no gate at all and with a FIRE term in their
    //  gain. So the wind clip was audible with Air fully off, and infusing Fire made it louder.
    //
    //  WHY A LINT OR A REVIEW WOULD NOT HAVE CAUGHT IT AND THIS DOES. The fault was not a wrong
    //  number, it was three correct-looking arguments to one call — a clip, a modulator and a
    //  filter — none of which is checkable from the line itself. What IS checkable is the
    //  CONSEQUENCE, and the consequence is arithmetic: with Air at zero, the total level of every
    //  emitter that plays the wind buffer must be EXACTLY zero, for every time and every element
    //  state there is, INCLUDING Fire fully infused. That is one assertion and it fails loudly on
    //  the shipped code (see TheShippedCandleBedWasAWind, which drives the shipped lambda and shows
    //  it failing).
    //
    //  THE LEVEL MATH IS MIRRORED HERE, for the reason the fire's four constants above are: this
    //  project is a plugin against Unity and EnvSound.cs cannot be compiled into this harness (it
    //  needs AudioSource, AudioClip, AudioListener and the whole audio module), while
    //  EnvSoundSchedule.cs deliberately can. scripts/check-mirrors.sh cannot register these either —
    //  its extractor resolves every site under src/GloomhavenVR and has no test-file form — which is
    //  the same position TheFireCrackleRateIsDezent's mirrors are already in. What is held is not
    //  the constants but the PROPERTY, and a drift in a constant cannot make the property pass
    //  falsely: every assertion below is about a level being exactly zero, or about one level moving
    //  while another does not, and neither survives a gate being wired to the wrong element.
    //
    //  WHAT THIS SECTION CANNOT HOLD, stated so nobody trusts it further than it goes. It holds the
    //  ARITHMETIC and not the WIRING. BuildCellar's `AddBed(..., clip, modulator, ...)` call lives in
    //  EnvSound.cs, which is not on this harness, so no case here can see which lambda a given clip
    //  was actually handed — and that pairing IS the shipped defect. Two mechanisms cover the half
    //  this cannot:
    //    * EnvSound.AddBed WARNS AT BUILD if an emitter declares EnvSoundClip.Bed without
    //      airGated: true. That is the wiring, checked where it is declared.
    //    * EnvSound.TickBeds WARNS AT RUNTIME ("ENV SOUND WIND LEAK") if a wind-clip bed's modulator
    //      returns anything but zero while Air is down. That is the consequence, checked on the
    //      device, and it is what would have turned the last two user reports into one log line.
    //  This section is the third leg: it proves the level functions those two lean on are correct,
    //  so a WIND LEAK line can only ever mean a mis-wired emitter and never a broken gate.

    // ---- mirrors of EnvSound's gate constants ---------------------------------------------------
    private const float AirGateOn = 0.06f;
    private const float AirGateFull = 0.45f;
    private const float AirGateOpenSeconds = 0.9f;
    private const float AirGateCloseSeconds = 2.4f;

    private const float FireGateOn = 0.05f;
    private const float FireGateFull = 0.40f;
    private const float FireGateOpenSeconds = 0.7f;
    private const float FireGateCloseSeconds = 1.6f;

    /// <summary>Mirror of <c>EnvSound.CandleFireLift</c> — how much a full Fire infusion lifts the
    /// CANDLE bed, on top of its resting 0.72..1.00. It was 0.90 until ModBuild 153, on a bed that
    /// was playing the wind buffer.</summary>
    private const float CandleFireLift = 0.30f;

    /// <summary>The four beds' authored gains, mirrored from <c>EnvSound.BuildCellar</c>,
    /// <c>BuildSwamp</c> and <c>FireBedGain</c>. They are here so that "the total level of every
    /// wind bed" is a level and not a modulator value — the user's complaint is about what he hears,
    /// which is gain times modulator.</summary>
    private const float DraughtGain = 0.075f;
    private const float LeavesGain = 0.070f;
    private const float CandleGain = 0.055f;
    private const float FireGain = 0.16f;

    /// <summary>
    /// EnvSound's three gated level functions and the two gates behind them, driven frame by frame.
    ///
    /// <para>The gates are STATEFUL — <c>Mathf.MoveTowards</c> per frame at an asymmetric rate — so
    /// this is a driver and not a table of pure functions. That matters for what it can catch: a
    /// gate that opened on the wrong element, or one that never closed, is a defect nobody can see
    /// in a single evaluation.</para>
    /// </summary>
    private sealed class Beds
    {
        internal float WindGate;
        internal float FireGate;
        internal float Clock;      // stands in for Time.time, which drives the LFOs only

        /// <summary>One frame. <paramref name="air"/> and <paramref name="fire"/> are the live
        /// element intensities <c>ElementMood.Live(2)</c> / <c>Live(0)</c>.</summary>
        internal void Step(float dt, float air, float fire)
        {
            Clock += dt;

            float wt = Mathf.Clamp01((Mathf.Clamp01(air) - AirGateOn)
                                     / Mathf.Max(AirGateFull - AirGateOn, 1e-4f));
            wt = wt * wt * (3f - 2f * wt);
            WindGate = Mathf.MoveTowards(WindGate, wt,
                                         dt / Mathf.Max(wt > WindGate ? AirGateOpenSeconds
                                                                      : AirGateCloseSeconds, 0.01f));

            float ft = Mathf.Clamp01((Mathf.Clamp01(fire) - FireGateOn)
                                     / Mathf.Max(FireGateFull - FireGateOn, 1e-4f));
            ft = ft * ft * (3f - 2f * ft);
            FireGate = Mathf.MoveTowards(FireGate, ft,
                                         dt / Mathf.Max(ft > FireGate ? FireGateOpenSeconds
                                                                      : FireGateCloseSeconds, 0.01f));
        }

        private float Lfo(float period) =>
            0.5f + 0.5f * Mathf.Sin(Clock * (2f * Mathf.PI / Mathf.Max(period, 0.1f)));

        /// <summary>Mirror of <c>EnvSound.WindBed</c>. Returns a LITERAL zero with the gate shut,
        /// which is what <c>TickBeds</c> tests to PAUSE the source.</summary>
        internal float WindBed(float period, float air)
        {
            if (WindGate <= 0f)
                return 0f;
            return WindGate * (0.55f + 0.45f * Lfo(period) + 1.0f * Mathf.Clamp01(air));
        }

        /// <summary>Mirror of <c>EnvSound.FireBed</c>.</summary>
        internal float FireBed(float period, float fire)
        {
            if (FireGate <= 0f)
                return 0f;
            return FireGate * (0.58f + 0.14f * Lfo(period) + 0.28f * Mathf.Clamp01(fire));
        }

        /// <summary>Mirror of <c>EnvSound.CandleBed</c> — ModBuild 153's. NOT gated on anything: the
        /// candles are alight for the whole scenario.</summary>
        internal float CandleBed(float period, float fire) =>
            0.72f + 0.28f * Lfo(period) + CandleFireLift * Mathf.Clamp01(fire);

        /// <summary>THE SHIPPED ModBuild 152 CANDLE LAMBDA, verbatim, kept so the regression can be
        /// shown failing rather than asserted to have existed. It played EnvSoundClip.Bed.</summary>
        internal float ShippedCandleBed(float fire) =>
            0.72f + 0.28f * Lfo(3.11f) + 0.9f * fire;
    }

    /// <summary>Every element state worth driving, as (air, fire) pairs — the corners plus the two
    /// places a gate has a knee (its ON threshold and its FULL point), because a threshold crossed
    /// is where a level function is most likely to be wrong by a hair.</summary>
    private static readonly float[] ElementSweep =
    {
        0f, 0.001f, AirGateOn, 0.2f, AirGateFull, 0.7f, 1f,
    };

    /// <summary>
    /// THE DEFECT, DRIVEN. The shipped candle bed is evaluated with Air at zero — every time, every
    /// Fire — and it is non-zero everywhere and RISES WITH FIRE. That is the user's sentence turned
    /// into arithmetic: the clip it multiplied was EnvSoundClip.Bed, the wind buffer.
    ///
    /// <para>It is a vector rather than a comment for the reason <see cref="TheCreakThatFroze"/> is:
    /// a defect that is only described is a defect the next reader may reintroduce believing it was
    /// a style choice. This case FAILS if somebody restores the old lambda, because the case below
    /// it asserts the opposite of what this one measures.</para>
    /// </summary>
    private static void TheShippedCandleBedWasAWind(Harness t)
    {
        t.Case("bed levels: the ModBuild 152 candle bed played the wind clip, ungated and fire-lit");
        var b = new Beds();
        float quiet = 0f, lit = 0f;
        for (int i = 0; i < 400; i++)
        {
            b.Step(1f / 90f, 0f, 0f);          // AIR ZERO, FIRE ZERO — the gate stays shut
            quiet += b.ShippedCandleBed(0f);
            lit += b.ShippedCandleBed(1f);
        }
        t.True(b.WindGate == 0f, "the wind gate is shut throughout — there is no Air anywhere in this case");
        t.True(quiet > 0f,
               $"the shipped candle bed averaged {quiet / 400f:F3} of its gain with Air at zero, on a "
               + "clip that IS the window draught — \"sonst kein Geräusch\" was not true of it");
        t.True(lit / quiet > 1.9f,
               $"and a full Fire infusion multiplied that wind by {lit / quiet:F2}x "
               + $"({20f * System.Math.Log10(lit / quiet):F1} dB) — \"beim Feuer Geräusch ist auch "
               + "immer das Wind Geräusch mit dabei\", as a number");
    }

    /// <summary>
    /// THE REGRESSION. With Air at zero, the TOTAL level of every emitter that plays the wind buffer
    /// is exactly zero — across a long sweep of frames and across every Fire state, including fully
    /// infused. Exactly zero and not "small": <c>EnvSound.TickBeds</c> tests <c>want &lt;= 0</c> to
    /// PAUSE the source, so a level of 1e-7 is not a quiet wind, it is a wind bed that never stops
    /// being spatialised.
    ///
    /// <para>THE SUM IS OVER BOTH ROOMS' WIND BEDS AT ONCE even though only one can exist at a time,
    /// deliberately: the assertion is about the CLIP and not about a room, and a future room that
    /// added a third wind emitter should be added to this sum rather than tested separately.</para>
    /// </summary>
    private static void TheWindClipIsSilentWithoutAir(Harness t)
    {
        t.Case("bed levels: with Air at 0 the wind clip's total level is EXACTLY zero");
        float worst = 0f;
        string worstAt = string.Empty;
        foreach (float fire in ElementSweep)
        {
            var b = new Beds();
            // Come from a fully OPEN gate, so this also holds while the gate is closing and after it
            // has closed — a bed that leaked only on the way down would pass a fresh-start test.
            for (int i = 0; i < 300; i++)
                b.Step(1f / 90f, 1f, fire);
            t.True(b.WindGate > 0.99f, $"fire {fire:F2}: the gate really did open first");

            for (int i = 0; i < 900; i++)     // 10 s at 90 Hz — four times the 2.4 s close
            {
                b.Step(1f / 90f, 0f, fire);
                if (b.WindGate > 0f)
                    continue;                 // still closing; the gate's own ramp is not a leak
                float total = DraughtGain * b.WindBed(7.93f, 0f) + LeavesGain * b.WindBed(11.31f, 0f);
                if (total <= worst)
                    continue;
                worst = total;
                worstAt = $"fire {fire:F2}, frame {i}, gate {b.WindGate:F6}";
            }
            t.True(b.WindGate == 0f, $"fire {fire:F2}: the gate reaches a HARD zero, not an asymptote "
                                     + "— MoveTowards lands on the target and a lerp would not");
        }
        t.True(worst == 0f,
               $"the loudest the wind clip ever reached with Air down was {worst:E3} ({worstAt}) — it "
               + "must be exactly 0, because TickBeds pauses on `want <= 0` and anything above it is "
               + "a source that keeps running");

        t.Case("bed levels: and a full Fire infusion cannot open the wind gate");
        var f = new Beds();
        for (int i = 0; i < 900; i++)
            f.Step(1f / 90f, 0f, 1f);
        t.True(f.WindGate == 0f && f.FireGate > 0.99f,
               $"after 10 s of full Fire with no Air: wind gate {f.WindGate:F3} (must be 0), fire gate "
               + $"{f.FireGate:F3} (must be open) — the two gates read different elements and this is "
               + "the case that says so");
        t.True(f.WindBed(7.93f, 0f) == 0f && f.WindBed(11.31f, 0f) == 0f,
               "so both wind beds are at a literal zero while the room is fully on fire");
    }

    /// <summary>
    /// WHAT A FIRE INFUSION IS ALLOWED TO RAISE, and by how much. The seated fires, from nothing;
    /// the candles, by a flare; the wind, not at all.
    ///
    /// <para>The candle's lift is asserted as a BAND rather than as a value. Zero would be wrong —
    /// <c>EnvFlame.shader</c> scales a candle flame by (1 + 1.05·fire) in brightness and names that
    /// as a standing design, so a silent candle under Fire is the picture and the sound disagreeing
    /// about one object. And the old 0.90 was wrong the other way: it made the candles the room's
    /// whole fire response, which is what put a wind under a Fire infusion for five builds.</para>
    /// </summary>
    private static void FireRaisesOnlyWhatIsOnFire(Harness t)
    {
        t.Case("bed levels: a Fire infusion raises the fires and flares the candles, and moves no wind");

        // Two rooms, identical but for the Fire element, driven the same number of frames from the
        // same start — so every difference below is Fire and nothing else.
        var cold = new Beds();
        var hot = new Beds();
        float coldFire = 0f, hotFire = 0f, coldCandle = 0f, hotCandle = 0f, coldWind = 0f, hotWind = 0f;
        for (int i = 0; i < 900; i++)
        {
            cold.Step(1f / 90f, 1f, 0f);
            hot.Step(1f / 90f, 1f, 1f);
            coldFire += FireGain * cold.FireBed(8.17f, 0f);
            hotFire += FireGain * hot.FireBed(8.17f, 1f);
            coldCandle += CandleGain * cold.CandleBed(3.11f, 0f);
            hotCandle += CandleGain * hot.CandleBed(3.11f, 1f);
            coldWind += DraughtGain * cold.WindBed(7.93f, 1f);
            hotWind += DraughtGain * hot.WindBed(7.93f, 1f);
        }

        t.True(coldFire == 0f,
               "with no Fire the fire beds are at a literal zero — TickBeds pauses them, so a lit "
               + "room costs nothing when nothing is lit");
        t.True(hotFire > 0f, $"and with Fire they play (mean {hotFire / 900f:F4} of full scale)");

        float candleLift = 20f * (float)System.Math.Log10(hotCandle / coldCandle);
        t.True(candleLift > 1.5f && candleLift < 4f,
               $"the candles FLARE by {candleLift:F2} dB — enough to answer EnvFlame's own "
               + "(1 + 1.05*fire) brightness, and far under the +6.2 dB the ModBuild 152 lambda "
               + "applied to a clip that was the window draught");

        t.True(hotWind == coldWind,
               $"and the wind bed is BIT-IDENTICAL between the two rooms ({hotWind:F6} vs "
               + $"{coldWind:F6}) — no term in WindBed reads the Fire element, and that is the "
               + "property the user's report is literally about");
    }

    /// <summary>
    /// THE TWO BEDS DO NOT KNOW ABOUT EACH OTHER, asserted in both directions because the shipped
    /// defect was one direction of it and the obvious over-correction is the other.
    ///
    /// <list type="number">
    ///   <item>No element state makes the CANDLE bed a function of the wind gate. Gating the candles
    ///   on Air would have "fixed" the report by making the cellar silent, which is not what the
    ///   user asked for — his 147 ruling explicitly keeps what is not wind.</item>
    ///   <item>No element state makes the WIND bed a function of Fire. That is the defect.</item>
    /// </list>
    /// </summary>
    private static void TheCandleAndTheWindAreIndependent(Harness t)
    {
        t.Case("bed levels: the candle bed is not a function of the wind gate");
        foreach (float fire in ElementSweep)
        {
            var noAir = new Beds();
            var fullAir = new Beds();
            bool same = true;
            float openedTo = 0f;
            for (int i = 0; i < 600; i++)
            {
                noAir.Step(1f / 90f, 0f, fire);
                fullAir.Step(1f / 90f, 1f, fire);
                if (noAir.CandleBed(3.11f, fire) != fullAir.CandleBed(3.11f, fire))
                    same = false;
                openedTo = fullAir.WindGate;
            }
            t.True(openedTo > 0.99f, $"fire {fire:F2}: the second room's wind gate really did open");
            t.True(same,
                   $"fire {fire:F2}: the candle bed is bit-identical with the wind gate shut and with "
                   + "it fully open — a candle is not wind, and gating it would answer the report by "
                   + "deleting a sound nobody complained about");
        }

        t.Case("bed levels: the wind bed is not a function of Fire");
        foreach (float air in ElementSweep)
        {
            var noFire = new Beds();
            var fullFire = new Beds();
            bool same = true;
            for (int i = 0; i < 600; i++)
            {
                noFire.Step(1f / 90f, air, 0f);
                fullFire.Step(1f / 90f, air, 1f);
                if (noFire.WindBed(7.93f, air) != fullFire.WindBed(7.93f, air)
                    || noFire.WindBed(11.31f, air) != fullFire.WindBed(11.31f, air))
                    same = false;
            }
            t.True(same,
                   $"air {air:F2}: both wind beds are bit-identical with Fire at 0 and at 1 — this is "
                   + "the assertion that fails the day somebody puts an ElementMood.Live(0) term back "
                   + "into anything that plays EnvSoundClip.Bed");
        }
    }

    /// <summary>
    /// THE GATES ARRIVE AND LEAVE WITHOUT A STEP, which is the argument THE WIND GATE's doc makes
    /// and which nothing has ever checked. A cut is itself an event — the ear tracks offsets as
    /// keenly as onsets — so "kein Geräusch" has to arrive without announcing itself.
    ///
    /// <para>The bound is per FRAME rather than per second, because a step is what the ear hears and
    /// a step happens in one frame. At 90 Hz the wind's 2.4 s close is 0.0046 per frame and the
    /// fire's 0.7 s open is 0.0159; the assertion allows 0.05, which is comfortably above both and
    /// far below anything audible as an edge — it fails only if a gate is made an <c>if</c> again.</para>
    /// </summary>
    private static void TheGatesLeaveAndArriveWithoutAStep(Harness t)
    {
        t.Case("bed levels: neither gate can move more than a hair in one frame");
        var b = new Beds();
        float worstWind = 0f, worstFire = 0f;
        // Full on, full off, and a spike through the middle of both thresholds — the shapes an
        // element channel actually produces when a card resolves.
        float[][] script =
        {
            new[] { 1f, 1f, 300f }, new[] { 0f, 0f, 400f }, new[] { 0.5f, 0.2f, 120f },
            new[] { 0f, 1f, 200f }, new[] { 1f, 0f, 200f }, new[] { 0f, 0f, 400f },
        };
        foreach (float[] leg in script)
        {
            for (int i = 0; i < (int)leg[2]; i++)
            {
                float w = b.WindGate, f = b.FireGate;
                b.Step(1f / 90f, leg[0], leg[1]);
                worstWind = Mathf.Max(worstWind, Mathf.Abs(b.WindGate - w));
                worstFire = Mathf.Max(worstFire, Mathf.Abs(b.FireGate - f));
            }
        }
        t.True(worstWind < 0.05f,
               $"the wind gate's largest single-frame move is {worstWind:F4} of full scale");
        t.True(worstFire < 0.05f,
               $"the fire gate's largest single-frame move is {worstFire:F4} of full scale");
        t.True(b.WindGate == 0f && b.FireGate == 0f,
               "and both are back at a hard zero at the end of the script, so the sources are paused "
               + "rather than spinning at an inaudible level");
    }
}
