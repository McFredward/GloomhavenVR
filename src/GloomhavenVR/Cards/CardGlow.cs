using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Shared factory for the emissive "drop here" telegraph glow used by BOTH the control-board
/// slot overlay (<see cref="PlayTray"/>) and the hand-fan insertion gap overlay
/// (<see cref="CardFan"/>) — extracted so the two look IDENTICAL (gold, additive, card-shaped).
/// The glow material routes through <see cref="PlayTray.OverlayShader"/> (the bundled
/// <c>GloomhavenVR/Overlay</c> additive shader) with a <c>Sprites/Default</c> fallback, exactly
/// as the board slot glow did before extraction — so board behaviour is unchanged.
/// </summary>
internal static class CardGlow
{
    /// <summary>
    /// An emissive glow material tinted <paramref name="color"/>: Overlay with ADDITIVE blend
    /// (<c>_SrcBlend=One,_DstBlend=One</c>) so the tint reads as light ADDED over the surface,
    /// ZWrite off, drawn in the transparent queue after the opaque board so it occludes
    /// naturally (no shine-through). Falls back to alpha-blended Sprites/Default when Overlay is
    /// absent. Null only when even the fallback shader is missing. (Verbatim move of the former
    /// <c>PlayTray.MakeGlowMaterial</c>.)
    /// </summary>
    internal static Material? MakeGlowMaterial(Color color)
    {
        Shader? s = PlayTray.OverlayShader();
        if (s != null)
        {
            var m = new Material(s) { color = color };
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); // additive
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }
        Shader? fb = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        return fb != null ? new Material(fb) { color = color } : null;
    }

    /// <summary>
    /// Build a card-shaped glow quad (the exact recipe the board slot overlay used): a collider-
    /// free <see cref="PrimitiveType.Quad"/> parented to <paramref name="parent"/> at
    /// <paramref name="localPos"/>/<paramref name="localScale"/>, tinted via
    /// <see cref="MakeGlowMaterial"/>, created INACTIVE (the caller toggles it). Returns the quad
    /// GameObject so the caller can attach extra behaviour (e.g. a pulse) or toggle it.
    /// </summary>
    internal static GameObject CreateGlowQuad(string name, Transform parent, Vector3 localScale,
        Vector3 localPos, Color color)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, worldPositionStays: false);
        quad.transform.localScale = localScale;
        quad.transform.localPosition = localPos;
        Material? mat = MakeGlowMaterial(color);
        if (mat != null)
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        quad.SetActive(false);
        return quad;
    }

    /// <summary>
    /// Give a glow quad the calm "breathing" animation the board's wanted-slot hint wears
    /// (verbatim math from <c>PlayTray.SlotPulse</c>: 3.2 rad/s, brightness 0.30 → 0.85), so an
    /// attention cue reads as living light instead of a flat sticker. Extracted here so ANY
    /// mod-owned glow (item-card usable rim, items-stack rim) can wear it without its owner
    /// running a per-frame Update.
    ///
    /// WHY unscaled time (and why that matters here): every pulse is driven by the SAME global
    /// clock, so N simultaneously-pulsing glows — e.g. three usable item cards in the fan —
    /// breathe in PHASE and read as one coherent cue rather than as three competing flickers.
    /// It also keeps breathing while the game pauses simulation time (modal dialogs, menus).
    ///
    /// Safe to call on any quad built by <see cref="CreateGlowQuad"/>: that factory hands each
    /// quad its OWN material instance, so writing <c>sharedMaterial.color</c> here can never
    /// bleed into another glow.
    /// </summary>
    internal static void AddPulse(GameObject quad, Color baseColor)
    {
        if (quad == null)
            return;
        var mr = quad.GetComponent<MeshRenderer>();
        if (mr != null)
            quad.AddComponent<GlowPulse>().Init(mr, baseColor);
    }

    /// <summary>Self-animated soft pulse for a glow quad (no owner Update). See <see cref="AddPulse"/>.</summary>
    private sealed class GlowPulse : MonoBehaviour
    {
        private MeshRenderer? _renderer;
        private Color _base;

        internal void Init(MeshRenderer? renderer, Color baseColor)
        {
            _renderer = renderer;
            _base = baseColor;
        }

        private void Update()
        {
            if (_renderer == null || _renderer.sharedMaterial == null)
                return;
            // The glow material uses the ADDITIVE Overlay shader (rgb IS the emitted brightness,
            // alpha unused), so scale rgb by the breath; alpha is breathed too so the
            // alpha-blended Sprites/Default fallback (Overlay bundle absent) still pulses.
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);
            float k = Mathf.Lerp(0.30f, 0.85f, t);
            Color c = _base;
            c.r *= k;
            c.g *= k;
            c.b *= k;
            c.a = k;
            _renderer.sharedMaterial.color = c;
        }
    }
}
