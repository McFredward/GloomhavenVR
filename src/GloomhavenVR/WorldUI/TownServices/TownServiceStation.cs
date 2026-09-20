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
    private readonly Renderer[] _renderers;
    private readonly MaterialPropertyBlock _properties = new();
    private static readonly int VisibilityId = Shader.PropertyToID("_TownVisibility");
    private float _visibility = -1f;
    internal Transform Root => _root.transform;
    internal Transform InteractionAnchor { get; }

    private TownServiceStation(GameObject root)
    {
        _root = root;
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
            // A parchment frame is common to visitors, unlike each client's camera focus/yaw.
            Vector3 offset = service == 1 ? new Vector3(-1.65f, -.78f, .9f)
                : service == 2 ? new Vector3(1.65f, -.78f, .9f) : new Vector3(0f, -.78f, 1.95f);
            Quaternion readingFrame = Quaternion.identity;
            float floor = center.y - MapRoomSeat.TableTopHeightMeters * scale;
            if (MapRoomDriver.TrySolveSeat(out MapRoomSeat.Seat seat, out _))
            {
                // Services occupy the far half of the authored map reading frame, outside its
                // table and spawn ring. A hardcoded world -X merchant would stand behind the
                // usual Campaign player, whose initial view reads the parchment from -X.
                readingFrame = Quaternion.Euler(0f, seat.YawDegrees, 0f);
                floor = seat.FloorPosition.y;
            }
            Vector3 position = center + readingFrame * offset * scale;
            position.y = floor;
            root.transform.position = position;
            Vector3 inward = center - root.transform.position;
            inward.y = 0f;
            root.transform.rotation = Quaternion.LookRotation(-inward.normalized, Vector3.up);
            root.transform.localScale = Vector3.one * scale;
            // Decorative bodies and furniture must not intercept the laser or hand election.
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = VRLayers.ModLayer;
            return new TownServiceStation(root);
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
        foreach (Renderer renderer in _renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(_properties);
            _properties.SetFloat(VisibilityId, value);
            renderer.SetPropertyBlock(_properties);
        }
    }

    internal void Sample(string clip, float seconds)
    {
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
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }
}
