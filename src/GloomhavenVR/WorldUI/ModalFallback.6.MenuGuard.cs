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
    /// EVERY SUBSEQUENT scripted window appears at EXACTLY the spot the PREVIOUS one was
    /// read at — including a spot the player grab-moved the window to. The player reads the
    /// whole hint chain at ONE stable, self-chosen location instead of each hint re-yanking
    /// to the gaze.
    ///
    /// ONE SHARED store for ALL scripted window kinds (hardware round 2026-08-02): the store
    /// was originally per GROUP (tutorial box / help-text strip — each a distinct UIWindow
    /// re-used across its messages), which made each group internally consistent but let the
    /// two kinds live in two DIFFERENT places — the "Zeige auf …" instruction strips kept
    /// appearing away from where the box messages were parked. The ruling is cross-kind
    /// ("every subsequent scripted window", not "of the same kind"), so the store is now a
    /// SINGLE pose shared by the whole chain, updated whenever ANY scripted window is placed,
    /// grab-moved or captured at an edge — the most recently touched window is "the previous
    /// window" the next one lands on, whatever its kind.
    ///
    /// UPDATE EDGES: window placement (rule 1 AND rule 2 — the just-placed window becomes
    /// the anchor immediately, so a strip opening right after the box's first spawn already
    /// lands on it), every tick WHILE gripped (a grab-move re-anchors live, so a window that
    /// spawns while its predecessor is still in-hand follows the hand's spot), message-key
    /// change (the reading spot was just implicitly approved), float release (the game
    /// sometimes CLOSES a group window briefly between two messages — the release is that
    /// gap's edge), and the recall/refloat re-placements. CONSUMED verbatim by the next
    /// convert (<see cref="TryConvertWindow"/> rule 2 — no re-clamp, no re-facing: the
    /// player approved that exact spot; only finiteness is sanity-checked). RESET on
    /// scenario end/teardown and on presence regain (a pose captured around a doff/don may
    /// sit behind the player — rule 1 wins there, the stale-pose fix).
    ///
    /// POSE = WINDOW CENTER (size-difference handling): every converted host gets pivot
    /// (0.5, 0.5) and centered content (CanvasConversion.Convert), so the stored host pose
    /// IS the window's visual center. Placing a differently-sized window verbatim at the
    /// shared pose therefore CENTER-aligns it with its predecessor — deliberate: the eyes
    /// are parked on the previous window's middle, and a taller box after a short strip
    /// grows symmetrically up/down instead of plunging toward the board the way top-edge
    /// anchoring would. No pivot conversion is needed as long as both hosts keep the
    /// centered pivot.
    /// </summary>
    private struct ChainPose
    {
        public bool Valid;
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>Which window kind last wrote the pose (<see cref="LevelMessageKindName"/>)
        /// — diagnostics only, surfaced in the rule-2 "re-floated at the stored chain pose"
        /// log line so a hardware log shows WHOSE spot the next window inherited.</summary>
        public string? SetBy;
    }

    /// <summary>The ONE chain pose shared by all scripted level-message window kinds.</summary>
    private static ChainPose _chainPose;

    /// <summary>
    /// Capture the LIVE host pose of a level-message float into the shared chain store
    /// (no-op for other windows / dead panels / non-finite poses). The live pose IS the
    /// grab-moved pose — a grab writes the host every tick — so reading it at the update
    /// edges (placement, grip, message change, release) persists a deliberate move without
    /// any extra grab bookkeeping.
    /// </summary>
    private static void StoreChainPose(WindowPanel wp) => StoreChainPose(wp.Window, wp.Panel);

    /// <summary>
    /// <see cref="StoreChainPose(WindowPanel)"/> on the two pieces it actually needs, so callers
    /// that hold no window record can re-seed the store too — today the one-shot pose re-place
    /// (<see cref="TickPoseRePlaceOne"/>): a rule-1 level-message spawn seeds the chain at convert
    /// time, and if the re-place then corrects that pose the store must follow, or the NEXT
    /// scripted window would inherit a spot this window no longer occupies.
    /// </summary>
    private static void StoreChainPose(UIWindow? window, ConvertedPanel panel)
    {
        int g = LevelMessageGroupIndex(window);
        if (g < 0 || panel == null || !panel.IsAlive || panel.HostGo == null)
            return;
        Transform host = panel.HostGo.transform;
        Vector3 pos = host.position;
        Quaternion rot = host.rotation;
        if (!IsFinitePose(pos, rot))
            return; // never poison the store — the next spawn then falls back to rule 1
        _chainPose.Valid = true;
        _chainPose.Position = pos;
        _chainPose.Rotation = rot;
        _chainPose.SetBy = LevelMessageKindName(g);
    }

    /// <summary>The shared chain pose, if a valid one exists (rule 2) — only ever consulted
    /// for level-message windows (<paramref name="window"/> is the defensive re-check).
    /// <paramref name="setBy"/> names the window kind that last wrote it (diagnostics). A
    /// non-finite stored pose is dropped and reported false — the minimal safety the ruling
    /// keeps: a genuinely lost/invalid pose falls back to rule 1.</summary>
    private static bool TryGetChainPose(UIWindow? window, out Vector3 pos, out Quaternion rot,
        out string setBy)
    {
        if (IsLevelMessageWindow(window) && _chainPose.Valid)
        {
            pos = _chainPose.Position;
            rot = _chainPose.Rotation;
            setBy = _chainPose.SetBy ?? "<unknown>";
            if (IsFinitePose(pos, rot))
                return true;
            _chainPose.Valid = false; // poisoned somehow → rule 1, never place at NaN
        }
        pos = default;
        rot = Quaternion.identity;
        setBy = "<none>";
        return false;
    }

    /// <summary>Drop the shared stored pose (scenario end / teardown / presence regain).
    /// Change-gated: silent no-op while nothing is stored, one log line otherwise.</summary>
    private static void ResetChainPose(string reason)
    {
        if (!_chainPose.Valid)
            return;
        _chainPose = default;
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

    // ---- user-owned re-open pose memory (user ruling 2026-08-04) ------------------------

    /// <summary>
    /// RE-OPEN POSE MEMORY: the last PLAYER-CHOSEN pose (+ two-hand resize factor) of each
    /// floated window the player had grabbed, keyed by <see cref="UIWindowID"/>. User ruling
    /// 2026-08-04 ("Sie sollen dort fix bleiben, wo sie stehen"): once the player has moved a
    /// window, its pose is theirs - and that extends across a close/re-open within the same
    /// scenario: re-opening the pause menu / Options must bring it back at the spot (and
    /// size) the player parked it at, not re-run the gaze spawn. Written at float release
    /// (<c>Tick</c> step 1) for every non-level-message window whose grab carries
    /// <see cref="GrabbableModal.UserMoved"/>; consumed verbatim by
    /// <see cref="TryConvertWindow"/> (no re-clamp, no re-facing - the same authority rule as
    /// the level-message chain pose, which keeps its OWN store and stays excluded here).
    /// Reset on scenario end and module teardown - a pose is anchored to one scenario's
    /// table. Deliberately NOT reset on presence regain: yanking user-parked windows on every
    /// brief doff/don is exactly the jumping the ruling forbids, and the stored pose stays
    /// findable via the recall-free escape hatches (X, escape chord).
    /// </summary>
    private struct UserPose
    {
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>The player's two-hand resize factor at release (frame local scale).</summary>
        public float ScaleFactor;
    }

    /// <summary>Player-chosen poses per window ID (see <see cref="UserPose"/>).</summary>
    private static readonly Dictionary<UIWindowID, UserPose> _userPoses = new();

    /// <summary>
    /// Capture a closing float's live pose into the re-open memory - only when the player had
    /// actually grabbed it (<see cref="GrabbableModal.UserMoved"/>); an untouched window keeps
    /// spawning fresh at the gaze exactly as today. Level-message windows are excluded: their
    /// continuity is owned by the shared chain store (<see cref="StoreChainPose(WindowPanel)"/>).
    /// </summary>
    private static void StoreUserPose(WindowPanel wp)
    {
        if (wp.Grab == null || !wp.Grab.UserMoved || wp.Window == null
            || IsLevelMessageWindow(wp.Window)
            || wp.Panel == null || !wp.Panel.IsAlive || wp.Panel.HostGo == null)
            return;
        Transform host = wp.Panel.HostGo.transform;
        Vector3 pos = host.position;
        Quaternion rot = host.rotation;
        if (!IsFinitePose(pos, rot))
            return; // never poison the memory - the next open falls back to the gaze spawn
        _userPoses[wp.Window.ID] = new UserPose
        {
            Position = pos,
            Rotation = rot,
            ScaleFactor = wp.Grab.UserScaleFactor,
        };
        VRLog.Info("WorldUI", $"MODAL WINDOW: '{wp.Window.name}' (ID {wp.Window.ID}) releases at a " +
                              $"PLAYER-CHOSEN pose ({pos.x:F2},{pos.y:F2},{pos.z:F2}), size factor " +
                              $"{wp.Grab.UserScaleFactor:F2} - remembered; a re-open this scenario " +
                              "restores it there instead of re-running the gaze spawn.");
    }

    /// <summary>The remembered player pose for a window ID, if one exists (finite-checked -
    /// a poisoned entry is dropped and the caller falls back to the fresh gaze spawn).</summary>
    private static bool TryGetUserPose(UIWindowID id, out Vector3 pos, out Quaternion rot,
        out float scaleFactor)
    {
        if (_userPoses.TryGetValue(id, out UserPose stored))
        {
            if (IsFinitePose(stored.Position, stored.Rotation))
            {
                pos = stored.Position;
                rot = stored.Rotation;
                scaleFactor = stored.ScaleFactor;
                return true;
            }
            _userPoses.Remove(id);
        }
        pos = default;
        rot = Quaternion.identity;
        scaleFactor = 1f;
        return false;
    }

    /// <summary>Drop every remembered player pose (scenario end / module teardown).
    /// Change-gated: silent no-op while nothing is stored, one log line otherwise.</summary>
    private static void ResetUserPoses(string reason)
    {
        if (_userPoses.Count == 0)
            return;
        _userPoses.Clear();
        VRLog.Info("WorldUI", $"MODAL WINDOW: remembered player window pose(s) dropped ({reason}) - " +
                              "the next open of each window spawns fresh at the gaze again.");
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
    /// pose into the shared chain store (<see cref="_chainPose"/>) instead of yanking a
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
                // CHAIN CONTINUITY: a gripped level message re-anchors the SHARED chain pose
                // LIVE (the host follows the grab frame every tick, so this reads the hand's
                // current spot). Without it, a scripted window that spawns while its
                // predecessor is still in-hand — or whose predecessor's message key changes
                // mid-carry (this branch skips the key-change capture below) — would land on
                // the stale pre-grab spot instead of where the player is holding the chain.
                if (isLevelMsg)
                    StoreChainPose(wp);
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
            // change is instead a CAPTURE EDGE for the shared chain store: the pose the
            // previous hint was read at (grab-moves included — this reads the live host pose)
            // becomes the spot the NEXT scripted window of ANY kind lands on (TryConvertWindow rule
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

            // USER-OWNED POSE (user ruling 2026-08-04, THE reported jump): once the player has
            // grabbed this window, its pose is theirs - deliberately parking a window OUT of
            // the view is a wanted state ("manchmal schiebe ich sie absichtlich zur Seite"),
            // and this very timer was what yanked it back every 6 s (hardware log: repeated
            // "MODAL RECALL: 'UI Options Window_unified' was open but out of view for 6s"
            // lines). Skip the recall for user-moved windows FOREVER, whatever the timer
            // reads; the chain-pose capture above still ran, so level-message continuity
            // keeps following the player's spot. Untouched windows (never grabbed) keep the
            // full recall as the lost-blocking-window deadlock insurance; a user-moved window
            // the player genuinely loses still has the X and the modal escape chord.
            if (wp.Grab != null && wp.Grab.UserMoved)
            {
                wp.OutOfViewSince = 0f;
                continue;
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
        ESCMenu? esc = FloatedEscMenu();
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

    // ---- OPEN-ONLY pause-menu tabs (user ruling 2026-08-04) -----------------------------

    /// <summary>
    /// OPEN-ONLY TABS: should this uGUI click be swallowed because it re-clicks a pause-menu
    /// tab whose window is ALREADY open? Called by <c>UguiPointer.Release</c> - the single
    /// choke point BOTH mod click paths (fingertip poke and far laser) deliver their
    /// pointerClick through - immediately before the click would be executed, so a swallowed
    /// click never reaches the game at all (no toggle flip, no Select/Deselect).
    ///
    /// <para>ROOT CAUSE (user report 2026-08-04: "Wenn man im Pausenmenue wiederholt denselben
    /// Tab drueckt, z.B. 'Optionen', dann cleared das irgendwann das andere Fenster"). The ESC
    /// menu's tabs are uGUI Toggles in a single-select ToggleGroup (decompiled ESCMenu.cs:25,
    /// UIMenuOptionToggle.cs:10/52-71): every click flips <c>toggle.isOn</c>, and the flip runs
    /// Select/Deselect (UIMenuOptionToggle.OnToggleValueChanged -> UIMenuOption.Select/
    /// Deselect). For the Options tab those are wired to the WINDOW's lifecycle
    /// (ESCMenu.cs:134-146): select -> <c>UIOptionsWindow.Show(...)</c>, deselect ->
    /// <c>UIOptionsWindow.Hide()</c>. The mod floats these windows STICKY (item 6): the game's
    /// Hide is visually defeated, so the float stays up while the game's window state moves on
    /// - and the NEXT click's re-Show runs <c>UIOptionsWindow.OnShow</c>, whose first act is
    /// <c>CloseWindows()</c> (decompiled UIOptionsWindow.cs:199-201): every open settings tab
    /// inside the still-floating window is closed and the content re-initialized. That is
    /// exactly the reported "cleared das andere Fenster und dort ist nicht mehr das Richtige
    /// zu sehen - schliesst das Fenster aber nicht".</para>
    ///
    /// <para>THE RULE (user): tabs are for OPENING only - ALL of them. When the window a tab
    /// targets is already open (game-side open OR alive as a converted float - the sticky
    /// model makes these diverge, and EITHER means the player already sees it), the click is
    /// swallowed here at the mod's click-delivery seam instead of patching the game's menu:
    /// the seam CAN resolve the target window (the tab -> window mapping below is the same
    /// verified one <see cref="SyncEscMenuTabHighlights"/> drives the highlights with), the
    /// game's code stays untouched, and the guard is scoped to clicks the mod itself
    /// synthesizes - the flat-screen/2D path keeps the game's native toggle semantics, where
    /// game state and visuals cannot diverge. When no window floats the resolver bails in one
    /// comparison, so the cost on every ordinary click is nil.</para>
    /// </summary>
    internal static bool ShouldSwallowMenuTabClick(GameObject? clickHandler)
    {
        if (clickHandler == null || Converted.Count == 0)
            return false;
        ESCMenu? esc = FloatedEscMenu();
        if (esc == null)
            return false; // the pause menu is not floating - its tabs cannot be clicked here

        UIWindowID? target = ResolveEscTabTarget(esc, clickHandler);
        if (target == null)
            return false; // not a pause-menu tab (or the Continue tab, which opens no window)

        UIWindowID id = target.Value;
        bool floated = IsIdFloated(id);
        bool gameOpen = IsIdOpenGameSide(id);
        if (!floated && !gameOpen)
            return false; // genuine OPEN click - pass through untouched

        VRLog.Info("WorldUI", $"MODAL MENU: pause-menu tab click on '{clickHandler.name}' SWALLOWED - " +
                              $"its window (ID {id}) is already open " +
                              $"(floated={floated}, gameOpen={gameOpen}). Tabs are OPEN-ONLY: a re-click " +
                              "would flip the tab toggle and re-run the game's Show/Hide, which resets " +
                              "the floating window's content (OnShow -> CloseWindows).");
        return true;
    }

    /// <summary>The ESCMenu component of the currently floated pause menu, or null when the
    /// pause menu is not floating (shared by the tab highlight sync and the open-only guard).</summary>
    private static ESCMenu? FloatedEscMenu()
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Window != null && wp.Window.ID == UIWindowID.ESCMenu && wp.Panel.IsAlive)
                return wp.Window.GetComponent<ESCMenu>();
        }
        return null;
    }

    /// <summary>
    /// Which window does this clicked object's pause-menu tab open? Null when the click is not
    /// on one of the ESC menu's window-opening tabs. Resolution is structural, two layers:
    /// the click handler for a tab is its ExtendedToggle's own GameObject (Toggle implements
    /// IPointerClickHandler), so first walk up for the owning <c>UIMainMenuOption</c>
    /// (toggle on the same GameObject or a child of the option - the common prefab layouts),
    /// then fall back to matching the tabs' serialized <c>toggle</c> references directly
    /// (covers a toggle serialized from elsewhere in the prefab). The resulting option is
    /// REFERENCE-compared against the ESC menu's own tab fields, so a UIMainMenuOption inside
    /// some other window (the Options window reuses the type for its settings tabs) can never
    /// match. Tab -> window ID mapping identical to <see cref="SyncEscMenuTabHighlights"/>
    /// (runtime-log verified there); mainMenu/exit map to the MainMenuConfirmationBox their
    /// click spawns (ESCMenu.LoadMainMenu / ExitGame -> UIConfirmationBoxManager
    /// .MainMenuInstance, confirmed as 'Confirmation Box_MainMenu_pc' in the runtime log);
    /// the Continue tab opens no window and stays a normal click.
    /// </summary>
    private static UIWindowID? ResolveEscTabTarget(ESCMenu esc, GameObject clickHandler)
    {
        UIMainMenuOption? opt = clickHandler.GetComponentInParent<UIMainMenuOption>();
        if (opt == null)
            opt = MatchTabByToggle(esc, clickHandler);
        if (opt == null)
            return null;
        if (ReferenceEquals(opt, esc.optionsButton))
            return UIWindowID.Options;
        if (ReferenceEquals(opt, esc.multiplayerButton))
            return UIWindowID.ViceOptionsSubmenu;
        if (ReferenceEquals(opt, esc.compendiumButton))
            return UIWindowID.CompendiumPanel;
        if (ReferenceEquals(opt, esc.mainMenuButton) || ReferenceEquals(opt, esc.exitButton))
            return UIWindowID.MainMenuConfirmationBox;
        return null; // continueButton / a UIMainMenuOption that is not an ESC tab
    }

    /// <summary>Fallback tab resolution: is <paramref name="clickHandler"/> one of the ESC
    /// tabs' own serialized Toggle GameObjects? (See <see cref="ResolveEscTabTarget"/>.)</summary>
    private static UIMainMenuOption? MatchTabByToggle(ESCMenu esc, GameObject clickHandler)
    {
        if (IsTabToggle(esc.optionsButton, clickHandler))
            return esc.optionsButton;
        if (IsTabToggle(esc.multiplayerButton, clickHandler))
            return esc.multiplayerButton;
        if (IsTabToggle(esc.compendiumButton, clickHandler))
            return esc.compendiumButton;
        if (IsTabToggle(esc.mainMenuButton, clickHandler))
            return esc.mainMenuButton;
        if (IsTabToggle(esc.exitButton, clickHandler))
            return esc.exitButton;
        return null;
    }

    /// <summary>Does this tab's serialized toggle live on the clicked GameObject?</summary>
    private static bool IsTabToggle(UIMainMenuOption? tab, GameObject clickHandler) =>
        tab != null && tab.toggle != null && ReferenceEquals(tab.toggle.gameObject, clickHandler);

    /// <summary>True while a tracked fallback window of this ID is open GAME-SIDE (the game's
    /// own <c>UIWindow.IsOpen</c>) - the sticky float model lets game state and float diverge,
    /// and the open-only tab rule must hold for either.</summary>
    private static bool IsIdOpenGameSide(UIWindowID id)
    {
        foreach (UIWindow window in Open)
        {
            if (window != null && window.ID == id && window.IsOpen)
                return true;
        }
        return false;
    }

}
