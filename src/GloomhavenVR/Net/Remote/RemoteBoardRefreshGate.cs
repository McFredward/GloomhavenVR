using System;

namespace GloomhavenVR.Net;

[Flags]
internal enum RemoteBoardRefreshSections : byte
{
    None = 0,
    Objectives = 1,
    Elements = 2,
    Track = 4,
    Actor = 8,
    All = Objectives | Elements | Track | Actor,
}

/// <summary>
/// Per-board content invalidation, separate from the unconditional per-frame animation drive.
/// Native objective/element/track structure and fitted geometry depend on their original sources;
/// owner extras and native use-bar descriptors affect actor/furniture content. Each section still
/// observes every source/model edge immediately and independently retries incomplete native builds.
/// </summary>
internal sealed class RemoteBoardRefreshGate
{
    private Section _objectives, _elements, _track, _actor;
    private uint _presence;
    private object? _actorSubject;

    internal RemoteBoardRefreshSections Poll(uint presence, ulong objectives, ulong elements,
        ulong track, ulong actor, object? actorSubject, float now, float recoverySeconds,
        RemoteBoardRefreshSections recovery = RemoteBoardRefreshSections.None)
    {
        RemoteBoardRefreshSections result = RemoteBoardRefreshSections.None;
        if (_objectives.Poll(objectives, (recovery & RemoteBoardRefreshSections.Objectives) != 0, now, recoverySeconds)) result |= RemoteBoardRefreshSections.Objectives;
        if (_elements.Poll(elements, (recovery & RemoteBoardRefreshSections.Elements) != 0, now, recoverySeconds)) result |= RemoteBoardRefreshSections.Elements;
        if (_track.Poll(track, (recovery & RemoteBoardRefreshSections.Track) != 0, now, recoverySeconds)) result |= RemoteBoardRefreshSections.Track;
        bool ownerChanged = _presence != presence || !ReferenceEquals(_actorSubject, actorSubject);
        if (_actor.Poll(actor, ownerChanged, now, recoverySeconds)) result |= RemoteBoardRefreshSections.Actor;
        _presence = presence;
        _actorSubject = actorSubject;
        return result;
    }

    internal void Reset()
    {
        _objectives = _elements = _track = _actor = default;
        _presence = 0;
        _actorSubject = null;
    }

    private struct Section
    {
        private bool _initialized;
        private ulong _revision;
        private float _nextRecovery;

        internal bool Poll(ulong revision, bool force, float now, float recoverySeconds)
        {
            if (_initialized && !force && _revision == revision && now < _nextRecovery) return false;
            _initialized = true;
            _revision = revision;
            _nextRecovery = now + recoverySeconds;
            return true;
        }
    }
}

/// <summary>Track row binding identity is independent of its unchanged visible art/hierarchy.</summary>
internal static class RemoteBoardEntryIdentity
{
    internal static void Mix(ref ulong revision, object? entry, object? actor, object? avatar)
    {
        MixReference(ref revision, entry);
        MixReference(ref revision, actor);
        MixReference(ref revision, avatar);
    }

    private static void MixReference(ref ulong revision, object? value)
    {
        int identity = value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        unchecked { revision = (revision ^ (uint)identity) * 1099511628211UL; }
    }
}

/// <summary>A local playback-readiness edge must not depend on a newer network sample.</summary>
internal struct RemoteBoardRecoveryEdge
{
    internal bool Pending { get; private set; }
    internal bool Observe(bool applied)
    {
        bool recovered = Pending && applied;
        Pending = !applied;
        return recovered;
    }
    internal void Reset() => Pending = false;
}
