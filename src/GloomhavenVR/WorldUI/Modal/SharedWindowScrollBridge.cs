using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal enum SharedScrollInput : byte { Wheel = 2, Position = 3, PointerDown = 5, PointerDrag = 6, PointerUp = 7 }

/// <summary>Input mailbox only. Private windows retain their original input path.</summary>
internal static class SharedWindowScrollBridge
{
    internal static Func<Component, SharedScrollInput, Vector2, int, bool>? Submit;
    internal static bool Wheel(Component target, Vector2 notches) => Submit?.Invoke(target, SharedScrollInput.Wheel, notches, 0) == true;
    internal static bool Position(Component target, Vector2 normalized) => Submit?.Invoke(target, SharedScrollInput.Position, normalized, 0) == true;
    internal static bool Pointer(Component target, SharedScrollInput input, PointerEventData data)
    {
        RectTransform? rect = target is SharedWindowScrollProxy proxy ? proxy.CoordinateRoot : target.transform as RectTransform;
        if (rect == null) return false;
        Camera? camera = data.pressEventCamera ?? data.enterEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, camera, out Vector2 local)) return false;
        Rect bounds = rect.rect;
        if (Mathf.Abs(bounds.width) < 1e-5f || Mathf.Abs(bounds.height) < 1e-5f) return false;
        return Submit?.Invoke(target, input, new Vector2((local.x - bounds.xMin) / bounds.width,
            (local.y - bounds.yMin) / bounds.height), data.pointerId) == true;
    }

    internal static Transform? PublicRoot(SharedWindowKind kind, out UIWindow? window)
    {
        window = SharedWindows.WindowOf(kind);
        if (window == null || !window.gameObject.activeInHierarchy || !(window.IsOpen || window.IsVisible)) return null;
        // A composed loadout is merely the shared story's physical host. Its card
        // lists and battle goals are private and must NEVER enter this registry.
        if (kind == SharedWindowKind.MapStory)
            return Singleton<MapStoryController>.IsInitialized
                ? Singleton<MapStoryController>.Instance.dialogBox?.transform : null;
        return window.transform;
    }

    internal static string Path(Transform root, Transform node)
    {
        if (root == node) return "/";
        var steps = new List<string>();
        for (Transform? current = node; current != root; current = current.parent)
        {
            if (current == null) throw new ArgumentException("Scroll target escaped its public root.");
            int occurrence = 0;
            for (int i = 0; i < current.GetSiblingIndex(); i++)
                if (current.parent.GetChild(i).name == current.name) occurrence++;
            steps.Add(Uri.EscapeDataString(current.name) + ":" + occurrence.ToString(CultureInfo.InvariantCulture));
        }
        steps.Reverse(); return "/" + string.Join("/", steps);
    }
    internal static Transform? Find(Transform root, string path)
    {
        Transform? current = root;
        foreach (string step in path.Split('/'))
        {
            if (step.Length == 0) continue;
            int colon = step.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(step.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture,
                out int occurrence) || occurrence < 0 || current == null) return null;
            string name = Uri.UnescapeDataString(step.Substring(0, colon)); Transform? next = null;
            for (int i = 0; i < current.childCount; i++)
            { Transform child = current.GetChild(i); if (child.name == name && occurrence-- == 0) { next = child; break; } }
            current = next;
        }
        return current;
    }
}

/// <summary>Metadata-only input on the EXACT visible inert results viewport.
/// No original controller/callback is copied onto a received picture.</summary>
internal sealed class SharedWindowScrollProxy : MonoBehaviour, IScrollHandler,
    IPointerDownHandler, IPointerUpHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    internal SharedWindowKind Kind;
    internal string Address = string.Empty;
    internal byte Control;
    internal bool CanScroll;
    internal RectTransform? CoordinateRoot;
    public void OnScroll(PointerEventData data) => SharedWindowScrollBridge.Wheel(this, data.scrollDelta);
    public void OnPointerDown(PointerEventData data) => SharedWindowScrollBridge.Pointer(this, SharedScrollInput.PointerDown, data);
    public void OnPointerUp(PointerEventData data) => SharedWindowScrollBridge.Pointer(this, SharedScrollInput.PointerUp, data);
    public void OnInitializePotentialDrag(PointerEventData data) { }
    public void OnBeginDrag(PointerEventData data) { }
    public void OnDrag(PointerEventData data) => SharedWindowScrollBridge.Pointer(this, SharedScrollInput.PointerDrag, data);
    public void OnEndDrag(PointerEventData data) { }
}
