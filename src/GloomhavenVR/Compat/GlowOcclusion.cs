using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #4 follow-up (companion to <see cref="WallSolidifier"/>) — makes the game's world VFX GLOW
/// respect perspective, so a candle / torch / fire glow BEHIND a solid wall is hidden by the wall
/// instead of bleeding through it.
///
/// THE PROBLEM
/// -----------
/// The game's world lighting VFX (RFX4 / KriptoFX additive glow, render queue ~3000–3900) draw with
/// <c>ZTest Always</c> / no depth test, so they paint OVER opaque walls regardless of what is in
/// front of them. <see cref="WallSolidifier"/> makes the walls opaque + depth-writing, but a glow
/// that ignores depth still draws on top. The user wants EVERY element (except the skybox — owned by
/// <see cref="Core.SkyBackdrop"/>, untouched here) to respect perspective.
///
/// WHAT THIS DOES (mirrors WallSolidifier's proven pattern: reflection-only, VR-gated, scene-scan on
/// Init + every additive sceneLoaded)
/// -----------------------------------------------------------------------------------------------
///  1. DIAGNOSTIC (ALWAYS, even when the fix toggle is off): logs every world-space <see cref="Renderer"/>
///     whose material has <c>_ZTest</c>/<c>_ZTestMode</c> == <see cref="CompareFunction.Always"/> (8),
///     reporting renderer name, parent name, shader name and render queue — bounded to ~30 lines then
///     a count. This reveals the ACTUAL names of what punches through walls so the next round can
///     refine the match patterns (we cannot see the live scene from here).
///  2. FIX (config-gated <c>OpaqueWorldGlow</c>, default ON): for those depth-ignoring world renderers
///     whose name / parent name / material name matches FIRE/CANDLE/TORCH/FLAME/LIGHT/GLOW/LANTERN/
///     BRAZIER/EMBER/FX (case-insensitive), forces the material <c>_ZTest</c>/<c>_ZTestMode</c> from
///     Always(8) to <see cref="CompareFunction.LessEqual"/>(4) on PER-INSTANCE materials so the glow
///     respects walls. EXCLUDES: renderers on the mod layer (<see cref="VRLayers.ModLayer"/> — sky /
///     laser), any renderer with an <c>Outlinable</c>/<c>OutlineWrapper</c> in its parent hierarchy
///     (the game's actor outlines, a deliberate gameplay affordance), and Canvas/UI renderers.
///
/// Strict no-op when VR isn't running. Every game-type lookup is reflection-guarded (missing type
/// logged once, never thrown). Reversible on <see cref="Uninstall"/> (hot-reload) — the original
/// shared materials are restored. Never throws into the game (first failure logged, then silent).
/// </summary>
internal static class GlowOcclusion
{
    private const string Name = "GlowOcclusion";
    private const int DiagMaxLines = 30;

    private static readonly int ZTestProp = Shader.PropertyToID("_ZTest");
    private static readonly int ZTestModeProp = Shader.PropertyToID("_ZTestMode");
    private static readonly int Always = (int)CompareFunction.Always;      // 8
    private static readonly int LEqual = (int)CompareFunction.LessEqual;   // 4

    /// <summary>Case-insensitive fragments that mark a renderer/material as a world glow VFX.</summary>
    private static readonly string[] GlowPatterns =
    [
        "fire", "candle", "torch", "flame", "light", "glow", "lantern", "brazier", "ember", "fx",
    ];

    private static ConfigFile? _configFile;
    private static ConfigEntry<bool>? _opaqueWorldGlow;

    private static bool _hooked;
    private static bool _typesResolved;
    private static Type? _outlinableType;
    private static Type? _outlineWrapperType;
    private static bool _firstFailureLogged;
    private static readonly HashSet<string> _loggedMissing = [];

    // Reversibility: for every renderer we depth-corrected, the ORIGINAL shared materials so
    // Uninstall (hot-reload / shutdown) can put them back verbatim.
    private static readonly List<(Renderer renderer, Material[] origShared)> _modified = [];

    /// <summary>Bind config + hook scene loads and run one immediate scan. No-op if VR isn't running.</summary>
    public static void Install()
    {
        if (_hooked || !VRSession.IsRunning)
            return;
        EnsureConfig();
        SceneManager.sceneLoaded += OnSceneLoaded;
        _hooked = true;
        Scan();
    }

    /// <summary>Unhook scene loads and restore every depth-corrected renderer (hot-reload safety).</summary>
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
            "When true, world VFX glow (fire/candle/torch/flame/lantern/brazier/ember/light/fx) that "
            + "ignores depth (ZTest Always) is forced to respect walls, so it can't be seen through "
            + "solid geometry. The diagnostic scan runs regardless of this toggle. Disable if it "
            + "harms an intended look.");
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
            EnsureTypes();
            bool fixOn = _opaqueWorldGlow?.Value ?? true;

            Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
            int candidates = 0;
            int diagLines = 0;
            int fixedRenderers = 0;

            foreach (Renderer r in all)
            {
                if (r == null || !IsDepthIgnoring(r))
                    continue;
                candidates++;

                string rname = r.name;
                Transform? parent = r.transform.parent;
                string pname = parent != null ? parent.name : "<no-parent>";
                Material[] shared = r.sharedMaterials;

                // DIAGNOSTIC (always) — bounded.
                if (diagLines < DiagMaxLines)
                {
                    Material? first = shared.FirstOrDefault(m => m != null);
                    string shader = first != null && first.shader != null ? first.shader.name : "<null-shader>";
                    int queue = first != null ? first.renderQueue : -1;
                    VRLog.Info(Name,
                        $"depth-ignoring world renderer #{candidates}: '{rname}' parent='{pname}' "
                        + $"shader='{shader}' queue={queue}.");
                    diagLines++;
                }

                // FIX (gated) — only the recognised glow VFX, and never the excluded classes.
                if (!fixOn)
                    continue;
                if (IsExcluded(r))
                    continue;
                if (!MatchesGlow(rname, pname, shared))
                    continue;

                if (DepthCorrect(r))
                    fixedRenderers++;
            }

            VRLog.Info(Name,
                $"scan: {candidates} depth-ignoring world renderer(s) found (logged up to {DiagMaxLines}); "
                + $"fix {(fixOn ? "ON" : "OFF")}, depth-corrected {fixedRenderers} glow renderer(s) this scan.");
        }
        catch (Exception e)
        {
            LogFirstFailure($"scan threw (glow may still bleed through walls): {e.Message}");
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
            string shader = m.shader != null ? m.shader.name : "<null-shader>";

            if (m.HasProperty(ZTestProp) && m.GetInt(ZTestProp) == Always)
            {
                m.SetInt(ZTestProp, LEqual);
                VRLog.Info(Name, $"depth-corrected '{r.name}' material '{m.name}' shader='{shader}' _ZTest 8→4.");
                changed = true;
            }
            if (m.HasProperty(ZTestModeProp) && m.GetInt(ZTestModeProp) == Always)
            {
                m.SetInt(ZTestModeProp, LEqual);
                VRLog.Info(Name, $"depth-corrected '{r.name}' material '{m.name}' shader='{shader}' _ZTestMode 8→4.");
                changed = true;
            }
        }

        if (changed)
            _modified.Add((r, origShared));
        else
            r.sharedMaterials = origShared; // nothing to change → drop the instances we just made
        return changed;
    }

    /// <summary>Exclusions: mod layer (sky/laser), actor outlines (Outlinable/OutlineWrapper), UI.</summary>
    private static bool IsExcluded(Renderer r)
    {
        if (r.gameObject.layer == VRLayers.ModLayer)
            return true;

        try
        {
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

    private static bool MatchesGlow(string rname, string pname, Material[] mats)
    {
        if (HasPattern(rname) || HasPattern(pname))
            return true;
        foreach (Material m in mats)
        {
            if (m != null && HasPattern(m.name))
                return true;
        }
        return false;
    }

    private static bool HasPattern(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        foreach (string p in GlowPatterns)
        {
            if (s!.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
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
