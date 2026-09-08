using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class SharedWindowPoseVectors
{
    internal static void Run(Harness t)
    {
        t.Case("shared windows: received drag poses interpolate, duplicates do not restart");
        var track = new SharedWindowPoseTrack();
        RigPose Pose(float x) => new() { Position = new Vector3(x, 0, 0), Rotation = Quaternion.identity };
        track.Seed(Pose(0), 1, 0);
        RigPose p = track.Sample(Pose(2), 2, .2f, out float size);
        t.Equal(0f, p.Position.x, "first packet retains the previously displayed endpoint");
        p = track.Sample(Pose(2), 2, .3f, out size);
        t.True(p.Position.x > .99f && p.Position.x < 1.01f, "next rendered frame reaches the actual midpoint");
        t.True(size > 1.49f && size < 1.51f, "resize follows the same interpolation clock");
        p = track.Sample(Pose(2), 2, .4f, out size);
        t.Equal(2f, p.Position.x, "identical repeated packet does not postpone the endpoint");
        p = track.Sample(Pose(2), 2, 10, out size);
        t.Equal(2f, p.Position.x, "missing packets hold the final position without extrapolation");
        p = track.Sample(Pose(4), 3, 10, out size);
        t.Equal(2f, p.Position.x, "motion after idle starts from the held position");
        p = track.Sample(Pose(4), 3, 10.21f, out size);
        t.Equal(4f, p.Position.x, "idle interval does not become seconds of slow motion");
        track.Reset();
        p = track.Sample(Pose(8), 4, 11, out size);
        t.Equal(8f, p.Position.x, "different window identity starts without stale predecessor");
        track.Seed(Pose(0), 1, 12);
        track.Sample(Pose(2), 2, 12.2f, out size);
        p = track.Sample(Pose(4), 3, 12.3f, out size);
        t.True(p.Position.x > .99f && p.Position.x < 1.01f, "mid-motion retarget retains the picture already displayed");
    }
}
