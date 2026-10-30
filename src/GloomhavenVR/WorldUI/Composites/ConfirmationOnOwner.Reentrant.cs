using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ConfirmationOnOwner
{
    internal sealed class NativeHideOpening
    {
        internal readonly Component Box;
        internal readonly Request Request;
        internal NativeHideOpening(Component box, Request request) { Box = box; Request = request; }
    }

    internal static NativeHideOpening? BeforeNativeHide(UIWindow window)
    {
        if (!Enabled || !window.IsOpen) return null;
        for (int i = 0; i < NavigationOpenings.Count; i++)
        {
            NavigationOpening opening = NavigationOpenings[i];
            if (opening.Box != null && opening.Box.TryGetTarget(out Component? box) && box != null
                && ReferenceEquals(box.GetComponent<UIWindow>(), window)
                && Requests.TryGetValue(box, out Request? current) && ReferenceEquals(current, opening.Request))
                return new NativeHideOpening(box, current);
        }
        return null;
    }

    internal static void AfterNativeHide(UIWindow window, NativeHideOpening? outgoing)
    {
        if (outgoing == null || !Enabled || window == null || window.IsOpen || ForeignOwner(outgoing.Box)
            || !Requests.TryGetValue(outgoing.Box, out Request? next) || ReferenceEquals(next, outgoing.Request) || next.Retired)
            return;
        bool requested = outgoing.Box is ConfirmationBox generic && generic.IsRequested
            || outgoing.Box is UIItemConfirmationBox item && item.IsActive;
        if (!requested) return;
        // Native UIWindow.Hide invokes onHidden before writing Hidden. An original
        // No callback can Show a NEW pooled request during that event; native Show
        // then returns early because the old window is still Shown. The old Hide
        // tail subsequently hides the new request and leaves its controller disabled.
        // Only that exact newer, still-requested opening gets its original window
        // Show after the old tail. No request, callback, actor or outcome is replayed.
        // The native regression counts two explicit requests, two shown/controller
        // enables, one old hide, then exactly the new Yes and its one final hide.
        window.Show(instant: true);
    }
}

[HarmonyPatch]
internal static class ConfirmationReentrantHidePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (MethodInfo method in typeof(UIWindow).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (method.Name == "Hide" && parameters.Length == 1 && parameters[0].ParameterType == typeof(bool)) yield return method;
        }
    }
    private static void Prefix(UIWindow __instance, out ConfirmationOnOwner.NativeHideOpening? __state) =>
        __state = ConfirmationOnOwner.BeforeNativeHide(__instance);
    private static void Postfix(UIWindow __instance, ConfirmationOnOwner.NativeHideOpening? __state) =>
        ConfirmationOnOwner.AfterNativeHide(__instance, __state);
}
