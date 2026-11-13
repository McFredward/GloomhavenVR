using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

public static partial class DiagnosticProgram
{
    private static void QualifyReadableCopies()
    {
        var source = Pattern("readable-cpu-authority", 13);
        // Change CPU mip zero without uploading it. A GPU draw still sees the old
        // generation, so this distinguishes copying original readable data from
        // merely passing another successful desktop graphics/readback fixture.
        Color32[] cpu = source.GetPixels32(0);
        for (int i = 0; i < cpu.Length; i++) cpu[i] = new Color32(37, (byte)(i % 251), 193, 87);
        source.SetPixels32(cpu);
        RenderTexture previous = RenderTexture.active;
        bool previousColor = GL.sRGBWrite;
        var baked = CardFaceMipBake.BakedTextureFor(source);
        Check(baked != null, "readable native CPU mip zero can be filtered without GPU readback");
        Pixels(Read(baked!), cpu, source.width, 0, 0, source.width, source.height,
            "readable filtering preserves the exact current CPU source rather than stale GPU pixels");
        Check(RenderTexture.active == previous && GL.sRGBWrite == previousColor,
            "readable filtering retains ambient GPU state");

        var gpuOnly = Pattern("gpu-only-native-original", 67);
        gpuOnly.Apply(false, true);
        var sprite = SpriteOf(gpuOnly, new Rect(4, 6, 9, 7));
        Check(!gpuOnly.isReadable && CardFaceMipBake.BakedTextureFor(gpuOnly) == null,
            "Quest GPU-only original is never replaced by an unqualified readback");
        Check(CardFaceMipBake.ReplacementFor(sprite) == null
            && CardFaceMipBake.PresentationFor(sprite) == sprite && CardFaceMipBake.IsPrepared(sprite),
            "GPU-only sprite keeps exact native art and records the completed optional-filter verdict");
        var rawObject = Keep(new GameObject("gpu-only-native-raw", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)));
        var raw = rawObject.GetComponent<RawImage>(); raw.texture = gpuOnly;
        raw.uvRect = new Rect(.25f, .125f, .5f, .75f); Rect uv = raw.uvRect;
        Check(!GloomhavenVR.WorldUI.PanelMipBake.RefreshRawTexture(raw)
            && raw.texture == gpuOnly && raw.uvRect == uv,
            "GPU-only RawImage keeps the original texture object and authored UVs");

        var desktop = Pattern("desktop-gpu-only-original", 101);
        Color32[] desktopPixels = desktop.GetPixels32(); desktop.Apply(false, true);
        bool stereo = Shader.IsKeywordEnabled("STEREO_INSTANCING_ON");
        Shader.DisableKeyword("STEREO_INSTANCING_ON");
        GL.sRGBWrite = true; // Ordinary desktop camera writes to the sRGB output.
        QuestStandalonePlatform.Enabled = false;
        try
        {
            Texture2D? desktopBake = CardFaceMipBake.BakedTextureFor(desktop);
            Check(desktopBake != null, "desktop GPU-only filtering retains its established readback route");
            Pixels(Read(desktopBake!), desktopPixels, desktop.width, 0, 0, desktop.width, desktop.height,
                "desktop filtering preserves original GPU-only pixels");
        }
        finally { QuestStandalonePlatform.Enabled = true; GL.sRGBWrite = previousColor; if (stereo) Shader.EnableKeyword("STEREO_INSTANCING_ON"); }
    }
}
