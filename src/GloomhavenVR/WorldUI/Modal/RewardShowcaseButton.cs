using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Explicit Continue affordance for the native Guildmaster showcase, which otherwise accepts
/// only a desktop mouse/keyboard event. This button never changes rewards or closes a window;
/// the native input handler and coroutine retain those responsibilities on every client.
/// </summary>
internal sealed class RewardShowcaseButton
{
    private UIRewardsManager? _owner;
    private RectTransform? _root;
    private Button? _button;
    private TextMeshProUGUI? _label;
    private Image? _image;
    private readonly List<Graphic> _graphics = new(16);
    private readonly Vector3[] _corners = new Vector3[4];

    internal void Tick(UIRewardsManager owner, bool canConfirm)
    {
        if (_owner != owner || _root == null) Build(owner);
        if (_root == null || _button == null || _label == null) return;
        // The same native window exists on every peer. Authority disables only its control;
        // reward artwork and the affordance keep the same placement for all participants.
        _button.interactable = canConfirm;
        _label.text = Loc.RewardContinue;
        ApplySkin();
        Place(owner);
    }

    private void Build(UIRewardsManager owner)
    {
        Dispose();
        if (owner.myWindow == null || owner.myWindow.transform is not RectTransform target) return;
        _owner = owner;
        // Belongs to the actual source hierarchy, so normal conversion, shared placement,
        // raycasting and teardown cover it. Its visible pixels participate in content fitting.
        var go = new GameObject("RewardContinue", typeof(RectTransform)) { layer = target.gameObject.layer };
        _root = (RectTransform)go.transform;
        _root.SetParent(target, false);
        _root.anchorMin = _root.anchorMax = target.pivot;
        _root.pivot = new Vector2(.5f, .5f);
        _root.sizeDelta = new Vector2(320f, 64f);
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        _image = go.AddComponent<Image>();
        ApplySkin();
        _image.type = Image.Type.Sliced;
        _image.color = new Color(.33f, .30f, .24f, 1f);
        _image.raycastTarget = true;
        _button = go.AddComponent<Button>();
        _button.targetGraphic = _image;
        _button.onClick.AddListener(Continue);
        var label = new GameObject("Label", typeof(RectTransform)) { layer = go.layer };
        label.transform.SetParent(_root, false);
        var rect = (RectTransform)label.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12f, 4f); rect.offsetMax = new Vector2(-12f, -4f);
        _label = label.AddComponent<TextMeshProUGUI>();
        _label.text = Loc.RewardContinue;
        _label.fontSize = 28f;
        _label.alignment = TextAlignmentOptions.Center;
        _label.color = Color.white;
        _label.enableWordWrapping = false;
        _label.raycastTarget = false;
        WorldUIAssets.TryAssignGameFont(_label);
        Place(owner);
    }

    private void ApplySkin()
    {
        if (_image == null) return;
        if (_image.sprite == null)
            _image.sprite = NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Idle);
        if (_image.sprite != null)
        {
            // NativeButtonSkin rebases its sprites for metre-sized SpriteRenderers. This is
            // uGUI in native pixel units: cancel that PPU rebasing so the border stays legible.
            float reference = _image.canvas != null ? _image.canvas.referencePixelsPerUnit : 100f;
            _image.pixelsPerUnitMultiplier = reference / Mathf.Max(1f, _image.sprite.pixelsPerUnit);
        }
    }

    private static void Continue() => RewardShowcase.TryConfirm();

    private void Place(UIRewardsManager owner)
    {
        if (_root == null || _root.parent is not RectTransform parent) return;
        _root.anchorMin = _root.anchorMax = parent.pivot;
        _graphics.Clear();
        if (owner.rewardHolder != null)
            owner.rewardHolder.GetComponentsInChildren(false, _graphics);
        if (owner.rewardAnnouncementText != null) _graphics.Add(owner.rewardAnnouncementText);
        if (owner.lineImage != null) _graphics.Add(owner.lineImage);
        bool any = false;
        float left = 0f, right = 0f, bottom = 0f;
        foreach (Graphic graphic in _graphics)
        {
            if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy
                || graphic.color.a <= .001f) continue;
            graphic.rectTransform.GetWorldCorners(_corners);
            foreach (Vector3 corner in _corners)
            {
                Vector3 point = parent.InverseTransformPoint(corner);
                if (!any) { left = right = point.x; bottom = point.y; any = true; }
                else { left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x); bottom = Mathf.Min(bottom, point.y); }
            }
        }
        // Measure native reward content only, never this button or the fitted host. The result
        // cannot feed back into its own height or drift downward on every conversion fit.
        _root.anchoredPosition3D = new Vector3(any ? (left + right) * .5f : 0f,
            (any ? bottom : 0f) - 56f, 0f);
    }

    internal void Dispose()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        _owner = null; _root = null; _button = null; _label = null; _image = null;
        _graphics.Clear();
    }
}
