using System;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Translate original prop shader inputs to the owned stereo surface.</summary>
internal static class TownServiceDecorMaterial
{
    internal static Material Copy(Material source, Shader shader)
    {
        var copy = new Material(source) { name = "Town.Original." + source.name,
            shader = shader, renderQueue = shader.renderQueue };
        // Build 544's native open book sampled a uniformly grey atlas quadrant.
        // The original Amp shader uses _UVTiling; its serialized _MainTex_ST=.5
        // is stale Standard-shader data. Original book/jug UVs already address
        // their atlas, and native _UVTiling=1 / _UV_Offset=0 retain those pages.
        // Do not apply this mapping to Standard materials or animated flames.
        bool amp = source.shader != null && source.shader.name.StartsWith("Amp_", StringComparison.Ordinal);
        if (amp && source.HasProperty("_UVTiling"))
        {
            float tiling = source.GetFloat("_UVTiling");
            copy.SetTextureScale("_MainTex", Vector2.one * tiling);
            copy.SetTextureOffset("_MainTex", Vector2.zero);
        }
        if (amp && source.HasProperty("_Tint"))
        {
            Color tint = source.GetColor("_Tint");
            tint.a = 1f; // Native tint alpha is not surface opacity.
            copy.SetColor("_Color", tint);
        }
        if (source.HasProperty("_Smoothness")) copy.SetFloat("_Glossiness", source.GetFloat("_Smoothness"));
        return copy;
    }
}
