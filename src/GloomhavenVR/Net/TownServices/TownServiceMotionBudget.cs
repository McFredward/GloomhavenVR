using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>One latest numeric entry, with the same no-catch-up budget as the rig lane.</summary>
internal class TownServiceMotionPending
{
    internal TownServiceMotionEntry Entry = null!;
    internal bool Dirty = true;
    internal float SentAt = float.NegativeInfinity;
    internal uint AdmittedReturnRevision;
    internal bool ReturnLayout = false;
    internal TownServiceMotionBudget.ReturnSnapshot? ReturnSnapshot;
}

/// <summary>Live controls and held targets get a finite turn before cold fan heartbeats.
/// A complete cold record retains a turn under full contention.</summary>
internal static class TownServiceMotionBudget
{
    private static readonly List<TownServiceMotionPending> NoVisibleFan = new();
    private static int _returnRecoveryTurn;
    private readonly struct Selected
    {
        internal readonly TownServiceMotionPending Slot;
        internal readonly int Group, Next, Bundle;
        internal Selected(TownServiceMotionPending slot, int group, int next, int bundle = 0)
        { Slot = slot; Group = group; Next = next; Bundle = bundle; }
    }

    /// <summary>Pack current numeric samples losslessly into the unchanged event budget.
    /// Fair ordered turns survive compression backpressure: unsent slots retain both
    /// their dirty flag and their last-send clock. No dropped record is marked sent.</summary>
    internal static byte[] FillPacked(TownServiceMotionPacket packet, List<TownServiceMotionPending> live,
        List<TownServiceMotionPending> visibleFan, List<TownServiceMotionPending> ordinary,
        ref int liveCursor, ref int visibleCursor, ref int ordinaryCursor, float now)
    {
        var groups = new[] { live, visibleFan, ordinary };
        var cursors = new[] { liveCursor, visibleCursor, ordinaryCursor };
        var visited = new int[3];
        var selected = new List<Selected>(TownServiceMotionCodec.MaxExpandedEntries);
        var seen = new HashSet<TownServiceMotionPending>();
        int initial = packet.Entries.Count, size = 21;
        foreach (TownServiceMotionEntry entry in packet.Entries) size += TownServiceMotionCodec.EntryBytes(entry);
        bool nativeReturn = live.Exists(slot => slot.Entry.Kind == 8);
        if (nativeReturn)
        {
            // Reserve one finite ordinary turn before an incompressible cohort.
            // Rotate all three categories even while the same physical return
            // continuously republishes. Empty categories lend their turn.
            int first = _returnRecoveryTurn; _returnRecoveryTurn = (first + 1) % 3;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int group = (first + attempt) % 3; List<TownServiceMotionPending> waiting = groups[group];
                TownServiceMotionPending? oldest = null; int next = 0;
                for (int i = 0; i < waiting.Count; i++)
                {
                    int index = (cursors[group] + i) % waiting.Count; TownServiceMotionPending slot = waiting[index];
                    if (slot.SentAt == now || slot.Entry.Kind == 8
                        || slot.ReturnLayout && HasNativeReturn(live, slot.Entry)
                        || slot.Entry.Kind == 1 && HasNativeReturn(live, slot.Entry)) continue;
                    // A cohort advances the same live cursor. Use the existing
                    // per-original last-send clock so that cannot repeatedly
                    // reserve only the first ordinary live module.
                    if (oldest == null || slot.SentAt < oldest.SentAt) { oldest = slot; next = index + 1; }
                }
                if (oldest == null) continue;
                packet.Entries.Add(oldest.Entry);
                if (TownServiceMotionCodec.TryWritePacked(packet) == null)
                { packet.Entries.RemoveAt(packet.Entries.Count - 1); continue; }
                seen.Add(oldest); selected.Add(new Selected(oldest, group, next));
                size += TownServiceMotionCodec.EntryBytes(oldest.Entry); break;
            }
        }
        // One physical card can contain thirteen or more native print partitions.
        // Repeating its common 28-float root receipt per module both split the
        // first picture and could exceed this event forever. TLV113 retains that
        // exact receipt once plus the original ten-float child geometry. Large
        // cohorts make bounded subset progress against one frozen source instant;
        // the receiver exposes them together only after every original is ready.
        int bundles = 0;
        for (int i = 0; i < live.Count && bundles < 2; i++)
        {
            int index = (liveCursor + i) % live.Count;
            TownServiceMotionPending clock = live[index];
            if (seen.Contains(clock) || clock.Entry.Kind != 8 || clock.SentAt == now || !clock.Dirty) continue;
            ReturnSnapshot? snapshot = clock.ReturnSnapshot;
            if (snapshot == null || !snapshot.Matches(clock.Entry, live)) snapshot = CaptureReturn(live, clock.Entry, now);
            if (snapshot == null) continue;
            foreach (TownServiceMotionPending part in snapshot.Sources)
            { seen.Add(part); int root = ReturnRoot(live, part.Entry); if (root >= 0) seen.Add(live[root]); }
            int cohortAt = packet.Entries.Count;
            TownServiceMotionEntry cohort = AddReturnSubset(packet, snapshot, snapshot.Parts.Length - snapshot.Cursor);
            while (TownServiceMotionCodec.TryWritePacked(packet) == null && cohort.ReturnParts.Length > 1)
            {
                packet.Entries.RemoveRange(cohortAt, packet.Entries.Count - cohortAt);
                cohort = AddReturnSubset(packet, snapshot, Math.Max(1, cohort.ReturnParts.Length * 3 / 4));
            }
            if (TownServiceMotionCodec.TryWritePacked(packet) == null)
            { packet.Entries.RemoveRange(cohortAt, packet.Entries.Count - cohortAt); continue; }
            bundles++;
            var staged = new TownServiceMotionPending { Entry = cohort, ReturnSnapshot = snapshot };
            selected.Add(new Selected(staged, 0, index + 1, bundles));
            size += TownServiceMotionCodec.EntryBytes(cohort);
            foreach (TownServiceReturnPart part in cohort.ReturnParts)
            {
                var rootDependency = new TownServiceMotionPending { Entry = snapshot.RootEntries[part.Index], ReturnSnapshot = snapshot };
                selected.Add(new Selected(rootDependency, 0, index + 1, bundles));
                size += TownServiceMotionCodec.EntryBytes(rootDependency.Entry);
                if (snapshot.Layouts[part.Index] is TownServiceMotionPending layout)
                {
                    seen.Add(layout);
                    // Preserve the immutable dependency from the same physical
                    // source instant, even if its latest layout changed meanwhile.
                    var dependent = new TownServiceMotionPending { Entry = snapshot.LayoutEntries[part.Index]!, ReturnSnapshot = snapshot };
                    selected.Add(new Selected(dependent, 0, index + 1, bundles));
                    size += TownServiceMotionCodec.EntryBytes(dependent.Entry);
                }
            }
        }
        int turn = 0;
        while (packet.Entries.Count < TownServiceMotionCodec.MaxExpandedEntries
            && (visited[0] < live.Count || visited[1] < visibleFan.Count || visited[2] < ordinary.Count))
        {
            // Two live turns, one visible fan and one recovery turn. The order matters:
            // fitting a smaller incompressible prefix preserves all three participants.
            int group = turn++ % 4; group = group < 2 ? 0 : group - 1;
            List<TownServiceMotionPending> waiting = groups[group];
            if (visited[group] >= waiting.Count) continue;
            int index = (cursors[group] + visited[group]++) % waiting.Count;
            TownServiceMotionPending slot = waiting[index];
            if (slot.SentAt == now || !seen.Add(slot)) continue;
            // The common return receipt owns its member roots and clocks; ordinary
            // numeric turns cannot split a physical picture during staging.
            if (slot.Entry.Kind == 8 || slot.ReturnLayout && HasNativeReturn(live, slot.Entry)
                || slot.Entry.Kind == 1 && HasNativeReturn(live, slot.Entry)) continue;
            int bytes = TownServiceMotionCodec.EntryBytes(slot.Entry);
            if (size + bytes > TownServiceMotionCodec.MaxExpandedBytes) break;
            packet.Entries.Add(slot.Entry); selected.Add(new Selected(slot, group, index + 1)); size += bytes;
        }
        byte[]? encoded = packet.Entries.Count == 0 ? null : TownServiceMotionCodec.TryWritePacked(packet);
        while (encoded == null && selected.Count > 0)
        {
            // At most logarithmically many compression probes; real random float
            // payloads must retain a legacy-sized finite turn too.
            int keep = Math.Max(0, selected.Count * 3 / 4);
            while (keep > 0 && keep < selected.Count && selected[keep - 1].Bundle != 0
                && selected[keep - 1].Bundle == selected[keep].Bundle) keep--;
            selected.RemoveRange(keep, selected.Count - keep);
            packet.Entries.RemoveRange(initial + keep, packet.Entries.Count - initial - keep);
            if (packet.Entries.Count > 0) encoded = TownServiceMotionCodec.TryWritePacked(packet);
        }
        if (encoded == null) return System.Array.Empty<byte>();
        foreach (Selected accepted in selected)
        {
            accepted.Slot.Dirty = false; accepted.Slot.SentAt = now;
            if (accepted.Slot.Entry.Kind == 2 && accepted.Slot.ReturnSnapshot is ReturnSnapshot layoutSnapshot)
                for (int i = 0; i < layoutSnapshot.LayoutEntries.Length; i++)
                    if (ReferenceEquals(layoutSnapshot.LayoutEntries[i], accepted.Slot.Entry)
                        && layoutSnapshot.Layouts[i] is TownServiceMotionPending source
                        && ReferenceEquals(source.Entry, accepted.Slot.Entry))
                    { source.Dirty = false; source.SentAt = now; }
            if (accepted.Slot.Entry.Kind == 10)
            {
                ReturnSnapshot snapshot = accepted.Slot.ReturnSnapshot!;
                snapshot.Cursor += accepted.Slot.Entry.ReturnParts.Length;
                for (int i = 0; i < snapshot.Sources.Length; i++)
                {
                    TownServiceMotionPending source = snapshot.Sources[i];
                    source.SentAt = now;
                    if (snapshot.Cursor == snapshot.Parts.Length)
                    {
                        // A staged instant may finish after native completion
                        // replaced this slot with its final physical geometry.
                        // Acknowledge only the exact entry we froze; keep that
                        // newer receipt dirty for the next complete cohort.
                        if (ReferenceEquals(source.Entry, snapshot.SourceEntries[i]))
                        { source.Dirty = false; source.AdmittedReturnRevision = source.Entry.Revision; }
                        TownServiceMotionPending root = snapshot.Roots[i];
                        if (ReferenceEquals(root.Entry, snapshot.RootSourceEntries[i]))
                        { root.Dirty = false; root.SentAt = now; }
                        source.ReturnSnapshot = null;
                    }
                }
            }
            cursors[accepted.Group] = accepted.Next;
        }
        liveCursor = cursors[0]; visibleCursor = cursors[1]; ordinaryCursor = cursors[2];
        return encoded;
    }
    internal static void Fill(TownServiceMotionPacket packet, List<TownServiceMotionPending> live,
        List<TownServiceMotionPending> ordinary, ref int liveCursor, ref int ordinaryCursor, float now)
    {
        int emptyCursor = 0;
        Fill(packet, live, NoVisibleFan, ordinary, ref liveCursor, ref emptyCursor, ref ordinaryCursor, now);
    }
    internal static void Fill(TownServiceMotionPacket packet, List<TownServiceMotionPending> live,
        List<TownServiceMotionPending> visibleFan, List<TownServiceMotionPending> ordinary,
        ref int liveCursor, ref int visibleCursor, ref int ordinaryCursor, float now)
    {
        int size = 21;
        foreach (TownServiceMotionEntry entry in packet.Entries) size += TownServiceMotionCodec.EntryBytes(entry);
        // Reserve a complete record, rather than a percentage too small to fit
        // its canvas. A visible wrist reveal has a turn before cold heartbeats;
        // it cannot consume the turn of a held/palm card, press or scroll.
        int cold = Reservation(ordinary), visible = Reservation(visibleFan);
        Append(packet, live, ref liveCursor, ref size, TownServiceMotionCodec.MaxBytes - cold - visible, now);
        Append(packet, visibleFan, ref visibleCursor, ref size, TownServiceMotionCodec.MaxBytes - cold, now);
        Append(packet, ordinary, ref ordinaryCursor, ref size, TownServiceMotionCodec.MaxBytes, now);
        Append(packet, live, ref liveCursor, ref size, TownServiceMotionCodec.MaxBytes, now);
        Append(packet, visibleFan, ref visibleCursor, ref size, TownServiceMotionCodec.MaxBytes, now);
    }
    private static int Reservation(List<TownServiceMotionPending> waiting)
    {
        int largest = 0;
        foreach (TownServiceMotionPending slot in waiting) largest = System.Math.Max(largest, TownServiceMotionCodec.EntryBytes(slot.Entry));
        return largest;
    }
    private static void Append(TownServiceMotionPacket packet, List<TownServiceMotionPending> waiting,
        ref int cursor, ref int size, int limit, float now)
    {
        if (waiting.Count == 0) { cursor = 0; return; }
        int start = cursor % waiting.Count;
        for (int i = 0; i < waiting.Count && packet.Entries.Count < TownServiceMotionCodec.MaxEntries; i++)
        {
            int index = (start + i) % waiting.Count;
            TownServiceMotionPending slot = waiting[index];
            if (slot.SentAt == now) continue;
            int bytes = TownServiceMotionCodec.EntryBytes(slot.Entry);
            if (size + bytes > limit) continue;
            packet.Entries.Add(slot.Entry); size += bytes; slot.Dirty = false; slot.SentAt = now;
            cursor = index + 1;
        }
    }
    private static int ReturnRoot(List<TownServiceMotionPending> live, TownServiceMotionEntry flight)
    {
        for (int n = 0; n < live.Count; n++)
        {
            TownServiceMotionEntry root = live[n].Entry;
            if (root.Kind == 1 && root.Lane == flight.Lane && root.Service == flight.Service
                && root.Session == flight.Session && root.PublicClaim == flight.PublicClaim
                && root.Module == flight.Module && root.Structure == flight.Structure && root.Hand == flight.Hand) return n;
        }
        return -1;
    }
    private static bool HasNativeReturn(List<TownServiceMotionPending> live, TownServiceMotionEntry root)
    {
        foreach (TownServiceMotionPending clock in live)
            if (clock.Entry.Kind == 8
                && clock.Entry.Lane == root.Lane && clock.Entry.Service == root.Service
                && clock.Entry.Session == root.Session && clock.Entry.PublicClaim == root.PublicClaim
                && clock.Entry.Module == root.Module && clock.Entry.Structure == root.Structure) return true;
        return false;
    }
    private static bool SameReturnRoot(TownServiceMotionEntry first, TownServiceMotionEntry second)
    {
        if (second.Kind != 8 || first.Lane != second.Lane || first.Service != second.Service
            || first.Session != second.Session || first.PublicClaim != second.PublicClaim
            || first.Hand != second.Hand || first.Revision != second.Revision
            || first.Numbers.Length != 38 || second.Numbers.Length != 38) return false;
        for (int n = 0; n < 28; n++) if (first.Numbers[n] != second.Numbers[n]) return false;
        return true;
    }

    internal sealed class ReturnSnapshot
    {
        internal TownServiceMotionEntry Header = null!;
        internal TownServiceReturnPart[] Parts = null!;
        internal TownServiceMotionPending[] Sources = null!;
        internal TownServiceMotionEntry[] SourceEntries = null!;
        internal TownServiceMotionPending[] Roots = null!;
        internal TownServiceMotionEntry[] RootSourceEntries = null!, RootEntries = null!;
        internal TownServiceMotionPending?[] Layouts = null!;
        internal TownServiceMotionEntry?[] LayoutEntries = null!;
        internal int Cursor;
        internal bool Matches(TownServiceMotionEntry entry, List<TownServiceMotionPending> live)
        {
            if (Header.Lane != entry.Lane || Header.Service != entry.Service || Header.Session != entry.Session
                || Header.PublicClaim != entry.PublicClaim || Header.Hand != entry.Hand || Header.Revision != entry.Revision) return false;
            for (int i = 0; i < Sources.Length; i++)
            {
                TownServiceMotionEntry current = Sources[i].Entry;
                if (!live.Contains(Sources[i]) || current.Kind != 8 || current.Module != Header.ReturnMembers[i]
                    || current.Structure != Header.ReturnStructures[i] || current.Hand != Header.Hand
                    || current.Session != Header.Session || current.Service != Header.Service
                    || current.PublicClaim != Header.PublicClaim || current.Revision != Header.Revision
                    || current.Numbers.Length != 38 || current.Numbers[2] != Header.Numbers[2]
                    || current.Numbers[3] != Header.Numbers[3]) return false;
                int root = ReturnRoot(live, current);
                if (root < 0 || live[root].Entry.Visible != Parts[i].Visible
                    || live[root].Entry.ParentAlpha != Parts[i].ParentAlpha) return false;
                // The native destination can animate within this same return
                // revision (merchant home/pop/fan rotation). Finish the frozen
                // exact source instant; the next receipt carries that movement.
                // Restarting every subset would starve its later child originals.
            }
            int members = 0;
            foreach (TownServiceMotionPending candidate in live)
                if (SameReturnRoot(entry, candidate.Entry)) members++;
            return members == Sources.Length;
        }
        internal TownServiceMotionEntry Subset(int count)
        {
            var parts = new TownServiceReturnPart[count]; Array.Copy(Parts, Cursor, parts, 0, count);
            return new TownServiceMotionEntry { Kind = 10, Lane = Header.Lane, Service = Header.Service,
                Session = Header.Session, PublicClaim = Header.PublicClaim, Module = Header.Module, Structure = Header.Structure,
                Hand = Header.Hand, Revision = Header.Revision, ReturnSampleTime = Header.ReturnSampleTime,
                Numbers = Header.Numbers, ReturnMembers = Header.ReturnMembers, ReturnStructures = Header.ReturnStructures, ReturnParts = parts };
        }
    }
    private static ReturnSnapshot? CaptureReturn(List<TownServiceMotionPending> live, TownServiceMotionEntry clock, float now)
    {
        var members = new List<TownServiceMotionPending>();
        foreach (TownServiceMotionPending part in live) if (SameReturnRoot(clock, part.Entry)) members.Add(part);
        members.Sort((first, second) => first.Entry.Module.CompareTo(second.Entry.Module));
        if (members.Count == 0 || members.Count > 64) return null;
        var header = new TownServiceMotionEntry { Kind = 10, Lane = clock.Lane, Service = clock.Service, Session = clock.Session,
            PublicClaim = clock.PublicClaim, Module = members[0].Entry.Module, Structure = members[0].Entry.Structure,
            Hand = clock.Hand, Revision = clock.Revision, ReturnSampleTime = now, Numbers = new float[28],
            ReturnMembers = new ushort[members.Count], ReturnStructures = new uint[members.Count] };
        Array.Copy(clock.Numbers, header.Numbers, 28);
        var snapshot = new ReturnSnapshot { Header = header, Sources = members.ToArray(),
            SourceEntries = new TownServiceMotionEntry[members.Count], Roots = new TownServiceMotionPending[members.Count],
            RootSourceEntries = new TownServiceMotionEntry[members.Count], RootEntries = new TownServiceMotionEntry[members.Count],
            Parts = new TownServiceReturnPart[members.Count],
            Layouts = new TownServiceMotionPending?[members.Count], LayoutEntries = new TownServiceMotionEntry?[members.Count] };
        for (int i = 0; i < members.Count; i++)
        {
            TownServiceMotionEntry member = members[i].Entry;
            snapshot.SourceEntries[i] = member;
            int root = ReturnRoot(live, member); if (root < 0) return null;
            snapshot.Roots[i] = live[root]; snapshot.RootSourceEntries[i] = live[root].Entry;
            TownServiceMotionEntry captured = live[root].Entry;
            // Canvas scale can legitimately be zero while a purchased original
            // is prepared. Freeze its current complete native recipe with the
            // physical receipt; a later ordinary heartbeat cannot release it.
            snapshot.RootEntries[i] = new TownServiceMotionEntry { Kind = 1, Lane = captured.Lane,
                Service = captured.Service, Session = captured.Session, PublicClaim = captured.PublicClaim,
                Module = captured.Module, Structure = captured.Structure, Hand = captured.Hand,
                ParentModule = captured.ParentModule, Binding = captured.Binding, ParentAlpha = captured.ParentAlpha,
                Visible = captured.Visible, Pose = (float[])captured.Pose.Clone(), HasCanvasUpdate = true,
                HasCanvasFrame = captured.HasCanvasFrame, CanvasOnHand = captured.CanvasOnHand,
                CanvasPose = (float[])captured.CanvasPose.Clone(), CanvasRect = (float[])captured.CanvasRect.Clone(),
                CanvasSettings = (float[])captured.CanvasSettings.Clone(),
                CanvasSortingOrder = captured.CanvasSortingOrder, CanvasSortingLayer = captured.CanvasSortingLayer };
            header.ReturnMembers[i] = member.Module; header.ReturnStructures[i] = member.Structure;
            var part = new TownServiceReturnPart { Index = (byte)i, Visible = live[root].Entry.Visible,
                ParentAlpha = live[root].Entry.ParentAlpha, Child = new float[10] };
            Array.Copy(member.Numbers, 28, part.Child, 0, 10); snapshot.Parts[i] = part;
            foreach (TownServiceMotionPending candidate in live)
                if (candidate.ReturnLayout && candidate.Dirty && candidate.Entry.Kind == 2
                    && candidate.Entry.Lane == member.Lane && candidate.Entry.Service == member.Service
                    && candidate.Entry.Session == member.Session && candidate.Entry.PublicClaim == member.PublicClaim
                    && candidate.Entry.Module == member.Module && candidate.Entry.Structure == member.Structure)
                { snapshot.Layouts[i] = candidate; snapshot.LayoutEntries[i] = candidate.Entry; break; }
        }
        foreach (TownServiceMotionPending source in members) source.ReturnSnapshot = snapshot;
        return snapshot;
    }
    private static TownServiceMotionEntry AddReturnSubset(TownServiceMotionPacket packet, ReturnSnapshot snapshot, int count)
    {
        TownServiceMotionEntry cohort = snapshot.Subset(count); packet.Entries.Add(cohort);
        foreach (TownServiceReturnPart part in cohort.ReturnParts)
        {
            packet.Entries.Add(snapshot.RootEntries[part.Index]);
            if (snapshot.LayoutEntries[part.Index] is TownServiceMotionEntry layout) packet.Entries.Add(layout);
        }
        return cohort;
    }

}
