using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// SINGLE OWNER of the mod's layer policy (docs/CAMERA-POLICY.md §2).
///
/// Problem this solves (hardware test #3): mod visuals used to inherit layer 0
/// (Default) or hard-coded 5 (UI), while the game's menu cameras cull with
/// mask 0x20 (UI only) — or even 0x0 — so hands/lasers/quad were invisible in the
/// HMD. Ad-hoc per-object "move to a layer the camera renders" fixes fought the
/// game's masks instead of owning one layer.
///
/// Policy:
/// - ONE dedicated mod layer, resolved at runtime: the first unnamed layer
///   scanning DOWN from 31 (project layers are authored low; 31 is the classic
///   mod slot). Fallback: built-in UI layer 5 when every layer is named.
/// - ALL mod-owned visuals go on it via <see cref="Apply"/> (hands, fingers,
///   lasers, reticles, flat screen, starting indicator, buttons, panels, tray,
///   cards, vignette, …).
/// - The VR head camera ORs <see cref="ModLayerMask"/> into its culling mask
///   (owner: <c>VRRigDriver</c>) — no other camera gets the bit, so mod visuals
///   never leak into the game's own cameras (UICamera, GUI 3D, minimap-style RTs).
/// - GAME-owned objects are never re-layered (reversibility): converted uGUI
///   panels / tooltips / card faces keep their authored layers; the UI-layer
///   culling bit for those remains owned by <c>CanvasConversion</c>.
/// - Dev mode without an HMD: <see cref="Apply"/> is a no-op (desktop cameras
///   don't render the mod layer; the pre-fix flat behavior is preserved).
/// </summary>
internal static class VRLayers
{
    private const int FallbackLayer = 5; // built-in "UI"

    private static int _modLayer = -1;

    /// <summary>The dedicated mod layer index (resolved once, logged).</summary>
    internal static int ModLayer
    {
        get
        {
            if (_modLayer < 0)
                Resolve();
            return _modLayer;
        }
    }

    /// <summary>Culling-mask bit for <see cref="ModLayer"/>.</summary>
    internal static int ModLayerMask => 1 << ModLayer;

    /// <summary>
    /// Put a mod-owned visual tree on the mod layer (recursive). No-op while VR is
    /// not running (dev-sim keeps vanilla layers so desktop cameras still render
    /// the sim visuals). Never call this on game-owned objects.
    /// </summary>
    internal static void Apply(GameObject go)
    {
        if (go == null || !VRSession.IsRunning)
            return;
        SetLayerRecursive(go.transform, ModLayer);
    }

    private static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i), layer);
    }

    private static void Resolve()
    {
        for (int i = 31; i >= 8; i--) // 0-7 are Unity built-ins
        {
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
            {
                _modLayer = i;
                VRLog.Info("Core", $"Mod layer resolved: {i} (first unnamed layer scanning 31→8; mask 0x{1 << i:X8}).");
                return;
            }
        }
        _modLayer = FallbackLayer;
        VRLog.Warn("Core", $"No unnamed layer free (31→8 all named) — falling back to built-in UI layer {FallbackLayer}. " +
                           "Mod visuals will share the game's UI layer and may show up in game UI cameras.");
    }
}
