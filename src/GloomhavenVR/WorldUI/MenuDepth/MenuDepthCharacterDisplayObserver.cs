using System;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Observe native requests without changing their model, animation or refcount.</summary>
[HarmonyPatch(typeof(Character3DDisplayManager), nameof(Character3DDisplayManager.Display),
    typeof(Component), typeof(ECharacter), typeof(string), typeof(string))]
internal static class MenuDepthCharacterDisplayObserver
{
    private static bool _reported;

    [HarmonyPostfix]
    private static void AfterDisplay(Character3DDisplayManager __instance, Component request)
    {
        try { MenuDepthCharacters.ObserveDisplay(__instance, request); }
        catch (Exception exception)
        {
            if (_reported) return;
            _reported = true;
            VRLog.Warn("WorldUI", "MENU DEPTH CHARACTER discovery failed after native display; native preview retained: "
                + exception.GetType().Name + ".");
        }
    }
}

/// <summary>Observe actual native presentation births without polling every object in the scene.</summary>
[HarmonyPatch]
internal static class MenuDepthLifecycleObserver
{
    private static int _failures;

    [HarmonyTargetMethods]
    private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> Targets()
    {
        var emitted = new System.Collections.Generic.HashSet<System.Reflection.MethodBase>();
        foreach (System.Reflection.MethodBase method in MenuDepthDriver.NativeOwnerLifecycles())
            if (emitted.Add(method)) yield return method;
        foreach (System.Reflection.MethodBase? method in new System.Reflection.MethodBase?[]
        {
            AccessTools.Method(typeof(UnityEngine.UI.Graphic), "OnEnable", Type.EmptyTypes),
            AccessTools.Method(typeof(UnityEngine.UI.Graphic), "OnDestroy", Type.EmptyTypes),
            AccessTools.PropertySetter(typeof(UnityEngine.UI.RawImage), "texture"),
            AccessTools.Method(typeof(UnityEngine.UI.UIWindow), "OnEnable", Type.EmptyTypes),
            AccessTools.Method(typeof(Character3DDisplayManager), "Start", Type.EmptyTypes),
        })
            if (method != null && emitted.Add(method)) yield return method;
    }

    [HarmonyPostfix]
    private static void AfterNative(object __instance, System.Reflection.MethodBase __originalMethod)
    {
        // Graphic's virtual base also serves native text and mod render effects.
        // Reject them before queueing, parent lookup or owner-field reflection.
        try
        {
            if (__instance is UnityEngine.UI.Graphic graphic)
            {
                if (graphic is not UnityEngine.UI.Image && graphic is not UnityEngine.UI.RawImage) return;
                if (__originalMethod.Name == "OnDestroy")
                {
                    MenuDepthDriver.ForgetGraphic(graphic);
                    if (graphic is UnityEngine.UI.RawImage removed) MenuDepthCharacters.ForgetImage(removed);
                    return;
                }
                MenuDepthDriver.ObserveGraphic(graphic);
                if (graphic is UnityEngine.UI.RawImage image) MenuDepthCharacters.ObserveImage(image);
            }
            else if (__instance is Character3DDisplayManager manager) MenuDepthCharacters.ObserveManager(manager);
            else if (__instance is MonoBehaviour owner) MenuDepthDriver.ObserveOwner(owner);
        }
        catch (Exception exception)
        {
            int count = ++_failures;
            if (count <= 3 || (count & (count - 1)) == 0)
                VRLog.Note("WorldUI", "MENU DEPTH lifecycle observation failed: " + __originalMethod.DeclaringType?.Name
                    + "." + __originalMethod.Name + "; " + exception.GetType().Name + "; occurrence=" + count + ".");
        }
    }
}
