using System;
using System.Collections.Generic;
using System.Globalization;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Read original active-bonus subwidgets; never invokes a slot or picker controller.</summary>
internal static class UseBarWidgetSampler
{
    private sealed class Cache
    {
        internal UIUseActiveBonus? Source;
        internal CActiveBonus? Model;
        internal string[] SelectedText = Array.Empty<string>();
        internal readonly UseBarWidgetState Scratch = new();
        internal UseBarWidgetState? Published;
        internal string? Refusal;
    }
    private static readonly Cache?[] Slots = new Cache?[NetProtocol.UseBarsMaxSlots];
    internal static void Reset() => Array.Clear(Slots, 0, Slots.Length);

    private static void Size<T>(ref T[] buffer, int count)
    {
        if (count > UseBarWidgetState.CountMax)
            throw new InvalidOperationException("native subwidget count exceeds record 47 bounds; no truncation permitted");
        if (buffer.Length != count) buffer = new T[count];
    }

    internal static UseBarWidgetState? Sample(UIUseActiveBonus slot, byte index)
    {
        if (index >= Slots.Length) return null;
        Cache cache = Slots[index] ??= new Cache();
        try
        {
            CActiveBonus? model = null;
            foreach (KeyValuePair<CActiveBonus, UIUseActiveBonus> item in Singleton<UIActiveBonusBar>.Instance.activeBonusSlots)
                if (ReferenceEquals(item.Value, slot))
                    model = item.Key;
            if (model == null)
                return null;
            if (!ReferenceEquals(cache.Source, slot) || !ReferenceEquals(cache.Model, model))
            {
                cache.Source = slot; cache.Model = model; cache.Published = null;
                List<IOption> options = Options(model, slot.actor, slot.initiativePickerIcon);
                cache.SelectedText = new string[options.Count];
                for (int o = 0; o < options.Count; o++) cache.SelectedText[o] = options[o].GetSelectedText();
            }
            UseBarWidgetState state = cache.Scratch;
            state.Slot = index; state.Flags = 0;
            state.SlotAlpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(slot.canvasGroup != null ? slot.canvasGroup.alpha : 1f) * 255f);
            Array.Clear(state.ElementStates, 0, state.ElementStates.Length);
            UIElementPicker? element = slot.elementPicker;
            UIOptionPicker? picker = slot.optionPicker;
            if (element != null && element.IsOpen)
                state.Flags |= 1;
            if (picker != null && picker.IsOpen)
                state.Flags |= 2;
            int consumeCount = 0;
            foreach (UIUseOption consume in slot.consumeElements)
                if (consume != null && consume.gameObject.activeSelf) consumeCount++;
            Size(ref state.ConsumeIcons, consumeCount);
            int consumeAt = 0;
            foreach (UIUseOption consume in slot.consumeElements)
                if (consume != null && consume.gameObject.activeSelf)
                    state.ConsumeIcons[consumeAt++] = ElementIcon(consume.icon);
            int inlineCount = 0;
            foreach (UIUseOption ui in slot.optionsUI)
                if (ui != null && ui.gameObject.activeSelf) inlineCount++;
            Size(ref state.InlineSlots, inlineCount);
            Size(ref state.InlineOptions, inlineCount);
            Size(ref state.InlineNumbers, inlineCount);
            int inlineAt = 0;
            for (int i = 0; i < slot.optionsUI.Count; i++)
            {
                UIUseOption ui = slot.optionsUI[i];
                if (ui == null || !ui.gameObject.activeSelf)
                    continue;
                if (i >= UseBarWidgetState.CountMax)
                    throw new InvalidOperationException("native inline slot index exceeds record 47 bounds");
                state.InlineSlots[inlineAt] = (byte)i;
                byte selected = 0;
                short number = 0;
                if (ui.text != null && ui.text.gameObject.activeSelf && !string.IsNullOrEmpty(ui.text.text))
                {
                    if (short.TryParse(ui.text.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                        selected = UseBarWidgetState.NumericOption;
                    else
                    {
                        for (int o = 0; o < cache.SelectedText.Length; o++)
                            if (cache.SelectedText[o] == ui.text.text)
                                selected = checked((byte)(o + 1));
                        if (selected == 0)
                            throw new InvalidOperationException("native inline wording does not match a public bonus option");
                    }
                }
                state.InlineOptions[inlineAt] = selected;
                state.InlineNumbers[inlineAt++] = number;
            }
            if (element != null)
                foreach (UIElementPickerSlot button in element.elementButtons)
                    if (button != null && (int)button.element >= 0 && (int)button.element < 6)
                        state.ElementStates[(int)button.element] = ButtonState(button.button,
                            button.IsSelected, button.gameObject.activeSelf);
            if (picker != null)
            {
                Size(ref state.OptionStates, picker.optionButtons.Count);
                for (int i = 0; i < state.OptionStates.Length; i++)
                {
                    UIPickerSlot button = picker.optionButtons[i];
                    state.OptionStates[i] = button != null ? ButtonState(button.Selectable, button.IsSelected,
                            button.gameObject.activeSelf) : (byte)0;
                }
            }
            else Size(ref state.OptionStates, 0);
            if (!state.Validate())
                throw new InvalidOperationException("native subwidget count exceeds record 47 bounds; no truncation permitted");
            cache.Refusal = null;
            if (!UseBarWidgetState.SameState(cache.Published, state)) cache.Published = state.Snapshot();
            return cache.Published;
        }
        catch (Exception e)
        {
            if (cache.Refusal != e.Message)
            {
                cache.Refusal = e.Message;
                VRLog.Warn("Net", $"USE BAR WIDGET SAMPLE: slot {index} refused ({e.Message}); no card text or identity substituted.");
            }
            return null;
        }
    }

    private static byte ElementIcon(Image? image)
    {
        if (image == null || !image.enabled || !image.gameObject.activeSelf || image.sprite == null)
            return 0;
        for (int i = 0; i < 7; i++)
            if (ReferenceEquals(image.sprite, UIInfoTools.Instance.GetElementPickerSprite((ElementInfusionBoardManager.EElement)i)))
                return (byte)(i + 1);
        throw new InvalidOperationException("consume icon is not one of the game's seven element symbols");
    }

    private static byte ButtonState(Selectable? button, bool chosen, bool visible)
    {
        if (!visible)
            return 0;
        byte state = UseBarWidgetState.VisibleBit;
        bool offered = button != null && button.IsInteractable();
        if (offered)
            state |= NetProtocol.UseSlotOfferedBit;
        if (chosen)
            state |= NetProtocol.UseSlotChosenBit;
        if (button != null && offered)
            state |= WorldUI.Surfaces.DecisionDockSurface.SamplePointerBits(button);
        return state;
    }

    /// <summary>The game's original public option objects, constructed without a controller or
    /// callback. This is UIUseActiveBonus.SetActiveBonus's candidate selection, without Init,
    /// CreateOptions or ForgoActiveBonus's simulation-changing constructor.</summary>
    internal static List<IOption> Options(CActiveBonus bonus, CActor actor, Sprite initiativeIcon)
    {
        var result = new List<IOption>();
        if (bonus is CAdjustInitiativeActiveBonus adjust && bonus.BespokeBehaviour is CAdjustInitiativeActiveBonus_AdjustInitiative)
        {
            result.Add(new InitiativeOption(-adjust.Ability.Strength,
                Math.Max(1, actor.Initiative() - adjust.Ability.Strength), initiativeIcon));
            result.Add(new InitiativeOption(adjust.Ability.Strength,
                actor.Initiative() + adjust.Ability.Strength, initiativeIcon));
        }
        else if (bonus is CForgoActionsForCompanionActiveBonus forgo)
        {
            result.Add(new AbilityOption(forgo.ForgoActionsForCompanionAbility.ForgoTopActionAbility));
            result.Add(new AbilityOption(forgo.ForgoActionsForCompanionAbility.ForgoBottomActionAbility));
        }
        else if (bonus is CChooseAbilityActiveBonus choose)
            foreach (CAbility ability in choose.ChooseAbility.ChooseAbilities)
                result.Add(new AbilityOption(ability));
        return result;
    }
}
