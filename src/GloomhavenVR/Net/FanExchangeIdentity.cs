namespace GloomhavenVR.Net;

/// <summary>Motion identity of a peer's open hand. Scenario actor ids and map-room character
/// keys belong to different domains; comparing them must not manufacture a cross-phase exchange.
/// Unknown identities and newly raised fans establish a baseline without inventing a switch.</summary>
internal readonly struct FanExchangeIdentity
{
    private readonly bool _map;
    private readonly int _actorId;
    private readonly uint _characterKey;

    private FanExchangeIdentity(bool map, int actorId, uint characterKey)
    {
        _map = map;
        _actorId = actorId;
        _characterKey = characterKey;
    }

    internal static FanExchangeIdentity Scenario(int actorId) => new(false, actorId, 0);
    internal static FanExchangeIdentity Map(uint characterKey) => new(true, 0, characterKey);

    internal bool SameAs(FanExchangeIdentity next)
        => _map == next._map && _actorId == next._actorId && _characterKey == next._characterKey;

    internal bool ShouldExchangeTo(FanExchangeIdentity next, bool visible, int outgoing, int incoming)
    {
        if (!visible || (outgoing <= 0 && incoming <= 0) || _map != next._map)
            return false;
        return _map
            ? _characterKey != 0 && next._characterKey != 0 && _characterKey != next._characterKey
            : _actorId != 0 && next._actorId != 0 && _actorId != next._actorId;
    }
}
