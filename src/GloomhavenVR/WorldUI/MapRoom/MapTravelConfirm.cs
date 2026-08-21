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
//         if (show) travelButton.gameObject.SetActive(!FFSNetwork.IsOnline);
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
// USER RULINGS THIS IMPLEMENTS (asked and answered, 2026-08-21):
//   * the confirm button belongs IN THE QUEST WINDOW — "dort, wo du sowieso hinschaust", no new
//     surface on the table to trigger by accident;
//   * the same-location shortcut is switched OFF ENTIRELY — exactly as the game already does
//     online — so travel commits through the button and through nothing else.
//
// TWO MECHANISMS, BOTH THROUGH SEAMS THE GAME ALREADY USES:
//
//   1. A PREFIX that reproduces the online branch. It does not "block a click": on the
//      same-location path it performs the original's own first statement (store the callback) and
//      returns, which is byte-for-byte what the original does when FFSNetwork.IsOnline. Every other
//      path runs vanilla. Nothing is invented — the behaviour already ships, for other players.
//
//   2. THE TRAVEL OPTIONS TRAVEL WITH THE WINDOW. `travelOptions` lives in the flat map HUD, which
//      the room does not render, so the button existed and was unreachable. It is parked inside the
//      floated quest window while that window is up and handed back afterwards — the same move
//      GuildmasterDestinations makes for the shared banner, and the same move the GAME makes for
//      its own banner when it enters the temple (UIGuildmasterHUD.cs:198-200). Anchored to the
//      window's bottom edge so it reads as the window's own footer rather than as loose furniture.
//
// THE FOOTER IS PLACED BY MEASURING THE CONTENT, NOT THE CONTAINER (ModBuild 191).
//
// User, on the ModBuild 190 build (questbutton.jpg): "Der Bestätigungsknopf ist zu weit weg auf der
// y-achse. Reduzier da gerne den Abstand." In that photograph the floated quest card hangs in the
// air and 'QUEST ERNEUT SPIELEN' sits far below it, down on the table over the map.
//
// THE CAUSE, and it is a frame confusion, not a taste question. Up to 190 this class anchored the
// CONTAINER: anchorMin/anchorMax = (0.5, 0), pivot = (0.5, 1), anchoredPosition = zero. That pins
// the container's OWN TOP EDGE to the window's bottom-centre — and the container is the flat game's
// HUD object ('New Adventure UI' is its home parent, per the 190 log), whose rect is a screen-sized
// bar with the actual 'Adventure button' laid out somewhere INSIDE it. The gap in the photograph is
// therefore exactly the distance from the container's top edge down to the button inside it: a
// number this class never looked at.
//
// THE FIX MEASURES THAT NUMBER EVERY TICK. <see cref="AlignFooter"/> takes the union of the
// container's ACTIVE, VISIBLE child Graphics' rects — expressed in the container's own local space,
// so it is directly comparable with anchoredPosition — and solves for the offset that puts the
// CONTENT'S top edge one stated gap under the window's bottom edge, and the content's horizontal
// centre under the window's centre. Nothing is hard-coded but the gap, and the gap is a FRACTION OF
// THE WINDOW'S HEIGHT rather than a pixel count, because the two labels this button carries
// ("Reisen" and "Quest erneut spielen") have different widths and the quest window itself is not
// one fixed size. A pixel offset tuned on one screenshot would be wrong for the other label.
//
// IT RE-SOLVES EVERY TICK, on purpose: the game re-labels the button (OnSelectedMapLocation:356),
// shows and hides it (EnableTravelOptions:408-415 — and online it hides travelButton outright), and
// a uGUI layout is not final on the frame a re-parent happens. A latched one-shot measurement would
// be right for one label and wrong for the other. The write is skipped whenever the solved offset
// already stands, so a steady state costs one bounds sweep over a handful of Graphics and no write
// at all — and if some OTHER writer fought us for anchoredPosition, the re-solve log below would
// print repeatedly, which is the evidence a write war leaves.
//
// RESTORE DISCIPLINE: the container's original parent, sibling index, anchors, pivot and
// anchoredPosition are recorded before the first move and written back on release, on stand-down
// and on teardown — and only while it is still OURS, because the game moves its own UI too and
// winning a write war with it is a bug this project has already paid for once.
//
// DEGRADES SAFELY: every private member is resolved through AccessTools once. If any is missing,
// ONE Warn names the consequence (the shortcut stays live / the button stays unreachable) and the
// class stands completely down. Nothing thrown, nothing on the wire, no rule library touched.
// ---------------------------------------------------------------------------

/// <summary>
/// Makes the game's own travel confirmation reachable in the 3D map room, and switches off the
/// single-player double-click shortcut that committed without asking. Installed by
/// <see cref="MapRoomDriver"/>.
/// </summary>
internal static class MapTravelConfirm
{
    private const string Scope = "MapRoom";

    private static bool _installed;
    private static bool _resolved;
    private static bool _standDown;

    private static FieldInfo? _locationToTravel;
    private static FieldInfo? _onConfirmCallback;
    private static FieldInfo? _travelOptions;
    private static FieldInfo? _travelButton;

    // ---- the parked container's home, recorded once ----------------------------------------
    private static Transform? _optionsHome;
    private static int _optionsHomeIndex;
    private static Vector2 _homeAnchorMin, _homeAnchorMax, _homePivot, _homeAnchoredPos;
    private static bool _homeRecorded;
    private static UIWindow? _host;
    private static bool _reported;

    // ---- the measured footer placement (ModBuild 191) ---------------------------------------

    /// <summary>
    /// Gap between the quest window's bottom edge and the TOP of the travel options' visible
    /// content, as a fraction of the WINDOW'S OWN HEIGHT.
    ///
    /// <para>A fraction and not a pixel count, because the thing it must look right against is the
    /// window — and the quest window is not one fixed size (its body grows with the objective text,
    /// the enemy row and the rewards block). 2% of the card's height reads as "the card's own
    /// footer": close enough to belong to it, far enough not to touch its border.</para>
    /// </summary>
    private const float GapWindowHeights = 0.02f;

    /// <summary>
    /// Alpha below which a Graphic is not counted as visible CONTENT. uGUI bars routinely carry a
    /// full-width fully transparent Image as a raycast blocker; including one would make the
    /// measured content the width of the whole HUD bar and put its invisible top edge under the
    /// window instead of the button's. Anything the player can actually see clears this by miles.
    /// </summary>
    private const float MinVisibleAlpha = 0.02f;

    /// <summary>How far the solved offset must move before it is written (container-local units).
    /// Sub-pixel churn is not worth a transform write on a converted world-space canvas.</summary>
    private const float OffsetEpsilon = 0.5f;

    /// <summary>Seconds between re-solve log lines. The FIRST solve always logs; after that only a
    /// materially different answer does, and never more often than this. A line that repeats on
    /// this cadence forever is the signature of another writer fighting us for anchoredPosition.</summary>
    private const float ResolveLogIntervalSeconds = 5f;

    /// <summary>Graphic sink for the content sweep — reused, so the per-tick measurement allocates
    /// nothing.</summary>
    private static readonly List<Graphic> ContentGraphics = new(32);

    /// <summary>World-corner scratch for the same sweep.</summary>
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    private static Vector2 _appliedOffset;
    private static bool _footerLogged;
    private static float _footerLoggedAt = float.NegativeInfinity;
    private static Vector2 _loggedOffset;
    private static bool _contentFallbackLogged;

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
                          + "invention), and the game's real Reisen/Abbrechen buttons are parked inside "
                          + "the floated quest window so they can be reached at all. Travel now commits "
                          + "through that button and through nothing else.");
    }

    /// <summary>
    /// Level-triggered, one call per tick from <see cref="MapRoomDriver"/>. Parks the travel
    /// options inside <paramref name="questWindow"/> while that window is floated, and hands them
    /// back otherwise. Idempotent: a steady state costs two reference compares and no writes.
    /// </summary>
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
            return;
        }

        if (ReferenceEquals(_host, questWindow))
        {
            // Already parked where it belongs — but the FOOTER OFFSET is re-solved anyway, because
            // the game re-labels and re-shows the button under us and a uGUI layout is not final on
            // the frame a re-parent happens. Cheap and write-free once the answer stops moving.
            AlignFooter(questWindow, options, mgr);
            return;
        }

        Unpark("a different quest window took over");

        Transform t = options.transform;
        if (!_homeRecorded)
        {
            _homeRecorded = true;
            _optionsHome = t.parent;
            _optionsHomeIndex = t.GetSiblingIndex();
            if (t is RectTransform home)
            {
                _homeAnchorMin = home.anchorMin;
                _homeAnchorMax = home.anchorMax;
                _homePivot = home.pivot;
                _homeAnchoredPos = home.anchoredPosition;
            }
        }

        _host = questWindow;
        t.SetParent(questWindow.transform, worldPositionStays: false);
        t.SetAsLastSibling();
        // The window's own FOOTER: the container's rect is pinned to the window's bottom-centre with
        // its own top edge as the pivot, and AlignFooter then slides it so the VISIBLE CONTENT — not
        // the container's rect — lands under that edge. Splitting it this way keeps the frame trivial
        // (offsets are read straight off anchoredPosition) and puts every measured number in one
        // place. Anchors, pivot and position are all restored verbatim on unpark.
        if (t is RectTransform rect)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }
        _appliedOffset = Vector2.zero;
        _footerLogged = false;
        _footerLoggedAt = float.NegativeInfinity;
        AlignFooter(questWindow, options, mgr);

        if (!_reported)
        {
            _reported = true;
            var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
            VRLog.Info(Scope, $"MAP TRAVEL CONFIRM: the game's travel options were moved from "
                              + $"'{(_optionsHome != null ? _optionsHome.name : "<none>")}' into the floated "
                              + $"quest window '{questWindow.name}' and anchored to its bottom edge. The "
                              + $"button itself is '{(btn != null ? btn.name : "<not found>")}' — the SAME "
                              + "ExtendedButton the flat game uses, so its label (Reisen / Wiederholen), "
                              + "its interactable state and every guard behind OnTravelButtonClick are the "
                              + "game's own. They go home the moment the window releases.");
        }
    }

    // ---- the measured footer placement ------------------------------------------------------

    /// <summary>
    /// Slide the parked container so its VISIBLE CONTENT sits snug under the quest window's bottom
    /// edge. Solved, not tuned — see the class doc for the frame confusion this replaces.
    ///
    /// <para>THE ARITHMETIC, stated once. With <c>anchorMin = anchorMax = (0.5, 0)</c> the anchor
    /// reference point is the window rect's bottom-centre, so in the WINDOW'S local space
    /// <c>anchorY = win.rect.yMin</c> and <c>anchorX = win.rect.center.x</c>. The container carries
    /// identity rotation and unit scale (set at park time), so its local space is the window's
    /// local space translated onto its pivot — which means a content edge measured at local
    /// <c>content.yMax</c> lands at <c>anchorY + anchoredPosition.y + content.yMax</c> in the
    /// window. Demanding that equal <c>win.rect.yMin - gap</c> gives
    /// <c>anchoredPosition.y = -gap - content.yMax</c>, and centring gives
    /// <c>anchoredPosition.x = -content.center.x</c>. No term in either is a guess.</para>
    /// </summary>
    private static void AlignFooter(UIWindow host, GameObject options, AdventureMapUIManager? mgr)
    {
        if (host == null || options == null)
            return;
        // The game hides the whole container (EnableTravelOptions → travelOptions.SetActive(flag),
        // and online it hides travelButton on top of that). An inactive subtree has no laid-out
        // rects to measure, so the last solved offset simply stands until it comes back.
        if (!options.activeInHierarchy)
            return;
        if (options.transform is not RectTransform rect || host.transform is not RectTransform win)
            return;

        bool measured = TryContentBounds(rect, out Rect content, out int counted, out int skipped);
        if (!measured)
        {
            // NOTHING VISIBLE TO MEASURE. Fall back to the container's own rect, which is exactly
            // the pre-191 behaviour — i.e. the button may sit low again — and say so, because "the
            // gap came back" then has a named cause instead of being a mystery.
            content = rect.rect;
            if (!_contentFallbackLogged)
            {
                _contentFallbackLogged = true;
                VRLog.Warn(Scope, $"MAP TRAVEL CONFIRM: the travel options container '{options.name}' "
                                  + $"exposed no visible Graphic to measure ({skipped} candidate(s) "
                                  + "skipped as disabled, transparent or zero-sized), so the footer falls "
                                  + "back to the CONTAINER'S OWN RECT. CONSEQUENCE: the confirm button may "
                                  + "hang far below the quest window again — that is exactly the ModBuild "
                                  + "190 report — because the container's rect is the flat HUD bar's rect "
                                  + "and the button sits somewhere inside it. Nothing throws.");
            }
        }

        float gap = GapWindowHeights * Mathf.Abs(win.rect.height);
        var want = new Vector2(-content.center.x, -gap - content.yMax);
        bool moved = (rect.anchoredPosition - want).sqrMagnitude > OffsetEpsilon * OffsetEpsilon;
        if (moved)
            rect.anchoredPosition = want;
        _appliedOffset = want;

        bool changed = (want - _loggedOffset).sqrMagnitude > OffsetEpsilon * OffsetEpsilon;
        if (_footerLogged && (!changed || Time.unscaledTime - _footerLoggedAt < ResolveLogIntervalSeconds))
            return;
        _footerLogged = true;
        _footerLoggedAt = Time.unscaledTime;
        _loggedOffset = want;
        LogFooter(host, options, mgr, rect, win, content, counted, skipped, measured, gap, want);
    }

    /// <summary>
    /// THE MEASUREMENT LINE. Every number the placement is derived from, in the frame it was read
    /// in, plus the resulting gap in three units — window heights (scale-free, the thing the eye
    /// judges), world units, and real metres at the current rig scale. If the next hardware report
    /// still says "too low", this line says whether the CONTENT bounds were wrong or the OFFSET was
    /// not applied; those are different bugs and no other line separates them.
    /// </summary>
    private static void LogFooter(UIWindow host, GameObject options, AdventureMapUIManager? mgr,
                                  RectTransform rect, RectTransform win, Rect content,
                                  int counted, int skipped, bool measured, float gap, Vector2 want)
    {
        var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
        string btnWhere = "<not found>";
        if (btn != null)
        {
            Vector3 local = rect.InverseTransformPoint(btn.transform.position);
            btnWhere = $"'{btn.name}' active={btn.gameObject.activeInHierarchy} at world "
                       + $"{btn.transform.position}, i.e. local y={local.y:F1} inside the container";
        }
        // RIG SCALE IS NAMED, NOT ASSUMED. A converted panel's host carries
        // localScale = metersPerPixel * rigScale (CanvasConversion.1.Core.PlaceHost), so dividing a
        // world length by the rig scale is what turns it back into REAL metres — the unit the user
        // judges "zu weit weg" in. Both are printed so a wrong scale is visible rather than baked in.
        float rigScale = Rig.RigTarget.Current != null
            ? Mathf.Max(Rig.RigTarget.Current.lossyScale.x, 0.0001f)
            : 1f;
        float gapWorld = gap * Mathf.Abs(win.lossyScale.y);
        float gapMeters = gapWorld / rigScale;
        // Built separately rather than inlined: a nested interpolated string inside another one is
        // legal C# but it defeats the repo's own source scanners (scripts/patch-inventory.py walks
        // string literals with a single-quote-depth reader), and a tool that mis-parses this file
        // reports its Harmony patch as unregistered. Not worth the one saved local.
        string contentHow = measured
            ? $"{counted} visible Graphic(s) ({skipped} skipped as disabled/transparent/zero-sized)"
            : "NOT MEASURABLE — the container's own rect is standing in";
        VRLog.Info(Scope,
            $"MAP TRAVEL CONFIRM footer SOLVED on '{host.name}'.\n"
            + $"  container : '{options.name}' rect {rect.rect} (this is the flat HUD bar's own rect — "
            + "its TOP edge is what ModBuild 190 pinned to the window, which is why the button hung "
            + "far below it).\n"
            + $"  content   : {contentHow}, "
            + $"union in CONTAINER-LOCAL space {content} (top edge y={content.yMax:F1}, centre "
            + $"x={content.center.x:F1}).\n"
            + $"  button    : {btnWhere}.\n"
            + $"  window    : rect {win.rect} (height {Mathf.Abs(win.rect.height):F1}), lossyScale "
            + $"{win.lossyScale.y:F4} world units per local unit, rig scale {rigScale:F2} world units "
            + "per real metre.\n"
            + $"  applied   : anchoredPosition = {want} — DERIVED as (-content.center.x, "
            + $"-gap - content.yMax), never a tuned pixel count.\n"
            + $"  gap       : {GapWindowHeights:P1} of the window's height = {gap:F1} local units = "
            + $"{gapWorld:F3} world units = {gapMeters * 1000f:F1} mm real. The button reads as the "
            + "card's own footer at any window size and for either label (Reisen / Quest erneut "
            + "spielen), because both terms are re-measured every tick.\n"
            + "  DISPROOF  : if the button is STILL too low, compare 'content' with 'button' above — "
            + "a content union whose yMax sits far above the button's local y means the union caught "
            + "something invisible (raise MinVisibleAlpha) and the gap is measured from the wrong "
            + "edge. If the two agree and the button is still low, anchoredPosition is being "
            + "overwritten by another writer, and THIS LINE WILL REPEAT on its 5 s cadence.");
    }

    /// <summary>
    /// The union of the container's ACTIVE, VISIBLE child Graphics' rects, expressed in the
    /// container's own local space.
    ///
    /// <para>GRAPHICS AND NOT RECTTRANSFORMS, deliberately. A uGUI bar is full of layout groups and
    /// empty spacers whose rects are far larger than anything drawn in them; taking every
    /// RectTransform would measure the bar's skeleton, which is the same mistake as measuring the
    /// container. A Graphic is the only component that PAINTS, so the union of the enabled,
    /// non-transparent, non-degenerate ones is "what the player can see", which is the thing the
    /// user is judging the distance to.</para>
    ///
    /// <para>Corner-based, not rect-based: a child may sit several transforms deep, so its rect is
    /// in ITS parent's space. <c>GetWorldCorners</c> + <c>InverseTransformPoint</c> lands every
    /// corner in the container's frame with no assumption about the chain between them.</para>
    /// </summary>
    private static bool TryContentBounds(RectTransform container, out Rect content,
                                         out int counted, out int skipped)
    {
        content = default;
        counted = 0;
        skipped = 0;
        ContentGraphics.Clear();
        container.GetComponentsInChildren(includeInactive: false, ContentGraphics);
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
            rt.GetWorldCorners(CornerScratch);
            for (int c = 0; c < 4; c++)
            {
                Vector3 p = container.InverseTransformPoint(CornerScratch[c]);
                if (p.x < xMin) xMin = p.x;
                if (p.x > xMax) xMax = p.x;
                if (p.y < yMin) yMin = p.y;
                if (p.y > yMax) yMax = p.y;
            }
            counted++;
        }
        if (counted == 0)
            return false;
        content = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    /// <summary>Put the container back — only while it is still parented under our host.</summary>
    private static void Unpark(string why)
    {
        if (_host == null)
            return;
        UIWindow? host = _host;
        _host = null;
        if (!EnsureReflection())
            return;
        AdventureMapUIManager? mgr = Manager();
        if (mgr == null || _travelOptions?.GetValue(mgr) is not GameObject options)
            return;
        Transform t = options.transform;
        // STILL OURS? The game re-parents its own UI freely, and taking it back from wherever it
        // has since put it would be a write war this project has already lost once.
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
        }
        VRLog.Info(Scope, $"MAP TRAVEL CONFIRM: travel options handed back to their own home ({why}) — "
                          + "parent, sibling index, anchors, pivot and position all restored verbatim "
                          + $"(the measured footer offset {_appliedOffset} is dropped with them; it lives "
                          + "on the RectTransform this line has just overwritten with the recorded home "
                          + "value, so nothing of ours is left on the game's object).");
        _appliedOffset = Vector2.zero;
        _footerLogged = false;
        _footerLoggedAt = float.NegativeInfinity;
        _loggedOffset = Vector2.zero;
    }

    /// <summary>Teardown — hand the container back before the room disappears under it.</summary>
    internal static void Reset() => Unpark("map room teardown");

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
