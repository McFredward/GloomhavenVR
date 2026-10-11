using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Assets.Script.Misc;
using GloomhavenVR.WorldUI;
using MapRuleLibrary.Adventure;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    { internal HarmonyPatch(Type _, string __) { } }
    internal static class AccessTools
    {
        internal static FieldInfo? Field(Type type, string name) => type.GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
namespace UnityEngine.UI
{
    // UIWindow and UIShopItemWindow are components on the same native GameObject.
    internal class UIWindow
    {
        internal UIShopItemWindow? Shop;
        internal bool IsOpen = true;
        internal T? GetComponent<T>() where T : class => Shop as T;
    }
}
namespace Assets.Script.Misc
{
    internal interface ICallbackPromise { ICallbackPromise Then(Action onDone, Action? onCancel = null); }
    internal sealed class CallbackPromise : ICallbackPromise
    {
        private enum EState { Pending, Resolved, Canceled }
        private EState state;
        private Action? onDoneCallback, onCancelCallback;
        internal bool IsPending => state == EState.Pending;
        internal static CallbackPromise Resolved() { var promise = new CallbackPromise(); promise.Resolve(); return promise; }
        internal void Done(Action? callback, Action? onCancelled = null)
        {
            if (state == EState.Resolved) { callback?.Invoke(); return; }
            if (onCancelled != null) Cancelled(onCancelled);
            Action? previous = onDoneCallback;
            onDoneCallback = () => { previous?.Invoke(); callback?.Invoke(); };
        }
        internal void Cancelled(Action callback)
        {
            if (state == EState.Canceled) { callback(); return; }
            onCancelCallback += callback;
        }
        internal void Resolve() { if (state != EState.Pending) return; state = EState.Resolved; onDoneCallback?.Invoke(); }
        internal void Cancel() { if (state != EState.Pending) return; state = EState.Canceled; onCancelCallback?.Invoke(); }
        public ICallbackPromise Then(Action onDone, Action? onCancel = null)
        {
            var next = new CallbackPromise();
            next.Done(onDone);
            if (onCancel != null) next.Cancelled(onCancel);
            Done(next.Resolve); Cancelled(next.Cancel);
            return next;
        }
    }
}
namespace MapRuleLibrary.Adventure
{
    internal static class AdventureState { internal static MapState? MapState; }
    internal sealed class MapState { internal bool IsCampaign; internal HeadquartersState HeadquartersState = new(); }
    internal sealed class HeadquartersState
    { internal bool MerchantUnlocked, TempleUnlocked, EnhancerUnlocked; }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static int CloseFailures; internal static void Note(string _, string __) { } internal static void Warn(string _, string __) => CloseFailures++; }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class Setting { internal bool Value; }
    internal static class WorldUIConfig
    {
        internal static readonly Setting ImmersiveTownServices = new();
        internal static bool ConversionActive = true;
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active = true; }
}
internal static class Singleton<T> where T : class { internal static T? Instance; }
internal static class Debug { internal static void LogGUI(string _) { } }
internal static class InputManager { internal static bool GamePadInUse = false; }
internal sealed class NativeStepEvent
{
    private event Action<EMapFTUEStep>? Listeners;
    internal void AddListener(Action<EMapFTUEStep> listener) => Listeners += listener;
    internal void RemoveListener(Action<EMapFTUEStep> listener) => Listeners -= listener;
    internal void Invoke(EMapFTUEStep step) => Listeners?.Invoke(step);
}
internal enum EMapFTUEStep { None, CreatedSecondCharacter, VisitMerchant, BuyItem, InteractWithMap }
internal interface IMapFTUEStep { EMapFTUEStep Step { get; } Assets.Script.Misc.ICallbackPromise StartStep(); void FinishStep(); }
internal sealed class UIMapFTUEStep : IMapFTUEStep
{
    // Exact original field name; the production close guard identifies the native promise.
    private Assets.Script.Misc.CallbackPromise? promise;
    private bool IsActive => promise?.IsPending == true;
    private void StopWait() { }
    private readonly StepConfig config = new();
    private object? waitCoroutine;
    private sealed class StepConfig { internal float DelayStart = 0f; }
    private object StartCoroutine(object routine) => routine;
    private object WaitStart(float _) => new object();
    private void Show(Action _) { }
    private void Hide() { if (!DelayHide) CompleteHide(); }
    internal bool DelayHide;
    internal int FinishRequests;
    internal bool Pending => promise?.IsPending == true;
    internal Action? OnFinish;
    internal UIMapFTUEStep(EMapFTUEStep step) { Step = step; }
    public EMapFTUEStep Step { get; }
    public Assets.Script.Misc.ICallbackPromise StartStep()
    {
        object?[] args = { this, null };
        bool runOriginal = (bool)(typeof(TownServiceTutorialStepPatch).GetMethod("Prefix",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args) ?? true);
        if (!runOriginal) return (Assets.Script.Misc.ICallbackPromise)args[1]!;
        return NativeStartStep();
    }
    private Assets.Script.Misc.ICallbackPromise NativeStartStep()
    {
        if (IsActive) return promise!;
        promise = new Assets.Script.Misc.CallbackPromise();
        StopWait();
        if (config.DelayStart > 0f) waitCoroutine = StartCoroutine(WaitStart(config.DelayStart));
        else Show(CompleteHide);
        return promise;
    }
    public void FinishStep() { FinishRequests++; OnFinish?.Invoke(); NativeFinishStep(); }
    private void NativeFinishStep()
    {
        if (waitCoroutine != null) CompleteHide();
        else Hide();
    }
    internal void CompleteHide() { var pending = promise; if (pending?.IsPending != true) return; pending.Resolve(); if (ReferenceEquals(promise, pending)) promise = null; }
}
internal sealed partial class UIShopItemWindow
{
    // Name deliberately matches the game's serialized private field.
    private readonly UIMapFTUEStep ftueStep;
    private readonly ShopIntroduction introduction = new();
    private readonly ShopWindow shopWindow = new();
    private Action? onExit;
    internal bool NativeListenersAttached = true;
    internal int Cleanups;
    internal Action? ExitCallback { set => onExit = value; }
    internal Action? IntroHideCallback { set => introduction.OnHide = value; }
    internal bool IsOpen => shopWindow.IsOpen;
    internal UIMapFTUEStep Step => ftueStep;
    internal UIShopItemWindow(UIMapFTUEStep step) { ftueStep = step; shopWindow.Hidden = HiddenHook; shopWindow.Shop = this; }
    internal UnityEngine.UI.UIWindow ConvertedWindow => shopWindow;
    internal bool FadePending => shopWindow.FadePending;
    internal void DelayShopFade(bool delay) => shopWindow.DelayFade = delay;
    internal void CompleteShopFade() => shopWindow.FadePending = false;
    internal void Enter() => FinishEnterShop();
    internal void BeginFirstIntroduction() => introduction.Show(FinishEnterShop);
    internal void CompleteIntroduction() => introduction.Hide();
    private void TryFocus() { }
    private void FinishEnterShop()
    {
        if (InputManager.GamePadInUse)
        {
            NewPartyDisplayUI.PartyDisplay.SelectCurrentCharacter();
            TryFocus();
        }
        Singleton<MapFTUEManager>.Instance?.StartStep(ftueStep);
    }
    internal void TemporaryHide() => shopWindow.Hide();
    internal void Reopen() { shopWindow.IsOpen = true; NativeListenersAttached = true; }
    private void ClearEvents() { Cleanups++; NativeListenersAttached = false; }
    private void HiddenHook()
    {
        object?[] args = { this, null };
        typeof(TownServiceTutorialShopHiddenPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        OnHidden();
        typeof(TownServiceTutorialShopHiddenPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { args[1] });
    }
    public void Exit()
    {
        object?[] args = { this, null };
        typeof(TownServiceTutorialShopExitPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        try { NativeExit(); }
        finally { typeof(TownServiceTutorialShopExitPatch).GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { args[1] }); }
    }
    // These two native method bodies are substituted from the read-only decompilation
    // by the local source-bound runner. Portable CI exercises the same explicit ports.
    private void NativeExit()
    {
        introduction.Hide();
        shopWindow.Hide();
    }
    private void OnHidden()
    {
        NewPartyDisplayUI.PartyDisplay.DisableSelectionMode();
        ClearEvents();
        onExit?.Invoke();
    }
    // Explicit introduction-process port: native UIIntroduceBase invokes its finish
    // callback on both Done and Cancel. The runner binds FinishEnterShop itself.
    private sealed class ShopIntroduction
    {
        internal Action? OnHide;
        private Action? _finished;
        internal void Show(Action finished) => _finished = finished;
        internal void Hide() { var finished = _finished; _finished = null; OnHide?.Invoke(); finished?.Invoke(); }
    }
    private sealed class ShopWindow : UnityEngine.UI.UIWindow
    {
        internal Action? Hidden;
        internal bool DelayFade, FadePending;
        internal void Hide() { if (!IsOpen) return; Hidden?.Invoke(); IsOpen = false; FadePending = DelayFade; }
    }
}
internal sealed class NewPartyDisplayUI
{
    internal static readonly NewPartyDisplayUI PartyDisplay = new();
    internal bool SelectionEnabled = true;
    internal void DisableSelectionMode() => SelectionEnabled = false;
    internal void SelectCurrentCharacter() { }
}
internal sealed class MapFTUEManager
{
    // Native IsPlaying reads the live singleton, while completion callbacks use
    // their captured manager's own isPlaying field. Keep these lifetimes distinct.
    internal static bool IsPlaying
    {
        get => Singleton<MapFTUEManager>.Instance?.isPlaying == true;
        set { if (Singleton<MapFTUEManager>.Instance is { } manager) manager.isPlaying = value; }
    }
    private bool isPlaying;
    internal void StopPlaying() => isPlaying = false;
    private readonly List<EMapFTUEStep> stepsCompleted = new();
    // Name matches the game's identity-bearing current step.
    private IMapFTUEStep? currentStep;
    internal readonly List<EMapFTUEStep> Started = new();
    internal bool ThrowCompletionProbe;
    internal readonly NativeStepEvent OnStartedStep = new(), OnFinishedStep = new();
    internal event Action<EMapFTUEStep> Finished
    { add => OnFinishedStep.AddListener(value); remove => OnFinishedStep.RemoveListener(value); }
    internal EMapFTUEStep CurrentStep => !IsPlaying || currentStep == null ? EMapFTUEStep.None : currentStep.Step;
    internal bool HasCompletedStep(EMapFTUEStep step)
    { if (ThrowCompletionProbe) throw new InvalidOperationException("native completion probe unavailable"); return stepsCompleted.Contains(step); }
    internal void SetComplete(EMapFTUEStep step) => stepsCompleted.Add(step);
    internal void CompleteStep(EMapFTUEStep step)
    { if (isPlaying && CurrentStep == step) currentStep?.FinishStep(); }
    internal Assets.Script.Misc.ICallbackPromise StartStep(IMapFTUEStep step)
    {
        if (isPlaying && !HasCompletedStep(step.Step)) Started.Add(step.Step);
        var promise = NativeStartStep(step);
        typeof(TownServiceTutorialSequencePatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { this, step });
        return promise;
    }
    private void CheckStartedStep(EMapFTUEStep _) { }
    private void CheckCompletedStep(EMapFTUEStep _) { }
    private Assets.Script.Misc.ICallbackPromise NativeStartStep(IMapFTUEStep step)
    {
        if (!isPlaying || HasCompletedStep(step.Step)) return Assets.Script.Misc.CallbackPromise.Resolved();
        if (currentStep != null) CompleteStep(currentStep.Step);
        currentStep = step;
        var promise = step.StartStep();
        CheckStartedStep(step.Step);
        OnStartedStep.Invoke(step.Step);
        return promise.Then(() => OnCompleteStep(step.Step));
    }
    private void OnCompleteStep(EMapFTUEStep step)
    {
        if (isPlaying && (currentStep == null || currentStep.Step == step))
        {
            stepsCompleted.Add(step);
            currentStep = null;
            CheckCompletedStep(step);
            OnFinishedStep.Invoke(step);
        }
    }
}

internal sealed class NativeMapNextStepListener
{
    private readonly EMapFTUEStep stepToContinue;
    private readonly UIMapFTUEStep stepToStart;
    internal NativeMapNextStepListener(EMapFTUEStep previous, UIMapFTUEStep next)
    {
        stepToContinue = previous; stepToStart = next;
        Singleton<MapFTUEManager>.Instance!.OnFinishedStep.AddListener(OnStepCompleted);
    }
    private void OnStepCompleted(EMapFTUEStep step)
    {
        if (stepToContinue == step)
        {
            Singleton<MapFTUEManager>.Instance!.OnFinishedStep.RemoveListener(OnStepCompleted);
            Singleton<MapFTUEManager>.Instance.StartStep(stepToStart);
        }
    }
}

internal sealed class NativeWorldMapToggleListener
{
    private readonly EMapFTUEStep step = EMapFTUEStep.BuyItem;
    private readonly bool completedOnToggledOn = true;
    internal bool Attached = true;
    private void Clear() => Attached = false;
    internal void Toggle(bool isOn) { if (Attached) OnValueChanged(isOn); }
    private void OnValueChanged(bool isOn)
    {
        if (completedOnToggledOn == isOn) CompleteStep();
    }
    private void CompleteStep()
    {
        Singleton<MapFTUEManager>.Instance!.CompleteStep(step);
        Clear();
    }
}

// Purchase/confirmation is an explicit boundary port, not a substitute game
// inventory implementation. Source-bound purchase bodies are hashed and checked
// separately for the absence of an FTUE completion path.
internal sealed class PurchaseConfirmationPort
{
    private readonly UIShopItemWindow shop;
    internal int Gold = 30, Items;
    internal bool Open;
    internal PurchaseConfirmationPort(UIShopItemWindow original) => shop = original;
    internal void Show() => Open = true;
    internal void Confirm() { Gold -= 10; Items++; CloseConfirmation(); }
    internal void Cancel() => CloseConfirmation();
    private void CloseConfirmation() { Open = false; _ = shop; }
}

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string text)
    { _checks++; if (!condition) throw new Exception(text); }
    private static bool DoesNotThrow(Action action) { try { action(); return true; } catch { return false; } }
    private static void Setup(bool immersive, bool campaign, bool merchantStep = true)
    {
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = true;
        WorldUIConfig.ConversionActive = true;
        NewPartyDisplayUI.PartyDisplay.SelectionEnabled = true;
        WorldUIConfig.ImmersiveTownServices.Value = immersive;
        AdventureState.MapState = new MapState { IsCampaign = campaign,
            HeadquartersState = { MerchantUnlocked = true, TempleUnlocked = true, EnhancerUnlocked = true } };
        Singleton<MapFTUEManager>.Instance = new MapFTUEManager();
        MapFTUEManager.IsPlaying = true;
        Singleton<MapFTUEManager>.Instance.SetComplete(EMapFTUEStep.CreatedSecondCharacter);
        Singleton<UIShopItemWindow>.Instance = merchantStep
            ? new UIShopItemWindow(new UIMapFTUEStep(EMapFTUEStep.BuyItem)) : null;
    }

    private static void MerchantCloseCases()
    {
        (MapFTUEManager manager, UIShopItemWindow shop) Pending(bool room = true)
        {
            Setup(false, true);
            GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = room;
            var manager = Singleton<MapFTUEManager>.Instance!;
            manager.SetComplete(EMapFTUEStep.VisitMerchant);
            var shop = Singleton<UIShopItemWindow>.Instance!;
            shop.Enter();
            Check(manager.CurrentStep == EMapFTUEStep.BuyItem && !manager.HasCompletedStep(EMapFTUEStep.BuyItem),
                "native shop starts its original pending BuyItem promise");
            return (manager, shop);
        }
        foreach (bool room in new[] { false, true })
        foreach (bool purchase in new[] { false, true })
        foreach (bool nativeExit in new[] { false, true })
        {
            var (manager, shop) = Pending(room);
            int gold = 30, items = 0, selection = 23;
            // The native step waits for shop exit, not a successful purchase. A lost
            // WorldMap callback reproduces the report without synthesizing native state.
            Action? worldMapListener = () => manager.CompleteStep(EMapFTUEStep.BuyItem);
            if (purchase) { gold -= 10; items++; worldMapListener = null; }
            int expectedGold = gold, expectedItems = items;
            bool cleanupObserved = false;
            manager.Finished += step =>
            {
                if (step != EMapFTUEStep.BuyItem) return;
                cleanupObserved = !NewPartyDisplayUI.PartyDisplay.SelectionEnabled
                    && !shop.NativeListenersAttached && shop.Cleanups > 0 && shop.IsOpen;
            };
            shop.ExitCallback = () => worldMapListener?.Invoke();
            if (nativeExit) shop.Exit();
            else using (GloomhavenVR.WorldUI.MapRoom.TownWindowCloseScope.Enter()) shop.TemporaryHide();
            Check(manager.HasCompletedStep(EMapFTUEStep.BuyItem) && cleanupObserved,
                "real converted merchant close completes BuyItem after native cleanup even with a consumed WorldMap listener");
            Check(gold == expectedGold && items == expectedItems && selection == 23,
                "merchant close changes neither purchased inventory nor selected character");
            Check(shop.Step.FinishRequests == 1,
                "a successful native completion is never dispatched twice by the bridge");
        }

        var pending = Pending();
        pending.shop.TemporaryHide();
        Check(!pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "temporary original shop presentation hide cannot consume a pending lesson");
        pending.shop.Reopen(); pending.shop.Exit();
        Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "the actual later native shop exit still completes its exact lesson");

        foreach (bool room in new[] { false, true })
        {
            pending = Pending(room);
            pending.shop.TemporaryHide(); // a purchase sibling consumed the real native hide
            Check(!pending.shop.IsOpen && pending.shop.Cleanups == 1
                && !pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
                "native-hidden sticky converted merchant remains pending until its real VR close");
            using (GloomhavenVR.WorldUI.MapRoom.TownWindowCloseScope.Enter())
            {
                var close = TownServiceTutorialPatches.CaptureConvertedMerchantClose(pending.shop.ConvertedWindow);
                TownServiceTutorialPatches.CompleteConvertedMerchantClose(pending.shop.ConvertedWindow, close);
                TownServiceTutorialPatches.CompleteConvertedMerchantClose(pending.shop.ConvertedWindow, close);
            }
            Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem) && pending.shop.Step.FinishRequests == 1,
                "real X of an already-native-hidden converted merchant completes its exact lesson once");
        }
        pending = Pending();
        using (GloomhavenVR.WorldUI.MapRoom.TownWindowCloseScope.Enter())
        {
            var close = TownServiceTutorialPatches.CaptureConvertedMerchantClose(pending.shop.ConvertedWindow);
            TownServiceTutorialPatches.CompleteConvertedMerchantClose(pending.shop.ConvertedWindow, close);
            Check(!pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
                "a converted close that leaves the native shop open cannot consume its lesson");
            pending.shop.TemporaryHide();
            TownServiceTutorialPatches.CompleteConvertedMerchantClose(pending.shop.ConvertedWindow, close);
            Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem) && pending.shop.Step.FinishRequests == 1,
                "normal native OnHidden and converted close completion share one exact promise");
        }
        pending = Pending(); pending.shop.TemporaryHide();
        var unscoped = TownServiceTutorialPatches.CaptureConvertedMerchantClose(pending.shop.ConvertedWindow);
        TownServiceTutorialPatches.CompleteConvertedMerchantClose(pending.shop.ConvertedWindow, unscoped);
        Check(!pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "a hidden converted window outside the actual close scope cannot consume onboarding");

        pending = Pending(); pending.shop.Step.DelayHide = true;
        bool nestedClose = false;
        pending.shop.Step.OnFinish = () => { if (nestedClose) return; nestedClose = true; pending.shop.Reopen(); pending.shop.Exit(); };
        pending.shop.Exit();
        Check(pending.shop.Step.FinishRequests == 1 && !pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "reentrant and delayed native hides request completion once per exact promise");
        pending.shop.Step.OnFinish = null; pending.shop.Step.CompleteHide();
        Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "native delayed step hide owns the eventual completed-state transition");

        pending = Pending();
        var foreign = new UIMapFTUEStep(EMapFTUEStep.BuyItem);
        pending.shop.Step.DelayHide = true;
        pending.manager.StartStep(foreign); // replace with another pending object using the same enum
        pending.shop.Exit();
        Check(foreign.FinishRequests == 0,
            "closing the shop cannot complete a different original step with the same enum");

        pending = Pending(); pending.shop.Step.DelayHide = true;
        pending.shop.ExitCallback = () => pending.manager.StartStep(new UIMapFTUEStep(EMapFTUEStep.InteractWithMap));
        pending.shop.Exit();
        Check(pending.manager.CurrentStep == EMapFTUEStep.InteractWithMap && pending.shop.Step.FinishRequests == 1,
            "native onExit replacing the current step invalidates the captured merchant continuation");

        pending = Pending();
        var oldManager = pending.manager;
        pending.shop.ExitCallback = () => Setup(false, true);
        pending.shop.Exit();
        Check(!oldManager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "native onExit replacing the manager invalidates the captured merchant continuation");

        pending = Pending();
        pending.shop.ExitCallback = () => AdventureState.MapState = new MapState { IsCampaign = true };
        pending.shop.Exit();
        Check(!pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "native onExit replacing the loaded save invalidates the captured merchant continuation");

        pending = Pending();
        pending.shop.ExitCallback = () => throw new InvalidOperationException("native close failure");
        try { pending.shop.Exit(); } catch (InvalidOperationException) { }
        pending.shop.ExitCallback = null;
        pending.shop.TemporaryHide();
        Check(!pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "failed native exit restores its scope and cannot turn a later temporary hide into progression");
        pending.shop.Reopen(); pending.shop.Exit();
        Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "a later valid exit recovers after native cleanup threw");

        pending = Pending(); pending.manager.ThrowCompletionProbe = true;
        bool captureSafe = DoesNotThrow(pending.shop.Exit);
        pending.manager.ThrowCompletionProbe = false;
        Check(captureSafe && pending.shop.Cleanups == 1 && !pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "a failing close capture cannot prevent the original native shop cleanup");
        pending.shop.Reopen(); pending.shop.Exit();
        Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem), "a later close retries after a transient capture failure");

        pending = Pending(); pending.shop.ExitCallback = () => pending.manager.ThrowCompletionProbe = true;
        bool postfixSafe = DoesNotThrow(pending.shop.Exit); pending.manager.ThrowCompletionProbe = false;
        Check(postfixSafe && pending.shop.Cleanups == 1 && !pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "a failing postfix eligibility probe cannot escape native shop dispatch");
        pending.shop.ExitCallback = null; pending.shop.Reopen(); pending.shop.Exit();
        Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem), "a later close retries after a transient eligibility failure");

        pending = Pending(); pending.shop.Step.OnFinish = () => throw new InvalidOperationException("native step hide unavailable");
        pending.shop.Exit();
        Check(!pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem), "a failed native step hide cannot fabricate completion");
        pending.shop.Step.OnFinish = null; pending.shop.Reopen(); pending.shop.Exit();
        Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem) && pending.shop.Step.FinishRequests == 2,
            "failed completion dispatch releases the pending promise marker for a later real close");
        Check(GloomhavenVR.Core.VRLog.CloseFailures == 1, "close continuation failures are reported once instead of flooding normal logs");

        pending = Pending();
        using (new TownServiceTutorialPatches.ShopExitScope(pending.shop))
        {
            default(TownServiceTutorialPatches.ShopExitScope).Dispose();
            pending.shop.TemporaryHide();
        }
        Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "a skipped prefix default finalizer cannot reset an outer native shop exit scope");

        pending = Pending(); WorldUIConfig.ConversionActive = false; pending.shop.Exit();
        Check(!pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem), "flat game outside VR conversion keeps its original close progression");
        pending = Pending(); AdventureState.MapState!.IsCampaign = false; pending.shop.Exit();
        Check(!pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem), "Guildmaster has no campaign close continuation");
        pending = Pending(); MapFTUEManager.IsPlaying = false; pending.shop.Exit();
        Check(!pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem), "finished campaign onboarding is never restarted on shop close");

        pending = Pending();
        WorldUIConfig.ImmersiveTownServices.Value = true;
        TownServiceTutorialPatches.Tick(); TownServiceTutorialPatches.Tick();
        Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem) && pending.shop.Step.FinishRequests == 1,
            "enabling immersive services resolves an already pending native BuyItem without a shop close");
        pending.shop.Exit();
        Check(pending.shop.Step.FinishRequests == 1, "later hidden callbacks do not repeat immersive native completion");

        pending = Pending(); pending.shop.Step.DelayHide = true;
        WorldUIConfig.ImmersiveTownServices.Value = true;
        TownServiceTutorialPatches.Tick(); TownServiceTutorialPatches.Tick();
        WorldUIConfig.ImmersiveTownServices.Value = false; TownServiceTutorialPatches.Tick();
        WorldUIConfig.ImmersiveTownServices.Value = true; TownServiceTutorialPatches.Tick();
        Check(pending.shop.Step.FinishRequests == 1 && !pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "option off/on cannot finish the same delayed BuyItem promise more than once");
        pending.shop.Step.CompleteHide(); TownServiceTutorialPatches.Tick();
        Check(pending.manager.HasCompletedStep(EMapFTUEStep.BuyItem), "delayed BuyItem completion remains owned by its native hide callback");

        pending = Pending(); pending.shop.Step.DelayHide = true; pending.shop.Exit();
        WorldUIConfig.ImmersiveTownServices.Value = true; TownServiceTutorialPatches.Tick();
        Check(pending.shop.Step.FinishRequests == 1, "enabling immersive mode cannot repeat a delayed real shop close");
        pending.shop.Step.CompleteHide(); TownServiceTutorialPatches.Tick();
        pending = Pending(); pending.shop.Step.DelayHide = true;
        WorldUIConfig.ImmersiveTownServices.Value = true; TownServiceTutorialPatches.Tick();
        WorldUIConfig.ImmersiveTownServices.Value = false; pending.shop.Exit();
        Check(pending.shop.Step.FinishRequests == 1, "a real shop close cannot repeat delayed immersive completion");
        pending.shop.Step.CompleteHide(); TownServiceTutorialPatches.Tick();

        Setup(false, true, merchantStep: false);
        var lateManager = Singleton<MapFTUEManager>.Instance!;
        var delayedVisit = new UIMapFTUEStep(EMapFTUEStep.VisitMerchant) { DelayHide = true };
        lateManager.StartStep(delayedVisit);
        WorldUIConfig.ImmersiveTownServices.Value = true;
        TownServiceTutorialPatches.Tick();
        Check(delayedVisit.FinishRequests == 0, "first-save VisitMerchant stays native while its serialized BuyItem is unavailable");
        Singleton<UIShopItemWindow>.Instance = new UIShopItemWindow(new UIMapFTUEStep(EMapFTUEStep.BuyItem));
        TownServiceTutorialPatches.Tick(); TownServiceTutorialPatches.Tick();
        WorldUIConfig.ImmersiveTownServices.Value = false; TownServiceTutorialPatches.Tick();
        WorldUIConfig.ImmersiveTownServices.Value = true; TownServiceTutorialPatches.Tick();
        Check(delayedVisit.FinishRequests == 1 && lateManager.CurrentStep == EMapFTUEStep.VisitMerchant,
            "late serialized merchant availability finishes the exact delayed VisitMerchant promise once");
        delayedVisit.CompleteHide();
        Check(lateManager.CurrentStep == EMapFTUEStep.None && !lateManager.HasCompletedStep(EMapFTUEStep.BuyItem),
            "delayed VisitMerchant completion leaves the game's own next-step boundary intact");
        TownServiceTutorialPatches.Tick(); TownServiceTutorialPatches.Tick();
        Check(lateManager.HasCompletedStep(EMapFTUEStep.VisitMerchant) && lateManager.HasCompletedStep(EMapFTUEStep.BuyItem)
            && lateManager.Started.Count == 2,
            "VisitMerchant to none to BuyItem continues once after a delayed native hide");
    }
    private static void Main()
    {
        MerchantCloseCases();
        PurchaseContinuationCases();
        for (byte service = 1; service <= 3; service++)
        {
            Check(!TownServiceAvailability.FromNativeState(service, false, false, false, false, true, true),
                "A locked native station and furniture must not appear");
            Check(TownServiceAvailability.FromNativeState(service, true, true, true, false, false, false),
                "An unlocked Campaign/Guildmaster station must appear outside FTUE");
        }
        Check(!TownServiceAvailability.FromNativeState(1, true, true, true, true, false, false),
            "Merchant stays locked until the second character");
        Check(TownServiceAvailability.FromNativeState(1, true, true, true, true, true, false),
            "Merchant unlocks after the second character");
        Check(!TownServiceAvailability.FromNativeState(2, true, true, true, true, true, false)
              && !TownServiceAvailability.FromNativeState(3, true, true, true, true, true, false),
            "Temple and enchantress retain the game's BuyItem FTUE gate");
        Check(!TownServiceAvailability.FromNativeState(4, true, true, true, false, true, true),
            "Unknown service cannot acquire a stand");
        Check(!TownServiceAvailability.ShouldPublish(false, true, true),
            "A remote visit and enabled setting cannot publish a locked resident");
        Check(TownServiceAvailability.ShouldPublish(true, true, false)
              && TownServiceAvailability.ShouldPublish(true, false, true)
              && !TownServiceAvailability.ShouldPublish(true, false, false),
            "Owner and opted-out observer publish only eligible resident sessions");
        // The production loop is the integration boundary: it must retire a locked station
        // before Acquire and continue without setting the aggregate active flag false. Other
        // unlocked residents then remain visible to both the author and observers.
        string population = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../src/GloomhavenVR/WorldUI/TownServices/TownServicePopulation.cs")));
        int loop = population.IndexOf("for (byte service = 1; service <= 3; service++)", StringComparison.Ordinal);
        int guard = population.IndexOf("if (!unlocked)", loop, StringComparison.Ordinal);
        int dispose = population.IndexOf("locked.Station.Dispose()", guard, StringComparison.Ordinal);
        int acquire = population.IndexOf("Acquire(service)", dispose, StringComparison.Ordinal);
        Check(loop >= 0 && guard > loop && dispose > guard && acquire > dispose,
            "Production retires locked NPC and furniture before any station acquisition");
        Check(population.Substring(guard, acquire - guard).Contains("continue;", StringComparison.Ordinal)
              && !population.Substring(guard, acquire - guard).Contains("published.Active = false", StringComparison.Ordinal),
            "One locked service must not hide the aggregate multiplayer resident channel");

        Setup(true, true);
        MapFTUEManager tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant)
              && tutorial.HasCompletedStep(EMapFTUEStep.BuyItem),
            "Immersive onboarding must advance both native flat-only steps through their promises");
        Check(tutorial.Started.Count == 2 && tutorial.Started[0] == EMapFTUEStep.VisitMerchant
              && tutorial.Started[1] == EMapFTUEStep.BuyItem,
            "The game's native BuyItem step must start once, after VisitMerchant");
        Check(TownServiceAvailability.NativeUnlocked(2) && TownServiceAvailability.NativeUnlocked(3),
            "Later map destinations unlock through native completed steps");
        AdventureState.MapState!.HeadquartersState.TempleUnlocked = false;
        Check(TownServiceAvailability.NativeUnlocked(1)
              && !TownServiceAvailability.NativeUnlocked(2)
              && TownServiceAvailability.NativeUnlocked(3),
            "A locked temple does not disable independently unlocked merchant/enchantress");
        var peerA = new HashSet<byte>(); var peerB = new HashSet<byte>();
        for (int tick = 0; tick < 3; tick++)
            foreach (HashSet<byte> peer in new[] { peerA, peerB })
                for (byte service = 1; service <= 3; service++)
                    if (TownServiceAvailability.ShouldPublish(TownServiceAvailability.NativeUnlocked(service), true, false))
                        peer.Add(service);
        Check(peerA.SetEquals(new byte[] { 1, 3 }) && peerB.SetEquals(peerA),
            "A locked temple has no published stand on either peer while other services remain");
        AdventureState.MapState.HeadquartersState.TempleUnlocked = true;
        for (int tick = 0; tick < 3; tick++)
            foreach (HashSet<byte> peer in new[] { peerA, peerB })
                for (byte service = 1; service <= 3; service++)
                    if (TownServiceAvailability.ShouldPublish(TownServiceAvailability.NativeUnlocked(service), true, false))
                        peer.Add(service);
        Check(peerA.SetEquals(new byte[] { 1, 2, 3 }) && peerB.SetEquals(peerA)
              && peerA.Count == 3 && peerB.Count == 3,
            "Unlock during map browsing converges to exactly one resident per service on each peer");

        Setup(false, true);
        tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(!tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant) && tutorial.Started.Count == 1,
            "Flat mode retains the original merchant onboarding");
        WorldUIConfig.ImmersiveTownServices.Value = true;
        TownServiceTutorialPatches.Tick();
        TownServiceTutorialPatches.Tick();
        Check(tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant)
              && tutorial.HasCompletedStep(EMapFTUEStep.BuyItem),
            "Switching the option on during a pending flat tutorial completes its native continuation");
        TownServiceTutorialPatches.Tick();
        Check(tutorial.Started.Count == 2, "The option transition does not start BuyItem twice");

        Setup(true, false);
        tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(!tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant),
            "Guildmaster tutorial is independent from Campaign map onboarding");

        Setup(true, true);
        GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
        tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(!tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant),
            "The 2D map and tutorial scenes keep their original flat merchant flow");

        Setup(true, true, merchantStep: false);
        tutorial = Singleton<MapFTUEManager>.Instance!;
        tutorial.StartStep(new UIMapFTUEStep(EMapFTUEStep.VisitMerchant));
        Check(!tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant),
            "Missing original BuyItem step must fall back to the intact native lesson");
        Singleton<UIShopItemWindow>.Instance = new UIShopItemWindow(new UIMapFTUEStep(EMapFTUEStep.BuyItem));
        TownServiceTutorialPatches.Tick();
        TownServiceTutorialPatches.Tick();
        Check(tutorial.HasCompletedStep(EMapFTUEStep.VisitMerchant)
              && tutorial.HasCompletedStep(EMapFTUEStep.BuyItem),
            "Late shop singleton resolves the already-started native lesson without a stranded map");
        AdventureState.MapState = null;
        Check(!TownServiceAvailability.NativeUnlocked(1), "No station before a savegame is loaded");
        Console.WriteLine($"Town resident availability/onboarding: {_checks} assertions.");
    }

    private static void PurchaseContinuationCases()
    {
        foreach (int exit in new[] { 0, 1, 2 })
        foreach (bool purchase in new[] { false, true })
        foreach (bool delayed in new[] { false, true })
        {
            Setup(false, true);
            var manager = Singleton<MapFTUEManager>.Instance!;
            manager.SetComplete(EMapFTUEStep.VisitMerchant);
            var shop = Singleton<UIShopItemWindow>.Instance!;
            var next = new UIMapFTUEStep(EMapFTUEStep.InteractWithMap);
            _ = new NativeMapNextStepListener(EMapFTUEStep.BuyItem, next);
            shop.BeginFirstIntroduction();
            Check(manager.CurrentStep == EMapFTUEStep.None && manager.Started.Count == 0,
                "cold first-save introduction keeps BuyItem unstarted until the native entry callback");
            shop.CompleteIntroduction();
            Check(manager.CurrentStep == EMapFTUEStep.BuyItem && manager.Started.Count == 1,
                "actual FinishEnterShop starts exactly the serialized native BuyItem step");
            // Native UIIntroduceProcess.Cancel invokes its callback synchronously;
            // the shop's visual fade does not defer onHidden or IsOpen. Retain an
            // additional delayed FTUE boundary stress for the bridge-only exits.
            shop.Step.DelayHide = delayed && exit != 1;
            shop.DelayShopFade(delayed);
            var confirmation = new PurchaseConfirmationPort(shop);
            confirmation.Show(); confirmation.Cancel();
            Check(!confirmation.Open && confirmation.Gold == 30 && confirmation.Items == 0
                && manager.CurrentStep == EMapFTUEStep.BuyItem && shop.Step.FinishRequests == 0,
                "purchase confirmation cancel preserves the pending native exit lesson");
            confirmation.Show();
            if (purchase) confirmation.Confirm();
            else confirmation.Cancel();
            Check(!confirmation.Open && manager.CurrentStep == EMapFTUEStep.BuyItem
                && shop.Step.FinishRequests == 0 && !manager.HasCompletedStep(EMapFTUEStep.BuyItem),
                "native purchase boundary changes inventory only and never completes the exit lesson");
            int gold = confirmation.Gold, items = confirmation.Items;
            bool cleanupBeforeNext = false;
            manager.OnStartedStep.AddListener(step =>
            {
                if (step == EMapFTUEStep.InteractWithMap)
                    cleanupBeforeNext = !NewPartyDisplayUI.PartyDisplay.SelectionEnabled
                        && !shop.NativeListenersAttached && shop.Cleanups > 0;
            });
            if (exit == 0) shop.Exit();
            else if (exit == 1)
            {
                var toggle = new NativeWorldMapToggleListener();
                toggle.Toggle(false);
                Check(shop.Step.FinishRequests == 0 && toggle.Attached,
                    "native WorldMap listener ignores the opposite toggle value");
                shop.ExitCallback = () => toggle.Toggle(true);
                shop.Exit();
                Check(!toggle.Attached, "native WorldMap listener removes itself after its exact completion event");
            }
            else
            {
                shop.TemporaryHide();
                Check(!manager.HasCompletedStep(EMapFTUEStep.BuyItem),
                    "purchase sibling hiding the borrowed shop cannot consume native continuation");
                using (GloomhavenVR.WorldUI.MapRoom.TownWindowCloseScope.Enter())
                {
                    var close = TownServiceTutorialPatches.CaptureConvertedMerchantClose(shop.ConvertedWindow);
                    TownServiceTutorialPatches.CompleteConvertedMerchantClose(shop.ConvertedWindow, close);
                    TownServiceTutorialPatches.CompleteConvertedMerchantClose(shop.ConvertedWindow, close);
                }
            }
            Check(shop.Step.FinishRequests == 1,
                "each real merchant exit requests one exact native hide after confirm or cancel");
            Check(!shop.IsOpen && shop.FadePending == delayed,
                "shop cleanup and native IsOpen transition precede its optional visual fade completion");
            if (shop.Step.DelayHide)
            {
                Check(manager.CurrentStep == EMapFTUEStep.BuyItem && !manager.HasCompletedStep(EMapFTUEStep.BuyItem)
                    && manager.Started.Count == 1,
                    "delayed native FTUE hide retains its own completion and map continuation");
                shop.Step.CompleteHide();
            }
            Check(manager.HasCompletedStep(EMapFTUEStep.BuyItem) && manager.CurrentStep == EMapFTUEStep.InteractWithMap
                && cleanupBeforeNext && manager.Started.Count == 2 && shop.Step.FinishRequests == 1,
                "actual native completion listener advances InteractWithMap after cleaned merchant exit");
            shop.CompleteShopFade();
            Check(!shop.FadePending && manager.CurrentStep == EMapFTUEStep.InteractWithMap,
                "visual shop fade completion cannot gate or repeat native map continuation");
            shop.Reopen(); shop.Exit();
            Check(manager.CurrentStep == EMapFTUEStep.InteractWithMap && manager.Started.Count == 2
                && confirmation.Gold == gold && confirmation.Items == items && next.FinishRequests == 0,
                "a repeated shop exit preserves next native map lesson and purchased state");
        }

        Setup(false, true);
        var old = Singleton<MapFTUEManager>.Instance!;
        old.SetComplete(EMapFTUEStep.VisitMerchant);
        var previousShop = Singleton<UIShopItemWindow>.Instance!;
        previousShop.Enter(); previousShop.Step.DelayHide = true; previousShop.Exit();
        old.StopPlaying();
        Setup(false, true);
        var current = Singleton<MapFTUEManager>.Instance!;
        current.SetComplete(EMapFTUEStep.VisitMerchant);
        var currentShop = Singleton<UIShopItemWindow>.Instance!;
        currentShop.Enter();
        previousShop.Step.CompleteHide();
        Check(!old.HasCompletedStep(EMapFTUEStep.BuyItem) && !current.HasCompletedStep(EMapFTUEStep.BuyItem),
            "delayed old-save promise after native shutdown cannot complete either manager");
        MapFTUEManager.IsPlaying = true;
        currentShop.Exit();
        Check(current.HasCompletedStep(EMapFTUEStep.BuyItem) && currentShop.Step.FinishRequests == 1,
            "old closing marker cannot strand the second save's distinct native promise");

        Setup(false, true);
        current = Singleton<MapFTUEManager>.Instance!;
        current.SetComplete(EMapFTUEStep.VisitMerchant);
        currentShop = Singleton<UIShopItemWindow>.Instance!;
        currentShop.BeginFirstIntroduction(); currentShop.Exit();
        Check(current.HasCompletedStep(EMapFTUEStep.BuyItem) && current.Started.Count == 1,
            "native intro cancellation during real Exit starts and completes its exact shop lesson");
        MapFTUEManager.IsPlaying = false;
        currentShop.Reopen(); currentShop.Enter();
        Check(current.Started.Count == 1 && current.CurrentStep == EMapFTUEStep.None,
            "native inactive multiplayer/completed-FTUE entry cannot restart the merchant lesson");

        Setup(false, true);
        current = Singleton<MapFTUEManager>.Instance!;
        current.SetComplete(EMapFTUEStep.VisitMerchant);
        currentShop = Singleton<UIShopItemWindow>.Instance!;
        MapFTUEManager.IsPlaying = false;
        currentShop.Enter();
        Check(current.Started.Count == 0 && !currentShop.Step.Pending,
            "native inactive manager refuses an uncompleted merchant step without creating its promise");

        Setup(false, true);
        current = Singleton<MapFTUEManager>.Instance!;
        current.SetComplete(EMapFTUEStep.VisitMerchant); current.SetComplete(EMapFTUEStep.BuyItem);
        currentShop = Singleton<UIShopItemWindow>.Instance!;
        currentShop.Enter(); currentShop.Exit();
        Check(current.Started.Count == 0 && !currentShop.Step.Pending && currentShop.Step.FinishRequests == 0,
            "a completed loaded merchant lesson keeps native entry and close inert");
    }
}
