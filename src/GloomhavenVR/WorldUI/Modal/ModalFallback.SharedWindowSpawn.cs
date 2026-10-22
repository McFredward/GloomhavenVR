using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    /// <summary>The build-717 user ruling replaces deterministic per-viewer seats with one
    /// host-gaze spawn in the shared game world. The author's head controls placement only;
    /// the render scale comes from common scene geometry. Peers do not sample their own head,
    /// arc or overlap population: their hidden frame waits for the current opening's author.</summary>
    private static bool ComputeSharedHostSpawn(ConvertedPanel panel, SpawnAnchor? replay,
        Vector2 halfSize, out Vector3 position, out Quaternion rotation, out float placementScale)
    {
        placementScale = PanelLayout.WorldScale;
        rotation = Quaternion.identity;
        position = Vector3.zero;
        s_lastSpawnAnchor = default;
        if (panel.SharedSpawnPoseReady && panel.HostGo != null)
        {
            position = panel.HostGo.transform.position;
            rotation = panel.HostGo.transform.rotation;
            return true;
        }
        if (!SharedWindowSpawnBridge.IsInitialAuthor)
            return true; // never revealed or interactive until TryReveal applies the shared pose

        Vector3 headPosition, headForward, raw;
        if (replay.HasValue && replay.Value.Valid)
        {
            SpawnAnchor anchor = replay.Value;
            headPosition = anchor.HeadPos;
            headForward = anchor.HeadForward;
            raw = anchor.RawPos;
            placementScale = anchor.Scale;
        }
        else
        {
            Camera? head = CanvasConversion.WorldCamera;
            if (head == null) return false;
            headPosition = head.transform.position;
            headForward = head.transform.forward;
            raw = headPosition + headForward * (WindowDistanceMeters * placementScale);
        }
        s_lastSpawnAnchor = new SpawnAnchor
        {
            Valid = true, HeadPos = headPosition, HeadForward = headForward,
            RawPos = raw, Scale = placementScale, ArcSlot = -1,
        };
        position = raw;
        ClampSpawnPose(headPosition, headForward, ref position, placementScale, halfSize,
                       MaxSpawnPitchDeg, centreAtEyeLevel: true, out HeightDecision _);
        Vector3 facing = position - headPosition;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f)
        {
            facing = headForward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        }
        rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
        PanelPoseWatch.Announce(panel, PanelPoseWatch.Writer.Placement,
            replay.HasValue ? "shared host spawn at final fitted geometry" : "shared host spawn");
        VRLog.Info("WorldUI", $"SHARED WINDOW HOST SPAWN: kind={panel.SharedDesignKind}, "
            + $"stage={(replay.HasValue ? "final-fit" : "spawn")}, "
            + $"position=({position.x:F3},{position.y:F3},{position.z:F3}), "
            + $"yaw={rotation.eulerAngles.y:F2}, fixedScale={SharedWindowSize.ReferenceWorldScale:F3}; "
            + "only the elected author's gaze places this opening; viewer zoom never sizes it.");
        return true;
    }

    internal static bool SharedInitialPoseReady(SharedWindowKind kind)
    {
        foreach (WindowPanel wp in Converted)
        {
            if (!wp.Panel.IsAlive || wp.Panel.SharedDesignKind != kind) continue;
            // A native window can be converted and revealed while still private, then join a
            // network session without reopening. There is no second CompleteReveal edge. The
            // elected author's standing pose is already readable and remains authoritative;
            // publishing it must not re-run gaze placement or import a follower's private seat.
            if (!wp.Panel.SharedSpawnPoseReady && !wp.Panel.RevealPending
                && SharedWindowSpawnBridge.Handles(wp.Panel)
                && SharedWindowSpawnBridge.IsInitialAuthor)
                wp.Panel.SharedSpawnPoseReady = true;
            if (wp.Panel.SharedSpawnPoseReady) return true;
        }
        return false;
    }

    internal static bool ApplySharedInitialPose(ConvertedPanel panel, Vector3 position,
                                                Quaternion rotation, float factor)
    {
        foreach (WindowPanel wp in Converted)
        {
            if (!ReferenceEquals(wp.Panel, panel) || wp.Grab == null || wp.Grab.IsGrabbed) continue;
            Transform? frame = ((IPanelGrabOwner)wp.Grab).GrabRoot;
            if (frame == null) return false;
            frame.localScale = Vector3.one * SharedWindowSizeLaw.SharedGrabFactor(factor);
            // The first visible sample has no interpolation from a viewer-local provisional seat.
            wp.Grab.SnapFrameTo(position, rotation);
            wp.SpawnAnchor = default;
            wp.PoseRePlaceDone = true;
            panel.SharedSpawnPoseReady = true;
            NoteSharedAnchorSpent(panel.SharedDesignKind, "current opening's initial author pose applied");
            return true;
        }
        return false;
    }
}
