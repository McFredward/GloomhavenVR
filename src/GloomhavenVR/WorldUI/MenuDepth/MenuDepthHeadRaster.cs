using System;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI.MenuDepth;

// Build730: head-pixel captures have no perspective-dependent glass-plane crop.
// This scope covers a complete source preparation + original UI draw; model
// scratch may be reused by the next eye only after that draw has consumed it.
internal static class MenuDepthHeadRaster
{
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
    // At the maintainer's4040x4040 RGBA32/D24 raster these ceilings are1992.38MiB
    // and498.10MiB. NativeFlat separately owns at most one249.05MiB pair.
    // These are admission ceilings, not eager allocations or movement rate tiers.
    internal static long WindowBudget(RenderTextureDescriptor original, Vector2Int pixels)
        => Budget(Bytes(original, pixels), WindowSlots, 448L * 1024 * 1024);
    internal static long SceneBudget(RenderTextureDescriptor original, Vector2Int pixels)
        => Budget(SingleBytes(original, pixels), SceneSlots, 96L * 1024 * 1024);
    internal static long Budget(long bytes, int slots, long minimum)
        => bytes > long.MaxValue / slots ? long.MaxValue : Math.Max(minimum, bytes * slots);

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
