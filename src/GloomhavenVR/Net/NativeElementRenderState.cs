using System;

namespace GloomhavenVR.Net;

/// <summary>Original element hierarchy output. Flags describe actual components, not recipes;
/// target identity comes from the matching original prefab hierarchy, never a game callback.</summary>
internal sealed class NativeElementRenderNode
{
    internal const ushort Active = 1, Rect = 2, Graphic = 4, Group = 8, Image = 16, Raw = 32,
        Renderer = 64, Text = 128, GraphicEnabled = 256, IgnoreParentGroups = 512, GroupEnabled = 1024;
    internal const ushort Components = Rect | Graphic | Group | Image | Raw | Renderer | Text;
    internal byte Parent, Sibling;
    internal uint Binding;
    internal ushort Flags, Sprite, OverrideSprite;
    // anchoredPosition3D (Rect) or localPosition3, scale3, quaternion4, anchors4, pivot2, size2
    internal float[] Geometry = new float[18];
    internal float[] Color = Array.Empty<float>(), RendererColor = Array.Empty<float>();
    internal float GroupAlpha, Fill, FontSize;
    internal float[] Uv = Array.Empty<float>();
    internal bool Validate(int index)
    {
        if (Binding == 0 || (Flags & ~2047) != 0 || (index == 0 ? Parent != 255 : Parent >= index)
            || Sibling >= NativeElementRenderState.NodesMax
            || ((Flags & (Image | Raw | Text)) != 0 && (Flags & Graphic) == 0)
            || ((Flags & Image) != 0 && (Flags & (Raw | Text)) != 0)
            || ((Flags & Raw) != 0 && (Flags & Text) != 0)
            || ((Flags & GraphicEnabled) != 0 && (Flags & Graphic) == 0)
            || ((Flags & (IgnoreParentGroups | GroupEnabled)) != 0 && (Flags & Group) == 0)
            || ((Flags & Group) == 0 && GroupAlpha != 0f)
            || ((Flags & Image) == 0 && (Fill != 0f || Sprite != 0 || OverrideSprite != 0))
            || ((Flags & Text) == 0 && FontSize != 0f)) return false;
        return Values(Geometry, (Flags & Rect) != 0 ? 18 : 10) && UnitRotation(Geometry) && Values(Color, (Flags & Graphic) != 0 ? 4 : 0)
            && Values(RendererColor, (Flags & Renderer) != 0 ? 4 : 0)
            && Values(Uv, (Flags & Raw) != 0 ? 4 : 0)
            && Finite(GroupAlpha) && Finite(Fill) && Finite(FontSize);
    }
    private static bool UnitRotation(float[] values)
    {
        double norm = 0;
        for (int i = 6; i < 10; i++) norm += (double)values[i] * values[i];
        return Math.Abs(norm - 1d) <= 0.01d; // native Transform quaternions are normalized
    }
    private static bool Finite(float value) => UseBarAnimationValue.Finite(value);
    private static bool Values(float[] values, int count)
    {
        if (values == null || values.Length != count) return false;
        foreach (float value in values) if (!Finite(value)) return false;
        return true;
    }
    internal NativeElementRenderNode Copy()
    {
        var copy = (NativeElementRenderNode)MemberwiseClone();
        copy.Geometry = (float[])Geometry.Clone(); copy.Color = (float[])Color.Clone();
        copy.RendererColor = (float[])RendererColor.Clone(); copy.Uv = (float[])Uv.Clone(); return copy;
    }
    internal bool Same(NativeElementRenderNode other) => Parent == other.Parent && Sibling == other.Sibling
        && Binding == other.Binding && Flags == other.Flags && Sprite == other.Sprite && OverrideSprite == other.OverrideSprite
        && GroupAlpha == other.GroupAlpha && Fill == other.Fill && FontSize == other.FontSize
        && Same(Geometry, other.Geometry) && Same(Color, other.Color) && Same(RendererColor, other.RendererColor) && Same(Uv, other.Uv);
    private static bool Same(float[] a, float[] b)
    { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
}

internal sealed class NativeElementRenderState
{
    internal const int NodesMax = 32;
    internal NativeElementRenderNode[] Nodes = Array.Empty<NativeElementRenderNode>();
    internal bool Validate()
    {
        if (Nodes == null || Nodes.Length == 0 || Nodes.Length > NodesMax) return false;
        for (int i = 0; i < Nodes.Length; i++)
        {
            if (Nodes[i] == null || !Nodes[i].Validate(i)) return false;
            for (int k = 0; k < i; k++)
                if (Nodes[k].Binding == Nodes[i].Binding
                    || (Nodes[k].Parent == Nodes[i].Parent && Nodes[k].Sibling == Nodes[i].Sibling)) return false;
        }
        return true;
    }
    internal NativeElementRenderState Copy()
    {
        var copy = new NativeElementRenderState { Nodes = new NativeElementRenderNode[Nodes.Length] };
        for (int i = 0; i < Nodes.Length; i++) copy.Nodes[i] = Nodes[i].Copy(); return copy;
    }
    internal bool Same(NativeElementRenderState other)
    {
        if (Nodes.Length != other.Nodes.Length) return false;
        for (int i = 0; i < Nodes.Length; i++) if (!Nodes[i].Same(other.Nodes[i])) return false; return true;
    }
}
