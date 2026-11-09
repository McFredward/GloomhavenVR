using GloomhavenVR.Core;
using GloomhavenVR.Net.TownServices;

namespace GloomhavenVR.Net;

/// <summary>Bounded lifecycle receipts, not a frame trace. A header may arrive while
/// a later full picture is still assembling; input traffic alone cannot prove that
/// the common statistics were received or applied. Never log native text or cards.</summary>
internal sealed class ScenarioResultsProgress
{
    private readonly string _stage;
    private int _author, _reports, _phase = -1, _parts, _nodes, _properties;
    private uint _key;
    private ulong _active;
    private bool _visible;
    private ScenarioResultsState? _last;

    internal ScenarioResultsProgress(string stage) { _stage = stage; }

    internal void Observe(ScenarioResultsState state, int bytes, int nativePhase = -1, ulong? transmittedBase = null)
    {
        if (!VRLog.Wants(VRLogLevel.Info) || !state.HasPresentation) return;
        if (_author != state.Author || _key != state.Key)
        { Reset(); _author = state.Author; _key = state.Key; }
        if (_reports >= 8 || ReferenceEquals(_last, state) && _phase == nativePhase) return;
        _last = state;
        int nodes = 0, properties = 0;
        ulong active = 0;
        for (int i = 0; i < state.Parts.Length; i++)
        {
            ScenarioResultsPart part = state.Parts[i];
            nodes += part.Nodes.Length;
            foreach (TownServiceNode node in part.Nodes) properties += node.Values.Count;
            if (i < 64 && part.Nodes.Length != 0
                && part.Nodes[0].Values.TryGetValue(TownServiceProperty.Active, out TownServiceValue? value)
                && value.Numbers.Length != 0 && value.Numbers[0] != 0f) active |= 1UL << i;
        }
        if (_reports != 0 && _phase == nativePhase && _parts == state.Parts.Length
            && _nodes == nodes && _properties == properties && _active == active && _visible == state.Visible) return;
        _phase = nativePhase; _parts = state.Parts.Length; _nodes = nodes;
        _properties = properties; _active = active; _visible = state.Visible; _reports++;
        VRLog.Note("Net", $"RESULTS PRESENTATION PROGRESS: stage={_stage}, author={state.Author}, opening={state.Key}, revision={state.Revision}, base={transmittedBase ?? state.BaseRevision}, visible={state.Visible}, nativePhase={nativePhase}, parts={_parts}, nodes={nodes}, properties={properties}, activeRoots=0x{active:X}, bytes={bytes}; receipt {_reports}/8 for this opening (application is not a pixel measurement).");
    }

    internal void Reset()
    { _author = _reports = _parts = _nodes = _properties = 0; _phase = -1; _key = 0; _active = 0; _visible = false; _last = null; }
}
