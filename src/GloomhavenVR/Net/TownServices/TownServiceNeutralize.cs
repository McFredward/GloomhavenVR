using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Only engine-owned rendering components survive. Original gameplay controllers,
/// custom Graphic callbacks, layout drivers, animations and input never run on an observer.</summary>
internal static class TownServiceNeutralize
{
    internal static void Apply(GameObject root)
    {
        if (root.activeInHierarchy) throw new InvalidOperationException("Town template must be inert before neutralization.");
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            Type type = graphic.GetType();
            if (type != typeof(Image) && type != typeof(RawImage) && type != typeof(Text)
                && type != typeof(TextMeshProUGUI))
                throw new InvalidDataException("Native custom graphic requires an explicit original mesh adapter: " + type.FullName);
            graphic.raycastTarget = false;
        }
        for (int pass = 0; pass < 4; pass++)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            bool removed = false;
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null || Keep(component.GetType())) continue;
                Object.DestroyImmediate(component); if (component == null) removed = true;
            }
            if (!removed) break;
        }
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
            if (component != null && !Keep(component.GetType()))
                throw new InvalidDataException("Cannot neutralize native town controller: " + component.GetType().FullName);
        foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
        { canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = null; }
        foreach (CanvasGroup group in root.GetComponentsInChildren<CanvasGroup>(true))
        { group.interactable = false; group.blocksRaycasts = false; }
    }
    private static bool Keep(Type type) => type == typeof(Transform) || type == typeof(RectTransform)
        || type == typeof(Canvas) || type == typeof(CanvasRenderer) || type == typeof(CanvasGroup)
        || type == typeof(Image) || type == typeof(RawImage) || type == typeof(Text) || type == typeof(TextMeshProUGUI)
        || type == typeof(Mask) || type == typeof(RectMask2D) || type == typeof(Shadow) || type == typeof(Outline)
        || type == typeof(MeshFilter) || type == typeof(MeshRenderer);
}
