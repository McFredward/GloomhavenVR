using System;
using System.IO;
using BepInEx.Configuration;
using GloomhavenVR;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private static void Main(string[] args)
    {
        Check(PerfConfig.WallVisibilityMode == 2 && PerfConfig.WallAutoHideBelowFps == 15, "unbound wall defaults are automatic at15FPS");
        foreach (bool frame in new[] { false, true })
        {
            FrameLaunchOptIn.Enabled = frame;
            var fresh = new ConfigFile(Path.Combine(args[0], frame ? "fresh-frame.cfg" : "fresh-pc.cfg"), false) { SaveOnConfigSet = false };
            PerfConfig.BindFixture(fresh);
            Check(PerfConfig.WallVisibilityMode == 2 && PerfConfig.WallAutoHideBelowFps == 15, "fresh PC and Frame wall defaults agree");
        }
        var file = new ConfigFile(Path.Combine(args[0], "wall.cfg"), false) { SaveOnConfigSet = false };
        PerfConfig.BindFixture(file);
        var mode = ConfigCatalog.Item(PerfConfig.WallVisibilityModeCount);
        var threshold = ConfigCatalog.Item(PerfConfig.WallAutoHideBelowFpsCount);
        Check(mode.Kind == ConfigCatalog.ConfigKind.Choice && mode.Choices is { Length: 3 }, "three wall modes use a dropdown");
        Check((int)mode.Choices![0] == 0 && (int)mode.Choices[1] == 1 && (int)mode.Choices[2] == 2, "mode indexes map to exact stored integers");
        Check(threshold.Kind == ConfigCatalog.ConfigKind.Number && threshold.Integral && threshold.HasRange
            && threshold.Min == 5 && threshold.Max == 30, "automatic threshold is an editable bounded FPS integer");
        Check(ConfigSteps.Resolve("Optimize", "WallAutoHideBelowFpsCount", 25, true) == 1, "FPS threshold steps by one");

        foreach (bool german in new[] { false, true })
        {
            Loc.German = german;
            string[] labels = german ? new[] { "Normal", "Alle ausblenden", "Automatisch" }
                : new[] { "Regular", "Hide all", "Automatic" };
            for (int selected = 0; selected < 3; selected++)
            {
                mode.Entry.BoxedValue = selected;
                VROptionsTab.Build(mode);
                Check(VROptionsTab.LastDropdown.value == selected, "localized labels do not change selected index");
                for (int index = 0; index < 3; index++)
                {
                    Check(VROptionsTab.LastDropdown.options[index].text == labels[index], "each wall choice reads in the selected language");
                    VROptionsTab.LastDropdown.onValueChanged.Invoke(index);
                    Check(PerfConfig.WallVisibilityMode == index, "dropdown selection writes the exact wall policy");
                }
            }
        }

        var other = ConfigCatalog.Item(file.Bind("Other", "IntegerChoiceCount", 2, new ConfigDescription("fixture", new AcceptableValueList<int>(1, 2, 3))));
        VROptionsTab.Build(other);
        Check(VROptionsTab.LastDropdown.value == 1 && VROptionsTab.LastDropdown.options[1].text == "2", "unrelated integer choices keep their raw labels and selected index");
        Check(ConfigCatalog.ChoiceText(other, null) == "-" && ConfigCatalog.ChoiceText(mode, 9) == "9", "generic and unknown-value fallbacks remain unchanged");

        var fade = ConfigCatalog.Item(file.Bind("Compat", "WallFade", true));
        var inside = ConfigCatalog.Item(file.Bind("WallFade", "WalkInStandDown", true));
        VROptionsTab.Add(mode); VROptionsTab.Add(threshold); VROptionsTab.Add(fade); VROptionsTab.Add(inside);
        Check(VROptionsTab.NeedsRebuild(mode), "wall policy edits rebuild conditional rows immediately");
        for (int selected = 0; selected < 3; selected++)
        {
            mode.Entry.BoxedValue = selected;
            Check(VROptionsTab.Available(mode), "wall policy stays reachable when regular fading is disabled");
            Check(VROptionsTab.Available(threshold) == (selected == 2), "threshold is visible only in automatic mode");
            Check(VROptionsTab.Available(fade) == (selected != 1), "regular fade control folds only for permanent hiding");
            Check(VROptionsTab.Available(inside) == (selected != 1), "inside-wall tuning transitively folds for permanent hiding");
        }
        fade.Entry.BoxedValue = false;
        for (int selected = 0; selected < 3; selected++)
        {
            mode.Entry.BoxedValue = selected;
            Check(VROptionsTab.Available(mode), "disabled regular fading cannot hide wall policy");
            Check(VROptionsTab.Available(threshold) == (selected == 2), "disabled regular fading cannot hide automatic threshold");
        }
        fade.Entry.BoxedValue = true;
        threshold.Entry.BoxedValue = 22;
        mode.Entry.BoxedValue = 1;
        Check(PerfConfig.WallAutoHideBelowFps == 22, "folding the automatic threshold preserves its chosen value");
        threshold.Entry.BoxedValue = -1;
        Check(PerfConfig.WallAutoHideBelowFps == 5, "low FPS edits clamp at five");
        threshold.Entry.BoxedValue = 1000;
        Check(PerfConfig.WallAutoHideBelowFps == 30, "high FPS edits clamp at thirty");
        // BepInEx already clamps bound edits, so exercise the separate production
        // getter with explicit unbounded storage too; otherwise a missing getter
        // clamp would escape the causal control despite the options still passing.
        var boundThreshold = PerfConfig.WallAutoHideBelowFpsCount;
        var rawFile = new ConfigFile(Path.Combine(args[0], "unbounded-fixture.cfg"), false) { SaveOnConfigSet = false };
        PerfConfig.WallAutoHideBelowFpsCount = rawFile.Bind("Fixture", "UnboundedFpsCount", 1000);
        Check(PerfConfig.WallAutoHideBelowFps == 30, "unbounded FPS storage clamps at thirty");
        PerfConfig.WallAutoHideBelowFpsCount.Value = -1;
        Check(PerfConfig.WallAutoHideBelowFps == 5, "unbounded FPS storage clamps at five");
        PerfConfig.WallAutoHideBelowFpsCount = boundThreshold;
        threshold.Entry.BoxedValue = 22;
        PerfConfig.ScenarioTerrainSubstitution.Value = false;
        file.Save();
        FrameLaunchOptIn.Enabled = !FrameLaunchOptIn.Enabled;
        var reload = new ConfigFile(file.ConfigFilePath, false) { SaveOnConfigSet = false };
        PerfConfig.BindFixture(reload);
        Check(PerfConfig.WallVisibilityMode == 1 && PerfConfig.WallAutoHideBelowFps == 22, "saved wall choices survive rebind and platform switch");
        Check(!PerfConfig.TerrainSubstitutionOn, "existing rendering keys retain their saved values");
        Console.WriteLine("PASS: " + _assertions + " wall option assertions with original BepInEx binding and production dropdown/dependencies.");
    }
}
