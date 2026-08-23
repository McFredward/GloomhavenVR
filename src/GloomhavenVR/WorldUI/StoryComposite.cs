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
/// <see cref="HoldsLoadoutFloatBack"/>. The difference from 231's convert-loop hold is not a matter
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
/// </list>
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
    private const float ImageGapPx = 24f;

    /// <summary>Below this the re-place is skipped, in authored uGUI px. Same purpose as
    /// <c>MapTravelConfirm</c>'s <c>OffsetEpsilon</c>: the layout settles to a value that is not
    /// bit-identical frame to frame, and writing it back every frame would dirty the host rect and
    /// keep the panel's content fit re-measuring forever.</summary>
    private const float OffsetEpsilonPx = 0.5f;

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

    private static RectTransform? _parked;
    private static Transform? _home;
    private static int _homeIndex;
    private static Vector2 _homeAnchorMin;
    private static Vector2 _homeAnchorMax;
    private static Vector2 _homePivot;
    private static Vector2 _homeAnchoredPos;
    private static Vector2 _homeSizeDelta;
    private static Quaternion _homeRotation = Quaternion.identity;
    private static Vector3 _homeScale = Vector3.one;
    private static LayoutElement? _addedIgnore;
    private static UIWindow? _parkHost;
    private static bool _composeLogged;

    /// <summary>The size the composite OWNS for the parked picture, in the story window's authored
    /// uGUI px, measured live at the instant of the park (see <see cref="Park"/>). It is written
    /// into the rect's <c>sizeDelta</c> and re-asserted if anything degenerates it, which is what
    /// makes the composite independent of both the async sprite and the paper-expand tween.</summary>
    private static Vector2 _parkedSize;

    /// <summary>The name of the sprite that was on the picture when it was parked — printed by the
    /// BUILT line and by the falsifier, because "0x0 px" and "NOT LOADED YET" are the two ways this
    /// feature has already shipped broken and both belong in the same line.</summary>
    private static string _parkedSprite = string.Empty;

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

    // ---- the loadout-float claim (ModBuild 233) -----------------------------------------------

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
    /// <para>Past the cap the claim is dead for this loadout window: both halves float, which is the
    /// ModBuild 232 presentation and therefore a known-survivable one, and
    /// <c>STORY COMPOSITE CLAIM CAPPED</c> says so exactly once.</para>
    /// </summary>
    private const int MaxWithdrawCycles = 2;

    /// <summary>The live level. Recomputed from measurement in <see cref="Tick"/> every tick and
    /// never latched — <see cref="HoldsLoadoutFloatBack"/> is a PURE read of it, because
    /// <c>FloatRefusalTable.Refuses</c> is re-entered several times per tick from a recursive
    /// ancestor walk and every caller must get the same answer.</summary>
    private static bool _claimStanding;

    /// <summary>The exact loadout window GameObject the claim is about. A claim about one object
    /// must never refuse another — <c>UILoadoutManager</c> is a Singleton and the refusal row asks
    /// by GameObject for the same reason the ready-toggle row does.</summary>
    private static GameObject? _claimObject;

    /// <summary>The loadout window instance <see cref="_claimCycles"/> is counted against, so a new
    /// quest starts from a clean budget.</summary>
    private static UIWindow? _claimHost;
    private static int _claimCycles;
    private static bool _claimCapReported;
    private static string _claimWhy = "the quest-intro composite has never claimed the loadout screen";

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
    internal static bool PointOfNoReturn => _curtainStanding || LoadoutScreenOpen;

    /// <summary>The ModBuild 233 level, unchanged and still the clause that holds the gate up for the
    /// whole pre-scenario interval.</summary>
    private static bool LoadoutScreenOpen =>
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
    /// clause of <see cref="TickLoadoutClaim"/>'s <c>want</c> stayed measured-true — and
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

        UIWindow? story = StoryWindow();
        UIWindow? loadout = LoadoutWindow();

        // ModBuild 234 — THE CURTAIN LEVEL IS COMPUTED FIRST, BECAUSE THE GATE NOW READS IT.
        // PointOfNoReturn is the UNION of "the story curtain stands" and "the loadout screen is up",
        // so the curtain's own level has to be settled before the gate edge below is evaluated
        // against it. TickCurtain closes nothing and writes nothing to the game: it raises or drops
        // a presentation refusal over a set of window instances frozen at its own rising edge.
        TickCurtain(story, loadout);

        bool gateWanted = PointOfNoReturn;
        // The composite exists only while BOTH halves do. Outside that the image belongs where the
        // game put it.
        bool composeWanted = gateWanted && story != null && loadout != null && loadout.IsOpen;

        if (composeWanted)
            EnsureParked(story!, loadout!);
        else
            Unpark(story == null ? "the story box closed"
                   : loadout == null || !loadout.IsOpen ? "the loadout screen closed"
                   : "the 3D map room stood down");

        // ModBuild 235 — THE CONFIRM PARKER, BEFORE THE CLAIM THAT USED TO HIDE IT.
        //
        // The order is load-bearing in one direction only: TickLoadoutClaim asks
        // LoadoutConfirmPark.GameWantsConfirmShown() as one of its terminators, and that read is PURE
        // (it resolves the control and reads two game fields; it never looks at the park), so it
        // would give the same answer either side of this call. What running the parker FIRST buys is
        // that on the tick the game switches the confirm on, the control is already parked into the
        // Character-UI before anything downstream measures whether it is reachable — one fewer frame
        // in which the falsifier could truthfully report a button nobody can see.
        //
        // IT IS CALLED FROM HERE RATHER THAN FROM ModalFallback because this is the earliest per-tick
        // point that runs BEFORE the release loop, and the release loop destroys the host GameObject
        // the parker parks into. Same call site, same reason, as this method's own unpark.
        LoadoutConfirmPark.Tick();

        // ModBuild 233 — THE CLAIM AND THE FALSIFIER, IN THAT ORDER AND BOTH FROM MEASUREMENT.
        // The claim decides whether the loadout screen is drawn by us this tick; the falsifier then
        // states, from a fresh read of the float set, whether the result is actually ONE window.
        TickLoadoutClaim(loadout);
        ReportOneWindow(story, loadout);
        // ModBuild 234's two falsifiers, both re-measured from the live objects on this tick and
        // neither of them reading any state this class set: "is the story box the ONLY floated
        // window" and "is the dialog actually directly under the picture, in millimetres".
        ReportOnlyWindow(story);
        ReportGap();

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
    /// <para>The answer is a level computed once per tick by <see cref="TickLoadoutClaim"/> from
    /// live measurement, and it is asked ABOUT A SPECIFIC GameObject: <c>UILoadoutManager</c> is a
    /// Singleton and a claim about one open of it must never refuse another.</para>
    ///
    /// <para><b>THE FAIL DIRECTION IS FIXED AND IT IS THE ONLY THING THAT MATTERS HERE.</b> Every
    /// clause of the claim is a POSITIVE fact about the composite standing (a picture parked, with a
    /// non-degenerate rect and a loaded sprite, under a story window the mod is floating). If any of
    /// them stops being true — the sprite never arrives, the story box closes, the park throws, this
    /// class stops running — the claim is false within one tick and the loadout screen floats with
    /// everything on it. There is no state in which this method can hide a window that is not being
    /// shown somewhere else. [[parent-wins-needs-a-real-parent]]: suppressing X because Y handles it
    /// requires Y to ACTUALLY handle it, so the test is "Y is handling it", not "Y ought to be".
    /// </para>
    /// </summary>
    internal static bool HoldsLoadoutFloatBack(GameObject? loadoutWindowObject) =>
        _claimStanding && loadoutWindowObject != null
        && ReferenceEquals(loadoutWindowObject, _claimObject);

    /// <summary>The claim's own words for what it did with the loadout screen, printed verbatim by
    /// the refusal line and by the lapse warning. Never null.</summary>
    internal static string LoadoutClaimWhy => _claimWhy;

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
    /// claim last longer — and the claim's own park clauses, the cycle cap, the deadlock floor and
    /// <see cref="LoadoutConfirmPark"/> drawing the control somewhere else all still bound it. There is
    /// no failure of this method that can reproduce the ModBuild 234 state, because that state
    /// required the mod to be the ONLY thing showing the story box, which
    /// <see cref="StoryWindow"/> no longer allows.</para>
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
    /// <para>The claim stands only while <b>all</b> of these are measured true this tick: the
    /// picture is parked (<see cref="_parked"/>), its rect is non-degenerate in both axes, its
    /// GameObject is active in the hierarchy, its <c>Image</c> has a sprite, its parent is the story
    /// window it was parked under, and the mod is FLOATING that story window. The last clause is the
    /// one that makes the refusal honest: a story window that is parked into but not floated is not
    /// on screen, and refusing the loadout screen then would show the player nothing at all.</para>
    /// </summary>
    private static void TickLoadoutClaim(UIWindow? loadout)
    {
        // A new loadout window instance gets a fresh cycle budget: the cap exists to protect ONE
        // window's churn record inside ONE 60 s fuse window, not to ration the feature.
        if (!ReferenceEquals(loadout, _claimHost))
        {
            _claimHost = loadout;
            _claimCycles = 0;
            _claimCapReported = false;
        }

        bool want = false;
        string why = string.Empty;
        // ModBuild 235 — THE TWO TERMINATORS THAT ARE READ OFF THE GAME, EVALUATED FIRST AND
        // RECORDED, so the LAPSED line can name which fact ended the claim instead of saying only
        // that it ended. See TerminatedBy for why they exist and why neither could be inferred from
        // anything this class already had.
        string? terminator = TerminatedBy(loadout);
        if (terminator == null && loadout != null && _parked != null && _parkHost != null)
        {
            Vector2 size = _parked.rect.size;
            var img = _parked.GetComponent<Image>();
            Sprite? spriteOn = img != null ? img.sprite : null;
            bool sized = size.x >= MinParkSizePx && size.y >= MinParkSizePx;
            bool active = _parked.gameObject.activeInHierarchy;
            bool underHost = _parked.parent != null
                             && ReferenceEquals(_parked.parent, _parkHost.transform);
            bool storyFloated = FloatedByMod(_parkHost);
            want = sized && active && spriteOn != null && underHost && storyFloated;
            if (want)
                why = $"the quest illustration ('{_parked.name}', {size.x:F0}x{size.y:F0} px, sprite "
                      + $"'{spriteOn!.name}') has been moved INTO the floated story window "
                      + $"'{_parkHost.name}', with MapStoryController's dialog box directly under it "
                      + "— one panel, one blue shared grab bar, no close cross. The loadout screen "
                      + "has nothing left on it that the player can read during the intro";
        }

        if (want && !_claimStanding)
        {
            if (_claimCycles >= MaxWithdrawCycles)
            {
                if (!_claimCapReported)
                {
                    _claimCapReported = true;
                    VRLog.Warn(Scope, $"STORY COMPOSITE CLAIM CAPPED — the composite has already asked "
                                      + $"{_claimCycles} time(s) for '{(loadout != null ? loadout.name : "<none>")}' "
                                      + "to stand down and it will not ask again for this window. THE CAP IS "
                                      + "DERIVED, NOT CHOSEN: ModalFallback's catch-all churn fuse suppresses a "
                                      + "window's NAME for the whole session on its 4th enrolment inside 60 s, a "
                                      + "REFUSED window is never counted, and each claim that falls costs exactly "
                                      + $"one re-float — so {MaxWithdrawCycles} cycles plus the first float is 3, "
                                      + "one under the fuse. FROM HERE BOTH HALVES FLOAT, which is the ModBuild "
                                      + "232 presentation: two windows, ugly, and NOT a deadlock. IF YOU ARE "
                                      + "READING THIS IN A HARDWARE LOG the composite is flapping — grep "
                                      + "STORY COMPOSITE UNPARKED for what kept taking the picture away.");
                }
                want = false;
            }
            else
            {
                _claimCycles++;
                _claimWhy = why;
                _claimObject = loadout!.gameObject;
                VRLog.Info(Scope, $"STORY COMPOSITE CLAIM RAISED (cycle {_claimCycles} of "
                                  + $"{MaxWithdrawCycles}) on '{loadout.name}': {why}. FloatRefusalTable's "
                                  + "conditional row now refuses to float it, and because that refusal is "
                                  + "asked at the TOP of the catch-all loop it also WITHDRAWS the float it "
                                  + "already has (ModalFallback.10.CatchAll.cs:234 → WithdrawRefusedFloat: "
                                  + "Sticky dropped, ordinary release loop takes panel + grab bar + slot "
                                  + "down, CanvasConversion.Release restores the exact 2D home). NOTHING IS "
                                  + "WRITTEN TO THE GAME — no Hide, no Escape, no SetActive, no CanvasGroup — "
                                  + "and this is NOT ModBuild 231's convert-loop hold: a refused window is "
                                  + "never enrolled and therefore never counted by the churn fuse.");
            }
        }
        else if (!want && _claimStanding)
        {
            VRLog.Info(Scope, $"STORY COMPOSITE CLAIM LAPSED on "
                              + $"'{(loadout != null ? loadout.name : "<the loadout window is gone>")}' — "
                              + (terminator ?? "the composite is no longer standing")
                              + ", so the loadout screen floats again from "
                              + "this tick with everything on it (its own paper-expand tween, its confirm "
                              + "button and its hotkeys were never touched). This is the level ending, not a "
                              + "failure: the ordinary end of it is the player clicking through the last "
                              + $"page of the intro. {_claimCycles} of {MaxWithdrawCycles} cycle(s) used. "
                              + "GREP THIS STRING: it is ABSENT from the whole ModBuild 234 log, and that "
                              + "absence IS the reported deadlock — the claim outlived the intro and took "
                              + "the single-player continue button off screen with the window it was "
                              + "refusing.");
            _claimObject = null;
            _claimWhy = "the quest-intro composite has stood down; the loadout screen is nobody's "
                        + "responsibility but its own";
        }
        _claimStanding = want;
    }

    // ---- the story curtain (ModBuild 234) ------------------------------------------------------

    /// <summary>
    /// THE FROZEN SET. Every window the mod was floating at the curtain's rising edge, minus the
    /// story box (the one window that must stay) and minus the loadout screen (which has a claim of
    /// its own, <see cref="HoldsLoadoutFloatBack"/>). Filled ONCE, in <see cref="RaiseCurtain"/>,
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
    /// <c>FloatRefusalTable</c> must ask, and — like <see cref="HoldsLoadoutFloatBack"/> — a PURE
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
            // (HoldsLoadoutFloatBack) for part of the same interval. Under the old clause the curtain
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
        if (story == null && _parked == null)
            return;   // no intro composite is being attempted this tick; nothing to falsify

        int total = ModalFallback.CountFloatsOtherThan(null);
        bool storyFloated = story != null && FloatedByMod(story, total);
        bool loadoutFloated = FloatedByMod(loadout, total);
        Vector2 size = _parked != null ? _parked.rect.size : Vector2.zero;
        var img = _parked != null ? _parked.GetComponent<Image>() : null;
        Sprite? sprite = img != null ? img.sprite : null;
        bool active = _parked != null && _parked.gameObject.activeInHierarchy;
        bool sized = size.x >= MinParkSizePx && size.y >= MinParkSizePx;

        bool ok = storyFloated && !loadoutFloated && _parked != null && sized && active
                  && sprite != null;

        string measured =
            $"story box '{(story != null ? story.name : "<none>")}' floated={storyFloated}; "
            + $"loadout screen '{loadout.name}' floated={loadoutFloated}; parked picture "
            + $"{(_parked != null ? "'" + _parked.name + "'" : "<nothing parked>")} "
            + $"{size.x:F0}x{size.y:F0} px, activeInHierarchy={active}, sprite="
            + $"{(sprite != null ? "'" + sprite.name + "'" : "NONE")}; the mod is floating {total} "
            + "window(s) in total (the map room's permanent quest log is one of them and is not part "
            + "of this claim)";

        string verdict = ok ? "CONFIRMED" : "NOT ACHIEVED";
        if (verdict == _oneWindowVerdict || _oneWindowReports >= MaxOneWindowReports)
            return;
        _oneWindowVerdict = verdict;
        _oneWindowReports++;

        if (ok)
        {
            VRLog.Info(Scope, "STORY COMPOSITE ONE WINDOW: CONFIRMED — the quest intro is ONE floated "
                              + "window. MEASURED THIS TICK: " + measured + ". That is the user's ruling "
                              + "satisfied on all four counts: one window, the picture ABOVE the dialog, "
                              + "the shared blue MapStory bar, and no close cross (the story window is in "
                              + "ModalFallback.8.Convert's isStoryBox no-X family). USER REPORT THIS LINE "
                              + "ANSWERS: \"Das Dialogfenster der Story und das Fenster mit dem Bild sind "
                              + "immer noch zwei getrennte Fenster. Außerdem ist die Zeichnung/Bild nicht "
                              + "mehr richtig zu sehen!\"");
            return;
        }
        VRLog.Warn(Scope, "STORY COMPOSITE ONE WINDOW: NOT ACHIEVED — the quest intro is not one window "
                          + "with a visible picture yet. MEASURED THIS TICK: " + measured + ". READ IT "
                          + "LIKE THIS: loadout floated=True means FloatRefusalTable's UILoadoutManager "
                          + "row did not fire — either the row is not in the table on this build (it is "
                          + "the one change this feature needs outside StoryComposite.cs) or "
                          + "StoryComposite.HoldsLoadoutFloatBack returned false, and the CLAIM lines "
                          + "above say which. A 0x0 or sprite=NONE picture means the park ran before the "
                          + "addressable landed, which is the ModBuild 232 defect. story floated=False "
                          + "means the host itself is not on screen and nothing should have been refused.");
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
    private static void ReportOnlyWindow(UIWindow? story)
    {
        if (!_curtainStanding)
        {
            _onlyWindowVerdict = string.Empty;
            _onlyWindowReports = 0;
            return;
        }
        FloatScratch.Clear();
        int others = ModalFallback.CollectFloatedWindows(FloatScratch, story);
        bool storyFloated = story != null && FloatedByMod(story);
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

        string measured = $"story box '{(story != null ? story.name : "<none>")}' floated="
                          + $"{storyFloated}; OTHER floated window(s): {others} [{standing}]; the "
                          + $"curtain's frozen member set is [{_curtainNames}]";
        if (ok)
        {
            VRLog.Info(Scope, "STORY CURTAIN ONLY WINDOW: CONFIRMED — the story box is the ONLY window "
                              + "the mod is floating. MEASURED THIS TICK: " + measured + ". USER RULING "
                              + "THIS LINE ANSWERS: \"Ich möchte aber das zu diesem Zeitpunkt alle "
                              + "anderen Fenster verschwinden und nur dieses Fenster sichtbar ist (Point "
                              + "of no return überschritten).\" Nothing was written to the game to "
                              + "achieve it: the members' floats are withheld and they come back, with "
                              + "everything on them, when the curtain lapses.");
            return;
        }
        VRLog.Warn(Scope, "STORY CURTAIN ONLY WINDOW: NOT ACHIEVED — something other than the story box "
                          + "is still floating past the point of no return. MEASURED THIS TICK: "
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
                          + "edge, not by widening the set afterwards. story floated=False means the "
                          + "host itself is not on screen and the curtain should already have lapsed.");
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
    /// <para>GREP: <c>STORY COMPOSITE GAP: CONFIRMED</c> — the fix.
    /// <c>STORY COMPOSITE GAP: TOO FAR</c> — the complaint, with the measured millimetres.</para>
    /// </summary>
    private static void ReportGap()
    {
        if (_parked == null || _parkHost == null)
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
        if (win == null || !Singleton<MapStoryController>.IsInitialized)
            return;
        MapStoryController mc = Singleton<MapStoryController>.Instance;
        var dialog = mc != null && mc.dialogBox != null
            ? mc.dialogBox.transform as RectTransform
            : null;
        if (dialog == null || !dialog.gameObject.activeInHierarchy)
            return;

        ConvertedPanel? panel = ModalFallback.PanelFor(_parkHost);
        if (!TryPaintedBounds(dialog, win, panel, out Rect ink, out int nDialog) || nDialog == 0)
            return;
        if (!TryPaintedBounds(_parked, win, panel, out Rect pic, out int nPic) || nPic == 0)
            return;

        float gapPx = pic.yMin - ink.yMax;
        float rig = RigScale();
        float mm = Mathf.Abs(gapPx) * Mathf.Abs(win.lossyScale.y) / Mathf.Max(rig, 1e-4f) * 1000f;
        bool scaleKnown = rig > 1e-3f && Mathf.Abs(win.lossyScale.y) > 1e-6f;
        bool ok = gapPx >= -OffsetEpsilonPx
                  && (scaleKnown ? mm <= MaxGapMillimetres
                                 : Mathf.Abs(gapPx) <= ImageGapPx * 3f);

        string verdict = ok ? "CONFIRMED" : "TOO FAR";
        if (verdict == _gapVerdict || _gapReports >= MaxOneWindowReports)
            return;
        _gapVerdict = verdict;
        _gapReports++;

        string measured =
            $"picture '{_parked.name}' painted bottom edge at y={pic.yMin:F0}, dialog "
            + $"'{dialog.name}' painted top edge at y={ink.yMax:F0} (both in the story window's own "
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
        VRLog.Warn(Scope, "STORY COMPOSITE GAP: TOO FAR — the dialog is not directly under the picture. "
                          + "MEASURED THIS TICK: " + measured + ". READ IT LIKE THIS: a gap of several "
                          + "hundred authored px means the placement is running on the AUTHORED-RECT "
                          + "fallback (the 'PLACED AGAINST' clause says which basis ran) — i.e. no "
                          + "graphic under MapStoryController.dialogBox passed the fit's visibility "
                          + "verdict, so there was no ink to place against. A NEGATIVE gap means the "
                          + "picture is overlapping the dialog and ImageGapPx is being applied against "
                          + "an ink offset this method measured differently from ApplyPose. A gap near "
                          + "the intent in px but over the bound in mm means the panel is simply drawn "
                          + "much larger than ModBuild 233's — read the host scale in the line above "
                          + "before changing ImageGapPx.");
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
        if (_claimStanding && !LoadoutConfirmPark.ConfirmReachable())
        {
            VRLog.Warn(Scope, "MODAL DEADLOCK FLOOR TRIPPED (CONTROL ARM) — the game wants a "
                              + "pre-scenario continue control shown and the mod cannot find it drawing "
                              + "anywhere, while StoryComposite's loadout claim is refusing to float "
                              + "'UI Loadout Window' — the window the single-player continue button is a "
                              + $"CHILD of. {LoadoutConfirmPark.Where}. THE CLAIM IS BEING LIFTED NOW and "
                              + "the loadout screen floats again with everything on it from this tick. "
                              + "THE COUNT IS KEPT: this claim has used "
                              + $"{_claimCycles} of {MaxWithdrawCycles} cycle(s) and the next round can "
                              + "still see that the rule fired. WHY THIS ARM EXISTS: the older floor "
                              + "below measures ZERO floated windows, and the ModBuild 234 deadlock had "
                              + "THREE — a full room with the one control that moves the game forward "
                              + "locked inside a suppressed window. USER REPORT: \"Der ist nie "
                              + "erschienen, man konnte nicht weiter vorranschreiten.\" See "
                              + "LOADOUT CONFIRM REACHABLE: NO above for the suppressor by name.");
            _claimStanding = false;
            _claimObject = null;
            _claimWhy = "the deadlock floor's control arm lifted this claim: the game wanted a "
                        + "continue control shown and the mod could not find it drawing anywhere";
            // The curtain goes with it. It is not the suppressor named above, but it withholds the
            // floats of windows that were standing when the player committed, and the cheapest thing
            // to be wrong about at this moment is a quest log that came back a few seconds early.
            _curtainLifted = true;
        }

        // ModBuild 234 — THE CURTAIN GETS THE SAME FLOOR, AND IT IS A BACKSTOP RATHER THAN THE
        // PRIMARY GUARD. TickCurtain's honesty clause already refuses to let the curtain stand
        // unless the mod is floating the story box or the loadout screen, so "zero floated windows
        // while the curtain withholds one" ought to be unreachable. IF THIS EVER FIRES, THE HONESTY
        // CLAUSE IS THE BUG and the line below says so by name — which is exactly what a floor is
        // for. It is checked before the bridge because it is the cheaper of the two to be wrong
        // about: a curtain lifted for nothing costs a quest log back on screen.
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

        string edge = _curtainStanding
            ? "the STORY CURTAIN (ModBuild 234) — the game has hidden the rest of its own UI for the "
              + "quest-start message chain (MapStoryController.isVisibleOtherUI is false), which is "
              + $"the moment the user named. {CurtainMembers.Count} floated window(s) are held out by "
              + $"the curtain's frozen member set [{_curtainNames}]"
            : "the PRE-SCENARIO LOADOUT SCREEN (UILoadoutManager.IsOpen) — this quest played no "
              + "full-attention story message, so the ModBuild 233 edge is the one that fired and the "
              + "curtain is not standing";

        VRLog.Info(Scope, $"POINT OF NO RETURN OPENED at edge: {edge}. So the party has committed to a "
                          + "quest. THIS IS A ONE-SHOT AT THE RISING EDGE: there is no level-triggered "
                          + "sweep in this build and nothing in StoryComposite closes a window after this "
                          + "line. GREP THIS STRING: it must appear EXACTLY ONCE per quest start — the "
                          + "gate's level is the UNION of the curtain and the loadout screen precisely so "
                          + "that the two halves of one commitment cannot produce two edges. CLOSED "
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

    private static void EnsureParked(UIWindow story, UIWindow loadout)
    {
        var win = story.transform as RectTransform;
        if (win == null)
            return;

        if (_parked == null || _parkHost == null || !ReferenceEquals(_parkHost, story))
        {
            if (_parked != null)
                Unpark("the story window changed under the parked picture");
            RectTransform? image = ImageSubtree(loadout);
            if (image == null)
                return;
            if (!Park(story, win, image))
                return;
        }

        if (_parked == null)
            return;
        // The game re-parented it: hand it back and stop. MapTravelConfirm's ownership re-check,
        // for its reason — a subtree that is no longer ours must never be written to.
        if (_parked.parent == null || !ReferenceEquals(_parked.parent, win))
        {
            Unpark("the game re-parented the quest picture");
            return;
        }

        // ModBuild 233 — RE-MEASURE, EVERY TICK, AND HAND BACK RATHER THAN SHOW NOTHING.
        //
        // The composite OWNS the parked rect's size (see Park), so the ordinary case is that this
        // costs one Vector2 compare and writes nothing. The two states worth acting on are the two
        // ways ModBuild 232 shipped an invisible picture, and both are now measured on the object
        // itself instead of assumed from the moment of the park:
        //   * the rect has gone degenerate — something outside this class drove its size; re-assert
        //     the number we measured, because that is the one thing here that is allowed to own it;
        //   * the GAME has taken the picture away (StoryImageViewer.LoadImages deactivates the
        //     holder for the next image, StoryImageViewer.Hide switches the container off, or the
        //     sprite reference is gone) — that is not ours to fight, so give the rect back and let
        //     the claim on the loadout screen lapse with it. A composite that is drawing nothing
        //     must never be the reason the loadout screen is hidden.
        if (_parked.rect.size != _parkedSize && _parkedSize.x >= MinParkSizePx
            && _parkedSize.y >= MinParkSizePx)
            _parked.sizeDelta = _parkedSize;
        var live = _parked.GetComponent<Image>();
        if (!_parked.gameObject.activeInHierarchy)
        {
            Unpark("the game deactivated the quest picture's own GameObject (StoryImageViewer "
                   + "switches the holder off between images, StoryImageViewer.cs:201)");
            return;
        }
        if (live != null && live.sprite == null)
        {
            Unpark($"the quest picture lost its sprite (it was '{_parkedSprite}') — the viewer is "
                   + "loading a different image, so the rect is no longer a picture");
            return;
        }
        ApplyPose(win);
    }

    /// <summary>
    /// Move the picture into the story window and MAKE THE COMPOSITE OWN ITS SIZE.
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
    /// <para>The size is captured BEFORE the anchor write, because after it the rect no longer knows
    /// what it was.</para>
    /// </summary>
    private static bool Park(UIWindow story, RectTransform win, RectTransform image)
    {
        try
        {
            // MEASURED FIRST — everything below changes what this rect would answer.
            Vector2 measured = image.rect.size;
            var img = image.GetComponent<Image>();
            _parkedSprite = img != null && img.sprite != null ? img.sprite.name : "<no Image>";

            _home = image.parent;
            _homeIndex = image.GetSiblingIndex();
            _homeAnchorMin = image.anchorMin;
            _homeAnchorMax = image.anchorMax;
            _homePivot = image.pivot;
            _homeAnchoredPos = image.anchoredPosition;
            _homeSizeDelta = image.sizeDelta;
            _homeRotation = image.localRotation;
            _homeScale = image.localScale;

            // ignoreLayout BEFORE the move, so the destination's layout (if it ever grows one)
            // never rebuilds with this rect in its rectChildren — MapTravelConfirm's discipline.
            var le = image.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = image.gameObject.AddComponent<LayoutElement>();
                _addedIgnore = le;
            }
            le.ignoreLayout = true;

            image.SetParent(win, worldPositionStays: false);
            image.SetAsLastSibling();
            image.anchorMin = new Vector2(0.5f, 0.5f);
            image.anchorMax = new Vector2(0.5f, 0.5f);
            image.pivot = new Vector2(0.5f, 0f);   // its BOTTOM edge is what we place
            image.sizeDelta = measured;            // ← the size the composite now owns
            image.localRotation = Quaternion.identity;
            image.localScale = Vector3.one;
            _parkedSize = measured;
            _parked = image;
            _parkHost = story;
            _composeLogged = false;
            ApplyPose(win);
            LogComposed(story, image, win);
            return true;
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"STORY COMPOSITE: parking the quest picture threw ({e.GetType().Name}) "
                              + "— unwinding to the split presentation, which is the status quo.");
            Unpark("the park itself failed");
            return false;
        }
    }

    /// <summary>
    /// Put the picture's BOTTOM edge just above the dialog's TOP edge, both measured in the story
    /// window's own local space this tick.
    ///
    /// <para>THE ZERO IS THE DIALOG, MEASURED, not a window edge and not a dial. The dialog is
    /// <c>MapStoryController.dialogBox</c> — the one object we KNOW is under the picture, because it
    /// is the other half of the composite — so its top edge is the whole answer and there is nothing
    /// to sweep for. That is the difference from <c>MapTravelConfirm</c>, which has to sweep the
    /// window's content because it does not know what its member will sit under.</para>
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
    /// <para>RE-APPLIED EVERY TICK because the dialog grows: the ModBuild 231 log shows the map story
    /// box fitting 1096x233 → 1096x289 as its text lands, and the composite's own fit shows the same
    /// painted strip at 168 px (three lines, story1.jpg) and 270 px (eight lines, story2.jpg).
    /// Change-gated on <see cref="OffsetEpsilonPx"/> so a settled layout costs one comparison and no
    /// write.</para>
    /// </summary>
    private static void ApplyPose(RectTransform win)
    {
        if (_parked == null)
            return;
        if (!Singleton<MapStoryController>.IsInitialized)
            return;
        MapStoryController mc = Singleton<MapStoryController>.Instance;
        var dialog = mc != null && mc.dialogBox != null
            ? mc.dialogBox.transform as RectTransform
            : null;
        // A HIDDEN DIALOG HAS NO MEASURABLE TOP EDGE. UICharacterStoryBox.Hide deactivates the box
        // between pages of a queued message chain, and its world corners then describe wherever the
        // rect was last laid out. Holding the picture's last position through that gap is right: it
        // is the position the dialog will come back to, and moving to a stale zero and back would be
        // a visible twitch on a window nobody touched.
        if (dialog == null || !dialog.gameObject.activeInHierarchy)
            return;

        ConvertedPanel? panel = ModalFallback.PanelFor(_parkHost);
        float top;
        float centreX;
        if (TryPaintedBounds(dialog, win, panel, out Rect ink, out int inkCount) && inkCount > 0)
        {
            top = ink.yMax;
            centreX = ink.center.x;
            _poseBasis = $"the dialog's PAINTED top edge, unioned from {inkCount} drawn graphic(s) "
                         + $"under '{dialog.name}' ({ink.width:F0}x{ink.height:F0} px of a "
                         + $"{RectSizeOf(dialog).x:F0}x{RectSizeOf(dialog).y:F0} px host rect)";
        }
        else
        {
            // FALLBACK, AND IT IS THE ModBuild 231-233 BEHAVIOUR VERBATIM: the authored rect. It is
            // wrong by ~888 px on this prefab and it is kept anyway, because "no graphic under the
            // dialog passed the fit's visibility verdict" is a state in which a picture placed
            // somewhere plausible beats no composite at all. The basis string says which was used,
            // so a hardware log never has to guess which branch ran.
            Vector3[] corners = Corners;
            dialog.GetWorldCorners(corners);
            // GetWorldCorners: 0 bottom-left, 1 TOP-LEFT, 2 TOP-RIGHT, 3 bottom-right.
            Vector3 topLeft = win.InverseTransformPoint(corners[1]);
            Vector3 topRight = win.InverseTransformPoint(corners[2]);
            top = Mathf.Max(topLeft.y, topRight.y);
            centreX = (topLeft.x + topRight.x) * 0.5f;
            _poseBasis = $"the dialog's AUTHORED rect top edge — NOT its ink, because no graphic "
                         + $"under '{dialog.name}' passed the fit's own visibility verdict this tick. "
                         + "This is the ModBuild 231-233 basis and on the shipped prefab it sits about "
                         + "888 px above the drawn dialog";
        }

        // The picture's own INK offset: how far its painted bottom sits above its rect bottom. The
        // parked rect's pivot is (0.5,0) and its anchors are collapsed to the window's centre, so
        // anchoredPosition.y IS its rect bottom in this space. For a full-bleed Image this is 0 and
        // the whole clause costs one measurement.
        float inkOffset = 0f;
        if (TryPaintedBounds(_parked, win, panel, out Rect pic, out int picCount) && picCount > 0)
            inkOffset = pic.yMin - _parked.anchoredPosition.y;

        var want = new Vector2(centreX, top + ImageGapPx - inkOffset);
        if ((_parked.anchoredPosition - want).sqrMagnitude > OffsetEpsilonPx * OffsetEpsilonPx)
            _parked.anchoredPosition = want;
    }

    private static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>Which edge <see cref="ApplyPose"/> last placed against — ink or authored rect.
    /// Printed by the BUILT line and by the gap falsifier so neither can assert a basis it did not
    /// use ([[an-instrument-can-assert-a-cause]]).</summary>
    private static string _poseBasis = "nothing has been placed yet";

    private static Vector2 RectSizeOf(RectTransform rt) => rt.rect.size;

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
        if (_parked == null)
        {
            _parkHost = null;
            return;
        }
        RectTransform image = _parked;
        _parked = null;
        _parkHost = null;
        _composeLogged = false;
        _parkedSize = Vector2.zero;
        try
        {
            if (_addedIgnore != null)
            {
                Object.Destroy(_addedIgnore);
                _addedIgnore = null;
            }
            else
            {
                var le = image != null ? image.GetComponent<LayoutElement>() : null;
                if (le != null)
                    le.ignoreLayout = false;
            }
            if (image == null || _home == null)
                return;
            image.SetParent(_home, worldPositionStays: false);
            image.SetSiblingIndex(Mathf.Clamp(_homeIndex, 0, Mathf.Max(0, _home.childCount - 1)));
            image.anchorMin = _homeAnchorMin;
            image.anchorMax = _homeAnchorMax;
            image.pivot = _homePivot;
            // sizeDelta BEFORE anchoredPosition, and it is restored AT ALL because the park
            // overwrote it (see Park). For a stretch child sizeDelta is the INSET PAIR, not a size:
            // leaving the park's absolute pixels in it would hand the loadout screen back a picture
            // inset by 1280x720 on every edge. This is the other half of the ModBuild 232 defect —
            // the same field, in the other direction.
            image.sizeDelta = _homeSizeDelta;
            image.anchoredPosition = _homeAnchoredPos;
            image.localRotation = _homeRotation;
            image.localScale = _homeScale;
            VRLog.Info(Scope, $"STORY COMPOSITE UNPARKED — {why}. The quest picture is back under "
                              + $"'{_home.name}' at sibling {_homeIndex} with its authored anchors, pivot, "
                              + $"sizeDelta ({_homeSizeDelta.x:F0},{_homeSizeDelta.y:F0}) and offset "
                              + "restored verbatim, so the loadout screen's own paper-expand tween "
                              + "(UILoadoutQuestWindow.FinishIntroduction) runs on the rect it was authored "
                              + "against — the tween drives 'Paper', and this rect stretches to whatever "
                              + "'Paper' becomes, which is exactly what it did before the mod touched it. "
                              + "THE CLAIM ON THE LOADOUT SCREEN GOES WITH IT: the refusal is conditional on "
                              + "this park and lapses on the next tick, so the loadout window floats again "
                              + "with everything on it.");
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"STORY COMPOSITE: handing the quest picture back threw ({e.GetType().Name}) "
                              + "— it may be left under the story window's root. It is the GAME's own "
                              + "object and the game re-parents nothing, so the next Show() re-lays it "
                              + "out where it belongs.");
        }
        finally
        {
            _home = null;
        }
    }

    /// <summary>THE ONE LINE the round asked for: what was composed, out of which two hosts, which
    /// one supplied the picture, what the layout came out as, and whether the result is shared.
    /// Everything in it is MEASURED this frame — no clause claims a mechanism this method cannot
    /// see.</summary>
    private static void LogComposed(UIWindow story, RectTransform image, RectTransform win)
    {
        if (_composeLogged)
            return;
        _composeLogged = true;
        UIWindow? loadout = LoadoutWindow();
        SharedWindowKind kind = SharedWindows.KindOf(story);
        bool shared = SharedWindows.IsShared(story);
        Vector2 imageSize = image.rect.size;
        Vector2 pos = image.anchoredPosition;
        VRLog.Info(Scope, $"STORY COMPOSITE BUILT: '{story.name}' (ID {story.ID}, component "
                          + $"MapStoryController — the DIALOG) and "
                          + $"'{(loadout != null ? loadout.name : "<no loadout window>")}' "
                          + $"(component UILoadoutManager — the IMAGE) are now ONE window. THE IMAGE CAME "
                          + $"FROM THE LOADOUT SCREEN: '{image.name}' {imageSize.x:F0}x{imageSize.y:F0} px "
                          + $"carrying sprite '{_parkedSprite}', the rect UILoadoutQuestWindow.imagePaper "
                          + "draws quest.LoadoutImageId into (UILoadoutQuestWindow.cs:55-63, "
                          + "StoryImageViewer.cs:233). THAT SIZE IS MEASURED AND THEN OWNED: it was read "
                          + "off the rect a line before the anchors were collapsed to a point and written "
                          + "straight into sizeDelta, which is what ModBuild 232 omitted — its BUILT line "
                          + "says '0x0 px' and that is the blank parchment of story_getrennt3.jpg. From "
                          + "here the picture's size depends on nothing outside this class, so the "
                          + "paper-expand tween cannot reach it. LAYOUT: the picture is parked under "
                          + $"'{win.name}' with pivot (0.5,0) at anchored ({pos.x:F0},{pos.y:F0}) px — its "
                          + $"PAINTED bottom edge {ImageGapPx:F0} px above the PAINTED top edge of "
                          + $"MapStoryController.dialogBox, measured against {_poseBasis} (ModBuild 234: "
                          + "the ink, not the authored rect — that host is ~888 px taller than the strip "
                          + "it draws and the difference IS the gap the user reported), i.e. the dialog "
                          + "DIRECTLY UNDER the image; see STORY COMPOSITE GAP for the millimetres. One "
                          + "panel, one "
                          + $"grab bar, no X. SHARED: {shared} (kind {kind}) — the bar this panel wears is "
                          + "the one SharedWindows.IsShared decides, and the pose it publishes is record 21 "
                          + "entry kind 1. THE LOADOUT SCREEN NOW STANDS DOWN: see the STORY COMPOSITE "
                          + "CLAIM RAISED line for the refusal that withdraws its float, and STORY "
                          + "COMPOSITE ONE WINDOW for whether that actually produced one window. USER "
                          + "RULING: \"Dialog und Bild soll ein einziges 'blaues' Fenster sein, mit dem "
                          + "Dialog unter dem Bild.\"");
    }

    /// <summary>Module teardown. Hands the picture back and unlocks the destinations — leaving a
    /// game object parked under a mod host, or a permanently grey merchant, across a scene change is
    /// how a presentation bug becomes a save-game one.</summary>
    internal static void Reset()
    {
        Unpark("module teardown");
        // ModBuild 235 — and the confirm parker goes with it, for the reason its own Reset states: a
        // park claim that outlived this class would keep the game's own confirm off screen with
        // nothing left to draw it.
        LoadoutConfirmPark.Reset();
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
        _parkedSize = Vector2.zero;
        _parkedSprite = string.Empty;
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
