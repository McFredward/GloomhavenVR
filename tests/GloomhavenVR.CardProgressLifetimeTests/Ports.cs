using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Cards.Patches;

// Explicit fixture boundaries: Unity scheduling/layout, native rule/network methods and
// presentation controls. The queue, four actual driver callers, scene-release coordinator,
// native cancellation/tween cleanup bodies and production Harmony hooks are not ports.
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple=true)]
    internal sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type owner, string method) { } }
}
namespace GloomhavenVR.Core
{
    internal static class Loc { internal static string Mod(string key)=>key; }
    internal static class VRLog
    {
        internal static int Errors;
        internal static void Error(string area, string message) { Errors++; }
        internal static void Info(string area, string message) { }
        internal static void Note(string area, string message) { }
        internal static void Warn(string area, string message) { }
        internal static void Alert(string area, string message) { }
    }
    internal static class TickGuard
    { internal static void Run(string area, Action action, string scope) => action(); }
}
namespace ScenarioRuleLibrary { internal static class NamespacePort { } }
namespace GloomhavenVR.Core.Events
{
    internal static class ProgressWaitNotice { internal static string? CurrentHint; }
    internal static class VRModeStateMachine { internal static bool TargetingActive=false; }
}
namespace GloomhavenVR.Net
{ internal static class NetFigures { internal static int StableActorId(CPlayerActor actor)=>actor.Id; } }
namespace GloomhavenVR.WorldUI
{ internal static class ModalFallback { internal static bool BlockingWindowModalActive=false; } }
internal static class Time { internal static float unscaledTime=100; internal static int frameCount=100; }
internal enum CardHandMode { CardsSelection, ActionSelection, LoseCard }
internal sealed class CAbilityCard { }
internal sealed class CPlayerActor
{
    internal int Id=1;
    internal readonly CharacterClass CharacterClass=new();
    internal CAbilityExtraTurn.EExtraTurnType SelectingCardsForExtraTurnOfType;
}
internal sealed class CharacterClass
{
    internal readonly List<int> HandAbilityCards=new(),RoundAbilityCards=new();
}
internal sealed class CAbilityExtraTurn { internal enum EExtraTurnType { None,ExtraTurn } }
internal sealed class CPhase { internal enum PhaseType { SelectAbilityCardsOrLongRest,Action,EndTurn,ActionSelection } }
internal static class PhaseManager { internal static CPhase.PhaseType PhaseType=CPhase.PhaseType.SelectAbilityCardsOrLongRest; }
internal sealed class Choreographer
{ internal static Choreographer? s_Choreographer;internal bool ThisPlayerHasTurnControl=true; }
internal sealed class AbilityCardUI
{
    internal readonly CAbilityCard AbilityCard=new();
    internal bool IsSelected=false;
    internal int Updates, Cancels;
    internal void CancelLostAnimation() { Cancels++; }
    internal void UpdateCard() { Updates++; }
}
internal sealed class LTDescr { internal readonly int id; internal LTDescr(int id) { this.id=id; } }
internal static class LeanTween { internal static readonly List<int> Cancelled=new(); internal static void cancel(int id,string why) { Cancelled.Add(id); } }
internal sealed class PortNode { internal bool activeInHierarchy=true; }
internal class MonoPort
{
    internal readonly PortNode gameObject=new();
    internal int Stops;
    internal void StopAllCoroutines() { Stops++; }
}
internal sealed class PortLayout { internal bool enabled; }
internal sealed class PortElement { internal float minHeight=100; }
internal sealed class PortGiven { internal int Resets; internal void ResetLoseCard() { Resets++; } }
internal sealed class UIManager
{
    internal static readonly UIManager Instance=new();
    internal bool Locked;
    internal void RequestToggleLockUI(bool active,PortNode owner) { Locked=active; }
}
internal sealed class LockState { }
internal sealed class Navigation
{
    internal bool Locked;
    internal bool IsCurrentState<T>() => Locked;
    internal void ToPreviousState() { Locked=false; }
}
internal sealed class UINavigation { internal readonly Navigation StateMachine=new(); }
internal static class Singleton<T> where T:new() { internal static readonly T Instance=new(); }
internal partial class CardsHandUI : MonoPort
{
    internal CPlayerActor? PlayerActor;
    internal bool Owned=true,Confirmed=false,SelectionReady=false,ShortRestChoosing=false,IsTurn=true;
    internal readonly List<AbilityCardUI> cardsUI=new();
    internal readonly List<LTDescr> animations=new();
    internal readonly PortLayout verticalLayoutGroup=new();
    internal readonly PortElement layoutElement=new();
    internal readonly PortGiven givenCardsGroup=new();
    internal Action? onAnimateCardLostCallback;
    internal bool animatedLosingCard;
    // Native private UI setter is publicized in the production game reference. No rule data.
    internal bool AnimatingLostCards {get;set;}
    internal int Sorts;
    internal CardHandMode Mode=CardHandMode.ActionSelection;
    private void SortCards() { Sorts++; }
    internal void BeginAnimation()
    {
        ProductionHooks.Start(this);
        AnimatingLostCards=true; animatedLosingCard=true;
        animations.Add(new LTDescr(animations.Count+1));
        UIManager.Instance.Locked=true; Singleton<UINavigation>.Instance.StateMachine.Locked=true;
    }
    internal void CompleteAnimation()
    {
        animatedLosingCard=false; onAnimateCardLostCallback?.Invoke(); onAnimateCardLostCallback=null;
        AnimatingLostCards=false;
    }
    internal void Cancel() => ProductionHooks.Cancel(this,CancelAnimateCardLostOriginal);
    private void CancelAnimateCardLost() => Cancel();
    internal void Disable() => OnDisable();
}
internal static class ProductionHooks
{
    private static readonly System.Reflection.MethodInfo Starting=typeof(CardsHandUI_AnimateCardsLost_Lifetime).GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
    private static readonly System.Reflection.MethodInfo Cancelling=typeof(CardsHandUI_CancelAnimateCardLost_Lifetime).GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
    private static readonly System.Reflection.MethodInfo Cancelled=typeof(CardsHandUI_CancelAnimateCardLost_Lifetime).GetMethod("Postfix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
    internal static void Start(CardsHandUI hand) => Starting.Invoke(null,new object?[]{hand});
    internal static void Cancel(CardsHandUI hand, Action native)
    {
        object?[] arguments={hand,null}; Cancelling.Invoke(null,arguments);
        native(); // A native exception deliberately skips the postfix, like actual Harmony.
        Cancelled.Invoke(null,arguments);
    }
}
namespace GloomhavenVR.Cards
{
    internal static partial class CardsGameApi
    {
        internal static CardsHandUI? LongRest;
        internal static CPlayerActor? Drift;
        internal static int NativeCalls;
        internal static bool Throw=false;
        internal static bool InScenario=true,MayConfirm=false,MayReady=false;
        internal static bool IsLocalHand(CardsHandUI hand)=>hand.Owned;
        internal static bool IsConfirmed(CardsHandUI hand)=>hand.Confirmed;
        internal static bool IsSelectionReady(CardsHandUI hand)=>hand.SelectionReady;
        internal static bool IsShortRestChoosing(CardsHandUI hand)=>hand.ShortRestChoosing;
        internal static bool IsActionTurn(CardsHandUI hand)=>hand.IsTurn&&hand.Owned;
        internal static bool CanConfirm()=>MayConfirm;
        internal static bool ReadyToggleAvailable()=>MayReady;
        internal static CardsHandUI? LongRestTurnHand() => LongRest;
        internal static CardHandMode Mode(CardsHandUI hand) => hand.Mode;
        internal static bool AnimatingLostCards(CardsHandUI hand) => hand.AnimatingLostCards;
        internal static bool TryAdvanceLongRestTurn(CardsHandUI hand,out string step,out bool clicked)
        { NativeCalls++; step="native rest callback"; clicked=true; if(Throw)throw new InvalidOperationException("native action failure");return true; }
        internal static CPlayerActor? SelectionHandDrift() => Drift;
        internal static object? SelectedActor() => null;
        internal static string ActorLabel(CPlayerActor actor) => "native actor";
        internal static bool SwitchHandTo(CPlayerActor actor) { NativeCalls++;return true; }
        internal static bool CancelPickConfirmDialog() { NativeCalls++;return true; }
        internal static void SelectCard(CardsHandUI hand,CAbilityCard card) { NativeCalls++; }
        internal static string CardName(AbilityCardUI card) => "native card";
    }
    internal static class NativeCardPoolLifetime { internal static void ReturnSceneHands() { } }
    internal sealed class VRCard { internal AbilityCardUI? GameCard=new(); }
    internal sealed class PortRoot { internal int GetInstanceID()=>17; }
    internal sealed class PortTray
    {
        internal bool IsVisible=true;
        internal PortRoot? Root=new();
        internal string? Banner;
        internal int Writes;
        internal void SetPickStatus(string? text,string? confirm,string? undo) { Banner=text;Writes++; }
        internal void CancelPendingSelections() { }
    }
    internal sealed class PortFactory { internal void ReturnBorrowedFacesBeforeSceneLoad() { } }
    internal sealed class PortFan { internal void Add(VRCard card) { } }
    internal sealed class PortHalf { internal bool IsVisible=true,ReadOnly=false; }
    internal sealed partial class CardsDriver
    {
        internal static CardsDriver? Instance;
        private bool _longRestPumpQueued,_selSwitchQueued,_pickReopenBusy,_pickCommitBusy,_dirty;
        private bool _longRestPumpAnnounced,_longRestRetryReadyClicked;
        private float _longRestRetryOpenedAt,_longRestPumpNextTry,_selSwitchNextTry;
        private int _longRestRetryAttempts,_longRestHoldPhaseLogged,_selSwitchDriftFrame=-1;
        private const float LongRestRetryWindowSeconds=3,LongRestAnswerSettleSeconds=3.4f,LongRestAnimatingCeilingSeconds=20;
        private const int LongRestRetryMaxAttempts=3;
        private readonly List<CAbilityCard> _reopenKeep=new();
        private readonly List<VRCard> _fieldCards=new();
        private readonly List<VRCard> _halfBuffer=new();
        private readonly HashSet<VRCard> _pickExitFlown=new();
        private readonly Dictionary<object,object> _dismissFlightOriginals=new();
        private readonly PortTray _tray=new();
        private readonly PortFactory _factory=new();
        private readonly PortFan _fan=new();
        private readonly PortHalf _half=new();
        private bool _overlayGateBlocked,_modalInputBlocked,_burnLayoutPending;
        private bool _sceneFacesReturned;
        private void ResetActiveDismiss() { }
        private void ClearLaserHover() { }
        private void ClearActiveHover() { }
        private void RelayoutField() { }
        internal bool Busy(int which) => which switch {0=>_longRestPumpQueued,1=>_selSwitchQueued,2=>_pickReopenBusy,_=>_pickCommitBusy};
        internal bool Dirty => _dirty;
        internal bool Released => _sceneFacesReturned;
        internal string? Banner=>_tray.Banner;
        internal int Writes=>_tray.Writes;
        internal void Help(CardsHandUI? hand,bool visible=true,bool blocked=false,bool modal=false,bool burning=false,bool actions=true)
        { _tray.IsVisible=visible;_overlayGateBlocked=blocked;_modalInputBlocked=modal;_burnLayoutPending=burning;
          _halfBuffer.Clear();if(actions)_halfBuffer.Add(new VRCard());UpdateProgressStatus(hand); }
        internal void Request(int which,CardsHandUI hand)
        {
            if(which==0)PumpLongRestTurn();
            else if(which==1) { PumpSelectionHandSwitch(); Time.frameCount+=2; PumpSelectionHandSwitch(); }
            else if(which==2) { _reopenKeep.Add(new CAbilityCard());EnqueuePickReopen(hand,"test"); }
            else TryCommitPick(new VRCard(),hand,"test");
        }
    }
}
namespace GloomhavenVR.Cards.Patches
{
    internal static class PickFlowWatch
    { internal static int AnswerSeq=>0;internal static bool AnswerOutstandingFor(CardsHandUI hand,out float seconds){seconds=0;return false;}internal static void ClearAnswer() { } }
}
