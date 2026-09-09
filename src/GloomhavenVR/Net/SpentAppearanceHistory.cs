using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Owner observations retain immutable model identity across a mutable pile transition.</summary>
internal sealed class SpentAppearanceHistory<TModel, TState> where TModel : class where TState : class
{
    private sealed class Entry
    {
        internal int Actor;
        internal TModel Model = null!;
        internal TState State = null!;
    }
    private readonly List<Entry> _entries = new();
    internal void Remember(int actor, TModel model, TState state)
    {
        foreach (var entry in _entries)
            if (entry.Actor == actor && ReferenceEquals(entry.Model, model)) { entry.State = state; return; }
        if (_entries.Count >= 64) _entries.RemoveAt(0);
        _entries.Add(new Entry { Actor = actor, Model = model, State = state });
    }
    internal void RemoveRecovered(Func<int, TModel, bool> recovered)
        => _entries.RemoveAll(entry => recovered(entry.Actor, entry.Model));
    internal bool TryGet(int actor, TModel model, bool lost, bool recovered, out TState? state)
    {
        state = null;
        if (recovered) _entries.RemoveAll(entry => ReferenceEquals(entry.Model, model));
        if (!lost || recovered) return false;
        foreach (var entry in _entries)
            if (entry.Actor == actor && ReferenceEquals(entry.Model, model)) { state = entry.State; return true; }
        return false;
    }
}
