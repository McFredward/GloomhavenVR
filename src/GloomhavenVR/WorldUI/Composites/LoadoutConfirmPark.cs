using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE PRE-SCENARIO CONFIRM BELONGS ON THE LOCAL CHARACTER SCREEN, AND THE GAME'S OWN SOURCE IS
/// WHAT DECIDES THAT.
///
/// <para><b>USER REPORT (ModBuild 234 hardware, item 4, verbatim):</b> <i>"DEADLOCK: Nachdem ich für
/// jeden Character die Quest ausgewählt habe, muss irgendwo der button erscheinen damit es weiter
/// gehen kann. Der ist nie erschienen, man konnte nicht weiter vorranschreiten."</i> And, in the
/// same breath, the question that decides the design: <i>"Frage: Im Flat-Spiel, sieht das nur der
/// Host oder wird das jedem im Multiplayer präsentiert? Abhängig von der Antwort möchte ich
/// entweder, dass der Button auf der lokalen Character-UI oder dem MP-Fenster mit der Story
/// angezeigt wird."</i></para>
///
/// <para><b>THE ANSWER, FROM SOURCE, AND IT IS "EVERY PLAYER".</b>
/// <c>UILoadoutManager.SetActiveConfirmationButton</c> (decompiled UILoadoutManager.cs:98-108) is
/// <c>value &amp;= CanShowConfirmationButton(); if (FFSNetwork.IsOnline)
/// Singleton&lt;UIReadyToggle&gt;.Instance.ToggleVisibility(value); else
/// SetActiveSinglePlayerLongConfirmButton(value);</c> — one branch per network mode and NO host
/// test anywhere in it. The online branch's toggle is set up by <c>MPConfirmEnterScenario</c>
/// (:474-517), which runs from <c>MultiplayerStartup</c> on EVERY client, initialises the ready
/// toggle with <c>show: true</c> and the label <c>"GUI_LOADOUT_ENTER_SCENARIO"</c>, and wires the
/// ALL-PLAYERS-READY callback to <c>delegate { if (FFSNetwork.IsHost) ConfirmEnterScenario(); }</c>.
/// So every player is shown the control, every player confirms FOR HIMSELF, and the host merely
/// commits once the last one has. That makes pressing it a LOCAL act, and by the user's own rule the
/// control belongs on the LOCAL Character-UI — not on the shared story window.</para>
///
/// <para><b>SO THIS CLASS PARKS IT INTO THE FLOATED CHARACTER SCREEN</b> ('New Party display', ID
/// <c>PartyPanel</c> — the map room's permanent, X-less window), exactly the way
/// <c>MapRoom/MapTravelConfirm</c> parks the quest confirm into the quest card: record the home
/// before the first move, restore it verbatim, re-measure the anchor on a cadence, never write game
/// state, and — for the online control, which is a <c>UIWindow</c> and therefore something the float
/// gate can see — set the park claim so <c>FloatRefusalTable</c> knows somebody else is drawing it.
/// That last point is not tidiness: a refusal with no parker behind it is a deadlock generator, and
/// the round this file was written in already contained one.</para>
///
/// <para>=====================================================================================
/// WHY THE DEADLOCK HAPPENED, BECAUSE THIS CLASS IS ALSO THE SECOND HALF OF ITS FIX
/// =====================================================================================</para>
///
/// <para>The single-player continue button is <c>UILoadoutManager.confirmationButton</c>, a private
/// serialized <c>Button</c> whose GameObject <c>SetActiveSinglePlayerLongConfirmButton</c> switches
/// on (:88-95). It is a CHILD OF THE LOADOUT WINDOW. In the ModBuild 234 log
/// <c>FloatRefusalTable</c>'s <c>UILoadoutManager</c> row withdrew that window's float at
/// Player.log:11483-11484 and the claim behind the row never lapsed, so from that instant the only
/// object carrying the continue button was drawn on 'Campaign Canvas', which the 3D map room does not
/// render. No loadout screen, no button, no way forward — and the game had said the button should be
/// showable (<c>[GUI] EnableLoadoutInteraction</c> at :11535).</para>
///
/// <para><b>PARKING THE CONTROL MAKES THAT CLASS OF FAULT SURVIVABLE RATHER THAN FATAL.</b> Once the
/// confirm is drawn inside the Character-UI it does not matter whether the loadout window floats:
/// the control is reachable either way. <c>StoryComposite</c>'s claim was fixed independently (it can
/// no longer outlive the intro), and this is the belt to that pair of braces — two independent
/// mechanisms, because the fault they prevent is the one the player cannot work around.</para>
///
/// <para><b>THE FALSIFIER IS THE POINT OF THE FILE.</b> <c>LOADOUT CONFIRM REACHABLE: CONFIRMED</c>
/// is printed only when the GAME says a continue control should be showable
/// (<c>UILoadoutManager.CanShowConfirmationButton()</c>, plus the game's own switch on the control)
/// AND that control is measured drawing inside a live floated panel this tick. The failing form is a
/// WARNING that NAMES THE SUPPRESSOR by asking <c>FloatRefusalTable.Describe</c> — so a hardware log
/// says which claim is hiding the button instead of leaving the next round to infer it from an
/// absence, which is what cost this one.</para>
///
/// <para><b>MULTIPLAYER: nothing here goes on the wire.</b> This class re-parents a local uGUI
/// subtree under a local host on THIS client. It never presses the control, never writes
/// <c>Show</c>/<c>Hide</c>/<c>Escape</c>/<c>SetActive</c> on a game window and never touches the ready
/// toggle's own state — <c>ToggleVisibility</c>, <c>SetInteractable</c> and the ready-up itself stay
/// the game's. Two players may legitimately disagree about every verdict in it.</para>
///
/// <para>=====================================================================================
/// ModBuild 241 — THE SEAT MOVED FROM UNDER THE ROSTER TO BESIDE IT, AND IT NOW RE-SOLVES
/// =====================================================================================</para>
///
/// <para><b>USER REPORT, 2026-08-24, verbatim:</b> <i>"Der 'Verlies betreten' Button ist jetzt auf dem
/// richtigen Fenster aber an einer komischen Position, siehe betreten_button.jpg. Er sollte eher
/// rechts neben den Charakteren angezeigt werden, mittig zentriert. Es muss aber noch möglich sein
/// auch im Nachgang bevor man den Button drückt noch eine persönliche Quest zu ändern. Das Auftauchen
/// der Questinfos obwohl der Button schon da ist muss ihn entsprechend verschieben, damit sich nichts
/// überlagert."</i></para>
///
/// <para><b>WHAT THE PHOTO SHOWS</b> (<c>.planning/debug/betreten_button.jpg</c>). The floated
/// Character-UI is a tall narrow column: the title <i>Die Löscher</i>, three party stat bars, then
/// four character rows stacked vertically (Hilde Die 2Te, Scream, Lastglowworm, Cryonaris). The
/// <i>VERLIES BETRETEN</i> plate sits at the very BOTTOM of that column, ON TOP of the fourth row —
/// Cryonaris' portrait and his gold line are behind it, and the bottom of his icon strip (the battle
/// goal toggle is the LAST icon in it) is under the plate's top edge. The brass grab bar is
/// immediately below the plate.</para>
///
/// <para><b>THE MECHANISM, AND IT IS ONE CLAMP.</b> ModBuild 239's zero put the control's painted TOP
/// edge <see cref="ConfirmGapPx"/> BELOW the window's painted BOTTOM edge and centred it horizontally
/// on the painted content. On this window the painted content fills the frame vertically — the roster
/// column runs from the title at y=+540 to the last row at y=-540 — so "below the content" is below
/// the frame, and <see cref="RefreshAnchor"/>'s frame clamp (which exists so a uGUI mask can never
/// cull the control, and which is KEPT) then pulled the control back up until its ink sat flush with
/// the frame's bottom edge. Flush with the bottom edge of a column that is roster all the way down IS
/// on top of the last character. The clamp did exactly what it was written to do; the construction it
/// was protecting had no room to be right.</para>
///
/// <para><b>THE COORDINATES, from the ModBuild 239 hardware log</b>
/// (<c>.planning/debug/LogOutput.log</c>), all in the game window root's own authored uGUI px unless
/// stated:</para>
/// <list type="bullet">
/// <item>the game window root 'New Party display' rect is <c>x -960..960, y -540..540</c>
/// (<c>ENTER DUNGEON …</c>, :9115). The mod's HOST rect around it is a DIFFERENT transform and a
/// different number, <c>x -994..994</c>, 1988x1080 — the two lines print both and they are not
/// interchangeable.</item>
/// <item>with the battle-goal picker CLOSED the window draws <c>x -982..-653</c>, 330 px wide, centred
/// at <c>x=-817</c>, <c>y -540..540</c> — measured twice on the same tick by two independent
/// instruments that share none of their code, <c>HIT RECT</c> (<c>DRAWN CONTENT 330x1080 px at
/// (-817,0) from 137 visible graphic(s)</c>, :9121) and <c>GRAB BAR CLEARS THE INK</c>
/// (<c>the ink union spans x -982..-653 … 137 graphic(s) unioned</c>, :9123). THAT 330 px COLUMN IS
/// "die Charaktere". The frame around it is 1920 px wide: five sixths of this window is empty.</item>
/// <item>with the picker OPEN the same union is <c>x -982..-88</c>, 894 px wide, centred at
/// <c>x=-535</c>, and it reaches down to <c>y=-628</c> on a graphic literally called <c>Rewards</c>
/// (:9117, 179 graphics). So the quest information the user means by <i>"die Questinfos"</i> draws in
/// the band from <c>x=-653</c> to <c>x=-88</c> — immediately to the RIGHT of the roster column, which
/// is the band ModBuild 239's centring would have put the button in.</item>
/// <item>the control's own painted ink is <c>307x65 px</c>. At :9115, with the picker open, it sat at
/// <c>x -679..-372, y -540..-475</c> — centred on the 894 px union's centre, i.e. inside the picker.
/// In the photograph the picker is closed, so the same rule put it at <c>x -960..-653</c>, the width
/// of the column and squarely on Cryonaris.</item>
/// </list>
///
/// <para><b>WHAT REPLACES IT.</b> The seat is now two independent measurements instead of one:</para>
/// <list type="number">
/// <item><b>THE VERTICAL ZERO IS THE CHARACTER BLOCK ITSELF</b>, not the window's content and
/// emphatically not its frame. <see cref="TryCharacterBlock"/> unions the painted bounds of the game's
/// OWN <c>NewPartyDisplayUI.CharacterSlots</c> — the four <c>NewPartyCharacterUI</c> rows, by
/// reference off the display's serialized list, never by name and never by a scene sweep — and the
/// control's ink centre is put on that block's centre. "Rechts neben den Charakteren, mittig
/// zentriert" is then a statement about the rows and nothing else: it does not move when the title,
/// the stat bars or a popup change, because none of those is a character row.</item>
/// <item><b>THE HORIZONTAL ZERO IS THE RIGHT EDGE OF EVERYTHING THE WINDOW DRAWS</b>, plus the gap.
/// Not the block's right edge: the quest information appears BETWEEN the block and the frame's right
/// half, and a seat measured off the block alone would be underneath it the moment it opened. Right of
/// the whole drawn union is the only x that satisfies <i>"damit sich nichts überlagert"</i> in both
/// states, and it is why the button moves ~565 px right when the picker opens (union right edge
/// -653 → -88) and back when it closes.</item>
/// </list>
///
/// <para><b>AND IT RE-SOLVES, WHICH IS THE THIRD REQUIREMENT.</b> The placement was already a cadence
/// — <see cref="AnchorRefreshIntervalSeconds"/>, four times a second, with
/// <see cref="OffsetEpsilonPx"/> suppressing the write when the answer has not moved — so requirement
/// three needed no second loop, only a faster edge. <see cref="ApplyPose"/> now also re-solves
/// IMMEDIATELY when <c>PanelInkBounds.ActiveSetSignature</c> changes; that signature's part two is the
/// game's own <c>ActiveDisplay</c> plus which sub-view roots are open, and it exists precisely because
/// the battle-goal picker's root is not a direct child of the conversion target and part one alone
/// misses it. THE CADENCE IS STILL THE GUARANTEE and the signature is the fast path, which is the
/// right way round: if the picker is ever shown by something an <c>activeInHierarchy</c> read cannot
/// see, the seat is late by one cadence rather than wrong forever. That the edge does fire on this
/// window is not assumed — the ModBuild 239 log has <c>GRAB BAR CLEARS THE INK</c> for
/// 'New Party display' moving from generation 4 to generation 5 with <c>cause: the set of open
/// sub-views changed</c> as the picker closed (:9117 and :9123). One int compare per tick buys
/// it.</para>
///
/// <para><b>NOTHING GATES THE PICKER, AND THAT IS MEASURED RATHER THAN ASSERTED.</b> The user's second
/// sentence is the standing ModBuild 239 ruling again (<i>"Ich will, dass es voll bedienbar ist egal
/// welche Fenster offen sind"</i>, which produced <c>MapRoom/CharacterUiOperability</c>). Two answers,
/// both of them readings and not intentions. (a) THE GAME does not gate it: the confirm's own
/// precondition <c>UILoadoutManager.CanShowConfirmationButton()</c> returns TRUE while
/// <c>ActiveDisplay == BATTLE_GOALS</c> (UILoadoutManager.cs:111-119), i.e. the button and an open
/// quest picker are a state the flat game already ships. (b) THIS MOD does not gate it either — no
/// line in this file writes <c>interactable</c>, a <c>CanvasGroup</c>, <c>SetActive</c> or a selection
/// mode — but until this round it OCCLUDED it, which is the same thing to a hand: the plate is a
/// <c>Button</c> whose graphics take raycasts, <see cref="Park"/> puts it at
/// <c>SetAsLastSibling</c> so it is drawn and hit-tested ABOVE the roster, and it was parked across the
/// fourth row. <c>ENTER DUNGEON ON THE CHARACTER UI</c> now carries a per-slot term that reads each
/// row's <c>battleGoalToggle.IsInteractable()</c> AND whether the control's ink intersects that
/// toggle's rect, so a hardware log states the answer instead of leaving it to be inferred from a
/// photograph. <see cref="CharacterUiOperability"/> cannot fight this: its re-arm only ever RAISES
/// <c>buttonsCanvasGroup.interactable</c>/<c>blocksRaycasts</c> on assigned slots, it writes no
/// geometry, and its own verdict is scoped to ticks on which a guildmaster destination window is open
/// — of which there are none during a loadout.</para>
///
/// <para>=====================================================================================
/// ModBuild 382 — WHAT ACTUALLY DIFFERS BETWEEN THE FIRST OPEN AND THE RE-OPEN
/// =====================================================================================</para>
///
/// <para><b>USER, 2026-09-03:</b> <i>"Beim erneuten Öffnen der persönlichen Quest nachdem man es
/// schon geöffnet hatte sind die Abstände falsch"</i>, and earlier, on the same defect: <i>"Ich will
/// das es wieder ganz links spawnt wie es war und der button rechts daneben, nicht darüber
/// liegend."</i></para>
///
/// <para><b>THE TERM THAT MOVES IS THIS CLASS'S CONTENT UNION, AND THE ModBuild 380 LOG PRINTS IT ON
/// BOTH SIDES.</b> Every number below is a <c>LOADOUT CONFIRM RESEATED</c> line from
/// <c>second_logs/LogOutput.log</c>, in order. Roster alone: 83-85 graphic(s), union
/// -960..-656. QUEST INFORMATION OPEN AND CORRECT — this is the state the user wants back:
/// 118/123/128 graphic(s), union right edge -104 / -90 / -80, seat -80..227 then -56..251, one 24 px
/// gap and no overlap. THE BROKEN STATE, twice: <b>599 and 595 graphic(s), union -960..960 x
/// -540..540</b>, seat clamped to 653..960. So the term is not subtle and it is not the gap constant:
/// the union's graphic count goes up by a factor of roughly five and its right edge jumps 1050 px to
/// the frame's own edge. <c>PanelInkBounds</c> agrees from its own implementation — the
/// <c>HIT RECT</c> line for the same window at that instant reads "DRAWN CONTENT 1964x1453 px at
/// (0,-187) from 597 visible graphic(s)", i.e. 373 px BELOW a frame that is 1080 px tall.</para>
///
/// <para><b>AND THE PHOTOGRAPH SAYS THE COLUMN REALLY IS SHOVED, NOT ONLY MIS-MEASURED.</b>
/// fenster-abstand2.jpg, measured rather than eyeballed: the character column is 329 screen px wide
/// against the 328 authored px the fixed-fit line reports, so near it the panel renders about 1:1,
/// and the quest cards start 1055 screen px to the RIGHT of that column's right edge. In the good
/// state the cards END at -80. They are drawn roughly a thousand authored px right of where the log's
/// own correct state puts them, hard against the frame's right edge — which is the whole of
/// <i>"die Abstände sind falsch"</i> and <i>"es soll wieder ganz links spawnen"</i>.</para>
///
/// <para><b>WHAT THAT MEANS FOR THIS FILE, STATED PLAINLY.</b> The seat solve is not the cause. It
/// asks for one 24 px gap right of what the window paints, the window is painting to its own right
/// edge, and a 307 px control cannot fit right of 960 inside a frame that ends at 960. The clamp is
/// the CONSEQUENCE. The cause is whatever places the battle-goal column on a re-open, and it is
/// <b>not</b> the ModBuild 367 viewport-fit cut: that cut FIRED in this log, on 'Container', inside
/// this very window, and returned zero exactly as designed — the union reached the frame edge anyway.
/// This file's job is therefore bounded and it is done here: <b>the overlap goes, on every one of the
/// user's three cases, without pretending to have moved the column.</b> The lane solve below seats
/// the control in the empty thousand px the photograph shows between the roster and the shoved
/// column, which is where the user asked for it in the first place — <i>"rechts neben den
/// Charakteren"</i>.</para>
///
/// <para><b>ALTERNATIVES REJECTED.</b>
/// <list type="bullet">
/// <item><i>Keep it under the roster and shorten the column.</i> That is presentation code resizing a
/// game window's content, which this file may not do, and the column is roster all the way to the
/// frame edge anyway.</item>
/// <item><i>Seat it against the window's frame — e.g. a fixed fraction of the 1920 px rect.</i> The
/// drawn column is 330 px of that 1920 and is pinned to the LEFT (centre x=-817). A frame-derived seat
/// puts the button roughly 900 px out in transparent nothing. This window has produced that exact bug
/// twice already — the ModBuild 234 arc reservation (88° booked to draw 14°) and the ModBuild 236 grab
/// bar — and <c>PanelInkBounds</c>' class comment is the write-up of both.</item>
/// <item><i>Seat it right of the CHARACTER BLOCK only.</i> Correct with the picker closed, underneath
/// the picker the moment it opens. Rejected by requirement three, in the same sentence that asks for
/// it. <b>ModBuild 382 did not quietly adopt this.</b> Its lane fallback is not "right of the rows";
/// it is "one gap right of the nearest painted edge inside the widest lane of the frame that draws
/// no ink across the control's row", it runs ONLY when there is no room to the right of the union at
/// all — a state the shipped build answered by drawing the button on top of the cards — and its
/// blockers are measured with their uGUI clippers applied, so it cannot seat the control under a
/// picker that is actually painting there. In the good state, where the union leaves room, not one
/// pixel of the ModBuild 241 solve changes.</item>
/// <item><i>Hard-code the ~565 px offset the picker adds.</i> That is coding against today's numbers.
/// Another lane is changing <c>PanelInkBounds</c> this same round so that mouseover and tooltip
/// subtrees stop counting as ink, and every union quoted above will move under this file. The seat is
/// therefore expressed as a PROPERTY — right of whatever the window is drawing, centred on whatever
/// the character rows are — and never as a distance.</item>
/// <item><i>Add a second re-solve loop keyed on the picker's own open/close event.</i> There is
/// already a cadence, an epsilon and an ink measurement here, and a second timer on the same subject
/// is how two writers start disagreeing. The signature edge feeds the EXISTING refresh instead of
/// running beside it.</item>
/// </list></para>
/// </summary>
internal static class LoadoutConfirmPark
{
    private const string Scope = "WorldUI";

    /// <summary>Gap between the Character-UI's painted RIGHT edge and the confirm's painted LEFT edge,
    /// in the host window's own authored uGUI px. (Through ModBuild 239 this was the gap under the
    /// window's painted BOTTOM edge; the axis changed, the number and the argument did not.)
    /// Deliberately a constant and not a config dial: the user asked for the button to be beside the
    /// characters, not for a spacing control, and every new dial is a surface somebody has to tune. It
    /// is the same 24 px <c>StoryComposite.ImageGapPx</c> uses between the quest picture and the
    /// dialog, for the same reason — on a committed panel that is roughly 17-25 mm, visibly one gap and
    /// never a separation.</summary>
    private const float ConfirmGapPx = ChromeParkTuning.ChromeGapPx;

    /// <summary>Has the frame clamp been reported as biting this session? One line, not one per
    /// tick: the solve runs on every reseat and a clamped state persists for as long as the picker
    /// is open, so a per-tick line would be the loudest thing in the log about a control the player
    /// can still press.</summary>
    private static bool _clampBiteLogged;

    /// <summary>ModBuild 382 — the three outcomes of the horizontal solve, in the order they cost
    /// the player anything. <see cref="Beside"/> is the design: there was room to the right of
    /// everything the window paints and the control took it. <see cref="Lane"/> is the escape added
    /// in this build: there was NO room to the right, so the seat was re-solved into a vertical lane
    /// of the frame that draws no ink across the control's own row. <see cref="Over"/> is the
    /// ModBuild 239 last resort, now reached only when the frame has no such lane: the control is
    /// clamped back over the content, ugly but reachable.</summary>
    private enum SeatVerdict
    {
        Beside = 0,
        Lane = 1,
        Over = 2,
    }

    /// <summary>How many solves ended in each <see cref="SeatVerdict"/> this session. These are the
    /// counts the seat-lane line carries so that a single printed line cannot be read as a stopped
    /// tick: the line is change-gated, so its numbers are the only evidence of how often the state
    /// it names actually held.</summary>
    private static int _seatBesideCount;
    private static int _seatLaneCount;
    private static int _seatOverCount;

    /// <summary>Change gate for the seat-lane line: the verdict kind and a coarse 64 px bucket of
    /// the seat's left edge. Kind alone would freeze the counts above at whatever they were on the
    /// first line of that kind; the seat bucket alone would re-print on every reflow, which is what
    /// the re-seat line is already for.</summary>
    private static int _seatVerdictStamp = int.MinValue;

    // THREE CONSTANTS THAT USED TO BE RESTATED HERE, each with a comment naming the file it was
    // copied from and the words "for its reason" — which is a shared meaning written twice, not two
    // meanings (ModBuild 439, survey row R35). They now ALIAS ChromeParkTuning, where the argument
    // for each is written once. No value moved. What is deliberately NOT shared is this class's
    // bounds sweep: it is the only one that clips to uGUI masks and the only one with an escape lane
    // when the union fills the frame, and its own comment refuses to extend ClipToMasks to the
    // others' unions.

    /// <summary>Below this the re-place is skipped, in authored uGUI px.</summary>
    private const float OffsetEpsilonPx = ChromeParkTuning.OffsetEpsilonPx;

    /// <summary>How often the measured zero is re-solved, seconds. Here the sweep walks every
    /// Graphic under the character screen and what it measures changes when the roster or a sub-panel
    /// does — the shared cadence's argument, on this window's numbers.</summary>
    private const float AnchorRefreshIntervalSeconds = ChromeParkTuning.AnchorRefreshIntervalSeconds;

    /// <summary>How long the park claim may stand while the online control is on screen but not yet
    /// parked.</summary>
    private const float ClaimGraceSeconds = ChromeParkTuning.ClaimGraceSeconds;

    // ---- park state ----------------------------------------------------------------------------

    private static GameObject? _parked;
    private static bool _parkedIsReadyToggle;
    private static UIWindow? _host;

    /// <summary>Has <see cref="ApplyPose"/> written the control's seat for the current parking?
    /// Handed to <see cref="MapQuestReadyRoster"/>, which hangs the ready row off the control's
    /// painted top edge and must not read a seat that has not been solved yet — it would place the
    /// icons at the control's PREVIOUS position for one tick.</summary>
    private static bool _posed;
    private static GameObject? _homeOwner;
    private static bool _homeRecorded;
    private static Transform? _home;
    private static int _homeIndex;
    private static Vector2 _homeAnchorMin;
    private static Vector2 _homeAnchorMax;
    private static Vector2 _homePivot;
    private static Vector2 _homeAnchoredPos;
    private static Quaternion _homeRotation = Quaternion.identity;
    private static Vector3 _homeScale = Vector3.one;
    private static LayoutElement? _addedIgnore;
    private static bool _standDown;
    private static bool _standDownWarned;
    private static bool _parkLogged;

    // ---- the layer record (ModBuild 238) ---------------------------------------------------------

    /// <summary>Every transform of the moved control whose layer this class overwrote, and the value
    /// the GAME had there. <c>StoryComposite.LayerTx</c>/<c>LayerWas</c>, for its reason and with its
    /// restore guard.</summary>
    private static readonly List<Transform> LayerTx = new(16);
    private static readonly List<int> LayerWas = new(16);
    private static int _layerWritten = -1;
    private static int _layerSkipped;

    // ---- the measured zero ---------------------------------------------------------------------

    private static bool _anchorValid;
    private static Vector2 _anchorPivot;
    private static float _anchorNextRefreshAt;
    private static string _anchorWhy = "never measured";
    private static Rect _anchorContent;
    private static int _anchorContentCount;
    private static Rect _anchorControl;
    private static int _anchorControlCount;

    /// <summary>The union of the game's own character rows, host-local px, and how many rows and
    /// graphics went into it. This is the block the seat is centred against; see
    /// <see cref="TryCharacterBlock"/>.</summary>
    private static bool _anchorBlockValid;
    private static Rect _anchorBlock;
    private static int _anchorBlockSlots;
    private static int _anchorBlockGraphics;

    /// <summary>Where the last solve asked the control's INK to land — the seat, not the pivot. Kept
    /// so the re-seat line and the falsifier can print the requested rectangle beside the one the
    /// control actually occupies, which is the only way a reader can tell a bad solve from a bad
    /// write.</summary>
    private static Rect _anchorSeat;

    /// <summary>ModBuild 241 — the reflow edge. <c>PanelInkBounds.ActiveSetSignature</c> of the host
    /// panel as of the last solve. Its part two is the game's <c>ActiveDisplay</c> plus which sub-view
    /// roots are open, so it changes on the tick the quest information appears or goes away; a change
    /// pulls the next <see cref="RefreshAnchor"/> forward to THIS tick instead of waiting out
    /// <see cref="AnchorRefreshIntervalSeconds"/>. <c>int.MinValue</c> is the "never sampled" value and
    /// is compared for equality only — never subtracted [[sentinel-overflow-and-silent-scans]].</summary>
    private static int _anchorSignature = int.MinValue;

    /// <summary>The re-seat line's change gate and budget. See <see cref="ReportReseat"/>.</summary>
    private static Rect _reseatFrom;
    private static bool _reseatFromValid;
    private static int _reseatReports;

    /// <summary>Cap on <c>LOADOUT CONFIRM RESEATED</c> lines per loadout screen, and the move that
    /// earns one. <see cref="MaxReachReports"/>'s number and argument; the distance is one
    /// <see cref="ConfirmGapPx"/>, i.e. the smallest move that can change whether two things touch,
    /// so settling jitter cannot spend the budget.</summary>
    private const int MaxReseatReports = 6;

    // ---- the claim (online only) ---------------------------------------------------------------

    private static float _visibleUnparkedSince = -1f;
    private static string _claimWhy = "nothing is claimed";
    private static bool _claimStanding;

    // ---- the falsifier's change gate ------------------------------------------------------------

    private static string _reachVerdict = string.Empty;
    private static int _reachReports;

    /// <summary>Cap on <c>LOADOUT CONFIRM</c> lines per loadout screen. The verdict is change-gated
    /// as well, so this only bites if the state genuinely oscillates — in which case six lines are
    /// already the diagnosis and a seventh is noise. Same number and same argument as
    /// <c>StoryComposite.MaxOneWindowReports</c>.</summary>
    private const int MaxReachReports = 6;

    private static UIWindow? _reachHost;

    // ---- the ModBuild 238 falsifier's change gate -------------------------------------------------

    private static string _enterVerdict = string.Empty;
    private static int _enterReports;
    private static float _enterNextAt;

    /// <summary><see cref="MaxReachReports"/>'s number and argument, kept separate so the two lines
    /// cannot silence each other.</summary>
    private const int MaxEnterReports = 6;

    // ---- the public reads ----------------------------------------------------------------------

    /// <summary>
    /// HAS THE GAME DECIDED THAT A CONTINUE CONTROL SHOULD BE SHOWABLE RIGHT NOW? A PURE read, safe
    /// from anywhere and as often as needed — <c>StoryComposite</c> asks it once per tick as one of
    /// the terminators of its loadout claim.
    ///
    /// <para>BOTH TERMS ARE THE GAME'S OWN AND NEITHER IS WRITTEN BY THIS MOD, which is the whole
    /// requirement: the fault this answers was a claim whose every "is it on screen" term was a value
    /// the mod itself set.</para>
    /// <list type="number">
    /// <item><c>UILoadoutManager.CanShowConfirmationButton()</c> — the game's own predicate, called
    /// rather than re-derived. It is false while <c>NewPartyDisplayUI.ActiveDisplay</c> is an open
    /// sub-panel other than <c>NONE</c> or <c>BATTLE_GOALS</c> (:111-119).</item>
    /// <item>The control's own switch: offline <c>confirmationButton.gameObject.activeSelf</c>, which
    /// is exactly what <c>SetActiveSinglePlayerLongConfirmButton</c> writes (:94); online
    /// <c>UIReadyToggle.IsVisible</c>, which is literally <c>window.IsOpen</c>
    /// (UIReadyToggle.cs:138) and therefore the game's <c>ToggleVisibility</c> decision. Parking
    /// changes NEITHER of those two fields, so the answer cannot be contaminated by the park.</item>
    /// </list>
    /// </summary>
    internal static bool GameWantsConfirmShown()
    {
        try
        {
            UILoadoutManager? lm = Manager();
            if (lm == null || !lm.IsOpen || !lm.CanShowConfirmationButton())
                return false;
            GameObject? control = ResolveControl(lm, out bool readyToggle);
            if (control == null)
                return false;
            return readyToggle ? ReadyToggleVisible() : control.activeSelf;
        }
        catch (System.Exception)
        {
            // A read is never worth a throw reaching the modal tick, and the safe direction here is
            // "the game is not asking for a button": it can only make a suppressor lapse LATER, and
            // the deadlock warning below still fires off the same measurement.
            return false;
        }
    }

    /// <summary>
    /// CAN THE PLAYER REACH THE CONTINUE CONTROL SOMEWHERE THAT IS NOT THE LOADOUT SCREEN? The
    /// question — and the ONLY question — <c>StoryComposite</c> may ask before it withholds the
    /// loadout window's float.
    ///
    /// <para><b>THE EXCLUSION IS THE WHOLE POINT AND IT IS NOT A DETAIL.</b>
    /// <see cref="ConfirmReachable"/> answers TRUE when the control is drawing inside ANY floated
    /// window, and the loadout screen is one of them. A suppressor that gated itself on that would
    /// oscillate once per tick — withhold, control unreachable, release, control reachable, withhold —
    /// which is a claim measuring its own effect [[a-claim-must-not-measure-itself]]. So this asks the
    /// question with the subject removed: is the control drawing inside a live floated panel that is
    /// NOT this window?</para>
    ///
    /// <para>TRUE when the game is not asking for a control at all, because then there is nothing to
    /// be unreachable — the battle-goal phase is exactly that state, and it is the phase in which the
    /// user wants the background window gone. FALSE is therefore precisely "withholding this window
    /// would strand the player", and nothing weaker. Never throws; the fail direction is FALSE, i.e.
    /// the window keeps its float.</para>
    /// </summary>
    internal static bool ContinueReachableOffTheLoadout(UIWindow? loadout)
    {
        try
        {
            UILoadoutManager? lm = Manager();
            if (lm == null || !GameWantsConfirmShown())
                return true;
            GameObject? control = ResolveControl(lm, out _);
            if (control == null || !control.activeInHierarchy)
                return false;
            UIWindow? drawnIn = FloatedAncestorWindow(control.transform);
            if (drawnIn == null || (loadout != null && ReferenceEquals(drawnIn, loadout)))
                return false;
            ConvertedPanel? panel = ModalFallback.PanelFor(drawnIn);
            if (panel == null || control.transform is not RectTransform rect
                || drawnIn.transform is not RectTransform win)
                return false;
            TryPaintedBounds(rect, win, panel, exclude: null, out _, out int drawn);
            return drawn > 0;
        }
        catch (System.Exception)
        {
            // A suppression must never be granted by a measurement that failed.
            return false;
        }
    }

    /// <summary>The map room's Character-UI window, or null. Exposed for
    /// <c>StoryComposite.ComposedHost</c>, which has to be able to name the ONE window the player is
    /// meant to be watching once the backdrop has been withdrawn — a falsifier that kept naming the
    /// withdrawn window would report NOT ACHIEVED for the state this round exists to produce.</summary>
    internal static UIWindow? CharacterUI => CharacterWindow();

    /// <summary>
    /// THE CONTROL THIS CLASS HAS PARKED INTO A GAME WINDOW, or null when it has parked nothing.
    ///
    /// <para>Published for ONE consumer and one question: <c>CanvasConversion</c>'s fixed fit measures
    /// "the character column" as every graphic under the window root that no serialized sub-view owns,
    /// and derives the COLUMN SEAM — the x every sub-view the game opens is seated on — from that
    /// union's width. The control below is the game's own <c>UIReadyToggle</c>, moved here by this
    /// class and NOT renamed, so the fit's only ownership test (a <c>GloomhavenVR.</c> name prefix)
    /// cannot see it. Counted, it makes the column measure 802 px instead of 328 and pushes the seam
    /// 474 px right, which is where the battle-goal cards then get seated
    /// [[a-gate-narrower-than-its-choke-point]].</para>
    ///
    /// <para>THE ARGUMENT IS THE SAME ONE THE READY ROW ALREADY WON: this control is SEATED AGAINST
    /// the window's own content, so measuring it AS that content is a term standing on its own
    /// output. It is still fully DRAWN and fully interactive — the hit rect, the grab bar and the
    /// supersample capture all still cover it, and none of them is touched here.</para>
    /// </summary>
    internal static Transform? HeldControl =>
        _parked != null && _host != null ? _parked.transform : null;

    /// <summary>Where the confirm is being drawn right now, for another subsystem's log line. Never
    /// null.</summary>
    internal static string Where =>
        _parked != null && _host != null
            ? $"'{_parked.name}' is parked inside the floated Character-UI '{_host.name}'"
            : "the confirm is not parked anywhere by this class";

    // ---- the per-tick step ----------------------------------------------------------------------

    /// <summary>
    /// Called once per <c>ModalFallback.Tick</c>, from <c>StoryComposite.Tick</c> — i.e. from the
    /// first line of <c>TickWindowLiveness</c>, BEFORE the release loop. That call site is the same
    /// one and load-bearing for the same reason: the release loop destroys the host GameObject this
    /// class parks into, and a subtree still parked under a destroyed host cannot be handed back.
    ///
    /// <para>EVERYTHING HERE IS LEVEL-TRIGGERED. There is no latch, no session verdict and no
    /// suppression list: every tick re-asks which object IS the confirm, whether the Character-UI is
    /// a live floated panel, and whether the object is still ours. A steady state costs two reference
    /// compares and — four times a second — two Graphic sweeps.</para>
    /// </summary>
    internal static void Tick()
    {
        try
        {
            TickCore();
            OfferReadyRow();
        }
        catch (System.Exception e)
        {
            // ONE guard around the whole step, and the fail direction is to hand the control back:
            // a parker that throws must not leave the game's own button inside a window that is
            // about to be released. [[stack-traces-restored]] — the type is named so the log is
            // attributable.
            VRLog.Warn(Scope, $"LOADOUT CONFIRM: the parker threw ({e.GetType().Name}) — handing the "
                              + "control back to its own home and standing down for this loadout. "
                              + "CONSEQUENCE: the continue button is wherever the game draws it, "
                              + "which offline is the loadout screen — see LOADOUT CONFIRM "
                              + "REACHABLE for whether that is reachable in this room.");
            Unpark("the parker threw");
            DropClaim("the parker threw");
            _standDown = true;
            OfferReadyRow();
        }
    }

    /// <summary>
    /// HAND THE READY ROW'S OWNER THIS SITE'S LIVE STATE — the icon row of "wer schon akzeptiert hat"
    /// beside the 'Verlies betreten' button.
    ///
    /// <para><b>USER REQUEST, 2026-09-05, verbatim:</b> <i>"Beim 'Verlies betreten' Button will ich
    /// auch die Symbole sehen vom Spiel, wer schon akzeptiert hat und wer nicht - wie zu Beginn auch
    /// bei der Questauswahl."</i></para>
    ///
    /// <para>NOTHING IS BUILT HERE AND NOTHING IS TRACKED HERE. The game already populates its own
    /// <c>UIReadyTrackerBar</c> for this exact phase — <c>MPConfirmEnterScenario</c> ends with
    /// <c>ShowLoadoutMultiplayer()</c>, which is <c>ShowCharactersTrackers()</c>
    /// (decompiled UILoadoutManager.cs:516, UIMapMultiplayerController.cs:95-99) — and keeps the cells
    /// current through <c>UpdateReadyPlayer</c> (UILoadoutManager.cs:499). It is drawn on the flat HUD,
    /// which this room does not draw. <see cref="MapQuestReadyRoster"/> owns the moving and the
    /// placing for both confirmations; this method only says what this site has to offer. See that
    /// class's section 4 for the full citation chain.</para>
    ///
    /// <para>THE CONTROL IS OFFERED ONLY WHILE IT IS THE READY TOGGLE. Offline the confirm is
    /// <c>UILoadoutManager.confirmationButton</c>, the game never populates the bar at all for this
    /// phase, and an icon row with nothing in it would be a decoration asserting a readiness nobody
    /// is tracking. The BUTTON is never withheld with it — this method cannot affect the control's
    /// own seat, which <see cref="ApplyPose"/> solved before it ran.</para>
    ///
    /// <para>LEVEL-TRIGGERED AND UNCONDITIONAL: it runs on every exit path of the tick, including the
    /// guarded throw, so a site that stands down stops offering within a frame rather than leaving a
    /// stale claim on the shared bar.</para>
    /// </summary>
    /// <remarks>
    /// GUARDED SEPARATELY FROM THE PARKER, and that is the whole reason this is a method and not a
    /// line. It runs INSIDE <see cref="Tick"/>'s try, whose catch unparks the control and stands the
    /// parking down — so without this guard a throw inside the ROW's placement would take the
    /// 'Verlies betreten' BUTTON away from the player. A decoration must never be able to do that to
    /// a control [[a-write-inside-a-logger]]. The row simply does not update this tick, and the
    /// roster's own report line is the thing that goes quiet.
    /// </remarks>
    private static void OfferReadyRow()
    {
        try
        {
            MapQuestReadyRoster.Tick(ReadyRosterSite.EnterDungeon,
                                     _host,
                                     _parkedIsReadyToggle ? _parked : null,
                                     _posed);
        }
        catch (System.Exception e)
        {
            if (_rowThrewLogged)
                return;
            _rowThrewLogged = true;
            VRLog.Warn(Scope, "LOADOUT CONFIRM: the ready-icon row threw while being offered "
                              + $"({e.GetType().Name}: {e.Message}). CONSEQUENCE: the 'Verlies "
                              + "betreten' button and its parking are UNAFFECTED — the row is a "
                              + "decoration and it is guarded away from the control on purpose. The "
                              + "player loses the 'wer hat schon akzeptiert' icons beside the button "
                              + "and nothing else. One line per session.");
        }
    }

    /// <summary>One Warn per session for the guard above — a throw that repeats every frame must not
    /// become the log.</summary>
    private static bool _rowThrewLogged;

    private static void TickCore()
    {
        UILoadoutManager? lm = Manager();
        UIWindow? loadout = lm != null ? lm.GetComponent<UIWindow>() : null;

        if (!MapRoomDriver.Active || lm == null || !lm.IsOpen)
        {
            Unpark(!MapRoomDriver.Active ? "the 3D map room stood down"
                                         : "the loadout screen is not open");
            DropClaim(!MapRoomDriver.Active ? "the 3D map room stood down"
                                            : "the loadout screen is not open");
            // Leaving the loadout clears a stand-down: whatever went wrong was about THIS quest's
            // objects, and the next EnterLoadout rebuilds all of them.
            _standDown = false;
            _standDownWarned = false;
            ResetReach();
            return;
        }
        if (_standDown)
        {
            ReportReach(lm, loadout);
            return;
        }

        GameObject? control = ResolveControl(lm, out bool readyToggle);

        // ==========================================================================================
        // ModBuild 238 — THE ModBuild 236 STAND-DOWN IS GONE, AND THE USER'S OWN WORDS ARE WHY.
        // ==========================================================================================
        //
        // 236 short-circuited here with "the loadout screen is floating with the confirm button on
        // it, so there is nothing for this class to fix", because the round before that the user had
        // asked for the button to stay on the window the story was told in. He has since changed his
        // mind about the phase AFTER the story, verbatim (2026-08-23):
        //
        //   "Ich möchte das letzte lokale Storyfenster mit nur dem Hintergrund doch nicht haben.
        //    Nachdem die Story erzählt wurde soll die Character-UI für die persönlichen Quests
        //    spawnen und das Storyfenster verschwinden. Hat man für all seine zugewiesenen Charactere
        //    die Quest ausgewählt soll der Button der jetzt auf dem Fenster mit nur dem Bild zu sehen
        //    ist 'Verlies betreten' am unteren Rand der character-UI zu sehen sein, wo man ihn
        //    betätigen kann. So braucht man nicht die Übersicht über zwei Fenster behalten."
        //
        // So the condition does not merely invert, it DISAPPEARS: there is no state left in which the
        // right answer is "leave the confirm on the loadout screen". The park is now unconditional on
        // the one term that has always gated it and that is not a value this class writes — IS THE
        // CHARACTER-UI A LIVE FLOATED PANEL — and that term is checked a few lines below.
        //
        // WHY THAT IS THE STABLE LEVEL AND "the loadout screen is being withheld" IS NOT. The
        // withholding is StoryComposite's, and StoryComposite gates it on THIS class having parked
        // the control (see StoryComposite.TickLoadoutBackdrop). Asking about it here would be a claim
        // measuring itself [[a-claim-must-not-measure-itself]] and would flap once per tick. The
        // Character-UI's float is a fact about a window this class never touches.
        //
        // NOTHING ABOUT THE STORY PHASE CHANGES. During the intro the game has not switched the
        // confirm on and the Character-UI is not floated (the ModBuild 237 log releases it at :2847,
        // because MapChoreographer.OnMoveClick hides it at :1455, and re-floats it at :3391 AFTER
        // FinishIntroduction), so this class stands down on its own for the whole story without
        // needing a rule that says so.

        UIWindow? host = CharacterWindow();
        bool hostFloated = host != null && FloatedByMod(host);

        if (control == null || host == null || !hostFloated)
        {
            Unpark(control == null ? "the game has no confirm control for this loadout"
                   : host == null ? "the Character-UI window does not exist"
                                  : "the Character-UI is not floated, so there is nothing to be part of");
            TickClaim(control, readyToggle, parked: false);
            ReportReach(lm, loadout);
            return;
        }

        // THE CONTROL IS PART OF THE PARKING IDENTITY, NOT ONLY THE HOST (MapTravelConfirm's ModBuild
        // 226 rule): going online swaps the single-player button for the ready toggle and going
        // offline swaps it back, and either way the object we are HOLDING must be handed home before
        // the other one is taken — otherwise the first stays parented into a window it is no longer
        // the confirm for.
        if (!ReferenceEquals(_host, host) || !ReferenceEquals(_parked, control))
        {
            Unpark(!ReferenceEquals(_host, host)
                ? "a different Character-UI window took over"
                : "the game switched which object IS the confirm (online ⇄ offline)");
            if (!Park(host, control, readyToggle))
            {
                TickClaim(control, readyToggle, parked: false);
                ReportReach(lm, loadout);
                return;
            }
        }

        // OWNERSHIP, EVERY TICK. The game re-parents its own UI freely and re-taking an object from
        // wherever it has since put it is the write war this project has already lost once.
        if (control.transform.parent != host.transform)
        {
            VRLog.Warn(Scope, $"LOADOUT CONFIRM: '{control.name}' is no longer parented under the "
                              + $"floated Character-UI '{host.name}' — the game moved it. Handing it "
                              + "back (home transform restored verbatim) and standing the parking down "
                              + "for this loadout. CONSEQUENCE: the continue button is wherever the "
                              + "game draws it; offline that is the loadout screen, so if THAT is also "
                              + "being withheld the player has no way forward — the LOADOUT CONFIRM "
                              + "REACHABLE line beside this one says which.");
            Unpark("the game re-parented the confirm");
            TickClaim(control, readyToggle, parked: false);
            _standDown = true;
            ReportReach(lm, loadout);
            return;
        }

        // THE LAYER, EVERY TICK, AND ONLY WHEN IT HAS DRIFTED. See WriteLayers for the second writer
        // this repairs after; the write is change-gated on the HOST ROOT'S OWN live layer, which is
        // exactly the value CanvasConversion.ApplyModLayer and PanelSupersample.ApplyCaptureLayer
        // want the subtree to have, so this cannot start a write war [[dont-win-a-write-war]].
        int hostLayer = host.gameObject.layer;
        if (LayerDrifted(control, hostLayer))
            WriteLayers(hostLayer, control.transform);

        ApplyPose(host, control);
        TickClaim(control, readyToggle, parked: true);
        ReportReach(lm, loadout);
    }

    // ---- which object IS the confirm -------------------------------------------------------------

    private static UILoadoutManager? Manager()
    {
        if (!Singleton<UILoadoutManager>.IsInitialized)
            return null;
        UILoadoutManager lm = Singleton<UILoadoutManager>.Instance;
        return lm != null ? lm : null;
    }

    /// <summary>
    /// WHICH OBJECT IS THE PRE-SCENARIO CONFIRM RIGHT NOW, read off the one branch the game itself
    /// takes: <c>UILoadoutManager.SetActiveConfirmationButton</c> switches on <c>FFSNetwork.IsOnline</c>
    /// (:100-108) and so does this.
    ///
    /// <para>ONLINE it is the multiplayer ready toggle — and the toggle is a SINGLETON reused for
    /// quests, city events, rewards, retirement and town records, so this refuses to touch it unless
    /// its own <c>readyUpToggleState</c> is NOT <c>Quests</c>. That is exact rather than merely
    /// likely: <c>MPConfirmEnterScenario</c> calls <c>Initialize</c> without the
    /// <c>readyUpToggleState</c> argument, so the loadout ready-up leaves it at its default
    /// <c>NotSet</c> (UIReadyToggle.cs:452,467), while <c>MapRoom/MapQuestReadyUp</c> claims the
    /// toggle only while it reads <c>Quests</c>. The two parkers are therefore mutually exclusive BY
    /// THE GAME'S OWN FIELD and never both write <c>ReadyToggleParkClaim</c> —
    /// [[a-remedy-knows-one-writer]].</para>
    ///
    /// <para>OFFLINE it is <c>UILoadoutManager.confirmationButton</c>, the object
    /// <c>SetActiveSinglePlayerLongConfirmButton</c> calls <c>SetActive</c> on in BOTH of its own
    /// branches (:88-95), so it is the right object with or without a gamepad. (The gamepad-only
    /// <c>LongConfirmHandler</c> button is a second control this class does not move; VR runs with
    /// <c>InputManager.GamePadInUse</c> false and reaches the button by laser or poke.)</para>
    /// </summary>
    private static GameObject? ResolveControl(UILoadoutManager lm, out bool readyToggle)
    {
        readyToggle = false;
        if (FFSNetwork.IsOnline)
        {
            if (!Singleton<UIReadyToggle>.IsInitialized)
                return null;
            UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
            if (toggle == null || toggle.readyUpToggleState == EReadyUpToggleStates.Quests)
                return null;
            readyToggle = true;
            return toggle.gameObject;
        }
        Button? button = lm.confirmationButton;
        return button != null ? button.gameObject : null;
    }

    private static bool ReadyToggleVisible()
    {
        if (!Singleton<UIReadyToggle>.IsInitialized)
            return false;
        UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
        return toggle != null && toggle.IsVisible;
    }

    /// <summary>
    /// <b>THE WINDOW THE MOD IS FLOATING WHOSE ID IS <c>PartyPanel</c> — the map room's Character-UI.</b>
    ///
    /// <para><b>WHAT ModBuild 238 RESOLVED INSTEAD, AND WHY NOTHING IN THIS FILE RAN.</b> It asked
    /// <c>NewPartyDisplayUI.PartyDisplay.GetComponent&lt;UIWindow&gt;()</c> and defended that with
    /// "the component and the window are one GameObject by construction". A <c>GetComponent</c> IS an
    /// IS-A question — but it was being asked of the WRONG GameObject, and the mod's own instrument
    /// had already written the disproof down. <c>FIXED FIT GATE 'GloomhavenVR.Panel_Modal_New Party
    /// display'</c> in the ModBuild 238 log:</para>
    /// <list type="bullet">
    /// <item><c>conversion target 'New Party display'</c>; <c>target carries UIWindow: YES (ID
    /// PartyPanel)</c> — that is the window this mod floats, and <c>LOADOUT BACKGROUND WITHDRAWN</c>
    /// on the same session names it the same way: <c>'New Party display' ID PartyPanel</c>.</item>
    /// <item><c>target carries NewPartyDisplayUI: NO; the live NewPartyDisplayUI sits on a DESCENDANT
    /// of the target, 1 level(s) DOWN ('Party Display UI ')</c>.</item>
    /// <item><c>identity 1 — the live party display drives this window: NO (the live party display
    /// drives a DIFFERENT UIWindow instance)</c>.</item>
    /// </list>
    /// <para>There are TWO <c>UIWindow</c> components in that hierarchy. ModBuild 238 returned the
    /// descendant's, so <c>ENTER DUNGEON ON THE CHARACTER UI</c> reported <c>THE HOST: 'Party Display
    /// UI ' floated=False, live panel=False, rect 0..300 x -540..540</c>, <c>LOADOUT CONFIRM PARKED</c>
    /// never appeared once in the whole session, and — because <c>StoryComposite</c> refuses to
    /// withdraw the loadout backdrop while the control is reachable NOWHERE else — the user got exactly
    /// what he reported: "wieder ein neues Fenster mit einem Hintergrundbild und dem 'Verlies betreten'
    /// button". One wrong term, every downstream symptom.</para>
    ///
    /// <para><b>THE IDENTITY USED NOW.</b> The window's own serialized <c>ID</c>, over the set of
    /// windows <c>ModalFallback</c> is actually floating. Both halves matter: the ID is what the rest
    /// of the mod identifies this window by (<c>CanvasConversion.3.Fit.cs</c>'s identity 2,
    /// <c>ModalFallback.1.Core.cs</c>'s permanent-window list), and the float set is what makes the
    /// answer a window that HAS a host rect and a grab frame to park onto. No component transform, no
    /// <c>GetComponentInParent</c>, no <c>GetComponentInChildren</c> — none of those can distinguish
    /// two <c>UIWindow</c>s one level apart, which is what this class needed and did not have
    /// ([[containment-is-not-identity]], now for the third time in this project).</para>
    ///
    /// <para>Returning null while the window is not floated is the CORRECT answer, not a degraded one:
    /// the caller's next test is <c>FloatedByMod</c> and its Unpark reason for a null host already
    /// reads "there is nothing to be part of".</para>
    /// </summary>
    private static UIWindow? CharacterWindow()
    {
        FloatScratch.Clear();
        try
        {
            ModalFallback.CollectFloatedWindows(FloatScratch, null);
            for (int i = 0; i < FloatScratch.Count; i++)
            {
                UIWindow w = FloatScratch[i];
                if (w != null && w.ID == UIWindowID.PartyPanel)
                    return w;
            }
        }
        catch (System.Exception)
        {
            // A reader that throws must not stand the whole tick down; null reads as "not floated".
        }
        finally
        {
            FloatScratch.Clear();
        }
        return null;
    }

    /// <summary>Scratch for <see cref="CharacterWindow"/>'s float-set walk — the set is never longer
    /// than a handful, and reusing one list keeps a per-tick reader allocation-free.</summary>
    private static readonly List<UIWindow> FloatScratch = new(8);

    /// <summary>
    /// <b>THE IDENTITY PROOF, ON THE FALSIFIER LINE — that the control lands on the SAME window the
    /// battle goals were chosen in, by reference and not by name.</b>
    ///
    /// <para>Three terms, none of them a name comparison. (1) The window's own serialized
    /// <c>ID</c> — the identity the rest of the mod uses for this window. (2) Whether the object the
    /// control is parented to IS the <c>ConvertedPanel.Target</c>, i.e. the very transform
    /// <c>CanvasConversion</c> converted: reference equality, so a descendant one level down (which is
    /// what ModBuild 238 parked onto) reads FALSE here instead of looking convincing. (3) Whether the
    /// live <c>NewPartyDisplayUI</c>'s battle-goal picker sits UNDER that same target — the picker is
    /// the thing the user names in his request ("bei der Character-UI bei der ich auch die auswahl der
    /// persönlichen Quest gemacht habe"), and containment under the shared root is exactly what "the
    /// same window" means for it, because the picker's own root is NOT a direct child.</para>
    /// </summary>
    private static string HostIdentity(UIWindow? host, ConvertedPanel? panel)
    {
        if (host == null)
            return "IDENTITY: no Character-UI window resolved, so there is nothing to prove.";
        Transform? target = panel?.Target;
        bool targetIsHost = target != null && ReferenceEquals(target, host.transform);
        string picker;
        try
        {
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            Component? goals = display != null ? display.BattleGoalWindow : null;
            picker = goals == null
                ? "the game exposes no battle-goal picker right now"
                : target == null
                    ? "there is no conversion target to compare it against"
                    : $"the battle-goal picker '{goals.name}' is under that same target="
                      + $"{IsUnder(goals.transform, target)}";
        }
        catch (System.Exception)
        {
            picker = "the battle-goal picker could not be read";
        }
        return $"IDENTITY, BY REFERENCE AND NOT BY NAME: window ID {host.ID} (the map room's "
               + $"Character-UI is {UIWindowID.PartyPanel}); the control's parent IS this panel's own "
               + $"conversion target '{(target != null ? target.name : "<none>")}'={targetIsHost}; "
               + $"{picker}. ModBuild 238 resolved a DESCENDANT of the target here — the GameObject the "
               + "NewPartyDisplayUI component sits on, one level down — and every term of this line "
               + "then read wrong from that one substitution.";
    }

    /// <summary>Is <paramref name="t"/> <paramref name="root"/> or below it? A plain parent walk, so
    /// it answers containment and nothing else — it is never used to answer IS-A.</summary>
    private static bool IsUnder(Transform t, Transform root)
    {
        Transform? p = t;
        for (int guard = 0; p != null && guard < 64; guard++, p = p.parent)
        {
            if (ReferenceEquals(p, root))
                return true;
        }
        return false;
    }

    /// <summary>Is this window one the mod is floating right now? Asked through the only float-set
    /// question <c>ModalFallback</c> exposes — a window is floated exactly when excluding it from the
    /// count changes the count.</summary>
    private static bool FloatedByMod(UIWindow? window) =>
        window != null
        && ModalFallback.CountFloatsOtherThan(window) < ModalFallback.CountFloatsOtherThan(null);

    // ---- parking ---------------------------------------------------------------------------------

    /// <summary>
    /// Move the control into the Character-UI, once. The <c>MapTravelConfirm</c> sequence verbatim:
    /// record the home for THIS object, add the layout opt-out BEFORE the reparent so the
    /// destination's layout never rebuilds with this rect in its children, then parent, anchor, pivot,
    /// rotation, scale and pose.
    /// </summary>
    private static bool Park(UIWindow host, GameObject control, bool readyToggle)
    {
        if (host.transform is not RectTransform win || control.transform is not RectTransform rect)
        {
            if (!_standDownWarned)
            {
                _standDownWarned = true;
                VRLog.Warn(Scope, "LOADOUT CONFIRM: cannot park the continue control — "
                                  + $"'{control.name}' or the Character-UI '{host.name}' is not a "
                                  + "RectTransform, so the anchor arithmetic this class is built on "
                                  + "does not apply. The parking stands down and nothing is moved. "
                                  + "CONSEQUENCE: the control stays where the game draws it.");
            }
            _standDown = true;
            return false;
        }

        // THE RECORD IS PER OBJECT (MapTravelConfirm's ModBuild 226 rule): with two possible controls
        // a single latch would apply the FIRST object's home to the SECOND on unpark, which is a
        // silent corruption of the game's own HUD layout rather than a visible bug.
        if (!_homeRecorded || !ReferenceEquals(_homeOwner, control))
        {
            _homeRecorded = true;
            _homeOwner = control;
            _home = rect.parent;
            _homeIndex = rect.GetSiblingIndex();
            _homeAnchorMin = rect.anchorMin;
            _homeAnchorMax = rect.anchorMax;
            _homePivot = rect.pivot;
            _homeAnchoredPos = rect.anchoredPosition;
            _homeRotation = rect.localRotation;
            _homeScale = rect.localScale;
        }

        // ignoreLayout BEFORE the move — MapTravelConfirm's discipline, for its reason: this takes the
        // anchors and the anchoredPosition out of a parent layout group's hands instead of racing it
        // for them.
        LayoutElement? le = control.GetComponent<LayoutElement>();
        if (le == null)
        {
            le = control.AddComponent<LayoutElement>();
            _addedIgnore = le;
        }
        le.ignoreLayout = true;

        _parked = control;
        _parkedIsReadyToggle = readyToggle;
        _host = host;
        _parkLogged = false;
        ResetAnchor("a new parking — the Character-UI has not been measured with this control in it yet");

        rect.SetParent(win, worldPositionStays: false);
        rect.SetAsLastSibling();
        rect.anchorMin = Vector2.up;
        rect.anchorMax = Vector2.up;
        // ITS LEFT-HAND MIDDLE IS WHAT WE PLACE (ModBuild 241; through 239 it was the top edge, and
        // that pair of numbers is the whole axis change). The arithmetic in RefreshAnchor does not
        // DEPEND on this value — it measures the pivot-to-ink offset off the live transform every
        // solve, so any pivot solves correctly — but a pivot that names the edge the placement is
        // about is what stops the next reader deriving the wrong offset from the wrong corner.
        rect.pivot = new Vector2(0f, 0.5f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        WriteLayers(host.gameObject.layer, rect);
        ApplyPose(host, control);
        return true;
    }

    /// <summary>
    /// WRITE THE HOST ROOT'S OWN LAYER OVER THE MOVED CONTROL, recording what the game had there so
    /// the hand-back can put it back. <c>StoryComposite.WriteLayers</c>, and it is NOT optional here
    /// either.
    ///
    /// <para><b>WHY ModBuild 238 HAD TO ADD IT.</b> Per-window capture cameras cull BY LAYER
    /// ([[one-shared-layer-leaks]]) and the Character-UI is ALREADY CONVERTED when the confirm
    /// arrives: 'New Party display' is floated, relayered onto the mod layer and given its own
    /// supersample capture camera long before the game switches the button on.
    /// <c>CanvasConversion.ApplyModLayer</c> and <c>PanelSupersample.ApplyCaptureLayer</c> both
    /// re-sweep for late children, but on their OWN cadence, so a subtree that arrives between two
    /// sweeps is drawn by the wrong camera or by two — the exact failure this project has shipped
    /// before, where every state test still reads "parked, active, claimed" and the player sees
    /// nothing ([[measure-the-picture-not-the-state]]). Through ModBuild 237 this class never wrote a
    /// layer at all; the defect was latent only because ModBuild 236 had stood the park down.</para>
    ///
    /// <para>A FOREIGN RENDER SUBTREE IS SKIPPED WHOLE — <c>CanvasConversion.ApplyModLayer</c>'s own
    /// rule for its own reason: a real <c>Renderer</c> under a uGUI tree is 3D owned by another camera
    /// and descending into it would take its children with it. <c>CanvasRenderer</c> is not a
    /// <c>Renderer</c>, so ordinary uGUI is unaffected.</para>
    /// </summary>
    private static void WriteLayers(int layer, Transform root)
    {
        RestoreLayers();
        _layerWritten = layer;
        _layerSkipped = 0;
        WriteLayerWalk(root, layer);
    }

    private static void WriteLayerWalk(Transform t, int layer)
    {
        if (t.GetComponent<Renderer>() != null)
        {
            _layerSkipped++;
            return;   // and NOT its children either — that is the whole point
        }
        if (t.gameObject.layer != layer)
        {
            LayerTx.Add(t);
            LayerWas.Add(t.gameObject.layer);
            t.gameObject.layer = layer;
        }
        for (int i = t.childCount - 1; i >= 0; i--)
            WriteLayerWalk(t.GetChild(i), layer);
    }

    /// <summary>Hand every layer this class wrote back to the value the GAME had there — and only
    /// where the transform is STILL on the layer we wrote. That guard is
    /// <c>PanelSupersample.RestoreLayers</c>'s, for its reason: a transform somebody else has since
    /// re-layered is no longer ours to hand back, and writing our stale value would strand it on a
    /// layer no camera renders.</summary>
    private static void RestoreLayers()
    {
        for (int i = 0; i < LayerTx.Count; i++)
        {
            Transform? t = LayerTx[i];
            if (t != null && t.gameObject.layer == _layerWritten)
                t.gameObject.layer = LayerWas[i];
        }
        LayerTx.Clear();
        LayerWas.Clear();
        _layerWritten = -1;
    }

    /// <summary>Has somebody else put the moved control back on another layer? One int compare in the
    /// steady state. It exists because of a real second writer rather than as belt-and-braces: the
    /// release of ANY panel restores that panel's own <c>Relayered</c> records with no "is it still
    /// mine" guard, and those records can name a transform this class is holding —
    /// <c>StoryComposite.LayersDrifted</c>'s argument verbatim.</summary>
    private static bool LayerDrifted(GameObject control, int layer) =>
        _layerWritten >= 0 && control.layer != layer;

    /// <summary>
    /// Put the control back. The TRANSFORM is restored only while the object is still parented under
    /// our host, because re-taking it from wherever the game has since put it is a write war
    /// ([[dont-win-a-write-war]]).
    /// </summary>
    private static void Unpark(string why)
    {
        if (_parked == null)
        {
            _host = null;
            return;
        }
        GameObject control = _parked;
        UIWindow? host = _host;
        bool wasReadyToggle = _parkedIsReadyToggle;
        _parked = null;
        _host = null;
        _parkedIsReadyToggle = false;
        _posed = false;
        _parkLogged = false;
        ResetAnchor("the control was unparked");

        try
        {
            // THE LAYER GOES BACK FIRST, while the transforms are still the ones we recorded.
            RestoreLayers();
            if (_addedIgnore != null)
            {
                Object.Destroy(_addedIgnore);
                _addedIgnore = null;
            }
            else
            {
                LayoutElement? le = control != null ? control.GetComponent<LayoutElement>() : null;
                if (le != null)
                    le.ignoreLayout = false;
            }
            if (control == null || _home == null)
                return;
            Transform t = control.transform;
            if (host == null || host.transform == null || t.parent == null
                || !t.parent.IsChildOf(host.transform))
                return;
            t.SetParent(_home, worldPositionStays: false);
            t.SetSiblingIndex(Mathf.Clamp(_homeIndex, 0, Mathf.Max(0, _home.childCount - 1)));
            if (t is RectTransform rect)
            {
                rect.anchorMin = _homeAnchorMin;
                rect.anchorMax = _homeAnchorMax;
                rect.pivot = _homePivot;
                rect.anchoredPosition = _homeAnchoredPos;
                rect.localRotation = _homeRotation;
                rect.localScale = _homeScale;
            }
            VRLog.Info(Scope, $"LOADOUT CONFIRM UNPARKED — {why}. "
                              + $"{(wasReadyToggle ? "The multiplayer ready toggle" : "The single-player continue button")} "
                              + $"'{control.name}' is back under '{_home.name}' at sibling {_homeIndex} "
                              + "with its parent, anchors, pivot, anchoredPosition, local rotation and "
                              + "local scale restored verbatim from the record taken before the first "
                              + "move, and the layout opt-out (LayoutElement.ignoreLayout) released or "
                              + "destroyed. Nothing of ours is left on the game's object. NOTHING WAS "
                              + "WRITTEN TO THE GAME at any point: no Show, no Hide, no Escape, no "
                              + "SetActive and no CanvasGroup — the control's own visibility is the "
                              + "game's (SetActiveSinglePlayerLongConfirmButton offline, "
                              + "UIReadyToggle.ToggleVisibility online) and it never changed hands.");
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"LOADOUT CONFIRM: handing the control back threw ({e.GetType().Name}) — "
                              + "it may be left under the Character-UI's root. It is the GAME's own "
                              + "object and the game re-lays it out on the next EnterLoadout.");
        }
        finally
        {
            _home = null;
            _homeRecorded = false;
            _homeOwner = null;
        }
    }

    // ---- the measured zero -----------------------------------------------------------------------

    private static void ResetAnchor(string why)
    {
        _anchorValid = false;
        _anchorNextRefreshAt = float.NegativeInfinity;
        _anchorPivot = Vector2.zero;
        _anchorContent = default;
        _anchorContentCount = 0;
        _anchorControl = default;
        _anchorControlCount = 0;
        _anchorBlockValid = false;
        _anchorBlock = default;
        _anchorBlockSlots = 0;
        _anchorBlockGraphics = 0;
        _anchorSeat = default;
        _anchorSignature = int.MinValue;
        _reseatFrom = default;
        _reseatFromValid = false;
        _anchorWhy = why;
    }

    /// <summary>
    /// WHERE THE CONFIRM GOES, AND WHY THAT PLACE IS DERIVED RATHER THAN DIALLED.
    ///
    /// <para><b>TWO ZEROS, ON TWO DIFFERENT SUBJECTS, AND THE SPLIT IS THE ModBuild 241 FIX.</b>
    /// VERTICALLY the control's painted centre is put on the centre of the CHARACTER BLOCK — the union
    /// of the game's own <c>NewPartyCharacterUI</c> rows, see <see cref="TryCharacterBlock"/>.
    /// HORIZONTALLY its painted LEFT edge is put <see cref="ConfirmGapPx"/> to the right of the RIGHT
    /// EDGE OF EVERYTHING THE WINDOW PAINTS. The two subjects are deliberately different, because the
    /// two requirements are: <i>"rechts neben den Charakteren … mittig zentriert"</i> is about the rows,
    /// and <i>"Das Auftauchen der Questinfos … muss ihn entsprechend verschieben, damit sich nichts
    /// überlagert"</i> is about everything else. Three reasons, all measurements rather than
    /// preferences:</para>
    /// <list type="number">
    /// <item><b>It cannot land on top of anything.</b> Both zeros are unions of what the window is
    /// ACTUALLY painting this tick — the roster grows and shrinks as characters are added, a sub-panel
    /// opens, a battle goal is picked — so a fixed fraction of the window would sit over the roster on
    /// one party size and off the panel on another. The unions move with it.</item>
    /// <item><b>The frame is not a usable zero on this window and the log says so in numbers.</b> The
    /// game window root is 1920x1080 px and the roster column is 330 px of it, pinned to the left at
    /// centre x=-817. Anything derived from the frame lands in transparent nothing; see the ModBuild
    /// 240 section of this class's doc for the two instruments that measured it.</item>
    /// <item><b>The host rect follows it for free.</b> <c>CanvasConversion</c>'s fit commits the
    /// drawn-content union as the host rect and the parked control is a GAME-named child of the
    /// conversion target, so a control placed one gap beyond that union is inside the fitted panel by
    /// construction — no width dial and no second layout step.</item>
    /// </list>
    ///
    /// <para><b>THE SWEEP EXCLUDES THE PARKED CONTROL, AND THAT IS WHAT MAKES IT A FIXED POINT RATHER
    /// THAN A FEEDBACK LOOP.</b> If the control's own graphics were in either union, every solve would
    /// push it one gap further right, once per cadence, forever. The control's pivot-to-ink offset is
    /// measured separately and subtracted, exactly as <c>MapTravelConfirm.MaybeRefreshAnchor</c> does,
    /// so translating the control moves its pivot and its ink by the SAME vector and re-solving after a
    /// write reproduces the same answer with zero gain.</para>
    ///
    /// <para>A FAILED REFRESH NEVER CLEARS A GOOD ANCHOR: the character screen switches whole
    /// sub-panels off, and a sweep that finds nothing must keep the last good measurement rather than
    /// throw the button to the origin.</para>
    /// </summary>
    private static void ApplyPose(UIWindow host, GameObject control)
    {
        if (host.transform is not RectTransform win || control.transform is not RectTransform rect)
            return;
        Rect frame = win.rect;
        if (Mathf.Abs(frame.height) <= 0f)
            return;

        // THE REFLOW EDGE, ModBuild 241. The cadence below is the steady state; this is what makes the
        // re-solve happen on the tick the quest information appears rather than up to
        // AnchorRefreshIntervalSeconds after it. It PULLS THE EXISTING REFRESH FORWARD and does not run
        // a second solve of its own — one subject, one writer. The signature is cheap by construction
        // (one childCount loop plus seven property reads on a singleton, PanelInkBounds' own accounting)
        // and it is compared for EQUALITY only, so the int.MinValue sentinel cannot overflow anything
        // [[sentinel-overflow-and-silent-scans]]. Parking the control changes the signature once, on the
        // tick of the park, because the control becomes a new active direct child of the conversion
        // target; after that it is constant, so this cannot become a loop.
        ConvertedPanel? signaturePanel = ModalFallback.PanelFor(host);
        if (signaturePanel != null)
        {
            // The out-count of raised mouseovers is discarded on purpose. ModBuild 241's other half
            // took hover subtrees OUT of this signature, and that exemption is exactly what this
            // call site wants: a tooltip appearing beside the roster must not re-seat the continue
            // button, only real content may. The number is a falsifier term for the grab bar's own
            // line, not something for this file to act on.
            int signature = PanelInkBounds.ActiveSetSignature(signaturePanel, out _);
            if (signature != _anchorSignature)
            {
                _anchorSignature = signature;
                _anchorNextRefreshAt = float.NegativeInfinity;
            }
        }

        float now = Time.unscaledTime;
        if (now >= _anchorNextRefreshAt)
        {
            _anchorNextRefreshAt = now + AnchorRefreshIntervalSeconds;
            RefreshAnchor(win, rect, frame);
        }
        if (!_anchorValid)
        {
            _posed = false;
            return;
        }

        // ANCHORS ARE READ, NEVER RE-WRITTEN unless they are STRETCHED. anchoredPosition means
        // nothing without the anchor it is measured from, so the number written is derived from the
        // anchor the rect is carrying this tick — a second writer that re-anchors the control cannot
        // move it by a pixel, it changes an input this line recomputes. MapTravelConfirm's rule.
        if (rect.anchorMin != rect.anchorMax)
            rect.anchorMin = rect.anchorMax = Vector2.up;
        Vector2 a = (rect.anchorMin + rect.anchorMax) * 0.5f;
        var reference = new Vector2(frame.xMin + a.x * frame.width, frame.yMin + a.y * frame.height);
        Vector2 want = _anchorPivot - reference;
        if ((rect.anchoredPosition - want).sqrMagnitude > OffsetEpsilonPx * OffsetEpsilonPx)
            rect.anchoredPosition = want;
        _posed = true;

        if (_parkLogged)
            return;
        _parkLogged = true;
        VRLog.Info(Scope, "LOADOUT CONFIRM PARKED: the game's pre-scenario continue control "
                          + $"'{control.name}' "
                          + (_parkedIsReadyToggle
                              ? "(the MULTIPLAYER ready toggle, UIReadyToggle — online this is what "
                                + "every player presses for himself: MPConfirmEnterScenario "
                                + "initialises it with show:true and label GUI_LOADOUT_ENTER_SCENARIO "
                                + "on EVERY client, and only the all-ready callback is host-gated, "
                                + "UILoadoutManager.cs:474-517)"
                              : "(the SINGLE-PLAYER long-confirm button, "
                                + "UILoadoutManager.confirmationButton, the object "
                                + "SetActiveSinglePlayerLongConfirmButton switches on at :94)")
                          + $" was moved from '{(_home != null ? _home.name : "<none>")}' INTO the "
                          + $"floated Character-UI '{host.name}' (ID {host.ID}). THE PLACE IS DERIVED, "
                          + "NOT DIALLED, AND IT IS TWO MEASUREMENTS ON TWO SUBJECTS (ModBuild 241). "
                          + "VERTICALLY its painted centre is on the centre of the CHARACTER BLOCK — "
                          + $"{_anchorBlock.xMin:F0}..{_anchorBlock.xMax:F0} x "
                          + $"{_anchorBlock.yMin:F0}..{_anchorBlock.yMax:F0} px, unioned this tick from "
                          + $"{_anchorBlockSlots} of the game's own NewPartyCharacterUI row(s) "
                          + $"({_anchorBlockGraphics} graphic(s)), read off "
                          + "NewPartyDisplayUI.CharacterSlots by reference and never by name. "
                          + $"HORIZONTALLY its painted LEFT edge sits {ConfirmGapPx:F0} authored px to "
                          + "the RIGHT of the right edge of everything the window paints "
                          + $"({_anchorContent.xMin:F0}..{_anchorContent.xMax:F0} x "
                          + $"{_anchorContent.yMin:F0}..{_anchorContent.yMax:F0} px, unioned this tick "
                          + $"from {_anchorContentCount} drawn graphic(s) that passed the CONVERSION'S "
                          + "OWN fit visibility verdict, with this control's own "
                          + $"{_anchorControlCount} graphic(s) EXCLUDED so the solve is a fixed point "
                          + "and not a feedback loop), CLAMPED into the window's own rect so a uGUI "
                          + "mask can never cull it. THE SEAT THE INK WAS ASKED FOR: "
                          + $"{_anchorSeat.xMin:F0}..{_anchorSeat.xMax:F0} x "
                          + $"{_anchorSeat.yMin:F0}..{_anchorSeat.yMax:F0} px. WHY TWO SUBJECTS: "
                          + "\"rechts neben den Charakteren … mittig zentriert\" is about the ROWS, and "
                          + "\"das Auftauchen der Questinfos … muss ihn entsprechend verschieben, damit "
                          + "sich nichts überlagert\" is about everything else the window draws — so the "
                          + "vertical zero is the rows and the horizontal zero is the whole union, and "
                          + "the button steps right when the quest information opens. WHY HERE AND NOT "
                          + "ON THE STORY WINDOW: pressing it is a LOCAL act (see this class's doc for "
                          + "the source), and the user's ruling was \"abhängig von der Antwort … "
                          + "entweder auf der lokalen Character-UI oder dem MP-Fenster mit der "
                          + "Story\". NOTHING WAS WRITTEN TO THE GAME: no Show, no Hide, no Escape, no "
                          + "SetActive, no CanvasGroup — this is a re-parent of a local uGUI subtree "
                          + "on this client and nothing goes on the wire.");
    }

    /// <summary>
    /// THE LINE THAT PROVES REQUIREMENT THREE HAPPENED — <i>"Das Auftauchen der Questinfos obwohl der
    /// Button schon da ist muss ihn entsprechend verschieben, damit sich nichts überlagert."</i>
    ///
    /// <para>It fires when the SEAT (the rectangle the ink was asked to occupy) has moved by more than
    /// one <see cref="ConfirmGapPx"/> since the last time it spoke, so a settling layout's sub-pixel
    /// jitter cannot spend the budget and a genuine re-flow always does. It prints both rectangles and
    /// the two unions the seat was solved from, so a reader can tell WHICH of the two subjects moved:
    /// a jump in the content union with the block union unchanged is the quest information opening,
    /// which is the case the user is describing.</para>
    ///
    /// <para>GREP: <c>LOADOUT CONFIRM RESEATED</c>. Its absence across a session in which the user
    /// opened a quest picker is the falsification — either the signature edge is blind or the solve is
    /// not moving.</para>
    /// </summary>
    private static void ReportReseat()
    {
        if (!_anchorValid)
            return;
        if (!_reseatFromValid)
        {
            _reseatFromValid = true;
            _reseatFrom = _anchorSeat;
            return;
        }
        Vector2 moved = _anchorSeat.center - _reseatFrom.center;
        if (Mathf.Abs(moved.x) < ConfirmGapPx && Mathf.Abs(moved.y) < ConfirmGapPx)
            return;
        Rect from = _reseatFrom;
        _reseatFrom = _anchorSeat;
        if (_reseatReports >= MaxReseatReports)
            return;
        _reseatReports++;
        VRLog.Info(Scope, "LOADOUT CONFIRM RESEATED: the continue control's seat moved by "
                          + $"({moved.x:F0},{moved.y:F0}) authored px, from "
                          + $"{from.xMin:F0}..{from.xMax:F0} x {from.yMin:F0}..{from.yMax:F0} to "
                          + $"{_anchorSeat.xMin:F0}..{_anchorSeat.xMax:F0} x "
                          + $"{_anchorSeat.yMin:F0}..{_anchorSeat.yMax:F0}. WHAT MOVED UNDER IT: the "
                          + "window's whole painted union is now "
                          + $"{_anchorContent.xMin:F0}..{_anchorContent.xMax:F0} x "
                          + $"{_anchorContent.yMin:F0}..{_anchorContent.yMax:F0} from "
                          + $"{_anchorContentCount} graphic(s) (the control's own excluded), and the "
                          + "character block is "
                          + (_anchorBlockValid
                              ? $"{_anchorBlock.xMin:F0}..{_anchorBlock.xMax:F0} x "
                                + $"{_anchorBlock.yMin:F0}..{_anchorBlock.yMax:F0} from "
                                + $"{_anchorBlockSlots} row(s)"
                              : "NOT MEASURABLE this tick, so the whole union is standing in for it")
                          + ". READ IT LIKE THIS: a horizontal move with the block unchanged is the "
                          + "quest information opening or closing beside the roster, which is the "
                          + "user's ModBuild 241 requirement three and the only thing this line exists "
                          + "to evidence. A vertical move means the roster itself changed height. "
                          + "NOTHING WAS WRITTEN TO THE GAME to produce it: this is an anchoredPosition "
                          + "on a re-parented local uGUI subtree.");
    }

    private static void RefreshAnchor(RectTransform win, RectTransform rect, Rect frame)
    {
        ConvertedPanel? panel = ModalFallback.PanelFor(_host);
        if (!TryPaintedBounds(rect, win, panel, exclude: null, out Rect ctrl, out int ctrlCount)
            || ctrlCount == 0)
        {
            _anchorWhy = "the control has no visible Graphic this tick (the game usually has it "
                         + "switched off until the requirements are met) — the previous zero is kept";
            return;
        }
        if (!TryPaintedBounds(win, win, panel, exclude: rect, out Rect raw, out int rawCount)
            || rawCount == 0)
        {
            _anchorWhy = "the Character-UI paints no visible Graphic outside the control itself — "
                         + "the previous zero is kept";
            return;
        }

        // FRAME-CLAMPED, MapTravelConfirm's reason verbatim: the raw union of a converted window
        // reaches well outside its own rect from graphics faint enough that the fit throws them away,
        // and clamping is what makes the horizontal zero agree with the panel the player sees.
        Rect content = Rect.MinMaxRect(Mathf.Max(raw.xMin, frame.xMin), Mathf.Max(raw.yMin, frame.yMin),
                                       Mathf.Min(raw.xMax, frame.xMax), Mathf.Min(raw.yMax, frame.yMax));
        if (content.width <= 0f || content.height <= 0f)
        {
            _anchorWhy = "the Character-UI's visible union does not overlap its own rect at all, so "
                         + "clamping leaves nothing to measure — the previous zero is kept";
            return;
        }

        // THE CHARACTER BLOCK — the vertical zero, and the ONLY subject the phrase "mittig zentriert"
        // is about. Measured off the game's own row list; a failure here is not fatal, the whole
        // painted union stands in for it and _anchorWhy says so, because a button beside the column at
        // the wrong height is a cosmetic fault and no button at all is a deadlock.
        bool haveBlock = TryCharacterBlock(win, panel, exclude: rect, out Rect block,
                                           out int blockSlots, out int blockGraphics);
        Rect band = haveBlock ? block : content;

        // THE CONTROL'S OWN PIVOT-TO-INK OFFSET. RectTransform.position IS the pivot's world
        // position, so this is exact whatever anchors the rect is carrying — which is the point:
        // a second writer that re-anchors it between two of our ticks cannot corrupt the solve.
        Vector3 pivotLocal = win.InverseTransformPoint(rect.position);
        float inkLeftFromPivot = ctrl.xMin - pivotLocal.x;
        float inkMidYFromPivot = ctrl.center.y - pivotLocal.y;
        float inkW = Mathf.Abs(ctrl.width);
        float inkHalfH = Mathf.Abs(ctrl.height) * 0.5f;

        // RIGHT OF EVERYTHING THE WINDOW DRAWS, NOT RIGHT OF THE ROWS. content already contains the
        // block, so the Max is a guard and not a second opinion: block is the RAW union and content is
        // the frame-clamped one, and on a window whose rows reached outside their own frame the raw
        // number is the one that must win, or the seat would be solved against a clipped edge.
        float wantLeft = Mathf.Max(content.xMax, band.xMax) + ConfirmGapPx;
        float wantMidY = band.center.y;

        // AND THE CONTROL MUST STAY INSIDE THE WINDOW'S OWN RECT, WHICH IS NOT A COSMETIC BOUND.
        // The character screen clips its content with uGUI masks, and a control pushed past the
        // window's edge would be CULLED — invisible, while every state test in this file still read
        // "parked, active, claimed". That is the exact failure mode this project has shipped before
        // ([[measure-the-picture-not-the-state]]), and it would be a deadlock wearing the fix's
        // clothes. So the ink is clamped into the frame. WHAT THAT COSTS WHEN IT BITES, stated rather
        // than hidden: if the drawn content ever reaches so far right that there is no room left for
        // the control between it and the frame's right edge, the clamp pulls the control back over
        // that content and the two overlap. Ugly beats unreachable, every time — and unlike ModBuild
        // 239's vertical clamp, which fired on EVERY tick of the shipped build because the roster
        // column is content all the way to the frame's bottom edge, this one has 1920 - 894 - 307 =
        // 719 px of slack in the worst state the ModBuild 239 hardware log recorded (the union with
        // the battle-goal picker open). Whether it ever fires is on the falsifier line as the
        // difference between the requested seat and the measured ink.
        float roomRight = Mathf.Max(frame.xMin, frame.xMax - inkW);
        float clampedLeft = Mathf.Clamp(wantLeft, frame.xMin, roomRight);
        float left = clampedLeft;
        float midY = Mathf.Clamp(wantMidY, frame.yMin + inkHalfH, Mathf.Max(frame.yMin + inkHalfH,
                                                                            frame.yMax - inkHalfH));

        // ModBuild 382 — THE ESCAPE THE PARAGRAPH BELOW SAID DID NOT EXIST. It said "there is no
        // geometric escape to fall back to on this window ... the painted union spans the FULL
        // HEIGHT of the frame (-540..540 in the same log), so 'below the content' and 'left of the
        // content' are not free either". That reasoning is a BOUNDING BOX argument, and a bounding
        // box cannot see a gap. The 2026-09-03 photograph shows what the box hides: the roster
        // column draws at the frame's LEFT edge, the shoved battle-goal column draws at its RIGHT
        // edge, and roughly a thousand authored px of the frame between them is empty forest. The
        // union -960..960 is true and useless; the LANES inside it are what decide where a 307 px
        // control can stand.
        //
        // So when there is no room to the right — and ONLY then, so every state the shipped build
        // gets right is untouched to the pixel — the seat is re-solved against the x-intervals of
        // the ink that actually crosses the control's own row, rather than against their union. The
        // rule inside the lane is the SAME rule as the primary solve, applied to the nearest painted
        // edge on the control's left instead of to the global maximum: one ConfirmGapPx gap right of
        // it. That is why this is not a second placement policy — it is the same sentence with a
        // smaller subject, and it degrades to the ModBuild 239 clamp when no lane fits.
        var verdict = SeatVerdict.Beside;
        Rect lane = default;
        float laneClearLeft = 0f;
        float laneClearRight = 0f;
        int laneBlockers = 0;
        if (wantLeft > roomRight + 0.5f)
        {
            if (TryFreeLane(win, panel, rect, frame, midY - inkHalfH - ConfirmGapPx,
                            midY + inkHalfH + ConfirmGapPx, inkW,
                            out float laneLeft, out lane, out laneBlockers,
                            out laneClearLeft, out laneClearRight))
            {
                left = laneLeft;
                verdict = SeatVerdict.Lane;
                _seatLaneCount++;
            }
            else
            {
                verdict = SeatVerdict.Over;
                _seatOverCount++;
            }
        }
        else
        {
            _seatBesideCount++;
        }

        // ...AND THE PARAGRAPH ABOVE WAS FALSIFIED ON HARDWARE (ModBuild 367). It reasoned that the
        // clamp had "1920 - 894 - 307 = 719 px of slack in the worst state the ModBuild 239
        // hardware log recorded". The 2026-09-03 log recorded a worse one: with the battle-goal
        // picker re-opened the painted union reached x=763, leaving 197 px for a 307 px control, so
        // the clamp bit and pulled the button 110 px back OVER the cards. User: "sind die Abstaende
        // kaputt ... der button rechts daneben, NICHT DARUEBER LIEGEND."
        //
        // THE CAUSE IS FIXED SOMEWHERE ELSE, and deliberately: the column had no business being at
        // x=763 at all — the game's own viewport fit shoved it there through a camera that no
        // longer looks at this panel (TooltipWindowPatches.CutWorldFitRectTransform). With that cut
        // the union goes back to about -104 and this clamp has its slack again.
        //
        // WHAT CHANGES HERE IS THAT THE CLAMP CAN NO LONGER BITE SILENTLY. There is no geometric
        // escape to fall back to on this window and the line above must not invent one: the painted
        // union spans the FULL HEIGHT of the frame (-540..540 in the same log), so "below the
        // content" and "left of the content" are not free either, and the ModBuild 239 ruling —
        // ugly beats unreachable — still stands as the last resort. A silent last resort is the
        // part that was wrong. If this ever fires again it now says so at a tier a hardware log
        // carries, with the two numbers that decide it, so the next round starts from the measured
        // overlap instead of from a screenshot.
        if (wantLeft > frame.xMax - inkW + 0.5f && !_clampBiteLogged)
        {
            _clampBiteLogged = true;
            // HW-VERIFY: this line is the falsifier for the 2026-09-03 layout report. It must stay
            // at a tier the DEFAULT log level prints (Note/Alert/Error).
            VRLog.Alert(Scope, "LOADOUT CONFIRM CLAMPED OVER THE CONTENT: the seat solve asked for "
                + $"left={wantLeft:F0} authored px (one {ConfirmGapPx:F0} px gap right of a painted "
                + $"union reaching {Mathf.Max(content.xMax, band.xMax):F0}), but the control is "
                + $"{inkW:F0} px wide and the window frame ends at {frame.xMax:F0}, so the frame "
                + $"clamp pulled it back to {clampedLeft:F0} — {wantLeft - clampedLeft:F0} px INSIDE "
                + "the content "
                + "it is supposed to sit beside. THIS IS THE STATE THE USER PHOTOGRAPHED on "
                + "2026-09-03 (fenster-abstand.jpg). If this line is in a log built after ModBuild "
                + "367 the viewport-fit cut did NOT hold and the battle-goal column is still being "
                + "shoved right — grep VIEWPORT FIT CUT and VIEWPORT FIT GATE ARMED next, in that "
                + "order. The button is still reachable; it is only ugly. Logged once per session. "
                // APPENDED ModBuild 382 — nothing above is reworded; two of its sentences are now
                // wrong and the corrections have to travel with them or the next reader repeats the
                // round this build came out of.
                + "APPENDED ModBuild 382, TWO CORRECTIONS AND ONE CHANGE OF BEHAVIOUR. (1) THE "
                + "INSTRUCTION ABOVE IS A DEAD END: in the ModBuild 380 hardware log the "
                + "viewport-fit cut DID hold — it fired on 'Container' inside this very window and "
                + "returned zero, exactly as designed — and the union still reached the frame edge. "
                + "The column is therefore not being shoved by that helper and re-fixing it buys "
                + "nothing. (2) THE NUMBER ABOVE IS NO LONGER WHERE THE CONTROL LANDS. It is what "
                + "the frame clamp WOULD have done, kept verbatim so this line reads the same "
                + "against every earlier log; since ModBuild 382 a seat with no room to its right "
                + "is re-solved into the widest lane of the frame that draws no ink across the "
                + "control's own row, and the clamp is only taken when the frame has no such lane. "
                + "WHICH OF THE TWO HAPPENED, on this tick and on every later one, is the SEAT LANE "
                + "line this class prints beside this one — read that for the outcome, and this one "
                + "only for the fact that the right-hand seat was impossible.");
        }

        ReportSeatVerdict(verdict, frame, wantLeft, clampedLeft, left, inkW,
                          Mathf.Max(content.xMax, band.xMax), lane, laneBlockers,
                          laneClearLeft, laneClearRight);

        _anchorPivot = new Vector2(left - inkLeftFromPivot, midY - inkMidYFromPivot);
        _anchorSeat = Rect.MinMaxRect(left, midY - inkHalfH, left + inkW, midY + inkHalfH);
        _anchorValid = true;
        _anchorContent = content;
        _anchorContentCount = rawCount;
        _anchorControl = ctrl;
        _anchorControlCount = ctrlCount;
        _anchorBlockValid = haveBlock;
        _anchorBlock = block;
        _anchorBlockSlots = blockSlots;
        _anchorBlockGraphics = blockGraphics;
        _anchorWhy = haveBlock
            ? $"resolved — vertically centred on {blockSlots} character row(s), horizontally one "
              + $"{ConfirmGapPx:F0} px gap right of everything the window paints"
            : "resolved, BUT THE CHARACTER ROWS COULD NOT BE MEASURED (NewPartyDisplayUI.CharacterSlots "
              + "is empty, gone, or none of its rows painted a graphic the fit counts) — the window's "
              + "whole painted union is standing in for the block, so the button is beside the content "
              + "at the content's mid-height rather than the roster's";
        ReportReseat();
    }

    /// <summary>Scratch for <see cref="TryCharacterBlock"/>'s per-row unions. Never held across a
    /// call.</summary>
    private static readonly List<NewPartyCharacterUI> SlotScratch = new(8);

    /// <summary>
    /// <b>WHERE THE CHARACTER ROWS ACTUALLY ARE, in <paramref name="win"/>'s own authored uGUI px.</b>
    ///
    /// <para>The subject of <i>"rechts neben den Charakteren angezeigt … mittig zentriert"</i>, and it
    /// is read off the game's OWN list: <c>NewPartyDisplayUI.CharacterSlots</c>, the serialized
    /// <c>List&lt;NewPartyCharacterUI&gt;</c> the display drives. No name match, no scene sweep, no
    /// <c>GetComponentInChildren</c> — the rows are enumerated by reference from the component that
    /// owns them, so this cannot answer "something related to a character row"
    /// ([[containment-is-not-identity]]).</para>
    ///
    /// <para><b>DRAWN-NESS IS THE FILTER, NOT SLOT STATE.</b> Each row is unioned through the same
    /// <see cref="TryPaintedBounds"/> the rest of this file uses, i.e. through the CONVERSION'S OWN fit
    /// visibility verdict, and a row that contributes no counted graphic contributes no rectangle. An
    /// empty or available slot that still paints its frame is therefore part of the block — because it
    /// is part of the column the player sees, which is what "neben den Charakteren" means — while a
    /// row held at <c>CanvasGroup.alpha</c> 0 is not, which no <c>activeInHierarchy</c> test could have
    /// told us ([[inherited-alpha-is-not-the-group]]). <c>IsHidden</c> is checked first only because it
    /// is one bool against a subtree walk.</para>
    ///
    /// <para><b>AND THE PARKED CONTROL IS EXCLUDED</b> — <paramref name="exclude"/> is threaded through
    /// for the same reason it is threaded through the content sweep. The control is parented to the
    /// window ROOT and not into a row, so today it cannot be inside one; passing it anyway costs one
    /// reference compare per graphic and means a future round that parks it elsewhere does not
    /// silently acquire a feedback loop.</para>
    ///
    /// <para>Never throws. FALSE means "the previous answer, or the whole content union, must stand
    /// in" and never "place it at the origin".</para>
    /// </summary>
    private static bool TryCharacterBlock(RectTransform win, ConvertedPanel? panel,
                                          RectTransform? exclude, out Rect block, out int slots,
                                          out int graphics)
    {
        block = default;
        slots = 0;
        graphics = 0;
        try
        {
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            List<NewPartyCharacterUI>? rows = display != null ? display.CharacterSlots : null;
            if (rows == null || rows.Count == 0)
                return false;

            SlotScratch.Clear();
            SlotScratch.AddRange(rows);
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < SlotScratch.Count; i++)
            {
                NewPartyCharacterUI? row = SlotScratch[i];
                if (row == null || row.IsHidden || row.transform is not RectTransform rt)
                    continue;
                if (!TryPaintedBounds(rt, win, panel, exclude, out Rect one, out int counted)
                    || counted == 0)
                    continue;
                slots++;
                graphics += counted;
                if (one.xMin < minX) minX = one.xMin;
                if (one.yMin < minY) minY = one.yMin;
                if (one.xMax > maxX) maxX = one.xMax;
                if (one.yMax > maxY) maxY = one.yMax;
            }
            SlotScratch.Clear();
            if (slots == 0 || maxX <= minX || maxY <= minY)
                return false;
            block = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }
        catch (System.Exception)
        {
            SlotScratch.Clear();
            slots = 0;
            graphics = 0;
            return false;
        }
    }

    private static readonly List<Graphic> PaintScratch = new(64);
    private static readonly Vector3[] Corners = new Vector3[4];

    // ---- ModBuild 382: the lane solve -------------------------------------------------------------

    /// <summary>Corners of a CLIPPER, kept apart from <see cref="Corners"/> because the clipper walk
    /// runs inside the loop that is already using those for the graphic being measured.</summary>
    private static readonly Vector3[] ClipCorners = new Vector3[4];

    /// <summary>The x-intervals of ink crossing the control's row, one <c>Vector2(min,max)</c> each,
    /// before merging. Reused; never escapes <see cref="TryFreeLane"/>.</summary>
    private static readonly List<Vector2> LaneScratch = new(64);

    /// <summary>Left-to-right order for <see cref="LaneScratch"/>, cached so the merge does not
    /// allocate a delegate on every solve.</summary>
    private static readonly System.Comparison<Vector2> LaneOrder = (a, b) => a.x.CompareTo(b.x);

    /// <summary>
    /// THE WIDEST VERTICAL LANE OF <paramref name="frame"/> THAT DRAWS NO INK ACROSS THE CONTROL'S
    /// OWN ROW, and the seat inside it.
    ///
    /// <para><b>WHY A LANE AND NOT THE UNION.</b> The horizontal solve above is
    /// <c>union.xMax + gap</c>, and when the union reaches the frame's right edge that has no answer.
    /// The ModBuild 367 note reasoned from the union's HEIGHT that there was no escape anywhere —
    /// "the painted union spans the FULL HEIGHT of the frame". Both statements are about a bounding
    /// box, and a bounding box is exactly the wrong instrument for "is there room": the
    /// 2026-09-03 photograph shows the roster pinned to the frame's left edge, the battle-goal
    /// column drawn against its right edge, and about a thousand authored px of nothing in between.
    /// The union sees one rectangle from -960 to 960; the player sees two columns and a gap.</para>
    ///
    /// <para><b>WHAT COUNTS AS INK HERE, AND WHY IT IS STRICTER THAN THE UNION'S.</b> Same
    /// visibility verdict as <see cref="TryPaintedBounds"/> — <c>CanvasConversion.CountsAsFitContent</c>,
    /// borrowed and not re-invented — but the rect that verdict is applied to is CLAMPED TO ITS
    /// ENCLOSING uGUI CLIPPERS (see <see cref="ClipToMasks"/>). The union deliberately does not do
    /// that and is not being changed to: it is the shipped, hardware-tested zero for the seat, and
    /// no build has shown its unclipped rects to move the answer. A LANE, though, is decided by the
    /// ABSENCE of ink, and a scroll list's content rect — which reaches far outside the viewport
    /// that clips it, 373 px below this very frame in the ModBuild 380 log — would close every lane
    /// in the frame while drawing nothing the player can see. A gap search must measure the pixels,
    /// not the layout rects that produce them.</para>
    ///
    /// <para><b>THE BAND.</b> Only ink whose clipped rect crosses <paramref name="bandMinY"/>..
    /// <paramref name="bandMaxY"/> can block the control, because only that ink can be beside it.
    /// The caller passes the control's own ink rows grown by one <see cref="ConfirmGapPx"/> on each
    /// side, so a seat one pixel under a card's bottom edge is not counted as clear.</para>
    ///
    /// <para><b>WHICH LANE, AND WHERE IN IT.</b> The WIDEST usable lane, tie-broken rightmost —
    /// widest because the clearance either side is the whole margin this solve has against a
    /// measurement being slightly off, and rightmost because the control's whole reason for moving
    /// is that content appeared to its left. Inside the lane the seat is the primary solve's own
    /// rule with a nearer subject: one <see cref="ConfirmGapPx"/> right of the painted edge on the
    /// control's left, or hard against the frame when the lane opens at the frame's own edge.</para>
    /// </summary>
    private static bool TryFreeLane(RectTransform win, ConvertedPanel? panel, RectTransform exclude,
                                    Rect frame, float bandMinY, float bandMaxY, float inkW,
                                    out float seatLeft, out Rect lane, out int blockers,
                                    out float clearLeft, out float clearRight)
    {
        seatLeft = 0f;
        lane = default;
        blockers = 0;
        clearLeft = 0f;
        clearRight = 0f;
        if (inkW <= 0f || frame.width <= inkW)
            return false;
        try
        {
            PaintScratch.Clear();
            LaneScratch.Clear();
            ClipMemo.Clear();
            win.GetComponentsInChildren(includeInactive: false, PaintScratch);
            if (panel != null)
                CanvasConversion.BeginContentQuery();
            Transform? rowExclude = MapQuestReadyRoster.HeldRow;

            Vector3[] corners = Corners;
            for (int i = 0; i < PaintScratch.Count; i++)
            {
                Graphic g = PaintScratch[i];
                if (g == null)
                    continue;
                RectTransform rt = g.rectTransform;
                if (rt == null)
                    continue;
                if (ReferenceEquals(rt, exclude) || rt.IsChildOf(exclude))
                    continue;
                // THE ROW IS THE CONTROL'S OWN DECORATION AND CANNOT BE ALLOWED TO BLOCK IT — see
                // IsTheReadyRow, whose doc carries the hardware evidence for this exact sweep.
                if (IsTheReadyRow(rt, rowExclude))
                    continue;
                if (panel != null)
                {
                    if (!CanvasConversion.CountsAsFitContent(panel, g))
                        continue;
                }
                else if (!CountsAsPaintedHere(g))
                {
                    continue;
                }
                Vector2 size = rt.rect.size;
                if (size.x < 1f || size.y < 1f)
                    continue;

                rt.GetWorldCorners(corners);
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                for (int c = 0; c < 4; c++)
                {
                    Vector3 p = win.InverseTransformPoint(corners[c]);
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }
                if (!ClipToMasks(rt, win, ref minX, ref minY, ref maxX, ref maxY))
                    continue;
                if (maxY <= bandMinY || minY >= bandMaxY)
                    continue; // draws nowhere near the control's row
                minX = Mathf.Max(minX, frame.xMin);
                maxX = Mathf.Min(maxX, frame.xMax);
                if (maxX - minX < 0.5f)
                    continue;
                LaneScratch.Add(new Vector2(minX, maxX));
                blockers++;
            }
            PaintScratch.Clear();
            if (blockers == 0)
            {
                // NOTHING PAINTS IN THE CONTROL'S ROW, which contradicts the only state that gets
                // here (the union reached the frame's right edge, so something is drawing). Either
                // the band is wrong or the clip walk is eating real ink, and a lane solved from an
                // empty measurement would fling the button to the frame's left edge. Decline, and
                // let the ModBuild 239 clamp answer — the verdict line then says OVER with 0
                // blockers, which is the signature of exactly this disagreement.
                LaneScratch.Clear();
                ClipMemo.Clear();
                return false;
            }

            LaneScratch.Sort(LaneOrder);

            // Walk the merged blockers left to right; every hole between two of them (and the two
            // margins against the frame) is a candidate lane.
            bool found = false;
            float bestWidth = 0f;
            float cursor = frame.xMin;
            bool cursorIsInk = false;
            for (int i = 0; i <= LaneScratch.Count; i++)
            {
                float holeMin = cursor;
                float holeMax;
                bool holeMaxIsInk;
                if (i == LaneScratch.Count)
                {
                    holeMax = frame.xMax;
                    holeMaxIsInk = false;
                }
                else
                {
                    Vector2 blk = LaneScratch[i];
                    if (blk.x <= cursor + 0.5f)
                    {
                        // Overlaps or touches the run we are already inside — extend it and go on.
                        if (blk.y > cursor)
                        {
                            cursor = blk.y;
                            cursorIsInk = true;
                        }
                        continue;
                    }
                    holeMax = blk.x;
                    holeMaxIsInk = true;
                }

                float usableMin = holeMin + (cursorIsInk ? ConfirmGapPx : 0f);
                float usableMax = holeMax - (holeMaxIsInk ? ConfirmGapPx : 0f);
                float usable = usableMax - usableMin;
                if (usable >= inkW - 0.5f && usable >= bestWidth)
                {
                    found = true;
                    bestWidth = usable;
                    seatLeft = usableMin;
                    lane = Rect.MinMaxRect(holeMin, bandMinY, holeMax, bandMaxY);
                    clearLeft = seatLeft - holeMin;
                    clearRight = holeMax - (seatLeft + inkW);
                }

                if (i < LaneScratch.Count)
                {
                    Vector2 blk = LaneScratch[i];
                    cursor = Mathf.Max(cursor, blk.y);
                    cursorIsInk = true;
                }
            }
            LaneScratch.Clear();
            ClipMemo.Clear();
            return found;
        }
        catch (System.Exception)
        {
            PaintScratch.Clear();
            LaneScratch.Clear();
            ClipMemo.Clear();
            blockers = 0;
            return false;
        }
    }

    /// <summary>
    /// Intersect a graphic's host-local rect with every enclosing uGUI clipper between it and
    /// <paramref name="win"/> — <see cref="RectMask2D"/> and stencil <see cref="Mask"/>, i.e. a
    /// ScrollRect viewport. Returns false when nothing survives, which is the "fully scrolled out"
    /// case. This is <c>CanvasConversion.TryGetVisibleHostRect</c>'s own clamp, re-derived here for
    /// one reason: that method computes the clipped rect and <c>CountsAsFitContent</c> throws it
    /// away with <c>out _</c>, and the conversion is another lane's file this round.
    /// </summary>
    private static bool ClipToMasks(RectTransform rt, RectTransform win,
                                    ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        Transform? p = rt.parent;
        for (int guard = 0; p != null && !ReferenceEquals(p, win) && guard < 64; guard++, p = p.parent)
        {
            Vector4 c = ClipperRect(p, win);
            if (float.IsNaN(c.x))
                continue;
            if (c.x > minX) minX = c.x;
            if (c.y > minY) minY = c.y;
            if (c.z < maxX) maxX = c.z;
            if (c.w < maxY) maxY = c.w;
            if (maxX - minX < 0.5f || maxY - minY < 0.5f)
                return false;
        }
        return maxX - minX >= 0.5f && maxY - minY >= 0.5f;
    }

    /// <summary>
    /// The window-local rect a transform clips its descendants to, as (minX,minY,maxX,maxY), or a
    /// <c>NaN</c> x when it clips nothing. A <see cref="RectMask2D"/> clips to its own rect; a
    /// stencil <see cref="Mask"/> clips to the rect of the Graphic it masks with, which is on the
    /// same object.
    ///
    /// <para>MEMOISED PER SOLVE, and that is not a micro-optimisation. Siblings share their whole
    /// ancestor chain, so without the memo the walk is two <c>GetComponent</c> calls plus a corner
    /// transform per ancestor per GRAPHIC — on the 600-graphic state this method exists to handle,
    /// several thousand of each. This project has shipped a per-frame scene sweep it called
    /// "near-free" more than once; the memo is the same lesson applied before the fact. The
    /// dictionary is cleared at the start of every <see cref="TryFreeLane"/> because transforms move
    /// between solves, which is <c>CanvasConversion.BeginContentQuery</c>'s own rule.</para>
    /// </summary>
    private static Vector4 ClipperRect(Transform t, RectTransform win)
    {
        if (ClipMemo.TryGetValue(t, out Vector4 memo))
            return memo;
        Vector4 result = NotAClipper;
        if (t is RectTransform clip)
        {
            var rect2D = t.GetComponent<RectMask2D>();
            bool clips = rect2D != null && rect2D.isActiveAndEnabled;
            if (!clips)
            {
                var mask = t.GetComponent<Mask>();
                clips = mask != null && mask.isActiveAndEnabled && mask.graphic != null;
            }
            if (clips)
            {
                clip.GetWorldCorners(ClipCorners);
                float cMinX = float.MaxValue, cMinY = float.MaxValue;
                float cMaxX = float.MinValue, cMaxY = float.MinValue;
                for (int c = 0; c < 4; c++)
                {
                    Vector3 q = win.InverseTransformPoint(ClipCorners[c]);
                    if (q.x < cMinX) cMinX = q.x;
                    if (q.x > cMaxX) cMaxX = q.x;
                    if (q.y < cMinY) cMinY = q.y;
                    if (q.y > cMaxY) cMaxY = q.y;
                }
                result = new Vector4(cMinX, cMinY, cMaxX, cMaxY);
            }
        }
        ClipMemo[t] = result;
        return result;
    }

    /// <summary>Sentinel for "this transform clips nothing" in <see cref="ClipMemo"/>.</summary>
    private static readonly Vector4 NotAClipper = new(float.NaN, float.NaN, float.NaN, float.NaN);

    /// <summary>Per-solve memo behind <see cref="ClipperRect"/>. Cleared by
    /// <see cref="TryFreeLane"/>, never read outside it.</summary>
    private static readonly Dictionary<Transform, Vector4> ClipMemo = new(32);

    /// <summary>
    /// THE OUTCOME OF THE HORIZONTAL SOLVE, AS A VERDICT AND NOT AS A PATH. Three states the user's
    /// 2026-09-03 report cannot be answered without telling apart: the control sat beside the
    /// content as designed; the control did not fit to the right and was re-seated into a free lane;
    /// the control did not fit anywhere and is clamped over the content. The previous instrument
    /// printed only the third and only once, so a log could not distinguish "fixed" from "the state
    /// never occurred".
    ///
    /// <para>GREP: <c>LOADOUT CONFIRM SEAT LANE</c>. It is change-gated on the verdict kind plus a
    /// 64 px bucket of the seat, so a settled layout says it once and a re-flow that changes the
    /// answer says it again; the three counters are what make a single line readable as a live
    /// instrument rather than a stopped one.</para>
    /// </summary>
    private static void ReportSeatVerdict(SeatVerdict verdict, Rect frame, float wantLeft,
                                          float clampedLeft, float left, float inkW, float unionRight,
                                          Rect lane, int blockers, float clearLeft, float clearRight)
    {
        int stamp = ((int)verdict * 1000003) + Mathf.RoundToInt(left / 64f);
        if (stamp == _seatVerdictStamp)
            return;
        _seatVerdictStamp = stamp;

        string body = verdict switch
        {
            SeatVerdict.Beside =>
                $"BESIDE — the design case. The window's painted union ends at {unionRight:F0} "
                + $"authored px, the {inkW:F0} px control was asked for left={wantLeft:F0}, and the "
                + $"frame's right edge at {frame.xMax:F0} left room for it, so the seat is the "
                + "solve's own answer and no clamp and no lane search ran.",
            SeatVerdict.Lane =>
                $"LANE — the right-hand seat was IMPOSSIBLE and was escaped without overlapping. The "
                + $"painted union ends at {unionRight:F0}, the solve asked for left={wantLeft:F0} for "
                + $"a {inkW:F0} px control, and the frame ends at {frame.xMax:F0}, so there was no "
                + $"room to the right; the frame clamp would have put it at {clampedLeft:F0}, "
                + $"{wantLeft - clampedLeft:F0} px inside the content. INSTEAD the seat is "
                + $"{left:F0}, inside the widest lane of the frame that draws no ink across the "
                + $"control's row: the lane spans x {lane.xMin:F0}..{lane.xMax:F0} over rows y "
                + $"{lane.yMin:F0}..{lane.yMax:F0}, and the control clears {clearLeft:F0} px on its "
                + $"left and {clearRight:F0} px on its right. {blockers} graphic(s) drew into that "
                + "row and were measured with their uGUI clippers applied, so a scroll list's "
                + "off-viewport content cannot close a lane it does not paint in.",
            _ =>
                $"OVER — the last resort, and the button is on the content. The painted union ends "
                + $"at {unionRight:F0}, the solve asked for left={wantLeft:F0} for a {inkW:F0} px "
                + $"control, the frame ends at {frame.xMax:F0}, and the lane search found no gap in "
                + $"the control's own row wide enough for it among {blockers} graphic(s) that draw "
                + $"there. Seat {left:F0}, overlapping the content by {wantLeft - left:F0} px. Ugly "
                + "beats unreachable (ModBuild 239) and the control is still pressable, but this is "
                + "the state the user photographed and it means the frame really is full across "
                + "that row — the fix is then upstream, in whatever placed the content, not here.",
        };

        string tally = $" SOLVES THIS SESSION: {_seatBesideCount} beside, {_seatLaneCount} lane, "
                       + $"{_seatOverCount} over.";
        if (verdict == SeatVerdict.Over)
        {
            // HW-VERIFY: the 2026-09-03 layout report is answered by WHICH of these three verdicts a
            // hardware log carries. It must stay at a tier the DEFAULT log level prints.
            VRLog.Alert(Scope, "LOADOUT CONFIRM SEAT LANE: " + body + tally);
        }
        else
        {
            // HW-VERIFY: same line, same question; the two non-failing verdicts are the evidence
            // that the failing one did NOT occur, which is the half a once-per-session Alert
            // could never carry.
            VRLog.Note(Scope, "LOADOUT CONFIRM SEAT LANE: " + body + tally);
        }
    }

    /// <summary>
    /// The painted bounds of a subtree in <paramref name="win"/>'s local (authored uGUI) space, with
    /// an optional subtree EXCLUDED.
    ///
    /// <para>THE VISIBILITY VERDICT IS BORROWED, NOT RE-INVENTED: with a panel in hand this asks
    /// <c>CanvasConversion.CountsAsFitContent</c> — the fit's own four-reason test — so the numbers
    /// here and the numbers in the fit line cannot disagree. That is the same choice
    /// <c>StoryComposite.TryPaintedBounds</c> makes and for the same recorded reason: a second, weaker
    /// predicate is how <c>MrBacking.GlyphTrueRect</c> once unioned back in text the fit had already
    /// judged invisible.</para>
    /// </summary>
    private static bool TryPaintedBounds(RectTransform root, RectTransform win, ConvertedPanel? panel,
                                         RectTransform? exclude, out Rect local, out int counted)
    {
        local = default;
        counted = 0;
        try
        {
            PaintScratch.Clear();
            root.GetComponentsInChildren(includeInactive: false, PaintScratch);
            if (PaintScratch.Count == 0)
                return false;
            Transform? rowExclude = MapQuestReadyRoster.HeldRow;
            if (panel != null)
                CanvasConversion.BeginContentQuery();

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            Vector3[] corners = Corners;
            for (int i = 0; i < PaintScratch.Count; i++)
            {
                Graphic g = PaintScratch[i];
                if (g == null)
                    continue;
                RectTransform rt = g.rectTransform;
                if (rt == null)
                    continue;
                if (exclude != null && (ReferenceEquals(rt, exclude) || rt.IsChildOf(exclude)))
                    continue;
                // THE READY ROW IS NEVER CONTENT — see IsTheReadyRow for the argument. Sweeps rooted
                // at the control's own subtree are unaffected: the row is never a child of the
                // control.
                if (IsTheReadyRow(rt, rowExclude))
                    continue;
                if (panel != null)
                {
                    if (!CanvasConversion.CountsAsFitContent(panel, g))
                        continue;
                }
                else if (!CountsAsPaintedHere(g))
                {
                    continue;
                }
                Vector2 size = rt.rect.size;
                if (size.x < 1f || size.y < 1f)
                    continue;
                rt.GetWorldCorners(corners);
                for (int c = 0; c < 4; c++)
                {
                    Vector3 p = win.InverseTransformPoint(corners[c]);
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }
                counted++;
            }
            PaintScratch.Clear();
            if (counted == 0)
                return false;
            local = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }
        catch (System.Exception)
        {
            PaintScratch.Clear();
            counted = 0;
            return false;
        }
    }

    /// <summary>
    /// IS THIS GRAPHIC PART OF THE READY ROW THIS MOD PARKED BESIDE THE CONTROL? If so it is never
    /// content and never a blocker, at ANY sweep in this file.
    ///
    /// <para>The row is the game's own <c>UIReadyTrackerBar</c>, adopted by
    /// <c>MapQuestReadyRoster</c> and hung off the control's painted top edge — so it is parked to
    /// the RIGHT of everything else this window draws and it FOLLOWS the control. Any solve that
    /// counted it would be a term standing on its own output: a seat measured against the right edge
    /// of the painted content pushes the control right, which pulls the row right, which pushes the
    /// control right again; and a LANE search that treats it as an obstacle can have its own
    /// decoration close the only gap the control fits in.</para>
    ///
    /// <para><b>THE SECOND FORM IS NOT HYPOTHETICAL — the 2026-09-05 hardware log has it.</b>
    /// <c>LOADOUT CONFIRM SEAT LANE</c> for 'New Party display' reads LANE with 52 blocker(s) and
    /// seat -632, then OVER with 58 blocker(s) ("the lane search found no gap … wide enough") and
    /// seat 510, then LANE with 58 blocker(s) and seat -632 again, within a few log lines and across
    /// the tick on which <c>LOADOUT CONFIRM PARKED</c> and <c>ICON ROW=YES</c> both landed. The seat
    /// moved 1142 px and back, and the six graphics that appeared between the two readings did so on
    /// the tick the row was adopted into this window — which is as far as a log that does not name
    /// blockers individually can go, and far enough: the row was a candidate blocker and it is now
    /// not one, so the next log's blocker count answers it outright. ModBuild 436 closed this loop
    /// inside <see cref="TryPaintedBounds"/> and stated
    /// that a call site added later could not forget it — but <see cref="TryFreeLane"/> is not a
    /// <see cref="TryPaintedBounds"/> call site; it is a second sweep with its own loop, and it did
    /// forget [[a-cascade-clears-only-what-it-lists]]. The test now lives in one method that BOTH
    /// sweeps call, which is the only shape that makes the claim true.</para>
    /// </summary>
    private static bool IsTheReadyRow(RectTransform rt, Transform? row) =>
        row != null && (ReferenceEquals(rt, row) || rt.IsChildOf(row));

    /// <summary>The minimal "is this drawn?" test, used ONLY when there is no converted panel to ask
    /// the fit's own verdict of. Deliberately weaker and deliberately stated as such —
    /// <c>StoryComposite.CountsAsPaintedHere</c>'s twin.</summary>
    private static bool CountsAsPaintedHere(Graphic g) =>
        g.enabled && g.gameObject.activeInHierarchy
        && (g.canvasRenderer == null || !g.canvasRenderer.cull)
        && g.color.a > 0.02f
        && !g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal);

    // ---- the park claim (online only) -------------------------------------------------------------

    /// <summary>
    /// Re-assert or drop <c>MapRoom.ReadyToggleParkClaim</c> — a LEVEL and never a latch, exactly as
    /// <c>MapQuestReadyUp.TickClaim</c> does it, because <c>FloatRefusalTable</c>'s ROW 2 is the
    /// reader and a refusal with no parker behind it is a deadlock generator.
    ///
    /// <para>THE CLAIM IS MADE ON INTENT, BEFORE THE GAME SHOWS THE TOGGLE, for the reason
    /// <c>MapQuestReadyUp</c> records: the toggle's <c>Show</c> can beat this parker's first tick to
    /// the float gate, and a claim that arrives one frame late means a float this class then has to
    /// withdraw. An invisible window never floats, so an early claim costs nothing.</para>
    ///
    /// <para>AND IT LETS GO RATHER THAN HOLD. If the toggle is on screen and NOT parked for longer
    /// than <see cref="ClaimGraceSeconds"/> the claim is dropped with one warning: the standing rule
    /// is that a window must never be invisible, and it outranks the rule that a bare button must not
    /// float. The <c>FLOAT REFUSAL LAPSED</c> warning that follows is EXPECTED beside it, not a second
    /// fault.</para>
    /// </summary>
    private static void TickClaim(GameObject? control, bool readyToggle, bool parked)
    {
        if (!readyToggle || control == null)
        {
            DropClaim(control == null ? "there is no confirm control to claim"
                                      : "the offline confirm is a plain Button, not a UIWindow — the "
                                        + "float gate never sees it and there is nothing to claim");
            return;
        }

        if (parked)
        {
            _visibleUnparkedSince = -1f;
            SetClaim(control, "LoadoutConfirmPark has parked it INSIDE the floated Character-UI "
                              + $"'{(_host != null ? _host.name : "<gone>")}', directly under the "
                              + "window's own painted content and centred on it — the local place the "
                              + "user chose for a control every player presses for himself");
            return;
        }

        if (!ReadyToggleVisible())
        {
            // NOT DRAWN, SO NOTHING CAN FLOAT AND NOTHING CAN BE UNREACHABLE. This is the claim that
            // beats the toggle's own Show to the float gate.
            _visibleUnparkedSince = -1f;
            SetClaim(control, "LoadoutConfirmPark is standing by to park the loadout ready toggle "
                              + "into the floated Character-UI the moment the game shows it; the claim "
                              + "is made on INTENT so the float gate has an answer the very first time "
                              + "it asks, rather than one frame after a float it would have to "
                              + "withdraw");
            return;
        }

        if (_visibleUnparkedSince < 0f)
            _visibleUnparkedSince = Time.unscaledTime;
        if (Time.unscaledTime - _visibleUnparkedSince <= ClaimGraceSeconds)
        {
            SetClaim(control, "LoadoutConfirmPark is about to park the loadout ready toggle into the "
                              + "Character-UI — that window is not a live floated panel yet, and this "
                              + "claim covers the few frames it takes to become one");
            return;
        }
        DropClaim($"the loadout ready toggle has been on screen and unparked for more than "
                  + $"{ClaimGraceSeconds:F1} s, so the refusal is released rather than leaving a "
                  + "button nobody can press");
    }

    private static void SetClaim(GameObject control, string why)
    {
        if (!_claimStanding || why != _claimWhy)
        {
            _claimStanding = true;
            _claimWhy = why;
            VRLog.Info(Scope, $"LOADOUT CONFIRM: park claim SET for '{control.name}' — {why}. WHAT IT "
                              + "BUYS: while it stands FloatRefusalTable's ROW 2 refuses to float the "
                              + "ready toggle as a window of its own, so the free-floating button in "
                              + "its own frame (.planning/debug/frei_schwebender_button_multiplayer"
                              + ".jpg) cannot appear. THE CLAIM IS A LEVEL with a one-second lifetime: "
                              + "it is re-asserted on every tick of this parker and lapses by itself if "
                              + "the parker stops running, throws or stands down — which is what makes "
                              + "it impossible for this refusal to outlive the thing that justifies "
                              + "it.");
        }
        ReadyToggleParkClaim.Set(control, _claimWhy);
    }

    private static void DropClaim(string why)
    {
        if (!_claimStanding)
            return;
        _claimStanding = false;
        _claimWhy = "nothing is claimed";
        _visibleUnparkedSince = -1f;
        VRLog.Info(Scope, $"LOADOUT CONFIRM: park claim DROPPED — {why}. The catch-all may float the "
                          + "ready toggle as a window of its own again from here, which is deliberate: "
                          + "a confirm nobody can reach is a worse fault than a confirm in an ugly "
                          + "frame.");
        ReadyToggleParkClaim.Set(null, "LoadoutConfirmPark is not presenting the loadout confirm");
    }

    // ---- the falsifier -----------------------------------------------------------------------------

    private static void ResetReach()
    {
        _reachVerdict = string.Empty;
        _reachReports = 0;
        _reachHost = null;
        _enterVerdict = string.Empty;
        _enterReports = 0;
        _enterNextAt = float.NegativeInfinity;
        // The re-seat budget is per LOADOUT SCREEN and not per park: a park/unpark cycle inside one
        // loadout (online ⇄ offline swaps the control) must not buy a fresh six lines, and the next
        // EnterLoadout must not inherit a spent one.
        _reseatReports = 0;
    }

    /// <summary>
    /// CAN THIS PLAYER GET AT THE CONTINUE CONTROL RIGHT NOW? A PURE read for
    /// <c>StoryComposite</c>'s deadlock floor: TRUE whenever the game is not asking for a control at
    /// all (there is nothing to be unreachable), and TRUE when the control is measured drawing inside
    /// a live floated panel. FALSE is therefore exactly the deadlock and nothing weaker.
    ///
    /// <para>THE HOST IS NOT ASSUMED TO BE THIS CLASS'S PARK. It walks UP from the control to the
    /// nearest <c>UIWindow</c> ancestor the mod is floating with a live panel, so the answer is TRUE
    /// in both of the good states: parked into the Character-UI by this class, and drawn on a loadout
    /// screen that is floating normally because nothing is refusing it. A floor that only recognised
    /// its own remedy would report a deadlock every time the remedy was unnecessary.</para>
    /// </summary>
    internal static bool ConfirmReachable()
    {
        try
        {
            UILoadoutManager? lm = Manager();
            return lm == null || MeasureReach(lm, out _, out _);
        }
        catch (System.Exception)
        {
            return true;   // never trip a floor on a measurement that failed
        }
    }

    /// <summary>
    /// The one measurement both the falsifier and <see cref="ConfirmReachable"/> read, so a log line
    /// and a floor can never disagree about the same tick.
    /// </summary>
    private static bool MeasureReach(UILoadoutManager lm, out string measured, out bool readyToggle)
    {
        if (!GameWantsConfirmShown())
        {
            readyToggle = false;
            measured = "the game is not asking for a continue control this tick";
            return true;
        }

        GameObject? control = ResolveControl(lm, out readyToggle);
        bool active = control != null && control.activeInHierarchy;
        UIWindow? drawnIn = active ? FloatedAncestorWindow(control!.transform) : null;
        ConvertedPanel? panel = ModalFallback.PanelFor(drawnIn);
        int drawn = 0;
        if (active && drawnIn != null && control!.transform is RectTransform rect
            && drawnIn.transform is RectTransform win)
            TryPaintedBounds(rect, win, panel, exclude: null, out _, out drawn);

        measured =
            "the game says a continue control should be showable now (UILoadoutManager.IsOpen=True, "
            + $"CanShowConfirmationButton()=True, network={(FFSNetwork.IsOnline ? "ONLINE" : "OFFLINE")}); "
            + "the control is "
            + (control != null
                ? $"'{control.name}' ({(readyToggle
                    ? "UIReadyToggle — the multiplayer ready toggle"
                    : "UILoadoutManager.confirmationButton — the single-player long confirm")})"
                : "<the mod cannot resolve which object IS the confirm>")
            + $", activeInHierarchy={active}, drawing {drawn} graphic(s) the conversion's own fit "
            + "counts; the nearest floated window it is inside is "
            + (drawnIn != null ? $"'{drawnIn.name}' (ID {drawnIn.ID}), live panel={panel != null}"
                               : "<NONE — it is inside no window the mod is floating>")
            + $"; this class {(_parked != null && _host != null ? $"has it parked in '{_host.name}'" : "has not parked it")}";
        return control != null && active && drawnIn != null && panel != null && drawn > 0;
    }

    /// <summary>
    /// The nearest <c>UIWindow</c> ANCESTOR (or self) that the mod is floating with a live panel, or
    /// null. A containment walk is the right shape here and only here: the question is not "IS this
    /// object an X" ([[containment-is-not-identity]]) but "is this object being DRAWN inside one of
    /// ours", which is a question about the hierarchy by construction.
    /// </summary>
    private static UIWindow? FloatedAncestorWindow(Transform from)
    {
        Transform? t = from;
        for (int guard = 0; t != null && guard < 64; guard++, t = t.parent)
        {
            UIWindow? w = t.GetComponent<UIWindow>();
            if (w != null && ModalFallback.PanelFor(w) != null && FloatedByMod(w))
                return w;
        }
        return null;
    }

    /// <summary>
    /// THE ONE LINE A TESTER CAN GREP THAT IS TRUE ONLY IF THE PLAYER CAN ACTUALLY GET ON WITH THE
    /// GAME, and the WARNING that names the suppressor when he cannot.
    ///
    /// <para>It is asked only while the GAME says a continue control should be showable
    /// (<see cref="GameWantsConfirmShown"/> — <c>UILoadoutManager.CanShowConfirmationButton()</c> plus
    /// the game's own switch on the control), so it is silent through the whole battle-goal phase and
    /// speaks exactly at the moment the ModBuild 234 tester was stuck. Every clause of the CONFIRMED
    /// form is measured on the object THIS TICK: the control is active in the hierarchy, it is
    /// painting at least one graphic the conversion's own fit counts, and its host is a live floated
    /// panel. It asserts nothing it cannot see ([[an-instrument-can-assert-a-cause]]).</para>
    ///
    /// <para>GREP: <c>LOADOUT CONFIRM REACHABLE: CONFIRMED</c> — the fix.
    /// <c>LOADOUT CONFIRM REACHABLE: NO</c> — the deadlock, with the suppressor named.</para>
    /// </summary>
    private static void ReportReach(UILoadoutManager lm, UIWindow? loadout)
    {
        if (!ReferenceEquals(loadout, _reachHost))
        {
            _reachHost = loadout;
            _reachVerdict = string.Empty;
            _reachReports = 0;
        }
        if (!GameWantsConfirmShown())
        {
            _reachVerdict = string.Empty;   // the question is not being asked; re-arm the edge
            _enterVerdict = string.Empty;
            return;
        }

        // ITS OWN COUNTER AND ITS OWN GATE: the two lines answer different requests and one must not
        // be able to silence the other.
        ReportEnterDungeon(lm);

        if (_reachReports >= MaxReachReports)
            return;
        bool ok = MeasureReach(lm, out string measuredCore, out bool readyToggle);
        string verdict = ok ? "CONFIRMED" : "NO";
        if (verdict == _reachVerdict)
            return;
        _reachVerdict = verdict;
        _reachReports++;

        string measured = measuredCore
            + "; the measured zero is "
            + (_anchorValid
                ? $"the host's painted content {_anchorContent.xMin:F0}..{_anchorContent.xMax:F0} x "
                  + $"{_anchorContent.yMin:F0}..{_anchorContent.yMax:F0} px from "
                  + $"{_anchorContentCount} graphic(s), against the control's own ink "
                  + $"{_anchorControl.width:F0}x{_anchorControl.height:F0} px from "
                  + $"{_anchorControlCount}"
                : $"NOT RESOLVED — {_anchorWhy}")
            + $" (control kind: {(readyToggle ? "the multiplayer ready toggle" : "the single-player long confirm")})";

        if (ok)
        {
            VRLog.Info(Scope, "LOADOUT CONFIRM REACHABLE: CONFIRMED — the player can see and press the "
                              + "continue control. MEASURED THIS TICK: " + measured + ". USER REPORT "
                              + "THIS LINE ANSWERS: \"DEADLOCK: Nachdem ich für jeden Character die "
                              + "Quest ausgewählt habe, muss irgendwo der button erscheinen damit es "
                              + "weiter gehen kann. Der ist nie erschienen, man konnte nicht weiter "
                              + "vorranschreiten.\"");
            return;
        }
        VRLog.Warn(Scope, "LOADOUT CONFIRM REACHABLE: NO — THE GAME WANTS A CONTINUE CONTROL SHOWN AND "
                          + "THE MOD CANNOT FIND IT ANYWHERE. THIS IS THE DEADLOCK: there is nothing to "
                          + "press and no way forward. MEASURED THIS TICK: " + measured
                          + ". THE SUPPRESSOR, IF ANY: "
                          + (loadout != null
                              ? FloatRefusalTable.Describe(loadout)
                                ?? "nothing in the refusal table has anything to say about the loadout "
                                   + "window, so its float is not being withheld by this mod — look at "
                                   + "the catch-all's own eligibility instead (MODAL FALLBACK / "
                                   + "CATCH-ALL lines for 'UI Loadout Window')"
                              : "the loadout window itself could not be resolved")
                          + ". READ IT LIKE THIS: offline the continue button is a CHILD of the loadout "
                          + "window, so a refusal that withholds that window's float takes the button "
                          + "with it — that is exactly what happened in the ModBuild 234 log "
                          + "(FLOAT REFUSED / FLOAT WITHDRAWN at :11483-11484, and no further float of "
                          + "'UI Loadout Window' in the rest of the session). This class exists to make "
                          + "that survivable by drawing the control in the Character-UI instead; if "
                          + "this line is in a hardware log, look UP for LOADOUT CONFIRM UNPARKED or a "
                          + "'cannot park' line to see why it is not.");
    }

    // ---- ModBuild 238's falsifier ------------------------------------------------------------------

    /// <summary>
    /// THE TOP EDGE OF THE WINDOW'S GRAB BAR, in <paramref name="win"/>'s own authored uGUI px, or
    /// <c>float.NaN</c> when there is no bar to measure.
    ///
    /// <para><b>WHY IT IS FOUND BY WALKING AND NOT ASKED FOR.</b> <c>GrabbableModal</c> exposes no
    /// accessor for the bar — <c>_bar</c>, <c>_inkRect</c> and the derived <c>barTopPx</c> are all
    /// private and the class is another lane's — so this reads the same transform its own
    /// <c>GRAB BAR CLEARS THE INK</c> line reads, from the one place that is public: the holder is
    /// registered as a render root of the panel (<c>CanvasConversion.AddRenderRoot(_panel, _holder)</c>
    /// in <c>GrabbableModal.Build</c>), which puts it in <c>ConvertedPanel.ExtraRenderRoots</c>. The
    /// names are <c>GloomhavenVR.ModalGrab_*</c> for the holder and <c>Visual/Bar</c> for the strip,
    /// both written by that same method.</para>
    ///
    /// <para><b>AND IT IS MEASURED THROUGH WORLD SPACE ON PURPOSE.</b> The bar's own local numbers are
    /// frame-local METRES and the control's are the window's authored PIXELS; converting through the
    /// world puts both in one space with no assumption about how the host rect and the game window
    /// root are related. The bar rides the DRAWN pose rather than the frame, so during a remote glide
    /// the two can disagree for a few frames — that is a transient of the multiplayer easing and not
    /// a placement fault, and it is why this number is only ever printed by a rate-limited line.</para>
    /// </summary>
    private static float BarTopInWindowPx(ConvertedPanel? panel, RectTransform win)
    {
        try
        {
            if (panel == null)
                return float.NaN;
            for (int i = 0; i < panel.ExtraRenderRoots.Count; i++)
            {
                Transform? root = panel.ExtraRenderRoots[i];
                if (root == null
                    || !root.name.StartsWith("GloomhavenVR.ModalGrab_", System.StringComparison.Ordinal))
                    continue;
                Transform? bar = root.Find("Visual/Bar");
                if (bar == null)
                    continue;
                // The strip is a unit cube scaled by the bar transform, so its top face is local
                // y = +0.5 — the same half-thickness GrabbableModal.ReportBarPlacement adds.
                Vector3 top = bar.TransformPoint(new Vector3(0f, 0.5f, 0f));
                return win.InverseTransformPoint(top).y;
            }
            return float.NaN;
        }
        catch (System.Exception)
        {
            return float.NaN;
        }
    }

    /// <summary>
    /// THE ONE LINE A TESTER CAN GREP THAT IS TRUE ONLY IF THE USER'S REQUEST IS SATISFIED. ModBuild
    /// 238's: <i>"soll der Button … 'Verlies betreten' … zu sehen sein, wo man ihn betätigen kann. So
    /// braucht man nicht die Übersicht über zwei Fenster behalten."</i> ModBuild 241's: <i>"Er sollte
    /// eher rechts neben den Charakteren angezeigt werden, mittig zentriert. Es muss aber noch möglich
    /// sein auch im Nachgang bevor man den Button drückt noch eine persönliche Quest zu ändern. Das
    /// Auftauchen der Questinfos … muss ihn entsprechend verschieben, damit sich nichts
    /// überlagert."</i>
    ///
    /// <para>Every clause is read back THIS TICK from the object the player is looking at — the
    /// control's own name and kind, its parent chain, its rect in the host's authored px, its layer
    /// against the host root's layer, the host window's rect, the character rows' own rects, and the
    /// grab bar's top edge in the SAME units so the clearance between them is a subtraction rather than
    /// a claim.</para>
    ///
    /// <para><b>WHAT HAPPENED TO THE ModBuild 238 GRAB-BAR CLEARANCE TERM — IT IS NOW UNFALSIFIABLE,
    /// AND THAT IS WHY IT IS NO LONGER A PASS/FAIL TERM.</b> The contract it enforced was: a control
    /// parked at the BOTTOM EDGE of this window must not land in the band the ModBuild 236 grab bar
    /// occupies (<c>.planning/debug/quest_überlap.jpg</c>). ModBuild 241 moved the control off the
    /// bottom edge, and the contract now holds by an argument no measurement can contradict:
    /// <c>GrabbableModal</c> places the bar's top at <c>Mathf.Min(hostRect.yMin, ink.yMin) - gap</c>,
    /// the parked control is a GAME-named child of the conversion target so <c>PanelInkBounds</c>
    /// counts it INTO that same <c>ink</c>, and therefore <c>barTop &lt; control.ink.yMin</c> is
    /// arithmetic and not a hope. A guard whose failing branch cannot be reached is reassurance and not
    /// a guard [[gated-remedy-never-ran]], so it has been REMOVED from the verdict — while the two
    /// numbers stay ON the line, because they are still the fastest way to see that the bar followed
    /// the button. WHAT REPLACES IT IN THE VERDICT is the term the ModBuild 241 report is actually
    /// about: does the control's painted ink INTERSECT anything the window draws — any character row,
    /// or any row's battle-goal toggle. That one can fail, it failed in the photograph, and it is the
    /// difference between the two builds.</para>
    ///
    /// <para><b>THE QUEST-PICKER TERM, AND WHY IT IS GEOMETRY AND NOT A FLAG.</b> <i>"Es muss aber
    /// noch möglich sein … noch eine persönliche Quest zu ändern"</i> can be broken two ways and only
    /// one of them shows up in a state test. The game does not gate it —
    /// <c>UILoadoutManager.CanShowConfirmationButton()</c> is TRUE while <c>ActiveDisplay ==
    /// BATTLE_GOALS</c> (:111-119), i.e. an open picker and a visible confirm are a shipped flat-game
    /// state — and this mod writes no <c>interactable</c>, no <c>CanvasGroup</c> and no selection mode.
    /// But <see cref="Park"/> puts the control at <c>SetAsLastSibling</c>, so it is drawn AND hit-tested
    /// above the roster, and a plate lying across a row's icon strip takes that row's clicks with every
    /// flag still reading true. So the line reports both halves per row: the toggle's own
    /// <c>IsInteractable()</c>, and whether the control's ink covers the toggle's rect.</para>
    ///
    /// <para>GREP: <c>ENTER DUNGEON ON THE CHARACTER UI: CONFIRMED</c> — the request is met.
    /// <c>… NOT ACHIEVED</c> — it is not, with the failing term named.</para>
    /// </summary>
    private static void ReportEnterDungeon(UILoadoutManager lm)
    {
        if (_enterReports >= MaxEnterReports)
            return;
        // ON THE ANCHOR'S OWN CADENCE, and for the anchor's own reason: the verdict below is decided
        // by two Graphic sweeps and a transform walk, the thing they measure changes when the roster
        // or a sub-panel changes, and running them per frame would buy nothing in a room whose Update
        // budget is already the one the perf line complains about.
        float now = Time.unscaledTime;
        if (now < _enterNextAt)
            return;
        _enterNextAt = now + AnchorRefreshIntervalSeconds;

        GameObject? control = ResolveControl(lm, out bool readyToggle);
        UIWindow? host = CharacterWindow();
        bool parked = control != null && _parked != null && ReferenceEquals(_parked, control)
                      && _host != null && ReferenceEquals(_host, host);
        bool hostFloated = host != null && FloatedByMod(host);
        ConvertedPanel? panel = ModalFallback.PanelFor(host);

        Rect frame = default;
        Rect ink = default;
        int drawn = 0;
        int controlLayer = -1;
        int hostLayer = -1;
        float barTop = float.NaN;
        float clearance = float.NaN;
        string chain = "<not parked>";
        bool underHost = false;
        bool clear = true;
        string rows = "the character rows were not measured this tick";

        if (control != null && host != null && host.transform is RectTransform win
            && control.transform is RectTransform rect)
        {
            frame = win.rect;
            underHost = ReferenceEquals(rect.parent, win);
            controlLayer = control.layer;
            hostLayer = host.gameObject.layer;
            TryPaintedBounds(rect, win, panel, exclude: null, out ink, out drawn);
            barTop = BarTopInWindowPx(panel, win);
            if (!float.IsNaN(barTop) && drawn > 0)
                clearance = ink.yMin - barTop;
            rows = DescribeRows(win, panel, rect, ink, drawn > 0, out clear);
        }

        // THE VERDICT. Every term is measured and none of them is a value this class merely intended.
        // THE ModBuild 241 TERM IS `clear` — the control's ink touches no character row and no row's
        // battle-goal toggle. The grab-bar clearance is NOT a term any more; see this method's doc for
        // why it can no longer fail and why it is still printed.
        bool ok = parked && hostFloated && panel != null && underHost && drawn > 0
                  && controlLayer == hostLayer
                  && clear;

        // THE CHANGE GATE BEFORE THE STRING, for the reason the sweeps above already argue: this is
        // asked on every tick from the moment the game switches the control on until the scenario
        // loads [[one-line-owned-the-frame]].
        string verdict = ok ? "CONFIRMED" : "NOT ACHIEVED";
        if (verdict == _enterVerdict)
            return;
        _enterVerdict = verdict;
        _enterReports++;

        if (control != null)
        {
            var sb = new System.Text.StringBuilder(64);
            Transform? t = control.transform;
            for (int guard = 0; t != null && guard < 6; guard++, t = t.parent)
            {
                if (sb.Length > 0)
                    sb.Append(" < ");
                sb.Append(t.name);
            }
            chain = sb.ToString();
        }

        string measured =
            $"THE CONTROL: '{(control != null ? control.name : "<the mod cannot resolve which object IS the confirm>")}' "
            + $"({(readyToggle ? "UIReadyToggle — the MULTIPLAYER ready toggle, which every player presses for himself"
                               : "UILoadoutManager.confirmationButton — the SINGLE-PLAYER long confirm, 'Verlies betreten'")}), "
            + $"activeInHierarchy={(control != null && control.activeInHierarchy)}, drawing {drawn} "
            + "graphic(s) the conversion's own fit counts. PARENT CHAIN, nearest first: "
            + $"{chain}; parented directly to the host root={underHost}; this class has it parked="
            + $"{parked}. ITS RECT in the host's own authored px: ink "
            + $"{ink.xMin:F0}..{ink.xMax:F0} x {ink.yMin:F0}..{ink.yMax:F0}. LAYER {controlLayer} "
            + $"against the host root's own layer {hostLayer} "
            + $"({LayerTx.Count} transform(s) written, {_layerSkipped} foreign render subtree(s) "
            + "skipped whole; per-window capture cameras cull BY LAYER, so a mismatch here is a "
            + "control drawn by the wrong camera or by two). THE HOST: "
            + $"'{(host != null ? host.name : "<none>")}' floated={hostFloated}, live panel="
            + $"{panel != null}, rect {frame.xMin:F0}..{frame.xMax:F0} x {frame.yMin:F0}..{frame.yMax:F0} "
            + $"({frame.width:F0}x{frame.height:F0} px). {HostIdentity(host, panel)} THE GRAB BAR: top edge "
            + (float.IsNaN(barTop) ? "NOT MEASURABLE — no grab frame is built for this window yet"
                                   : $"y={barTop:F0} px")
            + ", CLEARANCE between the control's painted bottom and that edge = "
            + (float.IsNaN(clearance) ? "not measurable"
                                      : $"{clearance:F0} px (positive means the bar is BELOW the button, which is the wanted order)")
            + " — PRINTED, NOT JUDGED, since ModBuild 241: the control no longer sits at the bottom "
            + "edge and PanelInkBounds counts it INTO the very union the bar is placed under, so this "
            + "number cannot go negative and a term that cannot fail is not a guard. THE ROWS: " + rows
            + $". THE ZERO the placement was solved from: {_anchorWhy}"
            + (_anchorValid
                ? $" — the character block {_anchorBlock.xMin:F0}..{_anchorBlock.xMax:F0} x "
                  + $"{_anchorBlock.yMin:F0}..{_anchorBlock.yMax:F0} px from {_anchorBlockSlots} row(s) "
                  + $"(measurable={_anchorBlockValid}) for the vertical centre, the host's whole painted "
                  + $"content {_anchorContent.xMin:F0}..{_anchorContent.xMax:F0} x "
                  + $"{_anchorContent.yMin:F0}..{_anchorContent.yMax:F0} px from {_anchorContentCount} "
                  + "graphic(s) for the right-hand edge, the control's own ink EXCLUDED from both so the "
                  + $"solve is a fixed point, gap {ConfirmGapPx:F0} px; THE SEAT ASKED FOR was "
                  + $"{_anchorSeat.xMin:F0}..{_anchorSeat.xMax:F0} x {_anchorSeat.yMin:F0}.."
                  + $"{_anchorSeat.yMax:F0} — if that differs from the ink above, the frame clamp bit or "
                  + "somebody else is writing the rect"
                : string.Empty);

        if (ok)
        {
            VRLog.Info(Scope, "ENTER DUNGEON ON THE CHARACTER UI: CONFIRMED — the continue control is "
                              + "drawn BESIDE the character rows of the floated Character-UI, vertically "
                              + "centred on them, inside the same window the battle goals were chosen "
                              + "in, and its ink touches no row and no row's battle-goal toggle. "
                              + "MEASURED THIS TICK: " + measured
                              + ". USER REQUEST THIS LINE ANSWERS (2026-08-24): \"Der 'Verlies betreten' "
                              + "Button ist jetzt auf dem richtigen Fenster aber an einer komischen "
                              + "Position, siehe betreten_button.jpg. Er sollte eher rechts neben den "
                              + "Charakteren angezeigt werden, mittig zentriert. Es muss aber noch "
                              + "möglich sein auch im Nachgang bevor man den Button drückt noch eine "
                              + "persönliche Quest zu ändern. Das Auftauchen der Questinfos obwohl der "
                              + "Button schon da ist muss ihn entsprechend verschieben, damit sich "
                              + "nichts überlagert.\" THE THIRD SENTENCE IS NOT ON THIS LINE: whether "
                              + "the button actually MOVED when the quest information opened is "
                              + "LOADOUT CONFIRM RESEATED, because a still picture of a good position "
                              + "cannot evidence a re-flow. NOTHING WAS WRITTEN TO THE GAME: no Show, "
                              + "no Hide, no Escape, no SetActive, no CanvasGroup — the control's own "
                              + "visibility is still the game's and this is a re-parent of a local uGUI "
                              + "subtree on this client.");
            return;
        }
        VRLog.Warn(Scope, "ENTER DUNGEON ON THE CHARACTER UI: NOT ACHIEVED — the continue control is "
                          + "not where the user asked for it. MEASURED THIS TICK: " + measured
                          + ". READ IT LIKE THIS, AND THE FAILING TERM IS THE ONE THAT READS WRONG "
                          + "ABOVE. parked=False or a parent chain that does not start at the host "
                          + "means the park did not happen — look up for LOADOUT CONFIRM UNPARKED or a "
                          + "'cannot park' line. host floated=False means the Character-UI is not on "
                          + "screen and there is nothing to be part of; that is the ordinary state "
                          + "during the story and it is not a fault there. 0 drawn graphic(s) means "
                          + "the game still has the control switched off, which is what the whole "
                          + "battle-goal phase looks like. A layer that differs from the host root's "
                          + "is the serious one: the control is being drawn by the wrong camera or by "
                          + "two, and every state test in this file will still read 'parked'. AN "
                          + "OVERLAP IN 'THE ROWS' IS THE ModBuild 241 FAULT ITSELF — the plate lying "
                          + "across a character row is betreten_button.jpg, and because Park puts the "
                          + "control at SetAsLastSibling it takes that row's clicks as well as covering "
                          + "it, which is the half of the report that reads \"es muss aber noch möglich "
                          + "sein … noch eine persönliche Quest zu ändern\". If the overlap is there "
                          + "while THE SEAT ASKED FOR is clear of the rows, the frame clamp bit and the "
                          + "window has no room to the right of its own content; if the seat itself "
                          + "overlaps, the character block measured wrong and the row count on this "
                          + "line says how many rows it found. THIS LINE IS NOT A DEADLOCK REPORT: "
                          + "whether the player can reach the control at all is LOADOUT CONFIRM "
                          + "REACHABLE beside it, and that is the line that fails towards letting him "
                          + "continue.");
    }

    /// <summary>
    /// <b>DOES THE PARKED CONTROL TOUCH ANY CHARACTER ROW, AND CAN EACH ROW'S QUEST PICKER STILL BE
    /// PRESSED?</b> The ModBuild 241 term, per row, measured off the game's own objects.
    ///
    /// <para>Two independent readings per row, because the requirement can break two ways.
    /// <c>battleGoalToggle.IsInteractable()</c> is Unity's own answer and already folds in every
    /// <c>CanvasGroup</c> above the control, so it catches a gate. The rect intersection catches an
    /// OCCLUSION, which no flag can see: the parked control is a sibling at
    /// <c>SetAsLastSibling</c> and therefore hit-tested first, so ink lying over a toggle takes its
    /// clicks with <c>IsInteractable()</c> still true.</para>
    ///
    /// <para><paramref name="clear"/> — the verdict term — is FALSE only on a real intersection. A row
    /// that cannot be measured, a toggle that is absent (single player never shows some of them) or a
    /// tick on which the control paints nothing all leave it TRUE: a falsifier must not fail on its own
    /// blindness, it must say it is blind, and the string does.</para>
    /// </summary>
    private static string DescribeRows(RectTransform win, ConvertedPanel? panel, RectTransform ctrlRect,
                                       Rect ctrlInk, bool inkValid, out bool clear)
    {
        clear = true;
        try
        {
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            List<NewPartyCharacterUI>? rows = display != null ? display.CharacterSlots : null;
            if (rows == null || rows.Count == 0)
                return "NewPartyDisplayUI exposes no character rows this tick, so the overlap term is "
                       + "UNJUDGED rather than passed";

            var sb = new System.Text.StringBuilder(160);
            int seen = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                NewPartyCharacterUI? row = rows[i];
                if (row == null || row.IsHidden || row.transform is not RectTransform rt)
                    continue;
                if (!TryPaintedBounds(rt, win, panel, ctrlRect, out Rect one, out int counted)
                    || counted == 0)
                    continue;
                seen++;
                bool hits = inkValid && Overlaps(ctrlInk, one);
                if (hits)
                    clear = false;

                Selectable? goal = row.battleGoalToggle;
                string picker;
                if (goal == null || !goal.gameObject.activeInHierarchy)
                {
                    picker = "quest picker absent";
                }
                else
                {
                    bool live = goal.IsInteractable();
                    bool covered = inkValid && goal.transform is RectTransform gt
                                   && TryLocalBounds(win, gt, out Rect gb) && Overlaps(ctrlInk, gb);
                    if (covered)
                        clear = false;
                    picker = $"quest picker interactable={live}, covered by the confirm={covered}";
                }

                if (sb.Length > 0)
                    sb.Append("; ");
                sb.Append("row ").Append(row.SlotIndex).Append(' ')
                  .Append($"{one.xMin:F0}..{one.xMax:F0} x {one.yMin:F0}..{one.yMax:F0}")
                  .Append(" confirm overlaps it=").Append(hits).Append(", ").Append(picker);
            }
            if (seen == 0)
                return "no character row painted a graphic the fit counts this tick, so the overlap "
                       + "term is UNJUDGED rather than passed";
            return $"{seen} drawn row(s) — " + sb;
        }
        catch (System.Exception e)
        {
            clear = true;
            return $"the row walk threw ({e.GetType().Name}), so the overlap term is UNJUDGED rather "
                   + "than passed";
        }
    }

    /// <summary>Do two host-local rectangles share any area? A strict test — touching edges are not an
    /// overlap, because the seat is solved to put them exactly one gap apart and a &gt;= here would
    /// report the correct answer as a fault.</summary>
    private static bool Overlaps(Rect a, Rect b) =>
        a.xMax > b.xMin && b.xMax > a.xMin && a.yMax > b.yMin && b.yMax > a.yMin;

    /// <summary>A RectTransform's axis-aligned bounds in <paramref name="win"/>'s local px, through
    /// world corners so any chain of scales or rotations is measured where it actually lands. Used for
    /// the quest-picker toggle, which has no Graphic of its own worth sweeping for.</summary>
    private static bool TryLocalBounds(RectTransform win, RectTransform rt, out Rect bounds)
    {
        bounds = default;
        try
        {
            Rect local = rt.rect;
            if (local.width < 0.5f || local.height < 0.5f)
                return false;
            rt.GetWorldCorners(Corners);
            Vector3 first = win.InverseTransformPoint(Corners[0]);
            float minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
            for (int i = 1; i < 4; i++)
            {
                Vector3 p = win.InverseTransformPoint(Corners[i]);
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>Module teardown. Hands the control back and lets go of the claim — a claim that
    /// outlived this class would keep the game's own confirm off screen with nothing left to park it
    /// into, which is the one failure mode this whole file exists to be incapable of.</summary>
    internal static void Reset()
    {
        Unpark("module teardown");
        DropClaim("module teardown");
        _standDown = false;
        _standDownWarned = false;
        _home = null;
        _homeRecorded = false;
        _homeOwner = null;
        _addedIgnore = null;
        ResetAnchor("module teardown");
        ResetReach();
        SlotScratch.Clear();
        PaintScratch.Clear();
        LayerTx.Clear();
        LayerWas.Clear();
        _layerWritten = -1;
        _layerSkipped = 0;
    }
}
