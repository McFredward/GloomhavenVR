using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Suppress the old service illustration without disabling its controller or changing
/// its own CanvasGroup. The wrapper has the same parent frame, so native layout keeps running.</summary>
internal sealed class TownServiceWindowMask : IDisposable
{
    // A pooled confirmation can report closed before uGUI has serviced its renderers. Returning
    // it to a converted ancestor in that tick produced the build-567 full-window sell flash and a
    // 232 ms CanvasConversion fit/capture rebuild. Keep the zero-alpha wrapper authoritative for
    // two complete render opportunities, while the controller callback continues normally.
    private const int ConfirmationRetirementTicks = 3;
    private static readonly List<TownServiceWindowMask> Retiring = new();
    private readonly RectTransform _source, _wrapper;
    private readonly UIWindow? _window;
    private readonly Transform? _parent;
    private readonly int _sibling;
    private bool _disposed, _retiring;
    private int _retirementTicks;
    private readonly List<CanvasGroup> _independentGroups = new();

    internal TownServiceWindowMask(RectTransform source)
    {
        _source = source; _parent = source.parent; _sibling = source.GetSiblingIndex();
        _window = source.GetComponent<UIWindow>();
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
        if (!_retiring && IsConfirmationWindow())
        {
            _retiring = true;
            _retirementTicks = ConfirmationRetirementTicks;
            Retiring.Add(this);
            return;
        }
        DisposeNow();
    }

    /// <summary>True while a closed physical confirmation still owns its invisible native root.
    /// The generic modal path must neither adopt nor fit that backing during this interval.</summary>
    internal static bool OwnsRetiring(UIWindow window)
    {
        for (int i = 0; i < Retiring.Count; i++)
            if (ReferenceEquals(Retiring[i]._window, window)) return true;
        return false;
    }

    /// <summary>Advance from the ordinary presentation tick. An actually reopened pooled prompt
    /// wins immediately; otherwise two complete masked frames let uGUI settle before rollback.</summary>
    internal static void TickRetirements()
    {
        for (int i = Retiring.Count - 1; i >= 0; i--)
        {
            TownServiceWindowMask mask = Retiring[i];
            if (mask._source == null || mask._wrapper == null)
            {
                Retiring.RemoveAt(i);
                mask.DisposeNow();
                continue;
            }
            if (mask._window != null && mask._window.IsOpen)
            {
                Retiring.RemoveAt(i);
                mask.DisposeNow();
                continue;
            }
            if (--mask._retirementTicks > 0 || mask._window != null && mask._window.IsVisible)
                continue;
            Retiring.RemoveAt(i);
            mask.DisposeNow();
        }
    }

    private bool IsConfirmationWindow() => _window != null
        && (_source.GetComponent<UIItemConfirmationBox>() != null
            || _source.GetComponent<UIEnhancementConfirmationBox>() != null);

    private void DisposeNow()
    {
        if (_disposed) return;
        _disposed = true;
        if (_retiring)
        {
            _retiring = false;
            Retiring.Remove(this);
        }
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
