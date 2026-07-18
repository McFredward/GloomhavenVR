using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// P6: a short TAP of the NON-dominant lower face button (A/X) opens or closes the
/// game's own PAUSE menu (<c>ESCMenu</c>, the ESC / pause screen) — the user wants it to
/// appear "just like the main menu, a screen in front of you", closable with another tap.
/// Options remain reachable from there via the pause menu's own Options button.
///
/// Nothing here draws the screen: opening the game window is enough. The pause menu's
/// <c>UIWindowID.ESCMenu</c> is in <see cref="ModalFallback"/>'s FallbackIds set, so the
/// moment it shows the mod asserts <c>VRMode.ModalUI</c> and floats it in front of the
/// player in VR (window-style) or on the full flat screen — no FlatScreen/ModalFallback
/// change needed. The long-hold escape chord can also close it.
///
/// The tap edge comes from <see cref="NonDominantHold.ShortTapThisFrame"/> (a
/// sub-threshold, unconsumed release); the settings short-hold and the manual/escape
/// long-holds own the at/above-threshold band, so the tap never collides with them.
///
/// State is kept in sync with the REAL window (<c>ESCMenu.IsOpen</c>): if the player
/// closes it another way (its own Resume/Back, the escape chord, a scene change), the
/// latch re-syncs so the next tap opens it again rather than trying to close an
/// already-closed window.
/// </summary>
internal sealed class OptionsToggle
{
    /// <summary>Intended/last-observed open state of the pause menu, mirrored from <c>ESCMenu.IsOpen</c>.</summary>
    private bool _open;

    /// <summary>
    /// Press-cycle latch (P6 flicker fix). A tap may toggle only while armed; a toggle
    /// disarms it. It re-arms only once the button is OBSERVABLY up again
    /// (<see cref="NonDominantHold.ButtonIsUp"/> — hand posed AND not pressed). A held or
    /// hiccuping button keeps ButtonIsUp false, so it can never re-toggle without a
    /// genuine physical release. This replaces the old wall-clock cooldown, which — because
    /// <c>UIWindow.IsOpen</c> flips the same frame as Show/Hide (no transitional state) —
    /// collapsed to a pure 0.5 s rate-limiter that any later release edge re-triggered.
    /// </summary>
    private bool _armed = true;

    public void Tick()
    {
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        ESCMenu? menu = Singleton<ESCMenu>.IsInitialized ? Singleton<ESCMenu>.Instance : null;
        if (menu == null)
        {
            _open = false; // no pause menu (wrong scene) — drop the latch
            _armed = true;
            return;
        }

        bool actuallyOpen = menu.IsOpen;

        // Re-sync an externally opened/closed menu (its own Resume/Back, the escape chord,
        // a scene change) so the next tap does the right thing.
        if (_open != actuallyOpen)
        {
            _open = actuallyOpen;
            VRLog.Info("WorldUI", actuallyOpen
                ? "OptionsToggle: pause menu opened externally — X-tap latch re-synced (now open)."
                : "OptionsToggle: pause menu closed externally — X-tap latch re-synced (now closed).");
        }

        // Re-arm the press-cycle latch once the button is genuinely, observably UP. A
        // still-held or hiccuping button keeps ButtonIsUp false and stays latched.
        if (!_armed && NonDominantHold.ButtonIsUp)
            _armed = true;

        if (!NonDominantHold.ShortTapThisFrame)
            return;

        // Consume the press so no later consumer this frame acts on the same tap.
        NonDominantHold.Consumed = true;

        // Latched from the previous toggle: swallow the edge until the button is released.
        if (!_armed)
        {
            VRLog.Info("WorldUI", "OPTIONS TAP: ignored — button not yet released since the last toggle (latch).");
            return;
        }

        if (actuallyOpen)
        {
            menu.Hide(); // public ESCMenu.Hide() -> myWindow.Hide()
            _open = false;
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", "OPTIONS TAP: pause menu CLOSED (X tap) — back to the game.");
        }
        else
        {
            // ESCMenu has no public Show; its opener is just myWindow.Show(). Calling the
            // window's Show() fires ESCMenu.OnShow via its onTransitionBegin listener.
            menu.GetComponent<UIWindow>().Show();
            _open = true;
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", "OPTIONS TAP: pause menu OPENED (X tap) — floats in front of the player in VR.");
        }

        // Disarm: no re-toggle until the button is observed genuinely up again.
        _armed = false;
    }
}
