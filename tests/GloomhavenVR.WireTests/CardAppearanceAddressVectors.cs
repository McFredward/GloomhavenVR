using System;
using System.IO;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class CardAppearanceAddressVectors
{
    private sealed class Widget
    {
        internal readonly object Model;
        internal Widget(object model) { Model = model; }
    }

    internal static void Run(Harness t, string root)
    {
        t.Case("492: dedicated rest widget publishes the canonical pile model's appearance");
        object other = new(), sacrificed = new();
        var arc = new[] { new Widget(other), new Widget(sacrificed) };
        var offer = new Widget(sacrificed);
        t.True(Array.IndexOf(arc, offer) == -1, "negative control: dialog widget is absent from the pile arc");
        t.True(CardAppearanceAddress.TryPile(arc, offer.Model, w => w.Model, false, out byte code, out byte count)
            && code == 0x41 && count == 2, "discard offer names the exact model at canonical discard seat one");
        var lost = new[] { new Widget(sacrificed) };
        t.True(CardAppearanceAddress.TryPile(lost, offer.Model, w => w.Model, true, out code, out count)
            && code == 0x60 && count == 1, "same dialog adopts the new canonical lost address after burn commit");
        t.True(!CardAppearanceAddress.TryPile(new[] { arc[0] }, offer.Model, w => w.Model, false, out code, out count)
            && code == 0 && count == 0, "removed source cannot keep its stale discard address");
        t.True(!CardAppearanceAddress.TryPile(arc, new object(), w => w.Model, false, out code, out count)
            && code == 0 && count == 0, "different actor/model is never guessed from the same widget position");
        t.True(!CardAppearanceAddress.TryPile(new[] { arc[1], lost[0] }, sacrificed, w => w.Model,
            false, out _, out _), "ambiguous duplicate model cannot select an arbitrary seat");
        var large = new Widget[256];
        for (int i = 0; i < large.Length; i++) large[i] = new Widget(new object());
        t.True(!CardAppearanceAddress.TryPile(large, large[0].Model, w => w.Model, false, out _, out _),
            "wire count overflow cannot wrap to a valid-looking address");
        var maximum = new Widget[255]; Array.Copy(large, maximum, maximum.Length);
        t.True(CardAppearanceAddress.TryPile(maximum, maximum[30].Model, w => w.Model, false, out code, out count)
            && code == 0x5e && count == 255, "last nameable seat and maximum count remain supported");
        t.True(!CardAppearanceAddress.TryPile(maximum, maximum[31].Model, w => w.Model, false, out _, out _),
            "unknown sentinel never names a real card");

        string sampler = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/CardAppearanceSampler.cs"));
        t.True(sampler.Contains("CardsHandManager.Instance?.GetHand(actor)")
            && sampler.Contains("CardsGameApi.GetPileArcWidgets(hand, burnt: false, Pile)")
            && sampler.Contains("CardsGameApi.GetPileArcWidgets(hand, burnt: true, Pile)")
            && sampler.Contains("CardAppearanceAddress.TryPile(Pile, card.GameCard.AbilityCard"),
            "production capture binds the card's own actor and model to the same pile arcs as the receiver");
        t.True(sampler.Contains("card.PreserveSpentBurnAppearance();"),
            "owner appearance is corrected before publication as well as before drawing");
    }
}
