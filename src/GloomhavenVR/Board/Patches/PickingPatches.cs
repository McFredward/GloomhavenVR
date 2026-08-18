using GloomhavenVR.Hands;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Board.Patches;

// ---------------------------------------------------------------------------
// Phase-3a picking patches. All THREE prefixes below fall through to the ORIGINAL
// method (return true) whenever BoardPick is inactive — no scenario, modal UI, VR
// pick unavailable — so mouse + Phase-2 virtual mouse behavior stays 100% vanilla.
// Applied by BoardModule when VR runs (or in dev mode), removed collectively via
// Plugin.OnDestroy (Harmony.UnpatchSelf) for the hot-reload contract.
// ---------------------------------------------------------------------------

/// <summary>
/// #1 world-pick choke point (BOARD-INPUT §3): every hover, targeting and click
/// pick in a scenario funnels through this. The prefix swaps the mouse ray for
/// the VR pick ray (near fingertip-down ray / far hand ray) and resolves the
/// interactable exactly as the original does.
///
/// Verified against the REAL GH.Runtime.dll (v1.1.8307.0) with ilspycmd 8.2 (2026-07-15):
/// <code>
///   // global namespace, GH.Runtime.dll, MF.cs:387 — IL 85 B (PATCH-TARGETS §1.1 ✅)
///   public static CInteractable FindInteractableAtMousePosition(
///       bool ignoreinteractableviaguiflag, LayerMask gameSelectionRaycastLayer)
///   {
///       if (Camera.main == null) return null;
///       Ray ray = Camera.main.ScreenPointToRay(InputManager.CursorPosition);
///       if (Physics.Raycast(ray, out hitInfo, 1000f, gameSelectionRaycastLayer))
///           result = hitInfo.transform.gameObject.GetComponentInParent&lt;CInteractable&gt;();
///       return result;
///   }
/// </code>
/// The replacement raycasts with the CALLER's mask (Controller passes
/// m_ActiveSelectionRaycastLayer, WorldspaceStarHexDisplay passes
/// m_HexSelectionRaycastLayer) and mirrors the original's
/// <c>GetComponentInParent&lt;CInteractable&gt;()</c> resolution — so hexes, actors,
/// doors, chests and loot resolve identically to a mouse pick.
/// </summary>
[HarmonyPatch(typeof(MF), nameof(MF.FindInteractableAtMousePosition))]
internal static class MF_FindInteractableAtMousePosition_Patch
{
    private static bool Prefix(LayerMask gameSelectionRaycastLayer, ref CInteractable? __result)
    {
        if (!BoardPick.TryGetGameRay(out Vector3 origin, out Vector3 direction, out float maxDistance))
            return true; // no VR pick — run the original mouse path

        __result = null;
        if (Physics.Raycast(origin, direction, out RaycastHit hitInfo, maxDistance, gameSelectionRaycastLayer))
            __result = hitInfo.transform.gameObject.GetComponentInParent<CInteractable>();
        return false;
    }
}

/// <summary>
/// Keeps every remaining cursor consumer consistent with the VR pick: the game
/// cursor becomes the screen projection of the VR pick point, so melee-AoE facing
/// (RotateAOEWithMouse builds the facing from the hovered tile) and tooltip anchoring
/// follow the hand. It does NOT reach HoverRegisterer — see the caveat-#12 note below.
///
/// Verified against the REAL GH.Runtime.dll with ilspycmd (2026-07-15):
/// <code>
///   // InputManager : Singleton&lt;InputManager&gt;, InputManager.cs:126
///   public static Vector2 CursorPosition { get; }   // get_CursorPosition, IL 208 B
///   // gamepad branch scans Camera.allCameras for tag "UICamera";
///   // mouse branch returns InputSystemUtilities.GetMousePosition()
/// </code>
/// PATCH-TARGETS §5: 208 B — explicitly NOT inline-endangered, safe to prefix.
///
/// CAVEAT #12 IS REAL — this comment used to claim it was "moot in Phase 1's rig design:
/// the rig drives the game's existing scenario camera object, so the cached reference stays
/// valid". That described a rig design that no longer exists and the claim is FALSE.
/// <c>HoverRegisterer</c> caches <c>Camera.main</c> in <c>Awake</c> and un-projects THIS
/// patched cursor through it, while this patch's value is measured in the screen space of
/// <c>VRRigDriver.HeadCamera</c> — a separate, never-MainCamera-tagged GameObject
/// (VRRigDriver.HeadCamera.cs:311, BoardPick.cs:174). The game's scenario camera is left
/// standing and FROZEN (<c>m_IsCameraCodeControlDisabled</c>, VRRigDriver.cs:~703), so
/// <c>Camera.main</c> is a parked vantage and the hover ray landed nowhere near the laser.
/// So the "HoverRegisterer follows the hand" promise in this class's summary is NOT delivered
/// by the cursor projection: it is delivered by <see cref="HoverPickPatch"/>, which replaces
/// HoverRegisterer's ray with the VR pick ray outright. Read that class before touching this
/// one. (A false comment is worse than a verbose one; this project has lost a round to exactly
/// that.)
/// </summary>
[HarmonyPatch(typeof(InputManager), nameof(InputManager.CursorPosition), MethodType.Getter)]
internal static class InputManager_CursorPosition_Patch
{
    private static bool Prefix(ref Vector2 __result)
    {
        if (!BoardPick.TryGetCursorScreenPoint(out Vector2 screenPoint))
            return true; // no VR pick — original (real mouse / virtual mouse / gamepad)

        __result = screenPoint;
        return false;
    }
}

/// <summary>
/// #3 pointer-over-UI truth (test #14 item 5 — hero-placement second click dead).
///
/// Vanilla flow (all decompiled GH.Runtime, verified 2026-07-17):
/// <code>
///   // WorldspaceStarHexDisplay.cs:3813-3819
///   private CInteractable Interactable()
///   {
///       if (AutoTestController.s_AutoLogPlaybackInProgress || UIManager.IsPointerOverUI)
///           return null;                       // ← THE GATE
///       return InteractableUnderMouse();       // → our patched MF pick
///   }
///   // UIManager.cs:108-122: IsPointerOverUI =
///   //   EventSystem.current.IsPointerOverGameObject()  (mouse pointer -1)
///   //   || IsControllerOverUI (gamepad only — false in our ForceMouseMode)
/// </code>
/// <c>Interactable()</c> feeds <c>PointingAtANewTile()</c> and
/// <c>HighlightSelectedPlacementHex()</c> (WorldspaceStarHexDisplay.cs:436/549) — the
/// ONLY place that sets <c>Waypoint.s_PlacementTile</c> (:551 clears, :627 sets), and
/// <c>Choreographer.TileHandler</c>'s placement branch requires
/// <c>clientTile == Waypoint.s_PlacementTile</c> (Choreographer.cs:1841) before
/// <c>PlaceActorAtRoundStart</c>. It also gates <c>HoverRegisterer</c> (:32).
///
/// DIVERGENCE IN VR: <c>EventSystem.IsPointerOverGameObject()</c> reflects the
/// InControl input module's mouse pointer, whose position is the PARKED virtual/
/// hardware mouse (PointerInputModuleExtended.cs:269-307 — GetMousePosition, not our
/// patched CursorPosition). Its per-frame <c>RaycastAll</c> hits our world-space host
/// canvases through the head camera (each host carries a GraphicRaycaster with
/// worldCamera = head cam) — with the pre-fit giant planes (Panel_InitiativeTrack
/// ~45x25 m) virtually ANY parked pixel was "over UI", so <c>Interactable()</c>
/// returned null every frame, <c>s_PlacementTile</c> stayed null, and the second
/// click fell through TileHandler. The character click (:1826) has no such gate,
/// which is exactly the observed asymmetry.
///
/// FIX: while the VR pick owns the pointer (<see cref="BoardPick.Active"/> — never in
/// Menu2D/ModalUI, so flat-screen/2D flows keep vanilla), report the VR truth: the
/// pointer is over UI iff the picking hand's beam is latched onto a UI surface
/// (<c>Ray.HasFreshUiHit</c> — world panels, card fan, flat screen) or its fingertip
/// hovers UI (<c>Poke.HoveredUi</c>). Same predicate BoardClickDriver uses to route
/// trigger clicks — hover gating and click routing can no longer disagree.
/// </summary>
[HarmonyPatch(typeof(UIManager), nameof(UIManager.IsPointerOverUI), MethodType.Getter)]
internal static class UIManager_IsPointerOverUI_Patch
{
    private static bool Prefix(ref bool __result)
    {
        if (!BoardPick.Active)
            return true; // no VR pick — vanilla EventSystem/gamepad answer

        VRHand? hand = BoardPick.SourceHand;
        __result = hand != null && (hand.Ray.HasFreshUiHit || hand.Poke.HoveredUi != null);
        return false;
    }
}
