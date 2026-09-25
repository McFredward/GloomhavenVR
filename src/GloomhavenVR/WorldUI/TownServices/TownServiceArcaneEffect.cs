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
    private readonly MeshRenderer[] _sparkRenderers = new MeshRenderer[24];
    private readonly Transform _core;
    private readonly MeshRenderer _coreRenderer;
    private readonly MaterialPropertyBlock _tint = new();
    private readonly float _meshScale;
    internal TownServiceArcaneEffect(Transform parent, MeshFilter source, Material[] materials)
    {
        _root = new GameObject("Town.ArcaneConstellation").transform;
        _root.SetParent(parent, false);
        Bounds bounds = source.sharedMesh.bounds;
        _meshScale = 1f / Mathf.Max(.001f, Mathf.Max(bounds.size.x, bounds.size.y));
        _core = Make("Core", source.sharedMesh, materials);
        _coreRenderer = _core.GetComponent<MeshRenderer>();
        for (int i = 0; i < _sparks.Length; i++)
        {
            _sparks[i] = Make("Orbit" + i, source.sharedMesh, materials);
            _sparkRenderers[i] = _sparks[i].GetComponent<MeshRenderer>();
        }
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
        => Sample(centre, Vector3.up, clock, strength, 0f);
    internal void Sample(Vector3 centre, Vector3 normal, float clock, float strength, float mode)
    {
        bool visible = strength > .001f;
        if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        if (!visible) return;
        _root.position = centre;
        _root.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        // WorkClock selects one of three synchronized experiments: a compact
        // palm star, a two-hand sigil, or luminous threads rising over the book.
        // CastSway carries the blended mode through attention and authority
        // handover. No local RNG or independent clock can split observer poses.
        float sigil = Mathf.Clamp01(mode * 2f);
        float threads = Mathf.Clamp01((mode - .5f) * 2f);
        Color palm = new(.30f, .65f, .90f, .55f);
        Color seal = new(.68f, .43f, .93f, .60f);
        Color script = new(.85f, .64f, .34f, .55f);
        Color color = Color.Lerp(Color.Lerp(palm, seal, sigil), script, threads);
        _tint.SetColor("_Color", color);
        _coreRenderer.SetPropertyBlock(_tint);
        _core.localScale = Vector3.one * (Mathf.Lerp(.085f, .040f, threads)
            + .07f * strength) * _meshScale;
        for (int i = 0; i < _sparks.Length; i++)
        {
            float phase = (i % 12) * Mathf.PI / 6f;
            float handAngle = clock * (i < 12 ? .9f : -.7f) + phase;
            Vector3 hand = new(Mathf.Cos(handAngle) * .11f,
                Mathf.Sin(handAngle) * .055f, Mathf.Sin(handAngle) * .080f);
            float sealAngle = phase + clock * (i < 12 ? .34f : -.34f);
            Vector3 ring = new(Mathf.Cos(sealAngle) * (i < 12 ? .22f : .145f),
                Mathf.Sin(sealAngle) * (i < 12 ? .19f : .12f),
                .018f * Mathf.Sin(clock * 1.5f + phase));
            float lane = i / 8, row = i % 8 / 7f;
            float curl = row * Mathf.PI * 1.2f + lane * Mathf.PI * 2f / 3f + clock * .42f;
            Vector3 rising = new(Mathf.Cos(curl) * (.045f + row * .030f),
                row * .31f, Mathf.Sin(curl) * (.045f + row * .030f));
            _sparks[i].localPosition = Vector3.Lerp(Vector3.Lerp(hand, ring, sigil), rising, threads);
            _sparks[i].localScale = Vector3.one * ((.015f + .028f * strength) * _meshScale);
            _sparkRenderers[i].SetPropertyBlock(_tint);
        }
    }
    internal void Suspend() => _root.gameObject.SetActive(false);
    public void Dispose() { if (_root != null) UnityEngine.Object.Destroy(_root.gameObject); }
}
