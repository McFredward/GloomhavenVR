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
    private static Func<bool>? _roomEnabled;
    private static Func<int>? _floorDetailPercent;
    private static Func<Mesh, int>? _roomRole;
    private static Func<Renderer, bool>? _floorGrouping;
    private static Action<Renderer>? _floorMeshReady;
    private static Func<int>? _floorCameraLimit;
    private static Func<bool>? _floorPreparation;
    private static Func<MaterialPropertyBlock, Material, bool>? _unsupportedBlock;
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
    internal static void ConfigureRoomArchitecture(Func<bool> enabled, Func<int> floorDetailPercent)
    { _roomEnabled = enabled; _floorDetailPercent = floorDetailPercent; }
    internal static void ConfigureArchitectureBank(Func<Mesh, int> role) => _roomRole = role;
    internal static void ConfigureFloorGrouping(Func<Renderer, bool> hasPreparedFloorGroup) => _floorGrouping = hasPreparedFloorGroup;
    internal static void ConfigureFloorMeshReady(Action<Renderer> meshReady) => _floorMeshReady = meshReady;
    internal static void ConfigureRoomFloorCameraSourceLimit(Func<int> sourceLimit) => _floorCameraLimit = sourceLimit;
    internal static void ConfigureRoomFloorPreparation(Func<bool> required) => _floorPreparation = required;
    internal static void ConfigureBlockEffectAdmission(Func<MaterialPropertyBlock, Material, bool> unsupported) => _unsupportedBlock = unsupported;
    internal static IDisposable BeginFloorReadPass() => new FloorReadPass(_driver);
    private static int CurrentFloorRole(Mesh mesh) => _driver != null
        ? _driver.CurrentFloorRole(mesh) : _roomRole?.Invoke(mesh) ?? 0;
    private sealed class FloorReadPass : IDisposable
    {
        private Driver? _owner;
        internal FloorReadPass(Driver? owner)
        {
            _owner = owner;
            if (owner == null) return;
            // Nesting always invalidates the interrupted pass. Native writers and
            // camera recovery clear the same reads before a resumed caller runs.
            owner.InvalidateFloorReadPass(); owner.FloorReadDepth++;
        }
        public void Dispose()
        {
            if (_owner == null) return;
            _owner.InvalidateFloorReadPass(); _owner.FloorReadDepth--; _owner = null;
        }
    }
    // Discovery may use the exact catalog role without confusing it with settled
    // geometry. Native ownership/effects and every camera's mutable guard remain
    // the responsibility of the renderer owner which actually submits the group.
    internal static bool CanGroupRoomFloor(Mesh mesh) => RoomArchitectureOn && (_roomRole?.Invoke(mesh) ?? 0) == 1;
    internal static bool TryGetSettledRoomFloor(Renderer renderer, out Mesh mesh)
    {
        mesh = null!;
        return !_failed && _driver != null && _driver.isActiveAndEnabled && VRSession.IsRunning
            && renderer != null && _driver.TryGetSettledRoomFloor(renderer, out mesh);
    }
    private static bool RoomArchitectureOn => _roomEnabled?.Invoke() == true;
    private static int FloorDetailPercent => Mathf.Clamp(_floorDetailPercent?.Invoke() ?? 100, 0, 100);
    private static bool AnySubstitutionOn => PerfConfig.TerrainSubstitutionOn || RoomArchitectureOn;
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
        PerfMonitor.RegisterDebug("Terrain.FloorSubstitutes");
        PerfMonitor.RegisterDebug("Terrain.StructuralSubstitutes");
        PerfMonitor.RegisterDebug("Terrain.FloorCameraCandidates");
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

    private struct ScopeState { internal bool Valid, Generated, Structural, Tile, Scenario, Prop; }
    private static bool NativeScope(MeshRenderer renderer, Dictionary<Transform, ScopeState> scopes,
        Dictionary<int, bool> scenes, List<Transform> ancestry, List<GameObject> roots, List<Component> components,
        HashSet<Transform>? propRoots, bool architecture = false, bool floor = false)
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
                state.Tile |= (role & ComponentRole.Tile) != 0;
                state.Scenario |= (role & ComponentRole.Scenario) != 0;
            }
            components.Clear();
            state.Valid &= !blocked;
            state.Generated |= nodeName == "Generated Content";
            state.Prop |= propRoots?.Contains(node) == true;
            scopes[node] = state;
        }
        ancestry.Clear();
        if (!state.Valid || !state.Generated
            || (floor ? !state.Tile : !state.Structural && !(architecture && state.Tile))) return false;
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
        private readonly List<Renderer> _floorNotifications = new();
        private readonly Dictionary<Transform, ScopeState> _scopeThisInvocation = new();
        private readonly Dictionary<int, bool> _sceneThisInvocation = new();
        private readonly List<Transform> _ancestry = new();
        private readonly List<GameObject> _sceneRoots = new();
        private readonly List<Component> _scopeComponents = new();
        private readonly Dictionary<Mesh, bool> _meshThisInvocation = new();
        private readonly Dictionary<Mesh, int> _roomRoleThisInvocation = new();
        private readonly Dictionary<Mesh, int> _floorRolesThisPass = new();
        internal int FloorReadDepth;
        internal void ClearFloorReads() => _floorRolesThisPass.Clear();
        internal void InvalidateFloorReadPass() => ClearValidation();
        internal int CurrentFloorRole(Mesh mesh)
        {
            if (FloorReadDepth <= 0) return _roomRole?.Invoke(mesh) ?? 0;
            if (!_floorRolesThisPass.TryGetValue(mesh, out int role))
            { role = _roomRole?.Invoke(mesh) ?? 0; _floorRolesThisPass[mesh] = role; }
            return role;
        }
        private Camera? _leaseCamera;
        private bool _active;
        private bool _roomWasEnabled;
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
        private bool Enabled => AnySubstitutionOn && VRSession.IsRunning && (PerfConfig.CheapWallShadingOn
            || PerfConfig.TerrainDetailPercent < 100 || PerfConfig.DistantTerrainDetailPercent < 100
            || RoomArchitectureOn && (FloorDetailPercent < 100 || _floorPreparation?.Invoke() == true));
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
                if (!AnySubstitutionOn)
                {
                    if (_active || _surfaces.Count != 0 || _pending.Count != 0) RestoreAll();
                    _active = false;
                    return;
                }
                bool active = Enabled;
                bool room = RoomArchitectureOn;
                if (active && (!_active || room != _roomWasEnabled)) Seed();
                _roomWasEnabled = room;
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
                    if (renderer == null || filter == null || filter.sharedMesh == null) continue;
                    int id = renderer.GetInstanceID();
                    bool first = _discovered.Add(id);
                    // Reject unsupported identities before decoding any bank member.
                    // Preparation can be expensive during loading, but it should prepare
                    // only sources which can actually save work on the settled board.
                    int role = 0;
                    if (room && !_roomRoleThisInvocation.TryGetValue(filter.sharedMesh, out role))
                    { role = _roomRole?.Invoke(filter.sharedMesh) ?? 0; _roomRoleThisInvocation[filter.sharedMesh] = role; }
                    bool architecture = role != 0;
                    bool floor = role == 1;
                    int refusal = !architecture && FloorIdentity(filter.sharedMesh) ? 1
                        : !architecture && !StructuralIdentity(filter.sharedMesh) ? 2
                        : !CurrentScope(renderer, architecture, floor) ? 0
                        : !EligibleMesh(filter.sharedMesh) ? 3 : -1;
                    if (refusal >= 0) { if (first) _refusals[refusal]++; continue; }
                    if (!_surfaces.ContainsKey(id)) { _surfaces.Add(id, new Surface(renderer, filter, transform, architecture, floor, role == 3)); _priorityDirty = true; }
                    else if (architecture) _surfaces[id].RoomArchitecture = true;
                }
                Camera? camera = Rig.VRRigDriver.HeadCamera;
                // Frame636's settled three-room capture spends about 1.7ms per frame
                // in this Update. Tracked poses and the detail settings are identical
                // throughout this synchronous loop; capture them once instead of
                // crossing Unity's transform boundary for each prepared wall. Renderer
                // bounds and mesh ownership remain current per source, and the next
                // Update always captures new poses/settings (including tracking loss).
                DetailState detail = active && camera != null ? new DetailState(camera) : default;
                foreach (KeyValuePair<int, Surface> item in _surfaces)
                {
                    Surface surface = item.Value;
                    if (!surface.Validate(_meshThisInvocation))
                    {
                        if (surface.HadFloorEndpoint && surface.Renderer != null) _floorNotifications.Add(surface.Renderer);
                        surface.Dispose(); _dead.Add(item.Key); continue;
                    }
                    int percent = active && camera != null ? DetailFor(surface, detail) : 100;
                    surface.StepGeometry(percent, delta);
                    if (surface.TakeFloorEndpointChange()) _floorNotifications.Add(surface.Renderer);
                }
                foreach (int id in _dead) { _surfaces.Remove(id); _priorityDirty = true; }
                _dead.Clear();
                // Native-owner callbacks may release/reseed/dispose presentation.
                // No callback runs inside the surface dictionary enumeration.
                for (int notice = 0; notice < _floorNotifications.Count; notice++)
                    if (_floorNotifications[notice] != null) _floorMeshReady?.Invoke(_floorNotifications[notice]);
                _floorNotifications.Clear();
                if (!active) { _pending.Clear(); _queued.Clear(); }
                int settings = (PerfConfig.CheapWallShadingOn ? 1 : 0) + PerfConfig.TerrainDetailPercent * 2
                    + PerfConfig.DistantTerrainDetailPercent * 202 + FloorDetailPercent * 20402
                    + (RoomArchitectureOn ? 2060602 : 0);
                if (_pending.Count == 0 && (_reportedSurfaces != _surfaces.Count || _reportedSettings != settings
                    || _reportedDiscovery != _discovered.Count)
                    && Time.unscaledTime >= _nextReport)
                {
                    _reportedSurfaces = _surfaces.Count; _reportedSettings = settings; _nextReport = Time.unscaledTime + 2f;
                    _reportedDiscovery = _discovered.Count;
                    VRLog.Note("Perf", "Scenario terrain budget: surfaces=" + _surfaces.Count + ", cheap="
                        + PerfConfig.CheapWallShadingOn + ", detail=" + PerfConfig.TerrainDetailPercent
                        + "%, distantDetail=" + PerfConfig.DistantTerrainDetailPercent
                        + "%, roomArchitecture=" + RoomArchitectureOn + ", floorDetail=" + FloorDetailPercent + "%.");
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
        private bool CurrentScope(MeshRenderer renderer, bool architecture = false, bool floor = false)
        {
            bool shared = PerfConfig.SharedEnvironmentMaterialReadsOn;
            if (shared) ReadCurrentPropRoots();
            return NativeScope(renderer, _scopeThisInvocation, _sceneThisInvocation,
                _ancestry, _sceneRoots, _scopeComponents, shared ? _propRoots : null, architecture, floor);
        }
        private bool EligibleMesh(Mesh mesh)
        {
            if (!_meshThisInvocation.TryGetValue(mesh, out bool eligible))
            { eligible = _eligibleMesh?.Invoke(mesh) == true; _meshThisInvocation[mesh] = eligible; }
            return eligible;
        }
        private void ClearValidation()
        {
            _scopeThisInvocation.Clear(); _sceneThisInvocation.Clear(); _ancestry.Clear(); _sceneRoots.Clear();
            _scopeComponents.Clear(); _meshThisInvocation.Clear();
            _roomRoleThisInvocation.Clear();
            ClearFloorReads();
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
            internal readonly int Floor;
            internal readonly HandProximity Left, Right;
            internal DetailState(Camera camera)
            {
                Transform head = camera.transform;
                Position = head.position;
                Scale = Mathf.Max(Mathf.Abs(head.lossyScale.x), .0001f);
                Left = new HandProximity(VRHands.Left); Right = new HandProximity(VRHands.Right);
                Near = PerfConfig.TerrainDetailPercent; Distant = PerfConfig.DistantTerrainDetailPercent;
                Distance = PerfConfig.TerrainDistanceMeters;
                Floor = FloorDetailPercent;
            }
        }
        private static int DetailFor(Surface surface, DetailState detail)
        {
            // Floor tiers preserve native heights, 3D bounds and open boundaries in
            // the audited asset bank. A global tier needs no per-tile proximity
            // or bounds query; floors are never part of the wall-fade system.
            if (surface.FloorBudget) return RoomArchitectureOn ? detail.Floor : 100;
            if (!surface.GeometryEnabled) return 100;
            Bounds bounds = surface.Renderer.bounds;
            Vector3 nearest = bounds.ClosestPoint(detail.Position);
            float metres = Vector3.Distance(detail.Position, nearest) / detail.Scale;
            // Leaning into the scenery, or touching it with either tracked side, restores
            // exact original geometry. Interactive and held objects are excluded separately.
            if (metres < .18f || NearHand(detail.Left, bounds) || NearHand(detail.Right, bounds)) return 100;
            int near = detail.Near;
            // Small hysteresis keeps a parked threshold from repeatedly morphing the same mesh.
            float edge = detail.Distance + (surface.Distant ? -.04f : .04f);
            surface.Distant = metres > edge;
            return surface.Distant ? Mathf.Min(near, detail.Distant) : near;
        }
        private static bool NearHand(HandProximity hand, Bounds bounds) => hand.Tracked
            && Vector3.Distance(hand.Position, bounds.ClosestPoint(hand.Position)) / hand.Scale < .12f;

        internal bool OwnsRenderSubstitute(Renderer renderer) => AnySubstitutionOn
            && _surfaces.TryGetValue(renderer.GetInstanceID(), out Surface surface)
            && surface.WantsSubstitute(_active) && !FloorGroupOwns(surface);
        private static bool FloorGroupOwns(Surface surface) => surface.Floor
            && _floorGrouping?.Invoke(surface.Renderer) == true && surface.TryGetSettledRoomFloor(out _);
        internal bool TryGetSettledRoomFloor(Renderer renderer, out Mesh mesh)
        {
            mesh = null!;
            // Group discovery/camera scopes share only this invocation's genuine
            // ancestry and held-root reads. An isolated lookup gets an independent
            // pass, so callers cannot accidentally retain a mutable scope verdict.
            bool independent = FloorReadDepth <= 0;
            if (independent) ClearValidation();
            try
            {
                return _surfaces.TryGetValue(renderer.GetInstanceID(), out Surface surface)
                    && surface.TryGetSettledRoomFloor(out mesh)
                    && CurrentScope(surface.Renderer, true, true);
            }
            finally { if (independent) ClearValidation(); }
        }
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
            int originals = 0, submitted = 0, cheap = 0, floors = 0, structures = 0;
            foreach (Surface surface in _leases)
            {
                // A later native MPB/material/visibility writer can revoke a pre-cull
                // admission. Count only leases still owned at this paired render end.
                // Frustum/occlusion and GPU execution remain outside this measurement.
                if (!surface.IsMasked) continue;
                originals += surface.OriginalTriangles; submitted += surface.DrawTriangles;
                if (surface.CheapLease) cheap++;
                if (surface.FloorBudget) floors++; else structures++;
            }
            PerfMonitor.Count("Terrain.OriginalTriangles", originals);
            PerfMonitor.Count("Terrain.SubmittedTriangles", submitted);
            PerfMonitor.Count("Terrain.CheapSurfaces", cheap);
            if (PerfMonitor.StepsActive && VRLog.Level >= VRLogLevel.Debug)
            {
                PerfMonitor.Count("Terrain.FloorSubstitutes", floors);
                PerfMonitor.Count("Terrain.StructuralSubstitutes", structures);
            }
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
                    || !isActiveAndEnabled || !AnySubstitutionOn
                    || (_nativeCameraConsumers?.Invoke(camera) ?? camera.commandBufferCount > 0)) return;
                using (PerfMonitor.Scope("ScenarioTerrain.PreCull"))
                using (_worldReadPass?.Invoke())
                using (BeginFloorReadPass())
                {
                    _leaseCamera = camera;
                    PreparePriority();
                    bool shared = PerfConfig.SharedEnvironmentMaterialReadsOn;
                    PrepareFrustum(camera, shared);
                    Matrix4x4 ownerPose = shared ? transform.localToWorldMatrix : default;
                    Vector3 ownerScale = shared ? transform.lossyScale : default;
                    int limit = PerfConfig.TerrainCameraSourceLimit;
                    int floorLimit = Mathf.Max(0, _floorCameraLimit?.Invoke() ?? limit);
                    bool substitute = Enabled;
                    bool world = _worldEnabled?.Invoke() == true;
                    bool cheap = PerfConfig.CheapWallShadingOn;
                    int candidates = 0, floorCandidates = 0, budgetFallback = 0, budgetDeferred = 0, frustumFallback = 0;
                    for (int index = 0; index < _priority.Count; index++)
                    {
                        // Build638 still reads native visibility for ~455 sources/frame
                        // AFTER the 64-candidate cap has exhausted its admission budget.
                        // None can acquire a lease in this invocation. RecoverLeases above
                        // already released every previous proxy, so leave the untouched
                        // remainder native without crossing Unity for each rejected source.
                        // This is a prepared-source count, NOT an examined active fallback.
                        if (shared && limit > 0 && candidates >= limit)
                        {
                            budgetDeferred += Mathf.Max(0, _firstFloor - index);
                            index = Mathf.Max(index, _firstFloor);
                            if (index >= _priority.Count) break;
                        }
                        if (shared && floorLimit > 0 && floorCandidates >= floorLimit && index >= _firstFloor)
                        { budgetDeferred += _priority.Count - index; break; }
                        Surface surface = _priority[index];
                        // Prepared surfaces include unopened rooms. Reject their native
                        // disabled/inactive renderers before bank/material/proxy work.
                        // No admission verdict survives this camera invocation.
                        if (surface.Renderer == null || !surface.Renderer.enabled
                            || !surface.Renderer.gameObject.activeInHierarchy || surface.Renderer.forceRenderingOff
                            || !surface.WantsSubstitute(substitute) || FloorGroupOwns(surface)) continue;
                        // Bound full guard/copy work, not merely successful masks. Rejected
                        // candidates also cost CPU; budget fallback keeps original output.
                        if (!surface.FloorBudget && limit > 0 && candidates >= limit) { budgetFallback++; continue; }
                        if (surface.FloorBudget && floorLimit > 0 && floorCandidates >= floorLimit) { budgetFallback++; continue; }
                        if (OutsideFrustum(surface.Renderer)) { frustumFallback++; continue; }
                        if (surface.FloorBudget) floorCandidates++; else candidates++;
                        if (!surface.Validate(_meshThisInvocation)
                            || surface.Renderer.isPartOfStaticBatch || surface.Renderer.additionalVertexStreams != null
                            || !CurrentScope(surface.Renderer, surface.RoomArchitecture, surface.Floor)) continue;
                        surface.Renderer.GetSharedMaterials(_materialScratch);
                        if (_materialScratch.Count != surface.Original.subMeshCount) continue;
                        bool supported = true;
                        if (!world)
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
                        surface.WorldLease = world;
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
                        PerfMonitor.Count("Terrain.CameraCandidates", candidates + floorCandidates);
                        PerfMonitor.Count("Terrain.FloorCameraCandidates", floorCandidates);
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
            _floorNotifications.Clear();
            foreach (Material material in _cheap.Values) if (material != null) UnityEngine.Object.Destroy(material);
            _cheap.Clear();
            _reportedSurfaces = -1;
            _reportedDiscovery = -1; _discovered.Clear(); Array.Clear(_refusals, 0, _refusals.Length);
        }
    }
}
