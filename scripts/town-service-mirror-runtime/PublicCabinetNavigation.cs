using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    // Native row/prefab construction and local player identity are boundaries.
    // Category OnPoke/Tick, drawer Select/Begin/Tick/Follow, publisher TickPublic,
    // capture, wire codec, template neutralization and remote playback are exact
    // production code. This includes the non-author publisher withdrawal which
    // the older manually-authored SetRack handover fixture did not exercise.
    private static IEnumerator PublicCabinetNavigation()
    {
        TownServiceSync.ResetNetwork(); TownServiceMirror.Shutdown(); Baselines.Clear();
        NetPlayerActors.Peer = 1;
        Transform owner = Go("public navigation native frame").transform;
        Transform observer = Go("public navigation observer frame").transform;
        Transform housing = Go("public original housing", owner).transform;
        Go("Cassette", housing);
        var drawer = new TownServiceMerchantDrawer { HousingRoot = housing,
            Root = Go("public original crank", owner).transform };
        drawer.FixturePrepareNavigation();
        var catalog = new TownServiceCatalog { ObserverRoot = housing };
        catalog.Drawers.Add(drawer);
        var hand = new VRHand();
        for (int category = 0; category < 2; category++)
        {
            Transform key = Rect("native category " + category, owner, new Vector2(category * 55, 0), new Vector2(40, 40));
            key.gameObject.AddComponent<Image>().color = category == 0 ? Color.red : Color.green;
            var button = new TownServiceCatalogCategory { Key = "merchant.category." + category, Root = key };
            button.FixturePrepareNavigation(drawer, category); catalog.Categories.Add(button);
            Transform mount = Go("native stock holder " + category, owner).transform;
            Transform face = Rect("native face canvas " + category, mount, Vector2.zero, new Vector2(60, 90));
            Transform card = Rect("native item artwork " + category, face, Vector2.zero, new Vector2(60, 90));
            card.gameObject.AddComponent<ItemCardUI>().CardID = 900 + category;
            card.gameObject.AddComponent<Image>().color = category == 0 ? Color.blue : Color.yellow;
            Transform row = Rect("original price " + category, owner, Vector2.zero, new Vector2(60, 20));
            row.gameObject.AddComponent<Image>().color = Color.cyan;
            Transform price = Rect("native price presentation " + category, mount, Vector2.down * 55, new Vector2(60, 20));
            price.gameObject.AddComponent<Image>().color = Color.cyan;
            var entry = new TownServiceCatalog.Entry { ItemId = 900 + category,
                NavigationRack = drawer, Page = category * 256, CardRoot = card, FaceRoot = face,
                PhysicalMount = mount, RowContent = price, RowSource = row };
            catalog.Entries.Add(entry); TownServiceCatalog.CardMounts.Add(mount, entry);
            foreach (Transform boundary in new[] { mount, face, card, price }) NativeTemplates.BoundaryRoots.Add(boundary);
        }
        catalog.StockLayout = new[] { new TownCatalogSlot(900, 0), new TownCatalogSlot(901, TownCatalogLayout.SlotsPerCategory) };
        TownServicePublicMerchant.Catalog = catalog; TownServicePublicMerchant.FixtureResetVisibility();
        TownServiceMirror.CommitPublicVisibility = TownServicePublicMerchant.FixtureCommitVisibility;
        TownServiceSync.UseProductionPublish = true;
        drawer.Select(1, false);
        int advancedFrame = -1;
        for (float until = Time.unscaledTime + TownRackState.TurnDuration + .03f; Time.unscaledTime < until;)
        {
            if (advancedFrame == Time.frameCount) { yield return null; continue; }
            advancedFrame = Time.frameCount;
            drawer.FixtureTick(); foreach (var key in catalog.Categories) key.Tick(1f); yield return null;
        }
        TownServiceSync.TickPublic(owner, owner, catalog, 882, 1f);
        List<byte[]> initial = Capture();
        Check(initial.Any(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out var f) && f!.Rack != null),
            "real public publisher emits its authored cassette clock before any peer category press");
        ushort missingPrice = TownServiceSync.PublicModuleId(catalog.Entries[1].RowContent!);
        var identities = new PublicNavigationIdentities();
        identities.Switch(2);
        // A new client's original input proxy starts on its native default tray.
        // Keep its price packet cold while its complete public clock has arrived.
        drawer.Follow(new TownRackState { Page = 0, From = 0, To = 0, Elapsed = TownRackState.TurnDuration });
        Receive(1, initial.Where(bytes => ReadModule(bytes) != missingPrice)); TownServiceMirror.TickRemote(_ => observer);
        Check(TownServiceMirror.PublicAuthor == 1 && !TownServiceMirror.HasReadyPublicPresentation
            && TownServiceMirror.PublicRack?.Page == 256,
            "joining peer receives the real public mechanism while one original price is still cold");
        TownServiceSync.TickPublic(owner, owner, catalog, 882, 1f);
        Check(TownServiceSync.PublicModuleCount == 0,
            "real observer publisher withdraws its local native modules instead of publishing a second cabinet");
        TownServiceMirror.BeginSession(1, 883, owner, owner);
        TownServiceMirror.SetLocalTransactionActive(1, true);
        catalog.Categories[0].OnPoke(hand);
        Check(TownServiceMirror.IsPublicAuthor && drawer.FixtureFollowingTurn && hand.Haptics == 1
            && drawer.FromPage == 256 && drawer.ToPage == 0,
            "peer physical category button adopts the public page despite missing artwork and a separate merchant transaction");
        float start = Time.unscaledTime;
        bool sampled = false;
        advancedFrame = -1;
        while (Time.unscaledTime - start < TownRackState.TurnDuration + .05f)
        {
            // EditorApplication.update can resume this IEnumerator more than once in one
            // play-mode frame. Native Tick consumes Time.unscaledDeltaTime, while capture/
            // retry/received clocks use Time.unscaledTime. Advancing only the drawer again
            // invented a fast owner clock and sampled before the corresponding real frame.
            // Match its actual production once-per-frame owner; preserve every parity term.
            if (advancedFrame == Time.frameCount) { yield return null; continue; }
            advancedFrame = Time.frameCount;
            identities.Switch(2);
            drawer.FixtureTick(); foreach (var key in catalog.Categories) key.Tick(1f);
            TownServiceSync.TickPublic(owner, owner, catalog, 882, 1f);
            List<byte[]> packets = Capture();
            identities.Switch(3); Receive(2, packets); TownServiceMirror.TickRemote(_ => observer);
            TownRackState? state = TownServiceMirror.PublicRack;
            if (!sampled && state != null && drawer.FixtureFollowClock > .15f && drawer.FixtureFollowClock < .35f)
            {
                ushort housingId = TownServiceSync.PublicModuleId(housing);
                Transform remoteHousing = Remote(-2, housingId)!.Root;
                bool matches = TownServiceMirror.HasReadyPublicPresentation && state.From == 256 && state.To == 0
                    && Vector3.Distance(housing.Find("Cassette").localPosition, remoteHousing.Find("Cassette").localPosition) < .005f;
                Check(matches,
                    "actual peer category callback preserves the same intermediate authored cassette motion remotely"
                    + ": ready=" + TownServiceMirror.HasReadyPublicPresentation + " from=" + state.From + " to=" + state.To
                    + " direction=" + state.ScrollDirection + " ownerClock=" + drawer.FixtureFollowClock + " remoteClock=" + state.Elapsed
                    + " owner=" + housing.Find("Cassette").localPosition.ToString("F6")
                    + " remote=" + remoteHousing.Find("Cassette").localPosition.ToString("F6")
                    + (matches ? "" : " readiness=" + NavigationReadiness(state)));
                foreach (var key in catalog.Categories)
                    Check(Vector3.Distance(key.Root.position, Remote(-2, TownServiceSync.PublicModuleId(key.Root))!.Root.position) < .003f,
                        "actual category key depression survives original capture and cross-client playback: owner=" + key.Root.position
                        + " remote=" + Remote(-2, TownServiceSync.PublicModuleId(key.Root))!.Root.position);
                identities.Switch(1);
                drawer.Follow(new TownRackState { Page = 0, From = 0, To = 0, Elapsed = TownRackState.TurnDuration });
                drawer._hand = hand; // Actual manual grab owns the crank input; animated turns do not.
                catalog.Categories[1].OnPoke(hand);
                Check(TownServiceMirror.PublicAuthor == 2 && hand.Haptics == 1,
                    "a manual crank grab prevents a competing category input from stealing authority");
                Check(!TownServicePublicMerchant.TryTurnPage(drawer, 1) && TownServiceMirror.PublicAuthor == 2,
                    "a manual crank grab prevents a competing page input from stealing authority");
                drawer._hand = null;
                TownServicePublicMerchant.FixtureFollowPublicRack();
                sampled = true;
            }
            yield return null;
        }
        Check(sampled && TownServiceMirror.PublicRack?.Page == 0 && TownServiceMirror.HasReadyPublicPresentation,
            "real category publication completes on the same populated public page after ownership withdrawal and takeover");
        identities.Switch(1);
        TownServicePublicMerchant.FixtureFollowPublicRack();
        catalog.Categories[1].OnPoke(hand);
        Check(TownServiceMirror.IsPublicAuthor && drawer.FromPage == 0 && drawer.ToPage == 256 && hand.Haptics == 2,
            "a subsequent different peer category press adopts the shared page before beginning its own return animation");
        advancedFrame = -1;
        for (float until = Time.unscaledTime + TownRackState.TurnDuration + .03f; Time.unscaledTime < until;)
        {
            if (advancedFrame == Time.frameCount) { yield return null; continue; }
            advancedFrame = Time.frameCount;
            drawer.FixtureTick(); yield return null;
        }
        drawer.PageCount = 2;
        Check(TownServicePublicMerchant.TryTurnPage(drawer, 1) && TownServiceMirror.IsPublicAuthor
            && drawer.FromPage == 256 && drawer.ToPage == 257,
            "a peer page request keeps the shared category and starts the exact native page animation");
        NetPlayerActors.Peer = 1;
        TownServiceMirror.CommitPublicVisibility = null;
        TownServiceSync.UseProductionPublish = false; TownServiceSync.ResetNetwork();
        TownServicePublicMerchant.Catalog = null; TownServicePublicMerchant.FixtureResetVisibility();
        drawer.FixtureDisposeFollower(); NativeTemplates.BoundaryRoots.Clear(); TownServiceCatalog.CardMounts.Clear();
        TownServiceMirror.Shutdown(); Baselines.Clear();
    }

    private static string NavigationReadiness(TownRackState state)
    {
        int peer = -TownServiceMirror.PublicAuthor;
        var remote = (IDictionary)typeof(TownServiceMirror).GetField("Remote", PrivateStatic)!.GetValue(null)!;
        var pending = (IDictionary)typeof(TownServiceMirror).GetField("Pending", PrivateStatic)!.GetValue(null)!;
        if (!TownServiceMirror.PublicSessions.TryGetValue(peer, out var session)
            || !remote.Contains(peer) || !pending.Contains(peer)) return "session/group absent";
        object modules = remote[peer]!;
        bool Controls() => (bool)typeof(TownServiceMirror).GetMethod("PublicControlsReady", PrivateStatic)!
            .Invoke(null, new object[] { session, state, modules })!;
        bool Page(ushort page) => (bool)typeof(TownServiceMirror).GetMethod("PublicPageReady", PrivateStatic)!
            .Invoke(null, new object[] { (ushort)TownServiceSync.PublicModuleId(TownServicePublicMerchant.Catalog!.ObserverRoot!), state, page, session, modules })!;
        var parts = new List<string>();
        foreach (ushort id in session.Modules)
        {
            var module = ((IDictionary)modules)[id];
            var received = ((IDictionary)pending[peer]!)[id] as TownServiceFrame;
            TownServiceFrame? applied = module == null ? null
                : module.GetType().GetField("LastFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(module) as TownServiceFrame;
            parts.Add(id + ":" + (received?.TemplateAddress ?? "no packet") + ":"
                + (received?.RackMember?.Turn.ToString() ?? "-") + ":"
                + (applied?.RackMember?.Turn.ToString() ?? "no applied")
                + ":seq=" + (received?.Sequence.ToString() ?? "-") + "/" + (applied?.Sequence.ToString() ?? "-"));
        }
        return "controls=" + Controls() + " from=" + Page(state.From) + " to=" + Page(state.To)
            + " modules=" + string.Join(",", parts);
    }

    // This Unity fixture executes clients sequentially. The local player adapter
    // and each process-local claim counter must move together; changing only the
    // player ID falsely carries the other client's claim into the authority tie.
    // All received sessions, authored packets and observed claim history still
    // go through the real capture/codec/Receive path, never a fabricated election.
    private sealed class PublicNavigationIdentities
    {
        private readonly Dictionary<int, uint> _claims = new();
        private readonly FieldInfo _claim = typeof(TownServiceMirror).GetField("_publicClaim", BindingFlags.Static | BindingFlags.NonPublic)!;
        internal void Switch(int player)
        {
            _claims[NetPlayerActors.Peer] = (uint)_claim.GetValue(null)!;
            NetPlayerActors.Peer = player;
            _claim.SetValue(null, _claims.TryGetValue(player, out uint claim) ? claim : 0u);
        }
    }
}
