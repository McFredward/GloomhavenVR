using System;
using System.IO;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class BurnFlightCompletionVectors
{
    internal static void Run(Harness t, string root)
    {
        t.Case("burn: actual native completion, including slow and paused game clocks");
        foreach (float elapsed in new[] { 0f, .49f, .5f, .68f, 2f, 3f, 60f, 600f })
        {
            t.True(!BurnFlightCompletion.MayRelease(true, false, elapsed, .5f), "live shader timeline holds");
            t.True(!BurnFlightCompletion.MayRelease(false, true, elapsed, .5f), "native loss sequence holds after shader handle clears");
            t.True(!BurnFlightCompletion.MayRelease(true, true, elapsed, .5f), "both native timelines hold");
            t.True(BurnFlightCompletion.MayRelease(false, false, elapsed, .5f) == (elapsed >= .5f),
                "only completed or absent timelines may leave after startup grace");
        }
        t.True(new CardFlightSource(4, 1, 2).Validate(), "second active card has independent address");
        t.True(new CardFlightSource(4, 0, 0).Validate(), "recess burn may carry actor only");
        t.True(!new CardFlightSource(0, 0, 0).Validate(), "missing actor refused");
        t.True(!new CardFlightSource(4, 2, 2).Validate(), "out of range active seat refused");
        t.True(!new CardFlightSource(4, 1, 0).Validate(), "actor only source has no seat");
        string rebuild = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Cards/Driver/CardsDriver.4.Rebuild.cs"));
        string flight = RemoteCapVisibilityVectors.Method(rebuild, "private bool TryStartFlyToPile(");
        t.True(HasActiveDestinationSwitch(flight), "actual active departures reach model destination switch");
        t.True(!HasActiveDestinationSwitch(flight.Replace("_lastActiveCards.Contains(card)", "false")),
            "removing the production active prefilter fails the same binding check");
        string flush = RemoteCapVisibilityVectors.Method(rebuild, "private void FlushBurnHolds(");
        int wait = flush.IndexOf("TryTakeBurnFlightSlot", StringComparison.Ordinal);
        int launch = flush.IndexOf("LaunchBurnFlight", StringComparison.Ordinal);
        t.True(wait >= 0 && launch > wait, "focus change cannot bypass native completion");
        t.True(!flush.Contains("_burnHoldSince.Clear()"), "focus change retains pending old actor holds");
    }
    private static bool HasActiveDestinationSwitch(string body) =>
        body.Contains("_lastActiveCards.Contains(card)") && body.Contains("RoundCardExitOf(hand, card")
        && body.Contains("case RoundCardExit.Lost:") && body.Contains("case RoundCardExit.Discarded:");

}
