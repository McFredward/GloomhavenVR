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
/// previous mitigation stays — zero the swimming DECORATION layers
/// (<c>_BorderFlameIntensity</c>, <c>_CrossHair</c>). The two bisect knobs that could also
/// zero <c>_BorderLineIntensity</c>/<c>_HexIntensity</c> are GONE (user ruling 2026-08-13):
/// the ring and the fill are the "which hex" readout and must never be switchable off.
/// When the swap IS active the kill knobs are bypassed: the layers no longer swim, so the
/// full vanilla look comes back.
///
/// SHUTDOWN: swapped materials are tracked and restored to the original shader in
/// <see cref="Reset"/> (best effort — the game recreates materials from
/// <c>_exampleMaterial</c> on the next refresh anyway).
///
/// SAFETY: postfix body fully try/caught (WorldUI lesson: an unguarded NRE in a
/// per-frame game path starves input); work happens only when the game itself just
/// rewrote the material (state changes), not per-frame.
///
/// ---------------------------------------------------------------------------
/// ENVIRONMENT-ROOM BLEED — ModBuild 132 hardware finding 5
/// ---------------------------------------------------------------------------
/// User, verbatim: "5) Das Hex feld mit dem aktuellen ausgewählt character ist auch
/// UNTER dem Spielbrett auf dem Boden drauf zu sehen bzw. die Umrahmung des hex
/// felds." — the selection hex, or rather its outline, is also visible BELOW the
/// board, on the floor of the mod's new 3D environment room
/// <see cref="Core.SkyAlternative"/>, which is spawned at the player's floor point
/// and therefore spans the space under the diorama.
///
/// WHAT THE HEX HIGHLIGHT IS — from decompiled source, NOT inferred:
/// <c>HexSelect_Control.HexProjector</c> is declared
/// <c>public MeshRenderer HexProjector { get; set; }</c>
/// (decompiled GH.Runtime/HexSelect_Control.cs:243-245) — the highlight itself is a
/// MESH decal box, not a <c>UnityEngine.Projector</c>, so
/// <c>Projector.ignoreLayers</c> has no purchase on it. Its geometry is exactly what
/// <c>ProjectorModifier.ReplaceProjector</c> produces
/// (decompiled GH.Runtime/ProjectorModifier.cs:32-53): builtin Cube mesh, local scale
/// <c>new Vector3(aspectRatio * 2f, farClipPlane - nearClipPlane, aspectRatio * 2f)</c> —
/// the measured (2, 0.3, 2) of the shader header, i.e. aspectRatio 1 and a 0.3-deep
/// frustum. That converter runs in <c>Awake</c> and ONLY when
/// <c>PlatformLayer.Setting.UseDecalOptimization</c> is on AND the source projector has
/// <c>ignoreLayers == 0</c>; it then disables the <c>Projector</c> and adds the mesh.
/// So a build with the optimization OFF keeps LIVE <c>Projector</c> components on the
/// very same objects, and a live orthographic projector paints its material onto every
/// renderer inside its frustum on any layer not in <c>ignoreLayers</c> — the mod's room
/// floor a metre below the board included. Both shapes of the game are therefore
/// covered here:
///
///  * MESH shape (what this rig logged in ModBuild 132: the material dump shows the
///    swap landing on <c>HexProjector.material</c>): the decal draws the pattern at the
///    per-pixel view-ray ∩ tile-plane point and depth-tests with the plane's own
///    exported depth, so wherever its box footprint covers a pixel that shows something
///    FARTHER than the tile plane — the void past a room's edge, the gap between two
///    rooms — the pattern still passes ZTest and is drawn. That was always true; until
///    ModBuild 132 the surface behind was the black sky sphere, so nobody could see it.
///    Nothing in this file can clip that: the receiving-surface test lives in the
///    fragment shader (unity/…/HexDecalStable.shader, another lane's file). Documented
///    for the next round rather than half-fixed here.
///  * PROJECTOR shape: <see cref="GuardProjector"/> ORs the mod layer's bit into
///    <c>ignoreLayers</c>, which is exactly the mechanism the bleed needs and costs
///    nothing when the component is already disabled. STRICTLY ADDITIVE — the game's own
///    bits are never cleared — and reversed in <see cref="Reset"/> by clearing ONLY the
///    bit we set.
///
/// This is a CLASS of bug, not one hex: every live <c>Projector</c> in the game paints
/// the room the same way. The decompiled inventory of components that own one:
/// <c>ProjectorModifier</c>, <c>ObjectPosToMaterial</c>, <c>DeathDissolve</c>
/// (<c>dissolveProjectors</c>), and the RFX4 spell-FX family
/// (<c>RFX4_ColorHelper</c>, <c>RFX4_EffectSettingVisible</c>, <c>RFX4_UVAnimation</c>,
/// <c>RFX4_UVScroll</c>, <c>RFX4_ScaleCurves</c>, <c>RFX4_ShaderFloatCurve</c>,
/// <c>RFX4_ShaderColorGradient</c>). The guard is component-type-driven, so it covers all
/// of them without naming any.
///
/// WHERE IT IS HOOKED, and why that covers re-creation:
///  1. one sweep at install — <see cref="InstallProjectorGuard"/>, the ONE
///     <c>HEX PROJECTOR</c> log line;
///  2. a postfix on <c>ProjectorModifier.Awake</c> — the game's own decal-projector
///     creation site, so every instantiated/pooled decal is guarded at birth. It runs
///     AFTER the vanilla body, so the <c>ignoreLayers == 0</c> precondition of
///     <c>ReplaceProjector</c> still sees the authored value and the game's decal
///     optimization is never disabled by us;
///  3. the hex postfix guards its OWN tree whenever the swap lands on a FRESH material —
///     that is exactly <c>RefreshHexUI</c>'s <c>m_Material = new Material(_exampleMaterial)</c>
///     (HexSelect_Control.cs:335-338), i.e. the moment a pooled star is (re)built mid-scenario,
///     which is the one creation the install sweep cannot have seen. With the swap disabled
///     this signal is absent and hooks 2 and 4 carry the star instead;
///  4. scene loads re-arm the sweep, which then runs on the next hex material write.
/// NO periodic full-scene sweep: the perf lane deleted those on purpose. Every trigger
/// above is an event, and each projector is touched once (an instance-keyed record).
/// </summary>
internal static class HexHighlightFix
{
    private const string Scope = "HexHighlightFix";

    // The bundle ASSET PATH of the stable decal shader used to be a const here. It lives in
    // Core.BundleShaders now, with every other bundled shader's, so no call site can get the folder
    // or the casing wrong and a stale path fails a build gate instead of a hardware round.
    private const string StableShaderName = "GloomhavenVR/HexDecalStable";
    private const string OriginalShaderName = "OmniDecal_Shd";

    // -------- config (own file: dev.gloomhavenvr.hexhighlight.cfg) --------

    internal static ConfigEntry<bool>? SwapStableShader;
    internal static ConfigEntry<int>? StableZTest;
    internal static ConfigEntry<float>? StableDepthBias;
    internal static ConfigEntry<bool>? KillBorderFlame;
    internal static ConfigEntry<bool>? KillCrosshair;
    // [HexHighlight] KillBorderLine and KillFill are GONE (user ruling 2026-08-13). Both were
    // bisect knobs whose ON state ERASES the targeting readout itself — the hex outline and the
    // soft fill that say WHICH field you are about to act on ("this removes most of the
    // highlight", its own description). A diagnostic that can blank the selection marker is not
    // an optional content setting. The two knobs that survive (KillBorderFlame, KillCrosshair)
    // only remove the DECORATIVE swimming layers and are the shipped fallback mitigation.
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
        // KillBorderLine / KillFill: not bound any more (user ruling 2026-08-13) — the border
        // ring and the white fill are the selection readout and are never zeroed now.
        LogMaterialDump = config.Bind(
            "HexHighlight", "LogMaterialDump", Defaults.LogMaterialDump,
            "Log the hex highlight material's shader name and full property dump for the " +
            "first few materials seen (evidence for tuning the fix).");
    }

    // -------- stable shader lookup (Core.BundleShaders) --------

    private static Shader? _stableShader;
    private static Shader? _originalShader;   // kept for best-effort restore on Reset()
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
        // find-then-probe-every-loaded-bundle now lives in Core.BundleShaders, which also owns the
        // asset path. See that class for why the inlined version had to be centralised.
        _stableShader ??= BundleShaders.Resolve(
            StableShaderName, Scope,
            "hex highlight materials will be swapped off the depth-reconstructing OmniDecal_Shd.",
            "Falling back to zeroing the swimming OmniDecal layers.");
        return _stableShader;
    }

    /// <summary>Materials this class swapped; restored to the original shader on Reset().</summary>
    private static readonly List<Material> Swapped = new();

    // -------- environment-room bleed: game Projectors must ignore the mod layer --------
    // (class doc, ENVIRONMENT-ROOM BLEED)

    /// <summary>
    /// Every <c>Projector</c> we ORed the mod-layer bit into, with the mask it had when we
    /// first saw it. Instance-keyed, so a projector is touched exactly once and a sweep that
    /// runs again is free. Destroyed entries fake-null and are skipped/dropped.
    /// </summary>
    private static readonly Dictionary<Projector, int> GuardedProjectors = new();

    /// <summary>Set by install and by every scene load; consumed by the next hex material write.</summary>
    private static bool _sweepPending;

    /// <summary>Scene-load hook registered once, so <see cref="Reset"/> can take it off again.</summary>
    private static bool _sceneHookArmed;

    /// <summary>
    /// Log budget for the guard. The install line is unconditional (proof the fix ran); the
    /// scenario sweeps that follow are what actually find projectors, so a few of those are
    /// allowed through and then it goes quiet for the session.
    /// </summary>
    private const int MaxProjectorLogLines = 4;
    private static int _projectorLogLines;

    /// <summary>
    /// One sweep of every <c>Projector</c> currently in the scene, called once from
    /// <c>BoardModule.Init</c>. Logs the single <c>HEX PROJECTOR</c> line (count + names) the
    /// next hardware log needs to prove the guard ran, even when the count is zero — install
    /// happens in the menu, where the scenario's decals do not exist yet, so a zero here is
    /// the expected reading and the scenario sweep is the interesting one.
    /// </summary>
    internal static void InstallProjectorGuard()
    {
        _sweepPending = true;
        SweepProjectors("install", forceLog: true);

        if (!_sceneHookArmed)
        {
            _sceneHookArmed = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                      UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Arm only. The sweep itself runs on the next hex material write, i.e. when a board
        // with decals actually exists — never on a timer (the perf lane deleted those).
        _sweepPending = true;
    }

    /// <summary>
    /// Guard every projector in the scene, INCLUDING inactive ones (pooled FX decals are
    /// parked disabled and re-enabled without a fresh Awake). Idempotent: already-guarded
    /// instances cost a dictionary probe. Only ever called from an event — install, a scene
    /// load's first hex write, or a fresh hex material.
    /// </summary>
    private static void SweepProjectors(string why, bool forceLog = false)
    {
        _sweepPending = false;
        if (!VRSession.IsRunning)
            return;   // no mod-layer visuals exist without the VR head camera

        int adjusted = 0;
        var names = new System.Text.StringBuilder(128);
        try
        {
            Projector[] all = Object.FindObjectsOfType<Projector>(includeInactive: true);
            foreach (Projector p in all)
            {
                // NEVER pre-empt ProjectorModifier.Awake: it converts a projector into the
                // cheap mesh decal ONLY while ignoreLayers == 0 (ProjectorModifier.cs:32-36),
                // so writing our bit before that object has awakened would silently cost the
                // game its decal optimization. An object that is not active in the hierarchy
                // may still be waiting for its first Awake — leave every such ProjectorModifier
                // to the creation-site postfix that owns it and that runs after the vanilla body.
                if (!p.gameObject.activeInHierarchy && p.GetComponent<ProjectorModifier>() != null)
                    continue;
                if (!GuardProjector(p))
                    continue;
                adjusted++;
                if (adjusted <= 12)
                    names.Append(names.Length > 0 ? ", " : "").Append(p.name);
                else if (adjusted == 13)
                    names.Append(", …");
            }
        }
        catch (System.Exception e)
        {
            if (_errorLogs < 3)
            {
                _errorLogs++;
                VRLog.Error(Scope, $"HEX PROJECTOR sweep failed: {e}");
            }
            return;
        }

        if (!forceLog && (adjusted == 0 || _projectorLogLines >= MaxProjectorLogLines))
            return;
        _projectorLogLines++;
        int layer = VRLayers.ModLayer;
        VRLog.Info(Scope, $"HEX PROJECTOR guard [{why}]: {adjusted} game projector(s) now ignore the mod " +
                          $"layer {layer} — mask bit 0x{VRLayers.ModLayerMask:X8} ORed into ignoreLayers, so " +
                          "no game projector paints the mod's environment room. Names: " +
                          (adjusted > 0 ? names.ToString() : "none in this scope"));
    }

    /// <summary>
    /// ADD the mod layer to one projector's <c>ignoreLayers</c>. Never clears a bit the game
    /// set; records the authored mask once so <see cref="Reset"/> can take only OUR bit back
    /// out. True when this call changed something.
    /// </summary>
    private static bool GuardProjector(Projector? p)
    {
        if (p == null || !VRSession.IsRunning)
            return false;
        int bit = VRLayers.ModLayerMask;
        if (GuardedProjectors.ContainsKey(p))
            return false;
        int authored = p.ignoreLayers;
        // Keep the record from growing forever across scene loads: destroyed projectors stay
        // as (fake-null) keys, so drop them when the book gets long. Same hygiene as Swapped.
        if (GuardedProjectors.Count > 512)
            PruneGuardedProjectors();
        GuardedProjectors[p] = authored;
        if ((authored & bit) != 0)
            return false;              // the game already excludes it — nothing to do or undo
        p.ignoreLayers = authored | bit;
        return true;
    }

    /// <summary>Drop destroyed (fake-null) keys from the guard record.</summary>
    private static void PruneGuardedProjectors()
    {
        var dead = new List<Projector>();
        foreach (var kv in GuardedProjectors)
        {
            // Unity fake-null: the component is destroyed but the key REFERENCE is alive and
            // is still the handle Remove needs — hence the suppression, not a null add.
            if (kv.Key == null)
                dead.Add(kv.Key!);
        }
        foreach (Projector p in dead)
            GuardedProjectors.Remove(p);
    }

    /// <summary>
    /// Guard the projectors on ONE object tree — the per-instance path, used at the game's own
    /// creation sites (<c>ProjectorModifier.Awake</c>) and when a hex star is (re)built. No
    /// scene scan; the tree is a handful of nodes.
    /// </summary>
    private static void GuardTree(GameObject? go)
    {
        if (go == null || !VRSession.IsRunning)
            return;
        Projector[] found = go.GetComponentsInChildren<Projector>(includeInactive: true);
        for (int i = 0; i < found.Length; i++)
            GuardProjector(found[i]);
    }

    /// <summary>
    /// Postfix on the game's decal-projector creation site. Runs AFTER the vanilla body, so
    /// <c>ReplaceProjector</c>'s <c>ignoreLayers == 0</c> precondition still reads the authored
    /// value and the game's own decal optimization is never disabled by this guard. Covers
    /// every instantiated / pooled decal at birth, which is the re-creation path a one-shot
    /// sweep would miss.
    /// </summary>
    [HarmonyPatch(typeof(ProjectorModifier), "Awake")]
    internal static class ProjectorModifier_Awake_Patch
    {
        private static void Postfix(ProjectorModifier __instance)
        {
            try
            {
                GuardTree(__instance.gameObject);
            }
            catch (System.Exception e)
            {
                if (_errorLogs < 3)
                {
                    _errorLogs++;
                    VRLog.Error(Scope, $"HEX PROJECTOR creation-site guard failed: {e}");
                }
            }
        }
    }

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

        // Restore-on-uninstall for the projector guard: clear ONLY the bit we set, and only
        // where the authored mask did not already have it — a game write that happened after
        // us keeps every bit of its own.
        if (GuardedProjectors.Count > 0)
        {
            int bit = VRLayers.ModLayerMask;
            foreach (var kv in GuardedProjectors)
            {
                try
                {
                    Projector p = kv.Key;
                    if (p != null && (kv.Value & bit) == 0)
                        p.ignoreLayers &= ~bit;
                }
                catch { /* restoring is cosmetic; never throw during shutdown */ }
            }
            GuardedProjectors.Clear();
        }
        if (_sceneHookArmed)
        {
            _sceneHookArmed = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        _sweepPending = false;
        _projectorLogLines = 0;

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

                // Environment-room bleed: a sweep armed by install/scene load is spent HERE —
                // the first hex material write after a load is the earliest moment a board
                // with decals provably exists, and it costs nothing on every later write.
                if (_sweepPending)
                    SweepProjectors("first hex highlight of the scene");

                if (_materialDumps < MaxMaterialDumps && LogMaterialDump?.Value == true)
                {
                    _materialDumps++;
                    DumpMaterial(mat);
                }

                if (SwapStableShader?.Value == true && TrySwapStable(mat, __instance))
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
                // The border ring (_BorderLineIntensity) and the fill (_HexIntensity) are
                // never touched: they ARE the "which hex" readout (user ruling 2026-08-13).
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
        private static bool TrySwapStable(Material mat, HexSelect_Control owner)
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
            // A material is created exactly once per RefreshHexUI
            // (HexSelect_Control.cs:335-338), i.e. this branch IS the "a pooled hex star was
            // just (re)built" signal — guard whatever projectors that star owns, right here,
            // without a scene scan.
            GuardTree(owner != null ? owner.gameObject : null);
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
