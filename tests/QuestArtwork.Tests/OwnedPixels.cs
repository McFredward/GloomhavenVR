using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

public static partial class DiagnosticProgram
{
    private static IEnumerator QualifyOwnedPixels()
    {
        string directory = Path.Combine(Application.temporaryCachePath, "owned-card-pixel-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string previous = QuestStandalonePlatform.ResourceDirectory;
        QuestStandalonePlatform.ResourceDirectory = directory;
        try
        {
            File.WriteAllBytes(Path.Combine(directory, QuestCardPixels.FileName), Resources.Load<TextAsset>("NativeCardPixels").bytes);
            Texture2D source = Resources.Load<Texture2D>("OwnedSamplingPattern");
            if (source == null) throw new Exception("Owned sampling source did not import");
            Check(!source.isReadable && source.updateCount == 0 && source.mipmapCount == 1,
                "actual imported GPU-only pattern matches immutable native source boundary");
            byte[] expectedBytes = Resources.Load<TextAsset>("OwnedPatternExpected").bytes;
            Color32[]? exact = QuestCardPixels.Read(source);
            Check(exact != null && exact.Length == source.width * source.height,
                "owned GPU-only card receives exact original RGBA before mip generation");
            for (int i = 0; i < exact!.Length; i++)
                Check(exact[i].r == expectedBytes[i * 4] && exact[i].g == expectedBytes[i * 4 + 1]
                    && exact[i].b == expectedBytes[i * 4 + 2] && exact[i].a == expectedBytes[i * 4 + 3],
                    "owned GPU-only card receives exact original RGBA before mip generation");
            Sprite native = Keep(Sprite.Create(source, new Rect(0, 0, source.width, source.height), Vector2.one * .5f, 512, 0, SpriteMeshType.FullRect));
            Sprite? filtered = CardFaceMipBake.PresentationFor(native);
            Check(filtered != null && filtered != native && filtered.texture.mipmapCount > 1,
                "owned GPU-only card receives exact mip filtering without XR readback");
            Check(CardFaceMipBake.PresentationFor(native) == filtered, "warm owned native card reuses the exact baked sprite");
            foreach (string name in new[] { "NativeHeader", "NativeItem" })
            {
                Sprite original = Resources.Load<Sprite>(name);
                if (original == null) throw new Exception("Retained native card did not import: " + name);
                Check(!original.texture.isReadable, "retained actual original card is GPU-only: " + name);
                Sprite? copy = CardFaceMipBake.PresentationFor(original);
                Check(copy != null && copy != original && copy.texture.mipmapCount > 1,
                    "native trimmed card receives the established Frame mipmapped replacement");
                Check(copy!.rect == original.rect && copy.pivot == original.pivot && copy.border == original.border
                    && copy.pixelsPerUnit == original.pixelsPerUnit, "native filtered card retains original logical geometry");
                QualifyNativeMipPicture(original, copy, name == "NativeHeader");
            }

            var canvasObject = Keep(new GameObject("owned-card-mip-canvas", typeof(RectTransform), typeof(Canvas)));
            canvasObject.layer = 29; var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var imageObject = Keep(new GameObject("owned-card-mip-image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
            imageObject.layer = 29; imageObject.transform.SetParent(canvasObject.transform, false);
            var image = imageObject.GetComponent<Image>(); image.rectTransform.sizeDelta = Vector2.one;
            var camera = Keep(new GameObject("owned-card-mip-camera")).AddComponent<Camera>();
            camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None; camera.fieldOfView = 60;
            camera.nearClipPlane = .01f; camera.farClipPlane = 30; camera.cullingMask = 1 << 29;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; canvas.worldCamera = camera;
            double nativeError = 0, filteredError = 0; int views = 0;
            foreach (int size in new[] { 32, 64, 128 }) foreach (float angle in new[] { 0f, 45f })
            {
                canvasObject.transform.rotation = Quaternion.Euler(0, angle, 0);
                camera.transform.position = new Vector3(.0017f, 0, -256f / (2 * Mathf.Tan(30 * Mathf.Deg2Rad) * size));
                image.sprite = native; Color32[] reference = OwnedPicture(camera, 2048), raw = OwnedPicture(camera, 256);
                image.sprite = filtered; Color32[] mipped = OwnedPicture(camera, 256);
                double rawRms = OwnedError(raw, reference, 8), mipRms = OwnedError(mipped, reference, 8);
                nativeError += rawRms; filteredError += mipRms; views++;
                UnityEngine.Debug.Log("OWNED CARD MIP pixels=" + size + " tilt=" + angle + " nativeRms=" + rawRms + " filteredRms=" + mipRms);
            }
            Check(filteredError < nativeError * .8, "owned filtering reduces actual distance and oblique minification error");
            // A stale/mutable texture cannot fall back to its old disk entry.
            var changed = Keep(new Texture2D(source.width, source.height, TextureFormat.RGBA32, false));
            changed.name = source.name; changed.SetPixels32(new Color32[source.width * source.height]); changed.Apply(false, true);
            Check(changed.updateCount != 0 && QuestCardPixels.Read(changed) == null,
                "same-name changed GPU texture retains native content rather than immutable file pixels");
            string nativeName = source.name; source.name = "NativeHeaderTexture";
            Check(QuestCardPixels.Read(source) == null, "owned pixel identity rejects wrong native dimensions"); source.name = nativeName;
            QuestStandalonePlatform.Enabled = false;
            Check(QuestCardPixels.Read(source) == null, "desktop never consumes Quest owned sidecar pixels");
            QuestStandalonePlatform.Enabled = true;
            Metrics = "owned RGBA exact=" + exact.Length + " minificationViews=" + views + " rmsNative=" + nativeError + " rmsFiltered=" + filteredError;
        }
        finally
        {
            QuestStandalonePlatform.ResourceDirectory = previous;
            Directory.Delete(directory, true);
        }
        yield return null;
    }

    private static void QualifyNativeMipPicture(Sprite original, Sprite filtered, bool abilityShader)
    {
        var camera = Keep(new GameObject("actual-native-mip-oracle-camera")).AddComponent<Camera>();
        camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.orthographic = true; camera.orthographicSize = .5f; camera.cullingMask = 1 << 29;
        camera.transform.position = new Vector3(0, 0, -2); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        var canvasObject = Keep(new GameObject("actual-native-card-canvas", typeof(RectTransform), typeof(Canvas)));
        canvasObject.layer = 29; var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        var imageObject = Keep(new GameObject("actual-native-card-image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)));
        imageObject.layer = 29; imageObject.transform.SetParent(canvasObject.transform, false);
        var image = imageObject.GetComponent<Image>(); image.rectTransform.sizeDelta = original.rect.size / 1024f;
        image.useSpriteMesh = false; image.preserveAspect = true;
        Shader native = Resources.Load<Shader>("NativeHeaderShader");
        Check(native != null && native.isSupported, "retained actual native ability shader is supported");
        foreach (bool authored in abilityShader ? new[] { false, true } : new[] { false })
        {
            image.material = authored ? Keep(new Material(native)) : null;
            image.sprite = original; Color32[] expected = OwnedPicture(camera, 1024);
            image.sprite = filtered; Color32[] actual = OwnedPicture(camera, 1024);
            long absolute = 0; int solid = 0, lost = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                Color32 a = actual[i], b = expected[i];
                absolute += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) + Math.Abs(a.a - b.a);
                if (b.a > 245) { solid++; if (a.a < 200) lost++; }
            }
            double mean = absolute / (double)(expected.Length * 4);
            UnityEngine.Debug.Log("NATIVE OWNED MIP sprite=" + original.name + " authoredShader=" + authored + " meanChannelError=" + mean + " nativeSolid=" + solid + " lost=" + lost);
            Check(solid > 40000, "actual native card picture oracle contains complete printed artwork");
            Check(lost < 4 && mean < 1, "native mip replacement preserves the actual complete card picture at full texel density");
        }
        canvasObject.SetActive(false);
    }

    private static Color32[] OwnedPicture(Camera camera, int size)
    {
        var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create(); RenderTexture previous = RenderTexture.active; RenderTexture previousTarget = camera.targetTexture;
        var read = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        try
        {
            camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0); read.Apply(false); return read.GetPixels32();
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(read); UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static double OwnedError(Color32[] actual, Color32[] reference, int factor)
    {
        int width = (int)Math.Sqrt(actual.Length), referenceWidth = width * factor; double sum = 0;
        for (int y = 0; y < width; y++) for (int x = 0; x < width; x++)
        {
            double r = 0, g = 0, b = 0;
            for (int dy = 0; dy < factor; dy++) for (int dx = 0; dx < factor; dx++)
            {
                Color32 p = reference[(y * factor + dy) * referenceWidth + x * factor + dx];
                // The Quest project renders in Linear. A supersampled pixel
                // oracle must average radiance, not sRGB byte values.
                r += Mathf.GammaToLinearSpace(p.r / 255f); g += Mathf.GammaToLinearSpace(p.g / 255f); b += Mathf.GammaToLinearSpace(p.b / 255f);
            }
            Color32 a = actual[y * width + x]; double count = factor * factor;
            sum += Math.Pow(Mathf.GammaToLinearSpace(a.r / 255f) - r / count, 2)
                + Math.Pow(Mathf.GammaToLinearSpace(a.g / 255f) - g / count, 2)
                + Math.Pow(Mathf.GammaToLinearSpace(a.b / 255f) - b / count, 2);
        }
        return Math.Sqrt(sum / (actual.Length * 3));
    }
}
