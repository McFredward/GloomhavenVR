using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

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
        // Part 12: and hand back every canvas we re-bound onto the flat screen's UI camera — a
        // game canvas the mod moved must never outlive the module that moved it.
        ReleaseAllScreenBind("module shutdown");
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
        // ModBuild 231: the fuse-lift latch, its burst counter and its give-up set are one session's,
        // like every other catch-all latch CatchAllReset drops one line above.
        ChurnLiftLogged.Clear();
        ChurnLifts.Clear();
        ChurnLiftGivenUp.Clear();
        MapRoom.GuildmasterDestinations.Reset(); // hand the borrowed banner back before we vanish
        MapRoom.HoverCardPose.Reset();          // per-card follow/seat state dies with the module
        // Every window has just been released, so every reserved angle is free by definition.
        // Clearing the registry here (rather than letting the sweep do it) means a fresh session
        // never inherits a claim held by a panel from the previous one.
        for (int i = 0; i < _arcClaims.Length; i++)
            _arcClaims[i] = default;
        _arcGeometryLogged = false;         // re-state the room geometry once per session
        _usableHalfConeDeg = float.NaN;     // and re-measure the headset's cone from scratch
        _coneSource = "not measured yet";
        _coneFallbackWarned = false;
        ScreenWanted = false;
        VRModeStateMachine.SetAuxModal(false);
        // The sub-step accumulators describe ONE session's map room; a fresh session must not
        // inherit the previous one's frames (see the SUB-STEP ATTRIBUTION block).
        _tickPhase = -1;
        _nextTickBreakdown = 0f;
        ResetTickBreakdown();
    }

    private static void OnWindow(WindowVisibilityEvent e)
    {
        // Test #10: EVERY window transition is logged at Debug — a lock caused by a
        // window this class does not know is then attributable from the log alone.
        VRLog.Debug("WorldUI", $"UIWindow {(e.Shown ? "SHOWN" : "hidden")}: '{e.Window?.name}' " +
                               $"(ID {e.Id}, room={VRModeStateMachine.TableInFrontOfPlayer}, " +
                               $"scenario={VRModeStateMachine.ScenarioBoardExists}, " +
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
            ReleaseScreenBind(e.Window, "the game hid it again");
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
            // ModBuild 196: do not assert a float for a window the tick is about to refuse — a
            // sub-view of an already-floated screen is presented INSIDE it (the tick prints the
            // full reason once per window type). See RendersInsideFloatedAncestor.
            else if (RendersInsideFloatedAncestor(e.Window))
                VRLog.Info("WorldUI", $"MODAL FALLBACK: window '{e.Window.name}' (ID {e.Id}) opened as a " +
                                      "SUB-VIEW of a window that is already floated — presented inside that " +
                                      "host, not floated on its own and no ModalUI.");
            else
                VRLog.Info("WorldUI", $"MODAL FALLBACK: window '{e.Window.name}' (ID {e.Id}) opened without a " +
                                      $"VR conversion (room={VRModeStateMachine.TableInFrontOfPlayer}, of which " +
                                      $"scenario board={VRModeStateMachine.ScenarioBoardExists}) → " +
                                      "ModalUI + floating window (or screen) while it stays open in a room.");
        }
    }

    /// <summary>
    /// Fly every hover card over the symbol the pointer is on, billboarded to the head, for
    /// exactly as long as the hover lasts (user ruling — see <see cref="IsMapRoomHoverCard"/>).
    ///
    /// <para>POSE, NOT PARENT. The card is not parented to the icon: the icon is a game object the
    /// map rebuilds on every <c>InitMap</c>, and a host parented into that hierarchy would be torn
    /// down with it mid-frame. Writing the pose each tick costs two transform writes and survives
    /// the map being rebuilt underneath it.</para>
    ///
    /// <para>THE POSE MATH AND THE TWO WRITERS IT HAS TO WIN AGAINST live in
    /// <see cref="MapRoom.HoverCardPose"/> (ModBuild 188): the game's own
    /// <c>UIFollowMapLocationInsideArea</c> is still running on the floated popup and writes both a
    /// screen-derived <c>localPosition</c> and a fresh PIVOT into the very rect the conversion
    /// re-parented — a different wrong offset per icon, computed through the map camera this mod
    /// freezes — and the card's height cannot be taken from its host rect, because a hover card is
    /// deliberately not content-fit. That file carries the evidence; this loop just walks the
    /// floated set.</para>
    ///
    /// <para>No anchor (the pointer left the icon while the game still has the popup open for a
    /// frame or two) leaves the card exactly where it was — it is about to be closed anyway, and
    /// snapping it to a fallback spot on the way out would be a visible jump.</para>
    /// </summary>
    private static void TickHoverCards()
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!wp.HoverCard || !wp.Panel.IsAlive || wp.Panel.HostGo == null)
                continue;
            bool hasAnchor = MapRoom.MapRoomDriver.TryHoverAnchor(out Vector3 anchor);
            MapRoom.HoverCardPose.Place(wp.Panel, hasAnchor, anchor, Rig.VRRigDriver.HeadCamera);
        }
    }

    /// <summary>
    /// The floated window carrying this ID, or null when no such window is currently a world-space
    /// panel. ASKS THE FLOAT SET, not the game: a window can be open and not floated (the parent
    /// won, the catch-all refused it, conversion failed), and something that wants to park content
    /// INSIDE a floated host needs the host to actually exist in world space. Windows the user is
    /// closing are excluded — their host is on its way out.
    /// </summary>
    internal static UIWindow? FloatedWindowWithId(UIWindowID id)
    {
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            if (wp.UserClosing || wp.Window == null || !wp.Panel.IsAlive || wp.Panel.HostGo == null)
                continue;
            if (wp.Window.ID == id)
                return wp.Window;
        }
        return null;
    }

    /// <summary>The floated guildmaster destination window, or null. Newest first — the game only
    /// ever has one mode active, so a second one can exist for at most the frames one is
    /// releasing while the next converts.</summary>
    private static UIWindow? FirstFloatedDestination()
    {
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            if (wp.UserClosing || !wp.Panel.IsAlive || wp.Window == null)
                continue;
            if (MapRoom.GuildmasterDestinations.IsDestination(wp.Window))
                return wp.Window;
        }
        return null;
    }

    // ---- THE MAP ROOM'S WINDOW SLOTS (slot reservation) --------------------------------------
    //
    // USER RULING, verbatim (report item 3):
    //
    //   "Die Fenster verändern ständig ihre Position wenn ein neues Fenster gespawned wird oder
    //    schließt. Das soll nicht sein - ohne explizite Bewegung vom User, sollen sie ihre Position
    //    nicht verändern. Spawne die Fenster so, das alle im Sichtfeld passen aber einmal gespawned
    //    sind sie fix."
    //
    // WHAT STOOD HERE, AND WHY IT MOVED THEM. ModBuild 183 laid the room out with
    // RelayoutMapRoomArc: whenever the floated SET changed — any add, any close, any release — it
    // walked <see cref="Converted"/> backwards and REWROTE the horizontal direction of every
    // non-grabbed window, so the newest sat dead ahead and the older ones stepped outward along
    // 0°, +34°, −34°, +68° … Its own header called that "everything already open steps aside",
    // which is exactly the behaviour being rejected: opening the temple moved the merchant, and
    // closing the temple moved it back. The change detector was the ARC COUNT
    // (ArcWindowCount() != _lastArcCount), so every open and every close fired it.
    //
    // WHAT PART OF 183 SURVIVES. The arc SHAPE was never the complaint — it was asked for
    // ("Ich möchte das die Fenster … im Halbkreis um einen gespawned werden, so dass man alle
    // direkt perfekt im Überblick hat"), which this ruling repeats. What changes here is who
    // decides the angle and when: from "recompute the ensemble on every event" to "claim one seat
    // at spawn". (183's literal ANGLES — 0, ±34°, ±68° — were carried over bit for bit by
    // ModBuild 192 and are GONE as of ModBuild 193: ±68° is outside the headset's field of view
    // and was the cause of the next report. See the ModBuild 193 header below; the reservation
    // model described here is untouched by that change.) The reading distance and the yaw-only
    // facing ARE kept bit for bit.
    //
    //   * a window CLAIMS the free slot nearest the centre the moment it is placed
    //     (ComputeHmdPose, part 9 — the one placement authority, so the claim rides through the
    //     same board-floor / eye-cap clamps and the same pre-reveal re-place as every other spawn);
    //   * it HOLDS that slot for its whole floated life — nothing in this class writes the pose of
    //     a standing map-room window again;
    //   * it RELEASES the slot when it stops floating, and the release moves NOTHING. The freed
    //     slot simply becomes available to the next window that opens, which may well be the same
    //     window re-opened at a different angle. That is expected and stated in the ruling
    //     ("einmal gespawned sind sie fix" is about the life of ONE float, not across re-opens).
    //
    // WHY A REGISTRY AND NOT A FIELD ON WindowPanel: WindowPanel lives in part 3, which this lane
    // does not own. The array IS the claim — slot i is occupied by the panel stored at i — so the
    // "which angle does this window hold" question is answered by one short scan and there is no
    // second copy of the truth to fall out of sync with the first.
    //
    // A WINDOW THE PLAYER HAS MOVED KEEPS ITS SLOT, deliberately. GrabbableModal.UserMoved latches
    // on the first grip and is already honoured everywhere a pose can be written (the presence-
    // regain refloat skips it; the pre-reveal re-place refuses to touch a grabbed OR an already
    // visible window, and a window the player has gripped is by construction both). Freeing the
    // slot of a carried-off window would let the NEXT window spawn on the angle it vacated — and
    // since an arc spawn deliberately does no modal-vs-modal box test (the slot is what
    // deconflicts), a window the player only NUDGED would then have a fresh one materialise on top
    // of it. Holding the claim costs one seat of the room's capacity and cannot superimpose
    // anything on the window the player carried off; that is
    // the safer side of the trade, and the player who moved one window can move the next.
    //
    // MULTIPLAYER: nothing here goes on the wire, and nothing may. Which windows a client has open,
    // and where in ITS room they stand, is local presentation — it is derived from that client's
    // own head pose at its own spawn moments, and two clients in the same map room legitimately see
    // their own arrangement. No NetProtocol record, no ModBuild-relevant wire change.

    // ---- ModBuild 193: THE SLOTS ARE DERIVED FROM THE FIELD OF VIEW, NOT FROM A FIXED STEP ----
    //
    // USER REPORT (item 4, verbatim):
    //
    //   "Neue Fenster spawnen irgendwo an der Seite wo man sie nicht sieht - sie sollen IM
    //    SICHTFELD spawnen, möglichst so das sie nicht mit einem anderen Fenster überlappen, aber
    //    IM SICHTFELD."
    //
    // THE MEASUREMENT THAT NAMES THE CAUSE. ModBuild 192's reservation was right — windows stopped
    // moving and that half of the ruling is settled — but its five seats were hard-coded at
    // 0°, ±34°, ±68° from the spawn gaze, and its own hardware log shows the fourth window taking
    // one of the outer ones:
    //
    //   "'GloomhavenVR.Panel_Modal_UI Shop Item Window' claimed arc slot 3 (68° from the spawn
    //    gaze, + = right) — the free slot NEAREST THE CENTRE (3 of 5 were taken)"
    //
    // A Quest 3 over Virtual Desktop shows roughly 110° horizontally, i.e. about ±55° monocular and
    // rather less than that with BOTH eyes; a window centred at 68° is past the edge of the display
    // entirely. That is precisely "irgendwo an der Seite wo man sie nicht sieht", and it was not a
    // tuning miss — the number 34 was a guess about window width and 85 was a guess about the
    // headset, and neither was ever measured against the hardware.
    //
    // WHAT REPLACES THEM. Two measurements, both taken at runtime, neither hard-coded:
    //
    //   1. THE USABLE CONE comes off the head camera's own PROJECTION MATRIX (see
    //      <see cref="UsableHalfConeDeg"/>). Not <c>Camera.fieldOfView</c>: that field is VERTICAL,
    //      and in XR it is not even the camera's own — the XR runtime overrides the projection per
    //      eye with an ASYMMETRIC frustum that no single fov/aspect pair can express. Converting
    //      "vertical fov × aspect" would additionally be wrong in the one direction that matters
    //      here, because a per-eye target of 3072x3264 has aspect 0.94 and would report a
    //      horizontal field NARROWER than the vertical one.
    //   2. THE WINDOW'S OWN ANGULAR WIDTH is 2 · atan(halfWidth / distance) from the two numbers
    //      the placement already carries: the world half-size handed to ComputeHmdPose and the
    //      reading distance WindowDistanceMeters × scale. BOTH ARE WORLD UNITS (the map room's
    //      diorama scale is ~198 world units per real metre), and the ratio is dimensionless, so
    //      the angle is correct without either of them being converted — that is the whole reason
    //      it is computed as a ratio and not from the metre constant alone. Mixing the two has
    //      shipped as a bug in this repo before (a bound named "…Meters" clamped against a
    //      world-unit product).
    //
    // Slots are then PACKED: a window reserves the angular INTERVAL it actually covers, and the
    // next one takes the interval nearest the centre that is free. Nothing is spaced at 34° because
    // nothing is 34° wide — the hardware log's own MODAL WINDOW SIZE lines measure the party roster
    // at 0.29 m (≈14° at reading distance) and a full-width 1920 px window at 1.00 m (≈45°). The
    // fixed step both wasted the room three narrow windows needed AND pushed the fourth outside the
    // display; measuring removes both faults with one change.
    //
    // THE PRIORITY ORDER IS THE USER'S, AND IT IS NOT SYMMETRIC. "IM SICHTFELD" is stated twice and
    // unconditionally; "möglichst so das sie nicht mit einem anderen Fenster überlappen" is
    // explicitly qualified. So:
    //
    //      (a) IN THE CONE — ALWAYS. A window's centre is clamped to ±(cone − ownHalfWidth) so its
    //          EDGES, not merely its centre, are inside the readable cone. This clamp is absolute.
    //      (b) NOT OVERLAPPING — IF POSSIBLE. When no free interval remains inside the cone the
    //          window is placed INSIDE THE CONE ANYWAY and overlaps, and the log says by how many
    //          degrees. It is NEVER parked outside the cone to avoid an overlap.
    //
    // AND THE ARITHMETIC SAYS THAT WILL HAPPEN. A full-width window is 1.00 m across at 1.2 m
    // (legibility 1.25 × ModalTargetWidthMeters 0.80), i.e. 2·atan(0.50/1.20) = 45.2°. Two of those
    // side by side need 45.2° of centre separation, and a ±34° comfort cone only offers
    // ±(34 − 22.6) = ±11.4° of centre travel — 22.8° in total. TWO FULL-WIDTH WINDOWS CANNOT BOTH
    // BE INSIDE THE CONE AND NON-OVERLAPPING AT THE READING DISTANCE. This is not a defect in the
    // packer, it is the geometry, and the user has already ruled which side of it to take.
    //
    // WHAT WAS REJECTED. Pushing the second window further away DOES buy the angle (1.00 m at 1.8 m
    // is 2·atan(0.50/1.80) = 31.0°, which fits twice inside ±34°), but it costs 33% of the window's
    // apparent size — and the reading distance is a tuned, accepted value that this lane does not
    // own (WindowDistanceMeters lives in ModalFallback.1.Core.cs). Trading away legibility the user
    // approved, to buy a non-overlap he called optional, is the wrong way round. Rejected.
    //
    // WHAT WAS CHOSEN WHEN THE CONE IS FULL — SPREAD + DEPTH, AND NO VERTICAL TERM:
    //   * SPREAD: the overlapping window takes the in-cone angle that maximises its distance from
    //     every existing window's centre. Two 45° windows whose centres are 22° apart still show
    //     ~22° of each other; concentric ones show nothing of the one behind.
    //   * DEPTH: it is pulled toward the head by <see cref="OverlapDepthStepMeters"/> per overlap
    //     generation, floored at <see cref="MinOverlapDistanceMeters"/>. Nearer means it draws in
    //     front (CanvasConversion.8.Order rewrites the sorting order every frame from the measured
    //     eye distance), so the newest window is the readable one and the older one is only
    //     partially covered — and the pull is along the window's own FLATTENED radial, whose y is
    //     zero, so it changes the distance and NOTHING ELSE. The step is deliberately SMALL (4 cm,
    //     not the scenario table's 14 cm): a nearer window is an angularly WIDER window, so a big
    //     pull feeds the overlap it is trying to remedy. That is measured, not asserted — see the
    //     constant's own doc for the four-generation simulation that forced the number down.
    //   * NO VERTICAL STAGGER, deliberately, and this is a DEPARTURE from ModBuild 192's overflow
    //     branch, which nudged down by SecondaryStaggerMeters. Down is where the control board is —
    //     the reason MaxSpawnPitchDeg exists at all — so ClampSpawnPose's board-top floor would
    //     catch the nudge and put two windows back on the SAME height, which is the aliasing the
    //     old ±85° clamp was condemned for. Removing it also keeps ModBuild 192's stated invariant
    //     literally true: "the raw pose is the ordinary gaze spawn ROTATED ABOUT WORLD UP, so
    //     distance, downward gaze bias and therefore HEIGHT out of ClampSpawnPose are bit-for-bit
    //     what they were". A yaw about world up preserves y, and a pull along a y-zero vector
    //     preserves y, so EVERY window this file places — overlapping or not — still has
    //     bit-for-bit ModBuild 192's height. Pitch and roll stay zero by construction and are still
    //     measured and printed by <see cref="Upright"/> on every single spawn.
    //
    // WHAT IS NOT TOUCHED, BECAUSE IT IS ALREADY RIGHT (and each of these was a shipped defect):
    //   * ModBuild 181 — "so weit neben mir, dass ich es zuerst nicht bemerkt habe": the allocator
    //     still fills CENTRE-OUTWARD. It does not index by "how many are open"; it searches for the
    //     free interval with the smallest |angle|.
    //   * ModBuild 183 — re-posing the whole set on every add/remove: NOTHING here ever writes a
    //     standing window's pose. A claim is made once, at spawn, and held for the window's life;
    //     a release frees an interval and moves nothing.
    //   * The old ±85° clamp aliased slot 6 and slot 8 onto one angle. There is no clamp that can
    //     alias two windows here: the centre clamp is a bound on ONE window's own centre, and two
    //     windows that both hit the bound are separated by the spread search, which maximises the
    //     distance between centres and can never return an existing centre while any other point
    //     in the range scores higher.
    //   * The MAP ROOM'S CENTRE is taken by the character screen, which converts first and by user
    //     ruling is permanent and non-closable. It therefore claims at 0° with an empty registry
    //     and keeps 0° forever; every later window is packed AROUND it, which is exactly what the
    //     centre-outward search does with a claim already sitting on 0°.
    //   * Hover cards never claim (TickHoverCards owns their pose), level messages never claim
    //     (chain-continuity ruling), the error box never claims (nothing would release it).
    //
    // MULTIPLAYER: unchanged and still nothing on the wire. Where a window stands is derived from
    // THIS client's head pose at THIS client's spawn moment and from THIS client's headset
    // projection matrix — two players with different headsets legitimately get different cones and
    // different angles. There is no shared state, no NetProtocol record, and nothing here that a
    // remote peer could disagree with.

    /// <summary>
    /// How many windows the map room may seat at once. There are no longer FIXED slots — a claim is
    /// an angular interval, and how many fit depends on how wide the windows actually are — so this
    /// is a capacity bound on the registry and nothing more. Eight is already a room with eight
    /// open windows; past it a window is still placed (in the cone, overlapping) but holds no claim,
    /// and the log says so instead of pretending otherwise.
    /// </summary>
    private const int MaxWindowClaims = 8;

    /// <summary>
    /// THE COMFORT MARGIN, and why it is a fraction of a MEASURED cone rather than a constant.
    /// The derived binocular half-angle is where a window's edge would be exactly ON the edge of
    /// what both eyes can see — readable only by turning the eyes to the limit, through the part of
    /// the lens with the worst blur and distortion. 0.80 keeps the outer fifth of the field free,
    /// so a window that the packer calls "in view" is one that can be READ without the head moving.
    /// It is one number, in one place, and the log prints both the raw cone and the margined one so
    /// the next round can move it on evidence rather than on another guess.
    /// </summary>
    private const float ViewConeComfortFraction = 0.80f;

    /// <summary>Used ONLY when no projection matrix can be read at all (see
    /// <see cref="UsableHalfConeDeg"/>, which Warns when it falls this far). 35° is the margined
    /// value a Quest-class headset produces, so the fallback fails toward the hardware in hand
    /// rather than toward the ±68° that caused the report.</summary>
    private const float FallbackUsableHalfConeDeg = 35f;

    /// <summary>Sanity floor on the DERIVED cone. A projection matrix that reports a 5° or a 90°
    /// half-field is a matrix we have misread or a camera that is not the headset's; clamping keeps
    /// one bad read from stacking every window on 0° or scattering them behind the player, and the
    /// clamp is reported on the geometry line when it engages.</summary>
    private const float MinUsableHalfConeDeg = 20f;

    /// <summary>Upper sanity bound on the derived cone — see <see cref="MinUsableHalfConeDeg"/>.</summary>
    private const float MaxUsableHalfConeDeg = 55f;

    /// <summary>Breathing room between two neighbouring windows' edges, degrees. Zero would let two
    /// windows touch exactly, which reads as one wide window with a seam; 2° at reading distance is
    /// a visible gap of about 4 cm.</summary>
    private const float NeighbourGapDegrees = 2f;

    /// <summary>
    /// How much CLOSER to the head each overlap generation is pulled, real metres (scaled by the
    /// diorama scale at use). Its ONLY job is to break the depth tie: CanvasConversion.8.Order
    /// rewrites every floated panel's sorting order each frame from its MEASURED eye distance, so
    /// two windows at the identical distance sort arbitrarily and can flicker, while any consistent
    /// separation makes the newest one draw in front. 4 cm at reading distance is ~8 world units at
    /// the map room's ~198 scale — orders of magnitude above that measurement's noise.
    ///
    /// <para>WHY IT IS SMALL, which is not a taste call but a correction. It was 0.14 m (the
    /// scenario table's <see cref="SecondaryForegroundMeters"/>) in the first cut of this change,
    /// and simulating the packer against it showed the fault: pulling a window NEARER makes it
    /// ANGULARLY WIDER, which shrinks the cone room left for it, which forces more overlap, which
    /// pulls the next one nearer still. Four generations of 0.14 m turned a 45° window into a 71°
    /// one that no longer fitted the cone at all — the remedy re-creating the reported bug. At
    /// 0.04 m the same four generations cost under 2° of width in total and the geometry stays
    /// where the measurement put it.</para>
    /// </summary>
    private const float OverlapDepthStepMeters = 0.04f;

    /// <summary>Floor on the pulled-in reading distance, real metres. The depth ladder may spend at
    /// most <c>WindowDistanceMeters − this</c> in total, so a deep stack cannot walk the newest
    /// window to arm's length, where a 1 m panel is both unreadable and physically in the way of
    /// the control board. Since ModBuild 241 raised the reading distance to 1.40 m this allows
    /// TWELVE generations before it binds — and the deepest level an <see cref="MaxWindowClaims"/>
    /// registry can produce is 7 (a chain of eight windows each in front of the last), so the floor
    /// is no longer reachable at all: it went from 0.02 m of margin at the old 1.20 m to 0.27 m.
    /// The "two windows share a plane" branch in the audit is therefore now a falsifier for a
    /// mis-computed level rather than a state the room can reach by being busy.</summary>
    private const float MinOverlapDistanceMeters = 0.90f;

    /// <summary>
    /// ONE CLAIM: the angular interval a floated window has reserved, in degrees from ITS OWN spawn
    /// gaze, + = right. <see cref="ArcClaim.Panel"/> null = the entry is free. Written ONLY by
    /// <see cref="TryClaimArcSlot"/> (spawn / presence-regain refloat), narrowed once by
    /// <see cref="NarrowArcClaim"/> (the pre-reveal re-place, which may only SHRINK it), and cleared
    /// by <see cref="ReleaseFinishedArcSlots"/> (the window stopped floating) or by Detach.
    /// </summary>
    private struct ArcClaim
    {
        /// <summary>The claiming panel. Null = free. The array IS the claim — there is no second
        /// copy of this truth anywhere to fall out of sync with.</summary>
        public ConvertedPanel? Panel;

        /// <summary>Log name, kept separately because the RELEASE line has to name the window after
        /// its host has already been destroyed.</summary>
        public string? Name;

        /// <summary>Centre of the reserved interval, degrees from the spawn gaze, + = right.</summary>
        public float CentreDeg;

        /// <summary>
        /// WORLD YAW OF THE GAZE THIS WINDOW WAS PLACED FROM — the frame in which the promise "im
        /// Sichtfeld" was made to it, and the reference the arc audit grades it against.
        ///
        /// <para>WHY THE AUDIT CANNOT USE THE CURRENT GAZE INSTEAD. Turning is free and is the one
        /// thing this mod may never block, so a window placed thirty seconds and a 90° turn ago is
        /// legitimately behind the player now. Grading it against where he is looking THIS instant
        /// would make the falsifier scream about a placement that was correct, and a falsifier that
        /// cries wolf is worse than none. Grading it against its OWN spawn gaze asks the only
        /// question the packer is answerable for: was it in the field of view WHEN IT ARRIVED.</para>
        ///
        /// <para>It is a recorded fact about a moment, not a measurement of the window — the
        /// window's position on that line is measured live off its transform, and the two are kept
        /// visibly separate in the audit text for that reason.</para>
        /// </summary>
        public float SpawnGazeWorldYaw;

        /// <summary>
        /// Half the window's angular width at the distance it was placed at, degrees. The reserved
        /// interval is <c>CentreDeg ± HalfWidthDeg</c>.
        ///
        /// <para>SINCE ModBuild 234 THIS IS WHAT THE WINDOW DRAWS, NOT WHAT ITS FRAME SPANS — see
        /// <see cref="FrameHalfWidthDeg"/> for the frame, and the ArcSeats.cs header for why the
        /// two had to be separated. Until the drawn extent is measurable (it is not at spawn: the
        /// window is still behind the reveal gate and no graphic passes the visibility test) this
        /// falls back to the frame and the two are equal, which is the pre-234 behaviour exactly.</para>
        /// </summary>
        public float HalfWidthDeg;

        /// <summary>
        /// Angular offset of the DRAWN content's centre from the HOST RECT's centre, degrees at
        /// <see cref="DistanceWorld"/>, + = to the player's right. Zero for every window whose
        /// content is centred in its own frame, which is nearly all of them.
        ///
        /// <para>WHY A RESERVATION NEEDS AN OFFSET AT ALL. 'New Party display' draws a 328 px
        /// character column at x −818 inside a 1988 px frame, so its visible column sits ~36° to
        /// the LEFT of the rect the placement positions. Booking a narrower interval without
        /// recording where that interval actually SITS would reserve the wrong 14° — the packer
        /// would keep the middle of an empty frame clear and seat the next window straight through
        /// the character column. The seat search therefore places the window so that
        /// <c>hostCentre + DrawnOffsetDeg</c> lands in the free interval, and the host centre is
        /// recovered from the seat by subtracting this.</para>
        /// </summary>
        public float DrawnOffsetDeg;

        /// <summary>
        /// Half the HOST RECT's angular width at <see cref="DistanceWorld"/>, degrees — the
        /// window's collider footprint, centred on <c>CentreDeg − DrawnOffsetDeg</c>.
        ///
        /// <para>IT IS KEPT BECAUSE THE LASER STILL SEES IT. The hit rect a ray is tested against
        /// is <c>Content ∪ Host</c> and therefore never shrinks below the frame — measured, from
        /// his own log: <c>HIT RECT 'New Party display': host rect 1988x1080; DRAWN CONTENT
        /// 328x1080 at (-818,0); LARGER = the host rect → HIT RECT 1988x1080</c>, i.e. a 2.09 m
        /// invisible sheet. Angle is decided by what the window DRAWS; DEPTH is decided by what it
        /// FRAMES, so a window seated inside someone else's empty frame is still unambiguously the
        /// nearer plane and still wins the ray. See <c>ArcSeatFramePush</c>.</para>
        /// </summary>
        public float FrameHalfWidthDeg;

        /// <summary>Reading distance this claim was measured at, WORLD units (already scaled).
        /// Kept so <see cref="NarrowArcClaim"/> can re-measure the same window's angular width from
        /// its final fitted size without re-deriving the placement.</summary>
        public float DistanceWorld;

        /// <summary>
        /// THE DEPTH LEVEL on the map room's one depth ladder. 0 = this window's FOOTPRINT (what it
        /// draws ∪ what its frame spans, i.e. the hit rect) intersects nothing standing and it
        /// hangs at the nominal reading distance. k ≥ 1 = it is one step in front of the deepest
        /// window it intersects — the user's rule 3, "wenn das nicht vermeidbar ist dann sollte das
        /// neue Fenster näher heran vor dem anderen Fenster spawnen".
        ///
        /// <para>IT IS A MAX, NOT A RUNNING COUNT (it was "the k-th window that had to overlap"
        /// before this build). Two windows that do not touch each other may share a level, so the
        /// ladder never marches a window toward the player for a collision it is not part of.</para>
        /// </summary>
        public int OverlapRank;

        /// <summary>Total depth term this seat carries, REAL metres: positive = pulled toward the
        /// head (the overflow rule's foreground ladder), negative = pushed away (the phantom-frame
        /// push, <c>ArcSeatFramePush</c>). Stored rather than re-derived so a presence-regain
        /// refloat reproduces the same distance instead of recomputing a rank it no longer has the
        /// inputs for.</summary>
        public float DepthPullMeters;

        /// <summary>Worst overlap with any neighbour at claim time, degrees (0 = none). Printed on
        /// the spawn line so "these two are on top of each other" is answerable from the log.</summary>
        public float OverlapDeg;

        /// <summary>
        /// THE CLAIMING WINDOW IS PERMANENT — it has no X and the player cannot close it in the map
        /// room (<see cref="IsMapRoomPermanent"/>: the character screen and the quest log).
        ///
        /// <para>WHY THE REGISTRY HAS TO KNOW (ModBuild 194 report). The overflow rule "in view
        /// wins, overlap is the price" is right as a rule, but it is not indifferent to WHOM it
        /// spends that price on. The 194 log: the merchant claimed 8°±24° and
        /// <c>"overlaps 'GloomhavenVR.Panel_Modal_New Party display' by 39° of the 48° it spans"</c>
        /// — i.e. it buried the ONE window the player can neither close nor dismiss. He could not
        /// read the character screen, so instead of closing the merchant he dragged it aside by
        /// hand, which left the shop's selection mode live and cost him the whole test round.</para>
        ///
        /// <para>A closable window that gets covered is a two-second problem: press its X, or move
        /// it. A permanent window that gets covered has no such exit, so covering it is strictly
        /// worse and the packer must prefer the other victim whenever a choice exists. Recorded
        /// at claim time (spawn only) so the choice costs one bool per claim and no lookups.</para>
        /// </summary>
        public bool Permanent;
    }

    /// <summary>THE CLAIM REGISTRY. See <see cref="ArcClaim"/>.</summary>
    private static readonly ArcClaim[] _arcClaims = new ArcClaim[MaxWindowClaims];

    /// <summary>Spawn-only scratch for the candidate angles the packer tests: dead centre, the two
    /// edges of the map room's MAP CHANNEL (ArcSeats.cs — the parchment's own angular interval, the
    /// one thing in that room that is not a window and must never be covered), and two per standing
    /// claim edge. Static because this path runs a handful of times per session on the Unity main
    /// thread and a per-spawn allocation would be pure litter. The +3 is 1 + 2 and both terms are
    /// load-bearing: the claim loops guard with <c>candidateCount + 1 &lt; Length</c>, so a full
    /// registry of <see cref="MaxWindowClaims"/> fills this array exactly and never past it.</summary>
    private static readonly float[] _arcCandidates = new float[3 + 2 * MaxWindowClaims];

    /// <summary>One-time geometry report (see <see cref="LogArcGeometryOnce"/>).</summary>
    private static bool _arcGeometryLogged;

    /// <summary>The derived, margined half-cone in degrees, or NaN until it has been read. Cached
    /// because a headset's projection does not change within a session; left NaN on failure so a
    /// later spawn (by which time the rig may exist) retries instead of freezing a fallback.</summary>
    private static float _usableHalfConeDeg = float.NaN;

    /// <summary>Where <see cref="_usableHalfConeDeg"/> came from, and the raw numbers behind it —
    /// printed verbatim on the geometry line so the cone is auditable from the log alone.</summary>
    private static string _coneSource = "not measured yet";

    /// <summary>True once the "no projection matrix at all" Warn has fired (once per session).</summary>
    private static bool _coneFallbackWarned;

    /// <summary>
    /// The horizontal half-angles of one projection matrix, degrees, measured from its own optical
    /// axis. Returns false for anything that does not look like a perspective projection.
    ///
    /// <para>THE DERIVATION, so a reader does not have to trust it. Unity's projection has
    /// <c>m00 = 2n/(r−l)</c> and <c>m02 = (r+l)/(r−l)</c>, and a view-space point projects to
    /// <c>ndc.x = m00·(x/−z) − m02</c>. Setting ndc.x = +1 gives the right edge at
    /// <c>tan = (1 + m02)/m00</c>, and ndc.x = −1 gives the left edge at <c>tan = (1 − m02)/m00</c>.
    /// Substituting the two definitions back reduces those to <c>r/n</c> and <c>−l/n</c>, which is
    /// the identity that makes this valid for an ASYMMETRIC frustum — i.e. for every XR eye, where
    /// the two sides genuinely differ and a single "field of view" number does not exist.</para>
    /// </summary>
    private static bool TryProjectionHalfAngles(Matrix4x4 p, out float leftDeg, out float rightDeg)
    {
        leftDeg = 0f;
        rightDeg = 0f;
        float m00 = p.m00;
        float m02 = p.m02;
        if (float.IsNaN(m00) || float.IsInfinity(m00) || float.IsNaN(m02) || float.IsInfinity(m02))
            return false;
        if (m00 <= 1e-4f)
            return false; // orthographic, zeroed, or simply not a matrix we understand
        float tanRight = (1f + m02) / m00;
        float tanLeft = (1f - m02) / m00;
        if (tanRight <= 1e-4f || tanLeft <= 1e-4f)
            return false;
        rightDeg = Mathf.Atan(tanRight) * Mathf.Rad2Deg;
        leftDeg = Mathf.Atan(tanLeft) * Mathf.Rad2Deg;
        return rightDeg > 1f && leftDeg > 1f && rightDeg < 89f && leftDeg < 89f;
    }

    /// <summary>
    /// THE USABLE HALF-CONE, DERIVED — degrees each side of the spawn gaze inside which a window
    /// may be placed. Measured once per session and cached; never a hard-coded 55.
    ///
    /// <para>THE LADDER, best source first, and the log always names which rung answered:
    /// <list type="number">
    /// <item>BOTH STEREO PROJECTION MATRICES (<c>Camera.GetStereoProjectionMatrix</c>). This is the
    /// authority in XR: the runtime writes these per eye and they carry the real asymmetric
    /// frustum, whereas <c>Camera.projectionMatrix</c> read outside rendering is the mono one and
    /// <c>Camera.fieldOfView</c> is a VERTICAL number the runtime has overridden anyway. The cone
    /// taken is the BINOCULAR OVERLAP — for each side, the SMALLER of the two eyes' extents —
    /// because a window only one eye can see is not a window that can be read: it is monocular, it
    /// sits where the other eye's nose occlusion begins, and this mod renders MultiPass, where
    /// exactly that region is where stereo disagreements have shipped as bugs before.</item>
    /// <item>THE MONO PROJECTION MATRIX, for the flat-screen case and for an XR session that has not
    /// produced eye matrices yet. Same arithmetic, one frustum.</item>
    /// <item>VERTICAL FOV × ASPECT, with a Warn. Stated last on purpose: on a per-eye 3072x3264
    /// target the aspect is 0.94, so this rung reports a horizontal field NARROWER than the
    /// vertical one, which is wrong for every headset. It is here only so that a camera with a
    /// custom projection we cannot parse still yields a number in the right decade.</item>
    /// <item><see cref="FallbackUsableHalfConeDeg"/>, with a Warn naming the consequence.</item>
    /// </list></para>
    ///
    /// <para>Never throws: the whole read is wrapped, because this is reached from the spawn path
    /// and an unguarded exception on that path starves VR input.</para>
    /// </summary>
    private static float UsableHalfConeDeg()
    {
        if (!float.IsNaN(_usableHalfConeDeg))
            return _usableHalfConeDeg;

        float rawHalf = float.NaN;
        string source = "no head camera";
        try
        {
            Camera? head = CanvasConversion.WorldCamera;
            if (head != null)
            {
                source = "no parsable projection";
                if (XRSettings.isDeviceActive)
                {
                    Matrix4x4 lp = head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                    Matrix4x4 rp = head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
                    if (TryProjectionHalfAngles(lp, out float lL, out float lR)
                        && TryProjectionHalfAngles(rp, out float rL, out float rR))
                    {
                        // Binocular overlap: a direction is seen by BOTH eyes only within the
                        // narrower of the two eyes' extents on that side. The cone handed to the
                        // packer is symmetric, so it takes the tighter of the two sides.
                        float bothLeft = Mathf.Min(lL, rL);
                        float bothRight = Mathf.Min(lR, rR);
                        rawHalf = Mathf.Min(bothLeft, bothRight);
                        source = $"the STEREO projection matrices (the XR authority): left eye "
                                 + $"{lL:F1}° left / {lR:F1}° right, right eye {rL:F1}° left / "
                                 + $"{rR:F1}° right, i.e. monocular half-fields; binocular overlap "
                                 + $"{bothLeft:F1}° left and {bothRight:F1}° right, symmetric raw "
                                 + $"half-cone {rawHalf:F1}°";
                    }
                }
                if (float.IsNaN(rawHalf)
                    && TryProjectionHalfAngles(head.projectionMatrix, out float mL, out float mR))
                {
                    rawHalf = Mathf.Min(mL, mR);
                    source = $"the MONO projection matrix ({mL:F1}° left / {mR:F1}° right) — no "
                             + "usable stereo eye matrices were available";
                }
                if (float.IsNaN(rawHalf) && head.fieldOfView > 1f && head.aspect > 0.01f)
                {
                    rawHalf = Mathf.Atan(Mathf.Tan(head.fieldOfView * 0.5f * Mathf.Deg2Rad)
                                         * head.aspect) * Mathf.Rad2Deg;
                    source = $"vertical fieldOfView {head.fieldOfView:F1}° × aspect "
                             + $"{head.aspect:F2} — LAST RESORT, this is not a reliable horizontal "
                             + "field on a headset";
                    VRLog.Warn("WorldUI", "MAP ROOM WINDOW SLOTS: no projection matrix could be "
                                          + "parsed, so the usable field of view was estimated from "
                                          + $"vertical fieldOfView {head.fieldOfView:F1}° × aspect "
                                          + $"{head.aspect:F2} = {rawHalf:F1}° half-cone. On a "
                                          + "per-eye 3072x3264 target the aspect is below 1, so "
                                          + "this UNDER-reports the horizontal field and windows "
                                          + "will be packed more tightly than they need to be — "
                                          + "they stay readable and in view, they just overlap "
                                          + "sooner. Reported once.");
                }
            }
        }
        catch (System.Exception ex)
        {
            // House rule for an engine read on a hot path: never propagate, name it once.
            source = $"unreadable ({ex.GetType().Name})";
            rawHalf = float.NaN;
        }

        if (float.IsNaN(rawHalf))
        {
            if (!_coneFallbackWarned)
            {
                _coneFallbackWarned = true;
                VRLog.Warn("WorldUI", "MAP ROOM WINDOW SLOTS: the headset's field of view could NOT "
                                      + "be read (no head camera, or no projection matrix this code "
                                      + $"understands — {source}). Falling back to the stated "
                                      + $"constant {FallbackUsableHalfConeDeg:F0}° half-cone, which "
                                      + "is the margined value a Quest-class headset produces. "
                                      + "Windows still spawn in front of the player and are still "
                                      + "packed by their measured width; only the cone WIDTH is a "
                                      + "guess this session, so on a much wider headset the room "
                                      + "will be packed more tightly than it needs to be. If the "
                                      + "head camera appears later this is retried.");
            }
            // Deliberately NOT cached: leaving it NaN means the next spawn re-reads, so a session
            // that simply started before the rig existed recovers by itself.
            _coneSource = $"the FALLBACK constant (could not measure: {source})";
            return FallbackUsableHalfConeDeg;
        }

        float margined = rawHalf * ViewConeComfortFraction;
        float clamped = Mathf.Clamp(margined, MinUsableHalfConeDeg, MaxUsableHalfConeDeg);
        _coneSource = $"{source}; × comfort margin {ViewConeComfortFraction:F2} = {margined:F1}°"
                      + (Mathf.Abs(clamped - margined) > 0.05f
                          ? $"; CLAMPED to {clamped:F1}° by the [{MinUsableHalfConeDeg:F0}°,"
                            + $"{MaxUsableHalfConeDeg:F0}°] sanity bounds — the raw read looks "
                            + "wrong, check that the head camera really is the headset's"
                          : "");
        _usableHalfConeDeg = clamped;
        return clamped;
    }

    /// <summary>The centre angle of claim <paramref name="slot"/>, degrees from its spawn gaze, or
    /// 0 for a free / out-of-range index (the replay path and the release line both ask).</summary>
    private static float ArcClaimAngleDeg(int slot) =>
        slot >= 0 && slot < _arcClaims.Length ? _arcClaims[slot].CentreDeg : 0f;

    /// <summary>The reserved half-width of claim <paramref name="slot"/> in degrees, or 0 for a
    /// free / out-of-range index. The spawn line prints it so "how wide was it, and did that fit"
    /// is answerable without re-deriving anything.</summary>
    private static float ArcClaimHalfWidthDeg(int slot) =>
        slot >= 0 && slot < _arcClaims.Length ? _arcClaims[slot].HalfWidthDeg : 0f;

    /// <summary>How many claims are held, split by whether they had to overlap.</summary>
    private static void CountArcClaims(out int clean, out int overlapping)
    {
        clean = 0;
        overlapping = 0;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            if (_arcClaims[i].OverlapRank > 0)
                overlapping++;
            else
                clean++;
        }
    }

    /// <summary>The reserved intervals, "centre±half 'name'", for the log lines. Answers "what else
    /// was standing when this window chose its angle" without a second capture.</summary>
    private static string ArcOccupancyText()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(_arcClaims[i].CentreDeg.ToString("F0")).Append("°±")
              .Append(_arcClaims[i].HalfWidthDeg.ToString("F0")).Append("° '")
              .Append(_arcClaims[i].Name ?? "?").Append('\'');
            // ModBuild 194: mark the un-closable ones inline, so a reader of this list can see at a
            // glance which claims a later window may not bury without consequence.
            if (_arcClaims[i].Permanent)
                sb.Append(" PERMANENT/no-X");
        }
        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    /// <summary>
    /// States the room's geometry ONCE per session, so a hardware log can be read without knowing
    /// any constant: where the cone came from, how wide it is raw and after the margin, what the
    /// packing rule is, and what happens when it is full. Called from the first claim, i.e. once
    /// the head camera exists — deriving it earlier would only record a fallback.
    /// </summary>
    private static void LogArcGeometryOnce()
    {
        if (_arcGeometryLogged)
            return;
        float cone = UsableHalfConeDeg();
        if (float.IsNaN(_usableHalfConeDeg))
            return; // still on the fallback: say nothing yet, retry when the rig is up
        _arcGeometryLogged = true;
        float legibility = WindowLegibilityLive();
        float fullWidthDeg = 2f * Mathf.Atan2(ModalTargetWidthMeters * legibility * 0.5f,
            WindowDistanceMeters) * Mathf.Rad2Deg;
        int fullWidthFit = fullWidthDeg > 0.1f
            ? Mathf.Max(1, Mathf.FloorToInt((2f * cone + NeighbourGapDegrees)
                                            / (fullWidthDeg + NeighbourGapDegrees)))
            : 0;
        VRLog.Info("WorldUI", "MAP ROOM WINDOW SLOTS: the usable half-cone is "
                              + $"±{cone:F1}° from the spawn gaze, derived from {_coneSource}. "
                              + "HOW TO READ THIS: a window may be placed anywhere its EDGES stay "
                              + $"inside ±{cone:F1}°; windows are packed by their own measured "
                              + "angular width (2·atan(halfWidth/distance), both WORLD units) and "
                              + $"kept {NeighbourGapDegrees:F0}° apart, and the free interval "
                              + "NEAREST THE CENTRE wins. For scale: a full-width 1920 px window is "
                              + $"{ModalTargetWidthMeters * legibility:F2} m across at the "
                              + $"{WindowDistanceMeters:F2} m reading distance = {fullWidthDeg:F0}° "
                              + $"of view, so at most {fullWidthFit} of THOSE fit side by side — "
                              + "narrower windows (the party roster measures ~0.29 m ≈ 14°) fit "
                              + "several more. WHEN THE CONE IS FULL the next window is placed IN "
                              + "THE CONE ANYWAY and overlaps (user ruling: 'sie sollen IM "
                              + "SICHTFELD spawnen, möglichst so das sie nicht mit einem anderen "
                              + "Fenster überlappen, aber IM SICHTFELD') — spread to the in-cone "
                              + "angle furthest from every neighbour and pulled "
                              + $"{OverlapDepthStepMeters:F2} m nearer per generation so it draws "
                              + $"in front, never nearer than {MinOverlapDistanceMeters:F2} m. "
                              + $"Capacity {MaxWindowClaims} reservations. A window claims ONCE at "
                              + "spawn and keeps its angle until it stops floating; opening or "
                              + "closing a window never moves any other window (user ruling: "
                              + "'einmal gespawned sind sie fix').");
    }

    /// <summary>
    /// The half angular width, in degrees, of a window <paramref name="halfWidthWorld"/> world units
    /// wide (half-extent) seen from <paramref name="distanceWorld"/> world units away.
    ///
    /// <para>BOTH ARGUMENTS ARE WORLD UNITS AND THE RATIO IS DIMENSIONLESS. That is the point: the
    /// map room's diorama scale is ~198 world units per real metre, and this repo has already
    /// shipped a bug where a bound named "…Meters" was compared against a world-unit product. The
    /// angle is taken from the ratio precisely so that neither operand has to be converted, and
    /// both callers pass the scaled pair.</para>
    /// </summary>
    private static float HalfAngleDeg(float halfWidthWorld, float distanceWorld)
    {
        if (distanceWorld <= 1e-4f || halfWidthWorld <= 0f)
            return 0f;
        return Mathf.Atan2(halfWidthWorld, distanceWorld) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// The half-width to reserve for a window whose measured half-size is unusable (no host rect
    /// yet): the widest a window is allowed to be, i.e. the board-relative target
    /// <c>ModalTargetWidthMeters × legibility</c>. Over-reserving is the SAFE direction — it can
    /// only push the NEXT window further out or into overlap, never this one out of the cone.
    /// </summary>
    private static float FallbackHalfWidthWorld(float scale) =>
        ModalTargetWidthMeters * WindowLegibilityLive() * 0.5f * scale;

    /// <summary>True when a window of half-width <paramref name="halfAngle"/> centred on
    /// <paramref name="centreDeg"/> is inside the cone AND clear of every standing claim.</summary>
    private static bool ArcAngleIsFree(float centreDeg, float halfAngle, float centreLimit)
    {
        if (Mathf.Abs(centreDeg) > centreLimit + 1e-3f)
            return false;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            float needed = _arcClaims[i].HalfWidthDeg + halfAngle + NeighbourGapDegrees;
            if (Mathf.Abs(centreDeg - _arcClaims[i].CentreDeg) < needed - 1e-3f)
                return false;
        }
        return true;
    }

    /// <summary>The worst overlap in degrees between a window of half-width
    /// <paramref name="halfAngle"/> at <paramref name="centreDeg"/> and any standing claim (0 = it
    /// is clear of all of them), plus the name of the window it overlaps most.</summary>
    private static float ArcWorstOverlapDeg(float centreDeg, float halfAngle, out string withName)
    {
        float worst = 0f;
        withName = "";
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            float ov = (_arcClaims[i].HalfWidthDeg + halfAngle)
                       - Mathf.Abs(centreDeg - _arcClaims[i].CentreDeg);
            if (ov > worst)
            {
                worst = ov;
                withName = _arcClaims[i].Name ?? "?";
            }
        }
        return worst;
    }

    /// <summary>How far toward the head an overlap generation is pulled, WORLD units. Floored at
    /// <see cref="MinOverlapDistanceMeters"/> so a deep stack cannot arrive at the player's
    /// nose.</summary>
    private static float OverlapPullWorld(int overlapRank, float scale)
    {
        if (overlapRank <= 0)
            return 0f;
        float pull = OverlapDepthStepMeters * overlapRank;                 // real metres
        float maxPull = Mathf.Max(0f, WindowDistanceMeters - MinOverlapDistanceMeters);
        return Mathf.Min(pull, maxPull) * Mathf.Max(scale, 0f);            // → world units
    }

    /// <summary>
    /// The total angular extent of PERMANENT standing claims that a window of half-width
    /// <paramref name="halfAngle"/> centred on <paramref name="centreDeg"/> would cover, degrees —
    /// summed over every permanent claim, because covering two permanent windows is twice the harm
    /// of covering one. 0 when nothing permanent stands or the window clears them all.
    /// </summary>
    private static float PermanentOverlapDeg(float centreDeg, float halfAngle)
    {
        float total = 0f;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null || !_arcClaims[i].Permanent)
                continue;
            float ov = (_arcClaims[i].HalfWidthDeg + halfAngle)
                       - Mathf.Abs(centreDeg - _arcClaims[i].CentreDeg);
            if (ov > 0f)
                total += ov;
        }
        return total;
    }

    /// <summary>Names the permanent claims this angle would cover, with the degrees each loses —
    /// for the log line that has to say WHICH un-closable window is being buried and by how much.
    /// "(none)" when the placement clears every permanent window.</summary>
    private static string PermanentOverlapText(float centreDeg, float halfAngle)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null || !_arcClaims[i].Permanent)
                continue;
            float ov = (_arcClaims[i].HalfWidthDeg + halfAngle)
                       - Mathf.Abs(centreDeg - _arcClaims[i].CentreDeg);
            if (ov <= 0.5f)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(_arcClaims[i].Name ?? "?").Append("' by ")
              .Append(ov.ToString("F0")).Append("° of its ")
              .Append((_arcClaims[i].HalfWidthDeg * 2f).ToString("F0")).Append('°');
        }
        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    /// <summary>
    /// The in-cone angle for a window that cannot avoid overlapping — the overflow remedy. Sampled
    /// rather than solved because the objective has its optimum at an endpoint or a midpoint, and a
    /// ~1°-resolution sweep finds it to within half a degree, which is far below anything the eye
    /// can judge; it runs once per spawn, never per frame. With nothing standing it answers 0°.
    ///
    /// <para>THE RANKING, AND WHY IT CHANGED (ModBuild 194 report — the merchant over the character
    /// screen). ModBuild 193 ranked candidates by ONE number: the minimum distance to any standing
    /// claim's centre, i.e. "spread out". That objective is blind to a distinction the player is
    /// not: a covered CLOSABLE window costs him one press of its X, while a covered PERMANENT
    /// window (<see cref="ArcClaim.Permanent"/> — the character screen and the quest log, both of
    /// which float without an X by standing ruling) has no exit at all. The 194 log shows the shop
    /// window burying the character screen by 39°, and his workaround — grabbing the merchant and
    /// dragging it aside rather than closing it — is what left the shop's selection mode live and
    /// cost him the round. The remedy is not to move anything (that is forbidden) and not to leave
    /// the cone (also forbidden); it is to choose the victim.</para>
    ///
    /// <para>So the sweep now ranks, in order:</para>
    /// <list type="number">
    /// <item>LEAST PERMANENT SURFACE COVERED — <see cref="PermanentOverlapDeg"/>, summed over every
    /// permanent claim. This is the new term and it is FIRST.</item>
    /// <item>then the ModBuild 193 rule verbatim: the largest minimum distance to any standing
    /// centre, so each window still shows a readable strip of itself;</item>
    /// <item>then nearest the gaze centre; then RIGHT, the side every build since 183 has filled
    /// first (without it the ±limit tie would be settled by which end the sweep started at).</item>
    /// </list>
    ///
    /// <para>IT REDUCES TO 193 EXACTLY when nothing permanent stands: term 1 is then 0 at every
    /// candidate and the decision falls through to the old comparison unchanged. And it does NOT
    /// buy a non-overlap that the geometry does not have — when every in-cone angle covers the same
    /// permanent degrees (the merchant case: a 48°-wide window against a 46°-wide centred screen in
    /// a ±32° cone covers 39° of it wherever it is put), term 1 ties and the old rule decides, which
    /// is the honest outcome. What it DOES buy is the second victim: in that same log the chosen
    /// +8° also covered 16° of the permanent quest log, and −8° covers none of it for the identical
    /// 39° on the character screen. One permanent window buried instead of two, for free.</para>
    ///
    /// <para>REJECTED ALTERNATIVE — giving a permanent claim a WIDER reserved interval than its own
    /// width. It reads as the same idea and is not: the reservation is what the in-cone free-interval
    /// search tests against, so inflating it makes windows that WOULD have fitted cleanly fall into
    /// the overflow branch, and the overflow branch is the one being repaired. It also lies in the
    /// occupancy line about how wide a window is. The ranking above changes only the choice AMONG
    /// angles that already overlap, which is precisely the decision at issue.</para>
    /// </summary>
    private static float SpreadAngleDeg(float centreLimit, float halfAngle)
    {
        if (centreLimit <= 0.5f)
            return 0f;
        int samples = Mathf.Clamp(Mathf.CeilToInt(centreLimit * 2f) + 1, 3, 241);
        float best = 0f;
        float bestScore = -1f;
        float bestPerm = float.MaxValue;
        for (int s = 0; s < samples; s++)
        {
            float a = Mathf.Lerp(-centreLimit, centreLimit, s / (float)(samples - 1));
            float score = float.MaxValue;
            bool any = false;
            for (int i = 0; i < _arcClaims.Length; i++)
            {
                if (_arcClaims[i].Panel == null)
                    continue;
                any = true;
                score = Mathf.Min(score, Mathf.Abs(a - _arcClaims[i].CentreDeg));
            }
            if (!any)
                return 0f;
            float perm = PermanentOverlapDeg(a, halfAngle);

            // (1) Less PERMANENT surface covered always wins. 0.5° of slack, so a rounding-level
            //     difference does not override the separation rule the player actually sees.
            bool permTied = Mathf.Abs(perm - bestPerm) <= 0.5f;
            bool permBetter = perm < bestPerm - 0.5f;
            // (2)-(4) the ModBuild 193 comparison, unchanged, applied within a permanence tie.
            bool tied = Mathf.Abs(score - bestScore) <= 0.01f;
            bool better = permBetter
                          || (permTied
                              && (score > bestScore + 0.01f
                                  || (tied && Mathf.Abs(a) < Mathf.Abs(best) - 1e-3f)
                                  || (tied && Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(best)) <= 1e-3f
                                      && a > best)));
            if (better)
            {
                bestScore = score;
                bestPerm = perm;
                best = a;
            }
        }
        return best;
    }

    /// <summary>
    /// THE MEASURED TRADE, so the next report is decidable instead of a surprise: the smallest
    /// reading distance (real metres) at which this window AND the widest standing PERMANENT window
    /// would BOTH fit inside the cone, side by side, with the usual neighbour gap — or 0 when no
    /// distance up to <paramref name="ceilingMeters"/> achieves it.
    ///
    /// <para>WHY IT IS COMPUTED AND PRINTED RATHER THAN APPLIED. Pushing the window further DOES buy
    /// the angle — a 1.00 m panel is 45.2° at 1.20 m and 31.0° at 1.80 m — but it costs apparent
    /// size in exact proportion. ModBuild 193 rejected spending it to buy a non-overlap the user had
    /// called optional, and that reasoning stands for an ordinary overlap. What it did NOT price is
    /// this case: the covered window is un-closable, so the overlap is not optional for him at all.
    /// Rather than reverse a tuned value unilaterally, the line states the number — "both fit at
    /// X m, which is Y % smaller" — and he decides.</para>
    ///
    /// <para>AND HE DID DECIDE, ModBuild 241: "die Fenster zB die Questinfo immer bisschen zu nah
    /// spawnen, gerne ein bisschen (nicht viel) weiter weg". <c>WindowDistanceMeters</c> moved 1.20
    /// → 1.40 m on that instruction (the whole derivation is on the constant itself, in
    /// ModalFallback.1.Core.cs). This line keeps its job unchanged: it is the number for the NEXT
    /// such decision, and the distance it reports is now measured from 1.40 m, so a figure quoted
    /// from a pre-241 log is not comparable with one quoted from a later log.</para>
    ///
    /// <para>Both widths are WORLD units and the angles are ratios, so nothing here has to be
    /// converted out of the diorama scale (the "…Meters against a world-unit product" bug class).
    /// Spawn path only, ~60 iterations of two atan calls.</para>
    /// </summary>
    /// <param name="halfWidthWorld">This window's half-width, WORLD units.</param>
    /// <param name="scale">Diorama scale (world units per real metre).</param>
    /// <param name="cone">The usable half-cone, degrees.</param>
    /// <param name="ceilingMeters">Stop searching past this reading distance.</param>
    private static float DistanceThatWouldFitMeters(float halfWidthWorld, float scale, float cone,
        float ceilingMeters = 4f)
    {
        // The widest permanent neighbour, expressed as a WORLD half-width at ITS claim distance —
        // that is the physical size that does not change when either window is moved.
        float permHalfWorld = 0f;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null || !_arcClaims[i].Permanent)
                continue;
            float w = Mathf.Tan(_arcClaims[i].HalfWidthDeg * Mathf.Deg2Rad) * _arcClaims[i].DistanceWorld;
            permHalfWorld = Mathf.Max(permHalfWorld, w);
        }
        if (permHalfWorld <= 1e-4f || halfWidthWorld <= 1e-4f || scale <= 1e-4f)
            return 0f;
        for (float d = WindowDistanceMeters; d <= ceilingMeters + 1e-3f; d += 0.05f)
        {
            float dw = d * scale;
            float own = HalfAngleDeg(halfWidthWorld, dw);
            float perm = HalfAngleDeg(permHalfWorld, dw);
            if (2f * own + 2f * perm + NeighbourGapDegrees <= 2f * cone)
                return d;
        }
        return 0f;
    }

    /// <summary>The first free registry index, or −1 when all <see cref="MaxWindowClaims"/> are
    /// held.</summary>
    private static int FirstFreeClaimIndex()
    {
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Claim (or re-find) this window's angular interval. Called from <see cref="ComputeHmdPose"/>,
    /// i.e. at spawn and at presence-regain refloat, and NEVER per frame.
    ///
    /// <para>THE CHOICE RULE — THE FREE INTERVAL NEAREST THE CENTRE. The candidate angles are dead
    /// centre plus, for every standing claim, the two angles that put this window exactly against
    /// that claim's left and right edge (their centre ± their half-width ± own half-width ± gap).
    /// One of those is always the optimum: the nearest-to-centre feasible position is either 0°
    /// itself or flush against something, so testing 1 + 2N angles finds it exactly, with no
    /// stepping and no search tolerance. The smallest |angle| that is inside the cone and clear of
    /// everything wins; a tie between the two sides goes RIGHT, which is the side ModBuild 183 and
    /// 192 both filled first. Why not "the next index after the last one used": that wastes the
    /// middle — close the centre window and every later one would sit off to the side with a hole
    /// where the player is looking. Why not "always dead ahead": dead ahead is only free for the
    /// first window, and taking it from a window already standing there is the move the ruling
    /// forbids.</para>
    ///
    /// <para>WHEN NOTHING FITS — the user's priority, not ours. The window is placed INSIDE THE
    /// CONE and allowed to overlap: the angle chosen is the one that maximises the distance to
    /// every standing centre (so each window still shows a readable strip of itself), and the
    /// window is pulled <see cref="OverlapDepthStepMeters"/> nearer per overlap generation so it
    /// draws in FRONT of what it covers. It is never pushed outside the cone to avoid an overlap —
    /// that is the exact fault being fixed.</para>
    ///
    /// <para>EXCLUSIONS. Outside the map room there is no arc (a scenario table stacks its
    /// secondaries instead). A HOVER CARD never claims: <c>TickHoverCards</c> owns its pose, and it
    /// joins and leaves <see cref="Converted"/> on every mouseover, so a claim would churn the
    /// registry for something that is not a window. A LEVEL MESSAGE never claims (chain-continuity
    /// ruling). The global error box never claims: it is not a <see cref="Converted"/> window, so
    /// nothing would ever release it.</para>
    /// </summary>
    /// <param name="halfSizeWorld">The window's half-extent in WORLD units, as handed to the
    /// placement. x is the half-WIDTH; only x is read here.</param>
    /// <param name="scale">Diorama scale (world units per real metre) at this spawn.</param>
    /// <param name="slot">The claimed registry index, or −1 when the registry is full.</param>
    /// <param name="yawDeg">Degrees to rotate the spawn gaze by, + = right.</param>
    /// <param name="overlapRank">0 = it got a free interval; ≥1 = the k-th overlapping window,
    /// which is also its depth-ladder index.</param>
    /// <param name="foregroundPullWorld">World units to pull the window toward the head along the
    /// FLATTENED forward (y = 0, so the placement's height is untouched). 0 unless overlapping.</param>
    /// <param name="why">Human-readable reason for the log line.</param>
    /// <returns>true when the map room's cone governs this placement.</returns>
    private static bool TryClaimArcSlot(ConvertedPanel? panel, bool levelMessage,
        Vector2 halfSizeWorld, float scale,
        out int slot, out float yawDeg, out int overlapRank, out float foregroundPullWorld,
        out string why)
    {
        slot = -1;
        yawDeg = 0f;
        overlapRank = 0;
        foregroundPullWorld = 0f;
        why = "";
        if (panel == null || levelMessage || !MapRoom.MapRoomDriver.Active)
            return false;
        if (ReferenceEquals(panel, _errorPanel))
            return false; // not a Converted window — no release path would ever free its claim
        if (IsHoverCardPanel(panel))
            return false; // TickHoverCards owns its pose (and it churns on every mouseover)

        LogArcGeometryOnce();

        // Already holds one? Presence-regain refloat re-places an EXISTING float, and it must land
        // back on its own angle rather than take a second claim (and rather than pile every
        // non-grabbed window dead ahead, which is what it did while the relayout owned the arc).
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (!ReferenceEquals(_arcClaims[i].Panel, panel))
                continue;
            slot = i;
            yawDeg = _arcClaims[i].CentreDeg;
            overlapRank = _arcClaims[i].OverlapRank;
            foregroundPullWorld = OverlapPullWorld(overlapRank, scale);
            why = $"re-uses the interval it already claimed ({yawDeg:F0}°±"
                  + $"{_arcClaims[i].HalfWidthDeg:F0}°) — a refloat must not take a second one";
            return true;
        }

        float cone = UsableHalfConeDeg();
        float nominalDist = WindowDistanceMeters * scale;  // WORLD units, both operands scaled
        float halfWidthWorld = halfSizeWorld.x > 1e-4f
            ? halfSizeWorld.x
            : FallbackHalfWidthWorld(scale);
        float halfAngle = HalfAngleDeg(halfWidthWorld, nominalDist);
        CountArcClaims(out int cleanBefore, out int overlapBefore);
        string standing = ArcOccupancyText();

        // ---- (a) IN THE CONE, ALWAYS: the centre bound that keeps the window's EDGES inside.
        float centreLimit = cone - halfAngle;
        bool widerThanCone = centreLimit < 0f;
        if (widerThanCone)
            centreLimit = 0f;

        // ---- (b) NOT OVERLAPPING, IF POSSIBLE: the nearest-to-centre free interval.
        int candidateCount = 0;
        _arcCandidates[candidateCount++] = 0f;
        for (int i = 0; i < _arcClaims.Length && candidateCount + 1 < _arcCandidates.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            float edge = _arcClaims[i].HalfWidthDeg + halfAngle + NeighbourGapDegrees;
            _arcCandidates[candidateCount++] = _arcClaims[i].CentreDeg + edge;
            _arcCandidates[candidateCount++] = _arcClaims[i].CentreDeg - edge;
        }
        bool haveFree = false;
        float bestFree = 0f;
        for (int c = 0; c < candidateCount; c++)
        {
            float a = _arcCandidates[c];
            if (!ArcAngleIsFree(a, halfAngle, centreLimit))
                continue;
            // Nearest the centre wins; a tie between the two sides of a step goes RIGHT (+).
            if (!haveFree
                || Mathf.Abs(a) < Mathf.Abs(bestFree) - 1e-3f
                || (Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(bestFree)) <= 1e-3f && a > bestFree))
            {
                haveFree = true;
                bestFree = a;
            }
        }

        if (haveFree)
        {
            yawDeg = bestFree;
            overlapRank = 0;
            foregroundPullWorld = 0f;
            why = cleanBefore + overlapBefore == 0
                ? $"the room was empty, so it took dead centre; the window is {halfAngle * 2f:F0}° "
                  + $"wide and the usable cone is ±{cone:F1}°"
                : $"the FREE interval NEAREST THE CENTRE ({halfAngle * 2f:F0}°-wide window, "
                  + $"reserving {yawDeg:F0}°±{halfAngle:F0}° inside the ±{cone:F1}° cone with a "
                  + $"{NeighbourGapDegrees:F0}° gap) — it does NOT overlap anything, and the "
                  + $"windows already standing [{standing}] were not touched";
        }
        else
        {
            // ---- THE CONE IS FULL. In view wins; overlap is the price, and it is stated.
            overlapRank = overlapBefore + 1;
            foregroundPullWorld = OverlapPullWorld(overlapRank, scale);
            float pulledDist = nominalDist - foregroundPullWorld;
            // The pull makes the window ANGULARLY WIDER (it is nearer), so the cone bound and the
            // reserved interval are both re-measured at the distance it will actually hang at —
            // otherwise "in the cone" would be checked against a size the window no longer has.
            halfAngle = HalfAngleDeg(halfWidthWorld, pulledDist);
            widerThanCone = cone - halfAngle < 0f;
            centreLimit = Mathf.Max(0f, cone - halfAngle);
            yawDeg = SpreadAngleDeg(centreLimit, halfAngle);
            float pullMeters = foregroundPullWorld / Mathf.Max(scale, 1e-4f);
            float pulledMeters = pulledDist / Mathf.Max(scale, 1e-4f);
            why = $"NO free interval is left inside the cone (±{cone:F1}°) — this window is "
                  + $"{halfAngle * 2f:F0}° wide and the standing set is [{standing}]. IT IS PLACED "
                  + "IN THE CONE ANYWAY AND OVERLAPS, which is the user's stated priority ('sie "
                  + "sollen IM SICHTFELD spawnen, möglichst so das sie nicht mit einem anderen "
                  + "Fenster überlappen, aber IM SICHTFELD'): spread to the in-cone angle furthest "
                  + $"from every neighbour and pulled {pullMeters:F2} m nearer (overlap generation "
                  + $"{overlapRank}, reading distance {pulledMeters:F2} m) so it draws IN FRONT of "
                  + "what it covers instead of merging with it";
        }

        if (widerThanCone)
        {
            why += $". NOTE: this window is WIDER than the whole usable cone ({halfAngle * 2f:F0}° "
                   + $"vs ±{cone:F1}°), so it is centred and its edges hang over by "
                   + $"{halfAngle - cone:F0}° each side no matter where it is put — nothing can be "
                   + "done about that from here; it has to be narrower or further away";
        }

        float overlapDeg = ArcWorstOverlapDeg(yawDeg, halfAngle, out string overlapWith);
        if (overlapDeg > 0.5f)
            why += $". MEASURED: it overlaps '{overlapWith}' by {overlapDeg:F0}° of the "
                   + $"{halfAngle * 2f:F0}° it spans";
        // ModBuild 194: SAY IT IN THOSE WORDS. An overlap on a closable window is a press of its X;
        // an overlap on a permanent one has no exit, and the last report is what happens when the
        // log does not distinguish them — he dragged the merchant aside by hand instead of closing
        // it, which left the shop's selection mode live and cost him the round. The line names the
        // un-closable victims, the degrees each loses, and the ONE lever that would remove it.
        float permOverlap = PermanentOverlapDeg(yawDeg, halfAngle);
        if (permOverlap > 0.5f)
        {
            why += $". PERMANENT WINDOWS COVERED: {PermanentOverlapText(yawDeg, halfAngle)} — "
                   + "those windows have NO X and the player can neither close nor dismiss them "
                   + "(standing ruling), so this overlap is not one he can clear the way he clears "
                   + "any other. The packer already chose the in-cone angle that covers the LEAST "
                   + "permanent surface (see SpreadAngleDeg); what is left is geometry, not a "
                   + "placement mistake";
            float fitAt = DistanceThatWouldFitMeters(halfWidthWorld, scale, cone);
            why += fitAt > 0f
                ? $". THE TRADE, MEASURED: this window and the widest permanent one would BOTH fit "
                  + $"inside the ±{cone:F1}° cone at a reading distance of {fitAt:F2} m instead of "
                  + $"{WindowDistanceMeters:F2} m — that is "
                  + $"{(1f - WindowDistanceMeters / fitAt) * 100f:F0}% less apparent size, in "
                  + "exchange for both windows being readable at once. The reading distance was "
                  + "last moved on the user's own instruction (ModBuild 241, 1.20 -> 1.40 m: "
                  + "'gerne ein bisschen (nicht viel) weiter weg'); this line exists so the NEXT "
                  + "such choice can be made on the number rather than on a guess"
                : ". THE TRADE, MEASURED: no reading distance up to 4.00 m makes this window and "
                  + "the widest permanent one both fit inside the cone — the pair is simply wider "
                  + "than the headset's usable field, and only a NARROWER window (a tighter content "
                  + "fit) can change that";
        }
        if (overlapDeg <= 0.5f && overlapRank > 0)
            why += $". MEASURED: it does NOT actually overlap anything after all — it only failed "
                   + $"to keep the full {NeighbourGapDegrees:F0}° breathing gap, so it was routed "
                   + "through the overlap rule and carries its depth offset. The windows are edge "
                   + "to edge, not on top of each other";

        int free = FirstFreeClaimIndex();
        if (free < 0)
        {
            // Registry full: still placed, still in the cone, but holding no reservation — so a
            // later window may land on the same angle, and the log says so rather than quietly
            // aliasing two windows onto one position.
            slot = -1;
            why += $". THE REGISTRY IS FULL ({MaxWindowClaims} reservations) — this window holds "
                   + "NONE, so a later window may land on the same angle. It is still in view and "
                   + "grabbable; close a window to free a reservation";
            return true;
        }

        slot = free;
        _arcClaims[free] = new ArcClaim
        {
            Panel = panel,
            Name = PanelLogName(panel),
            CentreDeg = yawDeg,
            HalfWidthDeg = halfAngle,
            DistanceWorld = nominalDist - foregroundPullWorld,
            OverlapRank = overlapRank,
            OverlapDeg = overlapDeg,
            Permanent = IsPermanentPanel(panel),
        };
        return true;
    }

    /// <summary>
    /// Is this PANEL one of the map room's permanent, un-closable windows
    /// (<see cref="IsMapRoomPermanent"/> — the character screen and the quest log)?
    ///
    /// <para>Asked of the panel rather than the window for the same reason
    /// <see cref="IsHoverCardPanel"/> is: the placement path only carries the panel, because the
    /// claim is made from inside <c>ComputeHmdPose</c> during the conversion — before the
    /// <c>WindowPanel</c> record exists to be looked up.</para>
    ///
    /// <para>IS-A, NOT RELATED-TO. <c>GetComponent</c> on the converted root's OWN GameObject, never
    /// <c>GetComponentIn{Parent,Children}</c>: this repo has shipped that confusion twice in four
    /// builds and it is what once flew the whole character UI over a map icon. A permanent window's
    /// converted target IS its own <c>UIWindow</c> rect — only the guildmaster destinations convert
    /// a different root (<c>GuildmasterDestinations.PreferredConvertRoot</c>), and none of those is
    /// permanent — so a null here means "not permanent", which is also the safe direction: the
    /// packer then treats it as an ordinary closable neighbour and behaves exactly as ModBuild 193
    /// did.</para>
    /// </summary>
    private static bool IsPermanentPanel(ConvertedPanel panel)
    {
        if (panel.Target == null)
            return false;
        var window = panel.Target.GetComponent<UIWindow>();
        return window != null && IsMapRoomPermanent(window);
    }

    /// <summary>
    /// SHRINK a standing claim to the window's FINAL fitted width. It never moves the claim and
    /// never widens it.
    ///
    /// <para>WHY THIS EXISTS. A window is placed the instant it converts, i.e. BEFORE the content
    /// fit runs — so the width the claim was measured from is the PRE-fit host rect, typically the
    /// whole captured 1920x1080 window. The hardware log makes the size of that error concrete: the
    /// party roster claims as a full-width 1.00 m window (≈45°) and then fits to 328x1080 px =
    /// 0.29 m (≈14°). Left alone, that window would reserve three times the angle it occupies for
    /// its whole life and force every later window into overlap for nothing.</para>
    ///
    /// <para>WHY SHRINKING IS SAFE AND MOVING WOULD NOT BE. This runs on the pre-reveal re-place,
    /// which replays the SAME stored spawn inputs against the final geometry — the window's own
    /// pose is unchanged by this call (the centre is not touched) and no other window is read, let
    /// alone written. All that changes is how much room LATER windows see as taken. Widening is
    /// refused for the same reason: a claim that grew could swallow the interval a neighbour is
    /// already standing in, and the log would then describe a room that does not exist.</para>
    /// </summary>
    private static bool NarrowArcClaim(int slot, Vector2 halfSizeWorld, out string note)
    {
        note = "";
        if (slot < 0 || slot >= _arcClaims.Length || _arcClaims[slot].Panel == null)
            return false;
        if (halfSizeWorld.x <= 1e-4f)
            return false;
        float dist = _arcClaims[slot].DistanceWorld;
        if (dist <= 1e-4f)
            return false;
        float fitted = HalfAngleDeg(halfSizeWorld.x, dist);
        float held = _arcClaims[slot].HalfWidthDeg;
        if (fitted >= held - 0.5f)
            return false; // same size, or the fit made it wider — never widen a standing claim
        _arcClaims[slot].HalfWidthDeg = fitted;
        note = $"reservation NARROWED from ±{held:F0}° to ±{fitted:F0}° now that the content fit is "
               + "final (it was claimed from the pre-fit rect); its own pose did not move and no "
               + "other window was touched — the freed angle is simply available to the next window";
        return true;
    }

    /// <summary>
    /// A HOVER CARD, asked of the PANEL rather than the window (the placement path only carries the
    /// panel). The converted target of a hover card IS the popup/tooltip window's own GameObject —
    /// <c>TryConvertWindow</c> converts <c>window.transform</c> for this family — so reading the
    /// <c>UIWindow</c> back off the target and applying the ONE hover-card rule
    /// (<see cref="IsMapRoomHoverCard"/>) is the same test, not a second one. Spawn-path only.
    /// </summary>
    private static bool IsHoverCardPanel(ConvertedPanel panel)
    {
        if (panel.Target == null)
            return false;
        var window = panel.Target.GetComponent<UIWindow>();
        return window != null && IsMapRoomHoverCard(window);
    }

    /// <summary>The name the rest of the log already uses for a floated panel (its host object).</summary>
    private static string PanelLogName(ConvertedPanel panel) =>
        panel.HostGo != null ? panel.HostGo.name : "<panel>";

    /// <summary>
    /// Free the reservations of windows that have stopped floating, and NOTHING ELSE — no window is
    /// re-posed here, which is the whole point of the ruling: "ohne explizite Bewegung vom User,
    /// sollen sie ihre Position nicht verändern" covers a CLOSE just as much as an open.
    ///
    /// <para>Run from <see cref="Tick"/> between the release loop and the convert loop, so an
    /// interval freed by a window closing this tick is available to a window opening in the SAME
    /// tick. Membership in <see cref="Converted"/> is the liveness test rather than
    /// <see cref="ConvertedPanel.IsAlive"/>, because <c>CanvasConversion.Release</c> deliberately
    /// leaves the panel's target alive (it hands the game window back to its 2D home) — a released
    /// window is one that has left <see cref="Converted"/>, and that list is the authority.</para>
    /// </summary>
    private static void ReleaseFinishedArcSlots()
    {
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            ConvertedPanel? claimed = _arcClaims[i].Panel;
            if (claimed == null || ContainsPanel(claimed))
                continue;
            // Deliberately NOT gated on MapRoomDriver.Active. A claim is released by the window
            // ceasing to float and by nothing else — so leaving and re-entering the room (or the
            // room signal flickering for a frame) can never orphan a reservation that a standing
            // window still occupies, and a window that IS released while the room is gone still
            // frees its interval. Module shutdown clears the whole registry in Detach().
            string name = _arcClaims[i].Name ?? "<window>";
            float centre = _arcClaims[i].CentreDeg;
            float half = _arcClaims[i].HalfWidthDeg;
            int level = _arcClaims[i].OverlapRank;
            _arcClaims[i] = default;
            CountArcClaims(out int clean, out int overlapping);
            VRLog.Info("WorldUI", $"MAP ROOM WINDOW SLOT RELEASED: '{name}' gave up the interval "
                                  + $"{centre:F0}°±{half:F0}° from its spawn gaze (+ = right) at "
                                  + $"depth level {level} — it is no longer floated (the player "
                                  + "closed it, the game released it, or the room ended). "
                                  + $"{clean + overlapping}/{MaxWindowClaims} reservations still "
                                  + $"held ({clean} at depth level 0, {overlapping} one or more "
                                  + $"steps nearer): [{ArcOccupancyText()}]. NOTHING WAS MOVED: "
                                  + "every window still standing keeps the exact pose it claimed at "
                                  + "spawn (user ruling). "
                                  + (level > 0
                                      ? "AND THE WINDOWS IT WAS IN FRONT OF GET THEIR CLICKS BACK "
                                        + "WITH NOTHING LEFT OVER: a window's hit rect lives on its "
                                        + "own host, so it ceases to exist when the host does, and "
                                        + "RayUguiDriver's nearest-plane search simply stops seeing "
                                        + "it. Whatever was behind it is the nearest plane again "
                                        + "from the next ray onward — no re-place, no re-sort, no "
                                        + "state to unwind. "
                                      : "")
                                  + "The freed angle AND the freed depth level go to the next "
                                  + "window that opens — which may be this same window re-opened, "
                                  + "at a different angle, and that is expected.");
        }
    }

    /// <summary>True while <paramref name="panel"/> is still one of the floated windows.</summary>
    private static bool ContainsPanel(ConvertedPanel panel)
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            if (ReferenceEquals(Converted[i].Panel, panel))
                return true;
        }
        return false;
    }

    // ---- what the arc looks like from outside (for the travel-confirm lane) -------------------

    /// <summary>How many windows the room can seat at once (see <see cref="MaxWindowClaims"/>).
    /// NOT a count of fixed angles — since ModBuild 193 a seat is an angular INTERVAL sized from
    /// the window's own measured width, so how many actually fit side by side depends on how wide
    /// they are; this is the registry's capacity and nothing more.</summary>
    internal static int FloatedArcSlotCapacity => MaxWindowClaims;

    /// <summary>How many reservations are held right now, overlapping ones included — i.e. how
    /// many floated map-room windows currently own an angle.</summary>
    internal static int FloatedArcSlotsOccupied
    {
        get
        {
            CountArcClaims(out int clean, out int overlapping);
            return clean + overlapping;
        }
    }

    /// <summary>Corner scratch for <see cref="TryGetFloatedEnsembleBounds"/> (never per frame).</summary>
    private static readonly Vector3[] _ensembleCorners = new Vector3[4];

    /// <summary>
    /// THE WORLD-SPACE EXTENT OF THE FLOATED ENSEMBLE — the union of the host rects of every floated
    /// window that is not a hover card, measured LIVE from the transforms.
    ///
    /// <para>WHY IT IS OFFERED AND HOW IT MUST BE USED. Anything that wants to sit RELATIVE to the
    /// set of open windows (the map room's travel-confirm button) can no longer assume the set is
    /// re-laid-out on a change: a window keeps the pose it claimed at spawn and the player may have
    /// carried it anywhere afterwards, so the ensemble's centre and extent are only knowable by
    /// measuring. This is that measurement, and it is deliberately a POLL rather than an event:
    /// callers re-solve on their own cadence. It allocates nothing and costs one
    /// <c>GetWorldCorners</c> per floated window, so a low cadence (not per frame) is the contract.</para>
    ///
    /// <para>WORLD UNITS, not metres — the hosts are placed in world space and the map room's rig
    /// runs at ~198 world units per real metre. Divide by <see cref="PanelLayout.WorldScale"/>
    /// before comparing anything here against a real-metre tunable.</para>
    /// </summary>
    /// <param name="bounds">World-axis AABB enclosing every floated window.</param>
    /// <param name="windows">How many windows went into it.</param>
    /// <returns>false when nothing is floated (bounds is then meaningless).</returns>
    internal static bool TryGetFloatedEnsembleBounds(out Bounds bounds, out int windows)
    {
        bounds = default;
        windows = 0;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.HoverCard || !wp.Panel.IsAlive)
                continue;
            RectTransform? host = wp.Panel.HostRect;
            if (host == null)
                continue;
            host.GetWorldCorners(_ensembleCorners);
            if (windows == 0)
                bounds = new Bounds(_ensembleCorners[0], Vector3.zero);
            for (int c = 0; c < 4; c++)
                bounds.Encapsulate(_ensembleCorners[c]);
            windows++;
        }
        return windows > 0;
    }

    // (HoverCardHalfHeight lived here until ModBuild 188. It measured the HOST RECT — the window's
    // authored root, which for a hover card is never content-fit — and assumed the drawn card was
    // centred in it, which the game's own follow component was busy making untrue. Both duties moved
    // into MapRoom.HoverCardPose, which measures what is actually drawn and stands that component
    // down; the fallback there is this exact rule, kept for the case where nothing can be measured.)

    // =========================================================================================
    //  ModBuild 231 — WHY A WINDOW THE PLAYER ASKED FOR IS NOT ON THE TABLE.
    //
    //  THE REPORT: "Wenn ich die buttons wiederholt hintereinander drücke tauchen die Fenster
    //  irgendwann gar nicht mehr auf. Das habe ich nun mit dem Händler und der Liste der in
    //  Ruhestand gegangenen Charactere hinbekommen. Das darf niemals passieren."
    //
    //  THE CAUSE, NAMED BY THE LOG IN ITS OWN WORDS. Two lines in the ModBuild 230 session, and
    //  they are the two windows he named:
    //
    //    Player.log:10230  CATCH-ALL FUSE: window 'UI Shop Item Window' re-floated 4× in 60s
    //                      — a cycling HUD banner, not a waiting decision; suppressed for this
    //                      session (manual A/X screen chord still reaches it).
    //    Player.log:11217  CATCH-ALL FUSE: window 'UI Town Records Window' re-floated 4× in 60s …
    //
    //  Every guildmaster destination reaches VR through the CATCH-ALL (their IDs are Shop /
    //  None, none of them is in FallbackIds), so every open of the merchant is one tick of
    //  ChurnMaxFloats. The fuse's own premise is written at its declaration: "a window name
    //  floating more than this often within ChurnWindowSeconds is NOT a stuck decision —
    //  decisions open once and wait. It is a self-cycling HUD banner." A destination the PLAYER
    //  opens and closes four times in a minute is neither a stuck decision nor a self-cycling
    //  banner, and the fuse cannot tell the difference because it counts FLOATS, not causes.
    //  ModBuild 184 already met this exact shape once — the map's quest-preview hover card
    //  floats once per hover, which IS its life cycle — and answered it by exempting hover cards
    //  from the count. This is the same class of mistake one family further on, and the same
    //  answer: A FUSE MUST NOT COUNT A CLASS WHOSE NORMAL LIFE LOOKS LIKE THE ABUSE.
    //
    //  WHY THE SUPPRESSION IS LIFTED RATHER THAN THE COUNT EXEMPTED. The fuse is real insurance
    //  and it is not being removed: the loop it was capping (ModBuild 185/186 — float, release,
    //  float, release at one full conversion each) is a genuine hazard, and if it ever returns
    //  the fuse must still blow inside ONE open. So the count keeps running for destinations
    //  too; what changes is that the SESSION-LONG verdict is lifted again the moment a
    //  destination the player can still reach is standing open and un-floated. A loop would
    //  re-blow it within the same second and the log would say so four times over — which is
    //  strictly MORE information than one line and then silence forever. What can no longer
    //  happen is the permanent, unrecoverable wedge he reported: "das darf niemals passieren".
    //
    //  WHY IT LIVES HERE AND NOT AT THE FUSE. ModalFallback.10.CatchAll.cs is another lane's
    //  file this build. This is the same partial class, so the state is directly reachable; the
    //  sweep is placed in Tick immediately BEFORE TickCatchAll so a suppression lifted this tick
    //  is already gone when the catch-all reads it, i.e. the window floats on the SAME tick and
    //  the player never sees a missing frame. The equivalent one-line change at the fuse itself
    //  is written out in this lane's report so the owner can take it instead.
    // =========================================================================================

    /// <summary>Per-window-name latch for the lift line — one per window type, like every other
    /// catch-all latch (the sweep is level-triggered and would otherwise print per tick).</summary>
    private static readonly HashSet<string> ChurnLiftLogged = new();

    /// <summary>
    /// THE LIFT'S OWN FUSE. A lift that can happen without limit would turn a genuine re-float LOOP
    /// into blow → lift → blow → lift forever, at one un-latched <c>CATCH-ALL FUSE</c> Warn per
    /// blow — i.e. it would remove the cap and hide the loop underneath it, which is the exact
    /// mistake ModBuild 184 made when it exempted hover cards from the count outright (the ModBuild
    /// 185 log then had 1417 float/release lines).
    ///
    /// <para>The two rates are far apart and that is what makes the test decide something. One lift
    /// costs the player more than <see cref="ChurnMaxFloats"/> deliberate open/close cycles of the
    /// same destination; three of them inside <see cref="ChurnLiftBurstSeconds"/> would be twelve
    /// cycles in ten seconds, which no hand does. A float/release loop reaches it in well under a
    /// second. So: lift freely at human speed, stand down at machine speed and say so once.</para>
    /// </summary>
    private const int ChurnLiftMaxPerBurst = 3;
    private const float ChurnLiftBurstSeconds = 10f;

    /// <summary>Lifts per window NAME within the rolling burst window.</summary>
    private static readonly Dictionary<string, (int Count, float WindowStart)> ChurnLifts = new();

    /// <summary>Window names whose lift budget blew — the fuse stands for the rest of the session.</summary>
    private static readonly HashSet<string> ChurnLiftGivenUp = new();

    /// <summary>
    /// Lift the churn fuse's session verdict off any GUILDMASTER DESTINATION window that is open
    /// right now. Runs from <see cref="Tick"/> immediately before <c>TickCatchAll</c>.
    ///
    /// <para>COST: one <c>Count</c> compare in the overwhelmingly normal case. The set is empty
    /// unless a fuse has blown at all, and only then does it walk <see cref="UnknownShown"/> — the
    /// windows the catch-all is currently tracking, which in the map room is a handful.</para>
    ///
    /// <para>SCOPE IS THE DESTINATION FAMILY AND NOTHING ELSE.
    /// <c>GuildmasterDestinations.IsDestination</c> is a component test on the window's OWN
    /// GameObject (IS-A, not related-to — this repo has shipped the containment version of that
    /// test twice and had to correct it both times), so a genuinely cycling HUD banner that trips
    /// the fuse stays suppressed exactly as it does today.</para>
    /// </summary>
    private static void LiftChurnFuseForDestinations()
    {
        if (ChurnSuppressed.Count == 0)
            return;
        UnknownScratch.Clear();
        foreach (KeyValuePair<UIWindow, int> kv in UnknownShown)
        {
            UIWindow window = kv.Key;
            if (window == null || !window.IsOpen)
                continue;
            if (!ChurnSuppressed.Contains(window.name) || ChurnLiftGivenUp.Contains(window.name))
                continue;
            if (!MapRoom.GuildmasterDestinations.IsDestination(window))
                continue;
            UnknownScratch.Add(window);
        }
        for (int i = 0; i < UnknownScratch.Count; i++)
        {
            UIWindow window = UnknownScratch[i];

            // THE LIFT'S OWN FUSE — see ChurnLiftMaxPerBurst. Counted BEFORE the lift, so the
            // budget bounds the number of lifts and not the number of attempts.
            float nowT = Time.unscaledTime;
            if (!ChurnLifts.TryGetValue(window.name, out (int Count, float WindowStart) lifts)
                || nowT - lifts.WindowStart > ChurnLiftBurstSeconds)
                lifts = (0, nowT);
            lifts.Count++;
            ChurnLifts[window.name] = lifts;
            if (lifts.Count > ChurnLiftMaxPerBurst)
            {
                ChurnLiftGivenUp.Add(window.name);
                VRLog.Warn("WorldUI", $"CATCH-ALL FUSE STANDS for '{window.name}' (ID {window.ID}): "
                                      + $"its suppression has been lifted {lifts.Count - 1}× inside "
                                      + $"{ChurnLiftBurstSeconds:0}s and it blew again every time. "
                                      + "That is NOT a player toggling a table cap — one lift already "
                                      + $"costs more than {ChurnMaxFloats} deliberate open/close "
                                      + "cycles — it is a genuine re-float LOOP (the ModBuild 185/186 "
                                      + "class: the window is floated, becomes ineligible BECAUSE it "
                                      + "is floated, is not re-added, and the release loop drops it, "
                                      + "at one full conversion each). The fuse is left standing for "
                                      + "the rest of the session, which is the behaviour it had "
                                      + "before ModBuild 231, and the lines above this one are the "
                                      + "evidence for the loop. Fix the loop; do not raise this "
                                      + "budget.");
                continue;
            }

            ChurnSuppressed.Remove(window.name);
            FloatChurn.Remove(window.name);
            if (!ChurnLiftLogged.Add(window.name))
                continue;
            VRLog.Warn("WorldUI", $"CATCH-ALL FUSE LIFTED for '{window.name}' (ID {window.ID}) — it "
                                  + "is a GUILDMASTER DESTINATION, i.e. a window the player opens and "
                                  + "closes on purpose from a table cap, and the fuse counts FLOATS "
                                  + "rather than causes. Four open/close cycles inside "
                                  + $"{ChurnWindowSeconds:0}s look exactly like the self-cycling HUD "
                                  + "banner the fuse exists for, and the ModBuild 230 log has the "
                                  + "result twice: 'UI Shop Item Window' and 'UI Town Records Window' "
                                  + "were suppressed FOR THE WHOLE SESSION, after which every press "
                                  + "opened the game window and floated nothing ('IsOpen=True "
                                  + "float=none') — the user's report, verbatim: 'tauchen die Fenster "
                                  + "irgendwann gar nicht mehr auf … das darf niemals passieren'. The "
                                  + "count itself is NOT disabled: if the ModBuild 185/186 "
                                  + "float/release loop ever returns it still blows inside one open, "
                                  + "and it will simply be lifted and re-blown, which is more "
                                  + "information than one line and then silence. This line appears "
                                  + "ONCE per window type. AND THE LIFT HAS ITS OWN FUSE: more than "
                                  + $"{ChurnLiftMaxPerBurst} lifts inside {ChurnLiftBurstSeconds:0}s "
                                  + "means a re-float LOOP rather than a player, and the suppression "
                                  + "is then left standing — look for 'CATCH-ALL FUSE STANDS'.");
        }
        UnknownScratch.Clear();
    }

    /// <summary>
    /// THE CHURN FUSE'S STATE FOR ONE WINDOW, in the words a press line can carry. Pure read.
    /// </summary>
    internal static string ChurnStateFor(UIWindow? window)
    {
        if (window == null)
            return "fuse=<no window>";
        if (ChurnSuppressed.Contains(window.name))
            return "fuse=BLOWN (session-suppressed — this window cannot float)";
        return FloatChurn.TryGetValue(window.name, out (int Count, float WindowStart) churn)
            ? $"fuse=ok ({churn.Count}/{ChurnMaxFloats} floats in the last "
              + $"{Time.unscaledTime - churn.WindowStart:F0}s of a {ChurnWindowSeconds:0}s window)"
            : $"fuse=ok (0/{ChurnMaxFloats} floats counted)";
    }

    /// <summary>
    /// IS THIS EXACT WINDOW A LIVE FLOAT RIGHT NOW? Object-keyed, so no ID can shadow it.
    ///
    /// <para><see cref="FloatedWindowWithId"/> answers a different question and CANNOT answer this
    /// one for the destination family. It walks the converted set newest-first and returns the first
    /// panel whose window carries the asked-for ID — and <c>UITownRecordsWindow</c>,
    /// <c>UITempleWindow</c> and the map room's permanent 'Quest Log Manager' all carry
    /// <c>UIWindowID.None</c>. In the ModBuild 230 session the quest log was floated from 3599 to
    /// 17019, i.e. for the whole run, so <c>FloatedWindowWithId(None)</c> returned the QUEST LOG
    /// every single time the temple or the records window asked — five presses closed correctly and
    /// were nevertheless reported as "float=STANDS … id-shadowed", which that instrument's own text
    /// names as the FAILURE reading (Player.log:7213, 7588, 11074, 11135, 11199; each is followed
    /// four lines later by its MAP ROOM WINDOW SLOT RELEASED).</para>
    ///
    /// <para>The exclusions are <see cref="FloatedWindowWithId"/>'s, verbatim: a window the user is
    /// closing, a dead panel or a panel with no host is not something he can look at.</para>
    /// </summary>
    internal static bool FloatIsLive(UIWindow? window)
    {
        if (window == null)
            return false;
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            if (wp.UserClosing || wp.Window == null || !wp.Panel.IsAlive || wp.Panel.HostGo == null)
                continue;
            if (ReferenceEquals(wp.Window, window))
                return true;
        }
        return false;
    }

    /// <summary>
    /// THE ARC REGISTRY IN ONE LINE — how many reservations are held, how many are free, and by
    /// which windows. Offered so the map room's press instrument can print the occupancy at the
    /// moment of a press without a second capture (the RELEASED line already carries it).
    /// </summary>
    internal static string ArcOccupancyLine()
    {
        CountArcClaims(out int clean, out int overlapping);
        int held = clean + overlapping;
        return $"arcSlots={held}/{MaxWindowClaims} held ({clean} clear, {overlapping} overlapping), "
               + $"{MaxWindowClaims - held} free: [{ArcOccupancyText()}]";
    }

    /// <summary>
    /// WHY IS THIS WINDOW NOT A FLOATED PANEL, AND WHICH GATE SAID SO — the first refusal, named.
    ///
    /// <para>A wedge that is invisible until the player notices it is the thing being removed here.
    /// Every gate below is one of the tests <c>TickCatchAll</c> / <c>CatchAllEligible</c> actually
    /// runs, in the order they run, so the answer is the real reason and not a plausible one. The
    /// walk is deliberately READ-ONLY — <c>CatchAllEligible</c> itself mutates (it clears an expired
    /// empty-window refusal), so it is re-stated here rather than called; a diagnostic must not
    /// change the state it reports on.</para>
    ///
    /// <para>COST: called ONCE, from the map room's open watch, when a press decided to open a
    /// window and no float appeared within the watch's frame budget. Never per frame, never per
    /// press that worked.</para>
    /// </summary>
    internal static string ExplainNotFloated(UIWindow? window)
    {
        if (window == null)
            return "the window reference is gone (destroyed or never resolved) — nothing could float";
        if (FloatIsLive(window))
            return "it IS a live float right now (the watch fired late; nothing refused it)";
        if (!window.IsOpen)
            return "the GAME's own UIWindow.IsOpen is FALSE — the window never opened, so no float "
                   + "was ever refused. Look UP the log at the dispatch: the press either did not "
                   + "reach the bar's Toggle, or the game's own mode machine declined it";
        if (!WorldUIConfig.ConversionActive)
            return "[WorldUI] conversion is switched OFF in the config — nothing floats at all";
        if (IsFloatedByUs(window))
            return "the mod HAS a panel for it, but that panel is flagged UserClosing or its host is "
                   + "already gone — a close and an open crossed inside one tick";
        if (ChurnSuppressed.Contains(window.name))
            return $"THE CATCH-ALL CHURN FUSE ('{window.name}' is in ChurnSuppressed). It blew after "
                   + $"more than {ChurnMaxFloats} floats inside {ChurnWindowSeconds:0}s and the "
                   + "verdict is session-long: the window opens on the hidden 2D stack and is shown "
                   + "NOWHERE. This is the ModBuild 231 report. "
                   + (ChurnLiftGivenUp.Contains(window.name)
                       ? "THE LIFT DELIBERATELY STOOD DOWN for this window — see 'CATCH-ALL FUSE "
                         + "STANDS' above: the suppression was lifted and re-blown faster than a "
                         + "human can press, which means a real re-float loop. The loop is the bug, "
                         + "not the fuse"
                       : "LiftChurnFuseForDestinations should have lifted it one tick before the "
                         + "catch-all read it, so if this string appears the sweep did not run, or it "
                         + "did not recognise this window as a destination (IsDestination is a "
                         + "component test on the window's OWN GameObject), or the window was not in "
                         + "UnknownShown at the time");
        // ModBuild 232: the refusal table is asked FIRST here for the same reason the catch-all asks
        // it first — without this arm a table refusal falls through to the "EVERY GATE PASSES … this
        // is a NEW failure" verdict at the bottom, which would send a later round hunting a defect
        // that is a deliberate rule.
        if (FloatRefusalTable.Refuses(window))
            return FloatRefusalTable.Describe(window)!;
        if (EmptyRefusedNow(window))
            return "the EMPTY-WINDOW refusal (ModBuild 226) holds it: it floated once, reached its "
                   + "reveal edge with nothing drawn under it, and may not float again until the "
                   + "game has closed and re-opened it";
        if (EmptyHeldNow(window))
            return "the LIVENESS hold (ModBuild 230) holds it: it floated, drew nothing for the whole "
                   + "dwell and was released for it";
        if (IsKnownHudWindow(window))
            return "it matches a KNOWN FLAT-HUD OWNER (CardsHandManager / CombatLogHandler / "
                   + "UINotificationManager / PhaseBannerHandler / UIGuildmasterHUD on its own "
                   + "GameObject) — the mod owns that subsystem elsewhere and must not float it";
        if (IsPollWindow(window))
            return "one of the three EXPLICIT POLLS owns this instance (story box, level-message "
                   + "group, dialogPopup) — it joins through its poll, not through the catch-all";
        if (DecisionDock.ClaimsWindow(window))
            return "the DECISION DOCK claims it — its option row docks below the cards instead";
        if (IsCardsOwnedItemPickerWindow(window))
            return "the CARDS item flow owns the picker window while a surrender/refresh/lose pick "
                   + "is live — the item fan is the VR affordance for it";
        if (IsAdoptedByConversion(window))
            return "its subtree is ADOPTED by another live conversion — some surface already "
                   + "physicalizes it this instant";
        if (!NonBlockingMenus.Contains(window.ID) && !MultiplayerRosterMenus.Contains(window.ID)
            && MapRoom.MapRoomDriver.Active && HasOpenAncestorWindow(window))
            return "THE PARENT WINS (ModBuild 181/184/188): an ancestor UIWindow will be floated and "
                   + "renders this subtree inside its own host. Look for the 'MAP ROOM: window … is "
                   + "NOT floated on its own' line for the ancestor's name";
        if (RendersInsideFloatedAncestor(window))
            return "it RENDERS INSIDE A LIVE FLOATED ANCESTOR (or a declared sibling group) — "
                   + "floating it again would be a duplicate, not a window";
        var rect = window.transform as RectTransform;
        if (rect == null)
            return "its transform is not a RectTransform — the generic float is a screen-space→world "
                   + "conversion and has nothing to convert";
        Canvas? canvas = window.GetComponentInParent<Canvas>();
        if (canvas == null)
            return "there is no Canvas above it at all";
        if (canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
            return "its root canvas is ALREADY WorldSpace — it is world-space UI and is visible in VR "
                   + "without a conversion";
        if (IsFallbackWindow(window.ID))
            return $"its ID ({window.ID}) is on the ENROLLED path (FallbackIds), so the catch-all "
                   + "never tracks it — if it is not floated, the enrolled poll/convert loop is what "
                   + "declined it, not any gate above";
        if (!UnknownShown.ContainsKey(window))
            return "the catch-all is NOT TRACKING it: no Show transition for this instance reached "
                   + "CatchAllObserve. Either the window was shown before the module attached, or "
                   + "UIWindow_Transition_Patch never saw it";
        return "EVERY GATE PASSES — the catch-all should append it and the convert loop should float "
               + "it within a tick or two. Nothing known refused it, so this is a NEW failure and the "
               + "MODAL/CONVERSION lines around this one are the evidence, not this class";
    }

    private static bool IsFallbackWindow(UIWindowID id)
    {
        // ConfirmationBox is normally physicalized by DialogSurface — it needs the
        // fallback only when that surface is switched off.
        if (id == UIWindowID.ConfirmationBox)
            return !WorldUIConfig.Dialogs.Value;
        return FallbackIds.Contains(id);
    }

    // =====================================================================================
    //  SUB-STEP ATTRIBUTION FOR THIS TICK (ModBuild 195)
    //
    //  WHY IT EXISTS. The ModBuild 194 hardware log says this, over and over, for 45,000
    //  consecutive frames:
    //
    //    [Perf] STEPS 30.0s … ModalFallback 12.991ms avg, worst 21.02ms, 647.9ms/s, frames 1497
    //    [Perf] FRAME 30.0s n=1497 … frametime mean 20.05 … over-budget 1497/1497 (100.0%)
    //                                | mod 15.20ms/frame avg (75.8% of frame time)
    //
    //  ONE step is eating the whole 11.11 ms budget, EVERY frame, and the report can only name
    //  the step — which is this entire file plus everything it calls. That is not actionable:
    //  it is 30 named things behind one number. The [Perf] SPLIT line already proves the cost is
    //  real main-thread work and not a GPU wait ("logic (Update→LateUpdate) 16.61ms (83%) |
    //  blocked (waiting on GPU/compositor) 1.92ms (10%)"), so the only thing missing is WHICH
    //  part. This is that measurement, and it is deliberately shipped even though it does not by
    //  itself make a single frame faster.
    //
    //  HOW IT WORKS, AND WHY IT IS A CURSOR RATHER THAN A using-BLOCK. Every phase boundary in
    //  Tick calls EnterPhase(slot); the call CLOSES whatever phase was open and OPENS the new
    //  one, so the body of Tick keeps its original shape, its original indentation and its
    //  original comments — a reviewer diffing this against ModBuild 194 sees one added line per
    //  boundary and nothing else. It also survives a throw: a phase left open by an exception is
    //  DISCARDED at the top of the next Tick (never recorded, so a fault cannot invent a 16 ms
    //  phase), and PerfMonitor zeroes its own nesting depth on every frame roll.
    //
    //  TWO CONSUMERS, ONE MEASUREMENT.
    //    * PerfMonitor gets each phase as a NESTED named step, so the existing [Perf] STEPS
    //      ranking and — more importantly — the per-frame [Perf] SPIKE line's "worst steps:"
    //      list will now print e.g. "ModalFallback 12.9ms, ModalFallback.Probe.RenderTarget
    //      11.4ms" and name the culprit on the very frame it happened. Nested steps are
    //      attributed individually but counted ONCE in the mod total (PerfMonitor's depth
    //      counter), so the mod share cannot exceed 100 % because of this.
    //    * THIS class keeps its own accumulators as well, and prints ONE line every
    //      TickBreakdownSeconds that ranks EVERY phase — because [Perf] STEPS only prints the
    //      top [Perf] TopSteps entries, and a phase that is cheap is exactly the thing a
    //      breakdown has to be able to state rather than omit. Grep MODAL TICK BREAKDOWN.
    //
    //  COST OF THE INSTRUMENT ITSELF: two Stopwatch.GetTimestamp() reads per boundary (~20 ns
    //  each on Windows) — 19 phases is under 1 µs against an 11.11 ms budget — plus one Info
    //  line every 30 s. It allocates nothing per frame; the breakdown line's StringBuilder is
    //  static and reused. It is unconditional on purpose: a diagnostic that has to be switched
    //  on is a diagnostic that is off in the log you actually receive.
    // =====================================================================================

    /// <summary>Phase slots. The order is the order they run in, which is also the order the
    /// breakdown line prints them in when their costs tie.</summary>
    private const int PhasePreConvertHide = 0;
    private const int PhasePolls = 1;
    private const int PhaseCatchAll = 2;
    private const int PhaseErrorBox = 3;
    private const int PhaseDecide = 4;
    private const int PhaseRelease = 5;
    private const int PhaseConvert = 6;
    private const int PhaseRaycast = 7;
    private const int PhaseGrabFollow = 8;
    private const int PhaseDestinations = 9;
    private const int PhaseProbeFlicker = 10;
    private const int PhaseProbeCameraOrder = 11;
    private const int PhaseProbeRenderTarget = 12;
    private const int PhaseScroll = 13;
    private const int PhaseRefit = 14;
    private const int PhaseChainPose = 15;
    private const int PhaseMenuGuard = 16;
    private const int PhaseEscape = 17;
    private const int PhasePublish = 18;

    /// <summary>
    /// The step name each phase is reported under. They are all prefixed <c>ModalFallback.</c>
    /// so ONE grep — <c>grep 'ModalFallback\.'</c> — over a [Perf] STEPS or [Perf] SPIKE line
    /// answers "which part of ModalFallback is slow", and the parent step keeps its own name so
    /// nothing that already greps for <c>ModalFallback</c> stops matching.
    ///
    /// <para>WHAT EACH ONE COVERS (so a number can be acted on without reading the method):
    /// <c>PreConvertHide</c> the round-8 2D blackout bookkeeping; <c>Polls</c> the open-window
    /// prune plus the three ID-less deadlock polls and the rebuild of the open set;
    /// <c>CatchAll</c> part 10's unknown-window enrollment (ModalFallback.10.CatchAll.cs);
    /// <c>ErrorBox</c> the GlobalErrorMessage poll and float; <c>Decide</c> the sticky/blocking
    /// scans that produce want/wantLock PLUS part 12's presentation step (the flat-screen canvas
    /// bind and the fail-open stranded set); <c>Release</c> the release loop, the arc-slot release
    /// and the Failed prune; <c>Convert</c> TryConvertWindow for newly opened windows;
    /// <c>Raycast</c> the raycaster and sticky-visibility re-asserts; <c>GrabFollow</c> the grab
    /// frame follow and the hover-card pose; <c>Destinations</c> the guildmaster banner
    /// reconcile; the three <c>Probe.*</c> phases the ModBuild 182/185/186 flicker instruments
    /// (Probe.RenderTarget additionally drives PanelSamplingProbe → the eye-pixel probe → PanelMipBake,
    /// so a large number there is a probe-family number, not one class); <c>Scroll</c> the
    /// results-window stick scroll; <c>Refit</c> the fitted-scale re-derivation and the one
    /// pre-reveal pose re-place; <c>ChainPose</c> the level-message chain store; <c>MenuGuard</c>
    /// the ESC-tab highlights and the InControl hover-focus guard; <c>Escape</c> the modal escape
    /// chord; <c>Publish</c> the lock/screen policy tail.</para>
    /// </summary>
    private static readonly string[] TickPhaseNames =
    {
        "ModalFallback.PreConvertHide",
        "ModalFallback.Polls",
        "ModalFallback.CatchAll",
        "ModalFallback.ErrorBox",
        "ModalFallback.Decide",
        "ModalFallback.Release",
        "ModalFallback.Convert",
        "ModalFallback.Raycast",
        "ModalFallback.GrabFollow",
        "ModalFallback.Destinations",
        "ModalFallback.Probe.Flicker",
        "ModalFallback.Probe.CameraOrder",
        "ModalFallback.Probe.RenderTarget",
        "ModalFallback.Scroll",
        "ModalFallback.Refit",
        "ModalFallback.ChainPose",
        "ModalFallback.MenuGuard",
        "ModalFallback.Escape",
        "ModalFallback.Publish",
    };

    /// <summary>Stopwatch ticks accumulated per phase since the last breakdown line.</summary>
    private static readonly long[] TickPhaseTicks = new long[19];

    /// <summary>Worst SINGLE frame per phase since the last breakdown line — the number that
    /// separates a steady floor from a periodic burst, which is the first thing anyone reading a
    /// stutter report needs to know.</summary>
    private static readonly long[] TickPhaseWorst = new long[19];

    /// <summary>Open phase, or −1. See the cursor discussion in the block above.</summary>
    private static int _tickPhase = -1;
    private static long _tickPhaseBegin;
    private static long _tickPhasePerfBegin;

    private static long _tickTotalTicks;
    private static long _tickWorstTotalTicks;
    private static int _tickFrames;
    private static float _nextTickBreakdown;

    /// <summary>Seconds between MODAL TICK BREAKDOWN lines. 30 s ON PURPOSE: it is the same
    /// window [Perf] FRAME / [Perf] STEPS use, so the two can be read side by side in the log
    /// without correcting for different averaging periods.</summary>
    private const float TickBreakdownSeconds = 30f;

    /// <summary>Line builder for the breakdown (static, reused — the instrument must not become
    /// the allocation it is hunting).</summary>
    private static readonly System.Text.StringBuilder TickBreakdownSb = new(768);

    /// <summary>Phase ranking scratch (indices into <see cref="TickPhaseNames"/>, reused).</summary>
    private static readonly int[] TickPhaseRank = new int[19];

    /// <summary>Close the open phase (if any) and open <paramref name="slot"/>.</summary>
    private static void EnterPhase(int slot)
    {
        EndPhase();
        _tickPhase = slot;
        _tickPhaseBegin = System.Diagnostics.Stopwatch.GetTimestamp();
        _tickPhasePerfBegin = PerfMonitor.BeginStep();
    }

    /// <summary>Close the open phase (if any) and fold its duration into both consumers.</summary>
    private static void EndPhase()
    {
        int slot = _tickPhase;
        if (slot < 0)
            return;
        _tickPhase = -1;
        long dt = System.Diagnostics.Stopwatch.GetTimestamp() - _tickPhaseBegin;
        if (dt < 0L)
            dt = 0L;
        TickPhaseTicks[slot] += dt;
        if (dt > TickPhaseWorst[slot])
            TickPhaseWorst[slot] = dt;
        PerfMonitor.EndStep(TickPhaseNames[slot], _tickPhasePerfBegin);
    }

    /// <summary>Drop every accumulator (module teardown, and after each breakdown line).</summary>
    private static void ResetTickBreakdown()
    {
        for (int i = 0; i < TickPhaseTicks.Length; i++)
        {
            TickPhaseTicks[i] = 0L;
            TickPhaseWorst[i] = 0L;
        }
        _tickTotalTicks = 0L;
        _tickWorstTotalTicks = 0L;
        _tickFrames = 0;
    }

    /// <summary>
    /// ONE LINE THAT ANSWERS "WHICH PART OF ModalFallback COSTS THE 11 ms" — every phase, ranked,
    /// with its average per frame, its share of the step and its worst single frame, plus the
    /// room state the numbers were measured in. Emitted every <see cref="TickBreakdownSeconds"/>.
    /// </summary>
    private static void LogTickBreakdown()
    {
        try
        {
            int frames = _tickFrames;
            if (frames <= 0)
                return;
            double freq = System.Diagnostics.Stopwatch.Frequency;
            double totalMs = _tickTotalTicks * 1000d / freq / frames;

            int n = 0;
            for (int i = 0; i < TickPhaseNames.Length; i++)
                TickPhaseRank[n++] = i;
            // Insertion sort by window total, descending. 19 entries, once every 30 s.
            for (int i = 1; i < n; i++)
            {
                int key = TickPhaseRank[i];
                int j = i - 1;
                while (j >= 0 && TickPhaseTicks[TickPhaseRank[j]] < TickPhaseTicks[key])
                {
                    TickPhaseRank[j + 1] = TickPhaseRank[j];
                    j--;
                }
                TickPhaseRank[j + 1] = key;
            }

            System.Text.StringBuilder sb = TickBreakdownSb;
            sb.Length = 0;
            sb.Append("MODAL TICK BREAKDOWN over ").Append(frames).Append(" frame(s): the whole "
                      + "ModalFallback step cost ").Append(totalMs.ToString("F3"))
              .Append("ms/frame avg, worst ")
              .Append((_tickWorstTotalTicks * 1000d / freq).ToString("F2"))
              .Append("ms. Ranked by total time:");
            for (int r = 0; r < n; r++)
            {
                int slot = TickPhaseRank[r];
                double avgMs = TickPhaseTicks[slot] * 1000d / freq / frames;
                double share = totalMs > 1e-9d ? avgMs / totalMs * 100d : 0d;
                sb.Append(r == 0 ? " " : " | ")
                  .Append(TickPhaseNames[slot]).Append(' ')
                  .Append(avgMs.ToString("F3")).Append("ms (")
                  .Append(share.ToString("F0")).Append("%), worst ")
                  .Append((TickPhaseWorst[slot] * 1000d / freq).ToString("F2")).Append("ms");
            }
            sb.Append(" | state: ").Append(Converted.Count).Append(" floated window(s) [")
              .Append(FloatedWindowNames()).Append("], ").Append(Open.Count)
              .Append(" tracked open, ").Append(OpenWindows.Count).Append(" presented, map room ")
              .Append(MapRoom.MapRoomDriver.Active ? "ACTIVE" : "down")
              .Append(". HOW TO READ THIS LINE. The frame budget at 90 Hz is 11.11 ms for "
                      + "EVERYTHING, mod and game together, so any phase here above ~1 ms is "
                      + "already a large fraction of it. Compare the phase avg against its own "
                      + "WORST: avg ≈ worst is a STEADY per-frame cost (an unbounded sweep, a "
                      + "per-frame engine call) and it is fixed by making the work happen on an "
                      + "edge or by bounding it; avg far below worst is a PERIODIC burst (a "
                      + "cadence-driven rescan) and it is fixed by spreading the cadence, not by "
                      + "deleting it. 'floated window(s)' is the multiplier for every phase that "
                      + "loops over the float set — if a phase grows with that count it scales "
                      + "per window, and if it does not, it is a fixed cost that arms with the "
                      + "FIRST window. Probe.* phases are the ModBuild 182/185/186 flicker "
                      + "instruments plus (under Probe.RenderTarget) the sampling/eye/mip-bake "
                      + "family: those are DIAGNOSTICS, so a large number there is pure overhead "
                      + "on a question that may already be answered — check their own BASELINE "
                      + "lines for whether they have found anything before paying for them. The "
                      + "same numbers appear per-frame on [Perf] SPIKE, so a single blown frame "
                      + "can be attributed without waiting 30 s for this line.");
            VRLog.Info("WorldUI", sb.ToString());
        }
        catch (System.Exception ex)
        {
            // House rule: an instrument may never be the thing that starves VR input.
            VRLog.Warn("WorldUI", $"MODAL TICK BREAKDOWN could not be composed "
                                  + $"({ex.GetType().Name}: {ex.Message}) — the per-phase numbers "
                                  + "are still on the [Perf] STEPS and [Perf] SPIKE lines under "
                                  + "their 'ModalFallback.' names; only this summary is missing.");
        }
    }

    /// <summary>The floated windows' names for the breakdown line's state clause (allocates once
    /// every 30 s, never per frame).</summary>
    private static string FloatedWindowNames()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Converted.Count; i++)
        {
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(Converted[i].Window != null ? Converted[i].Window!.name : "<menu>");
        }
        return sb.Length == 0 ? "none" : sb.ToString();
    }

    /// <summary>
    /// Per-frame service (WorldUI driver): prune dead/closed windows, poll the ID-less
    /// deadlockers, maintain the window conversions, drive the aux-modal mode input.
    /// Allocation-free steady state (WindowPanel records allocate only when a window
    /// is first converted — a rare event).
    ///
    /// <para>Every phase boundary below opens a measured sub-step — see the SUB-STEP ATTRIBUTION
    /// block above for what each name covers and why the cost had to be split.</para>
    /// </summary>
    internal static void Tick()
    {
        // Discard a phase left open by an exception in the PREVIOUS frame rather than closing it
        // now: closing it would charge one whole frame's wall time to whichever phase threw, and
        // an instrument that invents a 16 ms phase after a fault is worse than one that drops a
        // frame. PerfMonitor's own nesting depth is zeroed on its frame roll, so nothing leaks
        // past this point either.
        _tickPhase = -1;
        long tickBegin = System.Diagnostics.Stopwatch.GetTimestamp();
        EnterPhase(PhasePreConvertHide);
        // Round 8, FIRST — before any step that could throw: end every pre-convert 2D blackout
        // whose window will not be floated after all, and enforce the frame budget (part 11).
        // A window the mod switched off must never outlive the reason it was switched off.
        TickPreConvertHide();

        // "IS THERE A ROOM TO FLOAT A WINDOW IN", not "is a scenario running" (ModBuild 178). The
        // 3D map room is a room: the flat screen is deliberately OFF there (FlatScreen.ScreenWanted
        // returns false on MapRoomDriver.Active, so the room and the flat map render never both own
        // the parchment) — so a window that is not floated here is not shown on a screen instead,
        // it is shown NOWHERE. That was the whole of the 177 report: the pause menu opened, logged
        // "opened without a VR conversion (scenario=False)", and the player saw nothing at all.
        // Every downstream use of this local — the catch-all, the reward showcase, the chain-pose
        // reset — is about PRESENCE, which is what this predicate names.
        bool inScenario = VRModeStateMachine.TableInFrontOfPlayer;
        EnterPhase(PhasePolls);

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
            // ModBuild 196 (the equipment tab): an enrolled window that is nested inside a window
            // this class has ALREADY floated is a SUB-VIEW of that screen, not a window of its
            // own — it is drawn and hit-tested inside its host where the game lays it out. Same
            // "parent wins" rule the catch-all applies, same level-triggered shape (it stays in
            // Open, so it floats by itself the moment its host stops being one). See
            // RendersInsideFloatedAncestor for the hierarchy evidence and the map-room scope.
            if (RendersInsideFloatedAncestor(window))
                continue;
            // ModBuild 232: the refusal table governs the ENROLLED path too, so a row cannot mean one
            // thing for a catch-all window and another for an enrolled one. Inert as it stands —
            // neither of today's two rows has an enrolled id.
            if (FloatRefusalTable.Refuses(window))
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

        EnterPhase(PhaseCatchAll);
        // ModBuild 231 — BEFORE the catch-all reads its own fuse, lift the session verdict off any
        // guildmaster destination that is standing open. Ordering is the whole point: a suppression
        // lifted here is already gone when TickCatchAll asks, so the window floats on THIS tick and
        // the player never sees a frame without it. See LiftChurnFuseForDestinations for the two log
        // lines that made this the ModBuild 231 report.
        LiftChurnFuseForDestinations();
        // Part 10: the reward-showcase poll (enrollment #2 — the chest showcase window's ID
        // is scene-serialized and unprovable, see the part-10 verification comment) and the
        // CATCH-ALL — unknown scenario windows join OpenWindows after a short grace so an
        // un-enrolled window can never again wait invisibly on the hidden 2D stack. Runs
        // AFTER the explicit polls so their dedupe/claim handling always wins.
        TickCatchAll(inScenario);

        EnterPhase(PhaseErrorBox);
        // Part 10: GlobalErrorMessage (enrollment #1) — NOT a UIWindow (SetActive-shown), so
        // neither the transition patch nor the catch-all above can see it; dedicated poll +
        // direct float. Feeds the lock below via ErrorModalOpen (a genuine blocker: the whole
        // game halts on ShowingMessage) and the screen policy via ErrorScreenWanted.
        TickErrorMessage(inScenario);

        EnterPhase(PhaseDecide);
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
        bool convertWanted = want && ConvertBaseActive;

        // ModBuild 198 (part 12) — WHERE IS EACH OPEN WINDOW ACTUALLY BEING SHOWN. Outside a room
        // this class hands its windows to the flat screen, which composites the game's CAMERAS;
        // a window whose root canvas is Screen-Space-OVERLAY is rendered by no camera at all, so
        // that hand-off showed it NOWHERE — the map ESC menu, opened eleven times in the 197 log
        // and never seen. This step binds such a canvas onto the UI camera the screen is
        // capturing, and when it cannot, marks the window STRANDED so the loops below float it
        // anyway (fail open). It also prints the verdict line the 197 log did not have. Placed
        // here because it needs the finished OpenWindows and must run before the release/convert
        // loops read StrandedFloat; measured inside the Decide phase, whose scans it joins.
        TickScreenBind(floatPathOwnsWindows: convertWanted);

        EnterPhase(PhaseRelease);
        // 0. ModBuild 230 — THE LIVENESS RULE, user: "Es darf niemals leere Fenster geben -
        //    verschwindet das Objekt das in dem Fenster dargestellt wird, soll auch das Fenster
        //    verschwinden." Judge every floated window's CONTENT before the release loop runs, so a
        //    window found dead this tick leaves through that one existing teardown with its whole
        //    chrome — grab holder, X, collider, arc slot — instead of through a second, partial
        //    path. It marks and never releases; the loop below is still the only releaser. The
        //    whole rule, its two failure shapes and what the ModBuild 226 reveal-edge test could and
        //    could not see live in ModalFallback.9.Spawn.cs. Its self-cost is inside this phase's
        //    number (ModalFallback.Release in the MODAL TICK BREAKDOWN) and is additionally printed
        //    per-window-walk by its own MODAL LIVENESS CENSUS line.
        TickWindowLiveness();
        // 1. Release conversions whose window closed/died or that are no longer wanted.
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            // ModBuild 198: PER-WINDOW, not global. A window the fail-open path floated because
            // nothing else can draw it (StrandedFloat) must not be torn down again by the next
            // tick's release pass just because there is still no room — that oscillation would be
            // the unreachable menu again, one frame at a time. FloatWantedFor asks both questions.
            bool alive = wp.Window != null && wp.Panel.IsAlive && FloatWantedFor(wp.Window, convertWanted);
            // Item 6: a sticky reachable menu the user has NOT closed stays floated even when the
            // game hid it (not in OpenWindows) — parallel windows. Every other window releases as
            // soon as it leaves the open set (or convert is no longer wanted, or the user closed it).
            // Tutorial deadlock #2 do-no-harm: a level-message group whose scripted message the
            // game still considers DISPLAYED (LevelMessagesUIHandler current-message state) is
            // NEVER released, whatever the poll flags momentarily read — releasing it mid-message
            // restores the box to the invisible 2D stack and the dismiss-chained tutorial dies
            // there. The float releases normally the moment the message is genuinely dismissed
            // (current message nulled / next message's DisplayDelay in effect).
            // ModBuild 230: the liveness verdict OVERRIDES every keep-alive clause below it, and it
            // has to. The two shapes it fires on are "the content was destroyed" and "the content
            // draws nothing" — a window in either state is exactly what the sticky clause and the
            // scripted-message clause exist to keep floating, so leaving them ahead of it would keep
            // the empty frame for the same reasons that produced it. The verdict is not a guess: see
            // wp.EmptyReleaseShape, which is printed verbatim in the release line below.
            // ModBuild 235 — AND A REFUSAL OUTRANKS `Sticky`, WHICH IS THE ONE THING THAT STOPPED
            // ModBuild 234's STORY CURTAIN FROM DOING ANYTHING AT ALL.
            //
            // USER REPORT, item 3, verbatim: "Die Questliste als Fenster soll auch verschwinden nach
            // dem 'Point of no return'." The curtain shipped in 234, NAMED the quest log as a frozen
            // member (Player.log:11187) — and the window stayed on screen for the rest of the
            // session, with no `FLOAT REFUSED: 'Quest Log Manager'` line anywhere in the log. The two
            // members failed for two DIFFERENT reasons and both of them end on this line:
            //
            //   * 'Quest Log Manager' (UIWindowID.None, catch-all path). CatchAllObserve drops a
            //     window from `UnknownShown` the moment the game hides it, and the game hid the quest
            //     log at :10503 — 684 lines BEFORE the curtain rose. TickCatchAll only ever iterates
            //     `UnknownShown`, so FloatRefusalTable.Refuse was never asked about it and
            //     WithdrawRefusedFloat was never reached. The float stayed alive purely on `Sticky`,
            //     with ReassertStickyVisible force-showing a window the game had closed.
            //   * 'UI Quest Popup' (UIWindowID.QuestPopup, ENROLLED path). The refusal WAS consulted:
            //     the `Open` normalisation loop above already does
            //     `if (FloatRefusalTable.Refuses(window)) continue;`, so the window was correctly kept
            //     OUT of OpenWindows. It changed nothing, because `|| wp.Sticky` on the next line kept
            //     the existing float alive regardless of the set it had just been removed from.
            //
            // SO THE REFUSAL TABLE COULD ONLY EVER WITHDRAW A FLOAT ON ONE OF THE THREE PATHS INTO
            // THIS CLASS, and which path a window takes is an accident of whether its prefab carries
            // a serialized UIWindowID. One clause fixes both, and this is the honest place for it:
            // this line is where "is this float still wanted?" is decided, and a refusal is exactly an
            // answer of no. It is also the only lever that reaches a window the catch-all has stopped
            // tracking.
            //
            // NOTHING IS WRITTEN TO THE GAME BY THIS. Falling through to the release below is the
            // ORDINARY exit — Converted.RemoveAt, the grab holder destroyed, CanvasConversion.Release
            // restoring the exact 2D home, the arc slot handed back. `UserClosing` is NOT set, so the
            // `wp.Window.Hide()` gap-close below cannot fire for a refused window: refusing to DRAW
            // something must never take it away from a game that still needs it. That is
            // WithdrawRefusedFloat's own ruling, applied on the paths WithdrawRefusedFloat cannot
            // reach.
            //
            // ModBuild 251 — AND A REFUSAL REACHES A FLOAT THAT IS NESTED INSIDE THE REFUSED WINDOW.
            // The clause above asks the table about THIS window only, which is right for an identity
            // refusal and one window short for an interval one: a child that was already floating when
            // its ancestor was refused for the moment keeps its float on `wp.Sticky` for exactly the
            // reason the ModBuild 235 note above describes. It is the same lever and the same line.
            // The catch-all's own eligibility check stops the child being taken in the first place
            // (ModalFallback.10.CatchAll.RefusedForTheMomentAbove, which carries the ModBuild 250
            // hardware trace); this is the backstop for a float that already exists.
            UIWindow? intervalHost = wp.Window != null ? RefusedForTheMomentAbove(wp.Window) : null;
            bool refused = wp.Window != null
                           && (FloatRefusalTable.Refuses(wp.Window) || intervalHost != null);
            bool stillOpen = alive && !wp.UserClosing && !wp.EmptyReleasePending && !refused
                             && (ContainsWindow(OpenWindows, wp.Window!) || wp.Sticky
                                 || ScriptedLevelMessageActive(wp.Window));
            if (refused && alive)
                VRLog.Info("WorldUI", $"FLOAT RELEASED ON REFUSAL: '{wp.Window!.name}' (ID " +
                                      $"{wp.Window.ID}) — " +
                                      (FloatRefusalTable.Describe(wp.Window)
                                       ?? (intervalHost != null
                                           ? "it is NESTED INSIDE '" + intervalHost.name + "' (ID " +
                                             intervalHost.ID + "), which the table refuses for the "
                                             + "moment, so drawing this child would put the refused "
                                             + "window's own content in front of the player under a "
                                             + "different frame (ModBuild 251). THE ANCESTOR'S "
                                             + "REFUSAL: " +
                                             (FloatRefusalTable.Describe(intervalHost) ?? "<none>")
                                           : "<the table has nothing to say about this window>")) +
                                      ". The " +
                                      "float is given up through the ORDINARY release path (panel, grab " +
                                      "bar, close cross and arc slot together; CanvasConversion.Release " +
                                      "restores the exact 2D home). NOTHING WAS WRITTEN TO THE GAME: " +
                                      "UserClosing is not set, so no Hide, no Escape and no CanvasGroup " +
                                      "write — and the window floats again, with everything on it, the " +
                                      "moment the refusal lapses.");
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
            // ModBuild 230: read the chrome BEFORE it is torn down, so the release line can state
            // what actually went with the window rather than what usually does. That distinction is
            // the whole point of the report: in .planning/debug/leeres_fenster2.jpg the content went
            // and the chrome did not.
            bool hadGrab = wp.Grab != null;
            bool hadClose = wp.Panel != null && wp.Panel.HostRect != null
                            && wp.Panel.HostRect.Find("GloomhavenVR.ModalCloseX") != null;
            wp.Grab?.Destroy(); // drop the mod-owned grab holder (sub-item B) before releasing the host
            CanvasConversion.Release(wp.Panel); // restores the exact 2D home
            if (wp.EmptyReleasePending)
            {
                // HOLD IT OUT OF THE FLOAT SET UNTIL ITS CONTENT COMES BACK. The game may still
                // report this window open (that is precisely the failure: open, standing, drawing
                // nothing), so without this the convert loop three steps below would re-float it
                // into the same dark state on the very next tick, forever. The hold lifts on a
                // close/re-open or the moment the window is measured drawing again — see
                // EmptyHeldNow. Nothing is done to the GAME's window here: no Hide, no Escape, no
                // state write, and nothing on the wire. This is a presentation release.
                //
                // THE BLOCKING/NON-BLOCKING SPLIT IS ModBuild 226's, COPIED DELIBERATELY rather than
                // re-decided. A BLOCKING window that has gone dark is a prompt the player must
                // answer and cannot see, so it goes into `Failed` — which is a term of ScreenWanted,
                // so the full flat screen rises for it. That is the DurabilityPanel rule: a wrongly
                // screened window is recoverable, a dropped blocker is a deadlock. Everything else
                // goes into the hold, which has no screen term, so one dark map-room panel can never
                // raise the whole flat screen because some unrelated blocking window is up.
                string holdKind = "none — the window is already gone";
                if (wp.Window != null)
                {
                    if (IsBlockingWindow(wp.Window))
                    {
                        if (!ContainsWindow(Failed, wp.Window))
                            Failed.Add(wp.Window);
                        holdKind = "Failed (blocking → the flat screen rises so the prompt can still be answered)";
                    }
                    else
                    {
                        EmptyHold.Add(wp.Window);
                        holdKind = "EmptyHold (non-blocking → no screen; it re-floats when it draws again)";
                    }
                }
                VRLog.Warn("WorldUI", $"EMPTY WINDOW RELEASED: '{name}' (ID " +
                                      $"{(wp.Window != null ? wp.Window.ID.ToString() : "?")}) — " +
                                      $"{wp.EmptyReleaseShape}. It had been standing for " +
                                      $"{(Time.unscaledTime - wp.FloatedAt):F1} s, was last measured " +
                                      $"drawing something {(wp.LastDrawnAt > 0f ? $"{(Time.unscaledTime - wp.LastDrawnAt):F1} s ago" : "NEVER since it floated")}, " +
                                      $"and the liveness rule had been armed for it since " +
                                      $"{(wp.LivenessArmed ? $"{(Time.unscaledTime - wp.LivenessArmedAt):F1} s ago by {wp.LivenessArmReason}" : "never (the GONE shape needs no arming)")}. " +
                                      $"TORN DOWN WITH IT: the world host and its collider/raycaster, " +
                                      $"the grab bar ({(hadGrab ? "present" : "none — this window had no grab holder")}), " +
                                      $"the X ({(hadClose ? "present" : "none — this window floats without one")}), " +
                                      $"and the window's arc slot. It {(wp.Transient ? "IS" : "is NOT")} a transient " +
                                      $"announcement. RE-FLOAT GATE: {holdKind}. USER RULING (ModBuild 230): \"Es darf " +
                                      "niemals leere Fenster geben - verschwindet das Objekt das in dem " +
                                      "Fenster dargestellt wird, soll auch das Fenster verschwinden.\"");
            }
            else
            {
                VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' released — restored to its 2D home " +
                                      $"(open={wp.Window != null && wp.Window.IsOpen}, " +
                                      $"convertWanted={convertWanted}).");
            }
        }

        // 1b. SLOT RELEASE (map room): every window that just left the float set gives its arc slot
        //     back. Placed BETWEEN the release loop and the convert loop on purpose — a slot freed
        //     by a window closing this tick is available to a window opening in the SAME tick — and
        //     it MOVES NOTHING: the windows still standing keep the pose they claimed at spawn.
        //     See the slot-registry block above for the ruling this replaces the relayout with.
        ReleaseFinishedArcSlots();

        // 2. Failed windows retry only after a close/re-open (no per-frame spam).
        for (int i = Failed.Count - 1; i >= 0; i--)
        {
            if (Failed[i] == null || !ContainsWindow(OpenWindows, Failed[i]))
                Failed.RemoveAt(i);
        }

        EnterPhase(PhaseConvert);
        // 3. Convert newly opened windows — plus, since ModBuild 198, any window the presentation
        //    step proved is drawn by nothing at all (StrandedFloat). The per-window test is the
        //    same one the release loop above uses, so the two can never disagree about a window.
        for (int i = 0; i < OpenWindows.Count; i++)
        {
            UIWindow window = OpenWindows[i];
            if (!FloatWantedFor(window, convertWanted))
                continue;
            if (IsConverted(window) || ContainsWindow(Failed, window))
                continue;
            // ModBuild 230: this window was released for drawing nothing and is STILL reported open,
            // so re-floating it now would rebuild the empty frame the release just removed. Held
            // until its content comes back (or it closes) — the same "retry only after the condition
            // changes" shape Failed uses, keyed on the condition that actually decided it. Placed
            // here rather than only in CatchAllEligible because THIS window is ID-tracked: the
            // ModBuild 226 EmptyRefused set is consulted by the catch-all alone, which is why an
            // enrolled ID could never have been held by it.
            if (EmptyHeldNow(window))
                continue;
            // ModBuild 232 — A TWO-TICK BRIDGE, NOT A SUPPRESSION. StoryComposite's point-of-no-
            // return gate is a ONE-SHOT at the rising edge: it closes a NAMED set of windows through
            // the game's own close (CloseFloatedWindow) and then closes nothing ever again. This
            // call covers only the frame or two in which a window whose Escape() ran a TRANSITION
            // still reads IsOpen and would be taken straight back. It is bounded by membership (only
            // instances that edge closed), by time (2 ticks per gate, never re-armed) and by
            // StoryComposite's own deadlock floor.
            //
            // THE 2 IS DERIVED FROM THE FUSE IN ModalFallback.10.CatchAll.cs AND MUST NOT BE RAISED.
            // The churn fuse counts every tick on which the catch-all enrols a window the mod is not
            // already floating, and ChurnMaxFloats is 3 — so a window held out of the float set for
            // four ticks is session-suppressed BY NAME. ModBuild 231 held the loadout screen
            // indefinitely here and the log reads "CATCH-ALL FUSE: window 'UI Loadout Window'
            // re-floated 4x in 60s — suppressed for this session", after which the player had zero
            // floated windows and quit. A hold that outlives its edge is a deadlock generator.
            //
            // A `continue` and NOT a TryConvertWindow refusal: a refusal enrols the window in Failed
            // and raises the flat screen for it, and "closed" here means closed, not moved to a
            // screen.
            if (StoryComposite.HoldsBack(window))
                continue;
            if (!TryConvertWindow(window))
                Failed.Add(window);
        }

        EnterPhase(PhaseRaycast);
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

        EnterPhase(PhaseGrabFollow);
        // 5. Sub-item B: the game-owned host follows its mod-owned grab frame every tick
        //    (static while ungripped; moved/scaled by the shared PanelGrabHandle while a hand
        //    grips the bar). Every floated modal is grabbable now, Sieg/Niederlage included.
        //    A HOVER CARD has no grab frame at all (ModBuild 181) — TickHoverCards owns its pose.
        for (int i = 0; i < Converted.Count; i++)
        {
            if (Converted[i].HoverCard)
                continue;
            // ...and its grab bar is re-tinted from the LIVE shared-window predicate in the same
            // pass. THIS is the one place that knows both halves — the mod-owned GrabbableModal and
            // the game UIWindow it was built for — and the tint has to be re-derived per tick
            // because participation can flip while the window stands (the player toggles the 3D
            // world map; MapRoomDriver reads that config live). Change-gated inside: a standing
            // window costs one Color comparison and writes nothing. See GrabbableModal's
            // SHARED-WINDOW BAR COLOUR block for the user request and the whole design.
            // ModBuild 290 — AND BECAUSE THE SESSION ITSELF CAN COME UP OR DROP under a standing
            // window. "Wechselt der Spieler von Singleplayer zum Multiplayer werden diese
            // entsprechenden betroffenen Fenster 'blau' und verhalten sich entsprechend." This loop
            // is what makes that live: SharedWindows.ParticipatesHere now tests
            // FFSNetwork.IsOnline first, so a window standing open when a session starts turns blue
            // on the next tick and starts behaving as shared without being reopened.
            //
            // THIS CALL NOW CARRIES A SECOND LOAD (2026-08-22, requests 7a/7b), and it must stay
            // AHEAD of the Tick() below rather than beside it: the same predicate answer is cached
            // on the grab (GrabbableModal._shared) for the two consumers that never see a UIWindow —
            // the release re-face gate ("a window everyone shares keeps the orientation it is given")
            // and the remote pose easing. Both are reached from paths that hold only the mod-owned
            // grab: the handle's release edge, and the two net pose appliers. This is the one place
            // per tick that knows both halves, so a window whose bar is blue is exactly a window
            // that will not re-face — one fact, one evaluation, no second predicate to drift.
            Converted[i].Grab?.SyncSharedBarTint(Converted[i].Window);
            Converted[i].Grab?.Tick();
        }
        TickHoverCards();
        EnterPhase(PhaseDestinations);
        // ModBuild 184: the merchant/temple/trainer/enchantress/records window carries its shared
        // background banner while it is floated — the same move the game makes for the temple.
        // Level-triggered and idempotent; see GuildmasterDestinations for the whole story,
        // including why closing one of these must press the bar's map button.
        MapRoom.GuildmasterDestinations.Reconcile(FirstFloatedDestination());
        // (ModBuild 183's RelayoutMapRoomArc was called from HERE, gated on the arc COUNT changing,
        // and that call WAS the reported bug: an open or a close re-posed every other window. It is
        // gone — the arc is claimed one slot at a time at spawn (see the slot registry above), and
        // there is deliberately NO per-tick layout step left in this method. If a future round finds
        // itself wanting to "just re-arrange them once more", that is this bug being re-introduced;
        // the ruling is quoted in full at the registry.)
        EnterPhase(PhaseProbeFlicker);
        // The flicker instrument (ModBuild 182): armed exactly while floated panels exist, so it
        // costs nothing in a scenario with none and nothing in the menu. See the panel-state probe for
        // why the next round needs a measurement rather than a fourth hypothesis.
        EnterPhase(PhaseProbeCameraOrder);
        // ModBuild 185: the measurement the panel-state probe's silence pointed at — see
        // CameraOrderProbe. Armed on the same condition; TickApply performs any correction the
        // last judged frame asked for, here in Update and never inside the render loop.
        CameraOrderProbe.Sync(Converted.Count > 0);
        CameraOrderProbe.TickApply();
        EnterPhase(PhaseProbeRenderTarget);
        // ModBuild 186: with the panels proven steady and both eyes proven to read the same
        // texture, what is left is TEMPORAL content change — measurable from Update, no render
        // hook needed. See RenderTargetProbe.
        RenderTargetProbe.Tick(Converted.Count > 0);

        EnterPhase(PhaseScroll);
        // 5a-scroll. User #12: thumbstick-Y scrolls the Sieg/Niederlage results window's
        //    scroll area while a laser/poke hovers ANYWHERE on the floated window — the
        //    generic RayUguiDriver stick-scroll only fires when the hover raycast lands
        //    INSIDE a ScrollRect subtree, which the results list misses (its rows carry no
        //    raycast targets, so the hover lands on the window frame outside the viewport).
        TickResultsStickScroll();

        EnterPhase(PhaseRefit);
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

        EnterPhase(PhaseChainPose);
        // 5c. LEVEL-MESSAGE CHAIN POSE: record where the player is reading the scripted message
        //     chain, so the NEXT hint spawns there. Runs after the grab follow so it sees the
        //     final host pose of this tick — for a gripped window that is the hand's current spot.
        //
        //     THIS STEP NO LONGER MOVES ANYTHING. Until ModBuild 149 it was TickMenuRecall, and it
        //     re-placed any open, never-grabbed menu that had been out of view for 6 s or was more
        //     than 4 m away. The user ruled that out ("Die Fenster ... sollen dort dauerhaft fest
        //     sitzen wenn sie nicht aktiv verschoben werden"); the ruling block is at the top of
        //     ModalFallback.6.MenuGuard.cs and says what still rescues a genuinely lost window.
        TickLevelMessageChain();

        EnterPhase(PhaseMenuGuard);
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

        EnterPhase(PhaseEscape);
        TickEscapeChord(); // test #17: floating modals must always be closable
        EnterPhase(PhasePublish);

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

        // ---- close the measurement out (see the SUB-STEP ATTRIBUTION block) ------------------
        EndPhase();
        long tickTicks = System.Diagnostics.Stopwatch.GetTimestamp() - tickBegin;
        if (tickTicks > 0L)
        {
            _tickTotalTicks += tickTicks;
            if (tickTicks > _tickWorstTotalTicks)
                _tickWorstTotalTicks = tickTicks;
        }
        _tickFrames++;
        float nowT = Time.unscaledTime;
        if (_nextTickBreakdown <= 0f)
        {
            // First tick of a session: start the clock rather than printing a one-frame window.
            _nextTickBreakdown = nowT + TickBreakdownSeconds;
        }
        else if (nowT >= _nextTickBreakdown)
        {
            _nextTickBreakdown = nowT + TickBreakdownSeconds;
            LogTickBreakdown();
            ResetTickBreakdown();
        }
    }
}
