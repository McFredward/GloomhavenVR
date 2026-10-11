using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Positive native scenery-emitter provenance, independent of static mesh eligibility.
/// The Frame633 pictures show dark puffs above torches with environment FX already at
/// zero. Original pcg_crypt/necropolis/cave/stonerooms prefabs author p_fire_torch (8),
/// (9) and (1); stripping only Clone/Instance made the exact family check miss them.
/// Render material/shader identity is deliberately irrelevant: alpha, distortion and
/// additive children of the same decorative family all belong to this budget.
/// No figure, combat/condition pool, native controller, Light or water mesh is owned.
/// </summary>
internal static class ScenarioEnvironmentAmbientEffects
{
    private static string FamilyName(string name)
    {
        while (true)
        {
            name = name.TrimEnd();
            if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 7);
            else if (name.EndsWith("(Instance)", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 10);
            else if (name.EndsWith(")", StringComparison.Ordinal))
            {
                int open = name.LastIndexOf(" (", StringComparison.Ordinal);
                if (open < 0 || open + 2 == name.Length - 1) return name;
                for (int i = open + 2; i < name.Length - 1; i++)
                    if (name[i] < '0' || name[i] > '9') return name;
                name = name.Substring(0, open);
            }
            else return name;
        }
    }

    private static bool Family(string name) => name.StartsWith("p_Moths_", StringComparison.Ordinal)
        || name.StartsWith("Candle_Fire_FX_", StringComparison.Ordinal)
        || name is "p_fire_torch" or "p_fire_torch_blue" or "p_fire_torch_Demon_01"
            or "p_fireflies" or "p_Fireflies" or "P_SewerFog";

    private static bool ForeignEffect(string name) => name.StartsWith("P_", StringComparison.Ordinal)
        || name.StartsWith("p_", StringComparison.Ordinal)
        || name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("condition", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("invisib", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0;

    internal static bool Identity(Transform leaf, Transform tile)
    {
        bool family = false;
        for (Transform? node = leaf; node != null && node != tile; node = node.parent)
        {
            string name = FamilyName(node.name);
            if (Family(name)) family = true;
            else if (ForeignEffect(name)) return false;
        }
        // Do not return early at a familiar leaf: a pooled P_attack container above
        // an identically named fire emitter must still veto ownership.
        return family;
    }

    internal static ProceduralMapTile? Scope(Transform leaf)
    {
        bool generated = false;
        ProceduralMapTile? tile = null;
        for (Transform? node = leaf; node != null; node = node.parent)
        {
            if (node.GetComponent<Canvas>() != null || node.GetComponent<ActorBehaviour>() != null
                || node.GetComponent<SkinnedMeshRenderer>() != null || node.name == "Preview"
                || node.name.StartsWith("GloomhavenVR", StringComparison.Ordinal)) return null;
            if (node.name == "Generated Content") generated = true;
            tile = node.GetComponent<ProceduralMapTile>();
            if (tile != null) break;
        }
        if (tile == null || !generated) return null;
        // Animated door/prop scenery can own decorative fire. Static mesh restrictions
        // such as Animator, ProceduralDoorway and ProceduralProp are not evidence of a
        // combat effect; exact family identity above provides that separate boundary.
        for (Transform? parent = tile.transform; parent != null; parent = parent.parent)
        {
            if (parent.GetComponent<Canvas>() != null || parent.GetComponent<ActorBehaviour>() != null
                || parent.GetComponent<SkinnedMeshRenderer>() != null) return null;
            if (parent.GetComponent<ProceduralScenario>() != null) return tile;
        }
        Scene scene = tile.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.GetComponent<ProceduralScenario>() != null) return tile;
        return null;
    }

    internal static bool CanPause(ParticleSystem system) => system.main.stopAction == ParticleSystemStopAction.None
        && !system.collision.enabled && !system.trigger.enabled;
}
