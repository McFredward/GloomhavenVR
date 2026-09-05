using System;
using GloomhavenVR.Core;
using HarmonyLib;
using MapRuleLibrary.MapState;
using UnityEngine;
using UnityEngine.UI;   // UIWindow — the game ships it in this namespace

namespace GloomhavenVR.WorldUI.MapRoom;

// ---------------------------------------------------------------------------
// THE JOINT QUEST START IN MULTIPLAYER — TWO REPORTED FAULTS THAT ARE ONE FAULT,
// AND THE ONE THING THAT WAS ACTUALLY MISSING.
//
// USER REPORT (2026-08-23), the fault, verbatim:
//
//   "4a) Zuerst haben wir probiert gemeinsam in ein Szenario zu laden. Das hat nicht geklappt auch
//    nicht wenn sie den button gedrückt hat."
//
// USER REPORT, the change, verbatim:
//
//   "5) Ich möchte, dass du änderst wie man im Multiplayer gemeinsam eine Quest startet. Anstatt auf
//    den physischen Map-Button möchte ich das das derselbe Button unter der Quest-Info-Tafel für
//    alle Spieler sichtbar sind. Dort müssen alle Mitspieler bestätigen damit es weiter geht."
//
// and the clarification that fixes the target exactly:
//
//   "Der Host sieht ja den button 'Quest starten' und er kann draufdrücken. Ich will, dass die
//    anderen Mitspieler genau an der selben Stelle auch den selben oder ähnlichen button sehen und
//    genau an der selben Stelle bestätigen."
//
// ═══ 1. THE HOST SIDE ALREADY WORKS, AND THE LOG PROVES IT ═══════════════════
//
// This is checked BEFORE anything is designed, because the one thing this lane must not do is build
// a second implementation of a thing that already works. ModBuild 231, host log,
// .planning/debug/Player.log:82708 — the placement line, in the middle of the two-player session:
//
//     MAP TRAVEL CONFIRM placement — INSIDE the quest window 'UI Quest Popup', …
//       container : 'Multiplayer Ready Toggle' rect (x:-153.50, y:-65.00, width:307.00, height:65.00)
//       applied   : anchoredPosition = (257.73, -735.84), putting the container's pivot at
//                   (1.73, -225.34) in window-local space = the measured zero (-2.2, -225.3) …
//       button    : 'Adventure button' active=False …
//
// Three facts in one line, and all three matter:
//
//   * WHAT THE HOST SEES ONLINE IS THE SINGLETON `UIReadyToggle` ('Multiplayer Ready Toggle', a
//     307x65 UIWindow), parked by <see cref="MapTravelConfirm"/> INSIDE the floated quest card at
//     the ModBuild 197 moving zero. Its label for the quest ready-up is GUI_SELECT_QUEST
//     (MapChoreographer.InitializeSelectQuestReadyUp, decompiled MapChoreographer.cs:3579-3600).
//   * THE OFFLINE 'Adventure button' IS NOT IT. It reads `active=False`, exactly as
//     `AdventureMapUIManager.EnableTravelOptions` says it must online
//     (`travelButton.gameObject.SetActive(!FFSNetwork.IsOnline)`, decompiled
//     AdventureMapUIManager.cs:412). So the two candidates are not both live; only one is.
//   * THE ANCHOR IS THE MEASURED ONE. `(dial.x, dial.y) x 1021.0` off a zero solved from the card's
//     own painted content — nothing about it is host-specific.
//
// So the HOST's presentation is the REFERENCE and it is not touched by this file. What is missing is
// on the client, and it is not a placement problem at all.
//
// ═══ 2. WHY THE CLIENT NEVER GOT A BUTTON — READ FROM SOURCE, CONFIRMED IN THE LOG ═══
//
// The game action crossed the wire and was processed. Host Player.log:83124 `[[Sending GameAction #2
// (SelectQuest @ MapHQ) to clients.]]`; client .planning/debug/remote/Player.log:75225 `[[Received
// GameAction #2 (SelectQuest @ MapHQ) …]]`, :75237 `[[Processing …]]`. The client's ENTIRE reaction
// is four lines (remote/Player.log:75239-75242):
//
//     MAP: Host Selected Location Quest_Campaign_003_Scenario0
//     MP Ready Toggle set to  UNINTERACTABLE.
//     Determining client toggle interactability True JJ_Pueppi
//     MP Ready Toggle set to  INTERACTABLE.
//
// and then nothing. `MP Ready Toggle set to  VISIBLE.` occurs THREE times in the host log and ZERO
// times in the client log; `Multiplayer Ready Toggle` occurs 127 times on the host and ONCE on the
// client (a hide at startup, remote/Player.log:4642). The toggle's GameObject exists on the client
// and is never shown, so `UIWindow.Show` never fires, so no `UIWindow SHOWN`, so the mod's catch-all
// is never offered it, so there is no world-space panel and nothing to press. That is the deadlock,
// and it is upstream of every VR concern.
//
// THE GATE, from decompiled/:
//
//   MapChoreographer.ProxySelectedLocation      (:3373)  → InitializeSelectQuestReadyUp() (:3388)
//                                                        → UIMapMultiplayerController
//                                                          .ProxyHostSelectedLocation(location)
//   UIMapMultiplayerController.ProxyHostSelectedLocation  (:759)
//       hostSelectedLocation = location;                                          // :770
//       if (!IsChoosingLinkedQuestOption())                                       // :771
//           GuildmasterConfirmAction.ShowQuestSelectedAction(location.LocationQuest,
//                                                            () => PreviewQuest());// :773-776
//       else { HideQuestSelectedAction(); PreviewQuest(isCancellable: false); }   // :780-781
//   UIMapMultiplayerController.PreviewQuest              (:667)
//       … ToggleReadyUpUI(show: true, EReadyUpToggleStates.Quests);               // :682
//
// `PreviewQuest` is the ONLY thing on a client that makes the quest ready-up visible, and on the
// non-linked branch it is reachable ONLY by clicking a prompt that `UIGuildmasterHUD` draws
// (`UIGuildmasterConfirmActionButtonPresenter` — a marker button on the flat guildmaster bar — or
// `UIGuildmasterConfirmActionPopupPresenter` on the console layout). The 3D map room replaces that
// bar with physical button caps on the table and never draws it. A PROMPT NOBODY CAN SEE IS A PROMPT
// NOBODY CAN ANSWER. Falsified rather than assumed: the client log's floated set is exhaustively
// 'UI Quest Preview Popup', 'UI Map Esc Menu', 'UI Options Window_unified', 'UI Quest Popup', 'Quest
// Log Manager', 'New Party display' — no confirm-action window and no guildmaster window at all.
//
// ═══ 3. THE SEAM, AND WHY THE ALTERNATIVES LOSE ══════════════════════════════
//
// This file POSTFIXES `ShowQuestSelectedAction` on both concrete presenters and, while the 3D map
// room stands, invokes THE CALLBACK THE GAME JUST REGISTERED — the same `Action` the prompt's own
// click handler would invoke (`UIGuildmasterConfirmActionButton.OnClicked` →
// `_onConfirmCallback?.Invoke()`, decompiled UIGuildmasterConfirmActionButton.cs:92-95;
// `UIGuildmasterConfirmActionPopup.Confirm`, UIGuildmasterConfirmActionPopup.cs:89-92). Nothing is
// re-implemented; `PreviewQuest` is never named, never reflected for, and never copied.
//
//   * WHY NOT A POSTFIX ON `ProxyHostSelectedLocation`. It would have to REACH the callback — it is
//     a lambda closed over the controller and handed straight into a private `_button` /
//     `_popup` field — so it would mean reflection into two private fields in two classes, or a
//     reflected call to the private `PreviewQuest`. And it would have to re-test
//     `IsChoosingLinkedQuestOption()` itself. The postfix on `ShowQuestSelectedAction` takes the
//     callback AS AN ARGUMENT and gets the linked-quest exclusion FOR FREE, because that branch
//     (UIMapMultiplayerController.cs:780-781) never calls this method at all — it calls
//     `HideQuestSelectedAction()` and `PreviewQuest(isCancellable: false)` directly. A structural
//     guarantee beats a test that can rot; the value is nevertheless PRINTED on the drive line, so
//     the claim stays falsifiable from one grep.
//   * WHY NOT SURFACE THE PROMPT IN THE ROOM. It is not a `UIWindow` — it is a child of
//     `UIGuildmasterHUD`, which the map room deliberately does not draw (`IsKnownHudWindow` refuses
//     to float it, and the button rail replaces it). Floating a HUD child would fight the subsystem
//     that owns it, and it would add a SECOND thing to press for one decision. The user asked for
//     ONE button, in one place, for everybody.
//   * IS A DECISION BEING SKIPPED? NO, AND THIS IS READ FROM SOURCE RATHER THAN ASSERTED. Both
//     presenters carry a confirm callback and NOTHING ELSE — no decline, no second option
//     (UIGuildmasterConfirmActionButtonPresenter.cs:12-31 wires onClicked/onHover/onUnhover, where
//     hover only shows a quest preview popup; UIGuildmasterConfirmActionPopup.Show:72-79 stores one
//     `_onConfirmCallback`). The prompt is purely "the host picked one, here it is". The real
//     decision survives untouched and lands one step later: `PreviewQuest(isCancellable: true)`
//     activates the cancel button (:684) and the parked toggle itself un-readies on a second press
//     (`UIReadyToggle.InputToggle` → `ReadyUp(false)`), which is the client's decline.
//
// IT RUNS ONE TICK LATER, ON PURPOSE, AND THAT IS CLOSER TO VANILLA THAN RUNNING IN PLACE.
// In the flat game the callback fires from a uGUI click in `EventSystem`'s Update, seconds after the
// prompt was raised — never from inside `MapChoreographer.ProxySelectedLocation`. That method wraps
// its whole body in a try/catch whose handler is
// `GlobalErrorMessage.ShowMessageDefaultTitle(… ErrorHandlingUnloadSceneAndLoadMainMenu …)`
// (MapChoreographer.cs:3390-3396): anything thrown on that stack KICKS THE PLAYER TO THE MAIN MENU.
// So the postfix only RECORDS the callback, and <see cref="TickPendingClientPrompt"/> — called once
// per frame from <see cref="MapTravelConfirm.Reconcile"/>, i.e. from Update, i.e. exactly where a
// uGUI click callback runs — drives it on the next tick, behind its own try/catch.
//
// RE-VALIDATED IMMEDIATELY BEFORE THE DRIVE, so a host who cancels inside that one frame cannot make
// us call a callback closed over a `hostSelectedLocation` that is already null. The test is the
// game's own PUBLIC `UIMapMultiplayerController.HostSelectedQuest` (:34, literally
// `hostSelectedLocation?.LocationQuest`) compared by reference against the quest the prompt was
// raised for. Idempotent for the same reason plus one more: a pending drive is dropped the moment
// the toggle is already visible, so nothing can run `PreviewQuest` twice.
//
// NOTHING GOES ON THE WIRE FROM HERE. This file sends no action, opens no channel and writes no
// replicated state. It presses a button on this client that the flat game draws and the room does
// not. The ready-up itself remains the game's own `ReadyUpPlayer` GameAction
// (UIReadyToggle.cs:657-665), sent by the game's own toggle when the player presses it.
//
// ═══ 4. WHO DECIDES "EVERYBODY HAS CONFIRMED" — ONE OBJECT, NAMED ════════════
//
// `UIReadyToggle` ON THE HOST, and nothing else. Read from source so nobody builds a second one:
//
//   UIReadyToggle.ReadyUpPlayer      (:643)  adds the player to `PlayersReady` and, on the HOST only
//                                            (:667-676), when
//                                              readyUpType == Participant
//                                              && PlayersReady.Count >= PlayerRegistry.Participants.Count
//                                              && playersAwaited.Count == 0
//                                              && PlayersReady.Contains(MyPlayer)
//                                            starts `WaitForStateSyncBeforeProceeding`.
//   WaitForStateSyncBeforeProceeding (:679)  waits for every peer's controllable-state ACK, then
//                                            sends `GameActionType.ReadyProceed` to everybody (:721)
//                                            and calls `Proceed()` locally.
//   Proceed                          (:725)  invokes `onAllPlayersReady`.  Clients reach the same
//                                            `Proceed()` through `ClientProceed()` (:829) when they
//                                            process ReadyProceed.
//
// For the QUEST ready-up `onAllPlayersReady` is, on the host,
// `AdventureMapUIManager.ConfirmTravel()` (MapChoreographer.cs:3589-3594) and, on a client,
// `ToggleReadyUpUI(show: false, Quests)` + `EnableHeadquartersOptions(false)` (:3616-3620) — the
// journey itself then arrives as the host's own travel choreography. So "all players must confirm"
// is ALREADY the game's contract and this round adds nothing to it; what this round fixes is that
// one of the players could not reach the button that casts his vote.
//
// WHAT THE WAITING PLAYERS SEE. `UIMapMultiplayerController.OnReady(true)` shows
// `HelpBox.Show("GUI_WAIT_PLAYERS_CONFIRM_TIP", "GUI_WAIT_PLAYERS_CONFIRM_QUEST")` (:352) and
// `ToggleReadyUpUI` shows `GUI_ACCEPT_QUEST_TIP` / `GUI_WAIT_PLAYERS_CONFIRM_TIP` on a client
// (:411-421). The HelpBox is part of the flat HUD the room does not draw, so a VR player does NOT
// see that text today — but he does see the button itself change state (the parked toggle's label
// flips GUI_SELECT_QUEST/GUI_ACCEPT_QUEST → GUI_CANCEL and it greys out when it is not
// interactable), and the tracker bar is the game's own second signal. Stated here so the next round
// can decide whether the HelpBox is worth surfacing; it is NOT a deadlock and it is not fixed here.
// ---------------------------------------------------------------------------

/// <summary>
/// The multiplayer quest confirm in the 3D map room: makes a CLIENT reach the game's own quest
/// ready-up (which is otherwise gated behind a prompt drawn on the flat guildmaster HUD), and owns
/// the <see cref="ReadyToggleParkClaim"/> that says whether that confirm is being presented inside
/// the quest card. Installed and ticked by <see cref="MapTravelConfirm"/>. See the block comment
/// above for the seam, the source citations and the hardware log lines behind every claim.
/// </summary>
internal static class MapQuestReadyUp
{
    private const string Scope = "MapRoom";

    private static bool _installed;

    // ---- the captured prompt (A) -----------------------------------------------------------------

    /// <summary>The callback the game handed to <c>ShowQuestSelectedAction</c> and has not yet had
    /// answered, or null. THIS IS THE GAME'S OWN DELEGATE, stored and invoked — never rebuilt.</summary>
    private static Action? _pendingConfirm;

    /// <summary>The quest the pending prompt was raised for, compared by REFERENCE against
    /// <c>UIMapMultiplayerController.HostSelectedQuest</c> immediately before the drive.</summary>
    private static CQuestState? _pendingQuest;

    /// <summary>Which presenter raised it — printed, because which one is in the scene is a SCENE
    /// fact (the console layout uses the popup, the desktop layout the marker button) and cannot be
    /// read from source.</summary>
    private static string _pendingSeam = "<none>";

    /// <summary>Re-entrancy guard: the drive calls into the game, which could in principle raise
    /// another quest-selected prompt on the same stack. Such a prompt is IGNORED rather than queued —
    /// it would be this class answering itself. (Synchronous recursion is already impossible because
    /// the postfix only records; this closes the remaining case where the recorded prompt would come
    /// from our own call.)</summary>
    private static bool _driving;

    /// <summary>One Warn per session if the drive throws — the same rule every other guarded seam in
    /// this room follows: name the consequence once, never per frame.</summary>
    private static bool _driveThrewLogged;

    // ---- the park claim (C) ----------------------------------------------------------------------
    //
    // THE CLAIM IS MADE ON INTENT, NOT ON COMPLETION, AND THAT ORDERING IS LOAD-BEARING.
    //
    // The reader (the catch-all's float gate) is consulted the moment the game SHOWS the toggle, and
    // in both the ModBuild 225 and the ModBuild 231 logs the toggle's `UIWindow SHOWN` PRECEDES the
    // quest popup's. A claim asserted only after the reparent has landed therefore always arrives one
    // or more frames late: the toggle floats first, the gate withdraws the float, and
    // CanvasConversion.Release restores it to its recorded 2D home under 'Campaign Canvas' — undoing
    // a park that had already happened and costing a visible frame before this class re-parks it.
    // Both sides are level-triggered so it converges either way; claiming early makes it converge
    // with nothing on screen. The tell is one 'FLOAT WITHDRAWN' line per quest selection instead of
    // none.
    //
    // (The same lane established that the ModBuild 226 `WindowGroups` row — leader UIQuestPopup,
    // member UIReadyToggle — has never once fired, because `CatchAllEligible` is only reached after
    // the catch-all's `oursAlready` re-add and the member's Show beats its leader's. NOTHING IN THIS
    // FILE DEPENDS ON THAT ROW. The claim is the whole mechanism, and it is asserted from a state
    // this class can see one phase EARLIER than any window exists: the singleton toggle being
    // initialised into the Quests ready-up.)
    //
    // SO WHAT EXACTLY IS "INTENT"? Three terms, and only the first two are about wanting it:
    //
    //   the 3D map room stands           — outside it the flat HUD draws the confirm and this class
    //                                      has no business claiming anything;
    //   the singleton toggle is serving  — UIReadyToggle is a SINGLETON reused for city events,
    //   the QUESTS ready-up                rewards, retirement and town records, and claiming the
    //                                      RETIREMENT confirm would make a confirm nobody can reach;
    //   the parking has not stood down   — if this class cannot park, it must not stop the float.
    //
    // VISIBILITY IS DELIBERATELY NOT ONE OF THEM. The claim only ever says "if this object floats,
    // refuse it, because it is mine" — an invisible window never floats, so claiming it early costs
    // nothing and buys the whole race.
    //
    // AND THE CLAIM IS RELEASED AGAIN WHEN THE CONFIRM WOULD OTHERWISE BE INVISIBLE. If the toggle is
    // ON SCREEN and this class has not managed to park it for <see cref="ClaimGraceSeconds"/> — the
    // player closed the quest card, or the card never floated — the claim is dropped, the confirm
    // floats as its own small window and the player can still press it. That is the older and
    // stronger of the two rules: a window must never be invisible.

    private enum ClaimState
    {
        /// <summary>Nothing claimed.</summary>
        None,

        /// <summary>Claimed while the game is not drawing the toggle at all. This is the claim that
        /// beats the toggle's own <c>Show</c> to the float gate, and it can never hide anything:
        /// there is nothing on screen to hide.</summary>
        IntentStandingBy,

        /// <summary>Claimed while the toggle IS drawn and the quest card has not become a live
        /// floated panel yet. Bounded by <see cref="ClaimGraceSeconds"/> — this is the only state in
        /// which the claim could keep something invisible, so it is the only one with a clock.</summary>
        IntentWaitingForCard,

        /// <summary>Claimed and actually parked inside a floated quest window.</summary>
        Parked,
    }

    /// <summary>
    /// How long the confirm may stay claimed-but-not-parked while it is ON SCREEN, seconds. It has to
    /// cover the few frames between the game showing the toggle and the quest card becoming a live
    /// floated panel (the catch-all's own enrolment grace plus one conversion), and it has to be
    /// short enough that a player who CLOSES the quest card gets his button back almost at once.
    ///
    /// <para>It has nothing to do with <c>ReadyToggleParkClaim.ClaimLifetimeSeconds</c>: that one
    /// bounds how long a claim survives WITHOUT BEING RE-ASSERTED (i.e. how quickly a dead parker
    /// releases its refusal), while this one bounds how long a LIVE parker may keep asserting a claim
    /// it cannot make good on.</para>
    ///
    /// <para>ONE VALUE, EVERY PARKER: <see cref="ChromeParkTuning.ClaimGraceSeconds"/>, which
    /// carries this bound and the ruling behind it once. <c>LoadoutConfirmPark</c> held a copy whose
    /// own comment said "MapQuestReadyUp's ClaimGraceSeconds, for its reason" (ModBuild 439, survey
    /// row R35). The paragraph above about ReadyToggleParkClaim.ClaimLifetimeSeconds travelled with
    /// it.</para>
    /// </summary>
    private const float ClaimGraceSeconds = ChromeParkTuning.ClaimGraceSeconds;

    private static ClaimState _claimState = ClaimState.None;
    private static GameObject? _claimed;
    private static UIWindow? _claimHost;
    private static string _claimWhy = "nothing is claimed";

    /// <summary>When the toggle first became VISIBLE while unparked, or negative while it is parked
    /// or off screen. The grace above is measured from here.</summary>
    private static float _visibleUnparkedSince = -1f;

    /// <summary>One Warn per unparked episode, cleared as soon as the confirm is parked again, so a
    /// player who closes and re-opens the quest card gets one line per close and not one per
    /// frame.</summary>
    private static bool _cannotParkWarned;

    // ---- installation ------------------------------------------------------------------------------

    /// <summary>
    /// Register the two postfixes exactly once. Called from <see cref="MapTravelConfirm.Install"/>,
    /// which the map room's engage path already runs — for the same reason that class states for not
    /// registering from <c>WorldUIModule</c>: other lanes own that file.
    /// </summary>
    internal static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        VRSession.Harmony?.PatchAll(typeof(ClientQuestPromptSeam));
        VRLog.Info(Scope, "MAP QUEST READY-UP installed — while the 3D map room stands, a CLIENT's "
                          + "quest confirm is driven through the game's OWN seam: the callback the "
                          + "game hands to UIGuildmasterConfirmActionPresenter.ShowQuestSelectedAction "
                          + "(decompiled UIMapMultiplayerController.cs:773) is invoked exactly as the "
                          + "prompt's own click handler would invoke it, one tick later, from Update. "
                          + "READ FROM SOURCE: UIMapMultiplayerController.PreviewQuest (:667-685) is "
                          + "the ONLY thing that makes a client's quest ready-up visible, and it hangs "
                          + "off a prompt that UIGuildmasterHUD draws — a HUD this room replaces with "
                          + "the table's button rail and never draws, which is why ModBuild 231's "
                          + "client log contains 'MP Ready Toggle set to  VISIBLE.' ZERO times against "
                          + "the host's three. Nothing is re-implemented, nothing goes on the wire, "
                          + "and the linked-quest option flow is untouched because that branch "
                          + "(UIMapMultiplayerController.cs:780-781) never calls the patched method. "
                          + "Once the toggle is up, MapTravelConfirm parks it on the client's own quest "
                          + "card at the SAME measured zero and the SAME two [WorldUI] "
                          + "TravelButtonOffset dials the host's is parked at.");
    }

    // ---- A: driving the client's confirm ----------------------------------------------------------

    /// <summary>
    /// Record the prompt the game just raised, to be answered on the next tick. Runs on EVERY client
    /// whether or not the room stands — the room test belongs at the DRIVE, because the room can
    /// stand down between the two and the flat HUD's own prompt is then reachable again.
    /// </summary>
    private static void CapturePrompt(string seam, CQuestState? quest, Action? onConfirm)
    {
        if (onConfirm == null || quest == null)
            return;
        if (_driving)
            return;
        _pendingConfirm = onConfirm;
        _pendingQuest = quest;
        _pendingSeam = seam;
    }

    /// <summary>
    /// Answer a captured prompt, or drop it and say why. One call per frame from
    /// <see cref="MapTravelConfirm.Reconcile"/>; costs one null compare when there is nothing pending,
    /// which is every frame but one per quest selection.
    /// </summary>
    internal static void TickPendingClientPrompt()
    {
        if (_pendingConfirm == null)
            return;

        // THE ROOM TEST IS HERE AND NOT AT THE CAPTURE, and it WAITS rather than dropping. Outside the
        // room the game draws its own guildmaster HUD and its own prompt works exactly as it always
        // did, so there is nothing to do — and if the player answers it there, the idempotence gate
        // below drops this pending prompt the moment the room comes up. If he does NOT answer it and
        // then enters the room, waiting is what lets him answer it at all. (Unreachable through the
        // only call site, which is inside MapRoomDriver.TickActive; kept because it makes the
        // method's contract true on its own rather than by where it happens to be called from.)
        if (!MapRoomDriver.Active)
            return;

        CQuestState? live = null;
        try
        {
            if (Singleton<UIMapMultiplayerController>.IsInitialized)
            {
                UIMapMultiplayerController mp = Singleton<UIMapMultiplayerController>.Instance;
                if (mp != null)
                    live = mp.HostSelectedQuest;
            }
        }
        catch (Exception e)
        {
            Drop($"UIMapMultiplayerController.HostSelectedQuest threw ({e.Message}) — the prompt "
                 + "cannot be validated, so it is not answered");
            return;
        }

        if (!ReferenceEquals(live, _pendingQuest))
        {
            Drop("the host's selection is no longer the quest this prompt was raised for "
                 + "(UIMapMultiplayerController.HostSelectedQuest changed between the prompt and this "
                 + "tick — he cancelled or picked another one). Answering it would invoke a callback "
                 + "closed over a location the game has already cleared");
            return;
        }

        if (ReadyToggleIsUp())
        {
            Drop("the quest ready-up is ALREADY visible — PreviewQuest has run for this selection, so "
                 + "there is nothing left to advance. This is the idempotence gate; seeing it is "
                 + "normal when two prompts arrive for one selection");
            return;
        }

        // NO WALL-CLOCK DEADLINE, DELIBERATELY. Every path through this method either drives or drops
        // with a named reason, and the first tick of the room after a capture reaches one of them —
        // so a timeout could only ever fire in the one case it would be WRONG in: a prompt captured
        // while the player was still on the flat map, waiting for him to enter the room. The pending
        // prompt is bounded by Reset() (the room standing down) and by the three gates above.

        Action confirm = _pendingConfirm;
        string seam = _pendingSeam;
        CQuestState quest = _pendingQuest!;
        _pendingConfirm = null;
        _pendingQuest = null;

        bool linked = ChoosingLinkedQuestOption();
        VRLog.Info(Scope, $"MAP QUEST READY-UP: client confirm DRIVEN for quest '{QuestName(quest)}' "
                          + $"through the game's own seam {seam}.ShowQuestSelectedAction — the exact "
                          + "Action the game registered on that prompt is invoked here, which is what "
                          + "UIGuildmasterConfirmActionButton.OnClicked / "
                          + "UIGuildmasterConfirmActionPopup.Confirm do when the prompt is clicked "
                          + "(decompiled UIGuildmasterConfirmActionButton.cs:92-95, "
                          + "UIGuildmasterConfirmActionPopup.cs:89-92). It lands in "
                          + "UIMapMultiplayerController.PreviewQuest (:667), which is the ONLY thing "
                          + "that makes a client's quest ready-up visible — expect the game's own 'MP "
                          + "Ready Toggle set to  VISIBLE.' on the next lines, and then this room's "
                          + "'MAP QUEST READY-UP: park claim SET'. THE PROMPT CARRIES NO DECISION: "
                          + "both presenters register a confirm callback and nothing else, so nothing "
                          + "the player is entitled to choose is skipped — the decline is the cancel "
                          + "button PreviewQuest activates and the toggle's own second press. "
                          + $"linkedQuestOption={linked} (this seam is unreachable on the linked-quest "
                          + "branch by construction — UIMapMultiplayerController.cs:780-781 calls "
                          + "PreviewQuest directly and never raises a prompt — so a TRUE here would be "
                          + "a real finding and must be reported).");

        _driving = true;
        try
        {
            confirm();
        }
        catch (Exception e)
        {
            if (!_driveThrewLogged)
            {
                _driveThrewLogged = true;
                VRLog.Warn(Scope, "MAP QUEST READY-UP: the game's own quest-selected callback THREW "
                                  + $"while being driven ({e.GetType().Name}: {e.Message}). It is "
                                  + "caught here on purpose: this runs from the map room's Update tick "
                                  + "rather than from inside MapChoreographer.ProxySelectedLocation, "
                                  + "whose own catch would have unloaded the scene and returned the "
                                  + "player to the main menu (decompiled MapChoreographer.cs:3390-3396). "
                                  + "CONSEQUENCE: this client's quest confirm did not appear for this "
                                  + "selection; the host cancelling and re-selecting raises a fresh "
                                  + "prompt and tries again. One line per session.\n" + e.StackTrace);
            }
        }
        finally
        {
            _driving = false;
        }
    }

    /// <summary>Forget a pending prompt with a stated reason. Info, not Warn: every reason this is
    /// called with is a legitimate state, and the line exists so that "it was dropped" and "it was
    /// never captured" are different readings of a log.</summary>
    private static void Drop(string why)
    {
        _pendingConfirm = null;
        _pendingQuest = null;
        VRLog.Info(Scope, $"MAP QUEST READY-UP: client confirm NOT driven — {why}. The seam that "
                          + $"raised it was {_pendingSeam}.ShowQuestSelectedAction.");
        _pendingSeam = "<none>";
    }

    /// <summary>Is the game's ready toggle on screen right now? <c>UIReadyToggle.IsVisible</c> is
    /// literally <c>window.IsOpen</c> (decompiled UIReadyToggle.cs:138), which is the only honest
    /// test — a UIWindow hide is a CanvasGroup fade and need not deactivate the GameObject.</summary>
    private static bool ReadyToggleIsUp()
    {
        if (!Singleton<UIReadyToggle>.IsInitialized)
            return false;
        UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
        return toggle != null && toggle.IsVisible;
    }

    /// <summary>The game's own linked-quest predicate, read only so the drive line can PRINT it. The
    /// seam is unreachable on that branch by construction; printing the value is what keeps that a
    /// falsifiable claim rather than a comment.</summary>
    private static bool ChoosingLinkedQuestOption()
    {
        try
        {
            if (!Singleton<MapChoreographer>.IsInitialized)
                return false;
            MapChoreographer choreo = Singleton<MapChoreographer>.Instance;
            return choreo != null && choreo.IsChoosingLinkedQuestOption();
        }
        catch (Exception)
        {
            // A missing choreographer is a real state (the map is being rebuilt), not an error.
            return false;
        }
    }

    /// <summary>A quest's own id for the log, never localised text — a tester greps for this.</summary>
    private static string QuestName(CQuestState? quest)
    {
        try
        {
            return quest != null ? quest.ID : "<none>";
        }
        catch (Exception)
        {
            return "<unreadable>";
        }
    }

    // ---- C: the park claim ------------------------------------------------------------------------

    /// <summary>
    /// RE-ASSERT (or drop) THE CLAIM, every tick, with the live answer. Called at the end of
    /// <see cref="MapTravelConfirm.Reconcile"/>; <see cref="ReadyToggleParkClaim"/> is a LEVEL with a
    /// one-second lifetime, not a latch, so a parker that stops running releases the refusal by
    /// itself and the confirm floats on its own — ugly, and reachable, which is the correct trade.
    /// </summary>
    /// <param name="questConfirmToggle">The singleton ready toggle while it is serving the QUESTS
    /// ready-up, whether or not it is on screen, or null when it is serving something else or does
    /// not exist. THIS IS THE INTENT: it is what makes the object ours to claim.</param>
    /// <param name="questConfirmVisible">Is that toggle actually drawn right now
    /// (<c>UIReadyToggle.IsVisible</c> = <c>window.IsOpen</c>)? Only in this state can an unparked
    /// confirm be a fault, and only in this state does the grace clock run.</param>
    /// <param name="parkedToggle">The ready toggle the parker is holding inside a quest card THIS
    /// TICK, or null when it is holding something else (the offline travel options) or nothing.</param>
    /// <param name="host">The floated quest window it is parked in, for the log line.</param>
    /// <param name="questCardFloated">Is a quest card a live floated panel right now? One of the two
    /// causes the Warn has to choose between.</param>
    /// <param name="parkingStoodDown">Has the parking itself refused this visit? The other cause, and
    /// also a reason not to claim at all — a class that cannot park must not stop the float.</param>
    internal static void TickClaim(GameObject? questConfirmToggle, bool questConfirmVisible,
                                   GameObject? parkedToggle, UIWindow? host,
                                   bool questCardFloated, bool parkingStoodDown)
    {
        bool parked = parkedToggle != null && host != null;
        bool ours = MapRoomDriver.Active && questConfirmToggle != null && !parkingStoodDown;

        if (!ours)
        {
            DropClaim(!MapRoomDriver.Active
                ? "the 3D map room is not standing"
                : parkingStoodDown
                    ? "the parking has stood down for this map-room visit, so this class cannot "
                      + "promise to show the button anywhere"
                    : "the singleton ready toggle is not serving the QUESTS ready-up (it is a "
                      + "singleton shared with city events, rewards, retirement and town records, and "
                      + "those belong to their own windows, not to the quest card)");
            _visibleUnparkedSince = -1f;
            _cannotParkWarned = false;
            return;
        }

        if (parked)
        {
            _visibleUnparkedSince = -1f;
            _cannotParkWarned = false;
            SetClaim(ClaimState.Parked, parkedToggle!, host);
            return;
        }

        if (!questConfirmVisible)
        {
            // NOT DRAWN, SO NOTHING CAN FLOAT AND NOTHING CAN BE UNREACHABLE. This is the claim that
            // beats the toggle's own Show to the float gate — see the block above.
            _visibleUnparkedSince = -1f;
            _cannotParkWarned = false;
            SetClaim(ClaimState.IntentStandingBy, questConfirmToggle!, null);
            return;
        }

        // ON SCREEN AND NOT PARKED. Hold the claim for the few frames the quest card needs to become
        // a live floated panel, then let go: a confirm nobody can reach is worse than an ugly one.
        if (_visibleUnparkedSince < 0f)
            _visibleUnparkedSince = Time.unscaledTime;
        if (Time.unscaledTime - _visibleUnparkedSince <= ClaimGraceSeconds)
        {
            SetClaim(ClaimState.IntentWaitingForCard, questConfirmToggle!, null);
            return;
        }

        DropClaim($"the confirm has been on screen and unparked for more than "
                  + $"{ClaimGraceSeconds:F1} s, so the refusal is released rather than leaving a "
                  + "button nobody can press");

        // THE ONE WARN. It only fires in the state that is actually a fault: the game HAS the quest
        // confirm on screen and this room is not showing it on the card.
        if (_cannotParkWarned)
            return;
        _cannotParkWarned = true;
        VRLog.Warn(Scope, "MAP QUEST READY-UP: the game's quest confirm ('Quest wählen' / 'Quest "
                          + "starten', UIReadyToggle in state Quests) is UP and could NOT be parked "
                          + "under the quest information. CAUSE: "
                          + (questCardFloated
                              ? "a quest card IS floated but the parker is not holding the toggle — "
                                + "the two disagree, which should not happen. Look UP the log for a "
                                + "'MAP TRAVEL CONFIRM: cannot park' or '… is no longer parented under "
                                + "the floated quest window' line. REPORT THIS LINE"
                              : "THERE IS NO FLOATED QUEST CARD to park it into. The player has closed "
                                + "the quest window (or the catch-all refused it), so there is nothing "
                                + "for the button to be part of")
                          + ". CONSEQUENCE: the claim is RELEASED, so the confirm floats as a small "
                          + "window of its own with its own close cross, and the parallel gate's "
                          + "'FLOAT REFUSAL LAPSED' warning is EXPECTED beside this line rather than a "
                          + "second fault. That is deliberate and it is the safety valve — the "
                          + "standing rule is that a window must never be invisible, and it outranks "
                          + "the rule that a bare button must not float. RECOVERY: re-open the quest "
                          + "by pressing its icon on the map; the confirm jumps back onto the card on "
                          + "the next frame. One line per episode.");
    }

    /// <summary>
    /// Re-assert the claim — EVERY TICK, because it is a level — and print ONE line on a state, host
    /// or object edge.
    ///
    /// <para>The explanation string is BUILT ON THE EDGE and cached, never per tick. This method runs
    /// once per frame for as long as a quest is on the table, and an interpolated sentence rebuilt on
    /// each of those frames would be a per-frame allocation in a room whose Update budget is already
    /// the one the perf line complains about. It is also why the state enum distinguishes the two
    /// INTENT cases: they carry different explanations, and the reader prints
    /// <c>ReadyToggleParkClaim.Why</c> verbatim, so a stale one would be a lie in somebody else's log
    /// line.</para>
    /// </summary>
    private static void SetClaim(ClaimState state, GameObject toggle, UIWindow? host)
    {
        if (_claimState != state || !ReferenceEquals(_claimed, toggle)
            || !ReferenceEquals(_claimHost, host))
        {
            _claimState = state;
            _claimed = toggle;
            _claimHost = host;
            _claimWhy = ExplainClaim(state, host);
            VRLog.Info(Scope, $"MAP QUEST READY-UP: park claim SET ({state}) for '{toggle.name}'"
                              + (host != null
                                  ? $" in '{host.name}' at anchoredPosition {AnchoredPosition(toggle)} "
                                    + "(window-local uGUI units; MapTravelConfirm's own placement line "
                                    + "carries the same number in real millimetres together with the "
                                    + "measured zero it came from)"
                                  : " — no host window yet; this is the INTENT claim, made before the "
                                    + "game shows the toggle so the float gate has an answer the "
                                    + "first time it asks")
                              + ". WHAT IT BUYS: while this claim stands the catch-all REFUSES to "
                              + "float 'Multiplayer Ready Toggle' as a window of its own, so the "
                              + "free-floating button in its own frame "
                              + "(.planning/debug/frei_schwebender_button_multiplayer.jpg) cannot "
                              + "appear. THE CLAIM IS A LEVEL with a one-second lifetime: it is "
                              + "re-asserted on every tick of this parker and lapses by itself if the "
                              + "parker stops running, which is what makes it impossible for this "
                              + "refusal to outlive the thing that justifies it.");
        }
        ReadyToggleParkClaim.Set(toggle, _claimWhy);
    }

    /// <summary>
    /// The sentence the READER prints verbatim when it refuses a float — so it has to read as an
    /// explanation of where the button is instead, not as a status code. Built once per claim edge.
    /// </summary>
    private static string ExplainClaim(ClaimState state, UIWindow? host) => state switch
    {
        ClaimState.Parked =>
            $"MapTravelConfirm has parked it INSIDE the floated quest window "
            + $"'{(host != null ? host.name : "<gone>")}', directly under the quest information at "
            + "the ModBuild 197 measured zero, on the two [WorldUI] TravelButtonOffset dials — the "
            + "same place, the same anchor and the same dials the HOST's confirm sits at",
        ClaimState.IntentWaitingForCard =>
            "MapTravelConfirm is about to park it under the quest information — the quest card is not "
            + "a live floated panel yet, and this claim covers the few frames it takes to become one",
        ClaimState.IntentStandingBy =>
            "MapTravelConfirm is standing by to park it under the quest information the moment the "
            + "game shows it; the claim is made on INTENT so this gate has an answer the very first "
            + "time it asks, rather than one frame after the float it would have to withdraw",
        _ => "nothing is claimed",
    };

    /// <summary>Release the claim, once, with a stated reason.</summary>
    private static void DropClaim(string why)
    {
        if (_claimState == ClaimState.None)
            return;
        VRLog.Info(Scope, $"MAP QUEST READY-UP: park claim DROPPED for "
                          + $"'{(_claimed != null ? _claimed.name : "<gone>")}' — {why}. The catch-all "
                          + "may float it as a window of its own again from here, which is "
                          + "deliberate: a confirm nobody can reach is a worse fault than a confirm "
                          + "in an ugly frame.");
        _claimState = ClaimState.None;
        _claimed = null;
        _claimHost = null;
        _claimWhy = "nothing is claimed";
        ReadyToggleParkClaim.Set(null, "MapTravelConfirm is not presenting the quest confirm");
    }

    /// <summary>Forget everything — the map room stood down. Called from
    /// <see cref="MapTravelConfirm.Reset"/>, which <c>MapRoomDriver.StandDown</c> already runs.</summary>
    internal static void Reset()
    {
        _pendingConfirm = null;
        _pendingQuest = null;
        _pendingSeam = "<none>";
        _driving = false;
        _claimState = ClaimState.None;
        _claimed = null;
        _claimHost = null;
        _claimWhy = "nothing is claimed";
        _visibleUnparkedSince = -1f;
        _cannotParkWarned = false;
        ReadyToggleParkClaim.Reset();
    }

    /// <summary>The parked object's own <c>anchoredPosition</c>, for the claim line. Read rather than
    /// remembered, so the number on the line is what the rect is actually carrying.</summary>
    private static string AnchoredPosition(GameObject go) =>
        go.transform is RectTransform rect ? rect.anchoredPosition.ToString() : "<not a RectTransform>";

    // ---- the patches ------------------------------------------------------------------------------

    /// <summary>
    /// The two postfixes, one per concrete presenter. The base
    /// <c>UIGuildmasterConfirmActionPresenter.ShowQuestSelectedAction</c> is abstract and cannot be
    /// patched, and which subclass is in the scene is a SCENE fact (the console layout serialises the
    /// popup, the desktop layout the marker button), so both are hooked and the drive line names
    /// whichever one actually fired.
    ///
    /// <para>Neither runs the game differently: a postfix with no return value and no <c>__result</c>
    /// cannot change what the method did. The prompt is left exactly as vanilla leaves it after a
    /// click — <c>UIGuildmasterConfirmActionButton</c> does not hide itself on click either
    /// (decompiled UIGuildmasterConfirmActionButton.cs:92-95), and it is a child of the guildmaster
    /// HUD, which the room does not draw.</para>
    /// </summary>
    [HarmonyPatch]
    internal static class ClientQuestPromptSeam
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIGuildmasterConfirmActionButtonPresenter),
                      nameof(UIGuildmasterConfirmActionButtonPresenter.ShowQuestSelectedAction))]
        private static void AfterButtonPrompt(CQuestState quest, Action onConfirmCallback) =>
            CapturePrompt("UIGuildmasterConfirmActionButtonPresenter", quest, onConfirmCallback);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIGuildmasterConfirmActionPopupPresenter),
                      nameof(UIGuildmasterConfirmActionPopupPresenter.ShowQuestSelectedAction))]
        private static void AfterPopupPrompt(CQuestState quest, Action onConfirmCallback) =>
            CapturePrompt("UIGuildmasterConfirmActionPopupPresenter", quest, onConfirmCallback);
    }
}
