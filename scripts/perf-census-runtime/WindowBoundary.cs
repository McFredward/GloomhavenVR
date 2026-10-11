using UnityEngine;

namespace GloomhavenVR.Core;

// Only pacing inputs and summary accumulators are seams. MarkChange,
// RefreshBudget and the loaded native census request/pump are production code.
internal static class XrProbe
{
    internal static float LastRefreshHz;
    internal static bool TryGetRefreshRate(out string source)
    {
        source = "fixture adaptive runtime";
        return true;
    }
}

internal static partial class PerfMonitor
{
    private static object? _host;
    private static float _windowStart, _refreshHz, _budgetResolvedAt, _budgetSeconds;
    private static string _refreshSource = "";
    private static int _frameCount;
    private const int MinMarkFrames = 120;
    internal static int ClosedWindows, ResetWindows;
    internal static float LastSummarizedHz;

    internal static void SeedPacing()
    {
        _host = new object();
        _refreshHz = 72;
        ClosedWindows = ResetWindows = 0;
    }

    internal static void AdaptiveRefresh(float hz, int frames=140, float seconds=10f)
    {
        XrProbe.LastRefreshHz = hz;
        _frameCount = frames;
        _windowStart = Time.unscaledTime - seconds;
        _budgetResolvedAt = Time.unscaledTime - 11;
        RefreshBudget();
    }

    private static void LogSummary(float seconds)
    {
        ClosedWindows++;
        LastSummarizedHz = _refreshHz;
    }

    private static void ResetWindow(float now)
    {
        ResetWindows++;
        _windowStart = now;
        _frameCount = 0;
    }
}
