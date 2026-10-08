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
    internal static bool TryAdmit(MeshRenderer renderer, ProceduralMapTile tile, out Transform? unit)
    {
        unit = null;
        if (renderer == null || tile == null) return false;
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null
            || !ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(filter.sharedMesh)
            || FigureRendererGuard.IsFigureOrActorRenderer(renderer)
            || FigureRendererGuard.HeldByPlayer(renderer)
            || HeldProps.OwnsRendererOf(renderer.transform)
            || PropGrab.OwnsRendererOf(renderer.transform)) return false;

        // LOD members share their actual native density unit. An unknown/mixed
        // group, light, collider or controller cannot inherit an ornament label.
        unit = renderer.transform;
        for (Transform? node = renderer.transform; node != null && node != tile.transform; node = node.parent)
        {
            if (node.name == "Generated Content") break;
            LODGroup group = node.GetComponent<LODGroup>();
            if (group == null) continue;
            foreach (LOD lod in group.GetLODs())
                foreach (Renderer member in lod.renderers)
                {
                    MeshFilter memberFilter = member != null ? member.GetComponent<MeshFilter>() : null!;
                    if (member is not MeshRenderer || memberFilter == null || memberFilter.sharedMesh == null
                        || !ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(memberFilter.sharedMesh)) return false;
                }
            unit = node;
            break;
        }
        foreach (Transform member in unit.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (!SafeNode(member, authored: true) || member.GetComponent<Collider>() != null) return false;
            Renderer child = member.GetComponent<Renderer>();
            if (child != null)
            {
                MeshFilter childFilter = member.GetComponent<MeshFilter>();
                if (child is not MeshRenderer || childFilter == null || childFilter.sharedMesh == null
                    || !ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(childFilter.sharedMesh)) return false;
            }
        }

        bool generated = false, reachedTile = false;
        for (Transform? node = renderer.transform; node != null; node = node.parent)
        {
            if (node == tile.transform) reachedTile = true;
            if (!SafeNode(node, authored: !generated && node != tile.transform)) return false;
            if (node.name == "Generated Content") generated = true;
            // Shared native wall/tile boxes stay represented by their room cores.
            // Other boxes require an actual retained original solid member; a
            // convenient parent name or another removable ornament is insufficient.
            if (!generated && node != tile.transform
                && node.GetComponent<ProceduralWall>() == null)
                foreach (Collider collider in node.GetComponents<Collider>())
                    if (collider != null && (!RetainedCollision(node, renderer, collider))) return false;
        }
        return generated && reachedTile;
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

    private static bool RetainedCollision(Transform owner, MeshRenderer ornament, Collider collider)
    {
        if (owner == ornament.transform || collider.isTrigger || collider.attachedRigidbody != null) return false;
        if (collider is MeshCollider meshCollider
            && meshCollider.sharedMesh == ornament.GetComponent<MeshFilter>().sharedMesh) return false;
        foreach (MeshRenderer member in owner.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
        {
            if (member == null || member == ornament || !member.enabled || member.forceRenderingOff
                || !member.gameObject.activeInHierarchy) continue;
            MeshFilter filter = member.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            int role = ScenarioEnvironmentMeshBank.RoomArchitectureRole(filter.sharedMesh);
            if ((role == 1 || role == 2) && !ScenarioEnvironmentMeshBank.IsArchitecturalOrnament(filter.sharedMesh)) return true;
        }
        return false;
    }
}
