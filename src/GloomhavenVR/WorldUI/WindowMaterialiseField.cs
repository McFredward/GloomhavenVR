using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE ONE FIELD, WRITTEN IN C#.</b> The erosion front that decides which parts of a floating
/// window are still there, evaluated on the CPU so the window's own uGUI elements wink out along
/// exactly the front the flake shader paints.
///
/// <para><b>WHY THE SAME ARITHMETIC EXISTS THREE TIMES.</b> A dissolve that is only a shader can
/// paint flakes over a window but cannot remove the window, and uGUI gives no per-pixel handle on
/// a canvas without putting a material on every Graphic — which is invasive, breaks on the
/// non-stock materials this project has already been bitten by (the <c>SimpleGrabPassBlur</c>
/// under the item confirmation box), and would have to be undone perfectly on every interruption.
/// So the removal is done at ELEMENT granularity from C# (<see cref="WindowMaterialiseRunner"/>
/// writes <c>CanvasRenderer.SetAlpha</c>) and the flakes are painted by
/// <c>Assets/Bundle/Table/WindowMaterialise.shader</c>. The two only agree — the element vanishing
/// exactly where the flakes peel off it — if they compute the same number, so the field lives
/// here, in the shader, and in <c>unity/asset-preview/windowmaterialise_field.py</c> (which is
/// what the reviewed frame strips are rendered from). <b>Change one, change all three.</b></para>
///
/// <para><b>THE STEREO ARGUMENT IS IN THE INPUTS.</b> Every function here takes a PANEL UV and
/// per-effect constants. Nothing takes a camera, a screen position, a depth sample or a clock.
/// The CPU side is per-window (not per-eye) by definition, and the GPU side interpolates the same
/// UV to the same surface point in both eyes, so the left and right eye erode identically. This
/// project's flicker history is spatial aliasing read as stereo rivalry; a field that is constant
/// per surface point cannot produce it.</para>
///
/// <para><b>NO CLOCK.</b> <c>progress</c> is the only time-like input and it is used as a POSITION
/// (where the front is) and as an AMPLITUDE (how far the plume drifted). No dial multiplies a
/// frequency here, because there is no frequency.</para>
///
/// <para><b>EXACT AT BOTH ENDS.</b> <see cref="Presence"/> returns exactly 1 for every UV at
/// progress 0 and exactly 0 for every UV at progress 1, because the front travels from
/// <c>-Softness</c> to <c>1 + Softness</c> while the threshold is confined to 0..1. That is not a
/// nicety: it is what lets the runner hand a window back at literal alpha 1 with no epsilon and no
/// "close enough" branch, which is the difference between an interruption restoring a window and
/// an interruption leaving it at 0.98 forever.</para>
/// </summary>
internal static class WindowMaterialiseField
{
    // ---- the shape constants. Deliberately NOT config dials -------------------------------
    // The user asked for a duration and for "cool", not for fifteen knobs; this project's own
    // ruling is "one switch and one factor, not five" (PanelSupersample.1.Core.cs). These are the
    // authored look. Anything the player is meant to touch is in WindowMaterialise.Config.

    /// <summary>Wind direction in ISOTROPIC q-space (see <see cref="QOf"/>), unit length. Right and
    /// slightly up: a window blows off toward the player's dominant side and out of the arc rather
    /// than down into the table.</summary>
    internal static readonly Vector2 Wind = new Vector2(0.92f, 0.39f).normalized;

    /// <summary>Half-width of the erosion front, in threshold units. Wider = more of the window is
    /// mid-crumble at once.</summary>
    internal const float Softness = 0.15f;

    /// <summary>How much the front is a ragged noise edge rather than a straight line. 0 is a
    /// guillotine, 1 is pure noise with no direction left at all.</summary>
    internal const float Ragged = 0.55f;

    /// <summary>Noise cells across the panel's SHORT side for the ragged front.</summary>
    internal const float FrontScale = 3.5f;

    /// <summary>How long (in threshold units) a point keeps shedding flakes after the front passes
    /// it. Mirrors <c>_AgeSpan</c>.</summary>
    internal const float AgeSpan = 0.30f;

    /// <summary>Plume travel at full progress, in q-space units (i.e. multiples of the panel's
    /// HEIGHT). Mirrors <c>_Drift</c>; the effect quad is padded by this much so the plume has
    /// somewhere to go.</summary>
    internal const float Drift = 0.8f;

    /// <summary>How long a BLOWN-AWAY flake stays alive, in threshold units. Much longer than
    /// <see cref="AgeSpan"/>, and the first preview strip is why: at 0.30 the debris was gone by
    /// 63 % of the duration and the last third of the animation was empty frames — a window that
    /// had finished vanishing but was still holding its release. Mirrors <c>_PlumeSpan</c>.</summary>
    internal const float PlumeSpan = 1.5f;

    /// <summary>How far the plume shears sideways as it travels. A plume that only translates reads
    /// as a sliding texture rather than as something blowing apart. Mirrors <c>_Spread</c>.</summary>
    internal const float Spread = 0.6f;

    /// <summary>How much a travelling flake stretches ALONG the wind. A field of dots at any density
    /// reads as static; the second preview strip's tail was exactly that, a rectangle of TV snow.
    /// Mirrors <c>_Streak</c>.</summary>
    internal const float Streak = 2.6f;

    /// <summary>How much sparser the plume gets as it blows away (it raises the flake threshold with
    /// age). Mirrors <c>_Thin</c>.</summary>
    internal const float Thin = 0.34f;

    // ---- the field ------------------------------------------------------------------------

    /// <summary>Value-noise hash. Byte-for-byte the shader's <c>hash21</c>.</summary>
    internal static float Hash21(Vector2 v)
    {
        float x = Frac(v.x * 123.34f);
        float y = Frac(v.y * 456.21f);
        float d = x * (x + 45.32f) + y * (y + 45.32f); // dot(p, p + 45.32)
        x += d;
        y += d;
        return Frac(x * y);
    }

    /// <summary>Bilinear value noise with a smoothstep fade. The shader's <c>vnoise</c>.</summary>
    internal static float VNoise(Vector2 v)
    {
        float ix = Mathf.Floor(v.x), iy = Mathf.Floor(v.y);
        float fx = v.x - ix, fy = v.y - iy;
        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);
        float a = Hash21(new Vector2(ix, iy));
        float b = Hash21(new Vector2(ix + 1f, iy));
        float c = Hash21(new Vector2(ix, iy + 1f));
        float d = Hash21(new Vector2(ix + 1f, iy + 1f));
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    /// <summary>0 at the rect's upwind edge, 1 at its downwind edge. The shader's <c>sweep</c>.
    /// The normalisation by <c>|w.x| + |w.y|</c> is what keeps the front reaching both far corners
    /// for any wind direction — without it a diagonal wind finishes early in one corner and the
    /// window's last elements pop.</summary>
    internal static float Sweep(Vector2 uv)
    {
        float n = Mathf.Max(Mathf.Abs(Wind.x) + Mathf.Abs(Wind.y), 1e-4f);
        return (Vector2.Dot(uv - new Vector2(0.5f, 0.5f), Wind) + 0.5f * n) / n;
    }

    /// <summary>Panel UV to isotropic space. A 3x-wide panel must not get 3x-stretched flakes.</summary>
    internal static Vector2 QOf(Vector2 uv, float aspect) => new Vector2(uv.x * aspect, uv.y);

    /// <summary>The threshold this point's presence is tested against. The shader's
    /// <c>threshold</c>. Confined to 0..1 because both terms are.</summary>
    internal static float Threshold(Vector2 uv, float aspect) =>
        Mathf.Lerp(Sweep(uv), VNoise(QOf(uv, aspect) * FrontScale), Ragged);

    /// <summary>Where the erosion front stands at <paramref name="progress"/>. Runs from
    /// <c>-Softness</c> to <c>1 + Softness</c>, which is what makes <see cref="Presence"/> exact at
    /// both ends.</summary>
    internal static float Front(float progress) => progress * (1f + 2f * Softness) - Softness;

    /// <summary>How many points of an element are sampled to decide how much of it is left. See
    /// <see cref="PresenceOf"/> for why one is not enough.</summary>
    internal const int Samples = 5;

    /// <summary>
    /// <b>1 = this point of the window is still fully there, 0 = it is gone.</b>
    ///
    /// <para><paramref name="threshold"/> is passed in rather than recomputed because it does not
    /// change over the life of the effect — only the front moves. The noise is therefore evaluated
    /// ONCE per element per sample when the effect starts, and never again.</para>
    /// </summary>
    internal static float Presence(float threshold, float progress)
    {
        float front = Front(progress);
        return SmoothStep01(front - Softness, front + Softness, threshold);
    }

    /// <summary>
    /// <b>How much of one ELEMENT is left</b>, as the mean presence over
    /// <see cref="Samples"/> points of its own rect (four corners and the centre).
    ///
    /// <para><b>WHY NOT JUST THE CENTRE — this is the difference between a wipe and a fade.</b> A
    /// window's single biggest CanvasRenderer is usually its background plate, which covers the
    /// whole rect. Judged by its centre alone it is intact until the front reaches the middle and
    /// then fades out over about a third of the duration, all at once, while everything around it
    /// has been winking out in a wave — so the wave reads as detail ON TOP OF a plain cross-fade of
    /// the window, which is exactly the "aufploppen" being replaced, just slower. Averaging over the
    /// element's extent makes a big element fade across the WHOLE sweep (its far corner is still
    /// there while its near corner has gone) and leaves a small element snapping crisply, which is
    /// what makes the window look like it is being eaten rather than dimmed.</para>
    ///
    /// <para>Endpoint exactness survives the averaging because every term is exact: at progress 0
    /// every sample returns 1, at progress 1 every sample returns 0.</para>
    /// </summary>
    internal static float PresenceOf(System.Collections.Generic.IList<float> thresholds, int offset,
                                     float progress)
    {
        float front = Front(progress);
        float lo = front - Softness;
        float inv = 1f / Mathf.Max(2f * Softness, 1e-6f);
        float sum = 0f;
        for (int k = 0; k < Samples; k++)
        {
            float t = Mathf.Clamp01((thresholds[offset + k] - lo) * inv);
            sum += t * t * (3f - 2f * t);
        }
        return sum * (1f / Samples);
    }

    /// <summary>
    /// HLSL/GLSL <c>smoothstep(edge0, edge1, x)</c> — and it is NOT
    /// <see cref="Mathf.SmoothStep"/>, which is an eased LERP FROM..TO with the interpolant last.
    /// Reaching for the Unity one here would have silently mirrored a different function than the
    /// shader's, which is the exact failure mode this file exists to prevent.
    /// </summary>
    internal static float SmoothStep01(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(edge1 - edge0, 1e-6f));
        return t * t * (3f - 2f * t);
    }

    private static float Frac(float x) => x - Mathf.Floor(x);
}
