using System;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Original active-bonus tooltip presentation on an inert slot clone.</summary>
internal sealed class RemoteUseBarTooltip
{
    private GameObject? _bonusRoot, _itemRoot, _itemFront, _itemBack;
    private RawImage? _backArt;
    private CActiveBonus? _model;
    private ActiveBonusLayout? _layout;
    private int _tracker, _remaining, _strength;
    private CItem.EItemSlotState _itemState;
    private bool _itemKind;
    private readonly List<Material> _materials = new();
    private readonly List<ImageLoadingContext> _loads = new();
    private IDisposable? _itemEffects;
    private bool _disposed;
    private int _pendingLoads;
    private CanvasGroup[] _loadGroups = Array.Empty<CanvasGroup>();
    private static readonly MethodInfo CopyObject = typeof(object).GetMethod("MemberwiseClone",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static RemoteUseBarTooltip Capture(UIUseActiveBonus source, CActiveBonus? model, bool initialize)
    {
        UIUseActiveBonusTooltip tip = source.tooltip;
        var result = new RemoteUseBarTooltip
        {
            _bonusRoot = tip.activeAbilityTooltip.gameObject,
            _itemRoot = tip.itemTooltip.gameObject,
            _model = model, _layout = model?.Layout,
            _tracker = model?.TrackerIndex ?? 0, _remaining = model?.Remaining ?? 0,
            _strength = model?.Ability.Strength ?? 0,
            _itemState = model?.BaseCard is CItem item ? item.SlotState : CItem.EItemSlotState.None,
            _itemKind = model?.Layout == null && model?.BaseCard is CItem,
        };
        if (!initialize) return result;
        // MB487: serialized prefabs contain visible example tooltip content. The game's Init /
        // OnPointerExit hides it; those controller callbacks must never run on a display clone.
        Set(result._bonusRoot, false);
        Set(result._itemRoot, false);
        if (model == null) throw new InvalidOperationException("original bonus tooltip model is unavailable");
        try
        {
            if (!result._itemKind)
                PrepareBonus(tip.activeAbilityTooltip, model);
            else
                result.PrepareItem(tip.itemTooltip, (CItem)model.BaseCard);
        }
        catch { result.Destroy(); throw; }
        Set(result._bonusRoot, false);
        return result;
    }

    private void PrepareItem(UIItemTooltip tooltip, CItem item)
    {
        GameObject? borrowed = null;
        GameObject holder = new("NativeItemTooltipBorrow");
        holder.SetActive(false);
        try
        {
            borrowed = ObjectPool.SpawnCard(item.ID, ObjectPool.ECardType.Item, holder.transform,
                resetLocalScale: true, resetToMiddle: true, activate: false);
            if (borrowed == null) throw new InvalidOperationException("original item tooltip card is unavailable");
            GameObject clone = UnityEngine.Object.Instantiate(borrowed, tooltip.cardHolder, false);
            _itemFront = clone;
            ItemCardUI ui = clone.GetComponent<ItemCardUI>();
            ui.item = item;
            RectTransform rect = (RectTransform)clone.transform;
            rect.pivot = tooltip.cardPivot; rect.anchoredPosition = Vector2.zero;
            rect.localScale = Vector3.one;
            PrepareItemBack(rect);
            if (ui.modifiersTooltip != null) ui.modifiersTooltip.gameObject.SetActive(false);

            // ItemCardEffects.Initialize's original bounds write, on private clone materials.
            // Its controller/coroutines never run; the shared card renderer applies its visual FX.
            if (ui.cardEffects != null)
                foreach (Image image in ui.cardEffects.imgComp)
                {
                    if (image == null || image.material == null) continue;
                    Material material = new(image.material); _materials.Add(material); image.material = material;
                    if (material.HasProperty("_PosAndBounds"))
                        material.SetVector("_PosAndBounds", new Vector4(rect.position.x, rect.position.y,
                            rect.rect.width, rect.rect.height));
                }
            // Native ItemCardEffects sets its used look with a 0.001-second transition. Its
            // original smoke is sampled independently in record56, including native activation,
            // seed, age and board pose; this stage must not start or retain a second emitter.
            _itemEffects = RemoteCardArt.PrepareNativeItem(clone, item.SlotState switch
            {
                CItem.EItemSlotState.Spent => RemoteCardArt.SpentLook.Spent,
                CItem.EItemSlotState.Consumed => RemoteCardArt.SpentLook.Consumed,
                _ => RemoteCardArt.SpentLook.None,
            });

            // The original OnEnable loader cannot survive Neutralize. Use its pure loading
            // context against the same original Image and native sprite reference instead.
            _loadGroups = ui._imageAddressableLoader?._objectsToHideWhileLoad?.ToArray()
                ?? Array.Empty<CanvasGroup>();
            StartLoad(ui.cardBackground, UIInfoTools.Instance.GetItemBackgroundSprite(item.YMLData.Art));
            if (item.YMLData.ValidEquipCharacterClassIDs.Count > 0)
                StartLoad(ui.validOwnerIcon, UIInfoTools.Instance.GetCharacterAssemblyIcon(
                    item.YMLData.ValidEquipCharacterClassIDs[0]));
            clone.SetActive(true); // its stage parent is still inactive
        }
        finally
        {
            // The native item recycle branch leaves its parent unchanged. The shared return
            // detaches the game-owned borrow before this temporary holder can be destroyed.
            try { if (borrowed != null) RemoteItemCardSource.ReturnBorrowed(item.ID, borrowed); }
            finally { UnityEngine.Object.Destroy(holder); }
        }
    }

    private void PrepareItemBack(RectTransform original)
    {
        // ItemCardUI has no native reverse-side asset. Authorized selection coverage borrows
        // the SAME VR item back and captured silhouette as ItemsPile, on the original pixel rect.
        _itemBack = new GameObject("CoveredItemTooltip", typeof(RectTransform), typeof(RawImage));
        var rect = (RectTransform)_itemBack.transform;
        rect.SetParent(original.parent, false);
        rect.anchorMin = original.anchorMin; rect.anchorMax = original.anchorMax; rect.pivot = original.pivot;
        rect.sizeDelta = original.sizeDelta; rect.anchoredPosition3D = original.anchoredPosition3D;
        rect.localRotation = original.localRotation; rect.localScale = original.localScale;
        _backArt = _itemBack.GetComponent<RawImage>();
        var material = new Material(_backArt.defaultMaterial);
        material.mainTexture = CardMesh.CreateBackMaterial(CardBodyKind.Item).mainTexture;
        CardMesh.BindSilhouette(material, CardBodyKind.Item, CardMesh.SilhouetteLayer.Back);
        _materials.Add(material);
        _backArt.material = material; _backArt.texture = material.mainTexture;
        _backArt.raycastTarget = false;
        _itemBack.SetActive(false);
    }

    private void StartLoad(Image image, SpriteMemoryManagement.ReferenceToSprite reference)
    {
        if (reference.InitializedWithSpecialSprite)
        { image.sprite = reference.SpecialSprite; image.enabled = true; return; }
        var context = new ImageLoadingContext(); _loads.Add(context);
        _pendingLoads++;
        UpdateLoadGroups();
        _ = Load(context, image, reference);
    }

    private async Task Load(ImageLoadingContext context, Image image,
        SpriteMemoryManagement.ReferenceToSprite reference)
    {
        try { await context.LoadAsync(new object(), image, reference.SpriteReference); }
        catch (Exception e)
        {
            if (!_disposed) GloomhavenVR.Core.VRLog.Warn("Net", "NATIVE BONUS TOOLTIP ART: " + e.Message);
        }
        finally { _pendingLoads--; if (!_disposed) UpdateLoadGroups(); }
    }

    private void UpdateLoadGroups()
    {
        foreach (CanvasGroup group in _loadGroups)
            if (group != null) { group.enabled = true; group.alpha = _pendingLoads == 0 ? 1f : 0f; }
    }

    internal void Destroy()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (ImageLoadingContext context in _loads) context.Unload();
        _loads.Clear();
        _itemEffects?.Dispose(); _itemEffects = null;
        foreach (Material material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
        _materials.Clear();
    }

    private static void PrepareBonus(UIActiveAbility ui, CActiveBonus model)
    {
        // Layout is NOT a field on CActiveBonus: its setter writes Ability.ActiveBonusYML.
        // Isolate both objects and the layout before the original visual generator runs.
        var copy = (CActiveBonus)CopyObject.Invoke(model, null);
        var ability = (CAbility)CopyObject.Invoke(model.Ability, null);
        ability.ActiveBonusYML = model.Layout?.Copy();
        AccessTools.Field(typeof(CActiveBonus), "m_Ability").SetValue(copy, ability);
        UIActiveAbility.CheckForLayoutToGenerate(copy);

        // Native NormalizePool instantiates before parenting. Pre-size the exact native pools
        // under this inactive clone so Initialize cannot wake a prefab outside our hierarchy.
        int descriptions = copy.Layout == null ? 0
            : Math.Max(0, Math.Max(copy.Layout.IconNames.Count, copy.Layout.ListLayouts.Count) - 1);
        while (ui.extraDescriptionsUI.Count < descriptions)
        {
            GameObject child = UnityEngine.Object.Instantiate(ui.activeBonusDescriptionPrefab.gameObject,
                ui.abilityDescriptionHolder.transform, false);
            ui.extraDescriptionsUI.Add(child.GetComponent<UIActiveAbilityDescription>());
        }
        foreach (UIActiveAbilityDescription child in ui.extraDescriptionsUI)
            child.transform.SetParent(ui.abilityDescriptionHolder.transform, false);
        if (ui.abilityTracker != null)
        {
            PersistentAbilityTracker tracker = ui.abilityTracker;
            int count = copy.Layout?.TrackerPattern.Count ?? 0;
            while (tracker.elements.Count < count)
            {
                GameObject child = UnityEngine.Object.Instantiate(tracker.elementPrefab, tracker.stepsHolder, false);
                tracker.elements.Add(child.GetComponent<PersistentAbilityTrackerElement>());
            }
        }
        ui.ActiveBonus = null;
        ui.Initialize(copy, 1, forceGeneration: true);
        // UIUseActiveBonusTooltip.Show hides these original summon cells for its compact view.
        SummonContainer? summon = ui.GetComponentInChildren<SummonContainer>(true);
        if (summon != null)
        {
            summon.SummonLT.SetActive(false); summon.SummonLB.SetActive(false);
            summon.SummonMT.SetActive(false); summon.SummonMB.SetActive(false);
            LayoutElement? layout = summon.GetComponentInChildren<LayoutElement>(true);
            if (layout != null) layout.flexibleWidth = 0f;
        }
    }

    internal bool Matches(CActiveBonus? model) => ReferenceEquals(_model, model)
        && ReferenceEquals(_layout, model?.Layout) && _tracker == (model?.TrackerIndex ?? 0)
        && _remaining == (model?.Remaining ?? 0) && _strength == (model?.Ability.Strength ?? 0)
        && _itemState == (model?.BaseCard is CItem item ? item.SlotState : CItem.EItemSlotState.None);

    internal bool OwnsRoot(Transform source) =>
        _bonusRoot != null && source == _bonusRoot.transform || _itemRoot != null && source == _itemRoot.transform;

    internal RemoteUseBarTooltip Map(RemoteWidgetMirror mirror) => new()
    {
        _bonusRoot = mirror.CloneOf(_bonusRoot?.transform)?.gameObject,
        _itemRoot = mirror.CloneOf(_itemRoot?.transform)?.gameObject,
        _itemKind = _itemKind,
        _model = _model,
        _itemFront = mirror.CloneOf(_itemFront?.transform)?.gameObject,
        _itemBack = mirror.CloneOf(_itemBack?.transform)?.gameObject,
        _backArt = mirror.CloneOf(_backArt?.transform)?.GetComponent<RawImage>(),
    };

    // The independent native smoke stream uses the same owner visibility as this original
    // tooltip. Covering the card front does not hide the tooltip or invent a smoke episode.
    internal static bool IsShown(byte slotState, UseBarWidgetState? state) =>
        (slotState & NetProtocol.UseSlotHoveredBit) != 0 && (state?.Flags ?? 0) == 0;

    internal void Paint(byte slotState, UseBarWidgetState? state)
    {
        if (_loads.Count > 0) UpdateLoadGroups();
        // Native CheckShowTooltip: hovered, with neither the element nor option picker open.
        bool show = IsShown(slotState, state);
        Set(_bonusRoot, show && !_itemKind);
        bool itemFaceAllowed = !_itemKind || RevealGate.CardFaces(RevealGate.PeerCardPopulation.ItemCard,
            _model?.Actor as CPlayerActor) != RevealGate.CardFaceSource.None;
        Set(_itemRoot, show && _itemKind);
        // Only one side is active, including while asynchronous front art is still loading.
        Set(_itemFront, show && _itemKind && itemFaceAllowed);
        Set(_itemBack, show && _itemKind && !itemFaceAllowed);
        if (_backArt != null) _backArt.texture = _backArt.material.mainTexture;
    }

    private static void Set(GameObject? root, bool active)
    { if (root != null && root.activeSelf != active) root.SetActive(active); }
}
