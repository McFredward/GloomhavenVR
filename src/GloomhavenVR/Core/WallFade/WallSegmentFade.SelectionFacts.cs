using UnityEngine;
using GloomhavenVR.Board.FigureGrab;
using System.Collections.Generic;

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
