using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A non-interactive image of the original purse above the real donation bowl.</summary>
internal sealed class TownServiceTempleBowlMarker : IDisposable
{
    private readonly GameObject _root;
    private readonly List<Material> _materials = new();
    private GameObject? _bag;
    private float _visibility;
    internal GameObject Root => _root;
    internal Transform? Visual => _bag != null ? _bag.transform : null;

    internal TownServiceTempleBowlMarker(Transform bowl)
    {
        _root = new GameObject("GloomhavenVR.Temple.GhostPurse");
        _root.transform.SetParent(bowl, false);
        _root.transform.localPosition = TownServiceTempleBowl.Center + Vector3.up * .018f;
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
                    Material copy = new(copies[i]);
                    if (copy.HasProperty("_Color")) copy.SetColor("_Color", new Color(.44f, .88f, .79f));
                    if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", new Color(.18f, .45f, .38f));
                    _materials.Add(copy); copies[i] = copy;
                }
                renderer.sharedMaterials = copies;
            }
        }
        _visibility = Mathf.MoveTowards(_visibility, shown ? 1f : 0f, Time.unscaledDeltaTime / .12f);
        float pulse = .86f + .07f * Mathf.Sin(Time.unscaledTime * 4f);
        _root.transform.localScale = Vector3.one * pulse;
        foreach (Material material in _materials)
            if (material != null && material.HasProperty("_TownVisibility"))
                material.SetFloat("_TownVisibility", _visibility * .55f);
        if (_bag != null) _bag.SetActive(_visibility > .01f);
    }

    public void Dispose()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        foreach (Material material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
        _materials.Clear();
    }
}
