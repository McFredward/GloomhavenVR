using System;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI;

/// <summary>A visible Continue beneath the original revealed card, inside its native hierarchy.</summary>
internal sealed class AnnouncementContinueView
{
    private UILevelUpWindow? _owner;
    private RectTransform? _root;
    private AnnouncementContinueButton? _button;
    private TextMeshProUGUI? _label;
    private Image? _image;
    private Func<bool>? _continue;
    private readonly Vector3[] _corners = new Vector3[4];

    internal void Tick(UILevelUpWindow owner, bool canConfirm, Func<bool> onContinue)
    {
        if (_owner != owner || _root == null) Build(owner);
        if (_root == null || _button == null || _label == null) return;
        _continue = onContinue;
        _button.interactable = canConfirm;
        _label.text = Loc.RewardContinue;
        _button.RefreshSkin();
        if (_image != null)
        {
            if (_image.sprite == null)
                _image.sprite = NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Idle);
            if (_image.sprite != null)
            {
                float reference = _image.canvas != null ? _image.canvas.referencePixelsPerUnit : 100f;
                _image.pixelsPerUnitMultiplier = reference / Mathf.Max(1f, _image.sprite.pixelsPerUnit);
            }
        }
        // Follow only the original highlighter, never our own fitted bounds or the inventory.
        // This remains stable as the native card animates, and is included in normal panel fitting.
        RectTransform? content = owner.cardHolder?.cardHolder?.Card?.fullAbilityCard?.transform as RectTransform
            ?? owner.cardHolder?.transform as RectTransform;
        if (_root.parent is RectTransform parent && content != null)
        {
            content.GetWorldCorners(_corners);
            float left = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.PositiveInfinity;
            foreach (Vector3 corner in _corners)
            {
                Vector3 point = parent.InverseTransformPoint(corner);
                left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x);
                bottom = Mathf.Min(bottom, point.y);
            }
            _root.anchoredPosition3D = new Vector3((left + right) * .5f, bottom - 52f, 0f);
        }
    }

    private void Build(UILevelUpWindow owner)
    {
        Dispose();
        if (owner.myWindow == null || owner.myWindow.transform is not RectTransform parent) return;
        _owner = owner;
        var go = new GameObject("LevelUpContinue", typeof(RectTransform)) { layer = parent.gameObject.layer };
        _root = (RectTransform)go.transform;
        _root.SetParent(parent, false);
        _root.anchorMin = _root.anchorMax = parent.pivot;
        _root.pivot = new Vector2(.5f, .5f);
        _root.sizeDelta = new Vector2(320f, 64f);
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        _image = go.AddComponent<Image>();
        _image.sprite = NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Idle);
        _image.type = Image.Type.Sliced;
        _image.raycastTarget = true;
        _button = go.AddComponent<AnnouncementContinueButton>();
        _button.transition = Selectable.Transition.None;
        _button.targetGraphic = _image;
        _button.onClick.AddListener(Continue);
        var text = new GameObject("Label", typeof(RectTransform)) { layer = go.layer };
        var rect = (RectTransform)text.transform;
        rect.SetParent(_root, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12f, 4f); rect.offsetMax = new Vector2(-12f, -4f);
        _label = text.AddComponent<TextMeshProUGUI>();
        _label.fontSize = 28f;
        _label.alignment = TextAlignmentOptions.Center;
        _label.enableWordWrapping = false;
        _label.color = Color.white;
        _label.raycastTarget = false;
        WorldUIAssets.TryAssignGameFont(_label);
    }

    private void Continue()
    {
        _continue?.Invoke();
        // Native readiness changes synchronously. Do not leave an enabled-looking button for
        // the rest of this frame after its reveal has begun moving away.
        if (_button != null) _button.interactable = AnnouncementContinue.CanConfirm;
    }

    internal void Dispose()
    {
        _continue = null;
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = null; _button = null; _label = null; _image = null; _owner = null;
    }
}

internal sealed class AnnouncementContinueButton : Button
{
    internal void RefreshSkin() => DoStateTransition(currentSelectionState, true);

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        if (targetGraphic is not Image image) return;
        NativeButtonSkin.FaceState face = state switch
        {
            SelectionState.Disabled => NativeButtonSkin.FaceState.Disabled,
            SelectionState.Pressed => NativeButtonSkin.FaceState.Pressed,
            SelectionState.Highlighted or SelectionState.Selected => NativeButtonSkin.FaceState.Accent,
            _ => NativeButtonSkin.FaceState.Idle,
        };
        var sprite = NativeButtonSkin.SpriteFor(face);
        if (sprite != null) image.sprite = sprite;
        image.color = NativeButtonSkin.ColorFor(face);
    }
}
