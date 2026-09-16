namespace UnityEngine { internal sealed class GameObject { internal bool activeInHierarchy=true; } internal static class Time { internal static float unscaledTime; } }
namespace ScenarioRuleLibrary {
    internal class CAbilityCard { }
    internal class CPlayerActor { internal CCharacterClass CharacterClass = new(); }
    internal class CCharacterClass {
        internal List<CAbilityCard> RoundAbilityCards = new(), ExtraTurnCards = new(), DiscardedAbilityCards = new(), LostAbilityCards = new(), PermanentlyLostAbilityCards = new(), ActivatedCards = new(), HandAbilityCards = new();
    }
}
namespace GloomhavenVR.Cards {
    using ScenarioRuleLibrary;
    internal sealed class AbilityCardUI { internal CPlayerActor? PlayerActor; internal CAbilityCard? AbilityCard; internal bool Playing; }
    internal sealed class FullAbilityCard { internal bool Playing; }
    internal sealed class CardsHandUI { internal CPlayerActor? PlayerActor; internal UnityEngine.GameObject gameObject=new(); internal bool AnimatingLostCards,animatedLosingCard; internal List<AbilityCardUI> cardsUI=new(); }
    internal sealed class VRCard { internal AbilityCardUI? GameCard; internal FullAbilityCard FullCard = new(); internal bool IsFlying, IsVanishing, IsHeld, Parked; internal int Seat; }
    internal static class BurnArtwork {
        internal static object? EffectsOf(object? value) => value;
        internal static bool Playing(object? value) => value is FullAbilityCard full ? full.Playing : value is AbilityCardUI widget && widget.Playing;
    }
    internal enum PileKind { Discard, Burnt }
    internal enum RoundCardExit { NoModel, StillRound, Discarded, Lost, PermanentlyLost, Activated, Hand, OffModel }
    internal sealed class Factory { internal List<VRCard> All = new(); }
    internal sealed class Active { internal List<VRCard> Cards = new(); internal bool Contains(VRCard card) => Cards.Contains(card); }
    internal sealed class Half { internal bool ReadOnly; internal void SetReadOnly(bool value) => ReadOnly = value; }
    internal sealed partial class CardsDriver {
        private static CardsDriver? Instance;
        private readonly Factory _factory = new();
        private readonly Active _active = new();
        private readonly Half _half = new();
        private bool _fakeActive, _dirty;
        private CardsHandUI? _boundHand, _burnWatchHand;
        private readonly HashSet<AbilityCardUI> _knownBurntWidgets = new();
        private readonly Dictionary<AbilityCardUI, BurnHold> _burnHoldSince = new();
        private readonly Dictionary<AbilityCardUI, int> _activeExitOrigins = new();
        private readonly struct BurnHold { internal BurnHold(float since, bool artworkSeen) { Since=since; } internal readonly float Since; }
        private bool IsParked(VRCard card) => card.Parked;
        private int CapturedActiveSource(AbilityCardUI widget, CPlayerActor? owner) => 17;
        internal bool NativeLossActive;
        internal CardsHandUI? Incoming;
        private CardsHandUI? CurrentHand() => Incoming ?? _boundHand;
        internal int Renders, Flights, GrabBlocks;
        internal List<VRCard> Requested = new();
        internal List<VRCard> Drawn = new();
        internal CardsDriver() { Instance=this; _fakeActive=false; }
        internal void Bind(CPlayerActor actor) { _boundHand=new CardsHandUI{PlayerActor=actor}; _burnWatchHand=_boundHand; }
        internal void Add(VRCard card) { _factory.All.Add(card); Drawn.Add(card); }
        internal void Known(AbilityCardUI card) => _knownBurntWidgets.Add(card);
        internal void ActiveCard(VRCard card) => _active.Cards.Add(card);
        internal bool HasSource(AbilityCardUI card) => _activeExitOrigins.ContainsKey(card);
        internal bool Pending => _burnLayoutPending;
        internal CPlayerActor? LayoutActor => PresentedHandForCardLayout(CurrentHand())?.PlayerActor;
        internal bool Dirty => _dirty;
        internal bool HalfBlocked => _half.ReadOnly;
        internal void Tick() { _dirty=false; RefreshBurnLayoutBarrier(); RebuildProbe(); }
        private void BlockCardInteractions() => GrabBlocks++;
        private void RenderRequestedLayout() { Renders++; Drawn = new(Requested); for(int i=0;i<Drawn.Count;i++) Drawn[i].Seat=i; }
        // The tested barrier calls its existing flight service. Model this boundary explicitly:
        // native completion is externally driven; no test advances game state on its behalf.
        private void FlushBurnHolds(string reason) {
            if (_burnLayoutNativeActive || NativeLossActive) return;
            foreach (var pair in _burnHoldSince.ToArray()) {
                if (UnityEngine.Time.unscaledTime-pair.Value.Since < .5f) continue;
                _burnHoldSince.Remove(pair.Key);_knownBurntWidgets.Add(pair.Key); Flights++;
                var card=_factory.All.Find(x=>ReferenceEquals(x.GameCard,pair.Key));if(card!=null) card.IsFlying=true;
            }
        }
    }
}

namespace GloomhavenVR.Board { internal static class CharacterFocus { internal static Cards.CardsHandUI? PresentedHand(Cards.CardsHandUI? hand)=>hand; } }
