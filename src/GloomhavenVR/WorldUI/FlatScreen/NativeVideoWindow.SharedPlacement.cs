using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class NativeVideoWindow
{
    private static bool _sharedPlacementPending;
    private static bool SharedPlacementRequired => SharedWindows.ParticipatesHere(SharedWindowKind.Video);

    /// <summary>The first physical pose belongs to the host/VR surrogate, independently of
    /// which native or cosmetic decoder supplies the pixels. The transport reads this only
    /// for the current existing movie panel; a replacement starts with a fresh lease.</summary>
    internal static bool SharedInitialPoseReady => _root != null && _panel != null
        && _panel.SharedSpawnPoseReady;

    private static void PrepareSharedPlacement()
    {
        if (_panel == null) return;
        _sharedPlacementPending = SharedPlacementRequired && !SharedWindowSpawnBridge.IsInitialAuthor;
        _panel.RevealPending = _sharedPlacementPending;
        _panel.SharedSpawnPoseReady = SharedPlacementRequired && !_sharedPlacementPending;
        if (_sharedPlacementPending)
            CanvasConversion.SetPanelRenderVisible(_panel, visible: false);
    }

    private static bool TryRevealSharedPlacement()
    {
        if (_panel == null || _grab == null) return false;
        if (!SharedPlacementRequired)
        {
            // Leaving a session cannot strand a still-running native movie at its hidden
            // provisional pose. It has never been visible or held, so local placement is safe.
            if (_sharedPlacementPending && CanvasConversion.WorldCamera != null)
            {
                Camera head = CanvasConversion.WorldCamera;
                PanelPlacement.Spawn(head, PanelLayout.WorldScale, out Vector3 local, out Quaternion facing);
                local.y = head.transform.position.y;
                _grab.SnapFrameTo(local, facing);
            }
            _sharedPlacementPending = false;
            _panel.RevealPending = false;
            _panel.SharedSpawnPoseReady = false;
            if (_panel.RenderHidden) CanvasConversion.SetPanelRenderVisible(_panel, visible: true);
            return true;
        }
        if (_panel.SharedSpawnPoseReady) return true;
        if (SharedWindowSpawnBridge.IsInitialAuthor)
        {
            // A host may be showing another player's decoder. Waiting for that player's
            // physical pose would deadlock the very host pose that the room is awaiting.
            _panel.SharedSpawnPoseReady = true;
        }
        else
        {
            _sharedPlacementPending = true;
            _panel.RevealPending = true;
            CanvasConversion.SetPanelRenderVisible(_panel, visible: false);
            var reader = SharedWindowSpawnBridge.TryInitialPose;
            if (reader == null || !reader(SharedWindowKind.Video, out Vector3 position,
                    out Quaternion rotation, out float factor)) return false;
            Transform? frame = ((IPanelGrabOwner)_grab).GrabRoot;
            if (frame == null) return false;
            frame.localScale = Vector3.one * SharedWindowSizeLaw.SharedGrabFactor(factor);
            _grab.SnapFrameTo(position, rotation);
            _panel.SharedSpawnPoseReady = true;
        }
        _sharedPlacementPending = false;
        _panel.RevealPending = false;
        if (_panel.RenderHidden) CanvasConversion.SetPanelRenderVisible(_panel, visible: true);
        return true;
    }
}
