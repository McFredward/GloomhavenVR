using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Read original native output after the Unity animation update. Immutable publication
/// changes only with the picture; controllers, events, materials and transforms are never written.</summary>
internal static class NativeBoardSampler
{
    private static InfusionBoardUI? _source;
    private static NativeElementBindings[] _bindings = Array.Empty<NativeElementBindings>();
    private static NativeBoardState? _published;
    private static uint _generation;
    private static string? _refusal;
    private static readonly float[] Frame = new float[12];

    internal static NativeBoardState? Sample()
    {
        try
        {
            float depth = Mathf.Max(0f, WorldUI.WorldUIConfig.InitiativeDepthMaxSpreadPx.Value);
            InfusionBoardUI? source = InfusionBoardUI.Instance;
            if (source == null)
            {
                _source = null; _bindings = Array.Empty<NativeElementBindings>();
                if (_published == null || _published.Generation != 0 || _published.InitiativeDepthPixels != depth)
                    _published = new NativeBoardState(Time.unscaledTime, depth, 0, Array.Empty<NativeElementState>());
                return _published;
            }
            if (!ReferenceEquals(source, _source) || !BindingsMatch(source))
            {
                var bindings = new NativeElementBindings[6];
                for (int i = 0; i < 6; i++)
                {
                    if (!source.elementsUI.TryGetValue((ElementInfusionBoardManager.EElement)i, out InfusionElementUI element)
                        || element == null) throw new InvalidOperationException("original element widget is missing");
                    bindings[i] = new NativeElementBindings(element);
                }
                _bindings = bindings; _source = source;
                if (++_generation == 0) _generation++;
            }
            ReadFrame(source);
            bool changed = _published == null || _published.Generation != _generation
                           || _published.InitiativeDepthPixels != depth;
            for (int f = 0; !changed && f < Frame.Length; f++) changed |= Frame[f] != _published!.Frame[f];
            for (int i = 0; i < _bindings.Length; i++)
            {
                _bindings[i].Read();
                if (!changed && !_bindings[i].Scratch.Same(_published!.Elements[i])) changed = true;
            }
            if (changed)
            {
                var elements = new NativeElementState[6];
                for (int i = 0; i < elements.Length; i++) elements[i] = _bindings[i].Scratch;
                _published = new NativeBoardState(Time.unscaledTime, depth, _generation, elements, Frame);
            }
            _refusal = null;
            return _published;
        }
        catch (Exception e)
        {
            string refusal = e.GetType().Name + ": " + e.Message;
            if (_refusal != refusal)
            {
                _refusal = refusal;
                VRLog.Warn("Net", "NATIVE BOARD SAMPLE: original output unavailable: " + refusal);
            }
            return null; // no truncated or fabricated partial frame
        }
    }
    private static void ReadFrame(InfusionBoardUI source)
    {
        Array.Clear(Frame, 0, Frame.Length);
        var panels = WorldUI.CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            var panel = panels[i];
            if (panel.HostRect == null || !ReferenceEquals(panel.Target, source.transform)) continue;
            Frame[0] = panel.HostRect.rect.width; Frame[1] = panel.HostRect.rect.height; break;
        }
        if (source.transform.parent is RectTransform parent)
        { Frame[2] = parent.rect.width; Frame[3] = parent.rect.height; }
        NativeElementBindings.ReadRect((RectTransform)source.transform, Frame, 4);
    }
    private static bool BindingsMatch(InfusionBoardUI source)
    {
        if (_bindings.Length != 6) return false;
        for (int i = 0; i < 6; i++)
            if (!source.elementsUI.TryGetValue((ElementInfusionBoardManager.EElement)i, out InfusionElementUI element)
                || !ReferenceEquals(element, _bindings[i].Source)) return false;
        return true;
    }
    internal static void Reset()
    { _source = null; _bindings = Array.Empty<NativeElementBindings>(); _published = null; _refusal = null; }
}

/// <summary>Canonical original field/setting bindings shared by sampling and clone playback.</summary>
internal sealed class NativeElementBindings
{
    internal readonly InfusionElementUI Source;
    internal readonly Graphic[] Graphics;
    internal readonly Graphic[] Effects;
    internal readonly NativeUseBarAnimationBinding[][] Animations;
    internal readonly NativeElementState Scratch;
    internal NativeElementBindings(InfusionElementUI source)
    {
        Source = source;
        Graphics = new Graphic[] { source.elementImage, source.creationImage, source.availableHighlight,
            source.createElementText, source.creatingElementText, source.creationBumpImage, source.creationTextBackgroundImage };
        foreach (Graphic graphic in Graphics) Inside(graphic != null ? graphic.transform : null);
        Effects = BindEffects(source.effectsControl);
        Animations = new[] {
            NativeUseBarAnimationBinding.Capture(source.animatorCreating, source.transform),
            NativeUseBarAnimationBinding.Capture(source.animatorCreated, source.transform),
            BindLoop(source.loopAnimatorCreating),
        };
        Scratch = new NativeElementState { Graphics = NewGraphics(Graphics.Length), Effects = NewGraphics(Effects.Length),
            Animations = new UseBarAnimationValue[Animations.Length][] };
        for (int a = 0; a < Animations.Length; a++)
        {
            Scratch.Animations[a] = new UseBarAnimationValue[Animations[a].Length];
            for (int i = 0; i < Animations[a].Length; i++)
            {
                NativeUseBarAnimationBinding binding = Animations[a][i];
                Scratch.Animations[a][i] = new UseBarAnimationValue { SettingIndex = binding.SettingIndex,
                    Kind = binding.Kind, Values = new float[binding.Components] };
            }
        }
    }
    private static NativeElementGraphic[] NewGraphics(int count)
    {
        var result = new NativeElementGraphic[count];
        for (int i = 0; i < count; i++) result[i] = new NativeElementGraphic();
        return result;
    }
    private void Inside(Transform? target)
    {
        if (target == null || (!ReferenceEquals(target, Source.transform) && !target.IsChildOf(Source.transform)))
            throw new InvalidOperationException("original element target is missing or outside its widget");
    }
    private Graphic[] BindEffects(UIFX_MaterialFX_Control fx)
    {
        var images = new List<Graphic>();
        void Add(Image? image)
        {
            if (image == null || images.Contains(image)) return;
            Inside(image.transform); images.Add(image);
        }
        void AddAll(List<Image>? list) { if (list != null) foreach (Image image in list) Add(image); }
        if (fx != null)
        {
            Add(fx.MainIcon); Add(fx.MainIcon2);
            AddAll(fx.MainIconFX); AddAll(fx.MainIcon2FX); AddAll(fx.TextAndSubIconFX); AddAll(fx.ActivateFX);
        }
        if (images.Count > NativeElementState.FxMax) throw new InvalidOperationException("original element FX field count exceeds protocol bound");
        return images.ToArray();
    }
    private NativeUseBarAnimationBinding[] BindLoop(LoopAnimator loop)
    {
        if (loop == null || loop.effects == null) return Array.Empty<NativeUseBarAnimationBinding>();
        if (loop.effects.Count > NativeElementState.SettingsMax) throw new InvalidOperationException("original element loop setting count exceeds protocol bound");
        var result = new NativeUseBarAnimationBinding[loop.effects.Count];
        for (int i = 0; i < result.Length; i++)
        {
            AnimationSetting setting = loop.effects[i];
            Inside(setting.Transform);
            var binding = new NativeUseBarAnimationBinding { SettingIndex = (byte)i, Target = setting.Transform, Rect = setting.Transform };
            switch (setting.Animation)
            {
                case TweenAction.SCALE: binding.Kind = UseBarAnimationKind.Scale3; break;
                case TweenAction.MOVE: binding.Kind = UseBarAnimationKind.AnchoredPosition3; break;
                case TweenAction.MOVE_LOCAL: binding.Kind = UseBarAnimationKind.LocalPosition3; break;
                case TweenAction.CANVASGROUP_ALPHA:
                    binding.Kind = UseBarAnimationKind.CanvasGroupAlpha1;
                    binding.Group = setting.Transform.GetComponent<CanvasGroup>(); break;
                case TweenAction.CANVAS_ALPHA:
                    binding.Kind = UseBarAnimationKind.GraphicAlpha1;
                    binding.Graphic = setting.Transform.GetComponent<Graphic>(); break;
                default: throw new InvalidOperationException("unsupported original element loop setting");
            }
            binding.Components = UseBarAnimationValue.Components(binding.Kind);
            binding.Read(new float[binding.Components]); result[i] = binding;
        }
        return result;
    }
    internal void Read()
    {
        int state = Source.lastState switch { ElementInfusionBoardManager.EColumn.Inert => 1,
            ElementInfusionBoardManager.EColumn.Strong => 2, ElementInfusionBoardManager.EColumn.Waning => 3, _ => 0 };
        Scratch.Flags = (byte)((Source.gameObject.activeSelf ? 1 : 0) | (Source.elementImage.enabled ? 2 : 0)
            | (Source.creationImage.enabled ? 4 : 0) | (Source.availableHighlight.enabled ? 8 : 0) | (state << 4));
        Scratch.Sibling = checked((byte)Source.transform.GetSiblingIndex());
        ReadRect((RectTransform)Source.transform, Scratch.Rect, 0);
        for (int i = 0; i < Graphics.Length; i++) ReadGraphic(Graphics[i], Scratch.Graphics[i]);
        for (int i = 0; i < Effects.Length; i++) ReadGraphic(Effects[i], Scratch.Effects[i]);
        for (int a = 0; a < Animations.Length; a++)
            for (int i = 0; i < Animations[a].Length; i++) Animations[a][i].Read(Scratch.Animations[a][i].Values);
    }
    internal static void ReadRect(RectTransform rect, float[] values, int at)
    {
        Vector3 p = rect.anchoredPosition3D, s = rect.localScale; Vector2 size = rect.sizeDelta;
        values[at] = p.x; values[at + 1] = p.y; values[at + 2] = p.z;
        values[at + 3] = s.x; values[at + 4] = s.y; values[at + 5] = s.z;
        values[at + 6] = size.x; values[at + 7] = size.y;
    }
    private static void ReadGraphic(Graphic graphic, NativeElementGraphic state)
    {
        Color color = graphic.color;
        state.Flags = (byte)((graphic.gameObject.activeSelf ? 1 : 0) | (graphic.enabled ? 2 : 0));
        state.R = color.r; state.G = color.g; state.B = color.b; state.A = color.a; state.Fx = 0f;
        Material material = graphic.material;
        if (material != null && material.HasProperty("_FXAnim"))
        { state.Flags |= 4; state.Fx = material.GetFloat("_FXAnim"); }
    }
}
