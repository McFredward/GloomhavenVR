using UnityEngine;
using GloomhavenVR.Board.FigureGrab;
using System.Collections.Generic;
using System.Reflection;

namespace GloomhavenVR.Core;

internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>Shared cold-census and live-adoption ownership verdict. Build607
        /// signatures folded native-named home-ghost children such as MO_Spitting_Drake_Mesh
        /// and OcclusionVolume: visual-only clones intentionally keep their original names,
        /// layers, bones and LOD topology, so a child name/layer cannot establish ownership.
        /// FigureVisualMirror is the exact component installed by CloneVisual/BindHighlight,
        /// including inactive descendants. Native actor ancestry alone grants no exemption.
        /// A mirror mesh using an actual wall-fade shader still retains the conservative
        /// signature term in ClassifySlice; this predicate never writes native presentation.
        /// </summary>
        private static bool IsModPresentation(Renderer renderer, string name) =>
            renderer.gameObject.layer == VRLayers.ModLayer
            || ModVisualOwnership.IsName(name)
            || IsFigureVisualMirrorRenderer(renderer);

        // A renderer can be inspected by several adoption lanes in one synchronous
        // commit. Reuse this exact Unity parent query only in the existing read-cache
        // scope. Begin/EndFigureMemo clear it; no mutable ancestry crosses a frame.
        // Cold classification already retains RendererFact.Mod for the renderer lifetime.
        private static readonly Dictionary<Renderer, bool> VisualMirrorOwnershipMemo = new(1024);

        private static bool IsFigureVisualMirrorRenderer(Renderer renderer)
        {
            if (_figureRootMemoActive
                && VisualMirrorOwnershipMemo.TryGetValue(renderer, out bool owned)) return owned;
            bool mirror = renderer.GetComponentInParent<FigureVisualMirror>(true) != null;
            if (_figureRootMemoActive) VisualMirrorOwnershipMemo[renderer] = mirror;
            return mirror;
        }

        /// <summary>Build 605's 321.56 ms table rebuild was caused solely by the destruction
        /// of HexCenter_Proj and HexHighlight selection visuals. These native components own
        /// decals/selection particles, never masonry, floor samples or wall attachments. Treat
        /// their exact published renderer references as non-wall presentation alongside mod
        /// visuals. This changes neither selection rendering nor native callbacks; pooled stars
        /// may activate, emit and die without rebuilding an unrelated wall table.
        ///
        /// Do not exempt by object name: a real environmental mesh can have the same name.
        /// Do not exempt UnseenGroundPlane: it is not one of the published selection arrays.
        /// </summary>
        private static bool IsNativeHexSelectionVisual(Renderer renderer)
        {
            HexSelect_Control? selector = renderer.GetComponentInParent<HexSelect_Control>(true);
            if (selector != null && ReferenceEquals(selector.HexProjector, renderer))
                return true;
            if (!(renderer is ParticleSystemRenderer))
                return false;
            // The shipped resources.assets HexHighlight prefab (GameObject 5746) owns
            // HexSelect_Control 11576, ParticleSystem 8156 and its renderer 8674 on
            // the SAME root. CreateStar spawns that root through m_GenericHexStar.
            // It is not in the later ParticleBits/ParticleHover child arrays. Exact
            // same-object native ownership admits it; merely sharing a parent never does.
            if (renderer.GetComponent<HexSelect_Control>() != null)
                return true;
            HexSelectControlParticles? particles =
                renderer.GetComponentInParent<HexSelectControlParticles>(true);
            if (particles == null) return false;
            ParticleSystem? system = renderer.GetComponent<ParticleSystem>();
            if (system == null) return false;
            return SelectionArrayOwns(particles.ParticleBits, system)
                || SelectionArrayOwns(particles.ParticleHover, system);
        }

        // Build618 hardware: five published waypoint particles moving into/out of the
        // pool triggered 210/166 ms unrelated wall commits. Detached shield/retaliate
        // effects caused further commits. Recognize ORIGINAL owners and prefab references,
        // not names (Sparks/Rings also occur on actual wall sconces and water features).
        // These verdicts are shared by signature classification AND live collectors.
        private static readonly FieldInfo? NativeSpawnedObjectsField = typeof(ObjectPool)
            .GetField("spawnedObjects", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? NativePooledObjectsField = typeof(ObjectPool)
            .GetField("pooledObjects", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly HashSet<GameObject> NativeActionRoots = new();
        private static bool _nativeActionRootsReady;
        private static readonly Dictionary<Renderer, bool> NativeVisualOwnershipMemo = new(256);
        private static readonly List<Material> NativeVisualMaterials = new(8);

        private static bool IsNativeNonWallPresentation(Renderer renderer)
        {
            if (_figureRootMemoActive && NativeVisualOwnershipMemo.TryGetValue(renderer, out bool cached))
                return cached;
            bool owned = IsNativeHexSelectionVisual(renderer);
            if (!owned && renderer is ParticleSystemRenderer && renderer.GetComponent<ParticleSystem>() != null)
                owned = IsNativeWaypointParticle(renderer) || IsNativeActionParticle(renderer);
            // A published FX reference does not grant authority over actual wall shaders
            // or water-protection inputs. Both remain full signature/collector inputs.
            if (owned && HasProtectedNativePresentationMaterial(renderer)) owned = false;
            if (_figureRootMemoActive) NativeVisualOwnershipMemo[renderer] = owned;
            return owned;
        }

        private static bool IsNativeWaypointParticle(Renderer renderer)
        {
            WaypointHolder? holder = renderer.GetComponentInParent<WaypointHolder>(true);
            if (holder == null || holder.m_Prefabs == null) return false;
            foreach (WaypointHolder.WaypointPrefab member in holder.m_Prefabs)
                if (member != null && member.Prefab != null
                    && renderer.transform.IsChildOf(member.Prefab.transform)) return true;
            return false;
        }

        private static bool IsNativeActionParticle(Renderer renderer)
        {
            // ObjectPool publishes instance -> ORIGINAL prefab for active roots and
            // ORIGINAL prefab -> instance list for parked roots. Preserve the claim while
            // native recycling toggles/reparents the same effect; both exact dictionaries
            // are borrowed read-only for ONE synchronous ancestry window. No names or
            // scene-lifetime claim survive pool replacement, reassignment or destruction.
            if (!_figureRootMemoActive || !_nativeActionRootsReady) ReadNativeActionRoots();
            for (Transform? node = renderer.transform; node != null; node = node.parent)
                if (NativeActionRoots.Contains(node.gameObject)) return true;
            return false;
        }

        private static void ReadNativeActionRoots()
        {
            NativeActionRoots.Clear();
            _nativeActionRootsReady = true;
            ObjectPool? pool = ObjectPool.instance;
            GlobalSettings? settings = GlobalSettings.Instance;
            if (pool == null || settings == null) return;
            var spawns = NativeSpawnedObjectsField?.GetValue(pool) as Dictionary<GameObject, GameObject>;
            var parked = NativePooledObjectsField?.GetValue(pool) as Dictionary<GameObject, List<GameObject>>;
            if (spawns != null)
                foreach (KeyValuePair<GameObject, GameObject> entry in spawns)
                    if (entry.Key != null && IsPublishedActionPrefab(entry.Value, settings)) NativeActionRoots.Add(entry.Key);
            if (parked != null)
                foreach (KeyValuePair<GameObject, List<GameObject>> entry in parked)
                    if (IsPublishedActionPrefab(entry.Key, settings) && entry.Value != null)
                        foreach (GameObject root in entry.Value)
                            if (root != null && (spawns == null || !spawns.ContainsKey(root)))
                                NativeActionRoots.Add(root);
        }

        private static bool IsPublishedActionPrefab(GameObject prefab, GlobalSettings settings)
        {
            if (prefab == null) return false;
            GlobalSettings.GlobalParticleEffects? global = settings.m_GlobalParticles;
            GlobalSettings.ActiveBonusBuffTargetEffects? bonus = settings.m_ActiveBonusBuffTargetEffects;
            GlobalSettings.MagicEffects? magic = settings.m_MagicEffects;
            return global != null && (ReferenceEquals(prefab, global.DefaultHealEffect)
                    || ReferenceEquals(prefab, global.DefaultPositiveCondition)
                    || ReferenceEquals(prefab, global.DefaultNegativeCondition)
                    || ReferenceEquals(prefab, global.DefaultCharacterReveal)
                    || ReferenceEquals(prefab, global.DefaultCharacterSwap))
                || bonus != null && (ReferenceEquals(prefab, bonus.AttackBuffTargetEffect)
                    || ReferenceEquals(prefab, bonus.ShieldActiveBonusTargetEffect)
                    || ReferenceEquals(prefab, bonus.RetaliateActiveBonusTargetEffect)
                    || ReferenceEquals(prefab, bonus.GainShield) || ReferenceEquals(prefab, bonus.GainRetaliate)
                    || ReferenceEquals(prefab, bonus.GainDisarm) || ReferenceEquals(prefab, bonus.GainImmobilize)
                    || ReferenceEquals(prefab, bonus.GainPoison) || ReferenceEquals(prefab, bonus.GainStun)
                    || ReferenceEquals(prefab, bonus.GainWound) || ReferenceEquals(prefab, bonus.GainBless)
                    || ReferenceEquals(prefab, bonus.GainCurse) || ReferenceEquals(prefab, bonus.GainSleep)
                    || ReferenceEquals(prefab, bonus.GainStrengthen) || ReferenceEquals(prefab, bonus.GainMuddle)
                    || ReferenceEquals(prefab, bonus.GainInvisibility) || ReferenceEquals(prefab, bonus.GainAddTarget)
                    || ReferenceEquals(prefab, bonus.GainAddHeal) || ReferenceEquals(prefab, bonus.GainAddRange)
                    || ReferenceEquals(prefab, bonus.GainAttackersGainDisadvantage)
                    || ReferenceEquals(prefab, bonus.GainAttackActiveBonus) || ReferenceEquals(prefab, bonus.GainDefault))
                || magic != null && (ReferenceEquals(prefab, magic.RetaliateHit)
                    || ReferenceEquals(prefab, magic.RetaliateTarget) || ReferenceEquals(prefab, magic.WoundDamage));
        }

        private static bool HasProtectedNativePresentationMaterial(Renderer renderer)
        {
            if (IsWaterNameFamily(renderer.name)) return true;
            NativeVisualMaterials.Clear();
            renderer.GetSharedMaterials(NativeVisualMaterials);
            foreach (Material material in NativeVisualMaterials)
            {
                if (material == null || material.shader == null) continue;
                string shader = material.shader.name;
                if (IsWallFadeShaderName(shader)
                    || shader.IndexOf("Water_Sh", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>Only particles under an actual active native actor are outside every
        /// wall-table consumer. FootstepSound spawns its native prefab under m_FeetArray;
        /// detached projectile/world particles are deliberately not admitted. A live query
        /// preserves pool reparenting. Water still contributes protection rects. This is a
        /// signature exemption only: original effects, callbacks and visibility are untouched.
        /// </summary>
        private static bool IsActorParticleSignatureExempt(Renderer renderer, bool water)
        {
            if (!(renderer is ParticleSystemRenderer) || water
                || renderer.GetComponent<ParticleSystem>() == null) return false;
            ActorBehaviour? owner = renderer.GetComponentInParent<ActorBehaviour>(true);
            return owner != null && owner.gameObject.activeInHierarchy;
        }

        private static bool SelectionArrayOwns(ParticleSystem[]? systems, ParticleSystem system)
        {
            if (systems == null) return false;
            for (int i = 0; i < systems.Length; i++)
                if (ReferenceEquals(systems[i], system)) return true;
            return false;
        }
    }
}
