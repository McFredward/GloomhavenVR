namespace GloomhavenVR.Board;

/// <summary>Short B/Y release edges, with the B+Y recenter chord owning both complete presses.
/// Eligibility and target identity must remain valid throughout the press, not just at release.</summary>
internal sealed class AoeFaceButtonGesture
{
    private const float TapSeconds = .65f;
    private Press _left, _right;
    private bool _chord;

    private struct Press
    {
        internal bool Down, Armed;
        internal float Started;
        internal object? Target;
    }

    internal int Tick(bool leftDown, bool rightDown, bool leftEligible, bool rightEligible,
        object? target, float now)
    {
        if (leftDown && rightDown) _chord = true;
        bool left = Update(ref _left, leftDown, leftEligible && !_chord, target, now);
        bool right = Update(ref _right, rightDown, rightEligible && !_chord, target, now);
        if (_chord)
        {
            if (!leftDown && !rightDown) _chord = false;
            return 0;
        }
        return right ? 1 : left ? -1 : 0;
    }

    private static bool Update(ref Press press, bool down, bool eligible, object? target, float now)
    {
        if (!eligible || !ReferenceEquals(press.Target, target)) press.Armed = false;
        bool fire = false;
        if (down && !press.Down)
        {
            press.Armed = eligible && target != null;
            press.Started = now;
            press.Target = target;
        }
        else if (!down && press.Down)
        {
            fire = press.Armed && now - press.Started < TapSeconds;
            press.Armed = false;
        }
        press.Down = down;
        return fire;
    }
}
