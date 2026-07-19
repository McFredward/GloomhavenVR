using System;
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
/// TWO INDEPENDENT PARTS, installed separately so the per-scene-load order is
/// census → <see cref="WallSolidifier"/> wall depth enforcement → ZTest enforcement
/// (multicast sceneLoaded handlers fire in subscription order; CompatModule subscribes them in
/// exactly that order). The census therefore always logs the PRISTINE state the game shipped,
/// before any fix mutates materials.
///
///  1. OCCLUSION CENSUS (<see cref="InstallCensus"/> — ALWAYS on, the evidence engine): one cheap
///     single pass per scene load over all active world <see cref="Renderer"/>s (Mesh/Skinned/
///     ParticleSystem/Line/… — CanvasRenderer-driven UI graphics are not Renderers so UI canvases
///     are excluded by construction), skipping the mod layer and mod-owned objects. Renderers are
///     grouped per unique SHADER NAME; one log line per group reports count, renderQueue min-max,
///     _ZWrite / _ZTest(_ZTestMode) values when present, and one example parent/renderer path.
///     SUSPICIOUS groups sort first (ZTest==Always(8), ZWrite==0, or queue&gt;=2500) — those are
///     the candidates for "draws over walls without owning/testing depth". Bounded to ~40 group
///     lines plus a total-summary line. This is the instrument that will conclusively reveal the
///     wall shader's queue/ZWrite, the health-bar material, the hex-select shader and the insect
///     VFX on the next hardware round.
///  2. ZTEST ENFORCEMENT (<see cref="InstallEnforcement"/>, config-gated <c>OpaqueWorldGlow</c>,
///     default ON): ANY non-excluded world renderer material with <c>_ZTest</c>/<c>_ZTestMode</c>
///     == <see cref="CompareFunction.Always"/> (8) is forced to
///     <see cref="CompareFunction.LessEqual"/> (4) on PER-INSTANCE materials (<c>r.materials</c>,
///     never shared assets) — no name matching any more; depth-ignoring is the offence itself.
///     EXCLUDED: mod layer (sky/laser/hands), Canvas/UI graphics, anything with an EPOOutline
///     <c>Outlinable</c>/<c>OutlineWrapper</c> in its parent chain (actor outlines are an intended
///     gameplay affordance), and anything under the mod's head/hands rig (mod objects are all named
///     "GloomhavenVR.*").
///
/// Strict no-op when VR isn't running. Every game-type lookup is reflection-guarded (missing type
/// logged once, never thrown). Reversible on <see cref="Uninstall"/> (hot-reload) — the original
/// shared materials are restored. Never throws into the game (first failure logged, then silent).
/// Change logging is bounded: first ~30 changes verbose, then counted only.
/// </summary>
internal static class GlowOcclusion
{
    private const string Name = "GlowOcclusion";
    private const int CensusMaxGroups = 40;
    private const int VerboseFixLogMax = 30;
    private const int SuspiciousQueue = 2500;

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

    // Reversibility: for every renderer we depth-corrected, the ORIGINAL shared materials so
    // Uninstall (hot-reload / shutdown) can put them back verbatim.
    private static readonly List<(Renderer renderer, Material[] origShared)> _modified = [];

    // ------------------------------------------------------------------ install / uninstall

    /// <summary>
    /// Hook the always-on occlusion census (evidence engine) and run it once immediately.
    /// MUST be installed BEFORE <see cref="WallSolidifier.Install"/> so the census handler fires
    /// first on every scene load and logs the pristine shipped state. No-op if VR isn't running.
    /// </summary>
    public static void InstallCensus()
    {
        if (_censusHooked || !VRSession.IsRunning)
            return;
        SceneManager.sceneLoaded += OnSceneLoadedCensus;
        _censusHooked = true;
        RunCensus();
    }

    /// <summary>
    /// Hook the config-gated ZTest enforcement and run it once immediately.
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
        RunEnforcement();
    }

    /// <summary>Unhook both handlers and restore every depth-corrected renderer (hot-reload safety).</summary>
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

    private static void OnSceneLoadedCensus(Scene scene, LoadSceneMode mode)
    {
        if (VRSession.IsRunning)
            RunCensus();
    }

    private static void OnSceneLoadedFix(Scene scene, LoadSceneMode mode)
    {
        if (VRSession.IsRunning)
            RunEnforcement();
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
    /// One single pass over all active world renderers, grouped per shader name; one log line per
    /// group, suspicious groups first, capped at <see cref="CensusMaxGroups"/> lines + a summary.
    /// </summary>
    private static void RunCensus()
    {
        if (!VRSession.IsRunning)
            return;

        try
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

            var sb = new StringBuilder(4096);
            string sceneName = SceneManager.GetActiveScene().name;
            int lines = 0;
            int suspiciousCount = ordered.Count(kv => kv.Value.Suspicious);

            foreach (KeyValuePair<string, ShaderGroup> kv in ordered)
            {
                if (lines >= CensusMaxGroups)
                    break;
                lines++;
                string shaderName = kv.Key;
                ShaderGroup g = kv.Value;
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

            sb.Append($"census total: scene='{sceneName}' worldRenderers={totalRenderers} "
                + $"shaderGroups={groups.Count} suspicious={suspiciousCount} "
                + $"logged={lines}/{groups.Count} modSkipped={skipped}.");
            VRLog.Info(Name, sb.ToString());
        }
        catch (Exception e)
        {
            LogFirstFailure($"census threw: {e.Message}");
        }
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
    /// Config-gated (<c>OpaqueWorldGlow</c>); reversible via <see cref="_modified"/>.
    /// </summary>
    private static void RunEnforcement()
    {
        if (!VRSession.IsRunning)
            return;

        try
        {
            EnsureTypes();
            bool fixOn = _opaqueWorldGlow?.Value ?? true;

            Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
            int candidates = 0;
            int fixedRenderers = 0;

            foreach (Renderer r in all)
            {
                if (r == null || !IsDepthIgnoring(r))
                    continue;
                candidates++;

                if (!fixOn || IsExcluded(r))
                    continue;

                if (DepthCorrect(r))
                    fixedRenderers++;
            }

            VRLog.Info(Name,
                $"ZTest enforcement: {candidates} depth-ignoring world renderer(s); "
                + $"fix {(fixOn ? "ON" : "OFF")}, depth-corrected {fixedRenderers} this scan "
                + $"({_modified.Count} tracked for restore).");
        }
        catch (Exception e)
        {
            LogFirstFailure($"ZTest enforcement threw (VFX may still bleed through walls): {e.Message}");
        }
    }

    /// <summary>True if any of the renderer's SHARED materials has _ZTest/_ZTestMode == Always(8).</summary>
    private static bool IsDepthIgnoring(Renderer r)
    {
        Material[] shared;
        try { shared = r.sharedMaterials; }
        catch { return false; }

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
