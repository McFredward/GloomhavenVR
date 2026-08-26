using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    // ---- placement -----------------------------------------------------------------------

    private void PlaceScreen(bool instant)
    {
        if (_quad == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f)
            fwd = Vector3.forward;
        fwd.Normalize();

        float distance = Mathf.Max(0.1f, WorldUIConfig.ScreenDistance.Value);
        Vector3 target = h.position + fwd * (distance * scale);
        // Quad primitive faces -Z (visible from -forward side): +Z away from viewer.
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        float width = WantedQuadWidth(scale);
        Vector3 size = new(width, width / ScreenAspect, 1f);

        Transform t = _quad.transform;
        if (instant)
        {
            t.SetPositionAndRotation(target, rot);
        }
        else
        {
            t.position = Vector3.Lerp(t.position, target, Time.deltaTime * 3f);
            t.rotation = Quaternion.Slerp(t.rotation, rot, Time.deltaTime * 3f);
        }
        t.localScale = size;

        PlaceBackQuad(scale, distance);

        if (instant)
        {
            _placedHead = head;
            Transform? rig = Rig.VRRigDriver.RigRoot;
            _placedRigPos = rig != null ? rig.position : Vector3.zero;
            LogPlacement(head);
        }
    }

    /// <summary>
    /// Background quad (split): derived from the screen quad's CURRENT pose — so it
    /// stays coherent during the lazy-follow glide — the layer gap behind it along
    /// +forward (away from the viewer), scaled up so it subtends the same angle from
    /// the head (no visible edge inset where the glass ends).
    /// </summary>
    private void PlaceBackQuad(float scale, float distance)
    {
        if (_backQuad == null || _quad == null)
            return;
        Transform t = _quad.transform;
        float grow = 1f + BackplaneGapMeters / Mathf.Max(0.1f, distance);
        Transform b = _backQuad.transform;
        b.SetPositionAndRotation(t.position + t.forward * (BackplaneGapMeters * scale), t.rotation);
        Vector3 size = t.localScale;
        b.localScale = new Vector3(size.x * grow, size.y * grow, 1f);
    }

    /// <summary>
    /// One diagnostic line per (re)placement: quad pose vs head pose, culling mask,
    /// layer and shader — makes "quad exists but camera can't see it" visible in the
    /// BepInEx log without an HMD report.
    /// </summary>
    private void LogPlacement(Camera head)
    {
        if (_quad == null || _quadRenderer == null)
            return;
        Shader? shader = _quadRenderer.sharedMaterial != null ? _quadRenderer.sharedMaterial.shader : null;
        VRLog.Info("WorldUI",
            $"FlatScreen quad placed: pos={_quad.transform.position}, size={_quad.transform.localScale}, " +
            $"layer={_quad.layer}, shader='{(shader != null ? shader.name : "NULL")}', " +
            $"RT={( _rt != null ? $"{_rt.width}x{_rt.height}" : "NULL")}, " +
            $"split={(SplitActive ? (_splitRouting ? "routing" : "engaged") : "off")} | " +
            $"head '{head.name}' pos={head.transform.position}, fwd={head.transform.forward}, " +
            $"mask=0x{head.cullingMask:X8}, clear={head.clearFlags}, stereo={head.stereoTargetEye}.");
    }

    /// <summary>
    /// Quad width in world units for the configured screen size. Single source of
    /// truth for <see cref="PlaceScreen"/> AND <see cref="FollowHead"/>'s re-place
    /// trigger — a divergence between the two would re-place the screen every frame.
    /// </summary>
    private static float WantedQuadWidth(float scale) =>
        WorldUIConfig.ScreenWidth.Value * scale;

    private void FollowHead()
    {
        if (_quad == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        // Rig rebuild / recenter / head-camera swap: snap the screen back in front of
        // the (new) vantage instead of lazily drifting after it. Placement always uses
        // the TRACKED head's actual world forward (PlaceScreen) — never an assumed
        // axis: hardware test #4 placed the quad "forward" of a vantage the player
        // wasn't facing.
        Transform? rig = Rig.VRRigDriver.RigRoot;
        Vector3 rigPos = rig != null ? rig.position : Vector3.zero;
        // Live-tunable size/distance ([WorldUI] ScreenWidth/ScreenDistance):
        // re-place when the effective width no longer matches the quad (cheap
        // float compare).
        float wantedWidth = WantedQuadWidth(PanelLayout.WorldScale);
        if (head != _placedHead || (rigPos - _placedRigPos).sqrMagnitude > 1e-4f
            || Mathf.Abs(_quad.transform.localScale.x - wantedWidth) > 0.001f)
        {
            PlaceScreen(instant: true);
            _offGazeSince = -1f;
            _gliding = false;
            return;
        }

        // Lazy follow (P3a): after the gaze has been >45° off the screen for >1 s,
        // glide it back in front of the current gaze until it settles (<5°).
        Vector3 toScreen = _quad.transform.position - head.transform.position;
        float angle = Vector3.Angle(head.transform.forward, toScreen);
        if (angle > FollowAngleDegrees)
        {
            if (_offGazeSince < 0f)
                _offGazeSince = Time.unscaledTime;
            if (!_gliding && Time.unscaledTime - _offGazeSince >= FollowDwellSeconds)
            {
                _gliding = true;
                VRLog.Info("WorldUI", $"FlatScreen lazy follow: gaze {angle:F0}° off for " +
                                      $">{FollowDwellSeconds:F0}s — gliding back in front (head fwd {head.transform.forward}).");
            }
        }
        else
        {
            _offGazeSince = -1f;
        }

        if (_gliding)
        {
            PlaceScreen(instant: false);
            if (angle < FollowSettledDegrees)
            {
                _gliding = false;
                _offGazeSince = -1f;
            }
        }
    }

}
