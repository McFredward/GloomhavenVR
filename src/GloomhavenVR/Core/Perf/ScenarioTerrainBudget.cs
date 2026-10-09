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
    private static Func<Material, Material>? _worldVariant;
    private static Func<IDisposable>? _worldReadPass;
    private static Func<bool>? _worldEnabled;
    private static Func<Material, bool>? _worldOwns;
    private static Func<bool>? _assetsReady, _assetsUnavailable;
    private static Func<Renderer, bool>? _performanceWallHidden;
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
    internal static void ConfigureWorldMaterialIntegration(Func<Material, Material> variant,
        Func<IDisposable> readPass, Func<bool> enabled, Func<Material, bool> owns)
    { _worldVariant = variant; _worldReadPass = readPass; _worldEnabled = enabled; _worldOwns = owns; }
    internal static void ConfigureAssetPreparation(Func<bool> assetsReady, Func<bool> assetsUnavailable)
    { _assetsReady = assetsReady; _assetsUnavailable = assetsUnavailable; }
    internal static void ConfigurePerformanceWallVisibility(Func<Renderer, bool> hidden) =>
        _performanceWallHidden = hidden;
    private static bool PerformanceWallHidden(Renderer renderer) =>
        _performanceWallHidden?.Invoke(renderer) == true;
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
        PerfMonitor.RegisterDebug("Terrain.CameraCandidates");
        PerfMonitor.RegisterDebug("Terrain.CameraSubstitutes");
        PerfMonitor.RegisterDebug("Terrain.CameraBudgetFallback");
        PerfMonitor.RegisterDebug("Terrain.CameraFrustumFallback");
        PerfMonitor.RegisterDebug("ScenarioTerrain.BudgetDeferred");
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
    // Prepared membership selects the terrain owner before culling. Revocation
    // instead needs an actual owned lease, even if a later native callback has
    // already overwritten the physical renderer mask. Keep the two readers apart.
    internal static bool HasCurrentRenderLease(Renderer renderer) =>
        _driver != null && renderer != null && _driver.HasCurrentRenderLease(renderer);

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
        // Positively audited wall bodies/pillars from pcg_crypt/cave/city, in addition
        // to immutable bank provenance AND live ProceduralWall ownership below.
        // The bank also contains floors, props, foundations and anonymous meshes:
        // membership alone cannot authorize a coarse terrain representation.
        return AuthoredName(mesh.name) is "EN_CR_Pillar_Thin" or "EN_CR_Pillar_Large"
            or "EN_CR_Pillar_Large_02" or "EN_CR_Wall_Basic_Tall" or "EN_CR_Wall_TrimBasic_01"
            or "CV_Pillar_Generic_01" or "CV_Pillar_Generic_02"
            or "CV_Wall_Generic_01" or "CV_Wall_Generic_02" or "CV_Wall_Generic_03"
            or "CV_Wall_Generic_04" or "CV_Wall_Generic_05"
            or "CV_Wall_Generic_Thin_01" or "CV_Wall_Generic_Thin_Narrow_01"
            or "CV_Wall_Generic_Thin_Narrow_02"
            // Build627's three-room PC capture contains these ten exact definitions.
            // Their original pcg_city prefabs and existing bank streams were audited;
            // wall shelves/candles/tapestries, bases and arbitrary CR_INT names are
            // deliberately absent. A matching mesh inside an under-wall or doorway
            // template is still native (StructuralBoundary below), even when that
            // template is itself nested under a ProceduralWall.
            or "CR_INT_Stone_Int_Wall_01" or "CR_INT_Stone_Int_Wall_02"
            or "CR_INT_Stone_Int_Wall_03" or "CR_INT_Stone_Int_Wall_04"
            or "CR_INT_Stone_Int_Wall_02_Narrow" or "CR_INT_Stone_Pillar_04"
            or "CR_INT_Wooden_Int_Pillar_Single"
            or "CR_INT_Wooden_Hut_Pillars_01" or "CR_INT_Wooden_Hut_Pillars_02"
            or "CR_INT_Wooden_Hut_Pillars_03";
    }
    private static bool StructuralBoundary(string name)
    {
        // pcg_city uses identical wall/pillar meshes in TO_INT_* wall bodies AND
        // PCG_TO_INT_UnderWall / TO_INT_*Doorway / Entrance / EXIT templates.
        // Immutable mesh identity cannot distinguish those uses. Conservatively
        // retain such native ancestor boundaries, including late reparenting.
        return name.IndexOf("UnderWall", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("UnderFloor", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Foundation", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Slab", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("TopCap", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Doorway", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("DoorFrame", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Entrance", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_EXIT_", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    private static bool Gate(Material material, string key) =>
        material.HasProperty(key) && material.GetFloat(key) != 0f;

    private struct ScopeState { internal bool Valid, Generated, Structural, Scenario, Prop; }
    private static bool NativeScope(MeshRenderer renderer, Dictionary<Transform, ScopeState> scopes,
        Dictionary<int, bool> scenes, List<Transform> ancestry, List<GameObject> roots, List<Component> components,
        HashSet<Transform>? propRoots)
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
            string nodeName = node.name;
            // Build628 Frame evidence records 16--17ms average terrain pre-cull and
            // >60ms spikes with three rooms. One native component-list read replaces
            // twelve separate native lookups for each distinct ancestor. Keep this
            // verdict scoped to this invocation: components and parents added between
            // eyes must still revoke admission before native culling.
            node.GetComponents(components);
            bool blocked = BlockedName(nodeName);
            foreach (Component component in components)
            {
                if (component is null) continue;
                ComponentRole role = Classify(component);
                blocked |= (role & ComponentRole.Blocked) != 0;
                state.Structural |= (role & ComponentRole.Structural) != 0;
                state.Scenario |= (role & ComponentRole.Scenario) != 0;
            }
            components.Clear();
            state.Valid &= !blocked;
            state.Generated |= nodeName == "Generated Content";
            state.Prop |= propRoots?.Contains(node) == true;
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
        return state.Scenario && (propRoots != null ? !state.Prop
            : !HeldProps.OwnsRendererOf(renderer.transform) && !PropGrab.OwnsRendererOf(renderer.transform));
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
    private sealed partial class Driver : MonoBehaviour
    {
        private readonly Dictionary<int, Surface> _surfaces = new();
        private readonly Queue<Transform> _pending = new();
        private readonly HashSet<int> _queued = new();
        private readonly List<int> _dead = new();
        private readonly List<ProceduralMapTile> _tiles = new();
        private readonly List<Material> _materialScratch = new();
        private readonly Dictionary<Material, Material> _cheap = new();
        private readonly HashSet<Material> _readThisCamera = new();
        private readonly Dictionary<Material, int> _routesThisCamera = new();
        private readonly Dictionary<Material, Material> _canonicalThisCamera = new();
        private readonly Dictionary<Material, Material> _worldVariantsThisCamera = new();
        private readonly HashSet<int> _discovered = new();
        private readonly int[] _refusals = new int[4];
        private readonly List<Surface> _leases = new();
        private readonly Dictionary<Transform, ScopeState> _scopeThisInvocation = new();
        private readonly Dictionary<int, bool> _sceneThisInvocation = new();
        private readonly List<Transform> _ancestry = new();
        private readonly List<GameObject> _sceneRoots = new();
        private readonly List<Component> _scopeComponents = new();
        private readonly Dictionary<Mesh, bool> _meshThisInvocation = new();
        private Camera? _leaseCamera;
        private bool _active;
        private int _walls;
        private int _reportedSurfaces = -1, _reportedSettings;
        private int _reportedDiscovery = -1;
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
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { _active = false; ClearValidation(); }
        private void SceneUnloaded(Scene scene) { RestoreAll(); _active = false; }
        private bool Enabled => PerfConfig.TerrainSubstitutionOn && VRSession.IsRunning && (PerfConfig.CheapWallShadingOn
            || PerfConfig.TerrainDetailPercent < 100 || PerfConfig.DistantTerrainDetailPercent < 100);
        internal void QueueRoot(GameObject root)
        {
            if (Enabled && root != null && _queued.Add(root.GetInstanceID())) _pending.Enqueue(root.transform);
        }
        private void Seed()
        {
            _discovered.Clear(); Array.Clear(_refusals, 0, _refusals.Length);
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
                // The independent CPU-first comparison retains all selected detail
                // settings and cheap native-slot world shading. Off has no terrain
                // geometry/preparation work after this release; On reseeds from exact
                // native sources and uses the existing continuous morph again.
                if (!PerfConfig.TerrainSubstitutionOn)
                {
                    if (_active || _surfaces.Count != 0 || _pending.Count != 0) RestoreAll();
                    _active = false;
                    return;
                }
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
                    // QueueRoot keys GameObjects. Removing the Transform identity
                    // leaves permanent membership and loses later readiness/unmask
                    // callbacks for a source that has already been traversed once.
                    _queued.Remove(node.gameObject.GetInstanceID());
                    for (int child = 0; child < node.childCount; child++) QueueRoot(node.GetChild(child).gameObject);
                    MeshRenderer renderer = node.GetComponent<MeshRenderer>();
                    // The wall owner queues its exact released source through
                    // MaterialReady. No hidden-discovery polling set is necessary.
                    if (renderer != null && PerformanceWallHidden(renderer)) continue;
                    MeshFilter filter = node.GetComponent<MeshFilter>();
                    if (renderer == null || filter == null || filter.sharedMesh == null) continue;
                    int id = renderer.GetInstanceID();
                    bool first = _discovered.Add(id);
                    // Reject unsupported identities before decoding any bank member.
                    // Preparation can be expensive during loading, but it should prepare
                    // only sources which can actually save work on the settled board.
                    int refusal = FloorIdentity(filter.sharedMesh) ? 1 : !StructuralIdentity(filter.sharedMesh) ? 2
                        : !CurrentScope(renderer) ? 0
                        : _eligibleMesh?.Invoke(filter.sharedMesh) != true ? 3 : -1;
                    if (refusal >= 0) { if (first) _refusals[refusal]++; continue; }
                    // A genuine readiness/release callback may arrive after a native
                    // mesh replacement. Replace the old private record at this lifecycle
                    // boundary; otherwise its later validation would remove it after the
                    // queued source had already been consumed and lose rediscovery.
                    if (_surfaces.TryGetValue(id, out Surface previous) && previous.Original != filter.sharedMesh)
                    { previous.Dispose(); _surfaces.Remove(id); _priorityDirty = true; }
                    if (!_surfaces.ContainsKey(id)) { _surfaces.Add(id, new Surface(renderer, filter, transform)); _priorityDirty = true; }
                }
                Camera? camera = Rig.VRRigDriver.HeadCamera;
                // Frame636's settled three-room capture spends about 1.7ms per frame
                // in this Update. Tracked poses and the detail settings are identical
                // throughout this synchronous loop; capture them once instead of
                // crossing Unity's transform boundary for each prepared wall. Renderer
                // bounds and mesh ownership remain current per source, and the next
                // Update always captures new poses/settings (including tracking loss).
                DetailState detail = default;
                bool detailReady = false;
                foreach (KeyValuePair<int, Surface> item in _surfaces)
                {
                    Surface surface = item.Value;
                    // Hide-all owns source visibility and has already released any
                    // terrain lease. No native validation, detail reads or morph work
                    // can contribute pixels here. A broad forceRenderingOff test would
                    // also suspend unrelated native visibility and is deliberately absent.
                    if (PerformanceWallHidden(surface.Renderer)) continue;
                    if (!surface.Validate(_meshThisInvocation)) { surface.Dispose(); _dead.Add(item.Key); continue; }
                    if (!detailReady && active && camera != null) { detail = new DetailState(camera); detailReady = true; }
                    int percent = active && camera != null ? DetailFor(surface, detail) : 100;
                    surface.StepGeometry(percent, delta);
                }
                foreach (int id in _dead) { _surfaces.Remove(id); _priorityDirty = true; }
                _dead.Clear();
                if (!active) { _pending.Clear(); _queued.Clear(); }
                int settings = (PerfConfig.CheapWallShadingOn ? 1 : 0) + PerfConfig.TerrainDetailPercent * 2
                    + PerfConfig.DistantTerrainDetailPercent * 202;
                if (_pending.Count == 0 && (_reportedSurfaces != _surfaces.Count || _reportedSettings != settings
                    || _reportedDiscovery != _discovered.Count)
                    && Time.unscaledTime >= _nextReport)
                {
                    _reportedSurfaces = _surfaces.Count; _reportedSettings = settings; _nextReport = Time.unscaledTime + 2f;
                    _reportedDiscovery = _discovered.Count;
                    VRLog.Note("Perf", "Scenario terrain budget: surfaces=" + _surfaces.Count + ", cheap="
                        + PerfConfig.CheapWallShadingOn + ", detail=" + PerfConfig.TerrainDetailPercent
                        + "%, distantDetail=" + PerfConfig.DistantTerrainDetailPercent + "%.");
                    if (VRLog.Level >= VRLogLevel.Debug)
                        VRLog.Debug("Perf", "Scenario terrain coverage: prepared=" + _surfaces.Count
                            + ", distinctRendererVisits=" + _discovered.Count + ", refusedScope=" + _refusals[0]
                            + ", refusedFloor=" + _refusals[1] + ", refusedIdentity=" + _refusals[2]
                            + ", refusedBank=" + _refusals[3]
                            + "; preparation membership, not visible GPU draws. Triangle counters count surviving paired camera leases.");
                }
                ClearValidation();
            }
        }
        private bool CurrentScope(MeshRenderer renderer)
        {
            bool shared = PerfConfig.SharedEnvironmentMaterialReadsOn;
            if (shared) ReadCurrentPropRoots();
            return NativeScope(renderer, _scopeThisInvocation, _sceneThisInvocation,
                _ancestry, _sceneRoots, _scopeComponents, shared ? _propRoots : null);
        }
        private void ClearValidation()
        {
            _scopeThisInvocation.Clear(); _sceneThisInvocation.Clear(); _ancestry.Clear(); _sceneRoots.Clear();
            _scopeComponents.Clear(); _meshThisInvocation.Clear();
            _propRoots.Clear(); _propVisuals.Clear(); _propRootsReady = false;
        }
        private readonly struct HandProximity
        {
            internal readonly bool Tracked;
            internal readonly Vector3 Position;
            internal readonly float Scale;
            internal HandProximity(VRHand? hand)
            {
                Tracked = hand != null && hand.HasPose;
                Position = Tracked ? hand!.transform.position : default;
                Scale = Tracked ? Mathf.Max(hand!.WorldScale, .0001f) : 1f;
            }
        }
        private readonly struct DetailState
        {
            internal readonly Vector3 Position;
            internal readonly float Scale, Distance;
            internal readonly int Near, Distant;
            internal readonly HandProximity Left, Right;
            internal DetailState(Camera camera)
            {
                Transform head = camera.transform;
                Position = head.position;
                Scale = Mathf.Max(Mathf.Abs(head.lossyScale.x), .0001f);
                Left = new HandProximity(VRHands.Left); Right = new HandProximity(VRHands.Right);
                Near = PerfConfig.TerrainDetailPercent; Distant = PerfConfig.DistantTerrainDetailPercent;
                Distance = PerfConfig.TerrainDistanceMeters;
            }
        }
        private static int DetailFor(Surface surface, DetailState detail)
        {
            if (surface.Floor) return 100;
            // The cylinder needs the current source matrix instead of a world AABB.
            // Preserve the existing native bounds read only for ordinary surfaces
            // or a tracked hand's exact, unchanged closest-bounds touch guard.
            Bounds bounds = !surface.Pillar || detail.Left.Tracked || detail.Right.Tracked
                ? surface.Renderer.bounds : default;
            float metres = surface.HeadDistance(detail.Position, bounds) / detail.Scale;
            // Small hysteresis keeps a parked threshold from repeatedly morphing the same mesh.
            float edge = detail.Distance + (surface.Distant ? -.04f : .04f);
            bool protectedNear = metres < .18f || NearHand(detail.Left, bounds) || NearHand(detail.Right, bounds);
            if (surface.Pillar)
            {
                // The maintainer's radial follow-up (2026-10-09): the old 18cm touch
                // guard still left visibly coarse pillars throughout normal close viewing.
                // Frame's saved near/far0% made the configured distance inert (0 -> 0).
                // Protect original pillar geometry throughout that adjustable VR radius,
                // including with both caps at0. Figures retain their size-based policy;
                // this radius does not depend on head yaw or a pillar's full height.
                surface.Distant = metres > edge;
                return protectedNear || !surface.Distant ? 100 : Mathf.Min(detail.Near, detail.Distant);
            }
            // Other scenery keeps its existing leaning/touch guard and near/far caps.
            // Interactive and held objects are excluded separately.
            if (protectedNear) return 100;
            surface.Distant = metres > edge;
            return surface.Distant ? Mathf.Min(detail.Near, detail.Distant) : detail.Near;
        }
        private static bool NearHand(HandProximity hand, Bounds bounds) => hand.Tracked
            && Vector3.Distance(hand.Position, bounds.ClosestPoint(hand.Position)) / hand.Scale < .12f;

        internal bool OwnsRenderSubstitute(Renderer renderer) => PerfConfig.TerrainSubstitutionOn
            && !PerformanceWallHidden(renderer)
            && _surfaces.TryGetValue(renderer.GetInstanceID(), out Surface surface)
            && surface.WantsSubstitute(_active);
        internal bool HasCurrentRenderLease(Renderer renderer) => _surfaces.TryGetValue(renderer.GetInstanceID(), out Surface surface)
            && surface.HasCurrentRenderLease;
        internal void ReleaseLease(Renderer renderer)
        {
            if (_surfaces.TryGetValue(renderer.GetInstanceID(), out Surface surface)) surface.Unmask();
            ClearMaterialReads();
            // A native writer may also register/reparent a prop or replace a scope
            // component during a callback. Neither its ancestry nor prop-root verdict
            // may survive into the remainder of the same synchronous camera pass.
            ClearValidation();
        }
        internal void RecoverLeases()
        {
            foreach (Surface surface in _leases) surface.Unmask();
            _leases.Clear(); _leaseCamera = null; ClearMaterialReads();
            ClearValidation();
        }
        private void ClearMaterialReads()
        {
            _readThisCamera.Clear(); _routesThisCamera.Clear();
            _canonicalThisCamera.Clear(); _worldVariantsThisCamera.Clear();
        }
        private void HandlePostRender(Camera camera)
        {
            if (camera != _leaseCamera) return;
            int originals = 0, submitted = 0, cheap = 0;
            foreach (Surface surface in _leases)
            {
                // A later native MPB/material/visibility writer can revoke a pre-cull
                // admission. Count only leases still owned at this paired render end.
                // Frustum/occlusion and GPU execution remain outside this measurement.
                if (!surface.IsMasked) continue;
                originals += surface.OriginalTriangles; submitted += surface.DrawTriangles;
                if (surface.CheapLease) cheap++;
            }
            PerfMonitor.Count("Terrain.OriginalTriangles", originals);
            PerfMonitor.Count("Terrain.SubmittedTriangles", submitted);
            PerfMonitor.Count("Terrain.CheapSurfaces", cheap);
            RecoverLeases();
        }
        private void HandlePreCull(Camera camera)
        {
            try
            {
                // A nested/foreign camera cannot inherit a source mask. Command-buffer
                // DrawRenderer retains its original identity, shader and geometry too.
                RecoverLeases();
                if (camera == null || camera != Rig.VRRigDriver.HeadCamera || !VRSession.IsRunning
                    || !isActiveAndEnabled || !PerfConfig.TerrainSubstitutionOn
                    || (_nativeCameraConsumers?.Invoke(camera) ?? camera.commandBufferCount > 0)) return;
                using (PerfMonitor.Scope("ScenarioTerrain.PreCull"))
                using (_worldReadPass?.Invoke())
                {
                    _leaseCamera = camera;
                    PreparePriority();
                    bool shared = PerfConfig.SharedEnvironmentMaterialReadsOn;
                    PrepareFrustum(camera, shared);
                    Matrix4x4 ownerPose = shared ? transform.localToWorldMatrix : default;
                    Vector3 ownerScale = shared ? transform.lossyScale : default;
                    int limit = PerfConfig.TerrainCameraSourceLimit;
                    bool substitute = Enabled;
                    bool world = _worldEnabled?.Invoke() == true;
                    bool cheap = PerfConfig.CheapWallShadingOn;
                    int candidates = 0, budgetFallback = 0, budgetDeferred = 0, frustumFallback = 0;
                    for (int index = 0; index < _priority.Count; index++)
                    {
                        // Build638 still reads native visibility for ~455 sources/frame
                        // AFTER the 64-candidate cap has exhausted its admission budget.
                        // None can acquire a lease in this invocation. RecoverLeases above
                        // already released every previous proxy, so leave the untouched
                        // remainder native without crossing Unity for each rejected source.
                        // This is a prepared-source count, NOT an examined active fallback.
                        if (shared && limit > 0 && candidates >= limit)
                        { budgetDeferred = _priority.Count - index; break; }
                        Surface surface = _priority[index];
                        if (PerformanceWallHidden(surface.Renderer)) continue;
                        // Prepared surfaces include unopened rooms. Reject their native
                        // disabled/inactive renderers before bank/material/proxy work.
                        // No admission verdict survives this camera invocation.
                        if (surface.Renderer == null || !surface.Renderer.enabled
                            || !surface.Renderer.gameObject.activeInHierarchy || surface.Renderer.forceRenderingOff
                            || !surface.WantsSubstitute(substitute)) continue;
                        // Bound full guard/copy work, not merely successful masks. Rejected
                        // candidates also cost CPU; budget fallback keeps original output.
                        if (limit > 0 && candidates >= limit) { budgetFallback++; continue; }
                        if (OutsideFrustum(surface.Renderer)) { frustumFallback++; continue; }
                        candidates++;
                        if (!surface.Validate(_meshThisInvocation)
                            || surface.Renderer.isPartOfStaticBatch || surface.Renderer.additionalVertexStreams != null
                            || !CurrentScope(surface.Renderer)) continue;
                        surface.Renderer.GetSharedMaterials(_materialScratch);
                        if (_materialScratch.Count != surface.Original.subMeshCount) continue;
                        bool supported = true;
                        foreach (Material material in _materialScratch) supported &= CurrentMaterialRoute(CurrentCanonicalMaterial(material, shared)) >= 0;
                        if (!supported) continue;
                        // Never mask until ALL required slot submissions have valid materials.
                        surface.EnsureSlots(_materialScratch.Count);
                        for (int slot = 0; slot < _materialScratch.Count; slot++)
                        {
                            Material original = CurrentCanonicalMaterial(_materialScratch[slot], shared);
                            Material next = world ? CurrentWorldVariant(original, shared)
                                : cheap ? CheapMaterial(original) : _materialScratch[slot];
                            // A global shader refusal must keep the whole native source.
                            // The older cheap shader cannot substitute unknown world effects.
                            supported &= !world || _worldOwns?.Invoke(next) == true;
                            surface.SetMaterial(slot, next);
                        }
                        if (!supported || Array.Exists(surface.Materials, material => material == null)) continue;
                        if (!surface.PrepareProxy(shared, ownerPose, ownerScale)) continue;
                        surface.CheapLease = world || cheap;
                        // Canonical/world material callbacks may render another camera,
                        // which recovers the prior leases and clears their camera owner.
                        // The resumed outer pass owns each newly acquired lease again.
                        _leaseCamera = camera;
                        surface.Mask(); _leases.Add(surface);
                    }
                    if (PerfMonitor.StepsActive && VRLog.Level >= VRLogLevel.Debug)
                    {
                        PerfMonitor.Count("Terrain.CameraCandidates", candidates);
                        PerfMonitor.Count("Terrain.CameraSubstitutes", _leases.Count);
                        PerfMonitor.Count("Terrain.CameraBudgetFallback", budgetFallback);
                        PerfMonitor.Count("Terrain.CameraFrustumFallback", frustumFallback);
                        PerfMonitor.Count("ScenarioTerrain.BudgetDeferred", budgetDeferred);
                    }
                }
                ClearMaterialReads();
            }
            catch (Exception error) { RecoverLeases(); FailOpen(error); }
        }
        private Material CurrentCanonicalMaterial(Material material, bool shared)
        {
            if (material == null) return material!;
            if (!shared) return CanonicalMaterial(material);
            if (_canonicalThisCamera.TryGetValue(material, out Material original)) return original;
            original = CanonicalMaterial(material); _canonicalThisCamera[material] = original; return original;
        }
        private Material CurrentWorldVariant(Material original, bool shared)
        {
            if (shared && _worldVariantsThisCamera.TryGetValue(original, out Material cached)) return cached;
            // One outer read pass owns this synchronous loop. Native writes restore
            // leases, and the next eye starts with empty maps; mutable native shader,
            // texture, keyword and ownership verdicts never survive that boundary.
            Material next = _worldVariant?.Invoke(original) ?? original;
            if (shared) _worldVariantsThisCamera.Add(original, next);
            return next;
        }
        private int CurrentMaterialRoute(Material material)
        {
            if (material == null) return -1;
            if (_routesThisCamera.TryGetValue(material, out int route)) return route;
            route = MaterialRoute(material); _routesThisCamera.Add(material, route); return route;
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
                material.SetFloat("_GHVRTerrainNativeRoute", CurrentMaterialRoute(original));
            }
            return material;
        }
        internal void RestoreAll()
        {
            RecoverLeases();
            foreach (Surface surface in _surfaces.Values) surface.Dispose();
            _surfaces.Clear(); _priority.Clear(); _priorityDirty = false; _pending.Clear(); _queued.Clear();
            foreach (Material material in _cheap.Values) if (material != null) UnityEngine.Object.Destroy(material);
            _cheap.Clear();
            _reportedSurfaces = -1;
            _reportedDiscovery = -1; _discovered.Clear(); Array.Clear(_refusals, 0, _refusals.Length);
        }
    }
}
