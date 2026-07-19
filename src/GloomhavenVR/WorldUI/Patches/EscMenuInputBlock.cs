using System;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// ISSUE 1 — controller-X "keeps reopening" the pause menu. Root cause: the ESC-menu
/// open state has TWO independent controller-driven owners on one physical X press.
/// <list type="bullet">
/// <item><description>The GAME, on the PRESS-DOWN edge: <c>ESCMenu.Awake</c> registers
/// <c>InputManager.RegisterToOnPressed(KeyAction.UI_PAUSE, ShowUIWindow)</c>
/// (ESCMenu.cs:194) — an auto-SHOW — and, because the mod forces mouse mode
/// (<see cref="InputModeGuard"/>), <c>OnControllerTypeChanged</c> sets
/// <c>myWindow.escapeKeyAction = Toggle</c> (ESCMenu.cs:226-234), so the ESC-menu window
/// is a Toggle escapable and <c>UIWindowManager.Escape()</c> (fired on
/// <c>KeyAction.UI_CANCEL</c>) auto-TOGGLES it via <c>UIWindow.Escape()</c>.</description></item>
/// <item><description>The MOD, on the RELEASE edge:
/// <see cref="OptionsToggle"/> toggles from <see cref="NonDominantHold.ShortTapThisFrame"/>.</description></item>
/// </list>
/// One physical press therefore flips the state twice a few frames apart and the menu lands
/// reopened. This patch makes the MOD the SOLE controller-X owner of the ESC-menu family by
/// suppressing the game's two controller paths — but ONLY for the ESC menu, and ONLY while VR
/// runs:
/// <list type="number">
/// <item><description><see cref="ShowUIWindowSuppressor"/> — prefix no-op of
/// <c>ESCMenu.ShowUIWindow</c> (the <c>UI_PAUSE</c> auto-show). Method-specific to ESCMenu, so
/// nothing else is touched; only ever invoked through the registered <c>Action</c> delegate, so
/// no JIT-inlining hazard.</description></item>
/// <item><description><see cref="EscMenuEscapeSuppressor"/> — prefix of
/// <c>UIWindow.Escape()</c> scoped to <c>__instance.ID == UIWindowID.ESCMenu</c>: returns
/// <c>false</c> (unhandled, no Hide/Show) so the game's cancel-key can neither auto-hide nor
/// auto-toggle the ESC menu. Sub-windows (Options, Compendium, Multiplayer, …) keep their own
/// <c>Escape()</c> intact — their laser-X close still works.</description></item>
/// </list>
/// The mod's own direct <c>UIWindow.Show()/Hide()</c> calls (from <see cref="OptionsToggle"/> and
/// the laser <c>ModalCloseButton</c>) do NOT go through either suppressed method, so they are
/// unaffected — the mod remains fully in control.
///
/// Everything is reflection-guarded (<see cref="AccessTools"/>): if either target method can't be
/// resolved (renamed/removed by a game update) that patch degrades to a strict no-op via a null
/// <c>TargetMethod</c> and a single warning, exactly like <c>WallFadeDisable</c>/<c>InitialInputSkip</c>.
/// Both prefixes gate on <see cref="VRSession.IsRunning"/>, so desktop play is 100% vanilla.
///
/// Registration is self-contained (the natural point, <c>WorldUIModule</c>, is not owned by this
/// change): <see cref="EnsureRegistered"/> is called from <see cref="InputModeGuard.Tick"/> and
/// PatchAll's both patch classes once, the first frame VR (or dev conversion) is active and the
/// shared Harmony instance exists.
/// </summary>
internal static class EscMenuInputBlock
{
    private static bool _registered;
    private static bool _degraded;

    /// <summary>True while the game's controller ESC-menu paths must yield to the mod.</summary>
    internal static bool ShouldSuppress => VRSession.IsRunning;

    /// <summary>
    /// Idempotent, cheap-after-first-call self-registration. Called every frame from
    /// <see cref="InputModeGuard.Tick"/>; PatchAll's the two suppressors exactly once, only when
    /// VR (or forced dev conversion) is active and the shared Harmony instance is available.
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (_registered)
            return;
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        Harmony? harmony = VRSession.Harmony;
        if (harmony == null)
            return;

        _registered = true; // set first: a throw must not retry-spam every frame
        try
        {
            harmony.PatchAll(typeof(ShowUIWindowSuppressor));
            harmony.PatchAll(typeof(EscMenuEscapeSuppressor));
            VRLog.Info("WorldUI",
                "EscMenuInputBlock: registered — the game's controller ESC-menu auto-show " +
                "(UI_PAUSE→ESCMenu.ShowUIWindow) and auto-toggle (UI_CANCEL→UIWindow.Escape for ESCMenu) " +
                "are suppressed while VR runs; OptionsToggle is the sole controller-X owner.");
        }
        catch (Exception e)
        {
            Degrade($"registration threw: {e.Message}");
        }
    }

    /// <summary>Log the first failure and thereafter stay silent.</summary>
    internal static void Degrade(string reason)
    {
        if (_degraded)
            return;
        _degraded = true;
        VRLog.Warn("EscMenuInputBlock",
            $"disabled — {reason}. Game's controller ESC-menu paths left vanilla (X may double-act).");
    }
}

/// <summary>
/// Prefix no-op of <c>ESCMenu.ShowUIWindow()</c> — the private handler the game registers to
/// <c>KeyAction.UI_PAUSE</c> (ESCMenu.cs:194), which calls <c>myWindow.Show()</c>. Blocking it
/// stops the game auto-SHOWing the ESC menu on the controller button so only the mod opens it.
/// Only ever reached through the registered <c>Action</c> delegate → never inlined.
/// </summary>
[HarmonyPatch]
internal static class ShowUIWindowSuppressor
{
    private static MethodBase? TargetMethod()
    {
        try
        {
            MethodInfo? show = AccessTools.Method(typeof(ESCMenu), "ShowUIWindow");
            if (show == null)
            {
                EscMenuInputBlock.Degrade("method not found: ESCMenu.ShowUIWindow()");
                return null;
            }
            return show;
        }
        catch (Exception e)
        {
            EscMenuInputBlock.Degrade($"ESCMenu.ShowUIWindow resolution threw: {e.Message}");
            return null;
        }
    }

    private static bool Prefix()
    {
        if (!EscMenuInputBlock.ShouldSuppress)
            return true; // vanilla when VR is off

        VRLog.Debug("WorldUI",
            "EscMenuInputBlock: suppressed game UI_PAUSE auto-show (ESCMenu.ShowUIWindow) — mod owns X.");
        return false; // skip myWindow.Show(): the mod's OptionsToggle is the only opener
    }
}

/// <summary>
/// Prefix of <c>UIWindow.Escape()</c> scoped to the ESC-menu window
/// (<c>__instance.ID == UIWindowID.ESCMenu</c>). In mouse mode the ESC-menu window is a
/// <c>Toggle</c> escapable, so the game's <c>UIWindowManager.Escape()</c> (on
/// <c>KeyAction.UI_CANCEL</c>) would auto-hide/-show it. Returning <c>false</c> (unhandled — no
/// Hide/Show) leaves the ESC-menu untouched by the game's cancel key while the mod owns it.
/// Other windows' <c>Escape()</c> runs unchanged, so sub-windows still close on their own X.
/// </summary>
[HarmonyPatch]
internal static class EscMenuEscapeSuppressor
{
    private static MethodBase? TargetMethod()
    {
        try
        {
            MethodInfo? escape = AccessTools.Method(typeof(UIWindow), "Escape");
            if (escape == null)
            {
                EscMenuInputBlock.Degrade("method not found: UIWindow.Escape()");
                return null;
            }
            return escape;
        }
        catch (Exception e)
        {
            EscMenuInputBlock.Degrade($"UIWindow.Escape resolution threw: {e.Message}");
            return null;
        }
    }

    private static bool Prefix(UIWindow __instance, ref bool __result)
    {
        if (!EscMenuInputBlock.ShouldSuppress)
            return true; // vanilla when VR is off
        if (__instance == null || __instance.ID != UIWindowID.ESCMenu)
            return true; // scope to the ESC menu ONLY — sub-windows escape normally

        VRLog.Debug("WorldUI",
            "EscMenuInputBlock: suppressed game UI_CANCEL auto-toggle (UIWindow.Escape for ESCMenu) — mod owns X.");
        __result = false; // report "not handled / did not toggle" so the manager moves on
        return false;     // skip the Hide()/Show() toggle body
    }
}
