using System;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>A non-authoritative sample of an original catalog entry. Only releasing over the
/// work mat dispatches the original selection button; its native confirmation still owns payment.</summary>
internal sealed class TownServiceToken : IGrabbable, ITriggerOnlyGrabbable, IGrabHighlight, IDisposable
{
    private readonly RectTransform _source;
    private readonly Selectable _button;
    private readonly Func<object?> _identity;
    private readonly Func<bool> _sessionAlive;
    private readonly Transform _mat;
    private readonly GameObject _pick;
    private readonly BoxCollider _shape;
    private readonly Vector3[] _corners = new Vector3[4];
    private readonly Vector3[] _maskCorners = new Vector3[4];
    private GameObject? _held;
    private RemoteWidgetMirror? _mirror;
    private VRHand? _hand;
    private object? _pickedIdentity;
    private bool _disposed;
    private bool _hover;

    internal Transform Source => _source;
    internal Transform? HeldRoot => _held != null ? _held.transform : null;
    public bool GrabWithGrip => false;
    public bool CanGrab => !_disposed && _hand == null && _sessionAlive()
        && _source != null && _source.gameObject.activeInHierarchy
        && _button != null && _button.IsActive() && _button.IsInteractable() && _shape.enabled;

    internal TownServiceToken(RectTransform source, Selectable button, Func<object?> identity,
        Func<bool> sessionAlive, Transform mat)
    {
        _source = source; _button = button; _identity = identity; _sessionAlive = sessionAlive; _mat = mat;
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
            if (!ReferenceEquals(_pickedIdentity, _identity()) || !_source.gameObject.activeInHierarchy)
            { CancelHold(); return; }
            if (_held != null)
            {
                _held.transform.SetPositionAndRotation(_hand.Rig.GrabAnchor.position, _hand.Rig.GrabAnchor.rotation);
                _held.transform.localScale = Vector3.one * scale;
            }
            _mirror?.TickLive();
            return;
        }
        _shape.enabled = Visible();
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
        for (Transform? t = _source; t != null; t = t.parent)
        {
            CanvasGroup? group = t.GetComponent<CanvasGroup>();
            if (group != null && group.enabled && (group.alpha < .01f || !group.interactable)) return false;
            RectMask2D? mask = t.GetComponent<RectMask2D>();
            if (mask == null || !mask.enabled) continue;
            mask.rectTransform.GetWorldCorners(_maskCorners);
            Vector3 local = mask.rectTransform.InverseTransformPoint(center);
            if (!mask.rectTransform.rect.Contains(new Vector2(local.x, local.y))) return false;
        }
        return true;
    }

    public void OnGrab(VRHand hand)
    {
        if (!CanGrab) return;
        _pickedIdentity = _identity();
        if (_pickedIdentity == null) return;
        _hand = hand;
        _shape.enabled = false;
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
        bool select = !_disposed && _sessionAlive() && _button != null && _button.IsInteractable()
            && ReferenceEquals(_pickedIdentity, _identity()) && _held != null && _mat != null;
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
        if (_held != null) UnityEngine.Object.Destroy(_held);
        _held = null; _hand = null; _pickedIdentity = null;
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
