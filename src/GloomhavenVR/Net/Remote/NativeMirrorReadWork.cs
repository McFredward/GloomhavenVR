using System;
using System.Runtime.CompilerServices;
using GloomhavenVR.Core;

namespace GloomhavenVR.Net;

/// <summary>Debug work accounting for the immediate nonvirtual source-read subset.
/// Counts reads actually retained and second reads avoided on changed properties,
/// never whole mirror nodes or hypothetical unchanged-property savings. Nested native
/// callbacks contribute to one outer report; normal rendering retains only a cheap guard.
/// Local native clone users also contribute, so these counters are not a peer census.
/// </summary>
internal static class NativeMirrorReadWork
{
    private static int _depth;
    private static long _reads, _reused, _onNodes, _offNodes;

    internal readonly struct Scope : IDisposable
    {
        private readonly bool _active;
        internal Scope(bool active) { _active = active; }
        public void Dispose()
        {
            if (!_active || --_depth != 0) return;
            PerfMonitor.Count("Mirror.NativeSourceReads", _reads);
            PerfMonitor.Count("Mirror.NativeSourceReadsReused", _reused);
            PerfMonitor.Count("Mirror.ReadReuseOnNodes", _onNodes);
            PerfMonitor.Count("Mirror.ReadReuseOffNodes", _offNodes);
            _reads = _reused = _onNodes = _offNodes = 0;
        }
    }

    internal static Scope Begin()
    {
        if (!PerfMonitor.StepsActive || !VRLog.WantsDebug) return default;
        _depth++;
        return new Scope(true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Node(bool reused)
    {
        if (_depth == 0) return;
        if (reused) _onNodes++; else _offNodes++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Observe(bool secondReadNeeded, bool reused)
    {
        if (_depth == 0) return;
        _reads++;
        if (!secondReadNeeded) return;
        if (reused) _reused++; else _reads++;
    }
}
