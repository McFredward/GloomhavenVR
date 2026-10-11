using System;
using Code.State;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI.MapRoom;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.CampaignMapStates;
using Script.GUI.SMNavigation.States.CampaignMapStates.Enhancment;
using Script.GUI.SMNavigation.States.PopupStates;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static NavigationStateMachine Navigation(IState? state)
    {
        var machine = new NavigationStateMachine { CurrentState = state };
        Singleton<UINavigation>.IsInitialized = true;
        Singleton<UINavigation>.Instance = new UINavigation { StateMachine = machine };
        Singleton<AdventureMapUIManager>.IsInitialized = true;
        Singleton<AdventureMapUIManager>.Instance = new AdventureMapUIManager();
        return machine;
    }

    private static void Main()
    {
        // Each concrete type is the same admission category used by the original
        // selector. All public/solo/multiplayer packets lie outside this local path.
        foreach (IState? state in new IState?[] {
            new MapStoryState(), new PersonalQuestChoiceState(), new MerchantState(),
            new EnhancmentCardSelectState(), new EnhancmentConfirmationState(), new TempleState(),
            new MapEventState(), new TravelMapState(), new PersonalQuestCompletedState(),
            new CampaignRewardState(), new GuildmasterRewardsState(), new DistributeGoldRewardsState(),
            new UnknownState(), null })
        {
            ProtectedState(state);
            ProtectedRelease(state);
            NativeSelectorEligibility(state, admitted: false);
        }
        foreach (IState state in new IState[] { new WorldMapState(), new LoadoutState(), new LocationHoverState() })
        {
            OrdinaryTransitions(state);
            NativeSelectorEligibility(state, admitted: true);
        }
        PointerDispatchTakesOwnership();
        AbsentNavigation();
        Console.WriteLine($"Map navigation production regression: {_checks} assertions passed.");
    }

    private static void ProtectedState(IState? state)
    {
        var machine = Navigation(state);
        var interactor = new MapLocationInteractor();
        var wanted = new MapLocation();
        var hand = new VRHand();
        interactor.SetHover(wanted, "fixture pointer enter", pointer: hand);
        Check(wanted.EnterCount == 1 && hand.HapticCount == 1,
            "A protected navigation state does not remove native pointer entry or its feedback");
        Check(machine.EnterCount == 0 && ReferenceEquals(machine.CurrentState, state),
            "Protected native state must survive VR hover entry");
        interactor.SetHover(null, "fixture pointer exit");
        Check(wanted.ExitCount == 1 && interactor.HoverForFixture == null,
            "Native hover exit still clears the previous pointer and preview");
        Check(machine.EnterCount == 0 && machine.ExitCount == 0 && ReferenceEquals(machine.CurrentState, state),
            "Protected native state must survive VR hover exit without releasing its ownership");

        var hovered = new MapLocation();
        interactor.SeedHover(hovered);
        Singleton<AdventureMapUIManager>.Instance!.IsLocked = true;
        int entered = wanted.EnterCount;
        interactor.SetHover(wanted, "fixture native mask locked");
        Check(hovered.ExitCount == 1 && wanted.EnterCount == entered,
            "The native interaction mask still clears existing hover without entering another icon");
        Check(machine.EnterCount == 0 && ReferenceEquals(machine.CurrentState, state),
            "Clearing a map hover under the native mask cannot exit the new protected state");
    }

    private static void ProtectedRelease(IState? state)
    {
        var machine = Navigation(state);
        var interactor = new MapLocationInteractor();
        var hovered = new MapLocation();
        interactor.SeedHover(hovered);
        var ownedAdapter = interactor.SeedOwnedAdapter();
        VRHands.Left = new VRHand();
        VRHands.Right = new VRHand();
        int unregisters = VRInteractables.Unregistered;
        int exits = MapIconHoverAnimation.Exits;
        interactor.Release("fixture room teardown");
        Check(machine.EnterCount == 0 && machine.ExitCount == 0 && ReferenceEquals(machine.CurrentState, state),
            "Room release must retain the current protected native navigation state");
        Check(hovered.ExitCount == 1 && MapIconHoverAnimation.Exits == exits + 1 && interactor.HoverForFixture == null,
            "Room release retains native pointer exit and visual cleanup");
        Check(ownedAdapter.Destroyed && VRInteractables.Unregistered == unregisters + 1 && interactor.PadReleaseCount == 1,
            "Room release unregisters and destroys only its own input adapter");
        Check(VRHands.Left.Ray.Mask == 15 && VRHands.Right.Ray.Mask == 23,
            "Room release restores both original laser masks");
        interactor.Release("fixture idempotent teardown");
        Check(hovered.ExitCount == 1 && machine.EnterCount == 0,
            "Repeated room release cannot dispatch another native hover exit or state transition");
    }

    private static void OrdinaryTransitions(IState state)
    {
        var machine = Navigation(state);
        var interactor = new MapLocationInteractor();
        var location = new MapLocation();
        interactor.SetHover(location, "fixture ordinary map hover");
        Check(machine.EnterCount == 1 && machine.CurrentState is LocationHoverState
            && machine.Payload is MapLocationStateData data && ReferenceEquals(data.Location, location),
            "Ordinary native map states still enter hover with the exact location payload");
        interactor.SetHover(location, "fixture repeated pointer");
        Check(machine.EnterCount == 1 && location.EnterCount == 1,
            "An unchanged pointer is not another native state transition");
        interactor.SetHover(null, "fixture ordinary exit");
        Check(machine.EnterCount == 2 && machine.CurrentState is WorldMapState && location.ExitCount == 1,
            "Ordinary hover exit still returns navigation to the original world map state");
        interactor.SeedHover(location);
        interactor.Release("fixture ordinary room release");
        Check(machine.EnterCount == 3 && machine.CurrentState is WorldMapState && location.ExitCount == 2,
            "Ordinary room release preserves the native world map cleanup transition");
    }

    private static void PointerDispatchTakesOwnership()
    {
        var protectedState = new MapStoryState();
        var machine = Navigation(new WorldMapState());
        var location = new MapLocation { BeforeEnter = () => machine.CurrentState = protectedState };
        var interactor = new MapLocationInteractor();
        interactor.SetHover(location, "fixture native pointer callback acquires story");
        Check(machine.EnterCount == 0 && ReferenceEquals(machine.CurrentState, protectedState),
            "Eligibility must be measured after native pointer entry immediately before navigation Enter");
        machine.CurrentState = new LocationHoverState();
        location.BeforeExit = () => machine.CurrentState = protectedState;
        interactor.SetHover(null, "fixture native exit callback acquires story");
        Check(machine.EnterCount == 0 && ReferenceEquals(machine.CurrentState, protectedState),
            "Eligibility must be measured after native pointer exit immediately before navigation Enter");
    }

    private static void AbsentNavigation()
    {
        foreach (int condition in new[] { 0, 1, 2 })
        {
            Navigation(new WorldMapState());
            if (condition == 0) Singleton<UINavigation>.IsInitialized = false;
            if (condition == 1) Singleton<UINavigation>.Instance = null!;
            if (condition == 2) Singleton<UINavigation>.Instance!.StateMachine = null;
            var interactor = new MapLocationInteractor();
            var location = new MapLocation();
            interactor.SetHover(location, "fixture absent navigation");
            interactor.SetHover(null, "fixture absent navigation exit");
            Check(location.EnterCount == 1 && location.ExitCount == 1 && interactor.HoverForFixture == null,
                "Unavailable native navigation must preserve pointer entry/exit without throwing");
        }
    }

    private static void NativeSelectorEligibility(IState? state, bool admitted)
    {
        var machine = Navigation(state);
        var location = new MapLocation();
        var original = new NativeSelectorFixture(location);
        original.RunUpdate(hit: true);
        Check(machine.EnterCount == (admitted ? 1 : 0),
            "The original selector admits the same exact three native navigation states");
        original.RunUpdate(hit: false);
        Check(machine.EnterCount == (admitted ? 2 : 0),
            "The original selector owns its normal world-map return only inside eligible states");
    }
}
