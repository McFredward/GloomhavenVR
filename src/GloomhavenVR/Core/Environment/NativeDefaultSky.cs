using System;
using System.IO;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>
/// The original scenario sky's render assets for scenes which have no sky of their
/// own. Build740's user request extends Default to the menu and map as well. The
/// source prefab is only Transform/MeshFilter/MeshRenderer; no native controller,
/// callback, light, scene load or gameplay object is needed to display it.
/// </summary>
internal static class NativeDefaultSky
{
    internal const string Resource = "GloomhavenVR.DefaultSky.bundle";
    private const string MeshAsset = "native/default-sky/mesh";
    private const string MaterialAsset = "native/default-sky/material";
    // Exact level8 GH_SkySphere_PR -> GH_SkySphere authored transforms. Preserve
    // their offset/flattened shape/yaw; centering the mesh bounds changes horizon.
    private const float AuthoredRootScale = 1.0629240274429321f;
    private static readonly Vector3 AuthoredOffset = new(0f, 14.749992370605469f, 0f);
    private static readonly Vector3 AuthoredScale = new(.15803000330924988f, .06696362793445587f, .15803000330924988f);
    private static readonly Quaternion AuthoredRotation = new(0f, .22705163061618805f, 0f, .9738827347755432f);
    // Use the existing rig's 12-unit fallback as one common presentation
    // reference (VRRigDriver.FallbackWorldScale), independently of saved/current
    // zoom. Actual scenario auto-scale varies with the board and stays native.
    // The original half-kilounit mesh and authored transforms then give a
    // roughly 7 m horizontal / 2.97 m vertical radius in both menu and map.
    private const float NativeReferenceUnitsPerMeter = 12f;
    private static AssetBundle? _bank;
    private static Mesh? _mesh;
    private static Material? _sourceMaterial;
    private static Material? _material;
    private static GameObject? _go;
    private static MeshRenderer? _renderer;
    private static Transform? _rig;
    private static Camera? _head;
    private static bool _failed;
    private static bool _placed;

    internal static Renderer? Renderer => _renderer;
    internal static bool Owns(Renderer? renderer) => renderer != null && ReferenceEquals(renderer, _renderer);

    /// <summary>The real rig/camera lifetime, rather than locomotion or zoom, owns
    /// the presentation. A menu-to-map rebuild must not retain the old sky lease.</summary>
    internal static bool RefreshLifetime()
    {
        if (ReferenceEquals(_rig, null)) return false;
        if (ReferenceEquals(_rig, VRRigDriver.RigRoot)
            && ReferenceEquals(_head, VRRigDriver.HeadCamera)) return false;
        Release();
        return true;
    }

    internal static Renderer? Acquire()
    {
        Camera? head = VRRigDriver.HeadCamera;
        Transform? rig = VRRigDriver.RigRoot;
        if (!VRSession.IsRunning || !Events.MenuWorldContext.AllowsSurroundings
            || head == null || rig == null) return null;
        float baseScale = VRRigDriver.BaseWorldScale;
        if (!(baseScale > 0f) || float.IsNaN(baseScale) || float.IsInfinity(baseScale)) return null;
        if (_renderer != null) return _renderer;
        if (!EnsureAssets()) return null;
        _rig = rig;
        _head = head;
        _go = new GameObject("GloomhavenVR.NativeDefaultSky", typeof(MeshFilter), typeof(MeshRenderer));
        _go.SetActive(false);
        _go.hideFlags = HideFlags.HideAndDontSave;
        VRLayers.Apply(_go);
        UnityEngine.Object.DontDestroyOnLoad(_go);
        _go.GetComponent<MeshFilter>().sharedMesh = _mesh;
        _material = new Material(_sourceMaterial!)
        { name = "GloomhavenVR.NativeDefaultSky.Material", hideFlags = HideFlags.HideAndDontSave };
        _renderer = _go.GetComponent<MeshRenderer>();
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        UpdatePose();
        _go.SetActive(true);
        // The locked rig tail budgets clipping before MixedReality acquires the
        // shell. Consume this one new lease before its first camera draw too.
        VRRigDriver.RefreshDefaultSkyClipPlanes(head);
        VRLog.Info("Core", "Default sky: original scenario mesh/material/animated shader available without a scenario scene; head-only render assets.");
        return _renderer;
    }

    /// <summary>
    /// Place the finite original shell once at arrival. The maintainer's Build742
    /// review requires the same ability to leave this bubble as the scenario sky.
    /// Following the live eye and fitting its live far plane made that impossible.
    /// Preserve the exact authored observer offset/shape/yaw, with one uniform
    /// conversion through the rig's unzoomed reference. Movement, recenter, zoom
    /// and clip changes must never move or resize the occupied shell. The native
    /// scenario renderer remains authoritative whenever one exists.
    /// </summary>
    internal static void UpdatePose()
    {
        if (_placed || _go == null || _head == null) return;
        Vector3 authoredScale = AuthoredScale * AuthoredRootScale;
        Vector3 authoredOffset = AuthoredOffset * AuthoredRootScale;
        float factor = VRRigDriver.BaseWorldScale / NativeReferenceUnitsPerMeter;
        Vector3 position = _head.transform.position + authoredOffset * factor;
        Vector3 scale = authoredScale * factor;
        _go.transform.SetPositionAndRotation(position, AuthoredRotation);
        _go.transform.localScale = scale;
        _placed = true;
    }

    /// <summary>Cover only this live render-only shell, including lookback after
    /// flying outside. Its finite geometry owns the budget; changing the camera
    /// budget never moves the shell. Native scenarios, MR and other styles retain
    /// their existing policy when this private presentation has been released.</summary>
    internal static float MinFarWorldUnits()
    {
        if (!_placed || _go == null || !_go.activeInHierarchy || _renderer == null
            || !_renderer.enabled || _head == null || !VRSession.IsRunning
            || !Events.MenuWorldContext.AllowsSurroundings
            || !ReferenceEquals(_head, VRRigDriver.HeadCamera)
            || !ReferenceEquals(_rig, VRRigDriver.RigRoot)) return 0f;
        Bounds bounds = _renderer.bounds;
        return (Vector3.Distance(_head.transform.position, bounds.center) + bounds.extents.magnitude) * 1.02f;
    }

    /// <summary>Remove only this render-only presentation. Immutable source bank
    /// assets may be reused on the next explicit style/rig transition.</summary>
    internal static void Release()
    {
        if (_go != null)
        {
            _go.SetActive(false);
            UnityEngine.Object.Destroy(_go);
        }
        if (_material != null) UnityEngine.Object.Destroy(_material);
        _go = null;
        _renderer = null;
        _material = null;
        _rig = null;
        _head = null;
        _placed = false;
    }

    internal static void Reset()
    {
        Release();
        _mesh = null;
        _sourceMaterial = null;
        if (_bank != null) _bank.Unload(true);
        _bank = null;
        _failed = false;
    }

    private static bool EnsureAssets()
    {
        if (_mesh != null && _sourceMaterial != null) return true;
        if (_failed) return false;
        try
        {
            using Stream? resource = typeof(NativeDefaultSky).Assembly.GetManifestResourceStream(Resource);
            if (resource == null) throw new FileNotFoundException("missing native default sky bank", Resource);
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            _bank = AssetBundle.LoadFromMemory(bytes.ToArray());
            if (_bank == null) throw new InvalidDataException("native default sky bank could not load");
            _mesh = _bank.LoadAsset<Mesh>(MeshAsset);
            _sourceMaterial = _bank.LoadAsset<Material>(MaterialAsset);
            if (_mesh == null || _sourceMaterial == null || _sourceMaterial.shader == null
                || _sourceMaterial.shader.name != "AMP_SkyShader")
                throw new InvalidDataException("incomplete native default sky render assets");
            if (!_sourceMaterial.shader.isSupported)
                throw new NotSupportedException("the original native default sky shader is unsupported on this graphics API");
            return true;
        }
        catch (Exception error)
        {
            Reset();
            _failed = true;
            VRLog.Warn("Core", "Default sky unavailable without a native scene on " + SystemInfo.graphicsDeviceType
                + " (" + error.GetType().Name + ": " + error.Message + ").");
            return false;
        }
    }
}
