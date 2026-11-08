using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>One authored presentation frame for a nested character-creation opening.</summary>
internal sealed class CharacterCreationFrame
{
    private UICharacterCreatorWindow? _creator;
    private Transform? _view;
    private Rect _frame;
    private readonly List<RawImage> _previews = new(4);
    private readonly Vector3[] _corners = new Vector3[4];

    internal int Key => _creator != null ? _creator.GetInstanceID() : 0;
    internal bool Active => _creator != null;

    internal bool Update(ConvertedPanel panel, NewPartyDisplayUI display)
    {
        var selector = display.CharacterSelector as UICampaignAdventurePartyAssemblyWindow;
        var creator = selector?.CharacterCreator?.CreatorWindow;
        if (creator == null || !creator.IsShown || !creator.gameObject.activeInHierarchy
            || panel.Target == null || !creator.transform.IsChildOf(panel.Target)) creator = null;
        if (ReferenceEquals(creator, _creator)) return false;
        _creator = creator;
        _view = creator != null ? selector!.transform : null;
        _previews.Clear();
        if (_view == null) return true;

        // Build764 paired763: the selector stays shown while its nested creator
        // opens. Its visible union grows1627 ->2308px, but the old fit kept the
        // selector's frozen seat:303px over the party rail,637px out of frame,
        // then a gap when the native roster returned on Cancel. Capture the
        // creator's actual native rectangle and original RT preview apertures,
        // once per opening, independently of fading ink/animation/head pose.
        _frame = InView((RectTransform)creator!.transform);
        selector!.GetComponentsInChildren(true, _previews);
        for (int i = 0; i < _previews.Count; i++)
        {
            RawImage image = _previews[i];
            Rect rect = InView(image.rectTransform);
            _frame = Rect.MinMaxRect(Mathf.Min(_frame.xMin, rect.xMin), Mathf.Min(_frame.yMin, rect.yMin),
                Mathf.Max(_frame.xMax, rect.xMax), Mathf.Max(_frame.yMax, rect.yMax));
        }
        _previews.Clear();
        return true;
    }

    private Rect InView(RectTransform rectangle)
    {
        rectangle.GetWorldCorners(_corners);
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity), max = -min;
        for (int i = 0; i < _corners.Length; i++)
        {
            Vector2 point = _view!.InverseTransformPoint(_corners[i]);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    internal bool Measure(Transform view, RectTransform host, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.PositiveInfinity, float.PositiveInfinity); max = -min;
        if (_view == null || !ReferenceEquals(view, _view)) return false;
        Matrix4x4 frame = host.worldToLocalMatrix * view.localToWorldMatrix;
        for (int i = 0; i < 4; i++)
        {
            Vector2 point = frame.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? _frame.xMin : _frame.xMax,
                (i & 2) == 0 ? _frame.yMin : _frame.yMax));
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return true;
    }

    // The host is centred but its existing party-rail pin must stay put. Grow
    // only its transparent frame, never the native target, character metric,
    // accepted physical pose or user resize factor. No animated-ink feedback.
    internal float RequiredWidth(float seam, float padding)
        => Active ? 2f * Mathf.Max(0f, seam + _frame.width + padding) : 0f;
}
