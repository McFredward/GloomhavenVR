using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class StereoVideoRenderProgram
{
    private static int _count;
    private static string _output = string.Empty;
    private static Camera _camera = null!;
    private static RawImage _image = null!;
    private static GameObject _canvasRoot = null!;
    private static RenderTexture _target = null!;
    private static StereoVideoSurface? _surface;
    private static readonly List<Texture2D> Textures = new List<Texture2D>();

    private static void Check(bool condition, string label)
    {
        _count++;
        if (!condition) throw new Exception(label);
    }

    public static int Run(string output)
    {
        _count = 0; _output = output;
        try
        {
            Check(StereoVideoSurface.EyeIndex(Camera.MonoOrStereoscopicEye.Left) == 0, "left eye routing");
            Check(StereoVideoSurface.EyeIndex(Camera.MonoOrStereoscopicEye.Right) == 1, "right eye routing");
            Check(StereoVideoSurface.EyeIndex(Camera.MonoOrStereoscopicEye.Mono) == 0, "mono left fallback");
            Check(StereoVideoSurface.EyeIndex((Camera.MonoOrStereoscopicEye)432) == 0, "unknown left fallback");
            Check(StereoVideoSurface.IsAvailable(out string reason), "actual embedded bundle loads: " + reason);
            Setup();
            var original = new Material(Shader.Find("UI/Default"));
            _image.material = original;
            Rect originalUv = new Rect(.1f, .2f, .6f, .5f);
            _image.uvRect = originalUv;
            Check(StereoVideoSurface.TryAttach(_image, out _surface, out reason), "surface attaches: " + reason);
            Check(_image.uvRect == new Rect(0, 0, 1, 1), "SBS attachment uses full packed UV domain");
            _image.texture = Packed(false, 1f);
            Color left = Render(Camera.MonoOrStereoscopicEye.Left, "left").GetPixel(160, 90);
            Color right = Render(Camera.MonoOrStereoscopicEye.Right, "right").GetPixel(160, 90);
            Check(left.r > .97f && left.g < .02f, "actual left pixels select left packed half");
            Check(right.g > .97f && right.r < .02f, "actual right pixels select right packed half");
            NestedCamera();
            Color mono = Render(Camera.MonoOrStereoscopicEye.Mono, "mono").GetPixel(160, 90);
            Check(mono.r > .97f && mono.g < .02f, "mono camera after right render resets to left picture");
            Color unknown = Render((Camera.MonoOrStereoscopicEye)432, "unknown").GetPixel(160, 90);
            Check(unknown.r > .97f && unknown.g < .02f, "unknown eye after right render uses left picture");
            Texture2D edge = Render(Camera.MonoOrStereoscopicEye.Left, "left-seam");
            Check(edge.GetPixel(279, 90).r > .97f && edge.GetPixel(279, 90).g < .02f,
                "bilinear seam never samples opposite eye");
            edge = Render(Camera.MonoOrStereoscopicEye.Right, "right-seam");
            Check(edge.GetPixel(40, 90).g > .97f && edge.GetPixel(40, 90).r < .02f,
                "right bilinear seam never samples opposite eye");
            _image.texture = Packed(true, 1f);
            _image.rectTransform.sizeDelta = new Vector2(16f, 16f / StereoVideoSurface.PerEyeAspect(32, 9));
            Texture2D geometry = Render(Camera.MonoOrStereoscopicEye.Left, "aspect");
            int minX = 320, maxX = -1, minY = 180, maxY = -1;
            for (int y = 0; y < 180; y++) for (int x = 0; x < 320; x++)
                if (geometry.GetPixel(x, y).b > .7f)
                { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            Check(maxX >= minX && Math.Abs((maxX - minX) - (maxY - minY)) <= 3,
                "per-eye aspect renders the source circle round");
            _image.texture = Packed(false, .5f);
            _image.color = new Color(1, 1, 1, .5f);
            _canvasRoot.AddComponent<CanvasGroup>().alpha = .5f;
            Color alpha = Render(Camera.MonoOrStereoscopicEye.Left, "alpha").GetPixel(160, 90);
            Check(Math.Abs(alpha.r - .125f) < .02f, "texture tint and CanvasGroup alpha compose correctly");
            Object.DestroyImmediate(_canvasRoot.GetComponent<CanvasGroup>());
            _image.color = Color.white;
            _image.texture = Packed(false, 1f);
            Masks();
            _surface!.Dispose(); _surface.Dispose(); _surface = null;
            Check(ReferenceEquals(_image.material, original) && _image.uvRect == originalUv,
                "dispose restores exact previous material and UV");
            Check((int)typeof(StereoVideoSurface).GetField("_liveSurfaces", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)! == 0,
                "last dispose removes shared camera subscription lease");
            Check(Shader.GetGlobalFloat("_GvrStereoVideoEye") == 0f, "last dispose resets deterministic left eye");
            Render(Camera.MonoOrStereoscopicEye.Right, "disposed");
            Check(Shader.GetGlobalFloat("_GvrStereoVideoEye") == 0f, "disposed surface leaves no camera callback behind");
            Object.DestroyImmediate(original);
            File.WriteAllText(Path.Combine(output, "assertions.txt"), _count + " assertions\n");
            return _count;
        }
        finally
        {
            _surface?.Dispose(); _surface = null;
            // Deliberate lifecycle mutants may leave a hook behind. Remove only this
            // private fixture assembly's callbacks after measuring the actual defect.
            Camera.onPreRender -= (Camera.CameraCallback)Delegate.CreateDelegate(typeof(Camera.CameraCallback),
                typeof(StereoVideoSurface).GetMethod("BeforeCamera", BindingFlags.NonPublic | BindingFlags.Static)!);
            Camera.onPostRender -= (Camera.CameraCallback)Delegate.CreateDelegate(typeof(Camera.CameraCallback),
                typeof(StereoVideoSurface).GetMethod("AfterCamera", BindingFlags.NonPublic | BindingFlags.Static)!);
            FieldInfo bank = typeof(StereoVideoSurface).GetField("_bundle", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (bank.GetValue(null) is AssetBundle bundle) bundle.Unload(true);
            if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); }
            foreach (Texture2D texture in Textures) Object.DestroyImmediate(texture);
            Textures.Clear();
        }
    }

    public static int RunUnavailable(string output)
    {
        _count = 0; _output = output;
        Check(!StereoVideoSurface.IsAvailable(out string reason) && reason.Length != 0,
            "unavailable renderer rejects stereo before native media selection");
        Setup();
        Material original = _image.material;
        Rect uv = new Rect(.1f, .2f, .3f, .4f);
        _image.uvRect = uv;
        Texture2D texture = Packed(false, 1f);
        _image.texture = texture;
        Check(!StereoVideoSurface.TryAttach(_image, out StereoVideoSurface? surface, out reason) && surface == null,
            "unavailable renderer refuses attachment");
        Check(ReferenceEquals(_image.material, original) && _image.uvRect == uv && _image.texture == texture,
            "failed attachment preserves original material UV and decoder texture");
        Check((int)typeof(StereoVideoSurface).GetField("_liveSurfaces", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)! == 0,
            "failed attachment never subscribes shared camera callback");
        _target.Release(); Object.DestroyImmediate(_target);
        foreach (Texture2D owned in Textures) Object.DestroyImmediate(owned);
        Textures.Clear();
        File.WriteAllText(Path.Combine(output, "assertions.txt"), _count + " assertions\n");
        return _count;
    }

    private static void Setup()
    {
        _camera = new GameObject("Stereo fixture camera").AddComponent<Camera>();
        _camera.enabled = false;
        _camera.transform.position = new Vector3(0, 0, -10);
        _camera.orthographic = true; _camera.orthographicSize = 6; _camera.aspect = 320f / 180;
        _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = Color.black;
        _camera.allowHDR = false; _camera.allowMSAA = false;
        _target = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        _target.Create(); _camera.targetTexture = _target;
        _canvasRoot = new GameObject("Actual world-space UI", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = _canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = _camera;
        ((RectTransform)_canvasRoot.transform).sizeDelta = new Vector2(16, 9);
        var content = new GameObject("Movie", typeof(RectTransform)); content.transform.SetParent(_canvasRoot.transform, false);
        _image = content.AddComponent<RawImage>(); _image.rectTransform.sizeDelta = new Vector2(16, 9);
    }

    private static Texture2D Packed(bool circle, float alpha)
    {
        var texture = new Texture2D(32, 9, TextureFormat.RGBA32, false, true)
            { filterMode = circle ? FilterMode.Point : FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < 9; y++) for (int x = 0; x < 32; x++)
        {
            Color color = x < 16 ? Color.red : Color.green;
            if (circle && (x % 16 - 8f) * (x % 16 - 8f) + (y - 4f) * (y - 4f) <= 6.25f) color = Color.blue;
            color.a = alpha; texture.SetPixel(x, y, color);
        }
        texture.Apply(); Textures.Add(texture); return texture;
    }

    private static Texture2D Render(Camera.MonoOrStereoscopicEye eye, string name)
    {
        StereoEyeFixture.ActiveEye = eye;
        Canvas.ForceUpdateCanvases(); _camera.Render();
        RenderTexture previous = RenderTexture.active; RenderTexture.active = _target;
        var pixels = new Texture2D(320, 180, TextureFormat.RGBA32, false, true);
        pixels.ReadPixels(new Rect(0, 0, 320, 180), 0, 0); pixels.Apply(); RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(_output, name + ".png"), pixels.EncodeToPNG());
        Textures.Add(pixels);
        return pixels;
    }

    private static void Masks()
    {
        var parent = new GameObject("Original mask parent", typeof(RectTransform));
        parent.transform.SetParent(_canvasRoot.transform, false);
        ((RectTransform)parent.transform).sizeDelta = new Vector2(8, 9);
        _image.transform.SetParent(parent.transform, false);
        Image stencilShape = parent.AddComponent<Image>();
        Mask mask = parent.AddComponent<Mask>(); mask.showMaskGraphic = false;
        Texture2D stencil = Render(Camera.MonoOrStereoscopicEye.Right, "stencil");
        Check(stencil.GetPixel(160, 90).g > .97f && stencil.GetPixel(65, 90).g < .02f,
            "native UI stencil hides only outside original mask");
        Object.DestroyImmediate(mask); Object.DestroyImmediate(stencilShape);
        RectMask2D rect = parent.AddComponent<RectMask2D>();
        Texture2D clipped = Render(Camera.MonoOrStereoscopicEye.Left, "rect-clip");
        Check(clipped.GetPixel(160, 90).r > .97f && clipped.GetPixel(65, 90).r < .02f,
            "native RectMask2D clips original UI extent");
        rect.softness = new Vector2Int(10, 10);
        Texture2D soft = Render(Camera.MonoOrStereoscopicEye.Left, "rect-softness");
        float feather = soft.GetPixel(102, 90).r;
        Check(feather > .02f && feather < .9f, "native RectMask2D softness preserves feathered boundary");
        Object.DestroyImmediate(rect);
        _image.transform.SetParent(_canvasRoot.transform, false);
        Object.DestroyImmediate(parent);
    }

    private static void NestedCamera()
    {
        Camera auxiliary = new GameObject("Nested mono camera").AddComponent<Camera>();
        auxiliary.enabled = false;
        auxiliary.transform.position = new Vector3(0, 0, -10);
        var target = new RenderTexture(16, 16, 24);
        target.Create(); auxiliary.targetTexture = target;
        Camera.CameraCallback nested = camera =>
        {
            if (camera != _camera) return;
            Camera.MonoOrStereoscopicEye outerEye = StereoEyeFixture.ActiveEye;
            StereoEyeFixture.ActiveEye = Camera.MonoOrStereoscopicEye.Mono;
            auxiliary.Render();
            StereoEyeFixture.ActiveEye = outerEye;
        };
        // Subscribe after the production eye hook, exactly where another system can
        // render an auxiliary camera after the right eye was selected for the head.
        Camera.onPreRender += nested;
        try
        {
            Color pixel = Render(Camera.MonoOrStereoscopicEye.Right, "nested-mono-return").GetPixel(160, 90);
            Check(pixel.g > .97f && pixel.r < .02f,
                "nested mono camera restores outer right-eye pixels before outer draw");
        }
        finally
        {
            Camera.onPreRender -= nested;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(auxiliary.gameObject);
        }
    }
}
