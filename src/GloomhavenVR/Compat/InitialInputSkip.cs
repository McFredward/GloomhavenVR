using System;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #1 — after the game intro, the vanilla "press any key to continue" screen
/// (<c>Script.GUI.SMNavigation.InitialInputScreen</c>) blocks the way to the main menu.
/// In VR the mod drives a virtual mouse and there is no obvious "any key" to press, so
/// the player is stranded on a static screen. This patch auto-dismisses it: it postfixes
/// <c>InitialInputScreen.Update()</c> and, once the window is actually open (the exact
/// moment the game itself would accept input), invokes the private
/// <c>SelectInputDevice(false)</c> — i.e. it does precisely what a real keyboard/mouse
/// keypress would do (<c>isGamepad=false</c> matches the mod's virtual-mouse input
/// scheme). That hides the window and advances <c>UINavigation.StateMachine</c> to the
/// exit tag (the main menu), so the game proceeds with no keypress.
///
/// Everything is resolved by reflection (<see cref="AccessTools"/>): if the game type or
/// either method can't be found (renamed/removed by a game update) the patch is a strict
/// no-op — <see cref="TargetMethod"/> returns null so Harmony never patches anything, and
/// the screen is left 100% vanilla. Purely cosmetic flow: it touches no game state beyond
/// dismissing this one screen, exactly as the player would have.
/// </summary>
[HarmonyPatch]
internal static class InitialInputSkip
{
    private const string ScreenTypeName = "Script.GUI.SMNavigation.InitialInputScreen";

    private static MethodInfo? _selectInputDevice; // private void SelectInputDevice(bool)
    private static PropertyInfo? _windowProp;       // public UIWindow Window { get; }
    private static PropertyInfo? _isOpenProp;       // bool UIWindow.IsOpen { get; }
    private static bool _degraded;

    /// <summary>
    /// Harmony asks this for the method to patch. Resolving everything here (and returning
    /// null on any miss) is what makes the whole patch degrade to a strict no-op when the
    /// game type/methods aren't present.
    /// </summary>
    private static MethodBase? TargetMethod()
    {
        try
        {
            Type? screen = AccessTools.TypeByName(ScreenTypeName);
            if (screen == null)
                return Degrade($"type not found: {ScreenTypeName}");

            MethodInfo? update = AccessTools.Method(screen, "Update");
            if (update == null)
                return Degrade($"method not found: {ScreenTypeName}.Update()");

            _selectInputDevice = AccessTools.Method(screen, "SelectInputDevice", new[] { typeof(bool) });
            if (_selectInputDevice == null)
                return Degrade($"method not found: {ScreenTypeName}.SelectInputDevice(bool)");

            // Optional gate: prefer to fire only once the UIWindow is actually open (mirrors
            // the game's own Update guard). If we can't resolve it we still fire — the game
            // just called Show(), so this screen is the current UI regardless.
            _windowProp = AccessTools.Property(screen, "Window");
            Type? uiWindow = _windowProp?.PropertyType;
            _isOpenProp = uiWindow != null ? AccessTools.Property(uiWindow, "IsOpen") : null;

            return update;
        }
        catch (Exception e)
        {
            return Degrade($"resolution threw: {e.Message}");
        }
    }

    /// <summary>
    /// Runs after the game's own per-frame input check. If the window is open, dismiss it
    /// ourselves with <c>isGamepad=false</c> — identical to a real keypress at the same
    /// call site, so the state machine advances to the main menu exactly as vanilla would.
    /// After the first successful dismiss the window closes and every later frame is a
    /// no-op (the open-gate returns false).
    /// </summary>
    private static void Postfix(object __instance)
    {
        if (_selectInputDevice == null)
            return;
        try
        {
            if (!IsWindowOpen(__instance))
                return;
            _selectInputDevice.Invoke(__instance, new object[] { false });
        }
        catch (Exception e)
        {
            Degrade($"dismiss threw: {e.Message}");
        }
    }

    private static bool IsWindowOpen(object instance)
    {
        if (_windowProp == null || _isOpenProp == null)
            return true; // couldn't resolve the gate — the screen is up, so proceed
        object? window = _windowProp.GetValue(instance);
        if (window == null)
            return false;
        return _isOpenProp.GetValue(window) is bool open && open;
    }

    /// <summary>Log the first failure and thereafter stay silent; returns null for TargetMethod.</summary>
    private static MethodBase? Degrade(string reason)
    {
        if (!_degraded)
        {
            _degraded = true;
            VRLog.Warn("InitialInputSkip",
                $"disabled — {reason}. 'Press any key to continue' screen left vanilla.");
        }
        return null;
    }
}
