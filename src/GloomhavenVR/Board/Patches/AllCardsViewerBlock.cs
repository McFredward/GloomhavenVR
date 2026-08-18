using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.Patches;

// ---------------------------------------------------------------------------
// USER-BUG (MP hardware test 2026-08-07) — THE ACTION DEADLOCK.
//
// Reported: "Ich habe im Spiel auf andere Character geklickt und konnte dann
// keine Aktion mehr auf meinen Karten auswählen obwohl ich am Zug war."
//
// It was NOT the foreign-character clicks. Those were all refused correctly by
// InitiativeTrackPlayerAvatar_OnClick_Guard (SelectionGuardPatches.cs) — the
// session log carries 14 'Select REJECTED' / 'Action-phase select REJECTED'
// lines in the stuck window and not one unguarded select. The click that killed
// the turn was the one the guard is DESIGNED to let through: the portrait of the
// ACTING character himself (CardsGameApi.IsActionPhaseNonCurrentPlayerSelect
// returns false for the current actor — "re-selecting the acting actor itself is
// fine"). It is fine for the SELECT half. It is not fine for the OTHER half of
// the vanilla click:
//
//     // InitiativeTrackPlayerAvatar.cs:30-44 (decompiled, verified)
//     public override void OnClick(InitiativeTrackActorBehaviour actorUI) {
//       if (!InputManager.GamePadInUse) {
//         bool num = PhaseManager.CurrentPhase.Type
//                    != CPhase.PhaseType.SelectAbilityCardsOrLongRest || isSelected;
//         if (isSelectableByClick) base.OnClick(actorUI);          // the SELECT
//         if (num) CardsHandManager.Instance.ToggleViewAllCards(   // the LATCH
//                      actorUI.Actor as CPlayerActor);
//       }
//     }
//
// Outside the card-selection phase `num` is unconditionally true, so EVERY
// player-portrait click opens the game's screen-space All-Cards viewer, whether
// or not the select did anything. That sets
//
//     CardsHandUI.IsFullCardPreviewShowing = true            (CardsHandUI.cs:327)
//     CardsHandManager.IsFullCardPreviewShowing =>           (CardsHandManager.cs:141)
//         cardHandsUI.Any(a => a.IsFullCardPreviewShowing)
//
// and THAT is the first thing every card action click is refused on, silently,
// above the interactable / valid / phase checks:
//
//     // FullAbilityCard.cs:613-617 (decompiled, verified)
//     if (CardsHandManager.Instance.IsFullCardPreviewShowing && !isProxyAction) {
//         SimpleLog.AddToSimpleLog("... early due to CardsHandManager
//                                   IsFullCardPreviewShowing and !isProxyAction");
//         return;   // no sound, no state change, no mod-visible line
//     }
//
// WHY IT NEVER RECOVERED. FullCardHandViewer is `Singleton<FullCardHandViewer>,
// IEscapable` (FullCardHandViewer.cs:8) — it is NOT a UIWindow, and the mod's
// entire modal-rescue machinery (WorldUI.ModalFallback, the WindowVisibilityEvent
// seam) is keyed on UIWindow. So in VR the viewer is invisible, un-closable and
// unknown to the mod: it never got a floated panel, a close button, or an Escape.
// Re-clicking the same portrait cannot undo it either (CardsHandUI.cs:320
// `if (isPreviewingCards == active) return;` — the toggle is idempotent, not a
// flip). The one path that normally clears it, CloseViewAllCards() via the
// IsFullCardPreviewAllowed setter (CardsHandManager.cs:172-181), is driven by a
// turn hand-off — which can never happen while the flag blocks the action click.
// A closed loop.
//
// Hardware evidence, from the session that produced the report:
//   Player.log:384624  Added escapable All Full Card View (FullCardHandViewer)
//                      — and NEVER removed again (last removal: :291376)
//   Player.log:406887..477147  96 × "Returning from OnAbilityClick ... early due
//                      to CardsHandManager IsFullCardPreviewShowing"
//   LogOutput.log:49178..50506  ~40 dead clicks on 'Top button' / 'Bottom button'
//                      / 'Default action button', laser AND poke, zero reaction
//   LogOutput.log:40088  the mod's own gate probe, last line before the silence:
//                      "owner==current=True valid=True topInteractable=True
//                       bottomInteractable=True" — perfectly healthy, because
//                      DescribeActionGate does not model FullAbilityCard.cs:613.
//
// FIX: the All-Cards viewer has no VR representation at all — it cannot be seen,
// cannot be closed, and its side effect is an unrecoverable deadlock. Refuse to
// OPEN it while canvas conversion is active. Exact sibling of
// WorldUI.Patches.InitiativeHoverCardBlock, which already no-ops the HOVER half of
// the same problem (CardsHandManager.Preview would hoist an unscaled full-size card
// into the room); this closes the CLICK seam that patch left open.
//
// This is a RELEASE of stuck state, not a selection redesign: the select half of
// the click is untouched, every existing ownership/action-phase guard keeps its
// verdict, and with the mod off (ConversionActive false) both prefixes return
// true, so desktop play is byte-identical.
// ---------------------------------------------------------------------------

/// <summary>
/// Keeps the game's screen-space All-Cards viewer — and with it the
/// <c>IsFullCardPreviewShowing</c> latch that silently swallows every card-action
/// click (<c>FullAbilityCard.cs:613</c>) — from ever opening while VR canvas
/// conversion is active. See the file header for the full defect.
///
/// <para>TWO prefixes, deliberately, because the failure mode is an unrecoverable
/// deadlock that costs the session:</para>
/// <list type="bullet">
///   <item><see cref="BlockToggleViewAllCards"/> — the choke point EVERY opener
///   funnels through (the portrait click at InitiativeTrackPlayerAvatar.cs:40, the
///   DISPLAY_CARDS_HERO_1..4 hotkeys at CardsHandManager.cs:286, the gamepad combo
///   at :1169). Skipping the whole method also skips its
///   <c>UINavigation.StateMachine.Enter(ScenarioStateTag.AllCards)</c>, so the
///   game's nav state is never pushed either.</item>
///   <item><see cref="BlockFullCardsPreviewOpen"/> — the seam that actually SETS
///   the field (CardsHandUI.cs:327). Belt to the braces above: the latch becomes
///   unreachable by construction, no matter which caller appears in a future title
///   build. The CLOSE direction (<c>active == false</c>) is never blocked, so any
///   preview that is somehow already open can still be torn down normally.</item>
/// </list>
/// </summary>
[HarmonyPatch]
internal static class AllCardsViewerBlock
{
    /// <summary>One log line per burst — a portrait can be clicked at laser rate.</summary>
    private static float _lastLogTime = float.NegativeInfinity;

    private const float LogIntervalSeconds = 5f;

    /// <summary>
    /// Verified against the REAL GH.Runtime.dll (v1.1.8307.0), ilspycmd 8.2:
    /// <c>public void ToggleViewAllCards(CPlayerActor player, bool openByKey = false)</c>
    /// (CardsHandManager.cs:689).
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardsHandManager), nameof(CardsHandManager.ToggleViewAllCards),
        typeof(CPlayerActor), typeof(bool))]
    private static bool BlockToggleViewAllCards(CPlayerActor player)
    {
        if (!WorldUIConfig.ConversionActive)
            return true; // vanilla flat behaviour when VR is off

        Log("All-Cards viewer", player,
            "CardsHandManager.ToggleViewAllCards refused — the viewer is a screen-space "
            + "Singleton<FullCardHandViewer>/IEscapable, not a UIWindow, so VR can neither show "
            + "nor close it, while it latches IsFullCardPreviewShowing and every card-action "
            + "click is then swallowed by FullAbilityCard.cs:613 (the 2026-08-07 MP deadlock). "
            + "The portrait's SELECT half is untouched.");
        return false;
    }

    /// <summary>
    /// Verified against the REAL GH.Runtime.dll (v1.1.8307.0), ilspycmd 8.2:
    /// <c>public void ToggleFullCardsPreview(bool active, bool openByKey = false)</c>
    /// (CardsHandUI.cs:312) — the method whose <c>active</c> branch sets
    /// <c>IsFullCardPreviewShowing = true</c> (:327). Only the OPEN direction is
    /// refused; <c>active == false</c> always runs so nothing can be left stuck open.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardsHandUI), nameof(CardsHandUI.ToggleFullCardsPreview),
        typeof(bool), typeof(bool))]
    private static bool BlockFullCardsPreviewOpen(CardsHandUI __instance, bool active)
    {
        if (!active || !WorldUIConfig.ConversionActive)
            return true; // closing is always vanilla; so is everything with VR off

        Log("full-card preview", __instance != null ? __instance.PlayerActor : null,
            "CardsHandUI.ToggleFullCardsPreview(true) refused at the field seam — this is the "
            + "call that sets IsFullCardPreviewShowing, the flag that silently swallows every "
            + "card-action click until a turn hand-off clears it (which cannot happen, because "
            + "the flag is what blocks the turn). Defence in depth behind the "
            + "ToggleViewAllCards block.");
        return false;
    }

    private static void Log(string what, CPlayerActor? player, string why)
    {
        float now = Time.unscaledTime;
        if (now - _lastLogTime < LogIntervalSeconds)
            return;
        _lastLogTime = now;
        string who = player != null ? player.GetPrefabName() : "<null>";
        VRLog.Info("Board", $"[Deadlock guard] {what} suppressed for '{who}': {why}");
    }
}
