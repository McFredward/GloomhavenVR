using System;
using System.Reflection;
using GloomhavenVR.Core;
using MapRuleLibrary.MapState;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Read-only identity of the game's quest selection and its two original popup instances.
/// A flat host's SelectQuest first populates hostSelectedLocation, before the client's
/// PreviewQuest callback selects a local marker. IsQuestShown and the VR record-20 staging
/// field therefore cannot alone answer whether that client has a quest to accept.
/// </summary>
internal static class NativeMapQuestSelection
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly FieldInfo? HostLocation = typeof(UIMapMultiplayerController).GetField("hostSelectedLocation", Fields);
    private static readonly FieldInfo? SelectedQuest = typeof(UIQuestPopupManager).GetField("selectedQuest", Fields);
    private static readonly FieldInfo? ClientQuest = typeof(UIQuestPopupManager).GetField("clientSelectedQuest", Fields);
    private static readonly FieldInfo? SelectedPopup = typeof(UIQuestPopupManager).GetField("selectedQuestPopup", Fields);
    private static readonly FieldInfo? MultiplayerPopup = typeof(UIQuestPopupManager).GetField("multiplayerQuestPopup", Fields);
    private static readonly FieldInfo? QuestState = typeof(UIQuestPopupManager).Assembly
        .GetType("Assets.Script.GUI.Quest.Quest")?.GetField("questState", Fields);
    private static bool _warned;

    /// <summary>False means unavailable evidence, true with null means a measured absence.
    /// Location.ID is the native SelectQuest token identity, including travel quests whose
    /// quest ID differs from the ending village's location ID.</summary>
    internal static bool TryGetHostProposal(out CQuestState? quest, out string? locationId)
    {
        quest = null;
        locationId = null;
        if (!FFSNetwork.IsOnline || !FFSNetwork.IsClient)
            return true;
        if (!Singleton<UIMapMultiplayerController>.IsInitialized || HostLocation == null)
            return false;
        try
        {
            UIMapMultiplayerController controller = Singleton<UIMapMultiplayerController>.Instance;
            if (controller == null)
                return false;
            quest = controller.HostSelectedQuest;
            MapLocation? location = HostLocation.GetValue(controller) as MapLocation;
            if (quest == null)
                return true;
            // A destroyed/rebuilding native location is not proof of a cancellation.
            if (location == null)
                return false;
            locationId = LocationId(location);
            return true;
        }
        catch (Exception ex)
        {
            NoteUnavailable(ex);
            return false;
        }
    }

    /// <summary>The current native proposal outranks client-local browsing. Observing it
    /// does not invoke a selection, change readiness or publish a second gameplay channel.</summary>
    internal static bool TryGetDecision(out string? locationId)
    {
        locationId = null;
        if (!TryGetHostProposal(out CQuestState? proposal, out string? proposedId))
            return false;
        if (proposal != null)
        {
            locationId = proposedId;
            return true;
        }
        if (!Singleton<AdventureMapUIManager>.IsInitialized || !Singleton<UIQuestPopupManager>.IsInitialized
            || SelectedQuest == null)
            return false;
        try
        {
            AdventureMapUIManager map = Singleton<AdventureMapUIManager>.Instance;
            UIQuestPopupManager popups = Singleton<UIQuestPopupManager>.Instance;
            if (map == null || popups == null)
                return false;
            MapLocation? location = map.LocationToTravel;
            if (location != null && location.IsSelected)
            {
                locationId = LocationId(location);
                return true;
            }
            // A native popup can precede replacement map objects. Keep the settled
            // observation until its original subject actually leaves the manager.
            return SelectedQuest.GetValue(popups) == null;
        }
        catch (Exception ex)
        {
            NoteUnavailable(ex);
            return false;
        }
    }

    /// <summary>The original popup carrying the current subject, never an enum-ID match.
    /// MultiplayerPopup is the native proposal hover. PreviewQuest selects the marker and
    /// opens SelectedPopup, so online clients must also switch back to that original.</summary>
    internal static UIWindow? ConfirmationWindow
    {
        get
        {
            if (!Singleton<UIQuestPopupManager>.IsInitialized || SelectedQuest == null || ClientQuest == null
                || SelectedPopup == null || MultiplayerPopup == null)
                return null;
            try
            {
                UIQuestPopupManager manager = Singleton<UIQuestPopupManager>.Instance;
                if (manager == null)
                    return null;
                object? selected = SelectedQuest.GetValue(manager);
                CQuestState? hovered = ClientQuest.GetValue(manager) as CQuestState;
                bool measured = TryGetHostProposal(out CQuestState? proposal, out _);
                if (proposal != null)
                {
                    CQuestState? selectedState = selected != null && QuestState != null
                        && QuestState.DeclaringType!.IsInstanceOfType(selected)
                        ? QuestState.GetValue(selected) as CQuestState : null;
                    if (SameQuest(selectedState, proposal))
                        return WindowOf(SelectedPopup, manager);
                    if (SameQuest(hovered, proposal))
                        return WindowOf(MultiplayerPopup, manager);
                    return null; // pending native PreviewQuest, or unrelated local browsing
                }
                if (selected != null)
                    return WindowOf(SelectedPopup, manager);
                return !measured && hovered != null ? WindowOf(MultiplayerPopup, manager) : null;
            }
            catch (Exception ex)
            {
                NoteUnavailable(ex);
                return null;
            }
        }
    }

    internal static bool IsConfirmationWindow(UIWindow? window) =>
        window != null && ReferenceEquals(window, ConfirmationWindow);

    /// <summary>Each native manager field owns its own popup lifetime. A live selected
    /// subject must not keep an already-hidden multiplayer hover sticky, or vice versa.</summary>
    internal static bool TryHasSubject(UIWindow? window, out bool hasSubject)
    {
        hasSubject = false;
        if (window == null || !Singleton<UIQuestPopupManager>.IsInitialized || SelectedQuest == null
            || ClientQuest == null || SelectedPopup == null || MultiplayerPopup == null)
            return false;
        try
        {
            UIQuestPopupManager manager = Singleton<UIQuestPopupManager>.Instance;
            if (manager == null)
                return false;
            if (ReferenceEquals(window, WindowOf(SelectedPopup, manager)))
            {
                hasSubject = SelectedQuest.GetValue(manager) != null;
                return true;
            }
            if (ReferenceEquals(window, WindowOf(MultiplayerPopup, manager)))
            {
                hasSubject = ClientQuest.GetValue(manager) != null;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            NoteUnavailable(ex);
            return false;
        }
    }

    private static UIWindow? WindowOf(FieldInfo field, UIQuestPopupManager manager) =>
        (field.GetValue(manager) as UIQuestPopup)?.GetComponent<UIWindow>();

    private static bool SameQuest(CQuestState? first, CQuestState? second) => first != null && second != null
        && !string.IsNullOrEmpty(first.ID) && string.Equals(first.ID, second.ID, StringComparison.Ordinal);

    private static string LocationId(MapLocation location)
    {
        string? id = location.Location?.ID;
        return string.IsNullOrEmpty(id) ? "<unreadable-location-id>" : id!;
    }

    private static void NoteUnavailable(Exception ex)
    {
        if (_warned)
            return;
        _warned = true;
        VRLog.Warn("MapRoom", "MAP QUEST native selection identity could not be read — "
            + $"{ex.GetType().Name}: {ex.Message}. No cancellation is inferred; one report per process.");
    }
}
