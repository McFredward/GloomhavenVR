using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Net writes the elected pose; conversion reads it before revealing a late local window.</summary>
internal static class RewardShowcasePlacement
{
    private static uint _key;
    private static Vector3 _position;
    private static Quaternion _rotation;
    private static float _size;
    private static float _receivedAt;

    internal static void SetInitialPose(uint key, Vector3 position, Quaternion rotation, float size)
    {
        _key = key;
        _position = position;
        _rotation = rotation;
        _size = size;
        _receivedAt = Time.unscaledTime;
    }

    internal static void ClearInitialPose() => _key = 0;

    internal static bool ApplyInitialPose(UIWindow window, GrabbableModal? grab)
    {
        if (_key == 0 || grab == null || Time.unscaledTime - _receivedAt > 2f
            || !SharedWindows.ParticipatesHere(SharedWindowKind.RewardShowcase)
            || !ReferenceEquals(RewardShowcase.Window, window)
            || RewardShowcaseIdentity.ContentKey(window) != _key) return false;
        Transform? root = ((IPanelGrabOwner)grab).GrabRoot;
        if (root == null) return false;
        root.localScale = Vector3.one * SharedWindowSizeLaw.SharedGrabFactor(_size);
        grab.PlaceFrameAt(_position, _rotation);
        ModalFallback.NoteSharedAnchorSpent(SharedWindowKind.RewardShowcase, "received reward pose before reveal");
        VRLog.Info("WorldUI", $"REWARD WINDOW INITIAL POSE: key={_key:X8}, applied before reveal");
        return true;
    }
}
