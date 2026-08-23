using System.Collections.Generic;
using System.Diagnostics;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE ENCHANTRESS EDGE, PEER HALF. ModBuild 239 fixed the local one — "wenn ich eine Karte bei der
/// Magierin verändere, will ich dass die entsprechende Karte auch direkt in meinem Handfächer
/// geupdated ist" — and the lane that built it reported the adjacent gap it was not allowed to
/// touch: when a PEER enhances one of their cards, the card the LOCAL player sees lying in that
/// peer's ghost hand fan keeps the face it was born with.
///
/// <para>THE DEFECT, CONFIRMED RATHER THAN ASSUMED. A peer's map-phase fan face is printed by
/// <c>RemoteHandFan.PrintMapFace</c> through <c>RemoteAbilityCardSource.ShowFullFace</c> →
/// <c>TryPooledClone</c> → <c>RemoteCardArt.ShowFront</c>, i.e. the SAME
/// <c>Object.Instantiate</c> snapshot of a pooled <c>fullAbilityCard</c> the local fan uses, and
/// therefore the same object the game's own post-commit redraw cannot see
/// (<c>SaveDataShared.ApplyEnhancementIcons</c> walks <c>ObjectPool.GetAllCachedAbilityCards</c>,
/// which enumerates pool instances only). On top of that the peer path carries a latch the local
/// path does not: <c>PrintMapFace</c> returns early for any slab whose <c>_mapPrinted</c> entry
/// already equals <c>card.ID</c>, and <c>_mapPrinted</c> is only cleared when the RESOLVED HAND
/// moves (count or leading card, <c>ResolveMapFronts</c>), when the fan is torn down, or when the
/// slab falls back to a back. An enhancement moves none of those — a card's ID is the same card ID
/// before and after — so the stale face survives the whole map visit. Both halves of the local
/// lane's three-latch argument apply here verbatim; only the third latch is a different object.</para>
///
/// <para>WHAT THE LOCAL CLIENT KNOWS ABOUT A PEER'S PURCHASE, AND WHEN. Everything, through the
/// game's own state, in the same call the local buy uses — no new channel, and nothing to invent.
/// <c>UINewEnhancementWindow.ConfirmBuy</c> (UINewEnhancementWindow.cs:534) sends a
/// <c>GameActionType.BuyEnhancement</c> action and, on a CLIENT, returns without committing;
/// the host validates, commits, and forwards the action to every client
/// (<c>ActionProcessor.FinishProcessingAction</c> → <c>Synchronizer.ForwardGameActionToClients</c>,
/// ActionProcessor.cs:465). Each receiver runs <c>GameAction.Execute</c>, whose table routes
/// <c>BuyEnhancement</c> to <c>Singleton&lt;UIGuildmasterHUD&gt;.ProxyBuyEnhancement</c>
/// (GameAction.cs:108) → <c>UINewEnhancementWindow.ProxyBuyEnhancement</c> (:727) →
/// <c>shopService.AddEnhancement</c> (:738). <c>shopService</c> is built in that window's
/// <c>Awake</c> (:138), so it is live whether or not this client ever opened the Enchantress, and
/// <c>MapPartyEnhancementShopService</c> is the only implementation of
/// <c>IEnhancementShopService</c> in the game. SELL takes the mirror route (:777 → :793 →
/// <c>RemoveEnhancement</c>, one line onto the same <c>AddEnhancement</c>).
/// <b>So the local client's own <c>AddEnhancement</c> DOES run for a peer's purchase</b>, and the
/// Harmony postfix ModBuild 239 already registered
/// (<c>Cards.Patches.MapPartyEnhancementShopService_AddEnhancement_FanRefresh</c>) already fires on
/// it. The only thing that was missing is that its sweep is confined to
/// <c>CardsDriver.OffScenarioFanCards</c> — the LOCAL fan. This class is the other set.</para>
///
/// <para>NOTHING GOES ON THE WIRE AND NOTHING NEW IS PATCHED. No record, no field, no packet: the
/// enhancement is a fact the game already syncs, and this reacts to it locally. There is no new
/// Harmony patch either — the existing commit postfix fans out to here, so the mod's patch surface
/// is unchanged.</para>
///
/// <para>NOT A POLL. Nothing ticks this. The whole cost is one pass over the peers' slabs on the
/// one call the game makes when an enhancement is committed; when nobody enhances anything the cost
/// is exactly zero. The remedy is the same IN-PLACE sticker write ModBuild 239 chose over a
/// re-print, for the same reasons and with one extra one that only applies here: re-printing a peer
/// slab means clearing <c>_mapPrinted</c>, which would make <c>PrintMapFace</c> borrow a pool
/// widget, clone a whole card and reload header art on the next frame — a visible blink on someone
/// else's hand to move one icon — and it would mean this class writing the very latch
/// <c>RemoteHandFan</c> owns. It does not: the latch is read for the diagnostic and left alone,
/// because after the rewrite the slab is still drawing the same CARD.</para>
/// </summary>
internal static class RemoteFanEnhancementRefresh
{
    private const string Scope = "Net";

    /// <summary>Peer fan slots whose sticker this class has rewritten since the process started.</summary>
    internal static int SlotsRefreshed { get; private set; }

    /// <summary>
    /// A card's enhancement set was just committed by the game — locally or, far more usefully here,
    /// by the proxy path a PEER's purchase takes on this client. Re-sticker that card wherever it is
    /// lying in a peer's ghost hand fan, and state the outcome per peer in one line each.
    /// </summary>
    /// <param name="abilityCardId"><c>CEnhancement.AbilityCardID</c> — the card the shop wrote.</param>
    /// <param name="abilityName">The ability half the shop wrote, for the log only.</param>
    internal static void CardEnhanced(int abilityCardId, string? abilityName)
    {
        long t0 = Stopwatch.GetTimestamp();

        IReadOnlyList<RemoteHandFan> fans = RemoteHandFan.Live;
        if (fans.Count == 0)
        {
            Report(-1, abilityCardId, abilityName, achieved: true, fanCount: 0, slot: -1,
                   latchBefore: -1, latchAfter: -1,
                   before: EEnhancement.NoEnhancement, after: EEnhancement.NoEnhancement,
                   slotsFound: 0, slotsWritten: 0, failures: 0, t0,
                   "no peer hand fan exists on this client right now, so none can be stale — a fan "
                   + "built LATER borrows a pool widget the game has already re-stickered");
            return;
        }

        for (int f = 0; f < fans.Count; f++)
        {
            RemoteHandFan fan = fans[f];
            if (fan == null)
                continue;
            try
            {
                RefreshOneFan(fan, abilityCardId, abilityName, t0);
            }
            catch (System.Exception ex)
            {
                // This runs inside the game's own enhancement commit. One peer's fan failing must
                // never take the commit down, nor the other peers' fans, nor the LOCAL fan refresh
                // that runs after it.
                VRLog.Warn(Scope, "PEER HAND FAN CARD REFRESHED AFTER ENHANCEMENT: NOT ACHIEVED — "
                                  + $"peer: player {fan.OwnerPlayerId}; enhanced card id "
                                  + $"{abilityCardId}; the sweep itself threw: {ex.Message}. That "
                                  + "peer's fan face may be STALE until their fan is rebuilt.");
            }
        }
    }

    /// <summary>One peer's fan: find the enhanced card's stickers on its printed faces, write what a
    /// fresh print would draw, and emit that peer's falsifier line.</summary>
    private static void RefreshOneFan(RemoteHandFan fan, int abilityCardId, string? abilityName, long t0)
    {
        IReadOnlyList<GameObject> slabs = fan.Slabs;
        IReadOnlyList<int> printed = fan.PrintedMapCardIds;

        int slot = -1;
        int slotsFound = 0;
        int slotsWritten = 0;
        int failures = 0;
        int latchBefore = -1;
        EEnhancement before = EEnhancement.NoEnhancement;
        EEnhancement after = EEnhancement.NoEnhancement;

        for (int i = 0; i < slabs.Count; i++)
        {
            GameObject slab = slabs[i];
            if (slab == null)
                continue;

            // The face is a clone parented under the slab by RemoteCardArt (slab → host → clone).
            // A slab showing a BACK has no clone and therefore none of these, and needs none: its
            // future print is already current. RemoteCardArt strips only GraphicRaycaster,
            // CardEffects and ItemCardEffects from the clone, so the enhancement stickers — whose
            // identity fields are PUBLIC and thus copied verbatim by Instantiate — are all still there.
            EnhancementButtonBase[] stickers =
                slab.transform.GetComponentsInChildren<EnhancementButtonBase>(includeInactive: true);
            if (stickers.Length == 0)
                continue;

            int writtenOnThisSlab = 0;
            for (int s = 0; s < stickers.Length; s++)
            {
                EnhancementButtonBase sticker = stickers[s];
                if (sticker == null || sticker.AbilityCardID != abilityCardId)
                    continue;

                slotsFound++;
                if (slot < 0)
                {
                    slot = i;
                    latchBefore = i < printed.Count ? printed[i] : -1;
                }

                // WANTED STATE, read from the same place the game's own card UI reads it: the shared
                // CAbility.AbilityEnhancements slot the commit has just re-projected through
                // SaveDataShared.ApplyEnhancementIcons. That projection ran on THIS client for the
                // peer's character (the proxy path above committed here), so the model this reads is
                // the peer's own enhancement set, not ours. The getter walks CharacterClassManager
                // and can throw on a card the class pool no longer holds, so it is guarded and an
                // unreadable slot is left exactly as it was.
                EEnhancement want;
                try
                {
                    CEnhancement? model = sticker.Enhancement;
                    want = model != null ? model.Enhancement : EEnhancement.NoEnhancement;
                }
                catch (System.Exception ex)
                {
                    failures++;
                    VRLog.Debug(Scope, $"Peer enhancement refresh: card {abilityCardId} slot "
                                       + $"{sticker.EnhancementSlot} kept its sticker — the game "
                                       + $"model was unreadable: {ex.Message}");
                    continue;
                }

                EEnhancement had = sticker.EnhancementType;
                if (want == had)
                    continue;   // already what a fresh print would draw

                if (slotsWritten == 0)
                {
                    before = had;
                    after = want;
                }

                try
                {
                    // The game's OWN sticker writers, verbatim — the two arms
                    // SaveDataShared.ApplyEnhancementIcons takes on a pooled widget.
                    if (sticker is EnhancedAreaHex hex)
                    {
                        if (want == EEnhancement.NoEnhancement)
                            hex.RemoveEnhancement();
                        else
                            hex.ApplyEnhancement();
                    }
                    else if (sticker is EnhancementButton button)
                    {
                        button.UpdateEnhancement(want);
                    }
                    else
                    {
                        continue;   // an element kind the game itself does not draw here
                    }

                    slotsWritten++;
                    writtenOnThisSlab++;
                }
                catch (System.Exception ex)
                {
                    VRLog.Warn(Scope, $"Peer enhancement refresh: card {abilityCardId} slot "
                                      + $"{sticker.EnhancementSlot} refused the sticker write: "
                                      + ex.Message);
                    failures++;
                }
            }

            // THE SPRITE IT JUST ASSIGNED CAME OFF THE GAME'S MIPLESS UI ATLAS — UpdateEnhancement
            // reads the widget's own serialized sprite array, so a refreshed sticker has silently
            // left the mip bake every other Image on this face is in. One pass over the clone,
            // exactly the pass RemoteCardArt.RescanMips runs on its own 1 Hz cadence and against the
            // same shared cache (so it is dictionary hits, not bakes), re-swaps it. Doing it HERE
            // rather than waiting for that cadence is the difference between a correct icon now and
            // an aliased one for up to a second.
            if (writtenOnThisSlab > 0)
            {
                FullAbilityCard? clone =
                    slab.transform.GetComponentInChildren<FullAbilityCard>(includeInactive: true);
                if (clone != null)
                    Cards.CardFaceMipBake.Rescan(clone);
            }
        }

        // STRICT, exactly as the local half is: ONE refused slot is a stale sticker, so the verdict
        // is NOT ACHIEVED even when its neighbours landed. The counts are all in the line, so a
        // partial outcome stays readable — a verdict that averaged over slots would be the WORST-
        // field misread this project has been bitten by before.
        string note = slotsFound == 0
            ? "the enhanced card is NOT among the faces this peer's fan is drawing on this client — "
              + "nothing in their fan draws it, so nothing had to change"
            : failures > 0
                ? $"{failures} matching slot(s) refused the write — that much of the peer's fan face is STALE"
                : slotsWritten > 0
                    ? "the peer's fan face now draws what a fresh pool print would draw"
                    : "every matching slot already drew the committed enhancement";

        int latchAfter = slot >= 0 && slot < printed.Count ? printed[slot] : -1;

        Report(fan.OwnerPlayerId, abilityCardId, abilityName, achieved: failures == 0,
               slabs.Count, slot, latchBefore, latchAfter, before, after,
               slotsFound, slotsWritten, failures, t0, note);

        SlotsRefreshed += slotsWritten;
    }

    /// <summary>
    /// THE FALSIFIER, one line per peer fan, always emitted on the edge. It names the peer, the card
    /// that was enhanced, whether that card is among the faces that peer's fan is actually drawing,
    /// which slab slot it sits in, the PRINT LATCH before and after — deliberately unchanged, since
    /// the slab still draws the same card and only its face was corrected — the drawn enhancement
    /// before and after, whether a re-render was asked for and whether it landed, and the cost.
    ///
    /// <para>WHOSE LOG. This line is written by the client that is WATCHING, not by the one that
    /// bought: a peer's purchase reaches this client through the forwarded game action, so the
    /// "in that peer's fan: YES" line appears in the OTHER player's log. The buyer's own log carries
    /// ModBuild 239's local line instead.</para>
    /// </summary>
    private static void Report(int playerId, int cardId, string? abilityName, bool achieved,
        int fanCount, int slot, int latchBefore, int latchAfter, EEnhancement before,
        EEnhancement after, int slotsFound, int slotsWritten, int failures, long t0, string note)
    {
        double us = (Stopwatch.GetTimestamp() - t0) * 1000000.0 / Stopwatch.Frequency;
        VRLog.Info(Scope,
            "PEER HAND FAN CARD REFRESHED AFTER ENHANCEMENT: "
            + (achieved ? "CONFIRMED" : "NOT ACHIEVED")
            + $" — peer: {(playerId >= 0 ? "player " + playerId : "none on this client")}; "
            + $"enhanced card id {cardId}, ability '{abilityName ?? "?"}'; "
            + $"in that peer's visible fan: {(slotsFound > 0 ? "YES" : "no")}; "
            + $"fan slot: {(slot >= 0 ? slot.ToString() : "none")} of {fanCount}; "
            + $"print latch before: {latchBefore} -> after: {latchAfter} "
            + "(RemoteHandFan._mapPrinted for that slot; it holds the CARD ID the slab printed and "
            + "is left untouched ON PURPOSE — the slab still draws the same card, so re-printing it "
            + "would only buy a pool borrow, a widget clone, an async header reload and a blink); "
            + $"drawn enhancement before: {before} -> after: {after}; "
            + $"re-render requested: {(slotsFound > 0 ? "YES" : "no")} on {slotsFound} slot(s), "
            + $"completed: {slotsWritten}, refused: {failures}; "
            + $"cost {us:F1} us for the peer sweep so far and every write, this line excluded and "
            + "the LOCAL fan's own sweep not yet started (nothing ticks and nothing polls, so the "
            + "per-frame cost of the refresh is exactly zero); "
            + $"{note}.");
    }
}
