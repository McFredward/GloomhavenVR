using System.Collections.Generic;
using GloomhavenVR.Core;

namespace GloomhavenVR.Net;

/// <summary>
/// The LOCAL outbox for card-animation events (user report 6: "ALLE Kartenanimationen der
/// Mitspieler … sollen im Multiplayer für die Mitspieler genauso sichtbar sein").
///
/// ROOT CAUSE this file exists: everything the mod broadcast about cards was STATE — a hand-card
/// COUNT at 5 Hz plus one held-card POSE at 15 Hz. State is enough to show that a peer *has* a fan
/// and *is holding* a card, but it can never show a card MOVING: the whole point of
/// <see cref="Cards.VRCard.FlyToPile"/> / <see cref="Cards.VRCard.FlyFromPile"/> / the slot dock is
/// a 0.4 s flight between two places, and by the time the next 5 Hz count arrived the flight was
/// over. Peers therefore saw cards teleport (fan count silently drops by one, a card silently
/// appears in a board slot) instead of sliding into the discard/burnt stacks, gliding back into the
/// fan, or docking into a play slot.
///
/// DESIGN — events, not transforms. Streaming the flying card's pose would cost ~20 B × 15 Hz for
/// the whole flight and still look wrong under packet loss. Instead each launch reports a SEMANTIC
/// pair (from-anchor → to-anchor, one byte) plus a wrapping sequence byte, and the receiver plays
/// the identical arc locally against the SENDER'S OWN synced hand/board pose
/// (<see cref="RemoteCardFx"/>). Two bytes per animation, and it degrades to nothing on peers that
/// do not understand the flag.
///
/// The queue is bounded and drained by <see cref="NetAvatarDriver"/>. In single-player / offline
/// nothing drains it, so the cap (<see cref="MaxQueued"/>, oldest dropped) is what keeps this from
/// growing: reporting is a couple of enqueues per turn, never per frame.
/// </summary>
internal static class NetCardFx
{
    /// <summary>Hard cap on undelivered events. A card animation is a human-paced thing (a handful
    /// per turn), so anything beyond this means nobody is draining (single-player) — drop the
    /// OLDEST so the newest, most relevant animation still goes out.</summary>
    private const int MaxQueued = 8;

    private static readonly Queue<(byte Endpoints, byte Flags, CardFlightSource? Source, float CompletionTime, CardBurnCompletion? Completion)> s_queue = new(MaxQueued);
    private static byte s_seq;
    private static readonly Queue<(CardFlightEvent Event, double SentAt, float CompletionTime)> s_history = new(CardFlightHistory.CountMax);
    private static CardFlightHistory s_historySnapshot = new(0, System.Array.Empty<CardFlightEvent>());
    private static CardBurnCompletionHistory s_completionsSnapshot = new(0, System.Array.Empty<CardBurnCompletion>());
    private static readonly List<CardBurnCompletion> s_completions = new(CardBurnCompletionHistory.CountMax);
    private static CardAppearanceState? s_capturedCompletion;
    private static float s_capturedAt = -1f;
    internal static CardBurnCompletionHistory BurnCompletions => s_completionsSnapshot;
    internal static void NoteBurnCompletionCapture(CardAppearanceState state, float time)
    { s_capturedCompletion = state; s_capturedAt = time; }
    internal static void ForgetBurnCompletion(CardAppearanceState state)
    {
        int removed = s_completions.RemoveAll(entry => entry.Key == (state.ActorId, state.SourceActorId, state.PoolSeat, state.PoolCount));
        if (removed != 0) RefreshCompletions();
    }
    internal static void NoteBurnProgress(CardAppearanceState state, float time, bool running)
    {
        var key = (state.ActorId, state.SourceActorId, state.PoolSeat, state.PoolCount);
        int index = s_completions.FindIndex(entry => entry.Key == key);
        if (!running)
        {
            if (index >= 0 && s_completions[index].InProgress) { s_completions.RemoveAt(index); RefreshCompletions(); }
            return;
        }
        if (index >= 0 && s_completions[index].InProgress) return;
        var progress = new CardBurnCompletion(0, 0, CardBurnCompletion.InProgressBit, time,
            state.ActorId, state.SourceActorId, state.PoolSeat, state.PoolCount, 0, 0);
        if (!progress.Valid()) return;
        if (index >= 0) s_completions[index] = progress;
        else if (s_completions.Count < CardBurnCompletionHistory.CountMax) s_completions.Add(progress);
        RefreshCompletions();
    }

    private static void RefreshCompletions()
    {
        var entries = new CardBurnCompletion[s_completions.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = s_completions[i];
            bool recent = false;
            foreach (var flight in s_history)
                if (!entry.InProgress && flight.Event.Sequence == entry.Sequence && flight.CompletionTime == entry.Time)
                { recent = true; break; }
            entries[i] = entry.WithRecentFlight(recent);
        }
        s_completionsSnapshot = new CardBurnCompletionHistory(s_seq, entries);
    }
    internal static CardFlightHistory History => HistoryAt(Clock());
    private static double Clock() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    internal static CardFlightHistory HistoryAt(double now)
    {
        bool changed = false;
        while (s_history.Count > 0 && now - s_history.Peek().SentAt > 2.0)
        { s_history.Dequeue(); changed = true; }
        if (changed) RefreshHistory();
        return s_historySnapshot;
    }
    private static void RefreshHistory()
    {
        var events = new CardFlightEvent[s_history.Count];
        int i = 0;
        foreach (var value in s_history) events[i++] = value.Event;
        s_historySnapshot = new CardFlightHistory(s_seq, events);
        RefreshCompletions();
    }
    private static bool s_loggedFirst;

    /// <summary>How many events have ever been REPORTED into this outbox, and how many have ever
    /// been DISPATCHED onto a packet. THE DENOMINATOR: <see cref="s_seq"/> advances by exactly one
    /// per dispatch and wraps at 255, so a receiver that watches the sequence GAP can say exactly
    /// how many of these never reached it — see <c>Net.RemoteAvatar</c>'s CARD FX LOST line, which
    /// is this counter's other end. <see cref="s_dropped"/> is the outbox's own loss (nobody
    /// draining), which is a different failure and must not be confused with the wire's.</summary>
    private static int s_queued;
    private static int s_dispatched;
    private static int s_dropped;
    private static int s_loggedOutbox = -1;

    /// <summary>True when at least one event is waiting to be sent. The driver uses this to fire an
    /// extras packet IMMEDIATELY instead of waiting up to 200 ms for the next 5 Hz tick — a card
    /// flight only lasts 0.4 s, so the event has to leave on the frame it happened.</summary>
    public static bool Pending => s_queue.Count > 0;

    /// <summary>
    /// Report that the local VR just launched a card animation from <paramref name="from"/> to
    /// <paramref name="to"/>. Cosmetic, fire-and-forget and safe to call from anywhere in the Cards
    /// module: it never touches the transport (the driver drains it), never allocates beyond the
    /// bounded queue, and is a harmless no-op in single-player.
    /// </summary>
    public static void Report(CardFxAnchor from, CardFxAnchor to, byte flags = 0, CardFlightSource? source = null, float completionTime = -1f)
    {
        // Pack both endpoints into one byte: low nibble FROM, high nibble TO.
        byte packed = (byte)(((byte)from & 0x0F) | (((byte)to & 0x0F) << 4));
        s_queued++;
        if (s_queue.Count >= MaxQueued)
        {
            s_queue.Dequeue(); // nobody draining (single-player) — keep the newest
            s_dropped++;
        }
        if (to != CardFxAnchor.Burnt || float.IsNaN(completionTime) || float.IsInfinity(completionTime)) completionTime = -1f;
        CardBurnCompletion? terminal = null;
        CardAppearanceState? captured = s_capturedCompletion;
        if (completionTime >= 0f && completionTime == s_capturedAt && source.HasValue && captured != null
            && source.Value.ActorId == captured.ActorId)
        {
            var entry = new CardBurnCompletion(0, packed, flags, completionTime, captured.ActorId,
                captured.SourceActorId, captured.PoolSeat, captured.PoolCount, source.Value.Seat, source.Value.Count);
            if (entry.Valid())
            {
                s_completions.RemoveAll(old => old.Key == entry.Key);
                if (s_completions.Count < CardBurnCompletionHistory.CountMax)
                { s_completions.Add(entry); terminal = entry; RefreshCompletions(); }
            }
        }
        s_queue.Enqueue((packed, (byte)(flags & CardFlightVisibility.CoveredBurnBit), source, completionTime, terminal));

        if (!s_loggedFirst)
        {
            s_loggedFirst = true;
            VRLog.Info("Net", $"Card FX outbox: first event queued ({from} -> {to}) — remote peers can now " +
                              "play this card animation locally (2 bytes on the extras packet, no per-frame poses).");
        }
    }

    /// <summary>
    /// Pop a flight with its actor/seat provenance and append it to the bounded recent history.
    /// Record62 repeats the last eight events together, so presence coalescing and a lost first
    /// packet cannot erase the first flight of a pair. Receivers deduplicate by sequence.
    /// </summary>
    public static bool TryDequeue(out byte endpoints, out byte seq)
        => TryDequeue(out endpoints, out seq, out _);

    public static bool TryDequeue(out byte endpoints, out byte seq, out byte flags)
        => TryDequeue(out endpoints, out seq, out flags, out _);

    public static bool TryDequeue(out byte endpoints, out byte seq, out byte flags, out CardFlightSource? source)
    {
        source = null;
        flags = 0;
        endpoints = 0;
        seq = s_seq;
        if (s_queue.Count == 0)
            return false;
        var next = s_queue.Dequeue();
        endpoints = next.Endpoints;
        flags = next.Flags;
        source = next.Source;
        seq = ++s_seq; // wraps at 255 — dense by one per dispatch, which is what makes loss countable
        if (s_history.Count == CardFlightHistory.CountMax) s_history.Dequeue();
        s_history.Enqueue((new CardFlightEvent(seq, endpoints, flags, source), Clock(), next.CompletionTime));
        if (next.Completion.HasValue)
        {
            var completion = next.Completion.Value;
            for (int i = 0; i < s_completions.Count; i++)
                if (s_completions[i].Key == completion.Key && s_completions[i].Time == completion.Time)
                    s_completions[i] = completion.WithSequence(seq);
        }
        RefreshHistory();
        s_dispatched++;
        LogOutbox();
        return true;
    }

    /// <summary>
    /// THE SENDER'S HALF OF THE LOSS DENOMINATOR. Grep token: CARD FX OUTBOX.
    ///
    /// <para>Change-gated on the dispatch count, so one line per event dispatched and none while
    /// the outbox is idle. READ IT WITH the receiving client's CARD FX LOST line: this one says how
    /// many events this machine PUT ON THE WIRE and what sequence number the newest carries; that
    /// one says how many of those sequence numbers a peer never saw. Two logs, two counters, one
    /// subtraction — which is what the 2026-09-06 round could not do, because "six flight origins
    /// here and four flights there" needed two DIFFERENT tokens correlated by hand.</para>
    ///
    /// <para>WORKING = a peer's CARD FX LOST reading 0 lost against this line's dispatched count.
    /// INERT = this line absent while <c>[Cards] FLIGHT ORIGIN</c> is present: the outbox was never
    /// reached and the defect is upstream of the wire entirely. BEYOND THE INSTRUMENT = a non-zero
    /// <c>dropped</c> below, which is the OUTBOX overflowing because nothing is draining it
    /// (single-player, or a torn transport) and is not wire loss at all.</para>
    /// </summary>
    private static void LogOutbox()
    {
        if (s_loggedOutbox == s_dispatched)
            return;
        s_loggedOutbox = s_dispatched;
        // HW-VERIFY: the sender half of the card-FX loss measurement. Grep token: CARD FX OUTBOX.
        VRLog.Note("Net", $"CARD FX OUTBOX: dispatched {s_dispatched} of {s_queued} card-animation "
            + $"event(s) reported this session ({s_dropped} dropped here before ever reaching the "
            + $"wire, {s_queue.Count} still waiting); newest sequence number is {s_seq}. THE "
            + "SEQUENCE IS DENSE — exactly +1 per dispatch, wrapping at 255 — so a receiver's own "
            + "'CARD FX LOST' line can subtract and say precisely how many of these never arrived. "
            + "ONE EVENT LEAVES PER PACKET as a new dispatch; record62 repeats the eight most "
            + "recent events together. Receivers deduplicate each "
            + "sequence, preserving paired flights through presence coalescing and packet loss.");
    }

    /// <summary>Drop everything (module shutdown / hot reload) so a stale event never leaks into a
    /// new session.</summary>
    public static void Reset()
    {
        s_queue.Clear();
        s_history.Clear();
        s_completions.Clear(); s_capturedCompletion = null; s_capturedAt = -1f; RefreshCompletions();
        s_historySnapshot = new CardFlightHistory(s_seq, System.Array.Empty<CardFlightEvent>());
        CardFlightVisibility.Reset();
        s_loggedFirst = false;
        // The COUNTERS are deliberately NOT reset: they are a session-long denominator, and a
        // hot reload in the middle of a scenario must not make the loss arithmetic restart at zero
        // while the receiver's own count keeps climbing. s_seq is likewise left alone — the
        // receiver tracks it as a dense counter and a jump back to 0 would read as a huge loss.
    }

    /// <summary>Unpack the FROM endpoint of a wire byte.</summary>
    public static CardFxAnchor From(byte endpoints) => Clamp((byte)(endpoints & 0x0F));

    /// <summary>Unpack the TO endpoint of a wire byte.</summary>
    public static CardFxAnchor To(byte endpoints) => Clamp((byte)((endpoints >> 4) & 0x0F));

    /// <summary>Clamp an unknown wire id to a safe anchor (a future sender may use ids we do not
    /// know yet — degrade to the board centre rather than misplacing the flight).</summary>
    private static CardFxAnchor Clamp(byte id) =>
        id <= (byte)CardFxAnchor.Active ? (CardFxAnchor)id : CardFxAnchor.Board;
}
