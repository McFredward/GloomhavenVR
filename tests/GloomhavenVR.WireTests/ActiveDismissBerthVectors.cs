using System;
using System.Linq;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

/// <summary>Byte-exact active-card staging presentation. Exact stable actor provenance binds
/// the public counted seat; no card identity or action command crosses this presentation wire.</summary>
internal static class ActiveDismissBerthVectors
{
    public static void Run(Harness t)
    {
        var bytes = new byte[PresenceSerializer.MaxSize];
        t.Case("active dismissal: exact actor and counted public berth have byte-exact grammar");
        var staged = new PresenceState
        {
            HasActiveDismissBerth = true, ActiveDismissActorId = 0x04030201,
            ActiveDismissListCount = 4, ActiveDismissSeat = 2,
        };
        int size = PresenceSerializer.Write(staged, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 76 07 01 01 02 03 04 04 02"), bytes, size,
            "118/7 contains flags, exact stable actorI32LE, public active count and staged seat");
        t.True(PresenceSerializer.TryRead(bytes, size, out PresenceState read)
            && read.HasActiveDismissBerth && !read.ActiveDismissGhostShown
            && read.ActiveDismissActorId == 0x04030201
            && read.ActiveDismissListCount == 4 && read.ActiveDismissSeat == 2,
            "stage is bound to its original actor independently of focus/hand fan delivery");

        staged.ActiveDismissSeat = byte.MaxValue;
        staged.ActiveDismissGhostShown = true;
        size = PresenceSerializer.Write(staged, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 76 07 03 01 02 03 04 04 FF"), bytes, size,
            "near preview carries the same actor; FF explicitly means no staged card");
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && read.HasActiveDismissBerth
            && read.ActiveDismissGhostShown && read.ActiveDismissActorId == 0x04030201
            && read.ActiveDismissSeat == byte.MaxValue,
            "near preview does not fabricate a staged member");

        staged.ActiveDismissGhostShown = false;
        staged.ActiveDismissListCount = byte.MaxValue;
        staged.ActiveDismissSeat = 254;
        size = PresenceSerializer.Write(staged, bytes);
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && read.HasActiveDismissBerth
            && read.ActiveDismissListCount == 255 && read.ActiveDismissSeat == 254,
            "full counted seat range remains bounded and 255 remains the empty sentinel");
        foreach (int actor in new[] { int.MinValue, int.MaxValue, -1 })
        {
            staged.ActiveDismissActorId = actor;
            size = PresenceSerializer.Write(staged, bytes);
            t.True(PresenceSerializer.TryRead(bytes, size, out read) && read.HasActiveDismissBerth
                && read.ActiveDismissActorId == actor, "signed stable actor hash is lossless: " + actor);
        }
        staged.ActiveDismissActorId = 0x04030201;
        size = PresenceSerializer.Write(staged, bytes);

        t.Case("active dismissal: malformed or unsupported provenance cannot select a remote card");
        byte[] valid = (byte[])bytes.Clone();
        foreach (byte flags in new byte[] { 0, 2, 4, 5, 128, 255 })
        {
            Array.Copy(valid, bytes, valid.Length);
            bytes[size - 7] = flags;
            t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
                "unknown mode/flags fail closed: " + flags);
        }
        Array.Copy(valid, bytes, valid.Length);
        Array.Clear(bytes, size - 6, 4);
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
            "zero actor cannot be inferred from current focus or an equal-sized list");
        Array.Copy(valid, bytes, valid.Length);
        bytes[size - 2] = 0;
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
            "zero public list never resolves a card");
        bytes[size - 2] = 4;
        bytes[size - 1] = 4;
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
            "out-of-range stage cannot move a different active card");
        bytes[size - 1] = 2;
        bytes[size - 7] = 3;
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
            "preview plus occupied stage is contradictory and discarded");
        Array.Copy(valid, bytes, valid.Length);
        bytes[size - 8] = 3;
        t.True(PresenceSerializer.TryRead(bytes, size - 4, out read) && !read.HasActiveDismissBerth,
            "short former three-byte payload is skipped, never interpreted without actor provenance");
        Array.Copy(valid, bytes, valid.Length);
        bytes[size - 9] = 119;
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
            "unknown additive record is ignored safely");

        t.Case("active dismissal: default/invalid writer retains previous packets");
        var idle = new byte[PresenceSerializer.MaxSize];
        int idleSize = PresenceSerializer.Write(default, idle);
        foreach (PresenceState invalid in new[] {
            new PresenceState { HasActiveDismissBerth = false, ActiveDismissActorId = 1, ActiveDismissListCount = 4, ActiveDismissSeat = 1 },
            new PresenceState { HasActiveDismissBerth = true, ActiveDismissActorId = 0, ActiveDismissListCount = 4, ActiveDismissSeat = 1 },
            new PresenceState { HasActiveDismissBerth = true, ActiveDismissActorId = 1, ActiveDismissListCount = 0, ActiveDismissSeat = 255 },
            new PresenceState { HasActiveDismissBerth = true, ActiveDismissActorId = 1, ActiveDismissListCount = 4, ActiveDismissSeat = 4 },
            new PresenceState { HasActiveDismissBerth = true, ActiveDismissActorId = 1, ActiveDismissListCount = 4, ActiveDismissSeat = 1, ActiveDismissGhostShown = true },
        })
        {
            size = PresenceSerializer.Write(invalid, bytes);
            t.Wire(new ArraySegment<byte>(idle, 0, idleSize).ToArray(), bytes, size,
                "invalid/absent state opens no extension tail");
            t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth
                && read.ActiveDismissActorId == 0, "absence clears remote mode, actor and stage");
        }

        t.Case("active dismissal: original item placement keeps record26 and grammar");
        staged.HasItemUseClip = true;
        staged.ItemUseClipIndex = 7;
        size = PresenceSerializer.Write(staged, bytes);
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && read.HasItemUseClip
            && read.ItemUseClipIndex == 7 && read.HasActiveDismissBerth && read.ActiveDismissSeat == 254
            && read.ActiveDismissActorId == 0x04030201,
            "additive stage cannot reinterpret or replace an item-fan seat");
        t.True(bytes[size - 12] == 26 && bytes[size - 11] == 1 && bytes[size - 10] == 7
            && bytes[size - 9] == 118 && bytes[size - 8] == 7,
            "record26 remains its original one-byte payload beside exact-actor record118");
    }
}
