using System;
using System.Collections.Generic;
using System.Diagnostics;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Hands;
using UnityEngine;

public static partial class InteractionProgram
{
    private sealed class ControlTransport : INetTransport
    {
        public bool IsOnline => true;
        public int LocalPlayerId { get; set; }
        public event Action<int, byte[], int>? PacketReceived { add { } remove { } }
        public void Send(byte[] bytes, int length, object? identity = null) { }
        public void Install() { }
        public void Uninstall() { }
    }
    private static void MerchantControlProof(UIShopItemInventory inventory, Transform anchor)
    {
        var service = new Service();
        for (int i = 0; i < 48; i++) service.Buy.Add(new ScenarioRuleLibrary.CItem(5000 + i)
            { YMLData = { Slot = ScenarioRuleLibrary.CItem.EItemSlot.Head } });
        ShopService.Source = service;
        using var host = new TownServiceCatalog(inventory, anchor, () => service, () => true, anchor, persistent: true);
        using var peer = new TownServiceCatalog(inventory, anchor, () => service, () => true, anchor, persistent: true);
        host.SetVisibility(1f); peer.SetVisibility(1f); Census(host); Census(peer); host.Tick(1f); peer.Tick(1f);
        var hostRack = host.Drawers[0]; var peerRack = peer.Drawers[0];
        Transform original = Item(host, 5000).CardRoot;
        int originals = ObjectPool.Alive, nativeInitializations = UIShopItemSlot.Initializations;
        var transmitted = new List<byte[]>();
        TownMerchantControlSync.SendReliable = (bytes, count, hostOnly) =>
        { Check(count == 62 && bytes.Length == count, "public control contains 62 metadata bytes and no native artwork"); transmitted.Add(bytes); return true; };
        var hostTransport = new ControlTransport { LocalPlayerId = 1 };
        var peerTransport = new ControlTransport { LocalPlayerId = 2 };
        FFSNet.FFSNetwork.IsOnline = true; FFSNet.PlayerRegistry.HostPlayerID = 1;
        TownMerchantControlSync.Reset();
        ControlFixture.LocalPeer = 2; TownServicePublicMerchant.RegisterProbe(peer);
        TownMerchantControlSync.Tick(peerTransport, Time.unscaledTime);
        uint peerBefore = peerRack.TurnEpoch;
        Check(TownServicePublicMerchant.TryTurnPage(peerRack, 1), "unassigned visitor operates the common cabinet through its original public input");
        Check(peerRack.TurnEpoch == peerBefore && transmitted.Count == 1,
            "client input sends an intent instead of silently turning a private page");
        byte[] request = transmitted[0]; transmitted.Clear();
        Check(BitConverter.ToString(request).Replace("-", "").ToLowerInvariant()
            == "31525647031b6835000101020100000000010000000200000001000000000000000000000000000000000000000000000000000000000000000000000000",
            "public request matches independent byte-exact golden layout");
        var goldenClock = new TownRackState { Cassette = true, Turn = 0x04030201,
            Elapsed = .25f, LeadAngle = 7.5f, Page = 256, From = 256, To = 257, PageCount = 2, ScrollDirection = 1 };
        var goldenState = new TownMerchantControlMessage(TownMerchantControlKind.State,
            TownMerchantControlOperation.Snapshot, 0, 0xaabbccdd, 0x01020304, 2, 5, 0x11223344, goldenClock, 3, 17.5f);
        byte[] goldenBytes = TownMerchantControlCodec.Write(in goldenState);
        Check(BitConverter.ToString(goldenBytes).Replace("-", "").ToLowerInvariant()
            == "31525647031b68350001020300ddccbbaa040302010200000005000000010203040000803e0000f040000100010101020001443322110300000000008c41",
            "public original clock matches independent byte-exact golden layout");
        Check(TownMerchantControlCodec.TryRead(goldenBytes, goldenBytes.Length, out var nativeClock)
            && nativeClock.Session == 0xaabbccdd && nativeClock.Sequence == 0x01020304
            && nativeClock.Clock!.Page == 256 && nativeClock.Clock.From == 256 && nativeClock.Clock.To == 257
            && nativeClock.Clock.Elapsed == .25f && nativeClock.Clock.LeadAngle == 7.5f
            && nativeClock.Clock.ScrollDirection == 1 && nativeClock.Epoch == 0x11223344
            && nativeClock.CrankOwner == 3 && nativeClock.CrankLeadAngle == 17.5f,
            "independent clock bytes preserve original animation timing direction and control scope");
        Check(TownMerchantControlCodec.TryRead(request, request.Length, out var decoded)
            && decoded.Kind == TownMerchantControlKind.Request && decoded.Requester == 2,
            "actual public page press reaches the metadata request codec");
        ControlFixture.LocalPeer = 1; TownServicePublicMerchant.RegisterProbe(host);
        TownMerchantControlSync.Tick(hostTransport, Time.unscaledTime); transmitted.Clear();
        Check(TownMerchantControlSync.Receive(2, request, request.Length), "host consumes the visitor's actual page intent");
        Check(hostRack.TurnEpoch == 1 && hostRack.FromPage == 0 && hostRack.ToPage == 1,
            "one visitor request executes the original host drawer turn exactly once");
        Check(TownServiceMirror.AuthorityClaims == 0 && ObjectPool.Alive == originals
            && UIShopItemSlot.Initializations == nativeInitializations && Item(host, 5000).CardRoot == original,
            "public input preserves the complete prepared original bank without authority invalidation or native widget reconstruction");
        byte[] committed = transmitted[transmitted.Count - 1]; transmitted.Clear();
        Check(TownMerchantControlSync.Receive(2, request, request.Length) && hostRack.TurnEpoch == 1,
            "duplicate reliable request cannot replay the page turn");
        var forged = new TownMerchantControlMessage(TownMerchantControlKind.Request, TownMerchantControlOperation.Page,
            1, 0, 20, 3, 20, 0);
        byte[] forgedBytes = TownMerchantControlCodec.Write(in forged);
        Check(TownMerchantControlSync.Receive(2, forgedBytes, forgedBytes.Length) && hostRack.TurnEpoch == 1,
            "one peer cannot operate the cabinet using another peer's identity");
        ControlFixture.LocalPeer = 2; TownServicePublicMerchant.RegisterProbe(peer);
        TownMerchantControlSync.Tick(peerTransport, Time.unscaledTime);
        Check(TownMerchantControlSync.Receive(1, committed, committed.Length) && peerRack.TurnEpoch == hostRack.TurnEpoch
            && peerRack.FromPage == hostRack.FromPage && peerRack.ToPage == hostRack.ToPage,
            "host commit drives the observer's original local drawer with the same page and animation epoch");
        byte[] malformed = (byte[])committed.Clone(); malformed[7]++;
        Check(!TownMerchantControlCodec.TryRead(malformed, malformed.Length, out _), "wrong TLV payload length is rejected");
        malformed = (byte[])committed.Clone(); malformed[47] = 0; malformed[48] = 0;
        Check(!TownMerchantControlCodec.TryRead(malformed, malformed.Length, out _), "zero page capacity is rejected atomically");
        Check(TownMerchantControlCodec.Newer(1, uint.MaxValue) && !TownMerchantControlCodec.Newer(uint.MaxValue, 1),
            "request ordering is safe across unsigned wrap");
        // An actual physical crank release must use the same public request path.
        Set(peerRack, "_turning", false); Set(peerRack, "_clock", 1f);
        var hand = new VRHand(); uint beforeGrab = peerRack.TurnEpoch; transmitted.Clear();
        peerRack.OnGrab(hand);
        hand.Rig.GrabAnchor.position -= peerRack.Root.parent.TransformVector(Vector3.up * .05f);
        peerRack.Tick(1f);
        int manualMessages = transmitted.Count;
        for (int pulse = 0; pulse < 120; pulse++) peerRack.Tick(1f);
        Check(transmitted.Count == manualMessages, "manual crank sampling is bounded to 15 Hz independently of render frequency");
        peerRack.OnRelease(hand, Vector3.zero);
        Check(peerRack.TurnEpoch == beforeGrab && transmitted.Count == 3,
            "physical crank release follows the same shared intent instead of a private local turn");
        byte[][] manual = transmitted.ToArray(); transmitted.Clear();
        Check(TownMerchantControlCodec.TryRead(manual[0], manual[0].Length, out var grab)
            && grab.Operation == TownMerchantControlOperation.CrankGrab
            && TownMerchantControlCodec.TryRead(manual[1], manual[1].Length, out var drag)
            && drag.Operation == TownMerchantControlOperation.CrankDrag && Mathf.Abs(drag.CrankLeadAngle - 17.5f) < .001f
            && TownMerchantControlCodec.TryRead(manual[2], manual[2].Length, out var release)
            && release.Operation == TownMerchantControlOperation.CrankRelease && Mathf.Abs(release.CrankLeadAngle - 17.5f) < .001f,
            "manual crank transmits its real grab, intermediate handle angle and final authored lead angle");
        ControlFixture.LocalPeer = 1; TownServicePublicMerchant.RegisterProbe(host);
        TownMerchantControlSync.Tick(hostTransport, Time.unscaledTime); transmitted.Clear();
        Set(hostRack, "_turning", false); Set(hostRack, "_clock", 1f);
        TownMerchantControlSync.Receive(2, manual[0], manual[0].Length);
        Check(TownMerchantControlSync.CrankOwner == 2 && !hostRack.CanGrab,
            "first public crank visitor owns only the bounded shared handle clutch");
        var competing = new TownMerchantControlMessage(TownMerchantControlKind.Request, TownMerchantControlOperation.CrankGrab,
            0, 0, 1, 3, 1, 0);
        byte[] competingBytes = TownMerchantControlCodec.Write(in competing);
        TownMerchantControlSync.Receive(3, competingBytes, competingBytes.Length);
        Check(TownMerchantControlSync.CrankOwner == 2,
            "simultaneous public crank grabs cannot steal the first visitor's intermediate movement");
        TownMerchantControlSync.Receive(2, manual[1], manual[1].Length);
        Check(Quaternion.Angle(hostRack.Root.localRotation, Quaternion.Euler(-17.5f, 0f, 0f)) < .01f,
            "author crank immediately uses the visitor's actual intermediate hand angle");
        TownMerchantControlSync.Receive(2, manual[2], manual[2].Length);
        Check(TownMerchantControlSync.CrankOwner == 0 && hostRack.TurnEpoch == 2
            && Mathf.Abs(hostRack.LeadAngle - 17.5f) < .001f,
            "release relinquishes the shared clutch and continues the original full turn from the visitor's angle");
        UnityEngine.Object.DestroyImmediate(hand.Rig.GrabAnchor.gameObject);
        // Measure production Select/RequestTurn + codec + dedup at its real call site.
        ControlFixture.LocalPeer = 1; TownServicePublicMerchant.RegisterProbe(host);
        TownMerchantControlSync.Tick(hostTransport, Time.unscaledTime); transmitted.Clear();
        var timer = Stopwatch.StartNew();
        for (uint i = 21; i <= 1020; i++)
        {
            var turn = new TownMerchantControlMessage(TownMerchantControlKind.Request,
                TownMerchantControlOperation.Page, 1, 0, i, 2, i, 0);
            byte[] bytes = TownMerchantControlCodec.Write(in turn);
            TownMerchantControlSync.Receive(2, bytes, bytes.Length);
        }
        timer.Stop();
        Check(hostRack.TurnEpoch == 1002 && ObjectPool.Alive == originals
            && UIShopItemSlot.Initializations == nativeInitializations && TownServiceMirror.AuthorityClaims == 0,
            "1000 source control events do zero native card rebuilds and zero catalogue authority migrations");
        Console.WriteLine("MERCHANT_CONTROL events=1000 elapsed_ms=" + timer.Elapsed.TotalMilliseconds.ToString("F3")
            + " bytes_per_event=" + TownMerchantControlCodec.Size + " native_widget_rebuilds=0 authority_migrations=0");
        // A category press remains a normal public operation independent of the
        // transaction owner or selected character, and uses the exact native drawer.
        var category = new TownMerchantControlMessage(TownMerchantControlKind.Request,
            TownMerchantControlOperation.Category, 5, 0, 2, 3, 2, 0);
        byte[] categoryBytes = TownMerchantControlCodec.Write(in category);
        TownMerchantControlSync.Receive(3, categoryBytes, categoryBytes.Length);
        Check(hostRack.ToPage == 5 * 256 && hostRack.TurnEpoch == 1003 && ObjectPool.Alive == originals
            && UIShopItemSlot.Initializations == nativeInitializations,
            "an unassigned third visitor's category intent uses the original common page without native card reconstruction");
        Set(hostRack, "_turning", false); Set(hostRack, "_clock", 1f);
        var timedGrab = new TownMerchantControlMessage(TownMerchantControlKind.Request,
            TownMerchantControlOperation.CrankGrab, 0, 0, 3, 3, 3, 0);
        byte[] timedBytes = TownMerchantControlCodec.Write(in timedGrab);
        TownMerchantControlSync.Receive(3, timedBytes, timedBytes.Length);
        Check(TownMerchantControlSync.CrankOwner == 3, "third visitor can acquire a released physical clutch");
        TownMerchantControlSync.Tick(hostTransport, Time.unscaledTime + 3f);
        Check(TownMerchantControlSync.CrankOwner == 0 && hostRack.CanGrab,
            "abandoned crank ownership expires without stranding public controls or invalidating originals");
        var hostHand = new VRHand(); hostRack.OnGrab(hostHand);
        TownMerchantControlSync.Tick(hostTransport, Time.unscaledTime);
        Check(TownMerchantControlSync.CrankOwner == 1 && hostRack.Moving,
            "local physical author uses the same bounded crank ownership");
        TownMerchantControlSync.Tick(hostTransport, Time.unscaledTime + 3f);
        Check(TownMerchantControlSync.CrankOwner == 0 && !hostRack.Moving && hostRack.CanGrab,
            "expiry clears the actual local hand clutch instead of retaining a stale physical grab");
        UnityEngine.Object.DestroyImmediate(hostHand.Rig.GrabAnchor.gameObject);
        ControlFixture.LocalPeer = 2; TownServicePublicMerchant.RegisterProbe(peer);
        TownMerchantControlSync.Tick(peerTransport, Time.unscaledTime); transmitted.Clear();
        Check(TownServicePublicMerchant.TrySelectCategory(peerRack, 1), "peer control before a presentation reset is queued");
        Check(TownMerchantControlCodec.TryRead(transmitted[0], transmitted[0].Length, out var beforeReset),
            "peer control before reset retains a valid nonce");
        TownMerchantControlSync.Reset(); transmitted.Clear();
        TownMerchantControlSync.Tick(peerTransport, Time.unscaledTime);
        Check(TownServicePublicMerchant.TrySelectCategory(peerRack, 2), "peer control after a presentation reset is queued");
        Check(TownMerchantControlCodec.TryRead(transmitted[0], transmitted[0].Length, out var afterReset)
            && TownMerchantControlCodec.Newer(afterReset.Sequence, beforeReset.Sequence),
            "presentation rebuild retains monotone requester nonces against the surviving host");
        TownMerchantControlSync.Reset(); TownServicePublicMerchant.DetachProbe(); FFSNet.FFSNetwork.IsOnline = false;
    }
}

internal static class ControlFixture { internal static int LocalPeer = 1; }
namespace GloomhavenVR.Net
{
    internal static class NetPlayerActors { internal static int LocalPlayerId() => ControlFixture.LocalPeer; }
    internal static class NetSession { internal static bool FlatNetMode; }
}
namespace GloomhavenVR.Net.TownServices
{
    internal static class TownServiceGrantSync { internal static bool CoordinatorReady => true; }
}
