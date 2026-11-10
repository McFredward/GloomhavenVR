using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Core.SteamLink;

/// <summary>Passive arrival notifications for the already owned head source.
/// These never replace native spawn, enable, pool, UI or input callbacks.</summary>
internal static class SceneMaterialObservers
{
    private static SceneTransparentMaterials? _current;
    private static Harmony? _installedFor;
    private static int _failures;

    internal static void Install()
    {
        Harmony? harmony = VRSession.Harmony;
        if (harmony == null || ReferenceEquals(_installedFor, harmony)) return;
        harmony.PatchAll(typeof(SceneMaterialGraphicArrival));
        harmony.PatchAll(typeof(SceneMaterialGraphicRebuilt));
        harmony.PatchAll(typeof(SceneMaterialPoolArrival));
        _installedFor = harmony;
    }

    internal static void Bind(SceneTransparentMaterials materials) => _current = materials;
    internal static void Unbind(SceneTransparentMaterials? materials)
    {
        if (ReferenceEquals(_current, materials)) _current = null;
    }

    internal static void GraphicArrived(Graphic graphic)
    {
        try { if (_current?.Ready == true) _current.EnrollGraphic(graphic); }
        catch (Exception error) { try { Report(error); } catch (Exception) { /* A diagnostic cannot escape a native UI callback. */ } }
    }

    internal static void ObjectArrived(GameObject result)
    {
        try { if (_current?.Ready == true) _current.EnrollObject(result); }
        catch (Exception error) { try { Report(error); } catch (Exception) { /* A diagnostic cannot escape a native pool/action callback. */ } }
    }

    internal static void GraphicRebuilt(Graphic graphic)
    {
        try { if (_current?.Ready == true) _current.GraphicRebuilt(graphic); }
        catch (Exception error) { try { Report(error); } catch (Exception) { /* Keep the native graphic rebuild independent. */ } }
    }

    private static void Report(Exception error)
    {
        if (_failures++ < 3)
            VRLog.Info("Core", "Scene coverage material arrival could not be observed: "
                + error.GetType().Name + "; native callback and objects remain unchanged.");
    }
}

[HarmonyPatch(typeof(GraphicRegistry), nameof(GraphicRegistry.RegisterGraphicForCanvas))]
internal static class SceneMaterialGraphicArrival
{
    [HarmonyPostfix]
    private static void Postfix(Graphic graphic) => SceneMaterialObservers.GraphicArrived(graphic);
}

[HarmonyPatch(typeof(Graphic), "UpdateMaterial")]
internal static class SceneMaterialGraphicRebuilt
{
    [HarmonyPostfix]
    private static void Postfix(Graphic __instance) => SceneMaterialObservers.GraphicRebuilt(__instance);
}

[HarmonyPatch]
internal static class SceneMaterialPoolArrival
{
    // Target the single native implementation used by all three pool wrappers.
    // Reflection avoids requiring a publisher assembly in unrelated Unity tools.
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() => AccessTools.Method("ObjectPool:Spawn", new[]
    {
        typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Quaternion), typeof(bool)
    });

    [HarmonyPrepare]
    private static bool Prepare() => AccessTools.TypeByName("ObjectPool") != null;

    [HarmonyPostfix]
    private static void Postfix(GameObject __result) => SceneMaterialObservers.ObjectArrived(__result);
}
