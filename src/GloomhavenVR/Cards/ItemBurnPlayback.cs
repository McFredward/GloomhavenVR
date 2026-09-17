using System;
using System.Collections;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Cards;

/// <summary>
/// Native item burns use the game's global clock, while the VR return flourish uses unscaled
/// time. A fixed flourish duration cannot establish completion when that clock pauses. Track
/// the real iterator instead; its yielded objects, exceptions and disposal remain native.
/// ItemCardEffects.isBurning is unused by the game and cannot serve as a lifecycle signal.
/// </summary>
internal static class ItemBurnPlayback
{
    private sealed class State
    {
        internal int Active;
        internal CItem? Model;
        internal bool Completed, Retired, Historical;
    }
    private static readonly ConditionalWeakTable<ItemCardEffects, State> States = new();

    internal static bool Playing(ItemCardEffects? effect) => effect != null
        && States.TryGetValue(effect, out State? state) && state != null && state.Active != 0;

    private static ItemCardUI? Owner(ItemCardEffects effect) => effect.GetComponentInParent<ItemCardUI>();

    /// <summary>A newly hosted already-consumed item needs settled native paint, not another use.</summary>
    internal static void ObserveInitialState(ItemCardUI? card)
    {
        if (card == null || card.cardEffects == null || card.item == null) return;
        Retire(card.cardEffects);
        States.Add(card.cardEffects, new State { Model = card.item,
            Historical = card.item.SlotState == CItem.EItemSlotState.Consumed });
    }

    internal static void Retire(ItemCardEffects? effect)
    {
        if (effect == null) return;
        if (States.TryGetValue(effect, out var old)) old.Retired = true;
        States.Remove(effect);
    }

    private static bool Preserve(ItemCardEffects effect)
    {
        if (!States.TryGetValue(effect, out var state)) return false;
        var item = Owner(effect)?.item;
        if (item == null || !ReferenceEquals(item, state.Model) || item.SlotState != CItem.EItemSlotState.Consumed)
        { Retire(effect); return false; }
        return state.Active != 0 || state.Completed;
    }

    internal static IEnumerator Track(ItemCardEffects effect, IEnumerator original)
    {
        var item = Owner(effect)?.item;
        if (States.TryGetValue(effect, out var old) && !ReferenceEquals(old.Model, item)) Retire(effect);
        return new Playback(States.GetValue(effect, _ => new State { Model = item }), original,
            effect.fgFx != null);
    }

    private sealed class Playback : IEnumerator, IDisposable
    {
        private readonly State _state;
        private readonly IEnumerator _original;
        private readonly bool _historicalReady;
        private bool _started, _finished, _disposed, _yielded;
        internal Playback(State state, IEnumerator original, bool historicalReady)
        { _state = state; _original = original; _historicalReady = historicalReady; }
        public object Current => _original.Current;
        public bool MoveNext()
        {
            if (_finished) return false;
            if (_state.Retired) { Finish(); return false; }
            if (!_started) { _started = true; _state.Active++; }
            try
            {
                bool next = _original.MoveNext();
                _yielded |= next;
                if (!next) { _state.Completed |= _yielded || _state.Historical && _historicalReady; Finish(); }
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

    // ItemCardUI.Show and a subsequent forced UpdateState can request Consumed twice.
    // Unlike ability effects, the native item effect stores no coroutine handle: both ramps
    // then write the same materials concurrently. Stop the duplicate before RestoreCard.
    [HarmonyPatch(typeof(ItemCardEffects), nameof(ItemCardEffects.ToggleEffect))]
    internal static class ToggleEffect_Once
    {
        private static bool Prefix(ItemCardEffects __instance)
        { try { return !Preserve(__instance); } catch { return true; } }
    }

    [HarmonyPatch(typeof(ItemCardEffects), nameof(ItemCardEffects.ToggleAdditiveEffect))]
    internal static class ToggleAdditiveEffect_Once
    {
        private static bool Prefix(ItemCardEffects __instance)
        { try { return !Preserve(__instance); } catch { return true; } }
    }

    [HarmonyPatch(typeof(ItemCardEffects), nameof(ItemCardEffects.RestoreCard))]
    internal static class RestoreCard_Once
    {
        private static bool Prefix(ItemCardEffects __instance)
        { try { return !Preserve(__instance); } catch { return true; } }
    }

    [HarmonyPatch(typeof(ItemCardUI), nameof(ItemCardUI.OnReturnedToPool))]
    internal static class ReturnedToPool_Retire
    {
        private static void Prefix(ItemCardUI __instance)
        { try { Retire(__instance.cardEffects); } catch { /* Native pool cleanup must continue. */ } }
    }

    [HarmonyPatch(typeof(ItemCardEffects), nameof(ItemCardEffects.BurnCardTimeline))]
    internal static class BurnCardTimeline_Track
    {
        private static void Postfix(ItemCardEffects __instance, bool burnAnim, ref IEnumerator __result)
        {
            try
            {
                if (burnAnim && States.TryGetValue(__instance, out var state) && state.Historical)
                {
                    __result = __instance.BurnCardTimeline(burnAnim: false);
                    return;
                }
                __result = Track(__instance, __result);
            }
            catch { /* Keep native execution when presentation interception cannot be built. */ }
        }
    }
}
