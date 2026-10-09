using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Independent bounded module queues inside the existing global event budget.</summary>
internal sealed partial class TownServiceLaneSendQueue
{
    private readonly Dictionary<ushort, ExtrasSendQueue> _queues = new();
    private readonly List<ushort> _order = new();
    private readonly ulong _seed;
    private readonly Dictionary<ushort, ulong> _sequences = new();
    private readonly Dictionary<ushort, ExtrasSendQueue> _clocks = new();
    private readonly HashSet<ushort> _catalogOriginals = new();
    private readonly Dictionary<ushort, TownServiceFrame> _catalogBases = new();
    private readonly Dictionary<ushort, ulong> _catalogBaseKeys = new(), _catalogQueuedKeys = new();
    private readonly Dictionary<ushort, TownServiceFrame> _catalogRepairs = new();
    private static readonly Dictionary<ushort, TownServiceFrame> NoCatalogPatchBases = new();
    private int _clockCursor;
    private bool _clockRepairDue;
    private int _cursor;
    private int _priorityCursor, _priorityTurns;
    private ushort? _normalActive, _priorityActive;
    private readonly HashSet<ushort> _priority = new();
    private readonly Dictionary<ushort, ulong> _coldPriority = new();
    private readonly Dictionary<ushort, TownServiceFrame> _latestOriginals = new();
    private byte[]? _manifestBytes;
    private TownServiceFrame? _manifestFrame;
    private TownServiceFrame? _sentManifest;
    private double _nextManifest;
    private readonly ExtrasSendQueue _bundle;
    private readonly ExtrasSendQueue _urgentBundle;
    private readonly List<TownServiceFrame> _bundleFrames = new();
    private readonly List<TownServiceFrame> _urgentBundleFrames = new();
    private readonly Dictionary<ushort, byte[]> _bundleBytes = new();
    private readonly Dictionary<ushort, byte[]> _urgentBundleBytes = new();
    private readonly HashSet<ushort> _promotedBundle = new();
    private uint _session;
    private byte _service;
    private bool _openingPending;
    private int _openingPages;
    private double _openingStarted;
    internal TownServiceLaneSendQueue(ulong seed)
    {
        _seed = seed;
        _bundle = new ExtrasSendQueue((seed & ~65535UL) | TownServiceFrame.BundleStream,
            TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
            snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
            counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: seed & TownServiceFragments.StockLaneMarker);
        _urgentBundle = new ExtrasSendQueue((seed & ~65535UL) | TownServiceFrame.UrgentBundleStream,
            TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
            snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
            counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: seed & TownServiceFragments.StockLaneMarker);
    }
    internal void Enqueue(byte[] bytes, int length, TownServiceFrame frame)
    {
        if (_session != frame.Session || _service != frame.Service)
        { Clear(); _session = frame.Session; _service = frame.Service; }
        if (!_queues.TryGetValue(frame.Module, out ExtrasSendQueue? queue))
        {
            if (_queues.Count >= TownServiceFrame.MaxModules + 1) return;
            ulong sequence = _sequences.TryGetValue(frame.Module, out ulong previous) ? previous : (_seed & ~65535UL) | frame.Module;
            queue = new ExtrasSendQueue(sequence, TownServiceCodec.MessageType,
                TownServiceCodec.FragmentType, preserveFirst: true, snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
                counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: _seed & TownServiceFragments.StockLaneMarker);
            _queues.Add(frame.Module, queue); _order.Add(frame.Module);
        }
        bool retainedNativeOriginal = frame.Service == 3 && !frame.PublicCatalog && !frame.VisitorStock
            && frame.BaseSequence == 0 && _latestOriginals.TryGetValue(frame.Module, out var sameOriginal)
            && ReferenceEquals(sameOriginal, frame);
        if (TownRequestedOriginalRepair.Source?.Invoke(frame) == true)
            _requestedOriginals[frame.Module] = frame;
        // Ordinary mage originals can precede the offer that promotes their
        // deltas. Their exact full repair needs the same bounded dependency pin
        // as an original which was already urgent on its first capture.
        if (frame.Module != TownServiceFrame.ManifestModule && frame.BaseSequence == 0
            && !frame.PublicCatalog && !frame.VisitorStock && (frame.HighPriority || frame.Service == 3)
            && (!_latestOriginals.TryGetValue(frame.Module, out var previousOriginal)
                || frame.Sequence > previousOriginal.Sequence)) _latestOriginals[frame.Module] = frame;
        if (frame.Module == TownServiceFrame.ManifestModule)
        {
            // Reserve the first finite coherent picture of a physical offer, not
            // every subsequent hover, repair or session heartbeat.
            TownServiceFrame? previous = _manifestFrame ?? _sentManifest;
            bool before = previous?.TransactionActive ?? false;
            if (frame.TransactionActive && (!before || previous != null && !SameCensus(previous, frame))
                && !frame.PublicCatalog && !frame.VisitorStock && frame.Service is 1 or 3)
            { _openingPending = true; _openingPages = 0; _openingStarted = double.NaN; }
            if (!frame.TransactionActive) _openingPending = false;
            // Keep one newest census, not four repeated 8 KiB heartbeats in front of
            // every item. The current fragmented census still finishes atomically.
            _manifestBytes = new byte[length]; Buffer.BlockCopy(bytes, 0, _manifestBytes, 0, length);
            _manifestFrame = frame;
            // Obsolete row queues must not keep the last session's catalog in flight forever.
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                ushort id = _order[i];
                if (id == TownServiceFrame.ManifestModule || Array.BinarySearch(frame.Modules, id) >= 0 || _catalogOriginals.Contains(id)) continue;
                _sequences[id] = _queues[id].Sequence; _queues[id].Clear(); _queues.Remove(id); _order.RemoveAt(i); _priority.Remove(id); _coldPriority.Remove(id); _latestOriginals.Remove(id); _requestedOriginals.Remove(id);
                if (_clocks.TryGetValue(id, out var clock)) { _sequences[id] = Math.Max(_sequences[id], clock.Sequence); clock.Clear(); _clocks.Remove(id); }
            }
        }
        else
        {
            if (frame.PublicCatalog && !frame.VisitorStock && frame.RackMember != null && !frame.RackMember.Detached)
            {
                _catalogOriginals.Add(frame.Module);
                if (frame.BaseSequence == 0 && !_catalogBases.ContainsKey(frame.Module))
                {
                    _catalogBases[frame.Module] = frame;
                    _catalogBaseKeys[frame.Module] = TownCatalogBank.ContentKey(frame);
                }
                if (frame.BaseSequence == 0)
                    _catalogQueuedKeys[frame.Module] = TownCatalogBank.ContentKey(frame);
            }
            if (frame.PublicCatalog && frame.BaseSequence == 0 && frame.CatalogBank?.Prepared == true)
            {
                if (!_clocks.TryGetValue(frame.Module, out var clock) && _clocks.Count < 2)
                {
                    clock = new ExtrasSendQueue(queue.Sequence, TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
                        snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
                        counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: _seed & TownServiceFragments.StockLaneMarker);
                    _clocks.Add(frame.Module, clock);
                }
                if (clock != null)
                {
                    for (int i = 0; i < frame.CatalogBank.Updates.Length; i++)
                    {
                        TownServiceFrame current = frame.CatalogBank.Updates[i];
                        ulong key = frame.CatalogBank.Members[i].ContentKey;
                        if (!_catalogBases.ContainsKey(current.Module))
                        { _catalogBases[current.Module] = current; _catalogBaseKeys[current.Module] = key; }
                        if (!_catalogQueuedKeys.TryGetValue(current.Module, out ulong queued) || queued != key)
                        {
                            // Keep immutable originals, not serialized copies of the
                            // entire bank on every button press. Loss repair still owns
                            // real property delivery and late-join preparation.
                            TownServiceFrame repair = TownServiceDelta.Retain(current);
                            repair.HighPriority = true;
                            _catalogRepairs[current.Module] = repair;
                        }
                    }
                    TownServiceFrame reference = TownCatalogClock.Create(frame, _catalogBases, _catalogBaseKeys);
                    // The original root remains in _order for arbitration, but only
                    // this current header clock is queued. A second full bank would
                    // repeat multi-megabyte native serialization on every page turn.
                    try { byte[] referenceBytes = TownServiceCodec.Write(reference); clock.Enqueue(referenceBytes, referenceBytes.Length, frame); }
                    catch (System.IO.InvalidDataException)
                    {
                        // A large property delta can exceed the atomic header bound.
                        // Keep the complete current target keys and headers; bounded
                        // individual original repairs supply their exact dependencies.
                        // Never drop the owner's turn or publish a partial ready bank.
                        byte[] referenceBytes = TownServiceCodec.Write(TownCatalogClock.Create(frame, NoCatalogPatchBases));
                        clock.Enqueue(referenceBytes, referenceBytes.Length, frame);
                    }
                    return;
                }
            }
            if (frame.HighPriority || retainedNativeOriginal && _priority.Contains(frame.Module))
            {
                _priority.Add(frame.Module);
                if (frame.BaseSequence == 0)
                {
                    // Replacing an already offered card can keep the same lease
                    // and active census. It still starts a finite new picture;
                    // the previous completed offer cannot leave its expired timer.
                    if (_coldPriority.Count == 0 && !frame.PublicCatalog && !frame.VisitorStock
                        && frame.Service is 1 or 3
                        && (_manifestFrame?.TransactionActive ?? _sentManifest?.TransactionActive ?? false))
                    { _openingPending = true; _openingPages = 0; _openingStarted = double.NaN; }
                    if (!_coldPriority.TryGetValue(frame.Module, out ulong newest) || frame.Sequence > newest)
                        _coldPriority[frame.Module] = frame.Sequence;
                    if (!frame.PublicCatalog && !frame.VisitorStock)
                    {
                        queue.SupersedeTownOriginal(frame);
                    }
                }
                // Only a delta needs the exact old original inside a cold bundle.
                // Promoting it before a complete replacement doubled first-picture
                // traffic and replayed an obsolete hidden picture at every offer.
                if (frame.BaseSequence != 0 && _bundleBytes.TryGetValue(frame.Module, out byte[]? baselineBytes) && _promotedBundle.Add(frame.Module))
                    foreach (TownServiceFrame baseline in _bundleFrames)
                        if (baseline.Module == frame.Module && baseline.BaseSequence == 0 && baseline.Sequence == frame.BaseSequence)
                        { if (!_coldPriority.ContainsKey(frame.Module)) _coldPriority[frame.Module] = baseline.Sequence; queue.Enqueue(baselineBytes, baselineBytes.Length, baseline); break; }
            }
            else { _priority.Remove(frame.Module); _coldPriority.Remove(frame.Module); }
            // The retained full original repairs this exact already sent compact
            // identity. Keep it ahead of waiting dependent artwork revisions;
            // first/latest coalescing must not evict their only usable baseline.
            // Existing active pages still finish, with the same bounded pending
            // capacity and genuine newer-original supersession/removal rules.
            if (retainedNativeOriginal) queue.PrependTownOriginal(bytes, frame, preserveInFlight: true);
            else queue.Enqueue(bytes, length, frame);
        }
        SupersedeUrgentBundle();
    }

    private void SupersedeUrgentBundle()
    {
        if (!_urgentBundle.HasInFlight || _urgentBundleFrames.Count == 0) return;
        TownServiceFrame? census = _manifestFrame;
        if (census == null || !census.TransactionActive || census.PublicCatalog || census.VisitorStock) return;
        bool changed = false;
        var retain = new List<(TownServiceFrame Frame, byte[] Bytes)>();
        foreach (TownServiceFrame older in _urgentBundleFrames)
        {
            if (older.Session != census.Session || older.Service != census.Service
                || older.PublicCatalog != census.PublicCatalog || older.VisitorStock != census.VisitorStock
                || older.PublicClaim != census.PublicClaim) return;
            if (older.Module == TownServiceFrame.ManifestModule)
            {
                if (census.Sequence <= older.Sequence) return;
                // Hover can add/remove an original tooltip from the visible
                // census while the first card/list is fragmented. The newer
                // census already has its own immediate manifest path; restarting
                // unchanged originals here repeatedly discards received pages.
                // Only an actually superseded still-required original below justifies
                // replacing this still-useful immutable assembly.
                continue;
            }
            bool needed = Array.BinarySearch(census.Modules, older.Module) >= 0;
            _queues.TryGetValue(older.Module, out var queue);
            // A withdrawn hover partition is already absent from the current
            // census. Its old bytes cannot reveal it on the observer and do not
            // invalidate the useful card/list still being assembled beside it.
            if (!needed) continue;
            if (_latestOriginals.TryGetValue(older.Module, out var current)
                && ExtrasSendQueue.CanSupersedeTownRevision(older, current)
                && (older.BaseSequence != 0 || queue == null || !queue.HasTownDelta(older.Sequence, current.Sequence)))
            { changed = true; continue; }
            // An older cumulative delta without a replacement still owns its
            // exact baseline. Never turn cancellation into a missing dependency.
            if (older.BaseSequence != 0 || queue != null && queue.HasTownDelta(older.Sequence)) return;
            // An unchanged native row may still be required by the new card's
            // picture. Preserve its exact already encoded original in that new
            // atomic bundle instead of making its old fragment debt block it.
            if (queue == null || !_urgentBundleBytes.TryGetValue(older.Module, out var bytes)) return;
            retain.Add((older, bytes));
        }
        if (!changed) return;
        foreach (var item in retain) _queues[item.Frame.Module].PrependTownOriginal(item.Bytes, item.Frame);
        // Obsolete members and the old census are replaced; every unchanged
        // required original survives, and named delta dependencies forbid this
        // cancellation. The new fragment counter still exceeds the old writer.
        _urgentBundle.Clear(); _urgentBundleFrames.Clear(); _urgentBundleBytes.Clear();
        _openingPending = true; _openingPages = 0; _openingStarted = double.NaN;
    }
    internal byte[]? NextOpening(double now)
    {
        if (!_openingPending) return null;
        if (_coldPriority.Count == 0 && _requestedOriginals.Count == 0) return null;
        if (double.IsNaN(_openingStarted)) _openingStarted = now;
        if (_openingPages >= 16 || now - _openingStarted >= 1)
        { _openingPending = false; return null; }
        // Current complete originals do not depend on the old hidden bundle.
        // Its independent background assembly remains retained and still finishes.
        byte[]? page = Take(now, true);
        if (page != null) _openingPages++;
        return page;
    }
    internal byte[]? NextUrgent(double now)
    {
        // Both a first original and its current visible text/confirmation revision
        // may borrow a bounded early turn. Numeric pose/hover already has its own
        // lane. The global debt cap and this lane's background arbitration still
        // repay every borrowed page, so a sustained visit cannot starve the cabinet.
        bool waiting = _requestedOriginals.Count > 0 || _catalogRepairs.Count > 0
            || _priority.Count > 0 && (_urgentBundle.HasInFlight || _urgentBundle.HasPending);
        foreach (ushort id in _priority)
            if (_queues.TryGetValue(id, out var queue) && (queue.HasPending || queue.HasInFlight)) { waiting = true; break; }
        if (!waiting) return null;
        // Early turns borrow the existing town share rather than add bandwidth.
        // They must retain this lane's two-urgent/one-background arbitration too;
        // otherwise repaying every scheduled town turn would starve a cold rack.
        return Next(now);
    }
    private byte[]? NextManifest(double now)
    {
        if (_queues.TryGetValue(TownServiceFrame.ManifestModule, out ExtrasSendQueue? manifest))
        {
            if (!manifest.HasInFlight && _manifestFrame != null && _manifestBytes != null
                && (now >= _nextManifest || _sentManifest == null || !SameCensus(_sentManifest, _manifestFrame)))
            {
                manifest.Enqueue(_manifestBytes, _manifestBytes.Length, _manifestFrame);
                _sentManifest = _manifestFrame; _manifestBytes = null; _manifestFrame = null;
                _nextManifest = now + (_sentManifest.Visible ? 5 : .5);
            }
            byte[]? page = manifest.Next(now);
            if (page != null) return page;
        }
        return null;
    }
    internal byte[]? Next(double now)
    {
        PrepareOneCatalogRepair();
        // A cold offered picture owns its changed visible census atomically.
        // Sending that tiny census as a separate urgent page consumed one of
        // the same six borrowed turns and stranded the final original behind
        // the next global town turn. Empty/withdrawal and in-flight census
        // updates keep the immediate established manifest path.
        byte[]? census = BundleColdManifest ? null : NextManifest(now);
        if (census != null) return census;
        // Two urgent turns, then one background turn. A held face cannot wait behind
        // thousands of catalog rows; the full catalog cannot starve behind a held card.
        bool normalFirst = _priorityTurns >= 2;
        // A reference clock consumes an ordinary urgent town turn. The next
        // urgent turn after its complete assembly belongs to full repair;
        // every third still belongs to background.
        if (!normalFirst && !_clockRepairDue)
        {
            byte[]? clock = TakeClock(now);
            if (clock != null) { _priorityTurns++; return clock; }
        }
        byte[]? selected;
        if (normalFirst)
        {
            selected = Take(now, false);
            // An empty background consumes no arbitration debt. Otherwise the
            // counter stays above two forever and every fallback selects full
            // repair before the unfinished reference clock.
            _priorityTurns = 0;
            if (selected != null) return selected;
            if (!_clockRepairDue)
            {
                selected = TakeClock(now);
                if (selected != null) { _priorityTurns++; return selected; }
            }
        }
        selected = Take(now, true);
        if (selected != null) { _priorityTurns++; _clockRepairDue = false; return selected; }
        // With prepared-only clocks there may be no full root repair queued.
        // Giving repair its turn must not block later category clocks forever
        // behind an unrelated nonempty background original bank.
        _clockRepairDue = false;
        // An oversize full root uses this same module namespace directly. Finish
        // its interrupted clock before trying background; otherwise a nonempty
        // dormant bank could keep that assembly blocked indefinitely.
        selected = TakeClock(now, unfinishedOnly: true);
        if (selected != null) { _priorityTurns++; return selected; }
        selected = Take(now, false);
        if (selected != null) { _priorityTurns = 0; return selected; }
        selected = TakeClock(now);
        if (selected != null) _priorityTurns++;
        return selected;
    }
    private void PrepareOneCatalogRepair()
    {
        // One native member per actual send turn, never all page originals inside
        // the input/capture frame. The ordinary queues keep first-baseline and fair
        // urgent/background delivery semantics; periodic dormant repair remains.
        if (_catalogRepairs.Count == 0) return;
        ushort id = 0; TownServiceFrame? repair = null;
        foreach (var pair in _catalogRepairs) { id = pair.Key; repair = pair.Value; break; }
        _catalogRepairs.Remove(id);
        if (repair == null) return;
        byte[] packet = TownServiceDelivery.EncodeOriginal?.Invoke(repair) ?? TownServiceCodec.Write(repair);
        Enqueue(packet, packet.Length, repair);
    }
    private byte[]? TakeClock(double now, bool unfinishedOnly = false)
    {
        for (int i = 0; i < _order.Count; i++)
        {
            if (_clockCursor >= _order.Count) _clockCursor = 0;
            ushort id = _order[_clockCursor++];
            if (!_clocks.TryGetValue(id, out var clock) || !_queues.TryGetValue(id, out var complete) || complete.HasInFlight) continue;
            if (unfinishedOnly && !clock.HasInFlight) continue;
            if (!clock.HasInFlight) clock.AdvanceSequence(complete.Sequence);
            byte[]? page = clock.Next(now);
            if (page != null) { Completed(clock); _clockRepairDue = !clock.HasInFlight; return page; }
        }
        return null;
    }
    private byte[]? Take(double now, bool urgent)
    {
        // A peer has actually refused this exact original. Its finite existing
        // module queue must not wait for a new bundle of speculative full repairs.
        // Existing atomic bundles remain intact and resume at their next turn.
        if (urgent && TakeRequestedOriginal(now) is byte[] requested) return requested;
        ushort? active = urgent ? _priorityActive : _normalActive;
        if (active.HasValue && _queues.TryGetValue(active.Value, out ExtrasSendQueue? running) && running.HasInFlight)
        {
            byte[]? page = running.Next(now);
            if (page != null) { Completed(running); return page; }
            return null;
        }
        if (urgent) _priorityActive = null; else _normalActive = null;
        byte[]? batch = TakeBundle(now, urgent);
        ExtrasSendQueue bundleQueue = urgent ? _urgentBundle : _bundle;
        if (batch != null || bundleQueue.HasInFlight || bundleQueue.HasPending) return batch;
        for (int i = 0; i < _order.Count; i++)
        {
            int cursor = urgent ? _priorityCursor : _cursor;
            if (cursor >= _order.Count) cursor = 0;
            ushort id = _order[cursor++];
            if (urgent) _priorityCursor = cursor; else _cursor = cursor;
            if (id == TownServiceFrame.ManifestModule || _priority.Contains(id) != urgent) continue;
            if (id == (urgent ? _normalActive : _priorityActive)) continue;
            ExtrasSendQueue queue = _queues[id];
            if (_clocks.TryGetValue(id, out var clock))
            {
                if (clock.HasInFlight) continue;
                if (!queue.HasInFlight) queue.AdvanceSequence(clock.Sequence);
            }
            byte[]? page = queue.Next(now);
            if (page == null) continue;
            Completed(queue);
            if (urgent) _priorityActive = id; else _normalActive = id;
            return page;
        }
        return null;
    }
    private bool BundleColdManifest => _manifestFrame != null && _manifestBytes != null
        && _manifestFrame.TransactionActive && !_manifestFrame.PublicCatalog && !_manifestFrame.VisitorStock
        && _manifestFrame.Service is 1 or 3 && _coldPriority.Count > 0
        && !_urgentBundle.HasPending && !_urgentBundle.HasInFlight
        && (!_queues.TryGetValue(TownServiceFrame.ManifestModule, out var manifest) || !manifest.HasInFlight);

    private byte[]? TakeBundle(double now, bool urgent)
    {
        ExtrasSendQueue bundle = urgent ? _urgentBundle : _bundle;
        List<TownServiceFrame> frames = urgent ? _urgentBundleFrames : _bundleFrames;
        if (!bundle.HasInFlight && !bundle.HasPending)
        {
            var bytes = new List<byte[]>(); frames.Clear();
            var originalValues = new TownServiceCodec.OriginalValuePoolBuilder();
            if (!urgent) { _bundleBytes.Clear(); _promotedBundle.Clear(); }
            else _urgentBundleBytes.Clear();
            if (urgent && BundleColdManifest && originalValues.TryAdd(_manifestBytes!))
            {
                bytes.Add(_manifestBytes!); frames.Add(_manifestFrame!);
                _urgentBundleBytes[TownServiceFrame.ManifestModule] = _manifestBytes!;
                _sentManifest = _manifestFrame; _manifestBytes = null; _manifestFrame = null;
                _nextManifest = now + (_sentManifest!.Visible ? 5 : .5);
            }
            // Share exact native property values *before* the bounded snapshot is
            // assembled. Four12 KiB native enhancement rows used to fill this
            // batch, even when they all repeated the same original material/font
            // tables. A current64-module picture now owns one atomic value pool;
            // there is no earlier dictionary delivery or observer-default gate.
            for (int i=0; i<_order.Count && bytes.Count<TownServiceCodec.MaxOriginalValuePoolFrames; i++)
            {
                int cursor = urgent ? _priorityCursor : _cursor;
                if (cursor >= _order.Count) cursor = 0; ushort id = _order[cursor++];
                if (urgent) _priorityCursor = cursor; else _cursor = cursor;
                if (id == TownServiceFrame.ManifestModule || _priority.Contains(id) != urgent
                    || id == (urgent ? _normalActive : _priorityActive)) continue;
                ExtrasSendQueue queue=_queues[id];
                if (!queue.TryPeekPending(out byte[]? waiting, out _)) continue;
                if (!originalValues.TryAdd(waiting!))
                {
                    // Catalog banks/clock payloads keep the established grammar.
                    // If this is the first candidate, let ordinary Take deliver it;
                    // otherwise publish the bounded original picture already built.
                    if(urgent)_priorityCursor--;else _cursor--;break;
                }
                if(!queue.TryTakePending(out byte[]? packet,out object? identity))continue;
                bytes.Add(packet!);
                if(identity is TownServiceFrame frame)
                { frames.Add(frame); if (!urgent) _bundleBytes[id] = packet!; else _urgentBundleBytes[id] = packet!; }
            }
            if(bytes.Count==0)return null;
            byte[] container=originalValues.Write();
            bundle.Enqueue(container,container.Length);
        }
        byte[]? page=bundle.Next(now);
        if(page!=null&&!bundle.HasInFlight)
        { foreach(TownServiceFrame frame in frames) { CompleteCold(frame); TownServiceDelivery.Completed?.Invoke(frame); } frames.Clear();
          if(!urgent){_bundleBytes.Clear();_promotedBundle.Clear();} else _urgentBundleBytes.Clear(); }
        return page;
    }
    private void Completed(ExtrasSendQueue queue)
    {
        if (!queue.HasInFlight && queue.CompletedIdentity is TownServiceFrame frame)
        { CompleteCold(frame); TownServiceDelivery.Completed?.Invoke(frame); }
    }
    private void CompleteCold(TownServiceFrame frame)
    {
        if (frame.BaseSequence == 0 && _coldPriority.TryGetValue(frame.Module, out ulong newest)
            && newest == frame.Sequence) _coldPriority.Remove(frame.Module);
    }
    internal void RetireSources(bool publicCatalog, bool visitorStock)
    {
        if (!TownServiceDelivery.HasRetired(publicCatalog, visitorStock, _service, _session)) return;
        for (int i = _order.Count - 1; i >= 0; i--)
        {
            ushort id = _order[i];
            if (!TownServiceDelivery.IsRetired(publicCatalog, visitorStock, id)) continue;
            _sequences[id] = _queues[id].Sequence; _queues[id].Clear(); _queues.Remove(id); _order.RemoveAt(i);
            if (_clocks.TryGetValue(id, out var clock))
            { _sequences[id] = Math.Max(_sequences[id], clock.Sequence); clock.Clear(); _clocks.Remove(id); }
            _catalogBases.Remove(id); _catalogBaseKeys.Remove(id); _catalogQueuedKeys.Remove(id); _catalogRepairs.Remove(id);
            _catalogOriginals.Remove(id); _priority.Remove(id); _coldPriority.Remove(id); _latestOriginals.Remove(id);
            _requestedOriginals.Remove(id);
            _bundleBytes.Remove(id); _promotedBundle.Remove(id);
            if (_normalActive == id) _normalActive = null;
            if (_priorityActive == id) _priorityActive = null;
        }
        // Immutable active bundles finish in their existing namespace. Their
        // completion callback already refuses genuinely unregistered sources.
        // Removing current visibility alone never reaches this lifecycle path.
    }
    private static bool SameCensus(TownServiceFrame a, TownServiceFrame b)
    {
        if (a.VisitorStock != b.VisitorStock || a.PublicCatalog != b.PublicCatalog || a.PublicClaim != b.PublicClaim || a.Session != b.Session || a.Service != b.Service || a.Visible != b.Visible || a.TransactionActive != b.TransactionActive || a.Modules.Length != b.Modules.Length) return false;
        for (int i = 0; i < a.Modules.Length; i++) if (a.Modules[i] != b.Modules[i]) return false;
        if ((a.RequiredVisibleModules == null) != (b.RequiredVisibleModules == null)) return false;
        if (a.RequiredVisibleModules != null)
        { if (a.RequiredVisibleModules.Length != b.RequiredVisibleModules!.Length) return false;
          for (int i = 0; i < a.RequiredVisibleModules.Length; i++)
              if (a.RequiredVisibleModules[i] != b.RequiredVisibleModules[i]) return false; }
        return true;
    }
    internal void Clear()
    { _requestedOriginals.Clear(); foreach (var pair in _queues) { _sequences[pair.Key] = pair.Value.Sequence; pair.Value.Clear(); }
        foreach (var pair in _clocks) { _sequences[pair.Key] = Math.Max(_sequences.TryGetValue(pair.Key, out var sequence) ? sequence : 0, pair.Value.Sequence); pair.Value.Clear(); }
        _clocks.Clear(); _catalogOriginals.Clear(); _catalogBases.Clear(); _catalogBaseKeys.Clear(); _catalogQueuedKeys.Clear(); _catalogRepairs.Clear(); _clockCursor = 0; _clockRepairDue = false;
        _bundle.Clear(); _urgentBundle.Clear(); _bundleFrames.Clear(); _urgentBundleFrames.Clear(); _bundleBytes.Clear(); _urgentBundleBytes.Clear(); _promotedBundle.Clear(); _queues.Clear(); _order.Clear(); _priority.Clear(); _coldPriority.Clear(); _latestOriginals.Clear(); _cursor = _priorityCursor = _priorityTurns = 0;
        _normalActive = _priorityActive = null; _manifestBytes = null; _manifestFrame = _sentManifest = null; _nextManifest = 0;
        _openingPending = false; _openingPages = 0; _openingStarted = double.NaN; }
    internal static bool SameIdentity(TownServiceFrame a, TownServiceFrame b) => a.VisitorStock == b.VisitorStock && a.PublicCatalog == b.PublicCatalog && a.PublicClaim == b.PublicClaim && a.Session == b.Session
        && a.Service == b.Service && a.Module == b.Module && a.Template == b.Template && a.TemplateAddress == b.TemplateAddress && a.Structure == b.Structure
        && a.Visible == b.Visible && (a.BaseSequence == 0 ? a.Sequence : a.BaseSequence) == (b.BaseSequence == 0 ? b.Sequence : b.BaseSequence);
}

/// <summary>Private service, public cabinet and visitor stock retain independent module queues. All still
/// consume the same bounded global presentation budget; fragment sequence namespaces cannot
/// collide even when all lanes use the same module IDs and session number.</summary>
internal sealed class TownServiceSendQueue
{
    private readonly TownServiceLaneSendQueue _private, _public, _stock;
    private readonly ExtrasSendQueue _voice, _stockVoice;
    private readonly Queue<(byte[] Bytes, TownServiceFrame Frame)> _voicePending = new(), _stockVoicePending = new();
    private uint _voiceSession, _stockVoiceSession;
    private bool _stockVoiceTurn;
    private byte _voiceService;
    private int _laneTurn;
    internal TownServiceSendQueue(ulong seed)
    { _private = new TownServiceLaneSendQueue(seed & ~(131071UL | TownServiceFragments.StockLaneMarker));
      _public = new TownServiceLaneSendQueue((seed & ~(131071UL | TownServiceFragments.StockLaneMarker)) | 65536UL);
      _stock = new TownServiceLaneSendQueue((seed & ~(131071UL | TownServiceFragments.StockLaneMarker)) | TownServiceFragments.StockLaneMarker);
      _voice = new ExtrasSendQueue((seed & ~(131071UL | TownServiceFragments.StockLaneMarker)) | TownServiceFrame.VoiceModule,
          TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
          snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
          counterMask: TownServiceFragments.StockLaneMarker - 1);
      _stockVoice = new ExtrasSendQueue((seed & ~(131071UL | TownServiceFragments.StockLaneMarker)) | TownServiceFrame.VoiceModule,
          TownServiceCodec.MessageType, TownServiceCodec.FragmentType,
          snapshotLimit: TownServiceFrame.MaxBytes, sequenceStride: 131072,
          counterMask: TownServiceFragments.StockLaneMarker - 1, fixedMarker: TownServiceFragments.StockLaneMarker); }
    internal void Enqueue(byte[] bytes, int length, TownServiceFrame frame)
    {
        ApplyRetirements();
        if (!frame.PublicCatalog && !frame.VisitorStock && (_voiceSession != frame.Session || _voiceService != frame.Service))
        { _voice.Clear(); _voicePending.Clear(); _voiceSession = frame.Session; _voiceService = frame.Service; }
        if (frame.Module == TownServiceFrame.VoiceModule)
        {
            if (frame.PublicCatalog || frame.TemplateAddress != TownServiceFrame.VoiceAddress
                || frame.Session == 0 || frame.Sequence == 0 || frame.BaseSequence != 0) return;
            Queue<(byte[] Bytes, TownServiceFrame Frame)> voicePending = frame.VisitorStock ? _stockVoicePending : _voicePending;
            if (frame.VisitorStock && _stockVoiceSession != frame.Session)
            { _stockVoice.Clear(); _stockVoicePending.Clear(); _stockVoiceSession = frame.Session; }
            if (voicePending.Count >= 16) return;
            byte[] copy = new byte[length]; Buffer.BlockCopy(bytes, 0, copy, 0, length);
            voicePending.Enqueue((copy, frame));
            return;
        }
        (frame.VisitorStock ? _stock : frame.PublicCatalog ? _public : _private).Enqueue(bytes, length, frame);
    }
    internal byte[]? Next(double now)
    {
        ApplyRetirements();
        if (!_voice.HasPending && !_voice.HasInFlight && _voicePending.Count > 0)
        {
            var eventPacket = _voicePending.Dequeue();
            _voice.Enqueue(eventPacket.Bytes, eventPacket.Bytes.Length);
        }
        if (!_stockVoice.HasPending && !_stockVoice.HasInFlight && _stockVoicePending.Count > 0)
        {
            var eventPacket = _stockVoicePending.Dequeue();
            _stockVoice.Enqueue(eventPacket.Bytes, eventPacket.Bytes.Length);
        }
        _stockVoiceTurn = !_stockVoiceTurn;
        byte[]? voice = _stockVoiceTurn ? _stockVoice.Next(now) ?? _voice.Next(now)
            : _voice.Next(now) ?? _stockVoice.Next(now);
        if (voice != null) return voice;
        _laneTurn = (_laneTurn + 1) % 3;
        return _laneTurn == 0 ? _private.Next(now) ?? _public.Next(now) ?? _stock.Next(now)
            : _laneTurn == 1 ? _public.Next(now) ?? _stock.Next(now) ?? _private.Next(now)
            : _stock.Next(now) ?? _private.Next(now) ?? _public.Next(now);
    }
    // Cold visible originals get a bounded direct turn in the existing global
    // scheduler. The public, private and stock lanes retain equal arbitration;
    // this never bypasses their manifests or starts a separate send clock.
    internal byte[]? NextOpening(double now)
    { ApplyRetirements(); return _private.NextOpening(now); }
    internal byte[]? NextUrgent(double now)
    {
        ApplyRetirements();
        _laneTurn = (_laneTurn + 1) % 3;
        return _laneTurn == 0 ? _private.NextUrgent(now) ?? _public.NextUrgent(now) ?? _stock.NextUrgent(now)
            : _laneTurn == 1 ? _public.NextUrgent(now) ?? _stock.NextUrgent(now) ?? _private.NextUrgent(now)
            : _stock.NextUrgent(now) ?? _private.NextUrgent(now) ?? _public.NextUrgent(now);
    }
    internal void Clear()
    { _private.Clear(); _public.Clear(); _stock.Clear(); _voice.Clear(); _stockVoice.Clear();
        _voicePending.Clear(); _stockVoicePending.Clear(); _voiceSession = _stockVoiceSession = 0;
        _voiceService = 0; _stockVoiceTurn = false; TownServiceDelivery.ClearRetired(); }
    private void ApplyRetirements()
    {
        _private.RetireSources(false, false); _public.RetireSources(true, false); _stock.RetireSources(false, true);
        TownServiceDelivery.ClearRetired();
    }
    internal static bool SameIdentity(TownServiceFrame a, TownServiceFrame b) => TownServiceLaneSendQueue.SameIdentity(a,b);
}
