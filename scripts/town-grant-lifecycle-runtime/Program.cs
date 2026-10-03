using System;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string label) { _checks++; if (!value) throw new Exception(label); }
    private static TownGrantMessage Reply(TownGrantMessage request, int player = 22) => new(
        TownGrantKind.Grant, request.Service, player, request.Session, request.Nonce, 100, request.Sequence);
    private static void Deliver(in TownGrantMessage message)
    {
        byte[] bytes = TownServiceGrantCodec.Write(in message);
        Check(TownServiceGrantSync.Receive(11, bytes, bytes.Length), "actual reliable grant decoder accepted host response");
    }
    private static void Main()
    {
        var transport = new FfsNetTransport();
        foreach (byte service in new byte[] { 1, 3 })
        {
            TownServiceGrantSync.Reset(); transport.Sent.Clear(); Time.unscaledTime = 1f;
            TownServiceGrantSync.Tick(transport, Time.unscaledTime);
            TownServiceGrantSync.SetOffer(service, 10, true);
            TownServiceGrantSync.Tick(transport, Time.unscaledTime);
            TownGrantMessage first = transport.Sent[0], firstGrant = Reply(first);
            Time.unscaledTime += .05f; Deliver(in firstGrant);
            Check(TownServiceGrantSync.GrantedOwner(service) == 22
                && TownServiceGrantSync.TryGrantedOwner(service, out int owner, out uint session)
                && owner == 22 && session == 10, "parked local card displays exact granted occupant");
            Check(TownServiceGrantSync.MayCommit(service, 10), "visible identity does not replace native commit permission");
            TownServiceGrantSync.SetOffer(service, 10, false);
            Check(TownServiceGrantSync.GrantedOwner(service) == 0
                && !TownServiceGrantSync.TryGrantedOwner(service, out _, out _),
                "physical local release clears identity before host release round trip");
            Check(!TownServiceGrantSync.MayCommit(service, 10), "released offer cannot invoke native continuation");
            Check(transport.Sent[^1].Kind == TownGrantKind.Release
                && transport.Sent[^1].Session == first.Session && transport.Sent[^1].Nonce == first.Nonce,
                "physical return still sends exact release to authoritative host");
            int sent = transport.Sent.Count; TownServiceGrantSync.SetOffer(service, 10, false);
            Check(transport.Sent.Count == sent, "repeated physical release is idempotent");
            Deliver(in firstGrant);
            Check(TownServiceGrantSync.GrantedOwner(service) == 0
                && !TownServiceGrantSync.TryGrantedOwner(service, out _, out _),
                "late old grant cannot resurrect removed local occupant");
            TownServiceGrantSync.SetOffer(service, 10, true); Time.unscaledTime += 1f;
            TownServiceGrantSync.Tick(transport, Time.unscaledTime);
            TownGrantMessage replacement = transport.Sent[^1];
            Check(replacement.Nonce != first.Nonce, "same visit replacement has new physical identity");
            Deliver(in firstGrant);
            Check(TownServiceGrantSync.GrantedOwner(service) == 0, "old nonce cannot masquerade as replacement card occupant");
            var replacementGrant = Reply(replacement); Deliver(in replacementGrant);
            Check(TownServiceGrantSync.GrantedOwner(service) == 22, "fresh matching replacement grant remains visible");
            var oldRelease = new TownGrantMessage(TownGrantKind.Release, service, 22, first.Session, first.Nonce, 100, 0);
            Deliver(in oldRelease);
            Check(TownServiceGrantSync.GrantedOwner(service) == 22, "late prior nonce release cannot erase current card occupant");
            var wrongSessionRelease = new TownGrantMessage(TownGrantKind.Release, service, 22, 9, replacement.Nonce, 100, 0);
            Deliver(in wrongSessionRelease);
            Check(TownServiceGrantSync.GrantedOwner(service) == 22, "wrong visit release cannot erase replacement identity");
            TownServiceGrantSync.SetOffer(service, 10, false);
            var foreignGrant = new TownGrantMessage(TownGrantKind.Grant, service, 33, 20, 200, 100, 1);
            Deliver(in foreignGrant);
            TownServiceGrantSync.SetOffer(service, 30, true);
            TownServiceGrantSync.SetOffer(service, 30, false);
            Check(TownServiceGrantSync.GrantedOwner(service) == 33,
                "local failed offer release cannot hide authoritative foreign occupant");
            TownServiceGrantSync.ForgetPeer(33);
            Check(TownServiceGrantSync.GrantedOwner(service) == 0, "departed visitor identity retires");
        }
        TownServiceGrantSync.Reset(); Time.unscaledTime += 1f;
        TownServiceGrantSync.Tick(transport, Time.unscaledTime);
        TownServiceGrantSync.SetOffer(2, 40, true); TownServiceGrantSync.Tick(transport, Time.unscaledTime);
        var templeGrant = Reply(transport.Sent[^1]); Deliver(in templeGrant);
        Check(TownServiceGrantSync.MayCommit(2, 40), "temple retains its exact native callback commit mutex");
        Check(TownServiceGrantSync.GrantedOwner(2) == 0 && !TownServiceGrantSync.TryGrantedOwner(2, out _, out _),
            "temple callback mutex never represents exclusive NPC occupation");
        TownServiceGrantSync.SetOffer(2, 40, false);
        Check(!TownServiceGrantSync.MayCommit(2, 40), "temple commit mutex retires after actual callback");
        TownServiceGrantSync.Reset(); transport.Peer = NetPlayerActors.Peer = PlayerRegistry.HostPlayerID;
        Time.unscaledTime += 1f; TownServiceGrantSync.Tick(transport, Time.unscaledTime);
        TownServiceGrantSync.SetOffer(1, 50, true); TownServiceGrantSync.SetOffer(3, 60, true);
        Check(TownServiceGrantSync.GrantedOwner(1) == 11 && TownServiceGrantSync.GrantedOwner(3) == 11,
            "host can hold independent physical cards at two NPCs");
        TownServiceGrantSync.SetOffer(1, 50, false);
        Check(TownServiceGrantSync.GrantedOwner(1) == 0 && TownServiceGrantSync.GrantedOwner(3) == 11,
            "host physical removal releases only the exact resident");
        TownServiceGrantSync.SetOffer(3, 60, false);
        Check(TownServiceGrantSync.GrantedOwner(3) == 0, "host exact physical release is immediate without a visible echo");
        Console.WriteLine("Production town grant lifecycle: " + _checks + " assertions.");
    }
}
