using System;
using System.Collections.Generic;
using GloomhavenVR.Core;

namespace GloomhavenVR.Cards;

/// <summary>
/// Serialized executor for game calls that may block the main thread.
///
/// Why: <c>CardsHandUI.OnCardSelected/OnCardDeselected</c> spin-wait up to 1 s for the
/// ScenarioRuleLibrary worker ack (CARDS.md §8; bodies verified — see
/// <see cref="CardsGameApi"/> remarks). The P2 threading rules forbid blocking inside
/// interaction callbacks / bus handlers, so grab-release, poke and palm-gate code
/// enqueues here and <see cref="CardsDriver"/> pumps ONE action per Update frame:
/// the block lands on a single frame boundary (reprojection covers it) and never
/// re-enters game code from within another callback.
///
/// <c>onDone</c> runs immediately after the action on the same frame —
/// by then the SRL has acked (or timed out), so game state reads are final and the
/// caller can verify the outcome (e.g. "did the card actually enter the round?").
/// </summary>
internal static class CardActionQueue
{
    private readonly struct Entry
    {
        public readonly Action Action;
        public readonly Action? OnDone;

        public Entry(Action action, Action? onDone)
        {
            Action = action;
            OnDone = onDone;
        }
    }

    private static readonly Queue<Entry> Queue = new(8);

    internal static int Pending => Queue.Count;

    internal static void Enqueue(Action action, Action? onDone = null)
    {
        if (action == null)
            return;
        Queue.Enqueue(new Entry(action, onDone));
    }

    /// <summary>Called once per frame by <see cref="CardsDriver"/>. Executes at most one entry.</summary>
    internal static void Pump()
    {
        if (Queue.Count == 0)
            return;

        Entry entry = Queue.Dequeue();
        try
        {
            entry.Action();
        }
        catch (Exception ex)
        {
            VRLog.Error("Cards", $"Queued card action threw: {ex}");
        }
        if (entry.OnDone == null)
            return;
        try
        {
            entry.OnDone();
        }
        catch (Exception ex)
        {
            VRLog.Error("Cards", $"Card action completion callback threw: {ex}");
        }
    }

    internal static void Clear() => Queue.Clear();
}
