using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// P6: a short TAP of the NON-dominant lower face button (A/X) opens or closes the
/// game's own OPTIONS window (<c>UIOptionsWindow</c>) — the user wants it to appear
/// "just like the main menu, a screen in front of you", closable with another tap.
///
/// Nothing here draws the screen: opening the game window is enough. The window's
/// <c>UIWindowID.Options</c> is in <see cref="ModalFallback"/>'s FallbackIds set, so
/// the moment it shows the mod asserts <c>VRMode.ModalUI</c> and floats it in VR
/// automatically (window-style) or on the full flat screen — no FlatScreen/
/// ModalFallback change needed.
///
/// The tap edge comes from <see cref="NonDominantHold.ShortTapThisFrame"/> (a
/// sub-threshold, unconsumed release); the settings short-hold and the manual/escape
/// long-holds own the at/above-threshold band, so the tap never collides with them.
///
/// State is kept in sync with the REAL window (<c>UIWindow.IsOpen</c>): if the player
/// closes it another way (its own Back/ESC, a scene change), the latch re-syncs so the
/// next tap opens it again rather than trying to close an already-closed window.
/// </summary>
internal sealed class OptionsToggle
{
    /// <summary>
    /// Debounce window (P6 flicker fix): after a tap toggles the window, further taps
    /// are blocked for at least this long AND until the real window state settles to
    /// the intended one. <see cref="ModalFallback"/> independently adopts
    /// <c>UIWindowID.Options</c> and asserts/releases <see cref="VRMode.ModalUI"/> as the
    /// window opens/closes; that mode churn fed extra tap edges into the shared
    /// <see cref="NonDominantHold"/> tracker and re-toggled us (~1 cycle/0.8 s). Consuming
    /// the press + this settle-and-cooldown gate makes it one-press-one-toggle: the mode
    /// assertion then settles once and holds while the window stays open. Unscaled time so
    /// a paused game (Time.timeScale 0 while modal) does not freeze the cooldown.
    /// </summary>
    private const float DebounceSeconds = 0.5f;

    private bool _open;

    // Debounce state: while true, taps are ignored until the window's real IsOpen matches
    // the intended _open state AND the cooldown has elapsed (transition settled).
    private bool _debouncing;
    private float _debounceUntil;

    public void Tick()
    {
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        UIOptionsWindow? window = Singleton<UIOptionsWindow>.IsInitialized
            ? Singleton<UIOptionsWindow>.Instance
            : null;
        if (window == null)
        {
            _open = false; // no window (wrong scene) — drop the latch
            _debouncing = false;
            return;
        }

        bool actuallyOpen = IsWindowOpen(window);

        // Re-sync an externally closed window so the next tap re-opens it.
        if (_open && !actuallyOpen)
        {
            _open = false;
            VRLog.Info("WorldUI", "OptionsToggle: options window closed externally — X-tap latch re-synced (now closed).");
        }

        // Clear the debounce once the real window state matches the intended one (the
        // Show/Hide transition, and any ModalFallback mode assert/release it triggered,
        // has settled) AND the cooldown has elapsed. Only then can a new tap act.
        if (_debouncing && actuallyOpen == _open && Time.unscaledTime >= _debounceUntil)
            _debouncing = false;

        if (!NonDominantHold.ShortTapThisFrame)
            return;

        // Consume the press so no later consumer this frame — and no edge the mode churn
        // re-derives — acts on the same tap.
        NonDominantHold.Consumed = true;

        // Still settling from the previous toggle: swallow the (churn-manufactured) edge.
        if (_debouncing)
        {
            VRLog.Info("WorldUI", "OPTIONS TAP: ignored — still settling the previous toggle (debounce).");
            return;
        }

        if (actuallyOpen)
        {
            window.Hide();
            _open = false;
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", "OPTIONS TAP: game options window CLOSED (X tap) — back to the game.");
        }
        else
        {
            // The RectTransform arg is only the highlight pointer target; the
            // window's own transform is a safe self-reference (UIOptionsWindow.Show :188).
            window.Show(window.transform as RectTransform);
            _open = true;
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", "OPTIONS TAP: game options window OPENED (X tap) — floats in front of the player in VR.");
        }

        // Arm the debounce: block re-toggles until the window state settles to _open and
        // the cooldown lapses. Breaks the ModalFallback mode-churn feedback loop.
        _debouncing = true;
        _debounceUntil = Time.unscaledTime + DebounceSeconds;
    }

    /// <summary>Live open state from the window's own UIWindow component (RequireComponent guarantees it).</summary>
    private static bool IsWindowOpen(UIOptionsWindow window)
    {
        UIWindow uw = window.GetComponent<UIWindow>();
        return uw != null && uw.IsOpen;
    }
}
