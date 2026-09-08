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
    private int _stageActorId;
    private bool _failedLogged;
    private readonly ushort[] _stageIds;
    private readonly int[] _stageConsumeCounts, _stageOptionCounts;
    private readonly CActiveBonus?[] _stageModels;

    internal RemoteUseBarWidgets(Transform mount, int bar, int count)
    {
        _mount = mount;
        _bar = bar;
        _count = count;
        _wantedIcons = new Sprite?[count];
        _stageIds = new ushort[count];
        _stageConsumeCounts = new int[count];
        _stageOptionCounts = new int[count];
        _stageModels = new CActiveBonus?[count];
        // Width-only fit, like UseBarsSurface.StackDocked; open pickers grow downward.
        _mirror = new RemoteWidgetMirror("UseBar" + bar, mount, Cards.PlayTray.DecisionMountWidth,
            float.MaxValue, new Vector2(0f, -1f), densityScale: WorldUI.Surfaces.UseBarsSurface.DensityScale, contentOutsideFrame: true);
    }

    internal int Count => _count;
    private float PaddingMeters => _mirror.MeasuredSizePx.y > 0f
        ? WorldUI.CanvasConversion.FitContentPaddingPx * _mirror.FittedSize.y / _mirror.MeasuredSizePx.y : 0f;
    internal float Height => Mathf.Max(0f, _mirror.FittedSize.y - 2f * PaddingMeters);
    internal bool Available => _slots.Length == _count && _count != 0;

    internal void Refresh(CPlayerActor? actor, RemoteAvatar owner)
    {
        try
        {
            RectTransform? container = RemoteUseBarSymbols.ContainerOf(_bar);
            bool live = TryLiveSlots(container, actor, owner);
            // Active-bonus subchoices are owner-local. Once their descriptor exists, the original
            // prefab is the stable source; a viewer's live picker must not overwrite their choices.
            if (_bar == 0 && owner.UseBarWidgetStates != null)
                live = false;
            Transform? source;
            if (live)
                source = container;
            else
            {
                if ((_stage == null || !StageMatches(actor, owner)) && container != null)
                {
                    if (_stageHost != null)
                    {
                        _stageHost.SetActive(false);
                        Object.Destroy(_stageHost);
                    }
                    _stage = null;
                    BuildPrefabStage(container, actor, owner);
                }
                for (int i = 0; i < _stageSlots.Length; i++)
                {
                    UseBarWidgetState? state = Descriptor(owner, i);
                    if (state != null) _stageSlots[i].Subwidgets?.Paint(state);
                    PaintMasks(_stageSlots[i], State(owner, i), state);
                }
                if (_stage != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_stage);
                source = _stage;
            }
            bool changed = !ReferenceEquals(_source, source);
            _source = source;
            if (!_mirror.Refresh(source))
            {
                ReleaseSlots();
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
                ReleaseSlots();
                _slots = new Slot[_count];
                for (int i = 0; i < _count; i++)
                {
                    Transform src = live ? _sourceSlots[i] : _stage!.GetChild(i);
                    // Prefab stage has already been stripped; bindings were captured before that.
                    Slot original = live ? Capture(src) : _stageSlots[i];
                    if (live && _bar == 0 && src.GetComponent<UIUseActiveBonus>() is UIUseActiveBonus bonus)
                        original.Subwidgets = RemoteUseBarSubwidgets.Capture(bonus, Model(actor, owner, i),
                            actor, Descriptor(owner, i), false);
                    _slots[i] = original.Map(_mirror);
                    _slots[i].ActorId = live ? NetFigures.StableActorId(actor) : _stageActorId;
                    _slots[i].Identity = live ? SlotId(owner, i) : _stageIds[i];
                }
                _failedLogged = false;
                VRLog.Info("Net", $"USE BAR WIDGETS: bar {_bar}, {_count} original game slot(s), " +
                    $"source={(live ? "live owner-matched bar" : "serialized game slot prefab")}; " +
                    "original borders, icons, masks and layout; all game behaviours stripped before activation.");
            }
            _mirror.SetShown(true);
            Tick(owner);
            // Refit after native masks/pickers changed; this never rebuilds a stable source.
            _mirror.Refresh(source);
            Paint(owner);
            // UseBarsSurface pins the VISIBLE top, not the padded host top.
            if (_mirror.HostCanvas != null)
                _mirror.HostCanvas.transform.localPosition += Vector3.up * PaddingMeters;
            ApplyAnimations(owner); // final writer, after refit and native masks/hover
        }
        catch (Exception e)
        {
            _mirror.SetShown(false);
            ReleaseSlots();
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

    private static void FinishNativeShowPose(Transform root)
    {
        Component? component = SlotComponent(root);
        if (component == null || Field<GUIAnimator>(component, "showAnimation") is not LeanTweenGUIAnimator animation)
            return;
        // Read the original presentation recipe. Never run GUIAnimator events or custom effects,
        // and never allow a serialized target outside this clone to receive a write.
        foreach (LeanTweenGUIAnimationSetting setting in animation.GetSettings())
        {
            RectTransform? target = setting switch
            {
                LeanTweenGuiAnimationSettingScale scale => scale.Target,
                LeanTweenGuiAnimationSettingMove move => move.Target,
                LeanTweenGuiAnimationSettingFade fade => fade.Target,
                _ => null,
            };
            if (target != null && (ReferenceEquals(target, root) || target.IsChildOf(root)))
                setting.SetFinalValue();
        }
    }

    private Slot[] _stageSlots = Array.Empty<Slot>();

    private void BuildPrefabStage(RectTransform container, CPlayerActor? actor, RemoteAvatar owner)
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
        _stageActorId = NetFigures.StableActorId(actor);
        for (int i = 0; i < _count; i++)
        {
            GameObject go = Object.Instantiate(prefab.gameObject, _stage, false);
            _stageSlots[i] = Capture(go.transform);
            _stageIds[i] = SlotId(owner, i);
            _stageModels[i] = Model(actor, owner, i);
            _stageConsumeCounts[i] = Descriptor(owner, i)?.ConsumeIcons.Length ?? 0;
            _stageOptionCounts[i] = Descriptor(owner, i)?.OptionStates.Length ?? 0;
            if (_stageSlots[i].Icon != null)
                _stageSlots[i].Icon!.sprite = UseBarSlotSymbol.ResolveIcon(_bar, actor, _stageIds[i], out _);
            FinishNativeShowPose(go.transform);
            // Clear template controls, then materialize the OWNER's original native subwidgets.
            // Only clone-owned components receive these writes, while the hierarchy is inactive.
            foreach (UIUseOption option in go.GetComponentsInChildren<UIUseOption>(true))
                option.gameObject.SetActive(false);
            foreach (UIElementPicker picker in go.GetComponentsInChildren<UIElementPicker>(true))
                picker.content.SetActive(false);
            foreach (UIOptionPicker picker in go.GetComponentsInChildren<UIOptionPicker>(true))
                picker.content.SetActive(false);
            if (_bar == 0 && go.GetComponent<UIUseActiveBonus>() is UIUseActiveBonus bonus)
                _stageSlots[i].Subwidgets = RemoteUseBarSubwidgets.Capture(bonus, Model(actor, owner, i),
                    actor, Descriptor(owner, i), true);
            RemoteWidgetMirror.Neutralize(go, RemoteWidgetMirror.LayoutOwner.CloneAtBoardOwnersWidth, null);
            go.SetActive(true);
        }
        stage.SetActive(true);
        _stageHost.SetActive(true); // only inert presentation/layout components survive
        LayoutRebuilder.ForceRebuildLayoutImmediate(_stage);
    }

    private UseBarWidgetState? Descriptor(RemoteAvatar owner, int slot)
    {
        if (_bar != 0 || owner.UseBarWidgetStates == null) return null;
        foreach (UseBarWidgetState state in owner.UseBarWidgetStates)
            if (state.Slot == slot) return state;
        return null;
    }

    private CActiveBonus? Model(CPlayerActor? actor, RemoteAvatar owner, int slot)
    {
        int at = _bar * NetProtocol.UseBarsMaxSlots + slot;
        if (_bar != 0 || actor == null || owner.UseBarSlotIds == null || at >= owner.UseBarSlotIds.Length)
            return null;
        return UseBarSlotSymbol.ResolveBonusModel(actor, owner.UseBarSlotIds[at], out _);
    }

    private ushort SlotId(RemoteAvatar owner, int slot)
    {
        int at = _bar * NetProtocol.UseBarsMaxSlots + slot;
        return owner.UseBarSlotIds != null && at < owner.UseBarSlotIds.Length ? owner.UseBarSlotIds[at] : (ushort)0;
    }

    private bool StageMatches(CPlayerActor? actor, RemoteAvatar owner)
    {
        if (_stageActorId != NetFigures.StableActorId(actor)) return false;
        for (int i = 0; i < _count; i++)
        {
            UseBarWidgetState? state = Descriptor(owner, i);
            if (_stageIds[i] != SlotId(owner, i) || !ReferenceEquals(_stageModels[i], Model(actor, owner, i))
                || _stageConsumeCounts[i] != (state?.ConsumeIcons.Length ?? 0)
                || _stageOptionCounts[i] != (state?.OptionStates.Length ?? 0))
                return false;
        }
        return true;
    }

    private byte State(RemoteAvatar owner, int slot)
    {
        int at = _bar * NetProtocol.UseBarsMaxSlots + slot;
        return owner.UseBarSlotStates != null && at < owner.UseBarSlotStates.Length
            ? owner.UseBarSlotStates[at] : NetProtocol.UseSlotOfferedBit;
    }

    private static void PaintMasks(Slot slot, byte state, UseBarWidgetState? descriptor)
    {
        bool offered = (state & NetProtocol.UseSlotOfferedBit) != 0;
        bool chosen = (state & NetProtocol.UseSlotChosenBit) != 0;
        SetActive(slot.Selected, chosen);
        SetActive(slot.Optional, chosen && offered);
        SetActive(slot.Mandatory, (state & NetProtocol.UseSlotMandatoryBit) != 0);
        if (slot.Group != null)
        {
            float alpha = descriptor != null ? descriptor.SlotAlpha / 255f
                : (state & NetProtocol.UseSlotDimmedBit) != 0 ? slot.DisabledAlpha : 1f;
            if (slot.Group.alpha != alpha) slot.Group.alpha = alpha;
        }
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
        ApplyAnimations(owner); // mirror sync and hover must not erase an intermediate pose
    }

    private void ApplyAnimations(RemoteAvatar owner)
    {
        if (_bar != 0) return;
        for (int i = 0; i < _slots.Length; i++)
            _slots[i].Animation?.Apply(owner, _slots[i].ActorId, _slots[i].Identity, i);
    }

    private void Paint(RemoteAvatar owner)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            Slot slot = _slots[i];
            byte state = State(owner, i);
            bool offered = (state & NetProtocol.UseSlotOfferedBit) != 0;
            bool hovered = (state & NetProtocol.UseSlotHoveredBit) != 0;
            bool pressed = (state & NetProtocol.UseSlotPressedBit) != 0;
            PaintMasks(slot, state, Descriptor(owner, i));
            if (slot.Background != null)
            {
                // UIUseSlot never disables its ExtendedButton: the CanvasGroup carries disabled
                // alpha. Re-applying ColorBlock.disabledColor would dim this original widget twice.
                Color tint = (offered && pressed ? slot.Colors.pressedColor
                    : offered && hovered ? slot.Colors.highlightedColor : slot.Colors.normalColor)
                    * slot.Colors.colorMultiplier;
                if (slot.Background.canvasRenderer.GetColor() != tint)
                    slot.Background.canvasRenderer.SetColor(tint);
            }
            PaintHover(ref slot, offered, hovered, pressed);
            _slots[i] = slot;
            ApplyIcon(slot, _wantedIcons[i]);
            UseBarWidgetState? descriptor = Descriptor(owner, i);
            if (descriptor != null)
            {
                slot.Subwidgets?.Paint(descriptor);
                slot.Subwidgets?.TickHover();
                continue;
            }
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

    private static void PaintHover(ref Slot slot, bool offered, bool hovered, bool pressed)
    {
        if (slot.ScaleNode == null || !(slot.HoverFactor > 0f)) return;
        float target = !offered ? 1f : pressed ? (slot.HoverFactor + 1f) * 0.5f : hovered ? slot.HoverFactor : 1f;
        if (slot.ScaleTarget != target)
        {
            slot.ScaleFrom = slot.ScaleCurrent;
            slot.ScaleTarget = target;
            slot.ScaleAt = Time.unscaledTime;
        }
        float t = slot.HoverSeconds > 0f ? Mathf.Clamp01((Time.unscaledTime - slot.ScaleAt) / slot.HoverSeconds) : 1f;
        // The original ExtendedButton uses easeOutExpo for its pointer scale animation.
        float ease = t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
        slot.ScaleCurrent = Mathf.LerpUnclamped(slot.ScaleFrom, target, ease);
        Vector3 scale = slot.ScaleNode.localScale;
        Vector3 want = new(slot.ScaleCurrent, slot.ScaleCurrent, scale.z);
        if (scale != want) slot.ScaleNode.localScale = want;
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
        ExtendedButton? button = Field<ExtendedButton>(component, "button");
        return new Slot
        {
            Animation = component is UIUseActiveBonus bonus ? RemoteUseBarAnimation.Capture(bonus) : null,
            ScaleNode = button != null ? (button.overridedTargetRectScale != null ? button.overridedTargetRectScale
                : button.targetRect != null ? button.targetRect : button.transform) : null,
            HoverFactor = button != null ? button.highlightScaleFactor : 1f,
            HoverSeconds = button != null && button.animateScaling ? button.animationDuration : 0f,
            ScaleFrom = 1f, ScaleCurrent = 1f, ScaleTarget = 1f,
            Icon = Field<Image>(component, component is UIUseItemScenario ? "imageItem" : "icon"),
            Selected = Field<GameObject>(component, "selectedMask"),
            Optional = Field<GameObject>(component, "optionalHiglight"),
            Mandatory = Field<GameObject>(component, "mandatoryHiglight"),
            Group = Field<CanvasGroup>(component, "canvasGroup"),
            DisabledAlpha = (float)(AccessTools.Field(component.GetType(), "disabledAlpha")?.GetValue(component) ?? 0.25f),
            Background = button != null ? button.targetGraphic : null,
            Colors = button != null ? button.colors : ColorBlock.defaultColorBlock,
            ElementPicker = Field<UIElementPicker>(component, "elementPicker")?.content,
            OptionPicker = Field<UIOptionPicker>(component, "optionPicker")?.content,
        };
    }

    private struct Slot
    {
        internal RemoteUseBarSubwidgets? Subwidgets;
        internal RemoteUseBarAnimation? Animation;
        internal int ActorId;
        internal ushort Identity;
        internal Image? Icon;
        internal GameObject? Selected, Optional, Mandatory, ElementPicker, OptionPicker;
        internal CanvasGroup? Group;
        internal Graphic? Background;
        internal ColorBlock Colors;
        internal float DisabledAlpha;
        internal Transform? ScaleNode;
        internal float HoverFactor, HoverSeconds, ScaleFrom, ScaleCurrent, ScaleTarget, ScaleAt;
        internal Slot Map(RemoteWidgetMirror mirror) => new()
        {
            Subwidgets = Subwidgets?.Map(mirror),
            Animation = Animation?.Map(mirror), ActorId = ActorId, Identity = Identity,
            ScaleNode = mirror.CloneOf(ScaleNode), HoverFactor = HoverFactor, HoverSeconds = HoverSeconds,
            ScaleFrom = 1f, ScaleCurrent = 1f, ScaleTarget = 1f,
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

    private void ReleaseSlots()
    {
        foreach (Slot slot in _slots) slot.Animation?.Destroy();
        _slots = Array.Empty<Slot>();
    }

    internal void Destroy()
    {
        ReleaseSlots();
        _mirror.Destroy();
        if (_stageHost != null)
            Object.Destroy(_stageHost);
    }
}
