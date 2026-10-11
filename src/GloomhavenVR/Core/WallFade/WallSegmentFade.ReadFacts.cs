using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // Private presentation shaders expose a union of several native property schemas.
        // Their names/gates are not the authored fade capability: in particular the world
        // shader has _Tint/_Cutoff but no native wall-fade material gate. Classifying it
        // turns opaque masonry into an inert alpha rider. Resolve current ownership at
        // each read, then keep the existing synchronous fact/Shader caches on that source.
        // Never memoize this mapping across frames or restore bindings just to inspect them.
        private static Material FadeSourceMaterial(Material material) =>
            ScenarioEnvironmentBudget.CanonicalMaterial(material);

        private static void ReadFadeMaterials(Renderer renderer, List<Material> destination)
        {
            destination.Clear();
            renderer.GetSharedMaterials(destination);
            for (int i = 0; i < destination.Count; i++)
                if (destination[i] != null)
                    destination[i] = FadeSourceMaterial(destination[i]);
        }

        // Build 599 warms 4,631 wall-cache children before a 121 ms WallCache phase, with no
        // rejected warm or changed anchor set. That warm is already retained correctly. The
        // remaining material loop still crosses Unity's native boundary repeatedly for the
        // same shared material: shader, gate properties/keyword and authored cutoff. Cache
        // those exact answers only inside the synchronous WallCache phase. This phase can
        // restore renderer material lists, but never changes a shared material's shader,
        // properties or keywords. Each renderer still reads its current material list, in
        // order; replacement/native-loaded materials therefore acquire their own entry.
        // Nothing survives a frame boundary, so native toggle changes and room reveals are
        // read afresh on the next pass. Outside this phase the original live queries remain.
        private readonly Dictionary<Material, WallMaterialFact> _wallCacheMaterialFacts = new(128);
        private bool _wallCacheMaterialFactsActive;

        private readonly struct WallMaterialFact
        {
            internal readonly bool Valid;
            internal readonly string Name;
            internal readonly bool Low;
            internal readonly bool ByName;
            internal readonly bool ByToggle;
            internal readonly bool HasCutoff;
            internal readonly float Cutoff;

            internal WallMaterialFact(ShaderFadeName name, bool byToggle,
                                      bool hasCutoff, float cutoff)
            {
                Valid = true;
                Name = byToggle ? name.Name + "(toggle-native)" : name.Name;
                Low = name.Low;
                ByName = name.ByName;
                ByToggle = byToggle;
                HasCutoff = hasCutoff;
                Cutoff = cutoff;
            }
        }

        private void BeginWallCacheMaterialFacts()
        {
            _wallCacheMaterialFacts.Clear();
            // Capture the switch once per synchronous phase, never per renderer.
            _wallCacheMaterialFactsActive = PerfConfig.SharedWallReadCacheOn;
        }

        private void EndWallCacheMaterialFacts()
        {
            _wallCacheMaterialFactsActive = false;
            _wallCacheMaterialFacts.Clear();
        }

        private WallMaterialFact WallMaterialFactOf(Material material)
        {
            material = FadeSourceMaterial(material);
            if (_wallCacheMaterialFactsActive
                && _wallCacheMaterialFacts.TryGetValue(material, out WallMaterialFact cached))
                return cached;
            Shader shader = material.shader;
            WallMaterialFact fact = default;
            if (shader != null)
            {
                ShaderFadeName name = FadeNameOf(shader);
                bool byToggle = !name.ByName && HasLiveWallFadeToggle(material);
                // Do not query cutoff for a material the original admission loop rejected.
                bool hasCutoff = (name.ByName || byToggle) && material.HasProperty(CutoffId);
                float cutoff = hasCutoff
                    ? Mathf.Clamp(material.GetFloat(CutoffId), 0.05f, 0.95f) : 0.5f;
                fact = new WallMaterialFact(name, byToggle, hasCutoff, cutoff);
            }
            if (_wallCacheMaterialFactsActive)
                _wallCacheMaterialFacts[material] = fact;
            return fact;
        }

        // The standing rule needs the NEAREST actor/animator root, rather than the bool
        // cached by FigureAncestry. Its old loop queried three components at every ancestor
        // for every renderer, including siblings with the identical parent. Keep the exact
        // nearest-root and inactive-node semantics; share results within the already existing
        // synchronous figure memo window only. Prepare slices and commits each open/close
        // their own window, so reparenting and component changes between frames stay live.
        private static readonly Dictionary<Transform, Transform?> FigureRootMemo = new(1024);
        private static bool _figureRootMemoActive;

        private static Transform? FigurePropRootOf(Transform? t)
        {
            if (_figureRootMemoActive)
                return FigurePropRootMemoized(t);
            while (t != null)
            {
                if (t.GetComponent<ActorBehaviour>() != null
                    || t.GetComponent<CInteractableActor>() != null
                    || t.GetComponent<Animator>() != null)
                    return t;
                t = t.parent;
            }
            return null;
        }

        private static Transform? FigurePropRootMemoized(Transform? t)
        {
            if (t == null)
                return null;
            if (FigureRootMemo.TryGetValue(t, out Transform? cached))
                return cached;
            Transform? root = t.GetComponent<ActorBehaviour>() != null
                || t.GetComponent<CInteractableActor>() != null
                || t.GetComponent<Animator>() != null
                ? t : FigurePropRootMemoized(t.parent);
            FigureRootMemo[t] = root;
            return root;
        }
    }
}
