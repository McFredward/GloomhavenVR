using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Owned lights for the mod layer; never changes native lights or global ambient.</summary>
internal sealed class TownServiceLighting : IDisposable
{
    // At most one environment light and six practicals for the three live residents.
    // Exact object ownership, not layer/name/type heuristics: native lights keep their policy.
    private static readonly HashSet<Light> Owned = new();
    internal static void ClaimPractical(Light light) { Owned.Add(light); TownServiceLightList.Claim(light); }
    internal static void ForgetPractical(Light light) { Owned.Remove(light); TownServiceLightList.Forget(light); }
    // Calibrated against the real face at its ~0.9m lamp distance. The former1.05
    // disappeared under point falloff in default/MR; no ambient or emission floor is used.
    internal static float PracticalPower(byte service) => 2.6f;
    internal static Color PracticalColour(byte service) => service == 1 ? new Color(1f, .71f, .40f)
        : service == 2 ? new Color(1f, .82f, .60f) : new Color(1f, .74f, .48f);
    internal static bool Owns(Light light) => light != null && Owned.Contains(light);
    private static Light? _roomLight;
    private static int _users;
    private readonly Light _stand;
    private readonly Light? _second;
    private readonly float _power;
    private float _visibility;
    private bool _hasFlame, _hasSecond, _disposed;

    internal TownServiceLighting(Transform root, byte service)
    {
        _users++;
        var lightObject = new GameObject("TownService.PracticalLight");
        lightObject.transform.SetParent(root, false);
        // Position is refined to the original candle's flame after its assets finish loading.
        lightObject.transform.localPosition = new Vector3(-.57f, 1.30f, .16f);
        _stand = lightObject.AddComponent<Light>();
        ClaimPractical(_stand);
        _stand.type = LightType.Point;
        _stand.renderMode = LightRenderMode.ForceVertex;
        _stand.cullingMask = 1 << VRLayers.ModLayer;
        _stand.shadows = LightShadows.None;
        _stand.color = PracticalColour(service);
        _power = PracticalPower(service);
        _stand.intensity = 0f;
        {
            var secondObject = new GameObject("TownService.SecondCandleLight");
            secondObject.transform.SetParent(root, false);
            _second = secondObject.AddComponent<Light>();
            ClaimPractical(_second);
            _second.type = LightType.Point;
            _second.renderMode = LightRenderMode.ForceVertex;
            _second.cullingMask = _stand.cullingMask;
            _second.shadows = LightShadows.None;
            _second.color = _stand.color;
            _second.intensity = 0f;
        }
        Refresh(root);
    }

    internal void SetFlame(Vector3 world, int slot)
    {
        if (slot == 1 && _second != null) { _second.transform.position = world; _hasSecond = true; }
        else { _stand.transform.position = world; _hasFlame = true; }
        SetVisibility(_visibility);
    }
    internal void SetVisibility(float value)
    {
        _visibility = value;
        _stand.intensity = _hasFlame ? _power * value : 0f;
        if (_second != null) _second.intensity = _hasSecond ? _power * value : 0f;
    }

    internal void Refresh(Transform root, bool authorEnvironment = true)
    {
        _stand.range = 2.65f * Mathf.Abs(root.lossyScale.x);
        if (_second != null) _second.range = _stand.range;
        if (_roomLight == null)
        {
            // A scene teardown can destroy a Unity object before its station is disposed.
            // Remove the stale managed reference before replacing that light.
            if (_roomLight is not null) Owned.Remove(_roomLight);
            var obj = new GameObject("TownService.EnvironmentLight");
            _roomLight = obj.AddComponent<Light>();
            Owned.Add(_roomLight);
            _roomLight.type = LightType.Directional;
            _roomLight.cullingMask = 1 << VRLayers.ModLayer;
            _roomLight.shadows = LightShadows.None;
        }
        if (authorEnvironment && SkyAlternative.TryRoomMoonDirection(out Vector3 direction, out Color moon, out _))
        {
            _roomLight.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
            _roomLight.color = moon;
            _roomLight.intensity = .40f;
            _roomLight.enabled = true;
        }
        else if (authorEnvironment)
        {
            // Default/MR retain the native scene's ambient probe; only the visible practical
            // light supplies additional light. Do not invent a white studio fill in darkness.
            _roomLight.intensity = 0f;
            _roomLight.enabled = false;
        }
        SetVisibility(_visibility);
    }

    /// <summary>Sample only the mod-owned fill, in the same frame as all resident poses.</summary>
    internal static void SampleEnvironment(Transform frame, ref GloomhavenVR.Net.TownActivityState state)
    {
        state.HasEnvironmentLight = true;
        state.EnvironmentLightDirection = _roomLight != null
            ? frame.InverseTransformDirection(_roomLight.transform.forward) : new Vector3(0f, 0f, 1f);
        Color colour = _roomLight != null ? _roomLight.color : Color.white;
        state.EnvironmentLightColour = new Vector3(colour.r, colour.g, colour.b);
        state.EnvironmentLightIntensity = _roomLight != null && _roomLight.enabled ? _roomLight.intensity : 0f;
    }

    /// <summary>The observer's room can still have its own scenery; the shared NPC fill cannot.</summary>
    internal static void ApplyEnvironment(Transform frame, in GloomhavenVR.Net.TownActivityState state)
    {
        if (!state.HasEnvironmentLight || _roomLight == null) return;
        _roomLight.transform.rotation = Quaternion.LookRotation(frame.TransformDirection(state.EnvironmentLightDirection), Vector3.up);
        Vector3 colour = state.EnvironmentLightColour;
        _roomLight.color = new Color(colour.x, colour.y, colour.z);
        _roomLight.intensity = state.EnvironmentLightIntensity;
        _roomLight.enabled = state.EnvironmentLightIntensity > 0f;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Remove synchronously, before Unity's deferred Destroy; a same-frame scene sweep
        // must never retain a disposed helper as a live owner.
        ForgetPractical(_stand);
        if (_second is not null) ForgetPractical(_second);
        if (_stand != null) UnityEngine.Object.Destroy(_stand.gameObject);
        if (_second != null) UnityEngine.Object.Destroy(_second.gameObject);
        if (--_users == 0)
        {
            if (_roomLight is not null) Owned.Remove(_roomLight);
            if (_roomLight != null) UnityEngine.Object.Destroy(_roomLight.gameObject);
            _roomLight = null;
        }
    }
}
