using GloomhavenVR.Core;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

/// <summary>
/// How much of OTHER players' cosmetic VR control boards (and their played round cards) this
/// client renders. Chosen in the in-VR settings panel; a purely LOCAL rendering decision — it
/// never affects game state or what we transmit. The anti-cheat reveal gate (see
/// <see cref="RevealGate"/>) always applies ON TOP of this: even in <see cref="Always"/>, a
/// remote player's round cards show as BACKS during the game's own secret card-selection phase.
///
/// <para>"UNTIL THE PHASE ENDS" IS WHAT THIS SAID AND IT IS NOT THE RULE (corrected 2026-09-07).
/// The phase is the POPULATION's term and it is no longer the only term: a card that is already
/// public — in its owner's <c>ActivatedCards</c>, <c>LostAbilityCards</c> or
/// <c>PermanentlyLostAbilityCards</c> — draws its FRONT inside the selection window too, on every
/// surface, by the user's ruling ("Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit
/// der Vorderseite sichtbar sein"). <see cref="RevealGate.CardFaces"/> owns both terms; this dial
/// owns neither and never did. Three sentences in this file described the phase as the whole gate,
/// which is how a future reader deletes the exception as redundant.</para>
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
    /// visible but the round cards are shown as BACKS (anti-cheat) unless they are ALREADY PUBLIC
    /// (<see cref="RevealGate.IsPubliclyRevealedCard"/> — a burning or active card, which shows its
    /// front in every phase); the rest flip to the real faces at reveal.</summary>
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
/// classes never learned about the mode (the FX class only ever checked
/// <see cref="PeerBoardFadeMode.Off"/>, the see-through mode, not this one), so with
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
///
/// THAT LINE IS UNCHANGED BY USER ITEM 11a (2026-09), AND THE REASON IS WORTH WRITING DOWN, because
/// the peer-board SEE-THROUGH now fades a peer's hand fan and a reader will otherwise conclude the
/// classification moved. It did not. This gate answers "may this surface be DRAWN at all", and its
/// answer for hand-held content is still an unconditional yes — hiding a peer's hands remains a
/// different feature. <c>PeerBoardFade</c> answers a different question entirely: "is this surface,
/// right now, one of the things standing between ME and the play field that the board it is in
/// front of is already yielding for". For board furniture that is a permanent property; for a
/// hand-anchored root it is a question about where the hand currently IS, and
/// <c>PeerBoardFade.Follow</c> registers those roots under a rule that admits them only while their
/// owner parks them over the board (frozen for the duration of a fade so membership cannot strobe).
/// A surface can therefore be avatar content for this gate and a board follower for that ramp at
/// the same moment without either statement being weakened.
///
/// THE OUTER GATE (2026-08-22, user item 1 — a peer's board hovering over the 3D campaign map).
/// Everything above is the INSIDE-A-SCENARIO question. Whether the question may be asked at all is
/// <see cref="RemoteBoardScenarioGate"/>, and it is folded into <see cref="SurfaceVisible"/> so
/// there is exactly one expression to satisfy. Outside a scenario NO peer's board is built and none
/// is drawn, for anybody, at any dial setting; inside one the dial means precisely what it says
/// above, unchanged. Read that class for the authority it picked and why.
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
    ///
    /// <para>THE OUTER TERM COMES FIRST and is not part of the mode switch:
    /// <see cref="RemoteBoardScenarioGate.Open"/> asks whether this client is inside a scenario at
    /// all. Outside one — the 3D map room, the vanilla 2D campaign map, the loadout screen, the main
    /// menu — the answer is false for every peer, every dial setting and every seat at the table
    /// (user ruling, 2026-08-22: "In der 3D-Map-Umgebung ist kein board sichtbar von keinem
    /// Mitspieler und darf für niemanden sichtbar sein"). It is FIRST because
    /// <c>RemoteControlBoard.Tick</c> early-returns on a false answer BEFORE <c>EnsureBuilt</c>, so
    /// this one term is also what refuses the prefab instantiation rather than hiding it after the
    /// fact. Inside a scenario it folds out and the switch below is the whole rule, exactly as
    /// before.</para>
    /// </summary>
    internal static bool SurfaceVisible(RemoteBoardVisibility mode, bool showFronts) =>
        RemoteBoardScenarioGate.Open && mode switch
        {
            RemoteBoardVisibility.Always => true,
            // ActionPhaseOnly: only once the owner's cards may be shown — i.e. the whole board stays
            // hidden through the game's secret SelectAbilityCardsOrLongRest phase.
            RemoteBoardVisibility.ActionPhaseOnly => showFronts,
            _ => false,
        };

    /// <summary>
    /// May ANYTHING anchored to <paramref name="owner"/>'s control board be drawn this frame?
    /// Resolves the owner's DISPLAYED character itself (<see cref="RemoteBoardFocus"/> — the same
    /// resolution <see cref="RemoteControlBoard.Tick"/> makes, so a fan and the board it hangs off
    /// can never arbitrate the gate against two different characters), so callers that do not
    /// otherwise need the game model (the fans, the card FX) stay free of it. A peer with no synced
    /// board pose or the mode set to Off always answers false. A peer WITHOUT an actor (join-time,
    /// before the host assigns characters) answers like a peer outside the secret phase: they have
    /// no cards, so there is nothing to hide, and the board must be visible from their first
    /// packets — the same rule <see cref="RemoteControlBoard.Tick"/> applies to the board surface
    /// itself.
    /// </summary>
    internal static bool ShowBoardSurface(RemoteAvatar? owner)
    {
        RemoteBoardVisibility mode = Mode;
        // The outer scenario gate is tested up here as well as inside SurfaceVisible, purely so a
        // shut gate costs nothing: DisplayedActor below walks the focus record and the game model
        // for an answer that cannot matter. The verdict is identical either way — SurfaceVisible
        // owns the rule, this is a short circuit in front of it.
        if (owner == null || !owner.HasBoard || mode == RemoteBoardVisibility.Off
            || !RemoteBoardScenarioGate.Open)
            return false;
        CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(owner, out _);
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
        // THE ONE SEAM RemoteControlBoard.Tick REACHES UNCONDITIONALLY — before it can early-return
        // on HasBoard or on an "Aus" dial. The outer scenario gate borrows it to count the peer
        // boards it is answering for (the number its edge line reports) and to make sure the
        // predicate is evaluated, and therefore its edge stated, in every frame a board ticks. This
        // does NOT change the dial line below: that one is still one line per real dial flip, and
        // the scenario gate has an entirely separate line of its own.
        RemoteBoardScenarioGate.NoteBoardTick();

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
                "BACKS (RevealGate) and flip to the real faces at reveal — EXCEPT a card that is " +
                "already public (burning, or active), which shows its front in every phase by the " +
                "user's own ruling; read the per-recess 'CARD FACE RULE' line for which rule chose " +
                "a given face.",
        }
        // Every sentence above describes the dial INSIDE a scenario, which is the only place it
        // decides anything. Saying so here is not decoration: this line is change-gated on the dial,
        // so it survives verbatim into the 3D map room, where an unqualified "always drawn" would be
        // an instrument asserting something it cannot see. The gate states its own transitions.
        + " ALL OF THAT APPLIES ONLY INSIDE A SCENARIO: outside one (3D map room, vanilla 2D map, "
        + "loadout, main menu) RemoteBoardScenarioGate suppresses every peer board at every setting "
        + "— grep 'Remote board scenario gate' for the edge that decided.");
    }
}
