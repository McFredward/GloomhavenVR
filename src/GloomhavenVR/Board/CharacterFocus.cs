using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Board;

/// <summary>
/// How a player's control board / avatar is marked with respect to the character that is
/// currently AT TURN. One enum, three states — the whole visual vocabulary of the feature.
/// </summary>
internal enum FocusTurnMark
{
    /// <summary>No mark: this player does not own the character whose turn it is (or nobody is).</summary>
    None,

    /// <summary>GREEN: this player owns the character at turn AND is looking at that character.</summary>
    AtTurnCorrect,

    /// <summary>RED: this player owns the character at turn but is looking at a DIFFERENT one.</summary>
    AtTurnWrong,
}

/// <summary>
/// FREE CHARACTER FOCUS — "which character am I looking at right now", as a first-class,
/// multiplayer-synchronised concept, plus the turn state that makes it legible at the table.
///
/// <para>THE PROBLEM. Vanilla couples "the character I am looking at" to "the character I am
/// playing": the initiative track is only <c>playersSelectable</c> during card selection
/// (Choreographer.cs:12539/12601 pass <c>true</c>; the action-phase update at :12548 passes
/// <c>false</c>), and a portrait click switches the game's presented hand
/// (<c>InitiativeTrackPlayerAvatar.OnClick</c> → <c>InitiativeTrack.Select</c> →
/// <c>CardsHandManager.SwitchHand</c>). In VR, where the party sits around one table, players
/// want to LOOK at a teammate's hand, piles and played cards while that teammate acts — which
/// vanilla has no concept of, and which the mod previously had to REFUSE outright
/// (<c>Board/Patches/SelectionGuardPatches.cs</c> rejected the click, because letting the game
/// select a non-current actor during the action phase deadlocks the action board:
/// <c>FullAbilityCard.cs:635</c> silently drops every half-click whose owner is not
/// <c>Choreographer.CurrentActor</c>).</para>
///
/// <para>THE SHAPE OF THE ANSWER. Focus is a MOD-LOCAL presentation concept layered ON TOP of
/// the game's own selection, never a replacement for it. The game's presented hand
/// (<c>CardsDriver.CurrentHand()</c>) stays exactly what vanilla made it; this class only says
/// "…but render THAT character instead", and it says so only in a strictly READ-ONLY form.
/// Nothing here writes a single byte of game state: no <c>InitiativeTrack.Select</c>, no
/// <c>CardsHandManager.SwitchHand</c>, no rules call. That is the structural read-only guarantee
/// — the game's own ownership guards are not merely "still in force", they are the ONLY thing
/// that can ever act, because the focus path has no mutator to reach for.</para>
///
/// <para>THE ONE GATE (user ruling 2026-08-08: "Ich will nie wieder eine Blockierung haben, den
/// Character zu wechseln — dafür ist ja nun die Anzeige, ob er dran ist oder nicht"). Focus is
/// refused in EXACTLY one situation: the secret card-selection window
/// (<c>PhaseManager.PhaseType == SelectAbilityCardsOrLongRest</c>,
/// <see cref="RevealGate.IsSecretSelectionPhase"/>), because that is the only moment the game's
/// model holds a genuine secret — which two cards a remote player has CHOSEN this round
/// (<c>AbilityCardUI.cs:980/1024/1100/1188</c>). Nothing else may ever refuse a switch, and in
/// particular NO PHASE LIST does any more.</para>
///
/// <para>WHY THE PHASE WHITELIST HAD TO GO (read from the game's own source). The gate used to
/// enumerate the TURN phases <c>StartTurn, ActionSelection, Action, EndTurn, EndTurnLoot</c> and
/// refuse everything else. But the game parks a PENDING DECISION in whatever phase raised it, and
/// several of those are outside that list — <c>CPhase.PhaseType.CheckForForgoActionActiveBonuses</c>
/// is a phase of its very own that the game SITS IN until the player answers a "forgo your action
/// for this active bonus" prompt (<c>GameState.cs:1842</c> enters it for every actor,
/// <c>GameState.cs:2002-2022</c> either shows the bar and waits or passes straight to
/// <c>StartTurn</c>; <c>CPhaseCheckForForgoActionActiveBonuses.cs</c>), and
/// <c>CheckForInitiativeAdjustments</c>, <c>EndRound</c>, <c>StartRoundEffects</c>,
/// <c>PlayerExhausted</c>, <c>Autosave</c>, <c>MonsterClassesSelectAbilityCards</c> and
/// <c>None</c> are all reachable while the player is simply looking at the table. Every one of
/// them refused a switch. That is the reported bug ("Während der Character wegen den
/// Flitzstiefeln eine Entscheidung treffen muss, ist das Wechseln blockiert"), and enumerating
/// MORE phases would only move the wall. The gate no longer reads the phase at all except to
/// recognise the one secret window, so there is no state a pending decision can put the game in
/// that the gate inspects — a decision cannot close it, in any phase, present or future.</para>
///
/// <para>AND A SWITCH CANNOT DISTURB A DECISION EITHER — structurally, not by care.
/// <see cref="TryFocus"/> writes exactly one mod-local field (<see cref="_focused"/>) and asks the
/// card driver for a rebuild. It calls no rules API: no <c>InitiativeTrack.Select</c>, no
/// <c>CardsHandManager.SwitchHand</c>, no <c>PhaseManager</c>, no
/// <c>ScenarioRuleClient.MessageHandler</c>, no <c>CPhaseAction</c>. There is no mutator ON the
/// path to reach for. The click seam
/// (<c>Board/Patches/SelectionGuardPatches.cs</c>) additionally SUPPRESSES vanilla's entire
/// <c>OnClick</c> whenever a focus is taken, so not one of its side effects (Select → SwitchHand →
/// ClearHilightedActors → SetHilighted → CameraController.SmartFocus → ToggleViewAllCards) can
/// fire while a prompt is open. And the prompt itself is a SEPARATE surface: the game's decision
/// window, docked by <c>WorldUI.Surfaces.DecisionDockSurface</c> on <c>PlayTray.DecisionMount</c>
/// — a fixture of the BOARD, not of the focused character's presentation. Focus only chooses which
/// <c>CardsHandUI</c> the card fan renders, so the decision row stays exactly where it was, keeps
/// its own live widgets, and is still answerable while the player looks somewhere else.</para>
///
/// <para>Belt AND braces with <see cref="RevealGate"/>, which independently keeps a foreign
/// character's ROUND cards face DOWN through the secret window.</para>
///
/// <para>WHAT IS AND IS NOT A DISCLOSURE. A foreign character's HAND pile is not secret in
/// Gloomhaven: <c>CCharacterClass.HandAbilityCards</c> is host-replicated to every client and
/// carries no visibility gate whatsoever (<c>CCharacterClass.cs:91</c>), and the game itself
/// instantiates a fully populated <c>CardsHandUI</c> per player actor on EVERY client
/// (<c>Choreographer.cs:925/1112</c> → <c>CardsHandManager.AddPlayer</c>, no ownership check) —
/// the very fact <c>RemoteAbilityCardSource</c> is built on. The ONLY secret in the game's model
/// is which two cards a remote player has CHOSEN this round, and it is secret only during
/// <c>SelectAbilityCardsOrLongRest</c> (<c>AbilityCardUI.cs:980/1024/1100/1188</c>) — exactly the
/// window this class refuses to open. So a focused view renders from the replicated model the
/// local client already holds; NO card identity is added to our wire, here or anywhere.</para>
///
/// <para>WHAT DOES RIDE THE WIRE: one record, <c>NetProtocol.ExtIdCharFocus</c> (22) — the
/// stable id of the focused character plus one bit, "the character at turn is mine". Both are
/// facts a receiver genuinely cannot derive: <c>CActor.IsUnderMyControl</c> is a LOCAL flag
/// (false on every other machine), and the focus itself is a VR presentation choice the game
/// model knows nothing about. Everything else the outlines need — who is at turn — every client
/// reads for itself from the replicated <c>Choreographer.CurrentActor</c>.</para>
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY — costs wire bytes (extras extension record 22, 5 B, only
/// while a focus is known). Names a CHARACTER and a boolean; never a card. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class CharacterFocus
{
    /// <summary>The character the local player has focused, or null = "follow the game" (the
    /// default, and the only state before the first focus click of a scenario).</summary>
    private static CPlayerActor? _focused;

    /// <summary>True while <see cref="ResolveHand"/> last returned an OVERRIDE — i.e. the mod is
    /// presenting a character the game itself is NOT presenting. Every such view is read-only;
    /// see the class doc. Recomputed every rebuild, so it can never latch stale.</summary>
    private static bool _readOnlyView;

    /// <summary>Change-log guard: the last focus we logged, so a per-frame resolve is silent.</summary>
    private static int _loggedFocusId;

    /// <summary>Peer focus state, keyed by transport player id (wire record 22). Written by
    /// <see cref="ApplyPeer"/> from the extras apply pass, read by the remote outlines.</summary>
    private static readonly Dictionary<int, PeerFocus> Peers = new(4);

    /// <summary>One peer's synced focus. <see cref="OwnsTurn"/> is the wire bit; the mark is
    /// derived locally so it always agrees with THIS client's read of who is at turn.</summary>
    internal readonly struct PeerFocus
    {
        internal readonly int ActorId;
        internal readonly bool OwnsTurn;

        internal PeerFocus(int actorId, bool ownsTurn)
        {
            ActorId = actorId;
            OwnsTurn = ownsTurn;
        }
    }

    // ----------------------------------------------------------------------------- THE gate --

    /// <summary>
    /// True while free character focus is allowed at all. Exactly two clauses, and by user ruling
    /// there will never be a third: a live scenario to focus IN, and NOT the secret card-selection
    /// window. <see cref="RevealGate.IsSecretSelectionPhase"/> is the anti-cheat linchpin and is
    /// the ONE refusal this feature is allowed to have (class doc, "THE ONE GATE").
    ///
    /// <para>Deliberately NOT here, and never again: a phase whitelist, a wait-state test, a
    /// "is a decision pending" test, an "is a modal open" test, an ownership test. A pending
    /// decision parks the game in an arbitrary phase and an arbitrary
    /// <c>Choreographer.m_WaitState</c>; this property reads neither, so none of them can shut
    /// it. Use <see cref="Refusal"/> when you need to TELL the player (or the log) why.</para>
    /// </summary>
    internal static bool Open => Refusal(out _);

    /// <summary>
    /// The gate as a REASON rather than a bool: returns true when focus is allowed and
    /// <paramref name="why"/> is null, false with a short, log-safe explanation otherwise.
    /// Every refusal in the whole feature is minted here and nowhere else, so
    /// "<c>[Board] [Focus] switch REFUSED — …</c>" can never carry a reason this method does not
    /// know about — and after the 2026-08-08 ruling the only reason it can ever mint is the
    /// card-selection phase.
    /// </summary>
    /// <summary>The ONE refusal reason the feature is allowed to have, short enough to read in a
    /// clear/refuse line. The rationale is spelled out once in <see cref="SecretWindowDetail"/>
    /// so the refusal line can carry it without every other line repeating it.</summary>
    private const string SecretWindowReason =
        "card-selection phase (SelectAbilityCardsOrLongRest)";

    /// <summary>Why <see cref="SecretWindowReason"/> is the one gate that stays.</summary>
    private const string SecretWindowDetail =
        "which two cards a player has CHOSEN this round is the game's only real secret " +
        "(AbilityCardUI.cs:980/1024/1100/1188), so no foreign view may open until the reveal. " +
        "This is the feature's ONLY refusal and it lifts by itself the moment the cards are " +
        "revealed — nothing else, and no pending decision in any phase, can refuse a switch.";

    internal static bool Refusal(out string? why)
    {
        if (!CardsGameApi.InScenario)
        {
            // Not a refusal the player can ever see: no scenario means no initiative track and
            // therefore no portrait to click. Named anyway so a log line is never a mystery.
            why = "no live scenario (Choreographer is gone) — nothing to look at";
            return false;
        }
        if (RevealGate.IsSecretSelectionPhase)
        {
            why = SecretWindowReason;
            return false;
        }
        why = null;
        return true;
    }

    // ------------------------------------------------------------------------------- turn state --

    /// <summary>
    /// The character whose turn it is, or null. Reads the client-side authority
    /// <c>Choreographer.CurrentPlayerActor</c> (Choreographer.cs:474 — it already resolves a hero
    /// SUMMON to its summoner, which is what "whose board lights up" means), so a summon's turn
    /// marks its owner's board rather than nobody's.
    /// </summary>
    internal static CPlayerActor? TurnActor
    {
        get
        {
            Choreographer c = Choreographer.s_Choreographer;
            return c != null ? c.CurrentPlayerActor : null;
        }
    }

    /// <summary>Stable wire id of <see cref="TurnActor"/> (0 = nobody / no scenario).</summary>
    internal static int TurnActorId => NetFigures.StableActorId(TurnActor);

    /// <summary>True when the character at turn is one the LOCAL client controls. Offline every
    /// merc is ours, so this is simply "somebody is at turn" there.</summary>
    internal static bool LocalOwnsTurn
    {
        get
        {
            CPlayerActor? turn = TurnActor;
            return turn != null && (!FFSNetwork.IsOnline || turn.IsUnderMyControl);
        }
    }

    /// <summary>
    /// The LOCAL player's turn mark — the colour their own control board and their own Steam
    /// avatar wear. Green while they own the character at turn AND are looking at it; red while
    /// they own it but are looking at somebody else (the user's "wrong character" warning); none
    /// otherwise.
    /// </summary>
    internal static FocusTurnMark LocalMark
    {
        get
        {
            if (!LocalOwnsTurn)
                return FocusTurnMark.None;
            // NO OVERRIDE ⇒ we are looking at whatever the game presents, and during our OWN turn
            // that IS the acting character (the game selects it and switches the hand to it). So
            // "correct" is the state by construction, and it does not depend on a card rebuild
            // having run this frame — which matters, because Rebuild is edge-driven and the mark
            // is read every frame.
            if (_focused == null)
                return FocusTurnMark.AtTurnCorrect;
            return ReferenceEquals(_focused, TurnActor)
                ? FocusTurnMark.AtTurnCorrect
                : FocusTurnMark.AtTurnWrong;
        }
    }

    /// <summary>
    /// A PEER's turn mark, derived from their synced record and THIS client's own read of who is
    /// at turn — so the two machines can never disagree about the turn itself, only about the
    /// focus (which is the one thing the wire actually carries). Unknown peer / no record /
    /// they do not own the turn ⇒ <see cref="FocusTurnMark.None"/>.
    /// </summary>
    /// <summary>
    /// The stable actor id of the character a PEER is looking at (0 = unknown / no record). The
    /// companion of <see cref="MarkForPeer"/>: the mark says whether their focus is the RIGHT one,
    /// this says WHICH one, so their mirrored initiative track can ring the same entry the local
    /// track rings for the local player.
    /// </summary>
    internal static int FocusIdForPeer(int playerId) =>
        Peers.TryGetValue(playerId, out PeerFocus peer) ? peer.ActorId : 0;

    internal static FocusTurnMark MarkForPeer(int playerId)
    {
        if (!Peers.TryGetValue(playerId, out PeerFocus peer) || !peer.OwnsTurn)
            return FocusTurnMark.None;
        int turnId = TurnActorId;
        if (turnId == 0)
            return FocusTurnMark.None;
        return peer.ActorId == turnId ? FocusTurnMark.AtTurnCorrect : FocusTurnMark.AtTurnWrong;
    }

    // ------------------------------------------------------------------------------ local focus --

    /// <summary>The character the mod is actually PRESENTING right now (focus override when one
    /// is live, otherwise whatever the game presents). Null before the first rebuild of a
    /// scenario, or while no hand exists at all.</summary>
    internal static CPlayerActor? PresentedActor { get; private set; }

    /// <summary>Stable wire id of <see cref="PresentedActor"/> — what record 22 carries.</summary>
    internal static int PresentedActorId => NetFigures.StableActorId(PresentedActor);

    /// <summary>True while the presented character is NOT the one the game itself presents, i.e.
    /// the view is a focus OVERRIDE and therefore strictly read-only (class doc). Callers use
    /// this to strip every interaction affordance off what they build.</summary>
    internal static bool ReadOnlyView => _readOnlyView;

    /// <summary>The focused character, or null while following the game.</summary>
    internal static CPlayerActor? Focused => _focused;

    /// <summary>
    /// Is <paramref name="actor"/> a thing one can LOOK AT at all — a player character that is
    /// still in the scenario? Enemies, objects and exhausted heroes are not focus TARGETS (an
    /// exhausted hero leaves the initiative track and has no board presence left); that is a
    /// different statement from "the switch was refused", and the two are logged differently on
    /// purpose so <c>switch REFUSED</c> only ever means a gate said no.
    /// </summary>
    internal static bool IsFocusTarget(CActor? actor) =>
        actor is CPlayerActor player && !player.IsDead;

    /// <summary>
    /// May <paramref name="actor"/> be focused right now? A live player character plus
    /// <see cref="Open"/>. Deliberately says nothing about OWNERSHIP: focusing a teammate is the
    /// whole feature, and focusing one of your OWN characters that is not at turn is the state
    /// the red warning exists for. Deliberately says nothing about the hand WIDGET either — see
    /// <see cref="TryFocus"/>.
    /// </summary>
    internal static bool CanFocus(CActor? actor) => IsFocusTarget(actor) && Open;

    /// <summary>
    /// Focus <paramref name="actor"/>. Returns false — and changes nothing — only when the actor
    /// is not a focus target at all, or when <see cref="Refusal"/> refuses (card selection).
    /// Purely local: no game state is written, no rules call is made, no packet is sent from here
    /// (the sender samples <see cref="PresentedActorId"/> on its own cadence). That is what makes
    /// a switch harmless to a pending decision — there is no mutator on this path.
    ///
    /// <para>NOTE the check that is NOT here any more: "a <c>CardsHandUI</c> for this character
    /// exists on this client". It used to refuse the switch, which meant a click that landed in
    /// the one frame between a hand teardown and its rebuild was simply EATEN. The focus is now
    /// taken regardless and <see cref="ResolveHand"/> falls back to the game's own hand until the
    /// widget appears (it already had exactly that fallback) — a late hand delays the view by a
    /// frame instead of losing the click.</para>
    /// </summary>
    internal static bool TryFocus(CActor? actor)
    {
        if (actor is not CPlayerActor player)
            return false; // enemy / object portrait — never was a character view request

        if (player.IsDead)
        {
            // Not a refusal: there is no character left to present. Logged plainly, never as
            // "REFUSED", so the refusal line keeps its single meaning.
            VRLog.Info("Board", $"[Focus] portrait click ignored — '{Describe(player)}' is " +
                                "exhausted; an exhausted character has no hand, no turn and no " +
                                "board presence left to look at.");
            return false;
        }

        if (!Refusal(out string? why))
        {
            LogRefusal(player, why!);
            return false;
        }

        if (ReferenceEquals(_focused, player))
            return true; // already focused — idempotent, and never logs twice
        _focused = player;
        // The game raises no event for a mod-side focus, so the card board must be told.
        Cards.CardsDriver.RequestRebuild();
        VRLog.Info("Board", $"[Focus] now looking at '{Describe(player)}'" +
                            $"{(IsForeign(player) ? " (another player's character — read-only view)" : "")}" +
                            $"; phase {PhaseManager.PhaseType}, at turn '{Describe(TurnActor)}'. " +
                            "Nothing in the game was written: any prompt that was open is still " +
                            "open, still docked where it was, and still answerable.");
        return true;
    }

    /// <summary>Last refused (reason, actor) pair and when — so a laser held on a portrait during
    /// card selection logs once, not sixty times a second, while a NEW refusal is always loud.</summary>
    private static string? _lastRefusal;

    private static int _lastRefusedActorId;

    private static float _lastRefusalTime = float.NegativeInfinity;

    /// <summary>Re-log an unchanged refusal at most this often (seconds, unscaled).</summary>
    private const float RefusalLogIntervalSeconds = 5f;

    /// <summary>
    /// THE refusal line. Format is fixed by the 2026-08-08 ruling —
    /// <c>[Board] [Focus] switch REFUSED — &lt;reason&gt;</c> — so "blocked again" is never a
    /// guess: grep the log for <c>switch REFUSED</c> and the reason is right there. After this
    /// build the only reason that can ever appear is the card-selection phase; anything else in
    /// this position is a regression, not a design decision.
    /// </summary>
    private static void LogRefusal(CPlayerActor player, string why)
    {
        int id = NetFigures.StableActorId(player);
        float now = UnityEngine.Time.unscaledTime;
        if (why == _lastRefusal && id == _lastRefusedActorId
            && now - _lastRefusalTime < RefusalLogIntervalSeconds)
            return;
        _lastRefusal = why;
        _lastRefusedActorId = id;
        _lastRefusalTime = now;
        VRLog.Info("Board", $"[Focus] switch REFUSED — {why} (wanted '{Describe(player)}', " +
                            $"phase {PhaseManager.PhaseType})." +
                            (why == SecretWindowReason ? " " + SecretWindowDetail : ""));
    }

    /// <summary>Drop the focus and follow the game again (turn hand-off, phase exit, teardown).</summary>
    internal static void Clear(string reason)
    {
        if (_focused == null)
            return;
        VRLog.Info("Board", $"[Focus] cleared ({reason}) — following the game's own selection again.");
        _focused = null;
        _readOnlyView = false;
        Cards.CardsDriver.RequestRebuild();
    }

    /// <summary>True when <paramref name="actor"/> belongs to another player (online only —
    /// offline every merc is ours).</summary>
    internal static bool IsForeign(CPlayerActor? actor) =>
        actor != null && FFSNetwork.IsOnline && !actor.IsUnderMyControl;

    // ----------------------------------------------------------------------------- resolution --

    /// <summary>
    /// THE ONE SEAM into the card pipeline. Given the hand the game presents (which is already
    /// ownership-gated by <c>CardsGameApi.IsLocalHand</c>, so it is null for a foreign
    /// character), return the hand the mod should RENDER, and latch
    /// <see cref="ReadOnlyView"/>/<see cref="PresentedActor"/> for everything downstream.
    ///
    /// <para>Three outcomes, in order:</para>
    /// <list type="number">
    /// <item>no focus, or the gate is shut ⇒ <paramref name="gameHand"/> unchanged — vanilla
    ///   behaviour, fully interactive;</item>
    /// <item>the focus IS what the game presents ⇒ <paramref name="gameHand"/> unchanged, and the
    ///   focus is dropped: there is nothing to override, so we stop overriding. This is how a
    ///   player returns to interactive play — focus the character whose turn it is;</item>
    /// <item>otherwise ⇒ the focused character's hand, with <see cref="ReadOnlyView"/> set. Note
    ///   this branch is reached for an OWN character too (one of yours that is not at turn): the
    ///   game refuses to act on it during the action phase anyway (FullAbilityCard.cs:635), so
    ///   presenting it interactively would only rebuild the deadlock the mod already guards
    ///   against. Read-only is the correct — and the safe — answer for every override.</item>
    /// </list>
    /// </summary>
    internal static CardsHandUI? ResolveHand(CardsHandUI? gameHand)
    {
        PresentedActor = gameHand != null ? gameHand.PlayerActor : null;
        _readOnlyView = false;

        if (_focused == null)
            return gameHand;

        if (!Refusal(out string? why))
        {
            // The ONE way a live focus ends without the player asking: the round's card selection
            // opened (or the scenario went away). Named with the gate's own reason so the log
            // agrees with the refusal line a click would have produced.
            Clear(why!);
            return gameHand;
        }
        if (_focused.IsDead)
        {
            Clear("focused character is exhausted");
            return gameHand;
        }
        if (gameHand != null && ReferenceEquals(gameHand.PlayerActor, _focused))
        {
            // The game caught up with us (its own turn select landed on the focused character).
            // Stop overriding so the view becomes interactive again — this is the documented way
            // back to normal play, and it must not need a second click.
            Clear("the game now presents the focused character");
            return gameHand;
        }

        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI? focusHand = manager != null ? manager.GetHand(_focused) : null;
        if (focusHand == null)
        {
            // Mid-rebuild / mid-teardown: fall back to the game's hand rather than an empty
            // board. The focus survives — the next frame usually resolves it.
            return gameHand;
        }

        _readOnlyView = true;
        PresentedActor = focusHand.PlayerActor;
        LogFocusOnce();
        return focusHand;
    }

    /// <summary>
    /// Does the mod's ROUND-CARD dock apply to <paramref name="hand"/> — i.e. may the board's two
    /// card slots show this character's CHOSEN cards — and if not, WHY not?
    ///
    /// <para>VANILLA PATH (no focus override) is unchanged: <c>CardsGameApi.IsActionTurn</c>, which
    /// is "this hand's locally-controlled player is the one <c>Choreographer.CurrentActor</c> is on
    /// right now". That is the state the interactive half-play affordance belongs to and it stays
    /// keyed to the turn, exactly as before.</para>
    ///
    /// <para>FOCUS PATH (user 2026-08-08: "die Kartenslots sind leer — ich will AUCH, dass dort dann
    /// immer die jeweiligen ausgewählten Karten liegen"). The turn key was WRONG here, and that was
    /// the whole bug: a player focuses a teammate precisely because that teammate is NOT acting, so
    /// <c>TurnActor == hand.PlayerActor</c> was false for every switch the feature exists for and
    /// the dock never filled. There is no data gap behind it —
    /// <c>CCharacterClass.RoundAbilityCards</c> is per-character, host-replicated and populated from
    /// the moment the round's cards are committed until the game DISCARDS them at the end of that
    /// character's own turn (<c>GameState.cs:2286</c> → <c>CCharacterClass.DiscardRoundAbilityCards</c>,
    /// <c>CCharacterClass.cs:505</c>). It is the very list the remote control board already renders a
    /// peer's played cards from (<c>Net.RemoteControlBoard.OrderRoundCards</c>), so this adds no
    /// channel and no wire byte: the same replicated model, drawn on the local board instead.</para>
    ///
    /// <para>THE GATE STAYS <see cref="RevealGate"/>, and it is asked EXPLICITLY here rather than
    /// inferred from the turn. Belt: <see cref="Refusal"/> already refuses a focus outright during
    /// <c>SelectAbilityCardsOrLongRest</c> and <see cref="ResolveHand"/> drops a live focus the
    /// moment that window opens, so a read-only view cannot even exist then. Braces: this asks
    /// <c>RevealGate.ShowRoundCardFronts(actor)</c> anyway — the same predicate the remote board
    /// uses — so the secrecy rule is evaluated where the cards are about to be DRAWN, by the one
    /// class that owns it, and a future caller cannot reach the dock around it.</para>
    ///
    /// <para>Read-only is unaffected: the dock only decides WHAT is shown. Every card it produces is
    /// stamped non-grabbable / non-pokeable by the rebuild's per-card funnel, and the dock itself is
    /// put in read-only mode (<c>HalfSelection.SetReadOnly</c>) so its poke zones are never armed and
    /// the card canvas is never registered with the laser.</para>
    /// </summary>
    /// <param name="source">Where the dock's content comes from, or why it is empty — log-safe and
    /// short enough for a single line.</param>
    internal static bool RoundCardDock(CardsHandUI? hand, out string source)
    {
        if (hand == null || hand.PlayerActor == null)
        {
            source = "no hand / no actor";
            return false;
        }

        if (!_readOnlyView)
        {
            // Vanilla path, byte-for-byte the pre-feature decision.
            bool own = CardsGameApi.IsActionTurn(hand);
            source = own
                ? "the game's own action turn (CardsGameApi.IsActionTurn)"
                : "not this character's action turn (CardsGameApi.IsActionTurn)";
            return own;
        }

        if (!RevealGate.ShowRoundCardFronts(hand.PlayerActor))
        {
            source = $"gated by RevealGate — {SecretWindowReason}";
            return false;
        }

        source = "the replicated model (CCharacterClass.RoundAbilityCards)";
        return true;
    }

    /// <summary>
    /// How many cards the game's replicated model currently names as this character's CHOSEN round
    /// cards. Pure read of <c>CCharacterClass.RoundAbilityCards</c> (the same list
    /// <c>CardsGameApi.IsInRound</c> tests against), used ONLY to make the slot-fill log line able
    /// to tell "the model has nothing" apart from "the model has cards but no widget for them
    /// exists on this client yet". Never a gate.
    /// </summary>
    internal static int ModelRoundCardCount(CardsHandUI? hand)
    {
        try
        {
            CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
            CCharacterClass? cc = actor != null ? actor.CharacterClass : null;
            return cc?.RoundAbilityCards?.Count ?? 0;
        }
        catch
        {
            return 0; // diagnostic only — a half-torn actor reads as "nothing to say"
        }
    }

    // ---------------------------------------------------------------------------------- wire --

    /// <summary>
    /// The character we are EFFECTIVELY looking at — the wire's payload, and the entry the plain
    /// focus ring marks. With an override it is the focused character; without one it is whatever
    /// the game presents, which during our own turn is the acting character (so a player who never
    /// touches the feature still reports "correct" and wears a green board, exactly as the user
    /// asked). Falls back to the game's presented hand when it is somebody else's turn.
    /// </summary>
    internal static CPlayerActor? LookingAt
    {
        get
        {
            if (_focused != null)
                return _focused;
            if (LocalOwnsTurn)
                return TurnActor;
            return PresentedActor;
        }
    }

    /// <summary>Fill the sender's extras record (22). Called from the extras sampler.</summary>
    internal static void Sample(out int focusActorId, out bool ownsTurn)
    {
        focusActorId = NetFigures.StableActorId(LookingAt);
        ownsTurn = LocalOwnsTurn;
    }

    /// <summary>Apply a peer's decoded record 22. A packet WITHOUT the record clears that peer's
    /// entry, which is what makes an old-build (or scenario-less) peer render no outline at all —
    /// the pre-record behaviour.</summary>
    internal static void ApplyPeer(int playerId, bool hasFocus, int actorId, bool ownsTurn)
    {
        if (!hasFocus || actorId == 0)
        {
            Peers.Remove(playerId);
            return;
        }
        Peers[playerId] = new PeerFocus(actorId, ownsTurn);
    }

    /// <summary>Forget a peer (they left / went stale).</summary>
    internal static void ForgetPeer(int playerId) => Peers.Remove(playerId);

    /// <summary>Drop everything (scenario teardown, session end, module shutdown).</summary>
    internal static void Reset()
    {
        _focused = null;
        _readOnlyView = false;
        PresentedActor = null;
        _loggedFocusId = 0;
        _lastRefusal = null;
        _lastRefusedActorId = 0;
        _lastRefusalTime = float.NegativeInfinity;
        Peers.Clear();
    }

    // --------------------------------------------------------------------------------- detail --

    private static void LogFocusOnce()
    {
        int id = PresentedActorId;
        if (id == _loggedFocusId)
            return;
        _loggedFocusId = id;
        VRLog.Info("Cards", $"[Focus] presenting '{Describe(PresentedActor)}' READ-ONLY " +
                            $"(game presents nothing we may drive; every card is built " +
                            $"non-grabbable, non-pokeable and invisible to the laser).");
    }

    /// <summary>A short, log-safe character name. Never throws — a half-built actor logs "?".</summary>
    internal static string Describe(CPlayerActor? actor)
    {
        if (actor == null)
            return "?";
        try
        {
            string? name = actor.CharacterName;
            if (!string.IsNullOrEmpty(name))
                return name!;
            return actor.CharacterClass?.DefaultModel ?? "?";
        }
        catch
        {
            return "?";
        }
    }
}
