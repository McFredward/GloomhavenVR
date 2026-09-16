using GloomhavenVR.Net;
using ScenarioRuleLibrary;
using UnityEngine;
internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void Main()
    {
        var actor = new CPlayerActor { Id = 3 };
        CardAppearanceProvenance.Actors[3] = actor;
        var hand = new CardsHandUI(); CardsHandManager.Instance.Hands[actor] = hand;
        for (int i = 0; i < 32; i++) actor.CharacterClass.Pool.Add(new());
        for (int i = 0; i < 2; i++)
        {
            var card = actor.CharacterClass.Pool[i]; actor.CharacterClass.LostAbilityCards.Add(card);
            var widget = new AbilityCardUI { PlayerActor = actor, AbilityCard = card };
            widget.fullAbilityCard.cardEffects.Value = 0.73f + i / 10f; hand.Cards.Add(widget);
        }
        Time.unscaledTime = 12.5f;
        Check(CardAppearanceSampler.RetainBurnCompletion(hand.Cards[0], hand.Cards[0].fullAbilityCard) == 12.5f,
            "Completion reports the actual owner appearance clock");
        var first = CardAppearanceSampler.Retained();
        Check(first.Count == 1 && first[0].Nodes[0] == 0.73f, "Completed native pixels survive departure of the visible wrapper");
        hand.Cards[0].fullAbilityCard.cardEffects.Value = 0;
        Check(CardAppearanceSampler.Retained()[0].Nodes[0] == 0.73f, "Native widget reuse cannot alter retained completed pixels");
        Check(ReferenceEquals(first[0], CardAppearanceSampler.Retained()[0]), "Unchanged final pixels reuse their immutable snapshot");
        var live = first[0].Copy(); live.Nodes[0] = 0.91f;
        var joined = CardAppearanceSampler.Retained(live);
        Check(joined.Count == 1 && ReferenceEquals(joined[0], live), "Visible original wins over its retained completion without duplicate address");
        Time.unscaledTime = 13f;
        CardAppearanceSampler.RetainBurnCompletion(hand.Cards[1], hand.Cards[1].fullAbilityCard);
        hand.Cards.Reverse();
        var moved = CardAppearanceSampler.Retained();
        Check(moved.Count == 2 && first[0].FaceCode == 1, "Address refresh must not mutate previously published snapshots");
        Check(moved.Find(s => s.PoolSeat == 0)!.FaceCode == 2 && moved.Find(s => s.PoolSeat == 0)!.Nodes[0] == 0.73f,
            "Burnt list reorder changes the address without swapping completed card artwork");
        actor.CharacterClass.HandAbilityCards.Add(actor.CharacterClass.Pool[0]);
        Check(CardAppearanceSampler.Retained().Count == 1, "Recovery retires old burn completion before card reuse");
        actor.CharacterClass.Pool[1] = new CAbilityCard();
        Check(CardAppearanceSampler.Retained().Count == 0, "Immutable roster replacement retires stale completed pixels");
        CardAppearanceSampler.ResetForTest(); hand.Cards.Clear(); actor.CharacterClass.HandAbilityCards.Clear();
        actor.CharacterClass.LostAbilityCards.Clear();
        for (int i = 0; i < 32; i++)
        {
            var card = actor.CharacterClass.Pool[i]; actor.CharacterClass.LostAbilityCards.Add(card);
            hand.Cards.Add(new AbilityCardUI { PlayerActor = actor, AbilityCard = card });
        }
        foreach (var widget in hand.Cards) CardAppearanceSampler.RetainBurnCompletion(widget, widget.fullAbilityCard);
        Check(CardAppearanceSampler.Retained().Count == 32, "A full native card population retains every completion");
        var allLive = new CardAppearanceState[32];
        for (int i = 0; i < 32; i++) allLive[i] = new CardAppearanceState { ActorId = 99, PoolSeat = (ushort)i };
        Check(CardAppearanceSampler.Retained(allLive).Count == 32, "Capacity pressure never removes original visible cards or exceeds protocol count");
        Check(CardAppearanceSampler.Retained().Count == 32, "Capacity pressure keeps pending final snapshots for a later frame");
        CardAppearanceSampler.ResetForTest();
        Check(CardAppearanceSampler.Retained().Count == 0, "Teardown drops completed appearance ownership");
        Console.WriteLine($"Burn completion capture: {checks} assertions passed.");
    }
}
