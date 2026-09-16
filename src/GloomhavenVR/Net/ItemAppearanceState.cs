using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

internal sealed class ItemAppearanceNode
{
    internal uint Binding;
    internal CardAppearanceNode Value = new();
    internal ItemAppearanceNode Copy() => new() { Binding = Binding, Value = Value.Copy() };
}

/// <summary>Original item output with positional source address and an opening generation, never an item ID.</summary>
internal sealed class ItemAppearanceState
{
    internal int ActorId;
    internal byte Seat, Count, Population;
    internal uint Generation;
    // bit0: the native burn is running; bit1: its terminal output is retained for handoff; bit2: occupies the use recess.
    internal byte Flags;
    internal ItemAppearanceNode[] Nodes = Array.Empty<ItemAppearanceNode>();
    internal bool Validate()
    {
        if (Population > 1 || ActorId == 0 || Count == 0 || Seat >= Count || Generation == 0 || (Flags & ~7) != 0
            || (Flags & 3) == 3 || Nodes == null || Nodes.Length == 0 || Nodes.Length > ItemAppearanceSnapshot.MaxNodes) return false;
        var keys = new HashSet<ulong>();
        foreach (var node in Nodes)
            if (node == null || node.Binding == 0 || node.Value == null || !node.Value.Validate()
                || node.Value.Role != 0 && node.Value.Role != 7 && node.Value.Role != 11 && node.Value.Role != 12
                || node.Value.Role == 12 && node.Value.Binding != node.Binding
                || !keys.Add(((ulong)node.Binding << 8) | node.Value.Role)) return false;
        return true;
    }
    internal ItemAppearanceState Copy()
    {
        var copy = new ItemAppearanceState { ActorId = ActorId, Seat = Seat, Count = Count, Population = Population, Generation = Generation, Flags = Flags,
            Nodes = new ItemAppearanceNode[Nodes.Length] };
        for (int i = 0; i < Nodes.Length; i++) copy.Nodes[i] = Nodes[i].Copy();
        return copy;
    }
    internal static bool Same(ItemAppearanceState a, ItemAppearanceState b)
    {
        if (a.Population != b.Population || a.ActorId != b.ActorId || a.Seat != b.Seat || a.Count != b.Count || a.Generation != b.Generation
            || a.Flags != b.Flags || a.Nodes.Length != b.Nodes.Length) return false;
        for (int i = 0; i < a.Nodes.Length; i++)
        {
            var x = a.Nodes[i]; var y = b.Nodes[i]; var p = x.Value; var q = y.Value;
            if (x.Binding != y.Binding || p.Role != q.Role || p.Flags != q.Flags || p.Mask != q.Mask || p.Binding != q.Binding) return false;
            for (int j = 0; j < p.Values.Length; j++) if (p.Values[j] != q.Values[j]) return false;
        }
        return true;
    }
}
internal sealed class ItemAppearanceSnapshot
{
    internal const int MaxCards = 32, MaxNodes = 384;
    internal readonly float SampleTime;
    internal readonly ItemAppearanceState[] States;
    internal static bool SameIdentity(ItemAppearanceSnapshot a, ItemAppearanceSnapshot b)
    {
        if (a.States.Length != b.States.Length) return false;
        for (int i = 0; i < a.States.Length; i++)
            if (a.States[i].ActorId != b.States[i].ActorId || a.States[i].Generation != b.States[i].Generation) return false;
        return true;
    }
    internal ItemAppearanceSnapshot(float time, ItemAppearanceState[] states)
    {
        if (float.IsNaN(time) || float.IsInfinity(time) || time < 0 || states == null || states.Length > MaxCards)
            throw new ArgumentException("Invalid native item frame.");
        var keys = new HashSet<ulong>(); int nodes = 0;
        foreach (var state in states)
        {
            if (state == null || !state.Validate() || !keys.Add(((ulong)(uint)state.ActorId << 32) | state.Generation)
                || (nodes += state.Nodes.Length) > MaxNodes) throw new ArgumentException("Invalid native item output.");
        }
        SampleTime = time;
        States = new ItemAppearanceState[states.Length];
        for (int i = 0; i < States.Length; i++) States[i] = states[i].Copy();
    }
}
