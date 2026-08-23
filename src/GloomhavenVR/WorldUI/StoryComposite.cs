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
/// <para><b>MULTIPLAYER.</b> Nothing here goes on the wire. The park is a local re-parent of a local
/// uGUI subtree. The close is the same local UI close the window's own X has performed since
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

    /// <summary>Gap between the dialog's top edge and the image's bottom edge, in the story
    /// window's own authored uGUI pixels. Deliberately a constant and not a config dial: the user
    /// asked for "den Dialog unter dem Bild", not for a spacing control, and every new dial is a
    /// surface somebody has to tune. 24 px at the window's 1920x1080 authored scale is about 1.5 %
    /// of its height — visibly one gap, never a separation.</summary>
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
    /// IS THE PLAYER PAST THE POINT OF NO RETURN — i.e. is the pre-scenario LOADOUT screen up in the
    /// 3D map room?
    ///
    /// <para>Read from the GAME and from one public property: <c>UILoadoutManager.IsOpen</c>
    /// (decompiled/GH.Runtime/UILoadoutManager.cs:57, <c>_window.IsOpen</c>). That window is opened
    /// when the party has committed to a quest and it stays open until the scenario is entered, so
    /// it IS the interval the user described ("zu diesem Zeitpunkt"). Deliberately NOT keyed on the
    /// story box: the story box closes when the player clicks through the intro, and the merchant
    /// must stay shut for the rest of the loadout screen too.</para>
    ///
    /// <para>THE 3D-ROOM GATE IS PART OF THE ANSWER, not a caveat. Everything this class enforces is
    /// about floated world-space windows and the map room's 3D caps; with the 3D map switched off
    /// there are neither, and the flat game's own behaviour is what the player asked to keep.</para>
    ///
    /// <para>OFFERED AS A PUBLIC SEAM for the cap lane: one <c>if</c> in
    /// <c>MapButtonRail.Pressable</c> makes every cap dead and grey even if the game-side lock-out
    /// below is ever defeated. It is NOT required for the caps to go dead — see
    /// <see cref="SetDestinationsLocked"/>, which drives the game's own
    /// <c>Toggle.interactable</c> that <c>Pressable</c> already reads. This property is a pure read
    /// of game state and has never been part of the deadlock: it closes nothing and holds
    /// nothing.</para>
    /// </summary>
    internal static bool PointOfNoReturn =>
        MapRoomDriver.Active
        && Singleton<UILoadoutManager>.IsInitialized
        && Singleton<UILoadoutManager>.Instance != null
        && Singleton<UILoadoutManager>.Instance.IsOpen;

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
    /// </summary>
    private static UIWindow? StoryWindow()
    {
        if (!Singleton<MapStoryController>.IsInitialized)
            return null;
        MapStoryController mc = Singleton<MapStoryController>.Instance;
        if (mc == null || mc.window == null)
            return null;
        if (mc.window.IsOpen || mc.window.IsVisible)
            return mc.window;
        return FloatedByMod(mc.window) ? mc.window : null;
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

        // ModBuild 233 — THE CLAIM AND THE FALSIFIER, IN THAT ORDER AND BOTH FROM MEASUREMENT.
        // The claim decides whether the loadout screen is drawn by us this tick; the falsifier then
        // states, from a fresh read of the float set, whether the result is actually ONE window.
        TickLoadoutClaim(loadout);
        ReportOneWindow(story, loadout);

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
        if (loadout != null && _parked != null && _parkHost != null)
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
                              + $"'{(loadout != null ? loadout.name : "<the loadout window is gone>")}' — the "
                              + "composite is no longer standing, so the loadout screen floats again from "
                              + "this tick with everything on it (its own paper-expand tween, its confirm "
                              + "button and its hotkeys were never touched). This is the level ending, not a "
                              + "failure: the ordinary end of it is the player clicking through the last "
                              + $"page of the intro. {_claimCycles} of {MaxWithdrawCycles} cycle(s) used.");
            _claimObject = null;
            _claimWhy = "the quest-intro composite has stood down; the loadout screen is nobody's "
                        + "responsibility but its own";
        }
        _claimStanding = want;
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
        if (_holdLifted || _heldLastPass <= 0)
            return;
        int floated = ModalFallback.CountFloatsOtherThan(null);
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

        VRLog.Info(Scope, $"POINT OF NO RETURN OPENED — the pre-scenario loadout screen is up "
                          + "(UILoadoutManager.IsOpen), so the party has committed to a quest. THIS IS A "
                          + "ONE-SHOT AT THE RISING EDGE: there is no level-triggered sweep in this build "
                          + "and nothing in StoryComposite closes a window after this line. CLOSED "
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
    /// <para>RE-APPLIED EVERY TICK because the dialog grows: the ModBuild 231 log shows the map story
    /// box fitting 1096x233 → 1096x289 as its text lands. Change-gated on
    /// <see cref="OffsetEpsilonPx"/> so a settled layout costs one comparison and no write.</para>
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

        Vector3[] corners = Corners;
        dialog.GetWorldCorners(corners);
        // GetWorldCorners: 0 bottom-left, 1 TOP-LEFT, 2 TOP-RIGHT, 3 bottom-right.
        Vector3 topLeft = win.InverseTransformPoint(corners[1]);
        Vector3 topRight = win.InverseTransformPoint(corners[2]);
        float top = Mathf.Max(topLeft.y, topRight.y);
        float centreX = (topLeft.x + topRight.x) * 0.5f;

        var want = new Vector2(centreX, top + ImageGapPx);
        if ((_parked.anchoredPosition - want).sqrMagnitude > OffsetEpsilonPx * OffsetEpsilonPx)
            _parked.anchoredPosition = want;
    }

    private static readonly Vector3[] Corners = new Vector3[4];

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
                          + $"BOTTOM edge {ImageGapPx:F0} px above the measured TOP edge of "
                          + "MapStoryController.dialogBox, i.e. the dialog UNDER the image, one panel, one "
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
    }
}
