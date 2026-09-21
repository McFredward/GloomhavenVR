using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A real constrained filing drawer. Its contents persist at all times; no page
/// swaps or filtered card population. Both physical and laser pulls move the same track.</summary>
internal sealed class TownServiceMerchantDrawer : IGrabbable, IGrabbableHandFilter, IGrabCancellation, IDisposable
{
    private static readonly HashSet<Transform> ContentRoots=new();
    internal static bool IsContentRoot(Transform node)=>ContentRoots.Contains(node);
    internal const int Capacity = 48;
    internal const float Travel = .78f;
    private readonly GameObject _root, _housing;
    private readonly Transform _frame;
    private readonly Func<bool> _alive, _mayClose;
    private readonly Action<TownServiceMerchantDrawer> _opening;
    private readonly BoxCollider _pick;
    private readonly Vector3 _home;
    private readonly Material _material;
    private readonly TMP_Text _caption;
    private VRHand? _hand;
    private Vector3 _cursorStart;
    private float _start, _amount, _target;
    private bool _laser, _disposed;
    internal Transform Root => _root.transform;
    internal Transform Content { get; }
    internal readonly bool Selling;
    internal readonly int Category;
    internal Transform HousingRoot=>_housing.transform;
    internal bool Exposed=>_amount>.001f;
    internal bool Accessible => _amount > .95f;
    internal bool Moving => _hand != null || Mathf.Abs(_amount - _target) > .001f;
    public bool GrabWithGrip => true;
    public bool CanGrab => !_disposed && _hand == null && _alive();
    public bool AllowsHand(VRHand hand) => !_disposed && _alive() && (_hand == null || _hand == hand);
    internal TownServiceMerchantDrawer(Transform parent, int level, bool selling, int category, string label, TMP_Text? font,
        Func<bool> alive, Func<bool> mayClose, Action<TownServiceMerchantDrawer> opening)
    {
        Selling = selling; Category = category; _frame = parent; _alive = alive; _mayClose = mayClose; _opening = opening;
        _home = new Vector3(selling ? .68f : -.68f, -.15f - level * .13f, .19f);
        _housing=CreateHousingTemplate();_housing.transform.SetParent(parent,false);_housing.transform.localPosition=_home;
        _root = CreateTemplate(font); Root.SetParent(parent, false); Root.localPosition = _home;
        _material = new Material(OriginalWood());
        foreach (MeshRenderer renderer in Root.GetComponentsInChildren<MeshRenderer>(true))
            if(renderer.GetComponent<TMP_Text>()==null)renderer.sharedMaterial = _material;
        _caption=Root.Find("Label").GetComponent<TMP_Text>();_caption.text=label;
        foreach(MeshRenderer renderer in _housing.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterial=_material;
        Content = new GameObject("PersistentCards").transform; Content.SetParent(Root,false);ContentRoots.Add(Content);
        _pick = Root.Find("Handle").gameObject.AddComponent<BoxCollider>(); _pick.isTrigger = true;
        VRInteractables.RegisterGrabbable(this,_pick); VRLayers.Apply(_root);
    }
    internal static GameObject CreateHousingTemplate()
    {
        var root=new GameObject("MerchantDrawerHousing");
        Part(root.transform,"Top",new Vector3(0,.132f,0),new Vector3(1.065f,.016f,.44f));
        Part(root.transform,"Left",new Vector3(-.531f,.055f,0),new Vector3(.014f,.145f,.44f));
        Part(root.transform,"Right",new Vector3(.531f,.055f,0),new Vector3(.014f,.145f,.44f));
        Part(root.transform,"Back",new Vector3(0,.055f,.219f),new Vector3(1.065f,.145f,.014f));
        Material wood=OriginalWood();foreach(MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())renderer.sharedMaterial=wood;
        return root;
    }
    private static Material OriginalWood()
    {
        GameObject? prefab=TownServiceAssets.Prefab("townmerchant");
        Transform? plank=prefab!=null?prefab.transform.Find("Counter/SurfacePlank0"):null;
        Material? material=plank!=null?plank.GetComponent<MeshRenderer>().sharedMaterial:null;
        if(material==null)throw new InvalidOperationException("Merchant filing drawer requires original counter wood material");
        return material;
    }
    internal static GameObject CreateTemplate(TMP_Text? font)
    {
        var root = new GameObject("MerchantFilingDrawer");
        Part(root.transform,"Floor",new Vector3(0f,-.012f,0f),new Vector3(1.04f,.024f,.42f));
        Part(root.transform,"Front",new Vector3(0f,.057f,-.215f),new Vector3(1.04f,.13f,.022f));
        Part(root.transform,"Left",new Vector3(-.515f,.035f,0f),new Vector3(.018f,.09f,.42f));
        Part(root.transform,"Right",new Vector3(.515f,.035f,0f),new Vector3(.018f,.09f,.42f));
        Part(root.transform,"Back",new Vector3(0f,.035f,.205f),new Vector3(1.04f,.09f,.018f));
        Part(root.transform,"Handle",new Vector3(0f,.026f,-.245f),new Vector3(.20f,.025f,.035f));
        var label = new GameObject("Label",typeof(TextMeshPro));label.transform.SetParent(root.transform,false);
        label.transform.localPosition=new Vector3(0f,.062f,-.228f);
        label.transform.localRotation=Quaternion.Euler(0f,180f,0f);
        var text=label.GetComponent<TextMeshPro>();if(font!=null){text.font=font.font;text.fontSharedMaterial=font.fontSharedMaterial;}
        text.fontSize=1.7f;text.alignment=TextAlignmentOptions.Center;text.color=new Color(.95f,.89f,.72f);
        text.rectTransform.sizeDelta=new Vector2(.95f,.06f);text.enableWordWrapping=false;
        Material wood=OriginalWood();
        foreach(MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            if(renderer.GetComponent<TMP_Text>()==null)renderer.sharedMaterial=wood;
        return root;
    }
    private static void Part(Transform parent,string name,Vector3 position,Vector3 scale)
    {
        var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent,false);
        part.transform.localPosition=position;part.transform.localScale=scale;
        Collider shape=part.GetComponent<Collider>();shape.enabled=false;UnityEngine.Object.Destroy(shape);
    }
    internal static Vector3 CardPosition(int index) => new Vector3((index%6-2.5f)*.168f,
        .006f+(index/6)*.008f,-.145f+(index/6)*.038f);
    internal void BeginLaser() => _laser=true;
    public void OnGrab(VRHand hand)
    {
        if(!CanGrab)return;TownServicePhysicalRay.Claim(hand);_hand=hand;_start=_amount;_cursorStart=Cursor(hand);_opening(this);
    }
    private Vector3 Cursor(VRHand hand)
    {
        if(_laser)
        {
            hand.GetAimRay(out Vector3 origin,out Vector3 direction);
            var plane=new Plane(_frame.up,Root.position);
            if(plane.Raycast(new Ray(origin,direction),out float distance)&&distance>=0f)
                return _frame.InverseTransformPoint(origin+direction*distance);
        }
        return _frame.InverseTransformPoint(hand.Rig.GrabAnchor.position);
    }
    internal void Tick(float opacity)
    {
        if(_disposed)return;
        if(_hand!=null)
        {
            float wanted=Mathf.Clamp01(_start- (Cursor(_hand).z-_cursorStart.z)/Travel);
            _target=(!_mayClose()&&wanted<_amount)?_amount:wanted;
        }
        _amount=Mathf.MoveTowards(_amount,_target,Time.unscaledDeltaTime*6f);
        Root.localPosition=_home+new Vector3(0f,0f,-Travel*_amount);
        _pick.enabled=opacity>.99f&&_alive();
        Color color=_caption.color;color.a=opacity;_caption.color=color;
        // Same material visibility channel as the station; captured for observer playback.
        if(_material.HasProperty("_TownVisibility"))_material.SetFloat("_TownVisibility",opacity);
    }
    internal void Close(){if(_hand==null&&_mayClose())_target=0f;}
    public void OnRelease(VRHand hand,Vector3 velocity)
    {
        if(_hand!=hand)return;TownServicePhysicalRay.Claim(hand);
        // A short pull/click opens fully; an actual closing drag crosses the middle.
        _target=_amount<.05f&&_start<.05f?1f:_amount>=.5f?1f:0f;
        if(!_mayClose())_target=1f;
        _hand=null;_laser=false;
    }
    public void OnGrabCancelled(VRHand hand){if(_hand==hand){_hand=null;_laser=false;_target=_amount;}}
    public void Dispose(){if(_disposed)return;_disposed=true;ContentRoots.Remove(Content);VRInteractables.UnregisterGrabbable(this);UnityEngine.Object.Destroy(_material);UnityEngine.Object.Destroy(_root);UnityEngine.Object.Destroy(_housing);}
}
