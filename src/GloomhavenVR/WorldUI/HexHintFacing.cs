using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Per-frame head-follow for the hover HEX-HINT panels (task #4 / #7).
///
/// When the laser hovers a board field the game pops a hint/explanation label — e.g.
/// "Geschlossene Tür" (closed door), chests, obstacles, pressure plates, portals,
/// terrain — via <c>UITextInfoPanel</c>, and carryable-quest-item cards via
/// <c>UIPropInfoPanel</c>. Both are hover-driven info popups (verified:
/// <c>decompiled/GH.Runtime/WorldspaceStarHexDisplay.cs</c> <c>ShowTooltipForTile()</c>
/// routes the door label to <c>UITextInfoPanel.Show</c> at :3607 with the
/// <c>CLOSED_DOOR_TOOLTIP</c> string at :3441). <see cref="PropInfoSurface"/> already
/// converts them to world space and docks them at the fixed <see cref="PanelSlot.PropInfo"/>
/// pose — but that slot rotation faces the CACHED SEAT yaw (PanelLayout world-anchoring)
/// and its position sits low at the table edge, so after a snap-turn / world-grab / the
/// player simply leaning the hint no longer squarely faces the head and reads off to the
/// side. That is the reported "hint not facing the player".
///
/// This step runs in LateUpdate, AFTER PropInfoSurface's Update-time placement, and
/// OVERRIDES the host POSITION and ROTATION (PropInfoSurface keeps owning scale) — but
/// ONLY while the panel is actually visible (<see cref="UIWindow.IsVisible"/>). While
/// shown it drifts the hint to a comfortable reading spot near the CENTER of the player's
/// forward field of view (slightly below the gaze center, at a fixed distance × diorama
/// scale) with LAZY, critically-damped motion — <see cref="Vector3.SmoothDamp"/> for
/// position and a frame-rate-independent exponential slerp for rotation — so it eases
/// toward the center and settles rather than rigidly locking or hard-billboarding. The
/// lazy follow is gated by <see cref="WorldUIConfig.HexHintFollowView"/> (default on);
/// with it off we keep PropInfoSurface's dock position and only re-face the host to the
/// head. When the hint hides we do nothing: the panel keeps PropInfoSurface's pose and
/// then releases, so the next hover starts fresh.
///
/// Non-invasive: it reads the shared <see cref="CanvasConversion.ActivePanels"/> registry
/// to find each singleton panel's converted host by <see cref="ConvertedPanel.Target"/>
/// identity — it never touches <see cref="PropInfoSurface"/>. If <c>PropInfoCards</c> is
/// off (panel never converted) the host lookup misses and this is a no-op. Facing
/// convention matches <c>PanelPlacement.Facing</c>: uGUI fronts render toward the viewer,
/// so the host's +Z points AWAY from the head. Occlusion-sane: only the transform is
/// touched (no sorting/material draw-through), and the host stays non-pokeable.
/// </summary>
internal sealed class HexHintFacing
{
    /// <summary>Comfortable reading distance in front of the head (real meters × diorama scale).</summary>
    private const float FollowDistance = 0.6f;

    /// <summary>The hint hovers this far below the gaze center (real meters × diorama scale).</summary>
    private const float FollowDrop = 0.12f;

    /// <summary>SmoothDamp time constant for the lazy position drift (seconds) — bigger = lazier.</summary>
    private const float FollowSmoothTime = 0.28f;

    /// <summary>Exponential approach rate for the lazy rotation (per second) — bigger = snappier.</summary>
    private const float FollowRotLambda = 7f;

    /// <summary>Per-panel follow state (singleton + damping accumulators).</summary>
    private sealed class HintState
    {
        public Component? Attached;
        public UIWindow? Window;
        public bool Engaged;
        public Vector3 Pos;
        public Vector3 PosVel;
        public Quaternion Rot;
    }

    private readonly HintState _text = new();
    private readonly HintState _prop = new();

    public void LateTick()
    {
        if (!WorldUIConfig.ConversionActive)
        {
            _text.Engaged = false;
            _prop.Engaged = false;
            return;
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        FaceHint(
            Singleton<UITextInfoPanel>.IsInitialized ? Singleton<UITextInfoPanel>.Instance : null,
            _text, head, "TextInfoPanel (e.g. \"Geschlossene Tür\")");
        FaceHint(
            Singleton<UIPropInfoPanel>.IsInitialized ? Singleton<UIPropInfoPanel>.Instance : null,
            _prop, head, "PropInfoPanel (quest item)");
    }

    private void FaceHint(Component? panel, HintState s, Camera head, string name)
    {
        if (panel == null)
        {
            Reset(s);
            return;
        }

        // Cache the UIWindow across the singleton's life (both panels RequireComponent it).
        if (!ReferenceEquals(panel, s.Attached))
        {
            s.Attached = panel;
            s.Window = panel.GetComponent<UIWindow>();
            s.Engaged = false;
        }

        // Only while genuinely visible — IsVisible gates on CanvasGroup alpha > 0, so the
        // hide-fade / PropInfoSurface release hysteresis is excluded. On hide: do nothing.
        if (s.Window == null || !s.Window.IsVisible)
        {
            s.Engaged = false;
            return;
        }

        // Resolve the world host PropInfoSurface converted this panel onto. Missing =
        // not converted (PropInfoCards off, or a frame mid-convert) → leave it be.
        Transform? host = FindHost(panel.transform);
        if (host == null)
        {
            s.Engaged = false;
            return;
        }

        if (WorldUIConfig.HexHintFollowView.Value)
        {
            // Lazy follow to the center of view. Target: a comfortable reading spot in front
            // of the head, slightly below the gaze center, at a fixed distance × diorama scale.
            Transform h = head.transform;
            float worldScale = PanelLayout.WorldScale;
            Vector3 targetPos = h.position
                                + h.forward * (FollowDistance * worldScale)
                                - Vector3.up * (FollowDrop * worldScale);
            Quaternion targetRot = FaceHead(targetPos, h.position);

            if (!s.Engaged)
            {
                // Start the drift from wherever PropInfoSurface last docked it (this frame's
                // Update placement) so it eases toward center rather than snapping.
                s.Pos = host.position;
                s.Rot = host.rotation;
                s.PosVel = Vector3.zero;
            }

            float dt = Time.deltaTime;
            s.Pos = Vector3.SmoothDamp(s.Pos, targetPos, ref s.PosVel, FollowSmoothTime, Mathf.Infinity, dt);
            // Frame-rate-independent exponential approach (lazy, not a hard billboard).
            s.Rot = Quaternion.Slerp(s.Rot, targetRot, 1f - Mathf.Exp(-FollowRotLambda * dt));

            host.position = s.Pos;
            host.rotation = s.Rot;
        }
        else
        {
            // Toggle off: keep PropInfoSurface's dock position, only re-face to the head.
            host.rotation = FaceHead(host.position, head.transform.position);
        }

        if (!s.Engaged)
        {
            s.Engaged = true;
            VRLog.Info("WorldUI", $"Hex hint follows head (lazy, center of view): {name}.");
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

    private static void Reset(HintState s)
    {
        s.Attached = null;
        s.Window = null;
        s.Engaged = false;
    }

    public void Shutdown()
    {
        Reset(_text);
        Reset(_prop);
    }
}
