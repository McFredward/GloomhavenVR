using System;
using System.Collections;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class DiagnosticProgram
{
    private static IEnumerator QualifyStatSnapshots()
    {
        Sprite nativeAsset = Resources.Load<Sprite>("NativeStatPortrait");
        Check(nativeAsset != null && !nativeAsset.texture.isReadable, "native GPU-only stat portrait input is present");
        // Imported editor assets may retain an internal serialized pixel shadow
        // even when the public isReadable flag is false. Explicitly discard it
        // on this owned source to exercise the real GPU-only runtime contract.
        Texture2D original = nativeAsset!.texture;
        var gpuOnly = Keep(new Texture2D(original.width, original.height, original.format, original.mipmapCount,
            !UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(original.graphicsFormat)));
        gpuOnly.name = "native-stat-gpu-only"; gpuOnly.filterMode = original.filterMode; gpuOnly.wrapMode = original.wrapMode;
        gpuOnly.Apply(false, true); Graphics.CopyTexture(original, gpuOnly);
        yield return null;
        Sprite native = Keep(Sprite.Create(gpuOnly, nativeAsset.rect,
            new Vector2(nativeAsset.pivot.x / nativeAsset.rect.width, nativeAsset.pivot.y / nativeAsset.rect.height),
            nativeAsset.pixelsPerUnit, 0, SpriteMeshType.FullRect, nativeAsset.border));
        var root = Keep(new GameObject("native-live-stat-panel", typeof(RectTransform), typeof(Canvas)));
        Canvas canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var live = root.AddComponent<ActorStatPanel>();
        Image Make(string name, Transform parent)
        {
            var image = Keep(new GameObject(name, typeof(RectTransform), typeof(Image))).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.rectTransform.sizeDelta = new Vector2(326, 110);
            image.sprite = native; image.preserveAspect = true;
            image.color = new Color(.8f, .9f, .7f, .85f);
            return image;
        }
        live.characterPortrait = Make("Portrait", root.transform);
        live.enemyPortrait = Make("EnemyPortrait", root.transform); live.enemyPortrait.gameObject.SetActive(false);
        var frame = Make("NineSlicedFrame", root.transform);
        frame.type = Image.Type.Sliced; frame.gameObject.SetActive(false);
        var holder = Keep(new GameObject("inactive-static-holder")); holder.SetActive(false);
        var copy = Keep(Object.Instantiate(root, holder.transform, false));
        int detached = ActualStat.Detach(live, copy);
        Check(detached == 2, "portrait-only detachment retains native character and enemy image bindings");
        Check(copy.transform.Find("NineSlicedFrame").GetComponent<Image>().sprite == frame.sprite
            && copy.transform.Find("NineSlicedFrame").GetComponent<Image>().type == Image.Type.Sliced,
            "portrait snapshot leaves nine-sliced frames and all other original sprites untouched");
        foreach (Texture2D texture in ActualStat._copyOwnedTextures) Keep(texture);
        foreach (Sprite sprite in ActualStat._copyOwnedSprites) Keep(sprite);
        var camera = Keep(new GameObject("stat-snapshot-private-camera")).AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 60;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        var target = Keep(new RenderTexture(326, 120, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
        target.Create(); camera.targetTexture = target;
        void Layer(GameObject item) { foreach (Transform t in item.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30; }
        Layer(root); Layer(copy);
        Color32[] Capture()
        {
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active;
            RenderTexture capture = camera.targetTexture;
            var pixels = Keep(new Texture2D(capture.width, capture.height, TextureFormat.RGBA32, false, false));
            try { RenderTexture.active = capture; pixels.ReadPixels(new Rect(0, 0, capture.width, capture.height), 0, 0); pixels.Apply(false); return pixels.GetPixels32(); }
            finally { RenderTexture.active = previous; }
        }
        root.SetActive(true); holder.SetActive(false); yield return null;
        live.characterPortrait.sprite = nativeAsset;
        Color32[] expected = Capture();
        live.characterPortrait.sprite = native;
        Pixels(Capture(), expected, 326, 0, 0, 326, 120,
            "owned GPU-only source retains actual serialized native portrait pixels");
        int painted = 0; foreach (Color32 pixel in expected) if (pixel.a > 8) painted++;
        Check(painted > 1000, "native stat portrait actually paints private GPU pixels");
        root.SetActive(false); holder.SetActive(true); yield return null;
        Color32[] frozen = Capture();
        Pixels(frozen, expected, 326, 0, 0, 326, 120,
            "native portrait-only copy preserves original GPU output before filtering");
        Texture2D candidate = copy.transform.Find("Portrait").GetComponent<Image>().sprite.texture;
        if (candidate.isReadable)
        {
            // CopyTexture guarantees CPU copying only when BOTH textures are
            // readable. Model a permitted stale CPU shadow without uploading it:
            // GPU output must remain native, and the real rescan must not trust it.
            // Vulkan's default CPU-copy behaviour alone did not reproduce Android.
            candidate.SetPixels32(new Color32[candidate.width * candidate.height]);
            Pixels(Capture(), expected, 326, 0, 0, 326, 120,
                "stale CPU-shadow control leaves original GPU output untouched");
        }
        PanelMipBake.Rescan(copy.GetComponent<RectTransform>(), "ActorStatPanelCopy");
        Pixels(Capture(), expected, 326, 0, 0, 326, 120,
            "actual panel mip rescan preserves native frozen portrait GPU pixels");
        Image frozenImage = copy.transform.Find("Portrait").GetComponent<Image>();
        Check(frozenImage.sprite.texture != native!.texture,
            "frozen portrait owns its source texture independently of the native loader");
        Check(!frozenImage.sprite.texture.isReadable && frozenImage.sprite.texture.graphicsFormat == native.texture.graphicsFormat,
            "GPU-only copy preserves truthful CPU availability and native color encoding");
        Sprite alternate = SpriteOf(Pattern("other-live-actor", 160), new Rect(0, 0, 32, 24));
        live.characterPortrait.sprite = alternate;
        root.SetActive(true); root.transform.position = new Vector3(400, 0, 0);
        yield return null;
        Color32[] retained = Capture();
        Pixels(retained, expected, 326, 0, 0, 326, 120,
            "two held panels retain independent native portrait pixels after live source rebind");

        // Both panels actually draw in ONE camera pass, on opposite sides of the
        // same view. Compare each side to that owner's independent native output.
        var dual = Keep(new RenderTexture(652, 120, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
        dual.Create(); camera.targetTexture = dual;
        root.transform.position = new Vector3(-163, 0, 0); copy.transform.position = new Vector3(163, 0, 0);
        holder.SetActive(false); Color32[] primaryPixels = Capture();
        root.SetActive(false); holder.SetActive(true); Color32[] secondaryPixels = Capture();
        root.SetActive(true); Color32[] bothPixels = Capture();
        int leftVisible = 0, rightVisible = 0;
        for (int y = 0; y < 120; y++) for (int x = 0; x < 652; x++)
        {
            int index = y * 652 + x;
            Color32 desired = x < 326 ? primaryPixels[index] : secondaryPixels[index];
            Check(bothPixels[index].Equals(desired), "simultaneously rendered panels retain each owner's exact independent output");
            if (desired.a > 8) { if (x < 326) leftVisible++; else rightVisible++; }
        }
        Check(leftVisible > 1000 && rightVisible > 1000, "both held-panel owners actually paint the shared GPU view");
        camera.targetTexture = target; copy.transform.position = Vector3.zero;

        holder.SetActive(false); root.transform.position = Vector3.zero;

        // A runtime native source can be unloaded after the singleton changes actor.
        Texture2D runtime = Pattern("unloadable-actor", 45);
        Sprite runtimeSprite = SpriteOf(runtime, new Rect(3, 2, 25, 19));
        live.characterPortrait.sprite = runtimeSprite;
        Canvas.ForceUpdateCanvases();
        var frozenHost = Keep(Object.Instantiate(root, holder.transform, false));
        ActualStat.Detach(live, frozenHost);
        foreach (Texture2D texture in ActualStat._copyOwnedTextures) if (!Owned.Contains(texture)) Keep(texture);
        Layer(frozenHost);
        frozenHost.SetActive(false); Color32[] runtimeExpected = Capture();
        root.SetActive(false); copy.SetActive(false); frozenHost.SetActive(true); holder.SetActive(true);
        live.characterPortrait.sprite = alternate;
        Object.Destroy(runtimeSprite); Object.Destroy(runtime);
        yield return null;
        Color32[] runtimeFrozen = Capture();
        Pixels(runtimeFrozen, runtimeExpected, 326, 0, 0, 326, 120,
            "frozen native portrait survives original sprite and texture unload");
        QuestStandalonePlatform.Enabled = false;
        var desktop = Keep(Object.Instantiate(root, holder.transform, false));
        Check(ActualStat.Detach(live, desktop) == 2 && desktop.transform.Find("Portrait").GetComponent<Image>().sprite.texture.isReadable,
            "ordinary PC panel retains its existing portrait Sprite detachment path");
        foreach (Texture2D texture in ActualStat._copyOwnedTextures) if (!Owned.Contains(texture)) Keep(texture);
        foreach (Sprite sprite in ActualStat._copyOwnedSprites) Keep(sprite);
        QuestStandalonePlatform.Enabled = true;
        ActualStat._copyOwnedTextures.Clear(); ActualStat._copyOwnedSprites.Clear();
        root.SetActive(false); holder.SetActive(false);
    }
}
