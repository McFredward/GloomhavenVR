using System;
using GloomhavenVR.Board.FigureGrab;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Positive original-mesh proof for the optional architectural detail density.
/// The native whole-game/DLC catalog and verified bank signatures authorize ornaments,
/// never a substring such as Wall/Trim/Shelf. Runtime gameplay and collision boundaries
/// remain current. This helper runs during bounded discovery/native readiness, not a
/// hierarchy-wide frame sweep, and never writes a native component or collider.</summary>
internal static class ScenarioArchitecturalDetailBudget
{
    // Witnesses are captured only during bounded discovery. The existing 64-record
    // watch checks these exact live native cores; it never repeats a subtree scan.
    internal sealed class RetainedCore
    {
        internal readonly MeshRenderer Renderer;
        private readonly MeshFilter _filter;
        private readonly Mesh _mesh;
        private readonly Transform[] _chain;
        internal RetainedCore(MeshRenderer renderer, Transform scope)
        {
            Renderer = renderer; _filter = renderer.GetComponent<MeshFilter>(); _mesh = _filter.sharedMesh!;
            var chain = new System.Collections.Generic.List<Transform>();
            for (Transform? node = renderer.transform; node != null; node = node.parent)
            { chain.Add(node); if (node == scope) break; }
            _chain = chain.ToArray();
        }
        internal bool IsCurrent()
        {
            if (Renderer == null || !Renderer.enabled || Renderer.forceRenderingOff
                || !Renderer.gameObject.activeInHierarchy || _filter == null || _filter.sharedMesh != _mesh) return false;
            Transform? node = Renderer.transform;
            foreach (Transform original in _chain)
            { if (node == null || node != original) return false; node = node.parent; }
            return true;
        }
    }

    internal static bool TryAdmit(MeshRenderer renderer, ProceduralMapTile tile, out Transform? unit) =>
        TryAdmit(renderer, tile, out unit, out _);

    internal static bool TryAdmit(MeshRenderer renderer, ProceduralMapTile tile, out Transform? unit,
        out RetainedCore[] cores)
    {
        unit = null; cores = Array.Empty<RetainedCore>();
        if (renderer == null || tile == null || !Ornament(renderer)) return false;
        int requiredRole = ScenarioEnvironmentMeshBank.RoomArchitectureRole(renderer.GetComponent<MeshFilter>().sharedMesh!) == 2 ? 2 : 1;

        // Promote non-LOD multipart trim too. Stop before any native room boundary,
        // retained mesh, collision, light or controller. Every renderer in the selected
        // original subtree receives one path hash, including inactive LOD members.
        unit = renderer.transform;
        if (!WholeOrnament(unit)) return false;
        Transform? requiredLod = null;
        for (Transform? node = renderer.transform; node != null && node != tile.transform; node = node.parent)
        {
            if (Boundary(node, tile)) break;
            if (node.GetComponent<LODGroup>() != null) requiredLod = node;
        }
        for (Transform? parent = unit.parent; parent != null && !Boundary(parent, tile); parent = parent.parent)
        {
            if (!WholeOrnament(parent)) break;
            unit = parent;
        }
        if (requiredLod != null && !requiredLod.IsChildOf(unit)) return false;

        var witnesses = new System.Collections.Generic.List<RetainedCore>();
        bool generated = false, reachedTile = false;
        Transform? scope = null;
        for (Transform? node = renderer.transform; node != null; node = node.parent)
        {
            if (node == tile.transform) reachedTile = true;
            if (!SafeNode(node, authored: !generated && node != tile.transform)) return false;
            if (node.name == "Generated Content")
            { if (scope == null) scope = node; generated = true; }
            if (!generated && scope == null && node.GetComponent<ProceduralWall>() != null) scope = node;
            // A shared authored box needs an actual retained member inside its own
            // native prefab. Never borrow a distant wall to justify a lone ornament box.
            if (!generated && node != tile.transform && node.GetComponent<ProceduralWall>() == null)
                foreach (Collider collider in node.GetComponents<Collider>())
                {
                    if (collider == null) continue;
                    if (node == renderer.transform || collider.isTrigger || collider.attachedRigidbody != null
                        || (collider is MeshCollider meshCollider && meshCollider.sharedMesh == renderer.GetComponent<MeshFilter>().sharedMesh)) return false;
                    MeshRenderer? represented = FindCore(node, unit, requiredRole);
                    if (represented == null) return false;
                    witnesses.Add(new RetainedCore(represented, node));
                }
        }
        if (!generated || !reachedTile || scope == null) return false;
        // Native ProceduralWall/Generated Content labels alone cannot prove that an
        // ornament is detachable: the retained original room core must actually exist.
        MeshRenderer? core = FindCore(scope, unit, requiredRole);
        if (core == null) return false;
        witnesses.Add(new RetainedCore(core, scope));
        cores = witnesses.ToArray();
        return true;
    }

    private static bool Boundary(Transform node, ProceduralMapTile tile) => node == tile.transform
        || node.name == "Generated Content" || node.GetComponent<ProceduralWall>() != null;

    private static bool Ornament(MeshRenderer renderer)
    {
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null && filter.sharedMesh != null
            && ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(filter.sharedMesh)
            && !FigureRendererGuard.IsFigureOrActorRenderer(renderer) && !FigureRendererGuard.HeldByPlayer(renderer)
            && !HeldProps.OwnsRendererOf(renderer.transform) && !PropGrab.OwnsRendererOf(renderer.transform);
    }

    private static bool WholeOrnament(Transform unit)
    {
        bool found = false;
        foreach (Transform member in unit.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (!SafeNode(member, authored: true) || member.GetComponent<Collider>() != null) return false;
            foreach (Renderer child in member.GetComponents<Renderer>())
            { if (child is not MeshRenderer mesh || !Ornament(mesh)) return false; found = true; }
            LODGroup group = member.GetComponent<LODGroup>();
            if (group != null)
                foreach (LOD lod in group.GetLODs())
                    foreach (Renderer lodMember in lod.renderers)
                        if (lodMember is not MeshRenderer mesh || !mesh.transform.IsChildOf(unit) || !Ornament(mesh)) return false;
        }
        return found;
    }

    private static MeshRenderer? FindCore(Transform scope, Transform unit, int requiredRole)
    {
        foreach (MeshRenderer member in scope.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
        {
            if (member == null || member.transform.IsChildOf(unit) || !member.enabled || member.forceRenderingOff
                || !member.gameObject.activeInHierarchy || FigureRendererGuard.IsFigureOrActorRenderer(member)
                || HeldProps.OwnsRendererOf(member.transform) || PropGrab.OwnsRendererOf(member.transform)) continue;
            MeshFilter filter = member.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null
                && ScenarioEnvironmentMeshBank.RoomArchitectureRole(filter.sharedMesh) == requiredRole
                && !ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(filter.sharedMesh)) return member;
        }
        return null;
    }

    private static bool SafeNode(Transform node, bool authored)
    {
        CInteractable interactable = node.GetComponent<CInteractable>();
        UnityGameEditorObject original = node.GetComponent<UnityGameEditorObject>();
        if ((interactable != null && interactable is not CInteractableTile)
            || (original != null && original.PropObject != null)
            || node.GetComponent<ProceduralProp>() != null || node.GetComponent<ProceduralDoorway>() != null
            || node.GetComponent<UnityGameEditorDoorProp>() != null
            || node.GetComponent<ActorBehaviour>() != null || node.GetComponent<CInteractableActor>() != null
            || node.GetComponent<Canvas>() != null || node.GetComponent<Light>() != null
            || node.GetComponent<Animator>() != null || node.GetComponent<Animation>() != null
            || node.GetComponent<ParticleSystem>() != null || node.GetComponent<Rigidbody>() != null
            || node.GetComponent<SkinnedMeshRenderer>() != null || node.name == "Preview") return false;
        if (!authored) return true;
        foreach (MonoBehaviour callback in node.GetComponents<MonoBehaviour>())
        {
            if (callback == null) return false;
            Type type = callback.GetType();
            if (type != typeof(MaterialLoader) && type != typeof(DetailsDisabler)
                && type != typeof(DetailLevelDisableProvider) && type != typeof(ImportantObjectsShadowsDisabler)
                && type != typeof(PropObjectsShadowsDisabler) && type != typeof(UnityGameEditorObject)
                && type != typeof(CInteractableTile) && type != typeof(ProceduralWall)) return false;
        }
        return true;
    }

}
