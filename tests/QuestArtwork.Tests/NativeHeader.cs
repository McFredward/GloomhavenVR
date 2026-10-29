using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

public static partial class DiagnosticProgram
{
    private static void QualifyNativeHeader()
    {
        Sprite native = Resources.Load<Sprite>("NativeHeader");
        if (native == null) return; // Explicit optional owned-input qualification.
        Shader shader = Resources.Load<Shader>("NativeHeaderShader");
        Check(shader != null && shader.isSupported, "Exact retained original card shader compiles on this backend");
        var camera = Keep(new GameObject("native-header-camera")).AddComponent<Camera>();
        camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.orthographic = true; camera.orthographicSize = 225; camera.cullingMask = 1 << 29;
        var target = Keep(new RenderTexture(294, 450, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
        Check(target.Create(), "Native header draw target allocated"); camera.targetTexture = target;
        var canvasObject = Keep(new GameObject("native-header-canvas", typeof(RectTransform), typeof(Canvas)));
        canvasObject.layer = 29; canvasObject.transform.position = new Vector3(0, 0, 2);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        var imageObject = Keep(new GameObject("native-header-image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
        imageObject.layer = 29; imageObject.transform.SetParent(canvasObject.transform, false);
        var image = imageObject.GetComponent<Image>(); image.rectTransform.sizeDelta = new Vector2(294, 450);
        image.sprite = native; image.color = Color.white;
        bool stereo = Shader.IsKeywordEnabled("STEREO_INSTANCING_ON");
        RenderTexture previous = RenderTexture.active;
        try
        {
            Shader.DisableKeyword("STEREO_INSTANCING_ON");
            Color32[] Draw()
            {
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                var pixels = Keep(new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, false));
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                return pixels.GetPixels32();
            }
            Color32[] plain = Draw();
            int upper = 0, lower = 0;
            for (int y = 0; y < target.height; y++) for (int x = 0; x < target.width; x++)
            {
                if (plain[y * target.width + x].a < 200) continue;
                if (y > target.height * .75f) upper++;
                if (y < target.height * .25f) lower++;
            }
            Check(upper > 20000 && lower > 20000, "Retained native header Sprite draws real opaque art in both upper and lower quarters");
            Sprite? filtered = CardFaceMipBake.PresentationFor(native);
            image.sprite = filtered;
            Color32[] mip = Draw();
            int mipMismatch = 0, mipSolidLoss = 0;
            for (int i = 0; i < plain.Length; i++)
            {
                if (Math.Abs(plain[i].a - mip[i].a) > 2) mipMismatch++;
                if (plain[i].a >= 245 && mip[i].a < 150) mipSolidLoss++;
            }
            // The native import uses bilinear sampling; the filtered copy has a
            // trilinear/aniso mip footprint, so small AA-edge values may differ.
            // Missing the entire upper header is a loss of solid printed area.
            Check(mipSolidLoss < plain.Length / 500, "Retained native header mip presentation preserves solid upper and lower printed artwork");
            image.sprite = native;
            var material = Keep(new Material(shader)); material.SetFloat("_Flow", 0); material.SetFloat("_GreyOut", 0);
            material.SetFloat("_RedOut", 0); material.SetFloat("_OverlayOn", 0);
            material.SetColor("_MainColour", Color.white); material.SetColor("_Color", Color.white);
            image.material = material;
            Color32[] effect = Draw();
            int nativeMismatch = 0, shaderSolidLoss = 0;
            for (int i = 0; i < plain.Length; i++)
            {
                if (Math.Abs(plain[i].a - effect[i].a) > 2) nativeMismatch++;
                if (plain[i].a >= 245 && effect[i].a < 150) shaderSolidLoss++;
            }
            Check(shaderSolidLoss < plain.Length / 500, "Exact original idle card shader retains solid upper and lower printed artwork");
            int evidenceRecords = 0;
            bool evidenceFits = false;
            Application.LogCallback observer = (message, trace, type) =>
            {
                if (!message.StartsWith("[Quest startup] artwork role=retained-header ", StringComparison.Ordinal)) return;
                evidenceRecords++;
                evidenceFits = System.Text.Encoding.UTF8.GetByteCount(message) <= 1000 && message.Contains(" rgba6=")
                    && message.Contains("hierarchy=False") && message.Contains("sourceShader=GUI/AbilityCard_Shd");
            };
            image.sprite = filtered;
            canvasObject.SetActive(false);
            bool debug = VRLog.WantsDebug; VRLog.WantsDebug = true;
            Application.logMessageReceived += observer;
            try { QuestArtworkEvidence.Image(image, "retained-header"); }
            finally { Application.logMessageReceived -= observer; VRLog.WantsDebug = debug; }
            Check(evidenceRecords == 1, "Actual retained native header emits one bounded source record");
            Check(evidenceFits, "Retained native header source and both pixel crops fit the protected Android record while parked");
            UnityEngine.Debug.Log("Native header source: sprite=" + native.name + " texture=" + native.texture.name
                + " size=" + native.texture.width + "x" + native.texture.height + " rect=" + native.rect
                + " outer=" + UnityEngine.Sprites.DataUtility.GetOuterUV(native) + " padding=" + UnityEngine.Sprites.DataUtility.GetPadding(native)
                + " topOpaque=" + upper + " bottomOpaque=" + lower + " mipAlphaMismatch=" + mipMismatch + " shaderAlphaMismatch=" + nativeMismatch + " solidLoss=" + mipSolidLoss + "," + shaderSolidLoss);
        }
        finally
        {
            canvasObject.SetActive(false); RenderTexture.active = previous;
            if (stereo) Shader.EnableKeyword("STEREO_INSTANCING_ON"); else Shader.DisableKeyword("STEREO_INSTANCING_ON");
        }
    }
}
