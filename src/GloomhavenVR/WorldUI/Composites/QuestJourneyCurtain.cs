using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE QUEST LIST GOES AT THE COMMIT, NOT AT THE STORY — AND THE GAME ITSELF SAYS WHEN THE COMMIT
/// HAPPENED.
///
/// <para><b>USER REQUEST (2026-08-23), verbatim:</b> <i>"Die Liste der Quests das Fenster soll schon
/// direkt zum Beginn des Point of no returns verschwinden, also in dem moment in dem eine Quest
/// bestätigt wird und die Animation 'der Reise' beginnt."</i></para>
///
/// <para><b>WHAT SHIPPED BEFORE THIS, AND WHY IT IS LATE.</b> <c>StoryComposite</c>'s ROW 0 curtain
/// rises on <c>MapStoryController.isVisibleOtherUI == false</c> — the game hiding its own UI for the
/// quest-start MESSAGE. In the ModBuild 237 hardware log that edge is at <c>LogOutput.log:3135</c>,
/// and the quest confirm is hundreds of lines earlier: <c>'UI Quest Popup'</c> floats at :2760 and
/// <c>'New Party display'</c> is released at :2847 because the game itself hid it. So for the whole
/// journey — the fade, the token move, the arrival — <c>'Quest Log Manager'</c> was still hanging in
/// the room. THAT is the interval this class covers, and nothing about the curtain changes.</para>
///
/// <para>=====================================================================================
/// THE GAME FACT THIS IS KEYED ON
/// =====================================================================================</para>
///
/// <para><b><c>MapChoreographer.OnMoveClick(MapLocation)</c> —
/// decompiled/GH.Runtime/MapChoreographer.cs:1431 — IS THE COMMIT.</b> It is the single method every
/// confirm path funnels into: the travel button
/// <c>AdventureMapUIManager.OnTravelButtonClick → ConfirmTravel</c> at :200/:240, the gamepad long
/// press <c>TravelMapState.TravelLongConfirm</c> at TravelMapState.cs:51-57, the second click on an
/// already-selected location at AdventureMapUIManager.cs:343-350, and the multiplayer all-ready
/// callback at MapChoreographer.cs:3146/:3591. Three consecutive lines of its body are what this
/// class reads:</para>
/// <list type="number">
/// <item><b>:1448</b> <c>AdventureMapUIManager.HideTravelOption()</c>, which runs
/// <c>ResetLocationToTravel()</c> and sets <c>locationToTravel = null</c>
/// (AdventureMapUIManager.cs:217-224, :438-442). Read through the public
/// <c>AdventureMapUIManager.LocationToTravel</c> (:70).</item>
/// <item><b>:1449</b> <c>AdventureMapUIManager.LockOptionsInteraction(locked: true, this)</c>, which
/// puts the choreographer into <c>lockInteractionRequests</c> (AdventureMapUIManager.cs:363-386).
/// Read through the public <c>AdventureMapUIManager.IsLocked</c> (:72).</item>
/// <item><b>:1450</b> <c>QuestManager.OnPartyMove()</c>, whose whole body is
/// <c>questPopups.HideAll(); questLog.HideLogScreen();</c> (QuestManager.cs:188-192) — i.e.
/// <b>the game closes the quest list window itself, at this instant.</b> Read through the window's
/// own <c>UIWindow.IsOpen</c>.</item>
/// </list>
///
/// <para><b>SO THE MOD IS NOT INVENTING A MOMENT. The game hides the quest list at the confirm and
/// the mod's map-room PERMANENCE ruling is the only reason it is still on screen</b>
/// (<c>ModalFallback.IsQuestLogWindow</c> / <c>IsMapRoomPermanent</c>, and the float is kept alive by
/// <c>WindowPanel.Sticky</c> regardless of what the game did). This class withholds that float for
/// the COMMITTED INTERVAL and hands it straight back when the interval ends.
/// <b>ModBuild 243 corrected the second half of that sentence</b>: up to 242 it read "hands it
/// straight back the moment the game re-opens it", and the mod's own map-room deselect turned out to
/// be able to re-open it at an instant the flat game seals all map input. See the ModBuild 243
/// section below.</para>
///
/// <para><b>WHY EACH ALTERNATIVE IS WORSE, ALL FROM THE SAME DECOMPILED READ.</b></para>
/// <list type="bullet">
/// <item><c>MapChoreographer.MovingToLocation</c> (:186, the public journey flag) turns on in
/// <c>StartMove</c> (:1667-1674) only after <c>CMove_MapDLLMessage</c> has round-tripped through the
/// rule library and come back as <c>CStartMoving_MapClientMessage</c> (:786-790) — several frames
/// LATE, which is the half of the request this class exists to fix. It is also nulled again in
/// <c>CompleteMoveCallback</c> (:2176), so it cannot hold anything across the story.</item>
/// <item><c>PartyToken.IsMoving</c> (PartyToken.cs:18) is the animation itself, and this project
/// already knows the game TELEPORTS the token behind a full-screen fade
/// [[the-flat-screen-hid-it]]. It also has no accessor — <c>MapChoreographer.m_PartyToken</c> is
/// private — so reading it means a scene sweep [[findobjectsoftype-is-the-default-suspect]].</item>
/// <item><c>GameActionType.SelectQuest</c> is sent only by
/// <c>UIMapMultiplayerController.ConfirmSelectedLocation</c> (:259-267): multiplayer only. Its
/// single-player twin <c>GameActionType.MoveToNewNode</c> is sent inside
/// <c>if (FFSNetwork.IsHost)</c> at MapChoreographer.cs:1441, so a CLIENT never sees it — and this
/// mod may not drive or listen on a host-authoritative path for a decision that is local
/// presentation.</item>
/// <item><c>AdventureMapUIManager.IsLocked</c> ALONE is not the confirm: it is a refcount over a
/// dictionary and eight other callers write it — MapTransitioner.cs:17, UIDistributeRewardManager
/// .cs:88, MapFTUEManager.cs:117, UIMapMultiplayerController.cs:338 and MapChoreographer.cs:2428
/// among them. That is exactly why the edge below is a THREE-TERM measurement and not one flag: the
/// multiplayer ready-up locks while a location is still SELECTED, so
/// <c>LocationToTravel == null</c> is what separates the commit from every other lock.</item>
/// <item>A Harmony patch on <c>OnMoveClick</c> would be the most precise hook of all, and it is not
/// used for two reasons. It is a private method, so the patch is a name match against a decompile
/// this round cannot run; and an EDGE that fires once has no way to notice it was wrong, whereas the
/// level below is re-measured every tick and lapses by itself
/// [[a-rule-read-too-late-never-runs]].</item>
/// </list>
///
/// <para>=====================================================================================
/// WHY THIS IS A ROW OF ITS OWN AND NOT A SECOND RISING EDGE ON THE CURTAIN
/// =====================================================================================</para>
///
/// <para><c>StoryComposite.RaiseCurtain</c> FREEZES the set of everything the mod is floating at its
/// edge. At the confirm that set contains <c>'New Party display'</c> — the log has it floated at
/// :2051 and released at :2847, i.e. the game hides it INSIDE <c>OnMoveClick</c> itself
/// (MapChoreographer.cs:1455, <c>PartyDisplay.Hide(this)</c>) — and <c>CurtainRefuses</c> tests by
/// REFERENCE against a Singleton that comes back as the same instance. Moving the curtain's edge
/// earlier would therefore freeze the Character-UI into the member set and refuse it again at :3391,
/// when it re-opens carrying the battle-goal picker and the ENTER DUNGEON button. That is the
/// ModBuild 234 deadlock rebuilt from parts. This class refuses ONE window instead, it is a window
/// with no advancing control on it, and the honesty question the curtain has to ask — "is anything
/// still on screen?" — is answered trivially because everything else is untouched.</para>
///
/// <para><b>NOTHING IS WRITTEN TO THE GAME.</b> No <c>Show</c>, no <c>Hide</c>, no <c>Escape</c>, no
/// <c>SetActive</c>, no <c>CanvasGroup</c>. The float is withheld and the window keeps its ordinary
/// 2D rendering on 'Map Canvas', which the 3D map room does not draw.</para>
///
/// <para><b>MULTIPLAYER: nothing here goes on the wire.</b> Every term is a local read of a local
/// singleton and the verdict decides which local GameObject a local uGUI subtree is drawn under. Two
/// players may legitimately disagree about all of it.</para>
///
/// <para>=====================================================================================
/// ModBuild 243 — THE LEVEL LET GO ON THE ONE TERM THE MOD ITSELF CAN MOVE
/// =====================================================================================</para>
///
/// <para><b>USER REPORT (2026-08-24, ModBuild 242), verbatim:</b> <i>"Nach dem 'Point of no Return'
/// soll das Fenster mit der Questliste dauerhaft weg sein. Wenn ich über eins der Symbole hovere im
/// Test ist es wieder gespawnt."</i></para>
///
/// <para><b>WHICH WINDOW IT ACTUALLY WAS, NAMED FROM THE LOG AND NOT FROM THE CODE.</b> It is the
/// quest log itself, not a hover card. The ModBuild 242 hardware log's
/// <c>WINDOW IDENTITY</c> line (LogOutput.log:1187) reads
/// <c>'Quest Log Manager' (ID None): path Map Canvas/Quest UI/Quest Log Manager; components
/// [RectTransform, CanvasRenderer, QuestLogManager, CanvasGroup, UIWindow,
/// ControllerInputEscapableArea]</c> — no <c>UIQuestPreviewPopup</c> and no <c>UILocalTooltip</c>, so
/// <see cref="ModalFallback.IsMapRoomHoverCard"/> says NO for it. The map's hover card is the
/// SIBLING <c>'UI Quest Preview Popup'</c>, and the log has that one converting twelve separate times
/// (:6060-:6342) while the quest log converted exactly once (:5982). The two are genuinely
/// confusable — <see cref="ModalFallback.IsMapRoomHoverCard"/>'s own doc records a build in which a
/// window CONTAINING tooltips was flown over the map as one — so the identity is settled here from
/// the component list, which is the log's, not from the name.</para>
///
/// <para><b>IT WAS A FRESH CONVERSION, NOT A REVIVED FLOAT.</b> :5982
/// <c>Converted 'Modal_Quest Log Manager' to world space (390x880 px)</c>, with a new arc-seat claim
/// and a new one-shot facing on the lines around it. The float this class withdrew at :4562
/// (<c>FLOAT RELEASED ON REFUSAL … PastThePointOfNoReturn (ROW 0b, the quest-journey curtain)</c>)
/// was gone; the catch-all built a new one. So no held-out membership could have caught it and no
/// frozen set was involved. <b>The refusal simply was not standing any more.</b></para>
///
/// <para><b>WHY IT WAS NOT STANDING — THE WHOLE BUG IN ONE LINE.</b> :5978
/// <c>QUEST JOURNEY CURTAIN LAPSED — THE GAME RE-OPENED THE QUEST LIST ITSELF</c>, which is the
/// <c>!_member.IsOpen</c> clause of the standing branch below. One line earlier, :5977,
/// <c>MAP ROOM location DESELECTED 'CampaignMapLocation Variant(Clone)' — Right trigger pulled with
/// the ray on the TABLE</c>. That is <c>MapLocationInteractor.TickDeselect</c> calling the game's own
/// <c>MapLocation.Deselect()</c>, and the chain from there is entirely the game's:
/// <c>MapLocation.Deselect</c> (MapLocation.cs:670) → <c>m_OnClickAction(this, active: false)</c> =
/// <c>MapChoreographer.OnMapLocationSelect(loc, active: false)</c> (:1220) → :1288
/// <c>QuestManager.OnMapLocationQuestSelected(quest, false)</c> → QuestManager.cs:165
/// <c>ShowLogScreen(this)</c> → :212 <c>questLog.ShowLogScreen()</c> →
/// QuestLogManager.cs:311-313 <c>window.Show(instant)</c>. <c>UIWindow.IsOpen</c> is
/// <c>m_CurrentVisualState == VisualState.Shown</c> (UIWindow.cs:317), so the window is open again
/// and this class handed it straight back — 318.7 s into a point of no return that was still
/// standing. The falsifier said so in the same breath at :5999:
/// <c>IsLocked=True … LocationToTravel=null again … UIWindow.IsOpen=True … refusal is standing=False
/// and FloatRefusalTable.Refuses says False</c>.</para>
///
/// <para><b>AND IT COULD NOT RE-RAISE EITHER</b>, which is why one deselect cost the whole interval:
/// the rising edge's own guard is <c>if (log == null || log.IsOpen) return</c> — "the game has it
/// open and it must stay". Both halves of the level read the same term, so once the game re-opened
/// the window the class was out of the game for good.</para>
///
/// <para><b>WHY <c>IsOpen</c> IS THE WRONG TERM DURING THE COMMITTED INTERVAL, AND THIS IS THE PART
/// THAT MAKES THE FIX HONEST RATHER THAN A FIGHT WITH THE GAME.</b>
/// <c>AdventureMapUIManager.LockOptionsInteraction</c> (:363-386) ends with
/// <c>lockMapInteractionMask.SetActive(lockInteractionRequests.Count &gt; 0)</c> — a full-screen
/// raycast blocker. On the flat screen, from <c>OnMoveClick</c>:1449 until the lock is released, NO
/// MapLocation can be hovered, selected or deselected at all, so <c>OnMapLocationQuestSelected(…,
/// false)</c> is UNREACHABLE and the quest list the game closed at :1450 stays closed. In VR the
/// mod's <c>MapLocationInteractor</c> dispatches onto the <c>MapLocation</c> objects directly and
/// never goes through that mask, so the VR player can reach a deselect the flat player cannot. The
/// re-open is therefore not the game asking for the window back; it is a side effect of an input the
/// flat game does not permit at this moment. Refusing to FLOAT it is not overriding the game — and
/// nothing is written to it either way.</para>
///
/// <para><b>THE SHAPE OF THE FIX IS THE ONE THE PROJECT ALREADY RULED ON</b>
/// [[gate-outliving-its-edge]]: <b>one named window, for a bounded interval, never a blanket
/// exclusion.</b> The subject is still the single frozen <see cref="_member"/> instance, the
/// loadout screen / story window / continue button are untouched, and the interval is still the
/// game's own. What changes is exactly one thing: <b>while the interval stands, the game re-opening
/// the window no longer drops the refusal.</b> The <c>IsOpen</c> drop is kept for the case it was
/// written for — the interval is over and the game wants its window back — and the rising edge gains
/// a narrow re-raise for a log that is open while
/// <see cref="StoryComposite.PointOfNoReturn"/> stands and this class has ALREADY witnessed the
/// commit once in this interval (<see cref="_cycles"/> &gt; 0). The first raise still demands
/// <c>!IsOpen</c>, i.e. the game's own <c>OnPartyMove</c> close, so the three-term measurement that
/// separates this lock from the seven other lockers is not weakened.</para>
///
/// <para><b>WHEN THE REFUSAL DISARMS, and it is deliberately NOT the term whose absence was the
/// bug.</b> It ends when <c>locked || StoryComposite.PointOfNoReturn</c> has been false for
/// <see cref="JourneyBridgeSeconds"/> — i.e. the game has unlocked the map options AND the loadout
/// screen is gone AND no story message owns the screen. In practice that is the scenario load
/// (<c>MapRoomDriver.Active</c> goes false, which drops it outright), or the player backing out to an
/// unlocked map. It also still ends the moment the window instance is destroyed, and the deadlock
/// floor can still <see cref="Lift"/> it. It does NOT end on <c>IsOpen</c> while the interval stands,
/// because that is the term the mod's own deselect moves.</para>
///
/// <para><b>ALTERNATIVES REJECTED.</b>
/// (a) <i>A blanket "after the point of no return refuse everything"</i> — that is
/// [[gate-outliving-its-edge]] verbatim, and it would refuse the loadout screen, the story window and
/// the ENTER DUNGEON button, all of which MUST appear after this edge.
/// (b) <i>Suppress the mod's deselect while the map is locked</i> — the right long-term fix and it
/// is NOT in this lane; <c>MapLocationInteractor.TickDeselect</c> belongs to the map-room lane. It is
/// written up in <c>.planning/debug/laneP-out-of-lane.diff</c>. It is also the WIDER change: it
/// would stop the mod driving ANY map-location input while the game has the map masked, which is a
/// ruling about input, not about this window.
/// (c) <i>Call <c>questLog.HideLogScreen()</c> when the game re-opens it</i> — forbidden. Presentation
/// code never writes game state; the mod may refuse to FLOAT a window and may not close the game's
/// own.
/// (d) <i>Freeze the float set at the edge, the way the story curtain does</i> — a set frozen at the
/// edge cannot contain a window that is converted afresh 318 s later, which is precisely what
/// happened here. A reference to ONE named instance can.</para>
///
/// <para>GREP: <c>QUEST JOURNEY CURTAIN RAISED</c> / <c>LAPSED</c> — the edges.
/// <c>QUEST JOURNEY CURTAIN AUDIT</c> — the ModBuild 243 falsifier: the point-of-no-return terms,
/// whether the refusal is ARMED, how many cadence samples it has WITHHELD on, and the names of the
/// windows that floated while it was armed. An unarmed refusal and an armed one that never fires
/// print different lines on purpose.
/// <c>QUEST LOG GONE AT THE POINT OF NO RETURN: CONFIRMED</c> — the request is met.
/// <c>… NOT ACHIEVED</c> — it is not, with the failing term named.</para>
/// </summary>
internal static class QuestJourneyCurtain
{
    private const string Scope = "WorldUI";

    /// <summary>How long the refusal may stand with neither the game's own map lock nor the
    /// point-of-no-return gate holding it up. <c>StoryComposite.CurtainBridgeSeconds</c>'s number and
    /// its argument: the ModBuild 237 log's own seam between a chain ending and the next level rising
    /// is 28 lines, twenty seconds is two orders of magnitude of headroom, and it is a HARD CEILING
    /// rather than a timeout to tune. Past it the refusal lapses and the quest log comes back a few
    /// seconds early, which is the cheap direction to be wrong in.</summary>
    private const float JourneyBridgeSeconds = 20f;

    /// <summary>How many times this refusal may be raised before it stops asking, and the number is
    /// DERIVED rather than chosen — <c>StoryComposite.MaxCurtainCycles</c>'s derivation verbatim.
    /// <c>ModalFallback</c>'s catch-all churn fuse session-suppresses a window's NAME on its 4th
    /// enrolment inside 60 s and a REFUSED window is never counted, so the only counted events in this
    /// window's life are its FIRST float and one re-float per refusal that FALLS: 1 + 2 = 3 is at the
    /// fuse's limit and never over it.</summary>
    private const int MaxJourneyCycles = 2;

    /// <summary>Cap on the falsifier's lines per session-level state. The verdict is change-gated as
    /// well, so this only bites if the state genuinely oscillates — in which case six lines are the
    /// diagnosis and a seventh is noise. <c>StoryComposite.MaxOneWindowReports</c>'s number.</summary>
    private const int MaxJourneyReports = 6;

    // ---- the level -------------------------------------------------------------------------------

    private static bool _standing;
    private static bool _lifted;
    private static int _cycles;
    private static bool _capReported;
    private static float _heldAt;

    /// <summary>The quest-log window INSTANCE frozen at the rising edge. The refusal is a reference
    /// compare against this and nothing else, so a quest log that opens later — or a different
    /// instance after a scene load — can never be caught by a level that is still standing. Kept
    /// after the level falls so <c>FloatRefusalTable</c>'s lapse warning can name the right
    /// claimant.</summary>
    private static UIWindow? _member;

    private static string _why =
        "the quest-journey curtain has never been raised in this session";

    // ---- the edge's own inputs -------------------------------------------------------------------

    /// <summary>Has a map location been SELECTED since the last time this level rested? The term that
    /// separates <c>OnMoveClick</c>'s lock from the seven other callers of
    /// <c>LockOptionsInteraction(true, …)</c>: every confirm path reaches
    /// <c>AdventureMapUIManager.ConfirmTravel</c>, which returns immediately unless
    /// <c>locationToTravel</c> is non-null (:230-243), so a commit is always preceded by a
    /// selection.</summary>
    private static bool _selectionSeen;
    private static string _selectionName = "<none>";

    /// <summary>When the commit was measured — printed by the falsifier, never read as a policy
    /// input.</summary>
    private static int _edgeFrame = -1;
    private static float _edgeTime = -1f;

    // ---- the falsifier's change gate --------------------------------------------------------------

    private static string _verdict = string.Empty;
    private static int _reports;
    private static float _nextReportAt;

    /// <summary><c>LoadoutConfirmPark.AnchorRefreshIntervalSeconds</c>'s number and argument, applied
    /// to the falsifier's float-set walk.</summary>
    private const float ReportIntervalSeconds = 0.2f;

    private static readonly List<UIWindow> FloatScratch = new(8);

    // ---- the reads FloatRefusalTable makes --------------------------------------------------------

    /// <summary>
    /// IS THIS WINDOW BEING WITHHELD BY THE QUEST-JOURNEY CURTAIN RIGHT NOW? A PURE read with no
    /// state and no logging, because <c>FloatRefusalTable.Refuses</c> is re-entered several times per
    /// tick from a recursive ancestor walk and every caller must get the same answer.
    /// </summary>
    internal static bool Refuses(UIWindow? window) =>
        _standing && !_lifted && window != null && ReferenceEquals(window, _member);

    /// <summary>Was this window the SUBJECT of the refusal, whether or not it still stands? Asked by
    /// <c>FloatRefusalTable.LastWordsFor</c> so a lapse warning names the claim that actually held the
    /// window rather than a fixed one — the ModBuild 235 instrument defect, which this class must not
    /// re-introduce by being a fourth unnamed row.</summary>
    internal static bool WasSubject(UIWindow? window) =>
        window != null && ReferenceEquals(window, _member);

    /// <summary>The curtain's own words, printed verbatim by the refusal line and by the lapse
    /// warning. Never null.</summary>
    internal static string Why => _why;

    /// <summary>Is the level standing this instant? For another subsystem's log line only.</summary>
    internal static bool Standing => _standing && !_lifted;

    /// <summary>
    /// STAND DOWN BECAUSE SOMEBODY ELSE MEASURED A DEADLOCK. Called from
    /// <c>StoryComposite.TickDeadlockFloor</c> with the floor's own reason. "Lift the verdict, keep
    /// the count" — the cycle counter is deliberately not touched, so a later round can still see
    /// that the rule fired and how often.
    /// </summary>
    internal static void Lift(string why)
    {
        if (_lifted)
            return;
        _lifted = true;
        VRLog.Warn(Scope, "QUEST JOURNEY CURTAIN LIFTED BY THE DEADLOCK FLOOR — " + why
                          + $". '{(_member != null ? _member.name : "<no member>")}' floats again from "
                          + "this tick with everything on it. Nothing has to be undone, because nothing "
                          + "was ever written to the game: this class only ever withheld a float. THE "
                          + $"COUNT IS KEPT: {_cycles} of {MaxJourneyCycles} cycle(s) used.");
    }

    // ---- the per-tick step ------------------------------------------------------------------------

    /// <summary>
    /// Called once per <c>ModalFallback.Tick</c>, as the FIRST line of <c>StoryComposite.Tick</c> —
    /// i.e. from the first line of <c>TickWindowLiveness</c>, before the release loop and long before
    /// the convert pass that asks the refusal table. That order is what makes the withdrawal cost one
    /// edge and not a flap: the level is settled before anything is asked about it.
    ///
    /// <para>EVERYTHING HERE IS LEVEL-TRIGGERED except the member freeze, which is a reference taken
    /// once at the rising edge and never appended to. There is no session verdict and no suppression
    /// list.</para>
    /// </summary>
    internal static void Tick()
    {
        try
        {
            TickCore();
        }
        catch (System.Exception e)
        {
            // The fail direction is to let go. A presentation refusal is never worth a throw reaching
            // the modal tick, and a refusal held by a class that is throwing is a suppression with no
            // owner — the one shape this whole area of the code exists to be incapable of.
            VRLog.Warn(Scope, $"QUEST JOURNEY CURTAIN: the level threw ({e.GetType().Name}) — the "
                              + "refusal is dropped and the quest log floats again from this tick. "
                              + "Nothing was written to the game.");
            Drop("the level threw");
        }
    }

    private static void TickCore()
    {
        if (!MapRoomDriver.Active)
        {
            Drop("the 3D map room stood down");
            _selectionSeen = false;
            _cycles = 0;
            _capReported = false;
            _lifted = false;
            ResetReport();
            return;
        }

        AdventureMapUIManager? map = MapManager();
        bool locked = map != null && map.IsLocked;
        MapLocation? selected = map != null ? map.LocationToTravel : null;
        bool selectionPending = selected != null;
        if (selectionPending)
        {
            _selectionSeen = true;
            _selectionName = selected!.name;
        }

        // THE HOLD'S TWO REAL REASONS, and the bridge clock is reset by those and never by this class
        // itself. A latch that is its own reason to latch is not a level at all —
        // StoryComposite.TickCurtain's rule, for its reason.
        bool realReason = locked || StoryComposite.PointOfNoReturn;
        if (realReason)
            _heldAt = Time.unscaledTime;

        if (_standing)
        {
            bool bridging = Time.unscaledTime - _heldAt <= JourneyBridgeSeconds;

            // ModBuild 243 — THE ORDER OF THESE THREE CLAUSES IS THE FIX, AND THE MIDDLE ONE'S
            // `!realReason` IS THE WHOLE OF IT. Up to 242 the second clause had no such guard, so
            // ONE call to the game's own MapLocation.Deselect() — which runs
            // QuestManager.OnMapLocationQuestSelected(quest, false) → ShowLogScreen → window.Show()
            // — handed the quest list back in the middle of a point of no return that was still
            // standing (LogOutput.log:5977 → :5978). See the class doc's ModBuild 243 section for
            // why a re-open reached that way is not the game asking for its window: the flat game
            // cannot reach a deselect at all while lockMapInteractionMask is up.
            if (_member == null)
            {
                Drop("the quest log window is gone");
            }
            else if (_member.IsOpen && !realReason)
            {
                Drop("THE GAME RE-OPENED THE QUEST LIST ITSELF and the committed interval is over "
                     + "— AdventureMapUIManager.IsLocked is false and StoryComposite.PointOfNoReturn "
                     + "is false, so nothing is holding this level up and there is nothing left to "
                     + "withhold: a window the game is showing OUTSIDE the committed interval must "
                     + "never be one the mod is hiding");
            }
            else if (!realReason && !bridging)
            {
                Drop($"the game unlocked the map options and no point-of-no-return level followed "
                     + $"within {JourneyBridgeSeconds:F0} s, so this was not a quest commit after all");
            }
            Report(map, locked, selectionPending);
            return;
        }

        // ---- the rising edge -------------------------------------------------------------------
        // All three terms are the game's own and none of them is a value this mod writes.
        bool committed = _selectionSeen && !selectionPending && locked;
        if (!committed || _lifted)
        {
            // RESTING: back at HQ with the map unlocked and nothing selected. Refill the budget here
            // and only here, from the game's own level rather than from a timer.
            if (!locked && !selectionPending && !StoryComposite.PointOfNoReturn)
            {
                _cycles = 0;
                _capReported = false;
                _lifted = false;
                _selectionSeen = false;
                // ModBuild 243 — and the falsifier's budget with it, so the audit's counters always
                // read "since the map was last at rest" and the next quest gets a fresh set of lines
                // instead of a spent one.
                ResetReport();
            }
            Report(map, locked, selectionPending);
            return;
        }

        UIWindow? log = FloatedQuestLog();

        // ModBuild 243 — THE RE-RAISE, AND IT IS AS NARROW AS IT CAN BE MADE.
        //
        // The FIRST raise still demands `!log.IsOpen`, because the game's own
        // QuestManager.OnPartyMove close at MapChoreographer.cs:1450 is the fourth term that
        // separates OnMoveClick's lock from the seven other callers of LockOptionsInteraction(true).
        // Giving it up would let this level raise during reward distribution or an FTUE lock.
        //
        // But once this class has WITNESSED that term in the current interval (_cycles > 0 — the
        // counter is refilled only at rest, below) and the point-of-no-return level is standing on
        // its own game terms (the loadout screen is up, or the game has hidden the rest of its UI
        // for a story message), an OPEN quest log is no longer evidence that the game wants it: it
        // is the ModBuild 242 symptom. In that state the level may take the window back.
        //
        // WHY THIS CLAUSE AND NOT A WIDER ONE: StoryComposite.PointOfNoReturn is
        // `_curtainStanding || UILoadoutManager.IsOpen`, and neither is true during reward
        // distribution, a map transition, an FTUE lock or a multiplayer ready-up — the other things
        // that write AdventureMapUIManager.IsLocked. So the widening cannot reach them.
        bool reRaise = _cycles > 0 && StoryComposite.PointOfNoReturn;
        if (log == null || (log.IsOpen && !reRaise))
        {
            // Nothing to withhold: either the mod is not floating a quest log at all, or the game has
            // it open outside a witnessed commit and it must stay. Both are ordinary, neither is an
            // edge.
            Report(map, locked, selectionPending);
            return;
        }
        if (_cycles >= MaxJourneyCycles)
        {
            if (!_capReported)
            {
                _capReported = true;
                VRLog.Warn(Scope, $"QUEST JOURNEY CURTAIN CAPPED — it has already been raised "
                                  + $"{_cycles} time(s) since the map was last unlocked and it will not "
                                  + "be raised again until it is. THE CAP IS DERIVED, NOT CHOSEN: "
                                  + "ModalFallback's catch-all churn fuse suppresses a window's NAME "
                                  + "for the whole session on its 4th enrolment inside 60 s, a REFUSED "
                                  + "window is never counted, and each refusal that FALLS costs its "
                                  + $"subject exactly one re-float — so {MaxJourneyCycles} cycles plus "
                                  + "the first float is 3, at the fuse's limit. FROM HERE THE QUEST LOG "
                                  + "FLOATS NORMALLY, which is the ModBuild 237 presentation: it stands "
                                  + "beside the story box until the ROW 0 curtain takes it. Ugly, and "
                                  + "NOT a deadlock. IF YOU ARE READING THIS IN A HARDWARE LOG this "
                                  + "level is flapping — grep QUEST JOURNEY CURTAIN LAPSED for what "
                                  + "kept dropping it.");
            }
            Report(map, locked, selectionPending);
            return;
        }
        Raise(log, map, reRaise);
        Report(map, locked, selectionPending);
    }

    private static void Raise(UIWindow log, AdventureMapUIManager? map, bool reRaise)
    {
        _member = log;
        _standing = true;
        _lifted = false;
        _cycles++;
        _edgeFrame = Time.frameCount;
        _edgeTime = Time.unscaledTime;
        _heldAt = Time.unscaledTime;
        _why = "the party has COMMITTED to a quest and the journey has begun — "
               + "MapChoreographer.OnMoveClick has run HideTravelOption at :1448 "
               + "(AdventureMapUIManager.LocationToTravel is null again), "
               + "LockOptionsInteraction(locked: true, this) at :1449 "
               + "(AdventureMapUIManager.IsLocked is true) and QuestManager.OnPartyMove at :1450, "
               + "whose whole body is questPopups.HideAll and questLog.HideLogScreen. So THE GAME HAS "
               + "CLOSED THIS WINDOW ITSELF and the only reason it is still in the room is the mod's "
               + "map-room permanence ruling. Its float is withheld for as long as the map options "
               + "stay locked or the point-of-no-return level stands (ModBuild 243: NOT until the "
               + "game re-opens it — the mod's own map-room deselect can re-open it, and that is not "
               + "the game asking for it back)";

        VRLog.Info(Scope, $"QUEST JOURNEY CURTAIN RAISED (cycle {_cycles} of {MaxJourneyCycles}"
                          + $"{(reRaise ? ", RE-RAISED on a quest log the game had re-opened inside a "
                                          + "standing point of no return — ModBuild 243" : "")}) on "
                          + $"'{log.name}' (ID {log.ID}) at frame {_edgeFrame}. THE MEASUREMENT, ALL "
                          + "THREE TERMS THE GAME'S OWN AND NONE OF THEM WRITTEN BY THIS MOD: a map "
                          + $"location had been selected — '{_selectionName}' — and this tick "
                          + "AdventureMapUIManager.LocationToTravel is null again while "
                          + $"AdventureMapUIManager.IsLocked is {(map != null ? map.IsLocked : false)}, "
                          + $"and the quest list window's own UIWindow.IsOpen is {log.IsOpen}. Those "
                          + "are lines 1448, 1449 and 1450 of MapChoreographer.OnMoveClick, in that "
                          + "order, and OnMoveClick is the single method every confirm path funnels "
                          + "into: the travel button, the gamepad long press, the second click on an "
                          + "already-selected location and the multiplayer all-ready callback. USER "
                          + "REQUEST THIS ANSWERS: \"Die Liste der Quests das Fenster soll schon "
                          + "direkt zum Beginn des Point of no returns verschwinden, also in dem "
                          + "moment in dem eine Quest bestätigt wird und die Animation 'der Reise' "
                          + "beginnt.\" NOTHING IS WRITTEN TO THE GAME: no Hide, no Escape, no "
                          + "SetActive, no CanvasGroup — this is a FloatRefusalTable refusal, asked at "
                          + "the top of the catch-all loop so the churn fuse never counts it, and "
                          + "WithdrawRefusedFloat plus the release loop's ModBuild 235 refusal clause "
                          + "take down the float that already exists. THE PERMANENCE RULING IS NOT "
                          + "OVERTURNED: this is an INTERVAL-ONLY withholding, the window still has no "
                          + "X, CloseFloatedWindow still refuses it and the escape chord still skips "
                          + "it, and at every other moment the ruling governs it in full.");
    }

    private static void Drop(string why)
    {
        if (!_standing)
            return;
        _standing = false;
        VRLog.Info(Scope, $"QUEST JOURNEY CURTAIN LAPSED — {why}. "
                          + $"'{(_member != null ? _member.name : "<the window is gone>")}' floats "
                          + "again from this tick with everything on it. NOTHING HAS TO BE UNDONE, "
                          + "because nothing was written: this class only ever withheld a float. "
                          + $"{_cycles} of {MaxJourneyCycles} cycle(s) used.");
        _why = "the quest-journey curtain has lapsed; the quest list is nobody's responsibility but "
               + "its own";
    }

    /// <summary>
    /// The quest log window the mod is floating right now, or null. Asked of the mod's OWN float set
    /// rather than of the scene, for two reasons that are both rules this project has already paid
    /// for: a scene sweep is the default suspect for a per-frame cost
    /// [[findobjectsoftype-is-the-default-suspect]], and the only quest log this class may ever
    /// withhold is one it is currently drawing — a window nobody is floating cannot be hidden by
    /// refusing to float it.
    ///
    /// <para>The identity is the IS-A form and a COMPONENT TYPE, never a name:
    /// <c>QuestLogManager</c> carries <c>[RequireComponent(typeof(UIWindow))]</c>
    /// (QuestLogManager.cs:16) and caches <c>window = GetComponent&lt;UIWindow&gt;()</c> in Awake
    /// (:67), so the component and the window are one GameObject by construction and this
    /// <c>GetComponent</c> cannot reach any other window's parts
    /// [[containment-is-not-identity]].</para>
    /// </summary>
    private static UIWindow? FloatedQuestLog()
    {
        FloatScratch.Clear();
        ModalFallback.CollectFloatedWindows(FloatScratch, null);
        UIWindow? found = null;
        for (int i = 0; i < FloatScratch.Count && found == null; i++)
        {
            UIWindow w = FloatScratch[i];
            if (w != null && w.GetComponent<QuestLogManager>() != null)
                found = w;
        }
        FloatScratch.Clear();
        return found;
    }

    private static AdventureMapUIManager? MapManager()
    {
        if (!Singleton<AdventureMapUIManager>.IsInitialized)
            return null;
        AdventureMapUIManager m = Singleton<AdventureMapUIManager>.Instance;
        return m != null ? m : null;
    }

    // ---- the falsifier -----------------------------------------------------------------------------

    private static void ResetReport()
    {
        _verdict = string.Empty;
        _reports = 0;
        _nextReportAt = float.NegativeInfinity;
        // ModBuild 243 — the audit's budget and its counters are refilled with the cycle budget and
        // in the same place, so a session's numbers are always "since the map was last at rest"
        // rather than "since some other clock".
        _auditKey = string.Empty;
        _auditLines = 0;
        _lastAuditAt = float.NegativeInfinity;
        _samplesWithheld = 0;
        _samplesLeaked = 0;
        FloatedWhileArmed.Clear();
    }

    /// <summary>
    /// THE ONE LINE A TESTER CAN GREP THAT IS TRUE ONLY IF THE USER'S REQUEST IS SATISFIED.
    ///
    /// <para>It is asked only once the game has COMMITTED — i.e. once the three-term measurement above
    /// says the point of no return has begun — so it is silent for the whole of the ordinary map and
    /// speaks exactly at the moment the request is about. Every clause is re-read this tick from the
    /// live objects and none of them is a value this class set: whether the mod is floating a quest
    /// log at all, whether the refusal table refuses it, and what the panel record says.
    /// [[an-instrument-can-assert-a-cause]] — the one thing it deliberately does NOT claim is WHICH
    /// float path put the window back, because that is not observable from here; what it does instead
    /// is print the discriminator, which is whether the table's verdict and the float set AGREE.</para>
    ///
    /// <para>GREP: <c>QUEST LOG GONE AT THE POINT OF NO RETURN: CONFIRMED</c> — the request is met.
    /// <c>… NOT ACHIEVED</c> — it is not, with the failing term named.</para>
    /// </summary>
    private static void Report(AdventureMapUIManager? map, bool locked, bool selectionPending)
    {
        bool committed = locked && !selectionPending && _selectionSeen;
        if (!committed && !_standing)
        {
            _verdict = string.Empty;   // the question is not being asked; re-arm the edge
            _auditKey = string.Empty;
            return;
        }
        // ON A CADENCE, because the only expensive thing these lines do — walking the mod's float set
        // and asking each window for a QuestLogManager — is done for a LOG LINE and not for the level
        // above, which is three field reads [[one-line-owned-the-frame]]. 0.2 s is
        // LoadoutConfirmPark.AnchorRefreshIntervalSeconds, the same number for the same reason.
        float now = Time.unscaledTime;
        if (now < _nextReportAt)
            return;
        _nextReportAt = now + ReportIntervalSeconds;

        // ONE walk, TWO lines. The verdict line below and the ModBuild 243 audit line are fed from
        // the same snapshot so they can never disagree with each other about what was floating.
        FloatScratch.Clear();
        ModalFallback.CollectFloatedWindows(FloatScratch, null);
        UIWindow? floatedLog = null;
        for (int i = 0; i < FloatScratch.Count; i++)
        {
            UIWindow w = FloatScratch[i];
            if (w == null)
                continue;
            if (floatedLog == null && w.GetComponent<QuestLogManager>() != null)
                floatedLog = w;
            // THE IMPORTANT HALF OF THE FALSIFIER: what DID float while the refusal was armed. A
            // refusal that quietly took the whole room down would show up here as an empty list and
            // nothing else in this class would ever say so.
            if (Standing)
                NoteFloatedWhileArmed(w.name);
        }
        FloatScratch.Clear();
        bool gone = floatedLog == null;

        if (Standing)
        {
            if (gone)
                _samplesWithheld++;
            else
                _samplesLeaked++;
        }
        Audit(map, locked, selectionPending, committed, floatedLog, now);

        if (_reports >= MaxJourneyReports)
            return;

        // THE CHANGE GATE FIRST, THEN THE STRING. This line is asked on every tick of an interval
        // that lasts a whole journey plus a whole loadout, so building a paragraph-length message and
        // then throwing it away would be a per-frame allocation for nothing
        // [[one-line-owned-the-frame]].
        string verdict = gone ? "CONFIRMED" : "NOT ACHIEVED";
        if (verdict == _verdict)
            return;
        _verdict = verdict;
        _reports++;

        UIWindow? subject = _member ?? floatedLog;
        bool refuses = subject != null && FloatRefusalTable.Refuses(subject);
        ConvertedPanel? panel = ModalFallback.PanelFor(subject);

        string measured =
            "THE GAME FACT: MapChoreographer.OnMoveClick has run — AdventureMapUIManager.IsLocked="
            + $"{locked} (written at MapChoreographer.cs:1449), LocationToTravel="
            + $"{(selectionPending ? "still selected" : "null again")} (nulled at :1448 through "
            + $"HideTravelOption), a location had been selected={_selectionSeen} "
            + $"('{_selectionName}'), and the quest list window's own UIWindow.IsOpen="
            + $"{(subject != null ? subject.IsOpen.ToString() : "<no window>")} (closed by "
            + "QuestManager.OnPartyMove at :1450). IT TURNED at frame "
            + $"{(_edgeFrame >= 0 ? _edgeFrame.ToString() : "<never>")}, "
            + $"{(_edgeTime >= 0f ? (Time.unscaledTime - _edgeTime).ToString("F1") : "?")} s ago. "
            + $"THE WINDOW: '{(subject != null ? subject.name : "<none>")}' "
            + $"(ID {(subject != null ? subject.ID.ToString() : "?")}); the mod is floating a quest "
            + $"log right now={!gone}; this class's refusal is standing={Standing} and "
            + $"FloatRefusalTable.Refuses says {refuses}; a live converted panel exists for it="
            + $"{panel != null}; {_cycles} of {MaxJourneyCycles} cycle(s) used, lifted by the deadlock "
            + $"floor={_lifted}; the mod is floating "
            + $"{ModalFallback.CountFloatsOtherThan(null)} window(s) in total. "
            + $"MAP MANAGER RESOLVED: {map != null}";

        if (gone)
        {
            VRLog.Info(Scope, "QUEST LOG GONE AT THE POINT OF NO RETURN: CONFIRMED — the quest list "
                              + "window is not being floated, and it stopped being floated at the "
                              + "moment the game itself committed the party to the quest rather than "
                              + "later, when the story message arrives. MEASURED THIS TICK: " + measured
                              + ". USER REQUEST THIS LINE ANSWERS: \"Die Liste der Quests das Fenster "
                              + "soll schon direkt zum Beginn des Point of no returns verschwinden, "
                              + "also in dem moment in dem eine Quest bestätigt wird und die Animation "
                              + "'der Reise' beginnt.\" WHAT IT DOES NOT CLAIM: that the window is "
                              + "closed. It is not — nothing was written to the game and the GAME's "
                              + "own HideLogScreen is the only thing that touched it.");
            return;
        }
        VRLog.Warn(Scope, "QUEST LOG GONE AT THE POINT OF NO RETURN: NOT ACHIEVED — the party has "
                          + "committed and the mod is still floating the quest list. MEASURED THIS "
                          + "TICK: " + measured + ". READ IT LIKE THIS, AND THE FAILING TERM IS "
                          + "WHICHEVER OF THESE TWO IT IS. If Refuses says FALSE the level is the bug: "
                          + "look at the three game terms above and at QUEST JOURNEY CURTAIN LAPSED or "
                          + "CAPPED for which one let go. If Refuses says TRUE while the window is "
                          + "still floated, the refusal is being asked and something is not listening "
                          + "— of the three paths into ModalFallback.OpenWindows the ENROLLED loop "
                          + "asks the table at ModalFallback.4.Tick.cs:2518 and the CATCH-ALL asks it "
                          + "at ModalFallback.10.CatchAll.cs:268, but the POLL and GROUP path "
                          + "(ModalFallback.7.Close.cs:367, AddPollWindow and AddGroupWindow) never "
                          + "asks at all, and the backstop for a float that is already standing is the "
                          + "release loop's ModBuild 235 refusal clause at "
                          + "ModalFallback.4.Tick.cs:2690. Grep FLOAT WITHDRAWN and FLOAT RELEASED ON "
                          + "REFUSAL for this window's name: their ABSENCE with Refuses=True is the "
                          + "whole diagnosis and it points at that clause. "
                          + "CORRECTION (ModBuild 439): the POLL and GROUP path DOES ask now — "
                          + "AddPollWindow gained the same refusal clause the other two paths carry, "
                          + "so all three agree. The sentence above is kept verbatim because it is "
                          + "what the shipped builds behaved like; read it as history and take the "
                          + "release loop's clause as the remaining backstop.");
    }

    // ---- ModBuild 243: the falsifier that can FAIL ---------------------------------------------

    /// <summary>Seconds between forced <see cref="Audit"/> lines when nothing about the state has
    /// changed. A change-gated line with a CONSTANT key prints once and then looks exactly like a
    /// stopped tick [[held-instrument-reads-as-dead]] — and this level's whole failure mode is that
    /// it stops being armed silently, so the heartbeat is not optional here.</summary>
    private const float AuditHeartbeatSeconds = 15f;

    /// <summary>Cap on audit lines per committed interval. A loadout can stand for minutes; at the
    /// heartbeat above that is one line every 15 s, and 24 covers six minutes before it goes
    /// quiet. Refilled with the cycle budget, at rest.</summary>
    private const int MaxAuditLines = 24;

    /// <summary>How many distinct window names the audit will name. The map room holds a handful of
    /// windows at a time; eight is the whole room with headroom, and a list that grew without bound
    /// would be a leak in a diagnostic.</summary>
    private const int MaxNamedFloats = 8;

    private static string _auditKey = string.Empty;
    private static int _auditLines;
    private static float _lastAuditAt = float.NegativeInfinity;

    /// <summary>Cadence samples taken while the refusal was ARMED on which the mod was NOT floating a
    /// quest log — i.e. the refusal doing its job. Distinct from "armed" on purpose: a refusal that
    /// is armed and a refusal that has actually withheld something must not read the same.</summary>
    private static int _samplesWithheld;

    /// <summary>Cadence samples taken while the refusal was ARMED on which a quest log was floating
    /// ANYWAY. Non-zero means the level is right and something downstream is not listening — the
    /// ModBuild 235 shape, and the NOT ACHIEVED line above names the three paths.</summary>
    private static int _samplesLeaked;

    /// <summary>Distinct names of windows seen floating while the refusal was armed, capped at
    /// <see cref="MaxNamedFloats"/>.</summary>
    private static readonly List<string> FloatedWhileArmed = new(MaxNamedFloats);

    private static void NoteFloatedWhileArmed(string name)
    {
        if (string.IsNullOrEmpty(name) || FloatedWhileArmed.Count >= MaxNamedFloats)
            return;
        for (int i = 0; i < FloatedWhileArmed.Count; i++)
        {
            if (FloatedWhileArmed[i] == name)
                return;
        }
        FloatedWhileArmed.Add(name);
    }

    private static string NamedFloats()
    {
        if (FloatedWhileArmed.Count == 0)
            return "<none — nothing at all floated while the refusal was armed, which for a map room "
                   + "in a loadout is itself suspicious: the loadout screen and its continue control "
                   + "are supposed to be there>";
        string s = string.Empty;
        for (int i = 0; i < FloatedWhileArmed.Count; i++)
            s += (i == 0 ? "'" : ", '") + FloatedWhileArmed[i] + "'";
        if (FloatedWhileArmed.Count >= MaxNamedFloats)
            s += " (list full at " + MaxNamedFloats + ")";
        return s;
    }

    /// <summary>
    /// THE ModBuild 243 FALSIFIER. It exists because ModBuild 242's instrument could not tell
    /// "the refusal fired and held" from "the refusal was never armed" without a human reading two
    /// other lines: the NOT ACHIEVED line above prints only when the two-valued verdict CHANGES, and
    /// a level that quietly lapsed 318 s into a point of no return produced exactly one of them.
    ///
    /// <para>EVERY FIELD IS RE-READ THIS TICK AND NONE OF THEM IS A VALUE THIS CLASS SET, except the
    /// two sample counters, which are named as counters. It prints:</para>
    /// <list type="bullet">
    /// <item><b>The point-of-no-return state</b> — <c>StoryComposite.PointOfNoReturn</c> and the two
    /// terms behind it, plus <c>AdventureMapUIManager.IsLocked</c>, so a reader can see WHICH one is
    /// holding the interval up (or that none of them is).</item>
    /// <item><b>ARMED</b> — whether the refusal stands this instant, and when it does not, the term
    /// that is missing.</item>
    /// <item><b>How many times it has fired</b> — <see cref="_samplesWithheld"/> against
    /// <see cref="_samplesLeaked"/>. ARMED with withheld=0 is a rule that is not reaching anything;
    /// ARMED with leaked&gt;0 is a rule that is reaching the table and losing downstream.</item>
    /// <item><b>What floated while it was armed</b> — by name. This is the half that catches the
    /// [[gate-outliving-its-edge]] failure: if this list is empty or is missing the loadout window
    /// while the loadout screen is open, the refusal has taken more than its one named window.</item>
    /// </list>
    ///
    /// <para>GREP: <c>QUEST JOURNEY CURTAIN AUDIT</c>.</para>
    /// </summary>
    private static void Audit(AdventureMapUIManager? map, bool locked, bool selectionPending,
                              bool committed, UIWindow? floatedLog, float now)
    {
        if (_auditLines >= MaxAuditLines)
            return;

        bool ponr = StoryComposite.PointOfNoReturn;
        bool armed = Standing;
        bool refuses = _member != null && FloatRefusalTable.Refuses(_member);
        bool memberOpen = _member != null && _member.IsOpen;
        float held = _heldAt > 0f ? now - _heldAt : -1f;

        // THE CHANGE GATE FIRST, THEN THE STRING [[one-line-owned-the-frame]]. The key carries every
        // term whose change is worth a line; the sample counters are deliberately NOT in it, or the
        // line would print at the cadence.
        string key = $"{armed}|{refuses}|{ponr}|{locked}|{selectionPending}|{committed}|"
                     + $"{memberOpen}|{floatedLog != null}|{_cycles}|{_lifted}|{_capReported}";
        bool changed = key != _auditKey;
        if (!changed && now - _lastAuditAt < AuditHeartbeatSeconds)
            return;
        _auditKey = key;
        _lastAuditAt = now;
        _auditLines++;

        string why = armed
            ? "ARMED"
            : (!committed
                ? "NOT ARMED — the three-term commit measurement does not hold this tick"
                : (_lifted
                    ? "NOT ARMED — the deadlock floor LIFTED it (see QUEST JOURNEY CURTAIN LIFTED)"
                    : (_capReported
                        ? "NOT ARMED — the cycle cap is spent (see QUEST JOURNEY CURTAIN CAPPED)"
                        : "NOT ARMED — the commit measurement holds but the level is down; see "
                          + "QUEST JOURNEY CURTAIN LAPSED for the term that let go")));

        string line =
            $"QUEST JOURNEY CURTAIN AUDIT: {why}. "
            + $"POINT-OF-NO-RETURN STATE: StoryComposite.PointOfNoReturn={ponr} "
            + "(= the story curtain standing OR UILoadoutManager.IsOpen); "
            + $"AdventureMapUIManager.IsLocked={locked}; LocationToTravel="
            + $"{(selectionPending ? "still selected" : "null")}; a location had been "
            + $"selected={_selectionSeen} ('{_selectionName}'); COMMIT MEASUREMENT HOLDS={committed}; "
            + $"the hold has had a real reason for {(held >= 0f ? held.ToString("F1") : "?")} s "
            + $"(bridge {JourneyBridgeSeconds:F0} s). "
            + $"THE SUBJECT: '{(_member != null ? _member.name : "<none frozen>")}'"
            + $"{(_member != null ? " (ID " + _member.ID + ")" : "")}, its own UIWindow.IsOpen="
            + $"{(_member != null ? memberOpen.ToString() : "?")}, "
            + $"FloatRefusalTable.Refuses={refuses}, a quest log is floating right now="
            + $"{floatedLog != null}. "
            + $"FIRED: {_samplesWithheld} cadence sample(s) WITHHELD and {_samplesLeaked} LEAKED "
            + $"(0.2 s cadence, counted only while armed); {_cycles} of {MaxJourneyCycles} cycle(s) "
            + $"raised. FLOATED WHILE ARMED: {NamedFloats()}. "
            + $"MAP MANAGER RESOLVED: {map != null} — a false here makes every game term above a "
            + "DEFAULT and not a reading, and is the first thing to check before believing any of "
            + "them. "
            + "HOW TO READ IT, AND THE FOUR CASES ARE DELIBERATELY DIFFERENT LINES. "
            + "(1) ARMED with WITHHELD climbing and LEAKED 0 is the feature working — the quest list "
            + "is not in the room and the named windows above are. "
            + "(2) ARMED with LEAKED climbing means the level is right and a float path is not "
            + "listening: read the NOT ACHIEVED line, which names the three paths and the release "
            + "loop's backstop. "
            + "(3) NOT ARMED while the point-of-no-return state above is TRUE is the ModBuild 242 "
            + "bug — the level let go inside the interval; the LAPSED line names the term. "
            + "(4) NOT ARMED with the point-of-no-return state FALSE is correct and expected: the "
            + "map is the ordinary map again and the quest log is the map room's permanent window. "
            + "NOTHING HERE IS WRITTEN TO THE GAME AND NOTHING GOES ON THE WIRE: every term is a "
            + "local read, and a peer's client reaches its own verdict from its own singletons.";

        if (armed || !ponr)
            VRLog.Info(Scope, line);
        else
            VRLog.Warn(Scope, line);   // case (3): the interval stands and the refusal does not
    }

    /// <summary>Module teardown. Drops everything: a refusal that outlived this class would be a
    /// suppression with no owner.</summary>
    internal static void Reset()
    {
        Drop("module teardown");
        _member = null;
        _lifted = false;
        _cycles = 0;
        _capReported = false;
        _selectionSeen = false;
        _selectionName = "<none>";
        _edgeFrame = -1;
        _edgeTime = -1f;
        _heldAt = 0f;
        _why = "the quest-journey curtain has never been raised in this session";
        ResetReport();
        FloatScratch.Clear();
        FloatedWhileArmed.Clear();
    }
}
