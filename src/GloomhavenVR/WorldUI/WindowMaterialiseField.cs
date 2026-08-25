using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE ONE FIELD, WRITTEN IN C#.</b> The erosion front that decides which parts of a floating
/// window are still there, evaluated on the CPU so the window's own uGUI elements wink out along
/// exactly the front the debris is torn off along.
///
/// <para><b>WHY THE SAME ARITHMETIC EXISTS THREE TIMES.</b> A dissolve that is only a shader can
/// paint flakes over a window but cannot remove the window, and uGUI gives no per-pixel handle on
/// a canvas without putting a material on every Graphic — which is invasive, breaks on the
/// non-stock materials this project has already been bitten by (the <c>SimpleGrabPassBlur</c>
/// under the item confirmation box), and would have to be undone perfectly on every interruption.
/// So the removal is done at ELEMENT granularity from C# (<see cref="WindowMaterialiseRunner"/>
/// writes <c>CanvasRenderer.SetAlpha</c>) and the DEBRIS is real world-space geometry —
/// <see cref="WindowMaterialiseDebris"/> builds a shard mesh whose every vertex carries the
/// threshold of the point of the window it was torn from, and
/// <c>Assets/Bundle/Table/WindowMaterialise.shader</c> flies it in its vertex stage. The two only
/// agree — a shard leaving exactly where its patch of window went dark — if they compute the same
/// number, so the field lives here, in the shader, and in
/// <c>unity/asset-preview/windowmaterialise_field.py</c> (which is what the reviewed frame strips
/// and the Blender room renders are driven from). <b>Change one, change all three.</b></para>
///
/// <para><b>THE STEREO ARGUMENT IS IN THE INPUTS.</b> Every function here takes a PANEL UV and
/// per-effect constants. Nothing takes a camera, a HEAD POSE, a screen position, a depth sample or
/// a clock. User, 2026-08-26: <i>"Der Effekt soll nicht an den Kopfbewegungen gebunden sein"</i> —
/// so this is a requirement now, not only a stereo argument. It is why the debris is real mesh
/// geometry with its own tumbling orientation rather than camera-facing billboards: a billboard is
/// oriented BY the head, which is the thing he refused, and under MultiPass it is oriented
/// separately for each eye, which is this project's known route to stereo rivalry.
/// The CPU side is per-window (not per-eye) by definition, and the GPU side interpolates the same
/// UV to the same surface point in both eyes, so the left and right eye erode identically. This
/// project's flicker history is spatial aliasing read as stereo rivalry; a field that is constant
/// per surface point cannot produce it.</para>
///
/// <para><b>NO CLOCK.</b> <c>progress</c> is the only time-like input and it is used as a POSITION
/// (where the front is) and as an AMPLITUDE (how far the debris has travelled). No dial multiplies a
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

    // ---- the TWO FRONTS ---------------------------------------------------------------------
    // The 2026-08-26 redesign replaced a painted plume with real world-space shards, and that forced
    // the one front into two. The reason is arithmetic, not taste:
    //
    //   An ELEMENT is present while the front has not reached its threshold; a SHARD exists once the
    //   front HAS passed its threshold and has not yet run far enough past it for the shard to die.
    //   So "the window is complete" means front <= -Softness, and at front <= -Softness EVERY shard
    //   has age <= 0, i.e. there is no debris at all. Under one shared front, "the window is solid"
    //   and "debris is still flying" are mutually exclusive — and the brief asks for both: "make it
    //   read as complete earlier — the window should be legible and solid well before the particles
    //   finish."
    //
    // So the ELEMENT front runs over the first ElementSpan of the duration and the DEBRIS front over
    // the whole of it. On a VANISH the two are the SAME function until the element one saturates
    // (ElementSpan is exactly 1/(1+DebrisOverrun)), so a shard is still born in the frame its own
    // patch of window goes dark — the causal coupling that makes the effect read as tearing rather
    // than as two animations played together. On an APPEAR the element front finishes first and the
    // last shards settle onto an already-solid, already-legible window.

    /// <summary>Fraction of the duration over which the ELEMENT front sweeps the whole window. The
    /// remainder is debris-only. Chosen as exactly <c>1 / (1 + <see cref="DebrisOverrun"/>)</c> so
    /// that on a vanish the two fronts are the identical function until the element one
    /// saturates.</summary>
    internal const float ElementSpan = 0.58f;

    /// <summary>How far past the downwind edge the DEBRIS front travels on a vanish, as a fraction
    /// of one full sweep. <c>0.7241</c> is <c>1/ElementSpan - 1</c>, written out rather than derived
    /// so the shader and the numpy mirror can carry the same literal.</summary>
    internal const float DebrisOverrun = 0.7241f;

    /// <summary>Where in a VANISH the debris starts being faded out so the effect ends at exactly
    /// nothing. Only a vanish uses it; an appear STARTS at the top of the ramp and the same fade
    /// would make its first frames empty.</summary>
    internal const float TailStart = 0.78f;

    // ---- the DEBRIS. Sizes are in APPARENT METRES, never in canvas or world units ------------
    // The 9.57x rig-scale trap in one sentence: a number authored in the host canvas's local units
    // is multiplied by lossyScale to reach WORLD units and then divided by the rig's world scale to
    // reach the metres a player actually perceives. Everything below is the last of those three, and
    // WindowMaterialiseDebris converts once, from the panel's own measured transform, and LOGS every
    // step of the conversion. There is no ParticleSystem in this effect, so
    // ParticleSystemScalingMode — the setting 37 of 43 of the game's own systems get wrong at this
    // rig scale — is not a parameter of it. See WindowMaterialiseDebris for why not, at length.

    /// <summary>Shards per square metre of APPARENT window area. A 0.80 x 0.52 m modal is 0.42 m2,
    /// so about 290 shards: a few hundred pieces of a window, not a dust cloud.</summary>
    internal const float DebrisPerSquareMetre = 700f;

    /// <summary>Hard cap on shards for one window, whatever its area. The map room can hold several
    /// windows at once and each shard is 12 vertices.</summary>
    internal const int DebrisMaxCount = 420;

    /// <summary>Floor, so a tiny fitted window (the ESC menu fits to a few centimetres) still breaks
    /// into something rather than into four chips.</summary>
    internal const int DebrisMinCount = 90;

    /// <summary>Smallest shard, in apparent metres. NOT smaller, and the reason is stereo: this
    /// project's flicker history is spatial aliasing read as rivalry, and sub-pixel geometry is
    /// exactly that regime. 4 mm at 0.9 m is about 0.25 deg, roughly ten headset pixels across.
    /// </summary>
    internal const float DebrisMinMetres = 0.004f;

    /// <summary>Largest shard, in apparent metres. 22 mm is a readable chip of window at arm's
    /// length without becoming a flying plate.</summary>
    internal const float DebrisMaxMetres = 0.022f;

    /// <summary>Skew of the size distribution: <c>size = lerp(min, max, r^SizePower)</c>. Above 1
    /// means most shards are small and a few are large, which is what a broken thing looks
    /// like.</summary>
    internal const float DebrisSizePower = 2.2f;

    /// <summary>
    /// How far a shard travels DOWNWIND (in the window's plane, along <see cref="Wind"/>) by the end
    /// of its life, in apparent metres.
    ///
    /// <para><b>Raised from 0.46 with <see cref="DebrisLifeSpan"/>, and it had to be.</b> Travel goes
    /// as <c>drift · age^1.35</c>, so lengthening the lifetime lowers every shard's age at a given
    /// moment and roughly HALVES how far it has got — the strip after that change showed the debris
    /// dispersing in place rather than blowing anywhere, which loses the user's own word for the
    /// effect (<i>"in Partikel von Wind verweht"</i>). The two constants are coupled through the
    /// exponent and should be tuned together; this is the pair that keeps the cloud on screen AND
    /// visibly going somewhere.</para>
    /// </summary>
    internal const float DebrisDriftMetres = 0.72f;

    /// <summary>How far a shard travels OUT OF THE WINDOW'S PLANE by the end of its life, in
    /// apparent metres. <b>This one number is the redesign.</b> The previous effect had no such term
    /// at all — every flake it drew lived in the window's own plane, which is exactly why the user
    /// called it <i>"eher ein 2D-Effekt"</i>. Signed per shard; see
    /// <see cref="DebrisBehindFraction"/>.</summary>
    internal const float DebrisLiftMetres = 0.30f;

    /// <summary>Fraction of shards launched AWAY from the player rather than toward them. Not zero,
    /// and not for symmetry: a shard that goes behind the window is hidden by it and reappears past
    /// its edge, which is the most legible proof the eye can be given that the debris is in the room
    /// and not painted on the pane.</summary>
    internal const float DebrisBehindFraction = 0.38f;

    /// <summary>How far a shard falls by the end of its life, in apparent metres, as
    /// <c>fall * age^2</c>. Small: this is wind-borne debris, not gravel.</summary>
    internal const float DebrisFallMetres = 0.10f;

    /// <summary>Amplitude of the per-shard wander, in apparent metres: three sines of the shard's own
    /// seeds, scaled by age. It is what stops the cloud reading as a rigid field being translated —
    /// the defect the previous round's second preview strip caught, now solved with geometry instead
    /// of with a streak.</summary>
    internal const float DebrisWanderMetres = 0.075f;

    /// <summary>Full turns a shard makes over its life, at the top of the random range. Tumbling is
    /// what makes a flat-shaded solid read as a solid — and it is head-independent by construction,
    /// which a camera-facing billboard is not.</summary>
    internal const float DebrisSpinTurns = 1.7f;

    /// <summary>
    /// Shard lifetime in THRESHOLD units, the same units the front travels in. Longer than one sweep
    /// on purpose: the debris has to outlive the window, or the end of a vanish is empty frames.
    ///
    /// <para><b>1.15 was wrong and the first preview strip of this round caught it — the same defect,
    /// in the same place, as the previous round's first strip.</b> At 1.15 the debris front reaches
    /// 2.09 by the end of a vanish, so every shard except the very last-eroded ones is past age 1 and
    /// dead: three of the twelve strip columns (k = 0.81, 0.91, 1.00, i.e. the last 0.20 s) were
    /// completely empty, and the appear's last 0.10 s was a static window. At <b>1.85</b> the deaths
    /// are spread across the whole ramp and the final removal is done by the TAIL FADE, which is what
    /// should be doing it: the tail fade is exact at k = 1 by construction, whereas ageing only
    /// happens to be. A shorter lifetime also made shards travel FURTHER per unit of k (travel goes
    /// as age^1.35), which is why they were leaving the frame as well as dying in it.</para>
    ///
    /// <para>Mirrors <c>_LifeSpan</c>.</para>
    /// </summary>
    internal const float DebrisLifeSpan = 1.85f;

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

    /// <summary>
    /// <b>Normalised time to the two fronts.</b> <paramref name="k"/> is elapsed/duration in 0..1;
    /// <paramref name="materialising"/> plays the whole thing backwards, which is exactly what the
    /// user described (<i>"Auftauchen eventuell andersrum"</i>).
    ///
    /// <para>Both outputs are EXACT at both ends, and that is load-bearing rather than tidy: it is
    /// what lets the runner hand a window back at literal alpha 1, and leave literal shard size 0,
    /// with no epsilon and no "close enough" branch. At <c>k = 0</c> an appear has element progress
    /// exactly 1 (window absent) and at <c>k = 1</c> exactly 0 (window whole); a vanish is the
    /// mirror. The debris end is made exact by <see cref="DebrisSizeScale"/> and
    /// <see cref="SizeEnvelope"/>, not by the front.</para>
    /// </summary>
    internal static void Progresses(float k, bool materialising,
                                    out float elementProgress, out float debrisFront)
    {
        float e = Mathf.Clamp01(k);
        float pe = Mathf.Clamp01(e / ElementSpan);
        if (materialising)
        {
            elementProgress = 1f - pe;
            // An APPEAR does not overrun. Its debris front runs one plain sweep BACKWARDS, so at
            // k = 1 it sits at exactly -Softness, every shard is exactly age 0, and SizeEnvelope(0)
            // is exactly 0 — no debris is left sitting on the finished window.
            debrisFront = Front(1f - e);
        }
        else
        {
            elementProgress = pe;
            // A VANISH overruns, so the element front and the debris front are the SAME function
            // until the element one saturates: a shard is born in the frame its own patch of window
            // goes dark, and then keeps flying for the rest of the duration.
            debrisFront = Front(e * (1f + DebrisOverrun));
        }
    }

    /// <summary>
    /// The master size multiplier for the debris this frame: the user's intensity dial times the
    /// vanish-only tail fade.
    ///
    /// <para><b>The tail fade is the one place the two directions are not mirror images, and they
    /// must not be.</b> A vanish has to END at nothing, so its shards are shrunk to zero over the
    /// last <c>1 - TailStart</c> of the ramp — and because <c>smoothstep</c> returns exactly 1 at
    /// its upper edge, the size at <c>k = 1</c> is exactly 0. An appear STARTS at the top of the
    /// ramp, where the same fade would leave its first frames completely empty: a window that is
    /// live and clickable while showing the player nothing at all.</para>
    /// </summary>
    internal static float DebrisSizeScale(float k, bool materialising, float intensity) =>
        materialising
            ? intensity
            : intensity * (1f - SmoothStep01(TailStart, 1f, Mathf.Clamp01(k)));

    /// <summary>How far through its life a shard born at <paramref name="threshold"/> is, given where
    /// the debris front stands. 0 = just torn loose, 1 = gone. Mirrors the shader's <c>age</c>.
    /// </summary>
    internal static float Age(float threshold, float debrisFront) =>
        Mathf.Clamp01((debrisFront - threshold) / DebrisLifeSpan);

    /// <summary>The size envelope over a shard's life: it grows out of the window as the front eats
    /// its patch and shrinks away as it disperses. EXACTLY 0 at age 0 and at age 1.</summary>
    internal static float SizeEnvelope(float age) =>
        SmoothStep01(0f, 0.06f, age) * (1f - SmoothStep01(0.72f, 1f, age));

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
