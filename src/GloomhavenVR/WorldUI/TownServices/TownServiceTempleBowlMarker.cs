using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

/// <summary>A non-interactive image of the original purse above the real donation bowl.</summary>
internal sealed class TownServiceTempleBowlMarker : IDisposable
{
    private readonly GameObject _root;
    private readonly List<Material> _materials = new();
    private readonly List<Transform> _blessingSparks = new();
    private readonly List<Material> _blessingMaterials = new();
    private GameObject? _bag;
    private Mesh? _blessingMesh;
    private float _visibility;
    private float _blessingStarted = float.NegativeInfinity;
    internal GameObject Root => _root;
    internal Transform? Visual => _bag != null ? _bag.transform : null;

    /// <param name="stationSpace">True when the marker is owned by the permanent priestess
    /// station rather than by its temporary shared-bowl frame.</param>
    internal TownServiceTempleBowlMarker(Transform parent, bool stationSpace = false)
    {
        _root = new GameObject("GloomhavenVR.Temple.GhostPurse");
        _root.transform.SetParent(parent, false);
        _root.transform.localPosition = stationSpace
            ? TownServiceRitualLayout.Origin + TownServiceTempleBowl.PurseSeat
            : TownServiceTempleBowl.PurseSeat;
        _root.transform.localRotation = Quaternion.identity;
        VRLayers.Apply(_root);
    }

    internal void Tick(bool shown)
    {
        if (_root == null) return;
        if (_bag == null && TownServiceDecor.MoneyBagTemplate != null)
        {
            _bag = UnityEngine.Object.Instantiate(TownServiceDecor.MoneyBagTemplate.gameObject, _root.transform, false);
            _bag.name = "OriginalPurseSilhouette";
            _bag.SetActive(true);
            foreach (Collider collider in _bag.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Renderer renderer in _bag.GetComponentsInChildren<Renderer>(true))
            {
                Material[] copies = renderer.sharedMaterials;
                for (int i = 0; i < copies.Length; i++)
                {
                    if (copies[i] == null) continue;
                    Material copy = GhostMaterial(copies[i]);
                    _materials.Add(copy); copies[i] = copy;
                }
                renderer.sharedMaterials = copies;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            BuildBlessing();
        }
        _visibility = Mathf.MoveTowards(_visibility, shown ? 1f : 0f, Time.unscaledDeltaTime / .12f);
        float pulse = .96f + .06f * Mathf.Sin(Time.unscaledTime * 4f);
        _root.transform.localScale = Vector3.one * pulse;
        foreach (Material material in _materials)
            if (material != null && material.HasProperty("_Color"))
            {
                Color tint = material.GetColor("_Color");
                tint.a = _visibility * .30f;
                material.SetColor("_Color", tint);
            }
        if (_bag != null) _bag.SetActive(_visibility > .01f);
        TickBlessing();
    }

    /// <summary>The original donation has committed. This is a bounded cosmetic response;
    /// it never predicts payment and never invokes a service callback.</summary>
    internal void Bless(float elapsed = 0f) =>
        _blessingStarted = Time.unscaledTime - Mathf.Clamp(elapsed, 0f, 1.65f);

    private static Material GhostMaterial(Material source)
    {
        // TownNpc is deliberately opaque and its visibility channel is a dissolve. A guide
        // needs real perspective-correct transparency, so retain the original maps while
        // moving this owned copy onto Unity's guaranteed transparent Standard pass.
        Shader shader = Shader.Find("Standard")
            ?? throw new InvalidOperationException("Standard shader unavailable for temple guide");
        var copy = new Material(source) { shader = shader, renderQueue = (int)RenderQueue.Transparent };
        Color blue = new(.16f, .55f, 1f, .30f);
        copy.SetColor("_Color", blue);
        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", new Color(.035f, .13f, .28f, 1f));
        if (copy.HasProperty("_Mode")) copy.SetFloat("_Mode", 3f);
        if (copy.HasProperty("_SrcBlend")) copy.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (copy.HasProperty("_DstBlend")) copy.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (copy.HasProperty("_ZWrite")) copy.SetInt("_ZWrite", 0);
        copy.DisableKeyword("_ALPHATEST_ON"); copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        copy.EnableKeyword("_ALPHABLEND_ON"); copy.EnableKeyword("_EMISSION");
        return copy;
    }

    private void BuildBlessing()
    {
        if (_bag == null || _blessingSparks.Count != 0) return;
        _blessingMesh = new Mesh { name = "Temple blessing mote" };
        _blessingMesh.vertices = new[]
        {
            new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 0f),
            new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f),
            new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
        };
        _blessingMesh.triangles = new[]
        {
            0, 4, 2, 0, 3, 4, 0, 5, 3, 0, 2, 5,
            1, 2, 4, 1, 4, 3, 1, 3, 5, 1, 5, 2,
        };
        _blessingMesh.RecalculateNormals();
        _blessingMesh.RecalculateBounds();
        Material sourceMaterial = _materials.Count > 0 ? _materials[0]
            : throw new InvalidOperationException("Temple guide has no material");
        for (int i = 0; i < 18; i++)
        {
            var spark = new GameObject("BlessingSpark" + i) { layer = VRLayers.ModLayer };
            spark.transform.SetParent(_root.transform, false);
            spark.AddComponent<MeshFilter>().sharedMesh = _blessingMesh;
            var renderer = spark.AddComponent<MeshRenderer>();
            Material material = GhostMaterial(sourceMaterial);
            material.SetColor("_Color", i % 3 == 0
                ? new Color(1f, .80f, .30f, .55f) : new Color(.18f, .62f, 1f, .48f));
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false; spark.SetActive(false);
            _blessingSparks.Add(spark.transform); _blessingMaterials.Add(material);
        }
    }

    private void TickBlessing()
    {
        float age = Time.unscaledTime - _blessingStarted;
        bool active = age >= 0f && age < 1.65f;
        float t = Mathf.Clamp01(age / 1.65f);
        float strength = active ? Mathf.Sin(t * Mathf.PI) : 0f;
        for (int i = 0; i < _blessingSparks.Count; i++)
        {
            Transform spark = _blessingSparks[i];
            if (spark.gameObject.activeSelf != active) spark.gameObject.SetActive(active);
            if (!active) continue;
            float phase = i * (Mathf.PI * 2f / _blessingSparks.Count);
            float radius = Mathf.Lerp(.025f, .19f, t);
            spark.localPosition = new Vector3(Mathf.Cos(phase + age * .7f) * radius,
                .015f + t * .34f + Mathf.Sin(phase * 2f) * .018f,
                Mathf.Sin(phase + age * .7f) * radius);
            spark.localRotation = Quaternion.Euler(0f, -phase * Mathf.Rad2Deg, age * 85f + i * 19f);
            spark.localScale = Vector3.one * (.005f + .011f * strength);
            Material material = _blessingMaterials[i];
            Color color = material.GetColor("_Color"); color.a = strength * (i % 3 == 0 ? .55f : .42f);
            material.SetColor("_Color", color);
        }
    }

    public void Dispose()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        foreach (Material material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
        foreach (Material material in _blessingMaterials) if (material != null) UnityEngine.Object.Destroy(material);
        if (_blessingMesh != null) UnityEngine.Object.Destroy(_blessingMesh);
        _materials.Clear();
        _blessingMaterials.Clear(); _blessingSparks.Clear();
    }
}
