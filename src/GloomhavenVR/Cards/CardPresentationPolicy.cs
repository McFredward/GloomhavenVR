namespace GloomhavenVR.Cards;

/// <summary>Presentation decisions that can be exercised without Unity object lifetimes.</summary>
internal static class CardPresentationPolicy
{
    /// <summary>A cached highlight is only settled while its requested animation is alive.</summary>
    internal static bool HighlightSettled(bool sameState, bool sameRegion, bool sameVisibility,
                                          bool hoverWanted, bool pulseAlive) =>
        sameState && sameRegion && sameVisibility && (!hoverWanted || pulseAlive);

    /// <summary>Ready locks only this actor; undo reopens selection while peers are choosing.</summary>
    internal static bool RestSelectionEditable(bool selectionPhase, bool livingActor,
                                              bool ready, bool resolvingRest) =>
        selectionPhase && livingActor && !ready && !resolvingRest;
}
