// =====================================================================================
//  TEMPORARY FEATURE — BUILT TO BE DELETED.  READ THIS BLOCK BEFORE TOUCHING ANYTHING.
// =====================================================================================
//
//  USER REQUEST, verbatim (2026-08-15):
//
//      "Beim Spielen fallen mir immer wieder Dinge auf die ich erst beim spielen sehe. Ich
//       brauche daher eine Möglichkeit ohne das spiel wirklich zu spielen zum testen einmal
//       durch alle Räume zu gehen. Ich möchte daher von dir das du im "Erweitert" Menu einen
//       weiteren Reiter "Cheats" einbaust mit Zwei Buttons: Der eine schaltet in dem Savegame
//       alle Levels frei, so das ich durch alle einmal durch kann, und der andere öffnet direct
//       alle Türen und macht damit alle Räume sichtbar. Das ganze brauche ich nur zum Testen.
//       Nachdem ich alles durchgetestet habe soll das wieder entfernt werden."
//
//  ---------------------------------------------------------------------------------
//  HOW TO REMOVE THIS FEATURE COMPLETELY — four deletions, one commit, no archaeology:
//  ---------------------------------------------------------------------------------
//
//    1. DELETE THIS FILE:  src/GloomhavenVR/WorldUI/VROptionsTab.Cheats.cs
//
//    2. In src/GloomhavenVR/WorldUI/VROptionsTab.3.Content.cs, delete the ONE enum member
//       marked `// CHEATS (temporary)` from the `View` enum:
//
//           AdvancedCheats,
//
//    3. In the same file, delete the ONE switch arm marked `// CHEATS (temporary)` in
//       `Rebuild()`:
//
//           View.AdvancedCheats => BuildCheatsPage(),
//
//    4. In the same file, delete the ONE line marked `// CHEATS (temporary)` at the bottom of
//       `BuildAdvancedIndex()`:
//
//           rows += BuildCheatsIndexLink();
//
//    5. Delete the THREE lines marked `// CHEATS (temporary)` that carry the gate added on
//       2026-09-02 (user: "Das Cheats-Menu sollten über die cfg aktiviert werden können (per
//       default AUS)"):
//         * `Bind("cheats", BindCheatsConfig);`      in ConfigCatalog.EnsureBound
//         * the `["Cheats/Enabled"]` line            in ConfigCatalog.NotOffered
//         * `internal const bool CheatsEnabled …`    in Defaults/Defaults.WorldUI.cs
//       Nothing else refers to the key. The player's `dev.gloomhavenvr.cheats.cfg` then stops
//       being written and can be deleted by hand; it holds one line and nothing else reads it.
//
//  THAT IS THE WHOLE FOOTPRINT. Deliberately:
//    * NO Loc entries. Every user-facing string on this page lives in `Text()` at the bottom of
//      this file, so removing the feature cannot leave orphaned translation keys behind in
//      Core/Loc.cs (which is where they would normally go, and where nobody would think to look
//      for them a month from now).
//    * ONE config entry, ON ITS OWN FILE. `[Cheats] Enabled` in `dev.gloomhavenvr.cheats.cfg` is
//      the gate the user asked for, and it is a whole file rather than a line on a shared one for
//      exactly the reason this block exists: removing the feature must not leave a dead key in a
//      player's tuned .cfg. Nothing else here persists a setting — no dependency rule, no curated
//      row, no wire field.
//    * NO Harmony patches. Nothing is hooked; both buttons CALL the game, on a press, and then
//      stop existing again.
//    * NO wire fields, no NetProtocol change, no ModBuild implication.
//
//  ARCHITECTURAL RULE HONOURED: ScenarioRuleLibrary and Bolt are NOT patched. Both buttons go
//  through the game's OWN debug seams — `DebugMenu.RevealAllRooms()` (the very method the game's
//  own CheatPanel "Battle Cheats ▸ RevealAllRooms" calls) and the map's own
//  `CQuestState.UnlockQuest()` / `SaveData.SaveCurrentAdventureData()` save-data API. Nothing
//  here re-implements a rule; it presses the game's own buttons.
//
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GloomhavenVR.Core;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.MapState;
using MapRuleLibrary.YML.Quest;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Erweitert ▸ <b>Cheats</b> — a temporary test aid with exactly two buttons. See the block
/// comment at the top of this file for the request and for how to remove the whole thing.
///
/// <para><b>IT IS A PAGE UNDER "ERWEITERT", NOT A SIXTH SUB-TAB IN THE COLUMN</b>, and that is
/// the same shape the existing "Test-Auslöser" page has (<c>VROptionsTab.9.TestTriggers.cs</c>) —
/// which is what the user is calling a "Reiter" when he says "im Erweitert Menu einen weiteren
/// Reiter": a headed page you reach from the Erweitert index, sitting beside the topic pages. A
/// real sub-tab would have to touch four places in <c>VROptionsTab.3.Content.cs</c> instead of
/// three (the tab strip, the index mapping, the lit-tab rule and the view switch), which is a
/// worse trade for something that is going to be deleted. It is also the safer place: the column
/// is where the everyday settings live and a tab labelled "Cheats" sitting next to "Komfort" is
/// one mis-click from a player who never wanted it.</para>
///
/// <para><b>MULTIPLAYER: BOTH BUTTONS REFUSE WHILE A SESSION IS LIVE, and say so on the button
/// itself.</b> This is the "refuse" half of the standing choice, taken for each button on its own
/// evidence rather than as a blanket:</para>
/// <list type="bullet">
/// <item><b>Doors/rooms have NO synchronisation mechanism to go through.</b> The game's action
/// enum (<c>FFSNet.GameActionType</c>) carries <c>DebugWinScenario</c>, <c>DebugLoseScenario</c>,
/// <c>DebugAddItem</c> and <c>RegenerateAllScenarios</c> — and nothing door- or room-related.
/// Door opening normally reaches peers because every client runs the SAME deterministic rule
/// library over the same synchronised action; a locally forced <c>ForceActivate</c> is not an
/// action, so it changes one client's <c>ScenarioState</c> and no one else's. The game then
/// detects exactly that and logs "Map Revealed state does not match." (<c>CMap.Compare</c>). A
/// desynced session is worse than no cheat, so this one is offline-only, full stop.</item>
/// <item><b>Unlocking scenarios writes the HOST'S campaign save</b> — the party's
/// <c>CMapState</c>, which is one shared object owned by the session, not a per-client view. A
/// guest pressing it would edit a save that is not the one being played from; a host pressing it
/// mid-session would move the campaign under everyone's feet between two map phases. It could in
/// principle be made host-only, and it is not, for one reason worth writing down: the button
/// exists so the user can walk a scenario list ALONE ("ohne das spiel wirklich zu spielen"), and
/// a cheat that is correct only under a condition nobody will check is a trap. Offline-only is a
/// rule that cannot be got wrong.</item>
/// </list>
///
/// <para><b>CONFIRMATION: THE SAVE-WRITING BUTTON IS ARMED FIRST, AND THE FILE IS BACKED UP
/// BEFORE IT IS WRITTEN.</b> Both, not one — they protect against different accidents. The arm
/// step (press once → the caption changes to "REALLY?" for eight seconds → press again) protects
/// against the press the user did not mean to make; the backup copy protects against the press
/// they DID mean and regretted, which no confirmation can help with. The door button gets
/// neither and needs neither: it writes no file and its whole effect is gone the moment the
/// scenario is left.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>How long the "unlock everything" row stays armed after the first press, seconds.</summary>
    private const float CheatArmSeconds = 8f;

    /// <summary>Unscaled time the unlock row was armed at, or negative infinity while it is not.</summary>
    private static float _cheatArmedAt = float.NegativeInfinity;

    /// <summary>True while the unlock row is waiting for its confirming second press.</summary>
    private static bool CheatArmed => Time.unscaledTime - _cheatArmedAt < CheatArmSeconds;

    /// <summary>
    /// Captions that have to be repainted after a press, same shape (and same reason) as the test
    /// trigger page's <c>LatchLabels</c>: this page must not rebuild itself on a press, because a
    /// rebuild destroys the very button under the pointer — and the arm/confirm step needs exactly
    /// two presses of one button.
    /// </summary>
    private static readonly List<(TMP_Text label, Func<string> read)> CheatLabels = new(4);

    // ==========================================================================================
    //  THE GATE — [Cheats] Enabled, and it ships OFF
    // ==========================================================================================

    /// <summary>Own file so the whole feature is one <c>dev.gloomhavenvr.cheats.cfg</c> to delete.</summary>
    private static BepInEx.Configuration.ConfigFile? _cheatsFile;

    private static BepInEx.Configuration.ConfigEntry<bool>? _cheatsEnabled;

    /// <summary>
    /// Bind the one key that decides whether this page exists at all. Idempotent, and separate
    /// from the property below so <c>ConfigCatalog.EnsureBound</c> can force the file into
    /// existence when the options window opens — a gate the player cannot SEE in his config
    /// folder is not a gate he can use.
    /// </summary>
    internal static void BindCheatsConfig()
    {
        if (_cheatsEnabled != null)
            return;
        _cheatsFile = ModuleConfig.Create("cheats");
        _cheatsEnabled = _cheatsFile.Bind("Cheats", "Enabled", Defaults.CheatsEnabled,
            "OFF BY DEFAULT. Turns on the temporary 'Cheats' page under Erweitert in the VR "
            + "options menu — two test buttons that unlock every scenario in the savegame and "
            + "open every door in the current one. With this false the page does not exist: its "
            + "link is not drawn on the Erweitert index and the page itself cannot be reached. "
            + "Single-player only either way; both buttons refuse while a multiplayer session is "
            + "live. Read at the moment the menu is drawn, so a change applies the next time the "
            + "options window is opened - no restart.");

        // Once per session, at a printed tier. Without it "the Cheats page is gone" and "the
        // Cheats page failed to build" read identically in a log — and the whole point of this
        // gate is that the page's ABSENCE is the correct outcome, which is the one outcome a
        // silent log cannot distinguish from a broken build.
        // HW-VERIFY
        VRLog.Note("WorldUI", $"Cheats page gate: [Cheats] Enabled = {_cheatsEnabled.Value} "
                              + "(dev.gloomhavenvr.cheats.cfg) — "
                              + (_cheatsEnabled.Value
                                  ? "the page and its link on the Erweitert index are drawn."
                                  : "no link on the Erweitert index; the page cannot be opened."));
    }

    /// <summary>
    /// Whether the cheats page exists this session.
    ///
    /// <para>USER REQUEST, 2026-09-02, verbatim: <i>"Das Cheats-Menu sollten über die cfg
    /// aktiviert werden können (per default AUS)"</i>.</para>
    ///
    /// <para>ITS OWN CONFIG FILE, <c>dev.gloomhavenvr.cheats.cfg</c>, <c>[Cheats] Enabled</c>.
    /// Three reasons, in order of weight. (1) It is where somebody LOOKS: a player who wants the
    /// cheats on scans the config folder for the word "cheats", and finds a file with one key in
    /// it. (2) This feature is built to be DELETED (see the block at the top of this file), and a
    /// key on a shared file leaves a dead line in a player's .cfg forever; a whole file is one
    /// deletion. (3) The alternative homes are worse: the [General] section lives on the main
    /// plugin config owned by Plugin.cs, and [WorldUI] would file a cheat switch under the menu
    /// system that happens to draw it.</para>
    ///
    /// <para>THE KEY ITSELF IS NOT SHOWN IN THE MENU (ConfigCatalog.NotOffered). A gate that can
    /// be opened from inside the room it locks is not a gate — and the request is explicitly that
    /// this be a .cfg decision. Hand-editing the file still works, which is the whole point.</para>
    /// </summary>
    private static bool CheatsAvailable
    {
        get
        {
            BindCheatsConfig();
            return _cheatsEnabled != null && _cheatsEnabled.Value;
        }
    }

    /// <summary>
    /// The door into this page, drawn at the bottom of the Erweitert index. Returns the number of
    /// rows it added, so the caller's row tally (and therefore the built-page log line) stays true.
    /// </summary>
    private static int BuildCheatsIndexLink()
    {
        // NO LINK, NO PAGE. Returning 0 here is what makes "es darf niemals leere Fenster geben"
        // hold with the gate off: the Erweitert index simply has one row fewer, never a row that
        // opens onto nothing.
        if (ContentRoot == null || !CheatsAvailable)
            return 0;

        BuildLinkRow(ContentRoot, Text("Cheats (test aid)", "Cheats (Testhilfe)"), () =>
        {
            _view = View.AdvancedCheats;
            TickGuard.Run("VROptionsTab.Cheats", Rebuild, "WorldUI");
        });
        return 1;
    }

    /// <summary>
    /// THE PAGE. Two buttons, and four lines of German saying what they are before either can be
    /// pressed — the same safety-rail pattern the test-trigger page uses, and for a stronger
    /// reason: one of these buttons writes the player's savegame.
    /// </summary>
    private static int BuildCheatsPage()
    {
        if (ContentRoot == null)
            return 0;

        // SECOND LOCK, and it is not redundant with the missing link. _view is process state: a
        // player can be standing ON this page when the gate is turned off under him (the value is
        // live, and BepInEx writes the file on every edit), and the window's next show would then
        // rebuild a page the gate forbids. Falling back to the index rather than returning 0 is
        // the "niemals leere Fenster" rule — 0 rows would print the empty-list placeholder on a
        // page whose own heading says Cheats.
        if (!CheatsAvailable)
        {
            _view = View.AdvancedIndex;
            return BuildAdvancedIndex();
        }

        // The labels of the previous visit are about to be destroyed with the page. Repainting a
        // destroyed TMP_Text is a null-reference on Unity's fake-null, and the repaint runs from a
        // button callback where an exception eats the rest of the press.
        CheatLabels.Clear();
        // A page that is re-entered starts DISARMED. An arm that survived leaving and coming back
        // would mean a single press on a fresh page could write the save.
        _cheatArmedAt = float.NegativeInfinity;

        BuildLinkRow(ContentRoot, "‹ " + Loc.Mod("cat_debug"), () =>
        {
            _view = View.AdvancedIndex;
            TickGuard.Run("VROptionsTab.CheatsBack", Rebuild, "WorldUI");
        });

        BuildHeader(ContentRoot, Text("Cheats (test aid)", "Cheats (Testhilfe)"));
        BuildNote(ContentRoot, Text(
            "A temporary testing aid. It will be removed again once the walkthrough is done.",
            "Eine vorübergehende Testhilfe. Sie wird nach dem Durchtesten wieder entfernt."));
        BuildNote(ContentRoot, Text(
            "Both buttons are SINGLE-PLAYER ONLY. In a multiplayer session they refuse, because "
            + "neither change can be sent to the other players and both would desynchronise the game.",
            "Beide Knöpfe funktionieren NUR IM EINZELSPIELER. In einer Mehrspieler-Sitzung "
            + "verweigern sie, weil sich keine der beiden Änderungen an die Mitspieler senden lässt "
            + "und beide das Spiel auseinanderlaufen ließen."));

        bool online = SessionOnline();
        int rows = 0;
        rows += BuildUnlockAllLevelsRow(online);
        rows += BuildOpenAllDoorsRow(online);
        return rows;
    }

    // ---- button 1: unlock every scenario in the savegame ---------------------------------

    /// <summary>
    /// "Unlock all levels" — armed on the first press, done on the second, backed up before it
    /// writes.
    ///
    /// <para>The unlock itself is <see cref="CQuestState.UnlockQuest"/> per quest rather than the
    /// game's own <c>DebugMenu.ShowAllScenariosNoToggle()</c> alone, and that is not distrust of
    /// the seam — it is the SAVE ORDERING. That method starts a coroutine which yields a frame
    /// before it unlocks anything (<c>MapChoreographer.DebugShowAllScenariosCoroutine</c>), so a
    /// save fired straight after the call would persist the state from BEFORE the cheat, and the
    /// number in the log would be a guess. Doing the unlock inline makes both exact. The game's
    /// seam is still called, right afterwards, because it is the thing that rebuilds the map's
    /// location markers and clears the fog of war so the newly opened scenarios are actually
    /// visible — and its own <c>UnlockQuest</c> pass is a no-op by then, because
    /// <c>UnlockQuest</c> returns immediately for a quest that is no longer Locked.</para>
    ///
    /// <para>JOB QUESTS ARE SKIPPED, matching the game's own rule in that same coroutine
    /// (<c>q.Quest.Type != EQuestType.Job</c>). They are the randomly offered side jobs rather
    /// than campaign scenarios, so "all levels" does not mean them.</para>
    /// </summary>
    private static int BuildUnlockAllLevelsRow(bool online)
    {
        if (ContentRoot == null)
            return 0;

        BuildHeader(ContentRoot, Text("Savegame", "Spielstand"), sub: true);
        BuildNote(ContentRoot, Text(
            "Unlocks every campaign scenario in the loaded savegame so you can walk through all of "
            + "them. This WRITES the savegame — a copy of the old file is kept next to it first. "
            + "Press once to arm, press again to confirm.",
            "Schaltet im geladenen Spielstand alle Kampagnen-Szenarien frei, damit du durch alle "
            + "durchgehen kannst. Das SCHREIBT den Spielstand — eine Kopie der alten Datei wird "
            + "vorher daneben abgelegt. Einmal drücken zum Scharfschalten, erneut zum Bestätigen."));

        RegisterCheatRow(
            BuildLinkRow(ContentRoot, UnlockCaption(online), () => OnUnlockAllLevels(), asAction: true),
            () => UnlockCaption(SessionOnline()));
        return 1;
    }

    private static string UnlockCaption(bool online) =>
        online
            ? Text("Unlock all levels — BLOCKED (multiplayer session)",
                   "Alle Level freischalten — GESPERRT (Mehrspieler-Sitzung)")
            : CheatArmed
                ? Text("Unlock all levels — PRESS AGAIN TO CONFIRM",
                       "Alle Level freischalten — ZUM BESTÄTIGEN ERNEUT DRÜCKEN")
                : Text("Unlock all levels (writes the savegame)",
                       "Alle Level freischalten (schreibt den Spielstand)");

    private static void OnUnlockAllLevels()
    {
        try
        {
            bool online = SessionOnline();
            if (online)
            {
                VRLog.Info("WorldUI", "CHEAT 'unlock all levels': REFUSED — a multiplayer session is "
                                      + "live (FFSNetwork.IsOnline). Nothing was read and nothing was "
                                      + "written. The campaign save is the session's shared state and "
                                      + "there is no game action that could carry this change to the "
                                      + "other players.");
                return;
            }

            var map = AdventureState.MapState;
            if (map == null)
            {
                VRLog.Info("WorldUI", "CHEAT 'unlock all levels': nothing to do — no adventure is "
                                      + "loaded (AdventureState.MapState is null), so there is no "
                                      + "savegame to unlock anything in. Load a campaign party first. "
                                      + "0 levels touched, multiplayer session: no.");
                return;
            }

            if (!CheatArmed)
            {
                _cheatArmedAt = Time.unscaledTime;
                VRLog.Info("WorldUI", $"CHEAT 'unlock all levels': ARMED for {CheatArmSeconds:F0} s — "
                                      + "nothing has been changed yet. Press the same row again to "
                                      + "confirm, or leave the page and it disarms itself.");
                RefreshCheatRows();
                return;
            }
            _cheatArmedAt = float.NegativeInfinity;

            // MEASURED, NOT ASSUMED: the two counts below are read from the live CMapState on either
            // side of the loop, so the log's number is what actually changed rather than what the
            // list length suggested.
            List<CQuestState> all = map.AllQuests;
            int candidates = all.Count(q => q != null && q.Quest != null && q.Quest.Type != EQuestType.Job);
            int lockedBefore = all.Count(q => q != null && q.Quest != null
                                              && q.Quest.Type != EQuestType.Job
                                              && q.QuestState == CQuestState.EQuestState.Locked);

            string backup = BackUpAdventureSave();

            int unlocked = 0;
            foreach (CQuestState quest in all.ToList())
            {
                if (quest == null || quest.Quest == null || quest.Quest.Type == EQuestType.Job)
                    continue;
                if (quest.QuestState != CQuestState.EQuestState.Locked)
                    continue;
                quest.UnlockQuest();
                if (quest.QuestState != CQuestState.EQuestState.Locked)
                    unlocked++;
            }

            int lockedAfter = all.Count(q => q != null && q.Quest != null
                                             && q.Quest.Type != EQuestType.Job
                                             && q.QuestState == CQuestState.EQuestState.Locked);

            // The save-data API, not a file write of our own: BepInEx has no business serialising a
            // CMapState, and the game's own path is the one that deep-clones, writes on a worker and
            // updates the load-slot index.
            SaveData? save = SaveData.Instance;
            bool saved = false;
            if (save != null)
            {
                save.SaveCurrentAdventureData();
                saved = true;
            }

            // The game's own map seam, LAST: its UnlockQuest pass is a no-op by now (UnlockQuest
            // returns immediately for anything not Locked), and what it is here for is the visual
            // half — destroying and rebuilding the MapLocation markers and clearing the fog of war
            // so the newly opened scenarios can actually be walked to. A no-op when the campaign
            // map scene is not up, which is exactly right.
            DebugMenu.ShowAllScenariosNoToggle();

            VRLog.Info("WorldUI", "CHEAT 'unlock all levels' RAN. Mechanism: CQuestState.UnlockQuest() "
                                  + "on every non-Job quest of AdventureState.MapState, then "
                                  + "SaveData.SaveCurrentAdventureData(), then "
                                  + "DebugMenu.ShowAllScenariosNoToggle() for the map's own marker/fog "
                                  + $"rebuild. MEASURED: {candidates} campaign quest(s) in the save, "
                                  + $"{lockedBefore} of them Locked before, {lockedAfter} Locked after, "
                                  + $"{unlocked} state(s) actually flipped. Savegame written: "
                                  + $"{(saved ? "yes" : "NO — SaveData.Instance was null, so the unlock "
                                                        + "is in memory only and dies with the session")}. "
                                  + $"Backup: {backup}. Multiplayer session active: no (checked "
                                  + "FFSNetwork.IsOnline immediately before running). "
                                  + "WHAT WOULD DISPROVE THIS: a later log line showing quests back at "
                                  + $"Locked, or a 'Locked after' above 0 — either means UnlockQuest "
                                  + "refused (it early-returns for anything not Locked) or the map "
                                  + "reloaded the pre-cheat file over the top.");
            RefreshCheatRows();
        }
        catch (Exception e)
        {
            VRLog.Error("WorldUI", $"CHEAT 'unlock all levels' threw and was abandoned: {e}");
        }
    }

    /// <summary>
    /// Copy the party's save file next to itself before it is overwritten, and report what
    /// happened as one sentence for the log line.
    ///
    /// <para>BEST EFFORT, AND THE PRESS GOES AHEAD EITHER WAY. The confirming press has already
    /// been given at this point, so refusing the cheat because the backup could not be taken would
    /// override a decision the user just made twice; what the failure must not do is be silent,
    /// which is why the outcome is a string the log line carries rather than a bool nobody
    /// prints.</para>
    ///
    /// <para>The path is the game's own <c>PartyAdventureData.AdventureMapStateFilePath</c> — the
    /// exact file <c>SaveCurrentAdventureData</c> is about to write — rather than a path
    /// reconstructed here from the party name and the platform id, which would be a second opinion
    /// able to drift from the first.</para>
    /// </summary>
    private static string BackUpAdventureSave()
    {
        try
        {
            string? path = SaveData.Instance?.Global?.CurrentAdventureData?.AdventureMapStateFilePath;
            if (string.IsNullOrEmpty(path))
                return "NOT TAKEN — the game reported no save file path for this party";
            if (!File.Exists(path))
                return $"not needed — '{path}' does not exist yet (nothing to overwrite)";

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string copy = path + $".pre-vrcheat-{stamp}.bak";
            File.Copy(path, copy, overwrite: false);
            return $"written to '{copy}'";
        }
        catch (Exception e)
        {
            return $"NOT TAKEN — the copy threw ({e.GetType().Name}: {e.Message})";
        }
    }

    // ---- button 2: open every door / reveal every room -----------------------------------

    private static int BuildOpenAllDoorsRow(bool online)
    {
        if (ContentRoot == null)
            return 0;

        BuildHeader(ContentRoot, Text("Running scenario", "Laufendes Szenario"), sub: true);
        BuildNote(ContentRoot, Text(
            "Opens every door in the running scenario and reveals every room. Changes nothing on "
            + "disk and is gone when you leave the scenario. Only works while a scenario is running.",
            "Öffnet im laufenden Szenario alle Türen und deckt alle Räume auf. Ändert nichts auf der "
            + "Festplatte und ist beim Verlassen des Szenarios wieder weg. Funktioniert nur, während "
            + "ein Szenario läuft."));

        RegisterCheatRow(
            BuildLinkRow(ContentRoot, DoorCaption(online), () => OnOpenAllDoors(), asAction: true),
            () => DoorCaption(SessionOnline()));
        return 1;
    }

    private static string DoorCaption(bool online) =>
        online
            ? Text("Open all doors — BLOCKED (multiplayer session)",
                   "Alle Türen öffnen — GESPERRT (Mehrspieler-Sitzung)")
            : Text("Open all doors, reveal all rooms",
                   "Alle Türen öffnen, alle Räume aufdecken");

    private static void OnOpenAllDoors()
    {
        try
        {
            if (SessionOnline())
            {
                VRLog.Info("WorldUI", "CHEAT 'open all doors': REFUSED — a multiplayer session is live "
                                      + "(FFSNetwork.IsOnline). Nothing was touched. There is no game "
                                      + "action for revealing a room (FFSNet.GameActionType has none), "
                                      + "so this would change only this client's ScenarioState and the "
                                      + "session would desynchronise — the game itself detects that and "
                                      + "logs 'Map Revealed state does not match.'");
                return;
            }

            ScenarioState? state = ScenarioManager.CurrentScenarioState;
            if (state == null)
            {
                VRLog.Info("WorldUI", "CHEAT 'open all doors': nothing to do — no scenario is running "
                                      + "(ScenarioManager.CurrentScenarioState is null). 0 doors, 0 "
                                      + "rooms touched, multiplayer session: no.");
                return;
            }

            // MEASURED ON BOTH SIDES. `Activated` is the door's own flag and `Revealed` is the room's,
            // so these counts are the game's state rather than our idea of it.
            List<CObjectDoor> doors = state.Props.OfType<CObjectDoor>().ToList();
            int doorsClosedBefore = doors.Count(d => !d.Activated);
            int roomsHiddenBefore = state.Maps.Count(m => !m.Revealed);

            // The game's own cheat, whole: reveal every CMap, then ForceActivate every door (which
            // opens the pathfinder bridge and reveals both rooms the door joins, and emits the reveal
            // message the presentation layer turns into the door animation). This is exactly what the
            // game's CheatPanel "Battle Cheats ▸ RevealAllRooms" calls, and it is why nothing in
            // ScenarioRuleLibrary is patched here.
            DebugMenu.RevealAllRooms();

            int doorsClosedAfter = doors.Count(d => !d.Activated);
            int roomsHiddenAfter = state.Maps.Count(m => !m.Revealed);

            VRLog.Info("WorldUI", "CHEAT 'open all doors' RAN. Mechanism: DebugMenu.RevealAllRooms() — "
                                  + "CMap.Reveal(initial:false) on every map, then "
                                  + "CObjectDoor.ForceActivate(null) on every door prop. MEASURED: "
                                  + $"{doors.Count} door(s) and {state.Maps.Count} room(s) in the "
                                  + $"scenario; {doorsClosedBefore} door(s) were closed before and "
                                  + $"{doorsClosedAfter} after ({doorsClosedBefore - doorsClosedAfter} "
                                  + $"opened); {roomsHiddenBefore} room(s) were hidden before and "
                                  + $"{roomsHiddenAfter} after ({roomsHiddenBefore - roomsHiddenAfter} "
                                  + "revealed). Nothing was written to disk. Multiplayer session "
                                  + "active: no (checked FFSNetwork.IsOnline immediately before "
                                  + "running). WHAT WOULD DISPROVE THIS: a non-zero 'closed after' or "
                                  + "'hidden after' means ForceActivate/Reveal declined for those props "
                                  + "— the dungeon entrance is expected to be among them — and doors "
                                  + "that report open here but stay shut on screen mean the reveal "
                                  + "message reached the rules and not the presentation layer.");
            RefreshCheatRows();
        }
        catch (Exception e)
        {
            VRLog.Error("WorldUI", $"CHEAT 'open all doors' threw and was abandoned: {e}");
        }
    }

    // ---- shared plumbing ------------------------------------------------------------------

    /// <summary>
    /// Is a networked session live? Asked of the game's own <c>FFSNetwork.IsOnline</c> (which is
    /// <c>BoltNetwork.IsRunning</c> plus the session checks) and asked AGAIN inside each press
    /// rather than only when the page was built: the page does not rebuild itself, so a caption
    /// drawn before a session started must never be the thing that decides whether a cheat runs.
    /// Fails CLOSED — if the check throws, the answer is "a session is live" and the cheat refuses.
    /// </summary>
    private static bool SessionOnline()
    {
        try
        {
            return FFSNetwork.IsOnline;
        }
        catch (Exception e)
        {
            VRLog.Warn("WorldUI", $"CHEAT: FFSNetwork.IsOnline threw ({e.Message}) — treating the "
                                  + "session as ONLINE so both cheats refuse. A cheat that cannot "
                                  + "tell whether it would desync must not run.");
            return true;
        }
    }

    /// <summary>
    /// Remember a row's caption so <see cref="RefreshCheatRows"/> can repaint it in place — the
    /// arm/confirm step and the multiplayer-blocked caption both need to change without the page
    /// rebuilding under the pointer. Same mechanism, and the same "Title child, not the first TMP"
    /// rule, as the test-trigger page.
    /// </summary>
    private static void RegisterCheatRow(GameObject row, Func<string> caption)
    {
        TMP_Text? label = FindPart<TMP_Text>(row.transform, "Title");
        if (label == null)
            return;
        CheatLabels.Add((label, caption));
    }

    private static void RefreshCheatRows()
    {
        for (int i = 0; i < CheatLabels.Count; i++)
        {
            (TMP_Text label, Func<string> read) = CheatLabels[i];
            if (label == null)
                continue;
            try
            {
                label.text = read();
            }
            catch (Exception e)
            {
                VRLog.Warn("WorldUI", $"CHEAT: repainting a cheat row threw ({e.Message}). The buttons "
                                      + "still work; only the caption is stale until the next press.");
            }
        }
    }

    /// <summary>
    /// The page's whole string table, deliberately inline instead of in <c>Core/Loc.cs</c>.
    ///
    /// <para>A temporary feature that scatters translation keys through a permanent table leaves
    /// orphans behind when it goes, and the orphans are invisible — nothing fails, the keys simply
    /// sit there forever. Keeping the strings in the file that is going to be deleted makes the
    /// removal complete by construction. The language question is asked of the same authority
    /// <see cref="Loc.Mod"/> asks (<see cref="Loc.CurrentLanguage"/>), so this page follows the
    /// menu's language like every other page; anything that is not German gets the English text,
    /// which is <see cref="Loc.Mod"/>'s own fallback rule.</para>
    /// </summary>
    private static string Text(string english, string german) =>
        string.Equals(Loc.CurrentLanguage, "German", StringComparison.Ordinal) ? german : english;
}
