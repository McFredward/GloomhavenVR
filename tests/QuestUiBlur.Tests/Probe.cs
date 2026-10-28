using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>Real CanvasRenderer/GrabPass pixels; no native visibility adaptation.</summary>
public static class QuestUiBlurProbe
{
    [Serializable] public sealed class Sample
    {
        public string name;
        public float maximumError, meanError, maximumDifference;
        public float centerR, centerG, centerB, centerA;
        public bool culled, passed;
    }
    [Serializable] public sealed class Receipt
    {
        public int schema = 1, cases;
        public string unityVersion, graphicsDevice;
        public bool actualCanvasRenderer, originalPixelParityVerified;
        public Sample[] samples;
        public string importedNormalFormat, androidNormalFormat, compressedNormalFormat;
        public Vector4 importedNormalGpuChannels, compressedNormalGpuChannels;
    }
    private static readonly List<Sample> Samples = new List<Sample>();
    private static Camera camera;
    private static RenderTexture target;
    private static Image blur;
    private static CanvasGroup group;
    private static Material material;
    private const int Side = 128;

    public static void Run()
    {
        try
        {
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            ShaderUtil.allowAsyncCompilation = false;
            QuestBlurValidation.Validate(false);
            Directory.CreateDirectory("ProbeOutput");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shader/Custom_SimpleGrabPassBlur.shader");
            material = new Material(shader);
            material.SetFloat("_Size", 1);
            material.SetFloat("_BumpAmt", 0);
            material.SetColor("_Color", Color.white);
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
            var scene = new GameObject("Actual original UI Canvas fixture");
            var cameraObject = new GameObject("Framebuffer producer", typeof(Camera));
            camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = .5f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            target = new RenderTexture(Side, Side, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create(); camera.targetTexture = target;
            var canvasObject = new GameObject("Native-style World Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(scene.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.GetComponent<RectTransform>().sizeDelta = Vector2.one * Side;
            canvas.transform.localScale = Vector3.one / Side;
            var background = new GameObject("Asymmetric original framebuffer", typeof(RectTransform), typeof(RawImage));
            background.transform.SetParent(canvas.transform, false);
            SetRect(background.GetComponent<RectTransform>(), Side);
            var texture = new Texture2D(Side, Side, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[Side * Side];
            for (int y = 0; y < Side; y++) for (int x = 0; x < Side; x++)
                pixels[y * Side + x] = new Color((x % 11) < 4 ? .8f : .1f, (y % 13) < 6 ? .7f : .15f,
                    .1f + .6f * (x + 2f * y) / (3f * Side), 1);
            texture.SetPixels(pixels); texture.Apply();
            background.GetComponent<RawImage>().texture = texture;
            var image = new GameObject("UI_Blur original states", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            image.transform.SetParent(canvas.transform, false);
            SetRect(image.GetComponent<RectTransform>(), Side * .75f);
            blur = image.GetComponent<Image>(); blur.material = material;
            group = image.GetComponent<CanvasGroup>();
            blur.enabled = false;
            Color[] baseline = Render("baseline");
            blur.enabled = true;
            var expected = Reference(baseline);
            SamplePixels("visible-original-three-pair-blur", Render("visible"), expected, baseline);
            group.alpha = 0;
            blur.canvasRenderer.cullTransparentMesh = false;
            // The engine suppresses inherited-alpha-zero Canvas draws even
            // when the public cull flag remains false. Do not "fix" this by
            // changing the native visibility or adding a COLOR input.
            SamplePixels("zero-inherited-alpha-original-cull-false", Render("zero-alpha"), baseline, expected);
            group.alpha = 1;
            blur.color = new Color(1, 1, 1, 0);
            SamplePixels("zero-vertex-alpha-original-no-COLOR", Render("zero-vertex"), expected, baseline);
            blur.color = Color.white;
            blur.canvasRenderer.cullTransparentMesh = true;
            group.alpha = 0;
            SamplePixels("zero-alpha-cull-true-control", Render("zero-alpha-culled"), baseline, expected);
            group.alpha = 1;
            blur.canvasRenderer.cullTransparentMesh = false;
            material.SetFloat("_BumpAmt", 4);
            var rgb = Solid(new Color(.75f, .25f, .85f, 1));
            var ag = Solid(new Color(1, .25f, .85f, .75f));
            Color sampleControl = SampleImportedNormal(rgb);
            if (Mathf.Abs(sampleControl.r - .75f) > .006f || Mathf.Abs(sampleControl.g - .25f) > .006f ||
                Mathf.Abs(sampleControl.a - 1) > .006f)
                throw new InvalidOperationException("Actual normal-channel sample shader failed its raw RGBA control: " + sampleControl);
            var distorted = DistortionReference(expected, new Vector2(.5f, -.5f), Color.white);
            material.SetTexture("_BumpMap", rgb);
            Color[] rgbPixels = Render("normal-rgb");
            SamplePixels("original-rgb-normal-distortion", rgbPixels, distorted, expected, .025f);
            // The original D3D bank accepts AG too; this raw texture is not an
            // Android RGB-imported normal. Select that audited native branch
            // in a fixture-only shader rather than changing the production API.
            material.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/native-d3d-normal-control.shader");
            blur.SetMaterialDirty();
            material.SetFloat("_BumpAmt", 4);
            material.SetFloat("_Size", 1);
            material.SetColor("_Color", Color.white);
            material.SetTexture("_BumpMap", ag);
            Color[] agPixels = Render("normal-ag");
            SamplePixels("original-D3D-AG-normal-channel-contract", agPixels, rgbPixels, expected, .006f);
            material.shader = shader;
            blur.SetMaterialDirty();
            material.SetFloat("_BumpAmt", 4);
            material.SetFloat("_Size", 1);
            material.SetColor("_Color", Color.white);

            // Import a true tangent-space normal, rather than assuming that
            // a supplied Texture2D's channels represent the Android importer.
            string normalPath = "Assets/normal-import-control.png";
            File.WriteAllBytes(normalPath, rgb.EncodeToPNG());
            AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name = "Android", overridden = true, format = TextureImporterFormat.RGBA32, maxTextureSize = 32
            });
            importer.SaveAndReimport();
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            Color encoded = SampleImportedNormal(imported);
            Vector2 actualNormal = new Vector2(encoded.r * encoded.a, encoded.g) * 2 - Vector2.one;
            material.SetTexture("_BumpMap", imported);
            SamplePixels("actual-imported-RGBA32-normal-channel-contract", Render("normal-imported"),
                DistortionReference(expected, actualNormal, Color.white), expected, .025f);
            string rgbaFormat = imported.format.ToString();
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                name = "Android", overridden = true, format = TextureImporterFormat.ASTC_6x6, maxTextureSize = 32
            });
            importer.SaveAndReimport();
            imported = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            Color compressedEncoded = SampleImportedNormal(imported);
            Vector2 compressedNormal = new Vector2(compressedEncoded.a, compressedEncoded.g) * 2 - Vector2.one;
            Debug.Log("Actual ASTC normal channels=" + compressedEncoded + " unpacked=" + compressedNormal + " textureFormat=" + imported.format);
            if ((compressedNormal - new Vector2(.5f, -.5f)).sqrMagnitude > .02f)
                throw new InvalidOperationException("Actual Android ASTC import changed native normal channel contract");
            material.SetTexture("_BumpMap", imported);
            material.EnableKeyword("UNITY_ASTC_NORMALMAP_ENCODING");
            SamplePixels("actual-Android-ASTC-normal-native-channel-contract", Render("normal-imported-astc"),
                DistortionReference(expected, new Vector2(compressedEncoded.a, compressedEncoded.g) * 2 - Vector2.one, Color.white), expected, .025f);

            Color tint = new Color(.8f, .6f, .9f, .7f), main = new Color(.5f, .7f, .8f, .6f);
            material.SetColor("_Color", tint);
            // Image's native CanvasRenderer supplies _MainTex from its sprite;
            // setting only a material texture would test the wrong producer.
            blur.sprite = Sprite.Create(Solid(main), new Rect(0, 0, 8, 8), Vector2.one * .5f);
            SamplePixels("native-main-texture-and-tint-product-alpha", Render("native-tint"),
                DistortionReference(expected, new Vector2(compressedEncoded.a, compressedEncoded.g) * 2 - Vector2.one, tint * main), baseline, .025f);
            // Real shader mutations must compile, draw and fail the pixel
            // expectation. Compiler errors never count as a negative success.
            blur.sprite = null;
            material.DisableKeyword("UNITY_ASTC_NORMALMAP_ENCODING");
            material.SetColor("_Color", Color.white);
            foreach (string mutation in new[] { "vertical-axis-lost", "normal-AG-channel-lost", "homogeneous-Z-lost" })
            {
                material.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/" + mutation + ".shader");
                if (material.shader == null) throw new InvalidOperationException("Missing actual pixel defect shader");
                for (int i = 1; i < 6; i += 2) ShaderUtil.CompilePass(material, i, true);
                if (ShaderUtil.ShaderHasError(material.shader)) throw new InvalidOperationException("Defect shader failed compilation");
                material.SetColor("_Color", Color.white);
                material.SetFloat("_Size", 1);
                material.SetFloat("_BumpAmt", mutation == "vertical-axis-lost" ? 0 : 4);
                material.SetTexture("_BumpMap", mutation == "normal-AG-channel-lost" ? ag : rgb);
                blur.SetMaterialDirty();
                SamplePixels("pixel-defect-control-" + mutation, Render(mutation),
                    mutation == "vertical-axis-lost" ? expected : distorted, baseline, .025f, true);
            }
            var receipt = new Receipt { unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
                actualCanvasRenderer = true, originalPixelParityVerified = false,
                cases = Samples.Count, samples = Samples.ToArray(), importedNormalGpuChannels = encoded,
                importedNormalFormat = rgbaFormat, androidNormalFormat = importer.GetPlatformTextureSettings("Android").format.ToString(),
                compressedNormalFormat = imported.format.ToString(), compressedNormalGpuChannels = compressedEncoded };
            File.WriteAllText("ProbeOutput/results.json", JsonUtility.ToJson(receipt, true) + "\n");
            Debug.Log("UI Blur actual Canvas evidence: " + JsonUtility.ToJson(receipt));
            foreach (var sample in Samples) if (!sample.passed)
                throw new InvalidOperationException("Actual Canvas pixel control failed: " + sample.name + " error=" + sample.maximumError);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void SetRect(RectTransform rect, float side)
    {
        rect.sizeDelta = Vector2.one * side;
        rect.localPosition = Vector3.zero;
    }
    private static Color[] Render(string label)
    {
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var old = RenderTexture.active; RenderTexture.active = target;
        var readback = new Texture2D(Side, Side, TextureFormat.RGBA32, false, true);
        readback.ReadPixels(new Rect(0, 0, Side, Side), 0, 0); readback.Apply();
        RenderTexture.active = old;
        File.WriteAllBytes("ProbeOutput/" + label + ".png", readback.EncodeToPNG());
        Color[] result = readback.GetPixels(); UnityEngine.Object.DestroyImmediate(readback); return result;
    }
    private static Color[] Reference(Color[] background)
    {
        var result = (Color[])background.Clone();
        float[] weights = { .05f, .09f, .12f, .15f, .18f, .15f, .12f, .09f, .05f };
        for (int y = 20; y < Side - 20; y++) for (int x = 20; x < Side - 20; x++)
        {
            Color value = Color.clear;
            for (int j = -4; j <= 4; j++) for (int i = -4; i <= 4; i++)
                value += background[(y + j) * Side + x + i] * weights[i + 4] * weights[j + 4];
            result[y * Side + x] = value;
        }
        return result;
    }
    private static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        var pixels = new Color[64]; for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels); texture.Apply(); return texture;
    }
    private static Color SampleImportedNormal(Texture2D texture)
    {
        var rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var sample = new Material(Shader.Find("Hidden/QuestBlurNormalSampleProbe"));
        ShaderUtil.CompilePass(sample, 0, true);
        Graphics.Blit(texture, rt, sample);
        var old = RenderTexture.active; RenderTexture.active = rt;
        var read = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0); read.Apply();
        var color = read.GetPixel(4, 4);
        RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(read); UnityEngine.Object.DestroyImmediate(sample); return color;
    }
    private static Color[] DistortionReference(Color[] background, Vector2 normal, Color tint)
    {
        Matrix4x4 vp = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
        Vector4 clip = vp * new Vector4(0, 0, 0, 1);
        Vector2 offset = normal * (4 * clip.z / clip.w);
        var result = (Color[])background.Clone();
        for (int y = 24; y < Side - 24; y++) for (int x = 24; x < Side - 24; x++)
        {
            float u = x + offset.x, v = y + offset.y;
            int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
            Color a = Color.LerpUnclamped(background[y0 * Side + x0], background[y0 * Side + x0 + 1], u - x0);
            Color b = Color.LerpUnclamped(background[(y0 + 1) * Side + x0], background[(y0 + 1) * Side + x0 + 1], u - x0);
            result[y * Side + x] = Color.LerpUnclamped(a, b, v - y0) * tint;
        }
        return result;
    }
    private static void SamplePixels(string name, Color[] actual, Color[] expected, Color[] negative, float tolerance = .006f, bool expectMismatch = false)
    {
        float max = 0, mean = 0, difference = 0; int n = 0;
        for (int y = 24; y < Side - 24; y++) for (int x = 24; x < Side - 24; x++)
        {
            Color a = actual[y * Side + x], e = expected[y * Side + x], other = negative[y * Side + x];
            float error = Mathf.Max(Mathf.Abs(a.r - e.r), Mathf.Abs(a.g - e.g), Mathf.Abs(a.b - e.b), Mathf.Abs(a.a - e.a));
            max = Mathf.Max(max, error); mean += error; n++;
            difference = Mathf.Max(difference, Mathf.Abs(a.r - other.r), Mathf.Abs(a.g - other.g), Mathf.Abs(a.b - other.b));
        }
        Color center = actual[(Side / 2) * Side + Side / 2];
        Samples.Add(new Sample { name = name, maximumError = max, meanError = mean / n, maximumDifference = difference,
            centerR = center.r, centerG = center.g, centerB = center.b, centerA = center.a, culled = blur.canvasRenderer.cull,
            passed = expectMismatch ? max > .04f : max < tolerance && difference > .04f });
    }
}
