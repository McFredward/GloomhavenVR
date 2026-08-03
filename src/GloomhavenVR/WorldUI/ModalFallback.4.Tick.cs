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

    /// <summary>
    /// The world-space host of the floated FULL-SCREEN MENU (pause / options family), or null when
    /// no such menu is floating.
    ///
    /// <para>Asked by <see cref="WorldTooltips"/>, which has to know not whether a menu is open but
    /// WHERE it currently lives: in a scenario the pause menu is not on the flat screen, it is one
    /// of these floated panels on the mod's own layer — so a tooltip left in screen space renders
    /// into a screen nobody is being shown.</para>
    ///
    /// <para>The host carries the window's full screen rect converted to world space, so aligning
    /// another screen-space canvas to this transform makes the two coincide.</para>
    ///
    /// <para>TOPMOST, NOT FIRST (hover-hint bug): the menu family STACKS — opening Options from the
    /// pause menu floats a SECOND full-screen panel (log: 'Modal_UI Scenario Esc Menu' converted,
    /// then 'Modal_UI Options Window_unified') while the ESC root keeps floating in parallel
    /// (item 6, reachable menus are not hidden by the game's single-window toggle). The hover the
    /// tooltip answers can only have come from the window the player is actually looking at, which
    /// is the LAST one floated — but this scanned FORWARD and always returned the ESC root, so the
    /// hint was laid on the root's plane: at the root's height/rotation, and behind the Options
    /// window that floats in front of it. <see cref="Converted"/> is append-ordered (Convert adds,
    /// Release removes), so scanning BACKWARD yields the most recently floated menu and falls back
    /// to the ESC root by itself the moment Options closes.</para>
    /// </summary>
    internal static ConvertedPanel? MenuPanel
    {
        get
        {
            for (int i = Converted.Count - 1; i >= 0; i--)
            {
                WindowPanel w = Converted[i];
                if (w.FullScreenMenu && w.Panel != null && w.Panel.IsAlive && w.Panel.HostRect != null)
                    return w.Panel;
            }
            return null;
        }
    }

    /// <summary>World-space host rect of <see cref="MenuPanel"/> (null when no menu floats).</summary>
    internal static RectTransform? MenuPanelHost => MenuPanel?.HostRect;

    /// <summary>
    /// The floated window that OWNS <paramref name="hovered"/> — the converted panel whose world
    /// host is an ANCESTOR of that transform — or null when the hovered thing lives outside every
    /// floated window.
    ///
    /// <para>WHAT IT ANSWERS. There is exactly ONE tooltip box in the game
    /// (<c>CanvasManager.tooltipCanvas</c>), so <see cref="WorldTooltips"/> has to decide per hover
    /// WHERE to park it. The deciding question is ownership: the game's <c>UITooltipTarget</c>
    /// anchors the box to the RectTransform it was entered on
    /// (<c>UITooltipTarget.PrepareTooltip</c> → <c>UITooltip.AnchorToRect(base.transform, …)</c>,
    /// decompiled UITooltipTarget.cs:139), and a converted window OWNS its content by construction:
    /// <c>CanvasConversion.Convert</c> re-parents the game rect under the mod-owned host
    /// (<c>target.SetParent(hostRect, …)</c>, CanvasConversion.1.Core.cs:221). So "does this hover
    /// belong to a floated window" is a walk up the parent chain, not a guess — and it is the same
    /// answer for the whole subtree, however deeply pooled.</para>
    ///
    /// <para>ROOT CAUSE THIS REPLACES (user report 2026-08-03: "Nach dem letzten Fix ist ersteres
    /// (Hints der Karten) nicht mehr der Fall"). The previous round fixed the item-unlock window's
    /// tooltips ("sind 3D mit einem Winkel durch das Fenster") with a TOPMOST rule: a
    /// <c>TooltipHostPanel</c> property that scanned <see cref="Converted"/> BACKWARD and returned
    /// the last floated panel of any kind. Topmost is not ownership. A scripted level message, a
    /// tutorial box or the item-unlock window floats for minutes while the player keeps hovering
    /// CARDS lying on the control board and the decision buttons docked under it — and every one of
    /// those hints was then laid onto the unrelated window, at the window's angle, wherever the
    /// window happened to have been carried. The ownership test keeps the item-unlock fix (a hover
    /// INSIDE that window still resolves to it) and gives the board its hints back (a hover outside
    /// every floated host resolves to null, i.e. the board anchor).</para>
    ///
    /// <para>Cost: one parent walk per shown tooltip per frame, over a hierarchy depth, times the
    /// floated-window count (0–3 in practice; the loop is skipped entirely while nothing floats).
    /// Allocation-free.</para>
    /// </summary>
    internal static ConvertedPanel? FindOwningWindow(Transform? hovered)
    {
        if (hovered == null || Converted.Count == 0)
            return null;
        for (Transform? node = hovered; node != null; node = node.parent)
        {
            // Backward, so a window floated ON TOP of another (Options over the ESC root) wins the
            // tie when one host is nested inside the other's subtree — the same argument
            // MenuPanel makes, now applied only among the windows that actually CONTAIN the hover.
            for (int i = Converted.Count - 1; i >= 0; i--)
            {
                ConvertedPanel panel = Converted[i].Panel;
                if (panel != null && panel.IsAlive && panel.HostRect != null
                    && ReferenceEquals(panel.HostRect, node))
                    return panel;
            }
        }
        return null;
    }

    /// <summary>Open windows whose conversion failed → the screen covers them (retry on re-open).</summary>
    private static readonly List<UIWindow> Failed = new(2);

    private static bool _attached;

    // Per-source live-poll states (separate flags so every transition is attributable).
    private static bool _storyOpen;
    private static bool _levelMsgOpen;
    private static bool _dialogPopupOpen;
    private static bool _lastWant;

    /// <summary>Tutorial deadlock #2 (TB_2_1→TB_2_2 handover, hardware log 2026-08-02):
    /// consecutive closed ticks the level-message poll must see before a close is trusted —
    /// a 1-frame flicker during the game's dismiss→show handover must never release the
    /// float (mirrors the catch-all grace pattern, <see cref="CatchAllGraceTicks"/>).</summary>
    private const int LevelMsgCloseGraceTicks = 3;

    /// <summary>Consecutive ticks the level-message poll has read closed (see above).</summary>
    private static int _levelMsgClosedTicks;

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
    /// pause menu is open. The COMMIT-suppression layer (RayInteractor.UpdateCommitSuppression +
    /// the Cards driver's card/tray commit gate) keys on THIS instead of
    /// <see cref="WindowModalActive"/> (which is ANY floated window, and was wrongly suppressing
    /// every board/card/tray pick behind a floating pause menu — the reported "cards can't be
    /// grabbed while the menu is open"). Since the 2026-08 laser ruling the beam, its physics
    /// collision and hover are NEVER gated by this — only commits are (decision table on
    /// <see cref="HardCommitLockActive"/>).
    /// Tutorial deadlock #3: the rule is <see cref="IsBlockingWindow"/> — it additionally exempts
    /// ACTION-dismissed scripted level messages ("Wähle Trampeln" instruction overlays), whose
    /// blocking treatment gated off the very card/board interaction their dismiss trigger waits on.
    /// </summary>
    internal static bool BlockingWindowModalActive
    {
        get
        {
            for (int i = 0; i < Converted.Count; i++)
            {
                UIWindow w = Converted[i].Window;
                if (w != null && IsBlockingWindow(w))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// DIAGNOSTIC: names every floated window that currently counts as BLOCKING, as
    /// <c>'name' (ID …)</c>, comma-joined — "none" when nothing blocks.
    ///
    /// WHY this exists (user report 2026-08-02, MP host): the suppression logs said only
    /// "blocking modal open", so a hardware log could prove THAT a press was eaten but never
    /// WHICH window ate it — pinning the multiplayer player picker as the culprit needed a
    /// manual correlation across 300 log lines. Every suppression site now names its cause.
    /// ALLOCATES (string building): call it from THROTTLED / change-gated log paths only,
    /// never per frame.
    /// </summary>
    internal static string DescribeBlockingWindows()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Converted.Count; i++)
        {
            UIWindow w = Converted[i].Window;
            if (w == null || !IsBlockingWindow(w))
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(w.name).Append("' (ID ").Append(w.ID).Append(')');
        }
        if (ErrorModalOpen)
            sb.Append(sb.Length > 0 ? ", " : "").Append("the global error box (game halted)");
        return sb.Length > 0 ? sb.ToString() : "none";
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
        // Round 8: hand every pre-convert blackout back BEFORE anything else — a game window we
        // switched off must never survive the module (see part 11).
        ReleaseAllPreConvertHide("module shutdown");
        RestoreMenuSelectionGuard(); // put InControl mouse-hover focus back before we drop the windows
        Core.MixedReality.KeepMenusUnclipped(false); // item 5a: release the backdrop depth override
        ReleaseAllWindows("module shutdown");
        Open.Clear();
        OpenWindows.Clear();
        Failed.Clear();
        _storyOpen = _levelMsgOpen = _dialogPopupOpen = false;
        _levelMsgClosedTicks = 0;
        _lastWant = false;
        _escapeChordFired = false;
        _escapeArmingLogged = false;
        _lastActionDismissLogKey = null; // deadlock #3: re-log the ruling per fresh session
        ResetChainPose("module shutdown"); // chain continuity: teardown = rule 1 next time
        _forcedTabs.Clear();
        CatchAllReset(); // part 10: unknown-window tracker + reward poll + error-box float
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
        // Part 10 (catch-all): remember every shown window whose ID the explicit path does
        // NOT track — an unknown scenario window must never block invisibly (deadlock
        // insurance). Pure bookkeeping here; all judgement is level-triggered in Tick.
        CatchAllObserve(e.Window, e.Shown);
        if (!e.Shown)
        {
            ReleasePreConvertHide(e.Window, "the game hid it again");
            if (Open.Remove(e.Window))
                VRLog.Info("WorldUI", $"Modal fallback: window '{e.Window.name}' (ID {e.Id}) hidden — untracked.");
            return;
        }
        if (!IsFallbackWindow(e.Id))
            return;

        // ROUND 8 (the left-eye left-edge flicker) — THE ONE PLACE THIS CAN BE FIXED. This
        // postfix runs SYNCHRONOUSLY inside the game's own Show(), before the frame renders;
        // the conversion below only runs in the NEXT tick, so between the two the game's 2D
        // window is drawn once — by the head camera, which renders every layer — at its
        // screen-space home. Switch its rendering off right here; TryConvertWindow hands it
        // straight back so the conversion's own hide/reveal bookkeeping stays exact. Full
        // reasoning, evidence and the safety bounds: ModalFallback part 11.
        PreConvertHide(e.Window);
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
        // Round 8, FIRST — before any step that could throw: end every pre-convert 2D blackout
        // whose window will not be floated after all, and enforce the frame budget (part 11).
        // A window the mod switched off must never outlive the reason it was switched off.
        TickPreConvertHide();

        bool inScenario = VRModeStateMachine.ScenarioBoardExists;

        // LEVEL-MESSAGE CHAIN CONTINUITY (user ruling 2026-08-02): the shared stored
        // window pose is scoped to ONE scenario — outside it there is no chain to continue,
        // and a stale pose must never place the NEXT scenario's first tutorial box (that
        // first window is a rule-1 in-front spawn by definition). Change-gated inside.
        if (!inScenario)
            ResetChainPose("scenario ended / left");

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
            // Tutorial deadlock #2 (hardware log 2026-08-02, TB_2_1→TB_2_2 handover): the game's
            // static IsShown flag is CLOBBERABLE. HideWindow() arms an end-of-frame coroutine
            // that sets IsShown=false UNCONDITIONALLY (LevelMessageUILayoutGroup.cs:93-99), and
            // a dismiss-button press shows the NEXT scripted message SYNCHRONOUSLY in the same
            // frame (HideCurrentlyShownBoxMessage → ShowNextBoxMessage → StartCoroutine runs
            // DisplayMessageInWindowAfterDelay straight to window.Show() when DisplayDelay=0,
            // LevelMessagesUIHandler.cs:153-203) — so the fresh Show's IsShown=true is
            // overwritten at frame end and the flag reads false FOREVER while the window is
            // genuinely OPEN (release log: open=True). Trusting the flag alone released the
            // float and parked the still-open box on the invisible 2D stack — the tutorial's
            // dismiss-chained hint chain deadlocked on an unreachable dismiss button. GROUND
            // TRUTH: OR in the group windows' OWN IsOpen/IsVisible, so the poll only ever
            // reports closed when the game's actual window state agrees. (The flag is also
            // shared static across BOTH group instances — box + helptext — which the
            // per-window check sidesteps too.)
            levelMsg = LevelMessageUILayoutGroup.IsShown || AnyLevelMessageWindowOpen();
            manager = UIManager.Instance;
            dialog = manager != null && manager.dialogPopup != null && manager.dialogPopup.IsOpen();
        }
        // Debounce (belt to the ground-truth suspenders above): a hide→re-show handover can
        // still flicker BOTH signals false across a frame boundary (the window's Hide runs a
        // frame before a delayed re-Show). A momentary false must never trigger the release —
        // require LevelMsgCloseGraceTicks CONSECUTIVE closed ticks before a previously-open
        // level message is reported closed (the catch-all grace pattern, applied to a close).
        if (levelMsg)
        {
            _levelMsgClosedTicks = 0;
        }
        else if (_levelMsgClosedTicks < LevelMsgCloseGraceTicks)
        {
            _levelMsgClosedTicks++;
            if (_levelMsgOpen && _levelMsgClosedTicks < LevelMsgCloseGraceTicks)
                levelMsg = true; // hold the last open state until the close is trusted
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

        // Part 10: the reward-showcase poll (enrollment #2 — the chest showcase window's ID
        // is scene-serialized and unprovable, see the part-10 verification comment) and the
        // CATCH-ALL — unknown scenario windows join OpenWindows after a short grace so an
        // un-enrolled window can never again wait invisibly on the hidden 2D stack. Runs
        // AFTER the explicit polls so their dedupe/claim handling always wins.
        TickCatchAll(inScenario);

        // Part 10: GlobalErrorMessage (enrollment #1) — NOT a UIWindow (SetActive-shown), so
        // neither the transition patch nor the catch-all above can see it; dedicated poll +
        // direct float. Feeds the lock below via ErrorModalOpen (a genuine blocker: the whole
        // game halts on ShowingMessage) and the screen policy via ErrorScreenWanted.
        TickErrorMessage(inScenario);

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
        // BLOCKING prompt is open (story, dialog-confirm, results, durability, dismiss-button
        // level messages). Tutorial deadlock #3: an ACTION-dismissed scripted level message is
        // NOT a blocker — it floats visible but the board/cards/laser stay fully live, because
        // its dismiss trigger IS a board/card interaction (IsBlockingWindow, part 7, decides
        // both this lock and the ray/card pick gate from the same data-driven rule).
        bool anyBlocking = false;
        for (int i = 0; i < OpenWindows.Count; i++)
        {
            if (IsBlockingWindow(OpenWindows[i]))
            {
                anyBlocking = true;
                break;
            }
        }
        // Part 10: the error box is a genuine blocker too (Choreographer.Update early-outs
        // on ShowingMessage) — it holds the lock even though it is not a UIWindow.
        bool wantLock = (want && anyBlocking) || ErrorModalOpen;

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
            // Tutorial deadlock #2 do-no-harm: a level-message group whose scripted message the
            // game still considers DISPLAYED (LevelMessagesUIHandler current-message state) is
            // NEVER released, whatever the poll flags momentarily read — releasing it mid-message
            // restores the box to the invisible 2D stack and the dismiss-chained tutorial dies
            // there. The float releases normally the moment the message is genuinely dismissed
            // (current message nulled / next message's DisplayDelay in effect).
            bool stillOpen = alive && !wp.UserClosing
                             && (ContainsWindow(OpenWindows, wp.Window!) || wp.Sticky
                                 || ScriptedLevelMessageActive(wp.Window));
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
            // LEVEL-MESSAGE CHAIN CONTINUITY: the game sometimes CLOSES the group window
            // briefly between two messages of a chain — this release is that gap's edge.
            // Park the float's LIVE pose (grab-moves included) in the shared chain store
            // BEFORE the host is torn down, so the next scripted window of ANY kind re-floats
            // at exactly this spot (TryConvertWindow rule 2) instead of respawning at the gaze.
            // Scenario-gated: on scenario exit the store is reset above, not re-fed here.
            if (inScenario && IsLevelMessageWindow(wp.Window))
                StoreChainPose(wp);
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
            // Round 3: keyed on the APPLIED-FIT GENERATION, not a bool latch — a one-shot VERIFY
            // correction (CanvasConversion.VerifyOneShotFit) re-fits a rect that turned out not to
            // contain its own content, and the board-relative scale must be re-derived from the
            // corrected width instead of staying on the rejected one.
            if (wp.ScaleReDerivedAtFit == wp.Panel.FitAppliedGeneration || !wp.OneShotFitted
                || wp.Grab == null || !wp.Panel.IsAlive || !wp.Panel.FitOneShotApplied)
                continue;
            float refit = DeriveWindowScale(wp.Panel);
            wp.ExtraScale = refit;
            wp.Grab.SetExtraScale(refit);
            wp.ScaleReDerivedAtFit = wp.Panel.FitAppliedGeneration;
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{(wp.Window != null ? wp.Window.name : "<menu>")}' " +
                                  $"re-scaled to the fitted content (extraScale → {refit:F3}) — board-sized, " +
                                  "compact, consistent every open.");
        }

        // 5b-pose. FIRST-OPEN POSE FIX (user report 2026-08-02): a window is PLACED at convert
        //     time, i.e. before the content fit (5b's trigger) and before the scale re-derivation
        //     5b just did — so every spawn clamp (board-top clearance, eye cap, overlap box test,
        //     level-message view cone) was computed from the PRE-fit half-size, which for a
        //     full-screen menu is several times the fitted one. While the window is still
        //     render-hidden behind the reveal gate, replay that ONE placement against the now
        //     final rect/scale; the reveal gate itself lives in CanvasConversion.Tick, which runs
        //     LATER in this same Update, so the correction always lands BEFORE the first visible
        //     frame and never after it. Runs directly after 5b so a window whose scale was
        //     re-derived THIS tick is re-placed in the same tick (one-shot, see TickPoseRePlace).
        TickPoseRePlace();

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
        ScreenWanted = wantLock && (!WorldUIConfig.ModalWindowStyle || Failed.Count > 0
                                    || ErrorScreenWanted); // part 10: unfloatable error box
        VRModeStateMachine.SetAuxModal(wantLock); // ModalUI only for genuine blockers (item 3b)
    }
}
