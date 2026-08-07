using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// DISSOLVE SWAP — round 11 (user requirement, verbatim intent: "ALLE assets die faden
/// sollen das immer mit der Animation tun"). Plain meshes delivered via renderer.enabled
/// (stacked-shell rides, wall bodies, corner pieces — the '[enabled-only]'/'→cutoff'
/// no-working-channel class) POPPED in and out. They now dissolve with the game's OWN
/// masonry fade look:
///
/// <list type="bullet">
/// <item>TEMPLATE: the first live toggle-native material seen this scene donates its shader
///   (<c>Amp_Basic_N_MRAO</c> with the <c>_WALLFADE_ON_ON</c> fade branch — the exact shader
///   the keep's masonry dissolves on natively). Captured in
///   <see cref="WallSegmentFade"/>'s CollectWallFadeInfo; without it (a scene with no such
///   material) the swap is unavailable and the pop remains — the documented fallback.</item>
/// <item>SWAP (at dissolve start, once per piece): the renderer's authored sharedMaterials
///   array is snapshotted and replaced by per-slot COPIES on the template shader; every
///   property the template shader declares is copied from the source material when it has
///   it (textures incl. tiling/offset, floats, colors, vectors — the Amp family shares
///   _MainTex/_BumpMap/_Tint/… so masonry pieces map 1:1; a foreign source simply keeps the
///   template defaults for what it lacks, which the swap census makes visible). The copy
///   enables the fade keyword and gate. NEVER a shared game material — only our copies are
///   written, and they are destroyed on restore/teardown.</item>
/// <item>DRIVE: the swapped piece gets the SAME per-renderer MPB ramp the wall renderers
///   run — noise map + _Cutoff sweep during the transition, held map at fade 1 — an OPAQUE
///   per-pixel clip dissolve (no alpha blending: the MR chroma-key ruling forbids
///   semi-transparent surfaces). renderer.enabled=false remains the final state at fade
///   1.0, exactly as before.</item>
/// <item>RESTORE: authored sharedMaterials reassigned bit-for-bit, our copies destroyed,
///   MPB cleared — on unfade, ownership release, toggle-off and teardown (the shared
///   ledger's restore path, so no piece can stay swapped without an owner).</item>
/// </list>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>The masonry fade shader donated by the first live toggle-native
        /// material of the scene (null = no swap available, pop fallback).</summary>
        private Shader? _masonryFadeShader;
        /// <summary>Swap census: pieces currently dissolving on swapped materials.</summary>
        private int _swapTotal;
        private float _nextSwapLog;

        /// <summary>Capture the swap template from a LIVE toggle-native material (called
        /// from CollectWallFadeInfo — the material already proved its fade branch).</summary>
        private void CaptureMasonryTemplate(Material m)
        {
            if (_masonryFadeShader == null && m.shader != null)
                _masonryFadeShader = m.shader;
        }

        /// <summary>
        /// Decide ONCE whether this prop should dissolve on a swapped material, and perform
        /// the swap. Scope: plain MESH pieces without a particle system, without an alpha
        /// channel (those animate already) and without a live native toggle (those dissolve
        /// natively) — exactly the pop class. Figures can never get here (every collector
        /// guards), and the arch/doorway never fades at all.
        /// </summary>
        private void TryBeginSwap(MountedProp p)
        {
            if (p.SwapChecked || p.SwapCopies != null)
                return;
            p.SwapChecked = true;
            if (_masonryFadeShader == null || p.System != null || p.ColorId >= 0)
                return;
            if (p.Renderer is not MeshRenderer mr || mr == null)
                return;
            Material[] src = mr.sharedMaterials;
            if (src == null || src.Length == 0)
                return;
            foreach (Material m in src)
            {
                if (m == null)
                    return; // half-built renderer — try again next dissolve
                if (HasLiveWallFadeToggle(m))
                    return; // native path already animates this piece
            }
            var copies = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
                copies[i] = BuildSwapMaterial(src[i]);
            p.SwapOriginals = src;
            p.SwapCopies = copies;
            mr.sharedMaterials = copies;
            _swapTotal++;
            float now = Time.unscaledTime;
            if (now >= _nextSwapLog)
            {
                _nextSwapLog = now + 5f;
                VRLog.Info(Name,
                    $"DISSOLVE-SWAP: '{mr.name}' (+{_swapTotal - 1} earlier) dissolves on "
                    + $"'{_masonryFadeShader.name}' material copies — textures/props copied "
                    + "from the authored materials, opaque per-pixel clip (no alpha "
                    + "blending — MR ruling); authored materials restored on unfade "
                    + "(round 11: everything that fades animates).");
            }
        }

        /// <summary>One swap copy: template shader + every template-declared property the
        /// source material can donate; fade keyword and gate enabled on OUR copy only.</summary>
        private Material BuildSwapMaterial(Material source)
        {
            var mat = new Material(_masonryFadeShader!)
            {
                name = "GloomhavenVR.DissolveSwap." + source.name,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Shader sh = _masonryFadeShader!;
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string prop = sh.GetPropertyName(i);
                if (!source.HasProperty(prop))
                    continue;
                switch (sh.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        mat.SetColor(prop, source.GetColor(prop));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        mat.SetVector(prop, source.GetVector(prop));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        mat.SetFloat(prop, source.GetFloat(prop));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        mat.SetTexture(prop, source.GetTexture(prop));
                        mat.SetTextureScale(prop, source.GetTextureScale(prop));
                        mat.SetTextureOffset(prop, source.GetTextureOffset(prop));
                        break;
                }
            }
            // A '_MainTex'-less source (e.g. 'Standard' uses _MainTex too, so this is rare)
            // falls back to the renderer's main texture via Unity's implicit mapping.
            if (mat.GetTexture("_MainTex") == null && source.mainTexture != null)
                mat.SetTexture("_MainTex", source.mainTexture);
            // OUR copy opens the fade branch (never a shared game material).
            mat.EnableKeyword(WallFadeOnKeyword);
            if (mat.HasProperty(WallFadeOnMatId))
                mat.SetFloat(WallFadeOnMatId, 1f);
            if (mat.HasProperty(CutoffId))
                mat.SetFloat(CutoffId, 0.5f);
            return mat;
        }

        /// <summary>Drive a swapped piece with the wall renderers' own map/cutoff ramp
        /// (called from DriveProp when <see cref="MountedProp.SwapCopies"/> is set).</summary>
        private void DriveSwappedProp(MountedProp p, float fade)
        {
            if (!EnsureTextures())
                return;
            _mountedMpb ??= new MaterialPropertyBlock();
            _mountedMpb.Clear();
            _mountedMpb.SetInteger(ToggleWallFadeId, 1);
            _mountedMpb.SetFloat(ToggleWallfadeMatId, 1f);
            _mountedMpb.SetFloat(WallFadeOnMatId, 1f);
            if (fade >= 1f)
            {
                _mountedMpb.SetTexture(TilesOcclusionMapId, _occludedTex!);
                _mountedMpb.SetFloat(CutoffId, 0.5f);
            }
            else
            {
                _mountedMpb.SetTexture(TilesOcclusionMapId, _noiseTex!);
                _mountedMpb.SetFloat(CutoffId, Mathf.Lerp(-0.05f, 1f, fade));
            }
            p.Renderer.SetPropertyBlock(_mountedMpb);
        }

        /// <summary>Undo a swap exactly: authored materials back (renderer permitting), our
        /// copies destroyed either way (a renderer that died mid-swap must not leak
        /// them).</summary>
        private static void RestorePropSwap(MountedProp p, Renderer? r)
        {
            if (p.SwapCopies == null)
            {
                p.SwapChecked = false;
                return;
            }
            if (r is MeshRenderer mr && mr != null && p.SwapOriginals != null)
                mr.sharedMaterials = p.SwapOriginals;
            foreach (Material m in p.SwapCopies)
            {
                if (m != null)
                    UnityEngine.Object.Destroy(m);
            }
            p.SwapCopies = null;
            p.SwapOriginals = null;
            p.SwapChecked = false;
        }
    }
}
