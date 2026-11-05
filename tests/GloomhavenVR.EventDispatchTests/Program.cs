using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using ScenarioRuleLibrary;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool okay, string why)
    {
        _assertions++;
        if (!okay) throw new InvalidOperationException(why);
    }

    private static void Notification<T>(T payload, Action<Action<T>> add,
        Action<Action<T>> remove, Action<T> raise, string name) where T : struct
    {
        var order = new List<int>();
        Action<T> first = _ => { order.Add(1); throw new InvalidOperationException("observer failure"); };
        Action<T> second = _ => order.Add(2);
        Action<T> third = _ => order.Add(3);
        add(first); add(second); add(third);
        try
        {
            raise(payload);
            Check(order.Count == 3 && order[0] == 1 && order[1] == 2 && order[2] == 3,
                name + ": later event observers receive original edge");
            remove(first); order.Clear(); raise(payload);
            Check(order.Count == 2 && order[0] == 2 && order[1] == 3,
                name + ": changed subscription snapshot excludes retired observer");
        }
        finally { remove(first); remove(second); remove(third); }
    }

    private static void Main()
    {
        Notification(new ChoreoMessageEvent(new CMessageData()),
            x => VREvents.ChoreographerMessage += x, x => VREvents.ChoreographerMessage -= x,
            x => VREvents.Raise(x), "choreographer message");
        Notification(new ChoreoStateEvent(Choreographer.ChoreographerStateType.Play),
            x => VREvents.ChoreographerStateChanged += x, x => VREvents.ChoreographerStateChanged -= x,
            x => VREvents.Raise(x), "choreographer state");
        Notification(new NavStateEvent(null),
            x => VREvents.NavigationStateChanged += x, x => VREvents.NavigationStateChanged -= x,
            x => VREvents.Raise(x), "navigation");
        Notification(new CardSelectionEvent(new AbilityCardUI(), true),
            x => VREvents.CardSelectionChanged += x, x => VREvents.CardSelectionChanged -= x,
            x => VREvents.Raise(x), "card selection");
        Notification(new CardHoverEvent(new AbilityCardUI(), true),
            x => VREvents.CardHoverChanged += x, x => VREvents.CardHoverChanged -= x,
            x => VREvents.Raise(x), "card hover");
        Notification(new FullCardHoverEvent(true),
            x => VREvents.FullCardHoverChanged += x, x => VREvents.FullCardHoverChanged -= x,
            x => VREvents.Raise(x), "full card hover");
        Notification(new ShortRestEvent(new ShortRest(), true),
            x => VREvents.ShortRestSelected += x, x => VREvents.ShortRestSelected -= x,
            x => VREvents.Raise(x), "short rest");
        Notification(new UiLockEvent(false),
            x => VREvents.UiLockChanged += x, x => VREvents.UiLockChanged -= x,
            x => VREvents.Raise(x), "UI unlock");
        Notification(new SceneLoadedEvent(default, default),
            x => VREvents.SceneLoaded += x, x => VREvents.SceneLoaded -= x,
            x => VREvents.Raise(x), "scene loaded");
        Notification(new HandShownEvent(null, default),
            x => VREvents.HandShown += x, x => VREvents.HandShown -= x,
            x => VREvents.Raise(x), "hand shown");
        Notification(new MiniaturePokedEvent(new CActor()),
            x => VREvents.MiniaturePoked += x, x => VREvents.MiniaturePoked -= x,
            x => VREvents.Raise(x), "miniature");
        Notification(new WindowVisibilityEvent(new UnityEngine.UI.UIWindow(), false),
            x => VREvents.WindowVisibility += x, x => VREvents.WindowVisibility -= x,
            x => VREvents.Raise(x), "window closed");
        var recovered = new List<string>();
        Notification(new SessionResumedEvent(1f, "test", recovered),
            x => VREvents.SessionResumed += x, x => VREvents.SessionResumed -= x,
            x => VREvents.Raise(x), "session resumed");

        // Exercise the complete actual mode coordinator. A failed observer registered
        // before Attach used to prevent the mode's only unlock notification entirely.
        Action<UiLockEvent> badLock = _ => throw new InvalidOperationException("earlier unlock observer");
        VREvents.UiLockChanged += badLock;
        VRModeStateMachine.Attach();
        Choreographer.s_Choreographer = new Choreographer();
        VRModeStateMachine.Tick();
        Action<VRModeChange> badMode = _ => throw new InvalidOperationException("earlier mode observer");
        int modeCalls = 0;
        Action<VRModeChange> goodMode = _ => modeCalls++;
        VRModeStateMachine.ModeChanged += badMode;
        VRModeStateMachine.ModeChanged += goodMode;
        try
        {
            VREvents.Raise(new UiLockEvent(true));
            Check(VRModeStateMachine.CurrentMode == VRMode.ModalUI, "actual modal coordinator receives lock");
            Check(modeCalls == 1, "actual mode edge reaches later input observer");
            VREvents.Raise(new UiLockEvent(false));
            Check(VRModeStateMachine.CurrentMode == VRMode.TableIdle, "actual modal coordinator receives unlock");
            Check(modeCalls == 2, "actual unlock restores later input observer once");
        }
        finally
        {
            VREvents.UiLockChanged -= badLock;
            VRModeStateMachine.ModeChanged -= badMode;
            VRModeStateMachine.ModeChanged -= goodMode;
            VRModeStateMachine.Detach();
            Choreographer.s_Choreographer = null;
        }

        var calls = new List<int>();
        Action<UiLockEvent> current = null;
        Action<UiLockEvent> late = _ => calls.Add(3);
        Action<UiLockEvent> a = e =>
        {
            calls.Add(e.Locked ? 1 : 10);
            if (e.Locked) { current += late; EventDispatch<UiLockEvent>.Invoke(current, new UiLockEvent(false), "reentrant"); }
        };
        Action<UiLockEvent> b = e => calls.Add(e.Locked ? 2 : 20);
        current = a + b;
        EventDispatch<UiLockEvent>.Invoke(current, new UiLockEvent(true), "reentrant");
        Check(string.Join(",", calls) == "1,10,20,3,2", "snapshot reentrancy preserves original payload and order");

        Action<UiLockEvent> throwing = _ => throw new InvalidOperationException("repeated observer");
        int deliveries = 0;
        Action<UiLockEvent> okay = _ => deliveries++;
        Action<UiLockEvent> storm = throwing + okay;
        int before = VRLog.Errors.Count;
        for (int i = 0; i < 100; i++) EventDispatch<UiLockEvent>.Invoke(storm, default, "storm");
        Check(deliveries == 100, "repeated errors never starve later observer");
        Check(VRLog.Errors.Count == before + 1, "normal-level repeated errors are throttled");
        UnityEngine.Time.unscaledTime = 11f;
        EventDispatch<UiLockEvent>.Invoke(storm, default, "storm");
        Check(VRLog.Errors.Count == before + 2, "bounded diagnostic retains later ongoing-failure context");
        VRLog.FailLog = true;
        UnityEngine.Time.unscaledTime = 22f;
        EventDispatch<UiLockEvent>.Invoke(storm, default, "storm");
        VRLog.FailLog = false;
        Check(deliveries == 102, "diagnostic failure also preserves later observer");

        Action<UiLockEvent> fast = _ => { };
        Action<UiLockEvent> fastPair = fast + fast;
        for (int i = 0; i < 100; i++) EventDispatch<UiLockEvent>.Invoke(fastPair, default, "allocation");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) EventDispatch<UiLockEvent>.Invoke(fastPair, default, "allocation");
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "stable subscriptions allocate no per-notification list");
        EventDispatch<UiLockEvent>.Invoke(null, default, "none");
        Check(true, "empty subscriber list remains harmless");
        WaitGuidance();
        Console.WriteLine($"Event dispatch: {_assertions} assertions passed.");
    }

    private static void WaitGuidance()
    {
        ProgressWaitNotice.Reset();
        VRSession.IsRunning = true;
        FFSNetwork.IsOnline = true;
        UnityEngine.Time.unscaledTime = 100f;
        var owner = new Choreographer();
        Choreographer.s_Choreographer = owner;
        var wait = new Choreographer.CWaitState {
            m_State = Choreographer.ChoreographerStateType.WaitingForEndTurnSync,
            m_StateWaitActor = new CActor { ID = 11 }
        };
        owner.m_WaitState = wait;
        PhaseManager.PhaseType = CPhase.PhaseType.EndTurn;
        Check(ProgressWaitNotice.ImmediateHintKey == "progress_finishing_turn", "immediate help accessor describes an actual wait without its diagnostic delay");
        Check(VRLog.Alerts.Count==0,"instant help accessor never emits the diagnostic incident");
        Check(ProgressWaitNotice.CurrentHint == null, "ordinary short turn transition stays quiet");
        UnityEngine.Time.unscaledTime = 119f;
        Check(ProgressWaitNotice.CurrentHint == null, "unfinished automatic wait does not become a player accusation");
        UnityEngine.Time.unscaledTime = 120f;
        Check(ProgressWaitNotice.CurrentHint == "progress_finishing_turn", "prolonged turn closure gets specific guidance");
        int reports = VRLog.Alerts.Count;
        UnityEngine.Time.unscaledTime = 180f;
        Check(ProgressWaitNotice.CurrentHint == "progress_finishing_turn" && VRLog.Alerts.Count == reports,
            "prolonged same native barrier emits one bounded normal-level incident");
        Check(ReferenceEquals(owner.m_WaitState, wait) && !owner.m_EndOfTurnSyncComplete
            && PhaseManager.PhaseType == CPhase.PhaseType.EndTurn, "guidance never changes native wait, readiness or phase");
        owner.m_EndOfTurnSyncComplete = true;
        UnityEngine.Time.unscaledTime = 181f;
        Check(ProgressWaitNotice.CurrentHint == "progress_wait_players", "local-ready online barrier names pending peers without guessing a person");
        Check(ProgressWaitNotice.ImmediateHintKey=="progress_wait_players","instant text respects actual local-ready online flags");
        owner.m_WaitState = new Choreographer.CWaitState { m_State = wait.m_State };
        Check(ProgressWaitNotice.CurrentHint == null, "new actual barrier entry resets previous wait guidance immediately");
        Check(VRLog.Infos.Exists(x => x.StartsWith("PROGRESSION WAIT CLEARED", StringComparison.Ordinal)),
            "cleared incident retains useful completion context");
        owner.m_WaitState.m_State = Choreographer.ChoreographerStateType.WaitingForTileSelected;
        UnityEngine.Time.unscaledTime = 500f;
        Check(ProgressWaitNotice.CurrentHint == null, "mandatory input is never misclassified as automatic synchronization");
        Check(ProgressWaitNotice.ImmediateHintKey==null,"instant help does not misclassify native input as automatic wait");
        owner.m_WaitState = new Choreographer.CWaitState {
            m_State = Choreographer.ChoreographerStateType.WaitingForEndAbilityAnimSync
        };
        FFSNetwork.IsOnline = false;
        Check(ProgressWaitNotice.CurrentHint == null, "new offline action wait starts quietly");
        Check(ProgressWaitNotice.ImmediateHintKey=="progress_finishing_action","instant offline ability text is available before diagnostics");
        UnityEngine.Time.unscaledTime = 520f;
        Check(ProgressWaitNotice.CurrentHint == "progress_finishing_action", "single-player action wait gets action guidance");
        owner.m_EndAbilitySyncComplete = true;
        UnityEngine.Time.unscaledTime = 521f;
        Check(ProgressWaitNotice.CurrentHint == "progress_finishing_action", "offline wait never asks for nonexistent players");
        owner.m_WaitState = new Choreographer.CWaitState {
            m_State = Choreographer.ChoreographerStateType.WaitingForEndRoundSync
        };
        Check(ProgressWaitNotice.CurrentHint == null, "round wait has its own entry lifetime");
        Check(ProgressWaitNotice.ImmediateHintKey=="progress_finishing_round","instant round text uses its own native barrier");
        UnityEngine.Time.unscaledTime = 541f;
        Check(ProgressWaitNotice.CurrentHint == "progress_finishing_round", "round closure gets round-specific guidance");
        UnityEngine.Time.unscaledTime = 2f;
        Check(ProgressWaitNotice.CurrentHint == null, "clock rewind cannot inherit another incident age");
        VRSession.IsRunning = false;
        UnityEngine.Time.unscaledTime = 1000f;
        Check(ProgressWaitNotice.CurrentHint == null, "disabled VR leaves native presentation untouched");
        Check(ProgressWaitNotice.ImmediateHintKey==null,"instant help respects disabled VR");
        VRSession.IsRunning = true;
        Choreographer.s_Choreographer = null;
        Check(ProgressWaitNotice.CurrentHint == null, "scenario exit retires previous guidance");
        Check(ProgressWaitNotice.ImmediateHintKey==null,"instant text also retires on scenario exit");
        ProgressWaitNotice.Reset();
    }
}
