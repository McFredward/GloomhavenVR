using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The lightweight rig stream owns board motion after its first explicit board record.
/// A delayed/fragmented presence snapshot must not rewind it or revive a removed board.
/// This state belongs to one RemoteAvatar; reconnect creates a fresh instance.
/// </summary>
internal sealed class RemoteBoardPoseState
{
    private bool _rigOwnsPose;
    internal bool HasBoard { get; private set; }
    internal RigPose Pose { get; private set; } = new() { Rotation = Quaternion.identity };
    internal float Scale { get; private set; } = 1f;

    internal void AcceptRig(in AvatarState state)
    {
        if (!state.HasBoardPose) return;
        _rigOwnsPose = true;
        Set(state.HasBoard, state.BoardPose, state.BoardScale);
    }

    internal void AcceptPresence(in PresenceState state)
    {
        if (!_rigOwnsPose) Set(state.HasBoard, state.Board, state.BoardScale);
    }

    private void Set(bool visible, RigPose pose, float scale)
    {
        HasBoard = visible;
        if (!visible) return;
        Pose = pose;
        Scale = scale > 0f ? scale : 1f;
    }
}
