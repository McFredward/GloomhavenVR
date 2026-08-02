using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Board;

/// <summary>
/// Click commit (Phase 3a): trigger-press (far mode) / fingertip poke-touch (near
/// mode) while a pick target exists → one game click, injected at the game's OWN
/// click-detection point so everything downstream (double-click semantics, second-
/// click-to-confirm, undo, MP replication) is byte-identical to a mouse click.
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

    private static bool _pending;
    private static bool _armedLeft = true;
    private static bool _armedRight = true;

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
        _armedLeft = true;
        _armedRight = true;
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
                _armedLeft = _armedRight = true;
                TickFar();
                break;
            default:
                _armedLeft = _armedRight = true;
                break;
        }
    }

    private static void TickNear()
    {
        VRHand hand = BoardPick.SourceHand!;
        ref bool armed = ref (hand.Side == HandSide.Left ? ref _armedLeft : ref _armedRight);

        // The other hand is not touching the board — keep it armed.
        if (hand.Side == HandSide.Left)
            _armedRight = true;
        else
            _armedLeft = true;

        float scale = hand.WorldScale;
        float surface = BoardPick.NearSurfaceDistance;

        if (armed)
        {
            // Don't double-fire when the fingertip is actually pressing a registered
            // pokeable or a world-space canvas — the Poke interactor owns those.
            if (surface <= ContactDepth * scale
                && hand.Poke.Hovered == null
                && hand.Poke.HoveredUi == null)
            {
                armed = false;
                RequestClick(hand, "near touch");

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
            armed = true;
        }
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
