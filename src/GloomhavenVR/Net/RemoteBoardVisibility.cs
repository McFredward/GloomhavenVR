using GloomhavenVR.Core;
using ScenarioRuleLibrary;

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

/// <summary>
/// THE single decision point for <see cref="NetModule.RemoteBoards"/> ("Mitspieler-Boards" in the
/// VR settings panel).
///
/// WHY THIS EXISTS (audit 2026-07): the setting was originally written when a peer's board was just
/// a frame with two round cards, and <see cref="RemoteControlBoard"/> evaluated the mode inline.
/// Several rounds later that board grew a whole parity layer (objectives, elements, readouts, active
/// cards, initiative track, pile stacks, inert furniture) plus THREE transient reading fans that are
/// rendered by their OWN classes and are NOT children of the board root —
/// <see cref="RemoteItemFan"/>, <see cref="RemoteBrowserFan"/> and <see cref="RemoteCardFx"/>. Those
/// classes never learned about the mode (the FX class only ever checked <see cref="Off"/>), so with
/// "Aus" or "Aktionsphase" a peer's item fan / discard-browse fan still bloomed in mid-air exactly
/// where their hidden board would have been, and cards still flew to invisible pile stacks. Routing
/// every one of them through this one predicate is what makes the panel control mean what it says.
///
/// THE RULE, and the reason for the split:
///   • BOARD-ANCHORED surfaces — the board frame and every widget docked to it, PLUS a transient fan
///     that the sender parked ABOVE THEIR BOARD, plus any card flight that starts or ends on board
///     furniture — obey <see cref="ShowBoardSurface"/>.
///   • HAND-HELD surfaces — the peer's hand card fan, and an item/browse fan they are physically
///     holding — are AVATAR content, not board content, and are deliberately NOT gated. Hiding a
///     peer's hands would be a different feature; the setting is named "Mitspieler-Boards" and its
///     own documentation says "never render remote players' control BOARDS". This is the same line
///     <see cref="RemoteCardFx"/> already drew in prose ("every anchor except the hand fan is board
///     furniture") — it is now drawn in code, once, here.
/// </summary>
internal static class RemoteBoardGate
{
    /// <summary>The live mode. Degrades to <see cref="RemoteBoardVisibility.Off"/> while the [Net]
    /// config is unbound (networking module never inited) — render nothing rather than guess.</summary>
    internal static RemoteBoardVisibility Mode =>
        NetModule.RemoteBoards != null ? NetModule.RemoteBoards.Value : RemoteBoardVisibility.Off;

    /// <summary>Change-gated diagnostic state: the last mode we logged as APPLIED.</summary>
    private static RemoteBoardVisibility _loggedMode = (RemoteBoardVisibility)(-1);

    /// <summary>
    /// The mode → "may the board surface be drawn" decision, given the already-computed reveal
    /// answer for the owning actor. Kept as its own overload so <see cref="RemoteControlBoard"/> —
    /// which needs the actor and the reveal answer anyway — shares the EXACT expression the fans
    /// use, instead of a second copy that can drift.
    /// </summary>
    internal static bool SurfaceVisible(RemoteBoardVisibility mode, bool showFronts) => mode switch
    {
        RemoteBoardVisibility.Always => true,
        // ActionPhaseOnly: only once the owner's cards may be shown — i.e. the whole board stays
        // hidden through the game's secret SelectAbilityCardsOrLongRest phase.
        RemoteBoardVisibility.ActionPhaseOnly => showFronts,
        _ => false,
    };

    /// <summary>
    /// May ANYTHING anchored to <paramref name="owner"/>'s control board be drawn this frame?
    /// Resolves the owner's actor itself, so callers that do not otherwise need the game model
    /// (the fans, the card FX) stay free of it. A peer with no synced board pose or the mode set
    /// to Off always answers false. A peer WITHOUT an actor (join-time, before the host assigns
    /// characters) answers like a peer outside the secret phase: they have no cards, so there is
    /// nothing to hide, and the board must be visible from their first packets — the same rule
    /// <see cref="RemoteControlBoard.Tick"/> applies to the board surface itself.
    /// </summary>
    internal static bool ShowBoardSurface(RemoteAvatar? owner)
    {
        RemoteBoardVisibility mode = Mode;
        if (owner == null || !owner.HasBoard || mode == RemoteBoardVisibility.Off)
            return false;
        CPlayerActor? actor = NetPlayerActors.ActorFor(owner.PlayerId);
        return SurfaceVisible(mode, actor == null || RevealGate.ShowRoundCardFronts(actor));
    }

    /// <summary>
    /// One Info line per ACTUAL mode change, stating the value that was read off the config on the
    /// live rendering path and what it does. This is the evidence the settings-panel cycle button
    /// really lands somewhere (grep: "Remote board visibility"). Called from the board tick, which
    /// runs for every peer every frame — the change gate keeps it to one line per flip.
    /// </summary>
    internal static void LogModeIfChanged(RemoteBoardVisibility mode)
    {
        if (mode == _loggedMode)
            return;
        _loggedMode = mode;
        VRLog.Info("Net", $"Remote board visibility APPLIED: [Net] RemoteBoards = {mode} — " + mode switch
        {
            RemoteBoardVisibility.Off =>
                "nothing of any peer's control board is drawn: no frame, no parity widgets, no inert " +
                "furniture, no board-anchored item/pile-browse fan and no card flights to board furniture. " +
                "Their hands, head and hand-held fans are avatar content and stay visible.",
            RemoteBoardVisibility.ActionPhaseOnly =>
                "a peer's WHOLE board (frame + every widget + any board-anchored fan + board card flights) " +
                "is hidden during the game's secret SelectAbilityCardsOrLongRest phase and appears at reveal.",
            _ =>
                "a peer's board is always drawn; during the secret selection phase its round cards show " +
                "BACKS (RevealGate) and flip to the real faces at reveal.",
        });
    }
}
