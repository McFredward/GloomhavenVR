using System;
using GloomhavenVR.Core;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string why)
    { _assertions++; if (!value) throw new Exception(why); }
    private static void Main()
    {
        var policy = new PerfFigureMeasurement();
        var zero = new PerfFigureMeasurement.Settings(0, 0, 0, false);
        Check(policy.Observe(zero, true, 0) == PerfFigureMeasurement.Boundary.Preparing,
            "Initial configuration must prepare before steady measurements");
        Check(policy.Observe(zero, true, 1.99f) == PerfFigureMeasurement.Boundary.None,
            "Two-second preparation guard must not be bypassed");
        Check(policy.Observe(zero, false, 3) == PerfFigureMeasurement.Boundary.None && !policy.IsSteady,
            "Unapplied driver state cannot become steady");
        Check(policy.Observe(zero, true, 4) == PerfFigureMeasurement.Boundary.None,
            "Delayed application must restart the quiet guard after preparation");
        Check(policy.Observe(zero, true, 5) == PerfFigureMeasurement.Boundary.Steady,
            "Applied settled state must begin a steady window");
        Check(policy.Observe(zero, true, 5.1f) == PerfFigureMeasurement.Boundary.None,
            "Stable frames must not create repetitive boundaries");
        Check(policy.Observe(zero, false, 6) == PerfFigureMeasurement.Boundary.Preparing,
            "New native preparation must end the previous steady interval");
        Check(policy.Revision == 2 && !policy.IsSteady, "Preparation invalidation advances revision");
        var full = new PerfFigureMeasurement.Settings(100, 100, 0, false);
        Check(policy.Observe(full, true, 6.1f) == PerfFigureMeasurement.Boundary.Preparing,
            "Both slider changes must restart preparation");
        Check(policy.Observe(zero, true, 6.2f) == PerfFigureMeasurement.Boundary.Preparing,
            "Returning to the previous setting is a new interval");
        Check(policy.Observe(zero, true, 8.19f) == PerfFigureMeasurement.Boundary.None,
            "Rapid changes must restart the guard from the last change");
        Check(policy.Observe(zero, true, 8.21f) == PerfFigureMeasurement.Boundary.Steady,
            "Rapid changes settle when the last setting remains applied");
        Check(!zero.Same(new PerfFigureMeasurement.Settings(0, 0, 10, false)), "FX is a confound");
        Check(!zero.Same(new PerfFigureMeasurement.Settings(0, 0, 0, true)), "Cloth is a confound");
        policy.Clear();
        Check(!policy.HasState && policy.Revision == 0, "Shutdown clears measurement lifetime");

        PerfMonitor.ClearForTest();
        Check(PerfMonitor.Advance(0), "Initial adapter boundary must be consumed");
        Check(PerfMonitor.Tag().Contains("players=0 enemies=0 fx=0 cloth=False state=preparing revision=1"),
            "FRAME must label requested preparation state explicitly");
        Check(!PerfMonitor.Advance(1), "No repeated reset during unchanged preparation");
        Check(PerfMonitor.Advance(2.1f), "Driver-ready settled boundary must be consumed");
        Check(VRLog.Lines.Exists(s => s == "FIGURE-MEASURE begin revision=1 players=0 enemies=0 fx=0 cloth=False state=steady"),
            "Offline parser must get an exact stable-window reset marker");
        Check(PerfMonitor.Tag().Contains("state=steady revision=1"), "Stable FRAME carries latched phase");
        PerfMonitor.SeedOldSamples(150, 2.1f);
        PerfConfig.PlayerFigureDetailPercent = 100;
        Check(PerfMonitor.Advance(10), "Player change closes the old interval");
        Check(PerfMonitor.Closed.Count == 1 && PerfMonitor.Closed[0].Frames == 150,
            "Already-completed old samples must be reported once");
        Check(PerfMonitor.Closed[0].State.Contains("players=0 enemies=0")
            && PerfMonitor.Closed[0].State.Contains("state=steady"),
            "Old window must retain OLD settings even after the config value changes");
        Check(Math.Abs(PerfMonitor.Closed[0].Seconds - 7.85f) < 0.001f,
            "Old duration must exclude the discarded transition frame");
        Check(PerfMonitor.PendingCleared, "Mixed frame's nested steps and mod total must be discarded");
        Check(PerfMonitor.Closed[0].Work == 11 && PerfMonitor.Closed[0].WorstWork == 7,
            "Mixed frame's direct tally additions must not enter old total or worst");
        Check(PerfMonitor.CounterPendingDiscarded(), "Reset must discard mixed pending counter work");
        Check(PerfMonitor.Closed[0].CompleteProbesOnly,
            "Boundary summary must exclude unfinished native captures");
        Check(PerfFrameSplit.Dropped == 3, "Mixed camera samples must not leak across the boundary");
        Check(PerfMonitor.Tag().Contains("players=100 enemies=0")
            && PerfMonitor.Tag().Contains("state=preparing revision=2"), "New window labels its new state");
        ScenarioFigureDetailBudget.MeasurementReady = false;
        Check(!PerfMonitor.Advance(13), "Slow native application must keep the interval preparing");
        ScenarioFigureDetailBudget.MeasurementReady = true;
        Check(!PerfMonitor.Advance(14), "Newly applied state still needs a quiet guard");
        Check(PerfMonitor.Advance(15), "Delayed application must eventually begin steady measurement");
        PerfMonitor.SeedOldSamples(119, 15);
        PerfConfig.EnemyFigureDetailPercent = 100;
        Check(PerfMonitor.Advance(15), "Enemy change must close a short interval as well");
        Check(PerfMonitor.Closed.Count == 1, "Short intervals must not masquerade as sufficient evidence");
        Check(PerfMonitor.PendingCleared, "Omitted short interval cannot retain pending work");
        PerfMonitor.Advance(17.1f); PerfMonitor.SeedOldSamples(180, 17.1f);
        PerfConfig.PlayerFigureDetailPercent = PerfConfig.EnemyFigureDetailPercent = 0;
        PerfMonitor.Advance(28);
        Check(PerfMonitor.Closed[1].State.Contains("players=100 enemies=100"),
            "100-to-0 reversal must keep the original full-detail interval");
        Check(PerfMonitor.Tag().Contains("players=0 enemies=0"), "Reversal begins a distinct zero-detail interval");
        PerfMonitor.Advance(30.1f); PerfMonitor.SeedOldSamples(150, 30.1f);
        PerfConfig.FigureEffectsDensityPercent = 50; PerfMonitor.Advance(40);
        Check(PerfMonitor.Closed[2].State.Contains("fx=0"), "Effects changes must not relabel preceding frames");
        PerfMonitor.Advance(42.1f); PerfMonitor.SeedOldSamples(150, 42.1f);
        PerfConfig.FigureClothSimulationEnabled = true; PerfMonitor.Advance(52);
        Check(PerfMonitor.Closed[3].State.Contains("cloth=False"), "Cloth changes must not relabel preceding frames");

        PerfMonitor.ClearForTest(); PerfMonitor.Advance(0); PerfMonitor.Advance(2.1f);
        GloomhavenVR.WorldUI.VROptionsTab.IsOpen = true;
        Check(PerfMonitor.Advance(3), "Opening options must separate even an unchanged slider baseline");
        Check(!PerfMonitor.Advance(20) && PerfMonitor.Tag().Contains("state=preparing"),
            "Open options cannot be reported as steady scenario evidence");
        GloomhavenVR.WorldUI.VROptionsTab.IsOpen = false;
        Check(!PerfMonitor.Advance(21), "Closing options must allow presentation to settle");
        Check(PerfMonitor.Advance(22.1f) && PerfMonitor.Tag().Contains("state=steady revision=2"),
            "Closed unchanged options produce a fresh uncontaminated baseline");

        PerfMonitor.ClearForTest(); PerfMonitor.Advance(0);
        for (int i = 1; i < 20; i++)
        { PerfConfig.PlayerFigureDetailPercent = i; PerfMonitor.Advance(i * 0.02f); }
        Check(VRLog.Lines.Count == 1, "Rapid slider changes must rate-limit Debug preparation traces");
        Check(!VRLog.Lines.Exists(s => s.Contains("begin")), "Rapid changes cannot produce false steady markers");
        VRLog.WantsDebug = false; PerfConfig.PlayerFigureDetailPercent = 80; PerfMonitor.Advance(2);
        Check(VRLog.Lines.Count == 1, "Normal log level must omit detailed change traces");
        Console.WriteLine($"Perf figure measurement: {_assertions} assertions passed.");
    }
}
