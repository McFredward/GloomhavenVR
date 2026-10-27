using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Empty capture marker: the original card's entire transform subtree is the
/// visual surface, including TMP text, generated action layouts and native effects.
/// No original layout anchors, size, callbacks, graphics or materials are replaced.
/// The temporary root pose is restored before native input/animation resumes.
/// </summary>
[DisallowMultipleComponent]
internal sealed class MenuDepthCardPlane : MaskableGraphic, IMenuDepthOcclusionSource
{
    private MonoBehaviour? _owner;
    private RectTransform? _card;
    private Transform? _window;
    private IReadOnlyList<Graphic> _backgrounds = Array.Empty<Graphic>();
    private Vector3 _position, _scale;
    private bool _borrowed, _configured;
    private const float DepthWidthRatio = .035f;

    public Graphic Graphic => this;
    public IReadOnlyList<Graphic> DepthGraphics => _backgrounds;
    public bool IsReady => _configured && IsActive() && _owner != null && _owner.isActiveAndEnabled
        && _card != null && _card.gameObject.activeInHierarchy && _card.rect.width > 1e-5f && Eligible();

    internal void Configure(MonoBehaviour owner, RectTransform card, IReadOnlyList<Graphic> backgrounds, Transform? window)
    {
        _owner = owner; _card = card; _backgrounds = backgrounds; _configured = true;
        raycastTarget = false; color = Color.white;
        Rebind(window); MenuDepthSources.Register(this);
    }

    internal void Rebind(Transform? window)
    {
        _window = window;
        if (_card != null) gameObject.layer = _card.gameObject.layer;
    }

    private bool Eligible()
    {
        if (_card == null) return false;
        Canvas cardCanvas = canvas;
        // A physical hand/board/NPC card has its own world canvas. Only an actual
        // enrolled flat window permits world-space UI here; original flat native
        // canvases cover detached item tooltip/confirmation roots as well.
        return (_window != null && _card.IsChildOf(_window))
            || (cardCanvas != null && cardCanvas.rootCanvas != null && cardCanvas.rootCanvas.renderMode != RenderMode.WorldSpace);
    }

    public void BeforeCapture(MenuDepthEye eye)
    {
        AfterCapture();
        if (!IsReady || _card == null) return;
        Vector3 localEye = _card.InverseTransformPoint(eye.WorldPosition);
        float depth = -Mathf.Abs(_card.rect.width) * DepthWidthRatio;
        float factor = 1f;
        Vector3 offset = new(0f, 0f, depth);
        if (eye.ProjectToGraphicPlane)
        {
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
        if (!_borrowed) return;
        _borrowed = false;
        if (_card != null) { _card.localPosition = _position; _card.localScale = _scale; }
    }

    protected override void OnPopulateMesh(VertexHelper vertices) => vertices.Clear();
    protected override void OnEnable()
    { base.OnEnable(); if (_configured) MenuDepthSources.Register(this); }
    protected override void OnDisable()
    { AfterCapture(); MenuDepthSources.Unregister(this); base.OnDisable(); }
    protected override void OnDestroy()
    { AfterCapture(); MenuDepthSources.Unregister(this); base.OnDestroy(); }
}
