using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // A stalled frame has no intermediate pictures. Feeding its whole duration into
        // the first visible wall sample can finish a new dissolve immediately: at tau
        // 0.12s, 0.636s advances 0 -> 1 after the snap. This is a presentation defect,
        // not an authorized environment-budget compromise (Frame615 hardware report).
        // Bound only the wall's visual clock. Coverage EMA, dwell, decision cadence,
        // shared board fading and original gameplay clocks retain elapsed-time semantics.
        // At >=30 rendered frames/s this is the exact original step. A slower/stalled
        // display takes more wall-clock time to finish, preserving intermediate pictures.
        private static float AnimationDelta(float frameDelta) =>
            Mathf.Clamp(frameDelta, 0f, 1f / 30f);

        private int _animationClampedFrames;
        private float _animationWorstDelta;
        private float _animationClockNextReport;

        private void NoteAnimationClock(float frameDelta, float now)
        {
            if (!VRLog.Wants(VRLogLevel.Debug))
            {
                _animationClampedFrames = 0;
                _animationWorstDelta = 0f;
                _animationClockNextReport = now + 10f;
                return;
            }
            if (frameDelta > 1f / 30f)
            {
                _animationClampedFrames++;
                _animationWorstDelta = Mathf.Max(_animationWorstDelta, frameDelta);
            }
            if (now < _animationClockNextReport) return;
            _animationClockNextReport = now + 10f;
            if (_animationClampedFrames > 0)
                VRLog.Debug(Name, "ANIMATION CLOCK: " + _animationClampedFrames
                    + " rendered frame(s) bounded at 33.33ms; largest elapsed frame "
                    + (_animationWorstDelta * 1000f).ToString("F2")
                    + "ms. Wall dissolve preserves visible intermediate samples; coverage and dwell remain live.");
            _animationClampedFrames = 0;
            _animationWorstDelta = 0f;
        }
    }
}
