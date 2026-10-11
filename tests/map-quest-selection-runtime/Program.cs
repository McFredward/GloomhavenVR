using System;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions, _roles;
    private static UIQuestPopupManager Popups => Singleton<UIQuestPopupManager>.Instance;
    private static UIMapMultiplayerController Multiplayer => Singleton<UIMapMultiplayerController>.Instance;
    private static AdventureMapUIManager Map => Singleton<AdventureMapUIManager>.Instance;

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static MapLocationInteractor Reset(bool client, bool online = true)
    {
        FFSNetwork.IsOnline = online; FFSNetwork.IsClient = client;
        Time.unscaledTime = 0; Time.frameCount = 0;
        MapRoomDriver.Active = true;
        Singleton<UIQuestPopupManager>.Instance = new();
        Singleton<UIMapMultiplayerController>.Instance = new();
        Singleton<AdventureMapUIManager>.Instance = new();
        Singleton<UIGuildmasterHUD>.Instance = new();
        Singleton<MapChoreographer>.Instance = new();
        Singleton<UIReadyToggle>.Instance = new();
        UnityEngine.Object.FirstPopup = Popups.Multiplayer;
        MapQuestReadyUp.Edges.Clear();
        MapLocationInteractor.ClearStatic();
        return new MapLocationInteractor();
    }

    private static string? Decision()
    {
        Check(MapLocationInteractor.TryGetPublishedDecision(out string? id, out _), "A sampled/native decision must be available");
        return id;
    }

    private static void FromHost(MapLocation recipientLocation, bool flatHost)
    {
        // Distinct native host/client worlds: flat input calls original Select;
        // VR input passes through actual production AdoptSelection to the same native Select.
        var map = Map; var popups = Popups; var multiplayer = Multiplayer;
        var ready = Singleton<UIReadyToggle>.Instance;
        FFSNetwork.IsClient = false;
        Singleton<AdventureMapUIManager>.Instance = new();
        Singleton<UIQuestPopupManager>.Instance = new();
        Singleton<UIMapMultiplayerController>.Instance = new();
        Singleton<UIReadyToggle>.Instance = new();
        MapLocation hostLocation = new(recipientLocation.Location.ID, recipientLocation.LocationQuest.ID);
        if (flatHost) hostLocation.Select();
        else Check(new MapLocationInteractor().AdoptSelection(hostLocation, "VR host pointer"), "VR host browse must reach native selection");
        Multiplayer.ConfirmSelectedLocation(); // verbatim native gameplay token sender
        Check(Synchronizer.LastToken.ID == recipientLocation.Location.ID, "Flat and VR hosts must send unchanged native location token");
        Singleton<AdventureMapUIManager>.Instance = map;
        Singleton<UIQuestPopupManager>.Instance = popups;
        Singleton<UIMapMultiplayerController>.Instance = multiplayer;
        Singleton<UIReadyToggle>.Instance = ready;
        FFSNetwork.IsClient = true;
        Multiplayer.ProxyHostSelectedLocation(recipientLocation);
    }

    private static void Companion(MapLocation proposal, bool flatCompanion)
    {
        var map = Map; var popups = Popups; var multiplayer = Multiplayer;
        var ready = Singleton<UIReadyToggle>.Instance;
        var hud = Singleton<UIGuildmasterHUD>.Instance;
        Singleton<AdventureMapUIManager>.Instance = new();
        Singleton<UIQuestPopupManager>.Instance = new();
        Singleton<UIMapMultiplayerController>.Instance = new();
        Singleton<UIReadyToggle>.Instance = new();
        Singleton<UIGuildmasterHUD>.Instance = new();
        MapLocation companionLocation = new(proposal.Location.ID, proposal.LocationQuest.ID);
        Multiplayer.ProxyHostSelectedLocation(companionLocation);
        Check(!companionLocation.IsSelected && !Popups.IsQuestShown,
            "Each flat/VR companion retains native unpreviewed proposal until callback");
        if (flatCompanion) Multiplayer.ConfirmNativeProposal(); // reachable native HUD click
        else Multiplayer.Pending(); // callback the VR pending-prompt driver invokes
        Check(companionLocation.IsSelected && Multiplayer.ReadyVisible && Popups.IsQuestShown,
            "Each flat/VR companion opens original selected popup and native Accept");
        Singleton<AdventureMapUIManager>.Instance = map;
        Singleton<UIQuestPopupManager>.Instance = popups;
        Singleton<UIMapMultiplayerController>.Instance = multiplayer;
        Singleton<UIReadyToggle>.Instance = ready;
        Singleton<UIGuildmasterHUD>.Instance = hud;
    }

    private static void Client(bool flatHost, bool flatCompanion, bool reconnect)
    {
        _roles++;
        MapLocationInteractor observer = Reset(client: true);
        MapLocation first = new("scenario-A", "quest-A"), second = new("village-B", "travel-quest-B");
        if (!reconnect) observer.Sample(); // existing room has legitimately sampled null
        Check(!Popups.IsQuestShown, "Native selectedQuest starts absent");
        FromHost(first, flatHost);
        Companion(first, flatCompanion);
        Check(!Popups.IsQuestShown && !first.IsSelected, "Native proposal precedes selectedQuest and marker preview");
        Check(NativeMapQuestSelection.TryGetHostProposal(out var proposal, out string? proposalId)
            && ReferenceEquals(proposal, first.LocationQuest) && proposalId == "scenario-A", "Host proposal uses native location token identity");
        Check(!MapLocationInteractor.QuestSelectionCleared(out _), "Flat/VR host proposal must protect client popup before observer sampling");
        Check(Decision() == "scenario-A", "Host proposal must replace obsolete published null immediately");
        observer.Sample();
        Check(Decision() == "scenario-A", "Host proposal must enter settled observer identity without VR staging");
        Check(observer.SettledId == "scenario-A", "Observer must sample native proposal rather than local staging");
        Check(MapQuestReadyUp.Edges.Count == (reconnect ? 0 : 1), "Initial reconnect baseline must not withdraw readiness");

        Popups.ShowMultiplayerPreview(first.LocationQuest);
        Check(ReferenceEquals(NativeMapQuestSelection.ConfirmationWindow, Popups.Multiplayer.Window), "Native hover subject chooses original multiplayer popup");
        Check(NativeMapQuestSelection.TryHasSubject(Popups.Multiplayer.Window, out bool hasHover) && hasHover, "Hover popup owns its own native subject");
        Popups.HideMultiplayerPreview(); // native prompt click hides hover BEFORE PreviewQuest
        Multiplayer.ConfirmNativeProposal();
        Check(Popups.IsQuestShown && first.IsSelected && Multiplayer.ReadyVisible, "Original PreviewQuest opens selected popup and native Accept control");
        Check(ReferenceEquals(NativeMapQuestSelection.ConfirmationWindow, Popups.Selected.Window), "PreviewQuest must route back to original selected popup");
        Check(NativeMapQuestSelection.IsConfirmationWindow(Popups.Selected.Window)
            && !NativeMapQuestSelection.IsConfirmationWindow(new UIWindow()), "Equal enum IDs must never substitute another popup instance");
        Check(NativeMapQuestSelection.TryHasSubject(Popups.Multiplayer.Window, out hasHover) && !hasHover,
            "Closed hover loses subject while selected popup remains live");
        Check(observer.PopupOpen(), "Inactive first type-wide popup must not hide current selected popup");
        Time.unscaledTime = 2;
        observer.DeselectTick(); // actual TickAdoptGameSelection/auto-close path
        Check(first.IsSelected && first.DeselectCalls == 0, "Unrelated inactive popup must not trigger automatic deselection");
        Check(!observer.AdoptSelection(null, "late VR record20 clear") && first.IsSelected,
            "Late VR browsing clear must not cancel native host proposal");
        Check(!observer.AdoptSelection(second, "late VR record20 browse") && first.IsSelected && !second.IsSelected,
            "Late VR browsing replacement must not change native host proposal");
        Popups.HideAll();
        Time.unscaledTime += 2; // outlast actual TickAdoptGameSelection's one-second grace
        observer.DeselectTick();
        Check(first.IsSelected && first.DeselectCalls == 0, "Temporarily missing presentation must not auto-deselect live host proposal");
        Popups.ShowQuest(first.LocationQuest);
        int unchangedEdges = MapQuestReadyUp.Edges.Count;
        observer.Sample();
        Check(MapQuestReadyUp.Edges.Count == unchangedEdges, "Opening same native proposal must not change readiness decision");

        // Client-local browsing cannot override the host's pending decision or receive Accept.
        Popups.ShowQuest(second.LocationQuest);
        Check(NativeMapQuestSelection.ConfirmationWindow == null, "Unrelated local browsing must not receive host proposal control");
        Check(NativeMapQuestSelection.TryGetDecision(out string? authoritative) && authoritative == "scenario-A",
            "Host proposal outranks client-local browsing");
        Popups.ShowQuest(first.LocationQuest);

        FromHost(second, flatHost);
        Check(Decision() == "village-B", "New host proposal identity must replace prior selection immediately");
        observer.Sample();
        Check(MapQuestReadyUp.Edges[^1] == ("scenario-A", "village-B"), "Quest replacement must withdraw readiness by stable location identity");
        Multiplayer.ConfirmNativeProposal();
        observer.DeselectTick();
        observer.Sample();
        Check(Decision() == "village-B", "Travel quest identity must stay village ID after native marker preview");
        int stableEdges = MapQuestReadyUp.Edges.Count;
        MapLocation replacement = new("village-B", "travel-quest-B");
        FromHost(replacement, flatHost);
        observer.Sample();
        Check(MapQuestReadyUp.Edges.Count == stableEdges, "Same native location on fresh map object must not invalidate readiness");
        Multiplayer.ConfirmNativeProposal();
        observer.DeselectTick();
        observer.Sample();

        Multiplayer.CancelNativeProposal();
        observer.Sample();
        Check(!MapLocationInteractor.QuestSelectionCleared(out _), "Native cancellation must spend settle before releasing popup");
        Time.unscaledTime += 0.9f; observer.Sample();
        Check(!MapLocationInteractor.QuestSelectionCleared(out _), "Transient native null must preserve selected decision");
        Time.unscaledTime += 0.2f; observer.Sample();
        Check(MapLocationInteractor.QuestSelectionCleared(out _) && Decision() == null, "Flat host cancellation must settle without any VR record20");
        Check(MapQuestReadyUp.Edges[^1] == ("village-B", null), "Native cancellation must notify readiness once");
    }

    private static void Host(bool online)
    {
        _roles++;
        MapLocationInteractor observer = Reset(client: false, online);
        observer.Sample();
        MapLocation first = new("host-A", "host-quest-A");
        first.Select(); observer.Arm(first); observer.Sample();
        Check(Decision() == "host-A", "VR host/singleplayer native marker uses stable location identity");
        Check(ReferenceEquals(NativeMapQuestSelection.ConfirmationWindow, Popups.Selected.Window), "Host must retain original selected popup");
        Time.unscaledTime = 2;
        observer.DeselectTick();
        Check(first.IsSelected, "Host must not auto-close due to inactive multiplayer popup");
        Map.IsLocked = true; observer.Press = new(); observer.DeselectTick();
        Check(first.IsSelected, "Native map lock must protect local deselection");
        Map.IsLocked = false; observer.DeselectTick(); observer.Sample();
        Time.unscaledTime += 1.1f; observer.Sample();
        Check(!first.IsSelected && MapLocationInteractor.QuestSelectionCleared(out _), "Manual deselection stays native and settles normally");
    }

    private static void DelayedInitialization()
    {
        MapLocationInteractor observer = Reset(client: true);
        Singleton<UIMapMultiplayerController>.Instance = null!;
        observer.Sample();
        Check(!MapLocationInteractor.TryGetPublishedDecision(out _, out _), "Uninitialized native manager must not publish first-sight cancellation");
        Check(!MapLocationInteractor.QuestSelectionCleared(out _), "Missing native manager must not release pending quest popup");
        Singleton<UIMapMultiplayerController>.Instance = new();
        Multiplayer.ProxyHostSelectedLocation(new MapLocation("late-A", "late-quest-A"));
        Singleton<AdventureMapUIManager>.Instance = null!;
        observer.Sample();
        Check(Decision() == "late-A" && MapQuestReadyUp.Edges.Count == 0, "Late native proposal must establish readiness baseline without local map manager");
        MapRoomDriver.Active = false;
        Check(!MapLocationInteractor.QuestSelectionCleared(out _), "Inactive room must not speak for current native selection");
        MapRoomDriver.Active = true;
        Check(!NativeMapQuestSelection.TryHasSubject(new UIWindow(), out _), "Unrecognized equal-ID popup has no native subject evidence");
    }

    private static void LinkedQuest()
    {
        MapLocationInteractor observer = Reset(client: true);
        Singleton<MapChoreographer>.Instance.Linked = true;
        Multiplayer.ProxyHostSelectedLocation(new MapLocation("linked-A", "linked-quest-A"));
        observer.Sample();
        Check(Decision() == "linked-A" && Multiplayer.ReadyVisible && Popups.IsQuestShown,
            "Native linked-quest direct PreviewQuest branch must remain intact");
        Check(ReferenceEquals(NativeMapQuestSelection.ConfirmationWindow, Popups.Selected.Window), "Linked quest also uses native selected popup");
    }

    private static void UncommittedBrowsing()
    {
        MapLocationInteractor observer = Reset(client: true);
        observer.Sample();
        MapLocation first = new("browse-A", "browse-quest-A");
        Check(observer.AdoptSelection(first, "ordinary record20 browse") && first.IsSelected,
            "VR browsing before native commitment must retain ordinary native selection");
        observer.Sample();
        Check(Decision() == "browse-A", "Measured native click must preserve record20 selection identity");
        Check(observer.AdoptSelection(null, "ordinary record20 clear") && !first.IsSelected,
            "VR browsing clear before native commitment must remain native");
        observer.Sample(); Time.unscaledTime += 1.1f; observer.Sample();
        Check(MapLocationInteractor.QuestSelectionCleared(out _), "Ordinary shared browsing clear still settles");
    }

    public static void Main()
    {
        foreach (bool flatHost in new[] { true, false })
        foreach (bool flatCompanion in new[] { true, false })
        foreach (bool reconnect in new[] { true, false }) Client(flatHost, flatCompanion, reconnect);
        Host(true); Host(false);
        DelayedInitialization(); LinkedQuest(); UncommittedBrowsing();
        System.Console.WriteLine($"Map quest selection runtime: {_assertions} assertions; {_roles} host/client/reconnect paths; original native proposal, preview, cancel and selection methods.");
    }
}
