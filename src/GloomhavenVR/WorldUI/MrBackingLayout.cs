using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Back the measured window, including real full-frame artwork, with a small authored-pixel
/// margin. Confirm a new extent on two independent geometry samples before presenting it;
/// a single native layout/reveal frame must not flash an opaque room-sized rectangle. Continuous
/// native layout may still advance after one animation interval, rather than waiting forever
/// for identical samples. This is presentation only, with no native UI or gameplay writes.
/// </summary>
internal sealed class MrBackingLayout
{
    internal const int SampleStrideFrames = 4;
    // Match the handle's shipped 150 ms ease without consulting a viewer-local dial. The same
    // native owner samples must produce the same animation on every observer's board.
    internal const float DurationSeconds = Defaults.GrabBarTweenMs * 0.001f;

    internal static bool ReadyForSample(bool fitApplied, Rect frame) => fitApplied && Usable(frame);
    private bool _seeded;
    private Rect _shown, _from, _target, _candidate;
    private float _started, _candidateStarted;
    private bool _pending;
    private int _sample = -1, _candidateSamples;

    internal static Rect WindowRect(Rect frame, Rect ink, bool painted, float plateBottom)
    {
        Rect content = ink;
        if (painted)
            content = Rect.MinMaxRect(Mathf.Min(ink.xMin, frame.xMin),
                Mathf.Min(Mathf.Min(ink.yMin, frame.yMin), plateBottom),
                Mathf.Max(ink.xMax, frame.xMax), Mathf.Max(ink.yMax, frame.yMax));
        const float margin = 8f;
        return Rect.MinMaxRect(content.xMin - margin, content.yMin - margin,
                               content.xMax + margin, content.yMax + margin);
    }

    internal bool Present(Rect bounds, bool visible, int sample, float now, float duration,
                          out Rect shown)
    {
        if (!visible || !Usable(bounds))
        {
            Reset();
            shown = default;
            return false;
        }

        Advance(now, duration);
        if (sample != _sample)
        {
            _sample = sample;
            if (_seeded && Same(bounds, _target))
            {
                _pending = false;
            }
            else
            {
                bool agrees = _pending && Same(bounds, _candidate);
                if (!_pending)
                {
                    _candidateStarted = now;
                    _candidateSamples = 0;
                }
                _pending = true;
                _candidate = bounds;
                _candidateSamples++;
                if (_candidateSamples >= 2
                    && (agrees || now - _candidateStarted >= Mathf.Max(duration, 0.05f)))
                {
                    if (!_seeded)
                        _shown = new Rect(bounds.center, Vector2.zero);
                    _seeded = true;
                    _from = _shown;
                    _target = bounds;
                    _started = now;
                    _pending = false;
                }
            }
        }
        Advance(now, duration);
        shown = _shown;
        return _seeded;
    }

    internal void Reset()
    {
        _seeded = false;
        _pending = false;
        _sample = -1;
        _candidateSamples = 0;
        _shown = default;
    }

    private void Advance(float now, float duration)
    {
        if (!_seeded)
            return;
        float t = duration <= 0f ? 1f : Mathf.Clamp01((now - _started) / duration);
        float inverse = 1f - t;
        float ease = 1f - inverse * inverse * inverse;
        _shown = new Rect(Vector2.Lerp(_from.position, _target.position, ease),
                          Vector2.Lerp(_from.size, _target.size, ease));
    }

    private static bool Usable(Rect r) => r.width > 0.0001f && r.height > 0.0001f
        && !float.IsNaN(r.x) && !float.IsNaN(r.y)
        && !float.IsInfinity(r.x) && !float.IsInfinity(r.y)
        && !float.IsInfinity(r.width) && !float.IsInfinity(r.height);

    private static bool Same(Rect a, Rect b)
    {
        float tolerance = Mathf.Max(0.00001f, Mathf.Max(b.width, b.height) * 0.005f);
        return Mathf.Abs(a.xMin - b.xMin) <= tolerance
            && Mathf.Abs(a.xMax - b.xMax) <= tolerance
            && Mathf.Abs(a.yMin - b.yMin) <= tolerance
            && Mathf.Abs(a.yMax - b.yMax) <= tolerance;
    }
}
