using System;
using GloomhavenVR.WorldUI.MapRoom;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Net;
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
        MapQuestReadyUp.UnreadyRequested = false;
        StoryComposite.PointOfNoReturn = false;
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

    private static (MapLocationInteractor Observer, MapLocation First, MapLocation Second) Delivery(bool client = true)
    {
        var observer = Reset(client);
        MapLocation first = new("delivery-A", "quest-A"), second = new("delivery-B", "quest-B");
        observer.AdoptSelection(first, "initial local browse");
        observer.Sample();
        RemoteMapRoom.Begin(observer, first, second);
        RemoteMapRoom.Packet(1, 254, first); RemoteMapRoom.Tick(); // first sight never clicks
        Check(first.IsSelected && first.DeselectCalls == 0, "First peer snapshot must only establish selection history");
        return (observer, first, second);
    }

    private static void LockedDelivery()
    {
        foreach (bool client in new[] { false, true })
        {
            var (observer, first, second) = Delivery(client);
            Map.IsLocked = true;
            RemoteMapRoom.Packet(1, 255, null!); RemoteMapRoom.Tick();
            Check(RemoteMapRoom.Pending && first.IsSelected && Popups.Selected.gameObject.activeInHierarchy,
                "Locked shared clear must retain its original visible native subject until authorized");
            Check(MapQuestReadyUp.Edges.Count == 1 && MapQuestReadyUp.UnreadyRequested,
                "Locked shared clear must request own native quest-unready before adoption");
            for (int i = 0; i < 100; i++) RemoteMapRoom.Tick();
            Check(Map.IsLocked && first.DeselectCalls == 0 && MapQuestReadyUp.Edges.Count == 1,
                "Pending clear never releases native locks, repeats readiness, or clicks every frame");
            Map.IsLocked = false; // Explicit native-network authorization boundary.
            RemoteMapRoom.Tick();
            Check(!RemoteMapRoom.Pending && !first.IsSelected && observer.Staged == null
                && !Popups.Selected.gameObject.activeInHierarchy,
                "Authorized shared clear must retire the same native popup exactly once");
            RemoteMapRoom.Tick();
            Check(first.DeselectCalls == 1 && RemoteMapRoom.EchoSuppressed,
                "Applied shared clear consumes once and suppresses its transport echo");
            // A backwards packet is not a new human action; wrap 255 -> 0 is.
            RemoteMapRoom.Packet(1, 254, first); RemoteMapRoom.Tick();
            Check(!first.IsSelected, "Backwards selection packet must not reopen a closed subject");
            RemoteMapRoom.Packet(1, 0, second); RemoteMapRoom.Tick();
            Check(second.IsSelected, "Wrapping selection stamp must admit the next real edge");
        }

        var state = Delivery();
        state.Observer.AdoptSelection(state.Second, "newer local subject");
        RemoteMapRoom.Packet(1, 255, null!); RemoteMapRoom.Tick();
        Check(state.Second.IsSelected && !RemoteMapRoom.Pending && !MapQuestReadyUp.UnreadyRequested,
            "Delayed clear of old subject must not dismiss a new quest or withdraw its readiness");

        state = Delivery(); Map.IsLocked = true;
        RemoteMapRoom.Packet(1, 255, null!); RemoteMapRoom.Tick();
        RemoteMapRoom.Packet(1, 0, state.Second); RemoteMapRoom.Tick();
        Map.IsLocked = false; RemoteMapRoom.Tick();
        Check(state.Second.IsSelected && !state.First.IsSelected && !RemoteMapRoom.Pending,
            "New selection supersedes a pending clear before native authorization");

        state = Delivery();
        RemoteMapRoom.Begin(state.Observer, state.First, state.Second);
        RemoteMapRoom.Packet(1, 0, null!); RemoteMapRoom.Tick();
        RemoteMapRoom.Packet(1, 0, state.First); RemoteMapRoom.Tick(); // adopted elsewhere; no echo stamp
        RemoteMapRoom.Packet(1, 1, null!); RemoteMapRoom.Tick();
        Check(!state.First.IsSelected,
            "A peer may clear an adopted subject without first publishing its own selection stamp");

        state = Delivery(); Map.IsLocked = true;
        RemoteMapRoom.Packet(1, 255, null!); RemoteMapRoom.Tick();
        RemoteMapRoom.Packet(1, 0, new MapLocation("unresolvable-surface", "absent-quest")); RemoteMapRoom.Tick();
        Map.IsLocked = false; RemoteMapRoom.Tick();
        Check(state.First.IsSelected && !RemoteMapRoom.Pending,
            "A newer unavailable selection must retire its obsolete pending clear");

        state = Delivery(); Map.IsLocked = true;
        RemoteMapRoom.Packet(1, 255, state.Second); RemoteMapRoom.Tick();
        state.First.Selectable = false; Map.IsLocked = false; RemoteMapRoom.Tick();
        Check(state.First.IsSelected && ReferenceEquals(state.Observer.Staged, state.First)
            && !state.Second.IsSelected && !RemoteMapRoom.Pending,
            "Native Deselect refusal must preserve staging and prevent replacement dispatch");

        state = Delivery(); state.Second.Selectable = false;
        Check(!state.Observer.AdoptSelection(state.Second, "native click refuses replacement")
            && !state.Second.IsSelected && state.Observer.Staged == null,
            "Native click refusal must not report a requested selection as adopted");

        state = Delivery(); Map.IsLocked = true;
        RemoteMapRoom.Packet(1, 255, null!); RemoteMapRoom.Tick();
        state.Second.Select(); state.Observer.Arm(state.Second);
        state.First.Select(); state.Observer.Arm(state.First); // same key, newer native opening
        RemoteMapRoom.Tick();
        Check(!RemoteMapRoom.Pending && state.First.IsSelected,
            "Pending clear must retire after a new opening of the same quest");

        state = Delivery(client: false); Map.IsLocked = true;
        RemoteMapRoom.Packet(1, 255, state.Second); RemoteMapRoom.Tick();
        state.First.Deselect(); // Native host OnReady(false) owns this original callback.
        Map.IsLocked = false; Time.unscaledTime = 2; state.Observer.DeselectTick();
        Check(state.Observer.Staged == null && state.First.DeselectCalls == 1,
            "Native retirement must clear staging without repeating its original callback");
        RemoteMapRoom.Tick();
        Check(state.Second.IsSelected && !RemoteMapRoom.Pending,
            "Native unready retirement must preserve a pending replacement selection");

        foreach (bool x in new[] { false, true })
        {
            state = Delivery(); Map.IsLocked = true;
            RemoteMapRoom.Packet(1, 255, state.Second); RemoteMapRoom.Tick();
            Map.IsLocked = false; Time.unscaledTime = 2;
            if (x) MapLocationInteractor.HandleSharedQuestClose(Popups.Selected.Window);
            else { state.Observer.Press = new(); state.Observer.DeselectTick(); }
            RemoteMapRoom.Tick();
            Check(!state.Second.IsSelected && state.Observer.Staged == null && !RemoteMapRoom.Pending,
                "Later explicit local close must supersede a pending replacement selection");
        }

        foreach (string retirement in new[] { "generation", "surface", "interactor", "subject", "peer", "proposal", "commit", "unknown", "room" })
        {
            state = Delivery(); Map.IsLocked = true;
            RemoteMapRoom.Packet(1, 255, null!); RemoteMapRoom.Tick();
            Check(RemoteMapRoom.Pending, "Retirement fixture must genuinely retain a pending clear");
            switch (retirement)
            {
                case "generation": state.Observer.ScanGeneration++; break;
                case "surface": MapIconLayer.CurrentSurface = MapIconLayer.MapSurface.City; break;
                case "interactor": MapRoomDriver.ActiveLocations = new(); break;
                case "subject": state.First.Location.ID = "rebound-native-location"; break;
                case "peer": RemoteMapRoom.Leave(1); break;
                case "proposal": Multiplayer.ProxyHostSelectedLocation(state.First); break;
                case "commit": StoryComposite.PointOfNoReturn = true; break;
                case "unknown": Singleton<UIMapMultiplayerController>.Instance = null!; break;
                case "room": MapRoomDriver.Active = false; break;
            }
            RemoteMapRoom.Tick();
            Check(!RemoteMapRoom.Pending, "Pending clear must retire on " + retirement);
            Map.IsLocked = false; RemoteMapRoom.Tick();
            Check(state.First.IsSelected || retirement == "subject",
                "Retired clear must not replay after unlock: " + retirement);
        }

        state = Delivery(); Map.IsLocked = true; StoryComposite.PointOfNoReturn = true;
        RemoteMapRoom.Packet(1, 255, null!); RemoteMapRoom.Tick();
        Check(!RemoteMapRoom.Pending && !MapQuestReadyUp.UnreadyRequested && state.First.IsSelected,
            "Starting quest must never unready or queue a browsing clear");

        state = Delivery();
        Check(MapLocationInteractor.HandleSharedQuestClose(Popups.Selected.Window)
            && !state.First.IsSelected && state.Observer.Staged == null,
            "Shared quest X must follow original native browsing deselection");
        state = Delivery(); Map.IsLocked = true;
        Check(MapLocationInteractor.HandleSharedQuestClose(Popups.Selected.Window) && state.First.IsSelected,
            "Shared quest X must preserve an original native map lock");
        state = Delivery(); Multiplayer.ProxyHostSelectedLocation(state.First);
        Multiplayer.Pending(); // original native PreviewQuest; no automatic readiness/confirmation
        Check(NativeMapQuestSelection.BlocksSharedBrowsing, "Genuine native host proposal must block incidental VR browsing");
        Time.unscaledTime = 2; state.Observer.Press = new(); state.Observer.DeselectTick();
        Check(state.First.IsSelected && Popups.Selected.gameObject.activeInHierarchy,
            "Incidental table click must leave the shared native proposal visible");
        Check(MapLocationInteractor.HandleSharedQuestClose(Popups.Selected.Window) && state.First.IsSelected,
            "Incidental X must leave the shared native proposal visible");
        Check(!MapLocationInteractor.HandleSharedQuestClose(new UIWindow()),
            "Shared quest close must never seize an unrelated equal-ID popup");
        Multiplayer.CancelNativeProposal();
        Check(!NativeMapQuestSelection.BlocksSharedBrowsing,
            "Native proposal cancellation immediately restores ordinary shared browsing");
    }

    public static void Main()
    {
        foreach (bool flatHost in new[] { true, false })
        foreach (bool flatCompanion in new[] { true, false })
        foreach (bool reconnect in new[] { true, false }) Client(flatHost, flatCompanion, reconnect);
        Host(true); Host(false);
        DelayedInitialization(); LinkedQuest(); UncommittedBrowsing(); LockedDelivery();
        System.Console.WriteLine($"Map quest selection runtime: {_assertions} assertions; {_roles} host/client/reconnect paths; original native proposal, preview, cancel and selection methods.");
    }
}
