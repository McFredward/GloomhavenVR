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
    // ---- modal escape chord (test #17) --------------------------------------------------

    /// <summary>
    /// Modal escape hatch (test #17 hard-lock guarantee): while a floating modal
    /// window is open, holding the non-dominant A/X to the manual-chord threshold
    /// closes the TOP (most recently floated) modal through the game's own escape
    /// path — <c>UIWindow.Escape()</c> (exactly what the ESC key runs per window,
    /// honors escapeKeyAction), falling back to the public <c>UIWindow.Hide()</c>
    /// when the window opts out of escape. Game state observes the close normally
    /// (OnHide/onHidden fire); nothing is bypassed.
    ///
    /// Chord arbitration: this consumer runs BEFORE <see cref="FlatScreen"/> in the
    /// driver order and CONSUMES the press — one press, one action. While a modal
    /// floats the chord means "close it"; the flat-screen toggle (and the
    /// settings-panel short hold) need a fresh press once no modal floats, so the
    /// universal screen rescue stays reachable.
    /// </summary>
    private static void TickEscapeChord()
    {
        if (Converted.Count == 0 || NonDominantHold.HeldSeconds <= 0f)
        {
            _escapeChordFired = false;
            _escapeArmingLogged = false;
            return;
        }
        if (NonDominantHold.Consumed || _escapeChordFired)
            return;

        float threshold = Mathf.Max(0.5f, WorldUIConfig.ManualScreenChordSeconds.Value);
        if (!_escapeArmingLogged && NonDominantHold.HeldSeconds >= threshold * 0.5f)
        {
            _escapeArmingLogged = true;
            VRLog.Info("WorldUI", $"Modal escape chord ARMING: non-dominant A/X held " +
                                  $"{NonDominantHold.HeldSeconds:F1}s with a floating modal open — " +
                                  $"keep holding to {threshold:F1}s to close the top modal window.");
        }
        if (NonDominantHold.HeldSeconds < threshold)
            return;

        _escapeChordFired = true;
        NonDominantHold.Consumed = true; // one press, one action (screen/settings skip it)
        NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
        CloseTopModal(threshold);
    }

    /// <summary>Close the top (most recently floated) modal via the game's own path.</summary>
    private static void CloseTopModal(float heldSeconds)
    {
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            UIWindow window = wp.Window;
            // Item 6: a sticky menu the game already hid is still floated (force-visible) — the chord
            // must be able to close it too. Skip only windows already flagged for release. Route
            // through CloseFloatedWindow so both the game close (if open) and the sticky force-visible
            // release path are handled in one place.
            if (window == null || wp.UserClosing || (!window.IsOpen && !wp.Sticky))
                continue;
            VRLog.Info("WorldUI", $"MODAL ESCAPE CHORD: closing top modal '{window.name}' (ID {window.ID}) — " +
                                  $"non-dominant A/X held {heldSeconds:F1}s.");
            CloseFloatedWindow(window);
            return;
        }
        VRLog.Info("WorldUI", "MODAL ESCAPE CHORD: no floating modal left to close.");
    }

    /// <summary>
    /// Item 3c: close ONE floated window through the game's own escape/hide path (the mod X
    /// button's action). Mirrors <see cref="CloseTopModal"/>: <c>UIWindow.Escape()</c> first
    /// (honors escapeKeyAction, exactly what the ESC key runs), falling back to the public
    /// <c>UIWindow.Hide()</c> when the window opts out of escape. Game state observes the close
    /// normally (OnHide/onHidden fire); the mod's per-tick prune then releases the float.
    /// </summary>
    internal static void CloseFloatedWindow(UIWindow? window)
    {
        if (window == null)
            return;
        string name = window.name;

        // Item 6: flag THIS floated window for release regardless of the game's own IsOpen. A sticky
        // reachable menu the game's single-window toggle already hid stays floated in VR until its
        // OWN X closes it, so here its game state may already be Hidden — the flag is what actually
        // drops the parallel float, independent of whether the game close below does anything.
        WindowPanel? wp = FindPanel(window);
        if (wp != null)
            wp.UserClosing = true;

        try
        {
            if (window.IsOpen)
            {
                // Item 7a: close EXACTLY THIS window (submenus must be individually closable). The
                // return value of UIWindow.Escape() is NOT proof the window closed — a submenu whose
                // escapeKeyAction is Skip returns TRUE while doing nothing (decompiled UIWindow.cs:717),
                // and None/HideIfFocused-when-unfocused return FALSE without hiding. So run Escape()
                // for its honored per-window behavior, then FORCE this window hidden if it is still
                // open — its own Hide() (OnHide/onHidden fire), never the parent's.
                bool escaped = window.Escape();
                bool hidden = false;
                if (window.IsOpen)
                {
                    window.Hide();
                    hidden = true;
                }
                VRLog.Info("WorldUI", $"MODAL CLOSE (X button): '{name}' (ID {window.ID}) closed via " +
                                      $"{(hidden ? (escaped ? "UIWindow.Escape()+Hide()" : "UIWindow.Hide()") : "UIWindow.Escape()")}.");
            }
            else
            {
                // Item 6: the game already hid this sticky window (a sibling opened) and the mod kept
                // it floated + force-visible. There is nothing to close at the game level — reset the
                // forced CanvasGroup to the game's hidden state and let the per-tick release drop the
                // VR float (UserClosing flag above).
                if (wp?.WindowCanvasGroup != null)
                {
                    wp.WindowCanvasGroup.alpha = 0f;
                    wp.WindowCanvasGroup.blocksRaycasts = false;
                    wp.WindowCanvasGroup.interactable = false;
                }
                VRLog.Info("WorldUI", $"MODAL CLOSE (X button): '{name}' (ID {window.ID}) — game had already " +
                                      "hidden it (single-window toggle); releasing the parallel VR float only.");
            }
        }
        catch (Exception ex)
        {
            VRLog.Error("WorldUI", $"MODAL CLOSE (X button): closing '{name}' FAILED " +
                                   $"({ex.GetType().Name}: {ex.Message}).");
        }

        // Issue 4 (ESC menu unresponsive after closing a submenu): reset the ESC menu's ToggleGroup
        // deterministically on EVERY X-close of an ESC submenu, in BOTH branches above — the game's
        // own hide-callback (optionsButton.Deselect / OnHideOptionWindow → toggleGroup.SetAllTogglesOff)
        // does NOT run when our X-close takes the "game had already hidden it" branch, so the tab's
        // toggle is left ON. Clicking an already-on toggle in a single-select ToggleGroup does nothing,
        // so submenus could not be reopened. Forcing the group off here leaves it clean regardless of
        // which branch closed the window.
        ResetEscMenuToggleGroup(window);
    }

    /// <summary>
    /// Issue 4: turn the ESC menu's <c>ToggleGroup</c> fully off after an ESC SUBMENU (Options /
    /// OptionsSubmenu / Multiplayer submenu / Compendium) is X-closed, so its tab toggle is not left
    /// ON — an already-on toggle in a single-select group ignores the next click, which left the ESC
    /// menu unable to reopen submenus. Reached via the persistent <c>Singleton&lt;ESCMenu&gt;</c>
    /// (the pause menu the submenus belong to); no-op for non-submenu windows and when no ESC menu
    /// exists. Reflection-free (publicized <c>toggleGroup</c>), fully null-guarded.
    /// </summary>
    private static void ResetEscMenuToggleGroup(UIWindow window)
    {
        UIWindowID id = window.ID;
        if (id != UIWindowID.Options && id != UIWindowID.OptionsSubmenu
            && id != UIWindowID.ViceOptionsSubmenu && id != UIWindowID.CompendiumPanel)
            return;
        if (!Singleton<ESCMenu>.IsInitialized)
            return;
        ESCMenu esc = Singleton<ESCMenu>.Instance;
        if (esc == null)
            return;
        try
        {
            ToggleGroup? group = esc.toggleGroup;
            if (group == null)
                return;
            group.SetAllTogglesOff();
            VRLog.Info("WorldUI", $"MODAL CLOSE (X button): reset the ESC-menu ToggleGroup after closing " +
                                  $"submenu '{window.name}' (ID {id}) — its tab toggle is cleared so the ESC " +
                                  "menu can reopen submenus again (Issue 4).");
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"MODAL CLOSE (X button): could not reset the ESC-menu ToggleGroup " +
                                  $"({ex.GetType().Name}: {ex.Message}) — submenu reopen may need a second tap.");
        }
    }

    /// <summary>
    /// FIX A companion (controller-X close-all): close every still-floated STICKY menu window
    /// the OptionsToggle live probes cannot see — a sticky float whose game window the ESC
    /// menu's single-window toggle already hid reports <c>IsOpen == false</c>, so the open-state
    /// probe skips it, yet its float would stay force-visible forever (only
    /// <see cref="WindowPanel.UserClosing"/> ever drops a sticky float). Each is routed through
    /// <see cref="CloseFloatedWindow"/> — exactly the corner-X path. The ESC menu itself is
    /// EXCLUDED (the caller closes it LAST so its OnHide → SetAllTogglesOff cascade stays the
    /// final word); windows already flagged UserClosing are skipped (already on their way out).
    /// </summary>
    internal static void CloseStickyFloatsExceptEscMenu()
    {
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            UIWindow? window = wp.Window;
            if (window == null || wp.UserClosing || !wp.Sticky || window.ID == UIWindowID.ESCMenu)
                continue;
            CloseFloatedWindow(window);
        }
    }

    /// <summary>The floated <see cref="WindowPanel"/> for a game window, or null if not floated.</summary>
    private static WindowPanel? FindPanel(UIWindow window)
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            if (ReferenceEquals(Converted[i].Window, window))
                return Converted[i];
        }
        return null;
    }

    /// <summary>
    /// Item 6 (parallel windows): keep a sticky menu visible + clickable in VR after the game hid it
    /// (its single-window toggle set the window's visual state Hidden and tweened the CanvasGroup to
    /// alpha 0). The window root lives under our host, so forcing its CanvasGroup back to alpha 1 with
    /// raycasts on re-shows it in VR without calling <c>Show()</c> (no onShown side effects, no war
    /// with the toggle — the deselect is a one-shot event). Change-gated writes; also re-activates a
    /// <c>m_DisableOnZeroAlpha</c> window that went inactive at alpha 0.
    ///
    /// EMPTY-SHELL FIX: a <c>UIWindow</c> whose serialized <c>_disableCanvas</c> is set DISABLES its
    /// own Canvas component when the hide fade completes (UIWindow.OnTransitionCompleted →
    /// <c>_canvas.enabled = false</c>). A disabled Canvas renders NOTHING under it, so forcing only the
    /// CanvasGroup/active state left the sticky menu an EMPTY shell — the game content gone, only the
    /// mod-drawn grab bar + X visible (confirmed for Options when Spielanleitung/Compendium opens: the
    /// ESC-menu single-selection toggle turns the Options toggle off → <c>UIOptionsWindow.Hide()</c>).
    /// Re-enabling the window's own <see cref="WindowPanel.WindowCanvas"/> restores the full live
    /// content. This is the exact Canvas <c>_disableCanvas</c> targets (the window root's own), so it
    /// never re-shows sub-canvases the game legitimately keeps hidden (closed option tabs).
    /// </summary>
    private static void ReassertStickyVisible(WindowPanel wp)
    {
        CanvasGroup? cg = wp.WindowCanvasGroup;
        if (cg != null)
        {
            if (cg.alpha < 1f) cg.alpha = 1f;
            if (!cg.blocksRaycasts) cg.blocksRaycasts = true;
            if (!cg.interactable) cg.interactable = true;
        }
        // Empty-shell fix: re-enable the window's own Canvas that a `_disableCanvas` UIWindow turned
        // off on its hide-fade complete — otherwise the whole subtree stops rendering (empty shell).
        Canvas? canvas = wp.WindowCanvas;
        if (canvas != null && !canvas.enabled)
            canvas.enabled = true;
        GameObject go = wp.Window.gameObject;
        if (!go.activeSelf)
            go.SetActive(true);
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

    /// <summary>
    /// Ground truth behind the level-message poll (tutorial deadlock #2): is either group's
    /// OWN UIWindow open or visible? The static <c>LevelMessageUILayoutGroup.IsShown</c> flag
    /// lies after a same-frame hide→show handover (its end-of-frame reset coroutine clobbers
    /// the next message's fresh <c>IsShown=true</c>, LevelMessageUILayoutGroup.cs:93-99) and is
    /// shared across both group instances besides — the per-window state is what actually
    /// renders, so it is what the poll must trust.
    /// </summary>
    private static bool AnyLevelMessageWindowOpen()
    {
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        if (handler == null)
            return false;
        return LevelMessageWindowOpen(handler.LevelMessageBoxLayoutGroup)
               || LevelMessageWindowOpen(handler.LevelMessageHelpTextLayoutGroup);
    }

    private static bool LevelMessageWindowOpen(LevelMessageUILayoutGroup? group)
    {
        if (group == null)
            return false;
        UIWindow? window = group.window;
        return window != null && (window.IsOpen || window.IsVisible);
    }

    /// <summary>
    /// Do-no-harm release gate (tutorial deadlock #2): is a scripted level message logically
    /// ACTIVE for this window's group right now? Reads the game's own current-message state
    /// (<c>LevelMessagesUIHandler.m_CurrentlyDisplayed*MessageInfo</c>, publicized): between
    /// <c>ShowBoxMessageImmediately</c> and the dismiss the info stays set, and on a dismiss
    /// <c>HideCurrentlyShown*</c> → <c>ShowNext*</c> either nulls it or replaces it with the
    /// next message (LevelMessagesUIHandler.cs:115-161) — so "info set, no display delay
    /// pending" means the game is WAITING on this box, and the mod must not restore it to the
    /// invisible 2D stack no matter what the poll flags flicker to. <c>DisplayDelayInEffect</c>
    /// excludes the one legitimate window-closed-while-active phase (a delayed next message,
    /// LevelMessagesUIHandler.cs:185-200) so those release + re-float normally.
    /// </summary>
    private static bool ScriptedLevelMessageActive(UIWindow? window)
    {
        if (window == null)
            return false;
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        if (handler == null || handler.DisplayDelayInEffect)
            return false;
        if (handler.CurrentlyDisplayedBoxMessage != null
            && handler.LevelMessageBoxLayoutGroup != null
            && ReferenceEquals(handler.LevelMessageBoxLayoutGroup.window, window))
            return true;
        if (handler.CurrentlyDisplayedHelpTextMessage != null
            && handler.LevelMessageHelpTextLayoutGroup != null
            && ReferenceEquals(handler.LevelMessageHelpTextLayoutGroup.window, window))
            return true;
        return false;
    }

    /// <summary>
    /// True when <paramref name="window"/> is one of the two level-message GROUP windows
    /// (tutorial box / help-text action strip, <c>LevelMessagesUIHandler.s_Instance</c>'s
    /// serialized groups). This is the family with the dedicated spawn placement — closer
    /// (<see cref="LevelMessageDistanceMeters"/>), gaze-centered inside the view cone — and
    /// the chain pose continuity (user ruling 2026-08-02: subsequent scripted windows of a
    /// chain — of ANY kind — reopen at the previous window's pose, see <see cref="_chainPose"/>).
    /// </summary>
    private static bool IsLevelMessageWindow(UIWindow? window) => LevelMessageGroupIndex(window) >= 0;

    /// <summary>
    /// Which level-message GROUP a window belongs to: 0 = tutorial box group, 1 = help-text
    /// strip group, −1 = not a level-message window. Classification only — the chain-pose
    /// store is deliberately NOT per group anymore (hardware round 2026-08-02: two per-group
    /// stores let the box and strip chains live in two different places); the index now just
    /// gates "is scripted" and names the kind for diagnostics
    /// (<see cref="LevelMessageKindName"/>).
    /// </summary>
    private static int LevelMessageGroupIndex(UIWindow? window)
    {
        if (window == null)
            return -1;
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        if (handler == null)
            return -1;
        if (handler.LevelMessageBoxLayoutGroup != null
            && ReferenceEquals(handler.LevelMessageBoxLayoutGroup.window, window))
            return 0;
        if (handler.LevelMessageHelpTextLayoutGroup != null
            && ReferenceEquals(handler.LevelMessageHelpTextLayoutGroup.window, window))
            return 1;
        return -1;
    }

    /// <summary>Human-readable name of a level-message group (diagnostics: the shared chain
    /// store records which kind last anchored it, surfaced in the rule-2 re-float log so a
    /// hardware log shows whose spot a window inherited).</summary>
    private static string LevelMessageKindName(int groupIndex) =>
        groupIndex == 0 ? "tutorial box" : "help-text strip";

    /// <summary>
    /// The scripted message currently displayed in this level-message group window (its
    /// <c>MessageName</c>; null when none / not a level-message window) — the CHANGE SIGNAL the
    /// chain-pose capture in <see cref="TickMenuRecall"/> keys on: the tutorial chains messages
    /// through ONE kept-alive float (deadlock #2 do-no-harm gate), so when the key changes a
    /// NEW hint just re-showed inside the existing panel at its previous pose — which the
    /// capture then persists (position continuity, user ruling 2026-08-02).
    /// </summary>
    private static string? CurrentLevelMessageKey(UIWindow? window)
    {
        if (window == null)
            return null;
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        if (handler == null)
            return null;
        ScenarioRuleLibrary.CustomLevels.CLevelMessage? msg = null;
        if (handler.LevelMessageBoxLayoutGroup != null
            && ReferenceEquals(handler.LevelMessageBoxLayoutGroup.window, window))
            msg = handler.CurrentlyDisplayedBoxMessage;
        else if (handler.LevelMessageHelpTextLayoutGroup != null
                 && ReferenceEquals(handler.LevelMessageHelpTextLayoutGroup.window, window))
            msg = handler.CurrentlyDisplayedHelpTextMessage;
        return msg == null ? null : msg.MessageName ?? "<unnamed>";
    }

    /// <summary>Change-gated log key of <see cref="ActionDismissedLevelMessage"/> — the
    /// classification is polled per frame (Cards driver + RayInteractor read
    /// <see cref="BlockingWindowModalActive"/>), so the ruling logs once per message.</summary>
    private static string? _lastActionDismissLogKey;

    /// <summary>
    /// Tutorial deadlock #3 (HT_4 'Wähle Trampeln', hardware log 2026-08-02): is this window a
    /// level-message group whose CURRENT scripted message is dismissed by a game ACTION instead
    /// of the dismiss button? Such a message is an instruction OVERLAY, not a dialog — the game
    /// waits for the very interaction (card select, tile click, confirm…) that our blocking
    /// treatment (ModalUI + ray pick gate + card input block) forbids, so classifying it
    /// blocking is a guaranteed total deadlock: the hint said "select Trample" while the mod
    /// had just gated off every card. These messages float VISIBLE but must impose ZERO input
    /// restrictions, exactly like the <see cref="NonBlockingMenus"/> family.
    ///
    /// The ruling is DATA-DRIVEN from the message's own dismiss trigger
    /// (<c>CLevelMessage.DismissTrigger.IsTriggeredByDismiss</c> — the exact flag
    /// <c>LevelEventsController.ProcessEvent</c> branches on, and the same one
    /// <see cref="Compat.LevelMessageHeal"/> keys its dismiss-button-only heal on), NOT from
    /// the layout name: the tutorial flow dump proves BOX-layout messages can be
    /// action-dismissed too (TB_19 dismiss=ShortRestChoseToBurn, TB_22/HT_18_2 confirm/rest
    /// chains) — a layout rule would deadlock those identically. The layout is only the
    /// FALLBACK signal when the trigger is unreadable (HelpText strips are instruction
    /// overlays by design, LevelMessageUILayoutGroup.cs:59 even disables their ESC action).
    ///
    /// Interplay: <see cref="ScriptedLevelMessageActive"/> (do-no-harm release gate) is
    /// untouched — the float stays alive and visible for the whole message; the close
    /// debounce only affects open/closed, not blocking-ness; the heal ignores these
    /// messages by its own dismiss-button predicate. Dismiss-button messages keep the full
    /// blocking treatment (they need a click on the box and nothing else).
    /// </summary>
    private static bool ActionDismissedLevelMessage(UIWindow? window)
    {
        if (window == null)
            return false;
        LevelMessagesUIHandler? handler = LevelMessagesUIHandler.s_Instance;
        if (handler == null)
            return false;
        ScenarioRuleLibrary.CustomLevels.CLevelMessage? msg = null;
        if (handler.LevelMessageBoxLayoutGroup != null
            && ReferenceEquals(handler.LevelMessageBoxLayoutGroup.window, window))
            msg = handler.CurrentlyDisplayedBoxMessage;
        else if (handler.LevelMessageHelpTextLayoutGroup != null
                 && ReferenceEquals(handler.LevelMessageHelpTextLayoutGroup.window, window))
            msg = handler.CurrentlyDisplayedHelpTextMessage;
        if (msg == null)
            return false; // not a level-message window / no current message → normal rules
        ScenarioRuleLibrary.CustomLevels.CLevelTrigger? trigger = msg.DismissTrigger;
        bool actionDismissed = trigger != null
            ? !trigger.IsTriggeredByDismiss // data-driven ruling (see doc comment)
            : msg.LayoutType == ScenarioRuleLibrary.CustomLevels
                .CLevelMessage.ELevelMessageLayoutType.HelpText; // layout fallback signal
        if (actionDismissed)
        {
            string key = msg.MessageName ?? "<unnamed>";
            if (!string.Equals(_lastActionDismissLogKey, key, StringComparison.Ordinal))
            {
                _lastActionDismissLogKey = key;
                VRLog.Info("WorldUI", $"Level message '{key}' is ACTION-dismissed " +
                                      $"({(trigger != null ? "dismiss trigger read from game data" : "layout fallback: HelpText strip")}) " +
                                      "→ floats NON-BLOCKING: no ModalUI, no ray pick gate, no card input block — " +
                                      "board/cards stay live so the instructed action can actually be performed.");
            }
        }
        return actionDismissed;
    }

    /// <summary>
    /// THE blocking rule, shared by <see cref="BlockingWindowModalActive"/> and the Tick()
    /// ModalUI lock so the ray pick gate and the mode machine can never disagree: a window
    /// blocks unless it is a player-reachable menu (<see cref="NonBlockingMenus"/>) or an
    /// action-dismissed scripted level message (<see cref="ActionDismissedLevelMessage"/>).
    /// </summary>
    private static bool IsBlockingWindow(UIWindow window) =>
        !NonBlockingMenus.Contains(window.ID) && !ActionDismissedLevelMessage(window);

    /// <summary>
    /// LASER-GATING POLICY HOME (user ruling 2026-08: "Ich möchte, dass der Laser ausnahmslos
    /// da ist und collidet, egal in welcher Phase sich das Spiel aktuell befindet."). The BEAM,
    /// its PHYSICS COLLISION and all HOVER feedback are now unconditional in every phase —
    /// no modal state may suppress them anywhere (RayInteractor renders + raycasts always,
    /// BoardPick projects the cursor always, the Cards laser paths hover always). What modal
    /// state still gates is ONLY the COMMIT layer, per this decision table:
    ///
    ///   commit target                     | while a BLOCKING modal floats | rationale
    ///   ----------------------------------+-------------------------------+---------------------------------
    ///   modal window's own uGUI           | ALLOWED (the whole point)     | virtual-mouse / RayUgui path.
    ///   non-modal converted panels (uGUI) | ALLOWED (never was gated)     | game GraphicRaycasters govern.
    ///   panel grab bars (drag/X/escape)   | ALLOWED (never was gated)     | rescue affordances must survive.
    ///   board tile/hex/actor CLICK        | ALLOWED — EXCEPT this lock    | injected at Controller.CommonLoop,
    ///                                     |                               | so the game's OWN LateUpdate gating
    ///                                     |                               | runs in full (InteractabilityManager,
    ///                                     |                               | ThisPlayerHasTurnControl, tutorial
    ///                                     |                               | isolation; story/error blockers
    ///                                     |                               | stall processing via UpdateBlocker) —
    ///                                     |                               | a stray click self-gates or no-ops.
    ///   card pluck/select/poke            | SUPPRESSED (unchanged commit  | blocking dialogs float at reading
    ///   (fan/tray/browse/active/field)    | policy; hover/pop now LIVE)   | distance IN the beam path to the fan;
    ///                                     |                               | a pull meant for the dialog that
    ///                                     |                               | misses lands on cards (test #15/#21
    ///                                     |                               | history). Undo exists but the misfire
    ///                                     |                               | class is constant; keep the gate.
    ///   tray board buttons / pile stacks  | SUPPRESSED (unchanged)        | these call game APIs DIRECTLY,
    ///   / item chips / click-away dismiss |                               | bypassing the 2D overlay raycast
    ///                                     |                               | blocker vanilla relies on; END TURN /
    ///                                     |                               | item use are non-undoable.
    ///   long-rest pump / hand-switch pump | DEFERRED (unchanged)          | game-state-advancing background
    ///                                     |                               | commits; never under a blocker.
    ///
    /// This property is the one HARD board-click commit lock: true only for the families where
    /// a stray injected click genuinely bypasses a vanilla impossibility — the end-of-scenario
    /// results windows (<see cref="IsResultsPanel"/>: vanilla physically eats those clicks via
    /// s_StartedButtonDownInGUI on the full-screen blocker, and our CommonLoop injection clears
    /// exactly that flag; the scenario is over, there is nothing legitimate to click) and the
    /// global error box (<see cref="ErrorModalOpen"/>: the whole game is halted on
    /// ShowingMessage; a click could only pollute double-click bookkeeping). Every OTHER
    /// blocking window (story, level messages, dialog popups, take-damage, rewards…) leaves
    /// board clicks LIVE — their targets self-gate through the game's own seams (see table).
    /// Consumed by BoardClickDriver.RequestClick; logged centrally by
    /// RayInteractor.UpdateCommitSuppression so hardware logs show the policy on state change.
    /// </summary>
    internal static bool HardCommitLockActive
    {
        get
        {
            if (ErrorModalOpen)
                return true;
            for (int i = 0; i < Converted.Count; i++)
            {
                UIWindow w = Converted[i].Window;
                if (w != null && IsResultsPanel(w.ID) && IsBlockingWindow(w))
                    return true;
            }
            return false;
        }
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

}
