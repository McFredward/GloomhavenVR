using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ClockStone;

namespace GloomhavenVR.Core;

/// <summary>
/// Owns only the concrete audio objects produced by one native map flow's moving item.
/// The game's encounter leg leaves its loop running, then its continuation starts that same
/// item again. Replace the previous request before allowing the next native request to play.
/// This is a producer lifetime repair: unrelated audio, including another producer using the
/// same item, remains untouched, and every layer of the new native request is still played.
/// </summary>
internal static class NativeTravelLoop
{
    private sealed class Owner
    {
        internal readonly List<Receipt> Objects = new();
    }

    private sealed class Generation
    {
        internal long Value;
    }

    private sealed class Receipt
    {
        internal readonly AudioObject Object;
        internal readonly AudioSubItem SubItem;
        internal readonly double Started;
        internal readonly string Item;
        private readonly Generation _generation;
        private readonly long _version;

        internal Receipt(AudioObject source, string item, Generation generation)
        {
            Object = source;
            SubItem = source.subItem;
            Started = source.startedPlayingAtTime;
            Item = item;
            _generation = generation;
            _version = generation.Value;
        }

        internal bool StillOwned => Object != null && Object.gameObject.activeInHierarchy
            && _generation.Value == _version
            && ReferenceEquals(Object.subItem, SubItem) && Object.startedPlayingAtTime == Started
            && string.Equals(Object.audioID, Item, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class Capture
    {
        private readonly Owner _owner;
        internal readonly string Item;
        internal readonly Capture? Previous;

        private Capture(Owner owner, string item, Capture? previous)
        {
            _owner = owner;
            Item = item;
            Previous = previous;
        }

        private void Remember(AudioObject source, Generation generation)
        {
            for (int i = 0; i < _owner.Objects.Count; i++)
                if (ReferenceEquals(_owner.Objects[i].Object, source))
                {
                    // Item-mode native subitems may return the same leaf through multiple
                    // nested calls. Keep the receipt of this request's final native generation.
                    _owner.Objects[i] = new Receipt(source, Item, generation);
                    return;
                }
            _owner.Objects.Add(new Receipt(source, Item, generation));
        }

        internal bool Observe(AudioSubItem? item, AudioObject source)
        {
            if (item?.item == null || !string.Equals(item.item.Name, Item, StringComparison.OrdinalIgnoreCase)) return false;
            Generation generation = Generations.GetValue(source, _ => new Generation());
            generation.Value++;
            Remember(source, generation);
            return true;
        }

        internal static Capture Start(object producer, string item, Capture? previous)
        {
            Owner owner = Owners.GetValue(producer, _ => new Owner());
            int stopped = StopOwned(owner);
            if (stopped > 0)
                VRLog.Info("MapRoom", $"MAP TRAVEL AUDIO replaced {stopped} owned source(s) before the next native leg; item='{item}', flow={producer.GetType().Name}.");
            return new Capture(owner, item, previous);
        }
    }

    private static readonly ConditionalWeakTable<object, Owner> Owners = new();
    private static readonly ConditionalWeakTable<AudioObject, Generation> Generations = new();
    private static Capture? _capture;
    private static int _failures;

    /// <summary>
    /// A native producer runs synchronously through PlaySound/PlaySeqSounds, including the
    /// latter's DSP-scheduled loop. Capture at PlayAudioSubItem, where all native layers exist;
    /// do not query only IsPlaying, which is not ownership and can omit a pending request.
    /// </summary>
    internal static Capture? Begin(object producer, string? item)
    {
        Capture? previous = _capture;
        try
        {
            _capture = string.IsNullOrEmpty(item) ? null : Capture.Start(producer, item!, previous);
        }
        catch (Exception ex)
        {
            _capture = null;
            Failure(ex);
        }
        return previous;
    }

    internal static void End(Capture? previous) => _capture = previous;

    internal static void Observe(AudioSubItem? item, AudioObject? source)
    {
        try
        {
            if (source == null) return;
            // ClockStone's timestamp advances once per Update. A pooled same-item replay
            // in the same frame can retain both that timestamp and the same AudioSubItem.
            // Invalidate tracked receipts at every real reuse, including outside travel;
            // unrelated objects incur only a weak-table lookup and allocate no entry.
            if (_capture?.Observe(item, source) != true
                && Generations.TryGetValue(source, out Generation generation)) generation.Value++;
        }
        catch (Exception ex) { Failure(ex); }
    }

    /// <summary>Also cancel a scheduled owned loop if the flow is destroyed after room teardown.</summary>
    internal static void Release(object producer)
    {
        if (!Owners.TryGetValue(producer, out Owner owner)) return;
        StopOwned(owner);
        Owners.Remove(producer);
    }

    private static int StopOwned(Owner owner)
    {
        int stopped = 0;
        for (int i = 0; i < owner.Objects.Count; i++)
        {
            try
            {
                Receipt receipt = owner.Objects[i];
                if (!receipt.StillOwned) continue;
                // Stop(0) cancels a DSP-scheduled start as well. Fading the old source here
                // would retain the reported overlap while the original starts its new loop.
                receipt.Object.Stop(0f);
                stopped++;
            }
            catch (Exception ex) { Failure(ex); }
        }
        owner.Objects.Clear();
        return stopped;
    }

    private static void Failure(Exception ex)
    {
        if (_failures++ < 3)
            VRLog.Alert("MapRoom", $"MAP TRAVEL AUDIO ownership repair failed: {ex.GetType().Name}: {ex.Message}. Native travel continues unchanged.");
    }
}
