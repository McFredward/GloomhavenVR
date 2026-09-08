using System;
using System.Collections.Generic;
using System.Globalization;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net;

/// <summary>Original consume controls and element/option picker widgets, driven by record 47.</summary>
internal sealed class RemoteUseBarSubwidgets
{
    private readonly List<Inline> _consumes = new();
    private readonly List<Inline> _inline = new();
    private readonly List<Picker> _elements = new();
    private readonly List<Picker> _options = new();
    private GameObject? _elementContent, _optionContent;
    private Image? _unfocus;
    private Color _unfocused;
    private List<IOption> _candidates = new();
    private UseBarWidgetState? _painted;
    private bool _layoutSource;

    internal static RemoteUseBarSubwidgets Capture(UIUseActiveBonus source, CActiveBonus? model,
        CActor? actor, UseBarWidgetState? state, bool initialize)
    {
        var result = new RemoteUseBarSubwidgets { _layoutSource = initialize };
        if (initialize && state != null)
        {
            Normalize(source.consumeElements, state.ConsumeIcons.Length, source.consumeElements.Count > 0
                ? source.consumeElements[0] : null);
            UIOptionPicker picker = source.optionPicker;
            if (picker != null)
                Normalize(picker.optionButtons, state.OptionStates.Length, picker.optionPrefab);
        }
        if (model != null && actor != null)
            result._candidates = UseBarWidgetSampler.Options(model, actor, source.initiativePickerIcon);
        if (initialize && model != null && source.previewEffect != null)
        {
            // UIUseActiveBonus.Decorate's pure visual half. UIUsePreview writes only its own
            // original image/text controls; no tooltip, input handler or bonus callback runs.
            if (model.BaseCard is CItem item) source.previewEffect.SetDescription(item);
            else source.previewEffect.SetDescription(model.Ability);
        }
        foreach (UIUseOption ui in source.consumeElements)
            result._consumes.Add(new Inline(ui));
        foreach (UIUseOption ui in source.optionsUI)
            result._inline.Add(new Inline(ui));
        if (source.elementPicker != null)
        {
            result._elementContent = source.elementPicker.content;
            foreach (UIElementPickerSlot button in source.elementPicker.elementButtons)
            {
                if (initialize && button != null)
                {
                    // UIElementPickerSlot.Init's visual half. The original method also registers
                    // audio/input elsewhere; only these clone-owned Graphics are allowed here.
                    button.icon.sprite = UIInfoTools.Instance.GetElementPickerSprite(button.element);
                    button.highlightElement.color = UIInfoTools.Instance.GetElementHighlightColor(
                        button.element, button.highlightElement.color.a);
                }
                if (button != null) result._elements.Add(new Picker(button));
            }
        }
        if (source.optionPicker != null)
        {
            result._optionContent = source.optionPicker.content;
            for (int i = 0; i < source.optionPicker.optionButtons.Count; i++)
            {
                UIPickerSlot button = source.optionPicker.optionButtons[i];
                if (initialize && button != null && i < result._candidates.Count)
                {
                    string text = result._candidates[i].GetPickerText();
                    button.text.text = text;
                    button.text.enabled = !string.IsNullOrEmpty(text);
                    if (button.image != null)
                    {
                        button.image.sprite = result._candidates[i].GetPickerIcon();
                        button.image.enabled = button.image.sprite != null;
                    }
                }
                if (button != null) result._options.Add(new Picker(button));
            }
        }
        result._unfocus = source.unfocusImage;
        result._unfocused = source.unfocusedColor;
        if (initialize && state != null)
            result.Paint(state);
        return result;
    }

    // These lists belong to an inactive MOD CLONE. Never call HelperTools.NormalizePool on the
    // game's live lists: that method activates scripts as it instantiates and owns gameplay UI.
    private static void Normalize<T>(List<T> list, int count, T? template) where T : Component
    {
        if (count > list.Count && template == null)
            throw new InvalidOperationException("native subwidget template is missing");
        Transform? parent = list.Count > 0 ? list[0].transform.parent : template?.transform.parent;
        while (list.Count < count)
        {
            GameObject clone = Object.Instantiate(template!.gameObject, parent, false);
            list.Add(clone.GetComponent<T>());
        }
        for (int i = 0; i < list.Count; i++)
            list[i].gameObject.SetActive(i < count);
    }

    internal RemoteUseBarSubwidgets Map(RemoteWidgetMirror mirror)
    {
        var result = new RemoteUseBarSubwidgets
        {
            _elementContent = Node(mirror, _elementContent),
            _optionContent = Node(mirror, _optionContent),
            _unfocus = Component(mirror, _unfocus),
            _unfocused = _unfocused,
            _candidates = _candidates,
        };
        foreach (Inline ui in _consumes) result._consumes.Add(ui.Map(mirror));
        foreach (Inline ui in _inline) result._inline.Add(ui.Map(mirror));
        foreach (Picker ui in _elements) result._elements.Add(ui.Map(mirror));
        foreach (Picker ui in _options) result._options.Add(ui.Map(mirror));
        return result;
    }

    internal void Paint(UseBarWidgetState state)
    {
        // The source stage and the visible clone both receive immutable owner snapshots. A steady
        // picture needs no new numeric/rich text strings and no repeated canvas dirties.
        if (ReferenceEquals(_painted, state)) return;
        _painted = state;
        Active(_elementContent, (state.Flags & 1) != 0);
        Active(_optionContent, (state.Flags & 2) != 0);
        if (_unfocus != null)
        {
            Color color = state.Flags != 0 ? _unfocused : UIInfoTools.Instance.White;
            if (_unfocus.color != color) _unfocus.color = color;
        }
        for (int i = 0; i < _consumes.Count; i++)
        {
            Inline ui = _consumes[i];
            Active(ui.Root, i < state.ConsumeIcons.Length);
            if (i >= state.ConsumeIcons.Length) continue;
            byte value = state.ConsumeIcons[i];
            Active(ui.Text != null ? ui.Text.gameObject : null, false);
            Active(ui.Icon != null ? ui.Icon.gameObject : null, value != 0);
            if (ui.Icon != null && value != 0)
                ui.Icon.sprite = UIInfoTools.Instance.GetElementPickerSprite((ElementInfusionBoardManager.EElement)(value - 1));
        }
        for (int native = 0; native < _inline.Count; native++)
        {
            Inline ui = _inline[native];
            int i = Array.IndexOf(state.InlineSlots, (byte)native);
            Active(ui.Root, i >= 0);
            if (i < 0) continue;
            Active(ui.Icon != null ? ui.Icon.gameObject : null, false);
            byte value = state.InlineOptions[i];
            string text = value == UseBarWidgetState.NumericOption
                ? state.InlineNumbers[i].ToString(CultureInfo.InvariantCulture)
                : value > 0 && value <= _candidates.Count ? _candidates[value - 1].GetSelectedText() : string.Empty;
            Active(ui.Text != null ? ui.Text.gameObject : null, text.Length > 0);
            if (ui.Text != null)
            {
                if (ui.Text.text != text) ui.Text.text = text;
                Color color = value == UseBarWidgetState.NumericOption && _candidates.Count > 0
                    && _candidates[0] is InitiativeOption ? UIInfoTools.Instance.basicTextColor
                    : value > 0 && value <= _candidates.Count && _candidates[value - 1] is InitiativeOption initiative
                        ? initiative.GetSelectedTextColor() : UIInfoTools.Instance.White;
                if (ui.Text.color != color) ui.Text.color = color;
            }
        }
        foreach (Picker ui in _elements)
            ui.Paint(ui.Element >= 0 && ui.Element < 6 ? state.ElementStates[ui.Element] : (byte)0);
        for (int i = 0; i < _options.Count; i++)
            _options[i].Paint(i < state.OptionStates.Length ? state.OptionStates[i] : (byte)0);
    }

    internal void TickHover()
    {
        if (_layoutSource) return; // native fit measures rest geometry, not the pointer grow
        foreach (Picker picker in _elements) picker.TickHover();
        foreach (Picker picker in _options) picker.TickHover();
    }

    private static GameObject? Node(RemoteWidgetMirror m, GameObject? source) =>
        m.CloneOf(source != null ? source.transform : null)?.gameObject;
    private static T? Component<T>(RemoteWidgetMirror m, T? source) where T : Component =>
        m.CloneOf(source != null ? source.transform : null)?.GetComponent<T>();
    private static void Active(GameObject? node, bool on)
    {
        if (node != null && node.activeSelf != on) node.SetActive(on);
    }

    private sealed class Inline
    {
        internal GameObject? Root;
        internal Image? Icon;
        internal TextMeshProUGUI? Text;
        private Inline() { }
        internal Inline(UIUseOption source) { Root = source.gameObject; Icon = source.icon; Text = source.text; }
        internal Inline Map(RemoteWidgetMirror mirror) => new()
        { Root = Node(mirror, Root), Icon = Component(mirror, Icon), Text = Component(mirror, Text) };
    }

    private sealed class Picker
    {
        internal GameObject? Root;
        internal Image? Background, ElementHighlight;
        internal Graphic? ButtonGraphic;
        internal Sprite? LitSprite, UnlitSprite;
        internal ColorBlock Colors;
        internal float LitAlpha, UnlitAlpha;
        internal int Element = -1;
        private Transform? _scaleNode;
        private float _factor = 1f, _seconds, _from = 1f, _now = 1f, _target = 1f, _at;
        private Picker() { }
        internal Picker(UIElementPickerSlot source)
        {
            Root = source.gameObject; Background = source.highlightBackground;
            ElementHighlight = source.highlightElement; Element = (int)source.element;
            LitSprite = source.highlightedBackground; UnlitSprite = source.unhighlightedBackground;
            LitAlpha = source.highlightedBackgroundAlpha; UnlitAlpha = source.unhighlightedBackgroundAlpha;
            ButtonGraphic = source.button.targetGraphic; Colors = source.button.colors;
            BindHover(source.button);
        }
        internal Picker(UIPickerSlot source)
        {
            Root = source.gameObject; Background = source.highlightBackground;
            LitSprite = source.highlightedBackground; UnlitSprite = source.unhighlightedBackground;
            LitAlpha = source.highlightedBackgroundAlpha; UnlitAlpha = source.unhighlightedBackgroundAlpha;
            ButtonGraphic = source.button.targetGraphic; Colors = source.button.colors;
            BindHover(source.button);
        }
        internal Picker Map(RemoteWidgetMirror mirror) => new()
        {
            Root = Node(mirror, Root), Background = Component(mirror, Background),
            ElementHighlight = Component(mirror, ElementHighlight), ButtonGraphic = Component(mirror, ButtonGraphic),
            LitSprite = LitSprite, UnlitSprite = UnlitSprite, LitAlpha = LitAlpha, UnlitAlpha = UnlitAlpha,
            Colors = Colors, Element = Element,
            _scaleNode = mirror.CloneOf(_scaleNode), _factor = _factor, _seconds = _seconds,
        };
        private void BindHover(ExtendedButton button)
        {
            _scaleNode = button.overridedTargetRectScale != null ? button.overridedTargetRectScale
                : button.targetRect != null ? button.targetRect : button.transform;
            _factor = button.highlightScaleFactor > 0f ? button.highlightScaleFactor : 1f;
            _seconds = button.animateScaling ? button.animationDuration : 0f;
        }
        internal void TickHover()
        {
            if (_scaleNode == null) return;
            float t = _seconds > 0f ? Mathf.Clamp01((Time.unscaledTime - _at) / _seconds) : 1f;
            _now = Mathf.LerpUnclamped(_from, _target, t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t));
            Vector3 scale = _scaleNode.localScale;
            Vector3 desired = new(_now, _now, scale.z);
            if (scale != desired) _scaleNode.localScale = desired;
        }
        internal void Paint(byte state)
        {
            Active(Root, (state & UseBarWidgetState.VisibleBit) != 0);
            float target = (state & NetProtocol.UseSlotOfferedBit) == 0 ? 1f
                : (state & NetProtocol.UseSlotPressedBit) != 0 ? (_factor + 1f) * 0.5f
                : (state & NetProtocol.UseSlotHoveredBit) != 0 ? _factor : 1f;
            if (_target != target) { _from = _now; _target = target; _at = Time.unscaledTime; }
            bool lit = (state & (NetProtocol.UseSlotChosenBit | NetProtocol.UseSlotHoveredBit)) != 0;
            if (ElementHighlight != null) ElementHighlight.enabled = lit;
            if (Background != null)
            {
                Background.sprite = lit ? LitSprite : UnlitSprite;
                Color color = Background.color; color.a = lit ? LitAlpha : UnlitAlpha;
                if (Background.color != color) Background.color = color;
            }
            if (ButtonGraphic != null)
            {
                Color tint = (state & NetProtocol.UseSlotOfferedBit) == 0 ? Colors.disabledColor
                    : (state & NetProtocol.UseSlotPressedBit) != 0 ? Colors.pressedColor
                    : (state & NetProtocol.UseSlotHoveredBit) != 0 ? Colors.highlightedColor : Colors.normalColor;
                tint *= Colors.colorMultiplier;
                if (ButtonGraphic.canvasRenderer.GetColor() != tint) ButtonGraphic.canvasRenderer.SetColor(tint);
            }
        }
    }
}
