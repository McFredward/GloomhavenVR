namespace GloomhavenVR.Net.TownServices;

/// <summary>Authoritative per-resident reservations. Only a single elected coordinator
/// mutates this ledger. A requester may continue native gameplay only while its matching
/// grant is fresh; another resident's reservation never participates in this decision.</summary>
internal sealed class TownServiceGrantLedger
{
    private struct Entry
    {
        internal int Player;
        internal uint Session, Nonce;
        internal float Expires;
    }
    private readonly Entry[] _entries = new Entry[4];
    internal const float CoordinatorSeconds = 8f;
    internal bool Request(byte service, int player, uint session, uint nonce, float now)
    {
        if (service < 1 || service > 3 || player <= 0 || session == 0 || nonce == 0) return false;
        ref Entry entry = ref _entries[service];
        if (entry.Player != 0 && entry.Expires <= now) entry = default;
        if (entry.Player != 0 && (entry.Player != player || entry.Session != session || entry.Nonce != nonce)) return false;
        entry.Player = player; entry.Session = session; entry.Nonce = nonce;
        entry.Expires = now + CoordinatorSeconds;
        return true;
    }
    internal bool Holds(byte service, int player, uint session, uint nonce, float now)
    {
        if (service < 1 || service > 3) return false;
        Entry entry = _entries[service];
        return entry.Player == player && entry.Session == session && entry.Nonce == nonce && entry.Expires > now;
    }
    internal int Owner(byte service, float now)
    {
        if (service < 1 || service > 3) return 0;
        Entry entry = _entries[service];
        return entry.Expires > now ? entry.Player : 0;
    }
    internal bool TryOwner(byte service, float now, out int player, out uint session)
    {
        player = 0; session = 0;
        if (service < 1 || service > 3) return false;
        Entry entry = _entries[service];
        if (entry.Player <= 0 || entry.Expires <= now) return false;
        player = entry.Player; session = entry.Session;
        return true;
    }
    internal void Release(byte service, int player, uint session, uint nonce)
    {
        if (service < 1 || service > 3) return;
        Entry entry = _entries[service];
        if (entry.Player == player && entry.Session == session && entry.Nonce == nonce) _entries[service] = default;
    }
    internal void ForgetPeer(int player)
    { for (int service = 1; service <= 3; service++) if (_entries[service].Player == player) _entries[service] = default; }
    internal void Clear()
    { for (int service = 1; service <= 3; service++) _entries[service] = default; }
}
