using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>A declaration about one exact peer's opening, never a reward confirmation.</summary>
internal struct RewardPoseDecline
{
    internal int Requester;
    internal uint Key, Generation;
}

/// <summary>Record74: request key4/generation4/count1, followed by up to four
/// requester4/key4/generation4 declines. Record73 retains its original presentation meaning.</summary>
internal struct RewardPoseHandshakeState
{
    internal uint Key, Generation;
    internal byte Count;
    internal RewardPoseDecline First, Second, Third, Fourth;
    internal RewardPoseDecline At(int index) => index switch { 0 => First, 1 => Second, 2 => Third, _ => Fourth };
    internal void Set(int index, RewardPoseDecline value)
    {
        switch (index) { case 0: First = value; break; case 1: Second = value; break;
            case 2: Third = value; break; default: Fourth = value; break; }
    }
}

internal static class RewardPoseHandshakeCodec
{
    internal const int MaxPeers = 4, HeaderSize = 9, MaxPayload = HeaderSize + MaxPeers * 12;
    internal static bool Valid(in RewardPoseHandshakeState state)
    {
        if ((state.Key == 0) != (state.Generation == 0) || state.Count > MaxPeers) return false;
        for (int i = 0; i < state.Count; i++)
        {
            RewardPoseDecline entry = state.At(i);
            if (entry.Requester <= 0 || entry.Key == 0 || entry.Generation == 0) return false;
            for (int j = 0; j < i; j++) if (state.At(j).Requester == entry.Requester) return false;
        }
        return true;
    }
    internal static bool Write(byte[] bytes, ref int offset, in RewardPoseHandshakeState state)
    {
        int length = HeaderSize + state.Count * 12;
        if (!Valid(in state) || offset < 0 || offset > bytes.Length - length - 2) return false;
        bytes[offset++] = NetProtocol.ExtIdRewardPoseHandshake; bytes[offset++] = (byte)length;
        AvatarSerializer.WriteU32(bytes, ref offset, state.Key);
        AvatarSerializer.WriteU32(bytes, ref offset, state.Generation);
        bytes[offset++] = state.Count;
        for (int i = 0; i < state.Count; i++)
        {
            RewardPoseDecline entry = state.At(i);
            AvatarSerializer.WriteU32(bytes, ref offset, (uint)entry.Requester);
            AvatarSerializer.WriteU32(bytes, ref offset, entry.Key);
            AvatarSerializer.WriteU32(bytes, ref offset, entry.Generation);
        }
        return true;
    }
    internal static bool TryRead(byte[] bytes, int offset, int length, out RewardPoseHandshakeState state)
    {
        state = default;
        if (length < HeaderSize || length > MaxPayload || offset < 0 || offset > bytes.Length - length) return false;
        var read = new RewardPoseHandshakeState { Key = AvatarSerializer.ReadU32(bytes, ref offset),
            Generation = AvatarSerializer.ReadU32(bytes, ref offset), Count = bytes[offset++] };
        if (length != HeaderSize + read.Count * 12 || read.Count > MaxPeers) return false;
        for (int i = 0; i < read.Count; i++)
            read.Set(i, new RewardPoseDecline { Requester = unchecked((int)AvatarSerializer.ReadU32(bytes, ref offset)),
                Key = AvatarSerializer.ReadU32(bytes, ref offset), Generation = AvatarSerializer.ReadU32(bytes, ref offset) });
        if (!Valid(in read)) return false;
        state = read;
        return true;
    }
}

/// <summary>
/// A healthy VR peer without this native window used to win first placement solely by avatar id,
/// leaving the actual controller's Continue invisible forever. A missing publication is not proof
/// of absence. Ask through the exact opening's key/generation and accept an explicit decline only.
/// Once a peer declines, a late native opening follows the established pose instead of originating
/// a competing one. No timeout reveals a local seat and no gameplay callback is invoked here.
/// </summary>
internal sealed class RewardPoseHandshake
{
    private readonly struct Peer
    {
        internal readonly RewardPoseHandshakeState State;
        internal readonly float At;
        internal Peer(in RewardPoseHandshakeState state, float at) { State = state; At = at; }
    }
    private readonly Dictionary<int, Peer> _peers = new();
    private readonly Dictionary<int, RewardPoseDecline> _declines = new();
    private readonly List<int> _remove = new();
    private readonly HashSet<uint> _declinedKeys = new();
    private readonly List<uint> _removeKeys = new();
    private uint _key, _generation;
    private int _localId;
    private bool _changed;
    internal bool Changed => _changed;

    internal void Reset()
    {
        _peers.Clear(); _declines.Clear(); _remove.Clear(); _declinedKeys.Clear(); _removeKeys.Clear();
        _key = _generation = 0; _localId = 0; _changed = false;
    }
    internal void SetLocalKey(uint key)
    {
        if (_key == key) return;
        _key = key;
        if (key != 0) { unchecked { _generation++; } if (_generation == 0) _generation = 1; }
        _changed = true;
    }
    internal void Observe(int sender, in RewardPoseHandshakeState state, float now)
    {
        if (sender <= 0 || !RewardPoseHandshakeCodec.Valid(in state)) return;
        _peers[sender] = new Peer(in state, now);
    }
    internal void Forget(int sender) => _peers.Remove(sender);

    internal void Refresh(int localId, List<int> livePeers, float now, float staleSeconds)
    {
        _localId = localId;
        _remove.Clear();
        foreach (var pair in _peers)
            if (!livePeers.Contains(pair.Key) || now - pair.Value.At > staleSeconds) _remove.Add(pair.Key);
        foreach (int peer in _remove) _peers.Remove(peer);

        // Keep the declined role while this native window or any live request names the
        // content. A delayed empty extras snapshot must not promote a late follower.
        // The actual-window fallback election handles departure of the last originator.
        _removeKeys.Clear();
        foreach (uint key in _declinedKeys) if (key != _key && !Requested(key)) _removeKeys.Add(key);
        foreach (uint key in _removeKeys) _declinedKeys.Remove(key);
        _remove.Clear();
        foreach (var pair in _declines)
            if (!_peers.TryGetValue(pair.Key, out Peer request) || request.State.Key != pair.Value.Key
                || request.State.Generation != pair.Value.Generation) _remove.Add(pair.Key);
        foreach (int peer in _remove) { _declines.Remove(peer); _changed = true; }
        foreach (var pair in _peers)
        {
            RewardPoseHandshakeState request = pair.Value.State;
            if (request.Key == 0 || (request.Key == _key && !_declinedKeys.Contains(request.Key))) continue;
            if (_declines.ContainsKey(pair.Key)) continue;
            if (_declines.Count >= RewardPoseHandshakeCodec.MaxPeers) break;
            _declines[pair.Key] = new RewardPoseDecline { Requester = pair.Key, Key = request.Key, Generation = request.Generation };
            _declinedKeys.Add(request.Key);
            _changed = true;
        }
    }
    private bool Requested(uint key)
    {
        foreach (var peer in _peers.Values) if (peer.State.Key == key) return true;
        return false;
    }
    internal bool LocalDeclined(uint key) => key != 0 && _declinedKeys.Contains(key);
    internal bool PeerDeclined(int peerId, uint key)
    {
        if (key == 0 || !_peers.TryGetValue(peerId, out Peer peer)) return false;
        for (int i = 0; i < peer.State.Count; i++)
        {
            RewardPoseDecline decline = peer.State.At(i);
            if (decline.Key != key) continue;
            if (decline.Requester == _localId)
            {
                if (_key == key && _generation == decline.Generation) return true;
            }
            else if (_peers.TryGetValue(decline.Requester, out Peer request)
                && request.State.Key == key && request.State.Generation == decline.Generation) return true;
        }
        return false;
    }
    internal RewardPoseHandshakeState Sample()
    {
        var state = new RewardPoseHandshakeState { Key = _key, Generation = _key != 0 ? _generation : 0 };
        foreach (RewardPoseDecline decline in _declines.Values) state.Set(state.Count++, decline);
        _changed = false;
        return state;
    }
}
