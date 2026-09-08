using System;

namespace GloomhavenVR.Net;

/// <summary>Source-time playback without extrapolation. Underflow freezes the clock itself, so
/// the next arriving picture has an interval to interpolate instead of stepping straight to it.</summary>
internal sealed class UseBarAnimationPlaybackClock
{
    internal const float MaximumContinuousGap = 0.25f;
    internal float Cursor { get; private set; }
    private float _localAt, _newest;

    internal void Reset(float sourceTime, float localTime)
    { Cursor = sourceTime; _localAt = localTime; _newest = sourceTime; }

    internal float Advance(float localTime, float newestSourceTime)
    {
        float elapsed = Math.Max(0f, localTime - _localAt);
        _localAt = Math.Max(_localAt, localTime);
        if (newestSourceTime > _newest && Cursor >= _newest)
            elapsed = 0f; // time spent waiting before this arrival cannot consume its first pose
        _newest = newestSourceTime;
        Cursor = Math.Min(Cursor + elapsed, newestSourceTime);
        return Cursor;
    }

    internal float Progress(float previousSourceTime, float nextSourceTime)
    {
        float span = nextSourceTime - previousSourceTime;
        if (span <= 0f) return 1f;
        if (span > MaximumContinuousGap)
        {
            // The changed-only sender supplies the last held picture immediately before a new
            // motion. Skip the unobserved idle interval to that actual picture; never invent a
            // slow blend between an old finish and a new start after idle or packet loss.
            Cursor = nextSourceTime;
            return 1f;
        }
        return Math.Max(0f, Math.Min(1f, (Cursor - previousSourceTime) / span));
    }
}
