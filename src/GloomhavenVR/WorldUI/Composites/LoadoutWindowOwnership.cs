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
        if (ReferenceEquals(candidate, owner) || candidate.transform.IsChildOf(owner.transform))
            return true;
        if (candidate.ID != UIWindowID.PartyPanel)
            return false;

        // Build 501 hardware logs show two different native windows: the floated
        // "New Party display" (PartyPanel) contains "Party Display UI ", whose
        // NewPartyDisplayUI.window is the inner owner above. The curtain captured
        // the outer window before loadout started. Admitting only the inner owner
        // and its children therefore never released the refused native container.
        // Admit its nearest PartyPanel window by CURRENT hierarchy and identity,
        // not every ancestor or every window sharing the ID. A common canvas or
        // an unrelated party panel must not escape the story curtain. No cache:
        // native replacement/reparenting must revoke yesterday's ownership.
        for (var node = owner.transform; node != null; node = node.parent)
        {
            var container = node.GetComponent<UIWindow>();
            if (container != null && container.ID == UIWindowID.PartyPanel)
                return ReferenceEquals(candidate, container);
        }
        return false;
    }
}
