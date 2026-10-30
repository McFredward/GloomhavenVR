using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Bound the additional eye targets while preserving admitted native pixel density.</summary>
internal static class MenuDepthDesktopResources
{
    internal const long MaxOwnedBytes = 160L * 1024 * 1024;

    internal static bool Fits(RenderTextureDescriptor descriptor, int width, int height, bool expanded)
        => ExtraBytes(descriptor, width, height, expanded) <= MaxOwnedBytes;

    internal static long ExtraBytes(RenderTextureDescriptor descriptor, int width, int height, bool expanded)
    {
        if (width <= 0 || height <= 0) return long.MaxValue;
        try
        {
            checked
            {
                int layers = Math.Max(1, descriptor.volumeDepth);
                if (descriptor.dimension == TextureDimension.Cube) layers *= 6;
                int samples = Math.Max(1, descriptor.msaaSamples);
                long baseColor = Blocks(descriptor.graphicsFormat, width, height) * layers;
                long color = baseColor;
                if (descriptor.useMipMap)
                {
                    int mip = 1, w = width, h = height;
                    while ((w > 1 || h > 1) && (descriptor.mipCount <= 0 || mip < descriptor.mipCount))
                    {
                        w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
                        color += Blocks(descriptor.graphicsFormat, w, h) * layers; mip++;
                    }
                }
                long depth = descriptor.depthStencilFormat != GraphicsFormat.None
                    ? Blocks(descriptor.depthStencilFormat, width, height) * layers
                    : width * (long)height * layers * (descriptor.depthBufferBits == 0 ? 0 : descriptor.depthBufferBits <= 16 ? 2 : 4);
                // Non-bindMS textures also own the resolved sampled colour image.
                long target = samples > 1 ? baseColor * samples + (descriptor.bindMS ? 0 : color) + depth * samples : color + depth;
                return target * (expanded ? 2 : 1); // original normal UI target is the existing basis
            }
        }
        catch (OverflowException) { return long.MaxValue; }
    }

    private static long Blocks(GraphicsFormat format, int width, int height)
    {
        if (format == GraphicsFormat.None) return 0;
        long x = Math.Max(1, (int)GraphicsFormatUtility.GetBlockWidth(format));
        long y = Math.Max(1, (int)GraphicsFormatUtility.GetBlockHeight(format));
        return checked(((width + x - 1) / x) * ((height + y - 1) / y) * GraphicsFormatUtility.GetBlockSize(format));
    }

    internal static Rect RetainFrame(Rect current, Rect wanted, float now, ref float shrinkSince)
    {
        Rect union = Rect.MinMaxRect(Mathf.Min(current.xMin,wanted.xMin),Mathf.Min(current.yMin,wanted.yMin),
            Mathf.Max(current.xMax,wanted.xMax),Mathf.Max(current.yMax,wanted.yMax));
        if (union != current) { shrinkSince = -1f; return union; }
        bool substantiallySmaller = wanted.width <= current.width * .75f || wanted.height <= current.height * .75f;
        if (!substantiallySmaller) { shrinkSince = -1f; return current; }
        if (shrinkSince < 0f) shrinkSince = now;
        if (now - shrinkSince < 2f) return current;
        shrinkSince = -1f; return wanted;
    }
}
