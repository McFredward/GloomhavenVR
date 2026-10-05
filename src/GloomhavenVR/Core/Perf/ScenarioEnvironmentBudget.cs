using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// The Frame605 report still has about 1,600--1,900 visible render candidates after decorative
/// masking. Optional small static render chunks, cheaper opaque environment shading and native
/// ambience budgets target that remaining work. Native GameObjects, colliders, picking, visibility
/// controllers and continuations are retained. A batch is a render substitute only: if any original
/// changes visibility, transform, material or per-renderer effects, restore it before camera culling.
/// Only positively identified floor cores, audited masonry meshes and ambient prefab families are admitted; no
/// actor, held prop, water, foliage, UI or active wall-dissolve surface enters.
/// Fresh Frame defaults differ; these same reversible settings are available on PC.
/// </summary>
internal static class ScenarioEnvironmentBudget
{
    private const string Scope = "Perf";
    private const string SimpleShader = "GloomhavenVR/ScenarioSimpleEnvironment";
    private const int NodesPerFrame = 128;
    private const int MaxBatchVertices = 48000;
    private const int MaxBatchMembers = 24;
    private static Driver? _driver;
    private static LeaseRecovery? _recovery;
    private static bool _failed;
    private static Func<bool>? _structuralEnabled;

    internal static void ConfigureStructuralBatching(Func<bool> enabled) => _structuralEnabled = enabled;
    private static bool StructuralEnabled => _structuralEnabled?.Invoke() == true;

    // Actual queued discovery/substitute construction only. Ongoing cull leases and
    // material revalidation are steady presentation, not a reason to keep a spinner up.
    internal static bool IsPreparingPresentation => !_failed && VRSession.IsRunning
        && _driver != null && _driver.IsPreparingPresentation;

    // A renderer write can occur inside a nested render callback. Drop its substitute
    // synchronously, before native/wall effects can encounter an old chunk or mask.
    internal static void BeforeNativeRendererWrite(Renderer renderer)
    {
        if (_failed || renderer == null) return;
        try { _driver?.BeforeNativeRendererWrite(renderer); }
        catch (Exception error) { StopAfterFailure(error); }
    }
    internal static void BeforeNativeContentChange()
    {
        try { _driver?.RecoverRenderLeases(); }
        catch (Exception error) { StopAfterFailure(error); }
    }

    internal static void Install(GameObject host)
    {
        if (_driver != null) return;
        _driver = host.AddComponent<Driver>();
        _recovery = host.AddComponent<LeaseRecovery>();
        try
        {
            VRSession.Harmony?.PatchAll(typeof(ProceduralBase_Placed_EnvironmentBudgetPatch));
            VRSession.Harmony?.PatchAll(typeof(ProceduralMapTile_Show_EnvironmentBudgetPatch));
            VRSession.Harmony?.PatchAll(typeof(MaterialLoaderData_Ready_EnvironmentBudgetPatch));
            VRSession.Harmony?.PatchAll(typeof(SceneController_Loaded_EnvironmentBudgetPatch));
        }
        catch (Exception error) { StopAfterFailure(error); }
    }

    internal static void Shutdown()
    {
        if (_driver == null) return;
        _driver.RestoreAll();
        UnityEngine.Object.Destroy(_driver);
        _driver = null;
        if (_recovery != null) UnityEngine.Object.Destroy(_recovery);
        _recovery = null;
        _failed = false;
    }

    internal static void Placed(GameObject root) { if (!_failed) _driver?.QueueRoot(root); }
    internal static void MaterialReady(Renderer renderer)
    {
        if (_failed) return;
        try { _driver?.MaterialReady(renderer); }
        catch (Exception error) { StopAfterFailure(error); }
    }
    internal static void BeforeLoadingComplete() { if (!_failed) _driver?.FinishLoading(); }
    // This is the fail-open lifecycle mechanism, not a removable diagnostic.
    internal static void StopAfterFailure(Exception error)
    {
        if (_failed) return;
        _failed = true;
        VRLog.Note(Scope, "Scenario environment budget: preparation failed; native rendering and "
            + "continuation retained (" + error.GetType().Name + ": " + error.Message + ").");
        // Never let an optimization failure escape into native quest/load continuation.
        // Stop optional work for this VR session after restoring the original surfaces.
        if (_driver != null)
        {
            _driver.enabled = false;
            try { _driver.RestoreAll(); }
            catch (Exception cleanup) { VRLog.Debug(Scope, "Environment budget cleanup: " + cleanup.Message); }
        }
    }

    private static string AuthoredName(string name)
    {
        while (name.EndsWith("(Clone)", StringComparison.Ordinal)
            || name.EndsWith("(Instance)", StringComparison.Ordinal))
            name = name.Substring(0, name.LastIndexOf((char)0x28)).TrimEnd();
        return name;
    }

    private static bool FloorIdentity(string name)
    {
        name = AuthoredName(name);
        return name.IndexOf("_Floor_Base", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Basic", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_FloorTiles", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Stone_Floor_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool StructuralIdentity(Mesh mesh)
    {
        // Exact authored Mesh identities, audited in pcg_crypt/ancientcaverns/cave.
        // Ancestor labels, generic "Wall" meshes and decorative roots never qualify.
        return AuthoredName(mesh.name) is "EN_CR_Pillar_Thin" or "EN_CR_Pillar_Large"
            or "EN_CR_Pillar_Large_02" or "EN_CR_Wall_Top_x2" or "EN_CR_Wall_Top_x4"
            or "EN_CR_Wall_Basic_Tall" or "EN_CR_Wall_TrimBasic_01"
            or "CR_RU_UnderWall_01_Pillar" or "CR_RU_UnderWall_01_Slabs"
            or "CR_RU_UnderWall_01_Wall" or "CR_FR_Pillar_Stoun_02"
            or "CV_Pillar_Generic_01" or "CV_Pillar_Generic_02"
            or "CV_Wall_Generic_01" or "CV_Wall_Generic_02" or "CV_Wall_Generic_03"
            or "CV_Wall_Generic_04" or "CV_Wall_Generic_05"
            or "CV_Wall_Generic_Thin_01" or "CV_Wall_Generic_Thin_Narrow_01"
            or "CV_Wall_Generic_Thin_Narrow_02";
    }

    private static bool Gate(Material material, string name) =>
        material.HasProperty(name) && material.GetFloat(name) > 0f;

    private static bool NativeWallFadeEnabled(Material material) =>
        (material.HasProperty("_WallFade_On") && material.GetFloat("_WallFade_On") != 0f)
        || material.IsKeywordEnabled("_WALLFADE_ON_ON")
        || Gate(material, "_ToggleWallfade") || Gate(material, "ToggleWallFade")
        || (material.HasProperty("_ToggleWallFadeLocal")
            && Mathf.Abs(material.GetFloat("_ToggleWallFadeLocal")) > 0.5f);

    private static bool CompatibleMaterial(Material material, bool floor)
    {
        if (!floor || material == null || material.shader == null) return false;
        string shader = material.shader.name;
        if (shader != "Amp_Basic_N_MRAO" && shader != "Amp_Low/Amp_Basic_N_MRAO_Low"
            && shader != SimpleShader) return false;
        if (material.renderQueue > 2500 || Gate(material, "_AddVertexAnim")
            || Gate(material, "_UseEmissiveMap") || Gate(material, "_Diffuse_Emissive_On")) return false;
        // Frame615 logs identify CV_Floor_Basic_M (VR simple environment) as
        // "toggle-native" and include the cheap shader in Wall25's native fade set.
        // CopyPropertiesFromMaterial also preserves saved properties absent from the
        // new shader; HasProperty/GetFloat can therefore advertise a native dissolve
        // which this shader does not render. An authored floor label does not prove
        // that its material has no native wall channel. Preserve every live channel,
        // even on floors, rather than replacing animation with a cutoff/enable pop.
        if (NativeWallFadeEnabled(material)) return false;
        // Immediate native floor identity AND floor-plane geometry establish the native
        // never-fade veto. Broad ancestor names cannot promote mounted scenery into it.
        return true;
    }

    private static ProceduralMapTile? TileScope(Transform leaf, out bool floor)
    {
        floor = false;
        bool generated = false;
        ProceduralMapTile? tile = null;
        for (Transform? node = leaf; node != null; node = node.parent)
        {
            if (node.GetComponent<Canvas>() != null || node.GetComponent<ActorBehaviour>() != null
                || node.GetComponent<ProceduralProp>() != null || node.GetComponent<ProceduralDoorway>() != null
                || node.GetComponent<UnityGameEditorDoorProp>() != null || node.GetComponent<CInteractable>() != null
                || node.GetComponent<Animator>() != null || node.GetComponent<Rigidbody>() != null
                || node.GetComponent<SkinnedMeshRenderer>() != null || node.name == "Preview"
                || node.name.StartsWith("GloomhavenVR", StringComparison.Ordinal)) return null;
            if (node.name == "Generated Content") generated = true;
            tile = node.GetComponent<ProceduralMapTile>();
            if (tile != null) break;
        }
        if (tile == null || !generated) return null;
        for (Transform? parent = tile.transform; parent != null; parent = parent.parent)
            if (parent.GetComponent<ProceduralScenario>() != null) return tile;
        Scene scene = tile.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.GetComponent<ProceduralScenario>() != null) return tile;
        return null;
    }

    private static bool ProvenFloorCore(MeshRenderer renderer, Mesh mesh, ProceduralMapTile tile, Material[] materials)
    {
        bool identity = FloorIdentity(mesh.name) || FloorIdentity(renderer.name);
        foreach (Material material in materials)
            if (material != null) identity |= FloorIdentity(material.name);
        if (!identity) return false;
        Bounds bounds = mesh.bounds;
        Matrix4x4 toTile = tile.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = toTile.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
            min = Vector3.Min(min, point); max = Vector3.Max(max, point);
        }
        return WallFloorTile.Judge(new WallFloorTile.Plate(min.y, max.y, max.x - min.x, max.z - min.z), 0f)
            == WallFloorTile.Verdict.FloorTile;
    }

    private static bool AmbientIdentity(Transform leaf, Transform tile)
    {
        // These exact native prefab families are environment-owned and independent of
        // combat/condition particle lifetimes. Looping alone never establishes ambience.
        for (Transform? node = leaf; node != null && node != tile; node = node.parent)
        {
            string name = AuthoredName(node.name);
            if (name.StartsWith("p_Moths_", StringComparison.Ordinal)
                || name.StartsWith("Candle_Fire_FX_", StringComparison.Ordinal)
                || name == "p_fire_torch" || name == "p_fire_torch_blue"
                || name == "p_fireflies" || name == "p_Fireflies") return true;
            if (name.StartsWith("P_", StringComparison.Ordinal)
                || name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("condition", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        }
        return false;
    }

    private sealed class Surface
    {
        internal MeshRenderer Renderer = null!;
        internal int Id;
        internal MeshFilter Filter = null!;
        internal ProceduralMapTile Tile = null!;
        internal bool Floor;
        internal bool Structural;
        internal Mesh Mesh = null!;
        internal Material[] Original = Array.Empty<Material>();
        internal Material[]? Applied;
        internal bool IsApplied()
        {
            if (Renderer == null || Applied == null) return false;
            Material[] current = Renderer.sharedMaterials;
            if (current.Length != Applied.Length) return false;
            for (int i = 0; i < current.Length; i++) if (current[i] != Applied[i]) return false;
            return true;
        }
        internal void RestoreMaterial()
        {
            if (IsApplied()) Renderer.sharedMaterials = Original;
            Applied = null;
        }
    }

    private sealed class Ambient
    {
        internal ParticleSystem System = null!;
        internal ParticleSystemRenderer Renderer = null!;
        internal uint Hash;
        private bool _paused, _masked;
        internal void Apply(bool hidden)
        {
            if (System == null || Renderer == null) return;
            if (hidden)
            {
                if (System.isPlaying) { System.Pause(false); _paused = true; }
            }
            else
            {
                Unmask();
                if (_paused && System.isPaused) System.Play(false);
                _paused = false;
            }
        }
        internal void Mask(bool hidden)
        {
            if (!hidden || Renderer == null || Renderer.forceRenderingOff) return;
            Renderer.forceRenderingOff = true; _masked = true;
        }
        internal void Unmask()
        {
            if (_masked && Renderer != null && Renderer.forceRenderingOff) Renderer.forceRenderingOff = false;
            _masked = false;
        }
    }

    private sealed class Batch
    {
        internal GameObject Object = null!;
        internal MeshRenderer Renderer = null!;
        internal Mesh Mesh = null!;
        internal Material Material = null!;
        internal readonly List<Surface> Sources = new();
        internal readonly List<Matrix4x4> Matrices = new();
        private bool _owned;
        private readonly List<Material> _materialScratch = new(1);

        internal void Validate()
        {
            bool valid = Object != null && Material != null;
            Matrix4x4 inverse = Object != null ? Object.transform.worldToLocalMatrix : Matrix4x4.identity;
            for (int i = 0; valid && i < Sources.Count; i++)
            {
                Surface source = Sources[i];
                MeshRenderer r = source.Renderer;
                _materialScratch.Clear();
                if (r != null) r.GetSharedMaterials(_materialScratch);
                valid = r != null && r.enabled && r.gameObject.activeInHierarchy && !r.HasPropertyBlock()
                    && r.forceRenderingOff == _owned && source.Filter != null
                    && source.Filter.sharedMesh == source.Mesh && _materialScratch.Count == 1
                    && _materialScratch[0] == Material && Renderer != null && SameRenderFlags(r, Renderer)
                    && Object != null && r.gameObject.layer == Object.layer
                    && inverse * r.transform.localToWorldMatrix == Matrices[i];
            }
            if (valid == _owned) return;
            // Restore original visuals in the same render frame on any visibility/effect or
            // transform change. Never keep a combined room visible after a native hide.
            if (Renderer != null) Renderer.enabled = valid;
            foreach (Surface source in Sources)
                if (source.Renderer != null && source.Renderer.forceRenderingOff == _owned)
                    source.Renderer.forceRenderingOff = valid;
            _owned = valid;
        }

        internal void Unmask()
        {
            if (Renderer != null) Renderer.enabled = false;
            if (_owned)
                foreach (Surface source in Sources)
                    if (source.Renderer != null && source.Renderer.forceRenderingOff)
                        source.Renderer.forceRenderingOff = false;
            _owned = false;
        }

        internal void Dispose()
        {
            Unmask();
            if (Object != null) UnityEngine.Object.Destroy(Object);
            if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
        }
    }

    private static bool SameRenderFlags(MeshRenderer a, MeshRenderer b) =>
        a.shadowCastingMode == b.shadowCastingMode && a.receiveShadows == b.receiveShadows
        && a.lightProbeUsage == b.lightProbeUsage && a.reflectionProbeUsage == b.reflectionProbeUsage
        && a.probeAnchor == b.probeAnchor && a.motionVectorGenerationMode == b.motionVectorGenerationMode
        && a.allowOcclusionWhenDynamic == b.allowOcclusionWhenDynamic
        && a.renderingLayerMask == b.renderingLayerMask;

    private readonly struct BatchKey : IEquatable<BatchKey>
    {
        private readonly int _tile, _material, _layer, _x, _z, _y, _flags;
        internal BatchKey(Surface surface, Material material)
        {
            _tile = surface.Tile.GetInstanceID(); _material = material.GetInstanceID();
            _layer = surface.Renderer.gameObject.layer;
            MeshRenderer r = surface.Renderer;
            unchecked { _flags = (((((int)r.shadowCastingMode * 397 ^ (r.receiveShadows ? 1 : 0))
                * 397 ^ (int)r.lightProbeUsage) * 397 ^ (int)r.reflectionProbeUsage)
                * 397 ^ (r.probeAnchor != null ? r.probeAnchor.GetInstanceID() : 0))
                * 397 ^ (int)r.motionVectorGenerationMode;
                _flags = (_flags * 397 ^ (r.allowOcclusionWhenDynamic ? 1 : 0)) * 397 ^ (int)r.renderingLayerMask; }
            Vector3 p = surface.Tile.transform.InverseTransformPoint(surface.Renderer.bounds.center);
            _x = Mathf.FloorToInt(p.x / 4f); _z = Mathf.FloorToInt(p.z / 4f);
            _y = surface.Structural ? Mathf.FloorToInt(p.y / 4f) : 0;
        }
        public bool Equals(BatchKey other) => _tile == other._tile && _material == other._material
            && _layer == other._layer && _x == other._x && _z == other._z && _y == other._y && _flags == other._flags;
        public override bool Equals(object? other) => other is BatchKey key && Equals(key);
        public override int GetHashCode()
        { unchecked { return (((((_tile * 397 ^ _material) * 397 ^ _layer) * 397 ^ _x) * 397 ^ _z) * 397 ^ _y) * 397 ^ _flags; } }
    }

    // Recover an interrupted camera render before native Update can instantiate an
    // original source. Normal balanced rendering costs only one integer comparison.
    [DefaultExecutionOrder(-32000)]
    private sealed class LeaseRecovery : MonoBehaviour
    {
        private void Update() => _driver?.RecoverRenderLeases();
    }

    [DefaultExecutionOrder(31000)]
    private sealed class Driver : MonoBehaviour
    {
        private readonly Queue<Transform> _pending = new();
        private readonly HashSet<int> _queued = new();
        private readonly Dictionary<int, Surface> _surfaces = new();
        private readonly Dictionary<int, Ambient> _ambient = new();
        private readonly Dictionary<Material, Material> _materials = new();
        private readonly Dictionary<Material, Material> _originalByVariant = new();
        private readonly Dictionary<Material, bool> _preCullMaterialVerdicts = new();
        private readonly List<ProceduralMapTile> _tiles = new();
        private readonly List<Batch> _batches = new();
        private readonly Dictionary<int, Batch> _batchBySource = new();
        private readonly Queue<List<Surface>> _parts = new();
        private readonly List<int> _dead = new();
        private bool _batchOn, _structuralOn, _simpleOn, _active, _buildPending;
        private int _effects = 100, _unreadable, _renderDepth;
        private Shader? _shader;

        internal bool IsPreparingPresentation => _pending.Count > 0 || _parts.Count > 0;

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Camera.onPreCull += HandlePreCull;
            Camera.onPostRender += HandlePostRender;
        }
        private void OnDisable() => RecoverRenderLeases();
        internal void RecoverRenderLeases()
        {
            if (_renderDepth == 0) return;
            _renderDepth = 0;
            foreach (Batch batch in _batches) batch.Unmask();
            foreach (Ambient ambient in _ambient.Values) ambient.Unmask();
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Camera.onPreCull -= HandlePreCull;
            Camera.onPostRender -= HandlePostRender;
            RestoreAll();
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { if (VRSession.IsRunning) Seed(); }
        private void OnSceneUnloaded(Scene scene) { RestoreAll(); }
        private void Seed()
        {
            SceneRegistry.MapTiles.Collect(_tiles);
            foreach (ProceduralMapTile tile in _tiles) if (tile != null) QueueRoot(tile.gameObject);
            _tiles.Clear();
        }

        internal void QueueRoot(GameObject root)
        {
            if (!VRSession.IsRunning || root == null || !(PerfConfig.StaticScenarioBatchesOn
                || StructuralEnabled || PerfConfig.SimpleEnvironmentShadingOn || PerfConfig.EnvironmentEffectsDensityPercent < 100)) return;
            if (_queued.Add(root.GetInstanceID())) _pending.Enqueue(root.transform);
        }
        internal void MaterialReady(Renderer renderer)
        {
            if (renderer == null || !VRSession.IsRunning || !(PerfConfig.StaticScenarioBatchesOn
                || StructuralEnabled || PerfConfig.SimpleEnvironmentShadingOn || PerfConfig.EnvironmentEffectsDensityPercent < 100)
                || TileScope(renderer.transform, out _) == null) return;
            Settings();
            int id = renderer.GetInstanceID();
            InvalidateBatch(id);
            if (_surfaces.TryGetValue(id, out Surface old))
            {
                // Native material loaders can finish textures in-place. Restore only our
                // array before rebuilding its variant, never a foreign replacement array.
                old.RestoreMaterial();
                foreach (Material original in old.Original)
                    if (original != null && _materials.TryGetValue(original, out Material variant))
                    { variant.CopyPropertiesFromMaterial(original); variant.shaderKeywords = Array.Empty<string>(); }
                _surfaces.Remove(id);
            }
            Adopt(renderer.transform);
        }

        private void Update()
        {
            using var scope = PerfMonitor.Scope("EnvironmentBudget.Update");
            try
            {
                Settings();
                if (!_active) return;
                SceneController controller = SceneController.Instance;
                bool loading = controller != null && (controller.IsLoading || controller.ScenarioIsLoading);
                Walk(loading ? 4096 : NodesPerFrame);
                if (_buildPending && _pending.Count == 0) PrepareBatches();
                DrainBatches(loading ? int.MaxValue : 2);
            }
            catch (Exception error) { StopAfterFailure(error); }
        }

        private void Settings()
        {
            bool batch = VRSession.IsRunning && PerfConfig.StaticScenarioBatchesOn;
            bool simple = VRSession.IsRunning && PerfConfig.SimpleEnvironmentShadingOn;
            bool structural = VRSession.IsRunning && StructuralEnabled && simple;
            int effects = VRSession.IsRunning ? PerfConfig.EnvironmentEffectsDensityPercent : 100;
            bool active = batch || structural || simple || effects < 100;
            if (batch == _batchOn && structural == _structuralOn && simple == _simpleOn && effects == _effects && active == _active) return;
            ReleaseBatches();
            foreach (Surface surface in _surfaces.Values) surface.RestoreMaterial();
            RestoreClonedMaterials();
            foreach (Material material in _materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
            _materials.Clear(); _originalByVariant.Clear();
            _batchOn = batch; _structuralOn = structural; _simpleOn = simple; _effects = effects; _active = active;
            foreach (Surface surface in _surfaces.Values) ApplyMaterial(surface);
            foreach (Ambient ambient in _ambient.Values) ambient.Apply(Hide(ambient.Hash));
            if (!active) { RestoreAll(); return; }
            Seed(); _buildPending = true;
        }

        internal void FinishLoading()
        {
            try
            {
                Settings();
                if (!_active) return;
                Seed();
                Walk(int.MaxValue);
                PrepareBatches();
                DrainBatches(int.MaxValue);
                Report();
            }
            catch (Exception error) { StopAfterFailure(error); }
        }

        private void Walk(int budget)
        {
            while (budget-- > 0 && _pending.Count > 0)
            {
                Transform node = _pending.Dequeue();
                if (node == null) continue;
                _queued.Remove(node.gameObject.GetInstanceID());
                if (node.name.StartsWith("GloomhavenVR", StringComparison.Ordinal)
                    || node.GetComponent<ActorBehaviour>() != null || node.GetComponent<ProceduralProp>() != null
                    || node.GetComponent<Canvas>() != null || node.GetComponent<Animator>() != null) continue;
                Adopt(node);
                for (int i = 0; i < node.childCount; i++)
                {
                    Transform child = node.GetChild(i);
                    if (_queued.Add(child.gameObject.GetInstanceID())) _pending.Enqueue(child);
                }
            }
        }

        private void Adopt(Transform node)
        {
            ProceduralMapTile? tile = TileScope(node, out bool floor);
            if (tile == null) return;
            MeshRenderer renderer = node.GetComponent<MeshRenderer>();
            MeshFilter filter = node.GetComponent<MeshFilter>();
            if (renderer != null && filter != null && filter.sharedMesh != null)
            {
                Material[] materials = renderer.sharedMaterials;
                bool cloned = false;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && _originalByVariant.TryGetValue(materials[i], out Material original))
                    { materials[i] = original; cloned = true; }
                floor = ProvenFloorCore(renderer, filter.sharedMesh, tile, materials);
                bool structural = !floor && StructuralIdentity(filter.sharedMesh) && !renderer.HasPropertyBlock();
                if (structural)
                    foreach (Material material in materials)
                        structural &= material != null && !Gate(material, "_WallFade_On")
                            && !Gate(material, "_ToggleWallfade") && !Gate(material, "ToggleWallFade")
                            && !Gate(material, "_ToggleWallFadeLocal")
                            && !Array.Exists(material.shaderKeywords, keyword => keyword.IndexOf("WALLFADE", StringComparison.OrdinalIgnoreCase) >= 0);
                // Apparance clones keep native material slots. Canonicalize only our known
                // variant references so a later Off restores the genuine original shader.
                if (cloned && !_surfaces.ContainsKey(renderer.GetInstanceID())) renderer.sharedMaterials = materials;
                if (_surfaces.TryGetValue(renderer.GetInstanceID(), out Surface old))
                {
                    if (old.Filter != null && old.Filter.sharedMesh == old.Mesh
                        && (old.IsApplied() || SameMaterials(materials, old.Original)))
                    {
                        if (_simpleOn && !old.IsApplied()) ApplyMaterial(old);
                        return;
                    }
                    InvalidateBatch(renderer.GetInstanceID());
                    old.Applied = null;
                    _surfaces.Remove(renderer.GetInstanceID());
                }
                bool compatible = materials.Length > 0;
                foreach (Material material in materials) compatible &= CompatibleMaterial(material, floor || structural);
                if (compatible)
                {
                    var surface = new Surface { Renderer = renderer, Id = renderer.GetInstanceID(), Filter = filter, Tile = tile,
                        Floor = floor, Structural = structural, Mesh = filter.sharedMesh, Original = materials };
                    _surfaces[renderer.GetInstanceID()] = surface;
                    ApplyMaterial(surface);
                    _buildPending = true;
                }
            }
            ParticleSystem system = node.GetComponent<ParticleSystem>();
            ParticleSystemRenderer particleRenderer = node.GetComponent<ParticleSystemRenderer>();
            if (system != null && particleRenderer != null && system.main.loop
                && AmbientIdentity(node, tile.transform) && !_ambient.ContainsKey(system.GetInstanceID()))
            {
                var ambient = new Ambient { System = system, Renderer = particleRenderer, Hash = HashPath(node, tile.transform) };
                _ambient.Add(system.GetInstanceID(), ambient);
                ambient.Apply(Hide(ambient.Hash));
            }
        }

        private static bool SameMaterials(Material[] a, Material[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static uint HashPath(Transform leaf, Transform stop)
        {
            uint hash = 2166136261;
            unchecked
            {
                for (Transform? node = leaf; node != null && node != stop; node = node.parent)
                    foreach (char c in node.name) { hash ^= c; hash *= 16777619; }
            }
            return hash;
        }
        private bool Hide(uint hash) => _effects <= 0 || (_effects < 100 && hash % 100 >= _effects);

        private void ApplyMaterial(Surface surface)
        {
            if (!_simpleOn || (surface.Structural && !_structuralOn) || surface.Renderer == null) return;
            if (surface.Renderer.HasPropertyBlock()) return;
            foreach (Material original in surface.Original)
                if (!CompatibleMaterial(original, surface.Floor || surface.Structural)) return;
            _shader ??= BundleShaders.Resolve(SimpleShader, Scope, "simpler scenario environment shader available", "original environment materials retained");
            if (_shader == null) return;
            var changed = new Material[surface.Original.Length];
            for (int i = 0; i < changed.Length; i++)
            {
                Material original = surface.Original[i];
                if (!_materials.TryGetValue(original, out Material variant))
                {
                    variant = new Material(_shader) { name = original.name + " (VR simple environment)",
                        renderQueue = original.renderQueue, enableInstancing = original.enableInstancing };
                    variant.CopyPropertiesFromMaterial(original);
                    // Only compatible opaque/cutout materials enter; never copy a native wall
                    // keyword onto our floor shader and pretend it implements wall dissolution.
                    variant.shaderKeywords = Array.Empty<string>();
                    _materials.Add(original, variant); _originalByVariant.Add(variant, original);
                }
                changed[i] = variant;
            }
            surface.Renderer.sharedMaterials = changed;
            surface.Applied = changed;
        }

        private void PrepareBatches()
        {
            _buildPending = false;
            _parts.Clear(); _unreadable = 0;
            _dead.Clear();
            foreach (var pair in _surfaces) if (pair.Value.Renderer == null) _dead.Add(pair.Key);
            foreach (int id in _dead) { InvalidateBatch(id); _surfaces.Remove(id); }
            _dead.Clear();
            foreach (var pair in _ambient) if (pair.Value.System == null) _dead.Add(pair.Key);
            foreach (int id in _dead) _ambient.Remove(id);
            _dead.Clear();
            if (!_batchOn && !_structuralOn) return;
            var groups = new Dictionary<BatchKey, List<Surface>>();
            foreach (Surface surface in _surfaces.Values)
            {
                MeshRenderer renderer = surface.Renderer;
                if (renderer == null || _batchBySource.ContainsKey(renderer.GetInstanceID())
                    || !(surface.Floor ? _batchOn : surface.Structural && _structuralOn && surface.IsApplied()) || surface.Tile == null || surface.Mesh == null
                    || renderer.transform.localToWorldMatrix.determinant <= 0f
                    || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff
                    || renderer.HasPropertyBlock() || surface.Filter.sharedMesh != surface.Mesh
                    || surface.Mesh.subMeshCount != 1 || renderer.sharedMaterials.Length != 1
                    || renderer.GetComponentInParent<LODGroup>(true) != null
                    || (renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 65534)) continue;
                if (!surface.Mesh.isReadable) { _unreadable++; continue; }
                Material material = renderer.sharedMaterial;
                if (material == null) continue;
                var key = new BatchKey(surface, material);
                if (!groups.TryGetValue(key, out List<Surface> members)) groups.Add(key, members = new List<Surface>());
                members.Add(surface);
            }
            foreach (List<Surface> members in groups.Values)
            {
                var portion = new List<Surface>(MaxBatchMembers);
                int vertices = 0;
                foreach (Surface surface in members)
                {
                    int next = surface.Mesh.vertexCount;
                    if (next > MaxBatchVertices) continue;
                    if (portion.Count >= MaxBatchMembers || vertices + next > MaxBatchVertices)
                    { QueuePortion(portion); portion.Clear(); vertices = 0; }
                    portion.Add(surface); vertices += next;
                }
                QueuePortion(portion);
            }
        }

        private void QueuePortion(List<Surface> members)
        { if (members.Count > 1) _parts.Enqueue(new List<Surface>(members)); }

        private void DrainBatches(int budget)
        {
            while (budget-- > 0 && _parts.Count > 0)
            {
                List<Surface> members = _parts.Dequeue();
                // A native hide, material load or room change may occur after preparation.
                // Revalidate admission rather than publishing a stale substitute.
                members.RemoveAll(s => s.Renderer == null || s.Filter == null || s.Tile == null
                    || s.Mesh == null || !s.Mesh.isReadable || s.Filter.sharedMesh != s.Mesh
                    || s.Renderer.forceRenderingOff || !s.Renderer.enabled
                    || !s.Renderer.gameObject.activeInHierarchy || s.Renderer.HasPropertyBlock()
                    || s.Renderer.sharedMaterials.Length != 1
                    || !(s.Floor ? _batchOn : s.Structural && _structuralOn && s.IsApplied())
                    || !CompatibleMaterial(s.Renderer.sharedMaterial, s.Floor || s.Structural)
                    || s.Renderer.transform.localToWorldMatrix.determinant <= 0f
                    || _batchBySource.ContainsKey(s.Renderer.GetInstanceID()));
                if (members.Count < 2) continue;
                Material material = members[0].Renderer.sharedMaterial;
                members.RemoveAll(s => s.Renderer.sharedMaterial != material
                    || !SameRenderFlags(s.Renderer, members[0].Renderer));
                CreateBatch(members);
            }
        }

        internal void BeforeNativeRendererWrite(Renderer renderer)
        {
            int id = renderer.GetInstanceID();
            InvalidateBatch(id);
            if (_surfaces.TryGetValue(id, out Surface surface))
            {
                surface.RestoreMaterial();
                _surfaces.Remove(id);
            }
        }

        private void RetireChangedNativeMaterials()
        {
            // Native code can enable a material keyword/property in-place without a
            // MaterialLoader completion. Revalidate the original, not saved properties
            // copied into our variant. No hierarchy query or material-array allocation
            // is needed on the unchanged path. Restore before either eye is culled.
            // Build624's three-room Frame capture measures 3.653ms/frame in PreCull.
            // Many admitted surfaces share one original; repeating that material's
            // native shader/property/keyword reads per renderer adds no new evidence
            // within this synchronous callback. Reuse only this invocation's verdict:
            // the next camera/eye must see in-place native edits immediately. The
            // renderer-specific property-block veto remains live for every surface.
            _preCullMaterialVerdicts.Clear();
            _dead.Clear();
            foreach (var pair in _surfaces)
            {
                Surface surface = pair.Value;
                if (surface.Applied == null || surface.Renderer == null) continue;
                bool compatible = !surface.Renderer.HasPropertyBlock() && (surface.Floor || surface.Structural);
                foreach (Material original in surface.Original)
                {
                    if (original == null) { compatible = false; continue; }
                    if (!_preCullMaterialVerdicts.TryGetValue(original, out bool materialCompatible))
                    {
                        materialCompatible = CompatibleMaterial(original, true);
                        _preCullMaterialVerdicts.Add(original, materialCompatible);
                    }
                    compatible &= materialCompatible;
                }
                if (!compatible) _dead.Add(pair.Key);
            }
            _preCullMaterialVerdicts.Clear();
            foreach (int id in _dead)
            {
                Surface surface = _surfaces[id];
                InvalidateBatch(id);
                surface.RestoreMaterial();
                _surfaces.Remove(id);
            }
            _dead.Clear();
        }

        private void InvalidateBatch(int sourceId)
        {
            if (!_batchBySource.TryGetValue(sourceId, out Batch batch)) return;
            foreach (Surface source in batch.Sources) _batchBySource.Remove(source.Id);
            batch.Dispose(); _batches.Remove(batch); _buildPending = true;
        }

        private void CreateBatch(List<Surface> members)
        {
            if (members.Count < 2) return;
            Surface first = members[0];
            var batch = new Batch { Material = first.Renderer.sharedMaterial };
            var child = new GameObject("GloomhavenVR.StaticScenarioChunk");
            child.layer = first.Renderer.gameObject.layer;
            child.transform.SetParent(first.Tile.transform, false);
            batch.Object = child;
            try
            {
                var combines = new CombineInstance[members.Count];
                for (int i = 0; i < members.Count; i++)
                {
                    Surface surface = members[i];
                    Matrix4x4 matrix = child.transform.worldToLocalMatrix * surface.Renderer.transform.localToWorldMatrix;
                    combines[i] = new CombineInstance { mesh = surface.Mesh, subMeshIndex = 0, transform = matrix };
                    batch.Sources.Add(surface); batch.Matrices.Add(matrix);
                }
                batch.Mesh = new Mesh { name = "GloomhavenVR.StaticScenarioChunkMesh" };
                batch.Mesh.CombineMeshes(combines, true, true, false);
                child.AddComponent<MeshFilter>().sharedMesh = batch.Mesh;
                batch.Renderer = child.AddComponent<MeshRenderer>();
                batch.Renderer.enabled = false;
                batch.Renderer.sharedMaterial = batch.Material;
                batch.Renderer.shadowCastingMode = first.Renderer.shadowCastingMode;
                batch.Renderer.receiveShadows = first.Renderer.receiveShadows;
                batch.Renderer.lightProbeUsage = first.Renderer.lightProbeUsage;
                batch.Renderer.reflectionProbeUsage = first.Renderer.reflectionProbeUsage;
                batch.Renderer.probeAnchor = first.Renderer.probeAnchor;
                batch.Renderer.motionVectorGenerationMode = first.Renderer.motionVectorGenerationMode;
                batch.Renderer.allowOcclusionWhenDynamic = first.Renderer.allowOcclusionWhenDynamic;
                batch.Renderer.renderingLayerMask = first.Renderer.renderingLayerMask;
                _batches.Add(batch);
                foreach (Surface source in members) _batchBySource.Add(source.Renderer.GetInstanceID(), batch);
                // Sources retain their native flags between camera renders. Apparance may
                // instantiate them during native Update; it must never inherit our draw mask.
            }
            catch
            {
                batch.Dispose();
                throw;
            }
        }

        private void LateUpdate()
        {
            if (!_active) return;
            using var scope = PerfMonitor.Scope("EnvironmentBudget.Late");
            try
            {
                foreach (Ambient ambient in _ambient.Values) ambient.Apply(Hide(ambient.Hash));
            }
            catch (Exception error) { StopAfterFailure(error); }
        }
        private void HandlePreCull(Camera camera)
        {
            if (!_active || (_surfaces.Count == 0 && _batches.Count == 0 && _ambient.Count == 0)) return;
            // A native visibility callback can run after LateUpdate. Validation is idempotent
            // and precedes each camera's culling, including both eyes in MultiPass.
            using var scope = PerfMonitor.Scope("EnvironmentBudget.PreCull");
            try
            {
                RetireChangedNativeMaterials();
                _renderDepth++;
                // DrawRenderer command buffers target exact native Renderer identities.
                // Keep these sources available with native flags for command-buffer
                // consumers; such cameras use originals, never a substitute mask.
                if (camera != null && camera.commandBufferCount > 0)
                    foreach (Batch batch in _batches) { batch.Unmask(); }
                else ValidateBatches();
                foreach (Ambient ambient in _ambient.Values) ambient.Mask(Hide(ambient.Hash));
            }
            catch (Exception error) { StopAfterFailure(error); }
        }
        private void HandlePostRender(Camera camera)
        {
            if (_renderDepth <= 0 || --_renderDepth > 0) return;
            foreach (Batch batch in _batches) batch.Unmask();
            foreach (Ambient ambient in _ambient.Values) ambient.Unmask();
        }
        private void ValidateBatches() { foreach (Batch batch in _batches) batch.Validate(); }
        private void ReleaseBatches()
        {
            foreach (Batch batch in _batches) batch.Dispose();
            _batches.Clear(); _batchBySource.Clear(); _parts.Clear(); _renderDepth = 0;
        }
        private void Report()
        {
            int batched = 0;
            foreach (Batch batch in _batches) batched += batch.Sources.Count;
            if (!VRLog.Wants(VRLogLevel.Debug)) return;
            VRLog.Debug(Scope, "Scenario environment budget: " + _surfaces.Count + " compatible static surfaces; "
                + batched + " source renderers / " + _batches.Count + " chunks; " + _unreadable
                + " unreadable originals retained; " + _materials.Count + " simpler materials; "
                + (_structuralOn ? "audited structural chunks on; " : "structural chunks off; ")
                + _ambient.Count + " identified ambient solvers; effects " + _effects + "%. Actual FPS remains a hardware measurement.");
        }
        private void RestoreClonedMaterials()
        {
            if (_originalByVariant.Count == 0) return;
            SceneRegistry.MapTiles.Collect(_tiles);
            foreach (ProceduralMapTile tile in _tiles)
            {
                if (tile == null) continue;
                foreach (MeshRenderer renderer in tile.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer == null || TileScope(renderer.transform, out _) == null) continue;
                    Material[] current = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < current.Length; i++)
                        if (current[i] != null && _originalByVariant.TryGetValue(current[i], out Material original))
                        { current[i] = original; changed = true; }
                    if (changed) renderer.sharedMaterials = current;
                }
            }
            _tiles.Clear();
        }

        internal void RestoreAll()
        {
            _preCullMaterialVerdicts.Clear();
            ReleaseBatches();
            foreach (Surface surface in _surfaces.Values) surface.RestoreMaterial();
            foreach (Ambient ambient in _ambient.Values) ambient.Apply(false);
            RestoreClonedMaterials();
            foreach (Material material in _materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
            _materials.Clear(); _originalByVariant.Clear(); _surfaces.Clear(); _ambient.Clear(); _pending.Clear(); _queued.Clear();
            _buildPending = false;
        }
    }
}

[HarmonyPatch(typeof(ProceduralBase), nameof(ProceduralBase.NotifyContentPlacementComplete))]
internal static class ProceduralBase_Placed_EnvironmentBudgetPatch
{
    private static void Postfix(ProceduralBase __instance)
    { try { ScenarioEnvironmentBudget.Placed(__instance.gameObject); } catch (Exception e) { ScenarioEnvironmentBudget.StopAfterFailure(e); } }
}
[HarmonyPatch(typeof(ProceduralMapTile), nameof(ProceduralMapTile.ShowContent))]
internal static class ProceduralMapTile_Show_EnvironmentBudgetPatch
{
    private static void Prefix() => ScenarioEnvironmentBudget.BeforeNativeContentChange();
    private static void Postfix(GameObject o)
    { try { ScenarioEnvironmentBudget.Placed(o); } catch (Exception e) { ScenarioEnvironmentBudget.StopAfterFailure(e); } }
}
[HarmonyPatch(typeof(MaterialLoaderData), "CheckAllMaterialLoaded")]
internal static class MaterialLoaderData_Ready_EnvironmentBudgetPatch
{
    private static void Prefix(MaterialLoaderData __instance)
    { try { if (__instance.Renderer != null) ScenarioEnvironmentBudget.BeforeNativeRendererWrite(__instance.Renderer); } catch (Exception e) { ScenarioEnvironmentBudget.StopAfterFailure(e); } }
    private static void Postfix(MaterialLoaderData __instance)
    { try { if (__instance.Renderer != null) ScenarioEnvironmentBudget.MaterialReady(__instance.Renderer); } catch (Exception e) { ScenarioEnvironmentBudget.StopAfterFailure(e); } }
}
[HarmonyPatch(typeof(SceneController), nameof(SceneController.DisableLoadingScreen))]
internal static class SceneController_Loaded_EnvironmentBudgetPatch
{
    private static void Prefix()
    { try { ScenarioEnvironmentBudget.BeforeLoadingComplete(); } catch (Exception e) { ScenarioEnvironmentBudget.StopAfterFailure(e); } }
}
