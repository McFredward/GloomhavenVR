using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

public static partial class DiagnosticProgram
{
    private static void QualifyNativePortraits()
    {
        Sprite stat = Resources.Load<Sprite>("NativeStatPortrait");
        Sprite initiative = Resources.Load<Sprite>("NativeInitiativePortrait");
        if (stat == null && initiative == null) return;
        Check(stat != null && initiative != null, "Both retained original portrait references imported");
        var camera = Keep(new GameObject("native-portrait-camera")).AddComponent<Camera>();
        camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.orthographic = true; camera.cullingMask = 1 << 29;
        var canvasObject = Keep(new GameObject("native-portrait-canvas", typeof(RectTransform), typeof(Canvas)));
        canvasObject.layer = 29; canvasObject.transform.position = new Vector3(0, 0, 2);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        bool stereo = Shader.IsKeywordEnabled("STEREO_INSTANCING_ON");
        RenderTexture previous = RenderTexture.active;
        try
        {
            Shader.DisableKeyword("STEREO_INSTANCING_ON");
            Color32[] Draw(int width, int height)
            {
                var target = Keep(new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
                Check(target.Create(), "Native portrait target allocated"); camera.targetTexture = target;
                camera.orthographicSize = height / 2f;
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                var pixels = Keep(new Texture2D(width, height, TextureFormat.RGBA32, false, false));
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply(false);
                return pixels.GetPixels32();
            }
            void EqualPortrait(Color32[] original, Color32[] filtered, int width, string role)
            {
                double difference = 0;
                int top = 0, lower = 0;
                for (int i = 0; i < original.Length; i++)
                {
                    Color32 a = original[i], b = filtered[i];
                    difference += Math.Abs(a.r-b.r) + Math.Abs(a.g-b.g) + Math.Abs(a.b-b.b) + Math.Abs(a.a-b.a);
                    if (a.a >= 128 && Math.Max(a.r, Math.Max(a.g, a.b)) > 48)
                    {
                        if (i / width >= original.Length / width / 2) top++; else lower++;
                    }
                }
                Check(top > original.Length / 20 && lower > original.Length / 20,
                    "retained native " + role + " renders real art in both halves");
                Check(difference / (original.Length * 4) < 1.5,
                    "retained native " + role + " filtering retains the full original camera RGBA");
                UnityEngine.Debug.Log("Native portrait role=" + role + " top=" + top + " lower=" + lower
                    + " meanRGBAError=" + difference / (original.Length * 4));
            }
            var statObject = Keep(new GameObject("native-stat-image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
            statObject.layer = 29; statObject.transform.SetParent(canvasObject.transform, false);
            var image = statObject.GetComponent<Image>(); image.sprite = stat; image.preserveAspect = true; image.useSpriteMesh = false;
            image.rectTransform.sizeDelta = new Vector2(326, 106);
            Color32[] before = Draw(326, 106);
            image.sprite = CardFaceMipBake.ReplacementFor(stat!);
            Check(image.sprite != null && CardFaceMipBake.OriginalFor(image.sprite) == stat,
                "actual native trimmed stat portrait keeps exact original provenance");
            EqualPortrait(before, Draw(326, 106), 326, "stat");
            statObject.SetActive(false);
            var rawObject = Keep(new GameObject("native-initiative-image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)));
            rawObject.layer = 29; rawObject.transform.SetParent(canvasObject.transform, false);
            var raw = rawObject.GetComponent<RawImage>(); raw.texture = initiative!.texture;
            raw.rectTransform.sizeDelta = new Vector2(512, 512); raw.uvRect = new Rect(0, 0, 1, 1);
            before = Draw(512, 512);
            raw.texture = CardFaceMipBake.BakedTextureFor(initiative!.texture);
            Check(raw.texture != null && raw.uvRect == new Rect(0, 0, 1, 1),
                "actual native initiative filtering preserves the complete authored UV rectangle");
            EqualPortrait(before, Draw(512, 512), 512, "initiative");
            rawObject.SetActive(false);
        }
        finally
        {
            canvasObject.SetActive(false); RenderTexture.active = previous;
            if (stereo) Shader.EnableKeyword("STEREO_INSTANCING_ON"); else Shader.DisableKeyword("STEREO_INSTANCING_ON");
        }
    }
}
