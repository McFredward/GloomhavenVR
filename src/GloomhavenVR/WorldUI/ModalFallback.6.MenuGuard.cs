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
    // ---- level-message chain pose continuity (user ruling 2026-08-02) -------------------

    /// <summary>
    /// CHAIN POSE CONTINUITY (user ruling 2026-08-02, partially superseding the short-lived
    /// re-show recall below): only the FIRST window of a tutorial/scripted-message chain
    /// spawns in front of the player (rule 1 — the 0.95 m, view-cone-guaranteed placement);
    /// EVERY SUBSEQUENT message appears at EXACTLY the spot the PREVIOUS one was read at —
    /// including a spot the player grab-moved the window to. The player reads the whole hint
    /// chain at ONE stable, self-chosen location instead of each hint re-yanking to the gaze.
    ///
    /// The chain shares one float per GROUP (tutorial box / help-text strip — the same
    /// UIWindow re-used across messages), but the game sometimes CLOSES the group window
    /// briefly between two messages, which drops the float. This per-group store carries
    /// the last live pose across those gaps: CAPTURED at message-key change (the reading
    /// spot was just implicitly approved) and at float release (the gap's edge), CONSUMED
    /// verbatim by the next convert (<see cref="TryConvertWindow"/> rule 2 — no re-clamp,
    /// no re-facing: the player approved that spot by leaving the window there; only
    /// finiteness is sanity-checked). RESET on scenario end/teardown and on presence regain
    /// (a pose captured around a doff/don may sit behind the player — rule 1 wins there,
    /// the stale-pose fix).
    /// </summary>
    private struct ChainPose
    {
        public bool Valid;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>Per-group stored chain pose — indexed by <see cref="LevelMessageGroupIndex"/>
    /// (0 = tutorial box group, 1 = help-text strip group). The two groups may be parked at
    /// different spots, so their poses persist independently.</summary>
    private static readonly ChainPose[] ChainPoses = new ChainPose[2];

    /// <summary>
    /// Capture the LIVE host pose of a level-message float into its group's chain store
    /// (no-op for other windows / dead panels / non-finite poses). The live pose IS the
    /// grab-moved pose — a grab writes the host every tick — so reading it at the capture
    /// edges (message change, release) automatically persists a deliberate move without any
    /// extra grab bookkeeping.
    /// </summary>
    private static void StoreChainPose(WindowPanel wp)
    {
        int g = LevelMessageGroupIndex(wp.Window);
        if (g < 0 || !wp.Panel.IsAlive || wp.Panel.HostGo == null)
            return;
        Transform host = wp.Panel.HostGo.transform;
        Vector3 pos = host.position;
        Quaternion rot = host.rotation;
        if (!IsFinitePose(pos, rot))
            return; // never poison the store — the next spawn then falls back to rule 1
        ChainPoses[g].Valid = true;
        ChainPoses[g].Position = pos;
        ChainPoses[g].Rotation = rot;
    }

    /// <summary>The stored chain pose for this level-message window's group, if a valid one
    /// exists (rule 2). A non-finite stored pose is dropped and reported false — the minimal
    /// safety the ruling keeps: a genuinely lost/invalid pose falls back to rule 1.</summary>
    private static bool TryGetChainPose(UIWindow? window, out Vector3 pos, out Quaternion rot)
    {
        int g = LevelMessageGroupIndex(window);
        if (g >= 0 && ChainPoses[g].Valid)
        {
            pos = ChainPoses[g].Position;
            rot = ChainPoses[g].Rotation;
            if (IsFinitePose(pos, rot))
                return true;
            ChainPoses[g].Valid = false; // poisoned somehow → rule 1, never place at NaN
        }
        pos = default;
        rot = Quaternion.identity;
        return false;
    }

    /// <summary>Drop both groups' stored poses (scenario end / teardown / presence regain).
    /// Change-gated: silent no-op while nothing is stored, one log line otherwise.</summary>
    private static void ResetChainPoses(string reason)
    {
        if (!ChainPoses[0].Valid && !ChainPoses[1].Valid)
            return;
        ChainPoses[0] = default;
        ChainPoses[1] = default;
        VRLog.Info("WorldUI", $"LEVEL-MESSAGE CHAIN: stored window pose(s) dropped ({reason}) — the next " +
                              "scripted message spawns in front of the player again (rule 1).");
    }

    /// <summary>Finiteness sanity for a stored/consumed pose: every component a real number
    /// and the rotation non-degenerate (a zeroed quaternion cannot orient a window). net472 —
    /// no float.IsFinite, hence the explicit NaN/Infinity pairs.</summary>
    private static bool IsFinitePose(Vector3 pos, Quaternion rot)
    {
        return !(float.IsNaN(pos.x) || float.IsInfinity(pos.x)
                 || float.IsNaN(pos.y) || float.IsInfinity(pos.y)
                 || float.IsNaN(pos.z) || float.IsInfinity(pos.z)
                 || float.IsNaN(rot.x) || float.IsInfinity(rot.x)
                 || float.IsNaN(rot.y) || float.IsInfinity(rot.y)
                 || float.IsNaN(rot.z) || float.IsInfinity(rot.z)
                 || float.IsNaN(rot.w) || float.IsInfinity(rot.w))
               && rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w > 0.5f;
    }

    // ---- lost-menu recall (incident fix) ------------------------------------------------

    /// <summary>
    /// LOST-MENU RECALL: for each floated STICKY full-screen-menu panel (ESC/Options family)
    /// — and, since the Sieg/Niederlage rework, each end-of-scenario results window
    /// (<see cref="IsResultsPanel"/>: grabbable, no X, blocking → must never be lost) —
    /// whose game window is OPEN, track how long its host has been continuously outside the
    /// head camera's view frustum (center test with a generous margin) OR farther than
    /// <see cref="RecallDistanceMeters"/> (real scale) from the head. After
    /// <see cref="RecallOutOfViewSeconds"/> the panel is RECALLED: re-placed with the exact
    /// placement used at float time (in front of the HMD at reading distance, upright,
    /// facing the user) — grabbable panels via <see cref="GrabbableModal.PlaceFrameAt"/>
    /// (placing the host directly would be snapped back by the next follow tick, the
    /// RefloatOpenWindows lesson), others via <see cref="PlaceAtHmd"/>. This guarantees the
    /// user always SEES the menu that is blocking card/board input. The timer resets
    /// whenever the panel is visible, while a hand grips it (the user is deliberately
    /// carrying it — never yank it out of their grip), and on recall. Unscaled time — the
    /// pause menu may freeze timeScale.
    ///
    /// <para>LEVEL-MESSAGE participation (torbogen report 2026-08-02, reconciled with the
    /// chain-pose ruling): the two level-message group windows (tutorial box / action strip)
    /// participate in the slow TIMER recall only — the deadlock safety net for a genuinely
    /// lost blocking box. The IMMEDIATE "re-place when a new message re-shows out of view"
    /// path that briefly shipped here is SUPERSEDED by position continuity (user ruling):
    /// a message-key change (<see cref="CurrentLevelMessageKey"/>) now CAPTURES the live
    /// pose into the per-group chain store (<see cref="ChainPoses"/>) instead of yanking a
    /// deliberately parked window back to the gaze.</para>
    /// </summary>
    private static void TickMenuRecall()
    {
        if (Converted.Count == 0)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        float now = Time.unscaledTime;
        float scale = Mathf.Max(PanelLayout.WorldScale, 0.01f);
        Vector3 headPos = head.transform.position;

        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            // Sticky full-screen menus (ESC/Options family) with their game window OPEN
            // participate — those gate input while open. So do the now-grabbable
            // Sieg/Niederlage results windows (user request A): they BLOCK the scenario-end
            // flow, carry no X, and can only be advanced through their native buttons, so a
            // results window the user carried away and lost would be an invisible hard lock —
            // the recall guarantees it always comes back into view. And so do the LEVEL-MESSAGE
            // group windows (torbogen report): the tutorial chains scripted messages through
            // ONE kept-alive float, so a panel out of view is an unreadable (often blocking)
            // tutorial step. Closed/sticky-hidden floats, other non-sticky modals and panels
            // on their way out are left alone.
            bool isLevelMsg = IsLevelMessageWindow(wp.Window);
            bool recallable = wp.Window != null
                              && ((wp.Sticky && wp.FullScreenMenu) || IsResultsPanel(wp.Window.ID)
                                  || isLevelMsg);
            // Level-message ground truth: IsOpen can momentarily read false during the game's
            // hide→show handover (deadlock #2) while the window is genuinely displayed — accept
            // IsVisible too, mirroring LevelMessageWindowOpen.
            bool windowLive = wp.Window != null
                              && (wp.Window.IsOpen || (isLevelMsg && wp.Window.IsVisible));
            if (!recallable || wp.UserClosing || !windowLive
                || !wp.Panel.IsAlive || wp.Panel.HostGo == null)
            {
                wp.OutOfViewSince = 0f;
                continue;
            }
            // A gripped panel is being deliberately placed — never recall mid-carry.
            if (wp.Grab != null && wp.Grab.IsGrabbed)
            {
                wp.OutOfViewSince = 0f;
                continue;
            }

            Vector3 pos = wp.Panel.HostGo.transform.position;
            bool visible = IsInHeadView(head, pos)
                           && Vector3.Distance(headPos, pos) <= RecallDistanceMeters * scale;

            // LEVEL-MESSAGE CHAIN CONTINUITY (user ruling 2026-08-02): a message-key change
            // means the NEXT hint of the chain just re-showed inside the kept-alive float at
            // its previous pose — exactly what the ruling wants, so nothing is re-placed here
            // (the re-show recall that briefly lived at this spot is superseded: continuity
            // WINS, even when the player parked the window out of the current view). The key
            // change is instead the CAPTURE EDGE for the per-group chain store: the pose the
            // previous hint was read at (grab-moves included — this reads the live host pose)
            // becomes the spot a gap-reopened group window returns to (TryConvertWindow rule
            // 2). Runs before the visibility bookkeeping so a parked-out-of-view chain still
            // captures every approved pose.
            if (isLevelMsg)
            {
                string? key = CurrentLevelMessageKey(wp.Window);
                if (key != null && !string.Equals(key, wp.LastLevelMessageKey, StringComparison.Ordinal))
                {
                    wp.LastLevelMessageKey = key;
                    StoreChainPose(wp);
                }
            }

            if (visible)
            {
                wp.OutOfViewSince = 0f;
                continue;
            }
            if (wp.OutOfViewSince <= 0f)
            {
                wp.OutOfViewSince = now;
                continue;
            }
            float outFor = now - wp.OutOfViewSince;
            if (outFor < RecallOutOfViewSeconds)
                continue;

            // RECALL — the same placement the window floated with (user request A: with the
            // panel's size passed along, the recall pose also avoids the control board /
            // other open modals; the panel itself is excluded from the obstacle set).
            if (wp.Grab != null)
            {
                Vector2 half = PanelWorldHalfSize(wp.Panel, PanelLayout.WorldScale * wp.ExtraScale);
                if (!ComputeHmdPose(out Vector3 p, out Quaternion r, out _, 0, half, wp.Panel,
                        isLevelMsg))
                    continue; // no head pose this tick — retry next tick, timer keeps running
                wp.Grab.PlaceFrameAt(p, r);
            }
            else
            {
                PlaceAtHmd(wp.Panel, wp.ExtraScale, 0, isLevelMsg);
            }
            // Chain continuity: a recalled level message has a NEW player-visible pose — make
            // it the chain's stored pose too, or a close/reopen gap right after the recall
            // would jump the next hint back to the very lost spot the timer just rescued the
            // window from.
            if (isLevelMsg)
                StoreChainPose(wp);
            float wasOutFor = now - wp.OutOfViewSince; // > 0 by construction (timer path only)
            wp.OutOfViewSince = 0f;
            VRLog.Info("WorldUI", $"MODAL RECALL: '{wp.Window!.name}' was open but out of view for " +
                                  $"{wasOutFor:F0}s — recalled in front of the HMD (it blocks card/board " +
                                  "input while open).");
        }
    }

    /// <summary>Panel center inside the head frustum (with <see cref="RecallViewMargin"/> slack,
    /// so a half-on-screen menu at the view edge still counts as visible).</summary>
    private static bool IsInHeadView(Camera head, Vector3 worldPos)
    {
        Vector3 vp = head.WorldToViewportPoint(worldPos);
        return vp.z > 0f
               && vp.x >= -RecallViewMargin && vp.x <= 1f + RecallViewMargin
               && vp.y >= -RecallViewMargin && vp.y <= 1f + RecallViewMargin;
    }

    // ---- full-screen-menu selection guard (P6 flicker fix) ------------------------------

    /// <summary>
    /// SECONDARY measure (NOT the flicker root cause — that is render ordering, fixed by
    /// <see cref="ModalHostSortingOrder"/>). This suppresses a distinct, narrower artefact:
    /// the gamepad-nav SELECTION highlight churning on a floated FULL-SCREEN menu (ESC /
    /// Options family). Root cause (decompiled, verified): in a scenario the game
    /// runs its gamepad UI navigation — <c>ControllerInputArea.Focus</c> selects a button
    /// via <c>EventSystem.SetSelectedGameObject</c> (ControllerInputArea.cs:240) and shows
    /// the "selected" highlight. The mod's pointer keeps the InControl input module's
    /// <c>Mouse.current</c> alive, and that module DE-selects on hover change every frame:
    /// <c>InControlInputModule.ProcessMove</c> does
    /// <c>if (focusOnMouseHover &amp;&amp; pointerEnter changed) SetSelectedGameObject(hoverHandler)</c>
    /// (InControlInputModule.cs:350-354; hoverHandler is null over the floated menu's empty
    /// area). The head-relative screen pointer sweeps the WORLD-fixed menu as the head
    /// moves (and Virtual Desktop injects host-mouse motion + the Mouse.current flip-war),
    /// so the hover target changes every frame → the selection churns null↔button → the
    /// button highlight + its LeanTween fade FLICKER. Fix: while such a menu floats,
    /// disable <c>focusOnMouseHover</c> on the live input module so the pointer can no
    /// longer churn the gamepad-nav selection. Poke and laser clicks are unaffected — they
    /// drive the real widgets through <c>ExecuteEvents</c> (RayUguiDriver / UguiPokeSurfaces),
    /// not the hover-select path. The previous value is saved and restored the moment the
    /// last full-screen menu closes (and on <see cref="Detach"/>).
    /// </summary>
    private static void ApplyMenuSelectionGuard()
    {
        bool wantSuppress = false;
        for (int i = 0; i < Converted.Count; i++)
        {
            if (Converted[i].FullScreenMenu && Converted[i].Panel.IsAlive)
            {
                wantSuppress = true;
                break;
            }
        }

        InControlInputModuleExtended? module = InControlInputModuleExtended.Instance;
        if (module == null)
        {
            // No live input module (early boot / torn down) — nothing to guard; drop the
            // suppression flag so we re-save cleanly when it returns.
            _hoverFocusSuppressed = false;
            return;
        }

        if (wantSuppress)
        {
            if (!_hoverFocusSuppressed)
            {
                _savedHoverFocus = module.focusOnMouseHover;
                module.focusOnMouseHover = false;
                _hoverFocusSuppressed = true;
                VRLog.Info("WorldUI", "MODAL MENU: full-screen menu floated — InControl mouse-hover focus " +
                                      $"disabled (was {_savedHoverFocus}) so the gamepad-nav selection highlight " +
                                      "stops flickering; poke/laser clicks (ExecuteEvents) are unaffected.");
            }
            else if (module.focusOnMouseHover)
            {
                // Belt-and-suspenders: if the game re-enabled it mid-float, pin it back off.
                module.focusOnMouseHover = false;
            }
        }
        else if (_hoverFocusSuppressed)
        {
            module.focusOnMouseHover = _savedHoverFocus;
            _hoverFocusSuppressed = false;
            VRLog.Info("WorldUI", $"MODAL MENU: full-screen menu closed — restored InControl mouse-hover focus " +
                                  $"({_savedHoverFocus}).");
        }
    }

    /// <summary>Restore the InControl mouse-hover focus if we suppressed it (module teardown).</summary>
    private static void RestoreMenuSelectionGuard()
    {
        if (!_hoverFocusSuppressed)
            return;
        InControlInputModuleExtended? module = InControlInputModuleExtended.Instance;
        if (module != null)
            module.focusOnMouseHover = _savedHoverFocus;
        _hoverFocusSuppressed = false;
    }

    // ---- multi-highlight of parallel sub-windows (issue #9) -----------------------------

    /// <summary>
    /// Issue #9: highlight the ESC-menu tab of EVERY sub-window that floats in parallel, not just
    /// the last-opened one. The game's ESC menu drives a SINGLE-SELECT <c>ToggleGroup</c>
    /// (ESCMenu.toggleGroup), so opening a second sub-window turns the first tab's toggle OFF (its
    /// deselect handler is the very <c>Hide()</c> the sticky model defeats — issue #5) and only the
    /// newest tab stays highlighted. This paints the selected-tab highlight for each tab whose
    /// window the mod currently floats.
    ///
    /// WHY NOT the toggle group: <c>ToggleGroup.allowSwitchOff</c> only permits ZERO-on, never
    /// multiple-on, and driving <c>toggle.isOn</c>/<c>SetValue</c> true runs the Toggle setter →
    /// <c>ToggleGroup.NotifyToggleOn</c> → turns the REAL active toggle off → hides that window (the
    /// issue #5 cause again). So the toggle/group state is left ENTIRELY to the game; only the
    /// highlight IMAGE is driven, re-asserted each tick, and handed straight back on close. Purely
    /// cosmetic, contained to the ESC menu, no game window state touched.
    ///
    /// Tab → window ID (verified: the multiplayer submenu is 'UI Multiplayer Submenu' = ID
    /// ViceOptionsSubmenu in the runtime log; options = UIOptionsWindow (Options); compendium =
    /// CompendiumWindow (CompendiumPanel)).
    /// </summary>
    private static void SyncEscMenuTabHighlights()
    {
        ESCMenu? esc = null;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Window != null && wp.Window.ID == UIWindowID.ESCMenu && wp.Panel.IsAlive)
            {
                esc = wp.Window.GetComponent<ESCMenu>();
                break;
            }
        }
        if (esc == null)
        {
            // ESC menu not floated (closed / never opened) — the game owns every tab highlight again.
            _forcedTabs.Clear();
            return;
        }
        SyncTab(esc.optionsButton, UIWindowID.Options);
        SyncTab(esc.multiplayerButton, UIWindowID.ViceOptionsSubmenu);
        SyncTab(esc.compendiumButton, UIWindowID.CompendiumPanel);
    }

    /// <summary>
    /// Drive one ESC-menu tab's highlight to match whether its window floats (issue #9). Forces the
    /// highlight image lit (mirroring <c>UIMenuOption.RefreshHighlight(true)</c>) while floated —
    /// re-asserting each tick so the game's LeanTween unhighlight fade cannot win — and fades it out
    /// once the window closes, WITHOUT ever touching the tab's toggle/selected state.
    /// </summary>
    private static void SyncTab(UIMainMenuOption? tab, UIWindowID id)
    {
        if (tab == null || tab.highlightImage == null)
            return;

        if (IsIdFloated(id))
        {
            // Keep it solidly lit: cancel any in-flight unhighlight fade (cheap no-op when none),
            // then paint the highlight colour. Change-gated colour write.
            tab.CancelHighlightAnimations();
            if (tab.highlightImage.color != tab.highlightColor)
                tab.highlightImage.color = tab.highlightColor;
            if (_forcedTabs.Add(id))
                VRLog.Info("WorldUI", $"MODAL MENU: ESC-menu tab (ID {id}) highlighted for a parallel-open " +
                                      "window — every open sub-window's tab now marked, not just the last (#9).");
        }
        else if (_forcedTabs.Remove(id))
        {
            // Window closed → hand the highlight back to the game. Only fade out if the game itself
            // does not consider the tab selected (its normal post-Deselect state), so a genuinely
            // active tab is never dimmed.
            if (!tab.IsSelected && tab.highlightImage.color.a > 0f)
            {
                Color c = tab.highlightImage.color;
                c.a = 0f;
                tab.highlightImage.color = c;
            }
            VRLog.Info("WorldUI", $"MODAL MENU: ESC-menu tab (ID {id}) un-highlighted — its window closed.");
        }
    }

    /// <summary>True while a window of this ID currently floats in the parallel modal set (issue #9).</summary>
    private static bool IsIdFloated(UIWindowID id)
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Window != null && wp.Window.ID == id && !wp.UserClosing && wp.Panel.IsAlive)
                return true;
        }
        return false;
    }

}
