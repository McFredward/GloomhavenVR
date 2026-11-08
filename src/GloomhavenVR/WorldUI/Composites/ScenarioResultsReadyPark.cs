using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Seats the original results ready control inside its native results window.
/// Voting, host exit authority and the original clients' auto-ready remain game-owned.</summary>
internal static class ScenarioResultsReadyPark
{
    private static UIWindow? _host;
    private static RectTransform? _control;
    private static RectTransform? _seat;
    private static CanvasGroup? _seatGroup;
    private static ConvertedPanel? _capture;
    private static Transform? _home;
    private static UIReadyToggle? _ready;
    private static UIWindow? _readyWindow;
    private static OwnerLifetime? _lifetime;
    private static int _index;
    private static Vector2 _anchorMin, _anchorMax, _pivot, _size;
    private static Vector3 _position, _scale;
    private static Quaternion _rotation;
    private static LayoutElement? _layout;
    private static bool _addedLayout, _ignore;
    private static bool _restoring;
    private static readonly MovedSubtreeLayers Layers = new(32);
    private static readonly FooterFrame Footer = new();

    internal static bool Claims(GameObject go) => _control != null && _ready != null && _host != null
        && _host.IsOpen && _ready.IsVisible && _ready.readyUpToggleState == EReadyUpToggleStates.NotSet
        && _ready.ReadyUpType == UIReadyToggle.EReadyUpType.Player
        && _host.gameObject.activeInHierarchy && _seat != null
        && ReferenceEquals(_ready.gameObject, go) && _control.parent == _seat
        && ReferenceEquals(ScenarioResultsNative.Window, _host);

    internal static void Tick()
    {
        if (_restoring) return;
        if (!FFSNetwork.IsOnline || !VRSession.IsRunning || !WorldUIConfig.ConversionActive || FlatScreen.ManualScreenActive
            || !VRModeStateMachine.TableInFrontOfPlayer || !ScenarioResultsNative.TryRead(out var p))
        { Reset(); Footer.Reset(); return; }
        ScenarioResultsNative.RefreshReadyVisibility(p);
        if (!p.Window.IsOpen || !p.Window.gameObject.activeInHierarchy || p.Ready == null || !p.Ready.IsVisible
            || p.Window.transform is not RectTransform host || p.Ready.transform is not RectTransform control)
        { Reset(); Footer.Tick(p, null); return; }

        // A map/loadout/story composite already holding this pooled singleton has
        // first refusal. A results opening never steals another native waiter.
        if (MapRoom.ReadyToggleParkClaim.Claimed
            && ReferenceEquals(MapRoom.ReadyToggleParkClaim.ClaimedObject, control.gameObject))
        { Reset(); Footer.Reset(); return; }
        if (_control != control || _host != p.Window)
        {
            Reset();
            UIWindow? native = control.GetComponent<UIWindow>();
            if (native == null || !ModalFallback.ReleaseForComposite(native)) return;
            _host = p.Window; _ready = p.Ready; _control = control;
            _readyWindow = native;
            _home = control.parent; _index = control.GetSiblingIndex();
            _anchorMin = control.anchorMin; _anchorMax = control.anchorMax; _pivot = control.pivot;
            _size = control.sizeDelta; _position = control.anchoredPosition3D;
            _scale = control.localScale; _rotation = control.localRotation;
            _layout = control.GetComponent<LayoutElement>();
            _addedLayout = _layout == null;
            if (_layout == null) _layout = control.gameObject.AddComponent<LayoutElement>();
            _ignore = _layout.ignoreLayout; _layout.ignoreLayout = true;
            // A sibling dock shares the native owner's exact coordinate frame,
            // but survives its GameObject being hidden/destroyed. Unity forbids
            // reparenting native children from that parent's OnDisable callback.
            var dock = new GameObject("GloomhavenVR.ResultsReadyDock", typeof(RectTransform), typeof(CanvasGroup), typeof(LayoutElement));
            _seat = (RectTransform)dock.transform;
            _seatGroup = dock.GetComponent<CanvasGroup>();
            dock.GetComponent<LayoutElement>().ignoreLayout = true;
            MatchOwner(host);
            control.SetParent(_seat, false);
            Layers.Begin(host.gameObject.layer); Layers.Walk(control);
            _host.onHidden.AddListener(Reset);
            native.onHidden.AddListener(Reset);
            _lifetime = _host.gameObject.AddComponent<OwnerLifetime>();
            _lifetime.Owner = _host;
        }
        // A successor composite/native owner may have adopted the singleton since
        // the last tick. Release only our receipt; never pull it out of that owner.
        if (_seat == null || control.parent != _seat) { Reset(); return; }
        MatchOwner(host);
        control.anchorMin = control.anchorMax = new Vector2(.5f, 0f);
        control.pivot = new Vector2(.5f, .5f);
        control.anchoredPosition3D = new Vector3(0f, control.rect.height * .5f + 20f, 0f);
        control.localRotation = Quaternion.identity; control.localScale = Vector3.one;
        Footer.Tick(p, control);
        if (Layers.Written != host.gameObject.layer) Layers.Begin(host.gameObject.layer);
        Layers.Walk(control);
        if (ScenarioResultsNative.TryGetConverted(out ConvertedPanel? panel) && panel != null)
        {
            if (_capture != panel)
            {
                if (_capture != null) _capture.ExtraRenderRoots.Remove(_seat);
                _capture = panel;
            }
            if (!panel.ExtraRenderRoots.Contains(_seat)) panel.ExtraRenderRoots.Add(_seat);
            CanvasConversion.TransferSeatedSubtree(control, panel);
            if (panel.RenderHidden || panel.OwnerRenderHidden) SilenceOwner();
        }
    }

    private static void MatchOwner(RectTransform host)
    {
        if (_seat == null) return;
        if (ScenarioResultsNative.TryGetConverted(out ConvertedPanel? panel) && panel != null)
        {
            // Stay within the actually fitted visible frame, alongside (not
            // underneath) the native content. Both share the owner's canvas.
            if (_seat.parent != panel.HostRect) _seat.SetParent(panel.HostRect, false);
            _seat.anchorMin = Vector2.zero; _seat.anchorMax = Vector2.one;
            _seat.pivot = panel.HostRect.pivot; _seat.sizeDelta = Vector2.zero;
            _seat.anchoredPosition3D = Vector3.zero;
            _seat.localRotation = Quaternion.identity; _seat.localScale = Vector3.one;
        }
        else
        {
            if (_seat.parent != host.parent) _seat.SetParent(host.parent, false);
            _seat.anchorMin = host.anchorMin; _seat.anchorMax = host.anchorMax;
            _seat.pivot = host.pivot; _seat.sizeDelta = host.sizeDelta;
            _seat.anchoredPosition3D = host.anchoredPosition3D;
            _seat.localRotation = host.localRotation; _seat.localScale = host.localScale;
        }
        _seat.gameObject.layer = host.gameObject.layer;
        if (_seatGroup != null)
        {
            CanvasGroup? owner = host.GetComponent<CanvasGroup>();
            Canvas? canvas = host.GetComponent<Canvas>();
            _seatGroup.alpha = canvas != null && !canvas.isActiveAndEnabled ? 0f : owner != null ? owner.alpha : 1f;
            _seatGroup.interactable = owner == null || owner.interactable;
            _seatGroup.blocksRaycasts = owner == null || owner.blocksRaycasts;
        }
    }

    private static void SilenceOwner()
    {
        if (_seatGroup == null) return;
        _seatGroup.alpha = 0f; _seatGroup.blocksRaycasts = false; _seatGroup.interactable = false;
    }

    internal static void BeforeCommonPicture(ConvertedPanel panel) => Footer.Release(panel);

    internal static void BeforeExternalOwner(UIWindow window)
    {
        if (_control != null && ReferenceEquals(window.transform, _control)) Reset();
    }

    internal static void Reset()
    {
        if (_restoring) return;
        _restoring = true;
        RectTransform? control = _control;
        UIWindow? host = _host;
        if (host != null) host.onHidden.RemoveListener(Reset);
        if (_readyWindow != null) _readyWindow.onHidden.RemoveListener(Reset);
        if (_lifetime != null) { _lifetime.Owner = null; Object.Destroy(_lifetime); }
        _lifetime = null; _readyWindow = null;
        // SetParent may synchronously notify another native/converted owner.
        // Clear our identity before that edge so it cannot retire us recursively.
        _host = null; _ready = null; _control = null;
        try
        {
            if (control != null && _seat != null && control.parent == _seat)
            {
                CanvasConversion.TransferSeatedSubtree(control, null);
                control.SetParent(_home, false);
                if (_home != null) control.SetSiblingIndex(Mathf.Min(_index, _home.childCount - 1));
                control.anchorMin = _anchorMin; control.anchorMax = _anchorMax; control.pivot = _pivot;
                control.sizeDelta = _size; control.anchoredPosition3D = _position;
                control.localScale = _scale; control.localRotation = _rotation;
                if (_layout != null)
                {
                    _layout.ignoreLayout = _ignore;
                    if (_addedLayout) Object.Destroy(_layout);
                }
                Layers.Restore();
            }
            else Layers.Forget();
        }
        finally
        {
            if (_capture != null && _seat != null) _capture.ExtraRenderRoots.Remove(_seat);
            SilenceOwner();
            if (_seat != null) Object.Destroy(_seat.gameObject);
            _seat = null; _seatGroup = null; _capture = null;
            _home = null; _layout = null; _addedLayout = false; _restoring = false;
        }
    }

    /// <summary>Results-only geometry lease. The original viewport and native footer,
    /// rather than the changing visible-ink fit, define one stable capture frame.</summary>
    private sealed class FooterFrame
    {
        private ConvertedPanel? _panel;
        private UIWindow? _window;
        private Vector2 _frame;
        private Vector3 _position;
        private bool _fit;
        private RectTransform? _fitRoot;
        private Vector2 _writtenFrame;
        private Vector3 _writtenPosition;
        private readonly Vector3[] _corners = new Vector3[4];

        internal void Tick(ScenarioResultsParts parts, RectTransform? ready)
        {
            if (_window != null && (_window != parts.Window || !_window.IsOpen)) Reset();
            if (!parts.Window.IsOpen || !parts.Window.gameObject.activeInHierarchy)
            { Reset(); return; }
            if (!ScenarioResultsNative.TryGetConverted(out ConvertedPanel? panel) || panel == null
                || parts.Window.transform is not RectTransform root) return;
            foreach (Transform extra in panel.ExtraRenderRoots)
                if (extra != null && extra.name == "GloomhavenVR.Results.Observer") { Release(panel); return; }
            ScrollRect? scroll = root.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null || !scroll.gameObject.activeInHierarchy || scroll.viewport == null
                || ready == null && (parts.Buttons == null || !parts.Buttons.activeInHierarchy))
            { Reset(); return; }
            if (_panel != panel)
            {
                Reset(); _panel = panel; _window = parts.Window;
                _frame = panel.HostRect.rect.size; _position = root.anchoredPosition3D; _fit = panel.FitEnabled; _fitRoot = panel.FitContentRoot;
                _window.onHidden.AddListener(Reset);
            }

            // Build766: the measured765 viewport occupied y=-427..225 while the
            // fitted host ended at -439. Parking Ready at that host's bottom put
            // its entire60px face INSIDE the mask. Scrolling changed the ink fit
            // and alternately covered the control or the rows. Use the original
            // native viewport/header/button coordinates, which do not depend on
            // scroll position, private row counts, local zoom or the prior fit.
            Rect bounds = Bounds(root, scroll.viewport);
            if (scroll.verticalScrollbar != null) bounds = Union(bounds, Bounds(root, scroll.verticalScrollbar.transform as RectTransform));
            if (parts.Header != null) bounds = Union(bounds, Bounds(root, parts.Header.transform as RectTransform));
            Rect? buttons = parts.Buttons != null && parts.Buttons.transform is RectTransform nativeButtons
                ? Bounds(root, nativeButtons) : null;
            bool options = false;
            for (int i = 0; i < 2; i++)
            {
                UIResultsOption? option = i == 0 ? parts.Manager.returnToMapOption : parts.Manager.retryOption;
                if (option == null || !option.gameObject.activeInHierarchy || option.transform is not RectTransform rect) continue;
                options = true; bounds = Union(bounds, Bounds(root, rect));
            }
            Vector3 seat = Vector3.zero;
            if (ready != null)
            {
                Rect face = Bounds(root, ready);
                float height = face.height;
                float y = Mathf.Min(buttons?.center.y ?? bounds.yMin - 20f - height * .5f,
                    Bounds(root, scroll.viewport).yMin - 20f - height * .5f);
                if (options && buttons.HasValue) y = Mathf.Min(y, buttons.Value.yMin - 20f - height * .5f);
                seat = new Vector3(buttons?.center.x ?? 0f, y, 0f);
                ready.position = root.TransformPoint(seat);
                bounds = Union(bounds, Bounds(root, ready));
            }
            else if (buttons.HasValue)
            {
                // The row's native width stretches across the full desktop; only
                // its vertical reserve belongs to the footer. Actual original
                // option faces above supply their own horizontal extent.
                Rect row = buttons.Value;
                bounds = Union(bounds, new Rect(bounds.xMin, row.yMin, bounds.width, row.height));
            }
            bounds = Rect.MinMaxRect(bounds.xMin - 20f, bounds.yMin - 20f, bounds.xMax + 20f, bounds.yMax + 20f);
            Vector3 low = panel.HostRect.InverseTransformPoint(root.TransformPoint(bounds.min));
            Vector3 high = panel.HostRect.InverseTransformPoint(root.TransformPoint(bounds.max));
            panel.FitEnabled = false;
            panel.HostRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Abs(high.x - low.x));
            panel.HostRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Abs(high.y - low.y));
            root.position += panel.HostRect.TransformPoint(panel.HostRect.rect.center) - root.TransformPoint(bounds.center);
            if (ready != null) ready.position = root.TransformPoint(seat);
            _writtenFrame = panel.HostRect.rect.size; _writtenPosition = root.anchoredPosition3D;
        }

        private Rect Bounds(RectTransform root, RectTransform? rect)
        {
            if (rect == null) return Rect.zero;
            rect.GetWorldCorners(_corners);
            Vector2 min = root.InverseTransformPoint(_corners[0]), max = min;
            for (int i = 1; i < 4; i++)
            { Vector2 p = root.InverseTransformPoint(_corners[i]); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        internal void Release(ConvertedPanel panel)
        { if (_panel == panel) Reset(); }

        internal void Reset()
        {
            if (_window != null) _window.onHidden.RemoveListener(Reset);
            if (_panel != null && _panel.IsAlive)
            {
                // Restore only our exact writes. A successor capture/fit owner
                // may already have adopted this pooled panel; its frame wins.
                // Manual shared world movement/scale is never part of this lease.
                bool ownsFrame = ReferenceEquals(_panel.FitContentRoot, _fitRoot)
                    && !_panel.FitEnabled && _panel.HostRect.rect.size == _writtenFrame;
                bool ownsPosition = _panel.Target.anchoredPosition3D == _writtenPosition;
                if (ownsFrame)
                {
                    _panel.FitEnabled = _fit;
                    _panel.HostRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _frame.x);
                    _panel.HostRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _frame.y);
                }
                if (ownsPosition) _panel.Target.anchoredPosition3D = _position;
            }
            _panel = null; _window = null; _fitRoot = null;
        }
    }

    private sealed class OwnerLifetime : MonoBehaviour
    {
        internal UIWindow? Owner;
        private void OnDisable()
        {
            // Unity forbids reparenting while this hierarchy is deactivating.
            // The control is a sibling, so it survives; the next ordinary Tick
            // restores home. Stop its visible/input output synchronously meanwhile.
            if (Owner != null && ReferenceEquals(Owner, _host)) SilenceOwner();
        }
    }
}
