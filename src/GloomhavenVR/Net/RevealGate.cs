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
///   <c>FFSNetwork.IsOnline &amp;&amp; InScenario &amp;&amp; actor != null &amp;&amp;
///      !actor.IsUnderMyControl &amp;&amp;
///      PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest</c>
///
/// Every game access is null-guarded and degrades to the SAFE-to-show default (true) only when
/// we are clearly local / offline; during the secret selection phase online it degrades to
/// hiding (false). Simple static property reads are NOT wrapped in try/catch (they cannot throw
/// once the null guards pass); only <c>SaveData.Instance</c> is null-guarded.
/// </summary>
internal static class RevealGate
{
    /// <summary>True while the game is in the secret ability-card selection phase (cards not yet
    /// committed/revealed). Reads the game's authoritative <see cref="PhaseManager"/>.</summary>
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
    /// vanilla reveal rule: hide fronts only while online, in a scenario, for an actor NOT under
    /// our control, during the secret selection phase. In every other case (offline / single
    /// player / map / our own actor / post-reveal action phase) the cards are shown.
    /// </summary>
    public static bool ShowRoundCardFronts(CPlayerActor actor) =>
        !(FFSNetwork.IsOnline
          && InScenario
          && actor != null
          && !actor.IsUnderMyControl
          && PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest);

    /// <summary>
    /// THE SAME RULE, FROM THE OTHER END OF THE WIRE: do our PEERS currently see the fronts of OUR
    /// cards? It is <see cref="ShowRoundCardFronts"/> evaluated on our own actor from a peer's seat,
    /// where <c>!actor.IsUnderMyControl</c> is true by construction (every one of our characters is
    /// "somebody else's" to them) and therefore folds out — leaving the two terms a remote client can
    /// still check for itself: online, in a scenario, in the secret selection window.
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
        !(FFSNetwork.IsOnline && InScenario && IsSecretSelectionPhase);

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
    // ============================================================================================

    /// <summary>
    /// True while a MAP is loaded and NO scenario is running — the campaign/guildmaster map phase,
    /// the state the mod's 3D map room stands in. <c>MapState</c> is the same object
    /// <see cref="InScenario"/> guards against being null (see the note there) and the same one
    /// <see cref="ShowPersonalQuest"/> reads the party out of, so a half-loaded save answers false
    /// on every one of them alike. Guarded and degrading to false, because "we cannot tell where we
    /// are" must never be the reason a front is drawn.
    /// </summary>
    public static bool InMapPhase
    {
        get
        {
            if (InScenario)
                return false;
            try
            {
                return MapRuleLibrary.Adventure.AdventureState.MapState != null;
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
