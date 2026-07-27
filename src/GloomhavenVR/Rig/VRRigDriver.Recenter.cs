using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    /// <summary>Recenter the live rig, if any (the comfort entry point — chord/panel/dev key).</summary>
    internal static void RequestRecenter() => Instance?.Recenter();

    /// <summary>
    /// Reposition the rig so the player's CURRENT head pose ends up at the configured
    /// table-edge spot: eyes <see cref="ComfortSettings.EffectiveEyeHeightMeters"/> (real)
    /// above the orbit focus plane and <see cref="ComfortSettings.EffectiveEyeBackMeters"/>
    /// back (the STANDING preset + [Comfort] TableHeightOffset — there is no seated preset any
    /// more, see <see cref="ComfortSettings.StandingEyeHeightMeters"/>). Called automatically
    /// on the first tracked pose, and bound to the B+Y hold chord (see <see cref="Comfort"/>).
    /// </summary>
    internal void Recenter()
    {
        if (_rigRoot == null || _camera == null)
            return;

        if (_kind == RigKind.Menu)
        {
            RecenterMenu();
            return;
        }

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return;

        // World tilt composition: the seat math below is authored for a yaw-only rig
        // (seatYaw reads the current rotation; offsets assume a level horizon). Flatten the
        // tilt out first — TickWorldTilt re-applies the configured tilt on top of the fresh
        // seat in LateUpdate this same frame, so a recenter lands at the standard table-edge
        // seat viewed through the tilt, with no untilted frame ever rendered.
        if (_tiltActive)
            _rigRoot.transform.rotation = YawOnly(_rigRoot.transform.rotation);
        // Attribute this frame's tilt re-apply (the LateUpdate heal after the flatten
        // above) to the recenter in the WorldTilt change log — and let it act as a
        // MASKED RE-AIM event: TickWorldTilt instantly re-aims the tilt at the current
        // view yaw, exactly what Demeo's recenter does by absorbing the head yaw into
        // the avatar root (InputTracking.Recenter / AvatarController.cs:684-694).
        _axisSnapReason = "recenter";

        float scale = _rigRoot.transform.localScale.x;

        // Spawn circle (FEATURE D): give each player a distinct azimuth around the focus
        // point so N VR players sit evenly around the board — each FACING the center —
        // instead of stacking at one shared seat. Default seatYaw is the CURRENT rig
        // rotation, so single-player / offline (total <= 1) and the SpawnInCircle-off case
        // keep the EXACT prior behavior: rotation untouched, seat direction = current yaw.
        //
        // For 2+ players we rotate the whole rig about the focus point's up (world up) axis
        // by 360*idx/total, pivoting on the frozen flat base yaw — this rotates BOTH the
        // seat offset direction AND the facing, so the head lands on the circle and the
        // board still reads centered ahead. Rotating from the frozen base (not the live
        // rotation) makes repeated recenters idempotent. The re-seated head world pose is
        // broadcast as-is (foundation avatar sync is world-frame) → remote avatars separate
        // for free. NetPlayerActors is deterministic (participants sorted by PlayerID) and
        // returns (0,1) when the registry isn't ready yet → solo seat this pass; the Update
        // poll re-runs Recenter once (idx,total) changes.
        int idx = 0, total = 1;
        Quaternion seatYaw = _rigRoot.transform.rotation;
        if (Plugin.SpawnInCircle.Value)
        {
            idx = NetPlayerActors.LocalStableIndex(out total);
            if (total > 1)
            {
                seatYaw = Quaternion.AngleAxis(360f * idx / total, Vector3.up) * _scenarioBaseYaw;
                _rigRoot.transform.rotation = seatYaw;
            }
        }
        _lastCircleIdx = idx;
        _lastCircleTotal = total;

        Vector3 desiredHeadWorld = controller.FocusPoint
                                   + seatYaw * (Vector3.back * (ComfortSettings.EffectiveEyeBackMeters * scale))
                                   + Vector3.up * (ComfortSettings.EffectiveEyeHeightMeters * scale);
        Vector3 headOffsetWorld = seatYaw * (_camera.transform.localPosition * scale);
        _rigRoot.transform.position = desiredHeadWorld - headOffsetWorld;
        RigClamp.Apply(_rigRoot.transform);
        RigPoseVersion++; // P6: world-anchored panels re-derive their seat yaw on recenter

        VRLog.Info("Rig", $"Recentered — head at {desiredHeadWorld}, rig root at {_rigRoot.transform.position} " +
                          $"(circle seat {idx + 1}/{total}).");
    }

    /// <summary>
    /// Menu recenter: put the head back at the menu camera's authored vantage (1:1).
    /// Sign convention (verified against hardware test #3 logs): rig = anchor − yaw·headLocal
    /// puts head world = rig + yaw·headLocal = anchor exactly. With floor-origin
    /// tracking headLocal.y ≈ eye height, so the rig root legitimately sits ~1.1–1.7 m
    /// BELOW the anchor.
    /// </summary>
    private void RecenterMenu()
    {
        if (_rigRoot == null || _camera == null)
            return;
        _rigRoot.transform.rotation = _menuAnchorYaw;
        // Offset with the NEW yaw applied (rig scale is 1 in the menu).
        Vector3 headOffsetWorld = _menuAnchorYaw * _camera.transform.localPosition;
        _rigRoot.transform.position = _menuAnchorPos - headOffsetWorld;
        RigPoseVersion++;
        VRLog.Info("Rig", $"Menu rig recentered at the menu camera vantage (anchor {_menuAnchorPos}, " +
                          $"head local {_camera.transform.localPosition}, rig root {_rigRoot.transform.position}).");
    }

    /// <summary>
    /// WorldScale config wins when &gt; 0; otherwise derive from the runtime hex tile
    /// size (<c>UnityGameEditorRuntime.s_TileSize</c>, BOARD-INPUT §2: x = hex width in
    /// world units) so one hex reads as ~15 cm on the table. Falls back to 12× when the
    /// tile size isn't initialized yet (outside a scenario).
    /// </summary>
    private static float ResolveWorldScale()
    {
        float configured = Plugin.WorldScale.Value;
        if (configured > 0f)
            return Mathf.Clamp(configured, 1f, 100f);

        float tileSize = UnityGameEditorRuntime.s_TileSize.x;
        if (tileSize <= 0.001f)
            return FallbackWorldScale;

        return Mathf.Clamp(tileSize / TargetHexSizeMeters, 1f, 100f);
    }
}
