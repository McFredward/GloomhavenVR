using System;
using System.Collections.Generic;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Hands;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Optional private 3D terrain presentation. The August static-batching experiment changed
/// native sources which Apparance subsequently cloned with empty material slots. This owner
/// never writes a native MeshFilter or material array and never attaches children to a native
/// template. It submits private geometry at the original matrix for one real head-camera
/// invocation, with the original renderer masked only for that invocation. Native visibility,
/// room reveal, colliders, lights, controllers, doors, actors and targeting remain authoritative.
/// </summary>
internal static partial class ScenarioTerrainBudget
{
    internal delegate bool MeshLookup(Mesh original, int detailPercent, out Mesh mesh);
    private static Func<Mesh, bool>? _eligibleMesh;
    private static MeshLookup? _lookup;
    private static Func<Camera, bool>? _nativeCameraConsumers;
    private static Func<Material, Material>? _canonicalMaterial;
    private static Func<bool>? _assetsReady, _assetsUnavailable;
    private static Driver? _driver;
    private static bool _failed;
    private const string ShaderName = "GloomhavenVR/ScenarioCheapTerrain";

    // A verified immutable original bank is required, even for geometry whose CPU copy
    // happens to be readable. Names alone cannot authorize an unrelated mesh replacement.
    internal static void ConfigureMeshBank(Func<Mesh, bool> eligibleMesh, MeshLookup lookup)
    { _eligibleMesh = eligibleMesh; _lookup = lookup; }
    internal static void ConfigureNativeCameraConsumers(Func<Camera, bool> hasNativeConsumers) =>
        _nativeCameraConsumers = hasNativeConsumers;
    internal static void ConfigureCanonicalMaterial(Func<Material, Material> canonicalMaterial) =>
        _canonicalMaterial = canonicalMaterial;
    internal static void ConfigureAssetPreparation(Func<bool> assetsReady, Func<bool> assetsUnavailable)
    { _assetsReady = assetsReady; _assetsUnavailable = assetsUnavailable; }
    private static Material CanonicalMaterial(Material material) =>
        material != null ? _canonicalMaterial?.Invoke(material) ?? material : material!;
    internal static void Install(GameObject host)
    {
        if (_driver != null) return;
        _failed = false;
        _driver = host.AddComponent<Driver>();
        PerfMonitor.Register("ScenarioTerrain.Update");
        PerfMonitor.Register("ScenarioTerrain.PreCull");
        PerfMonitor.Register("Terrain.OriginalTriangles");
        PerfMonitor.Register("Terrain.SubmittedTriangles");
        PerfMonitor.Register("Terrain.CheapSurfaces");
    }
    internal static void Shutdown()
    {
        if (_driver != null) { _driver.RestoreAll(); UnityEngine.Object.Destroy(_driver); }
        _driver = null; _failed = false;
    }
    internal static void Placed(GameObject root) => QueueRoot(root);
    internal static void QueueRoot(GameObject root)
    { if (!_failed && _driver != null && root != null) _driver.QueueRoot(root); }
    internal static void MaterialReady(Renderer renderer)
    { if (renderer != null) QueueRoot(renderer.gameObject); }
    internal static void BeforeNativeContentChange()
    { if (_driver != null) _driver.RecoverLeases(); }
    internal static void BeforeNativeRendererWrite(Renderer renderer)
    { if (_driver != null && renderer != null) _driver.ReleaseLease(renderer); }
    internal static bool OwnsRenderSubstitute(Renderer renderer) =>
        _driver != null && renderer != null && _driver.OwnsRenderSubstitute(renderer);

    private static void FailOpen(Exception error)
    {
        if (_failed) return;
        _failed = true;
        if (_driver != null) { _driver.enabled = false; _driver.RestoreAll(); }
        VRLog.Note("Perf", "Scenario terrain presentation failed; native rendering retained ("
            + error.GetType().Name + ": " + error.Message + ").");
    }

    private static string AuthoredName(string name)
    {
        while (name.EndsWith("(Clone)", StringComparison.Ordinal)
            || name.EndsWith("(Instance)", StringComparison.Ordinal))
            name = name.Substring(0, name.LastIndexOf('(')).TrimEnd();
        return name;
    }
    private static bool FloorIdentity(Mesh mesh) =>
        AuthoredName(mesh.name).IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool StructuralIdentity(Mesh mesh)
    {
        // Positively audited wall bodies/pillars from pcg_crypt/cave, in addition
        // to immutable bank provenance AND live ProceduralWall ownership below.
        // The bank also contains floors, props, foundations and anonymous meshes:
        // membership alone cannot authorize a coarse terrain representation.
        return AuthoredName(mesh.name) is "EN_CR_Pillar_Thin" or "EN_CR_Pillar_Large"
            or "EN_CR_Pillar_Large_02" or "EN_CR_Wall_Basic_Tall" or "EN_CR_Wall_TrimBasic_01"
            or "CV_Pillar_Generic_01" or "CV_Pillar_Generic_02"
            or "CV_Wall_Generic_01" or "CV_Wall_Generic_02" or "CV_Wall_Generic_03"
            or "CV_Wall_Generic_04" or "CV_Wall_Generic_05"
            or "CV_Wall_Generic_Thin_01" or "CV_Wall_Generic_Thin_Narrow_01"
            or "CV_Wall_Generic_Thin_Narrow_02";
    }
    private static bool Gate(Material material, string key) =>
        material.HasProperty(key) && material.GetFloat(key) != 0f;

    private struct ScopeState { internal bool Valid, Generated, Structural, Scenario; }
    private static bool NativeScope(MeshRenderer renderer, Dictionary<Transform, ScopeState> scopes,
        Dictionary<int, bool> scenes, List<Transform> ancestry, List<GameObject> roots)
    {
        ancestry.Clear();
        ScopeState state = new() { Valid = true };
        for (Transform? node = renderer.transform; node != null; node = node.parent)
        {
            if (scopes.TryGetValue(node, out state)) break;
            state = new ScopeState { Valid = true }; ancestry.Add(node);
        }
        for (int i = ancestry.Count - 1; i >= 0; i--)
        {
            Transform node = ancestry[i];
            bool blocked = node.GetComponent<Canvas>() != null || node.GetComponent<ActorBehaviour>() != null
                || node.GetComponent<ProceduralProp>() != null || node.GetComponent<ProceduralDoorway>() != null
                || node.GetComponent<UnityGameEditorDoorProp>() != null || node.GetComponent<CInteractable>() != null
                || node.GetComponent<Animator>() != null || node.GetComponent<Rigidbody>() != null || node.GetComponent<Light>() != null
                || node.GetComponent<SkinnedMeshRenderer>() != null || node.name == "Preview"
                || node.name.StartsWith("GloomhavenVR", StringComparison.Ordinal);
            state.Valid &= !blocked;
            state.Generated |= node.name == "Generated Content";
            state.Structural |= node.GetComponent<ProceduralWall>() != null;
            state.Scenario |= node.GetComponent<ProceduralScenario>() != null;
            scopes[node] = state;
        }
        ancestry.Clear();
        if (!state.Valid || !state.Generated || !state.Structural) return false;
        if (!state.Scenario)
        {
            Scene scene = renderer.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return false;
            if (!scenes.TryGetValue(scene.handle, out state.Scenario))
            {
                scene.GetRootGameObjects(roots);
                foreach (GameObject root in roots)
                    if (root.GetComponent<ProceduralScenario>() != null) { state.Scenario = true; break; }
                roots.Clear(); scenes.Add(scene.handle, state.Scenario);
            }
        }
        return state.Scenario && !HeldProps.OwnsRendererOf(renderer.transform)
            && !PropGrab.OwnsRendererOf(renderer.transform);
    }

    // The route is the original native clip equation, not a material-name guess.
    // Unsupported shader families and live vertex/emissive effects retain their native path.
    private static int MaterialRoute(Material material)
    {
        if (material == null || material.shader == null || material.renderQueue > 2500
            || Gate(material, "_AddVertexAnim") || Gate(material, "_UseEmissiveMap")
            || Gate(material, "_Diffuse_Emissive_On")) return -1;
        string shader = material.shader.name;
        if (shader == "Amp_Basic_WallFade") return 2;
        if (shader == "Amp_Low/Amp_Basic_WallFade_Low") return 1;
        if (shader == "Amp_Basic_N_MRAO") return Gate(material, "_WallFade_On")
            || material.IsKeywordEnabled("_WALLFADE_ON_ON") ? 3 : 0;
        if (shader == "Amp_Low/Amp_Basic_N_MRAO_Low")
            return Gate(material, "_WallFade_On") || material.IsKeywordEnabled("_WALLFADE_ON_ON") ? -1 : 0;
        return shader == "GloomhavenVR/ScenarioSimpleEnvironment" ? 0 : -1;
    }

    [DefaultExecutionOrder(30010)]
    private sealed class Driver : MonoBehaviour
    {
        private readonly Dictionary<int, Surface> _surfaces = new();
        private readonly Queue<Transform> _pending = new();
        private readonly HashSet<int> _queued = new();
        private readonly List<int> _dead = new();
        private readonly List<ProceduralMapTile> _tiles = new();
        private readonly List<Material> _materialScratch = new();
        private readonly Dictionary<Material, Material> _cheap = new();
        private readonly HashSet<Material> _readThisCamera = new();
        private readonly List<Surface> _leases = new();
        private readonly Dictionary<Transform, ScopeState> _scopeThisInvocation = new();
        private readonly Dictionary<int, bool> _sceneThisInvocation = new();
        private readonly List<Transform> _ancestry = new();
        private readonly List<GameObject> _sceneRoots = new();
        private Camera? _leaseCamera;
        private bool _active;
        private int _walls;
        private int _reportedSurfaces = -1, _reportedSettings;
        private float _nextReport;
        private bool _assetsWereReady = true, _assetFailureReported;
        private Shader? _shader;

        private void Awake()
        {
            Camera.onPreCull += HandlePreCull;
            Camera.onPostRender += HandlePostRender;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
        }
        private void OnDisable() => RecoverLeases();
        private void OnDestroy()
        {
            Camera.onPreCull -= HandlePreCull;
            Camera.onPostRender -= HandlePostRender;
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            RestoreAll();
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { _active = false; }
        private void SceneUnloaded(Scene scene) { RestoreAll(); _active = false; }
        private bool Enabled => VRSession.IsRunning && (PerfConfig.CheapWallShadingOn
            || PerfConfig.TerrainDetailPercent < 100 || PerfConfig.DistantTerrainDetailPercent < 100);
        internal void QueueRoot(GameObject root)
        {
            if (Enabled && root != null && _queued.Add(root.GetInstanceID())) _pending.Enqueue(root.transform);
        }
        private void Seed()
        {
            SceneRegistry.MapTiles.Collect(_tiles);
            foreach (ProceduralMapTile tile in _tiles) if (tile != null) QueueRoot(tile.gameObject);
            _tiles.Clear();
            List<ProceduralWall> walls = ProceduralWall.m_WallCache;
            if (walls != null) foreach (ProceduralWall wall in walls) if (wall != null) QueueRoot(wall.gameObject);
            _walls = walls?.Count ?? 0;
        }
        private void Update()
        {
            try { Tick(Time.unscaledDeltaTime); }
            catch (Exception error) { FailOpen(error); }
        }
        private void Tick(float delta)
        {
            RecoverLeases();
            using (PerfMonitor.Scope("ScenarioTerrain.Update"))
            {
                bool active = Enabled;
                if (active && !_active) Seed();
                bool ready = !active || (_assetsReady?.Invoke() ?? true);
                bool unavailable = !ready && _assetsUnavailable?.Invoke() == true;
                if (active && ready && !_assetsWereReady) Seed();
                _assetsWereReady = ready;
                if (unavailable)
                {
                    _pending.Clear(); _queued.Clear();
                    if (!_assetFailureReported)
                    {
                        _assetFailureReported = true;
                        VRLog.Note("Perf", "Scenario terrain assets unavailable; native rendering and continuation retained.");
                    }
                }
                else if (ready) _assetFailureReported = false;
                List<ProceduralWall> currentWalls = ProceduralWall.m_WallCache;
                if (active && currentWalls != null && currentWalls.Count != _walls)
                {
                    if (currentWalls.Count < _walls) _walls = 0;
                    for (int wall = _walls; wall < currentWalls.Count; wall++)
                        if (currentWalls[wall] != null) QueueRoot(currentWalls[wall].gameObject);
                    _walls = currentWalls.Count;
                }
                _active = active;
                int nodes = 128;
                while (active && ready && nodes-- > 0 && _pending.Count > 0)
                {
                    Transform node = _pending.Dequeue();
                    if (node == null) continue;
                    _queued.Remove(node.GetInstanceID());
                    for (int child = 0; child < node.childCount; child++) QueueRoot(node.GetChild(child).gameObject);
                    MeshRenderer renderer = node.GetComponent<MeshRenderer>();
                    MeshFilter filter = node.GetComponent<MeshFilter>();
                    if (renderer == null || filter == null || filter.sharedMesh == null || !CurrentScope(renderer)
                        || _eligibleMesh?.Invoke(filter.sharedMesh) != true || FloorIdentity(filter.sharedMesh)
                        || !StructuralIdentity(filter.sharedMesh)) continue;
                    int id = renderer.GetInstanceID();
                    if (!_surfaces.ContainsKey(id)) _surfaces.Add(id, new Surface(renderer, filter, transform));
                }
                Camera? camera = Rig.VRRigDriver.HeadCamera;
                foreach (KeyValuePair<int, Surface> item in _surfaces)
                {
                    Surface surface = item.Value;
                    if (!surface.Validate()) { surface.Dispose(); _dead.Add(item.Key); continue; }
                    int percent = active && camera != null ? DetailFor(surface, camera) : 100;
                    surface.StepGeometry(percent, delta);
                }
                foreach (int id in _dead) _surfaces.Remove(id);
                _dead.Clear();
                if (!active) { _pending.Clear(); _queued.Clear(); }
                int settings = (PerfConfig.CheapWallShadingOn ? 1 : 0) + PerfConfig.TerrainDetailPercent * 2
                    + PerfConfig.DistantTerrainDetailPercent * 202;
                if (_pending.Count == 0 && (_reportedSurfaces != _surfaces.Count || _reportedSettings != settings)
                    && Time.unscaledTime >= _nextReport)
                {
                    _reportedSurfaces = _surfaces.Count; _reportedSettings = settings; _nextReport = Time.unscaledTime + 2f;
                    VRLog.Note("Perf", "Scenario terrain budget: surfaces=" + _surfaces.Count + ", cheap="
                        + PerfConfig.CheapWallShadingOn + ", detail=" + PerfConfig.TerrainDetailPercent
                        + "%, distantDetail=" + PerfConfig.DistantTerrainDetailPercent + "%.");
                }
                ClearValidation();
            }
        }
        private bool CurrentScope(MeshRenderer renderer) => NativeScope(renderer, _scopeThisInvocation,
            _sceneThisInvocation, _ancestry, _sceneRoots);
        private void ClearValidation()
        { _scopeThisInvocation.Clear(); _sceneThisInvocation.Clear(); _ancestry.Clear(); _sceneRoots.Clear(); }
        private static int DetailFor(Surface surface, Camera camera)
        {
            if (surface.Floor) return 100;
            float scale = Mathf.Max(Mathf.Abs(camera.transform.lossyScale.x), .0001f);
            Bounds bounds = surface.Renderer.bounds;
            Vector3 nearest = bounds.ClosestPoint(camera.transform.position);
            float metres = Vector3.Distance(camera.transform.position, nearest) / scale;
            // Leaning into the scenery, or touching it with either tracked side, restores
            // exact original geometry. Interactive and held objects are excluded separately.
            if (metres < .18f || NearHand(VRHands.Left, bounds) || NearHand(VRHands.Right, bounds)) return 100;
            int near = PerfConfig.TerrainDetailPercent;
            // Small hysteresis keeps a parked threshold from repeatedly morphing the same mesh.
            float edge = PerfConfig.TerrainDistanceMeters + (surface.Distant ? -.04f : .04f);
            surface.Distant = metres > edge;
            return surface.Distant ? Mathf.Min(near, PerfConfig.DistantTerrainDetailPercent) : near;
        }
        private static bool NearHand(VRHand? hand, Bounds bounds) => hand != null && hand.HasPose
            && Vector3.Distance(hand.transform.position, bounds.ClosestPoint(hand.transform.position))
                / Mathf.Max(hand.WorldScale, .0001f) < .12f;

        internal bool OwnsRenderSubstitute(Renderer renderer) => _surfaces.TryGetValue(renderer.GetInstanceID(), out Surface surface)
            && surface.WantsSubstitute(_active);
        internal void ReleaseLease(Renderer renderer)
        {
            if (_surfaces.TryGetValue(renderer.GetInstanceID(), out Surface surface)) surface.Unmask();
        }
        internal void RecoverLeases()
        {
            foreach (Surface surface in _leases) surface.Unmask();
            _leases.Clear(); _leaseCamera = null; _readThisCamera.Clear();
            ClearValidation();
        }
        private void HandlePostRender(Camera camera) { if (camera == _leaseCamera) RecoverLeases(); }
        private void HandlePreCull(Camera camera)
        {
            try
            {
                // A nested/foreign camera cannot inherit a source mask. Command-buffer
                // DrawRenderer retains its original identity, shader and geometry too.
                RecoverLeases();
                if (camera == null || camera != Rig.VRRigDriver.HeadCamera || !VRSession.IsRunning
                    || !isActiveAndEnabled || (_nativeCameraConsumers?.Invoke(camera) ?? camera.commandBufferCount > 0)) return;
                using (PerfMonitor.Scope("ScenarioTerrain.PreCull"))
                {
                    _leaseCamera = camera;
                    int originals = 0, submitted = 0, cheap = 0;
                    foreach (Surface surface in _surfaces.Values)
                    {
                        if (!surface.Validate() || !surface.WantsSubstitute(Enabled)
                            || !surface.Renderer.enabled || !surface.Renderer.gameObject.activeInHierarchy
                            || surface.Renderer.forceRenderingOff || surface.Renderer.isPartOfStaticBatch
                            || surface.Renderer.additionalVertexStreams != null || !CurrentScope(surface.Renderer)) continue;
                        surface.Renderer.GetSharedMaterials(_materialScratch);
                        if (_materialScratch.Count != surface.Original.subMeshCount) continue;
                        bool supported = true;
                        foreach (Material material in _materialScratch) supported &= MaterialRoute(CanonicalMaterial(material)) >= 0;
                        if (!supported) continue;
                        // Never mask until ALL required slot submissions have valid materials.
                        surface.EnsureSlots(_materialScratch.Count);
                        for (int slot = 0; slot < _materialScratch.Count; slot++)
                            surface.Materials[slot] = PerfConfig.CheapWallShadingOn
                                ? CheapMaterial(CanonicalMaterial(_materialScratch[slot])) : _materialScratch[slot];
                        if (Array.Exists(surface.Materials, material => material == null)) continue;
                        if (!surface.PrepareProxy()) continue;
                        surface.Mask(); _leases.Add(surface);
                        originals += surface.OriginalTriangles; submitted += surface.DrawTriangles;
                        if (PerfConfig.CheapWallShadingOn) cheap++;
                    }
                    PerfMonitor.Count("Terrain.OriginalTriangles", originals);
                    PerfMonitor.Count("Terrain.SubmittedTriangles", submitted);
                    PerfMonitor.Count("Terrain.CheapSurfaces", cheap);
                }
                _readThisCamera.Clear();
            }
            catch (Exception error) { RecoverLeases(); FailOpen(error); }
        }
        private Material CheapMaterial(Material original)
        {
            if (_shader == null) _shader = BundleShaders.Resolve(ShaderName, "Perf",
                "Scenario terrain shader loaded.", "Scenario terrain shader unavailable; original rendering retained.");
            if (_shader == null) return null!;
            if (!_cheap.TryGetValue(original, out Material material))
            {
                material = new Material(original) { name = original.name + " (VR cheap terrain)", shader = _shader };
                _cheap.Add(original, material);
            }
            // Exact native changes between cameras/eyes remain live. Reuse only within
            // this invocation, and never store mutable native material verdicts across it.
            if (_readThisCamera.Add(original))
            {
                material.CopyPropertiesFromMaterial(original);
                material.shader = _shader;
                material.SetFloat("_GHVRTerrainNativeRoute", MaterialRoute(original));
            }
            return material;
        }
        internal void RestoreAll()
        {
            RecoverLeases();
            foreach (Surface surface in _surfaces.Values) surface.Dispose();
            _surfaces.Clear(); _pending.Clear(); _queued.Clear();
            foreach (Material material in _cheap.Values) if (material != null) UnityEngine.Object.Destroy(material);
            _cheap.Clear();
            _reportedSurfaces = -1;
        }
    }
}
