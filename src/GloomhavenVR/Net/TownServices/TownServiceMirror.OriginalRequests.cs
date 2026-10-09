using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class OriginalRequest
    {
        internal TownServiceFrame Frame = null!;
        internal bool Enqueued;
        internal float Created;
    }
    private sealed class OriginalRequestOwner
    {
        internal byte Service;
        internal uint Session;
        internal readonly Dictionary<ushort, OriginalRequest> Modules = new();
    }
    private static readonly Dictionary<int, OriginalRequestOwner> OriginalRequests = new();
    private static readonly List<int> OriginalRequestOwners = new(24);
    private static readonly List<ushort> OriginalRequestsRemoved = new();
    private static readonly List<TownServiceOriginalReceiptEntry> OriginalRequestBatch = new(20);
    private static bool RequestedOriginalRepairPending;

    private static void RecordOriginalRequest(int peer, TownServiceFrame frame)
    {
        // A refused compact original is not a retained-original receipt. Request
        // only that exact private metadata identity; an unrelated cold catalog or
        // invisible visitor-stock preparation keeps its independent repair path.
        if (peer <= 0 || peer == LocalPeer || frame.PublicCatalog || frame.VisitorStock
            || frame.NativeTemplateBasisKey == 0 || frame.BaseSequence != 0
            || frame.Sequence == 0 || frame.Session == 0 || frame.Service is not (1 or 3)
            || frame.Module >= TownServiceFrame.VoiceModule) return;
        if (!OriginalRequests.TryGetValue(peer, out OriginalRequestOwner? owner))
        {
            if (OriginalRequests.Count >= 24) return;
            owner = new OriginalRequestOwner(); OriginalRequests.Add(peer, owner);
        }
        if (owner.Service != frame.Service || owner.Session != frame.Session)
        { owner.Modules.Clear(); owner.Service = frame.Service; owner.Session = frame.Session; }
        if (owner.Modules.TryGetValue(frame.Module, out OriginalRequest? previous)
            && previous.Frame.Sequence >= frame.Sequence) return;
        if (owner.Modules.Count >= TownServiceFrame.MaxModules && !owner.Modules.ContainsKey(frame.Module)) return;
        owner.Modules[frame.Module] = new OriginalRequest { Frame = frame, Created = UnityEngine.Time.unscaledTime };
    }

    private static bool OriginalRequestCurrent(int peer, TownServiceFrame frame)
    {
        if (ReceivedBaselines.TryGetValue(peer, out var originals)
            && originals.TryGetValue(frame.Module, out TownServiceFrame? retained)
            && retained.Service == frame.Service && retained.Session == frame.Session
            && retained.Sequence >= frame.Sequence && retained.NativeTemplateBasisKey == 0) return false;
        if (Sessions.TryGetValue(peer, out TownServiceSessionInfo? session)
            && session.Sequence >= frame.Sequence
            && (!session.Active || session.Service != frame.Service || session.Session != frame.Session
                || Array.BinarySearch(session.Modules, frame.Module) < 0)) return false;
        return true;
    }

    /// <summary>Publish each exact rejected-original request once through the
    /// existing reliable, bounded original metadata channel. A thrown queue
    /// admission preserves the request; a native close or full repair retires it.</summary>
    internal static void CaptureOriginalRequests(Action<byte[], int> send)
    {
        if (send == null || OriginalRequests.Count == 0 || !CollectReceiptPeers()) return;
        OriginalRequestOwners.Clear();
        foreach (int peer in OriginalRequests.Keys) OriginalRequestOwners.Add(peer);
        foreach (int peer in OriginalRequestOwners)
        {
            OriginalRequestOwner owner = OriginalRequests[peer];
            OriginalRequestBatch.Clear(); OriginalRequestsRemoved.Clear();
            foreach (var pair in owner.Modules)
            {
                if (UnityEngine.Time.unscaledTime - pair.Value.Created > NetProtocol.StaleTimeoutSeconds
                    || !OriginalRequestCurrent(peer, pair.Value.Frame))
                { OriginalRequestsRemoved.Add(pair.Key); continue; }
                if (!pair.Value.Enqueued && OriginalReceiptPeers.Contains(peer)
                    && OriginalRequestBatch.Count < TownServiceOriginalRequestCodec.MaxEntries)
                    OriginalRequestBatch.Add(new TownServiceOriginalReceiptEntry(pair.Key, pair.Value.Frame.Sequence));
            }
            foreach (ushort module in OriginalRequestsRemoved) owner.Modules.Remove(module);
            if (OriginalRequestBatch.Count != 0)
            {
                byte[] packet = TownServiceOriginalRequestCodec.Write(peer, owner.Service, owner.Session, 0, OriginalRequestBatch);
                send(packet, packet.Length);
                foreach (var entry in OriginalRequestBatch) owner.Modules[entry.Module].Enqueued = true;
            }
            if (owner.Modules.Count == 0) OriginalRequests.Remove(peer);
        }
    }

    /// <summary>Only the requester's missing, current exact immutable source may
    /// accelerate its already retained full repair. Wrong owners, sessions,
    /// stale sequences, withdrawn sources and actual fresh receipts are harmless.</summary>
    internal static bool ReceiveOriginalRequest(int sender, byte[] bytes, int length)
    {
        if (sender <= 0 || sender == LocalPeer
            || !TownServiceOriginalRequestCodec.TryRead(bytes, length, out var request)
            || !CollectReceiptPeers() || !OriginalReceiptPeers.Contains(sender)) return false;
        if (request!.OriginPeer != LocalPeer || request.Lane != 0
            || request.Service != PrivateLane.Service || request.Session != PrivateLane.Session) return true;
        foreach (var entry in request.Entries)
        {
            if (!PrivateLane.Active || !PrivateLane.Modules.TryGetValue(entry.Module, out LocalModule? source)) continue;
            TownServiceFrame? original = source.Baseline;
            NativeTemplateRepair? repair = source.NativeRepair;
            if (original == null || original.PublicCatalog || original.VisitorStock || original.BaseSequence != 0
                || original.Sequence != entry.Sequence || original.Service != request.Service
                || original.Session != request.Session || original.Module != entry.Module
                || repair?.Original == null || !ReferenceEquals(repair.Original, original)) continue;
            if (ReceivedOriginalReceipts.TryGetValue(entry.Module, out ReceivedOriginalReceipt? retained)
                && ReferenceEquals(retained.Source, source) && ReferenceEquals(retained.Baseline, original)
                && retained.Peers.Contains(sender)) continue;
            repair.Requested = true; repair.After = UnityEngine.Time.unscaledTime;
            RequestedOriginalRepairPending = true;
        }
        return true;
    }

    private static void RemoveOriginalRequestsPeer(int peer) => OriginalRequests.Remove(peer);
    private static void ResetOriginalRequests()
    {
        OriginalRequests.Clear(); OriginalRequestOwners.Clear(); OriginalRequestsRemoved.Clear();
        OriginalRequestBatch.Clear(); RequestedOriginalRepairPending = false;
    }
}
