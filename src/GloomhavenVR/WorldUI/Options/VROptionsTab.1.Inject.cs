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
/// <para>THE MODAL PATH, AND THE ID THAT IS NOT WHAT ModBuild 335 CLAIMED. That build's notes said
/// the pane "carries <c>UIWindowID.OptionsSubmenu</c>, which is already in ModalFallback's family".
/// THE HARDWARE LOG SAYS OTHERWISE, on every single line: <c>UIWindow SHOWN:
/// 'GloomhavenVR.OptionsTabWindow' (ID None, …)</c>. The donor tab pane's id is <c>None</c>, so the
/// clone's is too, and every consequence that was assumed from membership of
/// <c>NonBlockingMenus</c> simply does not hold. What actually follows, read off the shipped code
/// rather than assumed: the float is NOT sticky (<c>ModalFallback.8.Convert.cs</c>:657 —
/// <c>Sticky = NonBlockingMenus.Contains(window.ID) || MapRoomParallel(window)</c>), and the window
/// IS treated as blocking (<c>ModalFallback.7.Close.cs</c>:578 <c>IsBlockingWindow</c>), which is
/// why the hardware log reads <c>MODAL FALLBACK ASSERTED … blocking=True</c> for this window while
/// the game's own options window reads <c>blocking=False</c>. Neither of those is a dependency
/// BETWEEN the two windows, so neither is fixed here; the blocking difference is a ModalFallback
/// question and is written up in the ModBuild 336 report.</para>
///
/// <para><b>THE ID IS DELIBERATELY LEFT AT <c>None</c>, and that is now load-bearing.</b> Giving the
/// clone <c>UIWindowID.OptionsSubmenu</c> would look like a tidy-up and would CREATE couplings:
/// <c>UIWindow.GetWindow(id)</c>/<c>GetWindowsByID</c> would start returning the mod's window for
/// the game's id, <c>UIWindowManager</c>'s <c>extraSkipHideWindows</c> / <c>extraSkipShowWindows</c>
/// sets are keyed by id, and — worst — <c>ModalFallback.ResetEscMenuToggleGroup</c> fires for
/// exactly Options/OptionsSubmenu/ViceOptionsSubmenu/CompendiumPanel, so our X button would run
/// <c>ESCMenu.toggleGroup.SetAllTogglesOff()</c> and take the game's options window down with it.
/// An id shared with the game is a shared key, and shared keys are what this build removes.</para>
///
/// <para>Detached, it is a top-level window with no floatable ancestor, so it is floated as its OWN
/// panel — with the grab bar and the close button every floated modal gets. This class still adds
/// no rendering, no placement and no input path.</para>
///
/// <para><b>THE THREE SHARED STACKS THE CLONE WAS STILL STANDING IN (ModBuild 336).</b> User, after
/// the 335 hardware round: <i>"Die VR Optionen und normale Optionen sind irgendwie immer noch
/// abhängig … Aktuell schließt sich das eine Fenster, wenn das andere öffnet … Entferne hier
/// jegliche Abhängigkeit von beiden Fenstern."</i> Detaching the pane from the options window was
/// necessary and not sufficient — the two windows were still peers in three registries the game
/// keeps ONE of:</para>
/// <list type="number">
/// <item><description>THE PAUSE MENU'S <c>ToggleGroup</c> — and this is the one that produced the
/// symptom. <see cref="VRMenuEntry"/> clones the game's own Optionen row next to itself, so the
/// clone's <c>ExtendedToggle</c> came with the donor's <c>group</c> reference. <c>ESCMenu.cs</c>
/// :134-146 wires that row's DEselect to <c>Singleton&lt;UIOptionsWindow&gt;.Instance.Hide()</c>,
/// so picking VR Optionen turned the Optionen row off and hid the game's window, and picking
/// Optionen turned OUR row off and ran <see cref="Close"/>. That is the log's perfect alternation
/// — hide one, show the other three lines later — and it is fixed in <see cref="VRMenuEntry"/> by
/// taking the cloned row's toggle OUT of the group.</description></item>
/// <item><description>THE ESCAPABLE LIST. <c>UIWindow.Start</c> :371 and <c>UIWindow.Show</c> :484
/// enrol any window whose <c>escapeKeyAction</c> is not <c>None</c> in
/// <c>UIWindowManager</c>'s single <c>escapableListeners</c> list, where one ESC press walks the
/// whole list and stops at the first window that answers it (<c>UIWindowManager.cs</c> :83-115) and
/// <c>HideOrShowWindows</c> / <c>ForceHideWindows</c> hide every open member at once (:36-80,
/// :118-131 — <c>Choreographer.cs</c>:14651 calls the first of those). See
/// <see cref="LeaveSharedStacks"/>.</description></item>
/// <item><description>THE CONTROLLER INPUT AREA STACK. The donor tab brought a
/// <c>ControllerInputAreaLocal</c> called "Options Display", and <c>UISubmenuGOWindow.Show()</c>
/// registers it with <c>ControllerInputAreaManager</c> — which holds exactly ONE
/// <c>m_FocusArea</c> and one <c>m_StackedAreas</c> for the whole game
/// (<c>ControllerInputAreaManager.cs</c> :120-171). The game's options window drives its own
/// navigation off the same manager (<c>UIOptionsWindow.Awake</c> :85-86 binds "Options"'s
/// focus/unfocus to <c>EnableNavigation</c>/<c>DisableNavigation</c>), so with a gamepad in use one
/// window focusing switches the other one's navigation off. See
/// <see cref="LeaveInputAreaStack"/>.</description></item>
/// </list>
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

    // Failed injection is temporary. The first two retries wait two seconds; repeated failures
    // back off to thirty seconds. A different native host is always eligible immediately.
    // User ruling 2026-09-20 supersedes the old permanent third-window refusal: a transient
    // prefab/hierarchy failure must never consume all future access to the VR settings.
    private static bool _degraded;
    private static UIOptionsWindow? _failedHost;
    private static int _consecutiveInjectionFailures;
    private static float _nextInjectionRetry;
    private const int MaxConsecutiveInjectionFailures = 3;

    /// <summary>
    /// The clone's own <c>ControllerInputAreaLocal</c> — the component <c>UISubmenuGOWindow</c>
    /// registers with <c>ControllerInputAreaManager</c> on every <c>Show()</c>. Cached at injection
    /// so <see cref="LeaveInputAreaStack"/> does not have to search for it on the open edge.
    /// Null in fire-exit mode, where the game's own machinery still owns the pane.
    /// </summary>
    private static ControllerInputAreaLocal? _inputArea;

    /// <summary>One-shot log flags for the two decouplings — they are verdicts, not chatter.</summary>
    private static bool _loggedEscapableLeave;
    private static bool _loggedAreaLeave;

    /// <summary>The failure twin of <see cref="_loggedAreaLeave"/> (2026-09 refactor, F-75).
    /// <c>LeaveInputAreaStack</c> runs on EVERY open, so its catch could repeat once per open; the
    /// success line is one-shot and the failure line must be bounded the same way before it can be
    /// promoted to a printing tier.</summary>
    private static bool _loggedAreaLeaveFailed;

    /// <summary>
    /// One-shot log flag for <see cref="ShowStandalone"/>'s open-edge remedy. Cleared in
    /// <see cref="Forget"/> with the other per-pane state: the remedy is a property of THIS clone's
    /// lifetime (it has never been through <c>UIWindow.Start()</c>), so a new pane must be able to
    /// report it again rather than inherit the old one's silence.
    /// </summary>
    private static bool _loggedShowRemedy;

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

    /// <summary>
    /// The canvas the pane is actually drawn by — DIAGNOSTIC ONLY, read once per session by
    /// <see cref="MenuExclusivity.NoteYielded"/>.
    ///
    /// <para>It exists because the main-menu overlap defect is a SORTING-ORDER fact and nothing
    /// else: in <c>VRMode.Menu2D</c> every window is composited onto the one flat screen, so the
    /// number this returns against the number the opened window returns is the whole explanation
    /// for "the VR options lie on top and nothing can be controlled". Measuring it at the moment of
    /// the verdict is the difference between a log line that proves the cause and one that repeats
    /// an earlier build's census.</para>
    /// </summary>
    internal static Canvas? PaneCanvas
        => _window != null ? _window.GetComponentInParent<Canvas>() : null;

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
        // Desktop play stays 100% vanilla: no clone, no tab, nothing written.
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        UIOptionsWindow? host = Singleton<UIOptionsWindow>.IsInitialized
            ? Singleton<UIOptionsWindow>.Instance
            : null;

        if (host == null)
        {
            // Window gone (scene change): drop our references so the next one gets a fresh tab.
            if (!ReferenceEquals(_host, null))
            {
                VRLog.Info("WorldUI", "VR options tab: the options window went away (scene change) — "
                                      + "will re-inject into the next one.");
                // The detached clone lives on PersistentUI and need not die with its source.
                Shutdown();
            }
            return;
        }

        if (!InjectionRetryReady(host))
            return;

        // THE GUARD MUST ASK FOR WHAT THIS MODE ACTUALLY BUILDS. It used to require a toggle, and
        // a standalone menu never has one — so the guard could never be satisfied and Inject would
        // have run EVERY FRAME, cloning a fresh pane per frame. The fire exit keeps the old
        // question because in that mode the toggle is exactly what must still exist.
        if (ReferenceEquals(host, _host) && _window != null && (IsStandalone || _toggle != null))
            return;

        if (!ReferenceEquals(host, _host) && _window != null)
            Shutdown();
        Inject(host);
    }

    /// <summary>Retry transient injection failures without a per-frame clone/error loop.</summary>
    private static bool InjectionRetryReady(UIOptionsWindow host)
    {
        if (ReferenceEquals(host, _failedHost) && Time.unscaledTime < _nextInjectionRetry)
            return false;
        _failedHost = null;
        _degraded = false;
        return true;
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
                Degrade(host, "the options window has no tabs to clone from");
                return;
            }

            UIOptionsWindow.OptionTab? donor = PickDonor(host, out int donorIndex);
            if (donor == null)
            {
                Degrade(host, "no usable donor tab (need one with both a toggle and a tab window)");
                return;
            }

            // Measured while the donor is still sitting in its authored place. Detaching collapses
            // the pane's anchors to the middle of its new parent, so the size has to be an absolute
            // number taken BEFORE the move, or the panel silently re-solves against a different rect.
            Vector2 paneSize = MeasurePaneSize(host);

            UISubmenuGOWindow? window = CloneWindow(donor.TabWindow);
            if (window == null)
            {
                Degrade(host, "the tab window could not be cloned");
                return;
            }

            _host = host;
            _window = window;
            _toggle = null;
            IsStandalone = false;

            // MODAL REGISTRATION (ModBuild 341). The pane's UIWindowID is None and stays None (see
            // this class's "THE ID IS DELIBERATELY LEFT AT None" paragraph), so ModalFallback's
            // family tests — which are all id-keyed — classified it as a blocking GAME modal and
            // gated the card fan off. It is told about the window instead. Registered HERE, before
            // the detach can fail, so the classification is right in the fire-exit mode too;
            // released in Forget(), which every teardown path runs through.
            MenuWindowFamily.RegisterModMenu(window.GetComponent<UIWindow>());

            // The template comes from a LIVE row, so it has to be stamped while the donor tabs are
            // still intact — and the content is built on first show, not now, because the config
            // registry is not necessarily complete at injection time.
            CaptureRowTemplates(host);
            HookContentBuild(window);
            HookHidden(window);

            IsStandalone = TryDetach(window, host, paneSize);

            if (IsStandalone)
                LeaveSharedStacks(window);

            if (!IsStandalone)
            {
                // FIRE EXIT — the shipped behaviour, unchanged: a real tab of the game's window.
                UIMainMenuOption? toggle = CloneToggle(donor.OptionToggle);
                if (toggle == null)
                {
                    UnityEngine.Object.Destroy(window.gameObject);
                    Degrade(host, "the pane could not be detached AND the tab toggle could not be cloned");
                    return;
                }

                _toggle = toggle;
                // Join the game's own bookkeeping LAST, so a half-built tab is never reachable.
                host.m_Tabs.Add(new UIOptionsWindow.OptionTab { OptionToggle = toggle, TabWindow = window });
                host.InitializeOption(toggle, window);
                HookShownForTabSelect(host);

                // A REACHABLE MENU IS A SUCCESS, worse mode or not: the run of failures the
                // process-wide latch counts is broken here as much as on the standalone path.
                _consecutiveInjectionFailures = 0;

                VRLog.Warn("WorldUI",
                    "VR options: the pane could NOT be detached from the options window, so it is "
                    + $"registered as tab #{host.m_Tabs.Count} the way it shipped (cloned from tab "
                    + $"#{donorIndex} '{donor.OptionToggle.name}', label '{Loc.Mod("vr_options")}'). "
                    + "The settings stay reachable, but the game's own tab rail is on screen beside "
                    + "them — which is exactly what this build was meant to remove. The reason is on "
                    + "the warning line above this one.");
                return;
            }

            _consecutiveInjectionFailures = 0;

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
            Degrade(host, $"injection threw: {e}");
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
    /// STANDALONE ONLY: take the detached pane out of the registries the game keeps ONE of, so
    /// nothing that happens to the game's options window can reach ours and nothing that happens to
    /// ours can reach the game's. Run once, immediately after a successful detach.
    ///
    /// <para><b>THE ESCAPABLE LIST.</b> <c>UIWindowManager</c> holds a single
    /// <c>escapableListeners</c> list. A window joins it from <c>UIWindow.Start</c> (:371) and again
    /// from every <c>UIWindow.Show</c> (:484), gated on <c>escapeKeyAction != None</c> in both
    /// places, and the <c>escapeKeyAction</c> SETTER itself unregisters when the new value is
    /// <c>None</c> (:196-207). So one assignment takes us out and keeps us out — there is no other
    /// re-entry path. Membership mattered three ways: <c>UIWindowManager.Escape()</c> walks the
    /// shared list and stops at the first window that answers, so a single ESC press was a race
    /// between the two settings windows; the same method then ALSO escapes the first
    /// <c>EscapeKeyAction.Toggle</c> window it can find (:100-110), which is the pause menu; and
    /// <c>HideOrShowWindows</c>/<c>ForceHideWindows</c> hide every open member in one sweep, both
    /// of them skipping <c>escapeKeyAction == None</c> outright (:57, :121).</para>
    ///
    /// <para><b>THIS DOES NOT COST US THE CLOSE, AND THAT WAS CHECKED RATHER THAN HOPED.</b>
    /// <c>ModalFallback.CloseFloatedWindow</c> (ModalFallback.7.Close.cs:130-145) runs
    /// <c>Escape()</c> for whatever the window's own policy is and then FORCES <c>Hide()</c> if the
    /// window is still open — precisely because <c>Escape()</c> returning false proves nothing. With
    /// <c>None</c>, <c>UIWindow.Escape()</c> returns false without hiding (UIWindow.cs:713-716) and
    /// the forced <c>Hide()</c> closes the window, so the corner X, the pause-menu row and the modal
    /// escape chord all still work. The one thing that changes is that the keyboard ESC key no
    /// longer reaches this window — which is the whole point, because the ESC key reaching it was
    /// one of the shared stacks.</para>
    ///
    /// <para><b>WHY THE <c>UIWindow</c> COMPONENT STAYS.</b> Stripping it would look like the
    /// thorough answer and would cost the entire VR presentation: <c>ModalFallback</c> keys its
    /// float, its grab bar and its close X on <c>UIWindow</c>, <c>WindowMaterialise</c> subscribes
    /// to <c>VREvents.WindowVisibility</c> which is raised from a Harmony postfix on
    /// <c>UIWindow.EvaluateAndTransitionToVisualState</c>, and <c>UISubmenuGOWindow</c> itself is
    /// <c>[RequireComponent(typeof(UIWindow))]</c> and dereferences it in <c>Awake</c>, <c>Show</c>
    /// and <c>Hide</c>. The component is not the coupling — its REGISTRATIONS were, and those are
    /// what this removes.</para>
    /// </summary>
    private static void LeaveSharedStacks(UISubmenuGOWindow window)
    {
        try
        {
            _inputArea = window.GetComponent<ControllerInputAreaLocal>();

            var win = window.GetComponent<UIWindow>();
            if (win == null)
            {
                VRLog.Warn("WorldUI", "VR options: the detached pane has no UIWindow, so there was "
                    + "no escapable registration to leave. Nothing else about the menu depends on "
                    + "this step.");
                return;
            }

            UIWindow.EscapeKeyAction before = win.escapeKeyAction;
            win.escapeKeyAction = UIWindow.EscapeKeyAction.None;
            // The setter only acts on a CHANGE (UIWindow.cs:196), so a window that was already
            // None never got the unregister call. Ask for it explicitly rather than assume.
            UIWindowManager.UnregisterEscapable(win);

            if (_loggedEscapableLeave)
                return;
            _loggedEscapableLeave = true;
            // HW-VERIFY: this is the verdict the ModBuild 336 round is waiting on — it must stay at
            // a tier the default log level prints, or the round comes back unable to say whether the
            // two windows were separated at all.
            VRLog.Note("WorldUI",
                $"VR options: the standalone pane has LEFT the game's escapable stack (escapeKeyAction "
                + $"{before} -> None, then an explicit UIWindowManager.UnregisterEscapable). It is no "
                + "longer a peer of 'UI Options Window_unified' in UIWindowManager.escapableListeners, "
                + "so one ESC press can no longer pick between them, and HideOrShowWindows / "
                + "ForceHideWindows (which both skip escapeKeyAction None) can no longer sweep it away "
                + "with the rest. PROOF IN THE NEXT LOG: there must be NO further "
                + "'[GUI] Added escapable GloomhavenVR.OptionsTabWindow' line, and the X button's own "
                + "line must now read 'closed via UIWindow.Hide()' instead of "
                + "'closed via UIWindow.Escape()'. WHAT IT COSTS: the keyboard ESC key no longer closes "
                + "the VR settings; the corner X, the pause-menu row and the long-hold escape chord all "
                + "still do, and opening the settings is untouched.");
        }
        catch (Exception e)
        {
            // HW-VERIFY (2026-09 refactor, F-75) — THE NEGATIVE HALF OF THE VERDICT ABOVE. That
            // Note is the LAST statement of the try, after the mutating calls, and its latch is set
            // immediately before it; so if UnregisterEscapable throws, neither line appears and the
            // latch stays false — an empty log, which is byte-identical to "the patch never ran" and
            // to "IsStandalone was false". Three states, one reading. LeaveSharedStacks runs once
            // per session, so this fires at most once.
            VRLog.Alert("WorldUI", "VR options: could not leave the escapable stack "
                + $"({e.GetType().Name}: {e.Message}). The menu still opens and closes; the two "
                + "settings windows may still fight over a single ESC press.");
        }
    }

    /// <summary>
    /// Leave the CONTROLLER INPUT AREA stack, on every open. This cannot be done once at injection
    /// because <c>UISubmenuGOWindow.Show()</c> re-enrols the area every single time
    /// (<c>UISubmenuGOWindow.cs</c>:72 <c>controllerArea.Enable()</c> →
    /// <c>ControllerInputAreaLocal.Enable()</c> → <c>RegisterArea(this)</c> + <c>Focus()</c>).
    ///
    /// <para><b>WHY NOT SIMPLY DESTROY THE COMPONENT.</b> Because <c>UISubmenuGOWindow</c> holds it
    /// in a serialized field and dereferences it UNCONDITIONALLY in <c>Awake</c> (:38-40), in
    /// <c>Show</c> (:72) and in <c>OnDisable</c> (:121). Destroying it turns every open and every
    /// close of the VR settings into a <c>MissingReferenceException</c> — the component would be
    /// gone and so would the menu.</para>
    ///
    /// <para><b>WHAT IS CALLED INSTEAD.</b> <c>ControllerInputAreaLocal.Destroy()</c> — the game's
    /// own public "leave the manager" method, and the exact call <c>UISubmenuGOWindow.OnDisable</c>
    /// already makes on this same object on every hide. It runs <c>Unfocus()</c> →
    /// <c>UnfocusArea(Id)</c> → <c>ReturnPrevious()</c>, which puts the focus back on the area that
    /// held it before <c>Show()</c> took it and pops that area back off the shared stack
    /// (<c>ControllerInputAreaManager.cs</c>:252-262, :269-280), then <c>DisableGroup()</c>, then
    /// <c>UnregisterArea(this)</c>. The manager's focus and stack therefore end the open edge
    /// exactly as they began it, and "Options Display" is not in <c>m_AvailableAreas</c> at all —
    /// so the game's "Options" area can never be unfocused by ours, nor ours by the game's.</para>
    ///
    /// <para><b>WHAT IT COSTS.</b> Gamepad navigation INSIDE the VR settings window. That is the
    /// deliberate trade: in VR the panel is driven with the laser, and the alternative is the
    /// game's own options window losing ITS navigation every time the VR one opens. The name in the
    /// log is the falsifier either way.</para>
    /// </summary>
    private static void LeaveInputAreaStack()
    {
        if (!IsStandalone || _inputArea == null)
            return;
        try
        {
            _inputArea.Destroy();

            if (_loggedAreaLeave)
                return;
            _loggedAreaLeave = true;
            // HW-VERIFY: the second half of the ModBuild 336 verdict, and the one Finding 3 says an
            // escapable-only fix would have missed. Default-level tier for the same reason.
            VRLog.Note("WorldUI",
                $"VR options: the standalone pane has LEFT the controller input-area stack (area "
                + $"'{_inputArea.Id}' unregistered from ControllerInputAreaManager immediately after "
                + "every Show, via the game's own ControllerInputAreaLocal.Destroy — the same call "
                + "UISubmenuGOWindow.OnDisable already makes on this object). Its Unfocus returns the "
                + "focus to whatever held it before the window opened and pops that area back off the "
                + "shared stack, so the manager ends the open edge in the state it started it. PROOF "
                + "IN THE NEXT LOG: every '[AREA MANAGER] Register area Options Display' is followed "
                + "within a few lines by 'Unregister area Options Display' and a 'Return to previous "
                + "area', and the game's own '[AREA MANAGER] Set Focused Area Options' is never "
                + "preceded by an unfocus caused by ours. WHAT IT COSTS: no GAMEPAD navigation inside "
                + "the VR settings panel (the laser drives it); the game's own options window keeps "
                + "its navigation, which it did not while the two shared this stack.");
        }
        catch (Exception e)
        {
            // The same hole as its twin above (2026-09 refactor, F-75), with one difference: this
            // method runs on EVERY open, so the line needed its own one-shot latch before it could
            // print. Do not remove that latch and leave the tier.
            if (_loggedAreaLeaveFailed)
                return;
            _loggedAreaLeaveFailed = true;
            // HW-VERIFY: the negative half of the ModBuild 336 input-area verdict.
            VRLog.Alert("WorldUI", "VR options: could not leave the controller input-area stack "
                + $"({e.GetType().Name}: {e.Message}). The menu is open and usable; with a gamepad "
                + "in use it may still steal navigation focus from the game's options window.");
        }
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

        window.OnHidden.AddListener(() => NotifyHidden(window));
    }

    /// <summary>
    /// Accept the native close edge before UIWindow updates IsOpen. UISubmenuGOWindow first
    /// deactivates its own GameObject, then invokes OnHidden; UIWindow only changes visual state
    /// after that callback returns. Checking IsOpen alone discards every real close (build 536).
    /// A replaced clone or an active, reopened pane cannot consume the current close callback.
    /// </summary>
    private static void NotifyHidden(UISubmenuGOWindow source)
    {
        if (!ReferenceEquals(source, _window) || (IsOpen && source.gameObject.activeSelf))
            return;
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
            bool opened = ShowStandalone();
            if (!opened)
                _onHidden = null;
            return opened;
        }

        // FIRE EXIT: the menu is a tab, so the game's window has to carry it.
        if (!Singleton<UIOptionsWindow>.IsInitialized)
        {
            _onHidden = null;
            return false;
        }

        _selectOnShow = true;
        UISubmenuGOWindow openedPane = _window;
        Singleton<UIOptionsWindow>.Instance.Show(pointAt, () => NotifyHidden(openedPane));
        return true;
    }

    /// <summary>
    /// Show the standalone window and VERIFY THE OUTCOME, then leave the shared input-area stack.
    ///
    /// <para><b>THE FIRST OPEN OF A SESSION FAILED, AND THE ERROR LINE THAT REPORTED IT NAMED EVERY
    /// TERM EXCEPT THE BLOCKER (fixed here).</b> ModBuild 348's hardware log: the first click on the
    /// menu row printed <c>the standalone window DID NOT OPEN — Show() returned twice and
    /// UIWindow.IsOpen is still false (activeSelf True, activeInHierarchy True, UIWindow.enabled
    /// True, CanvasGroup alpha 0.00)</c>, and the next click on the same row opened it cleanly.
    /// Every value that line printed was true, and none of them was the reason.</para>
    ///
    /// <para><b>THE REASON IS <c>UIWindow.HasGoneToStartingState</c>.</b>
    /// <c>UISubmenuGOWindow.Show()</c> never calls <c>UIWindow.Show()</c> — it calls
    /// <c>ShowOrUpdateStartingState()</c> (UISubmenuGOWindow.cs:71), and that method OPENS NOTHING
    /// while the flag is false: it only records <c>m_StartingState = Shown</c> for a <c>Start()</c>
    /// that has not run (UIWindow.cs:512-521). The flag is set in <c>UIWindow.Start()</c> (:370),
    /// and Unity had never run <c>Start()</c> on this object, because <see cref="CloneWindow"/>
    /// deactivates the clone inside the same synchronous block that instantiates it. Unity
    /// dispatches <c>Start()</c> before the first <c>Update</c> AFTER an object becomes active, so
    /// an object created and deactivated within one call never reaches it — the pane sat un-started
    /// from injection until the player's first click, and that click was what finally started it.
    /// The retry that shipped in ModBuild 335 could not rescue it: it re-activated an object that
    /// was already active and then called the SAME declining method a second time. That is also why
    /// the failing click cost 63.68 ms of an 86.40 ms frame and logged <c>built Curated</c>
    /// TWICE — <c>UISubmenuGOWindow.Show()</c> fires <c>OnShow</c> (:73) whether or not it opened,
    /// and <c>OnShow</c> is what rebuilds the whole row list.</para>
    ///
    /// <para><b>THE REMEDY IS THE ONE METHOD THAT DOES NOT CONSULT THE FLAG.</b>
    /// <c>UIWindow.Show()</c> asks only <c>IsActive()</c> — <c>enabled &amp;&amp;
    /// activeInHierarchy</c>, UIWindow.cs:476/:417-423 — and then transitions to <c>Shown</c>
    /// outright (:481-492). It runs only AFTER <c>UISubmenuGOWindow.Show()</c> has done the parts
    /// only it can do (re-activate the GameObject, enrol the controller area, fire <c>OnShow</c> so
    /// the content exists), so nothing is skipped and, because it is not the submenu wrapper,
    /// nothing is built twice. It is also the remedy that cannot mis-fire: it NAMES the state it
    /// wants, where the obvious alternative — <c>UIWindow.OtherInit()</c>, which runs <c>Start()</c>
    /// by hand — would transition to whatever <c>m_StartingState</c> happens to hold, a value
    /// written by the very call that just declined. Unity's own <c>Start()</c> still arrives on the
    /// next frame and re-affirms <c>Shown</c> instantly; that costs one extra visibility edge and
    /// nothing else (<c>WindowMaterialise.OnWindowVisibility</c> returns on <c>e.Shown</c>, and
    /// <c>ModalFallback</c>'s tick is level-triggered).</para>
    ///
    /// <para>THE OUTCOME, NOT THE PATH — AND THE OTHER REFUSAL IS KEPT. This pane is a
    /// <c>m_DisableOnZeroAlpha</c> window, so its own alpha tween DEACTIVATES its GameObject on
    /// every close (UIWindow.cs:742-748, confirmed by the ModBuild 335 <c>WINDOW MATERIALISE</c>
    /// line), and an inactive object is the refusal <c>UIWindow.Show()</c> states outright.
    /// <c>UISubmenuGOWindow.Show()</c> re-activates its OWN GameObject first (:70-73) but cannot
    /// reach an inactive ANCESTOR, so that remedy stays — and the log now says WHICH of the two was
    /// needed instead of assuming.</para>
    ///
    /// <para>The standing rule is that it MUST always be possible to open the options menu, so a
    /// failure here is still an <c>Error</c> naming the state that produced it — and the state it
    /// names now includes the flag, without which no reader of that line could have reached the
    /// cause.</para>
    /// </summary>
    private static bool ShowStandalone()
    {
        if (_window == null)
            return false;

        UIWindow? win = _window.GetComponent<UIWindow>();
        if (win != null)
            ModalFallback.PrepareModMenuReopen(win);

        // READ BEFORE THE SHOW. The Show below re-activates the GameObject, and an activation is
        // exactly what makes Unity queue Start() — the method that sets this flag. Taking the
        // reading first means the line that reports the blocker cannot be reporting the remedy's
        // own after-effect.
        bool startedBefore = win != null && win.HasGoneToStartingState;

        _window.Show();

        // No UIWindow means there is nothing to ask, and nothing this method can repair — the
        // `[RequireComponent]` on UISubmenuGOWindow makes it unreachable in practice, and taking
        // the Show() as the answer is the only honest reading if a game update ever removes it.
        if (win == null)
        {
            LeaveInputAreaStack();
            return true;
        }

        bool open = win.IsOpen;
        string remedy = string.Empty;

        if (!open && !_window.gameObject.activeInHierarchy)
        {
            // An inactive object is the refusal UIWindow.Show() states outright (IsActive(),
            // :417-423). UISubmenuGOWindow.Show() re-activates its own GameObject but cannot reach
            // an inactive ANCESTOR, so re-activate and go back through the submenu's Show — the
            // input area and OnShow must run for the attempt that counts.
            _window.gameObject.SetActive(true);
            _window.Show();
            open = win.IsOpen;
            remedy = "the pane's GameObject was re-activated by hand and shown again";
        }

        if (!open && !startedBefore)
        {
            // THE FIRST OPEN OF THE SESSION, and the whole of the ModBuild 348 defect. See this
            // method's summary: ShowOrUpdateStartingState() declined because Start() has never run
            // on the clone. UIWindow.Show() is the path that does not ask.
            win.Show();
            open = win.IsOpen;
            remedy = "UIWindow.Show() was called directly, because UIWindow.HasGoneToStartingState "
                     + "was false and UISubmenuGOWindow.Show()'s ShowOrUpdateStartingState() "
                     + "therefore only recorded a starting state instead of opening anything";
        }

        if (!open)
            VRLog.Error("WorldUI", "VR options: the standalone window DID NOT OPEN — every remedy "
                + "this method has was tried and UIWindow.IsOpen is still false (activeSelf "
                + $"{_window.gameObject.activeSelf}, activeInHierarchy "
                + $"{_window.gameObject.activeInHierarchy}, UIWindow.enabled {win.enabled}, "
                + $"UIWindow.HasGoneToStartingState {win.HasGoneToStartingState}, "
                + $"CanvasGroup alpha {(win.GetComponent<CanvasGroup>() is { } cg ? cg.alpha.ToString("F2") : "n/a")}, "
                + $"parent '{(_window.transform.parent != null ? _window.transform.parent.name : "<none>")}'). "
                + "THE FLAG IS THE FIELD TO READ FIRST: false means UISubmenuGOWindow.Show() only "
                + "recorded a starting state for a Start() Unity has not run — the cause of the "
                + "ModBuild 348 first-click failure, which is remedied above, so a false here is a "
                + "NEW way of reaching it; true means the refusal is one this method has never seen. "
                + "This violates the standing ruling that the options menu must ALWAYS be openable. "
                + "Treat this line as the lead, not the click that produced it — every VR setting is "
                + "still editable in BepInEx/config/dev.gloomhavenvr*.cfg meanwhile.");
        else if (remedy.Length > 0 && !_loggedShowRemedy)
        {
            _loggedShowRemedy = true;
            // HW-VERIFY: the ModBuild 350 fix's own falsifier, and the only line that proves the
            // first click opened the menu. Exactly one of these per pane, on its first open, with
            // the HasGoneToStartingState reason, is the fix working as designed. The same reason on
            // a LATER open means Start() still never reaches this clone. This line ABSENT while the
            // first click works means the pane now starts on its own and the rescue is dead code.
            VRLog.Note("WorldUI", "VR options: the standalone window did not open on the plain "
                + $"UISubmenuGOWindow.Show() and DID open after a remedy — {remedy}. This is "
                + "EXPECTED EXACTLY ONCE per pane, on its very first open: the clone is deactivated "
                + "in the same block that instantiates it, so Unity has never dispatched "
                + "UIWindow.Start() on it and ShowOrUpdateStartingState() declines until it has "
                + "(UIWindow.cs:512-521/:370). Before ModBuild 350 that first click failed outright "
                + "and the player needed a second one, which is what this line replaces. Nothing "
                + "about the two settings windows is coupled by it: the pane's UIWindow.ID stays "
                + "None, it is still registered as a mod-owned player menu with MenuWindowFamily, "
                + "and no game window is opened, hidden or read on our behalf.");
        }

        // Whether or not it opened: if Show() got as far as enrolling the input area, take it back
        // out. Doing this only on success would leave the area registered on exactly the failure
        // path where it matters most.
        LeaveInputAreaStack();
        return open;
    }

    /// <summary>
    /// Close the menu, whichever mode it is in. A no-op when it is not open.
    ///
    /// <para>IT GOES THROUGH <see cref="ModalFallback.CloseFloatedWindow"/>, NOT
    /// <c>UISubmenuGOWindow.Hide()</c>, AND THAT IS NOT A DETAIL — BUT THE REASON PRINTED HERE
    /// UNTIL ModBuild 336 WAS WRONG. It said the pane's id is <c>OptionsSubmenu</c> and its float
    /// therefore STICKY, so only <c>UserClosing</c> could drop it. The hardware log says the id is
    /// <c>None</c> on every line, so the pane is not in <c>NonBlockingMenus</c> and
    /// <c>ModalFallback.8.Convert.cs</c>:657 gives it <c>Sticky = false</c> outside the map room.
    /// The right reason is the plainer one: <c>CloseFloatedWindow</c> is the SINGLE close path the
    /// corner X, the escape chord and this method share, it sets <c>UserClosing</c> so the float is
    /// released in the same act rather than a tick later, it force-<c>Hide()</c>s a window whose
    /// <c>Escape()</c> declines (which ours now always does — see
    /// <see cref="LeaveSharedStacks"/>), and it writes the one log line that says which of the two
    /// actually closed it. In the map room, where <c>MapRoomParallel</c> DOES make the float sticky,
    /// the old paragraph's argument holds as written.</para>
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
                {
                    ModalFallback.CloseFloatedWindow(win);

                    // A PENDING STARTING STATE IS A WINDOW THAT CAN STILL OPEN ITSELF. While
                    // UIWindow.Start() has not run on this clone, its m_StartingState holds whatever
                    // the last ShowOrUpdateStartingState() wrote — Shown — and Unity's deferred
                    // Start() would act on that and put a pane on screen that nobody asked for and
                    // no menu row is lit over. That is precisely what the ModBuild 348 log left
                    // behind: the row's own failure path ran Close(), CloseFloatedWindow took its
                    // "the game had already hidden it" branch (correctly — the window had never
                    // opened) and hid NOTHING, so the recorded Shown survived the close.
                    // HideOrUpdateStartingState() rewrites exactly that pending value, and once
                    // Start() HAS run it is a plain Hide(), which is a no-op on an already-hidden
                    // window (UIWindow.cs:500-509/:524-527). The term it is gated on is the game's
                    // own flag, never a value the mod writes.
                    if (!win.HasGoneToStartingState)
                        win.HideOrUpdateStartingState();
                }
                else
                {
                    _window.Hide();
                }
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

    /// <summary>Discard a failed partial clone and retry with bounded backoff and logging.</summary>
    private static void Degrade(UIOptionsWindow host, string reason)
    {
        // Forget alone would abandon any partially built live clone. Teardown unregisters its
        // menu family, removes a fire-exit tab and destroys only mod-owned objects before retry.
        Shutdown();
        _failedHost = host;
        _degraded = true;
        bool report = _consecutiveInjectionFailures < MaxConsecutiveInjectionFailures;
        _consecutiveInjectionFailures = Math.Min(_consecutiveInjectionFailures + 1,
            MaxConsecutiveInjectionFailures);
        _nextInjectionRetry = Time.unscaledTime
            + (_consecutiveInjectionFailures < MaxConsecutiveInjectionFailures ? 2f : 30f);
        if (!report)
            return;
        if (_consecutiveInjectionFailures < MaxConsecutiveInjectionFailures)
        {
            VRLog.Note("WorldUI", $"VR options: injection attempt {_consecutiveInjectionFailures} of "
                + $"{MaxConsecutiveInjectionFailures} FAILED — {reason}. The partial menu was removed; "
                + "this host will retry after two seconds, and a replacement host can retry immediately.");
            return;
        }
        VRLog.Alert("WorldUI", $"VR options: menu injection repeatedly failed — {reason}. "
            + "Retries continue every thirty seconds; further identical-cycle failures are silent "
            + "until a menu is built successfully. The game's own options remain untouched.");
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
        // The modal classifier must not outlive the window it names ([[gate-outliving-its-edge]]):
        // Forget() is on every teardown path, including Shutdown()'s finally.
        MenuWindowFamily.ForgetModMenu(_window != null ? _window.GetComponent<UIWindow>() : null);
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
        _loggedShowRemedy = false;
        // The failure owner/backoff deliberately survives this per-pane reference cleanup.
        _degraded = false;
        // The area component belongs to the pane that has just gone; a stale reference here would
        // make the next open's LeaveInputAreaStack act on a dead object.
        _inputArea = null;
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
