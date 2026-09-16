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
        CardsHandManager.Instance.Hands.Remove(actor);
        Check(CardAppearanceSampler.Retained().Count == 0, "Missing native hand UI does not paint an unresolved terminal address");
        CardsHandManager.Instance.Hands[actor] = hand;
        Check(CardAppearanceSampler.Retained().Count == 2, "Temporary native hand absence preserves terminal pixels and durable release");
        actor.CharacterClass.HandAbilityCards.Add(actor.CharacterClass.Pool[0]);
        Check(CardAppearanceSampler.Retained().Count == 1, "Recovery retires old burn completion before card reuse");
        actor.CharacterClass.HandAbilityCards.Clear();
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
        for (int a = 4; a <= 6; a++)
        {
            var extra = new CPlayerActor { Id = a }; CardAppearanceProvenance.Actors[a] = extra;
            var extraHand = new CardsHandUI(); CardsHandManager.Instance.Hands[extra] = extraHand;
            for (int i = 0; i < 32; i++)
            {
                var c = new CAbilityCard(); extra.CharacterClass.Pool.Add(c); extra.CharacterClass.LostAbilityCards.Add(c);
                extraHand.Cards.Add(new AbilityCardUI { PlayerActor = extra, AbilityCard = c });
            }
            foreach (var w in extraHand.Cards) CardAppearanceSampler.RetainBurnCompletion(w, w.fullAbilityCard);
        }
        var seen = new HashSet<(int, ushort)>();
        for (int page = 0; page < 5; page++)
        {
            Time.unscaledTime += .5f;
            var current = CardAppearanceSampler.Retained();
            foreach (var entry in current) seen.Add((entry.ActorId, entry.PoolSeat));
            Time.unscaledTime += .01f;
            var unchanged = CardAppearanceSampler.Retained();
            Check(current.Count == 32 && unchanged.Count == 32 && ReferenceEquals(current[0], unchanged[0]),
                "Retained terminal paging stays stable between half-second opportunities");
        }
        Check(seen.Count == 128, "All four complete card populations eventually publish their terminal native frames");
        CardAppearanceSampler.ResetForTest();
        Check(CardAppearanceSampler.Retained().Count == 0, "Teardown drops completed appearance ownership");
        var progressActor=new CPlayerActor {Id=8};
        var progressCard=new CAbilityCard();progressActor.CharacterClass.Pool.Add(progressCard);
        CardAppearanceProvenance.Actors[8]=progressActor;
        var native=new FullAbilityCard {playerActor=progressActor,AbilityCard=progressCard};
        native.cardEffects.Full=native; native.cardEffects.Running=true;
        progressActor.Controlled=false;
        CardAppearanceSampler.ObserveNativeBurnStart(native.cardEffects);
        Check(NetCardFx.Progress.Count==0,"A read-only proxy cannot publish owner burn progress");
        progressActor.Controlled=true;NetAvatarDriver.CanPublishNativePresentation=false;
        CardAppearanceSampler.ObserveNativeBurnStart(native.cardEffects);
        Check(NetCardFx.Progress.Count==0,"Offline native burns retain no network progress state");
        NetAvatarDriver.CanPublishNativePresentation=true;
        CardAppearanceSampler.ObserveNativeBurnStart(native.cardEffects);
        Check(NetCardFx.Progress.Contains((8,0)),"Offscreen native start publishes progress without a VR wrapper or AbilityCardUI parent");
        CardAppearanceSampler.AdvanceProgressForTest();
        Check(NetCardFx.Progress.Contains((8,0)),"Actual running iterator keeps incoming admission blocked");
        native.cardEffects.Running=false;
        CardAppearanceSampler.AdvanceProgressForTest();
        Check(NetCardFx.Progress.Count==0,"Actual native completion clears an unadopted incoming burn without a flight");
        Console.WriteLine($"Burn completion capture: {checks} assertions passed.");
    }
}
