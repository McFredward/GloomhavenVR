using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Named layout slots for world panels arranged around the table.</summary>
internal enum PanelSlot
{
    InitiativeTrack,
    ElementBoard,
    Objectives,
    CombatLog,
    StatPanel,
    PropInfo,
    ButtonCluster,
}

/// <summary>
/// Curved arrangement helper: computes world poses for panel slots around the
/// diorama table. The layout anchor is the camera orbit focus (the table center
/// the Phase-1 rig recenters against), yawed toward the seat the player last
/// recentered at. All slot offsets are REAL-WORLD meters (multiplied by the
/// diorama scale), so panels keep their physical size and placement regardless
/// of the table's game-unit scale.
///
/// WORLD-ANCHORED since P6 (test #8, "fix in der Welt platziert"): the seat yaw
/// is CACHED per <see cref="Rig.VRRigDriver.RigPoseVersion"/> — i.e. re-derived
/// only when the rig is (re)built or deliberately recentered — instead of read
/// from the live rig every frame. Snap turns and world-grab therefore leave the
/// panels standing at the table like physical objects (they still move/scale
/// with the diorama itself, because they are placed in world space at the table
/// anchor). `[WorldUI] PanelsFollowView = true` restores the legacy per-frame
/// rig-yaw follow.
/// </summary>
internal static class PanelLayout
{
    // Cached seat yaw (world anchoring): valid for one (rig instance, pose version).
    private static int _cachedPoseVersion = -1;
    private static Transform? _cachedRig;
    private static Quaternion _cachedYaw = Quaternion.identity;
    private struct Slot
    {
        public float AzimuthDeg;  // 0 = straight ahead of the player, + = right
        public float Distance;    // meters from table center
        public float Height;      // meters above table plane
        public float PitchDeg;    // extra tilt toward the player (+ = top leans forward)
    }

    // Curved arc behind/above the far table edge; buttons near the player edge.
    private static Slot GetSlot(PanelSlot slot) => slot switch
    {
        PanelSlot.InitiativeTrack => new Slot { AzimuthDeg = 0f, Distance = 0.95f, Height = 0.45f, PitchDeg = 12f },
        PanelSlot.ElementBoard => new Slot { AzimuthDeg = 32f, Distance = 0.90f, Height = 0.35f, PitchDeg = 12f },
        PanelSlot.Objectives => new Slot { AzimuthDeg = -34f, Distance = 1.05f, Height = 0.50f, PitchDeg = 10f },
        PanelSlot.CombatLog => new Slot { AzimuthDeg = 56f, Distance = 1.10f, Height = 0.45f, PitchDeg = 10f },
        PanelSlot.StatPanel => new Slot { AzimuthDeg = -18f, Distance = 0.70f, Height = 0.30f, PitchDeg = 18f },
        // Low in view near the player edge, opposite side of the stat panel — out of
        // the board-hover ray path (test #18: the hover keeps this card alive).
        PanelSlot.PropInfo => new Slot { AzimuthDeg = 22f, Distance = 0.62f, Height = 0.20f, PitchDeg = 22f },
        PanelSlot.ButtonCluster => new Slot { AzimuthDeg = 12f, Distance = -0.42f, Height = 0.02f, PitchDeg = 0f },
        _ => default,
    };

    /// <summary>Diorama scale (game units per real meter); 1 outside a scenario.</summary>
    internal static float WorldScale
    {
        get
        {
            Transform? rig = Rig.VRRigDriver.RigRoot;
            return rig != null ? Mathf.Max(rig.lossyScale.x, 0.01f) : 1f;
        }
    }

    /// <summary>
    /// Layout anchor: position at the orbit focus (table center, live — panels follow
    /// a genuine table move), yaw of the seat the player last recentered at (cached —
    /// see class doc; live rig yaw only with [WorldUI] PanelsFollowView). Falls back
    /// to 1.5 m in front of the main camera when no scenario/rig exists (dev preview).
    /// </summary>
    internal static bool TryGetAnchor(out Vector3 position, out Quaternion yaw)
    {
        Transform? rig = Rig.VRRigDriver.RigRoot;
        CameraController controller = CameraController.s_CameraController;
        if (rig != null && controller != null)
        {
            position = controller.FocusPoint;

            if (WorldUIConfig.PanelsFollowView.Value)
            {
                // Legacy: follow the live rig yaw (panels swing with snap turns).
                yaw = Quaternion.Euler(0f, rig.eulerAngles.y, 0f);
                return true;
            }

            // World anchoring: re-derive the seat yaw only when the rig was rebuilt
            // or recentered (RigPoseVersion bumps there and nowhere else).
            int version = Rig.VRRigDriver.RigPoseVersion;
            if (version != _cachedPoseVersion || !ReferenceEquals(rig, _cachedRig))
            {
                _cachedPoseVersion = version;
                _cachedRig = rig;
                _cachedYaw = Quaternion.Euler(0f, rig.eulerAngles.y, 0f);
            }
            yaw = _cachedYaw;
            return true;
        }

        Camera? cam = Camera.main;
        if (cam != null)
        {
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f)
                fwd = Vector3.forward;
            fwd.Normalize();
            position = cam.transform.position + fwd * 1.5f - Vector3.up * 0.3f;
            yaw = Quaternion.LookRotation(fwd, Vector3.up);
            return true;
        }

        position = Vector3.zero;
        yaw = Quaternion.identity;
        return false;
    }

    /// <summary>
    /// World pose for a slot. Panels face back toward the player seat (their +Z
    /// away from the table center, uGUI front toward the player), tilted by the
    /// slot pitch. Returns false when no anchor exists yet.
    /// </summary>
    internal static bool TryGetPose(PanelSlot slot, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (!TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return false;

        Slot s = GetSlot(slot);
        float scale = WorldScale;

        Quaternion azimuth = yaw * Quaternion.Euler(0f, s.AzimuthDeg, 0f);
        Vector3 radial = azimuth * Vector3.forward; // away from player, across the table
        position = anchor + radial * (s.Distance * scale) + Vector3.up * (s.Height * scale);

        // uGUI canvases render their front along -forward: pointing the host's +Z
        // away from the player makes the panel face the player.
        rotation = azimuth * Quaternion.Euler(-s.PitchDeg, 0f, 0f);
        return true;
    }
}
