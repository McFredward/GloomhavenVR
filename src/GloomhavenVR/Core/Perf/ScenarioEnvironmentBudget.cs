using System;
using System.Collections.Generic;
using System.Reflection;
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
    private static Func<bool>? _roomFloorGroupsEnabled;
    private static Func<Mesh, bool>? _roomFloorEligible;
    internal delegate bool RoomFloorLookup(Renderer renderer, out Mesh mesh);
    private static RoomFloorLookup? _roomFloorLookup;
    private static Func<Material, Material>? _roomFloorVariant;
    private static Func<IDisposable>? _roomFloorMaterialPass;
    private static Func<IDisposable>? _roomFloorReadPass;
    internal static void ConfigureRoomFloorReads(Func<IDisposable> pass) => _roomFloorReadPass = pass;
    internal static void ConfigureRoomFloorMaterials(Func<Material, Material> variant, Func<IDisposable> pass)
    { _roomFloorVariant = variant; _roomFloorMaterialPass = pass; }
    private static bool TryRoomFloorMaterial(Material source, out Material draw)
    {
        draw = source;
        if (!CompatibleMaterial(source, true, true)) return false;
        if (_worldEnabled?.Invoke() == true && _roomFloorVariant != null)
        {
            draw = _roomFloorVariant(CanonicalMaterial(source));
            return draw != null && _worldOwns?.Invoke(draw) == true;
        }
        // Original shaders cannot interpret our private never-fade markers. A live
        // native dissolve channel requires its owned floor-aware material variant.
        return !NativeWallFadeEnabled(CanonicalMaterial(source));
    }
    internal static void ConfigureRoomFloorGrouping(Func<bool> enabled, Func<Mesh, bool> eligible, RoomFloorLookup lookup)
    { _roomFloorGroupsEnabled = enabled; _roomFloorEligible = eligible; _roomFloorLookup = lookup; }
    internal static void RoomFloorMeshReady(Renderer renderer)
    {
        try { if (!_failed && renderer != null) _driver?.MaterialReady(renderer); }
        catch (Exception error) { StopAfterFailure(error); }
    }
    internal static bool HasPreparedRoomFloorGroup(Renderer renderer) =>
        _driver != null && renderer != null && _driver.HasPreparedRoomFloorGroup(renderer);
    private static bool RoomFloorGroupsEnabled => _roomFloorGroupsEnabled?.Invoke() == true;
    private static bool TryRoomFloorMesh(Renderer renderer, out Mesh mesh)
    { mesh = null!; return _roomFloorLookup?.Invoke(renderer, out mesh) == true; }


    internal static void ConfigureStructuralBatching(Func<bool> enabled) => _structuralEnabled = enabled;
    private static bool StructuralEnabled => _structuralEnabled?.Invoke() == true;
    private static Action<GameObject>? _terrainQueue;
    private static Action<Renderer>? _terrainReady, _terrainBeforeWrite;
    private static Action? _terrainBeforeContent;
    private static Func<Renderer, bool>? _terrainOwns;
    private static Action<GameObject>? _worldQueue;
    private static Action<Renderer>? _worldReady, _worldBeforeWrite;
    private static Action? _worldBeforeContent;
    private static Func<Material, Material>? _worldCanonical;
    private static Func<Material, bool>? _worldOwns;
    private static Func<bool>? _worldEnabled;
    internal static void ConfigureWorldMaterialIntegration(Action<GameObject> queue, Action<Renderer> ready,
        Action<Renderer> beforeWrite, Action beforeContent, Func<Material, Material> canonical,
        Func<Material, bool> owns, Func<bool> enabled)
    { _worldQueue = queue; _worldReady = ready; _worldBeforeWrite = beforeWrite; _worldBeforeContent = beforeContent;
        _worldCanonical = canonical; _worldOwns = owns; _worldEnabled = enabled; }
    internal static void ConfigureTerrainIntegration(Action<GameObject> queue, Action<Renderer> ready,
        Action<Renderer> beforeWrite, Action beforeContent, Func<Renderer, bool> owns)
    { _terrainQueue = queue; _terrainReady = ready; _terrainBeforeWrite = beforeWrite; _terrainBeforeContent = beforeContent; _terrainOwns = owns; }
    internal static bool OwnsRenderSubstitute(Renderer renderer) => _driver != null && _driver.OwnsSubstitute(renderer);
    internal static Material CanonicalMaterial(Material material)
    {
        Material original = _driver?.CanonicalMaterial(material) ?? material;
        return _worldCanonical?.Invoke(original) ?? original;
    }
    // A material owner notifies reference changes after its own enumeration. Release
    // obsolete chunks/proxies without calling back into that owner's restoration path.
    internal static void WorldMaterialChanged(Renderer renderer)
    {
        if (renderer == null) return;
        try
        {
            _terrainBeforeWrite?.Invoke(renderer);
            if (_failed) return;
            _driver?.BeforeNativeRendererWrite(renderer);
            _driver?.MaterialReady(renderer);
        }
        catch (Exception error) { StopAfterFailure(error); }
    }
    internal static void BeforeWorldMaterialDisposal()
    {
        // Factory-only terrain consumers may precede bounded world discovery.
        // Release current and queued substitutes before their material is destroyed.
        _terrainBeforeContent?.Invoke();
        _driver?.WorldMaterialsDisposing();
    }
    private static bool TerrainOwns(Renderer renderer) => _terrainOwns?.Invoke(renderer) == true;
    internal static bool HasNativeCommandBufferConsumers(Camera camera) => camera != null
        && camera.commandBufferCount > 0 && (_driver == null || _driver.HasForeignCommands(camera));

    // Actual queued discovery/substitute construction only. Ongoing cull leases and
    // material revalidation are steady presentation, not a reason to keep a spinner up.
    internal static bool IsPreparingPresentation => !_failed && VRSession.IsRunning
        && _driver != null && _driver.IsPreparingPresentation;

    // A renderer write can occur inside a nested render callback. Drop its substitute
    // synchronously, before native/wall effects can encounter an old chunk or mask.
    internal static void BeforeNativeRendererWrite(Renderer renderer)
    {
        if (renderer == null) return;
        try { _worldBeforeWrite?.Invoke(renderer); _terrainBeforeWrite?.Invoke(renderer); if (!_failed) _driver?.BeforeNativeRendererWrite(renderer); }
        catch (Exception error) { StopAfterFailure(error); }
    }
    internal static void BeforeNativeContentChange()
    {
        try
        {
            try { _worldBeforeContent?.Invoke(); _terrainBeforeContent?.Invoke(); }
            finally { _driver?.RecoverRenderLeases(); }
        }
        catch (Exception error) { StopAfterFailure(error); }
    }

    internal static void Install(GameObject host)
    {
        if (_driver != null) return;
        _driver = host.AddComponent<Driver>();
        _recovery = host.AddComponent<LeaseRecovery>();
        try
        {
            ScenarioCameraCullBoundary.Install();
            ScenarioCameraCullBoundary.Subscribe(AfterNativePreCull);
            VRSession.Harmony?.PatchAll(typeof(ProceduralBase_Placed_EnvironmentBudgetPatch));
            VRSession.Harmony?.PatchAll(typeof(ProceduralMapTile_Show_EnvironmentBudgetPatch));
            VRSession.Harmony?.PatchAll(typeof(MaterialLoaderData_Load_EnvironmentBudgetPatch));
            VRSession.Harmony?.PatchAll(typeof(MaterialLoaderData_Ready_EnvironmentBudgetPatch));
            VRSession.Harmony?.PatchAll(typeof(SceneController_Loaded_EnvironmentBudgetPatch));
            VRSession.Harmony?.PatchAll(typeof(ApparanceEntity_EnvironmentBudgetPatch));
        }
        catch (Exception error) { StopAfterFailure(error); }
    }

    internal static void Shutdown()
    {
        if (_driver == null) return;
        ScenarioCameraCullBoundary.Unsubscribe(AfterNativePreCull);
        _driver.RestoreAll();
        UnityEngine.Object.Destroy(_driver);
        _driver = null;
        if (_recovery != null) UnityEngine.Object.Destroy(_recovery);
        _recovery = null;
        _failed = false;
    }

    internal static void Placed(GameObject root) { _worldQueue?.Invoke(root); _terrainQueue?.Invoke(root); if (!_failed) _driver?.QueueRoot(root); }
    internal static void MaterialReady(Renderer renderer)
    {
        if (renderer == null) return;
        try { _worldReady?.Invoke(renderer); _terrainReady?.Invoke(renderer); if (!_failed) _driver?.MaterialReady(renderer); }
        catch (Exception error) { StopAfterFailure(error); }
    }
    internal static void BeforeLoadingComplete() { if (!_failed) _driver?.FinishLoading(); }
    private static void AfterNativePreCull(Camera camera)
    {
        try { if (!_failed) _driver?.FinishCameraPreCull(camera); }
        catch (Exception error) { StopAfterFailure(error); }
    }
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

    private static bool CompatibleMaterial(Material material, bool floor, bool roomFloor = false)
    {
        material = CanonicalMaterial(material);
        if (!floor || material == null || material.shader == null) return false;
        if (material.renderQueue > 2500 || Gate(material, "_AddVertexAnim")
            || Gate(material, "_UseEmissiveMap") || Gate(material, "_Diffuse_Emissive_On")) return false;
        // Wider floor roles do not weaken the legacy material contract. An active
        // world owner proves its complete current native shader/program/effect
        // contract, including the other game/DLC families beyond N_MRAO.
        if (roomFloor && _worldEnabled?.Invoke() == true && _roomFloorVariant != null)
            return _worldOwns?.Invoke(_roomFloorVariant(material)) == true;
        string shader = material.shader.name;
        if (shader != "Amp_Basic_N_MRAO" && shader != "Amp_Low/Amp_Basic_N_MRAO_Low"
            && shader != SimpleShader) return false;
        // Frame615 logs identify CV_Floor_Basic_M (VR simple environment) as
        // "toggle-native" and include the cheap shader in Wall25's native fade set.
        // CopyPropertiesFromMaterial also preserves saved properties absent from the
        // new shader; HasProperty/GetFloat can therefore advertise a native dissolve
        // which this shader does not render. An authored floor label does not prove
        // that its material has no native wall channel. Preserve every live channel,
        // even on floors, rather than replacing animation with a cutoff/enable pop.
        if (NativeWallFadeEnabled(material) && !roomFloor) return false;
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
        return ScenarioEnvironmentAmbientEffects.Identity(leaf, tile);
    }

    private sealed class Surface
    {
        internal MeshRenderer Renderer = null!;
        internal int Id;
        internal MeshFilter Filter = null!;
        internal ProceduralMapTile Tile = null!;
        internal bool Floor;
        internal bool RoomFloor;
        internal bool Structural;
        internal bool TerrainOwned;
        internal Mesh Mesh = null!;
        internal Mesh? ReadableMesh;
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
        internal bool StructuralMaterialReady()
        {
            if (IsApplied()) return true;
            if (_worldEnabled?.Invoke() != true || Renderer == null) return false;
            Material[] current = Renderer.sharedMaterials;
            if (current.Length != Original.Length || current.Length == 0) return false;
            for (int i = 0; i < current.Length; i++)
                if (_worldOwns?.Invoke(current[i]) != true || CanonicalMaterial(current[i]) != Original[i]) return false;
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
            if (!hidden)
            {
                Unmask();
                if (_paused && System != null && System.isPaused) System.Play(false);
                _paused = false;
                return;
            }
            if (System == null || Renderer == null) return;
            // Rendering can be optional while collision/trigger/stop callbacks are not.
            // Retain their native simulation, never Stop/Clear, and never pause children.
            bool mayPause = ScenarioEnvironmentAmbientEffects.CanPause(System);
            if (!mayPause && _paused)
            { if (System.isPaused) System.Play(false); _paused = false; }
            if (mayPause && System.isPlaying) { System.Pause(false); _paused = true; }
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
        internal bool RoomFloor;
        internal Material? NativeMaterial;
        internal Transform? RoomTile;
        internal long NativeFloorTriangles, SubmittedFloorTriangles;
        internal readonly List<Surface> Sources = new();
        internal readonly List<Matrix4x4> Matrices = new();
        private bool _owned;
        internal int MaskedSourceCount => _owned ? Sources.Count : 0;
        internal bool LightingRefused;
        private readonly List<Material> _materialScratch = new(1);

        internal bool HasLateLightingWrite()
        {
            if (!_owned || Renderer == null) return false;
            LightProbeUsage lightUsage = Renderer.lightProbeUsage;
            ReflectionProbeUsage reflectionUsage = Renderer.reflectionProbeUsage;
            // Only masked originals are at risk. This is a cheap native lighting
            // flag comparison after all camera callbacks, not another material,
            // transform, geometry or reflection-volume validation sweep.
            foreach (Surface source in Sources)
            {
                MeshRenderer r = source.Renderer;
                if (r == null || HasNativeLightmap(r) || r.lightProbeUsage != lightUsage || r.reflectionProbeUsage != reflectionUsage
                    || (lightUsage != LightProbeUsage.Off && r.lightProbeProxyVolumeOverride != null)) return true;
            }
            return false;
        }

        internal static bool TryTileMatrix(Surface source, Transform? tile, out Matrix4x4 matrix)
        {
            // Compare the current native local chain rather than cancelling two
            // large world matrices: common board motion must not introduce inverse
            // rounding differences or conceal an independently moved floor source.
            matrix = Matrix4x4.identity;
            Transform? node = source.Renderer != null ? source.Renderer.transform : null;
            while (node != tile)
            {
                if (node == null || tile == null) return false;
                matrix = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale) * matrix;
                node = node.parent;
            }
            return node != null;
        }

        private static bool UniformFrame(Transform? node, out float scale)
        {
            scale = 0f;
            if (node == null) return false;
            for (Transform? current = node; current != null; current = current.parent)
            {
                Vector3 local = current.localScale;
                float tolerance = 1e-5f * Mathf.Max(local.x, Mathf.Max(local.y, local.z));
                if (!(local.x > 0f && local.y > 0f && local.z > 0f)
                    || Mathf.Abs(local.x - local.y) > tolerance || Mathf.Abs(local.x - local.z) > tolerance) return false;
            }
            Matrix4x4 world = node.localToWorldMatrix;
            for (int i = 0; i < 16; i++) if (float.IsNaN(world[i]) || float.IsInfinity(world[i])) return false;
            Vector3 x = world.GetColumn(0), y = world.GetColumn(1), z = world.GetColumn(2);
            scale = x.magnitude;
            if (!(scale > 0f) || float.IsInfinity(scale)) return false;
            float epsilon = 1e-5f * scale;
            return Mathf.Abs(y.magnitude - scale) <= epsilon && Mathf.Abs(z.magnitude - scale) <= epsilon
                && Mathf.Abs(Vector3.Dot(x, y)) <= epsilon * scale
                && Mathf.Abs(Vector3.Dot(x, z)) <= epsilon * scale
                && Mathf.Abs(Vector3.Dot(y, z)) <= epsilon * scale && world.determinant > 0f;
        }

        internal bool FollowRoomTile()
        {
            if (!RoomFloor) return true;
            Transform? parent = Object != null ? Object.transform.parent : null;
            if (!UniformFrame(RoomTile, out float tileScale) || !UniformFrame(parent, out float parentScale)) return false;
            Transform chunk = Object!.transform;
            Vector3 position = parent!.InverseTransformPoint(RoomTile!.position);
            Quaternion rotation = Quaternion.Inverse(parent.rotation) * RoomTile.rotation;
            Vector3 scale = Vector3.one * (tileScale / parentScale);
            // The private chunk stays outside native clone roots. A grabbed,
            // recentered or uniformly scaled board only changes its private pose;
            // native transforms and the prepared combined mesh remain untouched.
            if (chunk.localPosition != position) chunk.localPosition = position;
            if (chunk.localRotation != rotation) chunk.localRotation = rotation;
            if (chunk.localScale != scale) chunk.localScale = scale;
            return true;
        }

        internal void FinishRoomPose()
        {
            // A later pre-cull listener can move the whole board without a native
            // renderer setter. Follow it before culling, or return its originals.
            if (_owned && RoomFloor && !FollowRoomTile()) Unmask();
        }

        internal void Validate()
        {
            LightingRefused = false;
            bool valid = Object != null && Material != null;
            if (valid && RoomFloor)
            {
                valid = FollowRoomTile() && NativeMaterial != null && TryRoomFloorMaterial(NativeMaterial, out Material currentDraw)
                    && currentDraw == Material;
            }
            Matrix4x4 inverse = Object != null ? Object.transform.worldToLocalMatrix : Matrix4x4.identity;
            for (int i = 0; valid && i < Sources.Count; i++)
            {
                Surface source = Sources[i];
                MeshRenderer r = source.Renderer;
                _materialScratch.Clear();
                if (r != null) r.GetSharedMaterials(_materialScratch);
                valid = r != null && (source.RoomFloor || !TerrainOwns(r)) && r.enabled && r.gameObject.activeInHierarchy && !r.HasPropertyBlock()
                    && NativeGeometryCompatible(r) && r.forceRenderingOff == _owned && source.Filter != null
                    && source.Filter.sharedMesh == source.Mesh && _materialScratch.Count == 1
                    && _materialScratch[0] == (RoomFloor ? NativeMaterial : Material)
                    && Renderer != null && SameRenderFlags(r, Renderer)
                    && Object != null && r.gameObject.layer == Object.layer
                    && (RoomFloor ? TryTileMatrix(source, RoomTile, out Matrix4x4 tileMatrix) && tileMatrix == Matrices[i]
                        : inverse * r.transform.localToWorldMatrix == Matrices[i]);
                if (valid && source.RoomFloor)
                    valid = RoomFloorGroupsEnabled && TryRoomFloorMesh(r!, out Mesh currentFloor)
                        && currentFloor == source.ReadableMesh;
                if (valid && HasNativeLightmap(r!)) { valid = false; LightingRefused = true; }
                if (valid && !ChunkLightingCompatible(r!)) { valid = false; LightingRefused = true; }
            }
            // Aggregate bounds can intersect a local reflection volume which none
            // of the separate source bounds intersects. Never adopt its sample.
            if (valid && !ChunkLightingCompatible(Renderer!)) { valid = false; LightingRefused = true; }
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

    private sealed class InstanceBatch
    {
        private static readonly FieldInfo? BufferPointer = typeof(CommandBuffer).GetField("m_Ptr", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool SameBuffer(CommandBuffer a, CommandBuffer b)
        {
            if (ReferenceEquals(a, b)) return true;
            // Camera.GetCommandBuffers creates new managed wrappers on Unity2021.3.5.
            // Names/counts cannot establish ownership: compare the actual nonzero native
            // command-buffer identity, and fail open if this runtime hides that field.
            if (BufferPointer == null) return false;
            object? first = BufferPointer.GetValue(a), second = BufferPointer.GetValue(b);
            return first is IntPtr pointer && pointer != IntPtr.Zero && second is IntPtr other && pointer == other;
        }
        internal Mesh Mesh = null!;
        internal readonly List<Surface> Sources = new();
        internal Material[] Materials = Array.Empty<Material>();
        internal Material[] DrawMaterials = Array.Empty<Material>();
        private readonly Matrix4x4[] _matrices = new Matrix4x4[MaxBatchMembers];
        private readonly Bounds[] _bounds = new Bounds[MaxBatchMembers];
        private readonly List<Material> _scratch = new();
        private bool _owned;
        internal int MaskedSourceCount => _owned ? Sources.Count : 0;
        private sealed class Submission { internal Camera Camera = null!; internal CommandBuffer Buffer = null!; internal bool Active; }
        private readonly List<Submission> _submitted = new();

        private bool Valid(Camera camera)
        {
            if (camera == null || camera.actualRenderingPath != RenderingPath.Forward || camera.depthTextureMode != DepthTextureMode.None || !SystemInfo.supportsInstancing
                || Mesh == null || Sources.Count < 2 || Materials.Length != Mesh.subMeshCount) return false;
            Surface first = Sources[0];
            MeshRenderer prototype = first.Renderer;
            if (prototype == null || (camera.cullingMask & (1 << prototype.gameObject.layer)) == 0) return false;
            for (int index = 0; index < Sources.Count; index++)
            {
                Surface source = Sources[index]; MeshRenderer r = source.Renderer;
                _scratch.Clear(); if (r != null) r.GetSharedMaterials(_scratch);
                if (r == null || TerrainOwns(r) || !r.enabled || !r.gameObject.activeInHierarchy || r.HasPropertyBlock()
                    || r.forceRenderingOff != _owned || source.Filter == null || source.Filter.sharedMesh != Mesh
                    || _scratch.Count != Materials.Length || !SameRenderFlags(r, prototype)
                    || r.gameObject.layer != prototype.gameObject.layer || !SupportedInstanceFlags(r)) return false;
                for (int sub = 0; sub < Materials.Length; sub++)
                    if (_scratch[sub] != Materials[sub] || !CompatibleMaterial(source.Original[sub], source.Floor || source.Structural)
                        || !SupportsInstanceMaterial(Materials[sub])) return false;
                _matrices[index] = r.transform.localToWorldMatrix;
                if (_matrices[index].determinant <= 0f) return false;
                _bounds[index] = r.bounds;
                // Instancing changes opaque submission order. Overlapping native surfaces,
                // particularly multiple material slots on coplanar floors, retain originals.
                for (int previous = 0; previous < index; previous++)
                    if (_bounds[previous].Intersects(_bounds[index])) return false;
            }
            return true;
        }
        internal void Submit(Camera camera, bool foreignCommands)
        {
            Unmask();
            if (foreignCommands || !Valid(camera)) return;
            // A queued Graphics.DrawMeshInstanced cannot be revoked when a later native
            // pre-cull callback changes a wall or creates a room. Keep a private command
            // buffer attached to this exact camera instead; a native write clears/removes
            // it synchronously before originals are restored, so neither old nor double
            // geometry survives. Unsupported shadows/probes/render paths stay original.
            Submission? submission = null;
            foreach (Submission known in _submitted) if (known.Camera == camera) { submission = known; break; }
            if (submission == null)
            {
                if (_submitted.Count >= 8) return;
                submission = new Submission { Camera = camera, Buffer = new CommandBuffer { name = "GloomhavenVR.EnvironmentInstances" } };
                _submitted.Add(submission);
            }
            CommandBuffer buffer = submission.Buffer; buffer.Clear();
            try
            {
                for (int sub = 0; sub < Materials.Length; sub++)
                {
                    Material draw = DrawMaterials[sub];
                    if (draw != Materials[sub]) { draw.CopyPropertiesFromMaterial(Materials[sub]); draw.enableInstancing = true; }
                    buffer.DrawMeshInstanced(Mesh, sub, draw, 0, _matrices, Sources.Count);
                }
                camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, buffer);
                submission.Active = true;
                Mask();
            }
            catch { camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, buffer); buffer.Clear(); submission.Active = false; Unmask(); throw; }
        }
        private void Mask()
        {
            foreach (Surface source in Sources) source.Renderer.forceRenderingOff = true;
            _owned = true;
        }
        internal void RestoreOuterMask(Camera camera)
        {
            bool submitted = false;
            foreach (Submission submission in _submitted) submitted |= submission.Active && submission.Camera == camera;
            if (submitted && Valid(camera)) Mask();
            else if (submitted) EndCamera(camera);
        }
        internal void Unmask()
        {
            if (_owned) foreach (Surface source in Sources)
                if (source.Renderer != null && source.Renderer.forceRenderingOff) source.Renderer.forceRenderingOff = false;
            _owned = false;
        }
        internal int ActiveBuffers(Camera camera)
        { int count = 0; foreach (Submission submission in _submitted) if (submission.Active && submission.Camera == camera) count++; return count; }
        internal bool OwnsBuffer(Camera camera, CommandBuffer buffer)
        { foreach (Submission submission in _submitted) if (submission.Active && submission.Camera == camera && SameBuffer(submission.Buffer, buffer)) return true; return false; }
        internal void EndCamera(Camera camera)
        {
            Unmask();
            for (int i = _submitted.Count - 1; i >= 0; i--)
            {
                Submission submission = _submitted[i];
                if (submission.Camera != camera) continue;
                if (submission.Camera != null) submission.Camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, submission.Buffer);
                submission.Buffer.Clear(); submission.Active = false;
            }
        }
        internal void ClearCameras()
        {
            Unmask();
            foreach (Submission submission in _submitted)
            {
                if (submission.Camera != null) submission.Camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, submission.Buffer);
                submission.Buffer.Clear(); submission.Active = false;
            }
        }
        internal void Dispose()
        {
            ClearCameras();
            foreach (Submission submission in _submitted) submission.Buffer.Release();
            _submitted.Clear();
            for (int i = 0; i < DrawMaterials.Length; i++)
                if (DrawMaterials[i] != null && DrawMaterials[i] != Materials[i]) UnityEngine.Object.Destroy(DrawMaterials[i]);
        }
    }
    private static bool SupportsInstanceMaterial(Material material) => material != null && material.shader != null
        && material.shader.isSupported && material.shader.keywordSpace.FindKeyword("INSTANCING_ON").isValid
        && (material.enableInstancing || material.shader.name == SimpleShader);

    // Explicit mesh submissions cannot reproduce renderer-owned supplementary vertex
    // streams or Unity's internal batch geometry. Keep those exact native draws, and
    // recheck each camera so a late native stream write cannot lose its channels.
    private static bool NativeGeometryCompatible(MeshRenderer r) =>
        !r.isPartOfStaticBatch && r.additionalVertexStreams == null;

    private static bool HasNativeLightmap(MeshRenderer r)
    {
        int index = r.lightmapIndex;
        return index >= 0 && index < 65534;
    }

    private static readonly List<ReflectionProbeBlendInfo> ReflectionScratch = new(4);
    private static bool ChunkLightingCompatible(MeshRenderer r)
    {
        // Build627 authored BlendProbes on almost all Crypt originals, even when
        // no live baked probes exist. Flag presence is not per-object lighting.
        // Admit only the native common ambient/sky fallback, never an inferred
        // scene absence or copied aggregate sample. Check original AND proxy at
        // every camera; no source flags, probe data or anchors are overwritten.
        if (r.lightProbeUsage != LightProbeUsage.Off)
        {
            if (r.lightProbeUsage != LightProbeUsage.BlendProbes || r.lightProbeProxyVolumeOverride != null) return false;
            LightProbes probes = LightmapSettings.lightProbes;
            if (probes != null && probes.count != 0) return false;
        }
        if (r.reflectionProbeUsage == ReflectionProbeUsage.Off) return true;
        if (r.reflectionProbeUsage != ReflectionProbeUsage.BlendProbes
            && r.reflectionProbeUsage != ReflectionProbeUsage.BlendProbesAndSkybox
            && r.reflectionProbeUsage != ReflectionProbeUsage.Simple) return false;
        // Native registration events cover enable/add/remove, not every position,
        // influence-volume or texture write. With any active local probe, keep
        // authored probe consumers native even when its current bounds miss them.
        // Otherwise a later callback could move that volume inside already-culled
        // source bounds. Common global sky reflection remains admissible.
        if (_driver == null || _driver.HasLocalReflectionProbes) return false;
        ReflectionScratch.Clear();
        r.GetClosestReflectionProbes(ReflectionScratch);
        return ReflectionScratch.Count == 0;
    }

    private static bool SupportedInstanceFlags(MeshRenderer r) =>
        NativeGeometryCompatible(r) &&
        !(r.lightmapIndex >= 0 && r.lightmapIndex < 65534) && r.probeAnchor == null
        && r.shadowCastingMode == ShadowCastingMode.Off && !r.receiveShadows
        && r.lightProbeUsage == LightProbeUsage.Off
        && r.reflectionProbeUsage == ReflectionProbeUsage.Off
        && r.motionVectorGenerationMode != MotionVectorGenerationMode.ForceNoMotion
        && r.renderingLayerMask == 1u && r.allowOcclusionWhenDynamic
        && r.GetComponentInParent<LODGroup>(true) == null;

    private static bool SameRenderFlags(MeshRenderer a, MeshRenderer b) =>
        a.shadowCastingMode == b.shadowCastingMode && a.receiveShadows == b.receiveShadows
        && a.lightProbeUsage == b.lightProbeUsage && a.reflectionProbeUsage == b.reflectionProbeUsage
        && a.probeAnchor == b.probeAnchor && a.motionVectorGenerationMode == b.motionVectorGenerationMode
        && a.allowOcclusionWhenDynamic == b.allowOcclusionWhenDynamic
        && a.renderingLayerMask == b.renderingLayerMask;

    private readonly struct BatchKey : IEquatable<BatchKey>
    {
        private readonly int _tile, _material, _layer, _x, _z, _y, _flags, _kind;
        internal BatchKey(Surface surface, Material material)
        {
            _tile = surface.Tile.GetInstanceID(); _material = material.GetInstanceID();
            // Floor endpoint ownership and never-fade markers belong only to the
            // wider room-floor lane. Shared materials/bounds cannot merge it with
            // legacy floor or structural groups and transfer those contracts.
            _kind = surface.RoomFloor ? 2 : surface.Structural ? 1 : 0;
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
            && _layer == other._layer && _x == other._x && _z == other._z && _y == other._y && _flags == other._flags && _kind == other._kind;
        public override bool Equals(object? other) => other is BatchKey key && Equals(key);
        public override int GetHashCode()
        { unchecked { return ((((((_tile * 397 ^ _material) * 397 ^ _layer) * 397 ^ _x) * 397 ^ _z) * 397 ^ _y) * 397 ^ _flags) * 397 ^ _kind; } }
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
        private readonly List<InstanceBatch> _instances = new();
        private readonly Dictionary<int, InstanceBatch> _instanceBySource = new();
        private readonly List<Camera> _renderCameras = new();
        private readonly List<LightProbes?> _cameraProbeSources = new();
        private readonly List<int> _cameraProbeCounts = new();
        private readonly List<DepthTextureMode> _cameraDepthModes = new();
        private readonly List<RenderingPath> _cameraPaths = new();
        private readonly List<bool> _cameraHadForeignCommands = new();
        private readonly HashSet<ReflectionProbe> _reflectionProbes = new();
        internal bool HasLocalReflectionProbes => _reflectionProbes.Count > 0;
        private bool _instancesOn, _meshBankOn, _roomFloorGroupsOn;
        private readonly List<int> _dead = new();
        private bool _batchOn, _structuralOn, _simpleOn, _active, _worldOn, _buildPending, _reportPending;
        internal Material CanonicalMaterial(Material material) => material != null && _originalByVariant.TryGetValue(material, out Material original) ? original : material!;
        internal bool OwnsSubstitute(Renderer r) => r != null && (_batchBySource.ContainsKey(r.GetInstanceID()) || _instanceBySource.ContainsKey(r.GetInstanceID()));
        internal bool HasPreparedRoomFloorGroup(Renderer r) => RoomFloorGroupsEnabled
            && _batchBySource.TryGetValue(r.GetInstanceID(), out Batch batch) && batch.RoomFloor;
        internal bool HasForeignCommands(Camera camera)
        {
            int owned = 0; foreach (InstanceBatch batch in _instances) owned += batch.ActiveBuffers(camera);
            if (owned != camera.commandBufferCount) return true;
            CommandBuffer[] actual = camera.GetCommandBuffers(CameraEvent.BeforeForwardOpaque);
            if (actual.Length != owned) return true;
            foreach (CommandBuffer buffer in actual)
            {
                bool known = false;
                foreach (InstanceBatch batch in _instances) known |= batch.OwnsBuffer(camera, buffer);
                if (!known) return true;
            }
            return false;
        }
        private int _effects = 100, _unreadable, _probeRefusals, _renderDepth, _reports;
        private int _chunkCandidates, _instanceCandidates, _instanceFlagRefusals, _instanceMaterialRefusals;
        private int _meshVisits, _nativeScopeRefusals, _nativeMaterialRefusals, _nativeWallChannelRefusals;
        private int _terrainRefusals, _mpbRefusals, _lightmapRefusals, _lodRefusals, _shadowRefusals, _geometryRefusals, _lightProbeFlagRefusals, _reflectionFlagRefusals;
        private Shader? _shader;

        internal bool IsPreparingPresentation => _pending.Count > 0 || _parts.Count > 0;

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Camera.onPreCull += HandlePreCull;
            Camera.onPostRender += HandlePostRender;
            ReflectionProbe.reflectionProbeChanged += HandleReflectionProbeChange;
            LightProbes.needsRetetrahedralization += HandleLightProbeChange;
            LightProbes.tetrahedralizationCompleted += HandleLightProbeChange;
            RefreshReflectionProbes();
        }
        private void OnDisable() => RecoverRenderLeases();
        internal void RecoverRenderLeases()
        {
            if (_renderDepth == 0) return;
            _renderDepth = 0; _renderCameras.Clear(); _cameraProbeSources.Clear(); _cameraProbeCounts.Clear(); _cameraDepthModes.Clear(); _cameraPaths.Clear(); _cameraHadForeignCommands.Clear();
            foreach (InstanceBatch batch in _instances) batch.ClearCameras();
            foreach (Batch batch in _batches) batch.Unmask();
            foreach (Ambient ambient in _ambient.Values) ambient.Unmask();
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Camera.onPreCull -= HandlePreCull;
            Camera.onPostRender -= HandlePostRender;
            ReflectionProbe.reflectionProbeChanged -= HandleReflectionProbeChange;
            LightProbes.needsRetetrahedralization -= HandleLightProbeChange;
            LightProbes.tetrahedralizationCompleted -= HandleLightProbeChange;
            RestoreAll();
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { RefreshReflectionProbes(); if (VRSession.IsRunning) Seed(); }
        private void OnSceneUnloaded(Scene scene) { RestoreAll(); _reports = 0; }
        private void RefreshReflectionProbes()
        {
            _reflectionProbes.Clear();
            foreach (ReflectionProbe probe in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>())
                if (probe != null && probe.isActiveAndEnabled) _reflectionProbes.Add(probe);
        }
        private void HandleReflectionProbeChange(ReflectionProbe probe, ReflectionProbe.ReflectionProbeEvent change)
        {
            try
            {
                if (change == ReflectionProbe.ReflectionProbeEvent.ReflectionProbeAdded) _reflectionProbes.Add(probe);
                else _reflectionProbes.Remove(probe);
                // Runs inside the native enable/add call, before subsequent camera
                // culling. Pre-render recovery alone is too late: the originals may
                // already have been excluded from the native draw list.
                if (_renderDepth > 0) PerfMonitor.Count("Environment.LightingFallback");
                RecoverRenderLeases();
            }
            catch (Exception error) { StopAfterFailure(error); }
        }
        private void HandleLightProbeChange()
        {
            try { RecoverRenderLeases(); }
            catch (Exception error) { StopAfterFailure(error); }
        }
        private void Seed()
        {
            SceneRegistry.MapTiles.Collect(_tiles);
            foreach (ProceduralMapTile tile in _tiles) if (tile != null) QueueRoot(tile.gameObject);
            _tiles.Clear();
        }

        internal void QueueRoot(GameObject root)
        {
            if (!VRSession.IsRunning || root == null || !(PerfConfig.StaticScenarioBatchesOn
                || StructuralEnabled || RoomFloorGroupsEnabled || PerfConfig.EnvironmentDrawInstancingOn || PerfConfig.SimpleEnvironmentShadingOn || PerfConfig.EnvironmentEffectsDensityPercent < 100)) return;
            if (_queued.Add(root.GetInstanceID())) _pending.Enqueue(root.transform);
        }
        internal void MaterialReady(Renderer renderer)
        {
            if (renderer == null || !VRSession.IsRunning || !(PerfConfig.StaticScenarioBatchesOn
                || StructuralEnabled || RoomFloorGroupsEnabled || PerfConfig.EnvironmentDrawInstancingOn || PerfConfig.SimpleEnvironmentShadingOn || PerfConfig.EnvironmentEffectsDensityPercent < 100)) return;
            Settings();
            if (TileScope(renderer.transform, out _) == null)
            { AdoptAmbient(renderer.transform); return; }
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
                if (_reportPending && !_buildPending && !IsPreparingPresentation)
                { _reportPending = false; Report(); }
            }
            catch (Exception error) { StopAfterFailure(error); }
        }

        private void Settings()
        {
            bool batch = VRSession.IsRunning && PerfConfig.StaticScenarioBatchesOn;
            bool simple = VRSession.IsRunning && PerfConfig.SimpleEnvironmentShadingOn;
            bool structural = VRSession.IsRunning && StructuralEnabled && simple;
            bool world = VRSession.IsRunning && _worldEnabled?.Invoke() == true;
            structural |= world && StructuralEnabled;
            // The global owner supplies its variants; keep geometry submission available
            // without making two owners rewrite the same native material slots.
            simple &= !world;
            int effects = VRSession.IsRunning ? PerfConfig.EnvironmentEffectsDensityPercent : 100;
            bool instances = VRSession.IsRunning && PerfConfig.EnvironmentDrawInstancingOn;
            bool bank = VRSession.IsRunning && PerfConfig.EnvironmentMeshBankOn;
            bool roomFloors = VRSession.IsRunning && RoomFloorGroupsEnabled;
            bool active = batch || structural || instances || simple || world || roomFloors || effects < 100;
            if (batch == _batchOn && structural == _structuralOn && simple == _simpleOn && effects == _effects && active == _active && instances == _instancesOn && bank == _meshBankOn && world == _worldOn && roomFloors == _roomFloorGroupsOn) return;
            ReleaseBatches();
            foreach (Surface surface in _surfaces.Values) surface.RestoreMaterial();
            RestoreClonedMaterials();
            foreach (Material material in _materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
            _materials.Clear(); _originalByVariant.Clear();
            _instancesOn = instances; _meshBankOn = bank; _roomFloorGroupsOn = roomFloors;
            _batchOn = batch; _structuralOn = structural; _simpleOn = simple; _effects = effects; _active = active; _worldOn = world;
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
                _reportPending = false;
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
                    || node.GetComponent<ActorBehaviour>() != null || node.GetComponent<Canvas>() != null) continue;
                // Static mesh eligibility still rejects every prop/animated descendant in
                // TileScope. The independently owned ambience budget must nevertheless
                // discover decorative fire below those containers when it is enabled.
                if (_effects >= 100 && (node.GetComponent<ProceduralProp>() != null
                    || node.GetComponent<Animator>() != null)) continue;
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
            AdoptAmbient(node);
            ProceduralMapTile? tile = TileScope(node, out bool floor);
            if (tile == null) return;
            MeshRenderer renderer = node.GetComponent<MeshRenderer>();
            MeshFilter filter = node.GetComponent<MeshFilter>();
            if (renderer != null && filter != null && filter.sharedMesh != null)
            {
                Material[] materials = renderer.sharedMaterials;
                bool cloned = false;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null)
                    {
                        Material original = CanonicalMaterial(materials[i]);
                        // Snapshot the genuine source; only our legacy clone references
                        // belong to this owner's repair. World references have one owner.
                        cloned |= _originalByVariant.ContainsKey(materials[i]);
                        materials[i] = original;
                    }
                bool roomFloor = _roomFloorGroupsOn && _roomFloorEligible?.Invoke(filter.sharedMesh) == true;
                floor = roomFloor || ProvenFloorCore(renderer, filter.sharedMesh, tile, materials);
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
                foreach (Material material in materials) compatible &= CompatibleMaterial(material, floor || structural, roomFloor);
                if (VRLog.Wants(VRLogLevel.Debug))
                {
                    ++_meshVisits;
                    if (!floor && !structural) ++_nativeScopeRefusals;
                    else if (!compatible)
                    {
                        ++_nativeMaterialRefusals;
                        foreach (Material material in materials)
                            if (material != null && NativeWallFadeEnabled(material)) { ++_nativeWallChannelRefusals; break; }
                    }
                }
                if (compatible)
                {
                    var surface = new Surface { Renderer = renderer, Id = renderer.GetInstanceID(), Filter = filter, Tile = tile,
                        Floor = floor, RoomFloor = roomFloor, Structural = structural, Mesh = filter.sharedMesh, Original = materials };
                    _surfaces[renderer.GetInstanceID()] = surface;
                    ApplyMaterial(surface);
                    _buildPending = true;
                }
            }
        }

        private void AdoptAmbient(Transform node)
        {
            ParticleSystem system = node.GetComponent<ParticleSystem>();
            if (system == null || _ambient.ContainsKey(system.GetInstanceID())) return;
            ProceduralMapTile? tile = ScenarioEnvironmentAmbientEffects.Scope(node);
            if (tile == null) return;
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
            if (surface.RoomFloor) return; // The world/terrain owner keeps native floor materials; groups never rewrite source slots.
            if (!_simpleOn || (surface.Structural && !_structuralOn) || surface.Renderer == null || TerrainOwns(surface.Renderer)) return;
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
            using IDisposable? floorReads = _roomFloorReadPass?.Invoke();
            using IDisposable? floorMaterials = _roomFloorMaterialPass?.Invoke();
            _buildPending = false;
            _reportPending = true;
            _parts.Clear(); _unreadable = 0; _probeRefusals = 0;
            _chunkCandidates = 0; _instanceCandidates = 0; _instanceFlagRefusals = 0; _instanceMaterialRefusals = 0;
            _terrainRefusals = 0; _mpbRefusals = 0; _lightmapRefusals = 0; _lodRefusals = 0; _shadowRefusals = 0; _geometryRefusals = 0; _lightProbeFlagRefusals = 0; _reflectionFlagRefusals = 0;
            _dead.Clear();
            foreach (var pair in _surfaces) if (pair.Value.Renderer == null) _dead.Add(pair.Key);
            foreach (int id in _dead) { InvalidateBatch(id); _surfaces.Remove(id); }
            _dead.Clear();
            foreach (var pair in _ambient) if (pair.Value.System == null) _dead.Add(pair.Key);
            foreach (int id in _dead) _ambient.Remove(id);
            _dead.Clear();
            if (_instancesOn) PrepareInstances();
            if (!_batchOn && !_structuralOn && !_roomFloorGroupsOn) return;
            var groups = new Dictionary<BatchKey, List<Surface>>();
            foreach (Surface surface in _surfaces.Values)
            {
                MeshRenderer renderer = surface.Renderer;
                RecordPreparationRefusals(renderer, false);
                if (renderer == null || !surface.RoomFloor && TerrainOwns(renderer) || _instanceBySource.ContainsKey(renderer.GetInstanceID()) || _batchBySource.ContainsKey(renderer.GetInstanceID())
                    || !(surface.RoomFloor ? _roomFloorGroupsOn : surface.Floor ? _batchOn : surface.Structural && _structuralOn && surface.StructuralMaterialReady()) || surface.Tile == null || surface.Mesh == null
                    || renderer.transform.localToWorldMatrix.determinant <= 0f
                    || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff
                    || renderer.HasPropertyBlock() || !NativeGeometryCompatible(renderer) || surface.Filter.sharedMesh != surface.Mesh
                    || surface.Mesh.subMeshCount != 1 || renderer.sharedMaterials.Length != 1
                    || renderer.GetComponentInParent<LODGroup>(true) != null
                    || (renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 65534)) continue;
                Material material = renderer.sharedMaterial;
                if (material == null || surface.RoomFloor && !TryRoomFloorMaterial(material, out _)) continue;
                ++_chunkCandidates;
                if (!ChunkLightingCompatible(renderer)) { _probeRefusals++; continue; }
                // Refused lighting never needs private geometry, source-bundle hashing
                // or mesh decoding. Admit the complete render contract first.
                if (surface.RoomFloor)
                {
                    // Only completed morph endpoints can enter an immutable private group.
                    // An endpoint event retries preparation without polling all source trees.
                    if (!TryRoomFloorMesh(renderer, out Mesh preparedFloor)) continue;
                    surface.ReadableMesh = preparedFloor;
                }
                else surface.ReadableMesh = surface.Mesh.isReadable ? surface.Mesh : null;
                if (surface.ReadableMesh == null && _meshBankOn && ScenarioEnvironmentMeshBank.TryGetExact(surface.Mesh, out Mesh exact)) surface.ReadableMesh = exact;
                if (surface.ReadableMesh == null) { _unreadable++; continue; }
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

        private void PrepareInstances()
        {
            if (!SystemInfo.supportsInstancing) return;
            var groups = new Dictionary<(BatchKey, int), List<Surface>>();
            foreach (Surface surface in _surfaces.Values)
            {
                MeshRenderer r = surface.Renderer;
                RecordPreparationRefusals(r, true);
                if (surface.RoomFloor || r == null || TerrainOwns(r) || _batchBySource.ContainsKey(surface.Id) || _instanceBySource.ContainsKey(surface.Id)
                    || surface.Mesh == null || surface.Filter == null || surface.Filter.sharedMesh != surface.Mesh
                    || !r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff || r.HasPropertyBlock()
                    || surface.Tile == null) continue;
                ++_instanceCandidates;
                if (!SupportedInstanceFlags(r)) { ++_instanceFlagRefusals; continue; }
                Material[] materials = r.sharedMaterials;
                if (materials.Length != surface.Mesh.subMeshCount || materials.Length == 0) continue;
                bool supported = true;
                foreach (Material material in materials)
                    supported &= material != null && CompatibleMaterial(material, surface.Floor || surface.Structural)
                        && SupportsInstanceMaterial(material);
                if (!supported) { ++_instanceMaterialRefusals; continue; }
                var key = (new BatchKey(surface, materials[0]), surface.Mesh.GetInstanceID());
                if (!groups.TryGetValue(key, out List<Surface> group)) groups.Add(key, group = new List<Surface>());
                group.Add(surface);
            }
            foreach (List<Surface> group in groups.Values)
                for (int start = 0; start < group.Count; start += MaxBatchMembers)
                {
                    int count = Math.Min(MaxBatchMembers, group.Count - start);
                    if (count < 2) continue;
                    var batch = new InstanceBatch { Mesh = group[start].Mesh, Materials = group[start].Renderer.sharedMaterials };
                    for (int i = start; i < start + count; i++)
                    {
                        Surface candidate = group[i];
                        bool overlaps = false;
                        foreach (Surface admitted in batch.Sources) overlaps |= admitted.Renderer.bounds.Intersects(candidate.Renderer.bounds);
                        if (!overlaps && SameMaterials(candidate.Renderer.sharedMaterials, batch.Materials)) batch.Sources.Add(candidate);
                    }
                    if (batch.Sources.Count < 2) continue;
                    batch.DrawMaterials = new Material[batch.Materials.Length];
                    for (int sub = 0; sub < batch.Materials.Length; sub++)
                        batch.DrawMaterials[sub] = batch.Materials[sub].enableInstancing ? batch.Materials[sub]
                            : new Material(batch.Materials[sub]) { name = "GloomhavenVR.EnvironmentInstanceMaterial", enableInstancing = true };
                    _instances.Add(batch);
                    foreach (Surface source in batch.Sources) _instanceBySource.Add(source.Id, batch);
                }
        }

        private void QueuePortion(List<Surface> members)
        { if (members.Count > 1) _parts.Enqueue(new List<Surface>(members)); }

        private void RecordPreparationRefusals(MeshRenderer renderer, bool instances)
        {
            if (!VRLog.Wants(VRLogLevel.Debug) || renderer == null) return;
            // The paths can inspect the same source and several flags can veto
            // one source. These bounded preparation observations intentionally
            // overlap; they are never distinct scene counts or FPS measurements.
            if (TerrainOwns(renderer)) ++_terrainRefusals;
            if (renderer.HasPropertyBlock()) ++_mpbRefusals;
            if (renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 65534) ++_lightmapRefusals;
            if (renderer.GetComponentInParent<LODGroup>(true) != null) ++_lodRefusals;
            if (!NativeGeometryCompatible(renderer)) ++_geometryRefusals;
            if (instances && (renderer.shadowCastingMode != ShadowCastingMode.Off || renderer.receiveShadows)) ++_shadowRefusals;
            if (renderer.lightProbeUsage != LightProbeUsage.Off)
            {
                LightProbes probes = LightmapSettings.lightProbes;
                if (instances || renderer.lightProbeUsage != LightProbeUsage.BlendProbes
                    || renderer.lightProbeProxyVolumeOverride != null || (probes != null && probes.count > 0)) ++_lightProbeFlagRefusals;
            }
            if (renderer.reflectionProbeUsage != ReflectionProbeUsage.Off && (instances || HasLocalReflectionProbes)) ++_reflectionFlagRefusals;
        }

        private void DrainBatches(int budget)
        {
            using IDisposable? floorReads = _roomFloorReadPass?.Invoke();
            using IDisposable? floorMaterials = _roomFloorMaterialPass?.Invoke();
            while (budget-- > 0 && _parts.Count > 0)
            {
                List<Surface> members = _parts.Dequeue();
                // A native hide, material load or room change may occur after preparation.
                // Revalidate admission rather than publishing a stale substitute.
                members.RemoveAll(s => s.Renderer == null || s.Filter == null || s.Tile == null
                    || s.Mesh == null || s.ReadableMesh == null || !s.ReadableMesh.isReadable || !s.RoomFloor && TerrainOwns(s.Renderer) || s.Filter.sharedMesh != s.Mesh
                    || s.Renderer.forceRenderingOff || !s.Renderer.enabled
                    || !s.Renderer.gameObject.activeInHierarchy || s.Renderer.HasPropertyBlock() || !NativeGeometryCompatible(s.Renderer)
                    || s.Renderer.sharedMaterials.Length != 1
                    || !(s.RoomFloor ? _roomFloorGroupsOn : s.Floor ? _batchOn : s.Structural && _structuralOn && s.StructuralMaterialReady())
                    || !CompatibleMaterial(s.Renderer.sharedMaterial, s.Floor || s.Structural, s.RoomFloor)
                    || !ChunkLightingCompatible(s.Renderer)
                    || s.Renderer.transform.localToWorldMatrix.determinant <= 0f
                    || _batchBySource.ContainsKey(s.Renderer.GetInstanceID())
                    || s.RoomFloor && (!TryRoomFloorMesh(s.Renderer, out Mesh floorMesh) || floorMesh != s.ReadableMesh));
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
                if (surface.Renderer == null) continue;
                // Floor groups have no legacy material binding and own their endpoint
                // handoff explicitly. Batch.Validate reads their current source contract.
                if (surface.RoomFloor) continue;
                if (TerrainOwns(surface.Renderer))
                {
                    if (!surface.TerrainOwned) { InvalidateBatch(surface.Id); surface.RestoreMaterial(); surface.TerrainOwned = true; }
                    continue;
                }
                if (surface.TerrainOwned)
                {
                    surface.TerrainOwned = false;
                    if (surface.Filter != null && surface.Filter.sharedMesh == surface.Mesh
                        && SameMaterials(surface.Renderer.sharedMaterials, surface.Original)) ApplyMaterial(surface);
                    _buildPending = true;
                }
                if (surface.Applied == null) continue;
                bool compatible = !surface.Renderer.HasPropertyBlock() && (surface.Floor || surface.Structural);
                foreach (Material original in surface.Original)
                {
                    if (original == null) { compatible = false; continue; }
                    bool share = PerfConfig.SharedEnvironmentMaterialReadsOn;
                    bool materialCompatible;
                    if (!share || !_preCullMaterialVerdicts.TryGetValue(original, out materialCompatible))
                    {
                        materialCompatible = CompatibleMaterial(original, true);
                        if (share) _preCullMaterialVerdicts.Add(original, materialCompatible);
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
            if (_instanceBySource.TryGetValue(sourceId, out InstanceBatch instances))
            {
                foreach (Surface source in instances.Sources) _instanceBySource.Remove(source.Id);
                instances.Dispose(); _instances.Remove(instances); _buildPending = true;
            }
            if (!_batchBySource.TryGetValue(sourceId, out Batch batch)) return;
            foreach (Surface source in batch.Sources) _batchBySource.Remove(source.Id);
            batch.Dispose(); _batches.Remove(batch); _buildPending = true;
        }

        private void CreateBatch(List<Surface> members)
        {
            if (members.Count < 2) return;
            Surface first = members[0];
            Material nativeMaterial = first.Renderer.sharedMaterial;
            Material drawMaterial = nativeMaterial;
            if (first.RoomFloor && !TryRoomFloorMaterial(nativeMaterial, out drawMaterial)) return;
            var batch = new Batch { Material = drawMaterial, NativeMaterial = nativeMaterial, RoomFloor = first.RoomFloor,
                RoomTile = first.RoomFloor ? first.Tile.transform : null };
            var child = new GameObject("GloomhavenVR.StaticScenarioChunk");
            child.layer = first.Renderer.gameObject.layer;
            // Broader floor substitutes live outside every native content cloning root.
            // Native GameObjects, mesh pointers, colliders and static-batch metadata stay intact.
            child.transform.SetParent(first.RoomFloor ? transform : first.Tile.transform, false);
            batch.Object = child;
            try
            {
                if (!batch.FollowRoomTile()) { batch.Dispose(); return; }
                var combines = new CombineInstance[members.Count];
                for (int i = 0; i < members.Count; i++)
                {
                    Surface surface = members[i];
                    Matrix4x4 matrix;
                    if (first.RoomFloor)
                    {
                        if (!Batch.TryTileMatrix(surface, batch.RoomTile, out matrix)) { batch.Dispose(); return; }
                    }
                    else matrix = child.transform.worldToLocalMatrix * surface.Renderer.transform.localToWorldMatrix;
                    combines[i] = new CombineInstance { mesh = surface.ReadableMesh, subMeshIndex = 0, transform = matrix };
                    batch.Sources.Add(surface); batch.Matrices.Add(matrix);
                }
                batch.Mesh = new Mesh { name = "GloomhavenVR.StaticScenarioChunkMesh" };
                batch.Mesh.CombineMeshes(combines, true, true, false);
                if (first.RoomFloor)
                {
                    foreach (Surface source in members) batch.NativeFloorTriangles += source.Mesh.GetIndexCount(0) / 3;
                    batch.SubmittedFloorTriangles = batch.Mesh.GetIndexCount(0) / 3;
                }
                child.AddComponent<MeshFilter>().sharedMesh = batch.Mesh;
                batch.Renderer = child.AddComponent<MeshRenderer>();
                batch.Renderer.enabled = false;
                batch.Renderer.sharedMaterial = batch.Material;
                if (first.RoomFloor)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetFloat("_GHVRTerrainNeverFade", 1f);
                    block.SetFloat("_GHVRWorldNeverFade", 1f);
                    batch.Renderer.SetPropertyBlock(block);
                }
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
            if (!isActiveAndEnabled || !_active || (_surfaces.Count == 0 && _batches.Count == 0 && _ambient.Count == 0)) return;
            // A native visibility callback can run after LateUpdate. Validation is idempotent
            // and precedes each camera's culling, including both eyes in MultiPass.
            using var scope = PerfMonitor.Scope("EnvironmentBudget.PreCull");
            try
            {
                RetireChangedNativeMaterials();
                _renderDepth++; _renderCameras.Add(camera);
                LightProbes probes = LightmapSettings.lightProbes;
                _cameraProbeSources.Add(probes); _cameraProbeCounts.Add(probes != null ? probes.count : 0);
                _cameraDepthModes.Add(camera.depthTextureMode); _cameraPaths.Add(camera.renderingPath);
                // DrawRenderer command buffers target exact native Renderer identities.
                // Keep these sources available with native flags for command-buffer
                // consumers; such cameras use originals, never a substitute mask.
                bool foreignCommands = camera != null && HasNativeCommandBufferConsumers(camera);
                _cameraHadForeignCommands.Add(foreignCommands);
                PerfMonitor.Count("Environment.NativeBufferFallback", foreignCommands && (_batches.Count > 0 || _instances.Count > 0) ? 1 : 0);
                if (camera != null && camera.commandBufferCount > 0 && foreignCommands)
                    foreach (Batch batch in _batches) { batch.Unmask(); }
                else
                {
                    using IDisposable? floorMaterials = _roomFloorMaterialPass?.Invoke();
                    using IDisposable? floorReads = _roomFloorReadPass?.Invoke();
                    ValidateBatches();
                }
                foreach (InstanceBatch batch in _instances) if (camera != null) batch.Submit(camera, foreignCommands);
                foreach (Ambient ambient in _ambient.Values) ambient.Mask(Hide(ambient.Hash));
            }
            catch (Exception error) { StopAfterFailure(error); }
        }
        private void HandlePostRender(Camera camera)
        {
            if (_renderDepth <= 0) return;
            if (_renderCameras.Count == 0 || _renderCameras[_renderCameras.Count - 1] != camera) { RecoverRenderLeases(); return; }
            ReportCameraDrawCounts();
            _renderCameras.RemoveAt(_renderCameras.Count - 1);
            _cameraProbeSources.RemoveAt(_cameraProbeSources.Count - 1); _cameraProbeCounts.RemoveAt(_cameraProbeCounts.Count - 1);
            _cameraDepthModes.RemoveAt(_cameraDepthModes.Count - 1); _cameraPaths.RemoveAt(_cameraPaths.Count - 1);
            _cameraHadForeignCommands.RemoveAt(_cameraHadForeignCommands.Count - 1);
            --_renderDepth;
            foreach (InstanceBatch batch in _instances) batch.EndCamera(camera);
            if (_renderDepth > 0)
            {
                Camera outer = _renderCameras[_renderCameras.Count - 1];
                foreach (InstanceBatch batch in _instances) batch.RestoreOuterMask(outer);
                return;
            }
            foreach (Batch batch in _batches) batch.Unmask();
            foreach (Ambient ambient in _ambient.Values) ambient.Unmask();
        }
        internal void FinishCameraPreCull(Camera camera)
        {
            if (_renderDepth <= 0 || _renderCameras.Count == 0 || _renderCameras[_renderCameras.Count - 1] != camera) return;
            using var scope = PerfMonitor.Scope("EnvironmentBudget.FinalPreCull");
            // A raw LightmapSettings assignment does not notify Unity2021's probe
            // events synchronously. Compare the real global source and count only
            // after every pre-cull listener, but still before native culling. A
            // stable camera costs no second per-renderer or per-material sweep.
            LightProbes probes = LightmapSettings.lightProbes;
            int last = _cameraProbeSources.Count - 1;
            bool foreign = camera.commandBufferCount > 0 && HasNativeCommandBufferConsumers(camera);
            bool changedPath = _cameraDepthModes[last] != camera.depthTextureMode || _cameraPaths[last] != camera.renderingPath;
            bool changedLighting = _cameraProbeSources[last] != probes || _cameraProbeCounts[last] != (probes != null ? probes.count : 0);
            foreach (Batch batch in _batches) batch.FinishRoomPose();
            foreach (Batch batch in _batches) if (batch.HasLateLightingWrite()) { changedLighting = true; break; }
            if (!foreign && !changedPath && !changedLighting) return;
            if (foreign && !_cameraHadForeignCommands[last]) PerfMonitor.Count("Environment.NativeBufferFallback");
            if (changedPath) PerfMonitor.Count("Environment.CameraPathFallback");
            if (changedLighting) PerfMonitor.Count("Environment.LightingFallback");
            RecoverRenderLeases();
        }
        private void ReportCameraDrawCounts()
        {
            if (!PerfMonitor.StepsActive) return;
            int chunkSources = 0, chunkGroups = 0, roomFloorSources = 0, roomFloorGroups = 0, instanceSources = 0, instanceGroups = 0;
            long nativeFloorTriangles = 0, submittedFloorTriangles = 0;
            foreach (Batch batch in _batches)
            {
                int count = batch.MaskedSourceCount;
                chunkSources += count; if (count > 0) ++chunkGroups;
                if (batch.RoomFloor && count > 0)
                {
                    roomFloorSources += count; ++roomFloorGroups;
                    nativeFloorTriangles += batch.NativeFloorTriangles;
                    submittedFloorTriangles += batch.SubmittedFloorTriangles;
                }
            }
            foreach (InstanceBatch batch in _instances)
            {
                int count = batch.MaskedSourceCount;
                instanceSources += count; if (count > 0) ++instanceGroups;
            }
            // Count completed camera leases before restitution, after any late native
            // writes have revoked obsolete geometry. These are per-camera sums, not
            // prepared membership, scene renderer totals, draw-call counts or FPS.
            PerfMonitor.Count("Environment.RenderCameras");
            PerfMonitor.Count("Environment.ChunkSources", chunkSources);
            PerfMonitor.Count("Environment.ChunkGroups", chunkGroups);
            if (VRLog.Wants(VRLogLevel.Debug))
            {
                PerfMonitor.Count("Environment.RoomFloorSources", roomFloorSources);
                PerfMonitor.Count("Environment.RoomFloorGroups", roomFloorGroups);
                PerfMonitor.Count("Environment.RoomFloorOriginalTriangles", nativeFloorTriangles);
                PerfMonitor.Count("Environment.RoomFloorSubmittedTriangles", submittedFloorTriangles);
            }
            PerfMonitor.Count("Environment.InstanceSources", instanceSources);
            PerfMonitor.Count("Environment.InstanceGroups", instanceGroups);
        }
        private void ValidateBatches()
        {
            foreach (Batch batch in _batches)
            {
                batch.Validate();
                if (batch.LightingRefused) PerfMonitor.Count("Environment.LightingFallback");
            }
        }
        internal void WorldMaterialsDisposing()
        {
            ReleaseBatches();
            _buildPending = true;
        }
        private void ReleaseBatches()
        {
            foreach (InstanceBatch batch in _instances) batch.Dispose();
            _instances.Clear(); _instanceBySource.Clear(); _renderCameras.Clear(); _cameraProbeSources.Clear(); _cameraProbeCounts.Clear(); _cameraDepthModes.Clear(); _cameraPaths.Clear(); _cameraHadForeignCommands.Clear();
            foreach (Batch batch in _batches) batch.Dispose();
            _batches.Clear(); _batchBySource.Clear(); _parts.Clear(); _renderDepth = 0; _probeRefusals = 0;
        }
        private void Report()
        {
            if (!VRLog.Wants(VRLogLevel.Debug) || _reports >= 32) return;
            ++_reports;
            int batched = 0;
            foreach (Batch batch in _batches) batched += batch.Sources.Count;
            VRLog.Debug(Scope, "Scenario environment budget: " + _surfaces.Count + " compatible static surfaces; "
                + batched + " source renderers / " + _batches.Count + " chunks; " + _unreadable
                + " unreadable originals retained; " + _probeRefusals + " probe-enabled originals retained at chunk preparation; "
                + _materials.Count + " simpler materials; "
                + (_structuralOn ? "audited structural chunks on; " : "structural chunks off; ")
                + _instances.Count + " explicit instance groups; " + (_meshBankOn ? "verified private mesh bank on; " : "private mesh bank off; ")
                + _ambient.Count + " identified ambient solvers; effects " + _effects + "%. Actual FPS remains a hardware measurement.");
            VRLog.Debug(Scope, "Scenario environment preparation: chunk candidates=" + _chunkCandidates
                + ", instance candidates=" + _instanceCandidates + ", instance native-flag refusals=" + _instanceFlagRefusals
                + ", instance material refusals=" + _instanceMaterialRefusals
                + ". Prepared groups are membership; completed camera counters report surviving source masks.");
            VRLog.Debug(Scope, "Scenario environment discovery: mesh visits=" + _meshVisits
                + ", native scope refusals=" + _nativeScopeRefusals + ", native material refusals=" + _nativeMaterialRefusals
                + ", live wall-channel refusals=" + _nativeWallChannelRefusals
                + ". Discovery counts visits since the previous report, not distinct renderers.");
            VRLog.Debug(Scope, "Scenario environment native refusal observations: terrain=" + _terrainRefusals
                + ", MPB=" + _mpbRefusals + ", lightmap=" + _lightmapRefusals + ", LOD=" + _lodRefusals
                + ", shadows(instances)=" + _shadowRefusals + ", source geometry=" + _geometryRefusals
                + ", light probes=" + _lightProbeFlagRefusals + ", local reflections=" + _reflectionFlagRefusals
                + ". Flags may overlap and sources may occur in both grouping paths.");
            _meshVisits = 0; _nativeScopeRefusals = 0; _nativeMaterialRefusals = 0; _nativeWallChannelRefusals = 0;
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
            _reportPending = false;
            _meshVisits = 0; _nativeScopeRefusals = 0; _nativeMaterialRefusals = 0; _nativeWallChannelRefusals = 0;
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
// Loading first hides the original renderer, before the asynchronous completion
// callback. A load begun during pre-cull must revoke queued substitutes immediately.
[HarmonyPatch(typeof(MaterialLoaderData), nameof(MaterialLoaderData.LoadMaterials))]
internal static class MaterialLoaderData_Load_EnvironmentBudgetPatch
{
    private static void Prefix(MaterialLoaderData __instance)
    {
        Renderer? renderer = __instance.Renderer;
        if (renderer == null) return;
        ScenarioEnvironmentBudget.BeforeNativeContentChange();
        ScenarioEnvironmentBudget.BeforeNativeRendererWrite(renderer);
    }
}
[HarmonyPatch(typeof(MaterialLoaderData), "CheckAllMaterialLoaded")]
internal static class MaterialLoaderData_Ready_EnvironmentBudgetPatch
{
    private static void Prefix(MaterialLoaderData __instance)
    { try { if (__instance.Renderer != null) { ScenarioEnvironmentBudget.BeforeNativeContentChange(); ScenarioEnvironmentBudget.BeforeNativeRendererWrite(__instance.Renderer); } } catch (Exception e) { ScenarioEnvironmentBudget.StopAfterFailure(e); } }
    private static void Postfix(MaterialLoaderData __instance)
    { try { if (__instance.Renderer != null) ScenarioEnvironmentBudget.MaterialReady(__instance.Renderer); } catch (Exception e) { ScenarioEnvironmentBudget.StopAfterFailure(e); } }
}
[HarmonyPatch(typeof(SceneController), nameof(SceneController.DisableLoadingScreen))]
internal static class SceneController_Loaded_EnvironmentBudgetPatch
{
    private static void Prefix()
    { try { ScenarioEnvironmentBudget.BeforeLoadingComplete(); } catch (Exception e) { ScenarioEnvironmentBudget.StopAfterFailure(e); } }
}

// Apparance may clone in-scene sources while a camera lease is active. Restore all
// original draw flags before Instantiate, rather than trying to heal an already masked clone.
[HarmonyPatch(typeof(ApparanceEntity), nameof(ApparanceEntity.CreateInstance))]
internal static class ApparanceEntity_EnvironmentBudgetPatch
{
    private static void Prefix() => ScenarioEnvironmentBudget.BeforeNativeContentChange();
    private static void Postfix(GameObject __result)
    { try { if (__result != null) ScenarioEnvironmentBudget.Placed(__result); } catch (Exception error) { ScenarioEnvironmentBudget.StopAfterFailure(error); } }
}
