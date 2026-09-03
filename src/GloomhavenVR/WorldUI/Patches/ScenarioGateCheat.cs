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
//  PROCESS STATE, NOT CONFIG. The toggle is a static bool that starts OFF at every game start and
//  is not written anywhere. A test aid that survives a restart is a trap for the day the testing
//  is over; and the Cheats page has exactly one config key by design (see its header).
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.MapState;
using ScenarioRuleLibrary;
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
    /// Is a networked session live? Fails CLOSED — an exception reads as "online", and online
    /// means the toggle is inert. Same rule as <c>VROptionsTab.SessionOnline</c>.
    /// </summary>
    internal static bool Online()
    {
        try
        {
            return FFSNetwork.IsOnline;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Flip the toggle. Called from the Cheats page only; the page has already refused
    /// the press while a session is live.</summary>
    internal static void Set(bool on)
    {
        Enabled = on;
        if (on)
            _everEnabled = true;
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
            if (Online())
                return;

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

            if (!Enabled)
            {
                // HW-VERIFY
                VRLog.Note("WorldUI", $"CHEAT 'every scenario loadable' at scenario load: OFF (was on earlier this "
                                      + $"session, switched off cleanly — no requirement was overridden) for {who}.");
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
                                  + $"the save. Multiplayer session active: {mp}. "
                                  + "WHAT WOULD DISPROVE THIS: a Travel refusal warning on the map while this line "
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
