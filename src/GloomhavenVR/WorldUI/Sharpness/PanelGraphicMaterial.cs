using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Inspect capture graphics without creating private TextMeshPro material instances.</summary>
internal static class PanelGraphicMaterial
{
    // Build 509 user/Player.log: merely evaluating TMP_SubMeshUI.material while a pooled
    // text submesh has no source material calls CreateMaterialInstance(null). The throw
    // escapes the capture-layer walk, tears down every panel target and repeats until
    // supersampling is disabled. A populated source also allocates an unnecessary private
    // instance. Shared getters are passive and preserve the game's font/sprite binding;
    // a not-yet-populated submesh is left untouched until native TMP supplies its material.
    // Do not replace this with materialForRendering: that evaluates material modifiers.
    internal static Material? Read(Graphic? graphic)
    {
        if (graphic == null)
            return null;
        if (graphic is TMP_SubMeshUI submesh)
            return submesh.sharedMaterial;
        if (graphic is TMP_Text text)
            return text.fontSharedMaterial;
        return graphic.material;
    }

    internal static bool NeutraliseGrabPass(Graphic? graphic)
    {
        Material? material = Read(graphic);
        if (graphic == null || material == null || material.shader == null
            || material.shader.name.IndexOf("GrabPass", System.StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        // Retain the game's UIBlurDisabler remedy for the offending backdrop only.
        // Never mutate the shared material asset or substitute a font material.
        graphic.material = null;
        graphic.color = Color.clear;
        graphic.enabled = false;
        return true;
    }
}
