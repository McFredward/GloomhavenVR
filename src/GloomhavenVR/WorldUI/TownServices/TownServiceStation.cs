using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A cosmetic station. It never owns a native transaction or continuation.</summary>
internal sealed class TownServiceStation : IDisposable
{
    private readonly GameObject _root;
    private readonly Animation? _animation;
    internal Transform Root => _root.transform;
    internal Transform InteractionAnchor { get; }

    private TownServiceStation(GameObject root)
    {
        _root = root;
        InteractionAnchor = root.transform.Find("InteractionAnchor")
            ?? throw new InvalidOperationException("Town station has no InteractionAnchor");
        _animation = root.GetComponentInChildren<Animation>(true);
    }

    internal static TownServiceStation? Create(byte service, Vector3 center, float scale)
    {
        string name = service == 1 ? "townmerchant" : service == 2 ? "townpriestess" : "townenchantress";
        GameObject? prefab = WorldUIAssets.TryLoadPrefab("assets/bundle/townservices/prefabs/" + name + ".prefab");
        if (prefab == null) return null;
        GameObject? root = null;
        try
        {
            root = UnityEngine.Object.Instantiate(prefab);
            root.name = "GloomhavenVR.TownService." + service;
            // A parchment frame is common to visitors, unlike each client's camera focus/yaw.
            Vector3 offset = service == 1 ? new Vector3(-1.65f, -.78f, .1f)
                : service == 2 ? new Vector3(1.65f, -.78f, .1f) : new Vector3(0f, -.78f, 1.65f);
            root.transform.position = center + offset * scale;
            Vector3 inward = center - root.transform.position;
            inward.y = 0f;
            root.transform.rotation = Quaternion.LookRotation(-inward.normalized, Vector3.up);
            root.transform.localScale = Vector3.one * scale;
            // Decorative bodies and furniture must not intercept the laser or hand election.
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            return new TownServiceStation(root);
        }
        catch
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            throw;
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
