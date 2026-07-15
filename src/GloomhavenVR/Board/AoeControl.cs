using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// AoE pattern rotation (Phase 3a): while the mode machine is in BoardTargeting and
/// a ranged AoE pattern is active on <c>WorldspaceStarHexDisplay</c>, a horizontal
/// thumbstick flick on the primary hand rotates the pattern one 60° step
/// (right = clockwise), with a haptic tick per step and hold-to-repeat.
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
    /// <summary>Stick must return below this before a new flick step can fire.</summary>
    private const float RearmThreshold = 0.3f;

    private static bool _armed = true;
    private static float _nextRepeatTime;

    public static void Reset()
    {
        _armed = true;
        _nextRepeatTime = 0f;
    }

    /// <summary>Per-frame from <see cref="BoardDriver"/>.</summary>
    public static void Tick()
    {
        if (VRModeStateMachine.CurrentMode != VRMode.BoardTargeting)
        {
            _armed = true;
            return;
        }

        VRHand? hand = VRHands.Primary;
        if (hand == null || !hand.HasPose)
            return;

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

        WorldspaceStarHexDisplay? display = WorldspaceStarHexDisplay.Instance;
        if (display == null || !CanRotate(display))
            return;

        display.RotateAOEClockwise(turnRight: x > 0f);
        RefreshStars(display);
        hand.SendHaptic(HapticPreset.HoverTick);
        Core.VRLog.Debug("Board", $"AoE rotated {(x > 0f ? "CW" : "CCW")} -> {display.AreaEffectAngle}°.");
    }

    /// <summary>Mirrors the gates around the game's own keyboard-rotate path (WSHD.Update :425-482).</summary>
    private static bool CanRotate(WorldspaceStarHexDisplay display)
    {
        Choreographer? choreographer = Choreographer.s_Choreographer;
        if (choreographer == null || !choreographer.ThisPlayerHasTurnControl)
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
