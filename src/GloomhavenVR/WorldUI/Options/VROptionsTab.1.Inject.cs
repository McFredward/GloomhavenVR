using System;
using System.Collections.Generic;
using System.Text;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and
// reasoning: FlatScreen.1.Core.cs).

/// <summary>
/// THE VR SETTINGS MENU — A WINDOW OF ITS OWN, no longer a tab of the game's Optionen.
///
/// <para>User, 2026-09-02: <i>"Lösche das 'VR Optionen' in den 'Optionen' … Es soll garnicht mehr
/// ein 'Tab' von 'Optionen' sein, sondern ein ganz eigenständiges Menu! Daher braucht es auch die
/// Einstellungstabs links dann nicht mehr anzeigen — wie zuvor. Bau es entsprechend so um das die
/// VR Optionstabs ganz links sind."</i> Three things follow from that and this file does all
/// three: the row is gone from the game's category column, the game's Optionen window is never
/// opened on our behalf (so its tab rail is never on screen beside our settings), and with that
/// rail gone the mod's own topic column IS the leftmost thing in the menu.</para>
///
/// <para>WHY THE CONTENT IS STILL A CLONE OF A TAB WINDOW. Nothing about the settings themselves
/// changed — only their HOST. The pane still has to behave like the game's: the masked scroll
/// viewport, the scrollbar art, the show animation, the <c>ControllerInputAreaLocal</c> and the
/// <c>UIWindow</c> registration all live in serialized references inside the shipped prefab, so
/// the panel is still INSTANTIATED FROM A LIVE DONOR TAB. What is new is that the clone is then
/// DETACHED from the options window and re-parented to the canvas, which is the whole of the
/// change: a <c>UISubmenuGOWindow</c> shows and hides itself (<c>UISubmenuGOWindow.cs</c> :62/:95),
/// it needs no owner, and once it is not a descendant of <c>UIOptionsWindow</c> nothing about the
/// options window can reach it — not its <c>CanvasGroup</c> alpha, not the <c>CloseWindows()</c>
/// that <c>OnShow</c> runs on every open (<c>UIOptionsWindow.cs</c> :203/:276), and not
/// <c>ModalFallback</c>'s "the ancestor wins" rule, which is why the mod's pane used to be drawn
/// INSIDE the options window's world panel instead of getting one of its own.</para>
///
/// <para>WHY THE VR MODAL PATH STILL NEEDS NO WORK. <c>UIWindowID.OptionsSubmenu</c> is already in
/// <see cref="ModalFallback"/>'s family (ModalFallback.1.Core.cs), and the clone carries the
/// donor's window id. Detached, it is a top-level window with no floatable ancestor, so it is
/// floated as its OWN panel — with the grab bar and the close button every floated modal gets.
/// This class still adds no rendering, no placement and no input path.</para>
///
/// <para>THE FIRE EXIT. Detaching is the one step that can fail on a game update (no usable
/// re-parent anchor, or a throw). If it does, the class falls back to EXACTLY the shipped
/// behaviour it replaces — the clone stays inside the options window and is registered as a tab —
/// so the standing rule that the settings must never become unreachable is kept by a path that has
/// already shipped rather than by a promise. <see cref="IsStandalone"/> says which mode is live and
/// <see cref="VRMenuEntry"/> opens the menu accordingly.</para>
///
/// <para>NEVER AN EMPTY WINDOW. <see cref="CanOpen"/> is false until there is a pane AND a content
/// root to build into, and <see cref="VRMenuEntry"/> does not inject a row that cannot open
/// anything. <c>Rebuild</c> writes a header even when a category resolves to zero rows.</para>
///
/// <para>REVERSIBILITY. Everything this creates is a mod-owned clone; the only write to a game
/// object is the fire exit's <c>m_Tabs</c> entry, and <see cref="Shutdown"/> undoes it. In the
/// normal (standalone) mode nothing of the game's is touched at all.</para>
///
/// <para>THE PROBE. The prefab hierarchy inside a tab window cannot be read from the decompiled
/// sources — it is authored data. <see cref="Probe"/> therefore logs what was actually found the
/// first time the window exists, so the content stages are built against the real thing instead of
/// against a guess. It runs once per session and costs nothing after that.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>The options window we cloned from (null = nothing built yet). In standalone mode
    /// this is a SOURCE, not a host: nothing of it is written to.</summary>
    private static UIOptionsWindow? _host;

    /// <summary>
    /// The cloned tab toggle in the game's left-hand option column — FIRE EXIT ONLY. Null in the
    /// normal standalone mode, because that is the row the user asked to be gone.
    /// </summary>
    private static UIMainMenuOption? _toggle;

    /// <summary>Our cloned pane: the standalone VR settings window.</summary>
    private static UISubmenuGOWindow? _window;

    private static bool _probed;
    private static bool _degraded;

    /// <summary>
    /// True once the pane has been detached from the options window and lives on the canvas as a
    /// window of its own. False means the fire exit is live and the pane is still a tab.
    /// </summary>
    internal static bool IsStandalone { get; private set; }

    /// <summary>
    /// Is there a menu to open at all? False until there is BOTH a pane and a content root to build
    /// into — <see cref="VRMenuEntry"/> refuses to inject a row that would open nothing, which is
    /// the "never an empty window" rule enforced at the door rather than apologised for after.
    /// </summary>
    internal static bool CanOpen => !_degraded && _window != null && ContentRoot != null;

    /// <summary>Is the standalone menu on screen right now?</summary>
    internal static bool IsOpen
    {
        get
        {
            if (_window == null)
                return false;
            var win = _window.GetComponent<UIWindow>();
            return win != null && win.IsOpen;
        }
    }

    /// <summary>
    /// What to call when the pane hides, whoever hid it — the pause-menu row's own
    /// <c>Deselect</c>, so a row cannot stay lit over a closed menu. Cleared as it fires; the
    /// listener behind it is added once and never removed.
    /// </summary>
    private static Action? _onHidden;
    private static bool _hiddenHooked;

    /// <summary>FIRE EXIT ONLY: select our tab the next time the options window finishes showing.
    /// Set by <see cref="Open"/> in tab mode, consumed by the shown listener.</summary>
    private static bool _selectOnShow;

    /// <summary>
    /// Root our content is built under — the clone's own child, so the donor's original content
    /// (deactivated, never destroyed) can be restored by simply dropping the clone. Null until the
    /// tab exists.
    /// </summary>
    internal static RectTransform? ContentRoot { get; private set; }

    /// <summary>
    /// The topic column: the mod's own category chooser, pinned down the LEFT edge of the pane,
    /// outside the scroll view so it cannot scroll away.
    ///
    /// <para><b>THIS IS NOW THE MENU'S ONLY RAIL, AND THAT IS THE POINT.</b> User, 2026-09-02:
    /// <i>"Bau es entsprechend so um das die VR Optionstabs ganz links sind."</i> It was already
    /// at x=0 of the pane; what was to the left of it was the GAME's settings tab rail, because the
    /// pane was a tab inside the game's options window. Detaching the pane (see
    /// <c>TryDetach</c>) removes that rail from the screen entirely, which makes this column the
    /// leftmost thing in the menu without moving it a pixel. Its geometry is deliberately
    /// unchanged: the caption fit below is hand-tuned against this exact width.</para>
    ///
    /// <para>It is a COLUMN, not a strip across the top, because that is the shape the game's own
    /// settings screen uses. The strip version had two practical faults — twelve captions across
    /// one width came out cramped and staggered, and it was anchored to the window root rather than
    /// the content pane, so it landed outside the visible panel where nothing could click it.
    /// Earlier still it was the first ROW of the scrolled list, where it scrolled out of sight
    /// almost immediately — the opposite of what a chooser is for.</para>
    /// </summary>
    internal static RectTransform? TabBarRoot { get; private set; }

    /// <summary>Width of that column.</summary>
    private const float TabColumnWidth = 210f;

    /// <summary>
    /// Clear space between the column and the first setting. The cloned tab option's frame art
    /// overhangs its own rect, so padding the content by the column width alone still left the
    /// buttons grazing the row backgrounds.
    /// </summary>
    private const float TabColumnGutter = 28f;

    /// <summary>
    /// Idempotent, cheap after the first success. Called every frame from
    /// <c>WorldUIModule.Update</c> (via TickGuard, so a throw here can never starve input).
    ///
    /// <para>Re-injects when the options window is replaced — it is a <c>Singleton</c> that does not
    /// survive every scene, and a stale reference would leave the tab silently missing for the rest
    /// of the session.</para>
    /// </summary>
    internal static void Tick()
    {
        if (_degraded)
            return;

        // Desktop play stays 100% vanilla: no clone, no tab, nothing written.
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        UIOptionsWindow? host = Singleton<UIOptionsWindow>.IsInitialized
            ? Singleton<UIOptionsWindow>.Instance
            : null;

        if (host == null)
        {
            // Window gone (scene change): drop our references so the next one gets a fresh tab.
            if (_host != null)
            {
                VRLog.Info("WorldUI", "VR options tab: the options window went away (scene change) — "
                                      + "will re-inject into the next one.");
                Forget();
            }
            return;
        }

        // THE GUARD MUST ASK FOR WHAT THIS MODE ACTUALLY BUILDS. It used to require a toggle, and
        // a standalone menu never has one — so the guard could never be satisfied and Inject would
        // have run EVERY FRAME, cloning a fresh pane per frame. The fire exit keeps the old
        // question because in that mode the toggle is exactly what must still exist.
        if (ReferenceEquals(host, _host) && _window != null && (IsStandalone || _toggle != null))
            return;

        Inject(host);
    }

    /// <summary>
    /// Build the menu. Every failure degrades to "no VR menu" with one explanatory line — the
    /// game's own options window has to keep working exactly as it did either way, since it is the
    /// player's only route to the game's own settings.
    ///
    /// <para>Two outcomes, in order of preference. STANDALONE: the pane is detached from the
    /// options window and lives on the canvas as a window of its own; the game's tab rail is never
    /// involved and no game object is written to. FIRE EXIT: detaching was not possible, so the
    /// pane is registered as a tab exactly as it shipped — worse, but reachable, and the log says
    /// which one happened and why.</para>
    /// </summary>
    private static void Inject(UIOptionsWindow host)
    {
        try
        {
            Probe(host);

            if (host.m_Tabs == null || host.m_Tabs.Count == 0)
            {
                Degrade("the options window has no tabs to clone from");
                return;
            }

            UIOptionsWindow.OptionTab? donor = PickDonor(host, out int donorIndex);
            if (donor == null)
            {
                Degrade("no usable donor tab (need one with both a toggle and a tab window)");
                return;
            }

            // Measured while the donor is still sitting in its authored place. Detaching collapses
            // the pane's anchors to the middle of its new parent, so the size has to be an absolute
            // number taken BEFORE the move, or the panel silently re-solves against a different rect.
            Vector2 paneSize = MeasurePaneSize(host);

            UISubmenuGOWindow? window = CloneWindow(donor.TabWindow);
            if (window == null)
            {
                Degrade("the tab window could not be cloned");
                return;
            }

            _host = host;
            _window = window;
            _toggle = null;
            IsStandalone = false;

            // The template comes from a LIVE row, so it has to be stamped while the donor tabs are
            // still intact — and the content is built on first show, not now, because the config
            // registry is not necessarily complete at injection time.
            CaptureRowTemplates(host);
            HookContentBuild(window);
            HookHidden(window);

            IsStandalone = TryDetach(window, host, paneSize);

            if (!IsStandalone)
            {
                // FIRE EXIT — the shipped behaviour, unchanged: a real tab of the game's window.
                UIMainMenuOption? toggle = CloneToggle(donor.OptionToggle);
                if (toggle == null)
                {
                    UnityEngine.Object.Destroy(window.gameObject);
                    Degrade("the pane could not be detached AND the tab toggle could not be cloned");
                    return;
                }

                _toggle = toggle;
                // Join the game's own bookkeeping LAST, so a half-built tab is never reachable.
                host.m_Tabs.Add(new UIOptionsWindow.OptionTab { OptionToggle = toggle, TabWindow = window });
                host.InitializeOption(toggle, window);
                HookShownForTabSelect(host);

                VRLog.Warn("WorldUI",
                    "VR options: the pane could NOT be detached from the options window, so it is "
                    + $"registered as tab #{host.m_Tabs.Count} the way it shipped (cloned from tab "
                    + $"#{donorIndex} '{donor.OptionToggle.name}', label '{Loc.Mod("vr_options")}'). "
                    + "The settings stay reachable, but the game's own tab rail is on screen beside "
                    + "them — which is exactly what this build was meant to remove. The reason is on "
                    + "the warning line above this one.");
                return;
            }

            VRLog.Info("WorldUI",
                $"VR options: STANDALONE menu built (pane cloned from tab #{donorIndex} "
                + $"'{donor.OptionToggle.name}', {paneSize.x:F0}x{paneSize.y:F0} px, label "
                + $"'{Loc.Mod("vr_options")}'). It is NOT a tab: nothing was added to the options "
                + "window's m_Tabs, no toggle was cloned into its category column, and the options "
                + "window is never opened on our behalf — so its tab rail never appears beside the "
                + "VR settings. The mod's own topic column is now the leftmost thing in the menu.");
        }
        catch (Exception e)
        {
            Degrade($"injection threw: {e}");
        }
    }

    /// <summary>
    /// The authored size of a tab pane, in canvas pixels, measured off the LIVE tabs.
    ///
    /// <para>Every tab window is the same rect, so the largest one any tab reports is the answer —
    /// taking the max rather than the first is what makes this immune to the donor being an
    /// INACTIVE tab (<see cref="PickDonor"/> prefers one) whose rect has never been driven.</para>
    ///
    /// <para>The last resort is the options window's own rect, and after that a literal. A literal
    /// is a poor answer, but a pane sized zero is a worse one: it would be an invisible window that
    /// every instrument reports as open.</para>
    /// </summary>
    private static Vector2 MeasurePaneSize(UIOptionsWindow host)
    {
        var best = Vector2.zero;

        if (host.m_Tabs != null)
        {
            for (int i = 0; i < host.m_Tabs.Count; i++)
            {
                UISubmenuGOWindow? tab = host.m_Tabs[i]?.TabWindow;
                if (tab == null || tab.transform is not RectTransform rect)
                    continue;
                Rect r = rect.rect;
                if (r.width * r.height > best.x * best.y)
                    best = new Vector2(r.width, r.height);
            }
        }

        if (best.x >= 200f && best.y >= 200f)
            return best;

        if (host.transform is RectTransform hostRect
            && hostRect.rect.width >= 200f && hostRect.rect.height >= 200f)
        {
            VRLog.Warn("WorldUI", "VR options: no tab pane reported a usable rect "
                + $"({best.x:F0}x{best.y:F0} px) — falling back to the options window's own "
                + $"({hostRect.rect.width:F0}x{hostRect.rect.height:F0} px).");
            return new Vector2(hostRect.rect.width, hostRect.rect.height);
        }

        VRLog.Warn("WorldUI", "VR options: neither a tab pane nor the options window reported a "
            + $"usable rect ({best.x:F0}x{best.y:F0} px) — the standalone menu is sized "
            + "1500x950 px so that it is at least visible and reportable.");
        return new Vector2(1500f, 950f);
    }

    /// <summary>
    /// Lift the cloned pane out of the options window and park it on the canvas.
    ///
    /// <para>THIS IS THE WHOLE OF "eigenständiges Menu". A <c>UISubmenuGOWindow</c> already shows
    /// and hides itself; what made it a tab was WHERE IT SAT. As a descendant of
    /// <c>UIOptionsWindow</c> it inherited that window's <c>CanvasGroup</c> alpha (so it could only
    /// be seen while the options window was up, tab rail and all), it was swept off by the
    /// <c>CloseWindows()</c> that <c>OnShow</c> runs on every open, and <c>ModalFallback</c>
    /// refused to float it because an ancestor window would be floated instead — which is why the
    /// mod's settings have always been drawn inside the options window's panel.</para>
    ///
    /// <para>THE ANCHOR IS CHOSEN, NOT ASSUMED. It must carry no <c>UIWindow</c> at or above it, or
    /// the float would defer to that one and nothing would have changed. The root canvas is tried
    /// first (always active, the full screen rect, and it is where a top-level window belongs);
    /// failing that, the first window-free ancestor of the options window. If neither exists this
    /// returns false and the caller takes the fire exit.</para>
    ///
    /// <para>The rect is then made ABSOLUTE — centre anchors and an explicit size — so the pane
    /// keeps the size it was authored at instead of re-solving against whatever the new parent
    /// happens to be. That is the same shape <c>CanvasConversion</c> gives every window it floats,
    /// so the two agree rather than fight.</para>
    /// </summary>
    private static bool TryDetach(UISubmenuGOWindow window, UIOptionsWindow host, Vector2 paneSize)
    {
        try
        {
            if (window.transform is not RectTransform rect)
            {
                VRLog.Warn("WorldUI", "VR options: the cloned pane has no RectTransform, so it "
                    + "cannot be parked on the canvas.");
                return false;
            }

            Transform? anchor = null;
            string how = "none";

            Canvas? canvas = host.GetComponentInParent<Canvas>();
            Canvas? root = canvas != null ? canvas.rootCanvas : null;
            if (root != null && !HasWindowAtOrAbove(root.transform))
            {
                anchor = root.transform;
                how = $"root canvas '{root.name}'";
            }

            if (anchor == null)
            {
                for (Transform? t = host.transform.parent; t != null; t = t.parent)
                {
                    if (HasWindowAtOrAbove(t))
                        continue;
                    anchor = t;
                    how = $"first window-free ancestor '{t.name}'";
                    break;
                }
            }

            if (anchor == null)
            {
                VRLog.Warn("WorldUI", "VR options: every candidate parent above the options window "
                    + "carries a UIWindow of its own, so a detached pane would still defer to an "
                    + "ancestor float. Staying a tab.");
                return false;
            }

            rect.SetParent(anchor, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = paneSize;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);
            rect.SetAsLastSibling();

            VRLog.Info("WorldUI", $"VR options: pane detached from '{host.name}' and parked on the "
                + $"{how}, centred at {paneSize.x:F0}x{paneSize.y:F0} px. It is now a top-level "
                + "window with no floatable ancestor, so ModalFallback floats it as a panel of its "
                + "own rather than drawing it inside the options window's.");
            return true;
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options: detaching the pane threw ({e.GetType().Name}: "
                + $"{e.Message}) — staying a tab, which is the shipped behaviour.");
            return false;
        }
    }

    /// <summary>Does this transform, or anything above it, carry a <c>UIWindow</c>?</summary>
    private static bool HasWindowAtOrAbove(Transform node)
    {
        for (Transform? t = node; t != null; t = t.parent)
        {
            if (t.GetComponent<UIWindow>() != null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// One listener, added once, that fires whoever asked to be told when the menu closes — the
    /// pause-menu row's <c>Deselect</c>, so the row cannot stay lit over a menu that is gone.
    ///
    /// <para>Guarded, and the callback is cleared BEFORE it runs: a <c>UnityEvent</c> has no
    /// per-listener catch, so a throw here would amputate every listener the game put on the same
    /// event.</para>
    /// </summary>
    private static void HookHidden(UISubmenuGOWindow window)
    {
        if (_hiddenHooked)
            return;
        _hiddenHooked = true;

        window.OnHidden.AddListener(() =>
        {
            Action? pending = _onHidden;
            _onHidden = null;
            if (pending == null)
                return;
            try
            {
                pending();
            }
            catch (Exception e)
            {
                VRLog.Warn("WorldUI", "VR options: the close callback threw "
                    + $"({e.GetType().Name}: {e.Message}); the menu is closed either way.");
            }
        });
    }

    /// <summary>
    /// FIRE EXIT ONLY. Selecting our tab straight after <c>UIOptionsWindow.Show()</c> is a RACE and
    /// always was: the window fades in, and the <c>onShown</c> that lands at the end of that fade
    /// runs <c>OnShow</c> then <c>CloseWindows()</c> then <c>m_ToggleGroup.SetAllTogglesOff()</c>
    /// (UIOptionsWindow.cs :201/:276-281), which turns the tab we just selected back off. That is
    /// what "wenn man es darüber öffnet ist es auch völlig kaputt" was. So in tab mode the
    /// selection waits for the shown edge instead of preceding it.
    /// </summary>
    private static void HookShownForTabSelect(UIOptionsWindow host)
    {
        var win = host.GetComponent<UIWindow>();
        if (win == null)
            return;

        win.onShown.AddListener(() =>
        {
            if (!_selectOnShow)
                return;
            _selectOnShow = false;
            SelectTab();
        });
    }

    /// <summary>
    /// Open the VR settings menu. Returns false when there is nothing to open, and that is the
    /// caller's cue to leave its row un-lit rather than to present an empty window.
    /// </summary>
    /// <param name="onHidden">Called once when the menu closes, however it closes.</param>
    /// <param name="pointAt">FIRE EXIT ONLY: the menu row the game's options window should aim its
    /// vertical pointer at (<c>UIOptionsWindow.Show</c> passes it straight to
    /// <c>VerticalPointerUI.PointAt</c>). Ignored by the standalone window, which has no pointer.
    /// </param>
    internal static bool Open(Action? onHidden, RectTransform? pointAt = null)
    {
        if (!CanOpen || _window == null)
        {
            VRLog.Warn("WorldUI", "VR options: asked to open, but there is no menu to open "
                + $"(degraded={_degraded}, pane={_window != null}, content={ContentRoot != null}). "
                + "Nothing is shown — an empty window would be worse than none.");
            return false;
        }

        _onHidden = onHidden;

        if (IsStandalone)
        {
            _window.Show();
            return true;
        }

        // FIRE EXIT: the menu is a tab, so the game's window has to carry it.
        if (!Singleton<UIOptionsWindow>.IsInitialized)
        {
            _onHidden = null;
            return false;
        }

        _selectOnShow = true;
        Singleton<UIOptionsWindow>.Instance.Show(pointAt, () =>
        {
            Action? pending = _onHidden;
            _onHidden = null;
            pending?.Invoke();
        });
        return true;
    }

    /// <summary>
    /// Close the menu, whichever mode it is in. A no-op when it is not open.
    ///
    /// <para>IT GOES THROUGH <see cref="ModalFallback.CloseFloatedWindow"/>, NOT
    /// <c>UISubmenuGOWindow.Hide()</c>, AND THAT IS NOT A DETAIL. The pane's window id is
    /// <c>OptionsSubmenu</c>, which is in <c>ModalFallback</c>'s parallel-menu family — so its
    /// float is STICKY: the mod deliberately keeps such a window on screen after the GAME hides it,
    /// because that is what lets Options and Multiplayer stand side by side instead of the ESC
    /// menu's single-select ToggleGroup taking one away. Only <c>UserClosing</c> ever drops a
    /// sticky float, and that flag is what <c>CloseFloatedWindow</c> sets. A plain <c>Hide()</c>
    /// here would hide the window in the game's own bookkeeping and leave the panel standing in
    /// front of the player — a menu that had been asked to close and did not.</para>
    /// </summary>
    internal static void Close()
    {
        try
        {
            if (IsStandalone)
            {
                if (_window == null)
                    return;
                var win = _window.GetComponent<UIWindow>();
                if (win != null)
                    ModalFallback.CloseFloatedWindow(win);
                else
                    _window.Hide();
                return;
            }

            _selectOnShow = false;
            if (Singleton<UIOptionsWindow>.IsInitialized)
                Singleton<UIOptionsWindow>.Instance.Hide();
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options: closing threw ({e.GetType().Name}: {e.Message}).");
        }
    }

    /// <summary>
    /// A SCROLLING tab is the only usable donor, and the probe is what settled that. The window
    /// comes in two authored shapes: General/Audio/Combat Log put their rows straight into a
    /// <c>VerticalLayoutGroupExtended</c> and simply run off the bottom when there are more of them
    /// than fit, while Video/Controls/Perfomance wrap theirs in an <c>ExtendedScrollRect</c> with a
    /// masked viewport and the game's own scrollbar art. The mod has far more settings than fit on
    /// one screen, so cloning a non-scrolling tab would produce a list whose lower half is simply
    /// unreachable — and it would look wrong besides, since the game's own long lists all scroll.
    ///
    /// <para>Within the scrolling group an INACTIVE tab is preferred, which is the opposite of the
    /// first version and the fix for the game's own keybindings appearing above the mod's settings.
    /// The Controls tab POPULATES ITSELF: cloning it cloned whatever rebuilds that list, so the
    /// donor rows were duly deactivated and then promptly rebuilt into the very content transform
    /// the mod writes into. A tab the game hides on this platform (Perfomance, console-only) has
    /// nothing maintaining it and nothing the player can miss.</para>
    /// </summary>
    private static UIOptionsWindow.OptionTab? PickDonor(UIOptionsWindow host, out int index)
    {
        index = -1;
        UIOptionsWindow.OptionTab? scrollingActive = null, plain = null;
        int scrollingActiveIndex = -1, plainIndex = -1;

        for (int i = 0; i < host.m_Tabs.Count; i++)
        {
            UIOptionsWindow.OptionTab tab = host.m_Tabs[i];
            if (tab?.OptionToggle == null || tab.TabWindow == null)
                continue;

            bool active = tab.OptionToggle.gameObject.activeSelf;

            if (tab.TabWindow.GetComponentInChildren<ScrollRect>(true) != null)
            {
                if (!active)
                {
                    index = i;
                    return tab;
                }

                if (scrollingActive == null)
                {
                    scrollingActive = tab;
                    scrollingActiveIndex = i;
                }
                continue;
            }

            if (plain == null && active)
            {
                plain = tab;
                plainIndex = i;
            }
        }

        if (scrollingActive != null)
        {
            VRLog.Warn("WorldUI", "VR options tab: only ACTIVE scrolling donors exist — cloning one "
                                  + "the game still maintains. If its own rows reappear above the "
                                  + "mod's settings, that is why; the per-show sweep hides them.");
            index = scrollingActiveIndex;
            return scrollingActive;
        }

        if (plain != null)
        {
            VRLog.Warn("WorldUI", "VR options tab: no scrolling donor tab found — falling back to a "
                                  + "plain one. The settings list will not scroll, so anything past "
                                  + "the bottom of the panel will be out of reach.");
        }

        index = plainIndex;
        return plain;
    }

    /// <summary>
    /// Clone the tab toggle next to its donor, re-label it, and make sure nothing re-labels it
    /// back. The game drives option captions through <c>TextLocalizedListener</c>, which rewrites
    /// the text on every language change — left alive on the clone it would silently replace our
    /// caption with the donor's the first time the player switches language.
    /// </summary>
    private static UIMainMenuOption? CloneToggle(UIMainMenuOption donor)
    {
        var clone = UnityEngine.Object.Instantiate(donor.gameObject, donor.transform.parent)
            .GetComponent<UIMainMenuOption>();
        if (clone == null)
            return null;

        clone.name = "GloomhavenVR.OptionsTab";
        clone.transform.SetAsLastSibling();
        clone.gameObject.SetActive(true);

        foreach (TextLocalizedListener listener in clone.GetComponentsInChildren<TextLocalizedListener>(true))
            UnityEngine.Object.Destroy(listener);

        SetCaption(clone, Loc.Mod("vr_options"));

        // A donor that happened to be non-interactable (Perfomance outside the main menu) would
        // hand us its locked state along with its wiring.
        clone.IsInteractable = true;
        return clone;
    }

    /// <summary>
    /// Caption via the option's own <c>text</c> field when it has one, else the first TMP label in
    /// the clone — the field is the authored one, the sweep is what keeps this working if a game
    /// update re-authors the row.
    /// </summary>
    internal static void SetCaption(UIMainMenuOption option, string caption)
    {
        TextMeshProUGUI? label = option.text;
        if (label == null)
            label = option.GetComponentInChildren<TextMeshProUGUI>(true);

        if (label == null)
        {
            VRLog.Warn("WorldUI", "VR options tab: the cloned toggle has no TMP label — the tab will "
                                  + "carry the donor's caption. Harmless, but it will read wrong.");
            return;
        }

        label.text = caption;
    }

    /// <summary>
    /// Clone the tab window and hand back a content root INSIDE the donor's own scroll view, so the
    /// masked viewport, the scrollbar art and the wheel/drag behaviour are the game's rather than
    /// ours. Only the donor's ROWS are deactivated — never destroyed, and never the scroll
    /// machinery around them.
    ///
    /// <para>Deactivating rather than destroying keeps the clone's serialized references intact: the
    /// window's own components already ran their <c>Awake</c> and hold pointers into this hierarchy,
    /// and destroying children would leave them pointing at dead objects.</para>
    /// </summary>
    private static UISubmenuGOWindow? CloneWindow(UISubmenuGOWindow donor)
    {
        var clone = UnityEngine.Object.Instantiate(donor.gameObject, donor.transform.parent)
            .GetComponent<UISubmenuGOWindow>();
        if (clone == null)
            return null;

        clone.name = "GloomhavenVR.OptionsTabWindow";

        ScrollRect? scroll = clone.GetComponentInChildren<ScrollRect>(true);
        RectTransform holder = scroll != null && scroll.content != null
            ? scroll.content
            : (RectTransform)clone.transform;

        var deactivated = new List<string>();
        for (int i = 0; i < holder.childCount; i++)
        {
            Transform child = holder.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;
            child.gameObject.SetActive(false);
            deactivated.Add(child.name);
        }

        ContentRoot = BuildContentRoot(holder, scrolled: scroll != null && scroll.content != null);
        TabBarRoot = BuildTabBar(scroll);
        clone.gameObject.SetActive(false);

        LogCloneHierarchy(clone);

        VRLog.Info("WorldUI",
            $"VR options tab: window cloned from '{donor.name}'. "
            + (scroll != null
                ? $"Content sits inside the donor's own scroll view ('{holder.name}'), so the viewport "
                  + "mask and scrollbar are the game's. "
                : "NO scroll view on this donor — the list cannot scroll. ")
            + $"Donor rows deactivated ({deactivated.Count}: {string.Join(", ", deactivated.ToArray())}); "
            + "nothing was destroyed, so dropping the clone restores everything.");
        return clone;
    }

    /// <summary>
    /// Our own root under the donor's holder. Inside a scroll view it must SIZE ITSELF to its rows
    /// (a stretched rect would report a fixed height and the scroll range would stay zero no matter
    /// how many settings are added); outside one it simply fills the window.
    /// </summary>
    private static RectTransform BuildContentRoot(RectTransform holder, bool scrolled)
    {
        var root = (RectTransform)new GameObject("GloomhavenVR.Content", typeof(RectTransform)).transform;
        root.SetParent(holder, worldPositionStays: false);

        if (scrolled)
        {
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.offsetMin = new Vector2(0f, 0f);
            root.offsetMax = new Vector2(0f, 0f);

            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            // The gap the sub-tab column sits in. Padding OUR content is the one way to reserve it
            // that no animation of the game's can undo — see BuildTabBar.
            layout.padding = new RectOffset(Mathf.RoundToInt(TabColumnWidth + TabColumnGutter), 0, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }
        else
        {
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
        }

        return root;
    }

    /// <summary>
    /// Pin the sub-tab strip above the scroll area and shorten the scroll area by its height.
    ///
    /// <para>The ScrollRect sits ON the tab's "Main Area" (the probe), so that transform IS the
    /// scrolling region: moving its top edge down and putting the strip in the gap needs no
    /// reparenting of anything the game owns.</para>
    /// </summary>
    private static RectTransform? BuildTabBar(ScrollRect? scroll)
    {
        if (scroll == null)
            return null;

        // NOTHING THE GAME OWNS IS MOVED. The first version insetting the pane's left edge to make
        // room, and the column then drew straight over the settings: "Main Area" carries a
        // LeanTweenGUIAnimator that tweens the pane every time the tab is shown, and that tween
        // wrote the offsets back. Room is made instead by PADDING THE MOD'S OWN CONTENT (see
        // BuildContentRoot), which nothing else touches.
        //
        // The column is parented to the pane itself so it inherits the pane's exact rect and rides
        // that same show animation. It is not under the Viewport, so the scroll mask does not clip
        // it, and it is created after the Viewport so it draws over the padding strip rather than
        // under it.
        var area = (RectTransform)scroll.transform;

        var column = (RectTransform)new GameObject("GloomhavenVR.SubTabs", typeof(RectTransform)).transform;
        column.SetParent(area, worldPositionStays: false);
        column.anchorMin = new Vector2(0f, 0f);
        column.anchorMax = new Vector2(0f, 1f);
        column.pivot = new Vector2(0f, 0.5f);
        column.sizeDelta = new Vector2(TabColumnWidth, 0f);
        column.anchoredPosition = Vector2.zero;

        var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 2f;
        layout.padding = new RectOffset(0, 6, 0, 0);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return column;
    }

    /// <summary>
    /// Dump the finished clone once. The player reports "control options at the very top that have
    /// nothing to do with VR", and the donor is the Controls tab — so something authored is still
    /// showing. Guessing at which node it is from a screenshot is how the last two rounds were
    /// spent; this says it outright.
    /// </summary>
    private static void LogCloneHierarchy(UISubmenuGOWindow clone)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("VR options tab CLONE — '").Append(clone.name).Append('\'');
            DescribeChildren(sb, clone.transform, "    ", depth: 3);
            VRLog.Info("WorldUI", sb.ToString());
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: clone dump threw ({e.Message}).");
        }
    }

    /// <summary>
    /// One-shot dump of what the options window actually looks like. This exists because the tab
    /// contents are authored prefab data that no decompiled source can show: the alternative to
    /// logging it once is guessing at it repeatedly.
    /// </summary>
    private static void Probe(UIOptionsWindow host)
    {
        if (_probed)
            return;
        _probed = true;

        try
        {
            var sb = new StringBuilder();
            sb.Append("VR options tab PROBE — options window '").Append(host.name).Append("', ");
            sb.Append(host.m_Tabs?.Count ?? 0).Append(" tab(s), toggle group ");
            sb.Append(host.m_ToggleGroup == null ? "MISSING" : host.m_ToggleGroup.name).Append('.');

            if (host.m_Tabs != null)
            {
                for (int i = 0; i < host.m_Tabs.Count; i++)
                {
                    UIOptionsWindow.OptionTab tab = host.m_Tabs[i];
                    sb.Append("\n  [").Append(i).Append("] toggle=");
                    sb.Append(tab?.OptionToggle == null ? "null" : tab.OptionToggle.name);
                    if (tab?.OptionToggle != null)
                        sb.Append(tab.OptionToggle.gameObject.activeSelf ? " (active)" : " (INACTIVE)");
                    sb.Append(" window=");
                    sb.Append(tab?.TabWindow == null ? "null" : tab.TabWindow.name);

                    if (tab?.TabWindow != null)
                    {
                        var w = tab.TabWindow.GetComponent<UIWindow>();
                        if (w != null)
                            sb.Append(" id=").Append(w.ID);
                        DescribeChildren(sb, tab.TabWindow.transform, "      ", depth: 2);
                    }
                }
            }

            ProbeRowArchetypes(sb, host);
            VRLog.Info("WorldUI", sb.ToString());
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: probe threw ({e.Message}) — injection continues.");
        }
    }

    /// <summary>
    /// Dump ONE real example of each control shape the mod needs to reproduce — a toggle row, a
    /// slider row and a dropdown row — with their full sub-tree.
    ///
    /// <para>These are the rows the content stage clones, and cloning is only safe if the anatomy is
    /// known: which child carries the caption, which carries the control, and which game component
    /// is bound to the setting behind it and therefore has to go. Searching by COMPONENT TYPE
    /// rather than by name means this keeps finding them if a game update renames the rows.</para>
    /// </summary>
    private static void ProbeRowArchetypes(StringBuilder sb, UIOptionsWindow host)
    {
        Transform? toggleRow = null, sliderRow = null, dropdownRow = null;

        for (int i = 0; i < host.m_Tabs.Count; i++)
        {
            UISubmenuGOWindow? window = host.m_Tabs[i]?.TabWindow;
            if (window == null)
                continue;

            toggleRow ??= FindRowWith<Toggle>(window.transform);
            sliderRow ??= FindRowWith<Slider>(window.transform);
            dropdownRow ??= FindRowWith<TMP_Dropdown>(window.transform);
        }

        AppendArchetype(sb, "TOGGLE row", toggleRow);
        AppendArchetype(sb, "SLIDER row", sliderRow);
        AppendArchetype(sb, "DROPDOWN row", dropdownRow);
    }

    /// <summary>
    /// The ROW is the layout item, not the widget: walk up from the control to the child that sits
    /// directly under a layout group, because that is the unit the content stage instantiates.
    /// </summary>
    private static Transform? FindRowWith<T>(Transform root) where T : Component
    {
        T[] found = root.GetComponentsInChildren<T>(true);
        if (found.Length == 0)
            return null;

        Transform node = found[0].transform;
        while (node.parent != null && node.parent != root)
        {
            if (node.GetComponent<LayoutElement>() != null)
                return node;
            node = node.parent;
        }
        return found[0].transform;
    }

    private static void AppendArchetype(StringBuilder sb, string what, Transform? row)
    {
        sb.Append("\n  ").Append(what).Append(": ");
        if (row == null)
        {
            sb.Append("none found in any tab.");
            return;
        }

        sb.Append('\'').Append(row.name).Append("' under '")
          .Append(row.parent == null ? "?" : row.parent.name).Append('\'');
        Component[] own = row.GetComponents<Component>();
        sb.Append("  [");
        for (int c = 0; c < own.Length; c++)
        {
            if (c > 0)
                sb.Append(", ");
            sb.Append(own[c] == null ? "<missing>" : own[c].GetType().Name);
        }
        sb.Append(']');
        DescribeChildren(sb, row, "        ", depth: 3);
    }

    /// <summary>Component-annotated child listing, depth-limited: this is a diagnostic, not a dump.</summary>
    private static void DescribeChildren(StringBuilder sb, Transform parent, string indent, int depth)
    {
        if (depth <= 0)
            return;

        for (int i = 0; i < parent.childCount && i < 12; i++)
        {
            Transform child = parent.GetChild(i);
            sb.Append('\n').Append(indent).Append(child.name);
            if (!child.gameObject.activeSelf)
                sb.Append(" (inactive)");

            Component[] components = child.GetComponents<Component>();
            sb.Append("  [");
            for (int c = 0; c < components.Length; c++)
            {
                if (c > 0)
                    sb.Append(", ");
                sb.Append(components[c] == null ? "<missing>" : components[c].GetType().Name);
            }
            sb.Append(']');

            DescribeChildren(sb, child, indent + "  ", depth - 1);
        }

        if (parent.childCount > 12)
            sb.Append('\n').Append(indent).Append("… ").Append(parent.childCount - 12).Append(" more");
    }

    /// <summary>Log the first failure, then stay silent for the rest of the session.</summary>
    private static void Degrade(string reason)
    {
        if (_degraded)
            return;
        _degraded = true;
        Forget();
        VRLog.Warn("WorldUI", $"VR options: no VR settings menu this session — {reason}. The game's "
                              + "own options window is untouched and still opens normally, and every "
                              + "VR setting remains editable in BepInEx/config/dev.gloomhavenvr*.cfg. "
                              + "The pause-menu VR row is not injected either, because a row that "
                              + "opens nothing is worse than no row.");
    }

    /// <summary>
    /// Drop references without touching anything (the objects are already gone).
    ///
    /// <para>THE ONE-SHOT HOOK FLAGS BELONG HERE TOO, and leaving them out was a real defect: they
    /// guard "add this listener to THE window", and after a scene change the window is a different
    /// object. A <c>_showHooked</c> that survived the old pane meant the NEW pane's
    /// <c>OnShow</c> was never subscribed, so its content was never built — an options menu that
    /// opened onto an empty panel for the rest of the session.</para>
    /// </summary>
    private static void Forget()
    {
        _host = null;
        _toggle = null;
        _window = null;
        ContentRoot = null;
        TabBarRoot = null;
        IsStandalone = false;
        _onHidden = null;
        _hiddenHooked = false;
        _selectOnShow = false;
        _showHooked = false;
    }

    /// <summary>FIRE EXIT ONLY: select the VR tab, if there is one. A no-op in the normal
    /// standalone mode, where there is no tab and nothing to select — the menu is its own
    /// window and <see cref="Open"/> shows it directly.</summary>
    internal static void SelectTab()
    {
        if (_toggle == null)
            return;
        try
        {
            _toggle.Select();
        }
        catch (Exception ex)
        {
            Core.VRLog.Warn("WorldUI", "Could not select the VR tab after opening the options "
                + $"window from the pause menu ({ex.GetType().Name}); the window is open on its "
                + "own tab and the VR tab is one click away.");
        }
    }

    /// <summary>
    /// Destroy the pane and — in fire-exit mode only — take our entry back out of the options
    /// window's <c>m_Tabs</c> and drop the toggle: the exact inverse of <see cref="Inject"/>.
    /// Called from <c>WorldUIModule.OnDestroy</c>; safe when nothing was ever built.
    /// </summary>
    internal static void Shutdown()
    {
        try
        {
            if (_host != null && _host.m_Tabs != null && _toggle != null)
            {
                for (int i = _host.m_Tabs.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(_host.m_Tabs[i]?.OptionToggle, _toggle))
                        _host.m_Tabs.RemoveAt(i);
                }
            }

            if (_toggle != null)
                UnityEngine.Object.Destroy(_toggle.gameObject);
            if (_window != null)
                UnityEngine.Object.Destroy(_window.gameObject);
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: teardown threw ({e.Message}).");
        }
        finally
        {
            Forget();
        }
    }
}
