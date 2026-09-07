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
    /// THE PARTITIONED OWNERSHIP TERM — "does THIS client control this character?", asked of the
    /// record the game itself keeps rather than of the cached bool that answers it wrong.
    ///
    /// <para>WHY <c>CActor.IsUnderMyControl</c> IS NOT A PARTITION (Lane F's finding, 2026-09-07
    /// report item 10, read from the game's own source). It is a plain auto-property — a CACHED
    /// PER-CLIENT BOOLEAN — and its two FFSNet writers are asymmetric:
    /// <c>CharacterManager.OnControlAssigned</c> (CharacterManager.cs:479-486) sets it TRUE only
    /// when <c>PlayerRegistry.MyPlayer.PlayerID == controller.PlayerID</c>, so the SET is scoped;
    /// <c>OnControlReleased</c> (CharacterManager.cs:488-495) clears it with NO identity test at
    /// all. A scoped set with an unscoped clear can be stale TRUE on the wrong client and stale
    /// FALSE on the right one AT THE SAME TIME — so <c>IsUnderMyControl</c> and
    /// <c>!IsUnderMyControl</c> are not complements across the table, which is precisely what a
    /// secrecy predicate needs them to be.</para>
    ///
    /// <para>STRICTLY MORE ACCURATE, NEVER MORE PERMISSIVE BY GUESS — Lane F's contract, kept
    /// verbatim here because this file is the one place where "more permissive" means a LEAK.
    /// <c>Cards.CardsGameApi.LocalControlsActor</c> reads <c>NetworkPlayer.MyControllables</c>,
    /// which is scoped at both edges and is the record the game subscribes to; when it cannot
    /// answer (offline, FFSNet absent, reflection incomplete, no local player yet) the flag is read
    /// exactly as before, so single player and every non-FFSNet build are byte-identical. Only a
    /// DISAGREEMENT changes an answer.</para>
    ///
    /// <para>ITS DEGRADATION IS THIS FILE'S STANDING DIRECTION, and that is checkable rather than
    /// asserted: all three call sites read the term NEGATED inside a product this predicate
    /// negates, so <c>false</c> here means "not ours" means SHOW LESS. A null actor answers false
    /// for that reason. A non-player <c>CActor</c> keeps the flag, which is today's behaviour
    /// unchanged — <c>MyControllables</c> would not list one anyway.</para>
    ///
    /// <para>DELIBERATELY NOT <c>Board.CharacterFocus.IsForeign</c>: <c>CharacterFocus</c> carries
    /// <c>using GloomhavenVR.Net;</c> and routing through it would point this file's dependency back
    /// at a consumer of it. <c>CardsGameApi</c> is the low-level game-read API every remote surface
    /// in this namespace already calls directly, so this follows the established direction.</para>
    ///
    /// <para>NOT SUBSTITUTED INTO <see cref="PeersSeeOurCardFronts"/>, and that is a statement: the
    /// ownership term FOLDS OUT there by construction (every character we control is "somebody
    /// else's" to a peer), so there is no read to correct and adding one would invent a term the
    /// predicate's whole derivation says must not exist.</para>
    /// </summary>
    private static bool LocallyControls(CActor? actor)
    {
        if (actor == null)
            return false;
        if (actor is CPlayerActor player)
        {
            bool byList = Cards.CardsGameApi.LocalControlsActor(player, out bool answerable);
            if (answerable)
                return byList;
        }
        return actor.IsUnderMyControl;
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
          && !LocallyControls(actor)
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

    /// <summary>
    /// MAY OUR PEERS BE TOLD, IN WORDS, WHICH CARD THIS IS, right now? The owner-seat NAMING
    /// predicate: the phase (<see cref="PeersSeeOurCardFronts"/>), then the burn/active exception
    /// for this particular card (<see cref="IsPubliclyRevealedCard"/>), which can only widen.
    ///
    /// <para>IT IS THE SISTER OF <see cref="CardFaces(PeerCardPopulation, CPlayerActor, int)"/> AND
    /// NO LONGER ITS TWIN. Both were the same two terms in the same order until 2026-09-07 evening;
    /// the face side has since gained the pile-fan ruling (<see cref="IsDiscardedCard"/>) and this
    /// side deliberately has not. See the rename paragraph below for the leak that separation
    /// prevents.</para>
    ///
    /// <para>IT EXISTS SO A CARD AND THE SENTENCE ABOUT IT CANNOT DISAGREE BY ACCIDENT. A face and a
    /// WORDING that name the same card are two surfaces answering one question, and this project has
    /// already paid for letting two surfaces answer it separately (see the note on
    /// <see cref="CardFaces(PeerCardPopulation, CPlayerActor)"/>: the hand fan and the held card
    /// each spelled their own gate and drifted apart the moment only one was edited). Where they
    /// disagree now they disagree ON PURPOSE, in ONE named term, with the ruling written beside
    /// it — which is the opposite of two surfaces drifting.</para>
    ///
    /// <para>WHY IT IS ASKED AT THE SENDER AND NOT AT THE RECEIVER, and this is the whole reason the
    /// exemption below is an ANTI-CHEAT boundary rather than a presentation preference: a receiver
    /// that is handed the name has been told the secret, whatever it then chooses to draw. This
    /// class's opening sentence is "We never transmit card identities over our side channel"; the
    /// decision row was the one place that was not true, and masking at the render end would have
    /// left it untrue. The identity does not go on the wire.</para>
    ///
    /// <para>The actor is the OWNER's own — every character we control is "somebody else's" to a
    /// peer, so <c>!IsUnderMyControl</c> folds out exactly as it does for
    /// <see cref="PeersSeeOurCardFronts"/>. Degrades to FALSE (mask it) on a null actor or an
    /// unknown card, which is this file's standing direction of failure.</para>
    ///
    /// <para>IT WAS CALLED <c>PeersMaySeeOurCard</c> UNTIL 2026-09-07 EVENING AND THE RENAME IS THE
    /// FIX, NOT A TIDY-UP. Every one of its callers asks it about a SENTENCE — the mirrored decision
    /// row's wording (<see cref="DecisionLabelMask"/>), the board tooltip's text
    /// (<c>WorldUI.Tooltips.WorldTooltips</c>), the mandatory-use hint's card-name keys
    /// (<c>WorldUI.Surfaces.DamageTooltipSurface</c>) — and not one asks it about a FACE. Faces go
    /// through <see cref="CardFaces(PeerCardPopulation, CPlayerActor, int)"/>. While the two
    /// questions had the same answer the shared name was harmless; item 3 separated them, because a
    /// card in a peer's DISCARD fan may now be SEEN by everyone while its name may still not be
    /// SPOKEN by a short-rest prompt, and a predicate whose name says "see" while it answers "name"
    /// is one edit away from the ModBuild 477 item 7 leak coming back.</para>
    ///
    /// <para>THE LEAK, SO THE NEXT READER CAN SEE WHY THE TWO MAY NOT BE RE-MERGED. ModBuild 477
    /// published <c>confirm='Verbrennen "Zusatzdolch"'</c> to every peer while
    /// <see cref="PeersSeeOurCardFronts"/> was shut, and <c>SpareDagger</c> was sitting in that
    /// character's <c>DiscardedAbilityCards</c> at the time.
    /// <c>DecisionLabelMask.AddCovered</c> SKIPS every card this predicate permits — so the day
    /// "discard membership" is folded in here, the mask stops masking the very card a short-rest
    /// prompt names and 478's fix is undone. The two questions are compatible in SUBSTANCE and only
    /// in substance: seeing every card in a peer's discard fan says nothing about WHICH of them the
    /// game has singled out as the sacrifice, and that selection is the secret. Keep them two
    /// predicates.</para>
    /// </summary>
    public static bool PeersMayNameOurCard(CPlayerActor? actor, int cardInstanceId) =>
        PeersSeeOurCardFronts || IsPubliclyRevealedCard(actor, cardInstanceId);

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
    /// <c>RemoteHandFan</c>'s "nothing is handed out" section — the borrow was removed on
    /// 2026-09-07 and <c>Cards/CardBorrow.cs</c> with it — whose guarantee is
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
        /// <para>WHAT THIS USED TO SAY IT DID NOT COVER, AND THE USER HAS SINCE RULED ON IT. The
        /// paragraph here read: "a peer's DISCARD pile is also 'cards that were played', and it is
        /// deliberately left in <see cref="Selectable"/>. The user ruled on the active matrix and on
        /// nothing else, and this file's standing invariant is to show LESS when nobody has ruled."
        /// That was the correct answer while nobody had ruled. He has now ruled, twice — 2026-09-07
        /// afternoon for discard and burnt, 2026-09-07 evening for items as well — and the discard
        /// pile is answered by <see cref="IsDiscardedCard"/>, a property of the CARD, rather than by
        /// a second exempt member here. See <see cref="IsPublicPopulation"/> for why the split
        /// between "a place is exempt" and "a card is public" is load-bearing and not cosmetic.
        /// </para>
        /// </summary>
        AlreadyPublic,

        /// <summary>
        /// An ITEM card — a chip in a peer's equipped-item arc, or one in their fist that came out
        /// of it. Exempt from the selection-phase carve-out.
        ///
        /// <para>USER, VERBATIM (2026-09-07 evening, item 3): "Wir haben beim Refactoring
        /// vereinbart, dass es keinen Sinn macht die Fächer der Piles (Abgeworfen, Verbrannt und
        /// Items) jemals (auch in der Auswahlphase) mit der Rückseite anzuzeigen. … Die Fächer der
        /// piles werden also ab jetzt immer mit Vorderseiten gezeigt ohne Ausnahme."</para>
        ///
        /// <para>IT IS A KIND AND NOT A PLACE, WHICH IS THE ONLY REASON IT MAY SIT ON THE EXEMPT
        /// SIDE OF <see cref="IsPublicPopulation"/>. The three members that were removed from that
        /// side (a card in a recess, a card in the pick field) named WHERE a card was lying while a
        /// decision about it was still in flight. This one names WHAT the card is: an item is not an
        /// ability card, it is never part of the two-card commit the secret window protects, it
        /// cannot be drawn, discarded or burnt by that commit, and the flat game shows a party
        /// member's inventory to everybody. So it follows the item — a surface drawing an item
        /// declares this member whether the item is in the arc, in a fist, or on the board — and
        /// there is no state in which declaring it reveals something about the selection.</para>
        ///
        /// <para>WHY IT IS NOT ANSWERED AS A CARD PROPERTY LIKE THE DISCARD PILE IS. A card property
        /// needs a <c>CAbilityCard.CardInstanceID</c> to ask about and an item has none — the mod's
        /// item surfaces resolve <c>CItem</c> instances out of <c>CPlayerActor.Inventory.AllItems</c>
        /// and never enter the instance-id space at all. The population is the only vocabulary the
        /// two item surfaces share.</para>
        ///
        /// <para>THE FILE'S OWN PRIOR ASSERTION ABOUT ITEMS WAS HALF TRUE AND IS CORRECTED HERE.
        /// <see cref="PickFan"/>'s flow list says "items are not ability cards and never carried the
        /// selection-phase secret; they are drawn by <c>RemoteItemFan</c> through
        /// <see cref="ShowRoundCardFronts"/>". The first clause is the ruling; the second clause
        /// describes the code, and the code CONTRADICTED the first clause —
        /// <c>ShowRoundCardFronts</c> is exactly the selection-phase term, so a peer's item fan went
        /// to BACKS in the secret window and the ModBuild 478 logs measure it on both machines
        /// (<c>item fan[p2] 0 FRONT / 2 BACK — Items: RevealGate.ShowRoundCardFronts(actor)=false</c>).
        /// This member is what makes the sentence true.</para>
        /// </summary>
        ItemCard,

        /// <summary>The card a peer is SACRIFICING — the one the game lays into a round recess
        /// during a SHORT REST for its owner to accept or re-roll. NOT exempt from the
        /// selection-phase carve-out (<see cref="IsPublicPopulation"/> answers false for it); the
        /// member survives to make a call site and the hardware log say WHICH population a recess
        /// drew, which is the same reason <see cref="PickFan"/> exists.
        ///
        /// <para>USER, VERBATIM (2026-09-07, item 6 — the THIRD statement of this rule and the one
        /// that governs): "Kurze Rast habe ich die Vorderseite der Karte gesehen, das ist die
        /// Auswahlphase und darf daher nicht sein. Nochmal: Kurze Rast = Auswahlphase = verdeckt,
        /// Lange Rast = Aktionsphase = alles offen."</para>
        ///
        /// <para>THIS MEMBER WAS AN EXEMPTION FOR TWO BUILDS, ON HIS EARLIER REQUEST (2026-09-05,
        /// item 15): "Bei einer kurzen Rast soll es sichtbar sein welche Karte dort liegt — ich
        /// sehe nur die Rückseite." THE CORRECTION IS RECORDED RATHER THAN OVERWRITTEN because the
        /// two rulings look contradictory and are not: they are separated in time by
        /// <c>FinalizeShortRest</c>. See <see cref="IsPublicPopulation"/> for the whole derivation.
        /// The short version is that the paragraph below had the right argument about the WRONG
        /// MOMENT.</para>
        ///
        /// <para>WHY THE ARGUMENT BELOW IS WRONG WHILE THE CARD MERELY LIES THERE. It reasons that
        /// a sacrifice "is a card LEAVING the player's resources" and therefore not a decision in
        /// flight. That is true of the card AFTER the accept and false before it: the very next
        /// paragraph of this doc establishes that <c>CardsHandUI.PerformShortRest</c> REMOVES
        /// NOTHING and only <c>FinalizeShortRest</c> moves it, so until the owner accepts, the card
        /// can still be re-drawn and nothing has left anything — the file's own evidence falsified
        /// its own conclusion one paragraph later. What actually makes a burning card public is a
        /// property of the CARD, and <see cref="IsPubliclyRevealedCard"/> owns it: on accept the
        /// card lands in <c>LostAbilityCards</c> BEFORE the burn artwork starts, that predicate
        /// turns true, and the front opens for the whole burn — in a short rest as anywhere else.
        /// Item 15's picture is delivered by the burn exception, one accept later.</para>
        ///
        /// <para>THE SUPERSEDED ARGUMENT, KEPT VERBATIM SO THE NEXT READER CAN SEE WHAT IT PROVED
        /// AND WHAT IT DID NOT: "A short rest always runs inside
        /// <c>SelectAbilityCardsOrLongRest</c> — that phase is what the game OFFERS the rest in —
        /// so <see cref="IsSecretSelectionPhase"/> is true for its whole duration and this card
        /// could never be shown while it was a term. But the secret that phase protects is a
        /// DECISION IN FLIGHT: which two cards a player is about to commit. The sacrifice is the
        /// opposite of a decision in flight — it is a card LEAVING the player's resources, drawn
        /// from their DISCARD pile by the game's own RNG, and the whole point of laying it face-up
        /// in a recess is that everyone watches it burn." The first sentence is a measurement and
        /// stands; the rest is the inference the user overruled.</para>
        ///
        /// <para>THIS ONE IS NOT SUFFICIENT ON ITS OWN — the identity has to travel too, and it now
        /// does: extension record 39 (<c>NetProtocol.ExtIdSacrificeSeat</c>) names WHICH recess
        /// holds the sacrifice and WHERE that card sits in its owner's discard arc, and
        /// <c>RemoteControlBoard.TryResolveSacrifice</c> asks THIS member before drawing the front.
        /// Both halves shipped in the same build, so this member has never been a permission
        /// without a picture behind it. THE RECORD IS UNCHANGED BY ITEM 6 and is still the only
        /// thing that can name the card: it is what lets the burn exception resolve a FACE for the
        /// sacrifice the instant the accept commits it, and what lets the recess draw an
        /// identity-known back rather than an anonymous one before that.</para>
        ///
        /// <para>WHAT THIS PARAGRAPH USED TO SAY, AND WHY THE CORRECTION IS RECORDED RATHER THAN
        /// OVERWRITTEN. It said the card was unnameable — that the peer's pile counts going
        /// <c>d/b/i=8/2/2</c> to <c>7/2/2</c> proved the card had left the DISCARD list without
        /// entering the BURNT one, and so lay in NEITHER replicated list. Two lanes and the
        /// integrator reasoned that way and it is FALSE. <c>CardsHandUI.PerformShortRest</c> picks
        /// the sacrifice as <c>DiscardedAbilityCards[ScenarioRNG.Next(...)]</c> and REMOVES NOTHING;
        /// only <c>FinalizeShortRest</c>, on accept, moves it. The <c>8 -> 7</c> was OUR OWN
        /// instrument subtracting the very card the question was about: extension record 15 carries
        /// the RENDERED stack label, <c>DiscardedCount - PendingPileArrivals</c>
        /// (<c>Cards.Piles.PileViewer.TickStatus</c>), and our own
        /// <c>CardsDriver.PresentShortRestCard</c> flies the sacrifice out of the stack, which makes
        /// it a pending arrival. The co-player's own log states both halves in one line: "Piles:
        /// discard=7, burnt=2 ... DEFERRED: the model already lists discard=8, burnt=2, but 1
        /// discard ... still ON THEIR WAY". The true blocker was never the model — it was that
        /// <c>RemoteControlBoard.OrderRoundCards</c> reads only <c>RoundAbilityCards</c>, and
        /// nobody checked the pile the card had never left.</para>
        /// </summary>
        SacrificedCard,

        /// <summary>The WORDING of a peer's mirrored decision row — the pressable-widget labels
        /// extension record 12 carries, which for a burn prompt read <c>Verbrennen "In die
        /// Nacht"</c> and therefore DO contain a card name.
        ///
        /// <para>IT IS AN EXEMPTION FOR THE ROW AND A MASK FOR THE NAME, AND IT IS NAMED HERE
        /// BECAUSE AN EXEMPTION NOBODY CAN FIND IS INDISTINGUISHABLE FROM A LEAK. Record 12's own send log asserted
        /// "pressable-widget labels only, NO card identity" and that claim was false — the assertion
        /// was about what the sampler SELECTS (only labels of pressable widgets) and was read as a
        /// statement about what those labels CONTAIN. Both wordings are corrected at their sites.
        /// </para>
        ///
        /// <para>THE MEMBER STAYS EXEMPT AND THE WORDING NO LONGER CARRIES AN IDENTITY — those are
        /// two different statements and the distinction is the whole of the 2026-09-07 follow-up.
        /// The ROW may still be mirrored inside the secret window (that is what the exemption is
        /// for: refusing it would blank a mirrored decision mid-prompt, which is an empty window and
        /// a standing prohibition). What may no longer ride is the CARD NAME inside it.</para>
        ///
        /// <para>USER, VERBATIM: "wegen dem Anti-Cheat-System in der Auswahlphase muss hier ein
        /// genehmigte Ausnahme der 1:1 Regel greifen, der Name der Karte in dem Dialog im remote
        /// board muss ausgeblendet werden. Nutz eine immersive Art das ausblenden und bleib trotzdem
        /// so nah wie möglich am Dialog den der Spieler auch sieht."</para>
        ///
        /// <para>THIS IS THE FIRST APPROVED, NAMED EXCEPTION TO THE 1:1 RULE, and it is recorded as
        /// exactly that so a future round does not "restore 1:1" and re-open the leak: the mirrored
        /// dialog is deliberately NOT what the owner sees, because the covering rule here is an
        /// ANTI-CHEAT boundary and not a presentation preference. <c>Net.DecisionLabelMask</c> owns
        /// the mechanism and the craft (why the mask is applied at the SENDER, why it is plain
        /// letters rather than a sprite, and why it is in the sender's language).</para>
        ///
        /// <para>THE SUPERSEDED RULING, KEPT so the reversal is legible. It had three grounds:
        /// "(1) What actually travels is the name of a card being BURNT or LOST — the same content
        /// <see cref="SacrificedCard"/> covers, and the user has now explicitly asked to be able to
        /// see it. (2) Refusing it would blank a mirrored decision row in the middle of a prompt …
        /// (3) Card identity is not a durable secret in this game: vanilla lets any player open any
        /// other player's complete card overview from the initiative track outside the selection
        /// window." Ground (2) still stands and is why the member is still exempt. Ground (1) fell
        /// with <see cref="SacrificedCard"/>'s carve-out — he no longer wants to see it INSIDE the
        /// window — and ground (3) was always an argument about OUTSIDE the window, where nothing is
        /// masked and this member changes nothing.</para>
        ///
        /// <para>WHAT IS OWED IS STILL A MEASUREMENT, not a promise, and it is now two lines: the
        /// mask's own <c>DECISION LABEL MASK</c> (what was replaced, with both lengths) and the
        /// sender's <c>DECISION LABEL INSIDE THE SECRET WINDOW</c> (the whole of what actually went
        /// out). The reading INVERTED with this change — a card name quoted in the second line used
        /// to be the expected exemption and is now a DEFECT, meaning the mask missed a list.</para>
        ///
        /// <para>IT IS NOT THE ONLY RECORD THAT CARRIED THIS CONTENT. Extension record 13's cap
        /// labels (bits 0 and 2) fall through to the same <c>DialogPopup</c> option wording during a
        /// pick flow and had no gate at all; they are masked at the same seam. If a THIRD surface is
        /// ever found spelling a covered card's name in prose, mask it there — the wire is the
        /// boundary — and add it to this paragraph.</para>
        /// </summary>
        DecisionRowWording,

        /// <summary>
        /// A peer's fan while the game has them stepping through a MODAL CARD PICK — the arc is
        /// their DISCARD or LOST pile rather than their hand, because the game re-Showed the hand
        /// over that pile (<c>CardHandMode.LoseCard</c> / <c>DiscardCard</c> /
        /// <c>RecoverDiscardedCard</c> / <c>RecoverLostCard</c> / <c>IncreaseCardLimit</c>).
        ///
        /// <para>USER, VERBATIM (2026-09-06, item 7): "Bei einer langen Rast werden bei dem
        /// betroffenen Spieler die Handkarten zu den abgeworfenen Karten. Diese sollen auch mit der
        /// Vorderseite sichtbar sein fuer alle Spieler. Aktuell ist der Faecher als auch die Karte
        /// in der Hand des Spielers wieder nur die Rueckseite obwohl es sich hier nicht um die
        /// Auswahlphase handelt. Auch wenn hier eine Auswahl getroffen wird handelt es sich NICHT
        /// um die Auswahlphase sondern die Lange Rast ist eine Aktion und das auswaehlen einer
        /// Karte nur Teil dieser Aktion."</para>
        ///
        /// <para>IT IS NOT AN EXEMPTION, AND SAYING SO IS THE POINT OF THE MEMBER.
        /// <see cref="IsPublicPopulation"/> answers FALSE for it, exactly as for
        /// <see cref="Selectable"/>: a pick fan gets the same phase term every other secret
        /// population gets. Nobody has ruled that a peer's discard pile is public during the
        /// selection window (the <see cref="AlreadyPublic"/> member says so in as many words), and
        /// this file's standing invariant is to show LESS where nobody has ruled. What the member
        /// exists for is to make the CALL SITE and the hardware log say WHICH population a surface
        /// drew, which is the same reason the three exemptions above are members rather than
        /// booleans.</para>
        ///
        /// <para>THE PHASE WAS NEVER THE BLOCKER, AND THE EVIDENCE SAYS SO IN ONE LINE. The obvious
        /// reading of item 7 is that something classified the long rest as the selection phase. It
        /// did not: on the observing host, through the whole of the co-player's long rest, the
        /// census stands at <c>round slots[p2] ... RevealGate.ShowRoundCardFronts(actor)=true</c>
        /// while the hand fan beside it reads <c>0 FRONT / 2 BACK - LENGTH BELT: 6 model card(s) vs
        /// 2 slab(s) on the wire</c>. The gate was OPEN; the ARITHMETIC was shut, because the
        /// observer resolved a DISCARD arc as a HAND (<c>CardsGameApi.HandFanMember</c>) and got a
        /// list of the wrong length. Extension record 43 is what tells it which list to walk; this
        /// member is what names the population it walked.</para>
        ///
        /// <para>AND THE ARITHMETIC HAD A SECOND CAUSE THAT RECORD 43 CANNOT REACH — 2026-09-06
        /// report item 4, one round later. Naming the right LIST does not make the two copies of it
        /// the same LENGTH: the owner's arc drops a card they are holding up or have laid in a round
        /// recess, and the observer's walk of the same list does not, because the card has not been
        /// played. The host census then read <c>hand fan[p2] 0 FRONT / 7 BACK - LENGTH BELT: 8 model
        /// card(s) vs 7 slab(s) on the wire, with 0 in their fist and 1 hand card(s) lying in their
        /// recesses - remainder 0</c> for eleven consecutive ticks with the gate open: the belt's own
        /// remainder said the difference was fully explained and it refused the fronts anyway,
        /// because a COUNT can say THAT two lists differ and never WHICH card differs. The fix is
        /// extension record 44 carrying an INJECTION rather than a permutation — the owner stating
        /// which members of the derived list their arc holds. Neither of these two causes is a
        /// secrecy term and neither is this member; that is still the point of it.</para>
        ///
        /// <para>THE FLOWS THAT PICK A CARD OUTSIDE THE SELECTION PHASE, ENUMERATED, with the face
        /// each one must show. Every row is a flow in which the game asks its owner to choose a
        /// card while <see cref="IsSecretSelectionPhase"/> may be either open or shut, and the
        /// column that matters is that NONE of them is the two-card commit the secret protects:</para>
        /// <list type="bullet">
        /// <item><description>LONG REST, burn step (<c>CardHandMode.LoseCard</c> over the DISCARD
        /// pile; the owner's own log line is <c>Pick fan source (LoseCard): discard pile</c>). The
        /// user's ruling is explicit and it is this member's reason for existing: FRONTS. Resolved
        /// through record 43 as <see cref="NetProtocol.HeldFaceListDiscard"/>.</description></item>
        /// <item><description>AVOID DAMAGE by burning / losing a card (<c>LoseCard</c> over the
        /// REAL HAND — <c>Pick fan source (LoseCard): real hand</c> in both logs of the 457
        /// session). FRONTS whenever the phase allows, i.e. the ordinary hand-fan rule, and NO
        /// record 43 is written: the fan already is the hand.</description></item>
        /// <item><description>CARD-LIMIT discard (<c>DiscardCard</c> / <c>IncreaseCardLimit</c>) —
        /// the hand again, same rule, no record.</description></item>
        /// <item><description>RECOVER a discarded card (<c>RecoverDiscardedCard</c>) — the DISCARD
        /// pile, record 43 as <see cref="NetProtocol.HeldFaceListDiscard"/>.</description></item>
        /// <item><description>RECOVER a lost card (<c>RecoverLostCard</c>) — the LOST pile, record
        /// 43 as <see cref="NetProtocol.HeldFaceListBurnt"/>.</description></item>
        /// <item><description>SHORT REST sacrifice — already carved out, and by a different
        /// mechanism: the card lies in a round RECESS rather than in a fan, it is named by record
        /// 39, and <see cref="SacrificedCard"/> is its population. It is the ONE of these flows that
        /// is genuinely exempt from the phase, because it runs INSIDE
        /// <c>SelectAbilityCardsOrLongRest</c> and would otherwise never be visible at all.
        /// </description></item>
        /// <item><description>ITEM USE / item surrender — items are not ability cards and never
        /// carried the selection-phase secret; they are drawn by <c>RemoteItemFan</c> through
        /// <see cref="ShowRoundCardFronts"/> and are untouched by this member.</description></item>
        /// </list>
        ///
        /// <para>WHY THE LIST IS HERE AND NOT AT A CALL SITE. Item 7 is the third round in which a
        /// choosing flow outside the two-card commit drew backs, and each time the fix named the
        /// flow that was reported. A rule written per flow is a rule the NEXT flow is not covered
        /// by; the rows above are the whole population, and the two that need a pile named on the
        /// wire are exactly the two record 43 can say.</para>
        /// </summary>
        PickFan,

        /// <summary>
        /// A card a peer has LAID INTO A ROUND RECESS as one step of a modal card PICK — the long
        /// rest's burn card is the one the user reported, and <c>RecoverDiscardedCard</c> /
        /// <c>RecoverLostCard</c> lay a card down the same way.
        ///
        /// <para>USER, VERBATIM (2026-09-06, item 7): "Weiterhin, wenn die Karte die man in der
        /// langen Rast abwerfen will dann auf das Board legt, sehen alle anderen Spieler hier wieder
        /// nur die Rueckseite. Wie gesagt - das ist NICHT als Auswahlphase zu klassifizieren, daher
        /// soll jeder die Karte voll mit der Vorderseite sehen."</para>
        ///
        /// <para>IT IS NO LONGER EXEMPT, and it lost the exemption with
        /// <see cref="SacrificedCard"/> and for the same reason — which is why it still sits beside
        /// it rather than inside <see cref="PickFan"/>. THE EXEMPTION COST NOTHING TO GIVE UP, and
        /// that is a measurement rather than a hope: every flow this member covers except the short
        /// rest RESOLVES IN THE ACTION PHASE, where <see cref="ShowRoundCardFronts"/> is open on its
        /// own and this member's answer was never load-bearing. The long rest is committed during
        /// selection and resolves as an action — the ModBuild 470 host log's <c>[Cards] REST
        /// GATE</c> line prints a live long-rest flag in phase <c>MonsterClassesSelectAbilityCards</c>
        /// and again in <c>ActionSelection</c>, never in the secret window — and
        /// <c>RecoverDiscardedCard</c> / <c>RecoverLostCard</c> are turn actions. The ONE flow this
        /// member covered INSIDE the secret window was the short-rest sacrifice, which is exactly
        /// the picture the user ruled against.</para>
        ///
        /// <para>THE SUPERSEDED ARGUMENT, KEPT: "The secret <c>SelectAbilityCardsOrLongRest</c>
        /// protects is a DECISION IN FLIGHT: which two cards a player is about to COMMIT. A card
        /// laid in a recess by a modal pick is the opposite — it is a card being LOST or RECOVERED,
        /// drawn out of a pile everyone can already browse, and laying it down is the announcement
        /// of that loss." Sound for a card that has been lost; the short rest's had not been yet.
        /// A card genuinely already lost is answered by <see cref="IsPubliclyRevealedCard"/>, which
        /// is a property of the card and reaches this recess too.</para>
        ///
        /// <para>THE BOUNDARY THAT KEEPS IT FROM BEING A LEAK, and it is enforced in three
        /// independent places rather than asserted here. (1) The mod only ever lays a card in a
        /// recess this way from <c>CardsDriver.HandlePickRelease</c>, which runs under
        /// <c>IsPickMode</c> — <c>LoseCard</c>/<c>DiscardCard</c>/<c>RecoverDiscardedCard</c>/
        /// <c>RecoverLostCard</c>/<c>IncreaseCardLimit</c>, and NEVER the ordinary two-card commit,
        /// which seats its cards through <c>PlayTray.PlaceCard</c> and is named by
        /// <c>CCharacterClass.RoundAbilityCards</c> instead. (2) The accessor the sender reads
        /// (<c>CardsDriver.PickFieldSeat</c>, called from <c>LocalRigSampler.SampleSacrificeSeats</c>
        /// — the name <c>SampleRecessCardSeats</c> that stood here has never existed) re-asks
        /// <c>IsPickMode</c> itself, and additionally requires the card to be PHYSICALLY seated in
        /// that recess this frame. (3) The seat that travels may name ONLY the DISCARD or the BURNT
        /// arc — never the
        /// HAND — so a card of the two-card commit is not expressible in the record at all. A
        /// hand-pile pick (avoid damage by burning a card from the hand, the card-limit discard)
        /// therefore keeps its anonymous back, deliberately: that card IS a hand card and its
        /// identity IS the secret.</para>
        /// </summary>
        BoardPickSeat,
    }

    /// <summary>
    /// Is <paramref name="population"/> exempt from the selection-phase carve-out? ONE expression,
    /// so a named member cannot answer it two ways — the members exist to make a call site and a
    /// hardware log say WHICH population is being drawn (and, for the exempt ones, which exemption
    /// is being claimed), never to compute a different answer.
    ///
    /// <para>THREE MEMBERS ARE EXEMPT, EACH BY AN EXPLICIT RULING, AND NONE OF THEM IS A PLACE:
    /// <see cref="PeerCardPopulation.AlreadyPublic"/> (a card that was played face-up in an earlier
    /// round), <see cref="PeerCardPopulation.DecisionRowWording"/> (the TEXT of a mirrored burn
    /// prompt, which is not a face at all) and <see cref="PeerCardPopulation.ItemCard"/> (an item,
    /// which is not an ability card and can never be part of the two-card commit). Every other
    /// population — the hand, the held ability card, the round slots, the ability-card pile arcs, a
    /// pick fan, a short-rest sacrifice and a card laid in a recess by a modal pick — gets the phase
    /// term.</para>
    ///
    /// <para>THE DISCARD AND BURNT FANS ARE NOT ON THIS LIST, AND THAT IS THE POINT OF THE 2026-09-07
    /// EVENING FIX RATHER THAN AN OMISSION FROM IT. The user's ruling covers them — "Die Fächer der
    /// piles werden also ab jetzt immer mit Vorderseiten gezeigt ohne Ausnahme" — but a fan is a
    /// PLACE, and the same message reported the falsifier for implementing it as one: "sobald ich
    /// eine Karte aus dem Fächer … nehme, sehen die anderen Spieler bei der genommenen Karte nur die
    /// Rückseite". A card leaving the fan for a fist leaves the place and keeps the ruling, so the
    /// ruling has to live on the CARD. It does: <see cref="IsPubliclyRevealedCard"/> already answers
    /// the burnt lists and <see cref="IsDiscardedCard"/> now answers the discard pile, and both are
    /// asked by the card-aware <see cref="CardFaces(PeerCardPopulation, CPlayerActor, int)"/>
    /// overload that every surface drawing a nameable card already routes through.</para>
    ///
    /// <para>THE SHORT REST IS THE REASON THIS EXPRESSION SHRANK, and the ruling is the user's,
    /// stated for the THIRD time (2026-09-07, item 6): "Kurze Rast habe ich die Vorderseite der
    /// Karte gesehen, das ist die Auswahlphase und darf daher nicht sein. Nochmal: Kurze Rast =
    /// Auswahlphase = verdeckt, Lange Rast = Aktionsphase = alles offen." He arrived at it himself
    /// on the game's logic once this project measured the short rest running inside
    /// <c>SelectAbilityCardsOrLongRest</c>: "während einer kurzen Rast ist man de facto noch in der
    /// Auswahlphase und bleibt daher verdeckt".</para>
    ///
    /// <para>IT SUPERSEDES 2026-09-05 ITEM 15, AND THE TWO RULINGS DO NOT ACTUALLY CONFLICT —
    /// THEY ARE SEPARATED IN TIME BY <c>FinalizeShortRest</c>. Item 15 asked to see WHICH card is
    /// lying there; <see cref="PeerCardPopulation.SacrificedCard"/>'s own doc records the game
    /// timing that settles it: <c>CardsHandUI.PerformShortRest</c> indexes
    /// <c>DiscardedAbilityCards</c> and REMOVES NOTHING, and only <c>FinalizeShortRest</c>, on
    /// ACCEPT, moves the card. So while the sacrifice merely LIES in the recess it is a decision
    /// still in flight — its owner may re-draw it — and it is covered, which is item 6. The instant
    /// they accept, the card lands in <c>LostAbilityCards</c> BEFORE the burn artwork starts
    /// (<c>CardEffects.BurnCardTimeline</c>), <see cref="IsPubliclyRevealedCard"/> turns true for
    /// it, and the burn exception opens the front for the whole of the burn — which is item 15's
    /// picture and the "EGAL AUS WELCHEM GRUND" ruling in one. Nothing was traded away; the front
    /// arrives one accept later than it used to.</para>
    ///
    /// <para>THE PHASE TERM IS ALREADY THE SHORT-REST/LONG-REST DISCRIMINATOR, WHICH IS WHY NO NEW
    /// TERM WAS ADDED. Both rests are OFFERED in <c>SelectAbilityCardsOrLongRest</c>, so the state
    /// name cannot tell them apart — but only the SHORT one RUNS there. A long rest is committed
    /// during selection (<c>CCharacterClass.LongRest</c>) and RESOLVES as an action: the ModBuild
    /// 470 host log carries both readings, <c>[Cards] REST GATE ... in phase
    /// MonsterClassesSelectAbilityCards ... long=True</c> and the same line again <c>in phase
    /// ActionSelection</c>, i.e. at both moments a long rest was live the phase was NOT the secret
    /// window. These two carve-outs were the ONLY thing that made a short rest behave like the
    /// action phase; removing them lets the phase term be the discriminator it always was. If a
    /// future build ever finds a long-rest pick evaluating inside the secret window, the term to
    /// add is <c>CCharacterClass.LongRest</c> — host-replicated, readable off the same
    /// <c>actor.CharacterClass</c> this file already walks, and needing no wire field.</para>
    ///
    /// <para><see cref="PeerCardPopulation.PickFan"/> IS NOT EXEMPT, and it is listed on the
    /// non-exempt side explicitly rather than by falling off the end of the expression: it is a
    /// member added for a defect that turned out NOT to be a secrecy defect at all, and the next
    /// reader has to be able to see that it grants nothing. See its own doc for the reading that
    /// settled it.</para>
    ///
    /// <para>A MEMBER ADDED TO THE EXEMPT SIDE OF THIS EXPRESSION OVERRIDES A RULING THE USER HAS
    /// NOW STATED THREE TIMES, SO THE TEST FOR ADDING ONE IS WRITTEN OUT HERE RATHER THAN LEFT AS A
    /// PROHIBITION. The prohibition used to read "do not add one for a PLACE" and it was the right
    /// instinct with the wrong scope — the 2026-09-07 evening ruling made two more populations
    /// public and only one of them could be expressed here at all. The test that separates them:
    /// </para>
    /// <list type="number">
    /// <item><description>IS THE THING PUBLIC BECAUSE OF WHAT IT IS, or because of WHERE IT IS
    /// LYING? A KIND may be exempt here (<see cref="PeerCardPopulation.ItemCard"/>: an item is never
    /// part of the two-card commit, wherever it sits). A PLACE may not — the recess and the pick
    /// field are places, they are where a card lies while the decision about it is STILL IN FLIGHT,
    /// and a place rule is what showed him a front in a short rest. <see cref="PeerCardPopulation
    /// .SacrificedCard"/> and <see cref="PeerCardPopulation.BoardPickSeat"/> are therefore still on
    /// the NON-exempt side and must stay there: until <c>FinalizeShortRest</c> runs, the owner may
    /// still re-draw the card in that recess.</description></item>
    /// <item><description>CAN THE THING LEAVE THE SURFACE? If yes, the rule does not belong here at
    /// all, because a population is evaluated per SURFACE and cannot follow a card off it. The pile
    /// fans are the worked example: a discarded card in a fan and the same card in its owner's fist
    /// are the same public card on two surfaces, so the answer lives in
    /// <see cref="IsPubliclyRevealedCard"/> / <see cref="IsDiscardedCard"/>, which every surface
    /// already asks and which follow the card wherever it goes.</description></item>
    /// </list>
    ///
    /// <para>WHY THE RECESS AND THE PICK FIELD ARE STILL NOT EXEMPT EVEN THOUGH THE DISCARD PILE
    /// NOW IS, which is the one question this expression will be re-litigated over. The short-rest
    /// sacrifice IS a discard-pile card — <c>CardsHandUI.PerformShortRest</c> indexes
    /// <c>DiscardedAbilityCards</c> and REMOVES NOTHING — so "in the discard pile" on its own would
    /// hand that recess a front and reverse the ruling the user has stated three times. It does not,
    /// because <see cref="PileFrontsReach"/> refuses the discard exemption to exactly those two
    /// populations. The pile-fan ruling is about a pile the player is BROWSING; the recess is about
    /// a card the game has singled out of it and is waiting for an answer on.</para>
    /// </summary>
    public static bool IsPublicPopulation(PeerCardPopulation population) =>
        population == PeerCardPopulation.AlreadyPublic
        || population == PeerCardPopulation.DecisionRowWording
        || population == PeerCardPopulation.ItemCard;

    /// <summary>
    /// Does the PILE-FAN ruling reach <paramref name="population"/>? The one term that keeps
    /// "a discarded card's face is public" (user, 2026-09-07 evening item 3) from colliding with
    /// "a short-rest sacrifice is covered" (user, 2026-09-07 afternoon item 6) — because the
    /// sacrifice is a discard-pile card and the two rulings would otherwise contradict each other on
    /// the same card in the same tick.
    ///
    /// <para>IT IS A REFUSAL LIST AND NOT AN ALLOW LIST, DELIBERATELY. The ruling is "ohne Ausnahme"
    /// and the two exceptions are the two places a card lies while a decision about it is still in
    /// flight; writing it as an allow list would mean a surface added later is silently NOT covered
    /// by a ruling that says it should be, which is the failure direction the user has reported
    /// three rounds running. Written this way, a new population inherits the ruling and only a
    /// deliberate addition here can take it away.</para>
    ///
    /// <para>NOTE WHAT IT DOES NOT GUARD: <see cref="IsPubliclyRevealedCard"/> (burnt / active) is
    /// NOT routed through this term and must not be — that exception is the "EGAL AUS WELCHEM
    /// GRUND" ruling and it reaches the recess ON PURPOSE, which is how a card burning inside a
    /// short rest shows its front the instant the accept commits it.</para>
    /// </summary>
    private static bool PileFrontsReach(PeerCardPopulation population) =>
        population != PeerCardPopulation.SacrificedCard
        && population != PeerCardPopulation.BoardPickSeat;

    /// <summary>
    /// IS THIS PARTICULAR CARD'S FACE ALREADY PUBLIC, whatever phase the game is in and whatever
    /// surface is about to draw it? Two lists answer yes: <paramref name="actor"/>'s ACTIVE /
    /// persistent pile (<c>CCharacterClass.ActivatedCards</c>) and their BURNT piles
    /// (<c>LostAbilityCards</c> + <c>PermanentlyLostAbilityCards</c>).
    ///
    /// <para>USER, VERBATIM (2026-09-06, item 9): "Die aktiven Karten sind immer sichtbar (was du
    /// schon gemacht hast) d.h. aber auch, dass wenn ein Spieler eine aktive Karte in die Hand
    /// nimmt, soll diese auch mit der Vorderseite AUCH in der Auswahlphase sichtbar sein. (Aktuell
    /// sieht man nur die Rueckseite beim remote Spieler)."</para>
    ///
    /// <para>IT IS A PROPERTY OF THE CARD, NOT OF THE SURFACE, and that is the whole reason it is
    /// a second predicate rather than a sixth <see cref="PeerCardPopulation"/> member. The active
    /// matrix's exemption (<see cref="PeerCardPopulation.AlreadyPublic"/>) is a statement about a
    /// PLACE — the small grid beside the control board — and the user's item 9 is the observation
    /// that the place was never what made the card public: the card was played FACE-UP in front of
    /// everybody, and picking it up again does not un-play it. A place rule cannot follow the card
    /// into a fist; this one does, and any future surface that draws somebody else's card gets the
    /// exemption by asking, without a new member and without a new ruling.</para>
    ///
    /// <para>IT READS <c>ActivatedCards</c> AND NOT <c>ActivatedAbilityCards</c> ON PURPOSE. The
    /// latter is a LINQ projection that allocates a fresh <c>List&lt;CAbilityCard&gt;</c> on every
    /// single call (<c>CCharacterClass.cs:99</c>); this predicate is asked from per-frame draw
    /// paths, so it walks the raw backing list and type-tests each entry instead. Same membership,
    /// zero allocation.</para>
    ///
    /// <para>THE BURNT LISTS WERE ADDED ON THE USER'S OWN RULING (2026-09-06, report item 6), and
    /// he stated it in the strongest terms he has used for any face: "Wenn ein Mitspieler eine Karte
    /// verbrennt sehe ich wieder nur die Rueckseite auf dem Board. Beim Verbrennen EGAL AUS WELCHEM
    /// GRUND muss die Karte immer mit der Vorderseite sichtbar sein. Es gibt keinen Grund warum sie
    /// nicht sichtbar sein sollte." There is no phase, no pile and no anti-cheat argument that
    /// outranks it, and the game agrees with him: a burn is COMMITTED into
    /// <c>LostAbilityCards</c> before the burn artwork starts (<c>CardEffects.BurnCardTimeline</c>,
    /// which is why the owner's own <c>CardsDriver.TickBurnToPile</c> has to hold the card on the
    /// board at all), that list is host-replicated to every client, and the card never comes back
    /// from it by any ordinary route. It is the SACRIFICE argument one list over: a card LEAVING a
    /// player's resources, permanently, in front of everybody — the opposite of the decision-in-
    /// flight the selection window protects. Knowing which card a peer just destroyed says nothing
    /// about the two cards they are about to commit.</para>
    ///
    /// <para>WHY HERE AND NOT AS A SIXTH <see cref="PeerCardPopulation"/>. Item 6 names three
    /// surfaces at once — the card charring in the recess, the slab <c>Net.RemoteBurnFx</c> flies,
    /// and the <c>Slot -&gt; Burnt</c> flight <c>Net.RemoteCardFx</c> draws when that mirror could
    /// not present one — and a burn can also be watched from the burnt-pile fan. A PLACE rule would
    /// have to be repeated on each, and the fourth surface added later would not have it. This is a
    /// property of the CARD, so every surface that can name the card it is drawing gets the ruling
    /// by asking, which is the same argument item 9's active-card-in-a-fist exemption is written
    /// under two paragraphs above.</para>
    ///
    /// <para>Degrades to FALSE — the answer that shows LESS — for a null actor, a missing character
    /// class, an unknown instance id, and any throw. That is this file's standing invariant and this
    /// predicate is on the OPEN side of every expression that uses it, so a "safely false" here
    /// really does close the gate rather than open it.</para>
    /// </summary>
    public static bool IsPubliclyRevealedCard(CPlayerActor? actor, int cardInstanceId)
    {
        if (actor == null || cardInstanceId == int.MinValue)
            return false;
        try
        {
            CCharacterClass? cc = actor.CharacterClass;
            if (cc == null)
                return false;
            System.Collections.Generic.List<CBaseCard>? active = cc.ActivatedCards;
            if (active != null)
            {
                for (int i = 0; i < active.Count; i++)
                {
                    if (active[i] is CAbilityCard ability && ability != null
                        && ability.CardInstanceID == cardInstanceId)
                        return true;
                }
            }
            // The two BURNT lists, walked raw for the same allocation reason as ActivatedCards
            // above. PermanentlyLostAbilityCards is included because the user's ruling says "EGAL
            // AUS WELCHEM GRUND": a card lost to a scenario effect is as burned, and as public, as
            // one its owner chose to burn.
            return HoldsAbility(cc.LostAbilityCards, cardInstanceId)
                   || HoldsAbility(cc.PermanentlyLostAbilityCards, cardInstanceId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// IS THIS CARD LYING IN ITS OWNER'S DISCARD PILE? The pile-fan half of the 2026-09-07 evening
    /// ruling, and — like <see cref="IsPubliclyRevealedCard"/> beside it — a property of the CARD
    /// rather than of the surface drawing it.
    ///
    /// <para>USER, VERBATIM (item 3): "Wir haben beim Refactoring vereinbart, dass es keinen Sinn
    /// macht die Fächer der Piles (Abgeworfen, Verbrannt und Items) jemals (auch in der
    /// Auswahlphase) mit der Rückseite anzuzeigen. Aktuell ist nur bei den verbrannten Karten eine
    /// Vorderseite zu sehen …, die Fächer der abgeworfenen Karten und der Items zeigen nach wie vor
    /// Rückseiten. Die Fächer der piles werden also ab jetzt immer mit Vorderseiten gezeigt ohne
    /// Ausnahme." He had already given the discard/burnt half on the afternoon of the same day
    /// ("Auch offen - dann gilt das aber auch für den Verbrannt-Fächer"); that ruling was recorded
    /// in the ModBuild 478 build note and never implemented, which is why he is restating it.</para>
    ///
    /// <para>WHY IT IS A SEPARATE PREDICATE AND NOT A FOURTH LIST INSIDE
    /// <see cref="IsPubliclyRevealedCard"/>, WHICH WOULD HAVE BEEN ONE LINE. Two reasons, and both
    /// are load-bearing:</para>
    /// <list type="number">
    /// <item><description>IT WOULD HAVE UNDONE THE 478 LEAK FIX.
    /// <see cref="PeersMayNameOurCard"/> reads <see cref="IsPubliclyRevealedCard"/>, and
    /// <c>DecisionLabelMask.AddCovered</c> SKIPS every card that predicate permits. Folding the
    /// discard pile in would have stopped the mask masking the short-rest sacrifice's name —
    /// ModBuild 477 item 7, the one live card-identity leak this mod has had. A face and a NAME are
    /// two questions here; see <see cref="PeersMayNameOurCard"/>.</description></item>
    /// <item><description>IT WOULD HAVE OPENED THE SHORT-REST RECESS. The sacrifice IS a
    /// discard-pile card until <c>FinalizeShortRest</c> moves it, so an unscoped discard exemption
    /// draws its front inside the secret window — the exact picture the user ruled against three
    /// times ("Kurze Rast = Auswahlphase = verdeckt"). <see cref="PileFrontsReach"/> is the term
    /// that keeps the two rulings from colliding, and it can only be applied to a predicate that is
    /// separately nameable.</description></item>
    /// </list>
    ///
    /// <para>WHY A DISCARDED CARD IS PUBLIC AT ALL, since this file may not take a ruling on trust
    /// alone: it is the <see cref="IsPubliclyRevealedCard"/> argument one list over. A card reaches
    /// <c>DiscardedAbilityCards</c> by having been PLAYED, face-up, in front of the whole table, and
    /// the secret the selection window protects is which two cards a player is ABOUT to commit —
    /// which the pile they have already spent says nothing about. Vanilla agrees twice over: the
    /// initiative-track overview shows any player's whole card state outside the window, and the
    /// game's own pile viewers carry no phase term.</para>
    ///
    /// <para>Degrades to FALSE — the answer that shows LESS — on a null actor, a missing character
    /// class, an unknown instance id and any throw, exactly as its sister predicate does, and it is
    /// on the OPEN side of every expression that reads it.</para>
    /// </summary>
    public static bool IsDiscardedCard(CPlayerActor? actor, int cardInstanceId)
    {
        if (actor == null || cardInstanceId == int.MinValue)
            return false;
        try
        {
            CCharacterClass? cc = actor.CharacterClass;
            // The raw backing list, for the same allocation reason IsPubliclyRevealedCard walks
            // ActivatedCards rather than ActivatedAbilityCards: this is asked from per-frame draw
            // paths.
            return cc != null && HoldsAbility(cc.DiscardedAbilityCards, cardInstanceId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Membership of <paramref name="cardInstanceId"/> in one raw ability-card list, with
    /// no LINQ projection and no allocation — the predicate above is asked from per-frame draw
    /// paths.</summary>
    private static bool HoldsAbility(System.Collections.Generic.List<CAbilityCard>? list,
                                     int cardInstanceId)
    {
        if (list == null)
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].CardInstanceID == cardInstanceId)
                return true;
        }
        return false;
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
        => CardFaces(population, actor, InScenario);

    /// <summary>
    /// THE SAME CALL WITH THE CAPABILITY HALF SUPPLIED BY THE CALLER — for the ONE surface whose
    /// own lifetime already answered it, and which must not be made to answer it twice.
    ///
    /// <para><paramref name="scenarioEstablished"/> IS THE <see cref="InScenario"/> TERM AND NOTHING
    /// ELSE. This method's own doc block is written about what happens when a CAPABILITY test is
    /// left standing as the answer to a SECRECY question; the remedy is not to hide the capability
    /// term inside the predicate for every caller, it is to let a caller that has ALREADY
    /// established the capability say so, so that the secrecy half is the only thing left to get
    /// wrong. <c>Net.Remote.RemoteControlBoard</c> is that caller: its whole lifetime is gated on
    /// <c>RemoteBoardScenarioGate</c>, whose own doc says in as many words that it is "deliberately
    /// NOT RevealGate.InScenario". In the window where the two disagree (the board is up, the
    /// save's <c>CurrentGameState</c> has not said Scenario yet) <see cref="ShowRoundCardFronts"/>
    /// answers TRUE by negation and that board's recesses draw FRONTS — and passing
    /// <c>InScenario</c> here instead would trade them into the one thing the user prohibited
    /// outright ("außerhalb der Auswahlphase NIEMALS Rückseiten").</para>
    ///
    /// <para>IT MAY ONLY EVER BE PASSED <c>true</c> BY A SURFACE WHOSE OWN GATE IS AT LEAST AS
    /// STRICT AS THE PICTURE IT DRAWS. It is not a permission to show fronts — the secrecy terms
    /// below are untouched by it — it is a statement that "a face can be resolved here" has already
    /// been decided somewhere else. Everything that has NOT decided it calls the two-argument
    /// overload above and gets <see cref="InScenario"/>, which is still the default.</para>
    /// </summary>
    public static CardFaceSource CardFaces(PeerCardPopulation population,
                                           ScenarioRuleLibrary.CPlayerActor? actor,
                                           bool scenarioEstablished)
    {
        try
        {
            if (scenarioEstablished)
            {
                if (actor == null)
                    return CardFaceSource.None;
                // THE CARVE-OUT AND THE CARVE-OUT FROM IT, in one expression and in this order: the
                // secrecy term is asked ONLY of a population the secret applies to. An already
                // public card still needs a running scenario (the capability half above) and still
                // needs a character to resolve against — it just has no phase to be secret in.
                bool secret = !IsPublicPopulation(population) && !ShowRoundCardFronts(actor);
                return secret ? CardFaceSource.None : CardFaceSource.Scenario;
            }
            return ShowMapPhaseHandFronts ? CardFaceSource.MapLoadout : CardFaceSource.None;
        }
        catch
        {
            return CardFaceSource.None;
        }
    }

    /// <summary>
    /// THE SAME CALL, WITH THE CARD IN HAND — for a surface that can name the very card it is about
    /// to draw. Identical to <see cref="CardFaces(PeerCardPopulation, CPlayerActor)"/> except that a
    /// card whose face is ALREADY PUBLIC (<see cref="IsPubliclyRevealedCard"/>) is answered as
    /// <see cref="PeerCardPopulation.AlreadyPublic"/> whatever population the surface declared.
    ///
    /// <para>THE ORDER OF THE TWO TERMS IS THE POINT. The population's own answer is asked FIRST and
    /// wins whenever it already says yes, so this overload can only ever WIDEN — it has no branch in
    /// which a card that would have been shown is hidden. That is what makes it safe to route a
    /// surface through it unconditionally.</para>
    ///
    /// <para><paramref name="cardInstanceId"/> is <c>CAbilityCard.CardInstanceID</c> (or
    /// <c>AbilityCardUI.CardInstanceID</c>, which is the same number) — never a card ID, which names
    /// a card TYPE and would exempt a second copy of the same card sitting in a hand.</para>
    /// </summary>
    public static CardFaceSource CardFaces(PeerCardPopulation population,
                                           ScenarioRuleLibrary.CPlayerActor? actor,
                                           int cardInstanceId)
    {
        CardFaceSource byPopulation = CardFaces(population, actor);
        if (byPopulation != CardFaceSource.None)
            return byPopulation;
        return CardIsPubliclyVisible(population, actor, cardInstanceId)
            ? CardFaces(PeerCardPopulation.AlreadyPublic, actor)
            : CardFaceSource.None;
    }

    /// <summary>
    /// THE TWO CARD-PROPERTY EXCEPTIONS THAT OPEN A FACE THE POPULATION HAS ALREADY REFUSED, in one
    /// expression so no surface can claim one of them and miss the other. Both are properties of the
    /// CARD, so both follow it off the surface it was drawn on — which is the whole reason they are
    /// not <see cref="PeerCardPopulation"/> members.
    ///
    /// <para>ONE OF THE TWO IS SCOPED AND THE OTHER IS NOT, AND THAT ASYMMETRY IS THE FIX RATHER
    /// THAN AN INCONSISTENCY. <see cref="IsPubliclyRevealedCard"/> (burnt / active) reaches EVERY
    /// population including the recess: "Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer
    /// mit der Vorderseite sichtbar sein". <see cref="IsDiscardedCard"/> reaches every population
    /// EXCEPT the two that are a decision in flight (<see cref="PileFrontsReach"/>), because the
    /// short-rest sacrifice is a discard-pile card and must stay covered. Two rulings, one
    /// expression, and the term that separates them is named.</para>
    ///
    /// <para>It can only ever WIDEN: it is asked only after the population has already answered
    /// <see cref="CardFaceSource.None"/>, so there is no state in which it hides something.</para>
    /// </summary>
    private static bool CardIsPubliclyVisible(PeerCardPopulation population,
                                              ScenarioRuleLibrary.CPlayerActor? actor,
                                              int cardInstanceId)
        => IsPubliclyRevealedCard(actor, cardInstanceId)
           || (PileFrontsReach(population) && IsDiscardedCard(actor, cardInstanceId));

    /// <summary>
    /// WHICH RULE CHOSE THE FACE — the named answer to the question every wrong-face report has
    /// cost a round to ask: not "was it a front or a back", which the census already prints, but
    /// WHY.
    ///
    /// <para>IT EXISTS BECAUSE A COUNT AND A REASON CAME APART, AND THE ModBuild 470 HOST LOG
    /// SHOWS IT. That census read <c>round slots[p2] 1 FRONT / 0 BACK —
    /// RevealGate.ShowRoundCardFronts(actor)=false</c>: a front, beside the sentence saying fronts
    /// were forbidden. Nothing was lying — the population's rule string was true of the POPULATION
    /// and the front had been chosen for one CARD by a different rule (the short-rest carve-out
    /// that item 6 has since retired, or the burn exception). A rule printed per population cannot
    /// name a rule that fires per card, and this project has a recorded finding for exactly that
    /// shape. This enum is per CARD.</para>
    /// </summary>
    public enum FaceRule
    {
        /// <summary>BACK. The game's own secret <c>SelectAbilityCardsOrLongRest</c> window is open
        /// online for a character that is not ours, and this card's population is one the secret
        /// applies to. THE SHORT REST LANDS HERE (user, 2026-09-07 item 6: "Kurze Rast =
        /// Auswahlphase = verdeckt"), because a short rest runs inside that phase.</summary>
        SelectionPhaseCovered,

        /// <summary>FRONT. No secret is in flight — the action phase, our own character, offline, or
        /// a scenario that is not in the selection window. THE LONG REST LANDS HERE (user: "Lange
        /// Rast = Aktionsphase = alles offen"): it is committed during selection but RESOLVES as an
        /// action, so its picks and its laid card are drawn under this rule and not the one above.
        ///
        /// <para>THAT SENTENCE WAS CHALLENGED AS THIS PROJECT'S SEVENTEENTH FALSE ASSERTION AND IT
        /// MEASURED TRUE. The 2026-09-07 report item 9 ("Wenn man bei der langen Rast die Karte auf
        /// das Board legt sehen die anderen Spieler wieder nur eine Rueckseite … Das Problem hatte
        /// ich bereits einmal gemeldet") reads exactly like a phase misclassification, and the
        /// obvious reading is that a long rest runs inside <c>SelectAbilityCardsOrLongRest</c>
        /// because the phase's own NAME says so. It does not, and the ModBuild 472 pair of logs says
        /// so three independent ways. It is recorded HERE rather than in a plan file because the next
        /// round will re-derive it from the phase name otherwise, as this one did.</para>
        ///
        /// <list type="number">
        /// <item><description>THE OWNER'S OWN LONG-REST FLAG, against the phase it was live in. The
        /// host log's <c>[Cards] REST GATE</c> line prints <c>long=True</c> in phase
        /// <c>MonsterClassesSelectAbilityCards</c> and again in phase <c>Action</c> — and NEVER in
        /// <c>SelectAbilityCardsOrLongRest</c>. So while a long rest was live,
        /// <see cref="IsSecretSelectionPhase"/> was false and <see cref="ShowRoundCardFronts"/> was
        /// OPEN.</description></item>
        /// <item><description>THE OBSERVER'S CENSUS, on the tick the peer was covered. Every
        /// <c>PEER CARD FACE CENSUS</c> line on the observing client that carries a non-zero BACK
        /// reads <c>PHASE=ActionSelection, online=True, POLICY=FRONTS EVERYWHERE</c>. Not one of them
        /// is in the secret window.</description></item>
        /// <item><description>THE RULE STRING BESIDE EVERY BACK. Across BOTH logs, 37 of 37
        /// per-population breach rows name an OPEN gate as the reason — the length belt, a seat the
        /// sender could not name, or a widget that did not resolve. ZERO name
        /// <c>ShowRoundCardFronts(actor)=false</c> and ZERO name
        /// <see cref="SelectionPhaseCovered"/>.</description></item>
        /// </list>
        ///
        /// <para>THE MEASUREMENT, REPRODUCIBLE:
        /// <c>grep -a '\] \[Net\] PEER CARD FACE CENSUS' Player.log | grep 'POLICY=FRONTS' |
        /// grep -v 'and 0 showing a BACK right now' | grep -c 'ShowRoundCardFronts(actor)=false'</c>
        /// — a NON-ZERO reading is the first evidence this paragraph is wrong, and then (and only
        /// then) the term to add is <c>CCharacterClass.LongRest</c>. It is available and it is sound:
        /// host-replicated (<c>PlayerState.IsLongResting</c>, <c>CPlayerActor.cs:449</c>), and
        /// DESYNC-CHECKED by the game itself across clients (<c>PlayerState.cs:321</c>, "Player State
        /// IsLongResting does not match"), so it is required to agree on every machine and needs no
        /// wire field. It would have to be spelled <c>LongRest &amp;&amp; !ImprovedShortRest</c>: an
        /// IMPROVED short rest sets the same flag (<c>CardsHandUI.cs:730</c>) because the game runs
        /// it through the long-rest confirmation UI, and a short rest must stay
        /// <see cref="SelectionPhaseCovered"/>. Nothing was added, because a term that can only widen
        /// a gate that already measured OPEN cannot fix a back and can only cost secrecy.</para>
        ///
        /// <para>WHERE ITEM 9 ACTUALLY COMES FROM, so the next round starts where this one finished:
        /// the fronts are refused downstream of this file, by ARITHMETIC, on surfaces whose rule
        /// strings already say the gate was open. The card laid on the board is
        /// <c>extension record 39 named NO seat for this recess</c>; the vanished FAN — the report's
        /// second symptom — is <c>Net.Remote.RemoteHandFan</c>'s LENGTH BELT refusing a pick fan
        /// whose arc order (record 44) the OWNER withheld, 42 change-gated times, with its own
        /// reason: <c>FAN ARC ORDER SENT: WITHHELD … why=an arc card is not in this hand's fan walk
        /// (a loan or a pick fan)</c>. That sender derives record 44 against the HAND unconditionally
        /// and has no record-43 branch, so a long rest's DISCARD arc can never be described at
        /// all.</para></summary>
        ActionPhaseOpen,

        /// <summary>FRONT, IN EVERY PHASE INCLUDING A SHORT REST — the burn exception. This card is
        /// in its owner's <c>ActivatedCards</c>, <c>LostAbilityCards</c> or
        /// <c>PermanentlyLostAbilityCards</c> (<see cref="IsPubliclyRevealedCard"/>). User,
        /// verbatim: "Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der
        /// Vorderseite sichtbar sein." It OUTRANKS <see cref="SelectionPhaseCovered"/> and is the
        /// only thing that does.</summary>
        BurnOrActivePublicCard,

        /// <summary>FRONT, IN EVERY PHASE — the PILE-FAN ruling. This card is in its owner's
        /// <c>DiscardedAbilityCards</c> (<see cref="IsDiscardedCard"/>) and the surface drawing it is
        /// not one of the two the ruling does not reach. User, verbatim (2026-09-07 evening, item
        /// 3): "Die Fächer der piles werden also ab jetzt immer mit Vorderseiten gezeigt ohne
        /// Ausnahme."
        ///
        /// <para>IT IS KEPT DISTINCT FROM <see cref="BurnOrActivePublicCard"/> DELIBERATELY. The two
        /// open the same front for different reasons, and this enum exists precisely because a
        /// verdict whose REASON is guessed costs a round: a front on a discarded card is the item-3
        /// ruling working, while the same front named as a BURN would send the next reader looking
        /// for a burn that never happened. It is also the falsifier for the scoping in
        /// <see cref="PileFrontsReach"/> — this rule appearing beside a SHORT-REST recess would mean
        /// the sacrifice carve-out has been undone.</para></summary>
        DiscardPileCard,

        /// <summary>FRONT. The surface declared a population that carries its own standing ruling —
        /// the active-card matrix, an ITEM card, or a mirrored decision row's WORDING. See
        /// <see cref="IsPublicPopulation"/>.</summary>
        PublicPopulation,

        /// <summary>FRONT, off the peer's replicated map loadout: no scenario is running, so there
        /// is no phase to be secret in.</summary>
        MapLoadout,

        /// <summary>BACK. Not a secrecy verdict at all — there is no actor, no scenario and no map,
        /// so no face could be resolved from anywhere. Kept distinct from
        /// <see cref="SelectionPhaseCovered"/> because reading a CAPABILITY failure as a SECRECY
        /// answer is this file's oldest recorded defect.</summary>
        NoContext,
    }

    /// <summary>
    /// <see cref="CardFaces(PeerCardPopulation, CPlayerActor, int)"/>, naming the rule that decided.
    /// The verdict is unchanged — this overload adds no branch and can widen nothing; it only says
    /// out loud which of the three sentences in the user's ruling produced the face, so the next
    /// report of a wrong face is one grep instead of a round.
    /// </summary>
    public static CardFaceSource CardFaces(PeerCardPopulation population,
                                           ScenarioRuleLibrary.CPlayerActor? actor,
                                           int cardInstanceId,
                                           out FaceRule rule)
        => CardFaces(population, actor, cardInstanceId, InScenario, out rule);

    /// <summary>
    /// THE RULE-NAMING OVERLOAD WITH THE CAPABILITY HALF SUPPLIED — see
    /// <see cref="CardFaces(PeerCardPopulation, CPlayerActor, bool)"/> for why a caller may own that
    /// term, and why passing it is the opposite of loosening the gate.
    ///
    /// <para>THE VERDICT IS THE THREE-ARGUMENT OVERLOAD'S, TERM FOR TERM, and this method adds no
    /// branch to it. What it adds is the NAME of the rule that produced it — including the one
    /// distinction the hand-written ladders that used to stand at the call sites could not make:
    /// a BACK because the phase COVERED the card (<see cref="FaceRule.SelectionPhaseCovered"/>)
    /// versus a BACK because no face could be resolved from anywhere
    /// (<see cref="FaceRule.NoContext"/>). Reading a CAPABILITY failure as a SECRECY answer is this
    /// file's oldest recorded defect and a call site spelling its own ternary cannot tell them
    /// apart, because it has already folded the two into one bool.</para>
    /// </summary>
    public static CardFaceSource CardFaces(PeerCardPopulation population,
                                           ScenarioRuleLibrary.CPlayerActor? actor,
                                           int cardInstanceId,
                                           bool scenarioEstablished,
                                           out FaceRule rule)
    {
        CardFaceSource byPopulation = CardFaces(population, actor, scenarioEstablished);
        if (byPopulation == CardFaceSource.MapLoadout)
        {
            rule = FaceRule.MapLoadout;
            return byPopulation;
        }
        if (byPopulation == CardFaceSource.Scenario)
        {
            rule = IsPublicPopulation(population)
                ? FaceRule.PublicPopulation
                : FaceRule.ActionPhaseOpen;
            return byPopulation;
        }
        // Refused by population. TWO card properties reopen it — the burn/active exception and the
        // pile-fan ruling — and both are asked second so they can only ever widen (see
        // CardIsPubliclyVisible). They are asked SEPARATELY here, and only here, so the rule name is
        // the one that actually decided: a log that says BURN when the card was merely discarded is
        // the "a rule printed per population cannot name a rule that fires per card" defect this
        // enum exists to kill, one level down.
        bool burnOrActive = IsPubliclyRevealedCard(actor, cardInstanceId);
        bool pileFan = !burnOrActive
                       && PileFrontsReach(population)
                       && IsDiscardedCard(actor, cardInstanceId);
        if (burnOrActive || pileFan)
        {
            CardFaceSource asPublic =
                CardFaces(PeerCardPopulation.AlreadyPublic, actor, scenarioEstablished);
            if (asPublic != CardFaceSource.None)
            {
                rule = burnOrActive ? FaceRule.BurnOrActivePublicCard : FaceRule.DiscardPileCard;
                return asPublic;
            }
        }
        // A BACK, and the two reasons for one are not the same reading. A running scenario with an
        // actor means the phase covered it; anything else means no face could have been resolved at
        // all.
        rule = scenarioEstablished && actor != null
            ? FaceRule.SelectionPhaseCovered
            : FaceRule.NoContext;
        return CardFaceSource.None;
    }

    /// <summary>The log wording for <paramref name="rule"/> — one sentence per rule, so a hardware
    /// log names the ruling and not just the outcome. Kept beside the enum because a rule whose
    /// wording lives at a call site is a rule two call sites can word differently.</summary>
    public static string RuleText(FaceRule rule) => rule switch
    {
        FaceRule.SelectionPhaseCovered =>
            "SELECTION PHASE — the game's own secret SelectAbilityCardsOrLongRest window is open "
            + "for this remote character, so this card is COVERED (a SHORT REST runs inside this "
            + "phase and is covered with it: user 2026-09-07 item 6)",
        FaceRule.ActionPhaseOpen =>
            "ACTION PHASE — no secret is in flight, so the real front is shown (a LONG REST "
            + "resolves here, not in the selection window: user 2026-09-07 item 6)",
        FaceRule.BurnOrActivePublicCard =>
            "BURN/ACTIVE EXCEPTION — this card is in its owner's activated or lost/permanently-lost "
            + "list, so its front is shown in EVERY phase including a short rest (user: 'Beim "
            + "Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite sichtbar "
            + "sein')",
        FaceRule.DiscardPileCard =>
            "PILE FAN — this card is in its owner's discard pile, and a pile a player has already "
            + "spent is public in EVERY phase (user 2026-09-07: 'Die Fächer der piles werden also "
            + "ab jetzt immer mit Vorderseiten gezeigt ohne Ausnahme'). It follows the card, so it "
            + "holds in the fan and in the owner's fist alike. This rule beside a SHORT-REST recess "
            + "would be a DEFECT — the sacrifice is a discard-pile card and RevealGate"
            + ".PileFrontsReach exists to keep it covered",
        FaceRule.PublicPopulation =>
            "PUBLIC POPULATION — this surface declared a population carrying its own standing "
            + "ruling (the active-card matrix, an ITEM card, or a decision row's wording)",
        FaceRule.MapLoadout =>
            "MAP LOADOUT — no scenario is running, so there is no phase to be secret in",
        _ =>
            "NO CONTEXT — no actor, no scenario and no map, so no face could be resolved from "
            + "anywhere. This is a CAPABILITY failure and NOT a secrecy verdict",
    };

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
        !(FFSNetwork.IsOnline && (actor == null || !LocallyControls(actor)));

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
            return !(quest.IsConcealed && FFSNetwork.IsOnline && !LocallyControls(actor));
        }
        catch
        {
            return false; // a half-loaded campaign state must never leak by accident
        }
    }
}
