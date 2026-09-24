using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Native narrative ownership; presentation alpha is not story progress.</summary>
internal static class NativeStoryWindow
{
    internal static bool IsStory(UIWindow? window)
    {
        if (window == null) return false;
        return (Singleton<MapStoryController>.IsInitialized
                && Singleton<MapStoryController>.Instance != null
                && ReferenceEquals(Singleton<MapStoryController>.Instance.window, window))
            || (Singleton<StoryController>.IsInitialized
                && Singleton<StoryController>.Instance != null
                && ReferenceEquals(Singleton<StoryController>.Instance.window, window));
    }

    // The last native Skip disables its button and runs the continuation before
    // hiding this window. Keeping its old text visible cannot advance it again.
    internal static bool IsCompleted(UIWindow? window) =>
        window != null && !window.IsOpen && IsStory(window);
}
