using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net;

/// <summary>
/// Native use-slot widgets, never a painted approximation. The MB482 hardware census names
/// Furniture/UseBarsDrawer/UseBar0/Caption as the 19.87 metre orange text. That caption, its dark
/// plate, square tiles and invented selection frames belonged to the replica, not the game.
///
/// A watcher cannot always clone a live bar: TakeDamagePanel.ShowOtherPlayer calls ResetToggles,
/// UIActiveBonusBar.Hide clears activeBonusSlots and parks its pool. In that case we clone the
/// SAME serialized slot prefab the game's GetSlotFromPool instantiates. Record 45 selects its
/// original icon; record 25 drives its original masks, CanvasGroup and button ColorBlock.
/// No SetActiveBonus, Init, Select, Refresh or Show method on a game slot may run here: those
/// methods register input handlers and some of them change the simulation.
/// </summary>
internal sealed class RemoteUseBarWidgets
{
    private readonly int _bar;
    private readonly int _count;
    private readonly RemoteWidgetMirror _mirror;
    private readonly Transform _mount;
    private GameObject? _stageHost;
    private RectTransform? _stage;
    private Transform? _source;
    private readonly List<Transform> _sourceSlots = new(8);
    private Slot[] _slots = Array.Empty<Slot>();
    private readonly Sprite?[] _wantedIcons;
    private int _stamp = -1;
    private bool _failedLogged;

    internal RemoteUseBarWidgets(Transform mount, int bar, int count)
    {
        _mount = mount;
        _bar = bar;
        _count = count;
        _wantedIcons = new Sprite?[count];
        // Width-only fit, like UseBarsSurface.StackDocked; open pickers grow downward.
        _mirror = new RemoteWidgetMirror("UseBar" + bar, mount, Cards.PlayTray.DecisionMountWidth,
            float.MaxValue, new Vector2(0f, -1f), densityScale: WorldUI.Surfaces.UseBarsSurface.DensityScale);
    }

    internal int Count => _count;
    internal float Height => _mirror.FittedSize.y;
    internal bool Available => _slots.Length == _count && _count != 0;

    internal void Refresh(CPlayerActor? actor, RemoteAvatar owner)
    {
        try
        {
            RectTransform? container = RemoteUseBarSymbols.ContainerOf(_bar);
            bool live = TryLiveSlots(container, actor, owner);
            Transform? source;
            if (live)
                source = container;
            else
            {
                if (_stage == null && container != null)
                    BuildPrefabStage(container);
                source = _stage;
            }
            bool changed = !ReferenceEquals(_source, source);
            _source = source;
            if (!_mirror.Refresh(source))
            {
                _slots = Array.Empty<Slot>();
                if (!_failedLogged)
                {
                    _failedLogged = true;
                    VRLog.Warn("Net", $"USE BAR WIDGETS: bar {_bar} withheld: {_mirror.Reason}. " +
                        "No replica or anonymous tile is substituted for a missing game widget.");
                }
                return;
            }
            if (changed || _stamp != _mirror.RebuildStamp || _slots.Length != _count)
            {
                _stamp = _mirror.RebuildStamp;
                _slots = new Slot[_count];
                for (int i = 0; i < _count; i++)
                {
                    Transform src = live ? _sourceSlots[i] : _stage!.GetChild(i);
                    // Prefab stage has already been stripped; bindings were captured before that.
                    Slot original = live ? Capture(src) : _stageSlots[i];
                    _slots[i] = original.Map(_mirror);
                }
                _failedLogged = false;
                VRLog.Info("Net", $"USE BAR WIDGETS: bar {_bar}, {_count} original game slot(s), " +
                    $"source={(live ? "live owner-matched bar" : "serialized game slot prefab")}; " +
                    "original borders, icons, masks and layout; all game behaviours stripped before activation.");
            }
            Tick(owner);
            // Refit after native masks/pickers changed; this never rebuilds a stable source.
            _mirror.Refresh(source);
            Paint(owner);
        }
        catch (Exception e)
        {
            _mirror.SetShown(false);
            _slots = Array.Empty<Slot>();
            if (!_failedLogged)
            {
                _failedLogged = true;
                VRLog.Warn("Net", $"USE BAR WIDGETS: bar {_bar} withheld after {e.Message}; no replica fallback.");
            }
        }
    }

    private bool TryLiveSlots(RectTransform? container, CPlayerActor? actor, RemoteAvatar owner)
    {
        _sourceSlots.Clear();
        if (container == null || actor == null || !RemoteUseBarSymbols.BarBelongsTo(_bar, actor))
            return false;
        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            if (!child.gameObject.activeSelf || !RemoteUseBarSymbols.IsSlot(child)
                || RemoteUseBarSymbols.RenderHiddenPlainItem(child))
                continue;
            int index = _bar * NetProtocol.UseBarsMaxSlots + _sourceSlots.Count;
            ushort id = owner.UseBarSlotIds != null && index < owner.UseBarSlotIds.Length
                ? owner.UseBarSlotIds[index] : (ushort)0;
            if (id != 0 && UseBarSlotSymbol.SlotId(_bar, child) != id)
                return false; // equal counts alone cannot prove that two rows have the same order
            _sourceSlots.Add(child);
        }
        return _sourceSlots.Count == _count;
    }

    private Slot[] _stageSlots = Array.Empty<Slot>();

    private void BuildPrefabStage(RectTransform container)
    {
        Component? prefab = _bar switch
        {
            0 => Singleton<UIActiveBonusBar>.Instance.activeBonusPrefab,
            1 => Singleton<UIUseAbilitiesBar>.Instance.slotPrefab,
            2 => Singleton<UIUseAugmentationsBar>.Instance.slotPrefab,
            3 => Singleton<UIUseItemsBar>.Instance.itemSlotPrefab,
            _ => null,
        };
        if (prefab == null)
            return;
        _stageHost = new GameObject("NativeUseBarSource", typeof(RectTransform));
        _stageHost.SetActive(false);
        _stageHost.transform.SetParent(_mount, false);
        // This is a source for a mirror, never a second visible widget. Zero scale on its PARENT
        // keeps its own rect poses at native pixel scale and guarantees that no stage glyph draws.
        _stageHost.transform.localScale = Vector3.zero;
        var frame = (RectTransform)_stageHost.transform;
        frame.sizeDelta = container.parent is RectTransform parent ? parent.rect.size : container.rect.size;
        GameObject stage = Object.Instantiate(container.gameObject, frame, false);
        RemoteWidgetMirror.Neutralize(stage, RemoteWidgetMirror.LayoutOwner.CloneAtBoardOwnersWidth, null);
        for (int i = stage.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(stage.transform.GetChild(i).gameObject);
        _stage = (RectTransform)stage.transform;
        _stageSlots = new Slot[_count];
        for (int i = 0; i < _count; i++)
        {
            GameObject go = Object.Instantiate(prefab.gameObject, _stage, false);
            _stageSlots[i] = Capture(go.transform);
            // Templates include dormant option/consume controls. A plain bonus is initialized by
            // the game with these hidden; stale serialized placeholder icons must never escape.
            foreach (UIUseOption option in go.GetComponentsInChildren<UIUseOption>(true))
                option.gameObject.SetActive(false);
            foreach (UIElementPicker picker in go.GetComponentsInChildren<UIElementPicker>(true))
                picker.gameObject.SetActive(false);
            foreach (UIOptionPicker picker in go.GetComponentsInChildren<UIOptionPicker>(true))
                picker.gameObject.SetActive(false);
            RemoteWidgetMirror.Neutralize(go, RemoteWidgetMirror.LayoutOwner.CloneAtBoardOwnersWidth, null);
            go.SetActive(true);
        }
        stage.SetActive(true);
        _stageHost.SetActive(true); // only inert presentation/layout components survive
        LayoutRebuilder.ForceRebuildLayoutImmediate(_stage);
    }

    internal void SetIcon(int index, Sprite? sprite)
    {
        if (index < 0 || index >= _wantedIcons.Length)
            return;
        _wantedIcons[index] = sprite;
        if (index < _slots.Length)
            ApplyIcon(_slots[index], sprite);
    }

    internal void Tick(RemoteAvatar owner)
    {
        _mirror.TickLive();
        Paint(owner);
    }

    private void Paint(RemoteAvatar owner)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            Slot slot = _slots[i];
            int at = _bar * NetProtocol.UseBarsMaxSlots + i;
            byte state = owner.UseBarSlotStates != null && at < owner.UseBarSlotStates.Length
                ? owner.UseBarSlotStates[at] : NetProtocol.UseSlotOfferedBit;
            bool offered = (state & NetProtocol.UseSlotOfferedBit) != 0;
            bool chosen = (state & NetProtocol.UseSlotChosenBit) != 0;
            bool hovered = (state & NetProtocol.UseSlotHoveredBit) != 0;
            bool pressed = (state & NetProtocol.UseSlotPressedBit) != 0;
            SetActive(slot.Selected, chosen);
            SetActive(slot.Optional, chosen && offered);
            SetActive(slot.Mandatory, (state & NetProtocol.UseSlotMandatoryBit) != 0);
            if (slot.Group != null)
            {
                float alpha = (state & NetProtocol.UseSlotDimmedBit) != 0 ? slot.DisabledAlpha : 1f;
                if (slot.Group.alpha != alpha)
                    slot.Group.alpha = alpha;
            }
            if (slot.Background != null)
            {
                // UIUseSlot never disables its ExtendedButton: the CanvasGroup carries disabled
                // alpha. Re-applying ColorBlock.disabledColor would dim this original widget twice.
                Color tint = (offered && pressed ? slot.Colors.pressedColor
                    : offered && hovered ? slot.Colors.highlightedColor : slot.Colors.normalColor)
                    * slot.Colors.colorMultiplier;
                slot.Background.canvasRenderer.SetColor(tint);
            }
            ApplyIcon(slot, _wantedIcons[i]);
            // A watcher may retain an old local popup. The owner's closed flags close that branch;
            // we never fabricate a picker from a badge when its content is unavailable locally.
            byte flags = owner.UseBarFlags != null && _bar < owner.UseBarFlags.Length
                ? owner.UseBarFlags[_bar] : (byte)0;
            if ((flags & NetProtocol.UseBarElementPickerBit) == 0)
                SetActive(slot.ElementPicker, false);
            if ((flags & NetProtocol.UseBarOptionPickerBit) == 0)
                SetActive(slot.OptionPicker, false);
        }
    }

    private static void ApplyIcon(Slot slot, Sprite? sprite)
    {
        if (slot.Icon == null)
            return; // augmentation widgets carry native child controls instead of a main icon
        if (slot.Icon.sprite != sprite)
            slot.Icon.sprite = sprite;
        if (slot.Icon.enabled != (sprite != null))
            slot.Icon.enabled = sprite != null;
    }

    private static void SetActive(GameObject? node, bool active)
    {
        if (node != null && node.activeSelf != active)
            node.SetActive(active);
    }

    // Field lookup is deliberate: these four widgets share a generic base, including PRIVATE
    // serialized fields. Capture once before Neutralize destroys their components; no hot-path
    // reflection and no guessed child names or "first Image" binding.
    private static T? Field<T>(Component component, string name) where T : class =>
        AccessTools.Field(component.GetType(), name)?.GetValue(component) as T;

    internal static bool MandatoryShown(Transform root)
    {
        Component? component = SlotComponent(root);
        return component != null && Field<GameObject>(component, "mandatoryHiglight") is GameObject node
            && node.activeSelf;
    }

    private static Component? SlotComponent(Transform root)
    {
        Component? c = root.GetComponent<UIUseActiveBonus>();
        c ??= root.GetComponent<UIUseAbility>();
        c ??= root.GetComponent<UIUseAugmentation>();
        c ??= root.GetComponent<UIUseItemScenario>();
        return c;
    }

    private static Slot Capture(Transform root)
    {
        Component? component = SlotComponent(root);
        if (component == null)
            return default;
        Selectable? button = Field<ExtendedButton>(component, "button");
        return new Slot
        {
            Icon = Field<Image>(component, component is UIUseItemScenario ? "imageItem" : "icon"),
            Selected = Field<GameObject>(component, "selectedMask"),
            Optional = Field<GameObject>(component, "optionalHiglight"),
            Mandatory = Field<GameObject>(component, "mandatoryHiglight"),
            Group = Field<CanvasGroup>(component, "canvasGroup"),
            DisabledAlpha = (float)(AccessTools.Field(component.GetType(), "disabledAlpha")?.GetValue(component) ?? 0.25f),
            Background = button != null ? button.targetGraphic : null,
            Colors = button != null ? button.colors : ColorBlock.defaultColorBlock,
            ElementPicker = Field<UIElementPicker>(component, "elementPicker")?.gameObject,
            OptionPicker = Field<UIOptionPicker>(component, "optionPicker")?.gameObject,
        };
    }

    private struct Slot
    {
        internal Image? Icon;
        internal GameObject? Selected, Optional, Mandatory, ElementPicker, OptionPicker;
        internal CanvasGroup? Group;
        internal Graphic? Background;
        internal ColorBlock Colors;
        internal float DisabledAlpha;
        internal Slot Map(RemoteWidgetMirror mirror) => new()
        {
            Icon = mirror.CloneOf(Icon != null ? Icon.transform : null)?.GetComponent<Image>(),
            Selected = mirror.CloneOf(Selected != null ? Selected.transform : null)?.gameObject,
            Optional = mirror.CloneOf(Optional != null ? Optional.transform : null)?.gameObject,
            Mandatory = mirror.CloneOf(Mandatory != null ? Mandatory.transform : null)?.gameObject,
            ElementPicker = mirror.CloneOf(ElementPicker != null ? ElementPicker.transform : null)?.gameObject,
            OptionPicker = mirror.CloneOf(OptionPicker != null ? OptionPicker.transform : null)?.gameObject,
            Group = mirror.CloneOf(Group != null ? Group.transform : null)?.GetComponent<CanvasGroup>(),
            Background = mirror.CloneOf(Background != null ? Background.transform : null)?.GetComponent<Graphic>(),
            Colors = Colors,
            DisabledAlpha = DisabledAlpha,
        };
    }

    internal void Destroy()
    {
        _mirror.Destroy();
        if (_stageHost != null)
            Object.Destroy(_stageHost);
    }
}
