using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        private const int NativeTransitionSteps = 64;
        // Original compiled binding: HIGH cb0[6].x / N_MRAO cb0[10].x. The
        // historical _ToggleWallfade alias is absent from both original programs.
        private static readonly int NativeMapEnableId = Shader.PropertyToID("_EnableOcclusionMap");
        private Texture2D[]? _nativeTransitionMaps;
        private int[]? _nativeNoiseOrder;

        // Whitelist exact original DXBC branches. A themed or future material keeps
        // its existing delivery until its fragment route is verified independently.
        private static bool NativeHighTransitionShaderName(string name) =>
            name == "Amp_Basic_WallFade" || name == "Amp_Basic_N_MRAO"
            || name == "Amp_Basic_N_MRAO(toggle-native)"; // cached fact's diagnostic suffix

        private static bool NativeHighTransitionMaterials(List<Material> materials)
        {
            if (materials.Count == 0) return false;
            foreach (Material material in materials)
                if (material == null || material.shader == null ||
                    !NativeHighTransitionShaderName(material.shader.name)) return false;
            return true;
        }

        internal void PrepareNativeTransitionMaps()
        {
            if (EnsureTextures()) NativeTransitionMap(0f);
        }

        // Build619: the original HIGH fragment changes its foundation multiplier when
        // M crosses exactly zero (B = M>0 ? 1 : S). Continuous noisy M remains positive
        // throughout a cutoff sweep, so upper fragments can survive until the entire
        // map switches to held M=0. Correct intermediate MPBs cannot prevent that pop.
        // Supply native SOLID (M=1) or native HELD (M=0) per texel instead, progressively
        // by the same ranked Perlin field. The original shader, authored cutoff, opaque
        // depth, native foundation/vignette and both endpoints remain unchanged.
        // The bank is at most 64 x 64 x 64 x 4 = 1 MiB without mipmaps.
        // Scenario preparation warms it under the genuine loading symbol.
        // Point filtering is essential: bilinear would reintroduce positive M between
        // binary neighbours. Maps are cached once; no frame allocates/uploads a texture.
        private Texture NativeTransitionMap(float fade)
        {
            if (_nativeTransitionMaps == null)
            {
                const int size = 64;
                int[] order = _nativeNoiseOrder!;
                var maps = new Texture2D[NativeTransitionSteps];
                var pixels = new Color32[size * size];
                try
                {
                    for (int step = 0; step < maps.Length; step++)
                    {
                        int hidden = step * order.Length / NativeTransitionSteps;
                        for (int rank = 0; rank < order.Length; rank++)
                            pixels[order[rank]] = new Color32(rank < hidden ? (byte)255 : (byte)0, 0, 0, 0);
                        var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
                        {
                            name = "GloomhavenVR.WallFadeNativeProgress" + step,
                            wrapMode = TextureWrapMode.Repeat,
                            filterMode = FilterMode.Point,
                            anisoLevel = 0,
                            hideFlags = HideFlags.HideAndDontSave,
                        };
                        maps[step] = texture;
                        texture.SetPixels32(pixels);
                        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                    }
                    _nativeTransitionMaps = maps;
                }
                catch
                {
                    foreach (Texture2D texture in maps)
                        if (texture != null) UnityEngine.Object.Destroy(texture);
                    throw;
                }
            }
            if (fade >= 1f) return _occludedTex!;
            return _nativeTransitionMaps[Mathf.Clamp(Mathf.FloorToInt(fade * NativeTransitionSteps), 0, NativeTransitionSteps - 1)];
        }

        private void ReleaseNativeTransitionMaps()
        {
            if (_nativeTransitionMaps != null)
                foreach (Texture2D texture in _nativeTransitionMaps)
                    if (texture != null) UnityEngine.Object.Destroy(texture);
            _nativeTransitionMaps = null;
            _nativeNoiseOrder = null;
        }
    }
}
