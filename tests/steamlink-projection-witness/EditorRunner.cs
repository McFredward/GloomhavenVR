using System;
using System.IO;
using GloomhavenVR.Core.SteamLink;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Actual current pack shader + output class. No XR, OpenVR or Frame access.
public static class ProjectionWitnessRunner
{
    private static int checks;
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    private static Color Pixel(Texture2D image, int x, int y) => image.GetPixel(x, image.height - 1 - y);
    private static void Zero(Color c, string why) => Check(c.r == 0 && c.g == 0 && c.b == 0 && c.a == 1, why);
    public static void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "-witnessOutput") + 1];
        RenderTexture zero = null, target = null; Texture2D read = null; SceneEyeOutput pack = null;
        RenderTexture previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            Check(Application.unityVersion.StartsWith("2021.3.5"), "Reviewed Unity compiler");
            Check(QualitySettings.activeColorSpace == ColorSpace.Gamma, "Gamma source scope");
            Shader shader = Shader.Find(SceneAtlasProfile.RobustShaderName);
            Check(shader != null && shader.isSupported, "Actual current v3 shader is supported");
            zero = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { useMipMap = false, antiAliasing = 1, filterMode = FilterMode.Point };
            target = new RenderTexture(3168, 3168, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { useMipMap = false, antiAliasing = 1 };
            Check(zero.Create() && target.Create(), "Owned zero input and complete carrier created");
            RenderTexture.active = zero; GL.sRGBWrite = false; GL.Clear(false, true, Color.clear);
            pack = new SceneEyeOutput(shader);
            read = new Texture2D(3168, 3168, TextureFormat.RGBA32, false, true);
            foreach (var eye in new[] { Camera.MonoOrStereoscopicEye.Left, Camera.MonoOrStereoscopicEye.Right })
            {
                Check(pack.TryRender(zero, target, eye) == SceneEyeResult.Rendered, "Constant-zero input packs for " + eye);
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 3168, 3168), 0, 0, false); read.Apply(false, false);
                int[] codes = { 32,224,224,96,96,160,96,224,160,224,96,96 };
                for (int n = 0; n < 12; n++)
                {
                    int x = (n < 6 ? 48 : 2672) + (n % 3) * 160;
                    int y = (n < 6 ? 64 : 2704) + (n % 6 / 3) * 160;
                    foreach (int d in new[] { 8, 64, 119 })
                    {
                        Color c = Pixel(read, x + d, y + d);
                        Check(Mathf.Abs(c.g * 255 - codes[n]) < 0.1f && c.r == c.g && c.b == c.g && c.a == 1,
                            "Unchanged v3 format cell " + n + " / " + eye);
                    }
                }
                int[,] origins = { {576,32},{2624,576},{544,2624},{32,544} };
                int[,] sizes = { {2048,512},{512,2048},{2048,512},{512,2048} };
                for (int tile = 0; tile < 4; tile++)
                    foreach (float u in new[] { .01f,.25f,.5f,.75f,.99f })
                        foreach (float v in new[] { .01f,.5f,.99f })
                            Zero(Pixel(read, origins[tile,0] + (int)(u*sizes[tile,0]), origins[tile,1] + (int)(v*sizes[tile,1])),
                                "Independent decoded mask remains exactly zero / tile " + tile);
                foreach (float u in new[] { .01f,.25f,.4f,.5f,.6f,.75f,.99f })
                    foreach (float v in new[] { .01f,.5f,.99f })
                        Zero(Pixel(read, 560 + (int)(2048*u), 560 + (int)(2048*v)), "Contribution is exactly zero");

                // The entire intended overlay footprint is strictly inside RGB,
                // with >700 carrier-pixel margin from every coverage strip.
                float min = .4f*3168, max = .6f*3168;
                Check(min > 560 + 700 && max < 2608 - 700, "Conservative unwarped overlay footprint remains wholly inside RGB");
                Color actualMask = Pixel(read, 576 + 1024, 32 + 256);
                Color actualRgb = Pixel(read, 1584, 1584);
                float camera = .7f;
                foreach (float alpha in new[] { 0f,64f/255,128f/255,1f })
                {
                    // Simulate PC flattening a black source into RGB only. Its
                    // E is still zero; untouched mask G is still zero. Thus no
                    // black lane can occlude a bright camera through this path.
                    float flattenedE = 0*alpha + actualRgb.r*(1-alpha);
                    float flattened = flattenedE + camera*(1-actualMask.g);
                    float separateRgba = camera*(1-alpha);
                    Check(Mathf.Abs(flattened-camera) < 1e-6f, "Flattened black cannot occlude the real camera");
                    if (alpha > 0) Check(separateRgba < flattened, "Independent actual alpha has distinguishable black occlusion");
                }
                File.WriteAllBytes(Path.Combine(output,"zero-carrier-" + eye + ".png"), read.EncodeToPNG());
            }
            Check(pack.TryRender(zero, target, Camera.MonoOrStereoscopicEye.Mono) == SceneEyeResult.UnknownEye,
                "Unlabelled eye does not publish a carrier");
            File.WriteAllText(Path.Combine(output,"gpu-result.json"), "{\"passed\":true,\"checks\":" + checks
                + ",\"actual_unity_pack\":true,\"zero_source_size\":1,\"carrier_size\":3168,"
                + "\"xr_executed\":false,\"openvr_executed\":false,\"native_transport_proven\":false,"
                + "\"pc_warp_bound_proven\":false,\"full_scene_quality_proven\":false}\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output,"failure.txt"), error + "\n"); Debug.LogException(error); EditorApplication.Exit(1);
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            pack?.Dispose();
            foreach (var rt in new[] { zero, target }) if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (read != null) Object.DestroyImmediate(read);
        }
    }
}
