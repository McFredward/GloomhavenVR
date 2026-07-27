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

    // NOTE (removed) — KEEP THIS COMMENT. It documents code that is deliberately absent, which is
    // the only kind of note a reader cannot reconstruct from the source, and deleting it as "a
    // comment about nothing" invites exactly the re-addition it forbids.
    // An AddPulse/GlowPulse pair used to live here so a glow QUAD could breathe on its
    // own. Its only two callers were the item-card and items-stack "usable now" cues, and the user
    // rejected that whole flat-quad look — the card is now framed by WorldUI.SoftCueArt's outline
    // (breathing via SoftFramePulse) and the stack drifts embers, so nothing wanted a pulsing quad any
    // more. The board play slots keep their own PlayTray.SlotPulse, which is where that math came from.
}
