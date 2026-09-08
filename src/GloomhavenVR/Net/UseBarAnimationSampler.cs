using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Samples the original show animation's rendered properties after the native update.
/// No tween, event, slot controller or animation reset runs on the owner's widgets.</summary>
internal static class UseBarAnimationSampler
{
    private sealed class Cache
    {
        internal UIUseActiveBonus? Source;
        internal GUIAnimator? Animator;
        internal CActiveBonus? Model;
        internal NativeUseBarAnimationBinding[] Bindings = Array.Empty<NativeUseBarAnimationBinding>();
        internal UseBarAnimationState Scratch = new();
        internal UseBarAnimationState? Published;
        internal string? Refusal;
    }

    private static readonly Cache?[] Slots = new Cache?[NetProtocol.UseBarsMaxSlots];
    internal static void Reset() => Array.Clear(Slots, 0, Slots.Length);

    internal static UseBarAnimationState? Sample(UIUseActiveBonus source, byte slot)
    {
        if (slot >= Slots.Length) return null;
        Cache cache = Slots[slot] ??= new Cache();
        try
        {
            CActiveBonus? model = null;
            foreach (KeyValuePair<CActiveBonus, UIUseActiveBonus> pair in Singleton<UIActiveBonusBar>.Instance.activeBonusSlots)
                if (ReferenceEquals(pair.Value, source)) { model = pair.Key; break; }
            if (model == null) return null;
            if (!ReferenceEquals(cache.Source, source) || !ReferenceEquals(cache.Animator, source.showAnimation)
                || !ReferenceEquals(cache.Model, model))
            {
                NativeUseBarAnimationBinding[] bindings = NativeUseBarAnimationBinding.Capture(source);
                var scratch = new UseBarAnimationState
                {
                    Slot = slot,
                    Entries = new UseBarAnimationValue[bindings.Length],
                };
                for (int i = 0; i < bindings.Length; i++)
                {
                    NativeUseBarAnimationBinding binding = bindings[i];
                    scratch.Entries[i] = new UseBarAnimationValue
                    {
                        SettingIndex = binding.SettingIndex, Kind = binding.Kind,
                        Values = new float[binding.Components],
                    };
                }
                // Publish cache keys only after every binding and buffer has been constructed.
                cache.Bindings = bindings; cache.Scratch = scratch;
                cache.Source = source; cache.Animator = source.showAnimation; cache.Model = model;
                cache.Published = null;
            }
            UseBarAnimationState state = cache.Scratch;
            state.ActorId = NetFigures.StableActorId(source.actor);
            state.SlotIdentity = UseBarSlotSymbol.SlotId(0, source.transform);
            for (int i = 0; i < cache.Bindings.Length; i++)
                cache.Bindings[i].Read(state.Entries[i].Values);
            if (!state.Validate())
                throw new InvalidOperationException("native animation sample exceeds wire bounds or has no verified slot identity");
            if (!UseBarAnimationState.SameState(cache.Published, state)) cache.Published = state.Snapshot();
            cache.Refusal = null;
            return cache.Published;
        }
        catch (Exception e)
        {
            if (cache.Refusal != e.Message)
            {
                cache.Refusal = e.Message;
                VRLog.Warn("Net", $"USE BAR ANIMATION: slot {slot} sample refused: {e.Message}");
            }
            return null;
        }
    }


}

/// <summary>A setting ordinal binds a wire value to an original, locally verified visual target.
/// Both the owner sampler and the inactive native-prefab clone use this same mapping.</summary>
internal sealed class NativeUseBarAnimationBinding
{
    internal byte SettingIndex;
    internal UseBarAnimationKind Kind;
    internal int Components;
    internal Transform Target = null!;
    internal RectTransform? Rect;
    internal CanvasGroup? Group;
    internal Graphic? Graphic;
    internal TMP_Text? Text;
    internal RawImage? Raw;
    internal Image? Image;
    internal string? MaterialProperty;
    internal bool MaterialForRendering;
    internal TMP_Text[] TextTargets = Array.Empty<TMP_Text>();
    internal Material? OriginalMaterial, FinalMaterial;
    private string? _progressProperty;
    private int _progressChannel = -1;
    private float _progressOriginal, _progressDelta;


    internal static NativeUseBarAnimationBinding[] Capture(UIUseActiveBonus source)
        => Capture(source, source.showAnimation);

    internal static NativeUseBarAnimationBinding[] Capture(GUIAnimator? animator, Transform root) => Capture(root, animator);

    internal static NativeUseBarAnimationBinding[] Capture(Component source, GUIAnimator? showAnimation)
    {
        if (showAnimation == null) return Array.Empty<NativeUseBarAnimationBinding>();
        if (showAnimation is not LeanTweenGUIAnimator animator)
            throw new InvalidOperationException("native show animator does not expose original LeanTween settings");
        List<LeanTweenGUIAnimationSetting> settings = animator.GetSettings();
        var result = new List<NativeUseBarAnimationBinding>(settings.Count);
        for (int i = 0; i < settings.Count; i++)
        {
            if (i > byte.MaxValue) throw new InvalidOperationException("native setting ordinal exceeds byte range");
            var binding = new NativeUseBarAnimationBinding { SettingIndex = (byte)i };
            switch (settings[i])
            {
                case LeanTweenGuiAnimationSettingScale scale:
                    binding.Rect = scale.Target;
                    binding.Kind = UseBarAnimationKind.Scale3; binding.Components = 3;
                    break;
                case LeanTweenGuiAnimationSettingMove move:
                    binding.Rect = move.Target;
                    switch (move.Animation)
                    {
                        // LTDescr.setCanvasMove writes anchoredPosition3D, including z. The
                        // native setting's SetFinalValue uses 2D and is not the tween's semantics.
                        case GUIAnimationMoveType.MOVE:
                            binding.Kind = UseBarAnimationKind.AnchoredPosition3; binding.Components = 3; break;
                        case GUIAnimationMoveType.MOVE_LOCAL:
                            binding.Kind = UseBarAnimationKind.LocalPosition3; binding.Components = 3; break;
                        case GUIAnimationMoveType.SIZE_DELTA:
                            binding.Kind = UseBarAnimationKind.SizeDelta2; binding.Components = 2; break;
                        case GUIAnimationMoveType.UV:
                            binding.Raw = move.Target.GetComponent<RawImage>();
                            binding.Kind = UseBarAnimationKind.UvPosition2; binding.Components = 2; break;
                        default: throw new InvalidOperationException("unknown native move setting");
                    }
                    break;
                case LeanTweenGuiAnimationSettingFade fade:
                    binding.Rect = fade.Target; binding.Components = 1;
                    switch (fade.Animation)
                    {
                        case GUIAnimationFadeType.CANVAS_GROUP:
                            binding.Group = fade.Target.GetComponent<CanvasGroup>();
                            binding.Kind = UseBarAnimationKind.CanvasGroupAlpha1; break;
                        case GUIAnimationFadeType.IMAGE:
                        case GUIAnimationFadeType.GRAPHIC:
                            binding.Graphic = fade.Target.GetComponent<Graphic>();
                            binding.Kind = UseBarAnimationKind.GraphicAlpha1; break;
                        case GUIAnimationFadeType.TEXT:
                            binding.Text = fade.Target.GetComponent<TMP_Text>();
                            binding.Kind = UseBarAnimationKind.TextAlpha1; break;
                        default: throw new InvalidOperationException("unknown native fade setting");
                    }
                    break;
                case LeanTweenGUIAnimationSettingColor color:
                    binding.Rect = color.Target; binding.Components = 4;
                    if (color.Animation == GUIAnimationColorType.TEXT)
                    { binding.Text = color.Target.GetComponent<TMP_Text>(); binding.Kind = UseBarAnimationKind.TextColor4; }
                    else if (color.Animation == GUIAnimationColorType.GRAPHIC)
                    { binding.Graphic = color.Target.GetComponent<Graphic>(); binding.Kind = UseBarAnimationKind.GraphicColor4; }
                    else throw new InvalidOperationException("unknown native color setting");
                    break;
                case LeanTweenGuiAnimationSettingMaterialPropertyFloat material:
                    binding.Graphic = material.Target;
                    binding.Rect = material.Target.rectTransform;
                    binding.MaterialProperty = material.property;
                    binding.MaterialForRendering = material.Animation == GUIAnimationMaterialType.MaterialForRendering;
                    binding.Kind = UseBarAnimationKind.MaterialFloat1; binding.Components = 1;
                    break;
                case CustomLeanTweenGuiAnimationSetting custom:
                    if (custom.animation is GUIImageFillAnimator fill)
                    {
                        binding.Image = fill.image; binding.Rect = fill.image.rectTransform;
                        binding.Kind = UseBarAnimationKind.CustomFill1; binding.Components = 1;
                    }
                    else if (custom.animation is UICampaignRewardRevealAnimator reveal)
                    {
                        binding.Rect = reveal.revealRect;
                        binding.Kind = UseBarAnimationKind.CustomSizeDelta2; binding.Components = 2;
                    }
                    else if (custom.animation is UITextMeshProMaterialAnimator textMaterial)
                    {
                        binding.TextTargets = textMaterial.texts.ToArray();
                        if (binding.TextTargets.Length == 0)
                            throw new InvalidOperationException("native material animation has no text targets");
                        binding.Rect = binding.TextTargets[0].rectTransform;
                        binding.OriginalMaterial = textMaterial.originalMaterial;
                        binding.FinalMaterial = textMaterial.finalMaterial;
                        binding.Kind = UseBarAnimationKind.CustomTextMaterial1; binding.Components = 1;
                        binding.BuildMaterialProbe();
                    }
                    else throw new InvalidOperationException("unknown native custom show setting");
                    break;
                default: throw new InvalidOperationException("unknown native show setting");
            }
            binding.Target = binding.Rect != null ? binding.Rect : throw new InvalidOperationException("native animation target is missing");
            if (!ReferenceEquals(binding.Target, source.transform) && !binding.Target.IsChildOf(source.transform))
                throw new InvalidOperationException("native animation target is outside its original slot");
            foreach (TMP_Text text in binding.TextTargets)
                if (text == null || (!ReferenceEquals(text.transform, source.transform) && !text.transform.IsChildOf(source.transform)))
                    throw new InvalidOperationException("native material text target is outside its original slot");
            // Validate component bindings while building the cache, before any snapshot is sent.
            binding.Read(new float[binding.Components]);
            result.Add(binding);
        }
        if (result.Count > 16) throw new InvalidOperationException("native show animation exceeds 16 setting entries; no truncation allowed");
        return result.ToArray();
    }

    internal void Read(float[] values)
    {
        switch (Kind)
        {
            case UseBarAnimationKind.Scale3: Put(values, Rect!.localScale); break;
            case UseBarAnimationKind.AnchoredPosition3: Put(values, Rect!.anchoredPosition3D); break;
            case UseBarAnimationKind.LocalPosition3: Put(values, Rect!.localPosition); break;
            case UseBarAnimationKind.SizeDelta2:
            case UseBarAnimationKind.CustomSizeDelta2: Put(values, Rect!.sizeDelta); break;
            case UseBarAnimationKind.UvPosition2: Put(values, Raw!.uvRect.position); break;
            case UseBarAnimationKind.CanvasGroupAlpha1: values[0] = Group!.alpha; break;
            case UseBarAnimationKind.GraphicAlpha1: values[0] = Graphic!.color.a; break;
            case UseBarAnimationKind.TextAlpha1: values[0] = Text!.alpha; break;
            case UseBarAnimationKind.GraphicColor4: Put(values, Graphic!.color); break;
            case UseBarAnimationKind.TextColor4: Put(values, Text!.color); break;
            case UseBarAnimationKind.CustomFill1: values[0] = Image!.fillAmount; break;
            case UseBarAnimationKind.CustomTextMaterial1: values[0] = MaterialProgress(); break;
            case UseBarAnimationKind.MaterialFloat1:
                Material material = MaterialForRendering ? Graphic!.materialForRendering : Graphic!.material;
                if (material == null || !material.HasProperty(MaterialProperty))
                    throw new InvalidOperationException("native animation material property is missing");
                values[0] = material.GetFloat(MaterialProperty); break;
            default: throw new InvalidOperationException("unknown native animation value kind");
        }
    }

    private void BuildMaterialProbe()
    {
        if (OriginalMaterial == null || FinalMaterial == null || OriginalMaterial.shader != FinalMaterial.shader)
            throw new InvalidOperationException("native text material animation has incompatible authored materials");
        Shader shader = OriginalMaterial.shader;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            string property = shader.GetPropertyName(i);
            ShaderPropertyType type = shader.GetPropertyType(i);
            if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                ConsiderProbe(property, -1, OriginalMaterial.GetFloat(property), FinalMaterial.GetFloat(property));
            else if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector)
            {
                Vector4 original = OriginalMaterial.GetVector(property), final = FinalMaterial.GetVector(property);
                for (int channel = 0; channel < 4; channel++)
                    ConsiderProbe(property, channel, original[channel], final[channel]);
            }
        }
    }

    private void ConsiderProbe(string property, int channel, float original, float final)
    {
        float delta = final - original;
        if (Mathf.Abs(delta) <= Mathf.Abs(_progressDelta)) return;
        _progressProperty = property; _progressChannel = channel;
        _progressOriginal = original; _progressDelta = delta;
    }

    private float MaterialProgress()
    {
        // UITextMeshProMaterialAnimator does exactly Material.Lerp(original, final, value),
        // sharing that result across its original text list. Recover the rendered value from
        // the largest authored numeric difference; neither tween timing nor easing is guessed.
        Material material = TextTargets[0].fontSharedMaterial;
        if (ReferenceEquals(material, OriginalMaterial)) return 0f;
        if (ReferenceEquals(material, FinalMaterial)) return 1f;
        if (_progressProperty == null) return 0f; // all interpolated numeric properties are equal
        float value = _progressChannel < 0 ? material.GetFloat(_progressProperty)
            : material.GetVector(_progressProperty)[_progressChannel];
        return (value - _progressOriginal) / _progressDelta;
    }

    private static void Put(float[] values, Vector3 value)
    { values[0] = value.x; values[1] = value.y; if (values.Length > 2) values[2] = value.z; }
    private static void Put(float[] values, Color value)
    { values[0] = value.r; values[1] = value.g; values[2] = value.b; values[3] = value.a; }
}
