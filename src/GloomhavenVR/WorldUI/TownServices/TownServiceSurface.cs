using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Moves an original logical widget section, with its original input and controllers.
/// The conversion owns rollback, including persistent scene membership and native canvas state.</summary>
internal sealed class TownServiceSurface : IDisposable
{
    internal readonly ushort Id;
    internal readonly ConvertedPanel Panel;
    private readonly Vector3 _offset;
    private readonly float _width;
    private readonly GrabbableModal _grab = new();
    private readonly List<CanvasGroup> _nativeGroups = new();
    private readonly List<Transform> _nativeAncestors = new();
    private readonly CanvasGroup _gate;
    private bool _placed, _allowInput = true;
    private readonly Transform? _counterAnchor;
    private float _visibility = 1f, _inheritedAlpha = 1f;

    internal TownServiceSurface(ushort id, RectTransform source, Vector3 offset, float width, Transform? counterAnchor = null)
    {
        Id = id;
        _offset = offset;
        _width = width;
        _counterAnchor = counterAnchor;
        CanvasGroup? ownGroup = source.GetComponent<CanvasGroup>();
        bool inheritGroups = ownGroup == null || !ownGroup.ignoreParentGroups;
        for (Transform? t = source.parent; t != null; t = t.parent)
        {
            _nativeAncestors.Add(t);
            CanvasGroup? group = t.GetComponent<CanvasGroup>();
            if (group != null && inheritGroups)
            {
                _nativeGroups.Add(group);
                if (group.ignoreParentGroups) inheritGroups = false;
            }
            // Follow the logical native window, not its old HUD root. Its world conversion
            // deliberately detaches it from that HUD and preserves its own visibility rules.
            if (t.GetComponent<UnityEngine.UI.UIWindow>() != null) break;
        }
        Panel = CanvasConversion.Convert(source, "TownService." + id, fitContent: false,
            useModLayer: true, transparentBackground: false)
            ?? throw new InvalidOperationException("Native town section could not be converted: " + id);
        // A printed physical card already has its own backing; never add a window plate.
        Panel.MrBackingSuppressed = counterAnchor != null;
        _gate = Panel.HostGo.AddComponent<CanvasGroup>();
        try { if (_counterAnchor == null) _grab.Build(Panel, 1f, "TownService." + id); }
        catch { CanvasConversion.Release(Panel); throw; }
    }

    internal void Tick(Vector3 origin, Quaternion yaw, float scale)
    {
        if (!Panel.IsAlive) return;
        float alpha = 1f;
        bool interactable = true, raycasts = true;
        foreach (Transform ancestor in _nativeAncestors)
            if (ancestor == null || !ancestor.gameObject.activeSelf) { alpha = 0f; interactable = raycasts = false; }
        foreach (CanvasGroup group in _nativeGroups)
        {
            if (group == null || !group.enabled) continue;
            alpha *= group.alpha; interactable &= group.interactable; raycasts &= group.blocksRaycasts;
        }
        _inheritedAlpha = alpha;
        _gate.alpha = alpha * _visibility; _gate.interactable = interactable && _allowInput; _gate.blocksRaycasts = raycasts && _allowInput;
        if (_counterAnchor != null)
        {
            // Physical countertop controls are horizontal objects, not pitched floating windows.
            float pixels = Mathf.Max(1f, Panel.HostRect.rect.width);
            float factor = _width * _counterAnchor.lossyScale.x / pixels / (WorldUIConfig.CanvasScaleMm.Value * .001f);
            CanvasConversion.PlaceHost(Panel, _counterAnchor.TransformPoint(_offset),
                _counterAnchor.rotation * Quaternion.Euler(90f, 0f, 0f), factor);
            return;
        }
        if (!_placed)
        {
            float pixels = Mathf.Max(1f, Panel.HostRect.rect.width);
            float factor = _width / pixels / (WorldUIConfig.CanvasScaleMm.Value * .001f);
            _grab.SetExtraScale(factor);
            _grab.SnapFrameTo(origin + yaw * (_offset * scale), yaw);
            _placed = true;
        }
        _grab.Tick();
    }

    internal void SetVisibility(float value, bool allowInput = true)
    {
        _visibility = Mathf.Clamp01(value); _allowInput = allowInput;
        if (_gate != null)
        {
            _gate.alpha = _inheritedAlpha * _visibility;
            if (!allowInput) _gate.interactable = _gate.blocksRaycasts = false;
        }
    }

    internal bool OwnsGrab(GrabbableModal holder) => Panel.IsAlive && ReferenceEquals(_grab, holder);

    internal void LateTick() { if (_counterAnchor == null) _grab.LateSyncHost(); }

    public void Dispose()
    {
        // Never destroy a root that still contains game-owned UI.
        CanvasConversion.Release(Panel);
        _grab.Destroy();
    }
}
