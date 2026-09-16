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
    private static uint _authorityKey;
    private static bool _mayReveal;
    private static bool _poseReady;

    internal static bool LocalPlacementFailed => ModalFallback.RewardPlacementFailed(RewardShowcase.Window);
    internal static bool LocalRevealPending => ModalFallback.PanelFor(RewardShowcase.Window)?.RevealPending == true;

    internal static void SetInitialAuthority(uint key, bool mayReveal)
    {
        _authorityKey = key;
        _mayReveal = mayReveal;
    }

    internal static void ClearInitialAuthority()
    {
        _authorityKey = 0;
        _mayReveal = false;
    }

    internal static void SetInitialPose(uint key, Vector3 position, Quaternion rotation, float size, bool ready = true)
    {
        _key = key;
        _position = position;
        _rotation = rotation;
        _size = size;
        _receivedAt = Time.unscaledTime;
        _poseReady = ready;
    }

    internal static void ClearInitialPose() => _key = 0;

    /// <summary>Only a follower's first reveal waits; native confirmation and other panels do not.</summary>
    internal static bool TryReveal(ConvertedPanel panel)
    {
        UIWindow? window = RewardShowcase.Window;
        if (window == null || !ReferenceEquals(ModalFallback.PanelFor(window), panel)
            || !SharedWindows.ParticipatesHere(SharedWindowKind.RewardShowcase)) return true;
        uint key = RewardShowcaseIdentity.ContentKey(window);
        // An absent/disabled net module must never turn a local native reward into a new deadlock.
        // Once net supplies an authority for this chest, only it may release this presentation gate.
        if (key == 0 || _authorityKey != key) return true;
        if (_poseReady && ModalFallback.TryGetGrabFor(window, out GrabbableModal? grab) && ApplyInitialPose(window, grab))
        {
            ModalFallback.PreserveRewardInitialPose(window);
            return true;
        }
        return _mayReveal;
    }

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
