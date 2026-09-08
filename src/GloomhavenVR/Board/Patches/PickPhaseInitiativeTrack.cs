using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// THE INITIATIVE TRACK IS EMPTY IN EVERY DECISION FLOW THE GAME NEVER FILLED IT FOR.
///
/// <para>USER REPORT 2026-08-24 (verbatim): "Die Initativreihenfolge verschwindet während dessen
/// komplett. Das darf niemals sein, die bereits implementierte Logik soll davon NICHT berührt
/// werden. D.h. es soll weiterhin ohne Probleme möglich sein die Charactere zu wechseln, auch wenn
/// die Entscheidung noch nicht getroffen wurde. Die Initativreihenfolge soll normal anzeigen wer
/// gerade noch eine ENtscheidung treffen muss wie zuvor auch." The screenshots are
/// kartenabwurf1.jpg / kartenabwurf2.jpg: the control board and its banner are up, and the whole
/// initiative row band is gone.</para>
///
/// <para>WHAT IS ACTUALLY WRONG — and it is NOT the mod. The panel is never withdrawn (the hardware
/// log has GloomhavenVR.Panel_InitiativeTrack docked continuously across the whole discard,
/// LogOutput.log:3668/3674/3685/3832/4098/4117/4153/4298/4315/4480…5013), so what is empty is the
/// CONTENT. Rows are activated in exactly ONE place: the loop in
/// <c>InitiativeTrack.UpdateInitiativeTrack</c> (InitiativeTrack.cs:607-628, <c>actorsUI[num]
/// .gameObject.SetActive(true)</c> at :609), and the private <c>NormalizeActorsPool</c> it calls
/// first (:544-585) ends by deactivating EVERY pooled entry unconditionally (:580-584). So an
/// <c>UpdateInitiativeTrack</c> that never ran leaves a fully-built, fully-blank widget — which is
/// the reported picture exactly, and it is what the hardware log's first content measurement of the
/// panel says too: "DRAWN CONTENT … from 0 visible graphic(s)" (LogOutput.log:3685).</para>
///
/// <para>WHY IT NEVER RAN. There are six call sites of <c>UpdateInitiativeTrack</c> in the whole
/// decompile (Choreographer.cs:12539/12548/12601, LevelEventsController.cs:1040/1105,
/// ChangeModelSMB.cs:69). The forced discard is presented by the
/// <c>CMessageData.MessageType.SelectLoseCards</c> handler (Choreographer.cs:7854-7910), which
/// calls <c>CardsHandManager.Show(…, CardHandMode.DiscardCard, …)</c> at :7880 and touches the
/// track only to write its <c>helpBox</c> at :7894 — it never refreshes the rows. The generic
/// per-message refresh at the end of the same method (Choreographer.cs:12534) cannot cover it
/// either: its message whitelist does not contain <c>SelectLoseCards</c>, AND it additionally
/// requires <c>GameState.InitiativeSortedActors.Count &gt; 0</c>, which is 0 before anybody has
/// chosen this round's cards. The first population of a scenario is <c>OnShow</c>
/// (Choreographer.cs:12601), the completion callback of the ROUND-1 card-selection screen
/// (started at Choreographer.cs:3602-3607) — i.e. AFTER the scenario-start discard, which is
/// installed as a <c>StartScenario</c> scenario modifier under <c>IsFirstLoad</c>
/// (UnityGameEditorRuntime.cs:259-262). READ from source; the flat game therefore shows the same
/// blank band, and none of the mod's three patches on this class can be responsible (two are
/// POSTFIXES, which cannot suppress the original, and no mod code anywhere calls
/// <c>UpdateInitiativeTrack</c>, <c>NormalizeActorsPool</c> or <c>SetActive</c> on a row).</para>
///
/// <para>THE REMEDY IS THE GAME'S OWN METHOD WITH THE GAME'S OWN ARGUMENTS. When a decision flow is
/// open (until ModBuild 474 that read "a forced card pick" — see the 2026-09-07 section below for
/// why that was the defect) and the track has zero visible rows, this asks
/// <c>UpdateInitiativeTrack(actors, playersSelectable: true, enemiesSelectable: false,
/// selectActor: false)</c> — the same <c>List&lt;CActor&gt;</c> overload every other call site ends
/// in (InitiativeTrack.cs:587), built from <c>Choreographer.m_ClientPlayers</c> +
/// <c>ClientMonsterObjects</c> exactly as the <c>GameObject</c> overload builds it
/// (InitiativeTrack.cs:702-713). <c>playersSelectable: true</c> is what the card-selection phase
/// passes (Choreographer.cs:12539) and is the half that keeps the rows CLICKABLE, i.e. the
/// character switch the report insists must keep working.</para>
///
/// <para><b>selectActor IS FALSE ON PURPOSE, and that is the whole safety argument.</b> The
/// <c>GameObject</c> overload hard-codes <c>selectActor: true</c> (InitiativeTrack.cs:713) and can
/// additionally <c>Select</c> a row itself (:715-729). <c>InitiativeTrack.Select</c> →
/// <c>InitiativeTrackActorBehaviour.Select</c> (InitiativeTrackActorBehaviour.cs:63-66) →
/// <c>InitiativeTrackPlayerAvatar.Select</c>, and THAT calls
/// <c>CardsHandManager.Instance.SwitchHand((CPlayerActor)m_Actor)</c>
/// (InitiativeTrackPlayerAvatar.cs:22-27). During a forced pick the actor the track would select is
/// <c>Choreographer.m_CurrentActor</c>, which the <c>SelectLoseCards</c> handler sets to the
/// ability's SPAWNING actor (Choreographer.cs:7859) — and that handler explicitly allows the
/// spawning and the LOSING actor to differ (:7862-7865). Selecting here could therefore switch the
/// presented hand out from under an open discard. So this path never selects anything: it fills
/// rows and nothing else. Which actor is highlighted stays the game's business, and the mod's own
/// attention ring (Board.FocusDriver.TickRings) paints the "who must decide" cue on whichever row
/// now exists.</para>
///
/// <para>WHAT IT COSTS AND WHAT IT DOES NOT COVER, stated plainly.
/// <list type="bullet">
/// <item>Two <c>List</c> allocations per FIRE inside the game's method, plus the
/// <c>ClientMonsterObjects</c> LINQ projection. That is why it fires on an EDGE and then latches:
/// the steady state is three field compares and a child-count loop over the row holder.</item>
/// <item>It cannot make the track show something the game does not know. The rows carry no
/// initiative number yet because nobody has chosen cards — that is true in vanilla too and this
/// does not invent one.</item>
/// <item>The game presents forced discards ONE ACTOR AT A TIME (<c>CardsHandManager.Show</c> is
/// called with <c>m_ActorLosingCards</c>, Choreographer.cs:7880, and the hardware Player.log shows
/// two separate <c>SelectLoseCards</c> messages at :10736 and :12126 for the two characters). The
/// report's "in dem Fall können es eben mehrere gleichzeitig sein" therefore describes a
/// simultaneity the rules engine does not produce here; the track shows the party, and the ring
/// follows the actor the game is actually waiting on.</item>
/// <item>It is deliberately blind to the row COUNT being wrong for any other reason (the game's own
/// dedupe by <c>CClass</c> at InitiativeTrack.cs:593 collapses two monsters of one class into one
/// row). The trigger is ZERO rows, never "fewer than I expected".</item>
/// </list></para>
///
/// <para>MULTIPLAYER: local presentation only. <c>UpdateInitiativeTrack</c> writes pooled UI
/// objects and reads <c>ScenarioManager.Scenario</c>, <c>PhaseManager.PhaseType</c> and
/// <c>Choreographer</c> state the game already replicates; no channel is opened, no game state is
/// written, and with <c>selectActor: false</c> not even a UI selection event is raised. Every seat
/// makes the same decision independently from data it already has.</para>
///
/// ══════════════════════════════════════════════════════════════════════════════════════════════
/// <para><b>2026-09-07 (ModBuild 474), item 4 — THE TRIGGER WAS THE DEFECT, NOT THE REMEDY.</b>
/// Verbatim: "In den Flows am Anfang eines Szenarios zB Gegenstand oder Karten ablegen - ist die
/// Initiativreihenfolge nicht sichtbar an allen boards. Die soll auch da schon sichtbar sein und
/// zeigen wer dran ist und eine Entscheidung treffen muss. Ich weiß aus Tests bei Kartenabwurf dass
/// es da sichtbar war. Im Test jetzt war explizit der Flow dass ein Spieler einen Gegenstand
/// abwerfen musste. <b>Bei allen Flows soll die Initiativreihenfolge sichtbar sein.</b>"</para>
///
/// <para>Everything above this line is CORRECT and was re-verified against the decompile on
/// 2026-09-07 — including the generic-refresh whitelist at Choreographer.cs:12534, which really
/// does exclude <c>SelectLoseCards</c> and really does additionally require
/// <c>GameState.InitiativeSortedActors.Count &gt; 0</c>. What was wrong is narrower and it is the
/// FIRST TEST this class ran: <b>the repair was keyed on a CARD HAND.</b> It asked
/// <c>CardsGameApi.ActiveHand()</c> and then demanded <see cref="IsForcedPickMode"/> of that hand's
/// <c>currentMode</c>. Every flow that is not presented through <c>CardsHandManager</c> failed that
/// test on its first line and the band stayed blank — which is the report exactly.</para>
///
/// <para><b>THE CONTROLLED COMPARISON, RUN AGAINST SOURCE.</b> The user handed us one: a CARD
/// discard where the track was visible against an ITEM discard where it was not. It could NOT be
/// run against this round's two logs — <c>grep -c 'SelectLoseCards'</c> and
/// <c>grep -c 'DiscardCard'</c> are BOTH 0 on both clients, so the card flow simply did not occur
/// this session and a log diff would have compared one flow against nothing. Run against the
/// decompiled handlers instead, the two flows differ in exactly the term this class tested:</para>
/// <list type="table">
///   <item><term>CARD discard — <c>SelectLoseCards</c> (Choreographer.cs:7854-7910)</term>
///     <description><c>CardsHandManager.Instance.Show(actor, DiscardCard|LoseCard, …)</c> at :7880
///       runs UNCONDITIONALLY — there is no <c>IsUnderMyControl</c> gate in front of it. So on
///       EVERY client <c>ActiveHand()</c> came back non-null in a forced mode, the old trigger hit,
///       and the band was refilled. That is precisely why the user remembers it working.</description></item>
///   <item><term>ITEM discard — <c>SelectRefreshOrConsumeItems</c> (Choreographer.cs:5853-5905)</term>
///     <description>presents <c>ItemCardRefreshPicker.Show(…)</c> at :5880, behind
///       <c>if (!FFSNetwork.IsOnline || m_ActorBeenRefreshed.IsUnderMyControl)</c>. There is NO
///       card hand in this flow at all, on ANY client — so <c>ActiveHand()</c> was null or a stale
///       leftover tab, <see cref="IsForcedPickMode"/> was false, and the tick returned before it
///       ever counted a row.</description></item>
/// </list>
///
/// <para><b>AND A LOCAL-CONTROL FIX WOULD HAVE MISSED THE REPORTER.</b> This is the half that
/// decides the shape of the remedy, so it is worth being blunt about. The obvious repair — swap
/// <c>ActiveHand()</c> for the mod's own <c>CardsGameApi.DecidingHand()</c> chain — is WRONG on its
/// own: every member of that chain (<c>TakeDamageHand</c>, <c>ActionSelectionHand</c>,
/// <c>ItemPickHand</c>, <c>LoseRewardPickHand</c>, <c>InitiativeAdjustHand</c>) ends in an
/// <c>IsUnderMyControl</c> gate, so it is non-null ONLY on the client that must decide. The logs
/// say that is the wrong client: the item pick of 2026-09-07 is in the PEER's log
/// (<c>remote/Player.log:43604</c> "ITEM SURRENDER pick OPEN (consume): the game demands 1 item(s)
/// from 'Testi'") and appears NOWHERE in the host's (<c>grep -c ItemCardRefreshPicker</c> = 0),
/// because :5880 only Shows it on the controlling seat. The host — the man who filed the report —
/// was the client that had nothing to decide and therefore nothing to look at. His words are
/// "zeigen WER dran ist und eine Entscheidung treffen muss": the surface exists for the players who
/// are WAITING. A trigger that can only fire on the decider answers the opposite question.</para>
///
/// <para><b>THE GENERAL TERM: <see cref="IsDecisionWait"/> over <c>Choreographer.m_WaitState</c>.</b>
/// One term is true on EVERY client in EVERY one of these flows, and both halves of the user's
/// controlled comparison set it on the line after the presenter they disagree about:
/// <c>SetChoreographerState(WaitingForCardSelection, …)</c> at Choreographer.cs:7887 for the card
/// discard, and <c>SetChoreographerState(WaitingForItemRefresh, …)</c> at :5892 for the item pick —
/// the latter OUTSIDE the <c>IsUnderMyControl</c> gate that owns the picker. The rules model is
/// local (every client runs the same Choreographer off the same messages), so this is a rendering
/// gate read from a purely local state machine, not a wire question. The card hand and the picker
/// are still consulted, but only to NAME the flow in the log — never to decide it.</para>
///
/// <para><b>WHY WIDENING THE TRIGGER DOES NOT START A FIGHT WITH THE GAME.</b> The old scope note
/// on <see cref="IsForcedPickMode"/> warned that "a repair that fires where nothing is broken is a
/// repair that will one day fight the game", and that warning is right — it is answered, not
/// ignored. The safety was never the mode test; it is that the fill fires ONLY on <b>zero visible
/// rows</b>, and the two places the game deliberately reshapes this band during a decision wait
/// both leave rows STANDING, so this class latches and writes nothing:
/// <list type="number">
///   <item><c>InitiativeTrack.ShowMonsterClassesForSelectingRoundAbilityCards</c>
///     (InitiativeTrack.cs:741-779) ADDS enemy rows via <c>NormalizeEnemiesUiPool</c> into the same
///     <c>initiativeTrackHolder</c> and only <c>Deselect()</c>s the player rows — it never
///     deactivates one. <see cref="VisibleRows"/> is &gt; 0 throughout the enemy-info screen.</item>
///   <item>Round-start card selection: <c>OnShow</c> calls <c>UpdateInitiativeTrack</c> at
///     Choreographer.cs:12601 and only THEN sets <c>WaitingForCardSelection</c> at :12613. The band
///     is already populated by the time this class is allowed to look at it.</item>
/// </list>
/// The window that is left is the one the report is about: scenario start, before any card has been
/// chosen, where <c>GameState.InitiativeSortedActors.Count</c> is still 0 and the game's own generic
/// refresh is blocked by its own precondition.</para>
///
/// <para><b>THE EVIDENCE THAT NAMES IT, from the two ModBuild 474 logs.</b> The host's initiative
/// panel was measured blank across the whole item decision. Aligning on the two anchor pairs either
/// side of it — the scenario gate OPEN edge (<c>Player.log:50045</c> / <c>remote/Player.log:40961</c>)
/// and the first <c>[CardsActionController.cs] Called Init(topCard:</c>
/// (<c>Player.log:103668</c> / <c>remote/Player.log:89316</c>) — the peer's item window
/// (<c>remote/Player.log:43604→53343</c>) interpolates onto the host's timeline at roughly
/// <c>Player.log:52,976→63,775</c>. The host's own panel instrument brackets that interval with two
/// CONSECUTIVE commits and nothing elided between them:
/// <c>Player.log:51233</c> "HIT RECT 'GloomhavenVR.Panel_InitiativeTrack' (commit #1 …) DRAWN
/// CONTENT 1920x1080 px at (0,0) from <b>0 visible graphic(s)</b>" and <c>Player.log:66076</c>
/// "(commit #2 …) DRAWN CONTENT 805x204 px at (-990,996) from <b>40 visible graphic(s)</b>". A
/// change-triggered instrument that does not re-fire is the statement that nothing changed, so the
/// band was blank from before the decision opened until after it closed. The panel itself was never
/// withdrawn — it is the CONTENT that was empty, the same picture as 2026-08-24.</para>
///
/// <para><b>WHAT THIS STILL DOES NOT COVER.</b> A decision the Choreographer does not park in one
/// of <see cref="IsDecisionWait"/>'s states is invisible here, and deliberately so: the animation
/// and sync waits (<c>WaitingFor*Anim</c>, <c>WaitingForPlayerIdle</c>, <c>WaitingFor*Sync</c>,
/// <c>WaitingForRewardsProcess</c>, <c>WaitingForAutosave</c>) are the game moving, not a human
/// choosing, and refilling a band during them would be inventing a cue.
/// <c>WaitingForAttackModifierCards</c> is excluded for the same reason — it waits on the modifier
/// DRAW to finish (see <c>FigureGrab.FigureBusy</c>, which classifies it as an unbounded FLOW wait,
/// not a decision). If a future report names a flow that is still blank, the log line below prints
/// the exact <c>m_WaitState.m_State</c> that was standing when it happened, so adding it is one
/// enum member rather than another round of archaeology.</para>
/// </summary>
internal static class PickPhaseInitiativeTrack
{
    /// <summary>The card-hand modes the game raises through <c>SelectLoseCards</c>
    /// (Choreographer.cs:7877 maps the ability type to exactly these two).
    ///
    /// <para>THIS IS NO LONGER THE TRIGGER — see <see cref="IsDecisionWait"/>. Until ModBuild 474
    /// it was, and being a CARD-hand test it excluded every flow the game does not present through
    /// <c>CardsHandManager</c>; the item pick of user report 2026-09-07 item 4 was one. It survives
    /// as a NAMING term only: when it is true the log line below can say which forced pick was
    /// standing, which is strictly more than the wait state alone can say.</para></summary>
    private static bool IsForcedPickMode(CardHandMode mode) =>
        mode == CardHandMode.DiscardCard || mode == CardHandMode.LoseCard;

    /// <summary>
    /// "IS THE GAME BLOCKED ON A HUMAN CHOOSING SOMETHING?" — the general trigger, and the whole
    /// point of the 2026-09-07 rework. Read off <c>Choreographer.m_WaitState.m_State</c> because
    /// that is the ONE term in these flows that is set on EVERY client rather than only on the seat
    /// holding the decision (Choreographer.cs:5892 and :7887 both set it outside any
    /// <c>IsUnderMyControl</c> gate), and the party waiting on one player is exactly who the
    /// initiative band has to answer for.
    ///
    /// <para>Enumerated, not pattern-matched on the name: three states spell "WaitingForPlayer…"
    /// and one of them (<c>WaitingForPlayerIdle</c>) is an ANIMATION wait with no decision in it at
    /// all. The class doc says what is left out and why.</para>
    /// </summary>
    private static bool IsDecisionWait(Choreographer.ChoreographerStateType s) =>
        s == Choreographer.ChoreographerStateType.WaitingForCardSelection
        || s == Choreographer.ChoreographerStateType.WaitingForItemRefresh
        || s == Choreographer.ChoreographerStateType.WaitingForLoseGoalChestRewardSelection
        || s == Choreographer.ChoreographerStateType.WaitingForElementPicked
        || s == Choreographer.ChoreographerStateType.WaitingForTileSelected
        || s == Choreographer.ChoreographerStateType.WaitingForPlayerWaypointSelection
        || s == Choreographer.ChoreographerStateType.WaitingForPlayerPushWaypointSelection
        || s == Choreographer.ChoreographerStateType.WaitingForPlayerPullWaypointSelection
        || s == Choreographer.ChoreographerStateType.WaitingForAreaAttackFocusSelection;

    /// <summary>
    /// The LOCAL presenter of the open decision, for the log line and for the re-fire latch —
    /// never for the trigger. Returns the object whose identity distinguishes one decision from the
    /// next (so a SECOND character's pick re-fires rather than being swallowed), and names it in
    /// <paramref name="presenter"/>.
    ///
    /// <para>Null is a MEANINGFUL answer and the common one on a waiting client: it says this seat
    /// holds no part of the decision. That case is the reported defect, so the string says so
    /// rather than reading as a failure to look.</para>
    ///
    /// <para><paramref name="describe"/> IS NOT A CONVENIENCE. This runs every frame for as long as
    /// any decision wait is open, and a mid-turn tile/waypoint selection holds one for seconds at a
    /// time; building <paramref name="presenter"/> unconditionally would allocate several strings
    /// per frame for a line that is emitted at most once per flow. The three tests themselves are
    /// allocation-free — each short-circuits on a phase compare or a singleton/window null check
    /// (see <c>CardsGameApi.DecidingHand</c>'s own note) — so only the prose is deferred, and the
    /// identity the latch compares is computed on exactly the same path either way.</para>
    /// </summary>
    private static object? LocalClaim(bool describe, out string presenter)
    {
        presenter = "";

        // 1. A forced CARD pick. Named first and explicitly because it is the user's WORKING
        //    REFERENCE: if this ever stops appearing, the regression is this class's, not the
        //    game's. Present on every client (Choreographer.cs:7880 has no control gate).
        CardsHandUI? hand = Cards.CardsGameApi.ActiveHand();
        if (hand != null && IsForcedPickMode(Cards.CardsGameApi.Mode(hand)))
        {
            if (describe)
                presenter = $"the card hand in {Cards.CardsGameApi.Mode(hand)} mode for " +
                            $"'{CharacterFocus.Describe(hand.PlayerActor)}'";
            return hand;
        }

        // 2. The ITEM refresh/consume picker — the flow of the 2026-09-07 report. Local to the
        //    deciding seat only (Choreographer.cs:5880).
        object? picker = Cards.CardsGameApi.OpenItemPicker(out CPlayerActor? itemActor,
                                                           out bool refreshing);
        if (picker != null)
        {
            if (describe)
                presenter = $"the item {(refreshing ? "REFRESH" : "CONSUME")} picker for " +
                            $"'{CharacterFocus.Describe(itemActor)}'";
            return picker;
        }

        // 3. Everything else this client owns, through the mod's own deciding-actor chain rather
        //    than a fourth hand-resolution rule of our own (take-damage, action selection, goal-
        //    chest forfeit, the boots ± phase).
        CardsHandUI? deciding = Cards.CardsGameApi.DecidingHand();
        if (deciding != null)
        {
            if (describe)
                presenter = "the deciding-actor chain, hand of " +
                            $"'{CharacterFocus.Describe(deciding.PlayerActor)}'";
            return deciding;
        }

        if (describe)
            presenter = "NONE on this client — the decision belongs to another seat and this " +
                        "client is one of the WAITING ones, which is the case the report is about";
        return null;
    }

    /// <summary>Re-arm interval. A fill that did NOT take (the pool was mid-build, an actor object
    /// was not resolvable yet) must be retried, but never per frame — <c>UpdateInitiativeTrack</c>
    /// allocates. 0.5 s is invisible to a player reaching for a card and bounds the cost at two
    /// tries a second in the pathological case.</summary>
    private const float RetrySeconds = 0.5f;

    private static float _nextTryAt;

    /// <summary>The decision the last verdict was reached for: the wait state, plus the presenter's
    /// reference identity (via <see cref="object.ReferenceEquals"/>) so a SECOND character's pick
    /// re-fires rather than being swallowed by the first one's latch. The state alone is not enough
    /// — two consecutive forced discards are both <c>WaitingForCardSelection</c>; the presenter
    /// alone is not enough either, because on a waiting client it is null for every flow.</summary>
    private static Choreographer.ChoreographerStateType _judgedState =
        Choreographer.ChoreographerStateType.NA;

    private static object? _judgedClaim;

    /// <summary>How many decision-flow opens each wait state has produced this session, and whether
    /// its "the band was already up" verdict has been stated once. A track that is UP is the
    /// EXPECTED reading and mid-turn waits (tile/waypoint selection) produce dozens of them per
    /// round, so that verdict class is capped at one line per state and carries its own running
    /// count — the flood ModBuild 331 removed must not come back through this door. The DOWN
    /// verdicts are never capped: they are the defect, they are rare, and they are the answer.</summary>
    private static readonly Dictionary<Choreographer.ChoreographerStateType, int> Opens = new(16);

    private static readonly HashSet<Choreographer.ChoreographerStateType> UpStated = new();

    /// <summary>Set once if the tick ever throws, so a bad frame reports and goes quiet instead of
    /// writing a line per frame for the rest of the session.</summary>
    private static bool _reportedThrow;

    /// <summary>Reused actor buffer — the fill is an edge, but the buffer keeps the steady-state
    /// allocation at zero and makes the retry path free.</summary>
    private static readonly List<CActor> Actors = new(16);

    /// <summary>Reset with the rest of the board state (scenario teardown / hot reload).</summary>
    internal static void Reset()
    {
        _nextTryAt = 0f;
        _judgedState = Choreographer.ChoreographerStateType.NA;
        _judgedClaim = null;
        Opens.Clear();
        UpStated.Clear();
        // ...AND THE THROW LATCH (2026-09 refactor, F-39). Same omission as EnemyInfoPhaseSkip's:
        // the SECOND scenario in a session that throws here reported nothing.
        _reportedThrow = false;
    }

    /// <summary>
    /// VISIBLE ROWS — the same question <c>EnemyInfoPhaseSkip.VisibleEnemyRows</c> asks about the
    /// enemy half, asked about the whole band. Counted off <c>initiativeTrackHolder</c>'s ACTIVE
    /// children rather than <c>actorsUI.Count</c> because the pool survives deactivation:
    /// <c>NormalizeActorsPool</c> leaves <c>actorsUI</c> fully populated and every entry
    /// <c>SetActive(false)</c> (InitiativeTrack.cs:580-584), so the list count is non-zero for a
    /// blank track. The same holder-child count is what
    /// <c>WorldUI.Surfaces.TablePanelSurfaces.RowLayoutSignature</c> already reads.
    /// </summary>
    internal static int VisibleRows(InitiativeTrack? track)
    {
        Transform? holder = track != null ? track.initiativeTrackHolder : null;
        if (holder == null)
            return 0;
        int n = 0;
        for (int i = 0; i < holder.childCount; i++)
        {
            if (holder.GetChild(i).gameObject.activeSelf)
                n++;
        }
        return n;
    }

    /// <summary>
    /// Per frame while an initiative track exists — see <see cref="TickGuarded"/> for the seam.
    ///
    /// <para>The steady state is TWO field reads and <see cref="IsDecisionWait"/>'s compare chain:
    /// outside a decision wait — which is nearly all of a scenario — nothing else runs, and
    /// <see cref="LocalClaim"/> is not reached at all. Inside one the extra cost is that method's
    /// three short-circuiting presence tests, allocation-free until a line is actually emitted.</para>
    /// </summary>
    private static void Tick(InitiativeTrack? track)
    {
        if (track == null)
            return;

        // THE TRIGGER — the choreographer's wait state, not a card hand. See IsDecisionWait.
        Choreographer chor = Choreographer.s_Choreographer;
        Choreographer.CWaitState? wait = chor != null ? chor.m_WaitState : null;
        Choreographer.ChoreographerStateType state =
            wait != null ? wait.m_State : Choreographer.ChoreographerStateType.NA;
        if (!IsDecisionWait(state))
        {
            // No decision open — drop the latch so the NEXT flow is judged from scratch.
            _judgedState = Choreographer.ChoreographerStateType.NA;
            _judgedClaim = null;
            return;
        }

        object? claim = LocalClaim(describe: false, out _);
        if (state == _judgedState && ReferenceEquals(claim, _judgedClaim))
            return; // already judged for this decision; the rows are the game's from here on

        int rows = VisibleRows(track);
        if (rows > 0)
        {
            // The band is up — the game filled it, or we did on an earlier frame. Latch so the
            // per-frame cost collapses to the compares above for the rest of this decision.
            _judgedState = state;
            _judgedClaim = claim;
            Opens[state] = Opens.TryGetValue(state, out int seenUp) ? seenUp + 1 : 1;
            if (UpStated.Add(state))
            {
                LocalClaim(describe: true, out string upPresenter);
                // HW-VERIFY: the "which gate answered" half of user report 2026-09-07 item 4. It
                // must stay at a tier the DEFAULT log level prints — a flow that is FINE has to be
                // distinguishable from a flow the instrument never saw, or the next missing flow
                // costs another round. scripts/check-hw-verify.py enforces the tier.
                VRLog.Note("Board", $"[PickTrack] decision flow OPEN in {state}: initiative band is " +
                                    $"UP with {rows} visible row(s) — nothing to repair. Gate that " +
                                    $"answered: the game's own fill (this class wrote nothing). " +
                                    $"Local presenter: {upPresenter}. Phase {PhaseManager.PhaseType}, " +
                                    "InitiativeSortedActors " +
                                    $"{GameState.InitiativeSortedActors.Count}. This is open #" +
                                    $"{Opens[state]} of {state} and the FIRST AND ONLY 'UP' line " +
                                    "for it this session — later opens of this state are counted " +
                                    "silently and the running total is printed by the next DOWN " +
                                    "line for it; a DOWN verdict is never capped.");
            }
            return;
        }
        if (Time.unscaledTime < _nextTryAt)
            return;
        _nextTryAt = Time.unscaledTime + RetrySeconds;

        // The game's own method dereferences ScenarioManager.Scenario BEFORE its own null check
        // (InitiativeTrack.cs:593 uses ScenarioManager.Scenario.HasActor, the guard is only at
        // :599) and Choreographer.s_Choreographer unconditionally at :606 — so both are OUR
        // preconditions, not its.
        if (chor == null || ScenarioManager.Scenario == null)
            return;

        Actors.Clear();
        int players = Collect(chor.m_ClientPlayers);
        int monsters = Collect(chor.ClientMonsterObjects);
        if (Actors.Count == 0)
            return; // nothing resolvable yet — the retry above comes back in half a second

        track.UpdateInitiativeTrack(Actors, playersSelectable: true, enemiesSelectable: false,
                                    selectActor: false);
        int after = VisibleRows(track);
        // LATCH EVEN ON A FAILURE. The retry above exists for the state where the actor OBJECTS are
        // not resolvable yet — and that state returns above, before the call, without logging. Once
        // the game's own method has actually RUN and still produced nothing, running it again half a
        // second later will produce nothing again; retrying would only turn one honest diagnosis
        // into a line every 0.5 s for the whole decision.
        _judgedState = state;
        _judgedClaim = claim;
        Opens[state] = Opens.TryGetValue(state, out int seen) ? seen + 1 : 1;
        LocalClaim(describe: true, out string presenter);

        // THE FALSIFIER. It prints the two numbers that can convict this class — the row count
        // BEFORE and AFTER — plus the inputs the fill was made from and the wait state that let it
        // run. A line that says "0 → 0" means the call ran and produced nothing (the actor list was
        // filtered away by the game's own IsHeroSummon / IsPropActor / Scenario.HasActor / dedupe
        // tests at InitiativeTrack.cs:591-598) and the diagnosis in this file's header is WRONG. A
        // band still blank with NO line at all means the trigger never fired: the wait state the
        // Choreographer was actually parked in is then the one term to add to IsDecisionWait, and
        // it is one enum member rather than another round of archaeology.
        // HW-VERIFY: this is the answer-bearing line of user report 2026-09-07 item 4 ("Bei allen
        // Flows soll die Initiativreihenfolge sichtbar sein"). It must stay at a tier the DEFAULT
        // log level prints; scripts/check-hw-verify.py enforces it.
        VRLog.Note("Board", $"[PickTrack] decision flow OPEN in {state} (open #{Opens[state]} of " +
                            "this state this session): initiative band was DOWN — " +
                            $"rows {rows} → {after} after this class refilled it. Gate that " +
                            $"answered: IsDecisionWait({state}) on Choreographer.m_WaitState, which " +
                            "every client sets whether or not it owns the decision — the card hand " +
                            "decides NOTHING here any more, and that CARD-ONLY trigger is exactly " +
                            "what hid this flow before ModBuild 475. Local presenter: " +
                            $"{presenter}. Phase {PhaseManager.PhaseType}, InitiativeSortedActors " +
                            $"{GameState.InitiativeSortedActors.Count} (0 = nobody has chosen cards " +
                            "yet, so the game's generic refresh at Choreographer.cs:12534 is blocked " +
                            $"by its own precondition). Refilled from {players} client player(s) + " +
                            $"{monsters} monster object(s) with the game's own UpdateInitiativeTrack, " +
                            "playersSelectable=true, selectActor=FALSE (selecting would run " +
                            "InitiativeTrackPlayerAvatar.Select → CardsHandManager.SwitchHand and " +
                            "could switch the hand out from under the open decision). Local " +
                            "presentation only — no game state written, nothing on the wire; every " +
                            "seat reaches this independently and the mirrored boards clone the " +
                            "widget this repaired.");
    }

    /// <summary>Map a client actor-object list onto <see cref="Actors"/>, skipping anything whose
    /// <c>ActorBehaviour</c> is not resolvable yet. The game's own overload does the same mapping
    /// (InitiativeTrack.cs:704-712) but without the null test, which is why it must not be called
    /// during a scenario load.</summary>
    private static int Collect(List<GameObject>? objects)
    {
        if (objects == null)
            return 0;
        int added = 0;
        for (int i = 0; i < objects.Count; i++)
        {
            GameObject go = objects[i];
            if (go == null)
                continue;
            ActorBehaviour behaviour = ActorBehaviour.GetActorBehaviour(go);
            CActor? actor = behaviour != null ? behaviour.m_Actor : null;
            if (actor == null)
                continue;
            Actors.Add(actor);
            added++;
        }
        return added;
    }

    /// <summary>The per-frame half, isolated. A Harmony postfix that throws takes the game's own
    /// method down with it, and a cosmetic row band is never worth that. No closure: this runs in a
    /// Unity <c>Update</c> and a lambda per frame is a steady-state allocation.</summary>
    internal static void TickGuarded(InitiativeTrack? track)
    {
        try
        {
            Tick(track);
        }
        catch (System.Exception e)
        {
            _judgedState = Choreographer.ChoreographerStateType.NA;
            _judgedClaim = null;
            _nextTryAt = Time.unscaledTime + 60f; // back right off; do not retry-storm on a bad state
            if (_reportedThrow)
                return;
            _reportedThrow = true;
            // A SELF-DISARM IS VRLog's OWN DEFINITION OF Error (2026-09 refactor, F-32): the
            // feature backs off for 60 s. At Warn it appeared in no shipped log. Once.
            VRLog.Error("Board", "[PickTrack] the decision-flow track refill threw and is backing off " +
                                "for 60 s — the initiative band stays exactly as the game left it " +
                                $"and nothing was written. {e}");
        }
    }
}
