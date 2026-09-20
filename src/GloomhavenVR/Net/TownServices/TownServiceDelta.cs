using System;
using System.IO;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>
/// Deltas are cumulative against an explicitly named complete baseline, never a chain of
/// preceding deltas. Losing or reordering an intermediate animation sample cannot corrupt state.
/// </summary>
internal static class TownServiceDelta
{
    internal static TownServiceFrame Create(TownServiceFrame baseline, TownServiceFrame current)
    {
        if (!Compatible(baseline, current)) throw new InvalidDataException("Town-service delta changes topology.");
        TownServiceFrame delta = Header(current); delta.BaseSequence = baseline.Sequence;
        var changed = new List<TownServiceNode>();
        for (int i = 0; i < current.Nodes.Length; i++)
        {
            var node = new TownServiceNode { Binding = current.Nodes[i].Binding };
            foreach (var property in current.Nodes[i].Values)
                if (!baseline.Nodes[i].Values.TryGetValue(property.Key, out TownServiceValue? before) || !property.Value.Same(before))
                    node.Values.Add(property.Key, property.Value);
            if (node.Values.Count != 0) changed.Add(node);
        }
        delta.Nodes = changed.ToArray();
        return delta;
    }
    internal static TownServiceFrame? Expand(TownServiceFrame? baseline, TownServiceFrame delta)
    {
        if (delta.BaseSequence == 0) return delta;
        if (baseline == null || baseline.BaseSequence != 0 || baseline.Sequence != delta.BaseSequence || !HeaderMatches(baseline, delta)) return null;
        var changes = new Dictionary<uint, TownServiceNode>();
        foreach (TownServiceNode node in delta.Nodes)
        { if (changes.ContainsKey(node.Binding)) return null; changes.Add(node.Binding, node); }
        TownServiceFrame frame = Header(delta); frame.BaseSequence = 0;
        frame.Nodes = new TownServiceNode[baseline.Nodes.Length];
        for (int i = 0; i < frame.Nodes.Length; i++)
        {
            var node = new TownServiceNode { Binding = baseline.Nodes[i].Binding };
            foreach (var property in baseline.Nodes[i].Values) node.Values.Add(property.Key, property.Value);
            if (changes.TryGetValue(node.Binding, out TownServiceNode? update))
            { foreach (var property in update.Values) node.Values[property.Key] = property.Value; changes.Remove(node.Binding); }
            frame.Nodes[i] = node;
        }
        return changes.Count == 0 ? frame : null;
    }
    internal static bool Compatible(TownServiceFrame a, TownServiceFrame b)
    {
        if (!HeaderMatches(a, b) || a.Nodes.Length != b.Nodes.Length) return false;
        for (int i = 0; i < a.Nodes.Length; i++) if (a.Nodes[i].Binding != b.Nodes[i].Binding) return false;
        return true;
    }
    private static bool HeaderMatches(TownServiceFrame a, TownServiceFrame b) => a.Service == b.Service
        && a.Session == b.Session && a.Module == b.Module && a.Template == b.Template && a.Structure == b.Structure;
    internal static TownServiceFrame Copy(TownServiceFrame source)
    {
        var result = Header(source); result.Nodes = new TownServiceNode[source.Nodes.Length];
        for (int i = 0; i < source.Nodes.Length; i++)
        {
            var node = new TownServiceNode { Binding = source.Nodes[i].Binding };
            foreach (var pair in source.Nodes[i].Values)
                node.Values.Add(pair.Key, new TownServiceValue { Numbers = (float[])pair.Value.Numbers.Clone(), Text = (string[])pair.Value.Text.Clone() });
            result.Nodes[i] = node;
        }
        return result;
    }
    private static TownServiceFrame Header(TownServiceFrame f) => new()
    {
        Service = f.Service, Session = f.Session, Sequence = f.Sequence, BaseSequence = f.BaseSequence,
        Module = f.Module, Template = f.Template, Structure = f.Structure, Visible = f.Visible,
        SampleTime = f.SampleTime, SessionAge = f.SessionAge, ParentModule = f.ParentModule, ParentBinding = f.ParentBinding,
        ParentAlpha = f.ParentAlpha, Pose = (float[])f.Pose.Clone(), Modules = (ushort[])f.Modules.Clone()
    };
}
