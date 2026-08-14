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
        Deterministic(t);
        DegenerateInputs(t);
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
}
