using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A hand-cranked revolving card rack. The historical class/template address is
/// retained for transport compatibility; no filing drawer or screen navigation remains.
/// A complete turn exposes the next tray, swapping its cards only behind the opaque back.</summary>
internal sealed class TownServiceMerchantDrawer : IGrabbable, IGrabbableHandFilter, IGrabCancellation, IDisposable
{
    private static readonly HashSet<Transform> ContentRoots = new();
    internal static bool IsContentRoot(Transform node) => ContentRoots.Contains(node);
    internal const int Capacity = 16;
    internal const float Travel = .16f;
    private readonly GameObject _root, _housing;
    private readonly Func<bool> _alive, _mayClose;
    private readonly Action<TownServiceMerchantDrawer> _opening;
    private readonly BoxCollider _pick;
    private readonly Material _material;
    private VRHand? _hand;
    private Vector3 _cursorStart;
    private float _pull, _leadAngle;
    private bool _laser;
    private float _turn, _clock;
    private bool _disposed, _turning, _swapped;
    internal Transform Root => _root.transform;
    internal Transform Content { get; }
    internal readonly bool Selling;
    internal readonly int Category;
    internal Transform HousingRoot => _housing.transform;
    internal int Page { get; private set; }
    internal int PageCount { get; private set; } = 1;
    private int _availablePages = 1;
    internal bool Exposed => true;
    internal bool Accessible => !_turning;
    internal bool Moving => _hand != null || _turning;
    public bool GrabWithGrip => true;
    public bool CanGrab => !_disposed && _hand == null && !_turning && PageCount > 1 && _alive() && _mayClose();
    public bool AllowsHand(VRHand hand) => CanGrab || _hand == hand;
    internal TownServiceMerchantDrawer(Transform parent, int level, bool selling, int category, string label, TMP_Text? font,
        Func<bool> alive, Func<bool> mayClose, Action<TownServiceMerchantDrawer> opening)
    {
        Selling = selling; Category = category; _alive = alive; _mayClose = mayClose; _opening = opening;
        _housing = CreateHousingTemplate(); HousingRoot.SetParent(parent, false);
        HousingRoot.localPosition = new Vector3(selling ? .35f : -.35f, -.20f, -.57f);
        Content = new GameObject("PersistentCards").transform; Content.SetParent(HousingRoot, false); ContentRoots.Add(Content);
        _root = CreateTemplate(font); Root.SetParent(parent, false);
        Root.localPosition = new Vector3(selling ? .725f : -.725f, -.15f, -.55f);
        _material = new Material(OriginalWood());
        foreach (Renderer renderer in _root.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = _material;
        foreach (Renderer renderer in _housing.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = _material;
        _pick = Root.Find("Handle").gameObject.AddComponent<BoxCollider>(); _pick.isTrigger = true;
        VRInteractables.RegisterGrabbable(this, _pick); VRLayers.Apply(_root); VRLayers.Apply(_housing);
    }
    internal void SetPageCount(int count)
    {
        _availablePages = Math.Max(1, count);
        // A native sale/unlock census may arrive while a sample is still returning.
        // Keep its physical tray fixed; an empty last tray remains crankable back home.
        PageCount = Math.Max(_availablePages, Page + 1);
    }
    internal bool RequestTurn()
    {
        if (!CanGrab) return false;
        _turning = true; _leadAngle = _pull * 35f; _clock = 0f; _swapped = false; _opening(this); return true;
    }
    internal static GameObject CreateHousingTemplate()
    {
        var root = new GameObject("MerchantRevolvingRack");
        Part(root.transform, "Opaque tray back", new Vector3(0,.015f,.006f), new Vector3(.63f,.65f,.018f));
        foreach (float x in new[]{-.312f,.312f}) Part(root.transform,"Forged side rail",new Vector3(x,.015f,-.013f),new Vector3(.016f,.65f,.022f));
        for (int row=0;row<4;row++) Part(root.transform,"Card retaining lip",new Vector3(0,-.246f+row*.14f,-.04f),new Vector3(.63f,.008f,.025f));
        Material wood = OriginalWood(); foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial=wood;
        return root;
    }
    private static Material OriginalWood()
    {
        GameObject? prefab=TownServiceAssets.Prefab("townmerchant");
        Transform? counter = prefab != null ? prefab.transform.Find("Counter") : null;
        // Authored furniture joins its curved wooden pieces into material meshes; it has
        // no procedural SurfacePlank0 child. Resolve the actual material rather than an
        // obsolete primitive name (otherwise creating the first crank hides the service).
        if (counter != null)
            foreach (MeshRenderer renderer in counter.GetComponentsInChildren<MeshRenderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && material.name == "DarkWood") return material;
        throw new InvalidOperationException("Merchant revolving cabinet requires original counter wood material");
    }
    internal static GameObject CreateTemplate(TMP_Text? font)
    {
        var root=new GameObject("MerchantRackCrank");
        Rod(root.transform, "Crank spindle", new Vector3(0,0,.025f), new Vector3(0,0,-.025f), .021f);
        Rod(root.transform, "Crank arm", Vector3.zero, new Vector3(0,-.13f,0), .014f);
        Rod(root.transform, "Handle", new Vector3(0,-.13f,-.005f), new Vector3(0,-.13f,-.105f), .026f);
        Material wood=OriginalWood();foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=wood;
        return root;
    }
    private static void Rod(Transform parent, string name, Vector3 start, Vector3 end, float radius)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cylinder); part.name = name;
        part.transform.SetParent(parent, false); part.transform.localPosition = (start + end) * .5f;
        part.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
        part.transform.localScale = new Vector3(radius * 2f, Vector3.Distance(start, end) * .5f, radius * 2f);
        Collider shape = part.GetComponent<Collider>(); shape.enabled = false; UnityEngine.Object.Destroy(shape);
    }
    private static void Part(Transform parent,string name,Vector3 position,Vector3 scale)
    {
        var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent,false);
        part.transform.localPosition=position;part.transform.localScale=scale;
        Collider shape=part.GetComponent<Collider>();shape.enabled=false;UnityEngine.Object.Destroy(shape);
    }
    internal static Vector3 CardPosition(int index)=>TownServiceMerchantLayout.StockPosition(index);
    internal void BeginLaser() => _laser = true;
    public void OnGrab(VRHand hand)
    {
        if(!CanGrab)return;TownServicePhysicalRay.Claim(hand);_hand=hand;_cursorStart=Root.parent.InverseTransformPoint(hand.Rig.GrabAnchor.position);_pull=0f;
    }
    internal void Tick(float opacity)
    {
        if(_disposed)return;
        if (_hand != null && !_laser)
        {
            float delta = _cursorStart.y - Root.parent.InverseTransformPoint(_hand.Rig.GrabAnchor.position).y;
            _pull = Mathf.Clamp01(delta / .10f);
            Root.localRotation = Quaternion.Euler(0f, 0f, -35f * _pull);
        }
        if (_turning)
        {
            _clock += Time.unscaledDeltaTime;
            float t=Mathf.Clamp01(_clock/.85f); _turn=t*t*(3f-2f*t);
            // At half a revolution the complete opaque rack back faces the visitor.
            if(!_swapped&&_turn>=.5f){Page=(Page+1)%_availablePages;PageCount=_availablePages;_swapped=true;}
            HousingRoot.localRotation=Quaternion.Euler(0f,360f*_turn,0f);
            Root.localRotation=Quaternion.Euler(0f,0f,-(_leadAngle+(360f-_leadAngle)*_turn));
            if(t>=1f){_turning=false;HousingRoot.localRotation=Quaternion.identity;Root.localRotation=Quaternion.identity;}
        }
        _pick.enabled=opacity>.99f&&_alive()&&!_turning&&PageCount>1&&_mayClose();
        if(_material.HasProperty("_TownVisibility"))_material.SetFloat("_TownVisibility",opacity);
    }
    internal void Close() { }
    public void OnRelease(VRHand hand,Vector3 velocity)
    {
        if(_hand!=hand)return;TownServicePhysicalRay.Claim(hand);_hand=null;RequestTurn();_laser=false;
    }
    public void OnGrabCancelled(VRHand hand){if(_hand==hand){_hand=null;_laser=false;_pull=0f;Root.localRotation=Quaternion.identity;}}
    public void Dispose(){if(_disposed)return;_disposed=true;ContentRoots.Remove(Content);VRInteractables.UnregisterGrabbable(this);UnityEngine.Object.Destroy(_material);UnityEngine.Object.Destroy(_root);UnityEngine.Object.Destroy(_housing);}
}
