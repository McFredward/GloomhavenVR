namespace GloomhavenVR.Net;

/// <summary>Owner-authored public hand-pick recesses. Pool provenance is identity:
/// the producer and consumer must independently require the existing public-face
/// permission. Private choices carry only anonymous occupancy or a tombstone.</summary>
internal sealed class PublicHandPickState
{
    internal const int HeaderBytes = 28;
    internal const int SeatBytes = 8;
    internal const int MaxPayloadBytes = HeaderBytes + 2 * SeatBytes;
    internal readonly uint Revision;
    internal readonly int ActorId, Round, ScenarioSeed;
    internal readonly ulong ScenarioKey;
    internal readonly byte Phase, Occupied, Named, BoardSlots;
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
        byte occupied, byte named, Seat left = default, Seat right = default, int scenarioSeed = 0, ulong scenarioKey = 0, byte boardSlots = byte.MaxValue)
    { Revision = revision; ActorId = actorId; Round = round; Phase = phase;
      Occupied = occupied; Named = named; Left = left; Right = right;
      ScenarioSeed = scenarioSeed; ScenarioKey = scenarioKey;
      BoardSlots = boardSlots == byte.MaxValue ? occupied : boardSlots; }

    internal bool Valid => Revision != 0 && Round >= 0 && (Occupied & ~3) == 0 && (BoardSlots & ~3) == 0 && (Occupied & ~BoardSlots) == 0
        && (Named & ~Occupied) == 0 && (ActorId != 0 || Occupied == 0)
        && (Named == 0 || Phase != 0 && ActorId != 0)
        && ((Named & 1) != 0 ? Left.Valid : Left.Empty)
        && ((Named & 2) != 0 ? Right.Valid : Right.Empty);
    internal bool MatchesScenario(int seed, ulong key) => ScenarioKey != 0 && key != 0
        && ScenarioSeed == seed && ScenarioKey == key;
    /// <summary>Deterministic namespace, not a unique run/connection epoch. Exact same save
    /// seed+ID replay is deliberately indistinguishable; local lifetime/order still apply.</summary>
    internal static ulong ScenarioFingerprint(string? id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        ulong value = 14695981039346656037UL;
        foreach (char c in id!) { value = unchecked((value ^ (byte)c) * 1099511628211UL);
            value = unchecked((value ^ (byte)(c >> 8)) * 1099511628211UL); }
        return value == 0 ? 1UL : value;
    }
    internal int PayloadBytes => HeaderBytes + ((Named & 1) != 0 ? SeatBytes : 0)
        + ((Named & 2) != 0 ? SeatBytes : 0);
    internal bool SamePicture(PublicHandPickState? other) => other != null
        && ActorId == other.ActorId && Round == other.Round && Phase == other.Phase
        && ScenarioSeed == other.ScenarioSeed && ScenarioKey == other.ScenarioKey
        && Occupied == other.Occupied && Named == other.Named && BoardSlots == other.BoardSlots
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
        buffer[at++] = Phase; buffer[at++] = Occupied; buffer[at++] = Named; buffer[at++] = BoardSlots;
        AvatarSerializer.WriteU32(buffer, ref at, unchecked((uint)ScenarioSeed));
        AvatarSerializer.WriteU32(buffer, ref at, (uint)ScenarioKey);
        AvatarSerializer.WriteU32(buffer, ref at, (uint)(ScenarioKey >> 32));
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
        byte phase = buffer[at++], occupied = buffer[at++], named = buffer[at++], boardSlots = buffer[at++];
        int seed = unchecked((int)AvatarSerializer.ReadU32(buffer, ref at));
        ulong key = AvatarSerializer.ReadU32(buffer, ref at);
        key |= (ulong)AvatarSerializer.ReadU32(buffer, ref at) << 32;
        int wanted = HeaderBytes + ((named & 1) != 0 ? SeatBytes : 0) + ((named & 2) != 0 ? SeatBytes : 0);
        if (length != wanted || (named & ~3) != 0) return false;
        Seat left = (named & 1) != 0 ? ReadSeat(buffer, ref at) : default;
        Seat right = (named & 2) != 0 ? ReadSeat(buffer, ref at) : default;
        var candidate = new PublicHandPickState(revision, actor, round, phase, occupied, named, left, right, seed, key, boardSlots);
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
    internal void Observe(bool seen, PublicHandPickState? incoming, object? scenario, int seed = 0, ulong key = 0)
    {
        if (!ReferenceEquals(_scenario, scenario))
        { _scenario = scenario; _state = null; }
        if (!seen) return;
        if (incoming == null || !incoming.Valid) { _state = null; return; }
        // Keep the accepted sequence floor across native scenario replacement.
        // Together with the native namespace check this rejects a previously accepted old picture.
        // A replay with identical seed+ID is not a new namespace; normal packet order still applies.
        if (_revision != 0 && !PublicHandPickState.Newer(incoming.Revision, _revision)) return;
        if (incoming.ActorId != 0 && !incoming.MatchesScenario(seed, key))
        { _state = null; return; }
        _revision = incoming.Revision; _state = incoming;
    }
    internal PublicHandPickState? Current(object? scenario) => ReferenceEquals(_scenario, scenario) ? _state : null;
}
