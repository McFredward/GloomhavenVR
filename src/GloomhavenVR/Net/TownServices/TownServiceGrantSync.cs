using System;
using System.Collections.Generic;
using FFSNet;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Hard pre-native grant for a physical town offer. The native game still owns
/// purchases, donations and enhancements; this protocol only prevents two VR clients
/// from invoking their local native callbacks for the same resident concurrently.
/// Cosmetic TLV92 remains useful for presentation but cannot authorize gameplay.
///
/// Prefer a compatible native host, otherwise elect the lowest compatible live player ID.
/// The shipped Bolt EventDispatcher.Global_Others forwards received reliable events to
/// every other connection before vanilla ignores the sentinel, including at a flat host.
/// A new coordinator waits out the four-second client permission window before granting;
/// election and native gameplay authority remain separate. Native purchase callbacks and
/// the flat host still own every purchase, donation and enhancement.
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
    private static readonly List<int> CompatiblePeers = new();
    private static int _coordinatorPlayer;
    private static float _coordinatorNotBefore;
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
    private static bool Coordinator => Online && LocalPlayer > 0 && LocalPlayer == _coordinatorPlayer;
    internal static bool CoordinatorReady
    {
        get
        {
            if (!FFSNetwork.IsOnline) return true;
            if (!Online || !TransportReady || LocalPlayer <= 0) return false;
            RefreshCoordinator(Time.unscaledTime);
            return _coordinatorPlayer > 0;
        }
    }
    internal static bool CanUseImmersive => CoordinatorReady;

    private static void RefreshCoordinator(float now)
    {
        // The ordinary version census is backed by the native connected-player roster.
        // Its compatible membership survives avatar staleness and excludes departed
        // players even though their build record is retained until the session ends.
        VersionGuard.CollectContinuationPeers(CompatiblePeers, LocalPlayer);
        int selected = LocalPlayer;
        for (int i = 0; i < CompatiblePeers.Count; i++)
        {
            int peer = CompatiblePeers[i];
            if (peer == HostPlayer) { selected = peer; break; }
            if (selected != HostPlayer && peer > 0 && peer < selected) selected = peer;
        }
        if (selected == _coordinatorPlayer) return;
        bool first = _coordinatorPlayer == 0;
        _coordinatorPlayer = selected;
        // Initial native-host operation keeps its existing immediate path. A guest
        // coordinator settles discovery, and every handoff drains old client grants.
        // Keep native originals visible during this interval: it only gates commit.
        _coordinatorNotBefore = first && selected == HostPlayer ? now : now + ClientGrantSeconds;
        VRLog.Note("Net", $"Town-service coordinator: player {selected}, native host {HostPlayer}; "
            + $"client permission drain {_coordinatorNotBefore - now:0.0}s. Native gameplay authority is unchanged.");
        Ledger.Clear(); _epoch = NextNonce();
        for (int service = 1; service <= 3; service++)
        {
            Visible[service] = default;
            ref Offer offer = ref Offers[service];
            offer.Epoch = 0; offer.LastGrant = offer.LastResponse = 0f; offer.Denied = false;
            offer.LastRequest = float.NegativeInfinity;
            offer.Started = _coordinatorNotBefore;
            // Retain the physical offer's session/nonce, but retire every old request
            // slot. A delayed response from a former coordinator cannot unlock it.
            if (offer.SentSequences != null) Array.Clear(offer.SentSequences, 0, offer.SentSequences.Length);
            if (offer.SentAt != null) Array.Clear(offer.SentAt, 0, offer.SentAt.Length);
        }
    }

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
        if (CoordinatorReady && Coordinator && Time.unscaledTime >= _coordinatorNotBefore && Ledger.Request(service, LocalPlayer, session, offer.Nonce, Time.unscaledTime))
        {
            // The coordinator's own ledger decision is a response too. Leaving LastResponse
            // unset made Unavailable declare a live self-grant dead three seconds
            // later, replacing the immersive merchant with the flat shop and
            // reclaiming the card in his hand. The same timeout affected offerings
            // to the other two residents when the host was their visitor.
            offer.Epoch = _epoch; offer.LastGrant = offer.LastResponse = Time.unscaledTime;
            offer.RequestSequence = 1;
            Broadcast(new TownGrantMessage(TownGrantKind.Grant, service, LocalPlayer,
                session, offer.Nonce, _epoch, offer.RequestSequence));
        }
        else if (Coordinator && Time.unscaledTime >= _coordinatorNotBefore)
        {
            // The coordinator is also a visitor. A peer's lease must reject its local offer
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
        if (now < _coordinatorNotBefore) return false;
        if (Coordinator) return Ledger.Holds(service, LocalPlayer, session, offer.Nonce, now);
        return offer.Epoch != 0 && offer.LastGrant > 0f && now - offer.LastGrant < ClientGrantSeconds;
    }

    internal static bool Unavailable(byte service, uint session)
    {
        if (!FFSNetwork.IsOnline || service < 1 || service > 3) return false;
        if (NetSession.FlatNetMode) return true;
        Offer offer = Offers[service];
        if (!offer.Active || offer.Session != session || offer.Denied) return false;
        // A currently valid grant is authoritative even if a transport reset or an
        // older local offer left the diagnostic response clock at its zero sentinel.
        if (MayCommit(service, session)) return false;
        if (CoordinatorReady && Time.unscaledTime < _coordinatorNotBefore) return false;
        return !CoordinatorReady || offer.LastResponse <= 0f
            && Time.unscaledTime - Math.Max(offer.Started, _coordinatorNotBefore) >= UnavailableSeconds;
    }

    /// <summary>A different visitor was granted this resident before this simultaneous
    /// drop. The caller should return its own parked item or purse, not reveal a native
    /// fallback which could bypass the still-active lease.</summary>
    internal static bool Denied(byte service, uint session) => service >= 1 && service <= 3
        && Offers[service].Active && Offers[service].Session == session && Offers[service].Denied;

    internal static int GrantedOwner(byte service)
    {
        // Temple grants are a brief original-callback commit mutex, never an NPC
        // occupation. They must not hide another visitor's purse, bowl or cue.
        if (!Online || service != 1 && service != 3) return 0;
        if (!CoordinatorReady) return 0;
        if (Coordinator) return Ledger.Owner(service, Time.unscaledTime);
        VisibleGrant grant = Visible[service];
        return VisibleOfferCurrent(service, in grant) ? grant.Player : 0;
    }
    internal static bool TryGrantedOwner(byte service, out int player, out uint session)
    {
        player = 0; session = 0;
        if (!Online || service != 1 && service != 3) return false;
        if (!CoordinatorReady) return false;
        if (Coordinator) return Ledger.TryOwner(service, Time.unscaledTime, out player, out session);
        VisibleGrant grant = Visible[service];
        if (!VisibleOfferCurrent(service, in grant)) return false;
        player = grant.Player; session = grant.Session;
        return true;
    }

    private static bool VisibleOfferCurrent(byte service, in VisibleGrant grant)
    {
        if (grant.Player <= 0 || grant.Session == 0 || grant.LastSeen <= 0f
            || Time.unscaledTime - grant.LastSeen >= VisibleSeconds) return false;
        if (grant.Player != LocalPlayer) return true;
        Offer own = Offers[service];
        // A coordinator response already in flight can arrive after this client's physical
        // return. It may remain useful reliable history, but it cannot resurrect
        // that removed card's local identity or overwrite a replacement offer.
        return own.Active && own.Session == grant.Session && own.Nonce == grant.Nonce;
    }

    internal static void Tick(INetTransport transport, float now)
    {
        _transport = transport;
        if (!Online || LocalPlayer <= 0 || !TransportReady) return;
        RefreshCoordinator(now);
        for (byte service = 1; service <= 3; service++)
        {
            ref Offer offer = ref Offers[service];
            if (!offer.Active || now - offer.LastRequest < RequestInterval) continue;
            offer.LastRequest = now;
            unchecked { offer.RequestSequence++; if (offer.RequestSequence == 0) offer.RequestSequence++; }
            int requestSlot = (int)(offer.RequestSequence & 7);
            offer.SentSequences![requestSlot] = offer.RequestSequence;
            offer.SentAt![requestSlot] = now;
            if (Coordinator)
            {
                if (now < _coordinatorNotBefore) continue;
                if (Ledger.Request(service, LocalPlayer, offer.Session, offer.Nonce, now))
                {
                    offer.Epoch = _epoch; offer.LastGrant = offer.LastResponse = now; offer.Denied = false;
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
                offer.Session, offer.Nonce, 0, offer.RequestSequence), hostOnly: _coordinatorPlayer == HostPlayer);
        }
    }

    internal static bool Receive(int sender, byte[] bytes, int length)
    {
        if (!TownServiceGrantCodec.TryRead(bytes, length, out TownGrantMessage message)
            || !Online || sender <= 0 || sender == LocalPlayer) return false;
        float now = Time.unscaledTime;
        if (!CoordinatorReady) return true;
        if (message.Kind == TownGrantKind.Request
            || message.Kind == TownGrantKind.Release && Coordinator && sender == message.Player)
        {
            if (!Coordinator || sender != message.Player || !CompatiblePeers.Contains(sender)
                || VersionGuard.PeerBuild(sender) != NetProtocol.ModBuild)
                return true;
            if (message.Kind == TownGrantKind.Release)
            {
                // Session + nonce identify the exact offer even if its first grant was lost.
                Ledger.Release(message.Service, sender, message.Session, message.Nonce);
                Broadcast(new TownGrantMessage(TownGrantKind.Release, message.Service,
                    sender, message.Session, message.Nonce, _epoch, 0));
                return true;
            }
            if (now < _coordinatorNotBefore) return true;
            bool granted = Ledger.Request(message.Service, sender, message.Session, message.Nonce, now);
            Broadcast(new TownGrantMessage(granted ? TownGrantKind.Grant : TownGrantKind.Busy,
                message.Service, sender, message.Session, message.Nonce, _epoch, message.Sequence));
            return true;
        }
        // Only the elected compatible coordinator may author grants or denials. A
        // broadcast grant is public; exact requester identity still gates gameplay.
        if (sender != _coordinatorPlayer || VersionGuard.PeerBuild(sender) != NetProtocol.ModBuild
            || now < _coordinatorNotBefore) return true;
        if (message.Kind == TownGrantKind.Grant && message.Player == LocalPlayer)
        {
            Offer current = Offers[message.Service];
            if (current.Active && current.Session == message.Session && current.Nonce == message.Nonce)
            {
                int slot = (int)(message.Sequence & 7);
                // A former coordinator ID can later become elected again. Its old
                // reply must not recreate visible occupation from a retired request
                // slot, even though the parked physical offer keeps its exact nonce.
                if (current.SentSequences == null || current.SentSequences[slot] != message.Sequence) return true;
            }
        }
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
            // delayed ReliableOrdered response from outliving the coordinator's reservation.
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
        // An avatar timeout must not elect a replacement coordinator or revoke a
        // live lease. RefreshCoordinator observes native roster departure instead.
    }

    internal static void Reset()
    {
        for (int service = 1; service <= 3; service++) Offers[service] = default;
        for (int service = 1; service <= 3; service++) Visible[service] = default;
        Ledger.Clear(); _transport = null; _epoch = NextNonce();
        CompatiblePeers.Clear(); _coordinatorPlayer = 0; _coordinatorNotBefore = 0f;
    }

    private static void Release(byte service, in Offer offer)
    {
        if (!Online || LocalPlayer <= 0) return;
        VisibleGrant visible = Visible[service];
        if (visible.Player == LocalPlayer && visible.Session == offer.Session
            && visible.Nonce == offer.Nonce)
            Visible[service] = default;
        if (CoordinatorReady && Coordinator)
        {
            Ledger.Release(service, LocalPlayer, offer.Session, offer.Nonce);
            Broadcast(new TownGrantMessage(TownGrantKind.Release, service, LocalPlayer,
                offer.Session, offer.Nonce, _epoch, 0));
        }
        else
            Send(new TownGrantMessage(TownGrantKind.Release, service, LocalPlayer,
                offer.Session, offer.Nonce, offer.Epoch, 0), hostOnly: _coordinatorPlayer == HostPlayer);
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
