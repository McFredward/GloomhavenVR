using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Optional ambient figure effects, separate from body LODs. Build619's LivingSpirit report
/// exposed the old four-demon allowlist. A complete original character-bundle census now
/// covers resident cosmetics of heroes, summons, bosses, normal and elite enemies, including
/// mesh-rendered eye bands, drake trails and exact supplemental alpha shells. Looping alone
/// is NOT evidence of ambience: attack buildups and invisibility also loop. Never admit pooled
/// combat/condition effects, unfamiliar descendants, world effects or UI. Exact original model,
/// effect and mesh-material provenance remains mandatory; body geometry is not an effect.
/// </summary>
internal static class ScenarioFigureEffects
{
    private static string OriginalName(string name)
    {
        // Native CharacterManager preserves the addressable model beneath an actor-GUID
        // wrapper. Choreographer/ActorBehaviour.material(s) instantiate its materials;
        // these Unity suffixes are provenance-preserving, not different authored assets.
        while (true)
        {
            if (name.EndsWith("(Clone)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 7).TrimEnd();
            else if (name.EndsWith("(Instance)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 10).TrimEnd();
            else return name.TrimEnd();
        }
    }

    private static bool IsModel(Transform item, Transform root, string wanted)
    {
        for (Transform? current = item; current != null; current = current.parent)
        {
            if (OriginalName(current.name) == wanted) return true;
            if (current == root) break;
        }
        return false;
    }

    // Build619 audits every original hero/NPC addressable bundle, not one test scenario.
    // These exact model/effect pairs are resident children of exported character prefabs.
    // Same-named standalone or pooled gameplay effects do not acquire model ownership.
    // Parent cosmetic containers and their authored P_ children are both listed because
    // generic substring "Idle" matching would either miss Elementalist/Savvas/summons or
    // silently capture an unrelated P_GainStrengthen attached beneath an idle container.
    private static readonly Dictionary<string, HashSet<string>> AmbientModels = new(StringComparer.Ordinal)
    {
        ["HE_Aesther_Summoner_PR"] = new(StringComparer.Ordinal) { "P_Summoner_Idle" },
        ["HE_Elementalist_PR"] = new(StringComparer.Ordinal) { "Elementalist_Idle_FX", "P_Elementalist_Chest", "P_Elementalist_Eye_L", "P_Elementalist_Eye_R", "P_Elementalist_Hand_L (1)", "P_Elementalist_Hand_R (1)", "P_Foot_L (2)", "P_Foot_R (2)" },
        ["HE_Nightshroud_PR"] = new(StringComparer.Ordinal) { "P_NightShroud_Idle" },
        ["HE_PlagueHerald_PR"] = new(StringComparer.Ordinal) { "P_PlagueHerald_Idle", "p_Fireflies_Small (1)", "p_Fireflies_Small (2)", "p_beetles" },
        ["HF_HE_Spellweaver_Burning_Avatar"] = new(StringComparer.Ordinal) { "P_Burning_Avatar" },
        ["HF_HE_Spellweaver_Mystic_Ally"] = new(StringComparer.Ordinal) { "P_Mystic_Ally (1)" },
        ["MO_Cultist_HighPriest_PR"] = new(StringComparer.Ordinal) { "P_Foot_L", "P_Foot_R", "P_HighCultist_Idle" },
        ["MO_Dark_Rider_PR"] = new(StringComparer.Ordinal) { "IdleFX", "P_DarkRider_Idle_HorseHead", "P_DarkRider_Idle_LB", "P_DarkRider_Idle_LF", "P_DarkRider_Idle_Neck", "P_DarkRider_Idle_RB", "P_DarkRider_Idle_RF" },
        ["MO_ElderDrake_PR"] = new(StringComparer.Ordinal) { "P_Flying_Idle (1)" },
        ["MO_FlameDemon_Elite_PR"] = new(StringComparer.Ordinal) { "P_FlameDemon" },
        ["MO_FlameDemon_PR"] = new(StringComparer.Ordinal) { "P_FlameDemon" },
        ["MO_Forest_Imp_Elite_PR"] = new(StringComparer.Ordinal) { "P_ForestImp_Idle_Elite" },
        ["MO_Forest_Imp_PR"] = new(StringComparer.Ordinal) { "P_ForestImp_Idle" },
        ["MO_Harrower_Infester_Elite_PR"] = new(StringComparer.Ordinal) { "P_HarrowerInfester_Idle (1)", "p_Fireflies_Small (1)" },
        ["MO_Harrower_Infester_PR"] = new(StringComparer.Ordinal) { "P_HarrowerInfester_Idle", "p_Fireflies_Small (1)" },
        ["MO_LivingSpirit_Elite_PR"] = new(StringComparer.Ordinal) { "P_Living_Spirit_Idle (1)" },
        ["MO_LivingSpirit_PR"] = new(StringComparer.Ordinal) { "P_Living_Spirit_Idle (1)" },
        ["MO_NightDemon_PR"] = new(StringComparer.Ordinal) { "P_NightDemon_Idle" },
        ["MO_Ooze_Elite_PR"] = new(StringComparer.Ordinal) { "P_Ooze_Idle" },
        ["MO_Ooze_Giant_PR"] = new(StringComparer.Ordinal) { "P_Ooze_Giant_Idle" },
        ["MO_Ooze_PR"] = new(StringComparer.Ordinal) { "P_Ooze_Idle" },
        ["MO_PrimeDemon_PR"] = new(StringComparer.Ordinal) { "PrimeDemon_IdleFX" },
        ["MO_SavvasIceStorm_Elite_PR"] = new(StringComparer.Ordinal) { "P_Elementalist_Chest", "P_Elementalist_Eye_L", "P_Elementalist_Eye_R", "P_Elementalist_Hand_L (1)", "P_Elementalist_Hand_R (1)", "P_Foot_L (2)", "P_Foot_R (2)", "Savvas_Icestorm_Idle_FX" },
        ["MO_SavvasIceStorm_PR"] = new(StringComparer.Ordinal) { "P_Elementalist_Chest", "P_Elementalist_Eye_L", "P_Elementalist_Eye_R", "P_Elementalist_Hand_L (1)", "P_Elementalist_Hand_R (1)", "P_Foot_L (2)", "P_Foot_R (2)", "Savvas_Icestorm_Idle_FX" },
        ["MO_SavvasLavaFlow_Elite_PR"] = new(StringComparer.Ordinal) { "P_Elementalist_Chest", "P_Elementalist_Eye_L", "P_Elementalist_Eye_R", "P_Elementalist_Hand_L (1)", "P_Elementalist_Hand_R (1)", "P_Foot_L (2)", "P_Foot_R (2)", "Savvas_LavaFlow_Idle_FX" },
        ["MO_SavvasLavaFlow_PR"] = new(StringComparer.Ordinal) { "P_Elementalist_Chest", "P_Elementalist_Eye_L", "P_Elementalist_Eye_R", "P_Elementalist_Hand_L (1)", "P_Elementalist_Hand_R (1)", "P_Foot_L (2)", "P_Foot_R (2)", "Savvas_LavaFlow_Idle_FX" },
        ["MO_SightlessEye_PR"] = new(StringComparer.Ordinal) { "P_Sightless_Eye", "P_SunDemon_CastRays (2)" },
        ["MO_SpittingDrake_Elite_PR"] = new(StringComparer.Ordinal) { "P_Flying_Idle" },
        ["MO_SpittingDrake_PR"] = new(StringComparer.Ordinal) { "P_Flying_Idle" },
        ["MO_SunDemon_Elite_PR"] = new(StringComparer.Ordinal) { "P_SunDemon_Idle" },
        ["MO_SunDemon_PR"] = new(StringComparer.Ordinal) { "P_SunDemon_Idle" },
        ["MO_TheBetrayer_PR"] = new(StringComparer.Ordinal) { "Betrayer_Idle_FX", "P_Elementalist_Chest", "P_Elementalist_Eye_L", "P_Elementalist_Eye_R", "P_Elementalist_Hand_L (1)", "P_Elementalist_Hand_R (1)", "P_Foot_L (2)", "P_Foot_R (2)" },
        ["MO_TheColorless_PR"] = new(StringComparer.Ordinal) { "Colorless_Idle_FX", "P_Elementalist_Chest", "P_Elementalist_Eye_L", "P_Elementalist_Eye_R", "P_Elementalist_Hand_L (1)", "P_Elementalist_Hand_R (1)", "P_Foot_L (2)", "P_Foot_R (2)" },
        ["MO_TheGloom_PR"] = new(StringComparer.Ordinal) { "P_TheGloom_Idle" },
        ["MO_WindDemon_Elite_PR"] = new(StringComparer.Ordinal) { "P_WindDemon_Idle" },
        ["MO_WindDemon_PR"] = new(StringComparer.Ordinal) { "P_WindDemon_Idle" },
        ["MO_Zephyr_PR"] = new(StringComparer.Ordinal) { "P_SummonerSummons_Idle_FX", "Zephyr_Idle_FX" },
        ["NPC_Hail_Censer_PR"] = new(StringComparer.Ordinal) { "Hail_Censer_FX", "P_HailOrb_FX", "P_Summoner_Idle", "P_WindDemon_Idle (1)" },
        ["NPC_Hail_PR"] = new(StringComparer.Ordinal) { "Hail_FX", "P_HailOrb_FX", "P_Summoner_Idle", "P_WindDemon_Idle (1)" },
        ["SU_BlackUnicorn_PR"] = new(StringComparer.Ordinal) { "P_SummonerSummons_Idle_FX" },
        ["SU_Doppelganger_PR"] = new(StringComparer.Ordinal) { "Elementalist_Idle_FX", "P_Elementalist_Chest", "P_Elementalist_Eye_L", "P_Elementalist_Eye_R", "P_Elementalist_Hand_L (1)", "P_Elementalist_Hand_R (1)", "P_Foot_L (2)", "P_Foot_R (2)" },
        ["SU_GiantBat_PR"] = new(StringComparer.Ordinal) { "P_SummonerSummons_Idle_FX" },
        ["SU_HealingSprite_PR"] = new(StringComparer.Ordinal) { "P_SummonerSummons_Idle_FX", "P_HealingSprite" },
        ["SU_IronBeast_PR"] = new(StringComparer.Ordinal) { "P_SummonerSummons_Idle_FX" },
        ["SU_JadeFalcon_PR"] = new(StringComparer.Ordinal) { "P_Flying_Idle (2)" },
        ["SU_LavaGolem_PR"] = new(StringComparer.Ordinal) { "P_SummonerSummons_Idle_FX", "p_fire_torch" },
        ["SU_LivingBomb_PR"] = new(StringComparer.Ordinal) { "P_LivingBomb", "P_SummonerSummons_Idle_FX" },
        ["SU_ManaSphere_PR"] = new(StringComparer.Ordinal) { "ManaSphere_FX", "P_WindDemon_Idle (1)" },
        ["SU_NailSphere_PR"] = new(StringComparer.Ordinal) { "P_SummonerSummons_Idle_FX" },
        ["SU_PlagueRat_PR"] = new(StringComparer.Ordinal) { "p_PlagueRat_Idle" },
        ["SU_RockColossus_PR"] = new(StringComparer.Ordinal) { "P_RockColossus", "P_SummonerSummons_Idle_FX" },
        ["SU_ShadowWolf_PR"] = new(StringComparer.Ordinal) { "P_ShadowWolf", "P_SummonerSummons_Idle_FX" },
        ["SU_Skeleton_PR"] = new(StringComparer.Ordinal) { "P_Skeleton" },
        ["SU_SlimeSpirit_PR"] = new(StringComparer.Ordinal) { "P_SlimeSpirit", "P_SummonerSummons_Idle_FX" },
        ["SU_Thornshooter_PR"] = new(StringComparer.Ordinal) { "P_SummonerSummons_Idle_FX", "P_ThornShooter" },
        ["SU_VoidEater_PR"] = new(StringComparer.Ordinal) { "P_SummonerSummons_Idle_FX", "P_VoidEater" },
        ["SU_Warhawk_PR"] = new(StringComparer.Ordinal) { "P_Flying_Idle (2)" },
        ["SU_WarriorSpirit_PR"] = new(StringComparer.Ordinal) { "P_Mystic_Ally (1)" },
    };

    private static readonly Dictionary<string, string> AmbientMeshes = new(StringComparer.Ordinal)
    {
        ["MO_Cultist_HighPriest_PR|geo_bendyband (3)"] = "HighCultist_EyeBand_Mat",
        ["MO_Cultist_HighPriest_PR|geo_bendyband (4)"] = "HighCultist_EyeBand_Mat",
        ["MO_Dark_Rider_PR|geo_bendyband"] = "DarkKnight_EyeBand_Mat",
        ["MO_Dark_Rider_PR|geo_bendyband (1)"] = "DarkKnight_EyeBand_Mat",
        ["MO_Dark_Rider_PR|geo_bendyband (2)"] = "DarkKnight_HeadBand_Mat",
        ["MO_ElderDrake_PR|geo_bendyband"] = "DarkKnight_EyeBand_Mat",
        ["MO_ElderDrake_PR|geo_bendyband (1)"] = "DarkKnight_EyeBand_Mat",
        ["MO_LivingSpirit_Elite_PR|geo_bendyband"] = "LivingSpirit_Elite_EyeBand_Mat",
        ["MO_LivingSpirit_Elite_PR|geo_bendyband (1)"] = "LivingSpirit_Elite_EyeBand_Mat",
        ["MO_LivingSpirit_Elite_PR|geo_bendyband (2)"] = "LivingSpirit_Elite_EyeBand_Mat",
        ["MO_LivingSpirit_PR|geo_bendyband"] = "LivingSpirit_EyeBand_Mat",
        ["MO_LivingSpirit_PR|geo_bendyband (1)"] = "LivingSpirit_EyeBand_Mat",
        ["MO_LivingSpirit_PR|geo_bendyband (2)"] = "LivingSpirit_EyeBand_Mat",
        ["SU_BlackUnicorn_PR|OmniDec_BlackUnicorn"] = "OmniDecalSummonerSummons_Mat",
        ["SU_GiantBat_PR|OmniDec_GiantBat"] = "OmniDecalSummonerSummons_Mat",
        ["SU_IronBeast_PR|OmniDec_IronBeast"] = "OmniDecalSummonerSummons_Mat",
        ["SU_LavaGolem_PR|OmniDec_LavaGolem"] = "OmniDecalSummonerSummons_Mat",
    };

    private static bool KnownIdle(Transform item, Transform root)
    {
        string name = OriginalName(item.name);
        for (Transform? current = item; current != null; current = current.parent)
        {
            if (AmbientModels.TryGetValue(OriginalName(current.name), out HashSet<string> effects)
                && effects.Contains(name)) return true;
            if (current == root) break;
        }
        return false;
    }

    private static bool KnownAmbientMesh(Renderer item, Transform root)
    {
        if (!AmbientPath(item.transform, root)) return false;
        Material[] materials = item.sharedMaterials;
        if (materials.Length != 1 || materials[0] == null) return false;
        string suffix = "|" + OriginalName(item.name);
        for (Transform? current = item.transform; current != null; current = current.parent)
        {
            if (AmbientMeshes.TryGetValue(OriginalName(current.name) + suffix, out string material)
                && OriginalName(materials[0].name) == material) return true;
            if (current == root) break;
        }
        return false;
    }

    private static bool AmbientPath(Transform item, Transform root)
    {
        if (item.GetComponentInParent<Canvas>(true) != null) return false;
        for (Transform? current = item; current != null; current = current.parent)
        {
            if (KnownIdle(current, root)) return true;
            string name = OriginalName(current.name);
            // Authored Idle children have generic particle names. A subsequently pooled
            // ability/condition prefab keeps its own P_ identity: never capture it by ancestry.
            if (name.StartsWith("P_", StringComparison.Ordinal)
                || name.StartsWith("p_", StringComparison.Ordinal)
                || name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("condition", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("invisib", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (current == root) break;
        }
        return false;
    }

    private static bool SupplementalAlpha(SkinnedMeshRenderer item, Transform root)
    {
        string name = OriginalName(item.name);
        string model, body, material;
        if (name == "MO_WindDemon_Alpha")
        { model = "MO_WindDemon_PR"; body = "MO_WindDemon_main"; material = "MO_WindDemon_Alpha_MAT"; }
        else if (name == "MO_WindDemon_Elite_Alpha")
        { model = "MO_WindDemon_Elite_PR"; body = "MO_WindDemon_Elite_main"; material = "MO_WindDemon_Elite_Alpha_MAT"; }
        else if (name == "MO_FlameDemon_Alpha")
        {
            bool elite = IsModel(item.transform, root, "MO_FlameDemon_Elite_PR");
            model = elite ? "MO_FlameDemon_Elite_PR" : "MO_FlameDemon_PR";
            body = "MO_FlameDemon_Mesh";
            material = elite ? "MO_FlameDemon_Elite_Alpha_MAT" : "MO_FlameDemon_Alpha_MAT";
        }
        else return false;
        if (item.sharedMesh == null || OriginalName(item.sharedMesh.name) != name
            || item.GetComponentInParent<Canvas>(true) != null || !IsModel(item.transform, root, model)) return false;
        Material[] materials = item.sharedMaterials;
        if (materials.Length != 1 || materials[0] == null || OriginalName(materials[0].name) != material) return false;
        // A shell is optional only with its exact independently rendered opaque body.
        Transform parent = item.transform.parent;
        if (parent == null) return false;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform sibling = parent.GetChild(i);
            if (OriginalName(sibling.name) != body) continue;
            SkinnedMeshRenderer nativeBody = sibling.GetComponent<SkinnedMeshRenderer>();
            if (nativeBody != null && nativeBody.sharedMesh != null
                && OriginalName(nativeBody.sharedMesh.name) == body && nativeBody.sharedMesh.vertexCount > 0)
                return true;
        }
        return false;
    }

    private sealed class Mask
    {
        internal Renderer Renderer = null!;
        internal bool Owned;
        internal bool IsOwned => Owned && Renderer != null && Renderer.forceRenderingOff;
        internal void Apply(bool reduce)
        {
            if (!reduce && !Owned) return;
            if (Renderer == null) { Owned = false; return; }
            if (reduce)
            {
                if (!Renderer.forceRenderingOff) { Renderer.forceRenderingOff = true; Owned = true; }
            }
            else if (Owned)
            {
                if (Renderer.forceRenderingOff) Renderer.forceRenderingOff = false;
                Owned = false;
            }
        }
    }

    private sealed class Particle
    {
        internal ParticleSystem System = null!;
        internal Mask? Mask;
        internal bool MayPause;
        internal bool Paused;
        internal void Apply(bool reduce)
        {
            Mask?.Apply(reduce);
            if (!reduce && !Paused) return;
            if (System == null) { Paused = false; return; }
            if (reduce)
            {
                // Pause preserves native particle clocks/buffers and removes solver work.
                // Never Stop/Clear a native system: its stopAction may recycle or destroy it.
                // Each cached child owns its own policy; withChildren would stop combat FX.
                if (MayPause && System.isPlaying && !System.isPaused) { System.Pause(false); Paused = true; }
            }
            else if (Paused)
            {
                // A foreign native stop while suppressed wins. Originally stopped/paused
                // systems were never claimed and must not start on quality restoration.
                if (System.isPaused) System.Play(false);
                Paused = false;
            }
        }
    }

    private sealed class Trail
    {
        internal TrailRenderer Renderer = null!;
        internal Mask Mask = null!;
        internal bool OwnedEmission;
        internal void Apply(bool reduce)
        {
            Mask.Apply(reduce);
            if (!reduce && !OwnedEmission) return;
            if (Renderer == null) { OwnedEmission = false; return; }
            if (reduce)
            {
                if (Renderer.emitting) { Renderer.emitting = false; OwnedEmission = true; }
            }
            else if (OwnedEmission)
            {
                if (!Renderer.emitting) Renderer.emitting = true;
                OwnedEmission = false;
            }
        }
    }

    internal sealed class Record
    {
        private readonly List<Particle> _particles = new();
        private readonly List<Trail> _trails = new();
        private readonly List<Mask> _shells = new();
        private readonly HashSet<Renderer> _renderers = new();
        private readonly HashSet<int> _particleIds = new();
        private int _density = 100;
        internal int ExaminedParticles;
        internal string CatalogModel = "";
        internal int Count => _particles.Count + _trails.Count + _shells.Count;
        internal int PausedParticles
        {
            get { int count = 0; foreach (Particle item in _particles) if (item.Paused && item.System != null && item.System.isPaused) count++; return count; }
        }
        internal int MaskedRenderers
        {
            get
            {
                int count = 0;
                foreach (Particle item in _particles) if (item.Mask != null && item.Mask.IsOwned) count++;
                foreach (Trail item in _trails) if (item.Mask.IsOwned) count++;
                foreach (Mask item in _shells) if (item.IsOwned) count++;
                return count;
            }
        }
        internal bool Contains(Renderer renderer) => _renderers.Contains(renderer);
        internal void Apply(int density)
        {
            density = Mathf.Clamp(density, 0, 100);
            // LOD/cloth can keep the figure driver active with original FX quality. Once
            // restored, that setting must cost no per-frame particle/renderer API reads.
            if (density == 100 && _density == 100) return;
            _density = density;
            int keep = Mathf.RoundToInt(Count * density / 100f);
            int index = 0;
            foreach (Particle item in _particles) item.Apply(index++ >= keep);
            foreach (Trail item in _trails) item.Apply(index++ >= keep);
            foreach (Mask item in _shells) item.Apply(index++ >= keep);
        }
        internal void Restore() => Apply(100);
        private bool CaptureParticle(ParticleSystem system, GameObject root, ActorBehaviour actor)
        {
            if (system == null || _particleIds.Contains(system.GetInstanceID())) return false;
            ActorBehaviour other = system.GetComponentInParent<ActorBehaviour>(true);
            if (other != null && other != actor || !AmbientPath(system.transform, root.transform)) return false;
            if (CatalogModel.Length == 0)
                for (Transform? current = system.transform; current != null; current = current.parent)
                {
                    string name = OriginalName(current.name);
                    if (AmbientModels.ContainsKey(name)) { CatalogModel = name; break; }
                    if (current == root.transform) break;
                }
            // Some original resident insect/summon systems have native collision modules. Their
            // optional image can disappear, but callbacks/collisions must keep running.
            // Splitting rendering from solver ownership avoids either retaining every
            // visible particle or changing native side effects by pausing its simulation.
            bool mayPause = system.main.stopAction == ParticleSystemStopAction.None
                && !system.collision.enabled && !system.trigger.enabled;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            Mask? mask = renderer != null ? new Mask { Renderer = renderer } : null;
            if (renderer != null) _renderers.Add(renderer);
            _particleIds.Add(system.GetInstanceID()); _particles.Add(new Particle { System = system, Mask = mask, MayPause = mayPause });
            return true;
        }
        internal bool MaterialReady(Renderer renderer, GameObject root, ActorBehaviour actor)
        {
            if (renderer == null || _renderers.Contains(renderer)
                || !renderer.transform.IsChildOf(root.transform)) return false;
            ActorBehaviour other = renderer.GetComponentInParent<ActorBehaviour>(true);
            if (other != null && other != actor) return false;
            if (renderer is ParticleSystemRenderer)
                return CaptureParticle(renderer.GetComponent<ParticleSystem>(), root, actor);
            if (renderer is TrailRenderer trail)
            {
                if (!AmbientPath(trail.transform, root.transform)) return false;
                _renderers.Add(trail); _trails.Add(new Trail { Renderer = trail, Mask = new Mask { Renderer = trail } });
                return true;
            }
            bool supplemental = renderer is SkinnedMeshRenderer shell && SupplementalAlpha(shell, root.transform);
            if (!supplemental && !KnownAmbientMesh(renderer, root.transform)) return false;
            _renderers.Add(renderer); _shells.Add(new Mask { Renderer = renderer });
            return true;
        }
        internal static Record Capture(GameObject root, ActorBehaviour actor)
        {
            var record = new Record();
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            record.ExaminedParticles = systems.Length;
            foreach (ParticleSystem system in systems)
                record.CaptureParticle(system, root, actor);
            foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true))
                record.MaterialReady(trail, root, actor);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                record.MaterialReady(renderer, root, actor);
            return record;
        }
    }
}
