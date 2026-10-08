using System;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Ordinary 2D copies while the Quest XR renderer owns array eye targets.</summary>
public static class QuestTextureCopy
{
    internal const string Recipe = "quest-sampler2d-v1";
    private static Material? _material;

    internal static bool AcceptCache(string? recipe) => !QuestStandalonePlatform.Enabled || recipe == Recipe;
    public static void Copy(Texture source, RenderTexture target) => Copy(source, target, Vector2.one, Vector2.zero);

    /// <summary>A separately owned identity copy for retained camera command buffers.</summary>
    public static Material CreateMaterial()
    {
        Shader shader = Resources.Load<Shader>("QuestTextureCopy");
        if (shader == null || shader.name != "Hidden/GloomhavenVR/QuestTextureCopy" || !shader.isSupported)
            throw new InvalidOperationException("Quest ordinary 2D copy shader is unavailable.");
        return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    public static void Copy(Texture source, RenderTexture target, Vector2 scale, Vector2 offset)
    {
        if (!QuestStandalonePlatform.Enabled)
        {
            Graphics.Blit(source, target, scale, offset);
            return;
        }
        if (source == target) throw new ArgumentException("A Quest texture copy cannot read its output target.");
        if (_material == null)
            _material = CreateMaterial();
        RenderTexture? previous = RenderTexture.active;
        bool previousSrgbWrite = GL.sRGBWrite;
        try
        {
            _material.SetVector("_UvScaleOffset", new Vector4(scale.x, scale.y, offset.x, offset.y));
            GL.sRGBWrite = target.sRGB;
            // The dedicated sampler remains 2D even with stereo keywords active.
            // Alpha, crop, and target color space belong to the actual source/RT.
            Graphics.Blit(source, target, _material, 0);
        }
        finally { GL.sRGBWrite = previousSrgbWrite; RenderTexture.active = previous; }
    }
}
