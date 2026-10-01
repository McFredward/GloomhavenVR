using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.Rendering;

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
    private static Light? _mainKey;
    private static int _users;
    private readonly Light _stand;
    private readonly Light? _second;
    private readonly float _power;
    private readonly List<Renderer> _surfaces = new();
    private readonly List<Renderer> _boundSurfaces = new();
    private readonly List<Material> _materials = new(), _boundMaterials = new(), _surfaceMaterials = new();
    private readonly MaterialPropertyBlock _surfaceProperties = new();
    private TownAmbientProbe _boundProbe;
    private bool _hasBoundProbe;
    private Vector3 _boundKeyDirection, _boundKeyColour;
    private bool _boundSharedKey, _boundLinear;
    private static readonly int SharedAmbientId = Shader.PropertyToID("_TownSharedAmbient");
    private static readonly int SharedKeyId = Shader.PropertyToID("_TownSharedKey");
    private static readonly int KeyDirectionId = Shader.PropertyToID("_TownKeyDirection");
    private static readonly int KeyColourId = Shader.PropertyToID("_TownKeyColour");
    private static readonly int[] AmbientIds = { Shader.PropertyToID("_TownAmbientAr"), Shader.PropertyToID("_TownAmbientAg"),
        Shader.PropertyToID("_TownAmbientAb"), Shader.PropertyToID("_TownAmbientBr"), Shader.PropertyToID("_TownAmbientBg"),
        Shader.PropertyToID("_TownAmbientBb"), Shader.PropertyToID("_TownAmbientC") };
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

    private static bool EligibleKey(Light? light) => light != null && light.isActiveAndEnabled
        && light.type == LightType.Directional && light.intensity > 0f
        && (light.cullingMask & (1 << VRLayers.ModLayer)) != 0;
    private static void SelectKey()
    {
        Light? sun = RenderSettings.sun;
        if (EligibleKey(sun)) { _mainKey = sun; return; }
        _mainKey = EligibleKey(_roomLight) ? _roomLight : null;
        // Environment changes are rare. Cache the eligible directional source;
        // do not allocate a scene-light array on every presentation sample.
        foreach (Light light in Light.GetLights(LightType.Directional, VRLayers.ModLayer))
            if (EligibleKey(light) && (_mainKey == null || light.intensity > _mainKey.intensity)) _mainKey = light;
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
        if (authorEnvironment) SelectKey();
    }

    /// <summary>Sample the owned fill, native ambient probe and eligible main key in the resident frame.</summary>
    internal static void SampleEnvironment(Transform frame, ref GloomhavenVR.Net.TownActivityState state)
    {
        state.HasEnvironmentLight = true;
        state.EnvironmentLightDirection = _roomLight != null
            ? frame.InverseTransformDirection(_roomLight.transform.forward) : new Vector3(0f, 0f, 1f);
        Color colour = _roomLight != null ? _roomLight.color : Color.white;
        state.EnvironmentLightColour = new Vector3(colour.r, colour.g, colour.b);
        state.EnvironmentLightIntensity = _roomLight != null && _roomLight.enabled ? _roomLight.intensity : 0f;
        // The canonical parchment frame is world-aligned (Population.Prepare), so
        // these coefficients use the same normal basis on every peer. Scenery's
        // RenderSettings and probes are never changed by this owned surface binding.
        SphericalHarmonicsL2 ambient = RenderSettings.ambientProbe;
        state.HasAmbientProbe = true;
        for (int n = 0; n < 9; n++)
            state.AmbientProbe.Set(n, new Vector3(ambient[0, n], ambient[1, n], ambient[2, n]));
        // Unity prioritizes an explicitly selected eligible sun. Preserve native
        // lighting rather than substituting only our owned room fill in the shader.
        Light? key = EligibleKey(RenderSettings.sun) ? RenderSettings.sun
            : EligibleKey(_mainKey) ? _mainKey : EligibleKey(_roomLight) ? _roomLight : null;
        state.HasAuthoredKey = true;
        state.KeyDirection = key != null ? frame.InverseTransformDirection(-key.transform.forward) : new Vector3(0f, 0f, 1f);
        Color keyColour = key != null ? key.color : Color.black;
        state.KeyColour = new Vector3(keyColour.r, keyColour.g, keyColour.b);
        state.KeyIntensity = key != null ? key.intensity : 0f;
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

    internal void BindEnvironment(Transform root, Transform frame, in TownActivityState state)
    {
        if (!state.HasAmbientProbe) return;
        // Native decorations finish asynchronously during station preparation. This
        // non-allocating census also includes their new surfaces. An unchanged probe
        // and renderer population require no property-block writes.
        _surfaces.Clear(); root.GetComponentsInChildren(true, _surfaces);
        _materials.Clear();
        foreach (Renderer surface in _surfaces)
        {
            if (surface == null) continue;
            surface.GetSharedMaterials(_surfaceMaterials);
            foreach (Material material in _surfaceMaterials) _materials.Add(material);
        }
        Vector3 direction = frame.TransformDirection(state.KeyDirection);
        Color keyColour = new(state.KeyColour.x, state.KeyColour.y, state.KeyColour.z);
        bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        if (linear) keyColour = keyColour.linear;
        Vector3 colour = new(keyColour.r * state.KeyIntensity, keyColour.g * state.KeyIntensity, keyColour.b * state.KeyIntensity);
        bool sameSurfaces = _boundSurfaces.Count == _surfaces.Count;
        for (int n = 0; sameSurfaces && n < _surfaces.Count; n++) sameSurfaces &= ReferenceEquals(_boundSurfaces[n], _surfaces[n]);
        sameSurfaces &= _boundMaterials.Count == _materials.Count;
        for (int n = 0; sameSurfaces && n < _materials.Count; n++) sameSurfaces &= ReferenceEquals(_boundMaterials[n], _materials[n]);
        if (_hasBoundProbe && sameSurfaces && _boundProbe.Same(in state.AmbientProbe)
            && _boundKeyDirection == direction && _boundKeyColour == colour
            && _boundSharedKey == state.HasAuthoredKey && _boundLinear == linear) return;
        _boundProbe = state.AmbientProbe; _hasBoundProbe = true;
        _boundKeyDirection = direction; _boundKeyColour = colour; _boundSharedKey = state.HasAuthoredKey; _boundLinear = linear;
        _boundSurfaces.Clear(); _boundSurfaces.AddRange(_surfaces);
        _boundMaterials.Clear(); _boundMaterials.AddRange(_materials);
        foreach (Renderer surface in _surfaces)
        {
            if (surface == null) continue;
            surface.GetSharedMaterials(_surfaceMaterials);
            bool supported = false;
            foreach (Material material in _surfaceMaterials)
            {
                if (!OwnedEnvironmentMaterial(material)) continue;
                supported = true;
                if (surface is MeshRenderer) BindMaterial(material, state.HasAuthoredKey, direction, colour, in state.AmbientProbe);
            }
            // Original mesh snapshots reject undeclared property blocks. Their
            // owned shader uniforms travel through the existing exact material
            // capture instead. Keep material references held by the cabinet and
            // decoration controllers so visibility/feedback updates still apply.
            if (!supported || surface is MeshRenderer) continue;
            surface.GetPropertyBlock(_surfaceProperties);
            _surfaceProperties.SetFloat(SharedAmbientId, 1f);
            _surfaceProperties.SetFloat(SharedKeyId, state.HasAuthoredKey ? 1f : 0f);
            _surfaceProperties.SetVector(KeyDirectionId, new Vector4(direction.x, direction.y, direction.z, 0f));
            _surfaceProperties.SetVector(KeyColourId, new Vector4(colour.x, colour.y, colour.z, 1f));
            // Unity's native L2 coefficients already carry the polynomial factors.
            // Preserve its L0/L1 and every L2 term, rather than inventing a flat fill.
            TownAmbientProbe p = state.AmbientProbe;
            _surfaceProperties.SetVector(AmbientIds[0], new Vector4(p.C3.x, p.C1.x, p.C2.x, p.C0.x - p.C6.x));
            _surfaceProperties.SetVector(AmbientIds[1], new Vector4(p.C3.y, p.C1.y, p.C2.y, p.C0.y - p.C6.y));
            _surfaceProperties.SetVector(AmbientIds[2], new Vector4(p.C3.z, p.C1.z, p.C2.z, p.C0.z - p.C6.z));
            _surfaceProperties.SetVector(AmbientIds[3], new Vector4(p.C4.x, p.C5.x, p.C6.x * 3f, p.C7.x));
            _surfaceProperties.SetVector(AmbientIds[4], new Vector4(p.C4.y, p.C5.y, p.C6.y * 3f, p.C7.y));
            _surfaceProperties.SetVector(AmbientIds[5], new Vector4(p.C4.z, p.C5.z, p.C6.z * 3f, p.C7.z));
            _surfaceProperties.SetVector(AmbientIds[6], new Vector4(p.C8.x, p.C8.y, p.C8.z, 1f));
            surface.SetPropertyBlock(_surfaceProperties);
        }
    }

    private static bool OwnedEnvironmentMaterial(Material? material) => material != null && material.shader != null
        && (material.shader.name == "GloomhavenVR/TownNpc" || material.shader.name == "GloomhavenVR/TownEye"
            || material.shader.name == "GloomhavenVR/TownCornea")
        && material.HasProperty(SharedAmbientId) && material.HasProperty(SharedKeyId);

    private static void BindMaterial(Material material, bool sharedKey, Vector3 direction, Vector3 colour, in TownAmbientProbe p)
    {
        material.SetFloat(SharedAmbientId, 1f); material.SetFloat(SharedKeyId, sharedKey ? 1f : 0f);
        material.SetVector(KeyDirectionId, new Vector4(direction.x, direction.y, direction.z, 0f));
        material.SetVector(KeyColourId, new Vector4(colour.x, colour.y, colour.z, 1f));
        material.SetVector(AmbientIds[0], new Vector4(p.C3.x, p.C1.x, p.C2.x, p.C0.x - p.C6.x));
        material.SetVector(AmbientIds[1], new Vector4(p.C3.y, p.C1.y, p.C2.y, p.C0.y - p.C6.y));
        material.SetVector(AmbientIds[2], new Vector4(p.C3.z, p.C1.z, p.C2.z, p.C0.z - p.C6.z));
        material.SetVector(AmbientIds[3], new Vector4(p.C4.x, p.C5.x, p.C6.x * 3f, p.C7.x));
        material.SetVector(AmbientIds[4], new Vector4(p.C4.y, p.C5.y, p.C6.y * 3f, p.C7.y));
        material.SetVector(AmbientIds[5], new Vector4(p.C4.z, p.C5.z, p.C6.z * 3f, p.C7.z));
        material.SetVector(AmbientIds[6], new Vector4(p.C8.x, p.C8.y, p.C8.z, 1f));
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
            _mainKey = null;
        }
    }
}
