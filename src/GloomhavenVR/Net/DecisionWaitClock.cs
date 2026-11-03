namespace GloomhavenVR.Net;

/// <summary>Attention only: never completes, cancels or retries a native decision.</summary>
internal sealed class DecisionWaitClock
{
    private int _player, _context;
    private float _since;
    internal int Tick(int singlePlayer, int context, float now)
    {
        if (singlePlayer <= 0) { _player = 0; return 0; }
        if (_player != singlePlayer || _context != context || now < _since)
        { _player = singlePlayer; _context = context; _since = now; }
        return now - _since >= 20f ? _player : 0;
    }
}
