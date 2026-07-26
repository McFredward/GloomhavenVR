using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Per-frame owner of the VR embodiment sync: samples + broadcasts the local rig at
/// <see cref="NetProtocol.SendRateHz"/>, receives peers' packets, and maintains one
/// <see cref="RemoteAvatar"/> per remote VR player (lazy-created on first packet — RepoXR
/// pattern — and torn down on staleness). Everything is guarded so single-player, offline,
/// netcode-absent and non-modded-peer scenarios are strict no-ops.
///
/// THREADING: Bolt dispatches events on the Unity main thread, so
/// <see cref="OnPacketReceived"/> (invoked from the Harmony prefix inside
/// <c>ProcessSideAction</c>) runs on the main thread. To avoid creating GameObjects while
/// Bolt is mid-event-iteration, received states are parked in <see cref="_pending"/> and
/// applied in <see cref="Update"/>.
/// </summary>
internal sealed class NetAvatarDriver : MonoBehaviour
{
    // Fingers are cheap (10 B) and improve presence; on by default. No shared-config edit.
    private const bool IncludeFingers = true;

    private INetTransport _transport = new NullNetTransport();
    private IBoardAnchor _anchor = WorldAnchor.Instance;

    // One buffer serves both packet types; sized to the larger of the two so either fits.
    private readonly byte[] _sendBuffer = new byte[Mathf.Max(AvatarSerializer.MaxSize, PresenceSerializer.MaxSize)];
    private float _sendAccumulator;
    private float _extrasAccumulator;

    // Sticky last card-FX event (report 6): re-sent on every extras packet for redundancy on the
    // unreliable side-channel. The receiver de-dupes on the sequence byte, so repeats cost 2 bytes
    // and never play twice.
    private byte _lastFxEndpoints;
    private byte _lastFxSeq;
    private bool _hasFx;

    // Last broadcast fan sizes — an on-change extras send keeps a peer's fan appearing/disappearing
    // with the gesture instead of up to one 5 Hz interval later (see TickExtrasSend).
    private int _lastSentHandCount = -1;
    private int _lastSentItemCount = -1;

    private readonly Dictionary<int, RemoteAvatar> _avatars = new();
    // Latest world-frame state per sender, awaiting apply on the next Update (dedup: only the
    // newest matters for an unreliable stream).
    private readonly Dictionary<int, AvatarState> _pending = new();
    // Latest world-frame EXTRAS (board + hand count) per sender, same dedup contract.
    private readonly Dictionary<int, PresenceState> _pendingExtras = new();
    private readonly List<int> _scratchIds = new();

    /// <summary>Wire the driver to its transport + anchor. Call once before enabling.</summary>
    public void Configure(INetTransport transport, IBoardAnchor anchor)
    {
        _transport = transport ?? new NullNetTransport();
        _anchor = anchor ?? WorldAnchor.Instance;
    }

    private void OnEnable()
    {
        _transport.PacketReceived += OnPacketReceived;
    }

    private void OnDisable()
    {
        _transport.PacketReceived -= OnPacketReceived;
        _pending.Clear();
        _pendingExtras.Clear();
        NetCardFx.Reset(); // never carry a queued card animation into the next session
        _hasFx = false;
        DestroyAllAvatars();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        ApplyPending();
        TickAvatars(dt);
        TickSend(dt);
        TickExtrasSend(dt);
        NetFigures.Tick();
    }

    // ---- send ---------------------------------------------------------------------------

    private void TickSend(float dt)
    {
        // Only VR players broadcast; flat/modded peers stay silent (and thus invisible to
        // others), exactly as intended. LocalPlayerId > 0 also gates out the join window
        // where the session is "online" but our NetworkPlayer (and thus MyPlayer, which
        // SendSideAction dereferences) does not exist yet.
        if (!VRSession.IsRunning || !_transport.IsOnline || _transport.LocalPlayerId <= 0)
        {
            _sendAccumulator = 0f;
            return;
        }

        _sendAccumulator += dt;
        float interval = 1f / NetProtocol.SendRateHz;
        if (_sendAccumulator < interval)
            return;
        // Drop excess accumulation (don't burst after a hitch).
        _sendAccumulator = 0f;

        if (!LocalRigSampler.TrySample(_anchor, IncludeFingers, out AvatarState state))
            return;

        int len = AvatarSerializer.Write(in state, _sendBuffer);
        _transport.Send(_sendBuffer, len);
    }

    // ---- extras send (board pose + hand count, slower) ----------------------------------

    private void TickExtrasSend(float dt)
    {
        if (!VRSession.IsRunning || !_transport.IsOnline || _transport.LocalPlayerId <= 0)
        {
            _extrasAccumulator = 0f;
            return;
        }

        _extrasAccumulator += dt;
        float interval = 1f / NetProtocol.ExtrasSendRateHz;
        // CARD-FX LATENCY (report 6): a card flight lasts 0.4 s, so an event that waits for the
        // next 5 Hz slot could arrive after the real animation already finished. A queued event
        // therefore PRE-EMPTS the rate gate and goes out on the frame it happened; the extras
        // packet is ~10 bytes, and events are human-paced (a handful per turn), so this cannot
        // become a stream. Everything else still rides the normal 5 Hz cadence.
        bool fxPending = NetCardFx.Pending;
        // FAN VISIBILITY (report 6, "Fächer ist sichtbar"): raising/lowering the hand or item fan,
        // and every card that enters or leaves it, is a COUNT change — and a count that waits up to
        // 200 ms makes the peer's fan appear noticeably after the hand gesture that raised it. Send
        // on change too, same argument (and same tiny cost) as the card-FX pre-emption below.
        int handNow = CardFan.Current?.Count ?? 0;
        ItemsPile? itemsNow = ItemsPile.Current;
        int itemsCount = itemsNow != null && itemsNow.IsOpen ? itemsNow.Chips.Count : 0;
        bool countsChanged = handNow != _lastSentHandCount || itemsCount != _lastSentItemCount;
        if (_extrasAccumulator < interval && !fxPending && !countsChanged)
            return;
        _extrasAccumulator = 0f;
        _lastSentHandCount = handNow;
        _lastSentItemCount = itemsCount;

        var extras = default(PresenceState);

        Transform? board = PlayTray.Current?.Root;
        if (board != null)
        {
            _anchor.ToAnchor(board.position, board.rotation, out Vector3 bp, out Quaternion br);
            extras.HasBoard = true;
            extras.Board.Position = bp;
            extras.Board.Rotation = br;
            float scale = board.lossyScale.x;
            extras.BoardScale = scale > 0f ? scale : 1f;
        }

        extras.HandCardCount = (byte)Mathf.Clamp(handNow, 0, 255);
        extras.DominantRight = LocalRigSampler.LocalDominantRight();

        // ITEM fan (report 5): the equipped-item fan is a completely separate object from the
        // ability fan (Cards.ItemsPile, not Cards.CardFan) and used to be broadcast NOWHERE, so a
        // peer raising their items saw nothing on anyone else's screen. Count + held/board-anchored
        // flag only — backs on the receiver, no item identity.
        if (itemsCount > 0)
        {
            extras.HasItemFan = true;
            extras.ItemCardCount = (byte)Mathf.Clamp(itemsCount, 0, 255);
            extras.ItemFanHeld = itemsNow != null && itemsNow.IsHandHeld;
            extras.ItemFanLeftHand = itemsNow != null && itemsNow.IsHeldByLeftHand;
        }

        // CARD-FX event (report 6): pop at most one queued animation per packet and stamp it with a
        // fresh sequence. When nothing is queued the LAST event is re-sent unchanged — deliberate
        // redundancy on an unreliable stream; the receiver only plays on a sequence CHANGE, so a
        // repeat is free and a single lost packet still lands within 200 ms.
        if (NetCardFx.TryDequeue(out byte fxEndpoints, out byte fxSeq))
        {
            _lastFxEndpoints = fxEndpoints;
            _lastFxSeq = fxSeq;
            _hasFx = true;
        }
        if (_hasFx)
        {
            extras.HasCardFx = true;
            extras.FxSeq = _lastFxSeq;
            extras.FxEndpoints = _lastFxEndpoints;
        }

        int len = PresenceSerializer.Write(in extras, _sendBuffer);
        _transport.Send(_sendBuffer, len);
    }

    // ---- receive ------------------------------------------------------------------------

    private void OnPacketReceived(int senderId, byte[] buffer, int length)
    {
        // Ignore our own echo and unparseable/foreign packets.
        if (senderId != 0 && senderId == _transport.LocalPlayerId)
            return;

        // Route by message type without fully parsing (also rejects magic/version mismatches).
        int type = NetPacket.PeekType(buffer, length);
        switch (type)
        {
            case NetProtocol.MsgRig:
                if (AvatarSerializer.TryRead(buffer, length, out AvatarState state))
                {
                    // Convert the shared-frame poses to world here so RemoteAvatar stays world-only.
                    ToWorld(ref state);
                    _pending[senderId] = state; // dedup: keep only the newest
                }
                break;

            case NetProtocol.MsgExtras:
                if (PresenceSerializer.TryRead(buffer, length, out PresenceState extras))
                {
                    ExtrasToWorld(ref extras);
                    _pendingExtras[senderId] = extras; // dedup: keep only the newest
                }
                break;
        }
    }

    private void ApplyPending()
    {
        if (_pending.Count > 0)
        {
            foreach (KeyValuePair<int, AvatarState> kv in _pending)
            {
                RemoteAvatar avatar = GetOrCreate(kv.Key);
                AvatarState s = kv.Value;
                avatar.SetTarget(in s);

                // Cosmetic figure sync: mirror the sender's held figure (no-op stub in foundation).
                if (s.HasHeldFigure)
                    NetFigures.ApplyRemoteHeld(kv.Key, s.HeldFigureActorId, s.HeldFigurePose.Position, s.HeldFigurePose.Rotation);
                else
                    NetFigures.ReleaseRemote(kv.Key);
            }
            _pending.Clear();
        }

        if (_pendingExtras.Count > 0)
        {
            foreach (KeyValuePair<int, PresenceState> kv in _pendingExtras)
            {
                RemoteAvatar avatar = GetOrCreate(kv.Key);
                PresenceState p = kv.Value;
                avatar.SetExtras(in p);
            }
            _pendingExtras.Clear();
        }
    }

    private RemoteAvatar GetOrCreate(int playerId)
    {
        if (_avatars.TryGetValue(playerId, out RemoteAvatar existing))
            return existing;
        var avatar = new RemoteAvatar(playerId);
        _avatars[playerId] = avatar;
        return avatar;
    }

    // ---- avatar lifetime ----------------------------------------------------------------

    private void TickAvatars(float dt)
    {
        if (_avatars.Count == 0)
            return;

        _scratchIds.Clear();
        foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
        {
            RemoteAvatar avatar = kv.Value;
            avatar.Tick(dt);
            // Teardown on staleness (covers Bolt player-left, a peer switching to flat, or a
            // long network stall — more robust than a single player-left callback).
            if (avatar.TimeSinceUpdate > NetProtocol.StaleTimeoutSeconds)
                _scratchIds.Add(kv.Key);
        }

        for (int i = 0; i < _scratchIds.Count; i++)
        {
            int id = _scratchIds[i];
            if (_avatars.TryGetValue(id, out RemoteAvatar avatar))
            {
                avatar.Destroy();
                _avatars.Remove(id);
                NetFigures.ReleaseRemote(id); // drop any figure this peer was holding
            }
        }
    }

    /// <summary>Immediate teardown entry point for a future <c>PlayerRegistry.OnPlayerLeft</c>
    /// hook (staleness already handles it after <see cref="NetProtocol.StaleTimeoutSeconds"/>).</summary>
    public void RemovePlayer(int playerId)
    {
        _pending.Remove(playerId);
        _pendingExtras.Remove(playerId);
        if (_avatars.TryGetValue(playerId, out RemoteAvatar avatar))
        {
            avatar.Destroy();
            _avatars.Remove(playerId);
            NetFigures.ReleaseRemote(playerId);
        }
    }

    private void DestroyAllAvatars()
    {
        foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
        {
            kv.Value.Destroy();
            NetFigures.ReleaseRemote(kv.Key);
        }
        _avatars.Clear();
    }

    private void OnDestroy() => DestroyAllAvatars();

    // ---- frame conversion ---------------------------------------------------------------

    private void ToWorld(ref AvatarState state)
    {
        if (state.HeadValid)
        {
            _anchor.ToWorld(state.Head.Position, state.Head.Rotation, out Vector3 p, out Quaternion r);
            state.Head.Position = p;
            state.Head.Rotation = r;
        }
        if (state.Left.Tracked)
        {
            _anchor.ToWorld(state.Left.Pose.Position, state.Left.Pose.Rotation, out Vector3 p, out Quaternion r);
            state.Left.Pose.Position = p;
            state.Left.Pose.Rotation = r;
        }
        if (state.Right.Tracked)
        {
            _anchor.ToWorld(state.Right.Pose.Position, state.Right.Pose.Rotation, out Vector3 p, out Quaternion r);
            state.Right.Pose.Position = p;
            state.Right.Pose.Rotation = r;
        }
        if (state.HasHeldFigure)
        {
            _anchor.ToWorld(state.HeldFigurePose.Position, state.HeldFigurePose.Rotation, out Vector3 p, out Quaternion r);
            state.HeldFigurePose.Position = p;
            state.HeldFigurePose.Rotation = r;
        }
        if (state.HasHeldCard)
        {
            _anchor.ToWorld(state.HeldCardPose.Position, state.HeldCardPose.Rotation, out Vector3 p, out Quaternion r);
            state.HeldCardPose.Position = p;
            state.HeldCardPose.Rotation = r;
        }
    }

    private void ExtrasToWorld(ref PresenceState p)
    {
        if (p.HasBoard)
        {
            _anchor.ToWorld(p.Board.Position, p.Board.Rotation, out Vector3 wp, out Quaternion wr);
            p.Board.Position = wp;
            p.Board.Rotation = wr;
        }
    }
}
