using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.Surfaces;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// SELECTION-PHASE "who still has to choose" cue. During the card-selection phase
/// (<c>CPhase.PhaseType.SelectAbilityCardsOrLongRest</c>) every actor THIS client controls that
/// has NOT yet finished its selection — i.e. has not placed two ability cards and has not
/// confirmed a long rest — gets its INITIATIVE ORDER BAR entry gently pulsed, so a glance at the
/// initiative bar shows which characters are still waiting on the player. The moment an actor
/// commits (two cards / long rest) its entry glow clears; when the phase ends every glow clears.
///
/// WHERE THE HIGHLIGHT LIVES: on the initiative bar, NOT on the board figures. This driver only
/// DETECTS the pending set (below) and hands it to <see cref="InitiativeSelectionGlow"/>, which
/// owns the initiative-entry rendering — it resolves each actor's own
/// <c>InitiativeTrackActorBehaviour</c> via the game's public
/// <c>InitiativeTrack.FindInitiativeTrackActor</c> and pulses a non-interactive amber overlay over
/// it. (This replaces the earlier board-mini glow through <c>ActorBehaviour.SetHilighted</c>,
/// which the player did not want; that path is gone.)
///
/// AUTHORITATIVE "done" SIGNAL: <c>CPlayerActorExtensions.IsCardSelectionReady(actor)</c> — the
/// SAME per-actor test the game itself uses to decide whether the round-ready button may light
/// (<c>InitiativeTrack.IsCardSelectionReady</c>). It already folds in the two-cards /
/// long-rest / short-rest rules AND is internally gated to the local player, so a remote actor
/// always reports "ready" and is never revealed here.
///
/// MULTIPLAYER: only the LOCAL player's own pending actors pulse — enumeration is filtered by
/// <c>IsUnderControlOrSingle()</c> (offline every merc is mine; online only <c>IsUnderMyControl</c>),
/// exactly the guard used by <c>Net/RevealGate</c>. We never light a teammate's entry, so this
/// discloses nothing beyond what the vanilla initiative track already shows (remote un-locked-in
/// players already render as "?").
///
/// Driven in <c>LateUpdate</c> (after the game's own initiative writes) off <c>Time.unscaledTime</c>
/// so the pulse keeps breathing while the game is time-paused during a camera transition in
/// selection. The pending set is re-derived every tick, so an entry clears the instant its actor
/// commits and the whole set clears when the phase ends.
/// </summary>
internal sealed class SelectionReadyHighlighter : MonoBehaviour
{
    // Smooth "breathing" glow: a low, gentle sine on the overlay alpha (unscaledTime so it animates
    // even while TimeManager is paused during selection camera moves). Subtle enough to keep the
    // portrait readable — a "still waiting" tint, not a strobe.
    private const float PulsePeriod = 1.5f;
    private const float MinAlpha = 0.10f;
    private const float MaxAlpha = 0.34f;

    private static ConfigEntry<bool>? _enabled;

    private readonly List<CPlayerActor> _pending = new(8);

    // Last logged pending signature, so we only log when the pending/done split actually changes.
    private string _lastLoggedSignature = string.Empty;

    /// <summary>Bind the [SelectionReady] toggle (its own module config file). Idempotent.</summary>
    public static void Bind()
    {
        if (_enabled != null)
            return;
        ConfigFile config = ModuleConfig.Create("selectionready");
        _enabled = config.Bind(
            "SelectionReady", "Enabled", true,
            "During the card-selection phase, pulse a soft highlight on the INITIATIVE ORDER BAR " +
            "entry of every character YOU control that has not yet chosen two cards or a long rest, " +
            "so it is clear on the initiative bar which characters still need selecting. Clears the " +
            "instant an actor commits and when the phase ends.");
    }

    private void LateUpdate()
    {
        TickGuard.Run("Board.SelectionReady", Tick);
    }

    private void OnDestroy()
    {
        InitiativeSelectionGlow.Reset();
    }

    private void Tick()
    {
        // Feature gate, phase gate, scenario gate — outside the selection phase (or with no live
        // scenario) hide every entry glow and stop.
        if (_enabled is { Value: false }
            || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest
            || ScenarioManager.Scenario?.PlayerActors == null)
        {
            InitiativeSelectionGlow.ClearAll();
            _pending.Clear();
            _lastLoggedSignature = string.Empty;
            return;
        }

        // Collect the LOCAL player's still-pending actors. IsCardSelectionReady is the game's own
        // per-actor "committed two cards / long rest" test and is itself local-only, so remote
        // actors drop out; IsUnderControlOrSingle is the belt-and-braces MP ownership guard.
        _pending.Clear();
        List<CPlayerActor> players = ScenarioManager.Scenario.PlayerActors;
        for (int i = 0; i < players.Count; i++)
        {
            CPlayerActor player = players[i];
            if (player != null && player.IsUnderControlOrSingle() && !player.IsCardSelectionReady())
                _pending.Add(player);
        }

        // Highlight the pending actors' initiative-bar entries, clearing any that just committed.
        float t = (Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / PulsePeriod)) + 1f) * 0.5f;
        float alpha = Mathf.Lerp(MinAlpha, MaxAlpha, t);
        InitiativeSelectionGlow.Apply(_pending, alpha);

        LogIfChanged(players);
    }

    /// <summary>Emit one log line listing pending vs done local actors whenever the split changes.</summary>
    private void LogIfChanged(List<CPlayerActor> players)
    {
        var pendingNames = new StringBuilder();
        var doneNames = new StringBuilder();
        for (int i = 0; i < players.Count; i++)
        {
            CPlayerActor player = players[i];
            if (player == null || !player.IsUnderControlOrSingle())
                continue;
            StringBuilder bucket = _pending.Contains(player) ? pendingNames : doneNames;
            if (bucket.Length > 0)
                bucket.Append(", ");
            bucket.Append(NameOf(player));
        }

        string signature = pendingNames + "|" + doneNames;
        if (signature == _lastLoggedSignature)
            return;
        _lastLoggedSignature = signature;
        VRLog.Info("Board",
            $"[SelectionReady] initiative-bar glow pending=[{(pendingNames.Length == 0 ? "-" : pendingNames.ToString())}] " +
            $"done=[{(doneNames.Length == 0 ? "-" : doneNames.ToString())}]");
    }

    private static string NameOf(CPlayerActor actor)
    {
        string? id = actor.CharacterClass?.CharacterID;
        return string.IsNullOrEmpty(id) ? "actor" : id!;
    }
}
