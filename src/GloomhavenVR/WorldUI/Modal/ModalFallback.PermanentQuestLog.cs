using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    private static UIWindow? _permanentQuestLog;

    // Build 530: keep the original Guildmaster list enrolled during map browsing, even
    // when native UI hides it before its first Show. Genuine quest commitment still hides it.
    private static bool GuildmasterMapVisible
    {
        get
        {
            if (!MapRoom.MapRoomDriver.Active) return false;
            var state = MapRuleLibrary.Adventure.AdventureState.MapState;
            var parchment = MapRoom.MapRoomDriver.ParchmentRenderer;
            return state != null && !state.IsCampaign && parchment != null
                && parchment.enabled && parchment.gameObject.activeInHierarchy;
        }
    }

    private static bool IsStandingGuildmasterQuestLog(UIWindow? window) =>
        GuildmasterMapVisible && !StoryComposite.PointOfNoReturn && IsQuestLogWindow(window);

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
        if (ReferenceEquals(window, _permanentQuestLog) && !IsStandingGuildmasterQuestLog(window))
            ResetPermanentQuestLog();
    }

    private static void TickPermanentQuestLog()
    {
        bool guildmaster = GuildmasterMapVisible;
        if (guildmaster && _permanentQuestLog == null)
        {
            // Discover the original even if it was native-hidden before its first Show event.
            // No native Show/Hide callbacks or hide-request collections are changed.
            var manager = QuestManager.Instance;
            if (manager != null && manager.questLog != null)
                _permanentQuestLog = manager.questLog.GetComponent<UIWindow>();
        }
        UIWindow? window = _permanentQuestLog;
        if (!MapRoom.MapRoomDriver.Active || window == null)
        {
            ResetPermanentQuestLog();
            return;
        }

        var state = MapRuleLibrary.Adventure.AdventureState.MapState;
        if (state != null && !state.IsCampaign && !guildmaster)
            return;

        // Both modes retain the real quest/story/loadout curtain. StoryComposite now distinguishes
        // an ordinary Guildmaster dialog from the story after actual quest confirmation.
        if (StoryComposite.PointOfNoReturn || FloatRefusalTable.Refuses(window))
            return;

        // Campaign consumes one deferred return; Guildmaster retains the same original while
        // the map stands. Existing conversion enrollment deduplicates already-floated windows.
        if (WorldUIConfig.ConversionActive)
            AddPollWindow(window);
    }
}
