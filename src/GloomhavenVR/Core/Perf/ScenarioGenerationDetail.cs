using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// The supplied Standalone Fastest profile still requests Apparance quality one. The
/// maintainer permits configurable geometry compromises for Frame (2026-10-02). Quality
/// zero is the game's own reduced generation input; its actual renderer/FPS saving remains
/// a hardware question. Only map/wall parameter writers receive a transient profile copy.
/// Native per-quest selection, underground visibility and every other setting are retained.
/// In particular native detail-disable providers never receive this copy: their SetActive
/// path would disable colliders/callbacks rather than only decorative rendering.
///
/// Freeze the choice per scenario scene, including later room reveals. A live option change
/// takes effect at the next scene load rather than rebuilding gameplay geometry mid-turn.
/// No game asset, platform object, authoritative state or Apparance focus is written.
/// </summary>
internal static class ScenarioGenerationDetail
{
    private sealed class SceneProfile
    {
        internal readonly bool Reduced;
        internal readonly Dictionary<ApparancePlatformSettingData, ApparancePlatformSettingData> Copies = new();
        internal SceneProfile(bool reduced) { Reduced = reduced; }
    }

    private static readonly Dictionary<int, SceneProfile> Profiles = new();
    private static bool _installed;
    private static bool _failureLogged;
    [ThreadStatic] private static int _scopeScene;
    [ThreadStatic] private static int _scopeDepth;

    internal readonly struct ScopeState
    {
        internal readonly int Scene, Depth;
        internal ScopeState(int scene, int depth) { Scene = scene; Depth = depth; }
    }

    internal static void Install()
    {
        if (_installed) return;
        SceneManager.sceneUnloaded += SceneUnloaded;
        _installed = true;
        try
        {
            VRSession.Harmony?.PatchAll(typeof(ProceduralMapTile_Parameters_GenerationDetailPatch));
            VRSession.Harmony?.PatchAll(typeof(ProceduralWall_Parameters_GenerationDetailPatch));
            VRSession.Harmony?.PatchAll(typeof(PlatformSetting_Apparance_GenerationDetailPatch));
        }
        catch (Exception e) { ReportFailure(e); }
    }

    internal static void Shutdown()
    {
        if (_installed) SceneManager.sceneUnloaded -= SceneUnloaded;
        _installed = false;
        foreach (SceneProfile profile in Profiles.Values) Release(profile);
        Profiles.Clear();
        _scopeDepth = 0;
        _scopeScene = 0;
        _failureLogged = false;
    }

    // Prefix/finalizer scopes also restore nested writers after a native exception. Returning
    // the original exception is essential: this presentation hook never consumes game errors.
    internal static ScopeState Enter(ProceduralBase entity)
    {
        var previous = new ScopeState(_scopeScene, _scopeDepth);
        _scopeDepth = 0; // an unrelated nested writer must not inherit a scenario override
        if (!VRSession.IsRunning || entity == null) return previous;
        try
        {
            Scene scene = entity.gameObject.scene;
            if (!scene.IsValid() || !IsScenario(scene)) return previous;
            if (!Profiles.TryGetValue(scene.handle, out SceneProfile? profile))
            {
                profile = new SceneProfile(PerfConfig.ReducedScenarioGenerationOn);
                Profiles.Add(scene.handle, profile);
                VRLog.Info("Perf", "Scenario generation detail: " + (profile.Reduced ? "reduced" : "original")
                    + " for scene " + scene.name + "; frozen through room reveals. Underground and native "
                    + "gameplay state retained; changes apply on the next scenario load.");
            }
            if (profile.Reduced)
            {
                _scopeScene = scene.handle;
                _scopeDepth = previous.Depth + 1;
            }
        }
        catch (Exception e) { ReportFailure(e); }
        return previous;
    }

    internal static void Leave(ScopeState previous)
    { _scopeScene = previous.Scene; _scopeDepth = previous.Depth; }

    internal static void Override(ref ApparancePlatformSettingData result)
    {
        if (_scopeDepth == 0 || !VRSession.IsRunning || result == null
            || !Profiles.TryGetValue(_scopeScene, out SceneProfile? profile) || !profile.Reduced)
            return;
        try
        {
            // Low per-quest overrides already request zero. Keep their exact object identity.
            if (result._qualityLevel <= 0) return;
            if (!profile.Copies.TryGetValue(result, out ApparancePlatformSettingData? copy) || copy == null)
            {
                copy = UnityEngine.Object.Instantiate(result);
                copy.name = "GloomhavenVR transient scenario detail";
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy._qualityLevel = 0;
                profile.Copies[result] = copy;
            }
            result = copy;
        }
        catch (Exception e) { ReportFailure(e); }
    }

    private static bool IsScenario(Scene scene)
    {
        // Positive native scene identity also handles additive ProcGen roots. A mere scene
        // name would accidentally admit editor/map scenery or miss renamed scenario scenes.
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i].GetComponent<ProceduralScenario>() != null) return true;
        return false;
    }

    private static void SceneUnloaded(Scene scene)
    {
        if (!Profiles.TryGetValue(scene.handle, out SceneProfile? profile)) return;
        Release(profile);
        Profiles.Remove(scene.handle);
    }

    private static void Release(SceneProfile profile)
    {
        foreach (ApparancePlatformSettingData copy in profile.Copies.Values)
            if (copy != null) UnityEngine.Object.Destroy(copy);
        profile.Copies.Clear();
    }

    private static void ReportFailure(Exception error)
    {
        if (_failureLogged) return;
        _failureLogged = true;
        VRLog.Note("Perf", "Scenario generation detail: override unavailable; native settings retained ("
            + error.Message + ").");
    }
}

[HarmonyPatch(typeof(ProceduralMapTile), nameof(ProceduralMapTile.WriteExtraParameters))]
internal static class ProceduralMapTile_Parameters_GenerationDetailPatch
{
    private static void Prefix(ProceduralMapTile __instance, out ScenarioGenerationDetail.ScopeState __state) =>
        __state = ScenarioGenerationDetail.Enter(__instance);
    private static Exception? Finalizer(Exception? __exception, ScenarioGenerationDetail.ScopeState __state)
    { ScenarioGenerationDetail.Leave(__state); return __exception; }
}

[HarmonyPatch(typeof(ProceduralWall), nameof(ProceduralWall.WriteExtraParameters))]
internal static class ProceduralWall_Parameters_GenerationDetailPatch
{
    private static void Prefix(ProceduralWall __instance, out ScenarioGenerationDetail.ScopeState __state) =>
        __state = ScenarioGenerationDetail.Enter(__instance);
    private static Exception? Finalizer(Exception? __exception, ScenarioGenerationDetail.ScopeState __state)
    { ScenarioGenerationDetail.Leave(__state); return __exception; }
}

[HarmonyPatch(typeof(PlatformSetting), nameof(PlatformSetting.GetApparenceSettingByCurrentLevel))]
internal static class PlatformSetting_Apparance_GenerationDetailPatch
{
    private static void Postfix(ref ApparancePlatformSettingData __result) => ScenarioGenerationDetail.Override(ref __result);
}
