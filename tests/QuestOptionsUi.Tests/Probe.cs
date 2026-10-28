using System;
using System.IO;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class QuestOptionsUiProbe
{
    [Serializable] sealed class Result
    {
        public bool passed, hardwareVerified;
        public int assertions, defectControls, viewportLeakedPixels, viewportRetainedPixels, sliderLeakedPixels;
        public int stencilReferencePixelDifferences, sliderValues;
        public string failure;
    }
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false); rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }
    static Color32[] Pixels(Camera camera, RenderTexture target)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var rectangle in UnityEngine.Object.FindObjectsOfType<RectMask2D>()) rectangle.PerformClipping();
        camera.Render(); var cpu = new Texture2D(512, 256, TextureFormat.RGBA32, false, true);
        var previous = RenderTexture.active;
        try { RenderTexture.active = target; cpu.ReadPixels(new Rect(0, 0, 512, 256), 0, 0); cpu.Apply(); return cpu.GetPixels32(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(cpu); }
    }
    static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, Material material, Vector2 size, Vector2 position, Color color)
    {
        var text = Rect(name, parent, size, position).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSharedMaterial = material; text.fontSize = 18; text.color = color;
        text.alignment = TextAlignmentOptions.Center; text.text = "18 / 18"; text.ForceMeshUpdate(true); return text;
    }
    static int Red(Color32[] pixels, bool outside, float low, float high)
    {
        int count = 0;
        for (int y = 0; y < 256; y++) for (int x = 0; x < 512; x++)
        { Color32 p = pixels[y * 512 + x]; if ((y < low || y >= high) == outside && p.r > 24 && p.r > p.g * 2 && p.r > p.b * 2) count++; }
        return count;
    }
    static int Difference(Color32[] expected, Color32[] actual)
    {
        int difference = 0;
        for (int i = 0; i < expected.Length; i++)
            if (Math.Abs(expected[i].r - actual[i].r) > 2 || Math.Abs(expected[i].g - actual[i].g) > 2
                || Math.Abs(expected[i].b - actual[i].b) > 2 || Math.Abs(expected[i].a - actual[i].a) > 2) difference++;
        return difference;
    }
    public static void Run()
    {
        var result = new Result(); int code = 1;
        try
        {
            var camera = new GameObject("native-options-camera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 100; camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create(); camera.targetTexture = target;
            var root = new GameObject("cloned-options-canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var font = Resources.Load<TMP_FontAsset>("fonts & materials/Sarala-Regular SDF");
            var original = Resources.Load<Material>("fonts & materials/Sarala-Regular-Shadow SDF");
            var referenceShader = Resources.Load<Shader>("NativeStencilReference");
            Check(font != null && original != null && referenceShader != null && referenceShader.isSupported, "Exact retained native options font/material/shader import");
            var reference = new Material(original); reference.shader = referenceShader;
            var pane = Rect("native-options-main-area", root.transform, new Vector2(280, 150), Vector2.zero);
            var viewport = Rect("native-options-viewport", pane, new Vector2(250, 64), Vector2.zero);
            viewport.gameObject.AddComponent<Image>(); var mask = viewport.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
            var scroll = pane.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport;
            for (int i = -1; i <= 1; i++)
                Text("native-option-caption-" + i, viewport, font, original, new Vector2(220, 26), new Vector2(0, i * 48), Color.red);
            Canvas.ForceUpdateCanvases(); var corners = new Vector3[4]; viewport.GetWorldCorners(corners);
            float low = camera.WorldToScreenPoint(corners[0]).y, high = camera.WorldToScreenPoint(corners[2]).y;
            Color32[] before = Pixels(camera, target); result.viewportLeakedPixels = Red(before, true, low, high);
            Check(result.viewportLeakedPixels > 50, "Recovered native options TMP stencil leak is reproduced");
            Check(!QuestOptionsMask.ApplyViewport(scroll, false) && viewport.GetComponent<RectMask2D>() == null, "Desktop cloned options remain untouched");
            Check(QuestOptionsMask.ApplyViewport(scroll, true), "Only the actual cloned options scroll viewport admits the repair");
            Check(!QuestOptionsMask.ApplyViewport(scroll, true), "Repeated preparation does not duplicate the viewport mask");
            Color32[] after = Pixels(camera, target); result.viewportRetainedPixels = Red(after, false, low, high);
            Check(Red(after, true, low, high) == 0 && result.viewportRetainedPixels > 50, "Options captions retain their interior and cannot spill beyond the authored viewport");
            viewport.GetComponent<RectMask2D>().enabled = false;
            Check(Red(Pixels(camera, target), true, low, high) > 50, "Removing only viewport repair reproduces text leak"); result.defectControls++;
            pane.gameObject.SetActive(false);

            var sliderRect = Rect("Master Slider", root.transform, new Vector2(240, 28), Vector2.zero);
            var slider = sliderRect.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 100;
            var area = Rect("Area", sliderRect, new Vector2(236, 24), Vector2.zero);
            // Exact original donor geometry: Amount and Amount(1) have distinct
            // x offsets (-10.6 and -3), explaining the visible doubled outline
            // when the second caption loses its filled-image stencil.
            var baseText = Text("Amount", area, font, original, new Vector2(75, 24), new Vector2(69.9f, 0), new Color(.82f, .82f, .82f, .4f));
            var fill = Rect("Progress", area, new Vector2(236, 24), Vector2.zero);
            var image = fill.gameObject.AddComponent<Image>(); image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal;
            image.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), Vector2.one * .5f);
            image.fillOrigin = (int)Image.OriginHorizontal.Left; image.color = new Color(.2f, .18f, .1f, 1);
            fill.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var filledText = Text("Amount (1)", fill, font, original, new Vector2(75, 24), new Vector2(77.5f, 0), Color.red);
            slider.fillRect = fill; slider.value = 0;
            Color32[] leaked = Pixels(camera, target); result.sliderLeakedPixels = Red(leaked, false, 0, 256);
            Check(result.sliderLeakedPixels > 50, "Original duplicate amount is visible outside the empty fill before repair");
            Check(!QuestOptionsMask.ApplySlider(slider, false), "Desktop slider retains its original captions and masking");
            image.fillMethod = Image.FillMethod.Radial360; Check(!QuestOptionsMask.ApplySlider(slider, true), "Shaped/radial native masks are never guessed as rectangles"); image.fillMethod = Image.FillMethod.Horizontal;
            Check(QuestOptionsMask.ApplySlider(slider, true), "Exact native horizontal fill admits the Quest rectangle");
            Check(!QuestOptionsMask.ApplySlider(slider, true), "Slider repair is installed once");
            var rectangle = fill.GetComponent<RectMask2D>();
            foreach (float value in new[] { 0f, 28.4f, 72f, 90f, 100f })
            {
                slider.value = value;
                baseText.fontSharedMaterial = original; filledText.fontSharedMaterial = original;
                Color32[] actual = Pixels(camera, target);
                rectangle.enabled = false; baseText.fontSharedMaterial = reference; filledText.fontSharedMaterial = reference;
                Color32[] expected = Pixels(camera, target);
                int differences = Difference(expected, actual); result.stencilReferencePixelDifferences += differences;
                // Rect clipping interpolates coverage at the fill edge whereas
                // the original stencil is a per-sample bit. Permit only that
                // one-pixel edge; every interior/exterior glyph pixel is exact.
                float boundary = camera.WorldToScreenPoint(fill.TransformPoint(new Vector3(fill.rect.xMin + fill.rect.width * image.fillAmount, 0, 0))).x;
                for (int pixel = 0; pixel < actual.Length; pixel++)
                {
                    Color32 a = actual[pixel], b = expected[pixel];
                    if (Math.Abs(a.r - b.r) <= 2 && Math.Abs(a.g - b.g) <= 2 && Math.Abs(a.b - b.b) <= 2 && Math.Abs(a.a - b.a) <= 2) continue;
                    Check(Math.Abs(pixel % 512 + .5f - boundary) < 1.5f,
                        "Quest amount pixels outside the exact fill edge differ from the original stencil at " + value + "%");
                }
                Check(differences < 32, "Only a bounded one-pixel stencil/rectangle coverage edge may differ at " + value + "%");
                rectangle.enabled = true; result.sliderValues++;
            }
            baseText.fontSharedMaterial = original; filledText.fontSharedMaterial = original; slider.value = 0;
            Check(Red(Pixels(camera, target), false, 0, 256) == 0, "Empty fill has no offset duplicate amount");
            rectangle.enabled = false;
            Check(Red(Pixels(camera, target), false, 0, 256) > 50, "Removing only fill repair reproduces duplicate amount"); result.defectControls++;
            rectangle.enabled = true;
            Check(slider.GetComponentsInChildren<TMP_Text>().Length == 2 && filledText.enabled && baseText.enabled,
                "Both original amount captions remain enabled; original unfilled and filled typography is retained");
            result.assertions = assertions; result.passed = true; code = 0;
        }
        catch (Exception error) { result.failure = error.ToString(); Debug.LogException(error); }
        Directory.CreateDirectory("ProbeOutput"); File.WriteAllText("ProbeOutput/results.json", JsonUtility.ToJson(result, true)); EditorApplication.Exit(code);
    }
}
