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
        Console.WriteLine($"Selection latejoin: {_assertions} production-bound assertions passed.");
    }
}
