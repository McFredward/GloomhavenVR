using System;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        LoadoutTransition();
        NativePartyContainerTransitions();
        NativeMapLocks();
        FailedTravelParking();
        Console.WriteLine($"Map flow production regression harness: {_assertions:N0} assertions passed.");
    }

    private static void FailedTravelParking()
    {
        var location = new MapLocation();
        var manager = new AdventureMapUIManager { locationToTravel = location };
        int confirmed = 0;
        Action<MapLocation> confirm = received =>
        {
            Check(ReferenceEquals(location, received), "Fallback keeps the exact native selected location");
            confirmed++;
        };
        MapRoomDriver.Active = true;
        MapTravelConfirm._standDown = false;
        MapTravelConfirm._parkStandDown = false;
        Check(!MapTravelConfirm.TravelShortcutGate.Run(manager, location, confirm),
            "Working VR travel parking still blocks accidental second-click travel");
        Check(ReferenceEquals(manager.onConfirmTravelCallback, confirm),
            "Ordinary VR selection retains the native callback for its explicit button");
        MapTravelConfirm._parkStandDown = true;
        bool runNative = MapTravelConfirm.TravelShortcutGate.Run(manager, location, confirm);
        Check(runNative, "Failed VR parking must restore the native travel confirmation path");
        if (runNative) manager.OnSelectedMapLocation(location, confirm);
        Check(confirmed == 1,
            "Failed parking dispatches the same native callback exactly once");
        manager.locationToTravel = location;
        manager.TravelPermitted = false;
        if (MapTravelConfirm.TravelShortcutGate.Run(manager, location, confirm)) manager.OnSelectedMapLocation(location, confirm);
        Check(confirmed == 1, "Native travel conditions still refuse invalid quest travel");
        manager.TravelPermitted = true;
        FFSNetwork.IsOnline = true;
        if (MapTravelConfirm.TravelShortcutGate.Run(manager, location, confirm)) manager.OnSelectedMapLocation(location, confirm);
        Check(confirmed == 1, "Fallback cannot replace multiplayer ready-up with local confirmation");
        FFSNetwork.IsOnline = false;
        MapTravelConfirm.Reset();
        Check(!MapTravelConfirm._parkStandDown, "A new map hierarchy must retry previously failed travel parking");
        Check(!MapTravelConfirm.TravelShortcutGate.Run(manager, location, confirm),
            "New map restores ordinary explicit VR travel confirmation");
        MapRoomDriver.Active = false;
        Check(MapTravelConfirm.TravelShortcutGate.Run(manager, location, confirm),
            "Flat map keeps native confirmation behavior");
        MapRoomDriver.Active = true;
    }

    private static void LoadoutTransition()
    {
        var owner = new UIWindow { IsOpen = true };
        var battleGoals = new UIWindow { IsOpen = true };
        var cards = new UIWindow { IsOpen = true };
        var equipment = new UIWindow { IsOpen = true };
        foreach (var child in new[] { battleGoals, cards, equipment }) child.transform.parent = owner.transform;
        var unrelated = new UIWindow { IsOpen = true };
        var questPopup = new UIWindow { IsOpen = true };
        var manager = new UILoadoutManager { IsOpen = true };
        var party = new NewPartyDisplayUI { window = owner };
        NewPartyDisplayUI.PartyDisplay = party;
        Singleton<UILoadoutManager>.Instance = manager;
        Singleton<UILoadoutManager>.IsInitialized = true;
        MapRoomDriver.Active = true;
        StoryComposite._curtainStanding = true;
        StoryComposite.CurtainMembers.AddRange(new[] { owner, battleGoals, cards, equipment, unrelated });
        party.hideRequests.Add(manager);
        Check(StoryComposite.CurtainRefuses(owner), "Intro-hidden party must retain its frozen curtain membership");
        Check(!StoryComposite.CurtainRefuses(questPopup), "Later quest popup remains outside the frozen curtain");
        // Reproduce the reported trap: another popup already floats, so the old empty-room
        // recovery cannot help. Native loadout reopens the SAME owner instance for decisions.
        party.hideRequests.Remove(manager);
        for (int frame = 0; frame < 120; frame++)
        {
            Check(!StoryComposite.CurtainRefuses(owner), "Reopened native loadout owner must escape the story curtain");
            foreach (var child in new[] { battleGoals, cards, equipment })
                Check(!StoryComposite.CurtainRefuses(child), "Every native loadout decision child must remain reachable");
            Check(StoryComposite.CurtainRefuses(unrelated), "Unrelated frozen member must not escape the curtain");
            Check(!StoryComposite.CurtainRefuses(questPopup), "An existing quest popup must not veto loadout ownership");
        }
        Check(StoryComposite.Withheld == 1, "Only unrelated frozen content counts toward the deadlock floor after handover");
        // No cache: waiting for another player, close/reopen, and successive quests all
        // re-evaluate the game's current owner/request rather than keeping a prior exemption.
        party.hideRequests.Add(manager);
        Check(StoryComposite.CurtainRefuses(owner), "Multiplayer wait with native manager hide request stays curtained");
        party.hideRequests.Remove(manager);
        manager.IsOpen = false;
        Check(StoryComposite.CurtainRefuses(owner), "Closing loadout rearms the curtain for the old owner");
        manager.IsOpen = true;
        owner.IsOpen = false;
        Check(!LoadoutWindowOwnership.IsCurrentContent(owner), "Closed native content has no current loadout ownership");
        owner.IsOpen = true;
        Check(!StoryComposite.CurtainRefuses(owner), "Reopening loadout reacquires the same owner without a timer");
        var replacement = new UIWindow { IsOpen = true };
        party.window = replacement;
        Check(StoryComposite.CurtainRefuses(owner), "Replacing native party window revokes the previous owner exemption");
        Check(LoadoutWindowOwnership.IsCurrentContent(replacement), "Current replacement receives native ownership");
        party.window = owner;
        cards.transform.parent = unrelated.transform;
        Check(StoryComposite.CurtainRefuses(cards), "Reparented decision child must lose the old owner exemption");
        Check(!LoadoutWindowOwnership.IsCurrentContent(null), "Null candidate is not native loadout content");
        owner.Destroyed = true;
        Check(!LoadoutWindowOwnership.IsCurrentContent(owner), "Destroyed candidate is not native loadout content");
        owner.Destroyed = false;
        manager.Destroyed = true;
        Check(!LoadoutWindowOwnership.IsCurrentContent(owner), "Destroyed loadout manager cannot own content");
        manager.Destroyed = false;
        party.Destroyed = true;
        Check(!LoadoutWindowOwnership.IsCurrentContent(owner), "Destroyed party display cannot own content");
        party.Destroyed = false;
        Singleton<UILoadoutManager>.IsInitialized = false;
        Check(!LoadoutWindowOwnership.IsCurrentContent(owner), "Cold singleton is not accessed for loadout ownership");
        Singleton<UILoadoutManager>.IsInitialized = true;
        MapRoomDriver.Active = false;
        Check(StoryComposite.CurtainRefuses(owner), "Loadout ownership exemption is map-only");
        MapRoomDriver.Active = true;
        StoryComposite._curtainLifted = true;
        Check(!StoryComposite.CurtainRefuses(unrelated), "Existing emergency curtain lift remains effective");
        StoryComposite._curtainLifted = false;
        StoryComposite._curtainStanding = false;
        Check(!StoryComposite.CurtainRefuses(unrelated), "Lapsed curtain remains transparent");
    }

    private static void NativePartyContainerTransitions()
    {
        var canvas = new UIWindow { IsOpen = true };
        var outer = new UIWindow { IsOpen = true, ID = UIWindowID.PartyPanel };
        var unrelated = new UIWindow { IsOpen = true, ID = UIWindowID.PartyPanel };
        var owner = new UIWindow { IsOpen = true };
        var decisions = new UIWindow { IsOpen = true };
        var sibling = new UIWindow { IsOpen = true };
        outer.transform.parent = canvas.transform;
        unrelated.transform.parent = canvas.transform;
        owner.transform.parent = outer.transform;
        decisions.transform.parent = owner.transform;
        sibling.transform.parent = outer.transform;
        var manager = new UILoadoutManager { IsOpen = true };
        var party = new NewPartyDisplayUI { window = owner };
        Singleton<UILoadoutManager>.Instance = manager;
        Singleton<UILoadoutManager>.IsInitialized = true;
        NewPartyDisplayUI.PartyDisplay = party;
        MapRoomDriver.Active = true;
        StoryComposite._curtainStanding = true;
        StoryComposite._curtainLifted = false;
        StoryComposite.CurtainMembers.Clear();
        StoryComposite.CurtainMembers.AddRange(new[] { outer, unrelated, sibling });

        // The reported two quests reuse the native outer PartyPanel and a DIFFERENT
        // inner party.window. Existing popups must not postpone the actual handover.
        foreach (string quest in new[] { "078", "039" })
        {
            party.hideRequests.Add(manager);
            Check(StoryComposite.CurtainRefuses(outer), "Native outer container stays curtained during quest intro " + quest);
            Check(ReferenceEquals(ModalFallback.RefusedAncestor(decisions), outer), "Native ancestor refusal must block required decision descendants during intro");
            party.hideRequests.Remove(manager);
            for (int frame = 0; frame < 120; frame++)
            {
                Check(!StoryComposite.CurtainRefuses(outer), "Native outer PartyPanel must escape the curtain when its different inner owner reopens");
                Check(ModalFallback.RefusedAncestor(decisions) == null, "Reopened decisions must escape the actual ancestor refusal walk");
                Check(StoryComposite.CurtainRefuses(unrelated), "Unrelated same-ID party container must remain curtained");
                Check(StoryComposite.CurtainRefuses(sibling), "Unrelated sibling content must retain its own frozen refusal");
                Check(!LoadoutWindowOwnership.IsCurrentContent(canvas), "A common native canvas must not acquire party ownership");
            }
            Check(StoryComposite.Withheld == 2, "Outer container handover must remove only its own frozen refusal");
            party.hideRequests.Add(manager);
            Check(StoryComposite.CurtainRefuses(outer), "Multiplayer wait must rearm the outer container refusal immediately");
            Check(ReferenceEquals(ModalFallback.RefusedAncestor(decisions), outer), "Multiplayer wait must block descendants through the native ancestor walk");
            party.hideRequests.Remove(manager);
            manager.IsOpen = false;
            Check(StoryComposite.CurtainRefuses(outer), "Ending a quest must revoke the outer container exemption");
            manager.IsOpen = true;
        }

        owner.IsOpen = false;
        Check(StoryComposite.CurtainRefuses(outer), "Closed inner native owner cannot release its container");
        owner.IsOpen = true;
        owner.transform.parent = unrelated.transform;
        Check(StoryComposite.CurtainRefuses(outer), "Reparenting the native owner revokes the former container immediately");
        Check(!StoryComposite.CurtainRefuses(unrelated), "Reparenting admits only the current native PartyPanel container");
        owner.transform.parent = outer.transform;
        var nearer = new UIWindow { IsOpen = true, ID = UIWindowID.PartyPanel };
        nearer.transform.parent = outer.transform;
        owner.transform.parent = nearer.transform;
        Check(!LoadoutWindowOwnership.IsCurrentContent(outer), "A higher PartyPanel ancestor cannot bypass the nearest native container");
        Check(LoadoutWindowOwnership.IsCurrentContent(nearer), "Nearest PartyPanel identity determines the live container");
        owner.ID = UIWindowID.PartyPanel;
        Check(!LoadoutWindowOwnership.IsCurrentContent(nearer), "An owner that is itself PartyPanel needs no additional ancestor exemption");
        owner.ID = UIWindowID.None;
        owner.transform.parent = outer.transform;
        var spacer = new UnityEngine.Transform { parent = outer.transform };
        owner.transform.parent = spacer;
        Check(LoadoutWindowOwnership.IsCurrentContent(outer), "Non-window hierarchy nodes must not hide the actual PartyPanel container");
        owner.transform.parent = null;
        Check(!LoadoutWindowOwnership.IsCurrentContent(outer), "Detached native owner must not leave a cached container exemption");
        Check(LoadoutWindowOwnership.IsCurrentContent(owner), "Detached current owner retains its own direct identity");
        owner.transform.parent = outer.transform;
        var replacement = new UIWindow { IsOpen = true };
        replacement.transform.parent = unrelated.transform;
        party.window = replacement;
        Check(StoryComposite.CurtainRefuses(outer), "Replacing the inner native owner revokes the old container");
        Check(!StoryComposite.CurtainRefuses(unrelated), "Replacement owner admits its own current native container");
        party.window = owner;
        foreach (var destroyed in new UnityEngine.Object[] { owner, outer, party, manager })
        {
            destroyed.Destroyed = true;
            Check(!LoadoutWindowOwnership.IsCurrentContent(outer), "Destroyed native ownership chain cannot keep a container exemption");
            destroyed.Destroyed = false;
        }
        party.window = null;
        Check(!LoadoutWindowOwnership.IsCurrentContent(outer), "Missing native owner cannot keep a container exemption");
        party.window = owner;
        Singleton<UILoadoutManager>.IsInitialized = false;
        Check(!LoadoutWindowOwnership.IsCurrentContent(outer), "Scene teardown must revoke container ownership before singleton access");
        Singleton<UILoadoutManager>.IsInitialized = true;
        MapRoomDriver.Active = false;
        Check(!LoadoutWindowOwnership.IsCurrentContent(outer), "Native container handover remains map-only");
        MapRoomDriver.Active = true;
        StoryComposite._curtainStanding = false;
        StoryComposite.CurtainMembers.Clear();
    }

    private static void NativeMapLocks()
    {
        Singleton<AdventureMapUIManager>.IsInitialized = false;
        Check(!MapInputGate.IsBlocked, "Cold map singleton does not invent a native lock");
        Check(!MapInputGate.IsBlockedBy(null), "Missing map manager has no observed native lock");
        var manager = new AdventureMapUIManager();
        Singleton<AdventureMapUIManager>.Instance = manager;
        Singleton<AdventureMapUIManager>.IsInitialized = true;
        var intro = new object();
        var loadout = new object();
        var options = new UnityEngine.GameObject();
        var travel = new MapTravelConfirm();
        Check(!MapInputGate.IsBlocked, "Unlocked native map admits input");
        Check(travel.Visible(options, manager), "Ordinary offline travel remains visible when unlocked");
        manager.lockRequests.Add(intro);
        Check(MapInputGate.IsBlocked, "Native map lock must block direct VR input");
        Check(!travel.Visible(options, manager), "Offline travel must disappear under the native map lock");
        manager.lockRequests.Add(loadout);
        manager.lockRequests.Remove(intro);
        Check(MapInputGate.IsBlocked, "Finishing story cannot release another native map lock");
        Check(!travel.Visible(options, manager), "Travel remains hidden throughout required loadout decisions");
        travel._parkedIsReadyToggle = true;
        foreach (bool active in new[] { false, true })
        foreach (bool ready in new[] { false, true })
        {
            options.activeInHierarchy = active;
            travel.ReadyVisible = ready;
            Check(travel.Visible(options, manager) == (active && ready), "Online ready visibility remains native while map is locked");
        }
        options.activeInHierarchy = true;
        travel._parkedIsReadyToggle = false;
        manager.lockRequests.Remove(loadout);
        Check(!MapInputGate.IsBlocked, "Last native unlock restores VR input immediately");
        Check(travel.Visible(options, manager), "Last native unlock restores offline travel immediately");
        options.activeInHierarchy = false;
        Check(!travel.Visible(options, manager), "Inactive native travel remains hidden even without a lock");
        options.activeInHierarchy = true;
        Check(!travel.Visible(options, null), "Offline travel without a live manager must not claim readiness");
        manager.lockRequests.Add(loadout);
        manager.Destroyed = true;
        Check(!MapInputGate.IsBlocked, "Destroyed manager cannot leave a stale cached lock");
        Check(!travel.Visible(options, manager), "Destroyed manager cannot display offline travel");
        Singleton<AdventureMapUIManager>.Instance = new AdventureMapUIManager();
        Check(!MapInputGate.IsBlocked, "Replacing map manager starts with its own current lock state");
    }
}
