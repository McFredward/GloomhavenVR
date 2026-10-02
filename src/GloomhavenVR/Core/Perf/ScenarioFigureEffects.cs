using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Optional ambient figure effects, separate from body LODs. The Frame604 hardware pictures
/// show persistent Wind/Sun demon wisps despite working mesh caps. Native bundle inspection
/// found their authored Idle particle subtrees and the separate Wind/Flame Alpha meshes;
/// none has a native lower body replacement. Reduce those identified cosmetics only. Looping
/// alone is NOT evidence of ambience: attack buildups and invisibility also loop. Never admit
/// pooled combat/condition effects, unfamiliar descendants, world effects or UI.
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
            else return name;
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

    private static bool KnownIdle(Transform item, Transform root)
    {
        string name = OriginalName(item.name);
        return name == "P_WindDemon_Idle" && (IsModel(item, root, "MO_WindDemon_PR") || IsModel(item, root, "MO_WindDemon_Elite_PR"))
            || name == "P_SunDemon_Idle" && (IsModel(item, root, "MO_SunDemon_PR") || IsModel(item, root, "MO_SunDemon_Elite_PR"))
            || name == "P_NightDemon_Idle" && IsModel(item, root, "MO_NightDemon_PR")
            || name == "P_FlameDemon" && (IsModel(item, root, "MO_FlameDemon_PR") || IsModel(item, root, "MO_FlameDemon_Elite_PR"));
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
                if (System.isPlaying && !System.isPaused) { System.Pause(false); Paused = true; }
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
            // Callback/collision systems have possible native side effects. Keep them.
            if (system.main.stopAction != ParticleSystemStopAction.None
                || system.collision.enabled || system.trigger.enabled) return false;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            Mask? mask = renderer != null ? new Mask { Renderer = renderer } : null;
            if (renderer != null) _renderers.Add(renderer);
            _particleIds.Add(system.GetInstanceID()); _particles.Add(new Particle { System = system, Mask = mask });
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
            if (renderer is not SkinnedMeshRenderer shell || !SupplementalAlpha(shell, root.transform)) return false;
            _renderers.Add(shell); _shells.Add(new Mask { Renderer = shell });
            return true;
        }
        internal static Record Capture(GameObject root, ActorBehaviour actor)
        {
            var record = new Record();
            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
                record.CaptureParticle(system, root, actor);
            foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true))
                record.MaterialReady(trail, root, actor);
            foreach (SkinnedMeshRenderer shell in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                record.MaterialReady(shell, root, actor);
            return record;
        }
    }
}
