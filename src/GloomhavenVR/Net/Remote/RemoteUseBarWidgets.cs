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
    private readonly object?[] _stageModels;
    private readonly NativeUseBarState?[] _stageNative;

    internal RemoteUseBarWidgets(Transform mount, int bar, int count)
    {
        _mount = mount;
        _bar = bar;
        _count = count;
        _wantedIcons = new Sprite?[count];
        _stageIds = new ushort[count];
        _stageConsumeCounts = new int[count];
        _stageOptionCounts = new int[count];
        _stageModels = new object?[count];
        _stageNative = new NativeUseBarState?[count];
        // Width-only fit, like UseBarsSurface.StackDocked; open pickers grow downward.
        _mirror = new RemoteWidgetMirror("UseBar" + bar, mount, Cards.PlayTray.DecisionMountWidth,
            float.MaxValue, new Vector2(0f, -1f), densityScale: WorldUI.Surfaces.UseBarsSurface.DensityScale,
            contentOutsideFrame: true, excludedFromFitBranch: IsOriginalTooltipRoot);
    }

    private bool IsOriginalTooltipRoot(Transform source)
    {
        foreach (Slot slot in _stageSlots)
            if (slot.Tooltip?.OwnsRoot(source) == true) return true;
        return false;
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
            // Active-bonus subchoices and tooltips are owner-local. Their original prefab stage
            // also owns the phase cover; the viewer's live tooltip cannot supply that state.
            if (_bar == 0 || _bar > 0 && NativeDescriptor(owner, 0) != null)
                live = false;
            if (!live && _bar > 0)
                for (int i = 0; i < _count; i++)
                    if (NativeDescriptor(owner, i) == null || StageModel(actor, owner, i) == null)
                    { _mirror.SetShown(false); return; } // await the real owner's complete native content
            Transform? source;
            if (live)
                source = container;
            else
            {
                if ((_stage == null || !StageMatches(actor, owner)) && container != null)
                {
                    if (_stageHost != null)
                    {
                        ReleaseStageTooltips();
                        _stageHost.SetActive(false);
                        Object.Destroy(_stageHost);
                    }
                    _stage = null;
                    BuildPrefabStage(container, actor, owner);
                }
                PaintStage(owner);
                if (_stage != null) LayoutRebuilder.ForceRebuildLayoutImmediate(_stage);
                source = _stage;
            }
            bool changed = !ReferenceEquals(_source, source);
            _source = source;
            RestoreAnimationGeometry();
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
                    {
                        original.Subwidgets = RemoteUseBarSubwidgets.Capture(bonus, Model(actor, owner, i),
                            actor, Descriptor(owner, i), false);
                        original.Tooltip = RemoteUseBarTooltip.Capture(bonus, Model(actor, owner, i), false);
                    }
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
            _mirror.TickLive();
            Paint(owner);
            // Refit only resting native geometry; an animated scale/position must not change the
            // host fit and thereby cancel its own movement. Apply the owner pose after this pass.
            RestoreAnimationGeometry();
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
        if (component != null) FinishNativeShowPose(root, Field<GUIAnimator>(component, "showAnimation"));
    }

    internal static void FinishNativeShowPose(Transform root, GUIAnimator? animator)
    {
        if (animator is not LeanTweenGUIAnimator animation) return;
        // Read the original presentation recipe. Never run GUIAnimator events or custom effects,
        // and never allow a serialized target outside this clone to receive a write.
        foreach (LeanTweenGUIAnimationSetting setting in animation.GetSettings())
        {
            RectTransform? target = setting switch
            {
                LeanTweenGuiAnimationSettingScale scale => scale.Target,
                LeanTweenGuiAnimationSettingMove move => move.Target,
                LeanTweenGuiAnimationSettingFade fade => fade.Target,
                CustomLeanTweenGuiAnimationSetting custom when custom.animation is UICampaignRewardRevealAnimator reveal
                    => reveal.revealRect,
                _ => null,
            };
            if (target != null && (ReferenceEquals(target, root) || target.IsChildOf(root)))
            {
                if (setting is CustomLeanTweenGuiAnimationSetting && target.parent is RectTransform parent)
                    target.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, parent.rect.width);
                else if (setting is not CustomLeanTweenGuiAnimationSetting)
                    setting.SetFinalValue();
            }
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
            _stageModels[i] = StageModel(actor, owner, i);
            _stageNative[i] = NativeDescriptor(owner, i);
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
            {
                _stageSlots[i].Subwidgets = RemoteUseBarSubwidgets.Capture(bonus, Model(actor, owner, i),
                    actor, Descriptor(owner, i), true);
                _stageSlots[i].Tooltip = RemoteUseBarTooltip.Capture(bonus, Model(actor, owner, i), true);
            }
            if (_bar > 0 && _stageNative[i] is NativeUseBarState native)
                _stageSlots[i].Native = RemoteNativeUseBar.Capture(SlotComponent(go.transform)!, actor,
                    _stageModels[i], native, true);
            RemoteWidgetMirror.Neutralize(go, RemoteWidgetMirror.LayoutOwner.CloneAtBoardOwnersWidth, null);
            go.SetActive(true);
        }
        stage.SetActive(true);
        _stageHost.SetActive(true); // only inert presentation/layout components survive
        LayoutRebuilder.ForceRebuildLayoutImmediate(_stage);
    }

    private UseBarWidgetState? Descriptor(RemoteAvatar owner, int slot)
    {
        if (_bar != 0) return NativeDescriptor(owner, slot)?.WidgetState;
        if (owner.UseBarWidgetStates == null) return null;
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

    private NativeUseBarState? NativeDescriptor(RemoteAvatar owner, int slot) =>
        _bar > 0 ? owner.NativeUseBarStates[_bar * 8 + slot] : null;

    private object? StageModel(CPlayerActor? actor, RemoteAvatar owner, int slot)
    {
        if (_bar == 0) return Model(actor, owner, slot);
        NativeUseBarState? state = NativeDescriptor(owner, slot);
        if (state != null) return NativeUseBarModels.Resolve(actor, state);
        return _bar == 3 && actor != null ? UseBarSlotSymbol.ResolveItemModel(actor, SlotId(owner, slot), out _) : null;
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
            NativeUseBarState? native = NativeDescriptor(owner, i);
            NativeUseBarState? oldNative = _stageNative[i];
            if (native != null && (oldNative == null || !RemoteNativeUseBar.SameIdentity(oldNative, native)
                || native.InfuseIcons.Length != oldNative.InfuseIcons.Length || native.Augments.Length != oldNative.Augments.Length
                || native.PreviewOption != oldNative.PreviewOption)) return false;
            if (native == null && oldNative != null) return false;
            if (_stageIds[i] != SlotId(owner, i) || !ReferenceEquals(_stageModels[i], StageModel(actor, owner, i))
                || _bar == 0 && _stageSlots[i].Tooltip?.Matches(Model(actor, owner, i)) == false
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
        if (index < _slots.Length && (_bar == 0 || _bar == 3 && _slots[index].Native == null))
            ApplyIcon(_slots[index], sprite);
    }

    private void PaintStage(RemoteAvatar owner)
    {
        if (!ReferenceEquals(_source, _stage) && _source != null) return;
        for (int i = 0; i < _stageSlots.Length; i++)
        {
            UseBarWidgetState? state = Descriptor(owner, i);
            if (_bar == 0 && state != null) _stageSlots[i].Subwidgets?.Paint(state);
            NativeUseBarState? native = NativeDescriptor(owner, i);
            if (native != null) _stageSlots[i].Native?.Paint(native);
            PaintMasks(_stageSlots[i], State(owner, i), state);
            _stageSlots[i].Tooltip?.Paint(State(owner, i), state);
        }
    }

    internal void Tick(RemoteAvatar owner)
    {
        PaintStage(owner);
        _mirror.TickLive();
        Paint(owner);
        ApplyAnimations(owner); // mirror sync and hover must not erase an intermediate pose
    }

    private void RestoreAnimationGeometry()
    {
        foreach (Slot slot in _slots) { slot.Animation?.RestoreGeometry(); slot.Native?.RestoreGeometry(); }
    }

    private void ApplyAnimations(RemoteAvatar owner)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_bar == 0) _slots[i].Animation?.Apply(owner, _slots[i].ActorId, _slots[i].Identity, i);
            else _slots[i].Native?.ApplyAnimation(owner);
        }
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
            slot.Tooltip?.Paint(state, Descriptor(owner, i));
            // UIUseSlot keeps its ExtendedButton enabled; its CanvasGroup carries disabled alpha.
            slot.Button?.Paint(true, hovered, pressed);
            _slots[i] = slot;
            if (_bar == 0 || _bar == 3 && slot.Native == null) ApplyIcon(slot, _wantedIcons[i]);
            NativeUseBarState? native = NativeDescriptor(owner, i);
            if (native != null && slot.Native != null) { slot.Native.Paint(native); continue; }
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

    internal static bool? Interactable(Transform root)
    {
        Component? component = SlotComponent(root);
        return component != null ? AccessTools.Field(component.GetType(), "interactable")?.GetValue(component) as bool? : null;
    }

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
            Button = RemoteNativeButton.Capture(button),
            Icon = Field<Image>(component, component is UIUseItemScenario ? "imageItem" : "icon"),
            Selected = Field<GameObject>(component, "selectedMask"),
            Optional = Field<GameObject>(component, "optionalHiglight"),
            Mandatory = Field<GameObject>(component, "mandatoryHiglight"),
            Group = Field<CanvasGroup>(component, "canvasGroup"),
            DisabledAlpha = (float)(AccessTools.Field(component.GetType(), "disabledAlpha")?.GetValue(component) ?? 0.25f),
            ElementPicker = Field<UIElementPicker>(component, "elementPicker")?.content,
            OptionPicker = Field<UIOptionPicker>(component, "optionPicker")?.content,
        };
    }

    private struct Slot
    {
        internal RemoteUseBarSubwidgets? Subwidgets;
        internal RemoteUseBarTooltip? Tooltip;
        internal RemoteNativeUseBar? Native;
        internal RemoteUseBarAnimation? Animation;
        internal int ActorId;
        internal ushort Identity;
        internal Image? Icon;
        internal GameObject? Selected, Optional, Mandatory, ElementPicker, OptionPicker;
        internal CanvasGroup? Group;
        internal RemoteNativeButton? Button;
        internal float DisabledAlpha;
        internal Slot Map(RemoteWidgetMirror mirror) => new()
        {
            Subwidgets = Subwidgets?.Map(mirror), Native = Native?.Map(mirror),
            Tooltip = Tooltip?.Map(mirror),
            Animation = Animation?.Map(mirror), ActorId = ActorId, Identity = Identity,
            Button = Button?.Map(mirror),
            Icon = mirror.CloneOf(Icon != null ? Icon.transform : null)?.GetComponent<Image>(),
            Selected = mirror.CloneOf(Selected != null ? Selected.transform : null)?.gameObject,
            Optional = mirror.CloneOf(Optional != null ? Optional.transform : null)?.gameObject,
            Mandatory = mirror.CloneOf(Mandatory != null ? Mandatory.transform : null)?.gameObject,
            ElementPicker = mirror.CloneOf(ElementPicker != null ? ElementPicker.transform : null)?.gameObject,
            OptionPicker = mirror.CloneOf(OptionPicker != null ? OptionPicker.transform : null)?.gameObject,
            Group = mirror.CloneOf(Group != null ? Group.transform : null)?.GetComponent<CanvasGroup>(),
            DisabledAlpha = DisabledAlpha,
        };
    }

    private void ReleaseSlots()
    {
        foreach (Slot slot in _slots) { slot.Animation?.Destroy(); slot.Native?.Destroy(); slot.Tooltip?.Destroy(); }
        _slots = Array.Empty<Slot>();
    }

    private void ReleaseStageTooltips()
    { foreach (Slot slot in _stageSlots) slot.Tooltip?.Destroy(); }

    internal void Destroy()
    {
        ReleaseSlots();
        _mirror.Destroy();
        ReleaseStageTooltips();
        if (_stageHost != null)
            Object.Destroy(_stageHost);
    }
}
