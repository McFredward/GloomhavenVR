using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    // The driver supplies actual compatible continuation peers. A local send,
    // prefab cache, actor assignment or absent collector cannot establish receipt.
    internal static Action<List<int>>? CollectOriginalReceiptPeers;
    private static readonly List<int> OriginalReceiptPeers = new(8);
    private sealed class PendingOriginalReceipt
    {
        internal byte Service;
        internal uint Session;
        internal readonly Dictionary<ushort, ulong> Modules = new();
    }
    private sealed class ReceivedOriginalReceipt
    {
        internal LocalModule Source = null!;
        internal TownServiceFrame Baseline = null!;
        internal readonly HashSet<int> Peers = new();
    }
    private static readonly Dictionary<int, PendingOriginalReceipt> PendingOriginalReceipts = new();
    private static readonly Dictionary<ushort, ReceivedOriginalReceipt> ReceivedOriginalReceipts = new();
    private static byte OriginalReceiptService;
    private static uint OriginalReceiptSession;
    private static readonly List<int> OriginalReceiptOwners = new(24);
    private static readonly List<TownServiceOriginalReceiptEntry> OriginalReceiptBatch = new(TownServiceOriginalReceiptCodec.MaxEntries);

    /// <summary>Called only after the actual complete ReceiveParsed original has
    /// entered ReceivedBaselines. A cumulative delta or unprepared native-template
    /// packet cannot produce a receipt, even if it is waiting in another queue.</summary>
    internal static void RecordOriginalReceipt(int peer, TownServiceFrame frame)
    {
        if (peer <= 0 || peer == LocalPeer || frame.PublicCatalog || frame.VisitorStock
            || frame.BaseSequence != 0 || frame.Sequence == 0 || frame.Session == 0 || frame.Service < 1 || frame.Service > 3
            || frame.Module >= TownServiceFrame.VoiceModule || frame.Module == TownServiceFrame.UrgentBundleStream
            || !ReceivedBaselines.TryGetValue(peer, out var retained)
            || !retained.TryGetValue(frame.Module, out var original)
            || original.PublicCatalog || original.VisitorStock || original.BaseSequence != 0 || original.Sequence != frame.Sequence
            || original.Service != frame.Service || original.Session != frame.Session) return;
        if (!PendingOriginalReceipts.TryGetValue(peer, out var pending))
        {
            if (PendingOriginalReceipts.Count >= 24) return;
            pending = new PendingOriginalReceipt(); PendingOriginalReceipts.Add(peer, pending);
        }
        if (pending.Service != frame.Service || pending.Session != frame.Session)
        { pending.Modules.Clear(); pending.Service = frame.Service; pending.Session = frame.Session; }
        if (pending.Modules.Count >= TownServiceFrame.MaxModules && !pending.Modules.ContainsKey(frame.Module)) return;
        pending.Modules[frame.Module] = frame.Sequence;
    }

    /// <summary>Bounded metadata batches share the existing transport budget. The
    /// caller must enqueue reliably; thrown admission retains this pending batch.
    /// It never acknowledges bytes which have only been sent by the owner.</summary>
    internal static void CaptureOriginalReceipts(Action<byte[], int> send)
    {
        if (send == null || PendingOriginalReceipts.Count == 0) return;
        // Originals can arrive before the build handshake has admitted their
        // owner. Keep the receipt pending until that owner is compatible; a
        // prematurely published one-shot ACK may be rejected and lost forever.
        if (!CollectReceiptPeers()) return;
        OriginalReceiptOwners.Clear();
        foreach (int owner in PendingOriginalReceipts.Keys) OriginalReceiptOwners.Add(owner);
        foreach (int owner in OriginalReceiptOwners)
        {
            if (!OriginalReceiptPeers.Contains(owner)) continue;
            PendingOriginalReceipt pending = PendingOriginalReceipts[owner];
            OriginalReceiptBatch.Clear();
            foreach (var pair in pending.Modules)
            {
                // A late close/retirement may have removed the received original
                // before receipt publication. Never affirm an already lost baseline.
                if (!ReceivedBaselines.TryGetValue(owner, out var retained)
                    || !retained.TryGetValue(pair.Key, out var original)
                    || original.Service != pending.Service || original.Session != pending.Session
                    || original.Sequence != pair.Value || original.BaseSequence != 0) continue;
                OriginalReceiptBatch.Add(new TownServiceOriginalReceiptEntry(pair.Key, pair.Value));
                if (OriginalReceiptBatch.Count == TownServiceOriginalReceiptCodec.MaxEntries) break;
            }
            if (OriginalReceiptBatch.Count > 0)
            {
                byte[] packet = TownServiceOriginalReceiptCodec.Write(owner, pending.Service, pending.Session, 0, OriginalReceiptBatch);
                send(packet, packet.Length);
                foreach (var entry in OriginalReceiptBatch) pending.Modules.Remove(entry.Module);
            }
            // Stale pending identities are discarded rather than keep a nonempty
            // owner forever after its original network session has been withdrawn.
            var stale = new List<ushort>();
            foreach (var pair in pending.Modules)
                if (!ReceivedBaselines.TryGetValue(owner, out var retained)
                    || !retained.TryGetValue(pair.Key, out var original)
                    || original.Service != pending.Service || original.Session != pending.Session
                    || original.Sequence != pair.Value || original.BaseSequence != 0) stale.Add(pair.Key);
            foreach (ushort module in stale) pending.Modules.Remove(module);
            if (pending.Modules.Count == 0) PendingOriginalReceipts.Remove(owner);
        }
    }

    internal static bool ReceiveOriginalReceipt(int sender, byte[] bytes, int length)
    {
        if (sender <= 0 || sender == LocalPeer
            || !TownServiceOriginalReceiptCodec.TryRead(bytes, length, out var receipt)
            || !CollectReceiptPeers() || !OriginalReceiptPeers.Contains(sender)) return false;
        // The side channel broadcasts: another owner's valid receipt, or an old
        // session's reliably delivered receipt, is harmless and needs no warning.
        // Neither can credit the local source's retained exact original.
        if (receipt!.OriginPeer != LocalPeer || receipt.Lane != 0
            || receipt.Service != PrivateLane.Service || receipt.Session != PrivateLane.Session) return true;
        RefreshOriginalReceiptLane();
        foreach (var entry in receipt.Entries)
        {
            if (!PrivateLane.Modules.TryGetValue(entry.Module, out var module)) continue;
            TownServiceFrame? baseline = module.Baseline;
            if (baseline == null || baseline.PublicCatalog || baseline.VisitorStock || baseline.BaseSequence != 0
                || baseline.Sequence != entry.Sequence || baseline.Service != receipt.Service
                || baseline.Session != receipt.Session || baseline.Module != entry.Module) continue;
            if (!ReceivedOriginalReceipts.TryGetValue(entry.Module, out var retained)
                || !ReferenceEquals(retained.Source, module) || !ReferenceEquals(retained.Baseline, baseline))
            {
                // Retired module IDs are not reused within a session. Bound retained
                // receipts too, even if a long visit repeatedly replaces widgets.
                if (!ReceivedOriginalReceipts.ContainsKey(entry.Module)
                    && ReceivedOriginalReceipts.Count >= TownServiceFrame.MaxModules)
                {
                    var obsolete = new List<ushort>();
                    foreach (var pair in ReceivedOriginalReceipts)
                        if (!PrivateLane.Modules.TryGetValue(pair.Key, out var source)
                            || !ReferenceEquals(pair.Value.Source, source)
                            || !ReferenceEquals(pair.Value.Baseline, source.Baseline)) obsolete.Add(pair.Key);
                    foreach (ushort id in obsolete) ReceivedOriginalReceipts.Remove(id);
                    if (ReceivedOriginalReceipts.Count >= TownServiceFrame.MaxModules) continue;
                }
                retained = new ReceivedOriginalReceipt { Source = module, Baseline = baseline };
                ReceivedOriginalReceipts[entry.Module] = retained;
            }
            if (retained.Peers.Count < 24 || retained.Peers.Contains(sender)) retained.Peers.Add(sender);
        }
        return true;
    }

    private static bool HasReceivedOriginal(LocalModule module)
    {
        RefreshOriginalReceiptLane();
        TownServiceFrame? baseline = module.Baseline;
        if (baseline == null || baseline.PublicCatalog || baseline.VisitorStock || baseline.BaseSequence != 0
            || baseline.Service != PrivateLane.Service || baseline.Session != PrivateLane.Session
            || !PrivateLane.Modules.TryGetValue(module.Id, out var current) || !ReferenceEquals(current, module)
            || !ReceivedOriginalReceipts.TryGetValue(module.Id, out var retained)
            || !ReferenceEquals(retained.Source, module) || !ReferenceEquals(retained.Baseline, baseline)
            || !CollectReceiptPeers()) return false;
        bool hasRemotePeer = false;
        foreach (int peer in OriginalReceiptPeers)
        {
            if (peer <= 0 || peer == LocalPeer) continue;
            hasRemotePeer = true;
            if (!retained.Peers.Contains(peer)) return false;
        }
        return hasRemotePeer;
    }

    private static bool CollectReceiptPeers()
    {
        OriginalReceiptPeers.Clear();
        if (CollectOriginalReceiptPeers == null) return false;
        try { CollectOriginalReceiptPeers(OriginalReceiptPeers); }
        catch { OriginalReceiptPeers.Clear(); return false; }
        return true;
    }
    private static void RefreshOriginalReceiptLane()
    {
        if (OriginalReceiptService == PrivateLane.Service && OriginalReceiptSession == PrivateLane.Session) return;
        OriginalReceiptService = PrivateLane.Service; OriginalReceiptSession = PrivateLane.Session;
        ReceivedOriginalReceipts.Clear();
    }
    internal static void RemoveOriginalReceiptPeer(int peer)
    {
        PendingOriginalReceipts.Remove(peer);
        RemoveOriginalRequestsPeer(peer);
        foreach (var receipt in ReceivedOriginalReceipts.Values) receipt.Peers.Remove(peer);
    }
    internal static void ResetOriginalReceipts()
    {
        ResetOriginalRequests();
        PendingOriginalReceipts.Clear(); ReceivedOriginalReceipts.Clear(); OriginalReceiptPeers.Clear();
        OriginalReceiptOwners.Clear(); OriginalReceiptBatch.Clear(); OriginalReceiptService = 0; OriginalReceiptSession = 0;
    }
}
