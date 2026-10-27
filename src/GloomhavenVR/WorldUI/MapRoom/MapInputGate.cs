namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// The native map interaction mask and the party's current public map decision.
/// VR icon dispatch and adopted travel controls otherwise bypass that full-screen blocker.
/// No native lock is acquired or released here. An unavailable manager supplies no
/// measured native lock; a separately validated public city/event can still own input.
/// </summary>
internal static class MapInputGate
{
    internal static bool PublicDecisionOwnsInput => MapCityEventReadyUp.BlocksQuestBrowsing;

    internal static bool IsBlocked => PublicDecisionOwnsInput
        || (Singleton<AdventureMapUIManager>.IsInitialized
            && IsBlockedBy(Singleton<AdventureMapUIManager>.Instance));

    internal static bool IsBlockedBy(AdventureMapUIManager? manager) =>
        manager != null && manager.IsLocked;
}
