using System;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class ConfirmationOnOwner
{
    private sealed class NavigationGeneration { internal int Value; }
    private static readonly ConditionalWeakTable<Component, NavigationGeneration> NavigationGenerations = new();
    private static readonly ConditionalWeakTable<object, NavigationOrigin> DeferredOrigins = new();
    private static NavigationGeneration NewGeneration(Component source) => new();
    private static int Generation(Component? source) => source == null ? 0 : NavigationGenerations.GetValue(source, NewGeneration).Value;
    private static void AdvanceGeneration(Component source) => NavigationGenerations.GetValue(source, NewGeneration).Value++;
    internal static void CaptureDeferredOrigin(object callback, Component owner)
    {
        DeferredOrigins.Remove(callback);
        DeferredOrigins.Add(callback, new NavigationOrigin(owner));
    }
    internal static NavigationOrigin? DeferredOrigin(object callback) => DeferredOrigins.TryGetValue(callback, out NavigationOrigin? origin) ? origin : null;
    internal static IEnumerator ScopeDeferredCoroutine(Component source, IEnumerator native) => new DeferredCoroutine(source, native);

    private sealed class DeferredCoroutine : IEnumerator, IDisposable
    {
        private readonly Component _source;
        private readonly IEnumerator _native;
        private readonly NavigationOrigin _origin;
        internal DeferredCoroutine(Component source, IEnumerator native)
        { _source = source; _native = native; _origin = new NavigationOrigin(source); }
        public object Current => _native.Current;
        public bool MoveNext()
        {
            Component? previous = Begin(_source);
            NavigationOrigin? origin = CurrentDeferredOrigin;
            CurrentDeferredOrigin = _origin;
            try { return _native.MoveNext(); }
            finally { CurrentScope = previous; CurrentDeferredOrigin = origin; }
        }
        public void Reset() => _native.Reset();
        public void Dispose() { if (_native is IDisposable disposable) disposable.Dispose(); }
    }
}
