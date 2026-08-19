using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// WHICH SHADER PROPERTY IS THE MIRROR, AND WHICH WAY DOES IT POINT — pinned here for the reason
/// the rest of this list exists: the verdict is observed ONLY by eye, from inside a headset, one
/// photograph per hardware round, and a wrong answer does not throw, does not log and does not
/// look like a failure. It looks like a pool that still mirrors, which is a report this project
/// has now received three times.
///
/// <para>WHAT MAKES THIS THE SHARPEST CASE ON THE LIST. Through ModBuild 159 the water retune
/// wrote ONE hard-coded property name, <c>_Smoothness</c>, onto ONE shader. ModBuild 160 walks
/// each shader's whole property table at runtime and writes onto every name it CLASSIFIES as a
/// reflection — including shaders whose property names nobody in this project has ever read,
/// because the game's shaders ship compiled inside <c>always_loaded_base*</c> asset bundles and
/// there is no game install on the build machine to open them with. An unattended write onto an
/// unknown shader is only defensible while one property holds:
/// <b>no cap may ever make a surface shinier than the tileset authored it.</b> That property is
/// what the sweep at the bottom of this file asserts, exhaustively, over every family, every
/// authored value and every declared range — because its violation would arrive as "jetzt
/// spiegelt es noch schlimmer" and nothing in this repository could otherwise have noticed.</para>
///
/// <para>THE TWO TRAPS IT EXISTS TO CATCH, both real names off real shaders:</para>
/// <list type="number">
///   <item><c>_Roughness</c> is <c>_Smoothness</c> READ BACKWARDS. Capping it the way gloss is
///   capped drives it towards zero — a perfect mirror — which is precisely the defect being
///   fixed. The basin under the report's pool runs <c>Amp_Basic_N_MRAO</c>, which is literally
///   named for Metallic/Roughness/AO, so this is the shader family the inversion was written
///   for.</item>
///   <item><c>_SmoothnessTextureChannel</c> is Unity Standard's ENUM choosing which texture
///   channel smoothness is read from. Writing 0.08 into it would not dim anything; it would
///   change where the shader reads its data. Every selector-shaped name must be refused, and the
///   <see cref="WaterReflectionCaps.SelectorTokens"/> list is what does it.</item>
/// </list>
///
/// <para>And one trap from this project's own history: the FREQUENCY-SCRUB class (ModBuild 149,
/// an element strength that was quietly scaling a frequency). A cap of 0.08 only means "almost
/// matte" on a normalised 0..1 property. On a <c>Range(0, 8)</c> it means something else
/// entirely, and the water film's own material carries <c>_WaveFrequency=3</c> and
/// <c>_OpaqueDetailMult=2</c> — plain Floats well outside 0..1 — so the guard is not
/// hypothetical.</para>
/// </summary>
internal static class WaterReflectionVectors
{
    internal static void Run(Harness t)
    {
        Classification(t);
        Direction(t);
        RangeGating(t);
        NeverShinierSweep(t);
    }

    // ---------------------------------------------------------------------------------------
    //  1. Classification — the names off the real materials, plus the two traps.
    // ---------------------------------------------------------------------------------------
    private static void Classification(Harness t)
    {
        // The one name ModBuild 159 knew about, read off the live water material on hardware.
        Is(t, "_Smoothness", WaterCapFamily.Gloss);
        Is(t, "_Glossiness", WaterCapFamily.Gloss);
        Is(t, "_GlossMapScale", WaterCapFamily.None);        // "map" — a scale ON a map, not a strength

        // The inverted axis. Amp_Basic_N_MRAO is named for it.
        Is(t, "_Roughness", WaterCapFamily.Rough);
        Is(t, "_RoughnessPower", WaterCapFamily.Rough);

        Is(t, "_Metallic", WaterCapFamily.Metal);
        Is(t, "_MetallicStrength", WaterCapFamily.Metal);
        Is(t, "_MetallicGlossMap", WaterCapFamily.None);     // a texture, and "map"

        Is(t, "_ReflectionIntensity", WaterCapFamily.Reflection);
        // "cubemap"/"envmap" contain "map", so these only classify because Classify elides the
        // compound word before the selector veto — a strength, not a map handle.
        Is(t, "_CubemapPower", WaterCapFamily.Reflection);
        Is(t, "_EnvMapStrength", WaterCapFamily.Reflection);
        Is(t, "_ReflectionMap", WaterCapFamily.None);        // a genuine handle: "map" still wins
        Is(t, "_ReflectionCubeMap", WaterCapFamily.Reflection); // …and the compound still does not

        // THE SELECTOR TRAP. Unity Standard ships this name; writing a strength into it would
        // change which texture channel the shader reads, not how bright the reflection is.
        Is(t, "_SmoothnessTextureChannel", WaterCapFamily.None);
        Is(t, "_MetallicMode", WaterCapFamily.None);
        Is(t, "_ReflectionToggle", WaterCapFamily.None);
        Is(t, "_GlossUVSet", WaterCapFamily.None);

        // TWO DIRECTIONS IN ONE NAME — refused rather than guessed at, because there is no
        // reading of it that cannot make the mirror worse.
        Is(t, "_RoughnessSmoothness", WaterCapFamily.None);
        Is(t, "_MetalRoughness", WaterCapFamily.None);

        // Every other property on the water film's own material, verbatim from the hardware
        // census (LogOutput.log:1018). Not one of them may be classified as a reflection: they
        // are the animation, the waves and the tint the user asked to KEEP.
        foreach (string untouched in new[]
                 {
                     "_Normal_Map", "_WaterUVAnimSpeedB", "_WaterUVAnimSpeedA", "_Color_Tint",
                     "_WaterBorderCol", "_NormalTilings", "_Edge_Colour_Distance",
                     "_Edge_Distance", "_Edge_Colour", "_EdgeColour_Toggle", "_InvertDepthFade",
                     "_WaterBorderWidth", "_OpaqueDetailStepMin", "_OpaqueDetailStepMax",
                     "_OpaqueDetailMult", "_WaveSphereCenter", "_VertexOffsetWaveMask",
                     "_VertexOffsetWaves", "_addSphericalWaves", "_AddOpaqueDetail",
                     "_WaveFrequency", "_WaveSpeed", "_DeactivateWallfade", "_WaterNoiseSpeed",
                     "_DetailOpacityBaseNormalStr", "__dirty",
                 })
        {
            Is(t, untouched, WaterCapFamily.None);
        }

        // Degenerate input must not throw on a game thread.
        Is(t, null, WaterCapFamily.None);
        Is(t, "", WaterCapFamily.None);
    }

    // ---------------------------------------------------------------------------------------
    //  2. Direction — the numbers the hardware census actually recorded.
    // ---------------------------------------------------------------------------------------
    private static void Direction(Harness t)
    {
        const float smooth = 0.08f;   // WaterSettings.Smoothness default
        const float reflect = 0f;     // WaterSettings.Reflectivity default

        // The water film, as authored: _Smoothness(Range 0..1) = 0.754.
        Cap(t, "film gloss", WaterCapFamily.Gloss, 0.754f, true, 0f, 1f, smooth, reflect, 0.08f);

        // Already below the cap — untouched, never raised to meet it. This is the whole "never
        // raise what the tileset authored" rule in one vector.
        Cap(t, "gloss under cap", WaterCapFamily.Gloss, 0.02f, true, 0f, 1f, smooth, reflect, 0.02f);

        // The inversion. A roughness of 0.10 is a near-mirror and must be FLOORED at 1 - 0.08.
        Cap(t, "roughness floored", WaterCapFamily.Rough, 0.10f, true, 0f, 1f, smooth, reflect, 0.92f);

        // Already rougher than the floor — left alone, never dragged down to it.
        Cap(t, "roughness over floor", WaterCapFamily.Rough, 0.97f, true, 0f, 1f, smooth, reflect, 0.97f);

        // Metal and explicit reflection strength ride WaterSettings.Reflectivity, not WaterSettings.Smoothness.
        Cap(t, "metal", WaterCapFamily.Metal, 1f, true, 0f, 1f, smooth, reflect, 0f);
        Cap(t, "reflection", WaterCapFamily.Reflection, 0.6f, true, 0f, 1f, smooth, reflect, 0f);

        // …and they follow that dial when the user moves it in the headset.
        Cap(t, "metal at 0.3", WaterCapFamily.Metal, 1f, true, 0f, 1f, smooth, 0.3f, 0.3f);

        // A sharpness dial of 1.0 is "do nothing" in both spellings — the identity a user gets
        // when they wind WaterSettings.Smoothness all the way up to compare.
        Cap(t, "gloss dial open", WaterCapFamily.Gloss, 0.754f, true, 0f, 1f, 1f, reflect, 0.754f);
        Cap(t, "rough dial open", WaterCapFamily.Rough, 0.10f, true, 0f, 1f, 1f, reflect, 0.10f);

        // None is never written, whatever is passed with it.
        Refused(t, "family None", WaterCapFamily.None, 0.9f, true, 0f, 1f, smooth, reflect);
    }

    // ---------------------------------------------------------------------------------------
    //  3. Range gating — the FREQUENCY-SCRUB guard (ModBuild 149).
    // ---------------------------------------------------------------------------------------
    private static void RangeGating(Harness t)
    {
        const float smooth = 0.08f;
        const float reflect = 0f;

        // A Range that is not 0..1 is not a normalised strength and must be refused outright.
        Refused(t, "Range(0,8)", WaterCapFamily.Gloss, 3f, true, 0f, 8f, smooth, reflect);
        Refused(t, "Range(-1,1)", WaterCapFamily.Metal, 0.5f, true, -1f, 1f, smooth, reflect);
        Refused(t, "empty range", WaterCapFamily.Gloss, 0.5f, true, 1f, 1f, smooth, reflect);

        // A serializer round-trip must not cost a legitimate 0..1 property its cap.
        Cap(t, "Range(0,1.0000001)", WaterCapFamily.Gloss, 0.754f, true, 0f, 1.0000001f,
            smooth, reflect, 0.08f);

        // A plain Float declares no limits, so the authored value is the only evidence there is.
        // 0.754 is consistent with a strength; the film's own _WaveFrequency=3 is not.
        Cap(t, "Float in range", WaterCapFamily.Gloss, 0.754f, false, 0f, 0f, smooth, reflect, 0.08f);
        Refused(t, "Float = 3 (a frequency)", WaterCapFamily.Gloss, 3f, false, 0f, 0f, smooth, reflect);
        Refused(t, "Float = -1", WaterCapFamily.Metal, -1f, false, 0f, 0f, smooth, reflect);

        // Non-finite authored values would be written straight onto a material.
        Refused(t, "NaN", WaterCapFamily.Gloss, float.NaN, true, 0f, 1f, smooth, reflect);
        Refused(t, "Infinity", WaterCapFamily.Rough, float.PositiveInfinity, true, 0f, 1f,
            smooth, reflect);

        // A narrow declared range still clamps, and the clamp must not break the invariant: a
        // roughness declared Range(0, 0.5) cannot reach the 0.92 floor, but 0.5 is still rougher
        // than the 0.1 it was authored at, so the write is legitimate and is made.
        Cap(t, "rough in Range(0,0.5)", WaterCapFamily.Rough, 0.1f, true, 0f, 0.5f, smooth,
            reflect, 0.5f);
    }

    // ---------------------------------------------------------------------------------------
    //  4. THE INVARIANT. The reason this file is on the list at all.
    // ---------------------------------------------------------------------------------------
    private static void NeverShinierSweep(Harness t)
    {
        var families = new[]
        {
            WaterCapFamily.Gloss, WaterCapFamily.Rough,
            WaterCapFamily.Metal, WaterCapFamily.Reflection,
        };
        int checks = 0;
        foreach (WaterCapFamily f in families)
        {
            for (int a = 0; a <= 20; a++)
            {
                float authored = a / 20f;
                for (int s = 0; s <= 10; s++)
                {
                    float smoothnessCap = s / 10f;
                    for (int m = 0; m <= 10; m++)
                    {
                        float reflectivityCap = m / 10f;
                        foreach ((bool hasRange, float lo, float hi) in new[]
                                 {
                                     (true, 0f, 1f), (true, 0f, 0.5f), (true, 0.25f, 1f),
                                     (false, 0f, 0f),
                                 })
                        {
                            if (!WaterReflectionCaps.TryCap(
                                    f, authored, hasRange, lo, hi, smoothnessCap, reflectivityCap,
                                    out float v, out _))
                            {
                                continue;
                            }
                            checks++;
                            // "Shinier" is family-dependent: for every downward family a HIGHER
                            // value is shinier; for roughness a LOWER one is. Neither may happen.
                            bool shinier = f == WaterCapFamily.Rough
                                ? v < authored - 1e-6f
                                : v > authored + 1e-6f;
                            t.True(!shinier,
                                $"water cap never shinier: {f} authored {authored:0.###} "
                                + $"range[{lo:0.##},{hi:0.##}] hasRange={hasRange} "
                                + $"smooth={smoothnessCap:0.#} reflect={reflectivityCap:0.#} "
                                + $"-> {v:0.###}");
                            // A written value must also be finite and inside the declared range,
                            // or the material takes a value the shader was never built for.
                            t.True(!float.IsNaN(v) && !float.IsInfinity(v),
                                $"water cap finite: {f} {authored:0.###} -> {v:0.###}");
                            float clampLo = hasRange ? Mathf.Max(lo, 0f) : 0f;
                            float clampHi = hasRange ? Mathf.Min(hi, 1f) : 1f;
                            t.True(v >= clampLo - 1e-6f && v <= clampHi + 1e-6f,
                                $"water cap in range: {f} {authored:0.###} -> {v:0.###} "
                                + $"not in [{clampLo:0.##},{clampHi:0.##}]");
                        }
                    }
                }
            }
        }
        t.True(checks > 1000,
            $"water cap sweep actually ran (only {checks} caps were applied — a classification "
            + "or gating change that refused everything would make this file assert nothing)");
    }

    // ---------------------------------------------------------------------------------------

    private static void Is(Harness t, string? name, WaterCapFamily expected)
    {
        WaterCapFamily got = WaterReflectionCaps.Classify(name);
        t.True(got == expected,
            $"water property family '{name ?? "<null>"}': expected {expected}, got {got}");
    }

    private static void Cap(
        Harness t, string what, WaterCapFamily family, float authored, bool hasRange,
        float lo, float hi, float smoothnessCap, float reflectivityCap, float expected)
    {
        bool ok = WaterReflectionCaps.TryCap(
            family, authored, hasRange, lo, hi, smoothnessCap, reflectivityCap,
            out float v, out string reason);
        t.True(ok, $"water cap '{what}' should have been written but was refused: {reason}");
        t.True(ok && Math.Abs(v - expected) < 1e-5f,
            $"water cap '{what}': expected {expected:0.#####}, got {v:0.#####} ({reason})");
    }

    private static void Refused(
        Harness t, string what, WaterCapFamily family, float authored, bool hasRange,
        float lo, float hi, float smoothnessCap, float reflectivityCap)
    {
        bool ok = WaterReflectionCaps.TryCap(
            family, authored, hasRange, lo, hi, smoothnessCap, reflectivityCap,
            out float v, out string reason);
        t.True(!ok,
            $"water cap '{what}' should have been REFUSED but wrote {v:0.#####} ({reason})");
        t.True(!string.IsNullOrEmpty(reason),
            $"water cap '{what}': a refusal must always carry a reason for the hardware log");
    }
}
