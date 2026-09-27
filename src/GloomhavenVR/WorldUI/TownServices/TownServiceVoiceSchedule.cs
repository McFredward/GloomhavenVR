using System;

namespace GloomhavenVR.WorldUI;

/// <summary>Presentation-only greeting arbitration. Native visits never await speech.</summary>
internal sealed class TownServiceVoiceSchedule
{
    internal sealed class Entry
    {
        internal bool Visiting, Pending, Ended;
        internal float VisitAge, Deadline, NextAllowed, NextAmbientAllowed, Started, ObservedAge;
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
        // A real native visit is a more reliable edge than the animation's sampled
        // attention value: the latter may already be above threshold when Work seeds.
        if (beginning && age <= 2f && now >= e.NextAllowed && e.Cue == 0)
            QueueVariant(service, TownServiceVoice.GreetingFirstCue(service), 1, now + 5f, age + now);
    }

    internal void Work(byte service, float clock, float cast, float attention, bool visible, float now)
    {
        Entry e = At(service);
        if (!visible || !Finite(clock) || !Finite(cast)) { e.WorkSeeded = false; return; }
        float delta = clock - e.LastWorkClock;
        bool continuous = e.WorkSeeded && delta >= -.001f && delta <= .25f;
        // Ambient prayer and casting are atmospheric, not a response to the
        // visitor. The old cast edge occurred on every spell cycle and filled
        // a long map visit with repeated lines. Keep the native activity and
        // visual spell frequency unchanged, but allow at most one incidental
        // utterance per several minutes on the elected speech author.
        if (continuous && attention < .2f && now >= e.NextAmbientAllowed)
        {
            if (service == 2 && Crossed(e.LastWorkClock, clock, 64f, 6f))
            {
                QueueVariant(service, 31, 0, now + 3f, clock);
                e.NextAmbientAllowed = now + 180f;
            }
            if (service == 3 && e.LastCast <= .16f && cast > .16f)
            {
                QueueVariant(service, 41, 0, now + 3f, clock);
                e.NextAmbientAllowed = now + 150f;
            }
        }
        // Native visit and card acceptance own the enchantress invitation. Sampling
        // an attention crossing here missed visits that began before Work seeded.
        e.WorkSeeded = true; e.LastWorkClock = clock; e.LastCast = cast; e.LastAttention = attention;
    }

    private static bool Crossed(float before, float after, float period, float threshold)
        => Math.Floor(before / period) == Math.Floor(after / period)
            && before % period < threshold && after % period >= threshold;

    internal void Request(byte service, ushort firstCue, float now)
    {
        Entry e = At(service);
        // Native visits can queue a greeting/invitation while a visitor is carrying
        // a card. Once the card is accepted, those requests are stale. Likewise a
        // completed enhancement retires an unfinished inspection prompt.
        if (service == 3 && (firstCue == 61 || firstCue == 46))
        {
            RetireRange(e, 11, now);
            RetireRange(e, 51, now);
            if (firstCue == 46) RetireRange(e, 61, now);
        }
        if (service == 3 && firstCue == 51
            && (e.Cue >= 11 && e.Cue <= 15 || e.Cue >= 51 && e.Cue <= 55
                || e.Pending && (e.PendingCue >= 11 && e.PendingCue <= 15
                    || e.PendingCue >= 51 && e.PendingCue <= 55)))
            return;
        // A committed temple donation outranks an availability refresh. The latter is
        // expected immediately after payment and must never consume the first spoken
        // response. If a stale refusal was already queued or speaking, retire that
        // cosmetic line before choosing one of the five grateful performances.
        if (service == 2 && firstCue == 36)
        {
            if (e.Cue >= 56 && e.Cue <= 60) { e.Cue = 0; e.Ended = true; _nextWorld = now; }
            if (e.Pending && e.PendingCue >= 56 && e.PendingCue <= 60) e.Pending = false;
        }
        else if (service == 2 && firstCue == 56
            && (e.Pending && e.PendingCue >= 36 && e.PendingCue <= 40
                || e.Cue >= 36 && e.Cue <= 40)) return;
        // Deduplicate the event, not its selected variant. Choosing before this
        // guard let two copies of one native callback evade the four-second gate.
        if (e.LastRequestedCue == firstCue && now - e.LastRequestedAt < 4f) return;
        e.LastRequestedCue = firstCue; e.LastRequestedAt = now;
        QueueVariant(service, firstCue, 2, now + 6f, now);
    }

    private void RetireRange(Entry entry, ushort firstCue, float now)
    {
        if (entry.Cue >= firstCue && entry.Cue < firstCue + 5)
        { entry.Cue = 0; entry.Ended = true; _nextWorld = now; }
        if (entry.Pending && entry.PendingCue >= firstCue && entry.PendingCue < firstCue + 5)
            entry.Pending = false;
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
