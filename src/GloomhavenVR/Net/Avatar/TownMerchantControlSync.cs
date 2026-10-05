using System;
using System.Collections.Generic;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

/// <summary>One reliable public input coordinator. Every visitor can press the cabinet,
/// even without a character; the host executes the same original Select/RequestTurn
/// presentation code once. A press no longer throws away the prepared native bank by
/// transferring catalogue authorship to its visitor. Transactions keep their own grants.</summary>
internal static class TownMerchantControlSync
{
    internal static Func<byte[], int, bool, bool>? SendReliable { get; set; }
    private static INetTransport? _transport;
    private static readonly Dictionary<int, uint> LastRequest = new();
    private static readonly Queue<TownMerchantControlMessage> Outgoing = new();
    private static TownMerchantControlMessage? _latest;
    private static uint _requestSequence, _stateSequence, _epoch = NewEpoch();
    private static float _nextState, _nextWarning, _receivedTime;
    private static TownRackState? _displayClock;
    internal static TownRackState? SharedClock
    {
        get
        {
            if (_displayClock != null && _latest is TownMerchantControlMessage latest)
                _displayClock.Elapsed = Mathf.Clamp(latest.Clock!.Elapsed + Mathf.Max(0f,
                    Time.unscaledTime - _receivedTime), 0f, TownRackState.TurnDuration);
            return _displayClock;
        }
    }
    private static bool Online => FFSNetwork.IsOnline && !NetSession.FlatNetMode;
    private static int LocalPlayer => _transport?.LocalPlayerId ?? NetPlayerActors.LocalPlayerId();
    private static bool Host => Online && LocalPlayer > 0 && LocalPlayer == PlayerRegistry.HostPlayerID;
    private static uint NewEpoch() { uint value = unchecked((uint)(DateTime.UtcNow.Ticks ^ Environment.TickCount)); return value == 0 ? 1u : value; }
    private static uint Next(ref uint value) { unchecked { if (++value == 0) ++value; } return value; }
    internal static bool RequestCategory(int category) => category >= 0 && category < 6
        && Request(TownMerchantControlOperation.Category, (sbyte)category);
    internal static bool RequestPage(int direction) => direction != 0
        && Request(TownMerchantControlOperation.Page, (sbyte)Math.Sign(direction));
    private static bool Request(TownMerchantControlOperation operation, sbyte value)
    {
        if (!Online) return TownServicePublicMerchant.ApplyOriginalControl(operation, value);
        if (_transport == null || !_transport.IsOnline || LocalPlayer <= 0 || !TownServiceGrantSync.CoordinatorReady) return false;
        if (Host) return Execute(operation, value, LocalPlayer, Next(ref _requestSequence));
        if (Outgoing.Count >= 16) return false;
        uint sequence = Next(ref _requestSequence);
        uint session = _latest?.Session ?? 0;
        Outgoing.Enqueue(new TownMerchantControlMessage(TownMerchantControlKind.Request, operation, value,
            session, sequence, LocalPlayer, sequence, 0));
        FlushRequests();
        return true;
    }
    internal static void Tick(INetTransport transport, float now)
    {
        _transport = transport;
        if (!Online || !_transport.IsOnline || LocalPlayer <= 0) return;
        FlushRequests();
        if (Host && now >= _nextState)
        {
            _nextState = now + 1f;
            Publish(0, 0);
        }
        if (!Host && _latest is TownMerchantControlMessage latest)
        {
            TownRackState clock = SharedClock!;
            TownServicePublicMerchant.ApplySharedControlClock(clock);
            TownServiceMirror.ApplyPublicControlClock(PlayerRegistry.HostPlayerID, latest.Session, clock);
        }
    }
    private static void FlushRequests()
    {
        if (Host || SendReliable == null) return;
        // ReliableOrdered owns retransmission. A failed local send retains the exact
        // request nonce; it must not replay an already-committed category/page action.
        for (int sent = 0; sent < 4 && Outgoing.Count > 0; sent++)
        {
            TownMerchantControlMessage request = Outgoing.Peek(); byte[] bytes = TownMerchantControlCodec.Write(in request);
            if (!SendReliable(bytes, bytes.Length, true)) { ReportSendFailure(); return; }
            Outgoing.Dequeue();
        }
    }
    private static bool Execute(TownMerchantControlOperation operation, sbyte value, int requester, uint request)
    {
        bool changed = TownServicePublicMerchant.ApplyOriginalControl(operation, value);
        Publish(requester, request); // A declined request still repairs this visitor's proxy.
        return changed;
    }
    private static void Publish(int requester, uint request)
    {
        TownRackState? clock = TownServicePublicMerchant.ControlClock;
        if (clock == null || SendReliable == null) return;
        var message = new TownMerchantControlMessage(TownMerchantControlKind.State, TownMerchantControlOperation.Snapshot,
            0, TownServicePublicMerchant.Session, Next(ref _stateSequence), requester, request, _epoch, clock);
        byte[] bytes = TownMerchantControlCodec.Write(in message);
        if (!SendReliable(bytes, bytes.Length, false)) ReportSendFailure();
    }
    internal static bool Receive(int sender, byte[] bytes, int length)
    {
        if (sender <= 0 || !TownMerchantControlCodec.TryRead(bytes, length, out TownMerchantControlMessage message)) return false;
        if (!Online || sender == LocalPlayer) return true;
        if (message.Kind == TownMerchantControlKind.Request)
        {
            if (!Host || message.Requester != sender || message.Session != 0
                && message.Session != TownServicePublicMerchant.Session) return true;
            if (LastRequest.TryGetValue(sender, out uint previous) && !TownMerchantControlCodec.Newer(message.Sequence, previous))
                return true;
            if (!LastRequest.ContainsKey(sender) && LastRequest.Count >= 8) return true;
            LastRequest[sender] = message.Sequence;
            Execute(message.Operation, message.Value, sender, message.Sequence);
            return true;
        }
        if (Host || sender != PlayerRegistry.HostPlayerID) return true;
        if (_latest is TownMerchantControlMessage previousState && message.Epoch == previousState.Epoch
            && !TownMerchantControlCodec.Newer(message.Sequence, previousState.Sequence)) return true;
        _latest = message; _receivedTime = Time.unscaledTime; _displayClock = message.Clock!.Copy();
        TownServicePublicMerchant.ApplySharedControlClock(message.Clock!);
        TownServiceMirror.ApplyPublicControlClock(sender, message.Session, message.Clock!);
        return true;
    }
    private static void ReportSendFailure()
    {
        if (Time.unscaledTime < _nextWarning) return;
        _nextWarning = Time.unscaledTime + 30f;
        VRLog.Warn("Net", "Merchant public control transport unavailable; retained the pending presentation request.");
    }
    internal static void ForgetPeer(int peer) { LastRequest.Remove(peer); if (peer == PlayerRegistry.HostPlayerID) { _latest = null; _displayClock = null; } }
    internal static void Reset()
    {
        LastRequest.Clear(); Outgoing.Clear(); _latest = null; _displayClock = null; _transport = null;
        _requestSequence = _stateSequence = 0; _epoch = NewEpoch(); _nextState = _nextWarning = 0;
    }
}
