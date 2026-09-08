using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class FanExchangeVectors
{
    internal static void Run(Harness t)
    {
        t.Case("fan exchange: map-room and scenario motion identities remain independent");
        var mapA = FanExchangeIdentity.Map(0x80000001);
        var mapB = FanExchangeIdentity.Map(0x80000002);
        var actorA = FanExchangeIdentity.Scenario(unchecked((int)0x80000001));
        var actorB = FanExchangeIdentity.Scenario(unchecked((int)0x80000002));
        t.True(mapA.ShouldExchangeTo(mapB, true, 10, 10),
               "equal-size map hands exchange when the loadout character changes");
        t.True(actorA.ShouldExchangeTo(actorB, true, 8, 8),
               "the existing scenario exchange accepts negative stable actor ids");
        t.True(!mapA.ShouldExchangeTo(mapA, true, 10, 9), "a plucked card is not a character exchange");
        t.True(!actorA.ShouldExchangeTo(actorA, true, 8, 9), "same-character refresh preserves the running animation");
        t.True(!mapA.ShouldExchangeTo(mapB, false, 10, 10), "a newly raised map fan uses the opening animation");
        t.True(!actorA.ShouldExchangeTo(actorB, false, 8, 8), "a hidden scenario fan cannot exchange");
        t.True(mapA.ShouldExchangeTo(mapB, true, 10, 0), "an empty incoming map hand still gathers its predecessor");
        t.True(actorA.ShouldExchangeTo(actorB, true, 8, 0), "an empty incoming scenario hand must not take the close path");
        t.True(mapA.ShouldExchangeTo(mapB, true, 0, 10), "a visible empty outgoing fan may deal the new hand");
        t.True(!mapA.ShouldExchangeTo(mapB, true, 0, 0), "two empty hands have no exchange slabs");
        t.True(!FanExchangeIdentity.Map(0).ShouldExchangeTo(mapB, true, 10, 10), "unknown map origin establishes a baseline");
        t.True(!mapA.ShouldExchangeTo(FanExchangeIdentity.Map(0), true, 10, 10), "missing map identity is not a switch");
        t.True(!FanExchangeIdentity.Scenario(0).ShouldExchangeTo(actorB, true, 8, 8), "unknown scenario origin establishes a baseline");
        t.True(!actorA.ShouldExchangeTo(FanExchangeIdentity.Scenario(0), true, 8, 8), "missing scenario identity is not a switch");
        t.True(!mapA.ShouldExchangeTo(actorB, true, 10, 8), "entering a scenario cannot compare its actor id with a map key");
        t.True(!actorA.ShouldExchangeTo(mapB, true, 8, 10), "leaving a scenario cannot compare its actor id with a map key");
        t.True(mapB.ShouldExchangeTo(mapA, true, 10, 10), "switching back mid-wave starts the reverse identity edge");
        t.True(!mapB.ShouldExchangeTo(mapB, true, 10, 10), "a repeated snapshot cannot restart the exchange");
    }
}
