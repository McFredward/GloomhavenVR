using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    private static readonly List<WindowPanel> Converted = new(4);

    /// <summary>Open windows whose conversion failed → the screen covers them (retry on re-open).</summary>
    private static readonly List<UIWindow> Failed = new(2);

    private static bool _attached;

    // Per-source live-poll states (separate flags so every transition is attributable).
    private static bool _storyOpen;
    private static bool _levelMsgOpen;
    private static bool _dialogPopupOpen;
    private static bool _lastWant;

    // Modal escape chord state (test #17) — per-press latches, reset on release.
    private static bool _escapeChordFired;
    private static bool _escapeArmingLogged;

    // Full-screen-menu selection guard state (P6 flicker fix) — see ApplyMenuSelectionGuard.
    private static bool _hoverFocusSuppressed;
    private static bool _savedHoverFocus;

    // Issue #9 (multi-highlight): ESC-menu sub-window tabs whose highlight the mod is currently
    // FORCING lit because their window floats in parallel — so it can hand the highlight back to
    // the game the moment that window closes. See SyncEscMenuTabHighlights.
    private static readonly HashSet<UIWindowID> _forcedTabs = new();

    /// <summary>True while the flat screen must show because a fallback window is open.</summary>
    internal static bool ScreenWanted { get; private set; }

    /// <summary>True while at least one fallback window floats as a world-space panel (P8).</summary>
    internal static bool WindowModalActive => Converted.Count > 0;

    /// <summary>
    /// Item 4: true while a BLOCKING modal floats — a converted window that is NOT one of the
    /// player-reachable <see cref="NonBlockingMenus"/> (pause/ESC, Options, Multiplayer,
    /// Compendium…). Those reachable menus float, stay grabbable and carry the X, but must NOT
    /// freeze world interaction: the user keeps grabbing cards / picking board hexes while the
    /// pause menu is open. The ray physics-pick block (RayInteractor.UpdateModalPickBlock) keys on
    /// THIS instead of <see cref="WindowModalActive"/> (which is ANY floated window, and was
    /// wrongly suppressing every board/card/tray pick behind a floating pause menu — the reported
    /// "cards can't be grabbed while the menu is open"). Genuine blockers (story/results/durability)
    /// also assert ModalUI, so the pick-block engages for them through the mode arm regardless — but
    /// this keeps the two consistent and does NOT re-introduce the ModalUI lock for reachable menus.
    /// </summary>
    internal static bool BlockingWindowModalActive
    {
        get
        {
            for (int i = 0; i < Converted.Count; i++)
            {
                UIWindow w = Converted[i].Window;
                if (w != null && !NonBlockingMenus.Contains(w.ID))
                    return true;
            }
            return false;
        }
    }

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
        RestoreMenuSelectionGuard(); // put InControl mouse-hover focus back before we drop the windows
        Core.MixedReality.KeepMenusUnclipped(false); // item 5a: release the backdrop depth override
        ReleaseAllWindows("module shutdown");
        Open.Clear();
        OpenWindows.Clear();
        Failed.Clear();
        _storyOpen = _levelMsgOpen = _dialogPopupOpen = false;
        _lastWant = false;
        _escapeChordFired = false;
        _escapeArmingLogged = false;
        _forcedTabs.Clear();
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
        {
            if (DecisionDock.ClaimsWindow(e.Window))
                VRLog.Info("WorldUI", $"MODAL FALLBACK: window '{e.Window.name}' (ID {e.Id}) opened — " +
                                      "CLAIMED by the control-board decision dock (no float, no ModalUI " +
                                      "while the claim holds; generic fallback resumes if it breaks).");
            else
                VRLog.Info("WorldUI", $"MODAL FALLBACK: window '{e.Window.name}' (ID {e.Id}) opened without a " +
                                      $"VR conversion (scenario={VRModeStateMachine.ScenarioBoardExists}) → " +
                                      "ModalUI + floating window (or screen) while it stays open in a scenario.");
        }
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
            if (window == null)
                continue;
            // Test #21/#22: while the decision dock claims this window, it is NOT
            // part of the generic modal path — no float, no screen, no ModalUI
            // (still tracked in Open: the claim is re-checked every tick, so a
            // broken claim hands the window back here level-triggered).
            if (DecisionDock.ClaimsWindow(window))
                continue;
            OpenWindows.Add(window);
        }
        if (story)
            AddPollWindow(Singleton<StoryController>.Instance.window);
        if (levelMsg && LevelMessagesUIHandler.s_Instance != null)
        {
            AddGroupWindow(LevelMessagesUIHandler.s_Instance.LevelMessageBoxLayoutGroup);
            AddGroupWindow(LevelMessagesUIHandler.s_Instance.LevelMessageHelpTextLayoutGroup);
        }
        // The scenario dialogPopup (burn-confirm / short-rest lose-card confirm) is
        // ID-less — it reaches the generic path ONLY through this poll. Skip it while
        // the decision dock claims it (test #22): its option-button row docks below
        // the cards instead, and standing the generic path down here is what keeps the
        // fan live for the LoseCard follow-up (no ModalUI).
        if (dialog && manager != null && manager.dialogPopup != null
            && !DecisionDock.ClaimsWindow(manager.dialogPopup.Window))
            AddPollWindow(manager.dialogPopup.Window);

        // Item 6 (parallel windows): a STICKY reachable menu stays floated even when the game hid it
        // (its single-window toggle), so it is NOT in OpenWindows. Keep the float wanted while any
        // sticky menu the user has not closed is still alive, or it would be released the moment the
        // game-open set empties (e.g. the toggle hid the only game-open submenu).
        bool stickyAlive = false;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Sticky && !wp.UserClosing && wp.Window != null && wp.Panel.IsAlive)
            {
                stickyAlive = true;
                break;
            }
        }

        bool anyOpen = OpenWindows.Count > 0 || stickyAlive;
        // FLOAT every open modal window into VR (convert + grabbable + X button). This must NOT
        // depend on the ModalUI lock below — coupling them made the pause/Options menu invisible
        // (want=false → convertWanted=false → the window was released to its 2D home, unseen in
        // VR). Floating is purely "is a window open in a scenario".
        bool want = inScenario && anyOpen;

        // Item 3b (user): the ModalUI LOCK is separate. The PLAYER-REACHABLE menus (pause/ESC,
        // Options, Multiplayer, Compendium…) must NOT lock world interaction — the user keeps
        // manipulating the board / cards while the pause menu is open. Lock ONLY when a genuine
        // BLOCKING prompt is open (story, level message, dialog-confirm, results, durability) —
        // i.e. an open window whose ID is NOT one of the reachable menus.
        bool anyBlocking = false;
        for (int i = 0; i < OpenWindows.Count; i++)
        {
            if (!NonBlockingMenus.Contains(OpenWindows[i].ID))
            {
                anyBlocking = true;
                break;
            }
        }
        bool wantLock = want && anyBlocking;

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
            bool alive = convertWanted && wp.Window != null && wp.Panel.IsAlive;
            // Item 6: a sticky reachable menu the user has NOT closed stays floated even when the
            // game hid it (not in OpenWindows) — parallel windows. Every other window releases as
            // soon as it leaves the open set (or convert is no longer wanted, or the user closed it).
            bool stillOpen = alive && !wp.UserClosing
                             && (ContainsWindow(OpenWindows, wp.Window!) || wp.Sticky);
            if (stillOpen)
                continue;
            // FIX B gap-close: the user closed this float (UserClosing) but the window reports
            // OPEN again — something re-showed it between the X-close and this release tick
            // (e.g. ESCMenu.OnControllerAreaFocused calls myWindow.Show() when unfocused-closed).
            // The user's close intent wins: hide it again so the release below restores a
            // genuinely CLOSED window (CanvasConversion.Release then forces its 2D canvas
            // hidden — without this, the restored window would render at its screen-space home).
            if (wp.UserClosing && wp.Window != null && wp.Window.IsOpen)
            {
                wp.Window.Hide();
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{wp.Window.name}' was re-shown between the user " +
                                      "close and the release tick — re-hidden (user close wins).");
            }
            // Item 6: if we force-showed a sticky menu whose game state is Hidden, reset its
            // CanvasGroup back to that hidden state before releasing so the 2D restore is clean.
            if (wp.Sticky && wp.WindowCanvasGroup != null && wp.Window != null && !wp.Window.IsOpen)
            {
                wp.WindowCanvasGroup.alpha = 0f;
                wp.WindowCanvasGroup.blocksRaycasts = false;
                wp.WindowCanvasGroup.interactable = false;
            }
            Converted.RemoveAt(i);
            string name = wp.Window != null ? wp.Window.name : "<destroyed>";
            wp.Grab?.Destroy(); // drop the mod-owned grab holder (sub-item B) before releasing the host
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

        // 4b. Item 6 (parallel windows): re-assert visibility on a sticky menu the GAME hid (its
        //     single-window toggle). The window was reparented into our host, so forcing its
        //     CanvasGroup back to alpha 1 + raycast-enabled keeps it visible AND clickable in VR
        //     while the game considers it Hidden — this is what lets Options and Multiplayer float
        //     in parallel. Change-gated writes; only runs while the game state is hidden/faded.
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!wp.Sticky || wp.UserClosing || wp.Window == null || !wp.Panel.IsAlive)
                continue;
            if (!wp.Window.IsOpen || !wp.Window.IsVisible)
                ReassertStickyVisible(wp);
        }

        // 5. Sub-item B: the game-owned host follows its mod-owned grab frame every tick
        //    (static while ungripped; moved/scaled by the shared PanelGrabHandle while a hand
        //    grips the bar). Every floated modal is grabbable now, Sieg/Niederlage included.
        for (int i = 0; i < Converted.Count; i++)
            Converted[i].Grab?.Tick();

        // 5a-scroll. User #12: thumbstick-Y scrolls the Sieg/Niederlage results window's
        //    scroll area while a laser/poke hovers ANYWHERE on the floated window — the
        //    generic RayUguiDriver stick-scroll only fires when the hover raycast lands
        //    INSIDE a ScrollRect subtree, which the results list misses (its rows carry no
        //    raycast targets, so the hover lands on the window frame outside the viewport).
        TickResultsStickScroll();

        // 5b. Item 1 (pause-menu size) + issue #1 (confirmations): once a ONE-SHOT content fit has
        //     shrunk the host rect from the full window (1920x…) to the visible button/dialog
        //     bounds, re-derive its board-relative scale from the FITTED width and push it to the
        //     grab — the scale first derived at Convert used the pre-fit rect, so the fitted panel
        //     would otherwise render mis-sized (a full-window-derived scale on a shrunk host renders
        //     tiny). Gated on OneShotFitted so it covers BOTH the ESC menu and the pause/options
        //     confirmation dialogs, and never fires for non-one-shot modals whose default per-frame
        //     fit also flips FitOneShotApplied. Runs once per open (ScaleReDerived latch).
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.ScaleReDerived || !wp.OneShotFitted || wp.Grab == null
                || !wp.Panel.IsAlive || !wp.Panel.FitOneShotApplied)
                continue;
            float refit = DeriveWindowScale(wp.Panel);
            wp.ExtraScale = refit;
            wp.Grab.SetExtraScale(refit);
            wp.ScaleReDerived = true;
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{(wp.Window != null ? wp.Window.name : "<menu>")}' " +
                                  $"re-scaled to the fitted content (extraScale → {refit:F3}) — board-sized, " +
                                  "compact, consistent every open.");
        }

        // 5c. LOST-MENU RECALL (incident fix): a floated STICKY full-screen menu whose game
        //     window is still OPEN blocks card/board input BY DESIGN — so it must never be
        //     lost off-view (the user laser-carried the ESC menu away, it drifted out of
        //     sight, and every later trigger aimed at the cards hit its grab zone: the whole
        //     session read as "cards can't be picked up" with no visible reason). Runs after
        //     the grab follow so it sees the final host pose of this tick.
        TickMenuRecall();

        // Issue #9 (multi-highlight): mark EVERY parallel-open sub-window's ESC-menu tab, not just
        // the single one the game's single-select toggle group leaves 'on'. Runs after the
        // release/convert loops so Converted reflects exactly which windows float this tick.
        SyncEscMenuTabHighlights();

        // Item 1a (revert): menus render with NORMAL ZTest again (no on-top treatment), so the
        // sky/backdrop must be made non-occluding for floated menus to stay visible at the shell
        // edge. That is a SEPARATE change owned by Core.MixedReality; this call signals it when any
        // menu floats. Hands/board still occlude the menu (ZTest LEqual), as the user wants.
        Core.MixedReality.KeepMenusUnclipped(Converted.Count > 0);

        // (Content fitting — test #13/#14 — is centralized in CanvasConversion.Tick:
        // every pokeable host is fitted after the show animation and periodically
        // re-fitted on content growth.)

        ApplyMenuSelectionGuard(); // P6: stop the gamepad-nav highlight flicker on a floated full-screen menu

        TickEscapeChord(); // test #17: floating modals must always be closable

        // The ModalUI LOCK tracks wantLock (blocking prompts only), NOT want (float) — item 3b.
        if (wantLock != _lastWant)
        {
            _lastWant = wantLock;
            VRLog.Info("WorldUI", $"MODAL FALLBACK {(wantLock ? "ASSERTED" : "RELEASED")}: " +
                                  $"windows={OpenWindows.Count}, story={story}, levelMsg={levelMsg}, " +
                                  $"dialogPopup={dialog}, scenario={inScenario}, blocking={anyBlocking}, " +
                                  $"style={(WorldUIConfig.ModalWindowStyle ? "window" : "screen")}, " +
                                  $"mode={VRModeStateMachine.CurrentMode} → " +
                                  $"{(wantLock ? "ModalUI" : "released (menus stay interactive)")}.");
        }

        // Screen policy: full composite for style=screen; for style=window only the
        // windows that FAILED to convert raise it (per-window automatic fallback).
        // The manual chord path forces the screen inside FlatScreen regardless.
        ScreenWanted = wantLock && (!WorldUIConfig.ModalWindowStyle || Failed.Count > 0);
        VRModeStateMachine.SetAuxModal(wantLock); // ModalUI only for genuine blockers (item 3b)
    }
}
