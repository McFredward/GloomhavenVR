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
/// FINGERTIP PING OUTSIDE SELECTION PHASES (user request 2026-08: "wenn KEINE Auswahlphase
/// ist ... soll es dort pingen"). The SAME gesture and the SAME commit mechanics (grip gate,
/// entry edge, cooldown, occluder/poke guards) — only the commit's MEANING branches at the
/// last moment: when the touched target is a hex TILE and no selection phase is active
/// (<see cref="SelectionPhaseActive"/>), the touch fires the game's own ping through
/// <see cref="BoardPing.TryPingClientTile"/> INSTEAD of the click. During any selection
/// phase the click path below runs untouched, byte-identical to before. Non-tile touches
/// (miniatures, doors, chests) always keep the click + <see cref="MiniaturePokedEvent"/> —
/// the user asked for tile pings only, and poking a miniature must keep opening its panel.
/// Vanilla precedent for "same click, different meaning by state": Controller.LateUpdate
/// itself branches the very same tile click into PingTile when s_ShouldPing is set
/// (decompiled Controller.cs:178-183) — we branch on phase instead of a gamepad combo.
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

                // Outside a selection phase a TILE touch means PING, not click (class remarks).
                // All commit gates above (grip, contact depth, poke/UI occluders, entry edge,
                // per-hand cooldown) have already passed — only the meaning branches here.
                CClientTile? pingTile = ResolveFingertipPingTile(target);
                if (pingTile != null)
                {
                    CommitFingertipPing(hand, pingTile);
                    return;
                }

                LogTouchCommit(hand);
                RequestClick(hand, "fingertip touch");

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
    /// The <c>CClientTile</c> to PING for this fingertip commit — or null when the commit must
    /// stay a CLICK. Null when (a) a selection phase is active (today's behaviour, untouched)
    /// or (b) the touched target is not a hex tile. Tile identification mirrors the game's own
    /// click dispatch verbatim: Controller.LateUpdate resolves the interactable and asks
    /// <c>cInteractable.GetComponent&lt;TileBehaviour&gt;()</c> — the SAME GameObject, not a
    /// parent walk — so exactly the touches vanilla would treat as tile clicks become pings
    /// (decompiled Controller.cs:170-176). Miniatures/doors/chests resolve no TileBehaviour
    /// on their interactable and keep the click path.
    /// </summary>
    private static CClientTile? ResolveFingertipPingTile(Component? target)
    {
        if (SelectionPhaseActive())
            return null;
        TileBehaviour? tile = target is CInteractable interactable
            ? interactable.GetComponent<TileBehaviour>()
            : null;
        return tile != null ? tile.m_ClientTile : null;
    }

    /// <summary>
    /// "Is the game waiting for the player to pick a tile/target right now?" — the gate that
    /// keeps the fingertip's SELECTION meaning exactly as it is today. Two game-owned signals,
    /// OR'd (belt and braces, each covers cases the other misses):
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
    /// Fire the ping for a fingertip tile touch: the shared <see cref="FingertipPingCooldownSeconds"/>
    /// debounce (see its doc — hex-border jitter is the enemy), then the SAME proven seam the
    /// A-press uses (<see cref="BoardPing.TryPingClientTile"/>: replicated online, local-only
    /// offline, name attached by the game itself). Haptic: the same <see cref="HapticPreset.ClickPulse"/>
    /// every other fingertip commit fires, keyed on the ping actually going out.
    /// </summary>
    private static void CommitFingertipPing(VRHand hand, CClientTile tile)
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
                                $"grip HELD, no selection phase (mode={VRModeStateMachine.CurrentMode}) — " +
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
