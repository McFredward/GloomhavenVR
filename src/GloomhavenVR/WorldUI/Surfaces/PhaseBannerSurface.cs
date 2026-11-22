using System;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>Seats the game's animated phase paper in world space once per visible line.</summary>
internal sealed class PhaseBannerSurface
{
    private const float PixelsPerMeter = 1440f;
    private ConvertedPanel? _panel;
    private PhaseBannerHandler? _owner;
    private string? _line;

    internal void Tick()
    {
        PhaseBannerHandler owner = Singleton<PhaseBannerHandler>.Instance;
        Camera? head = VRRigDriver.HeadCamera;
        if (!WorldUIConfig.ConversionActive || Choreographer.s_Choreographer == null || head == null
            || owner == null || !owner.gameObject.scene.isLoaded)
        {
            Release();
            return;
        }

        RectTransform? paper = owner.showAnimation != null ? owner.showAnimation.transform as RectTransform : null;
        if (_panel != null && (_owner != owner || !_panel.IsAlive || _panel.Target != paper
            || _panel.HostRect == null || paper == null || paper.parent != _panel.HostRect))
            Release();

        UIWindow window = owner.window;
        if (LoadingIndicator.FlatScreenSuppressed || window == null || !window.IsVisible
            || !window.gameObject.activeInHierarchy || owner.phaseText == null || paper == null)
        {
            _line = null;
            SetVisible(false);
            return;
        }

        if (_panel == null)
        {
            // Both scenario prefabs animate the Banner child. The UIWindow,
            // full-screen interaction blocker and onHidden callback stay native.
            if (!paper.IsChildOf(window.transform) || paper == window.transform
                || !owner.phaseText.transform.IsChildOf(paper) || paper.rect.width <= 1f
                || owner.interactionBlock == null || owner.interactionBlock.transform.IsChildOf(paper))
                return;

            Vector3 animatedScale = paper.localScale;
            _panel = CanvasConversion.Convert(paper, "PhaseBanner", pokeable: false, fitContent: false);
            if (_panel == null) return;
            _owner = owner;
            // Convert centers the root and normalizes scale. The running native
            // tween already owns scale, including on the very first frame.
            paper.localScale = animatedScale;
            _panel.MrBackingSuppressed = true;
        }

        RectTransform? host = _panel.HostRect;
        if (host == null) return;
        string line = owner.phaseText.text;
        if (!string.Equals(_line, line, StringComparison.Ordinal))
        {
            float scale = Mathf.Max(.001f, PanelLayout.WorldScale);
            Transform eye = head.transform;
            var cameraController = CameraController.s_CameraController;
            SpawnRing.Footprint board = default;
            if (cameraController != null)
                SpawnRing.TryFootprint(cameraController.FocusPoint.y, out board);
            Vector3 position = Opening(eye, scale, board);
            Vector3 forward = Vector3.ProjectOnPlane(position - eye.position, Vector3.up);
            Quaternion facing = forward.sqrMagnitude > .00001f
                ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.Euler(0, eye.eulerAngles.y, 0);
            host.SetPositionAndRotation(position, facing);
            host.localScale = Vector3.one * (scale / PixelsPerMeter);
            _line = line;
        }
        SetVisible(true);
    }

    private static Vector3 Opening(Transform eye, float scale, SpawnRing.Footprint board)
    {
        Vector3 preferred = eye.position + (eye.forward * 1.35f + Vector3.up * .16f) * scale;
        if (!board.Valid) return preferred;
        preferred.y = Mathf.Max(preferred.y, board.Center.y + .18f * scale);
        Vector2 half = board.OuterHalfWorld;
        Vector3 aboveBoard = new(
            Mathf.Clamp(preferred.x, board.Center.x - half.x, board.Center.x + half.x), preferred.y,
            Mathf.Clamp(preferred.z, board.Center.z - half.y, board.Center.z + half.y));
        Vector3 fromEye = aboveBoard - eye.position;
        return Vector3.Dot(eye.forward, fromEye) >= .65f * scale
            && Vector3.Angle(eye.forward, fromEye) <= 36f ? aboveBoard : preferred;
    }

    private void SetVisible(bool visible)
    {
        if (_panel?.HostRaycaster != null && _panel.HostRaycaster.enabled)
            _panel.HostRaycaster.enabled = false;
        // The native CanvasGroup and tween still control the fade. Disabling only
        // the host canvas keeps the original animator running through native Hide.
        if (_panel?.HostCanvas != null && _panel.HostCanvas.enabled != visible)
            _panel.HostCanvas.enabled = visible;
    }

    internal void Release()
    {
        CanvasConversion.Release(_panel);
        _panel = null;
        _owner = null;
        _line = null;
    }
}
