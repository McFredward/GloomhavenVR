using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE PRE-SCENARIO CONFIRM BELONGS ON THE LOCAL CHARACTER SCREEN, AND THE GAME'S OWN SOURCE IS
/// WHAT DECIDES THAT.
///
/// <para><b>USER REPORT (ModBuild 234 hardware, item 4, verbatim):</b> <i>"DEADLOCK: Nachdem ich für
/// jeden Character die Quest ausgewählt habe, muss irgendwo der button erscheinen damit es weiter
/// gehen kann. Der ist nie erschienen, man konnte nicht weiter vorranschreiten."</i> And, in the
/// same breath, the question that decides the design: <i>"Frage: Im Flat-Spiel, sieht das nur der
/// Host oder wird das jedem im Multiplayer präsentiert? Abhängig von der Antwort möchte ich
/// entweder, dass der Button auf der lokalen Character-UI oder dem MP-Fenster mit der Story
/// angezeigt wird."</i></para>
///
/// <para><b>THE ANSWER, FROM SOURCE, AND IT IS "EVERY PLAYER".</b>
/// <c>UILoadoutManager.SetActiveConfirmationButton</c> (decompiled UILoadoutManager.cs:98-108) is
/// <c>value &amp;= CanShowConfirmationButton(); if (FFSNetwork.IsOnline)
/// Singleton&lt;UIReadyToggle&gt;.Instance.ToggleVisibility(value); else
/// SetActiveSinglePlayerLongConfirmButton(value);</c> — one branch per network mode and NO host
/// test anywhere in it. The online branch's toggle is set up by <c>MPConfirmEnterScenario</c>
/// (:474-517), which runs from <c>MultiplayerStartup</c> on EVERY client, initialises the ready
/// toggle with <c>show: true</c> and the label <c>"GUI_LOADOUT_ENTER_SCENARIO"</c>, and wires the
/// ALL-PLAYERS-READY callback to <c>delegate { if (FFSNetwork.IsHost) ConfirmEnterScenario(); }</c>.
/// So every player is shown the control, every player confirms FOR HIMSELF, and the host merely
/// commits once the last one has. That makes pressing it a LOCAL act, and by the user's own rule the
/// control belongs on the LOCAL Character-UI — not on the shared story window.</para>
///
/// <para><b>SO THIS CLASS PARKS IT INTO THE FLOATED CHARACTER SCREEN</b> ('New Party display', ID
/// <c>PartyPanel</c> — the map room's permanent, X-less window), exactly the way
/// <c>MapRoom/MapTravelConfirm</c> parks the quest confirm into the quest card: record the home
/// before the first move, restore it verbatim, re-measure the anchor on a cadence, never write game
/// state, and — for the online control, which is a <c>UIWindow</c> and therefore something the float
/// gate can see — set the park claim so <c>FloatRefusalTable</c> knows somebody else is drawing it.
/// That last point is not tidiness: a refusal with no parker behind it is a deadlock generator, and
/// the round this file was written in already contained one.</para>
///
/// <para>=====================================================================================
/// WHY THE DEADLOCK HAPPENED, BECAUSE THIS CLASS IS ALSO THE SECOND HALF OF ITS FIX
/// =====================================================================================</para>
///
/// <para>The single-player continue button is <c>UILoadoutManager.confirmationButton</c>, a private
/// serialized <c>Button</c> whose GameObject <c>SetActiveSinglePlayerLongConfirmButton</c> switches
/// on (:88-95). It is a CHILD OF THE LOADOUT WINDOW. In the ModBuild 234 log
/// <c>FloatRefusalTable</c>'s <c>UILoadoutManager</c> row withdrew that window's float at
/// Player.log:11483-11484 and the claim behind the row never lapsed, so from that instant the only
/// object carrying the continue button was drawn on 'Campaign Canvas', which the 3D map room does not
/// render. No loadout screen, no button, no way forward — and the game had said the button should be
/// showable (<c>[GUI] EnableLoadoutInteraction</c> at :11535).</para>
///
/// <para><b>PARKING THE CONTROL MAKES THAT CLASS OF FAULT SURVIVABLE RATHER THAN FATAL.</b> Once the
/// confirm is drawn inside the Character-UI it does not matter whether the loadout window floats:
/// the control is reachable either way. <c>StoryComposite</c>'s claim was fixed independently (it can
/// no longer outlive the intro), and this is the belt to that pair of braces — two independent
/// mechanisms, because the fault they prevent is the one the player cannot work around.</para>
///
/// <para><b>THE FALSIFIER IS THE POINT OF THE FILE.</b> <c>LOADOUT CONFIRM REACHABLE: CONFIRMED</c>
/// is printed only when the GAME says a continue control should be showable
/// (<c>UILoadoutManager.CanShowConfirmationButton()</c>, plus the game's own switch on the control)
/// AND that control is measured drawing inside a live floated panel this tick. The failing form is a
/// WARNING that NAMES THE SUPPRESSOR by asking <c>FloatRefusalTable.Describe</c> — so a hardware log
/// says which claim is hiding the button instead of leaving the next round to infer it from an
/// absence, which is what cost this one.</para>
///
/// <para><b>MULTIPLAYER: nothing here goes on the wire.</b> This class re-parents a local uGUI
/// subtree under a local host on THIS client. It never presses the control, never writes
/// <c>Show</c>/<c>Hide</c>/<c>Escape</c>/<c>SetActive</c> on a game window and never touches the ready
/// toggle's own state — <c>ToggleVisibility</c>, <c>SetInteractable</c> and the ready-up itself stay
/// the game's. Two players may legitimately disagree about every verdict in it.</para>
/// </summary>
internal static class LoadoutConfirmPark
{
    private const string Scope = "WorldUI";

    /// <summary>Gap between the Character-UI's painted BOTTOM edge and the confirm's painted TOP
    /// edge, in the host window's own authored uGUI px. Deliberately a constant and not a config
    /// dial: the user asked for the button to be on the Character-UI, not for a spacing control, and
    /// every new dial is a surface somebody has to tune. It is the same 24 px
    /// <c>StoryComposite.ImageGapPx</c> uses between the quest picture and the dialog, for the same
    /// reason — on a committed panel that is roughly 17-25 mm, visibly one gap and never a
    /// separation.</summary>
    private const float ConfirmGapPx = 24f;

    /// <summary>Below this the re-place is skipped, in authored uGUI px — <c>MapTravelConfirm</c>'s
    /// <c>OffsetEpsilon</c>, for its reason: a settled layout is not bit-identical frame to frame and
    /// writing it back every frame would keep the host's content fit re-measuring forever.</summary>
    private const float OffsetEpsilonPx = 0.5f;

    /// <summary>How often the measured zero is re-solved, seconds. <c>MapTravelConfirm</c>'s
    /// <c>AnchorRefreshIntervalSeconds</c> and the same argument: the sweep walks every Graphic under
    /// the character screen, the thing it measures changes when the roster or a sub-panel changes,
    /// and running it per frame would buy nothing in a room whose Update budget is already the one
    /// the perf line complains about.</summary>
    private const float AnchorRefreshIntervalSeconds = 0.2f;

    /// <summary>How long the park claim may stand while the online control is on screen but not yet
    /// parked. <c>MapQuestReadyUp</c>'s <c>ClaimGraceSeconds</c>, for its reason: a confirm nobody
    /// can reach is worse than an ugly one, so the refusal is released rather than held.</summary>
    private const float ClaimGraceSeconds = 1.5f;

    // ---- park state ----------------------------------------------------------------------------

    private static GameObject? _parked;
    private static bool _parkedIsReadyToggle;
    private static UIWindow? _host;
    private static GameObject? _homeOwner;
    private static bool _homeRecorded;
    private static Transform? _home;
    private static int _homeIndex;
    private static Vector2 _homeAnchorMin;
    private static Vector2 _homeAnchorMax;
    private static Vector2 _homePivot;
    private static Vector2 _homeAnchoredPos;
    private static Quaternion _homeRotation = Quaternion.identity;
    private static Vector3 _homeScale = Vector3.one;
    private static LayoutElement? _addedIgnore;
    private static bool _standDown;
    private static bool _standDownWarned;
    private static bool _parkLogged;

    // ---- the measured zero ---------------------------------------------------------------------

    private static bool _anchorValid;
    private static Vector2 _anchorPivot;
    private static float _anchorNextRefreshAt;
    private static string _anchorWhy = "never measured";
    private static Rect _anchorContent;
    private static int _anchorContentCount;
    private static Rect _anchorControl;
    private static int _anchorControlCount;

    // ---- the claim (online only) ---------------------------------------------------------------

    private static float _visibleUnparkedSince = -1f;
    private static string _claimWhy = "nothing is claimed";
    private static bool _claimStanding;

    // ---- the falsifier's change gate ------------------------------------------------------------

    private static string _reachVerdict = string.Empty;
    private static int _reachReports;

    /// <summary>Cap on <c>LOADOUT CONFIRM</c> lines per loadout screen. The verdict is change-gated
    /// as well, so this only bites if the state genuinely oscillates — in which case six lines are
    /// already the diagnosis and a seventh is noise. Same number and same argument as
    /// <c>StoryComposite.MaxOneWindowReports</c>.</summary>
    private const int MaxReachReports = 6;

    private static UIWindow? _reachHost;

    // ---- the public reads ----------------------------------------------------------------------

    /// <summary>
    /// HAS THE GAME DECIDED THAT A CONTINUE CONTROL SHOULD BE SHOWABLE RIGHT NOW? A PURE read, safe
    /// from anywhere and as often as needed — <c>StoryComposite</c> asks it once per tick as one of
    /// the terminators of its loadout claim.
    ///
    /// <para>BOTH TERMS ARE THE GAME'S OWN AND NEITHER IS WRITTEN BY THIS MOD, which is the whole
    /// requirement: the fault this answers was a claim whose every "is it on screen" term was a value
    /// the mod itself set.</para>
    /// <list type="number">
    /// <item><c>UILoadoutManager.CanShowConfirmationButton()</c> — the game's own predicate, called
    /// rather than re-derived. It is false while <c>NewPartyDisplayUI.ActiveDisplay</c> is an open
    /// sub-panel other than <c>NONE</c> or <c>BATTLE_GOALS</c> (:111-119).</item>
    /// <item>The control's own switch: offline <c>confirmationButton.gameObject.activeSelf</c>, which
    /// is exactly what <c>SetActiveSinglePlayerLongConfirmButton</c> writes (:94); online
    /// <c>UIReadyToggle.IsVisible</c>, which is literally <c>window.IsOpen</c>
    /// (UIReadyToggle.cs:138) and therefore the game's <c>ToggleVisibility</c> decision. Parking
    /// changes NEITHER of those two fields, so the answer cannot be contaminated by the park.</item>
    /// </list>
    /// </summary>
    internal static bool GameWantsConfirmShown()
    {
        try
        {
            UILoadoutManager? lm = Manager();
            if (lm == null || !lm.IsOpen || !lm.CanShowConfirmationButton())
                return false;
            GameObject? control = ResolveControl(lm, out bool readyToggle);
            if (control == null)
                return false;
            return readyToggle ? ReadyToggleVisible() : control.activeSelf;
        }
        catch (System.Exception)
        {
            // A read is never worth a throw reaching the modal tick, and the safe direction here is
            // "the game is not asking for a button": it can only make a suppressor lapse LATER, and
            // the deadlock warning below still fires off the same measurement.
            return false;
        }
    }

    /// <summary>Where the confirm is being drawn right now, for another subsystem's log line. Never
    /// null.</summary>
    internal static string Where =>
        _parked != null && _host != null
            ? $"'{_parked.name}' is parked inside the floated Character-UI '{_host.name}'"
            : "the confirm is not parked anywhere by this class";

    // ---- the per-tick step ----------------------------------------------------------------------

    /// <summary>
    /// Called once per <c>ModalFallback.Tick</c>, from <c>StoryComposite.Tick</c> — i.e. from the
    /// first line of <c>TickWindowLiveness</c>, BEFORE the release loop. That call site is the same
    /// one and load-bearing for the same reason: the release loop destroys the host GameObject this
    /// class parks into, and a subtree still parked under a destroyed host cannot be handed back.
    ///
    /// <para>EVERYTHING HERE IS LEVEL-TRIGGERED. There is no latch, no session verdict and no
    /// suppression list: every tick re-asks which object IS the confirm, whether the Character-UI is
    /// a live floated panel, and whether the object is still ours. A steady state costs two reference
    /// compares and — four times a second — two Graphic sweeps.</para>
    /// </summary>
    internal static void Tick()
    {
        try
        {
            TickCore();
        }
        catch (System.Exception e)
        {
            // ONE guard around the whole step, and the fail direction is to hand the control back:
            // a parker that throws must not leave the game's own button inside a window that is
            // about to be released. [[stack-traces-restored]] — the type is named so the log is
            // attributable.
            VRLog.Warn(Scope, $"LOADOUT CONFIRM: the parker threw ({e.GetType().Name}) — handing the "
                              + "control back to its own home and standing down for this loadout. "
                              + "CONSEQUENCE: the continue button is wherever the game draws it, "
                              + "which offline is the loadout screen — see LOADOUT CONFIRM "
                              + "REACHABLE for whether that is reachable in this room.");
            Unpark("the parker threw");
            DropClaim("the parker threw");
            _standDown = true;
        }
    }

    private static void TickCore()
    {
        UILoadoutManager? lm = Manager();
        UIWindow? loadout = lm != null ? lm.GetComponent<UIWindow>() : null;

        if (!MapRoomDriver.Active || lm == null || !lm.IsOpen)
        {
            Unpark(!MapRoomDriver.Active ? "the 3D map room stood down"
                                         : "the loadout screen is not open");
            DropClaim(!MapRoomDriver.Active ? "the 3D map room stood down"
                                            : "the loadout screen is not open");
            // Leaving the loadout clears a stand-down: whatever went wrong was about THIS quest's
            // objects, and the next EnterLoadout rebuilds all of them.
            _standDown = false;
            _standDownWarned = false;
            ResetReach();
            return;
        }
        if (_standDown)
        {
            ReportReach(lm, loadout);
            return;
        }

        GameObject? control = ResolveControl(lm, out bool readyToggle);

        // ModBuild 236 — IF THE CONTROL IS ALREADY DRAWN IN A WINDOW THE PLAYER CAN SEE, THIS CLASS
        // DOES NOTHING AT ALL.
        //
        // WHY THE CONDITION IS "the loadout screen is floated" AND NOT "the control is inside a
        // floated window". The second question is the one ConfirmReachable asks, and asking it HERE
        // would flap: once this class has parked the control into the Character-UI, the control IS
        // inside a floated window — its own park — so the answer would flip every time it acted on
        // it. The loadout window's float is a fact about a window this class never touches, so the
        // level is stable whichever way it goes.
        //
        // AND IT IS THE OFFLINE CONTROL ONLY, BY CONSTRUCTION. UILoadoutManager.confirmationButton is
        // a CHILD of the loadout window (:88-95), so a floated loadout screen draws it. The ONLINE
        // control is UIReadyToggle — a Singleton that is its OWN window root under 'Campaign Canvas'
        // and is refused by ROW 2 of the refusal table as a bare control, so it is never inside the
        // loadout screen and the park below is still the only thing that draws it. The user's own
        // ruling for that case has not changed: pressing it is a LOCAL act, so it belongs on the
        // LOCAL Character-UI.
        //
        // THIS IS THE OTHER HALF OF ModBuild 236's INVERSION. StoryComposite no longer withholds the
        // loadout screen's float — it withholds the STORY window's — so through the whole
        // pre-scenario interval the loadout screen is on screen with its own confirm button on it,
        // and that is exactly the window the user asked for the button to appear on: "Es soll immer
        // noch das exakt gleiche Fenster sein." Moving it to the Character-UI would now be this mod
        // taking the button OFF the window he named.
        if (!readyToggle && loadout != null && FloatedByMod(loadout))
        {
            Unpark("the loadout screen is floating with the confirm button on it, so there is "
                   + "nothing for this class to fix");
            // TickClaim drops the claim itself on this path (the offline confirm is a plain Button,
            // not a UIWindow, so the float gate never sees it and there is nothing to claim).
            TickClaim(control, readyToggle, parked: false);
            ReportReach(lm, loadout);
            return;
        }

        UIWindow? host = CharacterWindow();
        bool hostFloated = host != null && FloatedByMod(host);

        if (control == null || host == null || !hostFloated)
        {
            Unpark(control == null ? "the game has no confirm control for this loadout"
                   : host == null ? "the Character-UI window does not exist"
                                  : "the Character-UI is not floated, so there is nothing to be part of");
            TickClaim(control, readyToggle, parked: false);
            ReportReach(lm, loadout);
            return;
        }

        // THE CONTROL IS PART OF THE PARKING IDENTITY, NOT ONLY THE HOST (MapTravelConfirm's ModBuild
        // 226 rule): going online swaps the single-player button for the ready toggle and going
        // offline swaps it back, and either way the object we are HOLDING must be handed home before
        // the other one is taken — otherwise the first stays parented into a window it is no longer
        // the confirm for.
        if (!ReferenceEquals(_host, host) || !ReferenceEquals(_parked, control))
        {
            Unpark(!ReferenceEquals(_host, host)
                ? "a different Character-UI window took over"
                : "the game switched which object IS the confirm (online ⇄ offline)");
            if (!Park(host, control, readyToggle))
            {
                TickClaim(control, readyToggle, parked: false);
                ReportReach(lm, loadout);
                return;
            }
        }

        // OWNERSHIP, EVERY TICK. The game re-parents its own UI freely and re-taking an object from
        // wherever it has since put it is the write war this project has already lost once.
        if (control.transform.parent != host.transform)
        {
            VRLog.Warn(Scope, $"LOADOUT CONFIRM: '{control.name}' is no longer parented under the "
                              + $"floated Character-UI '{host.name}' — the game moved it. Handing it "
                              + "back (home transform restored verbatim) and standing the parking down "
                              + "for this loadout. CONSEQUENCE: the continue button is wherever the "
                              + "game draws it; offline that is the loadout screen, so if THAT is also "
                              + "being withheld the player has no way forward — the LOADOUT CONFIRM "
                              + "REACHABLE line beside this one says which.");
            Unpark("the game re-parented the confirm");
            TickClaim(control, readyToggle, parked: false);
            _standDown = true;
            ReportReach(lm, loadout);
            return;
        }

        ApplyPose(host, control);
        TickClaim(control, readyToggle, parked: true);
        ReportReach(lm, loadout);
    }

    // ---- which object IS the confirm -------------------------------------------------------------

    private static UILoadoutManager? Manager()
    {
        if (!Singleton<UILoadoutManager>.IsInitialized)
            return null;
        UILoadoutManager lm = Singleton<UILoadoutManager>.Instance;
        return lm != null ? lm : null;
    }

    /// <summary>
    /// WHICH OBJECT IS THE PRE-SCENARIO CONFIRM RIGHT NOW, read off the one branch the game itself
    /// takes: <c>UILoadoutManager.SetActiveConfirmationButton</c> switches on <c>FFSNetwork.IsOnline</c>
    /// (:100-108) and so does this.
    ///
    /// <para>ONLINE it is the multiplayer ready toggle — and the toggle is a SINGLETON reused for
    /// quests, city events, rewards, retirement and town records, so this refuses to touch it unless
    /// its own <c>readyUpToggleState</c> is NOT <c>Quests</c>. That is exact rather than merely
    /// likely: <c>MPConfirmEnterScenario</c> calls <c>Initialize</c> without the
    /// <c>readyUpToggleState</c> argument, so the loadout ready-up leaves it at its default
    /// <c>NotSet</c> (UIReadyToggle.cs:452,467), while <c>MapRoom/MapQuestReadyUp</c> claims the
    /// toggle only while it reads <c>Quests</c>. The two parkers are therefore mutually exclusive BY
    /// THE GAME'S OWN FIELD and never both write <c>ReadyToggleParkClaim</c> —
    /// [[a-remedy-knows-one-writer]].</para>
    ///
    /// <para>OFFLINE it is <c>UILoadoutManager.confirmationButton</c>, the object
    /// <c>SetActiveSinglePlayerLongConfirmButton</c> calls <c>SetActive</c> on in BOTH of its own
    /// branches (:88-95), so it is the right object with or without a gamepad. (The gamepad-only
    /// <c>LongConfirmHandler</c> button is a second control this class does not move; VR runs with
    /// <c>InputManager.GamePadInUse</c> false and reaches the button by laser or poke.)</para>
    /// </summary>
    private static GameObject? ResolveControl(UILoadoutManager lm, out bool readyToggle)
    {
        readyToggle = false;
        if (FFSNetwork.IsOnline)
        {
            if (!Singleton<UIReadyToggle>.IsInitialized)
                return null;
            UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
            if (toggle == null || toggle.readyUpToggleState == EReadyUpToggleStates.Quests)
                return null;
            readyToggle = true;
            return toggle.gameObject;
        }
        Button? button = lm.confirmationButton;
        return button != null ? button.gameObject : null;
    }

    private static bool ReadyToggleVisible()
    {
        if (!Singleton<UIReadyToggle>.IsInitialized)
            return false;
        UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
        return toggle != null && toggle.IsVisible;
    }

    /// <summary>
    /// The map room's CHARACTER SCREEN — 'New Party display', ID <c>PartyPanel</c>. Asked as an IS-A
    /// question on the display's OWN GameObject: <c>NewPartyDisplayUI.Awake</c> caches
    /// <c>window = GetComponent&lt;UIWindow&gt;()</c> (NewPartyDisplayUI.cs:277), so the component and
    /// the window are one GameObject by construction and a <c>GetComponent</c> here cannot reach any
    /// other window's parts ([[containment-is-not-identity]]).
    /// </summary>
    private static UIWindow? CharacterWindow()
    {
        NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
        if (display == null)
            return null;
        UIWindow? window = display.GetComponent<UIWindow>();
        return window != null ? window : null;
    }

    /// <summary>Is this window one the mod is floating right now? Asked through the only float-set
    /// question <c>ModalFallback</c> exposes — a window is floated exactly when excluding it from the
    /// count changes the count.</summary>
    private static bool FloatedByMod(UIWindow? window) =>
        window != null
        && ModalFallback.CountFloatsOtherThan(window) < ModalFallback.CountFloatsOtherThan(null);

    // ---- parking ---------------------------------------------------------------------------------

    /// <summary>
    /// Move the control into the Character-UI, once. The <c>MapTravelConfirm</c> sequence verbatim:
    /// record the home for THIS object, add the layout opt-out BEFORE the reparent so the
    /// destination's layout never rebuilds with this rect in its children, then parent, anchor, pivot,
    /// rotation, scale and pose.
    /// </summary>
    private static bool Park(UIWindow host, GameObject control, bool readyToggle)
    {
        if (host.transform is not RectTransform win || control.transform is not RectTransform rect)
        {
            if (!_standDownWarned)
            {
                _standDownWarned = true;
                VRLog.Warn(Scope, "LOADOUT CONFIRM: cannot park the continue control — "
                                  + $"'{control.name}' or the Character-UI '{host.name}' is not a "
                                  + "RectTransform, so the anchor arithmetic this class is built on "
                                  + "does not apply. The parking stands down and nothing is moved. "
                                  + "CONSEQUENCE: the control stays where the game draws it.");
            }
            _standDown = true;
            return false;
        }

        // THE RECORD IS PER OBJECT (MapTravelConfirm's ModBuild 226 rule): with two possible controls
        // a single latch would apply the FIRST object's home to the SECOND on unpark, which is a
        // silent corruption of the game's own HUD layout rather than a visible bug.
        if (!_homeRecorded || !ReferenceEquals(_homeOwner, control))
        {
            _homeRecorded = true;
            _homeOwner = control;
            _home = rect.parent;
            _homeIndex = rect.GetSiblingIndex();
            _homeAnchorMin = rect.anchorMin;
            _homeAnchorMax = rect.anchorMax;
            _homePivot = rect.pivot;
            _homeAnchoredPos = rect.anchoredPosition;
            _homeRotation = rect.localRotation;
            _homeScale = rect.localScale;
        }

        // ignoreLayout BEFORE the move — MapTravelConfirm's discipline, for its reason: this takes the
        // anchors and the anchoredPosition out of a parent layout group's hands instead of racing it
        // for them.
        LayoutElement? le = control.GetComponent<LayoutElement>();
        if (le == null)
        {
            le = control.AddComponent<LayoutElement>();
            _addedIgnore = le;
        }
        le.ignoreLayout = true;

        _parked = control;
        _parkedIsReadyToggle = readyToggle;
        _host = host;
        _parkLogged = false;
        ResetAnchor("a new parking — the Character-UI has not been measured with this control in it yet");

        rect.SetParent(win, worldPositionStays: false);
        rect.SetAsLastSibling();
        rect.anchorMin = Vector2.up;
        rect.anchorMax = Vector2.up;
        rect.pivot = new Vector2(0.5f, 1f);   // its TOP edge is what we place
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        ApplyPose(host, control);
        return true;
    }

    /// <summary>
    /// Put the control back. The TRANSFORM is restored only while the object is still parented under
    /// our host, because re-taking it from wherever the game has since put it is a write war
    /// ([[dont-win-a-write-war]]).
    /// </summary>
    private static void Unpark(string why)
    {
        if (_parked == null)
        {
            _host = null;
            return;
        }
        GameObject control = _parked;
        UIWindow? host = _host;
        bool wasReadyToggle = _parkedIsReadyToggle;
        _parked = null;
        _host = null;
        _parkedIsReadyToggle = false;
        _parkLogged = false;
        ResetAnchor("the control was unparked");

        try
        {
            if (_addedIgnore != null)
            {
                Object.Destroy(_addedIgnore);
                _addedIgnore = null;
            }
            else
            {
                LayoutElement? le = control != null ? control.GetComponent<LayoutElement>() : null;
                if (le != null)
                    le.ignoreLayout = false;
            }
            if (control == null || _home == null)
                return;
            Transform t = control.transform;
            if (host == null || host.transform == null || t.parent == null
                || !t.parent.IsChildOf(host.transform))
                return;
            t.SetParent(_home, worldPositionStays: false);
            t.SetSiblingIndex(Mathf.Clamp(_homeIndex, 0, Mathf.Max(0, _home.childCount - 1)));
            if (t is RectTransform rect)
            {
                rect.anchorMin = _homeAnchorMin;
                rect.anchorMax = _homeAnchorMax;
                rect.pivot = _homePivot;
                rect.anchoredPosition = _homeAnchoredPos;
                rect.localRotation = _homeRotation;
                rect.localScale = _homeScale;
            }
            VRLog.Info(Scope, $"LOADOUT CONFIRM UNPARKED — {why}. "
                              + $"{(wasReadyToggle ? "The multiplayer ready toggle" : "The single-player continue button")} "
                              + $"'{control.name}' is back under '{_home.name}' at sibling {_homeIndex} "
                              + "with its parent, anchors, pivot, anchoredPosition, local rotation and "
                              + "local scale restored verbatim from the record taken before the first "
                              + "move, and the layout opt-out (LayoutElement.ignoreLayout) released or "
                              + "destroyed. Nothing of ours is left on the game's object. NOTHING WAS "
                              + "WRITTEN TO THE GAME at any point: no Show, no Hide, no Escape, no "
                              + "SetActive and no CanvasGroup — the control's own visibility is the "
                              + "game's (SetActiveSinglePlayerLongConfirmButton offline, "
                              + "UIReadyToggle.ToggleVisibility online) and it never changed hands.");
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"LOADOUT CONFIRM: handing the control back threw ({e.GetType().Name}) — "
                              + "it may be left under the Character-UI's root. It is the GAME's own "
                              + "object and the game re-lays it out on the next EnterLoadout.");
        }
        finally
        {
            _home = null;
            _homeRecorded = false;
            _homeOwner = null;
        }
    }

    // ---- the measured zero -----------------------------------------------------------------------

    private static void ResetAnchor(string why)
    {
        _anchorValid = false;
        _anchorNextRefreshAt = float.NegativeInfinity;
        _anchorPivot = Vector2.zero;
        _anchorContent = default;
        _anchorContentCount = 0;
        _anchorControl = default;
        _anchorControlCount = 0;
        _anchorWhy = why;
    }

    /// <summary>
    /// WHERE THE CONFIRM GOES, AND WHY THAT PLACE IS DERIVED RATHER THAN DIALLED.
    ///
    /// <para>The zero is the Character-UI's OWN PAINTED CONTENT: the control's painted TOP edge is put
    /// <see cref="ConfirmGapPx"/> below the window's painted BOTTOM edge, horizontally centred on that
    /// content's centre. This is <c>MapTravelConfirm</c>'s ModBuild 197 construction with the quest
    /// information swapped for the roster, and it is the right one here for three reasons that are all
    /// measurements rather than preferences:</para>
    /// <list type="number">
    /// <item><b>It is where the flat game puts it.</b> Offline the continue button is the loadout
    /// screen's bottom-centre confirm; online the ready toggle is a bottom bar. Under the roster,
    /// centred, is the same reading order the player already knows.</item>
    /// <item><b>It cannot land on top of anything.</b> The zero is the union of what the window is
    /// ACTUALLY painting this tick — the roster grows and shrinks as characters are added, a sub-panel
    /// opens, a battle goal is picked — so a fixed fraction of the window would sit over the roster on
    /// one party size and off the panel on another. The union moves with it.</item>
    /// <item><b>The host rect follows it for free.</b> <c>CanvasConversion</c>'s fit commits the
    /// drawn-content union as the host rect, so a control placed just under the content is inside the
    /// fitted panel by construction — no height dial and no second layout step.</item>
    /// </list>
    ///
    /// <para><b>THE SWEEP EXCLUDES THE PARKED CONTROL, AND THAT IS WHAT MAKES IT A FIXED POINT RATHER
    /// THAN A FEEDBACK LOOP.</b> If the control's own graphics were in the union, every solve would
    /// push it one gap further down, once per cadence, forever. The control's pivot-to-content offset
    /// is measured separately and subtracted, exactly as <c>MapTravelConfirm.MaybeRefreshAnchor</c>
    /// does, so translating the control moves its pivot and its ink by the SAME vector and re-solving
    /// after a write reproduces the same answer with zero gain.</para>
    ///
    /// <para>A FAILED REFRESH NEVER CLEARS A GOOD ANCHOR: the character screen switches whole
    /// sub-panels off, and a sweep that finds nothing must keep the last good measurement rather than
    /// throw the button to the origin.</para>
    /// </summary>
    private static void ApplyPose(UIWindow host, GameObject control)
    {
        if (host.transform is not RectTransform win || control.transform is not RectTransform rect)
            return;
        Rect frame = win.rect;
        if (Mathf.Abs(frame.height) <= 0f)
            return;

        float now = Time.unscaledTime;
        if (now >= _anchorNextRefreshAt)
        {
            _anchorNextRefreshAt = now + AnchorRefreshIntervalSeconds;
            RefreshAnchor(win, rect, frame);
        }
        if (!_anchorValid)
            return;

        // ANCHORS ARE READ, NEVER RE-WRITTEN unless they are STRETCHED. anchoredPosition means
        // nothing without the anchor it is measured from, so the number written is derived from the
        // anchor the rect is carrying this tick — a second writer that re-anchors the control cannot
        // move it by a pixel, it changes an input this line recomputes. MapTravelConfirm's rule.
        if (rect.anchorMin != rect.anchorMax)
            rect.anchorMin = rect.anchorMax = Vector2.up;
        Vector2 a = (rect.anchorMin + rect.anchorMax) * 0.5f;
        var reference = new Vector2(frame.xMin + a.x * frame.width, frame.yMin + a.y * frame.height);
        Vector2 want = _anchorPivot - reference;
        if ((rect.anchoredPosition - want).sqrMagnitude > OffsetEpsilonPx * OffsetEpsilonPx)
            rect.anchoredPosition = want;

        if (_parkLogged)
            return;
        _parkLogged = true;
        VRLog.Info(Scope, "LOADOUT CONFIRM PARKED: the game's pre-scenario continue control "
                          + $"'{control.name}' "
                          + (_parkedIsReadyToggle
                              ? "(the MULTIPLAYER ready toggle, UIReadyToggle — online this is what "
                                + "every player presses for himself: MPConfirmEnterScenario "
                                + "initialises it with show:true and label GUI_LOADOUT_ENTER_SCENARIO "
                                + "on EVERY client, and only the all-ready callback is host-gated, "
                                + "UILoadoutManager.cs:474-517)"
                              : "(the SINGLE-PLAYER long-confirm button, "
                                + "UILoadoutManager.confirmationButton, the object "
                                + "SetActiveSinglePlayerLongConfirmButton switches on at :94)")
                          + $" was moved from '{(_home != null ? _home.name : "<none>")}' INTO the "
                          + $"floated Character-UI '{host.name}' (ID {host.ID}). THE PLACE IS DERIVED, "
                          + "NOT DIALLED: its painted TOP edge sits "
                          + $"{ConfirmGapPx:F0} authored px below the window's own painted BOTTOM edge "
                          + $"({_anchorContent.xMin:F0}..{_anchorContent.xMax:F0} x "
                          + $"{_anchorContent.yMin:F0}..{_anchorContent.yMax:F0} px, unioned this tick "
                          + $"from {_anchorContentCount} drawn graphic(s) that passed the CONVERSION'S "
                          + "OWN fit visibility verdict, with this control's own "
                          + $"{_anchorControlCount} graphic(s) EXCLUDED so the solve is a fixed point "
                          + "and not a feedback loop), CLAMPED into the window's own rect so a uGUI "
                          + "mask can never cull it, horizontally centred on that content — the "
                          + "same construction MapTravelConfirm's ModBuild 197 zero uses on the quest "
                          + "card, with the quest information swapped for the roster. WHY HERE AND NOT "
                          + "ON THE STORY WINDOW: pressing it is a LOCAL act (see this class's doc for "
                          + "the source), and the user's ruling was \"abhängig von der Antwort … "
                          + "entweder auf der lokalen Character-UI oder dem MP-Fenster mit der "
                          + "Story\". NOTHING WAS WRITTEN TO THE GAME: no Show, no Hide, no Escape, no "
                          + "SetActive, no CanvasGroup — this is a re-parent of a local uGUI subtree "
                          + "on this client and nothing goes on the wire.");
    }

    private static void RefreshAnchor(RectTransform win, RectTransform rect, Rect frame)
    {
        ConvertedPanel? panel = ModalFallback.PanelFor(_host);
        if (!TryPaintedBounds(rect, win, panel, exclude: null, out Rect ctrl, out int ctrlCount)
            || ctrlCount == 0)
        {
            _anchorWhy = "the control has no visible Graphic this tick (the game usually has it "
                         + "switched off until the requirements are met) — the previous zero is kept";
            return;
        }
        if (!TryPaintedBounds(win, win, panel, exclude: rect, out Rect raw, out int rawCount)
            || rawCount == 0)
        {
            _anchorWhy = "the Character-UI paints no visible Graphic outside the control itself — "
                         + "the previous zero is kept";
            return;
        }

        // FRAME-CLAMPED, MapTravelConfirm's reason verbatim: the raw union of a converted window
        // reaches well outside its own rect from graphics faint enough that the fit throws them away,
        // and clamping is what makes the horizontal zero agree with the panel the player sees.
        Rect content = Rect.MinMaxRect(Mathf.Max(raw.xMin, frame.xMin), Mathf.Max(raw.yMin, frame.yMin),
                                       Mathf.Min(raw.xMax, frame.xMax), Mathf.Min(raw.yMax, frame.yMax));
        if (content.width <= 0f || content.height <= 0f)
        {
            _anchorWhy = "the Character-UI's visible union does not overlap its own rect at all, so "
                         + "clamping leaves nothing to measure — the previous zero is kept";
            return;
        }

        // THE CONTROL'S OWN PIVOT-TO-INK OFFSET. RectTransform.position IS the pivot's world
        // position, so this is exact whatever anchors the rect is carrying — which is the point:
        // a second writer that re-anchors it between two of our ticks cannot corrupt the solve.
        Vector3 pivotLocal = win.InverseTransformPoint(rect.position);
        float inkTopFromPivot = ctrl.yMax - pivotLocal.y;
        float inkCentreFromPivot = ctrl.center.x - pivotLocal.x;

        // AND THE CONTROL MUST STAY INSIDE THE WINDOW'S OWN RECT, WHICH IS NOT A COSMETIC BOUND.
        // The character screen clips its content with uGUI masks, and a control pushed past the
        // window's bottom edge would be CULLED — invisible, while every state test in this file still
        // read "parked, active, claimed". That is the exact failure mode this project has shipped
        // before ([[measure-the-picture-not-the-state]]), and it would be a deadlock wearing the
        // fix's clothes. So the ink is clamped into the frame: if the painted content already reaches
        // the bottom of the window, the confirm sits flush with that bottom edge and overlaps the
        // last of the roster rather than falling off the panel. Ugly beats unreachable, every time.
        float inkTop = Mathf.Clamp(content.yMin - ConfirmGapPx,
                                   frame.yMin + Mathf.Abs(ctrl.height), frame.yMax);
        _anchorPivot = new Vector2(content.center.x - inkCentreFromPivot, inkTop - inkTopFromPivot);
        _anchorValid = true;
        _anchorContent = content;
        _anchorContentCount = rawCount;
        _anchorControl = ctrl;
        _anchorControlCount = ctrlCount;
        _anchorWhy = "resolved";
    }

    private static readonly List<Graphic> PaintScratch = new(64);
    private static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>
    /// The painted bounds of a subtree in <paramref name="win"/>'s local (authored uGUI) space, with
    /// an optional subtree EXCLUDED.
    ///
    /// <para>THE VISIBILITY VERDICT IS BORROWED, NOT RE-INVENTED: with a panel in hand this asks
    /// <c>CanvasConversion.CountsAsFitContent</c> — the fit's own four-reason test — so the numbers
    /// here and the numbers in the fit line cannot disagree. That is the same choice
    /// <c>StoryComposite.TryPaintedBounds</c> makes and for the same recorded reason: a second, weaker
    /// predicate is how <c>MrBacking.GlyphTrueRect</c> once unioned back in text the fit had already
    /// judged invisible.</para>
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

    // ---- the park claim (online only) -------------------------------------------------------------

    /// <summary>
    /// Re-assert or drop <c>MapRoom.ReadyToggleParkClaim</c> — a LEVEL and never a latch, exactly as
    /// <c>MapQuestReadyUp.TickClaim</c> does it, because <c>FloatRefusalTable</c>'s ROW 2 is the
    /// reader and a refusal with no parker behind it is a deadlock generator.
    ///
    /// <para>THE CLAIM IS MADE ON INTENT, BEFORE THE GAME SHOWS THE TOGGLE, for the reason
    /// <c>MapQuestReadyUp</c> records: the toggle's <c>Show</c> can beat this parker's first tick to
    /// the float gate, and a claim that arrives one frame late means a float this class then has to
    /// withdraw. An invisible window never floats, so an early claim costs nothing.</para>
    ///
    /// <para>AND IT LETS GO RATHER THAN HOLD. If the toggle is on screen and NOT parked for longer
    /// than <see cref="ClaimGraceSeconds"/> the claim is dropped with one warning: the standing rule
    /// is that a window must never be invisible, and it outranks the rule that a bare button must not
    /// float. The <c>FLOAT REFUSAL LAPSED</c> warning that follows is EXPECTED beside it, not a second
    /// fault.</para>
    /// </summary>
    private static void TickClaim(GameObject? control, bool readyToggle, bool parked)
    {
        if (!readyToggle || control == null)
        {
            DropClaim(control == null ? "there is no confirm control to claim"
                                      : "the offline confirm is a plain Button, not a UIWindow — the "
                                        + "float gate never sees it and there is nothing to claim");
            return;
        }

        if (parked)
        {
            _visibleUnparkedSince = -1f;
            SetClaim(control, "LoadoutConfirmPark has parked it INSIDE the floated Character-UI "
                              + $"'{(_host != null ? _host.name : "<gone>")}', directly under the "
                              + "window's own painted content and centred on it — the local place the "
                              + "user chose for a control every player presses for himself");
            return;
        }

        if (!ReadyToggleVisible())
        {
            // NOT DRAWN, SO NOTHING CAN FLOAT AND NOTHING CAN BE UNREACHABLE. This is the claim that
            // beats the toggle's own Show to the float gate.
            _visibleUnparkedSince = -1f;
            SetClaim(control, "LoadoutConfirmPark is standing by to park the loadout ready toggle "
                              + "into the floated Character-UI the moment the game shows it; the claim "
                              + "is made on INTENT so the float gate has an answer the very first time "
                              + "it asks, rather than one frame after a float it would have to "
                              + "withdraw");
            return;
        }

        if (_visibleUnparkedSince < 0f)
            _visibleUnparkedSince = Time.unscaledTime;
        if (Time.unscaledTime - _visibleUnparkedSince <= ClaimGraceSeconds)
        {
            SetClaim(control, "LoadoutConfirmPark is about to park the loadout ready toggle into the "
                              + "Character-UI — that window is not a live floated panel yet, and this "
                              + "claim covers the few frames it takes to become one");
            return;
        }
        DropClaim($"the loadout ready toggle has been on screen and unparked for more than "
                  + $"{ClaimGraceSeconds:F1} s, so the refusal is released rather than leaving a "
                  + "button nobody can press");
    }

    private static void SetClaim(GameObject control, string why)
    {
        if (!_claimStanding || why != _claimWhy)
        {
            _claimStanding = true;
            _claimWhy = why;
            VRLog.Info(Scope, $"LOADOUT CONFIRM: park claim SET for '{control.name}' — {why}. WHAT IT "
                              + "BUYS: while it stands FloatRefusalTable's ROW 2 refuses to float the "
                              + "ready toggle as a window of its own, so the free-floating button in "
                              + "its own frame (.planning/debug/frei_schwebender_button_multiplayer"
                              + ".jpg) cannot appear. THE CLAIM IS A LEVEL with a one-second lifetime: "
                              + "it is re-asserted on every tick of this parker and lapses by itself if "
                              + "the parker stops running, throws or stands down — which is what makes "
                              + "it impossible for this refusal to outlive the thing that justifies "
                              + "it.");
        }
        ReadyToggleParkClaim.Set(control, _claimWhy);
    }

    private static void DropClaim(string why)
    {
        if (!_claimStanding)
            return;
        _claimStanding = false;
        _claimWhy = "nothing is claimed";
        _visibleUnparkedSince = -1f;
        VRLog.Info(Scope, $"LOADOUT CONFIRM: park claim DROPPED — {why}. The catch-all may float the "
                          + "ready toggle as a window of its own again from here, which is deliberate: "
                          + "a confirm nobody can reach is a worse fault than a confirm in an ugly "
                          + "frame.");
        ReadyToggleParkClaim.Set(null, "LoadoutConfirmPark is not presenting the loadout confirm");
    }

    // ---- the falsifier -----------------------------------------------------------------------------

    private static void ResetReach()
    {
        _reachVerdict = string.Empty;
        _reachReports = 0;
        _reachHost = null;
    }

    /// <summary>
    /// CAN THIS PLAYER GET AT THE CONTINUE CONTROL RIGHT NOW? A PURE read for
    /// <c>StoryComposite</c>'s deadlock floor: TRUE whenever the game is not asking for a control at
    /// all (there is nothing to be unreachable), and TRUE when the control is measured drawing inside
    /// a live floated panel. FALSE is therefore exactly the deadlock and nothing weaker.
    ///
    /// <para>THE HOST IS NOT ASSUMED TO BE THIS CLASS'S PARK. It walks UP from the control to the
    /// nearest <c>UIWindow</c> ancestor the mod is floating with a live panel, so the answer is TRUE
    /// in both of the good states: parked into the Character-UI by this class, and drawn on a loadout
    /// screen that is floating normally because nothing is refusing it. A floor that only recognised
    /// its own remedy would report a deadlock every time the remedy was unnecessary.</para>
    /// </summary>
    internal static bool ConfirmReachable()
    {
        try
        {
            UILoadoutManager? lm = Manager();
            return lm == null || MeasureReach(lm, out _, out _);
        }
        catch (System.Exception)
        {
            return true;   // never trip a floor on a measurement that failed
        }
    }

    /// <summary>
    /// The one measurement both the falsifier and <see cref="ConfirmReachable"/> read, so a log line
    /// and a floor can never disagree about the same tick.
    /// </summary>
    private static bool MeasureReach(UILoadoutManager lm, out string measured, out bool readyToggle)
    {
        if (!GameWantsConfirmShown())
        {
            readyToggle = false;
            measured = "the game is not asking for a continue control this tick";
            return true;
        }

        GameObject? control = ResolveControl(lm, out readyToggle);
        bool active = control != null && control.activeInHierarchy;
        UIWindow? drawnIn = active ? FloatedAncestorWindow(control!.transform) : null;
        ConvertedPanel? panel = ModalFallback.PanelFor(drawnIn);
        int drawn = 0;
        if (active && drawnIn != null && control!.transform is RectTransform rect
            && drawnIn.transform is RectTransform win)
            TryPaintedBounds(rect, win, panel, exclude: null, out _, out drawn);

        measured =
            "the game says a continue control should be showable now (UILoadoutManager.IsOpen=True, "
            + $"CanShowConfirmationButton()=True, network={(FFSNetwork.IsOnline ? "ONLINE" : "OFFLINE")}); "
            + "the control is "
            + (control != null
                ? $"'{control.name}' ({(readyToggle
                    ? "UIReadyToggle — the multiplayer ready toggle"
                    : "UILoadoutManager.confirmationButton — the single-player long confirm")})"
                : "<the mod cannot resolve which object IS the confirm>")
            + $", activeInHierarchy={active}, drawing {drawn} graphic(s) the conversion's own fit "
            + "counts; the nearest floated window it is inside is "
            + (drawnIn != null ? $"'{drawnIn.name}' (ID {drawnIn.ID}), live panel={panel != null}"
                               : "<NONE — it is inside no window the mod is floating>")
            + $"; this class {(_parked != null && _host != null ? $"has it parked in '{_host.name}'" : "has not parked it")}";
        return control != null && active && drawnIn != null && panel != null && drawn > 0;
    }

    /// <summary>
    /// The nearest <c>UIWindow</c> ANCESTOR (or self) that the mod is floating with a live panel, or
    /// null. A containment walk is the right shape here and only here: the question is not "IS this
    /// object an X" ([[containment-is-not-identity]]) but "is this object being DRAWN inside one of
    /// ours", which is a question about the hierarchy by construction.
    /// </summary>
    private static UIWindow? FloatedAncestorWindow(Transform from)
    {
        Transform? t = from;
        for (int guard = 0; t != null && guard < 64; guard++, t = t.parent)
        {
            UIWindow? w = t.GetComponent<UIWindow>();
            if (w != null && ModalFallback.PanelFor(w) != null && FloatedByMod(w))
                return w;
        }
        return null;
    }

    /// <summary>
    /// THE ONE LINE A TESTER CAN GREP THAT IS TRUE ONLY IF THE PLAYER CAN ACTUALLY GET ON WITH THE
    /// GAME, and the WARNING that names the suppressor when he cannot.
    ///
    /// <para>It is asked only while the GAME says a continue control should be showable
    /// (<see cref="GameWantsConfirmShown"/> — <c>UILoadoutManager.CanShowConfirmationButton()</c> plus
    /// the game's own switch on the control), so it is silent through the whole battle-goal phase and
    /// speaks exactly at the moment the ModBuild 234 tester was stuck. Every clause of the CONFIRMED
    /// form is measured on the object THIS TICK: the control is active in the hierarchy, it is
    /// painting at least one graphic the conversion's own fit counts, and its host is a live floated
    /// panel. It asserts nothing it cannot see ([[an-instrument-can-assert-a-cause]]).</para>
    ///
    /// <para>GREP: <c>LOADOUT CONFIRM REACHABLE: CONFIRMED</c> — the fix.
    /// <c>LOADOUT CONFIRM REACHABLE: NO</c> — the deadlock, with the suppressor named.</para>
    /// </summary>
    private static void ReportReach(UILoadoutManager lm, UIWindow? loadout)
    {
        if (!ReferenceEquals(loadout, _reachHost))
        {
            _reachHost = loadout;
            _reachVerdict = string.Empty;
            _reachReports = 0;
        }
        if (_reachReports >= MaxReachReports)
            return;
        if (!GameWantsConfirmShown())
        {
            _reachVerdict = string.Empty;   // the question is not being asked; re-arm the edge
            return;
        }

        bool ok = MeasureReach(lm, out string measuredCore, out bool readyToggle);
        string verdict = ok ? "CONFIRMED" : "NO";
        if (verdict == _reachVerdict)
            return;
        _reachVerdict = verdict;
        _reachReports++;

        string measured = measuredCore
            + "; the measured zero is "
            + (_anchorValid
                ? $"the host's painted content {_anchorContent.xMin:F0}..{_anchorContent.xMax:F0} x "
                  + $"{_anchorContent.yMin:F0}..{_anchorContent.yMax:F0} px from "
                  + $"{_anchorContentCount} graphic(s), against the control's own ink "
                  + $"{_anchorControl.width:F0}x{_anchorControl.height:F0} px from "
                  + $"{_anchorControlCount}"
                : $"NOT RESOLVED — {_anchorWhy}")
            + $" (control kind: {(readyToggle ? "the multiplayer ready toggle" : "the single-player long confirm")})";

        if (ok)
        {
            VRLog.Info(Scope, "LOADOUT CONFIRM REACHABLE: CONFIRMED — the player can see and press the "
                              + "continue control. MEASURED THIS TICK: " + measured + ". USER REPORT "
                              + "THIS LINE ANSWERS: \"DEADLOCK: Nachdem ich für jeden Character die "
                              + "Quest ausgewählt habe, muss irgendwo der button erscheinen damit es "
                              + "weiter gehen kann. Der ist nie erschienen, man konnte nicht weiter "
                              + "vorranschreiten.\"");
            return;
        }
        VRLog.Warn(Scope, "LOADOUT CONFIRM REACHABLE: NO — THE GAME WANTS A CONTINUE CONTROL SHOWN AND "
                          + "THE MOD CANNOT FIND IT ANYWHERE. THIS IS THE DEADLOCK: there is nothing to "
                          + "press and no way forward. MEASURED THIS TICK: " + measured
                          + ". THE SUPPRESSOR, IF ANY: "
                          + (loadout != null
                              ? FloatRefusalTable.Describe(loadout)
                                ?? "nothing in the refusal table has anything to say about the loadout "
                                   + "window, so its float is not being withheld by this mod — look at "
                                   + "the catch-all's own eligibility instead (MODAL FALLBACK / "
                                   + "CATCH-ALL lines for 'UI Loadout Window')"
                              : "the loadout window itself could not be resolved")
                          + ". READ IT LIKE THIS: offline the continue button is a CHILD of the loadout "
                          + "window, so a refusal that withholds that window's float takes the button "
                          + "with it — that is exactly what happened in the ModBuild 234 log "
                          + "(FLOAT REFUSED / FLOAT WITHDRAWN at :11483-11484, and no further float of "
                          + "'UI Loadout Window' in the rest of the session). This class exists to make "
                          + "that survivable by drawing the control in the Character-UI instead; if "
                          + "this line is in a hardware log, look UP for LOADOUT CONFIRM UNPARKED or a "
                          + "'cannot park' line to see why it is not.");
    }

    /// <summary>Module teardown. Hands the control back and lets go of the claim — a claim that
    /// outlived this class would keep the game's own confirm off screen with nothing left to park it
    /// into, which is the one failure mode this whole file exists to be incapable of.</summary>
    internal static void Reset()
    {
        Unpark("module teardown");
        DropClaim("module teardown");
        _standDown = false;
        _standDownWarned = false;
        _home = null;
        _homeRecorded = false;
        _homeOwner = null;
        _addedIgnore = null;
        ResetAnchor("module teardown");
        ResetReach();
        PaintScratch.Clear();
    }
}
