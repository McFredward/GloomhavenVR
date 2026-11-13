using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Filter the existing Quest menu capture when the eye minifies it.</summary>
internal static class QuestScreenSampling
{
    internal static void Configure(RenderTexture target)
    {
        if (!QuestStandalonePlatform.Enabled)
            return;

        // B618 hardware: the eye is 1680x1760 and the menu capture is 4128x2208.
        // MSAA 4x already runs on the eyes, but it cannot filter text/frame detail
        // inside a minified texture. The previous mipless Bilinear capture reads
        // only four level-zero texels, letting subpixel strokes shimmer. Keep the
        // raster, native UI and eye settings; filter the completed capture instead.
        // Automatic generation follows camera rendering and shifted-video blits,
        // so animated UI never samples an older mip level. This adds one third of
        // the colour target's storage, without a second capture or resolve target.
        target.useMipMap = true;
        target.autoGenerateMips = true;
        target.filterMode = FilterMode.Trilinear;
        target.wrapMode = TextureWrapMode.Clamp;
        target.anisoLevel = 8;
    }
}
