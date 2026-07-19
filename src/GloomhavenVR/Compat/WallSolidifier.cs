using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #4 (companion to <see cref="WallFadeDisable"/> and <see cref="GlowOcclusion"/>) — makes
/// walls authoritatively opaque AND depth-writing.
///
/// PART 1 (legacy, demoted): neutralize the game's ToggleWallFade drivers. The hardware round's own
/// diagnostic proved these drivers are ABSENT in the tested scenes (all component counts 0, global
/// already 0), so this part currently fixes nothing — it is KEPT because it is harmless and would
/// matter in any scene that does ship the fade components:
///   a. <c>ToggleWallTransparencyGlobal</c> — set <c>WallTransparencyEnabled = false</c>, disable.
///   b. <c>ToggleWallFadeScript</c> — force each child renderer's <c>_ToggleWallFadeLocal = 0</c>.
///   c. <c>ActivateWallFadeInGame</c> — disable so it can't re-set the global.
///
/// PART 2 (NEW, the actual fix candidate — config-gated <c>SolidWallDepth</c>, default ON):
/// WALL DEPTH ENFORCEMENT. Central hypothesis (UNVERIFIED until the next hardware log — the wall
/// shaders live in unreadable asset bundles, so <see cref="GlowOcclusion"/>'s census must confirm):
/// the fade-capable wall materials sit in the Transparent queue (&gt;2500) with ZWrite Off even when
/// the fade is off, so walls never own the depth buffer — and EVERYTHING transparent/high-queue
/// behind them (fire glow ~3000-3900, world-space health-bar canvases ~3000, hex select/border
/// effects, floor decals, insect VFX) draws AFTER the wall and paints over it. This also explains
/// why OPAQUE elements (figures, queue ~2000, ZWrite On) do NOT show through.
///
/// Wall renderers are identified by STRUCTURAL signals, never name guessing alone:
///   (a) any Renderer under a <c>ProceduralWall</c> component (reflection-only via
///       AccessTools.TypeByName — decompiled/GH.Runtime/ProceduralWall.cs);
///   (b) any Renderer under a <c>ProceduralDoorway</c> component;
///   (c) fallback: renderer / material / shader name containing "wall" (case-insensitive) on
///       non-mod-layer, non-UI world geometry.
/// For each wall renderer's materials we LOG shader/queue/ZWrite (bounded), and IF
/// (renderQueue &gt; 2450 || _ZWrite exists and == 0) we force PER-INSTANCE (<c>r.materials</c>,
/// never shared assets): <c>_ZWrite = 1</c> (when the property exists) and
/// <c>renderQueue = 2000</c> (Geometry). Original shared material sets are snapshotted and restored
/// on <see cref="Uninstall"/>. If ZERO wall renderers are found by all three signals, that is logged
/// clearly as evidence for the next round. Mod-layer renderers and the sky
/// (<see cref="Core.SkyBackdrop"/>'s depth-reset, mod layer, queue 1999) are NEVER touched.
///
/// Strict no-op when VR isn't running. Every game-type lookup is reflection-guarded: a missing type
/// is logged once and skipped, never thrown. Never throws into the game.
/// </summary>
internal static class WallSolidifier
{
    private const string Name = "WallSolidifier";
    private const string GlobalIntName = "ToggleWallFade";
    private const string LocalIntName = "_ToggleWallFadeLocal";

    private const string TransparencyGlobalType = "ToggleWallTransparencyGlobal";
    private const string FadeScriptType = "ToggleWallFadeScript";
    private const string ActivateType = "ActivateWallFadeInGame";

    private const string WallType = "ProceduralWall";
    private const string DoorwayType = "ProceduralDoorway";

    /// <summary>Suspicious-queue threshold: anything above AlphaTest(2450) can't be trusted to depth-write.</summary>
    private const int QueueThreshold = 2450;
    private const int GeometryQueue = 2000;
    private const int InventoryLogMax = 20;

    /// <summary>All mod-created GameObjects share this name prefix (rig, hands, sky, drivers).</summary>
    private const string ModNamePrefix = "GloomhavenVR";

    private static readonly int _globalProp = Shader.PropertyToID(GlobalIntName);
    private static readonly int _localProp = Shader.PropertyToID(LocalIntName);
    private static readonly int _zWriteProp = Shader.PropertyToID("_ZWrite");

    private static ConfigFile? _configFile;
    private static ConfigEntry<bool>? _solidWallDepth;

    private static bool _hooked;
    private static int _inventoryLogs;
    private static readonly HashSet<string> _loggedMissing = [];

    // Reversibility: for every wall renderer we solidified, the ORIGINAL shared materials so
    // Uninstall (hot-reload / shutdown) can put them back verbatim (mirrors GlowOcclusion).
    private static readonly List<(Renderer renderer, Material[] origShared)> _modified = [];

    /// <summary>Hook scene loads and run one immediate scan. Idempotent; no-op if VR isn't running.</summary>
    public static void Install()
    {
        if (_hooked || !VRSession.IsRunning)
            return;
        EnsureConfig();
        SceneManager.sceneLoaded += OnSceneLoaded;
        _hooked = true;
        Scan();
    }

    /// <summary>Unhook scene loads and restore every solidified wall renderer (hot-reload safety).</summary>
    public static void Uninstall()
    {
        if (_hooked)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _hooked = false;
        }

        int restored = 0;
        foreach ((Renderer renderer, Material[] origShared) in _modified)
        {
            if (renderer != null && origShared != null)
            {
                try { renderer.sharedMaterials = origShared; restored++; }
                catch { /* renderer destroyed under us — nothing to restore */ }
            }
        }
        if (restored > 0)
            VRLog.Info(Name, $"restored {restored} wall renderer material set(s) on shutdown.");
        _modified.Clear();
    }

    private static void EnsureConfig()
    {
        if (_solidWallDepth != null)
            return;
        // Standalone module config file (dev.gloomhavenvr.walls.cfg) — no edit to Plugin.cs needed.
        _configFile = ModuleConfig.Create("walls");
        _solidWallDepth = _configFile.Bind("Compat", "SolidWallDepth", true,
            "When true, wall renderers (found under ProceduralWall/ProceduralDoorway components, or "
            + "by 'wall' in renderer/material/shader names) whose materials sit in the transparent "
            + "queue (>2450) or have ZWrite off are forced to write depth (_ZWrite=1) at Geometry "
            + "queue (2000), so fire glow, health bars, hex highlights and other transparent VFX "
            + "behind a wall can no longer paint over it. The wall inventory diagnostic runs "
            + "regardless of this toggle. Disable if walls render incorrectly.");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (VRSession.IsRunning)
            Scan();
    }

    private static void Scan()
    {
        if (!VRSession.IsRunning)
            return;

        try
        {
            // PART 1 (legacy, demoted — proven absent in the tested scenes, kept because harmless).
            int global = SafeGetGlobal();
            int transpCount = NeutralizeTransparencyGlobal();
            (int fadeCount, string? diag) = NeutralizeFadeScripts();
            int activateCount = NeutralizeActivate();

            VRLog.Info(Name,
                $"fade-driver scan (legacy): {GlobalIntName} global={global}; "
                + $"ToggleWallTransparencyGlobal={transpCount}, ToggleWallFadeScript={fadeCount}, "
                + $"ActivateWallFadeInGame={activateCount}.");
            if (diag != null)
                VRLog.Info(Name, diag);

            // PART 2 (new) — wall depth enforcement.
            EnforceWallDepth();
        }
        catch (Exception e)
        {
            VRLog.Warn(Name, $"scan threw (walls may not occlude): {e.Message}");
        }
    }

    // ------------------------------------------------------------------ TASK 2: wall depth

    /// <summary>
    /// Find wall renderers by the three structural signals, log their material state, and (gated)
    /// force depth-writing Geometry-queue rendering on the suspicious ones.
    /// </summary>
    private static void EnforceWallDepth()
    {
        bool fixOn = _solidWallDepth?.Value ?? true;

        var walls = new HashSet<Renderer>();
        int fromWall = CollectFromComponentType(WallType, walls);
        int fromDoorway = CollectFromComponentType(DoorwayType, walls);
        int fromNames = CollectByName(walls);

        if (walls.Count == 0)
        {
            VRLog.Info(Name,
                "wall depth: ZERO wall renderers found by ALL three signals "
                + $"(ProceduralWall components={fromWall}, ProceduralDoorway components={fromDoorway}, "
                + "name-matched renderers=0) — cannot test the wall-ZWrite hypothesis in this scene; "
                + "check the GlowOcclusion census for what the walls actually are.");
            return;
        }

        int solidified = 0;
        foreach (Renderer r in walls)
        {
            if (r == null)
                continue;
            // Hard guard: never the mod layer (sky depth-reset lives there) or mod-owned objects.
            if (r.gameObject.layer == VRLayers.ModLayer || IsModOwned(r.transform))
                continue;

            if (SolidifyRenderer(r, fixOn))
                solidified++;
        }

        VRLog.Info(Name,
            $"wall depth: {walls.Count} wall renderer(s) "
            + $"(ProceduralWall comps={fromWall}, Doorway comps={fromDoorway}, name-matched={fromNames}); "
            + $"fix {(fixOn ? "ON" : "OFF")}, solidified {solidified} this scan "
            + $"({_modified.Count} tracked for restore).");
    }

    /// <summary>FindObjectsOfType for a reflection-resolved component type; adds child renderers. Returns component count.</summary>
    private static int CollectFromComponentType(string typeName, HashSet<Renderer> sink)
    {
        Type? type = ResolveType(typeName);
        if (type == null)
        {
            LogMissingOnce(typeName);
            return 0;
        }

        int count = 0;
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type, includeInactive: false))
        {
            count++;
            if (obj is not Component comp)
                continue;
            try
            {
                foreach (Renderer r in comp.GetComponentsInChildren<Renderer>(includeInactive: false))
                {
                    if (r != null)
                        sink.Add(r);
                }
            }
            catch { /* component torn down mid-scan — skip */ }
        }
        return count;
    }

    /// <summary>Fallback signal (c): 'wall' in renderer / material / shader name on world geometry. Returns newly added count.</summary>
    private static int CollectByName(HashSet<Renderer> sink)
    {
        int added = 0;
        foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            if (r == null || sink.Contains(r))
                continue;
            if (r.gameObject.layer == VRLayers.ModLayer || IsModOwned(r.transform))
                continue;

            bool matched = ContainsWall(r.name);
            if (!matched)
            {
                Material[] shared;
                try { shared = r.sharedMaterials; }
                catch { continue; }
                foreach (Material m in shared)
                {
                    if (m == null)
                        continue;
                    if (ContainsWall(m.name) || (m.shader != null && ContainsWall(m.shader.name)))
                    {
                        matched = true;
                        break;
                    }
                }
            }
            if (!matched)
                continue;

            // Not UI: skip anything under a Canvas (e.g. a menu image that happens to say "wall").
            try
            {
                if (r.GetComponentInParent<Canvas>() != null)
                    continue;
            }
            catch { continue; }

            if (sink.Add(r))
                added++;
        }
        return added;
    }

    private static bool ContainsWall(string? s) =>
        !string.IsNullOrEmpty(s) && s!.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// Log this wall renderer's material state (bounded) and — when gated on and suspicious
    /// (queue &gt; 2450 or ZWrite==0) — force per-instance _ZWrite=1 + renderQueue=2000.
    /// Returns true if anything was changed.
    /// </summary>
    private static bool SolidifyRenderer(Renderer r, bool fixOn)
    {
        Material[] shared;
        try { shared = r.sharedMaterials; }
        catch { return false; }

        // Decide on the SHARED materials first so we never instantiate needlessly.
        bool anySuspicious = false;
        foreach (Material m in shared)
        {
            if (m == null)
                continue;

            int queue = m.renderQueue;
            int zwrite = SafeGetInt(m, _zWriteProp, out bool hasZWrite);
            bool suspicious = queue > QueueThreshold || (hasZWrite && zwrite == 0);
            anySuspicious |= suspicious;

            if (_inventoryLogs < InventoryLogMax)
            {
                _inventoryLogs++;
                string shader = m.shader != null ? m.shader.name : "<null-shader>";
                VRLog.Info(Name,
                    $"wall material: '{r.name}' shader='{shader}' queue={queue} "
                    + $"zwrite={(hasZWrite ? zwrite.ToString() : "-")}{(suspicious ? " SUSPECT" : string.Empty)}.");
            }
        }

        if (!anySuspicious || !fixOn)
            return false;

        Material[] origShared = shared;      // snapshot ORIGINAL shared assets for restore
        Material[] instances = r.materials;  // per-renderer INSTANCES (no global mutation)
        bool changed = false;

        foreach (Material m in instances)
        {
            if (m == null)
                continue;

            int queueBefore = m.renderQueue;
            int zwriteBefore = SafeGetInt(m, _zWriteProp, out bool hasZWrite);
            if (!(queueBefore > QueueThreshold || (hasZWrite && zwriteBefore == 0)))
                continue;

            if (hasZWrite && zwriteBefore != 1)
                m.SetInt(_zWriteProp, 1);
            m.renderQueue = GeometryQueue;

            string shader = m.shader != null ? m.shader.name : "<null-shader>";
            VRLog.Info(Name,
                $"wall solidified: '{r.name}' shader='{shader}' "
                + $"queue {queueBefore}->{GeometryQueue} "
                + $"zwrite {(hasZWrite ? zwriteBefore.ToString() : "-")}->{(hasZWrite ? "1" : "-")}.");
            changed = true;
        }

        if (changed)
            _modified.Add((r, origShared));
        else
            r.sharedMaterials = origShared; // nothing changed → drop the instances we just made
        return changed;
    }

    private static int SafeGetInt(Material m, int prop, out bool has)
    {
        has = false;
        try
        {
            if (!m.HasProperty(prop))
                return -1;
            has = true;
            return m.GetInt(prop);
        }
        catch
        {
            has = false;
            return -1;
        }
    }

    /// <summary>True if any transform up the chain is a mod object ("GloomhavenVR*" name).</summary>
    private static bool IsModOwned(Transform? t)
    {
        for (int depth = 0; t != null && depth < 64; t = t.parent, depth++)
        {
            if (t.name.StartsWith(ModNamePrefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ legacy fade neutralizers

    /// <summary>Set WallTransparencyEnabled=false and disable each instance so it stops re-asserting the global.</summary>
    private static int NeutralizeTransparencyGlobal()
    {
        Type? type = ResolveType(TransparencyGlobalType);
        if (type == null)
        {
            LogMissingOnce(TransparencyGlobalType);
            return 0;
        }

        FieldInfo? field = AccessTools.Field(type, "WallTransparencyEnabled");
        int count = 0;
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type, includeInactive: true))
        {
            try { field?.SetValue(obj, false); }
            catch (Exception e) { VRLog.Warn(Name, $"WallTransparencyEnabled set failed: {e.Message}"); }

            if (obj is Behaviour b)
                b.enabled = false;
            count++;
        }
        return count;
    }

    /// <summary>Force each fade-script's child renderers to _ToggleWallFadeLocal=0. Returns count + a one-off diagnostic for the first wall renderer.</summary>
    private static (int count, string? diag) NeutralizeFadeScripts()
    {
        Type? type = ResolveType(FadeScriptType);
        if (type == null)
        {
            LogMissingOnce(FadeScriptType);
            return (0, null);
        }

        int count = 0;
        string? diag = null;
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type, includeInactive: true))
        {
            count++;
            if (obj is not Component comp)
                continue;

            Renderer[] renderers;
            try { renderers = comp.GetComponentsInChildren<Renderer>(includeInactive: true); }
            catch { continue; }

            foreach (Renderer? rend in renderers)
            {
                Material? mat = rend != null ? rend.material : null;
                if (mat == null)
                    continue;

                // Emit the before/after diagnostic for the very first wall renderer we touch.
                if (diag == null)
                {
                    int before = SafeGetMatInt(mat);
                    mat.SetInt(_localProp, 0);
                    int after = SafeGetMatInt(mat);
                    string shader = mat.shader != null ? mat.shader.name : "<null-shader>";
                    diag = $"first wall renderer '{rend!.name}' shader='{shader}' "
                        + $"{LocalIntName}: before={before} after={after}.";
                }
                else
                {
                    mat.SetInt(_localProp, 0);
                }
            }
        }
        return (count, diag);
    }

    /// <summary>Disable each ActivateWallFadeInGame so it doesn't re-set the global on its manual toggles.</summary>
    private static int NeutralizeActivate()
    {
        Type? type = ResolveType(ActivateType);
        if (type == null)
        {
            LogMissingOnce(ActivateType);
            return 0;
        }

        int count = 0;
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type, includeInactive: true))
        {
            if (obj is Behaviour b)
                b.enabled = false;
            count++;
        }
        return count;
    }

    private static int SafeGetGlobal()
    {
        try { return Shader.GetGlobalInt(_globalProp); }
        catch { return -1; }
    }

    private static int SafeGetMatInt(Material mat)
    {
        try { return mat.GetInt(_localProp); }
        catch { return -1; }
    }

    private static void LogMissingOnce(string typeName)
    {
        if (_loggedMissing.Add(typeName))
            VRLog.Info(Name, $"type not found (skipped): {typeName}.");
    }

    /// <summary>Resolve a type by (assembly-qualified or plain) name across loaded assemblies.</summary>
    private static Type? ResolveType(string name)
    {
        Type? type = AccessTools.TypeByName(name);
        if (type != null)
            return type;

        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(asm => asm.GetType(name, throwOnError: false))
            .FirstOrDefault(t => t != null);
    }
}
