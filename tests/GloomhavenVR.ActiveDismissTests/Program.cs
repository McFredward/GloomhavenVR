using GloomhavenVR.Cards;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;

static class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(message);
    }
    private static (CardsHandUI hand, AbilityCardUI widget) Setup()
    {
        VRSession.IsRunning = true;
        Singleton<UIConfirmationBoxManager>.IsInitialized = true;
        Singleton<UIConfirmationBoxManager>.Instance = new();
        Choreographer.s_Choreographer = new();
        PhaseManager.CurrentPhase = new();
        var actor = new CPlayerActor(); // Guildmaster may have no character name.
        var card = new CAbilityCard();
        actor.Active.Add(card);
        var widget = new AbilityCardUI { playerActor = actor, AbilityCard = card };
        var hand = new CardsHandUI { PlayerActor = actor, cardsUI = new() { widget } };
        CardsHandManager.Instance = new() { Hand = hand };
        return (hand, widget);
    }
    private static CardsGameApi.ActiveCardDismissal Arm(CardsHandUI hand, AbilityCardUI widget)
    {
        var pending = CardsGameApi.RequestDismissActiveCard(hand, widget);
        Check(pending != null && pending.CanConfirm, "Placing must arm the captured original confirmation");
        Check(CardsHandManager.Instance.NativeAccepted == 0 && CardsHandManager.Instance.NativeNetworked == 0,
            "Placement must never accept or network a dismissal");
        Check(Singleton<UIConfirmationBoxManager>.Instance.NativePopupCount == 0,
            "The explicit board confirmation must not create a second native popup");
        return pending!;
    }
    private static void Main()
    {
        new Harmony("ghvr.test.active-dismiss").CreateClassProcessor(typeof(ActiveDismissConfirmation)).Patch();
        var (hand, widget) = Setup();
        widget.AbilityCard!.ActiveBonuses[0].Duration = CActiveBonus.EActiveBonusDurationType.Summon;
        var pending = Arm(hand, widget);
        Check(pending.DismissesSummon && pending.LosesCard, "Summon and native loss destination must be retained");
        Check(pending.Confirm(), "Explicit confirmation must run the original affirmative callback");
        Check(CardsHandManager.Instance.NativeAccepted == 1 && CardsHandManager.Instance.NativeNetworked == 1
            && Choreographer.s_Choreographer!.ForceEnterToCardSelection
            && !hand.PlayerActor!.Active.Contains(widget.AbilityCard!), "Original accepted effects and network must run together");
        Check(!pending.Confirm() && !pending.IsPending, "A completed confirmation is one shot");
        pending.Cancel();
        Check(CardsHandManager.Instance.NativeCancelled == 0, "Completed confirmations must not call cancel afterwards");

        (hand, widget) = Setup();
        pending = Arm(hand, widget);
        pending.Cancel(); pending.Cancel();
        Check(!pending.Confirm() && CardsHandManager.Instance.NativeAccepted == 0
            && CardsHandManager.Instance.NativeCancelled == 1 && hand.PlayerActor!.Active.Contains(widget.AbilityCard!),
            "Taking the card back must cancel once without ending its effect");

        (hand, widget) = Setup();
        CardsHandManager.Instance.RemoveOnConfirm = false;
        pending = Arm(hand, widget);
        Check(pending.Confirm() && !pending.Confirm() && CardsHandManager.Instance.NativeAccepted == 1,
            "Even a callback that leaves the model intact must be one shot");

        var staleChanges = new Action<CardsHandUI, AbilityCardUI>[] {
            (h,w) => h.PlayerActor!.Controlled = false,
            (h,w) => CardsHandManager.Instance.EnableCancelActiveAbilities = false,
            (h,w) => h.PlayerActor!.IsDead = true,
            (h,w) => h.PlayerActor!.Active.Clear(),
            (h,w) => w.AbilityCard!.ActiveBonuses[0].Ability!.ActiveBonusData!.CannotCancel = true,
            (h,w) => w.AbilityCard!.ActiveBonuses.Clear(),
            (h,w) => w.AbilityCard!.ActiveBonuses[0].IsFinished = true,
            (h,w) => w.AbilityCard!.ActiveBonuses[0].IsFinishing = true,
            (h,w) => h.cardsUI.Clear(),
            (h,w) => h.PlayerActor = new(),
            (h,w) => w.AbilityCard = new(),
            (h,w) => w.playerActor = new(),
            (h,w) => w.AbilityCard!.SelectedAction = null!,
            (h,w) => h.currentMode = CardHandMode.LoseCard,
            (h,w) => CardsHandManager.Instance = new() { Hand = h },
            (h,w) => Choreographer.s_Choreographer = new(),
            (h,w) => PhaseManager.CurrentPhase = new(),
            (h,w) => Singleton<UIConfirmationBoxManager>.Instance = new(),
            (h,w) => Singleton<UIConfirmationBoxManager>.Instance.IsOpen = true,
            (h,w) => Singleton<UIConfirmationBoxManager>.Instance.IsRequested = true,
            (h,w) => Singleton<UIConfirmationBoxManager>.IsInitialized = false,
            (h,w) => VRSession.IsRunning = false,
        };
        foreach (var change in staleChanges)
        {
            (hand, widget) = Setup();
            var originalManager = CardsHandManager.Instance;
            pending = Arm(hand, widget);
            change(hand, widget);
            Check(!pending.CanConfirm, "Every stale native permission or exact-object lifetime must revoke confirmation");
            Check(!pending.Confirm() && originalManager.NativeAccepted == 0 && !pending.IsPending,
                "A stale explicit click must cancel without invoking the original affirmative callback");
        }

        (hand, widget) = Setup();
        hand.PlayerActor!.Controlled = false;
        Check(!CardsGameApi.CanDismissActiveCard(hand, widget) && CardsGameApi.RequestDismissActiveCard(hand, widget) == null,
            "A remote actor must never gain a local dismissal offer");
        (hand, widget) = Setup();
        widget.AbilityCard!.ActiveBonuses[0].Ability!.ActiveBonusData!.CannotCancel = true;
        Check(!CardsGameApi.CanDismissActiveCard(hand, widget), "CannotCancel must be enforced before placing");

        (hand, widget) = Setup();
        CardsHandManager.Instance.ThrowAfterRequest = true;
        Check(CardsGameApi.RequestDismissActiveCard(hand, widget) == null
            && CardsHandManager.Instance.NativeAccepted == 0 && CardsHandManager.Instance.NativeCancelled == 1,
            "Native preparation faults must cancel captured callbacks without accepting");
        var confirmation = Singleton<UIConfirmationBoxManager>.Instance;
        confirmation.ShowCancelActiveAbility("ordinary", true, () => { });
        Check(confirmation.NativePopupCount == 1, "Capture scope must clear after exceptions");

        (hand, widget) = Setup();
        pending = Arm(hand, widget);
        confirmation = Singleton<UIConfirmationBoxManager>.Instance;
        confirmation.ShowCancelActiveAbility("ordinary", true, () => { });
        Check(confirmation.NativePopupCount == 1, "Ordinary native confirmation calls must pass after preparation");
        pending.Cancel();

        (hand, widget) = Setup();
        Check(CardsGameApi.RequestDismissActiveCard(null, widget) == null
            && CardsGameApi.RequestDismissActiveCard(hand, null) == null, "Missing presentation must not dismiss anything");
        Console.WriteLine($"Active dismissal: {_checks} assertions passed (production adapter and real Harmony prefix; game boundary simulated).");
    }
}
