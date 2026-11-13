using System;
using UnityEngine;

// These are desktop GL fixtures, without an Android player or owned pixel
// sidecar. Keep the actual shared QuestTextureCopy implementation in the
// compiled source set: its desktop branch executes the original Graphics.Blit.
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
            => throw new InvalidOperationException("Android-only card pixels reached a desktop menu fixture.");
    }
}
