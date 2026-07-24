using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// SELECTION-PHASE "who still has to choose" cue. During the card-selection phase
/// (<c>CPhase.PhaseType.SelectAbilityCardsOrLongRest</c>) every board figure THIS client
/// controls that has NOT yet finished its selection — i.e. has not placed two ability cards
/// and has not confirmed a long rest — gets the game's OWN actor spotlight, gently pulsed, so
/// the player can see at a glance which minis are still waiting on them. The moment an actor
/// commits (two cards / long rest) its glow clears; when the phase ends every glow clears.
///
/// NATIVE EFFECT (no new shader): the highlight is the game's own actor glow toggled through
/// <c>ActorBehaviour.SetHilighted(figure, bool)</c> — the exact object the game lights under the
/// active / initiative-selected figure (see <c>Choreographer.ClearHilightedActors</c> +
/// <c>InitiativeTrack.Select</c>). We drive that same boolean on a slow duty cycle
/// (<see cref="PulsePeriod"/>) so the pending minis "breathe" rather than sit as a static
/// spotlight, which reads as "still needs you" instead of "this one is selected". Because it is
/// the game's own effect it needs no bundle asset and matches the dungeon art exactly. (The
/// bundled additive amber overlay <c>FigureGrab.FigureHighlight</c> remains the fallback for the
/// grab-proximity cue; here we prefer the native glow per the feature request.)
///
/// AUTHORITATIVE "done" SIGNAL: <c>CPlayerActorExtensions.IsCardSelectionReady(actor)</c> — the
/// SAME per-actor test the game itself uses to decide whether the round-ready button may light
/// (<c>InitiativeTrack.IsCardSelectionReady</c>). It already folds in the two-cards /
/// long-rest / short-rest rules AND is internally gated to the local player, so a remote actor
/// always reports "ready" and is never revealed here.
///
/// MULTIPLAYER: only the LOCAL player's own pending actors pulse — enumeration is filtered by
/// <c>IsUnderControlOrSingle()</c> (offline every merc is mine; online only <c>IsUnderMyControl</c>),
/// exactly the guard used by <c>Net/RevealGate</c>. We never light a teammate's figure, so this
/// discloses nothing beyond what the vanilla client already shows.
///
/// Re-asserted in <c>LateUpdate</c> (after the game's own highlight writes, mirroring
/// <c>FigureGrab.FigureRingSuppressor</c>) and driven off <c>Time.unscaledTime</c> so the pulse
/// keeps breathing while the game is time-paused during a camera transition in selection. We only
/// ever turn OFF a glow we turned ON (tracked in <see cref="_owned"/>), so the game's own hover /
/// selection highlight on an already-done figure is never suppressed.
/// </summary>
internal sealed class SelectionReadyHighlighter : MonoBehaviour
{
    // Slow "breathing" duty cycle for the native glow: visible for most of a 1.5 s cycle, a short
    // dark gap, repeat — a soft pulse that reads as "pending", not a strobe. unscaledTime so it
    // animates even while TimeManager is paused during selection camera moves.
    private const float PulsePeriod = 1.5f;
    private const float PulseOnFraction = 0.72f;

    private static ConfigEntry<bool>? _enabled;

    // Actors we currently drive, mapped to the figure GameObject we light. Only figures in here
    // are ever turned OFF by us, so a done actor's game-owned highlight is left untouched.
    private readonly Dictionary<CPlayerActor, GameObject> _owned = new();
    private readonly List<CPlayerActor> _pending = new(8);
    private readonly List<CPlayerActor> _stale = new(8);

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
            "During the card-selection phase, pulse the game's native actor highlight under every " +
            "figure YOU control that has not yet chosen two cards or a long rest, so it is clear " +
            "which characters still need selecting. Clears the instant an actor commits and when " +
            "the phase ends.");
    }

    private void LateUpdate()
    {
        TickGuard.Run("Board.SelectionReady", Tick);
    }

    private void OnDestroy()
    {
        ClearOwned();
    }

    private void Tick()
    {
        // Feature gate, phase gate, scenario gate — outside the selection phase (or with no live
        // scenario / choreographer) clear everything we lit and restore the game's control.
        if (_enabled is { Value: false }
            || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest
            || Choreographer.s_Choreographer == null
            || ScenarioManager.Scenario?.PlayerActors == null)
        {
            if (_owned.Count > 0)
                ClearOwned();
            _lastLoggedSignature = string.Empty;
            return;
        }

        // Collect the LOCAL player's still-pending actors (initiative/display order = PlayerActors
        // order, which is stable across the phase). IsCardSelectionReady is the game's own per-actor
        // "committed two cards / long rest" test and is itself local-only, so remote actors drop out.
        _pending.Clear();
        List<CPlayerActor> players = ScenarioManager.Scenario.PlayerActors;
        for (int i = 0; i < players.Count; i++)
        {
            CPlayerActor player = players[i];
            if (player != null && player.IsUnderControlOrSingle() && !player.IsCardSelectionReady())
                _pending.Add(player);
        }

        // Drop actors we owned that are no longer pending (committed, exhausted, or gone) — force
        // their glow OFF exactly once and hand control back to the game.
        _stale.Clear();
        foreach (KeyValuePair<CPlayerActor, GameObject> kv in _owned)
        {
            if (!_pending.Contains(kv.Key))
                _stale.Add(kv.Key);
        }
        for (int i = 0; i < _stale.Count; i++)
        {
            CPlayerActor actor = _stale[i];
            if (_owned.TryGetValue(actor, out GameObject figure) && figure != null)
                ActorBehaviour.SetHilighted(figure, hilight: false);
            _owned.Remove(actor);
        }

        // Pulse every pending actor's figure with the game's native highlight.
        bool pulseOn = Time.unscaledTime % PulsePeriod < PulsePeriod * PulseOnFraction;
        for (int i = 0; i < _pending.Count; i++)
        {
            CPlayerActor actor = _pending[i];
            GameObject figure = Choreographer.s_Choreographer.FindClientActorGameObject(actor);
            if (figure == null)
            {
                // Figure not spawned yet — keep the actor pending but light nothing this frame.
                _owned.Remove(actor);
                continue;
            }
            _owned[actor] = figure;
            ActorBehaviour.SetHilighted(figure, pulseOn);
        }

        LogIfChanged(players);
    }

    /// <summary>Force every glow we drove OFF and forget them (phase exit / shutdown).</summary>
    private void ClearOwned()
    {
        foreach (GameObject figure in _owned.Values)
        {
            if (figure != null)
                ActorBehaviour.SetHilighted(figure, hilight: false);
        }
        _owned.Clear();
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
            $"[SelectionReady] pending=[{(pendingNames.Length == 0 ? "-" : pendingNames.ToString())}] " +
            $"done=[{(doneNames.Length == 0 ? "-" : doneNames.ToString())}]");
    }

    private static string NameOf(CPlayerActor actor)
    {
        string? id = actor.CharacterClass?.CharacterID;
        return string.IsNullOrEmpty(id) ? "actor" : id!;
    }
}
