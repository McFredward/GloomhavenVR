using System;
using GloomhavenVR.Cards;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

public static partial class DiagnosticProgram
{
    private static void QualifyReadableColorSpace()
    {
        bool stereo = Shader.IsKeywordEnabled("STEREO_INSTANCING_ON");
        RenderTexture previous = RenderTexture.active;
        var camera = Keep(new GameObject("readable-linear-camera")).AddComponent<Camera>();
        camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.orthographic = true; camera.orthographicSize = 24; camera.cullingMask = 1 << 29;
        var target = Keep(new RenderTexture(64, 48, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
        Check(target.Create(), "linear-readable camera output allocated"); camera.targetTexture = target;
        var canvasObject = Keep(new GameObject("readable-linear-canvas", typeof(RectTransform), typeof(Canvas)));
        canvasObject.layer = 29; canvasObject.transform.position = new Vector3(0, 0, 2);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        var rawObject = Keep(new GameObject("readable-linear-raw", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)));
        rawObject.layer = 29; rawObject.transform.SetParent(canvasObject.transform, false);
        var raw = rawObject.GetComponent<RawImage>(); raw.rectTransform.sizeDelta = new Vector2(64, 48);
        var source = Keep(new Texture2D(32, 24, TextureFormat.RGBA32, false, true));
        source.name = "readable-linear-original"; source.filterMode = FilterMode.Point; Fill(source, 31);
        Check(!GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat), "camera fixture original contains linear encoded pixels");
        try
        {
            Shader.DisableKeyword("STEREO_INSTANCING_ON");
            Color32[] Draw()
            {
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                var pixels = Keep(new Texture2D(64, 48, TextureFormat.RGBA32, false, false));
                pixels.ReadPixels(new Rect(0, 0, 64, 48), 0, 0); pixels.Apply(false);
                return pixels.GetPixels32();
            }
            void Equal(Color32[] before, Color32[] after, string reason)
            {
                for (int i = 0; i < before.Length; i++)
                    Check(Math.Abs(before[i].r - after[i].r) <= 1 && Math.Abs(before[i].g - after[i].g) <= 1
                        && Math.Abs(before[i].b - after[i].b) <= 1 && Math.Abs(before[i].a - after[i].a) <= 1, reason);
            }
            raw.texture = source; Color32[] original = Draw();
            Texture2D? filtered = CardFaceMipBake.BakedTextureFor(source);
            Check(filtered != null && !GraphicsFormatUtility.IsSRGBFormat(filtered.graphicsFormat),
                "readable whole-texture filtering retains original linear encoding");
            raw.texture = filtered; filtered!.filterMode = FilterMode.Point; filtered.anisoLevel = 1;
            Equal(original, Draw(), "actual UI camera retains linear-readable whole-texture RGBA");
            rawObject.SetActive(false);
            var imageObject = Keep(new GameObject("readable-linear-sprite", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
            imageObject.layer = 29; imageObject.transform.SetParent(canvasObject.transform, false);
            var image = imageObject.GetComponent<Image>(); image.rectTransform.sizeDelta = new Vector2(64, 48);
            // PreferRegion selects the real small region/CPU slice route, not the
            // whole-atlas copy whose encoding was checked above.
            var regionSource = Keep(new Texture2D(32, 24, TextureFormat.RGBA32, false, true));
            regionSource.name = "readable-linear-region-original"; regionSource.filterMode = FilterMode.Point; Fill(regionSource, 31);
            Sprite sprite = SpriteOf(regionSource, new Rect(5, 7, 8, 6)); image.sprite = sprite;
            original = Draw(); image.sprite = CardFaceMipBake.PresentationFor(sprite);
            Check(image.sprite != sprite && !GraphicsFormatUtility.IsSRGBFormat(image.sprite!.texture.graphicsFormat),
                "readable region filtering retains original linear encoding");
            image.sprite.texture.filterMode = FilterMode.Point; image.sprite.texture.anisoLevel = 1;
            Equal(original, Draw(), "actual UI camera retains linear-readable region RGBA");
        }
        finally
        {
            canvasObject.SetActive(false); RenderTexture.active = previous;
            if (stereo) Shader.EnableKeyword("STEREO_INSTANCING_ON"); else Shader.DisableKeyword("STEREO_INSTANCING_ON");
        }
    }
}
