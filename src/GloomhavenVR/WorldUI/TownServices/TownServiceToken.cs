using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
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
    private readonly Transform? _physicalHomeParent;
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
    internal const float ReturnSeconds = .35f;
    private float _returnStarted = float.NegativeInfinity;
    private uint _returnRevision;
    private Vector3 _returnScale;
    private bool _returning, _heldTracked;
    private bool _settling, _settlementDecided;
    private object? _settledIdentity, _settledContext;
    private Vector3 _settledPosition;
    private Quaternion _settledRotation;
    private float _settledAt;
    internal float PhysicalVisibility { get; private set; } = 1f;
    internal bool IsPhysical => _physical != null;
    internal bool PhysicalAtHome => _physical != null && ReferenceEquals(_physical.parent, _physicalHomeParent);
    internal bool HasReturnMotion => !_disposed && _hand == null && !_settling && _offering == null
        && _returnRevision != 0 && Time.unscaledTime - _returnStarted >= 0f
        && Time.unscaledTime - _returnStarted <= ReturnSeconds + .25f;
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
    private readonly Bounds _physicalBounds;
    private readonly bool _hasPhysicalBounds;
    private Vector3 _heldPinch;
    private bool _insidePhysicalDrop;
    private float _nextPhysicalDropPulse;
    private readonly Func<VRHand, bool>? _handAllowed;

    internal Transform Source => _source;
    internal Transform? HeldRoot => _held != null ? _held.transform : null;
    internal VRHand? HoldingHand => _hand;
    internal Vector3 OfferingPoint => IsHeld ? PhysicalDropPoint : Vector3.zero;
    // The token owns the labelled Piece.Root, not the normalized bag's bottom root.
    // Its original mesh sits 65 mm lower. Sample the actual body rather than an
    // assumed midpoint above the label root (Build620's six missed bowl releases).
    private Vector3 PhysicalDropPoint => _physical != null && _uprightProp && _hasPhysicalBounds
        ? _physical.TransformPoint(_physicalBounds.center)
        : _held != null ? _held.transform.position : Vector3.zero;
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
        Func<bool>? drop = null, Func<bool>? eligible = null, Vector3 zoneCenter = default, Func<bool>? inspect = null, float zoneHalfWidth = .20f, Func<Vector3, bool>? dropLocation = null, Action? grabbing = null, float reachDepth = .009f, bool uprightProp = false, Func<VRHand, bool>? handAllowed = null, Transform? physicalBody = null)
    {
        _reachDepth = Mathf.Max(.001f, reachDepth);
        _uprightProp = uprightProp; _handAllowed = handAllowed;
        IsItemCard = physical != null && source.GetComponent<ItemCardUI>() != null;
        _source = source; _button = button; _identity = identity; _contextIdentity = contextIdentity;
        _sessionAlive = sessionAlive; _mat = mat; _physical = physical;
        _physicalHomeParent = physical != null ? physical.parent : null;
        if (uprightProp && physical != null)
            _hasPhysicalBounds = ReadPhysicalBounds(physicalBody ?? physical, physical, out _physicalBounds);
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
                else if (_uprightProp && _hasPhysicalBounds) TickPursePose(_hand);
                else _held.transform.SetPositionAndRotation(_hand.Rig.GrabAnchor.TransformPoint(_heldPosition),
                    _hand.Rig.GrabAnchor.rotation * _heldRotation);
                if (!IsPhysical) _held.transform.localScale = Vector3.one * scale;
                if (_uprightProp && _dropLocation != null)
                {
                    bool inside = DropEligible && _dropLocation(PhysicalDropPoint);
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
                if (t >= 1f)
                {
                    // A completed donation consumes the offered purse visual at the bowl,
                    // not the player's ability to inspect a purse again. Restore the
                    // physical fan prop only after the sink is fully invisible; the
                    // original native blessing still rejects any second payment.
                    _settling = _settlementDecided = false;
                    _settledIdentity = _settledContext = null;
                    _physical.SetParent(_homeParent, false);
                    _physical.localPosition = _homePosition;
                    _physical.localRotation = _homeRotation;
                    _physical.localScale = _homeScale;
                    PhysicalVisibility = 1f;
                }
            }
            if (_settling) { _shape.enabled = false; return; }
        }
        _offering?.Tick();
        if (_returning && _physical != null)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _returnStarted) / ReturnSeconds);
            float ease = t * t * (3f - 2f * t);
            _physical.localPosition = Vector3.Lerp(_returnPosition, _homePosition, ease);
            _physical.localRotation = Quaternion.Slerp(_returnRotation, _homeRotation, ease);
            _physical.localScale = Vector3.Lerp(_returnScale, _homeScale, ease);
            if (t >= 1f) _returning = false;
        }
        bool visible = !_returning && Visible();
        if (_uprightProp && !_hasPhysicalBounds) visible = false;
        if (_shape.enabled != visible) _shape.enabled = visible;
        if (!visible) return;
        if (_uprightProp && _physical != null)
        {
            // Only original mesh geometry is touchable. The inscription rectangle
            // above it used to steal trigger input and put the glove over the text.
            Transform physical = _physical;
            _pick.transform.SetPositionAndRotation(physical.TransformPoint(_physicalBounds.center), physical.rotation);
            Vector3 physicalScale = physical.lossyScale;
            _shape.size = Vector3.Scale(_physicalBounds.size, new Vector3(
                Mathf.Abs(physicalScale.x), Mathf.Abs(physicalScale.y), Mathf.Abs(physicalScale.z)));
            return;
        }
        // Visible sampled these exact corners immediately above. No transform or
        // source mutation occurs between the visibility check and collider fit.
        Vector3 center = (_corners[0] + _corners[2]) * .5f;
        Quaternion rotation = _source.rotation;
        Transform pick = _pick.transform;
        if (!pick.position.Equals(center) || !pick.rotation.Equals(rotation))
            pick.SetPositionAndRotation(center, rotation);
        Vector3 size = new(Vector3.Distance(_corners[0], _corners[3]),
            Vector3.Distance(_corners[0], _corners[1]), _reachDepth * scale);
        if (!_shape.size.Equals(size)) _shape.size = size;
    }

    private static bool ReadPhysicalBounds(Transform body, Transform frame, out Bounds bounds)
    {
        bool any = false; bounds = default;
        foreach (MeshFilter filter in body.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) continue;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = mesh.center + Vector3.Scale(mesh.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                point = frame.InverseTransformPoint(filter.transform.TransformPoint(point));
                if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                else bounds.Encapsulate(point);
            }
        }
        return any && bounds.size.sqrMagnitude > 1e-8f;
    }

    private void TickPursePose(VRHand hand)
    {
        Transform physical = _physical!;
        Vector3 pinch = hand.Rig.GrabAnchor.TransformPoint(_heldPinch);
        Vector3 forward = VRRigDriver.HeadCamera != null
            ? pinch - VRRigDriver.HeadCamera.transform.position : physical.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > .0001f)
            physical.rotation = Quaternion.LookRotation(forward, Vector3.up);
        // Native purse provenance gives the neck, including Piece.Body's real
        // offset. Root-at-bottom arithmetic used to leave the neck above the hand.
        Vector3 neck = _physicalBounds.center;
        neck.y = _physicalBounds.min.y + _physicalBounds.size.y * .90f;
        physical.position = pinch - physical.TransformVector(neck);
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
        _returnStarted = float.NegativeInfinity;
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
                _heldRotation = Quaternion.Inverse(hand.Rig.GrabAnchor.rotation) * _physical.rotation;
                _heldPinch = pinch;
            }
            _physical.SetParent(hand.Rig.GrabAnchor, true);
            _held = _physical.gameObject;
            if (_uprightProp && _hasPhysicalBounds) TickPursePose(hand);
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
            // Sample the visible purse body before parenting back to the worktop. A
            // Debug line per release separates a missed trigger, stale owner, unavailable
            // native blessing and missed bowl from a native confirmation refusal.
            Vector3 dropPoint = PhysicalDropPoint;
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
            if (IsItemCard && VRLog.WantsDebug)
                VRLog.Debug("TownServices", "Town item card release: " + (commit ? "palm eligible" : "returned")
                    + " tracked=" + _heldTracked + " pose=" + hand.HasPose + " triggerUp=" + hand.TriggerUp
                    + " eligible=" + eligible + " identity=" + identity + " context=" + context
                    + " palm=" + inZone + " point=" + dropPoint);
            Hover(false);
            _physical.SetParent(_homeParent, true);
            _returnPosition = _physical.localPosition; _returnRotation = _physical.localRotation; _returnScale = _physical.localScale;
            BeginReturn();
            _held = null; _hand = null; _pickedIdentity = null; _pickedContext = null;
            if (commit)
            {
                if (_uprightProp && _physical != null)
                {
                    // Await the original native confirmation in the physical bowl. This
                    // is visual ownership only; successful payment still runs exclusively
                    // through the native service callback, including its host validation.
                    _returnStarted = float.NegativeInfinity;
                    _returning = false; _settling = true; _settlementDecided = false;
                    _settledIdentity = _identity(); _settledContext = _contextIdentity();
                    _physical.SetParent(_mat, true);
                    // Piece.Body is a child of this labelled root. Its former
                    // Body.parent == DropFrame check could never seat the purse.
                    // Move the actual body's bottom to the authored bowl seat
                    // once; the subsequent accepted sink keeps its own clock.
                    if (_hasPhysicalBounds)
                    {
                        _physical.localRotation = Quaternion.identity;
                        Vector3 bottom = _physicalBounds.center; bottom.y = _physicalBounds.min.y;
                        _physical.position = _mat.TransformPoint(TownServiceTempleBowl.PurseSeat)
                            - _physical.TransformVector(bottom);
                    }
                    _settledPosition = _physical.localPosition; _settledRotation = _physical.localRotation;
                    _shape.enabled = false;
                }
                bool accepted = _drop!();
                if (accepted && IsItemCard && hand.HasPose)
                    hand.SendHaptic(HapticPreset.ClickPulse);
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
        _returnScale = _physical.localScale; BeginReturn();
    }

    internal void ParkOffering(Transform seat, Action reclaimed)
    {
        if (_physical == null || _disposed || _hand != null) return;
        _returnStarted = float.NegativeInfinity;
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
        _returnScale = _physical.localScale; BeginReturn();
    }

    private void BeginReturn()
    {
        _returnStarted = Time.unscaledTime; _returning = true;
        if (++_returnRevision == 0) _returnRevision = 1;
    }

    /// <summary>Author the actual local return tween against the approved destination holder.
    /// Keep its final endpoint briefly for a delayed packet; pickup/deposit/cancel invalidate it.
    /// A receiver evaluates the same SmoothStep instead of stretching this short flight into
    /// the sparse wrist-fan interpolation interval.</summary>
    internal bool TryReturnMotion(Transform source, VRHand destination, Transform shared,
        out uint revision, out float[] numbers)
    {
        revision = _returnRevision; numbers = Array.Empty<float>();
        float age = Time.unscaledTime - _returnStarted;
        if (_disposed || _physical == null || _homeParent == null || _hand != null || _settling
            || _offering != null || revision == 0 || age < 0f || age > ReturnSeconds + .25f
            || !(source == _physical || source.IsChildOf(_physical))) return false;
        numbers = new float[22]; numbers[0] = age; numbers[1] = ReturnSeconds;
        Matrix4x4 relative = _physical.worldToLocalMatrix * source.localToWorldMatrix;
        Quaternion relativeRotation = Quaternion.Inverse(_physical.rotation) * source.rotation;
        WriteReturnPose(numbers, 2, _returnPosition, _returnRotation, _returnScale, relative,
            relativeRotation, destination, shared);
        WriteReturnPose(numbers, 12, _homePosition, _homeRotation, _homeScale, relative,
            relativeRotation, destination, shared);
        return true;
    }

    internal bool TryCardReturnMotion(Transform source, Transform shared, VRHand? hand,
        out uint revision, out float[] numbers)
    {
        revision = _returnRevision; numbers = Array.Empty<float>();
        float age = Time.unscaledTime - _returnStarted;
        if (!IsItemCard || !HasReturnMotion || _physical == null || _homeParent == null
            || !(source == _physical || source.IsChildOf(_physical))) return false;
        Matrix4x4 parent = _homeParent.localToWorldMatrix;
        numbers = Net.TownServices.TownCardReturnMotion.Capture(source, _physical, shared, null,
            age, ReturnSeconds, 0, 0f,
            parent * Matrix4x4.TRS(_returnPosition, _returnRotation, _returnScale), _homeParent.rotation * _returnRotation,
            parent * Matrix4x4.TRS(_homePosition, _homeRotation, _homeScale), _homeParent.rotation * _homeRotation,
            Vector3.zero);
        return true;
    }

    private void WriteReturnPose(float[] values, int at, Vector3 position, Quaternion rotation,
        Vector3 scale, Matrix4x4 relative, Quaternion relativeRotation, VRHand hand, Transform shared)
    {
        Matrix4x4 world = _homeParent!.localToWorldMatrix * Matrix4x4.TRS(position, rotation, scale) * relative;
        float holderScale = Mathf.Max(.0001f, Mathf.Abs(hand.WorldScale));
        Vector3 p = Quaternion.Inverse(hand.Rig.Root.rotation)
            * (world.MultiplyPoint3x4(Vector3.zero) - hand.Rig.Root.position) / holderScale;
        Quaternion r = Quaternion.Inverse(shared.rotation) * _homeParent.rotation * rotation * relativeRotation;
        Vector3 size = new(world.GetColumn(0).magnitude / holderScale,
            world.GetColumn(1).magnitude / holderScale, world.GetColumn(2).magnitude / holderScale);
        values[at] = p.x; values[at + 1] = p.y; values[at + 2] = p.z;
        values[at + 3] = r.x; values[at + 4] = r.y; values[at + 5] = r.z; values[at + 6] = r.w;
        values[at + 7] = size.x; values[at + 8] = size.y; values[at + 9] = size.z;
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
        _returnStarted = float.NegativeInfinity;
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
