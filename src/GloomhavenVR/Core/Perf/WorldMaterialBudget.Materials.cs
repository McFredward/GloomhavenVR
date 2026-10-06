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
    private static int Route(Material original)
    {
        if (original == null || original.shader == null || original.renderQueue > 2500) return -1;
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
        if (shader == "Standard" && (original.GetFloat("_Mode") != 0f && original.GetFloat("_Mode") != 1f
            || original.GetFloat("_SrcBlend") != 1f || original.GetFloat("_DstBlend") != 0f
            || original.GetFloat("_ZWrite") != 1f || original.GetColor("_EmissionColor").maxColorComponent > 0f)) return -1;
        // No combined native WORLD+ALPHA MRAO program was present in the audited
        // bytecode table. Do not invent an equation for this unsupported combination.
        if ((shader == "Amp_Basic_N_MRAO" || shader == "Amp_Low/Amp_Basic_N_MRAO_Low")
            && original.IsKeywordEnabled("_WORLDSPACE_ON") && original.IsKeywordEnabled("_DIFUSE_ALPHA_ON_ON")) return -1;
        return shader switch
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
    }
    private sealed partial class Driver
    {
        private readonly Dictionary<Material, Material> _variants = new();
        private readonly Dictionary<Material, Material> _originalByVariant = new();
        private readonly Dictionary<Material, Material> _prepared = new();
        private readonly MaterialPropertyBlock _block = new(), _slotBlock = new();
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
            variant.enableInstancing = original.enableInstancing;
            variant.SetFloat("_GHVRWorldMaterialMode", _mode);
            variant.SetFloat("_GHVRWorldNativeRoute", route);
            _refreshes++;
            _prepared.Add(original, variant);
            return variant;
        }
        private bool RendererEffect(MeshRenderer renderer, int slot)
        {
            if (!renderer.HasPropertyBlock()) return false;
            renderer.GetPropertyBlock(_block);
            renderer.GetPropertyBlock(_slotBlock, slot);
            foreach (string property in EffectProperties)
                if (_block.GetFloat(property) != 0f || _slotBlock.GetFloat(property) != 0f) return true;
            return false;
        }
    }
}
