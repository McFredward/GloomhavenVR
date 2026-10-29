using System;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI.MenuDepth;

// Build730: head-pixel captures have no perspective-dependent glass-plane crop.
// This scope covers a complete source preparation + original UI draw; model
// scratch may be reused by the next eye only after that draw has consumed it.
internal static class MenuDepthHeadRaster
{
    internal const int SamplesPerAxis = 2;
    internal const int WindowSlots = 8, SceneSlots = 4;
    internal static bool Active { get; private set; }
    internal static Vector2Int Pixels { get; private set; }
    internal static Vector2Int Size(Camera head)
    {
        if (head.targetTexture != null)
            return new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(head.targetTexture.width * head.rect.width)),
                Mathf.Max(1, Mathf.CeilToInt(head.targetTexture.height * head.rect.height)));
        if (head.stereoEnabled && XRSettings.enabled)
            return new Vector2Int(Mathf.Max(1, XRSettings.eyeTextureWidth), Mathf.Max(1, XRSettings.eyeTextureHeight));
        return new Vector2Int(Mathf.Max(1, head.scaledPixelWidth), Mathf.Max(1, head.scaledPixelHeight));
    }
    internal static RenderTextureDescriptor Descriptor(RenderTextureDescriptor original, Vector2Int size)
    {
        original.width = size.x; original.height = size.y;
        original.msaaSamples = 1; original.bindMS = false;
        original.useMipMap = false; original.autoGenerateMips = false; original.mipCount = 1;
        original.useDynamicScale = false; original.memoryless = RenderTextureMemoryless.None;
        original.depthBufferBits = 24; original.volumeDepth = 1;
        original.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        original.vrUsage = VRTextureUsage.None;
        return original;
    }
    internal static bool Matches(RenderTexture target, RenderTextureDescriptor original, Vector2Int size)
    {
        RenderTextureDescriptor wanted = Descriptor(original, size), actual = target.descriptor;
        return target.IsCreated() && actual.width == wanted.width && actual.height == wanted.height
            && actual.graphicsFormat == wanted.graphicsFormat && actual.depthStencilFormat == wanted.depthStencilFormat
            && actual.msaaSamples == 1 && !actual.bindMS && !actual.useMipMap && !actual.useDynamicScale
            && actual.dimension == wanted.dimension && actual.volumeDepth == 1 && actual.vrUsage == VRTextureUsage.None;
    }
    internal static long SingleBytes(RenderTextureDescriptor original, Vector2Int size)
    {
        if (size.x <= 0 || size.y <= 0) return long.MaxValue;
        try
        {
            original = Descriptor(original, size);
            return checked(Blocks(original.graphicsFormat, size) + Blocks(original.depthStencilFormat, size));
        }
        catch (OverflowException) { return long.MaxValue; }
    }
    private static long Blocks(UnityEngine.Experimental.Rendering.GraphicsFormat format, Vector2Int size)
    {
        if (format == UnityEngine.Experimental.Rendering.GraphicsFormat.None) return 0;
        long bw = Math.Max(1L, UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockWidth(format));
        long bh = Math.Max(1L, UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockHeight(format));
        long block = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(format);
        return checked(((size.x + bw - 1) / bw) * ((size.y + bh - 1) / bh) * block);
    }
    internal static long Bytes(RenderTextureDescriptor original, Vector2Int size)
    {
        long one = SingleBytes(original, size);
        return one > long.MaxValue / 2 ? long.MaxValue : one * 2;
    }
    // Head pairs are separate from the ordinary448MiB mono photograph budget.
    // Eight active window slots and four one-eye scene scratches bound the sum.
    // At4040x4040 RGBA32/D24, eight1x pairs cost at most1992.38MiB;
    // NativeFlat separately owns one249.05MiB pair. Fixed2x scene scratches
    // cost at most498.10MiB each, four slots; one shared UI scratch has that
    // same maximum. Conservative occupied regions normally require far less.
    // These are admission ceilings, not eager allocations or movement rate tiers.
    internal static long WindowBudget(RenderTextureDescriptor original, Vector2Int pixels)
        => Budget(Bytes(original, pixels), WindowSlots, 448L * 1024 * 1024);
    internal static long SceneBudget(RenderTextureDescriptor original, Vector2Int pixels)
        => Budget(SingleBytes(original, pixels), SceneSlots, 96L * 1024 * 1024);
    internal static long Budget(long bytes, int slots, long minimum)
        => bytes > long.MaxValue / slots ? long.MaxValue : Math.Max(minimum, bytes * slots);

    // Build742: the stable1x head captures bypassed the ordinary windows'
    // supersampling. Integrate a fixed2x grid into those same1x head pairs. Only
    // storage is cropped: absolute quarter-pixel sample positions never follow
    // a moving silhouette. Uncertain volumes keep the complete head grid.
    internal static Rect ClipBounds(Matrix4x4 clip, Bounds local)
    {
        Vector3 min = local.min, max = local.max;
        float x0 = 1f, y0 = 1f, x1 = -1f, y1 = -1f;
        for (int i = 0; i < 8; i++)
        {
            Vector4 p = clip * new Vector4((i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z, 1f);
            if (!(p.w > 1e-6f) || float.IsNaN(p.w) || float.IsInfinity(p.w)) return new Rect(-1, -1, 2, 2);
            float x = p.x / p.w, y = p.y / p.w;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return new Rect(-1, -1, 2, 2);
            x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
            y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    internal static RectInt PixelRegion(Rect clip, Vector2Int head)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Clamp(clip.xMin, -1f, 1f) * .5f + .5f) * head.x) - 2, 0, head.x - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Clamp(clip.yMin, -1f, 1f) * .5f + .5f) * head.y) - 2, 0, head.y - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Clamp(clip.xMax, -1f, 1f) * .5f + .5f) * head.x) + 2, x0 + 1, head.x);
        int y1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Clamp(clip.yMax, -1f, 1f) * .5f + .5f) * head.y) + 2, y0 + 1, head.y);
        return new RectInt(x0, y0, x1 - x0, y1 - y0);
    }

    internal static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
        Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    private static RenderTexture? _filterScratch;
    private static Material? _filterMaterial;
    private static readonly int ResolveDepthId = Shader.PropertyToID("_GvrResolveDepth");
    private static readonly int ResolveStepId = Shader.PropertyToID("_GvrResolveStep");
    private static readonly int ResolveUvId = Shader.PropertyToID("_GvrResolveUV");
    private static readonly int ResolveRectId = Shader.PropertyToID("_GvrResolveRect");
    private static bool _filterBorrowed, _filterReleasePending;
    internal static bool FilterBorrowed => _filterBorrowed;
    private static float _filterShrinkSince = -1f;
    internal static long FilterBytes => _filterScratch != null
        ? SingleBytes(_filterScratch.descriptor, new Vector2Int(_filterScratch.width, _filterScratch.height)) : 0;

    internal static Rect HeadViewport(Camera? current, Vector2Int head)
        => current != null && current.targetTexture != null ? current.pixelRect : new Rect(0, 0, head.x, head.y);

    private static int FilterCapacity(int required, int ceiling)
    {
        // Grow through64/96/128/192/... instead of reallocating every64 head
        // pixels during a near approach. Storage never exceeds the real head;
        // larger capacity leaves absolute2x sample spacing unchanged.
        int bucket = 64;
        while (bucket < required && bucket < ceiling)
        {
            int power = Mathf.NextPowerOfTwo(bucket + 1);
            int middle = power / 2 + power / 4;
            bucket = middle > bucket ? middle : power;
        }
        return Mathf.Min(bucket, ceiling);
    }

    internal static FilterLease Filter(RenderTextureDescriptor original, Vector2Int head, RectInt region, Shader shader)
    {
        if (_filterBorrowed) throw new InvalidOperationException("A nested head antialias draw is not owned.");
        int width = FilterCapacity(region.width, head.x);
        int height = FilterCapacity(region.height, head.y);
        Vector2Int needed = new(width * SamplesPerAxis, height * SamplesPerAxis);
        if (needed.x > SystemInfo.maxTextureSize || needed.y > SystemInfo.maxTextureSize)
            throw new InvalidOperationException("The fixed antialias head grid exceeds the graphics texture limit.");
        Vector2Int capacity = needed;
        if (_filterScratch != null)
        {
            bool smaller = needed.x < _filterScratch.width * .75f || needed.y < _filterScratch.height * .75f;
            if (smaller && _filterShrinkSince < 0f) _filterShrinkSince = Time.unscaledTime;
            if (!smaller) _filterShrinkSince = -1f;
            if (!smaller || Time.unscaledTime - _filterShrinkSince < 1.5f)
                capacity = Vector2Int.Max(needed, new Vector2Int(_filterScratch.width, _filterScratch.height));
        }
        if (_filterScratch == null || !Matches(_filterScratch, original, capacity))
        {
            if (_filterScratch != null) { _filterScratch.Release(); UnityEngine.Object.Destroy(_filterScratch); }
            _filterScratch = new RenderTexture(Descriptor(original, capacity))
            {
                name = "GloomhavenVR.MenuDepth.HeadAntialias", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            if (!_filterScratch.Create())
            {
                UnityEngine.Object.Destroy(_filterScratch); _filterScratch = null;
                throw new InvalidOperationException("The fixed antialias scratch was refused.");
            }
            _filterShrinkSince = -1f;
        }
        _filterMaterial ??= new Material(shader) { name = "GloomhavenVR.MenuDepth.HeadResolve", hideFlags = HideFlags.HideAndDontSave };
        _filterBorrowed = true;
        return new FilterLease(head, region, _filterScratch, _filterMaterial);
    }

    internal static void ReleaseFilter()
    {
        // The last native source can close inside a camera callback. Finish its
        // source/camera restoration before disposing a surface still being used.
        if (_filterBorrowed) { _filterReleasePending = true; return; }
        if (_filterScratch != null) { _filterScratch.Release(); UnityEngine.Object.Destroy(_filterScratch); _filterScratch = null; }
        if (_filterMaterial != null) { UnityEngine.Object.Destroy(_filterMaterial); _filterMaterial = null; }
        _filterReleasePending = false; _filterShrinkSince = -1f;
    }

    private static void DrawResolve(Vector2Int head, Material material)
    {
        GL.Viewport(new Rect(0, 0, head.x, head.y));
        if (!material.SetPass(0)) throw new InvalidOperationException("The fixed head antialias resolve pass is unavailable.");
        GL.Begin(GL.QUADS);
        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.End();
    }

    internal readonly struct FilterLease : IDisposable
    {
        internal readonly RenderTexture Target;
        internal readonly Vector2Int Pixels;
        private readonly Vector2Int _head;
        private readonly RectInt _region;
        private readonly Rect _restoreViewport;
        private readonly Material _material;

        internal FilterLease(Vector2Int head, RectInt region, RenderTexture target, Material material)
        {
            Target = target; Pixels = new Vector2Int(target.width, target.height);
            _head = head; _region = region; _material = material;
            _restoreViewport = HeadViewport(Camera.current, head);
        }

        internal MenuDepthEye Eye(MenuDepthEye original)
        {
            float width = Pixels.x / (float)SamplesPerAxis, height = Pixels.y / (float)SamplesPerAxis;
            Matrix4x4 crop = Matrix4x4.identity;
            crop.m00 = _head.x / width; crop.m03 = (_head.x - 2f * _region.x - width) / width;
            crop.m11 = _head.y / height; crop.m13 = (_head.y - 2f * _region.y - height) / height;
            return new MenuDepthEye(original.Right, original.WorldPosition, original.HeadCamera, original.ProjectToGraphicPlane,
                new Vector2(Pixels.x, Pixels.y), crop * original.WorldToHeadClip);
        }

        internal void Resolve(RenderTexture destination)
        {
            RenderTexture? previous = RenderTexture.active;
            RenderBuffer previousColor = Graphics.activeColorBuffer, previousDepth = Graphics.activeDepthBuffer;
            bool srgb = GL.sRGBWrite;
            try
            {
                Graphics.SetRenderTarget(destination); GL.Clear(true, true, Color.clear);
                GL.sRGBWrite = destination.sRGB;
                _material.mainTexture = Target;
                _material.SetTexture(ResolveDepthId, Target, UnityEngine.Rendering.RenderTextureSubElement.Depth);
                _material.SetFloat(ResolveStepId, SamplesPerAxis > 1 ? .5f : 0f);
                _material.SetVector(ResolveUvId, new Vector4(_region.width * (float)SamplesPerAxis / Pixels.x,
                    _region.height * (float)SamplesPerAxis / Pixels.y, 0, 0));
                _material.SetVector(ResolveRectId, new Vector4(_region.x / (float)_head.x, _region.y / (float)_head.y,
                    _region.width / (float)_head.x, _region.height / (float)_head.y));
                DrawResolve(_head, _material);
            }
            finally
            {
                GL.sRGBWrite = srgb; RenderTexture.active = previous;
                Graphics.SetRenderTarget(previousColor, previousDepth); GL.Viewport(_restoreViewport);
            }
        }

        public void Dispose()
        {
            _filterBorrowed = false;
            if (_filterReleasePending) ReleaseFilter();
        }
    }

    internal static Lease Begin(Vector2Int pixels)
    {
        if (Active) throw new InvalidOperationException("Nested head-raster preparation is not owned.");
        Active = true; Pixels = pixels; return new Lease();
    }
    internal readonly struct Lease : IDisposable
    {
        public void Dispose() { Active = false; Pixels = default; }
    }
}
