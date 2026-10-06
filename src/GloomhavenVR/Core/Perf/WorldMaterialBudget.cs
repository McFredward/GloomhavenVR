using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Configurable world shading on the actual native renderer and original geometry.
/// The October 6 Frame capture remains slow after full room loading; copying every
/// renderer into another per-eye proxy would add to that measured CPU problem.
/// Only private material variants and conditionally owned slot references change.
/// Native visibility, transforms, meshes, controllers and property blocks stay live.
/// </summary>
internal static partial class WorldMaterialBudget
{
    private const string ShaderName = "GloomhavenVR/WorldSimpleMaterial";
    private static Driver? _driver;
    private static Func<Material, Material>? _canonicalSource;
    private static Action<Renderer>? _sourceChanged;
    private static Func<Renderer, bool>? _substituteOwnership;

    internal static void ConfigureCanonicalSource(Func<Material, Material> source) => _canonicalSource = source;
    internal static void ConfigureSourceChanged(Action<Renderer> changed) => _sourceChanged = changed;
    internal static void ConfigureRenderSubstituteOwnership(Func<Renderer, bool> owns) => _substituteOwnership = owns;
    internal static void Install(GameObject host)
    {
        if (_driver != null) return;
        _driver = host.AddComponent<Driver>();
        try { ScenarioCameraCullBoundary.Install(); ScenarioCameraCullBoundary.Subscribe(_driver.PreCull); }
        catch (Exception error) { _driver.Fail(error); }
        foreach (string counter in new[] { "WorldMaterial.Candidates", "WorldMaterial.VariantSlots",
            "WorldMaterial.NativeSlots", "WorldMaterial.MaterialRefreshes", "WorldMaterial.ScopeRefusals",
            "WorldMaterial.ShaderRefusals", "WorldMaterial.EffectRefusals" }) PerfMonitor.RegisterDebug(counter);
    }
    internal static void Shutdown()
    {
        if (_driver == null) return;
        ScenarioCameraCullBoundary.Unsubscribe(_driver.PreCull);
        _driver.RestoreAll(true);
        UnityEngine.Object.Destroy(_driver); _driver = null;
    }
    internal static void QueueRoot(GameObject root) { if (root != null) _driver?.QueueRoot(root); }
    // Additional world roots must come from a producer's actual native object;
    // neither arbitrary names nor material-family membership establish provenance.
    internal static void RegisterWorldRoot(GameObject root) { if (root != null) _driver?.RegisterWorldRoot(root); }
    internal static void MaterialReady(Renderer renderer)
    { if (renderer is MeshRenderer mesh) _driver?.Adopt(mesh); }
    internal static void BeforeNativeRendererWrite(Renderer renderer)
    { if (renderer != null) _driver?.RestoreRenderer(renderer); }
    internal static void BeforeNativeContentChange() => _driver?.RestoreBindings();
    // Deliberately does not call _canonicalSource: root composes this reverse map
    // with the older environment reverse map, so doing so here would recurse.
    internal static Material CanonicalMaterial(Material material) => _driver?.Canonical(material) ?? material;
    internal static bool IsOwnedVariant(Material material) => _driver?.IsVariant(material) == true;
    internal static Material VariantFor(Material original)
    {
        if (_driver == null) return original;
        using IDisposable pass = BeginMaterialReadPass();
        return _driver.VariantFor(original);
    }
    internal static IDisposable BeginMaterialReadPass() => _driver?.BeginPass() ?? EmptyPass.Instance;
    private sealed class EmptyPass : IDisposable
    {
        internal static readonly EmptyPass Instance = new();
        public void Dispose() { }
    }
    private sealed class ReadPass : IDisposable
    {
        private Driver? _owner;
        internal ReadPass(Driver owner) => _owner = owner;
        public void Dispose() { Driver? owner = _owner; _owner = null; owner?.EndPass(); }
    }
    private sealed class Surface
    {
        internal readonly MeshRenderer Renderer;
        internal readonly MeshFilter Filter;
        internal bool Refused;
        internal Surface(MeshRenderer renderer, MeshFilter filter) { Renderer = renderer; Filter = filter; }
    }
    [DefaultExecutionOrder(30008)]
    private sealed partial class Driver : MonoBehaviour
    {
        private const int NodesPerFrame = 128;
        private readonly Dictionary<int, Surface> _surfaces = new();
        private readonly Queue<Transform> _pending = new();
        private readonly HashSet<int> _queued = new();
        private readonly List<int> _dead = new();
        private readonly List<Material> _slots = new();
        private readonly List<MeshRenderer> _renderers = new();
        private readonly List<GameObject> _roots = new();
        private readonly List<Renderer> _changedSources = new();
        private readonly HashSet<Transform> _worldRoots = new();
        private int _mode = -1, _passDepth, _refreshes;
        private bool _failed;
        private float _nextDebug;

        private void Awake()
        {
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
        }
        private void OnDisable() => RestoreBindings();
        private void OnDestroy()
        {
            ScenarioCameraCullBoundary.Unsubscribe(PreCull);
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            RestoreAll(true);
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { if (VRSession.IsRunning) Seed(); }
        private void SceneUnloaded(Scene scene) => RestoreAll(true);
        internal IDisposable BeginPass()
        {
            if (_passDepth++ == 0) { _prepared.Clear(); _scopes.Clear(); _sceneScopes.Clear(); _refreshes = 0; }
            return new ReadPass(this);
        }
        internal void EndPass()
        {
            if (--_passDepth == 0) { _prepared.Clear(); _scopes.Clear(); _sceneScopes.Clear(); }
        }
        private void Settings()
        {
            int mode = VRSession.IsRunning && !_failed ? PerfConfig.WorldMaterialQualityMode : 0;
            if (mode == _mode) return;
            RestoreAll(true); _mode = mode;
            VRLog.Note("Perf", "World material quality: requested=" + mode + "; native geometry and visibility retained.");
            if (mode > 0) Seed();
        }
        internal void RegisterWorldRoot(GameObject root)
        { _worldRoots.Add(root.transform); QueueRoot(root); }
        private void Seed()
        {
            // One scene/settings-entry inventory. Subsequent native placement and
            // material-ready callbacks enqueue bounded subtrees, never a frame sweep.
            foreach (MapChoreographer map in UnityEngine.Object.FindObjectsOfType<MapChoreographer>(true))
            {
                if (map.worldMap != null) _worldRoots.Add(map.worldMap.transform);
                if (map.cityMap != null) _worldRoots.Add(map.cityMap.transform);
            }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                scene.GetRootGameObjects(_roots);
                foreach (GameObject root in _roots) QueueRoot(root);
                _roots.Clear();
            }
        }
        internal void QueueRoot(GameObject root)
        {
            if (!VRSession.IsRunning || PerfConfig.WorldMaterialQualityMode == 0 || _failed || root == null) return;
            if (_queued.Add(root.GetInstanceID())) _pending.Enqueue(root.transform);
        }
        private void Update()
        {
            try
            {
                Settings();
                if (_mode == 0) return;
                using IDisposable pass = BeginPass();
                int count = NodesPerFrame;
                while (_pending.Count > 0 && count-- > 0)
                {
                    Transform node = _pending.Dequeue();
                    if (node == null) continue;
                    _queued.Remove(node.gameObject.GetInstanceID());
                    MeshRenderer renderer = node.GetComponent<MeshRenderer>();
                    if (renderer != null) Adopt(renderer);
                    for (int i = 0; i < node.childCount; i++) QueueRoot(node.GetChild(i).gameObject);
                }
            }
            catch (Exception error) { Fail(error); }
        }
        internal void Adopt(MeshRenderer renderer)
        {
            if (_failed || renderer == null || !VRSession.IsRunning || PerfConfig.WorldMaterialQualityMode == 0) return;
            Settings();
            // Adopt records candidates, not a cross-camera eligibility verdict.
            // All mutable native guards are read again immediately before rendering.
            using IDisposable pass = BeginPass();
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null && InWorld(renderer) && !_surfaces.ContainsKey(renderer.GetInstanceID()))
                _surfaces[renderer.GetInstanceID()] = new Surface(renderer, filter);
        }
        internal void PreCull(Camera camera)
        {
            try
            {
                Settings();
                if (_mode == 0 || !isActiveAndEnabled) return;
                using IDisposable timing = PerfMonitor.Scope("WorldMaterial.PreCull");
                using IDisposable pass = BeginPass();
                int candidates = 0, changed = 0, native = 0, scopeRefusals = 0, shaderRefusals = 0, effectRefusals = 0;
                foreach (KeyValuePair<int, Surface> pair in _surfaces)
                {
                    Surface surface = pair.Value;
                    if (surface.Renderer == null || surface.Filter == null) { _dead.Add(pair.Key); continue; }
                    MeshRenderer renderer = surface.Renderer;
                    candidates++;
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy
                        || renderer.forceRenderingOff && _substituteOwnership?.Invoke(renderer) != true
                        || surface.Filter.sharedMesh == null || !InWorld(renderer))
                    {
                        bool restored = RestoreRenderer(renderer);
                        if (restored || !surface.Refused) _changedSources.Add(renderer);
                        surface.Refused = true; scopeRefusals++; continue;
                    }
                    renderer.GetSharedMaterials(_slots);
                    bool write = false, refused = false;
                    for (int slot = 0; slot < _slots.Count; slot++)
                    {
                        Material current = _slots[slot], original = Source(current);
                        Material next = original;
                        bool effect = RendererEffect(renderer, slot);
                        if (original != null && !effect) next = VariantFor(original);
                        else effectRefusals++;
                        if (IsVariant(next)) changed++;
                        else { native++; refused = true; if (original != null && !effect) shaderRefusals++; }
                        if (current != next) { _slots[slot] = next; write = true; }
                    }
                    // Keep slot count/order exactly, including unsupported/foreign
                    // slots. Never assign an empty array to a native room template.
                    if (write) renderer.sharedMaterials = _slots.ToArray();
                    if (write || refused && !surface.Refused) _changedSources.Add(renderer);
                    surface.Refused = refused;
                    _slots.Clear();
                }
                foreach (int id in _dead) _surfaces.Remove(id);
                _dead.Clear();
                // The environment bridge can synchronously re-adopt this source.
                // Notify after enumeration; unchanged owned slots never notify again.
                foreach (Renderer renderer in _changedSources) _sourceChanged?.Invoke(renderer);
                _changedSources.Clear();
                if (PerfMonitor.StepsActive && VRLog.Level >= VRLogLevel.Debug)
                {
                    PerfMonitor.Count("WorldMaterial.Candidates", candidates);
                    PerfMonitor.Count("WorldMaterial.VariantSlots", changed);
                    PerfMonitor.Count("WorldMaterial.NativeSlots", native);
                    PerfMonitor.Count("WorldMaterial.MaterialRefreshes", _refreshes);
                    PerfMonitor.Count("WorldMaterial.ScopeRefusals", scopeRefusals);
                    PerfMonitor.Count("WorldMaterial.ShaderRefusals", shaderRefusals);
                    PerfMonitor.Count("WorldMaterial.EffectRefusals", effectRefusals);
                    if (Time.unscaledTime >= _nextDebug)
                    {
                        _nextDebug = Time.unscaledTime + 10f;
                        VRLog.Debug("Perf", "World material coverage: requested=" + _mode + ", candidates=" + candidates
                            + ", effectiveSlots=" + changed + ", nativeSlots=" + native + ", uniqueRefreshes=" + _refreshes
                            + ", refusedScope=" + scopeRefusals + ", refusedShader=" + shaderRefusals
                            + ", refusedEffect=" + effectRefusals + "; source work, not visible GPU draws.");
                    }
                }
            }
            catch (Exception error) { Fail(error); }
        }
        internal bool RestoreRenderer(Renderer renderer)
        {
            renderer.GetSharedMaterials(_slots);
            bool changed = false;
            for (int slot = 0; slot < _slots.Count; slot++)
            {
                Material current = _slots[slot], original = Canonical(current);
                if (original != current) { _slots[slot] = original; changed = true; }
            }
            if (changed) renderer.sharedMaterials = _slots.ToArray();
            _slots.Clear();
            return changed;
        }
        internal void RestoreBindings()
        {
            foreach (Surface surface in _surfaces.Values)
                if (surface.Renderer != null) RestoreRenderer(surface.Renderer);
        }
        internal void RestoreAll(bool inherited)
        {
            RestoreBindings();
            if (inherited && _originalByVariant.Count > 0)
            {
                // Rare Off/scene/teardown edge also repairs an unknown clone which
                // borrowed an owned reference. No scene inventory runs per frame.
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.IsValid() || !scene.isLoaded) continue;
                    scene.GetRootGameObjects(_roots);
                    foreach (GameObject root in _roots)
                    {
                        root.GetComponentsInChildren(true, _renderers);
                        foreach (MeshRenderer renderer in _renderers) if (renderer != null) RestoreRenderer(renderer);
                        _renderers.Clear();
                    }
                    _roots.Clear();
                }
            }
            foreach (Material material in _variants.Values) if (material != null) UnityEngine.Object.Destroy(material);
            _variants.Clear(); _originalByVariant.Clear(); _prepared.Clear(); _scopes.Clear(); _sceneScopes.Clear();
            _changedSources.Clear();
            _surfaces.Clear(); _pending.Clear(); _queued.Clear();
        }
        internal void Fail(Exception error)
        {
            if (_failed) return;
            _failed = true;
            try { RestoreAll(true); }
            finally { VRLog.Note("Perf", "World material budget failed; native materials and continuation retained ("
                + error.GetType().Name + ": " + error.Message + ")."); }
        }
    }
}
