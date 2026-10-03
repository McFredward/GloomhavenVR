using System;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Numeric native presentation which can move without re-sending original artwork.
/// Strings, assets, TMP contents/styles and material tables always stay on the complete lane.</summary>
internal static class TownServiceFastNumbers
{
    internal static bool IsFast(ushort key) => key is TownServiceProperty.Transform
        or TownServiceProperty.Active or TownServiceProperty.Graphic or TownServiceProperty.Renderer
        or TownServiceProperty.Group or TownServiceProperty.Image or TownServiceProperty.RawImage
        or TownServiceProperty.Mask or TownServiceProperty.RectMask or TownServiceProperty.Shadow
        or TownServiceProperty.Outline or TownServiceProperty.Sibling;
    internal static bool IsMaterial(ushort key) => key is TownServiceProperty.Material or TownServiceProperty.TextMaterial
        || key >= TownServiceProperty.MeshMaterial0 && key <= TownServiceProperty.MeshMaterial7;
    internal static bool MaterialNumber(int offset) => offset >= 5 && (offset - 4) % 5 != 0;

    internal static bool Valid(ushort key, int count) => key switch
    {
        TownServiceProperty.Transform => count is 10 or 18,
        TownServiceProperty.Active or TownServiceProperty.Sibling => count == 1,
        TownServiceProperty.Graphic => count == 5,
        TownServiceProperty.Renderer or TownServiceProperty.RawImage => count == 4,
        TownServiceProperty.Group => count == 3,
        TownServiceProperty.Image => count == 8,
        TownServiceProperty.Mask => count == 2,
        TownServiceProperty.RectMask => count == 7,
        TownServiceProperty.Shadow or TownServiceProperty.Outline => count == 8,
        _ => false,
    };

    internal static bool SameArtwork(TownServiceFrame a, TownServiceFrame b)
    {
        if (a.VisitorStock != b.VisitorStock || a.PublicCatalog != b.PublicCatalog
            || a.PublicClaim != b.PublicClaim || a.Session != b.Session || a.Service != b.Service
            || a.Module != b.Module || a.Template != b.Template || a.TemplateAddress != b.TemplateAddress
            || a.Structure != b.Structure || a.Nodes.Length != b.Nodes.Length
            || a.ParentModule != b.ParentModule || a.ParentBinding != b.ParentBinding
            || a.HasCanvasFrame != b.HasCanvasFrame || a.CanvasSortingLayer != b.CanvasSortingLayer
            || a.CanvasSortingOrder != b.CanvasSortingOrder || a.Rack != null || b.Rack != null
            || (a.RackMember == null) != (b.RackMember == null)
            || a.RackMember != null && !a.RackMember.Same(b.RackMember)) return false;
        for (int i = 0; i < a.Nodes.Length; i++)
        {
            TownServiceNode before = a.Nodes[i], after = b.Nodes[i];
            if (before.Binding != after.Binding || before.Values.Count != after.Values.Count) return false;
            foreach (var property in before.Values)
            {
                if (!after.Values.TryGetValue(property.Key, out TownServiceValue? value)) return false;
                if (!IsFast(property.Key) && !IsMaterial(property.Key)) { if (!property.Value.Same(value)) return false; continue; }
                if (property.Value.Numbers.Length != value.Numbers.Length) return false;
                if (IsMaterial(property.Key))
                    for (int number = 0; number < value.Numbers.Length; number++)
                        if (!MaterialNumber(number) && property.Value.Numbers[number] != value.Numbers[number]) return false;
                if (property.Value.Text.Length != value.Text.Length) return false;
                for (int t = 0; t < value.Text.Length; t++)
                    if (!StringComparer.Ordinal.Equals(property.Value.Text[t], value.Text[t])) return false;
            }
        }
        return true;
    }
}
