namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// The native map interaction mask, read without depending on its original 2D hierarchy.
/// VR icon dispatch and adopted travel controls otherwise bypass that full-screen blocker.
/// No lock is acquired or released here; an unavailable manager supplies no measured lock.
/// </summary>
internal static class MapInputGate
{
    internal static bool IsBlocked => Singleton<AdventureMapUIManager>.IsInitialized
        && IsBlockedBy(Singleton<AdventureMapUIManager>.Instance);

    internal static bool IsBlockedBy(AdventureMapUIManager? manager) =>
        manager != null && manager.IsLocked;
}
