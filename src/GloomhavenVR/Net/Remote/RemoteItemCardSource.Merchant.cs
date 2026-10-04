using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static partial class RemoteItemCardSource
{
    private static ObjectPool? s_preparedMapPool;
    private static readonly HashSet<int> PreparedMapTemplates = new();
    private static readonly FieldInfo? CardPools = typeof(ObjectPool).GetField("cardPools", BindingFlags.Instance | BindingFlags.NonPublic);
    private static bool HasPreparedPool(int id)
    {
        if (ObjectPool.instance == null || CardPools?.GetValue(ObjectPool.instance) is not List<ObjectPool.CardPool> pools) return false;
        foreach (var pool in pools)
            if (pool.CardID == id && pool.CardType == ObjectPool.ECardType.Item)
                return pool.Instances.Count > 0 && pool.Instances[0] != null;
        return false;
    }
    internal static bool PrepareMapItemForLoading(int id)
    {
        bool spritesReady = PrepareMapItem(id);
        if (ObjectPool.instance == null) return false;
        if (s_preparedMapPool != ObjectPool.instance)
        { s_preparedMapPool = ObjectPool.instance; PreparedMapTemplates.Clear(); }
        if (!PreparedMapTemplates.Contains(id) || !HasPreparedPool(id))
        {
            // The ordinary inactive borrow warms the original pool/prefab. No clone is
            // activated, no Show runs and no gameplay listener is added. Ownership ends
            // inside this call, using the same canonical item recycle as ShowFace.
            GameObject? holder = null, card = null;
            ItemCardUI? ui = null;
            try
            {
                if (WorldUI.TownServiceNativeAssets.FindItemData(id) == null) return false;
                holder = new GameObject("GloomhavenVR.ItemCardPreparation") { hideFlags = HideFlags.HideAndDontSave };
                holder.transform.SetParent(ObjectPool.instance.transform, false); holder.SetActive(false);
                card = ObjectPool.SpawnCard(id, ObjectPool.ECardType.Item, holder.transform,
                    resetLocalScale: true, resetToMiddle: true, resetLocalRotation: true, activate: false);
                if (card == null || (ui = card.GetComponent<ItemCardUI>()) == null) return false;
                ui.item = new CItem(id);
                PreparedMapTemplates.Add(id);
            }
            finally
            {
                if (card != null && ui != null) ReturnBorrowed(ui.CardID, card);
                else if (card != null) Object.Destroy(card);
                if (holder != null) Object.Destroy(holder);
            }
        }
        return spritesReady && PreparedMapTemplates.Contains(id) && HasPreparedPool(id);
    }
    // True means the original background and class icon are resident, not merely requested.
    internal static bool PrepareMapItem(int id) => TryPreparedMapItem(id, out _, out _);
    private static bool TryPreparedMapItem(CItem item, out Sprite? background, out Sprite? icon)
        => TryPreparedMapItem(item.ID, out background, out icon);
    private static bool TryPreparedMapItem(int id, out Sprite? background, out Sprite? icon)
    {
        background = icon = null;
        var data = WorldUI.TownServiceNativeAssets.FindItemData(id);
        if (data == null || UIInfoTools.Instance == null) return false;
        var reference = UIInfoTools.Instance.GetItemBackgroundSprite(data.Art);
        CardArtPin.PinReference(reference);
        background = CardArtPin.PreparedSprite(reference, out bool pending);
        if (pending || background == null) return false;
        if (data.ValidEquipCharacterClassIDs.Count == 0) return true;
        reference = UIInfoTools.Instance.GetCharacterAssemblyIcon(data.ValidEquipCharacterClassIDs[0]);
        CardArtPin.PinReference(reference);
        icon = CardArtPin.PreparedSprite(reference, out pending);
        return !pending && icon != null;
    }
}
