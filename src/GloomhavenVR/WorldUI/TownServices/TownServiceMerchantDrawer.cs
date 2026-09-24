using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Physical indexed cassette inside the authored upright merchant cabinet.
/// The historical class/template addresses remain valid, while additive TLV86 identifies
/// the new withdraw/shutter/extend mechanism without changing any TLV85 byte.</summary>
internal sealed class TownServiceMerchantDrawer : IGrabbable, IGrabbableHandFilter, IGrabCancellation, IDisposable
{
    private static readonly HashSet<Transform> ContentRoots = new();
    internal static bool IsContentRoot(Transform node) => ContentRoots.Contains(node);
    internal const int Capacity = 12;
    internal const float Travel = .32f;
    private readonly GameObject _root, _housing;
    private readonly Func<bool> _alive, _mayClose;
    private readonly Action<TownServiceMerchantDrawer> _opening;
    private readonly BoxCollider _pick;
    private readonly List<Material> _materials = new();
    private VRHand? _hand;
    private Vector3 _cursorStart;
    private float _pull, _leadAngle, _clock;
    private float _nextStickTurn;
    private int _stickDirection;
    private bool _laser, _disposed, _turning, _swapped;
    internal Transform Root => _root.transform;
    internal Transform Content { get; }
    internal bool Selling => Page >= 2048;
    internal int Category => Page % 2048 / 256;
    internal Transform HousingRoot => _housing.transform;
    internal uint TurnEpoch { get; private set; }
    internal int FromPage { get; private set; }
    internal int ToPage { get; private set; }
    internal float TurnElapsed => Mathf.Clamp(_clock, 0f, TownRackState.TurnDuration);
    internal float LeadAngle => _leadAngle;
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
        _alive = alive; _mayClose = mayClose; _opening = opening;
        Page = category * 256 + (selling ? 2048 : 0);
        _housing = CreateHousingTemplate(); HousingRoot.SetParent(parent, false);
        HousingRoot.localPosition = new Vector3(-.95f, .25f, .035f);
        Content = new GameObject("PersistentCards").transform;
        Content.SetParent(HousingRoot.Find("Cassette"), false); ContentRoots.Add(Content);
        _root = CreateTemplate(font); Root.SetParent(parent, false);
        Root.localPosition = new Vector3(-.47f, .08f, .11f);
        CopyMaterials(_root); CopyMaterials(_housing);
        Transform handle = Root.Find("Handle") ?? Root;
        _pick = handle.gameObject.AddComponent<BoxCollider>(); _pick.isTrigger = true;
        MeshFilter? handleMesh = handle.GetComponent<MeshFilter>();
        if (handleMesh == null || handleMesh.sharedMesh == null)
            throw new InvalidOperationException("The authored merchant crank handle has no mesh bounds.");
        Bounds gripBounds = handleMesh.sharedMesh.bounds;
        _pick.center = gripBounds.center;
        _pick.size = gripBounds.size + Vector3.one * .025f;
        VRInteractables.RegisterGrabbable(this, _pick); VRLayers.Apply(_root); VRLayers.Apply(_housing);
        TownCassetteMotion.Apply(HousingRoot, 1f);
    }
    private void CopyMaterials(GameObject root)
    {
        var copies = new Dictionary<Material, Material>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                if (!copies.TryGetValue(materials[i], out Material copy))
                { copy = new Material(materials[i]); copies.Add(materials[i], copy); _materials.Add(copy); }
                materials[i] = copy;
            }
            renderer.sharedMaterials = materials;
        }
    }
    internal void Follow(TownRackState state)
    {
        if (_hand != null || !_mayClose()) return;
        TurnEpoch = state.Turn; Page = state.Page; FromPage = state.From; ToPage = state.To;
        _clock = state.Elapsed; _leadAngle = state.LeadAngle;
        _turning = state.Turn != 0 && state.Elapsed < TownRackState.TurnDuration;
        _swapped = state.Elapsed >= TownRackState.TurnDuration * .5f;
        TownCassetteMotion.Apply(HousingRoot, _turning ? state.Elapsed / TownRackState.TurnDuration : 1f);
    }
    internal void SetPageCount(int count) { _availablePages = Math.Max(1, count); PageCount = Math.Max(_availablePages, Page % 256 + 1); }
    internal bool RetainsPage(int page) => page == Page || (_turning && (page == FromPage || page == ToPage))
        || page == Page / 256 * 256 + (Page % 256 + 1) % Math.Max(1, PageCount);
    internal bool Select(int category, bool selling)
    {
        if (category < 0 || category >= 6 || _disposed || _hand != null || _turning || !_alive() || !_mayClose()) return false;
        int target = category * 256 + (selling ? 2048 : 0);
        return target != Page && Begin(target);
    }
    private bool Begin(int page)
    {
        TurnEpoch++; FromPage = Page; ToPage = page; _turning = true;
        _leadAngle = _pull * 35f; _clock = 0f; _swapped = false; _opening(this); return true;
    }
    internal bool RequestTurn() => RequestTurn(1);
    internal bool RequestTurn(int direction) => direction != 0 && CanGrab
        && Begin(Page / 256 * 256 + (Page % 256 + (direction > 0 ? 1 : _availablePages - 1)) % _availablePages);

    // The stock display is a physical scroll surface. Reuse the UI/flight arbitration so
    // aiming here never scrolls the cabinet and moves the player vertically at the same time.
    // A full mechanical change completes before a held stick can request the next page.
    internal bool NoteScrollHover(VRHand? hand)
    {
        if (_disposed || _hand != null || PageCount <= 1 || !_alive() || !_mayClose() || !TownServicePublicMerchant.CanClaim
            || hand == null || hand != VRHands.Primary || !hand.HasPose || !hand.Ray.Active || hand.Grabber.Held != null
            || hand.RayGrab.OwnsPointerFrame || hand.RayUgui.IsPressing)
            return false;
        hand.GetAimRay(out Vector3 origin, out Vector3 direction);
        // Authored cabinet, including its category buttons and side crank. This is a query,
        // not extra collision geometry that would hide the actual original item faces.
        Vector3 localOrigin = HousingRoot.InverseTransformPoint(origin);
        Vector3 localDirection = HousingRoot.InverseTransformVector(direction).normalized;
        var bounds = new Bounds(new Vector3(.025f, -.08f, .14f), new Vector3(.98f, 1.04f, .43f));
        if (!bounds.IntersectRay(new Ray(localOrigin, localDirection), out float localDistance))
            return false;
        Vector3 point = HousingRoot.TransformPoint(localOrigin + localDirection * localDistance);
        float distance = Vector3.Distance(origin, point), epsilon = .005f * hand.WorldScale;
        if (distance > RayGrabDriver.MaxDistanceMeters * hand.WorldScale
            || RayGrabDriver.OccludingBarDistance(origin, direction, distance) < distance - epsilon
            || hand.Ray.SolidOccluderDistance < distance - epsilon
            || hand.RayUgui.HasHit && hand.RayUgui.HitDistance < distance - epsilon)
            return false;
        UiScrollFocus.NoteScrollHover(hand, _housing, nameof(TownServiceMerchantDrawer));
        return true;
    }
    internal void TickStickScroll()
    {
        VRHand? hand = VRHands.Primary;
        if (!NoteScrollHover(hand)) { _stickDirection = 0; return; }
        float axis = hand!.Thumbstick.y;
        int step = Mathf.Abs(axis) < .3f ? 0 : axis > 0f ? -1 : 1;
        if (step == 0) { _stickDirection = 0; return; }
        if (_stickDirection != step) _nextStickTurn = 0f;
        _stickDirection = step;
        if (_turning || Time.unscaledTime < _nextStickTurn) return;
        TownServicePublicMerchant.Claim();
        if (RequestTurn(step))
        {
            _nextStickTurn = Time.unscaledTime + TownRackState.TurnDuration + .15f;
            UiScrollFocus.NoteScrollDelivered(hand, _housing, nameof(TownServiceMerchantDrawer));
            hand.SendHaptic(HapticPreset.ClickPulse);
        }
    }
    internal static GameObject Authored(string name)
    {
        Transform? source = TownServiceAssets.Prefab("townmerchant")?.transform.Find("Counter/" + name);
        if (source == null) throw new InvalidOperationException("The matching merchant cabinet asset is missing: " + name);
        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject);
        clone.transform.localPosition = Vector3.zero; clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one; clone.SetActive(true); return clone;
    }
    internal static GameObject CreateHousingTemplate()
    {
        var root = new GameObject("MerchantIndexedCassette");
        GameObject cassette = Authored("MerchantCassetteTemplate"); cassette.name = "Cassette"; cassette.transform.SetParent(root.transform, false);
        GameObject shutter = Authored("MerchantShutterTemplate"); shutter.name = "Shutter"; shutter.transform.SetParent(root.transform, false);
        shutter.transform.localPosition = new Vector3(0f, 0f, -.020f);
        TownCassetteMotion.Apply(root.transform, 1f); return root;
    }
    internal static GameObject CreateTemplate(TMP_Text? font) => Authored("MerchantCrankTemplate");
    internal static Vector3 CardPosition(int index) => TownServiceMerchantLayout.StockPosition(index);
    internal void BeginLaser() => _laser = true;
    public void OnGrab(VRHand hand)
    { if (!CanGrab) return; TownServicePublicMerchant.Claim(); TownServicePhysicalRay.Claim(hand); _hand = hand; _cursorStart = Root.parent.InverseTransformPoint(hand.Rig.GrabAnchor.position); _pull = 0f; }
    internal void Tick(float opacity)
    {
        if (_disposed) return;
        if (_hand != null && !_laser)
        {
            _pull = Mathf.Clamp01((_cursorStart.y - Root.parent.InverseTransformPoint(_hand.Rig.GrabAnchor.position).y) / .10f);
            Root.localRotation = Quaternion.Euler(-35f * _pull, 0f, 0f);
        }
        if (_turning)
        {
            _clock += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(_clock / TownRackState.TurnDuration);
            if (!_swapped && progress >= .5f) { Page = ToPage; _swapped = true; }
            TownCassetteMotion.Apply(HousingRoot, progress);
            Root.localRotation = Quaternion.Euler(-(_leadAngle + (360f - _leadAngle) * TownRackState.Progress(_clock)), 0f, 0f);
            if (progress >= 1f) { _turning = false; Root.localRotation = Quaternion.identity; }
        }
        _pick.enabled = opacity > .99f && _alive() && !_turning && PageCount > 1 && _mayClose();
        foreach (Material material in _materials) if (material.HasProperty("_TownVisibility")) material.SetFloat("_TownVisibility", opacity);
    }
    internal void Close() { }
    public void OnRelease(VRHand hand, Vector3 velocity)
    { if (_hand != hand) return; TownServicePhysicalRay.Claim(hand); _hand = null; RequestTurn(); _laser = false; }
    public void OnGrabCancelled(VRHand hand)
    { if (_hand == hand) { _hand = null; _laser = false; _pull = 0f; Root.localRotation = Quaternion.identity; } }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ContentRoots.Remove(Content); VRInteractables.UnregisterGrabbable(this);
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_housing);
    }
}
