using System;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The original game's complete Continue widget, with only its input connected to the native
/// level-up ClickTracker. UILevelUpWindow itself has a transparent click-anywhere tracker, not
/// a visible Button. The727 review rejects our constructed Image/TMP imitation: retain the
/// real reward Continue's authored hierarchy, font, geometry and native Selectable transition.
/// </summary>
internal sealed class AnnouncementContinueView
{
    private UILevelUpWindow? _owner;
    private RectTransform? _root;
    private ExtendedButton? _button;
    private Func<bool>? _continue;
    private readonly Vector3[] _corners = new Vector3[4];
    private float _retryAt;
    private bool _missingReported;

    internal void Tick(UILevelUpWindow owner, bool canConfirm, Func<bool> onContinue)
    {
        if (_owner != owner || (_root == null && Time.unscaledTime >= _retryAt)) Build(owner);
        if (_root == null || _button == null) return;
        _continue = onContinue;
        _button.interactable = canConfirm;
        // Follow the original card's pose, never our own fitted bounds or the inventory.
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
            _root.anchoredPosition3D = new Vector3((left + right) * .5f, bottom - _root.rect.height * .5f - 20f, 0f);
        }
    }

    private void Build(UILevelUpWindow owner)
    {
        Dispose();
        _owner = owner;
        _retryAt = Time.unscaledTime + 1f;
        if (owner.myWindow == null || owner.myWindow.transform is not RectTransform parent) return;
        ExtendedButton? original = Singleton<CampaignRewardsManager>.IsInitialized
            ? Singleton<CampaignRewardsManager>.Instance?.rewardsWindow?.continueButton : null;
        if (original == null)
        {
            // Exact typed fallback, once per build attempt, including the native hidden pool.
            foreach (UICampaignRewardWindow rewards in Object.FindObjectsOfType<UICampaignRewardWindow>(true))
                if (rewards != null && rewards.continueButton != null
                    && rewards.gameObject.scene == owner.gameObject.scene)
                { original = rewards.continueButton; break; }
        }
        if (original == null)
        {
            if (!_missingReported)
            {
                _missingReported = true;
                VRLog.Warn("WorldUI", "LEVEL-UP NATIVE CONTINUE: original reward widget is not ready; retrying without constructing replacement artwork.");
            }
            return;
        }
        var staging = new GameObject("GloomhavenVR.NativeContinueConstruction");
        staging.SetActive(false);
        GameObject? copy = null;
        try
        {
            // Inactive construction prevents cloned input controllers from ever enabling.
            copy = Object.Instantiate(original.gameObject, staging.transform, false);
            copy.SetActive(false);
            foreach (MonoBehaviour component in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component is ExtendedButton || component is Graphic || component is BaseMeshEffect
                    || component is LayoutGroup || component is ContentSizeFitter || component is LayoutElement
                    || component is AspectRatioFitter || component is Mask || component is RectMask2D) continue;
                Object.DestroyImmediate(component);
            }
            _button = copy.GetComponent<ExtendedButton>();
            _root = copy.transform as RectTransform;
            if (_button == null || _root == null) throw new InvalidOperationException("Native Continue widget has no ExtendedButton/RectTransform.");
            // Native onClick can contain serialized callbacks which RemoveAllListeners does
            // not remove. The view never invokes reward callbacks or controller navigation.
            _button.onClick = new Button.ButtonClickedEvent();
            _button.onMouseEnter = new UnityEvent(); _button.onMouseExit = new UnityEvent();
            _button.onSelected = new UnityEvent(); _button.onDeselected = new UnityEvent();
            // The native ExtendedButton imports a reward/ESC interactability check
            // which ClickTrackerExtended.ProcessClick never had. Keep its complete
            // visual behavior, while a narrow relay uses only the original tracker
            // permission through the existing TryConfirm callback.
            copy.AddComponent<AnnouncementNativeContinuePointer>().Press = Continue;
            _button.TextLanguageKey = "GUI_CONTINUE"; // native OnEnable subscribes language changes
            _root.SetParent(parent, false);
            _root.anchorMin = _root.anchorMax = parent.pivot;
            _root.pivot = new Vector2(.5f, .5f);
            _root.localScale = Vector3.one; _root.localRotation = Quaternion.identity;
            // Native prefab is307x65: do not substitute an invented footprint, font or tint.
            _root.sizeDelta = (original.transform as RectTransform)!.rect.size;
            (copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>()).ignoreLayout = true;
            foreach (Transform node in copy.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = parent.gameObject.layer;
            copy.SetActive(true);
            copy = null;
        }
        finally
        {
            if (copy != null) Object.Destroy(copy);
            Object.Destroy(staging);
        }
    }

    private void Continue()
    {
        _continue?.Invoke();
        if (_button != null) _button.interactable = AnnouncementContinue.CanConfirm;
    }

    internal void Dispose()
    {
        _continue = null;
        if (_root != null) Object.Destroy(_root.gameObject);
        _root = null; _button = null; _owner = null;
    }
}

internal sealed class AnnouncementNativeContinuePointer : MonoBehaviour, IPointerClickHandler, ISubmitHandler
{
    internal Action? Press;
    public void OnPointerClick(PointerEventData eventData)
    { if (eventData.button == PointerEventData.InputButton.Left) Press?.Invoke(); }
    public void OnSubmit(BaseEventData eventData) => Press?.Invoke();
}
