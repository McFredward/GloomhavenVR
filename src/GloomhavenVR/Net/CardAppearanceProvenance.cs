using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

/// <summary>Bind mutable visual seats to the original scenario class roster, without card IDs/names.</summary>
internal static class CardAppearanceProvenance
{
    internal static bool Capture(CardAppearanceState state, CPlayerActor displayedActor, CAbilityCard card)
    {
        if (CaptureFrom(state, displayedActor, card)) return true;
        // Supplied/given cards remain in the donor's immutable class pool after moving to another
        // actor's hand. The dynamic visual address continues to name the recipient's surface.
        var scenario = ScenarioManager.Scenario;
        if (scenario == null) return false;
        foreach (CPlayerActor actor in scenario.AllPlayers)
            if (!ReferenceEquals(actor, displayedActor) && CaptureFrom(state, actor, card)) return true;
        return false;
    }
    private static bool CaptureFrom(CardAppearanceState state, CPlayerActor actor, CAbilityCard card)
    {
        CCharacterClass? character = actor.CharacterClass;
        if (character == null || !CardAppearancePool.TryLocate(character.AbilityCardsPool, character.SupplyCards,
            card, out ushort seat, out ushort count)) return false;
        state.SourceActorId = NetFigures.StableActorId(actor);
        state.PoolSeat = seat; state.PoolCount = count;
        return state.SourceActorId != 0;
    }
    internal static CAbilityCard? Resolve(CardAppearanceState state)
    {
        CCharacterClass? character = RemoteBoardFocus.ActorById(state.SourceActorId)?.CharacterClass;
        return character != null ? CardAppearancePool.Resolve(character.AbilityCardsPool, character.SupplyCards,
            state.PoolSeat, state.PoolCount) : null;
    }
}
