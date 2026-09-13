using System;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static partial class Program
{
    private static int _assertions;
    private static void Check(bool result, string message)
    {
        _assertions++;
        if (!result) throw new InvalidOperationException(message);
    }

    private static GameObject Hand(bool animating = true)
    {
        var hand = new GameObject();
        hand.AddComponent<CardsHandUI>().AnimatingLostCards = animating;
        return hand;
    }

    private static void Main()
    {
        var manager = new UIManager();
        GameObject foreignHand = Hand();
        // No local VRCard, burn material, actor ownership or remote presentation packet is
        // present: the native hand lock itself must cover the pre-FX window immediately.
        manager.elementsLockUI.Add(foreignHand);
        Check(CardLossModalGuard.OwnsAllLocks(manager, true),
            "Foreign hand native lock suppresses empty fallback before card artwork starts");
        manager.elementsLockUI.Add(new GameObject());
        Check(!CardLossModalGuard.OwnsAllLocks(manager, true),
            "Mixed unrelated lock must retain the real modal fallback");
        manager.elementsLockUI.Clear();
        Check(!CardLossModalGuard.OwnsAllLocks(manager, true),
            "Cancelled unlocked hand must not suppress fallback despite stale animation flag");
        Check(foreignHand.GetComponent<CardsHandUI>()!.AnimatingLostCards,
            "Cancellation fixture retains native animation flag after lock removal");

        GameObject localHand = Hand();
        manager.elementsLockUI.Add(localHand);
        Check(CardLossModalGuard.OwnsAllLocks(manager, true), "Local card-loss lock has the same protection");
        manager.elementsLockUI.Add(foreignHand);
        Check(CardLossModalGuard.OwnsAllLocks(manager, true), "Overlapping native hand transactions remain protected");
        localHand.GetComponent<CardsHandUI>()!.AnimatingLostCards = false;
        Check(!CardLossModalGuard.OwnsAllLocks(manager, true), "A non-animating hand lock is not owned by card loss");
        manager.elementsLockUI.Remove(localHand);
        foreignHand.activeSelf = false;
        Check(CardLossModalGuard.OwnsAllLocks(manager, true), "Presentation-hidden hand keeps its still-held transaction lock");
        Check(!CardLossModalGuard.OwnsAllLocks(manager, false), "Disabled VR cards preserve fallback");
        Check(!CardLossModalGuard.OwnsAllLocks(null, true), "Missing manager preserves fallback");
        Check(!CardLossModalGuard.OwnsAllLocks(new UIManager { Destroyed = true }, true),
            "Destroyed manager preserves fallback");

        manager.elementsLockUI.Clear();
        manager.elementsLockUI.Add(new GameObject());
        Check(!CardLossModalGuard.OwnsAllLocks(manager, true), "Unrelated lock alone preserves fallback");
        manager.elementsLockUI.Clear();
        manager.elementsLockUI.Add(null!);
        Check(!CardLossModalGuard.OwnsAllLocks(manager, true), "Null lock owner preserves fallback");
        manager.elementsLockUI.Clear();
        GameObject destroyedHand = Hand();
        destroyedHand.Destroyed = true;
        manager.elementsLockUI.Add(destroyedHand);
        Check(!CardLossModalGuard.OwnsAllLocks(manager, true), "Destroyed lock owner preserves fallback");
        manager.elementsLockUI.Clear();
        GameObject destroyedComponent = Hand();
        destroyedComponent.GetComponent<CardsHandUI>()!.Destroyed = true;
        manager.elementsLockUI.Add(destroyedComponent);
        Check(!CardLossModalGuard.OwnsAllLocks(manager, true), "Destroyed hand component preserves fallback");

        // Four simultaneous players: every absent/idle/burning/destroyed combination,
        // with and without an unrelated requester. Ownership is universal, not existential.
        for (int encoded = 0; encoded < 256; encoded++)
        for (int unrelated = 0; unrelated < 2; unrelated++)
        {
            manager.elementsLockUI.Clear();
            bool expected = unrelated == 0;
            int liveLocks = 0;
            for (int actor = 0; actor < 4; actor++)
            {
                int state = (encoded >> (actor * 2)) & 3;
                if (state == 0) continue;
                GameObject hand = Hand(state == 2 || state == 3);
                hand.Destroyed = state == 3;
                manager.elementsLockUI.Add(hand);
                liveLocks++;
                expected &= state == 2;
            }
            if (unrelated != 0) manager.elementsLockUI.Add(new GameObject());
            expected &= liveLocks > 0;
            Check(CardLossModalGuard.OwnsAllLocks(manager, true) == expected,
                $"Four-player lock ownership matrix {encoded}/{unrelated}");
            Check(!CardLossModalGuard.OwnsAllLocks(manager, false),
                $"Disabled card presentation matrix {encoded}/{unrelated}");
        }
        manager.elementsLockUI.Clear();
        manager.elementsLockUI.Add(Hand());
        for (int i = 0; i < 10000; i++) CardLossModalGuard.OwnsAllLocks(manager, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int owned = 0;
        for (int i = 0; i < 20000; i++)
            if (CardLossModalGuard.OwnsAllLocks(manager, true)) owned++;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(owned == 20000, "Every steady-state read remains owned");
        Check(allocated == 0, "Steady-state ownership reads allocate no managed memory");
        FlatScreenPolicyCases();
        Console.WriteLine($"Card-loss modal production harness: {_assertions:N0} assertions passed.");
    }
}
