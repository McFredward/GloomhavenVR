using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// World-space tooltips on converted surfaces (ROADMAP P3c #8, config-gated).
///
/// SCENARIO-ONLY (I2, hardware test #5): in <see cref="VRMode.Menu2D"/> the tooltip
/// canvas must stay untouched — it is Screen-Space-Camera on the UICamera, which the
/// FlatScreen redirects into its RenderTexture, so menu tooltips already appear on
/// the quad at native size. Test #5 flipped it to world space in the menu anyway:
/// with no poke anchor the flip never applied a scale, leaving the canvas rect at
/// its pixel dimensions in WORLD METERS (a ~2560 px canvas ≈ 2.5 km), rendered by
/// the head camera — the "giant mouseover" report. The conversion now gates on the
/// VR mode state machine (flips in scenario modes, restores on entering Menu2D) and
/// applies its world scale AT FLIP TIME plus every frame, never only on anchor.
///
/// The trigger side is free: fingertip pokes on converted canvases synthesize REAL
/// <c>pointerEnter/Exit</c> events (Phase-2 UguiPointer), which drive the game's own
/// <c>UITooltipTarget.OnPointerEnter/Exit</c> → the static <c>UITooltip</c> API —
/// no patches needed. What this class owns is PRESENTATION: the tooltip lives on the
/// dedicated persistent <c>tooltipCanvas</c> (Screen-Space-Camera), which is useless
/// in the HMD; while active the canvas is flipped to WorldSpace and parked near the
/// currently poking fingertip, facing the player.
///
/// Scale sanity (I2): world scale = <see cref="WorldUIConfig.CanvasScaleMm"/> (mm per
/// uGUI pixel, default 1) × 0.001 × diorama scale × 0.5 — half the panel framework's
/// meters-per-pixel (CanvasConversion.cs: <c>metersPerPixel = CanvasScaleMm * 0.001f</c>)
/// so a ~400 px tooltip reads ~20 cm at arm's length instead of 40.
///
/// Verified via ilspycmd (GH.Runtime.dll): <c>CanvasManager</c> holds
/// <c>[SerializeField] private Canvas tooltipCanvas;</c> (publicized) and only
/// re-binds <c>worldCamera</c> on scene load — harmless in world mode, we rebind per
/// frame. <c>UITooltip.uiCamera</c> resolves per render mode incl. a world-camera
/// branch (UI-ARCH §6), so its internal placement math keeps working.
///
/// REVERSIBLE: render mode, worldCamera, plane distance, scale and sorting are
/// restored on disable/shutdown AND on every return to Menu2D; a Screen-Space canvas
/// re-drives its own rect once the mode is set back.
/// </summary>
internal sealed class WorldTooltips
{
    /// <summary>Unanchored world-space parking spot (out of every camera's view).</summary>
    private static readonly Vector3 ParkPosition = new(0f, -1000f, 0f);

    private Canvas? _canvas;
    private bool _converted;

    // Restore data.
    private RenderMode _originalMode;
    private Camera? _originalCamera;
    private float _originalPlaneDistance;
    private Vector3 _originalScale;

    public void LateTick()
    {
        // Menu2D keeps the vanilla 2D tooltip path (UICamera → FlatScreen RT); every
        // scenario mode (incl. ModalUI/BoardTargeting — Recompute() only leaves
        // Menu2D while a scenario runs) gets the world-space presentation.
        bool want = WorldUIConfig.Tooltips.Value && WorldUIConfig.ConversionActive
                    && VRModeStateMachine.CurrentMode != VRMode.Menu2D;

        if (!want)
        {
            Restore();
            return;
        }

        if (_canvas == null)
        {
            var manager = Object.FindObjectOfType<CanvasManager>();
            _canvas = manager != null ? manager.tooltipCanvas : null;
            if (_canvas == null)
                return;
        }

        float scale = PanelLayout.WorldScale;
        Vector3 worldScale = Vector3.one * (WorldUIConfig.CanvasScaleMm.Value * 0.001f * scale * 0.5f);

        if (!_converted)
        {
            _originalMode = _canvas.renderMode;
            _originalCamera = _canvas.worldCamera;
            _originalPlaneDistance = _canvas.planeDistance;
            _originalScale = _canvas.transform.localScale;
            _canvas.renderMode = RenderMode.WorldSpace;
            // Scale + park IMMEDIATELY: an unanchored flip must never leave the
            // canvas rect at pixel size in world meters (test-#5 giant tooltip).
            _canvas.transform.localScale = worldScale;
            _canvas.transform.position = ParkPosition;
            _converted = true;
            CanvasConversion.AddMaskRequest();
            VRLog.Info("WorldUI", "Tooltip canvas flipped to world space (scenario mode).");
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        if (_canvas.worldCamera != head)
            _canvas.worldCamera = head;

        // Re-assert the scale every frame (config/diorama scale are live; the game
        // may rewrite the transform) — independent of anchor presence.
        if (_canvas.transform.localScale != worldScale)
            _canvas.transform.localScale = worldScale;

        // Anchor near whichever fingertip is currently hovering converted UI.
        Vector3? anchor = FindPokeAnchor();
        if (!anchor.HasValue)
            return; // tooltip is hidden anyway; leave the canvas where it is

        Vector3 pos = anchor.Value + Vector3.up * (0.07f * scale);
        Vector3 fromHead = pos - head.transform.position;
        if (fromHead.sqrMagnitude < 1e-6f)
            return;

        Transform t = _canvas.transform;
        t.SetPositionAndRotation(pos, Quaternion.LookRotation(fromHead.normalized, Vector3.up));
    }

    private static Vector3? FindPokeAnchor()
    {
        VRHand? left = VRHands.Left;
        if (left != null && left.Poke.HoveredUi != null)
            return left.Rig.IndexTip.position;
        VRHand? right = VRHands.Right;
        if (right != null && right.Poke.HoveredUi != null)
            return right.Rig.IndexTip.position;
        return null;
    }

    private void Restore()
    {
        if (!_converted)
            return;
        _converted = false;
        CanvasConversion.RemoveMaskRequest();
        if (_canvas != null)
        {
            _canvas.renderMode = _originalMode;
            _canvas.worldCamera = _originalCamera;
            _canvas.planeDistance = _originalPlaneDistance;
            _canvas.transform.localScale = _originalScale;
            VRLog.Info("WorldUI", "Tooltip canvas restored to screen space.");
        }
    }

    public void Shutdown() => Restore();
}
