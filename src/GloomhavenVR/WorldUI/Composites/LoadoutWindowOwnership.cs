using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Identifies the native party content reopened for the current loadout.</summary>
internal static class LoadoutWindowOwnership
{
    internal static bool IsCurrentContent(UIWindow? candidate)
    {
        if (candidate == null || !MapRoomDriver.Active || !Singleton<UILoadoutManager>.IsInitialized)
            return false;
        var loadout = Singleton<UILoadoutManager>.Instance;
        if (loadout == null || !loadout.IsOpen)
            return false;

        var party = NewPartyDisplayUI.PartyDisplay;
        var owner = party != null ? party.window : null;
        if (owner == null || !owner.IsOpen || party!.hideRequests.Contains(loadout))
            return false;

        // EnterLoadout hides this exact native window on behalf of the manager.
        // EnableLoadoutInteraction releases that request and reopens the same instance
        // for battle goals, cards and equipment. Its earlier curtain membership must
        // not outlive that change of purpose. A floated-window lookup would deadlock:
        // the curtain is precisely what prevents the required owner from floating.
        return ReferenceEquals(candidate, owner) || candidate.transform.IsChildOf(owner.transform);
    }
}
