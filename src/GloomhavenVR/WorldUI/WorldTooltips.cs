using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// World-space tooltips on converted surfaces (ROADMAP P3c #8, config-gated).
///
/// The trigger side is free: fingertip pokes on converted canvases synthesize REAL
/// <c>pointerEnter/Exit</c> events (Phase-2 UguiPointer), which drive the game's own
/// <c>UITooltipTarget.OnPointerEnter/Exit</c> → the static <c>UITooltip</c> API —
/// no patches needed. What this class owns is PRESENTATION: the tooltip lives on the
/// dedicated persistent <c>tooltipCanvas</c> (Screen-Space-Camera), which is useless
/// in the HMD; while active the canvas is flipped to WorldSpace and parked near the
/// currently poking fingertip, facing the player.
///
/// Verified via ilspycmd (GH.Runtime.dll): <c>CanvasManager</c> holds
/// <c>[SerializeField] private Canvas tooltipCanvas;</c> (publicized) and only
/// re-binds <c>worldCamera</c> on scene load — harmless in world mode, we rebind per
/// frame. <c>UITooltip.uiCamera</c> resolves per render mode incl. a world-camera
/// branch (UI-ARCH §6), so its internal placement math keeps working.
///
/// REVERSIBLE: render mode, worldCamera, plane distance, scale and sorting are
/// restored on disable/shutdown; a Screen-Space canvas re-drives its own rect once
/// the mode is set back.
/// </summary>
internal sealed class WorldTooltips
{
    private Canvas? _canvas;
    private bool _converted;

    // Restore data.
    private RenderMode _originalMode;
    private Camera? _originalCamera;
    private float _originalPlaneDistance;
    private Vector3 _originalScale;

    public void LateTick()
    {
        bool want = WorldUIConfig.Tooltips.Value && WorldUIConfig.ConversionActive;

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

        if (!_converted)
        {
            _originalMode = _canvas.renderMode;
            _originalCamera = _canvas.worldCamera;
            _originalPlaneDistance = _canvas.planeDistance;
            _originalScale = _canvas.transform.localScale;
            _canvas.renderMode = RenderMode.WorldSpace;
            _converted = true;
            CanvasConversion.AddMaskRequest();
            VRLog.Info("WorldUI", "Tooltip canvas flipped to world space.");
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        if (_canvas.worldCamera != head)
            _canvas.worldCamera = head;

        // Anchor near whichever fingertip is currently hovering converted UI.
        Vector3? anchor = FindPokeAnchor();
        if (!anchor.HasValue)
            return; // tooltip is hidden anyway; leave the canvas where it is

        float scale = PanelLayout.WorldScale;
        Vector3 pos = anchor.Value + Vector3.up * (0.07f * scale);
        Vector3 fromHead = pos - head.transform.position;
        if (fromHead.sqrMagnitude < 1e-6f)
            return;

        Transform t = _canvas.transform;
        t.SetPositionAndRotation(pos, Quaternion.LookRotation(fromHead.normalized, Vector3.up));
        t.localScale = Vector3.one * (WorldUIConfig.CanvasScaleMm.Value * 0.001f * scale * 0.5f);
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
