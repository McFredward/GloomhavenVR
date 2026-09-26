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
    private readonly TownServiceCabinetAudio _audio;
    private readonly List<Material> _materials = new();
    private VRHand? _hand;
    private Vector3 _cursorStart;
    private float _pull, _leadAngle, _clock;
    private float _nextStickTurn;
    private int _stickDirection;
    private int _indicatorPage = -1, _indicatorCount;
    private bool _laser, _disposed, _turning, _swapped;
    internal Transform Root => _root.transform;
    internal Transform Content { get; }
    private readonly Transform[] _rowContents = new Transform[3];
    private readonly TMP_Text _pageLabel;
    private readonly CanvasGroup _pageLabelGate;
    internal Transform CardParent(int index) => _rowContents[index / TownServiceMerchantLayout.StockColumns % 3];
    internal sbyte ScrollDirection { get; private set; }
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
        for (int row = 0; row < 3; row++)
        {
            Transform? holder = HousingRoot.Find("Cassette/Row" + row);
            if (holder == null) throw new InvalidOperationException("The merchant cabinet requires the matching articulated holder rows.");
            Transform cards = new GameObject("PersistentCards").transform;
            cards.SetParent(holder, false); _rowContents[row] = cards; ContentRoots.Add(cards);
        }
        _pageLabel = HousingRoot.Find("PageIndicator/Caption").GetComponent<TMP_Text>();
        _pageLabelGate = HousingRoot.Find("PageIndicator").GetComponent<CanvasGroup>();
        if (font != null) { _pageLabel.font = font.font; _pageLabel.fontSharedMaterial = font.fontSharedMaterial; }
        _root = CreateTemplate(font); Root.SetParent(parent, false);
        // The cabinet cheek is at X=-.525 m. The old crank reached X=-.295 m,
        // 61 mm inside the ledger's left edge, so the grip pierced the desk.
        // The re-authored mounting plate and axle bridge this outside seat to
        // the cheek while the grip ends clear of the ledger.
        Root.localPosition = new Vector3(-.57f, .08f, .11f);
        CopyMaterials(_root); CopyMaterials(_housing);
        // Authored furniture is visual only. An imported collision shape must never turn
        // an invisible part of the cabinet into another laser/poke target.
        foreach (Collider collider in _root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Collider collider in _housing.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        Transform handle = Root.Find("Handle") ?? Root;
        _pick = handle.gameObject.AddComponent<BoxCollider>(); _pick.isTrigger = true;
        MeshFilter? handleMesh = handle.GetComponent<MeshFilter>();
        if (handleMesh == null || handleMesh.sharedMesh == null)
            throw new InvalidOperationException("The authored merchant crank handle has no mesh bounds.");
        Bounds gripBounds = handleMesh.sharedMesh.bounds;
        _pick.center = gripBounds.center;
        // Imported FBX children use a 100x local scale. Padding mesh-local bounds by
        // .025 made a 2.5-metre invisible box to the merchant's left; releasing its
        // laser grab advanced the cassette. Specify padding in cabinet metres instead.
        _pick.size = gripBounds.size + GripPadding(handle.localScale, .025f);
        _audio = new TownServiceCabinetAudio(HousingRoot);
        VRInteractables.RegisterGrabbable(this, _pick); VRLayers.Apply(_root); VRLayers.Apply(_housing);
        TownCassetteMotion.Apply(HousingRoot, 1f);
    }
    internal static Vector3 GripPadding(Vector3 localScale, float cabinetMetres) => new(
        cabinetMetres / Mathf.Max(.0001f, Mathf.Abs(localScale.x)),
        cabinetMetres / Mathf.Max(.0001f, Mathf.Abs(localScale.y)),
        cabinetMetres / Mathf.Max(.0001f, Mathf.Abs(localScale.z)));
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
        _audio.Begin(state.Turn, state.Elapsed);
        TurnEpoch = state.Turn; Page = state.Page; FromPage = state.From; ToPage = state.To;
        _clock = state.Elapsed; _leadAngle = state.LeadAngle; ScrollDirection = state.ScrollDirection;
        PageCount = state.PageCount;
        _turning = state.Turn != 0 && state.Elapsed < TownRackState.TurnDuration;
        _swapped = state.Elapsed >= TownRackState.TurnDuration * .5f;
        TownCassetteMotion.Apply(HousingRoot, _turning ? state.Elapsed / TownRackState.TurnDuration : 1f, ScrollDirection);
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
    private bool Begin(int page, int direction = 0)
    {
        ScrollDirection = (sbyte)Math.Sign(direction); TurnEpoch++; FromPage = Page; ToPage = page; _turning = true;
        _leadAngle = _pull * 35f; _clock = 0f; _swapped = false; _opening(this);
        _audio.Begin(TurnEpoch, 0f); return true;
    }
    internal bool RequestTurn() => RequestTurn(1);
    internal bool RequestTurn(int direction) => direction != 0 && CanGrab
        && Begin(Page / 256 * 256 + (Page % 256 + (direction > 0 ? 1 : _availablePages - 1)) % _availablePages, direction);

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
        // FBX empty nodes retain the importer's -90 degree rotation and 100x unit scale.
        // Animate metre-space pivots around them, preserving imported geometry underneath;
        // resetting an imported node itself rotates the holders and magnifies native cards.
        for (int row = 0; row < 3; row++)
        {
            Transform? imported = cassette.transform.Find("Row" + row);
            if (imported == null) throw new InvalidOperationException("The merchant cabinet is missing an articulated holder row.");
            Transform pivot = new GameObject("Row" + row).transform;
            pivot.SetParent(cassette.transform, false); pivot.localPosition = imported.localPosition;
            imported.name = "ImportedHolder"; imported.SetParent(pivot, true);
        }
        GameObject shutter = Authored("MerchantShutterTemplate"); shutter.name = "Shutter"; shutter.transform.SetParent(root.transform, false);
        shutter.transform.localPosition = new Vector3(0f, 0f, -.020f);
        var indicator = new GameObject("PageIndicator", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        indicator.transform.SetParent(root.transform, false);
        indicator.transform.localPosition = new Vector3(.465f, -.005f, -.055f);
        indicator.transform.localScale = Vector3.one * .001f;
        ((RectTransform)indicator.transform).sizeDelta = new Vector2(145f, 110f);
        indicator.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var caption = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
        caption.transform.SetParent(indicator.transform, false);
        TMP_Text label = caption.GetComponent<TMP_Text>(); label.rectTransform.sizeDelta = new Vector2(145f, 110f);
        label.fontSize = 25f; label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(.98f, .88f, .65f); label.raycastTarget = false;
        label.text = "↑\n1 / 1\n↓";
        indicator.GetComponent<CanvasGroup>().blocksRaycasts = false;
        TownCassetteMotion.Apply(root.transform, 1f, 0); return root;
    }
    internal static GameObject CreateTemplate(TMP_Text? font) => Authored("MerchantCrankTemplate");
    internal static Vector3 CardPosition(int index) => TownServiceMerchantLayout.StockPosition(index);
    internal void BeginLaser() => _laser = true;
    public void OnGrab(VRHand hand)
    { if (!CanGrab) return; TownServicePublicMerchant.Claim(); TownServicePhysicalRay.Claim(hand); _hand = hand; _cursorStart = Root.parent.InverseTransformPoint(hand.Rig.GrabAnchor.position); _pull = 0f; }
    internal void Tick(float opacity)
    {
        if (_disposed) return;
        _audio.Tick();
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
            TownCassetteMotion.Apply(HousingRoot, progress, ScrollDirection);
            Root.localRotation = Quaternion.Euler(-(ScrollDirection < 0 ? -1f : 1f) * (_leadAngle + (360f - _leadAngle) * TownRackState.Progress(_clock)), 0f, 0f);
            if (progress >= 1f) { _turning = false; Root.localRotation = Quaternion.identity; }
        }
        if (_indicatorPage != Page || _indicatorCount != PageCount)
        { _indicatorPage = Page; _indicatorCount = PageCount; _pageLabel.text = "↑\n" + (Page % 256 + 1) + " / " + PageCount + "\n↓"; }
        _pageLabelGate.alpha = PageCount > 1 ? opacity : 0f;
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
        if (_disposed) return; _disposed = true; ContentRoots.Remove(Content); foreach (Transform cards in _rowContents) ContentRoots.Remove(cards); VRInteractables.UnregisterGrabbable(this);
        foreach (Material material in _materials) UnityEngine.Object.Destroy(material);
        _audio.Dispose();
        UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_housing);
    }
}
