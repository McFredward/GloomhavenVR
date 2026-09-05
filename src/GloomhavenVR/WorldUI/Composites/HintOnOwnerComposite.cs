using System.Collections.Generic;
using GLOO.Introduction;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// A TUTORIAL HINT IS NOT A WINDOW. IT BELONGS ON THE WINDOW IT IS TALKING ABOUT.
///
/// <para><b>USER REPORT (2026-09-03, .planning/debug/tutorial-hints.jpg, verbatim, item 4):</b>
/// <i>"Problem bereits beim letzten Test erkannt: Die Tutorial-Hints sind ein eigenes großes Fenster
/// siehe tutorial-hints.jpg. Das soll nicht sein - wie die Tooltips auch sollen sie auf dem
/// jeweiligen Fenster direkt selber auftauchen!"</i></para>
///
/// <para><b>WHAT THE SCREENSHOT SHOWS.</b> The floated character screen stands on the left with the
/// "Gemeisterte Fertigkeiten" (mastered ability cards) list inside it; the quest popup stands on the
/// right; and BETWEEN AND BELOW THEM, in its own frame with its own brass grab bar and its own close
/// cross, floats a box reading <i>"Wenn Söldner eine Stufe aufsteigen, schalten sie neue, stärkere
/// Fertigkeitskarten frei… "</i> over a FORTFAHREN button. That box is talking about the list in the
/// window two metres to its left.</para>
///
/// <para>=====================================================================================
/// SECTION 1 — WHICH WINDOW THAT BOX ACTUALLY IS, AND WHY THE EXISTING no-X FAMILY NEVER SAW IT
/// =====================================================================================</para>
///
/// <para>It is <c>Introduction Canvas/LevelBoxMessageLayoutGroup</c> — named by the mod's own
/// identity line in the ModBuild 377 hardware log, which also records that it reached the room
/// through the CATCH-ALL rather than through any family:</para>
/// <code>
///   CATCH-ALL: unknown scenario window 'LevelBoxMessageLayoutGroup' (ID None) floated —
///              enroll it explicitly.                                    (LogOutput.log:2681)
///   WINDOW IDENTITY 'LevelBoxMessageLayoutGroup' (ID None): path Introduction Canvas/
///              LevelBoxMessageLayoutGroup; rect 1920x1080; components [RectTransform,
///              CanvasRenderer, CanvasGroup, UIWindow, LevelMessageUILayoutGroup,
///              InteractabilityIsolatedUIControl]; nearest ancestor UIWindow &lt;none&gt;. (:2682)
/// </code>
///
/// <para><b>SO IT CARRIES <c>LevelMessageUILayoutGroup</c> — AND IT IS STILL NOT A MEMBER OF THE
/// LEVEL-MESSAGE FAMILY THE MOD KNOWS.</b> <c>ModalFallback.LevelMessageGroupIndex</c> identifies
/// that family by REFERENCE against <c>LevelMessagesUIHandler.s_Instance</c>'s two serialized groups
/// (<c>LevelMessageBoxLayoutGroup</c> / <c>LevelMessageHelpTextLayoutGroup</c>). This is a THIRD
/// instance of the same component, owned by a different singleton entirely:
/// <c>UIIntroductionManager</c> holds its own <c>[SerializeField] LevelMessageUILayoutGroup
/// layoutGroup</c> (decompiled GLOO.Introduction/UIIntroductionManager.cs:32) and drives it with its
/// own pending-message queue. Every scripted-message identity test in the mod therefore answers
/// FALSE for it, which is why it fell through to the catch-all and was given the default treatment:
/// a grab bar, an HMD-centred spawn pose, and a close cross.</para>
///
/// <para><b>AND THAT CORRECTS THE ROUND'S OWN BRIEFING.</b> The brief expected this box to be the
/// <c>isLevelMsg</c> family of <c>ModalFallback.8.Convert.cs</c> and asked why the photo shows an X
/// on a family that is excluded from getting one. Both halves are answered by the same fact: it is
/// NOT that family, so the 2026-08-02 no-X ruling never applied to it and the catch-all's default
/// (every converted window gets an X unless it is on the exclusion list) gave it one. Nothing in
/// <c>WorldUI/Modal/**</c> is wrong; the object was simply never recognised.</para>
///
/// <para>=====================================================================================
/// SECTION 2 — WHO OWNS A HINT, FROM THE GAME'S OWN DATA
/// =====================================================================================</para>
///
/// <para>The association is not a position on screen and it is not a guess. Every panel that
/// introduces a concept holds the introducer as a SERIALIZED REFERENCE on itself:</para>
/// <code>
///   UIPerksWindow                       : [SerializeField] UIIntroduce introduction;     (:22)
///   UIPartyCharacterAbilityCardsDisplay : [SerializeField] UIIntroduce introduction;     (:85)
///   UIPartyCharacterEquipmentDisplay    : [SerializeField] UIIntroduce introduction;     (:62)
///   UILevelUpWindow                     : [SerializeField] UIIntroduceLevelUp introduction; (:42)
/// </code>
/// <para>and the introducer records ITS OWN live state: <c>UIIntroduceBase.Show(config, onFinished)</c>
/// sets <c>isShown = true</c> and clears it from the promise's done/fail handlers
/// (GLOO.Introduction/UIIntroduceBase.cs:20-40). So "which hint is on screen" and "who put it there"
/// are one and the same question, answered by the game: the single <c>UIIntroduceBase</c> whose
/// <c>isShown</c> is true. <see cref="ResolveIntroducer"/> reads exactly that, ONCE per hint (on the
/// rising edge of the hint window, never per frame — [[findobjectsoftype-is-the-default-suspect]]).</para>
///
/// <para><b>FROM THE INTRODUCER TO THE WINDOW: THE TOOLTIP PATH, NOT A SECOND ONE.</b>
/// <see cref="ResolveOwner"/> asks <c>ModalFallback.FindOwningWindow(introducer.transform)</c> —
/// byte-for-byte the call every local tooltip is placed by (<c>WorldUI/Patches/TooltipWindowPatches</c>
/// → <c>TooltipOnWindow</c>), and it is the right call for the user's own reason: he asked for
/// "wie die Tooltips auch". It answers OWNERSHIP and never topmost-ness — the very distinction that
/// file records as the root cause of the ModBuild 190 defect, where a topmost rule laid the board's
/// card hints onto whatever unrelated window happened to be floating. A second arm walks the
/// introducer's ancestor <c>UIWindow</c>s and asks <c>ModalFallback.PanelFor</c> for each, for the
/// case where the owner's subtree has not been re-parented under the host.</para>
///
/// <para><b>WHEN THERE IS NO ANSWER, THERE IS NO PARK, AND THAT IS THE HONEST FALLBACK.</b> Two of
/// the nineteen <c>EIntroductionConcept</c> members are raised by callers that hold no
/// <c>UIIntroduce</c> at all — <c>MapChoreographer</c> (LinkedQuest, :427) and
/// <c>UIUnlockLocationFlowManager</c> (CityQuest, :121) call
/// <c>UIIntroductionManager.Show(concept)</c> directly. For those the game expresses no owner, so
/// this class claims none: the hint keeps exactly the presentation it has today — its own floated
/// frame, readable, grabbable, with its own FORTFAHREN button — and the log line says so by name.
/// A fallback that PICKED a window (the focused one, the topmost one) would put a sentence about
/// linked quests onto the merchant's inventory with the same confidence as a correct answer, which
/// is the failure [[one-contributor-is-not-the-union]] and [[a-claim-must-not-measure-itself]] are
/// both about. Guessing is worse than the status quo here; not guessing is not.</para>
///
/// <para>=====================================================================================
/// SECTION 3 — HOW IT IS PUT ON THE WINDOW, AND WHY NOTHING IN WorldUI/Modal HAD TO CHANGE
/// =====================================================================================</para>
///
/// <para><see cref="Park"/> re-parents the HINT WINDOW'S OWN RectTransform under the owner GAME
/// WINDOW's rect — the same object <c>StoryComposite</c> and <c>LoadoutConfirmPark</c> park into,
/// with the same record-the-home-first / restore-verbatim discipline and the same explicit layer
/// write ([[one-shared-layer-leaks]]: per-window capture cameras cull BY LAYER and the owner is
/// already converted when the hint arrives, so a subtree that lands between two of the conversion's
/// own sweeps is drawn by the wrong camera or by two).</para>
///
/// <para><b>AND THAT ONE CHOICE OF PARENT IS WHAT MAKES THE FLOAT GO AWAY WITH NO NEW RULE.</b>
/// <c>ModalFallback.RendersInsideFloatedAncestor</c> walks a window's PARENT CHAIN for a
/// <c>UIWindow</c> that is a live floated host this instant, and both the catch-all
/// (<c>CatchAllEligible</c>) and the enrolled path (<c>ModalFallback.4.Tick</c>) refuse a window it
/// answers true for — "the parent wins (ModBuild 181/184)". Parking under the owner's window rect
/// makes the owner an ANCESTOR of the hint, so the existing rule sees it and the hint stops being a
/// window of its own. No <c>FloatRefusalTable</c> row, no claim plumbing, no edit anywhere in
/// <c>WorldUI/Modal/**</c>. It also cannot churn the catch-all's float fuse: this class runs from
/// the first lines of <c>StoryComposite.Tick</c>, i.e. from the first line of
/// <c>ModalFallback.TickWindowLiveness</c>, and the convert pass runs LATER IN THE SAME TICK — so on
/// the very tick the hint opens it is already a sub-view before anything counts it as a float.</para>
///
/// <para><b>THE PLACEMENT IS A RELATIVE CORRECTION ON MEASURED INK</b>, <see cref="ApplyPose"/>,
/// which is <c>StoryComposite.ApplyPose</c>'s shape for its recorded reason: the hint's rect is
/// 1920x1080 and its ink sits wherever the game's own FixedLowerRight layout put it inside that
/// rect, so there is no pivot arithmetic that could predict it. The ink is measured where it
/// currently is, the delta to where it should be is computed, and the delta is added to
/// <c>anchoredPosition</c>. The target is: horizontally centred on the OWNER's painted content, ink
/// bottom <see cref="HintGapPx"/> above the owner's painted bottom, then clamped inside the owner's
/// own frame so a uGUI mask can never cull it. Change-gated on <see cref="OffsetEpsilonPx"/>, so a
/// settled hint costs two measurements and no write.</para>
///
/// <para><b>THE ANCHOR COLLAPSE IS DONE WITH THE SIZE CAPTURED FIRST</b> —
/// [[anchors-own-a-stretch-child-size]]: the hint window is a STRETCH child of 'Introduction Canvas',
/// so collapsing its anchors to a point without writing <c>sizeDelta</c> would leave it 0x0, which is
/// exactly the 'Image' 0x0 px that cost ModBuild 232 a round.</para>
///
/// <para>=====================================================================================
/// SECTION 4 — THE ENGAGEMENT RULING IS PRESERVED, AND IT IS PRESERVED BY CONSTRUCTION
/// =====================================================================================</para>
///
/// <para><b>USER RULING (2026-08-02), as recorded verbatim in <c>ModalFallback.8.Convert.cs</c>:</b>
/// scripted tutorial/level-message windows get no X — <i>the player MUST engage with a tutorial hint
/// (its own dismiss button or the action it demands); an X let them skip instruction chains and
/// strand triggers.</i></para>
///
/// <para>The mod-drawn X is attached in exactly one place, <c>ModalCloseButton.Attach</c>, and only
/// from the conversion of a window that is being FLOATED. A hint parked by this class is never
/// converted and never floated, so no X is made for it and none can be. That is strictly stronger
/// than an exclusion-list entry: an exclusion has to be maintained as the list grows, this cannot be
/// forgotten. The FORTFAHREN button is the game's own <c>LevelMessageUILayout</c> child and moves
/// with the subtree; it is raised to the owner's LAST SIBLING (<see cref="KeepOnTop"/>), so it is
/// both drawn and hit-tested above the owner's own content. Nothing here presses it, and nothing
/// here writes <c>Show</c>, <c>Hide</c>, <c>Escape</c>, <c>SetActive</c> or a <c>CanvasGroup</c> on
/// any game object — the hint's own lifetime stays <c>UIIntroductionManager</c>'s.</para>
///
/// <para><b>AND THE EXITS ARE NEVER "NOTHING".</b> If the owner window closes, is released or stops
/// being a live floated host, <see cref="Unpark"/> puts the hint back under 'Introduction Canvas'
/// with its parent, sibling index, anchors, pivot, anchoredPosition, sizeDelta, rotation and scale
/// restored verbatim — and it runs BEFORE the release loop that destroys the host (that is why this
/// class is ticked from where it is ticked). On the next tick the ancestor rule no longer answers,
/// and the hint floats on its own again exactly as it does today. There is no state in which a hint
/// exists and is unreachable. The standing rules bound it the same way: nothing is left empty (the
/// hint never floats as a frame it has been emptied of — it is moved WHOLE), and no input, canvas or
/// window is disabled, so the options menu is reachable throughout.</para>
///
/// <para><b>MULTIPLAYER: NOTHING GOES ON THE WIRE, AND NO WIRE FIELD IS NEEDED.</b> A hint is raised
/// by <c>UIIntroductionManager</c> from a LOCAL panel interaction and is gated on
/// <c>AdventureState.MapState.MapParty.HasIntroduced(concept)</c> — the local player's own campaign
/// save (UIIntroduceLevelUp.cs:12-30). It teaches THIS player his own UI; the other client neither
/// has the same panel open nor the same "already introduced" set. This class only re-parents a local
/// uGUI subtree under a local host on this client, so two players may legitimately disagree about
/// every verdict in it — the same conclusion, for the same reason, that <c>LoadoutConfirmPark</c> and
/// <c>FloatRefusalTable</c> already record. The 1:1 owner-size ruling is satisfied without a term:
/// the hint inherits the OWNER window's transform and scale by being its child, so on a mirrored
/// board it is drawn at the owner's size by construction and never reads a viewer-side dial
/// ([[a-mirror-must-not-read-the-viewers-dial]]).</para>
///
/// <para><b>THE ONE GREP THAT IS ONLY TRUE IF THE COMPLAINT IS FIXED:</b> <c>HINT ON WINDOW</c>. See
/// <see cref="Report"/> — it names the hint, the owner it was anchored to, HOW that owner was
/// resolved, and the fallback when there is none, and it carries running counts so it can never read
/// as a dead instrument ([[a-held-instrument-reads-as-dead]]). Before this build the family printed
/// NOTHING at a printed tier — "TUTORIAL" and "Tutorial hint" both return zero hits in the ModBuild
/// 380 log — which is why the defect survived two hardware rounds.</para>
/// </summary>
internal static class HintOnOwnerComposite
{
    private const string Scope = "WorldUI";

    /// <summary>Authored uGUI px between the hint's painted BOTTOM edge and the owner window's
    /// painted bottom edge. Small on purpose: the hint sits ON the window like a tooltip, it does
    /// not hang off it.</summary>
    private const float HintGapPx = 24f;

    /// <summary>Authored uGUI px of clearance kept between the hint's ink and the owner window's own
    /// frame, so a uGUI mask on the host can never cull the hint the way it clipped the ModBuild 241
    /// confirm plate.</summary>
    private const float FrameMarginPx = 12f;

    /// <summary>Below this many authored px the placement correction is not written at all — a hint
    /// that is already where it should be must never be nudged, or the "correction" becomes the
    /// per-frame jitter that makes the two eyes disagree. That is this parker's own statement of the
    /// shared reason, so it takes the shared value (ModBuild 439, survey row R35).
    ///
    /// <para><see cref="HintGapPx"/> beside it deliberately does NOT join
    /// <see cref="ChromeParkTuning.ChromeGapPx"/>: it is the same 24 px by coincidence of taste and
    /// carries its own argument (the hint sits ON the window like a tooltip rather than hanging off
    /// it), and a number argued independently is exactly what must not be collapsed.</para></summary>
    private const float OffsetEpsilonPx = ChromeParkTuning.OffsetEpsilonPx;

    /// <summary>A hint rect smaller than this in either axis is not a message box; refuse to park it
    /// rather than do anchor arithmetic on a degenerate rect.</summary>
    private const float MinParkSizePx = 8f;

    // ---- the moved object and its home ---------------------------------------------------------

    private static UIWindow? _hint;
    private static UIWindow? _ownerWindow;
    private static RectTransform? _parked;

    private static Transform? _home;

    /// <summary>The scene the hint window lived in before the move, and whether that scene was the
    /// <c>DontDestroyOnLoad</c> one. Recorded because re-parenting MOVES a GameObject into its new
    /// parent's scene ([[adopting-a-window-takes-its-lifetime]]) — the exact mechanism that stopped
    /// 'Quest verwerfen' opening its dialog on 2026-09-03 and that
    /// <c>CanvasConversion.KeepHostInTargetScene</c> was written for. See
    /// <see cref="RefusesForScene"/>.</summary>
    private static UnityEngine.SceneManagement.Scene _homeScene;
    private static bool _homeWasPersistent;

    private static int _homeIndex;
    private static Vector2 _homeAnchorMin, _homeAnchorMax, _homePivot;
    private static Vector2 _homeAnchoredPos, _homeSizeDelta;
    private static Quaternion _homeRotation;
    private static Vector3 _homeScale;
    private static LayoutElement? _addedIgnore;

    private static readonly List<Transform> LayerTx = new(64);
    private static readonly List<int> LayerWas = new(64);
    private static int _layerWritten = -1;
    private static int _layerSkipped;

    // ---- the owner resolution, taken once per hint ----------------------------------------------

    /// <summary>The <c>UIIntroduceBase</c> the game reports as showing, resolved on the rising edge
    /// of the hint window. Null when the game expresses no owner for this hint.</summary>
    private static UIIntroduceBase? _introducer;

    /// <summary>True once the scan has been run for the CURRENT hint window instance.</summary>
    private static bool _scanned;

    /// <summary>
    /// Scans already spent on THIS hint. One is the expected number: <c>UIIntroduceBase.Show</c> sets
    /// <c>isShown = true</c> BEFORE it calls <c>process.Process(config)</c>, and that call runs
    /// straight through to <c>layoutGroup.Show</c> → <c>window.Show()</c> synchronously
    /// (UIIntroductionManager.cs:39-99), so the flag is already up on the first tick that sees the
    /// window open. <see cref="MaxScansPerHint"/> buys exactly one retry against that reading being
    /// wrong for some caller — bounded, because an unbounded retry is a per-frame
    /// <c>FindObjectsOfType</c> ([[findobjectsoftype-is-the-default-suspect]]) and this project has
    /// shipped that three times.
    ///
    /// <para>THE BUDGET ALSO PAYS FOR OWNER CHANGES INSIDE ONE CHAIN, which is a real case and not a
    /// hypothetical: <c>UIIntroductionManager</c> queues messages and only hides its window when the
    /// PENDING LIST EMPTIES (ShowNextMessage, :84-95), so a second panel's introduction can arrive
    /// while the window is still open and the cached introducer would then be stale. The cache is
    /// dropped on the EDGE where the game clears <c>isShown</c>, and the budget bounds how many such
    /// edges are re-resolved. Past it the last verdict simply stands — ugly, bounded, and never a
    /// per-frame scan.</para>
    /// </summary>
    private static int _scans;

    /// <summary>See <see cref="_scans"/>. Counted per HINT, not per tick.</summary>
    private const int MaxScansPerHint = 8;

    /// <summary>How many <c>UIIntroduceBase</c> instances reported <c>isShown</c> on the scan — 1 is
    /// the healthy value, 0 means a direct <c>UIIntroductionManager</c> caller, &gt;1 means the
    /// association is ambiguous and this class declines rather than guesses.</summary>
    private static int _shownCount;

    /// <summary>Instances walked by the scan, for the cost clause of the log line.</summary>
    private static int _scanWalked;

    /// <summary>
    /// PER-HINT LATCHES FOR THE LEDGER, and they are not tidiness. <see cref="ResolveOwner"/> and
    /// <see cref="RefusesForScene"/> run EVERY tick a hint stands, so counting there without a latch
    /// would change the report's signature once per frame and turn the change-gated
    /// <c>VRLog.Note</c> into a per-frame flood — the exact failure ModBuild 331 was cleaning up
    /// when it silenced this subsystem. The counters are therefore EDGE counts: one per hint, per
    /// kind, cleared on the rising edge of the next hint window.
    /// </summary>
    private static bool _countedNoOwner;
    private static bool _countedRefused;

    /// <summary>How the owner was resolved, in the log line's own words. Never null.</summary>
    private static string _how = "no hint has been seen yet";

    // ---- the ledger ------------------------------------------------------------------------------

    private static int _parks;
    private static int _unparks;
    private static int _hintsSeen;
    private static int _noOwner;

    /// <summary>Parks refused after the owner WAS resolved — a degenerate rect, or the lifetime
    /// guard. Separate from <see cref="_noOwner"/> because the two send a reader to different
    /// files.</summary>
    private static int _refused;

    private static int _ambiguous;
    private static int _throws;
    private static bool _disabledByError;

    private static string _verdict = string.Empty;
    private static int _reports;

    /// <summary>One "no owner" per hint (see the latch fields).</summary>
    private static void CountNoOwner()
    {
        if (_countedNoOwner)
            return;
        _countedNoOwner = true;
        _noOwner++;
    }

    /// <summary>One "park refused" per hint (see the latch fields).</summary>
    private static void CountRefused()
    {
        if (_countedRefused)
            return;
        _countedRefused = true;
        _refused++;
    }

    /// <summary>True while this class is drawing a hint inside a floated window — read by nothing
    /// today, and kept because it is the one question a future reader will ask of this file.</summary>
    internal static bool Standing => _parked != null && _ownerWindow != null;

    /// <summary>
    /// One tick. Called from the first lines of <c>StoryComposite.Tick</c>, i.e. from the first line
    /// of <c>ModalFallback.TickWindowLiveness</c>.
    ///
    /// <para><b>THAT CALL SITE IS LOAD-BEARING, FOR THE TWO REASONS StoryComposite RECORDS.</b> The
    /// unpark has to run BEFORE the release loop, which calls <c>CanvasConversion.Release</c> and
    /// destroys the host the hint was parked under; and the park has to run BEFORE the convert pass,
    /// so the ancestor rule has already made the hint a sub-view by the time anything would have
    /// floated it.</para>
    /// </summary>
    internal static void Tick()
    {
        if (_disabledByError)
            return;
        try
        {
            TickCore();
        }
        catch (System.Exception e)
        {
            _throws++;
            VRLog.Alert(Scope, $"HINT ON WINDOW: the tick threw ({e.GetType().Name}: {e.Message}) — "
                               + $"throw {_throws}. The hint is handed back to its own floated frame, "
                               + "which is the ModBuild 380 presentation and is reachable. The class "
                               + "disarms itself for the rest of the session rather than throwing once "
                               + "per frame.");
            _disabledByError = true;
            try
            {
                Unpark("the hint composite threw and disarmed itself");
            }
            catch (System.Exception)
            {
                // Nothing left to do: the hand-back is the recovery, and it has already failed.
            }
        }
    }

    private static void TickCore()
    {
        UIWindow? hint = HintWindow();
        bool open = hint != null && (hint.IsOpen || hint.IsVisible);

        if (!open)
        {
            if (_parked != null)
                Unpark("the hint was dismissed — the game closed the introduction window");
            _hint = null;
            _introducer = null;
            _scanned = false;
            _shownCount = 0;
            return;
        }

        // Rising edge (or a different window instance): resolve the owner ONCE. The scan is the only
        // FindObjectsOfType in this file and it is deliberately edge-driven — the per-tick answer is
        // the cached component reference, which costs one null check.
        bool newHint = !_scanned || !ReferenceEquals(_hint, hint);
        if (newHint)
        {
            if (_parked != null && !ReferenceEquals(_hint, hint))
                Unpark("a different introduction window took over");
            _hint = hint;
            _scanned = true;
            _hintsSeen++;
            _scans = 0;
            _countedNoOwner = false;
            _countedRefused = false;
        }
        // THE CACHE IS DROPPED ON THE GAME'S OWN EDGE. A UIIntroduceBase that no longer reports
        // isShown is no longer the owner of what is on screen, and inside one chain the window can
        // outlive it (see MaxScansPerHint). One field read per tick.
        if (_introducer != null && !_introducer.isShown)
            _introducer = null;
        // The bounded retry: only while nothing has been found, and never more than
        // MaxScansPerHint times for one hint. Once an introducer is in hand this costs one branch.
        if (_introducer == null && _scans < MaxScansPerHint)
        {
            _scans++;
            _introducer = ResolveIntroducer();
        }

        ConvertedPanel? panel = ResolveOwner(_introducer, out UIWindow? owner, out string how);
        _how = how;

        if (panel == null || owner == null)
        {
            if (_parked != null)
                Unpark("the owner window is no longer a live floated host");
            Report(hint!, null, null);
            return;
        }

        if (_parked == null || !ReferenceEquals(_ownerWindow, owner))
        {
            if (_parked != null)
                Unpark("the owner window changed under a standing hint");
            if (!Park(hint!, owner, panel))
            {
                Report(hint!, null, null);
                return;
            }
        }
        else if (!StillOurs(owner))
        {
            Unpark("the game re-parented the hint out of the owner window");
            Report(hint!, null, null);
            return;
        }
        else
        {
            int layer = owner.gameObject.layer;
            if (_layerWritten >= 0 && _parked!.gameObject.layer != layer)
                WriteLayers(layer, _parked);
            KeepOnTop();
            ApplyPose(owner, panel);
        }

        Report(hint!, owner, panel);
    }

    // ---- identity ---------------------------------------------------------------------------------

    /// <summary>
    /// THE HINT WINDOW: <c>UIIntroductionManager</c>'s OWN <c>LevelMessageUILayoutGroup</c>, by
    /// reference. Deliberately NOT "any window carrying a LevelMessageUILayoutGroup" — the scripted
    /// tutorial box and help-text strip carry the identical component and belong to
    /// <c>LevelMessagesUIHandler</c>, which has its own family, its own placement and its own
    /// chain-pose continuity in <c>WorldUI/Modal/**</c>. [[containment-is-not-identity]] applies to
    /// component TYPE the same way it applies to ancestry: the type is shared, the instance is not.
    /// </summary>
    private static UIWindow? HintWindow()
    {
        if (!Singleton<UIIntroductionManager>.IsInitialized)
            return null;
        UIIntroductionManager mgr = Singleton<UIIntroductionManager>.Instance;
        if (mgr == null)
            return null;
        LevelMessageUILayoutGroup? group = mgr.LayoutGroup;
        return group == null ? null : group.window;
    }

    /// <summary>
    /// WHO RAISED THIS HINT — the single <c>UIIntroduceBase</c> the GAME reports as showing.
    ///
    /// <para><c>UIIntroduceBase.isShown</c> is set in <c>Show(config, onFinished)</c> and cleared from
    /// both branches of the promise (GLOO.Introduction/UIIntroduceBase.cs:20-40), so it is true for
    /// exactly the interval the introduction is on screen. Ambiguity is REFUSED rather than broken by
    /// a tie-break: two shown introducers would mean the owner cannot be named, and naming the wrong
    /// window is worse than the box the user is complaining about.</para>
    ///
    /// <para>Edge-driven, once per hint. <c>Object.FindObjectsOfType</c> in a per-frame path is this
    /// project's most-shipped performance defect ([[findobjectsoftype-is-the-default-suspect]]); here
    /// it runs on the rising edge of a window the player sees a handful of times per campaign, and
    /// the walked count is printed so a future round can see the cost rather than assume it.</para>
    /// </summary>
    private static UIIntroduceBase? ResolveIntroducer()
    {
        UIIntroduceBase? found = null;
        _shownCount = 0;
        _scanWalked = 0;
        UIIntroduceBase[] all = Object.FindObjectsOfType<UIIntroduceBase>(includeInactive: true);
        _scanWalked = all.Length;
        for (int i = 0; i < all.Length; i++)
        {
            UIIntroduceBase b = all[i];
            if (b == null || !b.isShown)
                continue;
            _shownCount++;
            found ??= b;
        }
        if (_shownCount > 1)
        {
            _ambiguous++;
            return null;
        }
        return found;
    }

    /// <summary>
    /// FROM THE INTRODUCER TO THE FLOATED WINDOW IT LIVES IN — the tooltip path first, the ancestry
    /// walk second, and a named refusal when neither answers.
    /// </summary>
    private static ConvertedPanel? ResolveOwner(UIIntroduce? introducer, out UIWindow? owner,
                                                out string how)
    {
        owner = null;
        if (introducer == null)
        {
            how = _shownCount > 1
                ? $"REFUSED — {_shownCount} UIIntroduceBase instances report isShown at once, so the "
                  + "game does not name ONE owner for this hint and picking one would be a guess"
                : "NONE — no UIIntroduceBase reports isShown, i.e. this introduction was raised by a "
                  + "direct UIIntroductionManager.Show(concept) caller (MapChoreographer's LinkedQuest "
                  + "or UIUnlockLocationFlowManager's CityQuest), which holds no UIIntroduce and so "
                  + "expresses no owner window in the game's data";
            CountNoOwner();
            return null;
        }

        // ARM 1 — the tooltip path, byte-for-byte: ownership, never topmost-ness.
        ConvertedPanel? panel = ModalFallback.FindOwningWindow(introducer.transform);
        if (panel != null && panel.IsAlive && panel.HostRect != null)
        {
            owner = OwnerWindowOf(introducer, panel);
            if (owner != null)
            {
                how = $"ModalFallback.FindOwningWindow(introducer '{introducer.name}') — the same "
                      + "ownership walk every local tooltip is placed by";
                return panel;
            }
        }

        // ARM 2 — the introducer's own ancestor windows, for the case where the owner's subtree has
        // not been re-parented under a host. Nearest first: the innermost floated window wins.
        for (Transform? t = introducer.transform; t != null; t = t.parent)
        {
            var w = t.GetComponent<UIWindow>();
            if (w == null || ReferenceEquals(w, _hint))
                continue;
            ConvertedPanel? p = ModalFallback.PanelFor(w);
            if (p == null || !p.IsAlive || p.HostRect == null)
                continue;
            owner = w;
            how = $"the nearest ancestor UIWindow of the introducer '{introducer.name}' that the mod "
                  + "is floating this instant (ModalFallback.PanelFor)";
            return p;
        }

        how = $"NONE — the introducer '{introducer.name}' resolves to no floated window: neither the "
              + "tooltip ownership walk nor its own ancestor UIWindows name one the mod is drawing "
              + "right now (the owner screen is on the flat screen, or not open)";
        CountNoOwner();
        return null;
    }

    /// <summary>The game <c>UIWindow</c> that the panel converted — the object the parked hint is
    /// made a CHILD of, so <c>ModalFallback.RendersInsideFloatedAncestor</c> can find it by walking
    /// parents. Falls back to the introducer's own ancestry when the panel's target carries no
    /// window on its own GameObject.</summary>
    private static UIWindow? OwnerWindowOf(UIIntroduce introducer, ConvertedPanel panel)
    {
        if (panel.Target != null)
        {
            var onTarget = panel.Target.GetComponent<UIWindow>();
            // AND IT HAS TO BE ONE THE MOD IS ACTUALLY FLOATING. The park's whole effect is that
            // ModalFallback.RendersInsideFloatedAncestor finds this window above the hint and refuses
            // the float; a window that is not a live floated host would suppress nothing and the hint
            // would end up drawn inside a screen-space parent nobody renders in VR.
            // [[parent-wins-needs-a-real-parent]]: suppressing X because Y handles it requires Y to
            // ACTUALLY handle it.
            if (onTarget != null && ModalFallback.PanelFor(onTarget) != null)
                return onTarget;
        }
        for (Transform? t = introducer.transform; t != null; t = t.parent)
        {
            var w = t.GetComponent<UIWindow>();
            if (w != null && !ReferenceEquals(w, _hint) && ModalFallback.PanelFor(w) != null)
                return w;
        }
        return null;
    }

    /// <summary>Is the hint still parented where THIS class put it? A window the game has taken back
    /// is no longer ours to move ([[dont-win-a-write-war]]).</summary>
    private static bool StillOurs(UIWindow owner)
    {
        if (_parked == null || owner.transform == null)
            return false;
        Transform? p = _parked.parent;
        return p != null && ReferenceEquals(p, owner.transform);
    }

    // ---- the park ---------------------------------------------------------------------------------

    /// <summary>
    /// Move the hint window whole into the owner window's rect, recording its home first so the
    /// hand-back is exact.
    ///
    /// <para>THE SIZE IS CAPTURED BEFORE THE ANCHORS ARE COLLAPSED, and that order is the whole of
    /// the ModBuild 232 lesson: the hint window is a STRETCH child of 'Introduction Canvas', so its
    /// drawn size comes entirely from its parent and collapsing its anchors to a point without
    /// writing <c>sizeDelta</c> leaves it 0x0 ([[anchors-own-a-stretch-child-size]]).</para>
    /// </summary>
    private static bool Park(UIWindow hint, UIWindow owner, ConvertedPanel panel)
    {
        if (hint.transform is not RectTransform rect || owner.transform is not RectTransform win)
        {
            _how = $"REFUSED — the hint '{hint.name}' or the owner '{owner.name}' is not a "
                   + "RectTransform, so the anchor arithmetic this class is built on does not apply. "
                   + "Nothing is moved";
            return false;
        }

        Vector2 measured = rect.rect.size;
        if (measured.x < MinParkSizePx || measured.y < MinParkSizePx)
        {
            _how = $"REFUSED — the hint '{hint.name}' measures {measured.x:0}x{measured.y:0} px, "
                   + $"below the {MinParkSizePx:0} px floor: its layout has not been built yet, so "
                   + "capturing that size into sizeDelta would freeze a degenerate rect "
                   + "([[anchors-own-a-stretch-child-size]]). Nothing is moved; the park is retried "
                   + "on the next tick";
            CountRefused();
            return false;
        }
        if (RefusesForScene(hint, owner))
            return false;

        _home = rect.parent;
        _homeScene = hint.gameObject.scene;
        _homeWasPersistent = CanvasConversion.IsPersistentScene(_homeScene);
        _homeIndex = rect.GetSiblingIndex();
        _homeAnchorMin = rect.anchorMin;
        _homeAnchorMax = rect.anchorMax;
        _homePivot = rect.pivot;
        _homeAnchoredPos = rect.anchoredPosition;
        _homeSizeDelta = rect.sizeDelta;
        _homeRotation = rect.localRotation;
        _homeScale = rect.localScale;

        // ignoreLayout BEFORE the move — MapTravelConfirm's discipline, for its reason: this takes
        // the anchors and the anchoredPosition out of any parent layout group's hands rather than
        // racing it for them.
        LayoutElement? le = hint.gameObject.GetComponent<LayoutElement>();
        if (le == null)
        {
            le = hint.gameObject.AddComponent<LayoutElement>();
            _addedIgnore = le;
        }
        le.ignoreLayout = true;

        _parked = rect;
        _ownerWindow = owner;

        rect.SetParent(win, worldPositionStays: false);
        rect.SetAsLastSibling();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = measured;              // ← the size this class now owns
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        WriteLayers(owner.gameObject.layer, rect);
        ApplyPose(owner, panel);
        _parks++;
        return true;
    }

    /// <summary>
    /// REFUSE A PARK THAT WOULD COST THE HINT ITS LIFETIME. Re-parenting moves a GameObject into its
    /// new parent's SCENE, so parking a <c>DontDestroyOnLoad</c> window under a host that lives in an
    /// ordinary scene makes the window die at the next scene load while
    /// <c>UIIntroductionManager</c>'s serialized <c>layoutGroup</c> keeps pointing at it — every
    /// future hint in the session then throws instead of appearing. That is not a hypothetical: it is
    /// the 2026-09-03 'Quest verwerfen' deadlock, and <c>CanvasConversion.KeepHostInTargetScene</c>
    /// exists for it. The repair that class uses (<c>DontDestroyOnLoad</c> on the host) is not
    /// available here — Unity only accepts a ROOT GameObject, and the host is already parented.
    ///
    /// <para><b>SO THE ANSWER IS TO NOT PARK, AND TO SAY SO.</b> Today's evidence says this refusal
    /// should never fire: <c>UIIntroductionManager</c> derives from the game's plain
    /// <c>Singleton&lt;T&gt;</c> (decompiled Singleton.cs — <c>Awake</c> assigns <c>_instance</c> and
    /// there is no <c>DontDestroyOnLoad</c> anywhere in it), so 'Introduction Canvas' is an ordinary
    /// scene object like the windows it annotates. The guard is here because a refusal that never
    /// fires costs one scene comparison, and the failure it prevents is permanent for the session —
    /// and because if it DOES fire, the log names the exact thing a future round has to build (a
    /// persistent dock) instead of leaving it to be inferred from a dead hint chain.</para>
    /// </summary>
    private static bool RefusesForScene(UIWindow hint, UIWindow owner)
    {
        bool hintPersistent = CanvasConversion.IsPersistentScene(hint.gameObject.scene);
        if (!hintPersistent || CanvasConversion.IsPersistentScene(owner.gameObject.scene))
            return false;
        _how = $"REFUSED FOR LIFETIME — the hint window '{hint.name}' lives in the DontDestroyOnLoad "
               + $"scene and the owner '{owner.name}' lives in '{owner.gameObject.scene.name}'. "
               + "Re-parenting would move the hint into that scene and the next scene load would "
               + "DELETE it while UIIntroductionManager's serialized layoutGroup still pointed at it "
               + "— the 2026-09-03 deadlock class. Nothing is moved";
        CountRefused();
        return true;
    }

    /// <summary>Keep the hint the LAST child of the owner window, so it is drawn AND hit-tested above
    /// the owner's own content — <c>TooltipOnWindow.RaiseToWindowTop</c>'s job, for its reason: the
    /// ModBuild 190 hardware log proved a correctly placed box invisible underneath the very list it
    /// hangs inside. Change-gated: a re-write per frame would fight the owner's own layout.</summary>
    private static void KeepOnTop()
    {
        if (_parked == null || _parked.parent == null)
            return;
        int last = _parked.parent.childCount - 1;
        if (_parked.GetSiblingIndex() != last)
            _parked.SetAsLastSibling();
    }

    /// <summary>
    /// WRITE THE OWNER ROOT'S OWN LAYER OVER THE MOVED SUBTREE, recording what the game had there so
    /// the hand-back can put it back. <c>StoryComposite.WriteLayers</c> and
    /// <c>LoadoutConfirmPark.WriteLayers</c>, verbatim, and not optional here either: per-window
    /// capture cameras cull BY LAYER ([[one-shared-layer-leaks]]) and the owner is already converted
    /// when the hint arrives, so a subtree that lands between two of the conversion's own sweeps is
    /// drawn by the wrong camera or by two.
    ///
    /// <para>A FOREIGN RENDER SUBTREE IS SKIPPED WHOLE — <c>CanvasConversion.ApplyModLayer</c>'s own
    /// rule for its own reason. <c>CanvasRenderer</c> is not a <c>Renderer</c>, so ordinary uGUI is
    /// unaffected.</para>
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
    /// <c>PanelSupersample.RestoreLayers</c>'s: a transform somebody else has since re-layered is no
    /// longer ours to hand back, and writing our stale value would strand it on a layer no camera
    /// renders.</summary>
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

    // ---- the placement ---------------------------------------------------------------------------

    private static readonly List<Graphic> PaintScratch = new(64);
    private static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>
    /// Put the hint's INK where a tooltip would sit on this window: horizontally centred on the
    /// owner's painted content, its painted bottom <see cref="HintGapPx"/> above the owner's painted
    /// bottom, and then clamped inside the owner's own frame.
    ///
    /// <para><b>THE CORRECTION IS RELATIVE, WHICH IS WHAT MAKES IT PIVOT-FREE</b> —
    /// <c>StoryComposite.ApplyPose</c>'s argument verbatim. The hint holds a whole subtree whose ink
    /// sits wherever the game's own FixedLowerRight layout put it inside a 1920x1080 rect; there is
    /// no pivot arithmetic that could predict it. So the ink is measured where it currently is, the
    /// delta to where it should be is computed, and that delta is ADDED to
    /// <c>anchoredPosition</c> — which converges in one tick and then writes nothing.</para>
    ///
    /// <para>A measurement that fails leaves the hint where it is rather than moving it to a
    /// fabricated position: "not ready yet" is a state, never a placement.</para>
    /// </summary>
    private static void ApplyPose(UIWindow owner, ConvertedPanel? panel)
    {
        if (_parked == null || owner.transform is not RectTransform win)
            return;
        if (!TryPaintedBounds(_parked, win, panel, exclude: null, out Rect hintInk, out int hintCount)
            || hintCount == 0)
            return;
        if (!TryPaintedBounds(win, win, panel, exclude: _parked, out Rect ownerInk, out int ownerCount)
            || ownerCount == 0)
            return;

        Rect frame = win.rect;

        float wantMinX = ownerInk.center.x - hintInk.width * 0.5f;
        float wantMinY = ownerInk.yMin + HintGapPx;

        // The frame clamp is last and it is never skipped: a uGUI mask on the host culls whatever
        // leaves the frame, and a hint the player cannot see is the failure this whole file exists
        // to remove.
        float maxX = frame.xMax - FrameMarginPx - hintInk.width;
        float minX = frame.xMin + FrameMarginPx;
        wantMinX = maxX >= minX ? Mathf.Clamp(wantMinX, minX, maxX) : frame.center.x - hintInk.width * 0.5f;

        float maxY = frame.yMax - FrameMarginPx - hintInk.height;
        float minY = frame.yMin + FrameMarginPx;
        wantMinY = maxY >= minY ? Mathf.Clamp(wantMinY, minY, maxY) : frame.center.y - hintInk.height * 0.5f;

        Vector2 delta = new(wantMinX - hintInk.xMin, wantMinY - hintInk.yMin);
        if (Mathf.Abs(delta.x) < OffsetEpsilonPx && Mathf.Abs(delta.y) < OffsetEpsilonPx)
            return;
        _parked.anchoredPosition += delta;
    }

    /// <summary>
    /// The painted bounds of a subtree in <paramref name="win"/>'s local (authored uGUI) space, with
    /// an optional subtree EXCLUDED.
    ///
    /// <para>THE VISIBILITY VERDICT IS BORROWED, NOT RE-INVENTED: with a panel in hand this asks
    /// <c>CanvasConversion.CountsAsFitContent</c> — the fit's own test — so the numbers here and the
    /// numbers in the host's fit line cannot disagree. Same choice, same recorded reason, as
    /// <c>StoryComposite.TryPaintedBounds</c> and <c>LoadoutConfirmPark.TryPaintedBounds</c>.</para>
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

    /// <summary>The minimal "is this drawn?" test, used ONLY when there is no converted panel to ask
    /// the fit's own verdict of. Deliberately weaker and deliberately stated as such —
    /// <c>StoryComposite.CountsAsPaintedHere</c>'s twin.</summary>
    private static bool CountsAsPaintedHere(Graphic g) =>
        g.enabled && g.gameObject.activeInHierarchy
        && (g.canvasRenderer == null || !g.canvasRenderer.cull)
        && g.color.a > 0.02f
        && !g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal);

    // ---- the hand-back ---------------------------------------------------------------------------

    /// <summary>
    /// Put the hint window back exactly where the game had it. The TRANSFORM is restored only while
    /// the object is still parented under our owner, because re-taking it from wherever the game has
    /// since put it is a write war ([[dont-win-a-write-war]]).
    ///
    /// <para>NOTHING IS WRITTEN TO THE GAME HERE EITHER: no <c>Show</c>, no <c>Hide</c>, no
    /// <c>Escape</c>, no <c>SetActive</c>, no <c>CanvasGroup</c>. The hint's own visibility stays
    /// <c>UIIntroductionManager</c>'s, and on the next tick the ancestor rule stops answering, so
    /// the hint floats on its own again — the ModBuild 380 presentation, which is reachable.</para>
    /// </summary>
    private static void Unpark(string why)
    {
        RectTransform? rect = _parked;
        UIWindow? owner = _ownerWindow;
        _parked = null;
        _ownerWindow = null;
        if (rect == null)
        {
            _home = null;
            return;
        }
        _unparks++;
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
                LayoutElement? le = rect.GetComponent<LayoutElement>();
                if (le != null)
                    le.ignoreLayout = false;
            }
            if (_home == null || owner == null || owner.transform == null)
                return;
            if (rect.parent == null || !rect.parent.IsChildOf(owner.transform))
                return;
            rect.SetParent(_home, worldPositionStays: false);
            rect.SetSiblingIndex(Mathf.Clamp(_homeIndex, 0, Mathf.Max(0, _home.childCount - 1)));
            rect.anchorMin = _homeAnchorMin;
            rect.anchorMax = _homeAnchorMax;
            rect.pivot = _homePivot;
            rect.sizeDelta = _homeSizeDelta;
            rect.anchoredPosition = _homeAnchoredPos;
            rect.localRotation = _homeRotation;
            rect.localScale = _homeScale;
            // THE SCENE COMES BACK WITH THE PARENT, and this is the belt to that brace. SetParent
            // moves a GameObject into its new parent's scene, so landing back under the original
            // parent restores the original scene by construction — unless that parent has itself
            // been moved since. Re-assert it explicitly for the persistent case, where getting it
            // wrong is a session-long deadlock rather than a cosmetic one.
            if (_homeWasPersistent && !CanvasConversion.IsPersistentScene(rect.gameObject.scene))
                VRLog.Alert(Scope, $"HINT ON WINDOW: '{rect.name}' came back to '{_home.name}' but is "
                                   + $"in scene '{rect.gameObject.scene.name}', not the "
                                   + "DontDestroyOnLoad scene it started in — its own home parent has "
                                   + "moved. The next scene load will DELETE it while "
                                   + "UIIntroductionManager still points at it. This is the "
                                   + "2026-09-03 deadlock class and RefusesForScene should have "
                                   + "prevented the park; that it did not is the thing to fix.");
            VRLog.Info(Scope, $"HINT ON WINDOW UNPARKED — {why}. The introduction window "
                              + $"'{rect.name}' is back under '{_home.name}' at sibling {_homeIndex} "
                              + "with its parent, anchors, pivot, sizeDelta, anchoredPosition, local "
                              + "rotation and local scale restored verbatim from the record taken "
                              + "before the first move, and the layout opt-out released or destroyed. "
                              + "It floats on its own again from the next tick.");
        }
        catch (System.Exception e)
        {
            VRLog.Alert(Scope, $"HINT ON WINDOW: handing the introduction window back threw "
                               + $"({e.GetType().Name}) — it may be left under the owner window's "
                               + "root. It is the GAME's own object and UIIntroductionManager "
                               + "re-shows it through its own layout group on the next hint.");
        }
        finally
        {
            _home = null;
            _homeScene = default;
            _homeWasPersistent = false;
        }
    }

    // ---- the falsifier ----------------------------------------------------------------------------

    /// <summary>
    /// THE ONE LINE A HARDWARE ROUND READS. Change-gated on its own verdict signature, so a settled
    /// hint prints once and a hint that moves between owners prints once per move — never per frame,
    /// and never silent while something is happening ([[a-held-instrument-reads-as-dead]]: the
    /// running counts are IN the line, so a repeat that looks identical is impossible unless nothing
    /// has changed).
    ///
    /// <para>It states four things and asserts nothing it did not measure this tick: WHICH hint,
    /// WHICH owner it was anchored to, HOW that owner was resolved, and — when there is none — the
    /// fallback and what the player therefore sees.</para>
    /// </summary>
    private static void Report(UIWindow hint, UIWindow? owner, ConvertedPanel? panel)
    {
        string verdict = owner != null
            ? $"ON '{owner.name}' (ID {owner.ID}) via {_how}"
            : $"NOT ANCHORED — {_how}";
        string signature = $"{verdict}|{_parks}|{_unparks}|{_noOwner}|{_refused}|{_ambiguous}";
        if (signature == _verdict)
            return;
        _verdict = signature;
        _reports++;

        string body;
        if (owner != null)
        {
            body = $"the introduction hint '{hint.name}' (ID {hint.ID}) is drawn INSIDE the floated "
                   + $"window '{owner.name}' (ID {owner.ID}), as a child of that window's own rect. "
                   + $"OWNER RESOLVED BY: {_how}. It is therefore NOT a window of its own: "
                   + "ModalFallback.RendersInsideFloatedAncestor finds the owner above it and both "
                   + "the catch-all and the enrolled path refuse the float, so it has no grab bar "
                   + "and — the 2026-08-02 engagement ruling — no close cross, because the X is only "
                   + "ever made by the conversion of a window that IS floated. Its own FORTFAHREN / "
                   + "dismiss button is the only exit and it is raised to the owner's last sibling, "
                   + $"so it is drawn and hit-tested above the owner's content. Host layer written "
                   + $"over {LayerTx.Count} transform(s), {_layerSkipped} foreign render subtree(s) "
                   + $"left alone; host rect {(panel?.HostRect != null ? panel.HostRect.name : "<none>")}. "
                   + "ITS HOME, restored verbatim on the hand-back: parent "
                   + $"'{(_home != null ? _home.name : "<none>")}', sibling {_homeIndex}, scene "
                   + $"'{_homeScene.name}' (persistent={_homeWasPersistent}).";
        }
        else
        {
            body = $"the introduction hint '{hint.name}' (ID {hint.ID}) is NOT anchored to a window "
                   + $"this tick. WHY: {_how}. FALLBACK, AND WHAT IT GETS WRONG: nothing is moved, so "
                   + "the hint keeps the presentation it had before this build — its own floated "
                   + "frame with a grab bar and a close cross, standing beside the window it is "
                   + "talking about instead of on it. That is the reported defect and it is "
                   + "deliberately preferred to the alternative: picking the focused or topmost "
                   + "window would put this text onto an unrelated panel with the same confidence as "
                   + "a correct answer. The hint stays readable and dismissable either way.";
        }

        // HW-VERIFY: this family printed NOTHING at a printed tier before ModBuild 381 — "TUTORIAL"
        // and "Tutorial hint" both return zero hits in the ModBuild 380 log — which is why the
        // defect survived two hardware rounds. The next round's answer is read off this line.
        VRLog.Note(Scope, $"HINT ON WINDOW: {body} COUNTS: hints seen {_hintsSeen}, parked {_parks}, "
                          + $"handed back {_unparks}, no owner {_noOwner}, park refused {_refused}, "
                          + $"ambiguous {_ambiguous}, "
                          + $"throws {_throws}, reports {_reports}; the owner scan walked "
                          + $"{_scanWalked} UIIntroduceBase instance(s) on this hint's rising edge "
                          + $"and found {_shownCount} reporting isShown.");
    }

    /// <summary>Module teardown — hand the hint back before anything else is torn down.</summary>
    internal static void Reset()
    {
        Unpark("module teardown");
        _hint = null;
        _introducer = null;
        _scanned = false;
        _shownCount = 0;
        _scanWalked = 0;
        _verdict = string.Empty;
        _disabledByError = false;
    }
}
