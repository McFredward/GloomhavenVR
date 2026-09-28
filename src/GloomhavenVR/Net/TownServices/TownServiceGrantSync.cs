using System;
using FFSNet;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Hard pre-native grant for a physical town offer. The native game still owns
/// purchases, donations and enhancements; this protocol only prevents two VR clients
/// from invoking their local native callbacks for the same resident concurrently.
/// Cosmetic TLV92 remains useful for presentation but cannot authorize gameplay.
///
/// The native host is the sole coordinator because Bolt's client-to-client relay through
/// a flat host is not proven. With an unmodded host, the immersive offer must restore its
/// original native window; inventing a local grant would permit split-brain purchases.
/// </summary>
internal static class TownServiceGrantSync
{
    private struct Offer
    {
        internal uint Session, Nonce, Epoch, RequestSequence;
        internal bool Active, Denied;
        internal float Started, LastRequest, LastResponse, LastGrant;
        internal uint[]? SentSequences;
        internal float[]? SentAt;
    }
    private struct VisibleGrant
    {
        internal int Player;
        internal uint Session, Nonce, Epoch;
        internal float LastSeen;
    }
    private static readonly Offer[] Offers = new Offer[4];
    private static readonly VisibleGrant[] Visible = new VisibleGrant[4];
    private static readonly TownServiceGrantLedger Ledger = new();
    private static INetTransport? _transport;
    private static uint _nextNonce = (uint)(DateTime.UtcNow.Ticks ^ Environment.TickCount);
    private static uint _epoch = NextNonce();
    private static float _nextSendFailureLog;
    private const float RequestInterval = .8f;
    private const float ClientGrantSeconds = 4f;
    private const float UnavailableSeconds = 3f;
    private const float VisibleSeconds = TownServiceGrantLedger.CoordinatorSeconds;

    private static uint NextNonce()
    { unchecked { _nextNonce++; if (_nextNonce == 0) _nextNonce++; return _nextNonce; } }
    private static int LocalPlayer => _transport?.LocalPlayerId ?? NetPlayerActors.LocalPlayerId();
    private static int HostPlayer => PlayerRegistry.HostPlayerID;
    private static bool Online => !NetSession.FlatNetMode && FFSNetwork.IsOnline;
    private static bool TransportReady => _transport is FfsNetTransport && _transport.IsOnline;
    private static bool Host => Online && LocalPlayer > 0 && LocalPlayer == HostPlayer;
    internal static bool CoordinatorReady => !FFSNetwork.IsOnline || !NetSession.FlatNetMode
        && TransportReady && (Host
        || HostPlayer > 0 && VersionGuard.PeerBuild(HostPlayer) == NetProtocol.ModBuild);
    internal static bool CanUseImmersive => CoordinatorReady;

    internal static void SetOffer(byte service, uint session, bool active)
    {
        if (service < 1 || service > 3) return;
        ref Offer offer = ref Offers[service];
        if (offer.Active && (!active || offer.Session != session))
        {
            Release(service, in offer);
            offer = default;
        }
        if (!active || session == 0 || offer.Active) return;
        offer = new Offer { Active = true, Session = session, Nonce = NextNonce(),
            Started = Time.unscaledTime, LastRequest = float.NegativeInfinity,
            SentSequences = new uint[8], SentAt = new float[8] };
        if (Host && Ledger.Request(service, LocalPlayer, session, offer.Nonce, Time.unscaledTime))
        {
            offer.Epoch = _epoch; offer.LastGrant = Time.unscaledTime;
            offer.RequestSequence = 1;
            Broadcast(new TownGrantMessage(TownGrantKind.Grant, service, LocalPlayer,
                session, offer.Nonce, _epoch, offer.RequestSequence));
        }
        else if (Host)
        {
            // The host is also a visitor. A peer's lease must reject its local offer
            // like any other Busy reply, never route it through the flat fallback.
            offer.Denied = true;
            offer.LastResponse = Time.unscaledTime;
        }
    }

    internal static bool MayCommit(byte service, uint session)
    {
        if (service < 1 || service > 3 || session == 0) return false;
        if (!FFSNetwork.IsOnline) return true;
        if (NetSession.FlatNetMode) return false;
        Offer offer = Offers[service];
        if (!offer.Active || offer.Session != session || !CoordinatorReady) return false;
        float now = Time.unscaledTime;
        if (Host) return Ledger.Holds(service, LocalPlayer, session, offer.Nonce, now);
        return offer.Epoch != 0 && offer.LastGrant > 0f && now - offer.LastGrant < ClientGrantSeconds;
    }

    internal static bool Unavailable(byte service, uint session)
    {
        if (!FFSNetwork.IsOnline || service < 1 || service > 3) return false;
        if (NetSession.FlatNetMode) return true;
        Offer offer = Offers[service];
        if (!offer.Active || offer.Session != session || offer.Denied) return false;
        return !CoordinatorReady || offer.LastResponse <= 0f
            && Time.unscaledTime - offer.Started >= UnavailableSeconds;
    }

    /// <summary>A different visitor was granted this resident before this simultaneous
    /// drop. The caller should return its own parked item or purse, not reveal a native
    /// fallback which could bypass the still-active lease.</summary>
    internal static bool Denied(byte service, uint session) => service >= 1 && service <= 3
        && Offers[service].Active && Offers[service].Session == session && Offers[service].Denied;

    internal static int GrantedOwner(byte service)
    {
        if (!Online || service < 1 || service > 3) return 0;
        if (Host) return Ledger.Owner(service, Time.unscaledTime);
        VisibleGrant grant = Visible[service];
        return grant.LastSeen > 0f && Time.unscaledTime - grant.LastSeen < VisibleSeconds
            ? grant.Player : 0;
    }

    internal static void Tick(INetTransport transport, float now)
    {
        _transport = transport;
        if (!Online || LocalPlayer <= 0) return;
        for (byte service = 1; service <= 3; service++)
        {
            ref Offer offer = ref Offers[service];
            if (!offer.Active || now - offer.LastRequest < RequestInterval) continue;
            offer.LastRequest = now;
            unchecked { offer.RequestSequence++; if (offer.RequestSequence == 0) offer.RequestSequence++; }
            int requestSlot = (int)(offer.RequestSequence & 7);
            offer.SentSequences![requestSlot] = offer.RequestSequence;
            offer.SentAt![requestSlot] = now;
            if (Host)
            {
                if (Ledger.Request(service, LocalPlayer, offer.Session, offer.Nonce, now))
                {
                    offer.Epoch = _epoch; offer.LastGrant = now; offer.Denied = false;
                    Broadcast(new TownGrantMessage(TownGrantKind.Grant, service, LocalPlayer,
                        offer.Session, offer.Nonce, _epoch, offer.RequestSequence));
                }
                else
                {
                    offer.Denied = true;
                    offer.LastResponse = now;
                }
                continue;
            }
            if (!CoordinatorReady) continue;
            Send(new TownGrantMessage(TownGrantKind.Request, service, LocalPlayer,
                offer.Session, offer.Nonce, 0, offer.RequestSequence), hostOnly: true);
        }
    }

    internal static bool Receive(int sender, byte[] bytes, int length)
    {
        if (!TownServiceGrantCodec.TryRead(bytes, length, out TownGrantMessage message)
            || !Online || sender <= 0 || sender == LocalPlayer) return false;
        float now = Time.unscaledTime;
        if (message.Kind == TownGrantKind.Request
            || message.Kind == TownGrantKind.Release && Host && sender == message.Player)
        {
            if (!Host || sender != message.Player || VersionGuard.PeerBuild(sender) != NetProtocol.ModBuild)
                return true;
            if (message.Kind == TownGrantKind.Release)
            {
                // Session + nonce identify the exact offer even if its first grant was lost.
                Ledger.Release(message.Service, sender, message.Session, message.Nonce);
                Broadcast(new TownGrantMessage(TownGrantKind.Release, message.Service,
                    sender, message.Session, message.Nonce, _epoch, 0));
                return true;
            }
            bool granted = Ledger.Request(message.Service, sender, message.Session, message.Nonce, now);
            Broadcast(new TownGrantMessage(granted ? TownGrantKind.Grant : TownGrantKind.Busy,
                message.Service, sender, message.Session, message.Nonce, _epoch, message.Sequence));
            return true;
        }
        // Only the native host may author grants or denials. A broadcast grant is visible
        // to every observer, but only the named requester's matching nonce unlocks gameplay.
        if (sender != HostPlayer || VersionGuard.PeerBuild(sender) != NetProtocol.ModBuild) return true;
        if (message.Kind == TownGrantKind.Grant)
            Visible[message.Service] = new VisibleGrant { Player = message.Player,
                Session = message.Session, Nonce = message.Nonce, Epoch = message.Epoch, LastSeen = now };
        else if (message.Kind == TownGrantKind.Release)
        {
            VisibleGrant visible = Visible[message.Service];
            if (visible.Player == message.Player && visible.Session == message.Session
                && visible.Nonce == message.Nonce && visible.Epoch == message.Epoch)
                Visible[message.Service] = default;
        }
        if (message.Player != LocalPlayer) return true;
        ref Offer offer = ref Offers[message.Service];
        if (!offer.Active || offer.Session != message.Session || offer.Nonce != message.Nonce) return true;
        float sentAt = 0f;
        if (message.Kind != TownGrantKind.Release)
        {
            int slot = (int)(message.Sequence & 7);
            if (offer.SentSequences == null || offer.SentAt == null
                || offer.SentSequences[slot] != message.Sequence) return true;
            sentAt = offer.SentAt[slot];
        }
        offer.LastResponse = now;
        if (message.Kind == TownGrantKind.Grant)
        {
            if (offer.LastGrant > sentAt) return true; // an older reply cannot shorten a newer grant
            if (offer.Epoch != 0 && offer.Epoch != message.Epoch) offer.LastGrant = 0f;
            // The coordinator's eight-second clock started no later than this request.
            // Measuring our four-second validity from SEND, not receive, prevents a
            // delayed ReliableOrdered response from outliving the host's reservation.
            offer.Epoch = message.Epoch;
            offer.LastGrant = now - sentAt < ClientGrantSeconds ? sentAt : 0f;
            offer.Denied = false;
        }
        else if (message.Kind == TownGrantKind.Busy)
        {
            if (offer.LastGrant <= sentAt)
            { offer.LastGrant = 0f; offer.Denied = true; }
        }
        return true;
    }

    internal static void ForgetPeer(int player)
    {
        // Avatar staleness is not a native disconnect. A brief presentation gap can
        // forget this peer at the 3 s rig timeout while its last client grant is still
        // valid for 4 s. Revoking the coordinator lease here could grant somebody else
        // the NPC during that overlap. Let its 8 s coordinator deadline or an exact
        // offer Release retire it instead.
        for (int service = 1; service <= 3; service++)
            if (Visible[service].Player == player) Visible[service] = default;
        if (player == HostPlayer)
        {
            for (int service = 1; service <= 3; service++)
            { ref Offer offer = ref Offers[service]; offer.Epoch = 0; offer.LastGrant = 0f; offer.LastResponse = 0f; }
        }
    }

    internal static void Reset()
    {
        for (int service = 1; service <= 3; service++) Offers[service] = default;
        for (int service = 1; service <= 3; service++) Visible[service] = default;
        Ledger.Clear(); _transport = null; _epoch = NextNonce();
    }

    private static void Release(byte service, in Offer offer)
    {
        if (!Online || LocalPlayer <= 0) return;
        if (Host)
        {
            Ledger.Release(service, LocalPlayer, offer.Session, offer.Nonce);
            Broadcast(new TownGrantMessage(TownGrantKind.Release, service, LocalPlayer,
                offer.Session, offer.Nonce, _epoch, 0));
        }
        else
            Send(new TownGrantMessage(TownGrantKind.Release, service, LocalPlayer,
                offer.Session, offer.Nonce, offer.Epoch, 0), hostOnly: true);
    }

    private static void Broadcast(in TownGrantMessage message) => Send(in message, hostOnly: false);
    private static void Send(in TownGrantMessage message, bool hostOnly)
    {
        if (_transport is not FfsNetTransport ffs) return;
        byte[] bytes = TownServiceGrantCodec.Write(in message);
        if (!ffs.SendTownGrant(bytes, bytes.Length, hostOnly)
            && Time.unscaledTime >= _nextSendFailureLog)
        {
            _nextSendFailureLog = Time.unscaledTime + 10f;
            VRLog.Warn("Net", "Town-service transaction grant could not use ReliableOrdered side action.");
        }
    }
}
