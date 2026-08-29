using System.Collections.Generic;
using AStar;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// Click commit (Phase 3a): trigger-press (far mode) / fingertip touch (near mode)
/// while a pick target exists → one game click, injected at the game's OWN
/// click-detection point so everything downstream (double-click semantics, second-
/// click-to-confirm, undo, MP replication) is byte-identical to a mouse click.
///
/// DIRECT FINGERTIP TOUCH (user requirement 2026-08 — the gesture the tutorial teaches).
/// Putting the index fingertip onto a hex commits exactly what a laser click on that hex
/// commits: the SAME <see cref="RequestClick"/>, the same injection point, the same
/// suppression (<c>ModalFallback.HardCommitLockActive</c>), so there is no second
/// game-state path and multiplayer stays byte-identical. Its three rules:
///
/// - <b>Grip-gated.</b> <see cref="BoardPick.TryNearPick"/> only produces a near pick while
///   that hand holds the grip and holds nothing; <see cref="TickNear"/> re-checks the grip at
///   commit time. Letting go of the grip mid-touch therefore commits nothing — the pick is
///   gone on that very frame and the arming state is dropped.
/// - <b>One commit per hex ENTRY, not continuously.</b> The commit fires when the fingertip
///   reaches <see cref="ContactDepth"/> on a target, and then that target is spent. Re-arming
///   takes one of: the fingertip moving onto a DIFFERENT target (sliding along a row of hexes
///   commits each hex once, as it should), retracting past <see cref="ReleaseDepth"/> above the
///   same target (touch the same hex twice — this is how second-click-to-confirm is done with
///   the finger), or the pick going away at all (grip released / finger out of range).
/// - <b>Anti-jitter floor.</b> <see cref="TouchCooldownSeconds"/> between two commits of the
///   same hand, because the target-changed re-arm is exactly what tracking jitter on a hex
///   BORDER produces at frame rate (the same flip-flop <see cref="ArmPlacementTile"/> was
///   written for). It is far below any deliberate second touch and stays inside the game's
///   0.3 s double-click window, so a deliberate double-touch still reads as a double click.
///
/// The laser cannot double-commit what the finger commits: near and far are mutually
/// exclusive per frame by construction (<see cref="BoardPick"/> arbitration — the switch in
/// <see cref="Tick"/> reaches ONE of the two branches), so with the fingertip in range and
/// the grip held the trigger is simply not a board click.
///
/// FINGERTIP PING vs SELECT, DECIDED PER TARGET (user request 2026-08: "wird in der
/// Auswahl-Phase ein Tile angetippt, das NICHT zur Auswahl steht, soll trotzdem ein Ping
/// kommen; wird ein Feld angetippt, das ZUR AUSWAHL steht, soll kein Ping kommen und es
/// stattdessen ausgewählt werden"). The SAME gesture and the SAME commit mechanics (grip
/// gate, entry edge, cooldown, occluder/poke guards) — only the commit's MEANING branches at
/// the last moment, and it branches on the TAPPED HEX, not on the phase:
///
/// - the tapped hex IS a valid target of the pending selection → the click path below runs
///   untouched (SELECT), byte-identical to before;
/// - the tapped hex is NOT a valid target, or nothing is pending at all, or the pending
///   selection does not belong to a character we control → the touch fires the game's own
///   ping through <see cref="BoardPing.TryPingClientTile"/> instead.
///
/// The predecessor of this rule (2026-08, same user) suppressed the ping WHOLESALE while a
/// selection was pending; <see cref="DecideTapCore"/> replaces that single phase test with the
/// per-target decision table documented on it. Non-tile touches (miniatures, doors, chests)
/// always keep the click + <see cref="MiniaturePokedEvent"/> — the user asked for tile pings
/// only, and poking a miniature must keep opening its panel. Vanilla precedent for "same
/// click, different meaning by state": Controller.LateUpdate itself branches the very same
/// tile click into PingTile when s_ShouldPing is set (decompiled Controller.cs:178-183) — we
/// branch on the pending action's own accept set instead of a gamepad combo.
///
/// THE LASER TRIGGER IS DELIBERATELY NOT GIVEN THIS RULE (<see cref="TickFar"/> stays a pure
/// click). The two paths are not symmetric in INPUT: the laser hand already owns a dedicated,
/// explicit ping input — the dominant "A" press handled by <see cref="BoardPing"/>, which pings
/// whatever the beam points at, in or out of a selection — whereas the fingertip has exactly
/// one gesture and no second button to spend. Turning the trigger into a conditional ping would
/// therefore not add an ability, it would REMOVE one (the laser could no longer deliver a plain
/// click to a non-target hex, which vanilla self-gates harmlessly in Controller.LateUpdate) and
/// it would collide with the A-press the same hand already has. Both paths do share the ONE
/// ping seam (<see cref="BoardPing.TryPingClientTile"/>), so a ping is the same ping — same
/// channel, same MP replication, same visual — whichever hand produced it.
///
/// INJECTION STRATEGY — postfix on <c>Controller.CommonLoop</c> (the single input
/// read the click dispatcher uses). Rationale, from the decompiled Controller.cs
/// (verified against the real DLL, see the patch class below):
///
/// - <c>Controller.LateUpdate</c> only dispatches picks when <c>CommonLoop</c>
///   returns true; CommonLoop reads InControl
///   (<c>PlayerControl.MouseClickLeft.WasPressed/.WasReleased</c>) and produces
///   three static flags: <c>s_SingleClicked</c>, <c>s_DoubleClicked</c>,
///   <c>s_StartedButtonDownInGUI</c>.
/// - Making InControl's polled <c>WasPressed</c> observe a synthetic press is
///   awkward (GHControls.SimulateOnPress only fires event subscribers, not the
///   polling API — BOARD-INPUT §5), and calling
///   <c>CInteractableTile/CInteractableActor.ShowNormalInterface</c> or
///   <c>TileBehaviour.s_Callback</c> directly would SKIP LateUpdate's gating
///   (InteractabilityManager tile gating, ThisPlayerHasTurnControl, ping handling)
///   and the double-click bookkeeping.
/// - So the postfix simply ORs our click into CommonLoop's outputs — replicating
///   the method's own release-branch verbatim (single/double click timing against
///   <c>m_DoubleClickStart</c>) — and lets the vanilla LateUpdate do the entire
///   dispatch through our patched <c>MF.FindInteractableAtMousePosition</c>.
///   One patch, zero game logic bypassed. s_Callback is never invoked by us.
///
/// The pending click is set in Update (this driver) and consumed by the game's
/// LateUpdate in the same frame; it self-expires at the start of the next Tick,
/// so a click can never fire against a stale pick.
/// </summary>
internal static class BoardClickDriver
{
    /// <summary>Fingertip depth (real meters) that commits a near-mode click (mirrors PokeInteractor's contact radius).</summary>
    private const float ContactDepth = 0.008f;

    /// <summary>Fingertip must retract past this (real meters) to re-arm the near click.
    /// Mirrors <c>PokeInteractor.ReleaseRange</c> — the board click and the poke must arm and
    /// re-arm at the same depths or the two surfaces feel different under one finger. Both
    /// mirrors are enforced by <c>scripts/check-mirrors.sh</c> (deliberately a lint rather
    /// than a shared constant — REVIEW-Hands-Board-Core §P3).</summary>
    private const float ReleaseDepth = 0.02f;

    /// <summary>
    /// Minimum seconds between two fingertip commits of the SAME hand. Not a dwell and not a
    /// feel knob: the "target changed → re-arm" rule is what makes a hex BORDER dangerous,
    /// because tracking jitter flips the resolved target between two adjacent hexes at frame
    /// rate. Deliberately under the game's 0.3 s double-click window (Controller.CommonLoop)
    /// so touching one hex twice on purpose still produces a double click.
    /// </summary>
    private const float TouchCooldownSeconds = 0.15f;

    /// <summary>Seconds between two Info-level touch-commit lines for the same hand+hex (below it: Debug).</summary>
    private const float TouchLogIntervalSeconds = 1f;

    /// <summary>
    /// Minimum seconds between two FINGERTIP PINGS (both hands share it). The commit edge
    /// already guarantees one ping per hex ENTRY (a resting fingertip cannot repeat), so this
    /// only has to defeat the hex-BORDER case: tracking jitter flips the resolved target
    /// between two adjacent hexes, each flip re-arms, and at <see cref="TouchCooldownSeconds"/>
    /// (0.15 s — deliberately fast for clicks, it must stay inside the game's 0.3 s
    /// double-click window) that would machine-gun replicated pings at up to ~6/s. The game
    /// itself has NO ping rate limit to mirror — checked PingManager.cs: the only throttle is
    /// the IsPingShown dedupe (same element + same player while shown is a no-op over the 2 s
    /// lifetime), and a DIFFERENT element replaces the ping and re-sends
    /// <c>Synchronizer.SendSideAction(PingHex)</c> unthrottled. 0.6 s: well above any border
    /// jitter alternation, below deliberate "ping here, then there" pacing.
    /// </summary>
    private const float FingertipPingCooldownSeconds = 0.6f;

    /// <summary>
    /// Per-hand fingertip-touch arming. <see cref="Target"/> is the thing under the fingertip
    /// on the last near-pick frame (the <c>CInteractable</c> when there is one, else the raw
    /// collider) — identity only, never dereferenced, which is what makes "did the finger ENTER
    /// something new?" a reference comparison instead of a coordinate one.
    /// </summary>
    private struct NearTouch
    {
        public bool Armed;
        public Component? Target;
        public float LastCommitTime;

        /// <summary>Back to "nothing touched, ready to fire" — the state a fresh approach starts in.</summary>
        public void Clear()
        {
            Armed = true;
            Target = null;
        }
    }

    private static bool _pending;
    private static readonly NearTouch[] _near = { new() { Armed = true }, new() { Armed = true } };
    private static string _lastTouchLogKey = string.Empty;
    private static float _lastTouchLogTime = float.NegativeInfinity;
    private static float _lastFingertipPingTime = float.NegativeInfinity;

    /// <summary>Consumed by the CommonLoop postfix (once per game frame).</summary>
    public static bool ConsumePendingClick()
    {
        bool pending = _pending;
        _pending = false;
        return pending;
    }

    public static void Reset()
    {
        _pending = false;
        _near[0] = new NearTouch { Armed = true };
        _near[1] = new NearTouch { Armed = true };
        _lastTouchLogKey = string.Empty;
        _lastTouchLogTime = float.NegativeInfinity;
        _lastFingertipPingTime = float.NegativeInfinity;
    }

    /// <summary>Per-frame from <see cref="BoardDriver"/> (before the game's LateUpdate).</summary>
    public static void Tick()
    {
        _pending = false; // self-expire anything the game did not consume last frame

        switch (BoardPick.Source)
        {
            case BoardPick.PickSource.Near:
                TickNear();
                break;
            case BoardPick.PickSource.Far:
                _near[0].Clear();
                _near[1].Clear();
                TickFar();
                break;
            default:
                _near[0].Clear();
                _near[1].Clear();
                break;
        }
    }

    /// <summary>
    /// Direct fingertip touch on a board hex. See the class remarks for the grip gate, the
    /// one-commit-per-entry rule and the laser arbitration; this is the mechanism.
    /// </summary>
    private static void TickNear()
    {
        VRHand hand = BoardPick.SourceHand!;
        int index = (int)hand.Side;

        // The other hand is not touching the board — it starts its next approach armed.
        _near[1 - index].Clear();

        ref NearTouch state = ref _near[index];

        // Grip gate, re-checked at COMMIT time. BoardPick already refuses to produce a near
        // pick without the grip, so this is belt-and-braces against a future pick source —
        // but it is also the line that makes "releasing grip mid-touch does not commit" true
        // by construction rather than by chain of reasoning.
        if (!hand.GripPressed || hand.Grabber.Held != null)
        {
            state.Clear();
            return;
        }

        float scale = hand.WorldScale;
        float surface = BoardPick.NearSurfaceDistance;
        Component? target = ResolveTouchTarget();

        // ENTRY: the fingertip moved onto something else — that is a new touch, so re-arm.
        // (Leaving the board entirely lands in the Far/None branches above, which Clear().)
        if (!ReferenceEquals(target, state.Target))
        {
            state.Target = target;
            state.Armed = true;
        }

        if (state.Armed)
        {
            // Don't double-fire when the fingertip is actually pressing a registered
            // pokeable or a world-space canvas — the Poke interactor owns those.
            if (surface <= ContactDepth * scale
                && hand.Poke.Hovered == null
                && hand.Poke.HoveredUi == null
                && Time.unscaledTime - state.LastCommitTime >= TouchCooldownSeconds)
            {
                state.Armed = false;
                state.LastCommitTime = Time.unscaledTime;

                // PING or SELECT, decided per TAPPED HEX (class remarks + DecideTapCore).
                // All commit gates above (grip, contact depth, poke/UI occluders, entry edge,
                // per-hand cooldown) have already passed — only the meaning branches here.
                CClientTile? touchedTile = ResolveTouchedTile(target);
                if (touchedTile != null)
                {
                    TapVerdict verdict = DecideTap(touchedTile);
                    LogTapDecision(touchedTile, verdict);
                    if (!verdict.Select)
                    {
                        CommitFingertipPing(hand, touchedTile, verdict.Detail);
                        return;
                    }
                }

                LogTouchCommit(hand);
                RequestClick(hand, "fingertip touch");
                // Controls lesson: the "hold the grip and touch it" step. Reported HERE rather
                // than at the contact test, so only a commit that actually passed every gate
                // (grip held, depth reached, no UI in the way, cooldown clear) counts.
                Compat.ControlsProgress.Notify(Compat.ControlAction.FingertipPick);

                // P5 (MISSION A.8): poking an actor miniature additionally announces
                // the actor on the bus — WorldUI opens its world-space stat panel.
                // Verified (ilspycmd, GH.Runtime.dll): CInteractableActor.m_Actor
                // (private CActor, publicized), set in Start from CharacterManager.
                CInteractableActor? interactable = BoardPick.HitCollider != null
                    ? BoardPick.HitCollider.GetComponentInParent<CInteractableActor>()
                    : null;
                CActor? actor = interactable != null ? interactable.m_Actor : null;
                if (actor != null)
                    VREvents.Raise(new MiniaturePokedEvent(actor));
            }
        }
        else if (surface > ReleaseDepth * scale)
        {
            // Lifted off the same target — touching it AGAIN is allowed (second-click-to-confirm).
            state.Armed = true;
        }
    }

    /// <summary>
    /// The <c>CClientTile</c> the fingertip is on, or null when the touched target is not a hex
    /// tile. Tile identification mirrors the game's own click dispatch verbatim:
    /// Controller.LateUpdate resolves the interactable and asks
    /// <c>cInteractable.GetComponent&lt;TileBehaviour&gt;()</c> — the SAME GameObject, not a
    /// parent walk — so exactly the touches vanilla would treat as tile clicks go through the
    /// ping/select decision (decompiled Controller.cs:170-176). Miniatures/doors/chests resolve
    /// no TileBehaviour on their interactable and keep the plain click path.
    /// </summary>
    private static CClientTile? ResolveTouchedTile(Component? target)
    {
        TileBehaviour? tile = target is CInteractable interactable
            ? interactable.GetComponent<TileBehaviour>()
            : null;
        return tile != null ? tile.m_ClientTile : null;
    }

    // ---- PING vs SELECT, per tapped hex ------------------------------------------------

    /// <summary>Ping reasons — the exact words the <c>[Tap]</c> log line prints.</summary>
    private const string PingNoSelection = "no pending selection";
    private const string PingNotTarget = "not a valid target";
    private const string PingNotOurs = "not our character";

    /// <summary>What one fingertip tap on a hex MEANS. <see cref="Detail"/> carries the acting
    /// character for a SELECT and the ping reason for a PING — both go straight into the log
    /// line, which is the only consumer.</summary>
    private readonly struct TapVerdict
    {
        public readonly bool Select;
        public readonly string Detail;

        private TapVerdict(bool select, string detail)
        {
            Select = select;
            Detail = detail;
        }

        public static TapVerdict Selecting(string actor) => new TapVerdict(true, actor);
        public static TapVerdict Pinging(string reason) => new TapVerdict(false, reason);
    }

    /// <summary>
    /// <see cref="DecideTapCore"/> behind a hard failure floor. A throwing target test must never
    /// eat a tap: it degrades to SELECT — i.e. to the pre-2026-08 behaviour where every tap during
    /// a selection was a click — because the click then still runs the game's OWN gates
    /// (Controller.LateUpdate + TileHandler), so the worst case is a click the game itself
    /// refuses, never a wrong game action.
    /// </summary>
    private static TapVerdict DecideTap(CClientTile tile)
    {
        try
        {
            return DecideTapCore(tile);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Board", "[Tap] valid-target test threw — falling back to SELECT (the game's " +
                                $"own click gates still run, so a bad tap is merely ignored): {ex}");
            return TapVerdict.Selecting("unknown (target test threw)");
        }
    }

    /// <summary>
    /// THE DECISION TABLE. Every row is read from the GAME's own model of the pending action —
    /// the same data the game consults to accept or ignore a mouse click on that hex — never
    /// from a mod-side guess about what "should" be targetable.
    ///
    /// <list type="number">
    /// <item><b>Nothing pending</b> (<see cref="SelectionPhaseActive"/> false) → PING
    /// "<c>no pending selection</c>". This is the 2026-08 behaviour the finger already had.</item>
    ///
    /// <item><b>The game would not accept ANY board click right now</b> —
    /// <c>Choreographer.ThisPlayerHasTurnControl</c> is false → PING "<c>not our character</c>".
    /// AUTHORITATIVE because it is the literal top-level gate on the game's own click dispatch:
    /// <c>Controller.LateUpdate</c> only forwards a picked interactable to
    /// <c>SelectNewObject</c>/<c>ShowNormalInterface</c> inside
    /// <c>else if (Choreographer.s_Choreographer.ThisPlayerHasTurnControl)</c> (decompiled
    /// Controller.cs:184). With it false a click is swallowed whole, so pinging instead removes
    /// nothing and is exactly the "foreign / non-acting character" fallback the feature wants
    /// (the property is <c>m_CurrentActor.IsUnderMyControl</c> plus the mind-control/summoner
    /// derivations, Choreographer.cs:557-579; offline it is unconditionally true).</item>
    ///
    /// <item><b>A waypoint/path selection is installed</b> (<c>TileBehaviour.s_Callback</c> points
    /// at <c>Waypoint.TileHandler</c> — move, push, pull, attack-path) → valid ⇔ the hex index is
    /// in <c>Waypoint.s_ClearValidSelectionTiles</c>. AUTHORITATIVE because that list IS the
    /// acceptance test the callback runs on the clicked tile:
    /// <c>if (s_ClearValidSelectionTiles.Any(it =&gt; it.Equals(clientTile.m_Tile.m_ArrayIndex)))</c>
    /// … <c>else</c> log "was not found in the valid selection tile list" and do nothing
    /// (decompiled Waypoint.cs:775 / 921). The list is written by the star display itself while it
    /// paints the reachable hexes (WorldspaceStarHexDisplay.cs:1438-1462, 2807), so it is exactly
    /// the highlighted set — and it already folds in the straight-line / push / pull
    /// restrictions that the raw star dictionaries do not.</item>
    ///
    /// <item><b>Card-selection phase, a character stands on the tapped hex</b> — vanilla's FIRST
    /// TileHandler branch: wait state <c>WaitingForCardSelection</c> + <c>CardsHandManager.IsActive()</c>
    /// + <c>FindPlayerAt(index) != null</c> → <c>InitiativeTrack.Select</c> + <c>SwitchHand</c>,
    /// then RETURN (Choreographer.cs:1826-1839). So tapping your own figure's hex to switch to it
    /// IS a valid selection and must keep clicking. Foreign-controlled → PING
    /// "<c>not our character</c>", the same verdict the mod's own board-click ownership guard
    /// <c>Choreographer_TileHandler_OwnershipGuard</c> reaches (it refuses that exact branch), so
    /// the tap now falls back to a ping instead of a silently refused click.</item>
    ///
    /// <item><b>Hero placement</b> (<c>CurrentDisplayState == CharacterPlacement</c>) → valid ⇔
    /// <c>s_PlacementStars.ContainsKey(tile)</c>, AND the character being placed is ours. Both are
    /// the game's own predicates: the star dictionary is what <c>HighlightSelectedPlacementHex</c>
    /// arms <c>Waypoint.s_PlacementTile</c> from (WorldspaceStarHexDisplay.cs:582 — the same test
    /// <see cref="ArmPlacementTile"/> already mirrors), and TileHandler's placement branch refuses
    /// outright when <c>!InitiativeTrack.SelectedActor().Actor.IsUnderMyControl</c>
    /// (Choreographer.cs:1849).</item>
    ///
    /// <item><b>Anything else the star display is driving</b> (single/area target selection, long
    /// rest, exits) → valid ⇔ the hex carries a live star in <c>s_CurrentlyActiveStars</c>, or the
    /// game already counts it as chosen (<c>AlreadySelected(tile)</c> — the second-click-to-confirm
    /// case, and the one the AOE branch of TileHandler keys on, Choreographer.cs:1943).
    /// <c>s_CurrentlyActiveStars</c> is the game's OWN union of every star dictionary it is
    /// currently painting: <c>CreateStar</c> registers each star in it alongside its per-kind
    /// container (WorldspaceStarHexDisplay.cs:2913-2917) and the game itself queries it with
    /// <c>ContainsKey</c> to answer "does this hex already stand for something?"
    /// (<c>CanCursorHighlightTile</c> :3311, the summon target test :2613). It is deliberately the
    /// UNION and not a hand-picked subset: over-inclusion (e.g. an out-of-reach chest star) only
    /// reproduces today's behaviour — a click the game gates itself — whereas under-inclusion
    /// would ping where the player meant to select, the one regression this feature must not
    /// have. Corroboration that "no stars ⇒ no selection" is the game's own reading:
    /// TileHandler drops a USER click outright on
    /// <c>isUserClick &amp;&amp; CurrentDisplayState == ShowNone</c> (Choreographer.cs:1879).</item>
    ///
    /// <item><b>Otherwise</b> → PING "<c>not a valid target</c>".</item>
    /// </list>
    /// </summary>
    private static TapVerdict DecideTapCore(CClientTile tile)
    {
        if (!SelectionPhaseActive())
            return TapVerdict.Pinging(PingNoSelection);

        Choreographer? choreographer = Choreographer.s_Choreographer;
        if (choreographer == null || tile.m_Tile == null)
            return TapVerdict.Pinging(PingNoSelection);

        // (2) the game's own top-level gate on board clicks — see the table above.
        if (!choreographer.ThisPlayerHasTurnControl)
            return TapVerdict.Pinging(PingNotOurs);

        Point index = tile.m_Tile.m_ArrayIndex;

        // (3) waypoint / path selection: the callback's literal accept list.
        if (WaypointSelectionInstalled())
        {
            List<Point> valid = Waypoint.s_ClearValidSelectionTiles;
            if (valid != null)
            {
                for (int i = 0; i < valid.Count; i++)
                {
                    if (valid[i].X == index.X && valid[i].Y == index.Y)
                        return TapVerdict.Selecting(LabelOf(Waypoint.s_MovingActor ?? choreographer.CurrentActor));
                }
            }
            return TapVerdict.Pinging(PingNotTarget);
        }

        // (4) card-selection phase: tapping a character's hex switches to that character.
        CardsHandManager hands = CardsHandManager.Instance;
        if (choreographer.m_WaitState != null
            && choreographer.m_WaitState.m_State == Choreographer.ChoreographerStateType.WaitingForCardSelection
            && hands != null && hands.IsActive()
            && ScenarioManager.Scenario != null)
        {
            CPlayerActor? standing = ScenarioManager.Scenario.FindPlayerAt(index);
            if (standing != null)
            {
                return CardsGameApi.IsForeignControlledSelect(standing)
                    ? TapVerdict.Pinging(PingNotOurs)
                    : TapVerdict.Selecting(LabelOf(standing));
            }
        }

        WorldspaceStarHexDisplay display = WorldspaceStarHexDisplay.Instance;
        if (display == null)
            return TapVerdict.Pinging(PingNotTarget);

        // (5) hero placement.
        if (display.CurrentDisplayState == WorldspaceStarHexDisplay.WorldSpaceStarDisplayState.CharacterPlacement)
        {
            CActor? placing = CardsGameApi.SelectedActor();
            if (placing == null || CardsGameApi.IsForeignControlledSelect(placing))
                return TapVerdict.Pinging(PingNotOurs);
            return display.s_PlacementStars.ContainsKey(tile)
                ? TapVerdict.Selecting(LabelOf(placing))
                : TapVerdict.Pinging(PingNotTarget);
        }

        // (6) every other star-driven selection.
        if (display.s_CurrentlyActiveStars.ContainsKey(tile) || display.AlreadySelected(tile))
            return TapVerdict.Selecting(LabelOf(choreographer.CurrentActor));

        return TapVerdict.Pinging(PingNotTarget);
    }

    /// <summary>
    /// Is the pending selection a WAYPOINT one? Read from the game's own dispatch switch:
    /// <c>CInteractableTile.ShowNormalInterface</c> invokes whatever <c>TileBehaviour.s_Callback</c>
    /// currently holds (decompiled CInteractableTile.cs:14-47), and the Choreographer swaps that
    /// static between its own <c>TileHandler</c> and the static <c>Waypoint.TileHandler</c> as the
    /// move/push/pull/attack-path selections come and go (Choreographer.cs:4324, 4752, 9898, …).
    /// So the delegate's declaring type IS the game's answer to "which acceptance test would a
    /// click run right now?".
    /// </summary>
    private static bool WaypointSelectionInstalled()
    {
        TileBehaviour.CallbackType? callback = TileBehaviour.s_Callback;
        return callback != null
            && callback.Method != null
            && callback.Method.DeclaringType == typeof(Waypoint);
    }

    /// <summary>Log-only actor name (never per frame — one tap, one line).</summary>
    private static string LabelOf(CActor? actor) => actor != null ? CardsGameApi.ActorLabel(actor) : "?";

    /// <summary>
    /// "Is the game waiting for the player to pick a tile/target right now?" — row 1 of
    /// <see cref="DecideTapCore"/>'s table, i.e. "is there a pending selection AT ALL". Two
    /// game-owned signals, OR'd (belt and braces, each covers cases the other misses):
    ///
    /// - <see cref="VRModeStateMachine.TargetingActive"/> — the Choreographer sits in a
    ///   targeting wait state (waypoint / area-attack focus / push / pull / tile selection).
    ///   Read RAW rather than via <c>CurrentMode == BoardTargeting</c> because ModalUI masks
    ///   the mode while targeting stays live underneath.
    /// - <c>WorldspaceStarHexDisplay.CurrentDisplayState != ShowNone</c> — the game is
    ///   DISPLAYING selection stars on the board. This is what covers the phases that never
    ///   enter a TargetingStates member: hero placement and single-target attacks both wait in
    ///   <c>WaitingForCardSelection</c> (the exact trap documented on the laser-persistence
    ///   fix in VRModeStateMachine.InteractorsFor) but show CharacterPlacement /
    ///   TargetSelection stars (decompiled WorldspaceStarHexDisplay.cs:60-68).
    /// </summary>
    private static bool SelectionPhaseActive()
    {
        if (VRModeStateMachine.TargetingActive)
            return true;
        WorldspaceStarHexDisplay display = WorldspaceStarHexDisplay.Instance;
        return display != null
            && display.CurrentDisplayState != WorldspaceStarHexDisplay.WorldSpaceStarDisplayState.ShowNone;
    }

    /// <summary>
    /// ONE line per fingertip tap on a hex, stating the decision AND the reason, so the next
    /// hardware log shows the rule working without guesswork. Not throttled on purpose: the
    /// commit edge plus <see cref="TouchCooldownSeconds"/> already bound it to one line per hex
    /// ENTRY, and a rule this new is worth one line per deliberate tap.
    /// </summary>
    private static void LogTapDecision(CClientTile tile, TapVerdict verdict)
    {
        string hex = tile.m_Tile != null
            ? $"({tile.m_Tile.m_ArrayIndex.X},{tile.m_Tile.m_ArrayIndex.Y})"
            : "(?)";
        VRLog.Info("Board", verdict.Select
            ? $"[Tap] hex {hex} → SELECT (valid target for {verdict.Detail})"
            : $"[Tap] hex {hex} → PING ({verdict.Detail})");
    }

    /// <summary>
    /// Fire the ping for a fingertip tile touch: the shared <see cref="FingertipPingCooldownSeconds"/>
    /// debounce (see its doc — hex-border jitter is the enemy), then the SAME proven seam the
    /// A-press uses (<see cref="BoardPing.TryPingClientTile"/>: replicated online, local-only
    /// offline, name attached by the game itself). Haptic: the same <see cref="HapticPreset.ClickPulse"/>
    /// every other fingertip commit fires, keyed on the ping actually going out. Reached ONLY on a
    /// PING verdict, so a SELECT never puts a ping on the wire for anyone.
    /// </summary>
    private static void CommitFingertipPing(VRHand hand, CClientTile tile, string reason)
    {
        float now = Time.unscaledTime;
        if (now - _lastFingertipPingTime < FingertipPingCooldownSeconds)
        {
            VRLog.Debug("Board", $"fingertip ping swallowed ({hand.Side}) — inside the " +
                                 $"{FingertipPingCooldownSeconds:0.0}s ping cooldown (border-jitter guard).");
            return;
        }

        if (BoardPing.TryPingClientTile(tile, $"fingertip touch {hand.Side}"))
        {
            _lastFingertipPingTime = now;
            hand.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("Board", $"FINGERTIP PING: hex {DescribeTouchedHex()}, {hand.Side} hand, " +
                                $"grip HELD, {reason} (mode={VRModeStateMachine.CurrentMode}) — " +
                                "routed through the same game PingTile path as the laser A-press.");
        }
    }

    /// <summary>
    /// Identity of whatever the fingertip is over: the <c>CInteractable</c> the game itself
    /// would resolve (so every collider of one hex/miniature counts as ONE target), falling
    /// back to the raw collider when the hit carries none. Reference identity only.
    /// </summary>
    private static Component? ResolveTouchTarget()
    {
        Collider? collider = BoardPick.HitCollider;
        if (collider == null)
            return null;
        CInteractable? interactable = collider.GetComponentInParent<CInteractable>();
        if (interactable != null)
            return interactable;
        return collider;
    }

    /// <summary>
    /// The hardware-log proof of a fingertip commit: WHICH hex, WHICH hand, and that the grip
    /// really was held. Throttled to one Info line per hand+hex per
    /// <see cref="TouchLogIntervalSeconds"/> (repeats inside that window drop to Debug), so
    /// walking a finger along a row cannot turn the log into a flood.
    /// </summary>
    private static void LogTouchCommit(VRHand hand)
    {
        string hex = DescribeTouchedHex();
        string message = $"FINGERTIP TOUCH commit: hex {hex}, {hand.Side} hand, grip HELD " +
                         $"(grip={hand.GripValue:0.00}, depth={-BoardPick.NearSurfaceDistance * 1000f / Mathf.Max(0.0001f, hand.WorldScale):0}mm " +
                         $"into the surface, mode={VRModeStateMachine.CurrentMode}) — routed through the " +
                         "same click path as a laser trigger click.";

        string key = hand.Side + "|" + hex;
        float now = Time.unscaledTime;
        if (key == _lastTouchLogKey && now - _lastTouchLogTime < TouchLogIntervalSeconds)
        {
            VRLog.Debug("Board", message);
            return;
        }
        _lastTouchLogKey = key;
        _lastTouchLogTime = now;
        VRLog.Info("Board", message);
    }

    /// <summary>"(x,y)" of the touched hex, or the hit object's name when the hit is not a tile
    /// (miniatures, doors, chests — the finger commits on those exactly like the laser does).</summary>
    private static string DescribeTouchedHex()
    {
        Collider? collider = BoardPick.HitCollider;
        if (collider == null)
            return "none";
        TileBehaviour? tile = collider.GetComponentInParent<TileBehaviour>();
        if (tile != null && tile.m_ClientTile != null && tile.m_ClientTile.m_Tile != null)
            return $"({tile.m_ClientTile.m_Tile.m_ArrayIndex.X},{tile.m_ClientTile.m_Tile.m_ArrayIndex.Y})";
        return "non-tile '" + collider.name + "'";
    }

    private static void TickFar()
    {
        VRHand hand = BoardPick.SourceHand!;
        if (hand.TriggerDown
            && BoardPick.HasHit
            && hand.Grabber.Held == null
            && hand.Poke.HoveredUi == null
            // P6: while the beam is clamped to a UI surface (world panel via
            // RayUguiDriver, fan card, flat screen) the trigger belongs to that
            // surface — nearest UI hit wins over the board pick.
            && !hand.Ray.HasFreshUiHit)
        {
            ArmPlacementTile();
            RequestClick(hand, "trigger");
            // Controls lesson: the "point and pull the trigger" step.
            Compat.ControlsProgress.Notify(Compat.ControlAction.LaserClick);
        }
    }

    /// <summary>
    /// Test #16 — deterministic placement arming. The vanilla flow arms
    /// <c>Waypoint.s_PlacementTile</c> only in
    /// <c>WorldspaceStarHexDisplay.HighlightSelectedPlacementHex</c>, which (a) runs
    /// only when the pointed-at tile CHANGES and (b) clears the armed tile first and
    /// early-outs on any hover hiccup (WorldspaceStarHexDisplay.cs:551/554/560/582/588)
    /// — with hand jitter between adjacent hexes the armed tile flip-flopped between
    /// null and a tile at frame rate, and <c>Choreographer.TileHandler</c>'s placement
    /// branch (<c>clientTile == Waypoint.s_PlacementTile</c>, Choreographer.cs:1841)
    /// rejected most clicks ("tile=(10,10), armed=null → will NOT place").
    ///
    /// So at CLICK time, if the clicked tile qualifies under the game's OWN arming
    /// predicates (mirrored 1:1 from HighlightSelectedPlacementHex — starred tile
    /// :582, unoccupied :588 — plus TileHandler's selected-actor requirement :1841),
    /// arm it directly. This only mirrors the state a stable hover would have set;
    /// TileHandler still runs every placement validation itself — no rule bypassed.
    /// </summary>
    private static void ArmPlacementTile()
    {
        Choreographer? choreo = Choreographer.s_Choreographer;
        if (choreo == null || choreo.m_WaitState == null
            || choreo.m_WaitState.m_State != Choreographer.ChoreographerStateType.WaitingForCardSelection)
            return;

        WorldspaceStarHexDisplay display = WorldspaceStarHexDisplay.Instance;
        if (display == null
            || display.CurrentDisplayState != WorldspaceStarHexDisplay.WorldSpaceStarDisplayState.CharacterPlacement)
            return;

        // Resolve the clicked tile exactly like the game's own pick does (patched MF
        // → GetComponentInParent<CInteractable>, then TileBehaviour on the same
        // object — WorldspaceStarHexDisplay.cs:559).
        CInteractable? interactable = BoardPick.HitCollider != null
            ? BoardPick.HitCollider.GetComponentInParent<CInteractable>()
            : null;
        TileBehaviour? tileBehaviour = interactable != null ? interactable.GetComponent<TileBehaviour>() : null;
        CClientTile? tile = tileBehaviour != null ? tileBehaviour.m_ClientTile : null;
        if (tile == null || tile.m_Tile == null)
            return;

        // The game's own arming predicates (see doc comment above).
        if (!display.s_PlacementStars.ContainsKey(tile))
            return;
        if (ScenarioManager.Scenario == null
            || ScenarioManager.Scenario.FindActorAt(tile.m_Tile.m_ArrayIndex) != null)
            return;
        if (InitiativeTrack.Instance == null || InitiativeTrack.Instance.SelectedActor() == null)
            return;

        if (!ReferenceEquals(Waypoint.s_PlacementTile, tile))
        {
            Waypoint.s_PlacementTile = tile;
            VRLog.Info("Board", "[Placement] click-arm: s_PlacementTile ← " +
                                $"({tile.m_Tile.m_ArrayIndex.X},{tile.m_Tile.m_ArrayIndex.Y}) " +
                                "(hover refresh had not armed the clicked tile).");
        }
    }

    private static void RequestClick(VRHand hand, string kind)
    {
        // COMMIT layer only (user ruling 2026-08: beam/collision/hover are never gated —
        // BoardPick stays live under every modal). Board clicks are ALLOWED under ordinary
        // blocking modals (story / level messages / dialogs / rewards): the injection point
        // is Controller.CommonLoop, so the game's OWN LateUpdate gating runs in full
        // (InteractabilityManager, ThisPlayerHasTurnControl, tutorial isolation; story and
        // error blockers stall processing via UpdateBlocker) — a stray click self-gates.
        // The ONE exception is the hard lock (results screens / error box), where vanilla
        // makes such clicks physically impossible (full-screen blocker →
        // s_StartedButtonDownInGUI) and our injection clears exactly that flag — see the
        // decision table on WorldUI.ModalFallback.HardCommitLockActive.
        if (GloomhavenVR.WorldUI.ModalFallback.HardCommitLockActive)
        {
            VRLog.Info("Board", $"click SUPPRESSED ({kind}, {hand.Side}) — hard commit lock " +
                                "(results/error family modal open); beam+hover stay live.");
            return;
        }
        _pending = true;
        hand.SendHaptic(HapticPreset.ClickPulse);
        // Test #13 diagnostics: the mode matters — hero placement commits in
        // CardSelection (Choreographer.TileHandler placement branch), not only in
        // BoardTargeting. Event-driven, so the interpolation never runs per frame.
        VRLog.Debug("Board", $"click requested ({kind}, {hand.Side}, " +
                             $"mode={VRModeStateMachine.CurrentMode}).");
    }
}

/// <summary>
/// Injects the VR click into the game's click detection.
///
/// Verified against the REAL GH.Runtime.dll (v1.1.8307.0) with ilspycmd 8.2 (2026-07-15):
/// <code>
///   // Controller.cs:243 — IL 256 B (PATCH-TARGETS §1.1 ✅, only caller: Controller.LateUpdate)
///   private bool CommonLoop(bool isPaused)
///   // outputs (all verified):
///   public static bool s_SingleClicked;              // Controller.cs:34
///   public static bool s_DoubleClicked;              // Controller.cs:36
///   public static bool s_StartedButtonDownInGUI;     // Controller.cs:38
///   private float m_DoubleClickStart;                // Controller.cs:54 (publicized ref access)
///   // timing source (Main.cs:11): public static float s_NonPausedTime;
///   // tail:  if ((s_SingleClicked || s_DoubleClicked || s_ShouldPing) &amp;&amp; !s_StartedButtonDownInGUI)
///   //        { if (isPaused) return !TimeManager.IsPaused; return true; }  return false;
///   // TimeManager.cs:48: public static bool IsPaused =&gt; s_CurrentState.HasFlag(TimeStates.Paused);
/// </code>
/// The double-click block below is a verbatim replication of CommonLoop's own
/// release branch (0.3 s window against m_DoubleClickStart), so rapid double
/// trigger-presses/pokes get the game's double-click semantics.
/// </summary>
[HarmonyPatch(typeof(Controller), "CommonLoop")]
internal static class Controller_CommonLoop_Patch
{
    private static void Postfix(Controller __instance, bool isPaused, ref bool __result)
    {
        // Always consume, so a pending click can never leak into a later frame.
        if (!BoardClickDriver.ConsumePendingClick())
            return;
        if (__result)
            return; // a real mouse click already fired this frame — it wins

        Controller.s_StartedButtonDownInGUI = false;
        Controller.s_SingleClicked = true;

        // Verbatim CommonLoop double-click bookkeeping:
        if (Main.s_NonPausedTime - __instance.m_DoubleClickStart < 0.3f)
        {
            Controller.s_DoubleClicked = true;
            __instance.m_DoubleClickStart = Main.s_NonPausedTime - 0.3f;
        }
        else
        {
            __instance.m_DoubleClickStart = Main.s_NonPausedTime;
        }

        // Verbatim CommonLoop tail:
        __result = !isPaused || !TimeManager.IsPaused;
    }
}
