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
    internal static UseBarWidgetState? Sample(UIUseActiveBonus slot, byte index)
    {
        try
        {
            CActiveBonus? model = null;
            foreach (KeyValuePair<CActiveBonus, UIUseActiveBonus> item in Singleton<UIActiveBonusBar>.Instance.activeBonusSlots)
                if (ReferenceEquals(item.Value, slot))
                    model = item.Key;
            if (model == null)
                return null;
            List<IOption> options = Options(model, slot.actor, slot.initiativePickerIcon);
            var state = new UseBarWidgetState { Slot = index };
            UIElementPicker? element = slot.elementPicker;
            UIOptionPicker? picker = slot.optionPicker;
            if (element != null && element.IsOpen)
                state.Flags |= 1;
            if (picker != null && picker.IsOpen)
                state.Flags |= 2;
            var consumes = new List<byte>();
            foreach (UIUseOption consume in slot.consumeElements)
                if (consume != null && consume.gameObject.activeSelf)
                    consumes.Add(ElementIcon(consume.icon));
            state.ConsumeIcons = consumes.ToArray();
            var inlineSlots = new List<byte>();
            var inlineOptions = new List<byte>();
            var inlineNumbers = new List<short>();
            for (int i = 0; i < slot.optionsUI.Count; i++)
            {
                UIUseOption ui = slot.optionsUI[i];
                if (ui == null || !ui.gameObject.activeSelf)
                    continue;
                if (i >= UseBarWidgetState.CountMax)
                    throw new InvalidOperationException("native inline slot index exceeds record 47 bounds");
                inlineSlots.Add((byte)i);
                byte selected = 0;
                short number = 0;
                if (ui.text != null && ui.text.gameObject.activeSelf && !string.IsNullOrEmpty(ui.text.text))
                {
                    if (short.TryParse(ui.text.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                        selected = UseBarWidgetState.NumericOption;
                    else
                    {
                        for (int o = 0; o < options.Count; o++)
                            if (options[o].GetSelectedText() == ui.text.text)
                                selected = checked((byte)(o + 1));
                        if (selected == 0)
                            throw new InvalidOperationException("native inline wording does not match a public bonus option");
                    }
                }
                inlineOptions.Add(selected);
                inlineNumbers.Add(number);
            }
            state.InlineSlots = inlineSlots.ToArray();
            state.InlineOptions = inlineOptions.ToArray();
            state.InlineNumbers = inlineNumbers.ToArray();
            if (element != null)
                foreach (UIElementPickerSlot button in element.elementButtons)
                    if (button != null && (int)button.element >= 0 && (int)button.element < 6)
                        state.ElementStates[(int)button.element] = ButtonState(button.button,
                            button.IsSelected, button.gameObject.activeSelf);
            if (picker != null)
            {
                state.OptionStates = new byte[picker.optionButtons.Count];
                for (int i = 0; i < state.OptionStates.Length; i++)
                {
                    UIPickerSlot button = picker.optionButtons[i];
                    if (button != null)
                        state.OptionStates[i] = ButtonState(button.Selectable, button.IsSelected,
                            button.gameObject.activeSelf);
                }
            }
            if (!state.Validate())
                throw new InvalidOperationException("native subwidget count exceeds record 47 bounds; no truncation permitted");
            return state;
        }
        catch (Exception e)
        {
            VRLog.Warn("Net", $"USE BAR WIDGET SAMPLE: slot {index} refused ({e.Message}); no card text or identity substituted.");
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
