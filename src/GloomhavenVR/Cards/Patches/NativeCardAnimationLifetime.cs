using System.Runtime.CompilerServices;
using HarmonyLib;

namespace GloomhavenVR.Cards.Patches;

/// <summary>
/// Finish the native hand's animation-liveness bookkeeping after its own successful
/// cancellation. CancelAnimateCardLost already stops its iterator, clears the tweens,
/// restores layout, releases the UI lock and invokes the original completion callback,
/// but omits the iterator's final AnimatingLostCards=false. The stale presentation flag
/// can then block Choreographer.WaitTriggerAnyOnLongRestAddActiveBonuses indefinitely.
/// No card, rule phase, callback, acknowledgment or player choice is synthesized.
/// </summary>
internal static class NativeCardAnimationLifetime
{
    internal sealed class Generation
    {
        internal uint Value;
    }

    internal readonly struct Cancellation
    {
        internal readonly Generation? Generation;
        internal readonly uint Value;
        internal readonly bool WasRunning;

        internal Cancellation(Generation generation, bool running)
        { Generation = generation; Value = generation.Value; WasRunning = running; }
    }

    private static readonly ConditionalWeakTable<CardsHandUI, Generation> Generations = new();
    private static int _repairs;

    // Observe construction as well as execution: even a replacement iterator which has not
    // reached its first MoveNext belongs to a new request. Refuse to clear its flag.
    internal static void Starting(CardsHandUI hand)
    {
        Generation generation = Generations.GetValue(hand, _ => new Generation());
        generation.Value = unchecked(generation.Value + 1);
    }

    internal static Cancellation Cancelling(CardsHandUI hand) =>
        new(Generations.GetValue(hand, _ => new Generation()),
            hand.animatedLosingCard && hand.AnimatingLostCards);

    internal static void Cancelled(CardsHandUI hand, Cancellation cancellation)
    {
        if (!cancellation.WasRunning || cancellation.Generation == null
            || cancellation.Generation.Value != cancellation.Value
            || hand.animatedLosingCard || !hand.AnimatingLostCards)
            return;

        // This is the native UI's completed cancellation endpoint, not a timeout repair.
        // Its original exception bypasses the postfix; reentrant replacement requests carry
        // another generation even when they finished before the outer callback returned.
        hand.AnimatingLostCards = false;
        int repairs = ++_repairs;
        if (repairs <= 4 || (repairs & (repairs - 1)) == 0)
            Core.VRLog.Alert("Cards", $"CARD ANIMATION CANCEL COMPLETED (#{repairs}): original hand cleanup "
                + "finished; released its stale animation flag without repeating the completion callback.");
    }
}

[HarmonyPatch(typeof(CardsHandUI), "AnimateCardsLost")]
internal static class CardsHandUI_AnimateCardsLost_Lifetime
{
    private static void Prefix(CardsHandUI __instance) => NativeCardAnimationLifetime.Starting(__instance);
}

[HarmonyPatch(typeof(CardsHandUI), "CancelAnimateCardLost")]
internal static class CardsHandUI_CancelAnimateCardLost_Lifetime
{
    private static void Prefix(CardsHandUI __instance, out NativeCardAnimationLifetime.Cancellation __state) =>
        __state = NativeCardAnimationLifetime.Cancelling(__instance);

    private static void Postfix(CardsHandUI __instance, NativeCardAnimationLifetime.Cancellation __state) =>
        NativeCardAnimationLifetime.Cancelled(__instance, __state);
}
