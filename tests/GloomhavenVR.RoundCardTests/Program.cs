using ScenarioRuleLibrary;

namespace ScenarioRuleLibrary
{
    internal sealed class CAbilityCard { }
    internal sealed class CCharacterClass
    {
        public List<CAbilityCard> HandAbilityCards = new(), RoundAbilityCards = new(), ExtraTurnCards = new(),
            DiscardedAbilityCards = new(), LostAbilityCards = new(), PermanentlyLostAbilityCards = new(), ActivatedCards = new();
        public CAbilityCard? Initiative;
    }
    internal sealed class CPlayerActor { public CCharacterClass CharacterClass = new(); }
}
internal sealed class FullAbilityCard { }
internal sealed class AbilityCardUI
{
    public CAbilityCard? AbilityCard;
    public CPlayerActor? PlayerActor;
    public bool IsLongRest;
    public FullAbilityCard fullAbilityCard = new();
}
internal sealed class VRCard { public AbilityCardUI GameCard; public VRCard(AbilityCardUI widget) { GameCard = widget; } }
internal sealed class CardsHandUI { public CPlayerActor? PlayerActor; public bool LongRest, HasLongRested; }
internal static class Board { internal static class CharacterFocus { public static CPlayerActor? TurnActor; } }
internal static class VRLog { public static void Info(string tag, string text) { } }
internal static class CardsGameApi
{
    public static FullAbilityCard? First, Second;
    public static bool IsLongResting(CardsHandUI hand) => hand.LongRest;
    public static bool HasLongRested(CardsHandUI hand) => hand.HasLongRested;
    public static bool IsInRound(CardsHandUI hand, CAbilityCard card) => hand.PlayerActor?.CharacterClass.RoundAbilityCards.Contains(card) == true;
    public static CAbilityCard? InitiativeCard(CardsHandUI hand) => hand.PlayerActor?.CharacterClass.Initiative;
    public static void GetActionCards(out FullAbilityCard? first, out FullAbilityCard? second) { first=First; second=Second; }
}
internal sealed partial class CardsDriver
{
    private bool _loggedLongRestEmptyBoard;
    private readonly List<AbilityCardUI> _widgetBuffer = new();
    private readonly HashSet<AbilityCardUI> _loggedFlightRefusal = new();
    private readonly Dictionary<AbilityCardUI, VRCard> _cards = new();
    private VRCard AdoptedCard(AbilityCardUI widget)
    {
        if (!_cards.TryGetValue(widget, out VRCard? card)) _cards.Add(widget, card = new VRCard(widget));
        return card;
    }
    private void LogStaleStaticPairRefused(AbilityCardUI widget) { }
    public List<VRCard> Collect(CardsHandUI hand, params AbilityCardUI[] widgets)
    {
        _widgetBuffer.Clear(); _widgetBuffer.AddRange(widgets);
        var cards = new List<VRCard>(); CollectRoundCards(hand, cards); return cards;
    }
}
internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string text) { _assertions++; if (!value) throw new Exception(text); }
    private static void Seats(List<VRCard> cards, params AbilityCardUI[] expected)
    {
        Check(cards.Count == expected.Length, "Round dock must exactly match current native card membership");
        for (int i=0; i<expected.Length; i++) Check(ReferenceEquals(cards[i].GameCard, expected[i]), "Round dock order must follow initiative and surviving card");
    }
    public static void Main()
    {
        // Follow the reported two-card turn using the unchanged native static pair.
        {
            var owner = new CPlayerActor(); var cc = owner.CharacterClass;
            var hand = new CardsHandUI { PlayerActor=owner };
            var first = new AbilityCardUI { PlayerActor=owner, AbilityCard=new() };
            var second = new AbilityCardUI { PlayerActor=owner, AbilityCard=new() };
            var driver = new CardsDriver(); Board.CharacterFocus.TurnActor=owner;
            CardsGameApi.First=first.fullAbilityCard; CardsGameApi.Second=second.fullAbilityCard;
            cc.Initiative=first.AbilityCard; cc.RoundAbilityCards.Add(first.AbilityCard); cc.RoundAbilityCards.Add(second.AbilityCard);
            Seats(driver.Collect(hand, second, first), first, second);
            cc.RoundAbilityCards.Remove(first.AbilityCard); cc.LostAbilityCards.Add(first.AbilityCard);
            Seats(driver.Collect(hand, second, first), second);
            cc.LostAbilityCards.Remove(first.AbilityCard); cc.HandAbilityCards.Add(first.AbilityCard);
            Check(!driver.Collect(hand, first, second).Any(c => c.GameCard == first), "Recovered first card must stay in hand while second card resolves");
            cc.RoundAbilityCards.Remove(second.AbilityCard); cc.PermanentlyLostAbilityCards.Add(second.AbilityCard);
            for (int frame=0; frame<20; frame++)
                Check(driver.Collect(hand, first, second).Count == 0, "Completed pair must remain empty after lost recovery and second burn");
            // Focus away/back, widget replacement, and lingering native pair do not revive it.
            Board.CharacterFocus.TurnActor=new(); Seats(driver.Collect(hand, first, second));
            Board.CharacterFocus.TurnActor=owner; Seats(driver.Collect(hand, first, second));
            var replacement = new AbilityCardUI { PlayerActor=owner, AbilityCard=first.AbilityCard, fullAbilityCard=first.fullAbilityCard };
            Seats(driver.Collect(hand, replacement, second));
            // Same model can be played normally again next round; no permanent suppression latch.
            cc.HandAbilityCards.Remove(first.AbilityCard); cc.RoundAbilityCards.Add(first.AbilityCard);
            Seats(driver.Collect(hand, replacement, second), replacement);
            CardsGameApi.First=null; CardsGameApi.Second=null;
            Seats(driver.Collect(hand, replacement, second), replacement);
            Board.CharacterFocus.TurnActor=new();
            Seats(driver.Collect(hand, replacement, second), replacement);
            Board.CharacterFocus.TurnActor=owner;
            CardsGameApi.First=replacement.fullAbilityCard; CardsGameApi.Second=second.fullAbilityCard;
            // Genuine extra-turn cards are moved from their pending list into ExtraTurnCards.
            cc.RoundAbilityCards.Remove(first.AbilityCard); cc.ExtraTurnCards.Add(first.AbilityCard);
            Seats(driver.Collect(hand, replacement, second), replacement);
            Board.CharacterFocus.TurnActor=new(); Seats(driver.Collect(hand, replacement, second));
            Board.CharacterFocus.TurnActor=owner;
            hand.LongRest=true; Seats(driver.Collect(hand, replacement, second)); hand.LongRest=false;
            hand.HasLongRested=true; Seats(driver.Collect(hand, replacement, second)); hand.HasLongRested=false;
            cc.ExtraTurnCards.Remove(first.AbilityCard);
            // Preserve the existing unknown-model supplement while native transfer is unresolved.
            Seats(driver.Collect(hand, replacement, second), replacement);
            foreach (var pile in new[] { cc.HandAbilityCards, cc.DiscardedAbilityCards, cc.LostAbilityCards, cc.PermanentlyLostAbilityCards, cc.ActivatedCards })
            {
                pile.Add(first.AbilityCard); Seats(driver.Collect(hand, replacement, second)); pile.Remove(first.AbilityCard);
            }
            replacement.IsLongRest=true; Seats(driver.Collect(hand, replacement, second)); replacement.IsLongRest=false;
            // Owner membership, not an unrelated currently presented hand, determines departure.
            var foreign = new CPlayerActor(); replacement.PlayerActor=foreign;
            foreign.CharacterClass.HandAbilityCards.Add(first.AbilityCard); Seats(driver.Collect(hand, replacement, second));
            replacement.PlayerActor=null; cc.HandAbilityCards.Add(first.AbilityCard); Seats(driver.Collect(hand, replacement, second));
        }
        Console.WriteLine($"Round-card runtime assertions: {_assertions}");
    }
}
