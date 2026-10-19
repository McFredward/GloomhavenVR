using HarmonyLib;
using UnityEngine.Video;

namespace GloomhavenVR.WorldUI;

// These seams precede the original Play/Prepare. Never borrow PromotionVideoHandler
// for review: its coroutine writes the show counter and PlayVideo changes global audio.
[HarmonyPatch(typeof(IntroPlayer), "ShowVideo")]
internal static class NativeStartupMoviePatch
{
    [HarmonyPrefix]
    internal static void Prefix(VideoPlayer ____player) => NativeVideoMovies.SetEmbeddedSource(____player);
}

