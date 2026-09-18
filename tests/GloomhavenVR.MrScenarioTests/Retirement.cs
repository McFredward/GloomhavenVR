using System.Collections.Generic;

namespace GloomhavenVR.Core;

// Unity-independent doubles for the real retirement entry point. UI backing state is
// deliberately absent: scenery teardown has no authority over WorldUI.MrBacking.
internal static partial class MixedReality
{
    private static readonly List<object> UnseenUnderlays = new();
    private static readonly HashSet<int> UnseenSources = new();
    private static object? _unseenDarkMat, _unseenSkipMat, _dbgUnderlayMat, _dbgWaferMat, _dbgRimMat;
    internal static int RetireCalls;

    private static void RestoreUnseenUnderlays()
    {
        RetireCalls++;
        UnseenUnderlays.Clear();
        UnseenSources.Clear();
        _unseenDarkMat = _unseenSkipMat = _dbgUnderlayMat = _dbgWaferMat = _dbgRimMat = null;
    }

    internal static void Seed(int resource)
    {
        if (resource == 0) UnseenUnderlays.Add(new object());
        if (resource == 1) UnseenSources.Add(1);
        if (resource == 2) _unseenDarkMat = new object();
        if (resource == 3) _unseenSkipMat = new object();
        if (resource == 4) _dbgUnderlayMat = new object();
        if (resource == 5) _dbgWaferMat = new object();
        if (resource == 6) _dbgRimMat = new object();
    }

    internal static void TickRetirement() => RetireSceneryBackings();
}
