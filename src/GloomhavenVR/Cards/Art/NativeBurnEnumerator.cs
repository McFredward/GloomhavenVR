using System;
using System.Collections;

namespace GloomhavenVR.Cards;

/// <summary>Observe native timeline steps without changing their yields, lifetime or exceptions.</summary>
internal sealed class NativeBurnEnumerator : IEnumerator, IDisposable
{
    private readonly IEnumerator _native;
    private readonly Action _before;
    private readonly Action<bool> _after;
    private readonly Action<Exception> _report;
    internal NativeBurnEnumerator(IEnumerator native, Action before, Action<bool> after, Action<Exception> report)
    { _native = native; _before = before; _after = after; _report = report; }
    public object Current => _native.Current;
    public bool MoveNext()
    {
        try { _before(); } catch (Exception ex) { _report(ex); }
        bool running = _native.MoveNext();
        try { _after(running); } catch (Exception ex) { _report(ex); }
        return running;
    }
    public void Reset() => _native.Reset();
    public void Dispose() { if (_native is IDisposable disposable) disposable.Dispose(); }
}
