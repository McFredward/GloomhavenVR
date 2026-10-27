using System;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI.MenuDepth;

// Build730: head-pixel captures have no perspective-dependent glass-plane crop.
// This scope covers a complete source preparation + original UI draw; model
// scratch may be reused by the next eye only after that draw has consumed it.
internal static class MenuDepthHeadRaster
{
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
        original.msaaSamples = 1; original.useMipMap = false; original.autoGenerateMips = false;
        original.depthBufferBits = 24; original.volumeDepth = 1;
        original.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
        original.vrUsage = VRTextureUsage.None;
        return original;
    }
    internal static long Bytes(RenderTextureDescriptor original, Vector2Int size)
    {
        try
        {
            checked
            {
                original = Descriptor(original, size);
                var format = original.graphicsFormat;
                long bw = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockWidth(format);
                long bh = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockHeight(format);
                long block = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(format);
                long color = ((size.x + bw - 1) / bw) * ((size.y + bh - 1) / bh) * block;
                return 2 * (color + size.x * (long)size.y * 4);
            }
        }
        catch (OverflowException) { return long.MaxValue; }
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
