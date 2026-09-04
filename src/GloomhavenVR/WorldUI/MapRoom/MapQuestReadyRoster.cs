using System;
using System.Collections;
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
// ---------------------------------------------------------------------------

/// <summary>
/// The multiplayer ready ROW inside the quest card — the game's own <c>UIReadyTrackerBar</c> parked
/// directly above the parked confirm — plus the status line that names the blocker when the game is
/// withholding its confirm altogether. Ticked by <see cref="MapTravelConfirm.Reconcile"/>. See the
/// block comment above for the source citations and the hardware log lines behind every claim.
/// </summary>
internal static class MapQuestReadyRoster
{
    private const string Scope = "MapRoom";

    /// <summary>Gap between the icon row's bottom edge and the confirm's top edge, as a fraction of
    /// the quest window's own height. ~15 px on the 1021 px card the ModBuild 422 log measures —
    /// enough that the row reads as its own element and small enough that the pair still reads as
    /// one block hanging under the quest information. A fraction rather than a pixel count for the
    /// same reason the two offset dials are fractions: the card is not always the same height.</summary>
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

    // ---- the HW-VERIFY report ------------------------------------------------------------------------

    /// <summary>What the last report line said, so the report is printed on an EDGE and not per
    /// frame. The whole verdict is folded into this string; a verdict that has not moved says
    /// nothing new.</summary>
    private static string _reportVerdict = string.Empty;

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
    /// Reconcile both elements against the live state, once per frame, from
    /// <see cref="MapTravelConfirm.Reconcile"/> — i.e. from the map room's own Update tick, the same
    /// place the confirm's parking runs.
    /// </summary>
    /// <param name="questWindow">The floated quest card, or null when none is floated.</param>
    /// <param name="parkedConfirm">The object <see cref="MapTravelConfirm"/> is holding inside that
    /// card THIS TICK while it is the game's multiplayer ready toggle, or null. The icon row exists
    /// only beside that object: offline there is no multiplayer readiness to show, and an icon row
    /// with no button under it would be the "leeres Fenster" the standing ruling forbids.</param>
    /// <param name="confirmPosed">Has the confirm's own dialled pose been written yet? The row hangs
    /// off the confirm's top edge, so a pose that has not settled is one the row must not follow —
    /// it would place the icons at the confirm's PREVIOUS offset for one tick.</param>
    internal static void Tick(UIWindow? questWindow, GameObject? parkedConfirm, bool confirmPosed)
    {
        bool roomStands = MapRoomDriver.Active;
        bool haveCard = roomStands && questWindow != null;

        if (!haveCard || parkedConfirm == null)
            ReleaseRow(!roomStands ? "the map room stood down"
                : questWindow == null ? "no quest card is floated"
                : "the game's multiplayer quest confirm is not parked in the card");
        else
            HoldRow(questWindow!, parkedConfirm, confirmPosed);

        // THE NOTICE IS THE COMPLEMENT OF THE CONFIRM, never a companion to it: it is drawn only
        // while the card is floated, this client is online, and the game is showing no confirm at
        // all. The moment the confirm appears the sentence is gone, so the card can never carry both
        // a button and an explanation of why there is no button.
        bool wantNotice = haveCard && parkedConfirm == null && FFSNetwork.IsOnline;
        if (wantNotice)
            ShowNotice(questWindow!);
        else
            HideNotice();

        Report(questWindow, parkedConfirm);
    }

    /// <summary>
    /// The vertical room the icon row is taking above the confirm, in the host window's own local
    /// units — added to the confirm's downward offset by <c>MapTravelConfirm.ApplyPose</c> so the
    /// two do not overlap and the MEASURED ZERO still lands on the top of the pair.
    ///
    /// <para>Exactly 0 whenever this class is not holding the row, which is every offline tick. That
    /// is what makes single player bit-identical to ModBuild 422 rather than "probably unchanged".</para>
    /// </summary>
    internal static float ReservedHeight() => _row != null ? _reserved : 0f;

    /// <summary>Hand both elements back — the room is going away under them. Called from
    /// <see cref="MapTravelConfirm.Reset"/>.</summary>
    internal static void Reset()
    {
        ReleaseRow("map room teardown");
        HideNotice();
        _blockNextEvalAt = float.NegativeInfinity;
        _blockText = string.Empty;
        _rowWarned = false;
        _reportVerdict = string.Empty;
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

        // The game owns activeSelf: ShowCharactersTrackers does SetActive(true) and HideTrackers
        // SetActive(false) (UIReadyTrackerBar.cs:57/226). We only ever READ it — a row we switched on
        // ourselves would be a row claiming a readiness the game is not tracking.
        if (!bar.activeInHierarchy)
        {
            _reserved = 0f;
            return;
        }

        // THE ROW HANGS OFF THE CONFIRM'S TOP EDGE. The confirm's pivot is (0.5, 1) — its own top
        // centre — so the point below is literally where the confirm's top edge is this tick, in the
        // card's own local space, whatever anchor the confirm happens to be carrying. Reading it
        // rather than recomputing it is what keeps the two from ever disagreeing.
        if (!confirmPosed)
            return;
        Rect frame = win.rect;
        float windowHeight = Mathf.Abs(frame.height);
        if (windowHeight <= 0f)
            return;

        Vector2 confirmTop = LocalPivot(frame, confirmRect);
        float gap = RowGapWindowHeights * windowHeight;

        if (rect.anchorMin != rect.anchorMax)
            rect.anchorMin = rect.anchorMax = Vector2.up;
        // Bottom centre: the row grows UPWARD from the gap. Written only when it is not already
        // the answer — a uGUI pivot write dirties the layout whether or not the value changed, and
        // this method runs every frame a quest card is open.
        var wantPivot = new Vector2(0.5f, 0f);
        if (rect.pivot != wantPivot)
            rect.pivot = wantPivot;
        Vector2 want = new Vector2(frame.center.x, confirmTop.y + gap)
                       - AnchorReference(frame, rect.anchorMin, rect.anchorMax);
        if ((rect.anchoredPosition - want).sqrMagnitude > 0.01f)
            rect.anchoredPosition = want;

        // WHAT THE CONFIRM HAS TO MOVE DOWN BY, recomputed from the row's live height every tick so
        // a bar that gains or loses a cell (a player joins, a character dies) settles by itself.
        _reserved = Mathf.Max(0f, rect.rect.height) + gap;
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
            return Loc.Game("GUI_MULTIPLAYER_WARNING_TEXT_WaitingForCharAssignment",
                            "Waiting until every player has been assigned a mercenary.")
                   + "\n" + missing;

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
    private static void Report(UIWindow? questWindow, GameObject? parkedConfirm)
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

        string verdict = $"{(questWindow != null ? questWindow.name : "<none>")}|"
                         + $"{(parkedConfirm != null ? parkedConfirm.name : "<none>")}|"
                         + $"{(_row != null ? "row" : "norow")}|{cells}|{lit}|{_noticeText}";
        if (verdict == _reportVerdict)
            return;
        _reportVerdict = verdict;

        // Each clause is built into its own local FIRST. Nesting an interpolated string inside an
        // interpolation hole is legal C# and unreadable to every brace-counting tool this repo runs
        // over its own source — scripts/patch-inventory.py loses the nesting depth on it and then
        // reports the Harmony class below as unattributed. Flat locals, one clause each.
        string cardName = questWindow != null ? questWindow.name : "<none>";
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

        // HW-VERIFY: report 3 — "Alle spieler sollen dort den Button sehen … die icons der Spieler
        // die es bereits bestätigt haben über dem Button".
        VRLog.Note(Scope,
            "MAP QUEST READY CARD: what this client is drawing in the quest card right now. "
            + $"card='{cardName}'; CONFIRM={confirmClause}; ICON ROW={rowClause}; "
            + $"reserved {ReservedHeight():F1} window-local unit(s) above the confirm; "
            + $"NOTICE={noticeClause}. "
            + "READ IT WITH 'MAP TRAVEL CONFIRM ONLINE STAND-BY': that line carries the game's own "
            + "UIReadyToggle terms and the evaluated DetermineHostToggleInteractability sub-terms, "
            + "and this line carries what the player can see as a result. A CONFIRM=NO with a NOTICE "
            + "is the HONEST degraded state and is not a fault; a CONFIRM=NO with NOTICE=<none> "
            + "online IS one and must be reported.");
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

    /// <summary>Where a child's PIVOT sits in the host frame's own local space, read off the rect
    /// rather than recomputed from the dials — so this class can never disagree with the placement
    /// the confirm's own class wrote.</summary>
    private static Vector2 LocalPivot(Rect frame, RectTransform rect) =>
        AnchorReference(frame, rect.anchorMin, rect.anchorMax) + rect.anchoredPosition;
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
