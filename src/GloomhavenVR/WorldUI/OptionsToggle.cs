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
    private bool _open;

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
            return;
        }

        bool actuallyOpen = IsWindowOpen(window);

        // Re-sync an externally closed window so the next tap re-opens it.
        if (_open && !actuallyOpen)
        {
            _open = false;
            VRLog.Info("WorldUI", "OptionsToggle: options window closed externally — X-tap latch re-synced (now closed).");
        }

        if (!NonDominantHold.ShortTapThisFrame)
            return;

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
    }

    /// <summary>Live open state from the window's own UIWindow component (RequireComponent guarantees it).</summary>
    private static bool IsWindowOpen(UIOptionsWindow window)
    {
        UIWindow uw = window.GetComponent<UIWindow>();
        return uw != null && uw.IsOpen;
    }
}
