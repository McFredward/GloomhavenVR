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

    /// <summary>
    /// BELT reconcile state (Issue 1). After the mod acts on a tap it records the INTENDED
    /// open state (<see cref="_intendedOpen"/>) and the press it acted on
    /// (<see cref="_intendPressId"/>), and arms <see cref="_reconcilePending"/> for the ONE
    /// immediately-following tick. Now that the game's own controller ESC-menu show/toggle is
    /// suppressed (<see cref="Patches.EscMenuInputBlock"/>), a second-actor flip should never
    /// happen — but if a residual one does, it lands within a frame or two of the press. So the
    /// reconcile re-checks the live window state against the intent exactly once, keyed to the
    /// SAME still-active press (no new press since). A legitimate laser "closed externally" comes
    /// far later (human reaction time), long after this one-shot window has closed, so it is never
    /// fought — the existing external-resync below owns that case.
    /// </summary>
    private bool _reconcilePending;
    private bool _intendedOpen;
    private int _intendPressId = -1;

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

        // BELT reconcile (Issue 1): the tick immediately AFTER the mod acted on a tap, re-assert
        // the intent ONCE if the live window state disagrees and no new independent press has
        // happened. One-shot + same-press keyed, so it catches a residual second-actor flip
        // (which races within a frame or two) without ever fighting a much-later laser external
        // close (handled by the external-resync just below).
        if (_reconcilePending)
        {
            _reconcilePending = false; // one-shot: only the tick right after the action
            if (NonDominantHold.PressId == _intendPressId)
            {
                OpenState live = Probe(menu);
                if (live.Any != _intendedOpen)
                {
                    if (_intendedOpen)
                        OpenMenu(menu);
                    else
                        CloseAll(menu, live);
                    _open = _intendedOpen;
                    _spentPressId = _intendPressId; // press stays spent; its release must not re-toggle
                    escOpen = menu.IsOpen;          // re-read so the external-resync below stays consistent
                    VRLog.Info("WorldUI", $"[OptionsToggle] reconcile: live state ({live.Any}) disagreed with " +
                                          $"intent ({_intendedOpen}) on the same press — re-asserted " +
                                          $"{(_intendedOpen ? "OPEN" : "CLOSED")}.");
                }
            }
        }

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
        // at human tap cadence; the owners are reused by the close branch below.
        OpenState st = Probe(menu);
        bool actuallyOpen = st.Any;

        VRLog.Info("WorldUI", $"[OptionsToggle] X tap: actuallyOpen={actuallyOpen} (esc={st.Esc} opt={st.Opt} " +
                              $"mp={st.Mp} comp={st.Comp}) -> {(actuallyOpen ? "CLOSE" : "OPEN")}");

        int pressId = NonDominantHold.PressId;
        if (actuallyOpen)
        {
            CloseAll(menu, st);
            _open = false;
            _spentPressId = pressId; // this press did the close — it must not reopen
            ArmReconcile(intendedOpen: false, pressId);
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", "OPTIONS TAP: pause menu + all sub-menus CLOSED (X tap) — back to the game.");
        }
        else
        {
            OpenMenu(menu);
            _open = true;
            _spentPressId = pressId; // this press did the open — it must not re-close
            ArmReconcile(intendedOpen: true, pressId);
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", "OPTIONS TAP: pause menu OPENED (X tap) — floats in front of the player " +
                                  $"in VR (activeInHierarchy={menu.GetComponent<UIWindow>().gameObject.activeInHierarchy}).");
        }
    }

    /// <summary>Record the intent the mod just asserted so the next tick can belt-reconcile it once.</summary>
    private void ArmReconcile(bool intendedOpen, int pressId)
    {
        _intendedOpen = intendedOpen;
        _intendPressId = pressId;
        _reconcilePending = true;
    }

    /// <summary>
    /// Live open-state of the whole ESC-menu family (parent + Options/Multiplayer/Compendium
    /// sub-windows), read from the ACTUAL game windows. Tap-frequency only (never per-frame):
    /// the singleton lookups and the one compendium scene scan are cheap at human cadence. The
    /// captured windows are reused by <see cref="CloseAll"/> so no second lookup is needed.
    /// </summary>
    private static OpenState Probe(ESCMenu menu)
    {
        bool escOpen = menu.IsOpen;
        UIOptionsWindow? optOwner = Singleton<UIOptionsWindow>.IsInitialized ? Singleton<UIOptionsWindow>.Instance : null;
        UIMultiplayerEscSubmenu? mpOwner = Singleton<UIMultiplayerEscSubmenu>.IsInitialized ? Singleton<UIMultiplayerEscSubmenu>.Instance : null;
        UIWindow? optWin = optOwner != null ? optOwner.GetComponent<UIWindow>() : null;
        UIWindow? mpWin = mpOwner != null ? mpOwner.Window : null;
        UIWindow? compWin = FindOpenCompendiumWindow(); // side-effect-free; only ever an OPEN instance
        bool optOpen = optWin != null && optWin.IsOpen;
        bool mpOpen = mpWin != null && mpWin.IsOpen;
        bool compOpen = compWin != null;
        return new OpenState(escOpen, optOpen, mpOpen, compOpen, optWin, mpWin, compWin);
    }

    /// <summary>
    /// FIX A (controller-X close): route EVERY window of the ESC-menu family through the SAME
    /// path the corner-X uses — <see cref="ModalFallback.CloseFloatedWindow"/>. That path sets
    /// the float's <c>UserClosing</c> flag, the ONLY thing that ever drops a STICKY float (the
    /// whole reachable-menu family is sticky: ModalFallback keeps it floated and force-visible
    /// — <c>ReassertStickyVisible</c> — even after the game hides it). The old direct
    /// <c>owner.Hide()</c>/<c>menu.Hide()</c> calls closed the GAME windows but never set that
    /// flag, so the floated host stayed alive and force-visible forever: the hardware log showed
    /// "hidden — untracked" with NO "released — restored to its 2D home" line, and the pause menu
    /// never visually disappeared on a controller-X close.
    ///
    /// Escape-suppression interplay: <c>UIWindow.Escape()</c> is Harmony-blocked for
    /// ID==ESCMenu (<see cref="Patches.EscMenuInputBlock"/> returns "unhandled" without
    /// toggling), but CloseFloatedWindow calls <c>Hide()</c> whenever the window is still open
    /// after <c>Escape()</c> — so the ESC menu still closes through its own OnHide cascade
    /// (verified: UIWindow.Hide flips IsOpen synchronously, so the reconcile probe on the next
    /// tick sees the family closed; the float itself is released by ModalFallback's next tick,
    /// which counts as closed too — the probe reads game IsOpen, never the float).
    ///
    /// Order: open sub-windows first, then any REMAINING sticky family float (a float whose
    /// game window the single-window toggle already hid reports IsOpen==false, so the live
    /// probes cannot see it), and the parent ESC menu LAST — its OnHide →
    /// toggleGroup.SetAllTogglesOff cascade stays the belt-and-suspenders final word.
    /// </summary>
    private static void CloseAll(ESCMenu menu, OpenState st)
    {
        VRLog.Info("WorldUI", "OPTIONS TAP: close routed through CloseFloatedWindow (release path)");
        if (st.Opt)
            ModalFallback.CloseFloatedWindow(st.OptWin);   // Options UIWindow (UserClosing → Escape/Hide)
        if (st.Mp)
            ModalFallback.CloseFloatedWindow(st.MpWin);    // Multiplayer submenu UIWindow
        if (st.Comp)
            ModalFallback.CloseFloatedWindow(st.CompWin);  // compendium UIWindow
        // Sticky floats the game already hid (single-window toggle) are NOT IsOpen, so the
        // probes above cannot reach them — drop every remaining floated family window too.
        ModalFallback.CloseStickyFloatsExceptEscMenu();
        ModalFallback.CloseFloatedWindow(menu.GetComponent<UIWindow>()); // parent LAST
    }

    /// <summary>
    /// Open the ESC menu. ESCMenu has no public Show; its opener is just myWindow.Show(), which
    /// fires ESCMenu.OnShow via its onTransitionBegin listener. Belt-and-suspenders re-openability:
    /// re-activate the window GameObject if a previous close left it inactive, so Show() never
    /// depends on the window having stayed active since the last open.
    /// </summary>
    private static void OpenMenu(ESCMenu menu)
    {
        var w = menu.GetComponent<UIWindow>();
        if (!w.gameObject.activeSelf)
            w.gameObject.SetActive(true);
        w.Show();
    }

    /// <summary>Immutable snapshot of the ESC-menu family's live open-state plus the resolved
    /// sub-window <c>UIWindow</c>s (FIX A: <see cref="CloseAll"/> passes these to
    /// <see cref="ModalFallback.CloseFloatedWindow"/> — the corner-X release path).</summary>
    private readonly struct OpenState
    {
        internal readonly bool Esc;
        internal readonly bool Opt;
        internal readonly bool Mp;
        internal readonly bool Comp;
        internal readonly UIWindow? OptWin;
        internal readonly UIWindow? MpWin;
        internal readonly UIWindow? CompWin;

        internal bool Any => Esc || Opt || Mp || Comp;

        internal OpenState(bool esc, bool opt, bool mp, bool comp,
            UIWindow? optWin, UIWindow? mpWin, UIWindow? compWin)
        {
            Esc = esc;
            Opt = opt;
            Mp = mp;
            Comp = comp;
            OptWin = optWin;
            MpWin = mpWin;
            CompWin = compWin;
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
