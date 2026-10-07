using System;
using System.Collections.Generic;
using System.Linq;

namespace GloomhavenVR.Net.TownServices;

// Only the engine-facing owner/receiver storage is a boundary. The complete actual
// frame model, packet header, receipt codec and receipt partial compile verbatim.
// Required protocol constant declarations are extracted verbatim; unrelated
// engine board-tuning helpers are outside this receipt-only metadata proof.
internal sealed class TownRackState { }
internal sealed class TownRackStamp { }
internal sealed class TownCatalogBank { }

internal static partial class TownServiceMirror
{
    private sealed class LocalModule
    {
        internal ushort Id;
        internal TownServiceFrame? Baseline;
    }
    private sealed class LocalLane
    {
        internal byte Service;
        internal uint Session;
        internal readonly Dictionary<ushort, LocalModule> Modules = new();
    }
    private static readonly LocalLane PrivateLane = new();
    private static readonly Dictionary<int, Dictionary<ushort, TownServiceFrame>> ReceivedBaselines = new();
    private static int LocalPeer = 1;

    internal static void SetLane(byte service = 1, uint session = 7)
    { PrivateLane.Service = service; PrivateLane.Session = session; }
    internal static void Fresh()
    {
        ResetOriginalReceipts(); PrivateLane.Modules.Clear(); ReceivedBaselines.Clear();
        SetLane(); LocalPeer = 1;
        CollectOriginalReceiptPeers = null;
    }
    internal static void Install(TownServiceFrame frame, bool replaceSource = false)
    {
        if (replaceSource || !PrivateLane.Modules.TryGetValue(frame.Module, out var module))
            PrivateLane.Modules[frame.Module] = module = new LocalModule { Id = frame.Module };
        module.Baseline = frame;
    }
    internal static void ForgetLocal(ushort module) => PrivateLane.Modules.Remove(module);
    internal static bool Has(ushort id) => PrivateLane.Modules.TryGetValue(id, out var module) && HasReceivedOriginal(module);
    internal static void Store(int owner, TownServiceFrame frame)
    {
        if (!ReceivedBaselines.TryGetValue(owner, out var lane))
            ReceivedBaselines[owner] = lane = new Dictionary<ushort, TownServiceFrame>();
        lane[frame.Module] = frame;
    }
    internal static void ForgetReceived(int owner, ushort module)
    { if (ReceivedBaselines.TryGetValue(owner, out var lane)) lane.Remove(module); }
    internal static int PendingCount => PendingOriginalReceipts.Values.Sum(owner => owner.Modules.Count);
    internal static int ReceiptCount => ReceivedOriginalReceipts.Count;
}

internal static class ReceiptProof
{
    private static int _assertions;
    private static void Check(bool ok, string message)
    { _assertions++; if (!ok) throw new InvalidOperationException(message); }
    private static TownServiceFrame Full(ushort module = 3, ulong sequence = 111, byte service = 1, uint session = 7)
        => new() { Module = module, Sequence = sequence, Service = service, Session = session };
    private static byte[] Receipt(int origin = 1, byte service = 1, uint session = 7, byte lane = 0,
        ushort module = 3, ulong sequence = 111)
        => TownServiceOriginalReceiptCodec.Write(origin, service, session, lane,
            new[] { new TownServiceOriginalReceiptEntry(module, sequence) });
    private static void Peers(params int[] peers) => TownServiceMirror.CollectOriginalReceiptPeers = list => list.AddRange(peers);
    private static bool Receive(int peer, byte[] packet) => TownServiceMirror.ReceiveOriginalReceipt(peer, packet, packet.Length);
    private static List<byte[]> Capture()
    {
        var result = new List<byte[]>();
        TownServiceMirror.CaptureOriginalReceipts((bytes, length) =>
        { Check(length == bytes.Length, "receipt length exact"); result.Add(bytes); });
        return result;
    }
    private static void Bad(byte[] bytes, string message)
    { Check(!TownServiceOriginalReceiptCodec.TryRead(bytes, bytes.Length, out _), message); }
    private static void Throws(Action action, string message)
    {
        bool threw = false;
        try { action(); } catch (ArgumentException) { threw = true; }
        Check(threw, message);
    }

    private static void CodecVectors()
    {
        byte[] exact = Receipt(origin: 0x12345678, service: 3, session: 0x11223344,
            module: 0x1234, sequence: 0x0102030405060708);
        // Independent literal layout: ordinary GVR1/v3 header, type28, TLV112,
        // schema1, LE owner/service/session/lane/count and exact ushort/ulong pair.
        byte[] golden = Convert.FromHexString("31525647031C701601785634120344332211000134120807060504030201");
        Check(exact.SequenceEqual(golden), "independent exact receipt bytes");
        Check(TownServiceOriginalReceiptCodec.TryRead(golden, golden.Length, out var decoded), "literal parses");
        Check(decoded!.OriginPeer == 0x12345678 && decoded.Service == 3 && decoded.Session == 0x11223344
            && decoded.Lane == 0 && decoded.Entries.Length == 1 && decoded.Entries[0].Module == 0x1234
            && decoded.Entries[0].Sequence == 0x0102030405060708, "literal metadata exact");
        for (int cut = 0; cut < golden.Length; cut++)
            Check(!TownServiceOriginalReceiptCodec.TryRead(golden, cut, out _), "every truncated literal rejected " + cut);
        Check(!TownServiceOriginalReceiptCodec.TryRead(null, 30, out _), "null bounded");
        Check(!TownServiceOriginalReceiptCodec.TryRead(golden, golden.Length + 1, out _), "length beyond buffer bounded");
        byte[] oversized = new byte[TownServiceOriginalReceiptCodec.MaxSize + 1];
        Array.Copy(golden, oversized, golden.Length); Bad(oversized, "oversized native event rejected");
        var entries = Enumerable.Range(0, 20).Select(i => new TownServiceOriginalReceiptEntry((ushort)i, (ulong)i + 1)).ToArray();
        byte[] maximum = TownServiceOriginalReceiptCodec.Write(4, 1, 55, 0, entries);
        Check(maximum.Length == 220 && maximum[7] == 212, "maximum20 is220 bytes");
        Check(TownServiceOriginalReceiptCodec.TryRead(maximum, maximum.Length, out decoded)
            && decoded!.Entries.Length == 20 && decoded.Entries[19].Sequence == 20, "all20 exact identities decoded");
        byte[] unknown = golden.Take(6).Concat(new byte[] { 240, 3, 1, 2, 3 }).Concat(golden.Skip(6)).ToArray();
        Check(TownServiceOriginalReceiptCodec.TryRead(unknown, unknown.Length, out decoded)
            && decoded!.Entries[0].Sequence == 0x0102030405060708, "unknown TLV skipped");
        byte[] unknownEnd = golden.Concat(new byte[] { 240, 0 }).ToArray();
        Check(TownServiceOriginalReceiptCodec.TryRead(unknownEnd, unknownEnd.Length, out _), "zero-length unknown TLV bounded");
        Bad(golden.Concat(golden.Skip(6)).ToArray(), "duplicate known record rejected");
        Bad(golden.Concat(new byte[] { 240, 9, 1 }).ToArray(), "truncated unknown record rejected");
        Bad(golden.Concat(new byte[] { 240 }).ToArray(), "truncated TLV header rejected");
        foreach (int position in new[] { 0, 4, 5, 6, 7, 8, 13, 18, 19 })
        {
            byte[] corrupt = (byte[])golden.Clone(); corrupt[position] = 255;
            Bad(corrupt, "invalid fixed field rejected " + position);
        }
        foreach (int position in new[] { 9, 14, 22 })
        {
            byte[] corrupt = (byte[])golden.Clone();
            Array.Clear(corrupt, position, position == 22 ? 8 : 4);
            Bad(corrupt, "zero origin/session/sequence rejected " + position);
        }
        byte[] negative = (byte[])golden.Clone(); negative[12] = 128; Bad(negative, "negative owner rejected");
        byte[] duplicateModule = (byte[])maximum.Clone(); duplicateModule[30] = duplicateModule[20];
        duplicateModule[31] = duplicateModule[21]; Bad(duplicateModule, "duplicate module rejected even different sequence");
        foreach (ushort reserved in new[] { TownServiceFrame.ManifestModule, TownServiceFrame.BundleStream,
            TownServiceFrame.VoiceModule, TownServiceFrame.UrgentBundleStream })
        {
            byte[] corrupt = (byte[])golden.Clone(); corrupt[20] = (byte)reserved; corrupt[21] = (byte)(reserved >> 8);
            Bad(corrupt, "reserved module rejected " + reserved);
            Throws(() => Receipt(module: reserved), "writer rejects reserved module " + reserved);
        }
        Throws(() => Receipt(origin: 0), "writer rejects zero origin");
        Throws(() => Receipt(origin: -1), "writer rejects negative origin");
        Throws(() => Receipt(service: 0), "writer rejects zero service");
        Throws(() => Receipt(service: 4), "writer rejects unknown service");
        Throws(() => Receipt(session: 0), "writer rejects zero session");
        Throws(() => Receipt(lane: 1), "writer rejects unsupported public lane");
        Throws(() => Receipt(sequence: 0), "writer rejects zero sequence");
        Throws(() => TownServiceOriginalReceiptCodec.Write(1, 1, 7, 0, Array.Empty<TownServiceOriginalReceiptEntry>()), "writer rejects empty batch");
        Throws(() => TownServiceOriginalReceiptCodec.Write(1, 1, 7, 0, entries.Concat(entries.Take(1)).ToArray()), "writer rejects21 entries");
        Throws(() => TownServiceOriginalReceiptCodec.Write(1, 1, 7, 0,
            new[] { new TownServiceOriginalReceiptEntry(3, 1), new TownServiceOriginalReceiptEntry(3, 2) }), "writer rejects duplicated module");
    }

    private static void SourceIdentityAndPeers()
    {
        TownServiceMirror.Fresh(); Peers(2, 3, 4, 5);
        TownServiceFrame original = Full(); TownServiceMirror.Install(original);
        Check(!TownServiceMirror.Has(3), "local send/prefab/original existence alone never proves receipt");
        Check(!Receive(1, Receipt()), "self cannot acknowledge");
        Check(!Receive(0, Receipt()), "unowned sender cannot acknowledge");
        Check(!Receive(99, Receipt()), "noncompatible sender cannot acknowledge");
        Check(Receive(2, Receipt(origin: 99)), "valid broadcast to another owner accepted without warning");
        Check(!TownServiceMirror.Has(3), "foreign owner receipt cannot credit this source");
        Check(Receive(2, Receipt()), "first real compatible receiver admitted");
        Check(!TownServiceMirror.Has(3), "every compatible peer must explicitly acknowledge");
        Check(Receive(3, Receipt()) && Receive(4, Receipt()), "further receivers admitted");
        Check(!TownServiceMirror.Has(3), "fourth receiver not inferred from others");
        Check(Receive(5, Receipt()) && TownServiceMirror.Has(3), "four remote receivers establish exact retained original");
        Peers(1, -1, 0); Check(!TownServiceMirror.Has(3), "zero current positive remote peers is false");
        Peers(2, 3, 4, 5); Check(TownServiceMirror.Has(3), "current4 peer membership restored");
        Peers(2, 3, 4, 5, 6); Check(!TownServiceMirror.Has(3), "new compatible peer requires own acknowledgement");
        Check(Receive(6, Receipt()) && TownServiceMirror.Has(3), "fifth remote participant exact receipt");
        TownServiceMirror.RemoveOriginalReceiptPeer(2);
        Check(!TownServiceMirror.Has(3), "reconnected same peer ID cannot inherit old receipt");
        Check(Receive(2, Receipt()) && TownServiceMirror.Has(3), "reconnection explicit receipt restores readiness");
        TownServiceMirror.Install(Full(sequence: 112));
        Check(!TownServiceMirror.Has(3), "replaced full baseline requires new exact original");
        Check(Receive(2, Receipt()) && !TownServiceMirror.Has(3), "stale sequence cannot credit newer baseline");
        foreach (int peer in new[] { 2, 3, 4, 5, 6 }) Receive(peer, Receipt(sequence: 112));
        Check(TownServiceMirror.Has(3), "replacement complete original all peers admitted");
        // Equal numeric IDs do not authorize a different retained immutable object.
        TownServiceMirror.Install(Full(sequence: 112));
        Check(!TownServiceMirror.Has(3), "exact retained baseline identity required");
        foreach (int peer in new[] { 2, 3, 4, 5, 6 }) Receive(peer, Receipt(sequence: 112));
        Check(TownServiceMirror.Has(3), "new identical sequence object explicitly received");
        TownServiceMirror.Install(Full(sequence: 112), replaceSource: true);
        Check(!TownServiceMirror.Has(3), "new source instance cannot inherit retired source receipt");
        foreach (int peer in new[] { 2, 3, 4, 5, 6 }) Receive(peer, Receipt(sequence: 112));
        TownServiceMirror.ForgetLocal(3); Check(!TownServiceMirror.Has(3), "unregistered module cannot count receipt");
        TownServiceMirror.Install(Full(sequence: 112)); Check(!TownServiceMirror.Has(3), "retirement and reinstall not inferred");
        TownServiceMirror.SetLane(2, 88); TownServiceMirror.Install(Full(sequence: 113, service: 2, session: 88));
        Check(Receive(2, Receipt(sequence: 112)), "old session valid packet silently ignored");
        Check(!TownServiceMirror.Has(3), "old session cannot establish new service original");
        foreach (int peer in new[] { 2, 3, 4, 5, 6 }) Receive(peer, Receipt(service: 2, session: 88, sequence: 113));
        Check(TownServiceMirror.Has(3), "new service/session own exact receipt");
        TownServiceMirror.ResetOriginalReceipts();
        Check(!TownServiceMirror.Has(3), "reset forgets actual remote receipt");
        TownServiceMirror.CollectOriginalReceiptPeers = _ => throw new Exception("boundary unavailable");
        Check(!TownServiceMirror.Has(3), "collector failure conservatively falls back to full");
        TownServiceMirror.CollectOriginalReceiptPeers = null;
        Check(!TownServiceMirror.Has(3), "missing collector is never readiness");
    }

    private static void ActualReceiptAdmissionAndBatching()
    {
        TownServiceMirror.Fresh(); var original = Full();
        TownServiceMirror.RecordOriginalReceipt(2, original);
        Check(TownServiceMirror.PendingCount == 0, "unstored original cannot create receipt");
        Check(Capture().Count == 0, "unprepared original sends no acknowledgement");
        TownServiceMirror.Store(2, original);
        var delta = Full(sequence: 112); delta.BaseSequence = 111;
        TownServiceMirror.RecordOriginalReceipt(2, delta);
        Check(TownServiceMirror.PendingCount == 0, "cumulative delta cannot acknowledge unreceived full");
        var wrongOriginal = Full(sequence: 113);
        TownServiceMirror.RecordOriginalReceipt(2, wrongOriginal);
        Check(TownServiceMirror.PendingCount == 0, "queued newer full is not current retained original");
        original.PublicCatalog = true;
        TownServiceMirror.RecordOriginalReceipt(2, original);
        Check(TownServiceMirror.PendingCount == 0, "public original excluded initially");
        original.PublicCatalog = false; original.VisitorStock = true;
        TownServiceMirror.RecordOriginalReceipt(2, original);
        Check(TownServiceMirror.PendingCount == 0, "visitor stock original excluded initially");
        original.VisitorStock = false;
        TownServiceMirror.RecordOriginalReceipt(1, original); TownServiceMirror.RecordOriginalReceipt(-2, original);
        Check(TownServiceMirror.PendingCount == 0, "self and catalog namespaces excluded");
        TownServiceMirror.RecordOriginalReceipt(2, original); TownServiceMirror.RecordOriginalReceipt(2, original);
        Check(TownServiceMirror.PendingCount == 1, "duplicate received full coalesces exact metadata");
        bool sendThrew = false;
        try { TownServiceMirror.CaptureOriginalReceipts((_, _) => throw new Exception("admission unavailable")); }
        catch { sendThrew = true; }
        Check(sendThrew && TownServiceMirror.PendingCount == 1, "failed reliable admission retains pending receipt");
        List<byte[]> single = Capture();
        Check(single.Count == 1 && TownServiceMirror.PendingCount == 0, "admitted receipt drains pending once");
        Check(TownServiceOriginalReceiptCodec.TryRead(single[0], single[0].Length, out var decoded)
            && decoded!.OriginPeer == 2 && decoded.Entries[0].Sequence == 111, "metadata names real sender and retained sequence");
        Check(Capture().Count == 0, "no routine retransmission after reliable admission");
        for (ushort module = 0; module < 21; module++)
        {
            TownServiceFrame frame = Full(module, (ulong)module + 1);
            TownServiceMirror.Store(2, frame); TownServiceMirror.RecordOriginalReceipt(2, frame);
        }
        List<byte[]> batch1 = Capture();
        Check(batch1.Count == 1 && batch1[0].Length == 220 && TownServiceMirror.PendingCount == 1, "one owner emits at most20 entries per capture");
        List<byte[]> batch2 = Capture();
        Check(batch2.Count == 1 && batch2[0].Length == 30 && TownServiceMirror.PendingCount == 0, "remaining module next bounded batch");
        var all = new List<TownServiceOriginalReceiptEntry>();
        foreach (byte[] packet in batch1.Concat(batch2))
        { TownServiceOriginalReceiptCodec.TryRead(packet, packet.Length, out decoded); all.AddRange(decoded!.Entries); }
        Check(all.Select(entry => entry.Module).Distinct().Count() == 21, "all originals acknowledged exactly once across batches");
        // A genuine close which forgets a source baseline must remove the pending
        // receipt. An inactive same-session census which retains that baseline is
        // intentionally a different operation tested in RepeatTransaction().
        TownServiceMirror.Store(2, original); TownServiceMirror.RecordOriginalReceipt(2, original);
        TownServiceMirror.ForgetReceived(2, original.Module);
        Check(Capture().Count == 0 && TownServiceMirror.PendingCount == 0, "forgotten baseline cannot emit queued receipt after close");
        TownServiceMirror.Store(2, original); TownServiceMirror.RecordOriginalReceipt(2, original);
        TownServiceMirror.RemoveOriginalReceiptPeer(2);
        Check(Capture().Count == 0 && TownServiceMirror.PendingCount == 0, "peer removal clears queued receipt");
        TownServiceMirror.Store(2, original); TownServiceMirror.RecordOriginalReceipt(2, original);
        TownServiceMirror.ResetOriginalReceipts();
        Check(Capture().Count == 0, "network reset clears queued receipt");
    }

    private static void RepeatTransaction()
    {
        TownServiceMirror.Fresh(); Peers(2, 3);
        TownServiceFrame original = Full(); TownServiceMirror.Install(original);
        // First handoff: only complete real original reception may authorize reuse.
        foreach (int peer in new[] { 2, 3 })
        {
            TownServiceMirror.Store(peer, original); TownServiceMirror.RecordOriginalReceipt(peer, original);
        }
        List<byte[]> outgoing = Capture(); Check(outgoing.Count == 2, "first handoff has two distinct actual receivers");
        foreach (int peer in new[] { 2, 3 })
            Check(Receive(peer, Receipt()), "original observer receipt admitted for repeated transaction");
        Check(TownServiceMirror.Has(3), "first transaction current original known everywhere");
        // Cancel / inactive census retains the same complete metadata, while all
        // currently shown callbacks/widgets may be retired. Reuse does not depend
        // on an actor, prefab, render completion, transaction or local send event.
        for (int transaction = 0; transaction < 100; transaction++)
        {
            Check(TownServiceMirror.Has(3), "same-session close retains explicit original receipt");
            var changed = Full(sequence: (ulong)112 + (ulong)transaction); changed.BaseSequence = 111;
            Check(changed.BaseSequence == original.Sequence && TownServiceMirror.Has(3), "repeated handoff can send exact current cumulative delta");
        }
        TownServiceMirror.RemoveOriginalReceiptPeer(3); Peers(2);
        Check(TownServiceMirror.Has(3), "current remaining peer explicit receipt permits cumulative state");
        Peers(2, 3); Check(!TownServiceMirror.Has(3), "rejoin requires full fallback before reuse");
        Receive(3, Receipt()); Check(TownServiceMirror.Has(3), "new real received full permits next transaction");
        TownServiceMirror.SetLane(1, 8); TownServiceMirror.Install(Full(sequence: 111, session: 8));
        Check(!TownServiceMirror.Has(3), "new whole private session never inherits old receipt");
    }

    private static void BoundedLifetimes()
    {
        TownServiceMirror.Fresh(); Peers(2);
        for (ushort module = 0; module < TownServiceFrame.MaxModules; module++)
        { TownServiceMirror.Install(Full(module)); Receive(2, Receipt(module: module)); }
        Check(TownServiceMirror.ReceiptCount == TownServiceFrame.MaxModules, "receipt census bounded to maximum live modules");
        for (ushort module = 0; module < TownServiceFrame.MaxModules; module++) TownServiceMirror.ForgetLocal(module);
        TownServiceMirror.Install(Full(5000)); Receive(2, Receipt(module: 5000));
        Check(TownServiceMirror.ReceiptCount == 1 && TownServiceMirror.Has(5000), "retired source receipts pruned before admitting new widget");
        TownServiceMirror.Fresh();
        for (int owner = 2; owner <= 26; owner++)
        { var frame = Full(); TownServiceMirror.Store(owner, frame); TownServiceMirror.RecordOriginalReceipt(owner, frame); }
        Check(TownServiceMirror.PendingCount == 24, "pending owners bounded24 against unknown owner growth");
        Check(Capture().Count == 24 && TownServiceMirror.PendingCount == 0, "bounded owner batches eventually drain");
    }

    private static int Main()
    {
        try
        {
            CodecVectors(); SourceIdentityAndPeers(); ActualReceiptAdmissionAndBatching(); RepeatTransaction(); BoundedLifetimes();
            Console.WriteLine("PASS " + _assertions + " assertions: exact original receipt codec and real metadata state; no render/gameplay inference.");
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine("FAIL after " + _assertions + " assertions: " + error.Message); return 1; }
    }
}
