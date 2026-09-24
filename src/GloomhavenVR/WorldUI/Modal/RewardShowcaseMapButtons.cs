namespace GloomhavenVR.WorldUI;

internal static partial class RewardShowcase
{
    private static UIGuildmasterAdventureRewardsManager? _wiredAdventureRewards;
    private static UIUnlockLocationFlowManager? _wiredUnlockLocations;

    /// <summary>
    /// These map continuations are separate from the scenario showcase. Both native Awake
    /// methods omit their mouse listener when initialized in gamepad mode. A VR pointer then
    /// presses a real, visibly enabled button without entering its native continuation if
    /// those windows predate VR activation. InputModeGuard prevents the ordinary in-VR
    /// creation path from entering gamepad mode; this is recovery for the earlier lifecycle,
    /// not evidence of the cause of the reported post-quest hardware hang. The
    /// unlock flow retains a pending promise and disabled camera input; Guildmaster rewards
    /// never reach their unlock videos and onClosed callback. Repair only the missing input
    /// binding, including when no campaign/scenario reward window exists. Native button
    /// availability, reveal animations, callbacks and multiplayer rules remain in charge.
    /// </summary>
    private static void TickMapRewardButtons(bool enabled)
    {
        if (!enabled)
        {
            _wiredAdventureRewards = null;
            _wiredUnlockLocations = null;
            return;
        }

        var adventure = Singleton<UIAdventureRewardsManager>.IsInitialized
            ? Singleton<UIAdventureRewardsManager>.Instance as UIGuildmasterAdventureRewardsManager : null;
        if (adventure != null && adventure != _wiredAdventureRewards && adventure.closeButton != null)
        {
            adventure.closeButton.onClick.RemoveListener(adventure.Hide);
            adventure.closeButton.onClick.AddListener(adventure.Hide);
            _wiredAdventureRewards = adventure;
        }

        var locations = Singleton<UIUnlockLocationFlowManager>.IsInitialized
            ? Singleton<UIUnlockLocationFlowManager>.Instance : null;
        if (locations != null && locations != _wiredUnlockLocations && locations.continueButton != null)
        {
            locations.continueButton.onClick.RemoveListener(locations.Continue);
            locations.continueButton.onClick.AddListener(locations.Continue);
            _wiredUnlockLocations = locations;
        }
    }
}
