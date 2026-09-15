using System;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class VideoWindowVectors
{
    internal static void Run(Harness t)
    {
        t.Case("video window: additive72 golden native publication and untouched old records");
        var buffer = new byte[PresenceSerializer.MaxSize];
        var state = new PresenceState { HasVideoWindow = true,
            VideoWindow = new VideoWindowState { Native = true, Playing = true, Token = 1,
                Revision = 2, Millis = 1000, Clip = "Heroes/A.mov",
                Window = new SharedWindowEntry { ContentKey = 0x12345678 } } };
        int length = PresenceSerializer.Write(in state, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 48 1F 03 01 00 00 00 02 00 00 00 E8 03 00 00 0C 48 65 72 6F 65 73 2F 41 2E 6D 6F 76 78 56 34 12 00"),
            buffer, length, "native movie source has fixed fields and allowlisted relative ASCII clip");
        t.True(PresenceSerializer.TryRead(buffer, length, out PresenceState decoded)
            && decoded.HasVideoWindow && decoded.VideoWindow.Native && decoded.VideoWindow.Playing
            && decoded.VideoWindow.Clip == "Heroes/A.mov" && decoded.VideoWindow.Millis == 1000,
            "golden movie source reads");
        state.HasVideoWindow = false;
        length = PresenceSerializer.Write(in state, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), buffer, length, "no movie leaves the legacy packet untouched");

        t.Case("video window: bounded pose payload, truncation and malformed paths");
        VideoWindowState video = new() { Native = true, Token = 2, Revision = 3,
            Clip = "CP_Intro/GH_CP_Intro.mov", Window = new SharedWindowEntry {
                ContentKey = 42, Flags = NetProtocol.SharedPoseBit, PoseStamp = 7,
                SizeCode = NetProtocol.StorySizeDefaultCode, Frame = NetProtocol.SharedFrameSeatAnchor,
                Pose = new RigPose { Position = new Vector3(1, 2, 3), Rotation = Quaternion.identity } } };
        int at = 0;
        t.True(VideoWindowCodec.Write(buffer, ref at, in video), "pose record writes");
        t.True(VideoWindowCodec.TryRead(buffer, 2, at - 2, out VideoWindowState read)
            && read.Window.ContentKey == 42 && read.Window.Pose.Position.x == 1
            && read.Window.PoseStamp == 7, "actual pose and playback identity decode together");
        for (int truncated = 0; truncated < at - 2; truncated++)
            t.True(!VideoWindowCodec.TryRead(buffer, 2, truncated, out _), "truncated record cannot apply any video or pose " + truncated);
        foreach (string bad in new[] { "Heroes/../secret.mov", "https://example.org/a.mov", "/tmp/Heroes/A.mov", "Heroes/A\\B.mov", "Heroes/ä.mov", "" })
            t.True(!VideoWindowCodec.ValidClip(bad), "path cannot escape local movie namespace: " + bad);
        byte[] malformed = (byte[])buffer.Clone();
        malformed[2] = 0x80;
        t.True(!VideoWindowCodec.TryRead(malformed, 2, at - 2, out _), "unknown flags reject");
        malformed[2] = 7;
        t.True(!VideoWindowCodec.TryRead(malformed, 2, at - 2, out _), "closed cannot simultaneously be playing");
        malformed = (byte[])buffer.Clone();
        malformed[at - 21] = 255;
        t.True(!VideoWindowCodec.TryRead(malformed, 2, at - 2, out _), "unknown shared coordinate frame rejects");
        int unchanged = 1;
        t.True(!VideoWindowCodec.Write(new byte[10], ref unchanged, in video) && unchanged == 1,
            "small output buffer stays atomic");
        var maximum = video;
        maximum.Clip = "Heroes/" + new string('A', 149) + ".mov";
        int maximumLength = 0;
        t.True(VideoWindowCodec.Write(buffer, ref maximumLength, in maximum), "maximum relative clip fits");
        t.Equal(204, maximumLength, "maximum additive video record costs exactly204 bytes");
        t.True(3840 + maximumLength <= ExtrasFragments.MaxSnapshotBytes
            && PresenceSerializer.MaxSize - (3840 + maximumLength) >= 257,
            "all prior worst-case fields plus video retain reassembly limit and full-record spare margin");

        t.Case("video window: production source guard rejects loops, replay and late packets");
        var clock = new VideoSourceClock();
        t.True(clock.Observe(in video, 1) && clock.HasSource, "first native source opens");
        t.True(!clock.Observe(in video, 2) && clock.At == 1, "duplicate does not refresh freshness");
        video.Revision = 4;
        video.Closed = true;
        t.True(clock.Observe(in video, 3) && !clock.HasSource, "explicit native stop closes");
        video.Closed = false;
        video.Revision = 5;
        t.True(!clock.Observe(in video, 4), "even a later stale playing packet cannot restart retired token");
        video.Token = 3;
        t.True(clock.Observe(in video, 5) && clock.HasSource, "new native play of same clip opens");
        video.Native = false;
        video.Revision = 6;
        t.True(!clock.Observe(in video, 6), "cosmetic mirror never becomes a playback source");
        video.Native = true;
        clock.Retire();
        t.True(!clock.Observe(in video, 7), "decoder completion/error blocks restart from still-arriving source snapshots");
        video.Token = 4;
        t.True(clock.Observe(in video, 8), "next native movie remains possible after decoder retirement");
        video.Token = 3;
        video.Revision = 100;
        t.True(!clock.Observe(in video, 9) && clock.LastToken == 4,
            "old movie cannot replace the next movie even with higher packet revision");
    }
}
