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
/// <para>THE PHASE GATE (hard rule, read from the game's own source). Foreign focus exists only
/// while <c>PhaseManager.PhaseType</c> is one of the TURN phases
/// <see cref="ActionPhases"/> = <c>StartTurn, ActionSelection, Action, EndTurn, EndTurnLoot</c>
/// (<c>CPhase.cs:10-26</c>; the game's own "inside somebody's turn" test is the ordinal range
/// <c>StartTurn..EndTurn</c>, <c>CAbilityRequirements.cs:181</c>). During
/// <c>SelectAbilityCardsOrLongRest</c> — and during <c>MonsterClassesSelectAbilityCards</c> —
/// every player sees only what vanilla shows them. That is belt AND braces with
/// <see cref="RevealGate"/>, which independently keeps a foreign character's ROUND cards face
/// DOWN through the secret window.</para>
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
    /// <summary>
    /// The phases in which free character focus is allowed — "somebody is taking a turn".
    /// Read from <c>CPhase.PhaseType</c> (CPhase.cs:10-26). <c>StartTurn..EndTurn</c> is the
    /// game's own "inside a turn" range (CAbilityRequirements.cs:181); <c>EndTurnLoot</c> is the
    /// loot step spliced into the same turn and is included so a focus does not blink out for a
    /// frame when somebody picks up coins.
    /// </summary>
    private static readonly CPhase.PhaseType[] ActionPhases =
    {
        CPhase.PhaseType.StartTurn,
        CPhase.PhaseType.ActionSelection,
        CPhase.PhaseType.Action,
        CPhase.PhaseType.EndTurn,
        CPhase.PhaseType.EndTurnLoot,
    };

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

    // ---------------------------------------------------------------------------- phase gate --

    /// <summary>
    /// True while free character focus is allowed at all: a live scenario, a turn phase, and NOT
    /// the secret selection window. <see cref="RevealGate.IsSecretSelectionPhase"/> is asserted
    /// separately from the phase list on purpose — it is the anti-cheat linchpin and must remain
    /// readable as its own, independent refusal.
    /// </summary>
    internal static bool Open
    {
        get
        {
            if (!CardsGameApi.InScenario || RevealGate.IsSecretSelectionPhase)
                return false;
            CPhase.PhaseType phase = PhaseManager.PhaseType;
            for (int i = 0; i < ActionPhases.Length; i++)
            {
                if (ActionPhases[i] == phase)
                    return true;
            }
            return false;
        }
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
    /// May <paramref name="actor"/> be focused right now? A live player character, in the party,
    /// during a turn phase. Deliberately says nothing about OWNERSHIP: focusing a teammate is the
    /// whole feature, and focusing one of your OWN characters that is not at turn is the state
    /// the red warning exists for.
    /// </summary>
    internal static bool CanFocus(CActor? actor)
    {
        if (!Open || actor is not CPlayerActor player || player.IsDead)
            return false;
        // The hand widget must exist on THIS client, or there would be nothing to render. The
        // game builds one per player actor (Choreographer.cs:925/1112), so this is a liveness
        // check, not an ownership one.
        CardsHandManager manager = CardsHandManager.Instance;
        return manager != null && manager.GetHand(player) != null;
    }

    /// <summary>
    /// Focus <paramref name="actor"/>. Returns false — and changes nothing — when the phase gate
    /// or the liveness check refuses. Purely local: no game state is written, no packet is sent
    /// from here (the sender samples <see cref="PresentedActorId"/> on its own cadence).
    /// </summary>
    internal static bool TryFocus(CActor? actor)
    {
        if (!CanFocus(actor))
            return false;
        var player = (CPlayerActor)actor!;
        if (ReferenceEquals(_focused, player))
            return true; // already focused — idempotent, and never logs twice
        _focused = player;
        // The game raises no event for a mod-side focus, so the card board must be told.
        Cards.CardsDriver.RequestRebuild();
        VRLog.Info("Board", $"[Focus] now looking at '{Describe(player)}'" +
                            $"{(IsForeign(player) ? " (another player's character — read-only view)" : "")}" +
                            $"; phase {PhaseManager.PhaseType}.");
        return true;
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

        if (!Open)
        {
            Clear($"phase {PhaseManager.PhaseType}");
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
    /// Does the mod's ROUND-CARD dock apply to <paramref name="hand"/>? Vanilla's answer
    /// (<c>CardsGameApi.IsActionTurn</c>) folds in an ownership gate, because docking a foreign
    /// character's played cards used to be meaningless. Under a read-only focus it is precisely
    /// what the user asked to see ("which cards that character played, shown on the board"), so
    /// the ownership half is dropped and only the "it is genuinely this character's turn" half
    /// remains — the cards are still shown ONLY when the game itself has them on the table, and
    /// <see cref="RevealGate"/> independently keeps them face down through the secret window.
    /// </summary>
    internal static bool ViewActionTurn(CardsHandUI? hand)
    {
        if (hand == null || hand.PlayerActor == null)
            return false;
        if (!_readOnlyView)
            return CardsGameApi.IsActionTurn(hand); // vanilla path, unchanged
        return ReferenceEquals(TurnActor, hand.PlayerActor);
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
