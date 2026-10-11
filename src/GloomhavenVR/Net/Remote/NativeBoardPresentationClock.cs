using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>One owner frame pair for every native board surface, latched once per rendered frame.
/// Mirror rebuilds and viewer layout/visibility cannot reseed or advance a second timeline.</summary>
internal sealed class NativeBoardPresentationClock
{
    private readonly UseBarAnimationPlaybackClock _clock = new();
    private uint _generation;
    private bool _started;
    private int _frame = -1;
    private NativeBoardState? _from, _to;
    private float _progress;
    internal void Select(NativeBoardState latest, List<NativeBoardState>? history,
        out NativeBoardState from, out NativeBoardState to, out float progress)
    {
        if (_frame == Time.frameCount && _generation == latest.Generation && _from != null && _to != null)
        { from = _from; to = _to; progress = _progress; return; }
        int first = history?.Count ?? 0;
        if (history != null)
            for (int i = history.Count - 1; i >= 0; i--)
            { if (history[i].Generation != latest.Generation) break; first = i; }
        if (!_started || _generation != latest.Generation)
        {
            _started = true; _generation = latest.Generation;
            _clock.Reset(history != null && first < history.Count ? history[first].SampleTime : latest.SampleTime, Time.unscaledTime);
        }
        float cursor = _clock.Advance(Time.unscaledTime, latest.SampleTime);
        from = to = latest;
        if (history != null)
            for (int i = first; i < history.Count; i++)
            {
                NativeBoardState item = history[i];
                if (i == first || item.SampleTime <= cursor) from = to = item;
                if (item.SampleTime > cursor) { to = item; break; }
            }
        progress = _clock.Progress(from.SampleTime, to.SampleTime);
        _from = from; _to = to; _progress = progress; _frame = Time.frameCount;
    }
}
