using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled; }
}

public static class InteractionProgram
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    private static void Close(float left, float right, string message) => Check(Math.Abs(left - right) < 0.00003f, message);
    private static Color[] Pixels(RenderTexture source)
    {
        RenderTexture previous = RenderTexture.active;
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
        try
        {
            RenderTexture.active = source;
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            copy.Apply(false, false);
            return copy.GetPixels();
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
    }
    private static double Variance(Color[] pixels)
    {
        double sum = 0, square = 0;
        foreach (Color pixel in pixels) { sum += pixel.r; square += pixel.r * pixel.r; }
        return square / pixels.Length - Math.Pow(sum / pixels.Length, 2);
    }
    private static RenderTexture Target(bool quest, int size)
    {
        QuestStandalonePlatform.Enabled = quest;
        RenderTexture rt = FlatScreenStereo.CreateColorRt(size, size, 24, "fixture-menu");
        Check(rt.Create(), "capture allocation");
        return rt;
    }
    private static Image Image(Sprite sprite, Vector2 dimensions, Vector2 pivot, bool preserve)
    {
        var go = new GameObject("authored-loading-image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Image image = go.GetComponent<Image>();
        image.sprite = sprite; image.preserveAspect = preserve;
        image.rectTransform.sizeDelta = dimensions; image.rectTransform.pivot = pivot;
        return image;
    }
    private static void CompareImage(Image image, Image basis)
    {
        MethodInfo populate = typeof(Image).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(VertexHelper) }, null)!;
        using (var helper = new VertexHelper())
        {
            populate.Invoke(image, new object[] { helper });
            var original = new Mesh(); helper.FillMesh(original);
            Rect actual = LoadingIconGeometry.DrawingRect(image);
            Vector3[] vertices = original.vertices;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector3 vertex in vertices)
            {
                minX = Math.Min(minX, vertex.x); minY = Math.Min(minY, vertex.y);
                maxX = Math.Max(maxX, vertex.x); maxY = Math.Max(maxY, vertex.y);
            }
            Close(actual.xMin, minX, "native Image trim and preserveAspect drawing bounds");
            Close(actual.xMax, maxX, "native Image trim and preserveAspect drawing bounds");
            Close(actual.yMin, minY, "native Image trim and preserveAspect drawing bounds");
            Close(actual.yMax, maxY, "native Image trim and preserveAspect drawing bounds");
            var indicator = new LoadingIndicator();
            GameObject quad = indicator.Build(image, basis);
            Vector3[] converted = quad.GetComponent<MeshFilter>().sharedMesh.vertices;
            float units = Mathf.Max(basis.rectTransform.rect.width, basis.rectTransform.rect.height);
            Vector2 centre = basis.rectTransform.rect.center;
            foreach (Vector3 vertex in vertices)
            {
                Vector3 local = basis.rectTransform.InverseTransformPoint(image.rectTransform.TransformPoint(vertex));
                Vector3 expected = (local - new Vector3(centre.x, centre.y, 0)) * (.25f / units);
                bool found = false;
                foreach (Vector3 point in converted)
                {
                    Vector3 drawn = quad.transform.TransformPoint(point);
                    if ((drawn - expected).sqrMagnitude < 0.000000001f) found = true;
                }
                Check(found, "converted spinner quad preserves original trim scale and pivot");
            }
            UnityEngine.Object.DestroyImmediate(original);
        }
    }
    public static int Run()
    {
        checks = 0;
        RenderTexture desktop = Target(false, 512), quest = Target(true, 512);
        Check(!desktop.useMipMap && desktop.filterMode == FilterMode.Bilinear, "desktop capture defaults preserved");
        Check(quest.useMipMap && quest.mipmapCount > 1, "Quest capture has a mip chain");
        Check(quest.autoGenerateMips, "Quest animated captures regenerate their mip chain");
        Check(quest.filterMode == FilterMode.Trilinear && quest.anisoLevel == 8 && quest.wrapMode == TextureWrapMode.Clamp,
            "Quest capture uses trilinear clamped anisotropic sampling");
        Check(quest.antiAliasing == 1 && quest.width == desktop.width && quest.height == desktop.height,
            "capture resolution and MSAA remain unchanged");
        var texture = new Texture2D(512, 512, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point };
        var samples = new Color[512 * 512];
        for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
            samples[y * 512 + x] = x % 5 == 0 ? Color.white : Color.black;
        texture.SetPixels(samples); texture.Apply(false);
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.layer = 30; go.transform.position = new Vector3(0, 0, 1);
        var material = new Material(Shader.Find("Unlit/Texture")) { mainTexture = texture };
        go.GetComponent<Renderer>().sharedMaterial = material;
        var camera = new GameObject("fixture-native-UI-camera").AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .5f;
        camera.nearClipPlane = .1f; camera.farClipPlane = 3; camera.cullingMask = 1 << 30;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.targetTexture = desktop; camera.Render(); camera.targetTexture = quest; camera.Render();
        var smallDesktop = new RenderTexture(49, 49, 0); smallDesktop.Create();
        var smallQuest = new RenderTexture(49, 49, 0); smallQuest.Create();
        // This is the same built-in shader the ordinary floating screen samples.
        var screen = new Material(Shader.Find("Hidden/BlitCopy"));
        Graphics.Blit(desktop, smallDesktop, screen); Graphics.Blit(quest, smallQuest, screen);
        double raw = Variance(Pixels(smallDesktop)), filtered = Variance(Pixels(smallQuest));
        Check(raw > .02 && filtered < raw * .15, "actual minified screen pixels reject level-zero stroke aliasing");
        Debug.Log("Quest menu pixel proof: rawVariance=" + raw + " filteredVariance=" + filtered);
        // A later native render, with no manual GenerateMips call, must update the
        // filtered levels and retain the original camera's translucent clear alpha.
        camera.cullingMask = 0; camera.backgroundColor = new Color(1, .1f, .2f, .25f); camera.Render();
        Graphics.Blit(quest, smallQuest, screen);
        foreach (Color pixel in Pixels(smallQuest))
            Check(pixel.r > .97f && Math.Abs(pixel.a - .25f) < .02f, "animated mip output refreshes with original alpha");

        var sprite = Sprite.Create(texture, new Rect(0, 0, 128, 64), new Vector2(.5f, .5f));
        Image basis = Image(sprite, new Vector2(64, 64), new Vector2(.5f, .5f), true);
        CompareImage(basis, basis);
        CompareImage(Image(sprite, new Vector2(64, 96), new Vector2(.2f, .8f), true), basis);
        CompareImage(Image(sprite, new Vector2(64, 96), new Vector2(.2f, .8f), false), basis);
        string ownedBase = "Assets/Sprite/LoadingBase.asset";
        if (File.Exists(ownedBase))
        {
            Sprite nativeBase = AssetDatabase.LoadAssetAtPath<Sprite>(ownedBase);
            Sprite overlay = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/LoadingOverlay.asset");
            Check(nativeBase != null && overlay != null, "owned spinner assets import");
            Close(nativeBase!.rect.width, 128, "owned original full sprite width restored");
            Vector4 padding = DataUtility.GetPadding(nativeBase);
            Check(padding.x > 7 && padding.y > 7, "owned native Image observes original sprite trim padding");
            Image nativeImage = Image(nativeBase, new Vector2(64, 64), new Vector2(.5f, .5f), true);
            CompareImage(nativeImage, nativeImage);
            CompareImage(Image(overlay!, new Vector2(64, 64), new Vector2(.5f, .5f), true), nativeImage);
            foreach (string name in new[] { "DLC_Promo_JawsOfTheLion", "DLC_Promo_SoloScenarios_0" })
            {
                Sprite art = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprite/" + name + ".asset");
                Check(art != null && art.texture != null, "owned DLC image native texture binding");
                Vector4 uv = DataUtility.GetOuterUV(art!);
                Check(uv.z > uv.x && uv.w > uv.y, "owned DLC image native outer UV is nondegenerate");
                Debug.Log("DLC native sprite proof: " + name + " rect=" + art!.rect + " crop=" + art.textureRect + " UV=" + uv);
            }
        }
        foreach (RenderTexture rt in new[] { desktop, quest, smallDesktop, smallQuest })
        { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(screen);
        UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(sprite);
        return checks;
    }
}
