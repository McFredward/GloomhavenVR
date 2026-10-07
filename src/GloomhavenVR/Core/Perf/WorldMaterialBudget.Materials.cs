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
    private static readonly int[] EffectPropertyIds = PropertyIds(EffectProperties);
    private static readonly int[] StandardStatePropertyIds = PropertyIds(StandardStateProperties);
    private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
    private static int[] PropertyIds(string[] names)
    {
        int[] ids = new int[names.Length];
        for (int i = 0; i < names.Length; i++) ids[i] = Shader.PropertyToID(names[i]);
        return ids;
    }
    private sealed class MaterialMetadata
    {
        internal readonly int Route;
        private string[]? _keywords;
        internal MaterialMetadata(Material original) => Route = ShaderRoute(original);
        internal string[] Keywords(Material original) => _keywords ??= original.shaderKeywords;
    }
    private static bool NativePassesEnabled(Material original)
    {
        // Pass enablement is mutable native presentation. CopyProperties does not
        // translate a disabled native pass to this shader's differently named pass.
        // Read actual names once per unique original in this synchronous pass.
        for (int pass = 0; pass < original.passCount; pass++)
        {
            string name = original.GetPassName(pass);
            if (!string.IsNullOrEmpty(name) && !original.GetShaderPassEnabled(name)) return false;
        }
        // AMP HIGH/Legacy can inherit a caster through Fallback; LOW has its own
        // CUSTOM_SHADOW_PASS. Explicitly disabled fallback/caster remains native.
        return original.GetShaderPassEnabled("ShadowCaster") && original.GetShaderPassEnabled("CUSTOM_SHADOW_PASS");
    }
    private static bool ProvenProgram(string[] keywords, int route)
    {
        // Intersection, not union: native objects with the SAME shader name have
        // stripped program tables despite identical properties/pass/keyword schemas.
        // Material keywords therefore need one jointly proven albedo/clip branch.
        int features = 0;
        foreach (string keyword in keywords)
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
    private static int Route(Material original, MaterialMetadata metadata)
    {
        int route = metadata.Route;
        if (route < 0 || original.renderQueue > 2500 || !NativePassesEnabled(original)) return -1;
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
        return ProvenProgram(metadata.Keywords(original), route) ? route : -1;
    }
    private sealed partial class Driver
    {
        private readonly Dictionary<Material, Material> _variants = new();
        private readonly Dictionary<Material, Material> _originalByVariant = new();
        private readonly Dictionary<Material, Material> _prepared = new();
        private readonly Dictionary<Material, MaterialMetadata> _metadata = new();
        private float _passAmbient;
        private MaterialMetadata Metadata(Material original)
        {
            if (!_shareReads) return new MaterialMetadata(original);
            if (!_metadata.TryGetValue(original, out MaterialMetadata metadata))
            { metadata = new MaterialMetadata(original); _metadata.Add(original, metadata); }
            return metadata;
        }
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
        { Settings(); return Prepare(Source(input)); }
        private Material Prepare(Material original)
        {
            if (original == null || _mode == 0 || _failed || !isActiveAndEnabled) return original!;
            if (_prepared.TryGetValue(original, out Material prepared)) return prepared;
            MaterialMetadata metadata = Metadata(original);
            int route = Route(original, metadata);
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
            variant.shaderKeywords = metadata.Keywords(original);
            variant.renderQueue = original.renderQueue;
            variant.SetOverrideTag("RenderType", original.GetTag("RenderType", false, ""));
            variant.enableInstancing = original.enableInstancing;
            variant.SetFloat("_GHVRWorldMaterialMode", _mode);
            variant.SetFloat("_GHVRWorldNativeRoute", route);
            float ambient = _shareReads ? _passAmbient : _ambientWeight?.Invoke() ?? 1f;
            variant.SetFloat("_GHVRWorldAmbientWeight", float.IsNaN(ambient) || float.IsInfinity(ambient)
                ? 1f : Mathf.Clamp01(ambient));
            _refreshes++;
            // Factory-only terrain/chunk consumers can refresh before the final
            // owner pass. Report their actual work instead of resetting it away.
            if (PerfMonitor.StepsActive && VRLog.Level >= VRLogLevel.Debug)
                PerfMonitor.Count("WorldMaterial.FactoryVariantRefreshes");
            _prepared.Add(original, variant);
            return variant;
        }
        private bool RendererEffect(MeshRenderer renderer, int slot, Material original, bool blocks)
        {
            if (!blocks || original == null) return false;
            int route = _shareReads ? Metadata(original).Route : ShaderRoute(original);
            if (route < 0) return false;
            renderer.GetPropertyBlock(_slotBlock, slot);
            if (_block.HasTexture(MainTextureId) && _block.GetTexture(MainTextureId) is RenderTexture
                || _slotBlock.HasTexture(MainTextureId) && _slotBlock.GetTexture(MainTextureId) is RenderTexture) return true;
            for (int index = 0; index < EffectPropertyIds.Length; index++)
            {
                // A native spelling can have a different type in another family.
                // In particular Standard's _EmissionMap is a texture, not AMP's
                // float switch. Typed MPB presence also avoids absent-value reads.
                if (route == 9 && EffectProperties[index] == "_EmissionMap") continue;
                int property = EffectPropertyIds[index];
                if (_block.HasFloat(property) && _block.GetFloat(property) != 0f
                    || _slotBlock.HasFloat(property) && _slotBlock.GetFloat(property) != 0f) return true;
            }
            if (route == 9)
                foreach (int property in StandardStatePropertyIds)
                    if (_block.HasFloat(property) && _block.GetFloat(property) != original.GetFloat(property)
                        || _slotBlock.HasFloat(property) && _slotBlock.GetFloat(property) != original.GetFloat(property)) return true;
            return false;
        }
    }
}
