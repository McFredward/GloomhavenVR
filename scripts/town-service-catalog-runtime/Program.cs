using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Hands.Interact;
public static class InteractionProgram
{
    private static int assertions;
    private static void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
    private static RectTransform Rect(string name, Transform parent, float width = 100f, float height = 50f)
    { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(width, height); return rect; }
    private static float Alpha(Transform target)
    {
        float alpha = 1f;
        for (Transform? t = target; t != null; t = t.parent)
            foreach (CanvasGroup group in t.GetComponents<CanvasGroup>()) if (group.enabled) alpha *= group.alpha;
        return alpha;
    }
    private static void Census(TownServiceCatalog catalog) => typeof(TownServiceCatalog).GetMethod("RefreshRows", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(catalog, null);
    public static int Run()
    {
        assertions = 0;
        var root = new GameObject("CatalogFixture");
        var events = new GameObject("Events", typeof(EventSystem));
        var inventory = Rect("Inventory", root.transform).gameObject.AddComponent<UIShopItemInventory>();
        inventory.gameObject.AddComponent<CanvasGroup>().alpha = .6f;
        var scroll = Rect("Scroll", inventory.transform).gameObject.AddComponent<ScrollRect>();
        inventory.itemTooltip = Rect("OriginalDetails", inventory.transform, 400, 500).gameObject.AddComponent<UIPartyItemInventoryTooltip>();
        var detailCard = Rect("OriginalDetailCard", inventory.itemTooltip.transform).gameObject.AddComponent<ItemCardUI>();
        detailCard.AllHintsCardTooltip = Rect("OriginalRulesTarget", detailCard.transform).gameObject.AddComponent<UITextTooltipTarget>();
        inventory.itemTooltip.m_ItemCardUI = detailCard;
        NativeTemplates.Tooltip = Rect("NativeGlobalHint", root.transform).gameObject.AddComponent<UITooltip>();
        inventory.scroll = scroll; scroll.viewport = Rect("Viewport", scroll.transform, 500, 500);
        scroll.content = Rect("Content", scroll.viewport, 500, 1800); scroll.content.pivot = new Vector2(.5f, 1f);
        inventory.buyTab = Rect("Buy", inventory.transform); inventory.sellTab = Rect("Sell", inventory.transform);
        inventory.allFilter = Rect("All", inventory.transform); inventory._ownedFilter = Rect("Owned", inventory.transform);
        inventory.headFilter = Rect("Head", inventory.transform); inventory.bodyFilter = Rect("Body", inventory.transform);
        inventory.handsFilter = Rect("Hands", inventory.transform); inventory.legsFilter = Rect("Legs", inventory.transform);
        inventory.smallItemsFilter = Rect("Small", inventory.transform);
        var original = new[] { inventory.buyTab, inventory.sellTab, inventory.allFilter, inventory._ownedFilter,
            inventory.headFilter, inventory.bodyFilter, inventory.handsFilter, inventory.legsFilter, inventory.smallItemsFilter };
        int clicks = 0;
        for (int i = 0; i < 17; i++)
        {
            var slot = Rect("Row" + i, scroll.content, 500, 50).gameObject.AddComponent<UIShopItemSlot>();
            slot.gameObject.AddComponent<CatalogHoverProbe>();
            slot.Item = new ScenarioRuleLibrary.CItem(i + 1); var button = slot.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => clicks++); slot.Selectable = button; inventory.slotPool.Add(slot);
        }
        // Pool enumeration deliberately disagrees with hierarchy order.
        inventory.slotPool.Reverse();
        var anchor = new GameObject("Counter"); anchor.transform.SetParent(root.transform, false);
        anchor.transform.SetPositionAndRotation(new Vector3(3, 1, -2), Quaternion.Euler(0, 37, 0));
        anchor.transform.localScale = Vector3.one * 2f;
        object context = new object(); bool alive = true;
        try
        {
            for (int repetition = 0; repetition < 3; repetition++)
            {
                // Desktop merchant prefabs have no gamepad-only Owned filter. The previous
                // fixture supplied every field and therefore could not reproduce build 540.
                inventory._ownedFilter = repetition == 1 ? null! : original[3];
                TownServiceCatalog catalog;
                try { catalog = new TownServiceCatalog(inventory, anchor.transform, () => context, () => alive, anchor.transform); }
                catch (NullReferenceException e) when (repetition == 1)
                { throw new Exception("desktop merchant opens without a gamepad Owned filter", e); }
                Check(catalog.Controls.Count == (repetition == 1 ? 8 : 9), "only existing native merchant controls are presented");
                bool ownsFilter = false;
                foreach (var control in catalog.Controls) ownsFilter |= control.Key == "merchant.filter.owned";
                Check(ownsFilter == (repetition != 1), "optional owned filter follows original prefab availability");
                Census(catalog); catalog.Tick(2f); catalog.SetVisibility(.35f); catalog.LateTick();
                Check(Mathf.Abs(catalog.Root.GetComponent<CanvasGroup>().alpha - .35f) < .001f, "owner visibility applies to cards and navigation");
                foreach (var control in catalog.Controls)
                    Check(Mathf.Abs(control.Surface.Panel.HostGo.GetComponent<CanvasGroup>().alpha - .21f) < .001f,
                        "counter visibility multiplies original native fade");
                catalog.SetVisibility(1f);
                Check(catalog.PageCount == 3 && catalog.Entries.Count == 6, "bounded six-card native catalog");
                Check(catalog.Entries[0].ItemId == 1, "native hierarchy order wins over pool order");
                Check(scroll.viewport.parent.GetComponent<CanvasGroup>().alpha == 0, "old viewport is hidden");
                foreach (var control in catalog.Controls)
                {
                    Check(Vector3.Dot(control.Surface.Panel.HostGo.transform.forward, Vector3.down) > .99f, "controls lie on counter");
                    Check(control.Surface.Panel.Target.parent != inventory.transform, "original control handed to counter");
                }
                foreach (var entry in catalog.Entries)
                {
                    Check(Vector3.Dot(entry.CardRoot.forward, Vector3.down) > .89f
                        && Vector3.Dot(entry.CardRoot.forward, Vector3.down) < .92f, "physical card is raised toward the customer");
                    Check(entry.Sample.IsPhysical && entry.BodyRoot != null, "merchant presents a rigid inspect-only card");
                    Check(entry.BodyRoot!.parent == entry.CardRoot.parent.parent, "body and original face share one physical card");
                    Check(entry.BodyRoot.localScale.z == 1f, "physical card retains millimetre thickness");
                    var corners = new Vector3[4]; ((RectTransform)entry.CardRoot).GetWorldCorners(corners);
                    Check(Vector3.Distance(corners[0], corners[3]) <= .361f, "card width respects counter scale");
                    Check(entry.CardRoot.parent.GetComponent<GraphicRaycaster>() != null, "physical card has a raycaster");
                    Check(entry.CardUI.item == entry.Item && entry.Sample.CanGrab, "valid original card identity before Show");
                }
                catalog.TurnPage(1); Check(catalog.Page == 1 && catalog.Entries[0].ItemId == 7, "physical next page advances entries");
                catalog.TurnPage(1); Check(catalog.Page == 2 && catalog.Entries.Count == 5 && catalog.Entries[4].ItemId == 17, "last page retains remainder");
                catalog.TurnPage(1); Check(catalog.Page == 2, "last page cannot overflow");
                catalog.TurnPage(-1); catalog.TurnPage(-1);
                var scrollPointer = catalog.Entries[0].CardRoot.parent.GetComponent<TownServiceCatalogPointer>();
                scrollPointer.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down });
                Check(catalog.Page == 1, "physical card scroll advances page");
                var nextPointer = catalog.Entries[0].CardRoot.parent.GetComponent<TownServiceCatalogPointer>();
                nextPointer.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = Vector2.down });
                Check(catalog.Page == 1, "page rebuild preserves scroll repeat throttle");
                catalog.TurnPage(-1);
                var entry0 = catalog.Entries[0];
                var pointer = entry0.CardRoot.parent.GetComponent<TownServiceCatalogPointer>();
                inventory.itemTooltip.transform.SetParent(entry0.RowSource.transform, false);
                detailCard.item = new ScenarioRuleLibrary.CItem(entry0.Item.ID); inventory.itemTooltip.IsShown = true;
                int enters = detailCard.AllHintsCardTooltip.Enters;
                catalog.LateTick(); catalog.LateTick();
                Check(catalog.PreviewSource == inventory.itemTooltip.transform && catalog.PreviewContent != null,
                    "preview retains original tooltip provenance");
                Check(catalog.PreviewContent != null, "native tooltip reuse accepts equivalent item identities");
                Check(Alpha(inventory.itemTooltip.transform) == 0f && Alpha(catalog.PreviewContent!) > .99f,
                    "detail clone escapes hidden native viewport");
                Check(catalog.PreviewCloneOf(detailCard.transform) != null && detailCard.transform.parent == inventory.itemTooltip.transform,
                    "nested native card maps without reparenting original");
                Check(detailCard.AllHintsCardTooltip.Enters == enters + 1 && catalog.HintContent != null,
                    "native rules target entered exactly once");
                Check(Alpha(NativeTemplates.Tooltip!.transform) == 0f && Alpha(catalog.HintContent!) > .99f,
                    "native shared hint is masked while its copy is visible");
                inventory.itemTooltip.IsShown = false; catalog.LateTick();
                Check(catalog.PreviewContent == null && catalog.HintContent == null, "native hover exit hides copied details");
                inventory.itemTooltip.IsShown = true; catalog.LateTick();
                var otherAnchor = Rect("OtherHintOwner", inventory.transform);
                NativeTemplates.Tooltip!.m_AnchorToTarget = otherAnchor;
                catalog.TurnPage(1);
                Check(NativeTemplates.Tooltip.gameObject.activeSelf && Alpha(NativeTemplates.Tooltip.transform) > .99f, "ending inspect never hides another native tooltip");
                Check(catalog.PreviewContent == null, "page change hides copied details immediately");
                catalog.TurnPage(-1); entry0 = catalog.Entries[0];
                pointer = entry0.CardRoot.parent.GetComponent<TownServiceCatalogPointer>();
                pointer.OnPointerClick(new PointerEventData(EventSystem.current));
                Check(clicks == repetition + 1, "physical card dispatches native select once");
                entry0.RowSource.Selectable.interactable = false;
                pointer.OnPointerClick(new PointerEventData(EventSystem.current));
                Check(clicks == repetition + 1, "native disabled selection remains disabled");
                entry0.RowSource.Selectable.interactable = true;
                var hover = entry0.RowSource.GetComponent<CatalogHoverProbe>();
                int oldEnters = hover.Enters, oldExits = hover.Exits;
                pointer.OnPointerEnter(new PointerEventData(EventSystem.current));
                pointer.OnPointerEnter(new PointerEventData(EventSystem.current));
                Check(hover.Enters == oldEnters + 1, "physical hover enters native row only once");
                var sample = entry0.Sample;
                entry0.RowSource.Item = new ScenarioRuleLibrary.CItem(99);
                Check(!sample.CanGrab, "rebound row immediately fences stale sample");
                catalog.LateTick();
                Check(catalog.PreviewContent == null, "rebound tooltip item cannot appear under new row identity");
                Census(catalog); Check(sample.Disposed && catalog.Entries[0].ItemId == 99, "row reuse retires old sample before rebuild");
                Check(hover.Exits == oldExits + 1, "row rebind retires previous native hover");
                catalog.LateTick(); Check(catalog.PreviewContent == null, "rebound tooltip item cannot appear under new row identity");
                catalog.Entries[0].RowSource.Item = new ScenarioRuleLibrary.CItem(1);
                catalog.Entries[0].RowSource.gameObject.SetActive(false);
                Census(catalog); Check(catalog.Entries[0].ItemId == 2 && catalog.Page == 0, "filter hides row and resets page");
                foreach (var row in inventory.slotPool) row.gameObject.SetActive(true);
                Census(catalog);
                catalog.TurnPage(1); context = new object(); Census(catalog);
                Check(catalog.Page == 0, "character or mode change resets page");
                alive = false; catalog.Tick(2f);
                Check(detailCard != null && detailCard.transform.parent == inventory.itemTooltip.transform, "disposing preview preserves original pooled tooltip card");
                Check(catalog.Entries.Count == 0 && ObjectPool.Alive == 0, "closing returns every pooled card");
                Check(UguiPokeSurfaces.Registered.Count == 0, "closing unregisters every physical input surface");
                Check(CanvasConversion.Active.Count == 0, "closing retires every control conversion");
                foreach (Transform control in original) Check(control.parent == inventory.transform, "off restores original native control parent");
                Check(scroll.viewport.parent == scroll.transform, "off removes viewport suppression immediately");
                Check(clicks == repetition + 1, "toggle or cancel never confirms transaction");
                alive = true;
            }
            return assertions;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(events); }
    }
}
