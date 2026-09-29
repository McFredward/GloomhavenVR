using System;
using System.Globalization;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>One owned practical per actual workspace lantern. Frozen inactive templates
/// never allocate lights; owner and observer sample the same mirrored material opacity.</summary>
internal sealed class TownServiceWorkspacePractical : MonoBehaviour
{
    private static readonly int Visibility = Shader.PropertyToID("_TownVisibility");
    private MaterialPropertyBlock? _block;
    private MeshRenderer? _surface;
    private Light? _light;
    private Vector3 _point;
    private float _rangeScale, _power;
    private Color _colour;
    private bool _configured;

    internal static void RebindClone(string address, GameObject clone)
    {
        if (clone == null || !address.StartsWith("decor.", StringComparison.Ordinal)) return;
        int split = address.IndexOf('|');
        if (split >= 0 && split != address.Length - 1) return; // Only the whole prop owns a light.
        string key = split >= 0 ? address.Substring(0, split) : address;
        string[] parts = key.Split('.');
        if (parts.Length != 3 || !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out byte service)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || !TownServiceDecor.TryPractical(service, index, out Vector3 point, out float rangeScale)) return;
        var owner = clone.GetComponent<TownServiceWorkspacePractical>() ?? clone.AddComponent<TownServiceWorkspacePractical>();
        owner._point = point; owner._rangeScale = rangeScale;
        owner._power = TownServiceLighting.PracticalPower(service);
        owner._colour = TownServiceLighting.PracticalColour(service);
        owner._surface = clone.GetComponentInChildren<MeshRenderer>(true);
        owner._configured = true;
        if (owner.isActiveAndEnabled) owner.Activate();
    }

    private void OnEnable() { if (_configured) Activate(); }
    private void Activate()
    {
        if (_light != null || _surface == null) return;
        _block ??= new MaterialPropertyBlock();
        var obj = new GameObject("Town.WorkspacePractical") { layer = VRLayers.ModLayer };
        // Stay outside the published transform tree: an extra helper child would
        // change the owner topology without existing in the frozen native template.
        obj.transform.position = transform.TransformPoint(_point);
        _light = obj.AddComponent<Light>();
        _light.type = LightType.Point; _light.renderMode = LightRenderMode.ForceVertex;
        _light.cullingMask = 1 << VRLayers.ModLayer; _light.shadows = LightShadows.None;
        _light.color = _colour; _light.intensity = 0f;
        TownServiceLighting.ClaimPractical(_light);
        LateUpdate();
    }
    private void LateUpdate()
    {
        if (_light == null) return;
        float visibility = 0f;
        if (_surface != null && _surface.enabled && _surface.gameObject.activeInHierarchy)
        {
            // A network material update may replace sharedMaterial. Cache the renderer,
            // not yesterday's material, and reuse the block without allocating arrays.
            Material material = _surface.sharedMaterial;
            if (material != null) visibility = material.HasProperty(Visibility) ? material.GetFloat(Visibility) : 1f;
            _surface.GetPropertyBlock(_block!);
            if (_block!.HasFloat(Visibility)) visibility = _block.GetFloat(Visibility);
        }
        _light.transform.position = transform.TransformPoint(_point);
        _light.intensity = _power * Mathf.Clamp01(visibility);
        _light.range = 2.65f * _rangeScale * Mathf.Abs(transform.lossyScale.x);
    }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
    private void Release()
    {
        if (_light is null) return;
        if (_light != null) { _light.intensity = 0f; _light.gameObject.SetActive(false); }
        TownServiceLighting.ForgetPractical(_light!);
        if (_light != null) UnityEngine.Object.Destroy(_light.gameObject);
        _light = null;
    }
}
