namespace GloomhavenVR.WorldUI;

/// <summary>Continuous evidence, never a timeout that confirms or dismisses story content.</summary>
internal sealed class StoryRecoveryDwell
{
    private float _since = -1f;
    private bool _recovered;
    internal bool Tick(bool eligible, bool far, bool poseChangedAfterRecovery, float now)
    {
        if (!eligible || !far) { _since = -1f; _recovered = false; return false; }
        if (_recovered && !poseChangedAfterRecovery) return false;
        if (_recovered) { _recovered = false; _since = -1f; }
        if (_since < 0f || now < _since) _since = now;
        return now - _since >= 3f;
    }
    internal void Recovered() { _recovered = true; _since = -1f; }
}
