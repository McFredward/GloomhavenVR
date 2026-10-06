using System.Collections.Generic;
using GloomhavenVR.Cards;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Native ownership chooses the terminal destination; existing ItemChip
/// and TLV107 presentation own the actual curve and per-frame remote playback.</summary>
internal static class TownServiceCardFlights
{
    private sealed class Retained
    {
        internal readonly ItemsPile Fan;
        internal ItemsPile.ItemChip? Pending;
        internal TownServiceToken? Stock;
        internal CMapCharacter? Character;
        internal CItem? Item;
        internal bool Selling;
        internal int BeforeCount;
        internal float Deadline;
        internal Vector3 MerchantPalm;
        internal readonly HashSet<CItem> Before = new();
        internal Retained(ItemsPile fan) { Fan = fan; }
    }
    private static readonly List<Retained> RetainedFans = new();
    private static readonly List<ItemsPile.ItemChip> Published = new();
    private static float _nextOutcomeWarning;
    internal static void WarnUnresolvedOutcome()
    {
        if (Time.unscaledTime < _nextOutcomeWarning) return;
        _nextOutcomeWarning = Time.unscaledTime + 30f;
        Core.VRLog.Warn("TownServices", "Merchant confirmed transaction produced no native inventory outcome within 8 s; returning its original card to the source.");
    }
    internal static IReadOnlyList<ItemsPile.ItemChip> Returning => Published;
    internal static bool HasRetained => RetainedFans.Count != 0;
    internal static bool IsPendingStock(TownServiceToken stock)
    { foreach (Retained entry in RetainedFans) if (ReferenceEquals(entry.Stock, stock)) return true; return false; }

    internal static void Retain(ItemsPile fan)
    {
        if (fan.RetainInspectionReturns()) RetainedFans.Add(new Retained(fan));
        else fan.DestroyInspection();
        RefreshPublished();
    }

    /// <summary>A confirmed native request may finish after the visitor changes character or
    /// leaves. Keep only that request's original model and original physical copies.</summary>
    internal static void RetainCommitted(ItemsPile fan, ItemsPile.ItemChip? chip, TownServiceToken? stock,
        CMapCharacter character, CItem item, bool selling, int beforeCount, IEnumerable<CItem> before,
        Vector3 merchantPalm, Transform? station)
    {
        var entry = new Retained(fan) { Pending = chip, Stock = stock, Character = character,
            Item = item, Selling = selling, BeforeCount = beforeCount,
            Deadline = Time.unscaledTime + 8f, MerchantPalm = merchantPalm };
        foreach (CItem owned in before) entry.Before.Add(owned);
        fan.RetainInspectionReturns(chip);
        if (stock != null && station != null) stock.RetainOffering(station);
        RetainedFans.Add(entry); RefreshPublished();
    }

    internal static void Tick()
    {
        for (int i = RetainedFans.Count - 1; i >= 0; i--)
        {
            Retained entry = RetainedFans[i];
            if (!MapRoom.MapRoomDriver.Active)
            { entry.Stock?.ReturnOffering(); entry.Fan.DestroyInspection(); RetainedFans.RemoveAt(i); continue; }
            if (entry.Item != null && entry.Character != null)
            {
                int count = 0; CItem? purchased = null;
                foreach (CItem owned in entry.Character.AllCharacterItems)
                {
                    if (owned == null || owned.ID != entry.Item.ID) continue;
                    count++;
                    if (!entry.Before.Contains(owned)) purchased = owned;
                }
                bool success = entry.Selling ? count < entry.BeforeCount : count > entry.BeforeCount;
                if (success || Time.unscaledTime >= entry.Deadline)
                {
                    if (success) Complete(entry.Fan, entry.Pending, entry.Stock, entry.Selling,
                        purchased, entry.MerchantPalm);
                    else
                    {
                        WarnUnresolvedOutcome();
                        Return(entry.Fan, entry.Pending, entry.Stock);
                    }
                    entry.Item = null; entry.Character = null; entry.Pending = null; entry.Stock = null;
                    entry.Before.Clear();
                }
            }
            bool moving = entry.Fan.TickRetainedInspection(entry.Pending);
            if (!moving && entry.Item == null)
            { entry.Fan.DestroyInspection(); RetainedFans.RemoveAt(i); }
        }
        RefreshPublished();
    }

    internal static void Complete(ItemsPile fan, ItemsPile.ItemChip? chip, TownServiceToken? stock,
        bool sold, CItem? purchased, Vector3 merchantPalm)
    {
        if (chip != null)
        {
            chip.TownOffering = false; chip.TownOfferingReclaimed = null;
            if (sold) fan.CompleteMerchantSale(chip, merchantPalm);
            else fan.ResumeInspection(chip);
        }
        if (stock == null) return;
        if (!sold && purchased != null && stock.PhysicalRoot != null)
        { BeginPurchase(fan, purchased, stock.PhysicalRoot); stock.RestoreMerchantStock(); }
        else stock.ReturnOffering();
    }

    private static void Return(ItemsPile fan, ItemsPile.ItemChip? chip, TownServiceToken? stock)
    {
        if (chip != null)
        { chip.TownOffering = false; chip.TownOfferingReclaimed = null; fan.ResumeInspection(chip); }
        fan.CancelPreparedMerchantPurchase();
        stock?.ReturnOffering();
    }

    private static void RefreshPublished()
    {
        Published.Clear();
        foreach (Retained entry in RetainedFans)
        { Published.AddRange(entry.Fan.InspectionChips);
          if (entry.Fan.PreparedMerchantPurchase != null) Published.Add(entry.Fan.PreparedMerchantPurchase); }
    }
    internal static void BeginPurchase(ItemsPile fan, CItem purchased, Transform sample)
    {
        RectTransform? printed = sample.GetComponentInChildren<ItemCardUI>(true)?.GetComponent<RectTransform>();
        float width = printed != null
            ? printed.rect.width * printed.TransformVector(Vector3.right).magnitude
            : TownServiceMerchantLayout.CardWidth * sample.lossyScale.x;
        fan.BeginMerchantPurchase(purchased, sample.position, sample.rotation, Mathf.Abs(width));
    }
}
