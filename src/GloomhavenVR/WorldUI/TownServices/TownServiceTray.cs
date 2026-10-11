using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>A movable, horizontal sample tray. The existing panel handle owns carrying, laser
/// reeling and resize; this wrapper owns only its authored furniture and instruction graphic.</summary>
internal sealed class TownServiceTray : IPanelGrabOwner, IDisposable
{
    internal const float Width = .44f;
    internal const float Depth = .32f;
    private static readonly int VisibilityId = Shader.PropertyToID("_TownVisibility");
    private readonly GameObject _root;
    private readonly float _baseScale;
    private readonly List<Material> _materials = new();
    private readonly List<MeshRenderer> _renderers = new();
    private GrabBarVisual? _bar;
    private PanelGrabHandle? _handle;
    private Canvas? _canvas;
    private CanvasGroup? _captionGroup;
    private TextMeshProUGUI? _caption;
    private bool _disposed;
    private float _visibility = -1f;

    /// <summary>Local origin is the worktop contact plane, with the .44 by .32 metre drop
    /// region centred on x/z. Carrying and resizing preserve this coordinate contract.</summary>
    internal Transform Root => _root.transform;
    internal Transform? Content => _canvas != null ? _canvas.transform : null;
    internal TextMeshProUGUI? CaptionSource => _caption;
    internal Transform? HandleRoot => _bar?.Root;
    /// <summary>The actual local rod hierarchy, for an original-template presentation mirror.
    /// It contains the shared shaft/cap meshes and its laser capsule, but no grab controller.</summary>
    internal Transform? OriginalHandleTemplate => _bar?.Root;
    internal IReadOnlyList<Material> Materials => _materials;
    internal bool IsGrabbed => _handle != null && _handle.IsGrabbed;

    /// <summary>Build the same source hierarchy for original-widget lookup without opening a
    /// local service. Its inactive handle owns no input or reel; the caller owns disposal.</summary>
    internal static TownServiceTray CreateTemplate(TMP_Text? nativeText)
    {
        var tray = new TownServiceTray(nativeText, Vector3.zero, Quaternion.identity, 1f);
        tray._root.SetActive(false);
        return tray;
    }

    internal TownServiceTray(TMP_Text? nativeText, Vector3 position, Quaternion rotation, float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
            throw new ArgumentOutOfRangeException(nameof(scale));
        GameObject? prefab = TownServiceAssets.Prefab("townworktray");
        if (prefab == null) throw new InvalidOperationException("Town work tray is missing from the asset bundle");
        _baseScale = scale;
        _root = new GameObject("GloomhavenVR.TownService.WorkTray");
        try
        {
            Root.SetPositionAndRotation(position, LevelPose.Compose(LevelPose.TwistDegrees(rotation, Vector3.up), 0f));
            Root.localScale = Vector3.one * scale;
            GameObject furniture = UnityEngine.Object.Instantiate(prefab, Root, false);
            furniture.name = "Furniture";
            // The authored prefab's root already is the contact plane. Never derive it from
            // renderer bounds: handles, feet or future decorations are not the worktop.
            furniture.transform.localPosition = Vector3.zero;
            furniture.transform.localRotation = Quaternion.identity;
            furniture.transform.localScale = Vector3.one;
            foreach (Collider collider in furniture.GetComponentsInChildren<Collider>(true))
            { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            OwnFurnitureMaterials(furniture);
            BuildHandle();
            BuildCaption(nativeText);
            VRLayers.Apply(_root);
            Loc.OnChanged += RefreshCaption;
            RefreshCaption();
            SetVisibility(1f);
        }
        catch { Dispose(); throw; }
    }

    private void OwnFurnitureMaterials(GameObject furniture)
    {
        var copies = new Dictionary<Material, Material>();
        foreach (MeshRenderer renderer in furniture.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.enabled) _renderers.Add(renderer);
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null) continue;
                if (!copies.TryGetValue(source, out Material copy))
                {
                    copy = new Material(source);
                    copies.Add(source, copy); _materials.Add(copy);
                }
                materials[i] = copy;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private void BuildHandle()
    {
        // The same three-piece Generic rod used by original VR windows; no replacement mesh,
        // private highlight writer or carry implementation. Generic uses its original window
        // material, including the baked rod strip and depth-writing Overlay shader.
        _bar = GrabBarVisual.Build(Root, "Handle", GrabBarStyle.Generic, GrabBarLayout.BarRadius, overlay: true);
        _materials.Add(_bar.Material);
        foreach (MeshRenderer renderer in _bar.Renderers) _renderers.Add(renderer);
        GrabBarLayout.Rod size = GrabBarLayout.Solve(Width, Depth, 1f);
        Vector3 position = new(0f, -.025f, -Depth * .5f - size.Gap);
        _bar.Root.localPosition = position;
        _bar.Root.localScale = Vector3.one * size.Scale;
        _bar.SetLength(size.BarWidth / size.Scale);
        Collider laser = _bar.AttachLaserTarget();

        var reach = new GameObject("HandleReach");
        reach.transform.SetParent(Root, false);
        reach.transform.localPosition = position;
        // Only the handle gets an input collider. The worktop remains a coordinate-based drop
        // region, and the caption has no raycaster that could intercept original service buttons.
        var palm = reach.AddComponent<BoxCollider>();
        palm.isTrigger = true;
        palm.size = new Vector3(size.ZoneWidth, size.ZoneDepth, size.ZoneDepth);
        _handle = reach.AddComponent<PanelGrabHandle>(); // collider first: OnEnable registers it
        _handle.Init(this, _bar.Renderer, "WorldUI", "Town service tray");
        _handle.SetBarCollider(laser);
    }

    private void BuildCaption(TMP_Text? nativeText)
    {
        var canvasGo = new GameObject("Instructions", typeof(RectTransform));
        canvasGo.transform.SetParent(Root, false);
        canvasGo.transform.localPosition = new Vector3(0f, .001f, -.085f);
        canvasGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        canvasGo.transform.localScale = Vector3.one * .001f;
        var rect = (RectTransform)canvasGo.transform;
        rect.sizeDelta = new Vector2(400f, 72f);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = CanvasConversion.WorldCamera;
        _captionGroup = canvasGo.AddComponent<CanvasGroup>();
        _captionGroup.interactable = false; _captionGroup.blocksRaycasts = false;

        var label = new GameObject("Hint", typeof(RectTransform));
        label.transform.SetParent(rect, false);
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        _caption = label.AddComponent<TextMeshProUGUI>();
        if (nativeText != null && nativeText.font != null) _caption.font = nativeText.font;
        else WorldUIAssets.TryAssignGameFont(_caption);
        if (_caption.font == null) throw new InvalidOperationException("Town work tray needs the native UI font");
        _caption.fontSharedMaterial = _caption.font.material;
        _caption.alignment = TextAlignmentOptions.Center;
        _caption.fontSize = 24f; _caption.enableAutoSizing = true;
        _caption.fontSizeMin = 18f; _caption.fontSizeMax = 24f;
        _caption.color = new Color(.95f, .86f, .68f);
        _caption.raycastTarget = false;
    }

    private void RefreshCaption()
    {
        if (_caption != null) _caption.text = Loc.Mod("town_sample_hint");
    }

    /// <summary>The presentation owner may drive this scalar from its shared appearance clock.
    /// Only private material instances change; native continuation never waits for appearance.</summary>
    internal void SetVisibility(float value)
    {
        if (_disposed) return;
        value = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
        if (Mathf.Approximately(_visibility, value)) return;
        _visibility = value;
        foreach (Material material in _materials)
            if (material != null && material.HasProperty(VisibilityId)) material.SetFloat(VisibilityId, value);
        foreach (MeshRenderer renderer in _renderers)
            if (renderer != null) renderer.enabled = value > 0f;
        if (_captionGroup != null) _captionGroup.alpha = value;
    }

    internal void Tick()
    {
        if (_disposed || _canvas == null) return;
        Camera? camera = CanvasConversion.WorldCamera;
        if (_canvas.worldCamera != camera) _canvas.worldCamera = camera;
    }

    /// <summary>The shared PanelGrabHandle updates the root itself. There is deliberately no
    /// second pose writer in LateTick; callers may sample Root here for multiplayer publication.</summary>
    internal void LateTick() { }

    Transform? IPanelGrabOwner.GrabRoot => !_disposed ? Root : null;
    bool IPanelGrabOwner.GrabVisible => !_disposed && _visibility > 0f && _root.activeInHierarchy;
    PanelCarryMode IPanelGrabOwner.CarryMode => PanelCarryMode.Level;
    Quaternion IPanelGrabOwner.GrabLevelFrame => Quaternion.identity;
    Vector2 IPanelGrabOwner.GrabPitchLimits => Vector2.zero;
    Vector2 IPanelGrabOwner.GrabScaleLimits => new(_baseScale * PanelGrabHandle.MinScale, _baseScale * PanelGrabHandle.MaxScale);

    void IPanelGrabOwner.OnGrabFinished()
    {
        if (_disposed) return;
        // Preserve the player's chosen yaw and position. A shared horizontal worktop never
        // turns toward whichever client's headset happened to end the gesture.
        Root.rotation = LevelPose.Compose(LevelPose.TwistDegrees(Root.rotation, Vector3.up), 0f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loc.OnChanged -= RefreshCaption;
        if (_root != null) _root.SetActive(false); // unregisters the handle and relinquishes its reel immediately
        foreach (Material material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
        _materials.Clear(); _renderers.Clear();
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _bar = null; _handle = null; _caption = null; _canvas = null; _captionGroup = null;
    }
}
