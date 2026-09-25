using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>A non-authoritative physical sample of an original service entry. Picking up
/// only inspects it; an eligible deliberate drop invokes its original transaction callback.
/// Every other release returns the sample without changing native gameplay state.</summary>
internal sealed class TownServiceToken : IGrabbable, ITriggerOnlyGrabbable, IGrabbableHandFilter,
    IGrabCancellation, IGrabHighlight, IItemCardHold, IFanSweepTarget, IDisposable
{
    private readonly RectTransform _source;
    public bool IsItemCard { get; }
    private readonly Selectable _button;
    private readonly Func<object?> _identity;
    private readonly Func<bool> _sessionAlive;
    private readonly Func<object?> _contextIdentity;
    private readonly Transform _mat;
    private readonly Transform? _physical;
    private readonly Func<bool>? _drop, _eligible, _inspect;
    private readonly Func<Vector3, bool>? _dropLocation;
    private readonly Action? _grabbing;
    private readonly Vector3 _zoneCenter;
    private readonly float _zoneHalfWidth;
    private static ulong _nextPickup;
    internal ulong PickupSequence { get; private set; }
    internal bool IsHeld => _hand != null;
    internal bool DropEligible => _hand != null && (_eligible?.Invoke() ?? false);
    internal Collider PickCollider => _shape;
    internal Transform ZoneFrame => _mat;
    internal Vector3 ZoneCenter => _zoneCenter;
    private Vector3 _homePosition, _homeScale, _returnPosition;
    private Transform? _homeParent;
    private TownServiceOfferingCard? _offering;
    private Action? _offeringReclaimed;
    private Quaternion _homeRotation, _returnRotation;
    private float _returnStarted;
    private Vector3 _returnScale;
    private bool _returning, _heldTracked;
    private bool _settling, _settlementDecided;
    private object? _settledIdentity, _settledContext;
    private Vector3 _settledPosition;
    private Quaternion _settledRotation;
    private float _settledAt;
    internal float PhysicalVisibility { get; private set; } = 1f;
    internal bool IsPhysical => _physical != null;
    internal bool IsMoving => _hand != null || _returning || _offering != null || _settling && PhysicalVisibility > 0f;
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
    private float _heldScale = 1f, _heldWidth, _heldHeight;
    private VRHand? _transferTo;
    private bool _adopting;
    Transform? IItemCardHold.HeldRoot => HeldRoot;
    private Quaternion _heldRotation;
    private bool _disposed;
    private bool _hover;
    private float _nextRefresh;
    private readonly float _reachDepth;
    private readonly bool _uprightProp;
    private bool _insidePhysicalDrop;
    private float _nextPhysicalDropPulse;
    private readonly Func<VRHand, bool>? _handAllowed;

    internal Transform Source => _source;
    internal Transform? HeldRoot => _held != null ? _held.transform : null;
    internal Transform? HeldContent => _mirror?.CloneOf(_source);
    internal Transform? HeldCloneOf(Transform original) => _mirror?.CloneOf(original);
    public bool GrabWithGrip => false;
    public bool AllowsHand(VRHand hand) => !_disposed && _sessionAlive()
        && (_handAllowed?.Invoke(hand) ?? true)
        && (!ReferenceEquals(hand.Grabber.Held, this) || _hand == hand);
    public bool CanGrab => !_disposed && _hand == null && !_returning && !_settling && _sessionAlive()
        && ((_offering != null && TownServiceMerchantHandoff.CanReclaim(this)) || (_inspect?.Invoke() ?? true)) && _source != null && _source.gameObject.activeInHierarchy
        && (IsPhysical || (_button != null && _button.IsActive() && _button.IsInteractable())) && _shape.enabled;

    internal TownServiceToken(RectTransform source, Selectable button, Func<object?> identity,
        Func<object?> contextIdentity, Func<bool> sessionAlive, Transform mat, Transform? physical = null,
        Func<bool>? drop = null, Func<bool>? eligible = null, Vector3 zoneCenter = default, Func<bool>? inspect = null, float zoneHalfWidth = .20f, Func<Vector3, bool>? dropLocation = null, Action? grabbing = null, float reachDepth = .009f, bool uprightProp = false, Func<VRHand, bool>? handAllowed = null)
    {
        _reachDepth = Mathf.Max(.001f, reachDepth);
        _uprightProp = uprightProp; _handAllowed = handAllowed;
        IsItemCard = physical != null && source.GetComponent<ItemCardUI>() != null;
        _source = source; _button = button; _identity = identity; _contextIdentity = contextIdentity;
        _sessionAlive = sessionAlive; _mat = mat; _physical = physical;
        _dropLocation = dropLocation; _grabbing = grabbing;
        _drop = drop; _eligible = eligible; _zoneCenter = zoneCenter; _inspect = inspect; _zoneHalfWidth = zoneHalfWidth;
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
                _heldTracked = true;
                if (IsItemCard) ItemCardHold.Tick(_held.transform, _hand, _hand.Rig.GrabAnchor,
                    _heldPosition, _heldScale, _heldWidth, _heldHeight);
                else _held.transform.SetPositionAndRotation(_hand.Rig.GrabAnchor.TransformPoint(_heldPosition),
                    _hand.Rig.GrabAnchor.rotation * _heldRotation);
                if (!IsPhysical) _held.transform.localScale = Vector3.one * scale;
                if (_uprightProp && _dropLocation != null)
                {
                    bool inside = DropEligible && _dropLocation(_held.transform.position);
                    if (inside && !_insidePhysicalDrop && _hand.HasPose
                        && Time.unscaledTime >= _nextPhysicalDropPulse)
                    {
                        _nextPhysicalDropPulse = Time.unscaledTime + .3f;
                        _hand.SendHaptic(HapticPreset.ClickPulse);
                    }
                    _insidePhysicalDrop = inside;
                }
            }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + .25f;
                if (_mirror != null && !_mirror.Refresh(_source)) { CancelHold(); return; }
            }
            _mirror?.TickLive();
            return;
        }
        if (_settling && _physical != null)
        {
            if (!ReferenceEquals(_settledIdentity, _identity()) || !ReferenceEquals(_settledContext, _contextIdentity()))
                CompletePhysicalOffering(false);
            else if (_settlementDecided)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _settledAt) / .28f);
                float ease = t * t * (3f - 2f * t);
                _physical.localPosition = _settledPosition - Vector3.up * (.04f * ease);
                _physical.localRotation = Quaternion.Slerp(_settledRotation, Quaternion.identity, ease);
                PhysicalVisibility = 1f - ease;
            }
            if (_settling) { _shape.enabled = false; return; }
        }
        _offering?.Tick();
        if (_returning && _physical != null)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _returnStarted) / .35f);
            float ease = t * t * (3f - 2f * t);
            _physical.localPosition = Vector3.Lerp(_returnPosition, _homePosition, ease);
            _physical.localRotation = Quaternion.Slerp(_returnRotation, _homeRotation, ease);
            _physical.localScale = Vector3.Lerp(_returnScale, _homeScale, ease);
            if (t >= 1f) _returning = false;
        }
        _shape.enabled = !_returning && Visible();
        if (!_shape.enabled) return;
        _source.GetWorldCorners(_corners);
        _pick.transform.SetPositionAndRotation((_corners[0] + _corners[2]) * .5f, _source.rotation);
        _shape.size = new Vector3(Vector3.Distance(_corners[0], _corners[3]),
            Vector3.Distance(_corners[0], _corners[1]), _reachDepth * scale);
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
        _grabbing?.Invoke();
        _pickedIdentity = _identity();
        _pickedContext = _contextIdentity();
        if (_pickedIdentity == null) return;
        _hand = hand; PickupSequence=++_nextPickup; _heldTracked = false; _insidePhysicalDrop = false;
        TownServicePhysicalRay.Claim(hand);
        float side = Board.FigureGrab.HeldPoseMirror.OffsetSign(hand.Side == HandSide.Left);
        Vector3 pinch = new Vector3(0f, CardsConfig.HeldOffPalm.Value, CardsConfig.HeldForward.Value);
        FingerJoints thumb = hand.Rig.GetFinger(Finger.Thumb), index = hand.Rig.GetFinger(Finger.Index);
        if (thumb.IsValid && index.IsValid)
            pinch = hand.Rig.GrabAnchor.InverseTransformPoint((thumb.Tip.position + index.Tip.position) * .5f);
        _source.GetWorldCorners(_corners);
        // ReadingPose consumes anchor-local metres. World corners already include the
        // tabletop scale; using their world distance scales the grip offset a second time.
        float heldHeight = IsPhysical ? Vector3.Distance(
            hand.Rig.GrabAnchor.InverseTransformPoint(_corners[0]),
            hand.Rig.GrabAnchor.InverseTransformPoint(_corners[1])) : .24f;
        CardGripPose.ReadingPose(CardsConfig.HeldFaceBias.Value, side, pinch, heldHeight, .15f,
            out _heldPosition, out _heldRotation);
        _shape.enabled = false;
        if (_physical != null)
        {
            // Lift the actual displayed card, including its rigid body. Never keep a second
            // card on the counter, invoke selection, or depend on affordability to inspect it.
            bool reclaiming = _offering != null;
            if (!_adopting && !reclaiming)
            { _homeParent = _physical.parent; _homePosition = _physical.localPosition; _homeRotation = _physical.localRotation;
              _homeScale = _physical.localScale; }
            if (IsItemCard)
            {
            float width = Vector3.Distance(_corners[0], _corners[3]) / Mathf.Max(.0001f, _physical.lossyScale.x);
            float height = Vector3.Distance(_corners[0], _corners[1]) / Mathf.Max(.0001f, _physical.lossyScale.y);
            _heldScale = CardsConfig.CardWidth.Value / Mathf.Max(width, height) * CardsConfig.InspectScale.Value;
            _heldWidth = width * _heldScale; _heldHeight = height * _heldScale;
            ItemCardHold.ReadingPose(hand, _heldHeight, VRCard.PinchGripFraction, out _heldPosition, out _heldRotation);
            hand.SendHaptic(HapticPreset.ClickPulse);
            CardsDriver.PlayCardSound(CardsConfig.CardGrabSound.Value, _physical);
            }
            if (_uprightProp)
            {
                // The original purse template is normalized at its bottom and stands about
                // 15 cm tall. Put that bottom below the pinch so the fingers hold its neck;
                // the old 5.5 cm drop buried most of the mesh through the glove.
                _heldRotation = Quaternion.Inverse(hand.Rig.GrabAnchor.rotation) * _physical.rotation;
                _heldPosition = pinch + hand.Rig.GrabAnchor.InverseTransformVector(Vector3.down * (.13f * hand.WorldScale));
            }
            _physical.SetParent(hand.Rig.GrabAnchor, true);
            _held = _physical.gameObject;
            if (reclaiming)
            {
                _offering = null;
                Action? reclaimed = _offeringReclaimed; _offeringReclaimed = null;
                reclaimed?.Invoke();
            }
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
        TownServicePhysicalRay.Claim(hand);
        _insidePhysicalDrop = false;
        if (_transferTo != null && _physical != null)
        {
            VRHand recipient = _transferTo; _transferTo = null; _hand = null; _held = null;
            _adopting = true; _shape.enabled = true;
            try
            {
                if (recipient.Grabber.ForceGrab(this, releaseOnTriggerUp: true)) return;
                // Preserve the original hold if the recipient refuses; never transact or
                // teleport a sample merely because hand-to-hand adoption was declined.
                if (hand.Grabber.ForceGrab(this, releaseOnTriggerUp: true)) return;
            }
            finally { _adopting = false; }
            _hand = hand; _held = _physical.gameObject;
        }
        if (_physical != null)
        {
            // A deliberate trigger release in the matching zone is the sole transaction
            // gesture. Cancellation, stale context, pose loss and all other drops return.
            // The purse's normalized root is its visible bottom, which is the point that
            // must enter the bowl. Sample it before parenting back to the worktop. A
            // Debug line per release separates a missed trigger, stale owner, unavailable
            // native blessing and missed bowl from a native confirmation refusal.
            Vector3 dropPoint = _held != null ? _held.transform.position : Vector3.zero;
            bool gesture = _heldTracked && hand.HasPose && hand.TriggerUp && _drop != null;
            bool eligible = gesture && DropEligible;
            bool identity = eligible && ReferenceEquals(_pickedIdentity, _identity());
            bool context = identity && ReferenceEquals(_pickedContext, _contextIdentity());
            bool inZone = context && _held != null && (_dropLocation?.Invoke(dropPoint)
                ?? WithinDropZone(_mat.InverseTransformPoint(dropPoint) - _zoneCenter));
            bool commit = inZone;
            if (_uprightProp && VRLog.WantsDebug)
                VRLog.Debug("TownServices", "Temple purse release: " + (commit ? "bowl accepted" : "returned")
                    + " tracked=" + _heldTracked + " pose=" + hand.HasPose + " triggerUp=" + hand.TriggerUp
                    + " eligible=" + eligible + " identity=" + identity + " context=" + context
                    + " bowl=" + inZone + " point=" + dropPoint);
            Hover(false);
            _physical.SetParent(_homeParent, true);
            _returnPosition = _physical.localPosition; _returnRotation = _physical.localRotation; _returnScale = _physical.localScale;
            _returnStarted = Time.unscaledTime; _returning = true;
            _held = null; _hand = null; _pickedIdentity = null; _pickedContext = null;
            if (commit)
            {
                if (_uprightProp && _physical != null)
                {
                    // Await the original native confirmation in the physical bowl. This
                    // is visual ownership only; successful payment still runs exclusively
                    // through the native service callback, including its host validation.
                    _returning = false; _settling = true; _settlementDecided = false;
                    _settledIdentity = _identity(); _settledContext = _contextIdentity();
                    _physical.SetParent(_mat, true);
                    _settledPosition = _physical.localPosition; _settledRotation = _physical.localRotation;
                    _shape.enabled = false;
                }
                bool accepted = _drop!();
                if (_uprightProp && !accepted) CompletePhysicalOffering(false);
            }
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

    internal void CompletePhysicalOffering(bool accepted)
    {
        if (!_settling || _physical == null || _disposed) return;
        if (accepted)
        {
            if (_settlementDecided) return;
            _settlementDecided = true; _settledAt = Time.unscaledTime;
            return;
        }
        _settling = _settlementDecided = false; _settledIdentity = _settledContext = null;
        PhysicalVisibility = 1f;
        _physical.SetParent(_homeParent, true);
        _returnPosition = _physical.localPosition; _returnRotation = _physical.localRotation;
        _returnScale = _physical.localScale; _returnStarted = Time.unscaledTime; _returning = true;
    }

    internal void ParkOffering(Transform seat, Action reclaimed)
    {
        if (_physical == null || _disposed || _hand != null) return;
        _returning = false; _offeringReclaimed = reclaimed;
        float nativeScale = _homeParent != null ? Mathf.Abs(_homeParent.lossyScale.x) : 1f;
        // Cabinet cards are small samples; the hand presentation is comfortably readable.
        float scale = _homeScale.x * nativeScale / Mathf.Max(.0001f, seat.lossyScale.x) * 1.5f;
        _offering = new TownServiceOfferingCard(_physical, seat, scale);
    }

    internal void ReturnOffering()
    {
        _offering = null; _offeringReclaimed = null;
        if (_disposed || _physical == null || _hand != null) return;
        _physical.SetParent(_homeParent, true);
        _returnPosition = _physical.localPosition; _returnRotation = _physical.localRotation;
        _returnScale = _physical.localScale; _returnStarted = Time.unscaledTime; _returning = true;
    }

    private bool WithinDropZone(Vector3 point) => InDropZone(point) && Mathf.Abs(point.x) < _zoneHalfWidth;

    internal static bool InDropZone(Vector3 point) => Mathf.Abs(point.x) < .20f
        && Mathf.Abs(point.z) < .14f && point.y > -.045f && point.y < .15f;

    public bool Transfer(VRHand from, VRHand to)
    {
        if (_hand != from || !IsItemCard || to.Grabber.Held != null) return false;
        _transferTo = to;
        try { from.Grabber.CancelAll(); return ReferenceEquals(to.Grabber.Held, this); }
        finally { _transferTo = null; }
    }

    internal void CancelInspection()
    {
        if (_hand != null) _hand.Grabber.CancelAll();
        else if (_settling && !_settlementDecided) CompletePhysicalOffering(false);
        else if (_offering != null)
        {
            Action? reclaim = _offeringReclaimed;
            ReturnOffering();
            reclaim?.Invoke();
        }
    }

    bool IFanSweepTarget.SweepEligible => CanGrab && IsItemCard;
    float IFanSweepTarget.SweepFaceWidthWorld => _source != null
        ? Mathf.Abs(_source.rect.width * _source.lossyScale.x) : 0f;
    string IFanSweepTarget.SweepName => _source != null ? _source.name : "Merchant item";
    bool IFanSweepTarget.TrySweepDistance(Vector3 point, out float distance) => TryTouch(point, out distance);

    public bool TryTouch(Vector3 point, out float distance)
    {
        distance = float.MaxValue;
        if (_held == null || _source == null) return false;
        Vector3 local = _source.InverseTransformPoint(point);
        Rect rect = _source.rect;
        Vector3 closest = new Vector3(Mathf.Clamp(local.x, rect.xMin, rect.xMax),
            Mathf.Clamp(local.y, rect.yMin, rect.yMax), 0f);
        distance = Vector3.Distance(point, _source.TransformPoint(closest)); return true;
    }

    public void OnGrabHighlight(VRHand hand, bool highlighted) => Hover(highlighted);

    public void OnGrabCancelled(VRHand hand)
    {
        if (_hand == hand)
        { if (_transferTo != null) OnRelease(hand, Vector3.zero); else CancelHold(); }
    }

    private void Hover(bool value)
    {
        if (IsPhysical || _hover == value || _button == null || EventSystem.current == null) return;
        _hover = value;
        var pointer = new PointerEventData(EventSystem.current);
        if (value) ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        else ExecuteEvents.Execute(_button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
    }

    private void CancelHold()
    {
        Hover(false);
        _mirror?.Destroy(); _mirror = null;
        if (_physical != null && (_held != null || _returning || _offering != null || _settling))
        {
            _physical.SetParent(_homeParent, true);
            _physical.localPosition = _homePosition; _physical.localRotation = _homeRotation;
            _physical.localScale = _homeScale;
        }
        else if (_held != null) UnityEngine.Object.Destroy(_held);
        _returning = false; _offering = null; _offeringReclaimed = null;
        _settling = _settlementDecided = false; PhysicalVisibility = 1f; _settledIdentity = _settledContext = null;
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
