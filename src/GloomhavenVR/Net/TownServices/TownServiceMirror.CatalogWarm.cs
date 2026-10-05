using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class CatalogOriginal
    {
        internal TownServiceFrame Frame = null!;
        internal GameObject Template = null!;
        internal ulong Key;
        internal ulong FirstSequence;
        internal ulong LastUse;
    }
    private sealed class CatalogOriginalBank
    {
        internal byte Service;
        internal uint Session, Claim;
        internal ulong Touch;
        internal readonly Dictionary<ushort, List<CatalogOriginal>> Originals = new();
    }
    // Canonical native snapshots are independent of the current page's clones,
    // manifest membership and renderer gates. No dormant host is constructed here.
    private static readonly Dictionary<int, CatalogOriginalBank> CatalogOriginalBanks = new();
    private static int _catalogPreparationCursor;
    private static float _nextCatalogPreparation;

    private static void CaptureDormantCatalogOriginals(Action<byte[], int, object?> send, float now)
    {
        if (!ReferenceEquals(_local, PublicLane) || !HasPreparedLocalCatalogBank || now < _nextCatalogPreparation) return;
        int visited = 0, emitted = 0;
        while (visited++ < _local.CaptureOrder.Count && emitted < TownServiceCodec.MaxBundleFrames)
        {
            if (_catalogPreparationCursor >= _local.CaptureOrder.Count)
            { _catalogPreparationCursor = 0; _nextCatalogPreparation = now + 5f; break; }
            LocalModule source = _local.CaptureOrder[_catalogPreparationCursor++];
            if (!source.CatalogResident || source.CatalogDirty || source.Last == null
                || source.Last.RackMember == null || source.Last.RackMember.Detached) continue;
            TownServiceFrame original = TownServiceDelta.Retain(source.Last);
            original.BaseSequence = 0; original.PublicCatalog = true; original.VisitorStock = false;
            original.Service = _service; original.Session = _session; original.PublicClaim = _publicClaim;
            original.HighPriority = false;
            byte[] packet = TownServiceCodec.Write(original);
            send(packet, packet.Length, original); emitted++;
        }
        // Local completion only releases sender storage. Repeated original rounds
        // repair loss and also prepare peers which connected after the first round.
        if (_nextCatalogPreparation <= now) _nextCatalogPreparation = now + .05f;
    }

    private static bool CatalogScopeMatches(CatalogOriginalBank bank, TownServiceFrame frame) =>
        bank.Service == frame.Service && bank.Session == frame.Session && bank.Claim == frame.PublicClaim;

    private static CatalogOriginalBank? CatalogCache(int peer, TownServiceFrame frame, bool create)
    {
        if (CatalogOriginalBanks.TryGetValue(peer, out var bank))
        {
            if (CatalogScopeMatches(bank, frame)) return bank;
            // A delayed claim cannot replace the current owner's prepared originals.
            if (frame.PublicClaim <= bank.Claim) return null;
            // The higher claim explicitly reauthorizes every exact original by
            // its key. Keep content only for this actual sender's same session.
            if (bank.Service == frame.Service && bank.Session == frame.Session) return bank;
            if (!create) return null;
        }
        if (!create || CatalogOriginalBanks.Count >= 8 && !CatalogOriginalBanks.ContainsKey(peer)) return null;
        bank = new CatalogOriginalBank { Service = frame.Service, Session = frame.Session, Claim = frame.PublicClaim };
        CatalogOriginalBanks[peer] = bank; return bank;
    }

    private static void CacheCatalogOriginal(int peer, TownServiceFrame frame)
    {
        if (peer >= 0 || !frame.PublicCatalog || frame.VisitorStock || frame.Service != 1 || frame.BaseSequence != 0
            || frame.RackMember == null || frame.RackMember.Detached || frame.CatalogBank != null) return;
        try
        {
            CatalogOriginalBinding(frame).Validate(frame, Assets);
            CatalogOriginalBank? bank = CatalogCache(peer, frame, true);
            if (bank == null || !MakeCatalogCacheRoom(peer, bank, frame.Module)) return;
            bank.Claim = frame.PublicClaim;
            ulong key = TownCatalogBank.ContentKey(frame);
            if (!bank.Originals.TryGetValue(frame.Module, out var revisions)) bank.Originals.Add(frame.Module, revisions = new List<CatalogOriginal>(3));
            CatalogOriginal? previous = revisions.Find(original => original.Key == key);
            if (previous != null)
            { previous.LastUse = ++bank.Touch; previous.FirstSequence = Math.Min(previous.FirstSequence, frame.Sequence); if (previous.Frame.Sequence >= frame.Sequence) return; revisions.Remove(previous); }
            revisions.Add(new CatalogOriginal { Frame = frame, FirstSequence = previous?.FirstSequence ?? frame.Sequence,
                Template = Templates[TemplateKey(frame.Service, frame.Template, frame.TemplateAddress)],
                Key = key, LastUse = ++bank.Touch });
            if (revisions.Count > 3)
            {
                int first = 0; for (int i = 1; i < revisions.Count; i++) if (revisions[i].FirstSequence < revisions[first].FirstSequence) first = i;
                revisions.RemoveAt(first == 0 ? 1 : 0);
            }
        }
        catch (Exception error) { Report("catalog preparation original", error); }
    }

    private static bool MakeCatalogCacheRoom(int peer, CatalogOriginalBank bank, ushort id)
    {
        if (bank.Originals.ContainsKey(id) || bank.Originals.Count < TownServiceFrame.MaxModules) return true;
        ushort oldest = 0; ulong oldestUse = ulong.MaxValue; bool found = false;
        foreach (var pair in bank.Originals)
        {
            if (CatalogOriginalRequired(peer, bank, pair.Key)) continue;
            ulong used = 0; foreach (CatalogOriginal revision in pair.Value) used = Math.Max(used, revision.LastUse);
            if (!found || used < oldestUse) { oldest = pair.Key; oldestUse = used; found = true; }
        }
        if (!found) return false;
        bank.Originals.Remove(oldest); return true;
    }

    private static bool CatalogOriginalRequired(int peer, CatalogOriginalBank bank, ushort id)
    {
        if (Pending.TryGetValue(peer, out var pending) && pending.TryGetValue(id, out var frame)
            && frame.Service == bank.Service && frame.Session == bank.Session) return true;
        if (Remote.TryGetValue(peer, out var remote) && remote.TryGetValue(id, out var module) && module.Alive
            && module.LastFrame?.Service == bank.Service && module.LastFrame.Session == bank.Session) return true;
        if (ReceivedCatalogBanks.TryGetValue(peer, out var roots))
            foreach (TownServiceFrame root in roots.Values)
                if (root.Service == bank.Service && root.Session == bank.Session && root.CatalogBank != null
                    && (pending != null && pending.ContainsKey(root.Module)
                        || remote != null && remote.TryGetValue(root.Module, out var housing) && housing.Alive
                        || Sessions.TryGetValue(peer, out var session) && session.Service == bank.Service && session.Session == bank.Session
                            && Array.BinarySearch(session.Modules, root.Module) >= 0))
                    foreach (TownCatalogBankMember member in root.CatalogBank.Members) if (member.Id == id) return true;
        // Dormant cached frames survive ordinary visibility changes. Capacity
        // reclaim only discards an unused oldest value; missed references still
        // require exact originals and recover through the retained full bank.
        return false;
    }

    private static bool CompleteCatalogBank(int peer, TownServiceFrame root, TownCatalogBank bank, out TownCatalogBank complete)
    {
        complete = bank;
        CatalogOriginalBank? cache = CatalogCache(peer, root, false);
        var provided = new Dictionary<ushort, TownServiceFrame>();
        foreach (TownServiceFrame update in bank.Updates) provided.Add(update.Module, update);
        var updates = new TownServiceFrame[bank.Members.Length];
        for (int i = 0; i < bank.Members.Length; i++)
        {
            TownCatalogBankMember member = bank.Members[i];
            if (provided.TryGetValue(member.Id, out var update)) { updates[i] = update; continue; }
            if (bank.Headers.Length != bank.Members.Length || cache == null || !cache.Originals.TryGetValue(member.Id, out var revisions)) return false;
            CatalogOriginal? original = revisions.Find(candidate => candidate.Key == member.ContentKey)
                ?? revisions.Find(candidate => candidate.Key == bank.HeaderBaseKeys[i] && bank.HeaderBaseKeys[i] != 0);
            if (original == null
                || !Templates.TryGetValue(TemplateKey(root.Service, original.Frame.Template, original.Frame.TemplateAddress), out var template)
                || template == null || !ReferenceEquals(template, original.Template)) return false;
            TownServiceFrame retained = TownServiceDelta.Retain(bank.Headers[i]);
            // Every dynamic header is authored by the newly admitted clock. Only
            // exact original node values are reused from the same sender/session.
            if (original.Key == member.ContentKey)
            { retained.BaseSequence = 0; retained.Nodes = original.Frame.Nodes; }
            else
            {
                TownServiceFrame baseline = TownServiceDelta.Retain(retained); baseline.BaseSequence = 0;
                baseline.Sequence = retained.BaseSequence; baseline.Nodes = original.Frame.Nodes;
                TownServiceFrame? expanded = TownServiceDelta.Expand(baseline, retained);
                if (expanded == null) return false;
                retained = expanded;
            }
            updates[i] = retained;
        }
        complete = new TownCatalogBank { Prepared = bank.Prepared, Members = bank.Members, Updates = updates };
        TownServiceFrame validation = TownServiceDelta.Retain(root); validation.CatalogBank = complete;
        try { complete.Validate(validation); }
        catch (InvalidDataException) { return false; }
        return ValidateCatalogBankOriginals(peer, validation, complete);
    }
}
