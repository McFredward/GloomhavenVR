using System;

namespace GloomhavenVR.Cards;

internal enum ProgressHelpMode { Always, Never, AfterDelay }

/// <summary>Owner-only presentation clock. No input, actor or native wait is changed.</summary>
internal sealed class DecisionHelpClock
{
    private object? _owner, _wait, _actor;
    private string? _choice;
    private int _phase, _response;
    private float _since;

    internal bool Observe(string? choice, object? owner, object? wait, object? actor,
        int phase, int response, float now, ProgressHelpMode mode, float delaySeconds)
    {
        if (choice == null || owner == null) { Reset(); return false; }
        if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(wait, _wait)
            || !ReferenceEquals(actor, _actor) || _choice != choice || _phase != phase
            || _response != response || now < _since)
        {
            _owner = owner; _wait = wait; _actor = actor; _choice = choice;
            _phase = phase; _response = response; _since = now;
        }
        if (mode == ProgressHelpMode.Always) return true;
        if (mode == ProgressHelpMode.Never) return false;
        if (float.IsNaN(delaySeconds) || float.IsInfinity(delaySeconds)) delaySeconds = Defaults.ProgressHelpDelaySeconds;
        return now - _since >= Math.Max(0f, delaySeconds);
    }

    internal void Reset() { _owner = _wait = _actor = null; _choice = null; }
}
