using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>Ordinary 2D copies while the Quest XR renderer owns array eye targets.</summary>
public static class QuestTextureCopy
{
    internal const string Recipe = "quest-sampler2d-owned-quad-v2";
    private static Material? _material;
    private static Mesh? _quad;
    private static CommandBuffer? _commands;
    private static MaterialPropertyBlock? _properties;

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
        if (_quad == null)
            _quad = CreateQuad();
        _commands ??= new CommandBuffer { name = "GloomhavenVR.QuestTextureCopy" };
        _properties ??= new MaterialPropertyBlock();
        RenderTexture? previous = RenderTexture.active;
        bool previousSrgbWrite = GL.sRGBWrite;
        try
        {
            _properties.Clear();
            _properties.SetTexture("_MainTex", source);
            _properties.SetVector("_UvScaleOffset", new Vector4(scale.x, scale.y, offset.x, offset.y));
            _commands.Clear();
            _commands.SetRenderTarget(target);
            _commands.SetViewport(new Rect(0, 0, target.width, target.height));
            _commands.DrawMesh(_quad, Matrix4x4.identity, _material, 0, 0, _properties);
            GL.sRGBWrite = target.sRGB;
            // Own the fullscreen vertex upload as well as the ordinary 2D sampler.
            // B681 sampled the identical native source/crop twice with different
            // results. Avoid the builtin blit primitive and snapshot each source
            // and crop independently of retained video-copy materials. This is a
            // controlled Android input repair, not proof of a driver root cause.
            Graphics.ExecuteCommandBuffer(_commands);
        }
        finally
        {
            _commands.Clear();
            _properties.Clear();
            GL.sRGBWrite = previousSrgbWrite;
            RenderTexture.active = previous;
        }
    }

    private static Mesh CreateQuad()
    {
        // Match the existing copy shader's 0..1 position/UV contract, including
        // its API-specific target orientation. No camera matrices or XR arrays.
        var quad = new Mesh { name = "GloomhavenVR.QuestTextureCopy.Quad", hideFlags = HideFlags.HideAndDontSave };
        quad.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
        quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        quad.UploadMeshData(false);
        return quad;
    }
}
