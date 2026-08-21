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
// SO THE POSITION PATH CONTAINS NO SOLVE AT ALL. There is no content measurement in it, no window
// edge in it, no inset constant in it. The applied offset is exactly
//
//     anchoredPosition = (OffsetX * windowHeight, OffsetY * windowHeight)
//
// and with the shipped defaults of 0 and 0 that is `Vector2.zero` — the same two floats ModBuild 190
// wrote, through the same `RectTransform.anchoredPosition` setter, after the same anchors and pivot,
// in the same place in the same method. Byte for byte.
//
// THE FRAME THE DIALS LIVE IN, stated once. With `anchorMin = anchorMax = (0.5, 0)` the anchor
// reference point in the WINDOW'S local space is `(win.rect.center.x, win.rect.yMin)` — the middle
// of the window's bottom edge. With `pivot = (0.5, 1)` the container's local origin is its own pivot
// point, so `anchoredPosition` is precisely the offset from that bottom-edge anchor to the container.
// +x is the window's own RIGHT, +y is the window's own UP. The container carries identity rotation
// and unit scale, so the dials move the button in the plane of the card and nowhere else.
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
// THE CLAMPS, AND WHAT THEY ARE IN REAL MILLIMETRES. From the one hardware line this file has
// (ModBuild 191's): the quest window's rect is 512 x 1021 local units at lossyScale 0.1734 world
// units per local unit, and the map room runs at a rig scale of ~198 world units per REAL metre, so
// one local unit is 0.1734 / 198 m = 0.876 mm real. That makes the card 448 mm wide and 894 mm tall
// in front of the player's face, and the clamps
//
//     OffsetX  in  -0.5 … +0.5  window heights  =  -447 … +447 mm  (the card's side edges are at
//                                                  ±0.25 = ±224 mm, so either extreme is half a
//                                                  window WIDTH outside the edge)
//     OffsetY  in  -0.5 … +1.5  window heights  =  -447 … +1341 mm  (0 is the card's BOTTOM edge and
//                                                  +1.0 = +894 mm is its TOP edge, so either extreme
//                                                  is half a window HEIGHT outside the card)
//
// cover the whole card and a generous margin all round it. The Y range is deliberately asymmetric
// because the ZERO of this axis is an EDGE and not a centre; a symmetric ±1 would waste half its
// travel under the floor and still not reach past the top edge.
//
// NOTHING IN THAT BOX IS UNREACHABLE, which is the standing bound on any dial. The extremes trace a
// box that is the window's own outline grown by half a window height (447 mm) on every side, and the
// button is still a CHILD of the window: it moves, scales, occludes, floats and goes home with it,
// so no dial value can strand it somewhere in the world, behind the player, or under the table while
// the window is elsewhere. It is hit by the same raycaster and the same laser that already hit the
// window the player is looking at, and its CanvasGroup carries blocksRaycasts whenever it is
// visible, so it is clickable everywhere inside that box. And a setting may only make OPTIONAL
// content optional: these are PLACEMENT dials for content that is always present — no value of
// either one removes the confirm button, changes what it does, or affects game state.
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
    /// HEIGHT, either way from the window's centre line. ±0.5 ≈ ±447 mm at the measured rig scale —
    /// half a window WIDTH beyond either side edge of the card.</summary>
    internal const float OffsetLimitX = 0.5f;

    /// <summary>Lowest vertical offset, in fractions of the window's height, measured UP from the
    /// window's BOTTOM edge (which is where 0 sits). -0.5 ≈ 447 mm below that edge.</summary>
    internal const float OffsetLimitYMin = -0.5f;

    /// <summary>Highest vertical offset, same frame and unit. +1.0 is the window's TOP edge, so
    /// +1.5 ≈ 447 mm above it.</summary>
    internal const float OffsetLimitYMax = 1.5f;

    // ---- the other constants ---------------------------------------------------------------------

    /// <summary>
    /// Alpha below which a Graphic is not counted as visible CONTENT in the LOG's content sweep. uGUI
    /// bars routinely carry a full-width fully transparent Image as a raycast blocker, and reporting
    /// the whole HUD bar's width as "what the player sees" would make the evidence line useless.
    /// Nothing derived from this reaches the placement.
    /// </summary>
    private const float MinVisibleAlpha = 0.02f;

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

    /// <summary>Graphic sink for the log's content sweep — reused, so it allocates nothing.</summary>
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
                          + "the floated quest window at the ModBuild 190 pose (anchoredPosition = zero "
                          + "against the window's bottom-edge anchor), movable from there with the live "
                          + "[WorldUI] TravelButtonOffsetXWindowHeights / …YWindowHeights dials. Travel "
                          + "now commits through that button and through nothing else.");
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
                _revealForcedLogged = false;
        }

        PlaceButton(questWindow, options, mgr);
        TickVisibility(active);

        if (!_reported && _posed)
        {
            _reported = true;
            var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
            VRLog.Info(Scope, "MAP TRAVEL CONFIRM: the game's travel options were moved from "
                              + $"'{(_optionsHome != null ? _optionsHome.name : "<none>")}' INTO the floated "
                              + $"quest window '{questWindow.name}' at the ModBuild 190 pose (user ruling: "
                              + "\"Mach die Position des Quest Buttons ganz rückgängig wie es das erste mal "
                              + "war\"), offset from there only by the two [WorldUI] TravelButtonOffset "
                              + "dials — both 0 by default, which is that pose exactly. The button itself "
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

        _host = questWindow;
        _posed = false;
        _placementLogged = false;
        _placementLoggedAt = float.NegativeInfinity;
        _loggedOffset = Vector2.zero;
        _wasActive = options.activeInHierarchy;
        _activeSince = Time.unscaledTime;

        // THE ModBuild 190 SEQUENCE, IN ITS ORIGINAL ORDER — compare git show ac270f4 on this file.
        // Bottom-centre anchor with a top-edge pivot, so that anchoredPosition is a plain offset from
        // (win.rect.center.x, win.rect.yMin) to the container's own local origin, which is what makes
        // the two dials a pair of multiplications and no matrix work. The ApplyPose call stands where
        // 190's `rect.anchoredPosition = Vector2.zero` stood and writes that same value while both
        // dials are 0. Every one of these is recorded above and written back verbatim on unpark.
        rect.SetParent(win, worldPositionStays: false);
        rect.SetAsLastSibling();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        ApplyPose(rect, win, out _, out _, out _);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
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
            _revealForcedLogged = false;
            _parkWarned = false;
            return;
        }

        UIWindow? host = _host;
        _host = null;
        ReleaseHold();

        _posed = false;
        _placementLogged = false;
        _placementLoggedAt = float.NegativeInfinity;
        _loggedOffset = Vector2.zero;
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
                                  + "THE CAUSE IS ALWAYS THE SAME ONE: the floated quest window's rect "
                                  + "reports no height, so a non-zero [WorldUI] TravelButtonOffset dial "
                                  + "has nothing to be a fraction OF and the offset would land somewhere "
                                  + "arbitrary. CONSEQUENCE: the button is showing wherever it last stood "
                                  + "rather than where the dials say. A button that never appears would be "
                                  + "the worse failure, so visibility wins. Set both dials back to 0 to "
                                  + "get the ModBuild 190 pose, which needs no window height at all.");
            }
        }
        bool wantVisible = active && ready;
        if (_hidden == wantVisible)
            SetHidden(!wantVisible);
    }

    // ---- the placement -----------------------------------------------------------------------------

    /// <summary>
    /// The two dials, read LIVE and clamped to the same bounds the bind site advertises. Fractions of
    /// the quest window's own height; +x is the window's right, +y is up from its bottom edge.
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
    /// Write the dialled offset onto the parked container. NO SOLVE, NO MEASUREMENT — with both dials
    /// at 0 this writes <c>Vector2.zero</c>, which is the ModBuild 190 pose the user asked to have
    /// back, and no other input can change that. Level-triggered: the write is skipped whenever the
    /// rect already carries the wanted value, so a steady state costs one compare.
    /// </summary>
    /// <returns>True when the container now carries the dialled offset.</returns>
    private static bool ApplyPose(RectTransform rect, RectTransform win,
                                  out Vector2 want, out Vector2 dials, out float windowHeight)
    {
        dials = Dials();
        windowHeight = Mathf.Abs(win.rect.height);
        want = Vector2.zero;
        // A window whose rect has no height yet (a uGUI layout is not final on the frame a window is
        // floated) has nothing for a FRACTION to be a fraction of. Zero dials are exempt, because
        // zero times anything is the pose we want anyway; anything else waits, and the hold-down in
        // TickVisibility keeps the button off-screen while it does.
        if (windowHeight <= 0f && (dials.x != 0f || dials.y != 0f))
        {
            _posed = false;
            return false;
        }
        want = new Vector2(dials.x * windowHeight, dials.y * windowHeight);
        if ((rect.anchoredPosition - want).sqrMagnitude > OffsetEpsilon * OffsetEpsilon)
            rect.anchoredPosition = want;   // SKIPPED when the rect already carries the answer
        _posed = true;
        return true;
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

        if (!ApplyPose(rect, win, out Vector2 want, out Vector2 dials, out float windowHeight))
            return;

        // LOG ONLY WHEN THERE IS SOMETHING NEW TO SAY, and only while the button is actually on
        // screen — the numbers a tuner needs (where the button ended up) do not exist for a container
        // the game has switched off, and its child rects are not laid out either.
        if (!options.activeInHierarchy)
            return;
        bool changed = (want - _loggedOffset).sqrMagnitude > OffsetEpsilon * OffsetEpsilon;
        if (_placementLogged
            && (!changed || Time.unscaledTime - _placementLoggedAt < ResolveLogIntervalSeconds))
            return;
        _placementLogged = true;
        _placementLoggedAt = Time.unscaledTime;
        _loggedOffset = want;
        LogPlacement(host, options, mgr, rect, win, want, dials, windowHeight);
    }

    /// <summary>
    /// The union of the container's ACTIVE, VISIBLE child Graphics' rects, expressed in the
    /// container's own local space. EVIDENCE ONLY since ModBuild 194 — nothing it returns reaches
    /// <c>anchoredPosition</c>; it exists so the next hardware report can say where the button
    /// actually ended up for a given pair of dial values.
    ///
    /// <para>GRAPHICS AND NOT RECTTRANSFORMS, deliberately. A uGUI bar is full of layout groups and
    /// empty spacers whose rects are far larger than anything drawn in them. A Graphic is the only
    /// component that PAINTS, so the union of the enabled, non-transparent, non-degenerate ones is
    /// "what the player can see".</para>
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
    /// THE LINE THE USER TUNES FROM. It has to answer one question — "I set the dials to THAT; where
    /// did the button go?" — so it prints the two dial values, everything they are multiplied by
    /// (the window rect, its lossyScale, the rig scale), the resulting anchoredPosition, and then the
    /// ANSWER: the button's world position and its offset from the window's CENTRE in real
    /// millimetres. The container rect and the visible-content union are carried as evidence.
    /// </summary>
    private static void LogPlacement(UIWindow host, GameObject options, AdventureMapUIManager? mgr,
                                     RectTransform rect, RectTransform win, Vector2 want,
                                     Vector2 dials, float windowHeight)
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
            btnWhere = $"'{btn.name}' active={btn.gameObject.activeInHierarchy}, world position "
                       + $"{btn.transform.position} (WORLD units), i.e. {dxLocal * mmPerLocalX:F0} mm "
                       + $"right and {dyLocal * mmPerLocalY:F0} mm up from the window's CENTRE, in REAL "
                       + "millimetres";
        }
        // Built as separate locals rather than inlined: a nested interpolated string inside another
        // one is legal C# but it defeats the repo's own source scanners (scripts/patch-inventory.py
        // walks string literals with a single-quote-depth reader), and a tool that mis-parses this
        // file reports its Harmony patch as unregistered. Not worth the saved locals.
        string contentHow = TryContentBounds(rect, out Rect content, out int counted, out int skipped)
            ? $"{counted} visible Graphic(s) ({skipped} skipped as disabled/transparent/zero-sized), "
              + $"union in CONTAINER-LOCAL space {content}"
            : "NOT MEASURABLE — no visible Graphic in the container";
        string holdHow = _hidden ? "HELD DOWN (alpha 0)" : "revealed";
        string poseHow = dials == Vector2.zero
            ? "BOTH DIALS AT 0, so this is byte-for-byte the ModBuild 190 pose the user asked for"
            : "offset from the ModBuild 190 pose by the two dials";
        float mmPerTenth = 0.1f * windowHeight * mmPerLocalY;
        VRLog.Info(Scope,
            $"MAP TRAVEL CONFIRM placement — INSIDE the quest window '{host.name}', {poseHow}.\n"
            + $"  dials     : [WorldUI] TravelButtonOffsetXWindowHeights = {dials.x:F3}, "
            + $"TravelButtonOffsetYWindowHeights = {dials.y:F3} (fractions of the WINDOW'S HEIGHT; "
            + $"+x = right, +y = up from the window's BOTTOM edge). Clamped to {-OffsetLimitX:F2}…"
            + $"{OffsetLimitX:F2} and {OffsetLimitYMin:F2}…{OffsetLimitYMax:F2}. Read live, every "
            + "tick — turn them and the button moves on the next frame.\n"
            + $"  window    : rect {win.rect} (height {windowHeight:F1} local units), lossyScale "
            + $"{win.lossyScale.y:F4} WORLD units per local unit; rig scale {rigScale:F2} WORLD units "
            + $"per REAL metre, so one local unit is {mmPerLocalY:F3} mm real and the whole card is "
            + $"{windowHeight * mmPerLocalY:F0} mm tall. 0.1 on either dial = {mmPerTenth:F0} mm.\n"
            + $"  container : '{options.name}' rect {rect.rect} — the flat HUD bar's own rect, moved "
            + "here whole. Its TOP-edge pivot sits on the window's bottom-edge anchor at "
            + "anchoredPosition = zero, which is exactly what ModBuild 190 wrote.\n"
            + $"  applied   : anchoredPosition = {want} = (dial.x, dial.y) x {windowHeight:F1}. "
            + $"Nothing else is in this number — no content measurement, no window edge, no inset. "
            + $"The container is currently {holdHow}.\n"
            + $"  button    : {btnWhere}.\n"
            + $"  content   : {contentHow} — EVIDENCE ONLY (ModBuild 194): the visible-graphic sweep "
            + "191 and 193 solved their placements from still runs, but nothing it returns can reach "
            + "anchoredPosition. It is here so this line can say where the button ended up.\n"
            + "  TUNING    : both dials are 0 = the ModBuild 190 pose, whole. To move the button UP "
            + "onto the card, raise Y (the card's TOP edge is Y = 1.0); to move it sideways, use X "
            + $"(the card's side edges are at X = ±{0.5f * Mathf.Abs(win.rect.width) / Mathf.Max(windowHeight, 0.0001f):F2}). "
            + "THIS LINE PRINTS ONLY WHEN THE ANSWER CHANGES, at most every "
            + $"{ResolveLogIntervalSeconds:F1} s — so a line that repeats forever at an unchanged "
            + "dial setting is not tuning noise, it is a second writer fighting us for "
            + "anchoredPosition, and THAT is the bug to chase.");
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
