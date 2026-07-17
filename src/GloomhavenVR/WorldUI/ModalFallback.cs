using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// CATCH-ALL MODAL FALLBACK (P6, hardware test #8 critical fix; P8 window-style
/// rework, test #12): during a scenario the game opens 2D windows — events,
/// tutorials, take-damage choices, rewards, the ESC menu, confirmation variants not
/// owned by the P3c <see cref="Surfaces.DialogSurface"/> — that are INVISIBLE in VR
/// (the head camera never renders screen-space UI). The game then waits for a click
/// the player cannot give: a deadlock.
///
/// Detection, three layers (whichever fires first wins; when in doubt the window shows):
/// 1. <see cref="VREvents.WindowVisibility"/> — every <c>UIWindow</c> transition (single
///    choke-point patch, see <c>UIWindow_Transition_Patch</c>) — matched against the
///    <see cref="FallbackIds"/> set of in-scenario interactive/blocking window IDs.
/// 2. Live polls for the ID-less deadlockers whose <c>UIWindowID</c> is scene-serialized
///    (not visible in code): <c>StoryController.IsVisible</c> (the scenario story/subtitle
///    box 'UI Story Box' — decompiled StoryController.cs:85; while shown it blocks the
///    game via AddUpdateBlocker/LockProcessingAction, StoryController.cs:216-217 — the
///    hardware-test-#10 scenario-start lock), <c>LevelMessageUILayoutGroup.IsShown</c>
///    (static; tutorial/level messages incl. 'UILevelMessageBoxFixed' — decompiled
///    LevelMessageUILayoutGroup.cs:33) and <c>UIManager.dialogPopup.IsOpen()</c>
///    (scenario choice dialogs — decompiled DialogPopup.cs:426, UIManager.cs:94).
///    Polls are level-triggered per frame, so they also cover windows that opened
///    during loading, before the mode machine settled. Every poll source resolves to
///    a concrete <c>UIWindow</c> instance for the window-style conversion:
///    StoryController's serialized <c>window</c> field (StoryController.cs:65-66),
///    the layout groups' <c>window</c> (GetComponent in Awake,
///    LevelMessageUILayoutGroup.cs:8/37, reached via the public
///    <c>LevelMessagesUIHandler.s_Instance.LevelMessage{Box,HelpText}LayoutGroup</c>
///    fields, LevelMessagesUIHandler.cs:25-29) and <c>DialogPopup.Window</c>
///    (public property, DialogPopup.cs:96).
/// 3. The pre-existing <c>UIManager.ToggleLockUI</c> observation (UiLockChanged →
///    ModalUI) still covers everything that locks the UI outright.
///
/// While any of these hold in a scenario, this class asserts
/// <see cref="VRModeStateMachine.SetAuxModal"/> (mode → ModalUI) and makes the window
/// operable, per <c>[WorldUI] ModalStyle</c>:
///
/// - "window" (default, P8): each open fallback window's root RectTransform is moved
///   onto a world-space host via <see cref="CanvasConversion"/> and floated in front
///   of the HMD at reading distance (~1.2 m, the DialogSurface pattern). The host is
///   registered with UguiPokeSurfaces, so the fingertip poke AND the dominant-hand
///   laser (RayUguiDriver) click the REAL uGUI elements — e.g. the story box's
///   full-area skip button ('UI Story Box' carried the IPointerClickHandler in the
///   test-#10 DirectClick log; UICharacterStoryBox.skipButton → Skip(),
///   UICharacterStoryBox.cs:44/81). On close/scene change/hot reload the window is
///   restored to its exact 2D home (CanvasConversion restore records). If a specific
///   window FAILS to convert, the reason is logged and the full flat screen rises
///   for it instead (automatic per-window fallback).
/// - "screen": pre-P8 behavior — <see cref="ScreenWanted"/> shows the full 2D
///   composite (<see cref="FlatScreen"/>) while any fallback window is open.
///
/// The manual chord ([WorldUI] ManualScreenChord, <see cref="FlatScreen"/>) stays the
/// universal rescue: while it forces the screen, all window conversions are RELEASED
/// (a converted window would be missing from the screen's RT composite) and re-applied
/// when the chord toggles the screen off again.
///
/// CONVERTED vs FALLBACK window sets — see docs/TESTING-P3C.md (P6 section). Converted/
/// passive windows (ConfirmationBox → DialogSurface, ActorStatPanel /
/// EnemyCurrentTurnStatPanel → StatPanelSurface, CombatLog panel, CardHolder → Cards
/// module, QuestTracker/MapObjectiveManager HUD, hover popups TrapInfoPanel /
/// DoorInfoPanel / MapNodeInfoPanel, and the passive HelpBox hint strip — test #16)
/// deliberately do NOT trigger the fallback.
/// </summary>
internal static class ModalFallback
{
    /// <summary>Floating-window distance in front of the HMD, real meters (reading distance).</summary>
    private const float WindowDistanceMeters = 1.2f;

    /// <summary>Extra shrink on the host scale (a full-screen-wide window subtends ~60° at 1.2 m).</summary>
    private const float WindowScaleFactor = 0.7f;

    /// <summary>
    /// Window IDs that demand user interaction when opened during a scenario and have
    /// no world-space VR conversion → they float as windows (or raise the screen).
    /// IDs verified against decompiled GH.Runtime/UIWindowID.cs (45 members).
    /// </summary>
    private static readonly HashSet<UIWindowID> FallbackIds = new()
    {
        // Scenario flow blockers (story/event/tutorial/choice popups).
        UIWindowID.EventsPanel,
        UIWindowID.Message,
        // NOT UIWindowID.HelpBox (test #16 root cause of the dead card fan): the
        // HelpBox is the game's PASSIVE bottom hint strip (HelpBoxLine tooltips —
        // GUI_TOOLTIP_*, controller tips, HighlightWarning; a ~478x32 px line),
        // never interactive and never blocking. The game showed it right after hero
        // placement and it stays open indefinitely — listed here it held the mode
        // machine in ModalUI for the rest of the session: palm gate disabled (fan
        // could never open), board pick inactive (clicks dead). Passive HUD → no
        // fallback, no ModalUI.
        UIWindowID.TextInfoPanel,
        UIWindowID.IntroductionScreen,
        UIWindowID.RewardsPanel,
        UIWindowID.ResultsPanel,
        UIWindowID.TakeDamagePanel,
        UIWindowID.DurabilityPanel,
        UIWindowID.QuestPopup,
        UIWindowID.UnlockQuestPopup,
        UIWindowID.AdventureCompletionPanel,
        UIWindowID.HeroLevelUpPanel,
        // Menus reachable mid-scenario.
        UIWindowID.ESCMenu,
        UIWindowID.Options,
        UIWindowID.OptionsSubmenu,
        UIWindowID.ViceOptionsSubmenu,
        UIWindowID.DifficultyPanel,
        UIWindowID.CompendiumPanel,
        UIWindowID.PartyPanel,
        UIWindowID.EquipmentItemsPanel,
        // Unconverted confirmation variants + multiplayer panels.
        UIWindowID.MutiplayerConfirmationBox,
        UIWindowID.CharacterConfirmationBox,
        UIWindowID.MainMenuConfirmationBox,
        UIWindowID.MutiplayerHeroAssignPanel,
        UIWindowID.MutiplayerPlayerPicker,
        UIWindowID.MultiplayerFriendList,
    };

    /// <summary>Fallback windows currently open (tracked instances; pruned per tick).</summary>
    private static readonly HashSet<UIWindow> Open = new();
    private static readonly List<UIWindow> Scratch = new(8);

    /// <summary>All open modal windows this tick (ID-tracked + poll sources), rebuilt per tick.</summary>
    private static readonly List<UIWindow> OpenWindows = new(8);

    /// <summary>
    /// One floated window: the game window + its world-space host. Content fitting
    /// (test #13/#14) is centralized in <see cref="CanvasConversion"/> — every
    /// pokeable host is fitted and growth-re-fitted there; the story window's
    /// narrower content root is passed via <see cref="ConvertedPanel.FitContentRoot"/>.
    /// </summary>
    private sealed class WindowPanel
    {
        public UIWindow Window = null!;
        public ConvertedPanel Panel = null!;
    }

    private static readonly List<WindowPanel> Converted = new(4);

    /// <summary>Open windows whose conversion failed → the screen covers them (retry on re-open).</summary>
    private static readonly List<UIWindow> Failed = new(2);

    private static bool _attached;

    // Per-source live-poll states (separate flags so every transition is attributable).
    private static bool _storyOpen;
    private static bool _levelMsgOpen;
    private static bool _dialogPopupOpen;
    private static bool _lastWant;

    /// <summary>True while the flat screen must show because a fallback window is open.</summary>
    internal static bool ScreenWanted { get; private set; }

    /// <summary>True while at least one fallback window floats as a world-space panel (P8).</summary>
    internal static bool WindowModalActive => Converted.Count > 0;

    internal static void Attach()
    {
        if (_attached)
            return;
        _attached = true;
        VREvents.WindowVisibility += OnWindow;
    }

    internal static void Detach()
    {
        if (!_attached)
            return;
        _attached = false;
        VREvents.WindowVisibility -= OnWindow;
        ReleaseAllWindows("module shutdown");
        Open.Clear();
        OpenWindows.Clear();
        Failed.Clear();
        _storyOpen = _levelMsgOpen = _dialogPopupOpen = false;
        _lastWant = false;
        ScreenWanted = false;
        VRModeStateMachine.SetAuxModal(false);
    }

    private static void OnWindow(WindowVisibilityEvent e)
    {
        // Test #10: EVERY window transition is logged at Debug — a lock caused by a
        // window this class does not know is then attributable from the log alone.
        VRLog.Debug("WorldUI", $"UIWindow {(e.Shown ? "SHOWN" : "hidden")}: '{e.Window?.name}' " +
                               $"(ID {e.Id}, scenario={VRModeStateMachine.ScenarioBoardExists}, " +
                               $"mode={VRModeStateMachine.CurrentMode}).");

        if (e.Window == null)
            return;
        if (!e.Shown)
        {
            if (Open.Remove(e.Window))
                VRLog.Info("WorldUI", $"Modal fallback: window '{e.Window.name}' (ID {e.Id}) hidden — untracked.");
            return;
        }
        if (!IsFallbackWindow(e.Id))
            return;
        // Test #10: track fallback windows EVEN outside a scenario. The scenario-start
        // story/intro windows open during loading, BEFORE the mode machine's scenario
        // signal (Choreographer alive) settles — an edge-triggered event gated on
        // ScenarioBoardExists lost them forever, and the screen never rose. Tracking is
        // unconditional; the SCENARIO gate is applied level-triggered in Tick() (pre-
        // scenario Menu2D auto-shows the full screen anyway, so want stays false there).
        if (Open.Add(e.Window))
            VRLog.Info("WorldUI", $"MODAL FALLBACK: window '{e.Window.name}' (ID {e.Id}) opened without a " +
                                  $"VR conversion (scenario={VRModeStateMachine.ScenarioBoardExists}) → " +
                                  "ModalUI + floating window (or screen) while it stays open in a scenario.");
    }

    private static bool IsFallbackWindow(UIWindowID id)
    {
        // ConfirmationBox is normally physicalized by DialogSurface — it needs the
        // fallback only when that surface is switched off.
        if (id == UIWindowID.ConfirmationBox)
            return !WorldUIConfig.Dialogs.Value;
        return FallbackIds.Contains(id);
    }

    /// <summary>
    /// Per-frame service (WorldUI driver): prune dead/closed windows, poll the ID-less
    /// deadlockers, maintain the window conversions, drive the aux-modal mode input.
    /// Allocation-free steady state (WindowPanel records allocate only when a window
    /// is first converted — a rare event).
    /// </summary>
    internal static void Tick()
    {
        bool inScenario = VRModeStateMachine.ScenarioBoardExists;

        // Prune: scene unloads / ForceHideWindows can close windows without a clean
        // transition reaching us (window destroyed → no event). IsOpen is the game's
        // own state (UIWindow.cs:317). Deliberately NOT cleared outside scenarios
        // (test #10): windows opened during the loading transition must survive into
        // the scenario, where they become the lock.
        if (Open.Count > 0)
        {
            Scratch.Clear();
            foreach (UIWindow window in Open)
            {
                if (window == null || !window.IsOpen)
                    Scratch.Add(window!);
            }
            for (int i = 0; i < Scratch.Count; i++)
            {
                UIWindow window = Scratch[i];
                Open.Remove(window);
                if (window != null)
                    VRLog.Info("WorldUI", $"Modal fallback: window '{window.name}' (ID {window.ID}) " +
                                          "closed — fallback released.");
            }
            Scratch.Clear();
        }

        // ID-less deadlockers (scene-serialized UIWindowIDs) — cheap live polls, level-
        // triggered every frame so they cover the loading/early-scenario transition too:
        //
        // 1. StoryController.IsVisible (test #10 — THE scenario-start lock: the story/
        //    subtitle box "UI Story Box" on 'Story Canvas'. Decompiled StoryController.cs:85
        //    `public bool IsVisible => window.IsVisible`; while shown it BLOCKS the game:
        //    StoryController.cs:216-217 AddUpdateBlocker + LockProcessingAction — invisible
        //    in VR = hard deadlock).
        // 2. LevelMessageUILayoutGroup.IsShown (static; tutorial/level messages incl. the
        //    'UILevelMessageBoxFixed' box — decompiled LevelMessageUILayoutGroup.cs:33).
        // 3. UIManager.dialogPopup.IsOpen() (scenario choice dialogs — DialogPopup.cs:426).
        bool story = false, levelMsg = false, dialog = false;
        UIManager? manager = null;
        if (WorldUIConfig.ConversionActive)
        {
            story = Singleton<StoryController>.IsInitialized
                    && Singleton<StoryController>.Instance.IsVisible;
            levelMsg = LevelMessageUILayoutGroup.IsShown;
            manager = UIManager.Instance;
            dialog = manager != null && manager.dialogPopup != null && manager.dialogPopup.IsOpen();
        }
        LogPollTransition(ref _storyOpen, story, "story box (StoryController.IsVisible)");
        LogPollTransition(ref _levelMsgOpen, levelMsg, "level message (LevelMessageUILayoutGroup.IsShown)");
        LogPollTransition(ref _dialogPopupOpen, dialog, "dialog popup (UIManager.dialogPopup.IsOpen)");

        // Normalize every source to concrete UIWindow instances (P8): the ID-tracked
        // set plus the poll sources' serialized windows (see class doc for the
        // decompiled field/property citations).
        OpenWindows.Clear();
        foreach (UIWindow window in Open)
        {
            if (window != null)
                OpenWindows.Add(window);
        }
        if (story)
            AddPollWindow(Singleton<StoryController>.Instance.window);
        if (levelMsg && LevelMessagesUIHandler.s_Instance != null)
        {
            AddGroupWindow(LevelMessagesUIHandler.s_Instance.LevelMessageBoxLayoutGroup);
            AddGroupWindow(LevelMessagesUIHandler.s_Instance.LevelMessageHelpTextLayoutGroup);
        }
        if (dialog && manager != null && manager.dialogPopup != null)
            AddPollWindow(manager.dialogPopup.Window);

        bool anyOpen = OpenWindows.Count > 0;
        bool want = inScenario && anyOpen;

        // ---- window-style conversions (P8) ------------------------------------------
        // The manual chord's full screen needs the windows back in the 2D composite;
        // style=screen and scenario exit release everything too.
        bool convertWanted = want && WorldUIConfig.ModalWindowStyle
                             && !FlatScreen.ManualScreenActive
                             && WorldUIConfig.ConversionActive;

        // 1. Release conversions whose window closed/died or that are no longer wanted.
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            bool stillOpen = convertWanted && wp.Window != null && wp.Panel.IsAlive
                             && ContainsWindow(OpenWindows, wp.Window);
            if (stillOpen)
                continue;
            Converted.RemoveAt(i);
            string name = wp.Window != null ? wp.Window.name : "<destroyed>";
            CanvasConversion.Release(wp.Panel); // restores the exact 2D home
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' released — restored to its 2D home " +
                                  $"(open={wp.Window != null && wp.Window.IsOpen}, " +
                                  $"convertWanted={convertWanted}).");
        }

        // 2. Failed windows retry only after a close/re-open (no per-frame spam).
        for (int i = Failed.Count - 1; i >= 0; i--)
        {
            if (Failed[i] == null || !ContainsWindow(OpenWindows, Failed[i]))
                Failed.RemoveAt(i);
        }

        // 3. Convert newly opened windows.
        if (convertWanted)
        {
            for (int i = 0; i < OpenWindows.Count; i++)
            {
                UIWindow window = OpenWindows[i];
                if (IsConverted(window) || ContainsWindow(Failed, window))
                    continue;
                if (!TryConvertWindow(window))
                    Failed.Add(window);
            }
        }

        // 4. Keep the floating modal clickable: the game's UI lock legitimately
        //    disables all host raycasters (CanvasConversion lock mirror), but the
        //    modal window is the one surface that must accept input while modal —
        //    same exemption the DialogSurface applies.
        for (int i = 0; i < Converted.Count; i++)
        {
            GraphicRaycaster raycaster = Converted[i].Panel.HostRaycaster;
            if (raycaster != null && !raycaster.enabled)
                raycaster.enabled = true;
        }

        // (Content fitting — test #13/#14 — is centralized in CanvasConversion.Tick:
        // every pokeable host is fitted after the show animation and periodically
        // re-fitted on content growth.)

        if (want != _lastWant)
        {
            _lastWant = want;
            VRLog.Info("WorldUI", $"MODAL FALLBACK {(want ? "ASSERTED" : "RELEASED")}: " +
                                  $"windows={OpenWindows.Count}, story={story}, levelMsg={levelMsg}, " +
                                  $"dialogPopup={dialog}, scenario={inScenario}, " +
                                  $"style={(WorldUIConfig.ModalWindowStyle ? "window" : "screen")}, " +
                                  $"mode={VRModeStateMachine.CurrentMode} → " +
                                  $"{(want ? "ModalUI" : "released")}.");
        }

        // Screen policy: full composite for style=screen; for style=window only the
        // windows that FAILED to convert raise it (per-window automatic fallback).
        // The manual chord path forces the screen inside FlatScreen regardless.
        ScreenWanted = want && (!WorldUIConfig.ModalWindowStyle || Failed.Count > 0);
        VRModeStateMachine.SetAuxModal(want); // idempotent — mode flow unchanged by style
    }

    // ---- window gathering helpers (allocation-free) -------------------------------------

    private static void AddPollWindow(UIWindow? window)
    {
        if (window == null || ContainsWindow(OpenWindows, window))
            return;
        OpenWindows.Add(window);
    }

    /// <summary>Add a level-message group's UIWindow when that group is the visible one.</summary>
    private static void AddGroupWindow(LevelMessageUILayoutGroup? group)
    {
        if (group == null)
            return;
        // `window` = GetComponent<UIWindow>() in Awake (LevelMessageUILayoutGroup.cs:37,
        // RequireComponent :8); publicized field.
        UIWindow? window = group.window;
        if (window != null && (window.IsVisible || window.IsOpen))
            AddPollWindow(window);
    }

    private static bool ContainsWindow(List<UIWindow> list, UIWindow window)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], window))
                return true;
        }
        return false;
    }

    private static bool IsConverted(UIWindow window)
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            if (ReferenceEquals(Converted[i].Window, window))
                return true;
        }
        return false;
    }

    // ---- conversion ---------------------------------------------------------------------

    /// <summary>
    /// Float one fallback window in front of the HMD. Returns false (with the reason
    /// logged) when the window cannot be converted — the caller then raises the full
    /// flat screen for it instead.
    /// </summary>
    private static bool TryConvertWindow(UIWindow window)
    {
        string name = window.name;
        try
        {
            var rect = window.transform as RectTransform;
            if (rect == null)
            {
                VRLog.Warn("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) has no RectTransform " +
                                      "root — cannot float it, falling back to the full flat screen.");
                return false;
            }

            // Story-box sanity: the click-to-advance UICharacterStoryBox (serialized
            // separately from the window, StoryController.cs:62-66) must live INSIDE
            // the converted subtree, or the floating panel could never advance the
            // story. Same for its skip button (UICharacterStoryBox.cs:44). While
            // here, remember it as the content root for the host-rect fit — the
            // story window root is a full-screen 1920x1080 stretch rect, the visible
            // dialog is the UICharacterStoryBox subtree.
            RectTransform? contentRoot = null;
            if (Singleton<StoryController>.IsInitialized)
            {
                StoryController sc = Singleton<StoryController>.Instance;
                if (sc != null && ReferenceEquals(sc.window, window) && sc.dialogBox != null)
                {
                    if (!sc.dialogBox.transform.IsChildOf(window.transform))
                    {
                        VRLog.Warn("WorldUI", "MODAL WINDOW: story box dialog (UICharacterStoryBox) is NOT " +
                                              "under the story window root — clicking the floating panel could " +
                                              "not advance the story; falling back to the full flat screen.");
                        return false;
                    }
                    contentRoot = sc.dialogBox.transform as RectTransform;
                }
            }

            ConvertedPanel? panel = CanvasConversion.Convert(rect, $"Modal_{name}", pokeable: true);
            if (panel == null)
            {
                VRLog.Warn("WorldUI", $"MODAL WINDOW: conversion of '{name}' (ID {window.ID}) returned " +
                                      "null (target destroyed?) — falling back to the full flat screen.");
                return false;
            }

            PlaceAtHmd(panel);
            // Narrower measure root for the content fit (story window: the visible
            // UICharacterStoryBox, not the 1920x1080 stretch root). The fit itself
            // runs centrally in CanvasConversion.Tick (test #14 item 1).
            panel.FitContentRoot = contentRoot;
            Converted.Add(new WindowPanel
            {
                Window = window,
                Panel = panel,
            });
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' (ID {window.ID}) floated in front of the HMD " +
                                  $"({WindowDistanceMeters:F1} m, poke + laser clickable) — " +
                                  "restored to 2D when it closes.");
            return true;
        }
        catch (Exception ex)
        {
            VRLog.Error("WorldUI", $"MODAL WINDOW: conversion of '{name}' FAILED " +
                                   $"({ex.GetType().Name}: {ex.Message}) — falling back to the full " +
                                   "flat screen for this window.");
            return false;
        }
    }

    /// <summary>
    /// Presence-regained recovery (test #17): re-place every floating modal window in
    /// front of the CURRENT head pose. The user may have physically moved while the
    /// HMD was off — a modal stranded out of view is an un-dismissable lock. Returns
    /// how many windows were re-floated (for the "[Core] Session resumed" report).
    /// </summary>
    internal static int RefloatOpenWindows()
    {
        int count = 0;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!wp.Panel.IsAlive)
                continue;
            PlaceAtHmd(wp.Panel);
            count++;
        }
        return count;
    }

    /// <summary>HMD-anchored placement at reading distance (DialogSurface pattern).</summary>
    private static void PlaceAtHmd(ConvertedPanel panel)
    {
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        Vector3 pos = h.position + fwd * (WindowDistanceMeters * scale);
        // Canvas front faces -forward: point +Z away from the viewer.
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        CanvasConversion.PlaceHost(panel, pos, rot, scale * WindowScaleFactor);
    }

    private static void ReleaseAllWindows(string reason)
    {
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            string name = wp.Window != null ? wp.Window.name : "<destroyed>";
            CanvasConversion.Release(wp.Panel);
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' released ({reason}) — restored to its 2D home.");
        }
        Converted.Clear();
    }

    private static void LogPollTransition(ref bool state, bool now, string what)
    {
        if (now == state)
            return;
        state = now;
        VRLog.Info("WorldUI", now
            ? $"MODAL FALLBACK poll: {what} OPEN → ModalUI (floating window or screen) until it closes."
            : $"Modal fallback poll: {what} closed.");
    }
}
