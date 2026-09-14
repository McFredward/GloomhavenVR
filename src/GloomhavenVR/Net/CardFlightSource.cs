namespace GloomhavenVR.Net;

/// <summary>Public actor and positional source, captured before a card leaves its population.
/// Count zero carries only the actor; the semantic endpoint already identifies the source area.
/// Discard-to-hand recovery addresses the destination's native HandAbilityCards list after
/// restoration; its semantic endpoints distinguish that address from an active source cell.
/// No card identity is transmitted.</summary>
internal readonly struct CardFlightSource
{
    internal CardFlightSource(int actorId, byte seat, byte count)
    { ActorId = actorId; Seat = seat; Count = count; }
    internal int ActorId { get; }
    internal byte Seat { get; }
    internal byte Count { get; }
    internal bool Validate() => ActorId != 0 && (Count == 0 ? Seat == 0 : Seat < Count);
}
