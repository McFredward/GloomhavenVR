using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal enum SharedScrollInput : byte { Wheel = 2, Position = 3, PointerDown = 5, PointerDrag = 6, PointerUp = 7 }

/// <summary>Input mailbox only. Private windows retain their original input path.</summary>
internal static class SharedWindowScrollBridge
{
    internal static Func<Component, SharedScrollInput, Vector2, int, bool>? Submit;
    internal static Func<Transform, SharedWindowScrollProxy?>? ResultsInput;
    internal static void AttachResultsHeader(Transform picture, Canvas host, SharedWindowScrollCanvasLease lease, bool enabled)
    {
        if (!ScenarioResultsNative.TryRead(out var native) || native.Header == null
            || !native.Header.transform.IsChildOf(native.Window.transform)) return;
        Transform? header = Find(picture, Path(native.Window.transform, native.Header.transform));
        if (header != null) lease.AdmitHeader(host, header, enabled);
    }
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

/// <summary>Only canvases containing an own metadata input surface join the
/// existing nested pointer census. Original inert widgets remain non-raycastable.</summary>
internal sealed class SharedWindowScrollCanvasLease : MonoBehaviour
{
    private Canvas? _host;
    private readonly List<Canvas> _nested = new();
    private Transform? _header;
    private readonly List<SharedResultsHeaderGraphic> _headerInputs = new();
    internal void Admit(Canvas host, Canvas? canvas)
    {
        if (canvas == null || canvas == host || _nested.Contains(canvas)) return;
        if (_host != null && _host != host) Retire();
        _host = host;
        if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>();
        Hands.Interact.UguiPokeSurfaces.RegisterNested(host, canvas); _nested.Add(canvas);
    }
    internal void AdmitHeader(Canvas host, Transform header, bool enabled)
    {
        if (_header != header)
        {
            foreach (SharedResultsHeaderGraphic input in _headerInputs)
                if (input != null) UnityEngine.Object.Destroy(input.gameObject);
            _headerInputs.Clear(); _header = header;
            foreach (Graphic original in header.GetComponentsInChildren<Graphic>(true))
            {
                if (original is SharedResultsHeaderGraphic) continue;
                var go = new GameObject("GloomhavenVR.ResultsHeaderInput", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(CanvasGroup), typeof(SharedResultsHeaderGraphic));
                var rect = (RectTransform)go.transform; rect.SetParent(original.transform, false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero; go.layer = original.gameObject.layer;
                CanvasGroup group = go.GetComponent<CanvasGroup>();
                group.ignoreParentGroups = true; group.blocksRaycasts = group.interactable = true;
                SharedResultsHeaderGraphic input = go.GetComponent<SharedResultsHeaderGraphic>();
                input.color = Color.clear; input.raycastTarget = true; input.Original = original;
                _headerInputs.Add(input); Admit(host, input.GetComponentInParent<Canvas>(true));
            }
        }
        SetHeaderEnabled(enabled);
    }
    internal void SetHeaderEnabled(bool enabled)
    { foreach (SharedResultsHeaderGraphic input in _headerInputs) if (input != null) input.gameObject.SetActive(enabled); }
    private void Retire()
    {
        foreach (Canvas canvas in _nested) Hands.Interact.UguiPokeSurfaces.UnregisterNested(_host, canvas);
        _nested.Clear(); _host = null;
    }
    private void OnDestroy() => Retire();
}

/// <summary>The inert picture cannot expose its native header's callbacks or raycast
/// groups. Own hover-only ink follows each exact original graphic rectangle and its
/// masks/visibility. ModalFallback retains header stick delivery; this is never a
/// ScrollRect/proxy drag target or a transparent plate across empty window space.</summary>
internal sealed class SharedResultsHeaderGraphic : Image, IPointerDownHandler
{
    internal Graphic? Original;
    public override bool Raycast(Vector2 screen, Camera eventCamera)
    {
        Graphic? original = Original;
        if (original == null || !original.isActiveAndEnabled || original.canvasRenderer.cull
            || original.color.a * original.canvasRenderer.GetAlpha()
                * original.canvasRenderer.GetInheritedAlpha() < .01f) return false;
        if (original is TMP_Text text && string.IsNullOrWhiteSpace(text.text)
            || original is Text legacy && string.IsNullOrWhiteSpace(legacy.text)) return false;
        return base.Raycast(screen, eventCamera);
    }
    public void OnPointerDown(PointerEventData data) { }
}
