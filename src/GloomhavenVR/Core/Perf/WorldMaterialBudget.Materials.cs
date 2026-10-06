using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class WorldMaterialBudget
{
    // Native HIGH and LOW expose different emissive switches. Both material and
    // slot/renderer MPBs must retain unsupported animated/emissive presentation.
    private static readonly string[] EffectProperties = { "_AddVertexAnim", "_UseEmissiveMap",
        "_Diffuse_Emissive_On", "_EmissionMap", "_UseTextureEmission", "_Fresnel_On", "_AdvancedEmission",
        "_MossTexture_ON", "_MossTexture_Noise_ON", "_ToggleDissolve", "_Cutout_VertexPos_Influence" };
    private static readonly string[] EffectKeywords = { "_ADDVERTEXANIM_ON", "_ENABLE_ANIM",
        "_USEEMISSIVEMAP_ON", "_DIFFUSE_EMISSIVE_ON_ON", "_USE_TEXTURE_EMISSION", "_FRESNEL_ON_ON",
        "_MOSSTEXTURE_ON_ON", "_MOSSTEXTURE_NOISE_ON_ON", "_EMISSION", "_DETAIL_MULX2", "_PARALLAXMAP" };
    private static readonly string[] StandardStateProperties = { "_Mode", "_SrcBlend", "_DstBlend", "_ZWrite" };
    private static bool ProvenProgram(Material material, int route)
    {
        // Intersection, not union: native objects with the SAME shader name have
        // stripped program tables despite identical properties/pass/keyword schemas.
        // Material keywords therefore need one jointly proven albedo/clip branch.
        int features = 0;
        foreach (string keyword in material.shaderKeywords)
        {
            int bit = keyword switch
            {
                "_WORLDSPACE_ON" => 1, "_WALLFADE_ON_ON" => 2, "_DIFUSE_ALPHA_ON_ON" => 4,
                "_DESATURATION_ON" => 8, "_TOGGLEWALLFADE_ON" => 16, "_TOGGLEWALLFADEOFF_ON" => 32,
                // These channels are the explicitly selected lighting compromise;
                // they cannot alter the retained native UV/albedo/clip equation.
                "DIRECTIONAL" or "LIGHTPROBE_SH" or "INSTANCING_ON" or "LIGHTMAP_ON"
                    or "DYNAMICLIGHTMAP_ON" or "SHADOWS_SCREEN" or "SHADOWS_DEPTH"
                    or "FOG_LINEAR" or "FOG_EXP" or "FOG_EXP2" or "_NORMALMAP"
                    or "_DETAIL_NORM_ON_ON" or "_METALLICGLOSSMAP" or "_SPECGLOSSMAP"
                    or "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A" or "_SPECULARHIGHLIGHTS_OFF"
                    or "_GLOSSYREFLECTIONS_OFF" => 0,
                _ => -1,
            };
            if (bit < 0) return false;
            features |= bit;
        }
        return route switch
        {
            1 => features is 0 or 2 or 6,
            2 => features is 0 or 24,
            3 => features is 0 or 32 or 1,
            4 => features is 0 or 8 or 32 or 40 or 1,
            5 => features is 0 or 1,
            6 => features is 0 or 8 or 1,
            9 or 10 => features == 0,
            _ => false,
        };
    }
    private static int ShaderRoute(Material original) => original == null || original.shader == null ? -1 : original.shader.name switch
    {
        "Amp_Basic_N_MRAO" => 1,
        "Amp_Low/Amp_Basic_N_MRAO_Low" => 2,
        "Amp_Basic_WallFade" => 3,
        "Amp_Low/Amp_Basic_WallFade_Low" => 4,
        "Amp_Basic" => 5,
        "Amp_Low/Amp_Basic_Low" => 6,
        "Standard" => 9,
        "Legacy Shaders/Diffuse" => 10,
        _ => -1,
    };
    private static int Route(Material original)
    {
        int route = ShaderRoute(original);
        if (route < 0 || original.renderQueue > 2500) return -1;
        string shader = original.shader.name;
        foreach (string property in EffectProperties)
        {
            // Standard's _EmissionMap is a texture; the same native spelling is a
            // scalar switch in the Amp families. Do not issue a mismatched getter.
            if (shader == "Standard" && property == "_EmissionMap") continue;
            if (original.HasProperty(property) && original.GetFloat(property) != 0f) return -1;
        }
        foreach (string keyword in EffectKeywords) if (original.IsKeywordEnabled(keyword)) return -1;
        bool basic = shader == "Standard" || shader == "Legacy Shaders/Diffuse";
        if (!original.HasProperty("_MainTex") || !original.HasProperty(basic ? "_Color" : "_Tint")
            || original.GetTexture("_MainTex") is RenderTexture) return -1;
        if (shader == "Standard" && (original.GetFloat("_Mode") != 0f
            || original.GetFloat("_SrcBlend") != 1f || original.GetFloat("_DstBlend") != 0f
            || original.GetFloat("_ZWrite") != 1f || original.GetColor("_EmissionColor").maxColorComponent > 0f)) return -1;
        return ProvenProgram(original, route) ? route : -1;
    }
    private sealed partial class Driver
    {
        private readonly Dictionary<Material, Material> _variants = new();
        private readonly Dictionary<Material, Material> _originalByVariant = new();
        private readonly Dictionary<Material, Material> _prepared = new();
        // Native MPB construction is forbidden in a MonoBehaviour field initializer.
        // Awake runs on Unity's main thread after the component has been created.
        private MaterialPropertyBlock _block = null!, _slotBlock = null!;
        private Shader? _shader;
        internal bool IsVariant(Material material) => material != null && _originalByVariant.ContainsKey(material);
        internal Material Canonical(Material material) => material != null
            && _originalByVariant.TryGetValue(material, out Material original) ? original : material!;
        private Material Source(Material material)
        {
            Material original = Canonical(material);
            return original != null ? _canonicalSource?.Invoke(original) ?? original : original!;
        }
        internal Material VariantFor(Material input)
        {
            Material original = Source(input);
            Settings();
            if (original == null || _mode == 0 || _failed || !isActiveAndEnabled) return original!;
            if (_prepared.TryGetValue(original, out Material prepared)) return prepared;
            int route = Route(original);
            if (route < 0) { _prepared[original] = original; return original; }
            if (_ensureAssets?.Invoke() == false) { _prepared[original] = original; return original; }
            _shader ??= BundleShaders.Resolve(ShaderName, "Perf", "World material shader available.",
                "World material shader unavailable; original world materials retained.");
            if (_shader == null) { _prepared[original] = original; return original; }
            if (!_variants.TryGetValue(original, out Material variant))
            {
                variant = new Material(original) { name = original.name + " (VR world material)" };
                variant.shader = _shader;
                _variants.Add(original, variant); _originalByVariant.Add(variant, original);
            }
            // One exact original refresh per synchronous pass, never a mutable native
            // verdict across eyes. Property names, texture ST and native keywords stay
            // intact, including their distinction between the HIGH and LOW families.
            variant.CopyPropertiesFromMaterial(original);
            variant.shader = _shader;
            variant.shaderKeywords = original.shaderKeywords;
            variant.renderQueue = original.renderQueue;
            variant.SetOverrideTag("RenderType", original.GetTag("RenderType", false, ""));
            variant.enableInstancing = original.enableInstancing;
            variant.SetFloat("_GHVRWorldMaterialMode", _mode);
            variant.SetFloat("_GHVRWorldNativeRoute", route);
            _refreshes++;
            _prepared.Add(original, variant);
            return variant;
        }
        private bool RendererEffect(MeshRenderer renderer, int slot, Material original)
        {
            int route = ShaderRoute(original);
            if (route < 0) return false;
            if (!renderer.HasPropertyBlock()) return false;
            renderer.GetPropertyBlock(_block);
            renderer.GetPropertyBlock(_slotBlock, slot);
            foreach (string property in EffectProperties)
            {
                // A native spelling can have a different type in another family.
                // In particular Standard's _EmissionMap is a texture, not AMP's
                // float switch. Typed MPB presence also avoids absent-value reads.
                if (route == 9 && property == "_EmissionMap") continue;
                if (_block.HasFloat(property) && _block.GetFloat(property) != 0f
                    || _slotBlock.HasFloat(property) && _slotBlock.GetFloat(property) != 0f) return true;
            }
            if (route == 9)
                foreach (string property in StandardStateProperties)
                    if (_block.HasFloat(property) && _block.GetFloat(property) != original.GetFloat(property)
                        || _slotBlock.HasFloat(property) && _slotBlock.GetFloat(property) != original.GetFloat(property)) return true;
            return false;
        }
    }
}
