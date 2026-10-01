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
        internal Anchor(Transform root) => _root = root;
        public string FurnitureOrderName => "town presentation";
        public bool FurnitureOrderAlive => _root != null;
        public float FurnitureEyeDistance(Vector3 eye)
        {
            if (_root is RectTransform rect)
            {
                Vector3 point = rect.InverseTransformPoint(eye);
                Rect area = rect.rect;
                return Vector3.Distance(eye, rect.TransformPoint(new Vector3(
                    Mathf.Clamp(point.x, area.xMin, area.xMax), Mathf.Clamp(point.y, area.yMin, area.yMax), 0f)));
            }
            return Vector3.Distance(eye, _root.position);
        }
    }
    private static readonly ConditionalWeakTable<Transform, Anchor> Anchors = new();

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
