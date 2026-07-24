using GloomhavenVR.Cards;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Board.Patches;

// ---------------------------------------------------------------------------
// Action-phase selection guard (USER-BUG deadlock fix).
//
// Translated report: "During the action phase I laser-clicked ANOTHER of my
// characters who is NOT at turn. Nothing changed — but then I could no longer
// select ANY action (deadlock). Clicking an enemy plays an 'invalid' sound; I
// want the SAME when I click a non-current character."
//
// ROOT CAUSE: a human click on a PLAYER's initiative-track avatar routes
//   InitiativeTrackPlayerAvatar.OnClick(actorUI)          (:30)
//     -> base InitiativeTrackActorAvatar.OnClick(actorUI) (:154)
//       -> InitiativeTrack.Select(actorUI)                (:332)  -> sets SELECTED actor
// During the ActionSelection phase this re-points the docked control-board cards
// to the clicked actor, whose halves the game refuses to play while the owner is
// not Choreographer.CurrentActor (FullAbilityCard.cs:635). The action board then
// shows the WRONG actor's cards -> nothing is clickable -> deadlock.
//
// FIX: prefix the HUMAN player-portrait click seam ONLY and, when the click would
// select a NON-CURRENT locally-controlled player during the action phase, play the
// game's own invalid-click SFX and RETURN FALSE (skip the original entirely — no
// Select, no ToggleViewAllCards), exactly like clicking an enemy. The current actor
// stays selected so the action board remains usable.
//
// WHY THIS SEAM IS SAFE: the game's PROGRAMMATIC selects (round-start turn select in
// UpdateInitiativeTrack, character-tab switch via CardsHandTabs, the initiative-
// adjustment select in Choreographer) and the MOD's own take-damage drive
// (CardsGameApi.SelectActor -> track.Select) all call InitiativeTrack.Select DIRECTLY
// and never this avatar OnClick — so none of them are touched, and last round's
// attacked-actor selection keeps working. Enemy avatars run the base OnClick, never
// this override, and the guard ignores non-player actors anyway.
//
// Applied by BoardModule alongside the other board patches; removed collectively via
// Plugin.OnDestroy (Harmony.UnpatchSelf) for the hot-reload contract.
// ---------------------------------------------------------------------------

/// <summary>
/// Human player-portrait click gate. Verified against the REAL GH.Runtime.dll
/// (v1.1.8307.0), ilspycmd 8.2:
/// <c>public override void OnClick(InitiativeTrackActorBehaviour actorUI)</c>
/// (InitiativeTrackPlayerAvatar.cs:30) — the player override of the avatar click,
/// invoked by the avatar button's serialized UnityEvent (virtual dispatch guarantees
/// a click on a player avatar runs this override). Enemy avatars use the un-overridden
/// base at :154, so this patch never sees an enemy click.
/// </summary>
[HarmonyPatch(typeof(InitiativeTrackPlayerAvatar), nameof(InitiativeTrackPlayerAvatar.OnClick))]
internal static class InitiativeTrackPlayerAvatar_OnClick_Guard
{
    private static bool Prefix(InitiativeTrackActorBehaviour actorUI)
    {
        CActor? clicked = actorUI != null ? actorUI.Actor : null;
        if (clicked == null || !CardsGameApi.IsActionPhaseNonCurrentPlayerSelect(clicked))
            return true; // vanilla — run the original select

        CardsGameApi.RejectActionPhaseSelect(clicked);
        return false; // reject like an enemy click — keep the current actor selected
    }
}
