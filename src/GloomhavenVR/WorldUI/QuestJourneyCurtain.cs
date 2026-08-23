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
/// the interval the game has the window closed for, and hands it straight back the moment the game
/// re-opens it.</para>
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
/// <para>GREP: <c>QUEST JOURNEY CURTAIN RAISED</c> / <c>LAPSED</c> — the edges.
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
            bool gameStillHasItClosed = _member != null && !_member.IsOpen;
            if (!gameStillHasItClosed)
                Drop(_member == null
                    ? "the quest log window is gone"
                    : "THE GAME RE-OPENED THE QUEST LIST ITSELF, so there is nothing left to withhold "
                      + "— a window the game is showing must never be one the mod is hiding");
            else if (!realReason && !bridging)
                Drop($"the game unlocked the map options and no point-of-no-return level followed "
                     + $"within {JourneyBridgeSeconds:F0} s, so this was not a quest commit after all");
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
            }
            Report(map, locked, selectionPending);
            return;
        }

        UIWindow? log = FloatedQuestLog();
        if (log == null || log.IsOpen)
        {
            // Nothing to withhold: either the mod is not floating a quest log at all, or the game has
            // it open and it must stay. Both are ordinary and neither is an edge.
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
        Raise(log, map);
        Report(map, locked, selectionPending);
    }

    private static void Raise(UIWindow log, AdventureMapUIManager? map)
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
               + "map-room permanence ruling. Its float is withheld until the game re-opens it or the "
               + "map is unlocked";

        VRLog.Info(Scope, $"QUEST JOURNEY CURTAIN RAISED (cycle {_cycles} of {MaxJourneyCycles}) on "
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
            return;
        }
        if (_reports >= MaxJourneyReports)
            return;
        // ON A CADENCE, because the only expensive thing this line does — walking the mod's float set
        // and asking each window for a QuestLogManager — is done for a LOG LINE and not for the level
        // above, which is three field reads [[one-line-owned-the-frame]]. 0.2 s is
        // LoadoutConfirmPark.AnchorRefreshIntervalSeconds, the same number for the same reason.
        float now = Time.unscaledTime;
        if (now < _nextReportAt)
            return;
        _nextReportAt = now + ReportIntervalSeconds;

        UIWindow? floatedLog = FloatedQuestLog();
        bool gone = floatedLog == null;

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
                          + "whole diagnosis and it points at that clause.");
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
    }
}
