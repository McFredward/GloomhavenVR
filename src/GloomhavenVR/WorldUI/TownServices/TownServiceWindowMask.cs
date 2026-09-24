using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Suppress the old service illustration without disabling its controller or changing
/// its own CanvasGroup. The wrapper has the same parent frame, so native layout keeps running.</summary>
internal sealed class TownServiceWindowMask : IDisposable
{
    private readonly RectTransform _source, _wrapper;
    private readonly Transform? _parent;
    private readonly int _sibling;
    private bool _disposed;
    private readonly List<CanvasGroup> _independentGroups = new();

    internal TownServiceWindowMask(RectTransform source)
    {
        _source = source; _parent = source.parent; _sibling = source.GetSiblingIndex();
        var host = new GameObject("GloomhavenVR.TownService.NativeWindowMask", typeof(RectTransform));
        _wrapper = (RectTransform)host.transform;
        _wrapper.SetParent(_parent, false);
        _wrapper.SetSiblingIndex(_sibling);
        if (_parent is RectTransform parentRect) _wrapper.pivot = parentRect.pivot;
        _wrapper.anchorMin = Vector2.zero; _wrapper.anchorMax = Vector2.one;
        _wrapper.offsetMin = _wrapper.offsetMax = Vector2.zero;
        if (_parent is not RectTransform)
            _wrapper.sizeDelta = source.rect.size;
        // Unity permits only one CanvasGroup per object; owning a wrapper also avoids a
        // same-frame close/reopen finding a component scheduled for deferred destruction.
        CanvasGroup mask = host.AddComponent<CanvasGroup>();
        mask.alpha = 0f; mask.blocksRaycasts = false;
        // Nested confirmation canvases can ignore parent groups. Their native alpha remains
        // authoritative, but they must not escape the presentation mask around this old screen.
        foreach (CanvasGroup group in source.GetComponentsInChildren<CanvasGroup>(true))
            if (group.ignoreParentGroups) { _independentGroups.Add(group); group.ignoreParentGroups = false; }
        ReparentPreservingLocalRect(source, _wrapper);
    }

    // A native list may belong to another converted panel. Detaching its hidden wrapper
    // removes its layout footprint without disabling its gameplay controller or original pool.
    internal void DetachFromPanel(Transform frame)
    {
        Vector2 size = _wrapper.rect.size;
        _wrapper.SetParent(frame, false);
        _wrapper.anchorMin = _wrapper.anchorMax = new Vector2(.5f, .5f);
        _wrapper.sizeDelta = size;
    }

    private static void ReparentPreservingLocalRect(RectTransform source, Transform? parent)
    {
        Vector2 min = source.anchorMin, max = source.anchorMax, pivot = source.pivot, size = source.sizeDelta;
        Vector3 position = source.anchoredPosition3D, scale = source.localScale;
        Quaternion rotation = source.localRotation;
        source.SetParent(parent, false);
        source.anchorMin = min; source.anchorMax = max; source.pivot = pivot; source.sizeDelta = size;
        source.anchoredPosition3D = position; source.localRotation = rotation; source.localScale = scale;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (CanvasGroup group in _independentGroups)
            if (group != null) group.ignoreParentGroups = true;
        _independentGroups.Clear();
        if (_source != null && _source.parent == _wrapper)
        {
            ReparentPreservingLocalRect(_source, _parent);
            _source.SetSiblingIndex(_sibling);
        }
        if (_wrapper != null) UnityEngine.Object.Destroy(_wrapper.gameObject);
    }
}
