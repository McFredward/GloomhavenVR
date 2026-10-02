using System.Text;
using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class PerfMonitor
{
    private static readonly PerfFigureMeasurement FigureMeasurement = new();
    private static PerfFigureMeasurement.Settings _figureWindowSettings;
    private static int _figureWindowRevision;
    private static bool _figureWindowSteady;
    private static float _figureChangeLoggedAt = float.NegativeInfinity;

    /// <summary>
    /// Run at the early Update seam, before rolling the previous frame. A setting written
    /// during that frame can change meshes halfway through it: discard that one frame,
    /// close the already-complete old samples with their OLD latched settings, and record
    /// preparation separately. Never reset inside a nested timed hand/UI callback.
    /// </summary>
    private static bool ObserveFigureMeasurement(float now, bool splitOn)
    {
        var wanted = new PerfFigureMeasurement.Settings(PerfConfig.PlayerFigureDetailPercent,
            PerfConfig.EnemyFigureDetailPercent, PerfConfig.FigureEffectsDensityPercent,
            PerfConfig.FigureClothSimulationEnabled);
        bool applied = ScenarioFigureDetailBudget.MeasurementReady && !WorldUI.VROptionsTab.IsOpen;
        PerfFigureMeasurement.Boundary boundary = FigureMeasurement.Observe(wanted, applied, now);
        if (boundary == PerfFigureMeasurement.Boundary.None) return false;

        // Time.unscaledDeltaTime belongs to the frame being dropped, not to the closed
        // old interval. The frame/step samples already in the window stop before it.
        float elapsed = Mathf.Max(0.001f, now - Time.unscaledDeltaTime - _windowStart);
        if (_frameCount >= MinMarkFrames) LogSummary(elapsed);
        _figureWindowSettings = FigureMeasurement.Current;
        _figureWindowRevision = FigureMeasurement.Revision;
        _figureWindowSteady = FigureMeasurement.IsSteady;
        ResetWindow(now);
        DiscardFigureBoundaryFrame(splitOn);

        if (_figureWindowSteady)
        {
            // This marker also lets the offline reader discard heartbeat/scene samples
            // from an omitted short preparation interval. At least two seconds separate
            // settled blocks; rapid slider steps cannot create an unbounded Info stream.
            var text = new StringBuilder("FIGURE-MEASURE begin revision=");
            text.Append(_figureWindowRevision); _figureWindowSettings.Append(text);
            text.Append(" state=steady");
            VRLog.Info(Scope0, text.ToString());
        }
        else if (VRLog.WantsDebug && now - _figureChangeLoggedAt >= 1f)
        {
            _figureChangeLoggedAt = now;
            var text = new StringBuilder("FIGURE-MEASURE change revision=");
            text.Append(_figureWindowRevision); _figureWindowSettings.Append(text);
            text.Append(" state=preparing; prior samples closed or omitted below ")
                .Append(MinMarkFrames).Append(" frames; mixed boundary frame discarded");
            VRLog.Debug(Scope0, text.ToString());
        }
        return true;
    }

    private static void DiscardFigureBoundaryFrame(bool splitOn)
    {
        _frameModSeconds = 0d; _depth = 0; _firstSampleDone = true;
        foreach (Step step in StepOrder)
        { step.FrameSeconds = 0d; step.FrameCalls = 0; }
        PerfFrameSplit.RollFrame(splitOn, record: false, frameMs: 0f);
    }

    private static void AppendFigureMeasurement(StringBuilder text)
    {
        if (!FigureMeasurement.HasState) return;
        text.Append(" | figure"); _figureWindowSettings.Append(text);
        text.Append(" state=").Append(_figureWindowSteady ? "steady" : "preparing")
            .Append(" revision=").Append(_figureWindowRevision);
    }
}
