using System;
using GloomhavenVR.WorldUI;

internal static class WindowSwitchTests
{
    internal static void Run(Action<bool, string> check)
    {
        MrBacking.ResetTest(); WindowMaterialise.ResetSwitch(false);
        check(WindowMaterialise.Enabled, "new PC configuration keeps materialization ON");
        check(WorldUIConfig.FileHandle!.BoundDefault, "PC binds true as default");
        check(WorldUIConfig.FileHandle.EnabledKey == "WorldUI/WindowMaterialise", "persisted key is reused");
        WindowMaterialise.ResetSwitch(true);
        check(!WindowMaterialise.Enabled, "new Frame configuration defaults materialization OFF");
        check(!WorldUIConfig.FileHandle!.BoundDefault, "Frame binds false as default");
        WindowMaterialise.ResetSwitch(true, true);
        check(WindowMaterialise.Enabled, "saved Frame ON survives new false default");
        WindowMaterialise.ResetSwitch(false, false);
        check(!WindowMaterialise.Enabled, "saved PC OFF survives true default");
        WindowMaterialise.ResetSwitch(true); WorldUIConfig.FileHandle = null;
        check(!WindowMaterialise.Enabled, "Frame pre-bind fallback also avoids the effect");
        WindowMaterialise.ResetSwitch(false); WorldUIConfig.FileHandle = null;
        check(WindowMaterialise.Enabled, "PC pre-bind fallback retains the effect");
        WindowMaterialise.ResetSwitch(false, false);

        var panel = new ConvertedPanel(); int closed = 0;
        WindowMaterialise.PlayIn(panel);
        check(WindowMaterialiseRunner.BeginCalls == 0 && WindowMaterialise.LiveCount == 0,
            "OFF opening creates no runner, dust or alpha sweep");
        WindowMaterialise.PlayOut(panel, () => closed++);
        check(closed == 1 && panel.InputDetached, "OFF closing detaches input and continues inline");
        check(WindowMaterialiseRunner.BeginCalls == 0 && MrBacking.EntryCount == 0,
            "OFF closing creates no runner or animated MR backing");

        MrBacking.ResetTest(); WindowMaterialise.ResetSwitch(false);
        panel = new ConvertedPanel(); WindowMaterialise.PlayIn(panel);
        var appear = WindowMaterialise.Last; appear.Frame(.2f);
        check(WindowMaterialise.LiveCount == 1 && appear.Alpha != .7f,
            "ON reaches the production entry point and writes the effect");
        WorldUIConfig.FileHandle!.Entry!.Value = false;
        check(WindowMaterialise.LiveCount == 0 && appear.Restores == 1 && appear.Returns == 1,
            "live OFF restores and tears down before the config setter returns");
        check(appear.Alpha == .7f && !MrBacking.Active(panel),
            "live OFF restores exact original alpha and stops backing ownership");
        check(WindowMaterialise.ReleasedMeshes == 2, "live OFF releases both dust meshes");
        appear.Finish("late Unity destruction", true);
        check(appear.Restores == 1, "late runner destruction cannot restore twice");

        MrBacking.ResetTest(); WindowMaterialise.ResetSwitch(false);
        panel = new ConvertedPanel(); closed = 0;
        WindowMaterialise.PlayOut(panel, () => closed++);
        var vanish = WindowMaterialise.Last;
        var visibilityHold = vanish.CurrentHold;
        WindowMaterialise.PlayOut(panel, () => closed++);
        var preRoll = WindowMaterialise.AddPreRoll();
        check(closed == 0 && WindowMaterialiseRunner.BeginCalls == 1,
            "double close shares one active animation and chains its continuation");
        WorldUIConfig.FileHandle!.Entry!.Value = false;
        check(closed == 2 && WindowMaterialise.LiveCount == 0 && preRoll.Ended,
            "live OFF completes every close and releases the close-edge hold inline");
        check(visibilityHold.Released, "native visibility ownership is returned on live OFF");
        check(MrBacking.Closed(panel) && !MrBacking.Visible(panel),
            "live OFF close removes MR backing without a blank lingering plate");
        vanish.Finish("late completion", true);
        check(closed == 2, "completed native close callbacks cannot fire again");
        WindowMaterialise.PlayIn(new ConvertedPanel());
        check(WindowMaterialiseRunner.BeginCalls == 1, "later openings stay instant while OFF");
        WorldUIConfig.FileHandle.Entry.Value = true;
        WindowMaterialise.PlayIn(new ConvertedPanel());
        check(WindowMaterialiseRunner.BeginCalls == 2 && WindowMaterialise.LiveCount == 1,
            "turning ON again animates the next opening normally");
        WindowMaterialise.CancelAll("test complete");
    }
}
