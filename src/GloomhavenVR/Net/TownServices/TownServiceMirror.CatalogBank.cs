using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class LocalCatalogBank
    {
        internal bool Prepared;
        internal uint Turn, Claim, Session;
        internal TownCatalogBank? Last;
    }
    private static readonly Dictionary<ushort, LocalCatalogBank> LocalCatalogBanks = new();
    private static readonly Dictionary<int, Dictionary<ushort, TownServiceFrame>> ReceivedCatalogBanks = new();

    internal static void SetCatalogSource(ushort module, uint revision, bool dormant)
    {
        if (!ReferenceEquals(_local, PublicLane) || !Local.TryGetValue(module, out LocalModule? source)) return;
        if (!source.CatalogResident || source.CatalogRevision != revision) source.CatalogDirty = true;
        source.CatalogResident = true; source.CatalogRevision = revision; source.CatalogDormant = dormant;
    }
    internal static void SetCatalogDormant(ushort module, bool dormant)
    { if (Local.TryGetValue(module, out LocalModule? source) && source.CatalogResident) source.CatalogDormant = dormant; }
    internal static void SetCatalogBankPrepared(ushort rack, bool prepared)
    {
        if (!ReferenceEquals(_local, PublicLane)) return;
        if (!LocalCatalogBanks.TryGetValue(rack, out LocalCatalogBank? bank))
            LocalCatalogBanks.Add(rack, bank = new LocalCatalogBank());
        bank.Prepared = prepared;
    }
    internal static bool HasPreparedLocalCatalogBank
    {
        get
        {
            foreach (LocalModule module in PublicLane.Modules.Values)
                if (module.CatalogResident && (module.Last == null || module.CatalogDirty)) return false;
            return PublicLane.Modules.Count != 0;
        }
    }
    private static void ClearLocalCatalogBanks()
    { if (ReferenceEquals(_local, PublicLane)) LocalCatalogBanks.Clear(); }

    private static TownCatalogBank? CaptureCatalogBank(LocalModule root, TownRackState rack)
    {
        if (!ReferenceEquals(_local, PublicLane) || !LocalCatalogBanks.TryGetValue(root.Id, out LocalCatalogBank? cache)) return null;
        var refs = new TownCatalogBankMember[rack.Members.Length];
        bool prepared = true;
        for (int i = 0; i < rack.Members.Length; i++)
        {
            TownRackMember member = rack.Members[i];
            if (member.Detached || !Local.TryGetValue(member.Id, out LocalModule? source) || !source.CatalogResident || source.CatalogDirty || source.Last == null)
            { prepared = false; break; }
            refs[i] = new TownCatalogBankMember(member.Id, source.CatalogContentKey != 0
                ? source.CatalogContentKey : TownCatalogBank.ContentKey(source.Last));
        }
        // No speculative reference can authorize an original which has never been read.
        if (!prepared) return null;
        if (cache.Last != null && cache.Last.Prepared == cache.Prepared && cache.Turn == rack.Turn && cache.Claim == _publicClaim && cache.Session == _session && SameBankMembers(cache.Last.Members, refs)) return cache.Last;
        var updates = new TownServiceFrame[refs.Length];
        for (int i = 0; i < refs.Length; i++)
        {
            TownServiceFrame frame = TownServiceDelta.Retain(Local[refs[i].Id].Last!);
            frame.BaseSequence = 0; frame.Sequence = NextSequence(); frame.PublicClaim = _publicClaim;
            frame.PublicCatalog = true; frame.VisitorStock = false;
            TownRackMember member = rack.Members[i];
            frame.RackMember = frame.RackMember!.Copy(); frame.RackMember.Rack = root.Id;
            frame.RackMember.Page = member.Page; frame.RackMember.Turn = rack.Turn; frame.RackMember.Detached = false;
            updates[i] = frame;
        }
        // Keep complete immutable original updates on every heartbeat for this bank. Local
        // transport completion is not a remote acknowledgement; loss cannot expose a clock
        // without its genuinely changed original properties.
        cache.Turn = rack.Turn; cache.Claim = _publicClaim; cache.Session = _session;
        return cache.Last = new TownCatalogBank { Prepared = cache.Prepared, Members = refs, Updates = updates };
    }
    private static bool AdvertiseCatalogModule(LocalModule module)
    {
        if (!module.CatalogResident || !module.CatalogDormant) return true;
        foreach (TownRackState rack in LocalRacks.Values)
            foreach (TownRackMember member in rack.Members) if (member.Id == module.Id) return true;
        return false;
    }
    private static bool SameBankMembers(TownCatalogBankMember[] a, TownCatalogBankMember[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i].Id != b[i].Id || a[i].ContentKey != b[i].ContentKey) return false;
        return true;
    }
    private static bool SameCatalogBank(TownCatalogBank? a, TownCatalogBank? b) => ReferenceEquals(a, b)
        || a != null && b != null && a.Prepared == b.Prepared && SameBankMembers(a.Members, b.Members);

    private static readonly Dictionary<string, (GameObject Template, TownServiceBinding Binding)> CatalogTemplateBindings = new();
    private static TownServiceBinding CatalogOriginalBinding(TownServiceFrame frame)
    {
        string key = TemplateKey(frame.Service, frame.Template, frame.TemplateAddress);
        if (!Templates.TryGetValue(key, out GameObject? template) || template == null)
        { ResolveTemplate?.Invoke(frame.Service, frame.Template, frame.TemplateAddress); Templates.TryGetValue(key, out template); }
        if (template == null) throw new InvalidDataException("Prepared original cabinet template is absent.");
        if (!CatalogTemplateBindings.TryGetValue(key, out var original) || !ReferenceEquals(original.Template, template))
        {
            original.Binding?.Dispose();
            original = (template, new TownServiceBinding(template.transform)); CatalogTemplateBindings[key] = original;
        }
        return original.Binding;
    }
    private static bool ValidateCatalogBankOriginals(int peer, TownServiceFrame root, TownCatalogBank bank)
    {
        try
        {
            CatalogOriginalBinding(root).Validate(root, Assets);
            foreach (TownServiceFrame update in bank.Updates)
            {
                TownServiceBinding original = CatalogOriginalBinding(update);
                original.Validate(update, Assets);
                if (update.ParentModule != TownServiceFrame.ManifestModule)
                {
                    TownServiceFrame? parent = update.ParentModule == root.Module ? root : null;
                    foreach (TownServiceFrame candidate in bank.Updates)
                        if (candidate.Module == update.ParentModule) { parent = candidate; break; }
                    if (parent == null && Remote.TryGetValue(peer, out var standing)
                        && standing.TryGetValue(update.ParentModule, out var retained)
                        && retained.Alive && retained.LastFrame?.Session == root.Session
                        && retained.LastFrame.PublicClaim == root.PublicClaim)
                        foreach (TownCatalogBankMember member in bank.Members)
                            if (member.Id == update.ParentModule && member.ContentKey == retained.CatalogContentKey)
                            { parent = retained.LastFrame; break; }
                    if (parent == null || Array.IndexOf(CatalogOriginalBinding(parent).Bindings, update.ParentBinding) < 0)
                        throw new InvalidDataException("Prepared original cabinet parent binding is absent.");
                }
                // All native asset references and original topology are checked before any
                // child or clock enters pending. No per-member incomplete target can leak.
            }
            return true;
        }
        catch (Exception error) { Report("catalog bank original dependencies", error); return false; }
    }
    private static bool AdmitCatalogBank(int peer, TownServiceFrame root)
    {
        TownCatalogBank? bank = root.CatalogBank;
        if (bank == null) return true;
        if (!root.PublicCatalog || root.VisitorStock || root.Rack == null || peer >= 0) return false;
        try { bank.Validate(root); } catch (InvalidDataException) { return false; }
        if (!ValidateCatalogBankOriginals(peer, root, bank)) return false;
        if (Pending.TryGetValue(peer, out var existing) && existing.TryGetValue(root.Module, out var previous)
            && previous.Sequence >= root.Sequence) return true;
        if (!Pending.TryGetValue(peer, out var pending))
        { if (Pending.Count >= 24) return false; Pending.Add(peer, pending = new Dictionary<ushort, TownServiceFrame>()); }
        int additional = 0;
        foreach (TownServiceFrame update in bank.Updates) if (!pending.ContainsKey(update.Module)) additional++;
        if (pending.Count + additional + (pending.ContainsKey(root.Module) ? 0 : 1) > TownServiceFrame.MaxModules) return false;
        if (!ReceivedBaselines.TryGetValue(peer, out var baselines))
            ReceivedBaselines.Add(peer, baselines = new Dictionary<ushort, TownServiceFrame>());
        // Parsing and full membership/content validation completed before the first write.
        // Root and required canonical snapshots enter the same pending picture atomically.
        foreach (TownServiceFrame update in bank.Updates)
        {
            if (!baselines.TryGetValue(update.Module, out var before) || update.Sequence > before.Sequence) baselines[update.Module] = update;
            if (!pending.TryGetValue(update.Module, out var beforePending) || update.Sequence > beforePending.Sequence) pending[update.Module] = update;
        }
        if (!ReceivedCatalogBanks.TryGetValue(peer, out var banks))
            ReceivedCatalogBanks.Add(peer, banks = new Dictionary<ushort, TownServiceFrame>());
        banks[root.Module] = root;
        return true;
    }
    private static bool MatchesPreparedCatalogBank(int peer, ushort rack, TownRackState state, ushort id, RemoteModule module)
    {
        if (!ReceivedCatalogBanks.TryGetValue(peer, out var banks) || !banks.TryGetValue(rack, out var root)
            || root.Rack?.Turn != state.Turn || root.CatalogBank?.Prepared != true || module.LastFrame == null
            || module.LastFrame.Session != root.Session || module.LastFrame.PublicClaim != root.PublicClaim
            || !Templates.TryGetValue(TemplateKey(root.Service, module.Template, module.Address), out GameObject? template) || template == null)
            return false;
        foreach (TownCatalogBankMember member in root.CatalogBank.Members)
            if (member.Id == id) return member.ContentKey == module.CatalogContentKey;
        return false;
    }
    private static readonly Dictionary<long, (TownServiceFrame Frame, ulong Key)> IncomingCatalogKeys = new();
    private static ulong IncomingCatalogContentKey(int peer, TownServiceFrame frame)
    {
        long id = ((long)peer << 16) | frame.Module;
        // Expanded cumulative frames may be recreated while waiting for a parent.
        // Their immutable received packet is the cache identity, not reusable peer/
        // sequence numbers or a newly allocated expansion on each render tick.
        TownServiceFrame identity = Pending.TryGetValue(peer, out var pending)
            && pending.TryGetValue(frame.Module, out var received) && received.Sequence == frame.Sequence ? received : frame;
        if (IncomingCatalogKeys.TryGetValue(id, out var known) && ReferenceEquals(known.Frame, identity)) return known.Key;
        ulong key = TownCatalogBank.ContentKey(frame);
        if (IncomingCatalogKeys.Count >= 24 * TownServiceFrame.MaxModules) IncomingCatalogKeys.Clear();
        IncomingCatalogKeys[id] = (identity, key);
        return key;
    }
    private static void ClearCatalogPeer(int peer)
    {
        var removed = new List<long>();
        foreach (long key in IncomingCatalogKeys.Keys) if ((key >> 16) == peer) removed.Add(key);
        foreach (long key in removed) IncomingCatalogKeys.Remove(key);
    }
    private static readonly List<KeyValuePair<ushort, TownServiceFrame>> OrderedCatalogPending = new();
    private static readonly Dictionary<ushort, int> CatalogParentDepths = new();
    private static List<KeyValuePair<ushort, TownServiceFrame>> OrderCatalogPending(Dictionary<ushort, TownServiceFrame> pending)
    {
        OrderedCatalogPending.Clear(); CatalogParentDepths.Clear();
        foreach (var pair in pending) OrderedCatalogPending.Add(pair);
        int Depth(ushort id)
        {
            if (CatalogParentDepths.TryGetValue(id, out int depth)) return depth;
            var seen = new HashSet<ushort>(); ushort current = id; depth = 0;
            while (pending.TryGetValue(current, out var frame) && frame.ParentModule != TownServiceFrame.ManifestModule
                && seen.Add(current) && depth < TownServiceFrame.MaxModules)
            { depth++; current = frame.ParentModule; }
            return CatalogParentDepths[id] = depth;
        }
        OrderedCatalogPending.Sort((a, b) => { int order = Depth(a.Key).CompareTo(Depth(b.Key)); return order != 0 ? order : a.Key.CompareTo(b.Key); });
        return OrderedCatalogPending;
    }
    private static bool AtomicCatalogOriginal(int peer, TownServiceFrame frame)
    {
        if (frame.RackMember == null || !ReceivedCatalogBanks.TryGetValue(peer, out var banks)
            || !banks.TryGetValue(frame.RackMember.Rack, out var root) || root.CatalogBank?.Prepared != true
            || root.Session != frame.Session || root.PublicClaim != frame.PublicClaim || root.Rack!.Turn != frame.RackMember.Turn) return false;
        foreach (TownCatalogBankMember member in root.CatalogBank.Members)
            if (member.Id == frame.Module) return member.ContentKey == IncomingCatalogContentKey(peer, frame);
        return false;
    }
    private static bool IncomingCatalogOriginalMatches(int peer, TownServiceFrame frame)
    {
        if (frame.RackMember == null || frame.RackMember.Detached
            || !ReceivedCatalogBanks.TryGetValue(peer, out var banks)
            || !banks.TryGetValue(frame.RackMember.Rack, out var root) || root.CatalogBank?.Prepared != true) return true;
        foreach (TownCatalogBankMember member in root.CatalogBank.Members)
            if (member.Id == frame.Module) return member.ContentKey == IncomingCatalogContentKey(peer, frame);
        return true; // A dormant bank original is not an exposed member of this clock.
    }
    internal static bool HasReadyPublicBank => HasReadyPublicPresentation;
    private static void HideDormantCatalogOriginal(int peer, TownServiceFrame frame, RemoteModule module)
    {
        if (peer >= 0 || frame.RackMember == null || frame.RackMember.Detached
            || !RemoteRacks.TryGetValue(peer, out var racks) || !racks.TryGetValue(frame.RackMember.Rack, out var rack)
            || rack.GatedMembers.Contains(frame.Module)) return;
        module.Host.GetComponent<CanvasGroup>().alpha = 0f;
        if (frame.TemplateAddress.StartsWith("merchant.cardbody|", StringComparison.Ordinal))
            foreach (Renderer renderer in module.RackBodyRenderers ??= module.Binding.Root.GetComponentsInChildren<Renderer>(true))
                if (renderer != null) renderer.forceRenderingOff = true;
    }
}
