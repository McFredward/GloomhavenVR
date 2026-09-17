using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Cards;

internal sealed partial class PlayTray
{
    private object? _retryScenarioOwner;

    // Capture after pin carry and the arrival guard have authored their final output. Only the
    // first board at each automatic arrival revision is accepted by the rig; B+Y, grabbing and
    // later configuration changes cannot rewrite the retry baseline. The snapshot lives with
    // the rig's scenario identity because a tray/root can be destroyed while loading the retry.
    private void RememberRetryStart()
    {
        if (RetryBindingReady() && _root != null && _placed && (_handle == null || !_handle.IsGrabbed))
            VRRigDriver.RememberScenarioBoard(_root.position, _root.rotation, _root.lossyScale.x);
    }

    internal static void RequestRetryReset() => Current?.TryRestoreRetryStart();

    private bool RetryBindingReady() =>
        _root != null && VRRigDriver.RigRoot != null && VRRigDriver.HeadCamera != null
        && !CardsDriver.NativeSceneLoadInProgress
        && ReferenceEquals(_retryScenarioOwner, Choreographer.s_Choreographer)
        && _anchorParent != null && _anchorParent.IsChildOf(VRRigDriver.RigRoot);

    private bool TryRestoreRetryStart()
    {
        if (!RetryBindingReady() || _root == null || (_handle != null && _handle.IsGrabbed))
            return false;
        if (!VRRigDriver.TryTakeRetryBoard(out VRRigDriver.RetryBoardPose pose))
            return false;
        // Use the actual original world pose, not mutable TrayOffset/BoardPosOffset or today's
        // projected HMD forward. This includes SpawnLeftOfHead=false and changed pitch/roll.
        _placed = true;
        _everPlaced = true;
        ApplyFollowMode();
        _root.position = pose.Position;
        _root.rotation = pose.Rotation;
        float parentScale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        _root.localScale = Vector3.one * (pose.WorldScale / Mathf.Max(1e-4f, parentScale));
        NotePinnedWrite("scenario retry restored the original board arrival pose");
        _pinHousekeepingMove = "scenario retry restored the original board arrival pose";
        _anchor.ReauthorOrigin();
        _anchor.RecacheRigLocal(_root);
        // Do not let the old arrival guard re-solve this explicitly restored board next tick.
        _arrivalArmed = false;
        _arrivalPendingSeen = false;
        return true;
    }
}
