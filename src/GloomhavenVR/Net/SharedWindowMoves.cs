using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Actual pose streams keep their existing coordinate/content grammar. This companion
/// gives all six kinds one opening-scoped claim order, independent of packet arrival clocks.</summary>
internal static class SharedWindowMoves
{
    private static Func<bool>? _transport;
    private static int _local;
    private static readonly SharedWindowMoveRegister?[] Registers = new SharedWindowMoveRegister?[7];
    private sealed class Incoming
    {
        internal readonly SharedWindowMoveEntry?[] Rows = new SharedWindowMoveEntry?[7];
        internal readonly float[] At = new float[7];
        internal readonly bool[] Accepted = new bool[7];
    }
    private static readonly Dictionary<int, Incoming> Peers = new();
    private static bool Enabled => _transport?.Invoke() == true && !NetSession.FlatNetMode
        && global::FFSNetwork.IsOnline && NetPlayerActors.LocalPlayerId() > 0;
    internal static bool Active => Enabled;

    internal static void Bind(Func<bool> transport)
    {
        _transport = transport;
        SharedWindowInteractionBridge.CanBegin = CanBegin;
        SharedWindowInteractionBridge.MayWrite = MayWrite;
        SharedWindowInteractionBridge.Began = Begin;
        SharedWindowInteractionBridge.Released = Release;
        SharedWindowInteractionBridge.Canceled = Abandon;
    }

    internal static void Reset()
    {
        Array.Clear(Registers, 0, Registers.Length); Peers.Clear(); _local = 0;
    }

    private static SharedWindowMoveRegister Register(SharedWindowKind kind)
    {
        int local = NetPlayerActors.LocalPlayerId();
        if (_local != local) { Reset(); _local = local; }
        return Registers[(int)kind] ??= new SharedWindowMoveRegister(local, (byte)kind);
    }

    internal static int OpeningAuthor(SharedWindowKind kind, uint key)
        => kind == SharedWindowKind.RewardShowcase
            ? RemoteMapStory.MoveOpeningAuthor(key) : SharedWindowSpawnAuthority.AuthorId;

    internal static void Open(SharedWindowKind kind, uint key)
    {
        if (!Enabled || kind == SharedWindowKind.None || key == 0) return;
        SharedWindowMoveRegister register = Register(kind);
        register.Open(key, OpeningAuthor(kind, key));
        // A first native view may appear after its first valid network snapshot. Replay only
        // this exact kind/key, with the placement author's opening announcement first.
        int author = OpeningAuthor(kind, key);
        if (Peers.TryGetValue(author, out Incoming? publisher) && publisher != null && publisher.Rows[(int)kind].HasValue)
        {
            SharedWindowMoveEntry row = publisher.Rows[(int)kind]!.Value;
            register.Observe(author, in row, author, publisher.At[(int)kind]);
        }
        foreach (KeyValuePair<int, Incoming> peer in Peers)
        {
            if (peer.Key == author || !peer.Value.Rows[(int)kind].HasValue) continue;
            SharedWindowMoveEntry row = peer.Value.Rows[(int)kind]!.Value;
            register.Observe(peer.Key, in row, author, peer.Value.At[(int)kind]);
        }
    }

    internal static void Close(SharedWindowKind kind)
    {
        if (!Enabled || kind == SharedWindowKind.None || !SharedWindows.ParticipatesHere(kind)) return;
        Registers[(int)kind]?.Close();
    }

    private static bool CanBegin(SharedWindowKind kind) => !Enabled || Register(kind).CanBegin(Time.unscaledTime);
    internal static bool MayWrite(SharedWindowKind kind) => !Enabled || Register(kind).LocalWins;
    internal static bool HasLocalClaim(SharedWindowKind kind) => Enabled && Register(kind).HasLocalClaim;
    private static void Begin(SharedWindowKind kind)
    {
        if (Enabled) Register(kind).Begin(Time.unscaledTime);
    }
    private static void Release(SharedWindowKind kind)
    {
        if (Enabled) Register(kind).Release();
    }
    private static void Abandon(SharedWindowKind kind)
    {
        if (Enabled) Register(kind).Abandon();
    }

    internal static bool BeginAutomatic(SharedWindowKind kind)
    {
        return !Enabled || Register(kind).Begin(Time.unscaledTime, automatic: true);
    }

    internal static bool MayFollow(int sender, SharedWindowKind kind, uint key)
    {
        if (!Enabled) return true;
        SharedWindowMoveRegister register = Register(kind);
        return register.TryGet(sender, out SharedWindowMoveEntry row) && row.ContentKey == key
            && register.Accepts(sender, in row);
    }

    /// <summary>Legacy77 has no sample clock. In an ordered session its current motion bits
    /// come from the accepted opening/gesture, never from a delayed packet's byte stamps.</summary>
    internal static void CurrentMotion(int sender, SharedWindowKind kind, out bool held, out bool automatic)
    {
        held = automatic = false;
        if (!Enabled || !Peers.TryGetValue(sender, out Incoming? peer) || peer == null
            || Time.unscaledTime - peer.At[(int)kind] > SharedWindowMoveRegister.GripLeaseSeconds) return;
        SharedWindowMoveRegister register = Register(kind);
        if (!register.TryGet(sender, out SharedWindowMoveEntry row) || !register.Accepts(sender, in row)) return;
        held = row.Phase == SharedWindowMovePhase.Held;
        automatic = row.Phase == SharedWindowMovePhase.Automatic;
    }

    /// <summary>Exactly one call before the original window observers consume this same extras
    /// snapshot. Old/duplicate samples cannot overwrite a newer pose under a stable gesture.</summary>
    internal static void Observe(int sender, in PresenceState presence)
    {
        if (!Enabled || sender <= 0) return;
        if (!Peers.TryGetValue(sender, out Incoming? peer) || peer == null) Peers[sender] = peer = new Incoming();
        Array.Clear(peer.Accepted, 0, peer.Accepted.Length);
        SharedWindowMoveOrder? order = presence.WindowMoves;
        if (order == null || !order.Valid) return;
        foreach (SharedWindowMoveEntry row in order.Entries)
        {
            var kind = (SharedWindowKind)row.Kind;
            if (row.Phase == SharedWindowMovePhase.Automatic && sender != RemoteMapStory.SharedReflowAuthor()) continue;
            if (!HasMatchingPose(in presence, kind, row.ContentKey)) continue;
            SharedWindowMoveEntry? previous = peer.Rows[row.Kind];
            if (previous.HasValue && previous.Value.SameOpening(in row)
                && !SharedWindowMoveRegister.Newer(row.Sequence, previous.Value.Sequence)) continue;
            SharedWindowMoveRegister register = Register(kind);
            bool accepted = register.Observe(sender, in row, OpeningAuthor(kind, row.ContentKey), Time.unscaledTime);
            // Before local native discovery, keep the immutable metadata for Open's exact replay.
            if (register.ContentKey != 0 && !accepted) continue;
            peer.Rows[row.Kind] = row; peer.At[row.Kind] = Time.unscaledTime;
            peer.Accepted[row.Kind] = true;
        }
    }

    internal static bool Accepted(int sender, SharedWindowKind kind, uint key)
        => !Enabled || (Peers.TryGetValue(sender, out Incoming? peer) && peer != null && peer.Accepted[(int)kind]
            && peer.Rows[(int)kind].HasValue && peer.Rows[(int)kind]!.Value.ContentKey == key);

    internal static bool PoseStillCurrent(int sender, SharedWindowKind kind, uint key)
    {
        if (!Enabled) return true;
        SharedWindowMoveRegister register = Register(kind);
        if (!Peers.TryGetValue(sender, out Incoming? peer) || peer == null || !peer.Rows[(int)kind].HasValue
            || !register.TryGet(sender, out SharedWindowMoveEntry row) || row.ContentKey != key) return false;
        SharedWindowMoveEntry stored = peer.Rows[(int)kind]!.Value;
        return row.SameOpening(in stored);
    }

    internal static void Sample(ref PresenceState presence)
    {
        if (!Enabled) return;
        var rows = new List<SharedWindowMoveEntry>(6);
        for (int value = 1; value <= 6; value++)
        {
            var kind = (SharedWindowKind)value;
            uint key = PoseKey(in presence, kind);
            if (key == 0) continue;
            SharedWindowMoveRegister register = Register(kind);
            if (register.ContentKey != key) Open(kind, key);
            if (register.Sample(out SharedWindowMoveEntry row)) rows.Add(row);
        }
        presence.WindowMoves = new SharedWindowMoveOrder(rows.ToArray());
    }

    private static uint PoseKey(in PresenceState p, SharedWindowKind kind)
    {
        if (kind == SharedWindowKind.ScenarioStory)
            return p.HasStorySync && (p.StoryFlags & NetProtocol.StoryPoseBit) != 0 ? p.StoryKey : 0;
        if (kind == SharedWindowKind.Video)
            return p.HasVideoWindow && (p.VideoWindow.Window.Flags & NetProtocol.SharedPoseBit) != 0
                ? p.VideoWindow.Window.ContentKey : 0;
        if (kind == SharedWindowKind.RewardShowcase)
            return p.HasRewardWindow && (p.RewardWindow.Window.Flags & NetProtocol.SharedPoseBit) != 0
                ? p.RewardWindow.Window.ContentKey : 0;
        if (p.HasSharedWindow && p.SharedWindowEntries != null)
            for (int i = 0; i < p.SharedWindowCount && i < p.SharedWindowEntries.Length; i++)
            {
                SharedWindowEntry row = p.SharedWindowEntries[i];
                if (row.Kind + 1 == (int)kind && (row.Flags & NetProtocol.SharedPoseBit) != 0) return row.ContentKey;
            }
        return 0;
    }

    private static bool HasMatchingPose(in PresenceState p, SharedWindowKind kind, uint key)
        => key != 0 && PoseKey(in p, kind) == key && p.WindowPoseContext.HasValue
            && p.WindowPoseContext.Value.Valid && p.WindowScales.HasValue
            && p.WindowScales.Value.Reference((int)kind) >= 0.0001f
            && p.WindowScales.Value.Reference((int)kind) <= 1000000f;
}
