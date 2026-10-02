using System.Collections.Generic;
using System.Text;

// Only monitor/config/clock/log boundary inputs are replaced. The complete production
// state policy and early-Update adapter run unchanged, including ordering and frame discard.
namespace UnityEngine
{
    internal static class Time { internal static float unscaledDeltaTime = 0.05f; }
    internal static class Mathf { internal static float Max(float a, float b) => a > b ? a : b; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class VROptionsTab { internal static bool IsOpen; }
}
namespace GloomhavenVR.Core
{
    internal static class PerfConfig
    {
        internal static int PlayerFigureDetailPercent, EnemyFigureDetailPercent;
        internal static int FigureEffectsDensityPercent;
        internal static bool FigureClothSimulationEnabled;
    }
    internal static class ScenarioFigureDetailBudget { internal static bool MeasurementReady = true; }
    internal static class VRLog
    {
        internal static bool WantsDebug = true;
        internal static readonly List<string> Lines = new();
        internal static void Info(string scope, string text) => Lines.Add(text);
        internal static void Debug(string scope, string text) => Lines.Add(text);
    }
    internal static class PerfFrameSplit
    {
        internal static int Dropped;
        internal static void RollFrame(bool enabled, bool record, float frameMs)
        { if (!record) Dropped++; }
    }
    internal static partial class PerfMonitor
    {
        private const string Scope0 = "Perf";
        private const int MinMarkFrames = 120;
        private static int _frameCount, _depth;
        private static float _windowStart;
        private static double _frameModSeconds;
        private static bool _firstSampleDone;
        private sealed class Step { internal double FrameSeconds; internal int FrameCalls; }
        private static readonly List<Step> StepOrder = new();
        private static readonly Tally TestTally = new("boundary-work");
        internal static void Register(string name) => TestTally.PrintZero = true;
        internal static readonly List<(string State, float Seconds, int Frames, long Work, long WorstWork, bool CompleteProbesOnly)> Closed = new();
        internal static bool PendingCleared => _frameModSeconds == 0 && _depth == 0
            && _firstSampleDone && StepOrder.TrueForAll(s => s.FrameSeconds == 0 && s.FrameCalls == 0);

        internal static bool Advance(float now) => ObserveFigureMeasurement(now, true);
        internal static string Tag()
        { var text = new StringBuilder(); AppendFigureMeasurement(text); return text.ToString(); }
        internal static void SeedOldSamples(int count, float since)
        {
            _frameCount = count; _windowStart = since;
            _frameModSeconds = 999; _depth = 3;
            StepOrder.Clear(); StepOrder.Add(new Step { FrameSeconds = 999, FrameCalls = 7 });
            TestTally.Add(4); TestTally.RollFrame(); TestTally.Add(7); TestTally.RollFrame();
            TestTally.Add(999); // The mixed callback must not enter the old TALLY summary.
        }
        internal static void ClearForTest()
        {
            FigureMeasurement.Clear(); _figureChangeLoggedAt = float.NegativeInfinity;
            _frameCount = 0; _windowStart = 0; _frameModSeconds = 0; _depth = 0;
            _firstSampleDone = false; StepOrder.Clear(); Closed.Clear(); VRLog.Lines.Clear();
            PerfFrameSplit.Dropped = 0;
            PerfConfig.PlayerFigureDetailPercent = PerfConfig.EnemyFigureDetailPercent = 0;
            PerfConfig.FigureEffectsDensityPercent = 0; PerfConfig.FigureClothSimulationEnabled = false;
            ScenarioFigureDetailBudget.MeasurementReady = true;
            WorldUI.VROptionsTab.IsOpen = false;
            TestTally.ResetWindow();
            TestTally.PrintZero = false;
        }
        private static void LogSummary(float seconds) => Closed.Add((Tag(), seconds, _frameCount,
            TestTally.WindowTotal, TestTally.WindowWorstFrame, _figureBoundarySummary));
        private static void ResetWindow(float now)
        { _frameCount = 0; _windowStart = now; TestTally.ResetWindow(); }
        internal static bool CounterPendingDiscarded()
        { TestTally.RollFrame(); return TestTally.WindowTotal == 0 && TestTally.WindowWorstFrame == 0; }
    }
}
