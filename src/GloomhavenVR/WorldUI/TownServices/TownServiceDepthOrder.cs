using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Physical town props and their native inscriptions share the converted-window
/// distance ladder. A transparent purse or a native canvas at order zero otherwise loses to
/// the character window even when that window is physically behind it. This changes no depth
/// writes, transparency, source material, or gameplay input.</summary>
internal static class TownServiceDepthOrder
{
    private sealed class Anchor : IFurnitureOrderAnchor
    {
        private readonly Transform _root;
        internal Transform? Print;
        internal Anchor(Transform root) => _root = root;
        public string FurnitureOrderName => "town presentation";
        public bool FurnitureOrderAlive => _root != null;
        public float FurnitureEyeDistance(Vector3 eye)
        {
            Transform root = Print != null ? Print : _root;
            if (root is RectTransform rect)
            {
                Vector3 point = rect.InverseTransformPoint(eye);
                Rect area = rect.rect;
                return Vector3.Distance(eye, rect.TransformPoint(new Vector3(
                    Mathf.Clamp(point.x, area.xMin, area.xMax), Mathf.Clamp(point.y, area.yMin, area.yMax), 0f)));
            }
            return Vector3.Distance(eye, root.position);
        }
    }
    private static readonly ConditionalWeakTable<Transform, Anchor> Anchors = new();
    private sealed class OfferedOrder
    {
        internal Anchor Anchor = null!;
        internal Transform Root = null!, Print = null!;
        internal Canvas Canvas = null!, Paper = null!;
        internal Transform? Parent, PrintParent;
        internal OfferedPlate Plate = null!;
        internal int Depth, Identity;
    }
    private sealed class OfferedPlate
    {
        internal Canvas Canvas = null!;
        internal int PreviousOrder;
        internal readonly List<OfferedOrder> Roots = new();
        internal OfferedOrder Owner = null!;
    }
    private static readonly Dictionary<Transform, OfferedOrder> Offered = new();
    private static readonly Dictionary<Canvas, OfferedPlate> OfferedPlates = new();
    private static readonly List<Transform> Retired = new();
    // Existing native membership admits 4096 modules per peer, with at most
    // eight retained peer presentations plus the single local offered holder.
    private const int MaxOfferedRoots = 8 * 4096 + 1;

    /// <summary>Native offered ink and its physical print are one transparent plate.
    /// Their native canvas rects differ: the effect includes the aura, whereas the
    /// paper has the card's finite rect. Ranking those rects independently lets a
    /// later opaque face erase the nearer additive, non-depth-writing FlexFrame.
    /// The exact original-to-print relation supplies the shared distance and the
    /// original overlay tier; neither the visitor's hover alpha nor depth changes.</summary>
    internal static void BindOffered(Transform root, Transform print)
    {
        if (root == null || print == null) return;
        if (Offered.TryGetValue(root, out OfferedOrder current) && current.Print == print
            && current.Canvas != null && current.Paper != null && current.Parent == root.parent
            && current.PrintParent == print.parent && current.Canvas.sortingLayerID == current.Paper.sortingLayerID)
        { ApplyOffered(current.Plate); return; }
        Canvas? canvas = root.GetComponentInParent<Canvas>(true);
        Canvas? paper = print.GetComponentInParent<Canvas>(true);
        if (canvas == null || paper == null || canvas == paper || canvas.sortingLayerID != paper.sortingLayerID)
        { UnbindOffered(root); return; }
        if (current != null && current.Canvas != canvas) { UnbindOffered(root); current = null!; }
        if (current == null)
        {
            if (Offered.Count >= MaxOfferedRoots)
            { RefreshOffered(); if (Offered.Count >= MaxOfferedRoots) return; }
            if (!OfferedPlates.TryGetValue(canvas, out OfferedPlate plate))
            {
                plate = new OfferedPlate { Canvas = canvas, PreviousOrder = canvas.sortingOrder };
                OfferedPlates.Add(canvas, plate);
            }
            current = new OfferedOrder { Anchor = Anchors.GetValue(root, item => new Anchor(item)),
                Root = root, Canvas = canvas, Plate = plate, Identity = root.GetInstanceID() };
            if (Offered.Count == 0) Canvas.willRenderCanvases += RefreshOffered;
            Offered.Add(root, current); plate.Roots.Add(current);
        }
        int depth = 0;
        for (Transform? node = root; node != null && node != canvas.transform; node = node.parent) depth++;
        current.Depth = depth;
        current.Print = print; current.Paper = paper; current.Parent = root.parent; current.PrintParent = print.parent;
        current.Anchor.Print = print;
        SelectPlateOwner(current.Plate); ApplyOffered(current.Plate);
    }

    internal static void UnbindOffered(Transform root)
    {
        if (!Offered.TryGetValue(root, out OfferedOrder order)) return;
        Offered.Remove(root); order.Anchor.Print = null;
        OfferedPlate plate = order.Plate; plate.Roots.Remove(order);
        CanvasConversion.ReassertFurniture(order.Anchor);
        if (plate.Roots.Count == 0)
        {
            OfferedPlates.Remove(plate.Canvas);
            if (plate.Canvas != null) plate.Canvas.sortingOrder = plate.PreviousOrder;
        }
        else { SelectPlateOwner(plate); ApplyOffered(plate); }
        if (Offered.Count == 0) Canvas.willRenderCanvases -= RefreshOffered;
    }

    internal static void ClearOffered()
    {
        // A network/room reset can retain live pooled originals without another
        // render. Release the callback and every exact print reference now.
        Retired.Clear(); Retired.AddRange(Offered.Keys);
        foreach (Transform root in Retired) UnbindOffered(root);
        Retired.Clear();
    }

    private static void SelectPlateOwner(OfferedPlate plate)
    {
        // A canvas has one draw order even when its native branches have separate
        // records. Its nearest registered native root owns that plate. A delayed
        // descendant relation cannot steal it during a card replacement; stable
        // instance identity breaks sibling ties rather than packet/dictionary order.
        OfferedOrder owner = plate.Roots[0];
        foreach (OfferedOrder candidate in plate.Roots)
            if (candidate.Depth < owner.Depth || candidate.Depth == owner.Depth && candidate.Identity < owner.Identity)
                owner = candidate;
        plate.Owner = owner;
    }

    private static void RefreshOffered()
    {
        // Original samples and the ordinary furniture ladder can write after the
        // numeric pass. Reassert only these exact active relations at submission.
        Retired.Clear();
        foreach (var pair in Offered)
        {
            OfferedOrder order = pair.Value;
            if (pair.Key == null || order.Print == null || order.Canvas == null || order.Paper == null
                || order.Canvas.sortingLayerID != order.Paper.sortingLayerID
                || !pair.Key.gameObject.activeInHierarchy || !order.Print.gameObject.activeInHierarchy)
                Retired.Add(pair.Key!);
        }
        foreach (Transform root in Retired) UnbindOffered(root);
        Retired.Clear();
        foreach (OfferedPlate plate in OfferedPlates.Values) ApplyOffered(plate);
    }

    private static void ApplyOffered(OfferedPlate plate)
    {
        Canvas paper = plate.Owner.Paper;
        if (paper == null || plate.Canvas == null || paper.sortingLayerID != plate.Canvas.sortingLayerID) return;
        // The actual serialized highlighter has no nested canvas: its aura and
        // ability rows inherit one native tier above the print. Native sibling
        // order and nested canvases retain their original authored properties.
        int want = paper.sortingOrder + 1;
        if (plate.Canvas.sortingOrder != want) plate.Canvas.sortingOrder = want;
    }

    internal static void Refresh(Transform root)
    {
        if (root != null && Anchors.TryGetValue(root, out Anchor anchor))
            CanvasConversion.ReassertFurniture(anchor);
    }

    internal static void Bind(Transform root)
    {
        if (root == null) return;
        Anchor anchor = Anchors.GetValue(root, item => new Anchor(item));
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            CanvasConversion.RegisterFurniture(anchor, renderer, 0);
        foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            // The native hierarchy retains its internal relative paint order; a root canvas
            // represents that complete plate against independent converted windows.
            if (canvas.isRootCanvas || canvas.overrideSorting)
                CanvasConversion.RegisterFurniture(anchor, canvas, 1);
        }
    }
}
