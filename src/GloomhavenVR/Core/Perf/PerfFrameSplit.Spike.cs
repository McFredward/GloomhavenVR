using System.Diagnostics;
using System.Text;

namespace GloomhavenVR.Core;

internal static partial class PerfFrameSplit
{
    private static bool _completedFrameValid, _completedFrameRendered;
    private static int _completedFrame, _completedCameraFaults, _completedCameraPeak;
    private static double _completedWallMs, _completedLogicMs, _completedBeforeCameraMs;
    private static double _completedRenderMs, _completedAfterCameraMs;

    // Snapshot BEFORE RollFrame opens the next frame. The historical SPIKE frame
    // number is its reporting boundary; every figure here names the actual frame
    // whose callbacks and interval just completed. Sparse/replaced hosts stay n/a.
    private static void CaptureCompletedFrame(bool record, long now, int reportingFrame)
    {
        _completedFrameValid = record && _logicEndSeen && _logicStart != 0L
            && _logicFrame == reportingFrame - 1;
        if (!_completedFrameValid) return;
        _completedFrame = _logicFrame;
        double scale = 1000d / Stopwatch.Frequency;
        _completedWallMs = (now - _logicStart) * scale;
        _completedLogicMs = (_logicEnd - _logicStart) * scale;
        _completedFrameRendered = _renderSeen;
        _completedBeforeCameraMs = ((_renderSeen ? _renderFirst : now) - _logicEnd) * scale;
        _completedRenderMs = _renderSeen ? (_renderLast - _renderFirst) * scale : 0d;
        _completedAfterCameraMs = _renderSeen ? (now - _renderLast) * scale : 0d;
        _completedCameraFaults = _cameraPassFaults + _cameraPassDepth;
        _completedCameraPeak = _cameraPassPeak;
    }

    internal static void AppendSpike(StringBuilder sb)
    {
        if (!_active || !VRLog.WantsDebug) return;
        if (!_completedFrameValid)
        {
            sb.Append(" | completed-frame seams n/a (missing or unmatched frame boundary)");
            return;
        }
        sb.Append(" | completed frame ").Append(_completedFrame)
            .Append(" seams (Debug, wall brackets): host interval ").Append(_completedWallMs.ToString("F2"))
            .Append("ms, logic ").Append(_completedLogicMs.ToString("F2"))
            .Append("ms, after logic before first observed camera ").Append(_completedBeforeCameraMs.ToString("F2"))
            .Append("ms, camera span ").Append(_completedRenderMs.ToString("F2"))
            .Append("ms, after last observed camera ").Append(_completedAfterCameraMs.ToString("F2"))
            .Append("ms; camera callbacks ").Append(_completedFrameRendered ? "observed" : "none")
            .Append(", peak nesting ").Append(_completedCameraPeak)
            .Append(", unmatched/dropped ").Append(_completedCameraFaults)
            .Append("; per-camera parent/child intervals are inclusive and overlap; gaps remain unassigned");
    }
}
