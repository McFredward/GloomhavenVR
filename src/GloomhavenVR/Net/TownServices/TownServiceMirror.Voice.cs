using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

/// <summary>A private, presentation-only visitor request. The face author publishes the
/// resulting cue through TLV80; this packet cannot execute a native transaction.</summary>
internal static class TownServiceVoiceRelayCodec
{
    internal const string Address = TownServiceFrame.VoiceAddress;
    private const uint Signature = 0x564F0000;

    internal static TownServiceFrame Create(byte service, uint session, uint sequence, TownVoiceReaction reaction,
        float sampleTime, float sessionAge) => new()
    {
        Service = service, Session = session, Sequence = sequence,
        Module = TownServiceFrame.VoiceModule, Template = 1, TemplateAddress = Address,
        Structure = Signature | (byte)reaction, Visible = true, SampleTime = sampleTime,
        SessionAge = sessionAge, ParentModule = TownServiceFrame.ManifestModule,
        Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f },
        Nodes = new[] { new TownServiceNode { Binding = 1 } }
    };

    internal static bool TryRead(TownServiceFrame frame, out TownVoiceReaction reaction)
    {
        reaction = default;
        if (frame.Module != TownServiceFrame.VoiceModule || frame.PublicCatalog || frame.PublicClaim != 0
            || frame.Session == 0 || frame.Sequence == 0 || frame.Sequence > uint.MaxValue
            || frame.BaseSequence != 0 || frame.Template != 1 || frame.TemplateAddress != Address
            || (frame.Structure & 0xFFFFFF00) != Signature || !frame.Visible
            || frame.ParentModule != TownServiceFrame.ManifestModule || frame.ParentBinding != 0
            || frame.ParentAlpha != 1f || frame.Modules.Length != 0 || frame.Nodes.Length != 1
            || frame.Nodes[0].Binding != 1 || frame.Nodes[0].Values.Count != 0
            || frame.Rack != null || frame.RackMember != null || frame.WorkspaceCloth != null
            || frame.HasCanvasFrame || frame.Pose.Length != 10 || frame.Pose[0] != 0f
            || frame.Pose[1] != 0f || frame.Pose[2] != 0f || frame.Pose[3] != 0f
            || frame.Pose[4] != 0f || frame.Pose[5] != 0f || frame.Pose[6] != 1f
            || frame.Pose[7] != 1f || frame.Pose[8] != 1f || frame.Pose[9] != 1f)
            return false;
        reaction = (TownVoiceReaction)(byte)frame.Structure;
        return frame.Service switch
        {
            1 => reaction == TownVoiceReaction.MerchantOffer || reaction == TownVoiceReaction.MerchantBuy
                 || reaction == TownVoiceReaction.MerchantSell,
            2 => reaction == TownVoiceReaction.PriestessDonate,
            3 => reaction == TownVoiceReaction.EnchantressEnhance,
            _ => false
        };
    }
}

internal static partial class TownServiceMirror
{
    private readonly struct PendingVoice
    {
        internal readonly TownServiceFrame Frame;
        internal readonly float Received;
        internal PendingVoice(TownServiceFrame frame, float received) { Frame = frame; Received = received; }
    }
    private static readonly Queue<TownServiceFrame> VoiceOutgoing = new();
    private static readonly Dictionary<int, List<PendingVoice>> VoicePending = new();
    private static uint _voiceOrdinal;

    private static void QueueVoiceReaction(byte service, TownVoiceReaction reaction)
    {
        using var lane = new LaneScope(PrivateLane);
        if (!_active || _session == 0 || _service != service || VoiceOutgoing.Count >= 16
            || _voiceOrdinal == uint.MaxValue) return;
        // The same schema is checked before spending transport budget and on receive.
        TownServiceFrame frame = TownServiceVoiceRelayCodec.Create(service, _session, ++_voiceOrdinal,
            reaction, Time.unscaledTime, Mathf.Max(0f, Time.unscaledTime - _sessionStarted));
        if (TownServiceVoiceRelayCodec.TryRead(frame, out _)) VoiceOutgoing.Enqueue(frame);
    }

    private static void CaptureVoice(Action<byte[], int, object?> send)
    {
        // A single request per capture keeps these rare cues behind ordinary native presentation.
        if (VoiceOutgoing.Count == 0) return;
        TownServiceFrame frame = VoiceOutgoing.Dequeue();
        if (!_active || frame.Session != _session || frame.Service != _service
            || Time.unscaledTime - frame.SampleTime > 3f) return;
        byte[] packet = TownServiceCodec.Write(frame);
        send(packet, packet.Length, frame);
    }

    private static void ReceiveVoice(int peer, TownServiceFrame frame)
    {
        if (!TownServiceVoiceRelayCodec.TryRead(frame, out _)) return;
        if (VisitorSessions.TryGetValue(peer, out TownServiceSessionInfo? session)
            && session.Session == frame.Session && session.Service == frame.Service)
        { DeliverVoice(peer, frame, session, Time.unscaledTime); return; }
        if (!VoicePending.TryGetValue(peer, out List<PendingVoice>? pending))
        {
            if (VoicePending.Count >= 8) return;
            pending = new List<PendingVoice>(8); VoicePending.Add(peer, pending);
        }
        if (pending.Count >= 8) pending.RemoveAt(0);
        pending.Add(new PendingVoice(frame, Time.unscaledTime));
    }

    private static void FlushVoicePending(int peer)
    {
        if (!VoicePending.TryGetValue(peer, out List<PendingVoice>? pending)
            || !VisitorSessions.TryGetValue(peer, out TownServiceSessionInfo? session)) return;
        VoicePending.Remove(peer);
        pending.Sort((a, b) => a.Frame.Sequence.CompareTo(b.Frame.Sequence));
        foreach (PendingVoice queued in pending)
            if (Time.unscaledTime - queued.Received <= 3f)
                DeliverVoice(peer, queued.Frame, session, queued.Received);
    }

    private static void DeliverVoice(int peer, TownServiceFrame frame, TownServiceSessionInfo session, float received)
    {
        if (!session.Active || session.Service != frame.Service || session.Session != frame.Session
            || !TownServiceVoiceRelayCodec.TryRead(frame, out TownVoiceReaction reaction)
            || Time.unscaledTime - received > 3f
            || Mathf.Abs((frame.SampleTime - session.SampleTime)
                - (frame.SessionAge - session.SessionAge)) > .5f) return;
        float age = Mathf.Max(0f, Time.unscaledTime - session.ReceivedTime + session.SampleTime - frame.SampleTime);
        TownServiceVoice.AcceptRelayedReaction(frame.Service, reaction, peer, frame.Session,
            (uint)frame.Sequence, age);
    }

    private static void ClearVoiceOutgoing() { VoiceOutgoing.Clear(); _voiceOrdinal = 0; }
    private static void ClearVoicePeer(int peer) => VoicePending.Remove(peer);
    // Keep the local ordinal across a transport reconnect in the same visitor session;
    // the elected author may still remember the last accepted request.
    private static void ClearVoiceNetwork() { VoiceOutgoing.Clear(); VoicePending.Clear(); }
}
