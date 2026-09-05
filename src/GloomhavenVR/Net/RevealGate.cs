using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

/// <summary>
/// THE anti-cheat linchpin: decides whether a remote player's ROUND cards may be shown FACE-UP
/// on their cosmetic control board, using the game's OWN authoritative phase — identical to the
/// vanilla client's reveal rule (<c>AbilityCardUI</c>). We never transmit card identities over
/// our side channel; the remote board reads them locally from the already-host-replicated
/// <c>CPlayerActor.CharacterClass</c> and only turns them face-up once this gate opens.
///
/// The rule (hide a remote actor's round-card fronts):
///   <c>FFSNetwork.IsOnline &amp;&amp; actor != null &amp;&amp; !actor.IsUnderMyControl &amp;&amp;
///      PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest</c>
///
/// <para>THE STANDING INVARIANT OF THIS FILE: EVERY PREDICATE HERE MUST DEGRADE TO THE ANSWER
/// THAT SHOWS LESS when the game state it reads is missing or mid-teardown. That is not a style
/// note, it is the whole reason the class exists, and it is easy to get backwards — because a
/// predicate that reads "safely false" can sit inside a NEGATED CONJUNCTION where false is what
/// OPENS the gate. <see cref="InScenario"/> is exactly such a term: it answers false for a null
/// <c>SaveData.Instance</c>, a not-yet-assigned <c>SaveData.Global</c>, and a campaign whose
/// <c>MapState</c> is not up — all correct answers to its own question, and all of them used to
/// fold a <c>!(online &amp;&amp; inScenario &amp;&amp; secretPhase)</c> product to TRUE = SHOW.
/// So the reveal predicates below no longer take <see cref="InScenario"/> as a term at all: the
/// phase they turn on is read straight from <c>PhaseManager</c>, which needs no save data, and a
/// phase that cannot be read at all is <c>PhaseType.None</c> — not the secret window. See the
/// paragraph on <see cref="PeersSeeOurCardFronts"/> for the leak this closed.</para>
///
/// Every game access is null-guarded and degrades to the answer that shows LESS. Simple static
/// property reads are NOT wrapped in try/catch (they cannot throw once the null guards pass);
/// only <c>SaveData.Instance</c> is null-guarded.
/// </summary>
internal static class RevealGate
{
    /// <summary>True while the game is in the secret ability-card selection phase (cards not yet
    /// committed/revealed). Reads the game's authoritative <see cref="PhaseManager"/>.
    ///
    /// <para>THE ONE TERM IN THIS FILE THAT DEPENDS ON NO SAVE STATE, which is why every reveal
    /// predicate below is written to turn on it and nothing else. <c>PhaseManager.PhaseType</c>
    /// (PhaseManager.cs:14-23) is a static read of <c>s_CurrentPhase.Type</c> that answers
    /// <c>PhaseType.None</c> when there is no phase object — so it cannot throw and cannot be
    /// starved by a half-loaded or torn-down <c>SaveData</c>.</para>
    ///
    /// <para>ITS OWN DEGRADATION IS ACKNOWLEDGED AND DELIBERATELY NOT "FIXED": no phase object
    /// reads as "not the secret window", i.e. as SHOW. There is no second source to cross-check
    /// against — this static field IS the game's authority, and vanilla's own renderers
    /// (<c>AbilityCardUI</c>, <c>BattleGoalContainer</c>) read the identical field the identical
    /// way. Inventing a more pessimistic fallback here would make the mod hide cards the game
    /// itself is showing, which is a different bug, not a safer one.</para></summary>
    public static bool IsSecretSelectionPhase =>
        PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest;

    /// <summary>True while a scenario is actually running (as opposed to the map / menus). Guards
    /// <c>SaveData.Instance</c> so a not-yet-loaded save degrades to "not in scenario".</summary>
    public static bool InScenario
    {
        get
        {
            SaveData? save = SaveData.Instance;
            GlobalData? global = save != null ? save.Global : null;
            if (global == null)
                return false;
            // GlobalData.CurrentGameState (decompiled GH.Runtime/GlobalData.cs:563) reads
            // AdventureState.MapState.IsInScenarioPhase in its Campaign branch with NO null
            // check — the Guildmaster branch three lines below it HAS one — and MapState is
            // null until StartAdventure and again after End(). So the GAME's own getter throws
            // between "Campaign picked in the menu" and "save loaded". Observed 2026-08-08 as an
            // every-frame NRE out of this property (Board.FocusDriver tick). The guard belongs
            // here because this is the property every caller funnels through.
            if (global.GameMode == EGameMode.Campaign
                && MapRuleLibrary.Adventure.AdventureState.MapState == null)
                return false;
            return global.CurrentGameState == EGameState.Scenario;
        }
    }

    /// <summary>
    /// True when <paramref name="actor"/>'s round-card FRONTS may be shown to us. Mirrors the
    /// vanilla reveal rule: hide fronts only while online, for an actor NOT under our control,
    /// during the secret selection phase. In every other case (offline / single player / map /
    /// our own actor / post-reveal action phase) the cards are shown.
    ///
    /// <para>THE <c>InScenario</c> TERM WAS REMOVED, AND ITS REMOVAL IS THE FIX RATHER THAN A
    /// SIMPLIFICATION. It sat inside a NEGATED conjunction, so the direction it degraded in was
    /// inverted on the way out: a null <c>SaveData.Instance</c> (nulled in <c>SaveData.OnDestroy</c>,
    /// SaveData.cs:166), a <c>SaveData.Global</c> not yet assigned (a plain field, SaveData.cs:40 —
    /// the game's own loader spins on <c>Global == null</c> at :150), or a campaign whose
    /// <c>MapState</c> is not up all made <see cref="InScenario"/> answer false, which folded this
    /// whole product to false and this predicate to TRUE — every remote surface drawing a peer's
    /// card FRONTS in the middle of the secret selection window. The term never closed anything on
    /// its own: <see cref="IsSecretSelectionPhase"/> can only be true while a scenario's phase
    /// machine is running (<c>PhaseManager</c> is reset at <c>AdventureState.StartAdventure</c> and
    /// at <c>ScenarioManager.InitScenario</c>, and stopped by the scenario's own ESTOPMESSAGE), so
    /// wherever <see cref="InScenario"/> was CORRECT it was a no-op, and the only states it changed
    /// were the ones where it was WRONG. Dropping it is therefore strictly more conservative.</para>
    ///
    /// <para>Callers that additionally need "a scenario is actually running" — because they touch
    /// scenario-only singletons — still ask <see cref="InScenario"/> themselves, and should: that is
    /// a CAPABILITY question, and it does not belong in a secrecy predicate (the lesson recorded in
    /// the map-phase block below).</para>
    /// </summary>
    public static bool ShowRoundCardFronts(CPlayerActor actor) =>
        !(FFSNetwork.IsOnline
          && actor != null
          && !actor.IsUnderMyControl
          && IsSecretSelectionPhase);

    /// <summary>
    /// THE SAME RULE, FROM THE OTHER END OF THE WIRE: do our PEERS currently see the fronts of OUR
    /// cards? It is <see cref="ShowRoundCardFronts"/> evaluated on our own actor from a peer's seat,
    /// where <c>!actor.IsUnderMyControl</c> is true by construction (every one of our characters is
    /// "somebody else's" to them) and therefore folds out — leaving the two terms a remote client can
    /// still check for itself: online, and in the secret selection window.
    ///
    /// <para>THE LEAK THIS CLOSED (2026-08-24). This predicate used to carry an <see cref="InScenario"/>
    /// term as well, and it is the ONE place in the mod where a wrong answer puts a card IDENTITY on
    /// the wire rather than merely drawing a wrong picture: its consumer is
    /// <c>WorldUI.WorldTooltips.ContentPublicToPeers</c> (WorldTooltips.cs:880), the gate on whether
    /// this client TRANSMITS the tooltip text of the card it is hovering — and that text names the
    /// card. In the negated product, <see cref="InScenario"/> answering false (null
    /// <c>SaveData.Instance</c>, unassigned <c>SaveData.Global</c>, campaign with no <c>MapState</c>
    /// yet) made the whole predicate answer TRUE = "peers already see our fronts, so we may say what
    /// this card is" — during the exact phase in which card identity is the sanctioned secret. The
    /// term is gone; the phase, read from <c>PhaseManager</c> with no save-data dependency at all, is
    /// the only thing this turns on now. The cost of the new failure direction is a peer seeing a
    /// generic tooltip instead of a named one, which is the direction this file is required to fail
    /// in.</para>
    ///
    /// <para>WHY IT EXISTS AS A NAMED PREDICATE RATHER THAN AN INLINE EXPRESSION. It is the gate on
    /// what the mod may SAY about our own cards over the side channel — today the board tooltip's text
    /// (<c>WorldUI.WorldTooltips.ContentPublicToPeers</c>), because a tooltip names the card as surely
    /// as the card's face does. That decision must not be able to drift away from what the peers are
    /// actually RENDERING, and it did: the tooltip used to be gated on a PLACE (only a card parked in
    /// a round-card slot could be described), because at the time the round slots were the only local
    /// cards whose faces peers ever drew. The user ruling of 2026-08-08 retired that — a peer's hand
    /// fan, their pile-browse arcs and their item fan all show FRONTS now, and the only thing that
    /// hides anything is the PHASE. With one predicate, "what a peer can see" and "what we may say"
    /// are the same sentence in the same class.</para>
    ///
    /// <para>Note it is not a secrecy loosening either: vanilla itself lets any player open any other
    /// player's complete card overview from the initiative track
    /// (<c>InitiativeTrackPlayerAvatar.OnClick → CardsHandManager.ToggleViewAllCards</c>), so outside
    /// the selection window a card identity is public information in the base game.</para>
    /// </summary>
    public static bool PeersSeeOurCardFronts =>
        !(FFSNetwork.IsOnline && IsSecretSelectionPhase);

    // ============================================================================================
    //  THE MAP PHASE — THE ANSWER THIS CLASS ALREADY GAVE, AND THE ONE ITS CALLER THREW AWAY
    //
    //  USER REPORT, VERBATIM (2026-08-22, item 4): "Handkarten sind nicht sichtbar im Multiplayer
    //  im Map-Bereich. Das soll nicht sein, die Handkarten sollen wie in der Aktionsphase im
    //  Szenario voll sichtbar sein, wenn man den Fächer eines anderen Spielers betrachtet. Aktuell
    //  sieht man nur die Rückseiten (wie es zur Auswahlphase der Fall ist)."
    //
    //  He is describing the 3D map room, where the mod grew its own card hand in ModBuild 192
    //  (WorldUI/MapRoom/MapRoomHand.*) and a peer's fan is therefore the SCENARIO LOADOUT they are
    //  about to travel with. And he had already ruled on it the day the feature was designed
    //  (2026-08-21): "Weiterhin gibt es keine Geheimnisse in dieser Phase, das heißt schon hier
    //  sollen alle Karten voll sichtbar sein der jeweiligen Mitspieler im MP, wenn sie sich die
    //  Karten anschauen."
    //
    //  ─── WHERE THE BACKS CAME FROM, READ FROM SOURCE ─────────────────────────────────────────
    //  NOT from this class. <see cref="ShowRoundCardFronts"/> is WIDE OPEN on the map and always
    //  was — its conjunction folds in <see cref="InScenario"/>, which is false there, so the whole
    //  negated product is false and the predicate answers TRUE. Its own doc comment names the case
    //  ("offline / single player / MAP / our own actor / post-reveal action phase").
    //
    //  The backs came from the CALLER. <c>RemoteHandFan.UpdateFaces</c> required a SECOND term of
    //  its own before it would resolve a front:
    //        if (actor != null && RevealGate.InScenario && RevealGate.ShowRoundCardFronts(actor))
    //  and that <c>InScenario</c> was never an anti-cheat term. It was a CAPABILITY term, and its
    //  comment says so — "require an actual running scenario before touching the game's hand UI
    //  (the clone's widget lifecycle depends on scenario singletons)". It is true and it is right
    //  for the path it guards: <c>RemoteHandFan.ResolveHandFronts</c> reads
    //  <c>CardsHandManager.Instance.GetHand(actor).cardsUI</c>, and neither the manager nor the
    //  <c>CPlayerActor</c> exists in the map phase. What was wrong is that the SAFE DEFAULT of a
    //  capability test ("we cannot resolve fronts here") was left standing as the ANSWER to a
    //  secrecy question ("these cards are secret"). Two different sentences, one boolean.
    //
    //  So the fix is not to relax a rule; it is to give the map phase its own capability — the
    //  loadout the map room already resolves locally, out of the replicated
    //  <c>CMapCharacter.HandAbilityCardIDs</c> (MapRoomHand.TryResolvePeerLoadout) — and to make
    //  the secrecy question answer for itself, HERE, in this class's own vocabulary.
    //
    //  ─── WHY THE MAP PHASE IS A DIFFERENT CASE FROM THE SELECTION PHASE ──────────────────────
    //  The secrecy rule this class exists for has ONE purpose: during
    //  <c>SelectAbilityCardsOrLongRest</c> every player is simultaneously committing the two cards
    //  they are about to play, and seeing a teammate's choice before your own is locked is playing
    //  with their information. That is a rule about a DECISION IN FLIGHT. On the map no such
    //  decision is in flight: the loadout is a standing, already-committed fact about a character,
    //  and nothing about round order, initiative or targeting turns on keeping it from the table.
    //
    //  And the base game agrees, which is the same argument <see cref="PeersSeeOurCardFronts"/>
    //  makes from vanilla's initiative-track overview. In the flat party window
    //  <c>UIPartyCharacterAbilityCardsDisplay</c> shows EVERY selected character's whole ability
    //  pool with the loadout ticked, for any character you click, ownership or not: the cards are
    //  unconditionally <c>SetActive(true)</c> (:307) and the ONLY thing online ownership gates is
    //  <c>SetSelectable</c> — i.e. whether you may EDIT it (:305, :330, and
    //  <c>CanBeAbilityCardToggled</c> :313-319). A peer's loadout is therefore already public
    //  information in the flat game; the mod would be INVENTING a secret by hiding it in VR.
    //
    //  ANTI-CHEAT IS NOT WEAKENED BY ONE BYTE. Both predicates below require the absence of a
    //  running scenario, so neither can ever be true in the window
    //  <see cref="IsSecretSelectionPhase"/> describes — they are disjoint by construction, not by
    //  agreement between two conditions that could drift apart. Nothing here can open a front that
    //  <see cref="ShowRoundCardFronts"/> would close.
    //
    //  ─── CORRECTION (2026-08-24): THAT PARAGRAPH WAS TRUE OF THE INTENT AND FALSE OF THE CODE ──
    //  "Disjoint by construction" was doing the work of a load-bearing safety claim while resting
    //  on a term that could be wrong. <see cref="InMapPhase"/> asked TWO things — "not in a
    //  scenario" (<see cref="InScenario"/>) and "a map is loaded"
    //  (<c>AdventureState.MapState != null</c>) — and only the FIRST of those separates the map
    //  from a running scenario. <c>MapState</c> is one static, assigned in
    //  <c>AdventureState.StartAdventure</c> and cleared only in <c>End()</c>
    //  (AdventureState.cs:9-34): it is NON-NULL FOR THE WHOLE ADVENTURE, scenarios included. The
    //  game itself never uses its nullness as the discriminator — <c>GlobalData.CurrentGameState</c>
    //  asks <c>MapState.IsInScenarioPhase</c> (GlobalData.cs:563-585). So the entire separation
    //  between "map room" and "secret selection phase" hung on <see cref="InScenario"/>, whose
    //  degradation is FALSE, and false here meant "we are on the map" — i.e. show a peer's fan
    //  face-up. The review that found this believed the exposure was limited to the map loadout;
    //  it was not. <c>RemoteHandFan.UpdateFaces</c> resolves the map branch out of the peer's
    //  CURRENT hand, so in a scenario that branch would have shown the real round hand.
    //
    //  The remedy is below and it is the same one applied to the two reveal predicates above: stop
    //  inferring the phase from save state that can go missing, and ask the game's own
    //  authoritative bit, which needs no save data.
    // ============================================================================================

    /// <summary>
    /// True while a MAP is loaded and NO scenario is running — the campaign/guildmaster map phase,
    /// the state the mod's 3D map room stands in. <c>MapState</c> is the same object
    /// <see cref="InScenario"/> guards against being null (see the note there) and the same one
    /// <see cref="ShowPersonalQuest"/> reads the party out of, so a half-loaded save answers false
    /// on every one of them alike. Guarded and degrading to false, because "we cannot tell where we
    /// are" must never be the reason a front is drawn.
    ///
    /// <para>THREE INDEPENDENT TERMS, EACH ABLE TO CLOSE THIS ON ITS OWN, because this predicate is
    /// the only thing standing between a peer's fan and the secret window (see the correction in
    /// the block above for how the previous two-term version could open inside a scenario):</para>
    /// <list type="number">
    /// <item><description><c>MapState.IsInScenarioPhase</c> — THE GAME'S OWN DISCRIMINATOR
    /// (CMapState.cs:198-208, the bit <c>GlobalData.CurrentGameState</c> itself branches on). It
    /// reads no save data and no phase machine, so it stays correct in exactly the states that
    /// broke <see cref="InScenario"/>. This is the term that actually fixes the hole.</description></item>
    /// <item><description><see cref="InScenario"/> — kept, unchanged, as the second opinion. Where
    /// it is right it agrees; where it is wrong term 1 has already answered.</description></item>
    /// <item><description><c>!IsSecretSelectionPhase</c> — a backstop that cannot be reached in any
    /// consistent state (a map that says "no scenario here" while the phase machine says "secret
    /// selection" is self-contradictory). It costs a map-room fan nothing in any state the game can
    /// actually be in, and if the two ever DO disagree, this file's job is to show less. Kept
    /// deliberately, and flagged here: if a peer's map-room fan is ever seen showing BACKS, this is
    /// the term to suspect first, because a false negative here is the user's 2026-08-21 ruling
    /// ("alle Karten voll sichtbar") going wrong.</description></item>
    /// </list>
    /// </summary>
    public static bool InMapPhase
    {
        get
        {
            try
            {
                MapRuleLibrary.State.CMapState? map = MapRuleLibrary.Adventure.AdventureState.MapState;
                if (map == null)
                    return false; // no adventure loaded — menus, single scenario, level editor
                if (map.IsInScenarioPhase)
                    return false; // a scenario IS running, whatever the save state says about it
                if (IsSecretSelectionPhase)
                    return false; // contradictory state — show less (see term 3 above)
                // InScenario stays INSIDE the try. It is guarded, but its last line reads the game's
                // own GlobalData.CurrentGameState, whose non-Campaign branches deref
                // SaveData.Instance.Global with no guard of their own (GlobalData.cs:585) — and a
                // throw escaping a secrecy predicate is a crash where the answer should have been
                // "show less". The previous version evaluated it before the try and had that hole.
                return !InScenario;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// May a REMOTE player's card hand be shown FACE-UP in the map phase? Yes: on the map a hand is
    /// PUBLIC. See the block above for the whole derivation — the short version is that the secrecy
    /// rule protects a decision in flight, the map has none in flight, and the flat game's own party
    /// window already shows any character's loadout to anybody who clicks them.
    ///
    /// <para>NO ACTOR PARAMETER, and that is a statement rather than an omission: there is no
    /// <c>CPlayerActor</c> in the map phase at all (<c>CMapCharacter.GetActor()</c> reads
    /// <c>ScenarioManager.Scenario</c>, which is null here, so asking for one THROWS). The map-phase
    /// unit of identity is <c>CMapCharacter</c>, and the per-actor terms
    /// <see cref="ShowRoundCardFronts"/> folds in — <c>IsUnderMyControl</c> and the phase — have no
    /// counterpart that could hide anything: ownership does not make a loadout secret in the flat
    /// game either (only uneditable), and there is no phase here to be secret in.</para>
    ///
    /// <para>WHAT THIS DOES NOT GRANT. It says a hand may be READ; it says nothing about a hand
    /// being PLAYABLE. A peer's fan stays inspect-only on every path — see
    /// <c>Cards.CardBorrow</c> and <c>RemoteHandFan</c>'s borrow section, whose guarantee is
    /// structural (a borrowed copy carries no <c>VRCard.GameCard</c> and every commit seam in
    /// <c>Cards.CardsDriver</c> is reached only through one) and is untouched by this predicate.</para>
    /// </summary>
    public static bool ShowMapPhaseHandFronts => InMapPhase;

    /// <summary>
    /// WHICH POPULATION a peer-card surface belongs to, for <see cref="CardFaces"/> — the ONE term
    /// that decides whether the game's secret selection window applies to it.
    ///
    /// <para>IT IS A PARAMETER RATHER THAN A SECOND METHOD ON PURPOSE. The user's ruling of
    /// 2026-09-05 is absolute — "Gewährleiste das außerhalb der Auswahlphase NIEMALS Rückseiten auf
    /// Vorderseiten angezeigt werden sondern immer die echte Vorderseite. IMMER OHNE AUSNAHME" —
    /// and it carries ONE carve-out (the selection phase) plus ONE carve-out from THAT (the active
    /// card matrix beside the control board, "das ist kein Geheimnis. Auch in der Auswahlphase").
    /// A rule with two nested exceptions written as two sibling methods is a rule two surfaces can
    /// pick the wrong half of; written as an enum every caller must name, the exception is
    /// something a surface DECLARES about itself and the arithmetic stays in one place.</para>
    /// </summary>
    public enum PeerCardPopulation
    {
        /// <summary>Cards whose identity is the sanctioned secret of the game's own
        /// <c>SelectAbilityCardsOrLongRest</c> window: a peer's HAND fan, the card in their hand,
        /// their round-card slots, their pile-browse arcs and their item fan. THE CARVE-OUT: while
        /// <see cref="IsSecretSelectionPhase"/> is open online for a character that is not ours,
        /// these show BACKS. Outside that window they show the real front, without exception.
        /// </summary>
        Selectable,

        /// <summary>Cards that are ALREADY PUBLIC and therefore exempt from the carve-out above —
        /// today exactly the ACTIVE / persistent card matrix beside the peer's control board
        /// (<c>Net.RemoteActiveCards</c>).
        ///
        /// <para>THE CARVE-OUT FROM THE CARVE-OUT, and it is the user's, verbatim (2026-09-05, item
        /// 2b): "Die Vorderseite der aktiven Karten in der kleinen Matrix neben dem Controllboard
        /// soll IMMER angezeigt werden - das ist kein Geheimnis. Auch in der Auswahlphase." He is
        /// right about the game as well as about the picture: an active card is on the table
        /// BECAUSE it was played face-up in front of everybody in an earlier round, so its identity
        /// was public before the current selection window opened and hiding it now protects
        /// nothing. Vanilla agrees — <c>ActivePileViewer</c> has no phase term at all.</para>
        ///
        /// <para>WHAT THIS DOES NOT COVER, stated so the next surface does not adopt it by
        /// analogy: a peer's DISCARD pile is also "cards that were played", and it is deliberately
        /// left in <see cref="Selectable"/>. The user ruled on the active matrix and on nothing
        /// else, and this file's standing invariant is to show LESS when nobody has ruled.</para>
        /// </summary>
        AlreadyPublic,
    }

    /// <summary>
    /// WHERE A REMOTE PLAYER'S CARD FACES MAY COME FROM RIGHT NOW — the one call every remote
    /// ability-card surface asks, so that no two of them can answer the secrecy question
    /// differently.
    /// </summary>
    public enum CardFaceSource
    {
        /// <summary>No fronts. Either the game's own secret selection window is open for this
        /// character, or there is no context in which a face could be resolved at all.</summary>
        None,

        /// <summary>A running scenario: the faces come from that character's live
        /// <c>AbilityCardUI</c> widgets (<c>CardsHandManager</c>, <c>CardsGameApi.GetPileWidgets</c>).
        /// </summary>
        Scenario,

        /// <summary>The map room: there is no <c>CPlayerActor</c> and no <c>CardsHandManager</c>, so
        /// the faces come from the peer's replicated map LOADOUT
        /// (<c>MapRoomHand.TryResolvePeerLoadout</c> off <c>CMapCharacter.HandAbilityCardIDs</c>).
        /// </summary>
        MapLoadout,
    }

    /// <summary>
    /// THE ONE PREDICATE EVERY REMOTE ABILITY-CARD SURFACE ROUTES THROUGH. Answers both halves of
    /// the question at once — MAY this surface show fronts, and WHICH SOURCE can supply them —
    /// because those two were the halves that came apart.
    ///
    /// <para>WHY THIS EXISTS AS A METHOD RATHER THAN AS A CONVENTION. Every remote card surface used
    /// to spell its own gate, and every one of them spelled the same thing:
    /// <c>RevealGate.InScenario &amp;&amp; RevealGate.ShowRoundCardFronts(actor)</c>. That
    /// conjunction is not one test. <see cref="ShowRoundCardFronts"/> is the SECRECY question and it
    /// is wide open on the map; <see cref="InScenario"/> is a CAPABILITY question — "can a face be
    /// resolved here at all", true because the scenario resolve path needs scenario singletons — and
    /// leaving a capability test standing as the answer to a secrecy question is precisely the
    /// ModBuild-192 map-room defect this file's own block above is written about.</para>
    ///
    /// <para>THAT DEFECT WAS FIXED ONCE, ON ONE SURFACE, AND CAME BACK ON THE NEXT. The hand FAN
    /// grew a second branch on <see cref="ShowMapPhaseHandFronts"/> and reads correctly on the map.
    /// The HELD CARD (<c>Net.RemoteHeldCardFace</c>) kept the old conjunction, so in the map room a
    /// peer's fan showed its fronts while the card in his hand showed only its back — 2026-09-02
    /// report item 5a, which is the SAME defect one surface over. Two branches that happen to agree
    /// is what let that happen, so there are no longer two: both surfaces switch on this, and a
    /// third surface added later cannot drift because there is nothing left for it to drift FROM.
    /// </para>
    ///
    /// <para><paramref name="population"/> IS THE WHOLE OF THE SECRECY DIFFERENCE BETWEEN SURFACES,
    /// and there is nothing else for one to get wrong: every caller states which population it draws
    /// and this method owns both nested exceptions (see <see cref="PeerCardPopulation"/>). The
    /// answer for <see cref="PeerCardPopulation.AlreadyPublic"/> differs from the other in exactly
    /// one term — the phase — and in nothing about WHERE the faces come from, which is why it is the
    /// same call and not a second one.</para>
    ///
    /// <para>The actor is optional because the map phase has none — see
    /// <see cref="ShowMapPhaseHandFronts"/> for why that is a statement about the game's model and
    /// not a missing argument. Never throws: any failure answers <see cref="CardFaceSource.None"/>,
    /// which is the direction this whole file is required to fail in.</para>
    /// </summary>
    public static CardFaceSource CardFaces(PeerCardPopulation population,
                                           ScenarioRuleLibrary.CPlayerActor? actor)
    {
        try
        {
            if (InScenario)
            {
                if (actor == null)
                    return CardFaceSource.None;
                // THE CARVE-OUT AND THE CARVE-OUT FROM IT, in one expression and in this order: the
                // secrecy term is asked ONLY of a population the secret applies to. An already
                // public card still needs a running scenario (the capability half above) and still
                // needs a character to resolve against — it just has no phase to be secret in.
                bool secret = population == PeerCardPopulation.Selectable
                              && !ShowRoundCardFronts(actor);
                return secret ? CardFaceSource.None : CardFaceSource.Scenario;
            }
            return ShowMapPhaseHandFronts ? CardFaceSource.MapLoadout : CardFaceSource.None;
        }
        catch
        {
            return CardFaceSource.None;
        }
    }

    // ============================================================================================
    //  PER-CHARACTER GOALS — the SECOND secret this game has, and the one the free character focus
    //  put within reach. Researched from the game's OWN code (2026-08-08); the findings and their
    //  evidence, because the answer is not what board-game folklore predicts:
    //
    //   • SCENARIO OBJECTIVES + the quest header ("Aufgaben", MissionObjectiveContainer) are
    //     PUBLIC and party-wide. The header is AdventureState.MapState.InProgressQuestState — the
    //     party's in-progress scenario, not anybody's personal anything
    //     (MissionObjectiveContainer.cs:41-53) — and the rows are the scenario's own
    //     Win/LoseObjectives. There is no ownership test in that file at all. CObjective.IsHidden
    //     exists but is a SCENARIO-DESIGN flag, identical on every client: the game compares it
    //     across clients as a desync check (CObjective.cs:573-588, "CObjective IsHidden does not
    //     match"), which is only meaningful if it is supposed to be the same for everybody.
    //     ⇒ Nothing to gate. <see cref="RemoteObjectivesPanel"/> stays a GLOBAL surface.
    //
    //   • BATTLE GOAL (the per-character secret goal for the running scenario — what a player
    //     means by "die Szenario-Quest meines Characters") is SECRET, unconditionally, online.
    //     ActorStatPanel.cs:566 — the game's own "inspect this character" card:
    //         if (AdventureState.MapState?.InProgressQuestState != null
    //             && (!FFSNetwork.IsOnline || actor.IsUnderMyControl)) { …show… }
    //         else battleGoalContainer.SetActive(false);
    //     and the in-scenario HUD does the same twice over (BattleGoalContainer.cs:44 in Show,
    //     :73/:81 in UpdateProgress). Note it is a RENDER-time rule only: the chosen goal is
    //     broadcast in the clear (BattleGoalService.cs:31-43 → BattleGoalMultiplayerService.cs) and
    //     every client holds it. Which is exactly why the mod must re-implement the rule rather
    //     than assume the data is unavailable.
    //     ⇒ <see cref="ShowBattleGoal"/>.
    //
    //   • PERSONAL QUEST (the campaign retirement / life goal) is PUBLIC BY DEFAULT and secret
    //     only when its owner opted in. ActorStatPanel.cs:557:
    //         if (pq == null || (pq.IsConcealed && FFSNetwork.IsOnline && !actor.IsUnderMyControl))
    //             personalQuestContainer.SetActive(false); else { …show… }
    //     CPersonalQuestState.IsConcealed is a player-flipped toggle (UICampaignAssemblyCharacter-
    //     Information.cs:46-49, hotkey KeyAction.CONCEAL_PQ) that RESETS TO false
    //     (CPersonalQuestState.cs:271) — i.e. visible unless hidden. The game even ANNOUNCES a
    //     peer's personal-quest progress by name to everybody (UIPersonalQuestResultManager.cs:258+).
    //     ⇒ <see cref="ShowPersonalQuest"/>, same three-clause shape as the game's.
    //
    //  Both predicates are written as the game writes them — offline / single player answers TRUE,
    //  because offline every merc is ours and the game shows both surfaces freely.
    // ============================================================================================

    /// <summary>
    /// May <paramref name="actor"/>'s BATTLE GOAL (the per-character secret scenario goal) be shown
    /// to the local player? Verbatim <c>ActorStatPanel.cs:566</c> / <c>BattleGoalContainer.cs:44</c>:
    /// online, only for a character under our own control.
    ///
    /// <para>THIS IS THE RULE THAT DECIDES THE FOCUS FEATURE'S QUEST QUESTION: a player who focuses
    /// a character they do NOT own must not be shown that character's battle goal — not on their own
    /// board, and not via a peer's mirrored board either. It is deliberately a property of the
    /// CHARACTER and the VIEWER, never of who is looking: a mirrored board renders on the VIEWER's
    /// machine, so the viewer's own entitlement is the only one that can be evaluated there, and it
    /// is the strictly safer of the two (see <see cref="RemoteBoardFocus"/> rule 2).</para>
    ///
    /// <para>ITS LIVE CALLER (since 2026-08-24) is the control board's own battle-goal line,
    /// <c>WorldUI.Surfaces.TablePanelSurfaces.BuildQuestText</c>, which used to open-code the same
    /// three terms beside its own null check. They agreed — which is the failure mode, not the
    /// mitigation, because a mirrored secrecy rule leaks the first time only one copy is edited.
    /// One rule, one home. A future surface that wants a battle goal asks HERE.</para>
    ///
    /// <para>DEGRADATION, STATED HONESTLY: this is the game's expression verbatim, and
    /// <c>FFSNetwork.IsOnline</c> is <c>BoltNetwork.IsRunning &amp;&amp; !IsShuttingDown</c>
    /// (FFSNetwork.cs:25-35), so a session TEARING DOWN reads offline and this answers "show". That
    /// is not corrected here on purpose: it is exactly what vanilla's own <c>ActorStatPanel</c> and
    /// <c>BattleGoalContainer</c> do with the identical read, the surface is the LOCAL player's own
    /// hand, and diverging would make the mod hide a goal the flat game is displaying. It is the one
    /// term in this file whose degradation is inherited rather than chosen.</para>
    /// </summary>
    public static bool ShowBattleGoal(CActor? actor) =>
        !(FFSNetwork.IsOnline && (actor == null || !actor.IsUnderMyControl));

    /// <summary>
    /// May <paramref name="actor"/>'s PERSONAL QUEST (campaign life goal) be shown? Verbatim
    /// <c>ActorStatPanel.cs:557</c>: hidden only when its owner CONCEALED it and it is not ours.
    /// A quest we cannot resolve at all answers false — "no quest to show" and "not allowed to
    /// show it" are the same outcome for a renderer, and false is the safe one.
    /// </summary>
    public static bool ShowPersonalQuest(CActor? actor)
    {
        if (actor == null)
            return false;
        try
        {
            MapRuleLibrary.State.CMapState? map = MapRuleLibrary.Adventure.AdventureState.MapState;
            if (map == null || !map.IsCampaign || map.MapParty == null)
                return false; // guildmaster / no campaign party — the game shows no PQ panel either
            string? id = actor.Class != null ? actor.Class.ID : null;
            if (string.IsNullOrEmpty(id))
                return false;
            MapRuleLibrary.Party.CPersonalQuestState? quest = null;
            // SelectedCharactersArray, not the LINQ-filtered SelectedCharacters property: same
            // members, no per-call enumerator allocation on a path a per-frame surface may read.
            MapRuleLibrary.Party.CMapCharacter[]? chars = map.MapParty.SelectedCharactersArray;
            if (chars == null)
                return false;
            for (int i = 0; i < chars.Length; i++)
            {
                MapRuleLibrary.Party.CMapCharacter c = chars[i];
                if (c != null && c.CharacterID == id)
                {
                    quest = c.PersonalQuest;
                    break;
                }
            }
            if (quest == null)
                return false;
            return !(quest.IsConcealed && FFSNetwork.IsOnline && !actor.IsUnderMyControl);
        }
        catch
        {
            return false; // a half-loaded campaign state must never leak by accident
        }
    }
}
