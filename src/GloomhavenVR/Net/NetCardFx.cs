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

    private static readonly Queue<byte> s_queue = new(MaxQueued);
    private static byte s_seq;
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
    public static void Report(CardFxAnchor from, CardFxAnchor to)
    {
        // Pack both endpoints into one byte: low nibble FROM, high nibble TO.
        byte packed = (byte)(((byte)from & 0x0F) | (((byte)to & 0x0F) << 4));
        s_queued++;
        if (s_queue.Count >= MaxQueued)
        {
            s_queue.Dequeue(); // nobody draining (single-player) — keep the newest
            s_dropped++;
        }
        s_queue.Enqueue(packed);

        if (!s_loggedFirst)
        {
            s_loggedFirst = true;
            VRLog.Info("Net", $"Card FX outbox: first event queued ({from} -> {to}) — remote peers can now " +
                              "play this card animation locally (2 bytes on the extras packet, no per-frame poses).");
        }
    }

    /// <summary>
    /// Pop the next event and stamp it with a fresh sequence number. False when the queue is empty
    /// (the caller then re-sends the last event unchanged, for redundancy).
    ///
    /// <para>THE REDUNDANCY PROTECTS ONLY THE NEWEST EVENT, AND THE SENDER'S OWN COMMENT USED TO
    /// SAY OTHERWISE. <c>NetAvatarDriver</c>'s dispatch block claimed "a single lost packet still
    /// lands within 200 ms" — true only while nothing else is queued behind it. An event that has
    /// been dequeued exists nowhere but in the driver's one-slot <c>_lastFxEndpoints</c>, and the
    /// NEXT dequeue overwrites it, so when two events go out in consecutive packets the loss of the
    /// first is permanent. A turn-clear ALWAYS produces two.</para>
    ///
    /// <para>MEASURED (ModBuild 461, both logs): the owner logged six <c>[Cards] FLIGHT ORIGIN</c>
    /// events, three turn-clear pairs; the observing host played four (<c>Remote card FX ...
    /// playing</c>, zero SKIPPED), and in BOTH losses it was the FIRST of the pair. The stream is
    /// Bolt UNRELIABLE by design — <c>FfsNetTransport</c> sends with <c>canBeUnreliable: true</c>,
    /// and a reliable channel exists and is used elsewhere (<c>EnemyInfoContinue</c> sends
    /// <c>canBeUnreliable: false</c>) — so loss is the contract and redundancy is the intended
    /// answer. The defect is that the redundancy has a one-event memory. NOT FIXED IN THIS BUILD:
    /// every candidate remedy changes how a shipped wire field is consumed on an unreliable channel
    /// and can manufacture either a DUPLICATE flight or a flight replayed late out of an
    /// already-empty recess, which is report item 7's own symptom. This build MEASURES it instead —
    /// see <see cref="LogOutbox"/> and <c>Net.RemoteAvatar</c>'s CARD FX LOST — so the next round
    /// picks a remedy against a number rather than against an argument.</para>
    ///
    /// <para>THE THREE CANDIDATE REMEDIES, WITH WHAT EACH COSTS, so the choice is not re-derived:
    /// </para>
    /// <list type="number">
    /// <item><description>HOLD EACH EVENT IN THE REDUNDANCY SLOT FOR K PACKETS before advancing.
    /// Zero bytes, zero format change, receiver untouched. COST: the second flight of a turn-clear
    /// starts K x 200 ms after the first instead of 200 ms, which is a TIMING divergence from what
    /// the owner watched — and the owner launches both in the same frame. Pick K against the
    /// measured loss rate, not by taste.</description></item>
    /// <item><description>RE-SEND A RING of the last few dispatched events, cycling the one-slot
    /// field across them while the queue is empty. Zero bytes, but the receiver must stop comparing
    /// against a single "last sequence" and track a SET of recently played ones, or it replays each
    /// event every time it comes round. That is a change to how a shipped wire field is consumed on
    /// both ends, and its failure mode is a DUPLICATE flight.</description></item>
    /// <item><description>CARRY TWO EVENTS IN ONE PACKET. Exact, no delay, no duplicate. COST: the
    /// FX block is a FLAG-BIT field in the extras packet, not a length-prefixed TLV record, so
    /// widening it in place breaks every older peer's parse. It would have to be a NEW additive TLV
    /// record (id 46 is the next free — 45 is
    /// <c>NetProtocol.ExtIdUseBarSlotIdentity</c>) carrying the
    /// second pending event, with the documented worst case (1798) and
    /// <c>PresenceSerializer.MaxSize</c> (2100) updated together and wire-test vectors added.
    /// (These numbers have gone stale TWICE and are corrected rather than deleted: the text once
    /// said "id 44 is free" after 44 had been allocated and cited a 1738-byte worst case after it
    /// had grown to 1747, and it then said "45 is free" through ModBuild 479's allocation of 45.
    /// A design note that names a taken id is worse than one that names none — it reads as
    /// permission.)</description></item>
    /// </list>
    ///
    /// <para>WHATEVER IS CHOSEN, THE FAILURE DIRECTION MATTERS MORE THAN THE RATE: a LOST event is
    /// a missing animation and nothing on the receiver is drawn wrongly because of it, while a
    /// duplicated or late-replayed one puts a card flying out of a recess that is already empty —
    /// which is the very picture 2026-09-06 report item 7 was filed about. Today's behaviour fails
    /// in the safe direction, which is why this was measured rather than patched blind.</para>
    /// </summary>
    public static bool TryDequeue(out byte endpoints, out byte seq)
    {
        endpoints = 0;
        seq = s_seq;
        if (s_queue.Count == 0)
            return false;
        endpoints = s_queue.Dequeue();
        seq = ++s_seq; // wraps at 255 — dense by one per dispatch, which is what makes loss countable
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
            + "ONE EVENT LEAVES PER PACKET and the redundancy re-send carries only the NEWEST, so "
            + "two events dispatched into consecutive packets are protected unequally: losing the "
            + "packet that carried the first loses it for good. A turn-clear always dispatches two.");
    }

    /// <summary>Drop everything (module shutdown / hot reload) so a stale event never leaks into a
    /// new session.</summary>
    public static void Reset()
    {
        s_queue.Clear();
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
