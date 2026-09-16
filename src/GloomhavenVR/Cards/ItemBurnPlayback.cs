using System;
using System.Collections;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace GloomhavenVR.Cards;

/// <summary>
/// Native item burns use the game's global clock, while the VR return flourish uses unscaled
/// time. A fixed flourish duration cannot establish completion when that clock pauses. Track
/// the real iterator instead; its yielded objects, exceptions and disposal remain native.
/// ItemCardEffects.isBurning is unused by the game and cannot serve as a lifecycle signal.
/// </summary>
internal static class ItemBurnPlayback
{
    private sealed class State { internal int Active; }
    private static readonly ConditionalWeakTable<ItemCardEffects, State> States = new();

    internal static bool Playing(ItemCardEffects? effect) => effect != null
        && States.TryGetValue(effect, out State? state) && state != null && state.Active != 0;

    internal static IEnumerator Track(ItemCardEffects effect, IEnumerator original) =>
        new Playback(States.GetValue(effect, _ => new State()), original);

    private sealed class Playback : IEnumerator, IDisposable
    {
        private readonly State _state;
        private readonly IEnumerator _original;
        private bool _started, _finished, _disposed;
        internal Playback(State state, IEnumerator original) { _state = state; _original = original; }
        public object Current => _original.Current;
        public bool MoveNext()
        {
            if (_finished) return false;
            if (!_started) { _started = true; _state.Active++; }
            try
            {
                bool next = _original.MoveNext();
                if (!next) Finish();
                return next;
            }
            catch { Finish(); throw; }
        }
        public void Reset() => _original.Reset();
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { (_original as IDisposable)?.Dispose(); }
            finally { Finish(); }
        }
        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (_started) _state.Active--;
        }
    }

    [HarmonyPatch(typeof(ItemCardEffects), nameof(ItemCardEffects.BurnCardTimeline))]
    internal static class BurnCardTimeline_Track
    {
        private static void Postfix(ItemCardEffects __instance, ref IEnumerator __result) =>
            __result = Track(__instance, __result);
    }
}
