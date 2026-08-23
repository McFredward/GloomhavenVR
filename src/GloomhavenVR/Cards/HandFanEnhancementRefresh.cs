using System.Diagnostics;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE ENCHANTRESS EDGE — "wenn ich eine Karte bei der Magierin verändere, will ich dass die
/// entsprechende Karte auch direkt in meinem Handfächer geupdated ist, falls sie beim Character
/// ausgewählt ist" (user, 2026-08-23, item 4).
///
/// <para>WHAT WAS BROKEN, and why no instrument had seen it. The map-room hand fan does NOT adopt
/// a live game widget the way a scenario card does (<c>CardFace.Adopt</c>). It borrows an
/// <c>AbilityCardUI</c> from <c>ObjectPool</c> for the length of one call, clones its
/// <c>fullAbilityCard</c> subtree and hands the widget straight back
/// (<c>Net.RemoteAbilityCardSource.TryPooledClone</c> → <c>Net.RemoteCardArt.ShowFront</c>). The
/// clone is a SNAPSHOT: it is a mod-owned GameObject registered with nothing, so the game's own
/// card-face redraw — <c>SaveDataShared.ApplyEnhancementIcons</c>, which walks
/// <c>ObjectPool.GetAllCachedAbilityCards(cardID)</c> — cannot reach it, and three independent
/// latches guarantee it is never re-printed either:
/// <list type="bullet">
///   <item><c>MapRoomHand.BuildSignature</c> hashes character name/id/level and the LIST OF CARD
///   IDs; nothing about a card's CONTENT. An enhancement moves none of those, so
///   <c>Reconcile</c> returns on its first comparison and no rebuild is even considered.</item>
///   <item>the loadout diff claims a slab by <c>CAbilityCard.ID</c> and carries the existing
///   <c>RemoteCardArt</c> over with it;</item>
///   <item><c>PrintPendingFaces</c> skips every slot whose face is already non-null.</item>
/// </list>
/// So an enhancement bought at the Enchantress showed up on the shop's own card and on every
/// pooled game widget, and the card lying in his VR hand kept the face it was born with — for the
/// rest of the map visit.</para>
///
/// <para>WHY THIS IS AN IN-PLACE STICKER WRITE AND NOT A RE-PRINT. A re-print means destroying and
/// rebuilding the clone: a pool borrow, an <c>Object.Instantiate</c> of a whole card widget, a skin
/// re-hand, an async header-art reload and the fade-in the fan arms for a new face — milliseconds
/// and a visible blink, to move ONE sprite. The enhancement stickers on the clone are ordinary
/// copied <c>EnhancementButton</c> / <c>EnhancedAreaHex</c> components whose identity fields
/// (<c>AbilityCardID</c>, <c>AbilityName</c>, <c>EnhancementLine</c>, <c>EnhancementSlot</c>) are
/// PUBLIC and therefore copied verbatim by <c>Instantiate</c>. So this class does exactly what
/// <c>SaveDataShared.ApplyEnhancementIcons</c> does to a pooled widget — call the game's own
/// <c>UpdateEnhancement</c> / <c>ApplyEnhancement</c> / <c>RemoveEnhancement</c> — on the clone the
/// mod owns. Same machinery, same sprite table, same result as a fresh print, at no visual cost.
/// The invariant this holds is precisely: <b>the face in his hand equals the face a fresh print
/// would produce.</b></para>
///
/// <para>NOT A POLL. There is no per-frame comparison anywhere in this file and nothing ticks it.
/// The game raises no event on an enhancement commit — verified: <c>MapPartyEnhancementShopService.
/// AddEnhancement</c> contains no <c>Action</c>, no <c>UnityEvent</c>, and
/// <c>MapChoreographerUIEvents</c> (the game's actual bus, 16 events) has no enhancement member —
/// so the edge is taken with a Harmony postfix on the commit itself
/// (<see cref="Patches.MapPartyEnhancementShopService_AddEnhancement_FanRefresh"/>) and the whole
/// of this class runs on that one call. Cost when nobody enhances anything: zero.</para>
///
/// <para>ORDER IS LOAD-BEARING. The postfix is a POSTfix because the commit's LAST visual act is
/// <c>SaveDataShared.ApplyEnhancementIcons(character.Enhancements, character.CharacterID)</c>
/// (MapPartyEnhancementShopService.cs:58), which is what re-projects the character's persistent
/// enhancement list onto the shared <c>CAbility.AbilityEnhancements</c> slots this class reads back
/// through <c>EnhancementButtonBase.Enhancement</c>. A prefix would read the pre-commit state.</para>
///
/// <para>THE EDGE FIRING TWICE is a no-op: the second pass finds every slot already carrying the
/// wanted enhancement and writes nothing (see the <c>want == before</c> skip). THE EDGE NOT FIRING
/// AT ALL cannot leave a stale face either — it only fails to fire when no commit happened, and a
/// fan built LATER prints from the pool the game has already re-stickered.</para>
///
/// <para>MULTIPLAYER. Nothing here goes on the wire and nothing here reads the wire: card identity
/// stays local, exactly as the standing rule requires. The set this touches is
/// <c>CardsDriver.OffScenarioFanCards</c> — the LOCAL map-room loadout fan, published by
/// <c>WorldUI.MapRoom.MapRoomHand</c> — and a peer's fan is a different object graph owned by
/// <c>Net.RemoteHandFan</c>, which is not reachable from that list and is never walked here. A
/// peer's purchase does re-enter this code on the local client (the game routes
/// <c>ProxyBuyEnhancement</c> through the same <c>AddEnhancement</c>), and that is harmless and
/// correct: the sweep is keyed on the enhanced card's own <c>AbilityCardID</c>, so it matches
/// nothing unless that exact card is lying in the LOCAL fan, and if it is, re-reading the local
/// game state is the right answer anyway.</para>
/// </summary>
internal static class HandFanEnhancementRefresh
{
    private const string Scope = "Cards";

    /// <summary>Fan slots whose sticker this class has rewritten since the process started — the
    /// counter the falsifier's "did the re-render complete" term is read from.</summary>
    internal static int SlotsRefreshed { get; private set; }

    /// <summary>
    /// A card's enhancement set was just committed by the game. Re-sticker that card wherever it
    /// is lying in the local map-room hand fan, and state the outcome in one line.
    /// </summary>
    /// <param name="abilityCardId"><c>CEnhancement.AbilityCardID</c> — the card the shop wrote.</param>
    /// <param name="abilityName">The ability (top/bottom half) the shop wrote, for the log only.</param>
    internal static void CardEnhanced(int abilityCardId, string? abilityName)
    {
        // THE PEER HALF, FIRST AND SEPARATELY (ModBuild 240). The MULTIPLAYER note below is right
        // that a peer's purchase re-enters this method on the local client — the game routes
        // ProxyBuyEnhancement through this very AddEnhancement, verified — and right that a peer's
        // fan is a different object graph that this sweep can never reach. That second half was a
        // GAP, not a design: the peer's fan face is the same Object.Instantiate snapshot behind an
        // extra latch (RemoteHandFan._mapPrinted, keyed on CAbilityCard.ID, which an enhancement
        // does not move), so it went stale for the whole map visit. Net.RemoteFanEnhancementRefresh
        // is that set; it needs no new patch, no new wire field and no second channel, because this
        // one already fires for both cases. Called BEFORE t0 so the two halves' measured costs stay
        // separable, and it swallows its own failures so it can never sink the local refresh.
        Net.RemoteFanEnhancementRefresh.CardEnhanced(abilityCardId, abilityName);

        long t0 = Stopwatch.GetTimestamp();

        System.Collections.Generic.IReadOnlyList<VRCard>? fan = CardsDriver.OffScenarioFanCards;
        if (fan == null || fan.Count == 0)
        {
            Report(abilityCardId, abilityName, achieved: true, fanCount: 0, slot: -1,
                   before: EEnhancement.NoEnhancement, after: EEnhancement.NoEnhancement,
                   slotsFound: 0, slotsWritten: 0, failures: 0, t0,
                   "no hand-fan face exists on this client right now, so none can be stale — the "
                   + "next print borrows a pool widget the game has already re-stickered");
            return;
        }

        int slot = -1;
        int slotsFound = 0;
        int slotsWritten = 0;
        int failures = 0;
        EEnhancement before = EEnhancement.NoEnhancement;
        EEnhancement after = EEnhancement.NoEnhancement;

        for (int i = 0; i < fan.Count; i++)
        {
            VRCard card = fan[i];
            if (card == null)
                continue;

            // The face is a clone parented under the slab by Net.RemoteCardArt; a slab whose face
            // has not printed yet simply has none of these, and needs none — its future print is
            // already current.
            EnhancementButtonBase[] stickers =
                card.transform.GetComponentsInChildren<EnhancementButtonBase>(includeInactive: true);
            if (stickers.Length == 0)
                continue;

            int writtenOnThisCard = 0;
            for (int s = 0; s < stickers.Length; s++)
            {
                EnhancementButtonBase sticker = stickers[s];
                if (sticker == null || sticker.AbilityCardID != abilityCardId)
                    continue;

                slotsFound++;
                if (slot < 0)
                    slot = i;

                // WANTED STATE, read from the same place the game's own card UI reads it: the
                // shared CAbility.AbilityEnhancements slot the commit has just written. The getter
                // walks CharacterClassManager and can throw on a card the class pool no longer
                // holds, so it is guarded and an unreadable slot is left exactly as it was.
                EEnhancement want;
                try
                {
                    CEnhancement? model = sticker.Enhancement;
                    want = model != null ? model.Enhancement : EEnhancement.NoEnhancement;
                }
                catch (System.Exception ex)
                {
                    failures++;
                    VRLog.Debug(Scope, $"Enhancement refresh: card {abilityCardId} slot "
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
                    writtenOnThisCard++;
                }
                catch (System.Exception ex)
                {
                    failures++;
                    VRLog.Warn(Scope, $"Enhancement refresh: card {abilityCardId} slot "
                                      + $"{sticker.EnhancementSlot} refused the sticker write: "
                                      + ex.Message);
                }
            }

            // THE SPRITE IT JUST ASSIGNED CAME OFF THE GAME'S MIPLESS UI ATLAS — UpdateEnhancement
            // reads the widget's own serialized sprite array, so a refreshed sticker has silently
            // left the mip bake that every other Image on this face is in. One pass over the clone,
            // exactly the pass Net/RemoteCardArt.RescanMips runs on its own cadence and against the
            // same shared cache (so it is dictionary hits, not bakes), re-swaps it. Per CARD rather
            // than per sticker: Rescan walks a subtree, and the card is the subtree that matters.
            if (writtenOnThisCard > 0)
            {
                FullAbilityCard? clone =
                    card.transform.GetComponentInChildren<FullAbilityCard>(includeInactive: true);
                if (clone != null)
                    CardFaceMipBake.Rescan(clone);
            }
        }

        // STRICT: ONE refused slot is a stale sticker, so it is NOT ACHIEVED even when its
        // neighbours landed. A verdict that averages over the slots would be exactly the WORST-field
        // misread this project has been bitten by before — a summary that is best-case for the next
        // question. The counts are all in the line, so a partial outcome is still readable.
        string note = slotsFound == 0
            ? "the enhanced card is NOT in the selected character's hand on this client — nothing "
              + "in the fan draws it, so nothing had to change"
            : failures > 0
                ? $"{failures} matching slot(s) refused the write — that much of the fan face is STALE"
                : slotsWritten > 0
                    ? "the fan face now draws what a fresh pool print would draw"
                    : "every matching slot already drew the committed enhancement";

        Report(abilityCardId, abilityName, achieved: failures == 0,
               fan.Count, slot, before, after, slotsFound, slotsWritten, failures, t0, note);

        SlotsRefreshed += slotsWritten;
    }

    /// <summary>
    /// THE FALSIFIER, one line, always emitted on the edge. It names the card, whether that card is
    /// in the hand the fan is showing, which fan slot it sits in, the drawn enhancement before and
    /// after — the clone's own cache key, since the sticker sprite IS the cached artefact — whether
    /// a re-render was asked for and whether it landed, and what the whole pass cost.
    /// </summary>
    private static void Report(int cardId, string? abilityName, bool achieved, int fanCount,
        int slot, EEnhancement before, EEnhancement after, int slotsFound, int slotsWritten,
        int failures, long t0, string note)
    {
        double us = (Stopwatch.GetTimestamp() - t0) * 1000000.0 / Stopwatch.Frequency;
        string cardLabel = CardLabel(cardId);
        VRLog.Info(Scope,
            "HAND FAN CARD REFRESHED AFTER ENHANCEMENT: "
            + (achieved ? "CONFIRMED" : "NOT ACHIEVED")
            + $" — enhanced card {cardLabel}, ability '{abilityName ?? "?"}'; "
            + $"in the selected character's hand: {(slotsFound > 0 ? "YES" : "no")}; "
            + $"fan slot: {(slot >= 0 ? slot.ToString() : "none")} of {fanCount}; "
            + $"cache key before: {before} -> after: {after} "
            + "(the key is the sticker the CLONED face draws — the map-room fan holds no texture "
            + "cache, its face is an Object.Instantiate snapshot of a pool widget, so the drawn "
            + "enhancement per slot IS the cached value); "
            + $"re-render requested: {(slotsFound > 0 ? "YES" : "no")} on {slotsFound} slot(s), "
            + $"completed: {slotsWritten}, refused: {failures}; "
            + $"cost {us:F1} us for the whole sweep and every write, this line excluded "
            + "(it is the ONLY work this feature does — nothing ticks, nothing polls, so the "
            + "per-frame cost of the refresh is exactly zero); "
            + $"{note}.");
    }

    /// <summary>Best-effort human name for the enhanced card; the id alone if the class pool cannot
    /// be walked (it is a diagnostic, never a decision).</summary>
    private static string CardLabel(int cardId)
    {
        try
        {
            foreach (CCharacterClass klass in CharacterClassManager.Classes)
            {
                CAbilityCard card = klass.FindCardWithID(cardId);
                if (card != null)
                    return $"id {cardId} '{card.Name}'";
            }
        }
        catch
        {
            // fall through to the id
        }
        return $"id {cardId}";
    }
}
