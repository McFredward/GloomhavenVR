using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Capture marker: the original card's entire transform subtree is the
/// visual surface, including TMP text, generated action layouts and native effects.
/// No original layout anchors, size, callbacks, graphics or materials are replaced.
/// The temporary root pose is restored before native input/animation resumes.
/// </summary>
[DisallowMultipleComponent]
internal sealed class MenuDepthCardPlane : MonoBehaviour, IMenuDepthOcclusionSource, IMenuDepthDirectSource, IMenuDepthHeadBoundsSource, IMenuDepthRaycastSource
{
    private MonoBehaviour? _owner;
    private RectTransform? _card;
    private Transform? _window;
    private MenuDepthCardPlane? _parentPlane;
    private IReadOnlyList<Graphic> _backgrounds = Array.Empty<Graphic>();
    private Vector3 _position, _scale;
    private bool _borrowed, _configured, _headCapture;
    private Transform? _headParent;
    private Canvas? _headNativeCanvas;
    private Vector3 _headWorldEye;
    private const float DepthWidthRatio = .056f;
    private const float ItemDepthWidthRatio = .104f;
    private const float FullDepthWidthRatio = .104f;
    private float _depthWidthRatio;
    private readonly List<(Image Image, MenuDepthGraphic Surface)> _depthSurfaces = new(6);

    internal bool OwnsDepthGraphic(Graphic graphic)
    {
        if (_card == null || graphic == null || !graphic.transform.IsChildOf(_card)) return false;
        for (int i = 0; i < _backgrounds.Count; i++) if (ReferenceEquals(_backgrounds[i], graphic)) return true;
        return false;
    }
    internal bool HasHeadLease => MenuDepthHeadRaster.Active && _headCapture && isActiveAndEnabled
        && _owner != null && _owner.gameObject.activeInHierarchy && _card != null && _card.gameObject.activeInHierarchy
        && _card.parent == _headParent && Printed() != null
        && (Eligible() || (_headNativeCanvas != null && _headNativeCanvas.isActiveAndEnabled
            && _card.IsChildOf(_headNativeCanvas.transform)))
        && (_borrowed || (_parentPlane != null && _parentPlane._card != null
            && _card.IsChildOf(_parentPlane._card) && _parentPlane.HasHeadLease));
    internal Vector3 HeadWorldEye => _headWorldEye;

    public bool TryGetHeadBounds(MenuDepthEye eye, out Rect clipBounds)
    {
        clipBounds = default;
        if (!IsReady || _card == null) return false;
        Rect rect = _card.rect;
        float xScale = _card.TransformVector(Vector3.right).magnitude;
        float zScale = _card.TransformVector(Vector3.forward).magnitude;
        if (!(zScale > 1e-8f)) return false;
        float depth = _borrowed || (_parentPlane != null && _parentPlane.IsReady) ? 0f
            : -Mathf.Abs(rect.width) * _depthWidthRatio * xScale / zScale;
        var bounds = new Bounds(new Vector3(rect.center.x, rect.center.y, depth), new Vector3(rect.width, rect.height, 0f));
        clipBounds = MenuDepthHeadRaster.ClipBounds(eye.WorldToHeadClip * _card.localToWorldMatrix, bounds);
        return true;
    }

    public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, out MenuDepthRayHit hit)
    {
        hit = default;
        if (!IsReady || _card == null) return false;
        // Nested native card roots share their outer card's physical plane.
        // Derive it from the original pose even if a Canvas batch currently
        // holds the temporary capture pose; a pointer must never depend on eye.
        MenuDepthCardPlane plane = this;
        while (plane._parentPlane != null && plane._parentPlane.IsReady) plane = plane._parentPlane;
        RectTransform root = plane._card!;
        Matrix4x4 native = root.parent != null ? root.parent.localToWorldMatrix : Matrix4x4.identity;
        native *= Matrix4x4.TRS(plane._borrowed ? plane._position : root.localPosition,
            root.localRotation, plane._borrowed ? plane._scale : root.localScale);
        float xScale = native.MultiplyVector(Vector3.right).magnitude;
        float zScale = native.MultiplyVector(Vector3.forward).magnitude;
        if (!(xScale > 1e-8f) || !(zScale > 1e-8f)) return false;
        float depth = -Mathf.Abs(root.rect.width) * plane._depthWidthRatio * xScale / zScale;
        Matrix4x4 physical = native * Matrix4x4.Translate(new Vector3(0f, 0f, depth));
        if (plane != this) physical *= root.worldToLocalMatrix * _card.localToWorldMatrix;
        Matrix4x4 inverse = physical.inverse;
        Vector3 localOrigin = inverse.MultiplyPoint3x4(origin), localDirection = inverse.MultiplyVector(direction);
        if (Mathf.Abs(localDirection.z) <= 1e-8f) return false;
        float distance = -localOrigin.z / localDirection.z;
        if (!(distance > 1e-6f) || !(distance < maxDistance)) return false;
        Vector3 local = localOrigin + localDirection * distance;
        if (!_card.rect.Contains(new Vector2(local.x, local.y))) return false;
        bool ink = false;
        for (int i = 0; i < _depthSurfaces.Count; i++)
        {
            var surface = _depthSurfaces[i];
            if (surface.Image == null || surface.Surface == null || !surface.Image.isActiveAndEnabled
                || surface.Image.canvasRenderer.GetInheritedAlpha() <= 0f) continue;
            Matrix4x4 relative = _card.worldToLocalMatrix * surface.Image.transform.localToWorldMatrix;
            if (!surface.Surface.RayContainsLocal(relative.inverse.MultiplyPoint3x4(new Vector3(local.x, local.y, 0f)))) continue;
            ink = true; break;
        }
        if (!ink) return false;
        Matrix4x4 original = plane == this ? native : native * root.worldToLocalMatrix * _card.localToWorldMatrix;
        hit = new MenuDepthRayHit(Graphic, origin + direction * distance,
            original.MultiplyPoint3x4(new Vector3(local.x, local.y, 0f)), distance, nativeRoot: _card);
        return true;
    }

    private void RebindDepthSurfaces()
    {
        bool same = _backgrounds.Count == _depthSurfaces.Count;
        for (int i = 0; same && i < _depthSurfaces.Count; i++)
            same = ReferenceEquals(_backgrounds[i], _depthSurfaces[i].Image) && _depthSurfaces[i].Surface != null;
        if (same)
        {
            for (int i = 0; i < _depthSurfaces.Count; i++)
                if (_depthSurfaces[i].Surface != null) _depthSurfaces[i].Surface.EndPlaneCapture();
            return;
        }
        for (int i = 0; i < _depthSurfaces.Count; i++)
            if (_depthSurfaces[i].Surface != null) _depthSurfaces[i].Surface.ReleasePlaneOcclusion(this);
        _depthSurfaces.Clear();
        for (int i = 0; i < _backgrounds.Count; i++)
            if (_backgrounds[i] is Image image && image != null)
            {
                MenuDepthGraphic surface = image.GetComponent<MenuDepthGraphic>() ?? image.gameObject.AddComponent<MenuDepthGraphic>();
                surface.ConfigurePlaneOcclusion(image, this); _depthSurfaces.Add((image, surface));
            }
    }

    public Graphic Graphic => Printed() ?? _backgrounds[0];
    public Transform RayRoot => _card!;
    public IReadOnlyList<Graphic> DepthGraphics => _backgrounds;
    public bool IsReady => _configured && isActiveAndEnabled && _owner != null && _owner.gameObject.activeInHierarchy
        && _card != null && _card.gameObject.activeInHierarchy && _card.rect.width > 1e-5f && Printed() != null && Eligible();

    internal void Configure(MonoBehaviour owner, RectTransform card, IReadOnlyList<Graphic> backgrounds, Transform? window)
    {
        _owner = owner; _card = card; _backgrounds = backgrounds; _configured = true;
        // Build737 hardware review requests more relief for both ability and
        // item cards. Raise every existing plane by 60% of its accepted depth:
        // overview cards use 5.6%, exact item/full roots 10.4% of physical width.
        // Keep their relative emphasis and native widget geometry unchanged;
        // identity still follows the original typed roots, not hover or names.
        _depthWidthRatio = owner is ItemCardUI ? ItemDepthWidthRatio
            : owner is FullAbilityCard ? FullDepthWidthRatio : DepthWidthRatio;
        Rebind(window, backgrounds); MenuDepthSources.Register(this);
    }

    internal void Rebind(Transform? window, IReadOnlyList<Graphic> backgrounds)
    {
        _window = window;
        if (backgrounds.Count != 0) _backgrounds = backgrounds;
        RebindParent(); RebindDepthSurfaces();
    }

    internal void RebindParent() => _parentPlane = _card != null && _card.parent != null
        ? _card.parent.GetComponentInParent<MenuDepthCardPlane>(true) : null;

    private Graphic? Printed()
    {
        for (int i = 0; i < _backgrounds.Count; i++)
        {
            Graphic graphic = _backgrounds[i];
            if (graphic != null && graphic.isActiveAndEnabled && graphic.color.a > 0f
                && graphic.canvasRenderer.GetInheritedAlpha() > 0f) return graphic;
        }
        return null;
    }

    private bool Eligible()
    {
        if (_card == null) return false;
        Canvas cardCanvas = Graphic.canvas;
        // A physical hand/board/NPC card has its own world canvas. Only an actual
        // enrolled flat window permits world-space UI here; original flat native
        // canvases cover detached item tooltip/confirmation roots as well.
        return (_window != null && _card.IsChildOf(_window))
            || (cardCanvas != null && cardCanvas.rootCanvas != null && cardCanvas.rootCanvas.renderMode != RenderMode.WorldSpace);
    }

    // Native uGUI batches world-space transforms before Camera.onPreCull, just
    // as it retains a character preview's texture identity. Prime the fixed card
    // plane at that existing early boundary; the real head then supplies natural
    // perspective for either eye. Input/animation never retains the borrowed pose.
    public void PrimeDirectCapture(MenuDepthEye samplingEye) => BeforeCapture(samplingEye);
    public void BeforeDirectCapture(MenuDepthEye eye) { if (!_borrowed) BeforeCapture(eye); }
    public void CancelDirectCapture() => AfterCapture();

    public void BeforeCapture(MenuDepthEye eye)
    {
        AfterCapture();
        if (!IsReady || _card == null) return;
        _headCapture = MenuDepthHeadRaster.Active;
        _headWorldEye = eye.WorldPosition;
        _headParent = _card.parent;
        Canvas canvas = Graphic.canvas;
        _headNativeCanvas = canvas != null && canvas.rootCanvas != null
            && canvas.rootCanvas.renderMode != RenderMode.WorldSpace ? canvas.rootCanvas : null;
        RebindDepthSurfaces();
        for (int i = 0; i < _depthSurfaces.Count; i++)
            if (_depthSurfaces[i].Surface != null && _depthSurfaces[i].Image != null)
                _depthSurfaces[i].Surface.ConfigurePlaneOcclusion(_depthSurfaces[i].Image, this);
        if (_parentPlane != null && _parentPlane.IsReady) return;
        float xScale = _card.TransformVector(Vector3.right).magnitude;
        float yScale = _card.TransformVector(Vector3.up).magnitude;
        float zScale = _card.TransformVector(Vector3.forward).magnitude;
        if (!MenuDepthProjection.Finite(new Vector3(xScale, yScale, zScale))
            || Mathf.Min(xScale, Mathf.Min(yScale, zScale)) <= 1e-8f) return;
        // Native subview fitting scales XY while retaining its original Z scale.
        // Express relief in physical card widths even beneath that original fit.
        float depth = -Mathf.Abs(_card.rect.width) * _depthWidthRatio * xScale / zScale;
        if (float.IsNaN(depth) || float.IsInfinity(depth)) return;
        float factor = 1f;
        Vector3 offset = new(0f, 0f, depth);
        if (eye.ProjectToGraphicPlane)
        {
            Vector3 localEye = _card.InverseTransformPoint(eye.WorldPosition);
            float denominator = localEye.z - depth;
            factor = localEye.z / denominator;
            if (!MenuDepthProjection.Finite(localEye) || Mathf.Abs(denominator) <= 1e-5f
                || float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f)
                throw new InvalidOperationException("The original raised card plane intersects its capture eye.");
            offset.x = localEye.x * (1f - factor); offset.y = localEye.y * (1f - factor);
        }
        _position = _card.localPosition; _scale = _card.localScale; _borrowed = true;
        // Retain the raised Z even in an orthographic capture. Only XY receives
        // its affine eye projection; common character/UI composition consumes the
        // actual native background graphics at this same physical card plane.
        _card.localPosition = _position + _card.localRotation * Vector3.Scale(offset, _scale);
        _card.localScale = Vector3.Scale(_scale, new Vector3(factor, factor, 1f));
    }

    public void AfterCapture()
    {
        _headCapture = false;
        _headParent = null; _headNativeCanvas = null;
        for (int i = 0; i < _depthSurfaces.Count; i++)
            if (_depthSurfaces[i].Surface != null) _depthSurfaces[i].Surface.EndPlaneCapture();
        if (!_borrowed) return;
        _borrowed = false;
        if (_card != null) { _card.localPosition = _position; _card.localScale = _scale; }
    }

    private void OnEnable()
    { if (_configured) MenuDepthSources.Register(this); }
    private void OnDisable()
    {
        AfterCapture(); MenuDepthSources.Unregister(this);
        for (int i = 0; i < _depthSurfaces.Count; i++)
            if (_depthSurfaces[i].Surface != null) _depthSurfaces[i].Surface.ReleasePlaneOcclusion(this);
    }
    private void OnDestroy()
    {
        AfterCapture(); MenuDepthSources.Unregister(this);
        for (int i = 0; i < _depthSurfaces.Count; i++)
            if (_depthSurfaces[i].Surface != null) _depthSurfaces[i].Surface.ReleasePlaneOcclusion(this);
    }
}
