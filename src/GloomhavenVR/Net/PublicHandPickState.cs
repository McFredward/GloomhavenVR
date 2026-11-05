namespace GloomhavenVR.Net;

/// <summary>Owner-authored public hand-pick recesses. Pool provenance is identity:
/// the producer and consumer must independently require the existing public-face
/// permission. Private choices carry only anonymous occupancy or a tombstone.</summary>
internal sealed class PublicHandPickState
{
    internal const int HeaderBytes = 15;
    internal const int SeatBytes = 8;
    internal const int MaxPayloadBytes = HeaderBytes + 2 * SeatBytes;
    internal readonly uint Revision;
    internal readonly int ActorId, Round;
    internal readonly byte Phase, Occupied, Named;
    internal readonly Seat Left, Right;

    internal readonly struct Seat
    {
        internal readonly int SourceActorId;
        internal readonly ushort PoolSeat, PoolCount;
        internal Seat(int sourceActorId, ushort poolSeat, ushort poolCount)
        { SourceActorId = sourceActorId; PoolSeat = poolSeat; PoolCount = poolCount; }
        internal bool Empty => SourceActorId == 0 && PoolSeat == 0 && PoolCount == 0;
        internal bool Valid => SourceActorId != 0 && PoolCount > 0 && PoolCount <= 32768
            && (PoolSeat & 32767) < PoolCount;
        internal bool Same(in Seat other) => SourceActorId == other.SourceActorId
            && PoolSeat == other.PoolSeat && PoolCount == other.PoolCount;
    }

    internal PublicHandPickState(uint revision, int actorId, int round, byte phase,
        byte occupied, byte named, Seat left = default, Seat right = default)
    { Revision = revision; ActorId = actorId; Round = round; Phase = phase;
      Occupied = occupied; Named = named; Left = left; Right = right; }

    internal bool Valid => Revision != 0 && Round >= 0 && (Occupied & ~3) == 0
        && (Named & ~Occupied) == 0 && (ActorId != 0 || Occupied == 0)
        && (Named == 0 || Phase != 0 && ActorId != 0)
        && ((Named & 1) != 0 ? Left.Valid : Left.Empty)
        && ((Named & 2) != 0 ? Right.Valid : Right.Empty);
    internal int PayloadBytes => HeaderBytes + ((Named & 1) != 0 ? SeatBytes : 0)
        + ((Named & 2) != 0 ? SeatBytes : 0);
    internal bool SamePicture(PublicHandPickState? other) => other != null
        && ActorId == other.ActorId && Round == other.Round && Phase == other.Phase
        && Occupied == other.Occupied && Named == other.Named
        && Left.Same(in other.Left) && Right.Same(in other.Right);
    internal static bool Newer(uint incoming, uint current) => incoming != current
        && unchecked(incoming - current) < 0x80000000u;

    internal int Write(byte[] buffer, int at)
    {
        if (!Valid || at < 0 || at > buffer.Length - PayloadBytes) return 0;
        int start = at;
        AvatarSerializer.WriteU32(buffer, ref at, Revision);
        AvatarSerializer.WriteU32(buffer, ref at, unchecked((uint)ActorId));
        AvatarSerializer.WriteU32(buffer, ref at, (uint)Round);
        buffer[at++] = Phase; buffer[at++] = Occupied; buffer[at++] = Named;
        if ((Named & 1) != 0) WriteSeat(buffer, ref at, in Left);
        if ((Named & 2) != 0) WriteSeat(buffer, ref at, in Right);
        return at - start;
    }

    private static void WriteSeat(byte[] buffer, ref int at, in Seat seat)
    {
        AvatarSerializer.WriteU32(buffer, ref at, unchecked((uint)seat.SourceActorId));
        AvatarSerializer.WriteU32(buffer, ref at, seat.PoolSeat | (uint)seat.PoolCount << 16);
    }

    internal static bool TryRead(byte[] buffer, int at, int length, out PublicHandPickState? state)
    {
        state = null;
        if (at < 0 || length < HeaderBytes || length > MaxPayloadBytes || at > buffer.Length - length) return false;
        int end = at + length;
        uint revision = AvatarSerializer.ReadU32(buffer, ref at);
        int actor = unchecked((int)AvatarSerializer.ReadU32(buffer, ref at));
        int round = unchecked((int)AvatarSerializer.ReadU32(buffer, ref at));
        byte phase = buffer[at++], occupied = buffer[at++], named = buffer[at++];
        int wanted = HeaderBytes + ((named & 1) != 0 ? SeatBytes : 0) + ((named & 2) != 0 ? SeatBytes : 0);
        if (length != wanted || (named & ~3) != 0) return false;
        Seat left = (named & 1) != 0 ? ReadSeat(buffer, ref at) : default;
        Seat right = (named & 2) != 0 ? ReadSeat(buffer, ref at) : default;
        var candidate = new PublicHandPickState(revision, actor, round, phase, occupied, named, left, right);
        if (at != end || !candidate.Valid) return false;
        state = candidate; return true;
    }

    private static Seat ReadSeat(byte[] buffer, ref int at)
    {
        int actor = unchecked((int)AvatarSerializer.ReadU32(buffer, ref at));
        uint pool = AvatarSerializer.ReadU32(buffer, ref at);
        return new Seat(actor, (ushort)pool, (ushort)(pool >> 16));
    }
}

/// <summary>Per-peer ordered receipt. Native scenario lifetime is an explicit
/// owner token, not transmitted state; a reconnect constructs a fresh inbox.</summary>
internal sealed class PublicHandPickInbox
{
    private object? _scenario;
    private uint _revision;
    private PublicHandPickState? _state;
    internal void Observe(bool seen, PublicHandPickState? incoming, object? scenario)
    {
        if (!ReferenceEquals(_scenario, scenario))
        { _scenario = scenario; _state = null; }
        if (!seen) return;
        if (incoming == null || !incoming.Valid) { _state = null; return; }
        // Keep the accepted sequence floor across native scenario replacement.
        // A delayed packet from its previous scenario cannot become its first picture.
        if (_revision != 0 && !PublicHandPickState.Newer(incoming.Revision, _revision)) return;
        _revision = incoming.Revision; _state = incoming;
    }
    internal PublicHandPickState? Current(object? scenario) => ReferenceEquals(_scenario, scenario) ? _state : null;
}
