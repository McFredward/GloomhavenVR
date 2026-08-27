using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Shared generated ART for the mod's SOFT attention cues — the visual language the user asked for
/// after rejecting the flat gold overlay quads ("Die Overlays … gefallen mir überhaupt nicht … ich
/// dachte eher an a) einen Rahmen, ähnlich wie du es mal bei der Initiativreihenfolge gemacht hast,
/// oder b) Partikeleffekt … dezente Animationen/Effekte drumrum …, die gut und passend aussehen,
/// nicht viereckig sind").
///
/// Two pieces of art, both procedural (no bundle dependency, no asset shipping) and both cached
/// process-wide so N cues share ONE texture:
/// <list type="bullet">
/// <item><see cref="FrameSprite"/> — the hollow, 9-sliced, soft-falloff OUTLINE first built for the
///   initiative-order "still has to choose" ring (<c>InitiativeSelectionGlow</c>). Extracted here
///   verbatim (with <paramref name="cornerRadiusPx"/> 0 reproducing the initiative sprite pixel for
///   pixel) so the item-card "usable now" frame and the initiative ring are literally the SAME
///   sprite recipe and therefore read as one design language.</item>
/// <item><see cref="MoteTexture"/> — a soft ROUND dot for particle cues, so a hint that lives on a
///   deck/stack (where a frame has nothing to frame) is made of drifting embers rather than a box.</item>
/// </list>
///
/// WHY GENERATED AND NOT A BUNDLE ASSET: these cues must survive a missing/failed AssetBundle load
/// (the mod already degrades gracefully everywhere else), and the falloff profile has to be authored
/// against the 9-slice border exactly — which is far easier to state in code than to keep in sync
/// with an imported PNG's border metadata.
/// </summary>
internal static class SoftCueArt
{
    /// <summary>Sprite edge length in px. The whole falloff fits inside <see cref="Border"/>.</summary>
    private const int Size = 48;

    /// <summary>9-slice margin (px). The stretched CENTER tile is fully transparent (hollow frame).</summary>
    private const int Border = 16;

    // One sprite per corner radius (0 = the initiative ring's hard corners, >0 = rounded).
    private static readonly Dictionary<int, Sprite> s_frames = new(4);
    private static Texture2D? s_moteTex;

    /// <summary>
    /// The shared hollow frame sprite: a soft OUTLINE that rises from nothing at the outer lip to a
    /// bright core just inside the edge and then fades inward to nothing, authored so it 9-slices
    /// cleanly (the entire falloff lives inside the <see cref="Border"/> margin, the stretched centre
    /// is fully transparent). White pixels — the caller's <see cref="Graphic.color"/> tints it.
    ///
    /// <paramref name="cornerRadiusPx"/> rounds the outline's corners (rounded-box distance field
    /// instead of the axis-aligned edge distance). 0 gives the ORIGINAL initiative ring bit for bit —
    /// the rounded-box field degenerates to <c>min(dx,dy)</c> at radius 0 — so the initiative cue is
    /// unchanged by this extraction. The item-card frame passes a radius so its outline reads as a
    /// soft, rounded halo hugging the card rather than as the rectangle the user rejected. Clamped to
    /// the border margin so the corner arc always stays inside the 9-slice CORNER tile (a radius past
    /// the margin would be sliced apart and stretch into a smear).
    ///
    /// <para><paramref name="contour"/> selects the CONTOURED profile — see
    /// <see cref="ContourTone"/> for the whole argument. It is a separate cache entry keyed
    /// <c>r + 100</c>, so every existing caller (the initiative ring, the focus stroke, the table
    /// panel ring) keeps the original recipe byte for byte and only the callers that ask for the
    /// contour pay for it.</para>
    /// </summary>
    internal static Sprite FrameSprite(int cornerRadiusPx = 0, bool contour = false)
    {
        int r = Mathf.Clamp(cornerRadiusPx, 0, Border);
        int key = contour ? r + 100 : r;
        if (s_frames.TryGetValue(key, out Sprite cached) && cached != null)
            return cached;

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            name = $"GloomhavenVR.SoftFrameTex{key}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var px = new Color32[Size * Size];
        // Centre in the ORIGINAL integer-edge convention (min(x, Size-1-x)) so radius 0 reproduces the
        // initiative sprite exactly rather than half-a-pixel off it.
        const float c = (Size - 1) * 0.5f;
        float b = c - r; // half-extent of the rounded box's straight section
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // Signed distance to the rounded-box boundary, positive INSIDE (px to the nearest edge).
                float qx = Mathf.Abs(x - c) - b;
                float qy = Mathf.Abs(y - c) - b;
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                float d = r - (outside + inside); // r==0 ⇒ d == min(x, Size-1-x, y, Size-1-y)

                float a, tone;
                if (contour)
                {
                    BandProfile(d, Border, out a, out tone);
                }
                else
                {
                    tone = 1f;
                    if (d < 2f)
                        a = Mathf.Max(d, 0f) / 2f;     // soft outer lip
                    else if (d <= 6f)
                        a = 1f;                        // bright outline core
                    else if (d < Border)
                    {
                        float t = (d - 6f) / (Border - 6f); // fade inward to transparent
                        a = (1f - t) * (1f - t);
                    }
                    else
                        a = 0f;                        // transparent centre (stretched by the 9-slice)
                }
                px[y * Size + x] = Encode(a, tone);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(updateMipmaps: false);

        // ppu 100 == the uGUI reference, so the border strips render ~Border px thick in UI space.
        var sprite = Sprite.Create(
            tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
        sprite.name = $"GloomhavenVR.SoftFrame{key}";
        s_frames[key] = sprite;
        return sprite;
    }

    // =============================================================================================
    //  THE CONTOUR — why the mod's soft cues now carry a DARK band on both sides of the bright one.
    //
    //  USER REPORT (2026-08-09, third one on this area): "Die Animation über dem Pile die anzeigt
    //  dass ein Gegenstand genutzt werden kann ist immer noch zu dezent und kann man schnell
    //  übersehen. Ich mag die Animation aber sie muss mehr herausstechen." — and, for the item-use
    //  recess: "Aktuell ist es einfach so ein schwarzes Rechteck".
    //
    //  BOTH ARE THE SAME PHYSICS PROBLEM, and it is not amplitude. A cue made of ONE tone can only
    //  be seen where it differs in luminance from whatever is behind it, and in this mod what is
    //  behind it is UNKNOWN: mixed reality is a chroma-key composite, so the background is a live
    //  video feed of the player's room. A warm gold outline at 80 % opacity is a strong signal over
    //  a black VR skybox and a nearly INVISIBLE one over a sunlit white wall — and a dark plate is
    //  the exact mirror image, which is why the item-use recess reads as "ein schwarzes Rechteck"
    //  indoors and would vanish against a dark room. Raising the gold's brightness cannot fix the
    //  bright-background half; there is no single tone that is guaranteed to differ from a colour
    //  nobody controls.
    //
    //  A TWO-TONE EDGE ALWAYS HAS ONE. Sandwiching the bright core between two DARK bands is the
    //  same trick subtitles, map labels and HUD reticles have always used: whichever way the
    //  background goes, one of the two tones is far from it, so the cue keeps a luminance edge and
    //  therefore a readable SHAPE. It costs no extra size, no extra motion and no extra opacity —
    //  the cue stays exactly the cue the user said he likes, it just stops depending on the room.
    //
    //  HOW IT IS ENCODED: the dark band is baked into the texture's RGB, not into its alpha, so ONE
    //  tint colour still drives the whole thing. The bright core is white (tint × 1 = the caller's
    //  gold) and the contour is <see cref="ContourTone"/> grey (tint × 0.14 = a deep shadow of the
    //  same gold) — so a caller that recolours the cue recolours core and contour together and they
    //  can never drift into two different hues.
    // =============================================================================================

    /// <summary>How much of the caller's tint the CONTOUR band keeps (the rest is darkness). Low
    /// enough to be a real shadow against a bright room, warm enough to stay the same hue as the
    /// core rather than reading as a black keyline stuck onto a gold cue.</summary>
    private const float ContourTone = 0.14f;

    /// <summary>
    /// The contoured band profile shared by every generated cue in this file, expressed against a
    /// signed distance <paramref name="d"/> (positive INSIDE the shape, in the same units as
    /// <paramref name="band"/>): dark rise, dark hold, bright core, dark hold, dark fade. Both dark
    /// shoulders are deliberately WIDER than the bright core — at VR pixel densities a one-pixel
    /// keyline disappears into the mip chain, and the shoulders are what carry the cue on a bright
    /// background.
    /// </summary>
    private static void BandProfile(float d, float band, out float alpha, out float tone)
    {
        // Fractions of the band, outer lip → inner fade. Deliberately BRIGHT-DOMINANT (44 % core,
        // 28 % of shoulder on each side): the cue is a gold outline that a dark edge keeps legible,
        // not a dark outline with a gold filament in it. At the thicknesses these cues ship at, a
        // core much thinner than this stops reading as gold at all once the mip chain gets to it.
        float lip = band * 0.08f;   // 0.00 – 0.08  dark, alpha rising out of nothing
        float dark0 = band * 0.20f; // 0.08 – 0.20  dark, opaque
        float ramp0 = band * 0.28f; // 0.20 – 0.28  dark → bright
        float core = band * 0.72f;  // 0.28 – 0.72  bright core
        float ramp1 = band * 0.80f; // 0.72 – 0.80  bright → dark
        float dark1 = band * 0.90f; // 0.80 – 0.90  dark, opaque
        if (d <= 0f || d >= band)
        {
            alpha = 0f;
            tone = 0f;
            return;
        }
        if (d < lip) { alpha = d / lip; tone = 0f; return; }
        if (d < dark0) { alpha = 1f; tone = 0f; return; }
        if (d < ramp0) { alpha = 1f; tone = (d - dark0) / (ramp0 - dark0); return; }
        if (d < core) { alpha = 1f; tone = 1f; return; }
        if (d < ramp1) { alpha = 1f; tone = 1f - (d - core) / (ramp1 - core); return; }
        if (d < dark1) { alpha = 1f; tone = 0f; return; }
        float t = (d - dark1) / (band - dark1);
        alpha = (1f - t) * (1f - t);
        tone = 0f;
    }

    /// <summary>Pack a band sample: <paramref name="tone"/> 1 = the caller's tint at full strength
    /// (the bright core), 0 = <see cref="ContourTone"/> of it (the dark contour).</summary>
    private static Color32 Encode(float alpha, float tone)
    {
        byte rgb = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(ContourTone, 1f, tone) * 255f), 0, 255);
        return new Color32(rgb, rgb, rgb, (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
    }

    /// <summary>
    /// Soft ROUND mote for particle cues: a radial falloff disc (bright core, quadratic fade to nothing
    /// at the rim) on white pixels, tinted by the particle's own start colour. Round on purpose — the
    /// deck cue exists because the user rejected rectangles, so the individual particles must not be the
    /// hard-edged squares an untextured <c>Sprites/Default</c> particle material would draw.
    /// </summary>
    internal static Texture2D MoteTexture()
    {
        if (s_moteTex != null)
            return s_moteTex;

        const int size = 32;
        const float c = (size - 1) * 0.5f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            name = "GloomhavenVR.SoftMoteTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x - c, y - c).magnitude / c; // 0 centre … 1 rim
                float a = Mathf.Clamp01(1f - d);
                a *= a;                       // quadratic — a soft ember, no visible disc edge
                a = Mathf.Min(1f, a * 1.35f); // small bright core so a 4 mm mote still reads in VR
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(updateMipmaps: true);
        s_moteTex = tex;
        return tex;
    }

    // ---------------------------------------------------------------- world-space cue quads --
    //
    // WHY THESE ARE QUADS AND NOT uGUI IMAGES, even though FrameSprite exists two screens up. A cue
    // that lives ON the control board has to join the board's furniture draw-order group
    // (PlayTray.AdoptFurniture), and that group is a list of RENDERERS — a world-space Canvas draws
    // through CanvasRenderer, which the sweep cannot see, so a canvas cue mounted on the board would
    // be the only board widget a converted panel behind the board could still paint over. (That is
    // exactly why Cards/ItemsPile's canvas frame rides CardGlow.RankWithPanels instead: it hangs in
    // free space, off the board, where the furniture group is not the right answer.) A textured Quad
    // with a MeshRenderer is what the rest of the board's art already is, so these cues inherit the
    // board's whole ordering story for free.
    //
    // The band thickness is baked PER CALLER GEOMETRY rather than 9-sliced, and that is the point:
    // the caller states a thickness in METRES and the texture is generated with the caller's aspect
    // ratio compensated out, so the outline comes out the same thickness on every side of a
    // non-square rect. A 9-slice would give the same guarantee but only inside a Canvas.

    private static readonly Dictionary<long, Texture2D> s_bandTex = new(8);
    private static Material? s_bandMatTemplate;

    /// <summary>Resolution of the generated band textures. The band itself is only a few percent of
    /// the texture, so this is really "how many texels the two-tone edge gets": at 128 a 4 mm band on
    /// a card-sized quad is ~8 texels for the whole lip/shoulder/core/shoulder/fade profile, which
    /// magnifies into mush at VR pixel densities. 256 doubles that for 256 KB apiece, and the mod
    /// caches a couple of these for the whole process.</summary>
    private const int BandTexSize = 256;

    /// <summary>
    /// A card-shaped ROUNDED-RECT outline quad: constant-thickness two-tone band (see the CONTOUR
    /// note) hugging a <paramref name="width"/> × <paramref name="height"/> rectangle with
    /// <paramref name="corner"/>-radius corners, all in the parent's local metres. Collider-free,
    /// double-sided (Sprites/Default), tinted by <paramref name="color"/>.
    /// </summary>
    internal static GameObject RectOutlineQuad(string name, Transform parent, Vector3 localPos,
        float width, float height, float band, float corner, Color color)
    {
        GameObject quad = MakeQuad(name, parent, localPos, new Vector3(width, height, 1f), color);
        var renderer = quad.GetComponent<MeshRenderer>();
        if (renderer.sharedMaterial != null)
            renderer.sharedMaterial.mainTexture = RoundedRectBand(width, height, band, corner);
        return quad;
    }

    /// <summary>
    /// A ROUND ring quad — the hollow sibling of <see cref="MoteTexture"/>. Same two-tone band, no
    /// corners at all, so a cue that has nothing rectangular to trace (a pile of cards lying flat)
    /// can still draw a hard-edged shape without becoming the rectangle of light the user rejected.
    /// <paramref name="diameter"/> and <paramref name="band"/> are parent-local metres.
    /// </summary>
    internal static GameObject RingQuad(string name, Transform parent, Vector3 localPos,
        float diameter, float band, Color color)
    {
        GameObject quad = MakeQuad(name, parent, localPos, new Vector3(diameter, diameter, 1f), color);
        var renderer = quad.GetComponent<MeshRenderer>();
        if (renderer.sharedMaterial != null)
            renderer.sharedMaterial.mainTexture = RoundBand(band / Mathf.Max(diameter, 1e-5f));
        return quad;
    }

    /// <summary>
    /// A plain translucent FIELD quad in the same alpha-blended, double-sided, self-owned-material
    /// family as the two band quads above (no texture — the tint IS the whole surface).
    ///
    /// <para>It exists so a cue can fill an area with LIGHT rather than with darkness. A dark plate
    /// is a shape only against a bright background, and mixed reality hands the mod a background it
    /// does not control; a pale warm wash is the recipe <c>ItemsPile.BuildUseGhost</c> already uses
    /// for "a card belongs here", so a berth built from it is the ghost's resting state rather than
    /// a second visual idea.</para>
    /// </summary>
    internal static GameObject FieldQuad(string name, Transform parent, Vector3 localPos,
        float width, float height, Color color) =>
        MakeQuad(name, parent, localPos, new Vector3(width, height, 1f), color);

    private static GameObject MakeQuad(string name, Transform parent, Vector3 localPos,
        Vector3 localScale, Color color)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, worldPositionStays: false);
        quad.transform.localPosition = localPos;
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = localScale;
        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        // Per-quad material INSTANCE on purpose: every one of these cues animates its own colour
        // (SoftCuePing fades it, SoftCueReveal multiplies it), and the mod's established pattern for
        // a self-animating board widget is to write sharedMaterial.color on a material only it owns.
        s_bandMatTemplate ??= MakeBandMaterial();
        if (s_bandMatTemplate != null)
            renderer.sharedMaterial = new Material(s_bandMatTemplate) { color = color };
        return quad;
    }

    private static Material? MakeBandMaterial()
    {
        Shader? sh = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        return sh != null ? new Material(sh) : null;
    }

    /// <summary>
    /// Generated rounded-rect band texture for a quad of the given local size. The distance field is
    /// evaluated in METRES (the pixel grid is mapped onto the caller's real rectangle), which is what
    /// makes <paramref name="band"/> come out the same thickness top/bottom as left/right on a
    /// non-square card — a texture authored in UV space would stretch with the quad. Cached by the
    /// quantised (aspect, band, corner) triple, so the handful of cue geometries in the mod share a
    /// couple of textures however many cues are built.
    /// </summary>
    private static Texture2D RoundedRectBand(float width, float height, float band, float corner)
    {
        float w = Mathf.Max(width, 1e-4f);
        float h = Mathf.Max(height, 1e-4f);
        float t = Mathf.Clamp(band, 1e-4f, Mathf.Min(w, h) * 0.45f);
        float r = Mathf.Clamp(corner, 0f, Mathf.Min(w, h) * 0.5f - t * 0.5f);
        long key = (long)Mathf.RoundToInt(h / w * 512f) * 1_000_000L
                   + (long)Mathf.RoundToInt(t / w * 4096f) * 1_000L
                   + Mathf.RoundToInt(r / w * 512f);
        if (s_bandTex.TryGetValue(key, out Texture2D cached) && cached != null)
            return cached;

        var tex = NewBandTexture($"GloomhavenVR.SoftRectBand{key}");
        var px = new Color32[BandTexSize * BandTexSize];
        float hx = w * 0.5f, hy = h * 0.5f;
        float bx = hx - r, by = hy - r; // half-extents of the rounded box's straight section
        for (int y = 0; y < BandTexSize; y++)
        {
            float py = ((y + 0.5f) / BandTexSize - 0.5f) * h;
            for (int x = 0; x < BandTexSize; x++)
            {
                float pxm = ((x + 0.5f) / BandTexSize - 0.5f) * w;
                float qx = Mathf.Abs(pxm) - bx;
                float qy = Mathf.Abs(py) - by;
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                float d = r - (outside + inside); // metres inside the rounded-rect boundary
                BandProfile(d, t, out float a, out float tone);
                px[y * BandTexSize + x] = Encode(a, tone);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(updateMipmaps: true);
        s_bandTex[key] = tex;
        return tex;
    }

    /// <summary>Generated ROUND band texture; <paramref name="bandFraction"/> is the band thickness
    /// as a fraction of the ring's diameter. Cached like <see cref="RoundedRectBand"/>.</summary>
    private static Texture2D RoundBand(float bandFraction)
    {
        float t = Mathf.Clamp(bandFraction, 0.01f, 0.45f);
        long key = -(long)Mathf.RoundToInt(t * 4096f) - 1L; // negative half of the shared cache
        if (s_bandTex.TryGetValue(key, out Texture2D cached) && cached != null)
            return cached;

        var tex = NewBandTexture($"GloomhavenVR.SoftRoundBand{-key}");
        var px = new Color32[BandTexSize * BandTexSize];
        for (int y = 0; y < BandTexSize; y++)
        {
            float py = (y + 0.5f) / BandTexSize - 0.5f;
            for (int x = 0; x < BandTexSize; x++)
            {
                float pxn = (x + 0.5f) / BandTexSize - 0.5f;
                // Distance INSIDE the unit-diameter circle, in the same units as the band.
                float d = 0.5f - new Vector2(pxn, py).magnitude;
                BandProfile(d, t, out float a, out float tone);
                px[y * BandTexSize + x] = Encode(a, tone);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(updateMipmaps: true);
        s_bandTex[key] = tex;
        return tex;
    }

    private static Texture2D NewBandTexture(string name) =>
        new(BandTexSize, BandTexSize, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            // These cues are viewed at a steep angle across the tilted board; without anisotropy the
            // thin band aliases into a dashed line, which is the "shimmer" half of "zu dezent".
            anisoLevel = 4,
        };

    /// <summary>
    /// KEY-COLOR SAFETY for a generated cue colour (the rule <c>WorldUI.MrBacking</c> states for its
    /// backing plates, applied to cue art): mixed reality is a CHROMA-KEY composite, so any surface
    /// the mod draws in (or near) the live key colour is not a dark shape — it is a HOLE punched
    /// straight through to the passthrough camera, exactly where readability was wanted. The mod's
    /// gold is nowhere near the green/magenta/blue presets, but the two-tone contour above is a very
    /// dark warm grey and the BLACK preset sits right on top of it. When the live key comes within
    /// keying distance, the colour is pushed to whichever of a warm-light / warm-dark pair is FARTHER
    /// from the key, so the cue survives every preset without the caller having to know which is set.
    /// Alpha is preserved. Safe before the config is bound (returns the colour unchanged).
    /// </summary>
    internal static Color KeySafe(Color color)
    {
        var entry = Core.MixedReality.KeyColor;
        if (entry == null)
            return color;
        Color key = entry.Value;
        const float keyDistance = 0.25f; // MrBacking's own threshold — one rule, one number
        if (Mathf.Abs(key.r - color.r) >= keyDistance
            || Mathf.Abs(key.g - color.g) >= keyDistance
            || Mathf.Abs(key.b - color.b) >= keyDistance)
            return color;
        var light = new Color(0.86f, 0.80f, 0.68f, color.a);
        var dark = new Color(0.14f, 0.11f, 0.08f, color.a);
        float dl = Mathf.Abs(key.r - light.r) + Mathf.Abs(key.g - light.g) + Mathf.Abs(key.b - light.b);
        float dd = Mathf.Abs(key.r - dark.r) + Mathf.Abs(key.g - dark.g) + Mathf.Abs(key.b - dark.b);
        return dl >= dd ? light : dark;
    }

    // ---------------------------------------------------------------- shared cue rhythm --

    /// <summary>
    /// THE MOD'S ATTENTION RHYTHM — a double HEARTBEAT, not a sine.
    ///
    /// <para>WHY THE WAVEFORM IS THE FIX AND THE AMPLITUDE WAS NOT (user report 2026-08-09, third on
    /// this area: "immer noch zu dezent und kann man schnell übersehen"). A sine-breathing cue spends
    /// most of its cycle at some middling brightness and changes slowly everywhere; the retina's
    /// periphery — which is what "übersehen" is about, because the player is looking at their hand or
    /// the map, not at the pile — is a TRANSIENT detector. It is driven by sudden change, and it is
    /// almost blind to a slow ramp. So a sine is close to the least noticeable rhythm a pulsing cue
    /// could have picked, at any amplitude: turning it up makes the middling state brighter, which
    /// is not what the periphery answers to.</para>
    ///
    /// <para>A heartbeat is the opposite shape: a fast attack, a decay, a smaller echo, and then a
    /// REST. The rest is doing as much work as the beats — it is what makes the next attack a change
    /// rather than a continuation, and it is why two hearts' worth of light per cycle out-shouts a
    /// sine that is lit the whole time. It is also a rhythm with meaning (something alive, waiting),
    /// which is the register this cue wants; and it stays recognisably the same breathing cue the
    /// user said he likes rather than replacing it with a new effect.</para>
    ///
    /// <para><paramref name="t"/> is the cycle phase 0..1; the result is 0..1.</para>
    /// </summary>
    internal static float Heartbeat(float t)
    {
        float first = Beat(t, 0.00f, 0.30f);
        float echo = 0.62f * Beat(t, 0.26f, 0.26f); // the second, softer thump
        return Mathf.Clamp01(Mathf.Max(first, echo));
    }

    /// <summary>One thump: a 25 % attack and a 75 % decay inside its own window (0 outside).</summary>
    private static float Beat(float t, float start, float width)
    {
        float u = (t - start) / width;
        if (u <= 0f || u >= 1f)
            return 0f;
        return u < 0.25f ? u / 0.25f : 1f - (u - 0.25f) / 0.75f;
    }
}

/// <summary>
/// Self-driven "breath" for a <see cref="SoftCueArt.FrameSprite"/> outline: alpha swings between
/// <see cref="MinAlpha"/> and <see cref="MaxAlpha"/> while the frame scales by up to
/// <see cref="InitiativeSelectionGlow.ScalePulse"/>, the exact pair of motions the initiative ring wears (there driven from
/// <c>SelectionReadyHighlighter</c>'s own tick) — so the two cues breathe alike.
///
/// WHY IT DRIVES ITSELF: the item-card frame has no per-frame owner willing to push an alpha (the
/// chip's maintenance tick is change-gated down to a bool compare and must stay that way), and a cue
/// that only exists while it is switched on is cheapest as a component that dies with its GameObject.
///
/// WHY <see cref="Time.unscaledTime"/>: every frame in the fan is driven by the SAME global clock, so
/// several usable item cards breathe in PHASE and read as one cue instead of several competing
/// flickers — and the breath keeps running while the game pauses simulation time (modals, camera moves).
/// </summary>
/// <para>SINCE 2026-08-09 IT BEATS RATHER THAN BREATHES — see <see cref="SoftCueArt.Heartbeat"/> for
/// the whole argument (the user's third "zu dezent" report on the item area). Everything else about
/// the cue is unchanged: same sprite, same colour, same pair of motions, same shared clock.</para>
internal sealed class SoftFramePulse : MonoBehaviour
{
    /// <summary>Seconds per beat cycle. The initiative ring's own period, kept, so the cues stay
    /// relatives — only the shape of the curve inside the cycle changed.</summary>
    private float _beatSeconds = 1.5f;

    /// <summary>Peak of the beating scale pulse (1 = authored size). A SILHOUETTE change, which is
    /// the half of the cue a bright, busy passthrough room cannot swallow.</summary>
    private float _scalePulse = 0.035f;

    private float _minAlpha = 0.35f;
    private float _maxAlpha = 0.95f;

    private Graphic? _graphic;
    private Color _base;
    private SoftCueReveal? _reveal;

    internal void Init(Graphic graphic, Color baseColor)
    {
        _graphic = graphic;
        _base = baseColor;
        _reveal = GetComponentInParent<SoftCueReveal>();
    }

    /// <summary>Amplitude-tunable variant: the caller (which is the side that owns the config file)
    /// states the rhythm and the depth of the swing. Same cue, louder or quieter.</summary>
    internal void Init(Graphic graphic, Color baseColor, float beatSeconds, float minAlpha,
        float maxAlpha, float scalePulse)
    {
        Init(graphic, baseColor);
        _beatSeconds = Mathf.Max(0.05f, beatSeconds);
        _minAlpha = Mathf.Clamp01(minAlpha);
        _maxAlpha = Mathf.Clamp01(maxAlpha);
        _scalePulse = Mathf.Max(0f, scalePulse);
    }

    private void OnEnable()
    {
        // Re-apply immediately on show so a frame switched on mid-beat never flashes at the stale
        // alpha of the moment it was hidden.
        Tick();
    }

    private void Update() => Tick();

    private void Tick()
    {
        if (_graphic == null)
            return;
        float t = SoftCueArt.Heartbeat(Mathf.Repeat(Time.unscaledTime / _beatSeconds, 1f));
        Color c = _base;
        c.a = Mathf.Lerp(_minAlpha, _maxAlpha, t) * _base.a * (_reveal != null ? _reveal.Amount : 1f);
        _graphic.color = c;
        float s = 1f + _scalePulse * t;
        transform.localScale = new Vector3(s, s, 1f);
    }
}

/// <summary>
/// A repeating RING PING on one of <see cref="SoftCueArt"/>'s band quads: the ring travels between
/// two sizes and fades out, then rests before the next one.
///
/// <para>WHY A TRAVELLING RING IS THE CUE THAT SURVIVES PASSTHROUGH. The two things a chroma-keyed
/// living room is full of are luminance contrast and static edges; what it does NOT contain is a
/// shape that changes size. A pulsing outline only modulates BRIGHTNESS at a fixed silhouette, so it
/// competes directly with the background on the one axis the background already wins. A ring whose
/// diameter moves is a silhouette change — the eye's periphery reads it as an event, it survives the
/// lower-resolution motion-blurred passthrough feed, and in stereo it is unambiguously in front of
/// the room rather than part of it.</para>
///
/// <para>DIRECTION CARRIES MEANING, and both directions are used. <c>from &lt; to</c> is an OUTWARD
/// ping — "look over here", which is what the closed item pile says. <c>from &gt; to</c> is an
/// INWARD ping that closes onto a target — "put it in HERE", which is what the item-use recess says.
/// Same component, same rhythm, opposite sentence.</para>
///
/// <para>The rest between pings is deliberate (see <see cref="SoftCueArt.Heartbeat"/>): a ring that
/// is always somewhere is a continuous state, and a state is exactly what the eye stops reporting.
/// <see cref="Phase"/> lets two pings share a period at opposite phases so the cue never has a long
/// silence while still never being continuous.</para>
/// </summary>
internal sealed class SoftCuePing : MonoBehaviour
{
    private MeshRenderer? _renderer;
    private Color _base;
    private Vector3 _fromScale;
    private Vector3 _toScale;
    private float _period = 1.5f;
    private float _phase;
    private float _duty = 0.7f;
    private SoftCueReveal? _reveal;

    /// <summary>Phase offset in cycles (0..1) — see the class doc.</summary>
    internal float Phase { get => _phase; set => _phase = value; }

    internal void Init(MeshRenderer renderer, Color baseColor, Vector3 fromScale, Vector3 toScale,
        float period, float duty = 0.7f)
    {
        _renderer = renderer;
        _base = baseColor;
        _fromScale = fromScale;
        _toScale = toScale;
        _period = Mathf.Max(0.1f, period);
        _duty = Mathf.Clamp(duty, 0.1f, 1f);
        _reveal = GetComponentInParent<SoftCueReveal>();
        Tick();
    }

    private void OnEnable() => Tick();

    private void Update() => Tick();

    private void Tick()
    {
        if (_renderer == null || _renderer.sharedMaterial == null)
            return;
        float cycle = Mathf.Repeat(Time.unscaledTime / _period + _phase, 1f);
        Color c = _base;
        if (cycle > _duty)
        {
            // Resting between pings — parked at the start size so the next ping has no jump to make.
            transform.localScale = _fromScale;
            c.a = 0f;
            _renderer.sharedMaterial.color = c;
            return;
        }
        float t = cycle / _duty;
        // Ease-OUT travel: the ring leaps away from (or onto) the target and then coasts, which is
        // the profile that reads as "thrown" rather than as a slider being dragged.
        float eased = 1f - (1f - t) * (1f - t);
        transform.localScale = Vector3.Lerp(_fromScale, _toScale, eased);
        // Fast attack (the transient the periphery answers to), long linear fade.
        float a = Mathf.Clamp01(t / 0.12f) * (1f - t);
        c.a = _base.a * a * (_reveal != null ? _reveal.Amount : 1f);
        _renderer.sharedMaterial.color = c;
    }
}

/// <summary>
/// The ARRIVAL and DEPARTURE of a whole cue subtree — the mod's standing "nothing pops" rule applied
/// to a widget that used to be a raw <c>SetActive</c> toggle (the item-use recess: it blinked into
/// existence the moment an item became placeable and blinked out again when it stopped, which is the
/// one transition in the item flow that was never animated).
///
/// <para>It scales the subtree from <see cref="SeedScale"/> up to its authored size with a small
/// back-ease overshoot and multiplies every renderer's alpha in from zero, then holds at
/// <see cref="Amount"/> 1 — the self-animating cues below it (<see cref="SoftFramePulse"/>,
/// <see cref="SoftCuePing"/>) read that number and fold it into their own colour, so the reveal and
/// the rhythm never fight over the same material.</para>
///
/// <para>ON HIDE IT RUNS IN REVERSE AND ONLY THEN DEACTIVATES <see cref="DeactivateTarget"/>. That is
/// why the caller must ask this component to hide rather than calling SetActive itself: a component
/// cannot animate its own disappearance from inside a GameObject that has already been switched
/// off. The gates that read the target's <c>activeSelf</c> therefore see it stay true for the length
/// of the out-animation; every one of them is a coarse pre-filter in front of the real
/// authority (the game's own activatable/demand checks), so a release inside that window is still
/// resolved by those, not by this.</para>
/// </summary>
internal sealed class SoftCueReveal : MonoBehaviour
{
    /// <summary>Size the subtree grows from / collapses back to, as a fraction of authored.</summary>
    private const float SeedScale = 0.84f;

    /// <summary>Back-ease overshoot on arrival — a REVERSAL of direction, the loudest thing a short
    /// motion has, and it costs no extra travel (the item-fan pass's finding, reused).</summary>
    private const float Overshoot = 1.7f;

    private readonly System.Collections.Generic.List<Renderer> _renderers = new(4);
    private readonly System.Collections.Generic.List<Color> _baseColors = new(4);

    private float _seconds = 0.24f;
    private float _t;           // 0 = fully hidden, 1 = fully present
    private int _dir;           // +1 arriving, -1 leaving, 0 settled

    /// <summary>The GameObject switched off once the LEAVING animation completes (normally the cue's
    /// own root, one level above this component). Null = this component's own GameObject.</summary>
    internal GameObject? DeactivateTarget { get; set; }

    /// <summary>0..1 presence, published for the self-animating cues under this subtree.</summary>
    internal float Amount { get; private set; } = 1f;

    /// <summary>Seconds one direction of the transition takes.</summary>
    internal void Configure(float seconds) => _seconds = Mathf.Max(0.01f, seconds);

    /// <summary>
    /// Register one piece of art whose OPACITY this reveal fades (its authored colour is latched
    /// here, so call it while the colour is still the authored one).
    ///
    /// <para>DELIBERATELY OPT-IN, not a <c>GetComponentsInChildren&lt;Renderer&gt;</c> sweep. Two
    /// renderer kinds under a cue subtree carry a material that is SHARED process-wide, and writing
    /// a colour into either of them would repaint far more than this cue: a TextMeshPro draws
    /// through one font-atlas material shared with every other label in the game, and an MR backing
    /// plate (<see cref="MrBacking"/>) draws through one static plate material shared with every
    /// other backed label. A sweep cannot tell those apart from a quad the cue owns outright, so the
    /// builder names what it owns. Everything else still rides the reveal's SCALE, which touches no
    /// material at all.</para>
    /// </summary>
    internal void Track(GameObject? art)
    {
        var r = art != null ? art.GetComponent<Renderer>() : null;
        if (r == null || r.sharedMaterial == null)
            return;
        _renderers.Add(r);
        _baseColors.Add(r.sharedMaterial.color);
    }

    /// <summary>Play the ARRIVAL (idempotent while already arriving/arrived). It resumes from
    /// wherever a running departure had got to rather than restarting from zero — a re-show during a
    /// dismiss must catch the motion, not replace it with a second pop.</summary>
    internal void Show()
    {
        _dir = 1;
        Apply();
    }

    /// <summary>Play the DEPARTURE and switch <see cref="DeactivateTarget"/> off at the end of it.</summary>
    internal void Hide()
    {
        _dir = -1;
        Apply();
    }

    /// <summary>Arrive with no animation at all (first build, or a rebuild mid-state).</summary>
    internal void SnapShown()
    {
        _t = 1f;
        _dir = 0;
        Apply();
    }

    private void Update()
    {
        if (_dir == 0)
            return;
        _t = Mathf.Clamp01(_t + _dir * Time.unscaledDeltaTime / _seconds);
        Apply();
        if (_dir > 0 && _t >= 1f)
        {
            _dir = 0;
        }
        else if (_dir < 0 && _t <= 0f)
        {
            _dir = 0;
            (DeactivateTarget != null ? DeactivateTarget : gameObject).SetActive(false);
        }
    }

    private void Apply()
    {
        // Arrival overshoots and settles; departure is a plain ease-in (a collapse should not bounce).
        float e = _dir >= 0 ? EaseOutBack(_t) : _t * _t;
        float s = Mathf.Lerp(SeedScale, 1f, e);
        transform.localScale = new Vector3(s, s, 1f);
        Amount = Mathf.Clamp01(_t);
        for (int i = 0; i < _renderers.Count; i++)
        {
            Renderer r = _renderers[i];
            if (r == null || r.sharedMaterial == null)
                continue;
            Color c = _baseColors[i];
            c.a *= Amount;
            r.sharedMaterial.color = c;
        }
    }

    private static float EaseOutBack(float t)
    {
        float u = t - 1f;
        return 1f + (Overshoot + 1f) * u * u * u + Overshoot * u * u;
    }
}
