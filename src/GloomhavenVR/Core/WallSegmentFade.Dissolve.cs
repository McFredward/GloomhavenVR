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
/// <item>DRIVE: the swapped piece got the SAME per-renderer MPB ramp the wall renderers ran
///   — noise map + _Cutoff sweep. RETIRED by the EYE-LOCK ruling (round 14, see
///   <see cref="FadeDriver.TryBeginSwap"/>): that ramp decided each pixel through the
///   shader's SCREEN-UV map sample and its screen-radial/0.02·dist terms, i.e. per-eye
///   inputs under MultiPass. The swap no longer engages; if one ever does (switch off
///   <c>EyeLockRetiresSwap</c>), it is driven to the eye-locked binary instead.</item>
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
        /// <summary>The retirement notice is a one-shot (it is a ruling, not a heartbeat).</summary>
        private bool _swapRetiredLogged;
        /// <summary>EYE-LOCK ruling switch. <c>static readonly</c> on purpose: it must gate the
        /// swap at RUNTIME (so the machinery below stays compiled, reviewable and one edit away
        /// from revival) rather than compile it out.</summary>
        private static readonly bool EyeLockRetiresSwap = true;

        /// <summary>Capture the swap template from a LIVE toggle-native material (called
        /// from CollectWallFadeInfo — the material already proved its fade branch).</summary>
        private void CaptureMasonryTemplate(Material m)
        {
            if (_masonryFadeShader == null && m.shader != null)
                _masonryFadeShader = m.shader;
        }

        /// <summary>
        /// EYE-LOCK (round 14, user rule "either it fades in BOTH eyes or not at all"): the
        /// swap is RETIRED as a dissolve channel. It delivered its ramp through exactly the
        /// per-eye path the wall renderers just lost — the masonry shader's screen-UV
        /// occlusion-map sample plus the S term (screen-radial vignette + 0.02·dist, 8th
        /// power). On a swapped prop that produced the same rivalry as on a wall, only on a
        /// smaller surface: a torch or shell piece half-there in one eye. The algebra in the
        /// <see cref="WallSegmentFade"/> header shows the shader has no eye-identical PARTIAL
        /// state, so there is no way to keep this animation and the rule at the same time.
        /// Swapped pieces now fall back to the eye-identical channels the ledger already
        /// owns: their own object-UV cutoff/alpha ramp where the material has one, and
        /// <c>renderer.enabled</c> (a per-frame renderer state — inherently identical in
        /// both eyes) at the hide threshold. The machinery below stays intact and reversible
        /// so the next round can revive it the moment a view-independent dissolve channel
        /// exists (a mod-supplied shader, or an Amp dissolve pair on the template).
        /// </summary>
        private void TryBeginSwap(MountedProp p)
        {
            if (p.SwapChecked || p.SwapCopies != null)
                return;
            p.SwapChecked = true;
            if (_masonryFadeShader != null && !_swapRetiredLogged)
            {
                _swapRetiredLogged = true;
                VRLog.Info(Name,
                    "DISSOLVE-SWAP RETIRED (EYE-LOCK): the round-11 swap dissolved plain "
                    + $"pieces on '{_masonryFadeShader.name}' via the SCREEN-UV occlusion map "
                    + "and the shader's screen-radial vignette / 0.02·dist terms — per-eye "
                    + "inputs under MultiPass, i.e. the same rivalry the wall fade just "
                    + "removed. No eye-identical PARTIAL state exists on that shader (header "
                    + "algebra), so these pieces animate through their own object-UV cutoff/"
                    + "alpha ramp and disable at the hide threshold instead. Machinery kept "
                    + "for a future view-independent dissolve channel.");
            }
            if (EyeLockRetiresSwap || _masonryFadeShader == null || p.System != null
                || p.ColorId >= 0)
            {
                return;
            }
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

        /// <summary>Drive a swapped piece (only reachable while <see cref="EyeLockRetiresSwap"/>
        /// is off, or for a piece swapped before the ruling): the SAME eye-locked binary the
        /// wall renderers get — map r=0,a=0 ⇒ m ≡ 1 ⇒ clip = 1 − _Cutoff, so the piece is
        /// either untouched-solid or wholly discarded, never a per-eye partial. The swept
        /// screen-UV ramp this method used to run is exactly what the rule forbids.</summary>
        private void DriveSwappedProp(MountedProp p, float fade)
        {
            if (!EnsureTextures())
                return;
            if (fade < StaggerLastFade)
            {
                p.Renderer.SetPropertyBlock(null); // still vanilla-solid
                return;
            }
            _mountedMpb ??= new MaterialPropertyBlock();
            _mountedMpb.Clear();
            _mountedMpb.SetInteger(ToggleWallFadeId, 1);
            _mountedMpb.SetFloat(ToggleWallfadeMatId, 1f);
            _mountedMpb.SetFloat(WallFadeOnMatId, 1f);
            _mountedMpb.SetTexture(TilesOcclusionMapId, _hideMapTex!);
            _mountedMpb.SetFloat(CutoffId, HiddenCutoff);
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
