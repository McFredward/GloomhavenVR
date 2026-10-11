using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Actual town practicals in fixed shader slots, independent of Unity's per-renderer ranking.</summary>
internal static class TownServiceLightList
{
    // Six resident lamps plus two per visitor fit with ample headroom. Entries retain
    // their slot until disposal; changing a skin's bounds cannot elect another lamp.
    internal const int Capacity = 32;
    private static readonly Light?[] Lights = new Light?[Capacity];
    private static readonly Vector4[] Positions = new Vector4[Capacity];
    private static readonly Vector4[] Colours = new Vector4[Capacity];
    private static readonly int PositionId = Shader.PropertyToID("_TownPracticalPositions");
    private static readonly int ColourId = Shader.PropertyToID("_TownPracticalColours");
    private static readonly int CountId = Shader.PropertyToID("_TownPracticalCount");
    private static bool _subscribed, _overflowReported;

    internal static void Claim(Light light)
    {
        for (int i = 0; i < Capacity; i++) if (ReferenceEquals(Lights[i], light)) return;
        for (int i = 0; i < Capacity; i++)
        {
            if (Lights[i] != null) continue;
            Lights[i] = light;
            if (!_subscribed) { Camera.onPreCull += BeforeCamera; _subscribed = true; }
            return;
        }
        if (_overflowReported) return;
        _overflowReported = true;
        VRLog.Warn("TownServices", "Town practical-light capacity exceeded; additional lamp has no town shader contribution.");
    }

    internal static void Forget(Light light)
    {
        for (int i = 0; i < Capacity; i++)
            if (ReferenceEquals(Lights[i], light)) { Lights[i] = null; Positions[i] = Colours[i] = Vector4.zero; }
        Bind();
    }

    private static void BeforeCamera(Camera camera) => Bind();

    // Camera.onPreCull runs after the cosmetic owner/observer and workspace LateUpdate.
    // Updating this small fixed array also supports multiple camera renders in one frame
    // without stale poses. No LINQ, temporary arrays, materials or property-block writes.
    internal static void Bind()
    {
        int count = 0;
        bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        for (int i = 0; i < Capacity; i++)
        {
            Light? light = Lights[i];
            Positions[i] = Colours[i] = Vector4.zero;
            if (light == null) { Lights[i] = null; continue; }
            count = i + 1;
            if (!light.isActiveAndEnabled || light.type != LightType.Point || light.intensity <= 0f
                || light.range <= 0f || (light.cullingMask & (1 << VRLayers.ModLayer)) == 0) continue;
            Vector3 position = light.transform.position;
            float inverseRange = 1f / light.range;
            Positions[i] = new Vector4(position.x, position.y, position.z, inverseRange * inverseRange);
            Color colour = linear ? light.color.linear : light.color;
            colour *= light.intensity;
            Colours[i] = new Vector4(colour.r, colour.g, colour.b, 0f);
        }
        Shader.SetGlobalVectorArray(PositionId, Positions);
        Shader.SetGlobalVectorArray(ColourId, Colours);
        Shader.SetGlobalInt(CountId, count);
        if (count != 0 || !_subscribed) return;
        Camera.onPreCull -= BeforeCamera;
        _subscribed = false;
        _overflowReported = false;
    }
}
