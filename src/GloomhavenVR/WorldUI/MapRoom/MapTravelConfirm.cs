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
// have their windows in two different places. Nothing here is serialized, so no ModBuild handshake
// term depends on it.
//
// ===========================================================================================
// WHERE THE BUTTON GOES — ModBuild 193 IS A DELIBERATE REVERT OF ModBuild 192. DO NOT "FIX" IT BACK.
// ===========================================================================================
//
// THE RULING THAT GOVERNS THIS FILE, verbatim:
//
//   "Der 'Quest erneut spielen' Button soll teil des Fensters sein so wie beim aller ersten mal als
//    du es gemacht hattest. Ich meinte lediglich, das du innerhalb des Fensters den Button etwas
//    nach oben schiebst. Jetzt hast du den Button komplett vom Fenster getrennt. Mach das
//    rückgängig."
//
// It overrides everything that came after 190. The history, so the next reader can see which of the
// three earlier answers is being restored and which two are dead:
//
//   190  PARKED THE CONTAINER INSIDE THE FLOATED QUEST WINDOW — `SetParent(questWindow.transform)`,
//        anchorMin = anchorMax = (0.5, 0), pivot = (0.5, 1), anchoredPosition = zero. Zero pins the
//        CONTAINER'S TOP EDGE to the window's BOTTOM edge, and the container is the flat HUD's
//        screen-sized bar with the button laid out well inside it, so the button hung far BELOW the
//        window, out over the table. THIS IS THE MECHANISM THE USER CALLS "beim allerersten mal"
//        AND WANTS BACK. His complaint about it was ONE thing and only one thing:
//        "Der Bestätigungsknopf ist zu weit weg auf der y-achse. Reduzier da gerne den Abstand."
//   191  kept the parenting and solved the offset from the container's VISIBLE-GRAPHIC content, so
//        the content sat snug just BELOW the window's bottom edge (2 % of the window height,
//        17.9 mm real). Rejected: "viel zu weit oben und auch auf der x-achse verschoben".
//   192  DETACHED THE CONTAINER ENTIRELY onto a world host of its own, placed from a census of every
//        floated window. REJECTED OUTRIGHT by the ruling above; that machinery is gone from this
//        file, not commented out.
//
// SO: THE 190 MECHANISM, WITH 191's MEASUREMENT AIMED AT A NEW TARGET. The arithmetic in 191 was
// never wrong — its own hardware line proves it solved exactly what it set out to solve:
//
//   container : 'Travel Options' rect (x:-256.00, y:0.00, width:512.00, height:0.00)
//   content   : 2 visible Graphic(s) (1 skipped), union in CONTAINER-LOCAL space
//               (x:-153.50, y:30.00, width:307.00, height:65.00) (top edge y=95.0, centre x=0.0)
//   window    : rect (x:-256.00, y:-510.50, width:512.00, height:1021.00), lossyScale 0.1734
//   applied   : anchoredPosition = (0.00, -115.42)
//
// What was wrong was the TARGET: 191 put the content's TOP edge one gap BELOW `win.rect.yMin`, i.e.
// still outside the card. "Innerhalb des Fensters ... etwas nach oben" asks for the content's BOTTOM
// edge one small INSET ABOVE `win.rect.yMin` — on the card, near its lower edge. One sign, one edge;
// everything else about the solve is 191's, unchanged.
//
// THE ARITHMETIC, STATED ONCE. With `anchorMin = anchorMax = (0.5, 0)` the anchor reference point in
// the WINDOW'S local space is `(win.rect.center.x, win.rect.yMin)`. With `pivot = (0.5, 1)` the
// container's local ORIGIN is its pivot point, and `anchoredPosition` is exactly the offset from the
// anchor reference point to that origin. The container carries identity rotation and unit scale (set
// at park time), so a point measured at container-local `(x, y)` lands in the window at
// `(win.rect.center.x + anchoredPosition.x + x, win.rect.yMin + anchoredPosition.y + y)`. Demanding
//
//     content's BOTTOM edge  ==  win.rect.yMin + inset       →  anchoredPosition.y = inset - content.yMin
//     content's CENTRE x     ==  win.rect.center.x           →  anchoredPosition.x = -content.center.x
//
// and no term in either is a guess. Against the numbers above that is
// `anchoredPosition = (0.00, 20.4 - 30.0) = (0.00, -9.6)`, which puts the button's 65 local units of
// visible content across `win.rect.yMin + 20.4 … + 85.4` — the bottom 8 % of a 1021-unit card, ON
// the card. Compare 191's -115.4, which is where "hanging below" came from.
//
// IT IS A FIXED POINT, so it converges in ONE step and cannot oscillate: the content union is
// measured in CONTAINER-LOCAL space, and moving the container moves its children with it, so writing
// `anchoredPosition` does not change `content.yMin` or `content.center.x`. The solve is re-run every
// tick anyway — the game re-labels the button between "Reisen" and "Quest erneut spielen"
// (OnSelectedMapLocation :356) and shows/hides the container (EnableTravelOptions :408-415), and a
// uGUI layout is not final on the frame of a reparent — but the WRITE IS SKIPPED whenever the answer
// already stands, so a steady state costs one corner sweep and no transform writes at all.
//
// WHY THE INSET IS 2 % OF THE WINDOW'S HEIGHT. It is a FRACTION and not a pixel or millimetre count
// because the player can resize a floated window, and the thing the eye judges is the button's
// relationship to the card it sits on, not its absolute distance from an edge. The NUMBER is 191's
// number: 2 % was the one quantity in 191 the user did not object to (he objected to the direction
// and to the x-axis, both of which change here). Reusing it means exactly ONE thing differs between
// the rejected build and this one — which SIDE of `win.rect.yMin` the content sits on — so if the
// next report still says it is wrong, the disagreement isolates the TARGET and not the magnitude,
// and a magnitude change is then a one-constant edit against a measured line.
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
// THE ONE-FRAME FLASH AT THE OLD POSITION — STILL FIXED, WITH A PARENTED CONTAINER
// ===========================================================================================
//
// "Außerdem flackert er, für einen Frame sieht man ihn an seiner alten position."
//
// THE CAUSE, from 191's own code path. `Reconcile` set `anchoredPosition = Vector2.zero` at park
// time and `AlignFooter` returned immediately while the container was not `activeInHierarchy` —
// which is its state until the game's `EnableTravelOptions(true)` runs. ZERO IS THE ModBuild 190
// POSE. So the game showed the container, it drew one frame far below the window, and the next
// tick's solve moved it up.
//
// TWO RULES KILL IT HERE, and neither of them is a Harmony patch:
//
//   (1) `anchoredPosition` IS NEVER ZEROED. Park sets parent, sibling index, anchors, pivot,
//       rotation and scale, and deliberately leaves the offset alone until the solve has a
//       measurement to write. There is no longer any code path in this file that can produce the
//       190 pose.
//   (2) THE CONTAINER IS HELD RENDER-DOWN UNTIL A SOLVED POSE IS ON IT. 192 got this by owning a
//       host that is born render-hidden; a parented container has no host of its own, so the
//       equivalent is a CanvasGroup ON THE CONTAINER (added by us if it has none), alpha 0 and
//       blocksRaycasts off. It is set the moment we park — BEFORE the SetParent, so there is not
//       even a frame of the reparent to see — and it is set again whenever the game has the
//       container inactive, so the steady hidden state is ALSO alpha 0. That is what makes the rule
//       total: on the frame the game calls `SetActive(true)`, the alpha is already 0 no matter
//       whether our tick runs before or after that call, so nothing can be drawn at an unsolved
//       pose. Alpha goes back up only once the container is active, a pose has been WRITTEN, and the
//       content measurement has REPEATED ITSELF (two agreeing measurements, because a uGUI layout is
//       not final on the frame an object is enabled and the game changes the button's label — and
//       therefore its width — between shows).
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
// write war, and it would buy nothing rule (2) does not already give.
//
// BOUNDED. If the measurement never repeats itself, the button is revealed anyway after
// ShowDeadlineSeconds with a Warn: a confirm button that never appears is a worse failure than one
// that appears a few pixels off for a frame — the same ruling the modal reveal gate carries.
//
// ===========================================================================================
// RESTORE DISCIPLINE
// ===========================================================================================
//
// The container's home is recorded ONCE, before the first move: parent, sibling index, anchorMin,
// anchorMax, pivot, anchoredPosition, localRotation, localScale — plus the CanvasGroup's alpha and
// blocksRaycasts if it had one, or the fact that we added it. All of it is written back verbatim on
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
/// quest window, in that window's own lower area — and switches off the single-player double-click
/// shortcut that committed without asking. Installed by <see cref="MapRoomDriver"/>.
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

    // ---- the placement constants ---------------------------------------------------------------

    /// <summary>
    /// Distance from the window's BOTTOM EDGE UP TO the travel options' visible content, as a
    /// fraction of the window's own height. See the class doc for why it is a fraction and why the
    /// number is 2 %: it is 191's gap, reused unchanged so that the ONLY difference between the
    /// rejected build and this one is which side of the edge the content sits on.
    /// </summary>
    private const float InsetWindowHeights = 0.02f;

    /// <summary>
    /// Alpha below which a Graphic is not counted as visible CONTENT. uGUI bars routinely carry a
    /// full-width fully transparent Image as a raycast blocker; including one would make the measured
    /// content the width of the whole HUD bar and put its invisible bottom edge on the inset instead
    /// of the button's. Anything the player can actually see clears this by miles.
    /// </summary>
    private const float MinVisibleAlpha = 0.02f;

    /// <summary>How far the solved offset must move before it is written, and how far a content
    /// measurement may differ from the previous one and still count as "the same answer"
    /// (container-local uGUI units). Sub-pixel churn is not worth a transform write, and a rect
    /// rewritten every frame could never satisfy the two-agreeing-measurements reveal test.</summary>
    private const float OffsetEpsilon = 0.5f;

    /// <summary>
    /// Hard bound on how long the button may stay held down after the game showed it while the
    /// content measurement refuses to repeat itself. A confirm button that never appears is a worse
    /// failure than one that appears a few pixels off for a frame.
    /// </summary>
    private const float ShowDeadlineSeconds = 0.5f;

    /// <summary>Seconds between re-solve log lines. The FIRST solve always logs; after that only a
    /// materially different answer does, and never more often than this. A line that repeats on this
    /// cadence forever is the signature of another writer fighting us for anchoredPosition.</summary>
    private const float ResolveLogIntervalSeconds = 5f;

    /// <summary>Graphic sink for the content sweep — reused, so the per-tick measurement allocates
    /// nothing.</summary>
    private static readonly List<Graphic> ContentGraphics = new(32);

    /// <summary>World-corner scratch for the same sweep.</summary>
    private static readonly Vector3[] CornerScratch = new Vector3[4];

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

    // ---- the hold-down that replaces 192's render-hidden host ----------------------------------

    private static CanvasGroup? _hold;
    private static bool _holdAdded;
    private static float _holdHomeAlpha = 1f;
    private static bool _holdHomeBlocksRaycasts = true;
    private static bool _hidden;

    // ---- solved state ---------------------------------------------------------------------------

    private static Rect _content;
    private static Rect _lastContent;
    private static bool _contentMeasured;
    private static bool _contentRepeated;
    private static bool _contentFallbackLogged;
    private static Vector2 _appliedOffset;
    private static bool _solved;

    private static bool _wasActive;
    private static float _activeSince;
    private static bool _revealForcedLogged;

    private static bool _footerLogged;
    private static float _footerLoggedAt = float.NegativeInfinity;
    private static Vector2 _loggedOffset;
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
                          + "the floated quest window, in its own lower area, so they can be reached at "
                          + "all. Travel now commits through that button and through nothing else.");
    }

    /// <summary>
    /// Level-triggered, one call per tick from <see cref="MapRoomDriver"/>. Parks the travel options
    /// inside <paramref name="questWindow"/> while that window is floated, keeps their offset solved
    /// against the window's own lower edge, and hands them back otherwise. Idempotent: a steady state
    /// costs one corner sweep and no writes.
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
                // A fresh show may carry a different label ("Reisen" vs "Quest erneut spielen"), which
                // changes the content width: the agreement test starts over, so the button stays held
                // down until two measurements agree AND the pose has been written for them.
                _contentRepeated = false;
                _revealForcedLogged = false;
            }
        }

        AlignFooter(questWindow, options, mgr);
        TickVisibility(active);

        if (!_reported && _solved)
        {
            _reported = true;
            var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
            VRLog.Info(Scope, "MAP TRAVEL CONFIRM: the game's travel options were moved from "
                              + $"'{(_optionsHome != null ? _optionsHome.name : "<none>")}' INTO the floated "
                              + $"quest window '{questWindow.name}' and solved onto that window's own lower "
                              + "area (user ruling: \"soll teil des Fensters sein … innerhalb des Fensters "
                              + "den Button etwas nach oben\"). The button itself is "
                              + $"'{(btn != null ? btn.name : "<not found>")}' — the SAME ExtendedButton the "
                              + "flat game uses, so its label (Reisen / Quest erneut spielen), its "
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
    /// <para>ORDER MATTERS: the hold-down goes on BEFORE the reparent, so there is not even a frame
    /// of the move to see, and <c>anchoredPosition</c> is deliberately NOT written here — zero is the
    /// ModBuild 190 pose, and writing it is precisely what produced the one-frame flash in 191. The
    /// offset is written by the first solve that has something to measure, and until then the
    /// container is alpha 0.</para>
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

        _host = questWindow;
        rect.SetParent(win, worldPositionStays: false);
        rect.SetAsLastSibling();
        // THE FRAME THE SOLVE REASONS IN. Bottom-centre anchor with a top-edge pivot, so that
        // anchoredPosition is a plain offset from (win.rect.center.x, win.rect.yMin) to the
        // container's own local origin — which is what makes AlignFooter two subtractions and no
        // matrix work. Every one of these is recorded above and written back verbatim on unpark.
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        _solved = false;
        _contentMeasured = false;
        _contentRepeated = false;
        _appliedOffset = Vector2.zero;
        _footerLogged = false;
        _footerLoggedAt = float.NegativeInfinity;
        _loggedOffset = Vector2.zero;
        _wasActive = options.activeInHierarchy;
        _activeSince = Time.unscaledTime;
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
        if (_host == null && _hold == null)
        {
            // Nothing parked. Still clear the one-per-visit diagnostic latches, so a second visit
            // re-reports a failure instead of failing silently.
            _contentFallbackLogged = false;
            _revealForcedLogged = false;
            _parkWarned = false;
            return;
        }

        UIWindow? host = _host;
        _host = null;
        ReleaseHold();

        _solved = false;
        _contentMeasured = false;
        _contentRepeated = false;
        _appliedOffset = Vector2.zero;
        _footerLogged = false;
        _footerLoggedAt = float.NegativeInfinity;
        _loggedOffset = Vector2.zero;
        _wasActive = false;
        _contentFallbackLogged = false;
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
                          + "move, and the hold-down (CanvasGroup alpha / blocksRaycasts) released or "
                          + "destroyed. Nothing of ours is left on the game's object.");
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

    /// <summary>
    /// THE ANTI-FLASH GATE. The container is drawn only while the game has it shown AND a repeated
    /// content measurement has been written to <c>anchoredPosition</c> — so there is no frame in
    /// which the button is visible at a pose that is not its final one. Whenever the game has it
    /// hidden the hold goes back ON, which is what makes the rule total: on the frame the game calls
    /// SetActive(true) the alpha is already 0, whichever order the two run in.
    ///
    /// <para>BOUNDED: if the measurement never repeats, the button is shown anyway after
    /// <see cref="ShowDeadlineSeconds"/> with a Warn.</para>
    /// </summary>
    private static void TickVisibility(bool active)
    {
        bool ready = _solved && _contentRepeated;
        if (!ready && active && _solved && Time.unscaledTime - _activeSince >= ShowDeadlineSeconds)
        {
            ready = true;
            if (!_revealForcedLogged)
            {
                _revealForcedLogged = true;
                VRLog.Warn(Scope, "MAP TRAVEL CONFIRM: the travel options are being revealed before the "
                                  + "content measurement repeated itself (deadline "
                                  + $"{ShowDeadlineSeconds * 1000f:F0} ms since the game enabled them). "
                                  + "CONSEQUENCE: the button may sit a few pixels off for a frame or two "
                                  + "while the uGUI layout finishes, and will then settle. A button that "
                                  + "never appears would be the worse failure, so visibility wins. If this "
                                  + "line repeats every single time, the container's layout is being "
                                  + "re-driven every frame and the measurement can never agree with "
                                  + "itself — that is the bug to chase, not the deadline.");
            }
        }
        bool wantVisible = active && ready;
        if (_hidden == wantVisible)
            SetHidden(!wantVisible);
    }

    // ---- the measured placement --------------------------------------------------------------------

    /// <summary>
    /// Slide the parked container so its VISIBLE CONTENT sits ON the quest window, one small inset
    /// above that window's bottom edge, horizontally centred on it. Solved, not tuned — the class doc
    /// carries the arithmetic and the ruling this target comes from.
    /// </summary>
    private static void AlignFooter(UIWindow host, GameObject options, AdventureMapUIManager? mgr)
    {
        if (host == null || options == null)
            return;
        // The game hides the whole container (EnableTravelOptions -> travelOptions.SetActive(flag)).
        // An inactive subtree has no laid-out rects to measure, so the last solved offset simply
        // stands until it comes back — and the hold-down keeps it invisible meanwhile.
        if (!options.activeInHierarchy)
            return;
        if (options.transform is not RectTransform rect || host.transform is not RectTransform win)
            return;

        bool measured = TryContentBounds(rect, out Rect content, out int counted, out int skipped);
        if (!measured)
        {
            // NOTHING VISIBLE TO MEASURE. Fall back to the container's own rect and say so, because
            // "it went back to hanging below the window" then has a named cause instead of being a
            // mystery: the container's rect is the flat HUD bar's, 512 x 0 with its origin at the top.
            content = rect.rect;
            if (!_contentFallbackLogged)
            {
                _contentFallbackLogged = true;
                VRLog.Warn(Scope, $"MAP TRAVEL CONFIRM: the travel options container '{options.name}' "
                                  + $"exposed no visible Graphic to measure ({skipped} candidate(s) "
                                  + "skipped as disabled, transparent or zero-sized), so the placement "
                                  + "falls back to the CONTAINER'S OWN RECT. CONSEQUENCE: the confirm "
                                  + "button may appear off-centre and far below where this class reports "
                                  + "it — that is exactly the ModBuild 190 report — because that rect is "
                                  + "the flat HUD bar's and the button sits somewhere inside it. Nothing "
                                  + "throws.");
            }
        }

        float inset = InsetWindowHeights * Mathf.Abs(win.rect.height);
        // THE SOLVE. Content BOTTOM edge to win.rect.yMin + inset (inside the window, near its lower
        // edge); content CENTRE x to the window's centre. A fixed point: the union is measured in
        // CONTAINER-LOCAL space, and moving the container moves its children with it, so this
        // converges in one step and cannot oscillate.
        var want = new Vector2(-content.center.x, inset - content.yMin);
        if ((rect.anchoredPosition - want).sqrMagnitude > OffsetEpsilon * OffsetEpsilon)
            rect.anchoredPosition = want;   // SKIPPED when the answer already stands
        _appliedOffset = want;
        _solved = true;

        // TWO AGREEING MEASUREMENTS, NOT ONE — the reveal test. A uGUI layout is not final on the
        // frame an object is enabled or reparented, and the game re-labels this button between
        // "Reisen" and "Quest erneut spielen", which changes its width and therefore its centre.
        bool repeats = _contentMeasured
                       && Mathf.Abs(content.center.x - _lastContent.center.x) <= OffsetEpsilon
                       && Mathf.Abs(content.yMin - _lastContent.yMin) <= OffsetEpsilon
                       && Mathf.Abs(content.width - _lastContent.width) <= OffsetEpsilon
                       && Mathf.Abs(content.height - _lastContent.height) <= OffsetEpsilon;
        _contentRepeated = _contentRepeated || repeats;
        _lastContent = content;
        _content = content;
        _contentMeasured = true;

        bool changed = (want - _loggedOffset).sqrMagnitude > OffsetEpsilon * OffsetEpsilon;
        if (_footerLogged && (!changed || Time.unscaledTime - _footerLoggedAt < ResolveLogIntervalSeconds))
            return;
        _footerLogged = true;
        _footerLoggedAt = Time.unscaledTime;
        _loggedOffset = want;
        LogFooter(host, options, mgr, rect, win, content, counted, skipped, measured, inset, want);
    }

    /// <summary>
    /// The union of the container's ACTIVE, VISIBLE child Graphics' rects, expressed in the
    /// container's own local space.
    ///
    /// <para>GRAPHICS AND NOT RECTTRANSFORMS, deliberately. A uGUI bar is full of layout groups and
    /// empty spacers whose rects are far larger than anything drawn in them; taking every
    /// RectTransform would measure the bar's skeleton, which is the same mistake as measuring the
    /// container. A Graphic is the only component that PAINTS, so the union of the enabled,
    /// non-transparent, non-degenerate ones is "what the player can see", which is the thing the user
    /// is judging the position of.</para>
    ///
    /// <para>Corner-based, not rect-based: a child may sit several transforms deep, so its rect is in
    /// ITS parent's space. <c>GetWorldCorners</c> + <c>InverseTransformPoint</c> lands every corner in
    /// the container's frame with no assumption about the chain between them.</para>
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

    /// <summary>
    /// THE MEASUREMENT LINE. Every number the placement is derived from, in the frame it was read in,
    /// plus the resulting inset in three units — window heights (scale-free, the thing the eye
    /// judges), local units, and real millimetres at the current rig scale. If the next hardware
    /// report says the button is in the wrong place, this line says WHICH input was wrong; the
    /// DISPROOF block at the bottom names the two failures apart.
    /// </summary>
    private static void LogFooter(UIWindow host, GameObject options, AdventureMapUIManager? mgr,
                                  RectTransform rect, RectTransform win, Rect content,
                                  int counted, int skipped, bool measured, float inset, Vector2 want)
    {
        var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
        string btnWhere = "<not found>";
        if (btn != null)
        {
            Vector3 local = rect.InverseTransformPoint(btn.transform.position);
            btnWhere = $"'{btn.name}' active={btn.gameObject.activeInHierarchy} at world "
                       + $"{btn.transform.position}, i.e. local y={local.y:F1} inside the container";
        }
        // RIG SCALE IS NAMED, NOT ASSUMED. The map room runs at ~198 world units per real metre, so a
        // world length divided by the rig scale is what turns it back into REAL metres — the unit the
        // user judges "zu weit weg" in. Mixing the two silently has shipped as a bug in this very
        // file's neighbourhood, so both are printed.
        float rigScale = RigScale();
        float insetWorld = inset * Mathf.Abs(win.lossyScale.y);
        float insetMeters = insetWorld / rigScale;
        // Built as separate locals rather than inlined: a nested interpolated string inside another
        // one is legal C# but it defeats the repo's own source scanners (scripts/patch-inventory.py
        // walks string literals with a single-quote-depth reader), and a tool that mis-parses this
        // file reports its Harmony patch as unregistered. Not worth the saved locals.
        string contentHow = measured
            ? $"{counted} visible Graphic(s) counted, {skipped} skipped as disabled/transparent/zero-sized"
            : "NOT MEASURABLE — the container's own rect is standing in";
        string holdHow = _hidden ? "HELD DOWN (alpha 0)" : "revealed";
        VRLog.Info(Scope,
            $"MAP TRAVEL CONFIRM placement SOLVED — INSIDE the quest window '{host.name}', on its own "
            + "lower area (ModBuild 193 reverts 192's detached world host by user ruling: \"soll teil "
            + "des Fensters sein … innerhalb des Fensters den Button etwas nach oben\").\n"
            + $"  container : '{options.name}' rect {rect.rect} — the flat HUD bar's own rect. Pinning "
            + "THIS rect's top edge to the window (anchoredPosition = zero) is what ModBuild 190 did "
            + "and why the button hung far below the card; nothing here ever writes zero.\n"
            + $"  content   : {contentHow}; union in CONTAINER-LOCAL space {content} (BOTTOM edge "
            + $"y={content.yMin:F1}, centre x={content.center.x:F1}).\n"
            + $"  button    : {btnWhere}.\n"
            + $"  window    : rect {win.rect} (height {Mathf.Abs(win.rect.height):F1}), lossyScale "
            + $"{win.lossyScale.y:F4} world units per local unit; rig scale {rigScale:F2} world units "
            + "per REAL metre.\n"
            + $"  applied   : anchoredPosition = {want} — DERIVED as (-content.center.x, "
            + "inset - content.yMin), never a tuned pixel count. The container is currently "
            + $"{holdHow}.\n"
            + $"  inset     : {InsetWindowHeights:P1} of the window's height = {inset:F1} local units = "
            + $"{insetWorld:F3} world units = {insetMeters * 1000f:F1} mm real, measured UPWARD from the "
            + "window's bottom edge to the content's bottom edge — so the button sits ON the card, "
            + "near its lower edge, for either label (Reisen / Quest erneut spielen) and at any window "
            + "size, because both terms are re-measured every tick.\n"
            + "  DISPROOF  : if the button is still wrong, there are exactly two candidates and they "
            + "are told apart HERE — (1) 'content' DISAGREES with 'button' (the union's bottom edge "
            + "sits far below the button's own local y) ⇒ the Graphic union caught something the "
            + "player cannot see, and the inset is being measured from an invisible edge; raise "
            + "MinVisibleAlpha or exclude the offender. (2) 'content' and 'button' AGREE and the "
            + "position is still wrong ⇒ anchoredPosition is being overwritten by another writer, and "
            + $"THIS LINE WILL REPEAT on its {ResolveLogIntervalSeconds:F0} s cadence instead of being "
            + "printed once. A single line and agreeing numbers mean the solve is doing what it says "
            + "and only the INSET FRACTION is up for debate.");
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
