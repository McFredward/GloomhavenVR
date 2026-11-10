using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using ScenarioRuleLibrary;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static CPlayerActor _foreign = null!, _mine = null!;
    private static CardsHandUI _foreignHand = null!, _myHand = null!;
    private static CardsDriver Reset()
    {
        _foreign = new CPlayerActor("foreign", false) { IsUnderMyControl = true };
        _mine = new CPlayerActor("newly assigned", true);
        _foreignHand = new CardsHandUI(_foreign);
        _myHand = new CardsHandUI(_mine);
        CardsHandManager.Instance = new CardsHandManager { CurrentHand = _foreignHand, CardHandsUI = new() { _foreignHand, _myHand } };
        Choreographer.s_Choreographer = new Choreographer { LastMessage = new CMessageData { m_Type = CMessageData.MessageType.PlayerToSelectAbilityCardsOrLongRest } };
        PhaseManager.PhaseType = CPhase.PhaseType.SelectAbilityCardsOrLongRest;
        CardsGameApi.Selected = _foreign;
        CardsGameApi.Decision = null;
        ModalFallback.BlockingWindowModalActive = false;
#if NATIVE_STORY_PROOF
        Singleton<StoryController>.Instance = new StoryController();
        StoryController.DisplayDelayInEffect = false;
        FFSNet.ActionProcessor.Locks = 0;
        FFSNet.ActionProcessor.CurrentPhase = FFSNet.ActionPhaseType.StartOfRound;
        SceneController.Instance = new SceneController();
        FFSNet.Synchronizer.ReadyNotifications = 0;
#endif
        CardActionQueue.Actions.Clear();
        VRLog.Lines.Clear();
        Time.frameCount = 100;
        Time.unscaledTime = 2;
        return new CardsDriver();
    }
    private static void Check(bool ok, string message) { _assertions++; if (!ok) throw new InvalidOperationException(message); }
    private static void Frame(CardsDriver driver) { Time.frameCount++; Time.unscaledTime += .02f; driver.Tick(); }
    private static void QueueRepair(CardsDriver driver) { driver.Tick(); Frame(driver); Frame(driver); Check(driver.Queued && CardActionQueue.Actions.Count == 1, "Persistent latejoin foreign presenter must queue one native repair"); }
    private static void Main()
    {
        var driver = Reset();
        _mine.ListedMine = false;
        driver.Tick();
        Check(CardsGameApi.SelectionHandDrift() == null && driver.Wanted == 0, "An unassigned spectator must have no placement or repair");
        _mine.ListedMine = true; // Native assignment completes while foreign stale flag stays true.
        Check(ReferenceEquals(CardsGameApi.SelectionHandDrift(), _mine), "Latejoin must resolve the newly owned hand despite a foreign native selection");
        QueueRepair(driver);
        Check(driver.Wanted == 0 && CardsHandManager.Instance.NativeSwitches == 0, "The read-only floor must not offer placement before the native switch");
        Frame(driver);
        Check(CardActionQueue.Actions.Count == 1, "A queued hand repair must not duplicate the callback");
        CardActionQueue.Pump();
        Frame(driver);
        Check(ReferenceEquals(CardsHandManager.Instance.CurrentHand, _myHand) && driver.Dirty, "Latejoin must converge the original hand presenter and request its rebuild");
        Check(driver.Wanted == 3, "The real wanted-slot method must immediately restore both placement overlays");
        Check(CardsHandManager.Instance.NativeSwitches == 1 && Choreographer.s_Choreographer.OnSwitchHandCalls == 1, "Recovery must use the original native switch once");
        Check(VRLog.Lines.Count == 1 && VRLog.Lines[0].Contains("SELECTION HAND RECOVERED"), "Assignment recovery must produce one informative lifecycle report");
        for (int i = 0; i < 100; i++) Frame(driver);
        Check(CardActionQueue.Actions.Count == 0 && CardsHandManager.Instance.NativeSwitches == 1, "A stable owned presenter must not fight the remaining native selection");

        driver = Reset(); CardsGameApi.Selected = null;
        Check(ReferenceEquals(CardsGameApi.SelectionHandDrift(), _mine), "A cleared native selection after assignment must also recover");
        driver = Reset(); CardsGameApi.Selected = _mine;
        QueueRepair(driver); CardActionQueue.Pump(); Frame(driver);
        Check(driver.Wanted == 3 && VRLog.Lines.Single().StartsWith("Hand-switch watchdog:"), "Existing selected-character drift must keep its original recovery path");
        driver = Reset(); CardsHandManager.Instance.CurrentHand = _myHand;
        Check(CardsGameApi.SelectionHandDrift() == null, "An owned current presenter must survive a transient foreign selection");
        driver = Reset(); CardsHandManager.Instance.CurrentHand = null!;
        Check(ReferenceEquals(CardsGameApi.SelectionHandDrift(), _mine), "A missing native presenter must resolve a live owned hand");

        foreach (CPhase.PhaseType phase in new[] { CPhase.PhaseType.None, CPhase.PhaseType.Action, CPhase.PhaseType.ActionSelection, CPhase.PhaseType.TakeDamage })
        { Reset(); PhaseManager.PhaseType = phase; Check(CardsGameApi.SelectionHandDrift() == null, "Native non-selection phases must prevent reassignment recovery"); }
        foreach (CMessageData.MessageType message in new[] { CMessageData.MessageType.StartTurn, CMessageData.MessageType.ActionSelectionPhaseStart, CMessageData.MessageType.CheckForInitiativeAdjustments })
        { Reset(); Choreographer.s_Choreographer.LastMessage!.m_Type = message; Check(CardsGameApi.SelectionHandDrift() == null, "Native deliberate no-switch messages must remain authoritative"); }
        Reset(); _mine.ControlledByAbility = true;
        Check(CardsGameApi.SelectionHandDrift() == null, "An actual control ability must defer the native switch");
        Reset(); _mine.IsDead = true;
        Check(CardsGameApi.SelectionHandDrift() == null, "A dead owned hand must not be admitted");
        Reset(); _myHand.currentMode = CardHandMode.LoseCard;
        Check(CardsGameApi.SelectionHandDrift() == null, "A non-selection-mode hand must not be substituted for a missing selection presenter");
        Reset(); CardsGameApi.Decision = _myHand;
        Check(CardsGameApi.SelectionHandDrift() == null, "A genuine deciding flow must retain its hand");
        Reset(); Choreographer.s_Choreographer = null!;
        Check(CardsGameApi.SelectionHandDrift() == null, "A cold choreographer must not receive a native switch");
        Reset(); CardsHandManager.Instance = null!;
        Check(CardsGameApi.SelectionHandDrift() == null, "Missing hand manager must not recover");
        Reset(); CardsHandManager.Instance.CardHandsUI.Remove(_myHand);
        Check(CardsGameApi.SelectionHandDrift() == null, "A missing native hand must not recover");

        driver = Reset(); ModalFallback.BlockingWindowModalActive = true;
        for (int i = 0; i < 10; i++) Frame(driver);
        Check(CardActionQueue.Actions.Count == 0, "A genuine blocking modal must defer the repair");
        ModalFallback.BlockingWindowModalActive = false;
        QueueRepair(driver); CardActionQueue.Pump(); Frame(driver);
        Check(driver.Wanted == 3, "Closing a blocker must admit the still-pending owned hand without a new native edge");
        foreach (var revoke in new Action[] { () => _mine.ListedMine = false, () => PhaseManager.PhaseType = CPhase.PhaseType.Action, () => CardsGameApi.Decision = _myHand, () => _mine.ControlledByAbility = true, () => ModalFallback.BlockingWindowModalActive = true })
        {
            driver = Reset(); QueueRepair(driver); revoke(); CardActionQueue.Pump();
            Check(CardsHandManager.Instance.NativeSwitches == 0 && !driver.Queued, "Queued recovery must revalidate live native permission and ownership");
        }
        driver = Reset(); QueueRepair(driver);
        CardsHandManager.Instance.CurrentHand = _myHand; // A real native/tab callback won the race.
        CardActionQueue.Pump();
        Check(CardsHandManager.Instance.NativeSwitches == 0 && !driver.Queued, "A queued repair must not disturb an already recovered native presenter");
        driver = Reset(); QueueRepair(driver);
        _mine.ListedMine = false;
        var replacement = new CPlayerActor("replacement", true);
        var replacementHand = new CardsHandUI(replacement);
        CardsHandManager.Instance.CardHandsUI.Add(replacementHand);
        CardsGameApi.Selected = replacement;
        CardActionQueue.Pump(); Frame(driver);
        Check(ReferenceEquals(CardsHandManager.Instance.CurrentHand, replacementHand), "Queued repair must follow the fresh actor rather than the stale assignment");
        Check(driver.Wanted == 3 && VRLog.Lines.Single().Contains("'replacement'"), "The recovered overlay and lifecycle report must both identify the fresh native hand");

#if NATIVE_STORY_PROOF
        // Paired763: the hidden original intro remains visible to native state,
        // so both the card presenter watchdog and overlays correctly refuse work.
        // Shared-story authority/occurrence matching is tested upstream; consume
        // its successful original callback here without bypassing any card gate.
        driver = Reset();
        var story = Singleton<StoryController>.Instance;
        story.OpenNative();
        ModalFallback.BlockingWindowModalActive = story.IsVisible;
        for (int i = 0; i < 200; ++i) Frame(driver);
        Check(driver.Wanted == 0 && !driver.Queued && CardsHandManager.Instance.NativeSwitches == 0,
            "A pending original intro must preserve the read-only floor and defer recovery");
        Check(FFSNet.ActionProcessor.Locks == 1 && Choreographer.s_Choreographer.UpdateBlockers == 1,
            "The actual native intro must hold both gameplay and choreography locks");
        story.NativeFinish();
        Check(FFSNet.ActionProcessor.Locks == 0 && Choreographer.s_Choreographer.UpdateBlockers == 0,
            "Successful native story completion must release both original locks");
        Check(!story.IsVisible && story.NativeCompletions == 1 && SceneController.Instance.GameLoadedAndClientReady,
            "Original story completion must close its native window and retain native client readiness");
        Check(FFSNet.Synchronizer.ReadyNotifications == 1,
            "The native finish path must retain its original reliable ready notification");
        ModalFallback.BlockingWindowModalActive = story.IsVisible;
        QueueRepair(driver); CardActionQueue.Pump(); Frame(driver);
        Check(ReferenceEquals(CardsHandManager.Instance.CurrentHand, _myHand) && driver.Wanted == 3,
            "A completed latejoin intro must restore the owned hand and both real placement overlays without reloading");
        Check(CardsHandManager.Instance.NativeSwitches == 1,
            "Native story completion must not manufacture a second hand-switch callback");

        driver = Reset(); CardsHandManager.Instance.CurrentHand = _myHand;
        story = Singleton<StoryController>.Instance;
        story.OpenNative(2); // A genuine successor must still block gameplay.
        Frame(driver);
        Check(driver.Wanted == 0, "A visible native story must suppress placement even for an already-owned hand");
        story.NativeFinish(); ModalFallback.BlockingWindowModalActive = story.IsVisible;
        Frame(driver);
        Check(story.IsVisible && driver.Wanted == 0 && FFSNet.ActionProcessor.Locks == 1
            && Choreographer.s_Choreographer.UpdateBlockers == 1,
            "A genuine queued successor must keep both native locks and placement refusal");
        story.NativeFinish(); ModalFallback.BlockingWindowModalActive = story.IsVisible;
        Frame(driver);
        Check(!story.IsVisible && driver.Wanted == 3 && story.NativeCompletions == 2,
            "Only finishing the genuine successor may restore placement on the owned hand");

        driver = Reset(); _mine.ListedMine = false;
        story = Singleton<StoryController>.Instance; story.OpenNative();
        ModalFallback.BlockingWindowModalActive = true; Frame(driver);
        story.NativeFinish(); ModalFallback.BlockingWindowModalActive = story.IsVisible;
        for (int i = 0; i < 10; ++i) Frame(driver);
        Check(SceneController.Instance.GameLoadedAndClientReady && FFSNet.ActionProcessor.Locks == 0,
            "A visitor without a character must also complete native story readiness");
        Check(driver.Wanted == 0 && !driver.Queued && CardsHandManager.Instance.NativeSwitches == 0,
            "An unassigned visitor must remain unable to place cards after native story completion");

        driver = Reset(); story = Singleton<StoryController>.Instance;
        story.OpenNative(); story.FailCompletion = true;
        ModalFallback.BlockingWindowModalActive = true;
        bool failed = false;
        try { story.NativeFinish(); } catch (InvalidOperationException) { failed = true; }
        Frame(driver);
        Check(failed && story.IsVisible && FFSNet.ActionProcessor.Locks == 1
            && Choreographer.s_Choreographer.UpdateBlockers == 1 && driver.Wanted == 0,
            "A throwing native finish must preserve its native locks and placement refusal");
        Check(!SceneController.Instance.GameLoadedAndClientReady && FFSNet.Synchronizer.ReadyNotifications == 0,
            "A failed native finish must not manufacture a successful client-ready notification");
#endif
        Console.WriteLine($"Selection latejoin: {_assertions} production-bound assertions passed.");
    }
}
