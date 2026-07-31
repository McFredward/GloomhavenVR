using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// Kills the head-coupled "reflection" swimming inside the hex-selection highlight
/// (user issue #6; on hardware the artifact shows only in the RIGHT eye — classic
/// multipass staleness).
///
/// GROUND TRUTH (DXBC disassembly of the game's shader, 2026-07-21; evidence in
/// tools/ShaderDisasm/evidence/OmniDecal_Shd.*):
///
/// The highlight mesh <c>HexSelect_Control.HexProjector</c> ('HexCenter_Proj', the
/// builtin Cube scaled (2, 0.3, 2) — a Cull-Front decal BOX) uses shader
/// <b><c>OmniDecal_Shd</c></b>. Its fragment program is a screen-space
/// depth-reconstruction projector: it derives the shaded surface point from
/// <c>_CameraDepthTexture</c> + <c>unity_CameraInvProjection</c>/<c>unity_CameraToWorld</c>
/// per pixel, and samples EVERY visible layer (soft fill, crisp border, animated
/// border flames, pulsing target frame) at that reconstructed object-space position.
/// In the mod's multipass XR the depth texture / UnityPerCameraRare matrices bound
/// during the right-eye pass are stale left-eye/mono state → the pattern lands
/// differently per eye and per head pose (the "reflection").
///
/// PROPER FIX (this class, config <c>SwapStableShader</c>): swap the material's
/// shader to the bundled <b><c>GloomhavenVR/HexDecalStable</c></b>
/// (unity/GloomhavenVR.Assets/Assets/Bundle/Table/HexDecalStable.shader) — an
/// instruction-for-instruction port of the recovered OmniDecal algebra that replaces
/// the depth reconstruction with an analytic per-pixel view-ray ∩ tile-plane
/// intersection (only per-eye-correct inputs; no depth texture, no screen-space
/// UVs → inherently stereo-stable; equivalence argument in the shader header).
/// Property NAMES are identical, so the game's per-state writes in
/// <c>ProjectorMaterialAdjustment()</c> keep landing, and Unity carries all matching
/// property values (textures included) across the <c>Material.shader</c> assignment.
/// The swap happens in the same postfix that previously only zeroed layers, so it
/// re-applies after every material re-creation (<c>m_Material = new Material(_exampleMaterial)</c>
/// on each refresh). The pre-swap renderQueue (4000) is re-asserted after the swap.
///
/// OCCLUSION: vanilla's decal ran ZTest Always, so the highlight drew THROUGH
/// walls and figures. The stable shader exports the true floor-point depth per
/// pixel (SV_Depth of the ray∩plane point — the box mesh's own fragments sit
/// below the floor and would z-fail under a naive LEqual) and defaults ZTest to
/// LEqual, so figures standing on the hex and walls in front now occlude the
/// highlight like any other geometry, while ZWrite stays off. Config
/// <c>StableZTest</c> (4=LEqual default, 8=vanilla Always) and
/// <c>StableDepthBias</c> (anti-z-fight bias vs the tile floor) are re-applied on
/// every swap/postfix for on-device experiments; the applied ZTest is logged.
///
/// FALLBACK (old bundle without the shader, or <c>SwapStableShader=false</c>): the
/// previous mitigation stays — zero the swimming layers (<c>_BorderFlameIntensity</c>,
/// <c>_CrossHair</c> by default; <c>_BorderLineIntensity</c>/<c>_HexIntensity</c> as
/// bisect knobs). When the swap IS active the kill knobs are bypassed: the layers
/// no longer swim, so the full vanilla look comes back.
///
/// SHUTDOWN: swapped materials are tracked and restored to the original shader in
/// <see cref="Reset"/> (best effort — the game recreates materials from
/// <c>_exampleMaterial</c> on the next refresh anyway).
///
/// SAFETY: postfix body fully try/caught (WorldUI lesson: an unguarded NRE in a
/// per-frame game path starves input); work happens only when the game itself just
/// rewrote the material (state changes), not per-frame.
/// </summary>
internal static class HexHighlightFix
{
    private const string Scope = "HexHighlightFix";

    /// <summary>Bundle asset path of the stable decal shader (BuildBundles packs everything under Assets/Bundle/).</summary>
    private const string StableShaderAssetPath = "Assets/Bundle/Table/HexDecalStable.shader";
    private const string StableShaderName = "GloomhavenVR/HexDecalStable";
    private const string OriginalShaderName = "OmniDecal_Shd";

    // -------- config (own file: dev.gloomhavenvr.hexhighlight.cfg) --------

    internal static ConfigEntry<bool>? SwapStableShader;
    internal static ConfigEntry<int>? StableZTest;
    internal static ConfigEntry<float>? StableDepthBias;
    internal static ConfigEntry<bool>? KillBorderFlame;
    internal static ConfigEntry<bool>? KillCrosshair;
    internal static ConfigEntry<bool>? KillBorderLine;
    internal static ConfigEntry<bool>? KillFill;
    internal static ConfigEntry<bool>? LogMaterialDump;

    private static ConfigFile? _file;

    internal static void BindConfig()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("hexhighlight");

        SwapStableShader = config.Bind(
            "HexHighlight", "SwapStableShader", Defaults.SwapStableShader,
            "Replace the hex highlight's OmniDecal_Shd with the mod's stereo-stable " +
            "GloomhavenVR/HexDecalStable (same look, no screen-space depth reconstruction " +
            "— removes the per-eye 'reflection' that swims with head movement). When the " +
            "shader is missing from an older bundle, the Kill* knobs below apply instead.");
        StableZTest = config.Bind(
            "HexHighlight", "StableZTest", Defaults.StableZTest,
            "ZTest (UnityEngine.Rendering.CompareFunction) applied to the stable hex decal. " +
            "4 = LEqual (default): the shader exports the true floor-point depth per pixel " +
            "(SV_Depth), so figures standing on the hex and walls in front occlude the " +
            "highlight like normal geometry. 8 = Always: vanilla behavior — the highlight " +
            "draws through everything (on-device fallback if the depth export misbehaves). " +
            "Re-applied on every highlight state change, so edits take effect live.");
        StableDepthBias = config.Bind(
            "HexHighlight", "StableDepthBias", Defaults.StableDepthBias,
            "Camera-ward depth-buffer-space bias added to the stable hex decal's exported " +
            "depth. Prevents z-fighting speckle against the tile floor the highlight lies " +
            "on. Raise slightly if the highlight speckles/dropouts; lower toward 0 if it " +
            "visibly bleeds over the very bottom of figure bases.");
        KillBorderFlame = config.Bind(
            "HexHighlight", "KillBorderFlame", Defaults.KillBorderFlame,
            "FALLBACK (used only when the stable shader swap is off/unavailable): zero " +
            "_BorderFlameIntensity on hex highlight materials (OmniDecal_Shd). Kills the " +
            "animated border-flame layer — the screen-space depth-projected layer that " +
            "swims with head movement in VR. The white fill and border line stay.");
        KillCrosshair = config.Bind(
            "HexHighlight", "KillCrosshair", Defaults.KillCrosshair,
            "FALLBACK (used only when the stable shader swap is off/unavailable): zero " +
            "_CrossHair. Kills the pulsing target-frame/crosshair graphic projected " +
            "INSIDE the hex during target selection — same swimming projection.");
        KillBorderLine = config.Bind(
            "HexHighlight", "KillBorderLine", Defaults.KillBorderLine,
            "FALLBACK bisect knob: additionally zero _BorderLineIntensity (the crisp " +
            "border ring). Enable if the swimming artifact persists with the " +
            "flame/crosshair killed. Changes the look (hex loses its sharp outline).");
        KillFill = config.Bind(
            "HexHighlight", "KillFill", Defaults.KillFill,
            "FALLBACK bisect knob: additionally zero _HexIntensity (the soft white fill). " +
            "Only for diagnosis — this removes most of the highlight.");
        LogMaterialDump = config.Bind(
            "HexHighlight", "LogMaterialDump", Defaults.LogMaterialDump,
            "Log the hex highlight material's shader name and full property dump for the " +
            "first few materials seen (evidence for tuning the fix).");
    }

    // -------- stable shader lookup (OverlayShader/BoardLitShader probe pattern) --------

    private static Shader? _stableShader;
    private static Shader? _originalShader;   // kept for best-effort restore on Reset()
    private static bool _stableFoundLogged;
    private static bool _stableMissLogged;
    private static bool _knobsBypassLogged;
    /// <summary>Last ZTest value logged for the stable shader; -1 = none yet (log on change only).</summary>
    private static int _lastLoggedZTest = -1;

    /// <summary>
    /// The bundled stable decal shader, or null when no loaded bundle ships it (old
    /// bundle). Like GloomhavenVR/Overlay it is referenced only by runtime C#, so
    /// Shader.Find fails until it is loaded explicitly from a bundle; re-probed until
    /// present so a late bundle load still resolves. Logged once each way.
    /// </summary>
    private static Shader? StableShader()
    {
        if (_stableShader == null)
        {
            _stableShader = Shader.Find(StableShaderName);
            if (_stableShader == null)
            {
                foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b == null) continue;
                    var s = b.LoadAsset<Shader>(StableShaderAssetPath);
                    if (s != null) { _stableShader = s; break; }
                }
            }
        }
        if (_stableShader != null && !_stableFoundLogged)
        {
            _stableFoundLogged = true;
            VRLog.Info(Scope, $"stable hex decal shader '{StableShaderName}' loaded — hex highlight " +
                              "materials will be swapped off the depth-reconstructing OmniDecal_Shd.");
        }
        else if (_stableShader == null && !_stableMissLogged)
        {
            _stableMissLogged = true;
            VRLog.Warn(Scope, $"stable hex decal shader '{StableShaderName}' NOT found (older bundle?) — " +
                              "falling back to zeroing the swimming OmniDecal layers.");
        }
        return _stableShader;
    }

    /// <summary>Materials this class swapped; restored to the original shader on Reset().</summary>
    private static readonly List<Material> Swapped = new();

    // -------- diagnostics --------

    /// <summary>How many distinct materials get a full property dump before going quiet.</summary>
    private const int MaxMaterialDumps = 2;
    private static int _materialDumps;
    private static int _errorLogs;

    internal static void Reset()
    {
        // Best-effort restore (hot-reload hygiene). Destroyed materials compare == null.
        if (_originalShader != null)
        {
            foreach (Material mat in Swapped)
            {
                try
                {
                    if (mat != null && mat.shader != null && mat.shader.name == StableShaderName)
                        mat.shader = _originalShader;
                }
                catch { /* restoring is cosmetic; never throw during shutdown */ }
            }
        }
        Swapped.Clear();
        _materialDumps = 0;
        _errorLogs = 0;
        _lastLoggedZTest = -1;
    }

    private static void DumpMaterial(Material mat)
    {
        Shader shader = mat.shader;
        var sb = new System.Text.StringBuilder(512);
        sb.Append($"hex highlight material dump #{_materialDumps}: shader='{shader.name}' " +
                  $"renderQueue={mat.renderQueue} props:");
        int count = shader.GetPropertyCount();
        for (int i = 0; i < count; i++)
        {
            string name = shader.GetPropertyName(i);
            switch (shader.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    sb.Append($" {name}={mat.GetColor(name)}");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    sb.Append($" {name}={mat.GetVector(name)}");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    sb.Append($" {name}={mat.GetFloat(name):0.###}");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    Texture? tex = mat.GetTexture(name);
                    sb.Append($" {name}=tex:{(tex != null ? $"{tex.name}({tex.width}x{tex.height})" : "null")}");
                    break;
            }
        }
        VRLog.Info(Scope, sb.ToString());
    }

    // -------- the patch --------

    /// <summary>
    /// Postfix on the ONE method that writes every hex-highlight material property
    /// (verified decompiled GH.Runtime, HexSelect_Control.cs:696-772). Runs after each
    /// game write — including right after every material re-creation in
    /// ActivateHexObject — so the swap/neutralization always wins without polling.
    /// </summary>
    [HarmonyPatch(typeof(HexSelect_Control), "ProjectorMaterialAdjustment")]
    internal static class HexSelect_ProjectorMaterialAdjustment_Patch
    {
        private static readonly AccessTools.FieldRef<HexSelect_Control, Material?> MaterialRef =
            AccessTools.FieldRefAccess<HexSelect_Control, Material?>("m_Material");

        private static readonly int BorderFlameIntensity = Shader.PropertyToID("_BorderFlameIntensity");
        private static readonly int BorderLineIntensity = Shader.PropertyToID("_BorderLineIntensity");
        private static readonly int HexIntensity = Shader.PropertyToID("_HexIntensity");
        private static readonly int CrossHair = Shader.PropertyToID("_CrossHair");
        private static readonly int VRZTest = Shader.PropertyToID("_VRZTest");
        private static readonly int VRDepthBias = Shader.PropertyToID("_VRDepthBias");

        private static void Postfix(HexSelect_Control __instance)
        {
            try
            {
                Material? mat = MaterialRef(__instance);
                if (mat == null)
                    return;

                if (_materialDumps < MaxMaterialDumps && LogMaterialDump?.Value == true)
                {
                    _materialDumps++;
                    DumpMaterial(mat);
                }

                if (SwapStableShader?.Value == true && TrySwapStable(mat))
                {
                    // Stable shader active: the layers no longer swim, so the kill
                    // knobs are bypassed and the full vanilla look returns.
                    if (!_knobsBypassLogged)
                    {
                        _knobsBypassLogged = true;
                        VRLog.Info(Scope, "stable shader swap active — layer-kill fallback knobs bypassed.");
                    }
                    return;
                }

                // Fallback: previous least-invasive mitigation (swap off or shader
                // missing from an old bundle) — zero the swimming layers.
                if (KillBorderFlame?.Value == true)
                    mat.SetFloat(BorderFlameIntensity, 0f);
                if (KillCrosshair?.Value == true)
                    mat.SetFloat(CrossHair, 0f);
                if (KillBorderLine?.Value == true)
                    mat.SetFloat(BorderLineIntensity, 0f);
                if (KillFill?.Value == true)
                    mat.SetFloat(HexIntensity, 0f);
            }
            catch (System.Exception e)
            {
                if (_errorLogs < 3)
                {
                    _errorLogs++;
                    VRLog.Error(Scope, $"postfix failed: {e}");
                }
            }
        }

        /// <summary>
        /// Swap <paramref name="mat"/> onto the bundled stable shader. True when the
        /// material now runs (or already ran) the stable shader; false → caller uses
        /// the zeroing fallback. Unity keeps all matching property values (textures
        /// included) across the shader assignment; the pre-swap renderQueue (4000,
        /// from the game's _exampleMaterial) is re-asserted afterwards.
        /// </summary>
        private static bool TrySwapStable(Material mat)
        {
            Shader? current = mat.shader;
            if (current != null && current.name == StableShaderName)
            {
                // Already swapped (postfix re-runs on every state change) — still
                // re-assert the occlusion knobs so live config edits take effect.
                ApplyOcclusionKnobs(mat);
                return true;
            }
            // Only swap the shader we ported. Anything else (game update, other
            // variant) is left alone so the fallback knobs still govern it.
            if (current == null || current.name != OriginalShaderName)
                return false;

            Shader? stable = StableShader();
            if (stable == null)
                return false;

            _originalShader ??= current;
            int queue = mat.renderQueue; // 4000 in the shipped game
            mat.shader = stable;
            mat.renderQueue = queue > 0 ? queue : 4000;
            ApplyOcclusionKnobs(mat);
            Swapped.Add(mat);
            // Keep the restore list tidy across long sessions: drop destroyed entries.
            if (Swapped.Count > 512)
                Swapped.RemoveAll(m => m == null);
            return true;
        }

        /// <summary>
        /// (Re-)apply the occlusion knobs to a stable-shader material: ZTest
        /// (default 4 = LEqual — with the shader's per-pixel SV_Depth export the
        /// highlight is occluded by figures on the hex and by walls; 8 = vanilla
        /// draw-through) and the anti-z-fight depth bias. Logged when the applied
        /// ZTest changes.
        /// </summary>
        private static void ApplyOcclusionKnobs(Material mat)
        {
            int zTest = StableZTest?.Value ?? 4;
            mat.SetFloat(VRZTest, zTest);
            float bias = StableDepthBias?.Value ?? 0.0002f;
            mat.SetFloat(VRDepthBias, bias);
            if (_lastLoggedZTest != zTest)
            {
                _lastLoggedZTest = zTest;
                string meaning = zTest switch
                {
                    4 => "LEqual — highlight occluded by figures/walls via per-pixel depth export",
                    8 => "Always — vanilla draw-through",
                    _ => "custom CompareFunction",
                };
                VRLog.Info(Scope, $"stable hex decal ZTest={zTest} ({meaning}), depthBias={bias:0.######}.");
            }
        }
    }
}
