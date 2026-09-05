using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GloomhavenVR.Core;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

// ---------------------------------------------------------------------------
// WHO HAS ALREADY CONFIRMED — THE ICON ROW ABOVE THE QUEST CONFIRM, AND THE ONE
// SENTENCE THAT SAYS WHY THERE IS NO CONFIRM AT ALL.
//
// USER REQUEST (2026-09-04, round 3 of the same complaint), verbatim:
//
//   "3) Beide Probleme sind immer noch nach wie vor da im Multiplayer - Der Button ist immer noch
//    nicht da und kein Spieler sieht diesen Button. Ich will folgendes: Alle spieler sollen dort
//    den Button sehen und ich will das JEDER Mitspieler den klicken muss. Ich will das man die
//    icons der Spieler sieht die es bereits bestätigt haben über dem Button - so wie im flat game,
//    da gibt es so eine UI in der man sieht wer noch bestätigen muss. Wenn alle bestätigt haben,
//    gehts los. Aktuell sieht niemand einen button. WICHTIG: Alle Elemente (wer schon geklickt hat
//    und der Button) sollen direkt IN dem Fenster bzw teil des Fensters sein (wie der button auch
//    bereits im Singleplayer), keine eigenständigen neuen Fenster"
//
// ═══ 1. "SO WIE IM FLAT GAME" NAMES AN OBJECT, AND THE GAME ALREADY DRAWS IT ══
//
// The UI he is describing is `UIReadyTrackerBar` (decompiled UIReadyTrackerBar.cs), a singleton row
// of `UIReadyTracker` cells. Each cell is one Image showing the GAME's own character marker
// (`UIInfoTools.GetCharacterMarker`, :131) at alpha 0.5 with the greyed-out material while that
// character's controller has NOT readied, and at full alpha with `readyAnimation` playing once he
// has (`UIReadyTracker.ShowReady`, UIReadyTracker.cs:24-34). "Wer noch bestätigen muss" is
// literally the greyed cells.
//
// IT IS ALREADY LIVE ON THIS MACHINE. `UIMapMultiplayerController.ToggleReadyUpUI(show: true)` calls
// `ShowCharactersTrackers()` (:404) which activates the bar for every selected character, and the
// ModBuild 422 host log shows exactly that happening: Player.log:20008-20009,
// `Added BruteID ready tracker` / `Added MindthiefID ready tracker`. The bar is drawn on the flat
// HUD, which the 3D map room does not draw — so nothing had to be BUILT here, only moved, which is
// the same relationship <see cref="MapTravelConfirm"/> has to the confirm itself.
//
// SO NOTHING IN THIS FILE INVENTS A READY STATE. The cells are the game's own graphics and their
// ready/not-ready state is refreshed by the game from `UIReadyToggle.PlayersReady`
// (UIReadyTrackerBar.RefreshReady, :146-171) — which is replicated by the game's own
// `GameActionType.ReadyUpPlayer` (UIReadyToggle.cs:657-665). BOTH MACHINES THEREFORE DERIVE THE
// SAME PICTURE FROM STATE THE GAME ALREADY SYNCS, AND NO WIRE FIELD IS ADDED OR NEEDED. A mod-owned
// "who has confirmed" record would have been a second copy of a replicated fact, i.e. a desync
// waiting to be reported; it was considered and rejected for exactly that reason.
//
// ═══ 2. WHERE IT GOES, AND WHY THE CONFIRM MOVES DOWN RATHER THAN THE ROW UP ══
//
// "über dem Button", and both "direkt IN dem Fenster". The confirm's own top edge is the ModBuild
// 197 MEASURED ZERO — the bottom of the quest information's painted content — so the space directly
// above the confirm is the quest text itself. Hanging the row there would print icons over the
// text.
//
// The row therefore takes the zero and the confirm moves down by the row's own height:
// <see cref="ReservedHeight"/> is added to the confirm's downward offset inside
// <c>MapTravelConfirm.ApplyPose</c>. Three properties of that arrangement are the reason it was
// chosen over the alternatives:
//
//   * THE TUNED DIALS STILL MEAN WHAT THEY MEANT. [WorldUI] TravelButtonOffsetX/YWindowHeights are
//     measured FROM the zero, and the zero still lands on the TOP of the confirm GROUP, directly
//     under the quest information. His hand-tuned values are not reinterpreted, and the standing
//     lesson "never move a contract anchor his hand-tuned config is measured from" is respected.
//   * SINGLE PLAYER IS BIT-IDENTICAL. <see cref="ReservedHeight"/> returns exactly 0 whenever this
//     class is not holding the row, which is every offline tick and every online tick on which the
//     game is not showing its multiplayer confirm. Nothing about the offline placement changes.
//   * IT CONVERGES, IT DOES NOT SOLVE. The row follows the confirm's top edge and the confirm's
//     offset follows the row's height; both writes are level-triggered and skipped when the rect
//     already carries the answer, so the pair settles within two ticks of a size change and costs
//     one vector compare each afterwards.
//
// REJECTED: putting the row BELOW the confirm (there is room — the ModBuild 422 host log's hit rect
// line has ~220 px of empty frame under the drawn content). It reads as a separate thing hanging
// off the bottom, and he asked for it above the button in the same sentence in which he named the
// flat game's arrangement.
//
// REJECTED: a mod-drawn row of avatars. The game's markers are the ones the flat game shows and the
// ones his group already recognises, and drawing our own would have needed a portrait source, a
// ready-state source and a layout — three places to disagree with the game.
//
// ═══ 3. WHY NOBODY SAW A BUTTON AT ALL — AND WHY THAT IS NOT A PLACEMENT FAULT ══
//
// This is the answer to "Aktuell sieht niemand einen button", read out of the ModBuild 422 logs and
// out of decompiled source, and it is the reason this file also owns a NOTICE.
//
// `MAP TRAVEL CONFIRM ONLINE STAND-BY` fires 14 times in the host log and states its own terms:
// `_requestVisible=True, _interactable=False, _allPlayersReady=False`. The first false term is
// `_interactable`, so the cause is `MapChoreographer.DetermineHostToggleInteractability`
// (decompiled MapChoreographer.cs:3318-3339) computing its `flag` false. That `flag` is a
// conjunction of three terms and the logs settle each one:
//
//   1. `AllPlayers.Count > 1` — TRUE. Host Player.log:20006 `Number of players: 2`.
//   2. `JoiningPlayers.Count == 0 && ConnectingUsers.Count == 0` — TRUE. The join finished at
//      :8414 (`NotifyClientsPlayersAreFinishedJoining`, sent by HostCallbacks only when the list
//      reaches zero) and :8963 (`PlayerReadyForAssignment set to: True`); every
//      `Determining host toggle lock` from :20010 onwards is more than eleven thousand lines later.
//   3. `AllPlayers.All(x => x.IsParticipant)` — **FALSE, and this is the cause.** Both
//      NetworkControllables in the session are assigned to the HOST: Player.log:6980-7000,
//      `McFredward (PlayerID: 1) gained control over BruteID` and `… over MindthiefID`. The joined
//      player ARMA (PlayerID 2) never appears in a control assignment on either machine, and his
//      own client says so in the game's words at remote/Player.log:6166,
//      `Determining client toggle interactability false ` — the branch
//      `DetermineClientToggleInteractability` takes when `MyPlayer.IsParticipant` is false
//      (MapChoreographer.cs:3357-3369).
//
// A PLAYER WITH NO CHARACTER IS NOT A PARTICIPANT (`NetworkPlayer.IsParticipant` is
// `MyControllables.FirstOrDefault(x => x.IsParticipant) != null`, decompiled
// FFSNet/NetworkPlayer.cs:35), and the flat game withholds the quest ready-up in exactly that state
// ON PURPOSE. It is not silent about it either: `UIMapMultiplayerController.RefreshWaitingNotifications`
// raises a PERMANENT notification naming the players concerned —
// `ShowWaitingPlayersCharacterAssigned(AllPlayers.Where(x => !x.IsParticipant))`,
// UIMapMultiplayerController.cs:604-608, text key
// `GUI_MULTIPLAYER_WARNING_TEXT_WaitingForCharAssignment`, German "Warten, bis alle Spieler Söldner
// zugewiesen haben".
//
// THAT NOTIFICATION IS THE ONE THING THE ROOM DOES NOT DRAW. It is not a `UIWindow` at all — it is
// a `UINotificationManager` entry, and the catch-all refuses that whole family by component
// (`IsKnownHudWindow`, WorldUI/Modal/ModalFallback.10.CatchAll.cs:548/570) because those banners
// cycle show→hide and a float would flap. The refusal is right and is not touched here. The
// consequence, though, is that in VR the host got NO button and NO reason, three rounds running,
// while a flat player in the same session would have been told what to do. The assignment UI itself
// is fully reachable in the room and was open in this very session (host Player.log:6766,
// `UI Multiplayer Submenu_gamepad` floated "poke + laser clickable"; :6776 a laser click on
// `Multiplayer`) — the host simply closed it at :8849, before ARMA had finished joining at :8415,
// and never clicked a hero slot.
//
// SO THE CARD IS NEVER SILENT ANY MORE. Either the game's confirm is in it (with the icon row above
// it), or the card carries ONE line of the GAME's OWN localised text naming the blocker and the
// players it names. It is a STATUS LINE, never a control: the standing ruling that a control which
// cannot act must not be drawn is why the offline travel container is still refused online (see
// `MapTravelConfirm.ResolveContainer`), and a sentence is not a control.
//
// NOTHING HERE WRITES GAME STATE. Every value below is read; the ready-up is driven only by the
// game's own toggle when a player presses it (`UIReadyToggle.InputToggle` → `ReadyUp` →
// `ReadyUpPlayer` → `GameActionType.ReadyUpPlayer`, UIReadyToggle.cs:615-676), exactly as
// <see cref="MapQuestReadyUp"/> documents. This class moves two of the game's own objects and draws
// one sentence.
// ═══ 4. THE SECOND CONFIRMATION — 'Verlies betreten' (ModBuild 436) ═════════════════════════════
//
// USER REQUEST (2026-09-05, multiplayer session), verbatim:
//
//   "Beim 'Verlies betreten' Button will ich auch die Symbole sehen vom Spiel, wer schon akzeptiert
//    hat und wer nicht - wie zu Beginn auch bei der Questauswahl."
//
// THE GAME ALREADY TRACKS AND ALREADY DRAWS IT, on the SAME widget, and this was checked in the
// game's own source before a single line was written — because the one thing this mod may never do
// is invent a rules fact:
//
//   * ONLINE the 'Verlies betreten' control IS the game's `UIReadyToggle` singleton. It is
//     initialised by `UILoadoutManager.MPConfirmEnterScenario` with the label
//     `GUI_LOADOUT_ENTER_SCENARIO` (decompiled UILoadoutManager.cs:507), which is that button; the
//     offline `confirmationButton` is a different object and `SetActiveConfirmationButton` switches
//     between them on `FFSNetwork.IsOnline` (:98-109). `LoadoutConfirmPark.ResolveControl` takes the
//     same branch and has since ModBuild 238.
//   * ITS PER-PLAYER READY STATE IS THE TRACKER BAR'S. The very next statement after that
//     `Initialize` is `Singleton<UIMapMultiplayerController>.Instance.ShowLoadoutMultiplayer()`
//     (UILoadoutManager.cs:516), and `ShowLoadoutMultiplayer` is
//     `RefreshWaitingNotifications(); ShowCharactersTrackers();`
//     (UIMapMultiplayerController.cs:95-99) — the SAME `trackerBar.ShowCharactersTrackers(...)` call
//     (:386-389) the quest selection makes, on the SAME serialized bar, in character mode.
//   * AND THE CELLS ARE KEPT CURRENT IN THIS PHASE. The toggle's own per-player callback runs
//     `UIMapMultiplayerController.UpdateReadyPlayer(player, ready)` (UILoadoutManager.cs:499).
//     Outside the quest-selection phase — and the loadout IS outside it, `InQuestSelectionPhase()`
//     is `InHQ || AtLinkedScenario` (UIMapMultiplayerController.cs:250-256) — that method takes its
//     `else` branch straight to `UpdateReadyTracker` -> `trackerBar.RefreshReady(player, isReady)`
//     (:296-331). So a cell lights for the same reason and by the same code as on the quest card.
//
// SO NOTHING NEW IS BUILT AND NOTHING NEW IS TRACKED. The bar is populated and live during the
// loadout; it is simply drawn on the flat HUD, which the 3D room does not draw — the identical
// relationship this file already had to the quest card. THE FIX IS THEREFORE A SECOND HOST, NOT A
// SECOND IMPLEMENTATION: <see cref="ReadyRosterSite"/> names which confirmation is being served and
// every other line below is shared.
//
// WHY THE TWO SITES CANNOT COLLIDE OVER THE ONE SINGLETON BAR. They are mutually exclusive BY THE
// GAME'S OWN FIELD, and it is the same field `LoadoutConfirmPark` already relies on:
// `MapQuestReadyUp` claims the ready toggle only while `readyUpToggleState` reads `Quests`, and
// `LoadoutConfirmPark.ResolveControl` refuses it unless it reads anything else (`MPConfirmEnterScenario`
// calls `Initialize` without that argument, leaving the default `NotSet`, UIReadyToggle.cs:452,467).
// Both parkers still TICK every frame, though, so the offers are collected and resolved rather than
// acted on in call order — see <see cref="Tick"/>. A frame in which BOTH offer a confirm is a
// contradiction of the game's own field and is REPORTED, never silently resolved by preference.
//
// OFFLINE THERE IS NOTHING TO SHOW AND NOTHING IS SHOWN. `MultiplayerStartup` never reaches
// `MPConfirmEnterScenario` (UILoadoutManager.cs:465-471), so the bar is never populated for the
// loadout at all; the single-player 'Verlies betreten' button keeps exactly the seat
// `LoadoutConfirmPark` has given it since ModBuild 241, to the pixel. A row of icons that says
// nothing is not drawn — but the CONTROL is never withheld with it, which is the half of "es darf
// niemals leere Fenster geben" that matters: the roster is a decoration and the button is not.
//
// AND IT NEEDS NO WIRE FIELD, for the reason section 1 already gives and which is unchanged by the
// second site: every cell's state is derived on each machine from `UIReadyToggle.PlayersReady`, and
// that list is kept in step by the GAME'S OWN `GameActionType.ReadyUpPlayer` (UIReadyToggle.cs:657-665,
// dispatched through FFSNet/Synchronizer.cs:137). Both players open their own copy of this window and
// both read the same replicated fact. A mod-owned "who has accepted" record would be a SECOND copy of
// a replicated fact — a desync waiting to be reported — and the standing rule that this mod must never
// invent a rules fact forbids it outright. This class moves the game's widget and writes a position;
// it sends nothing and stores no readiness.
// ---------------------------------------------------------------------------

/// <summary>
/// WHICH CONFIRMATION the ready row is being asked for. The two are separate WINDOWS with separate
/// parkers and separate geometry, and they share one singleton bar — so the site has to be said out
/// loud rather than inferred from whichever parker happened to call.
/// </summary>
internal enum ReadyRosterSite
{
    /// <summary>The quest card's 'Quest wählen' / 'Quest annehmen' confirm, parked by
    /// <see cref="MapTravelConfirm"/>. The row takes the measured zero and the confirm moves DOWN by
    /// <see cref="MapQuestReadyRoster.ReservedHeight"/>.</summary>
    Quest,

    /// <summary>The Character-UI's 'Verlies betreten' confirm, parked by
    /// <c>WorldUI.Composites.LoadoutConfirmPark</c>. The confirm does NOT move: its seat is solved
    /// against the character rows and the window's painted content, and the row hangs in the empty
    /// column above it — so this site reserves nothing.</summary>
    EnterDungeon,
}

/// <summary>
/// The multiplayer ready ROW inside the quest card — the game's own <c>UIReadyTrackerBar</c> parked
/// directly above the parked confirm — plus the status line that names the blocker when the game is
/// withholding its confirm altogether. Ticked by <see cref="MapTravelConfirm.Reconcile"/>. See the
/// block comment above for the source citations and the hardware log lines behind every claim.
///
/// <para>Since ModBuild 436 it serves the 'Verlies betreten' confirmation as well — see section 4 of
/// the block comment. The row's placement is measured off PAINTED INK on both sites, which is the
/// other half of that build; the class doc's original claim that the confirm moves down by "the
/// row's own height" now means the row's INK height and not its rect's.</para>
/// </summary>
internal static class MapQuestReadyRoster
{
    private const string Scope = "MapRoom";

    /// <summary>FALLBACK gap between the icon row's painted bottom edge and the confirm's painted top
    /// edge, as a fraction of the host window's own height — ~15 px on the 1021 px card the ModBuild
    /// 422 log measures. A fraction rather than a pixel count for the same reason the two offset dials
    /// are fractions: the card is not always the same height.
    ///
    /// <para>SINCE ModBuild 436 IT IS ONLY THE FALLBACK. The gap is normally the host window's OWN
    /// layout pitch — see <see cref="ResolveGap"/> — because a number this file picked can only ever
    /// agree with the game's rhythm by accident, and the user's complaint was precisely that the two
    /// elements did not read as one block. This value stands in when the host carries no layout group
    /// with a usable spacing, and which of the two was used is on the report line.</para></summary>
    private const float RowGapWindowHeights = 0.015f;

    /// <summary>Where the NOTICE's bottom edge sits, as a fraction of the window height above the
    /// window's bottom edge. Deliberately NOT the measured zero: the zero is only measured while
    /// something is parked (<c>MapTravelConfirm.MaybeRefreshAnchor</c> runs from its pose write),
    /// and the notice exists precisely for the case in which nothing is parked. A construction that
    /// is inside the frame for every card length beats a measurement that cannot be taken.</summary>
    private const float NoticeBottomFractionAboveBottom = 0.06f;

    /// <summary>Notice width as a fraction of the window's own width. The card is 512 px wide in the
    /// log; 0.86 leaves a readable margin on both sides at every card length.</summary>
    private const float NoticeWidthFraction = 0.86f;

    /// <summary>Notice font size in the window's own uGUI pixels. The card's body text measures in
    /// this range and the notice is scaled by the same conversion the window is.</summary>
    private const float NoticeFontPx = 22f;

    /// <summary>Body ink of a game help box (<c>HelpBoxLine</c>: <c>#d0d0d0</c>) — the colour the
    /// flat game uses for exactly this register of text, so the line reads as part of the game.</summary>
    private static readonly Color NoticeInk = new(0.816f, 0.816f, 0.816f, 1f);

    // ---- A: the parked icon row --------------------------------------------------------------------

    private static GameObject? _row;
    private static UIWindow? _rowHost;
    private static Transform? _rowHome;
    private static int _rowHomeIndex;
    private static Vector2 _rowHomeAnchorMin;
    private static Vector2 _rowHomeAnchorMax;
    private static Vector2 _rowHomePivot;
    private static Vector2 _rowHomeAnchoredPos;
    private static Quaternion _rowHomeRotation = Quaternion.identity;
    private static Vector3 _rowHomeScale = Vector3.one;

    private static LayoutElement? _rowIgnore;
    private static bool _rowIgnoreAdded;
    private static bool _rowIgnoreHome;

    /// <summary>The row's own height plus the gap, in the host window's local units, as measured on
    /// the last tick it was parked. Read by <c>MapTravelConfirm.ApplyPose</c>; zero whenever the row
    /// is not held, which is what makes the offline placement bit-identical to ModBuild 422.</summary>
    private static float _reserved;

    /// <summary>ONE Warn per episode when the row cannot be parked, so a card that opens and closes
    /// does not produce a line per frame.</summary>
    private static bool _rowWarned;

    // ---- B: the notice ------------------------------------------------------------------------------

    /// <summary>How often the blocker sentence is re-evaluated, seconds. See the call site.</summary>
    private const float BlockEvalIntervalSeconds = 0.25f;

    private static float _blockNextEvalAt = float.NegativeInfinity;
    private static string _blockText = string.Empty;

    private static GameObject? _noticeGo;
    private static TextMeshProUGUI? _notice;
    private static UIWindow? _noticeHost;
    private static string _noticeText = string.Empty;

    // ---- C: which confirmation, and the two parkers' offers -------------------------------------------

    private const int SiteCount = 2;

    /// <summary>How many frames an offer stays live. Two, so a parker that skips one tick — a frame
    /// hitch, a guarded throw — does not drop the row, and one that stands down stops holding it
    /// almost at once. Same reasoning and the same direction of safety as
    /// <c>ReadyToggleParkClaim.ClaimLifetimeSeconds</c>.</summary>
    private const int OfferLifetimeFrames = 2;

    private static readonly UIWindow?[] _offerHost = new UIWindow?[SiteCount];
    private static readonly GameObject?[] _offerConfirm = new GameObject?[SiteCount];
    private static readonly bool[] _offerPosed = new bool[SiteCount];

    /// <summary>Initialised so far in the past that no <c>Time.frameCount</c> can be within
    /// <see cref="OfferLifetimeFrames"/> of it, and far enough from <c>int.MinValue</c> that the
    /// subtraction cannot overflow [[sentinel-overflow-and-silent-scans]].</summary>
    private static readonly int[] _offerFrame = { int.MinValue / 2, int.MinValue / 2 };

    /// <summary>Which confirmation the row is being held for right now. Only meaningful while
    /// <c>_row != null</c>; <see cref="ReservedHeight"/> is gated on it so the loadout site can never
    /// move the quest card's confirm.</summary>
    private static ReadyRosterSite _site = ReadyRosterSite.Quest;

    /// <summary>ONE Warn per episode when both sites offer a confirm at once.</summary>
    private static bool _collisionWarned;

    // ---- E: the adopted row's LAYER -------------------------------------------------------------------
    //
    // A CONVERTED WINDOW IS DRAWN BY ITS OWN CAPTURE CAMERA, AND THAT CAMERA CULLS BY LAYER. A subtree
    // re-parented into a floated window keeps the layer the flat HUD had it on, so it can be present,
    // active, correctly placed and reported as all three while being rendered by NO camera —
    // [[measure-the-picture-not-the-state]], and the exact defect `LoadoutConfirmPark` shipped latent
    // for a build before it started writing layers for the control it parks.
    //
    // The row is the same kind of object with the same problem, so it gets the same remedy, written
    // the same way: the walk skips a FOREIGN RENDER SUBTREE whole (a real `Renderer` under a uGUI tree
    // is 3D owned by another camera, and descending would take its children with it —
    // `CanvasConversion.ApplyModLayer`'s own rule; a `CanvasRenderer` is NOT a `Renderer`, so ordinary
    // uGUI is unaffected [[canvasrenderer-is-not-a-renderer]]), and every value written is handed back
    // to the one the GAME had, but ONLY where the transform is still on the layer we wrote — a
    // transform somebody else has since re-layered is no longer ours to hand back.
    //
    // ON THE QUEST CARD THIS IS EXPECTED TO BE A NO-OP: ModBuild 423's row is visible in
    // Abstand_Quest.jpg, so whatever re-layers that card's children already reaches it. Writing a
    // layer that already holds costs one int compare and changes nothing, and the count of transforms
    // actually written is on the report line — so if it IS a no-op there, the log says zero and says
    // it positively instead of leaving it assumed.
    private static readonly List<Transform> _layerTx = new(16);
    private static readonly List<int> _layerWas = new(16);
    private static int _layerWritten = -1;

    // ---- D: the measured ink, and the gap taken from the window's own layout ---------------------------
    //
    // ═══ WHY INK AND NOT THE RECT — THE WHOLE OF Abstand_Quest.jpg (ModBuild 436) ═══
    //
    // USER REPORT (2026-09-05), verbatim: "Der vertikale Abstand zwischen der Anzeige wer schon
    // akzeptiert hat im Questfenster ist zu groß, siehe Abstand_Quest.jpg."
    //
    // ModBuild 423 placed the row by its RectTransform: the row's rect BOTTOM was put a gap above the
    // confirm's PIVOT, and `_reserved` was `rect.rect.height + gap`. Both terms are the rect, and the
    // tracker bar's rect is not its picture — it is a layout frame several times taller than the two
    // 63 px shields it draws, with the cells sitting near its lower edge. The arithmetic then puts
    // the row's rect TOP exactly on the measured zero (dials default to 0 and are pinned), which is
    // why the shields ended up marooned in the middle of the card with the bar's own empty upper
    // region drawn as a void between the treasure panel and them. The screenshot IS that void
    // [[a-tight-box-is-not-the-rect]].
    //
    // So both terms are now the PAINTED UNION, measured with `MapTravelConfirm.TryInkBounds` — the
    // same sweep the confirm's own zero is solved from, reused rather than reimplemented so the two
    // placements can never disagree about what "drawn" means.
    //
    // ═══ WHY THE OFFSETS ARE STORED RELATIVE TO EACH RECT'S PIVOT ═══
    //
    // Translating a rect moves its pivot and every graphic under it by the SAME vector, so
    // `ink.yMin - pivotLocal.y` does not depend on where the rect currently is. That makes a value
    // measured up to InkRefreshIntervalSeconds ago EXACT after this frame's pose has already moved
    // the confirm — which is the only way the row can read a pose written earlier in the same frame
    // instead of chasing the last one. It is the identical fixed-point argument
    // `MapTravelConfirm.MaybeRefreshAnchor` rests on, and the pivot is read through
    // `RectTransform.position` so it is right under whatever anchors a second writer has left on the
    // rect [[anchoredposition-is-not-a-frame]]. What can stale it is the CONTENT changing (a player
    // joins and the bar gains a cell), which is exactly what the cadence is there to pick up.

    /// <summary>How often both inks are re-measured, seconds. Deliberately the same number
    /// <c>MapTravelConfirm.AnchorRefreshIntervalSeconds</c> uses, and for the same reason: the sweeps
    /// are the expensive part and the thing they measure changes when a player joins or readies, not
    /// per frame [[one-line-owned-the-frame]].</summary>
    private const float InkRefreshIntervalSeconds = 0.2f;

    /// <summary>Below this, a correction is not worth a uGUI layout dirty. Window-local units.</summary>
    private const float PlaceEpsilonPx = 0.5f;

    private static float _inkNextAt = float.NegativeInfinity;
    private static bool _inkValid;
    private static string _inkWhy = "never measured yet";

    private static float _rowInkTopFromPivot;
    private static float _rowInkBottomFromPivot;
    private static float _rowInkCentreXFromPivot;
    private static int _rowInkGraphics;

    private static float _confirmInkTopFromPivot;
    private static float _confirmInkCentreXFromPivot;
    private static int _confirmInkGraphics;

    /// <summary>The gap between the row's painted bottom and the confirm's painted top, in the host
    /// window's own authored px, and where that number came from. See <see cref="ResolveGap"/>.</summary>
    private static float _gapPx;
    private static string _gapWhy = "never resolved";

    // WHAT THE LAST PLACEMENT ACTUALLY DID, kept for the report so the line describes the numbers the
    // write was computed from rather than a fresh reading taken later in the tick. Every one of them
    // is in the HOST WINDOW'S OWN LOCAL SPACE except _lastRowAnchoredY, which is printed BESIDE its
    // parent-local centre precisely so a rect carrying a foreign pivot names itself in the log
    // instead of being measured off a photograph.
    private static Vector2 _lastDelta;
    private static float _lastRowInkCentreY;
    private static float _lastRowLocalCentreY;
    private static float _lastRowAnchoredY;
    private static float _lastConfirmInkTop;
    private static float _rowRectHeight;
    private static float _rowInkHeight;

    // ---- the HW-VERIFY report ------------------------------------------------------------------------

    /// <summary>UNCONDITIONAL LIVENESS. Incremented at the top of <see cref="Tick"/> before any early
    /// return can be taken, and printed on every report line. A line reading ticks=3 and a line
    /// reading ticks=41200 describe completely different builds, and neither of them can be told from
    /// the other by any of the fields beside it — a change-gated instrument with a constant verdict
    /// prints once and then looks stopped [[held-instrument-reads-as-dead]].</summary>
    private static long _ticks;

    // WHAT THE LAST REPORT LINE SAID, so the report is printed on an EDGE and not per frame. Kept as
    // SEPARATE PRIMITIVES rather than folded into one string: the fold was rebuilt every tick, which
    // is a per-frame allocation on the map room's Update path for a line that prints once per
    // selection. Comparing the terms costs no allocation at all.
    private static int _reportHostId;
    private static int _reportConfirmId;
    private static int _reportRowId;
    private static int _reportCells;
    private static int _reportLit;
    private static ReadyRosterSite _reportSite;
    private static bool _reportInkValid;
    private static string _reportNotice = string.Empty;
    private static bool _reportEver;

    /// <summary>Forget the last line so the next one prints whatever it says.</summary>
    private static void ClearReport()
    {
        _reportEver = false;
        _reportHostId = 0;
        _reportConfirmId = 0;
        _reportRowId = 0;
        _reportCells = -1;
        _reportLit = -1;
        _reportInkValid = false;
        _reportNotice = string.Empty;
    }

    // ---- reflection into FFSNet ----------------------------------------------------------------------
    //
    // ALL OF IT, AND ON PURPOSE. `FFSNet.NetworkPlayer` is `EntityBehaviour<IPlayerState>`, i.e. a
    // Photon Bolt type, and this mod does not reference Bolt (see GloomhavenVR.csproj — GH.Runtime,
    // GH.Shared, the two rule libraries and Unity, no Bolt). Naming `NetworkPlayer` in a signature
    // here would drag that reference in for a diagnostic. `Net/NetPlayerActors.cs` reaches the same
    // registry the same way and for the same reason; this is the house pattern, not an exception.
    //
    // EVERY MEMBER IS OPTIONAL. A missing one degrades the sentence to "<not resolvable>" and
    // nothing else changes — a missing field must never take a diagnostic, or a card, down with it.

    private static bool _ffsResolved;
    private static FieldInfo? _allPlayers;        // static List<NetworkPlayer> PlayerRegistry.AllPlayers
    private static PropertyInfo? _joiningPlayers; // static List<BoltConnection> PlayerRegistry.JoiningPlayers
    private static PropertyInfo? _connectingUsers;// static List<UserToken> PlayerRegistry.ConnectingUsers
    private static PropertyInfo? _isParticipant;  // bool NetworkPlayer.IsParticipant
    private static PropertyInfo? _username;       // string NetworkPlayer.Username

    // ---- the per-tick entry point ---------------------------------------------------------------------

    /// <summary>
    /// Offer this site's live state, once per frame, from that site's own parker —
    /// <see cref="MapTravelConfirm.Reconcile"/> for <see cref="ReadyRosterSite.Quest"/> and
    /// <c>LoadoutConfirmPark.Tick</c> for <see cref="ReadyRosterSite.EnterDungeon"/>. Both run from
    /// the map room's own Update tick, in the same place each site's parking runs.
    ///
    /// <para>WHY AN OFFER AND NOT A COMMAND. There is exactly ONE tracker bar (a
    /// <c>Singleton&lt;UIReadyTrackerBar&gt;</c>) and TWO parkers that both tick every frame. If each
    /// call acted immediately, the site with nothing to show would release the row the other site had
    /// just taken, once per frame, for ever — a write war with itself. So each call records what its
    /// site can offer and then RESOLVES over both offers, which makes the outcome independent of
    /// which parker happens to run first [[dont-win-a-write-war]].</para>
    ///
    /// <para>An offer is live for <see cref="OfferLifetimeFrames"/> frames, so a parker that skips a
    /// tick does not drop the row, and one that stands down entirely stops holding it within two
    /// frames.</para>
    /// </summary>
    /// <param name="site">Which confirmation this call is about. See <see cref="ReadyRosterSite"/>.</param>
    /// <param name="host">The floated window that site parks into, or null when none is floated.</param>
    /// <param name="parkedConfirm">The object that site's parker is holding inside that window THIS
    /// TICK while it is the game's multiplayer ready toggle, or null. The icon row exists only beside
    /// that object: offline there is no multiplayer readiness to show, and an icon row with no button
    /// under it would be the "leeres Fenster" the standing ruling forbids.</param>
    /// <param name="confirmPosed">Has the confirm's own pose been written yet? The row hangs off the
    /// confirm's painted top edge, so a pose that has not settled is one the row must not follow — it
    /// would place the icons at the confirm's PREVIOUS offset for one tick.</param>
    internal static void Tick(ReadyRosterSite site, UIWindow? host, GameObject? parkedConfirm,
                              bool confirmPosed)
    {
        _ticks++;   // UNCONDITIONAL LIVENESS: incremented before any early return can be taken.

        int slot = (int)site;
        if (slot < 0 || slot >= SiteCount)
            return;
        _offerHost[slot] = host;
        _offerConfirm[slot] = parkedConfirm;
        _offerPosed[slot] = confirmPosed;
        _offerFrame[slot] = Time.frameCount;
        Resolve();
    }

    /// <summary>
    /// Decide, over BOTH sites' live offers, who gets the one bar this frame — then hold or release
    /// it, and run the notice and the report.
    /// </summary>
    private static void Resolve()
    {
        bool roomStands = MapRoomDriver.Active;
        int now = Time.frameCount;

        int winner = -1;
        int offers = 0;
        for (int i = 0; i < SiteCount; i++)
        {
            bool live = roomStands && now - _offerFrame[i] <= OfferLifetimeFrames;
            if (!live || _offerHost[i] == null || _offerConfirm[i] == null)
                continue;
            offers++;
            if (winner < 0)
                winner = i;
        }

        // BOTH AT ONCE IS A CONTRADICTION OF THE GAME'S OWN FIELD, not a preference to express.
        // `readyUpToggleState` reads `Quests` for one parker and anything else for the other, so two
        // live confirms mean one of them is holding an object it should already have handed back.
        // Reported once per episode; the first offer still wins, because a row on the wrong one of two
        // confirms beats no row and beats a flap.
        if (offers > 1 && !_collisionWarned)
        {
            _collisionWarned = true;
            VRLog.Warn(Scope, "MAP QUEST READY ROW: BOTH confirmations are offering a parked ready "
                              + "toggle in the same frame — the quest card and the Character-UI's "
                              + "'Verlies betreten'. They are supposed to be mutually exclusive by the "
                              + "game's own UIReadyToggle.readyUpToggleState (Quests for the quest "
                              + "card, anything else for the loadout), so one parker is holding an "
                              + "object it should have handed back. The FIRST offer keeps the row; "
                              + "the button on the other confirmation is unaffected and still "
                              + "pressable, which is the right way round for a decoration and a "
                              + "control. One line per episode.");
        }
        else if (offers <= 1)
        {
            _collisionWarned = false;
        }

        if (winner < 0)
        {
            bool anyHost = roomStands && (_offerHost[0] != null || _offerHost[1] != null);
            ReleaseRow(!roomStands ? "the map room stood down"
                : !anyHost ? "neither confirmation has a floated window to sit in"
                : "the game's multiplayer confirm is not parked in either window");
        }
        else
        {
            _site = (ReadyRosterSite)winner;
            HoldRow(_offerHost[winner]!, _offerConfirm[winner]!, _offerPosed[winner]);
        }

        // THE NOTICE IS THE COMPLEMENT OF THE QUEST CONFIRM, never a companion to it: it is drawn only
        // while the quest card is floated, this client is online, and the game is showing no confirm
        // at all. The moment the confirm appears the sentence is gone, so the card can never carry
        // both a button and an explanation of why there is no button.
        //
        // IT IS QUEST-ONLY ON PURPOSE. Every term of DescribeBlock is one of
        // MapChoreographer.DetermineHostToggleInteractability's, which gates the QUEST ready-up; the
        // loadout's confirm is gated by UILoadoutManager.CheckRequirements instead, so printing this
        // sentence beside 'Verlies betreten' would be naming the wrong gate in the game's own words —
        // the most expensive kind of wrong an instrument can be [[an-instrument-can-assert-a-cause]].
        // The loadout is not left silent: LOADOUT CONFIRM REACHABLE already reports its own blocker.
        int questSlot = (int)ReadyRosterSite.Quest;
        bool questLive = roomStands && now - _offerFrame[questSlot] <= OfferLifetimeFrames;
        UIWindow? questWindow = questLive ? _offerHost[questSlot] : null;
        bool wantNotice = questWindow != null && _offerConfirm[questSlot] == null && FFSNetwork.IsOnline;
        if (wantNotice)
            ShowNotice(questWindow!);
        else
            HideNotice();

        // THE CARD IS STILL NAMED WHEN NOTHING IS PARKED. The state the report exists to describe
        // includes "a card is open and there is NO confirm in it, here is the sentence it carries
        // instead" — a line that says card='<none>' in that state would be reporting the absence of
        // the thing rather than the thing that is there.
        Report(winner >= 0 ? _offerHost[winner] : questWindow,
               winner >= 0 ? _offerConfirm[winner] : null);
    }

    /// <summary>
    /// The vertical room the icon row is taking above the QUEST confirm, in the host window's own
    /// local units — added to the confirm's downward offset by <c>MapTravelConfirm.ApplyPose</c> so
    /// the two do not overlap and the MEASURED ZERO still lands on the top of the pair.
    ///
    /// <para>Exactly 0 whenever this class is not holding the row for that site, which is every
    /// offline tick and every tick the row is on the 'Verlies betreten' confirmation instead. That is
    /// what makes single player bit-identical to ModBuild 422 rather than "probably unchanged".</para>
    ///
    /// <para>ModBuild 436 — THIS IS NOW AN INK HEIGHT AND IT USED TO BE A RECT HEIGHT. See
    /// <see cref="HoldRow"/>: the bar's RectTransform is far taller than the cells it paints, and
    /// reserving the rect is what put the stranded band in Abstand_Quest.jpg.</para>
    /// </summary>
    internal static float ReservedHeight() =>
        _row != null && _site == ReadyRosterSite.Quest ? _reserved : 0f;

    /// <summary>
    /// The adopted tracker bar's transform while this class is holding it, else null — so a parker
    /// whose seat is SOLVED against its window's painted content can leave the row out of that
    /// measurement.
    ///
    /// <para>WHY A PARKER HAS TO ASK. On the 'Verlies betreten' site the control's seat is solved
    /// against the right edge of everything the window paints, and the row is parked to the right of
    /// everything else and follows the control. Counting it would push the control right, which would
    /// pull the row right, which would push the control right again — a feedback loop wearing a
    /// measurement's clothes. Excluding it is what keeps the solve the fixed point its own comment
    /// claims it is. The quest site does not need this: its zero is the information's BOTTOM edge and
    /// <c>MapTravelConfirm</c> measures that with the container excluded already.</para>
    /// </summary>
    internal static Transform? HeldRow => _row != null ? _row.transform : null;

    /// <summary>Hand both elements back — the room is going away under them. Called from
    /// <see cref="MapTravelConfirm.Reset"/>.</summary>
    internal static void Reset()
    {
        ReleaseRow("map room teardown");
        HideNotice();
        _blockNextEvalAt = float.NegativeInfinity;
        _blockText = string.Empty;
        _rowWarned = false;
        _collisionWarned = false;
        ClearReport();
        for (int i = 0; i < SiteCount; i++)
        {
            _offerHost[i] = null;
            _offerConfirm[i] = null;
            _offerPosed[i] = false;
            _offerFrame[i] = int.MinValue / 2;   // never within OfferLifetimeFrames of any frameCount
        }
    }

    // ---- A: holding the game's icon row ----------------------------------------------------------------

    /// <summary>
    /// Move the game's tracker bar into the card and keep it directly above the confirm.
    ///
    /// <para>OWNERSHIP IS RE-TESTED EVERY TICK, exactly as the confirm's parking does it: the game
    /// re-parents its own UI freely and re-taking an object from wherever it has since put it is the
    /// write war this project has already lost once. If the bar is no longer our child we hand the
    /// recorded home back and stop, rather than fighting for it.</para>
    /// </summary>
    private static void HoldRow(UIWindow questWindow, GameObject confirm, bool confirmPosed)
    {
        GameObject? bar = TrackerBar();
        if (bar == null)
        {
            ReleaseRow("the game has no ready tracker bar in this scene");
            return;
        }
        if (questWindow.transform is not RectTransform win
            || bar.transform is not RectTransform rect
            || confirm.transform is not RectTransform confirmRect)
        {
            ReleaseRow("the card, the confirm or the tracker bar is not a RectTransform");
            return;
        }

        if (!ReferenceEquals(_row, bar) || !ReferenceEquals(_rowHost, questWindow))
        {
            ReleaseRow(!ReferenceEquals(_rowHost, questWindow)
                ? "a different quest card took over"
                : "the game replaced the tracker bar");
            _row = bar;
            _rowHost = questWindow;
            _rowHome = rect.parent;
            _rowHomeIndex = rect.GetSiblingIndex();
            _rowHomeAnchorMin = rect.anchorMin;
            _rowHomeAnchorMax = rect.anchorMax;
            _rowHomePivot = rect.pivot;
            _rowHomeAnchoredPos = rect.anchoredPosition;
            _rowHomeRotation = rect.localRotation;
            _rowHomeScale = rect.localScale;

            // BEFORE THE REPARENT, so the card's own layout group never sees a rebuild with this
            // rect among its children — the same ordering, and the same reason, as
            // MapTravelConfirm.Park's EnsureLayoutIgnore.
            EnsureRowLayoutIgnore(bar);

            rect.SetParent(win, worldPositionStays: false);
            rect.SetAsLastSibling();
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            _rowWarned = false;
            // A NEW SUBJECT HAS NO MEASUREMENT YET, and the previous one's is about a different rect
            // in a different window. Re-arming the deadline rather than keeping a stale reading is
            // what makes the first placement of a card wait one sweep instead of writing a wrong
            // number confidently [[one-step-too-early]].
            _inkValid = false;
            _inkNextAt = float.NegativeInfinity;
            _inkWhy = "a new bar or a new host — not measured yet";
        }

        if (rect.parent != win)
        {
            if (!_rowWarned)
            {
                _rowWarned = true;
                VRLog.Warn(Scope, $"MAP QUEST READY ROW: '{bar.name}' is no longer parented under the "
                                  + $"floated quest card '{questWindow.name}' — the game moved it. "
                                  + "Handing it back (home transform restored verbatim) and standing "
                                  + "down for this card. CONSEQUENCE: the confirm keeps its place and "
                                  + "loses the icon row above it; the button itself is unaffected, "
                                  + "which is the right way round for a decoration and a control.");
            }
            ReleaseRow("the game re-parented the tracker bar");
            return;
        }

        // THE LAYER, EVERY TICK, AND ONLY WHEN THE ROOT HAS DRIFTED. Change-gated on the HOST ROOT'S
        // OWN live layer, which is exactly the value CanvasConversion.ApplyModLayer and
        // PanelSupersample.ApplyCaptureLayer want the subtree to have — so this cannot start a write
        // war with either of them [[dont-win-a-write-war]]. See block E above.
        int hostLayer = questWindow.gameObject.layer;
        if (bar.layer != hostLayer)
            WriteRowLayers(hostLayer, bar.transform);

        // The game owns activeSelf: ShowCharactersTrackers does SetActive(true) and HideTrackers
        // SetActive(false) (UIReadyTrackerBar.cs:57/226). We only ever READ it — a row we switched on
        // ourselves would be a row claiming a readiness the game is not tracking.
        if (!bar.activeInHierarchy)
        {
            _reserved = 0f;
            return;
        }

        // THE ROW HANGS OFF THE CONFIRM'S PAINTED TOP EDGE — not off its pivot and not off its rect.
        // See the ink block above the fields for why the difference is the whole of Abstand_Quest.jpg.
        if (!confirmPosed)
            return;
        Rect frame = win.rect;
        float windowHeight = Mathf.Abs(frame.height);
        if (windowHeight <= 0f)
            return;

        MeasureInk(win, rect, confirmRect);
        if (!_inkValid)
        {
            // NOTHING IS WRITTEN FROM AN UNMEASURED INK, and nothing is reserved either — so the
            // confirm keeps the seat it already has instead of being shoved by a guess. The reason is
            // on the report line's INK field.
            _reserved = 0f;
            return;
        }

        _gapPx = ResolveGap(win, windowHeight);

        // ANCHORS ARE READ, NEVER RE-WRITTEN unless they are STRETCHED — MapTravelConfirm's rule,
        // and the correction is needed for the same reason: `anchoredPosition +=` TRANSLATES a rect
        // whose anchors are a point and RESIZES one whose anchors are a span. THE PIVOT IS NO LONGER
        // TOUCHED AT ALL (ModBuild 423 forced it to bottom-centre): the placement is now solved
        // against measured ink, so it does not care where the pivot is, and a pivot write dirties the
        // game's own layout on a rect this method visits every frame.
        if (rect.anchorMin != rect.anchorMax)
            rect.anchorMin = rect.anchorMax = Vector2.up;

        // WHERE THE INK IS NOW, AND WHERE IT SHOULD BE — both in the CARD'S OWN LOCAL SPACE, which is
        // the one frame both rects can be compared in. The correction is applied as a DELTA on
        // anchoredPosition and never as an absolute, because an absolute would be a number in the
        // row's own anchor frame written from a measurement taken in the card's
        // [[anchoredposition-is-not-a-frame]].
        Vector3 rowPivot = win.InverseTransformPoint(rect.position);
        Vector3 confirmPivot = win.InverseTransformPoint(confirmRect.position);

        float confirmInkTop = confirmPivot.y + _confirmInkTopFromPivot;
        float confirmInkCentreX = confirmPivot.x + _confirmInkCentreXFromPivot;

        float haveInkBottom = rowPivot.y + _rowInkBottomFromPivot;
        float haveInkCentreX = rowPivot.x + _rowInkCentreXFromPivot;

        float wantInkBottom = confirmInkTop + _gapPx;
        // CENTRED ON THE BUTTON IT QUALIFIES, not on the card. "Die icons … über dem Button" is about
        // the button, and on the loadout site the confirm is not on the card's centre line at all —
        // it sits right of the character rows, so centring on the frame would put the icons over the
        // roster instead of over the control they belong to.
        float wantInkCentreX = confirmInkCentreX;

        var delta = new Vector2(wantInkCentreX - haveInkCentreX, wantInkBottom - haveInkBottom);
        _lastDelta = delta;
        _lastRowInkCentreY = rowPivot.y + (_rowInkTopFromPivot + _rowInkBottomFromPivot) * 0.5f;
        _lastConfirmInkTop = confirmInkTop;
        _lastRowAnchoredY = rect.anchoredPosition.y;
        _lastRowLocalCentreY = rect.localPosition.y + rect.rect.center.y;
        if (delta.sqrMagnitude > PlaceEpsilonPx * PlaceEpsilonPx)
            rect.anchoredPosition += delta;   // SKIPPED when the rect already carries the answer

        // WHAT THE QUEST CONFIRM HAS TO MOVE DOWN BY — the row's INK height plus the gap, recomputed
        // from the live measurement so a bar that gains or loses a cell (a player joins, a character
        // dies) settles by itself. The loadout site reserves nothing; see ReservedHeight.
        _reserved = Mathf.Max(0f, _rowInkTopFromPivot - _rowInkBottomFromPivot) + _gapPx;
    }

    /// <summary>
    /// Re-measure both painted unions on the cadence, and cache each one as an offset from its own
    /// rect's PIVOT. See the ink block above the fields for why pivot-relative offsets stay exact
    /// between refreshes and why the rect itself is not the picture.
    ///
    /// <para>Allocation-free: <c>MapTravelConfirm.TryInkBounds</c> fills that class's static scratch
    /// list, and this is the only caller running at the time.</para>
    /// </summary>
    private static void MeasureInk(RectTransform win, RectTransform rowRect, RectTransform confirmRect)
    {
        float now = Time.unscaledTime;
        if (now < _inkNextAt)
            return;
        _inkNextAt = now + InkRefreshIntervalSeconds;

        if (!MapTravelConfirm.TryInkBounds(win, rowRect, out Rect rowInk, out int rowCount)
            || rowCount == 0)
        {
            _inkValid = false;
            _inkWhy = "the tracker bar paints no visible graphic this tick — the game has cells but "
                      + "none of them is drawn, so there is no picture to place";
            return;
        }
        if (!MapTravelConfirm.TryInkBounds(win, confirmRect, out Rect confirmInk, out int confirmCount)
            || confirmCount == 0)
        {
            _inkValid = false;
            _inkWhy = "the confirm paints no visible graphic this tick (the game usually has it "
                      + "switched off until it is pressable) — there is no top edge to hang from";
            return;
        }

        Vector3 rowPivot = win.InverseTransformPoint(rowRect.position);
        Vector3 confirmPivot = win.InverseTransformPoint(confirmRect.position);

        _rowInkTopFromPivot = rowInk.yMax - rowPivot.y;
        _rowInkBottomFromPivot = rowInk.yMin - rowPivot.y;
        _rowInkCentreXFromPivot = rowInk.center.x - rowPivot.x;
        _rowInkGraphics = rowCount;

        _confirmInkTopFromPivot = confirmInk.yMax - confirmPivot.y;
        _confirmInkCentreXFromPivot = confirmInk.center.x - confirmPivot.x;
        _confirmInkGraphics = confirmCount;

        _rowRectHeight = Mathf.Abs(rowRect.rect.height);
        _rowInkHeight = Mathf.Abs(rowInk.height);
        _inkValid = true;
        _inkWhy = "resolved";
    }

    /// <summary>
    /// THE GAP, TAKEN FROM THE WINDOW'S OWN LAYOUT RATHER THAN CHOSEN.
    ///
    /// <para>The host window lays its own content out with a uGUI layout group — that is not a guess,
    /// it is the component <c>MapTravelConfirm.EnsureLayoutIgnore</c> exists to opt OUT of, and
    /// <c>DescribeLayoutOwner</c> has been naming it on the park line since ModBuild 196. Its
    /// <c>spacing</c> IS "the pitch the game uses between its own rows" in this very window, in the
    /// same authored px everything here is measured in. So the icon row is set one of the window's
    /// own row gaps above the button, which is what makes the pair read as one block rather than as
    /// two elements at a distance somebody picked.</para>
    ///
    /// <para>The fallback is the ModBuild 423 fraction, kept verbatim for the case where the host has
    /// no such group — a window with no pitch of its own cannot lend one, and a fraction of the
    /// window height at least scales with the card. Which of the two was used is on the report
    /// line, so this never has to be inferred from the picture.</para>
    /// </summary>
    private static float ResolveGap(RectTransform win, float windowHeight)
    {
        var group = win.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (group != null && group.spacing > 0f)
        {
            _gapWhy = $"the host's own {group.GetType().Name}.spacing";
            return group.spacing;
        }
        var grid = win.GetComponent<GridLayoutGroup>();
        if (grid != null && grid.spacing.y > 0f)
        {
            _gapWhy = "the host's own GridLayoutGroup.spacing.y";
            return grid.spacing.y;
        }
        _gapWhy = $"FALLBACK {RowGapWindowHeights:0.###} x the window's height — the host carries no "
                  + "layout group with a usable spacing, so it has no pitch of its own to lend";
        return RowGapWindowHeights * windowHeight;
    }

    /// <summary>Hand the row back to the game, verbatim, and stop reserving room for it.</summary>
    private static void ReleaseRow(string why)
    {
        if (_row == null)
        {
            _reserved = 0f;
            return;
        }

        GameObject bar = _row;
        _row = null;
        _rowHost = null;
        _reserved = 0f;
        _inkValid = false;
        _inkNextAt = float.NegativeInfinity;
        _inkWhy = "the row is not held";
        RestoreRowLayers();
        ReleaseRowLayoutIgnore();

        if (bar.transform is RectTransform rect && _rowHome != null)
        {
            rect.SetParent(_rowHome, worldPositionStays: false);
            rect.SetSiblingIndex(_rowHomeIndex);
            rect.anchorMin = _rowHomeAnchorMin;
            rect.anchorMax = _rowHomeAnchorMax;
            rect.pivot = _rowHomePivot;
            rect.anchoredPosition = _rowHomeAnchoredPos;
            rect.localRotation = _rowHomeRotation;
            rect.localScale = _rowHomeScale;
        }
        _rowHome = null;

        VRLog.Info(Scope, $"MAP QUEST READY ROW: '{bar.name}' handed back to the game's own HUD — "
                          + $"{why}. Its parent, sibling index, anchors, pivot, anchoredPosition, "
                          + "rotation and scale are restored to the values recorded when it was "
                          + "taken, so the flat HUD's layout is exactly what it was.");
    }

    /// <summary>
    /// Take the row out of any layout group the card runs, once, remembering whether it already had
    /// a <c>LayoutElement</c> so the restore is honest. Same mechanism and same reason as
    /// <c>MapTravelConfirm.EnsureLayoutIgnore</c>: a group that lays this rect out would re-write the
    /// anchoredPosition the placement above just wrote, every rebuild, for ever.
    /// </summary>
    private static void EnsureRowLayoutIgnore(GameObject bar)
    {
        if (_rowIgnore != null)
            return;
        LayoutElement? existing = bar.GetComponent<LayoutElement>();
        if (existing != null)
        {
            _rowIgnore = existing;
            _rowIgnoreAdded = false;
            _rowIgnoreHome = existing.ignoreLayout;
        }
        else
        {
            _rowIgnore = bar.AddComponent<LayoutElement>();
            _rowIgnoreAdded = true;
            _rowIgnoreHome = false;
        }
        _rowIgnore.ignoreLayout = true;
    }

    /// <summary>Undo <see cref="EnsureRowLayoutIgnore"/> — restore the flag we found, and destroy the
    /// component only if we were the ones who added it.</summary>
    private static void ReleaseRowLayoutIgnore()
    {
        if (_rowIgnore == null)
            return;
        if (_rowIgnoreAdded)
            UnityEngine.Object.Destroy(_rowIgnore);
        else
            _rowIgnore.ignoreLayout = _rowIgnoreHome;
        _rowIgnore = null;
        _rowIgnoreAdded = false;
        _rowIgnoreHome = false;
    }

    /// <summary>Put the row's whole subtree on the host window's layer, remembering every value the
    /// GAME had so <see cref="RestoreRowLayers"/> can hand it back. See block E above.</summary>
    private static void WriteRowLayers(int layer, Transform root)
    {
        RestoreRowLayers();
        _layerWritten = layer;
        WriteRowLayerWalk(root, layer);
    }

    private static void WriteRowLayerWalk(Transform t, int layer)
    {
        if (t.GetComponent<Renderer>() != null)
            return;   // and NOT its children either — that is the whole point
        if (t.gameObject.layer != layer)
        {
            _layerTx.Add(t);
            _layerWas.Add(t.gameObject.layer);
            t.gameObject.layer = layer;
        }
        for (int i = t.childCount - 1; i >= 0; i--)
            WriteRowLayerWalk(t.GetChild(i), layer);
    }

    /// <summary>Hand every layer this class wrote back to the value the GAME had — and only where the
    /// transform is STILL on the layer we wrote, because one somebody else has since re-layered is no
    /// longer ours to hand back and a stale write would strand it on a layer no camera renders.</summary>
    private static void RestoreRowLayers()
    {
        for (int i = 0; i < _layerTx.Count; i++)
        {
            Transform? t = _layerTx[i];
            if (t != null && t.gameObject.layer == _layerWritten)
                t.gameObject.layer = _layerWas[i];
        }
        _layerTx.Clear();
        _layerWas.Clear();
        _layerWritten = -1;
    }

    /// <summary>The game's ready tracker bar, or null. Its own singleton — never a scene sweep.</summary>
    private static GameObject? TrackerBar()
    {
        try
        {
            if (!Singleton<UIReadyTrackerBar>.IsInitialized)
                return null;
            UIReadyTrackerBar bar = Singleton<UIReadyTrackerBar>.Instance;
            return bar != null ? bar.gameObject : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- B: the notice ----------------------------------------------------------------------------------

    /// <summary>
    /// Draw (or refresh) the one-line status inside the card. Level-triggered on the TEXT: the label
    /// is created once, and re-written only when the sentence actually changes, so a standing
    /// blocker costs one string compare per tick and no allocation.
    /// </summary>
    private static void ShowNotice(UIWindow questWindow)
    {
        if (questWindow.transform is not RectTransform win)
            return;

        // THE SENTENCE IS RE-EVALUATED ON A CADENCE, NOT PER FRAME. DescribeBlock walks the player
        // list and builds strings, and the state behind it changes on a human timescale (somebody
        // joins, the host assigns a hero) — so re-asking four times a second is already far finer
        // than the thing it measures, and re-asking every frame would be a per-frame allocation in
        // the room whose Update budget the perf line already complains about. The last answer stands
        // in between, which is also why it is cached rather than recomputed for the compare below.
        if (Time.unscaledTime >= _blockNextEvalAt)
        {
            _blockNextEvalAt = Time.unscaledTime + BlockEvalIntervalSeconds;
            _blockText = DescribeBlock();
        }
        string text = _blockText;
        if (text.Length == 0)
        {
            HideNotice();
            return;
        }

        if (_noticeGo == null || !ReferenceEquals(_noticeHost, questWindow))
        {
            HideNotice();
            if (!BuildNotice(win))
                return;
            _noticeHost = questWindow;
        }

        RectTransform rect = (RectTransform)_noticeGo!.transform;
        Rect frame = win.rect;
        float windowHeight = Mathf.Abs(frame.height);
        if (windowHeight > 0f)
        {
            Vector2 want = new Vector2(frame.center.x,
                                       frame.yMin + NoticeBottomFractionAboveBottom * windowHeight)
                           - AnchorReference(frame, rect.anchorMin, rect.anchorMax);
            if ((rect.anchoredPosition - want).sqrMagnitude > 0.01f)
                rect.anchoredPosition = want;
            float wantWidth = Mathf.Abs(frame.width) * NoticeWidthFraction;
            if (Mathf.Abs(rect.sizeDelta.x - wantWidth) > 0.5f)
                rect.sizeDelta = new Vector2(wantWidth, rect.sizeDelta.y);
        }

        if (_noticeText != text && _notice != null)
        {
            _noticeText = text;
            _notice.text = text;
        }
        if (!_noticeGo.activeSelf)
            _noticeGo.SetActive(true);
    }

    /// <summary>
    /// Create the label. It is a mod-owned object with no game component on it, so nothing the game
    /// runs can re-write its text: cloning one of the card's own TMP objects was rejected for the
    /// opposite reason — those carry the window's own behaviours and localisation bindings, which
    /// would overwrite the sentence on the next refresh.
    ///
    /// <para>The FONT is borrowed from the card's own text so the line reads as part of the window
    /// rather than as TMP's default asset; the search is one <c>GetComponentInChildren</c> on
    /// creation only, never per tick, and a card with no text at all simply keeps TMP's default.</para>
    /// </summary>
    private static bool BuildNotice(RectTransform win)
    {
        try
        {
            var go = new GameObject("GloomhavenVR.QuestReadyNotice", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(win, worldPositionStays: false);
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = Vector2.up;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(Mathf.Abs(win.rect.width) * NoticeWidthFraction, 0f);

            // OUT OF THE CARD'S LAYOUT GROUP, for the same reason the row and the confirm are.
            var ignore = go.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            TMP_Text? donor = win.GetComponentInChildren<TMP_Text>(true);
            if (donor != null && donor.font != null)
                tmp.font = donor.font;
            tmp.fontSize = NoticeFontPx;
            tmp.enableAutoSizing = false;
            tmp.enableWordWrapping = true;
            // NEVER Truncate: a sentence that names WHO is blocking must not lose the names, and a
            // mid-word stop is a codec, never layout (the ModBuild 281 keycap, and the 96 B placard
            // cap that ate the German tail). Overflow draws the whole string, so a sizing defect
            // shows as text past the frame and gets reported as one.
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = NoticeInk;
            tmp.raycastTarget = false;   // a status line is not a control and must never eat a poke
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _noticeGo = go;
            _notice = tmp;
            _noticeText = string.Empty;
            return true;
        }
        catch (Exception e)
        {
            VRLog.Warn(Scope, "MAP QUEST READY NOTICE: the status line could not be built "
                              + $"({e.GetType().Name}: {e.Message}). CONSEQUENCE: the quest card is "
                              + "silent about why the multiplayer confirm is missing — the reason is "
                              + "still on the MAP TRAVEL CONFIRM ONLINE STAND-BY line, which is "
                              + "unaffected. One line per attempt.");
            _noticeGo = null;
            _notice = null;
            return false;
        }
    }

    /// <summary>Destroy the label. Destroyed rather than hidden because the card it hangs off is
    /// itself destroyed and rebuilt between visits, and a label kept alive across that would be a
    /// leaked child of a window that no longer exists.</summary>
    private static void HideNotice()
    {
        if (_noticeGo != null)
            UnityEngine.Object.Destroy(_noticeGo);
        _noticeGo = null;
        _notice = null;
        _noticeHost = null;
        _noticeText = string.Empty;
    }

    // ---- the blocker, in the game's own words ------------------------------------------------------------

    /// <summary>
    /// WHY THE GAME IS NOT SHOWING ITS QUEST CONFIRM, as one sentence, or the empty string when
    /// nothing about the ready-up is blocked (in which case the confirm's absence is a different
    /// fault and the STAND-BY line's own terms name it).
    ///
    /// <para>The three terms are <c>MapChoreographer.DetermineHostToggleInteractability</c>'s own
    /// (decompiled MapChoreographer.cs:3325), evaluated in the order that method writes them, and
    /// the FIRST false one is what is reported — a conjunction has exactly one first cause and
    /// naming all three would be the "coverage fraction" mistake this project has already paid six
    /// rounds for.</para>
    ///
    /// <para>THE TEXT IS THE GAME'S. Each term maps to the localisation key the flat game shows for
    /// that same state through <c>UIMapMultiplayerController.RefreshWaitingNotifications</c>
    /// (:585-620), so a player who has seen the flat game reads the sentence he already knows. The
    /// English literals are fallbacks for a loc table that has not loaded, never a translation.</para>
    ///
    /// <para>Called from the notice (once per tick, one string compare when unchanged) and from the
    /// rate-limited STAND-BY line. Builds a string, so it must never move onto a per-frame path
    /// without a change gate in front of it.</para>
    /// </summary>
    internal static string DescribeBlock()
    {
        EnsureFfsReflection();

        int players = CountOf(_allPlayers?.GetValue(null));
        if (players <= 1)
            return Loc.Game("GUI_NOTIFICATION_WAITING_PLAYERS_JOIN",
                            "Waiting for other players to join the session.");

        int joining = CountOf(_joiningPlayers?.GetValue(null));
        int connecting = CountOf(_connectingUsers?.GetValue(null));
        if (joining > 0 || connecting > 0)
            return Loc.Game("GUI_NOTIFICATION_WAITING_PLAYERS_JOINING",
                            "Waiting for players to finish joining.");

        string missing = NonParticipants();
        if (missing.Length > 0)
            // THE GAME'S SENTENCE, THEN WHO, THEN WHAT TO DO — and the third line is the one the
            // user asked for (2026-09-04): "ein kleiner Hinweis, dass jedem Spieler mind. 1
            // Character zugewiesen werden muss, damit es weiter gehen kann - dann ist es dem
            // Spieler bewusst." The game's own key describes the STATE ("waiting until…") and never
            // names the ACTION that ends it, which is exactly what was missing in the session that
            // produced this report: three rounds were spent hunting a button the game was
            // deliberately withholding, and nobody could see why. Ordered state → who → action, so
            // a player who already knows the rule can stop reading after the first line.
            return Loc.Game("GUI_MULTIPLAYER_WARNING_TEXT_WaitingForCharAssignment",
                            "Waiting until every player has been assigned a mercenary.")
                   + "\n" + missing
                   + "\n" + Loc.Mod("mp_assign_hint");

        return string.Empty;
    }

    /// <summary>
    /// THE SAME THREE TERMS, FOR THE LOG RATHER THAN FOR THE PLAYER — each one evaluated, with the
    /// numbers behind it, and the first false one called out. Appended to
    /// <c>MAP TRAVEL CONFIRM ONLINE STAND-BY</c>.
    ///
    /// <para>Why both this and <see cref="DescribeBlock"/> exist: the card must say what the player
    /// can DO about it, in the game's own words, and the log must say what the GAME decided, with
    /// its own numbers. Folding them into one string would give the player a term name and the
    /// reader a translation.</para>
    ///
    /// <para>Called only from the STAND-BY line, which is change-gated and rate-limited to one line
    /// per twenty seconds. It allocates and it walks the player list, so it must not move onto a
    /// per-frame path.</para>
    /// </summary>
    internal static string DescribeInteractabilityTerms()
    {
        EnsureFfsReflection();

        int players = CountOf(_allPlayers?.GetValue(null));
        int joining = CountOf(_joiningPlayers?.GetValue(null));
        int connecting = CountOf(_connectingUsers?.GetValue(null));
        string missing = NonParticipants();

        string term1 = players < 0 ? "<not resolvable>" : (players > 1).ToString();
        string term2 = joining < 0 || connecting < 0
            ? "<not resolvable>"
            : (joining == 0 && connecting == 0).ToString();
        string term3 = _isParticipant == null || _allPlayers == null
            ? "<not resolvable>"
            : (missing.Length == 0).ToString();

        string first = players >= 0 && players <= 1
            ? "TERM 1"
            : joining > 0 || connecting > 0
                ? "TERM 2"
                : missing.Length > 0
                    ? "TERM 3"
                    : "NONE — all three read true, so if the confirm is still missing the cause is "
                      + "NOT this method and the remaining candidates are _requestVisible (the host "
                      + "never reached OnSelectedLocation for this selection) and an outstanding "
                      + "BlockVisibility request";

        return $"TERM 1 AllPlayers.Count > 1 = {term1} (Count={players}); "
               + $"TERM 2 JoiningPlayers.Count == 0 && ConnectingUsers.Count == 0 = {term2} "
               + $"(joining={joining}, connecting={connecting}); "
               + "TERM 3 AllPlayers.All(x => x.IsParticipant) = " + term3
               + (missing.Length > 0
                   ? $" — NOT a participant: {missing.Replace("\n", ", ")}. A player is a participant "
                     + "only while he CONTROLS a character (NetworkPlayer.IsParticipant is "
                     + "MyControllables.FirstOrDefault(x => x.IsParticipant) != null, decompiled "
                     + "FFSNet/NetworkPlayer.cs:35), and the host assigns one through the ESC menu's "
                     + "Multiplayer submenu -> a hero slot -> the player picker "
                     + "(UIMultiplayerEscSubmenu.cs:356/362 -> UIMultiplayerSelectPlayerScreen.cs:208). "
                     + "That whole path IS reachable in this room and is not refused by any float "
                     + "table. THE GAME WITHHOLDS ITS OWN READY-UP IN THIS STATE ON PURPOSE and says "
                     + "so through a permanent notification the room cannot draw "
                     + "(UIMapMultiplayerController.RefreshWaitingNotifications :604-608); that is "
                     + "why the card now carries the sentence itself"
                   : string.Empty)
               + $"; FIRST FALSE TERM: {first}.";
    }

    /// <summary>
    /// The usernames of every player who controls no participating character, newline-separated, or
    /// the empty string when they all do. Same population and same predicate the game's own
    /// notification uses (<c>AllPlayers.Where(x =&gt; !x.IsParticipant)</c>,
    /// UIMapMultiplayerController.cs:605-607).
    /// </summary>
    private static string NonParticipants()
    {
        if (_allPlayers?.GetValue(null) is not IEnumerable players
            || _isParticipant == null || _username == null)
            return string.Empty;

        var sb = new StringBuilder(64);
        foreach (object? player in players)
        {
            if (player == null)
                continue;
            try
            {
                if (_isParticipant.GetValue(player) is bool ok && ok)
                    continue;
                string name = _username.GetValue(player) as string ?? "<unnamed>";
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(name);
            }
            catch (Exception)
            {
                // One unreadable player must not cost the whole sentence.
            }
        }
        return sb.ToString();
    }

    /// <summary>A boxed <c>List&lt;T&gt;</c> read as its element-type-free count, so the Photon Bolt
    /// element types of <c>JoiningPlayers</c> / <c>ConnectingUsers</c> never have to resolve at
    /// compile time. -1 when the value could not be read at all.</summary>
    private static int CountOf(object? list) => list is ICollection c ? c.Count : -1;

    /// <summary>Resolve the five optional FFSNet members, once. Every failure is silent by design —
    /// see the reflection block above.</summary>
    private static void EnsureFfsReflection()
    {
        if (_ffsResolved)
            return;
        _ffsResolved = true;
        try
        {
            Type? registry = AccessTools.TypeByName("FFSNet.PlayerRegistry");
            if (registry != null)
            {
                _allPlayers = AccessTools.Field(registry, "AllPlayers");
                _joiningPlayers = AccessTools.Property(registry, "JoiningPlayers");
                _connectingUsers = AccessTools.Property(registry, "ConnectingUsers");
            }
            Type? player = AccessTools.TypeByName("FFSNet.NetworkPlayer");
            if (player != null)
            {
                _isParticipant = AccessTools.Property(player, "IsParticipant");
                _username = AccessTools.Property(player, "Username");
            }
        }
        catch (Exception)
        {
            // A missing registry is a real state offline; the callers all handle nulls.
        }
    }

    // ---- the report --------------------------------------------------------------------------------------

    /// <summary>
    /// WHAT THIS CLIENT ACTUALLY PUT IN THE CARD, on the next hardware log. Printed on an EDGE of the
    /// verdict only — one line per selection, never per frame — because the state it describes stands
    /// for as long as a quest is on the table.
    ///
    /// <para>It answers, positively and in one place, the three things the ModBuild 422 round had to
    /// assemble out of absences: is there a confirm in the card, how many ready icons are beside it
    /// and how many of them are lit, and — when there is no confirm — the sentence the card is
    /// showing instead. The press half of the question is answered by
    /// <see cref="MapQuestReadyPress"/>.</para>
    /// </summary>
    private static void Report(UIWindow? host, GameObject? parkedConfirm)
    {
        int cells = 0;
        int lit = 0;
        if (_row != null)
        {
            Transform t = _row.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (!child.gameObject.activeSelf)
                    continue;
                cells++;
                var group = child.GetComponent<CanvasGroup>();
                if (group != null && group.alpha > 0.9f)
                    lit++;
            }
        }

        // THE CHANGE GATE, ON PRIMITIVES AND WITH NO ALLOCATION. This runs every frame a card is
        // open; the ModBuild 423 version folded the whole verdict into an interpolated string first
        // and then compared it, which built and threw away a string per frame on the map room's
        // Update path for a line that prints once per selection.
        int hostId = host != null ? host.GetInstanceID() : 0;
        int confirmId = parkedConfirm != null ? parkedConfirm.GetInstanceID() : 0;
        int rowId = _row != null ? _row.GetInstanceID() : 0;
        if (_reportEver && hostId == _reportHostId && confirmId == _reportConfirmId
            && rowId == _reportRowId && cells == _reportCells && lit == _reportLit
            && _site == _reportSite && _inkValid == _reportInkValid
            && string.Equals(_noticeText, _reportNotice, StringComparison.Ordinal))
            return;
        _reportEver = true;
        _reportHostId = hostId;
        _reportConfirmId = confirmId;
        _reportRowId = rowId;
        _reportCells = cells;
        _reportLit = lit;
        _reportSite = _site;
        _reportInkValid = _inkValid;
        _reportNotice = _noticeText;

        // Each clause is built into its own local FIRST. Nesting an interpolated string inside an
        // interpolation hole is legal C# and unreadable to every brace-counting tool this repo runs
        // over its own source — scripts/patch-inventory.py loses the nesting depth on it and then
        // reports the Harmony class below as unattributed. Flat locals, one clause each.
        string cardName = host != null ? host.name : "<none>";
        string siteClause = _site == ReadyRosterSite.Quest
            ? "the QUEST card's confirm ('Quest wählen' / 'Quest annehmen', the ready toggle "
              + "MapChoreographer.InitializeSelectQuestReadyUp labels GUI_SELECT_QUEST for the host "
              + "and GUI_ACCEPT_QUEST for a client)"
            : "the CHARACTER-UI's 'Verlies betreten' (GUI_LOADOUT_ENTER_SCENARIO, the ready toggle "
              + "UILoadoutManager.MPConfirmEnterScenario initialises at :507 before calling "
              + "ShowLoadoutMultiplayer at :516)";
        string confirmClause = parkedConfirm != null
            ? $"YES ('{parkedConfirm.name}', the game's own UIReadyToggle — pressing it runs the "
              + "game's UIReadyToggle.InputToggle -> ReadyUp -> ReadyUpPlayer and nothing of this "
              + "mod's)"
            : "NO";
        string rowClause = _row != null
            ? $"YES ('{_row.name}', the game's own UIReadyTrackerBar) with {cells} cell(s), {lit} of "
              + "them CONFIRMED (a lit cell is CanvasGroup alpha 1, the game's own "
              + "UIReadyTracker.ShowReady(true); a greyed one is 0.5 and means that player still has "
              + "to press)"
            : "NO";
        string noticeClause = _noticeText.Length > 0
            ? "'" + _noticeText.Replace("\n", " / ") + "'"
            : "<none>";
        // ADOPTED, NEVER CLONED, and the distinction is not academic in this room: the scenario fan
        // ADOPTS the game's widget while the map-room fan CLONES it, so a fix naming one is inert in
        // the other [[two-fans-one-name]]. This is the game's own singleton instance, moved and handed
        // back verbatim — which is why the cells cannot disagree with the flat HUD's.
        string adoptClause = _row != null
            ? "ADOPTED (the game's own Singleton<UIReadyTrackerBar> instance re-parented into the "
              + "floated window and handed back with its parent, sibling index, anchors, pivot, "
              + "anchoredPosition, rotation and scale restored verbatim) — NOT cloned, so there is no "
              + "second copy of a replicated ready state anywhere in this mod; "
              + $"LAYER {_layerWritten} written onto {_layerTx.Count} transform(s) (a converted "
              + "window's capture camera culls BY LAYER, so a row left on the flat HUD's layer would "
              + "be present, active, correctly placed and drawn by no camera at all — 0 here means it "
              + "already held the host's layer and nothing had to be written)"
            : "<nothing adopted>";
        string geometryClause = _inkValid
            ? $"row ink {_rowInkHeight:F1} px tall from {_rowInkGraphics} graphic(s) inside a rect "
              + $"{_rowRectHeight:F1} px tall (THE DIFFERENCE IS THE ModBuild 423 DEFECT: the rect was "
              + "reserved and the ink is what the eye sees); confirm ink from "
              + $"{_confirmInkGraphics} graphic(s), its painted TOP at y={_lastConfirmInkTop:F1}; "
              + $"GAP {_gapPx:F1} px, taken from {_gapWhy}; the row's painted centre placed at "
              + $"y={_lastRowInkCentreY:F1}; DELTA APPLIED this tick "
              + $"({_lastDelta.x:F2}, {_lastDelta.y:F2}) px"
            : $"NOT PLACED — {_inkWhy}; nothing was written and nothing was reserved, so the confirm "
              + "keeps the seat its own parker gave it";
        // THE TWO NUMBERS THAT MEAN DIFFERENT THINGS, PRINTED TOGETHER. anchoredPosition runs to the
        // rect's OWN pivot and the centre runs to the parent's frame, so a rect authored with a
        // foreign pivot makes them disagree — printing the offset is what lets that rect name itself
        // in the log instead of being measured off a photograph [[anchoredposition-is-not-a-frame]].
        string frameClause = _inkValid
            ? $"row parent-local centre y={_lastRowLocalCentreY:F1} (localPosition.y + "
              + $"rect.center.y) against raw anchoredPosition.y={_lastRowAnchoredY:F1}, offset "
              + $"{_lastRowLocalCentreY - _lastRowAnchoredY:F1} px — they are the SAME frame only "
              + "when the rect's pivot and its anchor coincide, and every correction above is a DELTA "
              + "for exactly that reason"
            : "<no frame measured>";

        // HW-VERIFY: reports (a) and (b) of 2026-09-05 — "Beim 'Verlies betreten' Button will ich
        // auch die Symbole sehen vom Spiel, wer schon akzeptiert hat und wer nicht" and "Der
        // vertikale Abstand zwischen der Anzeige wer schon akzeptiert hat im Questfenster ist zu
        // groß, siehe Abstand_Quest.jpg".
        VRLog.Note(Scope,
            "MAP QUEST READY CARD: what this client is drawing beside the confirm right now. "
            + $"ticks={_ticks} (LIVENESS — counted before any early return, so a small number here "
            + "means this class stopped being called and not that nothing changed); "
            + $"CONFIRMATION={siteClause}; card='{cardName}'; CONFIRM={confirmClause}; "
            + $"ICON ROW={rowClause}; SOURCE={adoptClause}; GEOMETRY={geometryClause}; "
            + $"FRAMES={frameClause}; reserved {ReservedHeight():F1} window-local unit(s) above the "
            + $"confirm; NOTICE={noticeClause}. "
            + "READ IT WITH 'MAP TRAVEL CONFIRM ONLINE STAND-BY': that line carries the game's own "
            + "UIReadyToggle terms and the evaluated DetermineHostToggleInteractability sub-terms, "
            + "and this line carries what the player can see as a result. A CONFIRM=NO with a NOTICE "
            + "is the HONEST degraded state and is not a fault; a CONFIRM=NO with NOTICE=<none> "
            + "online IS one and must be reported. ICON ROW=NO beside a CONFIRM=YES is the honest "
            + "state OFFLINE and on the loadout it is the ONLY offline state, because "
            + "UILoadoutManager.MultiplayerStartup never reaches MPConfirmEnterScenario when "
            + "FFSNetwork.IsOnline is false and the bar is therefore never populated at all.");
    }

    // ---- geometry ------------------------------------------------------------------------------------------

    /// <summary>The point in <paramref name="frame"/>'s own local space that a child's
    /// <c>anchoredPosition</c> is measured FROM, for the given anchors. Identical to
    /// <c>MapTravelConfirm.AnchorReference</c> — duplicated rather than shared because it is three
    /// lines of definition and sharing it would make the two placements one another's dependency.</summary>
    private static Vector2 AnchorReference(Rect frame, Vector2 anchorMin, Vector2 anchorMax)
    {
        Vector2 a = (anchorMin + anchorMax) * 0.5f;
        return new Vector2(frame.xMin + a.x * frame.width, frame.yMin + a.y * frame.height);
    }
}

// ---- the press ---------------------------------------------------------------------------------------------

/// <summary>
/// WHICH READY-UP ENTRY POINT A PRESS REACHED, AND WHAT THE GAME REPORTED BACK.
///
/// <para>A read-only postfix on the game's own <c>UIReadyToggle.ReadyUp(bool, bool)</c> — the
/// method both the one-argument overload (:610) and the toggle's own <c>InputToggle</c> funnel
/// through, so one patch covers every path a press can take. It has no return value and no
/// <c>__result</c>, so it cannot change what the method did; it states what the method decided.</para>
///
/// <para>WHY THE LINE IS WORTH A PATCH. Three rounds have now ended with "he pressed it and
/// nothing happened" and no way to tell a press that never arrived from a press the game refused.
/// <c>ReadyUp</c> has exactly one silent refusal — the early return at UIReadyToggle.cs:618, when
/// the ready set is already full — and one gated one, <c>IsReadyUpForbidden</c> (:600), which for
/// the quest ready-up is <c>AdventureMapUIManager.CheckTravel</c>. Printing the population sizes
/// beside the verdict is what makes those two distinguishable from a press that was never
/// delivered at all.</para>
///
/// <para>ONE LINE PER PRESS. A ready-up is a deliberate human act; there is no cadence to bound
/// and nothing here runs per frame.</para>
/// </summary>
[HarmonyPatch]
internal static class MapQuestReadyPress
{
    private const string Scope = "MapRoom";

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.ReadyUp), typeof(bool), typeof(bool))]
    private static void AfterReadyUp(UIReadyToggle __instance, bool toggledOn)
    {
        try
        {
            if (!MapRoomDriver.Active)
                return;
            int ready = __instance.PlayersReady != null ? __instance.PlayersReady.Count : -1;
            // HW-VERIFY: report 3 — "ich will das JEDER Mitspieler den klicken muss".
            VRLog.Note(Scope,
                $"MAP QUEST READY PRESS: the game's own UIReadyToggle.ReadyUp(toggledOn={toggledOn}) "
                + $"ran on this client for '{__instance.name}'. THE GAME REPORTS BACK: "
                + $"PlayersReady.Count={ready}, ToggledOn={__instance.ToggledOn}, "
                + $"IsVisible={__instance.IsVisible}, IsInteractable={__instance.IsInteractable}, "
                + $"ShouldBeVisible={__instance.ShouldBeVisible}, online={FFSNetwork.IsOnline}, "
                + $"host={FFSNetwork.IsHost}. THIS IS AN INPUT AND NOTHING ELSE: the press lands "
                + "in the game's own ReadyUpPlayer, which adds this player to PlayersReady and "
                + "sends GameActionType.ReadyUpPlayer (decompiled UIReadyToggle.cs:643-676) — the "
                + "mod sends nothing and writes no ready state. IF PlayersReady.Count DID NOT "
                + "RISE, the game refused the press and there are exactly two refusals to look "
                + "at: the early return at UIReadyToggle.cs:618 (the ready set is already full) "
                + "and IsReadyUpForbidden (:600), which for the quest ready-up is "
                + "AdventureMapUIManager.CheckTravel. When the LAST participant readies, the host "
                + "runs WaitForStateSyncBeforeProceeding and the journey starts — that is the "
                + "game's decision, not this room's.");
        }
        catch (Exception)
        {
            // A diagnostic must never take the game's own ready-up down with it.
        }
    }
}
