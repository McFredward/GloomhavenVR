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
/// <para>THE ROW'S LIT STATE IS THEREFORE OURS TO KEEP HONEST, and <see cref="TickRowLatch"/> is
/// the belt: a row lit over a settings window that is not open is forced back off. That is the
/// falsifiable guard behind the standing rule that it must ALWAYS be possible to open the settings
/// — <c>UIMenuOption.Select()</c> returns early when it already believes itself selected
/// (<c>UIMenuOption.cs</c>:121-126), so a stale lit row is exactly a door that stops
/// answering.</para>
///
/// <para>TWO MENUS, BECAUSE THERE ARE TWO PLACES A PLAYER CAN BE. <c>UIMapEscMenu</c> and
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

    /// <summary>The main menu we are injected into, and our row in it.</summary>
    private static UIMainOptionsMenu? _mainHost;
    private static UIMainMenuOption? _mainEntry;

    private static bool _loggedInject;
    private static bool _loggedIcon;
    private static bool _loggedMain;
    private static bool _degraded;
    private static bool _loggedDetach;
    private static bool _loggedLatch;

    /// <summary>
    /// LATCH RECONCILE (see <see cref="TickRowLatch"/>). The unscaled time at which a row was first
    /// seen lit over a CLOSED settings window, or 0 when the two agree. A mismatch has to hold for
    /// <see cref="LatchGraceSeconds"/> before it is acted on, because there is a legitimate one —
    /// the settings window reports <c>IsOpen == false</c> the instant <c>Hide()</c> is called, while
    /// the row's own deselect only arrives at the END of the hide fade
    /// (<c>UISubmenuGOWindow.OnCompleteHidden</c>).
    /// </summary>
    private static float _latchSince;

    /// <summary>How long a lit-row-over-closed-window mismatch must hold before it is corrected.
    /// Comfortably longer than the pane's 0.1 s hide fade and shorter than any human retry.</summary>
    private const float LatchGraceSeconds = 1f;

    /// <summary>
    /// The pause menu whose injection already FAILED, so it is not retried every frame.
    ///
    /// <para>Without this the retry ran once per frame for the rest of the scene and wrote its
    /// explanatory warning each time — a log flood that buries the very line it exists to show.
    /// Keyed on the menu instance, so the next scene's menu still gets its one attempt.</para>
    /// </summary>
    private static ESCMenu? _pauseInjectFailed;

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
        if (_degraded)
            return;
        try
        {
            TickPauseMenu();
            TickMainMenu();
            TickRowLatch();
        }
        catch (Exception ex)
        {
            // A settings entry must never be able to take a menu down with it. Disarm for the
            // session and say so once, with the stack.
            _degraded = true;
            VRLog.Error("WorldUI", "The VR menu entries threw and are disabled for this session; "
                + "the game's own menus are unaffected and every VR setting remains editable in "
                + $"BepInEx/config/dev.gloomhavenvr*.cfg. {ex.GetType().Name}: {ex.Message}\n"
                + ex.StackTrace);
        }
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
            return;
        }
        if (ReferenceEquals(host, _host) && _entry != null)
            return;
        if (ReferenceEquals(host, _pauseInjectFailed))
            return;

        // NEVER A DOOR ONTO NOTHING. VROptionsTab.Tick runs BEFORE this one (WorldUIModule's
        // order), so by now it has either built the menu or said it could not.
        if (!VROptionsTab.CanOpen)
            return;

        _host = host;
        _entry = null;
        Inject(host);
        if (_entry == null)
            _pauseInjectFailed = host;
    }

    private static void Inject(ESCMenu host)
    {
        UIMainMenuOption? donor = host.optionsButton;
        if (donor == null)
        {
            VRLog.Warn("WorldUI", "Pause menu has no options button to clone from — the VR entry "
                + "is not added. The main-menu row is unaffected, and every VR setting remains "
                + "editable in BepInEx/config/dev.gloomhavenvr*.cfg.");
            return;
        }

        UIMainMenuOption? clone = CloneRow(donor, CloneName);
        if (clone == null)
        {
            VRLog.Warn("WorldUI", "The pause-menu options button cloned without its "
                + "UIMainMenuOption — the VR entry is not added.");
            return;
        }

        // The two halves mirror ESCMenu's own wiring for the options button (ESCMenu.cs:134-146):
        // selecting unfocuses the menu and opens the settings; deselecting closes them and gives
        // focus back. What it opens is the difference — the mod's OWN window, not the game's.
        clone.Init(
            delegate
            {
                SetFocused(host, false);
                // OUT OF THE GROUP, THE ROW'S STATE IS OURS: the close callback must survive the row
                // outliving the window (a scene change destroys the menu, and a UnityEvent has no
                // per-listener catch — a MissingReferenceException here would amputate the rest of
                // the chain the game put on the same event).
                if (!VROptionsTab.Open(() => ClearRow(clone), clone.transform as RectTransform))
                {
                    // Nothing to show: hand the menu straight back rather than leaving a lit row
                    // over an empty screen.
                    SetFocused(host, true);
                    ClearRow(clone);
                }
            },
            delegate
            {
                VROptionsTab.Close();
                SetFocused(host, true);
            });

        _entry = clone;
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
        }

        if (_mainScansLeft <= 0)
            return;
        if (Time.unscaledTime < _nextMainScan)
            return;

        _nextMainScan = Time.unscaledTime + MainScanInterval;
        _mainScansLeft--;

        if (!VROptionsTab.CanOpen)
            return;

        UIMainOptionsMenu? menu = UnityEngine.Object.FindObjectOfType<UIMainOptionsMenu>();
        if (menu == null)
            return;

        _mainScansLeft = 0;
        InjectMain(menu);
    }

    private static void InjectMain(UIMainOptionsMenu menu)
    {
        UIMainMenuOption? donor = menu.optionsButton != null ? menu.optionsButton.Button : null;
        if (donor == null)
            donor = menu.exitButton;
        if (donor == null)
        {
            VRLog.Warn("WorldUI", "The main menu exposes neither an Optionen nor an Exit row to "
                + "clone from — no VR row there. The pause menu still carries one, and every VR "
                + "setting remains editable in BepInEx/config/dev.gloomhavenvr*.cfg.");
            return;
        }

        UIMainMenuOption? clone = CloneRow(donor, MainCloneName);
        if (clone == null)
        {
            VRLog.Warn("WorldUI", "The main-menu row cloned without its UIMainMenuOption — no VR "
                + "row there.");
            return;
        }

        // A MainOption (or a subclass of it) came along with the clone and would answer for a
        // menu entry that is not ours to answer for — the donor's Select() would run beside our
        // own. Ours drives the row directly through Init, so the component has no work left.
        foreach (MainOption stowaway in clone.GetComponents<MainOption>())
            UnityEngine.Object.Destroy(stowaway);

        clone.Init(
            delegate
            {
                SetMainFocused(menu, false);
                if (!VROptionsTab.Open(() => ClearRow(clone), clone.transform as RectTransform))
                {
                    SetMainFocused(menu, true);
                    ClearRow(clone);
                }
            },
            delegate
            {
                VROptionsTab.Close();
                SetMainFocused(menu, true);
            });

        _mainHost = menu;
        _mainEntry = clone;

        if (!_loggedMain)
        {
            _loggedMain = true;
            VRLog.Info("WorldUI", $"VR is a MAIN-MENU entry too, directly under '{donor.name}' "
                + $"(label '{Loc.Mod("vr_options")}'). This row is what replaces the deleted VR "
                + "tab as the route to the settings before a game is loaded — without it the "
                + "settings would be unreachable in the main menu.");
        }
    }

    /// <summary>
    /// <c>UIMainOptionsMenu.SetFocused</c> is the main menu's own focus handoff, reached through a
    /// publicised member. Guarded for the same reason the ESC-menu one is: losing the handoff is
    /// cosmetic, losing the row is not.
    /// </summary>
    private static void SetMainFocused(UIMainOptionsMenu menu, bool focused)
    {
        try
        {
            menu.SetFocused(focused);
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"Main-menu focus handoff failed ({ex.GetType().Name}); the VR "
                + "entry still opens and closes the settings.");
        }
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
    /// Turn a row's light off, safely, from the "the settings window closed" callback.
    ///
    /// <para>Guarded on THREE counts. The row may be Unity-null (its menu died with a scene while
    /// the window was still up). <c>Deselect()</c> runs the row's own deselect delegate, which calls
    /// <see cref="VROptionsTab.Close"/> — harmless when the window is already closed, and it is what
    /// keeps the ESC-menu focus handoff running, so it is the right call rather than a bare state
    /// write. And it is wrapped, because this is invoked from a <c>UnityEvent</c> chain that has no
    /// per-listener catch: a throw here would amputate every listener the game put on the same
    /// event.</para>
    /// </summary>
    private static void ClearRow(UIMainMenuOption? row)
    {
        if (row == null)
            return;
        try
        {
            row.Deselect();
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
                + "untouched and still deselect each other.");
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
    private static void TickRowLatch()
    {
        bool open = VROptionsTab.IsOpen;
        UIMainMenuOption? stale = null;

        if (!open)
        {
            if (_entry != null && _entry.IsSelected)
                stale = _entry;
            else if (_mainEntry != null && _mainEntry.IsSelected)
                stale = _mainEntry;
        }

        if (stale == null)
        {
            _latchSince = 0f;
            return;
        }

        if (_latchSince <= 0f)
        {
            _latchSince = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - _latchSince < LatchGraceSeconds)
            return;

        float held = Time.unscaledTime - _latchSince;
        _latchSince = 0f;
        stale.SetSelected(false);

        if (_loggedLatch)
            return;
        _loggedLatch = true;
        // HW-VERIFY: if this line ever appears it names a real defect that would otherwise present
        // as "the VR options row stopped working", so it has to survive the default log level.
        VRLog.Note("WorldUI",
            $"VR menu row: '{stale.name}' was lit for {held:F1}s over a CLOSED VR settings window and "
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

    /// <summary>ESCMenu.SetFocused is the game's own focus handoff. Guarded because it is reached
    /// through a publicised member and a game update could change or drop it — losing the focus
    /// handoff is a cosmetic regression, losing the entry is not.</summary>
    private static void SetFocused(ESCMenu host, bool focused)
    {
        try
        {
            host.SetFocused(focused);
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"Pause-menu focus handoff failed ({ex.GetType().Name}); the VR "
                + "entry still opens and closes the window.");
        }
    }

    /// <summary>Module teardown: drop both clones, leaving the menus exactly as they shipped.</summary>
    internal static void Shutdown()
    {
        if (_entry != null)
            UnityEngine.Object.Destroy(_entry.gameObject);
        if (_mainEntry != null)
            UnityEngine.Object.Destroy(_mainEntry.gameObject);
        _entry = null;
        _host = null;
        _mainEntry = null;
        _mainHost = null;
        _mainScanScene = 0;
        _mainScansLeft = 0;
        _nextMainScan = 0f;
        _pauseInjectFailed = null;
        _loggedInject = false;
        _loggedIcon = false;
        _loggedMain = false;
        _degraded = false;
        _loggedDetach = false;
        _loggedLatch = false;
        _latchSince = 0f;
    }
}
