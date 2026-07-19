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
/// TIMING (the previous round's failure): the scenario map — including every ProceduralWall — is
/// built PROCEDURALLY AFTER <c>SceneManager.sceneLoaded</c>, so the old scene-load-only scan
/// always found ZERO wall renderers (hardware log: "ProceduralWall components=0"). The wall depth
/// enforcement therefore now runs from <see cref="GlowOcclusion"/>'s persistent sweep driver via
/// <see cref="SweepWallDepth"/> (~2s/5s/10s/20s/40s after scene load, then every ~15s), and is
/// INCREMENTAL: wall renderers already processed with an unchanged material set are skipped
/// (instance-ID → material-set signature, cleared on scene load), so repeat sweeps are idempotent.
///
/// PART 1 (legacy): neutralize the game's ToggleWallFade drivers:
///   a. <c>ToggleWallTransparencyGlobal</c> — set <c>WallTransparencyEnabled = false</c>, disable.
///   b. <c>ToggleWallFadeScript</c> — force each child renderer's <c>_ToggleWallFadeLocal = 0</c>.
///   c. <c>ActivateWallFadeInGame</c> — disable so it can't re-set the global.
/// Runs on scene load (as before, with its summary line) AND quietly on every sweep — the
/// components may well spawn WITH the procedural map; a sweep that finds live drivers logs one
/// Info line per scene, otherwise nothing.
///
/// PART 2 (the actual fix — config-gated <c>SolidWallDepth</c>, default ON): WALL DEPTH
/// ENFORCEMENT. Hypothesis (to be confirmed by <see cref="GlowOcclusion"/>'s census now that it
/// sweeps after the map is built): the fade-capable wall materials sit in the Transparent queue
/// (&gt;2500) with ZWrite Off even when the fade is off, so walls never own the depth buffer — and
/// EVERYTHING transparent/high-queue behind them (fire glow ~3000-3900, world-space health-bar
/// canvases ~3000, hex select/border effects, floor decals, insect VFX) draws AFTER the wall and
/// paints over it.
///
/// Wall renderers are identified by STRUCTURAL signals, never name guessing alone:
///   (a) any Renderer under a <c>ProceduralWall</c> component (reflection-only via
///       AccessTools.TypeByName — decompiled/GH.Runtime/ProceduralWall.cs);
///   (b) any Renderer under a <c>ProceduralDoorway</c> component;
///   (c) fallback: renderer / material / shader name containing "wall" (case-insensitive) on
///       non-mod-layer, non-UI world geometry.
/// For each NEW wall renderer's materials we LOG shader/queue/ZWrite (full inventory on first
/// discovery, then only new walls, bounded per scene), and IF (renderQueue &gt; 2450 || _ZWrite
/// exists and == 0) we force PER-INSTANCE (<c>r.materials</c>, never shared assets):
/// <c>_ZWrite = 1</c> (when the property exists) and <c>renderQueue = 2000</c> (Geometry).
/// Original shared material sets are snapshotted across ALL sweeps and restored on
/// <see cref="Uninstall"/>. If ZERO wall renderers exist in a RICH world (map demonstrably built),
/// that is logged once per scene as evidence. Mod-layer renderers and the sky
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

    /// <summary>Per-scene cap on wall-inventory material lines (full first inventory + new walls).</summary>
    private const int SceneInventoryLogMax = 40;

    /// <summary>All mod-created GameObjects share this name prefix (rig, hands, sky, drivers).</summary>
    private const string ModNamePrefix = "GloomhavenVR";

    private static readonly int _globalProp = Shader.PropertyToID(GlobalIntName);
    private static readonly int _localProp = Shader.PropertyToID(LocalIntName);
    private static readonly int _zWriteProp = Shader.PropertyToID("_ZWrite");

    private static ConfigFile? _configFile;
    private static ConfigEntry<bool>? _solidWallDepth;

    private static bool _hooked;
    private static bool _firstSweepFailureLogged;
    private static readonly HashSet<string> _loggedMissing = [];

    // ---- per-scene sweep state (cleared in OnSceneLoaded) ------------------------------------
    // Incremental enforcement: wall renderer instanceID → signature of its material set when we
    // last inspected it. Unchanged ⇒ skip (idempotent across sweeps).
    private static readonly Dictionary<int, int> _processedSig = [];
    /// <summary>Wall renderers whose material inventory was already logged this scene.</summary>
    private static readonly HashSet<int> _inventoryLogged = [];
    private static int _sceneInventoryLines;
    private static bool _zeroWallsReported;      // rich-world-but-no-walls evidence, once per scene
    private static bool _wallSummaryPrinted;     // first wall-depth summary line, once per scene
    private static bool _fadeDriversReported;    // live fade drivers found by a sweep, once per scene

    // Reversibility: for every wall renderer we solidified (across ALL sweeps), the ORIGINAL
    // shared materials so Uninstall (hot-reload / shutdown) can put them back verbatim
    // (mirrors GlowOcclusion).
    private static readonly List<(Renderer renderer, Material[] origShared)> _modified = [];

    /// <summary>
    /// Hook scene loads and run one immediate legacy scan. The wall depth enforcement itself runs
    /// on <see cref="GlowOcclusion"/>'s sweep schedule via <see cref="SweepWallDepth"/>.
    /// Idempotent; no-op if VR isn't running.
    /// </summary>
    public static void Install()
    {
        if (_hooked || !VRSession.IsRunning)
            return;
        EnsureConfig();
        SceneManager.sceneLoaded += OnSceneLoaded;
        _hooked = true;
        ScanLegacy();
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
        ClearSceneState();
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
        if (!VRSession.IsRunning)
            return;
        // New scene ⇒ all renderer instance IDs and printing state are stale.
        ClearSceneState();
        ScanLegacy();
    }

    private static void ClearSceneState()
    {
        _processedSig.Clear();
        _inventoryLogged.Clear();
        _sceneInventoryLines = 0;
        _zeroWallsReported = false;
        _wallSummaryPrinted = false;
        _fadeDriversReported = false;
    }

    /// <summary>Scene-load legacy pass: neutralize fade drivers and log the summary line.</summary>
    private static void ScanLegacy()
    {
        if (!VRSession.IsRunning)
            return;

        try
        {
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
        }
        catch (Exception e)
        {
            VRLog.Warn(Name, $"scan threw (walls may not occlude): {e.Message}");
        }
    }

    // ------------------------------------------------------------------ TASK 2: wall depth (sweep)

    /// <summary>
    /// One wall-depth sweep, called by <see cref="GlowOcclusion"/>'s driver between the census and
    /// the ZTest enforcement. Finds wall renderers by the three structural signals, quietly
    /// re-neutralizes any fade drivers that spawned with the map, logs NEW walls' material state
    /// (bounded per scene) and — gated on <c>SolidWallDepth</c> — forces depth-writing
    /// Geometry-queue rendering on suspicious ones. Incremental: unchanged, already-inspected
    /// renderers are skipped. <paramref name="richWorld"/> = the census saw a populated world this
    /// sweep (used for the once-per-scene zero-walls evidence line).
    /// Returns (wallRenderers, solidifiedThisSweep) for the heartbeat. Never throws.
    /// </summary>
    internal static (int walls, int solidified) SweepWallDepth(bool richWorld)
    {
        if (!_hooked || !VRSession.IsRunning)
            return (0, 0);

        try
        {
            // Legacy fade drivers may spawn WITH the procedural map — re-neutralize quietly;
            // report once per scene only if a sweep actually finds live drivers.
            SweepLegacyQuiet();

            bool fixOn = _solidWallDepth?.Value ?? true;

            var walls = new HashSet<Renderer>();
            int fromWall = CollectFromComponentType(WallType, walls);
            int fromDoorway = CollectFromComponentType(DoorwayType, walls);
            int fromNames = CollectByName(walls);

            if (walls.Count == 0)
            {
                if (richWorld && !_zeroWallsReported)
                {
                    _zeroWallsReported = true;
                    VRLog.Info(Name,
                        "wall depth: world is populated but ZERO wall renderers found by ALL three "
                        + $"signals (ProceduralWall components={fromWall}, ProceduralDoorway "
                        + $"components={fromDoorway}, name-matched renderers=0) — cannot test the "
                        + "wall-ZWrite hypothesis in this scene; check the GlowOcclusion census for "
                        + "what the walls actually are.");
                }
                return (0, 0);
            }

            int solidified = 0;
            int newWalls = 0;
            foreach (Renderer r in walls)
            {
                if (r == null)
                    continue;
                // Hard guard: never the mod layer (sky depth-reset lives there) or mod-owned objects.
                if (r.gameObject.layer == VRLayers.ModLayer || IsModOwned(r.transform))
                    continue;

                Material[] shared;
                try { shared = r.sharedMaterials; }
                catch { continue; }

                int id = r.GetInstanceID();
                int sig = MaterialSignature(shared);
                if (_processedSig.TryGetValue(id, out int prevSig) && prevSig == sig)
                    continue; // already inspected, material set unchanged — idempotent skip
                newWalls++;

                if (SolidifyRenderer(r, fixOn))
                    solidified++;
                // Record the POST-fix signature (SolidifyRenderer may swap in instance materials)
                // so the next sweep skips this wall unless the game swaps its materials again.
                try { _processedSig[id] = MaterialSignature(r.sharedMaterials); }
                catch { _processedSig[id] = sig; }
            }

            // Summary: once per scene when walls first appear, then only when a sweep changed something.
            if (solidified > 0 || (newWalls > 0 && !_wallSummaryPrinted))
            {
                _wallSummaryPrinted = true;
                VRLog.Info(Name,
                    $"wall depth: {walls.Count} wall renderer(s) "
                    + $"(ProceduralWall comps={fromWall}, Doorway comps={fromDoorway}, name-matched={fromNames}); "
                    + $"fix {(fixOn ? "ON" : "OFF")}, {newWalls} new this sweep, solidified {solidified} "
                    + $"({_modified.Count} tracked for restore).");
            }
            return (walls.Count, solidified);
        }
        catch (Exception e)
        {
            if (!_firstSweepFailureLogged)
            {
                _firstSweepFailureLogged = true;
                VRLog.Warn(Name, $"wall sweep threw (walls may not occlude): {e.Message}");
            }
            return (0, 0);
        }
    }

    /// <summary>
    /// Sweep-time legacy pass: re-neutralize fade drivers without the per-scan summary line;
    /// one Info line per scene IF live drivers are actually found post-load.
    /// </summary>
    private static void SweepLegacyQuiet()
    {
        int transpCount = NeutralizeTransparencyGlobal();
        (int fadeCount, string? diag) = NeutralizeFadeScripts();
        int activateCount = NeutralizeActivate();

        if (!_fadeDriversReported && (transpCount > 0 || fadeCount > 0 || activateCount > 0))
        {
            _fadeDriversReported = true;
            VRLog.Info(Name,
                $"fade-driver sweep: live fade drivers found POST-load — "
                + $"ToggleWallTransparencyGlobal={transpCount}, ToggleWallFadeScript={fadeCount}, "
                + $"ActivateWallFadeInGame={activateCount} (neutralized; re-checked every sweep).");
            if (diag != null)
                VRLog.Info(Name, diag);
        }
    }

    /// <summary>Order-insensitive-enough cheap signature of a renderer's shared material set.</summary>
    private static int MaterialSignature(Material[] shared)
    {
        int h = 17;
        foreach (Material m in shared)
            h = unchecked(h * 31 + (m != null ? m.GetInstanceID() : 0));
        return h;
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
    /// Log this wall renderer's material state (first time we see it this scene, bounded per
    /// scene) and — when gated on and suspicious (queue &gt; 2450 or ZWrite==0) — force
    /// per-instance _ZWrite=1 + renderQueue=2000. Returns true if anything was changed.
    /// </summary>
    private static bool SolidifyRenderer(Renderer r, bool fixOn)
    {
        Material[] shared;
        try { shared = r.sharedMaterials; }
        catch { return false; }

        // Inventory: full on first discovery, then only walls not yet logged this scene.
        bool logInventory = _sceneInventoryLines < SceneInventoryLogMax
            && _inventoryLogged.Add(r.GetInstanceID());

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

            if (logInventory && _sceneInventoryLines < SceneInventoryLogMax)
            {
                _sceneInventoryLines++;
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
