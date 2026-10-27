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
internal sealed class MenuDepthCardPlane : MonoBehaviour, IMenuDepthOcclusionSource, IMenuDepthDirectSource
{
    private MonoBehaviour? _owner;
    private RectTransform? _card;
    private Transform? _window;
    private MenuDepthCardPlane? _parentPlane;
    private IReadOnlyList<Graphic> _backgrounds = Array.Empty<Graphic>();
    private Vector3 _position, _scale;
    private bool _borrowed, _configured;
    private const float DepthWidthRatio = .035f;

    public Graphic Graphic => Printed() ?? _backgrounds[0];
    public IReadOnlyList<Graphic> DepthGraphics => _backgrounds;
    public bool IsReady => _configured && isActiveAndEnabled && _owner != null && _owner.gameObject.activeInHierarchy
        && _card != null && _card.gameObject.activeInHierarchy && _card.rect.width > 1e-5f && Printed() != null && Eligible();

    internal void Configure(MonoBehaviour owner, RectTransform card, IReadOnlyList<Graphic> backgrounds, Transform? window)
    {
        _owner = owner; _card = card; _backgrounds = backgrounds; _configured = true;
        Rebind(window, backgrounds); MenuDepthSources.Register(this);
    }

    internal void Rebind(Transform? window, IReadOnlyList<Graphic> backgrounds)
    {
        _window = window;
        if (backgrounds.Count != 0) _backgrounds = backgrounds;
        RebindParent();
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
        if (!IsReady || _card == null || (_parentPlane != null && _parentPlane.IsReady)) return;
        float xScale = _card.TransformVector(Vector3.right).magnitude;
        float yScale = _card.TransformVector(Vector3.up).magnitude;
        float zScale = _card.TransformVector(Vector3.forward).magnitude;
        if (!MenuDepthProjection.Finite(new Vector3(xScale, yScale, zScale))
            || Mathf.Min(xScale, Mathf.Min(yScale, zScale)) <= 1e-8f) return;
        // Native subview fitting scales XY while retaining its original Z scale.
        // Express relief in physical card widths even beneath that original fit.
        float depth = -Mathf.Abs(_card.rect.width) * DepthWidthRatio * xScale / zScale;
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
        if (!_borrowed) return;
        _borrowed = false;
        if (_card != null) { _card.localPosition = _position; _card.localScale = _scale; }
    }

    private void OnEnable()
    { if (_configured) MenuDepthSources.Register(this); }
    private void OnDisable()
    { AfterCapture(); MenuDepthSources.Unregister(this); }
    private void OnDestroy()
    { AfterCapture(); MenuDepthSources.Unregister(this); }
}
