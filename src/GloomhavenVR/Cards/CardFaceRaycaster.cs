using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// Task #5 (card-area highlights followed HEAD movement): the FaceCanvas raycaster
/// that answers ONLY the mod's own hand pointers.
///
/// Root cause (verified against the decompiled assemblies, 2026-07-22): the game's
/// half-/area-hover FX on a card is driven EXCLUSIVELY by uGUI pointer enter/exit —
/// <c>FullCardEventPusher.OnPointerEnter/Exit</c> → <c>FullAbilityCard.OnPointerEnter /
/// OnDefaultPointerEnter</c> → <c>CardsActionControlller.OnCardHighlight</c> →
/// <c>ToggleHighlightHover</c> (FullCardEventPusher.cs:29-85, FullAbilityCard.cs:516-575).
/// It does NOT read <c>InputManager.CursorPosition</c>, so the Board pick patches were
/// innocent. The head coupling came from the game's OWN EventSystem mouse pointer:
/// <c>InControlInputModule.Process → ProcessMouseEvent</c> runs EVERY frame
/// (InControlInputModule.cs:234-248) and its <c>GetMousePointerEventData</c> position is
/// the PARKED virtual/hardware mouse pixel (<c>InputSystemUtilities.GetMousePosition</c>,
/// PointerInputModuleExtended.cs:269-307 — NOT our patched CursorPosition). Its
/// <c>EventSystem.RaycastAll</c> reaches every enabled <see cref="GraphicRaycaster"/>,
/// including each VRCard's world-space FaceCanvas whose <c>worldCamera</c> is the HEAD
/// camera — so a FIXED pixel through the MOVING head camera sweeps a world ray across
/// the fan/docked cards, firing enter/exit on the pushers purely from head motion.
///
/// Fix: filter by pointer id. The mod's laser/poke pointers (<c>UguiPointer</c>:
/// poke -101/-102, laser -111/-112 — see <see cref="Hands.Interact.UguiPointer.IsModPointerId"/>)
/// raycast this canvas directly with their own <c>PointerEventData</c> and keep full
/// hover + click behavior; the game's mouse pointer (-1..-3) and any touch pointer
/// (&gt;= 0) get an empty result, so the head-swept ray can neither hover nor click a
/// card. With no laser on any card, NO card shows area hover — exactly the required
/// laser-only behavior. Board hover, tooltips and melee-AoE facing are untouched:
/// this changes no cursor code, only which pointers this one canvas answers.
/// </summary>
internal sealed class CardFaceRaycaster : GraphicRaycaster
{
    public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
    {
        // The id test is <see cref="Hands.Interact.UguiPointer.IsModPointerId"/> — the ONE
        // definition of "this event is ours", read rather than mirrored so the ceiling cannot
        // drift from the id block that produces it.
        if (eventData == null || !Hands.Interact.UguiPointer.IsModPointerId(eventData.pointerId))
            return; // game mouse/touch pointer — this card face is invisible to it
        base.Raycast(eventData, resultAppendList);
    }
}
