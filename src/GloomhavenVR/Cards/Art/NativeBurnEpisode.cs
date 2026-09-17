namespace GloomhavenVR.Cards;

/// <summary>One native animated burn per card ownership episode, including its settled tail.</summary>
internal sealed class NativeBurnEpisode<T> where T : class
{
    private T? _card;
    private bool _running, _completed, _leftRecoveryPile;

    internal void Clear() { _card = null; _running = _completed = _leftRecoveryPile = false; }
    internal void CancelIfRunning() { if (_running) Clear(); }

    internal void Observe(T card, bool recovered, bool running)
    {
        if (!ReferenceEquals(_card, card)) Clear();
        _card = card;
        _leftRecoveryPile |= !recovered;
        if (running) _running = true;
        else if (_running) { _running = false; _completed = true; }
    }

    internal bool Preserve(T card, bool recovered, bool durable, bool resetting)
    {
        if (!ReferenceEquals(_card, card) || recovered && _leftRecoveryPile)
        { Clear(); return false; }
        _leftRecoveryPile |= !recovered;
        // A restore on a recovered hand/round card ends a completed action episode. Merely
        // changing BurnCard to LostMode, or repainting a lost pile, never creates a new burn.
        bool preserve = _running || _completed && (!resetting || durable);
        if (!preserve && resetting) Clear();
        return preserve;
    }
}
