// =====================================================================================
//  TEMPORARY FEATURE — PART OF THE CHEATS PAGE, BUILT TO BE DELETED WITH IT.
//  See the block at the top of WorldUI/Options/VROptionsTab.Cheats.cs for the removal steps.
// =====================================================================================
//
//  USER REQUEST (2026-09-03, translated): "I want a new cheat in the Cheat menu that helps me
//  while testing. Something like 'unlock all locked levels': some scenarios can only be played
//  when certain conditions are met — e.g. a specific character with a specific personal quest
//  must be present. For testing I want to go through ALL scenarios once, so I need a cheat that
//  lets me load those scenarios too."
//
//  THE GAME HAS THREE INDEPENDENT GATES between a scenario and the Travel button. Read from
//  source (decompiled/), not from memory:
//
//  (a) QUEST STATE Locked — the scenario has not been revealed by the story yet.
//        MapRuleLibrary.MapState/CQuestState.cs:400   new quests start Locked
//        MapRuleLibrary.State/CMapState.cs:2916-2924  CheckAllLockedQuests → UnlockQuest when the
//                                                      YML UnlockCondition is met
//        GH.Runtime/MapChoreographer.cs:626,676,2657,2680  a Locked (or Blocked) quest gets NO
//                                                      MapLocation marker at all
//      This one is SAVE STATE, evaluated into CQuestState.QuestState and persisted; a runtime
//      toggle cannot reach the marker filter (a LINQ lambda inside the marker rebuild). It is
//      covered by the EXISTING armed button "Unlock all levels" (CQuestState.UnlockQuest per
//      quest + SaveData.SaveCurrentAdventureData), not by this file.
//
//  (b) REQUIREMENT CONDITIONS — the party must satisfy the scenario's YML requirements.
//        GH.Runtime/CQuestStateExtensions.cs:14-95   CheckRequirements(this CQuestState) builds a
//                                                     RequirementCheckResult:
//            :23-31   startingLocationState   (starting village Locked / Missing)
//            :36-69   missingAmountCharacters (party smaller than RequiredCharacterCount /
//                                              MapState.MinRequiredCharacters)
//            :75-79   missingRequiredCharacters      (a specific class must be in the party)
//            :80-83   charactersWithoutLevel          (that class below RequiredLevel)
//            :85-88   missingRequiredItems            (an item must be equipped)
//            :89-92   missingRequiredPersonalQuests   (a character must OWN a personal quest)
//        GH.Runtime/RequirementCheckResult.cs:48-55  IsUnlocked() = ValidCharacterParty &&
//              ValidSizeParty && ValidStartingLocation && !IsQuestStateLocked &&
//              IsValidLinkedQuestChoice && !HasRemainingCharactersToRetire
//        Consumers, every one of them "IsUnlocked() || MapChoreographer.ShowAllScenariosMode":
//            GH.Runtime/AdventureMapUIManager.cs:269-270  CheckTravelQuestConditions — THE
//                                                          TRAVEL BUTTON'S VERDICT
//            GH.Runtime/MapChoreographer.cs:1243-1244      selecting the marker
//            GH.Runtime/MapChoreographer.cs:1338-1339,1370-1371  party token placement
//            GH.Runtime/UIQuestMapMarker.cs:43-44          greyed marker + lock mask
//            GH.Runtime/UIQuestDescription.cs:241-247      quest card
//            GH.Runtime/UIQuestPopup.cs:318-319            quest popup
//            GH.Runtime/UIQuestLogSlot.cs:197-198          quest log
//      The user's example (a class with a personal quest) is EXACTLY this gate. It is evaluated
//      fresh on every call and never cached into the save, so a runtime toggle reaches all of it.
//
//  (c) QUEST STATE Blocked — the other branch of a linked-scenario choice.
//        MapRuleLibrary.State/CMapState.cs:2928-2957  CheckForBlockedQuests: BlockQuest() when
//                                                      the YML BlockedCondition holds; re-run on
//                                                      every map load (CMapState.cs:1544 → :1766)
//        MapRuleLibrary.MapState/CQuestState.cs:506-511  UnlockQuest() early-returns for anything
//                                                      not Locked, so Blocked quests are skipped
//                                                      by BOTH the game's own DebugShowAllScenarios
//                                                      and the mod's unlock button as it was
//      Save state again, same marker filter as (a). The data model DOES allow loading one (the
//      game's own debug mode does ResetQuest → Unlocked → SetInProgressQuest on travel,
//      MapChoreographer.cs:1454-1461), so the EXISTING armed button now also flips Blocked →
//      Locked → Unlocked (see VROptionsTab.Cheats.cs). The game's own pass re-blocks them on the
//      next map load if the branch condition still holds; that is documented on the button.
//
//  WHAT THIS FILE DOES — gate (b) as a TOGGLE, no save write:
//
//    * A postfix on CQuestStateExtensions.CheckRequirements that, while the toggle is on, EMPTIES
//      the six requirement fields of the RequirementCheckResult it just built. Every consumer
//      above then sees "requirements met" through the game's own verdict method; the warning
//      box has nothing to list; nothing is persisted. Switching the toggle off restores the
//      game's verdicts on the next evaluation, which the map UI performs on every refresh.
//      NOT touched, on purpose: IsQuestStateLocked (gate (a), the other button), the linked-quest
//      choice phase (IsValidLinkedQuestChoice — bypassing it would let the party leave a phase
//      the map state machine is waiting in) and the pending-retirement block (a party rule, not a
//      scenario one; the player can retire and continue).
//    * A prefix on UnityGameEditorRuntime.LoadScenario(ScenarioState, UnityAction<ScenarioState>)
//      — the one overload every scenario load funnels through (the string and two-arg overloads
//      both call it; Choreographer.cs:1673-1707 for the campaign/guildmaster map) — that prints
//      ONE line per scenario load naming the quest and the gates the toggle overrode for it,
//      with the original verdict. That line is the hardware evidence for this feature.
//
//  WHY NOT MapChoreographer.ShowAllScenariosMode: the game's own flag would flip every consumer
//  in one place, but it also makes the travel path ResetQuest()+SetInProgressQuest() the quest it
//  moves to (MapChoreographer.cs:1454-1461, a state write) and inverts the village branch at
//  :1371. Clearing the requirement fields is the narrower change: the verdict method is the
//  game's, the state writes are the game's, only the party's fitness is lied about.
//
//  MULTIPLAYER: INERT WHILE A SESSION IS LIVE. The postfix asks FFSNetwork.IsOnline on every call
//  and does nothing while it is true; the page refuses to switch the toggle on while online and
//  says so on the row. Same policy, same reason as the other two cheats: the campaign save is the
//  session's shared state and there is no game action that could carry "the host lied about the
//  party" to the guests — a host travelling to a scenario the guests' clients consider locked
//  would put the two sides of the session on different verdicts. Offline-only cannot be got wrong.
//
//  ---------------------------------------------------------------------------------------------
//  ROUND 2026-09-04 — THE RULING: COMPLETELY OFF IN MULTIPLAYER, AND THE PICTURE GOES BACK TOO
//  ---------------------------------------------------------------------------------------------
//
//  USER, verbatim: "Es ist wieder vorgekommen, dass das Fenster komplett verschwunden ist und es
//  dannach komisch wurde. Ich habe aber rausgefunden woran es liegt: Das passiert wenn ich eine
//  map anklicke, die wegen den Cheats aktiviert wurde, die eigentlich gesperrt sind. Dieser Cheat
//  soll im Multiplayer völlig deaktiviert werden - man soll also nach wie vor dann nicht darauf
//  klicken können - das sollte dann auch das Problem fixen."
//
//  He found the trigger for the recurring "the window vanished and then it got strange" defect:
//  clicking a scenario on the map that is selectable ONLY because a cheat opened it. The ruling is
//  not a hypothesis to re-litigate: in multiplayer the cheat is off, and such a scenario must stay
//  un-clickable there.
//
//  WHAT WAS ALREADY TRUE (ModBuild 407) AND WHAT WAS NOT. The verdict override already refused
//  while FFSNetwork.IsOnline — the postfix returned early and the game's own numbers stood. What
//  did NOT go back was THE PICTURE. UIQuestMapMarker.RefreshState (UIQuestMapMarker.cs:41-59) is
//  the only writer of a marker's lock mask and greyed material, and it runs on a REFRESH, not per
//  frame: a marker painted while the toggle was on and the session was still offline keeps its
//  unlocked look — no incompleteMask, full alpha — after the session goes live. A scenario the
//  game holds locked therefore still LOOKED open, which is the invitation he describes.
//
//  WHAT THE VANISH ACTUALLY IS — read from source this round, and it is NOT this file:
//    * The lock mask is paint, not a gate. MapLocation.IsSelectable (MapLocation.cs:315-329)
//      returns true for ANY campaign location carrying a LocationQuest, and in VR the click is
//      dispatched straight at the MapLocation with ExecuteEvents.pointerClickHandler
//      (WorldUI/MapRoom/MapLocationInteractor.cs:1400), so nothing drawn on the marker can stop
//      it. A greyed, masked marker is clickable in VR with or without any cheat.
//    * The refusal is what tears the window down. MapChoreographer.cs:1243-1248 answers a refused
//      selection with AdventureMapUIManager.DeselectCurrentMapLocation (:387-394), which deselects
//      the PREVIOUSLY selected location — the one whose quest window is open — and that re-enters
//      OnMapLocationSelect(prev, active:false) → QuestManager.OnMapLocationQuestSelected(prev,
//      false) (QuestManager.cs:142-167) → UIQuestPopup.Hide (:342-351) → UIWindow.Hide, whose
//      ChangeActive (UIWindow.cs:742-747) can SetActive(false) the popup GameObject. That rect is
//      the one the VR side re-parented into a world panel, so the panel blanks, the sticky
//      re-show fights it and concedes, and the float is released. "Das Fenster ist komplett
//      verschwunden und dannach wurde es komisch."
//    * So the cheat is the thing that puts him in front of that click; it is not the thing that
//      makes the click possible. Making a refused scenario genuinely UN-clickable in VR is a
//      pre-check in the map-room interactor (a different lane owns WorldUI/MapRoom/**), and
//      surviving a game-side hide of an adopted window is the modal fallback's job. Neither is
//      here, and this file must not be read as having delivered them.
//
//  THEREFORE, three things now, all of them subtractive, and all of them about the cheat itself:
//    1. The verdict is still asked per call — the gate is evaluated AT THE MOMENT THE VERDICT IS
//       PRODUCED (in the postfix, on the game's own result object), never latched when the toggle
//       is flipped. Toggling the row mid-session cannot bypass it because the row is not what is
//       consulted; FFSNetwork.IsOnline is, on every single evaluation.
//    2. The first such refusal SWITCHES THE TOGGLE OFF for good ("völlig deaktiviert"), so nothing
//       downstream — now or the day someone adds a second consumer — can find it on while a
//       session is live. Only a CERTAIN answer latches: if the game's own property throws we still
//       refuse (fail closed) but leave the toggle alone, because one throw in single player must
//       not silently end his test run.
//    3. And the map's own picture is put back: one deferred, one-shot
//       MapMarkersManager.RefreshQuestsState() (MapMarkersManager.cs:204-210 — the very call
//       QuestManager.RefreshLockedQuests makes, marker repaint only, no window lifecycle, no state
//       write) so every marker re-asks CheckRequirements, now gets the game's verdict, and puts
//       its lock mask and greyed icon back. Deferred by one frame onto VRSession.CoroutineHost
//       rather than called inline, because the refusal usually happens INSIDE a marker's own
//       RefreshState and repainting the list from within one of its entries is re-entrancy for no
//       gain. This restores what the player SEES — the same picture a client without the cheat
//       gets — and, per the paragraph above, it does not by itself make the icon un-clickable.
//
//  WHY THIS IS THE FIX AND NOT A PLASTER: both machines then compute the same unlock verdict from
//  the same game state, with the same code, because neither of them is running the override any
//  more. Nothing is sent, no wire field exists, no game state is written — the correctness comes
//  from REMOVING a local divergence, which is why the desync-shaped symptom goes with it.
//
//  ALTERNATIVES REJECTED. (a) Unpatching the postfix when a session starts: it needs a session-
//  start seam the mod does not own (FFSNetwork.StartUp is the game's) and a patch that comes and
//  goes is harder to reason about than a body that returns early on a term it reads live.
//  (b) Leaving 407's per-call refusal alone and doing nothing else: that is the build he tested,
//  and it left a scenario the game holds locked wearing an unlocked face. (c) Host-only instead of offline-only: rejected for the
//  whole page already (VROptionsTab.Cheats.cs) — a cheat that is correct only under a condition
//  nobody checks is a trap. (d) Calling QuestManager.RefreshLockedQuests instead of the marker
//  manager: it also refreshes the quest log and the quest POPUPS, and a popup is a window the VR
//  side adopts — repainting markers must not move a window the player is looking at.
//
//  PROCESS STATE, NOT CONFIG. The toggle is a static bool that starts OFF at every game start and
//  is not written anywhere. A test aid that survives a restart is a trap for the day the testing
//  is over; and the Cheats page has exactly one config key by design (see its header).
// =====================================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.MapState;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.Events;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// The "every scenario loadable" test toggle: state, the record of what it overrode per quest,
/// and the scenario-load log line. The two Harmony patch classes below feed and read it.
/// Registered by <c>WorldUIModule</c>; switched from the Cheats page.
/// </summary>
internal static class ScenarioGateCheat
{
    /// <summary>The toggle. Process state; starts off; never persisted.</summary>
    internal static bool Enabled { get; private set; }

    /// <summary>
    /// True once the toggle has been on at any point this session. The scenario-load line prints
    /// while this is true even with the toggle off, so "switched off cleanly" is visible in the
    /// same log as "switched on" — and a player who never opened the Cheats page never sees it.
    /// </summary>
    private static bool _everEnabled;

    /// <summary>
    /// Per quest id, what the postfix last overrode for it (a readable summary), or absent when
    /// the last evaluation had nothing to override. Overwritten on every evaluation, so it tracks
    /// the party as it changes rather than the first thing it ever saw.
    /// </summary>
    private static readonly Dictionary<string, string> Overrides = new();

    /// <summary>Cheap change detector per quest id, so the summary string is rebuilt only when
    /// the set of overridden requirements actually changes (the map UI re-evaluates often).</summary>
    private static readonly Dictionary<string, int> Signatures = new();

    private static bool _postfixWarned;

    /// <summary>
    /// How many times a verdict was consulted while the toggle was on and a session was live —
    /// i.e. how many times the multiplayer rule refused it. In the ordinary case this reaches
    /// exactly 1, because the first CERTAIN refusal also switches the toggle off; it climbs only
    /// while <c>FFSNetwork.IsOnline</c> is throwing, which is the case the count is here to make
    /// visible. Never reset — it is a session tally, and the toggle going off does not clear it.
    /// </summary>
    private static int _onlineRefusals;

    /// <summary>Change gate for the refusal line: ONE line per session, never one per call. The
    /// count above carries every later refusal to the scenario-load line instead.</summary>
    private static bool _onlineRefusalNoted;

    /// <summary>True when the multiplayer rule, and not the player, switched the toggle off.</summary>
    private static bool _forcedOffByOnline;

    /// <summary>One-shot guard for the deferred marker repaint (see the header, point 3).</summary>
    private static bool _repaintQueued;

    /// <summary>
    /// Is a networked session live? Fails CLOSED — an exception reads as "online", and online
    /// means the toggle is inert. Same rule as <c>VROptionsTab.SessionOnline</c>.
    /// </summary>
    internal static bool Online() => Online(out _);

    /// <summary>
    /// The same question, plus whether the game actually ANSWERED it. <paramref name="certain"/>
    /// is false only when <c>FFSNetwork.IsOnline</c> threw: the verdict is still "online" so the
    /// override refuses either way, but an answer nobody gave must not be the thing that switches
    /// the toggle off for the rest of the session — one throw in single player would silently end
    /// a test run, and the whole point of this toggle is walking a scenario list alone.
    /// </summary>
    private static bool Online(out bool certain)
    {
        try
        {
            bool online = FFSNetwork.IsOnline;
            certain = true;
            return online;
        }
        catch
        {
            certain = false;
            return true;
        }
    }

    /// <summary>Flip the toggle. Called from the Cheats page only; the page has already refused
    /// the press while a session is live.</summary>
    internal static void Set(bool on)
    {
        Enabled = on;
        if (on)
        {
            _everEnabled = true;
            _forcedOffByOnline = false;
        }
        else
        {
            Overrides.Clear();
            Signatures.Clear();
        }
    }

    /// <summary>
    /// The postfix body: empty the six requirement fields while the toggle is on, and remember
    /// what was emptied for the scenario-load line. Runs on the result the game just built, so
    /// the game's own <c>IsUnlocked()</c> is what turns true — no verdict is re-implemented here.
    /// </summary>
    internal static void OverrideRequirements(CQuestState? quest, RequirementCheckResult? result)
    {
        if (!Enabled || result == null)
            return;
        try
        {
            // THE MULTIPLAYER GATE, and it sits HERE on purpose: this is the moment the verdict is
            // produced, so no order of events — toggling the row mid-session, a session started
            // after the toggle, a session ended and restarted — can present an answer that was
            // decided earlier. The term is the game's own FFSNetwork.IsOnline, asked live.
            if (Online(out bool certain))
            {
                RefuseWhileOnline(quest?.ID, certain);
                return;
            }

            string id = quest?.ID ?? "?";
            int chars = result.missingRequiredCharacters?.Count ?? 0;
            int levels = result.charactersWithoutLevel?.Count ?? 0;
            int items = result.missingRequiredItems?.Count ?? 0;
            int pqs = result.missingRequiredPersonalQuests?.Count ?? 0;
            int amount = result.missingAmountCharacters;
            var loc = result.startingLocationState;

            bool nothing = chars == 0 && levels == 0 && items == 0 && pqs == 0 && amount == 0
                           && loc == RequirementCheckResult.StartingLocatinState.Valid;
            if (nothing)
            {
                Overrides.Remove(id);
                Signatures.Remove(id);
                return;
            }

            // net472 has no System.HashCode; a small-ranged fold is enough for a change detector.
            int signature = chars;
            signature = signature * 31 + levels;
            signature = signature * 31 + items;
            signature = signature * 31 + pqs;
            signature = signature * 31 + amount;
            signature = signature * 31 + (int)loc;
            signature = signature * 31 + (result.IsQuestStateLocked ? 1 : 0);
            if (!Signatures.TryGetValue(id, out int previous) || previous != signature)
            {
                Signatures[id] = signature;
                Overrides[id] = Summarise(result, chars, levels, items, pqs, amount, loc);
            }

            // THE OVERRIDE. Public fields, the game's own result object; the verdict methods
            // (ValidCharacterParty, ValidSizeParty, ValidStartingLocation) read exactly these.
            result.missingRequiredCharacters?.Clear();
            result.charactersWithoutLevel?.Clear();
            result.missingRequiredItems?.Clear();
            result.missingRequiredPersonalQuests?.Clear();
            result.missingAmountCharacters = 0;
            result.startingLocationState = RequirementCheckResult.StartingLocatinState.Valid;
        }
        catch (Exception e)
        {
            if (_postfixWarned)
                return;
            _postfixWarned = true;
            VRLog.Warn("WorldUI", "CHEAT 'every scenario loadable': the CheckRequirements postfix threw "
                                  + $"and left the game's verdict untouched ({e.GetType().Name}: {e.Message}). "
                                  + "Printed once; later throws are silent.");
        }
    }

    /// <summary>
    /// The multiplayer rule, run at the moment a verdict was produced: count the refusal, switch
    /// the toggle off for good when the session term was answered rather than thrown, put the
    /// map's own picture back, and say all of it once.
    ///
    /// <para>NOT a diagnostic despite what it mostly does — it carries the ruling's state change,
    /// which is why the switch-off is here and not inside a <c>Log…</c> helper: a write that lives
    /// in an instrument is a write that dies with the instrument.</para>
    /// </summary>
    private static void RefuseWhileOnline(string? questId, bool certain)
    {
        _onlineRefusals++;

        // THE RULING, 2026-09-04: completely disabled in multiplayer. Refusing per call would be
        // enough for THIS call; switching the toggle off is what makes it true for every OTHER
        // reader of the flag, now and later, without any of them having to remember the rule.
        bool latched = false;
        if (certain && Enabled)
        {
            Set(false);
            _forcedOffByOnline = true;
            latched = true;
        }

        string repaint = latched
            ? QueueMarkerRepaint()
            : "not attempted — the toggle was left as it was";

        if (_onlineRefusalNoted)
            return;
        _onlineRefusalNoted = true;

        string term = certain
            ? "FFSNetwork.IsOnline = true (BoltNetwork.IsRunning && !IsShuttingDown), read live at "
              + "the moment the verdict was produced"
            : "FFSNetwork.IsOnline THREW, and an unanswerable session question reads as online "
              + "(fail closed) — so the override refused, and the toggle was deliberately NOT "
              + "switched off, because a throw in single player must not end a test run";
        string forQuest = questId ?? "unnamed quest";
        string state = latched
            ? "the toggle is now OFF for the rest of the session"
            : Enabled
                ? "the toggle is still ON and still inert while the session lasts"
                : "the toggle was already off";

        // HW-VERIFY
        VRLog.Note("WorldUI", "CHEAT 'every scenario loadable': CONSULTED and REFUSED because the "
                              + "session is online. The game's own RequirementCheckResult was returned "
                              + $"untouched for {forQuest}. Deciding term: {term}. Ruling 2026-09-04 — "
                              + $"the cheat is completely disabled in multiplayer: {state}. Map marker "
                              + $"repaint (so a marker painted while the toggle was on gets its lock "
                              + $"mask back): {repaint}. Printed once per session; refusal count so far "
                              + $"{_onlineRefusals}, later ones ride on the scenario-load line. "
                              + "WHAT WOULD DISPROVE THIS: a travel that SUCCEEDED after this line to "
                              + "a scenario the game holds locked, or a later line of this file "
                              + "reporting an override while the session is still up. NOT a "
                              + "falsifier: a locked marker that can still be poked in VR — the "
                              + "map-room interactor dispatches the click past the marker paint "
                              + "(MapLocationInteractor.cs:1400), which is a different lane.");
    }

    /// <summary>
    /// Ask the map to repaint its quest markers on the NEXT frame, once per session.
    ///
    /// <para>Deferred rather than inline because this is reached from inside an arbitrary caller of
    /// <c>CheckRequirements</c> — usually a marker's own <c>RefreshState</c> — and repainting the
    /// marker list from within one of its entries is re-entrancy for no gain. Returns what happened
    /// for the log line; the repaint itself reports its own outcome.</para>
    /// </summary>
    private static string QueueMarkerRepaint()
    {
        if (_repaintQueued)
            return "already queued earlier this session";
        MonoBehaviour? host = VRSession.CoroutineHost;
        if (host == null)
            return "NOT queued — no coroutine host yet; the map repaints on its own next refresh "
                   + "and the click path re-asks CheckRequirements either way";
        _repaintQueued = true;
        host.StartCoroutine(RepaintMarkersNextFrame());
        return "queued for the next frame";
    }

    /// <summary>
    /// <c>MapMarkersManager.RefreshQuestsState()</c> — the marker half of the game's own
    /// <c>QuestManager.RefreshLockedQuests</c>. Every quest marker re-asks
    /// <c>CheckRequirements</c>, which the postfix no longer touches, and puts back the lock mask
    /// and greyed material that MARK a locked scenario as locked. Paint, not a gate:
    /// <c>MapLocation.IsSelectable</c> never consults it (see the header). No window is opened,
    /// closed or reparented; nothing is written to the map state or the save.
    /// </summary>
    private static IEnumerator RepaintMarkersNextFrame()
    {
        yield return null;
        string outcome;
        try
        {
            MapMarkersManager? markers = Singleton<MapMarkersManager>.IsInitialized
                ? Singleton<MapMarkersManager>.Instance
                : null;
            if (markers == null)
            {
                outcome = "no MapMarkersManager in the scene — the campaign map is not up, so there "
                          + "is no stale marker to repaint";
            }
            else
            {
                markers.RefreshQuestsState();
                outcome = "MapMarkersManager.RefreshQuestsState() ran; every quest marker re-asked "
                          + "the game for its own verdict";
            }
        }
        catch (Exception e)
        {
            outcome = $"the repaint threw ({e.GetType().Name}: {e.Message}) and was abandoned — the "
                      + "map still repaints itself on its next refresh";
        }

        // HW-VERIFY
        VRLog.Note("WorldUI", "CHEAT 'every scenario loadable': marker repaint after the multiplayer "
                              + $"refusal: {outcome}. WHAT WOULD DISPROVE THIS: a scenario icon still "
                              + "drawn open — no lock mask, full alpha — after this line while the "
                              + "game holds it locked.");
    }

    private static string Summarise(RequirementCheckResult result, int chars, int levels, int items,
                                    int pqs, int amount, RequirementCheckResult.StartingLocatinState loc)
    {
        var sb = new StringBuilder(160);
        int gates = 0;
        void Gate(string text)
        {
            if (gates++ > 0)
                sb.Append("; ");
            sb.Append(text);
        }

        if (loc != RequirementCheckResult.StartingLocatinState.Valid)
            Gate($"starting village {loc}");
        if (amount > 0)
            Gate($"party size (needs {amount})");
        if (chars > 0)
            Gate("required character(s) [" + string.Join(", ", result.missingRequiredCharacters) + "]");
        if (levels > 0)
        {
            var parts = new List<string>(levels);
            foreach (Tuple<string, int> t in result.charactersWithoutLevel)
                parts.Add($"{t.Item1} needs level {t.Item2}");
            Gate("character level [" + string.Join(", ", parts) + "]");
        }
        if (items > 0)
            Gate("required item(s) [" + string.Join(", ", result.missingRequiredItems) + "]");
        if (pqs > 0)
            Gate("required personal quest(s) [" + string.Join(", ", result.missingRequiredPersonalQuests) + "]");

        // Not overridden here, and worth saying on the same line so "the toggle is on and it is
        // still greyed out" has its answer: the state gate belongs to the other button.
        if (result.IsQuestStateLocked)
            sb.Append(" — NOT overridden: quest state is Locked (gate (a), use 'Unlock all levels')");

        return $"{gates} gate(s): " + sb;
    }

    /// <summary>
    /// The multiplayer half of the scenario-load line: was the override consulted while a session
    /// was live, how often, and what the rule did about it. Reads state, writes none — the switch-off
    /// it reports was made by <see cref="RefuseWhileOnline"/> at the moment of the verdict.
    /// </summary>
    private static string OnlineClause()
    {
        if (_onlineRefusals == 0)
            return "Online rule: the override was never consulted while a session was live "
                   + "(0 refusals), so nothing was refused for that reason.";

        string what = _forcedOffByOnline
            ? "and the rule switched the toggle OFF for the session (ruling 2026-09-04: the cheat "
              + "is completely disabled in multiplayer)"
            : "and the toggle was left as it was, because the session term threw rather than "
              + "answered and an unanswered question must not end a single-player test run";
        string repainted = _repaintQueued
            ? " The quest markers were asked to repaint, so a marker painted while the toggle was "
              + "on shows the game's own lock state again."
            : "";
        return $"Online rule: CONSULTED and REFUSED {_onlineRefusals} time(s) — the game's own "
               + $"RequirementCheckResult was returned untouched each time {what}."
               + repainted;
    }

    /// <summary>
    /// The prefix body for <c>UnityGameEditorRuntime.LoadScenario</c>: one line per scenario load
    /// naming the quest being loaded and what the toggle overrode for it.
    /// </summary>
    internal static void LogAtScenarioLoad(ScenarioState? scenarioState)
    {
        if (!Enabled && !_everEnabled)
            return;
        try
        {
            CQuestState? quest = null;
            try
            {
                quest = AdventureState.MapState?.InProgressQuestState;
            }
            catch
            {
                // AllIncompleteQuests.SingleOrDefault throws on a duplicate in-progress quest;
                // the line below then says "no in-progress quest", which is still true enough.
            }

            string who = quest != null
                ? $"quest '{quest.ID}' (state {quest.QuestState}, scenario id '{scenarioState?.ID ?? "?"}')"
                : $"no in-progress campaign quest (scenario id '{scenarioState?.ID ?? "?"}' — level editor, "
                  + "custom level, tutorial or a load outside the map)";

            // The multiplayer clause, appended to both branches below rather than folded into
            // them: the round of 2026-09-04 asks the next hardware log to say whether the cheat was
            // CONSULTED and REFUSED online, and that is a different fact from whether it is on.
            string online = OnlineClause();

            if (!Enabled)
            {
                // HW-VERIFY
                VRLog.Note("WorldUI", $"CHEAT 'every scenario loadable' at scenario load: OFF (was on earlier this "
                                      + $"session, switched off cleanly — no requirement was overridden) for {who}. "
                                      + online);
                return;
            }

            // Resolved OUTSIDE the interpolated string below on purpose: scripts/patch-inventory.py
            // walks string literals with a plain quote scanner, so a quoted literal inside an
            // interpolation hole flips its idea of what is code, and an apostrophe in that
            // stretch then swallows the rest of the file as a char literal — the two patch
            // classes below silently stopped existing for the inventory check.
            string mp = Online()
                ? "YES — the override was INERT, the verdicts of the game stood"
                : "no";
            string overrode = quest != null && Overrides.TryGetValue(quest.ID, out string? summary)
                ? $"overrode {summary}"
                : "overrode 0 gates — the game's own requirement verdict already passed for it (or it was "
                  + "never evaluated through CheckRequirements this session)";

            // HW-VERIFY
            VRLog.Note("WorldUI", $"CHEAT 'every scenario loadable' at scenario load: ON for {who}: {overrode}. "
                                  + "Mechanism: postfix on CQuestStateExtensions.CheckRequirements emptied the "
                                  + "requirement fields of the game's own RequirementCheckResult; nothing written to "
                                  + $"the save. Multiplayer session active: {mp}. " + online
                                  + " WHAT WOULD DISPROVE THIS: a Travel refusal warning on the map while this line "
                                  + "says ON and the session is offline, or a load-time gate summary that does not "
                                  + "name the requirement the map showed.");
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"CHEAT 'every scenario loadable': the scenario-load log line threw ({e.GetType().Name}: {e.Message}).");
        }
    }
}

/// <summary>
/// Gate (b): empties the requirement fields of every <c>RequirementCheckResult</c> the game
/// builds while the toggle is on (offline only). See the file header.
/// </summary>
[HarmonyPatch(typeof(CQuestStateExtensions), nameof(CQuestStateExtensions.CheckRequirements))]
internal static class CQuestStateExtensions_CheckRequirements_Patch
{
    private static void Postfix(CQuestState quest, RequirementCheckResult __result)
    {
        ScenarioGateCheat.OverrideRequirements(quest, __result);
    }
}

/// <summary>
/// The scenario-load evidence line. Patched on the one overload every load funnels through;
/// the prefix only logs, it changes nothing about the load.
/// </summary>
[HarmonyPatch(typeof(UnityGameEditorRuntime), nameof(UnityGameEditorRuntime.LoadScenario),
              typeof(ScenarioState), typeof(UnityAction<ScenarioState>))]
internal static class UnityGameEditorRuntime_LoadScenario_Patch
{
    private static void Prefix(ScenarioState scenarioState)
    {
        ScenarioGateCheat.LogAtScenarioLoad(scenarioState);
    }
}
