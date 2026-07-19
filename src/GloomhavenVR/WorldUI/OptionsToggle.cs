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

        // Cheap re-acquire every frame: cached ref (survives deactivation) or the singleton.
        if (_menu == null && Singleton<ESCMenu>.IsInitialized)
            _menu = Singleton<ESCMenu>.Instance;
        // EXPENSIVE recovery + diagnostic ONLY on an actual tap (never per-frame): if the cache
        // and singleton are both empty when the user presses to (re)open, scan the scene INCLUDING
        // inactive objects. The game's Singleton clears its ref only in OnDestroy, so this recovers
        // an ESCMenu that is alive but lost to the singleton (deactivated / reparented by the modal
        // float). If this ALSO finds nothing, the object was genuinely DESTROYED on close — the log
        // line then says so unambiguously (build 08600b7's log proved every post-close tap produced
        // a perfect shortTap yet OptionsToggle still hit menu==null: this pins destroyed-vs-lost).
        if (_menu == null && NonDominantHold.ShortTapThisFrame)
        {
            ESCMenu[] found = UnityEngine.Object.FindObjectsOfType<ESCMenu>(includeInactive: true);
            if (found.Length > 0)
            {
                _menu = found[0];
                VRLog.Info("WorldUI", $"OptionsToggle: ESCMenu recovered by scene scan (incl-inactive, " +
                                      $"active={found[0].gameObject.activeInHierarchy}) — singleton had lost it.");
            }
            else
            {
                VRLog.Info("WorldUI", $"OPTIONS TAP: no ESCMenu object exists (Singleton.IsInitialized=" +
                                      $"{Singleton<ESCMenu>.IsInitialized}, scene-scan incl-inactive found none) — " +
                                      "the game DESTROYED the pause menu on close; a reload currently re-creates it.");
            }
        }
        ESCMenu? menu = _menu;
        if (menu == null)
        {
            _open = false; // no pause menu (wrong scene / destroyed) — drop the state
            _spentPressId = NonDominantHold.PressId; // spend any in-flight press across the boundary
            return;
        }

        // PARENT open-state (cheap, every frame). The re-sync latch tracks the ESC menu
        // itself: opening a sub-menu leaves the parent open behind it, so this is the
        // meaningful "external change" edge. The TOGGLE DECISION below reads the full
        // live picture (parent + sub-menus) freshly on the tap, so it can never desync.
        bool escOpen = menu.IsOpen;

        // Re-sync an externally opened/closed menu (the game's own gamepad-escape on the
        // X button, the menu's Resume/Back, the escape chord, a laser click, a scene
        // change). The press that COINCIDED with this external change is spent: its
        // release must not toggle, so the game closing the menu on a press cannot bounce
        // straight back open, and — crucially — the NEXT independent press reopens.
        if (_open != escOpen)
        {
            _open = escOpen;
            _spentPressId = NonDominantHold.PressId;
            VRLog.Info("WorldUI", escOpen
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
        // independent press may toggle. This is what guarantees a reliable REOPEN. Kept
        // EXACTLY as the core reopen fix: one physical press serves exactly one intent.
        if (NonDominantHold.PressId == _spentPressId)
        {
            VRLog.Info("WorldUI", "OPTIONS TAP: ignored — this press already served the menu's open/close " +
                                  "(spent); the next independent X press toggles.");
            return;
        }

        // LIVE open-state, read from the ACTUAL game windows on THIS tap — never a cached
        // `_open` bool that a stray external Show/Hide (e.g. ESCMenu.OnControllerAreaFocused
        // re-showing the parent) can desync. A sub-menu that is focused/open while the ESC
        // menu is closed still forces a CLOSE, so X while any sub-menu shows always closes
        // everything rather than "reopening". These probes run ONLY on the tap frame (never
        // per-frame), so the singleton lookups and the one compendium scene scan are cheap
        // at human tap cadence; the flags are also reused by the close branch below.
        UIOptionsWindow? optOwner = Singleton<UIOptionsWindow>.IsInitialized ? Singleton<UIOptionsWindow>.Instance : null;
        UIMultiplayerEscSubmenu? mpOwner = Singleton<UIMultiplayerEscSubmenu>.IsInitialized ? Singleton<UIMultiplayerEscSubmenu>.Instance : null;
        UIWindow? optWin = optOwner != null ? optOwner.GetComponent<UIWindow>() : null;
        UIWindow? mpWin = mpOwner != null ? mpOwner.Window : null;
        UIWindow? compWin = FindOpenCompendiumWindow(); // side-effect-free; only ever an OPEN instance
        bool optOpen = optWin != null && optWin.IsOpen;
        bool mpOpen = mpWin != null && mpWin.IsOpen;
        bool compOpen = compWin != null;
        bool anySubmenuOpen = optOpen || mpOpen || compOpen;
        bool actuallyOpen = escOpen || anySubmenuOpen;

        VRLog.Info("WorldUI", $"[OptionsToggle] X tap: actuallyOpen={actuallyOpen} (esc={escOpen} opt={optOpen} " +
                              $"mp={mpOpen} comp={compOpen}) -> {(actuallyOpen ? "CLOSE" : "OPEN")}");

        if (actuallyOpen)
        {
            // Submenu-safe close: hide any open sub-window DIRECTLY (a belt for the rare
            // case a sub-menu outlives the parent's SetAllTogglesOff cascade), then hide
            // the PARENT LAST — its ESCMenu.OnHide → toggleGroup.SetAllTogglesOff cascade
            // is the belt-and-suspenders final word that closes anything still lingering.
            if (optOpen)
                optOwner!.Hide();  // UIOptionsWindow.Hide() -> m_Window.Hide()
            if (mpOpen)
                mpOwner!.Hide();   // UIMultiplayerEscSubmenu.Hide() -> Window.Hide()
            if (compOpen)
                compWin!.Hide();   // compendium UIWindow.Hide()
            menu.Hide();           // ESCMenu.Hide() -> myWindow.Hide() -> OnHide cascade
            _open = false;
            _spentPressId = NonDominantHold.PressId; // this press did the close — it must not reopen
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", "OPTIONS TAP: pause menu + all sub-menus CLOSED (X tap) — back to the game.");
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

    /// <summary>
    /// The compendium sub-window IF it is currently OPEN, else null. Deliberately a scene
    /// scan rather than <c>Singleton&lt;SpecialUIProvider&gt;.Instance.CompendiumUIObject</c>:
    /// that getter INSTANTIATES the compendium prefab on first access (a synchronous
    /// Addressables load — SpecialUIProvider.GetCompendiumUI), so probing it merely to test
    /// open-state would create the window and hitch the frame. An OPEN compendium window is
    /// always an active object, so <c>FindObjectsOfType</c> (active-only) reaches it; this
    /// runs ONLY on an X tap (never per-frame), and the returned instance is reused for the
    /// close-branch Hide() so no second lookup is needed. Compendium window ID verified as
    /// <c>UIWindowID.CompendiumPanel</c> (ModalFallback FallbackIds / SyncEscMenuTabHighlights).
    /// </summary>
    private static UIWindow? FindOpenCompendiumWindow()
    {
        UIWindow[] all = UnityEngine.Object.FindObjectsOfType<UIWindow>();
        for (int i = 0; i < all.Length; i++)
        {
            UIWindow w = all[i];
            if (w != null && w.ID == UIWindowID.CompendiumPanel && w.IsOpen)
                return w;
        }
        return null;
    }
}
