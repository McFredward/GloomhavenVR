using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// THE INITIATIVE TRACK IS EMPTY DURING A FORCED CARD PICK, AND THE GAME NEVER FILLED IT.
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
/// <para>THE REMEDY IS THE GAME'S OWN METHOD WITH THE GAME'S OWN ARGUMENTS. When a forced pick is
/// open and the track has zero visible rows, this asks
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
/// </summary>
internal static class PickPhaseInitiativeTrack
{
    /// <summary>The card-hand modes this repair covers: the FORCED picks the game raises through
    /// <c>SelectLoseCards</c> (Choreographer.cs:7877 maps the ability type to exactly these two).
    /// Recover/card-limit picks are deliberately absent — they are raised by other messages and
    /// have not been observed with a blank track, and a repair that fires where nothing is broken
    /// is a repair that will one day fight the game.</summary>
    private static bool IsForcedPickMode(CardHandMode mode) =>
        mode == CardHandMode.DiscardCard || mode == CardHandMode.LoseCard;

    /// <summary>Re-arm interval. A fill that did NOT take (the pool was mid-build, an actor object
    /// was not resolvable yet) must be retried, but never per frame — <c>UpdateInitiativeTrack</c>
    /// allocates. 0.5 s is invisible to a player reaching for a card and bounds the cost at two
    /// tries a second in the pathological case.</summary>
    private const float RetrySeconds = 0.5f;

    private static float _nextTryAt;

    /// <summary>The hand identity the last successful fill was made for (RuntimeHelpers-style
    /// reference identity via <see cref="object.ReferenceEquals"/>), so a SECOND character's forced
    /// pick re-fires rather than being swallowed by the first one's latch.</summary>
    private static object? _filledForHand;

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
        _filledForHand = null;
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
    /// Three compares in the steady state (no forced pick open ⇒ the mode test fails first).
    /// </summary>
    private static void Tick(InitiativeTrack? track)
    {
        if (track == null)
            return;

        CardsHandUI? hand = Cards.CardsGameApi.ActiveHand();
        if (hand == null || !IsForcedPickMode(Cards.CardsGameApi.Mode(hand)))
        {
            // Not our situation any more — drop the latch so the NEXT forced pick (the second
            // character's, a mid-scenario discard ability) is judged from scratch.
            _filledForHand = null;
            return;
        }
        if (ReferenceEquals(_filledForHand, hand))
            return; // already filled for this pick; the rows are the game's from here on

        int rows = VisibleRows(track);
        if (rows > 0)
        {
            // The game filled it (or we did on an earlier frame). Latch so the per-frame cost
            // collapses to the compares above for the rest of this pick.
            _filledForHand = hand;
            return;
        }
        if (Time.unscaledTime < _nextTryAt)
            return;
        _nextTryAt = Time.unscaledTime + RetrySeconds;

        // The game's own method dereferences ScenarioManager.Scenario BEFORE its own null check
        // (InitiativeTrack.cs:593 uses ScenarioManager.Scenario.HasActor, the guard is only at
        // :599) and Choreographer.s_Choreographer unconditionally at :606 — so both are OUR
        // preconditions, not its.
        Choreographer chor = Choreographer.s_Choreographer;
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
        // into a line every 0.5 s for the whole pick.
        _filledForHand = hand;

        // THE FALSIFIER. It prints the two numbers that can convict this class — the row count
        // BEFORE and AFTER — plus the inputs the fill was made from. A line that says "0 → 0" means
        // the call ran and produced nothing (the actor list was filtered away by the game's own
        // IsHeroSummon / IsPropActor / Scenario.HasActor / dedupe tests at InitiativeTrack.cs:
        // 591-598) and the diagnosis in this file's header is WRONG. A line that never appears at
        // all while the band is blank means the trigger never fired, and the mode/rows it would
        // have printed are the first thing to check.
        VRLog.Info("Board", $"[PickTrack] initiative rows {rows} → {after} for a forced " +
                            $"{Cards.CardsGameApi.Mode(hand)} pick (owner " +
                            $"'{CharacterFocus.Describe(hand.PlayerActor)}', phase " +
                            $"{PhaseManager.PhaseType}): the game presents this pick through " +
                            "CMessageData.SelectLoseCards, whose handler never refreshes the track " +
                            "(Choreographer.cs:7854-7910) and whose message is not on the generic " +
                            "refresh whitelist (:12534) — so the band stayed blank and the rows " +
                            "could not be clicked to switch character. Refilled with the game's own " +
                            $"UpdateInitiativeTrack from {players} client player(s) + {monsters} " +
                            "monster object(s), playersSelectable=true, selectActor=FALSE (selecting " +
                            "would run InitiativeTrackPlayerAvatar.Select → CardsHandManager." +
                            "SwitchHand and could switch the hand out from under the open pick). " +
                            "Local presentation only — no game state written, nothing on the wire.");
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
            _filledForHand = null;
            _nextTryAt = Time.unscaledTime + 60f; // back right off; do not retry-storm on a bad state
            if (_reportedThrow)
                return;
            _reportedThrow = true;
            VRLog.Warn("Board", "[PickTrack] the forced-pick track refill threw and is backing off " +
                                "for 60 s — the initiative band stays exactly as the game left it " +
                                $"and nothing was written. {e}");
        }
    }
}
