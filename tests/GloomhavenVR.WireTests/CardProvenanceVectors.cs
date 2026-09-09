using System;
using System.IO;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

internal static class CardProvenanceVectors
{
    internal static void Run(Harness t, string root)
    {
        t.Case("490: held map pool and native appearance provenance survive independent stream timing");
        var rig = new AvatarState { HasHeldCard = true, WorldScale = 1,
            HeldCardPose = new RigPose { Rotation = Quaternion.identity },
            HasHeldMapCard = true, HeldMapKey = 0x01020304, HeldMapPoolSeat = 2, HeldMapPoolCount = 12,
            HeldMapArcSeat = 255 };
        var bytes = new byte[AvatarSerializer.MaxSize];
        int length = AvatarSerializer.Write(rig, bytes);
        var tail = new byte[11]; Array.Copy(bytes, length - 11, tail, 0, 11);
        t.Wire(Hex.Bytes("43 09 04 03 02 01 02 00 0C 00 FF"), tail, 11, "record67 pool identity is positional and independent of old loadout36");
        t.True(AvatarSerializer.TryRead(bytes, length, out var decoded) && decoded.HasHeldMapCard
            && decoded.HeldMapKey == rig.HeldMapKey && decoded.HeldMapPoolSeat == 2
            && decoded.HeldMapPoolCount == 12 && decoded.HeldMapArcSeat == 255,
            "held deselected map card remains resolvable without a loadout address");
        for (int cut = length - 10; cut < length; cut++)
            t.True(!AvatarSerializer.TryRead(bytes, cut, out _), "partial map provenance cannot become a different held card " + cut);
        byte saved = bytes[length - 5]; bytes[length - 5] = 12;
        t.True(!AvatarSerializer.TryRead(bytes, length, out _), "out of pool seat rejected"); bytes[length - 5] = saved;
        rig.HasHeldCard = false;
        length = AvatarSerializer.Write(rig, bytes);
        t.True(AvatarSerializer.TryRead(bytes, length, out decoded) && !decoded.HasHeldMapCard, "release clears map provenance");

        var presence = new PresenceState { HasSecondHeldCard = true,
            SecondHeldCardPose = new RigPose { Rotation = Quaternion.identity },
            HasHeldCardFace = true, SecondHeldFaceCode = 0x21, SecondHeldFaceCount = 5,
            SecondHeldFaceActorId = 1234 };
        bytes = new byte[PresenceSerializer.MaxSize];
        length = PresenceSerializer.Write(presence, bytes);
        t.True(PresenceSerializer.TryRead(bytes, length, out var received) && received.SecondHeldFaceActorId == 1234,
            "second pose carries its own actor independently of board focus");
        presence.HasHeldMapCard = true; presence.HeldMapKey = 99;
        presence.HeldMapPoolSeat = 3; presence.HeldMapPoolCount = 10; presence.HeldMapArcSeat = 4;
        length = PresenceSerializer.Write(presence, bytes);
        t.True(PresenceSerializer.TryRead(bytes, length, out received) && received.HasHeldMapCard
            && received.HeldMapKey == 99 && received.HeldMapPoolSeat == 3 && received.HeldMapArcSeat == 4
            && received.SecondHeldFaceActorId == 0, "map and scenario provenance are exclusive on one pose");
        presence.HasHeldCardFace = false;
        length = PresenceSerializer.Write(presence, bytes);
        t.True(PresenceSerializer.TryRead(bytes, length, out received) && received.HasHeldMapCard,
            "map provenance does not require obsolete loadout membership");

        int mapAt = -1;
        for (int i = 0; i + 11 <= length; i++)
            if (bytes[i] == NetProtocol.ExtIdHeldMapCard && bytes[i + 1] == 9 && bytes[i + 2] == 99) { mapAt = i; break; }
        t.True(mapAt >= 0, "second map fixture contains its addressed record");
        bytes[mapAt + 2] = 0;
        t.True(!PresenceSerializer.TryRead(bytes, length, out _), "invalid map key cannot degrade to legacy second-card source");
        bytes[mapAt + 2] = 99; bytes[mapAt + 6] = 10;
        t.True(!PresenceSerializer.TryRead(bytes, length, out _), "invalid map pool seat rejects the entire paired pose");

        var state = new CardAppearanceState { ActorId = 17, FaceCode = 0x20, ListCount = 2,
            SourceActorId = 0x01020304, PoolSeat = 0x8002, PoolCount = 5,
            Nodes = new[] { new CardAppearanceNode { Role = 0, Flags = 3 } } };
        var frame = new CardAppearanceSnapshot(1, new[] { state });
        bytes = new byte[CardAppearanceCodec.MaxSize];
        length = CardAppearanceCodec.Write(frame, bytes);
        Array.Copy(bytes, length - 11, tail, 0, 11);
        t.Wire(Hex.Bytes("44 09 00 04 03 02 01 02 80 05 00"), tail, 11, "record68 names exact source actor and supply-pool seat");
        t.True(CardAppearanceCodec.TryRead(bytes, length, out var sampled) && sampled!.States[0].SourceActorId == state.SourceActorId
            && sampled.States[0].PoolSeat == state.PoolSeat && sampled.States[0].PoolCount == 5,
            "source population and node values round trip together");
        for (int cut = length - 10; cut < length; cut++)
            t.True(!CardAppearanceCodec.TryRead(bytes, cut, out _), "partial appearance binding rejected " + cut);
        bytes[length - 9] = 1;
        t.True(!CardAppearanceCodec.TryRead(bytes, length, out _), "provenance cannot bind a nonexistent frame card");
        state.PoolSeat = 0x8003;
        var replacement = new CardAppearanceSnapshot(2, new[] { state });
        t.True(!CardAppearanceSnapshot.SameIdentity(frame, replacement), "same dynamic seat/count with another pool card is a stream boundary");
        t.True(!CardAppearanceState.Same(frame.States[0], replacement.States[0]), "unchanged graphics cannot suppress a source change");

        string sampler = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Avatar/LocalRigSampler.cs"));
        t.True(sampler.Contains("card.GameCard?.PlayerActor") && sampler.Contains("chip.Owner?.OwnerActor"),
            "physical held objects supply their actual actor");
        string avatar = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/RemoteAvatar.cs"));
        t.True(avatar.Contains("HeldSlotMatchesBoard(1) && NetProtocol.HeldFaceNamesCard")
            && avatar.Contains("HeldSlotMatchesBoard(2) && NetProtocol.HeldFaceNamesCard"),
            "old-character hold cannot hide new-character arc seats");
        t.True(avatar.Contains("state.HasHeldCard && (state.HasHeldCardFace || state.HasHeldMapCard)")
            && avatar.Contains("p.HasSecondHeldCard && (p.HasHeldCardFace || p.HasHeldMapCard)"),
            "both consumers admit map-only provenance after a held card leaves the loadout");
        string driver = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Avatar/NetAvatarDriver.CardAppearance.cs"));
        t.True(driver.Contains("if (!changed) _appearanceSnapshot = new CardAppearanceSnapshot(now, states);"),
            "steady recovery gets a fresh binding opportunity after model catchup");
    }
}
