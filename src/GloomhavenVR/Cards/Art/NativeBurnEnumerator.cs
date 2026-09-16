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
    internal bool Started { get; private set; }
    internal bool Finished { get; private set; }
    internal NativeBurnEnumerator(IEnumerator native, Action before, Action<bool> after, Action<Exception> report)
    { _native = native; _before = before; _after = after; _report = report; }
    public object Current => _native.Current;
    public bool MoveNext()
    {
        try { _before(); } catch (Exception ex) { _report(ex); }
        Started = true;
        bool running;
        try { running = _native.MoveNext(); }
        catch { Finished = true; throw; }
        if (!running) Finished = true;
        try { _after(running); } catch (Exception ex) { _report(ex); }
        return running;
    }
    public void Reset() => _native.Reset();
    public void Dispose()
    {
        try { if (_native is IDisposable disposable) disposable.Dispose(); }
        finally { Finished = true; }
    }
}
