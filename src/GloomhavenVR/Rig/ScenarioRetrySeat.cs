namespace GloomhavenVR.Rig;

/// <summary>
/// Keeps a local player's original arrival across an explicit native retry. A rig rebuild is
/// not a new scenario; a new scenario without a retry is not allowed to inherit the old seat.
/// The generic payload keeps this lifetime independent of Unity objects and testable directly.
/// </summary>
internal sealed class ScenarioRetrySeat<TPose>
{
    private object? _scenario;
    private bool _retryRequested;
    private bool _preserveRequested;
    private bool _preservedArrival;
    internal bool CanCapture => !_retryRequested && !_preserveRequested && !RestorePending && !_preservedArrival;
    internal bool HasPose { get; private set; }
    internal bool RestorePending { get; private set; }
    internal TPose Pose { get; private set; } = default!;

    internal bool EnterScenario(object scenario)
    {
        if (ReferenceEquals(_scenario, scenario))
            return false;
        _scenario = scenario;
        RestorePending = _retryRequested && HasPose;
        bool keep = (_retryRequested || _preserveRequested) && HasPose;
        _preservedArrival = keep;
        _retryRequested = false;
        _preserveRequested = false;
        if (!keep)
        {
            HasPose = false;
            Pose = default!;
        }
        return true;
    }

    internal void CaptureArrival(TPose pose)
    {
        // A duplicate callback during teardown must not replace the original with the moved rig.
        if (!CanCapture)
            return;
        Pose = pose;
        HasPose = true;
    }

    internal void RequestRetry()
    {
        if (_scenario != null && HasPose)
            _retryRequested = true;
    }

    internal void PreserveForRoundReload()
    {
        if (_scenario != null && HasPose)
            _preserveRequested = true;
    }

    internal void CompleteRestore() => RestorePending = false;

    internal void LeaveScenario()
    {
        _scenario = null;
        _retryRequested = false;
        _preserveRequested = false;
        _preservedArrival = false;
        RestorePending = false;
        HasPose = false;
        Pose = default!;
    }
}
