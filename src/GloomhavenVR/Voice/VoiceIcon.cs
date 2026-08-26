namespace GloomhavenVR.Voice;

// =================================================================================================
//  VOICE ICON — the loudspeaker glyph, rasterised from arithmetic. No bundle asset, no shader, no
//  Unity type at all: this file turns a level step into a block of RGBA bytes, and nothing else.
// =================================================================================================

/// <summary>
/// Draws the speaking indicator: a loudspeaker with <see cref="VoiceCurve.Steps"/>-1 arcs that
/// light up as the talker gets louder — the user's own description, verbatim: <i>"zB mit einem
/// Lautsprechersymbol in einer Ecke das ausschlägt bei Ton"</i>.
///
/// <para><b>WHY IT IS DRAWN IN CODE RATHER THAN SHIPPED AS A SPRITE.</b> Three reasons, in order of
/// weight. (1) The asset bundle is 70,204,340 bytes and rebuilding it is the integrator's job on a
/// separate cadence; a feature that needs a bundle rebuild cannot ship in the same round it was
/// written. (2) <c>Shader.Find</c> only sees LOADED shaders and has already cost this project two
/// builds (<c>Core/BundleShaders.cs:8-41</c>) — a runtime-generated texture on the game's own
/// <c>Sprites/Default</c> avoids that whole class of failure. (3) It makes the glyph TESTABLE: the
/// rasteriser is free of Unity, so <c>tests/GloomhavenVR.WireTests</c> links this exact file,
/// asserts its invariants and writes the four frames out as images. The pictures in
/// <c>.planning/voice/</c> are therefore produced BY THE SHIPPED CODE and not by a lookalike — which
/// matters, because this project has previously drawn a glow on the wrong object and passed every
/// gate while doing it.</para>
///
/// <para><b>THE GLYPH.</b> A cabinet and a horn on the left, three concentric arcs on the right.
/// Unlit arcs are drawn faintly rather than omitted, so the icon keeps a constant silhouette and the
/// eye reads a LEVEL rather than a shape change. Every arc dark = the peer is not talking; three
/// arcs bright = they are talking loudly.</para>
///
/// <para><b>THE DARK RIM IS NOT DECORATION.</b> The indicator floats over whatever the room happens
/// to be — a lit cellar wall, a black sky, a peer's own mask — so a plain white glyph would vanish
/// against half of them. Every shape is rasterised twice, once at its true size and once inflated by
/// <see cref="RimWidth"/>, and the difference is filled with <see cref="RimLevel"/>. That gives the
/// glyph its own contrast wherever it lands, at zero runtime cost, and with no second draw call.</para>
///
/// <para><b>COVERAGE IS SUPERSAMPLED, not thresholded.</b> <see cref="Supersamples"/> squared taps
/// per texel. A hard edge on a 64 px glyph seen at arm's length in a headset crawls badly under
/// stereo — and per this project's own finding, spatial aliasing IS the stereo flicker
/// (<c>.planning</c>, "Aliasing is per-eye"). This is the cheap half of that fix; the other half is
/// the mip chain, which the caller asks for when it creates the texture.</para>
/// </summary>
internal static class VoiceIcon
{
    /// <summary>Texels per side of a generated frame. Square, power of two, mip-friendly.</summary>
    internal const int Size = 64;

    /// <summary>Bytes per generated frame — RGBA32, four bytes a texel.</summary>
    internal const int ByteCount = Size * Size * 4;

    /// <summary>Square root of the number of coverage taps per texel.</summary>
    private const int Supersamples = 4;

    /// <summary>How far the dark rim extends beyond every shape, in normalised units.</summary>
    private const float RimWidth = 0.028f;

    /// <summary>Rim brightness. Not zero: a pure black rim reads as a hole on a dark wall.</summary>
    private const byte RimLevel = 26;

    /// <summary>Alpha of an arc that is present but not lit.</summary>
    private const float UnlitAlpha = 0.20f;

    // ---- glyph geometry, in normalised 0..1 texture space (origin bottom-left) -------------------

    private const float Mid = 0.5f;          // the horizontal axis the whole glyph is mirrored about
    private const float BodyLeft = 0.115f;
    private const float BodyRight = 0.255f;
    private const float BodyHalfH = 0.105f;
    private const float HornRight = 0.455f;
    private const float HornHalfH = 0.265f;
    private const float ArcHalfAngleCos = 0.64f; // cos(50 degrees), the wedge the arcs occupy
    private const float ArcWidth = 0.052f;

    /// <summary>Mid-radius of arc <paramref name="index"/> (1..3), measured from the horn's mouth.</summary>
    private static float ArcRadius(int index) => 0.115f + 0.098f * index;

    /// <summary>
    /// Rasterise the frame for <paramref name="step"/> (0 = silent, 3 = loudest) into
    /// <paramref name="rgba"/>, which must hold at least <see cref="ByteCount"/> bytes.
    ///
    /// <para>Row 0 is the BOTTOM row, which is Unity's texture convention and therefore what
    /// <c>Texture2D.LoadRawTextureData</c> expects. The test harness that writes these frames to
    /// disk flips them itself; if the images ever come out upside down, that flip is the bug and not
    /// this function.</para>
    ///
    /// <para>Colour is left white and the tint is applied by the material, so all
    /// <see cref="VoiceCurve.Steps"/> frames share one allocation path and a peer's indicator can be
    /// tinted to that peer's own avatar colour without a second texture.</para>
    /// </summary>
    internal static void Render(int step, byte[] rgba)
    {
        if (rgba == null || rgba.Length < ByteCount)
            return;
        if (step < 0)
            step = 0;
        if (step > VoiceCurve.Steps - 1)
            step = VoiceCurve.Steps - 1;

        const float Inv = 1f / Supersamples;
        const float Taps = Supersamples * Supersamples;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float core = 0f;   // coverage of the glyph at true size
                float rim = 0f;    // coverage of the glyph inflated by RimWidth
                float lit = 0f;    // coverage weighted by how bright that part should be

                for (int sy = 0; sy < Supersamples; sy++)
                {
                    for (int sx = 0; sx < Supersamples; sx++)
                    {
                        float u = (x + (sx + 0.5f) * Inv) / Size;
                        float v = (y + (sy + 0.5f) * Inv) / Size;

                        float a = Brightness(u, v, step, 0f);
                        if (a > 0f)
                        {
                            core += 1f;
                            lit += a;
                        }
                        if (Brightness(u, v, step, RimWidth) > 0f)
                            rim += 1f;
                    }
                }

                int i = (y * Size + x) * 4;
                if (rim <= 0f)
                {
                    rgba[i] = 0; rgba[i + 1] = 0; rgba[i + 2] = 0; rgba[i + 3] = 0;
                    continue;
                }

                // Inside the glyph the pixel is white scaled by how lit that part is; in the rim it
                // is RimLevel. Between the two it is a blend, which is what antialiases the outline.
                float coreFrac = core / Taps;
                float litAvg = core > 0f ? lit / core : 0f;
                float white = 255f * litAvg;
                byte c = (byte)(RimLevel + (white - RimLevel) * coreFrac + 0.5f);

                // Alpha: fully opaque wherever the rim covers, so the dark outline is solid and only
                // the OUTER edge of the rim is feathered.
                byte alpha = (byte)(255f * (rim / Taps) + 0.5f);

                rgba[i] = c;
                rgba[i + 1] = c;
                rgba[i + 2] = c;
                rgba[i + 3] = alpha;
            }
        }
    }

    /// <summary>
    /// How bright the glyph is at (<paramref name="u"/>, <paramref name="v"/>) for
    /// <paramref name="step"/>: 0 outside it, 1 on the cabinet/horn or a lit arc,
    /// <see cref="UnlitAlpha"/> on an arc that is present but dark.
    ///
    /// <para><paramref name="grow"/> inflates every shape by that many normalised units and is how
    /// the rim is produced — one geometry description, sampled twice, so the rim can never drift out
    /// of register with the glyph it outlines.</para>
    /// </summary>
    private static float Brightness(float u, float v, int step, float grow)
    {
        float dy = v - Mid;
        float ady = dy < 0f ? -dy : dy;

        // ---- the cabinet: a plain box -------------------------------------------------------
        if (u >= BodyLeft - grow && u <= BodyRight + grow && ady <= BodyHalfH + grow)
            return 1f;

        // ---- the horn: a wedge opening to the right -----------------------------------------
        if (u >= BodyRight - grow && u <= HornRight + grow)
        {
            float t = (u - BodyRight) / (HornRight - BodyRight);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            float halfH = BodyHalfH + (HornHalfH - BodyHalfH) * t;
            if (ady <= halfH + grow)
                return 1f;
        }

        // ---- the arcs -----------------------------------------------------------------------
        // Centred on the horn's mouth and confined to a forward wedge, so they read as sound
        // leaving the speaker rather than as rings around it.
        float ax = u - HornRight;
        if (ax > -grow)
        {
            float r = Sqrt(ax * ax + dy * dy);
            if (r > 1e-5f && ax / r >= ArcHalfAngleCos - grow)
            {
                for (int k = 1; k <= VoiceCurve.Steps - 1; k++)
                {
                    float mid = ArcRadius(k);
                    float d = r - mid;
                    if (d < 0f) d = -d;
                    if (d <= ArcWidth * 0.5f + grow)
                        return k <= step ? 1f : UnlitAlpha;
                }
            }
        }

        return 0f;
    }

    /// <summary>
    /// Square root without <c>UnityEngine.Mathf</c> or <c>System.Math</c>, so this file has NO
    /// dependency whatsoever and can be linked into any harness. Newton, seeded generously; the
    /// inputs are all in 0..2 and it converges well inside float precision in five steps.
    /// </summary>
    private static float Sqrt(float v)
    {
        if (v <= 0f)
            return 0f;
        float g = v > 1f ? v * 0.5f : 0.5f + v * 0.5f;
        g = 0.5f * (g + v / g);
        g = 0.5f * (g + v / g);
        g = 0.5f * (g + v / g);
        g = 0.5f * (g + v / g);
        g = 0.5f * (g + v / g);
        return g;
    }
}
