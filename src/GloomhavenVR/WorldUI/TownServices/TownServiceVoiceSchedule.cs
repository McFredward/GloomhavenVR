using System;

namespace GloomhavenVR.WorldUI;

/// <summary>Presentation-only greeting arbitration. Native visits never await speech.</summary>
internal sealed class TownServiceVoiceSchedule
{
    internal sealed class Entry
    {
        internal bool Visiting, Pending, Ended;
        internal float VisitAge, Deadline, NextAllowed, Started, ObservedAge;
        internal ushort Cue, PendingCue, LastRequestedCue;
        internal uint Generation, ObservedGeneration;
        internal int Author;
        internal bool Observed;
        internal float LastRequestedAt, LastWorkClock, LastCast;
        internal bool WorkSeeded;
        internal byte Priority;
    }
    private readonly Entry[] _entries = { new(), new(), new() };
    private float _nextWorld;
    internal Entry At(byte service) => _entries[service - 1];

    internal void Visit(byte service, bool visiting, float age, float now)
    {
        Entry e = At(service);
        bool beginning = visiting && (!e.Visiting || age + .2f < e.VisitAge);
        e.Visiting = visiting; e.VisitAge = age;
        if (!visiting && e.Priority == 1) e.Pending = false;
        if (beginning && age <= 2f && now >= e.NextAllowed && e.Cue == 0)
            Queue(service, TownServiceVoice.GreetingCue(service), 1, now + 5f);
    }

    internal void Work(byte service, float clock, float cast, float attention, bool visible, float now)
    {
        Entry e = At(service);
        if (!visible || !Finite(clock) || !Finite(cast)) { e.WorkSeeded = false; return; }
        float delta = clock - e.LastWorkClock;
        bool continuous = e.WorkSeeded && delta >= -.001f && delta <= .25f;
        if (continuous && attention < .2f)
        {
            if (service == 2 && Crossed(e.LastWorkClock, clock, 64f, 6f))
                Queue(service, 7, 0, now + 3f);
            if (service == 3 && e.LastCast <= .16f && cast > .16f)
                Queue(service, (ushort)(Math.Floor(clock / 48f) % 2 == 0 ? 9 : 10), 0, now + 3f);
        }
        e.WorkSeeded = true; e.LastWorkClock = clock; e.LastCast = cast;
    }

    private static bool Crossed(float before, float after, float period, float threshold)
        => Math.Floor(before / period) == Math.Floor(after / period)
            && before % period < threshold && after % period >= threshold;

    internal void Request(byte service, ushort cue, float now)
    {
        Entry e = At(service);
        if (e.LastRequestedCue == cue && now - e.LastRequestedAt < 4f) return;
        e.LastRequestedCue = cue; e.LastRequestedAt = now;
        Queue(service, cue, 2, now + 6f);
    }

    private void Queue(byte service, ushort cue, byte priority, float deadline)
    {
        Entry e = At(service);
        if (e.Pending && e.Priority > priority) return;
        e.Pending = true; e.PendingCue = cue; e.Priority = priority; e.Deadline = deadline;
    }

    internal void Sample(byte service, Func<ushort, float> duration, float now, bool narration)
    {
        Entry e = At(service);
        if (e.Cue != 0 && (narration || now - e.Started >= duration(e.Cue)))
        { e.Cue = 0; e.Ended = true; _nextWorld = now + 1f; }
        if (e.Pending && now > e.Deadline) e.Pending = false;
        if (!e.Pending || e.Cue != 0 || narration || now < _nextWorld || duration(e.PendingCue) <= 0f) return;
        foreach (Entry other in _entries) if (other.Cue != 0) return;
        e.Pending = false;
        if (e.Generation == uint.MaxValue) return; // Never wrap an utterance identity.
        e.Generation++; e.Cue = e.PendingCue; e.Started = now; e.Ended = false;
        if (e.Priority == 1) e.NextAllowed = now + 45f;
    }

    /// <summary>Adopt the elected author's exact performance before a possible handover.
    /// Same-generation late packets cannot reopen an ended cue or rewind its clock.</summary>
    internal bool Observe(byte service, int author, ushort cue, uint generation, float age, float now)
    {
        if (generation == 0 && cue != 0 || !Finite(age) || age < 0f) return false;
        Entry e = At(service);
        bool same = e.Observed && e.Author == author && e.ObservedGeneration == generation;
        if (e.Observed && e.Author == author && generation < e.ObservedGeneration) return false;
        if (same && (cue != 0 && (age + .001f < e.ObservedAge || e.Ended))) return false;
        if (same && e.Cue != 0 && cue != 0 && cue != e.Cue) return false;
        e.Author = author; e.Observed = true; e.Generation = generation; e.ObservedGeneration = generation; e.ObservedAge = age;
        e.Cue = cue; e.Ended = cue == 0;
        if (cue != 0)
        {
            e.Started = now - age; e.Pending = false; e.NextAllowed = Math.Max(e.NextAllowed, now + 45f);
        }
        return true;
    }

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
