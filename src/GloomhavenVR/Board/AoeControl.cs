using System;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// AoE pattern rotation: while native targeting accepts rotation and
/// a ranged AoE pattern is active on <c>WorldspaceStarHexDisplay</c>, short B/Y releases
/// rotate one 60° step (B = clockwise), leaving all locomotion axes available. The
/// optional legacy stick binding supports a horizontal flick and hold-to-repeat.
///
/// <para>OPTIONAL LEGACY STICK — AND WHY IT IS NOT THE PRIMARY HAND. TURN NEVER (user, hardware
/// ModBuild 138: "Die drehung soll nie blockiert sein!"). This class used to read
/// <c>VRHands.Primary</c>, which under the shipped defaults is the SAME controller
/// <c>[Comfort] TurnHand</c> turns with (both Right) — so the two really did contend for one
/// physical x axis, and the project answered that contention by taking turning away
/// (<c>SnapTurn</c>) and strafe with it (<c>Flight</c>). The ruling above removes the option of
/// answering it at all: turning may not be stood down, ever, for any board state. The only fix
/// that satisfies it WITHOUT deleting a game control the user has never complained about is to
/// stop sharing the axis — so pattern rotation moves to the hand that is NOT bound to turning
/// (<see cref="ResolveRotationHand"/>). Turning is then always live on its own stick, rotation
/// always live on the other, and there is no arbitration left to get wrong.</para>
///
/// <para>WHAT THIS CLASS OWES THE ARBITRATION IT LEFT: an honest answer to "are you really
/// reading a stick this frame". <see cref="WouldRotate"/> / <see cref="ClaimsStick"/> compute
/// exactly the gates <see cref="Tick"/> applies, so <c>LocalTurnControl</c> can ask the CONSUMER
/// instead of guessing from <c>VRMode</c>. The mode was always the wrong question: it is derived
/// from the shared Choreographer wait-state and is true through movement/waypoint selection,
/// where <see cref="CanRotate"/> refuses to rotate anything at all (display state is
/// <c>MovementSelection</c>, not <c>TargetSelection</c>) — i.e. the old suppression took the
/// stick away for a consumer that had already declined it.</para>
///
/// Verified against the REAL GH.Runtime.dll (v1.1.8307.0) with ilspycmd 8.2 (2026-07-15):
/// <code>
///   // WorldspaceStarHexDisplay (global namespace):
///   public static WorldspaceStarHexDisplay Instance;                       // :111
///   public WorldSpaceStarDisplayState CurrentDisplayState { get; set; }    // :261
///   public EAbilityDisplayType CurrentAbilityDisplayType { get; private set; } // :259
///   public int AbilityRange { get; }        // :245 — 0 when no saved ability
///   public void RotateAOEClockwise(bool turnRight)                         // :2398, IL 319 B
///   public void DisplayAOEStars(CClientTile originTile = null)             // :2083
///   public void DisplaySelectObjectPositionAOEStars(CClientTile originTile = null) // :2238
///   public enum WorldSpaceStarDisplayState { ShowNone, CharacterPlacement,
///       MovementSelection, TargetSelection, LongResting, LevelEditorSpawning }
///   public enum EAbilityDisplayType { None, Normal, AreaOfEffect, EnemyAreaOfEffect,
///       SelectObjectPosition, TargetingAbility, NegativeAbility, SelectPath,
///       SelectObjectPositionAreaOfEffect, RedistributeDamageAbility, ObjectiveAbility }
///   // gates mirrored from WorldspaceStarHexDisplay.Update/ListenForTargetingInputEvents:
///   private bool m_HexDisplayToggledOff;    // :227 (publicized read)
///   private bool m_AllHexesHighlighted;     // :203 (publicized read)
///   public bool LockView { get; set; }      // :257
///   // Choreographer.cs: public static Choreographer s_Choreographer; (:99)
///   //   public CWaitState m_WaitState; (:218, .m_State) · public bool ThisPlayerHasTurnControl (:557)
/// </code>
///
/// The keyboard path the game uses (<c>ListenForTargetingInputEvents</c> →
/// <c>RotateAOEWithKeyboard</c> → <c>RotateAOEClockwise</c>) only redraws the
/// stars because its return value triggers <c>DisplayAOEStars()</c> in Update —
/// so after rotating we call the same redraw the game would, matched on
/// <c>CurrentAbilityDisplayType</c>. Melee AoE (Range &lt;= 1) is intentionally NOT
/// handled here: its facing follows the hovered tile (<c>RotateAOEWithMouse</c>),
/// which the picking patches already drive.
///
/// Note: <c>RotateAOEClockwise</c> latches the turn direction for 0.3 s
/// (m_LastRecievedRotateDirection) — the repeat interval config is clamped to
/// ≥ 0.3 s so direction reversals always take effect.
/// </summary>
internal static class AoeControl
{
    // User ruling, 2026-09-22: both sticks must remain available for locomotion by default.
    // Upper face buttons are otherwise used only by the two-button recenter chord. Release
    // recognition below distinguishes a short single tap from that chord and from long holds.
    private static readonly AoeFaceButtonGesture FaceButtons = new();
    internal static bool UsesStick => BoardConfig.AoeRotationInput != null
        && BoardConfig.AoeRotationInput.Value == AoeRotationInputMode.OppositeTurnStick;

    /// <summary>Stick must return below this before a new flick step can fire.</summary>
    private const float RearmThreshold = 0.3f;

    private static bool _armed = true;
    private static float _nextRepeatTime;

    /// <summary>
    /// Which physical stick the last resolution landed on, so a change of hand can re-arm the
    /// flick latch. Same pattern (and same reason) as <c>SnapTurn._gateHand</c>: the latch
    /// describes ONE stick, and a stick left deflected on the controller we just stopped reading
    /// would otherwise fire a stale step the moment the other one is picked up.
    /// </summary>
    private static HandSide? _stickHand;

    /// <summary>One-shot guard so a throwing predicate cannot flood the log every frame.</summary>
    private static bool _loggedClaimThrow;

    public static void Reset()
    {
        _armed = true;
        _nextRepeatTime = 0f;
    }

    /// <summary>
    /// The hand whose thumbstick rotates the pattern, or null while hands are down.
    ///
    /// <para>THE HAND THAT IS NOT TURNING. <c>[Comfort] TurnHand</c> (default Right, Dominant
    /// follows <c>[Hands] PrimaryHand</c>) names the turn stick; rotation takes the other one, so
    /// the two controls can never contend and turning can never be suppressed for this one.</para>
    ///
    /// <para>EXCEPT WHEN TURNING IS OFF, where the turn stick is free and there is nothing to
    /// avoid — then rotation keeps the primary hand, which is where it has always been and where
    /// a player who has switched turning off will look for it. Same fallback while
    /// <c>ComfortSettings</c> is not yet bound: before the config exists there is no TurnHand to
    /// dodge, and answering "primary" is the pre-existing behaviour.</para>
    /// </summary>
    internal static VRHand? ResolveRotationHand()
        => VRHands.Get(ResolveRotationSide());

    // Tutorial wording must resolve the same physical side even before tracked hands exist.
    internal static HandSide ResolveRotationSide()
    {
        if (!ComfortSettings.IsBound || ComfortSettings.Turn.Value == TurnMode.Off)
            return VRHands.Primary != null ? VRHands.Primary.Side : HandSide.Right;

        HandSide turn = LocalTurnControl.Resolve(ComfortSettings.TurnHand.Value);
        return turn == HandSide.Left ? HandSide.Right : HandSide.Left;
    }

    // Native PlayerSelectingObjectPosition and ActorIsSelectingDamageFocus can leave the
    // choreography in Play while WorldspaceStarHexDisplay is already TargetSelection. Requiring
    // the derived BoardTargeting mode silently removed rotation for these valid effects. Native
    // display/authority gates below decide eligibility; mod menus and selection still block it.
    private static bool ModeAllowsRotation => VRModeStateMachine.CurrentMode != VRMode.Menu2D
        && VRModeStateMachine.CurrentMode != VRMode.ModalUI
        && VRModeStateMachine.CurrentMode != VRMode.CardSelection;

    /// <summary>
    /// Would a stick flick REALLY rotate something this frame? The honest predicate the locomotion
    /// arbitration asks instead of testing <c>VRMode</c> — see the class doc for why the mode was
    /// the wrong question. It is exactly the pair of tests <see cref="Tick"/> applies before it
    /// calls <c>RotateAOEClockwise</c>, minus the stick reading itself.
    ///
    /// <para>Fails to "no claim" on any throw or null. A predicate whose only power is to TAKE A
    /// COMFORT CONTROL AWAY must answer "no" when it does not know — the same fail-open rule
    /// <c>LocalTurnControl.ThisSeatActs</c> documents.</para>
    /// </summary>
    internal static bool WouldRotate
    {
        get
        {
            try
            {
                if (!ModeAllowsRotation)
                    return false;
                // Unity-null: a destroyed display compares equal to null.
                WorldspaceStarHexDisplay? display = WorldspaceStarHexDisplay.Instance;
                if (display == null)
                    return false;
                bool would = CanRotate(display);
                _loggedClaimThrow = false;
                return would;
            }
            catch (Exception ex)
            {
                if (!_loggedClaimThrow)
                {
                    _loggedClaimThrow = true;
                    VRLog.Error("Board", "AoE stick claim: the rotate predicate threw — answering " +
                                         $"\"no claim\", so no locomotion control can be lost to it. {ex}");
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Does a REAL rotation claim sit on THIS physical stick right now? The side test comes first
    /// because it is two static reads and settles the overwhelmingly common case (rotation and the
    /// asking control are on different controllers) without touching game state at all.
    /// </summary>
    internal static bool ClaimsStick(HandSide side)
    {
        if (!UsesStick) return false;
        VRHand? hand = ResolveRotationHand();
        return hand != null && hand.HasPose && !hand.ThumbstickClick
            && hand.Side == side && WouldRotate;
    }

    /// <summary>Per-frame from <see cref="BoardDriver"/>.</summary>
    public static void Tick()
    {
        WorldspaceStarHexDisplay? buttonDisplay = WorldspaceStarHexDisplay.Instance;
        bool buttonsEligible = !UsesStick && ModeAllowsRotation
            && buttonDisplay != null && CanRotate(buttonDisplay);
        VRHand? left = VRHands.Left, right = VRHands.Right;
        int step = FaceButtons.Tick(left != null && left.SecondaryButton,
            right != null && right.SecondaryButton,
            buttonsEligible && left != null && left.HasPose,
            buttonsEligible && right != null && right.HasPose,
            buttonDisplay?.m_SavedAbility, Time.unscaledTime);
        if (!UsesStick)
        {
            Reset();
            if (step != 0 && buttonDisplay != null)
            {
                // Native keyboard input only rotates clockwise, and its 0.3-second direction
                // latch otherwise reverses a quick B/Y alternation. This is preview input state,
                // not ability/gameplay state: select the explicit button direction before using
                // the same native rotation/redraw and eventual TargetSelectionToken as flat.
                buttonDisplay.m_TurningRight = step > 0;
                buttonDisplay.RotateAOEClockwise(turnRight: step > 0);
                RefreshStars(buttonDisplay);
                (step > 0 ? right : left)?.SendHaptic(HapticPreset.HoverTick);
            }
            return;
        }
        VRHand? hand = ResolveRotationHand();
        if (hand != null && _stickHand != hand.Side)
        {
            // Edge-only, so the log answers "which stick rotates the pattern" once per change
            // rather than per frame. It changes when the player edits [Comfort] TurnHand or
            // TurnMode live in the in-VR options list, which is not a hypothetical path.
            _stickHand = hand.Side;
            Reset(); // the flick latch belonged to the OTHER stick — see _stickHand
            string turnMode = ComfortSettings.IsBound ? ComfortSettings.Turn.Value.ToString() : "unbound";
            string turnHand = ComfortSettings.IsBound ? ComfortSettings.TurnHand.Value.ToString() : "unbound";
            VRLog.Info("Board", $"AoE rotation reads the {hand.Side} thumbstick ([Comfort] " +
                                $"TurnMode={turnMode}, TurnHand={turnHand}) — the turn stick is " +
                                "deliberately left alone (TURN NEVER, user ModBuild 138).");
        }

        if (!ModeAllowsRotation)
        {
            _armed = true;
            return;
        }

        if (hand == null || !hand.HasPose || hand.ThumbstickClick)
            return;

        WorldspaceStarHexDisplay? display = WorldspaceStarHexDisplay.Instance;
        if (display == null || !CanRotate(display))
        {
            Reset();
            return;
        }

        float x = hand.Thumbstick.x;
        float threshold = Mathf.Clamp(BoardConfig.AoeFlickThreshold.Value, 0.2f, 0.95f);

        if (Mathf.Abs(x) < RearmThreshold)
        {
            _armed = true;
            return;
        }
        if (Mathf.Abs(x) < threshold)
            return;

        float repeat = Mathf.Max(0.3f, BoardConfig.AoeRepeatInterval.Value);
        bool fire;
        if (_armed)
        {
            fire = true;
            _armed = false;
        }
        else
        {
            fire = Time.unscaledTime >= _nextRepeatTime;
        }
        if (!fire)
            return;
        _nextRepeatTime = Time.unscaledTime + repeat;

        display.RotateAOEClockwise(turnRight: x > 0f);
        RefreshStars(display);
        hand.SendHaptic(HapticPreset.HoverTick);
        VRLog.Debug("Board", $"AoE rotated {(x > 0f ? "CW" : "CCW")} -> {display.AreaEffectAngle}°.");
    }

    /// <summary>Mirrors the gates around the game's own keyboard-rotate path (WSHD.Update :425-482).</summary>
    private static bool CanRotate(WorldspaceStarHexDisplay display)
    {
        // ONE authority for "is this seat the acting seat". LocalTurnControl.ThisSeatActs wraps the
        // very Choreographer.ThisPlayerHasTurnControl this line always read (null Choreographer ->
        // false, the property throwing -> false). Asking it here rather than re-expressing it keeps
        // the consumer and the arbitration that quotes the consumer from ever parting company —
        // which is the failure LocalTurnControl's own doc calls out as "how a gate ends up
        // disagreeing with the thing it gates".
        if (!LocalTurnControl.ThisSeatActs)
            return false;
        Choreographer? choreographer = Choreographer.s_Choreographer;
        if (choreographer == null)
            return false;
        if (TimeManager.IsPaused)
            return false;
        if (choreographer.m_WaitState.m_State == Choreographer.ChoreographerStateType.WaitingForTileSelected)
            return false;
        if (display.CurrentDisplayState != WorldspaceStarHexDisplay.WorldSpaceStarDisplayState.TargetSelection)
            return false;
        if (display.m_HexDisplayToggledOff || display.m_AllHexesHighlighted || display.LockView)
            return false;

        WorldspaceStarHexDisplay.EAbilityDisplayType type = display.CurrentAbilityDisplayType;
        if (type != WorldspaceStarHexDisplay.EAbilityDisplayType.AreaOfEffect
            && type != WorldspaceStarHexDisplay.EAbilityDisplayType.SelectObjectPositionAreaOfEffect)
        {
            return false;
        }

        // Ranged AoE only — melee facing (Range <= 1) follows the hovered tile.
        return display.AbilityRange > 1;
    }

    /// <summary>Same redraw the game's Update performs after a keyboard rotate.</summary>
    private static void RefreshStars(WorldspaceStarHexDisplay display)
    {
        switch (display.CurrentAbilityDisplayType)
        {
            case WorldspaceStarHexDisplay.EAbilityDisplayType.AreaOfEffect:
                display.DisplayAOEStars();
                break;
            case WorldspaceStarHexDisplay.EAbilityDisplayType.SelectObjectPositionAreaOfEffect:
                display.DisplaySelectObjectPositionAOEStars();
                break;
        }
    }
}
