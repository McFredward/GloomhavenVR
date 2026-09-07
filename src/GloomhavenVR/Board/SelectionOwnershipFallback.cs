using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// MP test item #8b — reassignment fallback. When the HOST reassigns the character this
/// client currently has SELECTED to another player, the local selection must not keep
/// pointing at a character we no longer control: fall back to the first character still
/// under local control, or — when none is left — enter the clean no-character-selected
/// state the game itself uses.
///
/// TRIGGER (event-driven, deliberately NOT a poll): the game's own in-scenario control
/// release seam — <c>CharacterManager.OnControlReleased</c> (CharacterManager.cs:489-495,
/// invoked by FFSNet when a NetworkControllable leaves <c>MyControllables</c>; it is the
/// method that flips <c>CActor.IsUnderMyControl</c> to false). A poll on
/// <c>IsUnderMyControl</c> was rejected: the flag is ALSO toggled transiently by the SRL's
/// control-ability bookkeeping (GameState.cs:3441-3586, mind-control / OverrideActorForTurn),
/// and a poll would fight those legitimate mid-action overrides. The release seam fires
/// exclusively for real network control changes.
///
/// The Harmony hook (<see cref="Patches.CharacterManager_OnControlReleased_Fallback"/>) only
/// ARMS this class; the reaction runs from <see cref="Tick"/> (BoardDriver.Update,
/// TickGuard-isolated) on a LATER frame, so the game's own handler for the same event —
/// <c>CardsHandManager.OnMyControllableOwnershipChanged</c> (CardsHandManager.cs:1208), which
/// during the card phase deselects and re-points hands itself — has fully run first and we
/// only correct what it left wrong.
///
/// WHAT COUNTS AS "left wrong" (checked once per armed release, next frame):
/// the selected actor is a player NOT under local control, AND either
///   (a) it IS one of the just-released characters (any phase — the direct reassignment), or
///   (b) we are in <c>SelectAbilityCardsOrLongRest</c> (the game's own card-phase fallback
///       loop, CardsHandManager.cs:1237, re-selects the FIRST non-dead hand with no ownership
///       check, so after a reassignment it can land on a foreign character).
/// Clause (b) is phase-fenced on purpose: during Action/ActionSelection the game legitimately
/// selects REMOTE actors for turn display (e.g. Choreographer.cs:11681 selects the
/// initiative-adjusting actor, its own phase; UpdateInitiativeTrack selects the acting actor),
/// and this fallback must never steal that — a one-shot armed by MY control release, fenced to
/// the card phase for the not-the-released-actor case, cannot.
///
/// ACTIONS (both are pure UI selection, the exact seams the game itself uses — no rules or
/// ownership mutation, MP-safe):
/// - fall back: first still-owned, non-dead player in initiative-track player order
///   (<c>InitiativeTrack.PlayersUI</c>) via <see cref="CardsGameApi.SelectActor"/> —
///   <c>InitiativeTrack.Select(actorUI)</c>, whose <c>avatar.Select()</c> override also
///   switches the hand exactly like a portrait click (InitiativeTrackPlayerAvatar.cs:21-28);
/// - none owned: <c>SelectedActor()?.Deselect()</c> +
///   <c>Choreographer.ClearHilightedActors()</c> — verbatim the no-selection state
///   <c>CardsHandManager.OnMyControllableOwnershipChanged</c> establishes
///   (CardsHandManager.cs:1223-1225).
///
/// Offline / single-player: <c>OnControlReleased</c> only fires in FFSNet sessions, and
/// <see cref="Tick"/> additionally bails on <see cref="CardsGameApi.OwnershipGuardActive"/> —
/// byte-identical vanilla behavior when not in a real MP session.
/// </summary>
internal static class SelectionOwnershipFallback
{
    /// <summary>Characters released from local control this event batch (a host reassigning
    /// several characters at once fires one release each).</summary>
    private static readonly List<CPlayerActor> _released = new List<CPlayerActor>();

    private static int _armedFrame;

    /// <summary>
    /// DOES THIS CLIENT OWN <paramref name="player"/>? The same term the whole card funnel now runs
    /// on — <c>CardsGameApi.IsLocalHand</c>'s <c>NetworkPlayer.MyControllables</c> question, with
    /// <c>CActor.IsUnderMyControl</c> kept only for the cases the registry cannot answer.
    ///
    /// <para>IT MATTERS MOST HERE, of all places. This class reacts to a control RELEASE, and the
    /// release is exactly the edge at which the flag goes wrong: <c>CharacterManager.OnControlReleased</c>
    /// (CharacterManager.cs:488-495) clears it with no test of WHICH player the release concerned,
    /// while its counterpart <c>OnControlAssigned</c> sets it only for the matching player. So on the
    /// client that has just been GIVEN the character the flag can read false, and reading it here
    /// would make this fallback deselect the player's own, newly-assigned character and then report
    /// that he has none left — turning a stale bit into the reported "he cannot take the cards of
    /// that character into his hand at all". Asking the list instead cannot do that: the list is
    /// what <c>AssignControllable</c> just added him to.</para>
    /// </summary>
    private static bool LocallyOwned(CPlayerActor player)
    {
        bool byList = CardsGameApi.LocalControlsActor(player, out bool answerable);
        return answerable ? byList : player.IsUnderMyControl;
    }

    /// <summary>Called from the OnControlReleased postfix — arm a next-frame check.</summary>
    public static void Arm(CPlayerActor released)
    {
        if (!_released.Contains(released))
            _released.Add(released);
        _armedFrame = Time.frameCount;
        VRLog.Info("Board", $"[Ownership] control of '{CardsGameApi.ActorLabel(released)}' released " +
                            "— selection fallback armed for next frame.");
    }

    /// <summary>Hot-reload hygiene (BoardModule.Shutdown).</summary>
    public static void Reset() => _released.Clear();

    /// <summary>Per-frame from <see cref="BoardDriver"/> (TickGuard step "Board.OwnershipFallback").</summary>
    public static void Tick()
    {
        // One-shot with a ≥1-frame delay: the game's own ownership handlers for the same
        // network event must finish before we judge what selection they left behind.
        if (_released.Count == 0 || Time.frameCount <= _armedFrame)
            return;

        bool wasReleased;
        try
        {
            if (!CardsGameApi.OwnershipGuardActive())
                return; // session ended between arm and tick — nothing to correct

            CActor? selected = CardsGameApi.SelectedActor();
            if (!(selected is CPlayerActor selectedPlayer) || LocallyOwned(selectedPlayer))
                return; // selection is fine (or not a player) — nothing to do

            wasReleased = _released.Contains(selectedPlayer);
            if (!wasReleased && PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)
                return; // foreign selection outside the card phase that we did not cause —
                        // the game's own turn display owns it (see class doc, clause fence)
        }
        finally
        {
            _released.Clear(); // consume the arm in every path — strictly one-shot
        }

        // Fall back to the first character still under local control, in track order.
        InitiativeTrack track = InitiativeTrack.Instance;
        if (track == null)
            return;
        List<InitiativeTrackPlayerBehaviour> players = track.PlayersUI;
        for (int i = 0; i < players.Count; i++)
        {
            InitiativeTrackPlayerBehaviour beh = players[i];
            if (beh != null && beh.Actor is CPlayerActor mine
                && LocallyOwned(mine) && !mine.IsDead
                && CardsGameApi.SelectActor(mine))
            {
                VRLog.Info("Board", "[Ownership] selected character was reassigned to another player " +
                    $"— fell back to still-owned '{CardsGameApi.ActorLabel(mine)}'" +
                    (wasReleased ? "." : " (game's card-phase fallback had landed on a foreign character)."));
                return;
            }
        }

        // No owned character left — the clean no-selection state, verbatim the game's own
        // (CardsHandManager.OnMyControllableOwnershipChanged, CardsHandManager.cs:1223-1225).
        track.SelectedActor()?.Deselect();
        Choreographer.s_Choreographer?.ClearHilightedActors();
        VRLog.Info("Board", "[Ownership] selected character was reassigned and no local character " +
                            "remains — cleared selection (game's own no-selection state).");
    }
}
