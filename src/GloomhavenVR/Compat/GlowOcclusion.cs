using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #4 follow-up (companion to <see cref="WallSolidifier"/>) — occlusion enforcement: makes
/// world renderers respect perspective so nothing (fire glow, VFX, decals, …) bleeds through walls.
/// The ONLY intended see-through is the sky depth-reset (<see cref="Core.SkyBackdrop"/>, mod layer,
/// queue 1999) — never touched here because everything on the mod layer is excluded.
///
/// TIMING (the previous round's failure): Gloomhaven builds the scenario map (walls, tiles, props,
/// VFX, actors) PROCEDURALLY AFTER <c>SceneManager.sceneLoaded</c>, and more content (enemies,
/// effects, insect VFX) spawns continuously during play — a scene-load-only scan sees an empty
/// world (hardware log: worldRenderers=0..4 even in the scenario scene). Fix: a tiny persistent
/// driver (<c>GloomhavenVR.OcclusionSweep</c>, DontDestroyOnLoad, owned here) re-runs the scans on
/// a SWEEP SCHEDULE — after every scene load at ~2s, 5s, 10s, 20s, 40s, then a steady sweep every
/// ~15s while VR runs. Each sweep = census-check → wall depth enforcement
/// (<see cref="WallSolidifier.SweepWallDepth"/>) → ZTest enforcement, in that order, so the census
/// still records the pristine state of anything new before a fix mutates it. No per-frame work
/// (coroutine + WaitForSecondsRealtime); sweeps are INCREMENTAL — renderers already processed with
/// an unchanged material set are skipped (per-renderer instance-ID → material-set signature,
/// cleared on scene load).
///
/// TWO INDEPENDENT PARTS, installed separately so the per-sweep order is
/// census → <see cref="WallSolidifier"/> wall depth enforcement → ZTest enforcement.
/// The sceneLoaded hooks remain as the schedule (re)starter and per-scene state reset.
///
///  1. OCCLUSION CENSUS (<see cref="InstallCensus"/> — ALWAYS on, the evidence engine): one cheap
///     pass per sweep over all active world <see cref="Renderer"/>s (Mesh/Skinned/ParticleSystem/
///     Line/… — CanvasRenderer-driven UI graphics are not Renderers so UI canvases are excluded by
///     construction), skipping the mod layer and mod-owned objects. Renderers are grouped per
///     unique SHADER NAME; one log line per group reports count, renderQueue min-max, _ZWrite /
///     _ZTest(_ZTestMode) values when present, and one example parent/renderer path. SUSPICIOUS
///     groups sort first (ZTest==Always(8), ZWrite==0, or queue&gt;=2500). PRINTING POLICY (no log
///     spam): the FULL census (up to 40 group lines + total) prints the FIRST time a sweep in a
///     scene sees a RICH world (&gt;50 world renderers); afterwards only NEW suspect shader groups
///     (vs what was already printed this scene) are printed, with an updated total. Every sweep
///     logs a one-line heartbeat at Debug level.
///  2. ZTEST ENFORCEMENT (<see cref="InstallEnforcement"/>, config-gated <c>OpaqueWorldGlow</c>,
///     default ON): ANY non-excluded world renderer material with <c>_ZTest</c>/<c>_ZTestMode</c>
///     == <see cref="CompareFunction.Always"/> (8) is forced to
///     <see cref="CompareFunction.LessEqual"/> (4) on PER-INSTANCE materials (<c>r.materials</c>,
///     never shared assets) — no name matching; depth-ignoring is the offence itself.
///     EXCLUDED: mod layer (sky/laser/hands), Canvas/UI graphics, anything with an EPOOutline
///     <c>Outlinable</c>/<c>OutlineWrapper</c> in its parent chain (actor outlines are an intended
///     gameplay affordance), and anything under the mod's head/hands rig (mod objects are all named
///     "GloomhavenVR.*").
///
/// Strict no-op when VR isn't running. Every game-type lookup is reflection-guarded (missing type
/// logged once, never thrown). Reversible on <see cref="Uninstall"/> (hot-reload) — the original
/// shared materials recorded across ALL sweeps are restored and the driver is destroyed. Never
/// throws into the game (first failure logged, then silent). Change logging is bounded: first ~30
/// changes verbose, then counted only.
/// </summary>
internal static class GlowOcclusion
{
    private const string Name = "GlowOcclusion";
    private const string DriverName = "GloomhavenVR.OcclusionSweep";
    private const int CensusMaxGroups = 40;
    private const int VerboseFixLogMax = 30;
    private const int SuspiciousQueue = 2500;

    /// <summary>A scene counts as RICH (map actually built) above this many world renderers.</summary>
    private const int RichWorldThreshold = 50;

    private static readonly int ZTestProp = Shader.PropertyToID("_ZTest");
    private static readonly int ZTestModeProp = Shader.PropertyToID("_ZTestMode");
    private static readonly int ZWriteProp = Shader.PropertyToID("_ZWrite");
    private static readonly int Always = (int)CompareFunction.Always;      // 8
    private static readonly int LEqual = (int)CompareFunction.LessEqual;   // 4

    /// <summary>All mod-created GameObjects share this name prefix (rig, hands, sky, drivers).</summary>
    private const string ModNamePrefix = "GloomhavenVR";

    private static ConfigFile? _configFile;
    private static ConfigEntry<bool>? _opaqueWorldGlow;

    private static bool _censusHooked;
    private static bool _fixHooked;
    private static bool _typesResolved;
    private static Type? _outlinableType;
    private static Type? _outlineWrapperType;
    private static bool _firstFailureLogged;
    private static int _verboseFixLogs;
    private static readonly HashSet<string> _loggedMissing = [];

    // ---- sweep driver + per-scene sweep state ------------------------------------------------

    /// <summary>
    /// Optional post-sweep hook, invoked (guarded) at the end of every <see cref="Sweep"/> so
    /// other occlusion work — <see cref="OcclusionProbe"/>'s target discovery — can ride the same
    /// schedule instead of running a second census. Subscribers must not throw; a failure is
    /// caught and logged once.
    /// </summary>
    internal static event Action? PostSweep;

    private static SweepDriver? _driver;
    private static int _sweepIndex;                                   // per scene, for the heartbeat
    private static bool _richCensusPrinted;                           // per scene
    private static readonly HashSet<string> _printedSuspectShaders = []; // per scene

    // Incremental enforcement: renderer instanceID → signature of its material set at the time we
    // last inspected it. Unchanged ⇒ skip (idempotent). Cleared on scene load.
    private static readonly Dictionary<int, int> _processedSig = [];

    // Reversibility: for every renderer we depth-corrected (across ALL sweeps), the ORIGINAL
    // shared materials so Uninstall (hot-reload / shutdown) can put them back verbatim.
    private static readonly List<(Renderer renderer, Material[] origShared)> _modified = [];

    // ------------------------------------------------------------------ install / uninstall

    /// <summary>
    /// Hook the always-on occlusion census (evidence engine) and start the persistent sweep driver.
    /// MUST be installed BEFORE <see cref="WallSolidifier.Install"/> so the census part of each
    /// sweep runs first and logs the pristine shipped state. No-op if VR isn't running.
    /// </summary>
    public static void InstallCensus()
    {
        if (_censusHooked || !VRSession.IsRunning)
            return;
        SceneManager.sceneLoaded += OnSceneLoadedCensus;
        _censusHooked = true;
        EnsureDriver();
    }

    /// <summary>
    /// Hook the config-gated ZTest enforcement into the sweep schedule.
    /// MUST be installed AFTER <see cref="WallSolidifier.Install"/> (census → walls → ZTest).
    /// No-op if VR isn't running.
    /// </summary>
    public static void InstallEnforcement()
    {
        if (_fixHooked || !VRSession.IsRunning)
            return;
        EnsureConfig();
        SceneManager.sceneLoaded += OnSceneLoadedFix;
        _fixHooked = true;
        EnsureDriver();
    }

    /// <summary>
    /// Unhook both handlers, destroy the sweep driver, and restore every depth-corrected renderer
    /// recorded across all sweeps (hot-reload safety).
    /// </summary>
    public static void Uninstall()
    {
        if (_censusHooked)
        {
            SceneManager.sceneLoaded -= OnSceneLoadedCensus;
            _censusHooked = false;
        }
        if (_fixHooked)
        {
            SceneManager.sceneLoaded -= OnSceneLoadedFix;
            _fixHooked = false;
        }

        if (_driver != null)
        {
            try { UnityEngine.Object.Destroy(_driver.gameObject); }
            catch { /* scene teardown already got it */ }
            _driver = null;
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
            VRLog.Info(Name, $"restored {restored} renderer material set(s) on shutdown.");
        _modified.Clear();
        _processedSig.Clear();
        _printedSuspectShaders.Clear();
        _richCensusPrinted = false;
        _sweepIndex = 0;
    }

    private static void EnsureConfig()
    {
        if (_opaqueWorldGlow != null)
            return;
        // Standalone module config file (dev.gloomhavenvr.glow.cfg) — no edit to Plugin.cs needed.
        _configFile = ModuleConfig.Create("glow");
        _opaqueWorldGlow = _configFile.Bind("Compat", "OpaqueWorldGlow", true,
            "Occlusion enforcement: when true, ANY world renderer material that ignores depth "
            + "(ZTest Always) is forced to respect it (ZTest LEqual), so fire glow, VFX, decals "
            + "etc. can no longer be seen through solid geometry. Excluded: the mod's own layer "
            + "(sky/laser/hands), UI canvases, and the game's actor outline effect. The occlusion "
            + "census diagnostic runs regardless of this toggle. Disable if it harms an intended look.");
    }

    // ------------------------------------------------------------------ sweep driver

    /// <summary>
    /// Create the persistent DontDestroyOnLoad sweep driver (idempotent). The GO name starts with
    /// the mod prefix so every scan automatically excludes it.
    /// </summary>
    private static void EnsureDriver()
    {
        if (_driver != null)
            return;
        var go = new GameObject(DriverName);
        UnityEngine.Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<SweepDriver>();
        _driver.RestartSchedule();
    }

    private static void OnSceneLoadedCensus(Scene scene, LoadSceneMode mode)
    {
        if (!VRSession.IsRunning)
            return;
        // Per-scene census state reset + schedule restart (2s/5s/10s/20s/40s, then every ~15s).
        _sweepIndex = 0;
        _richCensusPrinted = false;
        _printedSuspectShaders.Clear();
        if (_driver != null)
            _driver.RestartSchedule();
        else
            EnsureDriver();
    }

    private static void OnSceneLoadedFix(Scene scene, LoadSceneMode mode)
    {
        // New scene ⇒ all renderer instance IDs are stale; re-inspect everything the sweeps see.
        _processedSig.Clear();
    }

    /// <summary>
    /// One full sweep: census-check → wall depth enforcement → ZTest enforcement, in that order.
    /// Called by the driver on the schedule. Never throws into the game.
    /// </summary>
    internal static void Sweep()
    {
        if (!VRSession.IsRunning)
            return;

        try
        {
            _sweepIndex++;
            (int world, int groupCount, int suspects) = RunCensusSweep();
            bool richWorld = world > RichWorldThreshold;
            (int wallCount, int solidified) = WallSolidifier.SweepWallDepth(richWorld);
            (int candidates, int corrected) = _fixHooked ? RunEnforcementSweep() : (0, 0);

            // Heartbeat: every sweep, one Debug line — traceable without Info-level noise.
            VRLog.Debug(Name,
                $"sweep #{_sweepIndex} scene='{SceneManager.GetActiveScene().name}' "
                + $"worldRenderers={world} shaderGroups={groupCount} suspects={suspects} "
                + $"walls={wallCount} wallsSolidified+={solidified} "
                + $"ztestNewCandidates={candidates} ztestFixed+={corrected}.");
        }
        catch (Exception e)
        {
            LogFirstFailure($"sweep threw: {e.Message}");
        }

        // Shared-cadence hook (OcclusionProbe target discovery) — isolated so a subscriber
        // failure can never poison the census/enforcement above or vice versa.
        try
        {
            PostSweep?.Invoke();
        }
        catch (Exception e)
        {
            LogFirstFailure($"post-sweep hook threw: {e.Message}");
        }
    }

    /// <summary>
    /// Persistent MonoBehaviour on <c>GloomhavenVR.OcclusionSweep</c>: after every schedule
    /// (re)start, sweeps at ~2s, 5s, 10s, 20s, 40s cumulative, then steadily every ~15s (the
    /// scenario spawns enemies/VFX/insects continuously). Realtime waits so a paused game
    /// (timeScale 0) can't stall the schedule. No per-frame work.
    /// </summary>
    private sealed class SweepDriver : MonoBehaviour
    {
        // Deltas between sweeps ⇒ cumulative 2s, 5s, 10s, 20s, 40s after (re)start.
        private static readonly float[] InitialDelays = [2f, 3f, 5f, 10f, 20f];
        private const float SteadyIntervalSeconds = 15f;

        private Coroutine? _schedule;

        internal void RestartSchedule()
        {
            if (_schedule != null)
                StopCoroutine(_schedule);
            _schedule = StartCoroutine(RunSchedule());
        }

        private IEnumerator RunSchedule()
        {
            foreach (float delay in InitialDelays)
            {
                yield return new WaitForSecondsRealtime(delay);
                Sweep();
            }
            while (true)
            {
                yield return new WaitForSecondsRealtime(SteadyIntervalSeconds);
                Sweep();
            }
        }
    }

    // ------------------------------------------------------------------ TASK 1: census

    /// <summary>Per-shader-name aggregation bucket for one census pass.</summary>
    private sealed class ShaderGroup
    {
        public int RendererCount;
        public int QueueMin = int.MaxValue;
        public int QueueMax = int.MinValue;
        public readonly SortedSet<int> ZWriteValues = [];
        public readonly SortedSet<int> ZTestValues = [];
        public bool HasZWrite;
        public bool HasZTest;
        public string ExamplePath = "?";
        public bool Suspicious =>
            ZTestValues.Contains(8) || ZWriteValues.Contains(0) || QueueMax >= SuspiciousQueue;
    }

    /// <summary>
    /// One pass over all active world renderers, grouped per shader name. PRINTING POLICY:
    /// full census (suspicious first, capped at <see cref="CensusMaxGroups"/> lines + total) the
    /// first time this scene's world is RICH; afterwards only new suspect groups + updated total.
    /// Returns (worldRenderers, shaderGroups, suspiciousGroups) for the heartbeat.
    /// </summary>
    private static (int world, int groups, int suspects) RunCensusSweep()
    {
        var groups = new Dictionary<string, ShaderGroup>(StringComparer.Ordinal);
        int totalRenderers = 0;
        int skipped = 0;

        Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
        foreach (Renderer r in all)
        {
            if (r == null)
                continue;
            // Exclusions: mod layer (sky/laser) and any mod-owned object (rig/hands particles).
            // CanvasRenderer-driven UI graphics never appear here — CanvasRenderer is not a
            // Renderer subclass, so FindObjectsOfType<Renderer> can't return them.
            if (r.gameObject.layer == VRLayers.ModLayer || IsModOwned(r.transform))
            {
                skipped++;
                continue;
            }

            Material[] shared;
            try { shared = r.sharedMaterials; }
            catch { continue; }

            totalRenderers++;
            string path = ExamplePath(r);

            foreach (Material m in shared)
            {
                if (m == null)
                    continue;
                string shaderName = m.shader != null ? m.shader.name : "<null-shader>";
                if (!groups.TryGetValue(shaderName, out ShaderGroup? g))
                {
                    g = new ShaderGroup { ExamplePath = path };
                    groups.Add(shaderName, g);
                }

                g.RendererCount++;
                int q = m.renderQueue;
                if (q < g.QueueMin) g.QueueMin = q;
                if (q > g.QueueMax) g.QueueMax = q;

                SampleIntProp(m, ZWriteProp, g.ZWriteValues, ref g.HasZWrite);
                bool hadZTest = g.HasZTest;
                SampleIntProp(m, ZTestProp, g.ZTestValues, ref hadZTest);
                SampleIntProp(m, ZTestModeProp, g.ZTestValues, ref hadZTest);
                g.HasZTest = hadZTest;
            }
        }

        // Suspicious groups first, then by renderer count descending.
        List<KeyValuePair<string, ShaderGroup>> ordered = groups
            .OrderByDescending(kv => kv.Value.Suspicious)
            .ThenByDescending(kv => kv.Value.RendererCount)
            .ToList();

        int suspiciousCount = ordered.Count(kv => kv.Value.Suspicious);
        string sceneName = SceneManager.GetActiveScene().name;

        if (!_richCensusPrinted)
        {
            // FULL census, once per scene, the first time the world is actually populated
            // (>RichWorldThreshold world renderers) — sparse pre-build sweeps print nothing.
            if (totalRenderers > RichWorldThreshold)
            {
                _richCensusPrinted = true;
                var sb = new StringBuilder(4096);
                int lines = 0;
                foreach (KeyValuePair<string, ShaderGroup> kv in ordered)
                {
                    if (lines >= CensusMaxGroups)
                        break;
                    lines++;
                    AppendCensusLine(sb, kv.Key, kv.Value);
                    if (kv.Value.Suspicious)
                        _printedSuspectShaders.Add(kv.Key);
                }
                sb.Append($"census total: scene='{sceneName}' worldRenderers={totalRenderers} "
                    + $"shaderGroups={groups.Count} suspicious={suspiciousCount} "
                    + $"logged={lines}/{groups.Count} modSkipped={skipped}.");
                VRLog.Info(Name, sb.ToString());
            }
        }
        else
        {
            // Delta census: only SUSPECT shader groups not yet printed in this scene (late spawns:
            // enemies, insect VFX, health bars, hex effects), plus an updated total.
            List<KeyValuePair<string, ShaderGroup>> fresh = ordered
                .Where(kv => kv.Value.Suspicious && !_printedSuspectShaders.Contains(kv.Key))
                .Take(CensusMaxGroups)
                .ToList();
            if (fresh.Count > 0)
            {
                var sb = new StringBuilder(1024);
                foreach (KeyValuePair<string, ShaderGroup> kv in fresh)
                {
                    AppendCensusLine(sb, kv.Key, kv.Value);
                    _printedSuspectShaders.Add(kv.Key);
                }
                sb.Append($"census delta total: scene='{sceneName}' newSuspects={fresh.Count} "
                    + $"worldRenderers={totalRenderers} shaderGroups={groups.Count} "
                    + $"suspicious={suspiciousCount} modSkipped={skipped}.");
                VRLog.Info(Name, sb.ToString());
            }
        }

        return (totalRenderers, groups.Count, suspiciousCount);
    }

    private static void AppendCensusLine(StringBuilder sb, string shaderName, ShaderGroup g)
    {
        string queueRange = g.QueueMin == g.QueueMax
            ? g.QueueMin.ToString()
            : $"{g.QueueMin}-{g.QueueMax}";
        sb.Append("census: shader='").Append(shaderName)
          .Append("' n=").Append(g.RendererCount)
          .Append(" queue=").Append(queueRange)
          .Append(" zwrite=").Append(FormatValues(g.HasZWrite, g.ZWriteValues))
          .Append(" ztest=").Append(FormatValues(g.HasZTest, g.ZTestValues))
          .Append(" ex='").Append(g.ExamplePath).Append('\'');
        if (g.Suspicious)
            sb.Append(" SUSPECT");
        sb.AppendLine();
    }

    private static void SampleIntProp(Material m, int prop, SortedSet<int> values, ref bool has)
    {
        try
        {
            if (!m.HasProperty(prop))
                return;
            has = true;
            if (values.Count < 8) // bounded; render-state ints are tiny enums anyway
                values.Add(m.GetInt(prop));
        }
        catch { /* non-int property with this name — treat as absent */ }
    }

    private static string FormatValues(bool has, SortedSet<int> values) =>
        !has || values.Count == 0 ? "-" : string.Join("/", values);

    /// <summary>'parent/renderer' example path for a census line.</summary>
    private static string ExamplePath(Renderer r)
    {
        Transform? parent = r.transform.parent;
        return parent != null ? $"{parent.name}/{r.name}" : r.name;
    }

    // ------------------------------------------------------------------ TASK 3: ZTest enforcement

    /// <summary>
    /// Force ZTest Always(8) → LEqual(4) on ALL non-excluded world renderers (no name matching).
    /// INCREMENTAL: renderers already inspected with an unchanged material set are skipped, so
    /// repeat sweeps are idempotent and cheap. Config-gated (<c>OpaqueWorldGlow</c>); reversible
    /// via <see cref="_modified"/>. Returns (newCandidates, corrected) for the heartbeat.
    /// </summary>
    private static (int candidates, int corrected) RunEnforcementSweep()
    {
        EnsureTypes();
        bool fixOn = _opaqueWorldGlow?.Value ?? true;

        Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
        int candidates = 0;
        int fixedRenderers = 0;

        foreach (Renderer r in all)
        {
            if (r == null)
                continue;

            Material[] shared;
            try { shared = r.sharedMaterials; }
            catch { continue; }

            int id = r.GetInstanceID();
            int sig = MaterialSignature(shared);
            if (_processedSig.TryGetValue(id, out int prevSig) && prevSig == sig)
                continue; // already inspected, material set unchanged — idempotent skip

            if (!IsDepthIgnoring(shared))
            {
                _processedSig[id] = sig;
                continue;
            }
            candidates++;

            if (!fixOn || IsExcluded(r))
            {
                _processedSig[id] = sig;
                continue;
            }

            if (DepthCorrect(r))
                fixedRenderers++;
            // Record the POST-correction signature (DepthCorrect swaps in instance materials) so
            // the next sweep skips this renderer unless the game swaps its materials again.
            try { _processedSig[id] = MaterialSignature(r.sharedMaterials); }
            catch { _processedSig[id] = sig; }
        }

        // Info only when something actually changed; the per-sweep heartbeat carries the counts.
        if (fixedRenderers > 0)
        {
            VRLog.Info(Name,
                $"ZTest enforcement: {candidates} new depth-ignoring world renderer(s) this sweep; "
                + $"depth-corrected {fixedRenderers} ({_modified.Count} tracked for restore).");
        }
        return (candidates, fixedRenderers);
    }

    /// <summary>Order-insensitive-enough cheap signature of a renderer's shared material set.</summary>
    private static int MaterialSignature(Material[] shared)
    {
        int h = 17;
        foreach (Material m in shared)
            h = unchecked(h * 31 + (m != null ? m.GetInstanceID() : 0));
        return h;
    }

    /// <summary>True if any of the given SHARED materials has _ZTest/_ZTestMode == Always(8).</summary>
    private static bool IsDepthIgnoring(Material[] shared)
    {
        foreach (Material m in shared)
        {
            if (m == null)
                continue;
            if (IsAlways(m, ZTestProp) || IsAlways(m, ZTestModeProp))
                return true;
        }
        return false;
    }

    private static bool IsAlways(Material m, int prop)
    {
        try { return m.HasProperty(prop) && m.GetInt(prop) == Always; }
        catch { return false; }
    }

    /// <summary>Force this renderer's instance materials' Always ZTest → LEqual; returns true if changed.</summary>
    private static bool DepthCorrect(Renderer r)
    {
        Material[] origShared = r.sharedMaterials;   // snapshot ORIGINAL shared assets for restore
        Material[] instances = r.materials;          // per-renderer INSTANCES (no global mutation)
        bool changed = false;

        foreach (Material m in instances)
        {
            if (m == null)
                continue;

            if (m.HasProperty(ZTestProp) && m.GetInt(ZTestProp) == Always)
            {
                m.SetInt(ZTestProp, LEqual);
                LogFixBounded(r, m, "_ZTest");
                changed = true;
            }
            if (m.HasProperty(ZTestModeProp) && m.GetInt(ZTestModeProp) == Always)
            {
                m.SetInt(ZTestModeProp, LEqual);
                LogFixBounded(r, m, "_ZTestMode");
                changed = true;
            }
        }

        if (changed)
            _modified.Add((r, origShared));
        else
            r.sharedMaterials = origShared; // nothing to change → drop the instances we just made
        return changed;
    }

    /// <summary>First ~30 changes are logged verbosely; after that only the scan summary counts.</summary>
    private static void LogFixBounded(Renderer r, Material m, string prop)
    {
        if (_verboseFixLogs >= VerboseFixLogMax)
            return;
        _verboseFixLogs++;
        string shader = m.shader != null ? m.shader.name : "<null-shader>";
        VRLog.Info(Name, $"depth-corrected '{r.name}' material '{m.name}' shader='{shader}' {prop} 8→4."
            + (_verboseFixLogs == VerboseFixLogMax ? " (verbose change log cap reached — counting only from here.)" : string.Empty));
    }

    /// <summary>
    /// Exclusions for the FIX (census is broader on purpose): mod layer (sky/laser), any mod-owned
    /// object (head/hands rig incl. its particle systems — all mod objects are "GloomhavenVR.*"),
    /// actor outlines (Outlinable/OutlineWrapper), and Canvas/UI graphics.
    /// </summary>
    private static bool IsExcluded(Renderer r)
    {
        if (r.gameObject.layer == VRLayers.ModLayer)
            return true;

        try
        {
            if (IsModOwned(r.transform))
                return true;
            if (r.GetComponentInParent<Canvas>() != null)
                return true;
            if (_outlinableType != null && r.GetComponentInParent(_outlinableType) != null)
                return true;
            if (_outlineWrapperType != null && r.GetComponentInParent(_outlineWrapperType) != null)
                return true;
        }
        catch (Exception e)
        {
            // Be conservative: if we can't prove it's safe, EXCLUDE it (don't touch actor outlines).
            LogFirstFailure($"exclusion check threw for '{r.name}' — excluding to be safe: {e.Message}");
            return true;
        }
        return false;
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

    private static void EnsureTypes()
    {
        if (_typesResolved)
            return;
        _typesResolved = true;
        _outlinableType = ResolveType("EPOOutline.Outlinable") ?? ResolveType("Outlinable");
        _outlineWrapperType = ResolveType("OutlineWrapper");
        if (_outlinableType == null)
            LogMissingOnce("EPOOutline.Outlinable");
        if (_outlineWrapperType == null)
            LogMissingOnce("OutlineWrapper");
    }

    private static void LogMissingOnce(string typeName)
    {
        if (_loggedMissing.Add(typeName))
            VRLog.Info(Name, $"outline type not found (exclusion for it skipped): {typeName}.");
    }

    private static void LogFirstFailure(string message)
    {
        if (_firstFailureLogged)
            return;
        _firstFailureLogged = true;
        VRLog.Warn(Name, message);
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
