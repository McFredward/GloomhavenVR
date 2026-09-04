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
    /// <summary>
    /// The built-in "UI" layer (5) — the layer the GAME authors its whole flat 2D UI on, and the
    /// layer its own <c>UI Camera</c> renders EXCLUSIVELY (observed mask <c>0x00000020</c>, target
    /// <c>GloomhavenVR.DesktopScrubSink</c>). Also the <see cref="FallbackLayer"/> when no unnamed
    /// layer is free.
    ///
    /// <para>THE INVARIANT THIS LAYER *ALMOST* SATISFIES, and the one place it does not (ModBuild
    /// 424 finding). The mod's convention is: <b>the head camera draws mod-owned content and the 3D
    /// world, never the game's flat 2D UI</b> — converted windows are moved onto
    /// <see cref="ModLayer"/> (<c>CanvasConversion.ApplyModLayer</c>), the game's own 2D UI stays on
    /// layer 5 and is drawn by the game's UI Camera into the scrub sink. The head camera's mask
    /// nevertheless CONTAINS layer 5, and it must, because three families of GAME-owned uGUI are
    /// presented in world space by the mod and are deliberately never re-layered (CAMERA-POLICY §2
    /// reversibility rule — <i>"converted uGUI panels, tooltips and live card faces keep their
    /// authored layers; the UI-layer culling bit for those remains owned by CanvasConversion"</i>):
    /// <list type="bullet">
    /// <item><c>Cards/VRCard.Build</c> — <i>"The live game face (FullAbilityCard) re-parented in
    /// LATER keeps its own game layer"</i>: every ability card face in the player's hand.</item>
    /// <item><c>Cards/Piles/ItemsPile</c> :5074 — <i>"do NOT VRLayers.Apply(go) … a GAME-owned canvas
    /// that must keep its authored UI layer … it renders via the VR camera's UI-layer bit owned by
    /// CanvasConversion"</i>: every item card face.</item>
    /// <item><c>WorldUI/Tooltips/WorldTooltips</c> :1061 — the game's own <c>Tooltip Canvas_unified</c>
    /// (layer 5) is flipped to <see cref="UnityEngine.RenderMode.WorldSpace"/> IN PLACE, never
    /// re-parented and never re-layered; <c>CanvasConversion.AddMaskRequest()</c> exists for exactly
    /// this.</item>
    /// </list>
    /// Dropping bit 5 from the head camera's mask would therefore blank the player's cards and every
    /// hover panel. It is not a narrowing this mod can make — see the block comment on
    /// <c>VRRigDriver.MaskNarrowingFloor</c> for the full round-by-round evidence.</para>
    /// </summary>
    internal const int GameUiLayer = 5;

    /// <summary>Culling-mask bit for <see cref="GameUiLayer"/>.</summary>
    internal const int GameUiLayerMask = 1 << GameUiLayer;

    private const int FallbackLayer = GameUiLayer; // built-in "UI"

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
