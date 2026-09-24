using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    /// <summary>Give a new native story float its shared spawn seat after its predecessor closed.</summary>
    private static void RearmFreshStoryAnchor(UIWindow window)
    {
        // Build 555 now releases completed native stories. SharedAnchorSpent outlived
        // that float until whole-room teardown, so a later story fell back to each
        // viewer's gaze while the new content had no pose publisher yet. Rearm only
        // before a fresh conversion; pages, final-fit replay and existing transforms
        // retain their current pose, including a manually held composite host.
        if (!window.IsOpen || !NativeStoryWindow.IsStory(window)) return;
        SharedWindowKind kind = SharedWindows.KindOf(window);
        if ((kind != SharedWindowKind.MapStory && kind != SharedWindowKind.ScenarioStory)
            || !SharedWindows.ParticipatesHere(kind)) return;
        foreach (WindowPanel existing in Converted)
            if (existing.Panel.IsAlive && (ReferenceEquals(existing.Window, window)
                || SharedWindows.KindOf(existing.Window) == kind)) return;

        SharedAnchorSpent.Remove(kind);
        SharedAnchorSpentWhy.Remove(kind);
    }
}
