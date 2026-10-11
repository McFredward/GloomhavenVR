using System;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Net;
using ScenarioRuleLibrary;

public static partial class InteractionProgram
{
    private static void NativeBankPreparationProof(UIShopItemInventory inventory, Transform anchor)
    {
        Service prior = ShopService.Source; var service = new Service(); ShopService.Source = service;
        for (int i = 0; i < 18; i++) service.Buy.Add(new CItem(5100 + i));
        try
        {
            using var catalog = new TownServiceCatalog(inventory, anchor, () => service, () => true, anchor, persistent: true);
            catalog.SetVisibility(1f); Census(catalog);
            foreach (var entry in catalog.Entries) Set(entry, "_presentedAt", Time.unscaledTime - 1f);
            RemoteItemCardSource.Resident = false;
            Check(!catalog.PrepareOriginalBankForLoading(), "cold canonical artwork cannot authorize the original bank");
            RemoteItemCardSource.Resident = true;
            Check(!catalog.PrepareOriginalBankForLoading(), "resident sprite pins alone cannot authorize an original widget before native image arrival");
            var texture = new Texture2D(2, 2); var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
            foreach (var entry in catalog.Entries)
            {
                var card = entry.CardRoot.GetComponent<ItemCardUI>(); card.cardBackground = entry.CardRoot.GetComponent<Image>();
                card.cardBackground.sprite = sprite; card.cardBackground.enabled = true;
            }
            int resolved = NativeTemplates.Resolved, attempts = 0;
            while (!catalog.OriginalBankPrepared && attempts++ < 100) catalog.PrepareOriginalBankForLoading();
            Check(catalog.OriginalBankPrepared && catalog.PreparedOriginalEntries.Count == 18
                && NativeTemplates.Resolved - resolved == 18 * 5,
                "loading prepares every genuine original widget and observer template before first far category");
            int captures = GloomhavenVR.Cards.CardArtWatch.Captures, polls = GloomhavenVR.Cards.CardArtWatch.Polls;
            resolved = NativeTemplates.Resolved;
            for (int frame = 0; frame < 1000; frame++) Check(catalog.PrepareOriginalBankForLoading(), "ready original bank remains ready");
            Check(captures == GloomhavenVR.Cards.CardArtWatch.Captures && polls == GloomhavenVR.Cards.CardArtWatch.Polls
                && NativeTemplates.Resolved == resolved, "unchanged prepared bank performs zero native sprite widget or template census work");
            service.Price = 29; Census(catalog);
            Check(!catalog.OriginalBankPrepared, "genuine native price revision invalidates preparation rather than borrowing stale content");
            foreach (var entry in catalog.Entries) Check(entry.RowSource.LastPrice == 29, "native price bank refresh uses the real original initializer");
            attempts = 0; while (!catalog.OriginalBankPrepared && attempts++ < 100) catalog.PrepareOriginalBankForLoading();
            Check(catalog.OriginalBankPrepared, "changed originals become prepared again through the bounded source queue");
            UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture);
        }
        finally { ShopService.Source = prior; RemoteItemCardSource.Resident = true; }
    }
}
