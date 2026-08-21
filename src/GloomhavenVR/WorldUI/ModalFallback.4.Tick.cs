using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using Script.GUI.Popups;
using UnityEngine;
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
        MapRoom.GuildmasterDestinations.Reset(); // hand the borrowed banner back before we vanish
        MapRoom.HoverCardPose.Reset();          // per-card follow/seat state dies with the module
        // Every window has just been released, so every arc slot is free by definition. Clearing
        // the registry here (rather than letting the sweep do it) means a fresh session never
        // inherits a claim held by a panel from the previous one.
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            _arcClaims[i] = null;
            _arcClaimNames[i] = null;
        }
        _arcGeometryLogged = false; // re-state the arc geometry once per session
        ScreenWanted = false;
        VRModeStateMachine.SetAuxModal(false);
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
    // WHAT PART OF 183 SURVIVES. The arc GEOMETRY was never the complaint — it was asked for
    // ("Ich möchte das die Fenster … im Halbkreis um einen gespawned werden, so dass man alle
    // direkt perfekt im Überblick hat") and it is what keeps the whole set in the field of view,
    // which this ruling repeats. So the angles (0, ±34°, ±68°), the constant reading distance and
    // the yaw-only facing are kept BIT FOR BIT; what changes is who decides them and when. The
    // decision moves from "recompute the ensemble on every event" to "claim one slot at spawn":
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
    // "which slot does this window hold" question is answered by a five-entry scan and there is no
    // second copy of the truth to fall out of sync with the first.
    //
    // A WINDOW THE PLAYER HAS MOVED KEEPS ITS SLOT, deliberately. GrabbableModal.UserMoved latches
    // on the first grip and is already honoured everywhere a pose can be written (the presence-
    // regain refloat skips it; the pre-reveal re-place refuses to touch a grabbed OR an already
    // visible window, and a window the player has gripped is by construction both). Freeing the
    // slot of a carried-off window would let the NEXT window spawn on the angle it vacated — and
    // since an arc spawn deliberately does no modal-vs-modal box test (the slot is what
    // deconflicts), a window the player only NUDGED would then have a fresh one materialise on top
    // of it. Holding the claim costs one seat out of five and cannot superimpose anything; that is
    // the safer side of the trade, and the player who moved one window can move the next.
    //
    // MULTIPLAYER: nothing here goes on the wire, and nothing may. Which windows a client has open,
    // and where in ITS room they stand, is local presentation — it is derived from that client's
    // own head pose at its own spawn moments, and two clients in the same map room legitimately see
    // their own arrangement. No NetProtocol record, no ModBuild-relevant wire change.

    /// <summary>
    /// How many DISTINCT slots the arc has: the centre plus one step each way until
    /// <see cref="MaxArcHalfDegrees"/> is passed, i.e. <c>1 + 2 × floor(85 / 34) = 5</c> at the
    /// shipped angles → 0°, ±34°, ±68°.
    ///
    /// <para>THE OLD CODE HAD NO SUCH BOUND AND THAT WAS A LATENT COLLISION: it clamped the angle
    /// with <c>Mathf.Min(step × 34, 85)</c>, so the sixth window sat at +85° and the seventh at
    /// −85°, the eighth at +85° again — i.e. exactly ON TOP of the sixth. Here the capacity is the
    /// number of slots that genuinely exist, and everything past it is handled explicitly by the
    /// overflow rule below instead of silently coinciding.</para>
    /// </summary>
    private const int ArcSlotCount = 5;

    /// <summary>
    /// WHAT HAPPENS WHEN THE ARC IS FULL. A sixth window may not vanish and may not land exactly on
    /// top of a fifth, so it falls back to the SAME treatment a stacked secondary gets at a
    /// scenario table (<see cref="SecondaryStaggerMeters"/> / <see cref="SecondaryForegroundMeters"/>):
    /// dead centre, nudged right+down and pulled toward the head, so it stands clearly in the
    /// FOREGROUND of the centre window rather than merging with it — visible, readable, grabbable,
    /// and separable by hand. Each overflow window takes its own stagger index, so overflow windows
    /// never coincide with each other either. Four of them is already an eight-window room; past
    /// that the last index is reused and the log says so rather than pretending otherwise.
    /// </summary>
    private const int ArcOverflowCapacity = 4;

    /// <summary>
    /// THE CLAIM ITSELF. Index &lt; <see cref="ArcSlotCount"/> = a real arc slot (angle from
    /// <see cref="ArcSlotAngleDeg"/>); index ≥ that = an overflow stack index. Null = free.
    /// Written ONLY by <see cref="TryClaimArcSlot"/> (spawn / presence-regain refloat) and
    /// <see cref="ReleaseFinishedArcSlots"/> (a window stopped floating / the room ended).
    /// </summary>
    private static readonly ConvertedPanel?[] _arcClaims =
        new ConvertedPanel?[ArcSlotCount + ArcOverflowCapacity];

    /// <summary>Log name of the panel holding each claim — kept separately because the RELEASE line
    /// has to name the window AFTER its host has already been destroyed.</summary>
    private static readonly string?[] _arcClaimNames = new string?[ArcSlotCount + ArcOverflowCapacity];

    /// <summary>One-time geometry report (see <see cref="LogArcGeometryOnce"/>).</summary>
    private static bool _arcGeometryLogged;

    /// <summary>
    /// The angle of arc slot <paramref name="slot"/> in degrees, measured from the spawn gaze,
    /// + = right. IDENTICAL to ModBuild 183's sequence (0°, +34°, −34°, +68°, −68°) so a room laid
    /// out by the old relayout and one laid out by the slot claims look the same; only the timing
    /// of the decision changed. Out-of-range (overflow) slots answer 0° — they are centred and
    /// staggered instead, see <see cref="ArcOverflowCapacity"/>.
    /// </summary>
    private static float ArcSlotAngleDeg(int slot)
    {
        if (slot < 0 || slot >= ArcSlotCount)
            return 0f;
        int step = (slot + 1) / 2;
        float sign = (slot % 2) == 1 ? 1f : -1f;
        return step * ArcStepDegrees * sign;
    }

    /// <summary>How many arc slots and overflow stacks are currently claimed.</summary>
    private static void CountArcClaims(out int slotsUsed, out int overflowUsed)
    {
        slotsUsed = 0;
        overflowUsed = 0;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i] == null)
                continue;
            if (i < ArcSlotCount)
                slotsUsed++;
            else
                overflowUsed++;
        }
    }

    /// <summary>
    /// States the arc's geometry ONCE per session, so a hardware log can be read without having to
    /// know the constants: how many slots exist, at which angles, and what the capacity means. If a
    /// future edit changes <see cref="ArcStepDegrees"/> / <see cref="MaxArcHalfDegrees"/> without
    /// changing <see cref="ArcSlotCount"/>, the mismatch is named here rather than showing up as two
    /// windows quietly sharing an angle.
    /// </summary>
    private static void LogArcGeometryOnce()
    {
        if (_arcGeometryLogged)
            return;
        _arcGeometryLogged = true;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ArcSlotCount; i++)
            sb.Append(i == 0 ? "" : ", ").Append(i).Append(':').Append(ArcSlotAngleDeg(i).ToString("F0")).Append('°');
        int derived = 1 + 2 * Mathf.FloorToInt(MaxArcHalfDegrees / ArcStepDegrees);
        VRLog.Info("WorldUI", $"MAP ROOM WINDOW SLOTS: the arc has {ArcSlotCount} slots [{sb}] at "
                              + $"{WindowDistanceMeters:F2} m reading distance, plus {ArcOverflowCapacity} "
                              + "overflow stacks in front of the centre. A window claims ONE slot when it "
                              + "spawns and keeps it until it stops floating; opening or closing a window "
                              + "never moves any other window (user ruling: 'einmal gespawned sind sie fix')."
                              + (derived == ArcSlotCount
                                  ? ""
                                  : $" WARNING: the step/half-angle constants ({ArcStepDegrees:F0}°/"
                                    + $"{MaxArcHalfDegrees:F0}°) imply {derived} distinct slots, not "
                                    + $"{ArcSlotCount} — slots would share angles or be wasted. Fix "
                                    + "ArcSlotCount."));
    }

    /// <summary>
    /// Claim (or re-find) this panel's arc slot. Called from <see cref="ComputeHmdPose"/>, i.e. at
    /// spawn and at presence-regain refloat, and NEVER per frame.
    ///
    /// <para>THE CHOICE RULE — FREE SLOT NEAREST THE CENTRE, right side first. The slot indices are
    /// already ordered centre-outward (0°, +34°, −34°, +68°, −68°), so "first free index" IS
    /// "nearest the centre", and the tie between the two sides of a step is broken to the right,
    /// which is the side ModBuild 183 filled first. Why this rule and not "always dead ahead":
    /// dead ahead is only free for the FIRST window, and taking it from a window already standing
    /// there is the move the ruling forbids. Why not "next index after the last one used": that
    /// wastes the middle — close the centre window and every future window would sit off to the
    /// side with a hole where the player is looking. Nearest-to-centre re-uses freed central space
    /// immediately and keeps the ensemble symmetric and inside the field of view, which is the
    /// other half of the same ruling ("so das alle im Sichtfeld passen").</para>
    ///
    /// <para>EXCLUSIONS. Outside the map room there is no arc (a scenario table stacks its
    /// secondaries instead — item 2/3b). A HOVER CARD never claims: <c>TickHoverCards</c> owns its
    /// pose, and it joins and leaves <see cref="Converted"/> on every single mouseover, so a claim
    /// would churn the whole registry for something that is not a window. A LEVEL MESSAGE never
    /// claims: its pose is governed by the chain-continuity ruling. The global error box never
    /// claims: it is not a <see cref="Converted"/> window, so nothing would ever release it.</para>
    /// </summary>
    /// <param name="slot">The claimed index, or −1 when the arc AND the overflow are both full
    /// (last-resort placement, see <paramref name="staggerIndex"/>).</param>
    /// <param name="yawDeg">Degrees to rotate the spawn gaze by, + = right.</param>
    /// <param name="staggerIndex">0 for a real arc slot; ≥1 for an overflow stack (the existing
    /// right+down+foreground stagger).</param>
    /// <param name="why">Human-readable reason for the log line.</param>
    /// <returns>true when the map room's arc governs this placement.</returns>
    private static bool TryClaimArcSlot(ConvertedPanel? panel, bool levelMessage,
        out int slot, out float yawDeg, out int staggerIndex, out string why)
    {
        slot = -1;
        yawDeg = 0f;
        staggerIndex = 0;
        why = "";
        if (panel == null || levelMessage || !MapRoom.MapRoomDriver.Active)
            return false;
        if (ReferenceEquals(panel, _errorPanel))
            return false; // not a Converted window — no release path would ever free its slot
        if (IsHoverCardPanel(panel))
            return false; // TickHoverCards owns its pose (and it churns on every mouseover)

        LogArcGeometryOnce();

        // Already holds one? Presence-regain refloat re-places an EXISTING float, and it must land
        // back on its own slot rather than take a second one (and rather than pile every
        // non-grabbed window dead ahead, which is what it did while the relayout owned the arc).
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (!ReferenceEquals(_arcClaims[i], panel))
                continue;
            slot = i;
            yawDeg = ArcSlotAngleDeg(i);
            staggerIndex = i < ArcSlotCount ? 0 : i - ArcSlotCount + 1;
            why = "re-uses the slot it already claimed (a refloat must not take a second one)";
            return true;
        }

        CountArcClaims(out int slotsUsed, out int overflowUsed);
        for (int i = 0; i < ArcSlotCount; i++)
        {
            if (_arcClaims[i] != null)
                continue;
            slot = i;
            yawDeg = ArcSlotAngleDeg(i);
            staggerIndex = 0;
            why = slotsUsed == 0
                ? "the arc was empty, so the centre slot was free"
                : $"the free slot NEAREST THE CENTRE ({slotsUsed} of {ArcSlotCount} were taken); "
                  + "the windows already standing were not touched";
            _arcClaims[i] = panel;
            _arcClaimNames[i] = PanelLogName(panel);
            return true;
        }
        for (int i = ArcSlotCount; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i] != null)
                continue;
            slot = i;
            yawDeg = 0f;
            staggerIndex = i - ArcSlotCount + 1;
            why = $"ALL {ArcSlotCount} arc slots are occupied — overflow stack {staggerIndex}: "
                  + "placed centred but nudged right/down and pulled toward the head, so it stands "
                  + "in front of the centre window instead of merging with it";
            _arcClaims[i] = panel;
            _arcClaimNames[i] = PanelLogName(panel);
            return true;
        }
        // Nine floated windows in one room. Place it on the last overflow offset rather than
        // dropping it somewhere unreachable, and say plainly that it may coincide with another.
        slot = -1;
        yawDeg = 0f;
        staggerIndex = ArcOverflowCapacity;
        why = $"the arc ({ArcSlotCount} slots) AND the overflow ({ArcOverflowCapacity} stacks) are "
              + $"both full ({slotsUsed}+{overflowUsed} claims) — placed on the LAST overflow offset "
              + "WITHOUT a claim, so it may coincide with the window already there. It is still in "
              + "view and grabbable; close a window to free a slot";
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
    /// Free the slots of windows that have stopped floating, and NOTHING ELSE — no window is
    /// re-posed here, which is the whole point of the ruling: "ohne explizite Bewegung vom User,
    /// sollen sie ihre Position nicht verändern" covers a CLOSE just as much as an open.
    ///
    /// <para>Run from <see cref="Tick"/> between the release loop and the convert loop, so a slot
    /// freed by a window closing this tick is available to a window opening in the SAME tick.
    /// Membership in <see cref="Converted"/> is the liveness test rather than
    /// <see cref="ConvertedPanel.IsAlive"/>, because <c>CanvasConversion.Release</c> deliberately
    /// leaves the panel's target alive (it hands the game window back to its 2D home) — a released
    /// window is one that has left <see cref="Converted"/>, and that list is the authority.</para>
    /// </summary>
    private static void ReleaseFinishedArcSlots()
    {
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            ConvertedPanel? claimed = _arcClaims[i];
            if (claimed == null || ContainsPanel(claimed))
                continue;
            // Deliberately NOT gated on MapRoomDriver.Active. A claim is released by the window
            // ceasing to float and by nothing else — so leaving and re-entering the room (or the
            // room signal flickering for a frame) can never orphan a slot that a standing window
            // still occupies, and a window that IS released while the room is gone still frees its
            // seat. Module shutdown clears the whole registry in Detach().
            string name = _arcClaimNames[i] ?? "<window>";
            _arcClaims[i] = null;
            _arcClaimNames[i] = null;
            CountArcClaims(out int slotsUsed, out int overflowUsed);
            VRLog.Info("WorldUI", $"MAP ROOM WINDOW SLOT RELEASED: '{name}' gave up "
                                  + (i < ArcSlotCount
                                      ? $"arc slot {i} ({ArcSlotAngleDeg(i):F0}° from its spawn gaze)"
                                      : $"overflow stack {i - ArcSlotCount + 1}")
                                  + " — it is no longer floated (the player closed it, the game "
                                  + "released it, or the room ended). "
                                  + $"{slotsUsed}/{ArcSlotCount} arc slots now occupied, "
                                  + $"{ArcSlotCount - slotsUsed} free, {overflowUsed} overflow. "
                                  + "NOTHING WAS MOVED: every window still standing keeps the exact "
                                  + "pose it claimed at spawn (user ruling). The freed slot goes to "
                                  + "the next window that opens — which may be this same window "
                                  + "re-opened, at a different angle, and that is expected.");
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

    /// <summary>How many arc slots exist in total (see <see cref="ArcSlotCount"/>).</summary>
    internal static int FloatedArcSlotCapacity => ArcSlotCount;

    /// <summary>How many arc slots are claimed right now (overflow stacks not counted).</summary>
    internal static int FloatedArcSlotsOccupied
    {
        get
        {
            CountArcClaims(out int slotsUsed, out _);
            return slotsUsed;
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

        // "IS THERE A ROOM TO FLOAT A WINDOW IN", not "is a scenario running" (ModBuild 178). The
        // 3D map room is a room: the flat screen is deliberately OFF there (FlatScreen.ScreenWanted
        // returns false on MapRoomDriver.Active, so the room and the flat map render never both own
        // the parchment) — so a window that is not floated here is not shown on a screen instead,
        // it is shown NOWHERE. That was the whole of the 177 report: the pause menu opened, logged
        // "opened without a VR conversion (scenario=False)", and the player saw nothing at all.
        // Every downstream use of this local — the catch-all, the reward showcase, the chain-pose
        // reset — is about PRESENCE, which is what this predicate names.
        bool inScenario = VRModeStateMachine.TableInFrontOfPlayer;

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
        //    A HOVER CARD has no grab frame at all (ModBuild 181) — TickHoverCards owns its pose.
        for (int i = 0; i < Converted.Count; i++)
        {
            if (!Converted[i].HoverCard)
                Converted[i].Grab?.Tick();
        }
        TickHoverCards();
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
        // The flicker instrument (ModBuild 182): armed exactly while floated panels exist, so it
        // costs nothing in a scenario with none and nothing in the menu. See PanelFlickerProbe for
        // why the next round needs a measurement rather than a fourth hypothesis.
        PanelFlickerProbe.Sync(Converted.Count > 0);
        // ModBuild 185: the measurement PanelFlickerProbe's silence pointed at — see
        // CameraOrderProbe. Armed on the same condition; TickApply performs any correction the
        // last judged frame asked for, here in Update and never inside the render loop.
        CameraOrderProbe.Sync(Converted.Count > 0);
        CameraOrderProbe.TickApply();
        // ModBuild 186: with the panels proven steady and both eyes proven to read the same
        // texture, what is left is TEMPORAL content change — measurable from Update, no render
        // hook needed. See RenderTargetProbe.
        RenderTargetProbe.Tick(Converted.Count > 0);

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
