using System;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using Script.GUI.Controller;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// Why the on-screen keyboard appeared for zero frames.
///
/// <para><c>ControllerInputKeyboard</c> lives in the keyboard popup's own subtree and derives from
/// <c>ControllerInputElement</c>, whose <c>OnEnable</c> is unconditional:</para>
/// <code>
/// if (InputManager.GamePadInUse) OnEnabledControllerControl();   // → keyboard.Show()
/// else                           OnDisabledControllerControl();  // → keyboard.Hide()
/// </code>
/// <para>So <c>UIKeyboard.Show()</c> activates the popup, Unity delivers <c>OnEnable</c> into the
/// freshly-activated subtree synchronously, the mod's forced MOUSE mode
/// (<see cref="InputModeGuard"/>) takes the else-branch, and the popup hides itself again before
/// <c>Show()</c> has even reached its own <c>OnShow</c> callback. The game's log records the whole
/// round trip as an <c>Added escapable</c> / <c>Removed escapable</c> pair with nothing in between.</para>
///
/// <para>THE EARLIER READING WAS HALF RIGHT. Mouse mode is what makes the keys clickable at all
/// (<c>UIKeyboardKey.Awake</c> only wires <c>button.onClick</c> when <c>GamePadInUse</c> is false).
/// It is ALSO what makes the popup refuse to stay up. Both halves come from the same flag, so the
/// flag cannot be flipped — the auto-hide has to be suppressed instead, and only it.</para>
///
/// <para>SCOPE. The prefix runs the base class's one observable effect (<c>isEnabled = false</c>) and
/// then skips only the <c>keyboard.Hide()</c> — and only for the single keyboard
/// <see cref="VRKeyboard"/> is currently showing, identified by instance, so the other windows'
/// keyboards keep hiding themselves exactly as they do now. Every other path into
/// <c>OnDisabledControllerControl</c> (<c>OnDisable</c>, a real controller-type change) is left
/// alone by construction: none of them happens while the mod owns the keyboard, and if one does,
/// <see cref="VRKeyboard.Detach"/> has already released ownership.</para>
///
/// <para>Desktop play never reaches any of this: <see cref="VRKeyboard.IsShowing"/> is false unless
/// the mod put the keyboard up, which only happens while VR runs.</para>
/// </summary>
internal static class KeyboardAutoHideBlock
{
    private static bool _registered;
    private static bool _degraded;

    /// <summary>
    /// Idempotent self-registration, called from <see cref="VRKeyboard.Tick"/> the first frame the
    /// keyboard is wanted. Deliberately NOT at module start: the patch is pointless until a text
    /// field takes focus, and registering late keeps the failure (if any) next to the feature.
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (_registered)
            return;

        Harmony? harmony = VRSession.Harmony;
        if (harmony == null)
            return;

        _registered = true; // set first: a throw must not retry-spam every frame
        try
        {
            harmony.PatchAll(typeof(KeyboardHideSuppressor));
            VRLog.Info("WorldUI",
                "KeyboardAutoHideBlock: registered — ControllerInputKeyboard's mouse-mode auto-hide " +
                "is suppressed for the one keyboard the mod is showing, so the popup can stay up.");
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
        VRLog.Warn("KeyboardAutoHideBlock",
            $"disabled — {reason}. The on-screen keyboard will hide itself the instant it is shown, " +
            "so text entry needs a physical keyboard.");
    }
}

/// <summary>
/// Prefix of <c>ControllerInputKeyboard.OnDisabledControllerControl()</c>, scoped by instance to the
/// keyboard <see cref="VRKeyboard"/> is showing. See <see cref="KeyboardAutoHideBlock"/> for why.
/// </summary>
[HarmonyPatch]
internal static class KeyboardHideSuppressor
{
    private static MethodBase? TargetMethod()
    {
        try
        {
            MethodInfo? hide = AccessTools.Method(
                typeof(ControllerInputKeyboard), "OnDisabledControllerControl");
            if (hide == null)
            {
                KeyboardAutoHideBlock.Degrade(
                    "method not found: ControllerInputKeyboard.OnDisabledControllerControl()");
                return null;
            }
            return hide;
        }
        catch (Exception e)
        {
            KeyboardAutoHideBlock.Degrade(
                $"ControllerInputKeyboard.OnDisabledControllerControl resolution threw: {e.Message}");
            return null;
        }
    }

    private static bool Prefix(ControllerInputKeyboard __instance)
    {
        if (__instance == null)
            return true;
        if (!VRKeyboard.OwnsKeyboardOf(__instance))
            return true; // every other keyboard behaves exactly as it does in the vanilla game

        // The base class's whole body is `isEnabled = false`. Do that, skip only the Hide().
        __instance.isEnabled = false;
        return false;
    }
}
