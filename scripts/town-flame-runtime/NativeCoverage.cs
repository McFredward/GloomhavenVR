using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class NativeCoverage
{
    internal static int Run(Shader shader)
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-flameEvidence");
        if (at < 0) throw new Exception("Native flame evidence path is required");
        string dir = args[at + 1];
        var cameraRoot = new GameObject("Native atlas coverage camera");
        var camera = cameraRoot.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -2); camera.orthographic = true; camera.orthographicSize = .5f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.allowHDR = false;
        camera.backgroundColor = new Color(.35f, .35f, .35f, 1);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var target = new RenderTexture(128, 128, 24);
        var pixels = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        var material = new Material(shader); quad.GetComponent<MeshRenderer>().sharedMaterial = material;
        int checks = 0;
        try
        {
            foreach (string name in new[] { "CandleAnim", "CR_CandleFlame_01" })
            {
                var texture = new Texture2D(2, 2); texture.LoadImage(File.ReadAllBytes(Path.Combine(dir, name + ".png")));
                texture.filterMode = FilterMode.Bilinear;
                try
                {
                    material.mainTexture = texture; material.color = new Color(.5f, .5f, .5f, .5f);
                    material.SetFloat("_FlameCore", 1); material.SetFloat("_TownVisibility", 1);
                    bool atlas = name == "CandleAnim"; int count = atlas ? 64 : 1;
                    material.SetFloat("_Toggle_Flipbook", atlas ? 1 : 0);
                    material.SetFloat("_FlipbookTileX", atlas ? 8 : 1); material.SetFloat("_FlipbookTileY", atlas ? 8 : 1);
                    material.SetFloat("_FlipbookSpeed", 1);
                    for (int frame = 0; frame < count; frame++)
                    {
                        material.SetFloat("_TownAnimationTime", frame / 64f);
                        material.shader = shader; int good = RenderAndCountBlackPadding(false, texture, atlas, frame);
                        if (good < 20) throw new Exception("Native opaque black padding does not remain transparent: " + name + "/" + frame);
                        material.shader = Shader.Find("GloomhavenVR/TownFlameOpaquePaddingNegative");
                        if (RenderAndCountBlackPadding(true, texture, atlas, frame) < 20)
                            throw new Exception("Original build551 opaque padding regression was not detected");
                        checks += 2;
                    }
                }
                finally { Object.DestroyImmediate(texture); }
            }
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = null;
            Object.DestroyImmediate(cameraRoot); Object.DestroyImmediate(quad); Object.DestroyImmediate(material);
            Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
        }
        return checks;

        int RenderAndCountBlackPadding(bool negative, Texture2D texture, bool atlas, int frame)
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); pixels.Apply();
            int count = 0;
            for (int y = 4; y < 124; y += 3) for (int x = 4; x < 124; x += 3)
            {
                float u = (x + .5f) / 128f, v = (y + .5f) / 128f;
                if (atlas) { u = (u + frame % 8) / 8; v = (v + 7 - frame / 8) / 8; }
                Color texel = texture.GetPixelBilinear(u, v);
                if (texel.r > .001f || texel.g > .001f || texel.b > .001f || texel.a < .99f) continue;
                Color actual = pixels.GetPixel(x, y);
                if (negative ? Mathf.Max(actual.r, Mathf.Max(actual.g, actual.b)) < .1f : actual.r > .32f && actual.g > .32f && actual.b > .32f) count++;
            }
            return count;
        }
    }
}
