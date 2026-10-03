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
/// floor plates, masonry, doors, actors and native gameplay props retain their renderers. Native
/// tree pillars are vegetation, not masonry: 0% removes their bark and foliage together.
///
/// Only owned false-to-true Renderer.forceRenderingOff writes are restored. Native enabled state,
/// components, materials, property blocks, room reveal and multiplayer state are never written.
/// Only purely decorative tree/bay colliders are disabled/restored with an owned visual mask. Shared
/// procedural wall/tile colliders and all gameplay/trigger/body colliders remain untouched.
/// Grass, vegetation and loose-decoration budgets are independent: Build 601's
/// decoration-zero cap otherwise made every grass slider movement ineffective. All three at
/// 100 restore the original rendering on PC and Steam Frame. Dedicated foliage leaves beneath
/// a masonry asset remain optional; its solid or mixed-material core is never optional.
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
    private static bool _loadingFailureLogged;
    private static readonly Dictionary<Transform, ColliderFacts> ColliderReadFacts = new();
    private static readonly Dictionary<Collider, bool> TreeColliderReadFacts = new();
    private static readonly Dictionary<Collider, TreeColliderOwner> TreeColliderOwners = new();
    private static readonly Dictionary<Collider, bool> BayColliderOwners = new();
    private static readonly Dictionary<Collider, bool> BayColliderReadFacts = new();

    private sealed class TreeColliderOwner
    {
        internal int Claims;
        internal bool Owned;
    }

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
                VRSession.Harmony.PatchAll(typeof(SceneController_LoadingComplete_SceneryBudgetPatch));
                ScenarioDecorativePlacement.Install();
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
        ScenarioDecorativePlacement.Shutdown();
        _driver.RestoreAll();
        UnityEngine.Object.Destroy(_driver);
        _driver = null;
        _loadingFailureLogged = false;
    }

    internal static void LoadingFailure(Exception error)
    {
        if (_loadingFailureLogged) return;
        _loadingFailureLogged = true;
        VRLog.Note(Scope, "Scenario scenery loading preparation failed; native continuation retained (" + error.Message + ").");
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
    /// Prepare the assigned visual before a camera can render its newly enabled material; the
    /// A newly visible structural base also prepares its nearest shared-collider composite
    /// synchronously: its grass may already have completed while the box was unrepresented.
    /// The queued leaf/tile remains a fallback, never the first masking of those ready siblings.
    /// </summary>
    internal static void MaterialsReady(Renderer renderer)
    {
        if (renderer is MeshRenderer mesh && mesh.enabled)
        {
            _driver?.PrepareMaterialReady(mesh);
            _driver?.QueueRenderer(mesh);
        }
    }

    internal static void BeforeContentShown(GameObject root)
    {
        if (root != null) _driver?.PrepareSubtree(root);
    }

    internal static void BeforeLoadingComplete() => _driver?.PrepareLoadingCompletion();

    /// <summary>Only masks owned by this reversible, purely decorative budget qualify.
    /// Structural facts and foreign masks are not evidence that a wall can be discarded.
    /// </summary>
    internal static bool IsOwnedHidden(Renderer renderer) => renderer != null
        && _driver != null && _driver.IsOwnedHidden(renderer);

    internal static bool IsScenarioPlacement(Transform parent)
    {
        ProceduralMapTile? tile = TileAncestor(parent);
        if (tile == null || !IsScenarioTile(tile)) return false;
        bool generated = false;
        for (Transform? t = parent; t != null && t != tile.transform; t = t.parent)
        {
            UnityGameEditorObject native = t.GetComponent<UnityGameEditorObject>();
            if (t.GetComponent<ProceduralProp>() != null || t.GetComponent<ProceduralDoorway>() != null
                || t.GetComponent<UnityGameEditorDoorProp>() != null || t.GetComponent<CInteractable>() != null
                || (native != null && native.PropObject != null)
                || FigureRendererGuard.CarriesFigureComponent(t) || t.GetComponent<Canvas>() != null
                || t.GetComponent<Light>() != null || t.GetComponent<Animator>() != null
                || t.GetComponent<ParticleSystem>() != null || t.GetComponent<Rigidbody>() != null
                || t.name == "Preview") return false;
            if (t.name == "Generated Content") generated = true;
            ColliderFacts facts = ReadColliderFacts(t);
            if (!facts.Represented)
                for (int i = 0; i < facts.Colliders.Length; i++)
                    if (facts.Colliders[i] != null && ColliderIsPresent(facts.Colliders[i])) return false;
        }
        return generated;
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
        internal Collider[] TreeColliders = Array.Empty<Collider>();
        internal bool ColliderClaims;
        internal Collider[] BayColliders = Array.Empty<Collider>();
    }

    /// <summary>Own only false→true writes. Restoring an already-forced renderer would take an
    /// unrelated owner's mask away; a renderer we did not change is never in our restore set.</summary>
    private static void SetHidden(Record record, bool hide)
    {
        MeshRenderer renderer = record.Renderer;
        if (renderer == null)
        {
            ReleaseTreeColliders(record);
            record.Owned = false;
            for (int i = 0; i < record.BayColliders.Length; i++) RefreshBayCollider(record.BayColliders[i]);
            return;
        }
        if (hide)
        {
            if (!record.Owned && !renderer.forceRenderingOff)
            {
                ScenarioEnvironmentBudget.BeforeNativeRendererWrite(renderer);
                renderer.forceRenderingOff = true;
                record.Owned = true;
                ClaimTreeColliders(record);
            }
        }
        else if (record.Owned)
        {
            if (renderer.forceRenderingOff)
            {
                ScenarioEnvironmentBudget.BeforeNativeRendererWrite(renderer);
                renderer.forceRenderingOff = false;
            }
            record.Owned = false;
            ReleaseTreeColliders(record);
        }
        for (int i = 0; i < record.BayColliders.Length; i++)
            RefreshBayCollider(record.BayColliders[i]);
    }

    private static void ClaimTreeColliders(Record record)
    {
        if (record.ColliderClaims || record.TreeColliders.Length == 0)
            return;
        record.ColliderClaims = true;
        for (int i = 0; i < record.TreeColliders.Length; i++)
        {
            Collider collider = record.TreeColliders[i];
            if (collider == null)
                continue;
            if (!TreeColliderOwners.TryGetValue(collider, out TreeColliderOwner? owner))
            {
                owner = new TreeColliderOwner();
                TreeColliderOwners.Add(collider, owner);
            }
            owner.Claims++;
            if (owner.Claims == 1 && collider.enabled)
            {
                collider.enabled = false;
                owner.Owned = true;
            }
        }
    }

    private static void ReleaseTreeColliders(Record record)
    {
        if (!record.ColliderClaims)
            return;
        record.ColliderClaims = false;
        for (int i = 0; i < record.TreeColliders.Length; i++)
        {
            Collider collider = record.TreeColliders[i];
            if (ReferenceEquals(collider, null)
                || !TreeColliderOwners.TryGetValue(collider, out TreeColliderOwner? owner))
                continue;
            if (--owner.Claims > 0)
                continue;
            // Disabled before our first mask is foreign state. Only a false write we actually
            // made authorises its restoration, and the last hidden LOD member releases it.
            if (owner.Owned && collider != null && !collider.enabled)
                collider.enabled = true;
            TreeColliderOwners.Remove(collider!);
        }
    }

    private static bool SameColliders(Collider[] left, Collider[] right)
    {
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
            if (!ReferenceEquals(left[i], right[i]))
                return false;
        return true;
    }

    private static uint StableHash(Transform unit, ProceduralMapTile tile)
    {
        // FNV-1a over the tile/unit path and sibling positions. No Unity instance IDs: Apparance
        // destroys and recreates generated content, and the same placement should keep its budget.
        uint hash = 2166136261u;
        for (Transform? node = unit; node != null && node != tile.transform; node = node.parent)
        {
            // A deferred native handle already occupies the original prefab's path slot.
            // Its restored original hierarchy adds one implementation-only root; skip that
            // root so leaf density remains identical to an ordinary native placement.
            if (ScenarioDecorativePlacement.IsRestoredRoot(node)) continue;
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
    /// it happens to be named grass. Unrepresented collision vetoes hiding unless it exclusively
    /// belongs to an original decorative tree and can be reversibly masked with that tree.
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
                || t.GetComponent<Light>() != null
                || t.GetComponent<Animator>() != null
                || t.GetComponent<ParticleSystem>() != null
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
        if (renderer.GetComponent<Animator>() != null
            || renderer.GetComponent<ParticleSystem>() != null)
            return Verdict.Effect;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        Mesh? mesh = filter != null ? filter.sharedMesh : null;
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
        // Build 604 review found real CR_RU_Vines children on hardware's 17-renderer tree
        // assemblies. Their leaf names do not contain Tree, so a Tree-only promotion split
        // their density/collision ownership from the trunk. Bind eligible native plant members
        // to the original tree carrier; hard floors/masonry and loose dressing never inherit it.
        Transform? treeCarrier = NativeTreeCarrier(renderer);
        if (treeCarrier != null)
        {
            unit = treeCarrier;
            kind = Kind.Vegetation;
            structural = false;
        }
        if (IsNativeSceneryAsset(mesh.name)
            && (IsHardStructuralName(mesh.name) || (!foliage && IsGrassBase(mesh.name))))
            return Verdict.Structural;
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
        string assetName = meshFamily ? mesh.name : TreeAssetName(unit.name);
        // Build 603 explicitly exempted FR_Pillar_Tree_Trunk_01. The actual Build 603 census
        // still names those trunks throughout the wall profiles, and the maintainer defines
        // vegetation 0% as no trees. Original tree/trunk asset identity now overrides the word
        // Pillar only, never floor/wall/masonry identity or actual native gameplay ancestry.
        // Every bark/canopy LOD in a decorative tree shares one native tree density carrier.
        // Masonry foliage still requires a dedicated foliage-only renderer.
        if (structural || (!dedicatedStructuralFoliage && IsHardStructuralName(assetName))
            || (IsStructuralName(assetName) && !foliage && !IsNativeWallWoodLeaf(assetName)))
            return Verdict.Structural;
        // The forest's floor-grass half/full plates also supply the playable floor. Their solid
        // base remains; foliage/detail/scatter children disappear. This uses local mesh bounds,
        // never world AABB size, so setting scene scale to 1:1 cannot change this decision.
        if (kind == Kind.Grass && !foliage && IsGrassBase(assetName))
            return Verdict.Structural;
        if (blockingCollider)
        {
            Transform? tree = kind == Kind.Vegetation && IsNativeTreeAsset(assetName) ? unit : null;
            for (Transform? t = renderer.transform; t != null && t.name != "Generated Content";
                 t = t.parent)
                if (HasUnrepresentedCollider(t, renderer, tree))
                    return Verdict.Effect;
        }
        return Verdict.Eligible;
    }

    /// <summary>A hidden leaf must not leave an invisible laser blocker behind. Decorative
    /// meshes with their own enabled collider remain visible unless native tree identity permits
    /// an owned decorative collider mask. Ancestor colliders on native walls/tiles or a generated
    /// composite with a retained solid floor/masonry member stay. Gameplay collision never changes.
    /// </summary>
    private static bool HasUnrepresentedCollider(Transform node, MeshRenderer renderer,
                                                  Transform? treeUnit = null)
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
            if (collider == null || !ColliderIsPresent(collider))
                continue;
            if (CanOwnBayCollider(collider))
                continue;
            if (treeUnit != null && CanOwnTreeCollider(collider, treeUnit))
                continue;
            if (node == renderer.transform)
                return true;
            MeshFilter leafFilter = renderer.GetComponent<MeshFilter>();
            Mesh? leafMesh = leafFilter != null ? leafFilter.sharedMesh : null;
            if (collider is MeshCollider meshCollider && meshCollider.sharedMesh == leafMesh)
                return true;
            if (!facts.Represented)
                return true;
        }
        return false;
    }

    // An owned disabled tree collider still participates in reclassification; otherwise native
    // component/hierarchy changes could evade the collider veto merely because we hid it earlier.
    private static bool ColliderIsPresent(Collider collider) =>
        collider.enabled || (TreeColliderOwners.TryGetValue(collider, out TreeColliderOwner? owner)
                             && owner.Owned) || (BayColliderOwners.TryGetValue(collider, out bool owned) && owned);

    /// <summary>Locate an actual original tree assembly, not its surrounding wall or room.
    /// Native foliage/vines/bushes and grass children follow this vegetation unit even when their
    /// own names omit Tree. A hard solid member or independently named loose decoration is a
    /// scope barrier. Checking original meshes too protects an anonymously named playable floor.
    /// </summary>
    private static Transform? NativeTreeCarrier(MeshRenderer renderer)
    {
        Transform? carrier = null;
        bool foliage = UsesOnlyFoliage(renderer);
        for (Transform? t = renderer.transform; t != null && t.name != "Generated Content";
             t = t.parent)
        {
            MeshFilter filter = t.GetComponent<MeshFilter>();
            Mesh? mesh = filter != null ? filter.sharedMesh : null;
            string name = TreeAssetName(t.name);
            string original = mesh != null && IsNativeSceneryAsset(mesh.name) ? mesh.name : "";
            if (IsHardStructuralName(name) || IsHardStructuralName(original)
                || (!foliage && ((IsStructuralName(name) && !IsNativeWallWoodLeaf(name)) || (IsStructuralName(original) && !IsNativeWallWoodLeaf(original))
                                || IsGrassBase(name) || IsGrassBase(original)))
                || NamedKind(name) == Kind.Dressing || NamedKind(original) == Kind.Dressing)
                return carrier; // outside an already complete tree, the wall/floor is its boundary
            if (IsNativeTreeAsset(name) || IsNativeTreeAsset(original))
                carrier = t;
        }
        return carrier;
    }

    private static string TreeAssetName(string name) =>
        name.StartsWith("PCG_", StringComparison.Ordinal)
        && IsNativeTreeAsset(name.Substring(4)) ? name.Substring(4) : name;

    /// <summary>Only collision wholly belonging to the removed tree is a presentation mask.
    /// A native wall/tile, trigger, rigid body, gameplay prop or mixed solid subtree is never
    /// suppressed. Original tree identity is required; a conveniently named player wrapper is
    /// insufficient. Facts last one discovery slice, never across regeneration/config changes.
    /// </summary>
    private static bool CanOwnTreeCollider(Collider collider, Transform treeUnit)
    {
        if (_colliderFactsActive && TreeColliderReadFacts.TryGetValue(collider, out bool known))
            return known;
        if (collider == null)
            return false;
        bool safe = !collider.isTrigger && collider.attachedRigidbody == null;
        Transform owner = collider.transform;
        // Unity can report no attachedRigidbody while its collider is disabled. Inspect the
        // actual transform ancestry too, so a new native body never inherits an old tree mask.
        for (Transform? a = owner; a != null && safe; a = a.parent)
            if (a.GetComponent<Rigidbody>() != null)
                safe = false;
        bool reachesTree = owner.IsChildOf(treeUnit);
        if (!reachesTree && treeUnit.IsChildOf(owner))
            reachesTree = IsNativeTreeAsset(owner.name)
                          || (owner.name.StartsWith("PCG_", StringComparison.Ordinal)
                              && IsNativeTreeAsset(owner.name.Substring(4)));
        safe &= reachesTree;
        if (safe)
        {
            // A collider attached to a branch can still enclose other native content. Refuse
            // identity/effect descendants and any non-tree render member before disabling it.
            Transform[] nodes = owner.GetComponentsInChildren<Transform>(includeInactive: true);
            bool meshFound = false;
            for (int i = 0; i < nodes.Length && safe; i++)
            {
                Transform t = nodes[i];
                UnityGameEditorObject native = t.GetComponent<UnityGameEditorObject>();
                if (t.GetComponent<CInteractable>() != null
                    || t.GetComponent<ProceduralBase>() != null
                    || (native != null && native.PropObject != null)
                    || FigureRendererGuard.CarriesFigureComponent(t)
                    || t.GetComponent<Canvas>() != null || t.GetComponent<Light>() != null
                    || t.GetComponent<Animator>() != null || t.GetComponent<ParticleSystem>() != null
                    || t.GetComponent<SkinnedMeshRenderer>() != null)
                { safe = false; break; }
                MeshRenderer member = t.GetComponent<MeshRenderer>();
                if (member == null)
                    continue;
                meshFound = true;
                Transform? memberCarrier = NativeTreeCarrier(member);
                bool treeMember = memberCarrier != null && memberCarrier == treeUnit;
                safe &= treeMember;
            }
            safe &= meshFound;
        }
        if (_colliderFactsActive)
            TreeColliderReadFacts[collider] = safe;
        return safe;
    }

    private static Collider[] TreeCollidersFor(MeshRenderer renderer, Transform unit)
    {
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        Mesh? original = filter != null ? filter.sharedMesh : null;
        if (!IsNativeTreeAsset(TreeAssetName(unit.name))
            && (original == null || !IsNativeTreeAsset(original.name)))
            return Array.Empty<Collider>();
        var colliders = new List<Collider>();
        for (Transform? t = renderer.transform; t != null && t.name != "Generated Content";
             t = t.parent)
        {
            Collider[] found = t.GetComponents<Collider>();
            for (int i = 0; i < found.Length; i++)
                if (CanOwnTreeCollider(found[i], unit))
                    colliders.Add(found[i]);
        }
        return colliders.ToArray();
    }

    // Build604's surviving conifers are the real FR_Default_Bay_10 prefab: one box and
    // eleven original tree/bush/scatter meshes, no retained masonry. The old tree-only
    // ownership rejected every member because the box surrounds several independently named
    // trees and stones. Admit only the proven pure native bay; keep its collider while ANY
    // member is visible, and restore only our disable when settings or native identity change.
    private static bool CanOwnBayCollider(Collider collider)
    {
        if (_colliderFactsActive && BayColliderReadFacts.TryGetValue(collider, out bool known)) return known;
        if (collider == null || collider.isTrigger || collider.attachedRigidbody != null
            || !collider.name.StartsWith("FR_Default_Bay_", StringComparison.Ordinal)) return false;
        for (Transform? parent = collider.transform; parent != null; parent = parent.parent)
            if (parent.GetComponent<Rigidbody>() != null) return false;
        bool safe = DecorativeCategories(collider.gameObject) != 0;
        if (_colliderFactsActive) BayColliderReadFacts[collider] = safe;
        return safe;
    }

    private static Collider[] BayCollidersFor(MeshRenderer renderer)
    {
        var found = new List<Collider>();
        for (Transform? t = renderer.transform; t != null && t.name != "Generated Content"; t = t.parent)
            foreach (Collider collider in t.GetComponents<Collider>())
                if (CanOwnBayCollider(collider)) found.Add(collider);
        return found.ToArray();
    }

    private static void RefreshBayCollider(Collider collider)
    {
        if (ReferenceEquals(collider, null)) return;
        BayColliderOwners.TryGetValue(collider, out bool owned);
        bool hidden = CanOwnBayCollider(collider);
        if (hidden)
        {
            MeshRenderer[] members = collider.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            for (int i = 0; i < members.Length; i++)
                if (members[i] != null && members[i].enabled && members[i].gameObject.activeInHierarchy
                    && !members[i].forceRenderingOff) { hidden = false; break; }
        }
        if (hidden && collider != null && collider.enabled)
        { collider.enabled = false; BayColliderOwners[collider] = true; }
        else if (!hidden && owned)
        {
            if (collider != null && !collider.enabled) collider.enabled = true;
            BayColliderOwners.Remove(collider!);
        }
    }

    /// <summary>Positive whole-prefab proof for deferred creation and shared decorative boxes.
    /// Original asset names alone cannot approve a prefab: every component and every original
    /// mesh must be a known presentation member. Unknown scripts, native placement parameters,
    /// actors/props, light/effects, triggers, bodies and structural or mixed floor meshes veto it.
    /// MaterialLoader and the original detail/shadow providers only manage this visual subtree.
    /// This reads the untouched loaded template; no asset or native generation recipe is edited.
    /// </summary>
    internal static int DecorativeCategories(GameObject root)
    {
        if (root == null) return 0;
        string nativeRoot = root.name.StartsWith("PCG_", StringComparison.Ordinal) ? root.name.Substring(4) : root.name;
        if (!IsNativeSceneryAsset(nativeRoot)) return 0;
        int categories = 0;
        bool tree = IsNativeTreeAsset(nativeRoot);
        Transform[] nodes = root.transform.GetComponentsInChildren<Transform>(includeInactive: true);
        for (int i = 0; i < nodes.Length; i++)
        {
            Transform t = nodes[i];
            Component[] components = t.GetComponents<Component>();
            for (int j = 0; j < components.Length; j++)
            {
                Component component = components[j];
                if (component == null) return 0; // missing native script is not a proven decorative prefab
                if (component is Transform || component is MeshFilter || component is MeshRenderer
                    || component is LODGroup) continue;
                if (component is Collider collider)
                {
                    if (collider.isTrigger || collider.attachedRigidbody != null) return 0;
                    continue;
                }
                Type type = component.GetType();
                if (type != typeof(MaterialLoader) && type != typeof(DetailsDisabler) && type != typeof(DetailLevelDisableProvider)
                    && type != typeof(ImportantObjectsShadowsDisabler) && type != typeof(PropObjectsShadowsDisabler)) return 0;
            }
            MeshRenderer renderer = t.GetComponent<MeshRenderer>();
            if (renderer == null) continue;
            MeshFilter filter = t.GetComponent<MeshFilter>();
            Mesh? mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !IsNativeSceneryAsset(mesh.name)) return 0;
            Vector3 size = mesh.bounds.size;
            if (float.IsNaN(size.x) || float.IsNaN(size.y) || float.IsNaN(size.z)
                || float.IsInfinity(size.x) || float.IsInfinity(size.y) || float.IsInfinity(size.z)
                || size.x < 0f || size.y < 0f || size.z < 0f || size.sqrMagnitude < .000001f
                || Math.Max(size.x, Math.Max(size.y, size.z)) > 80f) return 0;
            // Mesh family wins over an incidental child label; require both to avoid a floor
            // renamed "Tree" and preserve separate structural members of a native composite.
            if (IsHardStructuralName(mesh.name) || IsGrassBase(mesh.name)
                || IsHardStructuralName(t.name) || IsGrassBase(t.name)) return 0;
            Kind kind = NamedKind(mesh.name);
            if (kind == Kind.None || NamedKind(t.name) == Kind.None) return 0;
            categories |= tree ? 2 : kind == Kind.Grass ? 1 : kind == Kind.Vegetation ? 2 : 4;
        }
        return categories;
    }

    private static ColliderFacts ReadColliderFacts(Transform node)
    {
        Collider[] colliders = node.GetComponents<Collider>();
        bool enabled = false;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null && ColliderIsPresent(colliders[i]))
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
            MeshFilter filter = t.GetComponent<MeshFilter>();
            Mesh? mesh = filter != null ? filter.sharedMesh : null;
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
        if (IsNativeWallPlantLeaf(name))
            return Kind.Vegetation;
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
            || name.IndexOf("_Flower", StringComparison.OrdinalIgnoreCase) >= 0
            || (IsNativeWallPlantLeaf(name) && name.IndexOf("_Log", StringComparison.OrdinalIgnoreCase) >= 0))
            return Kind.Vegetation;
        if (name.IndexOf("_Floor_Scatter_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Detail_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Clutter_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Stalagmites_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Crystal_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.StartsWith("FR_Stones_", StringComparison.Ordinal)
            || name.StartsWith("CR_FR_Stones_", StringComparison.Ordinal)
            || name.StartsWith("geranium ", StringComparison.Ordinal))
            return Kind.Dressing;
        return Kind.None;
    }

    private static bool IsStructuralName(string name) =>
        !IsNativeTreeAsset(name) && (name.IndexOf("_Wall_", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_UnderWall_", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Pillar_", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Door", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Arch", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Floor_Base", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Floor_Basic", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_FloorTiles", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("_Stone_Floor_", StringComparison.OrdinalIgnoreCase) >= 0
        || name == "Simple Tile");

    // NativeForestProvenance pins the original wall and underwall composites. Plants/leaves
    // remain foliage-only. Roots and the detached Thin_Log member are separate wood meshes
    // beside the original Wall/Rock/Stone core, so they can follow vegetation even with a bark
    // material. The solid core, and a log owning unrepresented collision, remain protected.
    private static bool IsNativeWallPlantLeaf(string name) =>
        IsNativeSceneryAsset(name)
        && (name.IndexOf("_Wall", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_UnderWall", StringComparison.OrdinalIgnoreCase) >= 0)
        && (name.IndexOf("_Plants", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Leaves", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Roots", StringComparison.OrdinalIgnoreCase) >= 0
            || name.StartsWith("FR_Wall_Grassy_Verge_Thin_Log_", StringComparison.Ordinal)
            || name.StartsWith("CR_FR_Wall_Grassy_Verge_Thin_Log_", StringComparison.Ordinal));

    private static bool IsNativeWallWoodLeaf(string name) =>
        IsNativeWallPlantLeaf(name) && (name.IndexOf("_Roots", StringComparison.OrdinalIgnoreCase) >= 0
                                       || name.IndexOf("_Log", StringComparison.OrdinalIgnoreCase) >= 0);

    private static bool IsNativeTreeAsset(string name) =>
        IsNativeSceneryAsset(name)
        && name.IndexOf("_Tree", StringComparison.OrdinalIgnoreCase) >= 0
        && name.IndexOf("_Floor", StringComparison.OrdinalIgnoreCase) < 0
        && name.IndexOf("_Wall", StringComparison.OrdinalIgnoreCase) < 0
        && name.IndexOf("_UnderWall", StringComparison.OrdinalIgnoreCase) < 0
        && name.IndexOf("_Door", StringComparison.OrdinalIgnoreCase) < 0
        && name.IndexOf("_Arch", StringComparison.OrdinalIgnoreCase) < 0;

    private static bool IsHardStructuralName(string name)
    {
        if (IsNativeTreeAsset(name))
            return false;
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
        bool foliageDressing = IsNativeWallPlantLeaf(name)
                    || name.IndexOf("Bush", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("_Ivy", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("_Grass_", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.EndsWith("_Grass", StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("_Vines", StringComparison.OrdinalIgnoreCase) >= 0;
        return wall && !foliageDressing;
    }

    private static bool IsGrassBase(string name) =>
        name.StartsWith("FR_Floor_Grass_Half", StringComparison.Ordinal)
        || name.StartsWith("FR_Floor_Grass_Full", StringComparison.Ordinal)
        || name.StartsWith("FR_Floor_Grass_Seg_", StringComparison.Ordinal)
        || name == "FR_Floor_Grass";

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
        private readonly List<GameObject> _heldRoots = new(8);
        private readonly List<MeshRenderer> _heldMeshes = new(32);
        // An operation count, not a timer: runtime regression checks prove unrelated hidden
        // scenery is never traversed when a player merely holds one prop.
        private int _heldMeshChecks;

        internal bool IsOwnedHidden(Renderer renderer) =>
            _byId.TryGetValue(renderer.GetInstanceID(), out Record? record)
            && record.Owned && !record.Invalidated && ReferenceEquals(record.Renderer, renderer)
            && renderer.forceRenderingOff;
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
            UpdateSettings();
            if (!_inScenarioScene)
            {
                ScenarioDecorativePlacement.Refresh(complete: true);
                return;
            }

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
                // Fallback only when the native loading-close hook did not run. Its synchronous
                // completion clears this retry; normal reveals/material completions prepare first.
                // Subsequent rooms/regeneration use their native placement and ShowContent hooks.
                _settlePending = false;
                SeedTiles();
            }
            // A scene can consist entirely of deferred decoration and have no renderer
            // records yet. Restore its recipes even when every live budget returns to 100.
            ScenarioDecorativePlacement.Refresh(complete: false);
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

        internal void PrepareSubtree(GameObject root)
        {
            UpdateSettings();
            if (!_inScenarioScene || !BudgetActive || root == null) return;
            ProceduralMapTile? tile = TileAncestor(root.transform);
            if (tile == null || !IsScenarioTile(tile)) return;
            _actualScenario = true;
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            ColliderReadFacts.Clear(); TreeColliderReadFacts.Clear(); BayColliderReadFacts.Clear(); _colliderFactsActive = true;
            try
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    MeshRenderer renderer = renderers[i];
                    if (_visitedRendererIds.Add(renderer.GetInstanceID())) _meshRenderers++;
                    Examine(renderer, tile);
                }
            }
            finally { _colliderFactsActive = false; ColliderReadFacts.Clear(); TreeColliderReadFacts.Clear(); BayColliderReadFacts.Clear(); }
        }

        internal void PrepareMaterialReady(MeshRenderer renderer)
        {
            PrepareSubtree(renderer.gameObject);
            if (!_inScenarioScene || !BudgetActive || !renderer.enabled || renderer.forceRenderingOff
                || !renderer.gameObject.activeInHierarchy || UsesOnlyFoliage(renderer)) return;
            ProceduralMapTile? tile = TileAncestor(renderer.transform);
            if (tile == null || !IsScenarioTile(tile)) return;
            // Native MaterialLoaderData completes independently for each renderer. A grass
            // sibling may have been rejected while this solid floor was still disabled. Only
            // the nearest original shared box below Generated Content needs immediate recheck;
            // native tile/wall colliders are always represented and never require this walk.
            for (Transform? composite = renderer.transform.parent;
                 composite != null && composite.name != "Generated Content"; composite = composite.parent)
            {
                if (composite.GetComponent<ProceduralMapTile>() != null
                    || composite.GetComponent<ProceduralWall>() != null) continue;
                Collider[] colliders = composite.GetComponents<Collider>();
                bool present = false;
                for (int i = 0; i < colliders.Length; i++)
                    present |= colliders[i] != null && ColliderIsPresent(colliders[i]);
                if (!present) continue;
                if (RepresentsSolidComposite(renderer.transform, composite))
                    PrepareSubtree(composite.gameObject);
                return;
            }
        }

        private void UpdateSettings()
        {
            Scene procedural = Choreographer.s_Choreographer != null ? Choreographer.s_Choreographer.m_ProcGenScene : default;
            Scene scene = procedural.IsValid() && procedural.isLoaded ? procedural : SceneManager.GetActiveScene();
            if (_sceneHandle != scene.handle || _inScenarioScene != VRSession.IsRunning) EnterScene(scene);
            int grass = Mathf.Clamp(PerfConfig.ScenarioSceneryDensityPercentValue, 0, 100);
            int vegetation = Mathf.Clamp(PerfConfig.ScenarioVegetationDensityPercentValue, 0, 100);
            int decoration = Mathf.Clamp(PerfConfig.ScenarioDecorationDensityPercentValue, 0, 100);
            if (_density != grass || _vegetationDensity != vegetation || _decorationDensity != decoration)
                ChangeDensity(grass, vegetation, decoration);
        }

        internal void PrepareLoadingCompletion()
        {
            // Native WaitForProcGen has finished. Complete presentation work synchronously before
            // DisableLoadingScreen hides the indicator; never change flags or native continuation.
            // The former +2-second settled retry exposed thousands of budget changes after load.
            UpdateSettings();
            ScenarioDecorativePlacement.Refresh(complete: true);
            if (!_inScenarioScene || !BudgetActive) return;
            SeedTiles();
            while (_retuneIndex >= 0) Retune();
            WalkNodes(loading: true, complete: true);
            _wasLoading = false;
            _settlePending = false;
            _summaryDue = Time.unscaledTime;
            MaybeReport(loading: false);
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

        private void WalkNodes(bool loading, bool complete = false)
        {
            // Collider-unit reads are exact only within this synchronous discovery slice. No
            // shared parent subtree is walked again for every grass leaf, and no facts survive
            // native regeneration or a collider toggle between frames.
            ColliderReadFacts.Clear();
            TreeColliderReadFacts.Clear();
            BayColliderReadFacts.Clear();
            _colliderFactsActive = true;
            try { WalkNodesCore(loading, complete); }
            finally { _colliderFactsActive = false; ColliderReadFacts.Clear(); TreeColliderReadFacts.Clear(); BayColliderReadFacts.Clear(); }
        }

        private void WalkNodesCore(bool loading, bool complete)
        {
            int budget = complete ? int.MaxValue : loading ? LoadingNodesPerFrame : NodesPerFrame;
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
                    if (!complete && (budget & 3) == 0 && Time.realtimeSinceStartup >= deadline)
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
                if (!complete && (budget & 3) == 0 && Time.realtimeSinceStartup >= deadline)
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
                ReleaseTreeColliders(known);
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
                TreeColliders = TreeCollidersFor(renderer, unit),
                BayColliders = BayCollidersFor(renderer),
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
                // Native placement can reuse a leaf with a changed collider layout. Do not
                // flip unchanged collider masks on repeat discovery; acquire/release only a
                // changed ownership plan, with the original renderer mask still reversible.
                Collider[] currentColliders = TreeCollidersFor(renderer, unit);
                if (!SameColliders(record.TreeColliders, currentColliders))
                {
                    ReleaseTreeColliders(record);
                    record.TreeColliders = currentColliders;
                    if (record.Owned)
                        ClaimTreeColliders(record);
                }
                Collider[] currentBays = BayCollidersFor(renderer);
                if (!SameColliders(record.BayColliders, currentBays))
                {
                    SetHidden(record, false);
                    record.BayColliders = currentBays;
                }
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
            // Build 605 measured 4.6–8.0 ms scenery Update peaks. The held-prop path was a
            // candidate: testing every hidden plant against every hand scales with the whole level.
            // Enumerate the actual <= 2 local roots plus remote roots, then use the existing
            // renderer identity index. Rescue remains in this very frame, including inactive
            // LOD children and replaced/reparented remote visuals; figures have no scenery row.
            _heldMeshChecks = 0;
            if (HeldProps.Count > 0 || NetHeldProps.Any)
            {
                _heldRoots.Clear();
                for (int slot = 0; slot < HeldProps.Count; slot++)
                    if (HeldProps.TryGetSlot(slot, out _, out GameObject visual, out _, out _))
                        _heldRoots.Add(visual);
                NetHeldProps.CopyVisualRoots(_heldRoots);
                foreach (GameObject visual in _heldRoots)
                {
                    if (visual == null) continue;
                    _heldMeshes.Clear();
                    visual.GetComponentsInChildren(includeInactive: true, _heldMeshes);
                    foreach (MeshRenderer renderer in _heldMeshes)
                    {
                        _heldMeshChecks++;
                        if (renderer == null
                            || !_byId.TryGetValue(renderer.GetInstanceID(), out Record? record)
                            || !record.Owned) continue;
                        SetHidden(record, false);
                        record.Invalidated = true;
                    }
                }
                _heldRoots.Clear();
                _heldMeshes.Clear();
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
                ReleaseTreeColliders(record);
                for (int i = 0; i < record.BayColliders.Length; i++) RefreshBayCollider(record.BayColliders[i]);
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
            foreach (var bay in BayColliderOwners)
                if (bay.Value && bay.Key != null && !bay.Key.enabled) bay.Key.enabled = true;
            BayColliderOwners.Clear();
            _records.Clear();
            _heldRoots.Clear();
            _heldMeshes.Clear();
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
    private static void Prefix(GameObject o)
    {
        try { ScenarioSceneryBudget.BeforeContentShown(o); }
        catch { /* keep native room reveal available if presentation preparation fails */ }
    }
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

/// <summary>Finish only local decorative presentation before native loading-screen closure.</summary>
[HarmonyPatch(typeof(SceneController), nameof(SceneController.DisableLoadingScreen))]
internal static class SceneController_LoadingComplete_SceneryBudgetPatch
{
    private static void Prefix()
    {
        try { ScenarioSceneryBudget.BeforeLoadingComplete(); }
        catch (Exception e) { ScenarioSceneryBudget.LoadingFailure(e); }
    }
}
