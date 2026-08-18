using System.Collections.Generic;
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
/// <para>The mechanism: <c>WorldUI/ActorBars</c> hides a figure's adopted worldspace panel
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
/// <para>REJECTED — not hiding the bar of a held figure at all (<c>ActorBars</c>). That is the one
/// line that does the damage, and removing it WOULD fix this instance — but the hide is a
/// deliberate user-facing choice ("the bar clutters the hand"), it lives in another module, and it
/// would leave the general shape ("a grab deactivates game objects mid-coroutine") open for the
/// next thing that hides something. The companion change LANDED in the narrower form instead:
/// ActorBars now skips the hide for a bar that reports <c>FlowControlActive()</c>, so the
/// mechanism is covered there too. This gate does not depend on it.</para>
/// <para>REJECTED — a watchdog ALONE. It repairs instead of preventing, and the user's words are
/// "sowas darf unter keinen Umständen passieren". It is kept as a BACKSTOP only
/// (<see cref="FigureStallWatchdog"/>).</para>
///
/// ──────────────────────────────────────────────── THE HOLD GATE SPLIT (2026-08-11, second report)
///
/// <para>The first build of this gate applied ALL of its clauses to the figure already IN the hand
/// too: <c>FigureGrabDriver.AutoReleaseMovedFigures</c> and <c>FigureGrabbable.AllowsHand</c> asked
/// the same <see cref="IsBusy"/> that gates the grab, so the GLOBAL clause — "the choreographer is
/// in an unbounded wait, refuse every figure" — dumped a held mini out of the hand the moment ANY
/// attack resolved anywhere on the board. The hardware log of that build shows it exactly: every
/// single "AUTO-RELEASE (turn-deadlock gate)" line carries the global why-string ("the game is
/// resolving an attack … the only two waits with no timeout"), never a per-figure one. The user's
/// ruling (hardware, 2026-08-11, verbatim):</para>
///
/// <para>"Auf Reaktion auf den letzten Deadlock in dem ich eine Figur gegriffen habe, die gerade
/// eine ANimation ausführt ist es jetzt wohl garnicht mehr möglich. Das ist ok wir können dabei
/// bleiben, dass man nur figure aufheben kann die idle Animationen abspielen ABER wenn ich eine
/// Figur in der Hand habe die idle ist soll sie auch in der Hand bleiben wenn wpo ganz anders
/// gerade animationen abgespielt werden. Aktuell ist es so, dass die Figur sofort aus der Hand
/// verschwindet, während einer Animation. Das soll nicht sein. Solange diese eine figure idle its
/// soll sie auch in der Hand bleiben können, egal was passiert. Wenn diese eine Figur, die man in
/// der Hand hat jetzt doch eine Animation abspielt soll sie zurück aufs Feld gehen, aber auch mit
/// der üblichen Animation als hätte der User sie losgelassen."</para>
///
/// <para>So the predicate is now TWO predicates over one shared per-figure core:</para>
/// <list type="bullet">
///   <item><see cref="IsBusy"/> — the GRAB gate, unchanged in reach: all per-figure clauses PLUS
///   the global unbounded-wait clause. Nothing may be PICKED UP while the game resolves an attack,
///   exactly as before ("wir können dabei bleiben").</item>
///   <item><see cref="HoldMustEnd"/> — the HOLD gate: the per-figure clauses ONLY. A held figure
///   stays in the hand through other figures' turns, attacks and animations ("egal was passiert"),
///   and is returned the moment the game depends on THIS figure — its bar flow, a choreographer
///   wait naming it, its own action being resolved, or its own animator leaving idle.</item>
/// </list>
///
/// <para>THE PER-FIGURE IDLE SIGNAL is the game's own: <c>Choreographer.IdleStates</c>
/// (Choreographer.cs:515 — "Idle-Run", "SleepIdle", "CheerAllyIdle", "CheerEnemyIdle") checked
/// against the actor's Animator exactly the way every choreographer wait-exit checks it
/// (<c>MF.GameObjectAnimatorControllerIsCurrentState</c>, MF.cs:190 —
/// <c>GetCurrentAnimatorStateInfo(0).IsName(state)</c> guarded by <c>HasState</c>). Reading the
/// same source the turn machine reads is what makes grab gate and hold gate agree: the user's own
/// definition of the grab rule is "nur Figuren … die idle Animationen abspielen", so the same
/// animator clause is applied to the grab edge too — a figure animating OUTSIDE any choreographer
/// wait is refused up front instead of being grabbed and auto-released one frame later (a grab
/// flash). Actors whose controller has NONE of the four idle states fail OPEN (treated as idle):
/// for such an actor "left idle" is meaningless, and the game-dependency clauses plus the watchdog
/// still cover it — failing closed would make it permanently ungrabbable.</para>
///
/// <para>WHY THE PER-FIGURE HOLD GATE CANNOT RECREATE THE SKELETON DEADLOCK. The hang needs three
/// links: a LIVE bar-flow coroutine, its host GameObject DEACTIVATED under it (the kill), and an
/// untimed wait latched on the flow flag. Walk the links:</para>
/// <list type="number">
///   <item>The deactivation edge only ever happens at GRAB time (ActorBars hides the host the tick
///   after the actor enters <see cref="HeldFigures"/>; while held it stays hidden — SetActive(false)
///   on an inactive host is a no-op — and release re-ACTIVATES, which kills nothing). The grab edge
///   is gated by the UNCHANGED <see cref="IsBusy"/>, global clause included, so no figure can enter
///   the hand while either untimed wait is live or while its own bar flow runs — exactly the
///   protection that has held since the incident.</item>
///   <item>A flow cannot COME ALIVE on a figure that is already held: <c>DisplayAttackModifierFlow</c>
///   starts its coroutines via <c>StartCoroutine</c> on the panel controller
///   (WorldspacePanelUIController.cs:618-643), and Unity refuses to start a coroutine on an
///   inactive host — the body never runs, and <c>IsFlowActive</c> is set INSIDE that body
///   (AttackModBar.cs:328), so the flag cannot latch true on a hidden bar. No live coroutine, no
///   kill, no latch. (The attack's bar UI is skipped for that one figure — the same already-true
///   consequence any release glide has today — and the choreographer's own exit still completes:
///   <c>WaitingForPlayerIdle</c> needs <c>!FlowControlActive()</c>, which stays false, plus the
///   actor idle, which the Animator — never suppressed for held figures — still reaches;
///   <c>WaitingForAttackModifierCards</c> names NO actor at all and self-drains its card list on
///   750 ms ticks, Choreographer.cs:2523-2540, 5188/5217.)</item>
///   <item>If the game nonetheless comes to depend on the held figure, the hold ends the same
///   frame through the per-figure clauses — and the 2026-08-11 deadlock is INSIDE that cover:
///   <c>WaitingForPlayerIdle</c> NAMES its actor (<c>m_StateWaitActorGO</c>, the exact figure whose
///   grab caused the hang), so "wait naming this figure" releases precisely the mini that
///   incident's player was holding. The residual sub-frame race (a flow starting on a figure in
///   the same frame it is grabbed, before ActorBars' hide) predates this change, is identical in
///   width to the old global gate's, and is what <see cref="FigureStallWatchdog"/> — unchanged,
///   last-resort — exists to repair.</item>
/// </list>
///
/// <para>The forced release takes the NORMAL user-release glide ("mit der üblichen Animation als
/// hätte der User sie losgelassen") — see <c>FigureGrabbable.OnRelease</c> for why the glide's
/// 0.28 s of continued bar-hide is safe (no deactivation edge, so nothing can be killed by it).</para>
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

    /// <summary>
    /// The game's OWN idle vocabulary — a verbatim copy of <c>Choreographer.IdleStates</c>
    /// (Choreographer.cs:515). Copied rather than read because that member is a property that
    /// allocates a fresh <c>List&lt;string&gt;</c> on every get — per-frame per-figure reads may
    /// not pay that. If a game update ever changed the list, every choreographer wait-exit this
    /// gate mirrors would change with it, so the copy and its source drift together or not at all.
    /// </summary>
    private static readonly string[] IdleStateNames = { "Idle-Run", "SleepIdle", "CheerAllyIdle", "CheerEnemyIdle" };

    /// <summary>
    /// Per-actor Animator resolution cache (keyed by root GameObject instance id, exactly like the
    /// driver's FigureScanCache: a destroyed value falls back to a re-resolve, an id can never pin
    /// a dead object). The game resolves the animator with <c>MF.GetGameObjectAnimator</c> — a
    /// <c>GetComponentsInChildren&lt;Animator&gt;</c> walk that allocates — on every idle check;
    /// this gate runs the check per adopted figure per frame and may not. The bool is whether that
    /// animator's controller carries ANY of the four idle states (<c>Animator.HasState</c>, the
    /// same guard MF.cs:190 applies): a controller with none fails OPEN — see the class doc.
    /// Cleared by <see cref="ClearCache"/> from the driver's teardown, so it is bounded per
    /// scenario like the driver's own caches.
    /// </summary>
    private static readonly Dictionary<int, (Animator animator, bool hasIdleVocabulary)> AnimatorCache = new(64);

    /// <summary>Driver teardown hook (scene change / GrabFigures off): drop the per-actor animator
    /// cache with the adoptions it was built for.</summary>
    internal static void ClearCache() => AnimatorCache.Clear();

    /// <summary>
    /// True when this figure's own Animator is currently in a NON-idle state — the game's
    /// per-actor "it is doing something" signal, read from the same source every choreographer
    /// wait-exit reads (<c>IdleStates.Any(x => MF.GameObjectAnimatorControllerIsCurrentState(go, x))</c>).
    /// False (idle) when the actor has no usable animator or its controller lacks the idle
    /// vocabulary entirely — fail open, per the class doc.
    /// </summary>
    private static bool OwnAnimationPlaying(ActorBehaviour actor)
    {
        GameObject root = actor.m_RootGameObject;
        if (root == null)
            return false;

        int id = root.GetInstanceID();
        if (!AnimatorCache.TryGetValue(id, out (Animator animator, bool hasIdleVocabulary) entry)
            || entry.animator == null)
        {
            entry = ResolveAnimator(root);
            AnimatorCache[id] = entry;
        }

        Animator animator = entry.animator;
        if (animator == null || !entry.hasIdleVocabulary || animator.runtimeAnimatorController == null)
            return false;

        // The exact comparison MF.GameObjectAnimatorControllerIsCurrentState performs, minus the
        // per-call HasState/StringToHash work the cache already answered.
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        for (int i = 0; i < IdleStateNames.Length; i++)
        {
            if (state.IsName(IdleStateNames[i]))
                return false;
        }
        return true;
    }

    /// <summary>Mirror of <c>MF.GetGameObjectAnimator</c> (MF.cs:52 — first child Animator with a
    /// controller), plus the one-time idle-vocabulary probe the cache stores alongside it.</summary>
    private static (Animator animator, bool hasIdleVocabulary) ResolveAnimator(GameObject root)
    {
        Animator[] animators = root.GetComponentsInChildren<Animator>();
        for (int i = 0; i < animators.Length; i++)
        {
            Animator candidate = animators[i];
            if (candidate == null || candidate.runtimeAnimatorController == null)
                continue;
            for (int s = 0; s < IdleStateNames.Length; s++)
            {
                if (candidate.HasState(0, Animator.StringToHash(IdleStateNames[s])))
                    return (candidate, true);
            }
            return (candidate, false);
        }
        return (null!, false);
    }

    /// <summary>
    /// The shared per-figure core of both gates: true when the game currently depends on THIS
    /// figure (its bar flow, a choreographer wait naming it, its own action mid-message) or the
    /// figure itself is not idle. <paramref name="why"/> is the log vocabulary.
    /// </summary>
    private static bool FigureItselfBusy(ActorBehaviour actor, out string why)
    {
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

        // (2) The choreographer is waiting on THIS figure — its animation, or (WaitingForPlayerIdle,
        //     which names its actor via m_StateWaitActorGO) the untimed wait whose exit depends on
        //     this figure's bar and idle pose. The 2026-08-11 deadlock's grabbed figure was exactly
        //     the named actor of such a wait.
        if (_animationWait && ReferenceEquals(_waitActor, actor))
        {
            why = "the choreographer is waiting on this figure";
            return true;
        }

        // (3) It is this figure's action and the rule engine is mid-message.
        if (_ruleClientBusy && _currentActor != null && ReferenceEquals(_currentActor, actor.Actor))
        {
            why = "its own action is being resolved right now";
            return true;
        }

        // (4) The figure's own Animator has left the game's idle states — the user's ruling in
        //     code: "Wenn diese eine Figur … jetzt doch eine Animation abspielt soll sie zurück
        //     aufs Feld gehen". Also part of the GRAB gate so the two agree (see the class doc).
        if (OwnAnimationPlaying(actor))
        {
            why = "the figure itself is playing a non-idle animation (Choreographer.IdleStates)";
            return true;
        }

        why = string.Empty;
        return false;
    }

    /// <summary>The GRAB gate: true when the game's turn machine currently depends on this figure —
    /// or on a bar coroutine any grab could kill (the GLOBAL unbounded-wait clause, which applies
    /// to the grab edge ONLY; see <see cref="HoldMustEnd"/> for the hand). <paramref name="why"/>
    /// is the log vocabulary for the refusal line.</summary>
    internal static bool IsBusy(ActorBehaviour? actor, out string why)
    {
        why = string.Empty;
        if (actor == null)
            return false;
        Sample();

        // GLOBAL: the choreographer is in one of the two states that can wait FOREVER. No grab at
        // all, because the figure whose bar the flow belongs to is the attack's TARGET and is not
        // knowable from "whose turn is it" — the 2026-08-11 deadlock is exactly that case. Checked
        // first (it is the cheap sampled flag) so a mid-attack refusal names the attack, not
        // whichever per-figure clause happens to be true as well.
        if (_unboundedWait)
        {
            why = "the game is resolving an attack (choreographer WaitingForPlayerIdle / "
                  + "WaitingForAttackModifierCards — the only two waits with no timeout)";
            return true;
        }

        return FigureItselfBusy(actor, out why);
    }

    internal static bool IsBusy(ActorBehaviour? actor) => IsBusy(actor, out _);

    /// <summary>
    /// The HOLD gate (user ruling 2026-08-11, see the class doc): true when the figure already in
    /// the hand must go back to the board — the game depends on THIS figure or the figure itself
    /// has left idle. Deliberately does NOT include the global unbounded-wait clause: a held idle
    /// figure stays in the hand while other figures fight ("egal was passiert"). Consumed by
    /// <c>FigureGrabDriver.AutoReleaseMovedFigures</c> and by <c>FigureGrabbable.AllowsHand</c>
    /// for the hand that holds the figure (the ProximityGrabber heal path).
    /// </summary>
    internal static bool HoldMustEnd(ActorBehaviour? actor, out string why)
    {
        why = string.Empty;
        if (actor == null)
            return false;
        Sample();
        return FigureItselfBusy(actor, out why);
    }

    internal static bool HoldMustEnd(ActorBehaviour? actor) => HoldMustEnd(actor, out _);
}
