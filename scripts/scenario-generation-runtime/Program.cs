using System;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InteractionProgram
{
    private static int _checks;
    private static void Check(bool value, string name)
    { _checks++; if (!value) throw new Exception(name); }
    private static ApparancePlatformSettingData Read(ApparancePlatformSettingData source)
    { ScenarioGenerationDetail.Override(ref source); return source; }
    public static int Run()
    {
        _checks = 0;
        VRSession.IsRunning = true; PerfConfig.ReducedScenarioGenerationOn = true;
        string suffix = Guid.NewGuid().ToString("N");
        Scene scene = SceneManager.CreateScene("native-scenario-generation-fixture-" + suffix);
        Scene ordinary = SceneManager.CreateScene("native-campaign-fixture-" + suffix);
        var scenario = new GameObject("Procedural Scenario"); scenario.AddComponent<ProceduralScenario>();
        SceneManager.MoveGameObjectToScene(scenario, scene);
        var entity = new GameObject("native additive tile"); var tile = entity.AddComponent<ProceduralMapTile>();
        SceneManager.MoveGameObjectToScene(entity, scene);
        var wallObject = new GameObject("native wall"); var wall = wallObject.AddComponent<ProceduralWall>();
        SceneManager.MoveGameObjectToScene(wallObject, scene);
        var mapObject = new GameObject("ordinary map"); var mapTile = mapObject.AddComponent<ProceduralMapTile>();
        SceneManager.MoveGameObjectToScene(mapObject, ordinary);
        var original = ScriptableObject.CreateInstance<ApparancePlatformSettingData>();
        original.name = "selected native per-quest profile";
        original._qualityLevel = 1; original._showUnderground = true;
        original._disableWallClutterGeneration = true; original._disableWallTorchesGeneration = false;
        original._disableSurfaceFeaturesGeneration = true; original._detailsDisablingLevel = 3;
        var other = ScriptableObject.CreateInstance<ApparancePlatformSettingData>();
        other._qualityLevel = 2; other._showUnderground = false;
        try
        {
            ScenarioGenerationDetail.Install();
            Check(Read(original) == original, "unscoped native callers retain original profile");
            var scope = ScenarioGenerationDetail.Enter(tile);
            var reduced = Read(original);
            Check(original._qualityLevel == 1, "original native asset is never changed");
            Check(reduced != original && reduced._qualityLevel == 0, "native parameter writer gets quality zero copy");
            Check(reduced._showUnderground && reduced._disableWallClutterGeneration
                && !reduced._disableWallTorchesGeneration && reduced._disableSurfaceFeaturesGeneration
                && reduced._detailsDisablingLevel == 3, "copy preserves underground and unrelated per-quest fields");
            Check(Read(original) == reduced, "same source reuses transient copy");
            Check(Read(other) != reduced && !Read(other)._showUnderground, "different per-quest source retains its fields");
            var nested = ScenarioGenerationDetail.Enter(wall);
            Check(Read(original) == reduced, "nested native wall shares frozen scene profile");
            ScenarioGenerationDetail.Leave(nested);
            Check(Read(original) == reduced, "nested exit restores outer scope");
            ScenarioGenerationDetail.Leave(scope);
            Check(Read(original) == original, "native detail providers never receive generation copy");
            scope = ScenarioGenerationDetail.Enter(tile);
            var unrelated = ScenarioGenerationDetail.Enter(mapTile);
            Check(Read(original) == original, "unrelated nested map writer cannot inherit override");
            ScenarioGenerationDetail.Leave(unrelated);
            Check(Read(original) == reduced, "unrelated exit restores outer scope");
            ScenarioGenerationDetail.Leave(scope);
            Check(Read(original) == original, "scope returns to unmodified native callers");
            PerfConfig.ReducedScenarioGenerationOn = false;
            scope = ScenarioGenerationDetail.Enter(tile);
            Check(Read(original) == reduced, "live setting does not change current room reveal quality");
            ScenarioGenerationDetail.Leave(scope);
            VRSession.IsRunning = false;
            scope = ScenarioGenerationDetail.Enter(tile);
            Check(Read(original) == original, "flat game retains native profile");
            ScenarioGenerationDetail.Leave(scope); VRSession.IsRunning = true;

            // The actual production finalizer, including native exception propagation.
            var error = new InvalidOperationException("native failure");
            var patch = typeof(ProceduralMapTile_Parameters_GenerationDetailPatch);
            object?[] arguments = { tile, default(ScenarioGenerationDetail.ScopeState) };
            patch.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);
            Check(Read(original) == reduced, "production Harmony prefix establishes scope");
            object? result = patch.GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new[] { (object)error, arguments[1]! });
            Check(ReferenceEquals(error, result) && Read(original) == original, "native exception propagates and scope closes");

            typeof(ScenarioGenerationDetail).GetMethod("SceneUnloaded", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { scene });
            scope = ScenarioGenerationDetail.Enter(tile);
            Check(Read(original) == original, "new scene activation reads new option and drops old copies");
            ScenarioGenerationDetail.Leave(scope);
            ScenarioGenerationDetail.Shutdown();
            Check(Read(original) == original && original._qualityLevel == 1, "shutdown leaves original intact");
            Check(entity.activeSelf && wallObject.activeSelf && mapObject.activeSelf,
                "native object and room activity retained");
            return _checks;
        }
        finally
        {
            ScenarioGenerationDetail.Shutdown();
            UnityEngine.Object.DestroyImmediate(original); UnityEngine.Object.DestroyImmediate(other);
            UnityEngine.Object.DestroyImmediate(entity); UnityEngine.Object.DestroyImmediate(wallObject);
            UnityEngine.Object.DestroyImmediate(mapObject); UnityEngine.Object.DestroyImmediate(scenario);
        }
    }
}
