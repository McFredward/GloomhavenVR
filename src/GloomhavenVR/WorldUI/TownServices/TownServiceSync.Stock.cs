using System;
using System.Globalization;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal sealed partial class TownServiceSync
{
    private static readonly TownServiceSync Stock = new();

    /// <summary>A lifted stock sample belongs to its visitor, independently of the public
    /// cabinet author and of the NPC currently receiving the visitor's other hand.</summary>
    private static void TickStock(Transform sharedFrame)
    {
        using (TownServiceMirror.UseStockLane()) Stock.TickStockCore(sharedFrame);
    }

    internal static void ResetStock()
    {
        using (TownServiceMirror.UseStockLane()) Stock.ResetCore();
    }

    private void TickStockCore(Transform sharedFrame)
    {
        TownServiceCatalog? catalog = TownServicePublicMerchant.Catalog;
        Transform? station = TownServicePublicMerchant.StationRoot;
        uint session = TownServicePublicMerchant.Session;
        bool moving = false;
        var abilityReturns = TownServiceEnhancementHandoff.Returning;
        var offeredAbility = TownServicePresentation.Service == 3 ? TownServicePresentation.Ritual?.Handoff : null;
        bool preparing = TownServiceMerchantHandoff.HasParkedOffer || offeredAbility?.Card != null
            || TownServiceMerchantHandoff.PreparedPurchase != null;
        foreach (ItemsPile.ItemChip chip in TownServiceMerchantHandoff.OwnedChips)
            if (chip != null && (chip.Holder != null || !ItemsPile.InspectionUsesAvatarTransport(chip)))
            { preparing = true; break; }
        bool returning = TownServiceCardFlights.HasRetained || abilityReturns.Count != 0;
        if (catalog != null && station != null)
            foreach (TownServiceCatalog.Entry entry in catalog.Entries)
                if (entry.Current && (entry.Sample.IsMoving || entry.Sample.HasReturnMotion)) { moving = true; break; }
        if (!moving && !returning && !preparing)
        { ResetCore(); return; }
        // Ability returns must not depend on having unlocked a merchant/cabinet.
        // This lane owns cosmetic originals only; its generation remains distinct
        // from every private service generation and has no gameplay continuation.
        if (station == null) station = abilityReturns.Count != 0 && abilityReturns[0].StationRoot != null
            ? abilityReturns[0].StationRoot : sharedFrame;
        if (session == 0) session = 1;
        _sharedFrame = sharedFrame;
        PrepareCore();
        if (!NativeTemplates.Ready || _generationExhausted) return;
        if (_session != session)
        {
            if (_generation == uint.MaxValue)
            {
                ResetCore(); _generationExhausted = true;
                Report("stock generation", new InvalidOperationException(
                    "Stock presentation generation exhausted; restart the mod to resume publishing."));
                return;
            }
            ResetCore(); _session = session; _service = 1; _nextId = 0; _generation++;
        }
        TownServiceMirror.BeginSession(1, _generation, sharedFrame, station,
            TownServicePublicMerchant.SessionAge);
        foreach (Published module in Modules.Values) module.Seen = false;
        foreach (SourceEntry source in Sources.Values) source.Seen = false;
        Visited.Clear(); Dynamic.Clear(); PriorityRoots.Clear();
        if (catalog != null) PublishStockEntries(catalog);
        PublishMerchantReturns();
        if (TownServiceMerchantHandoff.PreparedPurchase is ItemsPile.ItemChip purchase) PublishPreparedMerchantReturn(purchase);
        foreach (ItemsPile.ItemChip chip in TownServiceMerchantHandoff.OwnedChips)
            if (chip != null && (chip.Holder != null || !ItemsPile.InspectionUsesAvatarTransport(chip))) PublishPreparedMerchantReturn(chip);
        if (offeredAbility?.Card != null && offeredAbility.OfferedCardId > 0 && offeredAbility.Face != null)
        {
            TownServiceMirror.PrepareCardReturn(offeredAbility.Face, offeredAbility.Card.TryTownReturnMotion);
            Publish("face." + offeredAbility.OfferedCardId.ToString(CultureInfo.InvariantCulture), offeredAbility.Face, prewarm: true);
            Transform? backing = offeredAbility.Card.transform.Find("Visual/Backing");
            if (backing != null)
            { TownServiceMirror.PrepareCardReturn(backing, offeredAbility.Card.TryTownReturnMotion); Publish("map.cardbody", backing); }
        }
        foreach (TownServiceEnhancementHandoff.ReturnPresentation returningCard in abilityReturns)
        {
            Transform? face = returningCard.Face, body = returningCard.Body;
            if (face != null)
            {
                TownServiceMirror.RegisterCardReturn(face, returningCard.Card.TryTownReturnMotion);
                PriorityRoots.Add(face);
                Publish("face." + returningCard.CardId.ToString(CultureInfo.InvariantCulture), face, prewarm: true);
            }
            if (body != null)
            {
                TownServiceMirror.RegisterCardReturn(body, returningCard.Card.TryTownReturnMotion);
                PriorityRoots.Add(body); Publish("map.cardbody", body);
            }
        }
        Removed.Clear();
        foreach (var pair in Modules) if (!pair.Value.Seen) Removed.Add(pair.Key);
        foreach (string key in Removed)
        { TownServiceMirror.UnregisterModule(Modules[key].Id); Modules.Remove(key); }
        PruneSources();
    }

    private void PublishPreparedMerchantReturn(ItemsPile.ItemChip chip)
    {
        if (chip.NativeItemCard == null || chip.Item == null) return;
        Transform face = chip.NativeItemCard.transform;
        VRHand? hand = !chip.TownOffering && !chip.IsCollapsing
            ? VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left : null;
        TownServiceMirror.PrepareCardReturn(face, chip.TryTownReturnMotion, hand);
        Publish("item." + chip.Item.ID.ToString(CultureInfo.InvariantCulture), face, prewarm: true);
        if (chip.InspectionBody == null) return;
        TownServiceMirror.PrepareCardReturn(chip.InspectionBody, chip.TryTownReturnMotion, hand);
        Publish(TownServiceInspectionBody.Key(chip), chip.InspectionBody, prewarm: true);
    }

    private void PublishMerchantReturns()
    {
        // The visitor's private lane can already belong to another NPC. Keep only
        // these exact terminal originals in the existing independent cosmetic lane,
        // with their native clocks; no retired fan or transaction controller is mirrored.
        foreach (ItemsPile.ItemChip chip in TownServiceCardFlights.Returning)
        {
            if (chip == null || chip.NativeItemCard == null || chip.Item == null) continue;
            Transform face = chip.NativeItemCard.transform;
            Transform? body = chip.InspectionBody;
            VRHand? hand = !chip.TownOffering && !chip.IsCollapsing
                ? VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left : null;
            TownServiceMirror.RegisterCardReturn(face, chip.TryTownReturnMotion, hand);
            if (chip.TownOffering) TownServiceMirror.ExposeCommittedCardOriginal(face);
            TownServiceMirror.RegisterMotionOffering(face, chip.TownOffering);
            PriorityRoots.Add(face);
            Publish("item." + chip.Item.ID.ToString(CultureInfo.InvariantCulture), face, prewarm: true);
            if (body == null) continue;
            TownServiceMirror.RegisterCardReturn(body, chip.TryTownReturnMotion, hand);
            if (chip.TownOffering) TownServiceMirror.ExposeCommittedCardOriginal(body);
            TownServiceMirror.RegisterMotionOffering(body, chip.TownOffering);
            PriorityRoots.Add(body);
            Publish(TownServiceInspectionBody.Key(chip), body, prewarm: true);
        }
    }

    private void PublishStockEntries(TownServiceCatalog catalog)
    {
        foreach (TownServiceCatalog.Entry entry in catalog.Entries)
        {
            if (!entry.Current || !entry.Sample.IsMoving && !entry.Sample.HasReturnMotion) continue;
            // Freeze the same original while the avatar already owns the held pose.
            // Waiting until release made every .35-second return race its cold artwork;
            // observers mask this prepared duplicate only while the canonical avatar holds it.
            TownServiceMirror.RegisterCardReturn(entry.MountRoot, entry.Sample.TryCardReturnMotion);
            TownServiceMirror.RegisterCardReturn(entry.CardRoot, entry.Sample.TryCardReturnMotion);
            if (entry.BodyRoot != null) TownServiceMirror.RegisterCardReturn(entry.BodyRoot, entry.Sample.TryCardReturnMotion);
            if (entry.RowContent != null) TownServiceMirror.RegisterCardReturn(entry.RowContent, entry.Sample.TryCardReturnMotion);
            PriorityRoots.Add(entry.MountRoot); PriorityRoots.Add(entry.CardRoot);
            // The scenario's approved avatar holder already smooths the tracked
            // hand. Stock inspection must attach to that same frame rather than
            // interpolate another sampled world-space copy of the hand movement.
            TownServiceMirror.RegisterMotionHand(entry.MountRoot, entry.Sample.HoldingHand);
            TownServiceMirror.RegisterMotionHand(entry.CardRoot, entry.Sample.HoldingHand);
            if (entry.BodyRoot != null) TownServiceMirror.RegisterMotionHand(entry.BodyRoot, entry.Sample.HoldingHand);
            if (entry.BodyRoot != null) PriorityRoots.Add(entry.BodyRoot);
            if (entry.RowContent != null) PriorityRoots.Add(entry.RowContent);
            // These are the actual lifted originals. The address distinguishes the
            // visitor's sample from an identical owned item and from the shared rack;
            // the original model-aware face remains a child of this one moving mount.
            Publish("merchant.heldstock", entry.MountRoot, prewarm: true);
            Publish("item." + entry.ItemId.ToString(CultureInfo.InvariantCulture), entry.CardRoot, prewarm: true);
            Publish("merchant.heldstock.body", entry.BodyRoot, prewarm: true);
            if (entry.RowContent != null)
                Publish("merchant.heldstock.row", entry.RowContent, entry.RowSource.transform,
                    entry.RowCloneOf, prewarm: true);
        }
    }
}
