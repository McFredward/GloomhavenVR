using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Per-frame head-facing for the hover HEX-HINT panels (task #4).
///
/// When the laser hovers a board field the game pops a hint/explanation label — e.g.
/// "Geschlossene Tür" (closed door), chests, obstacles, pressure plates, portals,
/// terrain — via <c>UITextInfoPanel</c>, and carryable-quest-item cards via
/// <c>UIPropInfoPanel</c>. Both are hover-driven info popups (verified:
/// <c>decompiled/GH.Runtime/WorldspaceStarHexDisplay.cs</c> <c>ShowTooltipForTile()</c>
/// routes the door label to <c>UITextInfoPanel.Show</c> at :3607 with the
/// <c>CLOSED_DOOR_TOOLTIP</c> string at :3441). <see cref="PropInfoSurface"/> already
/// converts them to world space and docks them at the fixed <see cref="PanelSlot.PropInfo"/>
/// pose — but that slot rotation faces the CACHED SEAT yaw (PanelLayout world-anchoring),
/// so after a snap-turn / world-grab / the player simply leaning the hint no longer
/// squarely faces the head. That is the reported "hint not facing the player".
///
/// This step runs in LateUpdate, AFTER PropInfoSurface's Update-time placement, and
/// OVERRIDES only the host ROTATION (PropInfoSurface keeps owning position and scale)
/// to a live, upright billboard toward the head — but ONLY while the panel is actually
/// visible (<see cref="UIWindow.IsVisible"/>). When the hint hides we do nothing: the
/// panel keeps PropInfoSurface's pose and then releases. Per-frame facing is correct
/// here because the hint must TRACK the head continuously while shown (unlike a
/// grabbable menu, which stays put where the player parked it).
///
/// Non-invasive: it reads the shared <see cref="CanvasConversion.ActivePanels"/> registry
/// to find each singleton panel's converted host by <see cref="ConvertedPanel.Target"/>
/// identity — it never touches <see cref="PropInfoSurface"/>. If <c>PropInfoCards</c> is
/// off (panel never converted) the host lookup misses and this is a no-op. Facing
/// convention matches <c>PanelPlacement.Facing</c>: uGUI fronts render toward the viewer,
/// so the host's +Z points AWAY from the head.
/// </summary>
internal sealed class HexHintFacing
{
    private Component? _textAttached;
    private UIWindow? _textWindow;
    private bool _textFacing;

    private Component? _propAttached;
    private UIWindow? _propWindow;
    private bool _propFacing;

    public void LateTick()
    {
        if (!WorldUIConfig.ConversionActive)
        {
            _textFacing = false;
            _propFacing = false;
            return;
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        FaceHint(
            Singleton<UITextInfoPanel>.IsInitialized ? Singleton<UITextInfoPanel>.Instance : null,
            ref _textAttached, ref _textWindow, ref _textFacing, head, "TextInfoPanel (e.g. \"Geschlossene Tür\")");
        FaceHint(
            Singleton<UIPropInfoPanel>.IsInitialized ? Singleton<UIPropInfoPanel>.Instance : null,
            ref _propAttached, ref _propWindow, ref _propFacing, head, "PropInfoPanel (quest item)");
    }

    private void FaceHint(Component? panel, ref Component? attached, ref UIWindow? window,
        ref bool facing, Camera head, string name)
    {
        if (panel == null)
        {
            attached = null;
            window = null;
            facing = false;
            return;
        }

        // Cache the UIWindow across the singleton's life (both panels RequireComponent it).
        if (!ReferenceEquals(panel, attached))
        {
            attached = panel;
            window = panel.GetComponent<UIWindow>();
        }

        // Only while genuinely visible — IsVisible gates on CanvasGroup alpha > 0, so the
        // hide-fade / PropInfoSurface release hysteresis is excluded. On hide: do nothing.
        if (window == null || !window.IsVisible)
        {
            facing = false;
            return;
        }

        // Resolve the world host PropInfoSurface converted this panel onto. Missing =
        // not converted (PropInfoCards off, or a frame mid-convert) → leave it be.
        Transform? host = FindHost(panel.transform);
        if (host == null)
        {
            facing = false;
            return;
        }

        // Live upright billboard toward the head. Position + scale stay PropInfoSurface's.
        host.rotation = FaceHead(host.position, head.transform.position);

        if (!facing)
        {
            facing = true;
            VRLog.Info("WorldUI", $"Hex hint facing head while hovered: {name}.");
        }
    }

    /// <summary>Converted host for a panel's <see cref="ConvertedPanel.Target"/>, or null.</summary>
    private static Transform? FindHost(Transform target)
    {
        var panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            ConvertedPanel p = panels[i];
            if (p != null && p.IsAlive && ReferenceEquals(p.Target, target))
                return p.HostTransform;
        }
        return null;
    }

    /// <summary>Upright orientation facing the head (uGUI front toward the viewer → +Z away).</summary>
    private static Quaternion FaceHead(Vector3 hostPos, Vector3 headPos)
    {
        Vector3 away = hostPos - headPos;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-4f)
            away = Vector3.forward;
        else
            away.Normalize();
        return Quaternion.LookRotation(away, Vector3.up);
    }

    public void Shutdown()
    {
        _textAttached = null;
        _textWindow = null;
        _textFacing = false;
        _propAttached = null;
        _propWindow = null;
        _propFacing = false;
    }
}
