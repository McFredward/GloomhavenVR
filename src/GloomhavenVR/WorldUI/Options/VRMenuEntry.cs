using System;
using System.Collections.Generic;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE DOOR TO THE VR SETTINGS MENU — one row in the pause menu, and one in the main menu.
///
/// <para>User, 2026-09-02: <i>"Lösche das 'VR Optionen' in den 'Optionen', da man es ja nun über
/// das Pausenmenu öffnen kann … Es soll garnicht mehr ein 'Tab' von 'Optionen' sein, sondern ein
/// ganz eigenständiges Menu!"</i> The tab is gone (<see cref="VROptionsTab"/>), so these rows are
/// now the ONLY doors — which is why there are two of them.</para>
///
/// <para>WHAT CHANGED HERE, AND WHY IT WAS "völlig kaputt". This row used to open the GAME's
/// options window and then select the VR tab inside it. That did two wrong things at once: it put
/// the game's whole settings tab rail on screen beside the VR settings, and it lost the race
/// against that window's own <c>OnShow</c>, which runs <c>CloseWindows()</c> at the end of the fade
/// and turns every tab — including the one just selected — back off (UIOptionsWindow.cs
/// :201/:276-281). Both are gone: the row now calls <see cref="VROptionsTab.Open"/>, which shows
/// the mod's own detached window and nothing else.</para>
///
/// <para>A CLONE OF A REAL MENU ROW, for the same reason the settings pane is a clone of a real
/// tab: a row here must animate like its neighbours, carry the same hover and focus wiring, and be
/// reachable by the menu's navigation. All of that lives in serialized references inside the shipped
/// prefab, so each row is INSTANTIATED FROM A LIVE DONOR and re-labelled. Nothing about a donor is
/// modified, and each clone is inserted directly after it so the two read as a pair.</para>
///
/// <para><b>WITH ONE DELIBERATE EXCEPTION, AND IT IS THE WHOLE OF ModBuild 336: THE ROW DOES NOT
/// JOIN THE MENU'S <c>ToggleGroup</c>.</b> User, after the 335 hardware round: <i>"Die VR Optionen
/// und normale Optionen sind irgendwie immer noch abhängig … Aktuell schließt sich das eine
/// Fenster, wenn das andere öffnet … Entferne hier jegliche Abhängigkeit von beiden
/// Fenstern."</i></para>
///
/// <para>THE MECHANISM, read off the decompiled game rather than guessed. A cloned row keeps its
/// donor's <c>ExtendedToggle.group</c>, which is <c>ESCMenu</c>'s single <c>toggleGroup</c>
/// (<c>ESCMenu.cs</c>:25). A <c>ToggleGroup</c> is single-select: turning one member on turns every
/// other member off, and <c>UIMenuOptionToggle</c> routes that straight into the option's own
/// callbacks (<c>UIMenuOptionToggle.cs</c>:52-89, :113-121). The game wires its Optionen row's
/// DEselect to <c>Singleton&lt;UIOptionsWindow&gt;.Instance.Hide()</c> (<c>ESCMenu.cs</c>:138-146)
/// and ours is wired to <see cref="VROptionsTab.Close"/> — so each row was literally the other
/// window's close button. That is the alternation in the ModBuild 335 log, where a
/// <c>UIWindow hidden</c> on one window is followed three or four lines later by a
/// <c>UIWindow SHOWN</c> on the other, over and over.</para>
///
/// <para>SO THE CLONE'S TOGGLE IS TAKEN OUT OF THE GROUP (<see cref="Detach"/>) and becomes a free
/// on/off. Three things follow and all three are wanted. The two windows can stand open at the same
/// time. Clicking an already-lit VR row now actually turns it off — inside a single-select group a
/// lone lit toggle refuses to switch off and bounces straight back on, which is a standing way for
/// a row to latch lit over a closed window and never open it again. And <c>ESCMenu.OnHide</c>'s
/// <c>toggleGroup.SetAllTogglesOff()</c> (<c>ESCMenu.cs</c>:353) no longer reaches us, so closing
/// the pause menu leaves the VR settings standing — which is what an independent window does.</para>
///
/// <para><b>THAT PARAGRAPH IS ABOUT THE PAUSE MENU, AND ModBuild 349 IS THE BUILD THAT SAYS SO.</b>
/// The detach was applied to BOTH cloned rows, because at the time there was one cloning path and
/// no notion of WHERE the row was. In the main menu the group was not a coupling to be broken: it
/// was the only thing that closed the VR pane when the player picked another entry, and taking our
/// row out of it left the pane standing on top of whatever he opened next, swallowing every click
/// aimed at it (user, 2026-09-02: <i>"Im Hauptmenu … verschwinden die VR-Optionen nicht mehr was
/// sie dann darüber legen lässt - dann kann man nichts mehr steuern. Hier soll es sich anders
/// Verhalten als im Szenario oder in der map-umgebung"</i>). The detach STAYS — in both menus, and
/// for all three reasons above. What was added instead is
/// <see cref="TickMainMenuExclusivity"/>/<see cref="ClearRivals"/>: in the MAIN MENU ONLY, and
/// keyed on <see cref="MenuExclusivity"/> rather than on a local <c>if</c>, the mod performs the
/// one-at-a-time arbitration itself, over rows it only reads and closes through the game's own
/// <c>Deselect()</c>. Neither window is ever the other's close button again, and in a scenario and
/// in the map room nothing whatever changed.</para>
///
/// <para>THE ROW'S LIT STATE IS THEREFORE OURS TO KEEP HONEST, and <see cref="TickRowLatch"/> is
/// the belt: a row lit over a settings window that is not open is forced back off. That is the
/// falsifiable guard behind the standing rule that it must ALWAYS be possible to open the settings
/// — <c>UIMenuOption.Select()</c> returns early when it already believes itself selected
/// (<c>UIMenuOption.cs</c>:121-126), so a stale lit row is exactly a door that stops
/// answering.</para>
///
/// <para>TWO MENUS, BECAUSE THERE ARE TWO PLACES A PLAYER CAN BE — AND SINCE ModBuild 349 THEY DO
/// NOT ARBITRATE ALIKE (<see cref="MenuExclusivity"/>: the pause menu is <c>Parallel</c>, the main
/// menu is <c>OneAtATime</c>). <c>UIMapEscMenu</c> and
/// <c>UIScenarioEscMenu</c> are subclasses of <c>ESCMenu</c> and the <c>optionsButton</c> field is
/// on the base, so the map room and a scenario are one code path. The MAIN menu is not an
/// <c>ESCMenu</c> at all — it is <c>UIMainOptionsMenu</c> — and before this build the VR tab was
/// the only route from there. Deleting the tab without adding <see cref="TickMainMenu"/> would have
/// made every VR setting unreachable until a game was loaded, which is exactly the standing rule
/// this project keeps: the settings must never become unreachable.</para>
///
/// <para>NO DEAD ROWS. Neither row is injected unless <see cref="VROptionsTab.CanOpen"/> is true —
/// there must never be a menu entry that opens an empty window, or nothing at all. If the VR menu
/// could not be built, the rows simply do not appear and the game's own Optionen entry is untouched
/// and still opens normally.</para>
///
/// <para>REVERSIBILITY. The only things that exist are two mod-owned clones parented into the
/// game's hierarchy. <see cref="Shutdown"/> destroys them and both menus are byte-for-byte as they
/// shipped.</para>
/// </summary>
internal static class VRMenuEntry
{
    private const string CloneName = "GloomhavenVR.PauseMenuEntry";
    private const string MainCloneName = "GloomhavenVR.MainMenuEntry";

    /// <summary>The pause menu we are injected into (null = not injected). Compared by reference
    /// every tick: a new scene brings a new ESCMenu instance and our clone died with the old
    /// one.</summary>
    private static ESCMenu? _host;
    private static UIMainMenuOption? _entry;

    /// <summary>The game's row each clone was cut from — the seat pass (<see cref="MenuRowSeat"/>)
    /// measures the clone against it every frame the menu is shown.</summary>
    private static UIMainMenuOption? _donor;
    private static UIMainMenuOption? _mainDonor;

    /// <summary>The main menu we are injected into, and our row in it.</summary>
    private static UIMainOptionsMenu? _mainHost;
    private static UIMainOptionsMenu? _mainInjectFailed;
    private static UIMainMenuOption? _mainEntry;

    /// <summary>
    /// THE MAIN MENU'S OWN ARBITRATION SET — every row the menu itself switches between, ours
    /// excluded. Resolved lazily from <c>UIMainOptionsMenu.menuOptions</c>, the list the game fills
    /// in <c>Start()</c> through <c>InitializeButton</c> (decompiled :91-99, :162-176), because
    /// that IS the set the single-select <c>ToggleGroup</c> arbitrates over. A component sweep
    /// would also pick up the sub-option flyout's slots (<c>UIMainMenuSuboption : UIMainMenuOption</c>)
    /// and the hidden sandbox row, neither of which the menu arbitrates.
    /// </summary>
    private static UIMainMenuOption[]? _mainRivals;

    /// <summary>
    /// THE YIELD RULE IS ARMED ONLY ONCE THE FIELD HAS BEEN SEEN CLEAR — see
    /// <see cref="TickMainMenuExclusivity"/> for why, and <see cref="MenuExclusivity"/> for the
    /// rule itself. Never a standing suppression: nothing else in the mod reads it, its only effect
    /// is to permit one action, and it is cleared on every path that is not "the pane is open in
    /// the main menu with no rival lit".
    /// </summary>
    private static bool _yieldArmed;

    private static bool _loggedInject;
    private static bool _loggedIcon;
    private static bool _loggedMain;

    // A transient native hierarchy/layout error must not permanently remove the only doors
    // to settings. Retry after a short cooldown, with one report per pass per module lifetime.
    // The user explicitly requires repeated opening to remain available (2026-09-20).
    private static float _retryAfter;
    private static bool _loggedTickFailure;
    private static bool _loggedSeatFailure;

    private static bool _loggedDetach;
    private static bool _loggedLatch;

    // Failed pause-row construction retries on the same host after a bounded delay.
    // Remember the host separately to report missing native donor data only once per host.
    private static ESCMenu? _pauseInjectFailed;
    private static float _pauseRetryAfter;

    /// <summary>The mod's own VR emblem: a gold woodcut headset, generated for this menu and
    /// trimmed to what it DRAWS rather than to its frame (a mostly-transparent logo aligned to its
    /// frame has shipped 2.35x too wide in this project before).</summary>
    private const string IconAsset = "Assets/Bundle/UI/VRMenuIcon.png";

    // ==========================================================================================
    //  Main-menu discovery budget
    // ==========================================================================================

    /// <summary>
    /// THE MAIN MENU HAS NO SINGLETON, so it can only be found by a scene sweep — and a sweep per
    /// frame is this project's single most expensive recurring mistake (one <c>FindObjectOfType</c>
    /// per frame once owned 12.6 ms of an 11.11 ms budget). So the sweep is bounded twice: it runs
    /// at most once per <see cref="MainScanInterval"/> seconds, and at most
    /// <see cref="MainScanBudget"/> times per SCENE. A scene that has no main menu therefore costs
    /// a handful of sweeps just after it loads and nothing at all thereafter; the budget is re-armed
    /// only by the active scene actually changing.
    /// </summary>
    private const float MainScanInterval = 1f;

    private const int MainScanBudget = 12;

    /// <summary>Handle of the scene the budget below belongs to. 0 is no scene, which matches
    /// nothing, so the first tick in any scene arms a fresh budget.</summary>
    private static int _mainScanScene;
    private static int _mainScansLeft;
    private static float _nextMainScan;

    /// <summary>Per-frame step from <see cref="WorldUIModule"/>. Cheap: two singleton reads while
    /// injected, plus a bounded main-menu sweep that stops on its own.</summary>
    internal static void Tick()
    {
        if (Time.unscaledTime < _retryAfter)
            return;
        try
        {
            TickPauseMenu();
            TickMainMenu();
            // ONE read of the window's state for both rules, so they cannot disagree within a
            // frame: the exclusivity rule acts while it is open, the latch reconcile while it is
            // not, and asking twice would let a close that happens between them be seen by neither.
            bool open = VROptionsTab.IsOpen;
            TickMainMenuExclusivity(open);
            TickRowLatch(open);
        }
        catch (Exception ex)
        {
            _retryAfter = Time.unscaledTime + 2f;
            if (!_loggedTickFailure)
            {
                _loggedTickFailure = true;
                VRLog.Error("WorldUI", "The VR menu entries threw; recovery will retry after two seconds. "
                    + "The game's own menus are unaffected. "
                    + $"{ex.GetType().Name}: {ex.Message}\n" + ex.StackTrace);
            }
        }
    }

    /// <summary>
    /// Per-frame LateUpdate step: own each clone's SEAT in its menu (<see cref="MenuRowSeat"/>).
    /// After every Update writer the game has, so the rendered frame is the seated one. The
    /// pause menu is "shown" by its own window state; the main menu has no window of its own,
    /// so its host's activeInHierarchy stands in (a hidden row must still be repairable).
    /// </summary>
    internal static void LateTick()
    {
        if (Time.unscaledTime < _retryAfter)
            return;
        try
        {
            bool pauseVisible = _host != null && _host.IsOpen;
            bool mainVisible = _mainHost != null && _mainHost.gameObject.activeInHierarchy;
            if (pauseVisible)
                MaintainRowAvailability(_entry);
            if (mainVisible)
                MaintainRowAvailability(_mainEntry);
            MenuRowSeat.Tick(true, _entry, _donor, pauseVisible);
            MenuRowSeat.Tick(false, _mainEntry, _mainDonor, mainVisible);
        }
        catch (Exception ex)
        {
            _retryAfter = Time.unscaledTime + 2f;
            if (!_loggedSeatFailure)
            {
                _loggedSeatFailure = true;
                VRLog.Error("WorldUI", "The VR menu row seat pass threw; recovery will retry after two seconds. "
                    + "The game's own menus are unaffected. "
                    + $"{ex.GetType().Name}: {ex.Message}\n" + ex.StackTrace);
            }
        }
    }

    /// <summary>Keep only our cloned door visible and visibly usable while its host is shown.
    /// Never enable a host window or change the game's other rows or their gating.</summary>
    private static void MaintainRowAvailability(UIMainMenuOption? row)
    {
        if (row == null)
            return;
        if (!row.gameObject.activeSelf)
            row.gameObject.SetActive(true);
        if (!row.enabled)
            row.enabled = true;
        if (!row.IsInteractable)
            row.IsInteractable = true;
        row.SetFocused(true);
    }

    // ==========================================================================================
    //  The pause menu (map room and scenario)
    // ==========================================================================================

    private static void TickPauseMenu()
    {
        ESCMenu? host = Singleton<ESCMenu>.IsInitialized ? Singleton<ESCMenu>.Instance : null;
        if (host == null)
        {
            // The menu went away with its scene; our clone went with it.
            _host = null;
            _entry = null;
            _donor = null;
            return;
        }
        if (ReferenceEquals(host, _host) && _entry != null)
            return;
        if (ReferenceEquals(host, _pauseInjectFailed) && Time.unscaledTime < _pauseRetryAfter)
            return;

        // NEVER A DOOR ONTO NOTHING. VROptionsTab.Tick runs BEFORE this one (WorldUIModule's
        // order), so by now it has either built the menu or said it could not.
        if (!VROptionsTab.CanOpen)
            return;

        _host = host;
        _entry = null;
        _donor = null;
        Inject(host);
        if (_entry == null)
        {
            _pauseInjectFailed = host;
            _pauseRetryAfter = Time.unscaledTime + 2f;
        }
        else
            _pauseInjectFailed = null;
    }

    private static void Inject(ESCMenu host)
    {
        UIMainMenuOption? donor = host.optionsButton;
        if (donor == null)
        {
            if (!ReferenceEquals(host, _pauseInjectFailed))
                VRLog.Warn("WorldUI", "Pause menu has no options button to clone from — the VR entry "
                    + "is not added. The main-menu row is unaffected, and every VR setting remains "
                    + "editable in BepInEx/config/dev.gloomhavenvr*.cfg.");
            return;
        }

        UIMainMenuOption? clone = CloneRow(donor, CloneName);
        if (clone == null)
        {
            if (!ReferenceEquals(host, _pauseInjectFailed))
                VRLog.Warn("WorldUI", "The pause-menu options button cloned without its "
                    + "UIMainMenuOption — the VR entry is not added.");
            return;
        }

        BindRow(clone, mainMenu: false);

        _entry = clone;
        _donor = donor;
        if (!_loggedInject)
        {
            _loggedInject = true;
            VRLog.Info("WorldUI", "VR is its OWN pause-menu entry, directly under "
                + $"'{donor.name}' (label '{Loc.Mod("vr_options")}'). It opens the mod's own "
                + "standalone settings window — the game's options window is NOT opened, so its "
                + "tab rail never appears beside the VR settings. Mode: "
                + (VROptionsTab.IsStandalone ? "standalone window." : "FIRE EXIT (still a tab)."));
        }
    }

    // ==========================================================================================
    //  The main menu
    // ==========================================================================================

    /// <summary>
    /// Inject a VR row into the main menu's option list, next to its own Optionen row.
    ///
    /// <para>THIS EXISTS BECAUSE THE TAB IS GONE. The tab was the only route from the main menu,
    /// and the main menu is not an <c>ESCMenu</c>, so nothing else here could reach it. It is
    /// discovered by a bounded sweep rather than a singleton because <c>UIMainOptionsMenu</c> is
    /// not one — see the budget constants for what "bounded" costs.</para>
    /// </summary>
    private static void TickMainMenu()
    {
        if (_mainEntry != null && _mainHost != null)
            return;

        // A scene change re-arms the budget; nothing else does.
        int scene = SceneManager.GetActiveScene().handle;
        if (scene != _mainScanScene)
        {
            _mainScanScene = scene;
            _mainScansLeft = MainScanBudget;
            _nextMainScan = 0f;
            _mainHost = null;
            _mainEntry = null;
            _mainDonor = null;
            // The rival set and the arming belong to the menu instance that has just gone with its
            // scene; carrying either into the next one would arbitrate over destroyed rows
            // ([[gate-outliving-its-edge]]).
            _mainRivals = null;
            _yieldArmed = false;
        }

        // Waiting for pane recovery is not a failed scene scan. Otherwise a thirty-second
        // injection backoff would exhaust all twelve scans before the pane becomes usable.
        if (!VROptionsTab.CanOpen || Time.unscaledTime < _nextMainScan)
            return;
        _nextMainScan = Time.unscaledTime + MainScanInterval;

        // A known native menu can replace a lost row without another scene search, even after
        // discovery's budget is spent. Construction failures retain this host for a later retry.
        if (_mainHost != null)
        {
            InjectMain(_mainHost);
            return;
        }
        if (_mainScansLeft <= 0)
            return;
        _mainScansLeft--;
        UIMainOptionsMenu? menu = UnityEngine.Object.FindObjectOfType<UIMainOptionsMenu>();
        if (menu == null)
            return;
        _mainHost = menu;
        InjectMain(menu);
    }

    private static void InjectMain(UIMainOptionsMenu menu)
    {
        UIMainMenuOption? donor = menu.optionsButton != null ? menu.optionsButton.Button : null;
        if (donor == null)
            donor = menu.exitButton;
        if (donor == null)
        {
            if (!ReferenceEquals(menu, _mainInjectFailed))
                VRLog.Warn("WorldUI", "The main menu exposes neither an Optionen nor an Exit row to "
                    + "clone from — no VR row there. The pause menu still carries one, and every VR "
                    + "setting remains editable in BepInEx/config/dev.gloomhavenvr*.cfg.");
            _mainInjectFailed = menu;
            return;
        }

        UIMainMenuOption? clone = CloneRow(donor, MainCloneName);
        if (clone == null)
        {
            if (!ReferenceEquals(menu, _mainInjectFailed))
                VRLog.Warn("WorldUI", "The main-menu row cloned without its UIMainMenuOption — no VR "
                    + "row there.");
            _mainInjectFailed = menu;
            return;
        }

        _mainInjectFailed = null;
        // A MainOption (or a subclass of it) came along with the clone and would answer for a
        // menu entry that is not ours to answer for — the donor's Select() would run beside our
        // own. Ours drives the row directly through Init, so the component has no work left.
        foreach (MainOption stowaway in clone.GetComponents<MainOption>())
            UnityEngine.Object.Destroy(stowaway);

        BindRow(clone, mainMenu: true);

        _mainHost = menu;
        _mainEntry = clone;
        _mainDonor = donor;
        // Resolved here so the very first open already has the set; ResolveRivals() retries on its
        // own if the menu's Start() has not filled menuOptions yet.
        _mainRivals = null;
        _yieldArmed = false;
        ResolveRivals();

        if (!_loggedMain)
        {
            _loggedMain = true;
            VRLog.Info("WorldUI", $"VR is a MAIN-MENU entry too, directly under '{donor.name}' "
                + $"(label '{Loc.Mod("vr_options")}'). This row is what replaces the deleted VR "
                + "tab as the route to the settings before a game is loaded — without it the "
                + "settings would be unreachable in the main menu.");
        }
    }

    // These independent window toggles never hand focus away from the native menu. Its focus
    // mask dims otherwise usable rows; only native gameplay gating may disable native entries.
    private static void BindRow(UIMainMenuOption row, bool mainMenu)
    {
        row.Init(
            delegate
            {
                if (mainMenu)
                    ClearRivals();
                if (!VROptionsTab.Open(() => ClearRow(row), row.transform as RectTransform))
                    ClearRow(row);
            },
            delegate { VROptionsTab.Close(); });
    }

    // ==========================================================================================
    //  The main menu's one-at-a-time arbitration — the ModBuild 349 fix
    // ==========================================================================================

    /// <summary>
    /// THE MAIN MENU'S OWN RULE, PERFORMED BY US: while the VR settings pane stands in the main
    /// menu, another main-menu entry going lit closes it.
    ///
    /// <para><b>User, 2026-09-02, hardware round on ModBuild 348:</b> <i>"Im Hauptmenu wenn ich die
    /// VR-Optionen offen hatte, und dann etwas andere aufmache, verschwinden die VR-Optionen nicht
    /// mehr was sie dann darüber legen lässt - dann kann man nichts mehr steuern. Hier soll es sich
    /// anders Verhalten als im Szenario oder in der map-umgebung wenn es als Fenster spawnt."</i>
    /// <see cref="MenuExclusivity"/> holds the reasoning, the measurement and the rule; this method
    /// is only its hands, and it asks for the verdict rather than testing the place itself.</para>
    ///
    /// <para><b>A LIT ROW IS THE RIGHT DEFINITION OF "SOMETHING ELSE IS OPEN" HERE, and it is the
    /// menu's own.</b> Every screen the main menu can reach is opened by one of its rows —
    /// campaign, guildmaster, multiplayer, extras (credits / compendium / level editor), tutorial,
    /// the game's Optionen, exit — and each row's <c>Deselect</c> is what closes what it opened
    /// (<c>UIMainOptionsMenu.InitializeButton</c>, decompiled :162-176). So watching the ROWS
    /// catches every route, including the ones that then hide the menu itself: the extras row is
    /// already lit when it opens the credits window. Watching WINDOWS instead would need a taxonomy
    /// of which of the two dozen distinct <c>UIWindow</c>s in that scene count, and would fire on
    /// notification popups the player never opened.</para>
    ///
    /// <para><b>WHY IT ARMS ONLY AFTER SEEING THE FIELD CLEAR.</b> The test is a LEVEL — "a rival
    /// is lit" — not an edge, because an edge can be missed and would leave the pane standing over
    /// the very window it must yield to. But a bare level test has one failure mode that would
    /// break a standing ruling: if the player opens the VR settings while a rival is ALREADY lit
    /// and <see cref="ClearRivals"/> cannot clear it, the level is true the instant we open and the
    /// pane would close itself again immediately — a door that appears not to work, against
    /// <i>"Es MUSS immer möglich sein das Optionsmenu zu öffnen."</i> So the rule arms only on a
    /// tick where the pane is open and NO rival is lit, and it disarms on every other. The worst
    /// case is therefore exactly the behaviour that shipped in 348, plus a line saying so
    /// (<see cref="MenuExclusivity.NoteRefusedToArm"/>).</para>
    ///
    /// <para>THE FLAG CANNOT OUTLIVE ITS EDGE. It permits an action, it suppresses nothing, and
    /// nothing outside this method reads it. It is cleared when the pane is not open, when either
    /// menu reference is gone (teardown, scene change, degraded), when the place stops being the
    /// main menu, and in the same statement that acts on it.</para>
    /// </summary>
    private static void TickMainMenuExclusivity(bool paneOpen)
    {
        if (_mainHost == null || _mainEntry == null || !paneOpen
            || MenuExclusivity.RuleHere != MenuArbitration.OneAtATime)
        {
            _yieldArmed = false;
            return;
        }

        UIMainMenuOption? rival = FirstLitRival();
        if (rival == null)
        {
            // The menu is quiet and the pane is the only thing standing: from here on, anything the
            // player opens takes it down.
            _yieldArmed = true;
            return;
        }

        if (!_yieldArmed)
        {
            MenuExclusivity.NoteRefusedToArm(rival.name);
            return;
        }

        _yieldArmed = false;

        // SetSelected(false) rather than Deselect(): it clears the option's flag and the toggle's
        // value WITHOUT running our deselect delegate. The pane is then closed explicitly. Our
        // toggle carries no group (Detach), so there is no member to bounce back on.
        UIMainMenuOption ours = _mainEntry;
        Canvas? paneCanvas = VROptionsTab.PaneCanvas;
        try
        {
            ours.SetSelected(false);
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"VR menu row: clearing '{ours.name}' as it yielded to "
                + $"'{rival.name}' threw ({ex.GetType().Name}: {ex.Message}); the pane is closed "
                + "either way and the per-frame latch reconcile will correct the row.");
        }

        VROptionsTab.Close();
        MenuExclusivity.NoteYielded(rival.name, ours.name, paneCanvas,
                                    rival.GetComponentInParent<Canvas>());
    }

    /// <summary>
    /// The other half of the same rule: the player picked OUR row, so whatever the previous entry
    /// had opened goes. <c>Deselect()</c> is the game's own call and runs the game's own close —
    /// <c>MainOptionOptions.Deselect</c> hides the options window, a suboptions row hides the
    /// flyout — so nothing here needs to know what any row opens.
    ///
    /// <para>Guarded per row, because a <c>UnityEvent</c> chain has no per-listener catch and one
    /// row that throws must not stop the rest from clearing. A no-op outside the main menu: this is
    /// only ever called from the main-menu row's own select delegate.</para>
    /// </summary>
    private static void ClearRivals()
    {
        UIMainMenuOption[]? rivals = ResolveRivals();
        if (rivals == null)
            return;

        for (int i = 0; i < rivals.Length; i++)
        {
            UIMainMenuOption? row = rivals[i];
            if (row == null || !row.IsSelected)
                continue;
            try
            {
                row.Deselect();
            }
            catch (Exception ex)
            {
                VRLog.Warn("WorldUI", $"VR menu row: could not close the main-menu entry "
                    + $"'{row.name}' as the VR settings opened ({ex.GetType().Name}: {ex.Message}). "
                    + "The VR settings still open; that entry's window may be drawn beside them.");
            }
        }
    }

    /// <summary>The first main-menu entry other than ours that is currently lit, or null.</summary>
    private static UIMainMenuOption? FirstLitRival()
    {
        UIMainMenuOption[]? rivals = ResolveRivals();
        if (rivals == null)
            return null;

        for (int i = 0; i < rivals.Length; i++)
        {
            UIMainMenuOption? row = rivals[i];
            if (row == null || ReferenceEquals(row, _mainEntry))
                continue;
            if (row.IsSelected)
                return row;
        }
        return null;
    }

    /// <summary>
    /// The menu's own arbitration set, cached on first success. <c>menuOptions</c> is filled in
    /// <c>UIMainOptionsMenu.Start()</c>, so an empty list means "not ready yet" and is retried
    /// rather than cached — caching an empty set would silently disable the rule for the session.
    /// Our clone is not in the list (it is wired with <c>Init</c> directly rather than through the
    /// menu's <c>InitializeButton</c>), and is filtered anyway.
    /// </summary>
    private static UIMainMenuOption[]? ResolveRivals()
    {
        if (_mainRivals != null && _mainRivals.Length > 0)
            return _mainRivals;
        if (_mainHost == null)
            return null;

        List<UIMainMenuOption>? rows = _mainHost.menuOptions;
        if (rows == null || rows.Count == 0)
            return null;

        var kept = new List<UIMainMenuOption>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            UIMainMenuOption row = rows[i];
            if (row == null || ReferenceEquals(row, _mainEntry))
                continue;
            kept.Add(row);
        }

        _mainRivals = kept.ToArray();
        return _mainRivals.Length > 0 ? _mainRivals : null;
    }

    // ==========================================================================================
    //  Shared row construction
    // ==========================================================================================

    /// <summary>
    /// Clone one menu row next to its donor and re-label it. Shared by both menus because both are
    /// lists of <c>UIMainMenuOption</c> and the cloning problem is identical.
    /// </summary>
    private static UIMainMenuOption? CloneRow(UIMainMenuOption donor, string name)
    {
        // A clone from a previous injection into THIS instance (hot reload) would otherwise stack.
        Transform? stale = donor.transform.parent != null
            ? donor.transform.parent.Find(name) : null;
        if (stale != null)
            UnityEngine.Object.Destroy(stale.gameObject);

        var clone = UnityEngine.Object.Instantiate(donor.gameObject, donor.transform.parent)
            .GetComponent<UIMainMenuOption>();
        if (clone == null)
            return null;

        clone.name = name;
        // Directly BELOW the donor, so the pair reads as "settings, and the VR ones".
        clone.transform.SetSiblingIndex(donor.transform.GetSiblingIndex() + 1);
        clone.gameObject.SetActive(true);

        // The donor's localisation listeners would overwrite our caption with the German for
        // "Optionen" the first time the player switches language.
        foreach (TextLocalizedListener listener in clone.GetComponentsInChildren<TextLocalizedListener>(true))
            UnityEngine.Object.Destroy(listener);

        VROptionsTab.SetCaption(clone, Loc.Mod("vr_options"));
        ApplyIcon(clone);
        clone.IsInteractable = true;
        Detach(clone);
        return clone;
    }

    /// <summary>
    /// Synchronize the native toggle without invoking its close callback again. Native Hide
    /// dispatches OnHidden before changing IsOpen, so a recursive Close would re-enter Hide.
    /// Unity-null and callback exceptions remain guarded across scene replacement.
    /// </summary>
    private static void ClearRow(UIMainMenuOption? row)
    {
        if (row == null)
            return;
        try
        {
            row.SetSelected(false);
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"VR menu row: clearing '{row.name}' after the settings closed threw "
                + $"({ex.GetType().Name}: {ex.Message}); the per-frame latch reconcile will correct it.");
        }
    }

    /// <summary>
    /// Take the cloned row's toggle OUT of the menu's single-select <c>ToggleGroup</c> — the one
    /// change that makes the two settings windows independent. The class doc above has the full
    /// mechanism and the decompiled citations; this is the act.
    ///
    /// <para>Every <c>Toggle</c> under the clone is cleared, not just the one
    /// <c>UIMenuOptionToggle</c> happens to hold in its serialized <c>toggle</c> field: the field
    /// tells us which toggle the OPTION listens to, and the group membership that matters is
    /// whichever toggles the prefab actually put in the group. Clearing a toggle that was not in a
    /// group is a no-op, so the sweep cannot do harm and cannot miss.</para>
    ///
    /// <para>The DONOR is untouched, as always — its group membership is the game's and stays the
    /// game's, so the pause menu's own rows keep deselecting each other exactly as they shipped.
    /// Only the mod's own row steps out of the line.</para>
    ///
    /// <para><b>THIS RUNS FOR BOTH ROWS AND STILL SHOULD — but "both windows can stand open at
    /// once" is a per-PLACE consequence, not a global one.</b> See the class doc's ModBuild 349
    /// paragraph: in the main menu the group was the only thing closing our pane when the player
    /// opened another entry, so that arbitration is now performed explicitly by
    /// <see cref="TickMainMenuExclusivity"/> instead of by re-joining a group whose deselect
    /// callbacks would make each row the other window's close button again.</para>
    /// </summary>
    private static void Detach(UIMainMenuOption clone)
    {
        try
        {
            int cleared = 0;
            foreach (Toggle toggle in clone.GetComponentsInChildren<Toggle>(true))
            {
                if (toggle == null || toggle.group == null)
                    continue;
                toggle.group = null;
                cleared++;
            }

            if (_loggedDetach)
                return;
            _loggedDetach = true;
            // HW-VERIFY: this is the ModBuild 336 verdict for the coupling the user actually
            // reported, so it must print at the default log level or the round cannot say whether
            // the row was ever taken out of the group.
            VRLog.Note("WorldUI",
                $"VR menu row: taken OUT of the menu's single-select ToggleGroup ({cleared} toggle(s) "
                + "cleared). That group was the reason the two settings windows closed each other: the "
                + "game wires its own Optionen row's DEselect to UIOptionsWindow.Hide() (ESCMenu.cs"
                + ":138-146) and ours to VROptionsTab.Close(), so a single-select group made each row "
                + "the other window's close button. The VR row is now a free on/off: both windows can "
                + "stand open at once, an already-lit VR row can actually be switched off, and closing "
                + "the pause menu no longer takes the VR settings with it. PROOF IN THE NEXT LOG: with "
                + "both windows opened one after the other there must be NO 'UIWindow hidden' on one of "
                + "them in the same breath as a 'UIWindow SHOWN' on the other. The game's own rows are "
                + "untouched and still deselect each other."
                + " WHERE THAT PARALLELISM APPLIES (ModBuild 349): in a SCENARIO and in the MAP ROOM, "
                + "where the VR settings spawn as a free-floating window of their own. In the MAIN "
                + "MENU the same detach left the pane standing over whatever the player opened next, "
                + "swallowing his clicks, so there the mod now performs the menu's own one-at-a-time "
                + "rule itself — grep 'MENU ARBITRATION'. The row is still out of the group in both "
                + "places; only the arbitration differs.");
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"VR menu row: could not leave the ToggleGroup ({ex.GetType().Name}: "
                + $"{ex.Message}). The row still opens the VR settings, but it and the game's Optionen "
                + "row will keep closing each other.");
        }
    }

    /// <summary>
    /// THE BELT BEHIND THE STANDING RULE, run once a frame and costing two bool reads.
    ///
    /// <para>Out of the ToggleGroup the row's lit state is the mod's to keep honest, and the state
    /// that must never persist is LIT OVER A CLOSED WINDOW: <c>UIMenuOption.Select()</c> returns
    /// early when it already believes itself selected (<c>UIMenuOption.cs</c>:121-126), so a row
    /// stuck in that state is a door that has stopped answering — precisely the shape of the user's
    /// <i>"im Test konnte ich die VR Optionen dann irgendwann garnicht mehr öffnen"</i>. The ModBuild
    /// 335 log cannot prove that this is what happened (he did not retry in that session), so this is
    /// written as a guard that makes the state impossible AND says so when it fires, rather than as a
    /// claimed fix for something the evidence does not show.</para>
    ///
    /// <para>The correction is <c>SetSelected(false)</c>, not <c>Deselect()</c>: it clears the
    /// option's own flag and the toggle's value (<c>UIMenuOptionToggle.cs</c>:123-127) WITHOUT
    /// invoking the deselect callback — there is nothing to close, the window is already shut, and
    /// running the callback would fire a redundant close through ModalFallback.</para>
    /// </summary>
    private static void TickRowLatch(bool open)
    {
        if (open)
            return;
        UIMainMenuOption? stale = null;
        if (_entry != null && _entry.IsSelected)
        {
            stale = _entry;
            ClearRow(_entry);
        }
        if (_mainEntry != null && _mainEntry.IsSelected)
        {
            stale = _mainEntry;
            ClearRow(_mainEntry);
        }
        if (stale == null || _loggedLatch)
            return;
        _loggedLatch = true;
        // Normal closure clears the row synchronously. This bounded fallback reports a missed
        // close notification and repairs both entries before the player has to click again.
        VRLog.Note("WorldUI",
            $"VR menu row: '{stale.name}' was lit over a CLOSED VR settings window and "
            + "has been forced back off. A lit row is a door that has stopped answering — "
            + "UIMenuOption.Select() returns early when the option already believes itself selected — "
            + "so this is the guard behind the standing rule that the options menu must ALWAYS be "
            + "openable. THE MENU IS FINE; this line is the LEAD for how the row and the window got "
            + "out of step, and it prints once per session.");
    }

    /// <summary>
    /// Give the row the mod's own VR emblem — BUT ONLY IF THE DONOR ROW HAS AN ICON TO REPLACE.
    ///
    /// <para>This machine cannot see the game's menu art: only the Managed DLLs are on disk, and
    /// <c>UIMainMenuOption</c> itself declares no icon field — just a TMP label, a focus mask and a
    /// locked mask. Whether the shipped ROW carries an icon Image beside its text is authored
    /// prefab data, and therefore not a question that can be answered here. So this does not
    /// decide; it LOOKS. Exactly one image with a sprite that is neither mask means the row has an
    /// icon and ours goes in its place. Zero, or several, means it does not — and the row stays
    /// text-only like every one of its neighbours, which is the whole point: an icon nobody else
    /// has would not match the menu, it would break it.</para>
    ///
    /// <para>Either outcome is logged once, so the next hardware run answers the question that
    /// could not be answered from here.</para>
    /// </summary>
    private static void ApplyIcon(UIMainMenuOption clone)
    {
        try
        {
            var candidates = new List<UnityEngine.UI.Image>(2);
            foreach (UnityEngine.UI.Image image in
                     clone.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image == null || image.sprite == null)
                    continue;
                if (ReferenceEquals(image, clone.focusMask))
                    continue;
                candidates.Add(image);
            }

            if (candidates.Count != 1)
            {
                if (!_loggedIcon)
                {
                    _loggedIcon = true;
                    VRLog.Info("WorldUI", $"Menu rows carry {candidates.Count} sprite "
                        + "image(s) besides the focus mask, so there is no single icon slot to "
                        + "fill: the VR row stays TEXT-ONLY, matching its neighbours. The "
                        + "generated emblem is in the bundle "
                        + $"({IconAsset}) and is used the moment a row turns out to have one.");
                }
                return;
            }

            Sprite? icon = WorldUIAssets.TryLoadSprite(IconAsset);
            if (icon == null)
                return;
            candidates[0].sprite = icon;
            if (!_loggedIcon)
            {
                _loggedIcon = true;
                VRLog.Info("WorldUI", "Menu rows DO carry an icon, so the VR row wears the "
                    + $"mod's own emblem ({IconAsset}) in place of the cloned one.");
            }
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"Could not set the VR row's icon ({ex.GetType().Name}); the row "
                + "is otherwise complete.");
        }
    }

    /// <summary>Module teardown: drop both clones, leaving the menus exactly as they shipped.</summary>
    internal static void Shutdown()
    {
        // The seat pass re-based the game's own rows' hover caches; hand them back and rebuild
        // the layout without the clones BEFORE the clones go (Destroy is deferred, a rebuild is
        // not).
        MenuRowSeat.Release(_entry);
        MenuRowSeat.Release(_mainEntry);
        MenuRowSeat.ResetLogLatches();
        if (_entry != null)
            UnityEngine.Object.Destroy(_entry.gameObject);
        if (_mainEntry != null)
            UnityEngine.Object.Destroy(_mainEntry.gameObject);
        _entry = null;
        _host = null;
        _donor = null;
        _mainEntry = null;
        _mainHost = null;
        _mainInjectFailed = null;
        _mainDonor = null;
        _mainRivals = null;
        _yieldArmed = false;
        MenuExclusivity.ResetLogLatches();
        _mainScanScene = 0;
        _mainScansLeft = 0;
        _nextMainScan = 0f;
        _pauseInjectFailed = null;
        _pauseRetryAfter = 0f;
        _loggedInject = false;
        _loggedIcon = false;
        _loggedMain = false;
        _retryAfter = 0f;
        _loggedTickFailure = false;
        _loggedSeatFailure = false;
        _loggedDetach = false;
        _loggedLatch = false;
    }
}
