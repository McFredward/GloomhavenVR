using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// "IS THE GAME CURRENTLY DEPENDING ON THIS FIGURE?" — the gate that makes the ModBuild 107
/// TURN DEADLOCK structurally impossible.
///
/// ───────────────────────────────────────────────────────────────── THE USER REPORT (verbatim)
///
/// <para>2026-08-11, hardware, ModBuild 107 (b765a5b6e):
/// "SEHR WICHTIG: DEADLOCK - ich habe ein Skelet hochgehoben während es dran war, dann ist
/// plötzlich nichts mehr passiert die Gegner haben nicht mehr weitergemacht - sowas darf unter
/// keinen Umständen passieren. Vollziehe den deadlock in den logs nach und fix es."</para>
///
/// ─────────────────────────────────────────────────────────────────────────────── ROOT CAUSE
///
/// <para>Reconstructed from <c>Player.log</c> of that run; NO exception is involved, which is why
/// the mod log is silent about it. The chain, with the log lines that prove each link:</para>
///
/// <code>
/// 18625  [MessageHandler] PlayerWaitForIdle                       ← choreographer enters
///                                                                   WaitingForPlayerIdle on the
///                                                                   ATTACK'S TARGET (City Guard
///                                                                   Elite): CAbilityAttack.cs:1710
///                                                                   sets m_Actor = m_AttackingActors[0]
/// 18635  [DLL Log] Elite-Stadtwache (5) fügt Wandelndes Elite-     ← THE LAST [DLL Log] LINE IN THE
///        Skelett (8) mit Vergeltung [Retaliate] 1 Schaden zu        WHOLE 22 249-LINE FILE. The
///                                                                   scenario rule engine never
///                                                                   speaks again.
/// 18640  [AttackModifiers]: ShowAttackModifDamage show label       ← frame 28193. The bar's
///                                                                   coroutine is now inside its
///                                                                   final WaitForSeconds; the
///                                                                   healthy round two attacks
///                                                                   earlier took ~90 frames from
///                                                                   "show label" to "finish"
///                                                                   (27710 → 27800).
/// 18659  [FigureGrab] Right grabbed figure (LivingBonesEliteID)    ← frame ~28235, the skeleton.
/// 18674  [FigureGrab] Right released figure (LivingBonesEliteID)
/// 18695  [FigureGrab] Right grabbed figure (CityGuardEliteID)      ← frame ~28255. THE TARGET,
///                                                                   ~28 frames before its bar
///                                                                   coroutine would have finished.
///        (never)  [AttackModifiers]: ShowAttackModifDamage finish  ← it never finishes.
///        (never)  [AttackModifiers]: FinalizeFlow
/// </code>
///
/// <para>The mechanism: <c>WorldUI/ActorBars.cs:469</c> hides a figure's adopted worldspace panel
/// while its mini is in the hand (<c>panel.HostGo.SetActive(!hide)</c>, keyed on
/// <see cref="HeldFigures.Owns"/>). The game's <c>AttackModBar</c> flow coroutines are started ON
/// THAT PANEL'S <c>WorldspacePanelUIController</c> (WorldspacePanelUIController.cs:635, :655), and
/// Unity KILLS a coroutine permanently the moment its GameObject is deactivated. So the grab
/// killed <c>ShowAttackModifDamage</c> mid-wait; <c>FinalizeFlow()</c> — the ONLY writer of
/// <c>AttackModBar.IsFlowActive = false</c> (AttackModBar.cs:541) — never ran; and</para>
///
/// <code>
///   case ChoreographerStateType.WaitingForPlayerIdle:            // Choreographer.cs:2716-2726
///     if (!Scenario.HasActor(waitActor)
///         || (!GetActorBehaviour(waitGO).m_WorldspacePanelUI.FlowControlActive()   // ← latched TRUE
///             &amp;&amp; IdleStates.Any(...)))
///     { SetChoreographerState(Play); ScenarioRuleClient.StepComplete(); }
/// </code>
///
/// <para>…has <b>no timeout</b> — unlike every neighbouring wait state, it carries no
/// <c>m_StateWaitTickFrame</c> escape. <c>StepComplete()</c> is therefore never called and the
/// whole scenario turn machine stops for the rest of the session. Releasing the figure
/// re-activates the host but does NOT resurrect a killed coroutine, which is exactly why the
/// user's game never recovered.</para>
///
/// <para>NOTE WHICH FIGURE BROKE IT: not the one whose turn it was (the skeleton) but the TARGET
/// of its attack. A guard that only refused "the acting figure" would have missed this defect
/// entirely. The gate below is keyed on the game's own dependency, not on whose turn it is.</para>
///
/// ────────────────────────────────────────────────────────────────────────────── THE GATE
///
/// <para>Exactly two choreographer states can wait FOREVER —
/// <c>WaitingForPlayerIdle</c> (Choreographer.cs:2716, no tick timeout) and
/// <c>WaitingForAttackModifierCards</c> (:2593, no tick timeout, waits on the
/// <c>FinishedDrawingModifiers</c> actor event that only <c>FinalizeFlow</c>/<c>ShowModifiers</c>
/// register). BOTH exits depend on a bar coroutine that our host-hide can kill. While the game is
/// in either of them NO figure may be picked up at all — that single rule is what makes this class
/// of hang impossible rather than merely unlikely, and it costs the player at most the two seconds
/// an attack takes to resolve.</para>
///
/// <para>The remaining clauses are narrower and per-actor: a live bar flow on THIS figure, the
/// choreographer waiting on THIS figure's animation, or the rule engine mid-message on the figure
/// whose turn it is. They cover the same hazard outside those two states and give the "you cannot
/// pick up a figure that is currently moving" behaviour the design asks for.</para>
///
/// <para>MULTIPLAYER: purely local and read-only. A refused grab never enters
/// <see cref="HeldFigures"/>, and <c>Net/NetFigures.TrySampleHeldSlot</c> samples nothing else, so
/// a refusal is never broadcast as a take. Every client evaluates its own choreographer, which is
/// the same state machine driven by the same messages. Strict no-op offline and online alike.</para>
///
/// <para>REJECTED — patching <c>Choreographer.WaitingForPlayerIdle</c> to add a timeout. It is the
/// game's turn machine; the standing rule is UI seams only, and a mod-invented timeout would fire
/// on legitimate slow flows and desync a multiplayer table.</para>
/// <para>REJECTED — not hiding the bar of a held figure at all (<c>ActorBars.cs:469</c>). That is
/// the one line that does the damage, and removing it WOULD fix this instance — but the hide is a
/// deliberate user-facing choice ("the bar clutters the hand"), it lives in another module, and it
/// would leave the general shape ("a grab deactivates game objects mid-coroutine") open for the
/// next thing that hides something. Reported to the integrator as the recommended companion
/// change; this gate does not depend on it.</para>
/// <para>REJECTED — a watchdog ALONE. It repairs instead of preventing, and the user's words are
/// "sowas darf unter keinen Umständen passieren". It is kept as a BACKSTOP only
/// (<see cref="FigureStallWatchdog"/>).</para>
/// </summary>
internal static class FigureBusy
{
    /// <summary>Per-frame snapshot of the global (non per-actor) half of the predicate, so the
    /// per-figure calls — up to one per adopted figure per hand per frame — cost reference
    /// compares and nothing else.</summary>
    private static int _sampledFrame = -1;

    private static bool _unboundedWait;
    private static bool _animationWait;
    private static ActorBehaviour? _waitActor;
    private static CActor? _currentActor;
    private static bool _ruleClientBusy;

    /// <summary>
    /// The two choreographer states whose exit condition has NO timeout and depends on an
    /// <c>AttackModBar</c> coroutine completing. While the game sits in either of them, grabbing
    /// ANY figure is refused — see the class doc.
    /// </summary>
    private static bool IsUnboundedFlowWait(Choreographer.ChoreographerStateType s)
        => s == Choreographer.ChoreographerStateType.WaitingForPlayerIdle
           || s == Choreographer.ChoreographerStateType.WaitingForAttackModifierCards;

    /// <summary>
    /// The choreographer states that wait on an ACTOR'S ANIMATION or on the rule engine's progress
    /// (as opposed to the ones that wait on a human: card selection, waypoint picking, rewards —
    /// those last minutes and are precisely when a player wants to pick a mini up). These all carry
    /// a tick timeout, so they cannot hang on their own; the figure they name is still refused
    /// because riding the hand is exactly what stops it reaching the pose they are waiting for.
    /// </summary>
    private static bool IsAnimationWait(Choreographer.ChoreographerStateType s)
        => s == Choreographer.ChoreographerStateType.WaitingForMoveAnim
           || s == Choreographer.ChoreographerStateType.WaitingForAttackAnim
           || s == Choreographer.ChoreographerStateType.WaitingForModifierDrawAnim
           || s == Choreographer.ChoreographerStateType.WaitingForDamageAnim
           || s == Choreographer.ChoreographerStateType.WaitingForGeneralAnim
           || s == Choreographer.ChoreographerStateType.WaitingForEndAbilityAnimSync
           || s == Choreographer.ChoreographerStateType.WaitingForProgressChoreographer
           || IsUnboundedFlowWait(s);

    private static void Sample()
    {
        if (_sampledFrame == Time.frameCount)
            return;
        _sampledFrame = Time.frameCount;

        _unboundedWait = false;
        _animationWait = false;
        _waitActor = null;
        _currentActor = null;
        _ruleClientBusy = false;

        Choreographer choreo = Choreographer.s_Choreographer;
        if (choreo == null)
            return;

        _currentActor = choreo.m_CurrentActor;
        try
        {
            // ScenarioRuleClient is a pure static; reading it can throw only if the library was
            // never initialised (main menu). Never let a diagnostic read break the grab path.
            _ruleClientBusy = ScenarioRuleClient.IsProcessingOrMessagesQueued;
        }
        catch
        {
            _ruleClientBusy = false;
        }

        Choreographer.CWaitState wait = choreo.m_WaitState;
        if (wait == null)
            return;

        _unboundedWait = IsUnboundedFlowWait(wait.m_State);
        _animationWait = IsAnimationWait(wait.m_State);
        if (_animationWait && wait.m_StateWaitActorGO != null)
            _waitActor = ActorBehaviour.GetActorBehaviour(wait.m_StateWaitActorGO);
    }

    /// <summary>True when the game's turn machine currently depends on this figure — or on a bar
    /// coroutine any grab could kill. See the class doc; <paramref name="why"/> is the log
    /// vocabulary for the refusal line.</summary>
    internal static bool IsBusy(ActorBehaviour? actor, out string why)
    {
        why = string.Empty;
        if (actor == null)
            return false;
        Sample();

        // (1) An attack-modifier flow is LIVE on this figure's own bar. This is the exact flag the
        //     deadlock latched: the flow's completion writes it false, and hiding the bar's host
        //     while the mini is in the hand is what stops the completion from ever running.
        WorldspacePanelUIController panel = actor.m_WorldspacePanelUI;
        if (panel != null && panel.FlowControlActive())
        {
            why = "its attack-modifier flow is still running (the bar's coroutine must finish before "
                  + "the choreographer's WaitingForPlayerIdle can complete)";
            return true;
        }

        // (2) The choreographer is in one of the two states that can wait FOREVER. No figure at
        //     all, because the figure whose bar the flow belongs to is the attack's TARGET and is
        //     not knowable from "whose turn is it" — the 2026-08-11 deadlock is exactly that case.
        if (_unboundedWait)
        {
            why = "the game is resolving an attack (choreographer WaitingForPlayerIdle / "
                  + "WaitingForAttackModifierCards — the only two waits with no timeout)";
            return true;
        }

        // (3) The choreographer is waiting on THIS figure's animation.
        if (_animationWait && ReferenceEquals(_waitActor, actor))
        {
            why = "the choreographer is waiting for this figure's animation to finish";
            return true;
        }

        // (4) It is this figure's action and the rule engine is mid-message.
        if (_ruleClientBusy && _currentActor != null && ReferenceEquals(_currentActor, actor.Actor))
        {
            why = "its own action is being resolved right now";
            return true;
        }

        return false;
    }

    internal static bool IsBusy(ActorBehaviour? actor) => IsBusy(actor, out _);
}
