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
    /// </summary>
    internal static Sprite FrameSprite(int cornerRadiusPx = 0)
    {
        int r = Mathf.Clamp(cornerRadiusPx, 0, Border);
        if (s_frames.TryGetValue(r, out Sprite cached) && cached != null)
            return cached;

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            name = $"GloomhavenVR.SoftFrameTex{r}",
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

                float a;
                if (d < 2f)
                    a = Mathf.Max(d, 0f) / 2f;        // soft outer lip
                else if (d <= 6f)
                    a = 1f;                            // bright outline core
                else if (d < Border)
                {
                    float t = (d - 6f) / (Border - 6f); // fade inward to transparent
                    a = (1f - t) * (1f - t);
                }
                else
                    a = 0f;                            // transparent centre (stretched by the 9-slice)
                px[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(updateMipmaps: false);

        // ppu 100 == the uGUI reference, so the border strips render ~Border px thick in UI space.
        var sprite = Sprite.Create(
            tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
        sprite.name = $"GloomhavenVR.SoftFrame{r}";
        s_frames[r] = sprite;
        return sprite;
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
}

/// <summary>
/// Self-driven "breath" for a <see cref="SoftCueArt.FrameSprite"/> outline: alpha swings between
/// <see cref="MinAlpha"/> and <see cref="MaxAlpha"/> while the frame scales by up to
/// <see cref="ScalePulse"/>, the exact pair of motions the initiative ring wears (there driven from
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
internal sealed class SoftFramePulse : MonoBehaviour
{
    /// <summary>Breaths per second — the initiative ring's period, so the cues feel related.</summary>
    private const float PulseHz = 1f / 1.5f;

    /// <summary>Peak of the breathing scale pulse (1 = authored size). Subtle, in-theme.</summary>
    private const float ScalePulse = 0.035f;

    private const float MinAlpha = 0.35f;
    private const float MaxAlpha = 0.95f;

    private Graphic? _graphic;
    private Color _base;

    internal void Init(Graphic graphic, Color baseColor)
    {
        _graphic = graphic;
        _base = baseColor;
    }

    private void OnEnable()
    {
        // Re-apply immediately on show so a frame switched on mid-breath never flashes at the stale
        // alpha of the moment it was hidden.
        Tick();
    }

    private void Update() => Tick();

    private void Tick()
    {
        if (_graphic == null)
            return;
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI * PulseHz));
        Color c = _base;
        c.a = Mathf.Lerp(MinAlpha, MaxAlpha, t) * _base.a;
        _graphic.color = c;
        float s = 1f + ScalePulse * t;
        transform.localScale = new Vector3(s, s, 1f);
    }
}
