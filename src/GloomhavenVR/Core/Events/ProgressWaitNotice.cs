using System;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Core.Events;

/// <summary>Explain a prolonged automatic native barrier without advancing it.
/// Ordinary player choices keep their existing specific prompts and permissions.</summary>
internal static class ProgressWaitNotice
{
    private static Choreographer? _owner;
    private static Choreographer.CWaitState? _wait;
    private static float _since;
    private static float _nextSample;
    private static bool _announced;
    private static string? _hintKey;

    /// <summary>Read the current automatic barrier for configurable owner-authored help.
    /// The independent bounded diagnostic clock below retains its twenty-second threshold.</summary>
    internal static string? ImmediateHintKey
    {
        get
        {
            Choreographer? owner = Choreographer.s_Choreographer;
            Choreographer.CWaitState? wait = owner != null ? owner.m_WaitState : null;
            return VRSession.IsRunning && owner != null && wait != null && Automatic(wait.m_State)
                ? HintKey(owner, wait) : null;
        }
    }

    private static string HintKey(Choreographer owner, Choreographer.CWaitState wait)
    {
        bool ready = wait.m_State == Choreographer.ChoreographerStateType.WaitingForEndAbilityAnimSync
            ? owner.m_EndAbilitySyncComplete
            : wait.m_State == Choreographer.ChoreographerStateType.WaitingForEndTurnSync
                ? owner.m_EndOfTurnSyncComplete : owner.m_EndOfRoundSyncComplete;
        return FFSNetwork.IsOnline && ready ? "progress_wait_players"
            : wait.m_State == Choreographer.ChoreographerStateType.WaitingForEndTurnSync
                ? "progress_finishing_turn"
                : wait.m_State == Choreographer.ChoreographerStateType.WaitingForEndRoundSync
                    ? "progress_finishing_round" : "progress_finishing_action";
    }

    internal static string? CurrentHint
    {
        get
        {
            Choreographer? owner = Choreographer.s_Choreographer;
            Choreographer.CWaitState? wait = owner != null ? owner.m_WaitState : null;
            if (!VRSession.IsRunning || owner == null || wait == null || !Automatic(wait.m_State))
            {
                Clear();
                return null;
            }
            float now = Time.unscaledTime;
            // The game's actual wait object is one barrier entry. Routine messages,
            // hover events and participant poses do not restart its observation clock.
            if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(wait, _wait) || now < _since)
            {
                Clear();
                _owner = owner; _wait = wait; _since = now;
            }
            if (now >= _nextSample)
            {
                _nextSample = now + .5f;
                if (now - _since >= 20f)
                {
                    bool online = FFSNetwork.IsOnline;
                    bool localReady = wait.m_State == Choreographer.ChoreographerStateType.WaitingForEndAbilityAnimSync
                        ? owner.m_EndAbilitySyncComplete
                        : wait.m_State == Choreographer.ChoreographerStateType.WaitingForEndTurnSync
                            ? owner.m_EndOfTurnSyncComplete : owner.m_EndOfRoundSyncComplete;
                    _hintKey = HintKey(owner, wait);
                    if (!_announced)
                    {
                        _announced = true;
                        VRLog.Alert("Progress", $"PROGRESSION WAIT: {now - _since:0.0}s in "
                            + $"{wait.m_State}, phase={PhaseManager.PhaseType}, online={online}, "
                            + $"localReady={localReady}, actor={wait.m_StateWaitActor?.ID ?? 0}. "
                            + "The original barrier remains authoritative; no action, choice or acknowledgment is synthesized.");
                    }
                }
            }
            return _hintKey == null ? null : Loc.Mod(_hintKey);
        }
    }

    private static bool Automatic(Choreographer.ChoreographerStateType state) =>
        state == Choreographer.ChoreographerStateType.WaitingForEndAbilityAnimSync
        || state == Choreographer.ChoreographerStateType.WaitingForEndTurnSync
        || state == Choreographer.ChoreographerStateType.WaitingForEndRoundSync;

    // Observe even when the native hand is temporarily absent during turn reset.
    // A diagnostic of an empty board must not require that board to own a hand.
    internal static void Tick() { _ = CurrentHint; }
    internal static void Reset() => Clear();

    private static void Clear()
    {
        if (_announced)
            VRLog.Info("Progress", $"PROGRESSION WAIT CLEARED after {Math.Max(0f, Time.unscaledTime - _since):0.0}s; "
                + "the previous automatic wait ended or its scenario was released.");
        _owner = null; _wait = null; _since = _nextSample = 0f;
        _announced = false; _hintKey = null;
    }
}
