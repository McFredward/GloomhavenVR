using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
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
// MULTIPLAYER: PRESENTATION ONLY, PER CLIENT, NOTHING ON THE WIRE. Everything below moves a
// GameObject the LOCAL client already owns (the flat HUD's travel-options container) onto a
// world-space host this client renders, and reproduces — locally — the branch the game itself takes
// when FFSNetwork.IsOnline. No game state is written, no action is sent, no field the netcode reads
// is touched, and a remote client's own copy of this class reaches its own decision from its own
// windows. Two clients in the same room may legitimately show the button at two different places,
// because they are standing in two different places.
//
// ===========================================================================================
// WHERE THE BUTTON GOES — THE FRAME, NOT THE ARITHMETIC (ModBuild 192)
// ===========================================================================================
//
// USER RULINGS, IN THE ORDER THEY WERE GIVEN. The newest wins; the older ones are recorded so a
// later round does not "restore" something that was already revised away:
//
//   190 (asked and answered): "the button in the quest window."
//   190 (after seeing it):    "Der Bestätigungsknopf ist zu weit weg auf der y-achse. Reduzier da
//                             gerne den Abstand."  → move it UP, closer to the info cards.
//   191 (after seeing it):    "Der 'Quest erneut spielen' Button … nun völlig deplaziert, er soll
//                             zentral UNTER dem Rest angezeigt werden. Außerdem flackert er, für
//                             einen Frame sieht man ihn an seiner alten position. Von der alten
//                             Position wollte ich nur das du es etwas mehr nach oben schiebst näher
//                             an die Infokarten, nun ist es viel zu weit oben und auch auf der
//                             x-achse verschoben."
//
// SO THE 190/191 FRAME IS RETIRED BY RULING. It was the QUEST WINDOW: 191 anchored the container to
// that window's bottom edge and solved the offset from the container's visible-Graphic content
// union. THE ARITHMETIC WAS RIGHT — its own hardware line proves it:
//
//   container : 'Travel Options' rect (x:-256.00, y:0.00, width:512.00, height:0.00)
//   content   : 2 visible Graphic(s) (1 skipped), union in CONTAINER-LOCAL space
//               (x:-153.50, y:30.00, width:307.00, height:65.00) (top edge y=95.0, centre x=0.0)
//   window    : rect (x:-256.00, y:-510.50, width:512.00, height:1021.00), lossyScale 0.1734
//   applied   : anchoredPosition = (0.00, -115.42)
//   gap       : 2.0 % of the window's height = 20.4 local units = 3.540 world = 17.9 mm real
//
// The button ended up snug under the quest window — CENTRED ON THAT WINDOW. But the quest window
// is one member of a set that the map room lays out on an ARC around the player, and it is not the
// middle one: it hangs wherever its arc slot put it, up and to one side. "Centred on the window"
// and "centred under the rest" are different frames, and the user is asking for the second.
//
// WHAT THIS BUILD SOLVES AGAINST: THE ENSEMBLE, MEASURED WHERE IT ACTUALLY IS.
//
//   * The set: every floated modal host that is ALIVE, ACTIVE and ALREADY REVEALED
//     (<see cref="TryEnsemble"/>). Hover cards are excluded because their pose belongs to the icon
//     the pointer is on and they churn on every mouseover — the same exclusion the arc's own window
//     count makes (ModBuild 184). The test used here is POKEABILITY: a floated
//     window is registered with UguiPokeSurfaces, a hover card is deliberately converted
//     `pokeable: false` (ModBuild 187), so the registry answers "is this a window the player can
//     press" without this file having to re-derive what a hover card is.
//   * The basis: the ensemble's own facing, i.e. the average of the counted hosts' +Z (uGUI renders
//     its front along −Z, so a host's +Z points AWAY from the viewer). On the arc that average IS
//     the direction the arc is centred on. `right = up × n` completes it. NOTHING HERE READS THE
//     HEAD — standing ruling, "nichts darf sich mit der Kopfbewegung neu ausrichten". Each window
//     is aimed at the spawn gaze of the moment IT spawned and then never moves again, and the
//     windows are that record; deriving from the windows inherits their anchoring exactly, which is
//     the only way to be head-free without inventing a room anchor of our own.
//   * The pose: every counted host's four world corners are projected into (right, up, n). The
//     button's CONTENT CENTRE is then placed at the lateral MIDPOINT of that spread (this is what
//     "zentral unter dem Rest" means — centred under the full horizontal extent, not under any one
//     window and not at the centroid, which an asymmetric set would pull sideways), at the DEPTH
//     midpoint (so it reads at the group's own reading distance rather than nearer or farther), and
//     one stated gap below the LOWEST corner in the whole set ("UNTER dem Rest").
//   * The gap is a FRACTION OF THE ENSEMBLE'S MEASURED HEIGHT, never a pixel or metre count. With
//     exactly one window that is the window's own height, so the number the user did NOT complain
//     about in 191 (2 % ≈ 17.9 mm real) is reproduced verbatim; with four it grows with the thing
//     it is a gap under.
//   * The SIZE is measured too: the host is scaled to the MEAN of the counted hosts' lossyScale, so
//     the button's text is drawn at the same physical size as the text of the windows it sits under
//     — and it inherits any resize the player gave those windows. Parked inside the quest window
//     (190/191) it inherited that one window's scale; this is the same idea, generalised.
//
// WHAT HAPPENS AT EACH COUNT. The arc has five slots (0, +34, −34, +68, −68 from the spawn gaze,
// filled centre-outward) plus a foreground stagger beyond that, and in the map room the character
// screen converts first and holds the CENTRE slot for the room's whole life (it is permanent and
// non-closable by user ruling), so the practical minimum is one and the centre is stable:
//   1 window  — lateral midpoint, depth midpoint and lowest edge are all that window's, so the
//               result is "centred under that card", i.e. what 190 asked for, arrived at through
//               the general rule instead of by anchoring to it.
//   4 windows — centred under the whole fan (character screen at 0, the others at ±34 / ±68),
//               below whichever of the four hangs lowest. The extent grows in the arc's discrete
//               steps as windows open and shrinks again as they close; the button follows, once.
//   0 windows — there is nothing to be under. The last solved pose is HELD (the button stays where
//               the player last saw it, which is the only place they could look for it) and, if
//               there has never been a solve, the container is left untouched in the game's own HUD
//               and ONE Warn names the consequence. It is never placed from the head.
//
// A WINDOW THE PLAYER DRAGGED AWAY STILL COUNTS — a decision, so it is written down. A grab-moved
// window keeps its arc slot but its pose is player-owned and may be anywhere. It is counted anyway,
// because the ruling is "zentral UNTER dem Rest" and a window the player deliberately parked
// somewhere is emphatically part of what they are looking at; excluding it would put the button
// under a set that no longer matches the room. The cost is bounded and visible: the button slides
// once, after the hand lets go, and the placement line names every window the bounds were taken
// from — so if the next report says "it followed a window I threw aside", the log already says which
// one, and the fix is a one-line exclusion rather than a re-derivation.
//
// WHY THIS FILE MEASURES THE WINDOWS ITSELF INSTEAD OF ASKING FOR AN AABB. The arc lane offers a
// world-axis bounding box of the floated set. THE BOTTOM EDGE would be interchangeable with the one
// computed here — but nothing else would, and the difference is not a preference:
//   * the arc is a CURVED, YAWED arrangement, so a world-axis box's X/Z centre is only "centred as
//     the player sees it" when the arc happens to line up with the world axes. What is needed is the
//     midpoint along the ENSEMBLE'S OWN lateral axis, and that needs the per-window poses;
//   * the button also has to be ORIENTED, and it may not take that from the head — a Bounds carries
//     no rotation, so the facing has to come from the windows either way;
//   * the button is SIZED to the ensemble's mean host scale, which a Bounds does not carry;
//   * a host still behind its reveal gate is in the world but has never been seen and is still
//     moving; counting it would place the button against a pose the player never saw.
// An ORIENTED accessor — centre, facing, half-extents and mean scale — would replace this census
// wholesale, and this file would rather consume one than own one. Nothing here re-implements the arc
// walk: no slot index, no angle and no radius is computed, only where hosts already stand.
//
// A LATER SPAWN MUST NOT STRAND IT, AND IT MUST NOT JITTER. Windows claim an arc slot at SPAWN and
// never move again — there is no longer any event that says "the ensemble changed pose", so this
// has to be a POLL. The solve therefore re-runs every tick from where the windows ACTUALLY are, and
// is applied through a SETTLE GATE: the candidate pose must hold still for
// <see cref="EnsembleSettleSeconds"/> before it is written. Polling is what makes a later spawn
// impossible to miss; the gate is what stops a drag from smearing the button across the room. It is
// legitimate here and not the stillness-gate mistake this project has made before (a gate never
// opens for state someone else rewrites every frame): a floated window's pose is STATIC unless a
// hand is on it, so the steady state genuinely stands still. A new window opening moves the button
// exactly once, a quarter second later; a window being dragged moves it exactly once, on release;
// and a steady room costs one corner sweep and no writes at all.
//
// ===========================================================================================
// THE ONE-FRAME FLASH AT THE OLD POSITION (ModBuild 192)
// ===========================================================================================
//
// "Außerdem flackert er, für einen Frame sieht man ihn an seiner alten position."
//
// THE CAUSE, from 191's own code path. `Reconcile` parked the container into the quest window and
// set `anchoredPosition = Vector2.zero` BEFORE solving — and the solve (`AlignFooter`) returned
// immediately whenever the container was not `activeInHierarchy`, which is exactly its state until
// the game's `EnableTravelOptions(true)` runs. Zero pins the CONTAINER'S top edge to the window's
// bottom edge, and the container is the flat HUD bar whose rect is 512x0 with the button laid out
// 30-95 units above its origin — i.e. zero IS the ModBuild 190 pose, down over the table. The game
// then showed the container, it drew one frame at that pose, and the next tick's solve moved it up.
//
// THE SEAM USED HERE IS "TAKE IT BEFORE IT IS EVER SHOWN" — the second option the round offered,
// and it needs no patch on EnableTravelOptions and no flag fight. The container is moved onto a
// WORLD-SPACE HOST of its own (<see cref="CanvasConversion.Convert"/>) as soon as there is an
// ensemble to place it against, WHICH IS BEFORE THE GAME SHOWS IT, and that host is born
// RENDER-HIDDEN behind the mod's existing reveal gate — the same gate that already delivers "das
// Fenster soll direkt an der richtigen Stelle erscheinen" for every floated window. So by the time
// EnableTravelOptions(true) runs, the object is already ours and already invisible; the game's own
// call needs no interception, because there is nothing left for it to reveal prematurely.
//
// WHY NOT PATCH EnableTravelOptions ANYWAY. A prefix/postfix there would have to hold the object
// down (inactive, or alpha 0 through a CanvasGroup) and hand it back later — i.e. a second owner of
// a piece of state the game writes, which is the write war this project has already lost once, and
// a Harmony patch on a method whose only job is SetActive. The conversion already owns a piece of
// state the game cannot see, so there is nothing to contest. If the flash ever comes back, the
// placement line below plus the conversion's own "Converted 'MapTravelOptions'" line date the take
// against the show, and THAT is the measurement that would justify a patch.
//
// The same rule covers every LATER show, because the game shows and hides this container repeatedly
// (EnableTravelOptions, and it re-labels the button between Reisen and 'Quest erneut spielen',
// which changes the content width): the host's render visibility follows
// "the container is active AND the content measurement has repeated itself AND the pose has been
// written", level-triggered. Two agreeing measurements, not one, because a uGUI layout is not final
// on the frame an object is enabled.
//
// WHY THIS IS NOT A WRITE WAR — the failure this project has already paid for once. The game owns
// `travelOptions.activeSelf` and this class NEVER writes it, reads it only as a signal. What this
// class writes is its OWN host's canvas/renderer enable state through
// <see cref="CanvasConversion.SetPanelRenderVisible"/>, which nothing in the game can see or
// contest, and it writes it only on a transition. While the conversion's own reveal gate is still
// pending, this class does not write visibility at all — the gate owns it, and two owners writing
// the same flag on alternate frames is precisely the alternation bug.
//
// ===========================================================================================
// RESTORE DISCIPLINE, AND WHO HOLDS IT NOW
// ===========================================================================================
//
// It moved INTO the conversion, which records strictly more than the hand-rolled park did:
// original parent, sibling index, anchorMin/Max, pivot, anchoredPosition, sizeDelta, localScale,
// localPosition and localRotation — captured once at Convert, written back verbatim by
// <see cref="CanvasConversion.Release"/>, which also unregisters the poke surface, restores the
// game's nested canvases and destroys the host. Release runs on stand-down, on teardown and when
// the container disappears. OWNERSHIP IS CHECKED FIRST: if the container is no longer parented
// under our host, the game has taken it back and we log and hand over rather than fight for it.
//
// DEGRADES SAFELY: every private member is resolved through AccessTools once. If any is missing —
// or the conversion refuses — ONE Warn names the consequence (the shortcut stays live / the button
// stays unreachable) and the class stands completely down. Nothing thrown, nothing on the wire, no
// rule library touched, no Harmony patch beyond the one prefix that has shipped since 190.
// ---------------------------------------------------------------------------

/// <summary>
/// Makes the game's own travel confirmation reachable in the 3D map room — centred under the
/// ensemble of floated windows, derived from where those windows actually are — and switches off
/// the single-player double-click shortcut that committed without asking. Installed by
/// <see cref="MapRoomDriver"/>.
/// </summary>
internal static class MapTravelConfirm
{
    private const string Scope = "MapRoom";

    private static bool _installed;
    private static bool _resolved;

    /// <summary>The WHOLE feature stands down: no placement AND no shortcut gate. Set only when the
    /// reflection the gate itself needs is missing, because a gate that cannot identify the staged
    /// location must not guess. The consequence — the double-press shortcut comes back — is named
    /// in the Warn that sets it.</summary>
    private static bool _standDown;

    /// <summary>Only the PLACEMENT stands down (the container could not be hosted, or the game took
    /// it back). The shortcut gate keeps running, because "travel must not commit by accident" is a
    /// standing ruling that does not depend on the button being reachable — and with the gate up,
    /// an unreachable button is a missing convenience rather than an accidental journey. Cleared
    /// when the room stands down, so re-entering the map room retries.</summary>
    private static bool _placeStandDown;

    private static FieldInfo? _locationToTravel;
    private static FieldInfo? _onConfirmCallback;
    private static FieldInfo? _travelOptions;
    private static FieldInfo? _travelButton;

    // ---- the mod-owned world host the container rides ----------------------------------------

    /// <summary>Host name for the conversion. Deliberately NOT the <c>Modal_</c> prefix the floated
    /// windows use: <see cref="TryEnsemble"/> counts that family, and the button must never be part
    /// of the set it is solved against.</summary>
    private const string HostName = "MapTravelOptions";

    /// <summary>The prefix <c>CanvasConversion.Convert</c> gives every floated modal window's host
    /// (<c>GloomhavenVR.Panel_Modal_…</c>, built in ModalFallback.8.Convert.cs from
    /// <c>$"Modal_{name}"</c>). This is the census filter — see <see cref="TryEnsemble"/>.</summary>
    private const string ModalHostPrefix = "GloomhavenVR.Panel_Modal_";

    private static ConvertedPanel? _panel;
    private static bool _convertWarned;
    private static bool _reported;

    // ---- the measured placement ---------------------------------------------------------------

    /// <summary>
    /// Gap between the LOWEST edge of the floated-window ensemble and the TOP of the travel
    /// options' visible content, as a fraction of the ENSEMBLE'S OWN measured height.
    ///
    /// <para>A fraction and not a length, because the thing it must look right against is the set
    /// of cards above it, and that set is not one fixed size. With a single window it is that
    /// window's height, which reproduces the ModBuild 191 gap exactly (2 % ≈ 17.9 mm real at the
    /// measured rig scale) — the one number in 191 the user did not object to.</para>
    /// </summary>
    private const float GapEnsembleHeights = 0.02f;

    /// <summary>
    /// Alpha below which a Graphic is not counted as visible CONTENT. uGUI bars routinely carry a
    /// full-width fully transparent Image as a raycast blocker; including one would make the
    /// measured content the width of the whole HUD bar and centre the button on an invisible
    /// rectangle. Anything the player can actually see clears this by miles.
    /// </summary>
    private const float MinVisibleAlpha = 0.02f;

    /// <summary>How far the solved content offset must move before it is written (host-local uGUI
    /// units). Sub-pixel churn is not worth a transform write, and a rect that is rewritten every
    /// frame can never satisfy the reveal gate's stillness test.</summary>
    private const float ContentEpsilonPx = 0.5f;

    /// <summary>
    /// How long the solved host pose must HOLD STILL before it is applied, seconds. This is the
    /// anti-jitter cadence, and it is a settle gate on the OUTPUT rather than a timer: a window
    /// opening or closing changes the answer once and the button follows a quarter second later; a
    /// window being dragged changes it every frame, so the gate simply never opens until the hand
    /// lets go. Legitimate here — unlike the stillness gate this project got wrong once — because a
    /// floated window's pose is STATIC unless a hand is on it, so the steady state genuinely stands
    /// still.
    /// </summary>
    private const float EnsembleSettleSeconds = 0.25f;

    /// <summary>Pose move, in REAL metres, below which the candidate counts as "the same answer"
    /// (both for the settle gate and for the decision to write). Converted to world units against
    /// the live rig scale at use, because those are the units transforms are written in.</summary>
    private const float PoseEpsilonMeters = 0.002f;

    /// <summary>
    /// Hard bound on how long the button may stay invisible after the game showed it while the
    /// content measurement refuses to repeat itself. A confirm button that never appears is a
    /// worse failure than one that appears a frame early — the same ruling the modal reveal gate
    /// carries ("a window must never stay invisible").
    /// </summary>
    private const float ShowDeadlineSeconds = 0.5f;

    /// <summary>Seconds between re-solve log lines. The FIRST placement always logs; after that
    /// only a materially different answer does, and never more often than this. A line that repeats
    /// on this cadence forever is the signature of another writer fighting us for the host pose.</summary>
    private const float PlacementLogIntervalSeconds = 5f;

    /// <summary>Graphic sink for the content sweep — reused, so the per-tick measurement allocates
    /// nothing.</summary>
    private static readonly List<Graphic> ContentGraphics = new(32);

    /// <summary>World-corner scratch for the content sweep and the ensemble census.</summary>
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    // ---- solved state -------------------------------------------------------------------------

    private static Rect _content;
    private static bool _contentMeasured;
    private static bool _contentRepeated;
    private static Rect _lastContent;
    private static bool _contentFallbackLogged;

    private static Vector3 _candidatePos;
    private static Quaternion _candidateRot = Quaternion.identity;
    private static float _candidateScale;
    private static bool _haveCandidate;
    private static float _candidateStillSince;

    private static Vector3 _appliedPos;
    private static Quaternion _appliedRot = Quaternion.identity;
    private static float _appliedScale;
    private static bool _placed;

    private static bool _wasActive;
    private static float _activeSince;
    private static bool _revealForcedLogged;
    private static bool _noEnsembleWarned;

    private static bool _placementLogged;
    private static float _placementLoggedAt = float.NegativeInfinity;
    private static Vector3 _loggedPos;

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
                          + "invention), and the game's real Reisen/Abbrechen buttons are moved onto a "
                          + "world host of their own, centred UNDER the floated windows, so they can be "
                          + "reached at all. Travel now commits through that button and through nothing "
                          + "else.");
    }

    /// <summary>
    /// Level-triggered, one call per tick from <see cref="MapRoomDriver"/>. Keeps the game's travel
    /// options on a mod-owned world host placed centrally under the floated-window ensemble, and
    /// hands them back otherwise. Idempotent: a steady state costs one corner sweep and no writes.
    /// </summary>
    /// <param name="questWindow">
    /// The floated quest window, or null. RETAINED FOR THE CALLER'S SIGNATURE AND FOR THE LOG ONLY.
    /// Up to ModBuild 191 the button was parented into this window and stood down without it; the
    /// 192 ruling ("zentral UNTER dem Rest") makes the placement a property of the whole floated
    /// SET, so binding it to one window's presence would be exactly the defect being fixed. It is
    /// still printed, because "which window was open when it was placed" is a useful reading.
    /// </param>
    internal static void Reconcile(UIWindow? questWindow)
    {
        if (_standDown)
            return;
        if (!EnsureReflection())
            return;

        AdventureMapUIManager? mgr = Manager();
        GameObject? options = mgr != null ? _travelOptions?.GetValue(mgr) as GameObject : null;

        if (!MapRoomDriver.Active || options == null)
        {
            ReleaseHost(!MapRoomDriver.Active ? "map room stood down"
                                              : "the game's travel options are gone");
            // Leaving the room clears a placement stand-down: whatever went wrong was about THIS
            // visit's objects, and re-entering rebuilds all of them.
            if (!MapRoomDriver.Active)
                _placeStandDown = false;
            return;
        }
        if (_placeStandDown)
            return;

        // A host whose target died is dropped BEFORE the placeability precondition below, so a
        // rebuild goes through the same "only when it can be placed" rule a first conversion does.
        if (_panel != null && (!_panel.IsAlive || _panel.HostGo == null || _panel.HostRect == null))
            ReleaseHost("the host or its target died");

        // TAKEN BEFORE IT IS EVER SHOWN — the strongest form of the anti-flash rule. The container is
        // converted as soon as there is an ensemble to place it against, WITHOUT waiting for the game
        // to show it, so it is already on a render-hidden host of ours by the time
        // EnableTravelOptions(true) runs and there is no frame in which it is active anywhere else.
        //
        // The one thing that must NOT happen is a host brought into existence with no pose: a host is
        // created unpositioned (identity, i.e. the world origin) and the conversion's reveal gate
        // will show it at its deadline whether or not anyone placed it. So the ensemble — the only
        // input the pose genuinely needs — is required first. The button's own height is NOT
        // required: until it can be measured the solve simply uses zero half-height, which places the
        // content's centre where its top edge belongs, i.e. half a button too high, and corrects
        // itself on the first measurement while still render-hidden.
        if (_panel == null && !TryEnsemble(out _))
        {
            if (options.activeInHierarchy)
                WarnNoEnsembleOnce();
            return;
        }

        if (!EnsureHost(options))
            return;
        ConvertedPanel panel = _panel!;
        // Named once: the null-forgiving read above satisfies the compiler, but the dead-host check
        // further up compared HostGo against null, so the analyzer now tracks it as nullable here.
        string hostName = panel.HostGo != null ? panel.HostGo.name : "<destroyed>";

        // OWNERSHIP, FIRST AND EVERY TICK. The game re-parents its own UI freely, and taking an
        // object back from wherever it has since put it is the write war this project has already
        // lost once. If it is no longer under our host, hand it over and stand down.
        if (options.transform.parent != panel.HostRect)
        {
            VRLog.Warn(Scope, $"MAP TRAVEL CONFIRM: '{options.name}' is no longer parented under our "
                              + $"host '{hostName}' — the game moved it. Releasing the "
                              + "conversion (which restores its recorded 2D home verbatim) and standing "
                              + "down for this map-room visit. CONSEQUENCE: the Reisen / 'Quest erneut "
                              + "spielen' button is unreachable in the room until the room is re-entered; "
                              + "the double-press shortcut stays switched off, so nothing can commit "
                              + "travel by accident in the meantime.");
            ReleaseHost("the game re-parented the travel options");
            _placeStandDown = true;
            return;
        }

        // The game owns activeSelf (EnableTravelOptions → travelOptions.SetActive). We only READ it.
        bool active = options.activeInHierarchy;
        if (active != _wasActive)
        {
            _wasActive = active;
            _activeSince = Time.unscaledTime;
            if (active)
            {
                // A fresh show may carry a different label ("Reisen" vs "Quest erneut spielen"), so
                // the content measurement starts over: the button stays hidden until two
                // measurements agree AND the pose has been written for them.
                _contentRepeated = false;
                _revealForcedLogged = false;
            }
        }

        MeasureContent(panel, options);
        SolveAndPlace(panel, options, mgr, questWindow);
        TickVisibility(panel, active);
        KeepClickable(panel);

        if (!_reported && _placed)
        {
            _reported = true;
            var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
            VRLog.Info(Scope, "MAP TRAVEL CONFIRM: the game's travel options now ride a world host of "
                              + $"their own ('{hostName}'), placed centrally UNDER the floated "
                              + "window ensemble rather than inside any one window (user ruling 192: "
                              + "\"er soll zentral UNTER dem Rest angezeigt werden\"). The button itself "
                              + $"is '{(btn != null ? btn.name : "<not found>")}' — the SAME ExtendedButton "
                              + "the flat game uses, so its label (Reisen / Quest erneut spielen), its "
                              + "interactable state and every guard behind OnTravelButtonClick are the "
                              + "game's own. It goes home the moment the room stands down.");
        }
    }

    // ---- the world host -----------------------------------------------------------------------

    /// <summary>
    /// Move the container onto a mod-owned world-space host, once. Returns false when the
    /// conversion refused, in which case the class stands down and the container is left exactly
    /// where the game had it.
    ///
    /// <para>THE PARAMETERS ARE THE POINT. <c>pokeable: true</c> registers the host with
    /// <see cref="UguiPokeSurfaces"/> so fingertip and laser press the REAL uGUI button (parked
    /// inside the quest window it inherited that window's registration; on its own host it needs
    /// its own). <c>fitContent: false</c> keeps the central content fit off this host: the fit is
    /// built to re-size a WINDOW around its content, and this container's rect is a 512x0 HUD bar —
    /// <see cref="MeasureContent"/> does the equivalent job explicitly, and the host rect is set
    /// from that measurement so the poke plane matches what is drawn. <c>useModLayer: true</c> is
    /// what arms the RENDER-HIDDEN birth and the reveal gate — the whole answer to the one-frame
    /// flash — and additionally keeps the game's mono UI camera from double-drawing it.</para>
    /// </summary>
    private static bool EnsureHost(GameObject options)
    {
        if (_panel != null && _panel.IsAlive && _panel.HostGo != null && _panel.HostRect != null)
            return true;
        if (_panel != null)
            ReleaseHost("the host or its target died");
        if (options.transform is not RectTransform rect)
        {
            if (!_convertWarned)
            {
                _convertWarned = true;
                _placeStandDown = true;
                VRLog.Warn(Scope, $"MAP TRAVEL CONFIRM: '{options.name}' is not a RectTransform, so it "
                                  + "cannot be moved onto a world canvas — the whole placement stands "
                                  + "down. CONSEQUENCE: the Reisen / 'Quest erneut spielen' button stays "
                                  + "unreachable in the 3D map room. The double-press shortcut stays "
                                  + "switched off, so travel still cannot commit by accident.");
            }
            return false;
        }

        _panel = CanvasConversion.Convert(rect, HostName, pokeable: true, fitContent: false,
            sortingOrder: ModalFallback.ModalHostSortingOrder, useModLayer: true);
        if (_panel == null)
        {
            if (!_convertWarned)
            {
                _convertWarned = true;
                _placeStandDown = true;
                VRLog.Warn(Scope, $"MAP TRAVEL CONFIRM: converting '{options.name}' to a world host "
                                  + "returned null (target destroyed?) — the whole placement stands down. "
                                  + "CONSEQUENCE: the Reisen / 'Quest erneut spielen' button stays "
                                  + "unreachable in the 3D map room. The double-press shortcut stays "
                                  + "switched off, so travel still cannot commit by accident.");
            }
            return false;
        }

        _contentMeasured = false;
        _contentRepeated = false;
        _haveCandidate = false;
        _placed = false;
        _placementLogged = false;
        _placementLoggedAt = float.NegativeInfinity;
        _wasActive = options.activeInHierarchy;
        _activeSince = Time.unscaledTime;
        return true;
    }

    /// <summary>Hand the container back and destroy the host. Safe to call at any time.</summary>
    private static void ReleaseHost(string why)
    {
        // The one-per-visit diagnostic latches are cleared even when there is no host to release,
        // so re-entering the map room re-reports a failure instead of failing silently the second
        // time. (_reported is deliberately NOT among them: it is a once-per-session description of
        // the mechanism, not a per-visit measurement.)
        _noEnsembleWarned = false;
        _convertWarned = false;
        _contentFallbackLogged = false;
        _revealForcedLogged = false;
        if (_panel == null)
            return;
        ConvertedPanel panel = _panel;
        _panel = null;
        _contentMeasured = false;
        _contentRepeated = false;
        _haveCandidate = false;
        _placed = false;
        _placementLogged = false;
        _placementLoggedAt = float.NegativeInfinity;
        _wasActive = false;
        CanvasConversion.Release(panel);
        VRLog.Info(Scope, $"MAP TRAVEL CONFIRM: travel options handed back to their own home ({why}) — "
                          + "parent, sibling index, anchors, pivot, anchoredPosition, sizeDelta, local "
                          + "scale/position/rotation all restored verbatim by CanvasConversion.Release "
                          + "from the record it captured at Convert, the poke registration dropped and "
                          + "the host destroyed. Nothing of ours is left on the game's object.");
    }

    /// <summary>Teardown — hand the container back before the room disappears under it.</summary>
    internal static void Reset() => ReleaseHost("map room teardown");

    // ---- what is drawn: the content measurement ------------------------------------------------

    /// <summary>
    /// Measure the container's VISIBLE content in HOST-local space, and centre it on the host.
    ///
    /// <para>Centring is what makes the placement arithmetic trivial downstream: once the content's
    /// centre coincides with the host's own origin, the host's world pose IS the content's world
    /// pose, and <see cref="SolveAndPlace"/> has one frame to reason in instead of two. The host
    /// rect is then sized to the measured content, so the laser/poke plane covers exactly what the
    /// player can see and nothing else.</para>
    /// </summary>
    private static void MeasureContent(ConvertedPanel panel, GameObject options)
    {
        // The game hides the whole container (EnableTravelOptions → travelOptions.SetActive). An
        // inactive subtree has no laid-out rects to measure, so the last measurement simply stands
        // until it comes back.
        if (!options.activeInHierarchy)
            return;
        if (options.transform is not RectTransform rect || panel.HostRect == null)
            return;

        bool measured = TryContentBounds(panel.HostRect, rect, out Rect content, out int counted,
                                         out int skipped);
        if (!measured)
        {
            // NOTHING VISIBLE TO MEASURE. Fall back to the container's own rect expressed on the
            // host — the button will be centred on the HUD BAR instead of on itself, i.e. it may
            // sit off to one side — and say so, because "it drifted sideways" then has a named
            // cause instead of being a mystery.
            content = new Rect(rect.anchoredPosition + rect.rect.position, rect.rect.size);
            if (!_contentFallbackLogged)
            {
                _contentFallbackLogged = true;
                VRLog.Warn(Scope, $"MAP TRAVEL CONFIRM: the travel options container '{options.name}' "
                                  + $"exposed no visible Graphic to measure ({skipped} candidate(s) "
                                  + "skipped as disabled, transparent or zero-sized), so the placement "
                                  + "falls back to the CONTAINER'S OWN RECT. CONSEQUENCE: the container "
                                  + "is a 512x0 flat-HUD bar with the button laid out somewhere inside "
                                  + "it, so the button may appear off-centre and vertically offset from "
                                  + "where this class reports it. Nothing throws.");
            }
        }

        Vector2 offset = content.center;
        if (offset.sqrMagnitude > ContentEpsilonPx * ContentEpsilonPx)
        {
            rect.anchoredPosition -= offset;
            content.center = Vector2.zero;
        }

        // The poke/laser plane is the HOST rect: size it to what is drawn. Writing it only on a
        // real change keeps the reveal gate's rect-stillness test satisfiable.
        Vector2 want = content.size;
        if (want.x > 0f && want.y > 0f
            && (Mathf.Abs(panel.HostRect.rect.width - want.x) > ContentEpsilonPx
                || Mathf.Abs(panel.HostRect.rect.height - want.y) > ContentEpsilonPx))
            panel.HostRect.sizeDelta = want;

        // TWO AGREEING MEASUREMENTS, NOT ONE. A uGUI layout is not final on the frame an object is
        // enabled, and the game re-labels this button between "Reisen" and "Quest erneut spielen",
        // which changes its width. Requiring the answer to repeat is what stops the reveal from
        // happening on a half-laid-out rect.
        bool repeats = _contentMeasured
                       && Mathf.Abs(content.width - _lastContent.width) <= ContentEpsilonPx
                       && Mathf.Abs(content.height - _lastContent.height) <= ContentEpsilonPx;
        _contentRepeated = _contentRepeated || repeats;
        _lastContent = content;
        _content = content;
        _contentMeasured = true;
    }

    /// <summary>
    /// The union of the container's ACTIVE, VISIBLE child Graphics' rects, expressed in the HOST's
    /// local space.
    ///
    /// <para>GRAPHICS AND NOT RECTTRANSFORMS, deliberately. A uGUI bar is full of layout groups and
    /// empty spacers whose rects are far larger than anything drawn in them; taking every
    /// RectTransform would measure the bar's skeleton, which is the same mistake as measuring the
    /// container. A Graphic is the only component that PAINTS, so the union of the enabled,
    /// non-transparent, non-degenerate ones is "what the player can see", which is the thing the
    /// user is judging the position of.</para>
    ///
    /// <para>Corner-based, not rect-based: a child may sit several transforms deep, so its rect is
    /// in ITS parent's space. <c>GetWorldCorners</c> + <c>InverseTransformPoint</c> lands every
    /// corner in the host's frame with no assumption about the chain between them.</para>
    /// </summary>
    private static bool TryContentBounds(RectTransform host, RectTransform container,
                                         out Rect content, out int counted, out int skipped)
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
                Vector3 p = host.InverseTransformPoint(CornerScratch[c]);
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

    // ---- where it goes: the ensemble solve ------------------------------------------------------

    /// <summary>The measured floated-window ensemble, in its own frame.</summary>
    private struct Ensemble
    {
        public int Count;
        public Vector3 Normal;   // ensemble facing: AWAY from the player (uGUI front is -Z)
        public Vector3 Right;    // up x normal
        public float LateralMin, LateralMax;
        public float DepthMin, DepthMax;
        public float BottomY, TopY;
        public float MeanScale;  // mean host lossyScale: world units per uGUI pixel
        public bool NormalAveraged;
    }

    /// <summary>
    /// Census of the floated modal windows and their extent in the (right, up, normal) frame their
    /// own poses define. Returns false when the room has none — see the class doc for what happens
    /// then (the last pose is held; the head is never consulted).
    /// </summary>
    private static bool TryEnsemble(out Ensemble e)
    {
        e = default;
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        Vector3 forwardSum = Vector3.zero;
        Vector3 lowestForward = Vector3.forward;
        float lowestY = float.PositiveInfinity;
        float scaleSum = 0f;
        int count = 0;
        for (int i = 0; i < panels.Count; i++)
        {
            ConvertedPanel p = panels[i];
            if (!IsEnsembleMember(p))
                continue;
            Transform t = p.HostGo.transform;
            Vector3 fwd = t.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-6f)
                forwardSum += fwd.normalized;
            p.HostRect.GetWorldCorners(CornerScratch);
            for (int c = 0; c < 4; c++)
            {
                if (CornerScratch[c].y < lowestY)
                {
                    lowestY = CornerScratch[c].y;
                    lowestForward = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                }
            }
            scaleSum += Mathf.Abs(t.lossyScale.x);
            count++;
        }
        if (count == 0)
            return false;

        // THE BASIS. On the arc the average of the hosts' +Z is the direction the arc is centred
        // on — and it contains no head term, only the poses the windows are standing at. If the set
        // is so spread that the average cancels (two windows facing opposite ways), fall back to the
        // facing of the host carrying the LOWEST corner: that is the window the button will sit
        // under, so matching it is the answer that still reads as "under the rest".
        bool averaged = forwardSum.sqrMagnitude > 1e-4f;
        Vector3 n = averaged ? forwardSum.normalized : lowestForward;
        n.y = 0f;
        if (n.sqrMagnitude < 1e-6f)
            n = Vector3.forward;
        n.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, n);
        if (right.sqrMagnitude < 1e-6f)
            right = Vector3.right;
        right.Normalize();

        float rMin = float.PositiveInfinity, rMax = float.NegativeInfinity;
        float dMin = float.PositiveInfinity, dMax = float.NegativeInfinity;
        float yMin = float.PositiveInfinity, yMax = float.NegativeInfinity;
        for (int i = 0; i < panels.Count; i++)
        {
            ConvertedPanel p = panels[i];
            if (!IsEnsembleMember(p))
                continue;
            p.HostRect.GetWorldCorners(CornerScratch);
            for (int c = 0; c < 4; c++)
            {
                Vector3 w = CornerScratch[c];
                float r = Vector3.Dot(w, right);
                float d = Vector3.Dot(w, n);
                if (r < rMin) rMin = r;
                if (r > rMax) rMax = r;
                if (d < dMin) dMin = d;
                if (d > dMax) dMax = d;
                if (w.y < yMin) yMin = w.y;
                if (w.y > yMax) yMax = w.y;
            }
        }

        e.Count = count;
        e.Normal = n;
        e.Right = right;
        e.LateralMin = rMin;
        e.LateralMax = rMax;
        e.DepthMin = dMin;
        e.DepthMax = dMax;
        e.BottomY = yMin;
        e.TopY = yMax;
        e.MeanScale = scaleSum / count;
        e.NormalAveraged = averaged;
        return true;
    }

    /// <summary>
    /// Does this converted panel count as one of "the rest" the button must sit under?
    ///
    /// <para>Four conditions, each for a stated reason. It must be a FLOATED MODAL host
    /// (<see cref="ModalHostPrefix"/>) — surfaces and this class's own host are not windows the
    /// player reads. It must be ALIVE and ACTIVE. It must be REVEALED: a host still behind the
    /// conversion's reveal gate is invisible and is very likely still moving, so counting it would
    /// place the button against a pose the player never saw. And it must be POKEABLE: floated
    /// windows are registered with <see cref="UguiPokeSurfaces"/>, hover cards are deliberately
    /// converted <c>pokeable: false</c> (ModBuild 187), and excluding hover cards is not optional —
    /// their pose belongs to the icon the pointer is on and they join and leave on every single
    /// mouseover, which would make the button dance across the room (ModBuild 184 made exactly this
    /// exclusion for the arc itself).</para>
    /// </summary>
    private static bool IsEnsembleMember(ConvertedPanel p)
    {
        if (p == null || !p.IsAlive || p.HostGo == null || p.HostRect == null || p.HostCanvas == null)
            return false;
        if (ReferenceEquals(p, _panel))
            return false;
        if (!p.HostGo.activeInHierarchy)
            return false;
        if (p.RenderHidden || p.OwnerRenderHidden || p.RevealPending)
            return false;
        if (!p.HostGo.name.StartsWith(ModalHostPrefix, System.StringComparison.Ordinal))
            return false;
        return UguiPokeSurfaces.Surfaces.Contains(p.HostCanvas);
    }

    /// <summary>
    /// Solve the host pose from the ensemble and apply it through the settle gate.
    ///
    /// <para>THE ARITHMETIC, stated once. Every counted host's four world corners are projected
    /// into the orthonormal frame (right, up, n), which reconstructs any world point exactly as
    /// <c>right·r + up·y + n·d</c>. The button's content — already centred on its host by
    /// <see cref="MeasureContent"/>, so host centre = content centre — is placed at the LATERAL
    /// MIDPOINT of the spread, at the DEPTH MIDPOINT, and with its TOP EDGE one gap below the
    /// lowest corner in the set. Its top edge sits <c>half the measured content height × the host
    /// scale</c> above its centre, so the height term is
    /// <c>bottom − gap − halfHeight</c>. No term is a guess and none of them is the head.</para>
    /// </summary>
    private static void SolveAndPlace(ConvertedPanel panel, GameObject options,
                                      AdventureMapUIManager? mgr, UIWindow? questWindow)
    {
        if (!TryEnsemble(out Ensemble e))
        {
            // NOTHING TO BE UNDER. Hold the last solved pose — the button stays where the player
            // last saw it, which is the only place they could look for it — and never fall back to
            // a head-derived spot (standing ruling: nothing may re-orient with head movement).
            if (!_placed && options.activeInHierarchy)
                WarnNoEnsembleOnce();
            return;
        }

        float scale = e.MeanScale;
        if (scale <= 1e-6f)
            scale = Mathf.Abs(panel.HostGo.transform.lossyScale.x);
        float ensembleHeight = Mathf.Max(0f, e.TopY - e.BottomY);
        float gap = GapEnsembleHeights * ensembleHeight;
        // Zero until the container has been shown once and measured: the host is placed with its
        // CENTRE where the content's TOP edge belongs, which is half a button too high and is
        // corrected on the first measurement — always while still render-hidden, because the
        // visibility gate below refuses to show anything before the measurement has repeated itself.
        float halfHeight = _contentMeasured ? 0.5f * Mathf.Abs(_content.height) * scale : 0f;

        float lateralMid = 0.5f * (e.LateralMin + e.LateralMax);
        float depthMid = 0.5f * (e.DepthMin + e.DepthMax);
        Vector3 pos = e.Right * lateralMid
                      + Vector3.up * (e.BottomY - gap - halfHeight)
                      + e.Normal * depthMid;
        // uGUI renders its front along -Z: pointing the host's +Z along the ensemble's own normal
        // faces the button exactly the way the windows above it face. Upright by construction —
        // there is no pitch term, and no head term.
        Quaternion rot = Quaternion.LookRotation(e.Normal, Vector3.up);

        float rigScale = RigScale();
        float eps = PoseEpsilonMeters * rigScale;
        bool sameAsCandidate = _haveCandidate
                               && (pos - _candidatePos).sqrMagnitude <= eps * eps
                               && Mathf.Abs(scale - _candidateScale) <= 1e-4f * Mathf.Max(1f, scale)
                               && Quaternion.Angle(rot, _candidateRot) <= 0.5f;
        if (!sameAsCandidate)
        {
            _haveCandidate = true;
            _candidatePos = pos;
            _candidateRot = rot;
            _candidateScale = scale;
            _candidateStillSince = Time.unscaledTime;
        }
        // THE SETTLE GATE IS AN ANTI-JITTER RULE, AND JITTER IS SOMETHING THE PLAYER CAN SEE. While
        // the host is still RENDER-HIDDEN — the first solve, and every re-show before the reveal —
        // a correction is invisible by construction, so it is applied at once: that is the same
        // "correct it while it cannot be seen" principle the conversion's own reveal gate is built
        // on, and it is what keeps the host from sitting unplaced long enough for that gate's
        // deadline to expose it.
        bool immediate = !_placed || panel.RenderHidden;
        if (!immediate && Time.unscaledTime - _candidateStillSince < EnsembleSettleSeconds)
            return;

        bool sameAsApplied = _placed
                             && (pos - _appliedPos).sqrMagnitude <= eps * eps
                             && Mathf.Abs(scale - _appliedScale) <= 1e-4f * Mathf.Max(1f, scale)
                             && Quaternion.Angle(rot, _appliedRot) <= 0.5f;
        if (sameAsApplied)
            return;

        // PlaceHost multiplies the passed scale by metres-per-pixel; invert that so the host lands
        // at exactly the ensemble's own mean world-units-per-pixel and the button's text is drawn
        // the same physical size as the text of the windows above it.
        float metersPerPixel = Mathf.Max(1e-6f, WorldUIConfig.CanvasScaleMm.Value * 0.001f);
        CanvasConversion.PlaceHost(panel, pos, rot, scale / metersPerPixel);
        _appliedPos = pos;
        _appliedRot = rot;
        _appliedScale = scale;
        _placed = true;

        bool changed = (pos - _loggedPos).sqrMagnitude > eps * eps;
        if (_placementLogged
            && (!changed || Time.unscaledTime - _placementLoggedAt < PlacementLogIntervalSeconds))
            return;
        _placementLogged = true;
        _placementLoggedAt = Time.unscaledTime;
        _loggedPos = pos;
        LogPlacement(panel, options, mgr, questWindow, e, gap, halfHeight, lateralMid, depthMid,
                     ensembleHeight, scale, rigScale, pos);
    }

    /// <summary>
    /// THE MEASUREMENT LINE. Every number the placement is derived from, in the frame it was read
    /// in, plus the gap in three units — ensemble heights (scale-free, the thing the eye judges),
    /// world units, and real metres at the current rig scale. If the next hardware report says the
    /// button is in the wrong place, this line says WHICH input was wrong: the census (wrong set),
    /// the extent (wrong frame), the content (wrong thing measured) or the applied pose (someone
    /// else writing it). Those are four different bugs and no other line separates them.
    /// </summary>
    private static void LogPlacement(ConvertedPanel panel, GameObject options,
                                     AdventureMapUIManager? mgr, UIWindow? questWindow, Ensemble e,
                                     float gap, float halfHeight, float lateralMid, float depthMid,
                                     float ensembleHeight, float scale, float rigScale, Vector3 pos)
    {
        var btn = mgr != null ? _travelButton?.GetValue(mgr) as Component : null;
        string btnWhere = "<not found>";
        if (btn != null)
            btnWhere = $"'{btn.name}' active={btn.gameObject.activeInHierarchy} at world "
                       + $"{btn.transform.position}";
        string members = EnsembleNames();
        string questName = questWindow != null ? questWindow.name : "<none floated>";
        // Built as separate locals rather than inlined: a nested interpolated string inside another
        // one is legal C# but it defeats the repo's own source scanners (scripts/patch-inventory.py
        // walks string literals with a single-quote-depth reader), and a tool that mis-parses this
        // file reports its Harmony patch as unregistered.
        string normalHow = e.NormalAveraged
            ? "average of the counted hosts' +Z (the arc's own centre direction)"
            : "the LOWEST host's +Z — the average cancelled, so the set is not arc-shaped";
        float gapMeters = gap / Mathf.Max(1e-6f, rigScale);
        float widthWorld = Mathf.Abs(_content.width) * scale;
        VRLog.Info(Scope,
            "MAP TRAVEL CONFIRM placement SOLVED — centred under the ENSEMBLE, not under one window.\n"
            + $"  census    : {e.Count} floated modal window(s) counted: {members}. Excluded: hover "
            + "cards (not pokeable — their pose belongs to the icon under the pointer), unrevealed "
            + "hosts (still moving), and this button's own host. The quest window is "
            + $"'{questName}' — printed for context ONLY; the placement does not depend on it, which "
            + "is the whole of user ruling 192.\n"
            + $"  basis     : normal {e.Normal} = {normalHow}; right {e.Right} = up x normal. NO HEAD "
            + "TERM — the windows' own poses are the frame, so nothing here re-orients with head "
            + "movement.\n"
            + $"  extent    : lateral [{e.LateralMin:F2} .. {e.LateralMax:F2}] world (mid "
            + $"{lateralMid:F2}), depth [{e.DepthMin:F2} .. {e.DepthMax:F2}] (mid {depthMid:F2}), "
            + $"height [{e.BottomY:F2} .. {e.TopY:F2}] (span {ensembleHeight:F2}). The button's centre "
            + "goes to (lateral mid, depth mid); its TOP edge goes one gap below the LOWEST corner.\n"
            + $"  content   : '{options.name}' visible union {_content} host-local px, centred on its "
            + $"own host, drawn at {scale:F4} world units per px = {widthWorld:F2} x "
            + $"{halfHeight * 2f:F2} world units. Button: {btnWhere}.\n"
            + $"  applied   : host pose {pos}, rotation {panel.HostGo.transform.rotation.eulerAngles}, "
            + $"lossyScale {panel.HostGo.transform.lossyScale.x:F4} (matched to the ensemble's MEAN, so "
            + "the label reads at the same physical size as the windows above it).\n"
            + $"  gap       : {GapEnsembleHeights:P1} of the ensemble's height = {gap:F3} world units = "
            + $"{gapMeters * 1000f:F1} mm real at rig scale {rigScale:F2} world units per metre. With "
            + "ONE window this is that window's own height, i.e. exactly the ModBuild 191 gap the user "
            + "did not object to.\n"
            + $"  cadence   : re-solved every tick, applied only after the answer holds still for "
            + $"{EnsembleSettleSeconds:F2} s. A window opening moves the button ONCE; a window being "
            + "dragged moves it once, on release; a steady room writes nothing.\n"
            + "  DISPROOF  : if the button is not where this line says, compare in order — (1) 'census' "
            + "lists a window the player cannot see, or omits one they can ⇒ the member test is wrong; "
            + "(2) 'extent' disagrees with where the windows visibly are ⇒ the corners were read from "
            + "the wrong rect; (3) 'content' width/height disagrees with the visible button ⇒ the "
            + "Graphic union caught something invisible (raise MinVisibleAlpha); (4) all three agree "
            + "and it is still wrong ⇒ the host pose is being overwritten by another writer, and THIS "
            + $"LINE WILL REPEAT on its {PlacementLogIntervalSeconds:F0} s cadence.");
    }

    /// <summary>
    /// ONE Warn for the "the game wants to show the travel options and there is no floated window
    /// to put them under" case, naming the consequence. Deliberately not a fallback: placing the
    /// button from the head would re-orient with head movement, which is a standing ruling against.
    /// </summary>
    private static void WarnNoEnsembleOnce()
    {
        if (_noEnsembleWarned)
            return;
        _noEnsembleWarned = true;
        VRLog.Warn(Scope, "MAP TRAVEL CONFIRM: the game has the travel options shown but NO floated "
                          + "modal window is revealed in the room, so there is nothing to centre them "
                          + "under and no pose can be solved. CONSEQUENCE: the Reisen / 'Quest erneut "
                          + "spielen' button stays unreachable until a window (normally the quest card) "
                          + "floats — it is deliberately NOT placed in front of the head, because a "
                          + "head-derived pose would re-orient with head movement (standing ruling). The "
                          + "double-press shortcut stays switched off, so nothing can commit travel by "
                          + "accident meanwhile. The container is left exactly where the game had it.");
    }

    /// <summary>Host names of the counted ensemble, for the placement line. Allocates only on the
    /// throttled log path.</summary>
    private static string EnsembleNames()
    {
        var sb = new System.Text.StringBuilder(128);
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            if (!IsEnsembleMember(panels[i]))
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(panels[i].HostGo.name).Append('\'');
        }
        return sb.Length > 0 ? sb.ToString() : "<none>";
    }

    // ---- when it is drawn: the anti-flash gate --------------------------------------------------

    /// <summary>
    /// THE ONE-FRAME FLASH FIX. The host renders only while the game has the container shown AND a
    /// repeated content measurement has been placed at a solved pose — so there is no frame in
    /// which the button is visible at a pose that is not its final one.
    ///
    /// <para>NOT A WRITE WAR. The flag written here belongs to the mod's own host and nothing in
    /// the game can see it; the game's own <c>travelOptions.activeSelf</c> is never written, only
    /// read. The write is level-triggered against <see cref="ConvertedPanel.RenderHidden"/>, so a
    /// steady state writes nothing. And while the conversion's own reveal gate is still pending
    /// this method keeps its hands off entirely — the gate owns visibility until it opens, and two
    /// owners writing the same flag on alternate frames is exactly the alternation bug.</para>
    ///
    /// <para>BOUNDED. If the measurement never repeats itself, the button is shown anyway after
    /// <see cref="ShowDeadlineSeconds"/> with a Warn: a confirm button that never appears is worse
    /// than one that appears a frame early.</para>
    /// </summary>
    private static void TickVisibility(ConvertedPanel panel, bool active)
    {
        if (panel.RevealPending)
            return;
        bool ready = _placed && _contentRepeated;
        if (!ready && active && Time.unscaledTime - _activeSince >= ShowDeadlineSeconds)
        {
            ready = _placed;
            if (!_revealForcedLogged && _placed)
            {
                _revealForcedLogged = true;
                VRLog.Warn(Scope, "MAP TRAVEL CONFIRM: the travel options are being shown before the "
                                  + "content measurement repeated itself (deadline "
                                  + $"{ShowDeadlineSeconds * 1000f:F0} ms since the game enabled them). "
                                  + "CONSEQUENCE: the button may be centred a few pixels off for a frame "
                                  + "or two while the uGUI layout finishes, and will then settle. A "
                                  + "button that never appears would be the worse failure, so visibility "
                                  + "wins. If this line repeats every time, the container's layout is "
                                  + "being re-driven every frame and the measurement can never agree "
                                  + "with itself.");
            }
        }
        bool wantVisible = active && ready;
        if (panel.RenderHidden == wantVisible)
            CanvasConversion.SetPanelRenderVisible(panel, wantVisible);
    }

    /// <summary>
    /// Keep the confirm button clickable while the game's UI lock is asserted. The lock legitimately
    /// disables every converted host's raycaster (the CanvasConversion lock mirror, change-gated on
    /// its own dirty flag), but the travel confirmation is the one surface that must accept input
    /// while it is up — the same exemption <c>DialogSurface</c> and the floated modals apply. Parked
    /// inside the quest window (190/191) this came for free from that window's host; on a host of
    /// its own it has to be re-asserted. Change-gated, and the mirror only writes on a lock
    /// transition, so the two cannot fight.
    /// </summary>
    private static void KeepClickable(ConvertedPanel panel)
    {
        if (panel.HostRaycaster != null && !panel.HostRaycaster.enabled)
            panel.HostRaycaster.enabled = true;
    }

    /// <summary>World units per real metre at the current rig scale, or 1 when there is no rig.
    /// Named rather than assumed: the user judges distances in real millimetres and the transforms
    /// are written in world units, and mixing the two silently is a bug class this project has.</summary>
    private static float RigScale() =>
        Rig.RigTarget.Current != null
            ? Mathf.Max(Rig.RigTarget.Current.lossyScale.x, 0.0001f)
            : 1f;

    // ---- the shortcut gate (unchanged since ModBuild 190) --------------------------------------

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
                                  + "commits through the Reisen button under the floated windows.");
            }
            return false;
        }
    }
}
