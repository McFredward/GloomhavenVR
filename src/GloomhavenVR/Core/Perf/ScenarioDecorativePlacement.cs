using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Build604 hardware still drew original forest bays at zero detail. The native Apparance
/// CreateObject path resolves the loaded prefab, computes bounds, then calls CreateInstance
/// and retains the returned GameObject by handle/group. For a positively proven all-decorative
/// prefab at zero applicable density, return a transform-only placement with the same pose.
/// Native handle/name/child grouping and removal continue unchanged; no null native output,
/// gameplay state, asset, resource table or generation parameters are altered.
///
/// A retained original-template recipe can instantiate the exact original hierarchy later.
/// This avoids that prefab's mesh/collider/script instances and MaterialLoader.Start loads.
/// The native resource-list/prefab bundle was resolved BEFORE this seam and is still loaded.
/// Mixed floor/wall, effects, lights, native props/actors and unknown callbacks never qualify.
/// The original clone's native visual lifecycle runs normally when the option restores it.
/// </summary>
internal static class ScenarioDecorativePlacement
{
    private sealed class Recipe
    {
        internal GameObject Placement = null!;
        internal GameObject Template = null!;
        internal GameObject? Visual;
        internal Transform? VisualRoot;
        internal int Categories;
    }

    private static readonly List<Recipe> Recipes = new();
    private static readonly Dictionary<Transform, Recipe> RestoredRoots = new();
    [ThreadStatic] private static bool _insideObjectPlacement;
    [ThreadStatic] private static bool _leafPlacement;
    private static int _cursor;
    private static int _deferredObjects;
    private static int _deferredRenderers;
    private static int _reportedObjects;
    private static bool _failureLogged;
    internal static int DeferredObjects => _deferredObjects;
    internal static int DeferredRenderers => _deferredRenderers;

    internal static void Install()
    {
        VRSession.Harmony?.PatchAll(typeof(ApparanceEntity_ObjectPlacementContextPatch));
        VRSession.Harmony?.PatchAll(typeof(ApparanceEntity_DecorativePlacementPatch));
    }

    // CreateInstance alone cannot prove whether native CreateObject will subsequently use
    // this prefab as a group parent. Mesh-instanced children bypass CreateInstance entirely.
    // A nested/thread-local native scope admits only explicit leaf output (child_count == 0).
    internal static int BeginObjectPlacement(int childCount)
    {
        int previous = (_insideObjectPlacement ? 1 : 0) | (_leafPlacement ? 2 : 0);
        _insideObjectPlacement = true;
        _leafPlacement = childCount == 0;
        return previous;
    }

    internal static void EndObjectPlacement(int previous)
    {
        _insideObjectPlacement = (previous & 1) != 0;
        _leafPlacement = (previous & 2) != 0;
    }

    internal static bool IsRestoredRoot(Transform root) => RestoredRoots.ContainsKey(root);

    private static bool Zero(int categories) =>
        ((categories & 1) == 0 || PerfConfig.ScenarioSceneryDensityPercentValue <= 0)
        && ((categories & 2) == 0 || PerfConfig.ScenarioVegetationDensityPercentValue <= 0)
        && ((categories & 4) == 0 || PerfConfig.ScenarioDecorationDensityPercentValue <= 0);

    internal static bool TryDefer(GameObject template, Vector3 position, Vector3 scale,
                                  Quaternion rotation, Transform parent, out GameObject? result)
    {
        result = null;
        // Deferral requires zero for every category represented by the prefab. If all
        // budgets are positive, retain native creation without walking template components.
        if (PerfConfig.ScenarioSceneryDensityPercentValue > 0
            && PerfConfig.ScenarioVegetationDensityPercentValue > 0
            && PerfConfig.ScenarioDecorationDensityPercentValue > 0) return false;
        if (!_insideObjectPlacement || !_leafPlacement || !VRSession.IsRunning
            || template == null || !template.activeSelf || parent == null
            || !ScenarioSceneryBudget.IsScenarioPlacement(parent)) return false;
        int categories = ScenarioSceneryBudget.DecorativeCategories(template);
        if (categories == 0 || !Zero(categories)) return false;
        // Inactive templates keep native creation: an extra recipe transform must never
        // reinterpret a prefab's authored activation/placement callbacks on a later reveal.
        // Allocate no mesh, renderer, collider or native behavior. Returning a real placement
        // retains Apparance's handle and any later emitted children rather than denying output.
        var placement = new GameObject(template.name);
        placement.transform.SetParent(parent, worldPositionStays: false);
        placement.transform.SetPositionAndRotation(position, rotation);
        placement.transform.localScale = scale;
        int renderers = template.GetComponentsInChildren<MeshRenderer>(includeInactive: true).Length;
        Recipes.Add(new Recipe { Placement = placement, Template = template,
            Categories = categories });
        _deferredObjects++;
        _deferredRenderers += renderers;
        result = placement;
        return true;
    }

    private static void Restore(Recipe recipe)
    {
        if (recipe.Visual != null || recipe.Placement == null || recipe.Template == null) return;
        // The native placement is the stable handle and may own generated child groups. Put
        // only the exact original template below it; never replace/delete native group children.
        GameObject visual = UnityEngine.Object.Instantiate(recipe.Template, recipe.Placement.transform, false);
        visual.name = recipe.Template.name;
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        recipe.Visual = visual;
        recipe.VisualRoot = visual.transform;
        RestoredRoots.Add(visual.transform, recipe);
        // Clone Awake executes synchronously; MaterialLoader/DetailsDisabler Start retain their
        // original native order. Budget original visual members before a camera can render them.
        ScenarioSceneryBudget.BeforeContentShown(visual);
    }

    internal static void Refresh(bool complete)
    {
        if (Recipes.Count == 0) return;
        int budget = complete ? Recipes.Count : Math.Min(16, Recipes.Count);
        while (budget-- > 0 && Recipes.Count > 0)
        {
            if (_cursor >= Recipes.Count) _cursor = 0;
            Recipe recipe = Recipes[_cursor];
            if (recipe.Placement == null || recipe.Template == null)
            {
                if (!ReferenceEquals(recipe.VisualRoot, null)) RestoredRoots.Remove(recipe.VisualRoot!);
                int last = Recipes.Count - 1;
                Recipes[_cursor] = Recipes[last]; Recipes.RemoveAt(last);
                continue;
            }
            if (!VRSession.IsRunning || !Zero(recipe.Categories)) Restore(recipe);
            _cursor++;
        }
        if (complete && _reportedObjects != _deferredObjects)
        {
            _reportedObjects = _deferredObjects;
            VRLog.Note("Perf", "Scenario decorative creation: session deferred " + _deferredObjects
                + " original prefab instance(s), " + _deferredRenderers
                + " renderer instance(s); transform/group recipes retained. Native source prefab/resource "
                + "bundles remain loaded; original visual creation resumes when its budget is restored.");
        }
    }

    internal static void Shutdown()
    {
        for (int i = 0; i < Recipes.Count; i++) Restore(Recipes[i]);
        Recipes.Clear();
        RestoredRoots.Clear();
        _cursor = _deferredObjects = _deferredRenderers = _reportedObjects = 0;
        _failureLogged = false;
    }

    internal static void Failure(Exception error)
    {
        if (_failureLogged) return;
        _failureLogged = true;
        VRLog.Note("Perf", "Scenario decorative creation unavailable; native instance retained ("
            + error.Message + ").");
    }
}

[HarmonyPatch]
internal static class ApparanceEntity_ObjectPlacementContextPatch
{
    private static MethodBase TargetMethod()
    {
        // The game implements this Apparance interface explicitly. Resolve the actual native
        // method, including its namespace prefix, rather than a similarly named public helper.
        foreach (MethodInfo method in typeof(ApparanceEntity).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
            if (method.Name.EndsWith(".CreateObject", StringComparison.Ordinal)) return method;
        throw new MissingMethodException(typeof(ApparanceEntity).FullName, "IObjectPlacement.CreateObject");
    }

    private static void Prefix(int child_count, out int __state) =>
        __state = ScenarioDecorativePlacement.BeginObjectPlacement(child_count);

    private static Exception? Finalizer(Exception? __exception, int __state)
    {
        ScenarioDecorativePlacement.EndObjectPlacement(__state);
        return __exception; // preserve native failures while always closing a nested scope
    }
}

[HarmonyPatch(typeof(ApparanceEntity), nameof(ApparanceEntity.CreateInstance))]
internal static class ApparanceEntity_DecorativePlacementPatch
{
    private static bool Prefix(GameObject template, Vector3 position, Vector3 scale, Quaternion rotation,
                               Transform parent, ref GameObject __result)
    {
        try
        {
            if (!ScenarioDecorativePlacement.TryDefer(template, position, scale, rotation, parent, out GameObject? placement))
                return true;
            __result = placement!;
            return false;
        }
        catch (Exception error) { ScenarioDecorativePlacement.Failure(error); return true; }
    }
}
