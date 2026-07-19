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
    /// PRESS-IDENTITY GATE (P6 reopen fix, replaces the old <c>_armed</c> release latch).
    /// The id of the physical press that must NOT toggle because it already served the
    /// menu's most-recent open/close. The old latch keyed re-arming off
    /// <see cref="NonDominantHold.ButtonIsUp"/>, which is TRUE on the very release frame
    /// that produces the tap — so it re-armed and toggled on the same edge, and could not
    /// tell the press that CLOSED the menu apart from the press that should RE-OPEN it.
    /// The failure mode: in a scenario the game treats the controllers as a GAMEPAD, so
    /// the X press is consumed by the game's own gamepad-escape and closes ESCMenu itself
    /// (there is never an "OPTIONS TAP CLOSED" line — only the "closed externally"
    /// re-sync); the mod then saw that same press's release with the menu already closed
    /// and the reopen edge was lost/ambiguous. Keying on the press IDENTITY instead makes
    /// one physical press serve exactly one intent: the close/open press is "spent", its
    /// release is ignored, and the NEXT independent press (a new <see cref="NonDominantHold.PressId"/>)
    /// always toggles — open→close(any path)→open(next press)→… indefinitely.
    /// Sentinel <c>-1</c> matches no real press (ids start at 1).
    /// </summary>
    private int _spentPressId = -1;

    /// <summary>
    /// Cached ESCMenu (REOPEN fix). Root cause found in the press-diagnostic log: after the mod
    /// CLOSES the menu via <c>menu.Hide()</c>, its GameObject is DEACTIVATED and
    /// <c>Singleton&lt;ESCMenu&gt;.IsInitialized</c> flips FALSE (the game's own close/gamepad-escape
    /// leaves it initialized — which is why external closes reopened fine, but a mod X-close did
    /// not). With the singleton null, <see cref="Tick"/> early-returned and no X could ever reopen
    /// it until a scenario reload re-created the singleton — exactly the reported symptom. A
    /// deactivated (not destroyed) MonoBehaviour is still a live C# reference, so we cache it the
    /// first time the singleton is valid and keep using it: reopen re-activates its GameObject and
    /// calls Show(). The cache self-invalidates on scene unload (the object is destroyed →
    /// Unity-null), where it is re-acquired from the fresh singleton.
    /// </summary>
    private ESCMenu? _menu;

    public void Tick()
    {
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        // Re-acquire only when our cached reference is Unity-DEAD (destroyed/scene change), NOT
        // merely when the singleton reports uninitialized (a mod-close deactivation nulls the
        // singleton but leaves the object alive — that is the case we must survive to reopen).
        if (_menu == null && Singleton<ESCMenu>.IsInitialized)
            _menu = Singleton<ESCMenu>.Instance;
        ESCMenu? menu = _menu;
        if (menu == null)
        {
            _open = false; // no pause menu (wrong scene) — drop the state
            _spentPressId = NonDominantHold.PressId; // spend any in-flight press across the scene boundary
            return;
        }

        bool actuallyOpen = menu.IsOpen;

        // Re-sync an externally opened/closed menu (the game's own gamepad-escape on the
        // X button, the menu's Resume/Back, the escape chord, a laser click, a scene
        // change). The press that COINCIDED with this external change is spent: its
        // release must not toggle, so the game closing the menu on a press cannot bounce
        // straight back open, and — crucially — the NEXT independent press reopens.
        if (_open != actuallyOpen)
        {
            _open = actuallyOpen;
            _spentPressId = NonDominantHold.PressId;
            VRLog.Info("WorldUI", actuallyOpen
                ? "OptionsToggle: pause menu opened externally — X-tap re-synced (now open; this press is spent, " +
                  "the next independent press will close it)."
                : "OptionsToggle: pause menu closed externally — X-tap re-synced (now closed; this press is spent, " +
                  "the next independent press will open it).");
        }

        if (!NonDominantHold.ShortTapThisFrame)
            return;

        // Consume the press so no later consumer this frame acts on the same tap.
        NonDominantHold.Consumed = true;

        // Distinct-edge guard: the press that just opened/closed the menu externally (or
        // that the mod itself already toggled on) is spent — only a genuinely fresh,
        // independent press may toggle. This is what guarantees a reliable REOPEN.
        if (NonDominantHold.PressId == _spentPressId)
        {
            VRLog.Info("WorldUI", "OPTIONS TAP: ignored — this press already served the menu's open/close " +
                                  "(spent); the next independent X press toggles.");
            return;
        }

        if (actuallyOpen)
        {
            menu.Hide(); // public ESCMenu.Hide() -> myWindow.Hide()
            _open = false;
            _spentPressId = NonDominantHold.PressId; // this press did the close — it must not reopen
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", "OPTIONS TAP: pause menu CLOSED (X tap) — back to the game.");
        }
        else
        {
            // ESCMenu has no public Show; its opener is just myWindow.Show(). Calling the
            // window's Show() fires ESCMenu.OnShow via its onTransitionBegin listener.
            // Belt-and-suspenders re-openability: re-activate the window GameObject if a
            // previous close left it inactive, so Show() never depends on the window
            // having stayed active since the last open.
            var w = menu.GetComponent<UIWindow>();
            if (!w.gameObject.activeSelf)
                w.gameObject.SetActive(true);
            w.Show();
            _open = true;
            _spentPressId = NonDominantHold.PressId; // this press did the open — it must not re-close
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", "OPTIONS TAP: pause menu OPENED (X tap) — floats in front of the player " +
                                  $"in VR (activeInHierarchy={w.gameObject.activeInHierarchy}).");
        }
    }
}
