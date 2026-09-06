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
        if (s_queue.Count >= MaxQueued)
            s_queue.Dequeue(); // nobody draining (single-player) — keep the newest
        s_queue.Enqueue(packed);

        if (!s_loggedFirst)
        {
            s_loggedFirst = true;
            VRLog.Info("Net", $"Card FX outbox: first event queued ({from} -> {to}) — remote peers can now " +
                              "play this card animation locally (2 bytes on the extras packet, no per-frame poses).");
        }
    }

    /// <summary>Pop the next event and stamp it with a fresh sequence number. False when the queue
    /// is empty (the caller then re-sends the last event unchanged, for redundancy).</summary>
    public static bool TryDequeue(out byte endpoints, out byte seq)
    {
        endpoints = 0;
        seq = s_seq;
        if (s_queue.Count == 0)
            return false;
        endpoints = s_queue.Dequeue();
        seq = ++s_seq; // wraps at 255 — the receiver only compares for CHANGE
        return true;
    }

    /// <summary>Drop everything (module shutdown / hot reload) so a stale event never leaks into a
    /// new session.</summary>
    public static void Reset()
    {
        s_queue.Clear();
        s_loggedFirst = false;
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
