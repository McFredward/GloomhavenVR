using GloomhavenVR.Board;
using GloomhavenVR.Compat;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using UnityEngine;

static class Program
{
    static int count;
    static void Check(bool value, string message)
    {
        count++;
        if (!value) throw new Exception(message);
    }
    static WorldspaceStarHexDisplay Fresh()
    {
        AoeControl.Reset();
        Time.unscaledTime += 2;
        VRModeStateMachine.CurrentMode = VRMode.BoardTargeting;
        LocalTurnControl.ThisSeatActs = true;
        Choreographer.s_Choreographer = new();
        TimeManager.IsPaused = false;
        ComfortSettings.IsBound = true;
        ComfortSettings.Turn.Value = TurnMode.Snap;
        ComfortSettings.TurnHand.Value = TurnHandChoice.Right;
        VRHands.Primary = VRHands.Right;
        foreach (var h in new[] { VRHands.Left, VRHands.Right })
        { h.Thumbstick = (0, 0); h.HasPose = true; h.ThumbstickClick = false; }
        return WorldspaceStarHexDisplay.Instance = new();
    }
    static void Blocked(Action change, string label)
    {
        var display = Fresh(); change(); VRHands.Left.Thumbstick = (1, 0);
        Check(!AoeControl.ClaimsStick(HandSide.Left), label + " must not claim flight axis");
        AoeControl.Tick(); Check(display.Calls == 0, label + " must not rotate");
    }
    static void Main()
    {
        foreach (var main in new[] { HandSide.Left, HandSide.Right })
        foreach (var turn in Enum.GetValues<TurnHandChoice>())
        foreach (var mode in Enum.GetValues<TurnMode>())
        {
            Fresh(); VRHands.Primary = VRHands.Get(main);
            ComfortSettings.Turn.Value = mode; ComfortSettings.TurnHand.Value = turn;
            var expected = mode == TurnMode.Off ? main
                : LocalTurnControl.Resolve(turn) == HandSide.Left ? HandSide.Right : HandSide.Left;
            Check(AoeControl.ResolveRotationSide() == expected, "Binding must follow turn configuration and handedness");
            foreach (var key in new[] { "SCENARIO_PUZZLE_5B_08", "SCENARIO_PUZZLE_5B_ROTATE_MESSAGE",
                         "Consoles/SCENARIO_PUZZLE_5B_ROTATE_MESSAGE" })
            {
                foreach (bool controller in new[] { false, true })
                {
                    Check(TutorialAoeHint.TryOverride(controller ? null : key, controller ? key : null, out var text),
                        "Both native hint keys must resolve with blank or missing gamepad glyphs");
                    Check(text == (expected == HandSide.Left ? "tut_vr_aoe_left" : "tut_vr_aoe_right"),
                        "Tutorial must name the same physical stick as rotation input");
                }
            }
        }
        foreach (string key in new[] { "SCENARIO_PUZZLE_5B_07", "OTHER_ROTATE_MESSAGE", "TUTORIAL_2_TEXT_003" })
            Check(!TutorialAoeHint.TryOverride(key, null, out _), "Unrelated narrative and camera hints must remain intact");
        foreach (var mode in new[] { VRMode.BoardTargeting, VRMode.HalfSelection, VRMode.TableIdle })
        foreach (var type in new[] { WorldspaceStarHexDisplay.EAbilityDisplayType.AreaOfEffect,
                     WorldspaceStarHexDisplay.EAbilityDisplayType.SelectObjectPositionAreaOfEffect })
        {
            var display = Fresh(); VRModeStateMachine.CurrentMode = mode; display.CurrentAbilityDisplayType = type;
            VRHands.Left.Thumbstick = (1, 0);
            Check(AoeControl.ClaimsStick(HandSide.Left), "Native effect targeting in Play must claim its stick");
            Check(!AoeControl.ClaimsStick(HandSide.Right), "Turning stick must always remain free");
            AoeControl.Tick(); Check(display.Calls == 1 && display.AreaEffectAngle == 300, "First flick must rotate native pattern immediately");
            Check(type == WorldspaceStarHexDisplay.EAbilityDisplayType.AreaOfEffect ? display.Attacks == 1 : display.Objects == 1,
                "Each effect type must redraw its own native stars");
            AoeControl.Tick(); Check(display.Calls == 1, "Holding a stick must not rotate every frame");
            Time.unscaledTime += .5f; AoeControl.Tick(); Check(display.Calls == 2, "Held stick repeats after interval");
            VRHands.Left.Thumbstick = (0, 0); AoeControl.Tick(); Time.unscaledTime += .5f;
            VRHands.Left.Thumbstick = (-1, 0); AoeControl.Tick();
            Check(display.Calls == 3 && display.AreaEffectAngle == 300, "Left flick rotates back counterclockwise");
        }
        foreach (var mode in new[] { VRMode.Menu2D, VRMode.ModalUI, VRMode.CardSelection })
            Blocked(() => VRModeStateMachine.CurrentMode = mode, mode.ToString());
        Blocked(() => LocalTurnControl.ThisSeatActs = false, "Other player's turn");
        Blocked(() => Choreographer.s_Choreographer = null, "No scenario");
        Blocked(() => TimeManager.IsPaused = true, "Paused");
        Blocked(() => Choreographer.s_Choreographer!.m_WaitState.m_State = Choreographer.ChoreographerStateType.WaitingForTileSelected, "Native tile-selection exclusion");
        Blocked(() => WorldspaceStarHexDisplay.Instance = null, "Missing native display");
        Blocked(() => WorldspaceStarHexDisplay.Instance!.AbilityRange = 1, "Melee uses hover facing");
        Blocked(() => WorldspaceStarHexDisplay.Instance!.CurrentDisplayState = WorldspaceStarHexDisplay.WorldSpaceStarDisplayState.MovementSelection, "Movement");
        Blocked(() => WorldspaceStarHexDisplay.Instance!.CurrentAbilityDisplayType = WorldspaceStarHexDisplay.EAbilityDisplayType.EnemyAreaOfEffect, "Enemy preview");
        Blocked(() => WorldspaceStarHexDisplay.Instance!.m_HexDisplayToggledOff = true, "Hidden stars");
        Blocked(() => WorldspaceStarHexDisplay.Instance!.m_AllHexesHighlighted = true, "All-hex highlight");
        Blocked(() => WorldspaceStarHexDisplay.Instance!.LockView = true, "Locked native view");
        Blocked(() => VRHands.Left.HasPose = false, "Untracked controller");
        Blocked(() => VRHands.Left.ThumbstickClick = true, "Clicked stick belongs to world grab");
        Console.WriteLine($"AoE control: {count} production runtime assertions passed.");
    }
}
