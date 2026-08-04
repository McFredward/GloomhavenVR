using HarmonyLib;
using UnityEngine.EventSystems;

namespace GloomhavenVR.Cards.Patches;

/// <summary>
/// Laser half-hover suppression for the ACTION-SELECTION dock (user report 2026-08:
/// "das Highlighting wird durch die Tooltipps ... unterbrochen").
///
/// ROOT CAUSE (full chain in the <see cref="HalfSelection"/> class doc): the game's
/// half highlight lives exclusively on per-graphic <c>FullCardEventPusher</c>s —
/// <c>AddFullCardEventPusher</c> stamps one on every MaskableGraphic present at card
/// build time (FullCardEventPusherExtension.cs), and a pusher EXIT clears the half
/// highlight unconditionally (<c>FullAbilityCard.OnPointerExit →
/// OnCardHighlight(false)</c>, FullAbilityCard.cs:542-545). A laser beam drifting
/// across the card face flaps the raycast leaf between graphics; whenever the new
/// leaf's ancestor chain does not contain the half's pusher — the tooltip-bearing
/// 'Text Container' / 'Header' rows are the reproducible case, because they are the
/// graphics that also raise the mouseover tooltip — the mod pointer's uGUI-correct
/// exit walk fires the pusher exit and the highlight drops exactly while that tooltip
/// shows (hardware log Player.log:14423 — dispatched "hover EXIT: 'Bottom button'"
/// followed by content re-enter on the SAME card).
///
/// FIX SPLIT: <see cref="HalfSelection.UpdateLaserHighlight"/> drives the half
/// highlight GEOMETRICALLY from the beam's position on the docked card, and these two
/// prefixes make it the single writer by silencing the pusher for mod LASER pointer
/// events on docked cards. Scope guard is entirely inside
/// <see cref="HalfSelection.SuppressPusherEvent"/>: poke/mouse/touch pointers, cards
/// outside the visible half-selection dock, and DEFAULT-action pushers (the
/// "Attack 2"/"Move 2" chips keep their own hover FX) all run the original untouched.
/// Tooltips are NOT touched: <c>UITooltipTarget</c> components on the same graphics
/// keep receiving every pointer event, so the hints keep showing — they just can no
/// longer take the half highlight down with them.
/// </summary>
[HarmonyPatch(typeof(FullCardEventPusher), nameof(FullCardEventPusher.OnPointerEnter))]
internal static class FullCardEventPusher_Enter_LaserGeometric
{
    private static bool Prefix(FullCardEventPusher __instance, PointerEventData eventData) =>
        !HalfSelection.SuppressPusherEvent(__instance, eventData);
}

/// <summary>Exit twin of <see cref="FullCardEventPusher_Enter_LaserGeometric"/> — the
/// exit is the half that actually clears the highlight, but BOTH directions must be
/// silenced or the pusher's own enter would double the game's hover FX on top of the
/// geometric driver's.</summary>
[HarmonyPatch(typeof(FullCardEventPusher), nameof(FullCardEventPusher.OnPointerExit))]
internal static class FullCardEventPusher_Exit_LaserGeometric
{
    private static bool Prefix(FullCardEventPusher __instance, PointerEventData eventData) =>
        !HalfSelection.SuppressPusherEvent(__instance, eventData);
}

/// <summary>
/// MULTIPLAYER half-hover TAP (extras extension record 14 — user defect 2026-08-04: "Die
/// Overlay-Auswahl auf Karten beim Hovern ist nicht synchronisiert"): record every half
/// enter/exit the game itself processes, so the Net sender can name the half OUR pointer is
/// lighting. <c>FullAbilityCard.OnPointerEnter(bool)</c> is the single funnel BOTH pointer
/// paths reach — the geometric laser driver calls it directly
/// (<see cref="HalfSelection.UpdateLaserHighlight"/>) and the fingertip/mouse pusher chain
/// calls it through <c>FullCardEventPusher</c> — which is exactly why the tap sits here and
/// not on either input path. POSTFIX + read-only: the game's own guards have already run, and
/// nothing of the game's behaviour changes with the mod on. The registry keeps a card
/// REFERENCE only for slot mapping inside <see cref="HalfSelection.TrySampleLocalHover"/>;
/// no identity ever reaches the wire.
/// </summary>
[HarmonyPatch(typeof(FullAbilityCard), nameof(FullAbilityCard.OnPointerEnter))]
internal static class FullAbilityCard_Enter_HalfHoverSync
{
    private static void Postfix(FullAbilityCard __instance, bool isTopAbility) =>
        HalfSelection.NoteGameHover(__instance, isTopAbility, active: true);
}

/// <summary>Exit twin of <see cref="FullAbilityCard_Enter_HalfHoverSync"/> — clears the
/// registry entry (match-gated inside, so an out-of-order exit cannot wipe a fresh hover).</summary>
[HarmonyPatch(typeof(FullAbilityCard), nameof(FullAbilityCard.OnPointerExit))]
internal static class FullAbilityCard_Exit_HalfHoverSync
{
    private static void Postfix(FullAbilityCard __instance, bool isTopAbility) =>
        HalfSelection.NoteGameHover(__instance, isTopAbility, active: false);
}
