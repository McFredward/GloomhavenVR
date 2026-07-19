using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Gamepad-mode guard (ROADMAP P3c #7): keeps <c>InputManager.GamePadInUse</c> false
/// while VR runs, so <c>SceneController.GetSceneNameForType</c> loads the mouse scene
/// variants (<c>Game</c>, <c>MainMenu</c>, ... — NOT the <c>*_gamepad</c> prefab sets)
/// and buttons commit synchronously instead of arming gamepad long-press flows.
///
/// Mechanism (verified via ilspycmd, GH.Runtime.dll, InputManager):
/// <code>
///   private static bool isUseGamepadInPc;                       // the PC-side flag
///   public static bool GamePadInUse =&gt; PlatformLayer.Instance.IsConsole || isUseGamepadInPc;
///   public void SetGamepadInputDevice(bool isUseGamepad)        // the ONLY public entry
///   private void AssignGamepadBindingsToPlayerActions(bool withRemoveCurrentBindings = false)
///       // ← the only writer of isUseGamepadInPc = true (called from SetGamepadInputDevice)
///   private void AssignMouseKeyboardBindingsToPlayerActions(bool withRemoveCurrentBindings = false)
///       // ← the only writer of isUseGamepadInPc = false
///   ESceneType.Scenario =&gt; InputManager.GamePadInUse ? "Game_gamepad" : "Game"   // SceneController:211
/// </code>
/// Least-invasive approach: a prefix on the public entry swallows switches TO gamepad
/// while the guard is active (a real gamepad button press mid-session would otherwise
/// flip the mode); switches to mouse mode always pass. Both patched methods are far
/// above inlining size. On activation any pre-existing gamepad mode is switched back
/// through the game's own <c>SetGamepadInputDevice(false)</c> (bindings swap included).
/// Vanilla behavior is untouched while VR is off (prefixes return true).
/// </summary>
internal static class InputModeGuard
{
    private static bool _enforced;

    /// <summary>Guard active: VR (or forced dev conversion) + config.</summary>
    internal static bool Active =>
        WorldUIConfig.ForceMouseMode.Value && WorldUIConfig.ConversionActive;

    /// <summary>Called every frame by the WorldUI driver (cheap; static reads only).</summary>
    internal static void Tick()
    {
        // Self-register the ESC-menu controller-input suppressors (Issue 1). Idempotent and
        // cheap after the first frame; done here because the natural registration point
        // (WorldUIModule) is outside this change's ownership. See EscMenuInputBlock.
        Patches.EscMenuInputBlock.EnsureRegistered();

        if (!Active)
        {
            _enforced = false;
            return;
        }
        if (_enforced || !InputManager.GamePadInUse)
            return;

        InputManager manager = Singleton<InputManager>.Instance;
        if (manager == null)
            return;

        VRLog.Info("WorldUI", "InputModeGuard: switching the game from gamepad to mouse mode for VR.");
        manager.SetGamepadInputDevice(false);
        _enforced = true;
    }

    internal static void Reset() => _enforced = false;
}

/// <summary>
/// Swallow runtime switches TO gamepad mode while the guard is active.
/// <c>public void SetGamepadInputDevice(bool isUseGamepad)</c> — verified (see
/// <see cref="InputModeGuard"/>); large body, no inlining risk.
/// </summary>
[HarmonyPatch(typeof(InputManager), nameof(InputManager.SetGamepadInputDevice))]
internal static class InputManager_SetGamepadInputDevice_Patch
{
    private static bool Prefix(bool isUseGamepad)
    {
        if (isUseGamepad && InputModeGuard.Active)
        {
            VRLog.Debug("WorldUI", "InputModeGuard: blocked switch to gamepad mode (VR keeps mouse-mode UI).");
            return false;
        }
        return true;
    }
}

/// <summary>
/// Belt-and-braces on the single <c>isUseGamepadInPc = true</c> writer:
/// <c>private void AssignGamepadBindingsToPlayerActions(bool withRemoveCurrentBindings
/// = false)</c> (verified) — in case a code path reaches it without going through
/// <c>SetGamepadInputDevice</c>.
/// </summary>
[HarmonyPatch(typeof(InputManager), "AssignGamepadBindingsToPlayerActions")]
internal static class InputManager_AssignGamepadBindings_Patch
{
    private static bool Prefix() => !InputModeGuard.Active;
}
