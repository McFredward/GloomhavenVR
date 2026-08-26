using System;
using System.IO;
using GloomhavenVR.Voice;

namespace GloomhavenVR.WireTests;

// =================================================================================================
//  VOICE VECTORS — the rolloff in decibels, the indicator's hysteresis, and the loudspeaker glyph,
//  all driven without Unity, without a headset and without a second client.
// =================================================================================================

/// <summary>
/// Gates for spatial voice chat.
///
/// <para><b>WHY THESE EXIST.</b> Everything this feature does is observed by EAR, from inside a
/// headset, with a second person on the other end of a microphone — the least reproducible test in
/// this repo, and the user is on holiday for a week and cannot run it at all. A rolloff 12 dB too
/// steep does not throw, does not log and does not look wrong; it produces a teammate who "cuts
/// out". An icon whose threshold sits where speech level actually lives does not fail either; it
/// strobes, which is the "nervig" verdict this project has already collected once. Both are decided
/// by arithmetic, so both are decided here.</para>
///
/// <para><b>WHAT THIS DOES NOT PROVE, STATED HERE SO NOBODY MISTAKES A GREEN TICK FOR THE
/// FEATURE WORKING.</b> These vectors prove the curve WE AUTHOR has the decibels we claim and that
/// the glyph we rasterise has the shape we claim. They say nothing about whether Unity's audio
/// engine applies that curve, or whether a human finds the result comfortable. The first question
/// belongs to <c>scripts/voice-spatial-probe.sh</c>, which measures a real mixed
/// <c>AudioSource</c>; the second belongs to the headset. See <c>.planning/VOICE-SPATIAL.md</c>.</para>
/// </summary>
internal static class VoiceVectors
{
    // The shipped defaults, restated here as literals ON PURPOSE. Reading them from Defaults.cs
    // would make this test agree with any future edit — a test that builds its own input proves
    // nothing. If somebody retunes the dials, THIS FILE MUST FAIL and the new numbers must be
    // written down deliberately.
    private const float Full = 2f;      // Defaults.VoiceFullLevelMeters
    private const float Silence = 25f;  // Defaults.VoiceSilenceMeters
    private const float Shape = 1.6f;   // Defaults.VoiceRolloffShape

    internal static void Run(Harness t, string repoRoot)
    {
        Curve(t);
        Plateau(t);
        Degenerate(t);
        Keys(t);
        Hysteresis(t);
        Smoothing(t);
        Icon(t);
        Dump(t, repoRoot);
    }

    // ---------------------------------------------------------------------------------------------
    //  THE CURVE, IN DECIBELS — the numbers quoted in VoiceCurve's class doc and in the German
    //  config description, so the documentation cannot drift away from the code.
    // ---------------------------------------------------------------------------------------------
    private static void Curve(Harness t)
    {
        t.Case("voice rolloff, shipped defaults, in dB at perceived metres");

        // Expected values, from ((silence - d) / (silence - full)) ^ shape with full=2, silence=25,
        // shape=1.6, computed to five places OUTSIDE the code under test:
        //   d=4:  (21/23)^1.6 = 0.86454  -> 20*log10 = -1.264 dB
        //   d=6:  (19/23)^1.6 = 0.73651  -> -2.657 dB
        //   d=8:  (17/23)^1.6 = 0.61652  -> -4.204 dB
        //   d=14: (11/23)^1.6 = 0.30725  -> -10.251 dB
        //   d=20: ( 5/23)^1.6 = 0.08704  -> -21.206 dB
        //
        // THE FIRST VERSION OF THIS BLOCK HAD TWO OF THESE WRONG (-2.62 and -10.29, from arithmetic
        // done in my head) AND THE GATE CAUGHT BOTH. That is worth leaving on the record: the whole
        // argument for asserting a curve in decibels is that nobody can eyeball a power function,
        // and the first thing it did was prove it on its own author.
        Db(t, 4f, -1.264f, "4 m");
        Db(t, 6f, -2.657f, "6 m");
        Db(t, 8f, -4.204f, "8 m");
        Db(t, 14f, -10.251f, "14 m");
        Db(t, 20f, -21.206f, "20 m");

        // THE ACCEPTANCE CRITERION, and the only one that matters to a person: speech has to stay
        // intelligible across a room. -6 dB is a comfortable "quieter but perfectly clear"; the
        // requirement is that a teammate anywhere within 10 perceived metres is above it.
        t.True(VoiceCurve.GainDb(10f, Full, Silence, Shape) > -6f,
               "a teammate at 10 perceived metres is still within 6 dB of full level");
        t.True(VoiceCurve.GainDb(3f, Full, Silence, Shape) > -1f,
               "a teammate across a table (3 m) is within 1 dB of full level");

        // And it must actually reach silence, rather than trailing off at some small value.
        t.Equal(0f, VoiceCurve.Gain(Silence, Full, Silence, Shape), "gain is exactly 0 at the silence radius");
        t.Equal(0f, VoiceCurve.Gain(Silence + 10f, Full, Silence, Shape), "gain stays 0 beyond it");
        t.Equal(-120f, VoiceCurve.GainDb(Silence, Full, Silence, Shape), "dB floors at -120 rather than -infinity");
    }

    /// <summary>Assert the curve's decibel value at a distance, to 0.02 dB -- far tighter than any
    /// ear, so a retune shows up as a failed vector and not as a shrug.</summary>
    private static void Db(Harness t, float metres, float expectedDb, string where)
    {
        float actual = VoiceCurve.GainDb(metres, Full, Silence, Shape);
        t.True(Math.Abs(actual - expectedDb) < 0.02f,
               $"rolloff at {where} is {expectedDb:F2} dB (read {actual:F3} dB)");
    }

    // ---------------------------------------------------------------------------------------------
    //  THE PLATEAU — the property the whole curve exists for. Logarithmic rolloff (what EnvSound
    //  uses, and what a careless implementation would reach for) fails every one of these.
    // ---------------------------------------------------------------------------------------------
    private static void Plateau(Harness t)
    {
        t.Case("voice rolloff plateau: level does not move inside the full-volume radius");
        for (int i = 0; i <= 20; i++)
        {
            float d = Full * i / 20f;
            t.Equal(1f, VoiceCurve.Gain(d, Full, Silence, Shape), $"gain is exactly 1 at {d:F2} m");
        }
        t.Equal(1f, VoiceCurve.Gain(0f, Full, Silence, Shape), "gain is 1 at zero distance");
        t.Equal(1f, VoiceCurve.Gain(-5f, Full, Silence, Shape), "a negative distance clamps to zero, not to silence");

        t.Case("voice rolloff is monotonically non-increasing");
        float last = 2f;
        for (int i = 0; i <= 300; i++)
        {
            float d = i * 0.1f;
            float g = VoiceCurve.Gain(d, Full, Silence, Shape);
            t.True(g <= last + 1e-6f, $"gain never rises with distance (at {d:F1} m)");
            last = g;
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  HAND-EDITED CONFIG — every dial has a range, but a .cfg is a text file and a user can put
    //  anything in it. None of these may divide by zero, return NaN, or silence the voice.
    // ---------------------------------------------------------------------------------------------
    private static void Degenerate(Harness t)
    {
        t.Case("voice rolloff survives nonsensical dials");

        t.Equal(1f, VoiceCurve.Gain(1f, 10f, 5f, 1.6f), "plateau beyond the silence radius reads as never attenuate");
        t.Equal(0f, VoiceCurve.Gain(20f, 10f, 5f, 1.6f), "...and still silences past the plateau");
        t.True(!float.IsNaN(VoiceCurve.Gain(3f, 2f, 2f, 1.6f)), "equal plateau and silence is not NaN");
        t.True(!float.IsNaN(VoiceCurve.Gain(3f, 2f, 25f, 0f)), "a zero shape exponent is not NaN");
        t.True(!float.IsNaN(VoiceCurve.Gain(float.NaN, 2f, 25f, 1.6f)), "a NaN distance is not NaN");
        t.Equal(1f, VoiceCurve.Gain(float.NaN, Full, Silence, Shape), "a NaN distance reads as zero distance");
        t.True(!float.IsNaN(VoiceCurve.Gain(3f, -1f, 25f, 1.6f)), "a negative plateau is not NaN");

        // A shape of 1 must be an exact straight line — the documented meaning of the dial.
        float mid = (Full + Silence) * 0.5f;
        t.True(Math.Abs(VoiceCurve.Gain(mid, Full, Silence, 1f) - 0.5f) < 1e-5f,
               "shape 1 is a straight line: the midpoint reads exactly half");
    }

    // ---------------------------------------------------------------------------------------------
    //  THE SAMPLED KEYS handed to Unity's AnimationCurve.
    // ---------------------------------------------------------------------------------------------
    private static void Keys(Harness t)
    {
        t.Case("voice rolloff keys");
        var times = new float[VoiceCurve.KeyCount];
        var values = new float[VoiceCurve.KeyCount];
        VoiceCurve.SampleKeys(times, values, Full, Silence, Shape);

        t.Equal(0f, times[0], "the first key is at distance 0");
        t.Equal(1f, values[0], "...at full gain");
        t.Equal(1f, times[VoiceCurve.KeyCount - 1], "the last key is at the silence radius");
        t.Equal(0f, values[VoiceCurve.KeyCount - 1], "...pinned to exactly zero, so the voice truly stops");

        for (int i = 1; i < VoiceCurve.KeyCount; i++)
        {
            t.True(times[i] > times[i - 1], $"key {i} time is strictly increasing");
            t.True(values[i] <= values[i - 1] + 1e-6f, $"key {i} value never rises");
        }

        // The sampled curve must track the analytic one, or the thing Unity is handed is not the
        // thing this file's decibel table describes.
        for (int i = 0; i < VoiceCurve.KeyCount; i++)
        {
            float metres = times[i] * Silence;
            float want = VoiceCurve.Gain(metres, Full, Silence, Shape);
            if (i == VoiceCurve.KeyCount - 1)
                continue; // pinned key, checked above
            t.True(Math.Abs(values[i] - want) < 1e-5f, $"key {i} matches the analytic gain at {metres:F2} m");
        }

        t.Case("voice rolloff keys reject an undersized buffer rather than overrunning it");
        var small = new float[2];
        VoiceCurve.SampleKeys(small, small, Full, Silence, Shape);
        t.Equal(0f, small[0], "an undersized buffer is left untouched");
    }

    // ---------------------------------------------------------------------------------------------
    //  THE INDICATOR — the failure mode is a strobing icon, so hysteresis is the thing under test.
    // ---------------------------------------------------------------------------------------------
    private static void Hysteresis(Harness t)
    {
        t.Case("speaking indicator: the game's flag is the only ON/OFF authority");
        t.Equal(0, VoiceCurve.StepFor(1f, 3, speaking: false),
                "not speaking is step 0 no matter how loud the audio is");
        t.Equal(0, VoiceCurve.StepFor(0f, 0, speaking: false), "silent and not speaking is step 0");
        t.True(VoiceCurve.StepFor(0f, 0, speaking: true) >= 1,
               "speaking but below every threshold still shows an icon, so it cannot contradict the game's roster");

        t.Case("speaking indicator: a level parked on a threshold does not strobe");
        // 0.070 is exactly the entry threshold for step 2. Sitting on it must hold, not oscillate.
        int step = VoiceCurve.StepFor(0.070f, 1, true);
        t.Equal(2, step, "clearing the step-2 threshold enters step 2");
        for (int i = 0; i < 50; i++)
        {
            int next = VoiceCurve.StepFor(0.070f, step, true);
            t.Equal(step, next, "the step holds while the level sits exactly on its entry threshold");
            step = next;
        }
        // It only falls once the level drops well below where it entered.
        t.Equal(2, VoiceCurve.StepFor(0.050f, 2, true), "a small dip does not drop the step");
        t.Equal(1, VoiceCurve.StepFor(0.040f, 2, true), "a real drop does");

        t.Case("speaking indicator: steps are ordered and bounded");
        t.Equal(3, VoiceCurve.StepFor(0.9f, 0, true), "a loud level reaches the top step");
        t.Equal(3, VoiceCurve.StepFor(9f, 3, true), "an over-range level cannot exceed the top step");
        t.Equal(1, VoiceCurve.StepFor(0.001f, 1, true), "a whisper stays at the bottom lit step");
        for (int i = -3; i < 8; i++)
        {
            int s = VoiceCurve.StepFor(0.5f, i, true);
            t.True(s >= 0 && s <= VoiceCurve.Steps - 1, $"a corrupt previous step ({i}) still yields a valid step");
        }
    }

    private static void Smoothing(Harness t)
    {
        t.Case("speaking indicator: the envelope attacks fast and releases slowly");
        // One 90 Hz frame.
        const float Dt = 1f / 90f;

        float up = VoiceCurve.Smooth(0f, 1f, Dt);
        float down = VoiceCurve.Smooth(1f, 0f, Dt);
        t.True(up > 0.35f, "a syllable lifts the envelope more than a third of the way in one frame");
        t.True(down > 0.9f, "...and one silent frame barely lowers it, so the icon does not flicker between words");
        t.True(up > 1f - down, "attack is faster than release");

        // It must converge, and never overshoot: an envelope above 1 would push the top step on
        // audio that never reached it.
        float e = 0f;
        for (int i = 0; i < 1000; i++)
            e = VoiceCurve.Smooth(e, 0.4f, Dt);
        t.True(Math.Abs(e - 0.4f) < 1e-3f, "the envelope converges on a steady level");
        t.True(e <= 0.4f + 1e-6f, "the envelope never overshoots its input");

        // Frame-rate independence: 90 Hz for 1 s and 45 Hz for 1 s must land in the same place, or
        // the icon behaves differently on the desktop mirror than in the headset.
        float a = 0f, b = 0f;
        for (int i = 0; i < 90; i++) a = VoiceCurve.Smooth(a, 1f, 1f / 90f);
        for (int i = 0; i < 45; i++) b = VoiceCurve.Smooth(b, 1f, 1f / 45f);
        t.True(Math.Abs(a - b) < 0.02f, "the envelope is frame-rate independent (90 Hz vs 45 Hz agree)");

        t.Equal(0f, VoiceCurve.Smooth(0f, float.NaN, Dt), "a NaN reading does not poison the envelope");
    }

    // ---------------------------------------------------------------------------------------------
    //  THE GLYPH. Rasterised by the SHIPPED code, so the invariants below and the images written
    //  next to them describe the thing that actually renders in the headset.
    // ---------------------------------------------------------------------------------------------
    private static void Icon(Harness t)
    {
        t.Case("voice icon raster");
        var frames = new byte[VoiceCurve.Steps][];
        for (int s = 0; s < VoiceCurve.Steps; s++)
        {
            frames[s] = new byte[VoiceIcon.ByteCount];
            VoiceIcon.Render(s, frames[s]);
        }

        // 1. THE CABINET IS THE SAME IN EVERY FRAME. If the speaker body moved or changed size with
        //    the level, the icon would read as a shape change rather than a level.
        for (int s = 1; s < VoiceCurve.Steps; s++)
        {
            int diffLeft = 0;
            for (int y = 0; y < VoiceIcon.Size; y++)
            {
                // The cabinet and horn live left of the horn mouth (0.455 of the width).
                for (int x = 0; x < (int)(VoiceIcon.Size * 0.44f); x++)
                {
                    int i = (y * VoiceIcon.Size + x) * 4;
                    if (frames[s][i] != frames[0][i] || frames[s][i + 3] != frames[0][i + 3])
                        diffLeft++;
                }
            }
            t.Equal(0, diffLeft, $"step {s} leaves the loudspeaker body pixel-identical to step 0");
        }

        // 2. LIT AREA RISES STRICTLY WITH THE STEP. This is "es schlägt aus": more sound, more icon.
        long prev = -1;
        for (int s = 0; s < VoiceCurve.Steps; s++)
        {
            long lit = 0;
            for (int i = 0; i < VoiceIcon.ByteCount; i += 4)
            {
                // "Lit" = near-white AND opaque. The dark rim and the dim unlit arcs are excluded
                // by the brightness test, which is exactly what the eye does.
                if (frames[s][i] > 200 && frames[s][i + 3] > 200)
                    lit++;
            }
            t.True(lit > prev, $"step {s} lights strictly more of the glyph than step {s - 1}");
            prev = lit;
        }

        // 3. THE GLYPH IS ACTUALLY THERE. A rasteriser that produced an empty buffer would pass
        //    every test above (0 == 0, and "strictly more" would fail — but say it plainly anyway).
        int opaque = 0;
        for (int i = 3; i < VoiceIcon.ByteCount; i += 4)
        {
            if (frames[0][i] > 200)
                opaque++;
        }
        t.True(opaque > VoiceIcon.Size * VoiceIcon.Size / 40, "the silent frame is not an empty texture");
        t.True(opaque < VoiceIcon.Size * VoiceIcon.Size * 3 / 4, "...and it is not a filled rectangle either");

        // 4. IT IS VERTICALLY SYMMETRIC. A loudspeaker that leans is a rasteriser bug, and it is
        //    the one defect a thumbnail would not make obvious.
        for (int s = 0; s < VoiceCurve.Steps; s++)
        {
            int asym = 0;
            for (int y = 0; y < VoiceIcon.Size / 2; y++)
            {
                for (int x = 0; x < VoiceIcon.Size; x++)
                {
                    int a = (y * VoiceIcon.Size + x) * 4;
                    int b = ((VoiceIcon.Size - 1 - y) * VoiceIcon.Size + x) * 4;
                    if (Math.Abs(frames[s][a] - frames[s][b]) > 2 || Math.Abs(frames[s][a + 3] - frames[s][b + 3]) > 2)
                        asym++;
                }
            }
            t.Equal(0, asym, $"step {s} is symmetric about the horizontal axis");
        }

        // 5. THE DARK RIM EXISTS. Without it the glyph disappears against a pale wall — this
        //    project has shipped an engraving that was present, drawn and invisible.
        for (int s = 0; s < VoiceCurve.Steps; s++)
        {
            int rim = 0;
            for (int i = 0; i < VoiceIcon.ByteCount; i += 4)
            {
                if (frames[s][i + 3] > 200 && frames[s][i] < 80)
                    rim++;
            }
            t.True(rim > 40, $"step {s} carries an opaque dark rim ({rim} texels) for contrast on any background");
        }

        // 6. THE BUFFER GUARD.
        var tiny = new byte[16];
        VoiceIcon.Render(2, tiny);
        t.Equal((byte)0, tiny[0], "an undersized buffer is left untouched rather than overrun");
    }

    // ---------------------------------------------------------------------------------------------
    //  THE PICTURES. Written by the shipped rasteriser, so what he looks at IS what renders.
    // ---------------------------------------------------------------------------------------------
    private static void Dump(Harness t, string repoRoot)
    {
        t.Case("voice icon renders written to .planning/voice/");
        string dir = Path.Combine(repoRoot, ".planning", "voice");
        Directory.CreateDirectory(dir);

        var rgba = new byte[VoiceIcon.ByteCount];
        for (int s = 0; s < VoiceCurve.Steps; s++)
        {
            VoiceIcon.Render(s, rgba);
            string path = Path.Combine(dir, $"voice-icon-step{s}.ppm");
            WritePpm(path, rgba, VoiceIcon.Size);
            t.True(File.Exists(path), $"step {s} frame written to {path}");
        }

        // The decibel table, as a file, so the curve can be plotted and diffed between rounds
        // without re-deriving it.
        string csv = Path.Combine(dir, "voice-rolloff-authored.csv");
        using (var w = new StreamWriter(csv))
        {
            w.WriteLine("# The rolloff GloomhavenVR AUTHORS, from VoiceCurve.Gain (the shipped code).");
            w.WriteLine("# This is NOT a measurement of Unity's audio engine — that is voice-spatial-probe.");
            w.WriteLine($"# full_level_m={Full} silence_m={Silence} shape={Shape}");
            w.WriteLine("distance_m_perceived,gain,gain_db");
            for (int i = 0; i <= 300; i++)
            {
                float d = i * 0.1f;
                w.WriteLine($"{d:F2},{VoiceCurve.Gain(d, Full, Silence, Shape):F6}," +
                            $"{VoiceCurve.GainDb(d, Full, Silence, Shape):F3}");
            }
        }
        t.True(File.Exists(csv), $"authored rolloff table written to {csv}");

        // ---- THE PROBE CONFIG, GENERATED FROM THE SHIPPED CURVE -------------------------------
        // This is the join between the two instruments, and it is deliberately not a hand-copied
        // table. scripts/voice-spatial-probe.sh drives a real Unity AudioSource and reads back the
        // real mixed output; feeding it keys transcribed by a human would mean the measurement
        // validated a curve that only resembled the shipped one. Generating the config HERE, from
        // VoiceCurve.SampleKeys, means the thing measured IS the thing that ships.
        string json = Path.Combine(dir, "shipped.json");
        WriteProbeConfig(json);
        t.True(File.Exists(json), $"probe config generated from the shipped curve at {json}");
    }

    /// <summary>
    /// Emit the headless probe's config with the SHIPPED rolloff in it, plus the three controls the
    /// measurement is only credible with: an unspatialised source (must read flat balance at every
    /// bearing and a flat level at every distance), two hard-panned ones (must read the extremes,
    /// which also proves L and R are not swapped), and silence (the noise floor).
    ///
    /// <para><c>rigScale 13.75</c> is not invented: it is a real logged session value, worked
    /// through in <c>Core/EnvSound.cs:1046</c> and again at <c>:417</c> (minDistance 1.2 perceived m
    /// = 16.5 world units). Measuring at a real scale rather than at 1 is the entire point — a
    /// rolloff that is right at scale 1 and wrong at 13.75 is the defect this convention exists to
    /// prevent.</para>
    ///
    /// <para>Hand-rolled JSON rather than a serializer: this project links no JSON library into the
    /// test assembly, and the schema is eight fields.</para>
    /// </summary>
    private static void WriteProbeConfig(string path)
    {
        var times = new float[VoiceCurve.KeyCount];
        var values = new float[VoiceCurve.KeyCount];
        VoiceCurve.SampleKeys(times, values, Full, Silence, Shape);

        var keys = new System.Text.StringBuilder();
        for (int i = 0; i < VoiceCurve.KeyCount; i++)
        {
            if (i > 0)
                keys.Append(", ");
            if (i % 6 == 0)
                keys.Append("\n        ");
            keys.Append($"[{times[i].ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}, " +
                        $"{values[i].ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}]");
        }

        string rolloff = $"\"rolloffMode\": \"Custom\", \"minDistance\": {Full}, \"maxDistance\": {Silence}";
        string body = $@"{{
  ""_comment"": [
    ""GENERATED by tests/GloomhavenVR.WireTests/VoiceVectors.cs from the SHIPPED VoiceCurve."",
    ""Do not hand-edit: rerun scripts/wire-tests.sh instead. The customRolloff keys below ARE the"",
    ""curve GloomhavenVR installs on a peer's AudioSource, so what the probe measures is what"",
    ""ships."",
    """",
    ""Distances are PERCEIVED metres and are multiplied by rigScale before they reach the"",
    ""AudioSource, the same convention as EnvSound.ApplyScale. rigScale 13.75 is a real logged"",
    ""session value (Core/EnvSound.cs:1046), not a round number."",
    """",
    ""Run:  ./scripts/voice-spatial-probe.sh .planning/voice/shipped.json""
  ],

  ""sampleRate"": 48000,
  ""dspBufferSize"": 1024,
  ""captureFramerate"": 0,
  ""rigScale"": 13.75,
  ""toneHz"": 440,
  ""toneAmplitude"": 0.5,
  ""settleSeconds"": 0.35,
  ""measureSeconds"": 0.25,
  ""customRolloffXUnits"": ""normalized"",

  ""bearings"": [0, 15, 30, 45, 60, 75, 90, 105, 120, 135, 150, 165,
               180, 195, 210, 225, 240, 255, 270, 285, 300, 315, 330, 345],
  ""bearingDistanceMeters"": 1.5,
  ""distancesMeters"": [0.25, 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10, 12, 14, 16, 20, 22, 25, 30],

  ""cases"": [
    {{
      ""name"": ""shipped"",
      ""spatialBlend"": 1.0, {rolloff},
      ""spread"": 35.0, ""dopplerLevel"": 0.0,
      ""customRolloff"": [{keys}
      ]
    }},
    {{
      ""name"": ""shipped_spread0"",
      ""spatialBlend"": 1.0, {rolloff},
      ""spread"": 0.0, ""dopplerLevel"": 0.0,
      ""customRolloff"": [{keys}
      ]
    }},
    {{
      ""name"": ""null_control"",
      ""spatialBlend"": 0.0, {rolloff},
      ""spread"": 35.0, ""dopplerLevel"": 0.0
    }},
    {{
      ""name"": ""hardpan_left"",
      ""spatialBlend"": 0.0, ""panStereo"": -1.0, {rolloff},
      ""spread"": 0.0, ""dopplerLevel"": 0.0
    }},
    {{
      ""name"": ""hardpan_right"",
      ""spatialBlend"": 0.0, ""panStereo"": 1.0, {rolloff},
      ""spread"": 0.0, ""dopplerLevel"": 0.0
    }},
    {{
      ""name"": ""silence"",
      ""play"": false,
      ""spatialBlend"": 0.0, {rolloff}
    }}
  ]
}}
";
        File.WriteAllText(path, body.Replace("\\n", "\n"));
    }

    /// <summary>
    /// Write an RGBA buffer as a binary PPM (P6) composited over a mid grey.
    ///
    /// <para>PPM because it needs no compression library and therefore no code that could itself be
    /// wrong; the shell step converts it to PNG. The composite is over MID GREY on purpose: the
    /// badge is drawn over whatever the room is, and a viewer looking at the frames must be able to
    /// judge the dark rim, which would be invisible composited over black and would look like the
    /// whole glyph composited over white.</para>
    ///
    /// <para>Row 0 of the raster is the BOTTOM row (Unity's convention), so the rows are emitted in
    /// reverse — PPM is top-down.</para>
    /// </summary>
    private static void WritePpm(string path, byte[] rgba, int size)
    {
        const byte Bg = 128;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        byte[] header = System.Text.Encoding.ASCII.GetBytes($"P6\n{size} {size}\n255\n");
        fs.Write(header, 0, header.Length);
        var row = new byte[size * 3];
        for (int y = size - 1; y >= 0; y--)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 4;
                float a = rgba[i + 3] / 255f;
                for (int c = 0; c < 3; c++)
                    row[x * 3 + c] = (byte)(Bg + (rgba[i + c] - Bg) * a + 0.5f);
            }
            fs.Write(row, 0, row.Length);
        }
    }
}
