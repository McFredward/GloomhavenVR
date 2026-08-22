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
        if (_standDown)
            return;
        if (!EnsureReflection())
            return;

        AdventureMapUIManager? mgr = Manager();
        GameObject? options = mgr != null ? _travelOptions?.GetValue(mgr) as GameObject : null;

        if (!MapRoomDriver.Active || questWindow == null || options == null)
        {
            Unpark(!MapRoomDriver.Active ? "map room stood down"
                : questWindow == null ? "no quest window is floated"
                : "the game's travel options are gone");
            // Leaving the room clears a park stand-down: whatever went wrong was about THIS visit's
            // objects, and re-entering rebuilds all of them.
            if (!MapRoomDriver.Active)
                _parkStandDown = false;
            return;
        }
        if (_parkStandDown)
            return;

        if (!ReferenceEquals(_host, questWindow))
        {
            Unpark("a different quest window took over");
            if (!Park(questWindow, options))
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
        bool active = options.activeInHierarchy;
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
                              + $"is '{(btn != null ? btn.name : "<not found>")}' — the SAME ExtendedButton "
                              + "the flat game uses, so its label (Reisen / Quest erneut spielen), its "
                              + "interactable state and every guard behind OnTravelButtonClick are the "
                              + "game's own. It moves, scales, occludes and goes home with the window.");
        }
    }

    /// <summary>Teardown — hand the container back before the room disappears under it.</summary>
    internal static void Reset() => Unpark("map room teardown");

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
    private static bool Park(UIWindow questWindow, GameObject options)
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

        if (!_homeRecorded)
        {
            _homeRecorded = true;
            _optionsHome = rect.parent;
            _optionsHomeIndex = rect.GetSiblingIndex();
            _homeAnchorMin = rect.anchorMin;
            _homeAnchorMax = rect.anchorMax;
            _homePivot = rect.pivot;
            _homeAnchoredPos = rect.anchoredPosition;
            _homeLocalRotation = rect.localRotation;
            _homeLocalScale = rect.localScale;
        }

        EnsureHold(options);
        SetHidden(true);
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
        if (_host == null && _hold == null && _layoutIgnore == null)
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

        if (!EnsureReflection())
            return;
        AdventureMapUIManager? mgr = Manager();
        if (mgr == null || _travelOptions?.GetValue(mgr) is not GameObject options)
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
        VRLog.Info(Scope, $"MAP TRAVEL CONFIRM: travel options handed back to their own home ({why}) — "
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
            return false;
        }

        // THE POSE IS A WINDOW-LOCAL POINT, NOT AN anchoredPosition (ModBuild 196, unchanged).
        Vector2 wantPivot = new(_anchorPivot.x + dials.x * windowHeight,
                                _anchorPivot.y + dials.y * windowHeight);

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
    ///   <see cref="AppendRecommendation"/>. THIS IS A NUMBER TO TYPE IN, NEVER A POSE THAT IS
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
    ///   ("auch auf der x-achse verschoben"), so it is derived rather than left at 0.</item>
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
