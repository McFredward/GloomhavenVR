using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A bounded constellation made from the game's original candle-glow artwork.
/// Its orbit is sampled from the shared performance clock, never a local particle simulation.</summary>
internal sealed class TownServiceArcaneEffect : IDisposable
{
    private readonly Transform _root;
    private readonly Transform[] _sparks = new Transform[24];
    private readonly Transform _core;
    private readonly float _meshScale;
    internal TownServiceArcaneEffect(Transform parent, MeshFilter source, Material[] materials)
    {
        _root = new GameObject("Town.ArcaneConstellation").transform;
        _root.SetParent(parent, false);
        Bounds bounds = source.sharedMesh.bounds;
        _meshScale = 1f / Mathf.Max(.001f, Mathf.Max(bounds.size.x, bounds.size.y));
        _core = Make("Core", source.sharedMesh, materials);
        for (int i = 0; i < _sparks.Length; i++) _sparks[i] = Make("Orbit" + i, source.sharedMesh, materials);
        _root.gameObject.SetActive(false);
    }
    private Transform Make(string name, Mesh mesh, Material[] materials)
    {
        var obj = new GameObject(name) { layer = VRLayers.ModLayer };
        obj.transform.SetParent(_root, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return obj.transform;
    }
    internal void Sample(Vector3 centre, float clock, float strength)
        => Sample(centre, Vector3.up, clock, strength);
    internal void Sample(Vector3 centre, Vector3 normal, float clock, float strength)
    {
        bool visible = strength > .001f;
        if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        if (!visible) return;
        _root.position = centre;
        _root.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        // A formed two-hand spell occupies a visible hand-span. The second, quieter
        // inspection phrase uses the same authored strength to contract the volume.
        float radius = Mathf.Lerp(.055f, .22f, strength);
        _core.localScale = Vector3.one * ((.055f + .15f * strength) * _meshScale);
        for (int i = 0; i < _sparks.Length; i++)
        {
            bool outer = i < 12;
            float angle = clock * (outer ? .9f : -.7f) + (i % 12) * Mathf.PI / 6f;
            _sparks[i].localPosition = new Vector3(Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius * (outer ? .45f : .9f), Mathf.Sin(angle) * radius * (outer ? .9f : -.45f));
            _sparks[i].localScale = Vector3.one * ((.018f + .022f * strength) * _meshScale);
        }
    }
    internal void Suspend() => _root.gameObject.SetActive(false);
    public void Dispose() { if (_root != null) UnityEngine.Object.Destroy(_root.gameObject); }
}
