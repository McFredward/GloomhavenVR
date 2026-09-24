using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>A real cabinet pushbutton with the game's original slot pictogram.
/// It changes presentation only, never calls the native shop's selection callbacks.</summary>
internal sealed class TownServiceCatalogCategory : IPokeable, IDisposable
{
    private static readonly List<TownServiceCatalogCategory> All = new();
    private static TownServiceCatalogCategory? _hover;
    private readonly GameObject _root;
    private readonly TownServiceMerchantDrawer _rack;
    private readonly Func<bool> _available;
    private readonly BoxCollider _shape;
    private readonly Vector3 _home;
    private readonly int _category;
    private readonly List<Material> _materials = new();
    private float _pressed, _lastPressed = float.NegativeInfinity;
    internal Transform Root => _root.transform;
    internal string Key => "merchant.category." + _category;
    internal TownServiceCatalogCategory(Transform parent, int category, TownServiceMerchantDrawer rack, Func<bool> available)
    {
        _category = category; _rack = rack; _available = available;
        _root = CreateTemplate(category); Root.SetParent(parent, false);
        _home = new Vector3(-1.25f + category * .12f, -.13f, .0f); Root.localPosition = _home;
        _shape = _root.AddComponent<BoxCollider>(); _shape.isTrigger = true; _shape.size = new Vector3(.095f,.09f,.04f);
        var copies = new Dictionary<Material, Material>();
        foreach (Renderer renderer in _root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material original = materials[i]; if (original == null) continue;
                if (!copies.TryGetValue(original, out Material copy))
                { copy = new Material(original); copies.Add(original, copy); _materials.Add(copy); }
                materials[i] = copy;
            }
            renderer.sharedMaterials = materials;
        }
        All.Add(this); VRInteractables.RegisterPokeable(this, _shape); VRLayers.Apply(_root);
    }
    internal static GameObject CreateTemplate(int category)
    {
        GameObject root = TownServiceMerchantDrawer.Authored("MerchantButtonTemplate");
        var icon = new GameObject("OriginalSlotIcon", typeof(RectTransform), typeof(Canvas), typeof(Image));
        icon.transform.SetParent(root.transform, false);
        var rect = (RectTransform)icon.transform; rect.sizeDelta = new Vector2(64f,64f);
        rect.localScale = Vector3.one * .001f; rect.localPosition = new Vector3(0f,0f,-.021f);
        icon.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        Image image = icon.GetComponent<Image>(); image.raycastTarget = false; image.preserveAspect = true;
        CItem.EItemSlot slot = category switch {0=>CItem.EItemSlot.Head,1=>CItem.EItemSlot.Body,2=>CItem.EItemSlot.Legs,
            3=>CItem.EItemSlot.OneHand,4=>CItem.EItemSlot.TwoHand,_=>CItem.EItemSlot.SmallItem};
        image.sprite = UIInfoTools.Instance.GetItemSlotIcon(slot.ToString());
        image.color = new Color(.21f,.12f,.035f,1f); return root;
    }
    internal void Tick(float opacity)
    {
        _shape.enabled = opacity > .99f && _available();
        foreach (Material material in _materials) if (material.HasProperty("_TownVisibility")) material.SetFloat("_TownVisibility", opacity);
        _pressed = Mathf.MoveTowards(_pressed, _rack.Category == _category ? 1f : 0f, Time.unscaledDeltaTime * 12f);
        Root.localPosition = _home + Vector3.forward * (.012f * _pressed);
    }
    public void OnPokeEnter(VRHand hand) { if (_available()) hand.SendHaptic(HapticPreset.HoverTick); }
    public void OnPokeExit(VRHand hand) { }
    public void OnPoke(VRHand hand)
    {
        if (!_available() || Time.unscaledTime - _lastPressed < .3f) return;
        TownServicePublicMerchant.Claim();
        if (_rack.Select(_category, false)) { _lastPressed = Time.unscaledTime; hand.SendHaptic(HapticPreset.ClickPulse); }
    }
    internal static float OccludingDistance(Vector3 origin, Vector3 direction, float maximum)
    {
        float best = float.PositiveInfinity; var ray = new Ray(origin,direction);
        foreach (var button in All) if (button._shape.enabled && button._shape.Raycast(ray,out RaycastHit hit,maximum)) best=Mathf.Min(best,hit.distance);
        return best;
    }
    internal static void TickLaser()
    {
        VRHand? hand=VRHands.Primary;
        if(hand==null||!hand.HasPose||!hand.Ray.Active||hand.Grabber.Held!=null||hand.RayGrab.OwnsPointerFrame) {_hover=null;return;}
        hand.GetAimRay(out Vector3 origin,out Vector3 direction);var ray=new Ray(origin,direction);
        float best=RayGrabDriver.MaxDistanceMeters*hand.WorldScale;TownServiceCatalogCategory? target=null;Vector3 point=default;
        foreach(var button in All) if(button._shape.enabled&&button._shape.Raycast(ray,out RaycastHit hit,best)) {target=button;best=hit.distance;point=hit.point;}
        if(target!=null && (RayGrabDriver.OccludingBarDistance(origin,direction,best)<best-.005f*hand.WorldScale
            || hand.Ray.SolidOccluderDistance<best-.005f*hand.WorldScale
            || hand.RayUgui.HasHit&&hand.RayUgui.HitDistance<best))target=null;
        if(target!=_hover&&target!=null)target.OnPokeEnter(hand);_hover=target;
        if(target==null)return;hand.Ray.UiHitOverride=point;
        if(hand.TriggerDown){TownServicePhysicalRay.Claim(hand);target.OnPoke(hand);}
    }
    public void Dispose() { All.Remove(this); if(_hover==this)_hover=null; VRInteractables.UnregisterPokeable(this); foreach (Material material in _materials) UnityEngine.Object.Destroy(material); _materials.Clear(); UnityEngine.Object.Destroy(_root); }
}
