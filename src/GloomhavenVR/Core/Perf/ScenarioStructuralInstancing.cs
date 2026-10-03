using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>Optional native GPU instancing for repeated, unbatched scenario surfaces.
/// Original compiled Amp high/low N_MRAO and WallFade shaders carry INSTANCING_ON vertex
/// variants (native bundle proof in FRAME-615-WALLS.md). This enables only the original
/// material flag; meshes, transforms, collider identity, layers, per-renderer lighting and
/// visibility remain native. Per-renderer wall fade blocks keep Unity's ordinary unbatched
/// fallback; no shader, effect property or MaterialPropertyBlock is replaced. Existing floor
/// chunks retain priority. Nothing is merged across doors or visibility/culling boundaries.
///
/// Discovery is event-driven and sliced, with final loading-screen preparation. No scene-wide
/// renderer census runs during play. Unsupported hardware/shaders and unknown identities keep
/// their original rendering. Only owned false-to-true flags are restored; observed foreign
/// changes release ownership until the setting next changes. Savings require repeated visible
/// meshes sharing the same material and compatible native passes, and are not promised by an
/// admitted-renderer count. The next headset run measures actual submission/frame-time benefit.
/// </summary>
internal static class ScenarioStructuralInstancing
{
    private static Driver? _driver;
    private static Func<bool>? _enabled;
    private static bool _failed;

    internal static void Install(GameObject host, Func<bool> enabled)
    {
        if (_driver != null) return;
        _enabled = enabled;
        _driver = host.AddComponent<Driver>();
        try
        {
            VRSession.Harmony?.PatchAll(typeof(StructuralInstancing_PlacedPatch));
            VRSession.Harmony?.PatchAll(typeof(StructuralInstancing_ShownPatch));
            VRSession.Harmony?.PatchAll(typeof(StructuralInstancing_MaterialPatch));
            VRSession.Harmony?.PatchAll(typeof(StructuralInstancing_LoadedPatch));
        }
        catch (Exception error) { Fail(error); }
    }

    internal static void Placed(GameObject root)
    {
        if (!_failed && root != null) _driver?.Queue(root.transform);
    }
    internal static void MaterialReady(Renderer renderer)
    {
        if (!_failed && renderer != null) _driver?.Queue(renderer.transform);
    }
    internal static void BeforeLoadingComplete()
    {
        if (!_failed) _driver?.FinishLoading();
    }
    internal static void Shutdown()
    {
        if (_driver != null) { _driver.Restore(); UnityEngine.Object.Destroy(_driver); }
        _driver = null; _enabled = null; _failed = false;
    }
    internal static void Fail(Exception error)
    {
        if (_failed) return;
        _failed = true;
        if (_driver != null) { _driver.enabled = false; _driver.Restore(); }
        VRLog.Note("Perf", "Structural instancing: original rendering retained after "
            + error.GetType().Name + ": " + error.Message);
    }

    private static bool ProvenShader(Material material)
    {
        if (material == null || material.shader == null || !material.shader.isSupported
            || material.renderQueue > 2500) return false;
        string name = material.shader.name;
        return name == "Amp_Basic_N_MRAO" || name == "Amp_Low/Amp_Basic_N_MRAO_Low"
            || name == "Amp_Basic_WallFade" || name == "Amp_Low/Amp_Basic_WallFade_Low";
    }

    private static bool NativeScenarioSurface(MeshRenderer renderer)
    {
        if (renderer == null || renderer.isPartOfStaticBatch || !renderer.enabled
            || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) return false;
        bool generated = false, tile = false;
        for (Transform? node = renderer.transform; node != null; node = node.parent)
        {
            if (node.name.StartsWith("GloomhavenVR", StringComparison.Ordinal)
                || node.name == "Preview" || node.GetComponent<Canvas>() != null
                || node.GetComponent<ActorBehaviour>() != null || node.GetComponent<CInteractable>() != null
                || node.GetComponent<ProceduralProp>() != null || node.GetComponent<ProceduralDoorway>() != null
                || node.GetComponent<UnityGameEditorDoorProp>() != null || node.GetComponent<Animator>() != null
                || node.GetComponent<Rigidbody>() != null) return false;
            generated |= node.name == "Generated Content";
            tile |= node.GetComponent<ProceduralMapTile>() != null;
            if (node.GetComponent<ProceduralScenario>() != null) return generated && tile;
        }
        if (!generated || !tile) return false;
        Scene scene = renderer.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded) return false;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.GetComponent<ProceduralScenario>() != null) return true;
        return false;
    }

    private sealed class Group
    {
        internal Material Material = null!;
        internal readonly HashSet<MeshRenderer> Members = new();
    }
    private sealed class Owner
    {
        internal Material Material = null!;
        internal bool Original;
        internal Shader Shader = null!;
    }
    private sealed class Driver : MonoBehaviour
    {
        private const int NodesPerFrame = 128;
        private readonly Queue<Transform> _pending = new();
        private readonly HashSet<int> _queued = new();
        private readonly Dictionary<(Mesh Mesh, Material Material), Group> _groups = new();
        private readonly Dictionary<Material, Owner> _owners = new();
        private readonly HashSet<Material> _foreign = new();
        private readonly List<ProceduralMapTile> _tiles = new();
        private bool _on, _dirty;
        private float _nextReport;
        private int _reports;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += Loaded;
            SceneManager.sceneUnloaded += Unloaded;
        }
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneUnloaded -= Unloaded;
            Restore();
        }
        private void Loaded(Scene scene, LoadSceneMode mode) { if (_on) Seed(); }
        private void Unloaded(Scene scene) { Restore(); _on = false; }
        private void Seed()
        {
            SceneRegistry.MapTiles.Collect(_tiles);
            foreach (ProceduralMapTile tile in _tiles) if (tile != null) Queue(tile.transform);
            _tiles.Clear();
        }
        internal void Queue(Transform root)
        {
            if (!VRSession.IsRunning || _enabled == null || !_enabled()) return;
            if (_queued.Add(root.GetInstanceID())) _pending.Enqueue(root);
        }
        private void Settings()
        {
            bool requested = VRSession.IsRunning && SystemInfo.supportsInstancing
                && _enabled != null && _enabled();
            if (requested == _on) return;
            Restore(); _on = requested;
            if (_on) Seed();
        }
        private void Walk(int budget)
        {
            while (budget-- > 0 && _pending.Count > 0)
            {
                Transform node = _pending.Dequeue();
                if (node == null) continue;
                _queued.Remove(node.GetInstanceID());
                for (int child = 0; child < node.childCount; child++) Queue(node.GetChild(child));
                MeshRenderer renderer = node.GetComponent<MeshRenderer>();
                if (!NativeScenarioSurface(renderer)) continue;
                MeshFilter filter = node.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0) continue;
                Material[] materials = renderer.sharedMaterials;
                // A complete one-submesh native surface shares its exact original material.
                // Multi-material groups and newly written non-instanced property blocks are
                // left to Unity's original path; native door/room behavior is not patched.
                if (materials.Length != 1 || filter.sharedMesh.subMeshCount != 1
                    || !ProvenShader(materials[0]) || renderer.HasPropertyBlock()) continue;
                var key = (filter.sharedMesh, materials[0]);
                if (!_groups.TryGetValue(key, out Group? group))
                    _groups.Add(key, group = new Group { Material = materials[0] });
                if (group.Members.Add(renderer)) _dirty = true;
            }
        }
        private void Apply()
        {
            if (!_dirty || _pending.Count > 0) return;
            _dirty = false;
            foreach (Group group in _groups.Values)
            {
                group.Members.RemoveWhere(renderer => !NativeScenarioSurface(renderer)
                    || renderer.GetComponent<MeshFilter>() == null
                    || renderer.sharedMaterial != group.Material || renderer.HasPropertyBlock());
                if (group.Material == null || _foreign.Contains(group.Material)
                    || group.Members.Count < 2 || group.Material.enableInstancing
                    || !ProvenShader(group.Material)) continue;
                var owner = new Owner { Material = group.Material, Original = group.Material.enableInstancing, Shader = group.Material.shader };
                group.Material.enableInstancing = true;
                _owners[group.Material] = owner;
            }
            if (VRLog.WantsDebug && _reports < 4 && Time.unscaledTime >= _nextReport)
            {
                _reports++; _nextReport = Time.unscaledTime + 10f;
                int members = 0; foreach (Group group in _groups.Values) members += group.Members.Count;
                VRLog.Debug("Perf", "Structural instancing: " + _groups.Count + " exact native mesh/material groups, "
                    + members + " admitted source renderers, " + _owners.Count
                    + " owned material flags; actual eye draw calls and savings are unmeasured.");
            }
        }
        private void ObserveForeign()
        {
            // A bounded material population, no scene or renderer walks. If another owner
            // resets our flag, retain that decision instead of fighting it every frame.
            foreach (Owner owner in _owners.Values)
                if (owner.Material != null && (!owner.Material.enableInstancing || owner.Material.shader != owner.Shader))
                    _foreign.Add(owner.Material);
        }
        private void Update()
        {
            if (_failed) return;
            try
            {
                using var scope = PerfMonitor.Scope("EnvironmentBudget.Instancing");
                Settings();
                if (!_on) return;
                ObserveForeign();
                SceneController controller = SceneController.Instance;
                bool loading = controller != null && (controller.IsLoading || controller.ScenarioIsLoading);
                Walk(loading ? 4096 : NodesPerFrame);
                Apply();
            }
            catch (Exception error) { Fail(error); }
        }
        internal void FinishLoading()
        {
            try { Settings(); if (!_on) return; Seed(); Walk(int.MaxValue); ObserveForeign(); Apply(); }
            catch (Exception error) { Fail(error); }
        }
        internal void Restore()
        {
            foreach (Owner owner in _owners.Values)
                if (owner.Material != null && !_foreign.Contains(owner.Material)
                    && owner.Material.shader == owner.Shader && owner.Material.enableInstancing) owner.Material.enableInstancing = owner.Original;
            _owners.Clear(); _groups.Clear(); _pending.Clear(); _queued.Clear(); _foreign.Clear();
            _dirty = false; _reports = 0; _nextReport = 0f;
        }
    }
}

[HarmonyPatch(typeof(ProceduralBase), nameof(ProceduralBase.NotifyContentPlacementComplete))]
internal static class StructuralInstancing_PlacedPatch
{
    private static void Postfix(ProceduralBase __instance)
    { try { ScenarioStructuralInstancing.Placed(__instance.gameObject); } catch (Exception error) { ScenarioStructuralInstancing.Fail(error); } }
}
[HarmonyPatch(typeof(ProceduralMapTile), nameof(ProceduralMapTile.ShowContent))]
internal static class StructuralInstancing_ShownPatch
{
    private static void Postfix(GameObject o)
    { try { ScenarioStructuralInstancing.Placed(o); } catch (Exception error) { ScenarioStructuralInstancing.Fail(error); } }
}
[HarmonyPatch(typeof(MaterialLoaderData), "CheckAllMaterialLoaded")]
internal static class StructuralInstancing_MaterialPatch
{
    private static void Postfix(MaterialLoaderData __instance)
    { try { if (__instance.Renderer != null) ScenarioStructuralInstancing.MaterialReady(__instance.Renderer); } catch (Exception error) { ScenarioStructuralInstancing.Fail(error); } }
}
[HarmonyPatch(typeof(SceneController), nameof(SceneController.DisableLoadingScreen))]
internal static class StructuralInstancing_LoadedPatch
{
    private static void Prefix()
    { try { ScenarioStructuralInstancing.BeforeLoadingComplete(); } catch (Exception error) { ScenarioStructuralInstancing.Fail(error); } }
}
