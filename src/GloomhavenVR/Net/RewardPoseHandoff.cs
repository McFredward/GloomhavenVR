namespace GloomhavenVR.Net;

/// <summary>The first visible reward pose must not interpolate backwards through an invisible
/// local spawn position. Keep the handoff armed across identity-settle/no-pose early returns.</summary>
internal sealed class RewardPoseHandoff
{
    private bool _firstVisible = true;

    internal void Reset() => _firstVisible = true;
    internal void LocalMove() => _firstVisible = false;

    internal RigPose Sample(SharedWindowPoseTrack track, RigPose target, float size, float now,
        bool revealPending, out float displayedSize)
    {
        if (revealPending || _firstVisible) track.Seed(target, size, now);
        RigPose displayed = track.Sample(target, size, now, out displayedSize);
        if (!revealPending) _firstVisible = false;
        return displayed;
    }
}
