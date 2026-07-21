using BepInEx.Configuration;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// Kills the head-coupled "reflection" swimming inside the hex-selection highlight
/// (user issue #6, second attempt — the depth-reset-quad fix in build 149c8dfa7 did
/// NOT remove it, which exonerates the mod's depth reset as the sole cause).
///
/// GROUND TRUTH (DXBC disassembly of the game's shader, 2026-07-21; evidence in
/// tools/ShaderDisasm — extraction via ShaderDisasm + DXDecompiler, same pipeline as
/// tools/ShaderDisasm/FINDINGS.md):
///
/// The highlight mesh <c>HexSelect_Control.HexProjector</c> uses shader
/// <b><c>OmniDecal_Shd</c></b> (resources.assets pathId 556, single pass 'Unlit',
/// serialized ZTest Always→LEqual by ShaderOcclusionPatcher, an Amplify/ASE shader).
/// Its fragment program is a <b>screen-space depth-reconstruction projector</b>:
/// <code>
///   uv    = screenPos.xy / screenPos.w                    // per-pixel SCREEN uv
///   d     = tex2D(_CameraDepthTexture, uv).r              // scene depth at that pixel
///   ndc   = float3(uv, 1-d) * 2 - 1
///   view  = (unity_CameraInvProjection * ndc)  (w-divide, z flip)
///   world = unity_CameraToWorld * view
///   obj   = unity_WorldToObject * world                   // hex pattern drawn in obj space
/// </code>
/// EVERY visible component — soft fill (t1 <c>_HexMask</c>.a), crisp border band
/// (mask^20 ring), animated border flames (t2 <c>_MainTex</c> "Projector Texture",
/// 8x8 flipbook over <c>_Time</c>), pulsing target frame / crosshair (t3
/// <c>_HexTargetFrame</c> x <c>_SinTime</c>) — is sampled at that RECONSTRUCTED
/// object-space position. (<c>_CameraNormalsTexture</c> is also referenced but nothing
/// in the entire game ever binds it, so that term is a constant — not the swimmer.)
///
/// WHY IT SWIMS IN VR: reconstruction is only stable when screen-uv, depth texture,
/// <c>unity_CameraInvProjection</c> and <c>unity_CameraToWorld</c> all describe the SAME
/// viewpoint. In the mod's multipass XR rendering the raster position and
/// <c>_CameraDepthTexture</c> are per-eye, but <c>unity_CameraInvProjection</c> /
/// <c>unity_CameraToWorld</c> come from the camera's MONO state (UnityPerCameraRare is
/// not stereo-aware; the Quest's per-eye frusta are asymmetric and laterally offset).
/// The reconstructed pattern therefore lands slightly differently for every eye and
/// every head pose — the projected texture layers slide across the floor like a
/// reflection ("bewegt sich heftig mit dem Kopf"). No ZTest/depth-reset change can fix
/// that; it is baked into the shader's projection math.
///
/// LEAST-INVASIVE RUNTIME MITIGATION (this class): the shader exposes independent
/// intensity properties for each projected layer, and <c>HexSelect_Control</c> rewrites
/// them on every state change in exactly one method, <c>ProjectorMaterialAdjustment()</c>
/// (HexSelect_Control.cs:696, sets _HexColour/_HexIntensity/_BorderLineIntensity/
/// _BorderFlameIntensity/_TargetFrameIntensity/_CrossHair + the six edge toggles).
/// A postfix there gets the last word after EVERY game write (RefreshHexUI → HexUpdate
/// → ProjectorMaterialAdjustment, incl. material re-creation) and zeroes the layers
/// that read as the swimming "reflection", while keeping the white selection look
/// (fill + border) intact by default:
///
///  - <c>_BorderFlameIntensity</c> → 0   (default ON)  — the animated flame flipbook,
///    the sharpest/brightest moving layer and prime suspect for "reflection".
///  - <c>_CrossHair</c> → 0              (default ON)  — the _SinTime-pulsing target
///    frame graphic drawn INSIDE the hex during target selection.
///  - <c>_BorderLineIntensity</c> → 0    (default OFF) — crisp border ring; config
///    bisect knob if the artifact persists.
///  - <c>_HexIntensity</c> → 0           (default OFF) — soft fill; bisect knob only.
///
/// NAME→TERM CAVEAT (from the disassembly): the compiled blob strips names, so the
/// pairing of {_BorderFlameIntensity, _BorderLineIntensity, _HexIntensity} to
/// {flame-texture term (cb0[10].y), crisp-band term (cb0[9].w), fill term (cb0[10].x)}
/// is inferred semantically. If Amplify packed them in property order instead, "flame"
/// and "line" swap — either way the config knobs cover all combinations, and killing
/// both border terms is one config flip away.
///
/// PROPER FIX (follow-up, out of scope here): a drop-in replacement shader in the mod's
/// asset bundle that reproduces OmniDecal_Shd's algebra (fully recovered in the
/// disassembly) but uses the INTERPOLATED MESH object-space position instead of the
/// depth reconstruction — identical look on the flat board floor, inherently
/// stereo-stable. This class is the correct host for the runtime material swap once
/// the bundle ships that shader.
///
/// SAFETY: postfix body is fully try/caught (WorldUI lesson: an unguarded NRE in a
/// per-frame game path starves input); it only runs when the game itself just rewrote
/// the material (state changes), so cost is a few SetFloats, not per-frame work.
/// Reversible: unpatching restores vanilla behaviour on the next state change (the
/// game recreates the material from <c>_exampleMaterial</c> on every cache-changed
/// refresh, so no lasting mutation survives).
/// </summary>
internal static class HexHighlightFix
{
    private const string Scope = "HexHighlightFix";

    // -------- config (own file: dev.gloomhavenvr.hexhighlight.cfg) --------

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

        KillBorderFlame = config.Bind(
            "HexHighlight", "KillBorderFlame", true,
            "Zero _BorderFlameIntensity on hex highlight materials (OmniDecal_Shd). Kills the " +
            "animated border-flame layer of the hex selection highlight — the screen-space " +
            "depth-projected layer that swims with head movement in VR (the 'reflection'). " +
            "The white fill and border line stay.");
        KillCrosshair = config.Bind(
            "HexHighlight", "KillCrosshair", true,
            "Zero _CrossHair on hex highlight materials. Kills the pulsing target-frame/" +
            "crosshair graphic projected INSIDE the hex during target selection — same " +
            "swimming projection, drawn mid-hex.");
        KillBorderLine = config.Bind(
            "HexHighlight", "KillBorderLine", false,
            "Bisect knob: additionally zero _BorderLineIntensity (the crisp border ring). " +
            "Enable if the swimming artifact persists with the flame/crosshair killed. " +
            "Changes the look (hex loses its sharp outline).");
        KillFill = config.Bind(
            "HexHighlight", "KillFill", false,
            "Bisect knob: additionally zero _HexIntensity (the soft white fill). Only for " +
            "diagnosis — this removes most of the highlight.");
        LogMaterialDump = config.Bind(
            "HexHighlight", "LogMaterialDump", true,
            "Log the hex highlight material's shader name and full property dump for the " +
            "first few materials seen (evidence for tuning the fix).");
    }

    // -------- diagnostics --------

    /// <summary>How many distinct materials get a full property dump before going quiet.</summary>
    private const int MaxMaterialDumps = 2;
    private static int _materialDumps;
    private static int _errorLogs;

    internal static void Reset()
    {
        _materialDumps = 0;
        _errorLogs = 0;
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
    /// game write, so the neutralized values always win without per-frame polling.
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
    }
}
