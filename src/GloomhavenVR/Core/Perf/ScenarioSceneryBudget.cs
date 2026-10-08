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
/// Only owned false-to-true Renderer.forceRenderingOff writes and identified decorative
/// Projector.enabled masks are restored. Native Renderer.enabled, scripts, materials, property
/// blocks, room reveal and multiplayer state are never written.
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

    // A scalar view of real queued work, for the existing presentation loading indicator.
    // Permanent ancestry/ownership monitoring and delayed diagnostic summaries are not loads.
    internal static bool IsPreparingPresentation => _driver != null && _driver.IsPreparingPresentation;

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

    // Original PCG blood/dirt projectors are decorative paint, not mesh renderers. Keep the
    // native projector's material/pose/controller alive; own only its render-enable change.
    // Unknown decals, gameplay circles, toxic splats and figure effects never qualify.
    private sealed class ProjectorRecord
    {
        internal Projector Projector = null!;
        internal int Id;
        internal ProceduralMapTile Tile = null!;
        internal Transform[] Chain = null!;
        internal uint Hash;
        internal bool Owned;
        internal bool Invalidated;
    }

    private static bool IsDecorativeProjectorName(string name)
    {
        string original = name;
        // Unity clone suffixes and authored numeric duplicates retain the exact native family.
        // Arbitrary suffixes such as MagicCircle do not become decoration through prefix matching.
        for (int pass = 0; pass < 2; pass++)
        {
            if (original.EndsWith("(Clone)", StringComparison.Ordinal))
                original = original.Substring(0, original.Length - 7);
            int space = original.LastIndexOf(' ');
            if (space < 0 || space + 3 >= original.Length || original[space + 1] != (char)40
                || original[original.Length - 1] != (char)41) continue;
            bool numeric = true;
            for (int i = space + 2; i < original.Length - 1; i++)
                if (original[i] < '0' || original[i] > '9') { numeric = false; break; }
            if (numeric) original = original.Substring(0, space);
        }
        return original == "DECAL_BloodSplat_Proj_PR" || original == "DECAL_Dirt_Proj_PR"
            || original == "DECAL_FR_BloodSplat_Proj_PR" || original == "DECAL_FR_Dirt_Proj_PR";
    }

    private static bool IsDecorativeProjector(Projector projector, ProceduralMapTile tile)
    {
        if (projector == null || tile == null || !IsDecorativeProjectorName(projector.name)) return false;
        bool generated = false;
        for (Transform? node = projector.transform; node != null && node != tile.transform; node = node.parent)
        {
            CInteractable interactable = node.GetComponent<CInteractable>();
            UnityGameEditorObject native = node.GetComponent<UnityGameEditorObject>();
            if ((interactable != null && interactable is not CInteractableTile)
                || (native != null && native.PropObject != null)
                || node.GetComponent<ProceduralProp>() != null || node.GetComponent<ProceduralDoorway>() != null
                || node.GetComponent<UnityGameEditorDoorProp>() != null || FigureRendererGuard.CarriesFigureComponent(node)
                || node.GetComponent<Canvas>() != null || node.GetComponent<Light>() != null
                || node.GetComponent<Animator>() != null || node.GetComponent<ParticleSystem>() != null
                || node.GetComponent<Rigidbody>() != null || node.name == "Preview") return false;
            if (node == projector.transform && node.GetComponent<Collider>() != null) return false;
            if (node.name == "Generated Content") generated = true;
            if (node.parent == tile.transform) return generated;
        }
        return false;
    }

    private static void SetProjectorHidden(ProjectorRecord record, bool hide)
    {
        Projector projector = record.Projector;
        if (projector == null) { record.Owned = false; return; }
        if (hide && !record.Owned && projector.enabled)
        {
            projector.enabled = false;
            record.Owned = true;
        }
        else if (!hide && record.Owned)
        {
            if (!projector.enabled) projector.enabled = true;
            record.Owned = false;
        }
    }

    private static bool SameProjectorChain(ProjectorRecord record)
    {
        Transform? current = record.Projector != null ? record.Projector.transform : null;
        if (current == null) return false;
        for (int i = 0; i < record.Chain.Length; i++)
        {
            if (current == null || !ReferenceEquals(current, record.Chain[i])) return false;
            current = current.parent;
        }
        return true;
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
        MeshFilter originalFilter = renderer.GetComponent<MeshFilter>();
        Mesh? originalMesh = originalFilter != null ? originalFilter.sharedMesh : null;
        bool smallDressing = originalMesh != null && IsNativeSmallDressing(originalMesh.name);
        // The global figure guard deliberately treats every Animator as a figure. Only this
        // exact original-small-mesh seam may distinguish an empty decorative Animator; the
        // explicit actor/interactable and live-controller tests below remain absolute.
        bool ancestrySafe = (smallDressing ? !HasSmallDressingFigureAncestor(renderer)
                                          : !FigureRendererGuard.IsFigureOrActorRenderer(renderer))
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
                || (!smallDressing && FigureRendererGuard.CarriesFigureComponent(t))
                || t.GetComponent<ActorBehaviour>() != null || t.GetComponent<CInteractableActor>() != null
                || t.GetComponent<Canvas>() != null
                || t.GetComponent<Light>() != null
                || BlocksSmallDressingAnimation(t, smallDressing)
                || IsLargeDecorativeUnit(t.name)
                || (smallDressing && !generated && HasUnknownSmallDressingCallback(t))
                || (smallDressing && t.GetComponent<Rigidbody>() != null)
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
        if (BlocksSmallDressingAnimation(renderer.transform, smallDressing)
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
        // A real ornamental child can live beneath a structural parent. Its exact original
        // mesh identity authorises only that child, never the parent's geometry or collider.
        // This is the skull/bone layer of necropolis floors and walls reported on Build619.
        if (IsNativeCompositeDressing(mesh.name) || smallDressing)
        {
            unit = renderer.transform;
            kind = Kind.Dressing;
            structural = false;
        }
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
        string assetName = meshFamily || smallDressing ? mesh.name : TreeAssetName(unit.name);
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
            || (!collider.name.StartsWith("FR_Default_Bay_", StringComparison.Ordinal)
                && !NativeSmallDressingContainers.Contains(OriginalAssetName(collider.name))
                && !IsNativeSmallDressing(OriginalAssetName(collider.name)))) return false;
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
        if (!IsNativeSceneryAsset(nativeRoot)
            && !NativeSmallDressingContainers.Contains(OriginalAssetName(nativeRoot))
            && !IsNativeSmallDressing(nativeRoot)) return 0;
        int categories = 0;
        bool tree = IsNativeTreeAsset(nativeRoot);
        Transform[] nodes = root.transform.GetComponentsInChildren<Transform>(includeInactive: true);
        for (int i = 0; i < nodes.Length; i++)
        {
            Transform t = nodes[i];
            if (IsLargeDecorativeUnit(t.name)) return 0;
            Component[] components = t.GetComponents<Component>();
            for (int j = 0; j < components.Length; j++)
            {
                Component component = components[j];
                if (component == null) return 0; // missing native script is not a proven decorative prefab
                if (component is Transform || component is MeshFilter || component is MeshRenderer
                    || component is LODGroup) continue;
                if (component is Animator animation && animation.runtimeAnimatorController == null
                    && IsExactSmallSubtree(t)) continue;
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
            if (mesh == null || (!IsNativeSceneryAsset(mesh.name) && !IsNativeSmallDressing(mesh.name))) return 0;
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
            if (kind == Kind.None || (NamedKind(t.name) == Kind.None && !IsNativeSmallDressing(mesh.name))) return 0;
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
            if (IsHardStructuralName(t.name) || IsGrassBase(t.name) || IsLargeDecorativeUnit(t.name))
                return true;
            if (NamedKind(t.name) != Kind.None)
                return false;
            MeshFilter filter = t.GetComponent<MeshFilter>();
            Mesh? mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null && IsNativeSceneryAsset(mesh.name))
            {
                if (IsHardStructuralName(mesh.name) || IsGrassBase(mesh.name) || IsNativeGroundCore(mesh.name) || NativeRetainedFurnitureCores.Contains(mesh.name))
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
        || name.StartsWith("EN_", StringComparison.Ordinal)
        || name.StartsWith("TO_", StringComparison.Ordinal)
        || name.StartsWith("ST_", StringComparison.Ordinal)
        || name.StartsWith("SB_", StringComparison.Ordinal)
        || name.StartsWith("SE_", StringComparison.Ordinal)
        || name.StartsWith("CS_", StringComparison.Ordinal)
        || name.StartsWith("CT_", StringComparison.Ordinal)
        || name.StartsWith("DLC_", StringComparison.Ordinal)
        || name.StartsWith("PR_", StringComparison.Ordinal)
        || name.StartsWith("GH_", StringComparison.Ordinal);

    // The original generic floor cores also represent their shared prefab collision. Their
    // names omit Base/Basic, so an unrepresented-box veto used to retain the detachable bones
    // despite correct ornament identity. This closed census grants representation only to
    // retained original ground meshes, never to an arbitrary floor-named decoration.
    private static readonly HashSet<string> NativeGroundCores = new(StringComparer.Ordinal)
    {
        "CR_Dungeon_Floor_Seg_J",
        "CR_Dungeon_Floor_Seg_L",
        "CR_Dungeon_Floor_Seg_P",
        "CR_EXT_Stone_Floor_01",
        "CR_EXT_Stone_Floor_01_Half_01",
        "CR_EXT_Stone_Floor_01_Half_02",
        "CR_EXT_Stone_Floor_01_Seg_J",
        "CR_EXT_Stone_Floor_01_Seg_L",
        "CR_EXT_Stone_Floor_01_Seg_P",
        "CR_EXT_Stone_Floor_04",
        "CR_EXT_Stone_Floor_06",
        "CR_EXT_Stone_Floor_07_Top",
        "CR_EXT_Stone_Floor_07_Well",
        "CR_EXT_Stone_Floor_BAY",
        "CR_FR_Floor_Mud_01",
        "CR_FR_Floor_Mud_02",
        "CR_FR_Floor_Mud_03",
        "CR_FR_Floor_Mud_04",
        "CR_FR_Floor_Wood",
        "CR_FR_Floor_Wood_01",
        "CR_FR_Floor_Wood_02",
        "CR_FR_Floor_Wood_03",
        "CR_INT_Marble_Floor_01",
        "CR_INT_Marble_Floor_03",
        "CR_INT_Marble_Floor_04",
        "CR_INT_Marble_Floor_Half_01",
        "CR_INT_Marble_Floor_Half_02",
        "CR_INT_Marble_Floor_Seg_J",
        "CR_INT_Marble_Floor_Seg_L",
        "CR_INT_Marble_Floor_Seg_P",
        "CR_INT_Shack_Wood_Floor_Half_01",
        "CR_INT_Shack_Wood_Floor_Half_02",
        "CR_INT_Shack_Wood_Floor_Seg_J",
        "CR_INT_Shack_Wood_Floor_Seg_L",
        "CR_INT_Shack_Wood_Floor_Seg_P",
        "CR_INT_Stone_Floor_01",
        "CR_INT_Stone_Floor_01_Seg_J",
        "CR_INT_Stone_Floor_01_Seg_L",
        "CR_INT_Stone_Floor_01_Seg_P",
        "CR_INT_Stone_Floor_01_b",
        "CR_INT_Stone_Floor_01_c",
        "CR_INT_Stone_Floor_01_d",
        "CR_INT_Stone_Floor_01_e",
        "CR_INT_Stone_Floor_02",
        "CR_INT_Stone_Floor_02_Half_01",
        "CR_INT_Stone_Floor_02_Half_02",
        "CR_INT_Stone_Floor_03",
        "CR_INT_Stone_Floor_03_Half_01",
        "CR_INT_Stone_Floor_03_Half_02",
        "CR_INT_Stone_Floor_03_Seg_P",
        "CR_INT_Stone_Floor_04",
        "CR_INT_Stone_Floor_04_Half_01",
        "CR_INT_Stone_Floor_04_Half_02",
        "CR_INT_Stone_Floor_04_Seg_P",
        "CR_INT_Stone_Floor_05",
        "CR_INT_Wood_Floor_01",
        "CR_INT_Wood_Floor_02",
        "CR_INT_Wood_Floor_03",
        "CR_INT_Wood_Floor_04",
        "CR_INT_Wood_Floor_06",
        "CR_INT_Wood_Floor_07",
        "CR_INT_Wood_Floor_Basic_Half_01",
        "CR_INT_Wood_Floor_Half_02",
        "CR_INT_Wood_Floor_Seg_J",
        "CR_INT_Wood_Floor_Seg_L",
        "CR_INT_Wood_Floor_Seg_P",
        "CR_OS_Floor_01",
        "CR_OS_Floor_02",
        "CR_OS_Floor_03",
        "CR_OS_Floor_04",
        "CR_OS_Floor_Basic_Half_01",
        "CR_OS_Floor_Basic_Half_02",
        "CR_OS_Floor_Basic_Seg_J",
        "CR_OS_Floor_Basic_Seg_L",
        "CR_OS_Floor_Basic_Seg_P",
        "CR_RU_Floor_01_New",
        "CR_RU_Floor_01_New_Half_01",
        "CR_RU_Floor_01_New_Seg_J",
        "CR_RU_Floor_01_New_Seg_L",
        "CR_RU_Floor_01_New_Seg_P",
        "CR_RU_Floor_02_New",
        "CR_RU_Floor_03_New",
        "CR_RU_Floor_RockSmall_New_01",
        "CR_RU_Floor_RockSmall_New_02",
        "CR_RU_Floor_RockSmall_New_03",
        "CR_RU_Floor_Rock_New_01",
        "CR_RU_Floor_Rock_New_02",
        "CR_RU_Floor_Rock_New_03",
        "CR_RU_Floor_Rock_New_04",
        "CR_RU_StoneBlock_Floor_01",
        "CR_RU_StoneBlock_Floor_02",
        "CR_ST_Floor_Basic_Half_01",
        "CR_ST_Floor_Basic_Half_02",
        "CR_ST_Floor_Basic_Seg_J",
        "CR_ST_Floor_Basic_Seg_L",
        "CR_ST_Floor_Basic_Seg_P",
        "CR_ST_Floor_WeaponRack_01",
        "CR_ST_Floor_WeaponRack_02",
        "CR_ST_Floor_WeaponRack_Small_01",
        "CR_ST_Floor_WeaponRack_Small_02",
        "CR_ST_Floor_Weapon_01",
        "CR_ST_Floor_Weapon_02",
        "CR_ST_Floor_Weapon_Small_01",
        "CR_ST_Floor_Weapon_Small_02",
        "CR_TC_Floor_01",
        "CR_TC_Floor_01_Base",
        "CR_TC_Floor_Basic",
        "CR_TC_Floor_Basic_Half_01",
        "CR_TC_Floor_Basic_Half_02",
        "CR_TC_Floor_DoorStep",
        "CR_TC_Floor_Hole_Complete",
        "CR_TC_Floor_Metal",
        "CR_TC_Floor_Plinth",
        "CV_Floor_Basic_01",
        "CV_Floor_Basic_02",
        "CV_Floor_Basic_03",
        "CV_Floor_Generic_Half_01",
        "CV_Floor_Generic_Half_02",
        "CV_Floor_Generic_Seg_J",
        "CV_Floor_Generic_Seg_L",
        "CV_Floor_Generic_Seg_P",
        "CV_Floor_HexOutline_Rock_01",
        "CV_Floor_HexOutline_Rock_02",
        "CV_Floor_HexOutline_Rock_03",
        "CV_Floor_HexOutline_Rock_04",
        "CV_Floor_MetalOre_01",
        "CV_Floor_MetalOre_02",
        "CV_Floor_MetalOre_03",
        "CV_Floor_MetalOre_04",
        "CV_Floor_MetalOre_05",
        "CV_Floor_MetalOre_06",
        "CV_Floor_MetalOre_07",
        "CV_Floor_Volcanic_01",
        "CV_Floor_Volcanic_04",
        "CV_Floor_Volcanic_05",
        "DLC_SB_Plaform_Floor_Tracks_01",
        "EN_CR_Floor_BaseHex_Plain",
        "FR_CW_Floor_Dirt_Half_01",
        "FR_CW_Floor_Dirt_Half_02",
        "FR_CW_Floor_Dirt_Seg_J",
        "FR_CW_Floor_Dirt_Seg_L",
        "FR_CW_Floor_Dirt_Seg_P",
        "FR_CW_Floor_Toadstool_01",
        "FR_CW_Floor_Toadstool_02",
        "FR_CW_Floor_Toadstool_03",
        "FR_CW_Floor_Toadstool_Base_01",
        "FR_CW_Floor_Toadstool_Base_02",
        "FR_CW_Floor_Toadstool_Base_03",
        "SE_Gothic_Floor_01",
        "SE_Gothic_Floor_02",
        "SE_Gothic_Floor_03",
        "SE_Gothic_Floor_04",
        "SE_Gothic_Floor_06",
        "SE_Gothic_Floor_07",
        "SE_Gothic_Floor_08",
        "SE_Gothic_Floor_09",
        "SE_Gothic_Floor_Half_01",
        "SE_Gothic_Floor_Half_02",
        "SE_Gothic_Floor_Seg_J",
        "SE_Gothic_Floor_Seg_L",
        "SE_Gothic_Floor_Seg_P",
        "SE_Rot_01_Floor_Under",
        "SE_Rot_Floor_01",
        "SE_Rot_Floor_02",
        "SE_Rot_Floor_03",
        "SE_Rot_Floor_04",
    };

    private static bool IsNativeGroundCore(string name) => NativeGroundCores.Contains(name);

    // Build620 reviews the original renderer/mesh pairs across all PCG databases. These
    // meshes are detached ornaments beside an authored floor/wall/pillar core, not the core
    // itself. A broad "Bone", "Rubble" or "Wall" exception would also erase real floors.
    // NativeDetailProvenance pins their actual prefab ancestry and independent core identity.
    private static readonly HashSet<string> NativeCompositeDressing = new(StringComparer.Ordinal)
    {
        "CR_INT_Stone_Floor_01_Rubble_01",
        "CR_INT_Stone_Floor_01_Rubble_02",
        "CR_INT_Stone_Floor_01_Rubble_03",
        "CR_INT_Stone_Floor_01_Rubble_04",
        "CR_INT_Stone_Floor_01_Rubble_05",
        "CR_OS_Floor_01_Bones",
        "CR_OS_Floor_02_Bones",
        "CR_OS_Floor_02_Skulls",
        "CR_OS_Floor_03_Bones",
        "CR_OS_Floor_04_Bones",
        "CR_OS_Floor_Basic_Half_01_Bone",
        "CR_OS_Floor_Basic_Half_02_Skull",
        "CR_OS_Floor_Basic_Seg_J_Bone",
        "CR_OS_Floor_Basic_Seg_L_Bone",
        "CR_OS_Floor_Basic_Seg_P_Bone",
        "CR_OS_Floor_Bone_01",
        "CR_OS_Floor_Detail_Bones_03",
        "CR_OS_Pillar_01_New_Skulls",
        "CR_OS_Pillar_LOD0_02_Skull00",
        "CR_OS_Pillar_LOD0_02_Skull01",
        "CR_OS_Pillar_LOD0_02_Skull02",
        "CR_OS_Pillar_LOD0_02_Skull03",
        "CR_OS_Pillar_LOD0_02_Skull04",
        "CR_OS_Pillar_LOD0_02_Skull05",
        "CR_OS_Pillar_LOD0_02_Skull06",
        "CR_OS_Pillar_LOD0_02_Skull07",
        "CR_OS_Pillar_LOD0_02_Skull08",
        "CR_OS_Pillar_LOD0_02_Skull09",
        "CR_OS_Pillar_LOD0_02_Skull10",
        "CR_OS_Pillar_LOD0_02_Skull11",
        "CR_OS_Pillar_LOD0_02_Skull12",
        "CR_OS_Pillar_LOD0_02_Skull13",
        "CR_OS_Pillar_LOD0_02_Skull14",
        "CR_OS_Pillar_LOD0_02_Skull15",
        "CR_OS_Pillar_LOD0_02_Skull16",
        "CR_OS_Pillar_LOD1_02_Skull00",
        "CR_OS_Pillar_LOD1_02_Skull01",
        "CR_OS_Pillar_LOD1_02_Skull02",
        "CR_OS_Pillar_LOD1_02_Skull03",
        "CR_OS_Pillar_LOD1_02_Skull04",
        "CR_OS_Pillar_LOD1_02_Skull05",
        "CR_OS_Pillar_LOD1_02_Skull06",
        "CR_OS_Pillar_Large_Bones",
        "CR_OS_Pillar_Large_Skulls",
        "CR_OS_Wall_01_Bones",
        "CR_OS_Wall_01_Skulls",
        "CR_OS_Wall_02_Skull",
        "CR_OS_Wall_03_Skulls",
        "CR_OS_Wall_06_Skulls",
        "CR_OS_Wall_08_Skulls",
        "CR_OS_Wall_DoorFrame_Thin_01_Bone",
        "CR_OS_Wall_DoorFrame_Thin_01_Skull",
        "CR_OS_Wall_Thin_01_Bone",
        "CR_OS_Wall_Thin_01_Bones",
        "CR_ST_FloorShelf_Stone_Bone_Skull",
        "CR_ST_Shelves_Stone_Bone_Bone",
        "CR_ST_SmallShelf_Stone_Bone_Bone",
        "CR_ST_WallShelf_Stone_Bone_Bone",
        "CR_ST_WallShelf_Stone_Bone_Skull",
        "TERRAIN_Crypt_Rubble_Bits",
        "TERRAIN_DU_Rubble_Skulls",
        "TERRAIN_Town_Ext_Rubble_Bits",
        "TERRAIN_Town_Rubble_Bits",
        "TERR_Forest_Rubble_Stones",
    };

    private static bool IsNativeCompositeDressing(string name) => NativeCompositeDressing.Contains(name);

    // Closed original mesh/container identities from the whole-game PCG audit. A name token
    // is only a review candidate; this table is the positive detached-small-member proof.
    private static readonly HashSet<string> NativeSmallDressing = new(StringComparer.Ordinal)
    {
        "Book_03",
        "Book_06",
        "Bottle_01",
        "Bottle_02",
        "Bottle_03",
        "CR_INT_Floor_Clutter_Bits_Stone_01",
        "CR_INT_Floor_Clutter_Bits_Stone_02",
        "CR_INT_Floor_Clutter_Bits_Stone_03",
        "CR_INT_Floor_Clutter_Bits_Stone_05",
        "CR_INT_Floor_Clutter_Bits_Stone_4",
        "CR_INT_Floor_Clutter_Bits_Stone_Edge",
        "CR_INT_Floor_Clutter_Bits_Stone_Edge_02",
        "CR_INT_Floor_Clutter_Parchment_02",
        "CR_INT_Floor_Clutter_Pots_01",
        "CR_INT_Floor_Clutter_Pots_02",
        "CR_INT_Floor_Clutter_Pots_03",
        "CR_INT_Floor_Clutter_Pots_05",
        "CR_INT_Floor_Clutter_Roof_05",
        "CR_INT_Floor_Clutter_Roof_07",
        "CR_INT_Floor_Clutter_Straw_01",
        "CR_INT_Floor_Clutter_Straw_02",
        "CR_INT_Floor_Clutter_Straw_03",
        "CR_INT_Floor_Clutter_Straw_04",
        "CR_INT_Floor_Clutter_Weapons_01",
        "CR_INT_Floor_Clutter_Weapons_03",
        "CR_INT_Floor_Clutter_Weapons_04",
        "CR_INT_Floor_Clutter_Weapons_05",
        "CR_INT_Floor_Clutter_Weapons_06",
        "CR_INT_Floor_Clutter_Wood_Debris_01",
        "CR_INT_Floor_Clutter_Wood_Debris_02",
        "CR_INT_Floor_Clutter_Wood_Debris_03",
        "CR_INT_Floor_Clutter_Wood_Debris_04",
        "CR_INT_Floor_Clutter_Wood_Debris_05",
        "CR_INT_Floor_Clutter_Wood_Debris_06",
        "CR_INT_Floor_Clutter_Wood_Debris_07",
        "CR_INT_Roof_Ceramic_Debris_01",
        "CR_INT_Roof_Ceramic_Debris_02",
        "CR_INT_Roof_Wood_Debris_01",
        "CR_INT_Roof_Wood_Debris_02",
        "CR_INT_Roof_Wood_Debris_03",
        "CR_OS_Floor_Clutter_Corner_M_01",
        "CR_OS_Floor_Clutter_Edge_M_01",
        "CR_OS_Floor_Clutter_M_01",
        "CR_OS_Floor_Clutter_M_02",
        "CR_OS_Floor_Clutter_M_03",
        "CR_OS_Floor_Clutter_S_01",
        "CR_OS_Floor_Clutter_S_02",
        "CR_OS_Skull_Half",
        "CR_OS_Skull_Low",
        "CR_RU_Stone_Urn_02_Broke",
        "CR_RU_Stone_Urn_05_Broke",
        "CR_RU_Vase_Bottom",
        "CR_RU_Vase_Middle",
        "CR_RU_Vase_Top",
        "CR_ST_Shelf_Alchemy_Balance",
        "CR_ST_Shelf_Alchemy_Book_01",
        "CR_ST_Shelf_Alchemy_Books_04",
        "CR_ST_Shelf_Alchemy_Jugs_01",
        "CR_ST_Shelf_Alchemy_Jugs_02",
        "CR_ST_Shelf_Alchemy_Jugs_03",
        "CR_ST_Shelf_Alchemy_Jugs_04",
        "CR_ST_Shelf_Alchemy_Jugs_05",
        "CR_ST_Shelf_Alchemy_Jugs_06",
        "CR_ST_Shelf_Alchemy_Oiler_01",
        "CR_ST_Shelf_Alchemy_Oiler_02",
        "CR_ST_Shelf_Alchemy_Plate_01",
        "CR_ST_Shelf_Alchemy_Plate_02",
        "CR_ST_Shelf_Alchemy_Plate_03",
        "CR_ST_Shelf_Alchemy_Plate_04",
        "CR_ST_Shelf_Alchemy_Plate_05",
        "CR_ST_Shelf_Alchemy_Scrolls_01",
        "CR_ST_Shelf_Alchemy_Scrolls_03",
        "CR_ST_Shelf_Alchemy_Scrolls_05",
        "CR_ST_Shelf_Book_01",
        "CR_ST_Shelf_Book_02",
        "CR_ST_Shelf_Book_03",
        "CR_ST_Shelf_Book_04",
        "CR_ST_Shelf_Book_05",
        "CR_ST_Shelf_Book_06",
        "CR_ST_Shelf_Book_07",
        "CR_ST_Shelf_Books_01",
        "CR_ST_Shelf_Books_02",
        "CR_ST_Shelf_Books_03",
        "CR_ST_Shelf_Books_04",
        "CR_ST_Shelf_Books_05",
        "CR_ST_Shelf_Books_Sparse_01",
        "CR_ST_Shelf_Books_Sparse_02",
        "CR_ST_Shelf_Books_Sparse_03",
        "CR_ST_Shelf_Books_Sparse_04",
        "CR_ST_Shelf_Books_Sparse_05",
        "CR_ST_Shelf_Druidic_Cup_01",
        "CR_ST_Shelf_Druidic_Cup_02",
        "CR_ST_Shelf_Druidic_Cup_03",
        "CR_ST_Shelf_Druidic_Cup_04",
        "CR_ST_Shelf_Druidic_Cup_05",
        "CR_ST_Shelf_Druidic_Jugs_01",
        "CR_ST_Shelf_Druidic_Jugs_02",
        "CR_ST_Shelf_Druidic_Plate_01",
        "CR_ST_Shelf_Floor_Pot_Detail",
        "CR_ST_Shelf_KitchenItems_Plate_01",
        "CR_ST_Shelf_KitchenItems_Plate_02",
        "CR_ST_Shelf_KitchenItems_Pots_04",
        "CR_ST_Shelf_KitchenItems_Pots_05",
        "CR_ST_Shelf_Pot_Detail",
        "CR_ST_Shelf_Pots_01",
        "CR_ST_Shelf_Pots_01_Seg_01",
        "CR_ST_Shelf_Pots_01_Seg_02",
        "CR_ST_Shelf_Pots_02",
        "CR_ST_Shelf_Pots_02_Seg_01",
        "CR_ST_Shelf_Pots_02_Seg_02",
        "CR_ST_Shelf_Pots_03",
        "CR_ST_Shelf_Pots_03_Seg_01",
        "CR_ST_Shelf_Pots_03_Seg_02",
        "CR_ST_Shelf_Pots_04",
        "CR_ST_Shelf_Pots_04_Seg_01",
        "CR_ST_Shelf_Pots_04_Seg_02",
        "CR_ST_Shelf_Pots_05",
        "CR_ST_Shelf_Pots_05_Seg_01",
        "CR_ST_Shelf_Pots_05_Seg_02",
        "CR_ST_Shelf_Pots_05_Seg_03",
        "CR_ST_Shelf_Pots_06",
        "CR_ST_Shelf_Pots_06_Seg_01",
        "CR_ST_Shelf_Pots_06_Seg_02",
        "CR_ST_Shelf_Scrolls_01",
        "CR_ST_Shelf_Scrolls_02",
        "CR_ST_Shelf_Scrolls_03",
        "CR_ST_Shelf_Scrolls_04",
        "CR_ST_Shelf_Scrolls_05",
        "CR_ST_Shelf_Scrolls_Sparse_01",
        "CR_ST_Shelf_Scrolls_Sparse_02",
        "CR_ST_Shelf_Scrolls_Sparse_03",
        "CR_ST_Shelf_Scrolls_Sparse_04",
        "CR_ST_Shelf_Scrolls_Sparse_05",
        "CR_ST_Shelf_Scrolls_V_01",
        "CR_ST_Shelf_Scrolls_V_02",
        "CR_ST_Shelf_Scrolls_V_03",
        "CR_ST_Shelf_Scrolls_V_04",
        "CR_ST_Shelf_Scrolls_V_05",
        "CR_ST_Shelf_Scrolls_V_06",
        "CR_ST_Shelf_Skulls_01",
        "CR_ST_Shelf_Skulls_02",
        "CR_ST_Shelf_Skulls_03",
        "CR_ST_Shelf_Skulls_04",
        "CR_ST_Shelf_Skulls_05",
        "CR_ST_Shelf_Skulls_Broken_01",
        "CR_ST_Shelf_Skulls_Broken_02",
        "CR_ST_Shelf_Skulls_Broken_03",
        "CR_ST_Shelf_Skulls_Broken_04",
        "CR_ST_Shelf_Skulls_Sparse_01",
        "CR_ST_Shelf_Skulls_Sparse_02",
        "CR_ST_Shelf_Skulls_Sparse_03",
        "CR_ST_Shelf_Skulls_Sparse_04",
        "CR_ST_Shelf_Skulls_Sparse_05",
        "CR_ST_Shelf_UrnsPots_01",
        "CR_ST_Shelf_UrnsPots_02",
        "CR_ST_Shelf_UrnsPots_03",
        "CR_ST_Shelf_UrnsPots_04",
        "CR_ST_Shelf_UrnsPots_05",
        "CR_ST_Shelf_UrnsPots_Sparse_01",
        "CR_ST_Shelf_UrnsPots_Sparse_02",
        "CR_ST_Shelf_UrnsPots_Sparse_03",
        "CR_ST_Shelf_UrnsPots_Sparse_04",
        "CR_ST_Shelf_UrnsPots_Sparse_05",
        "CR_ST_Shelf_Urns_01",
        "CR_ST_Shelf_Urns_01_Seg_01",
        "CR_ST_Shelf_Urns_01_Seg_02",
        "CR_ST_Shelf_Urns_02",
        "CR_ST_Shelf_Urns_02_Seg_01",
        "CR_ST_Shelf_Urns_02_Seg_02",
        "CR_ST_Shelf_Urns_03",
        "CR_ST_Shelf_Urns_03_Seg_01",
        "CR_ST_Shelf_Urns_03_Seg_02",
        "CR_ST_Shelf_Urns_04",
        "CR_ST_Shelf_Urns_04_Seg_01",
        "CR_ST_Shelf_Urns_04_Seg_02",
        "CR_ST_Writing_Desk_Books_01",
        "CR_ST_Writing_Desk_Books_02",
        "CR_ST_Writing_Desk_Small_Books_01",
        "CR_TC_WallChains_01",
        "CR_TC_WallChains_02",
        "CR_TC_WallChains_03",
        "CR_TC_WallChains_04",
        "CV_Floor_Scatter_03",
        "CV_Floor_Scatter_03_LOD",
        "CV_Floor_Scatter_04",
        "CV_Floor_Scatter_04_LOD",
        "CV_Floor_Scatter_05",
        "CV_Floor_Scatter_05_LOD",
        "CV_Floor_Scatter_06",
        "CV_Floor_Scatter_06_LOD",
        "CV_Floor_Scatter_07",
        "CV_Floor_Scatter_07_LOD",
        "CV_Floor_Scatter_08",
        "CV_Floor_Scatter_08_LOD",
        "CV_Floor_Scatter_09",
        "CV_Floor_Scatter_09_LOD",
        "CV_Floor_Scatter_10",
        "CV_Floor_Scatter_10_LOD",
        "CV_Ice_Clutter_01",
        "CV_Ice_Clutter_02",
        "CV_Ice_Clutter_03",
        "CV_Lair_Bear_Bone_01",
        "CV_Lair_Bear_Bone_02",
        "DLC_SB_Gothic_Clutter_StoneBowl",
        "DLC_SB_Gothic_Clutter_StoneBowl_Half",
        "DLC_Th_Slaughter_Bone_01",
        "DLC_Th_Slaughter_Bone_02",
        "DLC_Th_Slaughter_Bone_03",
        "DLC_Th_Slaughter_Skull_01b",
        "DLC_Th_Slaughter_Skull_01c",
        "DLC_Th_Slaughter_Skull_02",
        "EN_CR_Cup_01",
        "EN_CR_Skull",
        "EN_CR_Vase_02",
        "FR_BG_Bone_01",
        "FR_BG_Bone_02",
        "FR_BG_Bone_03",
        "FR_BG_Bone_04",
        "FR_BG_Bone_05",
        "FR_BG_Bone_06",
        "Flask_01",
        "Flask_02",
        "Flask_03",
        "Flask_05",
        "Gothic_Clutter_StoneBlock_01",
        "Inkpot_01",
        "PR_CR_OS_Candelabra_Skull_01",
        "PR_CR_OS_Candelabra_Skull_02",
        "PR_CR_OS_Candelabra_Skull_03",
        "Potion_01",
        "Potion_02",
        "SE_Gothic_Bone_01",
        "SE_Gothic_Bone_02",
        "SE_Gothic_Cobwebs_01",
        "SE_Gothic_Cobwebs_02",
        "SE_Gothic_Features_Vase_02",
        "ST_Cult_Clutter_01",
        "ST_Cult_Clutter_02",
        "ST_Cult_Clutter_03",
        "ST_Cult_Clutter_Chain",
        "ST_Demon_Plinth_Skulls",
        "ST_FireDemon_Skull",
        "ST_Skel_Clutter_01",
        "ST_Skel_Clutter_02",
        "ST_Skel_Clutter_03",
        "ST_Skel_Clutter_04",
        "ST_Skel_Clutter_05",
        "ST_Skel_Clutter_07",
        "ST_Skel_Clutter_08",
        "ST_Skel_Clutter_09",
        "ST_Skel_Clutter_10",
        "ST_Skel_Clutter_11",
        "ST_Skel_Clutter_12",
        "ST_TownMilitia_Inkpot_01",
        "ST_TownMilitia_Paper_01",
        "ST_TownMilitia_Paper_02",
        "ST_TownMilitia_Paper_03",
        "TO_INT_Cups_01",
        "TO_INT_Cups_03",
        "TO_INT_Cups_04",
        "TO_INT_Cups_05",
        "TO_INT_Cups_06",
        "TO_INT_Floor_Clutter_Chess_P02_01",
        "TO_INT_Floor_Clutter_Chess_P02_02",
        "TO_INT_Floor_Clutter_Food_P02_02",
        "TO_INT_Floor_Clutter_Food_P02_03",
        "TO_INT_Floor_Clutter_Pages_03",
        "TO_INT_Floor_Clutter_Pages_P02_01",
        "TO_INT_Floor_Clutter_Pages_P02_02",
        "TO_INT_Floor_Clutter_Paint_P02_01",
        "TO_INT_Floor_Clutter_Сards_P02_01",
        "TO_INT_Floor_Clutter_Сards_P02_02",
        "TO_INT_Scroll_01",
        "geo_bone_segment_01",
        "geo_bone_segment_02",
        "geo_bone_segment_03",
    };

    private static readonly HashSet<string> NativeSmallDressingContainers = new(StringComparer.Ordinal)
    {
        "Armoury_Bay_Sm_02",
        "BurialChamber_Bay_Sm_01",
        "BurialChamber_Bay_Sm_02",
        "BurialChamber_Bay_Sm_03",
        "CP_Prop_Sc_31_RockColumn",
        "CP_Prop_Scen58_Bones",
        "CP_Prop_Well_1Hex",
        "CP_Sc82_Bay_01_Savvas_Corpse",
        "CR_Dungeon_Bay_02",
        "CR_Dungeon_Bay_03",
        "CR_Dungeon_Bay_04",
        "CR_Dungeon_Bay_06",
        "CR_Dungeon_Clutter_Pages_03_PR",
        "CR_Dungeon_Entrance_Thick_PR",
        "CR_Dungeon_Entrance_Thin_PR",
        "CR_Dungeon_Frames_01_PR",
        "CR_Dungeon_Frames_02_PR",
        "CR_Dungeon_Frames_03_PR",
        "CR_Dungeon_Frames_04_PR",
        "CR_Dungeon_Obst_1Hex_02_PR",
        "CR_Dungeon_Underground_01_PR",
        "CR_Dungeon_Underground_02_PR",
        "CR_Dungeon_Underground_03_PR",
        "CR_Dungeon_Underground_04_PR",
        "CR_EXT_Stone_Floor_02b_PR",
        "CR_GE_Book_03",
        "CR_INT_Floor_Clutter_Bits_Stone_01_PR",
        "CR_INT_Floor_Clutter_Bits_Stone_02_PR",
        "CR_INT_Floor_Clutter_Bits_Stone_05_PR",
        "CR_INT_Floor_Clutter_Bits_Stone_Edge_02_PR",
        "CR_INT_Floor_Clutter_Bits_Stone_Edge_PR",
        "CR_INT_Floor_Clutter_Pots_05_PR",
        "CR_INT_Floor_Clutter_Roof_05_PR",
        "CR_INT_Floor_Clutter_Roof_07_PR",
        "CR_INT_Floor_Clutter_Straw_03_PR",
        "CR_INT_Floor_Clutter_Weapons_03_PR",
        "CR_INT_Floor_Clutter_Weapons_04_PR",
        "CR_INT_Floor_Clutter_Weapons_05_PR",
        "CR_INT_Floor_Clutter_Weapons_06_PR",
        "CR_INT_Floor_Clutter_Wood_Debris_03_PR",
        "CR_INT_Floor_Clutter_Wood_Debris_04_PR",
        "CR_INT_Floor_Clutter_Wood_Debris_05_PR",
        "CR_INT_Floor_Clutter_Wood_Debris_06_PR",
        "CR_INT_Floor_Clutter_Wood_Debris_07_PR",
        "CR_INT_Roof_Ceramic_Debris_01_PR",
        "CR_INT_Roof_Wood_Debris_01_PR",
        "CR_INT_Roof_Wood_Debris_03_PR",
        "CR_LIbrary_Obst_2Hex_01_PR",
        "CR_LIbrary_Obst_2Hex_02_PR",
        "CR_LIbrary_Obst_2Hex_03_PR",
        "CR_Library_Book_Fl_01",
        "CR_Library_Book_Fl_02",
        "CR_Library_Feature_Lg_01_PR",
        "CR_Necro_Clutter_Corner_Medium_01_PR",
        "CR_Necro_Clutter_Corner_Small_01_PR",
        "CR_Necro_Clutter_Corner_Small_02_PR",
        "CR_Necro_Clutter_Edge_Medium_01_PR",
        "CR_Necro_Clutter_Edge_Medium_02_PR",
        "CR_Necro_Clutter_Edge_Small_01_PR",
        "CR_Necro_Floor_Detail_01",
        "CR_OS_Bay_01_SM",
        "CR_OS_Bay_02_SM",
        "CR_OS_Bay_03_SM",
        "CR_OS_Bay_04_MED",
        "CR_OS_Bay_05_MED",
        "CR_OS_Chapel_Bay",
        "CR_OS_Feature_02_PR",
        "CR_OS_Feature_03_PR",
        "CR_OS_Feature_04_PR",
        "CR_OS_Obst_1Hex_01_PR",
        "CR_OS_Obst_1Hex_02_PR",
        "CR_OS_Obst_2Hex_01_PR",
        "CR_OS_Obst_2Hex_02_PR",
        "CR_OS_Underground_01",
        "CR_OS_Underground_02",
        "CR_Obst_4Hex_StoneBlocks",
        "CR_PR_Wooden_Pew_02_PR",
        "CR_RU_Bay_01_SM",
        "CR_RU_Bay_02_SM",
        "CR_RU_Bay_03_SM",
        "CR_RU_Bay_05_MED",
        "CR_RU_Floor_Clutter_Lg_02",
        "CR_RU_Obst_1Hex_03_PR",
        "CR_RU_Obst_2Hex_01_PR",
        "CR_RU_Vase",
        "CR_ST_Shelf_Alchemy_01",
        "CR_ST_Shelf_Alchemy_02",
        "CR_ST_Shelf_Alchemy_03",
        "CR_ST_Shelf_Alchemy_04",
        "CR_ST_Shelf_Alchemy_05",
        "CR_ST_Shelf_Alchemy_Balance",
        "CR_ST_Shelf_Alchemy_Jugs_01",
        "CR_ST_Shelf_Alchemy_Jugs_02",
        "CR_ST_Shelf_Alchemy_Jugs_03",
        "CR_ST_Shelf_Alchemy_Jugs_04",
        "CR_ST_Shelf_Alchemy_Jugs_05",
        "CR_ST_Shelf_Alchemy_Jugs_06",
        "CR_ST_Shelf_Alchemy_Oiler_01",
        "CR_ST_Shelf_Alchemy_Oiler_02",
        "CR_ST_Shelf_Alchemy_Sparse_01",
        "CR_ST_Shelf_Alchemy_Sparse_03",
        "CR_ST_Shelf_Alchemy_Sparse_04",
        "CR_ST_Shelf_Alchemy_Sparse_05",
        "CR_ST_Shelf_Book_01",
        "CR_ST_Shelf_Book_02",
        "CR_ST_Shelf_Book_07",
        "CR_ST_Shelf_Books_01",
        "CR_ST_Shelf_Books_02",
        "CR_ST_Shelf_Books_03",
        "CR_ST_Shelf_Books_04",
        "CR_ST_Shelf_Books_05",
        "CR_ST_Shelf_Books_Sparse_01",
        "CR_ST_Shelf_Books_Sparse_02",
        "CR_ST_Shelf_Books_Sparse_03",
        "CR_ST_Shelf_Books_Sparse_04",
        "CR_ST_Shelf_Books_Sparse_05",
        "CR_ST_Shelf_Detail_01",
        "CR_ST_Shelf_Druidic_03",
        "CR_ST_Shelf_Druidic_Cup_01",
        "CR_ST_Shelf_Druidic_Cup_02",
        "CR_ST_Shelf_Druidic_Cup_03",
        "CR_ST_Shelf_Druidic_Cup_04",
        "CR_ST_Shelf_Druidic_Cup_05",
        "CR_ST_Shelf_Druidic_Jugs_01",
        "CR_ST_Shelf_Druidic_Jugs_02",
        "CR_ST_Shelf_Floor_Detail",
        "CR_ST_Shelf_KitchenItems_04",
        "CR_ST_Shelf_KitchenItems_05",
        "CR_ST_Shelf_KitchenItems_Plate_01",
        "CR_ST_Shelf_KitchenItems_Plate_02",
        "CR_ST_Shelf_Pots_01",
        "CR_ST_Shelf_Pots_02",
        "CR_ST_Shelf_Pots_03",
        "CR_ST_Shelf_Pots_04",
        "CR_ST_Shelf_Pots_05",
        "CR_ST_Shelf_Pots_06",
        "CR_ST_Shelf_Scrolls_01",
        "CR_ST_Shelf_Scrolls_02",
        "CR_ST_Shelf_Scrolls_03",
        "CR_ST_Shelf_Scrolls_04",
        "CR_ST_Shelf_Scrolls_05",
        "CR_ST_Shelf_Scrolls_Sparse_01",
        "CR_ST_Shelf_Scrolls_Sparse_02",
        "CR_ST_Shelf_Scrolls_Sparse_03",
        "CR_ST_Shelf_Scrolls_Sparse_04",
        "CR_ST_Shelf_Scrolls_Sparse_05",
        "CR_ST_Shelf_Scrolls_V_01",
        "CR_ST_Shelf_Scrolls_V_02",
        "CR_ST_Shelf_Scrolls_V_03",
        "CR_ST_Shelf_Scrolls_V_04",
        "CR_ST_Shelf_Scrolls_V_05",
        "CR_ST_Shelf_Scrolls_V_06",
        "CR_ST_Shelf_Skulls_01",
        "CR_ST_Shelf_Skulls_02",
        "CR_ST_Shelf_Skulls_03",
        "CR_ST_Shelf_Skulls_04",
        "CR_ST_Shelf_Skulls_05",
        "CR_ST_Shelf_Skulls_Broken_01",
        "CR_ST_Shelf_Skulls_Broken_02",
        "CR_ST_Shelf_Skulls_Broken_03",
        "CR_ST_Shelf_Skulls_Broken_04",
        "CR_ST_Shelf_Skulls_Sparse_01",
        "CR_ST_Shelf_Skulls_Sparse_02",
        "CR_ST_Shelf_Skulls_Sparse_03",
        "CR_ST_Shelf_Skulls_Sparse_04",
        "CR_ST_Shelf_Skulls_Sparse_05",
        "CR_ST_Shelf_UrnsPots_01",
        "CR_ST_Shelf_UrnsPots_02",
        "CR_ST_Shelf_UrnsPots_03",
        "CR_ST_Shelf_UrnsPots_04",
        "CR_ST_Shelf_UrnsPots_05",
        "CR_ST_Shelf_UrnsPots_Sparse_01",
        "CR_ST_Shelf_UrnsPots_Sparse_02",
        "CR_ST_Shelf_UrnsPots_Sparse_03",
        "CR_ST_Shelf_UrnsPots_Sparse_04",
        "CR_ST_Shelf_UrnsPots_Sparse_05",
        "CR_ST_Shelf_Urns_01",
        "CR_ST_Shelf_Urns_02",
        "CR_ST_Shelf_Urns_03",
        "CR_ST_Shelf_Urns_04",
        "CR_ST_TreasureRoom_Shelf_01",
        "CR_ST_TreasureRoom_Shelf_04",
        "CR_ST_TreasureRoom_Shelf_06",
        "CR_ST_Writing_Desk_01_PR",
        "CR_ST_Writing_Desk_Small_01_PR",
        "CR_ST_Writing_Desk_Small_02_PR",
        "CR_Shrine_Clutter_Med_01",
        "CR_Shrine_Clutter_Med_02",
        "CR_Shrine_Clutter_Sm_01",
        "CR_TC_WallChains_01",
        "CR_TC_WallChains_02",
        "CR_TC_WallChains_03",
        "CR_TC_WallChains_04",
        "CR_TR_FloorClutter_Cup_01",
        "CR_TR_FloorClutter_Cup_02",
        "CR_TR_FloorClutter_Cup_03",
        "CR_TR_FloorClutter_Cup_04",
        "CS_02_GuardCamp_Bay_Med_PR",
        "CS_02_GuardCamp_Obst_1Hex_01_PR",
        "CS_03_WaterCave_Bay_02_PR",
        "CS_03_WaterCave_Feature_01_PR",
        "CS_03_WaterCave_Floor_Clutter_Corner_02_PR",
        "CS_04_WaterRuin_Bay_02_PR",
        "CS_04_WaterRuin_Bay_03_PR",
        "CS_04_WaterRuin_Floor_Clutter_Corner_02_PR",
        "CT_01_Ship_Bay_01_PR",
        "CT_01_Ship_Bay_02_PR",
        "CT_01_Ship_Feature_01_PR",
        "CT_01_Ship_Feature_02_PR",
        "CT_01_Ship_Feature_03_PR",
        "CT_01_Ship_Floor_Clutter_01_PR",
        "CT_01_Ship_Floor_Clutter_02_PR",
        "CT_01_Ship_Floor_Clutter_Corner_02_PR",
        "CT_01_Ship_Floor_Clutter_Edge_01_PR",
        "CT_01_Ship_Obst_1Hex_01_PR",
        "CT_01_Ship_Obst_2Hex_01_PR",
        "CT_01_Ship_Obst_2Hex_02_PR",
        "CV_Doorway_01_Narrow_Thick_PR",
        "CV_Doorway_01_Narrow_Thick_Split_PR",
        "CV_Doorway_01_Narrow_Thin_PR",
        "CV_Doorway_01_Narrow_Thin_Split_PR",
        "Clutter_Tile_Medium_01_PR",
        "Clutter_Tile_Medium_02_PR",
        "Clutter_Tile_Medium_03_PR",
        "Clutter_Tile_Small_01_PR",
        "Clutter_Tile_Small_02_PR",
        "Clutter_Tile_Small_03_PR",
        "DLC_Prop_Scen23_GolemStatue_Broke",
        "DLC_SB_Arena_Wall_01_PR",
        "DLC_SB_Arena_Wall_03_PR",
        "DLC_SB_Arena_Wall_04_PR",
        "DLC_SB_C_01_Void_Wall_03_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Corner_Medium_01_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Corner_Medium_02_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Corner_Small_01_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Edge_Medium_01_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Edge_Small_01_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Pages_03_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Tile_Large_01_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Tile_Large_02_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Tile_Medium_01_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Tile_Medium_02_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Tile_Medium_03_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Tile_Medium_04_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Tile_Small_01_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Tile_Small_02_PR",
        "DLC_SB_C_02_DemoShack_Clutter_Tile_Small_03_PR",
        "DLC_SB_C_02_DemoShack_Doorway_01_PR",
        "DLC_SB_C_02_DemoShack_Doorway_01_Split_PR",
        "DLC_SB_C_02_DemoShack_Doorway_01_Thin_PR",
        "DLC_SB_C_02_DemoShack_Floor_07_PR",
        "DLC_SB_Platform_Clutter_Corner_Medium_01_PR",
        "DLC_SB_Platform_Clutter_Corner_Medium_02_PR",
        "DLC_SB_Platform_Clutter_Edge_Medium_01_PR",
        "DLC_SB_Platform_Clutter_Tile_Medium_01_PR",
        "DLC_SB_Platform_Clutter_Tile_Medium_02_PR",
        "DLC_SB_Platform_Obst_1Hex_01_PR",
        "DLC_SB_Ship_Feature_01_PR",
        "DLC_SB_Ship_Feature_02_PR",
        "DLC_SB_Ship_Floor_Clutter_Corner_01_PR",
        "DLC_SB_Ship_Floor_Clutter_Corner_02_PR",
        "DLC_SB_Ship_Floor_Clutter_Edge_01_PR",
        "DLC_SB_Ship_Floor_Clutter_Edge_02_PR",
        "DLC_SB_Ship_Floor_Clutter_Lg_01_PR",
        "DLC_SB_Ship_Floor_Clutter_Lg_02_PR",
        "DLC_SB_Ship_Floor_Clutter_Med_01_PR",
        "DLC_SB_Ship_Floor_Clutter_Med_02_PR",
        "DLC_SB_Ship_Floor_Clutter_Med_03_PR",
        "DLC_SB_Ship_Floor_Clutter_Med_04_PR",
        "DLC_SB_Ship_Floor_Clutter_Sm_01_PR",
        "DLC_SB_Ship_Floor_Clutter_Sm_02_PR",
        "DLC_SB_Ship_Obst_1Hex_01_PR",
        "DLC_SB_Ship_Obst_1Hex_02_PR",
        "DLC_SB_Ship_Obst_2Hex_01_PR",
        "DLC_SB_Ship_Obst_2Hex_02_PR",
        "DLC_SB_Ship_SC03_1Hex_Altar_PR",
        "DLC_SC15_AlchemyDesk_01_PR",
        "DLC_SC15_AlchemyDesk_02_PR",
        "DLC_SC15_AlchemyDesk_03_PR",
        "DLC_ST_BloodCult_Bay_Lg_02_PR",
        "DLC_ST_BloodCult_Bay_Lg_03_PR",
        "DLC_ST_BloodCult_Bay_Lg_PR",
        "DLC_ST_BloodCult_Bay_Med_PR",
        "DLC_ST_BloodCult_Bay_Sm_PR",
        "DLC_ST_BloodCult_Clutter_Floor_01_PR",
        "DLC_ST_BloodCult_Clutter_Floor_02_PR",
        "DLC_ST_BloodCult_Clutter_Floor_05_PR",
        "DLC_ST_BloodCult_Clutter_Floor_06_PR",
        "DLC_ST_BloodCult_Obst_1Hex_01_PR",
        "DLC_ST_BloodCult_Obst_1Hex_02b_PR",
        "DLC_ST_BloodCult_Obst_Obst_2Hex_01_PR",
        "DLC_ST_BloodCult_Obst_Obst_2Hex_02_PR",
        "DLC_Solo_AM_Debris_Obs_1Hex_01",
        "DLC_Solo_AM_Debris_Obs_1Hex_02",
        "DLC_Solo_AM_Debris_Obs_1Hex_03",
        "DLC_Solo_PR_CrystalMonolith_PR",
        "DLC_TH_Gore_Bay_01",
        "DLC_TH_Gore_Bay_02",
        "DLC_TH_Gore_Bay_03",
        "DLC_TH_Gore_Clutter_Tile_Large_01_PR",
        "DLC_TH_Gore_Clutter_Tile_Medium_03_PR",
        "DLC_TH_Gore_Feature_Medium_02_PR",
        "DLC_TH_Gore_Feature_Medium_03_PR",
        "DLC_TH_Gore_Obst_1Hex_03",
        "DLC_TH_Gore_Obst_2Hex_01",
        "DLC_TH_Gore_Obst_2Hex_02",
        "DLC_TH_Gore_Obst_2Hex_03",
        "DLC_TH_Lab_Bay_01_PR",
        "DLC_TH_Lab_Bay_02_PR",
        "DLC_TH_Lab_Bay_03_PR",
        "DLC_TH_Lab_Bottle_01",
        "DLC_TH_Lab_Bottle_02",
        "DLC_TH_Lab_Bottle_03",
        "DLC_TH_Lab_Feature_01_PR",
        "DLC_TH_Lab_Feature_02_PR",
        "DLC_TH_Lab_Flask_01",
        "DLC_TH_Lab_Flask_02",
        "DLC_TH_Lab_Obst_1Hex_01_PR",
        "DLC_TH_Lab_Obst_1Hex_02_PR",
        "DLC_TH_Lab_Potion_01",
        "DLC_TH_Lab_Potion_02",
        "DLC_TH_Lab_Shelf_01_PR",
        "DLC_TH_Lab_Shelf_02_PR",
        "DLC_TH_Lab_Shelf_03_PR",
        "DLC_TH_Lab_Shelf_04_PR",
        "DLC_TH_Lab_Shelf_05_PR",
        "DLC_TH_Lab_Shelf_Clutter_Corner_Med_02_PR",
        "DLC_TH_Lab_Shelf_Clutter_Corner_Small_01_PR",
        "DLC_TH_Lab_Shelf_Clutter_Corner_Small_02_PR",
        "DLC_TH_Lab_Shelf_Clutter_Edge_Med_01_PR",
        "DLC_TH_Lab_Shelf_Clutter_Edge_Med_02_PR",
        "DLC_TH_Lab_Shelf_Clutter_Edge_Small_01_PR",
        "DLC_TH_Lab_Shelf_Clutter_Tile_Medium_02_PR",
        "DLC_TH_Lab_Shelf_Clutter_Tile_Medium_03_PR",
        "DLC_TH_Lab_Shelf_Clutter_Tile_Medium_04_PR",
        "DLC_TH_Lab_Shelf_Clutter_Tile_Medium_05_PR",
        "DLC_TH_Lab_Shelf_Clutter_Tile_Small_01_PR",
        "DLC_TH_Lab_Shelf_Clutter_Tile_Small_02_PR",
        "DLC_TH_Lab_Shelf_Clutter_Tile_Small_03_PR",
        "DLC_TH_Lab_Shelf_Sparse_01_PR",
        "DLC_TH_Lab_Shelf_Sparse_04_PR",
        "DLC_TH_Lab_Shelf_Sparse_05_PR",
        "DLC_TH_Lab_Table_02_PR",
        "DLC_TH_Lab_Table_03_PR",
        "DLC_TH_SlaughterHouse_Bay_02",
        "DLC_TH_SlaughterHouse_Bay_03",
        "DLC_TH_SlaughterHouse_Bay_04",
        "DLC_TH_SlaughterHouse_Bay_05",
        "DLC_TH_SlaughterHouse_Clutter_Corner_Med_01_PR",
        "DLC_TH_SlaughterHouse_Clutter_Tile_Medium_07_PR",
        "DLC_TH_SlaughterHouse_Clutter_Tile_Medium_08_PR",
        "DLC_TH_SlaughterHouse_Feature_Lg_01_PR",
        "DLC_TH_SlaughterHouse_Feature_Lg_03_PR",
        "DLC_TH_SlaughterHouse_Obst_1Hex_02_PR",
        "DLC_TH_SlaughterHouse_Obst_2Hex_01_PR",
        "DLC_TH_SlaughterHouse_Obst_2Hex_02_PR",
        "DLC_TH_Warehouse_Bay_01",
        "DLC_TH_Warehouse_Bay_02",
        "DLC_TH_Warehouse_Bay_04",
        "DLC_TH_Warehouse_Bay_05",
        "DLC_TH_Warehouse_Clutter_Corner_Medium_01_PR",
        "DLC_TH_Warehouse_Clutter_Corner_Medium_02_PR",
        "DLC_TH_Warehouse_Clutter_Edge_Medium_01_PR",
        "DLC_TH_Warehouse_Clutter_Edge_Small_01_PR",
        "DLC_TH_Warehouse_Clutter_Pages_03_PR",
        "DLC_TH_Warehouse_Clutter_Tile_Large_01_PR",
        "DLC_TH_Warehouse_Clutter_Tile_Large_02_PR",
        "DLC_TH_Warehouse_Clutter_Tile_Medium_01_PR",
        "DLC_TH_Warehouse_Clutter_Tile_Medium_02_PR",
        "DLC_TH_Warehouse_Clutter_Tile_Medium_03_PR",
        "DLC_TH_Warehouse_Clutter_Tile_Small_01_PR",
        "DLC_TH_Warehouse_Clutter_Tile_Small_03_PR",
        "DLC_TH_Warehouse_Feature_Medium_01_PR",
        "DLC_TH_Warehouse_Feature_Medium_03_PR",
        "DLC_TH_Warehouse_Obst_1Hex_03",
        "DLC_TH_Warehouse_Obst_2Hex_01",
        "DLC_TH_Warehouse_Obst_2Hex_02",
        "DLC_TH_Warehouse_Obst_2Hex_03",
        "DLC_TH_Warehouse_Obst_2Hex_04",
        "DLC_TH_Warehouse_Shelf_02_PR",
        "DLC_TH_Warehouse_Shelf_04_PR",
        "DLC_TH_Warehouse_Shelf_05_PR",
        "DLC_TH_Warehouse_URN_Obst_Broke_1Hex_01_PR",
        "DLC_Th_Slaughter_Bone_01",
        "DLC_Th_Slaughter_Bone_02",
        "DU_TO_Clutter_Sm_01",
        "DU_TO_Clutter_Sm_02",
        "Default_Bay_Large_01",
        "Default_Bay_Large_02",
        "Default_Bay_Medium_02",
        "Default_Bay_Medium_03",
        "Default_Bay_Medium_05",
        "Default_Bay_Sm_04",
        "EN_CR_Cup_01",
        "EN_CR_LBSkull",
        "EN_CR_Skull",
        "EN_CR_Skull 1",
        "FR_CW_Bay_Medium_01",
        "FR_CW_Bay_Small_02",
        "Kitchen_Bay_Sm_01",
        "Kitchen_Bay_Sm_03",
        "Library_Bay_Sm_01",
        "Library_Bay_Sm_02",
        "Library_Bay_Sm_03",
        "Library_Bay_Sm_04",
        "Necro_Altar03",
        "Necro_Altar04",
        "Necro_Altar05",
        "PCG_CR_ST_Shelf_Book_01",
        "PCG_CR_ST_Shelf_Book_01 1",
        "PCG_CR_ST_Shelf_Book_03",
        "PCG_CR_ST_Shelf_Book_04",
        "PCG_CR_ST_Shelf_Book_05",
        "PCG_CR_ST_Shelf_Book_06",
        "PCG_CR_ST_Shelf_Book_07",
        "PCG_CR_wall_Pillar_Shrine",
        "PCG_CR_wall_Pillar_Shrine 1",
        "PCG_CV_AnimalLair_Bay_Lg_PR",
        "PCG_CV_AnimalLair_Bay_Med_PR",
        "PCG_CV_AnimalLair_Bay_Sm_PR",
        "PCG_CV_AnimalLair_Clutter_Floor_01_PR",
        "PCG_CV_AnimalLair_Clutter_Floor_04_PR",
        "PCG_CV_AnimalLair_Clutter_Floor_05_PR",
        "PCG_CV_AnimalLair_Feature_Large_01_PR",
        "PCG_CV_AnimalLair_Obst_1Hex_01_PR",
        "PCG_CV_AnimalLair_Obst_1Hex_02_PR",
        "PCG_CV_AnimalLair_Obst_Obst_2Hex_01_PR",
        "PCG_CV_AnimalLair_Obst_Obst_2Hex_02_PR",
        "PCG_CV_Bay_Large_Fossil_01_PR",
        "PCG_CV_Bay_Large_Stalagmite_01_PR",
        "PCG_CV_Bay_Large_Stalagmite_02_PR",
        "PCG_CV_Bay_Medium_Campfire_01_PR",
        "PCG_CV_Bay_Medium_Foliage_01_PR",
        "PCG_CV_Bay_Medium_Ladder_01_PR",
        "PCG_CV_Bay_Medium_Pool_01_PR",
        "PCG_CV_Bay_Medium_Prison_01_PR",
        "PCG_CV_Bay_Medium_Storage_01_PR",
        "PCG_CV_Bay_Medium_Storage_02_PR",
        "PCG_CV_Bay_Medium_Waterfall_01_PR",
        "PCG_CV_Bay_Small_Crystal_01_PR",
        "PCG_CV_Bay_Small_Crystal_02_PR",
        "PCG_CV_Bay_Small_Crystal_03_PR",
        "PCG_CV_Bay_Small_Foliage_01_PR",
        "PCG_CV_Bay_Small_Foliage_02_PR",
        "PCG_CV_Bay_Small_Fossil_01_PR",
        "PCG_CV_Bay_Small_Fossil_02_PR",
        "PCG_CV_Bay_Small_Ladder_01_PR",
        "PCG_CV_Bay_Small_Prison_01_PR",
        "PCG_CV_Bay_Small_Prison_02_PR",
        "PCG_CV_Bay_Small_Rock_01_PR",
        "PCG_CV_Bay_Small_Rock_02_PR",
        "PCG_CV_Bay_Small_Rock_05_PR",
        "PCG_CV_Bay_Small_Skull_01_PR",
        "PCG_CV_Bay_Small_Stalagmite_01_PR",
        "PCG_CV_Doorway_01_PR",
        "PCG_CV_Doorway_01_Thick_PR",
        "PCG_CV_Doorway_01_Thin_PR",
        "PCG_CV_Entrance_01_PR",
        "PCG_CV_Exit_01_PR",
        "PCG_CV_Exit_01_Thin_PR",
        "PCG_CV_Feature_Large_01_PR",
        "PCG_CV_Feature_Large_02_PR",
        "PCG_CV_Feature_Large_03_PR",
        "PCG_CV_Feature_Large_06_PR",
        "PCG_CV_Feature_Large_07_PR",
        "PCG_CV_Feature_Medium_02_PR",
        "PCG_CV_Feature_Medium_05_PR",
        "PCG_CV_Feature_Medium_07_PR",
        "PCG_CV_Feature_Small_01_PR",
        "PCG_CV_Feature_Small_03_PR",
        "PCG_CV_Feature_Small_04_PR",
        "PCG_CV_Feature_Small_05_PR",
        "PCG_CV_Feature_Small_06_PR",
        "PCG_CV_Ice_Bay_Large_01_PR",
        "PCG_CV_Ice_Bay_Medium_01_PR",
        "PCG_CV_Ice_Bay_Small_01_PR",
        "PCG_CV_Ice_Bay_Small_02_PR",
        "PCG_CV_Ice_Clutter_Floor_01_PR",
        "PCG_CV_Ice_Clutter_Floor_02_PR",
        "PCG_CV_Ice_Clutter_Floor_03_PR",
        "PCG_CV_Ice_Clutter_Floor_05_PR",
        "PCG_CV_Ice_Clutter_Floor_06_PR",
        "PCG_CV_Ice_Clutter_Floor_07_PR",
        "PCG_CV_Ice_Feature_Large_02_PR",
        "PCG_CV_Ice_Feature_Large_03_PR",
        "PCG_CV_Ice_Feature_Medium_02_PR",
        "PCG_CV_Ice_Feature_Medium_03_PR",
        "PCG_CV_Ice_Obst_1Hex_01_PR",
        "PCG_CV_Ice_Obst_1Hex_02_PR",
        "PCG_CV_Ice_Obst_1Hex_02_PR 1",
        "PCG_CV_Ice_Obst_2Hex_01_PR",
        "PCG_CV_Ice_Obst_2Hex_02_PR",
        "PCG_CV_Ice_Obst_2Hex_02_PR 1",
        "PCG_CV_Ice_Obst_2Hex_03_PR",
        "PCG_CV_Mine_Clutter_Corner_01_PR",
        "PCG_CV_Mine_Clutter_Corner_02_PR",
        "PCG_CV_Mine_Edge_01_PR",
        "PCG_CV_Mine_Edge_02_PR",
        "PCG_CV_Mine_Feature_Large_03_PR",
        "PCG_CV_Mine_Feature_Medium_03_PR",
        "PCG_CV_Mine_Feature_Small_01_PR",
        "PCG_CV_Mine_Obst_1Hex_02_PR",
        "PCG_CV_Mine_Obst_2Hex_02_PR",
        "PCG_CV_Obst_1Hex_Rock_01_PR",
        "PCG_CV_Obst_1Hex_Rock_02_PR",
        "PCG_CV_Temple_Bay_Medium_01_PR",
        "PCG_CV_Temple_Bay_Medium_02_PR",
        "PCG_CV_Temple_Bay_Medium_03_PR",
        "PCG_CV_Temple_Bay_Small_01_PR",
        "PCG_CV_Temple_Bay_Small_02_PR",
        "PCG_CV_Temple_Bay_Small_04_PR",
        "PCG_CV_Temple_Clutter_Corner_02_PR",
        "PCG_CV_Temple_Clutter_Floor_03_PR",
        "PCG_CV_Temple_Edge_01_PR",
        "PCG_CV_Temple_Feature_Large_01_PR",
        "PCG_CV_Temple_Feature_Large_03_PR",
        "PCG_CV_Temple_Feature_Medium_03_PR",
        "PCG_CV_Temple_Feature_Small_04_PR",
        "PCG_CV_Temple_Obst_1Hex_02_PR",
        "PCG_CV_Temple_Obst_2Hex_01_PR",
        "PCG_CV_UnderWall_Crystal_01_PR",
        "PCG_CV_UnderWall_Crystal_02_PR",
        "PCG_CV_UnderWall_Rock_01_PR",
        "PCG_CV_UnderWall_Rock_02_PR",
        "PCG_CV_Volcanic_Bay_Medium_03_PR",
        "PCG_CV_Volcanic_Bay_Small_01_PR",
        "PCG_CV_Volcanic_Bay_Small_02_PR",
        "PCG_CV_Volcanic_Bay_Small_03_PR",
        "PCG_CV_Volcanic_Clutter_Floor_02_PR",
        "PCG_CV_Volcanic_Clutter_Floor_Small_03_PR",
        "PCG_CV_Volcanic_Feature_Large_01_PR",
        "PCG_CV_Volcanic_Feature_Large_02_PR",
        "PCG_CV_Volcanic_Obst_1Hex_02_PR",
        "PCG_CV_Volcanic_Obst_2Hex_01_PR",
        "PCG_CV_Volcanic_Obst_2Hex_02_PR",
        "PCG_CV_Wall_Crystal_02_PR",
        "PCG_CV_Wall_Crystal_03_PR",
        "PCG_CV_Wall_Crystal_05_PR",
        "PCG_CV_Wall_Crystal_Thin_02_PR",
        "PCG_CV_Wall_Crystal_Thin_03_PR",
        "PCG_CV_Wall_Crystal_Thin_Narrow_02_PR",
        "PCG_CV_Wall_Generic_02_PR",
        "PCG_CV_Wall_Generic_04_PR",
        "PCG_CV_Wall_Generic_05_PR",
        "PCG_CV_Wall_Generic_Thin_01_PR",
        "PCG_CV_Wall_Generic_Thin_02_PR",
        "PCG_CV_Wall_Generic_Thin_Narrow_02_PR",
        "PCG_CV_Wall_Space_Generic_02_PR",
        "PCG_CV_Wall_Space_Generic_Thin_01_PR",
        "PCG_CV_Wall_Stalagmite_01_PR",
        "PCG_CV_Wall_Stalagmite_04_PR",
        "PCG_CV_Wall_Stalagmite_Thin_01_PR",
        "PCG_CV_Wall_Stalagmite_Thin_02_PR",
        "PCG_CV_Wall_Stalagmite_Thin_03_PR",
        "PCG_Crypt_Entrance",
        "PCG_Crypt_Entrance_Thin",
        "PCG_Crypt_Exit",
        "PCG_Crypt_Exit_Thin",
        "PCG_FR_BG_Bay_Large_01_PR",
        "PCG_FR_BG_Bay_Medium_01_PR",
        "PCG_FR_BG_Bay_Small_01_PR",
        "PCG_FR_BG_Bay_Small_02_PR",
        "PCG_FR_BG_Clutter_Large_01_PR",
        "PCG_FR_BG_Clutter_Large_03_PR",
        "PCG_FR_BG_Clutter_Medium_01_PR",
        "PCG_FR_BG_Clutter_Medium_02_PR",
        "PCG_FR_BG_Clutter_Small_01_PR",
        "PCG_FR_BG_Obst_1Hex_01_PR",
        "PCG_FR_BG_Obst_1Hex_02_PR",
        "PCG_FR_BG_Obst_1Hex_03_PR",
        "PCG_FR_BG_Obst_2Hex_01_PR",
        "PCG_FR_BG_Obst_2Hex_02_PR",
        "PCG_FR_CW_Obst_1Hex_01_PR",
        "PCG_FR_CW_Obst_1Hex_04_PR",
        "PCG_FR_CW_Obst_1Hex_06_PR",
        "PCG_FR_CW_Obst_2Hex_02_PR",
        "PCG_FR_DG_Bay_Medium_02_PR",
        "PCG_FR_DG_Bay_Small_01_PR",
        "PCG_FR_DG_Bay_Small_02_PR",
        "PCG_FR_IC_Bay_Large_01_PR",
        "PCG_FR_IC_Clutter_Large_01_PR",
        "PCG_FR_IC_Clutter_Medium_01_PR",
        "PCG_FR_IC_Clutter_Medium_02_PR",
        "PCG_FR_IC_Clutter_Small_03_PR",
        "PCG_FR_IC_Obst_1Hex_02_PR",
        "PCG_FR_IC_Obst_1Hex_03_PR",
        "PCG_FR_IC_Obst_1Hex_04_PR",
        "PCG_FR_IC_Obst_2Hex_02_PR",
        "PCG_FR_IC_Obst_2Hex_03_PR",
        "PCG_FR_Obst_1Hex_02_PR",
        "PCG_FR_Obst_1Hex_03_PR",
        "PCG_FR_VV_Bay_Medium_02",
        "PCG_FR_VV_Bay_Small_01",
        "PCG_FR_VV_Bay_Small_03",
        "PCG_FR_VV_Bay_Small_04",
        "PCG_FR_VV_Obst_1Hex_02_PR",
        "PCG_FR_VV_Obst_1Hex_03_PR",
        "PCG_FR_VV_Obst_2Hex_01_PR",
        "PCG_FR_VV_Obst_2Hex_02_PR",
        "PCG_Necropolis_Entrance",
        "PCG_Necropolis_Entrance_Thin",
        "PCG_ST_Bandit_Bay_Med_PR",
        "PCG_ST_Cult_Bay_Lg_02_PR",
        "PCG_ST_Cult_Bay_Lg_03_PR",
        "PCG_ST_Cult_Bay_Lg_PR",
        "PCG_ST_Cult_Bay_Med_PR",
        "PCG_ST_Cult_Bay_Sm_PR",
        "PCG_ST_Cult_Clutter_Floor_01_PR",
        "PCG_ST_Cult_Clutter_Floor_02_PR",
        "PCG_ST_Cult_Clutter_Floor_04_PR",
        "PCG_ST_Cult_Clutter_Floor_05_PR",
        "PCG_ST_Cult_Clutter_Floor_06_PR",
        "PCG_ST_Cult_Obst_1Hex_01_PR",
        "PCG_ST_Cult_Obst_Obst_2Hex_01_PR",
        "PCG_ST_Cult_Obst_Obst_2Hex_02_PR",
        "PCG_ST_Demon_Bay_Med_PR",
        "PCG_ST_Demon_Bay_Sm_PR",
        "PCG_ST_Demon_Clutter_Floor_04_PR",
        "PCG_ST_Demon_Clutter_Floor_06_PR",
        "PCG_ST_Demon_Obst_1Hex_02_PR",
        "PCG_ST_Demon_Obst_Obst_2Hex_01_PR",
        "PCG_ST_Demon_Obst_Obst_2Hex_02_PR",
        "PCG_ST_FireDemon_Bay_Lg_PR",
        "PCG_ST_FireDemon_Bay_Med_PR",
        "PCG_ST_FireDemon_Bay_Sm_PR",
        "PCG_ST_FireDemon_Clutter_Floor_04_PR",
        "PCG_ST_FireDemon_Clutter_Floor_06_PR",
        "PCG_ST_FireDemon_Obst_1Hex_01_PR",
        "PCG_ST_IceDemon_Bay_Lg_PR",
        "PCG_ST_IceDemon_Clutter_Floor_04_PR",
        "PCG_ST_IceDemon_Clutter_Floor_06_PR",
        "PCG_ST_IceDemon_Obst_1Hex_01_PR",
        "PCG_ST_IceDemon_Obst_Obst_2Hex_02_PR",
        "PCG_TO_INT_Cathedral_Floor_01b_PR",
        "PCG_TO_INT_Cathedral_Floor_01c_PR",
        "PCG_TO_INT_Obst_1Hex_01_PR",
        "PCG_TO_INT_Obst_1Hex_02_PR",
        "PCG_TO_INT_Obst_1Hex_04_PR",
        "PCG_TO_INT_Obst_2Hex_01_PR",
        "PCG_TO_INT_Obst_2Hex_02_PR",
        "PCG_TO_INT_Obst_2Hex_03_PR",
        "PCG_TO_INT_UnderWall_01_PR",
        "PCG_Test_Feature_Medium_1_Nice4",
        "PR_2Hex_Storage_01",
        "PR_CR_OS_Candelabra_01",
        "PR_CR_OS_Candelabra_02",
        "PR_CR_OS_Candelabra_03",
        "PR_CR_OS_Candelabra_04",
        "PR_Sc05_Altar_PR",
        "PR_Sc28_ElementAltar_PR",
        "PR_Sc54_Altar_PR",
        "PR_Sc61_Light_PR",
        "PR_Sc75_3Hex_PR",
        "PR_Sc78_Cult_Altar_PR",
        "PR_Sc83_Altar_PR",
        "RockTriple 02",
        "SB_01_ElementalPower_Clutter_Corner_Medium_01_PR",
        "SB_01_ElementalPower_Clutter_Corner_Small_01_PR",
        "SB_01_ElementalPower_Clutter_Pages_03_PR",
        "SB_01_ElementalPower_Clutter_Tile_Medium_02_PR",
        "SB_01_ElementalPower_Clutter_Tile_Small_01_PR",
        "SB_01_ElementalPower_Entrance_Thin_PR",
        "SB_01_ElementalPower_Underground_01_PR",
        "SB_01_ElementalPower_Underground_02_PR",
        "SB_02_Fort_Bay_02",
        "SB_02_Fort_Bay_03",
        "SB_02_Fort_Entrance_Thin_PR",
        "SB_02_Fort_Obst_1Hex_02_PR",
        "SB_02_Fort_Obst_2Hex_02_PR",
        "SB_02_Fort_Underground_01_PR",
        "SB_02_Fort_Underground_02_PR",
        "SB_04_Snow_Clutter_Corner_Small_01_PR",
        "SB_04_Snow_Clutter_Edge_Medium_01_PR",
        "SB_04_Snow_Clutter_Tile_Medium_01_PR",
        "SB_04_Snow_Clutter_Tile_Medium_02_PR",
        "SB_04_Snow_Entrance_Thin_PR",
        "SB_04_Snow_Feature_Large_02_PR",
        "SB_04_Snow_Feature_Large_03_PR",
        "SB_04_Snow_Feature_Small_02_PR",
        "SB_04_Snow_Feature_Small_03_PR",
        "SB_04_Snow_Obst_1Hex_Rock_01_PR",
        "SB_04_Snow_Obst_1Hex_Rock_02_PR",
        "SB_04_Snow_UnderWall_Rock_01_PR",
        "SB_04_Snow_UnderWall_Rock_02_PR",
        "SB_04_Snow_Wall_Generic_02_Sp_PR",
        "SB_04_Snow_Wall_Generic_04_PR",
        "SB_04_Snow_Wall_Generic_05_PR",
        "SB_04_Snow_Wall_Generic_Thin_01_Sp_PR",
        "SB_04_Snow_Wall_Generic_Thin_02_Sp_PR",
        "SB_05_Bridge_Pillar_Thin_01_PR",
        "SB_05_Bridge_UnderWall_01_PR",
        "SB_05_Bridge_UnderWall_02_PR",
        "SB_05_Bridge_UnderWall_03_PR",
        "SB_05_Bridge_Wall_Tiny_PR",
        "SB_CC_Bridge_Feature_Large_02_PR",
        "SB_CC_Bridge_Feature_Large_03_PR",
        "SB_CC_Bridge_Feature_Small_02_PR",
        "SB_CC_Bridge_Feature_Small_03_PR",
        "SB_CC_Bridge_UnderWall_Rock_01_PR",
        "SB_CC_Bridge_UnderWall_Rock_02_PR",
        "SB_CC_Bridge_Wall_Generic_02_Sp_PR",
        "SB_CC_Bridge_Wall_Generic_04_PR",
        "SB_CC_Bridge_Wall_Generic_05_PR",
        "SB_CC_Bridge_Wall_Generic_Thin_01_Sp_PR",
        "SB_CC_Bridge_Wall_Generic_Thin_02_Sp_PR",
        "SB_Cu01_DemoBuilding_Bay_01",
        "SB_Cu01_DemoBuilding_Bay_02",
        "SB_Cu01_DemoBuilding_Bay_03",
        "SB_Cu01_DemoBuilding_Bay_04",
        "SB_Cu01_DemoBuilding_Wall_01_Window_02_PR",
        "SB_Cu01_DemoBuilding_Wall_01_Window_04_PR",
        "SE_Rot_Bay_02",
        "SE_Rot_Obst_1Hex_03_PR",
        "SE_Rot_Obst_2Hex_01_PR",
        "SE_Rot_Obst_2Hex_02_PR",
        "SE_Rot_Sewer_EXIT_Thin_PR",
        "SE_Rot_Sewer_Entrance_Thin_PR",
        "ST_TownMilitia_Bay_Sm_PR",
        "ST_TownMilitia_Clutter_Floor_02_PR",
        "ST_TownMilitia_Clutter_Floor_04_PR",
        "ST_TownMilitia_Clutter_Floor_06_PR",
        "ST_TownMilitia_Obst_Obst_2Hex_02_PR 1",
        "ST_Undead_Bay_01_Med_PR",
        "ST_Undead_Bay_Sm_PR",
        "ST_Undead_Clutter_Floor_01_PR",
        "ST_Undead_Clutter_Floor_03_PR",
        "ST_Undead_Clutter_Floor_04_PR",
        "ST_Undead_Clutter_Floor_05_PR",
        "ST_Undead_Clutter_Floor_06_PR",
        "ST_Undead_Clutter_Floor_07_PR",
        "ST_Undead_Clutter_Floor_08_PR",
        "ST_Undead_Clutter_Floor_09_PR",
        "ST_Undead_Clutter_Floor_10_PR",
        "ST_Undead_Clutter_Floor_11_PR",
        "ST_Undead_Obst_1Hex_01_PR",
        "ST_Undead_Obst_Obst_2Hex_01_PR",
        "ST_Undead_Obst_Obst_2Hex_02_PR",
        "ST_Vermling_Bay_Lg_PR",
        "ST_Vermling_Bay_Med_02_PR",
        "ST_Vermling_Clutter_Floor_01_PR",
        "ST_Vermling_Clutter_Floor_03_PR",
        "ST_Vermling_Clutter_Floor_05_PR",
        "ST_Vermling_Clutter_Floor_08_PR",
        "ST_Vermling_Clutter_Floor_11_PR",
        "ST_Vermling_Feature_01_PR",
        "ST_Vermling_Feature_02_PR",
        "ST_Vermling_Obst_1Hex_01_PR",
        "ST_Vermling_Obst_1Hex_02_PR",
        "ST_Vermling_Obst_2Hex_04_PR",
        "Shrine_Bay_Sm_01",
        "Shrine_Bay_Sm_02",
        "Shrine_Bay_Sm_03",
        "Storage_Bay_Sm_01",
        "Storage_Bay_Sm_02",
        "Storage_Bay_Sm_04",
        "TERRAIN_Cave_HotCoals_PR",
        "TERRAIN_Necro_Rubble_PR",
        "TERRAIN_Water_Wood_01_PR",
        "TERRAIN_Water_Wood_02_PR",
        "TERRAIN_Water_Wood_03_PR",
        "TERRAIN_Wood_Rubble_PR",
        "TO_EXT_Bay_Large_01_PR",
        "TO_EXT_Bay_Large_02_PR",
        "TO_EXT_Bay_Medium_01_PR",
        "TO_EXT_Bay_Medium_02_PR",
        "TO_EXT_Bay_Medium_03_PR",
        "TO_EXT_Bay_Medium_04_PR",
        "TO_EXT_Bay_Medium_05_PR",
        "TO_EXT_Bay_Medium_06_PR",
        "TO_EXT_Bay_Small_01_PR",
        "TO_EXT_Bay_Small_02_PR",
        "TO_EXT_Bay_Small_03_PR",
        "TO_EXT_Bay_Small_04_PR",
        "TO_EXT_Bay_Small_05_PR",
        "TO_EXT_Obst_1Hex_02_PR",
        "TO_EXT_Obst_2Hex_03_PR",
        "TO_EXT_Obst_2Hex_04_PR",
        "TO_EXT_UnderWall_01_PR",
        "TO_EXT_UnderWall_02_PR",
        "TO_EXT_UnderWall_03_PR",
        "TO_INT_Cathedral_Bay_02",
        "TO_INT_Cathedral_Bay_03",
        "TO_INT_Cathedral_Bay_04",
        "TO_INT_Cathedral_Clutter_Pages_03_PR",
        "TO_INT_Cathedral_EXIT_Thin_PR",
        "TO_INT_Cathedral_Entrance_Thick_PR",
        "TO_INT_Cathedral_Entrance_Thin_PR",
        "TO_INT_Cathedral_Obst_1Hex_01_PR",
        "TO_INT_Cathedral_Obst_2Hex_01_PR",
        "TO_INT_Cathedral_Obst_2Hex_02_PR",
        "TO_INT_Cathedral_Obst_2Hex_03_PR",
        "TO_INT_Cathedral_Underground_01_PR",
        "TO_INT_Cathedral_Underground_02_PR",
        "TO_INT_Cathedral_Underground_03_PR",
        "TO_INT_Cathedral_Underground_04_PR",
        "TO_INT_EXIT_Thick_PR",
        "TO_INT_EXIT_Thin_PR",
        "TO_INT_EXIT_Wood_Thin_PR",
        "TO_INT_Entrance_Thick_PR",
        "TO_INT_Entrance_Thin_PR",
        "TO_INT_Entrance_Wood_Thick_PR",
        "TO_INT_Entrance_Wood_Thin_PR",
        "TO_INT_Roof_Ceramic_Debris_01_PR",
        "TO_INT_Shelf_Clutter_Default_01_PR",
        "TO_INT_Shelf_Clutter_Default_02_PR",
        "TO_INT_Shelf_Clutter_Default_03_PR",
        "TO_INT_Shelf_Clutter_Default_04_PR",
        "TO_INT_Shelf_Clutter_Default_05_PR",
        "TO_INT_Shelf_Clutter_Default_06_PR",
        "TO_INT_Shelf_Clutter_Default_07_PR",
        "TO_INT_Shelf_Clutter_Default_08_PR",
        "TO_INT_Shelf_Clutter_Default_09_PR",
        "TO_INT_Shelf_Clutter_Default_10_PR",
        "TO_INT_Stone_Bay_Large_01_PR",
        "TO_INT_Stone_Bay_Medium_01_PR",
        "TO_INT_Stone_Bay_Medium_02_PR",
        "TO_INT_Stone_Bay_Small_01_PR",
        "TO_INT_Stone_Feature_Large_01_PR",
        "TO_INT_Stone_Feature_Large_02_PR",
        "TO_INT_Stone_Feature_Medium_03_PR",
        "TO_INT_Stone_Feature_Medium_06_PR",
        "TO_INT_Univ_Bay_Sm_01",
        "TO_INT_Univ_Bay_Sm_02",
        "TO_INT_Univ_Clutter_Floor_01_PR",
        "TO_INT_Univ_Clutter_Floor_02_PR",
        "TO_INT_Univ_Feature_Lg_01_PR",
        "TO_INT_Univ_Feature_Lg_02_PR",
        "TO_INT_Univ_Obst_1Hex_02_PR",
        "TO_INT_Univ_Obst_1Hex_03_PR",
        "TO_INT_Univ_Obst_2Hex_01_PR",
        "TO_INT_Univ_Obst_2Hex_02_PR",
        "TO_INT_Univ_Obst_2Hex_03_PR",
        "TO_INT_Wood_Bay_LSmall_01_PR",
        "TO_INT_Wood_Bay_Medium_02_PR 1",
        "TO_INT_Wood_Bay_Small_02_PR",
        "TO_INT_Wood_Bay_Small_03_PR",
        "TO_INT_Wood_Bay_Small_04_PR",
        "TO_INT_Wood_Feature_Medium_01_PR",
        "TO_INT_Wood_Feature_Medium_04_PR",
        "TO_INT_Wood_Shack_Bay_02",
        "TO_INT_Wood_Shack_Bay_03",
        "TO_INT_Wood_Shack_Bay_04",
        "TO_INT_Wood_Shack_Clutter_Corner_Medium_01_PR",
        "TO_INT_Wood_Shack_Clutter_Corner_Small_01_PR",
        "TO_INT_Wood_Shack_Clutter_Edge_Medium_01_PR",
        "TO_INT_Wood_Shack_Clutter_Edge_Small_01_PR",
        "TO_INT_Wood_Shack_Clutter_Pages_03_PR",
        "TO_INT_Wood_Shack_Clutter_Tile_Medium_01_PR",
        "TO_INT_Wood_Shack_Clutter_Tile_Medium_02_PR",
        "TO_INT_Wood_Shack_Clutter_Tile_Small_01_PR",
        "TO_INT_Wood_Shack_EXIT_Thin_PR",
        "TO_INT_Wood_Shack_Entrance_Thin_PR",
        "TO_INT_Wood_Shack_Obst_1Hex_01_PR",
        "TO_INT_Wood_Shack_Obst_1Hex_02_PR",
        "TO_INT_Wood_Shack_Obst_1Hex_03_PR",
        "TO_INT_Wood_Shack_Obst_1Hex_04_PR",
        "TO_INT_Wood_Shack_Obst_2Hex_01_PR",
        "TO_INT_Wood_Shack_Obst_2Hex_02_PR",
        "TO_INT_Wood_Shack_Obst_2Hex_03_PR",
        "TO_INT_Wood_Shack_Underground_01_PR",
        "TO_INT_Wood_Shack_Underground_02_PR",
        "TO_INT_Wood_Shack_Underground_03_PR",
        "TO_INT_Wood_Shack_Underground_04_PR",
        "TO_SB03_Wall_01_Narrow_PR",
        "TO_SB03_Wall_01_PR",
        "TO_SB03_Wall_02_PR",
        "TO_SB03_Wall_02b_PR",
        "TO_SB03_Wall_03_PR",
        "TO_SB03_Wall_Thin_01_PR",
        "TO_SB03_Wall_Thin_01b_PR",
        "TO_SB03_Wall_Tiny_PR",
        "TO_SB_03_Pillar_01_PR",
        "TO_SB_03_Pillar_01b_PR",
        "TO_SB_03_Pillar_01c_PR",
        "TO_SB_GothicTunnels_Bay_01",
        "TO_SB_GothicTunnels_Bay_02",
        "TO_SB_GothicTunnels_Bay_03",
        "TO_SB_GothicTunnels_Bay_04",
        "TO_SB_GothicTunnels_Bay_05",
        "TO_SB_GothicTunnels_Bay_06",
        "TO_SB_GothicTunnels_Bay_07",
        "TO_SB_GothicTunnels_Clutter_Corner_Medium_01_PR",
        "TO_SB_GothicTunnels_Clutter_Corner_Small_01_PR",
        "TO_SB_GothicTunnels_Clutter_Tile_Medium_01_PR",
        "TO_SB_GothicTunnels_Clutter_Tile_Medium_05_PR",
        "TO_SB_GothicTunnels_Clutter_Tile_Medium_06_PR",
        "TO_SB_GothicTunnels_Clutter_Tile_Small_01_PR",
        "TO_SB_GothicTunnels_Doorway_01_PR",
        "TO_SB_GothicTunnels_EXIT_Thick_PR",
        "TO_SB_GothicTunnels_EXIT_Thin_PR",
        "TO_SB_GothicTunnels_Entrance_Thick_PR",
        "TO_SB_GothicTunnels_Entrance_Thin_PR",
        "TO_SB_GothicTunnels_Feature_SM_01",
        "TO_SB_GothicTunnels_Feature_SM_02",
        "TO_SB_GothicTunnels_Feature_SM_06",
        "TO_SB_GothicTunnels_Obst_1Hex_01_PR",
        "TO_SB_GothicTunnels_Obst_1Hex_02_PR",
        "TO_SB_GothicTunnels_Obst_1Hex_03_PR",
        "TO_SB_GothicTunnels_Obst_2Hex_01_PR",
        "TO_SB_GothicTunnels_Obst_2Hex_02_PR",
        "TO_Sewer_Bay_02",
        "TO_Sewer_Bay_03",
        "TO_Sewer_Bay_06",
        "TO_Sewer_Bay_11",
        "TO_Sewer_EXIT_Thin_PR",
        "TO_Sewer_Entrance_Thin_PR",
        "TO_Sewer_Obst_1Hex_01_PR",
        "TO_Sewer_Obst_1Hex_02_PR",
        "TO_Sewer_Obst_1Hex_03_PR",
        "TO_Sewer_Obst_2Hex_01_PR",
        "TO_Sewer_Obst_2Hex_02_PR",
        "Torture_Bay_Large_01",
        "Torture_Bay_Large_02",
        "Torture_Bay_Sm_01",
        "Torture_Bay_Sm_02",
        "Treasure_Bay_Sm_02",
    };

    private static readonly HashSet<string> NativeRetainedFurnitureCores = new(StringComparer.Ordinal)
    {
        "CR_GE_Table_Small_V1",
        "CR_Library_Chair_Broken_V1",
        "CR_Library_Chair_V1",
        "CR_Library_Chair_V2",
        "CR_Library_Chair_V3",
        "CR_PR_Table_SingleHex",
        "CR_ST_Alchemy_Table_V1",
        "CR_ST_Alchemy_Table_V2",
        "CR_ST_Alchemy_Table_V3",
        "CR_ST_Artisan_Table_V1",
        "CR_ST_Artisan_Table_V2",
        "CR_ST_Artisan_Table_V3",
        "CR_ST_FloorShelf_Stone",
        "CR_ST_FloorShelf_Stone_Bone",
        "CR_ST_FloorShelf_Stone_Wood",
        "CR_ST_FloorShelf_Stone_Wood_Shelf",
        "CR_ST_Kitchen_Table_V1",
        "CR_ST_Kitchen_Table_V2",
        "CR_ST_Kitchen_Table_V3",
        "CR_ST_Shelf_Alchemy_01",
        "CR_ST_Shelf_Alchemy_02",
        "CR_ST_Shelf_Alchemy_03",
        "CR_ST_Shelf_Alchemy_04",
        "CR_ST_Shelf_Alchemy_05",
        "CR_ST_Shelves_Stone",
        "CR_ST_Shelves_Stone_Bone",
        "CR_ST_Shelves_Stone_Wood",
        "CR_ST_Shelves_Stone_Wood_Shelf",
        "CR_ST_SmallShelf_Stone",
        "CR_ST_SmallShelf_Stone_Bone",
        "CR_ST_SmallShelf_Stone_Shelf",
        "CR_ST_SmallShelf_Stone_Shelf1",
        "CR_ST_SmallShelf_Stone_Wood",
        "CR_ST_SmallShelf_Stone_Wood_Shelf",
        "CR_ST_Table_Chair_Broken_V1",
        "CR_ST_Table_Chair_V1",
        "CR_ST_Table_V1",
        "CR_ST_Table_V2",
        "CR_ST_WallShelf_Stone",
        "CR_ST_WallShelf_Stone_Bone",
        "CR_ST_WallShelf_Stone_Shelf",
        "CR_ST_WallShelf_Stone_Stone",
        "CR_ST_WallShelf_Stone_Wood",
        "CR_ST_WallShelf_Stone_Wood_Shelf01",
        "CR_ST_WallShelf_Stone_Wood_Shelf02",
        "CR_ST_WallShelf_Stone_Wood_Stone",
        "CR_TC_Table",
        "PR_Table",
        "TO_INT_Chair_Ornate",
        "TO_INT_Chair_Peasant",
        "TO_INT_Chair_Simple",
        "TO_INT_Market_Stall_Table",
        "TO_INT_Prop_Bench_Cloth",
        "TO_INT_Prop_Table",
    };

    // Build620 hardware retained actual floor pages/skulls and shelf contents. Exact original
    // meshes below a solid composite are optional leaves; native controller-less Animators
    // on the shelf contents are inert prefab residue, not proof of an active animation.
    // A real controller, native prop/actor, large decorative unit or unknown collider still vetoes.
    private static bool BlocksSmallDressingAnimation(Transform node, bool smallDressing)
    {
        Animator animator = node.GetComponent<Animator>();
        return animator != null && (!smallDressing || animator.runtimeAnimatorController != null);
    }

    // Match every figure marker in the global guard through the scene root, including above
    // the tile boundary. The only exception is an original decorative Animator without a
    // controller; an actual actor or animated accessory cannot inherit a small-mesh exemption.
    private static bool HasSmallDressingFigureAncestor(Renderer renderer)
    {
        for (Transform? node = renderer.transform; node != null; node = node.parent)
            if (node.GetComponent<ActorBehaviour>() != null
                || node.GetComponent<CInteractableActor>() != null
                || BlocksSmallDressingAnimation(node, smallDressing: true)) return true;
        return false;
    }

    private static bool IsExactSmallSubtree(Transform root)
    {
        MeshRenderer[] members = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
        if (members.Length == 0) return false;
        for (int i = 0; i < members.Length; i++)
        {
            MeshFilter filter = members[i].GetComponent<MeshFilter>();
            Mesh? mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !IsNativeSmallDressing(mesh.name)) return false;
        }
        return true;
    }

    private static bool HasUnknownSmallDressingCallback(Transform node)
    {
        MonoBehaviour[] callbacks = node.GetComponents<MonoBehaviour>();
        for (int i = 0; i < callbacks.Length; i++)
        {
            if (callbacks[i] == null) return true;
            Type type = callbacks[i].GetType();
            if (type != typeof(MaterialLoader) && type != typeof(DetailsDisabler)
                && type != typeof(DetailLevelDisableProvider) && type != typeof(ImportantObjectsShadowsDisabler)
                && type != typeof(PropObjectsShadowsDisabler) && type != typeof(UnityGameEditorObject)
                && type != typeof(CInteractableTile)) return true;
        }
        return false;
    }

    private static bool IsLargeDecorativeUnit(string name) =>
        name.IndexOf("Corpse", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Cadaver", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("DeadBody", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Skeleton", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Cross", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Crucifix", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Coffin", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Сoffin", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string OriginalAssetName(string name)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 7);
            int space = name.LastIndexOf(' ');
            if (space < 0 || space + 3 >= name.Length || name[space + 1] != (char)40
                || name[name.Length - 1] != (char)41) continue;
            bool numeric = true;
            for (int i = space + 2; i < name.Length - 1; i++)
                if (name[i] < '0' || name[i] > '9') { numeric = false; break; }
            if (numeric) name = name.Substring(0, space);
        }
        return name;
    }

    private static bool IsNativeSmallDressing(string name) =>
        NativeSmallDressing.Contains(name) || NativeSmallDressing.Contains(OriginalAssetName(name));

    private static Kind NamedKind(string name)
    {
        // Build619's complete original PCG-database census covers every biome/DLC, rather than
        // the forest/cave hardware sample. Named cosmetics still pass the full gameplay,
        // structural mesh, collision and generated-content checks. A new token is not permission
        // to mask an enclosing procedural wall or a native interactable tree/obstacle.
        if (NativeRetainedFurnitureCores.Contains(name))
            return Kind.None;
        if (IsNativeCompositeDressing(name) || IsNativeSmallDressing(name))
            return Kind.Dressing;
        if (IsNativeWallPlantLeaf(name))
            return Kind.Vegetation;
        if (name.IndexOf("_Grass", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_LongGrass", StringComparison.OrdinalIgnoreCase) >= 0
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
            || name.IndexOf("_Foliage", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Geranium", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Moss", StringComparison.OrdinalIgnoreCase) >= 0
            || (IsNativeWallPlantLeaf(name) && name.IndexOf("_Log", StringComparison.OrdinalIgnoreCase) >= 0))
            return Kind.Vegetation;
        if (name.IndexOf("_Floor_Scatter_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Detail_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Clutter_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Floor_Stalagmites_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Crystal_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Clutter_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Scatter_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Debris_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_SmallRock", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Stones_", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Vase", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Urn", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Pot", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Barrel", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Bottle", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Candle", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Book", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Banner", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Skull", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Bone", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Paper", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Pages", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Parchment", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Scroll", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Cup", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Carpet", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Curtain", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Furniture", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Chair", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Candelabra", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_WallChains", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("_Cobweb", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("FloorClutter", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("CoinsScatter", StringComparison.OrdinalIgnoreCase) >= 0
            || name.StartsWith("fi_vil_combs_props_bonepile_", StringComparison.Ordinal)
            || name.StartsWith("FR_Stones_", StringComparison.Ordinal)
            || name.StartsWith("CR_FR_Stones_", StringComparison.Ordinal)
            || name.StartsWith("geranium ", StringComparison.Ordinal))
            return Kind.Dressing;
        return Kind.None;
    }

    private static bool IsStructuralName(string name) =>
        !IsNativeTreeAsset(name) && !IsNativeCompositeDressing(name) && !IsNativeSmallDressing(name) && (name.IndexOf("_Wall_", StringComparison.OrdinalIgnoreCase) >= 0
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
        if (NativeRetainedFurnitureCores.Contains(name)) return true;
        if (IsNativeTreeAsset(name))
            return false;
        if (IsNativeCompositeDressing(name) || IsNativeSmallDressing(name))
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
        private readonly List<ProjectorRecord> _projectors = new();
        private readonly Dictionary<int, ProjectorRecord> _projectorIds = new();
        private readonly Dictionary<int, Record> _byId = new(1024);
        private readonly List<GameObject> _heldRoots = new(8);
        private readonly List<MeshRenderer> _heldMeshes = new(32);
        // An operation count, not a timer: runtime regression checks prove unrelated hidden
        // scenery is never traversed when a player merely holds one prop.
        private int _heldMeshChecks;

        internal bool IsPreparingPresentation => _inScenarioScene
            && (_pending.Count != 0 || _nodes.Count != 0 || _materialNodes.Count != 0
                || _retuneIndex >= 0);

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
        private int _projectorWatchIndex;
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
            if (!BudgetActive && _records.Count == 0 && _projectors.Count == 0)
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
                Projector[] projectors = root.GetComponentsInChildren<Projector>(includeInactive: true);
                for (int i = 0; i < projectors.Length; i++) ExamineProjector(projectors[i], tile);
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
                Projector projector = t.GetComponent<Projector>();
                if (projector != null) ExamineProjector(projector, node.Tile);
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

        private void ExamineProjector(Projector projector, ProceduralMapTile tile)
        {
            int id = projector.GetInstanceID();
            if (_projectorIds.TryGetValue(id, out ProjectorRecord? record))
            {
                if (!IsDecorativeProjector(projector, tile))
                { SetProjectorHidden(record, false); record.Invalidated = true; return; }
                if (!SameProjectorChain(record))
                {
                    SetProjectorHidden(record, false);
                    record.Hash = StableHash(projector.transform, tile);
                    record.Chain = CaptureChain(projector.transform, tile);
                    record.Tile = tile;
                }
                record.Invalidated = false;
            }
            else
            {
                if (!IsDecorativeProjector(projector, tile)) return;
                record = new ProjectorRecord { Projector = projector, Id = id, Tile = tile,
                    Hash = StableHash(projector.transform, tile), Chain = CaptureChain(projector.transform, tile) };
                _projectorIds.Add(id, record);
                _projectors.Add(record);
            }
            SetProjectorHidden(record, ShouldHide(record.Hash, _decorationDensity));
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
            int end = Math.Min(_records.Count + _projectors.Count, _retuneIndex + RetunesPerFrame);
            for (; _retuneIndex < end; _retuneIndex++)
            {
                if (_retuneIndex >= _records.Count)
                {
                    ProjectorRecord projection = _projectors[_retuneIndex - _records.Count];
                    if (!projection.Invalidated) SetProjectorHidden(projection, ShouldHide(projection.Hash, _decorationDensity));
                    continue;
                }
                Record record = _records[_retuneIndex];
                if (!record.Invalidated)
                    SetHidden(record, ShouldHide(record.Hash, DensityFor(record)));
            }
            if (_retuneIndex < _records.Count + _projectors.Count)
                return;
            _retuneIndex = -1;
            PerfMonitor.MarkChange($"Scenario scenery budget retune complete: grass {_density}%, "
                             + $"decoration {_decorationDensity}%, vegetation {_vegetationDensity}%");
            if (!BudgetActive)
            {
                _records.Clear();
                _byId.Clear();
                _projectors.Clear();
                _projectorIds.Clear();
            }
        }

        private void RecheckOwned()
        {
            // A bounded chain-only watch rescues reparented/retired original decal projections.
            // Ordinary wall/tile collision remains represented by retained meshes, never a decal.
            int projectorBudget = Math.Min(16, _projectors.Count);
            while (projectorBudget-- > 0)
            {
                if (_projectorWatchIndex >= _projectors.Count) _projectorWatchIndex = 0;
                ProjectorRecord projection = _projectors[_projectorWatchIndex++];
                if (!SameProjectorChain(projection))
                { SetProjectorHidden(projection, false); projection.Invalidated = true; }
            }
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
            if (_retuneIndex < 0 && _projectors.Count > 0)
            {
                int index = _projectorWatchIndex % _projectors.Count;
                ProjectorRecord projection = _projectors[index];
                if (projection.Projector == null)
                {
                    _projectorIds.Remove(projection.Id);
                    _projectors[index] = _projectors[_projectors.Count - 1];
                    _projectors.RemoveAt(_projectors.Count - 1);
                }
            }
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
            int projectorOwned = 0;
            for (int i = 0; i < _projectors.Count; i++)
                if (_projectors[i].Projector != null && _projectors[i].Owned && !_projectors[i].Invalidated) projectorOwned++;
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
                              + "structural walls preserved; actual frame gain is unverified. "
                              + $"Decorative projections {_projectors.Count}, owned masks {projectorOwned}.");
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
            for (int i = 0; i < _projectors.Count; i++) SetProjectorHidden(_projectors[i], false);
            _projectors.Clear();
            _projectorIds.Clear();
            _projectorWatchIndex = 0;
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
