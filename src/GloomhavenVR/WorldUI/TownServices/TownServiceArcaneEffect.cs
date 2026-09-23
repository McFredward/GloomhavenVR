using System;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A bounded constellation made from the game's original candle-glow artwork.
/// Its orbit is sampled from the shared performance clock, never a local particle simulation.</summary>
internal sealed class TownServiceArcaneEffect : IDisposable
{
    private readonly Transform _root;
    private readonly Transform[] _sparks = new Transform[12];
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
    {
        bool visible = strength > .001f;
        if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        if (!visible) return;
        _root.position = centre;
        _core.localScale = Vector3.one * ((.12f + .035f * Mathf.Sin(clock * 2f)) * _meshScale);
        for (int i = 0; i < _sparks.Length; i++)
        {
            float angle = clock * (i < 6 ? .9f : -.7f) + (i % 6) * Mathf.PI / 3f;
            float radius = .115f + .018f * Mathf.Sin(clock * 1.4f + i);
            _sparks[i].localPosition = new Vector3(Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius * (i < 6 ? .45f : .9f), Mathf.Sin(angle) * radius * (i < 6 ? .9f : -.45f));
            _sparks[i].localScale = Vector3.one * ((.021f + .008f * (.5f + .5f * Mathf.Sin(clock * 3f + i))) * _meshScale);
        }
    }
    internal void Suspend() => _root.gameObject.SetActive(false);
    public void Dispose() { if (_root != null) UnityEngine.Object.Destroy(_root.gameObject); }
}
