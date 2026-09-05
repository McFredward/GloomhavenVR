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
    // Smooth "breathing" ring: a gentle sine on the amber portrait-frame alpha (unscaledTime so it
    // animates even while TimeManager is paused during selection camera moves). Because the glow is
    // now a THIN OUTLINE in the portrait's margin (InitiativeSelectionGlow) — not a flat wash over
    // the face the user couldn't see — it can breathe at a bold, clearly-visible alpha while the
    // hollow center keeps the portrait fully readable. The scale pulse is added ring-side in phase.
    private const float PulsePeriod = 1.5f;
    private const float MinAlpha = 0.40f;
    private const float MaxAlpha = 0.90f;

    /// <summary>
    /// The breathing alpha of the "still has to choose" ring at this instant — extracted from
    /// <see cref="Tick"/> so there is exactly ONE clock and ONE amplitude for this cue.
    ///
    /// <para>The second reader is <c>Net.RemoteInitiativeTrack</c>: a peer's MIRRORED track has to
    /// draw the ring for the characters THAT board's owner still owes cards for, and a copy that
    /// breathed on its own clock would read as two different cues on two surfaces of one table.
    /// Its period is <c>PulsePeriod</c>, i.e. the 1.5 s every attention cue in the mod shares
    /// (<c>Board.FocusCue.BlinkHz</c>), on <c>Time.unscaledTime</c> so it keeps animating while the
    /// game is time-paused during the selection camera move.</para>
    /// </summary>
    internal static float PulseAlpha
    {
        get
        {
            float t = (Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / PulsePeriod)) + 1f) * 0.5f;
            return Mathf.Lerp(MinAlpha, MaxAlpha, t);
        }
    }

    private static ConfigEntry<bool>? _enabled;

    private readonly List<CPlayerActor> _pending = new(8);

    /// <summary>The change gate, the heartbeat and the "why am I printing" clause for the split
    /// line — the shared three-gate throttle (redundancy survey R42, 2026-09-05). It used to be a
    /// bare <c>signature == _lastLoggedSignature</c> with no heartbeat, so once the split settled
    /// the line printed once and then read like a tick that had stopped, for a driver that runs
    /// EVERY FRAME of the card-selection phase. Uncapped: the phase's own end resets it.</summary>
    private readonly VRLogThrottle _splitLog = new(SplitHeartbeatSeconds);

    /// <summary>Seconds after which an UNCHANGED pending/done split prints again. The phase can
    /// stand for minutes while a player reads his hand, and this is what separates "nobody has
    /// committed yet" from "this driver stopped running".</summary>
    private const float SplitHeartbeatSeconds = 30f;

    /// <summary>[Optimize] LeanLogStrings: allocation-free change detector in front of the string
    /// signature (0 = never computed).</summary>
    private int _lastLoggedHash;

    /// <summary>
    /// The bound entry itself, for the ONE caller that needs the ConfigEntry rather than its value:
    /// <c>Net/Board/BoardTuning</c>, which samples the owner's dials onto extension record 28 and
    /// writes a field only when the live entry differs from the shipped default — so it needs the
    /// entry to ask, and null while unbound has to mean "write nothing".
    ///
    /// <para>WHY THIS TOGGLE TRAVELS AT ALL (wire id
    /// <see cref="Net.NetProtocol.TuneSelectionReadyOn"/>): the MIRRORED ring on a peer's board was
    /// gated only on the selection-state record, never on whether its owner wants the highlight
    /// drawn — so an owner who switched it off still had rings lit around their figure on every
    /// other screen. The unusual direction, and the same rule: a remote board is a picture of its
    /// OWNER's board, including what they chose not to see.</para></summary>
    internal static BepInEx.Configuration.ConfigEntry<bool>? EnabledEntry => _enabled;

    /// <summary>Bind the [SelectionReady] toggle (its own module config file). Idempotent.</summary>
    public static void Bind()
    {
        if (_enabled != null)
            return;
        ConfigFile config = ModuleConfig.Create("selectionready");
        _enabled = config.Bind(
            "SelectionReady", "Enabled", Defaults.SelectionReady_Enabled,
            "During the card-selection phase, pulse a soft highlight on the INITIATIVE ORDER BAR " +
            "entry of every character YOU control that has not yet chosen two cards or a long rest, " +
            "so it is clear on the initiative bar which characters still need selecting. Clears the " +
            "instant an actor commits and when the phase ends.");
    }

    /// <summary>Cached tick delegate ([Optimize] CacheTickDelegates — see BoardPing.Update).</summary>
    private System.Action? _tickCached;

    private void LateUpdate()
    {
        // [Optimize] CacheTickDelegates: reuse ONE Action instead of allocating a fresh one from
        // this instance method group every frame (gen0 pressure = head-turn hitches).
        TickGuard.Run("Board.SelectionReady", PerfConfig.CacheDelegates ? _tickCached ??= Tick : Tick);
    }

    private void OnDestroy()
    {
        InitiativeSelectionGlow.Reset();
    }

    private void Tick()
    {
        // Feature gate, phase gate, scenario gate — outside the selection phase (or with no live
        // scenario) hide every entry glow and stop.
        //
        // TEARDOWN GATE (hardware MP test 2026-08: 3370 identical NREs on the peer machine,
        // starting right after "Hands torn down"). When a scenario ends MID-SELECTION-PHASE
        // (host quits to the map / scenario aborts), the game never advances the phase:
        // PhaseManager's static s_CurrentPhase stays SelectAbilityCardsOrLongRest and the stale
        // ScenarioManager.Scenario object keeps its PlayerActors list — so both gates above still
        // PASS while the scene under them is being unloaded. IsCardSelectionReady then throws on
        // its very first statement, `CardsHandManager.Instance.GetHand(...)`
        // (CPlayerActorExtensions.cs:7 — the Instance singleton dies with the scenario scene),
        // once per LateUpdate, forever, until the next scenario resets the phase. Two extra gates
        // make the tick DORMANT (glow cleared, pending dropped) the moment the scenario is no
        // longer truly live:
        //  * Net.RevealGate.InScenario — the save's authoritative CurrentGameState == Scenario
        //    (goes false the instant the teardown flips the game state, see the peer log's
        //    "gameState=None" placement line between the last good tick and the first NRE), and
        //  * CardsHandManager.Instance != null — the exact object IsCardSelectionReady derefs,
        //    as a belt-and-braces floor for any frame gap around the state flip.
        if (_enabled is { Value: false }
            || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest
            || !Net.RevealGate.InScenario
            || CardsHandManager.Instance == null
            || ScenarioManager.Scenario?.PlayerActors == null)
        {
            InitiativeSelectionGlow.ClearAll();
            _pending.Clear();
            _splitLog.Reset();
            _lastLoggedHash = 0; // stale-signature reset: the next live phase re-logs its first split
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
        InitiativeSelectionGlow.Apply(_pending, PulseAlpha);

        LogIfChanged(players);
    }

    /// <summary>Emit one log line listing pending vs done local actors whenever the split changes.</summary>
    private void LogIfChanged(List<CPlayerActor> players)
    {
        // [Optimize] LeanLogStrings (2026-07 perf pass): this method runs EVERY FRAME for the whole
        // card-selection phase and used to allocate two StringBuilders plus three strings before it
        // ever reached the change gate — for a line that only prints when a player commits, i.e.
        // maybe a dozen times a scenario. The cheap integer signature below decides first; the
        // strings are built only once it says something actually changed. (A hash collision could
        // at worst swallow ONE diagnostic line; the highlight itself is driven by Tick, not by this
        // method, so nothing the player sees depends on it.)
        float now = Time.unscaledTime;
        if (Core.PerfConfig.LeanStrings)
        {
            int hash = 17;
            for (int i = 0; i < players.Count; i++)
            {
                CPlayerActor p = players[i];
                if (p == null || !p.IsUnderControlOrSingle())
                    continue;
                unchecked
                {
                    hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p);
                    hash = hash * 31 + (_pending.Contains(p) ? 1 : 0);
                }
            }
            // …AND THE HEARTBEAT OVERRIDES THE PREFILTER. A cheap change detector in front of a
            // change gate is correct for cost and wrong for the heartbeat: it would swallow the
            // very line the heartbeat exists to emit, and the silence would be back. HeartbeatDue
            // is a read-only probe, so on the overwhelmingly common frame (unchanged, no heartbeat
            // due) this still costs one compare and builds no strings.
            if (hash == _lastLoggedHash && !_splitLog.HeartbeatDue(now))
                return;
            _lastLoggedHash = hash;
        }

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
        if (!_splitLog.Wants(signature, now))
            return;
        VRLog.Info("Board",
            $"[SelectionReady] initiative-bar glow pending=[{(pendingNames.Length == 0 ? "-" : pendingNames.ToString())}] " +
            $"done=[{(doneNames.Length == 0 ? "-" : doneNames.ToString())}]" +
            $" ({_splitLog.Why})");
    }

    private static string NameOf(CPlayerActor actor)
    {
        string? id = actor.CharacterClass?.CharacterID;
        return string.IsNullOrEmpty(id) ? "actor" : id!;
    }
}
