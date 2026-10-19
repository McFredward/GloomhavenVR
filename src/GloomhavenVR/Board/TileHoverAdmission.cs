using System;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// Admit a prop tooltip only on the native hex the laser actually hits.
///
/// Build674 hardware report: trap/terrain hints appear while the laser is slightly outside
/// their tile. HoverRegisterer raycasts elevated prop colliders independently of BoardPick's
/// hex ray: a cube or capsule can be hit before the ray reaches a neighbouring hex, or even
/// when it misses the board. Its volume is not the prop's rules-side tile footprint.
/// Resources/Hex uses its original convex hex MeshCollider; its rectangular BoxCollider is
/// disabled. Keep that exact native pick, including board scale/rotation and edge geometry,
/// and read the picked tile's current members. Do not approximate a hex with a radius, round
/// a position to its nearest centre, resize colliders or change gameplay targeting/clicks.
/// </summary>
internal static class TileHoverAdmission
{
    internal static bool Allows(Vector3 origin, Vector3 direction, float maxDistance,
                                LayerMask hexMask, GameObject target)
    {
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, hexMask))
            return false;
        TileBehaviour? behaviour = hit.collider.GetComponentInParent<TileBehaviour>();
        return Contains(behaviour != null ? behaviour.m_ClientTile?.m_Tile : null, target);
    }

    internal static bool Contains(CTile? tile, GameObject target)
    {
        if (tile == null || target == null)
            return false;

        CObjectProp? original = target.GetComponent<UnityGameEditorObject>()?.PropObject;
        string? name = original == null ? target.name : null;
        foreach (CObjectProp prop in tile.m_Props)
            if (Same(prop, original, name))
                return true;

        // Spawner props have their own native tile collection. Their info callback is still
        // the original IHoverable; only its spatial admission changes here.
        foreach (CSpawner spawner in tile.m_Spawners)
            if (spawner != null && Same(spawner.Prop, original, name))
                return true;

        return false;
    }

    private static bool Same(CObjectProp? member, CObjectProp? original, string? name)
    {
        if (member == null)
            return false;
        if (original != null)
        {
            // A native state refresh may replace the rules objects while retaining the
            // same visual. The immutable GUID survives that refresh without an allocation.
            return ReferenceEquals(member, original)
                   || (!string.IsNullOrEmpty(original.PropGuid)
                       && string.Equals(member.PropGuid, original.PropGuid, StringComparison.Ordinal));
        }
        // Authored initial scenery can lack PropObject; native terrain/trap callbacks use
        // this same exact InstanceName identity. No prefab/type/nearest-tile guess is valid.
        return string.Equals(member.InstanceName, name, StringComparison.Ordinal);
    }
}
