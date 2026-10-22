using System.Runtime.CompilerServices;
using ScenarioRuleLibrary;
using UnityEngine.Events;

namespace UnityEngine.Events { public delegate void UnityAction(); }
namespace GloomhavenVR.Net { }
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog { internal static void Warn(string area, string message) { } }
}
namespace ScenarioRuleLibrary
{
    public class CBaseCard
    {
        public enum ECardPile { Discarded, Lost, Activated }
    }
    public sealed class CAbilityCard : CBaseCard
    {
        public CardAction SelectedAction = new();
        public List<CActiveBonus> ActiveBonuses = new() { new() };
    }
    public sealed class CardAction { public CBaseCard.ECardPile CardPile = CBaseCard.ECardPile.Lost; }
    public sealed class BonusData { public bool CannotCancel; }
    public sealed class BonusAbility { public BonusData? ActiveBonusData = new(); }
    public sealed class CActiveBonus
    {
        public enum EActiveBonusDurationType { Persistent, Summon }
        public EActiveBonusDurationType Duration;
        public BonusAbility? Ability = new();
        public bool IsFinished, IsFinishing;
        public bool Finished() => IsFinished;
        public bool Finishing() => IsFinishing;
    }
    public sealed class CPlayerActor
    {
        public bool IsDead, Controlled = true;
        public string? CharacterName;
        public readonly HashSet<CBaseCard> Active = new();
    }
}
public enum CardHandMode { CardsSelection, ActionSelection, LoseCard }
public sealed class AbilityCardUI
{
    public CPlayerActor? playerActor;
    public CAbilityCard AbilityCard = null!;
}
public sealed class CardsHandUI
{
    public CPlayerActor PlayerActor = null!;
    public List<AbilityCardUI> cardsUI = new();
    public CardHandMode currentMode;
}
public sealed class Choreographer
{
    public static Choreographer s_Choreographer = new();
    public bool ForceEnterToCardSelection;
    public bool PlayerToSelectAbilityCardsOrLongRest;
    public CPlayerActor? CurrentPlayerActor;
}
public static class FFSNetwork { public static bool IsOnline; }
public static class PhaseManager { public static object CurrentPhase = new(); }
public static class Singleton<T> where T : class, new()
{
    public static bool IsInitialized = true;
    public static T Instance = new();
}
public sealed class UIConfirmationBoxManager
{
    public bool IsOpen, IsRequested;
    public int NativePopupCount;
    public UnityAction? ShownConfirm;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ShowCancelActiveAbility(string abilityName, bool lost, UnityAction onActionConfirmed,
        string? explanation = null, UnityAction? onCancelled = null)
    {
        NativePopupCount++;
        IsRequested = true;
        ShownConfirm = onActionConfirmed;
    }
}
public sealed class CardsHandManager
{
    public static CardsHandManager Instance = new();
    public bool EnableCancelActiveAbilities = true, ThrowAfterRequest, SendForeignRequest, RemoveOnConfirm = true;
    public CardsHandUI? Hand;
    public int NativeAccepted, NativeNetworked, NativeCancelled;
    public CardsHandUI? GetHand(CPlayerActor actor) => ReferenceEquals(Hand?.PlayerActor, actor) ? Hand : null;
    // Boundary model of the native DiscardActiveAbilityCard closure: production captures this
    // delegate rather than implementing its cancellation/continuation/network effects itself.
    public void DiscardActiveAbilityCard(CPlayerActor playerActor, CAbilityCard card)
    {
        if (!EnableCancelActiveAbilities) return;
        if (SendForeignRequest)
            new UIConfirmationBoxManager().ShowCancelActiveAbility("foreign", true, () => { });
        Singleton<UIConfirmationBoxManager>.Instance.ShowCancelActiveAbility("summon", true, () =>
        {
            NativeAccepted++;
            if (RemoveOnConfirm) playerActor.Active.Remove(card);
            Choreographer.s_Choreographer!.ForceEnterToCardSelection = true;
            NativeNetworked++;
        }, onCancelled: () => NativeCancelled++);
        if (ThrowAfterRequest) throw new InvalidOperationException("native preparation fault");
    }
}
namespace GloomhavenVR.Cards
{
    internal static class ActiveCardSet
    {
        internal static bool IsActive(CPlayerActor actor, CBaseCard card) => actor.Active.Contains(card);
    }
    internal static partial class CardsGameApi
    {
        internal static bool ControlsActor(CPlayerActor actor) => actor.Controlled;
    }
}
