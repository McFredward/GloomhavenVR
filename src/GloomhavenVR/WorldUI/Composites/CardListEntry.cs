using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Keep the game's original closed-list opener on its converted character window.</summary>
internal static class CardListEntry
{
    private static RectTransform? _button, _mount;
    private static Transform? _home;
    private static RectTransform? _view;
    private static ConvertedPanel? _panel;
    private static int _sibling;
    private static Vector2 _min, _max, _pivot, _size;
    private static Vector3 _position, _scale;
    private static Quaternion _rotation;
    private static TextMeshProUGUI? _caption;
    private static readonly MovedSubtreeLayers Layers = new(16);

    // False retains a refused native-home restore before the composite changes owners.
    internal static bool Tick()
    {
        NewPartyDisplayUI? party = NewPartyDisplayUI.PartyDisplay;
        UIResetLevelUpWindow? reset = Singleton<UIResetLevelUpWindow>.IsInitialized
            ? Singleton<UIResetLevelUpWindow>.Instance : null;
        RectTransform? view = party?.AbilityCardsDisplay?.transform as RectTransform;
        RectTransform? button = reset?.displayToggle?.transform as RectTransform;
        ConvertedPanel? panel = LevelUpComposite.FindContainingPanel(party?.window);
        bool available = VRSession.IsRunning && WorldUIConfig.ConversionActive
            && !FlatScreen.ManualScreenActive && VRModeStateMachine.TableInFrontOfPlayer
            && party != null && party.ActiveDisplay == NewPartyDisplayUI.DisplayType.CARDS
            && !party.IsDisableFurtherAbilityPanel && party.window != null && party.window.IsOpen
            && view != null && view.gameObject.activeInHierarchy && button != null
            && button.gameObject.activeSelf && reset != null && reset.requestDisableShow.Count == 0
            && panel != null && panel.IsAlive;
        if (!available) return Release();
        RectTransform nativeButton = button!;
        RectTransform nativeView = view!;
        if (!ReferenceEquals(button, _button) || !ReferenceEquals(view, _view)
            || !ReferenceEquals(panel, _panel))
        {
            if (!Release()) return false;
            _button = button; _view = view; _panel = panel;
            _home = nativeButton.parent; _sibling = nativeButton.GetSiblingIndex();
            _min = nativeButton.anchorMin; _max = nativeButton.anchorMax; _pivot = nativeButton.pivot;
            _size = nativeButton.sizeDelta; _position = nativeButton.anchoredPosition3D;
            _rotation = nativeButton.localRotation; _scale = nativeButton.localScale;
            // Native UIResetLevelUpWindow.Show activates this 43x65 arrow while
            // the inventory remains closed and slid outside the native desktop.
            // Waiting for inventory.IsOpen (the old composite) stranded its only
            // opener on the camera canvas outside the converted character window.
            // Carry this ORIGINAL toggle, never a replacement callback or action.
            _mount = (RectTransform)new GameObject("GloomhavenVR.CardListEntry",
                typeof(RectTransform), typeof(LayoutElement)).transform;
            _mount.GetComponent<LayoutElement>()!.ignoreLayout = true;
            _mount.SetParent(nativeView, false);
            _mount.anchorMin = _mount.anchorMax = _mount.pivot = Vector2.one;
            _mount.sizeDelta = nativeButton.rect.size;
            _mount.anchoredPosition3D = new Vector3(-16f, -16f, 0f);
            _mount.gameObject.layer = nativeView.gameObject.layer;
            nativeButton.SetParent(_mount, false);
            nativeButton.anchorMin = nativeButton.anchorMax = nativeButton.pivot = Vector2.one;
            nativeButton.sizeDelta = _mount.sizeDelta;
            nativeButton.anchoredPosition3D = Vector3.zero;
            nativeButton.localRotation = Quaternion.identity; nativeButton.localScale = Vector3.one;
            CreateCaption(nativeView);
            CanvasConversion.TransferSeatedSubtree(nativeButton, panel);
            Layers.Begin(nativeView.gameObject.layer); Layers.Walk(nativeButton);
        }
        return true;
    }

    private static void CreateCaption(RectTransform view)
    {
        TMP_Text? original = null;
        foreach (TMP_Text text in view.GetComponentsInChildren<TMP_Text>(true))
            if (text != null && text.font != null) { original = text; break; }
        if (original == null || _button == null) return;
        var go = new GameObject("GloomhavenVR.MoreCardsLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(_button, false);
        _caption = go.GetComponent<TextMeshProUGUI>()!;
        _caption.font = original.font; _caption.fontSharedMaterial = original.fontSharedMaterial;
        _caption.fontSize = original.fontSize; _caption.fontStyle = original.fontStyle;
        _caption.color = original.color; _caption.alignment = TextAlignmentOptions.MidlineRight;
        _caption.overflowMode = TextOverflowModes.Ellipsis;
        // The caption is a graphic CHILD of the native toggle. Native uGUI
        // dispatch walks to that same Toggle.OnPointerClick, retaining all native
        // interactability and onValueChanged listeners when the text is pointed at.
        _caption.raycastTarget = true;
        RectTransform rect = _caption.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f); rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(1f, .5f); rect.sizeDelta = new Vector2(200f, 0f);
        rect.anchoredPosition3D = new Vector3(-8f, 0f, 0f);
        RefreshCaption(); Loc.OnChanged += RefreshCaption;
    }

    private static void RefreshCaption()
    { if (_caption != null) _caption.text = Loc.MoreCards; }

    internal static bool Release()
    {
        if (_button != null)
        {
            _button.SetParent(_home, false);
            // Activation-time refusal must never destroy the original toggle.
            if (!ReferenceEquals(_button.parent, _home)) return false;
            CanvasConversion.TransferSeatedSubtree(_button, null);
            Layers.Restore();
            _button.anchorMin = _min; _button.anchorMax = _max; _button.pivot = _pivot;
            _button.sizeDelta = _size; _button.anchoredPosition3D = _position;
            _button.localRotation = _rotation; _button.localScale = _scale;
            _button.SetSiblingIndex(_sibling);
        }
        Loc.OnChanged -= RefreshCaption;
        if (_caption != null) Object.Destroy(_caption.gameObject);
        if (_mount != null) Object.Destroy(_mount.gameObject);
        _caption = null; _button = _mount = _view = null; _home = null; _panel = null;
        return true;
    }
}
