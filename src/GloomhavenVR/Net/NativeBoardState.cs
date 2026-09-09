using System;

namespace GloomhavenVR.Net;

internal sealed class NativeElementGraphic
{
    internal byte Flags; // bit0 activeSelf, bit1 Graphic.enabled, bit2 material has native _FXAnim
    internal float R, G, B, A, Fx;
    internal bool Validate() => (Flags & ~7) == 0 && Finite(R) && Finite(G) && Finite(B) && Finite(A) && Finite(Fx);
    private static bool Finite(float value) => UseBarAnimationValue.Finite(value);
    internal NativeElementGraphic Copy() => (NativeElementGraphic)MemberwiseClone();
    internal bool Same(NativeElementGraphic other) => Flags == other.Flags && R == other.R
        && G == other.G && B == other.B && A == other.A && Fx == other.Fx;
}

internal sealed class NativeElementState
{
    internal const int GraphicCount = 7, FxMax = 32, AnimationCount = 3, SettingsMax = 16;
    // bit0 root activeSelf; bit1 element image enabled; bit2 creation image enabled;
    // bit3 available ring enabled; bits4–5 lastState:0null/1inert/2strong/3waning.
    internal byte Flags, Sibling;
    internal float[] Rect = new float[8]; // anchoredPosition3D, localScale, sizeDelta
    internal NativeElementGraphic[] Graphics = Array.Empty<NativeElementGraphic>();
    internal NativeElementGraphic[] Effects = Array.Empty<NativeElementGraphic>();
    internal UseBarAnimationValue[][] Animations = Array.Empty<UseBarAnimationValue[]>();
    internal bool Validate()
    {
        if (Rect == null || Rect.Length != 8) return false;
        foreach (float value in Rect) if (!UseBarAnimationValue.Finite(value)) return false;
        if ((Flags & ~63) != 0 || Sibling >= 6 || Graphics == null || Graphics.Length != GraphicCount
            || Effects == null || Effects.Length > FxMax || Animations == null || Animations.Length != AnimationCount) return false;
        foreach (NativeElementGraphic value in Graphics) if (value == null || !value.Validate()) return false;
        foreach (NativeElementGraphic value in Effects) if (value == null || !value.Validate()) return false;
        foreach (UseBarAnimationValue[] set in Animations)
        {
            if (set == null || set.Length > SettingsMax) return false;
            int previous = -1;
            foreach (UseBarAnimationValue value in set)
            {
                if (value == null || !value.Validate() || value.SettingIndex <= previous) return false;
                previous = value.SettingIndex;
            }
        }
        return true;
    }
    internal NativeElementState Copy()
    {
        var copy = new NativeElementState { Flags = Flags, Sibling = Sibling, Rect = (float[])Rect.Clone(),
            Graphics = new NativeElementGraphic[Graphics.Length], Effects = new NativeElementGraphic[Effects.Length],
            Animations = new UseBarAnimationValue[Animations.Length][] };
        for (int i = 0; i < Graphics.Length; i++) copy.Graphics[i] = Graphics[i].Copy();
        for (int i = 0; i < Effects.Length; i++) copy.Effects[i] = Effects[i].Copy();
        for (int a = 0; a < Animations.Length; a++)
        {
            copy.Animations[a] = new UseBarAnimationValue[Animations[a].Length];
            for (int i = 0; i < Animations[a].Length; i++) copy.Animations[a][i] = Animations[a][i].Snapshot();
        }
        return copy;
    }
    internal bool Same(NativeElementState other)
    {
        if (Flags != other.Flags || Sibling != other.Sibling || Graphics.Length != other.Graphics.Length
            || Effects.Length != other.Effects.Length || Animations.Length != other.Animations.Length) return false;
        for (int i = 0; i < Rect.Length; i++) if (Rect[i] != other.Rect[i]) return false;
        for (int i = 0; i < Graphics.Length; i++) if (!Graphics[i].Same(other.Graphics[i])) return false;
        for (int i = 0; i < Effects.Length; i++) if (!Effects[i].Same(other.Effects[i])) return false;
        for (int a = 0; a < Animations.Length; a++)
        {
            if (Animations[a].Length != other.Animations[a].Length) return false;
            for (int i = 0; i < Animations[a].Length; i++)
            {
                UseBarAnimationValue x = Animations[a][i], y = other.Animations[a][i];
                if (x.SettingIndex != y.SettingIndex || x.Kind != y.Kind || x.Values.Length != y.Values.Length) return false;
                for (int k = 0; k < x.Values.Length; k++) if (x.Values[k] != y.Values[k]) return false;
            }
        }
        return true;
    }
}

/// <summary>One owner-rendered original board frame. No element names, card identifiers, callbacks
/// or paths travel. Animation settings and native effect fields are resolved from matching originals.</summary>
internal sealed class NativeBoardState
{
    internal readonly float SampleTime, InitiativeDepthPixels;
    internal readonly uint Generation;
    internal readonly NativeElementState[] Elements;
    internal readonly NativeElementRenderState[]? RenderElements;
    internal readonly float[] Frame; // fitted host W/H, source parent W/H, source root rect8
    internal NativeBoardState(float sampleTime, float initiativeDepthPixels, uint generation, NativeElementState[] elements,
        float[]? frame = null, NativeElementRenderState[]? renderElements = null)
    {
        if (!UseBarAnimationValue.Finite(sampleTime) || sampleTime < 0
            || !UseBarAnimationValue.Finite(initiativeDepthPixels) || initiativeDepthPixels < 0
            || elements == null || (elements.Length != 0 && elements.Length != 6)
            || (elements.Length == 0) != (generation == 0)) throw new ArgumentException("Invalid native board frame.");
        frame ??= new float[12];
        if (frame.Length != 12) throw new ArgumentException("Invalid native board frame geometry.");
        for (int i = 0; i < frame.Length; i++)
            if (!UseBarAnimationValue.Finite(frame[i]) || (i < 4 && frame[i] < 0))
                throw new ArgumentException("Invalid native board frame geometry.");
        int siblings = 0;
        foreach (NativeElementState state in elements)
        {
            if (state == null || !state.Validate() || (siblings & (1 << state.Sibling)) != 0)
                throw new ArgumentException("Invalid native element frame.");
            siblings |= 1 << state.Sibling;
        }
        if (renderElements != null)
        {
            if (elements.Length != 6 || renderElements.Length != 6)
                throw new ArgumentException("Invalid native element hierarchy count.");
            RenderElements = new NativeElementRenderState[6];
            for (int i = 0; i < 6; i++)
            {
                if (renderElements[i] == null || !renderElements[i].Validate())
                    throw new ArgumentException("Invalid native element hierarchy.");
                NativeElementRenderNode root = renderElements[i].Nodes[0];
                if ((root.Flags & NativeElementRenderNode.Rect) == 0 || root.Sibling != elements[i].Sibling
                    || (root.Flags & NativeElementRenderNode.Active) != (elements[i].Flags & 1))
                    throw new ArgumentException("Native element core and hierarchy disagree.");
                for (int r = 0; r < 8; r++)
                    if (elements[i].Rect[r] != root.Geometry[r < 6 ? r : r + 10])
                        throw new ArgumentException("Native element core and hierarchy geometry disagree.");
                RenderElements[i] = renderElements[i].Copy();
            }
        }
        SampleTime = sampleTime; InitiativeDepthPixels = initiativeDepthPixels; Generation = generation;
        Frame = (float[])frame.Clone();
        Elements = new NativeElementState[elements.Length];
        for (int i = 0; i < elements.Length; i++) Elements[i] = elements[i].Copy();
    }
    internal NativeBoardState CopyWithTime(float time) =>
        new(time, InitiativeDepthPixels, Generation, Elements, Frame, RenderElements);
    internal bool SamePicture(NativeBoardState other)
    {
        if (Generation != other.Generation || InitiativeDepthPixels != other.InitiativeDepthPixels
            || Elements.Length != other.Elements.Length) return false;
        if ((RenderElements == null) != (other.RenderElements == null)) return false;
        if (RenderElements != null)
            for (int i = 0; i < RenderElements.Length; i++) if (!RenderElements[i].Same(other.RenderElements![i])) return false;
        for (int i = 0; i < Frame.Length; i++) if (Frame[i] != other.Frame[i]) return false;
        for (int i = 0; i < Elements.Length; i++) if (!Elements[i].Same(other.Elements[i])) return false;
        return true;
    }
}
