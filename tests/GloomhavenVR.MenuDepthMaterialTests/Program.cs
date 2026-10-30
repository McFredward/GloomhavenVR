using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MenuDepthMaterialProgram
{
    private static int _checks;
    private static string _output = string.Empty;
    private static readonly Color32 Backing = new(28, 46, 69, 82);
    private const string NativePixels = "spatial dissolve preserves the exact native fragment and blend pixels";
    private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); }

    public static int Run(string output, string maskFile)
    {
        _checks = 0; _output = output;
        Check(QualitySettings.activeColorSpace == ColorSpace.Gamma, "the fixture uses the original game's Gamma colour space");
        Shader reference = Shader.Find("UI/Dissolve mask") ?? throw new Exception("The independent DXBC reference shader is absent.");
        Check(reference.isSupported && MenuDepthDisplay.DepthShader != null,
            "actual shipping depth shader and independently compiled native-DXBC reference are supported");
        Check(SystemInfo.SupportsTextureFormat(TextureFormat.DXT1), "actual native BC1 texture sampling is available");
        var nativeMask = new Texture2D(256, 256, TextureFormat.DXT1, true, false)
        { name = "actual native Noise1 BC1 mip payload", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 0, mipMapBias = 0 };
        nativeMask.LoadRawTextureData(File.ReadAllBytes(maskFile)); nativeMask.Apply(false, false);
        Check(nativeMask.mipmapCount == 9, "all nine original mask mips are loaded without RGBA conversion or regeneration");
        Texture2D randomMask = RandomMask(); Texture2D art = Artwork();
        Sprite sprite = Sprite.Create(art, new Rect(0, 0, art.width, art.height), new Vector2(.5f, .5f));
        try
        {
            using (var pair = new Pair(reference, sprite, nativeMask))
            {
                pair.Set(.35f, 1f); pair.Compare(NativePixels, "initial-native-reveal");
                Material clone = pair.Depth.Clone;
                foreach (float weight in new[] { 1f, 2.4900000095f })
                    foreach (float cutoff in new[] { 1f, .999f, .9f, .75f, .55f, .37f, .21f, .1f, 0f })
                    {
                        // Same Graphic, same native source Material, and no
                        // SetMaterialDirty. This is the original tween's port.
                        pair.Set(cutoff, weight); pair.Compare(NativePixels);
                    }
                Check(ReferenceEquals(clone, pair.Depth.Clone),
                    "native material clock changes reuse the same actual uGUI depth material clone");
                Check(pair.Depth.Image.material == pair.Depth.Source && pair.Depth.Source.GetFloat("_Cutoff") == 0,
                    "the native source material and its own animation value remain original");
                pair.Set(.47f, 1f);
                pair.MaskTransform(new Vector2(1.37f, .83f), new Vector2(.137f, .29f));
                pair.Compare(NativePixels, "independent-mask-transform");
                pair.MainTransform(new Vector2(.73f, .61f), new Vector2(.17f, .23f));
                pair.Compare(NativePixels, "independent-main-transform");
                pair.Mask(randomMask); pair.Compare(NativePixels, "live-mask-replacement");
                foreach (float cutoff in new[] { .8f, .51f, .25f, .07f })
                    foreach (float weight in new[] { .3f, 1f, 2.49f, 4.5f })
                    { pair.Set(cutoff, weight); pair.Compare(NativePixels); }
                pair.Depth.Image.color = pair.Reference.Image.color = new Color32(231, 173, 119, 137);
                pair.Depth.Group.alpha = pair.Reference.Group.alpha = .43f;
                pair.Set(.29f, 2.49f); pair.Compare(NativePixels, "native-tint-and-canvas-alpha");
                clone.SetColor("_Color", new Color(.17f, .04f, .73f, .13f));
                pair.Compare(NativePixels, "native-shader-ignores-ui-color");
                Check(pair.Depth.Clone.GetFloat("_GvrMenuDepthDissolve") == 1
                    && pair.Depth.Clone.GetFloat("_GvrMenuDepthSrcBlend") == 5,
                    "original dissolve identity selects its actual SrcAlpha blend pipeline");
                clone.SetColor("_Color", Color.white);
                // Ordinary UI's existing depth path assumes the original
                // identity ST. Native dissolve independently authored STs were
                // exercised above; do not broaden this repair's shader scope.
                pair.MainTransform(Vector2.one, Vector2.zero);
                pair.Depth.Source.shader = pair.Reference.Source.shader = Shader.Find("UI/Default");
                pair.Depth.Image.SetMaterialDirty(); pair.Reference.Image.SetMaterialDirty();
                pair.Compare("ordinary native UI remains unchanged when the source shader leaves dissolve", "ordinary-ui-after-native");
                Check(pair.Depth.Clone.GetFloat("_GvrMenuDepthDissolve") == 0
                    && pair.Depth.Clone.GetFloat("_GvrMenuDepthSrcBlend") == 1,
                    "the same native material changing shader releases the dissolve-specific blend mode");
            }
            ScalarOracle(reference);
            FloatingNativeAlpha(reference);
            Stencil(reference, sprite, nativeMask);
            RectClip(reference, nativeMask);
            TransparentDepth(reference);
        }
        finally
        {
            Object.DestroyImmediate(sprite); Object.DestroyImmediate(art);
            Object.DestroyImmediate(nativeMask); Object.DestroyImmediate(randomMask);
        }
        return _checks;
    }

    private static void ScalarOracle(Shader reference)
    {
        Color32 texel = new(153, 109, 67, 113), tint = new(205, 173, 241, 149);
        Texture2D image = Solid(texel), mask = Solid(new Color32(156, 0, 0, 255));
        Sprite sprite = Sprite.Create(image, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
        try
        {
            using (var pair = new Pair(reference, sprite, mask))
            {
                pair.Depth.Image.color = pair.Reference.Image.color = tint;
                foreach (float weight in new[] { .5f, 1f, 2.49f })
                    foreach (float cutoff in new[] { 0f, .2f, .4f, .612f, .7f, 1f })
                    {
                        pair.Set(cutoff, weight);
                        using (Pixels original = pair.Reference.Capture())
                        using (Pixels spatial = pair.Depth.Capture())
                        {
                            Color expected = Expected(texel, tint, 156f / 255f, cutoff, weight);
                            Check(Close(original.Center, expected, 2.01f / 255f),
                                "independent native scalar and SrcAlpha-on-alpha oracle matches the DXBC reference");
                            Check(Close(spatial.Center, expected, 2.01f / 255f),
                                "shipping shader preserves the independently derived native scalar and alpha blend");
                        }
                    }
            }
        }
        finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(image); Object.DestroyImmediate(mask); }
    }

    private static Color Expected(Color texel, Color tint, float mask, float cutoff, float edge)
    {
        Color background = Backing;
        if (mask < cutoff) return background;
        // Separate CPU oracle: the original SHDR arithmetic and the original
        // normalized-target blend state (SrcAlpha for alpha too). No shared
        // shipping/reference-shader function is used here.
        float opacity = ((mask - cutoff) * (texel.a * tint.a) / mask) * edge;
        float a = Mathf.Clamp01(opacity);
        return new Color(Mathf.Clamp01(texel.r * tint.r * a + background.r * (1 - a)),
            Mathf.Clamp01(texel.g * tint.g * a + background.g * (1 - a)),
            Mathf.Clamp01(texel.b * tint.b * a + background.b * (1 - a)),
            Mathf.Clamp01(a * a + background.a * (1 - a)));
    }

    private static void FloatingNativeAlpha(Shader reference)
    {
        Texture2D image = Solid(new Color32(153, 109, 67, 179)), mask = Solid(new Color32(156, 0, 0, 255));
        Sprite sprite = Sprite.Create(image, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
        try
        {
            using (var pair = new Pair(reference, sprite, mask, floating: true))
            {
                pair.Set(0, 2.49f);
                using (Pixels native = pair.Reference.Capture())
                using (Pixels depth = pair.Depth.Capture())
                {
                    Check(native.Center.a > 1.01f, "native shader alpha remains unsaturated before floating-target compositing");
                    Check(Close(native.Center, depth.Center, .0001f),
                        "depth shader retains native unclamped EdgeWeight alpha on a floating target");
                    File.WriteAllText(Path.Combine(_output, "floating-native-alpha.txt"),
                        "Reference=" + native.Center + "\nShipping=" + depth.Center + "\n");
                }
            }
        }
        finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(image); Object.DestroyImmediate(mask); }
    }

    private static void Stencil(Shader reference, Sprite sprite, Texture mask)
    {
        foreach (float weight in new[] { 1f, 2.49f })
        {
            using (var pair = new Pair(reference, sprite, mask))
            {
                pair.Set(.4f, weight); pair.Reference.AddMask(true); pair.Depth.AddMask(true);
                pair.Compare("native dissolve pixels and original stencil aperture retain parity", "native-stencil-" + weight);
                Check(pair.Depth.Clone.GetFloat("_StencilComp") == (float)UnityEngine.Rendering.CompareFunction.Equal,
                    "source-aware material updates retain the actual uGUI stencil comparison");
            }
        }
    }

    private static void RectClip(Shader reference, Texture mask)
    {
        Texture2D texture = Solid(new Color32(179, 127, 83, 255));
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
        try
        {
            using (var pair = new Pair(reference, sprite, mask))
            {
                // Opaque colour isolates the aperture: partially transparent
                // native dissolve intentionally uses SrcAlpha-on-alpha, while
                // UI/Default uses One. Those distinct pipelines are tested above.
                pair.Set(0, 1); pair.Reference.Source.shader = Shader.Find("UI/Default");
                pair.Depth.AddMask(false); pair.Reference.AddMask(false);
                pair.Compare("dissolve keeps the original VR RectMask aperture at fully revealed native opacity", "native-rect-aperture");
            }
        }
        finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
    }

    private static void TransparentDepth(Shader reference)
    {
        Texture2D image = Solid(new Color32(255, 0, 0, 255)), mask = Solid(new Color32(128, 0, 0, 255));
        Sprite sprite = Sprite.Create(image, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
        try
        {
            using (var pair = new Pair(reference, sprite, mask))
            {
                pair.Depth.AddBehind(); pair.Set(128f / 255f, 1);
                using (Pixels zero = pair.Depth.Capture("exact-zero-alpha-depth"))
                    Check(zero.Center.b > .95f && zero.Center.r < .05f,
                        "native exactly-zero dissolve alpha cannot occlude later artwork through the depth buffer");
                pair.Set(0, 1);
                using (Pixels opaque = pair.Depth.Capture("opaque-native-depth"))
                    Check(opaque.Center.r > .95f && opaque.Center.b < .05f,
                        "revealed native artwork still writes real depth and occludes later farther geometry");
            }
        }
        finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(image); Object.DestroyImmediate(mask); }
    }

    private static bool Close(Color a, Color b, float tolerance) => Mathf.Abs(a.r - b.r) <= tolerance
        && Mathf.Abs(a.g - b.g) <= tolerance && Mathf.Abs(a.b - b.b) <= tolerance && Mathf.Abs(a.a - b.a) <= tolerance;

    private sealed class Pair : IDisposable
    {
        internal readonly Pane Reference, Depth;
        internal Pair(Shader shader, Sprite sprite, Texture mask, bool floating = false)
        { Reference = new Pane(28, shader, sprite, mask, false, floating); Depth = new Pane(29, shader, sprite, mask, true, floating); }
        internal void Set(float cutoff, float edge)
        {
            foreach (Pane pane in new[] { Reference, Depth })
            { pane.Source.SetFloat("_Cutoff", cutoff); pane.Source.SetFloat("_EdgeWeight", edge); }
        }
        internal void Mask(Texture texture)
        { Reference.Source.SetTexture("_Mask", texture); Depth.Source.SetTexture("_Mask", texture); }
        internal void MaskTransform(Vector2 scale, Vector2 offset)
        {
            foreach (Pane pane in new[] { Reference, Depth })
            { pane.Source.SetTextureScale("_Mask", scale); pane.Source.SetTextureOffset("_Mask", offset); }
        }
        internal void MainTransform(Vector2 scale, Vector2 offset)
        {
            foreach (Pane pane in new[] { Reference, Depth })
            { pane.Source.SetTextureScale("_MainTex", scale); pane.Source.SetTextureOffset("_MainTex", offset); }
        }
        internal void Compare(string label, string? name = null)
        {
            // Borrow before the first global uGUI rebuild, as the actual private
            // eye capture does. A prior reference rebuild would accidentally
            // create the depth clone early and hide a missing post-copy sync.
            using (Pixels b = Depth.Capture(name == null ? null : name + "-depth"))
            using (Pixels a = Reference.Capture(name == null ? null : name + "-reference"))
            {
                Color[] first = a.Image.GetPixels(), second = b.Image.GetPixels();
                int bad = 0; float maximum = 0;
                for (int i = 0; i < first.Length; i++)
                {
                    if (!Close(first[i], second[i], 1.01f / 255f)) bad++;
                    maximum = Mathf.Max(maximum, Mathf.Abs(first[i].r - second[i].r), Mathf.Abs(first[i].g - second[i].g),
                        Mathf.Abs(first[i].b - second[i].b), Mathf.Abs(first[i].a - second[i].a));
                }
                File.AppendAllText(Path.Combine(_output, "pixel-comparisons.csv"), (name ?? "clock") + "," + bad + "," + maximum + "\n");
                Check(bad == 0, label);
            }
        }
        public void Dispose() { Depth.Dispose(); Reference.Dispose(); }
    }

    private sealed class Pixels : IDisposable
    {
        internal readonly Texture2D Image;
        internal Color Center => Image.GetPixel(64, 64);
        internal Pixels(Texture2D image) { Image = image; }
        public void Dispose() => Object.DestroyImmediate(Image);
    }
    private sealed class Pane : IDisposable
    {
        private readonly GameObject _root;
        private readonly Camera _camera;
        private readonly RenderTexture _target;
        private readonly MenuDepthGraphic? _graphic;
        private readonly bool _floating;
        internal readonly Image Image;
        internal readonly CanvasGroup Group;
        internal readonly Material Source;
        internal Material Clone => Image.canvasRenderer.GetMaterial(0);
        internal Pane(int layer, Shader reference, Sprite sprite, Texture mask, bool depth, bool floating)
        {
            _floating = floating;
            _root = new GameObject("original source image window", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)); _root.layer = layer;
            var rect = _root.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(128, 128); rect.localScale = Vector3.one * .01f;
            _root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; Group = _root.GetComponent<CanvasGroup>();
            var go = new GameObject("original animated Image", typeof(RectTransform)); go.layer = layer; go.transform.SetParent(_root.transform, false);
            Image = go.AddComponent<Image>(); Image.rectTransform.sizeDelta = new Vector2(128, 128); Image.sprite = sprite;
            Source = new Material(reference) { name = "original native source instance" }; Source.SetTexture("_Mask", mask); Image.material = Source;
            if (depth)
            { _graphic = go.AddComponent<MenuDepthGraphic>(); _graphic.Configure(Image, FlatField(sprite.texture.width, sprite.texture.height), .03f); }
            _camera = new GameObject("actual UI capture camera").AddComponent<Camera>(); _camera.enabled = false;
            _camera.transform.position = new Vector3(0, 0, -1); _camera.orthographic = true; _camera.orthographicSize = .64f;
            _camera.aspect = 1; _camera.nearClipPlane = .1f; _camera.farClipPlane = 3; _camera.cullingMask = 1 << layer;
            _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = Backing;
            _target = new RenderTexture(128, 128, 24, floating ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default); _target.Create(); _camera.targetTexture = _target;
        }
        internal Pixels Capture(string? name = null)
        {
            _graphic?.BeforeCapture(new MenuDepthEye(false, _camera.transform.position, _camera, true));
            try { Canvas.ForceUpdateCanvases(); _camera.Render(); }
            finally { _graphic?.AfterCapture(); }
            RenderTexture previous = RenderTexture.active; RenderTexture.active = _target;
            var pixels = new Texture2D(128, 128, _floating ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); pixels.Apply(); RenderTexture.active = previous;
            if (name != null && !_floating) File.WriteAllBytes(Path.Combine(_output, name + ".png"), pixels.EncodeToPNG());
            return new Pixels(pixels);
        }
        internal void AddMask(bool stencil)
        {
            var mask = new GameObject("native parent aperture", typeof(RectTransform)); mask.layer = _root.layer;
            mask.transform.SetParent(_root.transform, false); mask.GetComponent<RectTransform>().sizeDelta = new Vector2(72, 96);
            if (stencil) { mask.AddComponent<Image>(); mask.AddComponent<Mask>().showMaskGraphic = false; }
            else mask.AddComponent<RectMask2D>();
            Image.transform.SetParent(mask.transform, false); Image.SetAllDirty();
        }
        internal void AddBehind()
        {
            var go = new GameObject("later farther native artwork", typeof(RectTransform)); go.layer = _root.layer; go.transform.SetParent(_root.transform, false);
            var image = go.AddComponent<Image>(); image.rectTransform.sizeDelta = new Vector2(128, 128);
            image.rectTransform.localPosition = new Vector3(0, 0, 1); image.color = Color.blue;
        }
        public void Dispose()
        {
            Object.DestroyImmediate(_root); _target.Release(); Object.DestroyImmediate(_target); Object.DestroyImmediate(_camera.gameObject); Object.DestroyImmediate(Source);
        }
    }

    private static Texture2D Solid(Color32 color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        texture.SetPixel(0, 0, color); texture.Apply(); return texture;
    }
    private static Texture2D Artwork()
    {
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
        var pixels = new Color32[64 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) pixels[y * 64 + x] = new Color32((byte)(40 + x * 3),
            (byte)(210 - y * 2), (byte)(50 + y * 2), (byte)(40 + (x * 11 + y * 7) % 215));
        texture.SetPixels32(pixels); texture.Apply(); return texture;
    }
    private static Texture2D RandomMask()
    {
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, true, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
        var random = new System.Random(723); var pixels = new Color32[64 * 64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)random.Next(1, 255), (byte)random.Next(256), (byte)random.Next(256), 255);
        texture.SetPixels32(pixels); texture.Apply(true); return texture;
    }
    private static MenuDepthHeightfield FlatField(int sourceWidth, int sourceHeight)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { 71, 77, 68, 49 }); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write((ushort)2); writer.Write((ushort)2); writer.Write(3u); writer.Write(8); writer.Write(0u);
        writer.Write(sourceWidth); writer.Write(sourceHeight); for (int i = 0; i < 4; i++) writer.Write(ushort.MaxValue);
        byte[] data = stream.ToArray(); uint crc = uint.MaxValue;
        for (int i = 32; i < data.Length; i++)
        { crc ^= data[i]; for (int j = 0; j < 8; j++) crc = crc >> 1 ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
        Array.Copy(BitConverter.GetBytes(~crc), 0, data, 20, 4); return MenuDepthHeightfield.Read(data);
    }
}
