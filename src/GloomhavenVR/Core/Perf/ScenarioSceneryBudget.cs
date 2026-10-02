using System;
using System.Collections.Generic;
using GloomhavenVR.Board.FigureGrab;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Reversible local scenario-detail compromise authorised by the maintainer on 2026-10-01.
/// Build 600 hardware rejected nearly every real grass leaf and discovered the room before
/// Apparance had populated it: its 0% setting masked only 27 of 6,560 active renderers. This
/// revision classifies actual generated decorative mesh branches, including wall-side vegetation
/// and LOD descendants, and repeats discovery on the native loading-complete edge. Structural
/// floor plates, walls, pillars, doors, actors and native gameplay props retain their renderers.
/// Incidental decoration colliders are not gameplay identity and remain untouched.
///
/// Only owned false-to-true Renderer.forceRenderingOff writes are restored. Native enabled state,
/// colliders, components, materials, property blocks, room reveal and multiplayer state are never
/// written. Grass, vegetation and loose-decoration budgets are independent: Build 601's
/// decoration-zero cap otherwise made every grass slider movement ineffective. All three at
/// 100 restore the original rendering on PC and Steam Frame. Dedicated foliage leaves beneath
/// a structural asset remain optional; its solid or mixed-material core is never optional.
/// Source and Unity hierarchy tests establish admission/restoration, not headset FPS improvement.
/// </summary>
internal static class ScenarioSceneryBudget
{
    private const string Scope = "Perf";
    private const string GrassShader = "Amp_Basic_Foliage";
    private const int NodesPerFrame = 96;
    private const int LoadingNodesPerFrame = 2048;
    private const float LoadingWorkSeconds = .004f;
    private const int RetunesPerFrame = 96;
    private const int AncestryChecksPerFrame = 64;
    private const int NamedDebugCap = 8;

    private static Driver? _driver;
    private static bool _colliderFactsActive;
    private static readonly Dictionary<Transform, ColliderFacts> ColliderReadFacts = new();

    private readonly struct ColliderFacts
    {
        internal readonly Collider[] Colliders;
        internal readonly bool Represented;
        internal ColliderFacts(Collider[] colliders, bool represented)
        { Colliders = colliders; Represented = represented; }
    }

    internal static void Install(GameObject root)
    {
        if (_driver != null)
            return;
        _driver = root.AddComponent<Driver>();
        try
        {
            if (VRSession.Harmony != null)
            {
                VRSession.Harmony.PatchAll(typeof(ProceduralBase_ContentPlaced_SceneryBudgetPatch));
                VRSession.Harmony.PatchAll(typeof(ProceduralMapTile_ShowContent_SceneryBudgetPatch));
                VRSession.Harmony.PatchAll(typeof(MaterialLoaderData_Ready_SceneryBudgetPatch));
            }
            else
                VRLog.Note(Scope, "Scenario scenery budget: placement hooks unavailable; existing "
                                  + "tiles are still sampled at scene entry and setting changes.");
        }
        catch (Exception e)
        {
            VRLog.Note(Scope, $"Scenario scenery budget: placement hook unavailable ({e.Message}); "
                              + "existing tiles are still sampled at scene entry and setting changes.");
        }
    }

    internal static void Shutdown()
    {
        if (_driver == null)
            return;
        _driver.RestoreAll();
        UnityEngine.Object.Destroy(_driver);
        _driver = null;
    }

    /// <summary>Patch callbacks only enqueue; no content walk runs inside native placement.</summary>
    internal static void ContentPlaced(ProceduralBase entity)
    {
        if (entity != null)
            _driver?.QueueTile(entity is ProceduralMapTile tile ? tile : TileAncestor(entity.transform));
    }

    internal static void ContentShown(GameObject root)
    {
        if (root != null)
            _driver?.QueueTile(TileAncestor(root.transform));
    }

    /// <summary>Addressables finishes after native placement and can replace placeholder shaders.
    /// This notification enqueues only the assigned leaf; the native callback never walks content.
    /// A newly ready structural base also queues its tile so previously unrepresented collider
    /// composites can be reconsidered once their retained floor becomes visible.
    /// </summary>
    internal static void MaterialsReady(Renderer renderer)
    {
        if (renderer is MeshRenderer mesh && mesh.enabled)
            _driver?.QueueRenderer(mesh);
    }

    private static ProceduralMapTile? TileAncestor(Transform leaf)
    {
        for (Transform? node = leaf; node != null; node = node.parent)
        {
            ProceduralMapTile tile = node.GetComponent<ProceduralMapTile>();
            if (tile != null)
                return tile;
        }
        return null;
    }

    private enum Verdict : byte
    {
        Eligible, Name, Generator, Ancestry, Structural, Effect, Geometry,
    }

    private enum Kind : byte { None, Grass, Vegetation, Dressing }

    private static bool ShouldHide(uint hash, int densityPercent) =>
        densityPercent < 100 && hash % 100u >= (uint)densityPercent;

    private sealed class Record
    {
        internal MeshRenderer Renderer = null!;
        internal Transform[] Chain = null!;
        internal int Id;
        internal uint Hash;
        internal bool Owned;
        internal bool Invalidated;
        internal Kind Kind;
    }

    /// <summary>Own only false→true writes. Restoring an already-forced renderer would take an
    /// unrelated owner's mask away; a renderer we did not change is never in our restore set.</summary>
    private static void SetHidden(Record record, bool hide)
    {
        MeshRenderer renderer = record.Renderer;
        if (renderer == null)
        {
            record.Owned = false;
            return;
        }
        if (hide)
        {
            if (!record.Owned && !renderer.forceRenderingOff)
            {
                renderer.forceRenderingOff = true;
                record.Owned = true;
            }
        }
        else if (record.Owned)
        {
            if (renderer.forceRenderingOff)
                renderer.forceRenderingOff = false;
            record.Owned = false;
        }
    }

    private static uint StableHash(Transform unit, ProceduralMapTile tile)
    {
        // FNV-1a over the tile/unit path and sibling positions. No Unity instance IDs: Apparance
        // destroys and recreates generated content, and the same placement should keep its budget.
        uint hash = 2166136261u;
        for (Transform? node = unit; node != null && node != tile.transform; node = node.parent)
        {
            string name = node.name;
            for (int i = 0; i < name.Length; i++)
                hash = (hash ^ name[i]) * 16777619u;
            hash = (hash ^ (uint)node.GetSiblingIndex()) * 16777619u;
        }
        string tileName = tile.name;
        for (int i = 0; i < tileName.Length; i++)
            hash = (hash ^ tileName[i]) * 16777619u;
        return hash;
    }

    /// <summary>Classify the real transform chain, not a shader-only proxy. Generated Content
    /// proves scenario geometry; native prop/door/actor identity vetoes every descendant even if
    /// it happens to be named grass. A leaf collider vetoes hiding; represented parent colliders stay.
    /// LOD0/1/2 leaves inherit the nearest named mesh asset, not a whole mixed PCG wall generator.
    /// Mesh-local geometry avoids world-scale-dependent admission when the user zooms the board.
    /// </summary>
    private static Verdict Classify(MeshRenderer renderer, ProceduralMapTile tile,
                                    out Transform? unit, out Kind kind)
    {
        unit = null;
        kind = Kind.None;
        if (renderer == null || tile == null)
            return Verdict.Ancestry;
        bool generated = false;
        bool reachedTile = false;
        bool structural = false;
        Transform? structuralAsset = null;
        bool blockingCollider = false;
        bool ancestrySafe = !FigureRendererGuard.IsFigureOrActorRenderer(renderer)
                            && !FigureRendererGuard.HeldByPlayer(renderer);
        for (Transform? t = renderer.transform; t != null; t = t.parent)
        {
            if (t == tile.transform)
            {
                reachedTile = true;
                break;
            }
            CInteractable interactable = t.GetComponent<CInteractable>();
            UnityGameEditorObject nativeObject = t.GetComponent<UnityGameEditorObject>();
            if ((interactable != null && interactable is not CInteractableTile)
                || (nativeObject != null && nativeObject.PropObject != null)
                || t.GetComponent<ProceduralProp>() != null
                || t.GetComponent<ProceduralDoorway>() != null
                || t.GetComponent<UnityGameEditorDoorProp>() != null
                || FigureRendererGuard.CarriesFigureComponent(t)
                || t.GetComponent<Canvas>() != null
                || t.name == "Preview")
                ancestrySafe = false;
            if (!generated && HasUnrepresentedCollider(t, renderer))
                blockingCollider = true;
            if (t.name == "Generated Content")
            {
                generated = true;
                // Asset names above this boundary are containers, not this leaf's identity.
                continue;
            }
            if (!generated && unit == null && !t.name.StartsWith("PCG_", StringComparison.Ordinal))
            {
                Kind named = NamedKind(t.name);
                if (named != Kind.None)
                {
                    unit = t;
                    kind = named;
                }
                else if (IsStructuralName(t.name))
                {
                    structural = true;
                    if (structuralAsset == null)
                        structuralAsset = t;
                }
            }
        }
        if (!reachedTile || !generated)
            return Verdict.Generator;
        if (!ancestrySafe)
            return Verdict.Ancestry;
        if (blockingCollider)
            return Verdict.Effect;
        if (renderer.GetComponent<Animator>() != null
            || renderer.GetComponent<ParticleSystem>() != null)
            return Verdict.Effect;
        Mesh? mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null)
            return Verdict.Geometry;
        Vector3 size = mesh.bounds.size;
        if (float.IsNaN(size.x) || float.IsNaN(size.y) || float.IsNaN(size.z)
            || float.IsInfinity(size.x) || float.IsInfinity(size.y) || float.IsInfinity(size.z)
            || size.x < 0f || size.y < 0f || size.z < 0f
            || Math.Max(size.x, Math.Max(size.y, size.z)) > 80f
            || size.sqrMagnitude < .000001f)
            return Verdict.Geometry;

        bool foliage = UsesOnlyFoliage(renderer);
        bool dedicatedStructuralFoliage = false;
        // Apparance can label a renderer Mesh/LOD0 while retaining the original mesh asset
        // family. That asset is stronger provenance than the generated wrapper's generic name;
        // it still cannot override an explicit structural leaf or a gameplay ancestry veto.
        bool meshFamily = unit == null && !IsHardStructuralName(renderer.name)
                          && IsNativeSceneryAsset(mesh.name) && NamedKind(mesh.name) != Kind.None;
        if (meshFamily)
        {
            unit = renderer.transform;
            kind = NamedKind(mesh.name);
            structural = false;
        }
        // Real native composites put anonymous LOD/mesh children beneath their named asset.
        // A foliage-only child is the canopy/grass layer, not the wall/pillar/floor core. The
        // old nearest-asset veto retained those leaves just because a solid sibling's original
        // asset name contained Wall or Pillar. Never infer this for the named core itself, or
        // for a mixed-material renderer which could also carry the playable stone floor.
        if (foliage && !IsHardStructuralName(renderer.name))
        {
            Transform? carrier = unit ?? structuralAsset;
            if (carrier != null && carrier != renderer.transform && IsNativeSceneryAsset(carrier.name)
                && (structural || IsHardStructuralName(carrier.name)))
            {
                kind = NamedKind(carrier.name) == Kind.Grass ? Kind.Grass : Kind.Vegetation;
                // Keep the original carrier as the stable density key for every foliage LOD.
                // Selecting each anonymous leaf separately makes grass appear/disappear when
                // the native LOD switches, even though the user's density did not change.
                unit = carrier;
                structural = false;
                dedicatedStructuralFoliage = true;
            }
        }
        if (unit == null || kind == Kind.None)
            return structural ? Verdict.Structural : Verdict.Name;
        string assetName = meshFamily ? mesh.name : unit.name;
        // Vegetation textures attached to masonry are admitted only on foliage shader leaves.
        // FR_Pillar_Tree_Trunk_01 and the rock support beneath a wall are structural even though
        // they contain the word Tree or Floor. Ordinary FR_Tree_05 has separate foliage AND bark
        // renderers, all of which belong to the decorative tree and must disappear together.
        if (structural || (!dedicatedStructuralFoliage && IsHardStructuralName(assetName))
            || (IsStructuralName(assetName) && !foliage))
            return Verdict.Structural;
        // The forest's floor-grass half/full plates also supply the playable floor. Their solid
        // base remains; foliage/detail/scatter children disappear. This uses local mesh bounds,
        // never world AABB size, so setting scene scale to 1:1 cannot change this decision.
        if (kind == Kind.Grass && !foliage && IsGrassBase(assetName))
            return Verdict.Structural;
        return Verdict.Eligible;
    }

    /// <summary>A hidden leaf must not leave an invisible laser blocker behind. Decorative
    /// meshes with their own enabled collider remain visible. Ancestor colliders are permitted
    /// only on native walls/tiles or on a generated composite with an explicitly retained solid
    /// floor/structural member. Gameplay colliders are never disabled to obtain scenery savings.
    /// </summary>
    private static bool HasUnrepresentedCollider(Transform node, MeshRenderer renderer)
    {
        if (!_colliderFactsActive || !ColliderReadFacts.TryGetValue(node, out ColliderFacts facts))
        {
            facts = ReadColliderFacts(node);
            if (_colliderFactsActive)
                ColliderReadFacts[node] = facts;
        }
        for (int i = 0; i < facts.Colliders.Length; i++)
        {
            Collider collider = facts.Colliders[i];
            if (collider == null || !collider.enabled)
                continue;
            if (node == renderer.transform)
                return true;
            Mesh? leafMesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (collider is MeshCollider meshCollider && meshCollider.sharedMesh == leafMesh)
                return true;
            if (!facts.Represented)
                return true;
        }
        return false;
    }

    private static ColliderFacts ReadColliderFacts(Transform node)
    {
        Collider[] colliders = node.GetComponents<Collider>();
        bool enabled = false;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null && colliders[i].enabled)
                enabled = true;
        if (!enabled)
            return new ColliderFacts(colliders, true);
        if (node.GetComponent<ProceduralWall>() != null
            || node.GetComponent<ProceduralMapTile>() != null)
            return new ColliderFacts(colliders, true);
        MeshRenderer[] members = node.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
        for (int j = 0; j < members.Length; j++)
        {
            MeshRenderer member = members[j];
            if (member == null || !member.enabled || member.forceRenderingOff
                || !member.gameObject.activeInHierarchy || UsesOnlyFoliage(member))
                continue;
            if (RepresentsSolidComposite(member.transform, node))
                return new ColliderFacts(colliders, true);
        }
        return new ColliderFacts(colliders, false);
    }

    private static bool UsesFoliage(MeshRenderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] != null && materials[i].shader != null
                && materials[i].shader.name == GrassShader)
                return true;
        return false;
    }

    private static bool UsesOnlyFoliage(MeshRenderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;
        if (materials.Length == 0)
            return false;
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] == null || materials[i].shader == null
                || materials[i].shader.name != GrassShader)
                return false;
        return true;
    }

    /// <summary>Native wall/floor colliders often sit on a composite whose solid member is
    /// called LOD0, not FR_Wall_*. Read that member's nearest original asset ancestry. A tree's
    /// decorative bark is never a retained structural member merely because a distant wall is
    /// its parent. The collider owner itself is included for an authored solid composite.
    /// </summary>
    private static bool RepresentsSolidComposite(Transform member, Transform colliderOwner)
    {
        for (Transform? t = member; t != null; t = t.parent)
        {
            if (IsHardStructuralName(t.name) || IsGrassBase(t.name))
                return true;
            if (NamedKind(t.name) != Kind.None)
                return false;
            Mesh? mesh = t.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh != null && IsNativeSceneryAsset(mesh.name))
            {
                if (IsHardStructuralName(mesh.name) || IsGrassBase(mesh.name))
                    return true;
                if (NamedKind(mesh.name) != Kind.None)
                    return false;
            }
            if (t == colliderOwner)
                break;
        }
        return false;
    }

    private static bool IsNativeSceneryAsset(string name) =>
        name.StartsWith("FR_", StringComparison.Ordinal)
        || name.StartsWith("CR_", StringComparison.Ordinal)
        || name.StartsWith("CV_", StringComparison.Ordinal)
        || name.StartsWith("EN_", StringComparison.Ordinal);

    private static Kind NamedKind(string name)
    {
        // These are original asset families observed in scenario census paths, not arbitrary
        // names of containers. Generic floor/wall/prop shader matching would erase game geometry.
        if (name.IndexOf("_Grass", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Grassy", StringComparison.OrdinalIgnoreCase) >= 0)
            return Kind.Grass;
        if (name.IndexOf("_Tree", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Bush", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Bushes", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Plants", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Vines", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Ivy", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Roots", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Leaves", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Fern", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Shrub", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Reed", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Flower", StringComparison.OrdinalIgnoreCase) >= 0)
            return Kind.Vegetation;
        if (name.IndexOf("_Floor_Scatter_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Detail_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Clutter_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Stalagmites_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Crystal_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.StartsWith("FR_Stones_", StringComparison.Ordinal)
            || name.StartsWith("geranium ", StringComparison.Ordinal))
            return Kind.Dressing;
        return Kind.None;
    }

    private static bool IsStructuralName(string name) =>
        name.IndexOf("_Wall_", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_UnderWall_", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Pillar_", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Door", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Arch", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Floor_Base", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Floor_Basic", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_FloorTiles", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Stone_Floor_", StringComparison.OrdinalIgnoreCase) >= 0
        || name == "Simple Tile";

    private static bool IsHardStructuralName(string name)
    {
        if (name.IndexOf("_Pillar_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Door", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Arch", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Base", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Basic", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_FloorTiles", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Stone_Floor_", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        bool wall = name.IndexOf("_Wall_", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("_UnderWall_", StringComparison.OrdinalIgnoreCase) >= 0;
        bool foliageDressing = name.IndexOf("Bush", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("_Ivy", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("_Grass_", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.EndsWith("_Grass", StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("_Vines", StringComparison.OrdinalIgnoreCase) >= 0;
        return wall && !foliageDressing;
    }

    private static bool IsGrassBase(string name) =>
        name.StartsWith("FR_Floor_Grass_Half", StringComparison.Ordinal)
        || name.StartsWith("FR_Floor_Grass_Full", StringComparison.Ordinal)
        || name == "FR_Floor_Grass"
        || (name.StartsWith("FR_Floor_Detail_", StringComparison.Ordinal)
            && name.EndsWith("_Grass", StringComparison.Ordinal));

    /// <summary>Native ProceduralBase.GetScenario supports a root in the same scene as a tile,
    /// not just a parent. Mirror that proven hierarchy rule without calling an internal game API.
    /// </summary>
    private static bool IsScenarioTile(ProceduralMapTile tile)
    {
        for (Transform? t = tile.transform; t != null; t = t.parent)
            if (t.GetComponent<ProceduralScenario>() != null)
                return true;
        Scene scene = tile.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded)
            return false;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i].GetComponent<ProceduralScenario>() != null)
                return true;
        return false;
    }

    private static Transform[] CaptureChain(Transform leaf, ProceduralMapTile tile)
    {
        var nodes = new List<Transform>(8);
        for (Transform? t = leaf; t != null; t = t.parent)
        {
            nodes.Add(t);
            if (t == tile.transform)
                break;
        }
        return nodes.ToArray();
    }

    /// <summary>Validate the originally admitted chain. The rolling watch calls this for only
    /// a bounded number of records per frame; content-placement events validate known records
    /// immediately. Held props have a separate same-frame check when any prop is actually held.</summary>
    private static bool StillOnOriginalChain(Record record)
    {
        MeshRenderer renderer = record.Renderer;
        if (renderer == null)
            return false;
        Transform? t = renderer.transform;
        for (int i = 0; i < record.Chain.Length; i++)
        {
            if (t == null || !ReferenceEquals(t, record.Chain[i]))
                return false;
            t = t.parent;
        }
        return true;
    }

    private sealed class Driver : MonoBehaviour
    {
        private readonly struct Node
        {
            internal readonly Transform Transform;
            internal readonly ProceduralMapTile Tile;
            internal Node(Transform transform, ProceduralMapTile tile)
            {
                Transform = transform;
                Tile = tile;
            }
        }

        private readonly Queue<ProceduralMapTile> _pending = new();
        private readonly HashSet<int> _pendingIds = new();
        private readonly HashSet<int> _dirtyIds = new();
        private readonly Queue<Node> _nodes = new();
        private readonly Queue<Node> _materialNodes = new();
        private readonly HashSet<int> _materialNodeIds = new();
        private readonly List<ProceduralMapTile> _tiles = new(64);
        private readonly List<Record> _records = new(1024);
        private readonly Dictionary<int, Record> _byId = new(1024);
        private readonly HashSet<int> _tileIds = new();
        private readonly int[] _rejected = new int[7];
        private readonly HashSet<int> _visitedRendererIds = new();
        private int _sceneHandle = int.MinValue;
        private int _density = 100;
        private int _vegetationDensity = 100;
        private int _decorationDensity = 100;
        private ProceduralMapTile? _walkingTile;
        private bool _wasLoading;
        private bool _settlePending;
        private float _settleDue;
        private int _retuneIndex = -1;
        private int _pruneIndex;
        private int _watchIndex;
        private int _visitedNodes;
        private int _meshRenderers;
        private int _debugNames;
        private int _debugRejectedNames;
        private readonly int[] _debugRejectedVerdicts = new int[7];
        private readonly HashSet<int> _debugRejectedIds = new();
        private bool _inScenarioScene;
        private bool _actualScenario;
        private bool _summaryPrinted;
        private float _summaryDue;

        internal void QueueTile(ProceduralMapTile? tile)
        {
            if (!_inScenarioScene || !BudgetActive
                || tile == null || !IsScenarioTile(tile))
                return;
            _actualScenario = true;
            int id = tile.GetInstanceID();
            if (_pendingIds.Add(id))
                _pending.Enqueue(tile);
            else if (_walkingTile == tile)
                _dirtyIds.Add(id); // one catch-up pass, never a duplicate concurrent tile walk
            _summaryDue = Mathf.Max(_summaryDue, Time.unscaledTime + 1f);
        }

        internal void QueueRenderer(MeshRenderer renderer)
        {
            if (!_inScenarioScene || !BudgetActive)
                return;
            ProceduralMapTile? tile = TileAncestor(renderer.transform);
            if (tile == null || !IsScenarioTile(tile))
                return;
            if (_materialNodeIds.Add(renderer.GetInstanceID()))
                _materialNodes.Enqueue(new Node(renderer.transform, tile));
            _actualScenario = true;
            _summaryDue = Mathf.Max(_summaryDue, Time.unscaledTime + 1f);
            if (IsGrassBase(renderer.name) || IsStructuralName(renderer.name))
                QueueTile(tile);
        }

        private void Update()
        {
            Scene procedural = Choreographer.s_Choreographer != null
                ? Choreographer.s_Choreographer.m_ProcGenScene : default;
            Scene scene = procedural.IsValid() && procedural.isLoaded
                ? procedural : SceneManager.GetActiveScene();
            if (_sceneHandle != scene.handle || _inScenarioScene != VRSession.IsRunning)
                EnterScene(scene);

            int wantedGrass = Mathf.Clamp(PerfConfig.ScenarioSceneryDensityPercentValue, 0, 100);
            int wantedVegetation = Mathf.Clamp(PerfConfig.ScenarioVegetationDensityPercentValue, 0, 100);
            int wantedDecoration = Mathf.Clamp(PerfConfig.ScenarioDecorationDensityPercentValue, 0, 100);
            if (_density != wantedGrass || _vegetationDensity != wantedVegetation
                || _decorationDensity != wantedDecoration)
                ChangeDensity(wantedGrass, wantedVegetation, wantedDecoration);
            if (!_inScenarioScene)
                return;

            SceneController controller = SceneController.Instance;
            bool loading = controller != null && (controller.IsLoading || controller.ScenarioIsLoading);
            if (_wasLoading && !loading && BudgetActive)
            {
                // Build 600 seeded 484 renderers before Apparance produced the real 6,560. The
                // native loading flags, not sceneLoaded/active scene name, mark the usable room.
                SeedTiles();
                _settlePending = true;
                _settleDue = Time.unscaledTime + 2f;
                _summaryPrinted = false;
                _summaryDue = Time.unscaledTime + 3f;
            }
            _wasLoading = loading;
            if (_settlePending && !loading && Time.unscaledTime >= _settleDue)
            {
                // A bounded second completion pass covers delayed Apparance child registration.
                // Subsequent rooms/regeneration use their native placement and ShowContent hooks.
                _settlePending = false;
                SeedTiles();
            }
            if (!BudgetActive && _records.Count == 0)
                return;

            using var _perf = PerfMonitor.Scope("SceneryBudget.Update");
            RecheckOwned();
            PruneDead(16);
            Retune();
            if (BudgetActive)
                WalkNodes(loading || _settlePending);
            MaybeReport(loading);
        }

        private bool BudgetActive => _density < 100 || _vegetationDensity < 100 || _decorationDensity < 100;

        private void EnterScene(Scene scene)
        {
            RestoreAll();
            _sceneHandle = scene.handle;
            // Content is additive: the active scene in the supplied run is Game, not ProcGen.
            // Real ProceduralScenario membership below gates each tile; menus/maps admit none.
            _inScenarioScene = VRSession.IsRunning;
            _density = 100;
            _vegetationDensity = 100;
            _decorationDensity = 100;
            _actualScenario = false;
            _wasLoading = true;
            _settlePending = false;
            _summaryPrinted = false;
            _summaryDue = Time.unscaledTime + 3f;
        }

        private void ChangeDensity(int wantedGrass, int wantedVegetation, int wantedDecoration)
        {
            bool wasActive = BudgetActive;
            _density = wantedGrass;
            _vegetationDensity = wantedVegetation;
            _decorationDensity = wantedDecoration;
            PerfMonitor.MarkChange($"[Optimize] ScenarioSceneryDensityPercent={wantedGrass}; "
                             + $"ScenarioDecorationDensityPercent={wantedDecoration}; "
                             + $"ScenarioVegetationDensityPercent={wantedVegetation}");
            _retuneIndex = 0;
            _summaryPrinted = false;
            _summaryDue = Time.unscaledTime + 2f;
            if (!BudgetActive)
            {
                _pending.Clear();
                _pendingIds.Clear();
                _dirtyIds.Clear();
                _nodes.Clear();
                _materialNodes.Clear();
                _materialNodeIds.Clear();
                _walkingTile = null;
                _settlePending = false;
            }
            else if (!wasActive && _inScenarioScene)
                SeedTiles();
        }

        private void SeedTiles()
        {
            // Registry reads are scene/setting/loading edges, not full-heap per-frame sweeps.
            SceneRegistry.MapTiles.Collect(_tiles);
            for (int i = 0; i < _tiles.Count; i++)
                QueueTile(_tiles[i]);
            _tiles.Clear();
        }

        private void WalkNodes(bool loading)
        {
            // Collider-unit reads are exact only within this synchronous discovery slice. No
            // shared parent subtree is walked again for every grass leaf, and no facts survive
            // native regeneration or a collider toggle between frames.
            ColliderReadFacts.Clear();
            _colliderFactsActive = true;
            try { WalkNodesCore(loading); }
            finally { _colliderFactsActive = false; ColliderReadFacts.Clear(); }
        }

        private void WalkNodesCore(bool loading)
        {
            int budget = loading ? LoadingNodesPerFrame : NodesPerFrame;
            float deadline = Time.realtimeSinceStartup + LoadingWorkSeconds;
            while (budget-- > 0)
            {
                if (_materialNodes.Count > 0)
                {
                    Node ready = _materialNodes.Dequeue();
                    if (ready.Transform != null && ready.Tile != null)
                    {
                        MeshRenderer readyRenderer = ready.Transform.GetComponent<MeshRenderer>();
                        if (readyRenderer != null)
                        {
                            _materialNodeIds.Remove(readyRenderer.GetInstanceID());
                            if (_visitedRendererIds.Add(readyRenderer.GetInstanceID())) _meshRenderers++;
                            Examine(readyRenderer, ready.Tile);
                        }
                    }
                    if ((budget & 3) == 0 && Time.realtimeSinceStartup >= deadline)
                        break;
                    continue;
                }
                if (_nodes.Count == 0)
                {
                    if (_walkingTile != null)
                    {
                        int completedId = _walkingTile.GetInstanceID();
                        _pendingIds.Remove(completedId);
                        if (_dirtyIds.Remove(completedId))
                            QueueTile(_walkingTile);
                        _walkingTile = null;
                    }
                    if (_pending.Count == 0)
                        break;
                    ProceduralMapTile tile = _pending.Dequeue();
                    if (tile == null)
                        continue;
                    if (!IsScenarioTile(tile))
                    {
                        _pendingIds.Remove(tile.GetInstanceID());
                        continue;
                    }
                    _walkingTile = tile;
                    _tileIds.Add(tile.GetInstanceID());
                    _nodes.Enqueue(new Node(tile.transform, tile));
                }

                Node node = _nodes.Dequeue();
                Transform t = node.Transform;
                if (t == null || node.Tile == null)
                    continue;
                _visitedNodes++;
                MeshRenderer? renderer = t.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    if (_visitedRendererIds.Add(renderer.GetInstanceID()))
                        _meshRenderers++;
                    Examine(renderer, node.Tile);
                }
                for (int child = 0; child < t.childCount; child++)
                    _nodes.Enqueue(new Node(t.GetChild(child), node.Tile));
                if ((budget & 3) == 0 && Time.realtimeSinceStartup >= deadline)
                    break;
            }
        }

        private void Examine(MeshRenderer renderer, ProceduralMapTile tile)
        {
            int id = renderer.GetInstanceID();
            if (_byId.TryGetValue(id, out Record? known))
            {
                if (known.Renderer != null)
                {
                    RevalidateKnown(known, renderer, tile);
                    return;
                }
                _records.Remove(known);
                _byId.Remove(id);
                if (_retuneIndex >= 0)
                    _retuneIndex = 0;
            }

            Verdict verdict = Classify(renderer, tile, out Transform? unit, out Kind kind);
            _rejected[(int)verdict]++;
            if (verdict != Verdict.Eligible || unit == null)
            {
                if (VRLog.WantsDebug && _debugRejectedNames < NamedDebugCap
                    && _debugRejectedVerdicts[(int)verdict] < 2
                    && (kind != Kind.None || UsesFoliage(renderer))
                    && _debugRejectedIds.Add(id))
                {
                    _debugRejectedNames++;
                    _debugRejectedVerdicts[(int)verdict]++;
                    VRLog.Debug(Scope, "Scenario scenery rejected candidate "
                                       + PathOf(renderer.transform, tile.transform)
                                       + $" kind={kind} verdict={verdict} leafCollider="
                                       + (renderer.GetComponent<Collider>() != null));
                }
                return;
            }

            var record = new Record
            {
                Renderer = renderer,
                Chain = CaptureChain(renderer.transform, tile),
                Id = id,
                Hash = StableHash(unit, tile),
                Kind = kind,
            };
            _records.Add(record);
            _byId.Add(id, record);
            SetHidden(record, ShouldHide(record.Hash, DensityFor(record)));
            if (_debugNames < NamedDebugCap && VRLog.WantsDebug)
            {
                _debugNames++;
                VRLog.Debug(Scope, "Scenario scenery candidate " + PathOf(renderer.transform, tile.transform)
                                   + $" kind={kind} hash={record.Hash} forceRenderingOff={renderer.forceRenderingOff}");
            }
        }

        /// <summary>A placement or reveal can rebuild a unit without replacing its renderer.
        /// Re-run the full classifier before retaining our mask on a previously seen leaf.</summary>
        private void RevalidateKnown(Record record, MeshRenderer renderer, ProceduralMapTile tile)
        {
            Verdict verdict = Classify(renderer, tile, out Transform? unit, out Kind kind);
            if (verdict == Verdict.Eligible && unit != null)
            {
                record.Invalidated = false;
                record.Kind = kind;
                if (!StillOnOriginalChain(record))
                {
                    SetHidden(record, false);
                    record.Chain = CaptureChain(renderer.transform, tile);
                    record.Hash = StableHash(unit, tile);
                }
                SetHidden(record, ShouldHide(record.Hash, DensityFor(record)));
                return;
            }
            SetHidden(record, false);
            record.Invalidated = true;
        }

        private int DensityFor(Record record) => record.Kind switch
        {
            Kind.Grass => _density,
            Kind.Vegetation => _vegetationDensity,
            _ => _decorationDensity,
        };

        private void Retune()
        {
            if (_retuneIndex < 0)
                return;
            int end = Math.Min(_records.Count, _retuneIndex + RetunesPerFrame);
            for (; _retuneIndex < end; _retuneIndex++)
            {
                Record record = _records[_retuneIndex];
                if (!record.Invalidated)
                    SetHidden(record, ShouldHide(record.Hash, DensityFor(record)));
            }
            if (_retuneIndex < _records.Count)
                return;
            _retuneIndex = -1;
            PerfMonitor.MarkChange($"Scenario scenery budget retune complete: grass {_density}%, "
                             + $"decoration {_decorationDensity}%, vegetation {_vegetationDensity}%");
            if (!BudgetActive)
            {
                _records.Clear();
                _byId.Clear();
            }
        }

        private void RecheckOwned()
        {
            // A held prop is the one state transition that must never wait for a rolling watch.
            // This full list scan is only active during an actual local or remote prop hold.
            if (HeldProps.Count > 0 || NetHeldProps.Any)
            {
                for (int i = 0; i < _records.Count; i++)
                {
                    Record record = _records[i];
                    if (!record.Owned || record.Renderer == null
                        || !FigureRendererGuard.HeldByPlayer(record.Renderer))
                        continue;
                    SetHidden(record, false);
                    record.Invalidated = true;
                }
            }

            // Apparance may replace/reparent generated content outside a placement callback.
            // Check a fixed slice, not every hidden renderer's full Transform.parent chain.
            int budget = Math.Min(_records.Count, AncestryChecksPerFrame);
            while (budget-- > 0)
            {
                if (_watchIndex >= _records.Count)
                    _watchIndex = 0;
                Record record = _records[_watchIndex++];
                if (!record.Owned || StillOnOriginalChain(record))
                    continue;
                SetHidden(record, false);
                record.Invalidated = true;
            }
        }

        /// <summary>Apparance replaces generated renderer instances. Prune only our small record
        /// list, at most sixteen entries per frame, so destroyed clones cannot accumulate for a
        /// long session. Never mutate list indices while a setting retune is in progress.</summary>
        private void PruneDead(int budget)
        {
            if (_retuneIndex >= 0 || _records.Count == 0)
                return;
            while (budget-- > 0 && _records.Count > 0)
            {
                if (_pruneIndex >= _records.Count)
                    _pruneIndex = 0;
                Record record = _records[_pruneIndex];
                if (record.Renderer != null)
                {
                    _pruneIndex++;
                    continue;
                }
                if (_byId.TryGetValue(record.Id, out Record? mapped)
                    && ReferenceEquals(mapped, record))
                    _byId.Remove(record.Id);
                int last = _records.Count - 1;
                _records[_pruneIndex] = _records[last];
                _records.RemoveAt(last);
            }
        }

        private void MaybeReport(bool loading)
        {
            if (_summaryPrinted || loading || _settlePending || Time.unscaledTime < _summaryDue
                || _retuneIndex >= 0 || _pending.Count != 0 || _nodes.Count != 0
                || _walkingTile != null || _materialNodes.Count != 0 || !_actualScenario)
                return;
            int actuallyForced = 0;
            int owned = 0;
            int grass = 0;
            int vegetation = 0;
            int dressing = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                Record record = _records[i];
                if (record.Renderer == null || record.Invalidated)
                    continue;
                if (record.Renderer.forceRenderingOff) actuallyForced++;
                if (record.Owned) owned++;
                switch (record.Kind)
                {
                    case Kind.Grass: grass++; break;
                    case Kind.Vegetation: vegetation++; break;
                    case Kind.Dressing: dressing++; break;
                }
            }
            PerfMonitor.MarkChange($"Scenario scenery preparation complete: {owned} owned renderer masks");
            VRLog.Note(Scope, $"Scenario scenery budget: density {_density}%, decoration "
                              + $"{_decorationDensity}%, vegetation {_vegetationDensity}%; {_tileIds.Count} tile(s), "
                              + $"{_meshRenderers} unique mesh renderer(s) inspected, "
                              + $"eligible grass {grass}, trees/bushes/vines {vegetation}, "
                              + $"scatter/details {dressing}; {owned} owned and {actuallyForced} "
                              + "actually forceRenderingOff. Native props, actors, floors and "
                              + "structural walls preserved; actual frame gain is unverified.");
            if (VRLog.WantsDebug)
                VRLog.Debug(Scope, $"Scenario scenery rejection encounters (repeat walks included): "
                                   + $"no generated ancestry {_rejected[(int)Verdict.Generator]}, "
                                   + $"actor/prop/door/preview {_rejected[(int)Verdict.Ancestry]}, "
                                   + $"structural {_rejected[(int)Verdict.Structural]}, "
                                   + $"collider/effects {_rejected[(int)Verdict.Effect]}, "
                                   + $"geometry {_rejected[(int)Verdict.Geometry]}; "
                                   + $"{_visitedNodes} traversal node visits.");
            _summaryPrinted = true;
        }

        internal void RestoreAll()
        {
            for (int i = 0; i < _records.Count; i++)
                SetHidden(_records[i], false);
            _records.Clear();
            _byId.Clear();
            _pending.Clear();
            _pendingIds.Clear();
            _dirtyIds.Clear();
            _nodes.Clear();
            _materialNodes.Clear();
            _materialNodeIds.Clear();
            _walkingTile = null;
            _tileIds.Clear();
            _visitedRendererIds.Clear();
            Array.Clear(_rejected, 0, _rejected.Length);
            _visitedNodes = 0;
            _meshRenderers = 0;
            _debugNames = 0;
            _debugRejectedNames = 0;
            Array.Clear(_debugRejectedVerdicts, 0, _debugRejectedVerdicts.Length);
            _debugRejectedIds.Clear();
            _retuneIndex = -1;
            _pruneIndex = 0;
            _watchIndex = 0;
        }

        private static string PathOf(Transform leaf, Transform stop)
        {
            var parts = new List<string>(8);
            for (Transform? t = leaf; t != null && t != stop; t = t.parent)
                parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}

/// <summary>Pure notification; the driver does the bounded traversal outside the game callback.</summary>
[HarmonyPatch(typeof(ProceduralBase), nameof(ProceduralBase.NotifyContentPlacementComplete))]
internal static class ProceduralBase_ContentPlaced_SceneryBudgetPatch
{
    private static void Postfix(ProceduralBase __instance)
    {
        try { ScenarioSceneryBudget.ContentPlaced(__instance); }
        catch { /* no render-budget fault may escape into native placement */ }
    }
}

/// <summary>Generated Content may already exist but be inactive until a room is revealed.</summary>
[HarmonyPatch(typeof(ProceduralMapTile), nameof(ProceduralMapTile.ShowContent))]
internal static class ProceduralMapTile_ShowContent_SceneryBudgetPatch
{
    private static void Postfix(GameObject o)
    {
        try { ScenarioSceneryBudget.ContentShown(o); }
        catch { /* no render-budget fault may escape into room reveal */ }
    }
}

/// <summary>Observe the final native material assignment; never load or alter game materials.</summary>
[HarmonyPatch(typeof(MaterialLoaderData), "CheckAllMaterialLoaded")]
internal static class MaterialLoaderData_Ready_SceneryBudgetPatch
{
    private static void Postfix(MaterialLoaderData __instance)
    {
        try { if (__instance.Renderer != null) ScenarioSceneryBudget.MaterialsReady(__instance.Renderer); }
        catch { /* no presentation-budget fault may escape into Addressables completion */ }
    }
}
