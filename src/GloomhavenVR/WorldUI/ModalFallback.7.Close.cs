using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
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
    /// floats the chord means "close it"; the flat-screen toggle needs a fresh press
    /// once no modal floats, so the universal screen rescue stays reachable.
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
            // ModBuild 185 (character screen) + 194 (quest log): a PERMANENT map-room window is not
            // closable — skip it HERE rather than letting CloseFloatedWindow refuse it, so the chord
            // walks on to the NEXT window instead of stopping on one it may not touch. That
            // `continue` is what keeps a permanent window from becoming a trap: every other floated
            // window stays chord-closable no matter how many permanent ones stand in front of it.
            if (IsMapRoomPermanent(window))
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

        // ModBuild 185: the map room's character screen is not closable — see IsMapRoomPermanent
        // for why closing it SPLIT it rather than closing it. This covers the X, the escape chord
        // and CloseStickyFloatsExceptEscMenu in one place, because they all route through here.
        if (IsMapRoomPermanent(window))
        {
            VRLog.Info("WorldUI", $"MODAL CLOSE: refused for '{name}' (ID {window.ID}) — "
                                  + MapRoomPermanentReason(window)
                                  + " The escape chord walks PAST this window to the next one and the "
                                  + "pause menu is unaffected, so nothing here can trap the room.");
            return;
        }

        // Item 6: flag THIS floated window for release regardless of the game's own IsOpen. A sticky
        // reachable menu the game's single-window toggle already hid stays floated in VR until its
        // OWN X closes it, so here its game state may already be Hidden — the flag is what actually
        // drops the parallel float, independent of whether the game close below does anything.
        WindowPanel? wp = FindPanel(window);
        if (wp != null)
            wp.UserClosing = true;

        // ModBuild 184 — A GUILDMASTER DESTINATION IS LEFT, NOT HIDDEN. Merchant, temple, trainer,
        // enchantress and town records are MODES of UIGuildmasterHUD, and entering one disables the
        // party display's character slots (EnableSelectionMode). Only the mode's own Exit re-enables
        // them, and Escape()/Hide() on the window does not run it — which is exactly why "clicking a
        // character does nothing" survived a whole session after one merchant press. Pressing the
        // bar's map button runs UpdateCurrentMode → the destination's Exit → DisableSelectionMode,
        // and the window closes itself on the way; the branches below then simply release the float.
        // See GuildmasterDestinations.
        MapRoom.GuildmasterDestinations.LeaveMode(window, "X button");

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
        bool fought = false;
        CanvasGroup? cg = wp.WindowCanvasGroup;
        if (cg != null)
        {
            if (cg.alpha < 1f) { cg.alpha = 1f; fought = true; }
            if (!cg.blocksRaycasts) { cg.blocksRaycasts = true; fought = true; }
            if (!cg.interactable) { cg.interactable = true; fought = true; }
        }
        // Empty-shell fix: re-enable the window's own Canvas that a `_disableCanvas` UIWindow turned
        // off on its hide-fade complete — otherwise the whole subtree stops rendering (empty shell).
        Canvas? canvas = wp.WindowCanvas;
        if (canvas != null && !canvas.enabled)
        {
            canvas.enabled = true;
            fought = true;
        }
        GameObject go = wp.Window.gameObject;
        if (!go.activeSelf)
        {
            go.SetActive(true);
            fought = true;
        }

        // WAR DETECTOR (ModBuild 180). Re-asserting is correct against an EVENT-driven hide — the
        // guildmaster bar's ToggleGroup hides the sibling once when you pick another mode, we show
        // it again once, and it stays. It is NOT correct against a PER-FRAME writer: then the value
        // alternates every frame, the two MultiPass eyes can sample different sides of it, and the
        // window flickers instead of staying (the ModBuild-179 overrideSorting lesson, one layer
        // up). Change-gating the write does not prevent that — only noticing does. So count the
        // consecutive frames we had to fight and say so ONCE if it becomes a war, rather than
        // shipping a silent flicker and diagnosing it from a screenshot a round later.
        if (!fought)
        {
            wp.StickyFightFrames = 0;
            return;
        }
        if (++wp.StickyFightFrames == StickyFightWarnFrames)
        {
            VRLog.Warn("WorldUI", $"STICKY FIGHT: '{wp.Window.name}' has been re-shown "
                                  + $"{wp.StickyFightFrames} frames running — something in the game is "
                                  + "hiding it EVERY frame, not once. That is a write war and neither "
                                  + "side wins it: expect the window to flicker rather than stay. The "
                                  + "sticky rule assumes an event-driven hide (a ToggleGroup switching "
                                  + "modes); if this line appears, that assumption is wrong for this "
                                  + "window and it needs an exclusion, not a louder re-assert. ModBuild "
                                  + $"226: it now GETS one — after {StickyConcedeFrames} frames the "
                                  + "stickiness is dropped and the float releases on the next tick.");
        }
        // ModBuild 226 — AND THEN WE CONCEDE, BECAUSE THE ALTERNATIVE IS AN EMPTY WINDOW.
        //
        // The Warn above has been telling us since ModBuild 180 that a per-frame hider means "this
        // window needs an exclusion, not a louder re-assert", and the ModBuild 225 log duly prints
        // it ("STICKY FIGHT: 'Map Story Window' has been re-shown 3 frames running"). What it did
        // NOT do was act on its own diagnosis: the re-assert kept running for the rest of the
        // window's life, one write per frame, against a writer that wins. The user-visible outcome of
        // that stalemate is precisely the artefact of report 15 — this class's own WindowPanel doc
        // records it — "kept its float + CanvasGroup alpha but rendered as an EMPTY shell — only the
        // mod-drawn grab bar / X remained".
        //
        // So: drop STICKY. That is the smallest possible concession and it changes exactly one thing
        // — the release loop's `|| wp.Sticky` clause stops holding the float open, so a window the
        // game has genuinely taken away leaves with it on the next tick instead of standing as a bar
        // with nothing on it. Everything else about the window is untouched: no Hide(), no Show(), no
        // further CanvasGroup or Canvas write, so we are not answering a write war by writing more.
        // A window the game hides ONCE (the ToggleGroup case the rule exists for) never reaches this
        // count and keeps its parallel-windows behaviour exactly as before.
        if (wp.Sticky && wp.StickyFightFrames >= StickyConcedeFrames)
        {
            wp.Sticky = false;
            VRLog.Warn("WorldUI", $"STICKY CONCEDED: '{wp.Window.name}' — the game has hidden it on "
                                  + $"{wp.StickyFightFrames} consecutive frames, so the mod stops "
                                  + "re-showing it and the float will be released on the next tick. "
                                  + "REASON THIS IS THE RIGHT SIDE TO GIVE UP ON: a sticky window whose "
                                  + "content the game insists on hiding renders as an EMPTY SHELL — the "
                                  + "grab bar and the X with nothing between them — and the standing "
                                  + "ruling is that an empty window must never exist. Nothing was "
                                  + "written to the game to achieve this; only our own stickiness flag "
                                  + "was dropped. Re-open the window and it floats again normally.");
        }
    }

    /// <summary>Consecutive re-assert frames after which a sticky float is declared a write war.
    /// Three, for the same reason <c>CanvasConversion.ConcedeAfterReclears</c> uses three: a
    /// one-off hide is a single frame, a per-frame writer is unmistakable by the third.</summary>
    private const int StickyFightWarnFrames = 3;

    /// <summary>Consecutive re-assert frames after which the mod STOPS being sticky for this window
    /// (ModBuild 226). Deliberately later than <see cref="StickyFightWarnFrames"/>: three frames is
    /// enough to be sure there is a war, and a quarter of a second at 90 Hz is enough to be sure it
    /// is not a fade that happens to take a few frames to complete.</summary>
    private const int StickyConcedeFrames = 20;

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
    /// chain-pose capture in <see cref="TickLevelMessageChain"/> keys on: the tutorial chains messages
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
    /// blocks unless it is a player-reachable menu (<see cref="NonBlockingMenus"/>), a
    /// multiplayer roster/assignment surface (<see cref="MultiplayerRosterMenus"/> — an
    /// administrative window about OTHER players' seats, which must never freeze the local
    /// player's own board/piles/item fan) or an action-dismissed scripted level message
    /// (<see cref="ActionDismissedLevelMessage"/>).
    /// </summary>
    private static bool IsBlockingWindow(UIWindow window) =>
        !NonBlockingMenus.Contains(window.ID)
        && !MultiplayerRosterMenus.Contains(window.ID)
        && !MapRoomParallel(window)
        // ModBuild 185: a HOVER CARD is not a decision waiting for an answer, so it may not raise
        // the ModalUI lock. MapRoomParallel deliberately excludes hover cards (they must never be
        // sticky), and that exclusion leaked into this test: every single mouseover flipped the
        // mode machine to ModalUI and gated the card/tray commits off and on again — the 184 log
        // shows "Modal commit-block ENGAGED"/"RELEASED" pairs on alternating ticks. Non-sticky and
        // non-blocking are two different properties of the same object.
        && !IsMapRoomHoverCard(window)
        && !ActionDismissedLevelMessage(window);

    /// <summary>
    /// THE 3D MAP ROOM'S OWN WINDOW RULE (ModBuild 180), user verbatim: <i>"Anders als in Flat soll
    /// es hier möglich sein mehrere Fenster parallel offen zu haben zB Kirche zum Spenden UND
    /// Händler — es soll also nonblocking sein und der button öffnet die Fenster nur. (betrifft nur
    /// die 3D ansicht)"</i>, together with <i>"Die UI Elemente … dürfen NIE [verschwinden] selbst
    /// wenn ich auf den Händler oder so klicke."</i>
    ///
    /// <para>The flat game runs a single-window discipline: opening the merchant hides the temple.
    /// In a room where the windows are physical objects on a table that is simply wrong — you do
    /// not put the shop away to look at the temple. So a window opened while the map room stands is
    /// <b>non-blocking</b> (it never raises the ModalUI lock, so it cannot freeze anything) and
    /// <b>sticky</b> (the release loop keeps it floated when the game hides it behind a sibling).
    /// It closes on its X, on the escape chord, or when the room does — never on its own.</para>
    ///
    /// <para>THE CONFIRMATION FAMILY IS EXEMPT AND THAT IS DELIBERATE. A confirmation box is the
    /// one window whose whole purpose is to be answered before anything else happens; making it
    /// non-blocking would let a second action be committed behind the question it is asking. It is
    /// the only carve-out, and it is small enough to name.</para>
    /// </summary>
    internal static bool MapRoomParallel(UIWindow window) =>
        window != null
        && MapRoom.MapRoomDriver.Active
        && !ConfirmationFamily.Contains(window.ID)
        // ModBuild 181: a hover card is not a window the player opened — it must vanish with the
        // hover, so it is never sticky. See IsMapRoomHoverCard.
        && !IsMapRoomHoverCard(window);

    /// <summary>
    /// A HOVER CARD, not a window (ModBuild 181). User ruling, verbatim: <i>"JEDES mouseover
    /// bekommt nun ein eigenes Fenster, das ist zu viel. … Bei Mouseovers über ein Symbol soll es
    /// über dem Symbol entsprechend fliegen ohne ein separates Fenster zu sein das man verschieben
    /// kann (immer zum Kopf gedreht) und nur solange der Mouseover anhält."</i>
    ///
    /// <para>ModBuild 180 made every floated window in the map room STICKY so the merchant and the
    /// temple could stand open together. That was right for windows the player OPENS and wrong for
    /// windows the pointer merely TOUCHES: the game's quest-preview popup and its tooltips open and
    /// close with the hover, so sticky turned each one into a permanent panel and they piled up.
    /// The mistake was treating "floated" as one category — a card that follows a hover is a
    /// different kind of thing from a window that waits for you.</para>
    ///
    /// <para>Matched by COMPONENT, never by name: <c>UIQuestPreviewPopup</c> is the map's own
    /// location preview, and <c>UILocalTooltip</c> is the base every local tooltip in the game uses
    /// (it even carries an optional <c>UIWindow</c> of its own, which is exactly how these ended up
    /// in the window path at all).</para>
    /// </summary>
    /// <para>THE TEST IS ON THE WINDOW'S OWN GAMEOBJECT (ModBuild 182), and 181's was not. It asked
    /// <c>GetComponentInParent</c> AND <c>GetComponentInChildren</c>, and the character screen
    /// CONTAINS tooltips — so the whole character UI was classified as a hover card, flown over
    /// whatever icon the pointer was on, and stripped of the grab bar and X that made it a window.
    /// His report: <i>"statt die richtige Info des Symbols anzuzeigen wird über dem Symbol die
    /// Character UI angezeigt und das verschiebbare Fenster verschwindet dann."</i> Exactly the
    /// same shape of mistake as ModBuild 179's <c>GetComponentInParent&lt;UIGuildmasterHUD&gt;</c>,
    /// which caught every window that HUD owned. A containment test answers "is this related to a
    /// tooltip"; the question is "IS this a tooltip".</para>
    /// </summary>
    internal static bool IsMapRoomHoverCard(UIWindow window) =>
        window != null
        && MapRoom.MapRoomDriver.Active
        && (window.GetComponent<UIQuestPreviewPopup>() != null
            || window.GetComponent<UILocalTooltip>() != null);

    /// <summary>
    /// THE CHARACTER UI IS THE PHASE, NOT A WINDOW IN IT (ModBuild 185). User ruling, verbatim:
    /// <i>"Die Characterinfo soll klar an die Character-UI gebunden sein — wenn ich [das] X klicke
    /// dann trenne ich beides in separate Fenster, soll nicht sein. Das CharacterUI Fenster soll
    /// gar kein 'x' haben, das soll hier in der Phase nicht schließbar sein."</i>
    ///
    /// <para>WHAT THE X ACTUALLY DID, from the 184 log: line 2941 closes 'New Party display'
    /// (ID PartyPanel); twelve lines later 'Campaign Adventure Party Assembly Variant'
    /// (ID PartyAssemblyWindow) — until that moment a CHILD rendering inside the party display's
    /// host, correctly suppressed by the parent-wins rule — becomes eligible and floats as a window
    /// of its own. Closing the parent did not close the screen; it SPLIT it. That is inherent:
    /// "the parent wins" can only hold while the parent is there, so a screen whose parts are
    /// nested windows must not have a parent that can be taken away.</para>
    ///
    /// <para>So in the map room this family floats with NO X and is refused by
    /// <see cref="CloseFloatedWindow"/> and the escape chord alike. Nothing else is affected: in a
    /// scenario the flat screen composites whatever is not floated, and outside the map room these
    /// windows keep their X exactly as before. The pause menu remains the way out of the room.</para>
    ///
    /// <para>AND THE QUEST LOG (ModBuild 194). User ruling, verbatim: <i>"Auch das Fenster mit den
    /// Quests ('Weltquests', 'Abgeschlossene Quests') sollen nicht schließbar sein (Kein x)."</i>
    /// That window is <c>Quest Log Manager</c>, and the hardware log says its <c>UIWindowID</c> is
    /// <b>None</b> (<c>UIWindow SHOWN: 'Quest Log Manager' (ID None …)</c>) — the serialized default
    /// that dozens of other windows also carry, because the <c>UIWindowID</c> enum has no quest-log
    /// member at all (decompiled UIWindowID.cs has QuestPopup / QuestTracker / UnlockQuestPopup and
    /// nothing for the log). So the ID set above cannot name it, and adding <c>None</c> to that set
    /// would make EVERY unnamed window in the room permanent. It is matched by
    /// <see cref="IsQuestLogWindow"/> instead — see there for why that test is the stable one.</para>
    ///
    /// <para>THE CONSEQUENCE CHECK, BECAUSE THE 185 RULE EXISTS FOR A REASON AND THE REASON DOES NOT
    /// TRANSFER. 185 made the character screen permanent because closing it SPLIT it: a nested
    /// <c>UIWindow</c> (PartyAssemblyWindow) was being suppressed by the parent-wins rule and became
    /// eligible the moment the parent went away. THE QUEST LOG HAS NO SUCH CHILD. Its own subtree is
    /// <c>UIQuestLogGroup</c> / <c>UIQuestLogSlot</c> — plain MonoBehaviours, not windows — and the
    /// hardware log confirms the shape from the other side: the quest popup that a quest click opens
    /// is <c>UI Quest Preview Popup</c> at path <c>Map Canvas/Quest UI/UI Quest Preview Popup</c>,
    /// a SIBLING of <c>Map Canvas/Quest UI/Quest Log Manager</c> owned by
    /// <c>Singleton&lt;UIQuestPopupManager&gt;</c>, never a child of the log. So permanence here buys
    /// the ruling and cannot buy the 185 failure mode with it.</para>
    ///
    /// <para>WHAT PERMANENCE DOES COST, STATED PLAINLY. The game hides the quest log on its own in
    /// several flows (<c>QuestManager.OnMapLocationQuestSelected</c> → <c>HideLogScreen</c> when a
    /// quest is picked, <c>OnPartyMove</c>, city events, level-up, reward distribution). In the map
    /// room this window is already STICKY (<see cref="MapRoomParallel"/>, ModBuild 180), so the mod
    /// already re-shows it against those hides — that behaviour is UNCHANGED by this rule. What
    /// changes is only that the player can no longer take it down himself: no X, refused by
    /// <see cref="CloseFloatedWindow"/> and skipped by the escape chord. That is exactly the ruling.
    /// </para>
    ///
    /// <para>AND IT IS NOT A TRAP, WHICH IS THE ONE THING A PERMANENT WINDOW MUST NEVER BECOME.
    /// Three independent exits are untouched. (1) The quest log is NON-BLOCKING: it is not in
    /// <see cref="ConfirmationFamily"/> and not a hover card, so <see cref="MapRoomParallel"/> is
    /// true for it and <see cref="IsBlockingWindow"/> is false — it never raises the ModalUI lock,
    /// so it cannot freeze the board, the room or any other window. (2) The escape chord CONTINUES
    /// past it (see <see cref="CloseTopModal"/>) instead of stopping on it, so every other floated
    /// window is still chord-closable. (3) The pause menu is reached through
    /// <c>Singleton&lt;ESCMenu&gt;</c> / <see cref="OptionsToggle"/>, which neither consults this
    /// predicate nor requires any float to close first — and the quest log does not subscribe to
    /// <c>ESCMenu.OnShown</c> the way <c>NewPartyDisplayUI</c> does, so opening the pause menu
    /// neither closes it nor is blocked by it. Leaving the map room goes through the pause menu and
    /// through the room's own teardown, both of which release floats wholesale rather than asking
    /// this predicate.</para>
    /// </summary>
    internal static bool IsMapRoomPermanent(UIWindow? window) =>
        window != null
        && MapRoom.MapRoomDriver.Active
        && (MapRoomPermanentIds.Contains(window.ID) || IsQuestLogWindow(window));

    /// <summary>
    /// IS THIS WINDOW THE QUEST LOG — asked as an IS-A question on the window's OWN GameObject, for
    /// the reason <see cref="IsMapRoomHoverCard"/> spells out at length: ModBuild 181/182 shipped
    /// <c>GetComponentIn{Parent,Children}</c> twice and both times it answered "related to an X"
    /// when the question was "IS this an X", which flew the whole character UI over an icon.
    ///
    /// <para>THE TEST IS <c>window.GetComponent&lt;QuestLogManager&gt;()</c>, AND IT IS EXACT RATHER
    /// THAN MERELY LIKELY, because the game declares the pairing itself: <c>QuestLogManager</c> is
    /// <c>[RequireComponent(typeof(UIWindow))]</c> and caches <c>window = GetComponent&lt;UIWindow&gt;()</c>
    /// in its own <c>Awake</c> (decompiled QuestLogManager.cs:15-16, :59, :67). Unity's RequireComponent
    /// guarantees the two components share ONE GameObject, so "the UIWindow that has a QuestLogManager
    /// on it" and "the quest log's window" are the same object by construction, and a GetComponent on
    /// the window's own GameObject cannot reach any other window's parts.</para>
    ///
    /// <para>WHY NOT BY NAME, WHICH IS THE OBVIOUS SHORTCUT AND THE WRONG ONE. The log line that
    /// found this window reads <c>'Quest Log Manager'</c>, but a name match is fragile in three
    /// separate ways this project has already been bitten by: the game localises its UI (the user's
    /// own report names the German group headers 'Weltquests' / 'Abgeschlossene Quests', which are
    /// <c>GUI_QUEST_GROUP_*</c> translations), Unity appends <c>(Clone)</c> to instantiated copies,
    /// and a prefab variant may be renamed by an asset update without any code change. A component
    /// type survives all three. It also survives the ID being <b>None</b>, which is the whole reason
    /// the ID set could not be used.</para>
    ///
    /// <para>SCOPE: this predicate says nothing on its own — it is only ever read through
    /// <see cref="IsMapRoomPermanent"/>, which additionally requires the map room to be standing. In
    /// a scenario, on the flat screen, and with VR off, the quest log keeps byte-identical vanilla
    /// behaviour including its X.</para>
    /// </summary>
    internal static bool IsQuestLogWindow(UIWindow? window) =>
        window != null && window.GetComponent<QuestLogManager>() != null;

    /// <summary>
    /// Which permanence rule a window matched, phrased for the hardware log so a refusal line names
    /// the family and its reason rather than asserting "the character screen" for whatever it caught.
    /// Only meaningful when <see cref="IsMapRoomPermanent"/> is true.
    /// </summary>
    internal static string MapRoomPermanentReason(UIWindow? window) =>
        IsQuestLogWindow(window)
            ? "it is the QUEST LOG (matched by its own QuestLogManager component, not by name or ID — "
              + "its UIWindowID is None), which has no X and is not closable in the map room (user "
              + "ruling). Unlike the character screen this window has no nested UIWindow to strand: "
              + "the quest popup is a SIBLING under 'Map Canvas/Quest UI', so nothing splits here."
            : "it is the map room's CHARACTER SCREEN, which has no X and is not closable in this "
              + "phase (user ruling). Closing it would strand its nested character display as a "
              + "separate window, which is the split he reported.";

    /// <summary>
    /// A TRANSIENT ANNOUNCEMENT (ModBuild 230) — a one-shot popup the game puts up to TELL the
    /// player something and takes away again on the next click, as opposed to a window the player
    /// opened and owns.
    ///
    /// <para>USER RULING, verbatim: <i>"Als ich von einem Szenario in die Map-Umgebung gewechselt
    /// bin wurde ein neues Szenario freigeschaltet. Dafür erschien ein neues Fenster, statt das
    /// Fenster zu schließen habe ich auf die Info geklickt. Dadurch ist die Info verschwunden aber
    /// nicht das Fenster. Daher a) Solche flüchtigen Infos sollten nicht mit dem 'x' schließbar
    /// sein, es soll eher als Dialog behandelt werden was damit geschlossen wird, wenn der user
    /// draufklickt."</i></para>
    ///
    /// <para>THIS IS NOT A SECOND CATEGORY FOR AN EXISTING ONE. The mod already keeps a family of
    /// windows that float WITHOUT an X, each with its own reason: the click-through story box, the
    /// end-of-scenario results windows, the reward showcase, scripted level messages and the map
    /// room's permanent screens (the chain in <c>ModalFallback.8.Convert</c>). What none of them
    /// covered is a popup that is neither a decision nor a permanent surface — an announcement. It
    /// joins that same chain rather than getting a mechanism of its own, and it is the only member
    /// whose reason is "it is over as soon as you touch it".</para>
    ///
    /// <para>THE ONE PROVEN MEMBER IS THE UNLOCK-LOCATION FLOW, and it is matched by COMPONENT on
    /// the window's OWN GameObject — the IS-A form of the question, for the reason
    /// <see cref="IsMapRoomHoverCard"/> spells out (this project has shipped
    /// <c>GetComponentIn{Parent,Children}</c> twice where it meant "IS an X"). The pairing is
    /// provable from the decompile plus the ModBuild 229 hardware log:
    /// <c>UIUnlockLocationFlowManager</c> is <c>[RequireComponent(typeof(ControllerInputAreaLocal))]</c>
    /// and takes that component off its own GameObject in <c>Awake</c>
    /// (decompiled/GH.Runtime/UIUnlockLocationFlowManager.cs:14/50), and the log's controller-area
    /// registration names that object — <c>Register area Unlock location (object UI Unlock Locations
    /// Flow Manager (ControllerInputAreaLocal))</c> — as the SAME object the mod floats as the
    /// UIWindow <c>'UI Unlock Locations Flow Manager'</c> (ID UnlockQuestPopup). If the pairing were
    /// ever to change, this returns false and the window keeps its X: the fail-safe direction is
    /// the status quo, not a window with no way out.</para>
    ///
    /// <para>WHY THE POPUP IS TRANSIENT AND NOT MERELY SMALL, from the flow manager itself:
    /// <c>ShowUnlockedLocations</c> calls <c>window.Show()</c> ONCE for a whole sequence and
    /// <c>window.Hide()</c> once at its end (:78, :117); in between, each unlocked location is a
    /// <c>popup.Show(quest)</c> / <c>popup.Hide()</c> pair driven by a promise chain that only
    /// advances when <c>Continue()</c> runs (:139-160). <c>Continue</c> is wired to the flow's own
    /// <c>continueButton.onClick</c> (:48) and to <c>KeyAction.UI_SUBMIT</c> (:52). So the window is
    /// a FRAME around content the game swaps and blanks on its own — which is exactly why it was
    /// left standing with nothing in it.</para>
    ///
    /// <para>NOT INCLUDED, AND CHECKED RATHER THAN ASSUMED: <see cref="UIWindowID.IntroductionScreen"/>.
    /// The FTUE concept screens are shown through <c>UIIntroductionManager</c>, which owns a
    /// <c>LevelMessageUILayoutGroup</c> and drives it with <c>layoutGroup.Show(message,
    /// onClosedPressedAction, autocloseCondition)</c> (decompiled/GH.Runtime/GLOO.Introduction/
    /// UIIntroductionManager.cs:29/99) — i.e. they are level MESSAGES, and there is a standing user
    /// ruling for those ("the player MUST engage with a tutorial hint", the <c>isLevelMsg</c> arm of
    /// the X chain). Adding them here would quietly overturn that ruling, and a trade-off written in
    /// a comment is not one the user agreed to.</para>
    ///
    /// <para>MULTIPLAYER: nothing new goes on the wire for this. A transient announcement is LOCAL
    /// PRESENTATION of a fact the game already synchronises — the unlock itself travels as game
    /// state, and both the dismiss and the flow it advances run inside the game through its own
    /// button, so every peer's flow manager reaches the same place by the same route it does on the
    /// flat screen. This predicate reads only local scene components and sends nothing.</para>
    /// </summary>
    internal static bool IsTransientAnnouncement(UIWindow? window) =>
        window != null && window.GetComponent<UIUnlockLocationFlowManager>() != null;

    /// <summary>Which transient rule a window matched, phrased for the hardware log. Only meaningful
    /// when <see cref="IsTransientAnnouncement"/> is true — and phrased as what was MEASURED (a
    /// component on this GameObject), not as a claim about what the window will do next.</summary>
    internal static string TransientAnnouncementReason(UIWindow? window) =>
        "it is the UNLOCK-LOCATION announcement (matched by a UIUnlockLocationFlowManager on the "
        + "window's own GameObject). The flow shows the window ONCE for a whole sequence and swaps "
        + "the popup inside it per location, so the window is a frame around content the game "
        + "blanks on its own — a fleeting info, not a window the player owns";

    /// <summary>
    /// THE TRANSIENT'S REPLACEMENT FOR THE X: a full-host, invisible catcher that forwards a click
    /// to the GAME'S OWN dismiss button.
    ///
    /// <para>User ruling (b) of the same report: <i>"es soll eher als Dialog behandelt werden was
    /// damit geschlossen wird, wenn der user draufklickt."</i> On the flat screen that dismissal is
    /// a click on the flow's <c>continueButton</c>, and this dispatches EXACTLY that: an
    /// <c>ExecuteEvents</c> pointer click at the game's Button, which is the same event the uGUI
    /// input module delivers there. Nothing mod-side is hidden and no game state is written, so the
    /// flow manager's promise chain advances the way it always does — this project's rule is to
    /// commit through UI seams, and inventing a mod-side "hide it" would leave the flow thinking
    /// its popup is still up.</para>
    ///
    /// <para>IT CANNOT STEAL A CLICK FROM THE GAME. The catcher is made the FIRST child of the host,
    /// and a uGUI <see cref="GraphicRaycaster"/> orders its hits by graphic depth — a graphic drawn
    /// earlier loses to one drawn later. So every real game element under the window still wins its
    /// own clicks; the catcher only receives what landed on the window and on nothing interactive.
    /// It is named with the <c>GloomhavenVR.</c> prefix, which is the prefix both the content fit
    /// and the liveness measurement already skip, so it can never make an empty window look full.</para>
    ///
    /// <para>AND IT RESPECTS THE GAME'S OWN GATING. The dismiss target is resolved AT CLICK TIME and
    /// only an <c>interactable</c> Button is accepted, because the flow deliberately turns its
    /// continue button off while a camera focus is in flight (UIUnlockLocationFlowManager.cs:142/150,
    /// back on at :157). A click during that window does nothing here for the same reason it does
    /// nothing on the flat screen.</para>
    /// </summary>
    private static void AttachTransientDismiss(ConvertedPanel panel, UIWindow window)
    {
        if (panel == null || panel.HostRect == null || window == null)
            return;
        try
        {
            int layer = panel.HostGo != null ? panel.HostGo.layer : 5;
            var go = new GameObject("GloomhavenVR.TransientDismiss") { layer = layer };
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(panel.HostRect, worldPositionStays: false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);
            rect.SetAsFirstSibling(); // loses every raycast tie to real game content — see the doc
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f); // invisible; Graphic raycasting ignores alpha
            img.raycastTarget = true;
            UIWindow target = window;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => DismissTransient(target));
            VRLog.Info("WorldUI", $"MODAL TRANSIENT DISMISS: '{window.name}' (ID {window.ID}) carries a "
                                  + "full-host click catcher instead of an X — a click anywhere on it "
                                  + "that no game element claimed is forwarded to the window's own "
                                  + "dismiss Button as an ExecuteEvents pointer click, i.e. the same "
                                  + "dispatch the flat screen delivers. The catcher is the host's FIRST "
                                  + "child, so every real game widget still wins its own clicks.");
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"MODAL TRANSIENT DISMISS: could not build the click catcher for "
                                  + $"'{window.name}' ({ex.GetType().Name}: {ex.Message}) — the window's "
                                  + "own button still dismisses it, and the escape chord still closes it.");
        }
    }

    /// <summary>Forward a catcher click to the window's own dismiss Button. Resolved per click (the
    /// game toggles <c>interactable</c> as its flow advances) and logged once per resolution change,
    /// so the log names the button that was actually driven rather than the one we hoped for.</summary>
    private static void DismissTransient(UIWindow window)
    {
        if (window == null)
            return;
        Button? dismiss = null;
        TransientButtonScratch.Clear();
        window.GetComponentsInChildren(includeInactive: false, TransientButtonScratch);
        for (int i = 0; i < TransientButtonScratch.Count; i++)
        {
            Button b = TransientButtonScratch[i];
            if (b == null || !b.isActiveAndEnabled || !b.IsInteractable())
                continue;
            if (b.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                continue; // our own catcher — forwarding to it would be a loop
            dismiss = b;
            break;
        }
        if (dismiss == null)
        {
            VRLog.Info("WorldUI", $"MODAL TRANSIENT DISMISS: click on '{window.name}' (ID {window.ID}) "
                                  + $"found NO interactable Button among {TransientButtonScratch.Count} "
                                  + "under the window, so nothing was dispatched. That is the correct "
                                  + "outcome while the game has its own continue button switched off "
                                  + "(the unlock flow does exactly that during a camera focus); it is "
                                  + "also what this line would say if the window simply has no button.");
            return;
        }
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(dismiss.gameObject, data, ExecuteEvents.pointerClickHandler);
        VRLog.Info("WorldUI", $"MODAL TRANSIENT DISMISS: click on '{window.name}' (ID {window.ID}) "
                              + $"forwarded to the game's own '{dismiss.gameObject.name}' Button as a "
                              + "pointer click. The mod hid nothing and wrote no game state — whatever "
                              + "the game does next (advance its flow, hide the window) is the game's.");
    }

    /// <summary>Scratch for the dismiss-target resolution (per click, never per frame).</summary>
    private static readonly List<Button> TransientButtonScratch = new(8);

    /// <summary>The map room's un-closable screen: the party display and the assembly window it
    /// carries. The quest log belongs to the same rule but cannot be named here (its ID is None) —
    /// see <see cref="IsQuestLogWindow"/>.</summary>
    private static readonly HashSet<UIWindowID> MapRoomPermanentIds = new()
    {
        UIWindowID.PartyPanel,
        UIWindowID.PartyAssemblyWindow,
    };

    /// <summary>
    /// THE PARENT WINS ON THE ENROLLED PATH TOO (ModBuild 196) — the equipment tab.
    ///
    /// <para>USER REPORT, verbatim: <i>"Kannst du bitte das Ausrüstungsmenü an das Character-UI
    /// Fenster hängen statt dass es ein eigenes Fenster ist? Es ist ja quasi ein TAB der rechts an
    /// den Charakteren hängt wenn man ihn öffnet — genau wie die Karten und die Charakter-Detail-
    /// Ansicht. Die anderen Elemente öffnen sich korrekt im Fenster; das Ausrüstungsmenü ist die
    /// Ausnahme."</i></para>
    ///
    /// <para>WHAT THE EQUIPMENT PANEL IS, from the decompiled game. <c>NewPartyDisplayUI</c> — the
    /// map room's character screen — serialises FIVE sub-views side by side
    /// (NewPartyDisplayUI.cs:70/76/79/82/85): <c>abilityCardsDisplay</c>
    /// (<c>UIPartyCharacterAbilityCardsDisplay</c>), <c>perkManager</c> (<c>UIPerksWindow</c>),
    /// <c>characterSelector</c> (<c>UIAdventurePartyAssemblyWindow</c>), <c>itemInventoryDisplay</c>
    /// (<c>UIPartyCharacterEquipmentDisplay</c>) and <c>battleGoalSelector</c>
    /// (<c>UIBattleGoalPickerWindow</c>). They are ONE screen's tabs: <c>OnItemsSelected</c> and
    /// <c>OnCardsSelected</c> are the same method with a different sub-view
    /// (NewPartyDisplayUI.cs:1028/922), both route through the same <c>TryHideCurrentDisplay</c>
    /// single-tab discipline, and each sub-view aims a <c>VerticalPointerUI</c> at the character row
    /// that opened it (UIPartyCharacterEquipmentDisplay.cs:492 <c>verticalPointer.PointAt(sourceUI)</c>,
    /// which copies the button's WORLD y — a construction that only means anything while the two
    /// live on the same canvas).</para>
    ///
    /// <para>FOUR OF THOSE FIVE ARE PROVEN CHILDREN OF THE FLOATED CHARACTER SCREEN, from the
    /// ModBuild 195 hardware log (<c>.planning/debug/LogOutput.log</c>) — the party display's own
    /// host measures them, adopts them, or was shown to strand them:</para>
    /// <code>
    ///   Adopted nested canvas 'UI Battle Goal Picker Window' in
    ///     'GloomhavenVR.Panel_Modal_New Party display'                      ← battleGoalSelector
    ///   Host rect fit '…Panel_Modal_New Party display': 328x1080 → 1920x1080 px …
    ///     top (rendered rects): 'New Party display/New UIPerksWindow V…'    ← perkManager
    ///   Host rect fit '…Panel_Modal_New Party display': 328x1080 → 1066x1080 px …
    ///     top (rendered rects): 'Character Ability Cards Display Variant/Container'
    ///                                                                       ← abilityCardsDisplay
    ///   (ModBuild 184 log, quoted on IsMapRoomPermanent: closing 'New Party display' made
    ///    'Campaign Adventure Party Assembly Variant' float on its own)       ← characterSelector
    /// </code>
    /// <para>The party display's own rect follows them in place — 328x1080 with no tab open,
    /// 1066x1080 with the cards tab, 716x1080 with the enhancement tab — and its HIT RECT follows
    /// with it (<c>HIT RECT '…New Party display' … DRAWN CONTENT 716x1080 … GROWN</c>). The fifth
    /// sub-view, the equipment tab, is the ONLY one enrolled in <see cref="FallbackIds"/>, and the
    /// enrolled poll runs BEFORE the catch-all where "the parent wins" lives — so it alone was
    /// pulled out of the screen and re-parented onto a world host of its own
    /// (<c>MODAL FALLBACK: window 'Character Items Equipment Content' (ID EquipmentItemsPanel)
    /// opened without a VR conversion … → ModalUI + floating window</c>), which is the second window
    /// in his screenshot.</para>
    ///
    /// <para>WHY IT WAS ENROLLED, AND WHETHER THAT REASON STILL HOLDS. The recorded reason is the
    /// whole of the annotation on the entry: <i>"MODAL: UIPartyItemInventoryDisplay — equip/remove
    /// item slots (:136); internal hover is tooltips only, never the window's Show."</i> That is an
    /// answer to the test #16/#18 question <i>"is this a hover surface that would self-lock, or a
    /// real interactive window?"</i> — asked of a set the set's own doc-comment scopes to windows
    /// <i>"that demand user interaction when opened DURING A SCENARIO"</i>. It is a classification,
    /// not a placement ruling: nothing there says the panel should be its own window, and the map
    /// room did not exist when it was written. The classification still holds (the panel IS
    /// interactive and IS not hover-shown) and is left standing — this rule changes only WHERE an
    /// enrolled window is presented when it turns out to be a sub-view of a screen that is already
    /// floated.</para>
    ///
    /// <para>AND IT NEEDS A REAL PARENT — the ModBuild 184 lesson, which is why this asks for a LIVE
    /// FLOATED HOST rather than an open one. Suppressing a window because "the parent handles it"
    /// showed the merchant NOWHERE when the parent was open but permanently un-floatable. Here the
    /// test is <see cref="IsLiveFloatedHost"/>: an ancestor <c>UIWindow</c> that is in
    /// <see cref="Converted"/> with a living panel and is not on its way out — i.e. a world-space
    /// host that demonstrably exists this instant. If no such ancestor is found the window floats
    /// exactly as it did before, so a hierarchy that is not what the log says costs nothing.</para>
    ///
    /// <para>LEVEL-TRIGGERED, like every other rule in this class: the window stays in
    /// <see cref="Open"/> and this is re-asked every tick, so it floats by itself the moment its
    /// host stops being one.</para>
    ///
    /// <para><b>IT WAS MAP-ROOM SCOPED UNTIL ModBuild 226, AND THE REASON RECORDED FOR THAT SCOPE IS
    /// NO LONGER TRUE.</b> The sentence that stood here read: <i>"MAP-ROOM SCOPED for the same reason
    /// the catch-all's rule is: in a scenario the flat screen composites whatever is not floated, so
    /// nesting is not a visual problem there and a change would have no report behind it."</i> Both
    /// halves have since been falsified. (1) IN A SCENARIO THE MOD FLOATS WINDOWS TOO — the ModBuild
    /// 225 hardware log has <c>MODAL WINDOW: 'Story Window' (ID None) floated in front of the HMD</c>
    /// with <c>room=True, scenario=True</c>, its own <c>MODAL GRAB</c> and its own shared bar; the
    /// flat screen composites only what FAILED to convert. So a nested <c>UIWindow</c> opening inside
    /// a scenario window did not quietly render inside its parent's composite — it floated as a
    /// second world panel with a second grab bar. (2) THERE IS NOW A REPORT BEHIND IT, verbatim:
    /// <i>"Beim Storyfenster war der Dialog/Untertitel ein eigenes Fenster und später bei der
    /// Begegnung wurde das auch in mehrere Fenster statt einem einzigen getrennt! Das soll nicht
    /// sein."</i> Both of those are scenario windows. The gate is therefore gone: the parent wins
    /// wherever the parent is a LIVE FLOATED HOST, which is the condition the rule was always really
    /// about — the map room was only the first place a floated parent existed.</para>
    ///
    /// <para>THE BLAST RADIUS IS BOUNDED BY THE SAME TWO THINGS IT ALWAYS WAS, and they are stronger
    /// than the room gate was. The parallel-window families are still exempt (the two sets below),
    /// and the ancestor must still pass <see cref="IsLiveFloatedHost"/> — an ancestor that is merely
    /// OPEN, or open-but-unfloatable, buys the child nothing (the ModBuild 184 "parent wins needs a
    /// real parent" lesson). A window with no floated ancestor is untouched, which is every window in
    /// the flat game and every window in a scenario whose parent the mod does not float.</para>
    ///
    /// <para>AND THE GROUPS THE GAME BUILDS OUT OF SIBLINGS (ModBuild 226, second arm). Hierarchy is
    /// the game's usual way of saying "one screen", but not its only one: the multiplayer
    /// "Quest wählen" confirm is a ROOT under <c>Campaign Canvas</c>, a sibling of the quest window
    /// it belongs to. Those cannot be found by walking parents, so they are DECLARED —
    /// see <see cref="WindowGroups"/> — and answered here through
    /// <see cref="TryFindFloatedGroupLeader"/>, so that both call sites of this predicate get one
    /// answer to one question ("is this window part of a panel that already stands?") instead of
    /// two rules that can disagree.</para>
    ///
    /// <para>MULTIPLAYER: nothing here goes on the wire. It decides which local GameObject a local
    /// uGUI subtree is drawn under; no game state, no <c>NetProtocol</c> surface.</para>
    /// </summary>
    internal static bool RendersInsideFloatedAncestor(UIWindow? window)
    {
        if (window == null)
            return false;
        // BLAST-RADIUS BOUND: the pause/Options/Compendium family is EXEMPT. Those are the windows
        // ModBuild 180's ruling is about ("Anders als in Flat soll es hier möglich sein mehrere
        // Fenster parallel offen zu haben") — windows the player opens deliberately and stands next
        // to each other as objects on the table, never tabs of another screen. Reading the game's
        // hierarchy for them is unnecessary risk: ESCMenu reaches UIOptionsWindow through
        // Singleton, not a serialized child (ESCMenu.cs:137), and MainOptionOptions re-parents that
        // window at RUNTIME in the main menu (MainOptionOptions.cs:20-25), i.e. its parent is not a
        // stable thing to make a placement decision from. This rule exists for sub-views of ONE
        // screen; it does not get to reinterpret the parallel-windows family.
        if (NonBlockingMenus.Contains(window.ID) || MultiplayerRosterMenus.Contains(window.ID))
            return false;
        for (Transform? t = window.transform.parent; t != null; t = t.parent)
        {
            var above = t.GetComponent<UIWindow>();
            if (above == null || ReferenceEquals(above, window) || !IsLiveFloatedHost(above))
                continue;
            if (NestedSubViewLogged.Add(window.name))
                VRLog.Info("WorldUI", $"MODAL FALLBACK: '{window.name}' (ID {window.ID}) is NOT floated as "
                                      + $"a window of its own — it is a SUB-VIEW nested inside '{above.name}' "
                                      + $"(ID {above.ID}), which is already a live floated host, so this "
                                      + "subtree is drawn and hit-tested INSIDE that window, where the game "
                                      + "lays it out. The parent wins (ModBuild 181/184), now on the enrolled "
                                      + "path too (ModBuild 196 — the equipment tab). The host's content fit "
                                      + "and hit rect grow to include it and shrink again when it closes; the "
                                      + "host is never re-placed (windows do not move once spawned).");
            return true;
        }
        // SECOND ARM: the declared sibling groups. Asked LAST so the hierarchy — the game's own
        // statement of what belongs to what — always wins, and the table only ever answers for the
        // windows the hierarchy cannot.
        return RendersInsideFloatedGroup(window);
    }

    /// <summary>
    /// THE DECLARED ARM, on its own so the catch-all path and the enrolled path ask ONE question and
    /// get ONE answer. True when <paramref name="window"/> is a declared group MEMBER whose LEADER is
    /// a live floated host right now — see the block below for the table and its evidence.
    /// </summary>
    internal static bool RendersInsideFloatedGroup(UIWindow? window)
    {
        if (window == null)
            return false;
        if (!TryFindFloatedGroupLeader(window, out UIWindow? leader, out string why))
            return false;
        if (NestedSubViewLogged.Add(window.name))
            VRLog.Info("WorldUI", $"MODAL FALLBACK: '{window.name}' (ID {window.ID}) is NOT floated as "
                                  + "a window of its own — it is a DECLARED MEMBER of the panel led by "
                                  + $"'{leader!.name}' (ID {leader.ID}), which is already a live floated "
                                  + $"host. {why} The group table (ModalFallback.WindowGroups) exists "
                                  + "because the game builds this particular unit out of SIBLINGS rather "
                                  + "than out of a parent and its children, so no walk over the hierarchy "
                                  + "can find it.");
        return true;
    }

    // ---- ONE LOGICAL PANEL = ONE VR WINDOW: the declared sibling groups ------------------------
    //
    // USER REPORT (2026-08-22), verbatim: "Im Test waren der 'Quest Beginnen'-Button und das
    // Quest-Fenster separate 'Fenster' - nicht wie zuvor wie gewollt, dass der Button auf der Quest
    // angezeigt wird. Siehe Button_getrennt.jpg."
    //
    // WHAT THE ModBuild 225 HARDWARE LOG SAYS HAPPENED, at the exact instant of that screenshot —
    // the co-player joins, and the confirm floats as a window of its own:
    //
    //     Number of players: 2
    //     UIWindow SHOWN: 'Multiplayer Ready Toggle' (ID None, room=True, scenario=False …)
    //     UIWindow SHOWN: 'UI Quest Popup' (ID QuestPopup, room=True, scenario=False …)
    //     MODAL GRAB:   'UI Quest Popup' is now a grabbable/scalable world element
    //     MODAL WINDOW: 'UI Quest Popup' (ID QuestPopup) floated in front of the HMD …
    //     CATCH-ALL: unknown scenario window 'Multiplayer Ready Toggle' (ID None) floated …
    //     MODAL GRAB:   'Multiplayer Ready Toggle' is now a grabbable/scalable world element
    //     MODAL WINDOW: 'Multiplayer Ready Toggle' (ID None) floated in front of the HMD …
    //
    // and the identity line names the shape of the problem:
    //
    //     WINDOW IDENTITY 'Multiplayer Ready Toggle' (ID None): path Campaign Canvas/Multiplayer
    //     Ready Toggle; rect 307x65; components [… UIWindow, ExtendedToggle, UIReadyToggle …];
    //     nearest ancestor UIWindow <none>.
    //
    // A 307x65 strip, a ROOT under Campaign Canvas, with NO ancestor window — so "the parent wins"
    // could never have caught it, and it is not enrolled either: the CATCH-ALL floated it.
    // Its label is "Quest wählen": MapChoreographer.InitializeSelectQuestReadyUp initialises
    // Singleton<UIReadyToggle> with the localisation keys "GUI_SELECT_QUEST" / "GUI_CANCEL"
    // (decompiled MapChoreographer.cs:3575-3600), and the HOST branch's all-ready callback is
    // Singleton<AdventureMapUIManager>.Instance.ConfirmTravel() with AdventureMapUIManager.CheckTravel
    // as its validator. ONLINE, THIS TOGGLE *IS* THE TRAVEL CONFIRM — the same act the offline
    // 'Adventure button' performs, and MapTravelConfirm has been parking that button inside the quest
    // window since ModBuild 190. That is why it belongs on the quest card rather than beside it, and
    // it is the game itself that says so.
    //
    // THE TABLE IS DATA. Each row names a LEADER (the window that floats and hosts) and a MEMBER
    // (the window that must not float), both by IS-A component test on the window's OWN GameObject.
    // Never by name: the game localises its UI, Unity appends "(Clone)", and a prefab variant can be
    // renamed by an asset update — the ModBuild 194 argument on IsQuestLogWindow, word for word.
    // Never by UIWindowID either, because the id is None for the ready toggle and for most of the
    // windows this rule will ever be asked about.

    /// <summary>
    /// One declared grouping: two game <c>UIWindow</c>s the game builds as SIBLINGS but presents as
    /// one visual unit. Matched by component type on each window's own GameObject — see the block
    /// above for why not by name and not by id.
    /// </summary>
    private readonly struct WindowGroupRule
    {
        /// <summary>The window that floats and hosts the group.</summary>
        public readonly System.Type Leader;

        /// <summary>The window that must not float on its own while the leader stands.</summary>
        public readonly System.Type Member;

        /// <summary>
        /// Does the thing that RE-HOMES this member only exist while the 3D map room stands?
        ///
        /// <para>THIS FIELD IS THE ModBuild 184 LESSON WRITTEN INTO THE TABLE. Suppressing a window
        /// because "the leader handles it" is only allowed while the leader DEMONSTRABLY handles it;
        /// an open-but-unfloatable ancestor once showed the merchant nowhere. For the hierarchy arm
        /// the host does the handling by simply being the child's parent, so the liveness test is
        /// enough. For a SIBLING there is no such automatic mechanism: something has to move the
        /// member, and today the only thing that does is <c>MapTravelConfirm</c>, which runs from
        /// <c>MapRoomDriver.TickActive</c> and parks nothing outside the room. Suppressing the member
        /// where nothing can re-home it would make the game's own confirm unreachable — a strictly
        /// worse bug than the split it is meant to fix.</para>
        /// </summary>
        public readonly bool RequiresMapRoom;

        /// <summary>The evidence, printed verbatim into the suppression line.</summary>
        public readonly string Why;

        public WindowGroupRule(System.Type leader, System.Type member, bool requiresMapRoom, string why)
        {
            Leader = leader;
            Member = member;
            RequiresMapRoom = requiresMapRoom;
            Why = why;
        }
    }

    /// <summary>
    /// The declared sibling groups. ONE row today, because one is what the hardware log proves; a
    /// row with no log line behind it would be a guess sitting in a table that reads like a fact.
    ///
    /// <para><b>ModBuild 231 — THE SENTENCE THAT STOOD HERE WAS WRONG AND IS RECORDED SO IT IS NOT
    /// RE-ASSERTED.</b> It read: <i>"The two other splits in the same report — the story box and its
    /// subtitle, and the encounter — are HIERARCHY groups and are answered by the first arm of
    /// <see cref="RendersInsideFloatedAncestor"/>, which stopped being map-room-scoped in the same
    /// build. Do not add rows for them: a declared row would shadow the game's own hierarchy."</i>
    /// The quest-intro split (<c>.planning/debug/getrennt2.jpg</c>) is neither a hierarchy group nor
    /// answerable by a declared row:</para>
    /// <list type="number">
    /// <item>NOT A HIERARCHY. The picture lives in <c>Campaign Canvas/UI Loadout Window</c>
    /// (<c>UILoadoutManager</c>) and the dialog in <c>Story Canvas/Map Story Window</c>
    /// (<c>MapStoryController</c>); the ModBuild 231 log's identity line for BOTH ends with
    /// <c>nearest ancestor UIWindow &lt;none&gt;</c>. There is no parent to walk to.</item>
    /// <item>NOT ANSWERABLE BY A ROW EITHER, because of the ORDER. Both arms of this predicate are
    /// only ever consulted from <c>CatchAllEligible</c>, and the catch-all re-adds a window it is
    /// ALREADY floating before every eligibility test (<c>oursAlready</c>, ModBuild 186's
    /// oscillation fix). The picture's window floats FIRST and the dialog's arrives 0.4 s later
    /// (<c>UILoadoutQuestWindow.delayToShowText</c>), so by the time a leader exists the member is
    /// past the only gate that could refuse it. Adding a row would have shipped a table entry that
    /// reads like a fact and does nothing.</item>
    /// </list>
    /// <para>That split is therefore handled by <see cref="StoryComposite"/>, which is an ACTIVE step
    /// (it moves the picture and releases the float that was holding it) rather than a predicate.
    /// The ENCOUNTER, the third item of that old sentence, turned out not to be a split at all: it is
    /// <c>UIEventPanel</c>, ONE window with its own image and text, and what it needed was the shared
    /// bar and a pose (<see cref="SharedWindowKind.Encounter"/>).</para>
    ///
    /// <para><b>ModBuild 232 — AND THE ONE ROW THAT IS HERE HAS NEVER ONCE FIRED FOR THE WINDOW IT
    /// WAS WRITTEN FOR, FOR THE ORDERING REASON THE LIST ABOVE ALREADY NAMES.</b> The row was added
    /// in ModBuild 226 to stop 'Multiplayer Ready Toggle' floating beside the quest card, and the
    /// ModBuild 231 hardware log shows it floating anyway: <c>CATCH-ALL: unknown scenario window
    /// 'Multiplayer Ready Toggle' (ID None) floated</c> (Player.log:18129), then <c>MODAL CLOSE (X
    /// button): attached to 'Multiplayer Ready Toggle'</c> — the user's
    /// <c>frei_schwebender_button_multiplayer.jpg</c>. The mechanism is item 2 of the list above,
    /// applied to this row instead of to the quest-intro split: this predicate is only ever consulted
    /// from <c>CatchAllEligible</c>, and the catch-all re-adds a window it is ALREADY floating BEFORE
    /// every eligibility test (<c>oursAlready</c>, ModBuild 186's oscillation fix). The toggle's Show
    /// fires BEFORE the quest popup's — both the 225 and the 231 logs have them in that order — so it
    /// floats on a tick when no leader exists yet, and from the next tick on it is never re-examined.
    /// A rule that can only refuse a window it is not already holding cannot answer this window at
    /// all. The refusal that DOES answer it is <see cref="FloatRefusalTable"/>, which the catch-all
    /// asks ahead of the <c>oursAlready</c> branch and which can withdraw a float that already
    /// exists. THIS ROW IS LEFT STANDING AND IS NOT REDUNDANT: it is the one that holds while the
    /// quest card is a live floated host, which is a fact about the game's own layout, whereas the
    /// table row holds while <c>MapRoom.ReadyToggleParkClaim</c> says somebody is actually drawing
    /// the button — two different questions that happen to have the same answer most of the
    /// time.</para>
    /// </summary>
    private static readonly WindowGroupRule[] WindowGroups =
    {
        new(typeof(UIQuestPopup), typeof(UIReadyToggle), requiresMapRoom: true,
            "The game's own multiplayer quest confirm ('Quest wählen', localisation key "
            + "GUI_SELECT_QUEST) is the ONLINE form of the travel button the offline flow puts on "
            + "this same card: MapChoreographer.InitializeSelectQuestReadyUp wires it straight to "
            + "AdventureMapUIManager.ConfirmTravel/CheckTravel (decompiled MapChoreographer.cs:3575). "
            + "It carries [RequireComponent(typeof(Toggle), typeof(UIWindow))], so its UIWindow and "
            + "its UIReadyToggle are ONE GameObject by construction and this test cannot reach any "
            + "other window's parts. WHERE IT IS PRESENTED INSTEAD: MapTravelConfirm parks it INSIDE "
            + "the quest window, directly under the quest information, at the same measured zero and "
            + "on the same two [WorldUI] TravelButtonOffset dials the offline button already uses."),
    };

    /// <summary>
    /// Is <paramref name="window"/> a declared group MEMBER whose LEADER is a live floated host right
    /// now? Same liveness test as the hierarchy arm (<see cref="IsLiveFloatedHost"/>) and for the same
    /// ModBuild 184 reason: suppressing a window because "the leader handles it" is only true while
    /// the leader demonstrably exists. If it does not, the member floats exactly as it did before, so
    /// a table row that turns out to be wrong about the game costs nothing.
    /// </summary>
    private static bool TryFindFloatedGroupLeader(UIWindow window, out UIWindow? leader, out string why)
    {
        leader = null;
        why = string.Empty;
        for (int r = 0; r < WindowGroups.Length; r++)
        {
            WindowGroupRule rule = WindowGroups[r];
            if (rule.RequiresMapRoom && !MapRoom.MapRoomDriver.Active)
                continue; // nothing can re-home it here — see WindowGroupRule.RequiresMapRoom
            if (window.GetComponent(rule.Member) == null)
                continue;
            for (int i = 0; i < Converted.Count; i++)
            {
                WindowPanel wp = Converted[i];
                if (wp.Window == null || wp.Grab == null || !wp.Panel.IsAlive || wp.UserClosing)
                    continue;
                if (wp.Window.GetComponent(rule.Leader) == null)
                    continue;
                leader = wp.Window;
                why = rule.Why;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Is this window the multiplayer ready-up toggle — the "Quest wählen" confirm? Asked as an IS-A
    /// question on the window's own GameObject, which <c>UIReadyToggle</c>'s
    /// <c>[RequireComponent(typeof(Toggle), typeof(UIWindow))]</c> makes exact rather than merely
    /// likely (decompiled UIReadyToggle.cs:18-19). Read by <c>MapTravelConfirm</c>, which parks it
    /// into the quest window so the group has ONE panel, ONE grab bar and ONE close affordance.
    /// </summary>
    internal static bool IsMultiplayerReadyToggle(UIWindow? window) =>
        window != null && window.GetComponent<UIReadyToggle>() != null;

    /// <summary>Is this window the map's SELECTED-quest popup — the card the confirm belongs on?
    /// Same IS-A form; <c>UIQuestPopup</c> is <c>[RequireComponent(typeof(UIWindow))]</c>
    /// (decompiled UIQuestPopup.cs:16). Note this is NOT the hover preview
    /// (<c>UIQuestPreviewPopup</c>), which is a different component on a different GameObject and is
    /// handled as a hover card.</summary>
    internal static bool IsQuestCardWindow(UIWindow? window) =>
        window != null && window.GetComponent<UIQuestPopup>() != null;

    /// <summary>Is this window a floated world-space host RIGHT NOW — converted, panel alive, and
    /// not already on its way out under the player's close? See
    /// <see cref="RendersInsideFloatedAncestor"/> for why "open" is not good enough.</summary>
    private static bool IsLiveFloatedHost(UIWindow above)
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!ReferenceEquals(wp.Window, above))
                continue;
            return wp.Panel.IsAlive && !wp.UserClosing;
        }
        return false;
    }

    /// <summary>Per-window-name latch for the sub-view Info line — one line per window type per
    /// session (cleared by <c>CatchAllReset</c> alongside the catch-all's own latch).</summary>
    private static readonly HashSet<string> NestedSubViewLogged = new();

    /// <summary>The windows the map room's parallel rule must NOT relax — see
    /// <see cref="MapRoomParallel"/>.</summary>
    private static readonly HashSet<UIWindowID> ConfirmationFamily = new()
    {
        UIWindowID.ConfirmationBox,
        UIWindowID.MainMenuConfirmationBox,
        UIWindowID.MutiplayerConfirmationBox,
        UIWindowID.CharacterConfirmationBox,
    };

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

    /// <summary>
    /// SETTINGS-SURFACE EXEMPTION (user ruling 2026-08-02): is this canvas the world-space host
    /// of the mod-owned settings UI — the floated options window that carries the injected
    /// "VR Optionen" tab with its sub-tabs (Debug, config browser, every settings sub-page)?
    ///
    /// The ruling is absolute: "das Menü dort war nicht mehr wirklich bedienbar - das soll
    /// nicht sein, es soll nie geblockt werden von irgendwas." — the settings menu must NEVER
    /// be input-blocked by any modal state the mod maintains. The hardware incident behind it:
    /// while a BLOCKING scripted level message floated (gaze-centered at
    /// <see cref="LevelMessageDistanceMeters"/>, i.e. NEARER than every other float, and
    /// hard-clamped INTO the view cone by ComputeHmdPose), its canvas plane sat between the
    /// hand ray and the open settings menu — and RayUguiDriver's nearest-canvas arbitration
    /// then delivered every hover/press to the blocking float's canvas, where regions without
    /// a real widget (transparent host apron, inert backing graphics) simply ATE the press
    /// with no fall-through. The settings tabs behind it were dead until the instruction
    /// completed.
    ///
    /// Consumed by <c>RayUguiDriver</c>: a press/hover whose nearest-canvas winner has NO
    /// interactive widget under the beam falls through to this surface when it lies farther
    /// along the same ray (see <c>TrySettingsFallThrough</c>). Scoped DELIBERATELY tight —
    /// only the Options window and its tab sub-windows (<see cref="UIWindowID.Options"/> /
    /// <see cref="UIWindowID.OptionsSubmenu"/>, the surface hosting the mod's settings tab):
    /// every other float (ESC menu, multiplayer, compendium, and every blocking game window)
    /// keeps byte-identical arbitration, and a blocking window's OWN widgets (dismiss button,
    /// story skip area) still win wherever they actually are under the beam. Local-only by
    /// construction: floats exist only on this client, nothing here is networked. The moment
    /// the options float releases, this returns false and the prior behavior is restored
    /// exactly.
    /// </summary>
    internal static bool IsSettingsSurface(Canvas? canvas)
    {
        if (canvas == null)
            return false;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Window == null || wp.UserClosing || !wp.Panel.IsAlive)
                continue;
            UIWindowID id = wp.Window.ID;
            if ((id == UIWindowID.Options || id == UIWindowID.OptionsSubmenu)
                && ReferenceEquals(wp.Panel.HostCanvas, canvas))
                return true;
        }
        return false;
    }

    /// <summary>
    /// SETTINGS-CLICK EXEMPTION companion (user ruling 2026-08-02, rounds 3+4): is this widget
    /// transform INSIDE the floated options window or the floated pause menu that hosts it —
    /// the mod-owned surfaces whose clicks must never be swallowed?
    ///
    /// WHY this exists: the round-2 laser fall-through (<see cref="IsSettingsSurface"/> +
    /// RayUguiDriver.TrySettingsFallThrough) fixed only the MOD-side arbitration — but the
    /// hardware log (ModBuild 15) proved the press was DELIVERED to the tab widget all along
    /// ("uGUI click: 'GloomhavenVR.OptionsTab'/'Cat.N'" lines with zero effect): the click died
    /// GAME-side inside <c>ExtendedToggle.OnPointerClick</c>, which early-returns when
    /// <c>InteractabilityManager.ShouldAllowClickForExtendedToggle</c> vetoes it. During a
    /// scripted tutorial an interaction profile stays loaded (LevelEventsController
    /// .MessageWasDisplayed/-Dismissed → LoadProfile/LoadDefaultMessagelessProfile) and
    /// <c>s_EventsControllerActive</c> is true, so EVERY Extended*/Tracked*/UITab widget not on
    /// an isolated control's allow list silently eats its click — including all options-window
    /// tabs and the cloned VR-tab/sub-tab toggles. <see cref="Patches.SettingsClickExemption"/>
    /// finalizes those gate methods (round 4: a finalizer, so a gate that THROWS is covered
    /// too) and consults THIS predicate to flip the veto — only for widgets under the floated
    /// pause/options windows.
    ///
    /// Scope (round 4, user ruling 2026-08-02 "das Optionsmenue soll NIEMALS blockiert sein"):
    /// the settings floats of <see cref="IsSettingsSurface"/> (Options / OptionsSubmenu — the
    /// window's own tabs AND the mod-injected VR tab clones) PLUS the floated pause menu
    /// (<see cref="UIWindowID.ESCMenu"/>). The ESC menu is the ONLY path that OPENS the options
    /// window in VR: its "Options" widget is an <c>ExtendedToggle</c> behind the exact same five
    /// gates, so a gate veto (or a gate that THROWS, see the round-4 evidence in
    /// <see cref="Patches.SettingsClickExemption"/>) on the pause menu made the options window
    /// un-OPENABLE — the second half of the ruling. Deliberately NOT widened any further: the
    /// multiplayer submenu, the compendium, quit/main-menu confirmation dialogs and every
    /// decision/targeting confirm keep byte-identical gate semantics. All floats checked here
    /// must be alive and not user-closing; the check is by TRANSFORM ANCESTRY against the
    /// float's host rect (everything the conversion reparented under the host) with the window
    /// root as fallback. The moment a float releases, this returns false for it and every gate
    /// verdict is byte-identical vanilla again — nothing is mutated, so there is nothing to
    /// restore.
    /// </summary>
    internal static bool IsUnderFloatedPauseOrSettingsWindow(Transform? widget)
    {
        if (widget == null)
            return false;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Window == null || wp.UserClosing || !wp.Panel.IsAlive)
                continue;
            UIWindowID id = wp.Window.ID;
            if (id != UIWindowID.Options && id != UIWindowID.OptionsSubmenu
                && id != UIWindowID.ESCMenu)
                continue;
            RectTransform? host = wp.Panel.HostRect;
            if (host != null && widget.IsChildOf(host))
                return true;
            if (widget.IsChildOf(wp.Window.transform))
                return true;
        }
        return false;
    }

    /// <summary>
    /// HOW MANY FLOATED WINDOWS ARE NOT <paramref name="keep"/> — the cheap question
    /// <see cref="StoryComposite"/> asks before it considers sweeping, so a gate that is already
    /// clean costs one walk over a list that is never longer than a handful and writes nothing.
    /// </summary>
    internal static int CountFloatsOtherThan(UIWindow? keep)
    {
        int n = 0;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Window == null || wp.UserClosing || !wp.Panel.IsAlive)
                continue;
            if (keep != null && ReferenceEquals(wp.Window, keep))
                continue;
            n++;
        }
        return n;
    }

    /// <summary>
    /// THE FLOATED WINDOWS, AS OBJECTS — appended to <paramref name="into"/>, skipping
    /// <paramref name="skip"/> and anything already flagged for release. ModBuild 234's story
    /// curtain takes exactly one of these snapshots, at its rising edge, and FREEZES it: see
    /// <see cref="ReleaseFloatsExcept"/>'s note for why an exclusion is safe when it is evaluated
    /// once and fatal when it is evaluated every tick.
    ///
    /// <para>This is the object-valued sibling of <see cref="CountFloatsOtherThan"/> and it is
    /// deliberately a plain reader: it closes nothing, releases nothing and writes nothing, so the
    /// caller owns the whole of the decision and the log line that goes with it.</para>
    /// </summary>
    /// <returns>How many windows were appended.</returns>
    internal static int CollectFloatedWindows(List<UIWindow> into, UIWindow? skip)
    {
        int n = 0;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Window == null || wp.UserClosing || !wp.Panel.IsAlive)
                continue;
            if (skip != null && ReferenceEquals(wp.Window, skip))
                continue;
            into.Add(wp.Window);
            n++;
        }
        return n;
    }

    /// <summary>
    /// The <see cref="ConvertedPanel"/> this window is floated in, or null when it is not floated.
    ///
    /// <para>Exists so <c>StoryComposite</c> can ask <c>CanvasConversion.CountsAsFitContent</c> —
    /// THE FIT'S OWN VISIBILITY VERDICT — which graphics of the story window are actually PAINTED.
    /// [[tight-box-is-not-the-rect]]: ModBuild 231-233 placed the quest picture against
    /// <c>MapStoryController.dialogBox</c>'s authored RectTransform, which is a tall, mostly
    /// transparent host; the drawn dialog is the small 'Dialog/DialogContent' rect at its very
    /// bottom, and the difference was 786-888 authored px of black between the picture and the text.
    /// A panel is what that verdict needs (it does the clipper and authored-offset arithmetic
    /// host-relative), so it is handed out here rather than re-derived.</para>
    /// </summary>
    internal static ConvertedPanel? PanelFor(UIWindow? window)
    {
        if (window == null)
            return null;
        WindowPanel? wp = FindPanel(window);
        return wp != null && wp.Panel.IsAlive ? wp.Panel : null;
    }

    /// <summary>
    /// RELEASE EVERY FLOATED WINDOW EXCEPT ONE — presentation only, no game state, nothing on the
    /// wire. Returns how many were released and fills <paramref name="names"/> with their names for
    /// the caller's log line.
    ///
    /// <para><b>USER RULING (2026-08-23, the point-of-no-return report), verbatim:</b> <i>"Alle
    /// anderen Fenster sollen dabei dann geschlossen werden."</i></para>
    ///
    /// <para><b>IT IS THE EXISTING TEARDOWN, NOT A NEW ONE, AND NOT A HIDE.</b> The three statements
    /// below are the same three the per-tick release loop runs
    /// (<c>ModalFallback.4.Tick.cs</c>: leave <see cref="Converted"/>, destroy the grab holder,
    /// <c>CanvasConversion.Release</c>), and between them they take the panel, the grab bar and its
    /// collider, the X plate (a child of the host rect, destroyed with it) and the map room's arc
    /// slot — the slot because <c>ReleaseFinishedArcSlots</c> uses membership in
    /// <see cref="Converted"/> as its liveness test and runs later in the SAME tick. Hiding the
    /// panel instead would leave every one of those standing, which is the
    /// <c>leeres_fenster2.jpg</c> defect.</para>
    ///
    /// <para><b>WHY NOT <see cref="CloseFloatedWindow"/>, WHICH IS THE OTHER OBVIOUS CHOICE.</b> That
    /// path writes GAME state — <c>UIWindow.Escape()</c> with a forced <c>Hide()</c> — and one of the
    /// windows standing at the point of no return is the LOADOUT SCREEN, whose <c>Hide()</c> would
    /// abandon the scenario the party has just committed to. A presentation release cannot do that to
    /// any window, known or unknown, which is the property that makes it safe to point at a list.</para>
    ///
    /// <para><b>AND WHAT IT MEANS FOR A SHARED WINDOW AND FOR A PEER.</b> Releasing the quest-confirm
    /// float (kind 2) removes this client's floated copy, so <c>Net.RemoteMapStory.Sample</c> stops
    /// emitting that entry and every peer <c>Forget</c>s it — which is precisely the state record 21
    /// documents as defined ("no record from a peer ⇒ that peer has no shared map window ⇒ nothing is
    /// driven and the local placement stands"). No game state moves, no page is advanced, no pose is
    /// published or withdrawn on anybody else's table. A peer still in the map room keeps its own
    /// windows exactly where they were.</para>
    ///
    /// <para>The kept window is compared by REFERENCE and may be null, in which case everything
    /// goes.</para>
    ///
    /// <para>=================================================================================
    /// ModBuild 234 — THE "DO NOT REACH FOR THIS" NOTE, REWRITTEN, BECAUSE IT SAID THE WRONG THING
    /// =================================================================================</para>
    ///
    /// <para>ModBuild 232 left a note here that ended <i>"never resurrect the exclusion"</i>. That
    /// sentence is FALSE as written and it is worth exactly one paragraph to say what is actually
    /// forbidden, because ModBuild 234 was asked for an exclusion — <i>"alle anderen Fenster
    /// verschwinden und nur dieses Fenster [ist] sichtbar"</i> — and a rule that reads "never" would
    /// have sent it looking for a worse mechanism.</para>
    ///
    /// <list type="number">
    /// <item><b>FORBIDDEN: a STANDING exclusion.</b> That is what ModBuild 231 shipped — a
    /// level-triggered re-sweep that kept releasing anything that floated after the edge. An
    /// exclusion evaluated repeatedly cannot promise what it is pointing at: every window the
    /// pre-scenario loadout sequence opens (the story box, the battle-goal picker, the party
    /// display, the loadout screen itself) enters its scope the moment it opens, and 231 duly ate
    /// all four. The defect is the STANDING part, not the EXCEPT part.</item>
    ///
    /// <item><b>SANCTIONED: an exclusion evaluated ONCE, at an edge, and FROZEN into a set of
    /// window INSTANCES.</b> "Everything floated at this instant except X" is a finite list of
    /// objects the moment it is taken, and nothing that opens afterwards can join it. That is the
    /// same membership guarantee <c>StoryComposite.EdgeClosed</c> has, arrived at from the other
    /// side, and it is what <c>StoryComposite</c>'s ModBuild 234 story curtain uses.</item>
    ///
    /// <item><b>AND THIS PARTICULAR ROUTINE IS STILL NOT THE RIGHT LEVER, FOR A REASON THAT HAS
    /// NOTHING TO DO WITH EXCLUSION.</b> A release is undone by the very next tick: the game window
    /// is still open, the catch-all finds it in <c>UnknownShown</c>, <c>IsFloatedByUs</c> is now
    /// false, and it is re-enrolled AND re-counted by the churn fuse
    /// (<c>ModalFallback.10.CatchAll.cs</c>: <c>FloatChurn[name].Count++</c>, <c>ChurnMaxFloats</c>
    /// 3). Four release/re-float cycles session-suppress that window's NAME. So a release keeps a
    /// window away for exactly one tick and pays a fuse count for it. Whatever has to STAY away must
    /// go through <c>FloatRefusalTable</c> instead, which is asked at the TOP of that loop and
    /// <c>continue</c>s before the count — a refused window is never enrolled and never counted, and
    /// <c>WithdrawRefusedFloat</c> takes down a float that already exists.</item>
    /// </list>
    ///
    /// <para><b>SO: STILL NO CALLERS, AND THE HONEST REASON IS (3), NOT (1).</b> Keep it for the case
    /// it is genuinely right for — a wholesale teardown at a moment when nothing is expected to come
    /// back (room teardown, scene change). For "these windows must stay away for an interval", use a
    /// refusal; for "close this window for good", use <see cref="CloseFloatedWindow"/>.</para>
    /// </summary>
    internal static int ReleaseFloatsExcept(UIWindow? keep, out string names)
    {
        int closed = 0;
        var sb = new System.Text.StringBuilder(64);
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            if (wp.Window != null && keep != null && ReferenceEquals(wp.Window, keep))
                continue;
            string name = wp.Window != null ? wp.Window.name : "<destroyed>";
            Converted.RemoveAt(i);
            wp.Grab?.Destroy();
            CanvasConversion.Release(wp.Panel);
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(name).Append('\'');
            closed++;
        }
        names = sb.Length > 0 ? sb.ToString() : "none";
        return closed;
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
