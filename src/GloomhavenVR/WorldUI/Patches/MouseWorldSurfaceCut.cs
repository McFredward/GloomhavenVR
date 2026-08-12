using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// WORLD-SPACE SURFACES ANSWER ONLY THE MOD'S OWN POINTERS — the general cut of the
/// head-swept mouse ray (hardware test 2026-08-11, verbatim: "Im Pausenmenu kann es
/// vorkommen, dass tabs mit der Kopfbewegung ausgewählt bzw. gehighlighted werden als
/// wäre der laser darüber gegangen. Entferne der maus komplett aus dem Spiel und sorge
/// auch dafür, dass meine Kopfbewegungen niemals etwas auswählen von den flat menus,
/// sondern nur der Laser.").
///
/// <para>ROOT CAUSE — THE SAME DEFECT AS <see cref="Cards.CardFaceRaycaster"/> (task #5)
/// AND <see cref="TooltipRaiseGuard"/> (2026-08-09), ONE SURFACE FAMILY LATER. The
/// game's own EventSystem mouse pointer runs EVERY frame at the PARKED virtual-mouse
/// pixel: <c>InControlInputModule.Process → ProcessMouseEvent → GetMousePointerEventData</c>
/// (InControlInputModule.cs:234-248) reads <c>InputSystemUtilities.GetMousePosition()</c>
/// — the last position the laser warped the virtual mouse to, NOT anything the player
/// is aiming now — and its <c>EventSystem.RaycastAll</c> reaches every enabled
/// <see cref="GraphicRaycaster"/>. Converted panels (the floated pause/ESC menu, the
/// options window, dialogs, the control board's docked surfaces) are WORLD-SPACE
/// canvases whose <c>worldCamera</c> is the HEAD camera, so a FIXED screen pixel
/// through a MOVING head camera is a world ray that sweeps across the world-fixed menu
/// on its own: every tab it crosses gets a genuine <c>pointerEnter</c> → hover
/// highlight, exactly "als wäre der Laser darüber gegangen". The physical desktop
/// mouse is NOT the culprit — it is already disabled while VR runs
/// (<see cref="VirtualMouse"/>.TickSuppressPhysicalMice, [WorldUI] SuppressPhysicalMouse,
/// default on); what remained was the game's own pointer machinery re-interpreting the
/// PARKED pixel against world geometry each frame.</para>
///
/// <para>THE FIX IS AT THE RAYCAST, ONCE, FOR ALL WORLD SURFACES. The two prior fixes
/// each cut one surface family (card faces via a raycaster subclass, tooltips via the
/// raise seam); the pause-menu tabs proved the defect regenerates per family, so this
/// patch cuts it at the shared source instead: a postfix on
/// <c>EventSystem.RaycastAll</c> strips every hit whose raycaster sits on a WORLD-SPACE
/// root canvas whenever the event does NOT come from a mod pointer
/// (<see cref="UguiPointer.IsModPointerId"/> — poke -101/-102, laser -111/-112,
/// FlatScreen drag/scroll -120/-121). A surface that exists in the ROOM can only be
/// pointed at by something that aims in the room: the laser or the fingertip.</para>
///
/// <para>WHAT KEEPS WORKING, AND WHY:</para>
/// <list type="bullet">
/// <item><description>The mod's laser/poke pipeline on converted panels never passes
/// through <c>EventSystem.RaycastAll</c> at all — <see cref="UguiPointer.TryRaycast"/>
/// drives each canvas's <see cref="GraphicRaycaster"/> DIRECTLY and dispatches via
/// <c>ExecuteEvents</c> with mod pointer ids. Untouched by construction.</description></item>
/// <item><description>The FlatScreen path (laser → <see cref="VirtualMouse"/> warp →
/// game input module) targets SCREEN-SPACE canvases; their root render mode is not
/// WorldSpace, so hover/click/tooltips on the floating 2D screen are untouched. The
/// flat screen's own <c>DirectClick</c>/hover RaycastAlls (pointer id 0) can now no
/// longer be intercepted by a world panel that happens to project onto the same
/// pixel — strictly more correct.</description></item>
/// <item><description>The pause-menu X double-duty flow (NonDominantHold PressId +
/// EscMenuInputBlock) is BUTTON-driven (KeyAction edges), not pointer-raycast-driven —
/// unaffected. The corner-X close is a laser click through UguiPointer — unaffected.</description></item>
/// <item><description>Gamepad/keyboard UI navigation (<c>SendVectorEventToSelectedObject</c>,
/// submit/cancel) never raycasts — unaffected.</description></item>
/// </list>
///
/// <para>REVERSIBLE / VANILLA-SAFE: gated on <see cref="WorldUIConfig.ConversionActive"/> —
/// no conversion ⇒ no world-space UI ⇒ the postfix returns immediately and desktop play
/// is byte-identical. Nothing is mutated beyond the per-call result list; an emptied
/// list is exactly "mouse over nothing" to <c>PointerInputModuleExtended</c>
/// (<c>FindFirstRaycast</c> yields a null-object result, ProcessMove exits any stale
/// hover once and stays idle).</para>
/// </summary>
[HarmonyPatch]
internal static class MouseWorldSurfaceCut
{
    /// <summary>Latched (once per session): the required proof line naming what was neutralized.</summary>
    private static bool _loggedCut;

    /// <summary>
    /// Postfix on <c>public void EventSystem.RaycastAll(PointerEventData, List&lt;RaycastResult&gt;)</c>
    /// (UnityEngine.UI, stable public API; far too large to inline). Runs once per game
    /// pointer per frame; the loop is allocation-free and the canvas resolve is two
    /// component lookups per (rare) world-space hit.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(EventSystem), nameof(EventSystem.RaycastAll))]
    private static void StripWorldHitsFromGamePointers(PointerEventData eventData,
        List<RaycastResult> raycastResults)
    {
        if (!WorldUIConfig.ConversionActive)
            return; // VR off / nothing converted — vanilla flat behaviour
        if (eventData == null || raycastResults == null || raycastResults.Count == 0)
            return;
        // A mod pointer is a real aimed pointer (laser, fingertip, flat-screen drag/scroll).
        // Everything else — the game's mouse (-1..-3), touch (>= 0), and any synthesized
        // id-0 re-dispatch — is not aimed by anybody in VR.
        if (UguiPointer.IsModPointerId(eventData.pointerId))
            return;

        for (int i = raycastResults.Count - 1; i >= 0; i--)
        {
            BaseRaycaster module = raycastResults[i].module;
            if (module is not GraphicRaycaster)
                continue; // physics raycasters are the board's business (BoardPick patches)
            // Root canvas decides: a converted window keeps the game's own NESTED canvases,
            // whose effective render mode is inherited from the world-space host that draws
            // them (same resolve as TooltipRaiseGuard.IsOnWorldSurface).
            Canvas? canvas = module.GetComponent<Canvas>();
            Canvas? root = canvas != null ? canvas.rootCanvas : null;
            if (root == null || root.renderMode != RenderMode.WorldSpace)
                continue;

            if (!_loggedCut)
            {
                _loggedCut = true;
                // Info, not Debug (BepInEx's default disk config drops Debug): the one latched
                // line the next hardware log needs to prove the cut is live.
                VRLog.Info("WorldUI",
                    $"MOUSE CUT: game pointer id {eventData.pointerId} raycast the WORLD-SPACE " +
                    $"canvas '{root.name}' — hit stripped. The game's mouse pointer sits at a " +
                    "PARKED screen pixel that sweeps a world ray whenever the HEAD moves; from " +
                    "now on world-space surfaces (floated pause menu/options/dialogs, docked " +
                    "board panels) answer ONLY the mod's laser/poke pointers. Screen-space UI " +
                    "(flat screen) keeps the laser-driven virtual mouse. Further strips are not " +
                    "logged (steady state).");
            }
            raycastResults.RemoveAt(i);
        }
    }
}
