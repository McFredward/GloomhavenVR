namespace GloomhavenVR.Cards;

/// <summary>Actual native animation owns the card until it ends. The startup grace applies only
/// when neither animation is running; elapsed wall time must never cut a live native timeline.</summary>
internal static class BurnFlightCompletion
{
    internal static bool MayRelease(bool artworkPlaying, bool lossSequencePlaying,
                                    float elapsed, float startGrace) =>
        !artworkPlaying && !lossSequencePlaying && elapsed >= startGrace;
}
