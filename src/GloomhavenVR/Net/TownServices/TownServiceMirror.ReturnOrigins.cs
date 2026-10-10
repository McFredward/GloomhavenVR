using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private const int MaxReturnOriginsPerPeer = 128;
    private sealed class ReturnOriginal
    {
        internal TownCardReturnOrigin Origin = null!;
        internal RemoteModule Module = null!;
        internal float SeenAt, RetiredAt;
        internal float SourceTime;
        internal ulong SourceSequence;
        internal string SourceAddress = string.Empty;
        internal bool Retired, Transferred;
    }
    private static readonly Dictionary<int, Dictionary<TownCardReturnOrigin, ReturnOriginal>> ReturnOriginals = new();
    private sealed class ReturnOriginFloor
    { internal TownCardReturnOrigin Origin = null!; internal string Address = string.Empty; internal ulong Sequence; }
    private static readonly Dictionary<int, Dictionary<ushort, ReturnOriginFloor>> ReturnOriginFloors = new();
    private static readonly Dictionary<int, float> ClosedReturnStock = new();
    private static readonly List<TownCardReturnOrigin> DeadReturnOrigins = new();

    // Both lanes publish the same actual owner part. Address/model matching is
    // insufficient: only a reference-identical private source can declare origin.
    private static void CaptureReturnOrigin(LocalModule module, TownServiceFrame frame)
    {
        frame.ReturnOrigin = null;
        if (_sharedFrame == null || frame.PublicCatalog || (!frame.VisitorStock && frame.Service != 3)) return;
        CardReturnReference? returning = CardReturn(module.Binding.Root);
        if (returning == null || returning.Sample.Target is not VRCard) return;
        bool flying = returning.Sample(module.Binding.Root, _sharedFrame, returning.Hand, out uint revision, out _);
        LocalModule? offered = null;
        foreach (LocalModule candidate in PrivateLane.Modules.Values)
            if (PrivateLane.Service == 3 && PrivateLane.Active && ReferenceEquals(candidate.Binding.Root, module.Binding.Root))
            { offered = candidate; break; }
        if (offered != null)
        {
            TownCardReturnOrigin? prior = offered.ReturnOrigin;
            if (!flying)
                frame.ReturnOrigin = new TownCardReturnOrigin(3, PrivateLane.Session, offered.Id,
                    offered.Binding.Structure, 0, revision);
            else if (prior != null && prior.FlightRevision == revision)
                frame.ReturnOrigin = prior;
        }
        else if (frame.VisitorStock && module.ReturnOrigin is TownCardReturnOrigin prior
            && prior.FlightRevision == revision && (flying || returning.Sample.Target is VRCard card && card.Holder == null))
            frame.ReturnOrigin = prior;
        module.ReturnOrigin = frame.ReturnOrigin;
    }
    private static void ObserveReturnOriginEpoch(RemoteModule module, TownServiceFrame frame)
    {
        if (!TownCardReturnOrigin.Same(module.LastFrame?.ReturnOrigin, frame.ReturnOrigin))
            module.ReturnOriginChangedAt = frame.SampleTime;
    }
    private static bool SuppressTransferredOriginal(int peer, TownServiceFrame frame)
    {
        if (peer <= 0) return false;
        if (ReturnOriginFloors.TryGetValue(peer, out var floors) && floors.TryGetValue(frame.Module, out var floor)
            && floor.Origin.Matches(frame) && floor.Address == frame.TemplateAddress
            && frame.Sequence <= floor.Sequence && (frame.ReturnOrigin == null || TownCardReturnOrigin.Same(frame.ReturnOrigin, floor.Origin))) return true;
        if (!ReturnOriginals.TryGetValue(peer, out var origins)) return false;
        if (frame.ReturnOrigin != null)
            return origins.TryGetValue(frame.ReturnOrigin, out ReturnOriginal? original) && original.Transferred;
        // An entire origin-bearing private revision was already validated before
        // transfer. An older initial baseline cannot shed that provenance by
        // arriving late; a later revision still follows ordinary validation.
        foreach (ReturnOriginal original in origins.Values)
            if (original.Transferred && original.Origin.Matches(frame)
                && frame.TemplateAddress == original.SourceAddress && frame.Sequence <= original.SourceSequence) return true;
        return false;
    }

    private static void ObserveReturnOriginal(int peer, RemoteModule module)
    {
        TownServiceFrame? frame = module.LastFrame;
        if (peer <= 0 || frame?.ReturnOrigin == null || !frame.ReturnOrigin.Matches(frame)
            || !frame.Visible || !module.Alive || !module.Host.activeSelf) return;
        if (!ReturnOriginals.TryGetValue(peer, out var originals))
        { originals = new Dictionary<TownCardReturnOrigin, ReturnOriginal>(); ReturnOriginals.Add(peer, originals); }
        DeadReturnOrigins.Clear();
        foreach (var pair in originals)
            if (!TownCardReturnOrigin.Same(pair.Key, frame.ReturnOrigin)
                && (ReferenceEquals(pair.Value.Module, module) || pair.Key.Matches(frame)))
            {
                if (pair.Value.Retired && !pair.Value.Transferred) pair.Value.Module.Dispose();
                DeadReturnOrigins.Add(pair.Key);
            }
        foreach (TownCardReturnOrigin obsolete in DeadReturnOrigins) originals.Remove(obsolete);
        if (originals.TryGetValue(frame.ReturnOrigin, out ReturnOriginal? existing))
        { if (!existing.Transferred) {
            if (existing.Retired && !ReferenceEquals(existing.Module, module)) existing.Module.Dispose();
            existing.Module = module; existing.SeenAt = Time.unscaledTime; existing.SourceTime = frame.SampleTime;
            existing.SourceSequence = frame.Sequence; existing.SourceAddress = frame.TemplateAddress; existing.Retired = false;
          } return; }
        if (originals.Count >= MaxReturnOriginsPerPeer) return;
        originals.Add(frame.ReturnOrigin, new ReturnOriginal { Origin = frame.ReturnOrigin,
            Module = module, SeenAt = Time.unscaledTime, SourceTime = frame.SampleTime, SourceSequence = frame.Sequence, SourceAddress = frame.TemplateAddress });
    }

    // A source-side census/close can precede the first stock datagram. Keep only
    // the previously rendered, preannounced card, detached before its holder dies.
    // Disconnect/stale/reset never enter this path, and the hold is strictly bounded.
    private static bool RetainReturnOriginal(int peer, RemoteModule module)
    {
        TownServiceFrame? frame = module.LastFrame;
        if (peer <= 0 || frame?.ReturnOrigin == null || !frame.ReturnOrigin.Matches(frame)
            || !module.Alive || !module.Host.activeSelf || !ReturnOriginals.TryGetValue(peer, out var origins)
            || !origins.TryGetValue(frame.ReturnOrigin, out ReturnOriginal? original)
            || original.Transferred || !ReferenceEquals(original.Module, module)
            || ClosedReturnStock.TryGetValue(peer, out float closed) && closed >= frame.SampleTime) return false;
        Transform? shared = SharedFrameForRemote?.Invoke(peer);
        if (shared == null) return false;
        DetachOfferedOriginal(module, shared);
        original.Retired = true; original.RetiredAt = Time.unscaledTime;
        return true;
    }
    private static void TickReturnOriginals(float now)
    {
        foreach (var peer in ReturnOriginals)
        {
            DeadReturnOrigins.Clear();
            foreach (var pair in peer.Value)
            {
                ReturnOriginal original = pair.Value;
                bool obsolete = !original.Transferred && !original.Retired
                    && (original.Module.LastFrame == null || !original.Origin.Matches(original.Module.LastFrame)
                        || !TownCardReturnOrigin.Same(original.Origin, original.Module.LastFrame.ReturnOrigin));
                if (obsolete || !original.Transferred && !original.Module.Alive
                    || original.Retired && now - original.RetiredAt > NetProtocol.StaleTimeoutSeconds)
                {
                    if (original.Retired && !original.Transferred) original.Module.Dispose();
                    DeadReturnOrigins.Add(pair.Key);
                }
                else if (original.Retired && !original.Transferred)
                { original.Module.Motion.Tick(now); original.Module.Binding.TickAnimation(now); }
            }
            foreach (TownCardReturnOrigin origin in DeadReturnOrigins) peer.Value.Remove(origin);
        }
    }
    private static void ClearReturnOriginals(int peer)
    {
        if (ReturnOriginals.TryGetValue(peer, out var originals))
            foreach (ReturnOriginal original in originals.Values)
                if (original.Retired && !original.Transferred) original.Module.Dispose();
        ReturnOriginals.Remove(peer); ReturnOriginFloors.Remove(peer); ClosedReturnStock.Remove(peer);
    }
    private static void RetireAcknowledgedOrigins(int peer, TownServiceFrame census)
    {
        if (peer <= 0) return;
        if (ReturnOriginFloors.TryGetValue(peer, out var previousFloors))
        {
            var obsolete = new List<ushort>();
            foreach (var floor in previousFloors)
                if (floor.Value.Origin.Session != census.Session || floor.Value.Origin.Service != census.Service) obsolete.Add(floor.Key);
            foreach (ushort id in obsolete) previousFloors.Remove(id);
        }
        if (!ReturnOriginals.TryGetValue(peer, out var originals)) return;
        DeadReturnOrigins.Clear();
        foreach (var pair in originals)
            if (census.SampleTime >= pair.Value.SourceTime
                && (census.Session != pair.Key.Session || census.Service != pair.Key.Service
                    || !census.Visible || Array.BinarySearch(census.Modules, pair.Key.Module) < 0))
            {
                if (census.Session == pair.Key.Session && census.Service == pair.Key.Service)
                {
                    if (!ReturnOriginFloors.TryGetValue(peer, out var floors))
                    { floors = new Dictionary<ushort, ReturnOriginFloor>(); ReturnOriginFloors.Add(peer, floors); }
                    if (floors.Count >= MaxReturnOriginsPerPeer && !floors.ContainsKey(pair.Key.Module))
                    { ushort oldest = 0; ulong sequence = ulong.MaxValue;
                      foreach (var floor in floors) if (floor.Value.Sequence < sequence) { oldest = floor.Key; sequence = floor.Value.Sequence; }
                      floors.Remove(oldest); }
                    floors[pair.Key.Module] = new ReturnOriginFloor { Origin = pair.Key,
                        Address = pair.Value.SourceAddress, Sequence = census.Sequence };
                }
                if (pair.Value.Transferred) DeadReturnOrigins.Add(pair.Key);
            }
        foreach (TownCardReturnOrigin origin in DeadReturnOrigins) originals.Remove(origin);
    }
    private static void WithdrawReturnStock(int owner, float sampleTime)
    {
        ClosedReturnStock[owner] = sampleTime;
        if (!ReturnOriginals.TryGetValue(owner, out var originals)) return;
        DeadReturnOrigins.Clear();
        foreach (var pair in originals)
            if (pair.Value.Retired && !pair.Value.Transferred
                && pair.Value.Module.LastFrame != null && pair.Value.Module.LastFrame.SampleTime <= sampleTime)
            { pair.Value.Module.Dispose(); DeadReturnOrigins.Add(pair.Key); }
        foreach (TownCardReturnOrigin origin in DeadReturnOrigins) originals.Remove(origin);
    }

    // Validate the complete destination instant before rekeying any actual source
    // module. A cold observer with no rendered offered original uses its prepared
    // stock clone; it cannot invent an earlier object identity.
    private static bool AdoptReturnOriginals(int owner, TownServiceMotionEntry header,
        Dictionary<ushort, RemoteModule> destination)
    {
        if (header.Lane != 2 || header.Service != 1 || !ReturnOriginals.TryGetValue(owner, out var originals)) return true;
        var adoptions = new List<(ushort Id, RemoteModule Stock, ReturnOriginal Original)>();
        foreach (ushort member in header.ReturnMembers)
        {
            RemoteModule stock = destination[member]; TownServiceFrame frame = stock.LastFrame!;
            TownCardReturnOrigin? origin = frame.ReturnOrigin;
            if (origin == null)
            {
                // A legacy/preparation baseline can arrive before the additive
                // origin-bearing revision. A known rendered offered source is
                // evidence to WAIT, never authority to adopt by address alone.
                foreach (ReturnOriginal possible in originals.Values)
                    if (!possible.Transferred && possible.Module.Alive
                        && possible.Origin.FlightRevision == header.Revision
                        && possible.Module.Address == stock.Address
                        && possible.Module.Binding.Structure == stock.Binding.Structure) return false;
                continue;
            }
            if (!originals.TryGetValue(origin, out ReturnOriginal? original))
            {
                // The new private preparation can beat its matching Stock
                // header/cancellation. Its exact source identity supersedes an
                // older epoch; missing old provenance is not a cold observer.
                foreach (ReturnOriginal current in originals.Values)
                    if (!current.Transferred && current.Module.Alive && current.Module.LastFrame != null
                        && origin.Matches(current.Module.LastFrame)
                        && current.Module.Address == stock.Address
                        && !TownCardReturnOrigin.Same(current.Origin, origin)) return false;
                continue;
            }
            if (original.Transferred) continue;
            RemoteModule source = original.Module; TownServiceFrame? prior = source.LastFrame;
            if (origin.FlightRevision != header.Revision || !source.Alive || prior == null
                || !origin.Matches(prior) || !TownCardReturnOrigin.Same(prior.ReturnOrigin, origin)
                || source.Address != stock.Address || source.Binding.Structure != stock.Binding.Structure
                || (source.AddedCanvas != null) != (stock.AddedCanvas != null)) return false;
            source.Binding.Validate(frame, Assets);
            adoptions.Add((member, stock, original));
        }
        Transform? shared = SharedFrameForRemote?.Invoke(owner);
        if (adoptions.Count != 0 && shared == null) return false;
        // Detach every part first: private parent retirement cannot destroy a
        // sibling/child that is also becoming this exact stock return cohort.
        foreach (var adoption in adoptions) DetachOfferedOriginal(adoption.Original.Module, shared!);
        foreach (var adoption in adoptions)
        {
            ReturnOriginal original = adoption.Original; RemoteModule source = original.Module;
            TownServiceFrame frame = adoption.Stock.LastFrame!;
            if (Remote.TryGetValue(owner, out var offered) && offered.TryGetValue(original.Origin.Module, out var current)
                && ReferenceEquals(current, source)) offered.Remove(original.Origin.Module);
            source.Motion.Reset(); MotionRemoteFrames.Remove(source); ReturnPictures.Remove(source);
            source.Binding.Apply(frame, Assets);
            source.Session = frame.Session; source.Template = frame.Template; source.Address = frame.TemplateAddress;
            source.Sequence = adoption.Stock.Sequence; source.LastFrame = frame;
            adoption.Stock.Dispose(); destination[adoption.Id] = source;
            original.Transferred = true; original.Retired = false; original.SeenAt = Time.unscaledTime;
        }
        return true;
    }
}
