using System;
using GloomhavenVR.Core;
using UnityEngine;

public static partial class DiagnosticProgram
{
    private static void QualifyOwnedCopies()
    {
        RenderTexture previous = RenderTexture.active;
        bool previousColor = GL.sRGBWrite;
        Camera previousCamera = Camera.current;
        var stale = Keep(new GameObject("stale-xr-copy-camera")).AddComponent<Camera>();
        stale.enabled = false; stale.rect = new Rect(.3f, .25f, .2f, .15f);
        stale.transform.position = new Vector3(9, -4, 2); stale.transform.rotation = Quaternion.Euler(23, 61, -17);
        var source = Pattern("copy-source-A", 7);
        var alternate = Pattern("copy-source-B", 129);
        try
        {
            Camera.SetupCurrent(stale);
            // Identical inputs returned different samples in the actual B681 log.
            // Alternate actual sources/crops and temporary target sizes while an
            // unrelated camera and restricted viewport remain ambient. Verify the
            // production draw directly against readable CPU source texels.
            foreach (var texture in new[] { source, alternate, source })
                foreach (Rect crop in new[] { new Rect(3, 7, 7, 5), new Rect(11, 2, 13, 9), new Rect(0, 0, 32, 24) })
                {
                    Color32[] expected = texture.GetPixels32();
                    Color32[]? first = null;
                    for (int repeat = 0; repeat < 2; repeat++)
                    {
                        int width = (int)crop.width, height = (int)crop.height;
                        var target = Keep(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
                        Check(target.Create(), "Repeated copy output allocated");
                        RenderTexture.active = previous; GL.sRGBWrite = previousColor;
                        GL.Viewport(new Rect(1, 1, 2, 3));
                        QuestTextureCopy.Copy(texture, target,
                            new Vector2(crop.width / texture.width, crop.height / texture.height),
                            new Vector2(crop.x / texture.width, crop.y / texture.height));
                        Check(RenderTexture.active == previous && GL.sRGBWrite == previousColor && Camera.current == stale,
                            "owned copy preserves prior render target, color state and current camera");
                        // Exercise the actual restored GPU target, without another
                        // RenderTexture.active assignment before clearing/reading.
                        // A cached managed target equality alone cannot prove that
                        // ExecuteCommandBuffer returned the native target binding.
                        GL.Clear(false, true, Color.green);
                        var sentinelPixels = Keep(new Texture2D(previous.width, previous.height, TextureFormat.RGBA32, false, false));
                        sentinelPixels.ReadPixels(new Rect(0, 0, previous.width, previous.height), 0, 0); sentinelPixels.Apply(false);
                        foreach (Color32 pixel in sentinelPixels.GetPixels32())
                            Check(pixel.r == 0 && pixel.g == 255 && pixel.b == 0 && pixel.a == 255,
                                "following GPU operation uses the actual restored sentinel target without rebind");
                        var pixels = Keep(new Texture2D(width, height, TextureFormat.RGBA32, false, false));
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply(false);
                        Color32[] actual = pixels.GetPixels32();
                        Pixels(actual, expected, texture.width, (int)crop.x, (int)crop.y, width, height,
                            "owned copy preserves repeated source/crop RGBA despite ambient camera and viewport");
                        if (first != null)
                            for (int i = 0; i < actual.Length; i++)
                                Check(actual[i].Equals(first[i]), "identical ordinary copy inputs produce identical repeated pixels");
                        first = actual;
                    }
                }
            Check(!QuestTextureCopy.AcceptCache("quest-sampler2d-v1")
                && QuestTextureCopy.AcceptCache(QuestTextureCopy.Recipe),
                "owned copy recipe rejects saved masks made by the former implicit blit quad");
            QuestStandalonePlatform.Enabled = false;
            Check(QuestTextureCopy.AcceptCache("quest-sampler2d-v1"), "desktop cache acceptance remains unchanged");
            QuestStandalonePlatform.Enabled = true;
        }
        finally
        {
            Camera.SetupCurrent(previousCamera); RenderTexture.active = previous; GL.sRGBWrite = previousColor;
        }
    }
}
