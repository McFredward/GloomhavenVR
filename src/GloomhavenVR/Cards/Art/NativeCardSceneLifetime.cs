using System;
using System.Collections;

namespace GloomhavenVR.Cards;

/// <summary>Observe the start of the native load iterator without advancing, replacing or
/// swallowing any native step. Creating (or abandoning) an unstarted iterator has no effect.
/// Admission is sampled on first MoveNext because DataRestoring can change after construction.
/// There is intentionally no shared loading latch; SceneController owns that lifetime.
/// </summary>
internal sealed class NativeCardSceneLifetime : IEnumerator, IDisposable
{
    private readonly IEnumerator _native;
    private readonly Func<bool> _shouldRelease;
    private readonly Action _release;
    private bool _started;

    internal NativeCardSceneLifetime(IEnumerator native, Func<bool> shouldRelease, Action release)
    { _native = native; _shouldRelease = shouldRelease; _release = release; }

    public object Current => _native.Current;

    public bool MoveNext()
    {
        if (!_started)
        {
            _started = true;
            if (_shouldRelease()) _release();
        }
        return _native.MoveNext();
    }

    public void Reset() => _native.Reset();
    public void Dispose()
    {
        if (_native is IDisposable disposable) disposable.Dispose();
    }
}
