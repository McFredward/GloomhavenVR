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

    /// <summary>Fingertip must retract past this (real meters) to re-arm the near click.</summary>
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
            RequestClick(hand, "trigger");
        }
    }

    private static void RequestClick(VRHand hand, string kind)
    {
        _pending = true;
        hand.SendHaptic(HapticPreset.ClickPulse);
        VRLog.Debug("Board", $"click requested ({kind}, {hand.Side}).");
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
