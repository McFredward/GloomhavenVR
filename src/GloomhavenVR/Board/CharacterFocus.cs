using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Board;

/// <summary>
/// How a player's control board / avatar is marked with respect to the character THE GAME IS
/// WAITING ON — its turn, or (nobody at turn) an open decision it owes
/// (<see cref="CharacterFocus.AttentionActor"/>). One enum, three states — the whole visual
/// vocabulary of the feature. The names keep their <c>AtTurn…</c> spelling because the colours and
/// the blink are literally the ones the at-turn cue has always used; only the set of moments they
/// appear in grew, and it grew by exactly the pending-decision moments the user reported as
/// unmarked. The peer-side derivation (<see cref="CharacterFocus.MarkForPeer"/>) reproduces this
/// enum EXACTLY, from wire record 22 alone and with no local turn read, so a mirrored board can
/// never be wearing a state its owner's own board is not in.
/// </summary>
internal enum FocusTurnMark
{
    /// <summary>No mark: this player does not own the character the game is waiting on (or nobody
    /// is being waited on).</summary>
    None,

    /// <summary>GREEN: this player owns the character the game is waiting on AND is looking at that
    /// character.</summary>
    AtTurnCorrect,

    /// <summary>RED: this player owns the character the game is waiting on but is looking at a
    /// DIFFERENT one.</summary>
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
/// window, docked by <c>WorldUI.Surfaces.DecisionDockSurface</c> on <c>PlayTray.DecisionMount</c>.
/// Focus only chooses which <c>CardsHandUI</c> the card fan renders; the game's own decision
/// widgets are never touched by a switch, so they keep their state and stay answerable.</para>
///
/// <para>A DECISION BELONGS TO ONE CHARACTER (user ruling 2026-08-08: "Die Entscheidung soll auch
/// nur für den jeweiligen Character angezeigt werden! Wechsle ich den Character, während ich eine
/// Entscheidung treffen muss, soll auch die Entscheidung nicht mehr angezeigt werden bei dem neuen
/// Character."). <c>DecisionDockSurface</c> therefore reads <see cref="Focused"/> and RENDER-HIDES
/// its own converted host — the mod-owned canvas the row was reparented onto — while the player
/// looks at somebody other than the character the prompt was raised FOR (resolved from the game's
/// own model: the attacked actor, the short-resting hand, the hand whose pick opened the confirm).
/// That hide is a mod-side visibility toggle on mod-side objects: it disables <c>Canvas</c>
/// components under our host, never a game method, never a GameObject the game owns, so no
/// <c>OnDisable</c> of a game widget fires and no answer/cancel/expiry path is reachable. The
/// prompt's <c>UIWindow</c> stays open and the Choreographer stays parked exactly where it was —
/// looking away is as inert as looking at a different card fan.</para>
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
/// <para>WHAT DOES RIDE THE WIRE: one record, <c>NetProtocol.ExtIdCharFocus</c> (22) — the stable
/// id of the focused character, one bit "the character the game is waiting on is mine", and (only
/// when those two are different characters) the stable id of the character being waited on. Every
/// one of them is a fact a receiver genuinely cannot derive: <c>CActor.IsUnderMyControl</c> is a
/// LOCAL flag (false on every other machine), the focus itself is a VR presentation choice the game
/// model knows nothing about, and a remote player's OPEN DECISION is hidden by the game itself
/// (<c>UIScenarioMultiplayerController.RefreshDamagePhase</c> →
/// <c>TakeDamagePanel.ShowOtherPlayer</c> → <c>myWindow.Hide(instant: true)</c>,
/// TakeDamagePanel.cs:1133 — so <c>IsOpen</c> is false and both <see cref="DecisionOwner"/>'s
/// source and <c>DecisionDockSurface.PromptOwner</c> answer null on an observing client).</para>
///
/// <para>The only thing left to a receiver's own model read is the TURN, and only for the state
/// where the two can never disagree: with bit 0 clear the sender's attention actor IS
/// <c>Choreographer.CurrentPlayerActor</c> (a decision they do not own cannot be their attention
/// actor — the chain is locally gated), which is replicated. See
/// <see cref="AttentionIdForPeer"/>.</para>
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY — costs wire bytes (extras extension record 22, 5 B, or 9 B in
/// the "looking at the wrong character" state, and only while a focus is known). Names CHARACTERS
/// and flags; never a card. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
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

    /// <summary>
    /// One peer's synced cue, straight off record 22 — <see cref="ActorId"/> is the character they
    /// are LOOKING at, <see cref="OwnsAttention"/> is the wire bit "the character the game is
    /// waiting on is mine", and <see cref="AttentionId"/> is that character (0 when the bit is
    /// clear). The mark is a pure function of these three (<see cref="MarkForPeer"/>) and reads no
    /// local state at all: it used to be re-derived from THIS client's turn, which is precisely the
    /// assumption a pending decision breaks — see <see cref="MarkForPeer"/>.
    /// </summary>
    internal readonly struct PeerFocus
    {
        internal readonly int ActorId;
        internal readonly bool OwnsAttention;
        internal readonly int AttentionId;

        internal PeerFocus(int actorId, bool ownsAttention, int attentionId)
        {
            ActorId = actorId;
            OwnsAttention = ownsAttention;
            AttentionId = attentionId;
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
    /// THE CHARACTER WHOSE TURN IT IS — <b>the turn, never the momentarily-acting figure</b>.
    ///
    /// <para>DEFECT (user, hardware: "Im Spiel gibt es die Situation mit Karten, dass man einer
    /// Beschwörung oder einem anderen Character die Möglichkeit gibt anzugreifen — dies zählt aber
    /// noch als der Zug des Characters der dies ausgelöst hat, daher sollen während dessen auch die
    /// jeweiligen Karten liegen bleiben von dem ausgewählten Character. Beim Test war das
    /// Controllboard leer als ich aktiv mit einer Beschwörung angegriffen habe über eine Karte die
    /// mir das erlaubt hat.")</para>
    ///
    /// <para>THE GAME'S OWN MODEL, read at source, has TWO separate notions and the mod used to
    /// know only one of them:</para>
    /// <list type="bullet">
    /// <item><c>GameState.s_TurnActor</c> (<c>GameState.TurnActor</c>, GameState.cs:471) is THE
    ///   TURN. It is written in exactly two places — the initiative advance
    ///   (<c>s_TurnActor = s_CurrentActor</c>, GameState.cs:1780), the extra-turn hand-off
    ///   (GameState.cs:3626) — and cleared at EndRound (GameState.cs:2302). The game's own UI reads
    ///   it for the "END TURN of &lt;name&gt;" button (SelectActionScenarioState.cs:32,
    ///   SelectItemState.cs:35, LongRestScenarioState.cs:42), on every client.</item>
    /// <item><c>GameState.s_CurrentActor</c> / <c>Choreographer.m_CurrentActor</c> is THE ACTING
    ///   FIGURE, and a card may re-point it MID-TURN without the turn changing hands:
    ///   <c>GameState.OverrideCurrentActorForOneAction</c> (GameState.cs:3478-3480) sets
    ///   <c>s_CurrentActor = actor; OverridingCurrentActor = true;</c> and broadcasts
    ///   <c>CUpdateCurrentActor_MessageData</c>, which every client applies straight onto
    ///   <c>Choreographer.m_CurrentActor</c> (Choreographer.cs:10622). It NEVER touches
    ///   <c>s_TurnActor</c>. That is literally the user's sentence, in the game's code.
    ///   The two call sites the report names are
    ///   <c>CSummonActiveBonus_CastAbilityFromSummon.cs:28</c> ("cast this ability FROM your
    ///   summon" — the summon becomes the acting figure, the summoner stays the turn) and
    ///   <c>CAbilityControlActor.cs:181</c> (give ANOTHER figure the action).</item>
    /// </list>
    ///
    /// <para>SO THE RESOLUTION IS: while <c>GameState.OverridingCurrentActor</c> is set — and ONLY
    /// then — the turn is <c>GameState.TurnActor</c>, mapped to the player character that owns it
    /// (a <c>CHeroSummonActor</c> resolves to its <c>Summoner</c>, CHeroSummonActor.cs:116).
    /// Otherwise this is byte-for-byte the pre-existing read,
    /// <c>Choreographer.CurrentPlayerActor</c> (Choreographer.cs:474), which already resolves a
    /// summon to its summoner — so every situation that worked before answers exactly as it did,
    /// and the ONLY behaviour that changes is the one the report describes.</para>
    ///
    /// <para>WHY THE OVERRIDE FLAG IS THE GATE RATHER THAN "always prefer GameState.TurnActor".
    /// The two statics live one layer apart: the Choreographer's actor is driven by the REPLICATED
    /// message stream and is deliberately null through several between-turn steps (it is nulled at
    /// Choreographer.cs:3587/3711/9398 — the enemy-information reveal among them), while
    /// <c>s_TurnActor</c> keeps its value until EndRound. Preferring the rules-side field
    /// unconditionally would therefore make "somebody is at turn" TRUE in windows where this mod
    /// has already ruled that nobody is — most sharply <c>PlayTray.ConfirmCapsForeignView</c>'s
    /// "NOT ATTRIBUTABLE ⇒ EVERYBODY'S" rule, which is keyed on <see cref="TurnActor"/> being null
    /// during <c>MonsterClassesSelectAbilityCards</c> (user ruling, ModBuild 85). Gating on the
    /// override flag adds the missing case without reopening any settled one.</para>
    ///
    /// <para>MULTIPLAYER: both reads are client-side and identical on every machine — the override
    /// is announced by <c>CUpdateCurrentActor_MessageData</c> and the SRL turn advance runs from
    /// the same replicated stream — so a peer's mirror (which derives the displayed character from
    /// record 22, itself sampled from <see cref="LookingAt"/> ⇒ <see cref="AttentionActor"/> ⇒
    /// this) follows the summoner's board exactly as the owner's own board does. No wire change.</para>
    /// </summary>
    internal static CPlayerActor? TurnActor
    {
        get
        {
            try
            {
                if (GameState.OverridingCurrentActor)
                {
                    CPlayerActor? owner = TurnOwnerOf(GameState.TurnActor);
                    if (owner != null)
                        return owner;
                }
            }
            catch (System.Exception)
            {
                // Presentation question: a half-torn rules state must never take the board with it.
                // Falling through to the Choreographer read is the pre-existing answer.
            }
            Choreographer c = Choreographer.s_Choreographer;
            return c != null ? c.CurrentPlayerActor : null;
        }
    }

    /// <summary>
    /// The PLAYER CHARACTER an actor's turn belongs to: a hero summon's turn belongs to its
    /// <c>Summoner</c> (CHeroSummonActor.cs:116 — the same mapping
    /// <c>Choreographer.CurrentPlayerActor</c> makes at Choreographer.cs:476-479), a player
    /// character's to itself, and an enemy's / object's to nobody. Null-safe by construction so a
    /// summon whose summoner has left the scenario answers "nobody" rather than throwing.
    /// </summary>
    private static CPlayerActor? TurnOwnerOf(CActor? actor) => actor switch
    {
        CHeroSummonActor summon => summon.Summoner,
        CPlayerActor player => player,
        _ => null,
    };

    /// <summary>
    /// Does the CURRENT TURN belong to this hand's LOCALLY CONTROLLED character? This is
    /// <c>CardsGameApi.IsActionTurn</c> with the one substitution the summon report forces:
    /// <see cref="TurnActor"/> (the turn) instead of <c>Choreographer.CurrentActor is
    /// CPlayerActor</c> (the acting figure).
    ///
    /// <para>WHY THE OLD EXPRESSION EMPTIED THE BOARD. <c>CHeroSummonActor</c> derives from
    /// <c>CActor</c>, NOT from <c>CPlayerActor</c> (CHeroSummonActor.cs:10) — so the moment a card
    /// hands the action to a summon (<c>CSummonActiveBonus_CastAbilityFromSummon.cs:28</c>) or a
    /// move is driven for one (<c>Choreographer.cs:4269</c>,
    /// <c>m_CurrentActor = m_MoveAbility.CurrentMovingActor</c>), the pattern match fails, the
    /// predicate answers false for EVERY hand, and the round-card dock empties: the reported blank
    /// board. Asking the TURN cannot fail that way — the turn stays with the character that played
    /// the card, which is precisely what the user says the rules mean.</para>
    ///
    /// <para>DELIBERATELY NOT a change to <c>CardsGameApi.IsActionTurn</c> itself: that predicate
    /// also gates ITEM usability (<c>Cards.ItemsPile</c>), where "the acting figure" is the game's
    /// own gate and must keep agreeing with it. This is the BOARD-CONTENT question, and it has one
    /// consumer — <see cref="RoundCardDock"/>.</para>
    /// </summary>
    private static bool TurnOwnedBy(CardsHandUI hand)
    {
        CPlayerActor? turn = TurnActor;
        if (turn == null || hand.PlayerActor == null || !ReferenceEquals(turn, hand.PlayerActor))
            return false;
        return !FFSNetwork.IsOnline || turn.IsUnderMyControl;
    }

    /// <summary>Stable wire id of <see cref="TurnActor"/> (0 = nobody / no scenario).</summary>
    internal static int TurnActorId => NetFigures.StableActorId(TurnActor);

    /// <summary>
    /// The character the game is waiting on for an OPEN DECISION, when that is not simply "whose
    /// turn it is" — the attacked/burning hero of a take-damage prompt, the actor owing an item
    /// surrender, the local anchor of a reward forfeit, the hero stepping through the boots' ±
    /// phase. It is <c>Cards.CardsGameApi.DecidingHand()</c>'s actor and NOTHING ELSE: that method
    /// IS the deciding-actor chain the card board already presents from
    /// (<c>CardsDriver.CurrentHand</c> = <c>DecidingHand() ?? ActiveHand()</c>) and the same
    /// question <c>WorldUI.Surfaces.DecisionDockSurface.PromptOwner</c> answers for the docked
    /// prompt row. No third resolver exists and none may be added: if the mod ever disagreed with
    /// itself about WHO owes the decision, the docked prompt and the highlight pointing at it would
    /// name two different characters.
    ///
    /// <para>Every entry of that chain is non-null only while its own flow is genuinely OPEN and is
    /// already gated to a LOCALLY CONTROLLED actor (<c>TakeDamageHand</c> refuses unless
    /// <c>TakeDamagePanel.ThisPlayerHasTakeDamageControl</c> and the resolved actor is ours), so
    /// this can never name a teammate's pending decision. <see cref="LocalOwnsAttention"/> re-asks
    /// the ownership question anyway, belt and braces.</para>
    /// </summary>
    internal static CPlayerActor? DecisionOwner
    {
        get
        {
            try
            {
                CardsHandUI? hand = CardsGameApi.DecidingHand();
                CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
                return actor != null ? actor : null;
            }
            catch (System.Exception)
            {
                // Presentation question: a half-torn model must never take the cue down with it.
                return null;
            }
        }
    }

    /// <summary>
    /// THE CHARACTER THE GAME IS WAITING ON — the one fact the board stroke and the initiative
    /// ring have always meant, now stated completely.
    ///
    /// <para>DEFECT (user, hardware ModBuild 86): "Der Character der aktuell eine Entscheidung
    /// treffen muss soll genauso gehighlighted werden wie zuvor auch — im Falle einer
    /// Schadensauswahl ist das Highlighting nicht sichtbar, nur das rote Overlay". The cue used to
    /// read <see cref="TurnActor"/> alone, i.e. <c>Choreographer.CurrentPlayerActor</c>, which is
    /// NULL for the whole of an enemy's action — and a take-damage prompt is raised precisely
    /// there (<c>Choreographer.cs:5467</c>, <c>MessageType.PlayerSelectingToAvoidDamageOrNot</c>,
    /// inside the enemy's Action phase; the hardware log shows "phase Action, at turn '?'" for
    /// every frame of that prompt). So a character that genuinely owed the player a decision wore
    /// no ring and its board wore no stroke: the ONLY mark left was vanilla's own red
    /// <c>InitiativeTrackPlayerBehaviour.ShowWarning</c> pulse
    /// (<c>InitiativeTrackPlayerBehaviour.cs:60-69</c>, played from <c>Choreographer.cs:5477</c>),
    /// which is the red overlay the user describes — and which says something DIFFERENT ("this
    /// actor is being attacked", on <c>m_ActorBeingAttacked</c>) from what was missing.</para>
    ///
    /// <para>Turn FIRST, decision second: while somebody is at turn the two either agree (the
    /// acting hero's own prompt) or the turn is the stronger statement, and reading the turn first
    /// keeps every pre-existing situation byte-for-byte as it was — the decision branch can only
    /// ever ADD a mark where there was none.</para>
    /// </summary>
    internal static CPlayerActor? AttentionActor => TurnActor ?? DecisionOwner;

    /// <summary>True when the character the game is waiting on (turn OR open decision) is one the
    /// LOCAL client controls. Offline every merc is ours.</summary>
    internal static bool LocalOwnsAttention
    {
        get
        {
            CPlayerActor? actor = AttentionActor;
            return actor != null && (!FFSNetwork.IsOnline || actor.IsUnderMyControl);
        }
    }

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
    /// The LOCAL player's attention mark — the colour their own control board and their own Steam
    /// avatar wear. Green while they own the character the game is waiting on AND are looking at
    /// it; red while they own it but are looking at somebody else (the user's "wrong character"
    /// warning); none otherwise.
    ///
    /// <para>"The character the game is waiting on" is <see cref="AttentionActor"/>: the character
    /// AT TURN, or — when nobody is at turn, which is the whole of an enemy's action — the
    /// character that owes an OPEN DECISION (<see cref="DecisionOwner"/>). The colours and the
    /// blink are unchanged; only the set of moments in which they appear grew, and it grew by
    /// exactly the moments the user reported as unmarked ("Der Character der aktuell eine
    /// Entscheidung treffen muss soll genauso gehighlighted werden wie zuvor auch"). Both states
    /// mean one thing to the player — <b>the game is waiting on THIS character of yours</b> — so
    /// they are deliberately ONE cue rather than two competing ones.</para>
    /// </summary>
    internal static FocusTurnMark LocalMark
    {
        get
        {
            // ONE walk of the attention chain per read (it is a per-frame consumer): resolve the
            // actor once and ask the ownership question of THAT object, instead of calling
            // LocalOwnsAttention and then re-resolving for the comparison below.
            CPlayerActor? attention = AttentionActor;
            if (attention == null || (FFSNetwork.IsOnline && !attention.IsUnderMyControl))
                return FocusTurnMark.None;
            // NO OVERRIDE ⇒ we are looking at whatever the game presents, and the game presents
            // the character it is waiting on: during our own turn the acting character (it selects
            // it and switches the hand to it), and during a decision the deciding hand — the card
            // board resolves exactly that (CardsDriver.CurrentHand = DecidingHand() ?? ActiveHand(),
            // and DecidingHand IS what DecisionOwner reads). So "correct" is the state by
            // construction, and it does not depend on a card rebuild having run this frame — which
            // matters, because Rebuild is edge-driven and the mark is read every frame.
            if (_focused == null)
                return FocusTurnMark.AtTurnCorrect;
            return ReferenceEquals(_focused, attention)
                ? FocusTurnMark.AtTurnCorrect
                : FocusTurnMark.AtTurnWrong;
        }
    }

    /// <summary>
    /// The stable actor id of the character a PEER is looking at (0 = unknown / no record). The
    /// companion of <see cref="MarkForPeer"/>: the mark says whether their focus is the RIGHT one,
    /// this says WHICH one, so their mirrored initiative track can ring the same entry the local
    /// track rings for the local player.
    /// </summary>
    internal static int FocusIdForPeer(int playerId) =>
        Peers.TryGetValue(playerId, out PeerFocus peer) ? peer.ActorId : 0;

    /// <summary>
    /// The stable actor id of the character THE GAME IS WAITING ON as far as a PEER is concerned —
    /// the entry their mirrored initiative track must ring, and the exact object their OWN track
    /// rings (<c>FocusDriver.TickRings</c> passes <see cref="AttentionActor"/>).
    ///
    /// <para>TWO SOURCES, AND THE SPLIT IS NOT A COMPROMISE. When the peer OWNS the character being
    /// waited on, the id comes off the wire (record 22, flags bit 0, plus bit 1's trailing id when
    /// it differs from their focus) — it has to, because a decision they own is invisible on this
    /// machine. When they do NOT own it, their attention actor can only be
    /// <c>Choreographer.CurrentPlayerActor</c>: <see cref="DecisionOwner"/>'s chain is gated to
    /// LOCALLY CONTROLLED actors (<c>CardsGameApi.TakeDamageHand</c> refuses unless the resolved
    /// actor <c>IsUnderMyControl</c>), so a decision they do not own is never their attention actor
    /// either — and the turn is replicated, so <see cref="TurnActorId"/> is the same integer on both
    /// machines. Substituting it here is therefore an identity, not a guess.</para>
    /// </summary>
    internal static int AttentionIdForPeer(int playerId)
    {
        if (Peers.TryGetValue(playerId, out PeerFocus peer) && peer.AttentionId != 0)
            return peer.AttentionId;
        return TurnActorId;
    }

    /// <summary>
    /// A PEER's attention mark — the colour their mirrored control board, their mirrored Steam
    /// avatar and their mirrored initiative-track entry wear. Unknown peer / no record / they do not
    /// own the character the game is waiting on ⇒ <see cref="FocusTurnMark.None"/>.
    ///
    /// <para>IT READS NOTHING LOCAL, AND THAT IS THE FIX (2026-08-08). It used to compare the peer's
    /// focus id against THIS client's <see cref="TurnActorId"/>, which is correct only while both
    /// machines agree about what the game is waiting on — the assumption a pending DECISION breaks,
    /// because a take-damage prompt is raised inside an ENEMY's action where
    /// <c>Choreographer.CurrentPlayerActor</c> is null on every client, and the receiver cannot
    /// repair that locally: <c>UIScenarioMultiplayerController.RefreshDamagePhase</c> routes a remote
    /// player's prompt through <c>TakeDamagePanel.ShowOtherPlayer</c>, which ends in
    /// <c>myWindow.Hide(instant: true)</c> (TakeDamagePanel.cs:1133), so <c>IsOpen</c> is false and
    /// both <c>CardsGameApi.DecidingHand</c> and <c>DecisionDockSurface.PromptOwner</c> answer null
    /// there. So the SENDER states the two facts and this is now a pure function of them —
    /// term for term the same expression <see cref="LocalMark"/> evaluates on the sender's machine
    /// (own the attention actor at all? then: is it the one being looked at?), which is what makes
    /// "im Multiplayer soll immer exakt das angezeigt werden was der User auch sieht"
    /// structurally true rather than true by inspection.</para>
    ///
    /// <para>Vanilla's own red <c>InitiativeTrackPlayerBehaviour.ShowWarning</c> damage pulse is
    /// untouched and still crosses on its own (Choreographer.cs:5477 runs on every client, and
    /// <c>RemoteWidgetMirror.Pair.Apply</c> copies the resulting GameObject state) — the two cues
    /// are drawn together on a peer's mirror exactly as they are on their own screen.</para>
    /// </summary>
    internal static FocusTurnMark MarkForPeer(int playerId)
    {
        if (!Peers.TryGetValue(playerId, out PeerFocus peer) || !peer.OwnsAttention)
            return FocusTurnMark.None;
        int attentionId = peer.AttentionId;
        if (attentionId == 0)
            return FocusTurnMark.None;
        return peer.ActorId == attentionId
            ? FocusTurnMark.AtTurnCorrect
            : FocusTurnMark.AtTurnWrong;
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

    /// <summary>
    /// MAY THE PLAYER PICK A CARD OUT OF THIS HAND JUST TO LOOK AT IT? User ruling 2026-08-08:
    /// „Ich möchte das man jederzeit auch eine Karte aus der Hand nehmen kann um sie sich genau
    /// anzuschauen, auch wenn man die Karte nirgendwo ablegen kann. Das soll also niemals blockiert
    /// sein — aktuell kann man nur Karten in die Hand nehmen wenn man sie auch ablegen kann."
    ///
    /// <para>THE ENTITLEMENT RULE, in one line: <b>inspection is allowed for every character the
    /// LOCAL client controls, in every phase, and refused for a FOREIGN character.</b> That is
    /// exactly <c>CardsGameApi.IsLocalHand</c> — <c>!FFSNetwork.IsOnline ||
    /// hand.PlayerActor.IsUnderMyControl</c>, the game's own ownership test and the precise inverse
    /// of <see cref="IsForeign"/> — so offline (where every merc is ours) the answer is always yes
    /// and the feature is unconditional.</para>
    ///
    /// <para>WHY NOT ALSO FOREIGN, given that a foreign HAND is not secret. It is not secret: the
    /// class doc's "WHAT IS AND IS NOT A DISCLOSURE" reads it off the game's own model
    /// (<c>CCharacterClass.HandAbilityCards</c> is host-replicated with no visibility gate,
    /// <c>CCharacterClass.cs:91</c>), the game builds a fully populated <c>CardsHandUI</c> per
    /// player actor on EVERY client (<c>Choreographer.cs:925/1112</c>), and the focus fan already
    /// DRAWS those cards face-up today. So refusing here is NOT a secrecy necessity — nothing new
    /// would be disclosed. It is refused because a foreign view's read-only guarantee is
    /// STRUCTURAL and worth keeping absolute: while the mod presents a character the player may not
    /// drive, nothing it built is grabbable, so no VR input can reach a game call for that
    /// character at all. Keeping that funnel exception-free matters most for the one seam that
    /// would otherwise be within reach — <c>CardsDriver.OnCardReleased</c> resolves its game hand
    /// from <c>CurrentHand()</c>, which in a focus view is a DIFFERENT character's hand, so a
    /// foreign card released near a slot would be evaluated against the local player's own hand.
    /// The release path refuses that on <see cref="Cards.VRCard.InspectOnly"/> anyway; this keeps
    /// the card from ever getting there. <see cref="RevealGate"/> is untouched and unweakened: the
    /// only real secret it guards — which two cards a player CHOSE this round — lives in the
    /// ROUND-card dock, which stays a picture for every focus view (<c>HalfSelection.SetReadOnly</c>),
    /// and a focus cannot even be open during <see cref="RevealGate.IsSecretSelectionPhase"/>
    /// (<see cref="Refusal"/>, <see cref="ResolveHand"/>).</para>
    ///
    /// <para>NOTE WHAT THIS DOES <b>NOT</b> DECIDE: whether the card may be PLAYED. That stays
    /// exactly where it was (<c>CardsDriver.Rebuild</c>'s <c>grabbable</c>, i.e. the real
    /// card-selection window and the modal pick flows). A hand that answers true here but is
    /// outside that window becomes <c>CardFan.FanMode.Inspect</c>: fully handleable, never
    /// committable.</para>
    /// </summary>
    internal static bool HandInspectable(CardsHandUI? hand) =>
        hand != null && hand.PlayerActor != null && CardsGameApi.IsLocalHand(hand);

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

        CardsHandUI? focusHand = PresentedHand(gameHand);
        if (focusHand == null || ReferenceEquals(focusHand, gameHand))
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
    /// <see cref="ResolveHand"/>'s ANSWER WITHOUT ITS SIDE EFFECTS — "which character's hand is the
    /// control board presenting right now", askable from a per-frame path.
    ///
    /// <para>WHY IT EXISTS (user report, hardware ModBuild 89: "Die Zahlen wie viele Karten in dem
    /// verbrannt/abgeworfen Stapel waren haben sich bei Character-Tausch nicht aktualisiert", and
    /// its twin "beim verbrannt Stapel wurde nicht aktualisiert"). <see cref="ResolveHand"/> is the
    /// ONE seam into the card pipeline, but it LATCHES (<see cref="PresentedActor"/>,
    /// <see cref="ReadOnlyView"/>), CLEARS a stale focus and LOGS — so it may only ever be called
    /// from the edge-driven rebuild, and every per-frame consumer was therefore left reading the
    /// GAME's hand (<c>CardsDriver.CurrentHand</c>). For the pile stacks that meant the counts on
    /// the board belonged to whichever character the GAME presents, never to the one the player is
    /// looking at: a focus switch moved every other surface and left the two numbers behind, and a
    /// card burned by the focused character never showed up at all.</para>
    ///
    /// <para>It is the same decision tree, term for term, minus the mutations: no focus / gate shut
    /// / focused character exhausted ⇒ the game's hand; the focus IS the game's hand ⇒ the game's
    /// hand (<see cref="ResolveHand"/> additionally drops the now-pointless override, which is a
    /// state change and therefore stays there); otherwise the focused character's hand, falling back
    /// to the game's hand while its widget has not been built yet. <see cref="ResolveHand"/> calls
    /// THIS method for its own lookup, so the two cannot drift apart.</para>
    /// </summary>
    internal static CardsHandUI? PresentedHand(CardsHandUI? gameHand)
    {
        if (_focused == null || _focused.IsDead || !Refusal(out _))
            return gameHand;
        if (gameHand != null && ReferenceEquals(gameHand.PlayerActor, _focused))
            return gameHand;
        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI? focusHand = manager != null ? manager.GetHand(_focused) : null;
        return focusHand != null ? focusHand : gameHand;
    }

    /// <summary>
    /// Does the mod's ROUND-CARD dock apply to <paramref name="hand"/> — i.e. may the board's two
    /// card slots show this character's CHOSEN cards — and if not, WHY not?
    ///
    /// <para>VANILLA PATH (no focus override): <see cref="TurnOwnedBy"/> — "the CURRENT TURN belongs
    /// to this hand's locally-controlled player". That is the state the interactive half-play
    /// affordance belongs to and it is still keyed to the turn; what changed is only WHERE the turn
    /// is read from.</para>
    ///
    /// <para>IT USED TO BE <c>CardsGameApi.IsActionTurn</c>, i.e. <c>Choreographer.CurrentActor is
    /// CPlayerActor cur &amp;&amp; cur == hand.PlayerActor</c> — the ACTING FIGURE. Two user reports
    /// are the same defect in that one expression, and both are "the board went blank while the
    /// game was still in my character's turn":</para>
    /// <list type="bullet">
    /// <item>"Beim Test war das Controllboard leer als ich aktiv mit einer Beschwörung angegriffen
    ///   habe über eine Karte die mir das erlaubt hat." — the summon becomes
    ///   <c>Choreographer.m_CurrentActor</c> (<c>GameState.OverrideCurrentActorForOneAction</c> →
    ///   <c>CUpdateCurrentActor_MessageData</c> → Choreographer.cs:10622) and a
    ///   <c>CHeroSummonActor</c> is not a <c>CPlayerActor</c> (CHeroSummonActor.cs:10), so the
    ///   pattern match failed and NOBODY's cards docked;</item>
    /// <item>"Während dessen eine Bewegung oder ein Angriff bestätigt werden muss … werden die
    ///   ausgewählten Karten auf dem Controllboard nicht mehr angezeigt" — the same hole, reached
    ///   through the movement/targeting messages, which re-point the acting figure at the FIGURE
    ///   BEING MOVED or TARGETED (<c>Choreographer.cs:4269</c>
    ///   <c>m_CurrentActor = m_MoveAbility.CurrentMovingActor</c>, <c>:5996</c>
    ///   <c>ActorIsSelectingTargetingFocus</c>, <c>:9878/:10013</c> push/pull) — a summon or an
    ///   ally, while the turn never left the character who played the card.</item>
    /// </list>
    /// <para>Reading the TURN instead of the figure closes both with one substitution, and leaves
    /// every pre-existing situation identical (see <see cref="TurnActor"/>: outside an override the
    /// expression IS the old one).</para>
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
            // Vanilla path: the CURRENT TURN, not the momentarily-acting figure (see the doc's
            // "IT USED TO BE CardsGameApi.IsActionTurn" paragraph — a summon or a commanded ally
            // takes the action without taking the turn, and the board must follow the turn).
            bool own = TurnOwnedBy(hand);
            source = own
                ? "this character owns the CURRENT TURN (CharacterFocus.TurnActor — the turn, not " +
                  "the momentarily-acting figure, so a summon/ally action keeps the cards docked)"
                : "this character does not own the current turn (CharacterFocus.TurnActor)";
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

    /// <summary>
    /// How many cards the game's replicated model currently names as this character's HAND —
    /// a pure read of <c>CCharacterClass.HandAbilityCards</c> (CCharacterClass.cs:91, the
    /// host-replicated list with no visibility gate; the very list
    /// <c>TakeDamagePanel.cs:412</c> and <c>CPlayerActorExtensions.cs:27</c> test against).
    ///
    /// <para>THIS IS THE ARBITER OF "KEINE HANDKARTEN" (user ruling 2026-08-08: "'Keine
    /// Handkarten' soll wirklich nur dann kommen, wenn der Character auch wirklich keine
    /// Handkarten hat, egal in welcher Phase"). The empty-hand placard used to be raised from a
    /// MOD-side fact — "the fan buffer is empty" — which is a statement about what the mod built
    /// this frame, not about the character. Those two came apart in exactly one place and it was
    /// the reported bug (see <see cref="HandWidgetCount"/>). The placard now asks the MODEL, and
    /// the model alone, whether the hand is empty.</para>
    ///
    /// <para>Never a gate on anything else, and never a secrecy question: the hand pile is not
    /// secret in Gloomhaven (class doc, "WHAT IS AND IS NOT A DISCLOSURE") — only the two CHOSEN
    /// round cards are, and only during the window <see cref="RevealGate"/> owns.</para>
    /// </summary>
    internal static int ModelHandCardCount(CardsHandUI? hand)
    {
        try
        {
            CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
            CCharacterClass? cc = actor != null ? actor.CharacterClass : null;
            return cc?.HandAbilityCards?.Count ?? 0;
        }
        catch
        {
            return 0; // diagnostic only — a half-torn actor reads as "nothing to say"
        }
    }

    /// <summary>
    /// How many LIVE hand-pile card WIDGETS this client currently holds for the character —
    /// <c>CardsHandUI.cardsUI</c> entries whose <c>CardType</c> is <c>CardPileType.Hand</c>
    /// (the game instantiates a fully populated <c>CardsHandUI</c> per player actor on EVERY
    /// client, Choreographer.cs:925/1112). The companion of <see cref="ModelHandCardCount"/>:
    /// together they separate the only two honest answers a missing fan can have —
    /// "this character HAS no hand cards" (model 0) from "the widgets for them are not built on
    /// this client yet" (model &gt; 0, widgets 0, a transient the next rebuild retries).
    /// Diagnostic + retry signal only; never a gate.
    /// </summary>
    internal static int HandWidgetCount(CardsHandUI? hand)
    {
        try
        {
            if (hand == null)
                return 0;
            List<AbilityCardUI>? cards = hand.cardsUI;
            if (cards == null)
                return 0;
            int n = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                AbilityCardUI c = cards[i];
                if (c != null && c.AbilityCard != null && !c.IsLongRest
                    && c.CardType == CardPileType.Hand)
                    n++;
            }
            return n;
        }
        catch
        {
            return 0;
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
            // No override ⇒ we are looking at the character the game is waiting on, when that is
            // one of ours: the actor at turn, or (nobody at turn) the one owing an open decision.
            // In the steady state this is the same object PresentedActor already resolves to — the
            // card board presents DecidingHand() ?? ActiveHand() — so the wire payload is unchanged
            // in every pre-existing situation; it only stops depending on a rebuild having landed.
            CPlayerActor? attention = AttentionActor; // one chain walk — per-frame consumer
            if (attention != null && (!FFSNetwork.IsOnline || attention.IsUnderMyControl))
                return attention;
            return PresentedActor;
        }
    }

    /// <summary>
    /// Fill the sender's extras record (22). Called from the extras sampler.
    ///
    /// <para>THE THREE FIELDS ARE SAMPLED IN ONE PASS, from ONE walk of the attention chain, so
    /// they can never describe two different moments: the mark a receiver derives from them is
    /// <see cref="LocalMark"/> term for term, and a torn sample would be a mark that never existed
    /// on this screen. <paramref name="attentionActorId"/> is 0 exactly when
    /// <paramref name="ownsAttention"/> is false — the serializer then omits it, and the receiver
    /// substitutes its own (identical, replicated) turn actor. See
    /// <see cref="AttentionIdForPeer"/>.</para>
    /// </summary>
    internal static void Sample(out int focusActorId, out int attentionActorId,
                               out bool ownsAttention)
    {
        CPlayerActor? attention = AttentionActor; // ONE chain walk per packet
        ownsAttention = attention != null && (!FFSNetwork.IsOnline || attention.IsUnderMyControl);
        attentionActorId = ownsAttention ? NetFigures.StableActorId(attention) : 0;
        focusActorId = NetFigures.StableActorId(LookingAt);
    }

    /// <summary>Apply a peer's decoded record 22. A packet WITHOUT the record clears that peer's
    /// entry, which is what makes an old-build (or scenario-less) peer render no outline at all —
    /// the pre-record behaviour.</summary>
    internal static void ApplyPeer(int playerId, bool hasFocus, int actorId, bool ownsAttention,
                                   int attentionActorId)
    {
        if (!hasFocus || actorId == 0)
        {
            if (Peers.Remove(playerId))
                LogPeerCue(playerId, new PeerFocus(0, false, 0), FocusTurnMark.None);
            return;
        }
        var peer = new PeerFocus(actorId, ownsAttention,
                                 ownsAttention ? attentionActorId : 0);
        Peers[playerId] = peer;
        LogPeerCue(playerId, peer, MarkForPeer(playerId));
    }

    /// <summary>Last cue logged per peer, so the receive-side line is CHANGE-GATED: record 22 rides
    /// a 5 Hz packet, and a per-packet line would bury the one transition worth reading.</summary>
    private static readonly Dictionary<int, (int Focus, int Attention, bool Owns, FocusTurnMark Mark)>
        LoggedPeerCues = new(4);

    /// <summary>
    /// ONE line per peer whenever the cue this client applies for them changes — the receiving half
    /// of the sender's "Character focus SENT" line, so a hardware round can be read from BOTH
    /// machines: grep <c>Character focus</c> on the sender's log and
    /// <c>[Focus] peer cue</c> on the observer's (<c>.planning/debug/remote/</c>). It names the bits
    /// the mark came from, so "the peer's board is the wrong colour" is answerable without a repro.
    /// </summary>
    private static void LogPeerCue(int playerId, PeerFocus peer, FocusTurnMark mark)
    {
        var key = (peer.ActorId, peer.AttentionId, peer.OwnsAttention, mark);
        if (LoggedPeerCues.TryGetValue(playerId, out var last) && last == key)
            return;
        LoggedPeerCues[playerId] = key;

        if (peer.ActorId == 0)
        {
            VRLog.Info("Net", $"[Focus] peer cue CLEARED for player {playerId} — no record 22 in " +
                              "their packet (older build, spectator, or no scenario). Their " +
                              "mirrored board, avatar ring and initiative track show nothing, " +
                              "which is exactly the pre-record behaviour.");
            return;
        }

        string bits = peer.OwnsAttention
            ? (peer.AttentionId != peer.ActorId
                ? $"bit0 SET + bit1 SET (attention actor {peer.AttentionId} rode the record's " +
                  "4-byte tail because it is NOT the character they are looking at)"
                : "bit0 SET, bit1 clear (the character the game waits on IS the one they are " +
                  "looking at, so the focus id carries it and no tail was sent)")
            : "bit0 clear (the game is not waiting on a character of theirs; the attention id " +
              $"shown is this client's own replicated turn actor {TurnActorId})";

        string applied = mark switch
        {
            FocusTurnMark.AtTurnCorrect =>
                "APPLIED: 'correct' blink (green, or calm white in mixed reality) on their mirrored " +
                "board, their avatar ring and their track entry",
            FocusTurnMark.AtTurnWrong =>
                "APPLIED: 'wrong character' blink (red, or pumping amber in mixed reality) on their " +
                "mirrored board, their avatar ring and their track entry",
            _ =>
                "APPLIED: no blink — at most the steady gold 'somebody is up' ring their own track " +
                "also shows",
        };

        VRLog.Info("Net", $"[Focus] peer cue for player {playerId}: looking at actor {peer.ActorId}, " +
                          $"game waiting on actor {AttentionIdForPeer(playerId)} — from {bits}. " +
                          $"{applied}. The mark is a pure function of record 22 (no local turn read), " +
                          "so it is the SAME mark their own screen is wearing.");
    }

    /// <summary>Forget a peer (they left / went stale).</summary>
    internal static void ForgetPeer(int playerId)
    {
        Peers.Remove(playerId);
        LoggedPeerCues.Remove(playerId);
    }

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
        LoggedPeerCues.Clear();
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
