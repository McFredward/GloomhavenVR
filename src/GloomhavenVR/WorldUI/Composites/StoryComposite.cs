using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE QUEST-INTRO STORY IS ONE WINDOW, AND THE POINT OF NO RETURN IS AN EDGE, NOT A STATE.
///
/// <para><b>USER REPORT (2026-08-23, .planning/debug/getrennt2.jpg, verbatim):</b> <i>"Das
/// Storyfenster ist in zwei Fenster aufgeteilt: Das Bild und den Dialog. Nur der Dialog ist 'blau'
/// also ein MP synchronisiertes Fenster. Siehe getrennt2.jpg. Das soll nicht sein. Dialog und Bild
/// soll ein einziges 'blaues' Fenster sein, mit dem Dialog unter dem Bild. Weiterhin darf dieses
/// Story-Fenster kein 'x' haben, da man durchklicken muss. Zu diesem Zeitpunkt ist der 'Point of
/// Return' schon überschritten, d.h. zB Händler und co. darf man zu diesem Zeitpunkt nicht mehr
/// öffnen können (die buttons sollen das auch mit ihrer spieleigenen animation anzeigen). Alle
/// anderen Fenster sollen dabei dann geschlossen werden."</i></para>
///
/// <para><b>USER REPORT THAT REWROTE THIS CLASS (ModBuild 231 hardware, verbatim):</b> <i>"Dannach
/// habe ich versucht im Singleplayer anschließend eine Quest zu laden. Nach der Begegnung sind alle
/// Fenster verschwunden und es nichts mehr weiter passiert. Kein Storyfenster ist erschienen wie
/// erwartet."</i> He was STUCK — the log ends with the game being quit.</para>
///
/// <para>=====================================================================================
/// SECTION 1 — WHAT ModBuild 231 GOT WRONG, IN ONE SENTENCE, SO IT IS NOT REBUILT
/// =====================================================================================</para>
///
/// <para>The user's ruling names an INSTANT — <i>"Alle anderen Fenster sollen <b>dabei</b> dann
/// geschlossen werden"</i>, i.e. the windows that are standing WHEN the point of no return is
/// crossed. ModBuild 231 implemented it as a STANDING SUPPRESSION instead: a level-triggered
/// re-sweep that kept releasing anything that floated after the edge, plus a convert-loop hold with
/// no bound. The pre-scenario loadout sequence's own windows are exactly what floats after that
/// edge, so the gate ate the feature it was supposed to protect. From the ModBuild 231
/// Player.log, in order:</para>
/// <code>
///   :94226 POINT OF NO RETURN OPENED … CLOSED 2 floated window(s) ['UI Quest Popup',
///          'Quest Log Manager'] and LOCKED 7 guildmaster destination(s)      ← the ruling. Correct.
///   :94235 CATCH-ALL: unknown scenario window 'UI Loadout Window' (ID None) floated
///   :94241 CATCH-ALL FUSE: window 'UI Loadout Window' re-floated 4× in 60s — a cycling HUD
///          banner, not a waiting decision; SUPPRESSED FOR THIS SESSION            ← THE DEADLOCK
///   :94470 POINT OF NO RETURN: re-closed 1 window(s) that floated after the gate opened
///          ['Map Story Window'] (sweep 1 of 3)                             ← it ate the STORY box
///   :94498 MODAL PRE-CONVERT BLACKOUT: 'UI Quest Popup' was still un-floated after 9 frames
///   :94511 MODAL LIVENESS CENSUS: 0 floated window(s) — 0 ARMED                 ← nothing on screen
/// </code>
///
/// <para><b>AND THE MECHANISM OF :94241 IS THE WHOLE LESSON.</b> The catch-all's churn fuse counts
/// every tick on which it ENROLS a window that the mod is not already floating
/// (<c>ModalFallback.10.CatchAll.cs</c>: <c>bool oursAlready = IsFloatedByUs(window)</c> →
/// <c>continue</c>, otherwise <c>FloatChurn[name].Count++</c>, and <c>ChurnMaxFloats</c> is
/// <b>3</b>). A convert-loop hold makes <c>IsFloatedByUs</c> false FOREVER while the game keeps the
/// window open — so a hold that lasts four ticks is indistinguishable, to that fuse, from a window
/// cycling open/closed four times, and the fuse then suppresses that window's NAME for the rest of
/// the session. The mod's own suppression burned the loadout screen out of the float set
/// permanently. This is [[a-fuse-cannot-tell-a-hand-from-a-loop]] a second time, from the other
/// side: last time the fuse miscounted the player, this time it miscounted us.</para>
///
/// <para><b>THE RULE THAT FALLS OUT OF IT, AND IT IS THE DESIGN OF THIS FILE.</b> The mod's float
/// set is a MIRROR of the game's open-window set. Any mechanism that holds the two out of step —
/// a re-sweep, a standing hold, a suppression list — is a loop generator and a fuse burner. So the
/// only durable way to keep a window shut past the point of no return is to CLOSE IT IN THE GAME
/// and let the mirror follow for free. That is what <see cref="OpenGate"/> now does, through the
/// project's existing single close path, and it is why there is no sweep and no standing hold left
/// to restore.</para>
///
/// <para>=====================================================================================
/// ModBuild 459 — THE EDGE WAS TWO MINUTES LATE, AND THE USER HAD ALREADY NAMED THE RIGHT ONE
/// =====================================================================================</para>
///
/// <para>Everything below about the SHAPE of the gate stands. What changed is WHEN it fires.
/// <see cref="PointOfNoReturn"/> gains a third and earliest term — the QUEST CONFIRM, read from
/// <see cref="QuestJourneyCurtain.PartyCommitted"/> — because that is the instant the user means:
/// <i>"Beim point-of-no-return (Quest bestätigt)"</i>. Up to 458 the sweep waited for the
/// quest-start story message or the loadout screen, and the ModBuild 457 logs put a number on the
/// gap: confirm at HOST <c>Player.log:29962</c> / PEER <c>remote/Player.log:22011</c>, sweep at
/// <c>:40747</c> / <c>:32722</c> — ~8900 frames, about two minutes. Both gate lines read
/// <c>CLOSED 3 of 8 NAMED member(s) … (5 member(s) were neither open nor floated)</c>: the sweep
/// was CORRECT and simply ran too late to see the peer's merchant, which floated at
/// <c>remote:16136</c>, stood through the whole journey and left only when a hand pressed its X at
/// <c>remote:23681</c>. <i>"Beim Mitspieler blieb der Händler offen nach dem point of no return —
/// ALLE Fenster sollen sich da schließen."</i></para>
///
/// <para><b>AND MOVING THE EDGE IS ALSO WHAT GIVES THE QUEST INFO WINDOW BACK.</b>
/// <see cref="RaiseCurtain"/> freezes the set of everything the mod is floating at the CURTAIN's
/// edge, and in 457 'UI Quest Popup' was still floating then, so it became a frozen member and was
/// refused for the rest of the interval — including when the game re-opened it for the
/// private-quest step (<c>Player.log:44367</c>, <c>ActiveDisplay=BATTLE_GOALS</c> at <c>:44723</c>,
/// <c>MUTUAL HOLD MEASURED</c> at <c>:44401</c>). Closed at the confirm, it is not floating when the
/// curtain freezes, so it cannot be a member and the game's own re-open floats normally onto the
/// map table's right far corner. <i>"Es ist dann wieder aufgetaucht sobald die private quests
/// ausgewählt werden sollen in der rechten Ecke des Tischs."</i></para>
///
/// <para>=====================================================================================
/// SECTION 2 — THE SHAPE OF THE GATE NOW
/// =====================================================================================</para>
/// <list type="number">
/// <item><b>ONE SHOT, AT THE RISING EDGE.</b> <see cref="Tick"/> compares
/// <see cref="PointOfNoReturn"/> against <see cref="_gateOpen"/> and calls <see cref="OpenGate"/>
/// or <see cref="CloseGate"/> on the transition. Nothing else in this class closes a window, ever.
/// The ModBuild 231 belt-and-braces (<c>MaxSweepsPerGate</c>, <c>SweepIntervalSeconds</c>, the
/// <c>_sweeps</c> counter) is DELETED, not narrowed. <b>Why it was wrong and must not come back:</b>
/// a level-triggered arm cannot tell "a window I already closed came back" from "a window that had
/// every right to open just opened", because both look identical to it — a float that exists and
/// is not the one window it was told to keep. The story box, the quest popup, the battle-goal
/// picker and the party display are all in the second category, and it closed all four. There is no
/// interval short enough or fuse tight enough to fix a rule that is asking the wrong question; the
/// question "is this window one of the ones the player had standing when he committed?" can only be
/// answered at the instant he committed, so it is only asked there.</item>
///
/// <item><b>A NAMED SET, NOT AN EXCLUSION.</b> <see cref="CollectNamedSet"/> enumerates the windows
/// to close from the GAME's own serialized references — the five guildmaster destinations through
/// <c>GuildmasterDestinations.ModeWindow</c> (which reads <c>UIGuildmasterHUD.shopWindow</c> etc.)
/// and the map's three quest popups through <c>UIQuestPopupManager</c>'s own fields. Membership is
/// then re-verified with <c>GuildmasterDestinations.IsDestination</c> and printed per window, so the
/// log PROVES the set rather than asserting it. ModBuild 231 used
/// <c>ModalFallback.ReleaseFloatsExcept(keep, …)</c> — an exclusion rule, "everything except one" —
/// and this project has been burned by exclusion rules that exempt the wrong subject
/// ([[a-remedy-knows-one-writer]]). Under an exclusion, anything the loadout sequence opens is out
/// of scope BY LUCK; under a named set it is out of scope BY CONSTRUCTION, because the party
/// display, the battle-goal picker, the loadout screen and the story box are not members and no
/// code path can add them.</item>
///
/// <item><b>THE CLOSE IS THE GAME'S OWN CLOSE.</b> A destination goes through
/// <c>GuildmasterDestinations.CloseMode</c> → <c>ModalFallback.CloseFloatedWindow</c>, which is
/// byte-for-byte the window's own X button: flag the parallel float for release, run the mode's
/// <c>Exit</c> through <c>LeaveMode</c> (the ModBuild 184/195 party-display fix), then
/// <c>Escape()</c>/<c>Hide()</c> the game window. A quest popup goes through
/// <c>CloseFloatedWindow</c> directly. ModBuild 231 did a PRESENTATION release instead, and argued
/// for it on the grounds that a game-side close could not be allowed near the loadout screen, whose
/// <c>Hide()</c> would abandon the scenario. That argument was correct FOR AN EXCLUSION RULE, which
/// cannot promise what it is pointing at. It does not apply to a named set: the loadout screen is
/// not a member and cannot become one.</item>
///
/// <item><b>THE HOLD IS A TWO-TICK BRIDGE, NOT A POLICY.</b> See <see cref="HoldsBack"/> and
/// <see cref="HoldBridgeTicks"/>.</item>
///
/// <item><b>AND THERE IS A FLOOR UNDER ALL OF IT.</b> See <see cref="TripsDeadlockFloor"/>: if this
/// class's own suppression is the reason the room is empty, it says so by name and then lifts
/// itself. "Lift the verdict, keep the count" — the count still tells a later round the rule fired,
/// but the player is never left with nothing to click.</item>
/// </list>
///
/// <para><b>THE CAP LOCK IS UNCHANGED AND IS NOT PART OF THE DEFECT.</b>
/// <see cref="SetDestinationsLocked"/> drives <c>UIGuildmasterButton.ToggleGreyOut</c> and
/// <c>UIGuildmasterHUD.DisableCityEncounter</c> — the game's own mechanism, which worked ("LOCKED 7
/// guildmaster destination(s) plus the city encounter") and which <c>MapButtonRail.Pressable</c>
/// already reads through <c>Toggle.IsInteractable()</c>. The ONE fix here is that the UNLOCK used to
/// abort: the ModBuild 231 log's very last mod lines are <c>the guildmaster lock-out threw
/// (NullReferenceException)</c> followed by <c>UNLOCKED 0 guildmaster destination(s)</c>, because
/// the gate closes exactly when the scenario scene tears the HUD down and
/// <c>ToggleGreyOut</c> dereferences <c>EventSystem.current</c> and <c>UIInfoTools.Instance</c>
/// (decompiled UIGuildmasterButton.cs:238-252), so the FIRST button's throw skipped the other six.
/// The restore is now per-button and the record is cleared in a <c>finally</c>.</para>
///
/// <para>=====================================================================================
/// SECTION 3 — THE COMPOSITE ITSELF (the picture and the dialog are ONE window)
/// =====================================================================================</para>
///
/// <para>The quest intro is built by <c>UILoadoutQuestWindow</c>
/// (decompiled/GH.Runtime/UILoadoutQuestWindow.cs) and it deliberately puts its two halves in two
/// different <c>UIWindow</c>s on two different canvases:</para>
/// <code>
///   UILoadoutQuestWindow.Show(quest, …)                                 (:47)
///     imagePaper.LoadImages([quest.LoadoutImageId], …)                  (:52)  ← THE IMAGE
///       → ShowLoadoutBackground → imagePaper.Show(quest.LoadoutImageId) (:60)
///       → StartCoroutine(ShowIntroductionText(quest))                   (:61)
///           yield 0.4 s, then
///           Singleton&lt;MapStoryController&gt;.Instance.Show(…)          (:88-93) ← THE DIALOG
/// </code>
/// <para>They are roots of two different canvases (<c>Story Canvas</c> and <c>Campaign Canvas</c>)
/// and both log lines end with <c>nearest ancestor UIWindow &lt;none&gt;</c>, so no walk over
/// parents can relate them and no declarative suppression rule can answer them — the image's window
/// floats FIRST and a rule that is only consulted for windows that have not floated yet arrives too
/// late. What is needed is an ACTIVE step at the moment the dialog arrives, which is
/// <see cref="Tick"/>: it MOVES the picture into the story window.</para>
///
/// <para><b>WHICH RECT IS THE PICTURE — ModBuild 231 PARKED NOTHING AT ALL.</b> That build asked
/// for <c>StoryImageViewer.container</c>, and the hardware log shows what that is: forty repeats of
/// <c>refused to park 'UI Loadout Quest Information' (1920x1080 px) … it covers more than 90 % of
/// its own window</c>. <c>container</c> IS the loadout screen's full-window group (it is the parent
/// of the 1920x1080 <c>Blur</c>), so the guard was right and the candidate was wrong. The rect that
/// actually carries the illustration is <c>StoryImageViewer.imageHolder</c> — the <c>Image</c> the
/// viewer assigns the loaded sprite to and switches on:</para>
/// <code>
///   StoryImageViewer.Show(image, …) → token.Get(image, sprite =&gt; {          (:209-241)
///       imageHolder.sprite = sprite;                                        (:227) ← THE PICTURE
///       imageHolder.gameObject.SetActive(true);
///       container.SetActive(true);   ← the GROUP, which is what 231 asked for
///   })
/// </code>
/// <para>and the ModBuild 231 fit line names it at the size this class always expected:
/// <c>top (rendered rects): … 'Holder/Paper' 1280x720px</c>, i.e. 0.67 × 0.67 of the window.
/// <see cref="ImageSubtree"/> now asks for <c>imageHolder</c> first, keeps <c>container</c> and the
/// viewer's own transform as fallbacks, keeps <see cref="MaxParkFractionOfWindow"/> on ALL of them
/// (it is the guard that stopped the wrong parent), and adds the POSITIVE test the round asked for:
/// the chosen rect must carry a <c>Graphic</c> with a sprite. And it reports its choice ONCE per
/// loadout window instead of once per tick — forty identical warnings for one refusal is an
/// instrument defect, not a diagnosis.</para>
///
/// <para>=====================================================================================
/// SECTION 4 — ModBuild 233: WHY THE STORY WINDOW IS THE HOST, AND WHAT HAPPENS TO THE OTHER ONE
/// =====================================================================================</para>
///
/// <para><b>USER REPORT (ModBuild 232 hardware, .planning/debug/story_getrennt3.jpg, verbatim):</b>
/// <i>"Das Dialogfenster der Story und das Fenster mit dem Bild sind immer noch zwei getrennte
/// Fenster. Außerdem ist die Zeichnung/Bild nicht mehr richtig zu sehen!"</i> The screenshot shows
/// the loadout window floating with a close cross and a BLANK PARCHMENT where the illustration
/// should be, and the story dialog floating below it on its own blue bar. Two windows, and the
/// picture gone as well.</para>
///
/// <para><b>THE PICTURE WENT MISSING FOR A REASON THE LOG NAMES TWICE AND NEITHER LINE IS A
/// MYSTERY.</b> The ModBuild 232 log picks the rect at <c>'Image' is 1280x720 px … (67 % x 67 %)</c>
/// (:5224) and then reports the built composite at <c>'Image' 0x0 px</c> (:5225). Between those two
/// measurements <see cref="Park"/> set <c>anchorMin = anchorMax = (0.5,0.5)</c>. <c>imageHolder</c>
/// is a STRETCH child of <c>'Paper'</c>, so its <c>sizeDelta</c> is the inset pair and its drawn
/// size came entirely from its parent: collapsing the anchors to a point without writing
/// <c>sizeDelta</c> leaves <c>rect.size == sizeDelta ==</c> 0x0. The same arithmetic run backwards
/// explains :5408's <c>'Image' is 1814x1020 px</c> — that is AFTER the unpark restored the stretch
/// anchors and AFTER <c>UILoadoutQuestWindow.FinishIntroduction</c> expanded <c>'Paper'</c>, so the
/// rect was reporting the paper's post-tween size. One rect, three different numbers, none of them
/// wrong: the thing being measured is DRIVEN BY ITS PARENT and it is ANIMATED. The fix is to stop
/// depending on either — see <see cref="Park"/>, which captures the measured size into
/// <c>sizeDelta</c> so the composite OWNS the number from then on, and the parked rect is an
/// anchor-point rect with <c>ignoreLayout</c> whose parent is no longer <c>'Paper'</c>, so the
/// expand tween cannot reach it at all.</para>
///
/// <para><b>AND IT WAS PARKED BEFORE IT HAD ANYTHING TO DRAW.</b> :5224 ends
/// <c>its sprite is NOT LOADED YET</c>, and that was printed as part of an ACCEPTANCE. The
/// illustration arrives through an addressable (<c>StoryImageViewer.LoadImages</c> →
/// <c>token.Load</c> → <c>Addressables.LoadAssetAsync&lt;Sprite&gt;</c>, StoryImageViewer.cs:59-76),
/// and until it lands the viewer keeps <c>imageHolder.gameObject</c> DEACTIVATED
/// (<c>LoadImages</c> :201, re-activated only at <c>Show</c> :234). So the mod parked an inactive,
/// sprite-less, parent-driven rect and called it a picture. <see cref="Judge"/> now WAITS on all
/// three facts — laid out, active, sprite present — and "not ready yet" is an Info state that says
/// so, never a refusal.</para>
///
/// <para><b>WHICH WINDOW IS THE ONE WINDOW: THE STORY WINDOW, AND THE LOADOUT SCREEN LOSES ON THREE
/// COUNTS THAT ARE ALL FACTS RATHER THAN PREFERENCES.</b>
/// <list type="number">
/// <item><b>The blue bar.</b> The user asked for one <i>blaues</i> — multiplayer-synced — window.
/// <c>SharedWindows</c> keys the shared kind <c>MapStory</c> off
/// <c>MapStoryController.Instance.window</c> (SharedWindows.cs:161-163), i.e. off the STORY window
/// instance. Hosting on it costs nothing; hosting on the loadout screen would mean teaching
/// <c>SharedWindows</c> and the net record a second identity for the same kind.</item>
/// <item><b>The missing X.</b> "Weiterhin darf dieses Story-Fenster kein 'x' haben, da man
/// durchklicken muss." The story window already has none: <c>ModalFallback.8.Convert.cs</c>'s
/// <c>isStoryBox</c> chain resolves <c>MapStoryController.window</c> by reference and puts it in the
/// no-X family. The loadout window is not in it and the ModBuild 232 screenshot shows its cross.</item>
/// <item><b>The refusal has to be able to NAME the loser, and only one of the two can be named.</b>
/// <c>FloatRefusalTable</c> identifies a window by a component on its OWN GameObject, never by name
/// ([[containment-is-not-identity]]). <c>UILoadoutManager</c> is on the loadout window's own
/// GameObject — the ModBuild 232 identity line reads <c>components [RectTransform, CanvasRenderer,
/// UILoadoutManager, CanvasGroup, UIWindow, Image, ControllerInputArea]</c> and
/// <c>[RequireComponent(typeof(UIWindow), typeof(ControllerInputArea))]</c> makes it so by
/// construction. <c>MapStoryController</c> is NOT on the story window: it holds it as a serialized
/// <c>window</c> field (MapStoryController.cs:41-42). So a table row can refuse the loadout screen
/// and could not have refused the story box.</item>
/// </list></para>
///
/// <para><b>WHAT HAPPENS TO THE LOADOUT SCREEN, AND WHY IT IS NOT THE ModBuild 231 HOLD.</b> It is
/// WITHDRAWN from the float set by <c>FloatRefusalTable</c>'s conditional row, held by
/// <see cref="HoldsStoryFloatBack"/>. The difference from 231's convert-loop hold is not a matter
/// of degree:</para>
/// <list type="bullet">
/// <item><b>The fuse never sees it.</b> <c>ModalFallback.10.CatchAll.cs</c> asks
/// <c>FloatRefusalTable.Refuse</c> at the TOP of its loop (:234) and <c>continue</c>s — before
/// <c>oursAlready</c>, before <c>CatchAllEligible</c> and before the <c>FloatChurn[name].Count++</c>
/// at :289. A convert-loop hold, by contrast, leaves the window to be re-enrolled and re-counted
/// every single tick, which is exactly how 231 reached "re-floated 4× in 60s — SUPPRESSED FOR THIS
/// SESSION".</item>
/// <item><b>It can withdraw a float that already exists</b> (<c>WithdrawRefusedFloat</c>: drop
/// <c>Sticky</c>, let the ordinary release loop take the panel, grab bar and slot down, let
/// <c>CanvasConversion.Release</c> restore the exact 2D home). A hold can only ever starve a
/// conversion that has not happened yet, and the loadout screen floats long BEFORE the story box
/// opens — which is the other reason 231's shape could not have worked.</item>
/// <item><b>It is a LEVEL with a bounded number of edges.</b> The claim is recomputed from live
/// measurement every tick and lapses within one tick when the composite stops standing; and
/// <see cref="MaxWithdrawCycles"/> caps how many times it may be raised for one loadout window, at a
/// number DERIVED from the same fuse (see that field). Past the cap the claim never stands again for
/// that window, says so once, and the presentation degrades to the ModBuild 232 status quo — two
/// windows — which is ugly and is not a deadlock.</item>
/// <item><b>Nothing is written to the game.</b> No <c>Hide</c>, no <c>Escape</c>, no
/// <c>SetActive</c>, no <c>CanvasGroup</c>. The loadout screen keeps its ordinary 2D rendering on
/// 'Campaign Canvas', which the 3D map room does not draw; its confirm button, its hotkeys and its
/// own <c>FinishIntroduction</c> tween are untouched and it floats again, with everything on it, the
/// moment the intro is over.</item>
/// </list>
///
/// <para><b>THE ONE GREP THAT IS ONLY TRUE IF THE COMPLAINT IS FIXED:</b>
/// <c>STORY COMPOSITE ONE WINDOW: CONFIRMED</c>. See <see cref="ReportOneWindow"/> — every clause of
/// it is measured this tick and the NOT ACHIEVED form prints the same measurements, so the line
/// cannot be true while the screenshot is still two windows.</para>
///
/// <para>=====================================================================================
/// SECTION 5 — ModBuild 234: THE GAP, AND THE CURTAIN
/// =====================================================================================</para>
///
/// <para><b>ModBuild 233 WORKED AND THE LOG SAYS SO.</b> Player.log:15300 —
/// <c>STORY COMPOSITE ONE WINDOW: CONFIRMED … story box 'Map Story Window' floated=True; loadout
/// screen 'UI Loadout Window' floated=False; parked picture 'Image' 1280x720 px,
/// activeInHierarchy=True, sprite='CR_DF_03_Demon (VR-…'</c>, and story2.jpg is one window with the
/// crypt illustration over the dialog on one blue bar with no cross. NOTHING IN SECTIONS 1-4 IS
/// REDESIGNED HERE. Two separate faults are fixed.</para>
///
/// <para><b>FAULT 1 — THE GAP. USER REPORT (verbatim):</b> <i>"allerdings ist der Abstand zwischen
/// dem Bild und dem Dialog viel zu groß, das Dialogfenster soll direkt darunter angezeigt
/// werden."</i></para>
///
/// <para><b>AND THE LOG MEASURES IT WITHOUT ANY INFERENCE.</b> The fit line for the composite
/// (:15304) lists the rendered rects with their host-local positions:</para>
/// <code>
///   union (-640,-516)..(640,1260) px [frame-clamped]; top (rendered rects):
///     'Map Story Window/Image'     1280x720 px at (-640, 540)   ← the picture: y 540 … 1260
///     'Dialog/DialogContent'       1040x168 px at (-520,-516)   ← the DRAWN dialog: y -516 … -348
/// </code>
/// <para>Picture bottom 540, painted dialog top -348: <b>888 px of nothing</b> between them, against
/// an <see cref="ImageGapPx"/> of 24. One tick later, with eight lines of text instead of three
/// (:15323), it is 786 px. Half the composite's 1776 px of height was empty — the third of a window
/// of black in story2.jpg, measured.</para>
///
/// <para><b>WHERE THE 888 CAME FROM: THE RECT BEING MEASURED WAS NOT THE DIALOG.</b>
/// <see cref="ApplyPose"/> placed the picture 24 px above <c>MapStoryController.dialogBox</c>'s
/// RectTransform — and <c>dialogBox</c> is a <c>UICharacterStoryBox</c>, a TALL, MOSTLY TRANSPARENT
/// HOST whose top edge sits ~1030 px above the text it holds. The thing the player sees is
/// 'Dialog/DialogContent' + 'Background Container/Background' + 'DialogContent/Frame', a 168-270 px
/// strip pinned to that host's BOTTOM. The gap was correct to 24 px against a box that is not drawn.
/// [[tight-box-is-not-the-rect]] — a tight drawn box is not the authored rect — for the second time
/// in this project, and note that the rect WAS the dialog's and not the host window's: the ModBuild
/// 231 design was right about which object to measure and wrong about which of its two rects.</para>
///
/// <para><b>WHAT IS MEASURED NOW: INK, ON BOTH RECTS.</b> <see cref="TryPaintedBounds"/> unions the
/// world corners of every graphic under a root that the CONVERSION'S OWN FIT counts as content
/// (<c>CanvasConversion.CountsAsFitContent</c> — the same verdict that produced the numbers above,
/// so the placement and the fit can no longer disagree), and <see cref="ApplyPose"/> puts the
/// PICTURE'S PAINTED BOTTOM <see cref="ImageGapPx"/> px above the DIALOG'S PAINTED TOP. Both edges,
/// every tick, because the dialog grows with its text — three lines in story1.jpg, eight in
/// story2.jpg, and the fit line shows the same rect at 168 px and then 270 px.</para>
///
/// <para><b>AND THE COMPOSITE'S HEIGHT SHRINKS BY ITSELF — THE EMPTY BAND WAS INSIDE THE FITTED
/// RECT.</b> The fit measures the drawn-content UNION and commits it as the host rect, so the 888 px
/// of black was not padding around the window, it WAS the window: 1096x233 → 1304x1056 →
/// 1656x1056, and both of those carry <c>[frame-clamped]</c> because the 1776 px union no longer fit
/// the 1080 px target frame (the HIT RECT line at :15301 says the same thing from the other side —
/// "the window draws 184x350 px outside its own frame … FURTHEST OUTSIDE: 'Map Story Window/Image'
/// by 1555 px"). Closing the gap to 24 px leaves 720 + 24 + 270 = 1014 px of content, which fits the
/// frame with room to spare, so the host rect follows the content down and the clamp stops firing.
/// No new machinery, no height dial: the fit already does exactly the right thing and was being fed
/// a picture placed 888 px too high.</para>
///
/// <para><b>FAULT 2 — EVERYTHING ELSE. USER REPORT (verbatim):</b> <i>"Als ich eine Quest gestartet
/// habe kam zuerst ein Dialogfenster ganz ohne Bild (siehe story1.jpg), prüfe ob es zu dem Zeitpunkt
/// einfach noch kein Bild gab, dann ist das in Ordnung. Ich möchte aber das zu diesem Zeitpunkt alle
/// anderen Fenster verschwinden und nur dieses Fenster sichtbar ist (Point of no return
/// überschritten)."</i></para>
///
/// <para><b>HIS QUESTION, ANSWERED FROM THE LOG: YES, THERE WAS NO ILLUSTRATION AND IT IS IN
/// ORDER.</b> story1.jpg is the story box at :14749. The window that OWNS the illustration —
/// <c>UILoadoutManager</c>, through <c>questInfo.imagePaper</c> — was not shown until :15188, 439
/// lines later, so at story1's moment there was no <c>StoryImageViewer</c> with anything in it to
/// compose: the picture did not exist, it was not lost. The first tick the composite could look at
/// it (:15215) reports <c>'Image' is 1280x720 px but its GameObject is NOT ACTIVE — WAITING</c>
/// (the addressable had not landed; StoryImageViewer.cs:201), and the sprite arrives at :15285.
/// <b>AND THEY ARE TWO SEPARATE OPENINGS, NOT TWO PAGES.</b> <c>UIWindow SHOWN: 'Map Story Window'</c>
/// appears twice, at :14749 and :15318, each preceded by its own
/// <c>[AREA MANAGER] Register area StoryBox</c> (:14744, :15311) — two <c>MapStoryController.Show</c>
/// calls with different triggers: the map's own quest-start message chain first, then
/// <c>EMapMessageTrigger.Loadout</c> from <c>UILoadoutQuestWindow.ShowIntroductionText</c>
/// (UILoadoutQuestWindow.cs:88-93). Different text because they are different messages.</para>
///
/// <para><b>SO THE GATE FIRED ~480 LINES TOO LATE.</b> <c>POINT OF NO RETURN OPENED</c> is at
/// :15228, keyed on <c>UILoadoutManager.IsOpen</c>. At story1's moment it had not fired, and the
/// screenshot shows what that costs: the quest card ('UI Quest Popup'), the 'Weltquests' quest log
/// and the temple's gold/blessing panel ('UI Temple Window') all standing beside the dialog — which
/// is exactly the standing set the slot census at :14760 lists.</para>
///
/// <para><b>THE EDGE MOVES, AND IT MOVES BY WIDENING <see cref="PointOfNoReturn"/> RATHER THAN BY
/// ADDING A SECOND GATE.</b> The level is now <c>the loadout screen is up OR the story curtain
/// stands</c>, so the gate opens at whichever comes first and there is still exactly ONE opening
/// edge, ONE named-set close and ONE destination lock per quest. A second gate would have meant two
/// <c>OPENED</c> lines, two close sets and two lock/unlock pairs to keep in step, and the user's own
/// framing is not that there are two events — he wrote <i>"(Point of no return überschritten)"</i>
/// about the story-box moment itself. The loadout clause is KEPT, not replaced: a quest whose data
/// carries no start message plays no story box at all, and that quest must still lock the merchant
/// exactly as it does today.</para>
///
/// <para><b>WHAT THE CURTAIN'S EDGE ACTUALLY TESTS, AND WHY IT CANNOT FIRE ONCE PER PAGE.</b>
/// <see cref="StoryHidesOtherUI"/> reads <c>MapStoryController.isVisibleOtherUI</c>. That flag is
/// the GAME'S OWN decision that the current story message wants the screen to itself:
/// <c>ShowImmediately</c> calls <c>ShowOtherGUI(!message.HideOtherGUI)</c> and
/// <c>ShowOtherGUI</c> then hides every one of the controller's serialized <c>elementToHide</c>
/// (MapStoryController.cs:144-165). Three properties fall out of the game's own code and none of
/// them is a guess:</para>
/// <list type="number">
/// <item><b>ONCE PER CHAIN, NOT PER PAGE.</b> <c>ShowOtherGUI</c> is change-gated —
/// <c>if (isVisibleOtherUI == show) return;</c> — so the flag falls on the FIRST page of a message
/// chain and stays down for every page after it. It comes back up in <c>ShowNext</c> when the queue
/// empties. A chain of eight pages produces one edge.</item>
/// <item><b>ONCE PER QUEST START.</b> A map message built from a <c>CMapMessageState</c> takes
/// <c>HideOtherGUI</c>'s field initialiser, <c>true</c> (StoryController.cs:29,50-59) — that is the
/// quest-start chain. The LOADOUT intro passes <c>hideOtherUI: false</c> explicitly
/// (UILoadoutQuestWindow.cs:93), and so do the temple, achievement and town-records messages. So the
/// second story box of a quest start does NOT raise a second edge, and an ordinary shopping-trip
/// message raises none.</item>
/// <item><b>IT IS PRESENTATION PARITY, NOT A NEW POLICY.</b> The flat client hides its own UI here.
/// The mod's floated windows ARE this room's UI. Extending the game's own instant to them is the
/// smallest defensible reading of <i>"alle anderen Fenster verschwinden"</i>, and it is a fact read
/// off the game rather than a heuristic about what a story box means.</item>
/// </list>
///
/// <para><b>THE CURTAIN ITSELF: AN EXCLUSION EVALUATED ONCE AND THEN FROZEN.</b>
/// <see cref="OpenGate"/> snapshots every window the mod is floating at the edge, minus the story
/// box and minus the loadout screen, into <see cref="CurtainMembers"/> — and nothing is ever
/// appended to it afterwards. <see cref="CurtainRefuses"/> then refuses to float exactly those
/// instances while the curtain stands. THE DIFFERENCE FROM ModBuild 231, WHICH IS THE WHOLE POINT:
/// 231 re-evaluated "everything except one" every tick, so the loadout sequence's own windows
/// entered its scope the moment they opened and it closed all four. A frozen instance set cannot
/// acquire a member: the battle-goal picker, the party display, the quest popup the loadout re-shows
/// and the loadout screen were not floated at :14749 and can never be in it. That is the same
/// membership guarantee <see cref="EdgeClosed"/> has, arrived at from the other side, and it is why
/// an exclusion is the right shape HERE and was the wrong shape THERE.</para>
///
/// <para><b>AND IT IS A REFUSAL, NOT A RELEASE — WHICH IS THE OTHER HALF OF NOT REPEATING 231.</b>
/// <c>ModalFallback.ReleaseFloatsExcept</c> is not used and its "DO NOT REACH FOR THIS" note has
/// been rewritten to say the true thing (what is forbidden is a STANDING exclusion; a one-shot
/// frozen one is sanctioned). The honest reason it is still not the lever is different and worse: a
/// release lasts ONE TICK, because the game window is still open and the catch-all re-enrols it —
/// and re-enrolment is what the churn fuse COUNTS. Four cycles session-suppress the window's name,
/// which is the ModBuild 231 death. <c>FloatRefusalTable.Refuse</c> is asked at the TOP of that loop
/// and <c>continue</c>s before the count, so a refused window is never counted at all, and
/// <c>WithdrawRefusedFloat</c> takes down a float that already exists. NOTHING IS WRITTEN TO THE
/// GAME: no <c>Hide</c>, no <c>Escape</c>, no <c>SetActive</c>, no <c>CanvasGroup</c>. Every member
/// keeps its ordinary 2D rendering on a canvas the 3D map room does not draw, and floats again with
/// everything on it the moment the curtain lapses.</para>
///
/// <para><b>THE QUEST LOG IS IN THE SET, AND THAT IS A DELIBERATE, NARROW REVERSAL.</b> ModBuild
/// 233's <see cref="CollectNamedSet"/> left it out on the grounds that it is the map room's
/// PERMANENT window (<c>ModalFallback.IsMapRoomPermanent</c> / <c>MapRoomPermanentReason</c>, "which
/// has no X and is not closable in the map room") and said that changing it was a change to the
/// PERMANENCE ruling, to be made by the user. HE HAS NOW MADE IT: <i>"Ich möchte aber das zu diesem
/// Zeitpunkt alle anderen Fenster verschwinden und nur dieses Fenster sichtbar ist."</i> THE
/// REVERSAL IS FOR THIS INTERVAL AND NOTHING ELSE. At every other moment the permanence ruling still
/// governs in full: no X on the quest log, refused by <c>CloseFloatedWindow</c>, skipped by the
/// escape chord, re-shown against the game's own hides. The curtain does not touch any of that —
/// it never calls <c>CloseFloatedWindow</c>, so it never needs a way past
/// <c>MapRoomPermanentReason</c>; it withholds the FLOAT for a bounded interval and hands it back.
/// The player has not "closed" the quest log and cannot: when the curtain lapses it comes back on
/// its own, with no X, exactly as before.</para>
///
/// <para><b>WHY THE CURTAIN CANNOT OUTLIVE ITSELF — FIVE BOUNDS, ALL STRUCTURAL.</b></para>
/// <list type="number">
/// <item><b>MEMBERSHIP.</b> Frozen at the edge (above). Nothing opened later is refusable.</item>
/// <item><b>THE HONESTY CLAUSE.</b> <see cref="TickCurtain"/> requires the mod to be FLOATING the
/// story box or the loadout screen this tick. If neither is on screen the curtain lapses within one
/// tick — it can never be the reason the room is empty. [[parent-wins-needs-a-real-parent]].</item>
/// <item><b>THE GATE.</b> It lapses with <see cref="CloseGate"/>, with the 3D map room, and with
/// <see cref="Reset"/>.</item>
/// <item><b>THE CYCLE CAP.</b> <see cref="MaxCurtainCycles"/> is 2, derived from
/// <c>ChurnMaxFloats</c> exactly as <see cref="MaxWithdrawCycles"/> is: a member's first float is
/// one count and each curtain that FALLS costs one re-float, so 1 + 2 = 3 is at the fuse's limit and
/// never over it.</item>
/// <item><b>THE FLOOR.</b> <see cref="TickDeadlockFloor"/> now also watches the curtain: zero
/// floated windows while it is refusing prints <c>MODAL DEADLOCK FLOOR</c> and lifts it for good.</item>
/// <item><b>THE MUTUAL-HOLD ARM (ModBuild 364).</b> <see cref="TickMutualHold"/>. Bound 2 and the
/// backdrop claim's own honesty clause can BLOCK EACH OTHER, and did. See the section below.</item>
/// </list>
///
/// <para>=====================================================================================
/// ModBuild 364 — TWO HONEST CLAUSES, EACH SATISFIED BY WHAT THE OTHER ONE IS STUCK ON
/// =====================================================================================</para>
///
/// <para><b>USER REPORT (2026-09-03), verbatim:</b> <i>"(beinahe) DEADLOCK in der map! Nach der
/// Auswahl einer Quest und nach der Story war da nurnoch das Fenster 'Warte bis zur Auswahl [...]'
/// für die privaten Ziele aber das Fenster ist nicht erschienen! Dieses Fenster sollte auch gar
/// nicht offen bleiben. Erst als ich das Optionsmenu geöffnet und wieder geschlossen habe ist das
/// Fenster erschienen und das andere verschwunden wie es sein sollte. Schau dir das an und fix das.
/// Das hatten wir alles schon einmal optimiert - wie kann es sein das hier wieder ein regression
/// stattgefunden hat?"</i></para>
///
/// <para><b>THE ANSWER TO HIS QUESTION, AND IT IS NOT A COMFORTABLE ONE.</b> No commit from that
/// week broke this. Both clauses are byte-unchanged since 2026-08-23:
/// <see cref="AnyNonMemberFloated"/> came in with ModBuild 235 (<c>df694497</c>) and
/// <c>somethingElseOnScreen</c> with ModBuild 238 (<c>4324f4d0</c>), and <c>git log -S</c> on each
/// name returns exactly one commit, ever. <b>WHAT CHANGED IS THE WORLD, AND IT CHANGED TEN DAYS
/// EARLIER.</b> ModBuild 243 (<c>b6e790c8</c>, 2026-08-24) stopped the quest log leaking back into
/// the float set when the game re-opened it — that leak WAS the ModBuild 242 bug. While it leaked,
/// there was a third floated window in this phase, <c>CountFloatsOtherThan(loadout) &gt; 0</c> was
/// satisfied for free, and the mutual hold below could not form. Fixing a real defect removed the
/// accident that had been holding a latent one shut. So: a genuine regression in what the player
/// sees, caused by a correct fix, latent since ModBuild 238 and reachable since ModBuild 243.</para>
///
/// <para><b>AND ModBuild 238 PREDICTED IT IN WRITING.</b> Its own notes warn that freezing the float
/// set at the confirm would refuse the Character-UI "when it returns CARRYING THE BATTLE-GOAL PICKER
/// AND THE ENTER BUTTON — the 234 deadlock rebuilt from parts". That is this, exactly.</para>
///
/// <para><b>THE MECHANISM, IN THE ModBuild 362 LOG'S OWN FIELDS.</b> The curtain holds
/// <c>'New Party display'</c>, and the battle-goal picker is that window's CHILD, so the picker's
/// visibility is entirely a function of the Character-UI floating (:15924,
/// <c>NESTED IN A WINDOW THAT IS REFUSED FOR THE MOMENT</c> — printed once, and then nothing for 23
/// seconds). The curtain holds honestly, because <c>'UI Loadout Window'</c> IS on screen and is not a
/// member (:15787). The backdrop claim cannot withdraw that window, because it is the ONLY thing on
/// screen (:15879, <c>the honesty clause … reads False</c>). Each clause is correct. Together they
/// are a deadly embrace, and the tester broke it by ADDING a window — the map ESC menu at :16197 let
/// the claim raise on the very next tick (:16204), and closing it let the curtain lapse (:16266).</para>
///
/// <para><b>WHY NEITHER EXISTING ARM OF THE FLOOR FIRED, AND NEITHER WAS WRONG.</b>
/// <see cref="TripsDeadlockFloor"/> requires an EMPTY room; this room held one window. The control
/// arm requires the continue control to be unreachable; it was reachable, on that same window. That
/// is why he wrote "beinahe DEADLOCK" and not "DEADLOCK". The log's own
/// <c>lifted by the deadlock floor=False</c> reads False on all six ticks that printed it.</para>
///
/// <para><b>THE REMEDY IS A TICK COUNT, NOT A LEVEL</b> (ModBuild 353's ruling). See
/// <see cref="TickMutualHold"/> for the arm, <see cref="MutualHoldTicks"/> for why 90 and not a
/// level, and <see cref="BackdropHonestySettleTicks"/> for the oscillation the same log shows at
/// :16204 / :16267 / :16354 — three edges in 150 lines, the last of them at 2 of 2 cycles.</para>
///
/// <para><b>ONE CHANGE IS NEEDED OUTSIDE THIS FILE and the feature degrades honestly without it.</b>
/// ModBuild 234 said that change was "<c>FloatRefusalTable</c> has to ask
/// <see cref="CurtainRefuses"/>". <b>THAT WAS WRONG, AND THE ModBuild 234 LOG SAYS SO.</b> The table
/// DOES ask — the ROW 0 hook is in <c>FloatRefusalTable.Refuses</c> and it shipped — and the quest
/// log stayed on screen anyway. The real reason is one line further down the chain and it is stated
/// here so the next round does not re-verify the hook:</para>
/// <list type="number">
/// <item><b>'Quest Log Manager' (<c>UIWindowID.None</c>, catch-all path) is never ASKED.</b>
/// <c>ModalFallback.CatchAllObserve</c> drops a window from <c>UnknownShown</c> the moment the game
/// hides it, and the game hid the quest log at Player.log:10503 — 684 lines BEFORE the curtain rose
/// at :11187. <c>TickCatchAll</c> only iterates <c>UnknownShown</c>, so <c>FloatRefusalTable.Refuse</c>
/// was never called for it and <c>WithdrawRefusedFloat</c> was never reached. That is why
/// <c>FLOAT REFUSED: 'Quest Log Manager'</c> appears nowhere in the log. Its float stayed alive on
/// <c>WindowPanel.Sticky</c> alone, with <c>ReassertStickyVisible</c> force-showing a window the game
/// had closed.</item>
/// <item><b>'UI Quest Popup' (<c>UIWindowID.QuestPopup</c>, enrolled path) IS asked and it changes
/// nothing.</b> The <c>Open</c> normalisation loop in <c>ModalFallback.4.Tick.cs</c> already does
/// <c>if (FloatRefusalTable.Refuses(window)) continue;</c>, so the window is correctly kept out of
/// <c>OpenWindows</c> — and the release loop's keep-alive then reads
/// <c>ContainsWindow(OpenWindows, …) || wp.Sticky</c>, so <c>Sticky</c> outvotes the refusal.</item>
/// </list>
/// <para>So the refusal table could only ever WITHDRAW a float on one of the three paths into
/// <c>ModalFallback</c>, and which path a window takes is an accident of whether its prefab carries a
/// serialized <c>UIWindowID</c>. The change is one clause in the release keep-alive — a refused
/// window is never kept alive by stickiness — and it is in
/// <c>.planning/debug/modalfallback-release-honours-refusal.diff</c>. Without it the curtain still
/// closes the guildmaster destinations and the quest popups (the named set, at the earlier edge,
/// which is entirely inside this file) and the QUEST LOG stays up — and the falsifier
/// <c>STORY CURTAIN ONLY WINDOW: NOT ACHIEVED</c> says so by name, every time.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here goes on the wire. The park is a local re-parent of a local
/// uGUI subtree. The curtain is a local decision about which local GameObject a local uGUI subtree is
/// drawn under; two players may legitimately disagree about every verdict in it. The close is the same local UI close the window's own X has performed since
/// ModBuild 184 — <c>UIWindow.Escape()/Hide()</c> and one <c>pointerClick</c> on the bar's own map
/// Toggle, both local presentation; the purchase or blessing a destination may have committed was
/// committed by ITS own button, not by leaving it. The destination lock drives the GAME'S own
/// per-button state through the game's own public methods, which is what every flat client does to
/// itself at the same moment. For a SHARED window that closes here (the quest popup, kind 2) the
/// consequence is the record's documented one: this client stops publishing that entry, peers
/// <c>Forget</c> it, nobody's game state changes and nobody is driven anywhere.</para>
///
/// <para>=====================================================================================
/// SECTION 6 — ModBuild 236: THE COMPOSITE IS INVERTED. THE LOADOUT SCREEN IS THE HOST.
/// =====================================================================================</para>
///
/// <para><b>USER REPORT (ModBuild 235 hardware, .planning/debug/story_fertig.jpg, verbatim):</b>
/// <i>"Sobald die Story fertig erzählt wurde, spawned nun ein ganz neues Fenster mit einem
/// Hintergrund auf dem dann der Button später erscheint in das Szenario zu laden. Dieser neue
/// Hintergrund ist ein eigenes lokales Fenster. Es soll immer noch das exakt gleiche
/// Multiplayer-Fenster sein wo auch die Story drin erzählt wurde. Da der Dialog fertig ist soll er
/// darin verschwinden, aber es soll DAS SELBE Fenster sein."</i></para>
///
/// <para><b>SECTIONS 3-5 WORKED AND THE LOG SAYS SO</b> — <c>LogOutput.log:3496 STORY COMPOSITE ONE
/// WINDOW: CONFIRMED</c>, <c>:3462 STORY COMPOSITE GAP: CONFIRMED</c> (picture bottom y=-259, dialog
/// top y=-283). NOTHING ABOUT THE MEASUREMENT, THE GAP OR THE CURTAIN IS BEING REDESIGNED. What is
/// wrong is WHICH WINDOW HOSTS, and it is wrong for a reason that cannot be tuned out: <b>the host
/// dies</b>. <c>:3776</c> the claim lapses because <c>FinishIntroduction</c> has started expanding
/// the paper; <c>:3796</c> both halves float; <c>:3875</c> the game closes 'Map Story Window'
/// altogether. The window the story was told in is GONE by the time the continue control appears, so
/// under the ModBuild 235 arrangement the control CANNOT be in it. Keeping the story window alive
/// past the game's own close would mean pinning a closed window's alpha, which is the ModBuild 234
/// deadlock verbatim and is forbidden.</para>
///
/// <para><b>AND THE ModBuild 235 LOG ALSO SHOWS THE "ganz neues Fenster" LITERALLY, AS A FLAP.</b>
/// 'UI Loadout Window' floats at <c>:3435</c>, is WITHDRAWN at <c>:3489</c>, re-floats at
/// <c>:3519</c>, is withdrawn again at <c>:3559</c>, and floats a third time at <c>:3788</c> — and
/// every one of those floats carries its own <c>one-shot facing applied — yawed 72.8° / 72.1° /
/// 72.4° / 79.5°</c>, i.e. the window is RE-PLACED each time. It is not merely a second window, it
/// is a window that respawns in a new pose. 'Map Story Window', by contrast, floats ONCE
/// (<c>:3300</c>) and never moves. THE INVERSION SWAPS WHICH OF THOSE TWO IS THE STABLE ONE, and
/// that is the whole of his sentence.</para>
///
/// <para><b>SO THE LOADOUT SCREEN HOSTS AND THE STORY DIALOG IS PARKED INTO IT.</b>
/// <c>UILoadoutManager</c>'s window is open BEFORE the intro's first dialog page and stays open until
/// the scenario loads; <c>UILoadoutQuestWindow.Show</c> pins <c>paperFitter.transitionPercent = 0</c>
/// (:52) so during the intro it draws essentially just the illustration, and
/// <c>FinishIntroduction</c> expands that same paper in place (:101-107). With the dialog inside it:
/// the story is told in the loadout window, the game closes the story window, the dialog goes home
/// and vanishes, the paper expands, the continue control appears — ALL IN ONE WINDOW THAT NEVER
/// RESPAWNED AND NEVER MOVED. <see cref="ReportContinuity"/> is the falsifier for exactly that
/// sentence and it names the panel's instance id and world pose at both ends.</para>
///
/// <para><b>FOUR THINGS GET STRICTLY BETTER AND ONE GETS WORSE. THE WORSE ONE IS NAMED FIRST.</b>
/// <list type="number">
/// <item><b>WORSE — THE HOST HAS A CLOSE CROSS.</b> <c>ModalCloseButton.Attach</c> is decided ONCE,
/// at convert time (<c>ModalFallback.8.Convert.cs</c>:586), and the loadout window converts BEFORE
/// the story box exists — so no live condition in this file can take its X away. The user's ruling
/// <i>"darf dieses Story-Fenster kein 'x' haben, da man durchklicken muss"</i> is therefore NOT
/// satisfied by the host this build. It is a pre-existing hazard rather than a new one — that X is
/// on the loadout screen in story_getrennt3.jpg and in story_fertig.jpg already, and pressing it runs
/// <c>UIWindow.Escape()/Hide()</c> on the window that carries 'Enter Dungeon' — and the fix is one
/// clause in a file this lane does not own:
/// <c>.planning/debug/laneB-out-of-lane.diff</c> adds <c>UILoadoutManager</c> to the no-X family for
/// the same reason the story box is in it.</item>
/// <item><b>BETTER — NO WITHDRAWAL AND NO FLASH.</b> This class runs from the FIRST line of
/// <c>TickWindowLiveness</c>, before the convert loop of the SAME tick
/// (<c>ModalFallback.4.Tick.cs</c>: <c>PhaseRelease</c> → <c>TickWindowLiveness</c> → release loop →
/// <c>PhaseConvert</c>). The story window is BORN AFTER its host exists, so on the very tick it
/// opens the dialog is already parked and the claim already stands, and the catch-all's
/// <c>FloatRefusalTable.Refuse</c> then refuses it before it is ever converted. The ModBuild 235
/// arrangement could not do that in either direction: the loadout screen floats minutes before the
/// story box exists, so its float always had to be TAKEN DOWN again.</item>
/// <item><b>BETTER — THE ModBuild 232 ANCHOR TRAP IS GONE BY CONSTRUCTION.</b> The illustration is
/// never moved now; it is only MEASURED, as the placement zero. <see cref="ImageSubtree"/>,
/// <see cref="Judge"/> and the fraction guard are kept verbatim because they are also the READINESS
/// gate (the sprite arrives down an addressable), but nothing re-anchors a stretch child any more,
/// so "0x0 px" cannot happen to the picture.</item>
/// <item><b>BETTER — THE THING THAT MOVES IS A MOD-OWNED CONTAINER, NOT A GAME RECT.</b> See
/// <see cref="Park"/>: a <c>GloomhavenVR.StoryDock</c> RectTransform is created under the host, sized
/// to the story window ROOT's own rect, and the story root's CHILDREN are moved into it with
/// <c>worldPositionStays: false</c> — so every child keeps its authored anchors, pivot, sizeDelta and
/// offset UNTOUCHED and resolves them against a rect of exactly the size it always had. The hand-back
/// is therefore parent + sibling index + layer and nothing else, which is as verbatim as a hand-back
/// can be.</item>
/// <item><b>BETTER — NOTHING CAN BE LEFT BEHIND.</b> EVERY child of the story window root moves, not
/// just <c>MapStoryController.dialogBox</c>. That is not tidiness: the way past a page is a click on
/// <c>UICharacterStoryBox.skipButton</c> (UICharacterStoryBox.cs:44/81) and this round could not
/// prove from the log which branch of the window's hierarchy that button is on — the ModBuild 235 fit
/// line for the story panel rejected "1 culled/disabled, 2 faint, 1 zero-size" graphics it does not
/// name, and a full-window transparent click catcher is exactly what a "faint" rejection looks like.
/// Moving the whole content set makes the question moot; moving one branch would have risked a story
/// nobody can click past, which is a deadlock. The window ROOT itself keeps its own components
/// (the hardware identity line at <c>:3290</c> reads <c>[RectTransform, CanvasRenderer, CanvasGroup,
/// UIWindow, MapStoryController]</c> — no Graphic, so the root draws nothing and catches
/// nothing).</item>
/// </list></para>
///
/// <para><b>THE REFUSAL SWAPS SIDES, AND THE ROW CAN NAME ITS SUBJECT AFTER ALL.</b> SECTION 4 claim
/// (3) said a table row "could not have refused the story box" because <c>MapStoryController</c> is
/// not on the story window's own GameObject. <b>THAT WAS WRONG AND THE ModBuild 235 LOG FALSIFIES IT
/// IN ONE LINE:</b> <c>:3290 WINDOW IDENTITY 'Map Story Window' (ID None): path Story Canvas/Map
/// Story Window; … components [RectTransform, CanvasRenderer, CanvasGroup, UIWindow,
/// MapStoryController]</c>. The serialized <c>window</c> field points at the controller's OWN
/// GameObject, so the IS-A test the table requires is available. The row is still additionally
/// checked against the exact GameObject the claim was raised for
/// (<see cref="HoldsStoryFloatBack"/>), because a Singleton must never have a claim about one open of
/// it refuse another.</para>
///
/// <para><b>WHAT HAPPENS WHEN THERE IS NO LOADOUT SCREEN — AND THAT IS THE COMMON CASE.</b> A
/// tree-wide grep finds fourteen callers of <c>Singleton&lt;MapStoryController&gt;.Instance.Show</c>;
/// the loadout intro (UILoadoutQuestWindow.cs:88-93) is ONE of them. The temple, the achievement, the
/// town-records and the map's own quest-start chains have no loadout window to host anything. The
/// composite requires a host, a live illustration and a story box SIMULTANEOUSLY
/// (<see cref="EnsureParked"/> is only reached when all three are measured this tick), so in every
/// one of those cases it never stands and the story window floats alone EXACTLY as it does today —
/// and <see cref="ReportNoHost"/> says so once per story box, by name, so that branch is visible in a
/// hardware log instead of being an absence.</para>
///
/// <para>=====================================================================================
/// SECTION 7 — ModBuild 237: THE BLUE BAR FOLLOWS THE COMPOSED HOST
/// =====================================================================================</para>
///
/// <para><b>USER REQUEST (2026-08-23, verbatim):</b> <i>"Ich will aber das man die Story gemeinsam
/// erlebt. Ich weiß dass das flat spiel das anders macht und dort jeder lokal bei sich weiterklicken
/// kann. Ich möchte aber das die gesamte Story, das Fenster und damit auch der Status des Fensters
/// vollständig synchronisiert wird."</i></para>
///
/// <para><b>ModBuild 236 SAID THE BLUE BAR WAS NOT MOVED "THIS BUILD" AND NAMED A BASELINE AS THE
/// REASON. THAT RESIDUAL IS NOW PAID.</b> <see cref="SharedWindowIdentity"/> holds which window
/// carries <c>SharedWindowKind.MapStory</c>: the composed HOST while this class's claim is standing,
/// <c>MapStoryController.window</c> otherwise. <c>SharedWindows.KindOf</c> and
/// <c>SharedWindows.WindowOf</c> both read it, so the bar tint, the pose sync, the release re-face
/// gate and <c>PanelPoseWatch</c>'s <c>peerOwned</c> flag all move together and none of them can
/// disagree with the others about which window that is. The baseline objection was real and was fixed
/// where it lives: <c>RemoteMapStory</c>'s move tracker now keeps its baseline PER GRAB FRAME and
/// treats a change of grab frame as a reset — dropping the baseline, the <c>Moving</c> flag AND the
/// pose OWNERSHIP, which the one-line version of that fix would have left standing and which is the
/// half that would have kept this client elected as the room's last mover under a stale stamp.</para>
///
/// <para><b>THE CONTENT HALF WAS NEVER AFFECTED AND IS UNCHANGED.</b> <c>Net/RemoteMapStory</c>
/// resolves the story PAGE through <c>MapStoryController.dialogBox</c> directly
/// (<c>MapBox()</c> at RemoteMapStory.cs:379-389, read on the send path at :512/:525 and driven
/// by <c>ResolveStoryPage</c> at :1194 through the game's own <c>ShowLine</c> at :1264) and never
/// through <c>SharedWindows</c>, so page, text and the
/// finished bit kept syncing through ModBuild 236 while the dialog was parked — a park changes a
/// transform's PARENT, and the controller is reached through its Singleton. Equally, nothing of the
/// HOST's content ever goes on the wire: what follows the host is the POSE, and the identity is handed
/// back the moment the story stops being told in it, i.e. before the battle-goal phase, whose content
/// is private per player by the user's own ruling.</para>
/// </summary>
internal static class StoryComposite
{
    private const string Scope = "WorldUI";

    /// <summary>Gap between the dialog's PAINTED top edge and the image's PAINTED bottom edge, in
    /// the story window's own authored uGUI pixels. Deliberately a constant and not a config dial:
    /// the user asked for "den Dialog unter dem Bild", not for a spacing control, and every new dial
    /// is a surface somebody has to tune.
    ///
    /// <para><b>ModBuild 234: THE NUMBER DID NOT CHANGE AND THE NUMBER WAS NEVER THE BUG.</b> It was
    /// applied to the authored rect of a mostly-transparent host, so the DRAWN gap came out at
    /// 888 px (Player.log:15304) and then 786 px (:15323). Both edges are now measured as ink —
    /// see <see cref="ApplyPose"/> and <see cref="TryPaintedBounds"/> — and 24 px is roughly 17-25 mm
    /// on the composite's committed panel: visibly one gap, never a separation. The falsifier
    /// <c>STORY COMPOSITE GAP</c> states the millimetres against
    /// <see cref="MaxGapMillimetres"/>.</para></summary>
    private const float ImageGapPx = ChromeParkTuning.ChromeGapPx;

    /// <summary>Below this the re-place is skipped, in authored uGUI px —
    /// <see cref="ChromeParkTuning.OffsetEpsilonPx"/>, where the argument this used to restate from
    /// <c>MapTravelConfirm</c> is written once (ModBuild 439, survey row R35).</summary>
    private const float OffsetEpsilonPx = ChromeParkTuning.OffsetEpsilonPx;

    /// <summary>A parked subtree must be a MINORITY of its window, or it is not the image. The
    /// paper measures 1280x720 inside a 1920x1080 window (0.67 x 0.67); the group that CONTAINS it
    /// is a full-window 1920x1080 rect whose direct child is a full-window <c>Blur</c>, and parking
    /// THAT would drag a full-screen dark rectangle into the story panel. This is the guard that
    /// caught ModBuild 231's wrong candidate, so it is applied to every candidate and never
    /// skipped.</summary>
    private const float MaxParkFractionOfWindow = 0.9f;

    /// <summary>A rect smaller than this in either axis has not been laid out yet — the viewer
    /// deactivates <c>imageHolder</c> until the addressable sprite lands
    /// (StoryImageViewer.cs:201/234), and an inactive rect can measure zero. "Not yet" is not a
    /// refusal and must not be reported as one.</summary>
    private const float MinParkSizePx = 2f;

    /// <summary>Above this the loadout screen's paper has begun (or finished) its expand tween, so
    /// the quest intro is OVER: <c>UILoadoutQuestWindow.Show</c> sets
    /// <c>paperFitter.transitionPercent = 0f</c> (:52) and <c>FinishIntroduction</c> is the only
    /// thing that raises it, from 0 to 1 over <c>expandPaperDuration</c> (:101-107). Used only to
    /// tell "the intro has not started drawing yet" (WAITING, Info) apart from "the intro is over
    /// and there is no illustration left to compose" (also Info) — a story box that opens AFTER the
    /// expand is an ordinary map message and refusing it is the correct, unremarkable answer. The
    /// ModBuild 232 log printed that case as a WARNING twice (:5408, :5409).</summary>
    private const float PaperExpandedEpsilon = 0.01f;

    /// <summary>
    /// How long <see cref="StoryWindow"/> may keep answering "the story box is up" from the two
    /// MOD-WRITTEN facts (<c>UIWindow.IsVisible</c>, which is a <c>CanvasGroup.alpha</c> this mod
    /// pins to 1 on a sticky float, and the mod's own float set) after the GAME's own
    /// <c>UIWindow.IsOpen</c> has gone false. See <see cref="StoryWindow"/> for the full argument and
    /// the ModBuild 234 log lines.
    ///
    /// <para>THE NUMBER IS THE JOB IT HAS TO DO AND NOTHING MORE. The ModBuild 232 dropout it exists
    /// to cover is ONE TICK long; a second is two orders of magnitude of headroom over that at 50 Hz,
    /// and it is a HARD CEILING rather than a timeout to tune. It is the same one second, chosen for
    /// the same reason, as <c>MapRoom.ReadyToggleParkClaim.ClaimLifetimeSeconds</c>: long enough to
    /// outlive a frame hitch or a guarded throw, far too short to outlive a stand-down.</para>
    /// </summary>
    private const float StoryFloatGraceSeconds = 1.0f;

    /// <summary>The last <c>Time.unscaledTime</c> at which the GAME (never the mod) reported the story
    /// window open. <see cref="StoryFloatGraceSeconds"/> is measured from it.</summary>
    private static float _storyGameOpenAt = float.NegativeInfinity;

    // ---- park state ---------------------------------------------------------------------------

    /// <summary>The name of the mod-owned container the story window's content is moved into. It is
    /// created under the HOST (the loadout window) and destroyed on the hand-back, so nothing of it
    /// can survive a stand-down. The <c>GloomhavenVR.</c> prefix is load-bearing: it is what the
    /// conversion's own content tests use to skip this mod's furniture.</summary>
    private const string DockName = "GloomhavenVR.StoryDock";

    /// <summary>The mod-owned container. Null when nothing is parked.</summary>
    private static RectTransform? _dock;

    /// <summary>THE HOST — the window the composite is drawn IN. ModBuild 236: this is the LOADOUT
    /// window, not the story window.</summary>
    private static UIWindow? _parkHost;

    /// <summary>THE SOURCE — the story window whose children the dock is holding.</summary>
    private static UIWindow? _parkSource;

    /// <summary>The children taken from the source window's root, with the sibling index each one had
    /// there. The hand-back is parent + index and nothing else, because
    /// <c>SetParent(worldPositionStays: false)</c> left every one of their own rect fields
    /// untouched — see <see cref="Park"/>.</summary>
    private static readonly List<Transform> Moved = new(8);
    private static readonly List<int> MovedIndex = new(8);

    /// <summary>Every transform in the moved subtree whose LAYER this class overwrote, and the layer
    /// the game had given it — the shared record (<see cref="MovedSubtreeLayers"/>), which carries
    /// the walk, the restore guard (only where the transform is STILL on the layer we wrote) and the
    /// skip count the report lines below print. <c>Written</c> is the layer last written, or −1;
    /// this class re-asserts only when the HOST's own layer changes (the supersample path moves a
    /// panel's layers onto a capture layer and back), never per tick.</summary>
    private static readonly MovedSubtreeLayers Layers = new(64);

    private static bool _composeLogged;

    /// <summary>The size the composite OWNS for the dock, in the HOST's authored uGUI px: the story
    /// window ROOT's own rect size, measured live at the instant of the park. Writing it into the
    /// dock's <c>sizeDelta</c> is what lets every moved child resolve its authored anchors against a
    /// rect of exactly the size it always had, so the ModBuild 232 stretch-child collapse
    /// ([[anchors-own-a-stretch-child-size]]) cannot happen to any of them.</summary>
    private static Vector2 _dockSize;

    /// <summary>The picture the dialog is placed under. MEASURED, NEVER MOVED (ModBuild 236) — it
    /// stays exactly where <c>UILoadoutQuestWindow</c> put it, inside the host.</summary>
    private static RectTransform? _picture;

    /// <summary>The name of the sprite that was on the picture when the composite was built — printed
    /// by the BUILT line and by the falsifier, because "0x0 px" and "NOT LOADED YET" are the two ways
    /// this feature has already shipped broken and both belong in the same line.</summary>
    private static string _parkedSprite = string.Empty;

    // ---- the slot, measured ONCE per park -------------------------------------------------------

    /// <summary>The picture's PAINTED bounds in the host's own authored px — the composite's zero.
    /// Measured once per park and re-measured only when the host window's own frame or the picture's
    /// own rect changes size (see <see cref="ApplyPose"/>). A per-tick reading would make the dialog
    /// jump every time the window animates.</summary>
    private static Rect _slot;
    private static bool _slotValid;
    private static int _slotCount;
    private static Vector2 _slotHostSize;
    private static Vector2 _slotPictureSize;

    /// <summary>Change-gate for the candidate report (secondary objective 5a). The ModBuild 231 log
    /// carries FORTY byte-identical refusal warnings for ONE decision, because the choice was
    /// re-reported every tick the composite was wanted. One decision, one line, per loadout window
    /// instance — and a hard cap so even an oscillating layout cannot produce a wall.</summary>
    private static UIWindow? _pickHost;
    private static string _pickReported = string.Empty;
    private static int _pickReports;

    /// <summary>Raised from 3 to 6 in ModBuild 233, and the reason is a trap the cap itself would
    /// have sprung. The pick now has a WAITING state whose text carries the rect's MEASURED size,
    /// and that size changes as the layout settles — so several distinct WAITING lines can be
    /// printed before the illustration arrives, and a cap of 3 could be spent entirely on "not yet"
    /// and swallow the one line that says WHAT WAS PARKED. Six leaves room for the whole normal
    /// sequence (a couple of waits, the park, the hand-back) and is still nowhere near ModBuild
    /// 231's forty copies of one refusal.</summary>
    private const int MaxPickReportsPerHost = 6;

    // ---- the story-float claim (ModBuild 233, inverted in 236) --------------------------------

    /// <summary>
    /// How many times the claim may be RAISED for one loadout window instance, and the number is
    /// derived from the same fuse that cost ModBuild 231 the session rather than chosen.
    ///
    /// <para><c>ModalFallback.10.CatchAll.cs</c> counts one churn tick per <c>OpenWindows.Add</c> of
    /// a window the mod is not already floating (:286-291) and suppresses the NAME for the session
    /// when <c>churn.Count &gt; ChurnMaxFloats</c>, i.e. on the fourth. A refused window is never
    /// counted (the refusal <c>continue</c>s at :234-238, long before that block), so the only
    /// counted events in a composite's life are the loadout screen's FIRST float and one re-float
    /// per claim that FALLS. That is <c>1 + cycles</c> counts, so <c>cycles ≤ 2</c> keeps the total
    /// at or under 3 and the fuse cannot blow however badly the composite flaps.</para>
    ///
    /// <para><b>ModBuild 236 — THE SUBJECT CHANGED AND THE ARITHMETIC STILL HOLDS, FOR A BETTER
    /// REASON.</b> The claim now refuses the STORY window, and the story window is born AFTER its
    /// host, so on the ordinary path it is refused before it is ever enrolled and costs the fuse
    /// NOTHING at all. The counted events are the story box's own float on a chain the composite
    /// could not host (the quest-start chain, which opens before the loadout screen) plus one
    /// re-float per claim that FALLS. 1 + 2 = 3 is still at the fuse's limit and never over it. The
    /// budget is refilled by <see cref="CloseGate"/>, i.e. once per quest, because the fuse's own
    /// window is 60 s and a new quest start is a new interval.</para>
    ///
    /// <para>Past the cap the claim is dead for this gate: both halves float, which is the ModBuild
    /// 232 presentation and therefore a known-survivable one, and
    /// <c>STORY COMPOSITE CLAIM CAPPED</c> says so exactly once.</para>
    /// </summary>
    private const int MaxWithdrawCycles = 2;

    /// <summary>The live level. Recomputed from measurement in <see cref="Tick"/> every tick and
    /// never latched — <see cref="HoldsStoryFloatBack"/> is a PURE read of it, because
    /// <c>FloatRefusalTable.Refuses</c> is re-entered several times per tick from a recursive
    /// ancestor walk and every caller must get the same answer.</summary>
    private static bool _claimStanding;

    /// <summary>The exact STORY window GameObject the claim is about (ModBuild 236). A claim about
    /// one object must never refuse another — <c>MapStoryController</c> is a Singleton and the
    /// refusal row asks by GameObject for the same reason the ready-toggle row does.</summary>
    private static GameObject? _claimObject;

    /// <summary>The story window instance <see cref="_claimCycles"/> is counted against, so a
    /// different window starts from a clean budget. <see cref="CloseGate"/> refills it per quest.</summary>
    private static UIWindow? _claimHost;
    private static int _claimCycles;
    private static bool _claimCapReported;
    private static string _claimWhy = "the quest-intro composite has never claimed the story window";

    // ---- the falsifier's change gate ----------------------------------------------------------

    private static string _oneWindowVerdict = string.Empty;
    private static int _oneWindowReports;

    /// <summary>Cap on <c>STORY COMPOSITE ONE WINDOW</c> lines per composite. The verdict is
    /// change-gated as well, so this only bites if the state genuinely oscillates — in which case
    /// six lines are already the diagnosis and a seventh is noise.</summary>
    private const int MaxOneWindowReports = 6;

    // ---- gate state ---------------------------------------------------------------------------

    private static bool _gateOpen;
    private static int _closedAtOpen;
    private static readonly List<UIGuildmasterButton> Greyed = new(8);
    private static readonly List<bool> GreyedHadHighlight = new(8);
    private static Component? _cityRequest;

    /// <summary>The exact window INSTANCES <see cref="OpenGate"/> closed at the rising edge. This is
    /// the entire universe <see cref="HoldsBack"/> may refuse: it is filled ONCE, at the edge, and
    /// nothing appends to it afterwards. A window the loadout sequence opens later is therefore not
    /// holdable by construction, not by a test that could be got wrong.</summary>
    private static readonly List<UIWindow> EdgeClosed = new(8);

    /// <summary>Ticks of convert-loop hold the edge is allowed to buy, and the number is derived
    /// rather than chosen. <c>ModalFallback.10.CatchAll.cs</c>'s <c>ChurnMaxFloats</c> is 3: a
    /// window the mod holds out of the float set is re-enrolled by the catch-all on every tick and
    /// counted as a fresh float, so a hold of FOUR ticks session-suppresses that window's name (the
    /// ModBuild 231 log: <c>CATCH-ALL FUSE: window 'UI Loadout Window' re-floated 4× in 60s</c>).
    /// Two is strictly under that bound with a tick to spare, and two is all the bridge needs: the
    /// game window is already <c>Hide()</c>n and its panel already flagged <c>UserClosing</c> by the
    /// time the hold is armed, so the only job left is to survive the one tick between the close and
    /// the release loop noticing.</summary>
    private const int HoldBridgeTicks = 2;

    private static int _bridgeTicksLeft;
    private static bool _bridgeReported;

    /// <summary>Convert-loop refusals made by <see cref="HoldsBack"/> during the CURRENT tick's
    /// convert pass, and the count carried over from the previous one. Two counters because
    /// <see cref="Tick"/> runs in the release phase and the convert loop runs later in the SAME
    /// tick (<c>ModalFallback.4.Tick.cs</c>: <c>PhaseRelease</c> :2526 → <c>PhaseConvert</c> :2675),
    /// so the newest complete measurement available to the floor is always the previous pass's.
    /// That one-tick lag is stated rather than hidden: the floor cannot trip on the very tick the
    /// gate opens, which is correct — nothing has been converted yet on that tick either.</summary>
    private static int _heldThisPass;
    private static int _heldLastPass;
    private static string _heldLastName = string.Empty;

    /// <summary>Total refusals this gate made, for the closing line. Kept even after the floor lifts
    /// the hold: "lift the verdict, keep the count".</summary>
    private static int _heldTotal;

    /// <summary>Set by the deadlock floor. While true <see cref="HoldsBack"/> refuses nothing at all
    /// for the rest of this gate, whatever the bridge says.</summary>
    private static bool _holdLifted;

    /// <summary>
    /// IS THE PLAYER PAST THE POINT OF NO RETURN?
    ///
    /// <para><b>ModBuild 234 WIDENED THIS, AND THE WIDENING IS THE FIX FOR THE SECOND FAULT.</b> Up
    /// to ModBuild 233 the answer was <c>UILoadoutManager.IsOpen</c> alone, and the hardware log
    /// shows that firing ~480 lines after the quest-start story box was already on screen with three
    /// other windows beside it (:14749 vs :15228). The level is now the UNION of two facts, so the
    /// gate opens at whichever comes first and there is still exactly ONE rising edge per quest:</para>
    /// <list type="number">
    /// <item><b>THE STORY CURTAIN</b> — <see cref="_curtainStanding"/>, driven by
    /// <see cref="StoryHidesOtherUI"/>: the game itself has hidden the rest of its UI for the
    /// message chain that is being shown. See the class doc, SECTION 5, for why that flag is one
    /// edge per chain and one chain per quest start.</item>
    /// <item><b>THE LOADOUT SCREEN</b> — <c>UILoadoutManager.IsOpen</c>
    /// (decompiled/GH.Runtime/UILoadoutManager.cs:57, <c>_window.IsOpen</c>), KEPT and not replaced.
    /// That window is opened when the party has committed and stays open until the scenario is
    /// entered, so it is what holds the level up for the rest of the interval — the story box closes
    /// when the player clicks through the intro and the merchant must stay shut after that. It is
    /// also the only clause that fires for a quest whose data carries no start message at all.</item>
    /// </list>
    ///
    /// <para>THE 3D-ROOM GATE IS PART OF THE ANSWER, not a caveat. Everything this class enforces is
    /// about floated world-space windows and the map room's 3D caps; with the 3D map switched off
    /// there are neither, and the flat game's own behaviour is what the player asked to keep.</para>
    ///
    /// <para>OFFERED AS A PUBLIC SEAM for the cap lane: one <c>if</c> in
    /// <c>MapButtonRail.Pressable</c> makes every cap dead and grey even if the game-side lock-out
    /// below is ever defeated. It is NOT required for the caps to go dead — see
    /// <see cref="SetDestinationsLocked"/>, which drives the game's own
    /// <c>Toggle.interactable</c> that <c>Pressable</c> already reads. <b>NOTE FOR THAT LANE:</b>
    /// this property now also reads true during a full-attention story message, i.e. the caps go
    /// dead a little earlier than before and for the duration of any map message the GAME has
    /// decided should own the screen. That is the user's own framing of the moment
    /// (<i>"Zu diesem Zeitpunkt ist der 'Point of Return' schon überschritten"</i>) and it is
    /// self-limiting: the curtain lapses with its own bounds. This property is still a pure read of
    /// state — it closes nothing and holds nothing.</para>
    /// </summary>
    /// <para><b>ModBuild 459 WIDENS IT AGAIN, AND THE NEW TERM IS THE EARLIEST OF THE THREE.</b>
    /// <see cref="QuestJourneyCurtain.PartyCommitted"/> — the QUEST CONFIRM, i.e.
    /// <c>MapChoreographer.OnMoveClick</c> measured through its own three game terms. That class
    /// has owned this measurement since ModBuild 238 and this property simply reads it, so there is
    /// still one owner and no second timing rule. <b>The user's point of no return is the confirm:</b>
    /// <i>"Beim point-of-no-return (Quest bestätigt) hat sich auch das Questfenster geschlossen"</i>,
    /// and in the same round <i>"Beim Mitspieler blieb der Händler offen nach dem point of no return
    /// - ALLE Fenster sollen sich da schließen"</i>. The ModBuild 457 logs measure the cost of the
    /// old level exactly: the confirm is at HOST <c>Player.log:29962</c> (frame 24903) and PEER
    /// <c>remote/Player.log:22011</c> (frame 13797), the story-curtain edge that actually ran
    /// <see cref="OpenGate"/> is at <c>:40747</c> / <c>:32722</c> — ~8900 frames, about two minutes,
    /// later — and for the whole of that interval the peer's merchant ('UI Shop Item Window',
    /// floated <c>remote:16136</c>) stood open until a HAND press on its X took it away
    /// (<c>remote:23681</c>). Both gate lines read <c>CLOSED 3 of 8 NAMED member(s) … (5 member(s)
    /// were neither open nor floated)</c>: the sweep was correct and it simply ran too late to see
    /// the shop.</para>
    ///
    /// <para><b>AND IT IS WHAT PUTS THE QUEST INFO WINDOW BACK FOR THE PRIVATE QUESTS.</b> With the
    /// sweep at the confirm, 'UI Quest Popup' is closed there and is NOT floating when
    /// <see cref="RaiseCurtain"/> freezes its member set, so the curtain cannot refuse it later. In
    /// the 457 log it WAS in that frozen set, the game re-opened it for the battle-goal step
    /// (<c>Player.log:44367</c>, with <c>ActiveDisplay=BATTLE_GOALS</c> at <c>:44723</c>) and
    /// <c>MUTUAL HOLD MEASURED</c> at <c>:44401</c> records the curtain withholding it for the rest
    /// of the interval — <i>"spawnt dann später gar nicht mehr bei den privaten Quests"</i>.</para>
    ///
    /// <para><b>THE UNION STILL PRODUCES ONE RISING EDGE PER QUEST</b>, which is the property the
    /// whole gate rests on: the commit level is raised once per commit and holds until the map is
    /// unlocked with neither story nor loadout for the bridge, and the two later terms rise while it
    /// is still standing, so they can never open a second gate.</para>
    internal static bool PointOfNoReturn => QuestJourneyCurtain.PartyCommitted || StoryOrLoadoutStanding;

    /// <summary>
    /// THE ModBuild 234 LEVEL, TERM FOR TERM — <c>_curtainStanding || LoadoutScreenOpen</c>, i.e.
    /// exactly what <see cref="PointOfNoReturn"/> was up to ModBuild 458.
    ///
    /// <para>It exists because <see cref="QuestJourneyCurtain"/> is now a TERM of
    /// <see cref="PointOfNoReturn"/> and also a READER of it: every clause in that class that used
    /// to read the union reads this instead, so the commit level can never become its own reason to
    /// stand [[a-claim-must-not-measure-itself]]. Nothing about the meaning of those clauses
    /// changes — this property is the value they were reading.</para>
    /// </summary>
    internal static bool StoryOrLoadoutStanding => _curtainStanding || LoadoutScreenOpen;

    /// <summary>The ModBuild 233 level, unchanged and still the clause that holds the gate up for the
    /// whole pre-scenario interval.
    ///
    /// <para>ModBuild 477 made it <c>internal</c> and changed nothing else. It is the one term of
    /// the map room's PRIVATE QUEST CORNER exception (<c>SharedQuestCornerSeat.cs</c>): the loadout
    /// screen is the phase in which the game raises the quest info popup and the battle-goal
    /// ("private quest") picker from one call, and it is the only term of that phase which is not
    /// per-player. It is READ there and never written, so this stays a level with one writer: the
    /// game.</para></summary>
    internal static bool LoadoutScreenOpen =>
        MapRoomDriver.Active
        && Singleton<UILoadoutManager>.IsInitialized
        && Singleton<UILoadoutManager>.Instance != null
        && Singleton<UILoadoutManager>.Instance.IsOpen;

    /// <summary>
    /// HAS THE GAME HIDDEN THE REST OF ITS OWN UI FOR THE STORY MESSAGE ON SCREEN RIGHT NOW?
    ///
    /// <para>One field, read from the game: <c>MapStoryController.isVisibleOtherUI</c>.
    /// <c>ShowImmediately</c> calls <c>ShowOtherGUI(!message.HideOtherGUI)</c> and
    /// <c>ShowOtherGUI</c> hides every serialized <c>elementToHide</c> when that is false
    /// (MapStoryController.cs:144-165). So <c>isVisibleOtherUI == false</c> is not an inference
    /// about what a story box means — it is the game stating that THIS message owns the screen.</para>
    ///
    /// <para>WHY THIS AND NOT <c>messageTrigger</c>, WHICH LOOKS LIKE THE OBVIOUS TEST.
    /// <c>EMapMessageTrigger.QuestStart</c> exists in the enum and is written by NO line of the
    /// decompiled game — it arrives from the rule-library DLL through
    /// <c>CShowMapMessages_MapClientMessage.m_messageTrigger</c>, so gating the whole feature on it
    /// would be gating it on a value this round cannot confirm from either the source or the log.
    /// [[verify-outcome-not-path]]. <c>HideOtherGUI</c>, by contrast, is decided in C# at every call
    /// site: <c>true</c> by field initialiser for a map message built from a
    /// <c>CMapMessageState</c> (StoryController.cs:29,50-59 — the quest-start chain), and explicitly
    /// <c>false</c> for the loadout intro (UILoadoutQuestWindow.cs:93), the temple, the achievement
    /// and the town-records messages.</para>
    ///
    /// <para>The window itself must also be standing, so a stale flag left behind by a chain that has
    /// already ended cannot hold the curtain up on its own.</para>
    /// </summary>
    private static bool StoryHidesOtherUI(UIWindow? story)
    {
        try
        {
            if (!MapRoomDriver.Active || story == null || !Singleton<MapStoryController>.IsInitialized)
                return false;
            MapStoryController mc = Singleton<MapStoryController>.Instance;
            return mc != null && !mc.isVisibleOtherUI;
        }
        catch (System.Exception)
        {
            // The curtain is presentation. A throw reaching the modal tick is not worth an edge.
            return false;
        }
    }

    /// <summary>
    /// The story window of the composite, or null. Open-or-visible OR ALREADY FLOATED BY THE MOD —
    /// and the third clause is a ModBuild 233 fix rather than belt-and-braces.
    ///
    /// <para>The first two clauses alone flap. The ModBuild 232 log has
    /// <c>STICKY FIGHT: 'Map Story Window' has been re-shown 3 frames running — something in the
    /// game is hiding it EVERY frame</c> (:5257) and, five lines later, an
    /// <c>UNPARKED — the story box closed</c> immediately followed by a fresh <c>BUILT</c> (:5261,
    /// :5263): the window's own <c>IsOpen</c>/<c>IsVisible</c> went false for a tick while the mod
    /// kept the FLOAT standing throughout. Under ModBuild 233 that one-tick dropout would also drop
    /// the loadout claim and spend one of its <see cref="MaxWithdrawCycles"/>, so the question has
    /// to be asked of the thing that is actually stable: is the mod drawing this window right now?
    /// [[dont-win-a-write-war]] — concede the flag the game is rewriting, own the fact.</para>
    ///
    /// <para>The park still has to happen on the tick the window OPENS, one tick before the
    /// conversion measures it, or the panel's first fit would size itself to the dialog alone and
    /// then jump — which is why the open/visible clauses come first and the float clause only
    /// EXTENDS the answer, never replaces it.</para>
    ///
    /// <para>=====================================================================================
    /// ModBuild 235 — TWO OF THE THREE CLAUSES ARE VALUES THIS MOD ITSELF WRITES, AND THAT IS THE
    /// WHOLE OF THE REPORTED DEADLOCK
    /// =====================================================================================</para>
    ///
    /// <para><b>USER REPORT (ModBuild 234 hardware, item 4, verbatim):</b> <i>"DEADLOCK: Nachdem ich
    /// für jeden Character die Quest ausgewählt habe, muss irgendwo der button erscheinen damit es
    /// weiter gehen kann. Der ist nie erschienen, man konnte nicht weiter vorranschreiten."</i></para>
    ///
    /// <para><b>THE MECHANISM, FROM THE LOG AND THE GAME'S OWN SOURCE, WITH NO INFERENCE LEFT IN
    /// IT.</b> The player clicked through the last intro page and the game closed the story box —
    /// <c>Player.log:11533 UIWindow hidden: 'Map Story Window'</c>. The mod's sticky re-assert then
    /// took it back: <c>ModalFallback.ReassertStickyVisible</c> (ModalFallback.7.Close.cs:267-290)
    /// writes <c>CanvasGroup.alpha = 1</c>, <c>Canvas.enabled = true</c> and
    /// <c>GameObject.SetActive(true)</c> on any STICKY float whose game window is hidden. The very
    /// next line is <c>:11534 STICKY FIGHT: 'Map Story Window' has been re-shown 3 frames running</c>
    /// — the WARN threshold, not the concede threshold, which is 20 — so the game stopped hiding it,
    /// the re-assert won, and from that instant the mod was drawing a story box the GAME had
    /// closed.</para>
    ///
    /// <para><b>AND THEN THIS METHOD ACCEPTED THE MOD'S OWN WRITE AS PROOF.</b> Of its three clauses,
    /// exactly one is a value the mod never touches:</para>
    /// <list type="bullet">
    /// <item><c>IsOpen</c> is <c>m_CurrentVisualState == VisualState.Shown</c>
    /// (UnityEngine.UI/UIWindow.cs:317) — the game's own state machine, honest.</item>
    /// <item><c>IsVisible</c> is literally <c>m_CanvasGroup != null &amp;&amp; m_CanvasGroup.alpha &gt; 0</c>
    /// (UIWindow.cs:305-315) — the exact field <c>ReassertStickyVisible</c> pins to 1.</item>
    /// <item><c>FloatedByMod</c> is the mod's own float set.</item>
    /// </list>
    /// <para>So after :11534 the second and third clauses were both TRUE BECAUSE OF THIS MOD, this
    /// method kept returning the window, <c>composeWanted</c> stayed true, the park stayed up, every
    /// clause of <see cref="TickStoryClaim"/>'s <c>want</c> stayed measured-true — and
    /// <c>STORY COMPOSITE CLAIM LAPSED</c> never appears in the ModBuild 234 log at all. The
    /// <c>UILoadoutManager</c> refusal row therefore stood for the rest of the session, and in single
    /// player the continue button is a CHILD of that window
    /// (<c>UILoadoutManager.confirmationButton</c>, switched on by
    /// <c>SetActiveSinglePlayerLongConfirmButton</c>, :88-95). No loadout screen, no button.
    /// [[fuse-was-hiding-a-loop]]: "owned elsewhere" must exclude your own claim.</para>
    ///
    /// <para><b>THE FIX IS A BOUND, NOT A DELETION.</b> The ModBuild 233 reason for the extra clauses
    /// is real and is kept — the ModBuild 232 log's <c>STICKY FIGHT</c> / <c>UNPARKED</c> / <c>BUILT</c>
    /// triple shows the window's own flags going false for ONE TICK while the float stood throughout,
    /// and dropping the claim for that tick would spend one of <see cref="MaxWithdrawCycles"/>. So the
    /// mod-written clauses may now only EXTEND the answer for <see cref="StoryFloatGraceSeconds"/>
    /// past the last tick the GAME said <c>IsOpen</c>. A one-tick dropout is still covered; a game
    /// window that is closed for good can no longer be kept alive by the mod's own re-show.</para>
    /// </summary>
    private static UIWindow? StoryWindow()
    {
        if (!Singleton<MapStoryController>.IsInitialized)
            return null;
        MapStoryController mc = Singleton<MapStoryController>.Instance;
        if (mc == null || mc.window == null)
            return null;
        // THE ONE HONEST TERM FIRST, and it is the only one that may RESTART the clock.
        if (mc.window.IsOpen)
        {
            _storyGameOpenAt = Time.unscaledTime;
            return mc.window;
        }
        // Both remaining terms are mod-written (see the doc block), so they may only extend the
        // answer, and only briefly. Past the grace the story box is gone whatever the mod is drawing.
        if (Time.unscaledTime - _storyGameOpenAt > StoryFloatGraceSeconds)
            return null;
        return mc.window.IsVisible || FloatedByMod(mc.window) ? mc.window : null;
    }

    private static UIWindow? LoadoutWindow()
    {
        if (!Singleton<UILoadoutManager>.IsInitialized)
            return null;
        UILoadoutManager lm = Singleton<UILoadoutManager>.Instance;
        return lm != null ? lm.GetComponent<UIWindow>() : null;
    }

    /// <summary>
    /// HAS THE QUEST INTRO ALREADY FINISHED? Read from the game's own tween target:
    /// <c>UILoadoutQuestWindow.Show</c> sets <c>paperFitter.transitionPercent = 0f</c> (:52) and the
    /// ONLY thing that raises it is <c>FinishIntroduction</c>, which LeanTweens it 0 → 1 once the
    /// player has clicked through the last page (:101-107). So a value above
    /// <see cref="PaperExpandedEpsilon"/> means "the paper is expanding or expanded", i.e. the
    /// illustration is no longer a small picture waiting to be composed but a full-window backdrop
    /// for the loadout UI.
    ///
    /// <para>This is a DIAGNOSTIC ONLY: it decides the severity of a report line, never whether the
    /// composite stands. The composite is gated on the picture itself (parked, sized, active,
    /// sprited) so that a prefab without this fitter, or a future asset change, can only cost a
    /// nicer log line — [[verify-outcome-not-path]], do not gate behaviour on a field that looks
    /// like it should mean something.</para>
    /// </summary>
    private static bool PaperExpanded(UIWindow loadout)
    {
        try
        {
            if (!Singleton<UILoadoutManager>.IsInitialized)
                return false;
            UILoadoutManager lm = Singleton<UILoadoutManager>.Instance;
            UILoadoutQuestWindow? info = lm != null ? lm.questInfo : null;
            AspectRatioFitterTransition? fitter = info != null ? info.paperFitter : null;
            return fitter != null && fitter.transitionPercent > PaperExpandedEpsilon;
        }
        catch (System.Exception)
        {
            // The severity of one log line is not worth a throw reaching the modal tick. The
            // `loadout` argument is kept so a future round has the window to name if this ever
            // needs to say WHICH screen it could not read.
            _ = loadout;
            return false;
        }
    }

    // ---- the per-tick step --------------------------------------------------------------------

    /// <summary>
    /// Called once per <c>ModalFallback.Tick</c>, from the first line of <c>TickWindowLiveness</c>.
    ///
    /// <para><b>THAT CALL SITE IS LOAD-BEARING AND NOT A CONVENIENCE.</b> The unpark has to run
    /// BEFORE the release loop: the release loop calls <c>CanvasConversion.Release</c>, which
    /// destroys the host GameObject the story window was re-parented under, and a subtree that is
    /// still parked under a destroyed host cannot be handed back to the loadout screen. The liveness
    /// step is the earliest per-tick point of <c>ModalFallback</c> that runs before that loop, and
    /// it is in a file this lane owns.</para>
    ///
    /// <para>EVERYTHING THIS METHOD DOES AFTER THE GATE EDGE IS COUNTING AND EXPIRY. It closes no
    /// window, releases no float and suppresses nothing. Grep the log for
    /// <c>POINT OF NO RETURN OPENED</c>: it must appear exactly once per quest, and there must be no
    /// second closing line of any kind after it.</para>
    /// </summary>
    internal static void Tick()
    {
        // Roll the convert-pass counters first: everything below reads the LAST complete pass.
        _heldLastPass = _heldThisPass;
        _heldThisPass = 0;

        // ModBuild 238 — THE EARLIEST EDGE OF ALL, AND IT IS BEFORE ANYTHING THIS CLASS OWNS. The
        // quest list must go at the CONFIRM, not when the story message arrives, and the confirm is
        // hundreds of log lines earlier than the curtain's own edge. It is a level of its own with a
        // subject of its own — see QuestJourneyCurtain for why it cannot be a second rising edge on
        // TickCurtain — and it is ticked here because this is the earliest per-tick point of
        // ModalFallback and because the refusal it raises must be settled before the release loop.
        QuestJourneyCurtain.Tick();

        // ModBuild 381 — THE TUTORIAL HINT GOES ON THE WINDOW IT IS TALKING ABOUT, and it is ticked
        // from here for THIS METHOD'S OWN TWO REASONS rather than as a convenience. (1) Its unpark
        // must run BEFORE the release loop, which destroys the host the hint was parked under.
        // (2) Its park must run BEFORE the convert pass, so ModalFallback.RendersInsideFloatedAncestor
        // has already made the hint a sub-view by the time anything would have floated it — which is
        // what keeps the catch-all's float fuse from ever counting it. It is ABOVE every precondition
        // of this class because it has nothing to do with the story composite: the hint family is
        // UIIntroductionManager's own LevelMessageUILayoutGroup, which is neither of the two groups
        // LevelMessagesUIHandler owns. See HintOnOwnerComposite.
        HintOnOwnerComposite.Tick();

        UIWindow? story = StoryWindow();
        UIWindow? loadout = LoadoutWindow();

        // ModBuild 234 — THE CURTAIN LEVEL IS COMPUTED FIRST, BECAUSE THE GATE NOW READS IT.
        // PointOfNoReturn is the UNION of "the story curtain stands" and "the loadout screen is up",
        // so the curtain's own level has to be settled before the gate edge below is evaluated
        // against it. TickCurtain closes nothing and writes nothing to the game: it raises or drops
        // a presentation refusal over a set of window instances frozen at its own rising edge.
        TickCurtain(story, loadout);

        bool gateWanted = PointOfNoReturn;
        // ModBuild 236 — THE COMPOSITE EXISTS ONLY WHILE ALL OF THESE DO, AND THE TERMINATORS ARE
        // PART OF IT RATHER THAN OF THE CLAIM ALONE.
        //
        // Up to ModBuild 235 TerminatedBy dropped the CLAIM while the park stayed up. With the host
        // inverted that combination is not merely untidy, it is a window drawing nothing: the claim
        // is what keeps the story window out of the float set, so a claim that lapses under a
        // standing park floats an EMPTY story window whose entire content is inside the loadout
        // screen. One level, one instant: the park and the claim end together.
        string? terminator = TerminatedBy(loadout);
        bool composeWanted = gateWanted && !_floorLifted && terminator == null
                             && story != null && loadout != null && loadout.IsOpen;

        // THE BRANCH THE ROUND ASKED FOR, MADE VISIBLE: a story box with no loadout screen to host it
        // is thirteen of the fourteen callers of MapStoryController.Show, and it must float alone
        // exactly as it always has. It is logged once per box so the branch is a LINE in the log
        // rather than an absence somebody has to infer.
        if (story == null)
            _noHostReported = null;   // re-arm: the NEXT story box gets its own line
        else if (MapRoomDriver.Active && (loadout == null || !loadout.IsOpen))
            ReportNoHost(story, loadout);

        if (composeWanted)
            EnsureParked(loadout!, story!);
        else
            Unpark(story == null ? "the story box closed"
                   : loadout == null || !loadout.IsOpen ? "the loadout screen closed"
                   : _floorLifted ? "the deadlock floor stood this composite down for the rest of the gate"
                   : terminator ?? "the 3D map room stood down");

        // ModBuild 235 — THE CONFIRM PARKER, AND ModBuild 236 LEFT IT WHERE IT WAS.
        //
        // The order is load-bearing in one direction only: TerminatedBy above asks
        // LoadoutConfirmPark.GameWantsConfirmShown(), and that read is PURE (it resolves the control
        // and reads two game fields; it never looks at the park), so it would give the same answer
        // either side of this call. What running the parker FIRST buys is that on the tick the game
        // switches the confirm on, the control is already where it is going to be drawn before
        // anything downstream measures whether it is reachable — one fewer frame in which the
        // falsifier could truthfully report a button nobody can see.
        //
        // AFTER THE INVERSION THE PARKER IS USUALLY INERT, AND THAT IS THE POINT: the loadout screen
        // is never withheld now, so offline the confirm is already on a floated window and the parker
        // stands down rather than moving it off the window the user named. Online it still parks the
        // ready toggle, which is its own window root and is refused as a bare control.
        //
        // IT IS CALLED FROM HERE RATHER THAN FROM ModalFallback because this is the earliest per-tick
        // point that runs BEFORE the release loop, and the release loop destroys the host GameObject
        // the parker parks into. Same call site, same reason, as this method's own unpark.
        LoadoutConfirmPark.Tick();

        // ModBuild 238 — THE BACKGROUND WINDOW IS WITHDRAWN ONCE THE STORY IS TOLD, and this call is
        // AFTER the parker on purpose: the level it computes asks whether the continue control is
        // drawing somewhere that is not the loadout screen, and on the tick the game switches that
        // control on the parker has to have moved it already or the answer would be a false NO for
        // one tick and cost a float this class would then have to withdraw.
        TickLoadoutBackdrop(loadout, terminator);

        // ModBuild 233 — THE CLAIM AND THE FALSIFIER, IN THAT ORDER AND BOTH FROM MEASUREMENT.
        // The claim decides whether the STORY window is drawn by us this tick (ModBuild 236: it used
        // to be the loadout screen); the falsifier then states, from a fresh read of the float set,
        // whether the result is actually ONE window.
        TickStoryClaim(story, loadout, terminator);
        // ModBuild 237 — WHO CARRIES SharedWindowKind.MapStory THIS TICK, decided from the claim that
        // was just settled and from nothing else. While the claim stands, the story is being told
        // inside the composed host, so the host is the window that wears the blue bar, has its pose
        // published and applied, and is exempt from the release re-face; when it lapses the identity
        // hands back. It is fed the CLAIM rather than the park alone because the claim is the level
        // that already means "the mod is drawing the story inside a floated host" — the park can stand
        // for a tick inside a window nobody is floating, and a blue bar on that would be a lie.
        // The swap itself is deferred while a hand is on either bar; see SharedWindowIdentity.
        SharedWindowIdentity.TickMapStory(_claimStanding ? _parkHost : null, story);
        ReportOneWindow(story, loadout);
        // ModBuild 234's two falsifiers, both re-measured from the live objects on this tick and
        // neither of them reading any state this class set: "is the composed host the ONLY floated
        // window" and "is the dialog actually directly under the picture, in millimetres". ModBuild
        // 236 adds the third, which answers the reported complaint directly.
        ReportOnlyWindow(ComposedHost(story, loadout));
        ReportGap();
        ReportContinuity(loadout, terminator);

        bool openedThisTick = false;
        if (gateWanted && !_gateOpen)
        {
            OpenGate(loadout);
            openedThisTick = true;
        }
        else if (!gateWanted && _gateOpen)
        {
            CloseGate();
        }

        if (!_gateOpen)
            return;

        // Not on the opening tick: the bridge is armed for HoldBridgeTicks CONVERT PASSES, and the
        // first of those passes happens later in this very tick (PhaseConvert :2675). Ageing it here
        // would spend one of the two before it had been offered to anything.
        if (!openedThisTick)
            TickHoldBridge();
        TickDeadlockFloor();
    }

    // ---- the loadout-float claim ---------------------------------------------------------------

    /// <summary>
    /// IS THE MOD DRAWING THIS LOADOUT WINDOW SOMEWHERE BETTER RIGHT NOW? The question
    /// <c>FloatRefusalTable</c>'s conditional row asks, and a PURE read — no state, no logging, safe
    /// from the recursive ancestor walk that re-enters <c>Refuses</c> several times per tick.
    ///
    /// <para>The answer is a level computed once per tick by <see cref="TickStoryClaim"/> from
    /// live measurement, and it is asked ABOUT A SPECIFIC GameObject: <c>UILoadoutManager</c> is a
    /// Singleton and a claim about one open of it must never refuse another.</para>
    ///
    /// <para><b>THE FAIL DIRECTION IS FIXED AND IT IS THE ONLY THING THAT MATTERS HERE.</b> Every
    /// clause of the claim is a POSITIVE fact about the composite standing (the story window's own
    /// content moved into a dock, still under it, still active, inside a LOADOUT WINDOW THE MOD IS
    /// FLOATING). If any of them stops being true — the story box closes, the park throws, the game
    /// re-parents the content, this class stops running — the claim is false within one tick and the
    /// story window floats with everything on it. There is no state in which this method can hide a
    /// window that is not being shown somewhere else. [[parent-wins-needs-a-real-parent]]:
    /// suppressing X because Y handles it requires Y to ACTUALLY handle it, so the test is "Y is
    /// handling it", not "Y ought to be".</para>
    /// </summary>
    internal static bool HoldsStoryFloatBack(GameObject? storyWindowObject) =>
        _claimStanding && storyWindowObject != null
        && ReferenceEquals(storyWindowObject, _claimObject);

    /// <summary>The claim's own words for what it did with the story window, printed verbatim by
    /// the refusal line and by the lapse warning. Never null.</summary>
    internal static string StoryClaimWhy => _claimWhy;

    /// <summary>
    /// WHICH WINDOW IS THE ONE WINDOW RIGHT NOW — the composed HOST while the composite stands, and
    /// otherwise the story box (or, in the seam between the two chains, the loadout screen). Every
    /// falsifier that used to name the story box asks this instead, so
    /// <c>STORY CURTAIN ONLY WINDOW: CONFIRMED</c> keeps meaning what it says after the inversion.
    ///
    /// <para>It is a MEASUREMENT of this class's own park, not a policy: with nothing parked it names
    /// whichever of the two halves is actually up, which is exactly what the reader needs.</para>
    /// </summary>
    private static UIWindow? ComposedHost(UIWindow? story, UIWindow? loadout)
    {
        if (_dock != null && _parkHost != null)
            return _parkHost;
        // ModBuild 238 — ONCE THE BACKDROP IS WITHDRAWN THE ONE WINDOW IS THE CHARACTER-UI. That is
        // the whole of the user's new request for this phase: "So braucht man nicht die Übersicht
        // über zwei Fenster behalten." A falsifier that kept naming the window this round exists to
        // take OFF the screen would report NOT ACHIEVED for exactly the state it is meant to confirm
        // — the class of instrument defect this project has already had to retract twice.
        if (_backdropStanding)
            return LoadoutConfirmPark.CharacterUI ?? story ?? loadout;
        return story ?? loadout;
    }

    /// <summary>
    /// HAS THE THING THIS CLAIM WAS RAISED FOR ENDED? Returns the sentence to print when it has, and
    /// null while the quest intro is genuinely still being clicked through.
    ///
    /// <para><b>THE RULE THIS METHOD IS: A CLAIM THAT SUPPRESSES A WINDOW MUST NOT BE ABLE TO OUTLIVE
    /// THE THING IT WAS RAISED FOR.</b> Up to ModBuild 234 every clause of the claim was a property of
    /// the PARK (a rect with a sprite, under a host the mod is floating), and none of them is a
    /// property of the INTRO. When the mod's own sticky re-show kept the story box on screen after the
    /// game had closed it, all of them stayed true and the claim stood for the rest of the session —
    /// see <see cref="StoryWindow"/> for the log lines. So the claim now also has to answer to two
    /// facts THE MOD DOES NOT WRITE, and either one alone ends it.</para>
    ///
    /// <list type="number">
    /// <item><b>THE PAPER HAS EXPANDED.</b> <c>UILoadoutQuestWindow.Show</c> sets
    /// <c>paperFitter.transitionPercent = 0f</c> (:52) and the ONLY thing that raises it is
    /// <c>FinishIntroduction</c>, which LeanTweens it 0 → 1 once the player has clicked through the
    /// last page (:101-107). That is the game stating, in one float, that the intro is over and the
    /// loadout screen is about to become a real screen. <see cref="PaperExpanded"/> already read it —
    /// as a DIAGNOSTIC ONLY, deciding the severity of a log line and nothing else. It decides
    /// behaviour now, and the [[verify-outcome-not-path]] caveat that kept it diagnostic is answered
    /// by clause 2: a prefab without this fitter loses nothing, because the second clause is
    /// independent of it.</item>
    /// <item><b>THE GAME WANTS A CONTINUE CONTROL SHOWN.</b>
    /// <see cref="LoadoutConfirmPark.GameWantsConfirmShown"/> — the game's own
    /// <c>UILoadoutManager.CanShowConfirmationButton()</c> plus its own switch on the control. This is
    /// the clause that speaks to the user's actual complaint rather than to a proxy for it: the whole
    /// cost of the fault was a button that never appeared, so the moment the game says a button should
    /// appear, no presentation claim of this mod's may be standing between it and the player.</item>
    /// </list>
    ///
    /// <para>Both are pure reads and both fail SAFE: a throw or an unreadable field yields "not
    /// terminated" from clause 1 and "the game is not asking" from clause 2, which can only make the
    /// claim last longer — and the claim's own park clauses, the cycle cap and the deadlock floor all
    /// still bound it. There is no failure of this method that can reproduce the ModBuild 234 state,
    /// because that state required the mod to be the ONLY thing showing the story box, which
    /// <see cref="StoryWindow"/> no longer allows.</para>
    ///
    /// <para><b>ModBuild 236 — IT NOW TERMINATES THE WHOLE COMPOSITE, NOT ONLY THE CLAIM</b>, and it
    /// is read once per tick in <see cref="Tick"/> and passed down. With the host inverted the two
    /// cannot be separated: the claim is what keeps the story window out of the float set, so a claim
    /// that ended while the park stood would float an empty story window. It also stopped being a
    /// deadlock guard and became an ORDINARY end condition — the control it protects is a child of
    /// the HOST now, and nothing in this class can withhold the host.</para>
    /// </summary>
    private static string? TerminatedBy(UIWindow? loadout)
    {
        if (loadout == null)
            return null;   // no loadout screen at all: the park clauses answer this one
        if (PaperExpanded(loadout))
            return "the quest intro is OVER — UILoadoutQuestWindow.FinishIntroduction has started "
                   + "expanding the paper (paperFitter.transitionPercent is above "
                   + $"{PaperExpandedEpsilon:0.00}), which is the game's own statement that the player "
                   + "has clicked through the last page and the loadout screen is about to become a "
                   + "real screen";
        if (LoadoutConfirmPark.GameWantsConfirmShown())
            return "THE GAME WANTS A CONTINUE CONTROL SHOWN (UILoadoutManager.CanShowConfirmationButton() "
                   + "is true and the game has switched the control on), so no presentation claim of "
                   + "this mod's may stand between it and the player — that is the ModBuild 234 "
                   + "deadlock's own condition, used here as the thing that makes it impossible";
        return null;
    }

    /// <summary>
    /// Recompute the claim LEVEL, count its rising edges against <see cref="MaxWithdrawCycles"/> and
    /// print ONE line per edge.
    ///
    /// <para>ModBuild 236 — THE SUBJECT IS THE STORY WINDOW NOW. The claim stands only while
    /// <b>all</b> of these are measured true this tick: a dock exists, it is still parented to the
    /// host, it still holds at least one child taken from this story window, it is active in the
    /// hierarchy, and THE MOD IS FLOATING THE HOST. The last clause is the one that makes the refusal
    /// honest: a dock inside a window nobody is drawing is not on screen, and refusing the story box
    /// then would show the player nothing at all.</para>
    /// </summary>
    private static void TickStoryClaim(UIWindow? story, UIWindow? loadout, string? terminator)
    {
        _ = loadout;   // kept in the signature so a future round has the host to name here
        // A different story window instance gets a fresh cycle budget: the cap exists to protect ONE
        // window's churn record inside ONE 60 s fuse window, not to ration the feature. CloseGate
        // refills it per quest for the same reason.
        if (!ReferenceEquals(story, _claimHost))
        {
            _claimHost = story;
            _claimCycles = 0;
            _claimCapReported = false;
        }

        bool want = false;
        string why = string.Empty;
        if (terminator == null && story != null && _dock != null && _parkHost != null
            && ReferenceEquals(_parkSource, story))
        {
            bool underHost = _dock.parent != null
                             && ReferenceEquals(_dock.parent, _parkHost.transform);
            bool active = _dock.gameObject.activeInHierarchy;
            bool holding = Moved.Count > 0;
            bool hostFloated = FloatedByMod(_parkHost);
            want = underHost && active && holding && hostFloated;
            if (want)
                why = $"the story window's own content ({Moved.Count} root child object(s), "
                      + $"{_dockSize.x:F0}x{_dockSize.y:F0} px of authored rect) has been moved INTO "
                      + $"the floated loadout window '{_parkHost.name}', directly under the quest "
                      + $"illustration '{(_picture != null ? _picture.name : "<none>")}' — one panel, "
                      + "one grab bar. The story window itself is an empty frame with no Graphic on "
                      + "its root while that stands, so floating it would put a pane of nothing in "
                      + "the room";
        }

        if (want && !_claimStanding)
        {
            if (_claimCycles >= MaxWithdrawCycles)
            {
                if (!_claimCapReported)
                {
                    _claimCapReported = true;
                    VRLog.Warn(Scope, $"STORY COMPOSITE CLAIM CAPPED — the composite has already asked "
                                      + $"{_claimCycles} time(s) for '{(story != null ? story.name : "<none>")}' "
                                      + "to stand down and it will not ask again until the point-of-no-return "
                                      + "gate closes. THE CAP IS DERIVED, NOT CHOSEN: ModalFallback's catch-all "
                                      + "churn fuse suppresses a window's NAME for the whole session on its 4th "
                                      + "enrolment inside 60 s, a REFUSED window is never counted, and each "
                                      + $"claim that falls costs exactly one re-float — so {MaxWithdrawCycles} "
                                      + "cycles plus the first float is 3, at the fuse's limit. FROM HERE BOTH "
                                      + "HALVES FLOAT, which is the ModBuild 232 presentation: two windows, "
                                      + "ugly, and NOT a deadlock. IF YOU ARE READING THIS IN A HARDWARE LOG "
                                      + "the composite is flapping — grep STORY COMPOSITE UNPARKED for what "
                                      + "kept taking the dialog away.");
                }
                want = false;
            }
            else
            {
                _claimCycles++;
                _claimWhy = why;
                _claimObject = story!.gameObject;
                VRLog.Info(Scope, $"STORY COMPOSITE CLAIM RAISED (cycle {_claimCycles} of "
                                  + $"{MaxWithdrawCycles}) on '{story.name}': {why}. FloatRefusalTable's "
                                  + "conditional MapStoryController row now refuses to float it. ON THE "
                                  + "ORDINARY PATH THAT COSTS NOTHING AT ALL: this class runs from the first "
                                  + "line of TickWindowLiveness and the catch-all's convert pass runs later in "
                                  + "the SAME tick, so the story window is refused BEFORE it is ever enrolled "
                                  + "and there is no float to withdraw and no flash. If it was already floated "
                                  + "— the quest-start chain opens before the loadout screen exists — the same "
                                  + "refusal WITHDRAWS it, because it is asked at the TOP of the catch-all "
                                  + "loop (ModalFallback.10.CatchAll.cs:234 → WithdrawRefusedFloat: Sticky "
                                  + "dropped, ordinary release loop takes panel + grab bar + slot down, "
                                  + "CanvasConversion.Release restores the exact 2D home). NOTHING IS WRITTEN "
                                  + "TO THE GAME — no Hide, no Escape, no SetActive, no CanvasGroup — and this "
                                  + "is NOT ModBuild 231's convert-loop hold: a refused window is never "
                                  + "enrolled and therefore never counted by the churn fuse.");
            }
        }
        else if (!want && _claimStanding)
        {
            VRLog.Info(Scope, $"STORY COMPOSITE CLAIM LAPSED on "
                              + $"'{(story != null ? story.name : "<the story window is gone>")}' — "
                              + (terminator ?? "the composite is no longer standing")
                              + ", so the story window floats again from this tick with everything on it "
                              + "(nothing was ever written to it). This is the level ending, not a failure: "
                              + "the ordinary end of it is the player clicking through the last page of the "
                              + "intro, after which the GAME closes that window itself and there is nothing "
                              + $"left to float. {_claimCycles} of {MaxWithdrawCycles} cycle(s) used. THE "
                              + "PARK LAPSES WITH IT, IN THE SAME INSTANT (ModBuild 236): the claim is what "
                              + "keeps the story window out of the float set, so a claim that ended under a "
                              + "standing park would float an EMPTY story window whose whole content is "
                              + "inside the loadout screen.");
            _claimObject = null;
            _claimWhy = "the quest-intro composite has stood down; the story window is nobody's "
                        + "responsibility but its own";
        }
        _claimStanding = want;
    }

    // ---- the loadout BACKDROP withdrawal (ModBuild 238) -----------------------------------------

    private static bool _backdropStanding;
    private static GameObject? _backdropObject;
    private static bool _backdropLifted;
    private static int _backdropCycles;
    private static bool _backdropCapReported;
    private static string _backdropWhy =
        "the quest-intro composite has never withheld the loadout screen's float";
    private static string _backdropVerdict = string.Empty;
    private static int _backdropReports;

    /// <summary>Same derivation as <see cref="MaxWithdrawCycles"/> and <see cref="MaxCurtainCycles"/>:
    /// the catch-all churn fuse suppresses a window's NAME on its 4th enrolment inside 60 s, a REFUSED
    /// window is never counted, and each claim that falls costs exactly one re-float — so two cycles
    /// plus the first float is 3, at the fuse's limit and never over it.</summary>
    private const int MaxBackdropCycles = 2;

    /// <summary><see cref="MaxOneWindowReports"/>'s number and argument.</summary>
    private const int MaxBackdropReports = 6;

    // ---- ModBuild 364: the backdrop claim's INTENT, and the settle under its honesty clause ------

    /// <summary>
    /// THE WINDOW THIS CLAIM IS AIMED AT THIS TICK, or null while the phase it belongs to is not
    /// live. It is the loadout window during the interval where <see cref="TerminatedBy"/> has spoken,
    /// the map room is up and the claim has not been lifted — i.e. the interval in which this class
    /// is actively trying to take that window OFF the screen, whether or not the claim currently
    /// stands.
    ///
    /// <para><b>WHY THE INTENT AND NOT THE LEVEL.</b> <see cref="TickMutualHold"/> needs to know that
    /// the one window on screen is one this class WANTS gone. The level (<see cref="_backdropStanding"/>)
    /// cannot answer that: in the reported stall it read False for the whole 23 seconds, precisely
    /// because the claim was blocked. A clause that can only see the other clause's current STATE and
    /// never its INTENT is what made the two of them wait for each other.</para>
    /// </summary>
    private static UIWindow? _backdropSubject;

    /// <summary>
    /// HOW MANY CONSECUTIVE TICKS A STANDING BACKDROP CLAIM MAY SURVIVE ITS OWN HONESTY CLAUSE
    /// READING ZERO. The oscillation damper, and the number is measured rather than chosen.
    ///
    /// <para><b>WHAT IT DAMPS.</b> In the ModBuild 362 log the claim raised at :16204 (cycle 1 of 2),
    /// lapsed at :16267 and raised again at :16354 (cycle 2 of 2) — three edges inside 150 lines, and
    /// the second raise put it AT the cap, one flap away from leaving the backdrop window on screen
    /// for the rest of the quest. The middle edge is the one that had no business happening: the
    /// curtain lapsed at :16266 and the claim lapsed at :16267, the SAME tick, because the curtain's
    /// members had not been converted yet — 'New Party display' floats at :16272-16273, one convert
    /// pass later. The honesty clause was reading a one-pass gap as an empty room.</para>
    ///
    /// <para><b>THE DERIVATION.</b> The mechanism's own worst case for that gap is one full convert
    /// pass after the refusal drops plus <see cref="HoldBridgeTicks"/> of bridge = 3 ticks. Eight is
    /// over twice that and is still under a tenth of a second at the 90 Hz the reported run measured
    /// (:16210, 2286 frames over 25.4 s). <b>THE COST OF BEING WRONG IS BOUNDED AND IT IS PAID IN
    /// FULL:</b> while the settle runs the room really is empty, so this may never be raised to a
    /// number a player could see, and when it expires the claim lapses exactly as it does today.</para>
    /// </summary>
    private const int BackdropHonestySettleTicks = 8;

    private static int _backdropHonestySettle;
    private static bool _backdropSettleReported;

    /// <summary>
    /// IS THE MOD WITHHOLDING THIS LOADOUT WINDOW'S FLOAT RIGHT NOW? The question
    /// <c>FloatRefusalTable</c>'s ROW 4 asks, and a PURE read — no state, no logging, safe from the
    /// recursive ancestor walk that re-enters <c>Refuses</c> several times per tick.
    ///
    /// <para>Asked ABOUT A SPECIFIC GameObject: <c>UILoadoutManager</c> is a Singleton and a claim
    /// about one open of it must never withhold another.</para>
    /// </summary>
    internal static bool HoldsLoadoutBackdropBack(GameObject? loadoutWindowObject) =>
        _backdropStanding && loadoutWindowObject != null
        && ReferenceEquals(loadoutWindowObject, _backdropObject);

    /// <summary>The claim's own words, printed verbatim by the refusal line and by the lapse warning.
    /// Never null.</summary>
    internal static string LoadoutBackdropWhy => _backdropWhy;

    /// <summary>
    /// RECOMPUTE THE LEVEL THAT WITHHOLDS THE LOADOUT SCREEN AFTER THE STORY HAS BEEN TOLD.
    ///
    /// <para><b>USER REQUEST (2026-08-23), verbatim, and it SUPERSEDES what ModBuild 236 shipped for
    /// the phase after the story:</b> <i>"Ich möchte das letzte lokale Storyfenster mit nur dem
    /// Hintergrund doch nicht haben. Nachdem die Story erzählt wurde soll die Character-UI für die
    /// persönlichen Quests spawnen und das Storyfenster verschwinden. Hat man für all seine
    /// zugewiesenen Charactere die Quest ausgewählt soll der Button der jetzt auf dem Fenster mit nur
    /// dem Bild zu sehen ist 'Verlies betreten' am unteren Rand der character-UI zu sehen sein, wo
    /// man ihn betätigen kann. So braucht man nicht die Übersicht über zwei Fenster
    /// behalten."</i></para>
    ///
    /// <para><b>NOTHING ABOUT THE STORY PHASE CHANGES.</b> The story is still told inside the loadout
    /// window, one window, picture above and dialog directly under it — ModBuild 236's inversion, and
    /// the ModBuild 237 log's <c>FLOAT WITHDRAWN: 'UI Loadout Window'</c> count of ZERO is the thing
    /// that must stay zero for the intro. This level cannot stand while the intro is running because
    /// its first clause IS the intro's end: <see cref="TerminatedBy"/>, the same value the composite
    /// itself ends on, read once per tick in <see cref="Tick"/> and passed down so the two can never
    /// disagree about which tick that was.</para>
    ///
    /// <para><b>THE SECOND CLAUSE IS THE DEADLOCK GUARD AND IT IS THE ONLY REASON THIS IS SAFE.</b>
    /// ModBuild 234 deadlocked this exact flow by withholding the window the only advancing control
    /// was a child of, and that control is a child of THIS window offline
    /// (<c>UILoadoutManager.confirmationButton</c>, switched on by
    /// <c>SetActiveSinglePlayerLongConfirmButton</c>, UILoadoutManager.cs:88-95). So the level asks
    /// <see cref="LoadoutConfirmPark.ContinueReachableOffTheLoadout"/>: is the control drawing inside
    /// a live floated panel that is NOT this window? It answers TRUE for free while the game is not
    /// asking for a control at all — the whole battle-goal phase — and it answers FALSE the instant
    /// the game asks and the park has not landed. FALSE means this level does not stand, the loadout
    /// screen floats with its own button on it, and the player continues. THE FAIL DIRECTION IS
    /// TOWARDS THE PLAYER BEING ABLE TO GO ON, every tick, with no latch anywhere in it.</para>
    ///
    /// <para><b>AND IT MUST NOT MEASURE ITSELF.</b> The obvious predicate — "the control is reachable"
    /// — is TRUE while this window floats precisely BECAUSE it floats, so gating on it would
    /// oscillate once per tick [[a-claim-must-not-measure-itself]]. The exclusion of the subject is
    /// what makes the level stable, and it is why the read has its own method rather than reusing
    /// <c>ConfirmReachable</c>.</para>
    ///
    /// <para>MULTIPLAYER: nothing here goes on the wire, and both network modes end in the same place.
    /// Offline the control is <c>confirmationButton</c>, a child of this window, and
    /// <c>LoadoutConfirmPark</c> moves it to the Character-UI. Online it is <c>UIReadyToggle</c>,
    /// which is its own window root and is refused by ROW 2 as a bare control, and the same parker
    /// puts it in the same place. <c>SetActiveConfirmationButton</c> branches only on
    /// <c>FFSNetwork.IsOnline</c> and has no host test, so pressing it is a LOCAL act in both.</para>
    /// </summary>
    private static void TickLoadoutBackdrop(UIWindow? loadout, string? terminator)
    {
        bool want = false;
        string why = string.Empty;
        // THE HONESTY CLAUSE, and it is the curtain's word for word: this claim may only stand while
        // the mod is floating at least one window it is NOT withholding. Without it there is a real
        // reachable state — the battle-goal phase, where the game is not asking for a continue
        // control, so the deadlock clause below is satisfied for free — in which withholding this
        // window would leave the room with nothing in it at all. That is ModBuild 231's ending, and
        // [[parent-wins-needs-a-real-parent]]: suppressing X because Y is showing the player
        // something requires Y to ACTUALLY be showing it.
        bool somethingElseOnScreen = loadout != null
                                     && ModalFallback.CountFloatsOtherThan(loadout) > 0;
        // ModBuild 364 — THE PHASE TERMS ARE NAMED SEPARATELY FROM THE HONESTY CLAUSE, and that
        // separation is the whole of what this class now publishes about itself. `phaseLive` is "this
        // class is trying to take that window off the screen"; the honesty clause is "it is allowed
        // to right now". Up to 363 the two were one boolean, so the only thing another clause could
        // read was the LEVEL — and in the reported stall the level read False for 23 seconds for
        // exactly the reason the other clause needed to know about. The evaluation ORDER is unchanged
        // and so is the short circuit: ContinueReachableOffTheLoadout is still only called when
        // `somethingElseOnScreen` already holds, because it can end in a painted-bounds sweep
        // [[one-line-owned-the-frame]].
        bool phaseLive = !_backdropLifted && MapRoomDriver.Active && loadout != null
                         && loadout.IsOpen && terminator != null;
        _backdropSubject = phaseLive ? loadout : null;
        if (phaseLive && somethingElseOnScreen
            && LoadoutConfirmPark.ContinueReachableOffTheLoadout(loadout))
        {
            want = true;
            why = "the quest intro is OVER and the window has nothing left on it but the backdrop "
                  + "picture — " + terminator + ". The user's ruling for this phase is that this "
                  + "window must not be there at all: \"Ich möchte das letzte lokale Storyfenster mit "
                  + "nur dem Hintergrund doch nicht haben.\" THE CONTINUE CONTROL IS NOT ON IT: "
                  + LoadoutConfirmPark.Where + ", and this claim is re-measured every tick against "
                  + "that fact with THIS window excluded from the answer, so the instant the control "
                  + "is not drawing anywhere else the claim is false and this window floats again "
                  + "with its own button on it";
        }

        // ModBuild 364 — THE OSCILLATION DAMPER, AND IT ONLY EVER DELAYS A LAPSE THIS CLAUSE WOULD
        // HAVE TAKEN ANYWAY.
        //
        // It runs in exactly one state: the claim is STANDING, every phase term still holds, and the
        // only failing term is the honesty clause. That is the shape of the ModBuild 362 flap at
        // :16266-16267 — the curtain lapsed and this claim lapsed on the same tick, before the
        // curtain's members had been through a convert pass. Nothing else can reach this branch: a
        // closed loadout screen, a re-running intro, a lift by the floor and an unreachable continue
        // control all fail `phaseLive` or the control test and lapse on the tick, as they always did.
        //
        // WHY A LAPSE IS EXPENSIVE ENOUGH TO BE WORTH DELAYING: each one costs a cycle out of
        // MaxBackdropCycles, and at the cap the backdrop window stays on screen for the rest of the
        // quest. A claim that lapses on a one-pass gap and re-raises spends the budget on nothing.
        // WHAT IT COSTS TO BE WRONG: up to BackdropHonestySettleTicks ticks of a genuinely empty room
        // — bounded, counted, printed, and then the lapse happens exactly as it does today.
        if (!want && _backdropStanding && phaseLive && !somethingElseOnScreen
            && LoadoutConfirmPark.ContinueReachableOffTheLoadout(loadout))
        {
            if (_backdropHonestySettle < BackdropHonestySettleTicks)
            {
                _backdropHonestySettle++;
                want = true;
                if (!_backdropSettleReported)
                {
                    _backdropSettleReported = true;
                    VRLog.Warn(Scope, "LOADOUT BACKDROP CLAIM SETTLING UNDER ITS HONESTY CLAUSE — the "
                                      + "mod is floating nothing this claim is not withholding, but "
                                      + "every other term still holds, so the claim is HELD for up to "
                                      + $"{BackdropHonestySettleTicks} tick(s) instead of lapsing. THE "
                                      + "STATE THIS DAMPS (ModBuild 362 log :16266-16267): the story "
                                      + "curtain lapsed and this claim lapsed on the same tick, before "
                                      + "the curtain's members had been through one convert pass — "
                                      + "'New Party display' floated at :16272 — and the re-raise at "
                                      + ":16354 put the claim at 2 of 2 cycles for nothing. WHAT IT "
                                      + "COSTS IF THE SETTLE IS WRONG: the room is empty for at most "
                                      + $"{BackdropHonestySettleTicks} tick(s) and then this claim "
                                      + "lapses exactly as it always did. NOTHING IS WRITTEN TO THE "
                                      + "GAME by holding it.");
                }
            }
        }
        else
        {
            _backdropHonestySettle = 0;
            _backdropSettleReported = false;
        }

        if (want && !_backdropStanding)
        {
            if (_backdropCycles >= MaxBackdropCycles)
            {
                if (!_backdropCapReported)
                {
                    _backdropCapReported = true;
                    VRLog.Warn(Scope, "LOADOUT BACKDROP CLAIM CAPPED — the composite has already asked "
                                      + $"{_backdropCycles} time(s) for "
                                      + $"'{loadout!.name}' to stand down and it will not ask again "
                                      + "until the point-of-no-return gate closes. THE CAP IS DERIVED, "
                                      + "NOT CHOSEN: ModalFallback's catch-all churn fuse suppresses a "
                                      + "window's NAME for the whole session on its 4th enrolment "
                                      + "inside 60 s, a REFUSED window is never counted, and each "
                                      + "claim that falls costs exactly one re-float — so "
                                      + $"{MaxBackdropCycles} cycles plus the first float is 3, at the "
                                      + "fuse's limit. FROM HERE THE BACKGROUND WINDOW STAYS ON "
                                      + "SCREEN, which is the ModBuild 237 presentation: two windows, "
                                      + "ugly, and NOT a deadlock.");
                }
                want = false;
            }
            else
            {
                _backdropCycles++;
                _backdropWhy = why;
                _backdropObject = loadout!.gameObject;
                VRLog.Info(Scope, $"LOADOUT BACKDROP CLAIM RAISED (cycle {_backdropCycles} of "
                                  + $"{MaxBackdropCycles}) on '{loadout.name}': {why}. "
                                  + "FloatRefusalTable's conditional UILoadoutManager row now refuses "
                                  + "to float it, and WithdrawRefusedFloat plus the release loop's "
                                  + "ModBuild 235 refusal clause take down the float that already "
                                  + "exists. NOTHING IS WRITTEN TO THE GAME — no Hide, no Escape, no "
                                  + "SetActive, no CanvasGroup — the window keeps its ordinary 2D "
                                  + "rendering on the canvas the game put it on, which the 3D map room "
                                  + "does not draw. THE ModBuild 234 DEADLOCK CANNOT COME BACK THROUGH "
                                  + "THIS ROW: that one withheld this very window while the only "
                                  + "advancing control was a child of it and the claim never lapsed. "
                                  + "This claim's second clause is exactly the absence of that state, "
                                  + "re-measured every tick with this window excluded from the answer, "
                                  + "so it cannot be true while the control is only reachable here.");
                HandOverToCharacterUI(loadout);
            }
        }
        else if (!want && _backdropStanding)
        {
            VRLog.Info(Scope, "LOADOUT BACKDROP CLAIM LAPSED on "
                              + $"'{(loadout != null ? loadout.name : "<the loadout window is gone>")}' — "
                              + (loadout == null || !loadout.IsOpen
                                  ? "the loadout screen closed"
                                  : terminator == null
                                      ? "the quest intro is running again, so the story is being told "
                                        + "in this window and it must be on screen"
                                      : _backdropLifted
                                          ? "the deadlock floor stood it down"
                                          : !somethingElseOnScreen
                                          ? "THE HONESTY CLAUSE: the mod is floating nothing this "
                                            + "claim is not withholding, so there would be nothing on "
                                            + "screen at all"
                                          : "THE CONTINUE CONTROL IS NOT DRAWING IN ANY OTHER FLOATED "
                                            + "WINDOW, so withholding this one would leave the player "
                                            + "with nothing to press — that is the safety valve and "
                                            + "not a regression")
                              + ", so this window floats again from this tick with everything on it "
                              + "(nothing was ever written to it). "
                              + $"{_backdropCycles} of {MaxBackdropCycles} cycle(s) used.");
            _backdropObject = null;
            _backdropWhy = "the loadout backdrop claim has lapsed; that window is nobody's "
                           + "responsibility but its own";
        }
        _backdropStanding = want;
        ReportBackdrop(loadout, terminator);
    }

    /// <summary>
    /// TRUE once this quest's handover has been attempted. The swap is a ONE-SHOT per
    /// point-of-no-return gate, for the same reason the backdrop claim's cycle budget is: a claim
    /// that lapses and is raised again is the SAME quest, and swapping twice would move a window the
    /// player may well have been reading for the whole interval in between.
    /// </summary>
    private static bool _handoverDone;

    /// <summary>
    /// HAND THE STORY WINDOW'S PLACE TO THE CHARACTER-UI — one shot, at the withdrawal edge.
    ///
    /// <para><b>USER REPORT (2026-08-24, testing ModBuild 241), verbatim:</b> <i>"Nachdem die Story
    /// vorbei ist verschwindet das Fenster und stattdessen kommt die Character-UI wieder (was
    /// gewollt ist) — ich hätte gerne dass sie sich an exakt der selben Stelle auswechseln. Aktuell
    /// spawnt die Character-UI noch im Halbkreis daneben — obwohl das Fenster ja bereits verschwunden
    /// ist."</i></para>
    ///
    /// <para><b>THE MECHANISM.</b> Nothing about the withdrawal changes. The Character-UI has already
    /// floated by the time this runs — it MUST have, because the backdrop claim's deadlock clause
    /// only stands while the continue control is parked inside it — and it was seated by the arc
    /// allocator at a moment when the story window still held its reservation, so the allocator did
    /// the only correct thing available to it and put the Character-UI beside it. His log:
    /// <c>:9037 'New Party display' claimed reservation 0 at 60° from the spawn gaze, world yaw
    /// 140°</c> against the story window's <c>-4°±24° (world 76°)</c> — 32° of separation, and the
    /// pre-reveal re-place at <c>:9101</c> confirms it with
    /// <c>*** OUTSIDE THE FIELD OF VIEW by 16° ***</c>. What was missing is a HANDOVER, and this is
    /// it: <c>ModalFallback.BeginWindowHandover</c> captures the story window's LIVE pose on this
    /// tick — the last tick it is still floated — and moves the Character-UI so that its DRAWN
    /// CENTRE lands on the story window's DRAWN CENTRE, transferring the arc reservation rather than
    /// releasing one and claiming another.</para>
    ///
    /// <para><b>WHY THE LIVE POSE AND NOT THE SPAWN POSE.</b> The story window is the SHARED (blue)
    /// window: <c>SharedWindowIdentity</c> gives it <c>SharedWindowKind.MapStory</c> and wire record
    /// 21 (<c>Net.RemoteMapStory</c>) syncs its pose 1:1, so a PEER may have dragged it, the local
    /// player may have dragged it, and a shared window deliberately never re-faces. Where it stands
    /// at this instant is a fact about the world; where it was placed is a fact about the packer.
    /// Only the first one is what he is looking at.</para>
    ///
    /// <para><b>WHAT IS NOT CLAIMED.</b> Not the scale — the Character-UI keeps its own legibility
    /// scale, because "dieselbe Stelle" is a place and not a size, and its scale is derived from its
    /// own fitted width. Not the rect — the two windows draw different content at different widths
    /// and never could share one. See the handover block in ArcSeats.cs for the rest, including the
    /// millimetre reason the frame origin is the wrong invariant.</para>
    /// </summary>
    private static void HandOverToCharacterUI(UIWindow loadout)
    {
        if (_handoverDone)
            return;
        _handoverDone = true;
        ModalFallback.BeginWindowHandover(loadout, CharacterWindow(),
            "the quest intro is over and the loadout backdrop claim has just been raised, so this "
            + "is the last tick the story window is still floated");
    }

    /// <summary>
    /// THE WINDOW THE MOD IS FLOATING WHOSE ID IS <c>PartyPanel</c> — the map room's Character-UI.
    ///
    /// <para>Identified by the window's own serialized <c>ID</c> over the set of windows the mod is
    /// actually floating, which is <c>LoadoutConfirmPark.CharacterWindow</c>'s identity exactly and
    /// for the reason written there: there are TWO <c>UIWindow</c> components one level apart in
    /// that hierarchy and no containment test can separate them ([[containment-is-not-identity]]).
    /// The float-set half is what makes the answer a window that HAS a host rect and a grab frame to
    /// be moved by. Null while it is not floated is the CORRECT answer, and the handover's own
    /// refusal line says what that means.</para>
    /// </summary>
    /// <para>THE LOOKUP IS SHARED with <c>LoadoutConfirmPark.CharacterWindow</c> since the 2026-09
    /// refactor (<see cref="ModalFallback.FindFloatedWindow"/>): the two were the same twenty-two
    /// lines, guard and scratch list included. The named wrapper stays because the sentences above
    /// are about the character UI, not about a <c>UIWindowID</c> value.</para>
    private static UIWindow? CharacterWindow() =>
        ModalFallback.FindFloatedWindow(UIWindowID.PartyPanel);

    /// <summary>
    /// THE ONE LINE A TESTER CAN GREP THAT IS TRUE ONLY IF THE BACKGROUND WINDOW IS ACTUALLY GONE.
    ///
    /// <para>It speaks only once <see cref="TerminatedBy"/> says the intro is over, so it is silent
    /// for the whole story phase — where the loadout screen floating is the CORRECT state and a
    /// warning about it would be the third wrong instrument this project has had to retract. Every
    /// clause is re-read this tick from the float set itself.</para>
    ///
    /// <para>GREP: <c>LOADOUT BACKGROUND WITHDRAWN: CONFIRMED</c> — the request is met.
    /// <c>… NOT ACHIEVED</c> — it is not, with the failing term named and every window still standing
    /// listed by name.</para>
    /// </summary>
    private static void ReportBackdrop(UIWindow? loadout, string? terminator)
    {
        if (loadout == null || !loadout.IsOpen || terminator == null || !MapRoomDriver.Active)
        {
            _backdropVerdict = string.Empty;   // the question is not being asked; re-arm the edge
            return;
        }
        if (_backdropReports >= MaxBackdropReports)
            return;

        int total = ModalFallback.CountFloatsOtherThan(null);
        bool loadoutFloated = FloatedByMod(loadout, total);

        // THE CHANGE GATE FIRST, THEN THE STRING. This line is asked on every tick of the whole
        // battle-goal phase, and two of the clauses below are Graphic sweeps — building the message
        // and throwing it away would be a per-frame cost for nothing [[one-line-owned-the-frame]].
        string verdict = loadoutFloated ? "NOT ACHIEVED" : "CONFIRMED";
        if (verdict == _backdropVerdict)
            return;
        _backdropVerdict = verdict;
        _backdropReports++;

        FloatScratch.Clear();
        ModalFallback.CollectFloatedWindows(FloatScratch, null);
        var sb = new System.Text.StringBuilder(96);
        for (int i = 0; i < FloatScratch.Count; i++)
        {
            UIWindow w = FloatScratch[i];
            if (w == null)
                continue;
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append('\'').Append(w.name).Append("' ID ").Append(w.ID);
        }
        string standing = sb.Length > 0 ? sb.ToString() : "none at all";
        FloatScratch.Clear();

        string measured =
            $"'UI Loadout Window' resolved as '{loadout.name}' (ID {loadout.ID}), IsOpen=True, "
            + $"floated by the mod={loadoutFloated}; this class's backdrop claim is standing="
            + $"{_backdropStanding} and FloatRefusalTable.Refuses says "
            + $"{FloatRefusalTable.Refuses(loadout)}; the intro ended because {terminator}; the "
            + "continue control is reachable somewhere that is NOT this window="
            + $"{LoadoutConfirmPark.ContinueReachableOffTheLoadout(loadout)} and "
            + $"{LoadoutConfirmPark.Where}; the honesty clause — the mod is floating "
            + $"{ModalFallback.CountFloatsOtherThan(loadout)} window(s) this claim is not withholding "
            + $"— reads {ModalFallback.CountFloatsOtherThan(loadout) > 0}; "
            + $"{_backdropCycles} of {MaxBackdropCycles} cycle(s) used, "
            + $"lifted by the deadlock floor={_backdropLifted}. EVERY WINDOW THE MOD IS STILL "
            + $"FLOATING, {total} in total: {standing}";

        if (!loadoutFloated)
        {
            // ModBuild 364 — PROMOTED FROM Info TO Alert, TEXT UNCHANGED. Its NOT ACHIEVED sibling
            // below is the single line that decides the reported near-deadlock, and a verdict line
            // whose failure prints while its clearing does not is a trap for the next reader
            // [[quiet-log-silenced-the-backlog]]. Both branches are change-gated on the verdict and
            // capped at MaxBackdropReports, so this is bounded at six lines per quest.
            // HW-VERIFY
            VRLog.Alert(Scope, "LOADOUT BACKGROUND WITHDRAWN: CONFIRMED — the window that carried only "
                              + "the backdrop picture is no longer being floated, so the player has "
                              + "one window to watch and not two. MEASURED THIS TICK: " + measured
                              + ". USER REQUEST THIS LINE ANSWERS: \"Ich möchte das letzte lokale "
                              + "Storyfenster mit nur dem Hintergrund doch nicht haben. Nachdem die "
                              + "Story erzählt wurde soll die Character-UI für die persönlichen Quests "
                              + "spawnen und das Storyfenster verschwinden.\" WHAT IT DOES NOT CLAIM: "
                              + "that the window is closed. It is not — nothing was written to the "
                              + "game. See ENTER DUNGEON ON THE CHARACTER UI for whether the control "
                              + "that used to be on it is where he asked for it instead, and LOADOUT "
                              + "CONFIRM REACHABLE for whether he can press it at all.");
            return;
        }
        // ModBuild 364 — PROMOTED FROM Warn TO Alert, TEXT UNCHANGED so every existing grep still
        // finds it. This is the line the ModBuild 362 stall printed, once, at :15879, with the
        // honesty clause reading False — and it did not print at the shipped default, so the fault
        // was only visible because that tester happened to be running at Debug.
        //
        // A WARNING ABOUT ITS OWN "READ IT LIKE THIS" CLAUSE, WHICH IS WRONG FOR THIS FAULT AND MAY
        // NOT BE RE-WORDED (check-surface.py treats a vanished token as a removal): it points the
        // reader at 'reachable somewhere that is NOT this window'=False as the likely failure. In the
        // reported stall that term read TRUE and the ONLY False term was the honesty clause. Read the
        // honesty clause first; MUTUAL HOLD MEASURED now says the same thing without the misdirection.
        // HW-VERIFY
        VRLog.Alert(Scope, "LOADOUT BACKGROUND WITHDRAWN: NOT ACHIEVED — the story is told and the "
                          + "background window is still on screen. MEASURED THIS TICK: " + measured
                          + ". READ IT LIKE THIS: 'reachable somewhere that is NOT this window'=False "
                          + "is the SAFE failure and the most likely one — the continue control is not "
                          + "drawing in any other floated window, so this claim deliberately does not "
                          + "stand and the player keeps the button he can press. Look at ENTER DUNGEON "
                          + "ON THE CHARACTER UI for why the park did not land, and at whether 'New "
                          + "Party display' is in the list of standing windows above; if it is not "
                          + "floated there is nothing to park into and that is the term to fix. "
                          + "claim standing=True with floated=True instead means the refusal is being "
                          + "asked and something is not listening — grep FLOAT WITHDRAWN and FLOAT "
                          + "RELEASED ON REFUSAL for this window's name.");
    }

    // ---- the story curtain (ModBuild 234) ------------------------------------------------------

    /// <summary>
    /// THE FROZEN SET. Every window the mod was floating at the curtain's rising edge, minus the
    /// story box and minus the loadout screen — the two halves of the composite, of which exactly one
    /// is the host and the other has a claim of its own (<see cref="HoldsStoryFloatBack"/>). Filled
    /// ONCE, in <see cref="RaiseCurtain"/>,
    /// and nothing appends to it afterwards — that is the entire difference between this exclusion
    /// and ModBuild 231's, and it is a property of the code rather than of a test that could be got
    /// wrong. See the class doc, SECTION 5.
    /// </summary>
    private static readonly List<UIWindow> CurtainMembers = new(8);

    private static bool _curtainStanding;
    private static bool _curtainLifted;
    private static int _curtainCycles;
    private static bool _curtainCapReported;
    private static string _curtainNames = "none";
    private static string _curtainWhy =
        "the quest-intro story curtain has never been raised in this session";

    /// <summary>
    /// How many times the curtain may be raised before it stops asking, and the number is DERIVED
    /// from the same fuse as <see cref="MaxWithdrawCycles"/> rather than chosen.
    /// <c>ModalFallback.10.CatchAll.cs</c> counts one churn tick per enrolment of a window the mod
    /// is not already floating and session-suppresses the NAME on the fourth inside 60 s; a REFUSED
    /// window is never counted; so the only counted events in a member's life are its FIRST float
    /// and one re-float per curtain that FALLS. 1 + 2 = 3 is at the limit and never over it.
    /// </summary>
    private const int MaxCurtainCycles = 2;

    /// <summary>
    /// How long the curtain may stand on the BRIDGE alone — i.e. with neither the game's own
    /// hide-other-UI level nor the loadout screen holding it up. It exists for exactly one seam,
    /// and the hardware log measures that seam: the quest-start message chain ends at :15159
    /// (<c>[AREA MANAGER] Set Focused Area WorldMap</c>) and the loadout window is shown 28 lines
    /// later at :15188. Twenty seconds is two orders of magnitude of headroom over that and is a
    /// HARD CEILING rather than a timeout to tune: past it the curtain lapses, every member floats
    /// again, and the fail direction is a quest log that came back a few seconds early — never a
    /// window that cannot come back.
    /// </summary>
    private const float CurtainBridgeSeconds = 20f;

    private static float _curtainHeldAt;

    /// <summary>
    /// IS THE CURTAIN HOLDING THIS WINDOW OUT OF THE FLOAT SET RIGHT NOW? The question
    /// <c>FloatRefusalTable</c> must ask, and — like <see cref="HoldsStoryFloatBack"/> — a PURE
    /// read with no state and no logging, because that table's verdict is re-entered several times
    /// per tick from a recursive ancestor walk and every caller must get the same answer.
    ///
    /// <para>The membership test is by REFERENCE against a set that was frozen at the edge, so this
    /// method cannot refuse a window that opened afterwards however the level flaps.</para>
    /// </summary>
    internal static bool CurtainRefuses(UIWindow? window)
    {
        if (!_curtainStanding || _curtainLifted || window == null)
            return false;
        for (int i = 0; i < CurtainMembers.Count; i++)
        {
            if (ReferenceEquals(CurtainMembers[i], window))
                return true;
        }
        return false;
    }

    /// <summary>The curtain's own words for what it did, printed verbatim by the refusal line and by
    /// the lapse warning. Never null.</summary>
    internal static string CurtainWhy => _curtainWhy;

    /// <summary>
    /// Recompute the curtain LEVEL and print one line per EDGE.
    ///
    /// <para><b>THE HONESTY CLAUSE IS THE FIRST BOUND AND IT IS NOT OPTIONAL.</b> The curtain may
    /// only stand while the mod is actually FLOATING the story box or the loadout screen — i.e.
    /// while there is something on screen for the player to read. Take that away and the curtain
    /// would be hiding every window in the room and showing nothing in their place, which is
    /// ModBuild 231's ending exactly. [[parent-wins-needs-a-real-parent]]: suppressing X because Y
    /// is showing the player something requires Y to ACTUALLY be showing it.</para>
    ///
    /// <para><b>THE HOLD CONDITION IS DELIBERATELY WIDER THAN THE EDGE CONDITION.</b> The edge is
    /// the game's own <see cref="StoryHidesOtherUI"/>; the hold is that OR the gate being open OR
    /// the loadout screen being up. That is what carries the curtain across the seam between the
    /// quest-start message chain and the loadout screen — the log's own gap between :15159 (the
    /// chain ends, focus returns to WorldMap) and :15188 (the loadout window is shown) — without
    /// letting go and re-taking, which would cost two edges and two churn counts per member.</para>
    /// </summary>
    private static void TickCurtain(UIWindow? story, UIWindow? loadout)
    {
        int total = ModalFallback.CountFloatsOtherThan(null);
        bool onScreen = (story != null && FloatedByMod(story, total))
                        || (loadout != null && FloatedByMod(loadout, total));
        bool gameLevel = StoryHidesOtherUI(story);

        // THE SEAM BRIDGE. Its clock is reset by the two REAL reasons to hold, never by the curtain
        // itself — the hold below must not consult _gateOpen, because the gate's own level is the
        // union of the curtain and the loadout screen and a curtain that held "because the gate is
        // open" would hold the gate open forever. A latch that is its own reason to latch is not a
        // level at all.
        bool realReason = gameLevel || LoadoutScreenOpen;
        if (realReason)
            _curtainHeldAt = Time.unscaledTime;

        if (_curtainStanding)
        {
            bool bridging = Time.unscaledTime - _curtainHeldAt <= CurtainBridgeSeconds;
            // ModBuild 235 — THE HOLD'S HONESTY CLAUSE IS WIDER THAN THE RAISE'S, AND IT HAS TO BE.
            //
            // The RAISE asks for the story box or the loadout screen because those are the two things
            // the curtain is raised FOR. The HOLD must ask the weaker, honest question — "is there
            // anything on screen that this curtain is not withholding?" — because the interval it
            // covers outlives both of them: the story box closes when the player clicks through the
            // intro, and the loadout screen's own float is withheld by a DIFFERENT claim
            // (HoldsStoryFloatBack — and up to ModBuild 235 that claim was about the loadout
            // screen instead) for part of the same interval. Under the old clause the curtain
            // fell in the seam between the two and the quest log came back for the rest of the
            // pre-scenario phase, which is exactly the window the user asked for it to be gone in.
            //
            // IT IS STILL AN HONESTY CLAUSE AND STILL THE SAME PROMISE: the curtain can never be the
            // reason the room is empty, because the thing it counts is floated windows it is NOT
            // responsible for. In the reported run that set is the Character-UI, which is where the
            // continue button now lives.
            bool somethingLeft = onScreen || AnyNonMemberFloated();
            bool hold = MapRoomDriver.Active && somethingLeft && (realReason || bridging);
            if (!hold)
                CloseCurtain(!MapRoomDriver.Active ? "the 3D map room stood down"
                             : !somethingLeft ? "the mod is floating nothing this curtain is not "
                                                + "withholding, so there would be nothing on screen "
                                                + "to be alone with"
                             : $"the game stopped hiding its own UI for this message and no loadout "
                               + $"screen followed within {CurtainBridgeSeconds:F0} s, so this was not "
                               + "a quest start after all");
            return;
        }

        if (!gameLevel || !MapRoomDriver.Active)
            return;
        if (!onScreen)
            return;   // the honesty clause: never hide everything and show nothing
        if (_curtainCycles >= MaxCurtainCycles)
        {
            if (!_curtainCapReported)
            {
                _curtainCapReported = true;
                VRLog.Warn(Scope, $"STORY CURTAIN CAPPED — it has already been raised {_curtainCycles} "
                                  + "time(s) since the last point-of-no-return gate closed and it will "
                                  + "not be raised again until one does. THE CAP IS DERIVED, NOT CHOSEN: "
                                  + "ModalFallback's catch-all churn fuse suppresses a window's NAME for "
                                  + "the whole session on its 4th enrolment inside 60 s, a REFUSED window "
                                  + "is never counted, and each curtain that FALLS costs its members "
                                  + $"exactly one re-float — so {MaxCurtainCycles} cycles plus the first "
                                  + "float is 3, at the fuse's limit and never over it. FROM HERE EVERY "
                                  + "WINDOW FLOATS NORMALLY, which is the ModBuild 233 presentation: the "
                                  + "quest log and the map's other panels stand beside the story box. Ugly, "
                                  + "and NOT a deadlock. IF YOU ARE READING THIS IN A HARDWARE LOG the "
                                  + "curtain is flapping — grep STORY CURTAIN LAPSED for what kept "
                                  + "dropping it.");
            }
            return;
        }
        RaiseCurtain(story, loadout);
    }

    /// <summary>
    /// Freeze the member set and raise the curtain. ONE line, at the edge, naming every member.
    /// </summary>
    private static void RaiseCurtain(UIWindow? story, UIWindow? loadout)
    {
        CurtainMembers.Clear();
        ModalFallback.CollectFloatedWindows(CurtainMembers, story);
        // The loadout screen is NOT a curtain member: it has a claim of its own that knows when the
        // composite is drawing its illustration and when it must have its screen back. Two claims on
        // one window would be two writers of one decision — [[a-remedy-knows-one-writer]].
        for (int i = CurtainMembers.Count - 1; i >= 0; i--)
        {
            if (loadout != null && ReferenceEquals(CurtainMembers[i], loadout))
                CurtainMembers.RemoveAt(i);
        }

        var sb = new System.Text.StringBuilder(96);
        for (int i = 0; i < CurtainMembers.Count; i++)
        {
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append('\'').Append(CurtainMembers[i].name).Append("' (permanent map-room window: ")
              .Append(ModalFallback.IsMapRoomPermanent(CurtainMembers[i])).Append(')');
        }
        _curtainNames = sb.Length > 0 ? sb.ToString() : "none";
        _curtainCycles++;
        _curtainStanding = true;
        _curtainLifted = false;
        _curtainWhy = $"the quest-start story message owns the screen — the game itself has hidden "
                      + "every one of MapStoryController's own elementToHide objects for it "
                      + "(isVisibleOtherUI is false), and this window is one of the "
                      + $"{CurtainMembers.Count} the mod was floating at that instant. It is not "
                      + "closed, hidden or written to in any way; its float is withheld until the "
                      + "pre-scenario interval is over, and then it comes back with everything on it";

        VRLog.Info(Scope, $"STORY CURTAIN RAISED (cycle {_curtainCycles} of {MaxCurtainCycles}) — the "
                          + "game has hidden the rest of its own UI for this story message "
                          + "(MapStoryController.isVisibleOtherUI is false, set by "
                          + "ShowOtherGUI(!message.HideOtherGUI) at MapStoryController.cs:144-165), so "
                          + "the mod withdraws the floats that are the VR equivalent of the UI it just "
                          + $"took away. FROZEN MEMBER SET, {CurtainMembers.Count} window(s): "
                          + $"[{_curtainNames}]. THE SET IS TAKEN ONCE AND NEVER APPENDED TO — the "
                          + "battle-goal picker, the party display, the loadout screen and the quest "
                          + "popup the loadout re-shows all open AFTER this instant and are therefore "
                          + "out of scope BY CONSTRUCTION, which is the one thing ModBuild 231's "
                          + "level-triggered version of this rule could not promise. NOTHING IS WRITTEN "
                          + "TO THE GAME: no Hide, no Escape, no SetActive, no CanvasGroup — this is a "
                          + "FloatRefusalTable refusal (asked at the top of the catch-all loop, so the "
                          + "churn fuse never counts it) and WithdrawRefusedFloat takes down the floats "
                          + "that already exist. THE QUEST LOG IS IN THE SET ON PURPOSE and that is a "
                          + "NARROW REVERSAL of the permanence ruling FOR THIS INTERVAL ONLY — at every "
                          + "other moment it still has no X, is still refused by CloseFloatedWindow and "
                          + "is still skipped by the escape chord. USER RULING: \"Ich möchte aber das zu "
                          + "diesem Zeitpunkt alle anderen Fenster verschwinden und nur dieses Fenster "
                          + "sichtbar ist (Point of no return überschritten).\" IF THE MEMBERS ARE STILL "
                          + "ON SCREEN AFTER THIS LINE, THE TABLE IS ASKING AND THE RELEASE LOOP IS NOT "
                          + "LISTENING: ModBuild 234 shipped the ROW 0 hook and the quest log stayed up "
                          + "anyway, because a float already standing is kept alive by WindowPanel.Sticky "
                          + "regardless of OpenWindows, and a window the game has already hidden is no "
                          + "longer in the catch-all's UnknownShown to be asked about at all. The fix is "
                          + "one clause in ModalFallback's release keep-alive (see this class's SECTION 5 "
                          + "and .planning/debug/modalfallback-release-honours-refusal.diff) — see STORY "
                          + "CURTAIN ONLY WINDOW for the measurement either way.");
    }

    /// <summary>Drop the curtain: the members float again from the next tick, with everything on
    /// them. ONE line per edge, and the count is kept.</summary>
    private static void CloseCurtain(string why)
    {
        if (!_curtainStanding)
            return;
        _curtainStanding = false;
        VRLog.Info(Scope, $"STORY CURTAIN LAPSED — {why}. The {CurtainMembers.Count} member(s) "
                          + $"[{_curtainNames}] float again from this tick with everything on them; "
                          + "the quest log returns to being the map room's permanent, X-less window and "
                          + "the permanence ruling governs it again in full. NOTHING HAS TO BE UNDONE, "
                          + "because nothing was written: the curtain only ever withheld a float. "
                          + $"{_curtainCycles} of {MaxCurtainCycles} cycle(s) used"
                          + (_curtainLifted ? ", and the deadlock floor had already lifted this one — "
                                              + "see MODAL DEADLOCK FLOOR above" : string.Empty) + ".");
        CurtainMembers.Clear();
        _curtainNames = "none";
        _curtainWhy = "the quest-intro story curtain has lapsed; every window is nobody's "
                      + "responsibility but its own";
    }

    /// <summary>
    /// Is the mod floating at least one window this curtain is NOT withholding? The honesty clause's
    /// input while the curtain stands (see <see cref="TickCurtain"/>), and the only question that
    /// answers "does the player still have something on screen" without asking about a specific
    /// window the interval is allowed to outlive.
    ///
    /// <para>Measured from the float set every time, never from curtain state: a curtain that
    /// believes it is standing is not evidence that anything is or is not on screen
    /// ([[measure-the-picture-not-the-state]]).</para>
    /// </summary>
    private static bool AnyNonMemberFloated()
    {
        FloatScratch.Clear();
        ModalFallback.CollectFloatedWindows(FloatScratch, null);
        bool any = false;
        for (int i = 0; i < FloatScratch.Count && !any; i++)
            any = !CurtainRefuses(FloatScratch[i]);
        FloatScratch.Clear();
        return any;
    }

    /// <summary>How many curtain members the GAME still has open, i.e. how many windows would float
    /// right now if the curtain were not standing. The floor's input, and nothing else reads it.</summary>
    private static int CurtainWithheldNow()
    {
        if (!_curtainStanding || _curtainLifted)
            return 0;
        int n = 0;
        for (int i = 0; i < CurtainMembers.Count; i++)
        {
            UIWindow m = CurtainMembers[i];
            if (m != null && m.IsOpen)
                n++;
        }
        return n;
    }

    // ---- the falsifier ---------------------------------------------------------------------

    /// <summary>
    /// THE ONE LINE A TESTER CAN GREP THAT IS TRUE ONLY IF THE USER'S COMPLAINT IS FIXED.
    ///
    /// <para><b>WHY IT EXISTS.</b> ModBuild 231 and ModBuild 232 both reported this feature working
    /// while the hardware showed it broken — 232's own <c>STORY COMPOSITE BUILT</c> line contains the
    /// string <c>'Image' 0x0 px</c> and still ends with "are now ONE window"
    /// ([[judge-the-render-against-the-complaint]], [[an-instrument-can-assert-a-cause]]). So this
    /// line asserts nothing. Every clause is read back this tick from the object the player is
    /// looking at, and the NOT ACHIEVED form prints the SAME measurements so a failure is as
    /// legible as a success.</para>
    ///
    /// <para><b>WHAT IT DOES NOT CLAIM, SAID OUT LOUD.</b> It does not say the room contains exactly
    /// one floated window: the map room's permanent quest log has no X and is floated by an earlier
    /// user ruling (<c>ModalFallback.IsMapRoomPermanent</c>), and it is visible on the right-hand
    /// side of story_getrennt3.jpg. The claim is about THE TWO HALVES OF THE QUEST INTRO — the story
    /// box and the loadout screen — of which exactly one may be floated. The total is printed beside
    /// it so the reader can see the difference rather than infer it
    /// ([[one-contributor-is-not-the-union]]).</para>
    ///
    /// <para>GREP: <c>STORY COMPOSITE ONE WINDOW: CONFIRMED</c> — the fix.
    /// <c>STORY COMPOSITE ONE WINDOW: NOT ACHIEVED</c> — the complaint, with the reason.</para>
    /// </summary>
    private static void ReportOneWindow(UIWindow? story, UIWindow? loadout)
    {
        if (loadout == null || !loadout.IsOpen)
        {
            _oneWindowVerdict = string.Empty;
            _oneWindowReports = 0;
            return;
        }
        if (story == null && _dock == null)
            return;   // no intro composite is being attempted this tick; nothing to falsify

        int total = ModalFallback.CountFloatsOtherThan(null);
        bool storyFloated = story != null && FloatedByMod(story, total);
        bool loadoutFloated = FloatedByMod(loadout, total);
        Vector2 size = _dock != null ? _dock.rect.size : Vector2.zero;
        Vector2 at = _dock != null ? _dock.anchoredPosition : Vector2.zero;
        bool active = _dock != null && _dock.gameObject.activeInHierarchy;
        bool sized = size.x >= MinParkSizePx && size.y >= MinParkSizePx;
        bool underHost = _dock != null && _parkHost != null && _dock.parent != null
                         && ReferenceEquals(_dock.parent, _parkHost.transform);
        int hostLayer = loadout.gameObject.layer;
        int dockLayer = _dock != null ? _dock.gameObject.layer : -1;

        // ModBuild 236 — THE HOST IS THE LOADOUT SCREEN AND THE REFUSED HALF IS THE STORY BOX. The
        // verdict is the exact mirror of the ModBuild 233-235 one, and it is measured, not asserted.
        bool ok = loadoutFloated && !storyFloated && _dock != null && sized && active && underHost
                  && Moved.Count > 0 && dockLayer == hostLayer;

        string measured =
            $"HOST (the composed window) '{loadout.name}' floated={loadoutFloated}; REFUSED half "
            + $"'{(story != null ? story.name : "<none>")}' floated={storyFloated}; the parked dialog "
            + $"is '{(_dock != null ? _dock.name : "<nothing parked>")}' holding {Moved.Count} root "
            + $"child object(s) of the story window, rect {size.x:F0}x{size.y:F0} px at anchored "
            + $"({at.x:F0},{at.y:F0}) in the host's own authored px, activeInHierarchy={active}, "
            + $"parented to the host={underHost}, layer {dockLayer} against the host root's own layer "
            + $"{hostLayer} ({Layers.Written} written over {Layers.Count} transform(s), {Layers.Skipped} "
            + $"foreign render subtree(s) skipped whole); the quest illustration stays where the game "
            + $"put it: '{(_picture != null ? _picture.name : "<none>")}' sprite "
            + $"'{_parkedSprite}'; the mod is floating {total} window(s) in total (the map room's "
            + "permanent quest log is one of them and is not part of this claim)";

        string verdict = ok ? "CONFIRMED" : "NOT ACHIEVED";
        if (verdict == _oneWindowVerdict || _oneWindowReports >= MaxOneWindowReports)
            return;
        _oneWindowVerdict = verdict;
        _oneWindowReports++;

        if (ok)
        {
            VRLog.Info(Scope, "STORY COMPOSITE ONE WINDOW: CONFIRMED — the quest intro is ONE floated "
                              + "window and it is THE LOADOUT SCREEN, i.e. the window that is still "
                              + "standing when the continue control appears. MEASURED THIS TICK: "
                              + measured + ". USER REPORT THIS LINE ANSWERS: \"Es soll immer noch das "
                              + "exakt gleiche Multiplayer-Fenster sein wo auch die Story drin erzählt "
                              + "wurde. Da der Dialog fertig ist soll er darin verschwinden, aber es soll "
                              + "DAS SELBE Fenster sein.\" See STORY WINDOW CONTINUITY for whether that "
                              + "window is still the same panel, in the same pose, when the button "
                              + "arrives.");
            return;
        }
        VRLog.Warn(Scope, "STORY COMPOSITE ONE WINDOW: NOT ACHIEVED — the quest intro is not one window "
                          + "yet. MEASURED THIS TICK: " + measured + ". READ IT LIKE THIS: REFUSED half "
                          + "floated=True means FloatRefusalTable's MapStoryController row did not fire — "
                          + "either MapStoryController is not on the story window's own GameObject on "
                          + "this build (the ModBuild 235 identity line says it is) or "
                          + "StoryComposite.HoldsStoryFloatBack returned false, and the CLAIM lines above "
                          + "say which. HOST floated=False means the loadout screen itself is not on "
                          + "screen and nothing should have been parked into it. A dock layer that "
                          + "differs from the host root's layer means the dialog is being drawn by the "
                          + "wrong camera or twice — per-window capture cameras cull BY LAYER. "
                          + "0 root child object(s) means the park moved nothing at all.");
    }

    // ---- ModBuild 234's two falsifiers ---------------------------------------------------------

    /// <summary>Above this the picture is not "directly under" anything, in real millimetres at the
    /// live rig scale. The intent is <see cref="ImageGapPx"/> = 24 authored px, which on the
    /// composite's committed host works out at roughly 17-25 mm; 40 mm is comfortably above that and
    /// two orders of magnitude below what ModBuild 233 shipped (786-888 authored px, i.e. more than
    /// half a metre of black on the panel). A threshold that only a genuinely fixed build can pass,
    /// and that a build with the old basis cannot pass by accident.</summary>
    private const float MaxGapMillimetres = 40f;

    private static readonly List<UIWindow> FloatScratch = new(8);
    private static string _onlyWindowVerdict = string.Empty;
    private static int _onlyWindowReports;
    private static string _gapVerdict = string.Empty;
    private static int _gapReports;

    /// <summary>
    /// FALSIFIER 1 — IS THE STORY BOX THE ONLY FLOATED WINDOW IN THE ROOM RIGHT NOW?
    ///
    /// <para>This is the clause <see cref="ReportOneWindow"/> explicitly does NOT make: that line is
    /// about the two halves of the quest intro and says so, and it prints the total beside its
    /// verdict precisely so a reader can see the difference rather than infer it. The user's ModBuild
    /// 233 ruling asks the wider question — <i>"alle anderen Fenster verschwinden und nur dieses
    /// Fenster sichtbar ist"</i> — so it gets its own line, its own grep string and its own
    /// measurement, taken this tick from the float set itself and NOT from the curtain's own state
    /// (a curtain that believes it is standing is not evidence that anything vanished).</para>
    ///
    /// <para>GREP: <c>STORY CURTAIN ONLY WINDOW: CONFIRMED</c> — the fix.
    /// <c>STORY CURTAIN ONLY WINDOW: NOT ACHIEVED</c> — the complaint, with every window that is
    /// still standing named.</para>
    /// </summary>
    private static void ReportOnlyWindow(UIWindow? host)
    {
        if (!_curtainStanding)
        {
            _onlyWindowVerdict = string.Empty;
            _onlyWindowReports = 0;
            return;
        }
        FloatScratch.Clear();
        int others = ModalFallback.CollectFloatedWindows(FloatScratch, host);
        bool storyFloated = host != null && FloatedByMod(host);
        bool ok = storyFloated && others == 0;

        var sb = new System.Text.StringBuilder(96);
        for (int i = 0; i < FloatScratch.Count; i++)
        {
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(FloatScratch[i].name).Append("' (a curtain member: ")
              .Append(CurtainRefuses(FloatScratch[i])).Append(')');
        }
        string standing = sb.Length > 0 ? sb.ToString() : "none";
        FloatScratch.Clear();

        string verdict = ok ? "CONFIRMED" : "NOT ACHIEVED";
        if (verdict == _onlyWindowVerdict || _onlyWindowReports >= MaxOneWindowReports)
            return;
        _onlyWindowVerdict = verdict;
        _onlyWindowReports++;

        string measured = $"the composed host '{(host != null ? host.name : "<none>")}' floated="
                          + $"{storyFloated} (ModBuild 236: while the composite stands that host is the "
                          + "LOADOUT window with the story dialog parked inside it. ModBuild 238: once "
                          + "the intro is over and the backdrop claim stands it is the CHARACTER-UI, "
                          + "which is where the continue control has been parked; outside both it is "
                          + "whichever of the two intro halves is up); OTHER floated "
                          + $"window(s): {others} [{standing}]; the curtain's frozen member set is "
                          + $"[{_curtainNames}]";
        if (ok)
        {
            VRLog.Info(Scope, "STORY CURTAIN ONLY WINDOW: CONFIRMED — the composed host is the ONLY "
                              + "window the mod is floating. MEASURED THIS TICK: " + measured + ". USER RULING "
                              + "THIS LINE ANSWERS: \"Ich möchte aber das zu diesem Zeitpunkt alle "
                              + "anderen Fenster verschwinden und nur dieses Fenster sichtbar ist (Point "
                              + "of no return überschritten).\" Nothing was written to the game to "
                              + "achieve it: the members' floats are withheld and they come back, with "
                              + "everything on them, when the curtain lapses.");
            return;
        }
        VRLog.Warn(Scope, "STORY CURTAIN ONLY WINDOW: NOT ACHIEVED — something other than the composed "
                          + "host is still floating past the point of no return. MEASURED THIS TICK: "
                          + measured + ". READ IT LIKE THIS: a window listed with 'a curtain member: "
                          + "True' means CurtainRefuses says NO FLOAT and the window is floating anyway. "
                          + "ModBuild 234 read that as 'FloatRefusalTable is not asking CurtainRefuses' "
                          + "and that was WRONG — the hook shipped and it is asked. The real cause is "
                          + "one line further down: ModalFallback's release keep-alive reads "
                          + "'in OpenWindows || wp.Sticky', so a float that already exists survives a "
                          + "refusal that only removes it from OpenWindows; and a window the game has "
                          + "already hidden (the quest log, hidden 684 log lines before the curtain "
                          + "rose) has left the catch-all's UnknownShown, so it is never asked at all "
                          + "and never even prints a FLOAT REFUSED line. The change is one clause in "
                          + "that keep-alive — see .planning/debug/modalfallback-release-honours-"
                          + "refusal.diff. 'a curtain member: False' means the window opened "
                          + "AFTER the curtain's edge and is out of scope by construction, which is "
                          + "deliberate (it is what stops this rule from eating the loadout sequence the "
                          + "way ModBuild 231 did) — if such a window must also go, it has to go at the "
                          + "edge, not by widening the set afterwards. A composed host with "
                          + "floated=False means the host itself is not on screen and the curtain "
                          + "should already have lapsed.");
    }

    /// <summary>
    /// FALSIFIER 2 — IS THE PICTURE ACTUALLY DIRECTLY ABOVE THE DIALOG?
    ///
    /// <para>Both edges are re-measured HERE, from the live objects, rather than carried over from
    /// <see cref="ApplyPose"/>. That is the whole point: a number the placement computed and then
    /// reported back would agree with every broken build, which is the mistake ModBuild 232's own
    /// BUILT line made when it printed <c>'Image' 0x0 px</c> and still ended "are now ONE window"
    /// ([[an-instrument-can-assert-a-cause]]). The gap is converted to real millimetres at the live
    /// rig scale, because "24 px" means nothing to a reader and half a metre of black is what the
    /// user actually saw.</para>
    ///
    /// <para><b>ModBuild 236 — IT POINTS THE OTHER WAY AND MEASURES THE SAME TWO EDGES.</b> The
    /// picture is no longer moved, so its painted bottom is read off the rect the GAME laid out
    /// inside the host; the dialog's painted top is read off the DOCK. Both are still unioned in the
    /// HOST's own authored px with the conversion's own fit verdict, so the placement and the host
    /// fit still cannot disagree about what is drawn.</para>
    ///
    /// <para>GREP: <c>STORY COMPOSITE GAP: CONFIRMED</c> — the fix.
    /// <c>STORY COMPOSITE GAP: NOT ACHIEVED</c> — the complaint, with the measured millimetres.</para>
    /// </summary>
    private static void ReportGap()
    {
        if (_dock == null || _parkHost == null || _picture == null)
        {
            _gapVerdict = string.Empty;
            _gapReports = 0;
            return;
        }
        // Past the report cap this line can say nothing more, so it stops MEASURING as well — two
        // subtree walks per tick for a verdict that will not be printed is the kind of instrument
        // cost that gets a whole sweep blamed for a frame later on.
        if (_gapReports >= MaxOneWindowReports)
            return;
        var win = _parkHost.transform as RectTransform;
        if (win == null)
            return;
        RectTransform dialog = _dock;
        if (!dialog.gameObject.activeInHierarchy)
            return;

        ConvertedPanel? panel = ModalFallback.PanelFor(_parkHost);
        if (!TryPaintedBounds(dialog, win, panel, out Rect ink, out int nDialog) || nDialog == 0)
            return;
        if (!TryPaintedBounds(_picture, win, panel, out Rect pic, out int nPic) || nPic == 0)
            return;

        float gapPx = pic.yMin - ink.yMax;
        float rig = RigScale();
        float mm = Mathf.Abs(gapPx) * Mathf.Abs(win.lossyScale.y) / Mathf.Max(rig, 1e-4f) * 1000f;
        bool scaleKnown = rig > 1e-3f && Mathf.Abs(win.lossyScale.y) > 1e-6f;
        bool ok = gapPx >= -OffsetEpsilonPx
                  && (scaleKnown ? mm <= MaxGapMillimetres
                                 : Mathf.Abs(gapPx) <= ImageGapPx * 3f);

        string verdict = ok ? "CONFIRMED" : "NOT ACHIEVED";
        if (verdict == _gapVerdict || _gapReports >= MaxOneWindowReports)
            return;
        _gapVerdict = verdict;
        _gapReports++;

        string measured =
            $"picture '{_picture.name}' painted bottom edge at y={pic.yMin:F0}, dialog "
            + $"'{dialog.name}' (holding {Moved.Count} root child object(s) of "
            + $"'{(_parkSource != null ? _parkSource.name : "<the story window is gone>")}') "
            + $"painted top edge at y={ink.yMax:F0} (both in the HOST loadout window's own "
            + $"authored px, both unioned this tick from {nPic} and {nDialog} drawn graphic(s) that "
            + "passed the CONVERSION'S OWN fit visibility verdict) ⇒ GAP "
            + $"{gapPx:F0} authored px = {(scaleKnown ? $"{mm:F0} mm" : "n/a mm (no rig scale)")} at "
            + $"the live rig scale (rig {rig:F1} world units per tracking metre, host "
            + $"{Mathf.Abs(win.lossyScale.y):F4} world units per authored px), against a bound of "
            + $"{MaxGapMillimetres:F0} mm. INTENT: {ImageGapPx:F0} px. PLACED AGAINST: {_poseBasis}. "
            + $"Painted dialog strip {ink.width:F0}x{ink.height:F0} px; painted picture "
            + $"{pic.width:F0}x{pic.height:F0} px";

        if (ok)
        {
            VRLog.Info(Scope, "STORY COMPOSITE GAP: CONFIRMED — the dialog is directly under the "
                              + "picture. MEASURED THIS TICK: " + measured + ". USER REPORT THIS LINE "
                              + "ANSWERS: \"allerdings ist der Abstand zwischen dem Bild und dem Dialog "
                              + "viel zu groß, das Dialogfenster soll direkt darunter angezeigt "
                              + "werden.\" FOR SCALE: ModBuild 233's own fit line measured this gap at "
                              + "888 authored px (:15304) and 786 px one tick later (:15323) — more "
                              + "than half a metre on the panel — because it placed the picture against "
                              + "the dialog HOST's rect instead of the dialog's ink.");
            return;
        }
        VRLog.Warn(Scope, "STORY COMPOSITE GAP: NOT ACHIEVED — the dialog is not directly under the "
                          + "picture. MEASURED THIS TICK: " + measured + ". READ IT LIKE THIS: a gap of "
                          + "several hundred authored px means the placement is running on the "
                          + "AUTHORED-RECT fallback — the 'PLACED AGAINST' clause says which basis ran — "
                          + "i.e. no graphic under the quest illustration passed the fit's visibility "
                          + "verdict, so there was no ink to place against. A NEGATIVE gap means the "
                          + "dialog is overlapping the picture and ImageGapPx is being applied against "
                          + "an ink offset this method measured differently from ApplyPose. A gap near "
                          + "the intent in px but over the bound in mm means the panel is simply drawn "
                          + "much larger than ModBuild 235's — read the host scale in the line above "
                          + "before changing ImageGapPx.");
    }

    // ---- ModBuild 236's falsifier: the window never respawned and never moved -------------------

    /// <summary>Set for the rest of the gate when the deadlock floor's control arm trips. While it is
    /// true this class composes NOTHING: the dialog goes home, the story window floats on its own and
    /// the loadout screen is left entirely alone. "Lift the verdict, keep the count."</summary>
    private static bool _floorLifted;

    private static int _contPanelId;
    private static Vector3 _contPos;
    private static Quaternion _contRot = Quaternion.identity;
    private static bool _contHave;
    private static bool _contGrabbed;
    private static string _contVerdict = string.Empty;
    private static int _contReports;

    /// <summary>
    /// FALSIFIER 3 (ModBuild 236) — IS THE WINDOW THE BUTTON APPEARS ON THE SAME PANEL, IN THE SAME
    /// PLACE, AS THE WINDOW THE STORY WAS TOLD IN?
    ///
    /// <para>This is the user's sentence turned into two numbers taken at two instants and compared.
    /// AT THE FIRST DIALOG PAGE (the tick the composite first stands) it records the HOST PANEL's
    /// GameObject instance id and its world pose. AT THE MOMENT THE CONTINUE CONTROL BECOMES
    /// REACHABLE — the game's own <c>UILoadoutManager.CanShowConfirmationButton()</c> plus its own
    /// switch on the control, read through <see cref="LoadoutConfirmPark.GameWantsConfirmShown"/> —
    /// it reads the same two values again. A DIFFERENT instance id is a window that respawned; a
    /// different pose is a window that moved. Either one is the reported defect.</para>
    ///
    /// <para><b>THE PLAYER'S OWN HAND IS THE ONE LEGITIMATE EXCEPTION</b> and it is measured, not
    /// assumed: <see cref="_contGrabbed"/> latches on any tick the host's own grab handle reports a
    /// hand on it, and the verdict then reports the pose change without calling it a break. A window
    /// the player has grabbed is his forever (ModBuild 183) and moving it back would be the worse
    /// bug.</para>
    ///
    /// <para><b>ModBuild 238 — THE SECOND READ MOVED, AND SAYING WHY IS THE POINT.</b> Up to
    /// ModBuild 237 it was taken when the GAME asked for a continue control, because that control was
    /// on this window. The user has since retracted that arrangement — <i>"Ich möchte das letzte
    /// lokale Storyfenster mit nur dem Hintergrund doch nicht haben"</i> — and this window is now
    /// WITHDRAWN before that moment arrives, so the old trigger would find no panel, return early and
    /// say nothing at all. An instrument that quietly stops answering is the thing nobody can
    /// diagnose. The read is therefore taken at <see cref="TerminatedBy"/> instead: the instant the
    /// game says the intro is over, which is strictly earlier and is the last instant at which the
    /// question still has a subject. WHAT IT MEASURES IS NOW EXACTLY THE HALF OF THE USER'S SENTENCE
    /// THAT SURVIVES — that the story was told, start to finish, in ONE panel that never respawned
    /// and never re-placed itself. Where the BUTTON ends up is a different question and it has its own
    /// line, <c>ENTER DUNGEON ON THE CHARACTER UI</c>.</para>
    ///
    /// <para>GREP: <c>STORY WINDOW CONTINUITY: CONFIRMED</c> — the fix.
    /// <c>STORY WINDOW CONTINUITY: BROKEN</c> — "ein ganz neues Fenster" ("a completely new
    /// window"), with both ids and both poses.</para>
    /// </summary>
    private static void ReportContinuity(UIWindow? loadout, string? terminator)
    {
        if (loadout == null || !loadout.IsOpen)
        {
            _contHave = false;
            _contGrabbed = false;
            _contVerdict = string.Empty;
            _contReports = 0;
            return;
        }

        ConvertedPanel? panel = ModalFallback.PanelFor(loadout);
        GameObject? hostGo = panel != null ? panel.HostGo : null;
        if (hostGo == null)
            return;

        // A HAND ON THE WINDOW IS RECORDED WHENEVER IT HAPPENS, not only at the two instants.
        if (ModalFallback.TryGetGrabFor(loadout, out GrabbableModal? grab) && grab != null
            && grab.IsGrabbed)
            _contGrabbed = true;

        Transform t = hostGo.transform;
        if (!_contHave)
        {
            // THE FIRST DIALOG PAGE: the first tick this class has the story's content inside the
            // host. Nothing earlier is the right zero — before that the window is just the loadout
            // screen and the user's sentence has not started yet.
            if (_dock == null || Moved.Count == 0)
                return;
            _contHave = true;
            _contPanelId = hostGo.GetInstanceID();
            _contPos = t.position;
            _contRot = t.rotation;
            return;
        }

        // THE SECOND READ, at the game's own end of the intro rather than at the confirm — see the
        // ModBuild 238 paragraph above for why that had to move.
        if (terminator == null)
            return;
        if (_contReports >= MaxOneWindowReports)
            return;

        int nowId = hostGo.GetInstanceID();
        float movedMm = Vector3.Distance(_contPos, t.position) / Mathf.Max(RigScale(), 1e-4f) * 1000f;
        float turnedDeg = Quaternion.Angle(_contRot, t.rotation);
        bool samePanel = nowId == _contPanelId;
        bool stillThere = movedMm <= MaxGapMillimetres && turnedDeg <= 1f;
        bool ok = samePanel && (stillThere || _contGrabbed);

        string verdict = ok ? "CONFIRMED" : "BROKEN";
        if (verdict == _contVerdict)
            return;
        _contVerdict = verdict;
        _contReports++;

        string measured =
            $"at the first dialog page the host panel was instance {_contPanelId} at world "
            + $"({_contPos.x:F2},{_contPos.y:F2},{_contPos.z:F2}); now that the game says the intro is "
            + $"over — {terminator} — it is instance {nowId} at world "
            + $"({t.position.x:F2},{t.position.y:F2},{t.position.z:F2}) — same panel: {samePanel}, "
            + $"moved {movedMm:F0} mm and turned {turnedDeg:F1}° at the live rig scale "
            + $"(rig {RigScale():F1} world units per tracking metre), player grabbed it at some point "
            + $"during the interval: {_contGrabbed}. The window is '{loadout.name}' "
            + $"(UILoadoutManager), the one the story was told in";

        if (ok)
        {
            VRLog.Info(Scope, "STORY WINDOW CONTINUITY: CONFIRMED — the story was told from the first "
                              + "page to the last in ONE panel, and that panel was never re-spawned or "
                              + "re-placed while it was being read. MEASURED: " + measured + ". USER "
                              + "REPORT THIS LINE ANSWERS: \"Sobald die Story fertig erzählt wurde, "
                              + "spawned nun ein ganz neues Fenster mit einem Hintergrund … Es soll "
                              + "immer noch das exakt gleiche Fenster sein.\" WHAT IT NO LONGER CLAIMS "
                              + "(ModBuild 238): that the continue control appears on this window. It "
                              + "does not, by the user's own later ruling — the window is withdrawn "
                              + "once the intro ends and the control is parked on the Character-UI. "
                              + "See ENTER DUNGEON ON THE CHARACTER UI and LOADOUT BACKGROUND "
                              + "WITHDRAWN for those two.");
            return;
        }
        VRLog.Warn(Scope, "STORY WINDOW CONTINUITY: BROKEN — the window the continue control appears on "
                          + "is not the window the story was told in, or it has moved since. MEASURED: "
                          + measured + ". READ IT LIKE THIS: a DIFFERENT instance id means the panel was "
                          + "released and re-converted in between — grep MODAL WINDOW for "
                          + $"'{loadout.name}' released and count the 'one-shot facing applied' lines, "
                          + "because each one of those is a fresh placement and therefore a window that "
                          + "visibly respawns. That is exactly what the ModBuild 235 log shows: floated "
                          + "at :3435, WITHDRAWN at :3489, re-floated at :3519, withdrawn at :3559 and "
                          + "floated again at :3788, with four different yaws. The same id with a large "
                          + "pose delta and grabbed=False means something re-placed a revealed window, "
                          + "which is the ModBuild 183 bug.");
    }

    private static float RigScale()
    {
        try
        {
            Transform? rig = Rig.VRRigDriver.RigRoot;
            return rig != null ? Mathf.Abs(rig.lossyScale.x) : 1f;
        }
        catch (System.Exception)
        {
            return 1f;
        }
    }

    /// <summary>
    /// Age the two-tick bridge and say ONE line when it is over. After this counter reaches zero
    /// <see cref="HoldsBack"/> is incapable of refusing anything until the next rising edge, so the
    /// line is an EDGE and appears at most once per gate.
    /// </summary>
    private static void TickHoldBridge()
    {
        if (_bridgeTicksLeft <= 0)
            return;
        _bridgeTicksLeft--;
        if (_bridgeTicksLeft > 0 || _bridgeReported)
            return;
        _bridgeReported = true;
        VRLog.Info(Scope, $"POINT OF NO RETURN HOLD EXPIRED — the {HoldBridgeTicks}-tick convert bridge is "
                          + $"over. It existed to stop the {_closedAtOpen} window(s) closed at the rising "
                          + "edge from being re-converted in the frame or two an Escape() transition can "
                          + $"still read IsOpen, and it refused {_heldTotal} conversion(s)"
                          + (_heldTotal > 0 ? $", most recently '{_heldLastName}'" : string.Empty)
                          + ". FROM HERE StoryComposite.HoldsBack RETURNS FALSE FOR EVERY WINDOW until the "
                          + "next rising edge: the loadout screen, the quest popup the loadout re-shows, the "
                          + "battle-goal picker and the party display are all free to float. THE BOUND IS "
                          + "DERIVED, NOT CHOSEN — ModalFallback's catch-all churn fuse suppresses a window's "
                          + "NAME for the whole session on its 4th enrolment in 60 s, and a window the mod "
                          + "holds out of the float set is re-enrolled on EVERY tick, so any hold of 4+ ticks "
                          + $"blows it. That is exactly what ModBuild 231 did to 'UI Loadout Window' and it "
                          + $"is why he was left with 0 floated windows and nothing to click; {HoldBridgeTicks} "
                          + "is strictly under the fuse with a tick to spare.");
    }

    // ---- ModBuild 364: THE MUTUAL-HOLD ARM ------------------------------------------------------

    /// <summary>
    /// HOW MANY CONSECUTIVE TICKS THE TWO HONESTY CLAUSES MAY WAIT FOR EACH OTHER BEFORE ONE OF THEM
    /// IS MADE TO YIELD. <b>A TICK COUNT, NOT A LEVEL</b> — ModBuild 353's ruling, and the reason is
    /// that the state below is legitimately reachable for a handful of ticks in every ordinary quest
    /// start: on the tick the intro terminates, the loadout window is the only thing floated and the
    /// curtain's members have not been through a convert pass yet. A rule that fired on the LEVEL
    /// would lift the curtain in that seam every single time and put the quest log back on screen for
    /// no reason at all.
    ///
    /// <para><b>THE NUMBER IS DERIVED FROM BOTH ENDS OF THE MEASURED RANGE AND IS INSENSITIVE TO THE
    /// FRAME RATE.</b> The ModBuild 362 log measures the legitimate resolution of this exact state at
    /// ONE tick: the ESC menu floated at :16197 and the claim raised at :16204, the very next tick,
    /// confirming at :16214. It measures the stall at roughly 2 000 ticks — ~23 s at the 90 Hz its own
    /// :16210 reports (2 286 frames over 25.4 s). Ninety ticks sits two orders of magnitude above the
    /// transition and more than an order below the stall, so halving or doubling the headset's frame
    /// rate cannot move it onto either side.</para>
    ///
    /// <para><b>WHAT ONE SECOND OF BEING WRONG COSTS:</b> the quest log, the quest popup and the event
    /// window come back on screen about a second early. That is the ModBuild 233/237 presentation this
    /// file already calls "ugly, and NOT a deadlock", and it is the presentation the tester's own ESC
    /// detour produced and accepted (:16373 — 'New Party display' and 'UI Quest Popup' floated).</para>
    /// </summary>
    private const int MutualHoldTicks = 90;

    /// <summary>How long after the lift the outcome is given to happen before this arm says whether it
    /// worked. Derived from the mechanism: the refusal drops on the lift tick, the convert loop floats
    /// the members on the next pass and the backdrop claim raises the tick after that — the ModBuild
    /// 362 log walks that whole chain in six lines (:16266 → :16273). Thirty ticks is five times the
    /// chain and still a third of a second.</summary>
    private const int MutualHoldResolveTicks = 30;

    private static int _mutualHoldTicks;
    private static bool _mutualHoldReported;
    private static bool _mutualHoldLifted;
    private static int _mutualHoldSince;
    private static bool _mutualHoldOutcomeReported;

    /// <summary>
    /// THE MUTUAL-HOLD ARM — the third arm of the floor, and the one the reported near-deadlock
    /// needed. <b>THE CAUSE IT ENDS:</b> two honesty clauses, each individually correct, each
    /// satisfied by the very thing the other one is stuck on.
    ///
    /// <para><b>THE STATE, IN THE ModBuild 362 LOG'S OWN FIELDS.</b> :15787 — the curtain is holding
    /// and <c>OTHER floated window(s): 1 ['UI Loadout Window' (a curtain member: False)]</c>, so
    /// <see cref="AnyNonMemberFloated"/> is TRUE and the curtain holds honestly. :15879 — the backdrop
    /// claim's <c>honesty clause — the mod is floating 0 window(s) this claim is not withholding —
    /// reads False</c>, so it cannot withdraw that same window. :15924 — the battle-goal picker is
    /// therefore never drawn, because its ancestor 'New Party display' is a curtain member. The room
    /// held one window for ~23 s and the user's report is
    /// <i>"(beinahe) DEADLOCK in der map! … aber das Fenster ist nicht erschienen!"</i></para>
    ///
    /// <para><b>WHY THE CURTAIN IS THE ONE THAT YIELDS, AND IT IS A STRUCTURAL ARGUMENT RATHER THAN A
    /// PREFERENCE.</b> Lifting the curtain can only ever ADD to the float set — it stops refusing, and
    /// a window nobody refuses may float. Lifting the backdrop claim can only ever REMOVE from it — it
    /// withdraws the one window on screen. In this state the float set has exactly one member, so
    /// yielding the backdrop claim would leave ZERO floated windows, which is ModBuild 231's ending
    /// verbatim: <i>"Nach der Begegnung sind alle Fenster verschwunden und es nichts mehr weiter
    /// passiert."</i> Against the standing ruling <i>"Es darf niemals leere Fenster geben"</i> the
    /// monotone-adding side is the only one that can be yielded at all. THE COST OF YIELDING THE
    /// CURTAIN, NAMED: the quest log, the quest popup and the event window come back beside the
    /// battle-goal picker for the rest of the pre-scenario phase. That is precisely the picture the
    /// tester's ESC-menu detour produced (:16373) and called <i>"wie es sein sollte"</i>.</para>
    ///
    /// <para><b>AND THE OUTCOME IS VERIFIED, NOT ASSUMED.</b> A floor that lifts a suppression and
    /// does not check that the room actually filled is a remedy with no falsifier
    /// [[verify-outcome-not-path]]. <see cref="MutualHoldResolveTicks"/> ticks after the lift this arm
    /// states RESOLVED or NOT RESOLVED at a printing tier, measured from the float set.</para>
    ///
    /// <para><b>IT IS DISJOINT FROM THE OTHER TWO ARMS BY CONSTRUCTION.</b> The empty-room floor
    /// (<see cref="TripsDeadlockFloor"/>) requires <c>floated == 0</c>; this arm requires
    /// <c>floated &gt; 0</c>. The control arm requires the continue control to be unreachable
    /// ANYWHERE; here it is reachable, on the very window that is stuck — which is exactly why the
    /// user wrote "beinahe DEADLOCK" and not "DEADLOCK", and exactly why neither existing arm fired
    /// (the ModBuild 362 log's <c>lifted by the deadlock floor=False</c> on all six ticks that printed
    /// it, and <c>the deadlock floor did not trip</c> at :17407).</para>
    ///
    /// <para>ONE GREP: <c>MUTUAL HOLD</c>. Nothing is written to the game here either — the lift sets
    /// <see cref="_curtainLifted"/>, which makes <see cref="CurtainRefuses"/> stop saying no. No Hide,
    /// no Escape, no SetActive, no CanvasGroup.</para>
    /// </summary>
    private static void TickMutualHold(int floated)
    {
        // THE ARM RE-ARMS WITH THE THING IT LIFTED, and it has to. `_curtainLifted` is cleared by
        // RaiseCurtain, so a curtain raised again inside the same gate is a curtain this arm has not
        // stood down — and a one-shot latch that outlived its subject would be a remedy that fired
        // once and then watched the same fault form again in silence. The lift itself is still
        // bounded, because MaxCurtainCycles caps how many times the curtain can be raised at all.
        if (_mutualHoldLifted && !_curtainLifted)
        {
            VRLog.Warn(Scope, "MUTUAL HOLD ARM RE-ARMED — the story curtain was raised again after "
                              + "this arm stood it down, so the arm goes back on watch with it. The "
                              + $"curtain has used {_curtainCycles} of {MaxCurtainCycles} cycle(s), "
                              + "which is what bounds how often this can happen.");
            _mutualHoldLifted = false;
            _mutualHoldOutcomeReported = false;
            _mutualHoldSince = 0;
            _mutualHoldTicks = 0;
            _mutualHoldReported = false;
        }

        if (_mutualHoldLifted)
        {
            if (_mutualHoldOutcomeReported)
                return;
            _mutualHoldSince++;
            // RESOLVED means the thing the user could not see is on screen: the mod is floating at
            // least one window that is NOT the window the backdrop claim was aimed at. Measured from
            // the float set, never from a claim's own belief [[measure-the-picture-not-the-state]].
            int others = _backdropSubject != null
                ? ModalFallback.CountFloatsOtherThan(_backdropSubject)
                : floated;
            if (others > 0)
            {
                _mutualHoldOutcomeReported = true;
                // HW-VERIFY
                VRLog.Alert(Scope, "MUTUAL HOLD LIFT: RESOLVED — the story curtain stood down and the "
                                   + $"room filled: the mod is floating {others} window(s) that are not "
                                   + "the backdrop window, so the battle-goal picker's host is drawing "
                                   + "again and the backdrop claim's honesty clause can now let that "
                                   + $"window go. It took {_mutualHoldSince} tick(s) after the lift. "
                                   + "USER REPORT THIS ANSWERS: \"Nach der Auswahl einer Quest und nach "
                                   + "der Story war da nurnoch das Fenster 'Warte bis zur Auswahl "
                                   + "[...]' für die privaten Ziele aber das Fenster ist nicht "
                                   + "erschienen!\"");
                return;
            }
            if (_mutualHoldSince < MutualHoldResolveTicks)
                return;
            _mutualHoldOutcomeReported = true;
            // HW-VERIFY
            VRLog.Alert(Scope, "MUTUAL HOLD LIFT: NOT RESOLVED — the story curtain was stood down "
                               + $"{_mutualHoldSince} tick(s) ago and the mod is STILL floating nothing "
                               + "but the backdrop window. THE CURTAIN WAS NOT THE ONLY THING HOLDING "
                               + "THE PICKER BACK and this arm's premise is wrong: the next round "
                               + "starts at whatever else is refusing 'New Party display'. GREP FOR IT: "
                               + "FLOAT REFUSED and NESTED IN A WINDOW THAT IS REFUSED FOR THE MOMENT, "
                               + "for that window's name. Nothing further is lifted here — the backdrop "
                               + "claim is NOT stood down, because withdrawing the only window on "
                               + "screen is the one thing this floor exists to prevent.");
            return;
        }

        // The two terms of the embrace, each measured this tick and neither of them a value this
        // class believes about itself. `CurtainWithheldNow` counts members the GAME still has open —
        // i.e. windows that would be on screen if the curtain were not standing.
        bool curtainHolding = _curtainStanding && !_curtainLifted && CurtainWithheldNow() > 0;
        bool roomIsOnlyTheBlockedWindow =
            floated > 0 && _backdropSubject != null
            && ModalFallback.CountFloatsOtherThan(_backdropSubject) == 0;
        bool embrace = curtainHolding && roomIsOnlyTheBlockedWindow && !_backdropStanding;

        if (!embrace)
        {
            if (_mutualHoldTicks > 0)
                VRLog.Warn(Scope, $"MUTUAL HOLD CLEARED ON ITS OWN after {_mutualHoldTicks} tick(s) — "
                                  + "the two honesty clauses stopped waiting for each other without "
                                  + "this arm doing anything, which is the ordinary case and the reason "
                                  + $"the threshold is {MutualHoldTicks} ticks rather than a level. "
                                  + $"MEASURED THIS TICK: curtain holding={curtainHolding}, the only "
                                  + $"floated window is the backdrop claim's subject="
                                  + $"{roomIsOnlyTheBlockedWindow}, backdrop claim standing="
                                  + $"{_backdropStanding}, {floated} window(s) floated in total.");
            _mutualHoldTicks = 0;
            _mutualHoldReported = false;
            return;
        }

        _mutualHoldTicks++;
        if (!_mutualHoldReported)
        {
            _mutualHoldReported = true;
            // HW-VERIFY
            VRLog.Alert(Scope, "MUTUAL HOLD MEASURED — two of this lane's honesty clauses are waiting "
                               + "for each other, and each one is individually correct. THE STORY "
                               + $"CURTAIN is withholding {CurtainWithheldNow()} window(s) the game has "
                               + $"open, from the frozen member set [{_curtainNames}], and it holds "
                               + "because there IS something on screen it is not withholding. THE "
                               + "LOADOUT BACKDROP CLAIM wants "
                               + $"'{(_backdropSubject != null ? _backdropSubject.name : "<none>")}' "
                               + "withdrawn and cannot, because that window is the ONLY thing on "
                               + $"screen ({floated} floated in total). EACH CLAUSE IS SATISFIED BY THE "
                               + "THING THE OTHER ONE IS STUCK ON. If this state lasts "
                               + $"{MutualHoldTicks} consecutive tick(s) the STORY CURTAIN — and only "
                               + "the story curtain — is stood down, because lifting it can only ADD "
                               + "windows to the room while lifting the other one would empty it. IF "
                               + "YOU ARE READING THIS IN A HARDWARE LOG the next line to look for is "
                               + "MUTUAL HOLD BROKEN, and after it MUTUAL HOLD LIFT: RESOLVED.");
        }
        if (_mutualHoldTicks < MutualHoldTicks)
            return;

        _mutualHoldLifted = true;
        _mutualHoldSince = 0;
        _mutualHoldOutcomeReported = false;
        _curtainLifted = true;
        // HW-VERIFY
        VRLog.Alert(Scope, "MUTUAL HOLD BROKEN — THE STORY CURTAIN YIELDS. It has been in the mutual "
                           + $"hold above for {MutualHoldTicks} consecutive tick(s), which is two "
                           + "orders of magnitude longer than the ONE tick the ModBuild 362 log "
                           + "measures for the legitimate version of this state (:16197 → :16204). "
                           + $"THE CURTAIN'S {CurtainMembers.Count} FROZEN MEMBER(S) [{_curtainNames}] "
                           + "FLOAT AGAIN FROM THIS TICK with everything on them, and the battle-goal "
                           + "picker draws inside 'New Party display' as soon as that window is "
                           + "converted. WHICH CLAUSE WAS LIFTED AND WHY THAT ONE: the curtain, "
                           + "because standing it down can only ADD windows to the room, while "
                           + "standing down the loadout backdrop claim would withdraw the ONLY window "
                           + "on screen and leave the room empty — ModBuild 231's ending, and against "
                           + "the standing ruling \"Es darf niemals leere Fenster geben\". THE COST, "
                           + "NAMED: the quest log, the quest popup and the event window come back "
                           + "beside the picker for the rest of the pre-scenario phase, which is the "
                           + "picture the ModBuild 362 tester's own ESC-menu detour produced at :16373 "
                           + "and called \"wie es sein sollte\". THE COUNT IS KEPT — lift the verdict, "
                           + $"keep the count: {_curtainCycles} of {MaxCurtainCycles} curtain cycle(s) "
                           + $"and {_backdropCycles} of {MaxBackdropCycles} backdrop cycle(s) used. "
                           + "NOTHING IS WRITTEN TO THE GAME: this only stops a FloatRefusalTable "
                           + "refusal from saying no — no Hide, no Escape, no SetActive, no "
                           + "CanvasGroup.");
        _mutualHoldTicks = 0;
    }

    /// <summary>
    /// THE FLOOR. If this class's own suppression is why the room is empty, say so by name and then
    /// stand down. Reads the previous convert pass (see <see cref="_heldThisPass"/> for the one-tick
    /// lag) so the numbers are a completed measurement rather than a half-built one.
    /// </summary>
    private static void TickDeadlockFloor()
    {
        int floated = ModalFallback.CountFloatsOtherThan(null);

        // ModBuild 235 — THE SECOND ARM, AND THE ModBuild 234 LOG IS THE WHOLE ARGUMENT FOR IT.
        //
        // THE FLOOR BELOW MEASURES AN EMPTY ROOM, AND THE REPORTED DEADLOCK WAS A FULL ONE. When the
        // tester was stuck the mod was floating THREE windows (the census reads "3 floated window(s)
        // — 3 ARMED" for the rest of the session): the quest log, the story box the mod's own sticky
        // re-show was keeping alive, and the Character-UI. `floated > 0`, so TripsDeadlockFloor
        // returned false on every one of those ticks and was right to — by its own definition. Its
        // premise is simply too narrow: a deadlock is not a room with nothing in it, it is a CONTROL
        // THE GAME WANTS SHOWN THAT THE PLAYER CANNOT REACH, and a room can be full of windows while
        // the one control that moves the game forward is inside the window this class is refusing.
        //
        // SO THE FLOOR NOW ALSO WATCHES THE CONTROL. LoadoutConfirmPark answers both halves from the
        // game and from measurement — "the game says a continue control should be showable" and "the
        // mod cannot find it drawing anywhere" — and it prints the naming WARNING itself
        // (LOADOUT CONFIRM REACHABLE: NO, with FloatRefusalTable.Describe naming the suppressor). All
        // that is left here is the consequence: this class's own two suppressions stand down, in the
        // order that costs least to be wrong about. "Lift the verdict, keep the count."
        // ModBuild 236 — THE ARM SURVIVES THE INVERSION AND ITS CONSEQUENCE CHANGED. It no longer
        // makes sense to say "the claim is hiding the button": the claim now refuses the STORY
        // window, and the continue control is a child of the HOST, which this class never withholds.
        // What is left is still worth having and is still the same promise: if the game wants a
        // continue control shown and the mod cannot find it drawing anywhere, then EVERYTHING clever
        // this class is doing stands down for the rest of the gate — the dialog goes home, the story
        // window floats on its own, the curtain lifts — and the count is kept.
        // ModBuild 238 — THE BACKDROP CLAIM JOINS THE ARM'S TRIGGER SET, and it has to. That claim
        // withholds the very window the ModBuild 234 deadlock was about, and it stands in a phase
        // where the composite itself is already down (no dock, no story claim), so the old trigger
        // would never have looked at it. Its own second clause is the primary guard and this is the
        // backstop: two independent mechanisms, because the fault they prevent is the one the player
        // cannot work around.
        if (!_floorLifted && (_claimStanding || _dock != null || _backdropStanding)
            && !LoadoutConfirmPark.ConfirmReachable())
        {
            VRLog.Warn(Scope, "MODAL DEADLOCK FLOOR TRIPPED (CONTROL ARM) — the game wants a "
                              + "pre-scenario continue control shown and the mod cannot find it drawing "
                              + "anywhere, while StoryComposite is composing the quest intro into "
                              + $"'{(_parkHost != null ? _parkHost.name : "<no host>")}'. "
                              + $"{LoadoutConfirmPark.Where}. THE WHOLE COMPOSITE IS BEING STOOD DOWN "
                              + "NOW and stays down until the point-of-no-return gate closes: the story "
                              + "dialog goes back to its own window, that window floats again, and the "
                              + "loadout screen is left entirely alone with its own confirm button on "
                              + $"it. THE COUNT IS KEPT: this claim has used {_claimCycles} of "
                              + $"{MaxWithdrawCycles} cycle(s) and the next round can still see that the "
                              + "rule fired. WHY THIS ARM EXISTS: the older floor below measures ZERO "
                              + "floated windows, and the ModBuild 234 deadlock had THREE — a full room "
                              + "with the one control that moves the game forward out of reach. USER "
                              + "REPORT: \"Der ist nie erschienen, man konnte nicht weiter "
                              + "vorranschreiten.\" See LOADOUT CONFIRM REACHABLE: NO above for the "
                              + "suppressor by name.");
            _floorLifted = true;
            _claimStanding = false;
            _claimObject = null;
            _claimWhy = "the deadlock floor's control arm stood the composite down: the game wanted a "
                        + "continue control shown and the mod could not find it drawing anywhere";
            Unpark("the deadlock floor's control arm stood the composite down");
            // The curtain goes with it. It is not the suppressor named above, but it withholds the
            // floats of windows that were standing when the player committed, and the cheapest thing
            // to be wrong about at this moment is a quest log that came back a few seconds early.
            _curtainLifted = true;
            // ModBuild 238 — AND SO DO THE OTHER TWO SUPPRESSIONS THIS LANE OWNS. When the floor
            // trips, EVERYTHING clever stands down: the backdrop claim hands the loadout screen back
            // with its own confirm button on it, and the quest-journey curtain hands the quest list
            // back. Both are cheap to be wrong about and neither may be the reason the room has
            // nothing to press in it. The counts are kept — lift the verdict, keep the count.
            _backdropStanding = false;
            _backdropObject = null;
            _backdropLifted = true;
            _backdropWhy = "the deadlock floor's control arm stood the backdrop withdrawal down: the "
                           + "game wanted a continue control shown and the mod could not find it "
                           + "drawing anywhere";
            QuestJourneyCurtain.Lift("StoryComposite's deadlock floor tripped its CONTROL ARM — the "
                                     + "game wants a pre-scenario continue control shown and the mod "
                                     + "cannot find it drawing anywhere, so everything this lane "
                                     + "suppresses stands down together");
        }

        // ModBuild 234 — THE CURTAIN GETS THE SAME FLOOR, AND IT IS A BACKSTOP RATHER THAN THE
        // PRIMARY GUARD. TickCurtain's honesty clause already refuses to let the curtain stand
        // unless the mod is floating the story box or the loadout screen, so "zero floated windows
        // while the curtain withholds one" ought to be unreachable. IF THIS EVER FIRES, THE HONESTY
        // CLAUSE IS THE BUG and the line below says so by name — which is exactly what a floor is
        // for. It is checked before the bridge because it is the cheaper of the two to be wrong
        // about: a curtain lifted for nothing costs a quest log back on screen.
        // ModBuild 364 — THE THIRD ARM, AND IT IS THE ModBuild 235 LESSON AT A NEW PLACE. That round
        // widened the floor because "the floor measures an empty room, and the reported deadlock was a
        // full one". The room reported this time held ONE window and the continue control was
        // reachable on it, so neither existing arm could fire and neither was wrong to sit still: the
        // fault is not an empty room and not an unreachable control, it is two of this lane's own
        // clauses each waiting for the other. It is asked BEFORE the empty-room arm below because the
        // two are disjoint (this one needs floated > 0, that one needs floated == 0) and because this
        // is the state that actually happened.
        TickMutualHold(floated);

        int withheld = CurtainWithheldNow();
        if (withheld > 0
            && TripsDeadlockFloor("StoryComposite.CurtainRefuses (the point-of-no-return story curtain)",
                                  floated, withheld,
                                  $"its frozen member set is [{_curtainNames}] and the game still has "
                                  + $"{withheld} of them open; the curtain's own honesty clause was "
                                  + "supposed to make this state impossible, so THAT clause is where the "
                                  + "next round starts"))
            _curtainLifted = true;

        if (_holdLifted || _heldLastPass <= 0)
            return;
        if (!TripsDeadlockFloor("StoryComposite.HoldsBack (the point-of-no-return convert-loop bridge)",
                                floated, _heldLastPass,
                                $"the most recent window it refused was '{_heldLastName}'; the gate closed "
                                + $"{_closedAtOpen} window(s) at its rising edge and has closed nothing since"))
            return;
        _holdLifted = true;
    }

    /// <summary>
    /// THE DEADLOCK FLOOR, WRITTEN ONCE SO THE NEXT SUPPRESSOR CAN REUSE IT.
    ///
    /// <para>A mod suppression that leaves ZERO floated windows while the fallback had at least one
    /// window it would otherwise have converted is not a strict rule, it is a deadlock: there is
    /// nothing on screen, nothing to click and no way forward, and the player's only move is to quit
    /// — which is exactly what the ModBuild 231 tester did. This returns true when that state is
    /// measured, having already printed a WARNING that NAMES the suppressor. <b>The caller must then
    /// lift its own suppression.</b></para>
    ///
    /// <para><b>"LIFT THE VERDICT, KEEP THE COUNT"</b> ([[a-fuse-cannot-tell-a-hand-from-a-loop]]).
    /// The counts stay in the log so a later round can still see that the rule fired and how often;
    /// what is dropped is the rule's power to keep the room empty. A suppression that is right about
    /// its policy and wrong about its consequence must lose to the consequence.</para>
    ///
    /// <para><b>WHY THE "AT LEAST ONE WOULD-BE CONVERSION" HALF MATTERS.</b> Zero floated windows is
    /// a perfectly normal state — the map room with nothing open is exactly that. What makes it a
    /// deadlock is that the fallback WANTED to convert something and this mod said no. Callers must
    /// therefore pass a count of refusals they made from inside the convert loop's own guard chain
    /// (i.e. after <c>FloatWantedFor</c>, <c>IsConverted</c>, <c>Failed</c> and
    /// <c>EmptyHeldNow</c> have all already passed), which is precisely "a window the fallback would
    /// otherwise convert" and nothing weaker.</para>
    ///
    /// <para>ONE GREP: <c>MODAL DEADLOCK FLOOR</c>. It must never appear. If it does, the line names
    /// the suppressor and the next round starts there instead of at a black screen.</para>
    /// </summary>
    /// <param name="suppressor">Who refused. Named in the log line — a floor that says "something"
    /// is a floor that costs a build.</param>
    /// <param name="floatedNow">How many windows the mod is floating right now.</param>
    /// <param name="suppressedNow">How many would-be conversions <paramref name="suppressor"/>
    /// refused in the most recent complete convert pass.</param>
    /// <param name="detail">One clause of caller-specific evidence for the log line.</param>
    /// <returns>True when the floor tripped and the caller must lift its suppression.</returns>
    internal static bool TripsDeadlockFloor(string suppressor, int floatedNow, int suppressedNow,
                                            string detail)
    {
        if (floatedNow > 0 || suppressedNow <= 0)
            return false;
        VRLog.Warn(Scope, $"MODAL DEADLOCK FLOOR TRIPPED — {suppressor} refused {suppressedNow} "
                          + "would-be conversion(s) in the last convert pass while the mod is floating "
                          + "ZERO windows. That is not a strict rule, it is a room with nothing in it and "
                          + "no way forward: the game has windows it wants shown and the mod is the only "
                          + $"reason they are not. EVIDENCE: {detail}. THE SUPPRESSION IS BEING LIFTED NOW "
                          + "and the count above is kept — lift the verdict, keep the count. USER REPORT "
                          + "THIS FLOOR EXISTS FOR (ModBuild 231): \"Nach der Begegnung sind alle Fenster "
                          + "verschwunden und es nichts mehr weiter passiert.\" If this line is in a log, "
                          + "the named suppressor is the bug and no further diagnosis is needed.");
        return true;
    }

    /// <summary>
    /// THE CONVERT-LOOP BRIDGE. Called from <c>ModalFallback.4.Tick.cs</c>'s convert loop, right
    /// after <c>if (EmptyHeldNow(window)) continue;</c>.
    ///
    /// <para><b>IT IS BOUNDED THREE WAYS AND EVERY ONE OF THEM IS STRUCTURAL.</b></para>
    /// <list type="number">
    /// <item>BY MEMBERSHIP — it can only refuse a window INSTANCE that <see cref="OpenGate"/> itself
    /// closed at the rising edge (<see cref="EdgeClosed"/>, filled once and never appended to). The
    /// loadout screen, the story box, the quest popup the loadout re-opens, the battle-goal picker
    /// and the party display are not members and cannot become members.</item>
    /// <item>BY TIME — <see cref="HoldBridgeTicks"/> ticks, total, per gate. Not per window, not
    /// re-armed, not extendable. After that this method is a constant <c>false</c> until the next
    /// rising edge.</item>
    /// <item>BY THE FLOOR — <see cref="TripsDeadlockFloor"/> sets <see cref="_holdLifted"/> and ends
    /// it early if it is ever the reason the room is empty.</item>
    /// </list>
    ///
    /// <para><b>WHY IT EXISTS AT ALL, GIVEN THAT ModBuild 231's VERSION OF IT CAUSED THE BUG.</b>
    /// The edge closes its named set through <c>ModalFallback.CloseFloatedWindow</c>, which flags the
    /// panel <c>UserClosing</c> and hides the game window. The tick order is in our favour and is
    /// stated here because the bound depends on it: this class runs from the FIRST line of
    /// <c>TickWindowLiveness</c>, the release loop that acts on <c>UserClosing</c> runs LATER in the
    /// same tick, and the convert loop later still (<c>ModalFallback.4.Tick.cs</c>:
    /// <c>PhaseRelease</c> :2526 → <c>TickWindowLiveness</c> :2537 → release loop → <c>PhaseConvert</c>
    /// :2675). So in the normal case the close is already reflected in both sets before anything can
    /// re-convert, and this method refuses nothing at all. What it covers is the one case that is not
    /// normal: a window whose <c>Escape()</c> ran a TRANSITION rather than an instant hide still reads
    /// <c>IsOpen</c> for a frame or two, and the convert loop would take it straight back. That is the
    /// whole job. It is a bridge, not a policy, and the difference is that a bridge has a far
    /// end.</para>
    ///
    /// <para>It is a <c>continue</c> and not a <c>TryConvertWindow</c> refusal on purpose: a refusal
    /// enrols the window in <c>Failed</c> and raises the flat screen for it, and "closed" here means
    /// closed, not moved to a screen.</para>
    /// </summary>
    internal static bool HoldsBack(UIWindow? window)
    {
        if (window == null || !_gateOpen || _holdLifted || _bridgeTicksLeft <= 0)
            return false;
        for (int i = 0; i < EdgeClosed.Count; i++)
        {
            if (!ReferenceEquals(EdgeClosed[i], window))
                continue;
            _heldThisPass++;
            _heldTotal++;
            _heldLastName = window.name;
            return true;
        }
        return false;
    }

    // ---- the gate -----------------------------------------------------------------------------

    /// <summary>Scratch for <see cref="CollectNamedSet"/>. Single-threaded, reused, and only ever
    /// touched at a gate edge — no per-tick allocation and no per-tick walk.</summary>
    private static readonly List<UIWindow> NamedSet = new(8);
    private static readonly List<string> NamedWhy = new(8);

    /// <summary>
    /// THE NAMED SET — the windows the user's ruling names, enumerated from the GAME's own
    /// serialized references and never from a scene sweep ([[findobjectsoftype-is-the-default-suspect]]).
    ///
    /// <para><b>MEMBERS.</b> The five guildmaster destinations (merchant, temple, trainer,
    /// enchantress, town records — "Händler und co."), reached through
    /// <c>GuildmasterDestinations.ModeWindow</c>, which reads <c>UIGuildmasterHUD.shopWindow</c> and
    /// its four siblings; plus the map's three quest popups, reached through
    /// <c>UIQuestPopupManager</c>'s own <c>selectedQuestPopup</c>, <c>multiplayerQuestPopup</c> and
    /// <c>questPreviewPopup</c> fields (decompiled UIQuestPopupManager.cs:8-16). All are
    /// <c>[RequireComponent(typeof(UIWindow))]</c> or cache their own <c>GetComponent&lt;UIWindow&gt;</c>
    /// in <c>Awake</c>, so the component and the window are provably the same GameObject —
    /// [[containment-is-not-identity]].</para>
    ///
    /// <para><b>WHAT IS OUT, AND WHY IT IS OUT BY CONSTRUCTION.</b> Everything the pre-scenario
    /// loadout sequence opens: the loadout screen itself, the story box, the battle-goal picker, the
    /// party display, the quest popup the LOADOUT re-shows after <c>EnableLoadoutInteraction</c>
    /// (ModBuild 231 log :94474-94475 — note that this is the same window object as a member, which
    /// is exactly why the close is a one-shot at the edge and not a standing rule: closing it THEN
    /// says nothing about showing it LATER). None of them is reachable from the two singletons above,
    /// so no bug in this method can select one.</para>
    ///
    /// <para><b>AND ONE MEMBER IS DELIBERATELY ABSENT: THE QUEST LOG.</b> ModBuild 231's exclusion
    /// rule closed it ('Quest Log Manager' in the :94226 line). It is not a member here for three
    /// independent reasons, and any one of them would be enough: (1) it is the map room's PERMANENT
    /// window by an earlier user ruling — <c>ModalFallback.IsMapRoomPermanent</c> /
    /// <c>MapRoomPermanentReason</c>, "which has no X and is not closable in the map room"; (2)
    /// <c>ModalFallback.CloseFloatedWindow</c> therefore REFUSES it, so a membership would have been
    /// a no-op with a log line; (3) <c>QuestLogManager</c> is a plain <c>MonoBehaviour</c> with no
    /// singleton, so reaching it would need the scene sweep this method exists to avoid. If the user
    /// wants it shut at the point of no return that is a change to the PERMANENCE ruling, made by
    /// him, not a fourth mechanism bolted on here.</para>
    ///
    /// <para><b>ModBuild 459 — THE FULL ENUMERATION, BECAUSE THE USER WIDENED THE RULING TO "ALLE
    /// FENSTER" AND A MEMBER NOT NAMED HERE IS THE NEXT REPORT.</b> Every window the 3D map room can
    /// have standing, and where each one is decided:</para>
    /// <list type="number">
    /// <item><b>The five guildmaster destinations</b> — merchant, temple, trainer, enchantress, town
    /// records. MEMBERS, above. (<c>UIGuildmasterHUD.shopWindow / templeWindow / trainerWindow /
    /// enhancementWindow / townRecordsWindow</c>, :77-89 — the five serialized fields are the whole
    /// family, so there is no sixth destination to miss.)</item>
    /// <item><b>The three quest popups</b> — <c>selectedQuestPopup</c>, <c>multiplayerQuestPopup</c>,
    /// <c>questPreviewPopup</c>. MEMBERS, above. The first two are the user's "Questfenster".</item>
    /// <item><b>The quest log</b> — NOT a member, withheld instead by
    /// <see cref="QuestJourneyCurtain"/> over exactly this interval, for the three reasons in the
    /// paragraph above. Its verdict has its own grep line, so it is enforced and measured, not
    /// forgotten.</item>
    /// <item><b>'New Party display' (the character UI), the loadout screen, the story box and the
    /// battle-goal picker</b> — out BY CONSTRUCTION: the pre-scenario sequence needs every one of
    /// them, and none is reachable from the two singletons this method walks.</item>
    /// <item><b>'UI Event Window' (<c>UIEventPanel</c>, ID <c>EventsPanel</c>) — DELIBERATELY NOT A
    /// MEMBER, and this is the one exclusion that is a RULE rather than a construction.</b> It is a
    /// <c>MandatoryDecisionTerm.EncounterPanel</c>: the only sender of
    /// <c>GameActionType.ContinueRoadEvent</c> is an <c>EventButton</c> press (UIEventPanel.cs:606,
    /// :610, :724), so closing it sends NOTHING and leaves every remote client's event window open
    /// forever at <c>ProcessOneAndHalt</c>. It is also a legitimate part of the journey the commit
    /// begins — the ModBuild 457 peer log has the road event SHOWN at <c>remote/Player.log:22532</c>,
    /// 521 lines after the commit — so a sweep that took it would be closing the very window the
    /// player has to answer to get to the scenario. What the gate does do about the encounter is the
    /// game's own request-counted <c>UIGuildmasterHUD.DisableCityEncounter</c>, which stops a NEW one
    /// being opened from the HQ button; see <see cref="SetDestinationsLocked"/>.</item>
    /// <item><b>The hover preview and the local tooltips</b> — never sticky at all
    /// (<c>ModalFallback.IsMapRoomHoverCard</c>); they leave with the hover and there is nothing for
    /// a sweep to close.</item>
    /// <item><b>The player's own menus</b> — the ESC menu and the mod's VR options window. NOT
    /// members: they are opened by the player's own key and belong to him, the options key owns
    /// their lifetime by a standing ruling, and pulling a menu out from under a hand is not what
    /// "alle anderen Fenster" asks for.</item>
    /// </list>
    /// </summary>
    private static void CollectNamedSet()
    {
        NamedSet.Clear();
        NamedWhy.Clear();
        try
        {
            AddNamed(GuildmasterDestinations.ModeWindow(EGuildmasterMode.Merchant),
                     "guildmaster destination Merchant");
            AddNamed(GuildmasterDestinations.ModeWindow(EGuildmasterMode.Temple),
                     "guildmaster destination Temple");
            AddNamed(GuildmasterDestinations.ModeWindow(EGuildmasterMode.Trainer),
                     "guildmaster destination Trainer");
            AddNamed(GuildmasterDestinations.ModeWindow(EGuildmasterMode.Enchantress),
                     "guildmaster destination Enchantress");
            // MercenaryLog is a sixth MODE sharing UITownRecordsWindow, so it resolves to the same
            // window — AddNamed dedupes by reference and the log says TownRecords once.
            AddNamed(GuildmasterDestinations.ModeWindow(EGuildmasterMode.TownRecords),
                     "guildmaster destination TownRecords (also the MercenaryLog mode's window)");

            if (Singleton<UIQuestPopupManager>.IsInitialized)
            {
                UIQuestPopupManager qp = Singleton<UIQuestPopupManager>.Instance;
                if (qp != null)
                {
                    AddNamed(WindowOf(qp.selectedQuestPopup),
                             "map-room browsing window UIQuestPopupManager.selectedQuestPopup");
                    AddNamed(WindowOf(qp.multiplayerQuestPopup),
                             "map-room browsing window UIQuestPopupManager.multiplayerQuestPopup");
                    AddNamed(WindowOf(qp.questPreviewPopup),
                             "map-room browsing window UIQuestPopupManager.questPreviewPopup");
                }
            }
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"POINT OF NO RETURN: building the named close set threw ({e.GetType().Name}) "
                              + $"after {NamedSet.Count} member(s). The gate still opens and the destination "
                              + "lock still runs; whatever was collected before the throw is still closed. "
                              + "Fail direction: a window left standing, never a window the loadout needs "
                              + "taken away.");
        }
    }

    private static UIWindow? WindowOf(Component? owner) =>
        owner != null ? owner.GetComponent<UIWindow>() : null;

    private static void AddNamed(UIWindow? window, string why)
    {
        if (window == null)
            return;
        for (int i = 0; i < NamedSet.Count; i++)
        {
            if (ReferenceEquals(NamedSet[i], window))
                return;
        }
        NamedSet.Add(window);
        NamedWhy.Add(why);
    }

    /// <summary>Is this window one the mod is floating right now? Asked through the only float-set
    /// question <c>ModalFallback</c> exposes to this class — a window is floated exactly when
    /// excluding it from the count changes the count. Two walks of a list that is never longer than
    /// a handful, at most eight times, ONCE per gate.</summary>
    private static bool FloatedByMod(UIWindow window, int total) =>
        ModalFallback.CountFloatsOtherThan(window) < total;

    /// <summary>The same question when the caller has no total in hand. Two walks of a list that is
    /// never longer than a handful; called a few times per tick, not per window.</summary>
    private static bool FloatedByMod(UIWindow? window) =>
        window != null
        && ModalFallback.CountFloatsOtherThan(window) < ModalFallback.CountFloatsOtherThan(null);

    private static void OpenGate(UIWindow? loadout)
    {
        _gateOpen = true;
        _holdLifted = false;
        _bridgeReported = false;
        _heldTotal = 0;
        _heldThisPass = 0;
        _heldLastPass = 0;
        _heldLastName = string.Empty;
        EdgeClosed.Clear();

        CollectNamedSet();
        int total = ModalFallback.CountFloatsOtherThan(null);
        var sb = new System.Text.StringBuilder(192);
        int skipped = 0;
        for (int i = 0; i < NamedSet.Count; i++)
        {
            UIWindow w = NamedSet[i];
            bool open = w.IsOpen;
            bool floated = FloatedByMod(w, total);
            if (!open && !floated)
            {
                skipped++;
                continue;   // nothing of this member is standing — closing it would only log noise
            }
            EdgeClosed.Add(w);
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append('\'').Append(w.name).Append("' (").Append(NamedWhy[i])
              .Append(", IsDestination=").Append(GuildmasterDestinations.IsDestination(w))
              .Append(", game open=").Append(open)
              .Append(", floated=").Append(floated).Append(')');
            CloseOne(w);
        }
        _closedAtOpen = EdgeClosed.Count;
        _bridgeTicksLeft = _closedAtOpen > 0 ? HoldBridgeTicks : 0;
        string names = sb.Length > 0 ? sb.ToString() : "none";

        // THE LOCK RUNS AFTER THE CLOSES, NOT BEFORE. ToggleGreyOut writes `toggle.isOn = false` on
        // a button it makes non-interactable (decompiled UIGuildmasterButton.cs:249-252), which
        // fires the ToggleGroup's own value-changed path; running it before the closes would mean
        // the mode machine and the window closes race each other for the same button. Closing first
        // leaves the machine at home and the lock a pure presentation change on top of it.
        int caps = SetDestinationsLocked(true, loadout);

        string edge = QuestJourneyCurtain.PartyCommitted
            ? "the QUEST CONFIRM (ModBuild 459) — MapChoreographer.OnMoveClick has run, measured by "
              + "QuestJourneyCurtain's own three game terms (a location had been selected, "
              + "AdventureMapUIManager.LocationToTravel is null again, AdventureMapUIManager.IsLocked "
              + "is true). THIS IS THE EARLIEST OF THE THREE EDGES AND THE ONE THE USER NAMES: \"Beim "
              + "point-of-no-return (Quest bestätigt)\". Up to 458 the sweep waited for the story "
              + "curtain, about two minutes and ~8900 frames later, and the ModBuild 457 peer log has "
              + "the merchant standing open across the whole of that gap"
            : _curtainStanding
            ? "the STORY CURTAIN (ModBuild 234) — the game has hidden the rest of its own UI for the "
              + "quest-start message chain (MapStoryController.isVisibleOtherUI is false), which is "
              + $"the moment the user named. {CurtainMembers.Count} floated window(s) are held out by "
              + $"the curtain's frozen member set [{_curtainNames}]"
            : "the PRE-SCENARIO LOADOUT SCREEN (UILoadoutManager.IsOpen) — this quest played no "
              + "full-attention story message and no commit was measured, so the ModBuild 233 edge is "
              + "the one that fired and the curtain is not standing";

        VRLog.Info(Scope, $"POINT OF NO RETURN OPENED at edge: {edge}. So the party has committed to a "
                          + "quest. THIS IS A ONE-SHOT AT THE RISING EDGE: there is no level-triggered "
                          + "sweep in this build and nothing in StoryComposite closes a window after this "
                          + "line. GREP THIS STRING: it must appear EXACTLY ONCE per quest start — the "
                          + "gate's level is the UNION of the QUEST CONFIRM (ModBuild 459), the curtain "
                          + "and the loadout screen precisely so that the three halves of one commitment "
                          + "cannot produce three edges — the confirm rises first and holds while the "
                          + "other two rise inside it. IF THIS LINE APPEARS TWICE FOR ONE QUEST, the "
                          + "commit level let go mid-journey and PARTY COMMIT LEVEL LAPSED above names "
                          + "the term that let it. CLOSED "
                          + $"{_closedAtOpen} of {NamedSet.Count} NAMED member(s) [{names}] "
                          + $"({skipped} member(s) were neither open nor floated and were left alone) and "
                          + $"LOCKED {caps} guildmaster destination(s) plus the city encounter. THE SET IS "
                          + "NAMED, NOT EXCLUDED: it is the five guildmaster destinations off "
                          + "UIGuildmasterHUD's own serialized references plus UIQuestPopupManager's three "
                          + "quest popups, so the loadout screen, the story box, the battle-goal picker and "
                          + "the party display are out of scope BY CONSTRUCTION — every IsDestination=False "
                          + "entry above is a quest popup and nothing else can appear here. THE CLOSE IS THE "
                          + "GAME'S OWN X: GuildmasterDestinations.CloseMode → "
                          + "ModalFallback.CloseFloatedWindow (float flagged UserClosing, the mode's Exit "
                          + "run through LeaveMode, then UIWindow.Escape()/Hide()), which writes nothing on "
                          + "the wire. THE LOCK IS THE GAME'S OWN TOO: UIGuildmasterButton.ToggleGreyOut "
                          + "flips the same Toggle.interactable the flat client's quest-select flow flips "
                          + "and stops the same highlight animator, which is why the 3D caps go dead and "
                          + "dark without the cap lane changing a line. USER RULING: \"Zu diesem Zeitpunkt "
                          + "ist der 'Point of Return' schon überschritten, d.h. zB Händler und co. darf "
                          + "man zu diesem Zeitpunkt nicht mehr öffnen können … Alle anderen Fenster sollen "
                          + "dabei dann geschlossen werden.\"");

        // ModBuild 459 — THE ANSWER-BEARING LINE, AT THE SHIPPED DEFAULT TIER. The account above is
        // VRLog.Info, i.e. Debug-gated, and the question this round asks — "did EVERY window go, on
        // BOTH machines, at the moment the user calls the point of no return" — has to be readable
        // without it. It is a one-shot at the gate's rising edge, so it costs one line per quest.
        // HW-VERIFY
        VRLog.Note(Scope, $"POINT OF NO RETURN CLOSED THE ROOM: {_closedAtOpen} of {NamedSet.Count} "
                          + $"named window(s) were standing and went [{names}]; {caps} guildmaster "
                          + $"destination(s) plus the city encounter are now LOCKED. EDGE: "
                          + $"{(QuestJourneyCurtain.PartyCommitted ? "the QUEST CONFIRM"
                                                                   : _curtainStanding ? "the STORY CURTAIN"
                                                                   : "the LOADOUT SCREEN")}. THE "
                          + "SET IS THE WHOLE OF WHAT THE ROOM OPENS FROM ITS CAPS AND ITS MAP: the "
                          + "five guildmaster destinations (merchant, temple, trainer, enchantress, "
                          + "town records) off UIGuildmasterHUD's own serialized references, plus "
                          + "UIQuestPopupManager's three quest popups. THE QUEST LOG IS NOT IN IT AND "
                          + "IS NOT A GAP: it is the map room's permanent window by an earlier ruling "
                          + "and QuestJourneyCurtain withholds its float over exactly this interval "
                          + "— grep QUEST LOG GONE AT THE POINT OF NO RETURN for its verdict. READ "
                          + "IT LIKE THIS: this line must appear ONCE per quest start on EVERY "
                          + "client, and a window the user reports still standing afterwards is "
                          + "either in the bracket above (it was closed and the game re-opened it) "
                          + "or it is not in this set at all — the second case is the one that needs "
                          + "a new member, and the bracket is what tells the two apart.");
    }

    /// <summary>
    /// Close ONE named member through the project's single close path.
    ///
    /// <para>A destination goes through <c>GuildmasterDestinations.CloseMode</c> because a
    /// destination is a MODE and the game's mode machine has no "close", only "switch to another
    /// mode" — only the mode's <c>Exit</c> takes the party display back out of selection mode
    /// (ModBuild 184/195), and <c>CloseMode</c> is the one entry point that does both halves in the
    /// order that leaves no orphan. Everything else goes straight to
    /// <c>ModalFallback.CloseFloatedWindow</c>, which is the same routine <c>CloseMode</c> itself
    /// calls. There is no second close in this file.</para>
    /// </summary>
    private static void CloseOne(UIWindow window)
    {
        try
        {
            if (GuildmasterDestinations.IsDestination(window))
            {
                EGuildmasterMode mode = ModeOf(window);
                if (mode != EGuildmasterMode.None)
                {
                    GuildmasterDestinations.CloseMode(mode, window, "point of no return");
                    return;
                }
            }
            ModalFallback.CloseFloatedWindow(window);
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"POINT OF NO RETURN: closing named member '{window.name}' threw "
                              + $"({e.GetType().Name}). The gate still stands and the remaining members are "
                              + "still closed — one member's throw must not skip the rest, which is the "
                              + "defect the ModBuild 231 unlock had.");
        }
    }

    /// <summary>Which guildmaster mode owns this window, by asking the game for each mode's window
    /// and comparing REFERENCES — the same table <c>GuildmasterDestinations.ModeWindow</c> built the
    /// set from, so the answer cannot disagree with it.</summary>
    private static EGuildmasterMode ModeOf(UIWindow window)
    {
        if (ReferenceEquals(GuildmasterDestinations.ModeWindow(EGuildmasterMode.Merchant), window))
            return EGuildmasterMode.Merchant;
        if (ReferenceEquals(GuildmasterDestinations.ModeWindow(EGuildmasterMode.Temple), window))
            return EGuildmasterMode.Temple;
        if (ReferenceEquals(GuildmasterDestinations.ModeWindow(EGuildmasterMode.Trainer), window))
            return EGuildmasterMode.Trainer;
        if (ReferenceEquals(GuildmasterDestinations.ModeWindow(EGuildmasterMode.Enchantress), window))
            return EGuildmasterMode.Enchantress;
        if (ReferenceEquals(GuildmasterDestinations.ModeWindow(EGuildmasterMode.TownRecords), window))
            return EGuildmasterMode.TownRecords;
        return EGuildmasterMode.None;
    }

    private static void CloseGate()
    {
        _gateOpen = false;
        // THE CURTAIN DIES WITH THE GATE, ALWAYS AND FIRST. A curtain that outlived the interval it
        // was raised for would be a standing suppression with no owner, which is the one shape this
        // whole file exists to be incapable of. The cycle budget is refilled here and only here, so
        // one quest start can raise it at most MaxCurtainCycles times.
        CloseCurtain("the point-of-no-return gate closed");
        _curtainCycles = 0;
        _curtainCapReported = false;
        _curtainLifted = false;
        // ModBuild 236 — THE CLAIM'S CYCLE BUDGET IS REFILLED PER QUEST, HERE AND ONLY HERE. The cap
        // is derived from a fuse whose own window is 60 s and which counts ACTUAL floats; a new
        // point-of-no-return edge is a new quest and a new interval, and the story window that the
        // budget is spent on is a Singleton whose instance never changes.
        _claimCycles = 0;
        _claimCapReported = false;
        // ModBuild 238 — THE BACKDROP CLAIM'S BUDGET IS REFILLED HERE AND ONLY HERE, for the claim's
        // reason: a new point-of-no-return edge is a new quest and a new 60 s fuse window, and the
        // loadout window it is spent on is a Singleton whose instance never changes. The claim itself
        // is dropped too, because the gate closing means the loadout screen is gone and a claim about
        // a window that is not there is a suppression with no subject.
        _backdropStanding = false;
        _backdropObject = null;
        _backdropCycles = 0;
        _backdropCapReported = false;
        _backdropLifted = false;
        _backdropVerdict = string.Empty;
        _backdropReports = 0;
        // ModBuild 364 — THE MUTUAL-HOLD ARM AND THE HONESTY SETTLE ARE PER GATE, for this block's
        // own reason: a new point-of-no-return edge is a new quest, and a hold measured against the
        // last one's window set is not evidence about this one.
        _backdropSubject = null;
        _backdropHonestySettle = 0;
        _backdropSettleReported = false;
        _mutualHoldTicks = 0;
        _mutualHoldReported = false;
        _mutualHoldLifted = false;
        _mutualHoldSince = 0;
        _mutualHoldOutcomeReported = false;
        // ModBuild 242 — AND THE HANDOVER IS RE-ARMED HERE AND ONLY HERE, for the same reason as the
        // budget above: a new point-of-no-return edge is a new quest, with a new story window to
        // hand a place over from.
        _handoverDone = false;
        _backdropWhy = "the loadout backdrop claim is not standing; that window is nobody's "
                       + "responsibility but its own";
        // And the floor's stand-down is per gate for the same reason: whatever was unreachable
        // belonged to the quest that just ended.
        _floorLifted = false;
        int caps = SetDestinationsLocked(false, null);
        VRLog.Info(Scope, $"POINT OF NO RETURN CLOSED — the loadout screen is gone. UNLOCKED {caps} "
                          + $"guildmaster destination(s) and the city encounter; the {_closedAtOpen} "
                          + "window(s) closed when it opened are NOT re-opened, because the player closed "
                          + "nothing and the game will re-show whatever it still wants. Each destination is "
                          + "restored to the interactable state and highlight state it was MEASURED in "
                          + "before the lock, not to a guessed default, and a button whose restore throws "
                          + "no longer skips the other six (the ModBuild 231 log ends with 'the guildmaster "
                          + "lock-out threw (NullReferenceException)' followed by 'UNLOCKED 0'). THIS GATE "
                          + $"FIRED ONCE: {_closedAtOpen} window(s) at the rising edge and nothing since. "
                          + $"The {HoldBridgeTicks}-tick convert bridge refused {_heldTotal} conversion(s) "
                          + $"in total and the deadlock floor {(_holdLifted ? "TRIPPED — see MODAL DEADLOCK FLOOR above" : "did not trip")}.");
        _closedAtOpen = 0;
        _bridgeTicksLeft = 0;
        _bridgeReported = false;
        _heldTotal = 0;
        _heldThisPass = 0;
        _heldLastPass = 0;
        _holdLifted = false;
        EdgeClosed.Clear();
        NamedSet.Clear();
        NamedWhy.Clear();
    }

    /// <summary>
    /// Lock or unlock the guildmaster destinations THROUGH THE GAME'S OWN STATE.
    ///
    /// <para><b>WHY THE GAME'S STATE AND NOT A MOD TINT.</b> The user asked for the game's own
    /// disabled presentation — <i>"die buttons sollen das auch mit ihrer spieleigenen animation
    /// anzeigen"</i>. <c>UIGuildmasterButton.ToggleGreyOut(true)</c>
    /// (decompiled/GH.Runtime/UIGuildmasterButton.cs:238-259) is exactly that presentation: it sets
    /// <c>toggle.interactable = false</c>, swaps <c>icon.material</c> for
    /// <c>UIInfoTools.Instance.disabledGrayscaleMaterial</c>, clears <c>hoverMask</c> and calls
    /// <c>Highlight(false)</c>, which stops the button's <c>LoopAnimator</c>.</para>
    ///
    /// <para><b>AND IT REACHES THE 3D CAPS WITH NO CHANGE IN THE CAP LANE'S FILE.</b>
    /// <c>MapButtonRail.Pressable</c> already asks <c>c.Toggle.IsInteractable()</c>, and the cap
    /// already mirrors the game's highlight object. So the cap's collider goes off, its body and
    /// icon take the rail's disabled treatment, and the pulse the game just stopped stops on the cap
    /// too. The grayscale MATERIAL does not cross: the cap's icon is a <c>SpriteRenderer</c> with a
    /// mod material and the game's grey is a uGUI <c>Image.material</c>. That is a real difference
    /// and it is stated here rather than papered over.</para>
    ///
    /// <para><b>RESTORE IS MEASURED, NOT ASSUMED.</b> A button that was ALREADY non-interactable
    /// before the lock is not touched and not restored — un-greying it would overturn a decision the
    /// game made. For the ones that are locked, the highlight object's own <c>activeSelf</c> is
    /// recorded and put back, because <c>ToggleGreyOut(false)</c> would otherwise start a pulse on
    /// every destination.</para>
    ///
    /// <para><b>AND THE RESTORE IS PER-BUTTON, WHICH IS THE ModBuild 231 FIX.</b> The gate closes at
    /// exactly the moment the scenario scene tears the HQ down, and <c>ToggleGreyOut</c>
    /// dereferences <c>EventSystem.current</c> and the <c>UIInfoTools</c> singleton — both of which
    /// can be gone by then. One try/catch around the whole loop meant the FIRST button's
    /// <c>NullReferenceException</c> skipped the other six and left <see cref="Greyed"/> populated:
    /// the log's last two mod lines are <c>the guildmaster lock-out threw
    /// (NullReferenceException)</c> and <c>UNLOCKED 0 guildmaster destination(s)</c>. Each button now
    /// gets its own guard, the record is cleared in a <c>finally</c> whatever happens, and the
    /// failures are counted out loud so "UNLOCKED 0" can never again mean "the loop died on entry".
    /// </para>
    ///
    /// <para><b>THE CITY ENCOUNTER goes through its request-counted API</b>
    /// (<c>UIGuildmasterHUD.DisableCityEncounter/EnableCityEncounter</c>, :885-892), which is the
    /// game's own way of saying "somebody wants this off" and composes with the game's other
    /// requesters instead of fighting them. The request Component is the loadout window itself: a
    /// real, stable Component that exists for exactly the interval of the lock.</para>
    ///
    /// <para>Returns how many destination buttons this call changed.</para>
    /// </summary>
    private static int SetDestinationsLocked(bool locked, UIWindow? loadout)
    {
        int changed = 0;
        int failed = 0;
        UIGuildmasterHUD? hud = null;
        try
        {
            hud = Singleton<UIGuildmasterHUD>.IsInitialized ? Singleton<UIGuildmasterHUD>.Instance : null;
        }
        catch (System.Exception)
        {
            hud = null;
        }

        if (!locked)
        {
            try
            {
                for (int i = 0; i < Greyed.Count; i++)
                {
                    UIGuildmasterButton b = Greyed[i];
                    if (b == null)
                        continue;
                    // ONE GUARD PER BUTTON. See the doc block: the scene is being torn down around
                    // this loop and the game's own restore call reaches two singletons that may
                    // already be gone. A throw on button 1 must not cost buttons 2..7.
                    try
                    {
                        b.ToggleGreyOut(greyedOut: false);
                        b.Highlight(i < GreyedHadHighlight.Count && GreyedHadHighlight[i]);
                        changed++;
                    }
                    catch (System.Exception)
                    {
                        failed++;
                    }
                }
                try
                {
                    if (hud != null && _cityRequest != null)
                        hud.EnableCityEncounter(_cityRequest, enable: true);
                }
                catch (System.Exception)
                {
                    failed++;
                }
            }
            finally
            {
                // ALWAYS. A record that survives its own restore is a permanently grey merchant the
                // next time the room stands.
                Greyed.Clear();
                GreyedHadHighlight.Clear();
                _cityRequest = null;
            }
            if (failed > 0)
                VRLog.Warn(Scope, $"POINT OF NO RETURN: {failed} of {changed + failed} destination "
                                  + "restore(s) threw — almost always because the gate closed with the "
                                  + "scenario scene, so UIGuildmasterButton.ToggleGreyOut found "
                                  + "EventSystem.current or the UIInfoTools singleton already gone. The "
                                  + "remaining buttons WERE restored (that is the ModBuild 231 fix) and the "
                                  + "record is cleared either way, so nothing stays grey across a room "
                                  + "rebuild — the buttons themselves are re-created with the HQ.");
            return changed;
        }

        if (hud == null)
            return 0;
        try
        {
            Dictionary<EGuildmasterMode, GuildmasterMode>? modes = hud.modes;
            if (modes != null)
            {
                foreach (KeyValuePair<EGuildmasterMode, GuildmasterMode> kv in modes)
                {
                    // THE WORLD MAP IS NOT A DESTINATION. It is the view of the board itself, the
                    // game's own RefreshVisibilityHeadquartersOptions excludes it from the count
                    // that decides whether the bar exists at all (UIGuildmasterHUD.cs:742), and
                    // locking it would take away the way back to looking at the map. "Händler und
                    // co." is the shop/temple/trainer/records family, not the map.
                    if (kv.Key == EGuildmasterMode.WorldMap || kv.Value == null)
                        continue;
                    Transform? t = kv.Value.Button;
                    UIGuildmasterButton? b = t != null ? t.GetComponent<UIGuildmasterButton>() : null;
                    if (b == null || b.toggle == null || !b.toggle.interactable)
                        continue;   // already off game-side: leave it, and leave it out of the restore
                    try
                    {
                        bool hadHighlight = b.highlightAnimator != null
                                            && b.highlightAnimator.gameObject.activeSelf;
                        b.ToggleGreyOut(greyedOut: true);
                        Greyed.Add(b);
                        GreyedHadHighlight.Add(hadHighlight);
                        changed++;
                    }
                    catch (System.Exception)
                    {
                        failed++;
                    }
                }
            }
            _cityRequest = loadout != null ? loadout : (Component?)hud;
            if (_cityRequest != null)
                hud.DisableCityEncounter(_cityRequest);
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"POINT OF NO RETURN: the guildmaster lock-out threw ({e.GetType().Name}) "
                              + $"after locking {changed} button(s) — the named windows are still closed "
                              + "and the gate still stands, but the remaining destination buttons keep "
                              + "whatever state the game had them in. Fail direction: a reachable merchant, "
                              + "never an unreachable map. MapButtonRail.Pressable's PointOfNoReturn clause "
                              + "still refuses the 3D caps regardless of this.");
        }
        if (failed > 0)
            VRLog.Warn(Scope, $"POINT OF NO RETURN: {failed} destination button(s) threw while being "
                              + "greyed out and are NOT in the restore record, so they cannot be left grey "
                              + "by this mod. The rest were locked normally.");
        return changed;
    }

    // ---- the park -----------------------------------------------------------------------------

    /// <summary>
    /// The rect to park: the picture, and nothing that contains the picture.
    ///
    /// <para>THE CANDIDATES, IN ORDER, ALL THREE MEASURED BY THE SAME GUARD:</para>
    /// <list type="number">
    /// <item><c>StoryImageViewer.imageHolder</c> — the <c>Image</c> the viewer assigns the loaded
    /// quest sprite to and switches on (decompiled StoryImageViewer.cs:227-229). This is the
    /// POSITIVE test the round asked for: the rect must carry a <c>Graphic</c>, and once the
    /// addressable has landed that Graphic has a sprite. It is the 'Holder/Paper' 1280x720 rect the
    /// ModBuild 231 fit line lists, i.e. 0.67 x 0.67 of the 1920x1080 window.</item>
    /// <item><c>StoryImageViewer.container</c> — what ModBuild 231 asked for, kept as a fallback for
    /// a prefab in which the sprite lives on the group rather than on a child. The hardware log
    /// proves it is the loadout screen's full-window group in TODAY's prefab
    /// ('UI Loadout Quest Information', 1920x1080, parent of the full-window <c>Blur</c>), so it
    /// will normally fail the fraction guard and that is correct.</item>
    /// <item>the viewer's own transform — last resort, same guard.</item>
    /// </list>
    ///
    /// <para><b>THE FRACTION GUARD IS NEVER SKIPPED</b> (see <see cref="MaxParkFractionOfWindow"/>).
    /// It is the measurement that caught the wrong parent, and a positive test for the right one
    /// does not make it redundant — it makes it cheap.</para>
    ///
    /// <para><b>"NOT LAID OUT YET" IS NOT A REFUSAL.</b> The viewer deactivates <c>imageHolder</c>
    /// while the addressable loads (StoryImageViewer.cs:196-199), and an inactive rect can measure
    /// zero. That state reports as WAITING, is not a failure, and — like every other outcome here —
    /// is logged at most once per loadout window.</para>
    /// </summary>
    private static RectTransform? ImageSubtree(UIWindow loadout)
    {
        try
        {
            if (!Singleton<UILoadoutManager>.IsInitialized)
                return null;
            UILoadoutManager lm = Singleton<UILoadoutManager>.Instance;
            UILoadoutQuestWindow? info = lm != null ? lm.questInfo : null;
            StoryImageViewer? viewer = info != null ? info.imagePaper : null;
            if (viewer == null)
            {
                ReportPick(loadout, "no StoryImageViewer is reachable through "
                                    + "UILoadoutManager.questInfo.imagePaper", warn: true);
                return null;
            }

            var winRect = loadout.transform as RectTransform;
            if (winRect == null)
                return null;

            Image? holder = viewer.imageHolder;
            RectTransform? pick = Judge(holder != null ? holder.transform as RectTransform : null,
                                        winRect, "StoryImageViewer.imageHolder (the Image "
                                                 + "quest.LoadoutImageId's sprite is assigned to, "
                                                 + "StoryImageViewer.cs:233)", out string why1,
                                        out bool wait1);
            if (pick != null)
            {
                ReportPick(loadout, $"parking '{pick.name}' — {why1}", warn: false);
                return pick;
            }

            GameObject? container = viewer.container;
            RectTransform? containerRect =
                container != null && !ReferenceEquals(container, viewer.gameObject)
                    ? container.transform as RectTransform
                    : null;
            pick = Judge(containerRect, winRect, "StoryImageViewer.container (the group the viewer "
                                                 + "switches on around the holder)", out string why2,
                         out bool wait2);
            if (pick != null)
            {
                ReportPick(loadout, $"parking '{pick.name}' — {why2}", warn: false);
                return pick;
            }

            pick = Judge(viewer.transform as RectTransform, winRect,
                         "the StoryImageViewer's own transform (last resort)", out string why3,
                         out bool wait3);
            if (pick != null)
            {
                ReportPick(loadout, $"parking '{pick.name}' — {why3}", warn: false);
                return pick;
            }

            // THREE OUTCOMES, THREE SEVERITIES, AND THE ModBuild 232 LOG IS THE ARGUMENT FOR
            // SEPARATING THEM. That log carries two byte-identical WARNINGS (:5408, :5409) for a
            // state that is completely normal — a map story box opening AFTER the intro, when the
            // paper has already expanded and there is no illustration to compose. A warning that
            // fires on the normal path is how a real finding gets missed.
            if (PaperExpanded(loadout))
            {
                ReportPick(loadout, "the quest intro is OVER — UILoadoutQuestWindow.FinishIntroduction "
                                    + "has expanded the paper (paperFitter.transitionPercent is above "
                                    + $"{PaperExpandedEpsilon:0.00}), so this story box is an ordinary map "
                                    + "message and there is no quest illustration to compose with it. "
                                    + "Nothing is wrong and nothing is refused. What the candidates "
                                    + "measured: imageHolder: " + why1, warn: false);
                return null;
            }
            if (wait1 || wait2 || wait3)
            {
                ReportPick(loadout, "WAITING for the quest illustration — it has not finished arriving "
                                    + "yet and the composite will be built the moment it has. This is not "
                                    + "a refusal and needs no action. imageHolder: " + why1, warn: false);
                return null;
            }
            ReportPick(loadout, "no candidate qualified. imageHolder: " + why1 + " | container: "
                                + why2 + " | viewer: " + why3 + ". The picture and the dialog stay "
                                + "two windows this run, which is the status quo and not a new failure",
                       warn: true);
            return null;
        }
        catch (System.Exception e)
        {
            ReportPick(loadout, "reaching the loadout screen's quest picture "
                                + $"(UILoadoutManager.questInfo/imagePaper) threw {e.GetType().Name}",
                       warn: true);
            return null;
        }
    }

    /// <summary>
    /// Measure one candidate against the window it lives in. Returns the rect when it qualifies,
    /// otherwise null with <paramref name="why"/> naming the exact test it failed — the string that
    /// ends up in the ONE report line.
    ///
    /// <para><b><paramref name="waiting"/> IS THE ModBuild 233 ADDITION AND IT IS THE WHOLE
    /// DIFFERENCE BETWEEN "not yet" AND "no".</b> Three of the tests below are about a rect that has
    /// not finished becoming a picture: it has not been laid out, its GameObject is switched off, or
    /// its <c>Image</c> has no sprite. All three are the NORMAL state for the first few hundred
    /// milliseconds of every quest intro, because the illustration comes down an addressable
    /// (StoryImageViewer.cs:59-76) and the viewer deactivates <c>imageHolder</c> until it lands
    /// (:201, re-activated at :234). ModBuild 232 treated "sprite NOT LOADED YET" as an acceptance
    /// and parked a 0x0, inactive, sprite-less rect; the picture was then invisible for the whole
    /// intro. Waiting costs a few ticks. Parking early costs the feature.</para>
    /// </summary>
    private static RectTransform? Judge(RectTransform? rect, RectTransform winRect, string what,
                                        out string why, out bool waiting)
    {
        waiting = false;
        if (rect == null)
        {
            why = $"{what}: not present on this prefab";
            return null;
        }
        if (!rect.IsChildOf(winRect))
        {
            why = $"{what}: '{rect.name}' is not inside the loadout window";
            return null;
        }
        Vector2 ws = winRect.rect.size;
        Vector2 rs = rect.rect.size;
        if (rs.x < MinParkSizePx || rs.y < MinParkSizePx)
        {
            waiting = true;
            why = $"{what}: '{rect.name}' measures {rs.x:F0}x{rs.y:F0} px — WAITING for its first "
                  + "layout, not refused";
            return null;
        }
        if (ws.x > 1f && ws.y > 1f
            && (rs.x > ws.x * MaxParkFractionOfWindow || rs.y > ws.y * MaxParkFractionOfWindow))
        {
            why = $"{what}: '{rect.name}' is {rs.x:F0}x{rs.y:F0} px of a {ws.x:F0}x{ws.y:F0} px window, "
                  + $"i.e. more than {MaxParkFractionOfWindow:P0} of it — that is either the loadout "
                  + "screen's full-window group (the parent of the 1920x1080 'Blur') or a paper that "
                  + "has already run its expand tween, and neither is the quest picture";
            return null;
        }
        var graphic = rect.GetComponent<Graphic>();
        var image = graphic as Image;
        if (graphic == null)
        {
            why = $"{what}: '{rect.name}' draws nothing itself (no Graphic) — a container, not the "
                  + "picture";
            return null;
        }
        // THE TWO "NOT YET" TESTS. Both are read off the GAME's own switches, in the order the
        // viewer flips them: it activates the holder and assigns the sprite in the same callback
        // (StoryImageViewer.cs:233-234), so a rect that fails either is mid-load and nothing else.
        if (!rect.gameObject.activeInHierarchy)
        {
            waiting = true;
            why = $"{what}: '{rect.name}' is {rs.x:F0}x{rs.y:F0} px but its GameObject is NOT ACTIVE — "
                  + "WAITING, not refused (StoryImageViewer.LoadImages deactivates the holder at :201 "
                  + "and only re-activates it at :234, once the addressable sprite has landed)";
            return null;
        }
        if (image != null && image.sprite == null)
        {
            waiting = true;
            why = $"{what}: '{rect.name}' is {rs.x:F0}x{rs.y:F0} px and active, but its sprite is NOT "
                  + "LOADED YET — WAITING, not refused. Parking it now is exactly the ModBuild 232 "
                  + "defect: the composite would carry an empty rect and the player would see the "
                  + "blank parchment of story_getrennt3.jpg";
            return null;
        }
        why = $"{what}: '{rect.name}' is {rs.x:F0}x{rs.y:F0} px of a {ws.x:F0}x{ws.y:F0} px window "
              + $"({rs.x / Mathf.Max(1f, ws.x):P0} x {rs.y / Mathf.Max(1f, ws.y):P0}), draws through "
              + $"{graphic.GetType().Name}, is active in the hierarchy"
              + (image != null ? $" and carries sprite '{image.sprite.name}'" : string.Empty);
        return rect;
    }

    /// <summary>ONE line per decision per loadout window, capped. The ModBuild 231 log carries forty
    /// byte-identical copies of a single refusal; that is an instrument defect, and a wall of
    /// identical warnings is how a real second finding gets missed.</summary>
    private static void ReportPick(UIWindow? host, string outcome, bool warn)
    {
        if (!ReferenceEquals(host, _pickHost))
        {
            _pickHost = host;
            _pickReported = string.Empty;
            _pickReports = 0;
        }
        if (outcome == _pickReported || _pickReports >= MaxPickReportsPerHost)
            return;
        _pickReported = outcome;
        _pickReports++;
        string line = $"STORY COMPOSITE PICK: {outcome}. (Reported once per outcome per loadout window, "
                      + $"at most {MaxPickReportsPerHost} times — ModBuild 231 printed this decision 40 "
                      + "times for one refusal.)";
        if (warn)
            VRLog.Warn(Scope, line);
        else
            VRLog.Info(Scope, line);
    }

    /// <summary>ONE line per story box, so the "no host" branch is VISIBLE in a hardware log rather
    /// than being an absence. A tree-wide grep finds fourteen callers of
    /// <c>Singleton&lt;MapStoryController&gt;.Instance.Show</c> and only one of them is the quest
    /// intro; for the other thirteen there is no loadout screen to host anything and the story window
    /// must float alone exactly as it always has.</summary>
    private static UIWindow? _noHostReported;

    private static void ReportNoHost(UIWindow story, UIWindow? loadout)
    {
        if (ReferenceEquals(story, _noHostReported))
            return;
        _noHostReported = story;
        VRLog.Info(Scope, $"STORY COMPOSITE NO HOST — the story box '{story.name}' is up and there is "
                          + (loadout == null
                              ? "NO loadout screen at all (UILoadoutManager is not initialised or its "
                                + "window could not be resolved)"
                              : $"a loadout screen '{loadout.name}' but it is not open")
                          + ", so this is one of the THIRTEEN other callers of "
                          + "MapStoryController.Show — the temple, an achievement, the town records, "
                          + "the map's own quest-start chain — and there is nothing to compose it "
                          + "with. THE COMPOSITE DOES NOT STAND AND THE STORY WINDOW FLOATS ALONE, "
                          + "exactly as it did before ModBuild 233. Nothing is refused, nothing is "
                          + "parked and nothing is written to the game. This line is Info and is "
                          + "printed once per story box: it is the normal path, not a fault.");
    }

    private static void EnsureParked(UIWindow loadout, UIWindow story)
    {
        var hostRect = loadout.transform as RectTransform;
        var storyRect = story.transform as RectTransform;
        if (hostRect == null || storyRect == null)
            return;

        if (_dock == null || _parkHost == null || _parkSource == null
            || !ReferenceEquals(_parkHost, loadout) || !ReferenceEquals(_parkSource, story))
        {
            if (_dock != null)
                Unpark("the host or the story window changed under the parked dialog");
            // THE PICTURE IS THE READINESS GATE AND THE PLACEMENT ZERO, AND IT IS NEVER MOVED
            // (ModBuild 236). Judge's three "not yet" states — not laid out, not active, no sprite —
            // are still the reason the composite waits, because a dialog placed against an
            // illustration that has not arrived would be placed against nothing.
            RectTransform? image = ImageSubtree(loadout);
            if (image == null)
                return;
            if (!Park(loadout, hostRect, story, storyRect, image))
                return;
        }

        if (_dock == null || _picture == null)
            return;
        // The game re-parented the dock, or somebody took a moved child back: hand everything home
        // and stop. MapTravelConfirm's ownership re-check, for its reason — a subtree that is no
        // longer ours must never be written to.
        if (_dock.parent == null || !ReferenceEquals(_dock.parent, hostRect))
        {
            Unpark("the game re-parented the story dock");
            return;
        }
        for (int i = 0; i < Moved.Count; i++)
        {
            Transform m = Moved[i];
            if (m == null || !ReferenceEquals(m.parent, _dock))
            {
                Unpark("the game took one of the story window's own children back out of the dock");
                return;
            }
        }
        // The GAME has taken the illustration away (StoryImageViewer.LoadImages deactivates the
        // holder for the next image, StoryImageViewer.Hide switches the container off, or the sprite
        // reference is gone). That is not ours to fight, so stand the composite down and let the
        // story window have its content back — a composite with no picture in it has no zero to place
        // the dialog against.
        if (!_picture.gameObject.activeInHierarchy)
        {
            Unpark("the game deactivated the quest illustration's own GameObject (StoryImageViewer "
                   + "switches the holder off between images, StoryImageViewer.cs:201)");
            return;
        }
        var live = _picture.GetComponent<Image>();
        if (live != null && live.sprite == null)
        {
            Unpark($"the quest illustration lost its sprite (it was '{_parkedSprite}') — the viewer "
                   + "is loading a different image, so the rect is no longer a picture");
            return;
        }
        // THE COMPOSITE OWNS THE DOCK'S SIZE, so the ordinary case is one Vector2 compare and no
        // write. If anything drove it, re-assert the number measured at the park: every moved child
        // resolves its authored anchors against this rect and a degenerate one would collapse them
        // all ([[anchors-own-a-stretch-child-size]]).
        if (_dock.rect.size != _dockSize && _dockSize.x >= MinParkSizePx
            && _dockSize.y >= MinParkSizePx)
            _dock.sizeDelta = _dockSize;
        // THE LAYER IS RE-ASSERTED ONLY ON AN EDGE, AND THERE ARE TWO EDGES. The supersample path
        // moves a whole panel's layers onto its own capture layer and back again, so the host's layer
        // is not a constant; and a SECOND writer can revert the moved subtree underneath us (see
        // LayersDrifted). Both are a handful of events per window, not per tick, and a per-tick
        // subtree sweep is exactly the cost this project has had to take back out of a frame before.
        int hostLayer = loadout.gameObject.layer;
        if (Layers.Written != hostLayer || LayersDrifted(hostLayer))
            WriteLayers(hostLayer);
        ApplyPose(hostRect);
    }

    /// <summary>
    /// Move the STORY WINDOW'S OWN CONTENT into the loadout screen and MAKE THE COMPOSITE OWN THE
    /// SIZE OF THE RECT IT IS MOVED INTO. (Up to ModBuild 235 this method did the mirror image of
    /// that — it moved the picture into the story window — and the paragraph below is the lesson it
    /// learned there, applied to the container.)
    ///
    /// <para><b>THE <c>sizeDelta</c> WRITE IS THE ModBuild 232 FIX AND IT IS NOT OPTIONAL.</b>
    /// <c>imageHolder</c> is a STRETCH child of <c>'Paper'</c> — <c>anchorMin</c>/<c>anchorMax</c>
    /// spread it across its parent and its <c>sizeDelta</c> is the inset pair, so its drawn size is
    /// entirely its parent's. Collapsing the anchors to a point (which is what makes the rect
    /// placeable under the story window at all) therefore leaves <c>rect.size == sizeDelta</c>,
    /// which for a full-bleed stretch child is (0,0). That is the whole of "die Zeichnung/Bild ist
    /// nicht mehr richtig zu sehen": ModBuild 232's own BUILT line reads <c>'Image' 0x0 px</c>.
    /// Writing the size that was MEASURED one line earlier fixes it and buys something better than a
    /// fix — from this point the parked rect's size does not depend on the loadout screen at all, so
    /// neither the async sprite nor <c>FinishIntroduction</c>'s paper-expand tween (which drives
    /// <c>'Paper'</c>, no longer this rect's parent) can move it. [[a-remedy-knows-one-writer]].</para>
    ///
    /// <para><b>ModBuild 236 — THE SAME RULE, APPLIED TO A CONTAINER INSTEAD OF TO A GAME RECT.</b>
    /// A mod-owned <c>GloomhavenVR.StoryDock</c> is created under the HOST and given the STORY WINDOW
    /// ROOT's own measured rect size, and then the story root's CHILDREN are moved into it with
    /// <c>SetParent(worldPositionStays: false)</c> — which leaves every one of their own anchors,
    /// pivots, sizeDeltas and offsets exactly as authored. Because the dock's rect is the size the
    /// rect they were authored against, every stretch child resolves to the same size it always had
    /// and the ModBuild 232 collapse cannot occur to any of them. The one rect whose size this class
    /// must own is therefore the DOCK's, and it is captured BEFORE anything is moved.</para>
    ///
    /// <para><b>AND EVERY CHILD MOVES, NOT JUST THE DIALOG BOX.</b> See SECTION 6: the click that
    /// advances a page is <c>UICharacterStoryBox.skipButton</c> and this round could not prove from
    /// the log which branch of the window carries it. Moving the whole content set makes the question
    /// moot. The window ROOT keeps its own components and its own CanvasGroup — nothing is written to
    /// it, and it draws nothing on its own (no Graphic, per the ModBuild 235 identity line).</para>
    /// </summary>
    private static bool Park(UIWindow loadout, RectTransform hostRect, UIWindow story,
                             RectTransform storyRect, RectTransform image)
    {
        try
        {
            // MEASURED FIRST — the dock has to be the size of the rect the moved children were
            // authored against, and after the move the source rect is no longer the thing to ask.
            Vector2 measured = storyRect.rect.size;
            if (measured.x < MinParkSizePx || measured.y < MinParkSizePx)
                return false;
            if (storyRect.childCount == 0)
                return false;

            var img = image.GetComponent<Image>();
            _parkedSprite = img != null && img.sprite != null ? img.sprite.name : "<no Image>";

            var go = new GameObject(DockName, typeof(RectTransform));
            var dock = (RectTransform)go.transform;
            dock.SetParent(hostRect, worldPositionStays: false);
            dock.SetAsLastSibling();
            dock.anchorMin = new Vector2(0.5f, 0.5f);
            dock.anchorMax = new Vector2(0.5f, 0.5f);
            dock.pivot = new Vector2(0.5f, 0.5f);
            dock.sizeDelta = measured;             // ← the size the composite now owns
            dock.anchoredPosition = Vector2.zero;
            dock.localRotation = Quaternion.identity;
            dock.localScale = Vector3.one;
            // ignoreLayout, so a layout group on the host (if it ever grows one) never rebuilds with
            // this rect in its rectChildren — MapTravelConfirm's discipline.
            go.AddComponent<LayoutElement>().ignoreLayout = true;

            // THE STATE IS PUBLISHED BEFORE ANYTHING IS MOVED, so a throw half way through the move
            // is RECOVERABLE. Unpark returns immediately when _dock is null, and a dock full of the
            // story window's children with nobody holding a reference to it would strand the whole
            // dialog for the session — the exact class of leak the catch below exists to prevent.
            _dock = dock;
            _dockSize = measured;
            _parkHost = loadout;
            _parkSource = story;
            _picture = image;
            _slotValid = false;
            _composeLogged = false;

            Moved.Clear();
            MovedIndex.Clear();
            // Collected FIRST, because re-parenting mutates the child list underneath the walk.
            for (int i = storyRect.childCount - 1; i >= 0; i--)
            {
                Moved.Add(storyRect.GetChild(i));
                MovedIndex.Add(i);
            }
            // Moved back-to-front over the collected list, i.e. in ASCENDING original sibling order,
            // so the dock's draw order is the source window's draw order.
            for (int i = Moved.Count - 1; i >= 0; i--)
                Moved[i].SetParent(dock, worldPositionStays: false);

            WriteLayers(loadout.gameObject.layer);
            ApplyPose(hostRect);
            LogComposed(loadout, story, hostRect);
            return true;
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"STORY COMPOSITE: parking the story dialog threw ({e.GetType().Name}) "
                              + "— unwinding to the split presentation, which is the status quo.");
            Unpark("the park itself failed");
            return false;
        }
    }

    /// <summary>
    /// WRITE THE HOST ROOT'S OWN LAYER OVER THE WHOLE MOVED SUBTREE, recording what the game had
    /// there so the hand-back can put it back.
    ///
    /// <para><b>WHY IT IS NOT OPTIONAL AFTER THE INVERSION.</b> Per-window capture cameras cull BY
    /// LAYER ([[one-shared-layer-leaks]]), and the HOST is already converted when the dialog arrives —
    /// <c>CanvasConversion.ApplyModLayer</c> and <c>PanelSupersample.ApplyCaptureLayer</c> both
    /// re-sweep for late children, but only on their own cadence, so a subtree that arrives between
    /// sweeps is drawn by the wrong camera or by two. Up to ModBuild 235 this class relied on that
    /// cadence and could: the picture was parked into a window that had not been converted yet, so
    /// the INITIAL sweep covered it.</para>
    ///
    /// <para><b>A FOREIGN RENDER SUBTREE IS SKIPPED WHOLE</b>, which is
    /// <c>CanvasConversion.ApplyModLayer</c>'s own rule for its own reason: a real
    /// <c>Renderer</c> under a uGUI tree is 3D owned by another camera, and descending into it would
    /// take its children with it. <c>CanvasRenderer</c> is not a <c>Renderer</c>, so ordinary uGUI is
    /// unaffected.</para>
    /// </summary>
    private static void WriteLayers(int layer)
    {
        if (_dock == null)
            return;
        Layers.Begin(layer);
        _dock.gameObject.layer = layer;
        for (int i = 0; i < Moved.Count; i++)
        {
            Transform? m = Moved[i];
            if (m != null)
                Layers.Walk(m);
        }
    }

    /// <summary>
    /// HAS SOMEBODY ELSE PUT THE MOVED SUBTREE BACK ON ANOTHER LAYER? One int compare in the steady
    /// state, and it exists because of a real second writer rather than as belt-and-braces.
    ///
    /// <para>When the composite stands up over a story window the mod was ALREADY floating — the
    /// quest-start chain opens before the loadout screen exists, so this is the ordinary case for the
    /// first of a quest's two message chains — the refusal WITHDRAWS that float, and the release that
    /// follows runs <c>CanvasConversion.Release</c> on the STORY panel. That restores the story
    /// panel's own <c>Relayered</c> records (CanvasConversion.4.Lifecycle.cs:83), which NAME THE VERY
    /// TRANSFORMS THIS CLASS IS HOLDING and are written back with no "is it still mine" guard. The
    /// whole moved subtree therefore lands on the game's UI layer while it is sitting inside our
    /// host, and the host's own layer has not changed, so the edge test above would never notice.</para>
    ///
    /// <para>The records revert TOGETHER, so the first moved child is a sufficient sentinel. And the
    /// repair cannot start a write war: this class writes the HOST ROOT'S OWN live layer, which is
    /// exactly the value both <c>CanvasConversion.ApplyModLayer</c> and
    /// <c>PanelSupersample.ApplyCaptureLayer</c> want the subtree to have — and both of those are
    /// change-gated on <c>layer != theirs</c>, so neither of them records anything for a transform
    /// this class has already written. Exactly one owner, and the value it hands back on the unpark
    /// is the one the GAME gave.</para>
    /// </summary>
    private static bool LayersDrifted(int layer)
    {
        for (int i = 0; i < Moved.Count; i++)
        {
            Transform? m = Moved[i];
            if (m != null)
                return m.gameObject.layer != layer;
        }
        return false;
    }

    /// <summary>
    /// Put the dialog's TOP edge just under the picture's BOTTOM edge, both measured in the HOST
    /// window's own local space.
    ///
    /// <para>THE ZERO IS THE PICTURE, MEASURED, not a window edge and not a dial. The illustration is
    /// the one object we KNOW the dialog goes under, because it is the other half of the composite —
    /// so its bottom edge is the whole answer and there is nothing to sweep for. That is the
    /// difference from <c>MapTravelConfirm</c>, which has to sweep the window's content because it
    /// does not know what its member will sit under.</para>
    ///
    /// <para><b>ModBuild 234 — AND "THE DIALOG" IS ITS INK, NOT ITS RECT. THIS IS THE WHOLE OF THE
    /// SECOND REPORTED FAULT.</b> <i>"allerdings ist der Abstand zwischen dem Bild und dem Dialog
    /// viel zu groß, das Dialogfenster soll direkt darunter angezeigt werden."</i>
    /// <c>MapStoryController.dialogBox</c> is a <c>UICharacterStoryBox</c> whose RectTransform is a
    /// TALL, MOSTLY TRANSPARENT HOST; the strip the player sees is pinned to its BOTTOM. The fit line
    /// at Player.log:15304 measures both in the same breath —
    /// <c>'Map Story Window/Image' 1280x720px at (-640,540)</c> and
    /// <c>'Dialog/DialogContent' 1040x168px at (-520,-516)</c> — so the picture's bottom was 540 and
    /// the painted dialog's top was -348: <b>888 px of nothing</b>, against an
    /// <see cref="ImageGapPx"/> of 24 that was perfectly applied to the wrong edge.
    /// [[tight-box-is-not-the-rect]].</para>
    ///
    /// <para><b>SO BOTH EDGES ARE NOW INK, MEASURED WITH THE FIT'S OWN VERDICT.</b>
    /// <see cref="TryPaintedBounds"/> unions only the graphics
    /// <c>CanvasConversion.CountsAsFitContent</c> counts — the same test that produced the numbers
    /// above, so the placement and the host fit can no longer disagree about what is drawn. The
    /// picture's PAINTED BOTTOM goes <see cref="ImageGapPx"/> px above the dialog's PAINTED TOP; the
    /// offset between the picture's rect bottom and its ink is measured on the object and subtracted,
    /// so a sprite with transparent margin would be handled the same way.</para>
    ///
    /// <para><b>ModBuild 236 — IT POINTS THE OTHER WAY AND THE ZERO IS NOW MEASURED ONCE.</b> The
    /// picture is the fixed half now: it is the SLOT, and the slot is solved at the park and
    /// re-solved only when the host window's own frame or the picture's own rect changes size (a
    /// per-tick reading would make the dialog jump every time the window animates). The DIALOG's ink
    /// is still re-measured every tick, because that is the half that grows: the ModBuild 231 log
    /// shows the map story box fitting 1096x233 → 1096x289 as its text lands, and the composite's own
    /// fit shows the same painted strip at 168 px (three lines, story1.jpg) and 270 px (eight lines,
    /// story2.jpg).</para>
    ///
    /// <para><b>AND THE CORRECTION IS RELATIVE, WHICH IS WHAT MAKES IT PIVOT-FREE.</b> The dock holds
    /// a whole subtree whose ink sits wherever the game's own layout puts it inside a 1920x1080 rect;
    /// there is no pivot arithmetic that could predict it. So the ink is measured where it currently
    /// is, the delta to where it should be is computed, and that delta is added to the dock's
    /// <c>anchoredPosition</c>. Change-gated on <see cref="OffsetEpsilonPx"/> so a settled layout
    /// costs one comparison and no write.</para>
    /// </summary>
    private static void ApplyPose(RectTransform win)
    {
        if (_dock == null || _picture == null || _parkHost == null)
            return;

        ConvertedPanel? panel = ModalFallback.PanelFor(_parkHost);

        // ---- THE SLOT: the picture's painted bottom, solved ONCE per park -----------------------
        Vector2 hostSize = win.rect.size;
        Vector2 pictureSize = _picture.rect.size;
        if (!_slotValid || hostSize != _slotHostSize || pictureSize != _slotPictureSize)
        {
            if (TryPaintedBounds(_picture, win, panel, out Rect measuredSlot, out int slotCount)
                && slotCount > 0)
            {
                _slot = measuredSlot;
                _slotCount = slotCount;
                _slotValid = true;
                _slotHostSize = hostSize;
                _slotPictureSize = pictureSize;
                _poseBasis = $"the illustration's PAINTED bottom edge y={_slot.yMin:F0}, unioned from "
                             + $"{slotCount} drawn graphic(s) under '{_picture.name}' "
                             + $"({_slot.width:F0}x{_slot.height:F0} px of a {pictureSize.x:F0}x"
                             + $"{pictureSize.y:F0} px rect) inside a {hostSize.x:F0}x{hostSize.y:F0} "
                             + "px host window — MEASURED ONCE at the park and re-solved only when one "
                             + "of those two rects changes size";
            }
            else if (!_slotValid)
            {
                // FALLBACK, AND IT IS THE ModBuild 231-235 BEHAVIOUR VERBATIM: the authored rect.
                // "No graphic under the illustration passed the fit's visibility verdict" is a state
                // in which a dialog placed somewhere plausible beats no composite at all. The basis
                // string says which branch ran, so a hardware log never has to guess.
                Vector3[] corners = Corners;
                _picture.GetWorldCorners(corners);
                // GetWorldCorners: 0 BOTTOM-LEFT, 1 top-left, 2 top-right, 3 BOTTOM-RIGHT.
                Vector3 bl = win.InverseTransformPoint(corners[0]);
                Vector3 br = win.InverseTransformPoint(corners[3]);
                Vector3 tl = win.InverseTransformPoint(corners[1]);
                _slot = Rect.MinMaxRect(Mathf.Min(bl.x, br.x), Mathf.Min(bl.y, br.y),
                                        Mathf.Max(bl.x, br.x), Mathf.Max(tl.y, bl.y));
                _slotCount = 0;
                _slotValid = true;
                _slotHostSize = hostSize;
                _slotPictureSize = pictureSize;
                _poseBasis = $"the illustration's AUTHORED rect bottom edge y={_slot.yMin:F0} — NOT "
                             + $"its ink, because no graphic under '{_picture.name}' passed the fit's "
                             + "own visibility verdict at the instant of the park";
            }
        }
        if (!_slotValid)
            return;

        // ---- THE DIALOG: its ink, re-measured every tick, because it grows ----------------------
        if (!TryPaintedBounds(_dock, win, panel, out Rect ink, out int inkCount) || inkCount == 0)
            return;   // nothing drawn under the dock this tick: hold the last position, never jump

        float wantTop = _slot.yMin - ImageGapPx;
        var delta = new Vector2(_slot.center.x - ink.center.x, wantTop - ink.yMax);
        if (delta.sqrMagnitude <= OffsetEpsilonPx * OffsetEpsilonPx)
            return;
        _dock.anchoredPosition += delta;
    }

    private static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>Which edge <see cref="ApplyPose"/> last placed against — ink or authored rect.
    /// Printed by the BUILT line and by the gap falsifier so neither can assert a basis it did not
    /// use ([[an-instrument-can-assert-a-cause]]).</summary>
    private static string _poseBasis = "nothing has been placed yet";

    /// <summary>Scratch for <see cref="TryPaintedBounds"/>. One list, reused, never resized in the
    /// steady state — this runs once per tick per rect while the composite stands.</summary>
    private static readonly List<Graphic> PaintScratch = new(48);

    /// <summary>
    /// THE PAINTED BOUNDS OF A SUBTREE, in <paramref name="win"/>'s local (authored uGUI) space.
    ///
    /// <para><b>THE VISIBILITY VERDICT IS BORROWED, NOT RE-INVENTED.</b> With a
    /// <paramref name="panel"/> in hand this asks <c>CanvasConversion.CountsAsFitContent</c> — the
    /// fit's own four-reason test (culled / faint / empty / clipped out, plus the skip for this
    /// mod's own cue art). That matters for more than tidiness: the numbers this method produces are
    /// compared against the numbers the FIT produces in the same log, and a second, weaker predicate
    /// is exactly how <c>MrBacking.GlyphTrueRect</c> once unioned back in the text the fit had
    /// already judged invisible. Same lesson, same shared method.</para>
    ///
    /// <para><b>WITHOUT A PANEL IT FALLS BACK TO A MINIMAL LOCAL TEST</b>, and it has to: the park
    /// happens on the tick the story window OPENS, one tick before the conversion measures it, so
    /// there is genuinely no <c>ConvertedPanel</c> yet on the first call. The fallback is
    /// deliberately conservative (active, enabled, not culled, not transparent, non-degenerate) and
    /// the <paramref name="counted"/> figure is reported, so a caller can see how much it was
    /// working from.</para>
    ///
    /// <para><b>FOUR SWEEPS OF THIS SHAPE EXIST AND THEY ARE DELIBERATELY NOT ONE</b> (refactor
    /// 2026-09, REVIEW-worldui-front.md F6): this one, <c>LoadoutConfirmPark.TryPaintedBounds</c>
    /// (a null-tolerant subtree EXCLUDE), <c>LoadoutConfirmPark.TryFreeLane</c>'s lane sweep
    /// (per-graphic extents, clipped to the uGUI clippers) and
    /// <c>EnchantressComposite.TryPaintedBounds</c> (the PLATE test and the leftmost-ink
    /// attribution). Each difference is a recorded hardware fix, and the parts that were genuinely
    /// identical — the moved-subtree layer record and the floated-window lookup — are the ones that
    /// were shared instead.</para>
    /// </summary>
    private static bool TryPaintedBounds(RectTransform root, RectTransform win,
                                         ConvertedPanel? panel, out Rect local, out int counted)
    {
        local = default;
        counted = 0;
        try
        {
            PaintScratch.Clear();
            root.GetComponentsInChildren(includeInactive: false, PaintScratch);
            if (PaintScratch.Count == 0)
                return false;
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
                if (panel != null)
                {
                    if (!CanvasConversion.CountsAsFitContent(panel, g))
                        continue;
                }
                else if (!CountsAsPaintedHere(g))
                {
                    continue;
                }
                var rt = g.rectTransform;
                if (rt == null)
                    continue;
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
            // A measurement is never worth a throw reaching the modal tick. The caller falls back to
            // the authored rect and says so.
            PaintScratch.Clear();
            counted = 0;
            return false;
        }
    }

    /// <summary>The minimal "is this drawn?" test, used ONLY when there is no converted panel to ask
    /// the fit's own verdict of (the first tick of a park). Deliberately weaker and deliberately
    /// stated as such.</summary>
    private static bool CountsAsPaintedHere(Graphic g) =>
        g.enabled && g.gameObject.activeInHierarchy
        && (g.canvasRenderer == null || !g.canvasRenderer.cull)
        && g.color.a > 0.02f
        && !g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal);

    private static void Unpark(string why)
    {
        if (_dock == null)
        {
            _parkHost = null;
            _parkSource = null;
            _picture = null;
            Moved.Clear();
            MovedIndex.Clear();
            Layers.Forget();
            _slotValid = false;
            return;
        }
        RectTransform dock = _dock;
        UIWindow? source = _parkSource;
        int moved = Moved.Count;
        _dock = null;
        _parkHost = null;
        _parkSource = null;
        _picture = null;
        _composeLogged = false;
        _dockSize = Vector2.zero;
        _slotValid = false;
        try
        {
            // THE LAYERS GO BACK FIRST, while the transforms are still reachable and still ours.
            Layers.Restore();

            Transform? home = source != null ? source.transform : null;
            if (home != null)
            {
                // ASCENDING original sibling index, so the source window's own draw order is
                // reproduced exactly. SetParent(worldPositionStays: false) never touched their
                // anchors, pivots, sizeDeltas or offsets, so parent + index IS the whole hand-back.
                for (int i = Moved.Count - 1; i >= 0; i--)
                {
                    Transform? m = Moved[i];
                    if (m == null)
                        continue;
                    m.SetParent(home, worldPositionStays: false);
                    m.SetSiblingIndex(Mathf.Clamp(MovedIndex[i], 0,
                                                  Mathf.Max(0, home.childCount - 1)));
                }
            }
            VRLog.Info(Scope, $"STORY COMPOSITE UNPARKED — {why}. The story window's {moved} root "
                              + "child object(s) are back under "
                              + $"'{(source != null ? source.name : "<the story window is gone>")}' "
                              + "at their original sibling indices, with their authored anchors, pivots, "
                              + "sizeDeltas, offsets, scales and LAYERS restored — the moves were "
                              + "SetParent(worldPositionStays: false) into a dock of exactly the source "
                              + "rect's size, so not one of those fields was ever written and there is "
                              + "nothing to get wrong on the way back. "
                              + "FROM THIS TICK the dialog is inside the story window again and follows "
                              + "that window's own CanvasGroup: when the GAME has closed it (the ordinary "
                              + "end of the intro) that means it simply disappears, which is the user's "
                              + "\"Da der Dialog fertig ist soll er darin verschwinden\". THE CLAIM ON THE "
                              + "STORY WINDOW GOES WITH IT: the refusal is conditional on this park and "
                              + "lapses on the same tick. THE HOST IS NOT TOUCHED AT ALL — the loadout "
                              + "window keeps floating, in the same panel and the same pose, and its "
                              + "paper-expand tween and confirm button were never ours.");
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"STORY COMPOSITE: handing the story dialog back threw ({e.GetType().Name}) "
                              + "— some of it may still be under the loadout window's dock. It is the "
                              + "GAME's own content and the game re-parents nothing, so the next "
                              + "MapStoryController.Show re-lays out whatever made it home. The dock is "
                              + "NOT destroyed while anything is still inside it (see the finally "
                              + "below), because destroying it would take the game's own story UI with "
                              + "it — a presentation bug must never become a missing object.");
        }
        finally
        {
            Moved.Clear();
            MovedIndex.Clear();
            // THE DOCK IS DESTROYED ONLY WHEN IT IS EMPTY. If the source window was destroyed under
            // us (a scene change) the children have nowhere to go home to and are still inside it,
            // and Destroy would take the game's own objects with them. An empty mod-owned rect left
            // under the loadout window costs nothing and goes with that window; a destroyed story box
            // costs the feature for the session.
            if (dock != null)
            {
                if (dock.childCount == 0)
                {
                    Object.Destroy(dock.gameObject);
                }
                else
                {
                    VRLog.Warn(Scope, $"STORY COMPOSITE: the dock still holds {dock.childCount} of the "
                                      + "story window's own object(s) after the hand-back, so it is "
                                      + "being LEFT IN PLACE rather than destroyed — almost always "
                                      + "because the story window itself was destroyed with a scene "
                                      + "change and there is no home to return them to. They keep "
                                      + "drawing inside the loadout window until that window goes.");
                }
            }
        }
    }

    /// <summary>THE ONE LINE the round asked for: what was composed, out of which two windows, which
    /// one is the HOST, what the layout came out as, and whether the result is shared. Everything in
    /// it is MEASURED this frame — no clause claims a mechanism this method cannot see.</summary>
    private static void LogComposed(UIWindow loadout, UIWindow story, RectTransform win)
    {
        if (_composeLogged || _dock == null)
            return;
        _composeLogged = true;
        SharedWindowKind kind = SharedWindows.KindOf(loadout);
        bool shared = SharedWindows.IsShared(loadout);
        Vector2 pos = _dock.anchoredPosition;
        VRLog.Info(Scope, $"STORY COMPOSITE BUILT: '{loadout.name}' (ID {loadout.ID}, component "
                          + "UILoadoutManager — THE HOST, and the window that is still standing when "
                          + $"the continue control appears) and '{story.name}' (ID {story.ID}, component "
                          + "MapStoryController — the DIALOG) are now ONE window. ModBuild 236 INVERTED "
                          + "THIS: up to ModBuild 235 the story window hosted and the illustration was "
                          + "moved into it, and the log of that build shows why that could not answer the "
                          + "report — the host is CLOSED BY THE GAME seconds later (:3875) and the "
                          + "loadout screen then floats as a fresh window in a fresh pose (:3788). THE "
                          + $"DIALOG CAME FROM THE STORY WINDOW: {Moved.Count} root child object(s) moved "
                          + $"into '{DockName}', a mod-owned container sized to that window's own "
                          + $"{_dockSize.x:F0}x{_dockSize.y:F0} px rect so every one of them resolves its "
                          + "authored anchors against exactly the rect it was authored against. THE "
                          + "ILLUSTRATION IS NOT MOVED AT ALL and that deletes the whole ModBuild 232 "
                          + $"failure mode: '{(_picture != null ? _picture.name : "<none>")}' carrying "
                          + $"sprite '{_parkedSprite}' stays where UILoadoutQuestWindow.imagePaper put it "
                          + "(UILoadoutQuestWindow.cs:55-63, StoryImageViewer.cs:233). LAYOUT: the dock "
                          + $"sits under '{win.name}' at anchored ({pos.x:F0},{pos.y:F0}) px, with the "
                          + $"dialog's PAINTED top edge {ImageGapPx:F0} px below the illustration's "
                          + $"PAINTED bottom edge, placed against {_poseBasis} — the slot was unioned "
                          + $"from {_slotCount} drawn graphic(s) and is re-solved only when the host or "
                          + "the picture changes size; see STORY COMPOSITE GAP for the millimetres. "
                          + $"LAYER: the whole moved subtree was written onto the host root's own layer "
                          + $"{Layers.Written} over {Layers.Count} transform(s), {Layers.Skipped} foreign "
                          + "render subtree(s) skipped whole, because per-window capture cameras cull BY "
                          + $"LAYER. SHARED: {shared} (kind {kind}) — ModBuild 237 MOVES the MapStory "
                          + "kind onto this host for the life of the composite, so this panel wears the "
                          + "SHARED BLUE bar, its pose travels on record 21 and it does not re-face on "
                          + "release; the identity hands back the instant the claim lapses, i.e. BEFORE "
                          + "the battle-goal phase, because that screen's CONTENT is private per player. "
                          + "False here is the defect and not a design: it means SharedWindowIdentity "
                          + "could not resolve a grab frame for this window, or the swap is still "
                          + "deferred behind a hand — grep STORY WINDOW SHARED for which. Note this "
                          + "clause is read at the PARK, one tick before the identity tick that follows "
                          + "it, so a False on the very first BUILT line is expected and the standing "
                          + "falsifier is the one to believe. The story PAGE, TEXT and finished bit were "
                          + "never part of any of this, because Net/RemoteMapStory resolves the box "
                          + "through MapStoryController.dialogBox and never through SharedWindows. THE "
                          + "STORY WINDOW NOW STANDS DOWN: see STORY COMPOSITE CLAIM RAISED for the "
                          + "refusal, STORY COMPOSITE ONE WINDOW for whether that produced one window, "
                          + "and STORY WINDOW CONTINUITY for whether it is still the same window when "
                          + "the button arrives. USER RULING: \"Es soll immer noch das exakt gleiche "
                          + "Fenster sein wo auch die Story drin erzählt wurde.\"");
    }

    /// <summary>Module teardown. Hands the picture back and unlocks the destinations — leaving a
    /// game object parked under a mod host, or a permanently grey merchant, across a scene change is
    /// how a presentation bug becomes a save-game one.</summary>
    internal static void Reset()
    {
        Unpark("module teardown");
        // ModBuild 237 — AND THE SHARED IDENTITY GOES WITH IT, for the same reason the claim does: an
        // identity that outlived this class would leave SharedWindowKind.MapStory pointing at a
        // loadout window nobody is composing into, i.e. a blue bar and a published pose on a private
        // window. It is a plain field drop; nothing is moved and nothing is written to the game.
        SharedWindowIdentity.Reset();
        // ModBuild 235 — and the confirm parker goes with it, for the reason its own Reset states: a
        // park claim that outlived this class would keep the game's own confirm off screen with
        // nothing left to draw it.
        LoadoutConfirmPark.Reset();
        // ModBuild 238 — and the quest-journey curtain, for the same reason as everything else in
        // this block: a refusal that outlived this class would be a suppression with no owner.
        QuestJourneyCurtain.Reset();
        // ModBuild 381 — and the tutorial hint goes back to 'Introduction Canvas' before the scene
        // changes. A game window left parented under a mod host is exactly the class of leak this
        // block exists for, and here it is sharper than usual: re-parenting moves the object into
        // the host's scene ([[adopting-a-window-takes-its-lifetime]]), so a hint still parked at a
        // scene load would take UIIntroductionManager's own layout group with it.
        HintOnOwnerComposite.Reset();
        _backdropStanding = false;
        _backdropObject = null;
        _backdropLifted = false;
        _backdropCycles = 0;
        _backdropCapReported = false;
        _backdropVerdict = string.Empty;
        _backdropReports = 0;
        // ModBuild 364 — and the mutual-hold arm with it, for this block's own reason: a hold measured
        // in a session that no longer exists is not evidence about the next one.
        _backdropSubject = null;
        _backdropHonestySettle = 0;
        _backdropSettleReported = false;
        _mutualHoldTicks = 0;
        _mutualHoldReported = false;
        _mutualHoldLifted = false;
        _mutualHoldSince = 0;
        _mutualHoldOutcomeReported = false;
        _handoverDone = false;
        _backdropWhy = "the loadout backdrop claim has been torn down with the module";
        _storyGameOpenAt = float.NegativeInfinity;
        if (_gateOpen)
            CloseGate();
        Greyed.Clear();
        GreyedHadHighlight.Clear();
        _cityRequest = null;
        _gateOpen = false;
        _closedAtOpen = 0;
        _bridgeTicksLeft = 0;
        _bridgeReported = false;
        _heldThisPass = 0;
        _heldLastPass = 0;
        _heldTotal = 0;
        _heldLastName = string.Empty;
        _holdLifted = false;
        EdgeClosed.Clear();
        NamedSet.Clear();
        NamedWhy.Clear();
        _pickHost = null;
        _pickReported = string.Empty;
        _pickReports = 0;
        // THE CLAIM MUST DIE WITH THE MODULE. A standing claim that outlived this class would be a
        // loadout screen the refusal table keeps refusing while nothing is drawing it anywhere —
        // the one failure mode this whole design is built to be incapable of.
        _claimStanding = false;
        _claimObject = null;
        _claimHost = null;
        _claimCycles = 0;
        _claimCapReported = false;
        _claimWhy = "the quest-intro composite has been torn down with the module";
        _oneWindowVerdict = string.Empty;
        _oneWindowReports = 0;
        _dockSize = Vector2.zero;
        _parkedSprite = string.Empty;
        _picture = null;
        _parkSource = null;
        _slotValid = false;
        _slotCount = 0;
        _floorLifted = false;
        _noHostReported = null;
        _contHave = false;
        _contGrabbed = false;
        _contVerdict = string.Empty;
        _contReports = 0;
        Moved.Clear();
        MovedIndex.Clear();
        Layers.Reset();
        // ModBuild 234 — AND THE CURTAIN MUST DIE WITH THE MODULE FOR THE SAME REASON THE LOADOUT
        // CLAIM MUST: a standing refusal that outlived this class would hold a set of windows out of
        // the float set with nobody left to lapse it. CloseCurtain is called through CloseGate above
        // when the gate was open; these lines are the belt for the case where it was not.
        CloseCurtain("module teardown");
        CurtainMembers.Clear();
        _curtainStanding = false;
        _curtainLifted = false;
        _curtainCycles = 0;
        _curtainCapReported = false;
        _curtainHeldAt = 0f;
        _curtainNames = "none";
        _curtainWhy = "the quest-intro story curtain has been torn down with the module";
        _onlyWindowVerdict = string.Empty;
        _onlyWindowReports = 0;
        _gapVerdict = string.Empty;
        _gapReports = 0;
        _poseBasis = "nothing has been placed yet";
        FloatScratch.Clear();
        PaintScratch.Clear();
    }
}
