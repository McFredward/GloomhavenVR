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
        BoardConfig.AoeRotationInput.Value = AoeRotationInputMode.OppositeTurnStick;
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
        { h.Thumbstick = (0, 0); h.HasPose = true; h.ThumbstickClick = false; h.SecondaryButton = false; }
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
        FaceButtonTests();
        VisibleHintTests();
        Console.WriteLine($"AoE control: {count} production runtime assertions passed.");
    }

    static void FaceButtonTests()
    {
        var target = new object();
        foreach (bool rightFirst in new[] { false, true })
        foreach (bool rightReleasedFirst in new[] { false, true })
        {
            var gesture = new AoeFaceButtonGesture();
            Check(gesture.Tick(!rightFirst, rightFirst, true, true, target, 0) == 0, "Press must wait for release");
            Check(gesture.Tick(true, true, true, true, target, .1f) == 0, "Staggered recenter chord must not rotate");
            Check(gesture.Tick(true, true, true, true, target, 2) == 0, "Held recenter must not repeat");
            Check(gesture.Tick(rightReleasedFirst, !rightReleasedFirst, true, true, target, 2.1f) == 0, "First chord release must not rotate");
            Check(gesture.Tick(false, false, true, true, target, 2.2f) == 0, "Second chord release must not rotate");
            gesture.Tick(false, true, true, true, target, 3);
            Check(gesture.Tick(false, false, true, true, target, 3.1f) == 1, "Ordinary B tap must rearm after recenter");
        }
        {
            var gesture = new AoeFaceButtonGesture();
            gesture.Tick(true, true, true, true, target, 0);
            Check(gesture.Tick(false, false, true, true, target, .1f) == 0, "Same-frame chord must consume both releases");
            gesture.Tick(false, true, true, true, target, 1);
            Check(gesture.Tick(false, false, true, true, target, 2) == 0, "Long single hold must not rotate");
            gesture.Tick(false, true, true, true, target, 3);
            gesture.Tick(false, true, true, false, target, 3.05f);
            Check(gesture.Tick(false, false, true, true, target, 3.1f) == 0, "Lost tracking or a menu cancels a pending tap even after recovery");
            gesture.Tick(true, false, true, true, target, 4);
            gesture.Tick(true, false, true, true, new object(), 4.05f);
            Check(gesture.Tick(false, false, true, true, target, 4.1f) == 0, "Target change cancels a pending tap even if old target returns");
        }
        var display = Fresh();
        BoardConfig.AoeRotationInput.Value = AoeRotationInputMode.UpperFaceButtons;
        VRHands.Left.Thumbstick = (1, 1); VRHands.Right.Thumbstick = (1, 1);
        AoeControl.Tick();
        Check(display.Calls == 0 && !AoeControl.ClaimsStick(HandSide.Left) && !AoeControl.ClaimsStick(HandSide.Right),
            "Default B/Y mode must leave every locomotion stick axis free");
        Check(TutorialAoeHint.TryOverride("SCENARIO_PUZZLE_5B_08", null, out var hint)
            && hint == "tut_vr_aoe_buttons", "Tutorial must reflect face-button setting");
        VRHands.Right.SecondaryButton = true; AoeControl.Tick();
        Check(display.Calls == 0, "B press must not rotate before release");
        Time.unscaledTime += .05f; VRHands.Right.SecondaryButton = false; AoeControl.Tick();
        Check(display.Calls == 1 && display.AreaEffectAngle == 300, "B release must rotate clockwise once");
        VRHands.Left.SecondaryButton = true; AoeControl.Tick();
        Time.unscaledTime += .05f; VRHands.Left.SecondaryButton = false; AoeControl.Tick();
        Check(display.Calls == 2 && display.AreaEffectAngle == 0, "Rapid B/Y alternation must override native keyboard direction latch");
        VRHands.Right.SecondaryButton = true; AoeControl.Tick();
        VRModeStateMachine.CurrentMode = VRMode.ModalUI; AoeControl.Tick();
        VRModeStateMachine.CurrentMode = VRMode.BoardTargeting; VRHands.Right.SecondaryButton = false; AoeControl.Tick();
        Check(display.Calls == 2, "Menu transition must cancel pending rotation");
        VRHands.Right.SecondaryButton = true; AoeControl.Tick();
        display.m_SavedAbility = new(); AoeControl.Tick(); VRHands.Right.SecondaryButton = false; AoeControl.Tick();
        Check(display.Calls == 2, "New ability must not inherit the previous ability's held B");
        VRHands.Right.SecondaryButton = true; AoeControl.Tick();
        BoardConfig.AoeRotationInput.Value = AoeRotationInputMode.OppositeTurnStick;
        VRHands.Left.Thumbstick = (0, 0); AoeControl.Tick();
        BoardConfig.AoeRotationInput.Value = AoeRotationInputMode.UpperFaceButtons;
        VRHands.Right.SecondaryButton = false; AoeControl.Tick();
        Check(display.Calls == 2, "Binding changes must cancel a held face button");
    }

    static void VisibleHintTests()
    {
        var page = new LevelMessagePageUI { page = new MessagePage { PageTextKey = "SCENARIO_PUZZLE_5B_08", PageTextKeyController = null } };
        var title = new LevelMessageUILayout { _message = new Message { TitleKey = null, TitleKeyController = "Consoles/SCENARIO_PUZZLE_5B_ROTATE_MESSAGE" } };
        var story = new LevelMessagePageUI { page = new MessagePage { PageTextKey = "STORY_OTHER" } };
        UnityEngine.Object.Instances = [page, title, story];
        BoardConfig.AoeRotationInput.Value = AoeRotationInputMode.UpperFaceButtons;
        Check(page.information!.text == "tut_vr_aoe_buttons" && title.title!.text == "tut_vr_aoe_buttons",
            "Already-open tutorial body and title must refresh immediately when binding changes");
        Check(story.information!.text == "unchanged", "Binding changes must never rewrite unrelated story text");
        ComfortSettings.Turn.Value = TurnMode.Snap; ComfortSettings.TurnHand.Value = TurnHandChoice.Right;
        BoardConfig.AoeRotationInput.Value = AoeRotationInputMode.OppositeTurnStick;
        Check(page.information.text == "tut_vr_aoe_left", "Open tutorial must reflect the optional stick binding");
        ComfortSettings.TurnHand.Value = TurnHandChoice.Left; ComfortSettings.RaiseChanged("TurnHand");
        Check(page.information.text == "tut_vr_aoe_right", "Open tutorial must follow changed turning hand");
        ComfortSettings.Turn.Value = TurnMode.Off; VRHands.Primary = VRHands.Left;
        GloomhavenVR.Plugin.PrimaryHand.Value = "Left";
        Check(page.information.text == "tut_vr_aoe_left", "Open tutorial must follow changed main hand with turning off");
        GloomhavenVR.Compat.TutorialVR.IsTutorialActive = false;
        BoardConfig.AoeRotationInput.Value = AoeRotationInputMode.UpperFaceButtons;
        Check(page.information.text == "tut_vr_aoe_left", "Leaving the tutorial must stop stale presentation updates");
        GloomhavenVR.Compat.TutorialVR.IsTutorialActive = true;
        UnityEngine.Object.Instances = [];
    }
}
