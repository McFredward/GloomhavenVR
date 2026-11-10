using GloomhavenVR.Cards;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

/// <summary>The exact physical pick seats; no reconstruction from ordinary round
/// cards. Private phases never enter the original-roster provenance lookup.</summary>
internal static class PublicHandPickSampler
{
    private static readonly CardAppearanceState Scratch = new();
    private static PublicHandPickState? _current;
    private static uint _revision;
    private static object? _scenario;

    internal static PublicHandPickState? Sample()
    {
        object? scenario = ScenarioManager.Scenario;
        bool changedScenario = !ReferenceEquals(_scenario, scenario);
        _scenario = scenario;
        int seed = ScenarioManager.CurrentScenarioState?.Seed ?? 0;
        ulong key = PublicHandPickState.ScenarioFingerprint(ScenarioManager.CurrentScenarioState?.ID);
        int actorId = 0, round = 0;
        byte phase = 0, occupied = 0, named = 0, boardSlots = 0;
        PlayTray? tray = PlayTray.Current;
        PublicHandPickState.Seat left = default, right = default;
        if (scenario != null && RevealGate.InScenario && tray != null)
        {
            CPlayerActor? presented = LocalRigSampler.PresentedForCardSeats;
            actorId = presented == null ? 0 : NetFigures.StableActorId(presented);
            boardSlots = actorId == 0 ? (byte)0 : (byte)(tray.OccupiedSlotMask & 3);
            round = ScenarioManager.CurrentScenarioState?.RoundNumber ?? 0;
            int nativePhase = (int)PhaseManager.PhaseType;
            phase = nativePhase > 0 && nativePhase <= byte.MaxValue ? (byte)nativePhase : (byte)0;
            for (int slot = 0; slot < 2; slot++)
            {
                (CardsHandUI? hand, AbilityCardUI? widget) = CardsDriver.PickFieldSeat(slot);
                CPlayerActor? actor = hand?.PlayerActor;
                if ((boardSlots & (1 << slot)) == 0 || widget?.CardType != CardPileType.Hand
                    || hand == null || actor == null || widget?.AbilityCard == null
                    || !LocalRigSampler.OwnedByPresentedCharacter(hand)) continue;
                int id = NetFigures.StableActorId(actor);
                if (id == 0 || actorId != 0 && actorId != id) continue;
                actorId = id; occupied |= (byte)(1 << slot);
                // Pool seat/count is identity. Do not even capture it while the
                // original phase conceals cards, including all short-rest burns.
                if (phase == 0 || RevealGate.IsSecretSelectionPhase
                    || !RevealGate.PeersMayNameOurCard(actor, widget.CardInstanceID)
                    || !RevealGate.PeersSeeOurCardFronts) continue;
                Scratch.SourceActorId = 0; Scratch.PoolSeat = Scratch.PoolCount = 0;
                if (!CardAppearanceProvenance.Capture(Scratch, actor, widget.AbilityCard)) continue;
                var seat = new PublicHandPickState.Seat(Scratch.SourceActorId, Scratch.PoolSeat, Scratch.PoolCount);
                if (!seat.Valid) continue;
                if (slot == 0) left = seat; else right = seat;
                named |= (byte)(1 << slot);
            }
        }
        if (actorId == 0) { round = 0; phase = 0; boardSlots = 0; }
        if (_current == null && occupied == 0 && !changedScenario) return null;
        if (!changedScenario && _current != null && _current.ScenarioSeed == seed && _current.ScenarioKey == key
            && _current.ActorId == actorId && _current.Round == round && _current.Phase == phase
            && _current.Occupied == occupied && _current.Named == named && _current.BoardSlots == boardSlots
            && _current.Left.Same(in left) && _current.Right.Same(in right)) return _current;
        if (++_revision == 0) _revision++;
        _current = new PublicHandPickState(_revision, actorId, round, phase, occupied, named, left, right, seed, key, boardSlots);
        return _current;
    }
}
