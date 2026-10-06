using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Quest.Editor;
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
    private static void AtlasCopy()
    {
        var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        var pixels = new Color[256];
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            pixels[y * 16 + x] = x >= 8 && y >= 8
                ? new Color(x < 12 ? 1 : .5f, y < 12 ? .25f : .75f, .5f, x < 12 ? .25f : .75f) : Color.blue;
        texture.SetPixels(pixels); texture.Apply(false);
        var target = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create();
        var roundtrip = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        roundtrip.Create();
        RenderTexture previousTarget = RenderTexture.active;
        bool previousColor = GL.sRGBWrite;
        try
        {
            QuestStandalonePlatform.Enabled = false;
            Check(QuestTextureCopy.AcceptCache(null) && QuestTextureCopy.AcceptCache("desktop-v2"), "desktop cache compatibility preserved");
            QuestStandalonePlatform.Enabled = true;
            Check(!QuestTextureCopy.AcceptCache(null) && !QuestTextureCopy.AcceptCache("old-builtin-copy"), "Quest rejects previous builtin-copy cache");
            Check(QuestTextureCopy.AcceptCache(QuestTextureCopy.Recipe), "Quest accepts its exact copy recipe");
            // Copies execute with XR keywords present but consume an ordinary atlas.
            Shader.EnableKeyword("STEREO_INSTANCING_ON");
            GL.sRGBWrite = !target.sRGB;
            QuestTextureCopy.Copy(texture, target, new Vector2(.5f, .5f), new Vector2(.5f, .5f));
            Check(GL.sRGBWrite == !target.sRGB && RenderTexture.active == previousTarget, "Quest copy restores render state");
            Color[] copied = Pixels(target);
            Debug.Log("Quest ordinary atlas copy: api=" + SystemInfo.graphicsDeviceType + " first=" + copied[0]
                + " last=" + copied[copied.Length - 1] + " srgb=" + target.sRGB + " activeColor=" + QualitySettings.activeColorSpace);
            for (int index = 0; index < copied.Length; index++)
            {
                Color pixel = copied[index]; int x = index % 8, y = index / 8;
                Check(Math.Abs(pixel.r - (x < 4 ? 1 : .5f)) < .02f && Math.Abs(pixel.g - (y < 4 ? .25f : .75f)) < .02f
                    && Math.Abs(pixel.b - .5f) < .02f && Math.Abs(pixel.a - (x < 4 ? .25f : .75f)) < .02f,
                    "Quest atlas copy retains crop pixels and alpha");
            }
            // The owned sprite copy is itself an ordinary RT. Diagnostics and
            // subsequent copies must sample it without assuming an eye array.
            QuestTextureCopy.Copy(target, roundtrip, Vector2.one, Vector2.zero);
            Color[] retained = Pixels(roundtrip);
            for (int index = 0; index < retained.Length; index++)
                Check(Math.Abs(retained[index].r - copied[index].r) < .02f && Math.Abs(retained[index].g - copied[index].g) < .02f
                    && Math.Abs(retained[index].a - copied[index].a) < .02f, "Quest RT copy retains actual source pixels and alpha");
            try { QuestTextureCopy.Copy(target, target, Vector2.one, Vector2.zero); throw new Exception("self copy accepted"); }
            catch (ArgumentException) { checks++; }
        }
        finally
        {
            Shader.DisableKeyword("STEREO_INSTANCING_ON"); GL.sRGBWrite = previousColor; RenderTexture.active = previousTarget;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(texture);
            roundtrip.Release(); UnityEngine.Object.DestroyImmediate(roundtrip);
        }
    }
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
    private static void ImportedGate()
    {
        QuestSpriteGeometryValidation.ValidateStartupAssets();
        string path = QuestSpriteGeometryValidation.InputPath, original = File.ReadAllText(path);
        var receipt = JsonUtility.FromJson<QuestSpriteGeometryValidation.SourceReceipt>(original);
        receipt.assets[0].restoredAtlasRect.width += 1;
        bool rejected = false;
        try
        {
            File.WriteAllText(path, JsonUtility.ToJson(receipt));
            try { QuestSpriteGeometryValidation.ValidateStartupAssets(); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("geometry differs"); }
        }
        finally { File.WriteAllText(path, original); }
        Check(rejected, "native imported geometry gate rejects altered receipt");
    }
    private static double ArtVariance(Color[] pixels)
    {
        double sum = 0, square = 0; int count = 0;
        for (int y = 192; y < 320; y++) for (int x = 128; x < 384; x++)
        { float value = pixels[y * 512 + x].r; sum += value; square += value * value; count++; }
        return square / count - Math.Pow(sum / count, 2);
    }
    private static void PromotionPixels(Sprite art, Camera camera, RenderTexture target)
    {
        var canvas = new GameObject("native-promotion-canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.gameObject.layer = 30; canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        canvas.transform.position = new Vector3(0, 0, 1); canvas.transform.localScale = Vector3.one / 480;
        var background = Image(art, new Vector2(480, 234), new Vector2(.5f, .5f), false);
        background.sprite = null; background.color = new Color(.65f, .5f, .3f, 1);
        background.transform.SetParent(canvas.transform, false); background.gameObject.layer = 30;
        var image = Image(art, new Vector2(480, 234), new Vector2(.5f, .5f), false);
        image.transform.SetParent(canvas.transform, false); image.gameObject.layer = 30;
        var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/Narrative dissolve material.mat");
        Check(original != null && original.shader != null, "native DLC promotion material imports");
        var material = new Material(original!); image.material = material;
        camera.targetTexture = target; camera.cullingMask = 1 << 30; camera.backgroundColor = Color.black;
        Canvas.ForceUpdateCanvases(); camera.Render();
        double originalVariance = ArtVariance(Pixels(target));
        material.renderQueue = 3000; image.SetMaterialDirty();
        Canvas.ForceUpdateCanvases(); camera.Render();
        double transparentQueueVariance = ArtVariance(Pixels(target));
        Debug.Log("DLC original material queue pixel diagnostic: sprite=" + art.name + " originalQueue=" + original!.renderQueue
            + " originalVariance=" + originalVariance + " transparentQueueVariance=" + transparentQueueVariance);
        Check(transparentQueueVariance > .004, "owned DLC art rasterizes with an ordered native UI queue");
        UnityEngine.Object.DestroyImmediate(canvas.gameObject); UnityEngine.Object.DestroyImmediate(material);
    }
    public static int Run()
    {
        checks = 0;
        AtlasCopy();
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
            ImportedGate();
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
                go.SetActive(false);
                PromotionPixels(art, camera, quest);
            }
        }
        foreach (RenderTexture rt in new[] { desktop, quest, smallDesktop, smallQuest })
        { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(screen);
        UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(sprite);
        return checks;
    }
}
