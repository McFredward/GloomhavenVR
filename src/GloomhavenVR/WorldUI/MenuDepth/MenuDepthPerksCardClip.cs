using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Capture-only clip for native summary pool cells outside their original layout frame.</summary>
internal sealed class MenuDepthPerksCardClip
{
    private static readonly FieldInfo? Conditional = AccessTools.Field(typeof(UIPerksActiveModifiers), "conditionalPool");
    private static readonly FieldInfo? ConditionalContainer = AccessTools.Field(typeof(UIPerksActiveModifiers), "conditionalModifContainer");
    private static readonly FieldInfo? Additional = AccessTools.Field(typeof(UIPerksActiveModifiers), "additionalModifiersPool");
    private static readonly FieldInfo? AdditionalContainer = AccessTools.Field(typeof(UIPerksActiveModifiers), "additionalModifiersContainer");
    private readonly UIPerkAttackModifier _card;
    private readonly UIPerksActiveModifiers? _summary;
    private readonly UIPerksWindow? _perks;
    private readonly List<Graphic> _graphics = new(16);
    private readonly List<(CanvasRenderer Renderer, float Alpha)> _borrowed = new(16);
    private readonly Vector3[] _corners = new Vector3[4];
    private bool _graphicsDirty = true;

    internal MenuDepthPerksCardClip(UIPerkAttackModifier card)
    {
        _card = card;
        _summary = card.GetComponentInParent<UIPerksActiveModifiers>(true);
        _perks = _summary != null ? _summary.GetComponentInParent<UIPerksWindow>(true) : null;
    }

    internal void InvalidateGraphics() => _graphicsDirty = true;

    internal bool Outside()
    {
        if (_card == null || _summary == null || _perks == null || !_perks.IsVisible
            || !_card.transform.IsChildOf(_summary.transform)) return false;
        RectTransform? frame = Frame(Conditional, ConditionalContainer) ?? Frame(Additional, AdditionalContainer);
        if (frame == null || !frame.gameObject.activeInHierarchy || _card.transform is not RectTransform rect
            || !rect.IsChildOf(frame) || frame.rect.width <= 0f || frame.rect.height <= 0f) return false;
        rect.GetWorldCorners(_corners);
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity), max = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < _corners.Length; i++)
        {
            Vector3 point = frame.InverseTransformPoint(_corners[i]);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        // Retain intersecting cells, including legitimate animated/partly visible
        // summaries. A live shared hover is not a member of either summary pool.
        return !frame.rect.Overlaps(Rect.MinMaxRect(min.x, min.y, max.x, max.y));
    }

    private RectTransform? Frame(FieldInfo? poolField, FieldInfo? frameField)
        => poolField?.GetValue(_summary) is List<UIPerkAttackModifier> pool && pool.Contains(_card)
            ? frameField?.GetValue(_summary) as RectTransform : null;

    internal void BeforeCapture()
    {
        Restore();
        if (!Outside()) return;
        if (_graphicsDirty)
        {
            _graphics.Clear(); _card.GetComponentsInChildren(true, _graphics); _graphicsDirty = false;
        }
        // Never SetActive, edit native colours/CanvasGroups, or recycle a native
        // pool cell. The existing capture finally restores each renderer exactly;
        // layout can move it back into its valid frame on the very next draw.
        for (int i = 0; i < _graphics.Count; i++)
        {
            Graphic graphic = _graphics[i];
            if (graphic == null) continue;
            CanvasRenderer renderer = graphic.canvasRenderer;
            _borrowed.Add((renderer, renderer.GetAlpha())); renderer.SetAlpha(0f);
        }
    }

    internal void Restore()
    {
        for (int i = 0; i < _borrowed.Count; i++)
        {
            var item = _borrowed[i];
            if (item.Renderer != null) item.Renderer.SetAlpha(item.Alpha);
        }
        _borrowed.Clear();
    }
}
