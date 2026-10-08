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
    private static Func<Renderer, bool>? _substituteRevocation;
    private static Func<bool>? _ensureAssets;
    private static Action? _beforeVariantDisposal;
    private static Func<float>? _ambientWeight;

    internal static void ConfigureCanonicalSource(Func<Material, Material> source) => _canonicalSource = source;
    internal static void ConfigureSourceChanged(Action<Renderer> changed) => _sourceChanged = changed;
    internal static void ConfigureRenderSubstituteOwnership(Func<Renderer, bool> owns) => _substituteOwnership = owns;
    // Eligibility may include prepared/cap-deferred geometry. Revocation must
    // identify an actual live or queued consumer, otherwise unchanged refusals
    // would remove/re-adopt native sources every eye without releasing a draw.
    internal static void ConfigureRenderSubstituteRevocation(Func<Renderer, bool> needsRelease) => _substituteRevocation = needsRelease;
    internal static void ConfigureAssetPreparation(Func<bool> ensureLoaded) => _ensureAssets = ensureLoaded;
    internal static void ConfigureBeforeVariantDisposal(Action restoreConsumers) => _beforeVariantDisposal = restoreConsumers;
    internal static void ConfigureAmbientWeight(Func<float> weight) => _ambientWeight = weight;
    internal static void Install(GameObject host)
    {
        if (_driver != null) return;
        _driver = host.AddComponent<Driver>();
        try { ScenarioCameraCullBoundary.Install(); ScenarioCameraCullBoundary.Subscribe(_driver.PreCull); }
        catch (Exception error) { _driver.Fail(error); }
        foreach (string counter in new[] { "WorldMaterial.Candidates", "WorldMaterial.VariantSlots",
            "WorldMaterial.NativeSlots", "WorldMaterial.MaterialRefreshes", "WorldMaterial.ScopeRefusals",
            "WorldMaterial.ShaderRefusals", "WorldMaterial.EffectRefusals", "WorldMaterial.FactoryVariantRefreshes",
            "WorldMaterial.PropRootReads", "WorldMaterial.ScopeNodeReads", "WorldMaterial.InactiveCandidates",
            "WorldMaterial.CameraExcludedCandidates" }) PerfMonitor.RegisterDebug(counter);
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
    { if (renderer != null) _driver?.BeforeRendererWrite(renderer); }
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
        // A Component cannot move to another GameObject or acquire a different
        // Transform during its lifetime. Cache those exact owning references,
        // not their mutable layer, parent, name, active state or components.
        internal readonly GameObject Object;
        internal readonly Transform Transform;
        internal MeshFilter? Filter;
        internal Mesh? Mesh;
        internal bool Refused;
        internal Surface(MeshRenderer renderer, MeshFilter filter)
        {
            Renderer = renderer; Object = renderer.gameObject; Transform = renderer.transform;
            Filter = filter; Mesh = filter.sharedMesh;
        }
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
            _block = new MaterialPropertyBlock(); _slotBlock = new MaterialPropertyBlock();
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
        private bool Requested => VRSession.IsRunning && !_failed && PerfConfig.WorldMaterialQualityMode > 0;
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { if (Requested) Seed(); }
        private void SceneUnloaded(Scene scene)
        {
            RestoreAll(true);
            _worldRoots.RemoveWhere(root => root == null || root.gameObject.scene == scene);
            // An additive unload can leave the current world alive. RestoreAll
            // cleared its candidates too; unchanged settings do not rediscover them.
            if (Requested) Seed();
        }
        internal IDisposable BeginPass()
        {
            // Existing references still need transition/consumer reads until their
            // owner restores them. A settled Off path has nothing to refresh.
            if (!Requested && _originalByVariant.Count == 0) return EmptyPass.Instance;
            if (_passDepth++ == 0)
            {
                _prepared.Clear(); _scopes.Clear(); _sceneScopes.Clear(); _refreshes = 0;
                _metadata.Clear(); _shareReads = PerfConfig.SharedEnvironmentMaterialReadsOn;
                _passAmbient = _shareReads ? _ambientWeight?.Invoke() ?? 1f : 1f;
                _propRootReads = 0; _scopeNodeReads = 0;
            }
            // A nested producer/material-ready boundary can register or reparent
            // an interactive visual. Never reuse its earlier ancestry verdict.
            InvalidateScopeReads();
            return new ReadPass(this);
        }
        internal void EndPass()
        {
            if (--_passDepth == 0)
            { _prepared.Clear(); _metadata.Clear(); _scopes.Clear(); _sceneScopes.Clear(); InvalidateScopeReads(); }
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
        { _worldRoots.Add(root.transform); InvalidateScopeReads(); QueueRoot(root); }
        private void Seed()
        {
            _worldRoots.RemoveWhere(root => root == null);
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
                if (_passDepth > 1)
                {
                    // A real nested Camera.Render is a new native mutation
                    // boundary, even when an earlier factory read pass remains
                    // open. Its current shaders/keywords/ambient cannot borrow
                    // prepared material verdicts from that earlier camera.
                    _prepared.Clear(); _metadata.Clear();
                    _shareReads = PerfConfig.SharedEnvironmentMaterialReadsOn;
                    _passAmbient = _shareReads ? _ambientWeight?.Invoke() ?? 1f : 1f;
                    _refreshes = 0; _propRootReads = 0; _scopeNodeReads = 0;
                }
                int candidates = 0, changed = 0, native = 0, scopeRefusals = 0, shaderRefusals = 0, effectRefusals = 0, inactive = 0, cameraExcluded = 0;
                foreach (KeyValuePair<int, Surface> pair in _surfaces)
                {
                    Surface surface = pair.Value;
                    if (surface.Renderer == null) { _dead.Add(pair.Key); continue; }
                    MeshRenderer renderer = surface.Renderer;
                    bool ownershipRead = false, substitute = false;
                    bool revocationRead = false, needsRevocation = false;
                    bool HasSubstitute()
                    {
                        if (!ownershipRead)
                        { ownershipRead = true; substitute = _substituteOwnership?.Invoke(renderer) == true; }
                        return substitute;
                    }
                    bool NeedsSubstituteRevocation()
                    {
                        // Preserve the old integration/fixture boundary until the
                        // exact consumer reader is wired; reuse its one cached read.
                        if (_substituteRevocation is null) return HasSubstitute();
                        if (!revocationRead)
                        { revocationRead = true; needsRevocation = _substituteRevocation(renderer); }
                        return needsRevocation;
                    }
                    candidates++;
                    // Options-panel captures do not draw world layers, but used to repeat
                    // every world scope/material/MPB read anyway. Ask this camera's actual
                    // mask and each source's current layer, never a cached layer union or
                    // a camera name. Native DrawRenderer command buffers ignore that mask;
                    // current/queued geometry consumers can also have a different layer.
                    // Those consumers retain the complete path and synchronous revocation.
                    // BeginPass/nested invalidation above still run on a zero-match camera.
                    if (camera != null && camera.commandBufferCount == 0
                        && (camera.cullingMask & (1 << surface.Object.layer)) == 0
                        && !NeedsSubstituteRevocation())
                    { cameraExcluded++; continue; }
                    // A native closed room does not need mesh/component reads.
                    // Restore owned slots immediately; on its first active render
                    // re-read the mesh identity and all current ownership guards.
                    if (!renderer.enabled || !surface.Object.activeInHierarchy
                        || renderer.forceRenderingOff && !HasSubstitute())
                    {
                        bool restored = RestoreRenderer(renderer);
                        if (restored || !surface.Refused || NeedsSubstituteRevocation()) _changedSources.Add(renderer);
                        surface.Refused = true; scopeRefusals++; inactive++; continue;
                    }
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null!;
                    bool geometryChanged = surface.Filter != filter || surface.Mesh != mesh;
                    surface.Filter = filter; surface.Mesh = mesh;
                    if (geometryChanged) RestoreRenderer(renderer);
                    if (filter == null || mesh == null || !InWorld(renderer, surface.Object, surface.Transform))
                    {
                        bool restored = RestoreRenderer(renderer);
                        if (restored || geometryChanged || !surface.Refused || NeedsSubstituteRevocation()) _changedSources.Add(renderer);
                        surface.Refused = true; scopeRefusals++; continue;
                    }
                    renderer.GetSharedMaterials(_slots);
                    // Renderer-wide MPBs are identical for every native subslot in
                    // this synchronous loop. Fetch once; slot blocks remain distinct.
                    bool blocks = renderer.HasPropertyBlock();
                    if (blocks) renderer.GetPropertyBlock(_block);
                    bool write = false, refused = _slots.Count == 0;
                    for (int slot = 0; slot < _slots.Count; slot++)
                    {
                        Material current = _slots[slot], original = Source(current);
                        Material next = original;
                        bool effect = RendererEffect(renderer, slot, original, blocks);
                        if (original != null && !effect) next = Prepare(original);
                        else effectRefusals++;
                        if (IsVariant(next)) changed++;
                        else { native++; refused = true; if (original != null && !effect) shaderRefusals++; }
                        if (current != next) { _slots[slot] = next; write = true; }
                    }
                    // Keep slot count/order exactly, including unsupported/foreign
                    // slots. Never assign an empty array to a native room template.
                    if (write) renderer.sharedMaterials = _slots.ToArray();
                    // A previous refusal does not revoke a NEW earlier per-eye
                    // substitute lease. Native slots can already be restored while
                    // a terrain/chunk consumer renews its private draw next eye.
                    // Revoke every currently refused live substitute, including a
                    // slot-local effect/shader refusal or deliberately empty slots.
                    if (write || geometryChanged || refused && (!surface.Refused || NeedsSubstituteRevocation())) _changedSources.Add(renderer);
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
                    PerfMonitor.Count("WorldMaterial.PropRootReads", _propRootReads);
                    PerfMonitor.Count("WorldMaterial.ScopeNodeReads", _scopeNodeReads);
                    PerfMonitor.Count("WorldMaterial.InactiveCandidates", inactive);
                    PerfMonitor.Count("WorldMaterial.CameraExcludedCandidates", cameraExcluded);
                    if (Time.unscaledTime >= _nextDebug)
                    {
                        _nextDebug = Time.unscaledTime + 10f;
                        VRLog.Debug("Perf", "World material coverage: requested=" + _mode + ", candidates=" + candidates
                            + ", effectiveSlots=" + changed + ", nativeSlots=" + native + ", uniqueRefreshes=" + _refreshes
                            + ", refusedScope=" + scopeRefusals + ", refusedShader=" + shaderRefusals
                            + ", refusedEffect=" + effectRefusals + ", cameraExcluded=" + cameraExcluded
                            + "; source work, not visible GPU draws.");
                    }
                }
            }
            catch (Exception error) { Fail(error); }
        }
        internal void BeforeRendererWrite(Renderer renderer)
        { InvalidateScopeReads(); _prepared.Clear(); _metadata.Clear(); RestoreRenderer(renderer); }
        internal bool RestoreRenderer(Renderer renderer)
        {
            if (_originalByVariant.Count == 0) return false;
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
            InvalidateScopeReads(); _prepared.Clear(); _metadata.Clear();
            foreach (Surface surface in _surfaces.Values)
                if (surface.Renderer != null) RestoreRenderer(surface.Renderer);
        }
        internal void RestoreAll(bool inherited)
        {
            // Arrays are not the only consumers: earlier terrain proxies and queued
            // chunks/instance command buffers can own a factory-only variant. Release
            // ALL external consumers before destroying their referenced materials.
            bool destroy = true;
            if (_variants.Count > 0)
                try { _beforeVariantDisposal?.Invoke(); }
                catch (Exception error)
                {
                    destroy = false; _failed = true;
                    VRLog.Note("Perf", "World material consumer disposal failed; live private materials retained ("
                        + error.GetType().Name + ": " + error.Message + ").");
                }
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
            if (destroy)
            {
                foreach (Material material in _variants.Values) if (material != null) UnityEngine.Object.Destroy(material);
                _variants.Clear(); _originalByVariant.Clear();
            }
            _prepared.Clear(); _metadata.Clear(); _scopes.Clear(); _sceneScopes.Clear(); InvalidateScopeReads();
            _shader = null;
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
