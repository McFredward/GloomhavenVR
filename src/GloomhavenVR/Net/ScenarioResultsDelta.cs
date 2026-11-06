using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.Net;

/// <summary>Cumulative original-property updates against one explicitly named FULL results
/// picture. Existing pure TownServiceDelta does the node diff/merge through a local data
/// adapter; no NPC78 packet, controller, command or previous delta is used as a baseline.
/// Inputs are published immutable snapshots; unchanged native node/value objects are retained.</summary>
internal static class ScenarioResultsDelta
{
    internal static bool Compatible(ScenarioResultsState? basis, ScenarioResultsState? current)
    {
        if (!Full(basis) || !Full(current) || !SameHeaders(basis!, current!) || current!.Revision < basis!.Revision) return false;
        for (int i = 0; i < basis.Parts.Length; i++)
            if (!TownServiceDelta.Compatible(Adapter(basis, basis.Parts[i]), Adapter(current, current.Parts[i]))) return false;
        return true;
    }

    internal static ScenarioResultsState? Create(ScenarioResultsState? basis, ScenarioResultsState? current)
    {
        if (!Compatible(basis, current)) return null;
        var result = CloneHeaders(current!); result.BaseRevision = basis!.Revision;
        for (int i = 0; i < result.Parts.Length; i++)
        {
            TownServiceFrame before = Adapter(basis, basis.Parts[i]);
            TownServiceFrame after = Adapter(current!, current!.Parts[i]);
            TownServiceFrame changes = TownServiceDelta.Create(before, after);
            // TownServiceValue.Same deliberately equates IEEE signed zero. Results' exact
            // wire vocabulary retains its bits, so add only the differences it omitted.
            var byBinding = new Dictionary<uint, TownServiceNode>();
            foreach (TownServiceNode node in changes.Nodes) byBinding.Add(node.Binding, node);
            for (int n = 0; n < after.Nodes.Length; n++)
                foreach (var property in after.Nodes[n].Values)
                    if (!ScenarioResultsCodec.SameValue(before.Nodes[n].Values[property.Key], property.Value))
                    {
                        uint binding = after.Nodes[n].Binding;
                        if (!byBinding.TryGetValue(binding, out TownServiceNode? node))
                        { node = new TownServiceNode { Binding = binding }; byBinding.Add(binding, node); }
                        if (!node.Values.ContainsKey(property.Key)) node.Values.Add(property.Key, property.Value);
                    }
            var ordered = new List<TownServiceNode>();
            foreach (TownServiceNode node in after.Nodes)
                if (byBinding.TryGetValue(node.Binding, out TownServiceNode? changed)) ordered.Add(changed);
            result.Parts[i].Nodes = ordered.ToArray();
        }
        try { ScenarioResultsCodec.Validate(result); return result; }
        catch (InvalidDataException) { return null; }
    }

    internal static ScenarioResultsState? Expand(ScenarioResultsState? basis, ScenarioResultsState delta)
    {
        try { ScenarioResultsCodec.Validate(delta); }
        catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException) { return null; }
        if (!delta.HasPresentation) return null;
        if (delta.BaseRevision == 0) return delta;
        if (!Full(basis) || basis!.Revision != delta.BaseRevision || delta.Revision < delta.BaseRevision || !SameHeaders(basis, delta)) return null;
        var result = CloneHeaders(delta); result.BaseRevision = 0;
        for (int i = 0; i < result.Parts.Length; i++)
        {
            ScenarioResultsPart original = basis.Parts[i], patch = delta.Parts[i];
            var known = new Dictionary<uint, TownServiceNode>();
            foreach (TownServiceNode node in original.Nodes) known.Add(node.Binding, node);
            foreach (TownServiceNode node in patch.Nodes)
            {
                if (!known.TryGetValue(node.Binding, out TownServiceNode? old)) return null;
                foreach (ushort property in node.Values.Keys) if (!old.Values.ContainsKey(property)) return null;
            }
            TownServiceFrame? expanded = TownServiceDelta.Expand(Adapter(basis, original), Adapter(delta, patch));
            if (expanded == null) return null;
            result.Parts[i].Nodes = expanded.Nodes;
        }
        try { ScenarioResultsCodec.Validate(result); return result; }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>Copy every geometry/authority header. Part/node content is supplied by the
    /// full capture or diff/merge, never inferred from another window or another frame.</summary>
    internal static ScenarioResultsState CloneHeaders(ScenarioResultsState state)
    {
        var result = new ScenarioResultsState { Key = state.Key, Revision = state.Revision, BaseRevision = state.BaseRevision,
            SampleTime = state.SampleTime, Visible = state.Visible, HasPresentation = state.HasPresentation,
            Pose = state.Pose, Size = state.Size, Reference = state.Reference, Author = state.Author,
            Origin = state.Origin, Rotation = state.Rotation, Scale = state.Scale, FrameSize = state.FrameSize, Move = state.Move,
            Parts = new ScenarioResultsPart[state.Parts.Length] };
        for (int i = 0; i < result.Parts.Length; i++)
        {
            ScenarioResultsPart part = state.Parts[i];
            result.Parts[i] = new ScenarioResultsPart { Id = part.Id, ParentId = part.ParentId,
                ParentBinding = part.ParentBinding, Address = part.Address, Structure = part.Structure };
        }
        return result;
    }
    private static bool Full(ScenarioResultsState? state)
    {
        if (state == null || !state.HasPresentation || state.BaseRevision != 0) return false;
        try { ScenarioResultsCodec.Validate(state); return true; }
        catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException) { return false; }
    }
    private static bool SameHeaders(ScenarioResultsState a, ScenarioResultsState b)
    {
        if (a.Key != b.Key || a.Author != b.Author || a.Parts.Length != b.Parts.Length) return false;
        for (int i = 0; i < a.Parts.Length; i++)
        {
            ScenarioResultsPart x = a.Parts[i], y = b.Parts[i];
            if (x.Id != y.Id || x.ParentId != y.ParentId || x.ParentBinding != y.ParentBinding
                || x.Address != y.Address || x.Structure != y.Structure) return false;
        }
        return true;
    }
    private static TownServiceFrame Adapter(ScenarioResultsState state, ScenarioResultsPart part) => new()
    {
        Session = state.Key, Sequence = state.Revision, BaseSequence = state.BaseRevision,
        Module = part.Id, ParentModule = part.ParentId, ParentBinding = part.ParentBinding,
        Template = 1, TemplateAddress = part.Address, Structure = part.Structure,
        Visible = state.Visible, SampleTime = state.SampleTime, Nodes = part.Nodes,
    };
}
