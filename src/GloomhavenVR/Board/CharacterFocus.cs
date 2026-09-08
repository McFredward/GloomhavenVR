using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
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
/// <para>AND THERE IS A FLOOR UNDER IT (user ruling 2026-08-13, finding 11: "der Zustand NIEMANDEN
/// ausgewählt zu haben darf nicht existieren auch nicht während dessen die Gegner dran sind"). The
/// game's presented hand follows whoever is ACTING — teammates included — so on every client it
/// spends most of a multiplayer round pointing at a character that client may not drive, which the
/// mod correctly maps to "no hand" and then, wrongly, presented as NOBODY. <see cref="ResolveHand"/>
/// now ends with <see cref="LocalFloorHand"/>: whenever the focus tree would answer "no hand", the
/// board holds a character this client actually controls, read-only. It is a presentation floor
/// only — it writes no game selection and never presents a foreign character. Full derivation,
/// evidence and rejected alternatives are on <see cref="LocalFloorHand"/>.</para>
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
/// <para>…AND ONE PIN, WHICH IS A DIFFERENT SHAPE OF THING (user ruling 2026-08-14, ModBuild 139:
/// "Ich will das das highlighting sowie die Auswahl nur bei dem jeweiligen Character getroffen
/// werden kann der diese Entscheidung treffen muss"). While the board is waiting for one of THIS
/// player's characters to pick a hex — a move destination, an attack target — the focus is PINNED
/// to that character: <see cref="PinnedActor"/>. It is an ACTOR-DEPENDENT refusal ("may THIS
/// character be focused instead of the one being asked") and never a third clause of the GLOBAL
/// gate <see cref="Open"/>/<see cref="Refusal"/> that the 08-08 ruling is written on. It is
/// bounded by the game's own targeting wait states and by local seat AND local character ownership,
/// so it cannot exist during a teammate's turn, during the enemies' turn, or anywhere outside a
/// live hex pick. The full derivation, the multiplayer argument and the rejected alternatives are
/// on <see cref="PinnedActor"/>.</para>
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
/// card driver for a rebuild; by the read-only guarantee above there is no mutator ON the path to
/// reach for. The click seam
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

    /// <summary>
    /// SELECTION FLOOR — the last locally-controlled character the board actually presented, kept
    /// so that "nobody selected" has something to fall back TO and so the fallback is a CONTINUATION
    /// of what the player was looking at rather than a fresh pick every time (see
    /// <see cref="LocalFloorHand"/>).
    /// </summary>
    private static CPlayerActor? _floorActor;

    /// <summary>
    /// Edge guard for the floor's diagnostic — the actor id we last announced it for, or null while
    /// the floor is not engaged. Deliberately NULLABLE and not "0 means nothing": 0 is a real id in
    /// this space (<c>NetFigures.StableActorId(null)</c> returns it, NetFigures.cs:520-521), and it
    /// is exactly the id of the one case that would then re-log on every rebuild — the floor finding
    /// nothing to fall back to.
    /// </summary>
    private static int? _loggedFloorId;

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

    /// <summary>
    /// The gate as a REASON rather than a bool: returns true when focus is allowed and
    /// <paramref name="why"/> is null, false with a short, log-safe explanation otherwise.
    /// Every refusal in the whole feature is minted here and nowhere else, so
    /// "<c>[Board] [Focus] switch REFUSED — …</c>" can never carry a reason this method does not
    /// know about — and after the 2026-08-08 ruling the only reason a PLAYER can ever see there is
    /// the card-selection phase.
    /// </summary>
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
        return CardsGameApi.ControlsActor(turn); // F5: partitioned list; offline still true
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
            return CardsGameApi.ControlsActor(actor); // F5: partitioned list (null ⇒ false, as before)
        }
    }

    /// <summary>True when the character at turn is one the LOCAL client controls. Offline every
    /// merc is ours, so this is simply "somebody is at turn" there.</summary>
    internal static bool LocalOwnsTurn
    {
        get
        {
            CPlayerActor? turn = TurnActor;
            return CardsGameApi.ControlsActor(turn); // F5: partitioned list (null ⇒ false, as before)
        }
    }

    // -------------------------------------------------------------------------------- FOCUS PIN --

    /// <summary>
    /// THE CHARACTER THE FOCUS IS <b>PINNED</b> TO — non-null exactly while the board is waiting for
    /// a hex pick from a character of THIS player's, and null in every other moment of the game.
    ///
    /// <para>USER REPORT (hardware, ModBuild 139, 2026-08-14): "Wenn gerade ein character eine
    /// Auswahl treffen [muss] wohin er geht, wen er attackiert etc. dann sind die jeweiligen Felder
    /// gehighlighted. Das soll auch so sein ABER NUR für den Character der diese Entscheidung auch
    /// treffen muss. Aktuell ist es möglich in dieser Phase einen anderen Character auszuwählen und
    /// dann trotzdem das feld auszuwählen wo er hingehen soll. Da die Buttons fehlen (was gewollt
    /// ist, da man ja einen anderen character ausgewählt hat) kann man dann nicht bestätigen."</para>
    ///
    /// <para>ROOT CAUSE, and it is a SPLIT between two things that must not come apart. The board's
    /// highlighting and its pick BELONG TO THE GAME: the stars are painted for
    /// <c>Choreographer.m_CurrentActor</c> and the click is dispatched by the game's own
    /// <c>Controller.LateUpdate</c>, which gates on <c>ThisPlayerHasTurnControl</c> — a SEAT test,
    /// not a character test (decompiled GH.Runtime/Choreographer.cs:557-580). The CONFIRM/UNDO
    /// affordance, on the other hand, belongs to the character the mod is PRESENTING: the keycaps
    /// hide as soon as the player looks at somebody other than the hand the confirm belongs to
    /// (<c>PlayTray.ConfirmCapsForeignView</c>, and correctly so — a confirm must never be pressed
    /// for a character you are not looking at). So a portrait click during the targeting window left
    /// the player holding a live, pickable, highlighted board with NO way to commit it: exactly the
    /// reported dead end. Both halves are individually right; what was missing is that during that
    /// window they must name the SAME character.</para>
    ///
    /// <para>THE PREDICATE, all three clauses, and none of them is optional:</para>
    /// <list type="number">
    /// <item><b>The board really is waiting for a pick</b> — <c>Choreographer.m_WaitState.m_State</c>
    ///   (Choreographer.cs:218) is one of the five targeting wait-states
    ///   (<c>WaitingForPlayerWaypointSelection</c>, <c>WaitingForAreaAttackFocusSelection</c>,
    ///   <c>WaitingForPlayerPushWaypointSelection</c>, <c>WaitingForPlayerPullWaypointSelection</c>,
    ///   <c>WaitingForTileSelected</c>), classified by <see cref="VRModeStateMachine.IsTargetingState"/>
    ///   so the five members live in ONE table. Read LIVE from the game rather than from
    ///   <c>VRModeStateMachine.TargetingActive</c>: that mirror is event-driven and is deliberately
    ///   force-cleared by a superseding flow message, and a gate that takes a control away may not
    ///   fire on a stale bit.</item>
    /// <item><b>The acting SEAT is mine</b> — <c>Choreographer.ThisPlayerHasTurnControl</c>
    ///   (Choreographer.cs:557-580), the game's own answer, and the very property
    ///   <c>WorldspaceStarHexDisplay</c> gates its target-selection stars on
    ///   (WorldspaceStarHexDisplay.cs:442, :469). It returns true when <c>!FFSNetwork.IsOnline</c>,
    ///   so offline it is a no-op and clause 3 carries the whole gate.</item>
    /// <item><b>The acting CHARACTER is one of mine</b> — <see cref="TurnActor"/>
    ///   (<c>Choreographer.CurrentPlayerActor</c>, Choreographer.cs:474-488, which maps a
    ///   <c>CHeroSummonActor</c> to its <c>Summoner</c>), alive, and <c>IsUnderMyControl</c> when
    ///   online. This is the clause the two failed ModBuilds (137, 138) were missing.</item>
    /// </list>
    ///
    /// <para>WHY CLAUSE 1 ALONE WOULD REPEAT THE 137/138 MISTAKE. The wait state is the SHARED,
    /// networked Choreographer state — the lockstep stream drives it identically on every client, so
    /// while a teammate places a waypoint EVERY peer sits in
    /// <c>WaitingForPlayerWaypointSelection</c>. A pin built on it alone would freeze a spectator's
    /// view onto whoever happens to be acting, which is the exact class of bug that cost ModBuild 137
    /// and 138 a hardware round each (see <c>Rig/LocalTurnControl.cs</c>, the same lesson for the
    /// thumbstick). Clauses 2 and 3 are what make this LOCAL: they are false on every machine except
    /// the one whose player is being asked. A teammate aiming pins nobody here.</para>
    ///
    /// <para>WHY <see cref="TurnActor"/> AND NOT <c>Choreographer.CurrentActor</c> DIRECTLY. During
    /// exactly these wait states the game RE-POINTS <c>m_CurrentActor</c> at the figure being driven
    /// — <c>m_CurrentActor = messageData4.m_MoveAbility.CurrentMovingActor</c>
    /// (Choreographer.cs:4269) for a move, <c>= m_PushAbility.TargetingActor</c>
    /// (Choreographer.cs:10013) / <c>= m_PullAbility.TargetingActor</c> (Choreographer.cs:9878) for
    /// push and pull. For a hero that IS the hero; for a SUMMON acting on its summoner's turn it is
    /// the summon, which is a <c>CActor</c> and not a <c>CPlayerActor</c>
    /// (CHeroSummonActor.cs:10) — a raw read would answer "no player is acting" and refuse to pin in
    /// precisely the situation the board is presenting the summoner's cards for.
    /// <see cref="TurnActor"/> already resolves both (summon → summoner, plus the
    /// <c>GameState.OverridingCurrentActor</c> hand-off), and it is the SAME object the card board,
    /// the ring and the keycap owner-gate are keyed on — so the pin cannot name a character the rest
    /// of the mod disagrees about.</para>
    ///
    /// <para>WHY THIS IS NOT THE 2026-08-08 RULING BEING BROKEN, but scoped. That ruling ("Ich will
    /// nie wieder eine Blockierung haben, den Character zu wechseln") was minted against a PHASE
    /// WHITELIST that refused a switch for whole phases at a time, most of them while the player was
    /// simply looking at the table, and its trigger case — "Während der Character wegen den
    /// Flitzstiefeln eine Entscheidung treffen muss, ist das Wechseln blockiert" — is a DECISION-DOCK
    /// prompt raised in <c>CheckForForgoActionActiveBonuses</c>, which is not a targeting wait state
    /// and therefore pins nothing here. This pin is open only while the player's own character is
    /// being asked WHERE TO GO or WHOM TO HIT, it lifts by itself the moment that pick resolves or is
    /// cancelled, and during it the character it pins to is the one whose confirm button the player
    /// needs. The later ruling is the narrower one and it wins inside its own window; <see cref="Open"/>
    /// / <see cref="Refusal"/> — the GLOBAL gate the 08-08 ruling is written on — is deliberately left
    /// untouched, so this is an actor-dependent refusal and not a second global one.</para>
    ///
    /// <para>REJECTED: refusing the PICK instead (a gate in the board-click path). The pick is the
    /// game's own dispatch (<c>Controller.LateUpdate</c> → <c>TileBehaviour.s_Callback</c>), it is
    /// correct, and it is the same code path a mouse click uses; refusing it would mean the mod
    /// silently dropping a legal game input, and the highlighted hexes would still be there inviting
    /// the click. Pinning the focus removes the divergence at its source instead — with the focus on
    /// the acting character the keycaps are present, so there is no state left to refuse.</para>
    ///
    /// <para>REJECTED: touching the game's highlighting. It already follows the acting character
    /// exactly as the user wants ("Das soll auch so sein"); the complaint was never about which hexes
    /// glow, only about which character the player was allowed to be looking at while they did.</para>
    ///
    /// <para>REJECTED: a BepInEx entry to disable the pin. Standing ruling — settings may configure
    /// optional content, never repair or gate a broken interaction.</para>
    ///
    /// <para>FAILS OPEN in every degenerate case (no Choreographer, no wait state, a throwing game
    /// property): answers null = "nothing is pinned". For a gate whose only power is to refuse a
    /// character switch, and against a standing ruling that switches must stay free, the safe answer
    /// is always to leave the player alone.</para>
    /// </summary>
    internal static CPlayerActor? PinnedActor
    {
        get
        {
            try
            {
                // Unity-null: a destroyed Choreographer compares equal to null.
                Choreographer? choreographer = Choreographer.s_Choreographer;
                if (choreographer == null)
                    return null;

                // (1) the board is really waiting for a pick — live game state, not our mirror.
                Choreographer.CWaitState? wait = choreographer.m_WaitState;
                if (wait == null || !VRModeStateMachine.IsTargetingState(wait.m_State))
                    return null;

                // (2) the acting SEAT is mine. The same property WorldspaceStarHexDisplay gates its
                // stars on, and a no-op offline (it returns true when !FFSNetwork.IsOnline).
                // Deliberately read here rather than through Rig.LocalTurnControl.ThisSeatActs:
                // identical predicate, but that wrapper's diagnostic announces a THUMBSTICK verdict
                // and would be actively misleading in a focus log.
                if (!choreographer.ThisPlayerHasTurnControl)
                    return null;

                // (3) and the acting CHARACTER is one of mine. Offline every merc is ours, so this
                // clause degenerates to "a player character is acting" — which is what keeps an
                // enemy's turn (CurrentPlayerActor null) from pinning anything in single player.
                CPlayerActor? acting = TurnActor;
                if (acting == null || acting.IsDead)
                    return null;
                if (!CardsGameApi.ControlsActor(acting)) // F5: partitioned list
                    return null;
                return acting;
            }
            catch (System.Exception)
            {
                // Fail open: a half-torn rules state may not cost the player their character switch.
                return null;
            }
        }
    }

    /// <summary>
    /// Does the pin refuse a switch to <paramref name="wanted"/>? False when nothing is pinned and
    /// false when <paramref name="wanted"/> IS the pinned character — re-focusing the character you
    /// are already pinned to is always allowed, and is the idempotent no-op <see cref="TryFocus"/>
    /// already treats it as.
    ///
    /// <para>Split from <see cref="PinReason"/> so the predicate allocates NOTHING: it is asked
    /// every frame by <see cref="CanFocus"/> (two keycap interlocks), and only the click seam — an
    /// edge — ever needs the sentence.</para>
    /// </summary>
    private static bool PinRefuses(CPlayerActor wanted, out CPlayerActor? pinned)
    {
        pinned = PinnedActor;
        return pinned != null && !ReferenceEquals(pinned, wanted);
    }

    /// <summary>The pin as a refusal reason, in the shape <see cref="LogRefusal"/> prints.</summary>
    private static string PinReason(CPlayerActor? pinned) =>
        $"FOCUS PIN — the board is waiting for '{Describe(pinned)}' to pick a hex (move " +
        "destination / attack target), and that decision's confirm buttons belong to that character";

    /// <summary>
    /// THE PIN, APPLIED TO AN ALREADY-LIVE FOCUS. <see cref="TryFocus"/> stops a switch INTO the
    /// window; this closes the other door — a focus that was taken BEFORE the window opened and is
    /// still pointing at the wrong character when it does.
    ///
    /// <para>Both doors are needed and neither covers the other. The reported order is "targeting
    /// opens, then the player clicks a portrait" (TryFocus refuses that), but the reverse is
    /// reachable too: look at a teammate first, then have your own character pushed into a waypoint
    /// selection by a forced move, a summon hand-off or an item. Without this the player would sit in
    /// the identical dead end, having been refused nothing.</para>
    ///
    /// <para>It is a <see cref="Clear"/> — "follow the game again" — and NOT a re-focus onto the
    /// pinned actor, because following the game IS pointing at the acting character during that
    /// window (the game presents the acting hand, and <see cref="ResolveHandCore"/> then has no
    /// override left to make read-only). Setting <see cref="_focused"/> instead would leave a live
    /// override that the very next rebuild would clear anyway ("the game now presents the focused
    /// character"), i.e. one more state for no gain.</para>
    ///
    /// <para>Called per frame from <c>FocusDriver.Tick</c> (so the ring and the board frame follow on
    /// the frame the window opens, not on the next rebuild edge) and from
    /// <see cref="ResolveHandCore"/> (so the card pipeline can never PRESENT a focus the pin forbids
    /// even if the driver is down). One method, two callers — never two copies of the rule.</para>
    /// </summary>
    internal static void EnforcePin()
    {
        // Cheap first: no override live ⇒ nothing to return, and PinnedActor is never even resolved.
        // This is the steady state on every frame of every scenario.
        if (_focused == null)
            return;
        CPlayerActor? pinned = PinnedActor;
        if (pinned == null || ReferenceEquals(pinned, _focused))
            return;

        // EDGE-ONLY BY CONSTRUCTION, with no change-guard field to keep in sync: the Clear() below
        // drops the override, so the very next call returns at the first line and this line cannot
        // repeat until a NEW override meets a NEW pin. Re-clicking the portrait does not get here
        // either — TryFocus refuses that switch outright (and rate-limits its own refusal line).
        VRLog.Info("Board", $"[Focus] FOCUS PIN engaged — the board is waiting for " +
                            $"'{Describe(pinned)}' to pick a hex, so the view was RETURNED to " +
                            $"that character from '{Describe(_focused)}'. The highlighted hexes " +
                            "and the confirm/undo keycaps belong to the same character again " +
                            "(user ModBuild 139: \"das highlighting sowie die Auswahl [darf] nur " +
                            "bei dem jeweiligen Character getroffen werden\"). The pin lifts by " +
                            "itself when the pick resolves or is cancelled.");
        Clear("FOCUS PIN — the board is waiting for this character's hex pick");
    }

    /// <summary>
    /// The LOCAL player's attention mark — the colour their own control board and their own Steam
    /// avatar wear. Green while they own the character the game is waiting on AND are looking at
    /// it; red while they own it but are looking at somebody else (the user's "wrong character"
    /// warning); none otherwise.
    ///
    /// <para>"The character the game is waiting on" is <see cref="AttentionActor"/>: the character
    /// AT TURN, or — when nobody is at turn, which is the whole of an enemy's action — the
    /// character that owes an OPEN DECISION (<see cref="DecisionOwner"/>). Both states mean one
    /// thing to the player — <b>the game is waiting on THIS character of yours</b> — so they are
    /// deliberately ONE cue rather than two competing ones; why the visual vocabulary did not have
    /// to grow with them is on <see cref="FocusTurnMark"/>.</para>
    /// </summary>
    internal static FocusTurnMark LocalMark
    {
        get
        {
            // ONE walk of the attention chain per read (it is a per-frame consumer): resolve the
            // actor once and ask the ownership question of THAT object, instead of calling
            // LocalOwnsAttention and then re-resolving for the comparison below.
            CPlayerActor? attention = AttentionActor;
            // F5: the partitioned list. This is the cue that points the player back at a decision
            // he cannot see, so a stale FALSE here is dark on his board AND on every peer's mirror
            // (Sample below must stay term-for-term identical to this line — it is).
            if (!CardsGameApi.ControlsActor(attention))
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
    /// repair that locally: the game itself hides a remote player's prompt (the
    /// <c>RefreshDamagePhase</c> → <c>ShowOtherPlayer</c> → <c>Hide</c> chain cited in the class
    /// doc's "WHAT DOES RIDE THE WIRE"), so <c>CardsGameApi.DecidingHand</c> and
    /// <c>DecisionDockSurface.PromptOwner</c> both answer null
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

    // ================================================================= A DEAD BOARD HAS NO CARDS ==
    //
    // USER, hardware 2026-09-05, item 13, verbatim: "Wenn ein Character tot ist (was ein Character
    // im Test war) sollen dort gar keine Karten mehr liegen. Im Test wurden noch die Karten
    // angezeigt die ich fuer die Phase ausgewaehlt hatte bevor ich gestorben bin."
    //
    // THE RULE, and it is absolute: a control board is ABOUT exactly one character — the one it
    // PRESENTS. If that character is exhausted, the board carries NO cards, in ANY population:
    // the two round-card recesses, the hand fan, the discard / burnt / items stacks and their
    // captions, the item chips and their fan, the active-card column and its title, the pick /
    // decision field, the docked action halves and any open browse arc. "Empty except for one of
    // those" is the same defect wearing a smaller hat.
    //
    // WHERE IT IS ENFORCED, and why HERE and nowhere else. Every one of those populations is
    // already drained by ONE branch — CardsDriver.Rebuild's `hand == null` arm
    // (CardsDriver.6.Flows.RebuildFakeOrClear) — and every one of them is fed from ONE seam, this
    // class's ResolveHand / PresentedHand pair. So the rule is a filter ON THE SEAM, not a clause
    // repeated at each surface: refuse an exhausted character's hand and the existing clear does
    // the rest. A per-surface version of this is exactly the shape that has already cost this
    // project a round (a cascade clears only what it lists, and the row outside the membership
    // test survives the whole chain).
    //
    // WHY THE HOLE WAS HERE. The class already knew the rule and applied it three times — TryFocus
    // refuses an exhausted portrait ("an exhausted character has no hand, no turn and no board
    // presence left to look at"), ResolveHandCore drops a focus whose character died, and
    // LocalFloorHand skips dead actors in BOTH of its arms. What no path tested was the
    // PASS-THROUGH: with no focus taken, ResolveHandCore returns `gameHand` unchanged, and
    // CardsGameApi.IsLocalHand — the only gate in front of it — asks about OWNERSHIP, never about
    // life. So the game's own hand for a character this client owns kept flowing onto the board
    // after that character was killed. Session evidence (ModBuild 447): the host's Mindthief
    // 'Testo' died at .planning/debug/Player.log:255544 ("MindthiefID takes 3 damage and is now at
    // -2 health", [MessageHandler] ActorDead, "Disabling card hand tab for MindthiefID") and the
    // board went on reporting "Piles: discard=2, burnt=6 for 'Testo'" and sending "Board UI SENT:
    // ... slots=3 (slot1=card, slot2=card)" for the remaining two rounds, to the session's last line.
    //
    // WHAT DELIBERATELY STAYS. The BOARD itself, and everything on it that is not a card: the
    // initiative track, the objectives panel, the element strip and the status readouts.
    // Rebuild's clear arm keeps the tray up inside a scenario for exactly that reason
    // ("the dashboard"). The report is about CARDS lying on a dead character's board, and that is
    // exactly what this removes.
    //
    // AND ONE SENTENCE THAT USED TO STAND HERE IS NOW FALSE, corrected rather than deleted because
    // it was protecting a live defect. It read: "…and the CONFIRM / rest keycaps [must not be
    // taken away here]: the confirm is party-wide whenever nobody is at turn, so hiding it on the
    // only board a just-killed player still has would deadlock the scenario." The REST half was
    // already withdrawn in the same round (Cards/Caps/RestControls.RestUiOffered). The CONFIRM half
    // is withdrawn now, and only for the CARD-SELECTION commit — see
    // PlayTray.SelectionCapRefusedByDeath for the rule, the deadlock interlock that replaces the
    // blanket refusal, and the ModBuild 461 log evidence that a corpse was offered "Auswahl
    // ändern". The party-wide continue (the enemy-information reveal's "Fortfahren", every step
    // advance outside the selection phase) is untouched and still shows on a dead character's
    // board, which is exactly what the user asked for.

    /// <summary>
    /// May the board this hand feeds carry CARDS at all? False for an EXHAUSTED character — see
    /// the block above. A null hand and a hand with no actor answer as before (there is nothing to
    /// refuse), so this can only ever turn a drawn board into an empty one, never the reverse.
    ///
    /// <para>Guarded: a mid-teardown actor reads as gone rather than throwing inside a per-frame
    /// board path — the fail-safe direction is "no cards", which is the state the user asked
    /// for.</para>
    /// </summary>
    internal static bool BoardCarriesCards(CardsHandUI? hand)
    {
        CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
        if (actor == null)
            return true;
        try { return !actor.IsDead; }
        catch { return false; }
    }

    /// <summary>
    /// Is there still a LIVING character under local control — i.e. another board of this
    /// player's on which the party-wide card-selection commit is offered?
    ///
    /// <para>THE DEADLOCK INTERLOCK for <c>PlayTray.SelectionCapRefusedByDeath</c>, and the same
    /// shape <see cref="CanFocus"/> already serves for <c>ConfirmCapsForeignView</c>: a control may
    /// only be taken off a board when the player demonstrably still has somewhere to press it.
    /// Read ONLY on the rare frames where a dead character's board would otherwise draw an
    /// un-confirmed selection commit, never per frame in the steady state.</para>
    ///
    /// <para>LIFE AND OWNERSHIP ARE ASKED SEPARATELY AND BOTH ARE ASKED. <c>PlayerActors</c> is the
    /// game's LIVING set — <c>GameState.KillActorInternal</c> → <c>CScenario.RemovePlayer</c> moves
    /// a killed character to <c>ExhaustedPlayers</c> the moment it dies, and
    /// <c>CScenario.AllPlayers</c> is the concatenation of the two — so the list is already
    /// filtered; <c>IsDead</c> is re-asked anyway because "it is in that list" is a containment
    /// statement and this needs an identity one. <c>IsUnderControlOrSingle</c> is the game's own
    /// ownership predicate (<c>CPlayerActorExtensions</c>: <c>IsUnderMyControl</c> online, TRUE
    /// offline), so a single-player seat answers for every character it drives.</para>
    ///
    /// <para>FAILS CLOSED — no scenario, no list, or an actor that throws mid-teardown all answer
    /// "no living character", which makes the caller KEEP its keycap. That is the safe direction:
    /// the worst case is the button the user asked us to remove surviving one more frame, not a
    /// player who can never end the selection.</para>
    /// </summary>
    internal static bool AnyLivingOwnedCharacter()
    {
        List<CPlayerActor>? players = ScenarioManager.Scenario?.PlayerActors;
        if (players == null)
            return false;
        for (int i = 0; i < players.Count; i++)
        {
            CPlayerActor player = players[i];
            if (player == null)
                continue;
            try
            {
                if (!player.IsDead && player.IsUnderControlOrSingle())
                    return true;
            }
            catch
            {
                // A mid-teardown actor is not a living character for this question.
            }
        }
        return false;
    }

    /// <summary>
    /// The exhausted character whose hand the last resolve REFUSED, or null. Published so the
    /// rebuild that lands in the clear arm can say WHY it is empty (the <c>EXHAUSTED BOARD</c>
    /// evidence line) instead of reporting the same "no hand" a scenario teardown produces.
    /// </summary>
    internal static CPlayerActor? ExhaustedRefusal { get; private set; }

    /// <summary>Apply <see cref="BoardCarriesCards"/> to the hand entering the seam, and latch the
    /// refusal for the diagnostic. The ONE place the rule is applied to the game's own hand.</summary>
    private static CardsHandUI? LivingHand(CardsHandUI? hand)
    {
        if (BoardCarriesCards(hand))
        {
            ExhaustedRefusal = null;
            return hand;
        }
        ExhaustedRefusal = hand!.PlayerActor;
        return null;
    }

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
    ///
    /// <para>It DOES say something about the <see cref="PinnedActor"/>, because that is not an
    /// ownership statement but a "which character is the game asking right now" one, and because
    /// this property's whole job is to predict <see cref="TryFocus"/>: its two consumers
    /// (<c>PlayTray.ConfirmCapsForeignView</c>, <c>WorldUI.ButtonCluster</c>) use it as the
    /// deadlock interlock — "the way back to the confirm button must EXIST before we take the button
    /// away" — so a true here that TryFocus would refuse is the one answer that could strand a
    /// player. Both consumers fall OPEN on false (they keep the keycap), which is the safe
    /// direction, and during a pin the presented hand IS the pinned character, so the clause they
    /// evaluate it for cannot be reached anyway.</para>
    /// </summary>
    internal static bool CanFocus(CActor? actor) =>
        IsFocusTarget(actor) && Open
        && (actor is not CPlayerActor player || !PinRefuses(player, out _));

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

        // FOCUS PIN (user ModBuild 139) — the one actor-DEPENDENT refusal, deliberately minted
        // here and NOT as a second clause of Refusal() (that gate is global and is consumed as
        // Open by the interactability bypass and the two keycap interlocks, none of which may be
        // shut just because SOME character cannot be focused this instant — the argument is on
        // PinnedActor). It goes out through the same single refusal line.
        if (PinRefuses(player, out CPlayerActor? pinned))
        {
            LogRefusal(player, PinReason(pinned));
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
    /// guess: grep the log for <c>switch REFUSED</c> and the reason is right there. Exactly TWO
    /// reasons can ever appear in that position: the card-selection phase
    /// (<see cref="SecretWindowReason"/>, the global gate) and <c>FOCUS PIN</c>
    /// (<c>PinRefusal</c>, the actor-dependent one, bounded by a live hex pick belonging to
    /// one of this player's characters). Anything else there is a regression, not a design decision.
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

    // ---------------------------------------------------------------------- AUTO-FOLLOW THE TURN --

    /// <summary>
    /// The id of the turn actor the auto-follow has already DECIDED about. Not "the last turn
    /// actor": the whole point is that the decision is taken once per hand-off and then left alone.
    /// Only ever written for a NON-NULL turn actor, so the null the game parks
    /// <c>Choreographer.m_CurrentActor</c> at between turns (Choreographer.cs:3587/3711/9398 — the
    /// enemy-information reveal among them) cannot make one turn look like two.
    /// </summary>
    private static int _followedTurnId;

    /// <summary>
    /// AUTOMATICALLY LOOK AT THE CHARACTER THAT IS NOW UP.
    ///
    /// <para>USER REPORT (3-player hardware session, verbatim): "Ich möchte, dass wenn ein Spieler
    /// neu am Zug ist er automatisch zu dem Character wechselt der gerade dran ist. Dieses Verhalten
    /// soll optional auch ausschaltbar sein in der VR Einstellungen."</para>
    ///
    /// <para>WHAT WAS MISSING. <see cref="_focused"/> is a STICKY local override, taken by a
    /// portrait click and dropped by nothing except another click, the focus pin and teardown. So a
    /// player who looked at a teammate's board during someone else's turn was still looking at it
    /// when their OWN character came up — the red "wrong character" mark
    /// (<see cref="FocusTurnMark.AtTurnWrong"/>) exists precisely to shout about that state, and
    /// until now the only way out of it was a second click.</para>
    ///
    /// <para>WHY IT IS A <see cref="Clear"/> AND NOT A <c>TryFocus(turn)</c>. With no override the
    /// board already presents the character the game is waiting on — that is what
    /// <see cref="LookingAt"/> and <see cref="LocalMark"/> both say in as many words, and what the
    /// card pipeline resolves (<c>DecidingHand() ?? ActiveHand()</c>). So "switch to the character
    /// at turn" IS the no-override state, and restoring the default is strictly weaker than
    /// installing a new override: it cannot be refused, it leaves no read-only view behind, and it
    /// keeps following the game for the rest of that turn (a mid-turn hand-off to a summon included)
    /// instead of pinning the view to one object.</para>
    ///
    /// <para>THE FOCUS-STEALING DECISION, and it is the whole design. This fires ONCE PER TURN
    /// HAND-OFF — on the edge where the character at turn CHANGES — and never again until the next
    /// one. If the player then deliberately looks at somebody else DURING their own turn, that
    /// stands: the convenience has already had its say for this turn and does not get a second one.
    /// A per-frame "put them back on the acting character" would be a switch the player cannot win
    /// against, which is worse than no convenience at all and would re-open the 2026-08-08 ruling
    /// ("Ich will nie wieder eine Blockierung haben, den Character zu wechseln") from the other
    /// side — not by refusing a switch, but by undoing it.</para>
    ///
    /// <para>ONLY EVER ONE OF THIS PLAYER'S OWN. The gate is <see cref="LocalOwnsTurn"/>
    /// (<c>CActor.IsUnderMyControl</c>, a strictly LOCAL flag — CActor.cs:751). During a teammate's
    /// turn nothing is touched, so a spectator keeps whatever they were looking at.</para>
    ///
    /// <para>MULTIPLAYER: nothing new on the wire and nothing on the game. Every input is read from
    /// this client's own state and the only write is <see cref="_focused"/> going null, exactly as a
    /// portrait click's would. Record 22 keeps carrying <see cref="LookingAt"/> on its own cadence,
    /// which after this simply names the character the peer's own replicated turn state already
    /// names. Whose turn it is is not touched, asked or influenced.</para>
    ///
    /// <para>The setting is LOCAL and pure comfort: OFF is exactly the behaviour that shipped, which
    /// is why it may be a setting at all.</para>
    /// </summary>
    internal static void FollowTurn()
    {
        if (!BoardConfig.AutoFocusOnTurn.Value)
        {
            // Forget the edge while the convenience is off, so switching it back ON mid-turn does
            // not retro-fire on a hand-off the player never asked to be followed.
            _followedTurnId = 0;
            return;
        }

        CPlayerActor? turn = TurnActor;
        if (turn == null)
            return; // between turns — not an edge, and never recorded as one (see _followedTurnId)

        int id = NetFigures.StableActorId(turn);
        if (id == _followedTurnId)
            return; // same turn as last time we decided — the player owns the view from here on
        _followedTurnId = id;

        if (FFSNetwork.IsOnline && !CardsGameApi.ControlsActor(turn)) // F5: partitioned list
        {
            VRLog.Info("Board", $"[Focus] AUTO-FOLLOW suppressed — '{Describe(turn)}' is now at " +
                                "turn but this client does not control it (the game's own " +
                                "MyControllables list does not name it), so the view was left " +
                                "exactly where the player put " +
                                $"it ('{Describe(LookingAt)}'). A teammate's hand-off never moves " +
                                "anybody else's camera.");
            return;
        }

        if (_focused == null)
        {
            VRLog.Info("Board", $"[Focus] AUTO-FOLLOW not needed — '{Describe(turn)}' is now at " +
                                "turn and the board was already following the game (no override " +
                                "live), which presents the character at turn by construction. " +
                                "Nothing was written.");
            return;
        }

        if (ReferenceEquals(_focused, turn))
        {
            VRLog.Info("Board", $"[Focus] AUTO-FOLLOW not needed — the player was already looking " +
                                $"at '{Describe(turn)}', who is now at turn. The override is left " +
                                "standing (dropping it would present the same character anyway).");
            return;
        }

        CPlayerActor? from = _focused;
        VRLog.Info("Board", $"[Focus] AUTO-FOLLOW applied — '{Describe(turn)}' is now at turn and " +
                            $"the view was on '{Describe(from)}', so the override was dropped and " +
                            "the board follows the game again (which presents the character at " +
                            "turn). ONE decision per hand-off: if the player now deliberately looks " +
                            "somewhere else during this turn, that stands until the NEXT character " +
                            "comes up — a convenience that re-took the view every frame would be one " +
                            "the player cannot win against. Local only: no rules call, no packet, " +
                            "and whose turn it is was neither asked nor changed. Switch it off with " +
                            "[Board] AutoFocusOnTurn.");
        Clear($"AUTO-FOLLOW — '{Describe(turn)}' is now at turn");
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

    /// <summary>
    /// True when <paramref name="actor"/> belongs to another player (online only — offline every
    /// merc is ours).
    ///
    /// <para>IT IS THE PRECISE INVERSE OF <c>Cards.CardsGameApi.IsLocalHand</c>, which
    /// <see cref="HandInspectable"/> has always said it is, AND IT HAS TO STAY THAT WAY: the two are
    /// read on the same rebuild — one to decide whether the fan is interactive, the other to label it
    /// "another player's character — read-only view" and to decide whether a peer's use bar is
    /// answerable (<c>WorldUI.Surfaces.UseBarsSurface</c>, <c>Net.Avatar.NetAvatarDriver</c>). If the
    /// two disagreed, the mod would call a hand foreign and hand it out anyway, or the reverse.</para>
    ///
    /// <para>So it asks the same question through the same term. <c>IsLocalHand</c> stopped reading
    /// <c>CActor.IsUnderMyControl</c> because that flag's CLEAR is unpartitioned
    /// (<c>CharacterManager.OnControlReleased</c> tests no player identity,
    /// CharacterManager.cs:488-495) while its SET is — the full derivation is on
    /// <c>Cards.CardsGameApi.IsLocalHand</c> — and this predicate had to follow it, or the inverse
    /// claim above would have become false in exactly the state the pair exists to handle.</para>
    /// </summary>
    internal static bool IsForeign(CPlayerActor? actor)
    {
        if (actor == null || !FFSNetwork.IsOnline)
            return false;
        bool byList = CardsGameApi.LocalControlsActor(actor, out bool answerable);
        return !(answerable ? byList : actor.IsUnderMyControl);
    }

    /// <summary>
    /// MAY THE PLAYER PICK A CARD OUT OF THIS HAND JUST TO LOOK AT IT? User ruling 2026-08-08:
    /// „Ich möchte das man jederzeit auch eine Karte aus der Hand nehmen kann um sie sich genau
    /// anzuschauen, auch wenn man die Karte nirgendwo ablegen kann. Das soll also niemals blockiert
    /// sein — aktuell kann man nur Karten in die Hand nehmen wenn man sie auch ablegen kann."
    ///
    /// <para>THE ENTITLEMENT RULE, in one line: <b>inspection is allowed wherever the card's FRONT
    /// may be shown at all</b> — every character the local client controls, in every phase, AND a
    /// foreign character outside the secret selection window. Offline (where every merc is ours)
    /// the answer is always yes and the feature is unconditional.</para>
    ///
    /// <para><b>THE FOREIGN HALF IS A 2026-09-07 USER RULING AND IT REVERSES THE PARAGRAPH BELOW.</b>
    /// Asked to choose, he ruled verbatim: <i>"Nur ansehen, aber auch in die Hand nehmen koennen um
    /// eine Karte besser anzuschauen — so war es bisher auch schon implementiert und abgenommen."</i>
    /// He had first written <i>"Es soll generell niemals moeglich sein Karten von einem fremden
    /// Faecher zu nehmen"</i> and then drew the line himself: the thing that must never happen is
    /// reaching into the fan hanging at ANOTHER PLAYER'S AVATAR and pulling a real card out of it -
    /// which <c>Net.Remote.RemoteHandFan</c> makes structurally impossible, since it never calls
    /// <c>VRCardFactory</c>, <c>AttachGameCard</c> or <c>CardsDriver.HookCard</c> and can therefore
    /// only ever have handed out a copy with no game widget behind it — and since 2026-09-07 it hands
    /// out NOTHING AT ALL: its slabs carry no collider anywhere in their subtree, so the proximity
    /// election has nothing to win. He ruled on the copy too ("Interessante Beobachtung: Immerhin
    /// ist es ein Klon aktuell" — noted, and still not wanted). Switching
    /// one's OWN focus to a foreign character is a different surface and a different question, and
    /// there he wants the fan shown AND handleable.</para>
    ///
    /// <para><b>THE GATE IS THE FRONT, NOT THE OWNER, AND THAT IS WHY IT IS SAFE TO WIDEN.</b> The
    /// term below is <c>ShowRoundCardFronts</c> — the same predicate the fan's own faces are drawn
    /// under — so a card that may not be READ may not be LIFTED either, and the standing ruling
    /// ("Kurze Rast = Auswahlphase = verdeckt") keeps its teeth without this file restating it.
    /// It is deliberately NOT a second copy of the phase test: one predicate, asked once.
    /// Note it is marginally conservative on a foreign BURNT card, whose front is shown in every
    /// phase by the burn exception while this gate still refuses the lift during the secret window.
    /// That is the safe direction and it is recorded rather than special-cased.</para>
    ///
    /// <para>THE PARAGRAPH THIS RULING OVERTURNED, kept because its own first sentence is the
    /// reason the reversal is cheap: it said outright that refusing a foreign hand is <b>NOT a
    /// secrecy necessity</b>, only a structural convenience. Read on — WHY NOT ALSO FOREIGN,
    /// given that a foreign HAND is not secret (the class doc's "WHAT IS
    /// AND IS NOT A DISCLOSURE" reads that off the game's own model) and the focus fan already
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
        hand != null && hand.PlayerActor != null
        // OWN HAND FIRST, so single player and every locally controlled merc keep the exact
        // pre-2026-09-07 answer with no new term in their path at all.
        && (CardsGameApi.IsLocalHand(hand)
            // A FOREIGN HAND IS INSPECTABLE EXACTLY WHEN ITS FRONTS ARE DRAWN. The release path is
            // what makes this safe and it was already load-bearing before this change:
            // CardsDriver.OnCardReleased's InspectOnly branch sits BEFORE CurrentHand() is even
            // resolved (CardsDriver.5.Interactions.cs, verified 2026-09-07), returns the card home
            // animated, and writes nothing - no SelectCard, no UnselectCard, no slot occupancy, no
            // initiative reconcile, no reorder commit. The WRONG-HAND BELT below it is a second net.
            // So widening the GRAB cannot widen what reaches a game seam; it only lets the player
            // hold a card up to his face, which is the whole of what he asked for.
            || RevealGate.ShowRoundCardFronts(hand.PlayerActor));


    // ------------------------------------------------------------------ the ownership census --

    /// <summary>Last announced census signature, so the line lands on a CHANGE and a steady board
    /// costs one string compare per rebuild. Read by nothing but <see cref="LogOwnershipCensus"/>.</summary>
    private static string? _lastOwnershipCensus;

    /// <summary>Scratch, so the census allocates no list per rebuild.</summary>
    private static readonly List<int> _claimants = new List<int>();

    /// <summary>
    /// WHO DOES THE MOD THINK OWNS THE HAND IT IS ABOUT TO DRAW, AND DOES ANYBODY ELSE CLAIM IT?
    ///
    /// <para>USER REPORT (hardware, 2026-09-07, item 10): <i>"Wenn ein Spieler einen Character
    /// ausgewählt hat den ein anderer Spieler spielt, kann ich Handkarten aus dem Fächer des
    /// Mitspielers ziehen. Das darf nicht sein. Außerdem kann er die Karten von dem Character nicht
    /// in die Hand nehmen."</i> Two symptoms, and <c>Cards.CardsGameApi.IsLocalHand</c>'s doc shows
    /// they are one broken term: <c>CActor.IsUnderMyControl</c> has a partitioned SET
    /// (<c>CharacterManager.OnControlAssigned</c> tests <c>MyPlayer.PlayerID == controller.PlayerID</c>)
    /// and an UNPARTITIONED CLEAR (<c>OnControlReleased</c> tests nothing), so after a reassignment
    /// it can read stale TRUE on a client that does not own the character and stale FALSE on the one
    /// that does — both at once.</para>
    ///
    /// <para>THE FIX IS ALREADY IN (<c>IsLocalHand</c> now asks
    /// <c>NetworkPlayer.MyControllables</c>, which partitions at both edges). THIS LINE IS THE
    /// FALSIFIER, and it exists because the 2026-09-07 session's two logs do NOT contain the
    /// duplicate: host and peer held one character each for the whole session, so nothing in the
    /// evidence showed the two terms disagreeing. So the line prints BOTH terms side by side and the
    /// full claimant list, and it is the reading that decides whether the duplicate is real, which
    /// half of it each client saw, and whether raising the term cured it.</para>
    ///
    /// <para>It is emitted from <see cref="ResolveHand"/> — the ONE seam into the card pipeline,
    /// called once per rebuild and never per frame — and only when its signature CHANGES, so a
    /// steady board is silent. Every field it names is read at the moment the fan is built for that
    /// character, which is exactly the moment the report is about.</para>
    /// </summary>
    private static void LogOwnershipCensus(CardsHandUI? presented, bool readOnly)
    {
        CPlayerActor? actor = presented != null ? presented.PlayerActor : null;
        if (actor == null)
        {
            _lastOwnershipCensus = null; // no hand: the next real one always announces itself
            return;
        }

        bool online = FFSNetwork.IsOnline;
        bool flag = actor.IsUnderMyControl;
        bool byList = Cards.CardsGameApi.LocalControlsActor(actor, out bool answerable);
        bool mine = !online || (answerable ? byList : flag);

        _claimants.Clear();
        int claimCount = Cards.CardsGameApi.ClaimantPlayerIds(actor, _claimants);
        int localId = NetPlayerActors.LocalPlayerId();

        // WHAT THE PLAYER MAY DO WITH THIS FAN, in the vocabulary the report used. `mine` is
        // CardsGameApi.IsLocalHand's own answer, which is what CardsDriver.CurrentHand and
        // HandInspectable both consume, so the verdict cannot drift from the behaviour.
        string verdict = !mine
            ? "REFUSED (foreign hand — the fan is a picture; a teammate's card is reachable only as "
              + "a lift-to-read card on YOUR OWN board after switching to that character; the "
              + "fan at their avatar hands out nothing)"
            : readOnly
                ? "own-hand READ-ONLY (a focus/floor view of one of this client's own characters)"
                : "own-hand INTERACTIVE";

        var claims = new System.Text.StringBuilder();
        if (claimCount < 0)
        {
            claims.Append("unanswerable");
        }
        else if (claimCount == 0)
        {
            claims.Append("NOBODY");
        }
        else
        {
            for (int i = 0; i < _claimants.Count; i++)
                claims.Append(i == 0 ? string.Empty : ",").Append(_claimants[i]);
        }

        // THE DUPLICATE, NAMED. More than one player id listing the same character is the state the
        // report describes, and it must be visible here without a screenshot — he has hit it once
        // and will hit it again. A claimant list that does not contain THIS client while the flag
        // says it does (or the reverse) is the disagreement that convicts the flag.
        string duplicate = claimCount > 1
            ? $" — DUPLICATE: {claimCount} players claim this character"
            : claimCount == 1 && localId != 0 && _claimants[0] != localId && flag
                ? " — DISAGREEMENT: the registry names another player while IsUnderMyControl says mine"
                : claimCount == 1 && localId != 0 && _claimants[0] == localId && !flag
                    ? " — DISAGREEMENT: the registry names THIS client while IsUnderMyControl says not mine"
                    : string.Empty;

        string signature = $"{Describe(actor)}|{online}|{flag}|{answerable}|{byList}|{claims}|"
                           + $"{localId}|{readOnly}|{duplicate.Length}";
        if (signature == _lastOwnershipCensus)
            return;
        _lastOwnershipCensus = signature;

        // HW-VERIFY
        VRLog.Note("Board",
            $"[Ownership] HAND FAN for '{Describe(actor)}': this client is player "
            + $"{(localId == 0 ? "?" : localId.ToString())}; the game's own controllable list names "
            + $"claimant(s) {claims}; CActor.IsUnderMyControl={flag}; "
            + $"MyControllables says mine={(answerable ? byList.ToString() : "unanswerable")}; "
            + $"online={online} ⇒ interaction {verdict}.{duplicate} "
            + "The list is the term the fan is gated on (Cards.CardsGameApi.IsLocalHand); the flag "
            + "is printed beside it because its clear is unpartitioned "
            + "(CharacterManager.OnControlReleased tests no player identity) and it is the term "
            + "that shipped before ModBuild 473.");
    }

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
    /// <item>…and a FOURTH, which is not a focus decision at all: whenever the three above would
    ///   answer "no hand", the SELECTION FLOOR steps in (<see cref="LocalFloorHand"/>) so the board
    ///   is never empty. That clause lives in this method rather than in the tree, because it is
    ///   about the tree's ANSWER, not about the focus.</item>
    /// </list>
    /// </summary>
    internal static CardsHandUI? ResolveHand(CardsHandUI? gameHand)
    {
        // A DEAD BOARD HAS NO CARDS (user 2026-09-05 #13 — see the block at BoardCarriesCards).
        // Applied to the GAME's hand here, at the seam, so the whole card pipeline drains through
        // the one clear arm below instead of each surface growing a death test of its own.
        gameHand = LivingHand(gameHand);
        CardsHandUI? resolved = ResolveHandCore(gameHand);
        if (resolved != null)
        {
            LogOwnershipCensus(resolved, _readOnlyView);
            // Remember what we presented, while it is still ours: the floor's first choice is
            // CONTINUITY, so a board that has to fall back falls back to the character the player
            // was already looking at rather than to whoever happens to be first in the list.
            if (PresentedActor != null && !IsForeign(PresentedActor))
                _floorActor = PresentedActor;
            _loggedFloorId = null; // the floor is not engaged — the next engagement announces itself
            return resolved;
        }

        // NOBODY SELECTED IS NOT A STATE (user, hardware ModBuild 137, finding 11) — see
        // <see cref="LocalFloorHand"/> for the whole story.
        CardsHandUI? floor = LocalFloorHand();
        if (floor == null)
        {
            // Genuinely nothing to show: no scenario, no hands built yet, or this client controls
            // no living character at all (spectator / whole party exhausted). The empty board is
            // then the truth, and SelectionOwnershipFallback documents the same terminal case.
            LogFloor(null);
            LogOwnershipCensus(null, false);
            return null;
        }

        PresentedActor = floor.PlayerActor;
        // READ-ONLY, without exception. The commit seams (confirm/undo, rests, action play, item
        // use, every pick flow) deliberately read the GAME's hand — CardsDriver.CurrentHand() —
        // which is precisely the null that got us here, so an interactive fan would offer actions
        // that resolve against no hand at all. Presenting a picture is the only honest answer, and
        // it is the same answer the class already gives every override.
        _readOnlyView = true;
        _floorActor = floor.PlayerActor;
        LogFloor(floor.PlayerActor);
        LogOwnershipCensus(floor, readOnly: true);
        return floor;
    }

    /// <summary>
    /// <see cref="ResolveHand"/>'s original body — the focus decision alone, before the selection
    /// floor is applied on top. Split out so the floor is a single, visible wrapper rather than a
    /// clause repeated at four returns.
    /// </summary>
    private static CardsHandUI? ResolveHandCore(CardsHandUI? gameHand)
    {
        PresentedActor = gameHand != null ? gameHand.PlayerActor : null;
        _readOnlyView = false;

        // FOCUS PIN, belt to FocusDriver's braces: a rebuild may never PRESENT a focus the pin
        // forbids, even if the per-frame driver is down (its tick is TickGuard-isolated and can be
        // stood down by a throwing carrier). It is the same method, so the two cannot drift.
        EnforcePin();

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

        // The UN-FLOORED twin on purpose. The floor is applied ONCE, by ResolveHand, to this
        // method's answer — asking the floored PresentedHand here would let a focus whose widget is
        // not built yet resolve to the FLOOR character and be latched as though it were the focus.
        CardsHandUI? focusHand = PresentedHandCore(gameHand);
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
    /// <para>It is <see cref="ResolveHand"/>'s decision tree, term for term, minus the mutations
    /// (that method additionally drops a now-pointless override, which is a state change and
    /// therefore stays there). <see cref="ResolveHand"/> calls THIS method for its own lookup, so
    /// the two cannot drift apart.</para>
    /// </summary>
    internal static CardsHandUI? PresentedHand(CardsHandUI? gameHand)
    {
        // The same filter ResolveHand applies, for the reason this method's own doc gives for
        // existing: the two must answer the same question, or the per-frame consumers keep
        // counting a character the rebuild has already taken off the board.
        gameHand = LivingHand(gameHand);
        CardsHandUI? resolved = PresentedHandCore(gameHand);
        // The selection floor is part of the ANSWER, so this twin has to apply it too — otherwise
        // the per-frame consumers (the pile counts) would read "no hand" for exactly the character
        // the rebuild is showing, which is the very drift this method was extracted to end.
        return resolved != null ? resolved : LocalFloorHand();
    }

    private static CardsHandUI? PresentedHandCore(CardsHandUI? gameHand)
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
    /// THE SELECTION FLOOR: a hand for a character the LOCAL client controls, so that "no character
    /// selected" cannot exist while this client has a character to select.
    ///
    /// <para>USER REPORT (hardware, ModBuild 137, finding 11): "Während dem Test ist es passiert das
    /// ich keinen Character ausgewählt hatte. Ich konnte zwar normal jemand wieder auswählen aber der
    /// Zustand NIEMANDEN ausgewählt zu haben darf nicht existieren auch nicht während dessen die
    /// Gegner dran sind."</para>
    ///
    /// <para>ROOT CAUSE, and it is a MULTIPLAYER-ONLY one. The board's character comes from the
    /// GAME's presented hand, and the game re-points that hand at whoever is ACTING — remote players
    /// included, on every client (<c>Choreographer.cs:4024/4043/4062/5586/6004</c> call
    /// <c>CardsHandManager.Show(playerActor, …)</c> → <c>SwitchHand</c> → <c>currentHand = …</c>,
    /// CardsHandManager.cs:626, with no ownership test — the ownership branch that follows only
    /// picks the ActionProcessor state). <c>CardsDriver.CurrentHand()</c> then filters that through
    /// <c>CardsGameApi.IsLocalHand</c> and answers <b>null</b> for a foreign hand
    /// (CardsDriver.2.Update.cs:944-945), and <see cref="ResolveHand"/> latched that null straight
    /// into <see cref="PresentedActor"/>. Result: from the moment a teammate's turn began until the
    /// player clicked a portrait, the control board had NOBODY on it — no fan, no piles, no
    /// selection ring (<c>FocusDriver.TickRings</c> only draws it for a non-null
    /// <see cref="LookingAt"/>), and record 22 carried actor id 0, so the peers' mirrors showed him
    /// looking at nothing either. <c>CardsHandManager.Hide()</c> only clears <c>isShown</c>
    /// (CardsHandManager.cs:899-918) — <c>currentHand</c> keeps pointing at the foreign actor
    /// through the whole enemy phase, which is exactly the "auch nicht während dessen die Gegner
    /// dran sind" half of the report.</para>
    ///
    /// <para>EVIDENCE FROM HIS SESSION. Host log <c>.planning/debug/LogOutput.log</c>: line 43057
    /// <c>[Focus] cleared (the game now presents the focused character)</c> — his own turn arrived
    /// on the character he was watching, so the override was correctly dropped and the board went
    /// back to "follow the game". Line 45066 then records the game waiting on <c>'Scream'</c>, a
    /// character "this client does not control". From there the game's hand was foreign, the board
    /// had nobody, and the state ended only at line 46037/46038, a manual portrait click:
    /// <c>[Focus] now looking at 'Cryonaris'; phase Action, at turn 'Scream'</c> — verbatim his "Ich
    /// konnte zwar normal jemand wieder auswählen".</para>
    ///
    /// <para>THE ORDER, and why it is this one. First the character we were already presenting
    /// (<see cref="_floorActor"/>) while it is still ours and alive — a floor that CHANGES which
    /// character you are looking at every time a teammate's turn starts would be its own bug.
    /// Otherwise the first local, living hand in the game's own list order, which is the exact rule
    /// two neighbours already use for the same problem: <c>CardsGameApi.LoseRewardPickHand</c>
    /// ("the presented ActiveHand when it is local, else the first local hand", written because a
    /// demand "could arrive during a REMOTE actor's turn and no local surface would exist") and
    /// <c>SelectionOwnershipFallback</c> ("first still-owned, non-dead player").</para>
    ///
    /// <para>IT NEVER PRESENTS A FOREIGN CHARACTER. Every candidate passes
    /// <c>CardsGameApi.IsLocalHand</c>, so the floor cannot disclose anything the player is not
    /// already entitled to see, in any phase — including the secret card-selection window, which is
    /// why this clause needs no gate of its own and <see cref="Refusal"/> is left alone.</para>
    ///
    /// <para>REJECTED: writing the game's own selection instead (<c>CardsGameApi.SelectActor</c> →
    /// <c>InitiativeTrack.Select</c> → <c>SwitchHand</c>), which would make the fallback fully
    /// interactive. It would fight the game every turn: <c>InitiativeTrack.UpdateInitiativeTrack</c>
    /// re-selects the CURRENT ACTOR on every turn message (InitiativeTrack.cs:618-624), so the
    /// selection would flip back and forth for as long as somebody else is acting.
    /// <c>SelectionOwnershipFallback</c> may write that seam only because it is a strict one-shot
    /// armed by a real control-release event, and its own class doc warns that this fallback "must
    /// never steal" the game's turn display. A presentation floor steals nothing.</para>
    ///
    /// <para>REJECTED: fixing it at <c>CardsDriver.CurrentHand()</c> by returning a local hand
    /// there. That method is what the COMMIT seams read (confirm/undo, rests, action play, item use,
    /// the pick flows — "USE IT FOR WHAT IS DISPLAYED, NEVER FOR WHAT IS COMMITTED",
    /// CardsDriver.2.Update.cs:953-956), so widening it would let a click resolve against a hand the
    /// game is not presenting. The floor belongs on the PRESENTATION side, which is this class.</para>
    ///
    /// <para>REJECTED: keeping the board empty but reporting the floor character on the wire, so at
    /// least the peers see something. The report is about the local board being empty; a wire-only
    /// fix would answer a question nobody asked and make the mirror disagree with the screen.</para>
    /// </summary>
    private static CardsHandUI? LocalFloorHand()
    {
        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return null;

        if (_floorActor != null && !_floorActor.IsDead)
        {
            CardsHandUI kept = manager.GetHand(_floorActor);
            if (kept != null && CardsGameApi.IsLocalHand(kept))
                return kept;
        }

        List<CardsHandUI> hands = manager.CardHandsUI;
        if (hands == null)
            return null;
        for (int i = 0; i < hands.Count; i++)
        {
            CardsHandUI hand = hands[i];
            if (hand != null && hand.PlayerActor != null && !hand.PlayerActor.IsDead
                && CardsGameApi.IsLocalHand(hand))
                return hand;
        }
        return null;
    }

    /// <summary>
    /// The floor's diagnostic — <c>SELECTION GUARD</c>, edge-only (one line per change of the
    /// character it lands on), so the next multiplayer log proves both halves: that the empty state
    /// was reached at all, and that it was filled.
    /// </summary>
    private static void LogFloor(CPlayerActor? actor)
    {
        // Outside a live scenario there is nothing to select and nothing to report — the menu is not
        // an empty board.
        if (actor == null && !CardsGameApi.InScenario)
            return;
        int id = NetFigures.StableActorId(actor);
        if (_loggedFloorId == id)
            return;
        _loggedFloorId = id;
        VRLog.Info("Board", actor != null
            ? $"SELECTION GUARD: the game presents no hand this client may drive (its own hand "
              + $"follows whoever is acting, teammates included) — holding '{Describe(actor)}' on the "
              + "board READ-ONLY instead of showing nobody. 'No character selected' is not a state "
              + "(user 2026-08-13, #11); the view returns to normal by itself the moment the game "
              + "presents one of ours again."
            : "SELECTION GUARD: the game presents no hand this client may drive AND there is no "
              + "local character to fall back to (no scenario, hands not built yet, spectator, or "
              + "the whole party is exhausted) — the empty board is the truth here.");
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
    ///   habe über eine Karte die mir das erlaubt hat." — the summon becomes the acting figure and
    ///   is not a <c>CPlayerActor</c>, so the pattern match failed and NOBODY's cards docked (the
    ///   whole chain, with its source lines, is on <see cref="TurnActor"/>);</item>
    /// <item>"Während dessen eine Bewegung oder ein Angriff bestätigt werden muss … werden die
    ///   ausgewählten Karten auf dem Controllboard nicht mehr angezeigt" — the same hole, reached
    ///   through the movement/targeting messages, which re-point the acting figure at the FIGURE
    ///   BEING MOVED or TARGETED (<c>Choreographer.cs:4269</c> move, <c>:5996</c>
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
            if (CardsGameApi.ControlsActor(attention)) // F5: partitioned list, as LocalMark
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
        // F5: MUST stay the same expression LocalMark uses — the receiver derives the mark from
        // this bit term for term, so a divergence here is a mark that never existed on this screen.
        ownsAttention = CardsGameApi.ControlsActor(attention);
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
        ExhaustedRefusal = null;
        _loggedFocusId = 0;
        _floorActor = null;    // a dead scenario's character must never be the next one's floor
        _loggedFloorId = null;
        _lastRefusal = null;
        _lastRefusedActorId = 0;
        _lastRefusalTime = float.NegativeInfinity;
        _followedTurnId = 0;   // the next scenario's first hand-off is a fresh edge
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
