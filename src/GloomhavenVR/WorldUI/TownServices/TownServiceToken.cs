using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>A non-authoritative sample of an original service entry. Merchant cards lift their
/// actual rigid presentation and always return without selecting or buying. Legacy temple and
/// enhancement samples select only when released over their tray; native confirmation owns payment.</summary>
internal sealed class TownServiceToken : IGrabbable, ITriggerOnlyGrabbable, IGrabbableHandFilter,
    IGrabCancellation, IGrabHighlight, IDisposable
{
    private readonly RectTransform _source;
    private readonly Selectable _button;
    private readonly Func<object?> _identity;
    private readonly Func<bool> _sessionAlive;
    private readonly Func<object?> _contextIdentity;
    private readonly Transform _mat;
    private readonly Transform? _physical;
    private Vector3 _homePosition, _homeScale, _returnPosition;
    private Quaternion _homeRotation, _returnRotation;
    private float _returnStarted;
    private bool _returning;
    internal bool IsPhysical => _physical != null;
    internal bool IsMoving => _hand != null || _returning;
    private readonly GameObject _pick;
    private readonly BoxCollider _shape;
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly List<CanvasGroup> _groups = new();
    private readonly List<RectMask2D> _masks = new();
    private Transform? _parent;
    private GameObject? _held;
    private RemoteWidgetMirror? _mirror;
    private VRHand? _hand;
    private object? _pickedIdentity;
    private object? _pickedContext;
    private Vector3 _heldPosition;
    private Quaternion _heldRotation;
    private bool _disposed;
    private bool _hover;
    private float _nextRefresh;

    internal Transform Source => _source;
    internal Transform? HeldRoot => _held != null ? _held.transform : null;
    internal Transform? HeldContent => _mirror?.CloneOf(_source);
    internal Transform? HeldCloneOf(Transform original) => _mirror?.CloneOf(original);
    public bool GrabWithGrip => false;
    public bool AllowsHand(VRHand hand) => !_disposed && _sessionAlive()
        && (!ReferenceEquals(hand.Grabber.Held, this) || _hand == hand);
    public bool CanGrab => !_disposed && _hand == null && !_returning && _sessionAlive()
        && _source != null && _source.gameObject.activeInHierarchy
        && _button != null && _button.IsActive() && (IsPhysical || _button.IsInteractable()) && _shape.enabled;

    internal TownServiceToken(RectTransform source, Selectable button, Func<object?> identity,
        Func<object?> contextIdentity, Func<bool> sessionAlive, Transform mat, Transform? physical = null)
    {
        _source = source; _button = button; _identity = identity; _contextIdentity = contextIdentity;
        _sessionAlive = sessionAlive; _mat = mat; _physical = physical;
        _pick = new GameObject("GloomhavenVR.TownService.SampleReach");
        _shape = _pick.AddComponent<BoxCollider>();
        _shape.isTrigger = true;
        _shape.enabled = false;
        VRInteractables.RegisterGrabbable(this, _shape);
    }

    internal void Tick(float scale)
    {
        if (_disposed) return;
        if (_source == null || !_sessionAlive()) { Dispose(); return; }
        if (_hand != null)
        {
            if (!ReferenceEquals(_pickedIdentity, _identity()) || !ReferenceEquals(_pickedContext, _contextIdentity())
                || !_source.gameObject.activeInHierarchy || (!IsPhysical && !_button.IsInteractable()))
            { CancelHold(); return; }
            if (_held != null)
            {
                _held.transform.SetPositionAndRotation(_hand.Rig.GrabAnchor.TransformPoint(_heldPosition),
                    _hand.Rig.GrabAnchor.rotation * _heldRotation);
                if (!IsPhysical) _held.transform.localScale = Vector3.one * scale;
            }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + .25f;
                if (_mirror != null && !_mirror.Refresh(_source)) { CancelHold(); return; }
            }
            _mirror?.TickLive();
            return;
        }
        if (_returning && _physical != null)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _returnStarted) / .22f);
            float ease = t * t * (3f - 2f * t);
            _physical.localPosition = Vector3.Lerp(_returnPosition, _homePosition, ease);
            _physical.localRotation = Quaternion.Slerp(_returnRotation, _homeRotation, ease);
            if (t >= 1f) _returning = false;
        }
        _shape.enabled = !_returning && Visible();
        if (!_shape.enabled) return;
        _source.GetWorldCorners(_corners);
        _pick.transform.SetPositionAndRotation((_corners[0] + _corners[2]) * .5f, _source.rotation);
        _shape.size = new Vector3(Vector3.Distance(_corners[0], _corners[3]),
            Vector3.Distance(_corners[0], _corners[1]), .009f * scale);
    }

    private bool Visible()
    {
        if (!_source.gameObject.activeInHierarchy) return false;
        _source.GetWorldCorners(_corners);
        Vector3 center = (_corners[0] + _corners[2]) * .5f;
        // A pooled/scroll-clipped row cannot steal a grip through a book cover or viewport.
        if (_parent != _source.parent)
        {
            _parent = _source.parent; _groups.Clear(); _masks.Clear();
            for (Transform? t = _source; t != null; t = t.parent)
            {
                CanvasGroup? group = t.GetComponent<CanvasGroup>();
                if (group != null) _groups.Add(group);
                RectMask2D? mask = t.GetComponent<RectMask2D>();
                if (mask != null) _masks.Add(mask);
            }
        }
        foreach (CanvasGroup group in _groups)
            if (group != null && group.enabled && (group.alpha < .01f || !group.interactable)) return false;
        foreach (RectMask2D mask in _masks)
        {
            if (mask == null || !mask.enabled) continue;
            Vector3 local = mask.rectTransform.InverseTransformPoint(center);
            if (!mask.rectTransform.rect.Contains(new Vector2(local.x, local.y))) return false;
        }
        return true;
    }

    public void OnGrab(VRHand hand)
    {
        if (!CanGrab) return;
        _pickedIdentity = _identity();
        _pickedContext = _contextIdentity();
        if (_pickedIdentity == null) return;
        _hand = hand;
        float side = Board.FigureGrab.HeldPoseMirror.OffsetSign(hand.Side == HandSide.Left);
        Vector3 pinch = new Vector3(0f, CardsConfig.HeldOffPalm.Value, CardsConfig.HeldForward.Value);
        FingerJoints thumb = hand.Rig.GetFinger(Finger.Thumb), index = hand.Rig.GetFinger(Finger.Index);
        if (thumb.IsValid && index.IsValid)
            pinch = hand.Rig.GrabAnchor.InverseTransformPoint((thumb.Tip.position + index.Tip.position) * .5f);
        _source.GetWorldCorners(_corners);
        float heldHeight = IsPhysical ? Vector3.Distance(_corners[0], _corners[1]) : .24f;
        CardGripPose.ReadingPose(CardsConfig.HeldFaceBias.Value, side, pinch, heldHeight, .15f,
            out _heldPosition, out _heldRotation);
        _shape.enabled = false;
        if (_physical != null)
        {
            // Lift the actual displayed card, including its rigid body. Never keep a second
            // card on the counter, invoke selection, or depend on affordability to inspect it.
            _homePosition = _physical.localPosition; _homeRotation = _physical.localRotation;
            _homeScale = _physical.localScale;
            _held = _physical.gameObject;
            Hover(true);
            return;
        }
        _held = new GameObject("GloomhavenVR.TownService.HeldSample");
        _held.transform.SetPositionAndRotation(hand.Rig.GrabAnchor.position, hand.Rig.GrabAnchor.rotation);
        _held.transform.localScale = Vector3.one * PanelLayout.WorldScale;
        _mirror = new RemoteWidgetMirror("TownServiceSample", _held.transform, .24f, .32f, Vector2.zero);
        if (!_mirror.Refresh(_source)) { CancelHold(); return; }
        Hover(true);
    }

    public void OnRelease(VRHand hand, Vector3 velocity)
    {
        if (_hand != hand) return;
        if (_physical != null)
        {
            // A sample is not a transaction: release anywhere (including the old work tray)
            // returns it to the rack. Only an explicit native click may select an item.
            Hover(false);
            _returnPosition = _physical.localPosition; _returnRotation = _physical.localRotation;
            _returnStarted = Time.unscaledTime; _returning = true;
            _held = null; _hand = null; _pickedIdentity = null; _pickedContext = null;
            return;
        }
        bool select = hand.HasPose && hand.TriggerUp && !_disposed && _sessionAlive() && _button != null && _button.IsInteractable()
            && ReferenceEquals(_pickedIdentity, _identity()) && ReferenceEquals(_pickedContext, _contextIdentity())
            && _source != null && _source.gameObject.activeInHierarchy && _held != null && _mat != null;
        if (select)
        {
            Vector3 point = _mat!.InverseTransformPoint(_held!.transform.position);
            select = Mathf.Abs(point.x) < .22f && Mathf.Abs(point.z) < .16f
                && point.y > -.06f && point.y < .20f;
        }
        CancelHold();
        if (!select || EventSystem.current == null) return;
        // No lower-level service call: the real button validates selection and opens the game's
        // confirmation, including ownership, affordability, stock and native multiplayer rules.
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(_button!.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }

    public void OnGrabHighlight(VRHand hand, bool highlighted) => Hover(highlighted);

    public void OnGrabCancelled(VRHand hand)
    {
        if (_hand == hand) CancelHold();
    }

    private void Hover(bool value)
    {
        if (_hover == value || _button == null || EventSystem.current == null) return;
        _hover = value;
        var pointer = new PointerEventData(EventSystem.current);
        if (value) ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        else ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
    }

    private void CancelHold()
    {
        Hover(false);
        _mirror?.Destroy(); _mirror = null;
        if (_physical != null && (_held != null || _returning))
        {
            _physical.localPosition = _homePosition; _physical.localRotation = _homeRotation;
            _physical.localScale = _homeScale;
        }
        else if (_held != null) UnityEngine.Object.Destroy(_held);
        _returning = false;
        _held = null; _hand = null; _pickedIdentity = null; _pickedContext = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        VRInteractables.UnregisterGrabbable(this);
        CancelHold();
        UnityEngine.Object.Destroy(_pick);
    }
}
