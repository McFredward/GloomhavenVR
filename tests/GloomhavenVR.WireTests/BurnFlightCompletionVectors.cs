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
        t.Case("burn: addressed release and renderer ownership survive focus changes");
        var first = new CardFlightSource(4, 0, 2);
        var second = new CardFlightSource(4, 1, 2);
        var resized = new CardFlightSource(4, 0, 1);
        t.True(BurnReleasePolicy.Matches(4, true, -1, first, 4, CardFxAnchor.Active, first),
            "exact active departure releases its own claim");
        t.True(!BurnReleasePolicy.Matches(4, true, -1, first, 5, CardFxAnchor.Active, first),
            "changing focus cannot let another actor release the old burn");
        t.True(!BurnReleasePolicy.Matches(4, true, -1, first, 4, CardFxAnchor.Active, second),
            "second active card cannot release first claim");
        t.True(!BurnReleasePolicy.Matches(4, true, -1, first, 4, CardFxAnchor.Active, resized),
            "same seat in resized active population is not the old source");
        t.True(!BurnReleasePolicy.Matches(4, true, -1, first, 4, CardFxAnchor.Board, null),
            "generic board event cannot consume an active departure");
        t.True(!BurnReleasePolicy.Matches(4, false, 0, null, 4, CardFxAnchor.Active, first),
            "active departure cannot consume a recess burn");
        t.True(BurnReleasePolicy.Matches(4, false, 0, null, 4, CardFxAnchor.Slot0, null),
            "same actor and recess release normally");
        t.True(!BurnReleasePolicy.Matches(4, false, 0, null, 4, CardFxAnchor.Slot1, null),
            "paired sacrifice releases remain distinct");
        t.True(!BurnReleasePolicy.OwnsBoard(4, 5), "old actor burn cannot suppress new actor recess");
        t.True(BurnReleasePolicy.OwnsBoard(4, 4), "displayed actor burn suppresses its original renderer");
        t.True(!BurnReleasePolicy.OwnsBoard(0, 0), "unknown actors grant no renderer ownership");
        string remoteBurn = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/RemoteBurnFx.cs"));
        string drive = RemoteCapVisibilityVectors.Method(remoteBurn, "private void Drive(");
        t.True(HasBurnCancellation(drive), "recovered or destroyed burn drops claim and presentation");
        t.True(!HasBurnCancellation(drive.Replace("DropClaim(b.ClaimId)", "NoOp()")),
            "negative control: retaining claim after cancellation is detected");
        t.Case("burn: local read-only view consumes canonical release exactly once");
        CardFlightVisibility.Reset();
        byte slot0Burn = (byte)(((byte)CardFxAnchor.Burnt << 4) | (byte)CardFxAnchor.Slot0);
        byte slot1Burn = (byte)(((byte)CardFxAnchor.Burnt << 4) | (byte)CardFxAnchor.Slot1);
        CardFlightVisibility.ObserveOwnerRelease(4, slot0Burn, 1, new CardFlightSource(4, 0, 0));
        CardFlightVisibility.ObserveOwnerRelease(4, slot1Burn, 0, new CardFlightSource(4, 0, 0));
        t.True(!CardFlightVisibility.TryConsumeOwnerRelease(5, CardFxAnchor.Slot0, null, out _),
            "another displayed actor cannot spend the receipt");
        t.True(CardFlightVisibility.TryConsumeOwnerRelease(4, CardFxAnchor.Slot1, null, out byte flags1)
            && flags1 == 0, "second slot consumes independently without inheriting short-rest cover");
        t.True(CardFlightVisibility.TryConsumeOwnerRelease(4, CardFxAnchor.Slot0, null, out byte flags0)
            && flags0 == 1, "first slot retains explicit covered provenance");
        t.True(!CardFlightVisibility.TryConsumeOwnerRelease(4, CardFxAnchor.Slot0, null, out _),
            "canonical release cannot replay after consumption");
        CardFlightVisibility.ObserveOwnerRelease(4, slot0Burn, 0, new CardFlightSource(5, 0, 0));
        t.True(!CardFlightVisibility.TryConsumeOwnerRelease(4, CardFxAnchor.Slot0, null, out _),
            "mismatched source actor is refused before registry insertion");
        CardFlightVisibility.ObserveOwnerRelease(4, slot0Burn, 0, null);
        CardFlightVisibility.Reset();
        t.True(!CardFlightVisibility.TryConsumeOwnerRelease(4, CardFxAnchor.Slot0, null, out _),
            "scenario teardown removes pending receipts");
        string update = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Cards/Driver/CardsDriver.2.Update.cs"));
        string destroyHand = RemoteCapVisibilityVectors.Method(update, "private void OnHandDestroying(");
        t.True(!destroyHand.Contains("_burnHoldSince.Clear()") && destroyHand.Contains("ClearBurnHold(widget)"),
            "one hand teardown cancels only its own pending burns");
        t.True(!destroyHand.Contains("_activeExitOrigins.Clear()") && destroyHand.Contains("_activeExitOrigins.Remove(widget)"),
            "unrelated active departure source survives another hand teardown");
        string remoteFlight = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/RemoteCardFx.cs"));
        string tryPlay = RemoteCapVisibilityVectors.Method(remoteFlight, "private bool TryPlay(");
        t.True(HasDeferredActiveResolve(tryPlay), "active departure waits for its exact model source before acquiring a slab");
        t.True(!HasDeferredActiveResolve(tryPlay.Replace("return false;", "return true;")),
            "negative control: skipping unresolved active departure instead of retrying is detected");
        string tick = RemoteCapVisibilityVectors.Method(remoteFlight, "public void Tick(");
        t.True(WaitsBeforeTravel(tick, "if (!drawable)", "f.Elapsed +="),
            "an unresolved public front cannot consume the whole generic flight unseen");
        t.True(!WaitsBeforeTravel(tick.Replace("continue;", "NoOp();"), "if (!drawable)", "f.Elapsed +="),
            "negative control: advancing hidden travel while waiting for art is detected");
        t.True(WaitsBeforeTravel(drive, "if (b.HandoverLogged && !CanDrawBurn(b))", "b.Elapsed +="),
            "burn travel waits for public artwork with bounded cancellation");
        string rebuild = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Cards/Driver/CardsDriver.4.Rebuild.cs"));
        string fallback = rebuild.Substring(rebuild.IndexOf("private sealed class BurnSlab", StringComparison.Ordinal));
        t.True(HasOriginalFallback(fallback), "recycled local burn uses inert original art, original source size and ceiling arc");
        t.True(!HasOriginalFallback(fallback.Replace("SetLocalNativeAppearance", "NoNativeOutput")),
            "negative control: a clone without original output is refused");
        t.True(!HasOriginalFallback(fallback.Replace("slab._up = Vector3.up", "slab._up = worldUp")),
            "negative control: tilted-board fallback arc is refused");
        t.True(!HasOriginalFallback(fallback.Replace("sourceWorldWidth / (parentLossy * w)", "1f")),
            "negative control: fixed hand-size fallback is refused");
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
    private static bool WaitsBeforeTravel(string body, string wait, string advance)
    {
        int gate = body.IndexOf(wait, StringComparison.Ordinal);
        int elapsed = body.IndexOf(advance, StringComparison.Ordinal);
        int paused = body.IndexOf("continue;", gate >= 0 ? gate : 0, StringComparison.Ordinal);
        return gate >= 0 && paused > gate && elapsed > paused;
    }

    private static bool HasOriginalFallback(string body) => body.Contains("SetLocalNativeAppearance")
        && body.Contains("RemoteAbilityCardSource.ShowFullFace") && body.Contains("slab._up = Vector3.up")
        && body.Contains("sourceWorldWidth / (parentLossy * w)") && body.Contains("_art?.Destroy()");

    private static bool HasDeferredActiveResolve(string body)
    {
        int resolve = body.IndexOf("!RemoteActiveDepartures.TryTake", StringComparison.Ordinal);
        int retry = body.IndexOf("return false;", resolve >= 0 ? resolve : 0, StringComparison.Ordinal);
        int acquire = body.IndexOf("Flight f = Acquire()", StringComparison.Ordinal);
        return resolve >= 0 && retry > resolve && acquire > retry;
    }

    private static bool HasBurnCancellation(string body) => body.Contains("!b.HandoverLogged")
        && body.Contains("!actor.CharacterClass.LostAbilityCards.Contains(card)")
        && body.Contains("!actor.CharacterClass.PermanentlyLostAbilityCards.Contains(card)")
        && body.Contains("DropClaim(b.ClaimId)") && body.Contains("b.Active = false");

    private static bool HasActiveDestinationSwitch(string body) =>
        body.Contains("_lastActiveCards.Contains(card)") && body.Contains("RoundCardExitOf(hand, card")
        && body.Contains("case RoundCardExit.Lost:") && body.Contains("case RoundCardExit.Discarded:");

}
