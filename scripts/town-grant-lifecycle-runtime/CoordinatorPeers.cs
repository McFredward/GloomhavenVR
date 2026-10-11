using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

// Each assembly context has a distinct production GrantSync/ledger/codec and
// explicit native roster/version/clock/transport ports. Bytes cross the normal
// codec Receive seam; no simulated implementation of the grant algorithm exists.
internal static class CoordinatorPeerPort
{
    private static FfsNetTransport _transport = new();
    public static void Initialize(int local, int host, int[] roster, int[] compatible, float now)
    {
        TownServiceGrantSync.Reset(); GloomhavenVR.Core.VRLog.CoordinatorReports = 0; PlayerRegistry.HostPlayerID = host;
        NetPlayerActors.Peer = local; _transport = new FfsNetTransport { Peer = local };
        FFSNet.FFSNetwork.IsOnline = true; NetSession.FlatNetMode = false;
        Membership(roster, compatible); Tick(now);
    }
    public static void Membership(int[] roster, int[] compatible)
    {
        VersionGuard.Roster.Clear(); VersionGuard.Roster.AddRange(roster);
        VersionGuard.Compatible.Clear(); foreach (int id in compatible) VersionGuard.Compatible.Add(id);
    }
    public static void Tick(float now) { Time.unscaledTime = now; TownServiceGrantSync.Tick(_transport, now); }
    public static void Begin(byte service, uint session) => TownServiceGrantSync.SetOffer(service, session, true);
    public static void End(byte service, uint session) => TownServiceGrantSync.SetOffer(service, session, false);
    public static bool Commit(byte service, uint session) => TownServiceGrantSync.MayCommit(service, session);
    public static bool Unavailable(byte service, uint session) => TownServiceGrantSync.Unavailable(service, session);
    public static bool Denied(byte service, uint session) => TownServiceGrantSync.Denied(service, session);
    public static bool Immersive() => TownServiceGrantSync.CanUseImmersive;
    public static int Reports() => GloomhavenVR.Core.VRLog.CoordinatorReports;
    public static int Owner(byte service) => TownServiceGrantSync.GrantedOwner(service);
    public static void Forget(int peer) => TownServiceGrantSync.ForgetPeer(peer);
    public static void Deliver(int sender, byte[] bytes) => TownServiceGrantSync.Receive(sender, bytes, bytes.Length);
    public static byte[][] Pull() { byte[][] packets = _transport.Packets.ToArray(); _transport.Packets.Clear(); return packets; }
}

internal static class CoordinatorTests
{
    private sealed class Peer : IDisposable
    {
        private readonly AssemblyLoadContext _context;
        private readonly Type _port;
        internal readonly int Id;
        internal Peer(int id, int host, int[] roster, int[] compatible, float now = 1f)
        {
            Id = id; _context = new AssemblyLoadContext("grant-peer-" + id, isCollectible: true);
            _port = _context.LoadFromAssemblyPath(Assembly.GetExecutingAssembly().Location).GetType(nameof(CoordinatorPeerPort))!;
            Call(nameof(CoordinatorPeerPort.Initialize), id, host, roster, compatible, now);
        }
        internal object? Call(string method, params object[] args) => _port.GetMethod(method)!.Invoke(null, args);
        internal T Read<T>(string method, params object[] args) => (T)Call(method, args)!;
        internal void Tick(float now) => Call(nameof(CoordinatorPeerPort.Tick), now);
        internal void Begin(byte service, uint session) => Call(nameof(CoordinatorPeerPort.Begin), service, session);
        internal void End(byte service, uint session) => Call(nameof(CoordinatorPeerPort.End), service, session);
        internal bool Commit(byte service, uint session) => Read<bool>(nameof(CoordinatorPeerPort.Commit), service, session);
        internal bool Unavailable(byte service, uint session) => Read<bool>(nameof(CoordinatorPeerPort.Unavailable), service, session);
        internal bool Denied(byte service, uint session) => Read<bool>(nameof(CoordinatorPeerPort.Denied), service, session);
        internal int Owner(byte service) => Read<int>(nameof(CoordinatorPeerPort.Owner), service);
        internal void Membership(int[] roster, int[] compatible) => Call(nameof(CoordinatorPeerPort.Membership), roster, compatible);
        internal byte[][] Pull() => Read<byte[][]>(nameof(CoordinatorPeerPort.Pull));
        internal void Deliver(int sender, byte[] bytes) => Call(nameof(CoordinatorPeerPort.Deliver), sender, bytes);
        public void Dispose() => _context.Unload();
    }
    private sealed class World : IDisposable
    {
        internal readonly List<Peer> Peers = new();
        internal int FlatIgnored, ServerOnly;
        internal World(params int[] guests)
        {
            var roster = new int[guests.Length + 1]; roster[0] = 1;
            Array.Copy(guests, 0, roster, 1, guests.Length);
            foreach (int guest in guests) Peers.Add(new Peer(guest, 1, roster, guests));
        }
        internal void Tick(float now) { foreach (Peer peer in Peers) peer.Tick(now); Pump(); }
        internal void Pump()
        {
            for (int round = 0; round < 20; round++)
            {
                bool sent = false;
                foreach (Peer source in Peers)
                {
                    foreach (byte[] packet in source.Pull())
                    {
                        sent = true; FlatIgnored++;
                        // Mirrors shipped Global_Others: vanilla host forwards to other
                        // connections before ignoring the nonplayer sentinel locally.
                        if (packet[0] == 1) { ServerOnly++; continue; } // OnlyServer never reaches VR guests.
                        byte[] bytes = new byte[packet.Length - 1]; Array.Copy(packet, 1, bytes, 0, bytes.Length);
                        foreach (Peer target in Peers) if (target.Id != source.Id) target.Deliver(source.Id, bytes);
                    }
                }
                if (!sent) return;
            }
            throw new Exception("reliable grant delivery did not settle within its bounded message rounds");
        }
        public void Dispose() { foreach (Peer peer in Peers) peer.Dispose(); }
    }
    internal static void Run(Action<bool, string> check)
    {
        foreach (int count in new[] { 1, 2, 3 })
        {
            var guests = new int[count]; for (int i = 0; i < count; i++) guests[i] = i + 2;
            using var world = new World(guests);
            foreach (Peer peer in world.Peers)
            {
                check(peer.Read<bool>(nameof(CoordinatorPeerPort.Immersive)), "flat host never disables immersive native originals");
                peer.Begin(1, (uint)(100 + peer.Id));
            }
            world.Tick(4.1f);
            check(world.ServerOnly == 0, "flat-host requests never disappear into native OnlyServer delivery");
            foreach (Peer peer in world.Peers)
            {
                check(!peer.Commit(1, (uint)(100 + peer.Id)), "guest coordinator discovery drain blocks native commit");
                check(!peer.Unavailable(1, (uint)(100 + peer.Id)), "discovery drain never tears down a parked native offer");
            }
            world.Tick(5.1f);
            check(world.Peers[0].Commit(1, 102), "one two or three VR guests with flat host receive a real elected grant");
            foreach (Peer peer in world.Peers)
            {
                check(peer.Owner(1) == 2, "all compatible observers agree on elected coordinator's exact grant");
                if (peer.Id != 2)
                    check(!peer.Commit(1, (uint)(100 + peer.Id)) && peer.Denied(1, (uint)(100 + peer.Id)),
                        "simultaneous VR drops never authorize two native merchant callbacks");
            }
            world.Tick(20f);
            check(world.Peers[0].Commit(1, 102) && !world.Peers[0].Unavailable(1, 102),
                "nonhost coordinator self-grant renews without the flat host responding");
            check(world.FlatIgnored > 0, "unmodified host only routes and ignores sentinel grants");
            foreach (Peer peer in world.Peers)
                check(peer.Read<int>(nameof(CoordinatorPeerPort.Reports)) == 1,
                    "stable roster keeps one coordinator lifecycle report without per-frame reelection");
            if (count > 1)
            {
                Peer next = world.Peers[1]; world.Peers[0].End(1, 102); world.Pump();
                world.Tick(21f);
                check(next.Commit(1, 103), "exact elected release permits another VR visitor through the same native mutex");
                next.Call(nameof(CoordinatorPeerPort.Forget), 2);
                check(next.Commit(1, 103), "avatar staleness does not revoke current grant or elect a replacement");
                world.Tick(22f);
                check(next.Commit(1, 103), "avatar-stale coordinator remains authoritative while native roster is connected");
                // Capture a genuine response for the held offer before coordinator loss.
                next.Tick(23f);
                var requestPacket = next.Pull()[0]; var requestBytes = new byte[requestPacket.Length - 1];
                Array.Copy(requestPacket, 1, requestBytes, 0, requestBytes.Length);
                world.Peers[0].Deliver(3, requestBytes);
                var oldResponse = world.Peers[0].Pull()[0]; var oldBytes = new byte[oldResponse.Length - 1];
                Array.Copy(oldResponse, 1, oldBytes, 0, oldBytes.Length);
                var remaining = count == 2 ? new[] { 3 } : new[] { 3, 4 };
                var roster = count == 2 ? new[] { 1, 3 } : new[] { 1, 3, 4 };
                foreach (Peer peer in world.Peers) if (peer.Id != 2) { peer.Membership(roster, remaining); peer.Tick(23.1f); }
                check(!next.Commit(1, 103) && !next.Unavailable(1, 103), "coordinator departure revokes permissions without flat fallback");
                next.Deliver(2, oldBytes);
                check(!next.Commit(1, 103) && next.Owner(1) == 0, "former coordinator delayed epoch cannot revive permission or occupation");
                next.Tick(27.2f);
                check(next.Commit(1, 103), "replacement coordinator grants retained physical offer after old client leases drain");
                var fresh = next.Pull()[^1]; var freshBytes = new byte[fresh.Length - 1];
                Array.Copy(fresh, 1, freshBytes, 0, freshBytes.Length);
                TownServiceGrantCodec.TryRead(oldBytes, oldBytes.Length, out var oldGrant);
                TownServiceGrantCodec.TryRead(freshBytes, freshBytes.Length, out var newGrant);
                check(newGrant.Session == oldGrant.Session && newGrant.Nonce == oldGrant.Nonce
                    && newGrant.Epoch != oldGrant.Epoch, "handoff retains offer identity but replaces coordinator epoch");
                // The same coordinator ID can be elected again later. Genuine old
                // bytes still name this retained physical nonce, but their old sent
                // request slot must unlock neither gameplay nor visible occupation.
                next.Membership(new[] { 1, 2, 3 }, new[] { 2, 3 }); next.Tick(28.1f);
                next.Tick(32.2f); next.Deliver(2, oldBytes);
                check(!next.Commit(1, 103) && next.Owner(1) == 0,
                    "reelected coordinator old request slot cannot recreate visible occupation");
                var latestRequest = next.Pull()[^1]; var latestBytes = new byte[latestRequest.Length - 1];
                Array.Copy(latestRequest, 1, latestBytes, 0, latestBytes.Length);
                world.Peers[0].Tick(32.2f); world.Peers[0].Deliver(3, latestBytes);
                var currentResponse = world.Peers[0].Pull()[^1]; var currentBytes = new byte[currentResponse.Length - 1];
                Array.Copy(currentResponse, 1, currentBytes, 0, currentBytes.Length); next.Deliver(2, currentBytes);
                check(next.Commit(1, 103) && next.Owner(1) == 3,
                    "reelected coordinator fresh request restores the unchanged physical offer");
                next.End(1, 103);
                check(!next.Commit(1, 103), "handoff still requires exact physical offer lifetime");
            }
        }
        // The physical card may arrive after election but before its drain ends.
        // The last pre-drain Tick must not make the ordinary three-second fallback
        // expire before the next .8-second request interval can obtain its grant.
        using (var delayedOffer = new Peer(2, 1, new[] { 1, 2 }, new[] { 2 }))
        {
            delayedOffer.Tick(1.2f); delayedOffer.Begin(1, 201);
            delayedOffer.Tick(4.4f); delayedOffer.Tick(5.01f);
            check(!delayedOffer.Commit(1, 201), "late drain offer waits for its genuine next coordinator request interval");
            check(!delayedOffer.Unavailable(1, 201), "offer arriving inside discovery drain never expires before its next grant request");
            delayedOffer.Tick(5.3f);
            check(delayedOffer.Commit(1, 201), "late drain offer commits after the ordinary request interval grants it");
        }
        // A compatible native host retains the preexisting immediate coordinator
        // path and its priority even if a lower numerical guest ID is supplied.
        using (var host = new Peer(11, 11, new[] { 11, 2 }, new[] { 11, 2 }))
        {
            host.Begin(3, 77);
            check(host.Commit(3, 77), "compatible native host retains immediate authoritative grant precedence");
            host.Call(nameof(CoordinatorPeerPort.Forget), 2); host.Tick(2f);
            check(host.Commit(3, 77), "unrelated avatar timeout cannot replace the native host coordinator");
        }
        // Discovery can first select self at the higher-ID guest. Learning the
        // older compatible participant retires its old request slots and clock.
        using (var late = new Peer(3, 1, new[] { 1, 2, 3 }, new[] { 3 }))
        {
            late.Begin(3, 90); late.Tick(2f);
            check(!late.Commit(3, 90), "unsettled higher-ID initial guest cannot commit during discovery");
            late.Membership(new[] { 1, 2, 3, 4 }, new[] { 2, 3, 4 }); late.Tick(3f);
            check(!late.Commit(3, 90) && !late.Unavailable(3, 90), "late compatible discovery hands off without destroying the original offer");
            late.Tick(7.1f);
            var request = late.Pull()[^1]; check(request[0] == 0, "guest coordinator requests use Others rather than OnlyServer at a flat host");
            var bytes = new byte[request.Length - 1]; Array.Copy(request, 1, bytes, 0, bytes.Length);
            TownServiceGrantCodec.TryRead(bytes, bytes.Length, out var message);
            var forged = new TownGrantMessage(TownGrantKind.Grant, 3, 3, message.Session, message.Nonce, 100, message.Sequence);
            var reply = TownServiceGrantCodec.Write(forged);
            late.Deliver(4, reply); check(!late.Commit(3, 90), "a compatible unelected sender cannot manufacture native purchase permission");
            late.Deliver(2, reply); check(late.Commit(3, 90), "current elected compatible coordinator unlocks only a matching sent request");
            late.Tick(11.2f); check(!late.Commit(3, 90), "lost elected renewal expires by request-send time rather than receive time");
            late.Deliver(2, reply);
            check(!late.Commit(3, 90), "queued old reliable reply cannot restart an expired grant from receive time");
            var newest = late.Pull()[^1]; bytes = new byte[newest.Length - 1];
            Array.Copy(newest, 1, bytes, 0, bytes.Length);
            TownServiceGrantCodec.TryRead(bytes, bytes.Length, out message);
            forged = new TownGrantMessage(TownGrantKind.Grant, 3, 3, message.Session, message.Nonce, 101, message.Sequence);
            late.Deliver(2, TownServiceGrantCodec.Write(forged));
            check(late.Commit(3, 90), "newest matched request can recover the same elected coordinator with a fresh epoch");
            late.Deliver(2, reply);
            check(late.Commit(3, 90), "queued earlier sequence never shortens the recovered current request grant");
        }
    }
}
