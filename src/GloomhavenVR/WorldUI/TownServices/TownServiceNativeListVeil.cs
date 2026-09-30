using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Keeps mod-owned gates attached but disabled between visits. Unity destroys
/// components at frame end, so destroying and adding them on a same-frame reopen could
/// otherwise select a component that is already scheduled for destruction.</summary>
internal sealed class TownServiceNativeListVeilParts : MonoBehaviour
{
    internal CanvasGroup? Group;
    internal LayoutElement? Layout;
}

/// <summary>Hides the original enhancement chooser while retaining its live card pool and
/// callbacks. Unlike a window mask, this never reparents hundreds of native card widgets.
/// The immersive palm uses those same widgets as its gameplay data source.</summary>
internal sealed class TownServiceNativeListVeil : IDisposable
{
    private readonly RectTransform _source;
    private readonly TownServiceNativeListVeilParts _parts;
    private readonly CanvasGroup _group;
    private readonly bool _addedGroup, _groupEnabled, _blocksRaycasts, _ignoreParentGroups;
    private float _restoreAlpha;
    private readonly LayoutElement _layout;
    private readonly bool _addedLayout, _ignoreLayout, _layoutEnabled;
    private readonly List<(LayoutElement Element, bool Ignore, bool Enabled)> _otherLayouts = new();
    private readonly List<CanvasGroup> _independent = new();
    private readonly HashSet<CanvasGroup> _knownGroups = new();
    private readonly UIPartyCharacterEnhancementAbilityCardsDisplay? _display;
    private int _slotCount;
    private bool _disposed, _renderHooked;

    internal TownServiceNativeListVeil(RectTransform source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        TownServiceNativeListVeilParts? parts = source.GetComponent<TownServiceNativeListVeilParts>();
        _parts = parts == null ? source.gameObject.AddComponent<TownServiceNativeListVeilParts>() : parts;
        // CanvasGroup is unique on a Unity object. If the prefab already owns one,
        // snapshot its presentation properties and restore them on every exit path.
        CanvasGroup? group = _parts.Group != null ? _parts.Group : source.GetComponent<CanvasGroup>();
        _addedGroup = _parts.Group != null || group == null;
        // Unity's missing-component wrapper can be a non-null managed reference.
        // The C# ?? operator then keeps it even though Unity's == null reports missing.
        _group = group == null ? source.gameObject.AddComponent<CanvasGroup>() : group;
        if (_addedGroup) _parts.Group = _group;
        _restoreAlpha = _group.alpha; _groupEnabled = _group.enabled;
        _blocksRaycasts = _group.blocksRaycasts; _ignoreParentGroups = _group.ignoreParentGroups;
        // LayoutGroup counts a child if ANY ILayoutIgnorer returns false. An added
        // second LayoutElement would therefore not suppress an existing native one.
        LayoutElement[] layouts = source.GetComponents<LayoutElement>();
        _addedLayout = _parts.Layout != null || layouts.Length == 0;
        _layout = _parts.Layout != null ? _parts.Layout
            : _addedLayout ? source.gameObject.AddComponent<LayoutElement>() : layouts[0];
        if (_addedLayout) _parts.Layout = _layout;
        _ignoreLayout = _layout.ignoreLayout; _layoutEnabled = _layout.enabled;
        foreach (LayoutElement element in layouts)
            if (element != _layout)
                _otherLayouts.Add((element, element.ignoreLayout, element.enabled));
        _display = source.GetComponent<UIPartyCharacterEnhancementAbilityCardsDisplay>();
        _slotCount = _display?.slotsPool.Count ?? -1;
        try
        {
            // Child canvases can opt out of ancestor opacity. Preserve their own authored
            // values, but make this one invisible parent authoritative for the visit.
            CaptureIndependentGroups();
            Reassert();
            Canvas.willRenderCanvases += ReassertVisual;
            _renderHooked = true;
        }
        catch { Dispose(); throw; }
    }

    internal void Reassert()
    {
        if (_disposed || _source == null || _group == null || _layout == null) return;
        // Display and native card animations continue to run for ownership, enhancement
        // availability, and direct callbacks. Only their unused flat presentation is gated.
        ReassertVisual();
        // Selectable.IsInteractable() on the hidden original slots is queried by the
        // physical handoff before it invokes native Select(). Never turn interactivity
        // off; block only pointer raycasts to the unused flat list.
        if (!_layout.enabled) _layout.enabled = true;
        if (!_layout.ignoreLayout) _layout.ignoreLayout = true;
        foreach (var (element, _, _) in _otherLayouts)
            if (element != null && !element.ignoreLayout) element.ignoreLayout = true;
        // Native Display may grow the pooled list on a later character change. Only
        // that edge merits a descendant scan; never traverse the card pool each frame.
        int slots = _display?.slotsPool.Count ?? -1;
        if (slots != _slotCount) { _slotCount = slots; CaptureIndependentGroups(); }
    }

    private void ReassertVisual()
    {
        if (_disposed || _group == null) return;
        // A native GUIAnimator can write this existing group after the ordinary
        // presentation tick. The pre-render callback closes that one-frame gap.
        if (!_addedGroup && _group.alpha > 0f) _restoreAlpha = _group.alpha;
        if (!_group.enabled) _group.enabled = true;
        if (_group.alpha != 0f) _group.alpha = 0f;
        if (_group.blocksRaycasts) _group.blocksRaycasts = false;
        // The old mask detached the list from the party canvas. Preserve the same
        // Selectable availability independent of the parent panel's temporary gate.
        if (!_group.ignoreParentGroups) _group.ignoreParentGroups = true;
    }

    private void CaptureIndependentGroups()
    {
        foreach (CanvasGroup child in _source.GetComponentsInChildren<CanvasGroup>(true))
            if (child != _group && child.ignoreParentGroups && _knownGroups.Add(child))
            { _independent.Add(child); child.ignoreParentGroups = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_renderHooked) { Canvas.willRenderCanvases -= ReassertVisual; _renderHooked = false; }
        foreach (CanvasGroup child in _independent)
            if (child != null) child.ignoreParentGroups = true;
        _independent.Clear();
        _knownGroups.Clear();
        if (_layout != null)
        {
            if (_addedLayout)
            {
                _layout.ignoreLayout = false;
                _layout.enabled = false;
            }
            else { _layout.ignoreLayout = _ignoreLayout; _layout.enabled = _layoutEnabled; }
        }
        foreach (var (element, ignore, enabled) in _otherLayouts)
            if (element != null) { element.ignoreLayout = ignore; element.enabled = enabled; }
        if (_group != null)
        {
            if (!_addedGroup && _group.alpha > 0f) _restoreAlpha = _group.alpha;
            // Destroy is deferred. Undo the veil now so a same-frame fallback can
            // present its original list without a frame of invisible controls.
            _group.alpha = _addedGroup ? 1f : _restoreAlpha;
            _group.blocksRaycasts = _addedGroup || _blocksRaycasts;
            _group.ignoreParentGroups = _addedGroup ? false : _ignoreParentGroups;
            _group.enabled = _addedGroup ? false : _groupEnabled;
        }
    }
}
