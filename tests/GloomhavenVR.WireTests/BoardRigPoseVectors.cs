using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class BoardRigPoseVectors
{
    internal static void Run(Harness t)
    {
        t.Case("board rig pose: additive bytes retain the complete legacy prefix");
        var legacy = new AvatarState { WorldScale = 1 };
        var bytes = new byte[AvatarSerializer.MaxSize];
        int prefixLength = AvatarSerializer.Write(in legacy, bytes);
        var prefix = new byte[prefixLength];
        Array.Copy(bytes, prefix, prefixLength);
        var state = legacy;
        state.HasBoardPose = true;
        state.HasBoard = true;
        state.BoardPose = Pose(1, 2, 3);
        state.BoardScale = 2;
        int length = AvatarSerializer.Write(in state, bytes);
        var expected = new byte[prefix.Length + 27];
        Array.Copy(prefix, expected, prefix.Length);
        byte[] tail = Hex.Bytes("46 19 01 00 00 80 3F 00 00 00 40 00 00 40 40 00 00 00 00 00 00 FF 7F 00 00 00 40");
        Array.Copy(tail, 0, expected, prefix.Length, tail.Length);
        t.Wire(expected, bytes, length, "record70 has flag, float positions, quantized rotation and exact scale");
        t.True(AvatarSerializer.TryRead(bytes, length, out AvatarState read), "new board packet reads");
        t.True(read.HasBoardPose && read.HasBoard, "explicit modern presence survives");
        t.Equal(3f, read.BoardPose.Position.z, "sampled world position survives");
        t.Equal(2f, read.BoardScale, "board scale is independent of rig scale");
        t.True(AvatarSerializer.TryRead(prefix, prefix.Length, out read) && !read.HasBoardPose,
            "legacy rig remains accepted without pretending the board is absent");

        t.Case("board rig pose: malformed atomic records cannot supply partial motion");
        for (int n = prefixLength + 1; n < length; n++)
            t.True(!AvatarSerializer.TryRead(bytes, n, out _), "every known-record truncation rejects at " + n);
        foreach (byte bad in new byte[] { 0, 2, 24, 26, 255 })
        {
            var broken = (byte[])bytes.Clone();
            broken[prefixLength + 1] = bad;
            t.True(!AvatarSerializer.TryRead(broken, length, out _), "invalid payload length rejects " + bad);
        }
        foreach (byte bad in new byte[] { 0, 2, 255 })
        {
            var broken = (byte[])bytes.Clone();
            broken[prefixLength + 2] = bad;
            t.True(!AvatarSerializer.TryRead(broken, length, out _), "flags must match exact payload " + bad);
        }
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f })
        {
            var broken = (byte[])bytes.Clone();
            Array.Copy(BitConverter.GetBytes(bad), 0, broken, length - 4, 4);
            t.True(!AvatarSerializer.TryRead(broken, length, out _), "invalid scale rejects " + bad);
        }
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var broken = (byte[])bytes.Clone();
            Array.Copy(BitConverter.GetBytes(bad), 0, broken, prefixLength + 3, 4);
            t.True(!AvatarSerializer.TryRead(broken, length, out _), "nonfinite position rejects " + bad);
        }
        var duplicate = new byte[length + tail.Length];
        Array.Copy(bytes, duplicate, length);
        Array.Copy(tail, 0, duplicate, length, tail.Length);
        t.True(!AvatarSerializer.TryRead(duplicate, duplicate.Length, out _), "ambiguous duplicate board records reject");
        var future = new byte[length + 4];
        Array.Copy(bytes, future, prefixLength);
        future[prefixLength] = 250; future[prefixLength + 1] = 2;
        future[prefixLength + 2] = 7; future[prefixLength + 3] = 8;
        Array.Copy(tail, 0, future, prefixLength + 4, tail.Length);
        t.True(AvatarSerializer.TryRead(future, future.Length, out read) && read.HasBoard,
            "unknown preceding TLV skips by length without swallowing the board record");
        state.HasBoard = false;
        length = AvatarSerializer.Write(in state, bytes);
        t.Equal(prefixLength + 3, length, "explicit despawn costs three bytes");
        t.True(AvatarSerializer.TryRead(bytes, length, out read) && read.HasBoardPose && !read.HasBoard,
            "despawn is distinct from a legacy packet");

        t.Case("board rig pose: delayed extras cannot rewind or revive modern motion");
        var target = new RemoteBoardPoseState();
        var extras = new PresenceState { HasBoard = true, Board = Pose(2, 0, 0), BoardScale = 1 };
        target.AcceptPresence(in extras);
        t.True(target.HasBoard && target.Pose.Position.x == 2, "legacy/join fallback remains available");
        state.HasBoard = true; state.BoardPose = Pose(4, 0, 0); state.BoardScale = 3;
        target.AcceptRig(in state);
        target.AcceptPresence(in extras);
        t.True(target.HasBoard && target.Pose.Position.x == 4 && target.Scale == 3,
            "earlier fragmented presence cannot replace the head-aligned pose or scale");
        state.HasBoard = false;
        target.AcceptRig(in state);
        target.AcceptPresence(in extras);
        t.True(!target.HasBoard, "delayed presence cannot revive despawned board");
        target.AcceptRig(in legacy);
        target.AcceptPresence(in extras);
        t.True(!target.HasBoard, "legacy packet after modern ownership cannot reopen fallback");
        state.HasBoard = true; state.BoardPose = Pose(-5, 6, 7);
        target.AcceptRig(in state);
        t.True(target.HasBoard && target.Pose.Position.x == -5, "recenter/new spawn adopts exact owner pose");
        for (int frame = 0; frame < 20; frame++)
        {
            // Follow/fixed are deliberately absent from this contract: actual owner poses
            // supply motion. An assumed head attachment would move the fixed case wrongly.
            state.Head = Pose(frame * 2, 1, 0);
            state.BoardPose = Pose(frame < 10 ? frame : 9, 0, 0);
            target.AcceptRig(in state);
            target.AcceptPresence(in extras);
            t.Equal(frame < 10 ? (float)frame : 9f, target.Pose.Position.x,
                "follow motion and fixed pose retain each atomic target " + frame);
        }
        var reconnect = new RemoteBoardPoseState();
        reconnect.AcceptPresence(in extras);
        t.True(reconnect.HasBoard && reconnect.Pose.Position.x == 2, "new peer lifetime restores legacy fallback");

        t.Case("board arrival: actual seat faces player horizontally regardless of height");
        Vector3 head = new(10, 20, 30);
        foreach (Vector3 seat in new[] { new Vector3(-2, -3, 1), new Vector3(2, -8, 1),
                     new Vector3(0, 4, -2), new Vector3(-40, -200, -10) })
        {
            Vector3 heading = BoardPlacementPose.Heading(head, head + seat, Vector3.forward);
            Vector3 towardSeat = new Vector3(seat.x, 0, seat.z).normalized;
            t.True(Vector3.Dot(heading, towardSeat) > .99999f,
                "left/right/behind/scaled arrival points the board's player edge toward the head");
            t.Equal(0f, heading.y, "spawn heading adds neither pitch nor roll");
        }
        Vector3 fallback = BoardPlacementPose.Heading(head, head + Vector3.up, Vector3.right);
        t.True(fallback.x == 1 && fallback.y == 0, "degenerate seat retains valid head heading");
        fallback = BoardPlacementPose.Heading(head, head, Vector3.up);
        t.True(fallback.z == 1, "vertical head and coincident board retain finite fallback");
    }

    private static RigPose Pose(float x, float y, float z) => new()
        { Position = new Vector3(x, y, z), Rotation = Quaternion.identity };
}
