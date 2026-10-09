using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class ReturnCohortAssembly
    {
        internal TownServiceMotionEntry Header = null!;
        internal CardReturnClock Clock = null!;
        internal TownServiceReturnPart?[] Parts = null!;
        internal ulong[] Sequences = null!;
        internal ulong Sequence;
        internal float ReceivedAt, Offset;
        internal bool Activated, HasPrior;
    }
    private static readonly List<TownServiceMotionKey> DeadReturnCohorts = new();
    private sealed class ReturnPicture
    {
        internal Vector3 HostPosition, HostScale, RootPosition, RootScale;
        internal Quaternion HostRotation, RootRotation;
        internal bool Visible;
        internal float Alpha;
        internal readonly int Owner;
        internal readonly TownServiceFrame Identity;
        internal ReturnPicture? Authored;
        internal ReturnPicture(int owner, RemoteModule module)
        {
            Owner = owner; Identity = module.LastFrame!;
            Transform host = module.Host.transform, root = module.Binding.Root;
            HostPosition = host.position; HostRotation = host.rotation; HostScale = host.lossyScale;
            RootPosition = root.position; RootRotation = root.rotation; RootScale = root.lossyScale;
            Visible = module.LastFrame != null && module.Host.activeSelf;
            Alpha = module.Host.GetComponent<CanvasGroup>().alpha;
        }
        internal void Restore(RemoteModule module)
        {
            RestorePose(module.Host.transform, HostPosition, HostRotation, HostScale);
            RestorePose(module.Binding.Root, RootPosition, RootRotation, RootScale);
            module.Host.GetComponent<CanvasGroup>().alpha = Alpha;
            module.Host.SetActive(Visible);
        }
        private static void RestorePose(Transform root, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Vector3 parent = root.parent != null ? root.parent.lossyScale : Vector3.one;
            if (!ValidMotionScale(parent)) return;
            root.SetPositionAndRotation(position, rotation); root.localScale = DivideMotionScale(scale, parent);
        }
    }
    private static readonly Dictionary<RemoteModule, ReturnPicture> ReturnPictures = new();
    private static readonly List<RemoteModule> DeadReturnPictures = new();

    private static bool SameReturnMembers(TownServiceMotionEntry first, TownServiceMotionEntry second)
    {
        if (first.Lane != second.Lane || first.Service != second.Service || first.Session != second.Session
            || first.PublicClaim != second.PublicClaim || first.Hand != second.Hand || first.Revision != second.Revision
            || first.ReturnMembers.Length != second.ReturnMembers.Length) return false;
        for (int i = 0; i < first.ReturnMembers.Length; i++)
            if (first.ReturnMembers[i] != second.ReturnMembers[i] || first.ReturnStructures[i] != second.ReturnStructures[i]) return false;
        return true;
    }
    private static void ReceiveReturnCohort(int owner, PeerMotion peer, TownServiceMotionPacket packet, TownServiceMotionEntry entry)
    {
        if (entry.ReturnSampleTime > packet.SampleTime) return;
        foreach (ushort member in entry.ReturnMembers)
            if (peer.Slots.TryGetValue(new TownServiceMotionKey(1, entry.Lane, member, 0, 0, 0), out MotionSlot? current)
                && current.Entry.Session == entry.Session && current.Entry.Service == entry.Service
                && current.Entry.PublicClaim == entry.PublicClaim && current.SampleTime >= entry.ReturnSampleTime
                && current.ReceivedSequence >= packet.Sequence) return;

        float now = Time.unscaledTime;
        if (!peer.ReturnCohorts.TryGetValue(entry.Key, out ReturnCohortAssembly? assembly)
            || entry.ReturnSampleTime > assembly.Header.ReturnSampleTime)
        {
            if (assembly == null && peer.ReturnCohorts.Count >= TownServiceFrame.MaxModules) return;
            bool retained = assembly != null && SameReturnMembers(assembly.Header, entry);
            assembly = new ReturnCohortAssembly { Header = entry,
                Parts = new TownServiceReturnPart?[entry.ReturnMembers.Length], Sequences = new ulong[entry.ReturnMembers.Length],
                ReceivedAt = now, Offset = retained ? assembly!.Offset : ReturnOffset(peer, entry.ReturnSampleTime, now),
                HasPrior = retained && (assembly!.Activated || assembly.HasPrior) };
            CardReturnClock? priorClock = retained ? peer.ReturnCohorts[entry.Key].Clock : null;
            assembly.Clock = priorClock ?? new CardReturnClock(entry, entry.ReturnSampleTime, assembly.Offset);
            if (priorClock != null) priorClock.Observe(entry, entry.ReturnSampleTime, now);
            peer.ReturnCohorts[entry.Key] = assembly;
        }
        else if (entry.ReturnSampleTime < assembly.Header.ReturnSampleTime || !SameReturnMembers(assembly.Header, entry)
            || !SameNumbers(assembly.Header.Numbers, entry.Numbers)) return;
        foreach (TownServiceReturnPart part in entry.ReturnParts)
        {
            if (packet.Sequence <= assembly.Sequences[part.Index]) continue;
            assembly.Parts[part.Index] = part; assembly.Sequences[part.Index] = packet.Sequence;
        }
        assembly.Sequence = Math.Max(assembly.Sequence, packet.Sequence);
        // Originals can be cold while the complete clock arrives. Save their
        // previous picture before incoming headers or numeric roots can move them.
        if (!assembly.HasPrior && TryReturnModules(owner, entry, out Dictionary<ushort, RemoteModule>? modules, out bool obsolete) && !obsolete)
            foreach (ushort member in entry.ReturnMembers)
                if (modules!.TryGetValue(member, out RemoteModule? module) && module.Alive)
                    HoldReturnPicture(owner, module);
    }
    // Ordinary native roots are published only after this source sampler ends
    // (including regrab/hide). Their newer exact receipt cancels the physical
    // cohort, so partial loss or an older flight never owns a held original.
    private static void CancelReturnCohorts(int owner, PeerMotion peer, TownServiceMotionPacket packet, TownServiceMotionEntry root)
    {
        if (root.Kind != 1) return;
        DeadReturnCohorts.Clear();
        foreach (var pair in peer.ReturnCohorts)
        {
            TownServiceMotionEntry header = pair.Value.Header;
            if (root.Lane != header.Lane || root.Service != header.Service || root.Session != header.Session
                || root.PublicClaim != header.PublicClaim || packet.Sequence <= pair.Value.Sequence
                || packet.SampleTime < header.ReturnSampleTime) continue;
            int index = Array.BinarySearch(header.ReturnMembers, root.Module);
            if (index < 0 || header.ReturnStructures[index] != root.Structure) continue;
            for (int i = 0; i < header.ReturnMembers.Length; i++)
                peer.Slots.Remove(new TownServiceMotionKey(8, header.Lane, header.ReturnMembers[i], 0, 0, 0));
            foreach (var picture in ReturnPictures)
                if (picture.Value.Owner == owner && picture.Key.LastFrame != null
                    && picture.Key.LastFrame.Session == header.Session && picture.Key.LastFrame.Service == header.Service
                    && picture.Key.LastFrame.PublicClaim == header.PublicClaim
                    && Array.BinarySearch(header.ReturnMembers, picture.Key.LastFrame.Module) >= 0) DeadReturnPictures.Add(picture.Key);
            DeadReturnCohorts.Add(pair.Key);
        }
        foreach (RemoteModule module in DeadReturnPictures) ReturnPictures.Remove(module);
        DeadReturnPictures.Clear();
        foreach (TownServiceMotionKey key in DeadReturnCohorts) peer.ReturnCohorts.Remove(key);
    }
    private static bool TryReturnModules(int owner, TownServiceMotionEntry entry,
        out Dictionary<ushort, RemoteModule>? modules, out bool obsolete)
    {
        int key = owner; obsolete = false; modules = null;
        if (entry.Lane == 2 && !TryStockPeerKey(owner, out key)) return false;
        if (!Sessions.TryGetValue(key, out TownServiceSessionInfo? session)) return false;
        if (!session.Active || session.Session != entry.Session || session.Service != entry.Service
            || session.PublicClaim != entry.PublicClaim) { obsolete = session.SampleTime > entry.ReturnSampleTime; return false; }
        foreach (ushort member in entry.ReturnMembers)
            if (Array.BinarySearch(session.Modules, member) < 0) { obsolete = session.SampleTime > entry.ReturnSampleTime; return false; }
        return Remote.TryGetValue(key, out modules);
    }
    private static void ActivateReturnCohorts(float now)
    {
        foreach (var peer in MotionPeers)
        {
            DeadReturnCohorts.Clear();
            foreach (var pair in peer.Value.ReturnCohorts)
            {
                ReturnCohortAssembly assembly = pair.Value; TownServiceMotionEntry header = assembly.Header;
                assembly.Clock.Current(now, out float sourceAge);
                if (now - assembly.ReceivedAt > NetProtocol.StaleTimeoutSeconds
                    || sourceAge > header.Numbers[1] + .25f)
                { DeadReturnCohorts.Add(pair.Key); continue; }
                bool found = TryReturnModules(peer.Key, header, out var modules, out bool obsolete);
                if (obsolete) { DeadReturnCohorts.Add(pair.Key); continue; }
                if (assembly.Activated || !found) continue;
                bool ready = true;
                for (int i = 0; i < header.ReturnMembers.Length; i++)
                    if (assembly.Parts[i] == null || !modules!.TryGetValue(header.ReturnMembers[i], out RemoteModule? module)
                        || !module.Alive || module.LastFrame == null || module.LastFrame.Structure != header.ReturnStructures[i])
                    { ready = false; break; }
                if (!ready) continue;
                var packet = new TownServiceMotionPacket { Sequence = assembly.Sequence, SampleTime = header.ReturnSampleTime };
                for (int i = 0; i < header.ReturnMembers.Length; i++)
                {
                    TownServiceReturnPart part = assembly.Parts[i]!;
                    var clock = new TownServiceMotionEntry { Kind = 8, Lane = header.Lane, Service = header.Service,
                        Session = header.Session, PublicClaim = header.PublicClaim, Module = header.ReturnMembers[i],
                        Structure = header.ReturnStructures[i], Hand = header.Hand, Revision = header.Revision,
                        Numbers = new float[38], HasReturnVisibility = true, Visible = part.Visible,
                        ParentAlpha = part.ParentAlpha, CohortOffset = assembly.Offset };
                    Array.Copy(header.Numbers, clock.Numbers, 28); Array.Copy(part.Child, 0, clock.Numbers, 28, 10);
                    packet.Entries.Add(clock);
                    ReturnPictures.Remove(modules![header.ReturnMembers[i]]);
                }
                assembly.Activated = true; ReceiveMotion(peer.Key, packet);
            }
            foreach (TownServiceMotionKey key in DeadReturnCohorts) peer.Value.ReturnCohorts.Remove(key);
        }
    }
    private static bool PendingReturnModule(int owner, RemoteModule module)
    {
        if (!MotionPeers.TryGetValue(owner, out PeerMotion? peer) || module.LastFrame == null) return false;
        foreach (ReturnCohortAssembly assembly in peer.ReturnCohorts.Values)
            if (!assembly.Activated && !assembly.HasPrior
                && assembly.Header.Lane == (module.LastFrame.VisitorStock ? 2 : module.LastFrame.PublicCatalog ? 1 : 0)
                && assembly.Header.Session == module.LastFrame.Session
                && assembly.Header.Service == module.LastFrame.Service && assembly.Header.PublicClaim == module.LastFrame.PublicClaim)
            {
                int index = Array.BinarySearch(assembly.Header.ReturnMembers, module.LastFrame.Module);
                if (index >= 0 && assembly.Header.ReturnStructures[index] == module.LastFrame.Structure) return true;
            }
        return false;
    }
    private static void HoldReturnPicture(int owner, RemoteModule module)
    { if (!ReturnPictures.ContainsKey(module)) ReturnPictures.Add(module, new ReturnPicture(owner, module)); }

    // Only an exact received native return cohort can stage original headers.
    // A hidden/revealed stock card without that receipt follows ordinary rendering.
    private static bool HoldIncomingReturnPicture(int key, RemoteModule module, TownServiceFrame frame)
    {
        int owner = RealPeer(key);
        bool same = module.LastFrame != null && frame.Session == module.LastFrame.Session
            && frame.Service == module.LastFrame.Service && frame.Structure == module.LastFrame.Structure
            && frame.PublicClaim == module.LastFrame.PublicClaim && frame.Visible;
        if (!same) { ReturnPictures.Remove(module); return false; }
        bool pending = PendingReturnModule(owner, module);
        if (pending) HoldReturnPicture(owner, module);
        return pending;
    }
    private static void RestoreIncomingReturnPicture(RemoteModule module, bool held)
    {
        if (!held || !ReturnPictures.TryGetValue(module, out ReturnPicture? picture)) return;
        picture.Authored = new ReturnPicture(picture.Owner, module);
        picture.Restore(module);
    }
    private static void RestorePendingReturnPictures()
    {
        DeadReturnPictures.Clear();
        foreach (var pair in ReturnPictures)
        {
            RemoteModule module = pair.Key; ReturnPicture picture = pair.Value;
            TownServiceFrame? frame = module.LastFrame;
            if (!module.Alive || frame == null || frame.Session != picture.Identity.Session
                || frame.Service != picture.Identity.Service || frame.Structure != picture.Identity.Structure
                || frame.PublicClaim != picture.Identity.PublicClaim || !frame.Visible)
            { DeadReturnPictures.Add(module); continue; }
            if (PendingReturnModule(picture.Owner, module)) picture.Restore(module);
            else
            {
                // Expiry/loss releases the latest authored native picture, rather
                // than leaving a staged hidden root permanently parked.
                picture.Authored?.Restore(module); DeadReturnPictures.Add(module);
            }
        }
        foreach (RemoteModule module in DeadReturnPictures) ReturnPictures.Remove(module);
    }
    private static void ResetReturnCohorts()
    { ReturnPictures.Clear(); DeadReturnPictures.Clear(); DeadReturnCohorts.Clear(); }
}
