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
        MapCityEventReadyUp.OwnsMapDecision = false;
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

    private static void VersionPolicy()
    {
        var a = new SharedQuestSelectionVersion.Event(1, 1001, 0, 0, 11);
        var b = new SharedQuestSelectionVersion.Event(2, 2002, 1, 1001, 22);
        var clearA = new SharedQuestSelectionVersion.Event(2, 3003, 1, 1001, 0);
        var clearB = new SharedQuestSelectionVersion.Event(3, 4004, 2, 2002, 0);
        var reopenA = new SharedQuestSelectionVersion.Event(4, 5005, 3, 4004, 11);
        var oldClearA = new SharedQuestSelectionVersion.Event(5, 6006, 1, 1001, 0);
        void Observe(SharedQuestSelectionVersion register, SharedQuestSelectionVersion.Event e) =>
            register.Observe(e.Author, e.Counter, e.ParentCounter, e.ParentAuthor, e.Key, out _);
        void Permute(SharedQuestSelectionVersion.Event[] events, int index, uint expectedKey, ulong expectedOpening)
        {
            if (index == events.Length)
            {
                var register = new SharedQuestSelectionVersion(); register.BindBaseline(11);
                foreach (var e in events) { Observe(register, e); Observe(register, e); }
                Check(register.HasCurrent && register.Current.Key == expectedKey && register.Current.Opening == expectedOpening,
                    "Opening register must converge across reordered and duplicate causal events");
                return;
            }
            for (int i = index; i < events.Length; i++)
            {
                (events[index], events[i]) = (events[i], events[index]);
                Permute(events, index + 1, expectedKey, expectedOpening);
                (events[index], events[i]) = (events[i], events[index]);
            }
        }
        Permute(new[] { a, b, clearA }, 0, 22, b.Opening);
        Permute(new[] { a, b, clearB }, 0, 0, b.Opening);
        Permute(new[] { a, b, clearA, clearB, reopenA, oldClearA }, 0, 11, reopenA.Opening);
        var missing = new SharedQuestSelectionVersion(); Observe(missing, a); Observe(missing, clearB);
        Check(missing.Current.Key == 0 && missing.Current.Opening == b.Opening,
            "Clear must retain its causal opening when its selection snapshot was missed");
        Observe(missing, b);
        Check(missing.Current.Key == 0, "A late duplicate opening must not resurrect its accepted clear");
        var fullId = new SharedQuestSelectionVersion();
        Check(fullId.TryPublish(int.MaxValue, 7, 0, out var own) && own.Author == int.MaxValue
            && (uint)own.Token == int.MaxValue,
            "Causal author identity must preserve full native ConnectionId bits");
        fullId.RetireOwn();
        Check(!fullId.HasOwn && !fullId.HasCurrent && fullId.Clock == 1,
            "Native own-opening retirement must preserve the session clock without publishing a clear");
        Observe(fullId, own);
        Check(!fullId.HasCurrent, "A retired native opening must not replay after map stand-down");
        Check(fullId.TryPublish(1001, 8, 0, out var next) && next.Counter == 2 && fullId.Current.Key == 8,
            "A fresh human selection after native retirement must receive a new session counter");
        var malformed = new SharedQuestSelectionVersion();
        foreach (var e in new[] { new SharedQuestSelectionVersion.Event(0, 1, 0, 0, 1),
            new SharedQuestSelectionVersion.Event(1, 0, 0, 0, 1),
            new SharedQuestSelectionVersion.Event(2, 1, 3, 1, 1),
            new SharedQuestSelectionVersion.Event(2, 1, 2, 1, 0),
            new SharedQuestSelectionVersion.Event(2, 1, 0, 1, 1),
            new SharedQuestSelectionVersion.Event(2, 1, 1, 0, 1) })
            Check(!malformed.Observe(e.Author, e.Counter, e.ParentCounter, e.ParentAuthor, e.Key, out _)
                && !malformed.HasCurrent, "Malformed causal ancestry must not create a browsing event");
        malformed.Observe(1, uint.MaxValue, 0, 0, 2, out _);
        Check(!malformed.TryPublish(1, 3, 0, out _) && malformed.Current.Key == 2,
            "Exhausted counter must not wrap into an earlier quest opening");

        var relay = new SharedQuestSelectionVersion();
        Check(relay.Observe(9009, 7, 7, int.MaxValue, 77, out var relayed)
            && relayed.Opening == SharedQuestSelectionVersion.TokenOf(7, int.MaxValue)
            && !relay.HasOwn && relay.Clock == 7,
            "Selected relay must retain original full author and counter without minting a human event");
        relay.RetireCurrent();
        Check(!relay.Observe(9010, 7, 7, int.MaxValue, 77, out _),
            "A different live carrier must not resurrect a retired native opening");
    }

    private static (MapLocationInteractor Observer, MapLocation First, MapLocation Second) VersionWorld(int localPlayer)
    {
        var observer = Reset(client: true);
        MapLocation first = new("version-A", "quest-A"), second = new("version-B", "quest-B");
        RemoteMapRoom.Begin(observer, first, second);
        var initial = RemoteMapRoom.Snapshot(localPlayer);
        Check(!initial.HasMapSelectionVersion, "Native initial baseline must not invent a human opening");
        return (observer, first, second);
    }

    private static PresenceState VersionEvent(int sender, uint counter, uint parent, int parentAuthor, MapLocation? location)
    {
        return new PresenceState { HasMapRoom = true, MapRoomFlags = 1, HasMapSelectionVersion = true,
            MapRoomSelectKey = location == null ? 0 : NetProtocol.HashMapKey(location.Location.ID),
            MapSelectionRevision = counter, MapSelectionParent = parent, MapSelectionParentAuthor = parentAuthor };
    }

    private static void VersionDelivery()
    {
        const int owner = 1001001, carrier = 2002002, secondCarrier = 2502502, visitor = 3003003;
        var firstClickObserver = Reset(client: true);
        MapLocation firstClick = new("first-click", "first-quest");
        RemoteMapRoom.Begin(firstClickObserver, firstClick);
        firstClickObserver.AdoptSelection(firstClick, "physical dispatch before first presence census");
        PresenceState firstClickSnapshot = RemoteMapRoom.Snapshot(owner);
        Check(firstClickSnapshot.HasMapSelectionVersion && firstClickSnapshot.MapSelectionRevision == 1
            && firstClickSnapshot.MapSelectionParent == 0 && firstClickSnapshot.MapRoomSelectStamp == 1,
            "A genuine first dispatch before the first presence sample must publish its human opening");

        var restoredObserver = Reset(client: true);
        MapLocation restored = new("restored", "restored-quest");
        RemoteMapRoom.Begin(restoredObserver, restored);
        restored.Select(); restoredObserver.NativeSelectionTick();
        PresenceState restoredSnapshot = RemoteMapRoom.Snapshot(owner);
        Check(restoredSnapshot.MapRoomSelectKey != 0 && !restoredSnapshot.HasMapSelectionVersion
            && !RemoteMapRoom.HasAuthoredVersion && restoredSnapshot.MapRoomSelectStamp == 0,
            "Native initial selection adoption must remain a baseline rather than a forged first human click");

        var firstRelayObserver = Reset(client: true);
        MapLocation firstRelay = new("first-relay", "relay-quest");
        RemoteMapRoom.Begin(firstRelayObserver, firstRelay);
        RemoteMapRoom.Receive(owner, VersionEvent(owner, 9, 0, 0, firstRelay)); RemoteMapRoom.Tick();
        PresenceState firstRelaySnapshot = RemoteMapRoom.Snapshot(carrier);
        Check(firstRelay.IsSelected && firstRelaySnapshot.HasMapSelectionVersion
            && firstRelaySnapshot.MapSelectionRevision == 9 && firstRelaySnapshot.MapSelectionParent == 9
            && firstRelaySnapshot.MapSelectionParentAuthor == owner && !RemoteMapRoom.HasAuthoredVersion,
            "Remote dispatch before first presence sample must relay its original opening without a human mint");

        foreach (bool x in new[] { false, true })
        {
            var refused = VersionWorld(owner);
            refused.Observer.AdoptSelection(refused.First, "physical selection before refused close");
            PresenceState beforeRefusal = RemoteMapRoom.Snapshot(owner);
            refused.First.Selectable = false; Time.unscaledTime = 2;
            if (x) MapLocationInteractor.HandleSharedQuestClose(Popups.Selected.Window);
            else { refused.Observer.Press = new(); refused.Observer.DeselectTick(); }
            PresenceState afterRefusal = RemoteMapRoom.Snapshot(owner);
            Check(refused.First.IsSelected && afterRefusal.HasMapSelectionVersion
                && afterRefusal.MapSelectionRevision == beforeRefusal.MapSelectionRevision
                && afterRefusal.MapSelectionParent == beforeRefusal.MapSelectionParent
                && afterRefusal.MapRoomSelectStamp == beforeRefusal.MapRoomSelectStamp && !RemoteMapRoom.SendDue,
                "Refused manual X or table close must consume intent without minting a same-key reopening");
        }

        var fastReopen = VersionWorld(owner);
        fastReopen.Observer.AdoptSelection(fastReopen.First, "physical initial selection"); RemoteMapRoom.Snapshot(owner);
        MapLocationInteractor.HandleSharedQuestClose(Popups.Selected.Window);
        fastReopen.Observer.AdoptSelection(fastReopen.First, "physical reopen before next census");
        PresenceState fastReopened = RemoteMapRoom.Snapshot(owner);
        Check(fastReopen.First.IsSelected && fastReopened.MapSelectionRevision == 2 && fastReopened.MapSelectionParent == 1,
            "Accepted clear and same-quest reopen between samples must receive a fresh opening");

        var source = VersionWorld(owner);
        source.Observer.AdoptSelection(source.First, "local physical select");
        Check(RemoteMapRoom.SendDue, "A real local opening must preempt the presence cadence");
        PresenceState opened = RemoteMapRoom.Snapshot(owner);
        Check(opened.HasMapSelectionVersion && opened.MapSelectionRevision == 1
            && opened.MapSelectionParent == 0 && opened.MapSelectionParentAuthor == 0,
            "First human selection must author one authenticated full-ID opening");
        Check(RemoteMapRoom.TryReadQuestSelectionOpening(opened.MapRoomSelectKey, out ulong opening)
            && opening == SharedQuestSelectionVersion.TokenOf(1, owner),
            "Pose accessor must expose the opening actually displayed by the owner");
        MapLocationInteractor.HandleSharedQuestClose(Popups.Selected.Window);
        PresenceState cleared = RemoteMapRoom.Snapshot(owner);
        Check(cleared.HasMapSelectionVersion && cleared.MapRoomSelectKey == 0
            && cleared.MapSelectionRevision == 2 && cleared.MapSelectionParent == 1
            && cleared.MapSelectionParentAuthor == owner,
            "A physical close must name the exact displayed opening");
        source.Observer.AdoptSelection(source.First, "local reopening of same physical card");
        PresenceState reopened = RemoteMapRoom.Snapshot(owner);
        Check(reopened.MapSelectionRevision == 3 && reopened.MapRoomSelectKey == opened.MapRoomSelectKey,
            "Reopening the same quest must receive a new causal token");

        var mirror = VersionWorld(carrier);
        RemoteMapRoom.Receive(owner, opened); RemoteMapRoom.Tick();
        Check(mirror.First.IsSelected && RemoteMapRoom.TryReadQuestSelectionOpening(opened.MapRoomSelectKey, out ulong mirrored)
            && mirrored == opening, "Receiver must adopt the exact authenticated owner opening");
        PresenceState relayed = RemoteMapRoom.Snapshot(carrier);
        Check(relayed.HasMapSelectionVersion && relayed.MapSelectionRevision == 1
            && relayed.MapSelectionParent == 1 && relayed.MapSelectionParentAuthor == owner
            && relayed.MapRoomSelectStamp == 0 && !RemoteMapRoom.HasAuthoredVersion,
            "Adoption must publish an explicit full-ID relay without minting a human selection");
        RemoteMapRoom.Leave(owner);
        PresenceState afterLeave = RemoteMapRoom.Snapshot(carrier);
        Check(mirror.First.IsSelected && afterLeave.HasMapSelectionVersion && afterLeave.MapSelectionParentAuthor == owner,
            "Agreed shared opening must survive its original author leaving");

        var secondMirror = VersionWorld(secondCarrier);
        RemoteMapRoom.Receive(owner, opened); RemoteMapRoom.Receive(carrier, relayed); RemoteMapRoom.Tick();
        RemoteMapRoom.Snapshot(secondCarrier);
        RemoteMapRoom.Leave(owner); RemoteMapRoom.Leave(carrier);
        PresenceState secondRelay = RemoteMapRoom.Snapshot(secondCarrier);
        Check(secondMirror.First.IsSelected && secondRelay.HasMapSelectionVersion
            && secondRelay.MapSelectionRevision == 1 && secondRelay.MapSelectionParent == 1
            && secondRelay.MapSelectionParentAuthor == owner && !RemoteMapRoom.HasAuthoredVersion,
            "Two surviving adopters must retain the original opening when author and other carrier leave");

        var late = VersionWorld(visitor); Map.IsLocked = true;
        RemoteMapRoom.Receive(secondCarrier, secondRelay); RemoteMapRoom.Tick();
        Check(RemoteMapRoom.Pending && !late.First.IsSelected,
            "Late visitor must retain a valid live-carrier opening through native loading locks");
        Map.IsLocked = false; RemoteMapRoom.Tick();
        Check(late.First.IsSelected && RemoteMapRoom.TryReadQuestSelectionOpening(opened.MapRoomSelectKey, out ulong lateOpening)
            && lateOpening == opening && RemoteMapRoom.VersionClock == 1,
            "Late visitor must adopt the original opening from a live carrier after author departure");
        RemoteMapRoom.Snapshot(visitor);
        RemoteMapRoom.Receive(owner, reopened); RemoteMapRoom.Tick();
        int dismissals = late.First.DeselectCalls;
        Check(late.First.IsSelected && dismissals == 1
            && RemoteMapRoom.TryReadQuestSelectionOpening(opened.MapRoomSelectKey, out ulong newOpening)
            && newOpening == SharedQuestSelectionVersion.TokenOf(3, owner),
            "Same-key new opening must re-enter its original native selection lifecycle");
        RemoteMapRoom.Receive(owner, cleared); RemoteMapRoom.Tick();
        Check(late.First.IsSelected && late.First.DeselectCalls == dismissals,
            "Delayed incoming clear must not close a newer opening of the same quest");
        PresenceState newestRelay = RemoteMapRoom.Snapshot(visitor);
        Check(newestRelay.MapSelectionRevision == 3 && newestRelay.MapSelectionParent == 3
            && !RemoteMapRoom.HasAuthoredVersion,
            "Same-key remote reopening must remain a relay rather than a newly authored event");

        var replacing = VersionWorld(visitor);
        RemoteMapRoom.Receive(owner, opened); RemoteMapRoom.Tick(); RemoteMapRoom.Snapshot(visitor);
        Map.IsLocked = true;
        RemoteMapRoom.Receive(carrier, VersionEvent(carrier, 2, 1, owner, replacing.Second)); RemoteMapRoom.Tick();
        replacing.First.Deselect(); Map.IsLocked = false; Time.unscaledTime = 2;
        replacing.Observer.DeselectTick();
        PresenceState retiredWhilePending = RemoteMapRoom.Snapshot(visitor);
        Check(RemoteMapRoom.Pending && !retiredWhilePending.HasMapSelectionVersion
            && !RemoteMapRoom.HasAuthoredVersion && RemoteMapRoom.VersionClock == 2,
            "Native unready cleanup must not mint a human clear over the pending replacement");
        RemoteMapRoom.Tick();
        Check(replacing.Second.IsSelected && RemoteMapRoom.Snapshot(visitor).MapSelectionParent == 2,
            "Versioned replacement must survive native unready retirement and preserve its original opening");

        foreach (bool clearFirst in new[] { false, true })
        {
            var room = VersionWorld(visitor);
            RemoteMapRoom.Receive(owner, opened); RemoteMapRoom.Tick(); RemoteMapRoom.Snapshot(visitor);
            var b = VersionEvent(carrier, 2, 1, owner, room.Second);
            if (clearFirst) { RemoteMapRoom.Receive(owner, cleared); RemoteMapRoom.Tick(); }
            RemoteMapRoom.Receive(carrier, b); RemoteMapRoom.Tick();
            if (!clearFirst) { RemoteMapRoom.Receive(owner, cleared); RemoteMapRoom.Tick(); }
            Check(room.Second.IsSelected && !room.First.IsSelected,
                "Concurrent select and clear must converge on the new opening in both native arrival orders");
        }

        var returned = VersionWorld(visitor);
        RemoteMapRoom.Receive(owner, opened); RemoteMapRoom.Tick(); RemoteMapRoom.Snapshot(visitor);
        MapRoomDriver.Active = false;
        Check(!RemoteMapRoom.Snapshot(visitor).HasMapRoom, "Presentation stand-down must publish no map-room participation");
        MapRoomDriver.Active = true;
        var resumed = RemoteMapRoom.Snapshot(visitor);
        Check(resumed.HasMapSelectionVersion && resumed.MapSelectionRevision == 1
            && resumed.MapSelectionParent == 1 && returned.First.IsSelected,
            "Presentation-only map return must retain its native opening without minting or replay");
        StoryComposite.PointOfNoReturn = true;
        Check(!RemoteMapRoom.Snapshot(visitor).HasMapSelectionVersion && RemoteMapRoom.VersionClock == 1,
            "Native quest commitment must retire browsing publication without a fabricated human clear");
        StoryComposite.PointOfNoReturn = false;
        Singleton<MapChoreographer>.Instance = new();
        returned.Observer.CompleteNativeCensus(returned.First, returned.Second);
        returned.First.Deselect(); Time.unscaledTime = 2; returned.Observer.DeselectTick();
        RemoteMapRoom.Snapshot(visitor);
        RemoteMapRoom.Receive(owner, opened); RemoteMapRoom.Tick();
        Check(!returned.First.IsSelected && !RemoteMapRoom.Pending,
            "Old map-opening snapshot must not reappear after native retirement and return");
        RemoteMapRoom.Receive(owner, reopened); RemoteMapRoom.Tick();
        Check(returned.First.IsSelected,
            "A new authenticated opening after map return must remain usable");
    }

    private static PresenceState OnSurface(PresenceState state, bool city, byte stamp = 0)
    {
        state.MapRoomFlags |= NetProtocol.MapRoomSurfaceKnownBit;
        if (city) state.MapRoomFlags |= NetProtocol.MapRoomSurfaceCityBit;
        else state.MapRoomFlags &= unchecked((byte)~NetProtocol.MapRoomSurfaceCityBit);
        state.MapRoomSurfaceStamp = stamp;
        return state;
    }

    private static void NativeSurface(bool city)
    {
        MapParchment.NativeCity = city;
        MapIconLayer.CurrentSurface = city ? MapIconLayer.MapSurface.City : MapIconLayer.MapSurface.World;
        Map.IsLocked = false; // explicit original MapTransitioner final callback/ACK boundary
    }

    private static void NativeCensusDelivery()
    {
        const int owner = 1001001, local = 3003003;
        MapLocation a = new("census-A", "quest-A"), b = new("census-B", "quest-B"), c = new("census-C", "quest-C");
        var observer = Reset(client: true);
        Singleton<MapChoreographer>.Instance.SetInitialised(false);
        RemoteMapRoom.Begin(observer); RemoteMapRoom.Snapshot(local);
        PresenceState selected = OnSurface(VersionEvent(owner, 1, 0, 0, b), false);
        RemoteMapRoom.Receive(owner, selected); RemoteMapRoom.Tick();
        Check(RemoteMapRoom.Pending && observer.PointerDispatches == 0,
            "First zero-row native census must retain a valid incoming opening without a native click");
        observer.CompleteNativeCensus(a); RemoteMapRoom.Tick();
        Check(RemoteMapRoom.Pending && observer.PointerDispatches == 0,
            "Partial native census must retain the same live opening while InitMap is still constructing rows");
        observer.CompleteNativeCensus(a, b); RemoteMapRoom.Tick();
        Check(RemoteMapRoom.Pending && observer.PointerDispatches == 0,
            "Even complete-looking rows cannot replace native m_Initialised admission");
        Singleton<MapChoreographer>.Instance.SetInitialised(true); RemoteMapRoom.Tick();
        Check(observer.PointerDispatches == 0,
            "Native initialization requires an actual completed ready census before delivery");
        int sameSetGeneration = observer.ScanGeneration;
        observer.CompleteNativeCensus(a, b); RemoteMapRoom.Tick();
        Check(b.IsSelected && observer.PointerDispatches == 1 && observer.ScanGeneration == sameSetGeneration
            && RemoteMapRoom.TryReadQuestSelectionOpening(selected.MapRoomSelectKey, out ulong delivered)
            && delivered == SharedQuestSelectionVersion.TokenOf(1, owner),
            "A completed initialized census must deliver the original opening even when the row set generation did not change");
        RemoteMapRoom.Tick(); RemoteMapRoom.Tick();
        Check(observer.PointerDispatches == 1, "Census readiness must never become a repeated native click loop");

        foreach (bool committed in new[] { false, true })
        {
            observer = Reset(client: true); a = new("initial-controller-A", "A"); b = new("initial-controller-B", "B");
            RemoteMapRoom.Begin(observer, a, b); RemoteMapRoom.Snapshot(local);
            Singleton<UIMapMultiplayerController>.Instance = null!;
            RemoteMapRoom.Receive(owner, OnSurface(VersionEvent(owner, 1, 0, 0, b), false)); RemoteMapRoom.Tick();
            RemoteMapRoom.Snapshot(local);
            Check(observer.PointerDispatches == 0 && RemoteMapRoom.VersionClock == 0,
                "Missing native proposal controller must hold the latest peer snapshot without merging or fencing it");
            Singleton<UIMapMultiplayerController>.Instance = new();
            if (committed) Multiplayer.ProxyHostSelectedLocation(a);
            RemoteMapRoom.Tick();
            Check(committed ? observer.PointerDispatches == 0 && !b.IsSelected
                    : observer.PointerDispatches == 1 && b.IsSelected,
                "Native controller initialization must distinguish ordinary browsing from a genuine host proposal");
            if (!committed)
            {
                RemoteMapRoom.Snapshot(local); Singleton<UIMapMultiplayerController>.Instance = null!;
                RemoteMapRoom.Tick(); RemoteMapRoom.Snapshot(local);
                Singleton<UIMapMultiplayerController>.Instance = new(); RemoteMapRoom.Tick();
                PresenceState resumedOpening = RemoteMapRoom.Snapshot(local);
                Check(b.IsSelected && observer.PointerDispatches == 1 && resumedOpening.HasMapSelectionVersion
                    && resumedOpening.MapSelectionRevision == 1 && resumedOpening.MapSelectionParent == 1,
                    "An accepted opening must survive temporary native-controller absence with its original relay token");
            }
            if (committed)
            {
                Multiplayer.CancelNativeProposal(); RemoteMapRoom.Tick();
                Check(!b.IsSelected, "A measured committed proposal must retire preceding browsing rather than replay it after cancellation");
            }
        }

        observer = Reset(client: true); b = new("unknown-initial-surface", "B");
        RemoteMapRoom.Begin(observer, b); RemoteMapRoom.Snapshot(local);
        MapIconLayer.CurrentSurface = MapIconLayer.MapSurface.Unknown;
        RemoteMapRoom.Receive(owner, VersionEvent(owner, 1, 0, 0, b)); RemoteMapRoom.Tick();
        Check(RemoteMapRoom.Pending && observer.PointerDispatches == 0,
            "Unknown initial surface must retain the opening without guessing or pressing a cap");
        NativeSurface(false); RemoteMapRoom.Tick();
        Check(b.IsSelected && observer.PointerDispatches == 1 && MapRoomDriver.SurfacePresses == 0,
            "First measured native surface must complete initialization rather than retire its pending opening");

        observer = Reset(client: true); a = new("atomic-world", "world"); b = new("atomic-city", "city");
        RemoteMapRoom.Begin(observer, a); RemoteMapRoom.Snapshot(local);
        PresenceState world = OnSurface(default, false); world.HasMapRoom = true; world.MapRoomFlags |= 1;
        RemoteMapRoom.Receive(owner, world); RemoteMapRoom.TickSurfaceOnly();
        PresenceState city = OnSurface(VersionEvent(owner, 1, 0, 0, b), true, 1);
        RemoteMapRoom.Receive(owner, city); RemoteMapRoom.TickSurfacePacket();
        Check(MapRoomDriver.SurfacePresses == 1 && Map.IsLocked && RemoteMapRoom.Pending && observer.PointerDispatches == 0,
            "Atomic surface-switch and selection packet must survive the original transition mask and old cache");
        NativeSurface(true); RemoteMapRoom.TickSurfacePacket();
        Check(RemoteMapRoom.Pending && observer.PointerDispatches == 0,
            "A changed native surface must wait for its own active-row census before lookup");
        observer.CompleteNativeCensus(b); RemoteMapRoom.TickSurfacePacket();
        Check(b.IsSelected && observer.PointerDispatches == 1 && MapRoomDriver.SurfacePresses == 1,
            "Atomic City switch must dispatch its selected opening once after the actual City census");

        observer = Reset(client: true); a = new("ready-world", "world"); b = new("ready-city", "city");
        RemoteMapRoom.Begin(observer, a); RemoteMapRoom.Snapshot(local);
        RemoteMapRoom.Receive(owner, OnSurface(VersionEvent(owner, 1, 0, 0, a), false));
        RemoteMapRoom.Tick(); RemoteMapRoom.Snapshot(local); Map.IsLocked = true;
        RemoteMapRoom.Receive(owner, OnSurface(VersionEvent(owner, 2, 1, owner, b), true)); RemoteMapRoom.Tick();
        Check(RemoteMapRoom.Pending && MapQuestReadyUp.UnreadyRequested && MapQuestReadyUp.Edges.Count == 1
            && a.IsSelected && MapRoomDriver.SurfacePresses == 0,
            "Readied World A must request original unready before waiting for an unresolved City B or its locked cap");
        RemoteMapRoom.Tick();
        Check(MapQuestReadyUp.Edges.Count == 1 && MapRoomDriver.SurfacePresses == 0,
            "Unresolved cross-surface readiness withdrawal must run once and await native authorization");
        a.Deselect(); Map.IsLocked = false; Time.unscaledTime = 2; observer.DeselectTick();
        PresenceState cleanup = RemoteMapRoom.Snapshot(local);
        Check(RemoteMapRoom.Pending && !cleanup.HasMapSelectionVersion && !RemoteMapRoom.HasAuthoredVersion,
            "Native unready retirement must preserve the unresolved cross-surface opening without minting a clear");
        RemoteMapRoom.Tick();
        Check(MapRoomDriver.SurfacePresses == 1 && Map.IsLocked && !b.IsSelected,
            "Late visitor may request one native surface cap after original unready authorization");
        NativeSurface(true); observer.CompleteNativeCensus(b); RemoteMapRoom.Tick();
        Check(b.IsSelected && observer.PointerDispatches == 2 && a.DeselectCalls == 1 && MapQuestReadyUp.Edges.Count == 1,
            "Readied cross-surface replacement must enter the original selected card once without duplicating retirement: "
            + $"selected={b.IsSelected}, dispatch={observer.PointerDispatches}, nativeDeselect={a.DeselectCalls}, readyEdges={MapQuestReadyUp.Edges.Count}");

        foreach (bool capExists in new[] { false, true })
        {
            observer = Reset(client: true); a = new("unavailable-world", "world"); b = new("unavailable-city", "city");
            RemoteMapRoom.Begin(observer, a); RemoteMapRoom.Snapshot(local);
            MapRoomDriver.SurfaceAvailable = capExists; MapRoomDriver.SurfaceAdmitted = false;
            RemoteMapRoom.Receive(owner, OnSurface(VersionEvent(owner, 1, 0, 0, b), true)); RemoteMapRoom.Tick();
            RemoteMapRoom.Tick(); RemoteMapRoom.Tick();
            Check(MapRoomDriver.SurfacePresses == 1 && observer.PointerDispatches == 0,
                "Unavailable native surface cap must never be pressed again for the same opening");
            observer.CompleteNativeCensus(a); RemoteMapRoom.Tick();
            Check(!RemoteMapRoom.Pending && !b.IsSelected,
                "A fresh ready census after cap refusal must retire the undeliverable opening");
            NativeSurface(true); observer.CompleteNativeCensus(b); RemoteMapRoom.Tick();
            Check(!b.IsSelected && observer.PointerDispatches == 0,
                "A refused opening must not replay after a later unrelated map switch");
        }

        observer = Reset(client: true); a = new("delayed-A", "A"); b = new("delayed-B", "B");
        RemoteMapRoom.Begin(observer, a); RemoteMapRoom.Snapshot(local);
        RemoteMapRoom.Receive(owner, OnSurface(VersionEvent(owner, 1, 0, 0, b), false)); RemoteMapRoom.Tick();
        Check(RemoteMapRoom.Pending && observer.PointerDispatches == 0,
            "A ready cache miss must retain its opening until one fresh qualified census");
        observer.CompleteNativeCensus(a, b); RemoteMapRoom.Tick();
        Check(b.IsSelected && observer.PointerDispatches == 1,
            "A row arriving after the packet must remain deliverable in its next qualified census");

        foreach (string supersession in new[] { "local-refusal", "local-selection", "new-opening", "surface", "carrier-surface", "interactor", "map", "proposal" })
        {
            observer = Reset(client: true); a = new("obsolete-A", "A"); b = new("obsolete-B", "B"); c = new("obsolete-C", "C");
            RemoteMapRoom.Begin(observer, a, c); RemoteMapRoom.Snapshot(local);
            if (supersession == "local-refusal")
            {
                observer.AdoptSelection(a, "physical old selection"); RemoteMapRoom.Snapshot(local);
                MapRoomDriver.SurfaceAdmitted = false;
            }
            bool wantsCity = supersession == "local-refusal";
            uint counter = wantsCity ? 2u : 1u;
            PresenceState pending = OnSurface(VersionEvent(owner, counter, wantsCity ? 1u : 0u, wantsCity ? local : 0, b), wantsCity);
            RemoteMapRoom.Receive(owner, pending); RemoteMapRoom.Tick();
            Check(RemoteMapRoom.Pending, "Supersession case must contain a genuinely unresolved opening: " + supersession);
            switch (supersession)
            {
                case "local-refusal":
                    a.Selectable = false; MapLocationInteractor.HandleSharedQuestClose(Popups.Selected.Window);
                    RemoteMapRoom.Snapshot(local); a.Selectable = true;
                    NativeSurface(true); observer.CompleteNativeCensus(b); break;
                case "local-selection": observer.AdoptSelection(c, "physical newer local choice"); RemoteMapRoom.Snapshot(local); break;
                case "new-opening": RemoteMapRoom.Receive(owner, OnSurface(VersionEvent(owner, 2, 1, owner, c), false)); break;
                case "surface": NativeSurface(true); break;
                case "carrier-surface": pending = OnSurface(pending, true); RemoteMapRoom.Receive(owner, pending); break;
                case "interactor": MapRoomDriver.ActiveLocations = new(); break;
                case "map": Singleton<MapChoreographer>.Instance = new(); break;
                case "proposal": Multiplayer.ProxyHostSelectedLocation(c); break;
            }
            RemoteMapRoom.Tick();
            Check(!RemoteMapRoom.Pending && !b.IsSelected,
                "Obsolete unresolved opening must retire before native delivery: " + supersession);
            if (supersession == "new-opening" || supersession == "local-selection")
                Check(c.IsSelected, "New accepted local or remote opening must replace obsolete unresolved delivery");
            if (supersession == "proposal") Multiplayer.CancelNativeProposal();
            if (supersession == "interactor") MapRoomDriver.ActiveLocations = observer;
            NativeSurface(false); observer.CompleteNativeCensus(a, b, c); RemoteMapRoom.Receive(owner, pending); RemoteMapRoom.Tick();
            Check(!b.IsSelected, "Superseded unresolved opening must not replay in later native rows: " + supersession);
        }

        observer = Reset(client: true); a = new("invalid-A", "A"); b = new("invalid-B", "B");
        RemoteMapRoom.Begin(observer, a); RemoteMapRoom.Snapshot(local);
        selected = OnSurface(VersionEvent(owner, 1, 0, 0, b), false);
        RemoteMapRoom.Receive(owner, selected); RemoteMapRoom.Tick();
        observer.CompleteNativeCensus(a); RemoteMapRoom.Tick();
        Check(!RemoteMapRoom.Pending && observer.PointerDispatches == 0,
            "A fresh initialized census that still lacks the key must consume the invalid opening");
        observer.CompleteNativeCensus(a, b); RemoteMapRoom.Receive(owner, selected); RemoteMapRoom.Tick();
        Check(!b.IsSelected, "Consumed invalid opening must not replay onto an unrelated future native location");
    }

    private static void PublicCityDecision()
    {
        const int owner = 1001001, local = 2002002;
        foreach (bool client in new[] { false, true })
        foreach (string phase in new[] { "city proposal", "city ACK wait", "native encounter" })
        {
            var observer = Reset(client); var a = new MapLocation("public-A", "A"); var b = new MapLocation("public-B", "B");
            RemoteMapRoom.Begin(observer, a, b); RemoteMapRoom.Snapshot(local);
            MapCityEventReadyUp.OwnsMapDecision = true;
            Check(MapInputGate.IsBlocked && NativeMapQuestSelection.BlocksSharedBrowsing,
                "Public city/encounter decision blocks every participant's browsing: " + phase);
            Check(!observer.PrepareSharedSelectionChange(b) && MapQuestReadyUp.Edges.Count == 0,
                "Public city ownership cannot trigger quest-unready while preparing a cosmetic selection");
            Check(!observer.AdoptSelection(b, "blocked public decision") && !b.IsSelected,
                "Public city ownership rejects native cosmetic selection before dispatch");
            var old = VersionEvent(owner, 1, 0, 0, b);
            RemoteMapRoom.Receive(owner, old); RemoteMapRoom.Tick();
            Check(!b.IsSelected && !RemoteMapRoom.Pending && observer.PointerDispatches == 0,
                "Public city ownership retires incoming browsing instead of queuing a future click");
            MapCityEventReadyUp.OwnsMapDecision = false;
            RemoteMapRoom.Receive(owner, old); RemoteMapRoom.Tick();
            Check(!b.IsSelected && !RemoteMapRoom.Pending,
                "A pre-city browsing opening cannot replay after public city retirement");
            RemoteMapRoom.Receive(owner, VersionEvent(owner, 2, 0, 0, b)); RemoteMapRoom.Tick();
            Check(b.IsSelected && observer.PointerDispatches == 1,
                "Fresh post-city browsing resumes through the unchanged original native selection");
        }

        var state = Delivery(); Map.IsLocked = true;
        RemoteMapRoom.Packet(1, 255, state.Second); RemoteMapRoom.Tick();
        Check(RemoteMapRoom.Pending, "Existing native lock can hold a browsing replacement before city begins");
        MapCityEventReadyUp.OwnsMapDecision = true; Map.IsLocked = false; RemoteMapRoom.Tick();
        Check(!RemoteMapRoom.Pending && !state.Second.IsSelected,
            "A new public city decision fences an already pending browsing replacement");
        MapCityEventReadyUp.OwnsMapDecision = false; RemoteMapRoom.Tick();
        Check(!state.Second.IsSelected, "City cancellation cannot replay a discarded pending browsing click");

        var observerOff = Reset(client: true); var before = new MapLocation("mode-A", "A");
        RemoteMapRoom.Begin(observerOff, before); RemoteMapRoom.Snapshot(local);
        var prior = VersionEvent(owner, 1, 0, 0, before);
        RemoteMapRoom.Receive(owner, prior); RemoteMapRoom.Tick();
        MapRoomDriver.Active = false; MapCityEventReadyUp.OwnsMapDecision = true;
        Check(!MapInputGate.PublicDecisionOwnsInput,
            "Original 2D input remains outside the VR city browsing gate");
        RemoteMapRoom.Snapshot(local);
        MapCityEventReadyUp.OwnsMapDecision = false; MapRoomDriver.Active = true;
        Check(!RemoteMapRoom.Snapshot(local).HasMapSelectionVersion,
            "Native city authority retires the old browsing epoch even across 2D presentation");
        before.Deselect(); observerOff.Sample();
        RemoteMapRoom.Receive(owner, prior); RemoteMapRoom.Tick();
        Check(!before.IsSelected, "Native city authority retires the old browsing epoch even across 2D presentation");
    }

    public static void Main()
    {
        foreach (bool flatHost in new[] { true, false })
        foreach (bool flatCompanion in new[] { true, false })
        foreach (bool reconnect in new[] { true, false }) Client(flatHost, flatCompanion, reconnect);
        Host(true); Host(false);
        DelayedInitialization(); LinkedQuest(); UncommittedBrowsing(); LockedDelivery(); VersionPolicy(); VersionDelivery(); NativeCensusDelivery(); PublicCityDecision();
        System.Console.WriteLine($"Map quest selection runtime: {_assertions} assertions; {_roles} host/client/reconnect paths; original native proposal, preview, cancel and selection methods.");
    }
}
