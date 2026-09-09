using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ScenarioRuleLibrary;
using GLOOM;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Original auxiliary slot content and visual targets, captured before stripping and
/// mapped to clone-owned components. No game selection, infusion or augmentation callback runs.</summary>
internal sealed class RemoteNativeUseBar
{
    private RemoteUseBarSubwidgets _subwidgets = null!;
    private GameObject? _preview;
    private readonly List<Augment> _augments = new();
    private readonly List<Image> _highlights = new();
    private readonly List<RemoteUseBarAnimation> _animations = new();
    private readonly Dictionary<Graphic, Material> _materials = new();
    private List<IOption> _options = new();
    private NativeUseBarState? _painted;
    private NativeUseBarState _identity = null!;
    private CPlayerActor? _actor;
    private static readonly ConditionalWeakTable<CharacterDecisionPresentation, Playback[]> Playbacks = new();
    private sealed class Playback
    {
        internal NativeUseBarState? Identity;
        internal float Boundary = float.NegativeInfinity;
        internal readonly UseBarAnimationPlaybackClock Clock = new();
    }
    private sealed class Augment
    {
        internal GameObject Root = null!, Consume = null!;
        internal Transform Parent = null!;
        internal Image Icon = null!, Mask = null!, ConsumeIcon = null!;
        internal TMP_Text ConsumeText = null!;
        internal RectTransform ConsumeRect = null!;
        internal Color Unfocused;
        internal Augment(UIUseAugmentationElement source)
        {
            Root = source.gameObject; Parent = source.transform.parent;
            Icon = source.icon; Mask = source.selectedMask; Consume = source.consumeElement.gameObject;
            ConsumeIcon = source.consumeElement.icon; ConsumeText = source.consumeElement.text;
            ConsumeRect = (RectTransform)source.consumeElement.transform; Unfocused = source.unfocusedColor;
        }
        private Augment() { }
        internal Augment Map(RemoteWidgetMirror mirror) => new() {
            Root = mirror.CloneOf(Root.transform)!.gameObject, Parent = mirror.CloneOf(Parent)!,
            Consume = mirror.CloneOf(Consume.transform)!.gameObject,
            Icon = mirror.CloneOf(Icon.transform)!.GetComponent<Image>(), Mask = mirror.CloneOf(Mask.transform)!.GetComponent<Image>(),
            ConsumeIcon = mirror.CloneOf(ConsumeIcon.transform)!.GetComponent<Image>(),
            ConsumeText = mirror.CloneOf(ConsumeText.transform)!.GetComponent<TMP_Text>(),
            ConsumeRect = (RectTransform)mirror.CloneOf(ConsumeRect)!, Unfocused = Unfocused };
    }

    internal static RemoteNativeUseBar Capture(Component source, CPlayerActor? actor,
        object? model, NativeUseBarState state, bool initialize)
    {
        if (model == null || actor == null) throw new InvalidOperationException("original auxiliary slot model is not replicated yet");
        var result = new RemoteNativeUseBar { _identity = state, _actor = actor };
        result._options = NativeUseBarModels.Options(model, state, actor);
        UIUsePreview? preview = NativeUseBarModels.Field<UIUsePreview>(source, "previewEffect");
        result._preview = preview != null ? preview.gameObject : null;
        if (initialize && preview != null)
        {
            if (source is UIUseAbility ability)
            {
                if (model is CAbility effect) preview.SetDescription(new List<CAbility> { effect });
                else if (model is CAction action) preview.SetDescription(action.Abilities);
                else preview.SetDescription(new List<CAbility>());
                ability.icon.sprite = model is CAbilityChooseAbility choose ? new ChooseAbility(actor, choose).Icon
                    : UIInfoTools.Instance.GetElementUseSprite(ElementInfusionBoardManager.EElement.Any);
            }
            else if (source is UIUseItemScenario itemSlot && model is CItem item)
            {
                preview.SetDescription(item); itemSlot.imageItem.sprite = UIInfoTools.Instance.GetItemConfig(item.YMLData.Art).miniIcon;
                itemSlot.optionTick.Clear();
                itemSlot.optionTick.gameObject.SetActive(item.YMLData.Data.Abilities != null
                    && item.YMLData.Data.Abilities.Exists(a => a.AbilityType == CAbility.EAbilityType.Choose));
            }
            else if (source is UIUseAugmentation && model is CActionAugmentation augment)
            {
                if (state.ModelKind == NativeUseBarModelKind.AugmentGroup && state.PreviewOption == 0)
                    preview.SetDescription(LocalizationManager.GetTranslation($"UI_PREVIEW_EFFECT_AUGMENT_GROUP_{augment.ConsumeGroup}"));
                else if (state.ModelKind == NativeUseBarModelKind.AugmentGroup && state.PreviewOption <= result._options.Count
                    && result._options[state.PreviewOption - 1] is AugmentationOption option)
                    preview.SetDescription(option.Augmentation);
                else preview.SetDescription(augment);
            }
        }
        if (source is UIUseAugmentation augmentation)
        {
            if (initialize) RemoteUseBarSubwidgets.Normalize(augmentation.augmentations, state.Augments.Length, augmentation.augmentationPrefab);
            foreach (UIUseAugmentationElement child in augmentation.augmentations)
            {
                if (initialize) RemoteUseBarWidgets.FinishNativeShowPose(source.transform, child.showAnimator);
                result._augments.Add(new Augment(child));
            }
            result._highlights.AddRange(augmentation.highlight.images);
        }
        result._subwidgets = RemoteUseBarSubwidgets.CaptureAux(source, model, actor, state, initialize);
        result._animations.Add(RemoteUseBarAnimation.Capture(source, NativeUseBarModels.Field<GUIAnimator>(source, "showAnimation")));
        if (source is UIUseAugmentation withChildren)
            foreach (UIUseAugmentationElement child in withChildren.augmentations)
                result._animations.Add(RemoteUseBarAnimation.Capture(source, child.showAnimator));
        if (initialize) result.Paint(state);
        return result;
    }

    internal RemoteNativeUseBar Map(RemoteWidgetMirror mirror)
    {
        var result = new RemoteNativeUseBar { _identity = _identity, _actor = _actor,
            _subwidgets = _subwidgets.Map(mirror), _options = _options,
            _preview = _preview != null ? mirror.CloneOf(_preview.transform)?.gameObject : null };
        foreach (Augment child in _augments) result._augments.Add(child.Map(mirror));
        foreach (Image image in _highlights) result._highlights.Add(mirror.CloneOf(image.transform)!.GetComponent<Image>());
        foreach (RemoteUseBarAnimation animation in _animations)
            result._animations.Add(animation.Map(mirror, result._materials));
        return result;
    }
    internal void Paint(NativeUseBarState state)
    {
        if (!SameIdentity(_identity, state)) return;
        _subwidgets.Paint(state); _subwidgets.TickHover();
        if (ReferenceEquals(_painted, state)) return;
        _painted = state;
        Active(_preview, state.PreviewVisible);
        for (int i = 0; i < _augments.Count; i++)
        {
            Augment ui = _augments[i]; Active(ui.Root, i < state.Augments.Length && (state.Augments[i].Flags & 1) != 0);
            if (i >= state.Augments.Length) continue;
            NativeUseBarAugment a = state.Augments[i];
            ui.Icon.sprite = a.IconSymbol == 8 ? UIInfoTools.Instance.GetCharacterActiveAbilityIcon(_actor!.Class.ID)
                : a.IconSymbol == 0 ? null : UIInfoTools.Instance.GetElementUseSprite((ElementInfusionBoardManager.EElement)(a.IconSymbol - 1));
            ui.Icon.enabled = a.IconSymbol != 0; ui.Icon.color = (a.Flags & 4) != 0 ? UIInfoTools.Instance.White : ui.Unfocused;
            ui.Mask.enabled = (a.Flags & 2) != 0;
            Transform parent = (a.Flags & 16) != 0 ? ui.Parent : ui.Root.transform;
            if (ui.ConsumeRect.parent != parent) ui.ConsumeRect.SetParent(parent, false);
            ui.ConsumeRect.anchoredPosition3D = new Vector3(a.ConsumePosition[0], a.ConsumePosition[1], a.ConsumePosition[2]);
            Active(ui.Consume, (a.Flags & 8) != 0);
            Active(ui.ConsumeIcon.gameObject, a.ConsumeSymbol != 0); ui.ConsumeIcon.sprite = Element(a.ConsumeSymbol);
            string text = a.Option > 0 && a.Option <= _options.Count ? _options[a.Option - 1].GetSelectedText() : string.Empty;
            Active(ui.ConsumeText.gameObject, text.Length != 0); if (ui.ConsumeText.text != text) ui.ConsumeText.text = text;
        }
        for (int i = 0; i < _highlights.Count && i * 4 + 3 < state.HighlightColors.Length; i++)
        { int at = i * 4; _highlights[i].color = new Color(state.HighlightColors[at], state.HighlightColors[at+1],
            state.HighlightColors[at+2], state.HighlightColors[at+3]); }
    }
    internal static bool SameIdentity(NativeUseBarState? a, NativeUseBarState? b) => a != null && b != null
        && a.Bar == b.Bar && a.Slot == b.Slot && a.ActorId == b.ActorId && a.ModelKind == b.ModelKind
        && a.ModelId == b.ModelId && a.ModelIndex == b.ModelIndex && a.ModelCount == b.ModelCount && a.SlotIdentity == b.SlotIdentity;
    private static Sprite? Element(byte symbol) => symbol == 0 ? null
        : UIInfoTools.Instance.GetElementPickerSprite((ElementInfusionBoardManager.EElement)(symbol - 1));
    private static void Active(GameObject? node, bool show) { if (node != null && node.activeSelf != show) node.SetActive(show); }
    internal void RestoreGeometry() { foreach (RemoteUseBarAnimation animation in _animations) animation.RestoreGeometry(); }
    internal void Destroy()
    {
        foreach (RemoteUseBarAnimation animation in _animations) animation.Destroy();
        foreach (Material material in _materials.Values)
            if (material != null) UnityEngine.Object.Destroy(material);
        _materials.Clear();
    }

    internal void ApplyAnimation(CharacterDecisionPresentation owner)
    {
        int address = _identity.Address;
        NativeUseBarState? latest = owner.NativeUseBarStates[address];
        if (!SameIdentity(_identity, latest)) return;
        if (!Playbacks.TryGetValue(owner, out Playback[] clocks))
        {
            clocks = new Playback[32]; for (int i = 0; i < 32; i++) clocks[i] = new Playback(); Playbacks.Add(owner, clocks);
        }
        Playback playback = clocks[address]; List<NativeUseBarSnapshot> history = owner.NativeUseBarHistories[address];
        int first = history.Count; float boundary = float.NegativeInfinity;
        for (int i = history.Count - 1; i >= 0; i--)
        { if (!SameIdentity(_identity, history[i].State)) { boundary = history[i].SampleTime; break; } first = i; }
        float now = Time.unscaledTime, latestTime = owner.NativeUseBarSampleTimes[address];
        if (!SameIdentity(playback.Identity, _identity) || boundary > playback.Boundary)
        { playback.Identity = _identity; playback.Boundary = boundary; playback.Clock.Reset(first < history.Count ? history[first].SampleTime : latestTime, now); }
        float cursor = playback.Clock.Advance(now, latestTime);
        NativeUseBarState from = latest!, to = latest!; float fromTime = latestTime, toTime = latestTime;
        for (int i = first; i < history.Count; i++)
        {
            NativeUseBarSnapshot frame = history[i]; if (!SameIdentity(_identity, frame.State)) continue;
            if (i == first || frame.SampleTime <= cursor) { from = to = frame.State!; fromTime = toTime = frame.SampleTime; }
            if (frame.SampleTime > cursor) { to = frame.State!; toTime = frame.SampleTime; break; }
        }
        float progress = playback.Clock.Progress(fromTime, toTime);
        for (int i = 0; i < _animations.Count; i++)
            _animations[i].ApplyNative((byte)i, from.Animations, to.Animations, progress);
    }
}
