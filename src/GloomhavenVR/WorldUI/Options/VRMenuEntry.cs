using System;
using System.Collections.Generic;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

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
/// tab: a row here must join the same <c>ToggleGroup</c> (so picking it deselects the others),
/// animate like its neighbours, carry the same hover and focus wiring, and be reachable by the
/// menu's gamepad navigation. All of that lives in serialized references inside the shipped prefab,
/// so each row is INSTANTIATED FROM A LIVE DONOR and re-labelled. Nothing about a donor is
/// modified, and each clone is inserted directly after it so the two read as a pair.</para>
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
                if (!VROptionsTab.Open(clone.Deselect, clone.transform as RectTransform))
                {
                    // Nothing to show: hand the menu straight back rather than leaving a lit row
                    // over an empty screen.
                    SetFocused(host, true);
                    clone.Deselect();
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
                if (!VROptionsTab.Open(clone.Deselect, clone.transform as RectTransform))
                {
                    SetMainFocused(menu, true);
                    clone.Deselect();
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
        return clone;
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
    }
}
