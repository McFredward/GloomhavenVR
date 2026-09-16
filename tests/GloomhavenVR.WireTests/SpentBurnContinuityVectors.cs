using System;
using System.Collections;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class SpentBurnContinuityVectors
{
    internal static void Run(Harness t)
    {
        t.Case("spent burn keeps observed owner channels across native reset, identity and recovery");
        var card = new object(); var other = new object();
        var continuity = new SpentBurnContinuity<object>();
        float[] spent = { 1f, .8f, .5f };
        continuity.Apply(card, true, false, spent);
        float[] reset = { 0f, 0f, 0f };
        continuity.Apply(card, true, false, reset); // Same spent widget was refreshed before confirming.
        continuity.Apply(card, false, true, reset); // Model moved to Lost before its burn callback.
        t.True(reset[0] == 1f && reset[1] == .8f && reset[2] == .5f,
            "native zero reset retains actual spent wash even after lost membership moved");
        float[] progressing = { .2f, .9f, .6f };
        continuity.Apply(card, false, true, progressing);
        t.True(progressing[0] == 1f && progressing[1] == .9f && progressing[2] == .6f,
            "later native values above the observed floor remain native");
        float[] otherStart = { .05f, 0f, 0f };
        continuity.Apply(other, false, true, otherStart);
        t.True(otherStart[0] == .05f && otherStart[1] == 0f, "a reused surface cannot inherit another model's wash");
        continuity.Apply(card, true, false, spent);
        continuity.Apply(card, false, false, new float[3]); // Recovered into Hand/Round.
        float[] recovered = { .1f, .1f, .1f };
        continuity.Apply(card, false, true, recovered);
        t.True(recovered[0] == .1f, "a later hand sacrifice never resurrects pre-rest spent history");
        continuity.Apply(card, true, false, spent);
        float[] differentGraph = { 0f };
        continuity.Apply(card, false, true, differentGraph);
        t.True(differentGraph[0] == 0f, "different property population cannot receive another graph's channel positions");
        continuity.Clear();
        float[] firstVisibleBurn = { .2f, 0f, float.NaN };
        continuity.Apply(card, false, true, firstVisibleBurn);
        t.True(firstVisibleBurn[0] == .2f && float.IsNaN(firstVisibleBurn[2]),
            "already burning with no observed baseline never invents a grey look");
        // ToggleEffect prefix observes initialized original output before the first VR update.
        continuity.Apply(card, true, false, new[] { .75f, float.NaN, .4f });
        float[] firstNativeStep = { 0f, 0f, 0f };
        continuity.Apply(card, false, true, firstNativeStep);
        t.True(firstNativeStep[0] == .75f && firstNativeStep[1] == 0f && firstNativeStep[2] == .4f,
            "first native burn step uses pre-reset observation and ignores unsupported properties");

        continuity.Apply(card, true, false, spent);
        continuity.Apply(card, false, false, new float[3]); // Ordinary Activated is not discard history.
        continuity.Apply(card, true, false, new float[3]); // Prefix captures its current blue output.
        float[] activeBlue = { 0f, 0f, 0f };
        continuity.Apply(card, false, true, activeBlue);
        t.True(activeBlue[0] == 0f, "an activated card returned to blue cannot resurrect older ghost output");

        continuity.Apply(card, true, false, spent);
        continuity.Clear(); // Native recovery toggle or retirement of an idle VR wrapper.
        continuity.Apply(card, true, false, new float[3]); // Next prefix sees already-Lost hand sacrifice.
        var afterHiddenRecovery = new float[3];
        continuity.Apply(card, false, true, afterHiddenRecovery);
        t.True(afterHiddenRecovery[0] == 0f,
            "hidden recovery invalidation prevents old discard history surviving until another loss");

        t.Case("last spent owner frame retains provenance across lost transition only");
        var history = new SpentAppearanceHistory<object, object>(); var frame = new object();
        history.Remember(10, card, frame);
        t.True(history.TryGet(10, card, true, false, out var actual) && ReferenceEquals(frame, actual),
            "same owner actor and model can continue actual spent output after changing pile");
        t.True(!history.TryGet(11, card, true, false, out _), "another actor cannot borrow spent output");
        t.True(!history.TryGet(10, other, true, false, out _), "another same-seat card cannot borrow spent output");
        t.True(!history.TryGet(10, card, false, false, out _), "no lost membership means no burn fallback authority");
        t.True(!history.TryGet(10, card, true, true, out _), "recovery wins over a stale lost list stamp");
        t.True(!history.TryGet(10, card, true, false, out _), "recovered history never resurrects on another loss");
        history.Remember(10, card, frame);
        history.RemoveRecovered((actor, model) => actor == 10 && ReferenceEquals(model, card));
        t.True(!history.TryGet(10, card, true, false, out _), "receive-time recovery clears history before surface lookup");
        var anotherSender = new SpentAppearanceHistory<object, object>();
        t.True(!anotherSender.TryGet(10, card, true, false, out _), "per-sender histories cannot borrow another peer's output");

        t.Case("native burn iterator retains exact yields, completion, cancellation and exceptions");
        float material = 1f, raw = 0f; int step = 0; int reports = 0;
        var token = new object();
        var native = new Probe(() => { material = ++step == 1 ? .5f : 1f; return step <= 2; }, token);
        var wrapped = new NativeBurnEnumerator(native,
            () => material = raw,
            running => { raw = material; if (running) material = Math.Max(1f, material); },
            _ => reports++);
        t.True(!wrapped.Started && !wrapped.Finished, "creating a native iterator never advances or completes it");
        t.True(wrapped.MoveNext() && raw == .5f && material == 1f, "raw half-progress remains separate from full spent picture");
        t.True(wrapped.Started && !wrapped.Finished, "spent shader floor cannot masquerade as native completion");
        t.True(ReferenceEquals(wrapped.Current, token), "native yield instruction is forwarded by identity");
        t.True(wrapped.MoveNext() && raw == 1f, "native final jump to the exact cosmetic floor is observed");
        t.True(!wrapped.MoveNext() && step == 3, "native completion result and number of steps remain unchanged");
        t.True(wrapped.Finished, "actual completion is retained despite a stale native Coroutine handle");
        wrapped.Reset(); t.True(native.Resets == 1, "Reset delegates to the original enumerator");
        wrapped.Dispose(); t.True(native.Disposals == 1, "cancellation disposes the original iterator");
        int advances = 0;
        var canceledNative = new Probe(() => { advances++; return true; }, token);
        new NativeBurnEnumerator(canceledNative, () => { }, _ => { }, _ => { }).Dispose();
        t.True(advances == 0 && canceledNative.Disposals == 1, "cancellation never advances the native timeline");
        var expected = new InvalidOperationException("native failure");
        var failing = new NativeBurnEnumerator(new Probe(() => throw expected, token), () => { }, _ => { }, _ => reports++);
        Exception? caught = null;
        try { failing.MoveNext(); } catch (Exception ex) { caught = ex; }
        t.True(ReferenceEquals(caught, expected) && reports == 0, "native exceptions propagate unchanged");
        t.True(failing.Finished, "failed native iterator cannot retain a presentation hold");
        var bailed = new NativeBurnEnumerator(new Probe(() => false, token), () => { }, _ => { }, _ => { });
        t.True(!bailed.MoveNext() && bailed.Finished, "synchronous bail is terminal even with existing spent paint");
        var disposed = new NativeBurnEnumerator(new Probe(() => true, token), () => { }, _ => { }, _ => { });
        disposed.MoveNext(); disposed.Dispose();
        t.True(disposed.Finished, "native disposal releases presentation completion");
        var cosmetic = new NativeBurnEnumerator(new Probe(() => true, token),
            () => throw new Exception("cosmetic before"), _ => throw new Exception("cosmetic after"), _ => reports++);
        t.True(cosmetic.MoveNext() && reports == 2, "cosmetic failures cannot alter native execution");
    }
    private sealed class Probe : IEnumerator, IDisposable
    {
        private readonly Func<bool> _move;
        public int Resets, Disposals;
        internal Probe(Func<bool> move, object current) { _move = move; Current = current; }
        public object Current { get; }
        public bool MoveNext() => _move();
        public void Reset() => Resets++;
        public void Dispose() => Disposals++;
    }
}
