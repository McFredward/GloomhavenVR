using System;

namespace GloomhavenVR.Core.SteamLink;

// The Build 701 fixed 3072 encoder request reduced a 4040-square carrier again,
// regardless of the selected eye resolution. Derive only the transport request
// from the current carrier's real dimensions; never write XR allocation,
// viewport, camera, render-quality configuration or a separate MR resolution.
// This removes that additional encoder ceiling. It does not undo the existing
// v3 atlas's 2048/3168 scene fraction or prove the negotiated video dimensions.
internal static class SteamLinkResolutionPolicy
{
    internal const int MaximumAxis = 16384;
    internal const int EncoderAlignment = 64;

    internal static bool TryWidth(int carrierWidth, int carrierHeight, out int targetWidth)
    {
        targetWidth = 0;
        if (carrierWidth <= 0 || carrierHeight <= 0
            || carrierWidth > MaximumAxis || carrierHeight > MaximumAxis)
            return false;

        int longest = Math.Max(carrierWidth, carrierHeight);
        targetWidth = (longest + EncoderAlignment - 1) / EncoderAlignment * EncoderAlignment;
        return true;
    }
}
