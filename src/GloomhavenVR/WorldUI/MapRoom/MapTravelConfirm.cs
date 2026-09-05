using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

// ---------------------------------------------------------------------------
// THE TRAVEL CONFIRMATION — THE GAME ALREADY HAS ONE, AND VR WAS ROUTING AROUND IT.
//
// User: "Ich vermisse einen Bestätigungsknopf, aktuell löst man die tatsächliche Auswahl viel zu
// schnell versehentlich aus. Wie ist das nochmal im flat Spiel?"
//
// READ FROM SOURCE, not from memory (decompiled AdventureMapUIManager.cs):
//
//     public void OnSelectedMapLocation(MapLocation mapLocation, Action<MapLocation> cb)   // :340
//     {
//         this.onConfirmTravelCallback = cb;
//         if (mapLocation == locationToTravel)
//         {
//             if (!FFSNetwork.IsOnline)
//                 ConfirmTravel();          // <-- SECOND CLICK ON THE SAME LOCATION JUST GOES
//             return;
//         }
//         ...
//         travelButton.TextLanguageKey = locationToTravel.IsCompleted() ? "GUI_REPLAY_LOCATION"
//                                                                       : "GUI_TRAVEL";        // :356
//     }
//
//     public void EnableTravelOptions(bool show)                                              // :408
//     {
//         if (!InputManager.GamePadInUse)
//             travelButton.gameObject.SetActive(!FFSNetwork.IsOnline);
//         travelOptions.SetActive(flag);
//     }
//
// So the flat game DOES have a confirmation step — a `travelButton` labelled Reisen/Wiederholen
// with a Cancel beside it, inside a `travelOptions` container — and it ALSO has a single-player
// shortcut: clicking the same location twice commits immediately, with no prompt. Online that
// shortcut is deliberately switched off (`!FFSNetwork.IsOnline`), which is the game's own statement
// that it is a convenience and not part of the contract.
//
// IN VR THAT SHORTCUT IS A TRAP. A second trigger pull on the same icon is far easier to produce
// than a mouse double-click — the beam stays where your hand is — and the map room never drew the
// flat HUD that carries the travelButton, so the shortcut was the ONLY reachable commit path AND
// it committed without asking. Both halves of that are wrong, and this class fixes both.
//
// MULTIPLAYER: PRESENTATION ONLY, PER CLIENT, NOTHING ON THE WIRE. Everything below moves a
// GameObject the LOCAL client already owns (the flat HUD's travel-options container) into a window
// this client has floated for itself, and reproduces — locally — the branch the game itself takes
// when FFSNetwork.IsOnline. No game state is written (in particular `travelOptions.activeSelf` is
// only ever READ), no action is sent, no field the netcode reads is touched, and a remote client's
// own copy of this class parks its own container into its own quest window. Two clients in the same
// session may legitimately have the button in two different places in their rooms, because they
// have their windows in two different places AND because the two placement dials below are a local
// preference. Nothing here is serialized, so no ModBuild handshake term depends on it.
//
// ===========================================================================================
// WHERE THE BUTTON GOES — ModBuild 194 RESTORES ModBuild 190 EXACTLY AND HANDS THE USER THE DIALS.
// THREE SOLVED PLACEMENTS WERE REJECTED IN A ROW. DO NOT TURN THIS BACK INTO A SOLVE.
// ===========================================================================================
//
// THE RULING THAT GOVERNS THIS FILE, verbatim:
//
//   "1) Mach die Position des Quest Buttons ganz rückgängig wie es das erste mal war als du den
//    button im window hinzugefügt hast. Geb mir dann im debug menu die offsets um ihm zu
//    verschieben - ich stell es selber ein."
//
// Two instructions, and the first is not negotiable: the pose is the ModBuild 190 pose, the VERY
// FIRST one, and it is restored WHOLE — not approximated, not improved, not re-derived. The second
// instruction says who does the tuning from here: he does, with dials, in the headset.
//
// THE HISTORY, so nobody re-invents a rejected answer:
//
//   190  PARKED THE CONTAINER INSIDE THE FLOATED QUEST WINDOW and pinned it: SetParent(window,
//        worldPositionStays:false), SetAsLastSibling, anchorMin = anchorMax = (0.5, 0),
//        pivot = (0.5, 1), anchoredPosition = Vector2.zero, localRotation = identity,
//        localScale = one. THIS IS THE BASELINE HE IS ASKING FOR, and it is what the code below
//        writes when both dials are at their default of 0.
//   191  kept the parenting and SOLVED an offset from the container's visible-Graphic content so
//        the content sat snug just BELOW the window's bottom edge. REJECTED: "viel zu weit oben
//        und auch auf der x-achse verschoben".
//   192  DETACHED the container entirely onto a world host of its own, placed from a census of
//        every floated window. REJECTED OUTRIGHT: "Jetzt hast du den Button komplett vom Fenster
//        getrennt. Mach das rückgängig."
//   193  kept the parenting and SOLVED the content's BOTTOM edge onto the bottom 8 % of the card.
//        ALSO REJECTED — which is the whole lesson of this block: three different solves, three
//        rejections. The mod does not know where he wants the button. He does.
//
// THE POSITION PATH CARRIED NO SOLVE AT ALL THROUGH ModBuild 196, AND ModBuild 197 PUTS ONE BACK
// ON PURPOSE — BECAUSE THE USER ASKED FOR THE REFERENCE FRAME TO MOVE. See the ModBuild 197 block
// below for the ruling and the hardware that backs it. Through 196 the container's pivot went to
//
//     window-local (win.rect.xMin, win.rect.yMax) + (OffsetX * windowHeight, OffsetY * windowHeight)
//
// — a FIXED frame, the window rect's top-left corner, which is where ModBuild 190 drew the button in
// every frame it rendered. That frame is exactly what he rejected at 197: the window rect is a
// CONSTANT 512x1021 for this card whatever quest is in it, while the quest information inside it is
// 742, 815 or 863 px tall depending on the text, so one tuned pair of numbers cannot sit under both
// a short quest and a long one. From 197 the zero MOVES WITH THE INFORMATION.
//
// THE FRAME THE DIALS LIVE IN, ModBuild 197. The dials place the container's PIVOT POINT at
//
//     _anchorPivot + (OffsetX * windowHeight, OffsetY * windowHeight)
//
// where `_anchorPivot` is the window-local point at which the container's pivot has to sit for
//
//   * the TOP edge of the button's own painted content to lie exactly on the BOTTOM edge of the
//     quest information's painted content, and
//   * the button's painted content to be horizontally CENTRED on the information's.
//
// So 0/0 means "directly underneath the quest information, centred on it" — the thing he has asked
// for across five builds — and it means that for a short quest and a long one alike, because both
// terms of it are measured every fifth of a second from what is actually on the card. +x is still
// the window's own RIGHT and +y its own UP, the unit is still fractions of the WINDOW'S HEIGHT, and
// the container still carries identity rotation and unit scale, so the dials still move the button
// in the plane of the card and nowhere else.
//
// HIS TUNED NUMBERS DO NOT SURVIVE THIS, AND THAT IS SAID OUT LOUD RATHER THAN PAPERED OVER. He runs
// TravelButtonOffsetX = 0.244 / OffsetY = -0.726 against the OLD fixed frame. The zero has moved
// roughly three quarters of a window height down and a quarter of one across, so those two numbers
// now mean something else entirely and must go back to 0/0. They are NOT auto-migrated — a dropped
// cfg value is always against the newest build and is taken verbatim, so silently rewriting his file
// would break the one rule that makes his drops trustworthy. Instead `ZeroMovedNotice` prints ONE
// line, once per session, at Warn level while either dial is still non-zero, naming both values and
// saying what to set them to.
//
// AND THE NEW ZERO IS NOT A POSE HE HAS TO ACCEPT ON FAITH — IT IS WHERE HE HIMSELF PUT IT. From the
// ModBuild 196 hardware line (Player.log:4970, window rect 512x1021, one local unit = 0.875 mm):
// his tuned pair placed the button's painted content at window-local x-centre -7.0, y -201.3..-136.3.
// The 197 zero places it at x-centre 0.0 (the information's centre, frame-clamped — see below) and
// y -219.7..-154.7 (its top on the information's bottom). That is 6 mm sideways and 16 mm down from
// the pose he tuned by hand and accepted. The frame changed; the place did not.
//
// THE ANCHORS ARE NO LONGER ASSUMED — THEY ARE READ. `anchoredPosition` is meaningless without the
// anchor it is measured from, so the applied value is computed every tick as
// `wantedPivot - anchorReference(live anchorMin/anchorMax)`. Whatever anchor the container is
// carrying, the button lands on the same window-local point, and this class never writes an anchor
// after the park — which is what makes an anchor fight impossible rather than merely unlikely.
//
// THE UNIT IS FRACTIONS OF THE WINDOW'S OWN HEIGHT, and the reason is the one dial the user already
// owns: [WorldUI] WindowLegibility rescales the floated windows, and he resizes them. A pixel offset
// tuned against one window size is silently wrong at the next one — the button would drift off the
// card the moment the card changed size — while a fraction of the window's height keeps the button
// in the SAME PLACE ON THE CARD at every size, which is the relationship the eye actually judges.
// (This is the same argument [WorldUI] BarSizeScale makes for millimetres-at-the-eye and the same
// one 193's inset made for window heights; it is the house unit for "where on this surface".)
//
// WHY THE HEIGHT FOR BOTH AXES AND NOT THE WIDTH FOR X. One reference length makes the two dials
// COMMENSURABLE: 0.1 on either dial is the same real distance, so a diagonal nudge of equal numbers
// is a true 45°, and the millimetre figures in the log below apply to both dials at once. Using the
// width for x would make two numbers that look alike mean two different distances, which is exactly
// the mixed-units bug class this project has already shipped once.
//
// THE CLAMPS, RE-DERIVED AT ModBuild 197 AGAINST THE MOVING ZERO, AND TIGHTENED RATHER THAN WIDENED.
// From the ModBuild 196 hardware line: the quest window's rect is 512 x 1021 local units at
// lossyScale 0.1734 world units per local unit, and the map room runs at a rig scale of ~198 world
// units per REAL metre, so one local unit is 0.1734 / 198 m = 0.875 mm real. That makes the card
// 448 mm wide and 893 mm tall in front of the player's face, and the clamps are now
//
//     OffsetX  in  -0.25 … +0.25 window heights  =  -223 … +223 mm
//     OffsetY  in  -0.60 … +0.60 window heights  =  -536 … +536 mm
//
// WHY THOSE TWO NUMBERS, and why the old ±0.50 / ±1.50 would now be travel into nowhere:
//
//   THE OLD RANGES EXISTED TO REACH THE TARGET. Under the fixed top-left frame the button started
//   ABOVE the card's top edge and getting it under the information cost most of a window height of
//   downward travel — that is the entire reason ModBuild 195 widened Y to -1.5, and it is why the
//   user's own value is -0.726. From 197 the ZERO IS the target, so that journey is no longer part
//   of the dial's job and the range that paid for it is dead travel.
//
//   X = ±0.25 IS EXACTLY THE CARD. The zero is the information's horizontal centre, the card is
//   512 px = 0.2507 window heights wide, so ±0.25 reaches its LEFT edge and its RIGHT edge and
//   essentially nothing beyond. There is no sideways position ON the card this dial cannot express
//   and no value of it that walks the button off the side.
//
//   Y = ±0.60 SPANS THE CARD IN BOTH DIRECTIONS FROM A ZERO THAT IS ALREADY WHERE HE WANTS IT.
//   Measured: on the SHORT quest the information's bottom edge sat 356 px above the card's own
//   bottom edge and on the LONG one 189 px above it, so -0.35 already puts the button's top on the
//   card's bottom edge in the worst of those cases and -0.60 (613 px) carries it a further 257 px
//   (225 mm) clear below the card; +0.60 lifts it 613 px back up over the information, past its
//   middle on every quest measured. Every placement anybody has argued for in builds 190-196 is
//   inside that, and it is half a window height LESS rope than 196 handed out.
//
// NOTHING IN THAT BOX IS UNREACHABLE, which is the standing bound on any dial, and the box is now
// SMALLER than the one 196 shipped. The button is still a CHILD of the window at every value: it
// moves, scales, occludes, floats and goes home with it, so no dial value can strand it somewhere in
// the world, behind the player, or under the table while the window is elsewhere. It is hit by the
// same raycaster and the same laser that already hit the window the player is looking at, and its
// CanvasGroup carries blocksRaycasts whenever it is visible, so it is clickable everywhere inside
// that box. And a setting may only make OPTIONAL content optional: these are PLACEMENT dials for
// content that is always present — no value of either one removes the confirm button, changes what
// it does, or affects game state.
//
// READ LIVE, LEVEL-TRIGGERED. Both dials are read on every tick of `Reconcile` (MapRoomDriver calls
// it once per frame while the room stands), the wanted offset is recomputed, and it is WRITTEN ONLY
// WHEN IT DIFFERS from what the rect already carries by more than `OffsetEpsilon`. So he can turn a
// dial in the headset and the button moves on the next frame with nothing to reopen, and a steady
// state performs zero transform writes — no write war with anything, including with itself.
//
// WHAT PARENTING BUYS, AND WHY 192's HOST COULD NOT BUY IT. Inside the window the container is part
// of that window in every sense the user means: it moves when the window is grabbed, it scales when
// the window is resized, it is occluded and released with it, it inherits the window's poke/laser
// registration (UguiPokeSurfaces registers the window's host; a child of that canvas is hit by the
// same raycaster) and it inherits the floated modals' exemption from the game's UI lock. 192 had to
// re-acquire every one of those on a host of its own, and it still did not look like part of the
// window, because it was not.
//
// ===========================================================================================
// ModBuild 197 — THE ZERO MOVES WITH THE INFORMATION. A FIXED FRAME CANNOT SIT UNDER A VARIABLE
// BLOCK OF TEXT, AND THE LOG HAD ALREADY MEASURED BY HOW MUCH.
// ===========================================================================================
//
// ModBuild 196 WORKED — "Das Flackern des Reisen-Buttons hat aufgehört." The LayoutElement opt-out
// and the pivot-placement both stand and neither is touched here. What he asked for next is a
// change of REFERENCE FRAME and nothing else:
//
//   "Die Position ist jetzt allerdings absolut FIXIERT — die verschiedenen Questinfos sind
//    unterschiedlich LANG. Ich will die offsets RELATIV ZUM UNTEREN ENDE DER QUESTINFO, so dass er
//    immer darunter liegt. Bei besonders langen Questinfos sitzt er jetzt DARÜBER."
//
// THAT IS A MEASURED FACT AND NOT AN IMPRESSION — the ModBuild 196 hardware log contains the whole
// proof, in CanvasConversion's own fit lines for this one window in one session:
//
//     Player.log:4991  union 590x742 px, bottom edge at host y = -356, target anchored (0,-155)
//     Player.log:5623  union 590x863 px, bottom edge at host y = -416, target anchored (0,-094)
//     Player.log:5670  union 590x815 px, bottom edge at host y = -392, target anchored (0,-118)
//     Player.log:5737  union 590x742 px, bottom edge at host y = -356, target anchored (0,-155)
//
// Convert each bottom edge into the WINDOW'S own local space (subtract the target's anchored y, the
// only thing that separates the two frames) and the card's painted content ends at window-local
// y = -201, -322, -274, -201.
//
// AND THE FIRST AND LAST OF THOSE ARE NOT THE INFORMATION AT ALL — THEY ARE THE BUTTON. That fit
// does not exclude the travel container the way this class does, and Player.log:4970 puts the
// button's own painted content at window-local y = -201.3..-136.3 at the user's dials. So on the
// SHORT quest the lowest thing on the card IS the button, and this class's own sweep (which excludes
// it) measures the information ending at y = -154.7. On the LONG quest the card reaches -322 with
// the button still at -201.3: 121 px = 106 mm of information hanging BELOW the button. That is the
// complaint, in numbers, from a log taken before the complaint was made — "bei besonders langen
// Questinfos sitzt er DARÜBER". The information's bottom edge travels from -154.7 to -322 across one
// session, 167 px = 146 mm real, while the window's own rect never moves at all: every one of those
// four lines carries "target frame 512x1021 px". A dial measured from that rect is measured from
// something that does not know how long the quest is, which is exactly what he is asking to change.
//
// WHAT "THE QUEST INFORMATION" RESOLVES TO, stated so it can be falsified rather than believed. It
// is the union of the ACTIVE, VISIBLE, UNCLIPPED `Graphic`s under the floated quest window, with the
// travel container itself excluded, INTERSECTED WITH THE WINDOW'S OWN RECT. Four decisions in that
// sentence, each with a reason:
//
//   GRAPHICS, NOT RECTS, because a uGUI card is full of layout groups and empty spacers whose rects
//   are far larger than anything drawn in them — the window's own 512x1021 rect being the first of
//   them. A `Graphic` is the only component that PAINTS.
//
//   THE CONTAINER IS EXCLUDED, or the answer would chase itself: the container is a CHILD of the
//   window, so a sweep that counted it would measure the button as part of the information the
//   button is supposed to sit under, and every frame would move it further down.
//
//   UNCLIPPED IS NEW AT 197 AND IT IS LOAD-BEARING FOR THE LONG QUESTS THIS BUILD IS ABOUT. The
//   information block contains an `Information/Scroll View`, and a scrolling description's Text
//   graphic is routinely far TALLER than the viewport that shows it. An unclipped sweep would take
//   the full text extent, decide the information ends hundreds of pixels below the card, and drive
//   the button off the bottom of the world — a new failure mode, appearing on exactly the long
//   quests he is complaining about. So the sweep now honours `CanvasRenderer.cull` and intersects
//   every graphic with the rect of each enabled `RectMask2D`/`Mask` between it and the window. What
//   is measured is what is VISIBLE, which is the same rule CanvasConversion's fit already applies
//   ("rejected ... 0 clipped out" on the lines above).
//
//   INTERSECTED WITH THE WINDOW'S RECT, and this one is settled by his own tuning. The raw union on
//   the ModBuild 196 line reaches x = -577 — 321 px to the LEFT of a card that is only 512 px wide,
//   from graphics faint enough that CanvasConversion's stricter fit rejects 20 of them outright. Its
//   centre is therefore x = -160, and the "suggested" line built on it asked for X = 0.094. The
//   user, tuning by eye, chose 0.244, which puts the button's content centre at x = -7.0 — the
//   CARD'S centre, 134 mm away from the raw union's. Clamp the union to the card and its centre is
//   x = 0.0 and the same suggestion becomes X = 0.251. His hand and the clamped measurement agree to
//   0.007 window heights (6 mm); his hand and the unclamped one disagree by 0.157 (134 mm). The
//   clamp is not tidiness, it is the difference between reproducing his judgement and overruling it.
//
// WHICH LAYOUT STATE IS MEASURED, because this window has two and only one of them renders. A hard
// fact from the CanvasConversion lane: a measurement taken INSIDE
// `LayoutRebuilder.ForceRebuildLayoutImmediate` reports an INFLATED subtree that is never drawn.
// This class never calls it. `Reconcile` runs once per frame from `MapRoomDriver.TickActive` in
// Update, and the sweep reads `GetWorldCorners` — the transforms uGUI left behind after the LAST
// COMPLETED layout pass, i.e. the state that was on screen in the previous frame. The placement line
// says so explicitly and prints the age of the measurement it used, so "which state was that" is
// never again a thing anybody has to work out from context.
//
// THE SOLVE IS A FIXED POINT, NOT A FEEDBACK LOOP. The anchor is `information's bottom/centre` minus
// `the container's own pivot-to-content offset`, and that second term is invariant under translation
// — moving the container moves its pivot and its painted content by exactly the same vector, which
// is what a parent translation IS. So re-solving after a write reproduces the same answer with zero
// gain; the button cannot walk. (The one input that can genuinely change is the container's internal
// layout — the label going from "Reisen" to "Quest erneut spielen" makes it wider — and that settles
// in one step.)
//
// COST: the two sweeps are ~50 graphics over ~180 transforms, and they run at most every
// `AnchorRefreshIntervalSeconds` (0.2 s), not per frame. Between refreshes the placement costs the
// same one compare it cost at 196. The information does not change faster than the quest does.
//
// ===========================================================================================
// ModBuild 196 — THE ONE-FRAME RELAPSE. THE PARENT WINDOW HAS A uGUI LAYOUT GROUP, AND SINCE 190
// IT — NOT THIS CLASS — HAS OWNED THE CONTAINER'S ANCHORS.
// ===========================================================================================
//
// "Der Reise-Knopf glitcht jede Sekunde für einen Frame an eine andere Stelle. Man sieht ihn
//  ständig kurz von seiner Stelle verschwinden, kurz weiter UNTEN erscheinen, und wieder da sein.
//  Und das in einer Schleife. Das Fenster ist vertikal GRÖSSER, weil die Stelle wo er ständig
//  hinblitzt unten ist."
//
// MEASURED, NOT INFERRED. The ModBuild 195 placement line prints, in the SAME frame and the SAME
// space, both the applied `anchoredPosition` and the union of the button's painted Graphics in
// WINDOW-LOCAL coordinates. Fifteen such samples were taken on hardware. Solve each one for the
// anchor reference point the pair implies (content = anchorRef + anchoredPosition + C, with the
// container's own content offset C = (-153.5, +30) fixed by the first sample):
//
//   * THREE samples land on (0, -510.5) = the reference for `anchorMin = anchorMax = (0.5, 0)`,
//     the anchors this class writes in `Park`. All three are the FIRST placement line of a parking
//     — the tick `Park` ran on.
//   * TWELVE samples land on (-256, +510.5) = the reference for `anchorMin = anchorMax = (0, 1)`,
//     which is `Vector2.up`. Every one of them is a later tick of the same parking. Twelve of the
//     twelve match to within 0.01 px on both axes.
//
// `anchorMin = anchorMax = Vector2.up` is the literal signature of every uGUI LayoutGroup:
// `LayoutGroup.SetChildAlongAxisWithScale` writes exactly those two lines on each of its
// `rectChildren` — AND, in the same call, writes the child's `anchoredPosition` to its layout slot.
// So the parent 'UI Quest Popup' carries a layout group, `Park`'s `SetAsLastSibling` made this
// container its LAST layout child, and from the first rebuild onwards the group owns the anchors.
//
// THAT IS THE WHOLE BUG, both halves of it:
//
//   THE FLASH. uGUI rebuilds layout in `Canvas.willRenderCanvases`, i.e. AFTER LateUpdate;
//   `Reconcile` runs from `MapRoomDriver.TickActive` in Update. On any frame the group rebuilds it
//   therefore writes LAST and the button is drawn at the group's own slot — the bottom of the
//   layout, because we made it the last sibling — and our write does not correct it until the NEXT
//   frame. One frame, at the bottom, in a loop. The loop's period is the rebuild's: among other
//   sources, CanvasConversion's re-fit calls `LayoutRebuilder.ForceRebuildLayoutImmediate` on this
//   very subtree and its own damping (`FitRefitMinIntervalSeconds`) lets that happen at most once
//   every 1.5 s — the "jede Sekunde" in the report. The ModBuild 195 log shows 146 applied re-fits
//   of this one window, every one of them a no-op that re-ran.
//
//   THE WINDOW GROWING. The floated window's capture/hit frame is the union of the host rect and
//   the MEASURED DRAWN CONTENT of the subtree (CanvasConversion.3.Fit). That sweep runs on its own
//   cadence, so it eventually samples a rebuild frame, measures the button at the bottom slot and
//   bakes it in — "the window is vertically larger because the spot it flashes to is at the
//   bottom". Fixed at the SOURCE: with the relapse gone the button is never AT that pose, so there
//   is nothing for the measurement to find. Nothing clamps the measurement.
//
// AND IT SETTLES THE CONTRADICTION ModBuild 195 COULD NOT. That header recorded two pieces of
// evidence that "disagree about where the button is": the ModBuild 194 photograph showing the plaque
// ABOVE the card's top edge, and the runtime numbers putting it 26 mm above the card's BOTTOM edge.
// Both are correct. They were sampled in different anchor states — the log line the numbers came
// from is a FIRST-placement line (anchors still ours, bottom edge), the photograph is of a steady
// frame (anchors `Vector2.up`, top edge). The 195 header's own worry that it was choosing between
// two irreconcilable measurements was really a two-state bug reporting both of its states.
//
// THE FIX, AND WHY IT IS NOT A WRITE WAR — the standing project rule.
//
//   1. THE OTHER WRITER'S AUTHORITY IS REMOVED, NOT CONTESTED. A `LayoutElement` with
//      `ignoreLayout = true` goes on the container while it is parked. `LayoutGroup`'s own
//      `CalculateLayoutInputHorizontal` skips every child that carries an `ILayoutIgnorer` with
//      that flag set — the rect never enters `rectChildren`, `SetChildAlongAxisWithScale` is never
//      called on it, and `m_Tracker.Clear()` releases the properties it had been driving. The group
//      keeps laying out its own children exactly as before; it simply stops counting one that was
//      never its child until we put it there. Recorded and restored on unpark like everything else
//      (destroyed if we added it, `ignoreLayout` written back if the container already had one).
//   2. THE ANCHORS ARE CONCEDED AND THE NUMBER IS OWNED. Even with (1) in place this class no
//      longer writes an anchor after the park: the wanted pose is a window-local POINT and the
//      applied `anchoredPosition` is derived from whatever anchor the rect is carrying this tick.
//      A second writer that re-anchors the container therefore cannot move the button at all — it
//      changes a number this class recomputes — so there is no state left for two writers to
//      alternate over. (`Park` still sets the anchors once, to `Vector2.up`: the value the container
//      has actually carried in every rendered frame since 190, so 0/0 writes `Vector2.zero` and the
//      log line reads exactly as 190's did.)
//   3. IT IS PROVEN, NOT ASSUMED. A drift watch compares the rect against what we last wrote on
//      every tick and reports the COUNT OF COMPARISONS alongside the count of deviations, the
//      largest one in real millimetres, and the name of the layout component found on the parent.
//      "0 deviations" and "never ran" can therefore never look the same — see `ReportDrift`.
//
// ===========================================================================================
// THE ONE-FRAME FLASH — THE 193 FIX IS KEPT, WITH ONE OF ITS TWO MECHANISMS RETIRED ON PURPOSE
// ===========================================================================================
//
// "Außerdem flackert er, für einen Frame sieht man ihn an seiner alten position."
//
// 191 flashed because `Park` wrote `anchoredPosition = Vector2.zero` and the solve only ran later,
// so the container drew one frame at zero before being moved. THAT MECHANISM IS GONE BY
// CONSTRUCTION, because zero is now the WANTED pose: `Park` writes the dialled offset itself, in
// `Park`, before anything can render, so there is no such thing as an unsolved frame any more.
//
// THE HOLD-DOWN IS KEPT ANYWAY, and it is the rule that makes the guarantee total rather than
// probable: a CanvasGroup on the container (added by us if it has none), alpha 0 and blocksRaycasts
// off, applied BEFORE the `SetParent` — so not even the frame of the reparent is visible — and
// re-applied whenever the game has the container inactive, so the steady hidden state is also
// alpha 0. It is released only once the container is ACTIVE and a pose HAS BEEN WRITTEN for the
// current window rect. The one case that can still fail the second test is a window whose rect has
// no height yet (a uGUI layout is not final on the frame a window is floated): with a non-zero dial
// the offset would be computed against a zero reference and land in the wrong place, so that tick
// writes NOTHING and the hold stays down instead.
//
// WHY THIS IS NOT A WRITE WAR — the failure this project has already paid for once. The game owns
// `travelOptions.activeSelf`; this class NEVER writes it and only reads it as a signal. What this
// class writes is a CanvasGroup that the game does not use: `AdventureMapUIManager` contains no
// CanvasGroup reference at all (grep it — travelOptions is toggled with SetActive and nothing else),
// and CanvasConversion's own "force the restored 2D window hidden" path takes a CanvasGroup off the
// WINDOW ROOT with GetComponent, never off a child. So there is no second writer to alternate with.
// The write is level-triggered against our own `_hidden` flag, so a steady state writes nothing.
// A prefix/postfix on `EnableTravelOptions` was deliberately NOT added: it would make us a second
// owner of a piece of state the game writes every time a location is selected, which is exactly the
// write war, and it would buy nothing the hold-down does not already give.
//
// BOUNDED. If a pose can never be written, the button is revealed anyway after ShowDeadlineSeconds
// with a Warn: a confirm button that never appears is a worse failure than one that appears in the
// wrong place — the same ruling the modal reveal gate carries.
//
// ===========================================================================================
// ModBuild 195 — WHY THE Y DEFAULT IS STILL 0, AND WHAT WAS SHIPPED INSTEAD
// ===========================================================================================
//
// "Immer noch ist der Bestätigungsknopf in der Questinfo an der falschen Stelle. Siehe
//  bestätigungsknopf.jpg - er soll direkter UNTER der info sein."
//
// A DERIVED, NON-ZERO DEFAULT WAS CONSIDERED AND DELIBERATELY NOT SHIPPED, because the two pieces
// of evidence available DISAGREE ABOUT WHERE THE BUTTON IS, and a default computed from either one
// alone would be a guess with a number attached — which is precisely how 191, 192 and 193 each
// produced a confidently wrong placement.
//
//   THE PHOTOGRAPH (bestsätigungsknopf.jpg, ModBuild 194, 3840x2160) shows the "Quest erneut
//   spielen" plaque clear ABOVE the parchment card's top edge: plaque centred near y = 332 of 1125
//   displayed rows, card top near y = 375, card bottom near y = 950.
//
//   THE RUNTIME NUMBERS FROM THE SAME SESSION SAY THE OPPOSITE. Player.log:8705 gives the window's
//   world rect (bottom edge at world y = 56.477, top at 233.470) and Player.log:8682 gives the
//   button's world position (y = 61.68) — 5.2 world units, about 26 mm real, ABOVE the window's
//   BOTTOM edge. And CanvasConversion's own host-rect fit for the same window (Player.log:8718)
//   measures the card's visible-Graphic union as (-333,-496)..(257,526) in window-local px, i.e. the
//   information ENDS at y = -496, only ~15 px below the button's own content (which the travel
//   container's sweep puts at window-local y = -480.5 .. -415.5). By THAT measurement the button is
//   already directly under the information, which the user says it is not.
//
// ModBuild 196 SETTLED IT: BOTH ARE RIGHT, AND THAT WAS THE BUG. The two numbers were sampled in
// two different anchor states of the same container — the runtime line is a FIRST-placement line,
// taken on the one tick per parking on which the anchors are still the ones Park writes, while the
// photograph is of a steady frame, in which the parent window's layout group had long since
// re-anchored the container to Vector2.up and moved it one whole window height up. Neither
// measurement was wrong and neither instrument was lying; the SUBJECT had two states. See the
// ModBuild 196 block above. The three things 195 shipped to decide it all still stand and all still
// help, so they are kept:
//
//   1. THE Y CLAMP IS WIDENED to -1.5 (see OffsetLimitYMin) so the target is REACHABLE whichever
//      account is true. If the photograph is right, reaching below the card needs roughly a whole
//      window height of downward travel and the old -0.5 stop could not deliver it — he could have
//      turned the dial to its limit and still not arrived, which on its own would explain a fourth
//      report of "still wrong" from a user who was handed working dials.
//   2. THE LOG NOW PRINTS THE BUTTON AGAINST BOTH WINDOW EDGES in real millimetres, so "above the
//      card" and "at the card's bottom" stop being compatible readings of one number.
//   3. THE LOG COMPUTES AND PRINTS THE TWO DIAL VALUES that put the button directly under the
//      information — from the button's painted union AND the window's painted union measured in the
//      SAME frame and the SAME space (see Recommendation). That is the derivation, delivered as two
//      numbers to type rather than as a pose that is applied.
//
// 0 THEREFORE STILL MEANS EXACTLY THE ModBuild 190 POSE, his first instruction is not silently
// reversed, and the moving is still his. If the next log's "suggested:" line is stable across
// several quests, promoting it to Defaults.TravelButtonOffsetYWindowHeights is a one-constant change
// with a measurement behind it — which is the bar this file has failed three times.
//
// ===========================================================================================
// THE MEASUREMENT THAT SURVIVED, AS EVIDENCE ONLY
// ===========================================================================================
//
// The Graphic-union sweep 191/193 solved from is still here and still runs — but ONLY to fill in the
// log line, and NOTHING it returns can reach `anchoredPosition`. It is kept because the next
// hardware report has to be able to say WHERE THE BUTTON ACTUALLY IS, in millimetres, for a given
// pair of dial values; a report that says "still wrong" against a placement nobody can measure is
// how this file got three rejected builds. It is also computed only on the ticks that actually log.
//
// ===========================================================================================
// RESTORE DISCIPLINE
// ===========================================================================================
//
// The container's home is recorded ONCE, before the first move: parent, sibling index, anchorMin,
// anchorMax, pivot, anchoredPosition, localRotation, localScale — plus the CanvasGroup's alpha and
// blocksRaycasts and the LayoutElement's ignoreLayout if it had them, or the fact that we added
// them (both are DESTROYED again on unpark when they were ours). All of it is written back verbatim on
// unpark, on stand-down and on teardown, and the transform half only WHILE THE OBJECT IS STILL
// PARENTED UNDER OUR HOST: the game re-parents its own UI freely, and taking an object back from
// wherever it has since put it is the write war again. THE HOLD IS RELEASED UNCONDITIONALLY though,
// even when the transform restore is skipped — leaving the game's own button at alpha 0 in its own
// HUD would be the worst outcome of all.
//
// DEGRADES SAFELY: every private member is resolved through AccessTools once. If any is missing —
// or the container/window is not a RectTransform — ONE Warn names the consequence (the shortcut
// stays live / the button stays unreachable) and the class stands down. Nothing thrown, nothing on
// the wire, no rule library touched, no Harmony patch beyond the one prefix that has shipped since
// 190.
// ---------------------------------------------------------------------------

/// <summary>
/// Makes the game's own travel confirmation reachable in the 3D map room — parked INSIDE the floated
/// quest window at exactly the ModBuild 190 pose, movable from there with two live
/// <c>[WorldUI] TravelButtonOffset*WindowHeights</c> dials — and switches off the single-player
/// double-click shortcut that committed without asking. Installed by <see cref="MapRoomDriver"/>.
/// </summary>
internal static class MapTravelConfirm
{
    private const string Scope = "MapRoom";

    private static bool _installed;
    private static bool _resolved;

    /// <summary>The WHOLE feature stands down: no parking AND no shortcut gate. Set only when the
    /// reflection the gate itself needs is missing, because a gate that cannot identify the staged
    /// location must not guess. The consequence — the double-press shortcut comes back — is named
    /// in the Warn that sets it.</summary>
    private static bool _standDown;

    /// <summary>Only the PARKING stands down (the container is not a RectTransform, or the game took
    /// it back). The shortcut gate keeps running, because "travel must not commit by accident" is a
    /// standing ruling that does not depend on the button being reachable — and with the gate up, an
    /// unreachable button is a missing convenience rather than an accidental journey. Cleared when
    /// the room stands down, so re-entering the map room retries.</summary>
    private static bool _parkStandDown;

    private static FieldInfo? _locationToTravel;
    private static FieldInfo? _onConfirmCallback;
    private static FieldInfo? _travelOptions;
    private static FieldInfo? _travelButton;

    // ---- ModBuild 226: THE ONLINE CONFIRM IS A DIFFERENT OBJECT, AND IT IS THE SAME ACT ----------
    //
    // USER REPORT (2026-08-22), verbatim: "Im Test waren der 'Quest Beginnen'-Button und das
    // Quest-Fenster separate 'Fenster' - nicht wie zuvor wie gewollt, dass der Button auf der Quest
    // angezeigt wird. Siehe Button_getrennt.jpg."
    //
    // THE ModBuild 225 LOG SHOWS THIS CLASS WORKING AND STILL PRODUCING HIS SCREENSHOT, which is the
    // whole point of checking before writing: "MAP TRAVEL CONFIRM placement — INSIDE the quest window
    // 'UI Quest Popup', offset from 'directly under the quest information' by the two dials" appears
    // four times, the drift watch reports CLEAN, and no refusal line is printed. The reconciliation
    // is not gated out and it is not being undone. It simply parks THE WRONG OBJECT ONLINE.
    //
    // WHAT THE GAME DOES. Offline, the confirm is 'Adventure button' inside the container held by
    // AdventureMapUIManager's private `travelOptions` field — that is what this class has parked
    // since ModBuild 190. ONLINE, MapChoreographer.InitializeSelectQuestReadyUp hands the same act to
    // a completely different object (decompiled MapChoreographer.cs:3575-3600): it initialises
    // Singleton<UIReadyToggle> with the labels "GUI_SELECT_QUEST" ("Quest wählen") / "GUI_CANCEL",
    // with AdventureMapUIManager.CheckTravel as its validator and, in the host branch,
    // AdventureMapUIManager.ConfirmTravel() as its all-players-ready callback. So online the confirm
    // is 'Multiplayer Ready Toggle' — a 307x65 UIWindow of its own, a ROOT under 'Campaign Canvas'
    // with no ancestor window (the ModBuild 225 WINDOW IDENTITY line says exactly that) — and the
    // catch-all floated it as a window in its own right, next to the quest card.
    //
    // THIS CLASS THEREFORE RESOLVES WHICH CONTAINER TO PARK instead of assuming there is one. The
    // parking, the measured ModBuild 197 zero, the two [WorldUI] TravelButtonOffset dials, the drift
    // watch and the verbatim restore are all UNCHANGED and shared — the lesson is to feed the real
    // machinery, not to build a second one that looks like it.

    /// <summary>
    /// <c>UIReadyToggle.readyUpToggleState</c> — which ready-up the singleton toggle is currently
    /// serving (decompiled UIReadyToggle.cs:106). The toggle is a SINGLETON reused for city events,
    /// rewards, retirement and town records as well, so parking it on the quest card is only correct
    /// while this field reads <c>Quests</c>. OPTIONAL: if the field cannot be found the online park
    /// stands down and the offline container is used, which is exactly the pre-226 behaviour.
    /// </summary>
    private static FieldInfo? _readyToggleState;

    /// <summary>The GameObject actually parked right now — <see cref="_travelOptions"/>'s container
    /// offline, the ready toggle online. Recorded because <see cref="Unpark"/> must restore THE
    /// OBJECT IT MOVED: re-deriving it from the manager field (which is what 190…225 did, correctly,
    /// when there was only ever one candidate) would restore the wrong object's transform and leave
    /// the moved one parented under a host that is about to disappear.</summary>
    private static GameObject? _parked;

    /// <summary>Is <see cref="_parked"/> the multiplayer ready toggle? Governs two things and
    /// nothing else: the hold-down is skipped for it (its CanvasGroup is its own UIWindow's fade
    /// target — holding it would be a write war with the game's tween, and this project does not win
    /// those), and its shown-state is read from <c>UIReadyToggle.IsVisible</c> rather than from
    /// <c>activeInHierarchy</c>, because a UIWindow hide need not deactivate the object.</summary>
    private static bool _parkedIsReadyToggle;

    // ---- the placement dials' bounds -------------------------------------------------------------
    //
    // The clamp lives HERE and the bind site in WorldUIConfig reads these constants, so the range
    // the code enforces and the range the config browser advertises can never drift apart. See the
    // class doc for the millimetre figures and for why nothing inside these bounds is unreachable.

    /// <summary>Sideways travel of the confirm button, in fractions of the quest window's own
    /// HEIGHT, either way from the QUEST INFORMATION'S horizontal centre (ModBuild 197 moved the
    /// zero there; through 196 it was the window rect's left edge). ±0.25 ≈ ±223 mm at the measured
    /// rig scale, and the card is 512 px = 0.2507 window heights wide, so this range reaches the
    /// card's left edge and its right edge and essentially nothing beyond: every sideways position
    /// ON the card is expressible and no value walks the button off the side.
    ///
    /// <para>TIGHTENED AT ModBuild 197 FROM ±0.50. The old range was half a card wider on each side
    /// because the zero used to be the card's LEFT EDGE and the dial had to carry the button across
    /// the whole card before it could be trimmed. With the zero on the information's centre that
    /// journey is already made.</para></summary>
    internal const float OffsetLimitX = 0.25f;

    /// <summary>
    /// Lowest vertical offset, in fractions of the window's height, measured UP from the QUEST
    /// INFORMATION'S BOTTOM EDGE — the moving zero ModBuild 197 introduced, where 0 puts the top of
    /// the button's painted content exactly on the bottom of the information's. -0.6 ≈ 536 mm below
    /// that edge.
    ///
    /// <para>TIGHTENED AT ModBuild 197 FROM -1.5, WHICH IS THE OPPOSITE OF WHAT 195 DID AND FOR THE
    /// SAME REASON. 195 widened this bound to -1.5 so the button could REACH the information from a
    /// zero that sat above the card's top edge, and that is what made the user's -0.726 possible at
    /// all. From 197 the zero IS the information's bottom edge, so the whole journey the widened
    /// bound paid for is already made and the rest of it is dead travel a tuner can get lost in.
    /// MEASURED: on the ModBuild 196 hardware line the information's bottom edge sat 355 px above
    /// the card's own bottom edge, so -0.35 already lands the button's top on the card's bottom
    /// edge and -0.60 carries it 226 mm clear below the card. Nothing anybody has argued for since
    /// ModBuild 190 lies outside that.</para>
    ///
    /// <para>STILL BOUNDED, and by the same argument the class doc makes for every value in the box:
    /// the container never stops being a CHILD of the quest window, so at -0.6 it is 536 mm below
    /// the information and still moving, scaling, occluding and going home with the card, still hit
    /// by the raycaster that already hits the window, and still carrying blocksRaycasts whenever it
    /// is visible. No value of this dial removes the button, changes what it does, or touches game
    /// state.</para>
    /// </summary>
    internal const float OffsetLimitYMin = -0.6f;

    /// <summary>Highest vertical offset, same frame and unit. 0 is the quest information's BOTTOM
    /// edge, so +0.6 ≈ 536 mm above it — back up over the information, past its middle on every
    /// quest measured so far.</summary>
    internal const float OffsetLimitYMax = 0.6f;

    // ---- the other constants ---------------------------------------------------------------------

    /// <summary>
    /// Alpha below which a Graphic is not counted as visible CONTENT in the content sweeps. uGUI
    /// bars routinely carry a full-width fully transparent Image as a raycast blocker, and reporting
    /// the whole HUD bar's width as "what the player sees" would make both the evidence line and the
    /// ModBuild 197 zero wrong in the same direction.
    /// </summary>
    private const float MinVisibleAlpha = 0.02f;

    /// <summary>
    /// Seconds between re-measurements of the ModBuild 197 anchor (the information's bottom edge and
    /// centre, and the container's own pivot-to-content offset). NOT per frame: the two sweeps walk
    /// ~180 transforms and the thing they measure changes when the QUEST changes, not when the frame
    /// does. A fifth of a second is far below the time it takes a window to re-fit and far above the
    /// frame rate, so the button follows a new quest visibly at once and costs nothing in between.
    /// The first tick after a park always measures, whatever this says.
    /// </summary>
    private const float AnchorRefreshIntervalSeconds = 0.2f;

    /// <summary>How far the dialled offset must move before it is written (container-local uGUI
    /// units). Sub-pixel churn is not worth a transform write, and a rect rewritten every frame is
    /// indistinguishable from a write war in a log.</summary>
    private const float OffsetEpsilon = 0.5f;

    /// <summary>
    /// Hard bound on how long the button may stay held down after the game showed it while no pose
    /// can be written (a window rect with no height). A confirm button that never appears is a worse
    /// failure than one that appears in the wrong place.
    /// </summary>
    private const float ShowDeadlineSeconds = 0.5f;

    /// <summary>Seconds between placement log lines. The FIRST placement always logs; after that only
    /// a materially different offset does, and never more often than this. Short on purpose — the
    /// user tunes the two dials BY HAND from this line, so a new value has to show up while he still
    /// remembers turning it. A steady state logs nothing at all, because an unchanged offset is never
    /// a reason to print.</summary>
    private const float ResolveLogIntervalSeconds = 1.5f;

    /// <summary>
    /// Seconds between DRIFT WATCH lines. Long on purpose: this instrument answers "is anything
    /// still writing this rect behind us", and that question is answered by a COUNT over many
    /// frames, not by a per-frame trace. The first line of a parking always prints — even with
    /// nothing to report — because "0 deviations in 340 comparisons" and "the watch never ran" must
    /// never look the same in a log.
    /// </summary>
    private const float DriftReportIntervalSeconds = 8f;

    /// <summary>Graphic sink for the content sweeps — reused, so it allocates nothing.</summary>
    private static readonly List<Graphic> ContentGraphics = new(32);

    /// <summary>World-corner scratch for the same sweep.</summary>
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    /// <summary>A SECOND corner scratch, for the clip rects of the mask ancestors. It cannot share
    /// <see cref="CornerScratch"/>: the graphic's own corners are still being folded into the union
    /// while the mask walk runs, and one buffer for two readers is the aliasing bug this repo has
    /// already shipped once in a blit chain.</summary>
    private static readonly Vector3[] MaskCornerScratch = new Vector3[4];

    // ---- the park record (written ONCE, before the first move) ---------------------------------

    private static UIWindow? _host;
    private static bool _homeRecorded;

    /// <summary>The container the home record below belongs to (ModBuild 226 — there are two
    /// possible containers now, see <see cref="_parkedIsReadyToggle"/>).</summary>
    private static GameObject? _homeOwner;

    private static Transform? _optionsHome;
    private static int _optionsHomeIndex;
    private static Vector2 _homeAnchorMin;
    private static Vector2 _homeAnchorMax;
    private static Vector2 _homePivot;
    private static Vector2 _homeAnchoredPos;
    private static Quaternion _homeLocalRotation = Quaternion.identity;
    private static Vector3 _homeLocalScale = Vector3.one;

    // ---- the layout seam (ModBuild 196) ---------------------------------------------------------
    //
    // The one component that takes the container out of the parent window's layout group entirely.
    // See the ModBuild 196 block in the class doc for the fifteen hardware samples that proved a
    // layout group owns this rect; the point of this member is that the fix is a REMOVAL of the
    // other writer's authority, not a race against it.

    private static LayoutElement? _layoutIgnore;
    private static bool _layoutIgnoreAdded;
    private static bool _layoutIgnoreHome;

    /// <summary>What the parent window carries that could drive this rect, resolved once per park
    /// and printed in the diagnostics — so a future report names the writer instead of implying
    /// one.</summary>
    private static string _layoutOwner = "<not resolved>";

    // ---- the drift watch (ModBuild 196) ---------------------------------------------------------

    /// <summary>The pose this class last WROTE OR VERIFIED, and the anchors it was measured
    /// against. Compared on the next tick, before anything is written, so a foreign write in
    /// between is caught rather than silently overwritten.</summary>
    private static Vector2 _wrotePos;
    private static Vector2 _wroteAnchorMin;
    private static Vector2 _wroteAnchorMax;
    private static Vector2 _wrotePivot;
    private static bool _wroteValid;

    private static int _watchComparisons;
    private static int _watchPosDeviations;
    private static int _watchAnchorDeviations;
    private static int _watchPivotDeviations;
    private static float _watchWorstDeviation;
    private static Vector2 _watchWorstActual;
    private static Vector2 _watchWorstExpected;
    private static float _watchNextReportAt = float.PositiveInfinity;
    private static bool _watchReportedOnce;

    // ---- the hold-down that replaces 192's render-hidden host ----------------------------------

    private static CanvasGroup? _hold;
    private static bool _holdAdded;
    private static float _holdHomeAlpha = 1f;
    private static bool _holdHomeBlocksRaycasts = true;
    private static bool _hidden;

    // ---- the moving zero (ModBuild 197) ----------------------------------------------------------
    //
    // Everything below is the answer to "where is the bottom of the quest information THIS quest",
    // carried between ticks so the two sweeps do not have to run per frame. See the ModBuild 197
    // block in the class doc for what the subject is, why it is clipped and frame-clamped, and why
    // re-solving it after a write reproduces the same answer instead of walking.

    /// <summary>The window-local point the container's PIVOT sits on when both dials are 0 — i.e.
    /// the pose at which the button's painted content has its TOP edge on the information's BOTTOM
    /// edge and is horizontally CENTRED on it.</summary>
    private static Vector2 _anchorPivot;

    /// <summary>False until the two sweeps have both succeeded at least once for this parking. While
    /// it is false NO POSE IS WRITTEN and the hold-down keeps the button off screen — the same rule
    /// a window with no height already had, and bounded by the same reveal deadline.</summary>
    private static bool _anchorValid;

    private static float _anchorNextRefreshAt;
    private static float _anchorResolvedAt = float.NegativeInfinity;

    // The measurement the live anchor was solved from, kept so the placement line describes the
    // SAME numbers the pose was computed from rather than a fresh sweep taken later in the tick.
    private static Rect _anchorInfoRaw;
    private static Rect _anchorInfo;
    private static Rect _anchorButton;
    private static int _anchorInfoCounted;
    private static int _anchorInfoSkipped;
    private static int _anchorInfoClipped;
    private static int _anchorButtonCounted;
    private static int _anchorButtonSkipped;
    private static int _anchorButtonClipped;

    /// <summary>Why the last refresh did not produce an anchor, or "resolved". Printed, so "the zero
    /// could not be measured" and "the zero is stale" are different lines in a log.</summary>
    private static string _anchorWhy = "never measured yet";

    /// <summary>How far the information's bottom edge moved the last time the anchor changed, in
    /// window-local units — the number that makes "it follows the quest length" falsifiable.</summary>
    private static float _anchorLastMoveY;

    /// <summary>One-shot per session: the dials' zero changed meaning at ModBuild 197 and any tuned
    /// value from an earlier build is now measured from somewhere else.</summary>
    private static bool _zeroNoticeLogged;

    // ---- placement state ------------------------------------------------------------------------

    /// <summary>A pose has been written for the current window rect. This — and NOT any content
    /// measurement — is what releases the hold-down.</summary>
    private static bool _posed;

    private static bool _wasActive;
    private static float _activeSince;
    private static bool _revealForcedLogged;

    private static bool _placementLogged;
    private static float _placementLoggedAt = float.NegativeInfinity;
    private static Vector2 _loggedOffset;

    /// <summary>The DIAL VALUES the last printed placement line reported, so the next line can state
    /// what a turn of a dial actually DID — the before, the after and the difference in real
    /// millimetres. Kept separately from <see cref="_loggedOffset"/> because the two can move
    /// independently: a dial change of 0.0002 is a real change the user made and must be reported,
    /// while a window that has been resized moves the applied offset with the dials standing
    /// still.</summary>
    private static Vector2 _loggedDials;
    private static bool _parkWarned;
    private static bool _reported;

    /// <summary>Register the prefix exactly once. Called from the map room's engage path rather
    /// than from WorldUIModule so the two lanes that own that file cannot collide over it.</summary>
    internal static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        VRSession.Harmony?.PatchAll(typeof(TravelShortcutGate));
        // The journey the confirm button starts. Installed from HERE for the same reason this class
        // is installed from the map room's engage path rather than from WorldUIModule — that file is
        // owned by other lanes — and because its prefixes must be live before the first Reisen press
        // can reach MapChoreographer.StartMove. Idempotent, exactly like this method.
        MapPartyTravel.Install();
        // ModBuild 232 — THE MULTIPLAYER HALF OF THE SAME CONFIRM. Same reasoning again: its postfixes
        // must be live before the host's first SelectQuest can reach a client, and WorldUIModule is
        // owned by other lanes. Idempotent. See MapQuestReadyUp for the seam and the log lines.
        MapQuestReadyUp.Install();
        // ModBuild 423 — the icon row that says WHO has already confirmed, and the read-only observer
        // that reports what the game did with a press. Installed here for the third time for the same
        // reason; idempotent through Harmony's own per-type patch bookkeeping. See
        // MapQuestReadyRoster for the user request and the source citations.
        VRSession.Harmony?.PatchAll(typeof(MapQuestReadyPress));
        VRLog.Info(Scope, "MAP TRAVEL CONFIRM installed — the single-player 'click the same location "
                          + "twice and go' shortcut is switched off while the 3D map room stands (the "
                          + "game itself switches it off online, so this is its own behaviour and not an "
                          + "invention), and the game's real Reisen/Abbrechen buttons are parked INSIDE "
                          + "the floated quest window DIRECTLY UNDER THE QUEST INFORMATION — since "
                          + "ModBuild 197 that is a MEASURED, MOVING reference (the bottom edge and "
                          + "horizontal centre of what the card is actually painting, re-read four "
                          + "times a second), so the placement holds for a short quest and a long one "
                          + "alike. Movable from there with the live [WorldUI] "
                          + "TravelButtonOffsetXWindowHeights / …YWindowHeights dials, whose ZERO IS "
                          + "THAT POINT — any value tuned against ModBuild 196 or earlier was measured "
                          + "from the window's fixed top-left corner instead and has to go back to 0. "
                          + "Travel now commits through that button and through nothing else.");
    }

    /// <summary>
    /// Level-triggered, one call per tick from <see cref="MapRoomDriver"/>. Parks the travel options
    /// inside <paramref name="questWindow"/> while that window is floated, keeps their offset equal
    /// to the two live dials, and hands them back otherwise. Idempotent: a steady state costs two
    /// config reads, one compare and no writes at all.
    /// </summary>
    /// <param name="questWindow">
    /// The floated quest window, or null. THE PLACEMENT IS A PROPERTY OF THIS WINDOW — user ruling:
    /// "Der 'Quest erneut spielen' Button soll teil des Fensters sein". Without it there is nothing
    /// to be part of, so the container goes home.
    /// </param>
    internal static void Reconcile(UIWindow? questWindow)
    {
        // ModBuild 232 — THE TWO THINGS THAT HAVE TO HAPPEN ON EVERY TICK, WHATEVER THIS CLASS DOES
        // WITH THE CONTAINER. They are bracketed around the reconciliation rather than woven into it
        // for one reason: the body below has six early returns, and a claim that has to be released
        // on each of them would be six chances to forget one. Here there is exactly one call site for
        // each, and the state they read (`_parked`, `_host`, `_parkStandDown`) is whatever the body
        // left behind.
        //
        // FIRST, before anything else: answer any quest-selected prompt the game raised on this
        // client. It runs here — from MapRoomDriver.TickActive, i.e. from Update — and not in the
        // Harmony postfix that captured it, because MapChoreographer.ProxySelectedLocation wraps its
        // whole body in a catch that unloads the scene and returns to the main menu. See
        // MapQuestReadyUp's block comment.
        MapQuestReadyUp.TickPendingClientPrompt();
        ReconcileCore(questWindow);
        // AND LAST: re-assert the park claim with the live answer. A LEVEL, not a latch — see
        // ReadyToggleParkClaim. The toggle handed over as the INTENT is resolved without the
        // visibility test on purpose, so the claim is in force before the game shows the toggle and
        // the float gate has an answer the first time it asks.
        UIReadyToggle? confirm = QuestConfirmToggle();
        MapQuestReadyUp.TickClaim(confirm != null ? confirm.gameObject : null,
                                  confirm != null && confirm.IsVisible,
                                  _parkedIsReadyToggle ? _parked : null,
                                  _parkedIsReadyToggle ? _host : null,
                                  questWindow != null,
                                  _standDown || _parkStandDown);
        // AND THE ROW OF ICONS ABOVE IT (ModBuild 423). Fed the SAME two references the claim gets,
        // so the three of them can never disagree about what is parked where. It is ticked LAST
        // because it hangs off the confirm's placed top edge: reading a pose that has already been
        // written this frame is what keeps the pair one block instead of two chasing each other.
        // Offline `_parkedIsReadyToggle` is false, the row is released and MapQuestReadyRoster
        // .ReservedHeight() returns 0, so the single-player placement is bit-identical to 422.
        MapQuestReadyRoster.Tick(ReadyRosterSite.Quest,
                                 questWindow,
                                 _parkedIsReadyToggle ? _parked : null,
                                 _posed);
    }

    /// <summary>The reconciliation proper — see <see cref="Reconcile"/>, which brackets it with the
    /// two per-tick duties that must run whichever early return this takes.</summary>
    private static void ReconcileCore(UIWindow? questWindow)
    {
        if (_standDown)
            return;
        if (!EnsureReflection())
            return;

        AdventureMapUIManager? mgr = Manager();
        GameObject? options = ResolveContainer(mgr, out bool readyToggle);

        if (!MapRoomDriver.Active || questWindow == null || options == null)
        {
            // ModBuild 422 — the ONLINE no-container case is its own state and gets its own name.
            // Before this build it was reported as "the game's travel options are gone", which was
            // true of an object that online is never the confirm in the first place. See
            // ResolveContainer's class-doc paragraph and NoteOnlineConfirmMissing.
            bool onlineStandBy = MapRoomDriver.Active && questWindow != null && FFSNetwork.IsOnline;
            if (onlineStandBy)
                NoteOnlineConfirmMissing(questWindow!);
            Unpark(!MapRoomDriver.Active ? "map room stood down"
                : questWindow == null ? "no quest window is floated"
                : onlineStandBy
                    ? "online, and the game is not showing the multiplayer quest confirm yet"
                    : "the game's travel options are gone");
            // Leaving the room clears a park stand-down: whatever went wrong was about THIS visit's
            // objects, and re-entering rebuilds all of them.
            if (!MapRoomDriver.Active)
                _parkStandDown = false;
            return;
        }
        if (_parkStandDown)
            return;

        // THE CONTAINER IS PART OF THE PARKING IDENTITY (ModBuild 226), not only the window. Going
        // online swaps the offline travel container for the ready toggle and going offline swaps it
        // back, and either way the object we are HOLDING must be handed home before the other one is
        // taken — otherwise the first one stays parented into a window it is no longer the confirm
        // for. Level-triggered like everything else here: a steady state is one reference compare.
        if (!ReferenceEquals(_host, questWindow) || !ReferenceEquals(_parked, options))
        {
            Unpark(!ReferenceEquals(_host, questWindow)
                ? "a different quest window took over"
                : "the game switched which object IS the travel confirm (online ⇄ offline)");
            if (!Park(questWindow, options, readyToggle))
                return;
        }

        // OWNERSHIP, EVERY TICK. The game re-parents its own UI freely, and re-taking an object from
        // wherever it has since put it is the write war this project has already lost once. If it is
        // no longer our child, hand it over and stand the parking down for this visit.
        if (options.transform.parent != questWindow.transform)
        {
            VRLog.Warn(Scope, $"MAP TRAVEL CONFIRM: '{options.name}' is no longer parented under the "
                              + $"floated quest window '{questWindow.name}' — the game moved it. Handing "
                              + "it back (home transform restored verbatim, our hold-down released) and "
                              + "standing the parking down for this map-room visit. CONSEQUENCE: the "
                              + "Reisen / 'Quest erneut spielen' button is unreachable in the room until "
                              + "the room is re-entered; the double-press shortcut stays switched off, so "
                              + "nothing can commit travel by accident in the meantime.");
            Unpark("the game re-parented the travel options");
            _parkStandDown = true;
            return;
        }

        // The game owns activeSelf (EnableTravelOptions -> travelOptions.SetActive). We only READ it.
        // ModBuild 226: the ready toggle is shown and hidden through its own UIWindow, which need not
        // deactivate the GameObject at all (UIWindow only does that when its serialized
        // m_DisableOnZeroAlpha is set), so for that container the shown-state is the window's —
        // UIReadyToggle.IsVisible is literally `window.IsOpen` (decompiled UIReadyToggle.cs:138).
        // Reading activeInHierarchy for it would report "shown" forever and the confirm would stand
        // on the quest card after the game had taken it away.
        bool active = options.activeInHierarchy && (!_parkedIsReadyToggle || ReadyToggleVisible());
        if (active != _wasActive)
        {
            _wasActive = active;
            _activeSince = Time.unscaledTime;
            if (active)
            {
                _revealForcedLogged = false;
                // THE ONE EVENT WORTH JUMPING THE CADENCE FOR (ModBuild 197). The container's own
                // painted content cannot be measured while the game has it switched off, so the
                // measurement that was standing while it was hidden is the stalest one there is —
                // and this is the exact instant the player is about to look at the button. Re-measure
                // on this tick rather than up to 0.2 s into it.
                _anchorNextRefreshAt = float.NegativeInfinity;
            }
        }

        PlaceButton(questWindow, options, mgr);
        TickVisibility(active);

        if (!_reported && _posed)
        {
            _reported = true;
            var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
            VRLog.Info(Scope, "MAP TRAVEL CONFIRM: the game's travel options were moved from "
                              + $"'{(_optionsHome != null ? _optionsHome.name : "<none>")}' INTO the floated "
                              + $"quest window '{questWindow.name}', placed DIRECTLY UNDER THE QUEST "
                              + "INFORMATION (user ruling, ModBuild 197: \"Ich will die offsets relativ "
                              + "zum unteren Ende der Questinfo, so dass er immer darunter liegt\"), "
                              + "offset from there only by the two [WorldUI] TravelButtonOffset "
                              + "dials — both 0 by default, which is that placement exactly. The button itself "
                              + $"is '{(_parkedIsReadyToggle ? options.name : btn != null ? btn.name : "<not found>")}' "
                              + (_parkedIsReadyToggle
                                  ? "— the game's MULTIPLAYER quest confirm ('Quest wählen', "
                                    + "GUI_SELECT_QUEST). ONLINE this toggle is what commits the journey: "
                                    + "MapChoreographer.InitializeSelectQuestReadyUp wires its all-ready "
                                    + "callback to AdventureMapUIManager.ConfirmTravel and its gate to "
                                    + "CheckTravel, so it is the same act the offline button performs and "
                                    + "it belongs in the same place. Before ModBuild 226 it floated as a "
                                    + "SEPARATE VR window beside the quest card, which is the split in "
                                    + ".planning/debug/Button_getrennt.jpg."
                                  : "— the SAME ExtendedButton the flat game uses, so its label (Reisen / "
                                    + "Quest erneut spielen), its interactable state and every guard behind "
                                    + "OnTravelButtonClick are the game's own.")
                              + " It moves, scales, occludes and goes home with the window.");
        }
    }

    /// <summary>Teardown — hand the container back before the room disappears under it, and let go of
    /// the park claim in the same breath: a refusal that outlived the room would keep the game's own
    /// confirm off screen with nothing left to park it into.</summary>
    internal static void Reset()
    {
        Unpark("map room teardown");
        MapQuestReadyUp.Reset();
        MapQuestReadyRoster.Reset();
    }

    /// <summary>
    /// The singleton ready toggle WHILE IT IS SERVING THE QUEST READY-UP, whether or not it is on
    /// screen; null otherwise. Two callers with two different follow-up tests, which is why the
    /// visibility test is NOT in here:
    ///
    /// <list type="bullet">
    /// <item><see cref="ResolveContainer"/> adds <c>IsVisible</c>, because parking a toggle the game
    /// is not drawing would leave the offline container unparked for nothing.</item>
    /// <item>The park CLAIM does not, because a claim only ever says "if this object floats, refuse
    /// it — it is mine", and an invisible window never floats. Asking it earlier is what lets the
    /// claim be in force before the game's own <c>Show</c>, which is the whole race (see
    /// <see cref="MapQuestReadyUp"/>).</item>
    /// </list>
    ///
    /// <para><c>readyUpToggleState</c> is private and OPTIONAL: without it this returns null, the
    /// online branch is never taken and the class behaves exactly as it did in ModBuild 225 — the
    /// stand-down is announced by <see cref="EnsureReflection"/> and nothing throws.</para>
    /// </summary>
    private static UIReadyToggle? QuestConfirmToggle()
    {
        if (_readyToggleState == null || !Singleton<UIReadyToggle>.IsInitialized)
            return null;
        UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
        if (toggle == null)
            return null;
        return _readyToggleState.GetValue(toggle) is EReadyUpToggleStates state
               && state == EReadyUpToggleStates.Quests
            ? toggle
            : null;
    }

    /// <summary>
    /// WHICH OBJECT IS THE TRAVEL CONFIRM RIGHT NOW (ModBuild 226). The declared preference is the
    /// multiplayer ready toggle whenever the game has it serving the QUEST ready-up, because online
    /// that toggle is the object the journey commits through; otherwise the offline container held by
    /// <c>AdventureMapUIManager.travelOptions</c>, which is what this class has parked since
    /// ModBuild 190 and what still applies in single player.
    ///
    /// <para>THREE CONDITIONS, AND EACH ONE IS THERE FOR A REASON. (1) The singleton must exist.
    /// (2) Its private <c>readyUpToggleState</c> must read <c>Quests</c> — the toggle is a SINGLETON
    /// reused for city events, rewards, retirement and town records (decompiled
    /// EReadyUpToggleStates.cs), and parking the RETIREMENT confirm onto the quest card would be a
    /// new bug wearing this fix's clothes; while the map room stands the quest window is STICKY and
    /// can be open during any of those. (3) It must be visible — <c>UIReadyToggle.IsVisible</c> is
    /// <c>window.IsOpen</c> (UIReadyToggle.cs:138) — so a toggle the game has taken away does not
    /// keep the parking alive.</para>
    ///
    /// <para>IF THE REFLECTION FAILS the online branch is simply never taken and the offline
    /// container is used, i.e. exactly the ModBuild 225 behaviour. Nothing throws and nothing is
    /// lost that was working before.</para>
    ///
    /// <para><b>ONLINE, THE OFFLINE CONTAINER IS NOT A FALLBACK — IT IS AN EMPTY SHELL (ModBuild
    /// 422).</b> User report 2026-09-04, multiplayer, map environment, verbatim: <i>"Der Button mit
    /// dem ein Szenario starten kann ist nicht im Fenster - man kann also effektiv kein Szenario
    /// starten!"</i> The game's own <c>AdventureMapUIManager.EnableTravelOptions</c> opens with
    /// <c>travelButton.gameObject.SetActive(!FFSNetwork.IsOnline)</c> (decompiled
    /// AdventureMapUIManager.cs:410-412), so ONLINE the only button inside
    /// <c>travelOptions</c> is deactivated by the game on every single call. Parking that container
    /// therefore parks a rectangle with nothing drawn in it, and the ModBuild 420 host log says so
    /// in three places at once: the reveal warning at Player.log:45367 names
    /// <c>the container has no visible Graphic this tick</c> as the reason no pose could be
    /// written, <c>MAP TRAVEL CONFIRM placement</c> does not appear ONCE in 52,099 lines (so
    /// <see cref="ApplyPose"/> returned false on every tick of every parking), and the unpark line
    /// at :51296 reads <c>travel options handed back</c> rather than <c>the multiplayer quest
    /// confirm</c> — i.e. the object parked into the multiplayer quest card for the whole session
    /// was the SINGLE-PLAYER one. So online this returns null and the parking simply stands by for
    /// the ready toggle, which is the only object that commits the journey there.</para>
    ///
    /// <para>WHAT THIS DOES NOT CLAIM. It does not make the ready toggle appear: whether the game
    /// shows it is the game's decision (<c>UIReadyToggle.ShouldBeVisible</c> =
    /// <c>_requestVisible &amp;&amp; _interactable &amp;&amp; !_allPlayersReady &amp;&amp; no
    /// visibility block</c>, decompiled UIReadyToggle.cs:144-154), and in the 420 log
    /// <c>UIWindow SHOWN: 'Multiplayer Ready Toggle'</c> occurs ZERO times on either machine while
    /// <c>Determining host toggle lock</c> occurs on every selection — so the toggle was asked for
    /// and refused itself. Writing that state from here would be writing game state from
    /// presentation code, which is forbidden. What this build adds instead is the ONE line that
    /// names which term refused, so the next round reads the answer instead of inferring it — see
    /// <see cref="NoteOnlineConfirmMissing"/>.</para>
    /// </summary>
    private static GameObject? ResolveContainer(AdventureMapUIManager? mgr, out bool readyToggle)
    {
        // ModBuild 232 — conditions (1) and (2) moved into QuestConfirmToggle so the park CLAIM can
        // ask the same question one test earlier. Condition (3), visibility, stays HERE and only
        // here: it is what makes the offline container the answer while the game is not drawing the
        // toggle, and it must not leak into the claim.
        UIReadyToggle? toggle = QuestConfirmToggle();
        if (toggle != null && toggle.IsVisible)
        {
            readyToggle = true;
            return toggle.gameObject;
        }
        readyToggle = false;
        // ModBuild 422 — see the class-doc paragraph above. Online the game guarantees the offline
        // container's button is inactive, so parking it can only produce the "revealed with no
        // placement" state the 420 log shipped. REJECTED ALTERNATIVE: parking it anyway and letting
        // the fallback pose below put it inside the frame. That would put an EMPTY rectangle on the
        // quest card and call it the travel confirm — an empty control is exactly the failure the
        // standing ruling "es darf niemals leere Fenster geben" forbids, and it would also keep
        // reporting "travel options handed back" as if the multiplayer confirm had been handled.
        if (FFSNetwork.IsOnline)
            return null;
        return mgr != null ? _travelOptions?.GetValue(mgr) as GameObject : null;
    }

    /// <summary>Is the parked ready toggle still shown by the game? Its own public
    /// <c>IsVisible</c> — literally <c>window.IsOpen</c> — because a UIWindow hide is a CanvasGroup
    /// fade and need not deactivate the GameObject.</summary>
    private static bool ReadyToggleVisible()
    {
        if (!Singleton<UIReadyToggle>.IsInitialized)
            return false;
        UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
        return toggle != null && toggle.IsVisible;
    }

    // ---- parking and unparking -------------------------------------------------------------------

    /// <summary>
    /// Move the container into the window, once. Returns false when it cannot be done, in which case
    /// the parking stands down and the container is left exactly where the game had it.
    ///
    /// <para>THIS IS THE ModBuild 190 SEQUENCE, restored whole: SetParent, SetAsLastSibling, the
    /// bottom-centre anchors, the top-edge pivot, identity rotation, unit scale and then
    /// <c>anchoredPosition</c> — which at the shipped dial defaults is <c>Vector2.zero</c>, the same
    /// value 190 wrote in the same place. The only addition is the hold-down, and it goes on BEFORE
    /// the reparent so there is not even a frame of the move to see.</para>
    /// </summary>
    private static bool Park(UIWindow questWindow, GameObject options, bool readyToggle)
    {
        if (questWindow.transform is not RectTransform win)
        {
            WarnCannotPark($"the quest window '{questWindow.name}' is not a RectTransform");
            return false;
        }
        if (options.transform is not RectTransform rect)
        {
            WarnCannotPark($"'{options.name}' is not a RectTransform");
            return false;
        }

        // ModBuild 226 — THE RECORD IS PER OBJECT. The latch used to be a bare bool because there was
        // only ever one container to record; with two (offline travel options / online ready toggle)
        // a single latch would apply the FIRST object's home to the SECOND on unpark, which is a
        // silent corruption of the game's own HUD layout rather than a visible bug. Keyed on the
        // object, the record is taken once per container and restored verbatim to that container.
        if (!_homeRecorded || !ReferenceEquals(_homeOwner, options))
        {
            _homeRecorded = true;
            _homeOwner = options;
            _optionsHome = rect.parent;
            _optionsHomeIndex = rect.GetSiblingIndex();
            _homeAnchorMin = rect.anchorMin;
            _homeAnchorMax = rect.anchorMax;
            _homePivot = rect.pivot;
            _homeAnchoredPos = rect.anchoredPosition;
            _homeLocalRotation = rect.localRotation;
            _homeLocalScale = rect.localScale;
        }

        _parked = options;
        _parkedIsReadyToggle = readyToggle;
        // THE HOLD-DOWN IS SKIPPED FOR THE READY TOGGLE, DELIBERATELY (ModBuild 226). The hold writes
        // the container's CanvasGroup alpha to suppress the one frame between the game enabling the
        // container and this class writing a pose for it. For the offline container that CanvasGroup
        // is ours to add and nobody else writes it. For the ready toggle the container IS a UIWindow
        // root, and a UIWindow's CanvasGroup is the target its own show/hide TWEEN drives every frame
        // of a fade — holding it would be a two-writer war over one value, which this project has
        // already lost once and does not re-enter (see "Don't win a write war"). The cost is at most
        // one frame of the button at its previous offset on the first show; the alternative is a
        // confirm that fights its own fade.
        if (!readyToggle)
        {
            EnsureHold(options);
            SetHidden(true);
        }
        // BEFORE THE REPARENT, so the parent's layout group never sees a rebuild with this rect in
        // its children. See the ModBuild 196 block: this is what takes the anchors and the
        // anchoredPosition out of the group's hands instead of racing it for them.
        EnsureLayoutIgnore(options);
        _layoutOwner = DescribeLayoutOwner(win, options);

        _host = questWindow;
        _posed = false;
        _placementLogged = false;
        _placementLoggedAt = float.NegativeInfinity;
        _loggedOffset = Vector2.zero;
        _loggedDials = Vector2.zero;
        _wasActive = options.activeInHierarchy;
        _activeSince = Time.unscaledTime;
        ResetDriftWatch();
        ResetAnchor("a new parking — the information has not been measured in this window yet");

        // THE ModBuild 190 SEQUENCE, IN THE ORDER IT HAS ALWAYS HAD, WITH THE ANCHOR CORRECTED TO
        // THE ONE 190 ACTUALLY DREW WITH (ModBuild 196). 190 through 195 wrote (0.5, 0) here, and
        // the container carried that value for exactly one tick per parking: the parent window's
        // uGUI layout group re-anchored it to `Vector2.up` on its first rebuild and kept it there
        // for every rendered frame after — fifteen hardware samples in the class doc, twelve of them
        // solving to (0, 1) to within 0.01 px. Writing `Vector2.up` here is therefore not a new
        // placement; it is the SAME PLACEMENT 190 put on screen, written down honestly, so that
        // 0/0 puts `Vector2.zero` on the rect (exactly as 190's log line read) and the frame no
        // longer changes underneath the dials one tick after the park. With the LayoutElement above
        // in place nothing re-drives it, and ApplyPose does not depend on it either — it derives the
        // offset from whatever anchor the rect is carrying, so this line sets a starting value and
        // never has to defend it. Every property is recorded above and written back verbatim on
        // unpark.
        //
        // ONE REORDERING AT ModBuild 197, AND IT CHANGES NO END STATE. The rotation and the scale are
        // now written BEFORE `ApplyPose` instead of after it. Every one of these writes is
        // unconditional and none of them reads another, so the container ends this method in exactly
        // the state it ended it in at 196 — but `ApplyPose` now MEASURES the container's painted
        // content, and a measurement taken while the container still carried the flat HUD's own scale
        // or rotation would solve the zero against a shape that is about to change. The sweep must
        // see the final one.
        rect.SetParent(win, worldPositionStays: false);
        rect.SetAsLastSibling();
        rect.anchorMin = Vector2.up;
        rect.anchorMax = Vector2.up;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        ApplyPose(rect, win, out _, out _, out _);
        return true;
    }

    /// <summary>Seconds between <see cref="NoteOnlineConfirmMissing"/> lines. The state it reports
    /// is level-triggered and can stand for a whole map-room visit, so it is rate-limited rather
    /// than printed per tick; the answer only changes when the game changes its mind.</summary>
    private const float OnlineConfirmReportIntervalSeconds = 20f;

    private static float _onlineConfirmLoggedAt = float.NegativeInfinity;
    private static string _onlineConfirmLastVerdict = string.Empty;

    // Optional reflection into UIReadyToggle's own visibility terms. All four are private and ALL
    // FOUR ARE OPTIONAL: a missing one prints "<not resolvable>" and nothing else changes. Resolved
    // once, on the first line that needs them.
    private static bool _visibilityTermsResolved;
    private static FieldInfo? _fRequestVisible;
    private static FieldInfo? _fInteractable;
    private static FieldInfo? _fAllPlayersReady;
    private static FieldInfo? _fVisibilityRequests;

    /// <summary>
    /// THE LINE THAT DECIDES REPORT 3a ON THE NEXT HARDWARE LOG (ModBuild 422). User, 2026-09-04,
    /// multiplayer, map environment, verbatim: <i>"Der Button mit dem ein Szenario starten kann ist
    /// nicht im Fenster - man kann also effektiv kein Szenario starten!"</i>
    ///
    /// <para>WHY IT EXISTS. In the ModBuild 420 logs the reason the quest card carried no start
    /// button had to be assembled from four separate absences: <c>UIWindow SHOWN: 'Multiplayer
    /// Ready Toggle'</c> occurs ZERO times on either machine, <c>MAP TRAVEL CONFIRM placement</c>
    /// occurs ZERO times in 52,099 lines, the reveal warning at host Player.log:45367 names
    /// <c>the container has no visible Graphic this tick</c>, and the unpark at :51296 says
    /// <c>travel options handed back</c>. Four absences are not an answer. This is the one line
    /// that states, positively: the room is online, the quest window IS floated, and the object
    /// that commits the journey online is not being shown - together with WHICH term of the game's
    /// own <c>UIReadyToggle.ShouldBeVisible</c> is refusing it.</para>
    ///
    /// <para>IT WRITES NOTHING. Every value below is READ - three public properties and four
    /// private fields reached through optional reflection - and the ruling "never write game state
    /// from presentation code" is why this class cannot simply show the toggle itself. Whether the
    /// multiplayer confirm appears is <c>DetermineHostToggleInteractability</c>'s decision
    /// (decompiled MapChoreographer.cs:3318-3339) and it stays that way.</para>
    /// </summary>
    private static void NoteOnlineConfirmMissing(UIWindow questWindow)
    {
        if (!Singleton<UIReadyToggle>.IsInitialized)
            return;
        UIReadyToggle? toggle = Singleton<UIReadyToggle>.Instance;
        if (toggle == null)
            return;

        if (!_visibilityTermsResolved)
        {
            _visibilityTermsResolved = true;
            _fRequestVisible = AccessTools.Field(typeof(UIReadyToggle), "_requestVisible");
            _fInteractable = AccessTools.Field(typeof(UIReadyToggle), "_interactable");
            _fAllPlayersReady = AccessTools.Field(typeof(UIReadyToggle), "_allPlayersReady");
            _fVisibilityRequests = AccessTools.Field(typeof(UIReadyToggle), "_visibilityRequests");
        }

        string state = _readyToggleState?.GetValue(toggle) is EReadyUpToggleStates s
            ? s.ToString()
            : "<not resolvable>";
        string requestVisible = Term(_fRequestVisible, toggle);
        string interactable = Term(_fInteractable, toggle);
        string allReady = Term(_fAllPlayersReady, toggle);
        string blocks = _fVisibilityRequests?.GetValue(toggle) is System.Collections.ICollection c
            ? c.Count.ToString()
            : "<not resolvable>";

        // CHANGE-GATED AND RATE-LIMITED, in that order: a verdict that has not moved says nothing
        // new, and one that HAS moved is the event worth a line even inside the interval.
        string verdict = $"{state}|{requestVisible}|{interactable}|{allReady}|{blocks}";
        bool changed = verdict != _onlineConfirmLastVerdict;
        if (!changed && Time.unscaledTime - _onlineConfirmLoggedAt < OnlineConfirmReportIntervalSeconds)
            return;
        _onlineConfirmLastVerdict = verdict;
        _onlineConfirmLoggedAt = Time.unscaledTime;

        // HW-VERIFY: report 3a — "the button that starts a scenario is not in the window".
        VRLog.Note(Scope,
            "MAP TRAVEL CONFIRM ONLINE STAND-BY: this client is ONLINE, the quest card "
            + $"'{questWindow.name}' IS floated, and NOTHING has been parked into it, because the "
            + "object that commits a journey online is the game's multiplayer quest confirm "
            + $"('{toggle.name}', GUI_SELECT_QUEST) and the game is not showing it. "
            + $"ITS OWN VISIBILITY TERMS RIGHT NOW: ShouldBeVisible={toggle.ShouldBeVisible}, "
            + $"IsVisible (= its UIWindow.IsOpen)={toggle.IsVisible}, readyUpToggleState={state} "
            + $"(only Quests is this window's confirm), _requestVisible={requestVisible}, "
            + $"_interactable={interactable}, _allPlayersReady={allReady}, "
            + $"{blocks} outstanding BlockVisibility request(s). READ THEM IN THAT ORDER: "
            + "ShouldBeVisible is exactly `_requestVisible && _interactable && !_allPlayersReady && "
            + "no blocks` (decompiled UIReadyToggle.cs:144-154), so the FIRST false term on this "
            + "line names the cause and no other line has to be correlated with it. "
            + "_requestVisible false means UIMapMultiplayerController.ToggleReadyUpUI(show: true) "
            + "was never reached for this selection; _interactable false means "
            + "MapChoreographer.DetermineHostToggleInteractability computed its `flag` as false, "
            + "which online for a Participant ready-up requires AllPlayers.Count > 1, no joining or "
            + "connecting users, and EVERY player IsParticipant. "
            + "WHY THIS IS NOT A BUG THIS CLASS CAN FIX: the offline container "
            + "AdventureMapUIManager.travelOptions is NOT a fallback online - the game's own "
            + "EnableTravelOptions does travelButton.gameObject.SetActive(!FFSNetwork.IsOnline) "
            + "(decompiled AdventureMapUIManager.cs:410-412), so parking it would park a rectangle "
            + "with no active button in it, which is what ModBuild 420 shipped and what the user "
            + "reported as 'der Button ist nicht im Fenster'. Showing the toggle from here would be "
            + "writing game state from presentation code. IF THIS LINE IS ABSENT and the button is "
            + "still missing, the parking DID happen and the fault is a placement fault - read "
            + "MAP TRAVEL CONFIRM placement and MODAL CONTROL COVERAGE instead. "
            // ModBuild 423 — APPENDED, never reworded (a surface checker counts the tokens above).
            // 422 stated which of ShouldBeVisible's terms was false and then stopped, listing the
            // three sub-terms of DetermineHostToggleInteractability as PROSE - so the round that
            // shipped it still had to correlate `Number of players`, the join lifecycle and every
            // `gained control over` line by hand to find out which one it was. That is the
            // "name the blocker, not the number" lesson, and this clause EVALUATES them instead.
            + "WHICH SUB-TERM IS FALSE, EVALUATED HERE AND NOT LEFT AS PROSE: "
            + MapQuestReadyRoster.DescribeInteractabilityTerms()
            + " (the three terms of MapChoreographer.DetermineHostToggleInteractability's `flag`, "
            + "decompiled MapChoreographer.cs:3325, in the order that method writes them; a "
            + "conjunction has exactly ONE first cause). The player is told the same thing inside "
            + "the quest card - see MAP QUEST READY CARD, field NOTICE.");
    }

    /// <summary>One private bool read as text, or "&lt;not resolvable&gt;" when the field is gone.
    /// A missing field must never take a diagnostic down with it.</summary>
    private static string Term(FieldInfo? field, UIReadyToggle toggle) =>
        field?.GetValue(toggle) is bool b ? b.ToString() : "<not resolvable>";

    /// <summary>ONE Warn for "the container cannot be parked at all", naming the consequence.</summary>
    private static void WarnCannotPark(string why)
    {
        _parkStandDown = true;
        if (_parkWarned)
            return;
        _parkWarned = true;
        VRLog.Warn(Scope, $"MAP TRAVEL CONFIRM: cannot park the travel options — {why}, so the anchor "
                          + "arithmetic this class is built on does not apply. The parking stands down. "
                          + "CONSEQUENCE: the Reisen / 'Quest erneut spielen' button stays unreachable in "
                          + "the 3D map room. The double-press shortcut stays switched off, so travel "
                          + "still cannot commit by accident. Nothing throws.");
    }

    /// <summary>
    /// Put the container back. The HOLD is always released — leaving the game's own button at alpha 0
    /// in its own HUD would be the worst outcome available — but the TRANSFORM is restored only while
    /// the object is still parented under our host, because re-taking it from wherever the game has
    /// since put it is a write war.
    /// </summary>
    private static void Unpark(string why)
    {
        if (_host == null && _hold == null && _layoutIgnore == null && _parked == null)
        {
            // Nothing parked. Still clear the one-per-visit diagnostic latches, so a second visit
            // re-reports a failure instead of failing silently.
            _revealForcedLogged = false;
            _parkWarned = false;
            return;
        }

        UIWindow? host = _host;
        _host = null;
        ReleaseHold();
        ReleaseLayoutIgnore();
        ResetDriftWatch();
        ResetAnchor("the container was unparked");
        _watchNextReportAt = float.PositiveInfinity;

        _posed = false;
        _placementLogged = false;
        _placementLoggedAt = float.NegativeInfinity;
        _loggedOffset = Vector2.zero;
        _loggedDials = Vector2.zero;
        _wasActive = false;
        _revealForcedLogged = false;
        _parkWarned = false;

        // ModBuild 226 — RESTORE THE OBJECT WE MOVED, not the object we would move today. Through
        // ModBuild 225 this line re-derived the container from AdventureMapUIManager.travelOptions,
        // which was correct while that was the only candidate there could be. Now that the online
        // confirm is a different GameObject, re-deriving would hand the wrong object its home record
        // and leave the one we actually parked inside a window that is about to be released.
        GameObject? options = _parked;
        _parked = null;
        bool wasReadyToggle = _parkedIsReadyToggle;
        _parkedIsReadyToggle = false;
        if (options == null)
            return;
        Transform t = options.transform;
        // STILL OURS? Unity fake-null covers a destroyed window here as well: a window that no longer
        // exists cannot be compared against, so the transform restore is skipped and only the hold
        // release above has run.
        if (host == null || host.transform == null || t.parent == null
            || !t.parent.IsChildOf(host.transform))
            return;
        if (_optionsHome == null)
            return;

        t.SetParent(_optionsHome, worldPositionStays: false);
        t.SetSiblingIndex(Mathf.Clamp(_optionsHomeIndex, 0, Mathf.Max(0, _optionsHome.childCount - 1)));
        if (t is RectTransform rect)
        {
            rect.anchorMin = _homeAnchorMin;
            rect.anchorMax = _homeAnchorMax;
            rect.pivot = _homePivot;
            rect.anchoredPosition = _homeAnchoredPos;
            rect.localRotation = _homeLocalRotation;
            rect.localScale = _homeLocalScale;
        }
        VRLog.Info(Scope, $"MAP TRAVEL CONFIRM: {(wasReadyToggle
                              ? $"the multiplayer quest confirm '{options.name}'"
                              : "travel options")} handed back to their own home ({why}) — "
                          + "parent, sibling index, anchors, pivot, anchoredPosition, local rotation and "
                          + "local scale all restored verbatim from the record taken before the first "
                          + "move; the hold-down (CanvasGroup alpha / blocksRaycasts) and the layout "
                          + "opt-out (LayoutElement.ignoreLayout) released or destroyed. Nothing of ours "
                          + "is left on the game's object, and its own HUD lays it out again as it "
                          + "always did.");
    }

    // ---- the hold-down: 192's render-hidden host, done on a parented container --------------------

    /// <summary>
    /// Make sure there is a CanvasGroup on the container we can hold it down with, and record what it
    /// looked like before we touched it. If the container has none we ADD one and remember that we
    /// did, so <see cref="ReleaseHold"/> destroys it again rather than leaving a component behind.
    /// </summary>
    private static void EnsureHold(GameObject options)
    {
        if (_hold != null && _hold.gameObject == options)
            return;
        ReleaseHold();
        CanvasGroup? cg = options.GetComponent<CanvasGroup>();
        _holdAdded = cg == null;
        if (cg == null)
            cg = options.AddComponent<CanvasGroup>();
        _hold = cg;
        _holdHomeAlpha = cg.alpha;
        _holdHomeBlocksRaycasts = cg.blocksRaycasts;
        _hidden = false;
    }

    /// <summary>Level-triggered visibility write on OUR OWN state. The game never reads or writes
    /// this CanvasGroup (AdventureMapUIManager toggles the container with SetActive and nothing
    /// else), so there is no second owner to alternate with.</summary>
    private static void SetHidden(bool hide)
    {
        _hidden = hide;
        if (_hold == null)
            return;
        _hold.alpha = hide ? 0f : _holdHomeAlpha;
        _hold.blocksRaycasts = !hide && _holdHomeBlocksRaycasts;
    }

    /// <summary>Give the CanvasGroup back exactly as it was found, or destroy the one we added.</summary>
    private static void ReleaseHold()
    {
        CanvasGroup? cg = _hold;
        bool added = _holdAdded;
        _hold = null;
        _holdAdded = false;
        _hidden = false;
        if (cg == null)
            return;
        if (added)
        {
            Object.Destroy(cg);
            return;
        }
        cg.alpha = _holdHomeAlpha;
        cg.blocksRaycasts = _holdHomeBlocksRaycasts;
    }

    // ---- the layout seam: taking the container OUT of the parent's layout group ------------------

    /// <summary>
    /// THE FIX FOR THE ONE-FRAME RELAPSE. Put a <see cref="LayoutElement"/> with
    /// <c>ignoreLayout = true</c> on the container, recording what was there before.
    ///
    /// <para>WHY THIS AND NOT A PER-FRAME CORRECTION. uGUI's <c>LayoutGroup</c> builds its child
    /// list in <c>CalculateLayoutInputHorizontal</c> and SKIPS every child carrying an
    /// <c>ILayoutIgnorer</c> whose <c>ignoreLayout</c> is set — the rect never enters
    /// <c>rectChildren</c>, <c>SetChildAlongAxisWithScale</c> (the method that writes
    /// <c>anchorMin = anchorMax = Vector2.up</c> AND <c>anchoredPosition</c> on a layout child) is
    /// never called on it, and the group's <c>DrivenRectTransformTracker</c> is cleared of it. That
    /// is the seam that stops the other writer, which is the standing house rule: a value the game
    /// re-writes every rebuild must not be re-written back every frame.</para>
    ///
    /// <para>NOTHING ELSE CHANGES FOR THE GAME. The flag only means "do not lay this rect out"; the
    /// group keeps laying out its OWN children exactly as before, and this container was never one
    /// of them until <see cref="Park"/> put it there. On unpark the component is destroyed (if we
    /// added it) or its flag written back (if the container already had one), so the flat HUD lays
    /// it out again precisely as it always did.</para>
    /// </summary>
    private static void EnsureLayoutIgnore(GameObject options)
    {
        if (_layoutIgnore != null && _layoutIgnore.gameObject == options)
        {
            _layoutIgnore.ignoreLayout = true;
            return;
        }
        ReleaseLayoutIgnore();
        LayoutElement? le = options.GetComponent<LayoutElement>();
        _layoutIgnoreAdded = le == null;
        if (le == null)
            le = options.AddComponent<LayoutElement>();
        _layoutIgnoreHome = le.ignoreLayout;
        le.ignoreLayout = true;
        _layoutIgnore = le;
    }

    /// <summary>Give the layout opt-out back exactly as it was found, or destroy the one we
    /// added.</summary>
    private static void ReleaseLayoutIgnore()
    {
        LayoutElement? le = _layoutIgnore;
        bool added = _layoutIgnoreAdded;
        _layoutIgnore = null;
        _layoutIgnoreAdded = false;
        if (le == null)
            return;
        if (added)
        {
            Object.Destroy(le);
            return;
        }
        le.ignoreLayout = _layoutIgnoreHome;
    }

    /// <summary>
    /// NAME THE WRITER. Resolved once per park and printed in the diagnostics, so a report of "it
    /// still flashes" can say WHAT is driving the rect instead of leaving the next round to guess
    /// — the same bar the class doc sets for every other number on the placement line.
    /// </summary>
    private static string DescribeLayoutOwner(RectTransform win, GameObject options)
    {
        var group = win.GetComponent<LayoutGroup>();
        var fitter = win.GetComponent<ContentSizeFitter>();
        var own = options.GetComponent<LayoutGroup>();
        string parent = group != null
            ? $"the quest window carries a {group.GetType().Name} — THAT is the component that writes "
              + "anchorMin/anchorMax = Vector2.up and anchoredPosition on its layout children, and it "
              + "is why this container needs the LayoutElement opt-out"
            : "the quest window carries NO LayoutGroup of its own (if the rect still drifts, the "
              + "writer is further up the chain or is not a layout component at all — the drift watch "
              + "below says whether it drifts at all)";
        string sizer = fitter != null
            ? $"; it also carries a ContentSizeFitter ({fitter.GetType().Name}), which sizes it from "
              + "the same layout the opt-out removes this container from"
            : "";
        string mine = own != null
            ? $"; the container itself carries a {own.GetType().Name} for its OWN buttons, which is "
              + "untouched — the opt-out is about the container's place in its PARENT's layout"
            : "";
        return parent + sizer + mine;
    }

    /// <summary>
    /// THE ANTI-FLASH GATE. The container is drawn only while the game has it shown AND a pose has
    /// been written for the window rect it is sitting in — so there is no frame in which the button
    /// is visible at an offset that is not the dialled one, including on the frame a dial changes
    /// (the write happens in <see cref="PlaceButton"/>, earlier in the same tick). Whenever the game
    /// has it hidden the hold goes back ON, which is what makes the rule total: on the frame the game
    /// calls SetActive(true) the alpha is already 0, whichever order the two run in.
    ///
    /// <para>BOUNDED: if a pose can never be written, the button is shown anyway after
    /// <see cref="ShowDeadlineSeconds"/> with a Warn.</para>
    /// </summary>
    private static void TickVisibility(bool active)
    {
        bool ready = _posed;
        if (!ready && active && Time.unscaledTime - _activeSince >= ShowDeadlineSeconds)
        {
            ready = true;
            if (!_revealForcedLogged)
            {
                _revealForcedLogged = true;
                VRLog.Warn(Scope, "MAP TRAVEL CONFIRM: the travel options are being revealed although no "
                                  + "placement could be written for them (deadline "
                                  + $"{ShowDeadlineSeconds * 1000f:F0} ms since the game enabled them). "
                                  + "THERE ARE EXACTLY TWO CAUSES AND THE PLACEMENT LINE NAMES WHICH: "
                                  + "either the floated quest window's rect reports no height, so a dial "
                                  + "has nothing to be a fraction OF; or the quest information's bottom "
                                  + "edge — the ModBuild 197 zero — could not be measured yet, which the "
                                  + $"'zero' field of that line spells out ({_anchorWhy}). CONSEQUENCE: "
                                  + "the button is showing wherever it last stood rather than where the "
                                  + "dials say. A button that never appears would be the worse failure, "
                                  + "so visibility wins. It corrects itself on the first tick either "
                                  + "measurement succeeds; if this line repeats, REPORT IT.");
            }
        }
        bool wantVisible = active && ready;
        if (_hidden == wantVisible)
            SetHidden(!wantVisible);
    }

    // ---- the placement -----------------------------------------------------------------------------

    /// <summary>
    /// The two dials, read LIVE and clamped to the same bounds the bind site advertises. Fractions of
    /// the quest window's own height, measured from the ModBuild 197 zero — the point at which the
    /// button's painted content sits directly under the quest information, centred on it; +x is the
    /// window's right, +y is its up.
    /// </summary>
    private static Vector2 Dials()
    {
        var dx = WorldUIConfig.TravelButtonOffsetXWindowHeights;
        var dy = WorldUIConfig.TravelButtonOffsetYWindowHeights;
        float x = dx != null ? dx.Value : Defaults.TravelButtonOffsetXWindowHeights;
        float y = dy != null ? dy.Value : Defaults.TravelButtonOffsetYWindowHeights;
        return new Vector2(Mathf.Clamp(x, -OffsetLimitX, OffsetLimitX),
                           Mathf.Clamp(y, OffsetLimitYMin, OffsetLimitYMax));
    }

    /// <summary>
    /// Write the dialled offset onto the parked container, measured from the ModBuild 197 moving
    /// zero. Level-triggered: the write is skipped whenever the rect already carries the wanted
    /// value, so a steady state costs one compare (and, four times a second, two content sweeps).
    /// </summary>
    /// <returns>True when the container now carries the dialled offset.</returns>
    private static bool ApplyPose(RectTransform rect, RectTransform win,
                                  out Vector2 want, out Vector2 dials, out float windowHeight)
    {
        dials = Dials();
        Rect frame = win.rect;
        windowHeight = Mathf.Abs(frame.height);
        want = Vector2.zero;
        // A window whose rect has no height yet (a uGUI layout is not final on the frame a window is
        // floated) has nothing for a FRACTION to be a fraction of. That tick writes nothing and the
        // hold-down in TickVisibility keeps the button off-screen while it waits; the reveal deadline
        // bounds the wait.
        if (windowHeight <= 0f)
        {
            _posed = false;
            return false;
        }

        // THE ZERO IS MEASURED, NOT A WINDOW EDGE (ModBuild 197). Refreshed on a cadence, never per
        // frame, and it is the ONE input that decides where 0/0 lands. Until it has been measured
        // once for this parking nothing is written at all — a pose from an unmeasured zero would be
        // the "confident wrong placement" that got 191, 192 and 193 rejected, and the hold-down plus
        // the reveal deadline already cover the wait.
        MaybeRefreshAnchor(win, rect, frame);
        if (!_anchorValid)
        {
            _posed = false;
            ApplyFallbackPose(rect, frame, windowHeight);
            return false;
        }

        // THE POSE IS A WINDOW-LOCAL POINT, NOT AN anchoredPosition (ModBuild 196, unchanged).
        //
        // ModBuild 423 — ONE TERM ADDED, AND IT IS ZERO IN SINGLE PLAYER. The user asked for the
        // "wer hat schon bestätigt" icon row ABOVE this button ("Ich will das man die icons der
        // Spieler sieht die es bereits bestätigt haben über dem Button - so wie im flat game"), and
        // the space directly above this button is the quest information itself — the measured zero
        // IS that text's bottom edge. So the row takes the zero and the button moves down by the
        // row's own height. THE DIALS ARE NOT REINTERPRETED: they still measure from the zero, and
        // the zero still lands on the TOP of the confirm GROUP, directly under the quest
        // information. MapQuestReadyRoster.ReservedHeight() is exactly 0 whenever that class is not
        // holding the row — every offline tick, and every online tick on which the game is not
        // showing its multiplayer confirm — so nothing about the single-player placement changes.
        Vector2 wantPivot = new(_anchorPivot.x + dials.x * windowHeight,
                                _anchorPivot.y + dials.y * windowHeight
                                    - MapQuestReadyRoster.ReservedHeight());

        // ANCHORS ARE READ, NEVER RE-WRITTEN. anchoredPosition means nothing without the anchor it
        // is measured from, so the number that lands on the rect is derived from the anchor the rect
        // is CARRYING this tick. A second writer that re-anchors the container therefore cannot move
        // the button by a single pixel — it changes an input this line recomputes — and there is no
        // shared value left for two writers to alternate over. The one case that cannot be handled
        // by arithmetic is a STRETCHED anchor (anchorMin != anchorMax), because that also changes
        // the container's own rect SIZE and with it where its children draw; that is corrected, and
        // the drift watch counts it.
        if (rect.anchorMin != rect.anchorMax)
        {
            rect.anchorMin = rect.anchorMax = Vector2.up;
            _watchAnchorDeviations++;
        }
        Vector2 reference = AnchorReference(frame, rect.anchorMin, rect.anchorMax);
        want = wantPivot - reference;
        if ((rect.anchoredPosition - want).sqrMagnitude > OffsetEpsilon * OffsetEpsilon)
            rect.anchoredPosition = want;   // SKIPPED when the rect already carries the answer

        _wrotePos = rect.anchoredPosition;
        _wroteAnchorMin = rect.anchorMin;
        _wroteAnchorMax = rect.anchorMax;
        _wrotePivot = rect.pivot;
        _wroteValid = true;
        _posed = true;
        return true;
    }

    /// <summary>
    /// Fraction of the quest window's own height at which an UNPLACED container's top edge is
    /// parked, measured up from the window's bottom edge. A confirm bar is ~65 px tall in a card
    /// that is 800-1021 px tall, so a twelfth of the height leaves the whole bar inside the frame
    /// with room to spare at every card length this window takes.
    /// </summary>
    private const float FallbackTopFractionAboveBottom = 0.12f;

    /// <summary>
    /// WHERE A CONTAINER SITS WHILE ITS ZERO CANNOT BE MEASURED (ModBuild 422). User report
    /// 2026-09-04, multiplayer, map environment, verbatim: <i>"Der Button mit dem ein Szenario
    /// starten kann ist nicht im Fenster - man kann also effektiv kein Szenario starten!"</i>
    ///
    /// <para>THE DEFECT THIS REMOVES, WITH THE LOG LINE THAT PROVES IT. Until this build, a parking
    /// whose measured zero never resolved wrote NOTHING to <c>anchoredPosition</c> at all - the
    /// early return above was taken before the write. The container therefore kept the value
    /// <c>rect.SetParent(win, worldPositionStays: false)</c> left on it, which is the FLAT HUD BAR's
    /// own offset re-read against the quest window's top-left corner: a screen-bottom bar offset
    /// interpreted inside an 801 px card puts it hundreds of pixels below the frame. Then
    /// <see cref="TickVisibility"/>'s reveal deadline showed it anyway, and its own Warn said so -
    /// ModBuild 420 host Player.log:45367, <c>the travel options are being revealed although no
    /// placement could be written for them . CONSEQUENCE: the button is showing wherever it last
    /// stood</c>. "Wherever it last stood" was outside the window. That warning also promised
    /// <i>"if this line repeats, REPORT IT"</i>, and it fired twice in one session (:24353, :45367)
    /// while <c>MAP TRAVEL CONFIRM placement</c> fired zero times in 52,099 lines.</para>
    ///
    /// <para>WHAT IS WRITTEN INSTEAD. The container's pivot - <see cref="Park"/> sets it to
    /// (0.5, 1), i.e. its own TOP CENTRE - is put on the window's horizontal centre at
    /// <see cref="FallbackTopFractionAboveBottom"/> of the window height above the window's bottom
    /// edge. That is a point INSIDE the committed frame by construction, for every card length and
    /// every rig scale, with no measurement and no timer in it. It is NOT a guess at the tuned
    /// placement and it does not pretend to be one: <c>_posed</c> stays false, so the hold-down and
    /// the reveal deadline behave exactly as before, and the first tick the real zero resolves the
    /// dialled pose overwrites this one on the same frame.</para>
    ///
    /// <para>WHY NOT SIMPLY REFUSE TO REVEAL. That was considered and rejected: it converts "the
    /// button is in the wrong place" into "there is no button", and the standing ruling is that a
    /// control the flat game shows must exist in the room. The reveal deadline already exists and
    /// already chose visibility over placement - this makes the position it reveals at a defined,
    /// in-frame one instead of a foreign coordinate system's leftovers.</para>
    ///
    /// <para>LEVEL-TRIGGERED, like every other write in this class: the rect is only touched when it
    /// does not already carry the answer, so a steady state costs one vector compare. The
    /// <c>_wrote*</c> record is updated so <see cref="WatchDrift"/> does not count this class's own
    /// write as a foreign one.</para>
    /// </summary>
    private static void ApplyFallbackPose(RectTransform rect, Rect frame, float windowHeight)
    {
        // Same correction ApplyPose makes, for the same reason: anchoredPosition is meaningless
        // against a STRETCHED anchor, because that also drives the container's own rect size.
        if (rect.anchorMin != rect.anchorMax)
        {
            rect.anchorMin = rect.anchorMax = Vector2.up;
            _watchAnchorDeviations++;
        }
        Vector2 wantPivot = new(frame.center.x,
                                frame.yMin + FallbackTopFractionAboveBottom * windowHeight);
        Vector2 reference = AnchorReference(frame, rect.anchorMin, rect.anchorMax);
        Vector2 want = wantPivot - reference;
        if ((rect.anchoredPosition - want).sqrMagnitude > OffsetEpsilon * OffsetEpsilon)
            rect.anchoredPosition = want;

        _wrotePos = rect.anchoredPosition;
        _wroteAnchorMin = rect.anchorMin;
        _wroteAnchorMax = rect.anchorMax;
        _wrotePivot = rect.pivot;
        _wroteValid = true;
    }

    /// <summary>
    /// The point in <paramref name="frame"/>'s own local space that a child's
    /// <c>anchoredPosition</c> is measured FROM, for the given (equal) anchors: <c>rect.min +
    /// anchor * rect.size</c>. With <c>anchorMin == anchorMax</c> this is exact and the child's
    /// pivot lands at <c>reference + anchoredPosition</c>, which is the identity the whole placement
    /// is built on.
    /// </summary>
    private static Vector2 AnchorReference(Rect frame, Vector2 anchorMin, Vector2 anchorMax)
    {
        Vector2 a = (anchorMin + anchorMax) * 0.5f;
        return new Vector2(frame.xMin + a.x * frame.width, frame.yMin + a.y * frame.height);
    }

    // ---- the moving zero: WHERE THE QUEST INFORMATION ENDS (ModBuild 197) ------------------------

    /// <summary>Forget the measured zero. Called on every park and unpark, so an anchor can never be
    /// carried from one window — or one quest's parking — into another.</summary>
    private static void ResetAnchor(string why)
    {
        _anchorValid = false;
        _anchorNextRefreshAt = float.NegativeInfinity;
        _anchorResolvedAt = float.NegativeInfinity;
        _anchorPivot = Vector2.zero;
        _anchorInfoRaw = default;
        _anchorInfo = default;
        _anchorButton = default;
        _anchorInfoCounted = 0;
        _anchorInfoSkipped = 0;
        _anchorInfoClipped = 0;
        _anchorButtonCounted = 0;
        _anchorButtonSkipped = 0;
        _anchorButtonClipped = 0;
        _anchorLastMoveY = 0f;
        _anchorWhy = why;
    }

    /// <summary>
    /// Re-measure the zero if it is due, and keep the last good one otherwise.
    ///
    /// <para>THE CADENCE IS THE POINT. The two sweeps below walk every Graphic under the window; the
    /// thing they measure changes when the QUEST changes, which is many thousands of frames apart.
    /// Running them per frame would buy nothing and cost a per-frame walk of ~180 transforms in a
    /// room whose Update budget is already the one the perf line complains about.</para>
    ///
    /// <para>A FAILED REFRESH NEVER CLEARS A GOOD ANCHOR. The container is switched off by the game
    /// whenever no location is staged, and a sweep of an inactive subtree finds nothing; treating
    /// that as "the information moved to the origin" would throw the button across the card every
    /// time he deselects a location. Failure keeps the last measurement and says so in
    /// <see cref="_anchorWhy"/>.</para>
    /// </summary>
    private static void MaybeRefreshAnchor(RectTransform win, RectTransform rect, Rect frame)
    {
        float now = Time.unscaledTime;
        // THE CADENCE BINDS FAILURES TOO, deliberately. The container is switched off whenever no
        // location is staged, and a sweep of an inactive subtree finds nothing — so "retry until it
        // works" would mean two full Graphic walks EVERY FRAME for as long as the player has the
        // quest window open without a location selected, in a room whose Update budget is already the
        // one the perf line complains about. A failed attempt therefore waits like a successful one.
        // The first attempt of a parking is exempt (Park leaves the deadline at -inf), and 0.2 s is
        // inside the ShowDeadlineSeconds reveal bound, so nothing is held down any longer for it.
        if (now < _anchorNextRefreshAt)
            return;
        _anchorNextRefreshAt = now + AnchorRefreshIntervalSeconds;

        if (!TryContentBounds(win, rect, excludeRoot: null, out Rect btn,
                              out int btnCounted, out int btnSkipped, out int btnClipped))
        {
            _anchorWhy = "the container has no visible Graphic this tick (the game usually has it "
                         + "switched off when no location is staged) — the previous zero is kept";
            return;
        }
        if (!TryContentBounds(win, win, excludeRoot: rect, out Rect infoRaw,
                              out int infoCounted, out int infoSkipped, out int infoClipped))
        {
            _anchorWhy = "the quest window has no visible Graphic outside the button itself — the "
                         + "previous zero is kept";
            return;
        }

        // FRAME-CLAMPED, and the class doc carries the hardware that settles it: the raw union of
        // this card reaches 321 px to the LEFT of a 512 px card, from graphics faint enough that
        // CanvasConversion's own fit throws twenty of them away. Intersecting with the card is what
        // makes the horizontal zero agree with the position the user tuned by hand (6 mm) instead of
        // disagreeing with it (134 mm). The vertical side of the clamp almost never bites — the
        // information's bottom edge measured 355 px INSIDE the card — and when it does it saturates
        // the zero at the card's own bottom edge rather than sending the button after content drawn
        // past it.
        Rect info = Rect.MinMaxRect(Mathf.Max(infoRaw.xMin, frame.xMin),
                                    Mathf.Max(infoRaw.yMin, frame.yMin),
                                    Mathf.Min(infoRaw.xMax, frame.xMax),
                                    Mathf.Min(infoRaw.yMax, frame.yMax));
        if (info.width <= 0f || info.height <= 0f)
        {
            _anchorWhy = "the information's visible union does not overlap the window's own rect at "
                         + "all, so clamping it to the card leaves nothing to measure — the previous "
                         + "zero is kept";
            return;
        }

        // THE CONTAINER'S OWN PIVOT-TO-CONTENT OFFSET, which is what makes this a fixed point rather
        // than a feedback loop: translating the container moves its pivot and its painted content by
        // the SAME vector, so this difference does not depend on where the container currently is,
        // and re-solving after a write reproduces the same answer with zero gain.
        //
        // THE PIVOT IS READ FROM THE TRANSFORM, NOT REBUILT FROM THE ANCHORS. `RectTransform.position`
        // IS the pivot's world position, so this is exact whatever anchors the rect carries — where
        // `anchorReference + anchoredPosition` is only exact while anchorMin == anchorMax, and the
        // one thing this file has learned the hard way is that a second writer can re-anchor this
        // rect between two of our ticks. Same reason the painted content is read from world corners.
        Vector3 pivotLocal = win.InverseTransformPoint(rect.position);
        float contentTopFromPivot = btn.yMax - pivotLocal.y;
        float contentCentreFromPivot = btn.center.x - pivotLocal.x;

        Vector2 solved = new(info.center.x - contentCentreFromPivot,
                             info.yMin - contentTopFromPivot);

        _anchorLastMoveY = _anchorValid ? solved.y - _anchorPivot.y : 0f;
        _anchorPivot = solved;
        _anchorValid = true;
        _anchorResolvedAt = now;
        _anchorInfoRaw = infoRaw;
        _anchorInfo = info;
        _anchorButton = btn;
        _anchorInfoCounted = infoCounted;
        _anchorInfoSkipped = infoSkipped;
        _anchorInfoClipped = infoClipped;
        _anchorButtonCounted = btnCounted;
        _anchorButtonSkipped = btnSkipped;
        _anchorButtonClipped = btnClipped;
        _anchorWhy = "resolved";
    }

    /// <summary>
    /// The painted union of everything <paramref name="sweepRoot"/> draws, in
    /// <paramref name="frame"/>'s own local space — the SAME sweep the anchor solve is built on,
    /// exposed so a sibling placement measures ink with this file's rules rather than a second set
    /// of its own.
    ///
    /// <para>Allocation-free: the sweep fills this class's own static scratch list and is called from
    /// one place at a time, on a cadence, never re-entrantly.</para>
    /// </summary>
    internal static bool TryInkBounds(RectTransform frame, Transform sweepRoot,
                                      out Rect ink, out int counted) =>
        TryContentBounds(frame, sweepRoot, excludeRoot: null, out ink, out counted, out _, out _);

    /// <summary>
    /// ONE LINE, ONCE PER SESSION: the dials' ZERO changed meaning at ModBuild 197, so any value
    /// tuned against an earlier build now points somewhere else.
    ///
    /// <para>WHY THIS AND NOT AN AUTOMATIC MIGRATION. A dropped cfg value in this project is always
    /// measured against the newest build and is taken verbatim — that is the rule that makes a
    /// dropped value trustworthy at all. A build that quietly rewrote the user's numbers would break
    /// it for every future drop, and it would have to GUESS a window height and a quest length to do
    /// the arithmetic with. So the mod states the change and he decides, which is also the standing
    /// ruling for this whole file: "ich stell es selber ein".</para>
    ///
    /// <para>Warn rather than Info while either dial is non-zero, because in that state the button
    /// really is in the wrong place and the line is the only thing that explains why.</para>
    /// </summary>
    private static void ZeroMovedNotice(Vector2 dials, float windowHeight, float mmPerLocalY)
    {
        if (_zeroNoticeLogged)
            return;
        _zeroNoticeLogged = true;
        if (dials == Vector2.zero)
        {
            VRLog.Info(Scope, "MAP TRAVEL CONFIRM: the two [WorldUI] TravelButtonOffset dials are at "
                              + "0/0 and their ZERO MOVED at ModBuild 197 — it is no longer the quest "
                              + "window's fixed top-left corner but the BOTTOM EDGE OF THE QUEST "
                              + "INFORMATION, measured live from what the card is actually painting. "
                              + "0/0 therefore now means 'the button's content sits directly under the "
                              + "information, centred on it', for a short quest and a long one alike. "
                              + "Nothing to do.");
            return;
        }
        VRLog.Warn(Scope, "MAP TRAVEL CONFIRM: YOUR TUNED TRAVEL-BUTTON OFFSETS ARE MEASURED FROM A "
                          + "NEW ZERO. [WorldUI] TravelButtonOffsetXWindowHeights = "
                          + $"{dials.x:F3} and TravelButtonOffsetYWindowHeights = {dials.y:F3} were "
                          + "tuned against ModBuild 196, whose zero was the quest window's FIXED "
                          + "top-left corner. ModBuild 197 moved the zero onto the BOTTOM EDGE OF THE "
                          + "QUEST INFORMATION (your own request: the offsets should be relative to "
                          + "the end of the quest info so the button always lies underneath it), "
                          + "which is roughly three quarters of a window height further down and a "
                          + "quarter of one across. THOSE TWO NUMBERS THEREFORE NOW MEAN SOMETHING "
                          + "ELSE and are pushing the button "
                          + $"{dials.x * windowHeight * mmPerLocalY:F0} mm sideways and "
                          + $"{dials.y * windowHeight * mmPerLocalY:F0} mm vertically AWAY from "
                          + "'directly underneath'. SET BOTH BACK TO 0 — 0/0 is now exactly the "
                          + "placement you asked for, and on the hardware line this build was "
                          + "derived from it lands within 6 mm sideways and 16 mm below the pose you "
                          + "tuned by hand. Nothing was migrated for you on purpose: a value you drop "
                          + "is always taken verbatim against the newest build, and a build that "
                          + "rewrote your config would break that. The ranges also tightened, to "
                          + $"±{OffsetLimitX:F2} and {OffsetLimitYMin:F2}…{OffsetLimitYMax:F2}, "
                          + "because the travel the old range paid for is the travel the new zero "
                          + "already makes.");
    }

    // ---- the drift watch: PROOF that nothing writes this rect behind us -------------------------

    /// <summary>Start a fresh watch window. Called on every park and unpark, so the counts always
    /// belong to one parking of one window and can never blend two.</summary>
    private static void ResetDriftWatch()
    {
        _wroteValid = false;
        _watchComparisons = 0;
        _watchPosDeviations = 0;
        _watchAnchorDeviations = 0;
        _watchPivotDeviations = 0;
        _watchWorstDeviation = 0f;
        _watchWorstActual = Vector2.zero;
        _watchWorstExpected = Vector2.zero;
        _watchReportedOnce = false;
        _watchNextReportAt = Time.unscaledTime + DriftReportIntervalSeconds;
    }

    /// <summary>
    /// ONE COMPARISON, TAKEN BEFORE THIS TICK WRITES ANYTHING. Whatever the rect carries now is what
    /// the LAST frame left on it — our own value if nobody else touched it, somebody else's if they
    /// did. The comparison count is incremented on every single tick, so a silent instrument and a
    /// clean one are distinguishable in the log by construction.
    /// </summary>
    private static void WatchDrift(RectTransform rect)
    {
        if (!_wroteValid)
            return;
        _watchComparisons++;
        Vector2 now = rect.anchoredPosition;
        float dev = (now - _wrotePos).magnitude;
        if (dev > OffsetEpsilon)
        {
            _watchPosDeviations++;
            if (dev > _watchWorstDeviation)
            {
                _watchWorstDeviation = dev;
                _watchWorstActual = now;
                _watchWorstExpected = _wrotePos;
            }
        }
        if (rect.anchorMin != _wroteAnchorMin || rect.anchorMax != _wroteAnchorMax)
            _watchAnchorDeviations++;
        if (rect.pivot != _wrotePivot)
            _watchPivotDeviations++;
    }

    /// <summary>
    /// THE LINE THAT MAKES THE ModBuild 196 FIX FALSIFIABLE. It prints the number of COMPARISONS
    /// first, then the deviations, so the three outcomes are three different lines: "N comparisons,
    /// 0 deviations" (the fix holds), "N comparisons, K deviations" (a writer is still there, and
    /// the worst one is quoted in real millimetres with the component that most likely wrote it),
    /// and no line at all (the watch never ran — which now means the container was never parked and
    /// active, not that all is well).
    ///
    /// <para>Rate-limited to <see cref="DriftReportIntervalSeconds"/>, and after the first line of a
    /// parking it only prints when there is something to report. A healthy build therefore costs one
    /// line per quest window and then nothing.</para>
    /// </summary>
    private static void ReportDrift(UIWindow host, GameObject options, float mmPerLocal)
    {
        float now = Time.unscaledTime;
        if (now < _watchNextReportAt)
            return;
        bool anything = _watchPosDeviations > 0 || _watchAnchorDeviations > 0
                        || _watchPivotDeviations > 0;
        _watchNextReportAt = now + DriftReportIntervalSeconds;
        if (_watchReportedOnce && !anything)
            return;
        bool first = !_watchReportedOnce;
        _watchReportedOnce = true;

        string verdict = anything
            ? $"A SECOND WRITER IS STILL THERE. anchoredPosition differed from the value this class "
              + $"last wrote on {_watchPosDeviations} of {_watchComparisons} frames (worst "
              + $"{_watchWorstDeviation:F1} local units = {_watchWorstDeviation * mmPerLocal:F0} mm "
              + $"real: expected {_watchWorstExpected}, found {_watchWorstActual}); the anchors "
              + $"changed under us {_watchAnchorDeviations} time(s) and the pivot "
              + $"{_watchPivotDeviations} time(s). A deviation on a MINORITY of frames is the "
              + "one-frame relapse: uGUI rebuilds layout in Canvas.willRenderCanvases, AFTER this "
              + "class has run in Update, so a layout write always draws once before we correct it. "
              + "REPORT THIS LINE — the anchors are already immune (the pose is derived from "
              + "whatever anchor is live), so a surviving anchoredPosition deviation means the "
              + "LayoutElement opt-out did not reach this writer"
            : $"CLEAN: anchoredPosition matched the value this class last wrote on all "
              + $"{_watchComparisons} frames compared, and neither the anchors nor the pivot moved. "
              + "Nothing else is writing this rect, so the button cannot relapse for a frame — which "
              + "is also why the window's drawn-content measurement can no longer find it at the "
              + "bottom of the card and grow the frame to reach it";
        string span = first ? "first" : "repeat";
        VRLog.Info(Scope, $"MAP TRAVEL CONFIRM drift watch ('{options.name}' in '{host.name}', "
                          + $"{span} report, covering the {_watchComparisons} tick(s) since the "
                          + $"previous one — at least {DriftReportIntervalSeconds:F0} s of them) — "
                          + $"{verdict}.\n"
                          + $"  layout    : {_layoutOwner}.\n"
                          + $"  opt-out   : LayoutElement.ignoreLayout is "
                          + $"{(_layoutIgnore != null && _layoutIgnore.ignoreLayout ? "ON" : "NOT ON")} "
                          + $"({(_layoutIgnoreAdded ? "added by the mod, destroyed on unpark" : "the container's own component, its flag restored on unpark")}).");
        _watchComparisons = 0;
        _watchPosDeviations = 0;
        _watchAnchorDeviations = 0;
        _watchPivotDeviations = 0;
        _watchWorstDeviation = 0f;
    }

    /// <summary>
    /// One tick of the placement: read the dials, write the offset if it moved, and log the line the
    /// user tunes from when — and only when — the answer changed.
    /// </summary>
    private static void PlaceButton(UIWindow host, GameObject options, AdventureMapUIManager? mgr)
    {
        if (host == null || options == null)
            return;
        if (options.transform is not RectTransform rect || host.transform is not RectTransform win)
            return;

        // BEFORE THE WRITE. Whatever the rect carries at this instant is what the previous frame
        // left on it, so this is the only place a foreign write can be seen at all — one tick later
        // our own value is back on it and the evidence is gone.
        WatchDrift(rect);

        if (!ApplyPose(rect, win, out Vector2 want, out Vector2 dials, out float windowHeight))
            return;

        ReportDrift(host, options, Mathf.Abs(win.lossyScale.y) / RigScale() * 1000f);

        // LOG ONLY WHEN THERE IS SOMETHING NEW TO SAY, and only while the button is actually on
        // screen — the numbers a tuner needs (where the button ended up) do not exist for a container
        // the game has switched off, and its child rects are not laid out either. A dial turned while
        // the container is hidden is NOT lost: the latches below are only advanced on a tick that
        // actually printed, so the change is still pending and the line comes out on the first tick
        // the game shows the container again.
        if (!options.activeInHierarchy)
            return;

        // EVERY DIAL CHANGE PRINTS, not only a "material" one (ModBuild 195). Through 194 the only
        // test was whether the APPLIED offset had moved by more than half a uGUI unit, which made
        // "I turned the dial and the log said nothing" a reachable state — and an instrument that can
        // be silent about the one action it exists to report is worse than no instrument. Two
        // independent triggers now, because the two can move independently: the dials themselves
        // (exact compare — any turn at all, however small, is something the user DID and must be
        // answered), or the applied offset (which moves on its own when the window is resized while
        // the dials stand still, and that is worth a line too).
        Vector2 loggedDials = _loggedDials;
        bool dialsMoved = dials != loggedDials;
        bool offsetMoved = (want - _loggedOffset).sqrMagnitude > OffsetEpsilon * OffsetEpsilon;
        if (_placementLogged
            && ((!dialsMoved && !offsetMoved)
                || Time.unscaledTime - _placementLoggedAt < ResolveLogIntervalSeconds))
            return;
        bool first = !_placementLogged;
        Vector2 previousOffset = _loggedOffset;
        _placementLogged = true;
        _placementLoggedAt = Time.unscaledTime;
        _loggedOffset = want;
        _loggedDials = dials;
        LogPlacement(host, options, mgr, rect, win, want, dials, windowHeight,
                     first, previousOffset, loggedDials);
    }

    /// <summary>
    /// The union of the ACTIVE, VISIBLE, UNCLIPPED Graphics under <paramref name="sweepRoot"/>,
    /// expressed in <paramref name="frame"/>'s local space, optionally skipping everything under
    /// <paramref name="excludeRoot"/>.
    ///
    /// <para>SINCE ModBuild 197 THIS IS ON THE WRITE PATH, which it never was before. Through 196 it
    /// filled in the log line and nothing it returned could reach <c>anchoredPosition</c>; the user
    /// then asked for the offsets to be measured from the END OF THE QUEST INFORMATION, and that is a
    /// measurement or it is nothing. Everything it answers is still printed on the placement line, so
    /// a wrong placement can still be blamed on the sweep with numbers rather than argued about.</para>
    ///
    /// <para>IT IS CALLED TWICE PER REFRESH, WITH THE SAME FRAME AND DIFFERENT ROOTS, and that is the
    /// whole point of the parameters. Once on the CONTAINER, to say where the button's own painted
    /// pixels are; once on the WINDOW with the container excluded, to say where the quest card's
    /// INFORMATION is. Both answers land in the WINDOW'S local space — the frame the two dials are
    /// defined in — so the difference between them is directly a dial value, with no assumption at
    /// all about the container's own rotation or scale.</para>
    ///
    /// <para>THE EXCLUSION IS LOAD-BEARING, not tidiness: the container is a CHILD of the window
    /// since ModBuild 190, so a sweep of the window that did not skip it would measure the button as
    /// part of the information it is supposed to sit under, and the answer would chase itself.</para>
    ///
    /// <para>GRAPHICS AND NOT RECTTRANSFORMS, deliberately. A uGUI card is full of layout groups and
    /// empty spacers whose rects are far larger than anything drawn in them — and the quest window's
    /// own rect is one of those, which is exactly why the information's real extent has to be
    /// MEASURED rather than read off the window. A Graphic is the only component that PAINTS, so the
    /// union of the enabled, non-transparent, non-degenerate ones is "what the player can see".</para>
    ///
    /// <para>CLIPPING IS HONOURED, NEW AT ModBuild 197 AND LOAD-BEARING FOR EXACTLY THE LONG QUESTS
    /// THIS BUILD IS ABOUT. The information block contains an <c>Information/Scroll View</c>, and a
    /// scrolling description's Text graphic is routinely far taller than the viewport showing it. An
    /// unclipped union would decide the information ends hundreds of pixels below the card and drive
    /// the button off the bottom of the world. So a Graphic whose <c>CanvasRenderer</c> uGUI has
    /// already culled is dropped, and every surviving one is intersected with the rect of each
    /// enabled <c>RectMask2D</c>/<c>Mask</c> between it and the frame. What is measured is what is
    /// VISIBLE — the same rule CanvasConversion's own host-rect fit applies.</para>
    ///
    /// <para>Corner-based, not rect-based: a child may sit several transforms deep, so its rect is in
    /// ITS parent's space. <c>GetWorldCorners</c> + <c>InverseTransformPoint</c> lands every corner in
    /// the frame with no assumption about the chain between them.</para>
    /// </summary>
    private static bool TryContentBounds(RectTransform frame, Transform sweepRoot,
                                         Transform? excludeRoot, out Rect content,
                                         out int counted, out int skipped, out int clipped)
    {
        content = default;
        counted = 0;
        skipped = 0;
        clipped = 0;
        ContentGraphics.Clear();
        sweepRoot.GetComponentsInChildren(includeInactive: false, ContentGraphics);
        float xMin = float.PositiveInfinity, yMin = float.PositiveInfinity;
        float xMax = float.NegativeInfinity, yMax = float.NegativeInfinity;
        for (int i = 0; i < ContentGraphics.Count; i++)
        {
            Graphic g = ContentGraphics[i];
            if (g == null || !g.enabled || g.color.a <= MinVisibleAlpha)
            {
                skipped++;
                continue;
            }
            RectTransform rt = g.rectTransform;
            if (rt == null || rt.rect.width <= 0f || rt.rect.height <= 0f)
            {
                skipped++;
                continue;
            }
            if (excludeRoot != null && rt.IsChildOf(excludeRoot))
            {
                skipped++;
                continue;
            }
            // uGUI's own verdict first: RectMask2D sets this on a child it has clipped away
            // entirely, so it costs one field read and settles the common case.
            CanvasRenderer cr = g.canvasRenderer;
            if (cr != null && cr.cull)
            {
                clipped++;
                continue;
            }
            rt.GetWorldCorners(CornerScratch);
            float gxMin = float.PositiveInfinity, gyMin = float.PositiveInfinity;
            float gxMax = float.NegativeInfinity, gyMax = float.NegativeInfinity;
            for (int c = 0; c < 4; c++)
            {
                Vector3 p = frame.InverseTransformPoint(CornerScratch[c]);
                if (p.x < gxMin) gxMin = p.x;
                if (p.x > gxMax) gxMax = p.x;
                if (p.y < gyMin) gyMin = p.y;
                if (p.y > gyMax) gyMax = p.y;
            }
            if (!ClipToMasks(frame, rt, ref gxMin, ref gyMin, ref gxMax, ref gyMax))
            {
                clipped++;
                continue;
            }
            if (gxMin < xMin) xMin = gxMin;
            if (gxMax > xMax) xMax = gxMax;
            if (gyMin < yMin) yMin = gyMin;
            if (gyMax > yMax) yMax = gyMax;
            counted++;
        }
        if (counted == 0)
            return false;
        content = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    /// <summary>
    /// Intersect one graphic's frame-space box with every enabled <c>RectMask2D</c>/<c>Mask</c>
    /// between it and <paramref name="frame"/> (inclusive), and report whether anything is left.
    ///
    /// <para>The walk stops AT the frame, because a mask above the quest window clips the window as a
    /// whole and moving the button would not escape it — while a mask INSIDE the window (the
    /// description's scroll viewport) is precisely the thing that decides where the readable
    /// information ends.</para>
    ///
    /// <para>A <c>Mask</c> is treated as its own rect, which is the approximation uGUI itself makes
    /// for a rectangular sprite and errs on the side of measuring LESS. A mask sitting on the
    /// graphic's own object never clips that graphic — it is what draws the stencil — so it is
    /// skipped.</para>
    /// </summary>
    private static bool ClipToMasks(RectTransform frame, RectTransform rt,
                                    ref float xMin, ref float yMin, ref float xMax, ref float yMax)
    {
        Transform? t = rt;
        while (t != null)
        {
            if (t.TryGetComponent(out RectMask2D rectMask) && rectMask.enabled
                && rectMask.rectTransform != null)
                IntersectWithRect(frame, rectMask.rectTransform,
                                  ref xMin, ref yMin, ref xMax, ref yMax);
            if (t.TryGetComponent(out Mask mask) && mask.enabled && mask.rectTransform != null
                && !ReferenceEquals(mask.rectTransform, rt))
                IntersectWithRect(frame, mask.rectTransform, ref xMin, ref yMin, ref xMax, ref yMax);
            if (xMax <= xMin || yMax <= yMin)
                return false;
            if (ReferenceEquals(t, frame))
                break;
            t = t.parent;
        }
        return xMax > xMin && yMax > yMin;
    }

    /// <summary>Fold one clipper's frame-space box into the running one.</summary>
    private static void IntersectWithRect(RectTransform frame, RectTransform clipper,
                                          ref float xMin, ref float yMin,
                                          ref float xMax, ref float yMax)
    {
        clipper.GetWorldCorners(MaskCornerScratch);
        float cxMin = float.PositiveInfinity, cyMin = float.PositiveInfinity;
        float cxMax = float.NegativeInfinity, cyMax = float.NegativeInfinity;
        for (int c = 0; c < 4; c++)
        {
            Vector3 p = frame.InverseTransformPoint(MaskCornerScratch[c]);
            if (p.x < cxMin) cxMin = p.x;
            if (p.x > cxMax) cxMax = p.x;
            if (p.y < cyMin) cyMin = p.y;
            if (p.y > cyMax) cyMax = p.y;
        }
        if (cxMin > xMin) xMin = cxMin;
        if (cyMin > yMin) yMin = cyMin;
        if (cxMax < xMax) xMax = cxMax;
        if (cyMax < yMax) yMax = cyMax;
    }

    /// <summary>
    /// THE LINE THE USER TUNES FROM. It has to answer three questions now, and ModBuild 194's version
    /// answered only the first:
    /// <list type="number">
    ///   <item>"I set the dials to THAT; where did the button go?" — the two dial values, everything
    ///   they are multiplied by (the window rect, its lossyScale, the rig scale), the resulting
    ///   <c>anchoredPosition</c>, and the button's world position.</item>
    ///   <item>"I just turned a dial; WHAT DID IT DO?" — the previous dial pair, the new one, and the
    ///   distance the button travelled between them in REAL MILLIMETRES on both axes. 194 printed
    ///   only the absolute pose, so a turn could be confirmed but not measured, and the user had no
    ///   way to tell a dial that moved the button 9 mm from a dial that did nothing at all.</item>
    ///   <item>"Where would I have to set them to get what I asked for?" — see
    ///   <c>AppendRecommendation</c>. THIS IS A NUMBER TO TYPE IN, NEVER A POSE THAT IS
    ///   APPLIED: three solved placements were rejected in a row (191/192/193) and the ruling is that
    ///   he does the moving. Printing the arithmetic is the opposite of taking it over — it hands him
    ///   the measurement he cannot take from inside the headset and leaves the decision where it
    ///   belongs.</item>
    /// </list>
    /// </summary>
    private static void LogPlacement(UIWindow host, GameObject options, AdventureMapUIManager? mgr,
                                     RectTransform rect, RectTransform win, Vector2 want,
                                     Vector2 dials, float windowHeight,
                                     bool first, Vector2 previousOffset, Vector2 previousDials)
    {
        // RIG SCALE IS NAMED, NOT ASSUMED. The map room runs at ~198 WORLD units per REAL metre, so a
        // world length divided by the rig scale is what turns it back into real metres — the unit the
        // user judges "zu weit weg" in. Mixing the two silently has shipped as a bug in this very
        // file's neighbourhood, so every conversion below states which of the two it is in.
        float rigScale = RigScale();
        float mmPerLocalX = Mathf.Abs(win.lossyScale.x) / rigScale * 1000f;
        float mmPerLocalY = Mathf.Abs(win.lossyScale.y) / rigScale * 1000f;

        var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
        string btnWhere = "<not found>";
        if (btn != null)
        {
            Vector3 inWindow = win.InverseTransformPoint(btn.transform.position);
            float dxLocal = inWindow.x - win.rect.center.x;
            float dyLocal = inWindow.y - win.rect.center.y;
            // AGAINST ALL THREE REFERENCE LINES, NOT JUST THE CENTRE (ModBuild 195). Two consecutive
            // build notes described this pose in opposite ways — 193's header said the 190 pose "hung
            // far BELOW the window, out over the table", 194's report computed it as sitting ON the
            // card just above the bottom edge — and neither could be checked against the other,
            // because the only number printed was a distance from the CENTRE, which is equally
            // consistent with both if you disagree about how tall the window is. Naming the distance
            // to the BOTTOM and the TOP edge as well makes the claim falsifiable from one line: a
            // negative distance-to-bottom means the button is genuinely below the frame, a positive
            // distance-to-top means it is above it, and both being positive means it is inside.
            float aboveBottom = inWindow.y - win.rect.yMin;
            float belowTop = win.rect.yMax - inWindow.y;
            btnWhere = $"'{btn.name}' active={btn.gameObject.activeInHierarchy}, world position "
                       + $"{btn.transform.position} (WORLD units), i.e. {dxLocal * mmPerLocalX:F0} mm "
                       + $"right and {dyLocal * mmPerLocalY:F0} mm up from the window's CENTRE, "
                       + $"{aboveBottom * mmPerLocalY:F0} mm above the window rect's BOTTOM edge and "
                       + $"{belowTop * mmPerLocalY:F0} mm below its TOP edge — all in REAL millimetres "
                       + "at the current rig scale. A NEGATIVE 'above bottom' means the button really "
                       + "is hanging below the frame; a NEGATIVE 'below top' means it is floating over "
                       + "it; both positive means it is inside the rect, wherever it may LOOK";
        }
        // Built as separate locals rather than inlined: a nested interpolated string inside another
        // one is legal C# but it defeats the repo's own source scanners (scripts/patch-inventory.py
        // walks string literals with a single-quote-depth reader), and a tool that mis-parses this
        // file reports its Harmony patch as unregistered. Not worth the saved locals.
        //
        // THE LINE REPORTS THE MEASUREMENT THE POSE WAS COMPUTED FROM, NOT A FRESH ONE (ModBuild 197).
        // Through 196 this method took its own two sweeps, which was harmless while nothing they
        // returned could reach the transform; now that the zero IS the measurement, a second sweep
        // taken later in the tick could disagree with the one the button was placed by and the log
        // would be describing a pose that was never written. Both rects below are the cached ones,
        // with the age of the measurement printed beside them.
        //
        // BOTH SWEEPS ARE TAKEN IN THE WINDOW'S OWN LOCAL SPACE — the frame the dials are defined in
        // — so the two rects below and the anchoredPosition above are all in the same units and can
        // simply be subtracted.
        bool haveButton = _anchorValid;
        bool haveInfo = _anchorValid;
        Rect btnContent = _anchorButton;
        Rect info = _anchorInfo;
        float measuredAgeMs = _anchorResolvedAt > float.NegativeInfinity
            ? (Time.unscaledTime - _anchorResolvedAt) * 1000f
            : -1f;
        string contentHow = haveButton
            ? $"{_anchorButtonCounted} visible Graphic(s) ({_anchorButtonSkipped} skipped as "
              + $"disabled/transparent/zero-sized, {_anchorButtonClipped} clipped away by a mask), "
              + $"union in WINDOW-LOCAL space {btnContent}"
            : "NOT MEASURABLE — no visible Graphic in the container";
        string infoHow = haveInfo
            ? $"{_anchorInfoCounted} visible Graphic(s) ({_anchorInfoSkipped} skipped as disabled/"
              + $"transparent/zero-sized or belonging to the button itself, {_anchorInfoClipped} "
              + "clipped away by a mask — that last count is the scrolling description's viewport "
              + "doing its job, and without it a long quest would push this rect hundreds of px "
              + $"below the card). RAW union {_anchorInfoRaw}, CLAMPED to the window's own rect "
              + $"{info} — the clamp is what makes the horizontal zero agree with the position the "
              + "user tuned by hand; see the ModBuild 197 block. THE BOTTOM EDGE OF THIS RECT IS THE "
              + $"ZERO OF THE Y DIAL: window-local y = {info.yMin:F1}, i.e. "
              + $"{(info.yMin - win.rect.yMin) * mmPerLocalY:F0} mm above the card's own bottom edge "
              + $"and {(win.rect.yMax - info.yMin) * mmPerLocalY:F0} mm below its top. IT MOVES WITH "
              + "THE QUEST: a longer information block puts it lower and the button follows"
            : "NOT MEASURABLE — no visible Graphic in the quest window outside the button itself";
        string measuredHow = _anchorValid
            ? $"{_anchorWhy}, {measuredAgeMs:F0} ms ago (re-measured at most every "
              + $"{AnchorRefreshIntervalSeconds:F2} s). Taken in Update from GetWorldCorners, i.e. "
              + "from the LAST COMPLETED uGUI layout — the state that was on screen — and NOT from "
              + "inside a LayoutRebuilder.ForceRebuildLayoutImmediate, which this class never calls "
              + "and which is known to report an inflated subtree that never renders"
            : $"NO ZERO MEASURED: {_anchorWhy}";
        float infoMovedMm = _anchorLastMoveY * mmPerLocalY;
        string movedZeroHow = Mathf.Abs(_anchorLastMoveY) > OffsetEpsilon
            ? $"THE INFORMATION'S BOTTOM EDGE MOVED {_anchorLastMoveY:F1} local units "
              + $"({infoMovedMm:F0} mm real, negative = further down) since the previous measurement, "
              + "and the button moved with it. That is the ModBuild 197 behaviour working: a longer "
              + "or shorter quest text changes this number and the placement follows it"
            : "the information's bottom edge has not moved since the previous measurement";
        string holdHow = _hidden ? "HELD DOWN (alpha 0)" : "revealed";
        Vector2 anchorRef = AnchorReference(win.rect, rect.anchorMin, rect.anchorMax);
        string poseHow = dials == Vector2.zero
            ? "BOTH DIALS AT 0, so the button's content top edge is ON the quest information's bottom "
              + "edge and centred on it — 'direkt unter der Info', for this quest's length"
            : "offset from 'directly under the quest information' by the two dials";
        float mmPerTenth = 0.1f * windowHeight * mmPerLocalY;

        // WHAT THE LAST TURN OF A DIAL ACTUALLY DID, in the unit the user judges in. The first line
        // of a parking has no "before" to subtract, and saying so is better than printing a delta
        // against a zero that was never on screen.
        string movedHow = first
            ? "FIRST placement of this parking — there is no previous pose to measure against. The "
              + "next line will state what your turn of a dial did, in real millimetres"
            : $"dials went ({previousDials.x:F3}, {previousDials.y:F3}) -> ({dials.x:F3}, "
              + $"{dials.y:F3}); the button therefore moved "
              + $"{(want.x - previousOffset.x) * mmPerLocalX:F1} mm sideways and "
              + $"{(want.y - previousOffset.y) * mmPerLocalY:F1} mm vertically (positive = right / up), "
              + "in REAL millimetres at the current rig scale. A dial that changed with 0.0 mm of "
              + "movement means the window has no height this tick, not that the dial is dead";

        VRLog.Info(Scope,
            $"MAP TRAVEL CONFIRM placement — INSIDE the quest window '{host.name}', {poseHow}.\n"
            + $"  dials     : [WorldUI] TravelButtonOffsetXWindowHeights = {dials.x:F3}, "
            + $"TravelButtonOffsetYWindowHeights = {dials.y:F3} (fractions of the WINDOW'S HEIGHT; "
            + "+x = right, +y = up, and since ModBuild 197 the ZERO OF BOTH IS THE QUEST "
            + "INFORMATION'S BOTTOM EDGE AND HORIZONTAL CENTRE — a MOVING reference that is "
            + "re-measured from what the card is painting, so 0/0 is 'directly underneath' for a "
            + "short quest and a long one alike. It is NOT the window rect's top-left corner any "
            + "more, which is what 196 used and what any value tuned before this build was measured "
            + "from). "
            + $"Clamped to {-OffsetLimitX:F2}…{OffsetLimitX:F2} and {OffsetLimitYMin:F2}…"
            + $"{OffsetLimitYMax:F2} (tightened at 197 from ±0.50 / -1.50…1.50: the travel the old "
            + "range paid for is the travel the new zero already makes). Read live, every tick — "
            + "turn them and the button moves on the next frame.\n"
            + $"  window    : rect {win.rect} (height {windowHeight:F1} local units), lossyScale "
            + $"{win.lossyScale.y:F4} WORLD units per local unit; rig scale {rigScale:F2} WORLD units "
            + $"per REAL metre, so one local unit is {mmPerLocalY:F3} mm real and the whole card is "
            + $"{windowHeight * mmPerLocalY:F0} mm tall. 0.1 on either dial = {mmPerTenth:F0} mm.\n"
            + $"  container : '{options.name}' rect {rect.rect} — the flat HUD bar's own rect, moved "
            + $"here whole. Live anchors {rect.anchorMin}..{rect.anchorMax}, pivot {rect.pivot}; "
            + $"the anchor reference those put in window-local space is {anchorRef}, and the "
            + "container's pivot is placed at reference + anchoredPosition. THE ANCHORS ARE READ AND "
            + "NEVER RE-WRITTEN, so if a game component re-anchors this rect the offset below simply "
            + "recomputes and the button does not move.\n"
            + $"  applied   : anchoredPosition = {want}, putting the container's pivot at "
            + $"{anchorRef + want} in window-local space = the measured zero "
            + $"({_anchorPivot.x:F1}, {_anchorPivot.y:F1}) plus (dial.x, dial.y) x {windowHeight:F1}. "
            + $"The container is currently {holdHow}.\n"
            + $"  zero      : {measuredHow}.\n"
            + $"  zero moved: {movedZeroHow}.\n"
            + $"  layout    : {_layoutOwner}. The container carries a LayoutElement with ignoreLayout "
            + $"= {(_layoutIgnore != null && _layoutIgnore.ignoreLayout ? "TRUE" : "NOT SET")}, which "
            + "is what keeps that group from writing this rect's anchors and anchoredPosition on "
            + "every rebuild — the ModBuild 195 one-frame relapse.\n"
            + $"  button    : {btnWhere}.\n"
            + $"  moved     : {movedHow}.\n"
            + $"  content   : {contentHow} — this is one of the two rects the zero above is solved "
            + "from: the button's own painted pixels. If the placement looks wrong, these numbers "
            + "and the ones on the next line are what to doubt.\n"
            + $"  info block: {infoHow}.\n"
            + $"  {ZeroCheck(haveButton, btnContent, haveInfo, info, dials, windowHeight)}\n"
            + "  TUNING    : 0/0 now means the button's content sits DIRECTLY UNDER the quest "
            + "information and centred on it, whatever that quest's length — that is the whole "
            + "change in ModBuild 197 and it is why any value tuned before this build has to go back "
            + "to 0. From there the dials are nudges: -0.05 on Y opens a "
            + $"{0.05f * windowHeight * mmPerLocalY:F0} mm breathing gap under the information, "
            + $"0.1 on either dial = {mmPerTenth:F0} mm. The card is "
            + $"{Mathf.Abs(win.rect.width) / Mathf.Max(windowHeight, 0.0001f):F2} window heights "
            + $"wide, so X = ±{OffsetLimitX:F2} reaches its left and right edges and no further. "
            + "THIS LINE PRINTS ON EVERY DIAL CHANGE AND WHENEVER THE MEASURED ZERO MOVES, at most "
            + $"every {ResolveLogIntervalSeconds:F1} s. Whether anything is still fighting us for "
            + "this rect is no longer a guess from how often this line repeats — the 'drift watch' "
            + "line counts it directly, comparisons included.");
        ZeroMovedNotice(dials, windowHeight, mmPerLocalY);
    }

    /// <summary>
    /// THE RESIDUAL. Through ModBuild 196 this method printed the two numbers the user had to TYPE IN
    /// to get "direkter UNTER der Info"; at 197 those two numbers ARE the zero, so the same arithmetic
    /// becomes a self-check on the placement instead of a suggestion for the user.
    ///
    /// <para>WHAT IT PROVES. The suggestion is <c>dial + (measured delta) / windowHeight</c>. Under
    /// the 197 anchor the measured delta is exactly <c>-dial * windowHeight</c> — the anchor is
    /// defined so that it is — so a healthy build prints (0.000, 0.000) AT ANY DIAL SETTING. Anything
    /// else means the zero the pose was written from and the content that is actually on screen
    /// disagree, and the residual says by how much and in which direction. That is a stronger
    /// instrument than the suggestion was: it is a claim that can FAIL, printed on every line, rather
    /// than a number nobody can check from inside a headset.</para>
    ///
    /// <para>THE DERIVATION IS UNCHANGED and contains no invented constant. Both rects are in the
    /// WINDOW'S local space. Translating the container by Δ moves its whole painted subtree by the
    /// same Δ — that is what a parent translation IS, and it holds whatever the container's own scale
    /// and rotation are, which is why the sweeps are taken in the window's frame rather than the
    /// container's. "Directly under the information" is:</para>
    /// <list type="bullet">
    ///   <item>VERTICALLY: the TOP edge of the button's painted content lands on the BOTTOM edge of
    ///   the information's painted content — <c>Δy = info.yMin - button.yMax</c>. Zero gap, because
    ///   any gap would be a number nobody measured; "directly under" is the literal reading of
    ///   "direkter UNTER", and a breathing space is one downward nudge he can add by eye.</item>
    ///   <item>HORIZONTALLY: the button's painted content is centred on the information's —
    ///   <c>Δx = info.center.x - button.center.x</c>. The 191 rejection named the x axis explicitly
    ///   ("auch auf der x-achse verschoben" — "shifted on the x axis as well"), so it is derived
    ///   rather than left at 0.</item>
    /// </list>
    /// </summary>
    private static string ZeroCheck(bool haveButton, Rect btnContent,
                                    bool haveInfo, Rect info,
                                    Vector2 dials, float windowHeight)
    {
        const string Head = "zero check: ";
        if (windowHeight <= 0f)
            return Head + "NOT DERIVABLE this tick — the window rect reports no height, so a fraction "
                   + "of it is not a distance. Nothing is wrong; look at the next line.";
        if (!haveButton || !haveInfo)
            return Head + "NOT DERIVABLE this tick — one of the two content sweeps above found "
                   + "nothing to measure, so there is nothing to check the placement against. The "
                   + "last measured zero is still in force; this line is the only thing missing.";

        // The residual: where the button would still have to go to be exactly under the information,
        // expressed in the dials' own unit. Under the ModBuild 197 anchor this is (0,0) by
        // construction at any dial setting — see the doc above.
        float residualX = dials.x + (info.center.x - btnContent.center.x) / windowHeight;
        float residualY = dials.y + (info.yMin - btnContent.yMax) / windowHeight;
        bool clean = Mathf.Abs(residualX) * windowHeight <= OffsetEpsilon
                     && Mathf.Abs(residualY) * windowHeight <= OffsetEpsilon;
        if (clean)
            return Head + "PASS — the button's painted content is exactly where the dials say it "
                   + "should be relative to the quest information (residual "
                   + $"{residualX:F3}, {residualY:F3} window heights, i.e. under half a uGUI unit on "
                   + "both axes). At 0/0 that means its top edge is ON the information's bottom edge "
                   + "and it is centred on it; at any other dial pair it means the offset from that "
                   + "point is exactly the pair you set.";
        return Head + "RESIDUAL — the button is NOT where the measured zero plus the dials say it "
               + $"should be: it is still {residualX:F3} window heights "
               + $"({residualX * windowHeight:F1} local units) sideways and {residualY:F3} "
               + $"({residualY * windowHeight:F1} local units) vertically away from 'the dialled "
               + "offset from directly-under-the-information'. This should be 0/0 at ANY dial setting "
               + "— it is a self-check, not a suggestion — so a non-zero value means the zero the "
               + "pose was written from and the content now on screen disagree. The most likely "
               + "causes, in order: the information moved between the last measurement and this frame "
               + "(look at the 'zero' line's age above; it settles on the next refresh), or the "
               + "container's own internal layout changed because the button's label did (Reisen vs "
               + "Quest erneut spielen; that settles in one step too). A residual that PERSISTS "
               + "across several lines is a real fault — REPORT THIS LINE.";
    }

    /// <summary>World units per real metre at the current rig scale, or 1 when there is no rig.
    /// Named rather than assumed: the user judges distances in real millimetres and the transforms
    /// are written in local uGUI units, and mixing the two silently is a bug class this project
    /// has.</summary>
    private static float RigScale() =>
        Rig.RigTarget.Current != null
            ? Mathf.Max(Rig.RigTarget.Current.lossyScale.x, 0.0001f)
            : 1f;

    // ---- the shortcut gate (unchanged since ModBuild 190) ----------------------------------------

    /// <summary>Is this the location the game already has staged for travel? Used by the gate.</summary>
    private static bool IsStagedLocation(AdventureMapUIManager mgr, MapLocation? candidate)
    {
        if (candidate == null || _locationToTravel == null)
            return false;
        return ReferenceEquals(_locationToTravel.GetValue(mgr) as MapLocation, candidate);
    }

    private static AdventureMapUIManager? Object_FindManager() =>
        Object.FindObjectOfType<AdventureMapUIManager>(true);

    private static AdventureMapUIManager? Manager() =>
        Singleton<AdventureMapUIManager>.IsInitialized
            ? Singleton<AdventureMapUIManager>.Instance
            : Object_FindManager();

    private static bool EnsureReflection()
    {
        if (_standDown)
            return false;
        if (_resolved)
            return true;
        _resolved = true;
        _locationToTravel = AccessTools.Field(typeof(AdventureMapUIManager), "locationToTravel");
        _onConfirmCallback = AccessTools.Field(typeof(AdventureMapUIManager), "onConfirmTravelCallback");
        _travelOptions = AccessTools.Field(typeof(AdventureMapUIManager), "travelOptions");
        // ModBuild 226 — OPTIONAL, and deliberately not in the required set below: without it the
        // online container is never resolved and the class behaves exactly as it did in ModBuild 225
        // (offline correct, the multiplayer confirm floating as its own window). A missing optional
        // field must never take the working offline path down with it.
        _readyToggleState = AccessTools.Field(typeof(UIReadyToggle), "readyUpToggleState");
        if (_readyToggleState == null)
            VRLog.Warn(Scope, "MAP TRAVEL CONFIRM: UIReadyToggle.readyUpToggleState was not found by "
                              + "name, so this build cannot tell WHICH ready-up the singleton toggle is "
                              + "serving. The multiplayer quest confirm ('Quest wählen') is therefore NOT "
                              + "parked into the quest window and will float as a window of its own, as it "
                              + "did before ModBuild 226. Parking it blind was rejected: the same toggle "
                              + "serves city events, rewards, retirement and town records, and the quest "
                              + "window is sticky in the map room, so a blind park would move the WRONG "
                              + "confirm onto the quest card. Single player is unaffected.");
        _travelButton = AccessTools.Field(typeof(AdventureMapUIManager), "travelButton");
        if (_locationToTravel == null || _onConfirmCallback == null || _travelOptions == null)
        {
            _standDown = true;
            VRLog.Warn(Scope, "MAP TRAVEL CONFIRM: AdventureMapUIManager private members not found by name "
                              + $"(locationToTravel={_locationToTravel != null}, "
                              + $"onConfirmTravelCallback={_onConfirmCallback != null}, "
                              + $"travelOptions={_travelOptions != null}) — the whole feature stands down. "
                              + "CONSEQUENCE: the single-player double-press shortcut stays live (a second "
                              + "press on the same location travels immediately) and the Reisen button "
                              + "stays unreachable in the room. Nothing else changes.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Reproduces the game's ONLINE branch of <c>OnSelectedMapLocation</c> while the map room
    /// stands: store the callback and return, without the single-player <c>ConfirmTravel()</c>.
    /// Every other path runs vanilla.
    /// </summary>
    [HarmonyPatch(typeof(AdventureMapUIManager), "OnSelectedMapLocation")]
    internal static class TravelShortcutGate
    {
        private static bool _logged;

        private static bool Prefix(AdventureMapUIManager __instance, MapLocation mapLocation,
                                   System.Action<MapLocation> onConfirmTravelCallback)
        {
            if (_standDown || !MapRoomDriver.Active || __instance == null)
                return true;
            if (!EnsureReflection())
                return true;
            if (!IsStagedLocation(__instance, mapLocation))
                return true; // a DIFFERENT location — the original stages it, exactly as always

            // Same location, second press. The original's own first statement, then out — which is
            // precisely what it does when FFSNetwork.IsOnline. The ConfirmTravel() call is the only
            // thing skipped.
            _onConfirmCallback?.SetValue(__instance, onConfirmTravelCallback);
            if (!_logged)
            {
                _logged = true;
                VRLog.Info(Scope, "MAP TRAVEL CONFIRM: a second press on the ALREADY-SELECTED location "
                                  + "did NOT start the journey. The flat single-player build treats that "
                                  + "as 'go now'; in VR a second trigger pull on the same icon is far too "
                                  + "easy to produce, and the user reported committing by accident. This "
                                  + "is byte-for-byte the branch the game itself takes online. Travel "
                                  + "commits through the Reisen button in the quest window.");
            }
            return false;
        }
    }
}

// ---------------------------------------------------------------------------
// THE PARTY TOKEN'S JOURNEY — WHAT ACTUALLY MOVES IT, WHY IT STOPPED SHORT, AND
// WHAT REPLACES THE GAME'S CHOREOGRAPHY IN THE 3D MAP ROOM.
//
// User, against ModBuild 230: "Wenn man zu einer Quest fährt bewegt sich das Gruppensymbol auf der
// Map. In der 3D Mapumgebung bewegt es sich aber nicht über die ganze Strecke, skaliert also
// scheinbar nicht mit. Das Gruppensymbol sollte immer in der Mitte der Strecke eine Begegnung
// haben, das Symbol muss in der Animation also auf der Strecke bis zur Mitte fahren (immer die
// gleiche Zeit in Anspruch nehmen der Animation, also abhängig der Distanz dann unterschiedlich
// schnell)."
//
// ═══ 1. IT IS NOT A SCALE FACTOR, AND THE DRAW PATH PROVES IT CANNOT BE ══════
//
// The map room does not re-project anything. `MapIconLayer.DrawToken` issues a plain
// `CommandBuffer.DrawRenderer(tokenRenderer, …)` — a call that has NO matrix parameter and records
// the renderer's OWN localToWorld. The parchment is likewise the game's own renderer, left where
// the game put it (`MapParchment` only swaps its MATERIALS). The player is seated against those
// world bounds by `MapRoomSeat` and the RIG is scaled — the map is not. So the token's path across
// the parchment is, point for point, the path the game's `PartyToken.transform` takes in world
// space: there is no factor anywhere between the two that could shrink the journey and no
// re-projection that could lose part of it. "Skaliert scheinbar nicht mit" is the user's inference
// from what he sees; the mechanism is elsewhere and the numbers below name it.
//
// ═══ 2. WHAT IT ACTUALLY IS: TWO TELEPORTS THE FLAT SCREEN HIDES UNDER A FADE ═
//
// Read from source (decompiled/GH.Runtime/MapTimedMovementFlow.cs), a travel runs:
//
//   PrepareTravelTo               (:11)  → TeleportPartyToWayPoint(CalculatePositionToStartMovement)
//                                          — PartyInstantMove at :27, the FIRST teleport, which
//                                            skips whole waypoints until the token is
//                                            PartyMinStartTravelDistance from the origin.
//   MovePartyToFadeIn(D)          (:54)  → PartyToken.PartyMoveTo(waypoints, D, …), which walks at
//                                            the CONSTANT MoveSpeed and then, at
//                                            decompiled/GH.Runtime/PartyToken.cs:120-124,
//                                            `if (timeSpent > duration) { CompleteMoving(); yield
//                                            break; }` — it ABANDONS the route wherever it is.
//   MovePartyToFadeOutDestination (:68)  → TeleportPartyToWayPoint(CalculatePositionToSkipNear-
//                                            Destination) — the SECOND teleport, which jumps the
//                                            token to within DelayToArriveDestination × MoveSpeed
//                                            of the destination, i.e. across nearly the whole map.
//
// On a flat screen none of that is visible, because MovePartyToFadeIn's onProgress delegate (:63)
// drives `TransitionManager.SetFade` to full black as the leg ends and the jump happens behind
// PartyTravelFadedBlackDuration seconds of black screen. `TransitionManager.SetFade`
// (decompiled/GH.Runtime/TransitionManager.cs:144-148) sets the alpha of ONE full-screen uGUI
// Image; the 3D map room shows the map as world geometry and never puts that Image between the
// player's eyes and the table, so in the room the two jumps are simply SEEN.
//
// THIS IS MEASURED, NOT ARGUED. TeleportPartyToWayPoint prints its own before/after arrays
// (MapTimedMovementFlow.cs:19/24) and ModBuild 230's Player.log carries both of them for one
// journey to Quest_Campaign_025_Scenario0 (.planning/debug/Player.log:13365 and :14567):
//
//   route      17 waypoints, (-4.05, 0, -2.05) → (74.43, 0, 53.68) ≈ 96.3 world units
//   teleport 1 "Skip to: 1"   → token jumped to (0.86, 0, 1.43)
//   walked     ONE waypoint (the next printed array starts at (5.76, 0, 4.92)) ≈ 6.0 units
//   teleport 2 "Skip to: 13"  → token jumped to (69.53, 0, 50.19) — ≈ 78 units in one frame
//   walked     the last two waypoints ≈ 6.0 units
//
// ⇒ ABOUT 12 OF 96 WORLD UNITS WERE WALKED. The other ~87 % were two instant moves. That is the
// report as a number, and it is a property of the game's choreography, not of any scale.
//
// ═══ 3. WHAT THIS CLASS DOES INSTEAD ════════════════════════════════════════
//
// While `MapRoomDriver.Active` — and ONLY then; every prefix returns true otherwise, so flat play
// and the flat map render keep the choreography they were authored for:
//
//   • TeleportPartyToWayPoint keeps its bookkeeping and loses its jump. The prefix forces
//     `point = 0` (nothing is skipped, so the route survives to be walked) and swallows the single
//     PartyInstantMove that call is about to make. m_WaypointsToEnd is then advanced ONLY by
//     `visitedPositions`, which is what MovePartyToFadeIn's own callback (:57-59) already does.
//   • PartyMoveTo (both overloads) is replaced by <see cref="MapPartyTravel.Drive"/>: an ARC-LENGTH
//     drive along the same polyline that reaches a stated fraction of it in a stated number of
//     seconds and never bails out early.
//   • MovePartyToEncounter raises a one-shot flag, and the leg it opens targets arc-length fraction
//     0.5 — "immer in der Mitte der Strecke". Every other leg targets 1.0.
//
// CONSTANT DURATION, VARIABLE SPEED, AND WHY THAT COSTS NO MULTIPLAYER TIMING ON THE EVENT LEG.
// The timed overload is driven for exactly the `duration` the game passed — DelayToEncounter or
// DelayToFadeInBlack, both plain constants on the flow's ScriptableObject
// (decompiled/GH.Runtime/MapTimedFlowConfig.cs:5-9) and therefore already independent of distance.
// So that leg's callbacks fire at exactly the moment they fired before this change; the only thing
// that differs is where the token is while the clock runs. The UNTIMED overload had no duration at
// all — the game walked it at MoveSpeed for distance/MoveSpeed seconds — and it is the one leg
// whose length this class chooses (<see cref="MapPartyTravel.LegSeconds"/>, taken from the game's
// own DelayToFadeInBlack so both halves of a journey take the same time).
//
// THE FADE IS NOT PROPAGATED IN THE ROOM, deliberately. The ONLY thing any onProgress delegate in
// either flow ever does is call TransitionManager.SetFade (MapTimedMovementFlow.cs:63 and :85;
// MapPointsMovementFlow passes null at every call site). Driving the token across the whole route
// and then painting a full-screen black rectangle over it would be a contradiction, so while the
// room stands the drive does not invoke onProgress and clears the fade once per leg. Nothing else
// in the game reads that delegate.
//
// MULTIPLAYER. The travel is NOT a networked transform and this class opens no channel. Every peer
// receives the same CStartMoving_MapClientMessage (decompiled/GH.Runtime/MapChoreographer.cs:786-790)
// and then runs StartMove (:1641) and the whole choreography LOCALLY on its own frame clock; the
// route itself is generated per client with UnityEngine.Random.Range
// (decompiled/GH.Runtime/MapLocation.cs:788-802), so two peers do not even walk the same jittered
// polyline today. What this does NOT hide is that the arrival callbacks (CompleteMoveCallback
// :2159, OnEventTrigger :1906) post into the LOCAL rule-library queue, so a peer in the 3D room and
// a peer on a flat screen reach those posts at slightly different moments — already true between
// any two peers at different frame rates, and bounded here by the difference between the game's own
// untimed leg length and ours.
// ---------------------------------------------------------------------------

/// <summary>
/// The party token's travel, driven along the real route by arc length. See the block comment above
/// for what the game does instead and for the ModBuild 230 log lines that measure it.
/// </summary>
internal static class MapPartyTravel
{
    private const string Scope = "MapRoom";

    /// <summary>Arc-length fraction of the route at which a road-event leg stops — the user's rule
    /// ("immer in der Mitte der Strecke eine Begegnung"). Applied to the polyline the flow hands to
    /// <c>PartyMoveTo</c> measured from the token's CURRENT position, so it is the middle of what is
    /// still to be travelled and not of some remembered original.</summary>
    private const float EncounterFraction = 0.5f;

    /// <summary>Seconds for a leg the game handed no duration for, used when the active flow's own
    /// <c>DelayToFadeInBlack</c> cannot be read. Two seconds is that field's authored default
    /// (decompiled/GH.Runtime/MapTimedFlowConfig.cs:7).</summary>
    private const float LegSecondsFallback = 2f;

    /// <summary>Floor and ceiling on any leg duration, so a zeroed or absurd config value can never
    /// produce a division by zero or a token that crawls for a minute. A setting may configure
    /// pacing; it may never make the map unusable.</summary>
    private const float MinLegSeconds = 0.25f;

    /// <inheritdoc cref="MinLegSeconds"/>
    private const float MaxLegSeconds = 30f;

    /// <summary>Below this squared world distance a polyline is treated as having no length at all —
    /// the degenerate case the game itself hits when a flow hands over an empty array, and which
    /// must complete immediately rather than divide by zero.</summary>
    private const float DegenerateSqr = 1e-6f;

    /// <summary>Squared world distance within which the start of a new leg counts as the end of the
    /// previous one, i.e. the same journey. Only the report line's leg numbering and accumulators
    /// depend on it; nothing about the drive does.</summary>
    private const float SameJourneySqr = 1f;

    private static bool _installed;

    /// <summary>Raised by the prefix on <c>MovePartyToEncounter</c> and consumed by the very next
    /// <c>PartyMoveTo</c>, which that method reaches synchronously (MapTimedMovementFlow.cs:180-181
    /// → :54-56). One-shot: nothing runs between the two calls, so it cannot leak into a later
    /// leg.</summary>
    private static bool _encounterLegPending;

    /// <summary>Raised by the prefix on <c>TeleportPartyToWayPoint</c> for the ONE
    /// <c>PartyInstantMove</c> that method is about to make (MapTimedMovementFlow.cs:27), and
    /// cleared by the first <c>PartyInstantMove</c> that sees it. Every OTHER instant move in the
    /// game — the one that seats the party at a job's starting village
    /// (decompiled/GH.Runtime/MapChoreographer.cs:1704), the map-open placements (:1360, :1383,
    /// :1404) and <c>TeleportToDestination</c> (decompiled/GH.Runtime/MapMovementFlow.cs:41) — is a
    /// legitimate placement and still happens.</summary>
    private static bool _swallowNextInstantMove;

    /// <summary>Where the previous leg ended, used only to decide whether the next leg belongs to
    /// the same journey. NaN until the first leg has run.</summary>
    private static Vector3 _lastLegEnd = new(float.NaN, float.NaN, float.NaN);

    private static int _legIndex;
    private static float _travelWalkedWorld;
    private static int _travelTeleportsSuppressed;
    private static float _travelTeleportWorld;
    private static string _lastSuppressedTeleport = "none";

    /// <summary>
    /// Register the five prefixes exactly once. Called from <see cref="MapTravelConfirm.Install"/>,
    /// which the map room's engage path already runs — for the same reason that class states for not
    /// registering from <c>WorldUIModule</c>: other lanes own that file.
    /// </summary>
    internal static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        VRSession.Harmony?.PatchAll(typeof(TravelDrivePatches));
        VRLog.Info(Scope, "MAP ROOM PARTY TRAVEL installed — while the 3D map room stands, the party "
                          + "token drives the WHOLE route by arc length instead of walking a couple of "
                          + "waypoints and being instant-moved across the rest of the map. READ FROM "
                          + "SOURCE: the game's own timed flow teleports twice per journey "
                          + "(MapTimedMovementFlow.cs:27, once from PrepareTravelTo and once from "
                          + "MovePartyToFadeOutDestination) and hides both behind a full-screen fade to "
                          + "black that a world-space parchment never shows — ModBuild 230's log measures "
                          + "one journey at about 12 of 96 world units actually walked. Both jumps are now "
                          + "suppressed, the road-event leg stops at arc-length 0.500 of the route (the "
                          + "middle), and every leg takes a FIXED number of seconds, so a longer route is "
                          + "travelled FASTER rather than only further. Nothing is replicated: the journey "
                          + "is local presentation on every peer already (MapChoreographer.cs:786 delivers "
                          + "only the START event). Each leg prints one MAP ROOM PARTY TRAVEL line with "
                          + "the endpoints in world units, in parchment-local units and in perceived "
                          + "metres, the route midpoint, where the token ended up and the duration asked "
                          + "for against the duration measured.");
    }

    // ── the drive ───────────────────────────────────────────────────────────

    /// <summary>
    /// Walk <paramref name="token"/> from where it stands, along the polyline
    /// [current position] + <paramref name="positions"/>, to <paramref name="fraction"/> of that
    /// polyline's ARC LENGTH, in <paramref name="seconds"/> seconds.
    ///
    /// <para>ARC LENGTH IS THE WHOLE POINT. The game's coroutine
    /// (decompiled/GH.Runtime/PartyToken.cs:82-127) advances by <c>Vector3.MoveTowards</c> at a
    /// constant <c>MoveSpeed</c> and gives up when a stopwatch runs out, so how far it gets is a
    /// function of the distance — which is exactly the complaint. Parameterising by arc length
    /// inverts that: the distance decides the SPEED and the clock decides nothing but the clock.</para>
    ///
    /// <para><paramref name="visited"/> receives every entry of <paramref name="positions"/> the
    /// drive fully passed, in order, because MovePartyToFadeIn's completion callback
    /// (MapTimedMovementFlow.cs:57-59) uses <c>visitedPositions.Count</c> to advance the flow's own
    /// <c>m_WaypointsToEnd</c>. Under-reporting it would make the next leg start BEHIND the token;
    /// over-reporting it would make the next leg start AHEAD of it. It is counted, not guessed.</para>
    ///
    /// <para>The token's own <c>BeginMoving</c>/<c>CompleteMoving</c> are called rather than
    /// re-implemented, so the UINavigation lock and the arrive-callback contract stay byte-identical
    /// to vanilla — <c>MapChoreographer.cs:1856</c> and <c>:1892</c> both REPLACE that callback
    /// mid-flight and must keep being able to. The coroutine handle goes into the token's own
    /// <c>stopCoroutine</c> field for the same reason: <c>IsMoving</c> and <c>StopMoving</c> must
    /// keep meaning what they mean.</para>
    ///
    /// <para>Yields <c>null</c> rather than the original's <c>WaitForEndOfFrame</c>: end-of-frame
    /// resumes AFTER the frame has been rendered, so every drawn frame would show the previous
    /// frame's position. In VR that is a frame of avoidable lag on the one object the player is
    /// watching, and nothing in either flow depends on the write landing after rendering.</para>
    /// </summary>
    private static System.Collections.IEnumerator Drive(global::PartyToken token, Vector3[] positions,
                                                        float seconds, float fraction,
                                                        List<Vector3>? visited, string legName,
                                                        float askedDuration)
    {
        // The polyline, with the token's own position as its first point: a leg starts where the
        // token IS, not where the flow's array happens to begin.
        var pts = new Vector3[positions.Length + 1];
        pts[0] = token.transform.position;
        for (int i = 0; i < positions.Length; i++)
            pts[i + 1] = positions[i];

        var cum = new float[pts.Length];
        cum[0] = 0f;
        for (int i = 1; i < pts.Length; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
        float total = cum[cum.Length - 1];

        float legSeconds = Mathf.Clamp(seconds, MinLegSeconds, MaxLegSeconds);
        float targetArc = Mathf.Clamp01(fraction) * total;
        Vector3 startWorld = pts[0];
        Vector3 endWorld = pts[pts.Length - 1];
        Vector3 targetWorld = PointAtArc(pts, cum, targetArc, out _);

        float t0 = Time.realtimeSinceStartup;
        int frames = 0;

        token.BeginMoving();

        // The travel fade is a full-screen uGUI Image and the room shows the map as world geometry;
        // see the block comment. Cleared once per leg rather than tracked, because the drive never
        // raises it and no other writer runs during a travel.
        if (global::TransitionManager.s_Instance != null)
            global::TransitionManager.s_Instance.SetFade(0f);

        if (positions.Length == 0 || total * total <= DegenerateSqr)
        {
            // A zero-length or empty leg. Vanilla completes immediately here as well (the foreach at
            // PartyToken.cs:96 simply has nothing to iterate), and a plain travel whose first leg
            // already reached the destination produces exactly this for its second leg.
            if (positions.Length > 0)
                token.transform.position = endWorld;
            if (visited != null)
                for (int i = 0; i < positions.Length; i++)
                    visited.Add(positions[i]);
            Report(legName, pts, cum, total, fraction, targetArc, startWorld, endWorld, targetWorld,
                   token.transform.position, askedDuration, Time.realtimeSinceStartup - t0, frames,
                   visited != null ? visited.Count : -1, positions.Length, degenerate: true);
            _lastLegEnd = token.transform.position;
            token.CompleteMoving();
            yield break;
        }

        float elapsed = 0f;
        int reported = 0;                     // entries of `positions` already handed to `visited`

        // Keep the game's own arithmetic in agreement with ours: MovePartyToFadeOutDestination
        // (MapTimedMovementFlow.cs:73) times its arrival SFX as distance / MoveSpeed, and
        // MapPointsMovementFlow does the same at :23 and :41. Writing the speed this drive actually
        // implies is "concede the flag, own the number" — nothing reads MoveSpeed to MOVE anything
        // any more while the room stands, so the only thing left for it to be is the number those
        // formulas want.
        token.MoveSpeed = targetArc / legSeconds;

        while (elapsed < legSeconds)
        {
            yield return null;
            frames++;
            elapsed += ClockDelta();
            float u = Mathf.Clamp01(elapsed / legSeconds);
            Vector3 previous = token.transform.position;
            Vector3 p = PointAtArc(pts, cum, u * targetArc, out int passed);
            token.transform.position = p;

            // Face the way we are going, as the original does at PartyToken.cs:103. Guarded: a
            // zero-length direction makes Transform.LookAt log an error, every frame.
            Vector3 ahead = p - previous;
            if (ahead.sqrMagnitude > DegenerateSqr)
                token.transform.LookAt(p + ahead);

            // The flat camera still follows the token, exactly as PartyToken.cs:102 does. The room
            // does not use that camera, but a player who leaves the room mid-journey must not find
            // it parked on the origin.
            if (global::CameraController.s_CameraController != null)
                global::CameraController.s_CameraController.m_TargetFocalPoint = p;

            if (visited != null)
                while (reported < passed && reported < positions.Length)
                    visited.Add(positions[reported++]);
        }

        // Land EXACTLY on the target rather than wherever the last frame's delta put us — the
        // difference between the two is what makes "the middle" a measurement and not an
        // approximation.
        token.transform.position = targetWorld;
        PointAtArc(pts, cum, targetArc, out int passedFinal);
        if (visited != null)
            while (reported < passedFinal && reported < positions.Length)
                visited.Add(positions[reported++]);

        _travelWalkedWorld += targetArc;
        Report(legName, pts, cum, total, fraction, targetArc, startWorld, endWorld, targetWorld,
               token.transform.position, askedDuration, Time.realtimeSinceStartup - t0, frames,
               visited != null ? visited.Count : -1, positions.Length, degenerate: false);
        _lastLegEnd = token.transform.position;
        token.CompleteMoving();
    }

    /// <summary>
    /// The point <paramref name="arc"/> world units along the polyline, and the index of the last
    /// polyline point at or behind it. <paramref name="passed"/> counts POLYLINE points, and since
    /// point 0 is the token's own start, it is also the number of entries of the caller's
    /// <c>positions</c> array that have been fully passed.
    /// </summary>
    private static Vector3 PointAtArc(Vector3[] pts, float[] cum, float arc, out int passed)
    {
        passed = 0;
        if (pts.Length == 0)
            return Vector3.zero;
        if (pts.Length == 1)
            return pts[0];
        float total = cum[cum.Length - 1];
        if (arc <= 0f)
            return pts[0];
        if (arc >= total)
        {
            passed = pts.Length - 1;
            return pts[pts.Length - 1];
        }
        int i = 1;
        while (i < cum.Length - 1 && cum[i] < arc)
            i++;
        passed = i - 1;
        float seg = cum[i] - cum[i - 1];
        float f = seg > 1e-5f ? (arc - cum[i - 1]) / seg : 0f;
        return Vector3.Lerp(pts[i - 1], pts[i], f);
    }

    /// <summary>
    /// The seconds the CHRONOS global clock advanced this frame — the clock
    /// <c>PartyToken.PartyMoveCoroutine</c> integrates (decompiled/GH.Runtime/PartyToken.cs:99), so a
    /// paused or slowed world pauses or slows the token here too. Falls back to
    /// <c>Time.deltaTime</c> when no Timekeeper exists, which is the state of the Intro scene and
    /// which no travel can happen in anyway.
    /// </summary>
    private static float ClockDelta()
    {
        try
        {
            Chronos.Timekeeper keeper = Chronos.Timekeeper.instance;
            if (keeper != null && keeper.m_GlobalClock != null)
                return keeper.m_GlobalClock.deltaTime;
        }
        catch (System.Exception)
        {
            // A missing Timekeeper is a real state, not an error; the fallback is correct.
        }
        return Time.deltaTime;
    }

    /// <summary>
    /// Seconds for a leg the game handed no duration for. Read from the ACTIVE flow's own
    /// <c>DelayToFadeInBlack</c> so both halves of a journey take the same time and so a future
    /// change to the game's ScriptableObject moves both together; the constant is only the value
    /// that field is authored with.
    /// </summary>
    private static float LegSeconds()
    {
        global::MapChoreographer? choreo = MapRoomDriver.Choreographer;
        if (choreo != null
            && choreo.movementFlow is global::MapTimedMovementFlow timed
            && timed.mapConfig != null)
        {
            return Mathf.Clamp(timed.mapConfig.DelayToFadeInBlack, MinLegSeconds, MaxLegSeconds);
        }
        return LegSecondsFallback;
    }

    /// <summary>Name of the flow the game has serialised into the scene. Which one it is decides how
    /// a road-event leg is split, and it is a SCENE reference — it cannot be read from source, so it
    /// is printed rather than assumed. ModBuild 230's log settles it for that build: "Skip to:"
    /// (MapTimedMovementFlow.cs:22) appears twice, and only <c>MapTimedMovementFlow</c> prints
    /// it.</summary>
    private static string FlowName()
    {
        global::MapChoreographer? choreo = MapRoomDriver.Choreographer;
        global::MapMovementFlow? flow = choreo != null ? choreo.movementFlow : null;
        return flow != null ? flow.GetType().Name : "no movementFlow on the MapChoreographer";
    }

    // ── the instrument ──────────────────────────────────────────────────────

    /// <summary>
    /// ONE LINE PER LEG, carrying every number the next round needs in order to say whether this
    /// worked without anybody having to watch it happen: both endpoints in world units, in
    /// parchment-local units and in perceived metres, the route's length, the arc-length target and
    /// the route's midpoint, where the token actually stopped and how far that is from the target,
    /// the duration asked for against the duration measured, how many waypoints were reported as
    /// visited, and how much of this journey has been WALKED against how much the game wanted to
    /// teleport.
    ///
    /// <para>It states no mechanism. Every figure on it is either a value this method was handed or a
    /// distance between two points it also prints.</para>
    /// </summary>
    private static void Report(string legName, Vector3[] pts, float[] cum, float total,
                               float fraction, float targetArc, Vector3 startWorld, Vector3 endWorld,
                               Vector3 targetWorld, Vector3 actualWorld, float askedDuration,
                               float measured, int frames, int visitedCount, int waypointCount,
                               bool degenerate)
    {
        bool haveFrame = MapRoomDriver.TryGetParchmentFrame(out Vector3 centre, out float unitsPerMetre);
        if (!haveFrame || unitsPerMetre <= 0f)
            unitsPerMetre = 1f;
        MeshRenderer? parchment = MapRoomDriver.ParchmentRenderer;

        Vector3 midWorld = PointAtArc(pts, cum, total * 0.5f, out _);
        float missed = Vector3.Distance(actualWorld, targetWorld);

        var sb = new System.Text.StringBuilder(1600);
        sb.Append("MAP ROOM PARTY TRAVEL leg ").Append(_legIndex).Append(" '").Append(legName)
          .Append("' (flow ").Append(FlowName()).Append(", ").Append(waypointCount)
          .Append(" waypoint(s) handed over). ");
        sb.Append("ROUTE start ").Append(Describe(startWorld, centre, unitsPerMetre, parchment, haveFrame))
          .Append(" → end ").Append(Describe(endWorld, centre, unitsPerMetre, parchment, haveFrame))
          .Append(". LENGTH ").Append(total.ToString("F2")).Append(" world units = ")
          .Append((total / unitsPerMetre).ToString("F3")).Append(" m perceived at ")
          .Append(unitsPerMetre.ToString("F1")).Append(" u/m")
          .Append(haveFrame ? "" : " (NO PARCHMENT FRAME THIS TICK — the metre column is world units)")
          .Append(". ");
        sb.Append("MIDPOINT of the route ")
          .Append(Describe(midWorld, centre, unitsPerMetre, parchment, haveFrame)).Append(". ");
        sb.Append("TARGET fraction ").Append(fraction.ToString("F3")).Append(" ⇒ arc ")
          .Append(targetArc.ToString("F2")).Append(" u (")
          .Append((targetArc / unitsPerMetre).ToString("F3")).Append(" m) at ")
          .Append(Describe(targetWorld, centre, unitsPerMetre, parchment, haveFrame)).Append(". ");
        sb.Append("ENDED at ").Append(Describe(actualWorld, centre, unitsPerMetre, parchment, haveFrame))
          .Append(", ").Append(missed.ToString("F4")).Append(" u (")
          .Append((missed / unitsPerMetre * 1000f).ToString("F2")).Append(" mm) from the target. ");
        sb.Append("DURATION asked ").Append(askedDuration.ToString("F3")).Append(" s, measured ")
          .Append(measured.ToString("F3")).Append(" s over ").Append(frames).Append(" frame(s)")
          .Append(degenerate ? " (DEGENERATE LEG — nothing to walk, completed on the spot)" : "")
          .Append(". ");
        sb.Append("WAYPOINTS reported visited ")
          .Append(visitedCount < 0 ? "n/a (the untimed overload keeps no list)"
                                   : visitedCount.ToString())
          .Append(" of ").Append(waypointCount)
          .Append(" — this is what the flow uses to advance m_WaypointsToEnd "
                  + "(MapTimedMovementFlow.cs:57), so an under- or over-count is what would make the "
                  + "NEXT leg start behind or ahead of the token. ");
        sb.Append("THIS JOURNEY SO FAR: ").Append(_travelWalkedWorld.ToString("F2"))
          .Append(" world units (").Append((_travelWalkedWorld / unitsPerMetre).ToString("F3"))
          .Append(" m) walked; ").Append(_travelTeleportsSuppressed)
          .Append(" teleport(s) suppressed worth ").Append(_travelTeleportWorld.ToString("F2"))
          .Append(" world units (").Append((_travelTeleportWorld / unitsPerMetre).ToString("F3"))
          .Append(" m) that ModBuild 230 would have jumped instead of drawing. LAST SUPPRESSED: ")
          .Append(_lastSuppressedTeleport).Append(". ");
        sb.Append("HOW TO READ IT: 'ENDED' equal to 'TARGET' to a millimetre is the drive doing what "
                  + "it was asked; 'TARGET' equal to 'MIDPOINT' is the road-event rule; 'measured' "
                  + "equal to 'asked' across two journeys of DIFFERENT length is the constant-duration "
                  + "rule, and the speed that follows from it is the length divided by the duration. A "
                  + "suppressed-teleport count of 0 across a whole journey would mean the game stopped "
                  + "teleporting on its own and that this class is no longer the thing under test.");

        VRLog.Info(Scope, sb.ToString());
    }

    /// <summary>One point in the three spaces the report is read in: raw world units, parchment-local
    /// units (the parchment renderer's own frame, so the figure is comparable across sessions and
    /// seats) and metres from the parchment centre as the player perceives them at the LIVE rig
    /// scale — world units are not metres in this room and the two must never be confused.</summary>
    private static string Describe(Vector3 world, Vector3 centre, float unitsPerMetre,
                                   MeshRenderer? parchment, bool haveFrame)
    {
        string local = parchment != null
            ? Fmt(parchment.transform.InverseTransformPoint(world))
            : "no parchment renderer";
        string metres = haveFrame ? Fmt((world - centre) / unitsPerMetre) : "no parchment frame";
        return $"{Fmt(world)} world / {local} parchment-local / {metres} m from the parchment centre";
    }

    private static string Fmt(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";

    /// <summary>A leg whose start is not the previous leg's end is a new journey: reset the
    /// accumulators the report line carries. Cheap, and it keeps "this journey so far" honest across
    /// a session without needing a second hook on the choreographer.</summary>
    private static void NoteLegStart(Vector3 start)
    {
        bool sameJourney = !float.IsNaN(_lastLegEnd.x)
                           && (start - _lastLegEnd).sqrMagnitude <= SameJourneySqr;
        if (!sameJourney)
        {
            _legIndex = 0;
            _travelWalkedWorld = 0f;
            _travelTeleportsSuppressed = 0;
            _travelTeleportWorld = 0f;
            _lastSuppressedTeleport = "none";
        }
        _legIndex++;
    }

    // ── the patches ─────────────────────────────────────────────────────────

    /// <summary>
    /// The five prefixes. Every one of them runs the game unchanged whenever the 3D map room is not
    /// standing, so the flat screen keeps the fade-and-teleport choreography it was authored for and
    /// this class exists only inside the room.
    /// </summary>
    [HarmonyPatch]
    internal static class TravelDrivePatches
    {
        /// <summary>
        /// The TIMED leg (<c>MovePartyToFadeIn</c>, MapTimedMovementFlow.cs:54). Driven for exactly
        /// the duration the game asked for — that value is a constant on the flow's config, so the
        /// "always the same time" rule is satisfied by HONOURING it rather than by overriding it,
        /// and every callback downstream fires at the moment it fired before this change.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::PartyToken), nameof(PartyToken.PartyMoveTo),
                      new[] { typeof(Vector3[]), typeof(float),
                              typeof(System.Action<List<Vector3>>), typeof(System.Action<float>) })]
        private static bool TimedMovePrefix(global::PartyToken __instance, Vector3[] positions,
                                            float duration,
                                            System.Action<List<Vector3>> callback)
        {
            bool encounter = _encounterLegPending;
            _encounterLegPending = false;
            if (!MapRoomDriver.Active || __instance == null || positions == null)
                return true;

            var visited = new List<Vector3>();
            __instance.SetOnArriveCallback(delegate { callback?.Invoke(visited); });
            NoteLegStart(__instance.transform.position);
            __instance.stopCoroutine = __instance.StartCoroutine(
                Drive(__instance, positions, duration, encounter ? EncounterFraction : 1f, visited,
                      encounter ? "road-event leg, stops at the middle of the route"
                                : "timed leg, drives the whole route",
                      duration));
            return false;
        }

        /// <summary>
        /// The UNTIMED leg (the final approach, MapTimedMovementFlow.cs:81; every leg of
        /// <c>MapPointsMovementFlow</c>). This is the one the game timed as
        /// <c>distance / MoveSpeed</c> — the only leg whose length this class chooses, and the
        /// reason it chooses one at all is that a duration proportional to distance is exactly what
        /// the user asked to be rid of.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::PartyToken), nameof(PartyToken.PartyMoveTo),
                      new[] { typeof(Vector3[]), typeof(System.Action), typeof(System.Action<float>) })]
        private static bool UntimedMovePrefix(global::PartyToken __instance, Vector3[] positions,
                                              System.Action callback)
        {
            _encounterLegPending = false;
            if (!MapRoomDriver.Active || __instance == null || positions == null)
                return true;

            float seconds = LegSeconds();
            __instance.SetOnArriveCallback(callback);
            NoteLegStart(__instance.transform.position);
            __instance.stopCoroutine = __instance.StartCoroutine(
                Drive(__instance, positions, seconds, 1f, null,
                      "untimed leg, drives the whole remaining route", seconds));
            return false;
        }

        /// <summary>
        /// Keep the bookkeeping, lose the jump. <c>point</c> is forced to 0 so no waypoint is
        /// consumed by the teleport (the route survives for the drive to walk), and the single
        /// <c>PartyInstantMove</c> the method is about to make is flagged for suppression. The
        /// method's other effects — its two log lines and, on the second call, the camera reset —
        /// still run.
        ///
        /// <para>WHAT THE GAME ASKED FOR IS MEASURED BEFORE IT IS REFUSED. The arc length between the
        /// token and the waypoint it wanted to jump to is exactly "how much of the journey was not
        /// drawn", and it goes on the next report line. That is the number that makes "es bewegt sich
        /// nicht über die ganze Strecke" ("it does not move across the whole distance")
        /// falsifiable in both directions.</para>
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::MapTimedMovementFlow), "TeleportPartyToWayPoint")]
        private static void TeleportPrefix(global::MapTimedMovementFlow __instance,
                                           global::PartyToken partyoken, ref int point)
        {
            if (!MapRoomDriver.Active || __instance == null || partyoken == null)
                return;
            Vector3[] route = __instance.m_WaypointsToEnd;
            if (route == null || route.Length == 0)
                return;

            int wanted = Mathf.Clamp(point, 0, route.Length - 1);
            float jumped = 0f;
            Vector3 from = partyoken.transform.position;
            for (int i = 0; i <= wanted; i++)
            {
                jumped += Vector3.Distance(from, route[i]);
                from = route[i];
            }
            if (wanted > 0)
            {
                _travelTeleportsSuppressed++;
                _travelTeleportWorld += jumped;
                _lastSuppressedTeleport =
                    $"the game asked to skip to waypoint {wanted} of {route.Length - 1}, which would "
                    + $"have moved the token {jumped:F2} world units in one frame without drawing a step";
            }

            // Nothing skipped. m_WaypointsToEnd is then advanced ONLY by visitedPositions, which is
            // what MovePartyToFadeIn's own callback already does with it.
            point = 0;
            _swallowNextInstantMove = true;

            // The flow's own SFX arithmetic (MapTimedMovementFlow.cs:73) divides the remaining route
            // by MoveSpeed to time the arrival sound. Give it the speed the next leg will actually
            // run at, so the sound lands where it always landed relative to the arrival.
            float remaining = 0f;
            for (int i = 1; i < route.Length; i++)
                remaining += Vector3.Distance(route[i - 1], route[i]);
            float legSeconds = LegSeconds();
            if (remaining > 0f && legSeconds > 0f)
                partyoken.MoveSpeed = remaining / legSeconds;
        }

        /// <summary>
        /// Swallow the ONE instant move a suppressed teleport is about to make, and nothing else. The
        /// flag is raised immediately before the call it belongs to (MapTimedMovementFlow.cs:27) and
        /// is cleared here whether or not the room is up, so it can never survive to affect a later,
        /// legitimate placement.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::PartyToken), nameof(PartyToken.PartyInstantMove))]
        private static bool InstantMovePrefix(global::PartyToken __instance)
        {
            bool swallow = _swallowNextInstantMove;
            _swallowNextInstantMove = false;
            return !swallow || !MapRoomDriver.Active || __instance == null;
        }

        /// <summary>
        /// Flag the leg <c>MovePartyToEncounter</c> is about to open. It calls
        /// <c>MovePartyToFadeIn</c> synchronously (MapTimedMovementFlow.cs:180-181), which calls
        /// <c>PartyMoveTo</c> synchronously (:54-56), so the flag is consumed on the same stack and
        /// can never be read by any other leg.
        ///
        /// <para>ONLY the TIMED flow raises it. <c>MapPointsMovementFlow.MovePartyToEncounter</c>
        /// (decompiled/GH.Runtime/MapPointsMovementFlow.cs:33-38) already hands over a PREFIX of the
        /// route — <c>PercentMovedToEncounter</c>, authored at 0.571 — so halving what THAT passes
        /// would stop the token at 29 % of the journey. If that flow ever turns out to be the one in
        /// the scene, the drive walks the prefix it was given in constant time and the report line
        /// names the flow, so the difference is readable rather than silent.</para>
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::MapTimedMovementFlow),
                      nameof(MapTimedMovementFlow.MovePartyToEncounter))]
        private static void EncounterPrefix()
        {
            _encounterLegPending = MapRoomDriver.Active;
        }
    }
}
