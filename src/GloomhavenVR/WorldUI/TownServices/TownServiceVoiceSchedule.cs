using System;

namespace GloomhavenVR.WorldUI;

/// <summary>Presentation-only greeting arbitration. Native visits never await speech.</summary>
internal sealed class TownServiceVoiceSchedule
{
    internal sealed class Entry
    {
        internal bool Visiting, Pending, Ended;
        internal float VisitAge, Deadline, NextAllowed, Started, ObservedAge;
        internal ushort Cue, PendingCue, LastRequestedCue, LastVariantCue;
        internal uint Generation, ObservedGeneration;
        internal int Author;
        internal bool Observed;
        internal float LastRequestedAt, LastWorkClock, LastCast, LastAttention;
        internal uint VariantState;
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
        // The enchantress invites a visitor as her hand opens, not before the
        // shared gesture. This also avoids two back-to-back greetings on entry.
        if (beginning && service != 3 && age <= 2f && now >= e.NextAllowed && e.Cue == 0)
            QueueVariant(service, TownServiceVoice.GreetingFirstCue(service), 1, now + 5f, age + now);
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
                QueueVariant(service, 31, 0, now + 3f, clock);
            if (service == 3 && e.LastCast <= .16f && cast > .16f)
                QueueVariant(service, 41, 0, now + 3f, clock);
        }
        // The hand-extension is authored in TLV81. Cue 12 is selected only by
        // the elected face author from that shared edge, then TLV80 publishes its
        // identity and age; proximity on each observer can never choose a line.
        if (continuous && service == 3 && e.LastAttention < .35f && attention >= .35f
            && now >= e.NextAllowed)
            QueueVariant(service, 51, 1, now + 4f, clock);
        e.WorkSeeded = true; e.LastWorkClock = clock; e.LastCast = cast; e.LastAttention = attention;
    }

    private static bool Crossed(float before, float after, float period, float threshold)
        => Math.Floor(before / period) == Math.Floor(after / period)
            && before % period < threshold && after % period >= threshold;

    internal void Request(byte service, ushort firstCue, float now)
    {
        Entry e = At(service);
        // Deduplicate the event, not its selected variant. Choosing before this
        // guard let two copies of one native callback evade the four-second gate.
        if (e.LastRequestedCue == firstCue && now - e.LastRequestedAt < 4f) return;
        e.LastRequestedCue = firstCue; e.LastRequestedAt = now;
        QueueVariant(service, firstCue, 2, now + 6f, now);
    }

    /// <summary>Immediately retire every resident utterance and queued reaction when
    /// native story commitment crosses the point of no return. Generation is retained
    /// so the next authored face packet publishes cue zero for the exact shared take
    /// observers may still be playing.</summary>
    internal void Silence(float now)
    {
        foreach (Entry entry in _entries)
        {
            entry.Visiting = false;
            entry.Pending = false;
            entry.Cue = 0;
            entry.Ended = true;
            entry.WorkSeeded = false;
        }
        _nextWorld = Math.Max(_nextWorld, now + 1f);
    }

    private void QueueVariant(byte service, ushort firstCue, byte priority, float deadline, float entropy)
    {
        Entry e = At(service);
        if (e.Pending && e.Priority > priority) return;
        e.Pending = true; e.PendingCue = Pick(e, firstCue, entropy);
        e.Priority = priority; e.Deadline = deadline;
    }

    /// <summary>Choose one of five performances only on the elected author. TLV80
    /// publishes the resulting exact cue. A private xorshift state gives varied
    /// order without touching Unity's gameplay random stream or repeating the
    /// immediately preceding line.</summary>
    private static ushort Pick(Entry e, ushort firstCue, float entropy)
    {
        // Millisecond quantization is sufficient entropy for presentation and
        // remains available in the game's .NET Framework profile without an
        // allocating float-to-byte conversion.
        uint bits = unchecked((uint)(entropy * 1000f));
        uint state = e.VariantState;
        if (state == 0) state = bits ^ ((uint)firstCue * 0x9e3779b9u) ^ 0xa341316cu;
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        if (state == 0) state = 0x6d2b79f5u;
        e.VariantState = state;
        uint offset = state % 5u;
        ushort cue = (ushort)(firstCue + offset);
        if (cue == e.LastVariantCue)
            cue = (ushort)(firstCue + (offset + 1u + state % 4u) % 5u);
        e.LastVariantCue = cue;
        return cue;
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
            e.Started = now - age; e.Pending = false; e.LastVariantCue = cue;
            e.NextAllowed = Math.Max(e.NextAllowed, now + 45f);
        }
        return true;
    }

    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
