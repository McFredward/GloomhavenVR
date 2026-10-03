namespace GloomhavenVR.Cards;

partial class CardHalfTone
{
    // This fixture source-binds the production material-write routes, not native Unity
    // scene discovery or census reporting. The separate native census fixture owns
    // those lifetimes. Record the dependency call without replacing any burn policy,
    // graphic, material, ownership predicate or normalization under test here.
    internal static FullAbilityCard? CensusObservedFace;
    internal static int CensusObservationCalls;

    internal static void RegisterCensusFace(FullAbilityCard? face)
    {
        CensusObservedFace = face;
        CensusObservationCalls++;
    }
}
