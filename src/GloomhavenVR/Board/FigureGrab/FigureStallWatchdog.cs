using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// BACKSTOP for the ModBuild 107 turn deadlock — the belt to <see cref="FigureBusy"/>'s braces.
///
/// <para><see cref="FigureBusy"/> PREVENTS the hang (no figure can be held while the game is in a
/// wait whose exit depends on a bar coroutine). This class REPAIRS one if it ever happens anyway,
/// because the user's ruling is "sowas darf unter keinen Umständen passieren" and a defect that
/// costs a whole session — everyone's session, in multiplayer — earns a second line of defence.</para>
///
/// <para>WHAT IT WATCHES. The two choreographer states with no timeout (see
/// <see cref="FigureBusy"/> for why only these two can hang):
/// <c>WaitingForPlayerIdle</c> and <c>WaitingForAttackModifierCards</c>. Both exit only once the
/// <c>AttackModBar</c> flow that owns them has completed, and both were observed stuck for the
/// whole remainder of the 2026-08-11 session.</para>
///
/// <para>WHAT IT DOES. Calls the game's OWN public UI seam
/// <c>WorldspacePanelUIController.FinalizeAttackFlow()</c> on every actor bar still reporting
/// <c>FlowControlActive()</c>. That is precisely the call the killed coroutine would have made:
/// it stops the (dead) coroutines, sets <c>AttackModBar.IsFlowActive = false</c> and — the half
/// that matters for the second state — registers the <c>FinishedDrawingModifiers</c> actor event
/// the choreographer is waiting on (AttackModBar.cs:539-546). No <c>ScenarioRuleLibrary</c> and no
/// Bolt is touched; the rule engine is never told anything, it simply stops being blocked and
/// completes the step it was already trying to complete.</para>
///
/// <para>WHY IT CANNOT MISFIRE ON A HEALTHY GAME — four conditions, all required:</para>
/// <list type="number">
///   <item>the state is one of those two, and has not changed for <see cref="StallSeconds"/>;</item>
///   <item>a figure was HELD at some point since that state was entered. A player who never picks
///         a mini up can never trip this, so vanilla flow is untouchable by construction — and the
///         only known cause of the stall is our own host-hide during a hold;</item>
///   <item>a bar is actually still reporting a live flow (the latched flag);</item>
///   <item>that bar's controller is active and enabled, so the seam's own internal
///         <c>StartCoroutine</c>/<c>StopCoroutine</c> calls are legal.</item>
/// </list>
/// <para>For reference the healthy flow in the same hardware log ran ~90 frames (~1.5 s) from
/// "show label" to "finish"; <see cref="StallSeconds"/> is 8, i.e. five times the longest observed
/// legitimate flow.</para>
///
/// <para>MULTIPLAYER: strictly local and symmetric. Every client runs the same choreographer off
/// the same messages and repairs only its own UI state; nothing is sent, nothing is consumed from
/// the wire, and a peer that never stalled never fires. Guarded identically offline.</para>
///
/// <para>It logs at WARN with the full picture whenever it fires, so a hardware log tells us the
/// prevention leaked instead of quietly papering over it.</para>
/// </summary>
internal static class FigureStallWatchdog
{
    /// <summary>How long one of the two no-timeout wait states may persist before we treat it as a
    /// hang. Five times the longest legitimate attack-modifier flow measured on hardware.</summary>
    private const float StallSeconds = 8f;

    private static Choreographer.ChoreographerStateType _state = Choreographer.ChoreographerStateType.NA;
    private static GameObject? _waitGo;
    private static float _stateSince;
    private static bool _figureHeldSinceEnter;
    private static int _repairs;

    private static readonly List<WorldspacePanelUIController> Scratch = new(32);

    internal static void Reset()
    {
        _state = Choreographer.ChoreographerStateType.NA;
        _waitGo = null;
        _stateSince = 0f;
        _figureHeldSinceEnter = false;
    }

    internal static void Tick()
    {
        Choreographer choreo = Choreographer.s_Choreographer;
        Choreographer.CWaitState? wait = choreo != null ? choreo.m_WaitState : null;
        if (wait == null)
        {
            Reset();
            return;
        }

        float now = Time.unscaledTime;
        if (wait.m_State != _state || !ReferenceEquals(wait.m_StateWaitActorGO, _waitGo))
        {
            _state = wait.m_State;
            _waitGo = wait.m_StateWaitActorGO;
            _stateSince = now;
            _figureHeldSinceEnter = HeldFigures.Count > 0;
            return;
        }

        // Sticky: it is the hold DURING this wait that could have killed the bar coroutine, and the
        // player has usually let go again long before the stall is detectable.
        if (HeldFigures.Count > 0)
            _figureHeldSinceEnter = true;

        bool unbounded = _state == Choreographer.ChoreographerStateType.WaitingForPlayerIdle
                         || _state == Choreographer.ChoreographerStateType.WaitingForAttackModifierCards;
        if (!unbounded || !_figureHeldSinceEnter || now - _stateSince < StallSeconds)
            return;

        // Re-arm before repairing: if one pass is not enough the next one is another StallSeconds
        // away, so this can never become a per-frame hammer on the game's UI.
        _stateSince = now;

        WorldspaceUITools tools = WorldspaceUITools.Instance;
        if (tools == null || tools._panelUIControllers == null)
            return;

        Scratch.Clear();
        List<WorldspacePanelUIController> controllers = tools._panelUIControllers;
        for (int i = 0; i < controllers.Count; i++)
        {
            WorldspacePanelUIController c = controllers[i];
            if (c == null || !c.isActiveAndEnabled)
                continue;
            if (c.FlowControlActive())
                Scratch.Add(c);
        }
        if (Scratch.Count == 0)
            return;

        _repairs++;
        for (int i = 0; i < Scratch.Count; i++)
        {
            try
            {
                Scratch[i].FinalizeAttackFlow();
            }
            catch (System.Exception ex)
            {
                VRLog.Warn("FigureGrab",
                    $"TURN STALL WATCHDOG: FinalizeAttackFlow threw on '{Scratch[i].name}' "
                    + $"({ex.GetType().Name}: {ex.Message}) — continuing with the other bars.");
            }
        }

        VRLog.Warn("FigureGrab",
            $"TURN STALL WATCHDOG FIRED (repair #{_repairs}): the choreographer has been in {_state} "
            + $"for {StallSeconds:0}s with a figure held during it, and {Scratch.Count} actor bar(s) "
            + "still reported FlowControlActive() — the signature of an AttackModBar coroutine that "
            + "was killed when its panel host was deactivated under a held mini (ActorBars.cs host "
            + "hide). FinalizeAttackFlow() called on those bars, which is exactly what the killed "
            + "coroutine would have done, so WaitingForPlayerIdle can complete its step. THIS LINE "
            + "MEANS THE PREVENTION IN FigureBusy LEAKED — treat it as a defect report, not as a "
            + "healthy recovery.");
        Scratch.Clear();
    }
}
