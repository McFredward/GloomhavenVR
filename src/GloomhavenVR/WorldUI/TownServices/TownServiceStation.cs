using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A cosmetic station. It never owns a native transaction or continuation.</summary>
internal sealed class TownServiceStation : IDisposable
{
    private readonly GameObject _root;
    private readonly Animation? _animation;
    private readonly byte _service;
    private readonly TownServiceLighting _lighting;
    private readonly TownServiceDecor _decor;
    private Transform? _room;
    private Vector3 _center;
    private float _scale;
    private bool _placed;
    private readonly Renderer[] _renderers;
    private readonly MaterialPropertyBlock _properties = new();
    private static readonly int VisibilityId = Shader.PropertyToID("_TownVisibility");
    private float _visibility = -1f;
    internal Transform Root => _root.transform;
    internal bool IsReady => _decor.Ready;
    internal Transform InteractionAnchor { get; }
    internal float GreetingDuration => _animation != null && _animation["Greeting"] != null
        ? _animation["Greeting"].length : 0f;

    private TownServiceStation(GameObject root, byte service, Vector3 center, float scale)
    {
        _root = root;
        _service = service;
        _center = center;
        _scale = scale;
        _lighting = new TownServiceLighting(root.transform, service);
        try { _decor = new TownServiceDecor(root.transform, service, _lighting); }
        catch { _lighting.Dispose(); throw; }
        InteractionAnchor = root.transform.Find("InteractionAnchor")
            ?? throw new InvalidOperationException("Town station has no InteractionAnchor");
        _animation = root.GetComponentInChildren<Animation>(true);
        _renderers = root.GetComponentsInChildren<Renderer>(true);
    }

    internal static TownServiceStation? Create(byte service, Vector3 center, float scale)
    {
        string name = service == 1 ? "townmerchant" : service == 2 ? "townpriestess" : "townenchantress";
        GameObject? prefab = TownServiceAssets.Prefab(name);
        if (prefab == null) return null;
        GameObject? root = null;
        try
        {
            root = UnityEngine.Object.Instantiate(prefab);
            root.name = "GloomhavenVR.TownService." + service;
            if (!TownServicePlacement.TryResolve(service, center, scale, out Vector3 position, out Quaternion rotation))
            { UnityEngine.Object.Destroy(root); return null; }
            root.transform.SetPositionAndRotation(position, rotation);
            root.transform.localScale = Vector3.one * scale;
            // Decorative bodies and furniture must not intercept the laser or hand election.
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = VRLayers.ModLayer;
            return new TownServiceStation(root, service, center, scale);
        }
        catch
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    internal void SetVisibility(float value)
    {
        if (_visibility == value) return;
        _visibility = value;
        _lighting.SetVisibility(value);
        _decor.SetVisibility(value);
        foreach (Renderer renderer in _renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(_properties);
            _properties.SetFloat(VisibilityId, value);
            renderer.SetPropertyBlock(_properties);
        }
    }

    /// <summary>Only the elected author may update pose; observers retain the published pose.</summary>
    internal void RefreshEnvironment(bool authorPose)
    {
        Transform? room = SkyAlternative.PlacedRoomRoot;
        bool changed = !_placed || room != _room;
        if (authorPose && MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale))
        {
            changed |= center != _center || scale != _scale;
            if (changed && TownServicePlacement.TryResolve(_service, center, scale, out Vector3 position, out Quaternion rotation))
            {
                Root.SetPositionAndRotation(position, rotation);
                Root.localScale = Vector3.one * scale;
                _center = center; _scale = scale;
            }
        }
        if (changed) { _lighting.Refresh(Root); _room = room; _placed = true; }
        _decor.Tick();
    }

    internal void Sample(string clip, float seconds)
    {
        _decor.SetClock(seconds);
        if (_animation == null) return;
        AnimationState? state = _animation[clip];
        if (state == null) return;
        // Explicit sampling lets the shared cosmetic author supply the same phase to observers.
        _animation.Stop();
        state.enabled = true;
        state.weight = 1f;
        state.time = seconds;
        _animation.Sample();
        state.enabled = false;
    }

    public void Dispose()
    {
        _decor.Dispose();
        _lighting.Dispose();
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }
}
