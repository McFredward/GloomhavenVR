using System;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class HeldFaceAtomicVectors
{
    internal static void Run(Harness t)
    {
        t.Case("489: held pose, source seat and actor arrive atomically");
        var state = new AvatarState { HasHeldCard = true, WorldScale = 1,
            HeldCardPose = new RigPose { Rotation = Quaternion.identity }, HasHeldCardFace = true,
            HeldFaceActorId = 0x01020304, HeldFaceCode = 0x21, HeldFaceCount = 7 };
        var buffer = new byte[AvatarSerializer.MaxSize];
        int size = AvatarSerializer.Write(state, buffer);
        var tail = new byte[12]; Array.Copy(buffer, size - 12, tail, 0, 12);
        t.Wire(Hex.Bytes("24 04 21 07 00 00 3B 04 04 03 02 01"), tail, 12, "old36 grammar then actor59");
        t.True(AvatarSerializer.TryRead(buffer, size, out var decoded) && decoded.HasHeldCardFace
            && decoded.HeldFaceActorId == 0x01020304 && decoded.HeldFaceCode == 0x21, "same packet contains ready source");
        t.True(AvatarSerializer.TryRead(buffer, size - 12, out decoded) && decoded.HasHeldCard
            && !decoded.HasHeldCardFace, "legacy pose remains valid but cannot flash a guessed front");
        t.True(AvatarSerializer.TryRead(buffer, size - 6, out decoded) && !decoded.HasHeldCardFace,
            "address without actor cannot be attached to a different character");
        for (int cut = size - 11; cut < size; cut++)
            if (cut != size - 6) t.True(!AvatarSerializer.TryRead(buffer, cut, out _), "truncated atomic tail rejected " + cut);
        t.True(!AvatarSerializer.TryRead(buffer, buffer.Length + 1, out _), "declared size cannot exceed array");
        state.HasHeldCard = false;
        size = AvatarSerializer.Write(state, buffer);
        t.True(AvatarSerializer.TryRead(buffer, size, out decoded) && !decoded.HasHeldCardFace,
            "release clears readiness instead of latching previous held artwork");
    }
}
