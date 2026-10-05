using FFSNet;
using GloomhavenVR.Cards;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary;
using System.Collections.Generic;

namespace GloomhavenVR.WorldUI.MapRoom;

internal sealed partial class MapRoomHand
{
    private bool _merchantInspection, _templeInspection, _townInspectionFanWasOpen;
    private bool TownInspection => _merchantInspection || _templeInspection;
    // Opening another service must not temporarily republish ability cards over an
    // existing item fan simply to find out whether this character owns an ability.
    // Read the native loadout without changing either presentation or selection.
    internal static bool HasOwnedTownAbilityCards()
    {
        CMapCharacter? character = OwnedMerchantCharacter();
        if (character == null) return false;
        MapRoomHand? hand = s_live;
        if (hand == null) return false;
        var cards = hand._loadout;
        if (!ReferenceEquals(hand._character, character))
        {
            hand._townAbilityCensus.Clear();
            ResolveLoadout(character, hand._townAbilityCensus);
            cards = hand._townAbilityCensus;
        }
        foreach (CAbilityCard card in cards) if (card != null) return true;
        return false;
    }
    private readonly List<CAbilityCard> _townAbilityCensus = new(MaxCards);

    internal static CMapCharacter? OwnedMerchantCharacter()
    {
        CMapCharacter? character = MapCharacterSelection.Current(out _);
        return MapRoomDriver.Active && character != null
            && (!FFSNetwork.IsOnline || character.IsUnderMyControl) ? character : null;
    }
    // Map fans follow the native AllCharacterItems order (the handoff copies it verbatim).
    // Public held source101 also pins immutable item identity against same-size reorders;
    // viewer selection never substitutes for its character. Refuse ambiguous hashes/counts.
    internal static void CollectMerchantPreparationItems(List<int> into)
    {
        into.Clear();
        foreach (CMapCharacter character in PartyMembers())
            foreach (CItem item in character.AllCharacterItems) if (item != null && item.ID > 0) into.Add(item.ID);
        WorldUI.TownServiceCatalog? catalog = WorldUI.TownServicePublicMerchant.Catalog;
        if (catalog != null) foreach (var entry in catalog.Entries)
            if (entry.Current && !entry.Selling) into.Add(entry.ItemId);
    }

    internal static List<CItem>? ResolveMerchantItems(uint key)
    {
        if (key == 0) return null;
        CMapCharacter? match = null;
        foreach (CMapCharacter character in PartyMembers())
            if (Net.NetProtocol.HashMapKey(character.CharacterName) == key)
            { if (match != null) return null; match = character; }
        return match?.AllCharacterItems;
    }
    internal static bool TryNameMerchantItem(ItemsPile.ItemChip chip, out Net.TownItemHeldSource source)
    {
        source = default;
        uint key = chip?.Owner?.InspectionCharacterKey ?? 0;
        List<CItem>? items = ResolveMerchantItems(key);
        if (!MapRoomDriver.Active || chip == null || !chip.IsTownInspection || items == null || chip.Item == null) return false;
        int seat = items.IndexOf(chip.Item);
        if (seat < 0 || items.Count > ushort.MaxValue) return false;
        source = new Net.TownItemHeldSource(Net.TownItemHeldSource.Owned,
            key, chip.Item.ID, (ushort)seat, (ushort)items.Count);
        return source.Validate();
    }
    internal static CItem? ResolveMerchantHeldItem(Net.TownItemHeldSource source)
    {
        if (!source.Validate() || source.Kind != Net.TownItemHeldSource.Owned) return null;
        List<CItem>? items = ResolveMerchantItems(source.CharacterKey);
        return items != null && items.Count == source.Count && source.Seat < items.Count
            && items[source.Seat] != null && items[source.Seat].ID == source.ItemId ? items[source.Seat] : null;
    }
    internal static void SetTempleInspection(bool active)
    {
        MapRoomHand? hand = s_live;
        if (hand == null || hand._templeInspection == active) return;
        bool wasInspecting = hand.TownInspection;
        hand._templeInspection = active;
        hand.SetTownInspectionFan(active, wasInspecting, "temple donation pouch");
    }
    internal static void SetMerchantInspection(bool active)
    {
        MapRoomHand? hand = s_live;
        if (hand == null) return;
        if (hand._merchantInspection == active)
        {
            // Native transaction callbacks may request a normal hand rebuild. The
            // actual merchant inspection still owns this wrist until its teardown.
            if (active && CardsDriver.OffScenarioFanCards != null)
                hand.ReleaseFan("merchant owned-item inspection remains active");
            return;
        }
        bool wasInspecting = hand.TownInspection;
        hand._merchantInspection = active;
        hand.SetTownInspectionFan(active, wasInspecting, "merchant owned-item inspection");
    }

    private void SetTownInspectionFan(bool active, bool wasInspecting, string reason)
    {
        if (active)
        {
            if (wasInspecting) return;
            _townInspectionFanWasOpen = CardsDriver.OffScenarioFanIsOpen;
            if (_townInspectionFanWasOpen) CardsDriver.SuppressNextOffScenarioFanEdgeSound(open: false);
            ReleaseFan(reason);
            return;
        }
        if (TownInspection || !wasInspecting) return;
        if (_townInspectionFanWasOpen) CardsDriver.SuppressNextOffScenarioFanEdgeSound(open: true);
        _townInspectionFanWasOpen = false;
        if (MapRoomDriver.Active && _engaged) RebuildFan();
    }
}
