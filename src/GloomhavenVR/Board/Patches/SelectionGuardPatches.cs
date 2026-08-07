using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.PopupStates;

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
        if (clicked == null)
            return true; // vanilla

        // FREE CHARACTER FOCUS (feature): during a TURN phase a portrait click is a VIEW
        // request — "show me that character's hand, piles and played cards". CharacterFocus
        // records it and the card pipeline presents that character READ-ONLY; the phase gate
        // lives in CharacterFocus.CanFocus, so this call is simply false during card selection
        // and every path below then behaves exactly as it did before the feature.
        //
        // WHAT IS DELIBERATELY *NOT* RELAXED: the two `return false`s stay. Vanilla's OnClick has
        // two halves (InitiativeTrackPlayerAvatar.cs:30-44) — the select, and an unconditional
        // CardsHandManager.ToggleViewAllCards that latches IsFullCardPreviewShowing and, in VR,
        // deadlocks every subsequent card action (see Board/Patches/AllCardsViewerBlock.cs, which
        // blocks that latch independently). Suppressing the whole original is therefore both the
        // safe answer AND the correct one: the focus is a MOD-side view, so none of vanilla's
        // click side effects — SwitchHand, ClearHilightedActors, ActorBehaviour.SetHilighted,
        // CameraController.SmartFocus, the All-Cards viewer — must fire for a character the
        // player is merely LOOKING at. Focusing writes no game state at all.
        bool focused = CharacterFocus.TryFocus(clicked);

        // MP test item #8a: a portrait of a character ASSIGNED TO ANOTHER PLAYER —
        // refuse the whole click (select AND ToggleViewAllCards). Only active online with
        // >1 participant (OwnershipGuardActive). With the focus taken the click is no longer a
        // denial, so the denied SFX is skipped: something DID happen, it just was not a select.
        if (CardsGameApi.IsForeignControlledSelect(clicked))
        {
            if (!focused)
                CardsGameApi.RejectForeignSelect(clicked);
            return false;
        }

        if (!CardsGameApi.IsActionPhaseNonCurrentPlayerSelect(clicked))
            return true; // vanilla — run the original select (this is the ACTING character)

        if (!focused)
            CardsGameApi.RejectActionPhaseSelect(clicked);
        return false; // reject like an enemy click — keep the current actor selected
    }
}

// ---------------------------------------------------------------------------
// MP ownership select guard (MP test item #8a) — board-miniature seam.
//
// In VR a laser/poke click on a character's MINIATURE (or its hex) is dispatched
// through the game's own click path (Controller.LateUpdate → TileBehaviour.s_Callback
// → Choreographer.TileHandler, see BoardClickDriver), and during the card-selection
// wait state TileHandler's select branch has NO ownership check: it selects ANY
// player found on the clicked tile and switches the hand to it. Online, that lets
// a VR player select a character assigned to another player. The initiative-track
// PORTRAIT seam is guarded above; this guard closes the miniature seam.
//
// WHY NOT InitiativeTrack.Select(CPlayerActor): that overload is also the game's
// PROGRAMMATIC select for remote actors (CheckForInitiativeAdjustments,
// Choreographer.cs:11681, selects the initiative-adjusting actor on every client)
// — a wholesale ownership prefix there would break remote-turn display. TileHandler
// is exclusively the tile CLICK dispatch, so the guard sits on the human seam only.
// ---------------------------------------------------------------------------

/// <summary>
/// Verified against the REAL GH.Runtime.dll (v1.1.8307.0), ilspycmd 8.2:
/// <c>public void TileHandler(CClientTile clientTile, List&lt;CTile&gt; optionalTileList = null,
/// bool networkActionIfOnline = false, bool isUserClick = false,
/// bool actingPlayerHasSecondClickConfirmationEnabled = false)</c> (Choreographer.cs:1811).
/// Its select branch (Choreographer.cs:1826-1840): wait state <c>WaitingForCardSelection</c>
/// &amp;&amp; <c>CardsHandManager.IsActive()</c> &amp;&amp; <c>FindPlayerAt(tile)</c> != null
/// &amp;&amp; not in <c>ConfirmationBoxState</c> → <c>InitiativeTrack.Select(player)</c> +
/// <c>SwitchHand</c> + tile-click SFX, then RETURN — unconditionally, so when this prefix
/// replicates exactly those entry conditions and the found player is foreign-controlled,
/// skipping the whole original (return false) removes ONLY the select; every other
/// TileHandler branch is unreachable in that condition set anyway.
/// </summary>
[HarmonyPatch(typeof(Choreographer), nameof(Choreographer.TileHandler))]
internal static class Choreographer_TileHandler_OwnershipGuard
{
    private static bool Prefix(Choreographer __instance, CClientTile clientTile)
    {
        try
        {
            if (__instance == null || clientTile == null || clientTile.m_Tile == null)
                return true;
            if (__instance.m_WaitState == null
                || __instance.m_WaitState.m_State != Choreographer.ChoreographerStateType.WaitingForCardSelection)
                return true;
            CardsHandManager hands = CardsHandManager.Instance;
            if (hands == null || !hands.IsActive())
                return true;
            if (ScenarioManager.Scenario == null)
                return true;
            CPlayerActor? clicked = ScenarioManager.Scenario.FindPlayerAt(clientTile.m_Tile.m_ArrayIndex);
            if (clicked == null)
                return true;
            UINavigation nav = Singleton<UINavigation>.Instance;
            if (nav != null && nav.StateMachine.IsCurrentState<ConfirmationBoxState>())
                return true; // vanilla would not select either — keep byte-identical fallthrough
            if (!CardsGameApi.IsForeignControlledSelect(clicked))
                return true; // own character / offline / solo session — vanilla

            CardsGameApi.RejectForeignSelect(clicked);
            return false; // skip the select entirely — local selection untouched
        }
        catch (System.Exception e)
        {
            // A throwing guard must never eat the game's click dispatch.
            VRLog.Warn("Board", $"TileHandler ownership guard threw — passing click through: {e.Message}");
            return true;
        }
    }
}

// ---------------------------------------------------------------------------
// MP reassignment fallback trigger (MP test item #8b) — see
// Board/SelectionOwnershipFallback.cs for the full design. This patch only ARMS
// the fallback; the reaction runs from BoardDriver.Update on a later frame.
// ---------------------------------------------------------------------------

/// <summary>
/// Verified against the REAL GH.Runtime.dll (v1.1.8307.0), ilspycmd 8.2:
/// <c>public void OnControlReleased()</c> (CharacterManager.cs:488) — the in-scenario
/// seam FFSNet invokes when this client loses control of a character (host reassign /
/// drop); its body flips <c>CharacterActor.IsUnderMyControl</c> to false only when it
/// was true. Parameterless, so no Bolt type is referenced (the paired
/// <c>OnControlAssigned(NetworkPlayer)</c> is deliberately NOT patched — its parameter
/// is a Bolt-derived type and requirement #8b only needs the release edge).
/// Prefix records whether the character was locally controlled; postfix arms the
/// fallback only for a real mine→foreign transition.
/// </summary>
[HarmonyPatch(typeof(CharacterManager), nameof(CharacterManager.OnControlReleased))]
internal static class CharacterManager_OnControlReleased_Fallback
{
    private static void Prefix(CharacterManager __instance, out bool __state)
    {
        __state = __instance != null
                  && __instance.CharacterActor is CPlayerActor player
                  && player.IsUnderMyControl;
    }

    private static void Postfix(CharacterManager __instance, bool __state)
    {
        if (!__state || __instance == null)
            return;
        if (__instance.CharacterActor is CPlayerActor player && !player.IsUnderMyControl)
            SelectionOwnershipFallback.Arm(player);
    }
}
