using System.Collections.Generic;
using FFSNet;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// SKIP THE ENEMY-INFORMATION PHASE WHEN IT HAS NOTHING TO SHOW.
///
/// <para>USER REPORT (3-player hardware session, verbatim): "Wenn es keine Gegnerinfos gibt weil
/// aktuell sichtbar gar keine Gegner existieren, soll die Phase übersprungen werden in dem der
/// host nochmal mit 'Fortfahren' bestätigen muss."</para>
///
/// <para>THE PHASE, named from the game's own model: <c>CPhase.PhaseType
/// .MonsterClassesSelectAbilityCards</c> on the rules side, <c>ActionPhaseType.EnemyCardReveal</c>
/// on the wire. It is raised by the <c>MonsterClassesToSelectAbilityCards</c> message
/// (Choreographer.cs:3684) and it is not a popup of its own: the monster ability cards are revealed
/// INLINE on the initiative track by
/// <c>InitiativeTrack.ShowMonsterClassesForSelectingRoundAbilityCards</c> (InitiativeTrack.cs:741),
/// and the phase is left when the HOST presses the shared ReadyButton, which is standing in the
/// <c>EREADYBUTTONCONTINUE</c> state with the <c>GUI_CONTINUE</c> label ("Fortfahren").</para>
///
/// <para>WHY AN EMPTY ONE CAN HAPPEN AT ALL — the game HAS a skip and it is guarded on the wrong
/// list. Choreographer.cs:3705:</para>
/// <code>
///   List&lt;GameObject&gt; clientMonsterObjects = ClientMonsterObjects;
///   if (clientMonsterObjects.Count == 0 &amp;&amp; !Singleton&lt;UIActiveBonusBar&gt;.Instance.IsShowing)
///   { Pass(); break; }                       // ← the game's own empty-phase skip
/// </code>
/// <para><c>Choreographer.ClientMonsterObjects</c> (Choreographer.cs:470) is
/// <c>m_ClientEnemies ∪ m_ClientAllyMonsters ∪ m_ClientEnemy2Monsters ∪ m_ClientNeutralMonsters ∪
/// <b>m_ClientObjects</b></c> — the last term is OBJECTS (chests, obstacles, activatable props),
/// which are not enemies and have no monster class. So a room holding nothing but objects makes
/// that count non-zero, the skip does not fire, and the reveal is entered. Inside it, the list that
/// actually decides what is DRAWN is filtered much harder (InitiativeTrack.cs:753):</para>
/// <code>
///   if (list.Find(x =&gt; x == enemyActor.MonsterClass) == null
///       &amp;&amp; enemyActor.MonsterClass.RoundAbilityCard != null) { list.Add(...); }
///   NormalizeEnemiesUiPool(list.Count);   // list empty ⇒ every enemy row deactivated
///   ...
///   EnemyAbilityCardsAnimation();
/// </code>
/// <para>With that list empty, <c>EnemyAbilityCardsAnimation</c> (InitiativeTrack.cs:544) counts
/// zero active enemy rows, animates nothing, and its final statement still arms the button:
/// <c>readyButton.AlternativeAction(FinishAbilityCardsAnimation, EREADYBUTTONCONTINUE,
/// GetTranslation("GUI_CONTINUE"))</c>. The host is then looking at an EMPTY enemy-information
/// screen with a live "Fortfahren" on it — the report, exactly.</para>
///
/// <para>THE PREDICATE IS THE GAME'S OWN, not an invention. "Currently visible enemies" is read as
/// <b>the number of active rows in <c>InitiativeTrack.enemiesUI</c></b> — the same count
/// <c>EnemyAbilityCardsAnimation</c> computes as its <c>num2</c> and the same set
/// <c>FinishAbilityCardsAnimation</c> iterates. It is downstream of every visibility rule the game
/// applies (spawned client-side at all, a real monster class rather than an object, and that class
/// holding a drawn <c>CMonsterClass.RoundAbilityCard</c>), so this class never re-derives
/// "revealed" and cannot disagree with the screen. Zero active rows ⇔ the screen is blank.</para>
///
/// <para>THE SEAM IS THE CONTROL THE HOST WOULD PRESS. Nothing here touches
/// <c>ScenarioRuleLibrary</c>, Bolt or the phase machine: the skip is
/// <c>ReadyButton.OnClickInternal(networkActionIfOnline: true)</c>, the single point every press
/// path converges on (<c>OnClick</c> → <c>OnClickInternal</c>; the gamepad long-press →
/// <c>PlayGUIAnimation</c>/<c>HandleFinishAnimation</c> → <c>OnClickInternal</c>). It runs
/// <c>Synchronizer.SendGameAction(GameActionType.ConfirmAction, ActionProcessor.CurrentPhase)</c>
/// and the queued <c>FinishAbilityCardsAnimation</c> exactly as a human press does; the only thing
/// it omits is the press ANIMATION, which is correct for a press that did not happen.</para>
///
/// <para>MULTIPLAYER — THE HOST DECIDES AND THE OTHERS FOLLOW, which is also what the game already
/// enforces. A client's button is forced non-interactable for the whole of this phase
/// (<c>ReadyButton.CheckButtonInteractability</c>, ReadyButton.cs:496-505) and its click is a no-op
/// (<c>OnClickInternal</c>, ReadyButton.cs:255-258: <c>if (FFSNetwork.IsClient &amp;&amp;
/// PhaseManager.CurrentPhase.Type == MonsterClassesSelectAbilityCards) return;</c>); clients are
/// put in <c>ProcessOneAndHalt</c> and shown a "wait for host" tip while the host sits
/// <c>Halted</c> (Choreographer.cs:3723-3737). This class adds its OWN explicit
/// <see cref="IsDecidingSeat"/> gate on top rather than leaning on those, so a peer never even
/// evaluates the skip: a client that disagrees about visibility (different room reveal, a monster
/// spawned on the host a frame earlier) contributes nothing to the decision and simply leaves the
/// phase when the host's confirm replicates. Nothing new goes on the wire — the confirm is the
/// game's own ConfirmAction, the same bytes the host's own press sends.</para>
///
/// <para>WHAT IT DELIBERATELY DOES NOT DO: it never fires while
/// <c>Singleton&lt;UIActiveBonusBar&gt;.Instance.IsShowing</c>. That is the second half of the
/// game's own skip condition at Choreographer.cs:3705 — an active-bonus bar standing over the
/// reveal means the player has something to decide there, and an empty enemy row is then not an
/// empty screen. Refusing on the same term the game refuses on is what keeps this a skip of the
/// SAME phase the game would have skipped, one message later.</para>
/// </summary>
internal static class EnemyInfoPhaseSkip
{
    /// <summary>
    /// A reveal was entered and this class will decide about it on the following frames. Armed in
    /// the postfix of the reveal entry point rather than acted on there, because the Choreographer
    /// arms the button AFTER that method returns (<c>readyButton.Toggle(...)</c>,
    /// Choreographer.cs:3716) — a click issued inside the postfix would land on the button state of
    /// the phase we are leaving.
    /// </summary>
    private static bool _armed;

    /// <summary><see cref="Time.unscaledTime"/> at which <see cref="_armed"/> was set.</summary>
    private static float _armedAt;

    /// <summary>
    /// How long the arm survives. Generous next to the one or two frames the button needs, and
    /// bounded so a reveal that never becomes clickable (a modal parked over it, a torn-down
    /// track) can never leave a live trigger behind for a LATER phase — every fire re-checks the
    /// phase anyway, so this is belt-and-braces rather than the guard.
    /// </summary>
    private const float ArmWindowSeconds = 8f;

    /// <summary>One report per arming, so the log carries the decision and its inputs once.</summary>
    private static bool _logged;

    /// <summary>Why the last frame refused to press. Kept so an arm that runs out of window says
    /// WHAT it was waiting for instead of vanishing — the difference between "the skip is broken"
    /// and "a confirmation box sat over the reveal for eight seconds".</summary>
    private static string _lastBlock = "";

    /// <summary>Set once if the per-frame half ever throws, so a bad frame reports and goes quiet
    /// instead of writing a line per frame for the rest of the session.</summary>
    private static bool _reportedThrow;

    /// <summary>Reset with the rest of the board state (scenario teardown / hot reload).</summary>
    internal static void Reset()
    {
        _armed = false;
        _logged = false;
        _armedAt = 0f;
        _lastBlock = "";
    }

    // ------------------------------------------------------------------------------- the count --

    /// <summary>
    /// HOW MANY ENEMY ROWS THE REVEAL WILL ACTUALLY SHOW — the game's own number, read off the
    /// game's own pool. <c>EnemyAbilityCardsAnimation</c> computes exactly this as its <c>num2</c>
    /// (InitiativeTrack.cs:548-555) and animates one card per counted row;
    /// <c>FinishAbilityCardsAnimation</c> iterates the same set. Zero here is therefore not "the
    /// scenario has no monsters", it is "this screen is blank".
    /// </summary>
    internal static int VisibleEnemyRows(InitiativeTrack? track)
    {
        List<InitiativeTrackEnemyBehaviour>? rows = track != null ? track.enemiesUI : null;
        if (rows == null)
            return 0;
        int n = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            InitiativeTrackEnemyBehaviour row = rows[i];
            if (row != null && row.gameObject.activeSelf)
                n++;
        }
        return n;
    }

    /// <summary>
    /// Is THIS machine the seat that owns the confirm? Offline every seat is; online only the host.
    /// Stated here as its own predicate (rather than relying on the client's button being disabled)
    /// so the auto-skip is host-authoritative BY CONSTRUCTION and a peer never even counts.
    /// </summary>
    private static bool IsDecidingSeat => !FFSNetwork.IsOnline || !FFSNetwork.IsClient;

    // --------------------------------------------------------------------------- arm and decide --

    /// <summary>Called from the reveal entry point's postfix — see <see cref="_armed"/>.</summary>
    internal static void Arm()
    {
        _armed = true;
        _armedAt = Time.unscaledTime;
        _logged = false;
        _lastBlock = "";
    }

    /// <summary>
    /// Per frame while an initiative track exists (postfix on its own <c>Update</c>, so the tick
    /// lives and dies exactly with the widget this decision is about). One bool compare in the
    /// steady state.
    /// </summary>
    internal static void Tick(InitiativeTrack? track)
    {
        if (!_armed)
            return;
        if (track == null || Time.unscaledTime - _armedAt > ArmWindowSeconds)
        {
            bool expired = track != null;
            _armed = false;
            if (expired)
            {
                string waited = _lastBlock.Length > 0
                    ? _lastBlock
                    : "the phase never became decidable";
                LogOnce($"ENEMY-INFO PHASE: gave up after {ArmWindowSeconds:F0} s without pressing " +
                        $"anything — {waited}. The host's 'Fortfahren' is untouched and works " +
                        "exactly as vanilla; nothing was consumed.");
            }
            return;
        }

        // Still the enemy-information phase? The arm is dropped the moment it is not — a fire in a
        // later phase would be pressing a button that means something else entirely.
        if (PhaseManager.PhaseType != CPhase.PhaseType.MonsterClassesSelectAbilityCards)
        {
            _armed = false;
            return;
        }

        int visible = VisibleEnemyRows(track);
        if (!IsDecidingSeat)
        {
            _armed = false;
            LogOnce($"ENEMY-INFO PHASE: {visible} visible enemy row(s) on this screen — but this " +
                    "client is a PEER, so it decides NOTHING here and simply follows. Vanilla " +
                    "agrees: a client's ReadyButton is forced non-interactable for the whole of " +
                    "MonsterClassesSelectAbilityCards (ReadyButton.cs:496-505) and its click " +
                    "returns immediately (ReadyButton.cs:255-258). Whatever the host decides " +
                    "arrives over the game's own replicated stream.");
            return;
        }

        if (visible > 0)
        {
            _armed = false;
            LogOnce($"ENEMY-INFO PHASE: NOT skipped — {visible} enemy row(s) are visible on the " +
                    "initiative track, so there IS enemy information to read and the host's " +
                    "'Fortfahren' is the right control to be waiting on. The count is the game's " +
                    "own: active entries of InitiativeTrack.enemiesUI, the same number " +
                    "EnemyAbilityCardsAnimation animates a card for (InitiativeTrack.cs:548-555).");
            return;
        }

        // Zero rows. The game's own empty-phase skip (Choreographer.cs:3705) also stands down while
        // the active-bonus bar is up — honour the same term, or this would skip a phase the game
        // would not have.
        if (ActiveBonusBarShowing())
        {
            _lastBlock = "the active-bonus bar was showing, which is the second half of the game's " +
                         "own empty-phase condition (Choreographer.cs:3705) — the player has " +
                         "something to decide there, so the screen is not empty";
            return; // keep the arm: the bar may close inside the window
        }

        Choreographer? choreographer = Choreographer.s_Choreographer;
        ReadyButton? button = choreographer != null ? choreographer.readyButton : null;
        if (button == null)
        {
            _lastBlock = "the Choreographer's ReadyButton did not exist";
            return; // keep the arm: the button is built later than the reveal on a cold track
        }

        if (!Clickable(button, out string why))
        {
            _lastBlock = why; // reported if the window runs out — see _lastBlock
            return;           // keep the arm: retry next frame, e.g. while a confirmation box is up
        }

        _armed = false;
        LogOnce("ENEMY-INFO PHASE SKIPPED: 0 visible enemy row(s) — the reveal screen is BLANK, so " +
                "the host's 'Fortfahren' was pressed for them. WHY THE GAME DID NOT SKIP IT " +
                "ITSELF: its own guard is Choreographer.ClientMonsterObjects.Count == 0 " +
                "(Choreographer.cs:3705), and that list CONCATENATES m_ClientObjects — chests, " +
                "obstacles and activatable props, none of which are enemies and none of which " +
                "carry a monster class. The list that decides what is DRAWN is filtered far " +
                "harder (InitiativeTrack.cs:753: distinct CMonsterClass with a non-null " +
                "RoundAbilityCard), so an object-only room produces a non-empty guard list and an " +
                "empty screen. Counted here off the game's own pool (active InitiativeTrack" +
                ".enemiesUI rows = EnemyAbilityCardsAnimation's num2), never off a predicate of " +
                "our own. Advanced through ReadyButton.OnClickInternal(networkActionIfOnline: " +
                "true) — the seam every press path converges on, so the queued " +
                "FinishAbilityCardsAnimation runs and Synchronizer.SendGameAction(ConfirmAction) " +
                "goes out byte for byte as it would from a human press. Host-only by " +
                "construction: peers return above and leave the phase when this confirm " +
                "replicates.");
        button.OnClickInternal();
    }

    /// <summary>
    /// The preconditions <c>ReadyButton.OnClick</c> itself checks before it forwards to
    /// <c>OnClickInternal</c> (ReadyButton.cs:188-202), plus the one <c>OnClickInternal</c> checks
    /// first (ReadyButton.cs:246-249). Failing any of them is a REASON TO WAIT, not to give up:
    /// a confirmation box over the reveal closes, and the phase gate above ends the wait if the
    /// game moves on regardless.
    /// </summary>
    private static bool Clickable(ReadyButton button, out string why)
    {
        why = "";
        if (button.buttonState != ReadyButton.EButtonState.EREADYBUTTONCONTINUE)
        {
            why = $"the ready button is in {button.buttonState}, not EREADYBUTTONCONTINUE";
            return false;
        }
        Button? component = button.ButtonComponent;
        if (component == null || !component.enabled || !button.IsInteractable)
        {
            why = "the ready button is not interactable yet";
            return false;
        }
        if (button.warningMask != null && button.warningMask.gameObject.activeSelf)
        {
            why = "the button's warning mask is up (the game wants an explicit acknowledgement)";
            return false;
        }
        if (ConfirmationBoxOpen())
        {
            why = "a confirmation box is open — OnClickInternal would return immediately";
            return false;
        }
        return true;
    }

    /// <summary>The second half of the game's own empty-phase condition, read defensively: the
    /// singleton is scene-bound and a torn-down scene may not have one.</summary>
    private static bool ActiveBonusBarShowing()
    {
        try
        {
            UIActiveBonusBar bar = Singleton<UIActiveBonusBar>.Instance;
            return bar != null && bar.IsShowing;
        }
        catch
        {
            return false;
        }
    }

    private static bool ConfirmationBoxOpen()
    {
        try
        {
            UIConfirmationBoxManager box = Singleton<UIConfirmationBoxManager>.Instance;
            return box != null && box.IsRequested;
        }
        catch
        {
            return false;
        }
    }

    private static void LogOnce(string line)
    {
        if (_logged)
            return;
        _logged = true;
        VRLog.Info("Board", "[EnemyInfo] " + line);
    }

    /// <summary>Arm, isolated. A Harmony postfix that throws takes the game's own method down with
    /// it, and this decision is never worth that.</summary>
    internal static void ArmGuarded()
    {
        try
        {
            Arm();
        }
        catch (System.Exception e)
        {
            Disarm(e);
        }
    }

    /// <summary>The per-frame half, isolated. No closure: the postfix hands the instance straight
    /// through, because this runs in a Unity <c>Update</c> and a lambda per frame is a steady-state
    /// allocation (the same rule <c>FocusDriver</c>'s cached delegates follow).</summary>
    internal static void TickGuarded(InitiativeTrack? track)
    {
        try
        {
            Tick(track);
        }
        catch (System.Exception e)
        {
            Disarm(e);
        }
    }

    private static void Disarm(System.Exception e)
    {
        _armed = false;
        if (_reportedThrow)
            return;
        _reportedThrow = true;
        VRLog.Warn("Board", "[EnemyInfo] the empty-phase skip threw and was DISARMED for this " +
                            "reveal — the host's 'Fortfahren' still works exactly as vanilla, " +
                            $"nothing was consumed. {e}");
    }
}

/// <summary>
/// Arm on the reveal. <c>ShowMonsterClassesForSelectingRoundAbilityCards</c> is the game's public
/// entry into the enemy-information screen (InitiativeTrack.cs:741) and the ONE place it is called
/// from is the <c>MonsterClassesToSelectAbilityCards</c> message (Choreographer.cs:3715), so a
/// postfix here fires exactly once per reveal, on every client, and never for any other phase.
/// </summary>
[HarmonyPatch(typeof(InitiativeTrack),
              nameof(InitiativeTrack.ShowMonsterClassesForSelectingRoundAbilityCards))]
internal static class InitiativeTrack_ShowMonsterClasses_ArmSkip
{
    private static void Postfix() => EnemyInfoPhaseSkip.ArmGuarded();
}

/// <summary>
/// The per-frame half. Hung on the track's OWN <c>Update</c> rather than on a mod driver so the
/// decision cannot outlive the widget it is about, and so it works identically in flat play (where
/// none of the mod's world-space surfaces exist). Private method — patched by string name, which is
/// how Harmony addresses a Unity message.
///
/// <para>IT CARRIES TWO RIDERS, AND THAT IS DELIBERATE. This is the mod's ONE per-frame seam that
/// already holds a live <c>InitiativeTrack</c> instance, so a second track-side feature
/// (<see cref="PickPhaseInitiativeTrack"/> — the blank row band during a forced card pick, user
/// report 2026-08-24) rides it instead of adding a SECOND Harmony patch on the same method. Two
/// patches on one Unity message would have exactly the same cost and exactly the same ordering,
/// plus a second thing that can fail to register. Each rider is independently try/caught, so
/// neither can take the other — or the game's own <c>Update</c> — down with it.</para>
/// </summary>
[HarmonyPatch(typeof(InitiativeTrack), "Update")]
internal static class InitiativeTrack_Update_TickSkip
{
    private static void Postfix(InitiativeTrack __instance)
    {
        EnemyInfoPhaseSkip.TickGuarded(__instance);
        PickPhaseInitiativeTrack.TickGuarded(__instance);
    }
}
