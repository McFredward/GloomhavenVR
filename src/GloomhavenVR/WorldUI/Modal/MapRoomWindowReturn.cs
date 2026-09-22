using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Recover the two original permanent map windows after a presentation switch. Native
/// Show events are not replayed by a 2D/3D toggle; a closed flat window and an exhausted
/// catch-all churn counter therefore cannot be used as proof that the VR window is unwanted.
/// Only enrollment is repaired: native callbacks, tutorial unlocks and quest flow stay intact.
/// </summary>
internal static class MapRoomWindowReturn
{
    private static bool _partyPending;
    private static bool _questPending;

    internal static void Arm() => _partyPending = _questPending = true;
    internal static void Reset() => _partyPending = _questPending = false;

    internal static bool IsPresentationSwitch(bool flatMapWanted, bool nativeMapVisible,
        bool running) => flatMapWanted && nativeMapVisible && running;

    internal static void Tick()
    {
        if (!_partyPending && !_questPending) return;
        if (!MapRoom.MapRoomDriver.Active || !WorldUIConfig.ConversionActive) return;
        var parchment = MapRoom.MapRoomDriver.ParchmentRenderer;
        if (parchment == null || !parchment.enabled || !parchment.gameObject.activeInHierarchy) return;
        var state = MapRuleLibrary.Adventure.AdventureState.MapState;
        // NewPartyDisplayUI.Show itself requires this unlock. A cold campaign/tutorial
        // entry must never present the character controls ahead of the native introduction.
        if (state == null || !state.HeadquartersState.PartyUIUnlocked
            || StoryComposite.PointOfNoReturn) return;

        if (_partyPending)
        {
            var party = NewPartyDisplayUI.PartyDisplay;
            if (party != null) TryReturn(party.GetComponent<UIWindow>(), ref _partyPending);
        }
        if (_questPending)
        {
            var manager = QuestManager.Instance;
            if (manager != null && manager.questLog != null)
                TryReturn(manager.questLog.GetComponent<UIWindow>(), ref _questPending);
        }
    }

    private static void TryReturn(UIWindow? window, ref bool pending)
    {
        if (window == null || FloatRefusalTable.Refuses(window)) return;
        // Retain the request until a real conversion succeeds (late native roots, render
        // readiness and temporary story/loadout curtains can all postpone that conversion).
        pending = !ModalFallback.ReturnPermanentMapWindow(window);
    }
}

internal static partial class ModalFallback
{
    internal static bool ReturnPermanentMapWindow(UIWindow window)
    {
        if (!IsMapRoomPermanent(window) || FloatRefusalTable.Refuses(window)) return false;
        if (IsFloatedByUs(window)) return true;
        AddPollWindow(window);
        return false;
    }
}
