using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.YML.Locations;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>The original temple model and callbacks without its flat-window entry lifecycle.
/// Preparing the purse and book never enables party selection, opens a window or changes the
/// selected character. Only a deliberate validated bowl drop invokes the original selection.</summary>
internal static class TownServiceTempleController
{
    private sealed class CounterLease : IDisposable
    {
        internal readonly RectTransform Source;
        private readonly Transform? _parent;
        private readonly int _sibling;
        private readonly Vector2 _min, _max, _pivot, _size;
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;
        private readonly GameObject _wrapper;
        private readonly List<Canvas> _canvases = new();
        internal CounterLease(Component source)
        {
            Source = (RectTransform)source.transform;
            _parent = Source.parent; _sibling = Source.GetSiblingIndex();
            _min = Source.anchorMin; _max = Source.anchorMax; _pivot = Source.pivot; _size = Source.sizeDelta;
            _position = Source.anchoredPosition3D; _scale = Source.localScale; _rotation = Source.localRotation;
            _wrapper = new GameObject("GloomhavenVR.Temple.NativeCounterClock", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform frame = (RectTransform)_wrapper.transform;
            frame.sizeDelta = _parent is RectTransform parent ? parent.rect.size : Source.rect.size;
            _wrapper.GetComponent<CanvasGroup>().alpha = 0f;
            _wrapper.GetComponent<CanvasGroup>().blocksRaycasts = false;
            // No flat Canvas exists above this active island. Even an independent nested
            // canvas must remain dormant; the physical book uses the original graphic clone.
            foreach (Canvas canvas in Source.GetComponentsInChildren<Canvas>(true))
                if (canvas.enabled) { _canvases.Add(canvas); canvas.enabled = false; }
            Move(frame);
        }
        private void Move(Transform? parent)
        {
            Source.SetParent(parent, false);
            Source.anchorMin = _min; Source.anchorMax = _max; Source.pivot = _pivot; Source.sizeDelta = _size;
            Source.anchoredPosition3D = _position; Source.localRotation = _rotation; Source.localScale = _scale;
        }
        public void Dispose()
        {
            if (Source != null)
            {
                Move(_parent);
                Source.SetSiblingIndex(_sibling);
                foreach (Canvas canvas in _canvases) if (canvas != null) canvas.enabled = true;
            }
            UnityEngine.Object.Destroy(_wrapper);
        }
    }
    private static readonly List<CounterLease> Counters = new();
    private static UITempleWindow? _counterOwner;
    private static UITempleWindow? _temple;
    private static CounterLease? _tooltipLease;
    private static object? _character;
    private static readonly List<TempleYML.TempleBlessingDefinition> Blessings = new();
    private static readonly List<bool> Available = new(), Affordable = new();
    private static int _gold, _level, _progress, _nextLevel;
    private static string? _language;
    private static float _nextSample;

    internal static bool Prepare(UIWindow window)
    {
        UITempleWindow? temple = window.GetComponent<UITempleWindow>();
        var character = MapRoomHand.OwnedMerchantCharacter();
        if (temple == null || temple.service == null || character == null) return false;
        NewPartyCharacterUI? selectedSlot = NewPartyDisplayUI.PartyDisplay?.SelectedUISlot;
        ICharacter? selected = selectedSlot != null && selectedSlot.State == PartySlotState.Assigned
            && selectedSlot.Service?.CharacterID == character.CharacterID ? selectedSlot.Service : null;
        if (selected == null) return false;
        bool changedController = !ReferenceEquals(_temple, temple);
        bool changedCharacter = !ReferenceEquals(_character, character) || !ReferenceEquals(temple.character, selected);
        if (!ReferenceEquals(_counterOwner, temple))
        {
            ReleaseCounters();
            Counters.Add(new CounterLease(temple.totalDonatedGold));
            Counters.Add(new CounterLease(temple.devotionProgress));
            _counterOwner = temple;
        }
        if (!changedController && !changedCharacter && Time.unscaledTime < _nextSample) return true;
        _nextSample = Time.unscaledTime + .25f;
        var blessings = temple.service.GetAvailableBlessings();
        bool inventoryChanged = changedController || blessings.Count != Blessings.Count;
        if (!inventoryChanged)
            for (int i = 0; i < blessings.Count; i++)
                if (!ReferenceEquals(blessings[i], Blessings[i])) { inventoryChanged = true; break; }
        int gold = temple.service.CalculateTotalGoldDonated(), level = temple.service.DevotionLevel;
        int progress = temple.service.DevotionCurrentProgress, nextLevel = temple.service.NextDevotionLevelAmount;
        string language = I2.Loc.LocalizationManager.CurrentLanguage;
        bool changed = inventoryChanged || changedCharacter || gold != _gold || level != _level
            || progress != _progress || nextLevel != _nextLevel || language != _language;
        if (inventoryChanged)
        {
            // The native slot pool owns all icons, wording and status. Its window can remain
            // inactive: no OnEnable, focus/navigation or modal layout is needed to prepare it.
            temple.Shop.Display(blessings, temple.service);
            Blessings.Clear(); Blessings.AddRange(blessings);
            Available.Clear(); Affordable.Clear();
            for (int i = 0; i < blessings.Count; i++) { Available.Add(false); Affordable.Add(false); }
        }
        for (int i = 0; i < blessings.Count; i++)
        {
            bool available = temple.service.IsAvailable(character.CharacterID, blessings[i]);
            bool affordable = temple.service.CanAfford(character.CharacterID, blessings[i]);
            changed |= Available[i] != available || Affordable[i] != affordable;
            Available[i] = available; Affordable[i] = affordable;
        }
        if (changed)
        {
            // This is EnterTemple's presentation-only preparation, using the exact character
            // already selected by the visitor. EnterTemple itself resets that selection and
            // opens the old window, so it is deliberately not part of an immersive visit.
            temple.character = selected;
            temple.Shop.Refresh(selected);
            if (changedController)
            {
                temple.totalDonatedGold.SetCount(gold);
                temple.devotionProgress.SetAmount(progress, nextLevel);
                temple.devotionLevel.SetArguments(level.ToString());
            }
            if (VRLog.WantsDebug)
                VRLog.Debug("TownServices", "Quiet temple context refreshed: character=" + character.CharacterID
                    + " blessings=" + blessings.Count + " originalWindowOpen=" + window.IsOpen);
        }
        _temple = temple; _character = character; _gold = gold; _level = level;
        _progress = progress; _nextLevel = nextLevel; _language = language;
        return true;
    }

    /// <summary>Only active native descendants of this exact logical temple are admitted.
    /// The native window root is a presentation boundary, not a permission or child-visibility gate.</summary>
    internal static bool OriginalVisible(Transform source, UIWindow window)
    {
        if (source == null || !TownServicePresentation.IsQuietTemple(window)
            || _temple == null || !ReferenceEquals(_temple.GetComponent<UIWindow>(), window)
            || !ReferenceEquals(_character, MapRoomHand.OwnedMerchantCharacter())) return false;
        for (Transform? current = source; current != null; current = current.parent)
        {
            if (ReferenceEquals(current, window.transform)) return true;
            if (!current.gameObject.activeSelf) return false;
            foreach (CounterLease counter in Counters)
                if (ReferenceEquals(_counterOwner, _temple) && ReferenceEquals(current, counter.Source)) return true;
        }
        return false;
    }

    internal static bool SelectOriginal(UITempleWindow temple, UITempleShopSlot slot)
    {
        if (temple == null || slot == null) return false;
        UIWindow window = temple.GetComponent<UIWindow>();
        var character = MapRoomHand.OwnedMerchantCharacter();
        if (!OriginalVisible(slot.transform, window) || character == null
            || temple.character?.CharacterID != character.CharacterID || slot.Blessing == null || !slot.IsAvailable
            || !temple.service.IsAvailable(character.CharacterID, slot.Blessing)
            || !temple.service.CanAfford(character.CharacterID, slot.Blessing)
            || !temple.service.CanBuy(character.CharacterID, slot.Blessing)) return false;
        // This event is wired by UITempleWindow.Awake to its real OnSelectedSlot. Its
        // confirmation, server validation, payment and native BuyBlessing remain untouched.
        temple.Shop.OnBlessingSelected.Invoke(slot.Blessing);
        return true;
    }

    internal static void HoverOriginal(UITempleWindow temple, UITempleShopSlot slot, bool shown)
    {
        if (_tooltipLease != null)
        {
            // Hide while its original popup is still active; an inactive ancestor would
            // make UIWindow.Hide refuse the close and leave a stale shown native tooltip.
            temple.Shop.tooltip.Hide();
            _tooltipLease.Dispose(); _tooltipLease = null;
        }
        // Populate the exact original modifier, warning and quantity through its own hover.
        // Inactive native Selectables do not receive EventSystem pointer-enter messages.
        temple.Shop.OnHovered(shown, slot);
        if (!shown) return;
        _tooltipLease = new CounterLease(temple.Shop.tooltip);
        UIWindow? tooltipWindow = temple.Shop.tooltip.GetComponent<UIWindow>();
        if (tooltipWindow != null) tooltipWindow.Show();
    }

    internal static void AnimateProxy(UITempleWindow temple, int previousLevel)
    {
        // ProxyBuyBlessing already executed service.Buy. A closed native window intentionally
        // skips this presentation in flat; the immersive book still owns these exact widgets.
        temple.totalDonatedGold.CountTo(temple.service.CalculateTotalGoldDonated());
        temple.devotionLevel.SetArguments(temple.service.DevotionLevel.ToString());
        int maxProgress = temple.service.NextDevotionLevelAmount;
        int progress = temple.service.DevotionCurrentProgress;
        if (temple.service.DevotionLevel > previousLevel)
        {
            int previousMaximum = temple.service.CalculateDevotionTotalProgress(previousLevel);
            temple.devotionProgress.PlayProgressTo(previousMaximum, previousMaximum, () =>
            {
                temple.devotionProgress.SetAmount(0f, maxProgress);
                if (progress > 0)
                    temple.devotionProgress.PlayProgressTo(temple.service.DevotionCurrentProgress, maxProgress);
            });
        }
        else temple.devotionProgress.PlayProgressTo(progress, maxProgress);
        _nextSample = 0f;
    }

    private static void ReleaseCounters()
    {
        // Restore in reverse order because both originals can share a pooled parent.
        for (int i = Counters.Count - 1; i >= 0; i--) Counters[i].Dispose();
        Counters.Clear(); _counterOwner = null;
    }

    internal static void Reset()
    {
        if (_tooltipLease != null)
        {
            if (_temple != null) _temple.Shop.tooltip.Hide();
            _tooltipLease.Dispose(); _tooltipLease = null;
        }
        ReleaseCounters();
        _temple = null; _character = null; _language = null; _nextSample = 0f;
        Blessings.Clear(); Available.Clear(); Affordable.Clear();
    }
}

/// <summary>Keep native book animation on the logical temple, without claiming its hidden
/// window is visible or replaying the native service transaction.</summary>
[HarmonyPatch(typeof(UITempleWindow), "ProxyBuyBlessing", typeof(string), typeof(TempleYML.TempleBlessingDefinition))]
internal static class QuietTempleProxyPresentation
{
    private static UITempleWindow? _reported;
    private static void Prefix(UITempleWindow __instance, out int __state)
    {
        __state = TownServicePresentation.IsQuietTemple(__instance.GetComponent<UIWindow>())
            && !__instance.GetComponent<UIWindow>().IsVisible ? __instance.service.DevotionLevel : -1;
    }
    private static void Postfix(UITempleWindow __instance, int __state)
    {
        if (__state < 0 || !TownServicePresentation.IsQuietTemple(__instance.GetComponent<UIWindow>())) return;
        try { TownServiceTempleController.AnimateProxy(__instance, __state); }
        catch (Exception error)
        {
            if (ReferenceEquals(_reported, __instance)) return;
            _reported = __instance;
            VRLog.Warn("TownServices", "Original quiet temple transaction completed, but book animation failed: " + error);
        }
    }
}
