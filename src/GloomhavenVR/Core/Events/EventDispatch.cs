using System;
using System.Runtime.CompilerServices;

namespace GloomhavenVR.Core.Events;

/// <summary>Deliver every observer in the original subscription order, even if another
/// observer fails. This is mod notification isolation, never native action recovery.</summary>
internal static class EventDispatch<T> where T : struct
{
    // A multicast delegate is an immutable subscription snapshot. Cache its entries once,
    // rather than allocate GetInvocationList on every choreographer/hover notification.
    // The weak key also allows retired scene subscribers to be collected after unsubscription.
    private static readonly ConditionalWeakTable<Action<T>, Delegate[]> Entries = new();

    internal static void Invoke(Action<T>? handler, in T payload, string eventName)
    {
        if (handler == null) return;
        Delegate[] subscribers = Entries.GetValue(handler, Snapshot);
        foreach (Delegate entry in subscribers)
        {
            try { ((Action<T>)entry)(payload); }
            catch (Exception error)
            {
                // A failed unlock/scene-change observer previously skipped all later
                // observers permanently: there need not be another edge to repair their
                // stale mode or input latch. Preserve multicast snapshot/reentrancy
                // semantics, but contain each failure at its own observer boundary.
                try
                {
                    string receiver = entry.Method.DeclaringType?.FullName + "." + entry.Method.Name;
                    TickGuard.NoteThrow("Events", "Event/" + eventName + "/" + receiver, error,
                        "Subscriber to " + eventName + " (" + receiver + ")",
                        "later subscribers still receive the original notification.",
                        "Fix the failing observer; native actions and decisions remain unchanged.");
                }
                catch { /* A diagnostic failure must not starve the remaining observers either. */ }
            }
        }
    }

    private static Delegate[] Snapshot(Action<T> handler) => handler.GetInvocationList();
}
