using System;
using FFSNet;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static readonly FfsNetTransport Transport = new();
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); }

    private static void Reset(int local = 10, bool online = false)
    {
        FFSNetwork.IsOnline = online; NetSession.FlatNetMode = false;
        NetPlayerActors.Peer = Transport.Peer = local; PlayerRegistry.HostPlayerID = 11;
        Time.unscaledTime = 100f; Transport.Sent.Clear();
        TownServiceGrantSync.Reset(); TownServiceMirror.ResetFixture();
        TownServiceGrantSync.Tick(Transport, Time.unscaledTime);
    }
    private static TownServiceSessionInfo Session(byte service, uint id, float age = 1f,
        bool transaction = false) => new()
    {
        Active = true, Service = service, Session = id, TransactionActive = transaction,
        Started = Time.unscaledTime - age, LastSeenTime = Time.unscaledTime,
        SessionAge = age, ReceivedTime = Time.unscaledTime
    };
    private static void Visit(int peer, byte service, uint id, float age = 1f,
        bool transaction = false) => TownServiceMirror.VisitorSessions[peer] = Session(service, id, age, transaction);
    private static void Local(byte service, uint id, float age = 1f,
        bool transaction = false) => TownServiceMirror.PrivateLane = Session(service, id, age, transaction);
    private static void Deliver(in TownGrantMessage message)
    {
        byte[] bytes = TownServiceGrantCodec.Write(in message);
        Check(TownServiceGrantSync.Receive(11, bytes, bytes.Length),
            "production grant decoder admits the host's reliable response");
    }

    private static int DelayedOtherVisitor(int local, int remote)
    {
        Reset(local); Local(3, (uint)(local * 10));
        TownServiceMirror.InteractionOwner(3);
        Time.unscaledTime = 100.13f;
        Check(TownServiceMirror.InteractionOwner(3) == local, "one known visitor authors its original presentation");
        Time.unscaledTime = 100.30f;
        Visit(remote, 3, (uint)(remote * 10), 1.3f);
        int owner = TownServiceMirror.InteractionOwner(3);
        Check(owner == Math.Min(local, remote), "late visitor updates the settled browsing owner");
        Time.unscaledTime = 101f;
        Check(TownServiceMirror.InteractionOwner(3) == owner, "complete visitor set retains its common owner");
        return owner;
    }

    private static void Browsing()
    {
        int first = DelayedOtherVisitor(1, 2), second = DelayedOtherVisitor(2, 1);
        Check(first == second, "two independently settled clients converge after delayed peer admission");

        foreach (int[] order in new[] { new[] { 6, 3, 2 }, new[] { 2, 3, 6 }, new[] { 3, 2, 6 } })
        {
            Reset();
            foreach (int peer in order) Visit(peer, 1, (uint)peer, peer * 100f);
            TownServiceMirror.InteractionOwner(1); Time.unscaledTime += .13f;
            Check(TownServiceMirror.InteractionOwner(1) == 2,
                "reversed observation orders select the same lowest live player ID");
        }
        Reset(2); Local(3, 21, .01f); Visit(3, 3, 31, 1000f);
        TownServiceMirror.InteractionOwner(3); Time.unscaledTime += .13f;
        Check(TownServiceMirror.InteractionOwner(3) == 2, "packet ages do not rank browsing presentation");
        TownServiceMirror.VisitorSessions[3].ReceivedTime = Time.unscaledTime - 20f;
        Check(TownServiceMirror.InteractionOwner(3) == 2, "receiver delivery estimates do not rank browsing presentation");

        Reset(); Visit(2, 1, 21); Visit(4, 1, 41); Visit(3, 2, 32); Visit(5, 3, 53);
        Check(TownServiceMirror.InteractionOwner(1) == 2 && TownServiceMirror.InteractionOwner(2) == 3
            && TownServiceMirror.InteractionOwner(3) == 5, "three residents have independent presentation authors");
        TownServiceMirror.VisitorSessions[2].Active = false;
        Check(TownServiceMirror.InteractionOwner(1) == 4, "closing the elected session transfers presentation immediately");
        TownServiceMirror.VisitorSessions.Remove(3);
        Check(TownServiceMirror.InteractionOwner(2) == 0 && TownServiceMirror.InteractionOwner(1) == 4
            && TownServiceMirror.InteractionOwner(3) == 5, "departures affect only the departed resident's visitor set");
        TownServiceMirror.VisitorSessions[4].LastSeenTime = Time.unscaledTime - 3.01f;
        Check(TownServiceMirror.InteractionOwner(1) == 0, "stale visitor no longer authors a resident");
        Visit(4, 1, 42);
        Check(TownServiceMirror.InteractionOwner(1) == 4 && TownServiceMirror.IsInteractionOwner(4, 1, 42)
            && !TownServiceMirror.IsInteractionOwner(4, 1, 41), "reconnected replacement session invalidates prior owner identity");

        Reset(); Visit(-1, 3, 1); Visit(0, 3, 2); Visit(1, 3, 0); Visit(2, 1, 3); Visit(3, 3, 4);
        Check(TownServiceMirror.InteractionOwner(3) == 3, "invalid peer/session and different residents do not enter the election");
        Check(TownServiceMirror.InteractionOwner(0) == 0 && TownServiceMirror.InteractionOwner(4) == 0,
            "invalid service cannot index a resident lease");

        Reset(10); Local(1, 100); Visit(2, 1, 200);
        Check(TownServiceMirror.InteractionOwner(1) == 2 && TownServiceMirror.LocalOwnsInteraction(1, 100)
            && TownServiceMirror.CanLocalBeginTransaction(1), "shared browsing author does not lock out other visitors");
        Check(TownServiceMirror.PrivateLane.Active && TownServiceMirror.VisitorSessions[2].Active,
            "presentation selection never retires independent personal visitors");
    }

    private static void Transactions()
    {
        Reset(22, online: true); Local(1, 220, transaction: true); Visit(2, 1, 200, transaction: true);
        TownServiceMirror.TransactionOwner(1); Time.unscaledTime += .13f;
        Check(TownServiceMirror.TransactionOwner(1) == 2, "ungranted transaction presentation has a deterministic fallback");
        Visit(1, 1, 100, .01f, transaction: true);
        TownServiceMirror.TransactionOwner(1); Time.unscaledTime += .13f;
        Check(TownServiceMirror.TransactionOwner(1) == 1, "late transaction claimant reevaluates the settled cosmetic fallback");
        var grant = new TownGrantMessage(TownGrantKind.Grant, 1, 33, 330, 3300, 7, 1);
        Deliver(in grant);
        Check(TownServiceMirror.TransactionOwner(1) == 33 && TownServiceMirror.InteractionOwner(1) == 33,
            "actual host grant overrides the lowest-ID presentation claimant immediately");
        Check(!TownServiceMirror.LocalOwnsInteraction(1, 220) && !TownServiceMirror.CanLocalBeginTransaction(1),
            "actual foreign reservation still gates local offered-card interaction");
        Check(!TownServiceGrantSync.MayCommit(1, 220), "cosmetic claims never authorize native callbacks");

        Visit(3, 3, 300, transaction: true);
        var mageGrant = new TownGrantMessage(TownGrantKind.Grant, 3, 44, 440, 4400, 7, 1);
        Deliver(in mageGrant);
        Check(TownServiceMirror.InteractionOwner(1) == 33 && TownServiceMirror.InteractionOwner(3) == 44,
            "host grants for merchant and enchantress are independent");
        var release = new TownGrantMessage(TownGrantKind.Release, 1, 33, 330, 3300, 7, 0);
        Deliver(in release);
        Check(TownServiceMirror.TransactionOwner(1) == 1 && TownServiceMirror.InteractionOwner(3) == 44,
            "exact grant release restores fallback without disturbing the other resident");
        TownServiceMirror.VisitorSessions[1].TransactionActive = false;
        TownServiceMirror.TransactionOwner(1); Time.unscaledTime += .13f;
        Check(TownServiceMirror.TransactionOwner(1) == 2, "reclaimed offer retires its fallback claim even while browsing stays open");
        TownServiceMirror.VisitorSessions[2].Session = 201;
        Check(TownServiceMirror.TransactionOwner(1) == 0, "replacement offer session gets a fresh bounded cosmetic settle");
        Time.unscaledTime += .13f;
        Check(TownServiceMirror.TransactionOwner(1) == 2, "replacement offer session settles without retaining the prior identity");

        Reset(22, online: true); Local(2, 220, transaction: true); Visit(2, 2, 200, transaction: true);
        TownServiceMirror.TransactionOwner(2);
        TownServiceGrantSync.SetOffer(2, 220, true); TownServiceGrantSync.Tick(Transport, Time.unscaledTime);
        TownGrantMessage request = Transport.Sent[^1]; Time.unscaledTime += .05f;
        var templeGrant = new TownGrantMessage(TownGrantKind.Grant, 2, 22, 220, request.Nonce, 7, request.Sequence);
        Deliver(in templeGrant); Time.unscaledTime += .13f;
        Check(TownServiceGrantSync.MayCommit(2, 220), "temple retains the real host-granted native callback mutex");
        Check(TownServiceMirror.TransactionOwner(2) == 0, "temple transaction never occupies the shared resident");
        Check(TownServiceMirror.InteractionOwner(2) == 2 && TownServiceMirror.LocalOwnsInteraction(2, 220)
            && TownServiceMirror.CanLocalBeginTransaction(2), "temple browsing and purse visitors remain independent of commit mutex");

        TownServiceMirror.ResetFixture(); TownServiceGrantSync.Reset();
        Check(TownServiceMirror.InteractionOwner(1) == 0 && TownServiceMirror.InteractionOwner(2) == 0
            && TownServiceMirror.InteractionOwner(3) == 0, "session reset releases every presentation source");
    }

    private static void Main()
    {
        Browsing(); Transactions();
        Console.WriteLine("Production town presentation election: " + _checks + " assertions.");
    }
}
