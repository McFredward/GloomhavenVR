using System;
using System.Collections.Generic;
using System.Globalization;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Late read of the owner's original auxiliary slots. Controllers and gameplay models
/// are never changed. Immutable snapshots are reused while their actual rendered values hold.</summary>
internal static class NativeUseBarSampler
{
    private sealed class Cache
    {
        internal Component? Source;
        internal object? Model;
        internal NativeUseBarState Scratch = new();
        internal NativeUseBarState? Published;
        internal string[] Selected = Array.Empty<string>();
        internal NativeUseBarAnimationBinding[] Bindings = Array.Empty<NativeUseBarAnimationBinding>();
        internal string? Refusal;
    }
    private static readonly Cache?[] Slots = new Cache?[32];
    internal static void Reset() => Array.Clear(Slots, 0, Slots.Length);
    internal static NativeUseBarState? Sample(Component source, byte bar, byte slot)
    {
        if (source == null || bar < 1 || bar > 3 || slot >= 8) return null;
        Cache cache = Slots[bar * 8 + slot] ??= new Cache();
        try
        {
            object? model = NativeUseBarModels.Field<object>(source, "element");
            if (!ReferenceEquals(cache.Source, source) || !ReferenceEquals(cache.Model, model))
            {
                var state = new NativeUseBarState { Bar = bar, Slot = slot };
                object nativeModel = NativeUseBarModels.Describe(source, state);
                List<IOption> options = NativeUseBarModels.Options(nativeModel, state,
                    NativeUseBarModels.Field<CActor>(source, "actor") as CPlayerActor);
                var selected = new string[options.Count];
                for (int i = 0; i < options.Count; i++) selected[i] = NativeUseBarModels.SelectedText(options[i], nativeModel, i);
                var bindings = new List<NativeUseBarAnimationBinding>();
                var values = new List<NativeUseBarAnimationEntry>();
                AddAnimation(source, NativeUseBarModels.Field<GUIAnimator>(source, "showAnimation"), 0, bindings, values);
                if (source is UIUseAugmentation augment)
                    for (int i = 0; i < augment.augmentations.Count; i++)
                        AddAnimation(source, augment.augmentations[i].showAnimator, checked((byte)(i + 1)), bindings, values);
                if (values.Count > 16) throw new InvalidOperationException("native slot exceeds 16 authored animation settings");
                state.Animations = values.ToArray();
                cache.Source = source; cache.Model = model; cache.Scratch = state;
                cache.Selected = selected; cache.Bindings = bindings.ToArray(); cache.Published = null;
            }
            NativeUseBarState s = cache.Scratch;
            UIElementPicker? element = NativeUseBarModels.Field<UIElementPicker>(source, "elementPicker");
            UIOptionPicker? picker = NativeUseBarModels.Field<UIOptionPicker>(source, "optionPicker");
            UseBarWidgetState w = s.WidgetState; w.Slot = slot;
            CanvasGroup? group = NativeUseBarModels.Field<CanvasGroup>(source, "canvasGroup");
            w.SlotAlpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(group != null ? group.alpha : 1f) * 255f);
            w.Flags = (byte)((element != null && element.IsOpen ? 1 : 0) | (picker != null && picker.IsOpen ? 2 : 0));
            Icons(NativeUseBarModels.Field<List<UIUseOption>>(source, "consumeElements"), ref w.ConsumeIcons);
            Icons(NativeUseBarModels.Field<List<UIUseOption>>(source, "infuseElements"), ref s.InfuseIcons);
            List<UIUseOption>? inline = NativeUseBarModels.Field<List<UIUseOption>>(source, "optionsUI");
            int count = 0;
            if (inline != null) foreach (UIUseOption ui in inline) if (ui != null && ui.gameObject.activeSelf) count++;
            Size(ref w.InlineSlots, count); Size(ref w.InlineOptions, count); Size(ref w.InlineNumbers, count);
            int at = 0;
            if (inline != null) for (int i = 0; i < inline.Count; i++)
            {
                UIUseOption ui = inline[i]; if (ui == null || !ui.gameObject.activeSelf) continue;
                w.InlineSlots[at] = checked((byte)i); w.InlineNumbers[at] = 0;
                w.InlineOptions[at] = TextOption(ui, cache.Selected, out short number); w.InlineNumbers[at++] = number;
            }
            Array.Clear(w.ElementStates, 0, 6);
            if (element != null) foreach (UIElementPickerSlot button in element.elementButtons)
                if (button != null && (int)button.element >= 0 && (int)button.element < 6)
                    w.ElementStates[(int)button.element] = ButtonState(button.button, button.IsSelected, button.gameObject.activeSelf);
            Size(ref w.OptionStates, picker != null ? picker.optionButtons.Count : 0);
            if (picker != null) for (int i = 0; i < w.OptionStates.Length; i++)
            { UIPickerSlot button = picker.optionButtons[i]; w.OptionStates[i] = button != null
                ? ButtonState(button.Selectable, button.IsSelected, button.gameObject.activeSelf) : (byte)0; }
            UIUsePreview? preview = NativeUseBarModels.Field<UIUsePreview>(source, "previewEffect");
            s.PreviewVisible = preview != null && preview.gameObject.activeSelf;
            if (source is UIUseAugmentation augmentation)
            {
                if (s.Augments.Length != augmentation.augmentations.Count)
                {
                    if (augmentation.augmentations.Count > 32) throw new InvalidOperationException("native augment count exceeds 32");
                    s.Augments = new NativeUseBarAugment[augmentation.augmentations.Count];
                    for (int i = 0; i < s.Augments.Length; i++) s.Augments[i] = new NativeUseBarAugment();
                }
                s.PreviewOption = 0;
                for (int i = 0; i < s.Augments.Length; i++)
                {
                    UIUseAugmentationElement ui = augmentation.augmentations[i]; NativeUseBarAugment a = s.Augments[i];
                    a.IconSymbol = AugmentIcon(ui.icon, NativeUseBarModels.Field<CActor>(source, "actor")!); a.ConsumeSymbol = ElementIcon(ui.consumeElement.icon, false);
                    a.Option = TextOption(ui.consumeElement, cache.Selected, out _);
                    if (a.Option == 255) throw new InvalidOperationException("native augmentation option unexpectedly numeric");
                    a.Flags = (byte)((ui.gameObject.activeSelf ? 1 : 0) | (ui.selectedMask.enabled ? 2 : 0)
                        | (ui.icon.color == UIInfoTools.Instance.White ? 4 : 0) | (ui.consumeElement.gameObject.activeSelf ? 8 : 0)
                        | (ui.consumeElement.transform.parent == ui.transform.parent ? 16 : 0));
                    Vector3 position = ((RectTransform)ui.consumeElement.transform).anchoredPosition3D;
                    a.ConsumePosition[0] = position.x; a.ConsumePosition[1] = position.y; a.ConsumePosition[2] = position.z;
                    if (a.Option != 0) s.PreviewOption = a.Option;
                }
                List<Image> images = augmentation.highlight.images;
                if (images.Count > 32) throw new InvalidOperationException("native highlight image count exceeds 32");
                if (s.HighlightColors.Length != images.Count * 4) s.HighlightColors = new float[images.Count * 4];
                for (int i = 0; i < images.Count; i++)
                { Color color = images[i].color; int j = i * 4; s.HighlightColors[j] = color.r; s.HighlightColors[j+1] = color.g;
                    s.HighlightColors[j+2] = color.b; s.HighlightColors[j+3] = color.a; }
            }
            for (int i = 0; i < cache.Bindings.Length; i++) cache.Bindings[i].Read(s.Animations[i].Value.Values);
            if (!s.Validate()) throw new InvalidOperationException("native slot exceeds bounded visual descriptor");
            cache.Refusal = null;
            if (!NativeUseBarState.SameState(cache.Published, s)) cache.Published = s.Snapshot();
            return cache.Published;
        }
        catch (Exception e)
        {
            cache.Source = null; // failed capture must never become a successful cache hit
            if (cache.Refusal != e.Message)
            { cache.Refusal = e.Message; VRLog.Warn("Net", $"NATIVE USE BAR SAMPLE: {bar}/{slot} refused: {e.Message}; no replica substituted."); }
            return null;
        }
    }
    private static void AddAnimation(Component root, GUIAnimator? animator, byte index,
        List<NativeUseBarAnimationBinding> bindings, List<NativeUseBarAnimationEntry> entries)
    {
        foreach (NativeUseBarAnimationBinding binding in NativeUseBarAnimationBinding.Capture(root, animator))
        { bindings.Add(binding); entries.Add(new NativeUseBarAnimationEntry { AnimatorIndex = index,
            Value = new UseBarAnimationValue { SettingIndex = binding.SettingIndex, Kind = binding.Kind, Values = new float[binding.Components] } }); }
    }
    private static void Size<T>(ref T[] values, int count)
    { if (count > 32) throw new InvalidOperationException("native subwidget count exceeds 32"); if (values.Length != count) values = new T[count]; }
    private static void Icons(List<UIUseOption>? options, ref byte[] values)
    {
        int count = 0; if (options != null) foreach (UIUseOption ui in options) if (ui != null && ui.gameObject.activeSelf) count++;
        Size(ref values, count); int at = 0;
        if (options != null) foreach (UIUseOption ui in options) if (ui != null && ui.gameObject.activeSelf) values[at++] = ElementIcon(ui.icon, false);
    }
    internal static byte ElementIcon(Image? image, bool allowActor)
    {
        if (image == null || !image.enabled || !image.gameObject.activeSelf || image.sprite == null) return 0;
        for (int i = 0; i < 7; i++)
            if (ReferenceEquals(image.sprite, UIInfoTools.Instance.GetElementPickerSprite((ElementInfusionBoardManager.EElement)i))) return (byte)(i + 1);
        if (allowActor) return 8;
        throw new InvalidOperationException("native element icon does not match original seven symbols");
    }
    private static byte AugmentIcon(Image image, CActor actor)
    {
        if (image == null || !image.enabled || !image.gameObject.activeSelf || image.sprite == null) return 0;
        for (int i = 0; i < 7; i++)
            if (ReferenceEquals(image.sprite, UIInfoTools.Instance.GetElementUseSprite((ElementInfusionBoardManager.EElement)i))) return (byte)(i + 1);
        if (ReferenceEquals(image.sprite, UIInfoTools.Instance.GetCharacterActiveAbilityIcon(actor.Class.ID))) return 8;
        throw new InvalidOperationException("native augmentation icon is not its original element or actor symbol");
    }
    private static byte TextOption(UIUseOption ui, string[] candidates, out short number)
    {
        number = 0;
        if (ui.text == null || !ui.text.gameObject.activeSelf || string.IsNullOrEmpty(ui.text.text)) return 0;
        for (int i = 0; i < candidates.Length; i++) if (candidates[i] == ui.text.text) return checked((byte)(i + 1));
        if (short.TryParse(ui.text.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return 255;
        throw new InvalidOperationException("native option wording has no public model ordinal");
    }
    private static byte ButtonState(Selectable? button, bool selected, bool visible)
    {
        if (!visible) return 0;
        byte value = UseBarWidgetState.VisibleBit;
        if (selected) value |= NetProtocol.UseSlotChosenBit;
        if (button != null && button.IsInteractable()) value |= (byte)(NetProtocol.UseSlotOfferedBit
            | WorldUI.Surfaces.DecisionDockSurface.SamplePointerBits(button));
        return value;
    }
}
