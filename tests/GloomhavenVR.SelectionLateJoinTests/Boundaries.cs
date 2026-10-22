using ScenarioRuleLibrary;

namespace ScenarioRuleLibrary
{
    internal class CActor { }
    internal sealed class CPlayerActor : CActor
    {
        internal readonly string Name;
        internal bool ListedMine;
        internal bool IsUnderMyControl;
        internal bool IsDead;
        internal bool ControlledByAbility;
        internal CPlayerActor(string name, bool mine) { Name = name; ListedMine = mine; }
    }
    internal class CPhase
    {
        internal enum PhaseType { None, SelectAbilityCardsOrLongRest, Action, ActionSelection, TakeDamage }
    }
    internal class CMessageData
    {
        internal enum MessageType { PlayerToSelectAbilityCardsOrLongRest, StartTurn, ActionSelectionPhaseStart, CheckForInitiativeAdjustments }
        internal MessageType m_Type;
    }
}
internal static class PhaseManager { internal static CPhase.PhaseType PhaseType; }
internal enum CardHandMode { CardsSelection, LoseCard, ActionSelection }
internal sealed class CardsHandUI
{
    internal readonly CPlayerActor PlayerActor;
    internal CardHandMode currentMode = CardHandMode.CardsSelection;
    internal CardsHandUI(CPlayerActor actor) { PlayerActor = actor; }
}
internal sealed class CardsHandManager
{
    internal static CardsHandManager Instance = null!;
    internal CardsHandUI CurrentHand = null!;
    internal List<CardsHandUI> CardHandsUI = new();
    internal int NativeSwitches;
    internal CardsHandUI GetHand(CPlayerActor actor) => CardHandsUI.FirstOrDefault(h => ReferenceEquals(h.PlayerActor, actor))!;
    internal CardsHandUI GetActiveHand() => CurrentHand;
    internal void SwitchHand(CPlayerActor actor)
    {
        // Native boundary: the real method replaces currentHand and calls OnSwitchHand/ShowHands.
        CurrentHand = GetHand(actor);
        Choreographer.s_Choreographer.OnSwitchHandCalls++;
        NativeSwitches++;
    }
}
internal sealed class Choreographer
{
    internal static Choreographer s_Choreographer = null!;
    internal CMessageData? LastMessage;
    internal int OnSwitchHandCalls;
}
internal static class Singleton<T> where T : new()
{
    internal static bool IsInitialized = true;
    internal static T Instance = new();
}
internal sealed class AbilityEffectManager
{
    public AbilityEffectManager() { }
    internal bool IsControlAbilityAffectingActor(CPlayerActor actor) => actor.ControlledByAbility;
}
namespace UnityEngine
{
    internal static class Time { internal static int frameCount; internal static float unscaledTime; }
    internal static class Mathf { internal static int Clamp(int x, int min, int max) => Math.Clamp(x, min, max); internal static int Min(int a, int b) => Math.Min(a, b); }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static readonly List<string> Lines = new();
        internal static void Warn(string area, string text) => Lines.Add(text);
        internal static void Note(string area, string text) => Lines.Add(text);
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class ModalFallback { internal static bool BlockingWindowModalActive; }
}
namespace GloomhavenVR.Board
{
    internal static class CharacterFocus
    {
        // External focus boundary: no focus override in this card-selection reproduction.
        internal static CardsHandUI? PresentedHand(CardsHandUI? hand) => hand;
    }
}
namespace GloomhavenVR.Cards
{
    internal static partial class CardsGameApi
    {
        internal static CActor? Selected;
        internal static CardsHandUI? Decision;
        internal static bool InScenario = true;
        internal static CActor? SelectedActor() => Selected;
        internal static bool ControlsActor(CPlayerActor? actor) => actor != null && actor.ListedMine;
        internal static bool IsLocalHand(CardsHandUI hand) => ControlsActor(hand.PlayerActor);
        internal static CardsHandUI? DecidingHand() => Decision;
        internal static string ActorLabel(CPlayerActor actor) => actor.Name;
        internal static CardHandMode Mode(CardsHandUI hand) => hand.currentMode;
        internal static bool IsShortRestChoosing(CardsHandUI hand) => false;
        internal static bool IsSelectionPhase(CardsHandUI hand) => PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest;
        internal static bool IsLongRestSelected(CardsHandUI hand) => false;
        internal static bool IsShortRestSelected(CardsHandUI hand) => false;
        internal static bool IsSelectionReady(CardsHandUI hand) => false;
        internal static int SelectionCardsStillWanted(CardsHandUI hand) => 2;
        internal static bool IsPickConfirmDialogOpen(CardsHandUI hand) => false;
        internal static int PickCardsWanted() => 0;
    }
    internal static class CardActionQueue
    {
        internal static readonly Queue<(Action action, Action done)> Actions = new();
        internal static void Enqueue(Action action, Action done) => Actions.Enqueue((action, done));
        internal static void Pump() { var work = Actions.Dequeue(); work.action(); work.done(); }
    }
    internal sealed class VRCard { internal bool IsHeld => false; }
    internal sealed class FixtureTray
    {
        internal bool IsVisible = true;
        internal int Wanted;
        internal VRCard? Occupant(int slot) => null;
        internal void SetWantedSlots(int mask) => Wanted = mask;
    }
    internal sealed class ConfigValue { internal bool Value = true; }
    internal static class CardsConfig { internal static ConfigValue WantedSlotHint = new(); }
    internal sealed partial class CardsDriver
    {
        private float _selSwitchNextTry;
        private bool _selSwitchQueued;
        private int _selSwitchDriftFrame = -1;
        private bool _dirty;
        private bool _overlayGateBlocked = false;
        private float _overlayExitHeldSince;
        private (int holds, int flights)? _loggedOverlayExit;
        private int _pickLockedCount = 0;
        private readonly List<VRCard> _fieldCards = new();
        private readonly FixtureTray _tray = new();
        private bool BoardStillOwnsACardsExit(out int holds, out int flights) { holds = 0; flights = 0; return false; }
        private void LogOverlayHeldByExit(int holds, int flights) { }
        private bool PickFlowLive(CardsHandUI hand) => false;
        private void LogPlacementOffer(CardsHandUI hand, CardsHandUI? shown, bool offered) { }
        internal int Wanted => _tray.Wanted;
        internal bool Dirty => _dirty;
        internal bool Queued => _selSwitchQueued;
        internal float ExitHeldSince => _overlayExitHeldSince;
        internal (int holds, int flights)? LoggedExit => _loggedOverlayExit;
        internal void Tick() { PumpSelectionHandSwitch(); UpdateWantedSlots(CurrentHand()); }
    }
}
