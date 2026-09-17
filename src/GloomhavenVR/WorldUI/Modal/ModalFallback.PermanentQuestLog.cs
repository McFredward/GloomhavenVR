using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    private static UIWindow? _permanentQuestLog;

    /// <summary>
    /// Remember the original quest list when a temporary refusal releases its successful float.
    /// Native flat UI hides it on quest selection, story, rewards and city events. VR's existing
    /// sticky window policy keeps it standing during browsing, but story/journey curtains may
    /// temporarily release the entire WindowPanel, including that policy's Sticky flag.
    ///
    /// Build-527 Guildmaster logs show that release at 263/1495/3048 and refusal at
    /// 297/1528/3081. Hide also removes the window from the ordinary open-window trackers.
    /// A curtain ending therefore cannot restore a native-closed list: there is no new Show
    /// event to enroll it. Remember that specific temporary withdrawal independently of native
    /// IsOpen, then resume the existing original-widget presentation when the curtain ends.
    /// No native Show/Hide callback, quest state or hide-request collection is changed.
    /// </summary>
    private static void RememberWithdrawnQuestLog(UIWindow window)
    {
        if (!MapRoom.MapRoomDriver.Active || !IsQuestLogWindow(window))
            return;
        _permanentQuestLog = window;
    }

    private static void ResetPermanentQuestLog()
    {
        _permanentQuestLog = null;
    }

    private static void CompletePermanentQuestLogReturn(UIWindow window)
    {
        if (ReferenceEquals(window, _permanentQuestLog))
            ResetPermanentQuestLog();
    }

    private static void TickPermanentQuestLog()
    {
        UIWindow? window = _permanentQuestLog;
        if (!MapRoom.MapRoomDriver.Active || window == null)
        {
            ResetPermanentQuestLog();
            return;
        }

        // Keep the approved story/loadout/travel exclusion until normal browsing resumes.
        if (StoryComposite.PointOfNoReturn || FloatRefusalTable.Refuses(window))
            return;

        // A successful re-conversion consumes this one return. Empty-content/liveness releases
        // outside a curtain must not become an unconditional per-frame resurrection loop.
        if (WorldUIConfig.ConversionActive)
            AddPollWindow(window);
    }
}
