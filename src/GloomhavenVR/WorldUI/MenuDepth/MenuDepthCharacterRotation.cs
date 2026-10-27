using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Turn one original private character preview by dragging its actual RawImage.
/// The native manager has no drag/rotation API. This state is presentation-only:
/// its renderer composes a matrix about the main actor's fixed ground origin,
/// never writing the holder, model, animation or gameplay actor transforms.
/// </summary>
internal static class MenuDepthCharacterRotation
{
    private static readonly Dictionary<RawImage, MenuDepthCharacterRotationReceiver> Bindings = new();

    /// <summary>The callback returns null whenever this exact depth source is not ready.</summary>
    internal static void Bind(RawImage image, Character3DDisplayManager manager, Func<object?> readyIdentity)
    {
        if (image == null || manager == null) return;
        Component? owner = PrivateOwner(image);
        if (owner == null) { Unbind(image); return; }
        if (Bindings.TryGetValue(image, out MenuDepthCharacterRotationReceiver receiver) && receiver != null)
        {
            receiver.Configure(image, manager, owner, readyIdentity);
            return;
        }
        receiver = image.gameObject.AddComponent<MenuDepthCharacterRotationReceiver>();
        Bindings[image] = receiver;
        receiver.Configure(image, manager, owner, readyIdentity);
    }

    // Read-only authoring audit: eight of the twelve native RT consumers belong
    // to these interactive private controllers. Retirement/creation celebration
    // consumers retain their original automatic presentation and hit behavior.
    internal static Component? PrivateOwner(RawImage image)
        => image.GetComponentInParent<UIAdventurePartyAssemblyCharacterDisplay>(true) as Component
            ?? image.GetComponentInParent<UICharacterCreatorWindow>(true);

    internal static void Unbind(RawImage image)
    {
        if (ReferenceEquals(image, null) || !Bindings.TryGetValue(image, out MenuDepthCharacterRotationReceiver receiver)) return;
        Bindings.Remove(image);
        if (receiver == null) return;
        receiver.Retire();
        Object.Destroy(receiver);
    }

    internal static void Shutdown()
    {
        foreach (MenuDepthCharacterRotationReceiver receiver in Bindings.Values)
            if (receiver != null) { receiver.Retire(); Object.Destroy(receiver); }
        Bindings.Clear();
    }

    internal static float GetYaw(RawImage image, object identity) => image != null
        && Bindings.TryGetValue(image, out MenuDepthCharacterRotationReceiver receiver) && receiver != null
            ? receiver.Observe(identity) : 0f;

    internal static Matrix4x4 NativeRotation(float yaw, Vector3 nativeMainGround, Vector3 nativeUp)
        => Matrix4x4.Translate(nativeMainGround)
            * Matrix4x4.Rotate(Quaternion.AngleAxis(yaw, nativeUp))
            * Matrix4x4.Translate(-nativeMainGround);

    internal static void Forget(RawImage? image, MenuDepthCharacterRotationReceiver receiver)
    {
        if (ReferenceEquals(image, null)) return;
        if (Bindings.TryGetValue(image!, out MenuDepthCharacterRotationReceiver current) && ReferenceEquals(current, receiver))
            Bindings.Remove(image!);
    }
}

/// <summary>
/// A normal uGUI drag target on the original portrait. Existing VR laser and native
/// mouse dispatch retain their modality, tracking and press-capture authority.
/// </summary>
internal sealed class MenuDepthCharacterRotationReceiver : MonoBehaviour, IPointerDownHandler,
    IPointerUpHandler, IPointerClickHandler, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RawImage? _image;
    private Character3DDisplayManager? _manager;
    private Component? _owner;
    private UIWindow? _nativeWindow;
    private Func<object?>? _readyIdentity;
    private object? _identity;
    private bool _configured, _nativeRaycast, _ownsRaycast;
    private int? _pointer;
    private BaseRaycaster? _pressRaycaster;
    private float _lastX, _yaw;
    private static int _identityFailures;

    private bool EnabledHere => VRSession.IsRunning && WorldUIConfig.CharacterPreviewDepth.Value
        && isActiveAndEnabled && _image != null && _image.isActiveAndEnabled
        && _image.gameObject.activeInHierarchy && _manager != null && _manager.isActiveAndEnabled
        && _owner != null && _owner.gameObject.activeInHierarchy;

    internal void Configure(RawImage image, Character3DDisplayManager manager, Component owner, Func<object?> readyIdentity)
    {
        if (_configured && (!ReferenceEquals(_image, image) || !ReferenceEquals(_manager, manager)))
        { ReleaseRaycast(); ResetIdentity(null); }
        if (!_configured || !ReferenceEquals(_image, image)) _nativeRaycast = image.raycastTarget;
        _configured = true; _image = image; _manager = manager; _owner = owner;
        _nativeWindow = owner.GetComponent<UIWindow>(); _readyIdentity = readyIdentity;
        Refresh();
    }

    internal float Observe(object identity)
    {
        if (!EnabledHere) { ResetIdentity(null); ReleaseRaycast(); return 0f; }
        ResetIdentity(identity);
        AcquireRaycast();
        return _yaw;
    }

    private object? ReadyIdentity()
    {
        if (!EnabledHere || _readyIdentity == null) return null;
        try { return _readyIdentity(); }
        catch (Exception error)
        {
            int count = ++_identityFailures;
            if (count <= 2 || (count & (count - 1)) == 0)
                VRLog.Warn("WorldUI", "MENU DEPTH CHARACTER ROTATION: source readiness failed; native input restored: "
                    + error.GetType().Name + "; count=" + count + ".");
            return null;
        }
    }

    private bool Refresh()
    {
        object? identity = ReadyIdentity();
        ResetIdentity(identity);
        if (identity == null) { ReleaseRaycast(); return false; }
        AcquireRaycast();
        return true;
    }

    private void ResetIdentity(object? identity)
    {
        if (ReferenceEquals(identity, _identity)) return;
        Cancel(); _identity = identity; _yaw = 0f;
    }

    private void AcquireRaycast()
    {
        if (_image == null) return;
        _ownsRaycast = true;
        if (!_image.raycastTarget) _image.raycastTarget = true;
    }

    private void ReleaseRaycast()
    {
        if (!_ownsRaycast) return;
        _ownsRaycast = false;
        if (_image != null) _image.raycastTarget = _nativeRaycast;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || _pointer.HasValue || !Refresh()
            || !ReferenceEquals(MenuDepthCharacterRotation.PrivateOwner(_image!), _owner)
            || !Visible() || !TryLocalX(eventData, out float x)) return;
        BaseRaycaster module = eventData.pointerPressRaycast.module;
        if (module != null && !module.isActiveAndEnabled) return;
        // Every admitted native owner has a UIWindow on this same original root.
        // Its original portrait ancestry has no click/select action, but it does
        // focus through UIWindow.OnPointerDown. Preserve that exact virtual
        // callback and its m_DisabledClickToFocus guard when adding this target.
        if (_nativeWindow != null) _nativeWindow.OnPointerDown(eventData);
        _pointer = eventData.pointerId; _pressRaycaster = module; _lastX = x;
        eventData.eligibleForClick = false;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;
    public void OnBeginDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

    public void OnDrag(PointerEventData eventData)
    {
        if (!_pointer.HasValue || _pointer.Value != eventData.pointerId) return;
        if (!Refresh() || !Visible() || (_pressRaycaster != null && !_pressRaycaster.isActiveAndEnabled)
            || !TryLocalX(eventData, out float x)) { Cancel(); return; }
        // Refresh may retire a held old identity. Its remaining drag cannot turn
        // a replacement model, even when the pointer id and original image match.
        if (!_pointer.HasValue) return;
        float width = _image!.rectTransform.rect.width;
        float delta = (x - _lastX) / width * 360f;
        _lastX = x;
        if (!Finite(delta)) { Cancel(); return; }
        _yaw = Mathf.Repeat(_yaw + delta + 180f, 360f) - 180f;
        eventData.eligibleForClick = false;
    }

    public void OnPointerUp(PointerEventData eventData) { if (_pointer == eventData.pointerId) Cancel(); }
    public void OnEndDrag(PointerEventData eventData) { if (_pointer == eventData.pointerId) Cancel(); }
    public void OnPointerClick(PointerEventData eventData) { }

    private bool Visible() => _image != null && _image.color.a > 0f && _image.canvas != null
        && _image.canvas.isActiveAndEnabled && !_image.canvasRenderer.cull
        && _image.canvasRenderer.GetInheritedAlpha() > 0f;

    private bool TryLocalX(PointerEventData data, out float x)
    {
        x = 0f;
        if (_image == null || _image.rectTransform.rect.width <= 1e-5f) return false;
        Camera camera = data.pressEventCamera != null ? data.pressEventCamera : data.enterEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_image.rectTransform, data.position,
            camera, out Vector2 local) || !Finite(local.x)) return false;
        x = local.x;
        return true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private void Cancel() { _pointer = null; _pressRaycaster = null; }

    private void Update()
    {
        // Idle previews do not poll native identity. A captured pointer alone
        // needs the per-frame lifetime guard, including a native UI lock.
        if (!_pointer.HasValue) return;
        if (!Refresh() || !Visible() || (_pressRaycaster != null && !_pressRaycaster.isActiveAndEnabled)) Cancel();
    }

    private void OnEnable() { if (_configured) Refresh(); }
    private void OnDisable() { Cancel(); ReleaseRaycast(); }
    private void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
    private void OnApplicationPause(bool paused) { if (paused) Cancel(); }
    private void OnDestroy()
    {
        Cancel(); ReleaseRaycast(); MenuDepthCharacterRotation.Forget(_image, this);
        _readyIdentity = null; _manager = null; _owner = null; _nativeWindow = null; _identity = null;
    }

    internal void Retire()
    {
        Cancel(); ReleaseRaycast(); _readyIdentity = null; _manager = null; _owner = null; _nativeWindow = null; _identity = null;
        _configured = false; enabled = false;
    }
}
