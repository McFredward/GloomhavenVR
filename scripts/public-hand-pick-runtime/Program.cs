using System;
using System.Collections.Generic;
using GloomhavenVR.Net;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;

// Declared native boundary: real native selection callbacks/Photon/Unity rendering are not
// executed here. Exact production sampler, provenance, codec, inbox and consumer/flight
// methods are compiled unchanged by the source generator.
namespace ScenarioRuleLibrary
{
    internal sealed class CAbilityCard { internal int CardInstanceID; internal CAbilityCard(int id) => CardInstanceID = id; }
    internal sealed class CCharacterClass
    {
        internal List<CAbilityCard> AbilityCardsPool = new(), SupplyCards = new(), HandAbilityCards = new(),
            RoundAbilityCards = new(), DiscardedAbilityCards = new(), LostAbilityCards = new(), PermanentlyLostAbilityCards = new();
    }
    internal sealed class CPlayerActor { internal int Id; internal bool Ours; internal CCharacterClass CharacterClass = new(); }
    internal sealed class CScenario { internal List<CPlayerActor> AllPlayers = new(); }
    internal sealed class ScenarioState { internal int Seed = 91, RoundNumber = 1; internal string ID = "Scenario_A"; }
    internal static class ScenarioManager { internal static CScenario? Scenario; internal static ScenarioState? CurrentScenarioState; }
    internal static class PhaseManager { internal static CPhase.PhaseType PhaseType; }
}
internal sealed class CardsHandUI { internal CPlayerActor? PlayerActor; }
internal sealed class AbilityCardUI { internal CAbilityCard? AbilityCard; internal CardPileType CardType = CardPileType.Hand; internal int CardInstanceID => AbilityCard?.CardInstanceID ?? 0; }
namespace GloomhavenVR.Cards
{
    internal sealed class PlayTray
    {
        internal static PlayTray? Current = new(); internal static int Override = -1;
        internal int OccupiedSlotMask => Override >= 0 ? Override : (CardsDriver.Seats[0].widget == null ? 0 : 1) | (CardsDriver.Seats[1].widget == null ? 0 : 2);
    }
    internal static class CardsDriver
    {
        internal static readonly (CardsHandUI? hand, AbilityCardUI? widget)[] Seats = new (CardsHandUI?, AbilityCardUI?)[2];
        internal static (CardsHandUI? hand, AbilityCardUI? widget) PickFieldSeat(int seat) => Seats[seat];
    }
}
namespace GloomhavenVR.Net
{
    internal static class LocalRigSampler { internal static CPlayerActor? Presented; internal static CPlayerActor? PresentedForCardSeats => Presented; internal static bool OwnedByPresentedCharacter(CardsHandUI hand) => ReferenceEquals(Presented, hand.PlayerActor) && hand.PlayerActor?.Ours == true; }
    internal static class NetFigures { internal static int StableActorId(CPlayerActor actor) => actor.Id; }
    internal static class RemoteBoardFocus
    {
        internal static CPlayerActor? ActorById(int id) => ScenarioManager.Scenario?.AllPlayers.Find(a => a.Id == id);
        internal static CPlayerActor? DisplayedActor(Owner owner, out bool unused) { unused = false; return owner.Actor; }
    }
    internal sealed class CardAppearanceState { internal int SourceActorId; internal ushort PoolSeat, PoolCount; }
    internal static partial class RevealGate
    {
        internal static bool InScenario = true;
        internal static bool ForcedNamingException;
        private static bool LocallyControls(CPlayerActor? actor) => actor?.Ours == true;
        private static bool IsPubliclyRevealedCard(CPlayerActor? actor, int card) => ForcedNamingException;
    }
    internal static class FFSNetwork { internal static bool IsOnline = true; }
    internal static class Time { internal static float unscaledTime = 10; }
    internal enum CardFxAnchor { Board, Slot0, Slot1, Discard, Burnt }
    internal sealed class Owner { internal PublicHandPickState? PublicHandPick; internal CPlayerActor? Actor; }
    internal sealed partial class RemoteBoard
    {
        private const int SlotCount = 2;
        private const float DepartedFaceSeconds = 3f;
        internal readonly Owner _owner = new();
        private readonly bool[] _departedPublicPick = new bool[2];
        private PublicHandPickState? _publicPickContext;
        private object? _publicPickScenario;
        private readonly CAbilityCard?[] _publicPickFaces = new CAbilityCard?[2], _latchedFaces = new CAbilityCard?[2], _departedFace = new CAbilityCard?[2];
        private readonly float[] _departedAt = new float[2], _claimedAt = new float[2];
        private readonly int[] _claimedFaceId = { int.MinValue, int.MinValue };
        internal bool Resolve(int slot, CPlayerActor? actor, out CAbilityCard? card) => TryResolvePublicHandPick(slot, actor, out card);
        // The physical renderer branch is exercised separately in Unity. This adapter supplies
        // only its approved-face and empty-edge inputs to the actual flight-cache methods.
        internal int Occupancy(CPlayerActor actor, int old) => PublicPickOccupancy(actor, old);
        internal void Draw(int slot, CAbilityCard? card, bool ordinary = false) { if (ordinary) _latchedFaces[slot] = card; else { _publicPickFaces[slot] = card; _publicPickContext = _owner.PublicHandPick; _publicPickScenario = ScenarioManager.Scenario; } }
        internal void Depart(int slot) => PreservePublicPickDeparture(slot);
        internal void Empty(int slot) { NoteRecessDeparture(slot, _latchedFaces[slot] ?? _publicPickFaces[slot]); _latchedFaces[slot] = _publicPickFaces[slot] = null; }
    }
}
internal static class Program
{
    private static int _checks;
    private static void Check(bool yes, string what) { if (!yes) throw new Exception(what); _checks++; }
    private static (CardsHandUI hand, AbilityCardUI widget) Seat(CPlayerActor a, CAbilityCard card) => (new CardsHandUI { PlayerActor = a }, new AbilityCardUI { AbilityCard = card });
    private static PublicHandPickState Sample() => PublicHandPickSampler.Sample() ?? throw new Exception("expected sample");
    private static PublicHandPickState Roundtrip(PublicHandPickState picture) { var b = new byte[100]; int count = picture.Write(b, 0); Check(PublicHandPickState.TryRead(b, 0, count, out var p) && p != null, "actual codec roundtrip"); return p!; }
    public static int Main()
    {
        try
        {
            var a = new CPlayerActor { Id = 17, Ours = true }; var donor = new CPlayerActor { Id = 23 };
            var left = new CAbilityCard(101); var right = new CAbilityCard(102); var supplied = new CAbilityCard(103); var unrelated = new CAbilityCard(999);
            a.CharacterClass.AbilityCardsPool.AddRange(new[] { left, right, unrelated }); a.CharacterClass.HandAbilityCards.AddRange(new[] { left, right });
            a.CharacterClass.RoundAbilityCards.Add(unrelated); donor.CharacterClass.SupplyCards.Add(supplied);
            ScenarioManager.Scenario = new CScenario(); ScenarioManager.Scenario.AllPlayers.AddRange(new[] { a, donor }); ScenarioManager.CurrentScenarioState = new ScenarioState();
            PhaseManager.PhaseType = CPhase.PhaseType.Action; LocalRigSampler.Presented = a;
            var firstEmpty = Sample(); Check(firstEmpty.Occupied == 0, "new native lifetime publishes initial ordered tombstone");
            CardsDriver.Seats[0] = Seat(a, left); CardsDriver.Seats[1] = Seat(a, right);
            var publicPair = Sample(); Check(publicPair.Named == 3 && publicPair.Occupied == 3, "forced native action discard publishes BOTH exact hand originals");
            Check(ReferenceEquals(Sample(), publicPair), "unchanged picture reuses receipt and revision");
            var inbox = new PublicHandPickInbox(); int seed = 91; ulong key = PublicHandPickState.ScenarioFingerprint("Scenario_A");
            inbox.Observe(true, Roundtrip(publicPair), ScenarioManager.Scenario, seed, key);
            var board = new RemoteBoard(); board._owner.Actor = a; board._owner.PublicHandPick = inbox.Current(ScenarioManager.Scenario);
            a.Ours = false;
            Check(board.Resolve(0, a, out var l) && ReferenceEquals(l, left), "remote forced left uses actual original rather than unrelated round card");
            Check(board.Resolve(1, a, out var r) && ReferenceEquals(r, right), "remote forced right uses actual original rather than unrelated round card");
            Check(board.Occupancy(a, 0) == 3, "ordered hand fill overrides an older empty legacy occupancy");
            board.Draw(0, l); board.Draw(1, r);
            Check(board.TryTakeDepartedFace(0, CardFxAnchor.Discard, out var fly0, out var v0) && ReferenceEquals(fly0, left) && v0 == RemoteBoard.DepartedFaceVerdict.LiveLatch, "flight before empty edge takes approved public LEFT");
            Check(board.TryTakeDepartedFace(1, CardFxAnchor.Discard, out var fly1, out _) && ReferenceEquals(fly1, right), "flight before empty edge takes approved public RIGHT");
            board.Empty(0); board.Empty(1);
            Check(!board.TryTakeDepartedFace(0, CardFxAnchor.Discard, out _, out _), "empty edge cannot restamp already claimed public left");
            Check(!board.TryTakeDepartedFace(1, CardFxAnchor.Discard, out _, out _), "empty edge cannot restamp already claimed public right");
            Time.unscaledTime += 4; board.Draw(0, left); board.Draw(1, right); board.Empty(0); board.Empty(1);
            Check(board.TryTakeDepartedFace(0, CardFxAnchor.Discard, out fly0, out v0) && ReferenceEquals(fly0, left) && v0 == RemoteBoard.DepartedFaceVerdict.DepartedMemory, "flight after empty edge takes approved public left");
            Check(board.TryTakeDepartedFace(1, CardFxAnchor.Discard, out fly1, out _) && ReferenceEquals(fly1, right), "flight after empty edge takes approved public right");
            Check(!board.TryTakeDepartedFace(1, CardFxAnchor.Discard, out _, out _), "consumed departed identity is not duplicated");
            board.Draw(0, unrelated, ordinary: true); Check(board.TryTakeDepartedFace(0, CardFxAnchor.Discard, out var ordinary, out _) && ReferenceEquals(ordinary, unrelated), "ordinary round-card flight uses unchanged existing latch"); board.Empty(0);
            Check(!board.Resolve(0, donor, out _), "foreign displayed actor cannot reuse owner seats");
            ScenarioManager.CurrentScenarioState.RoundNumber++; Check(!board.Resolve(0, a, out _), "changed round cannot reuse public seats"); ScenarioManager.CurrentScenarioState.RoundNumber--;
            PhaseManager.PhaseType = CPhase.PhaseType.SelectAbilityCardsOrLongRest; Check(!board.Resolve(0, a, out _), "phase transition invalidates old public seats");
            a.Ours = true; RevealGate.ForcedNamingException = true; var privatePair = Sample();
            Check(privatePair.Named == 0 && privatePair.Left.Empty && privatePair.Right.Empty && privatePair.Occupied == 3, "private selection never emits source provenance even with independent naming exception");
            Check(ReferenceEquals(CardsDriver.Seats[0].widget!.AbilityCard, left), "remote concealment never changes owner's original front/model");
            board._owner.PublicHandPick = Roundtrip(privatePair); a.Ours = false;
            Check(board.Resolve(0, a, out var hidden) && hidden == null, "anonymous public-pick claim blocks unrelated ordinary round front");
            a.Ours = true; PhaseManager.PhaseType = CPhase.PhaseType.Action;
            CardsDriver.Seats[1] = default; var cancel = Sample(); Check(cancel.Occupied == 1 && cancel.Named == 1 && cancel.Right.Empty, "cancel/deselect clears exact right original immediately");
            CardsDriver.Seats[0] = default; var empty = Sample(); Check(empty.Occupied == 0 && empty.Named == 0 && PublicHandPickState.Newer(empty.Revision, cancel.Revision), "cancel/flow teardown emits ordered empty tombstone");
            inbox.Observe(true, empty, ScenarioManager.Scenario, seed, key); inbox.Observe(true, publicPair, ScenarioManager.Scenario, seed, key);
            Check(inbox.Current(ScenarioManager.Scenario)?.Occupied == 0, "late public packet cannot resurrect canceled choice");
            CardsDriver.Seats[0] = Seat(a, supplied); a.CharacterClass.HandAbilityCards.Add(supplied); var suppliedPicture = Sample();
            Check(suppliedPicture.Left.SourceActorId == donor.Id && suppliedPicture.Left.PoolSeat == 0x8000, "given card preserves donor immutable supply-pool provenance");
            board._owner.PublicHandPick = suppliedPicture; a.Ours = false;
            Check(board.Resolve(0, a, out var given) && ReferenceEquals(given, supplied), "receiver resolves given card from exact donor into recipient hand");
            a.CharacterClass.HandAbilityCards.Remove(supplied); Check(board.Resolve(0, a, out given) && given == null, "original unrelated to current owner inventory cannot be drawn"); a.CharacterClass.HandAbilityCards.Add(supplied);
            donor.CharacterClass.SupplyCards.Add(new CAbilityCard(104)); Check(board.Resolve(0, a, out given) && given == null, "changed original roster count refuses wrong pool binding"); donor.CharacterClass.SupplyCards.RemoveAt(1);
            ScenarioManager.CurrentScenarioState.Seed++; Check(!board.Resolve(0, a, out _), "different native seed invalidates consumer before next presence receipt");
            var previousScenario = ScenarioManager.Scenario; ScenarioManager.Scenario = new CScenario(); ScenarioManager.Scenario.AllPlayers.AddRange(new[] { a, donor });
            inbox.Observe(true, suppliedPicture, ScenarioManager.Scenario, seed + 1, key); Check(inbox.Current(ScenarioManager.Scenario) == null, "unseen old namespace revision cannot establish new scenario picture");
            a.Ours = true; CardsDriver.Seats[0] = default; var nextEmpty = Sample(); Check(nextEmpty.Occupied == 0 && PublicHandPickState.Newer(nextEmpty.Revision, suppliedPicture.Revision), "empty new scenario still publishes new revision tombstone");
            var rejoin = new PublicHandPickInbox(); Check(rejoin.Current(ScenarioManager.Scenario) == null, "rejoined observer starts with no cached source");
            CardsDriver.Seats[0] = Seat(a, left); var joinedPicture = Sample(); rejoin.Observe(true, joinedPicture, ScenarioManager.Scenario, seed + 1, key);
            Check(ReferenceEquals(rejoin.Current(ScenarioManager.Scenario), joinedPicture), "rejoined observer immediately admits current complete public picture");
            Time.unscaledTime += 4;
            board._owner.PublicHandPick = joinedPicture; board._owner.Actor = a; a.Ours = false;
            Check(board.Resolve(0, a, out var joinedFace) && ReferenceEquals(joinedFace, left), "new observer resolves exact current hand original");
            board.Draw(0, joinedFace);
            CardsDriver.Seats[0].widget!.CardType = CardPileType.Discarded;
            a.Ours = true; var handToDiscard = Sample(); a.Ours = false; board._owner.PublicHandPick = handToDiscard;
            Check(handToDiscard.Occupied == 0 && handToDiscard.Named == 0 && handToDiscard.BoardSlots == 1,
                "native hand-to-discard commit releases hand identity but keeps exact physical occupancy");
            Check(!board.Resolve(0, a, out _), "legacy Discard/Burn identity remains on record39");
            board.Depart(0);
            Check(board.TryTakeDepartedFace(0, CardFxAnchor.Discard, out var committedFace, out _)
                && ReferenceEquals(committedFace, left), "known public hand departure remains flight-eligible before native discard model catches up");
            CardsDriver.Seats[0] = default; var finalEmpty = Sample(); board._owner.PublicHandPick = finalEmpty;
            Check(board.Occupancy(a, 3) == 0, "ordered empty handover overrides older occupied legacy snapshot");
            Time.unscaledTime += 4;
            CardsDriver.Seats[0] = Seat(a, left); a.Ours = true; var laterHand = Sample(); a.Ours = false; board._owner.PublicHandPick = laterHand;
            board.Draw(0, left); board.Empty(0); board.ClearPublicHandPickFaces();
            Check(!board.TryTakeDepartedFace(0, CardFxAnchor.Discard, out _, out _), "unusable record immediately revokes only public live/departed faces");
            Time.unscaledTime += 4; board.Draw(0, left); board.Empty(0); PhaseManager.PhaseType = CPhase.PhaseType.SelectAbilityCardsOrLongRest;
            Check(!board.TryTakeDepartedFace(0, CardFxAnchor.Discard, out _, out _), "private phase clears public departure before next board pass");
            PhaseManager.PhaseType = CPhase.PhaseType.Action; Time.unscaledTime += 4; board.Draw(0, left); board.Empty(0);
            ScenarioManager.Scenario = new CScenario(); ScenarioManager.Scenario.AllPlayers.AddRange(new[] { a, donor });
            Check(!board.TryTakeDepartedFace(0, CardFxAnchor.Discard, out _, out _), "native scenario replacement clears public departure before next board pass");
            Time.unscaledTime += 4; board._owner.Actor = a; board.Draw(0, left); board.Empty(0); board._owner.Actor = donor;
            Check(!board.TryTakeDepartedFace(0, CardFxAnchor.Discard, out _, out _), "displayed actor switch clears public departure before next board pass");
            board._owner.Actor = a;
            CardsDriver.Seats[0] = default; Sample(); PlayTray.Override = 2;
            var ordinaryAfterEmpty = Sample(); board._owner.PublicHandPick = ordinaryAfterEmpty;
            Check(ordinaryAfterEmpty.Occupied == 0 && board.Occupancy(a, 0) == 2,
                "later ordinary round placement advances atomic occupancy after hand tombstone");
            PlayTray.Override = -1;
            LocalRigSampler.Presented = donor; var foreign = Sample(); Check(foreign.Occupied == 0 && foreign.Named == 0, "presented character ownership mismatch cannot publish another hand");
            LocalRigSampler.Presented = a; a.Ours = true;
            var copiedSupply = new CAbilityCard(10103); a.CharacterClass.HandAbilityCards.Add(copiedSupply);
            CardsDriver.Seats[0] = Seat(a, copiedSupply); var copyState = Sample();
            Check(copyState.Occupied == 1 && copyState.Named == 0,
                "copied supply instance absent from immutable original pool remains anonymous without prototype guessing");
            board._owner.Actor = a; board._owner.PublicHandPick = copyState; a.Ours = false;
            Check(board.Resolve(0, a, out var copiedArt) && copiedArt == null,
                "unresolved copied supply hand still claims its real seat without unrelated round fallback");
            RevealGate.InScenario = false; CardsDriver.Seats[0] = Seat(a, left); Check(Sample().Occupied == 0, "map has no scenario public hand pick");
            Console.WriteLine("PASS " + _checks + " source-bound producer/consumer/flight assertions; native callback/Photon/rendering boundary explicit"); return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL " + ex.Message); return 1; }
    }
}
