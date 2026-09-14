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
        NativeMapLocks();
        Console.WriteLine($"Map flow production regression harness: {_assertions:N0} assertions passed.");
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
