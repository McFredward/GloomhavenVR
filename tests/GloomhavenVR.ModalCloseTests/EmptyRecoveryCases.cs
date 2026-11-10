using System;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

internal static class EmptyRecoveryCases
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (bool refused in new[] { true, false })
        {
            ModalFallback.ResetEmptyRecoveryForTest();
            var first = new UIWindow { name = "first permanently empty native window" };
            var second = new UIWindow { name = "later native decision owner" };
            ModalFallback.HoldForRecoveryTest(first, refused);
            ModalFallback.HoldForRecoveryTest(second, refused);
            bool recovered = false;
            // Stable enrollment order is intentional: the old shared6Hz clock
            // gives every due poll to first and never inspects second's content.
            for (int tick = 0; tick < 60; tick++)
            {
                Time.unscaledTime = tick * .05f;
                if (tick == 4) second.transform.Drawn = true;
                check(ModalFallback.EmptyRecoveryForTest(first, refused),
                    "an empty original remains excluded without hiding or answering it");
                bool held = ModalFallback.EmptyRecoveryForTest(second, refused);
                if (!held) { recovered = true; break; }
            }
            check(recovered, refused
                ? "later populated refused window cannot be starved by an earlier empty sibling"
                : "later populated dormant window cannot be starved by an earlier empty sibling");
            check(first.Hides == 0 && second.Hides == 0 && first.Escapes == 0 && second.Escapes == 0,
                "content recovery never closes an original window or dispatches a game decision");

            ModalFallback.ResetEmptyRecoveryForTest();
            var rate = new UIWindow();
            ModalFallback.HoldForRecoveryTest(rate, refused);
            Time.unscaledTime = 10f;
            ModalFallback.EmptyRecoveryForTest(rate, refused);
            int probes = ModalFallback.RecoveryProbeCountForTest;
            for (int call = 0; call < 40; call++) ModalFallback.EmptyRecoveryForTest(rate, refused);
            check(ModalFallback.RecoveryProbeCountForTest == probes,
                "one original retains the existing6Hz probe bound despite repeated callers");
            Time.unscaledTime += .17f; ModalFallback.EmptyRecoveryForTest(rate, refused);
            check(ModalFallback.RecoveryProbeCountForTest == probes + 1,
                "the same still-empty original gets its next due recovery probe");
            rate.IsOpen = false; ModalFallback.EmptyRecoveryForTest(rate, refused);
            rate.IsOpen = true; rate.transform.Drawn = true;
            ModalFallback.HoldForRecoveryTest(rate, refused);
            check(!ModalFallback.EmptyRecoveryForTest(rate, refused),
                "explicit close and reopen does not inherit an earlier opening's probe delay");
        }

        var cadence = new WindowProbeCadence();
        var one = new UIWindow(); var two = new UIWindow();
        check(cadence.Due(one, 1) && cadence.Due(two, 1),
            "every new original is due independently in the same frame");
        check(!cadence.Due(one, 1.01f), "one original cannot consume its own probe twice");
        check(cadence.Due(one, .5f), "a restarted clock cannot strand a previous window deadline");
        cadence.Reset(); check(cadence.Due(one, .5f), "session reset retires each old original deadline");
    }
}
