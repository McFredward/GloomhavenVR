using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Board.Patches;

// ---------------------------------------------------------------------------
// Phase-3a picking patches. Both prefixes fall through to the ORIGINAL method
// (return true) whenever BoardPick is inactive — no scenario, modal UI, VR pick
// unavailable — so mouse + Phase-2 virtual mouse behavior stays 100% vanilla.
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
/// cursor becomes the screen projection of the VR pick point, so HoverRegisterer
/// (hover/highlight registration), melee-AoE facing (RotateAOEWithMouse builds the
/// facing from the hovered tile) and tooltip anchoring all follow the hand.
///
/// Verified against the REAL GH.Runtime.dll with ilspycmd (2026-07-15):
/// <code>
///   // InputManager : Singleton&lt;InputManager&gt;, InputManager.cs:126
///   public static Vector2 CursorPosition { get; }   // get_CursorPosition, IL 208 B
///   // gamepad branch scans Camera.allCameras for tag "UICamera";
///   // mouse branch returns InputSystemUtilities.GetMousePosition()
/// </code>
/// PATCH-TARGETS §5: 208 B — explicitly NOT inline-endangered, safe to prefix.
/// Caveat #12 (HoverRegisterer caches Camera.main in Awake) is moot in Phase 1's
/// rig design: the rig drives the game's existing scenario camera object, so the
/// cached reference stays valid.
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
