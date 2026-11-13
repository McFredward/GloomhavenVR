using System;
using UnityEngine;

// This fixture runs desktop GL, without an Android player or owned pixel
// sidecar. The actual shared QuestTextureCopy implementation remains compiled:
// its desktop branch executes the original Graphics.Blit.
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform
    {
        internal static bool Enabled => false;
    }
}

namespace GloomhavenVR.Cards
{
    internal static class QuestCardPixels
    {
        internal static Color32[]? Read(Texture2D source)
            => throw new InvalidOperationException("Android-only card pixels reached a desktop laser fixture.");
    }
}
