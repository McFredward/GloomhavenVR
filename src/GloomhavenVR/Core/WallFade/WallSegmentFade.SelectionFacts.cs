using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
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
            HexSelectControlParticles? particles =
                renderer.GetComponentInParent<HexSelectControlParticles>(true);
            if (particles == null) return false;
            ParticleSystem? system = renderer.GetComponent<ParticleSystem>();
            if (system == null) return false;
            return SelectionArrayOwns(particles.ParticleBits, system)
                || SelectionArrayOwns(particles.ParticleHover, system);
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
