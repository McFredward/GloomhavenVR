// The Editor has no headset provider. Only the native viewport query is injected;
// the complete production Prepare/Draw/restore command-buffer path executes below.
using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class ViewportFixture
{
    static int checks;
    static Rect providerViewport;
    static RenderTextureDescriptor providerTarget;
    static readonly Type Production = typeof(HealthbarForegroundDepth);
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    public static Rect ProviderViewport(ref int width, ref int height)
    {
        object[] args = { providerViewport, providerTarget, width, height };
        Rect result = (Rect)Production.GetMethod("XrViewportPixels", PrivateStatic)!.Invoke(null, args)!;
        width = (int)args[2]; height = (int)args[3];
        return result;
    }

    static void Check(bool ok, string why)
    {
        checks++;
        if (!ok) throw new InvalidOperationException(why);
    }

    static bool Same(Rect actual, Rect expected) =>
        Mathf.Abs(actual.x - expected.x) < .001f && Mathf.Abs(actual.y - expected.y) < .001f
        && Mathf.Abs(actual.width - expected.width) < .001f && Mathf.Abs(actual.height - expected.height) < .001f;

    static Mesh Quad(float left, float right)
    {
        var mesh = new Mesh { name = "Viewport witness" };
        mesh.vertices = new[] { new Vector3(left, -1), new Vector3(right, -1), new Vector3(right, 1), new Vector3(left, 1) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    static GameObject Surface(string name, Mesh mesh, Material material, int layer)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.layer = layer;
        go.transform.position = new Vector3(0, 0, 2);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    static Color32[] Read(RenderTexture target, string path)
    {
        RenderTexture before = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            return texture.GetPixels32();
        }
        finally { RenderTexture.active = before; Object.DestroyImmediate(texture); }
    }

    static Color32 Pixel(Color32[] values, int width, float x, float y) => values[(int)y * width + (int)x];
    static bool Green(Color32 pixel) => pixel.g > 200 && pixel.r < 20 && pixel.b < 20;
    static bool Red(Color32 pixel) => pixel.r > 200 && pixel.g < 20 && pixel.b < 20;
    static bool Black(Color32 pixel) => pixel.r < 20 && pixel.g < 20 && pixel.b < 20;

    public static int Run(string evidence)
    {
        checks = 0;
        Shader shader = Resources.Load<Shader>("ViewportWitness");
        Check(shader != null && shader.isSupported, "GPU viewport witness shader is supported");
        var depthMaterial = new Material(shader) { renderQueue = 2000 };
        depthMaterial.SetFloat("_DepthOnly", 1);
        var witnessMaterial = new Material(shader) { renderQueue = 3000 };
        Mesh foregroundMesh = Quad(-1, 0), witnessMesh = Quad(-1, 1);
        GameObject foreground = Surface("GloomhavenVR.Hands", foregroundMesh, depthMaterial, 31);
        GameObject witness = Surface("Viewport colour witness", witnessMesh, witnessMaterial, 30);
        witness.SetActive(false);
        var cameraGo = new GameObject("Viewport fixture camera");
        Camera camera = cameraGo.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 2;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.cullingMask = (1 << 31) | (1 << 30);
        camera.allowHDR = false; camera.allowMSAA = false;
        camera.nearClipPlane = .1f; camera.farClipPlane = 10;
        try
        {
            using (var report = new StreamWriter(Path.Combine(evidence, "viewport-pixels.txt")))
            {
                // Different per-eye allocations deliberately disagree with global
                // XRSettings dimensions and exercise non-square offset subrects.
                foreach (var row in new[] {
                    (name: "left", width: 160, height: 96, normalized: new Rect(.125f, .25f, .5f, .5f), expected: new Rect(20, 24, 80, 48)),
                    (name: "right", width: 224, height: 144, normalized: new Rect(.25f, .125f, .5f, .625f), expected: new Rect(56, 18, 112, 90)) })
                {
                    providerViewport = row.normalized;
                    providerTarget = new RenderTextureDescriptor(row.width, row.height, RenderTextureFormat.ARGB32, 24) { msaaSamples = 1 };
                    int width = 991, height = 773;
                    Rect resolved = ProviderViewport(ref width, ref height);
                    Check(width == row.width && height == row.height, "each MultiPass eye uses its own pass allocation");
                    var target = new RenderTexture(providerTarget);
                    target.Create(); camera.targetTexture = target;
                    // Native targetTexture cameras still ignore their authored
                    // rect: directly check that unmodified production branch.
                    camera.rect = new Rect(.4f, .3f, .2f, .1f);
                    object[] nativeArgs = { camera, row.width, row.height };
                    Rect native = (Rect)Production.GetMethod("Viewport", PrivateStatic)!.Invoke(null, nativeArgs)!;
                    Check(Same(native, new Rect(0, 0, row.width, row.height)), "native target-texture path retains full pixel viewport");
                    camera.rect = new Rect(0, 0, 1, 1);
                    try
                    {
                        HealthbarForegroundDepth.Register(foreground.transform);
                        HealthbarForegroundDepth.Prepare(camera);
                        // A new native renderer batch may establish its own state.
                        // Append the witness to the actual production buffer so
                        // it observes precisely the target/viewport just restored.
                        CommandBuffer[] capture = camera.GetCommandBuffers(CameraEvent.BeforeForwardAlpha);
                        Check(capture.Length == 1, "exactly one production foreground capture is attached");
                        capture[0].DrawMesh(witnessMesh, Matrix4x4.identity, witnessMaterial, 0, 0);
                        camera.Render();
                        Color32[] pixels = Read(target, Path.Combine(evidence, row.name + ".png"));
                        Rect expected = row.expected;
                        Color32 near = Pixel(pixels, row.width, expected.x + expected.width * .25f, expected.y + expected.height * .5f);
                        Color32 far = Pixel(pixels, row.width, expected.x + expected.width * .65f, expected.y + expected.height * .5f);
                        Color32 lowOutside = Pixel(pixels, row.width, 1, 1), highOutside = Pixel(pixels, row.width, row.width - 2, row.height - 2);
                        report.WriteLine(row.name + "; target=" + width + "x" + height + "; normalized=" + providerViewport
                            + "; resolved=" + resolved + "; expected=" + expected + "; near=" + near + "; far=" + far
                            + "; outside=" + lowOutside + "/" + highOutside);
                        report.Flush();
                        Check(Green(near), "production depth capture occupies the expected provider pixel subrect");
                        Check(Red(far), "production depth capture keeps the uncovered half outside the foreground silhouette");
                        Check(Black(lowOutside) && Black(highOutside), "production restoration confines following colour draws to the provider pixel subrect");
                        Check(Same(resolved, expected), "normalized provider coordinates convert to exact pixel offset and extent");
                        Vector4 size = Shader.GetGlobalVector("_GHVRActorForegroundSize");
                        Check(size.z == row.width && size.w == row.height, "depth allocation and sampling size match the current eye pass target");
                    }
                    finally
                    {
                        Production.GetMethod("Detach", PrivateStatic)!.Invoke(null, null);
                        camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
                    }
                }
                camera.pixelRect = new Rect(13, 9, 77, 53);
                object[] ordinaryArgs = { camera, 640, 480 };
                Rect ordinary = (Rect)Production.GetMethod("Viewport", PrivateStatic)!.Invoke(null, ordinaryArgs)!;
                Check(Same(ordinary, camera.pixelRect), "ordinary non-XR backbuffer camera retains its pixel viewport without normalization");
            }
        }
        finally
        {
            Production.GetMethod("Detach", PrivateStatic)!.Invoke(null, null);
            foreach (Object obj in new Object[] { cameraGo, foreground, witness, foregroundMesh, witnessMesh, depthMaterial, witnessMaterial })
                Object.DestroyImmediate(obj);
        }
        return checks;
    }
}
