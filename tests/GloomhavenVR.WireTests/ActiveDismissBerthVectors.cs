using System;
using System.Linq;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

/// <summary>Byte-exact active-card staging presentation. No card identity or action command is
/// carried; the native game remains the only source of dismissal permission and commitment.</summary>
internal static class ActiveDismissBerthVectors
{
    public static void Run(Harness t)
    {
        var bytes = new byte[PresenceSerializer.MaxSize];
        t.Case("active dismissal: counted public berth has additive, byte-exact grammar");
        var staged = new PresenceState
        {
            HasActiveDismissBerth = true, ActiveDismissListCount = 4, ActiveDismissSeat = 2,
        };
        int size = PresenceSerializer.Write(staged, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 76 03 01 04 02"), bytes, size,
            "118/3 contains mode, public active-list count and staged seat without identity");
        t.True(PresenceSerializer.TryRead(bytes, size, out PresenceState read)
            && read.HasActiveDismissBerth && !read.ActiveDismissGhostShown
            && read.ActiveDismissListCount == 4 && read.ActiveDismissSeat == 2,
            "staged card stays represented independently of an open fan or a held hand");

        staged.ActiveDismissSeat = byte.MaxValue;
        staged.ActiveDismissGhostShown = true;
        size = PresenceSerializer.Write(staged, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 76 03 03 04 FF"), bytes, size,
            "near preview is an owner-authored bit; FF explicitly means no staged card");
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && read.HasActiveDismissBerth
            && read.ActiveDismissGhostShown && read.ActiveDismissSeat == byte.MaxValue,
            "near preview does not fabricate a staged member");

        staged.ActiveDismissGhostShown = false;
        staged.ActiveDismissListCount = byte.MaxValue;
        staged.ActiveDismissSeat = 254;
        size = PresenceSerializer.Write(staged, bytes);
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && read.HasActiveDismissBerth
            && read.ActiveDismissListCount == 255 && read.ActiveDismissSeat == 254,
            "full counted seat range remains bounded and 255 remains the empty sentinel");

        t.Case("active dismissal: malformed or unsupported state cannot select a remote card");
        byte[] valid = (byte[])bytes.Clone();
        foreach (byte flags in new byte[] { 0, 2, 4, 5, 128, 255 })
        {
            Array.Copy(valid, bytes, valid.Length);
            bytes[size - 3] = flags;
            t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
                "unknown mode/flags fail closed: " + flags);
        }
        Array.Copy(valid, bytes, valid.Length);
        bytes[size - 2] = 0;
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
            "zero public list never resolves a card");
        bytes[size - 2] = 4;
        bytes[size - 1] = 4;
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
            "out-of-range stage cannot move a different active card");
        bytes[size - 1] = 2;
        bytes[size - 3] = 3;
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
            "preview plus occupied stage is contradictory and discarded");
        Array.Copy(valid, bytes, valid.Length);
        bytes[size - 4] = 2;
        t.True(PresenceSerializer.TryRead(bytes, size - 1, out read) && !read.HasActiveDismissBerth,
            "short payload is skipped without reading the next record");
        Array.Copy(valid, bytes, valid.Length);
        bytes[size - 5] = 119;
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
            "unknown additive record is ignored safely");

        t.Case("active dismissal: default/invalid writer retains previous packets");
        var idle = new byte[PresenceSerializer.MaxSize];
        int idleSize = PresenceSerializer.Write(default, idle);
        foreach (PresenceState invalid in new[] {
            new PresenceState { HasActiveDismissBerth = false, ActiveDismissListCount = 4, ActiveDismissSeat = 1 },
            new PresenceState { HasActiveDismissBerth = true, ActiveDismissListCount = 0, ActiveDismissSeat = 255 },
            new PresenceState { HasActiveDismissBerth = true, ActiveDismissListCount = 4, ActiveDismissSeat = 4 },
            new PresenceState { HasActiveDismissBerth = true, ActiveDismissListCount = 4, ActiveDismissSeat = 1, ActiveDismissGhostShown = true },
        })
        {
            size = PresenceSerializer.Write(invalid, bytes);
            t.Wire(new ArraySegment<byte>(idle, 0, idleSize).ToArray(), bytes, size,
                "invalid/absent state opens no extension tail");
            t.True(PresenceSerializer.TryRead(bytes, size, out read) && !read.HasActiveDismissBerth,
                "absence clears the remote mode and stage");
        }

        t.Case("active dismissal: original item placement keeps record26 and grammar");
        staged.HasItemUseClip = true;
        staged.ItemUseClipIndex = 7;
        size = PresenceSerializer.Write(staged, bytes);
        t.True(PresenceSerializer.TryRead(bytes, size, out read) && read.HasItemUseClip
            && read.ItemUseClipIndex == 7 && read.HasActiveDismissBerth && read.ActiveDismissSeat == 254,
            "additive stage cannot reinterpret or replace an item-fan seat");
        t.True(bytes[size - 8] == 26 && bytes[size - 7] == 1 && bytes[size - 6] == 7
            && bytes[size - 5] == 118 && bytes[size - 4] == 3,
            "record26 remains its original one-byte payload beside new record118");
    }
}
