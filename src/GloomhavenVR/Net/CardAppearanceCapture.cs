using System;

namespace GloomhavenVR.Net;

/// <summary>Private reusable sampling state; only immutable captured output is published.</summary>
internal sealed class CardAppearanceCapture
{
    internal readonly CardAppearanceBindings Bindings;
    internal readonly CardAppearanceState Candidate = new();
    private CardAppearanceState? _published;

    internal CardAppearanceCapture(CardEffects effects) => Bindings = new CardAppearanceBindings(effects);

    internal CardAppearanceState Publish()
    {
        if (_published != null && Candidate.ActorId == _published.ActorId
            && Candidate.FaceCode == _published.FaceCode && Candidate.ListCount == _published.ListCount
            && Candidate.SourceActorId == _published.SourceActorId
            && Candidate.PoolSeat == _published.PoolSeat && Candidate.PoolCount == _published.PoolCount
            && (ReferenceEquals(Candidate.Nodes, _published.Nodes) && ReferenceEquals(Candidate.ExtraGroups, _published.ExtraGroups)
                || CardAppearanceState.Same(Candidate, _published))) return _published;
        // Validation allocates its uniqueness set. Previously this happened for every card on
        // every sample, including unchanged output. An identical immutable snapshot is already
        // validated; every changed address, provenance, hierarchy and animated value still is.
        if (!Candidate.Validate()) throw new InvalidOperationException(
            $"Native card appearance has {Bindings.Groups.Count} groups and exceeds the bounded wire domain.");
        return _published = new CardAppearanceState { ActorId = Candidate.ActorId, FaceCode = Candidate.FaceCode,
            ListCount = Candidate.ListCount, SourceActorId = Candidate.SourceActorId,
            PoolSeat = Candidate.PoolSeat, PoolCount = Candidate.PoolCount,
            Nodes = Candidate.Nodes, ExtraGroups = Candidate.ExtraGroups };
    }
}
