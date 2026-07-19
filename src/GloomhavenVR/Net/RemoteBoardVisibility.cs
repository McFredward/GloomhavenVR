namespace GloomhavenVR.Net;

/// <summary>
/// How much of OTHER players' cosmetic VR control boards (and their played round cards) this
/// client renders. Chosen in the in-VR settings panel; a purely LOCAL rendering decision — it
/// never affects game state or what we transmit. The anti-cheat reveal gate (see
/// <see cref="RevealGate"/>) always applies ON TOP of this: even in <see cref="Always"/>, a
/// remote player's round cards show as BACKS until the game's own secret card-selection phase
/// ends (exactly the vanilla client rule).
/// </summary>
internal enum RemoteBoardVisibility
{
    /// <summary>Never render remote players' control boards.</summary>
    Off = 0,

    /// <summary>Render a remote board only once its owner's cards may be shown — i.e. NOT during
    /// the secret <c>SelectAbilityCardsOrLongRest</c> phase. During selection the board is hidden;
    /// after everyone has committed (reveal) it appears with the real cards.</summary>
    ActionPhaseOnly = 1,

    /// <summary>Always render remote boards. During the secret selection phase the board frame is
    /// visible but the round cards are shown as BACKS (anti-cheat); they flip to the real faces at
    /// reveal.</summary>
    Always = 2,
}
