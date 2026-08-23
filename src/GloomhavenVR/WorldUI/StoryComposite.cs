using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE QUEST-INTRO STORY IS ONE WINDOW, AND WHILE IT STANDS NOTHING ELSE DOES.
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
/// <para><b>WHAT THE TWO WINDOWS ACTUALLY ARE — read from the game, not from the picture.</b> The
/// quest intro is built by <c>UILoadoutQuestWindow</c>
/// (decompiled/GH.Runtime/UILoadoutQuestWindow.cs), and it deliberately puts its two halves in two
/// different <c>UIWindow</c>s on two different canvases:</para>
/// <code>
///   UILoadoutQuestWindow.Show(quest, …)                                 (:47)
///     imagePaper.LoadImages([quest.LoadoutImageId], …)                  (:52)  ← THE IMAGE
///       → ShowLoadoutBackground → imagePaper.Show(quest.LoadoutImageId) (:60)
///       → StartCoroutine(ShowIntroductionText(quest))                   (:61)
///           yield 0.4 s, then
///           Singleton&lt;MapStoryController&gt;.Instance.Show(
///               EMapMessageTrigger.Loadout, … quest.LocalisedIntroKey …,
///               FinishIntroduction, hideOtherUI: false)                 (:88-93) ← THE DIALOG
/// </code>
/// <para>So the IMAGE belongs to <c>UI Loadout Window</c> (<c>Campaign Canvas/UI Loadout Window</c>,
/// component <c>UILoadoutManager</c>) and the DIALOG belongs to <c>Map Story Window</c>
/// (<c>Story Canvas/Map Story Window</c>, component <c>MapStoryController</c>). The ModBuild 231
/// hardware log names both, and it names the art too:</para>
/// <code>
///   WINDOW IDENTITY 'Map Story Window' (ID None): path Story Canvas/Map Story Window; …
///     components [… UIWindow, MapStoryController]; nearest ancestor UIWindow &lt;none&gt;.
///   Host rect fit '…Panel_Modal_Map Story Window': 1920x1080 → 1096x233 px …
///     top (rendered rects): 'Dialog/DialogContent' …           ← the dialog, and ONLY the dialog
///   WINDOW IDENTITY 'UI Loadout Window' (ID None): path Campaign Canvas/UI Loadout Window; …
///     components [… UILoadoutManager, … UIWindow …]; nearest ancestor UIWindow &lt;none&gt;.
///   MODAL WINDOW: '…Panel_Modal_UI Loadout Window' pre-reveal first fit … 1920x1080 …
///     top (rendered rects): 'UI Loadout Window/UI Loadout Quest Information' 1920x1080px,
///     'UI Loadout Quest Information/Blur' 1920x1080px, 'Holder/Paper' 1280x720px  ← THE IMAGE
///   MIP BAKE atlas readback cached: 'ICE_Cave_01' 1920x1080
///   MIP BAKE ARRIVAL on 'UI Loadout Window' … 1 graphic(s) swapped to a mipmapped copy
/// </code>
///
/// <para><b>WHY ModBuild 226 DID NOT FIX THIS, WHICH IS THE PART WORTH WRITING DOWN.</b> That build
/// widened <c>ModalFallback.RendersInsideFloatedAncestor</c> out of the map room and recorded, in
/// the <c>WindowGroups</c> table's own doc comment, that <i>"the two other splits in the same report
/// — the story box and its subtitle, and the encounter — are HIERARCHY groups and are answered by
/// the first arm … Do not add rows for them"</i>. Both halves of that are false against the code:
/// <list type="number">
/// <item>THEY ARE NOT IN A HIERARCHY. The two windows are roots of two different canvases
/// (<c>Story Canvas</c> and <c>Campaign Canvas</c>) and both log lines end with
/// <c>nearest ancestor UIWindow &lt;none&gt;</c>. No walk over parents can ever relate them.</item>
/// <item>EVEN A DECLARED ROW COULD NOT HAVE FIRED, because of the ORDER. The image's window opens
/// FIRST and the dialog's window arrives 0.4 s later (<c>delayToShowText</c>, UILoadoutQuestWindow
/// .cs:32/86). By then the loadout window is already floated — and the catch-all re-adds a window
/// it is ALREADY floating <b>unconditionally, before every eligibility test</b>
/// (<c>ModalFallback.10.CatchAll.cs</c>, <c>bool oursAlready = IsFloatedByUs(window)</c> →
/// <c>OpenWindows.Add</c> → <c>continue</c>; that unconditional re-add is itself a deliberate
/// ModBuild 186 fix for an oscillation bug and must not be touched). A suppression rule that is
/// only consulted for windows that have NOT floated yet cannot answer a split whose first half
/// floats first.</item>
/// </list>
/// <b>So this is not a second copy of that predicate.</b> The predicate is asked at a moment that
/// has already passed; what is needed is an ACTIVE step at the moment the dialog arrives. That step
/// is <see cref="Tick"/>, and it does the one thing the declarative rule cannot: it MOVES the image
/// and RELEASES the float that was holding it.
///
/// <para><b>WHICH WINDOW HOSTS THE COMPOSITE, AND WHY IT IS THE STORY.</b> The dialog's window is
/// the one that is SHARED (<see cref="SharedWindowKind.MapStory"/>, wire kind 1, the blue bar and
/// the record-21 pose), the one the user calls "das Storyfenster", and the one that must be
/// click-through with no X. The loadout window is none of those things: after the intro finishes
/// (<c>FinishIntroduction</c> expands the paper, UILoadoutQuestWindow.cs:96) it becomes the
/// pre-scenario LOADOUT SCREEN with its own confirm, and a screen that is permanently blue and
/// permanently X-less would be a worse bug than the split. So the story window leads, the image is
/// parked into it, and when the story closes the image goes home and the loadout screen floats for
/// the first time — one float, at its own pose, with its own chrome.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here goes on the wire and nothing here writes game state that
/// travels. The park is a local re-parent of a local uGUI subtree; the sweep is a PRESENTATION
/// release (<c>CanvasConversion.Release</c> — panel, grab holder, X, collider and arc slot — and
/// deliberately NOT <c>UIWindow.Hide()</c>/<c>Escape()</c>, which would write game state); the
/// destination lock-out drives the GAME'S OWN per-button state through the game's own public
/// methods, which is what every flat client does to itself at the same moment. For a SHARED window
/// that the sweep releases (the quest-confirm, kind 2) the consequence is exactly the record's
/// documented one: this client stops publishing that entry, peers Forget it, nobody's game state
/// changes and nobody is driven anywhere. A peer that joins mid-story sees its own copy of whatever
/// the game gives it and composes it locally by the same rule.</para>
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
    /// paper measured 1280x720 inside a 1920x1080 window (0.67 x 0.67); the thing directly above it
    /// in the same subtree is a full-window 1920x1080 <c>Blur</c>, and parking THAT would drag a
    /// full-screen dark rectangle into the story panel. This is the guard that makes the difference
    /// measurable instead of assumed.</summary>
    private const float MaxParkFractionOfWindow = 0.9f;

    // ---- park state ---------------------------------------------------------------------------

    private static RectTransform? _parked;
    private static Transform? _home;
    private static int _homeIndex;
    private static Vector2 _homeAnchorMin;
    private static Vector2 _homeAnchorMax;
    private static Vector2 _homePivot;
    private static Vector2 _homeAnchoredPos;
    private static Quaternion _homeRotation = Quaternion.identity;
    private static Vector3 _homeScale = Vector3.one;
    private static LayoutElement? _addedIgnore;
    private static UIWindow? _parkHost;
    private static bool _composeLogged;

    // ---- gate state ---------------------------------------------------------------------------

    private static bool _gateOpen;
    private static int _sweeps;
    private static float _nextSweepAt;
    private static int _closedAtOpen;
    private static readonly List<UIGuildmasterButton> Greyed = new(8);
    private static readonly List<bool> GreyedHadHighlight = new(8);
    private static Component? _cityRequest;

    /// <summary>How many level-triggered re-sweeps one gate may run before it stands down and says
    /// so. See <see cref="Tick"/>: the one-shot at the rising edge is the fix; the re-sweeps are a
    /// belt-and-braces that only does anything if a window re-floats, and a window that re-floats
    /// forever means the convert-loop hold is missing from this build. Standing down with a Warn is
    /// better than a release/refloat loop, which this project has shipped before
    /// ([[fuse-was-hiding-a-loop]]).</summary>
    private const int MaxSweepsPerGate = 3;

    /// <summary>Seconds between level-triggered sweeps. Short enough that the degraded case (the
    /// convert-loop hold missing) costs about a second and a half of churn rather than ten, and long
    /// enough that a single stray float is not chased at the frame rate.</summary>
    private const float SweepIntervalSeconds = 0.5f;

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
    /// <c>Toggle.interactable</c> that <c>Pressable</c> already reads.</para>
    /// </summary>
    internal static bool PointOfNoReturn =>
        MapRoomDriver.Active
        && Singleton<UILoadoutManager>.IsInitialized
        && Singleton<UILoadoutManager>.Instance != null
        && Singleton<UILoadoutManager>.Instance.IsOpen;

    /// <summary>The story window of the composite, or null. Open-or-visible, never "floated": the
    /// park has to happen on the tick the window OPENS, one tick before the conversion measures it,
    /// or the panel's first fit would size itself to the dialog alone and then jump.</summary>
    private static UIWindow? StoryWindow()
    {
        if (!Singleton<MapStoryController>.IsInitialized)
            return null;
        MapStoryController mc = Singleton<MapStoryController>.Instance;
        if (mc == null || mc.window == null)
            return null;
        return mc.window.IsOpen || mc.window.IsVisible ? mc.window : null;
    }

    private static UIWindow? LoadoutWindow()
    {
        if (!Singleton<UILoadoutManager>.IsInitialized)
            return null;
        UILoadoutManager lm = Singleton<UILoadoutManager>.Instance;
        return lm != null ? lm.GetComponent<UIWindow>() : null;
    }

    /// <summary>
    /// The image subtree to park: the <c>StoryImageViewer</c>'s own <c>container</c>, which is the
    /// GameObject that viewer switches on and off around the <c>imageHolder</c> it draws the sprite
    /// into (decompiled/GH.Runtime/StoryImageViewer.cs:131-137, :186-192, :247). Reached through
    /// <c>UILoadoutManager.questInfo</c> → <c>UILoadoutQuestWindow.imagePaper</c> — private
    /// <c>[SerializeField]</c>s read as ordinary members because the build PUBLICIZES the game
    /// assemblies, the same route <c>Net.RemoteMapStory</c> takes to
    /// <c>MapStoryController.dialogBox</c>.
    ///
    /// <para>NOT the <c>UILoadoutQuestWindow</c> itself and not the viewer's own transform unless it
    /// has to be: the log's rendered-rect list shows a full-window <c>Blur</c> sitting in that same
    /// subtree, and dragging it into the story panel would compose a 1920x1080 dark rectangle
    /// instead of a picture. <see cref="MaxParkFractionOfWindow"/> is the measurement that enforces
    /// it rather than a comment that hopes for it.</para>
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
                return null;
            GameObject? container = viewer.container;
            Transform t = container != null && !ReferenceEquals(container, viewer.gameObject)
                ? container.transform
                : viewer.transform;
            var rect = t as RectTransform;
            if (rect == null || !rect.IsChildOf(loadout.transform))
                return null;
            var winRect = loadout.transform as RectTransform;
            if (winRect == null)
                return null;
            Vector2 ws = winRect.rect.size;
            Vector2 rs = rect.rect.size;
            if (ws.x > 1f && ws.y > 1f
                && (rs.x > ws.x * MaxParkFractionOfWindow || rs.y > ws.y * MaxParkFractionOfWindow))
            {
                VRLog.Warn(Scope, $"STORY COMPOSITE: refused to park '{rect.name}' ({rs.x:F0}x{rs.y:F0} px) "
                                  + $"into the story window — it covers more than "
                                  + $"{MaxParkFractionOfWindow:P0} of its own window ({ws.x:F0}x{ws.y:F0} px), "
                                  + "so it is the loadout screen's full-window group (the 'Blur' rect in the "
                                  + "ModBuild 231 log) rather than the quest picture. The two windows stay "
                                  + "separate this run, which is the status quo and not a new failure.");
                return null;
            }
            return rect;
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, "STORY COMPOSITE: could not reach the loadout screen's quest picture "
                              + $"(UILoadoutManager.questInfo/imagePaper): {e.GetType().Name}. The two "
                              + "windows stay separate, which is the status quo.");
            return null;
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
    /// </summary>
    internal static void Tick()
    {
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

        if (gateWanted && !_gateOpen)
            OpenGate(story, loadout);
        else if (!gateWanted && _gateOpen)
            CloseGate();

        if (!_gateOpen)
            return;
        // LEVEL-TRIGGERED BELT-AND-BRACES. The rising-edge sweep is the fix; this only fires if
        // something floated anyway, and it is rate-limited and fused because a release that is
        // immediately followed by a re-convert is a loop, not a repair.
        float now = Time.unscaledTime;
        if (now < _nextSweepAt || _sweeps >= MaxSweepsPerGate)
            return;
        UIWindow? keep = story ?? loadout;
        if (ModalFallback.CountFloatsOtherThan(keep) <= 0)
            return;
        _nextSweepAt = now + SweepIntervalSeconds;
        _sweeps++;
        int closed = ModalFallback.ReleaseFloatsExcept(keep, out string names);
        if (_sweeps < MaxSweepsPerGate)
        {
            VRLog.Info(Scope, $"POINT OF NO RETURN: re-closed {closed} window(s) that floated after the "
                              + $"gate opened [{names}] (sweep {_sweeps} of {MaxSweepsPerGate}). This is "
                              + "the level-triggered arm; the one-shot at the gate's own edge is what "
                              + "normally does the work.");
            return;
        }
        VRLog.Warn(Scope, $"POINT OF NO RETURN: the sweep has fired {MaxSweepsPerGate} times for this "
                          + $"loadout and windows are STILL re-floating [{names}] — standing down for "
                          + "the rest of this gate rather than running a release/refloat loop. That "
                          + "means the convert-loop hold (`if (StoryComposite.HoldsBack(window)) "
                          + "continue;` in ModalFallback.4.Tick's convert loop) is not in this build: "
                          + "without it a window the GAME still reports open is re-converted on the "
                          + "very next tick, which is exactly what this counter measured.");
    }

    /// <summary>
    /// THE CONVERT-LOOP HOLD, offered to the lane that owns <c>ModalFallback.4.Tick.cs</c>. True for
    /// every window that must not float while the point of no return stands — i.e. everything except
    /// the composed story window and, once the story has closed, the loadout screen itself.
    ///
    /// <para>ONE LINE, in the convert loop's existing guard chain, right after
    /// <c>if (EmptyHeldNow(window)) continue;</c>. It is a <c>continue</c> and not a
    /// <c>TryConvertWindow</c> refusal on purpose: a refusal enrols the window in <c>Failed</c> and
    /// raises the flat screen for it, and "closed" here means closed, not moved to a screen.</para>
    /// </summary>
    internal static bool HoldsBack(UIWindow? window)
    {
        if (window == null || !_gateOpen)
            return false;
        UIWindow? story = StoryWindow();
        if (story != null && ReferenceEquals(story, window))
            return false;
        if (story == null && ReferenceEquals(LoadoutWindow(), window))
            return false;
        return true;
    }

    // ---- the gate -----------------------------------------------------------------------------

    private static void OpenGate(UIWindow? story, UIWindow? loadout)
    {
        _gateOpen = true;
        _sweeps = 0;
        _nextSweepAt = Time.unscaledTime + SweepIntervalSeconds;
        UIWindow? keep = story ?? loadout;
        _closedAtOpen = ModalFallback.ReleaseFloatsExcept(keep, out string names);
        int caps = SetDestinationsLocked(true, loadout);
        VRLog.Info(Scope, $"POINT OF NO RETURN OPENED — the pre-scenario loadout screen is up "
                          + $"(UILoadoutManager.IsOpen), so the party has committed to a quest. CLOSED "
                          + $"{_closedAtOpen} floated window(s) [{names}] and LOCKED {caps} guildmaster "
                          + $"destination(s) plus the city encounter. USER RULING: \"Zu diesem Zeitpunkt "
                          + "ist der 'Point of Return' schon überschritten, d.h. zB Händler und co. darf "
                          + "man zu diesem Zeitpunkt nicht mehr öffnen können … Alle anderen Fenster "
                          + "sollen dabei dann geschlossen werden.\" THE CLOSE IS A PRESENTATION RELEASE "
                          + "— panel, grab bar, X, collider and arc slot, through the same "
                          + "CanvasConversion.Release the ordinary release loop uses — and writes NO "
                          + "game state and nothing on the wire. THE LOCK IS THE GAME'S OWN: "
                          + "UIGuildmasterButton.ToggleGreyOut(true) flips the same Toggle.interactable "
                          + "the flat client's own quest-select flow flips and stops the same highlight "
                          + "animator, which is why the 3D caps go dead and dark without the cap lane "
                          + "changing a line — MapButtonRail.Pressable already reads "
                          + "Toggle.IsInteractable().");
    }

    private static void CloseGate()
    {
        _gateOpen = false;
        int caps = SetDestinationsLocked(false, null);
        VRLog.Info(Scope, $"POINT OF NO RETURN CLOSED — the loadout screen is gone. UNLOCKED {caps} "
                          + $"guildmaster destination(s) and the city encounter; the {_closedAtOpen} "
                          + "window(s) closed when it opened are NOT re-opened, because the player "
                          + "closed nothing and the game will re-show whatever it still wants. Each "
                          + "destination is restored to the interactable state and highlight state it "
                          + "was MEASURED in before the lock, not to a guessed default.");
        _closedAtOpen = 0;
        _sweeps = 0;
    }

    /// <summary>
    /// Lock or unlock the guildmaster destinations THROUGH THE GAME'S OWN STATE.
    ///
    /// <para><b>WHY THE GAME'S STATE AND NOT A MOD TINT.</b> The user asked for the game's own
    /// disabled presentation — <i>"die buttons sollen das auch mit ihrer spieleigenen animation
    /// anzeigen"</i>. <c>UIGuildmasterButton.ToggleGreyOut(true)</c>
    /// (decompiled/GH.Runtime/UIGuildmasterButton.cs:239-252) is exactly that presentation: it sets
    /// <c>toggle.interactable = false</c>, swaps <c>icon.material</c> for
    /// <c>UIInfoTools.Instance.disabledGrayscaleMaterial</c>, clears <c>hoverMask</c> and calls
    /// <c>Highlight(false)</c>, which stops the button's <c>LoopAnimator</c>.</para>
    ///
    /// <para><b>AND IT REACHES THE 3D CAPS WITH NO CHANGE IN THE CAP LANE'S FILE.</b>
    /// <c>MapButtonRail.Pressable</c> already asks <c>c.Toggle.IsInteractable()</c>, and the cap
    /// already mirrors the game's highlight object. So the cap's collider goes off, its body and
    /// icon take the rail's disabled treatment, and the pulse the game just stopped stops on the cap
    /// too — the game's own animation, shown on the cap, which is what was asked for. The grayscale
    /// MATERIAL does not cross: the cap's icon is a <c>SpriteRenderer</c> with a mod material and the
    /// game's grey is a uGUI <c>Image.material</c>. That is a real difference and it is stated here
    /// rather than papered over.</para>
    ///
    /// <para><b>RESTORE IS MEASURED, NOT ASSUMED.</b> A button that was ALREADY non-interactable
    /// before the lock is not touched and not restored — un-greying it would overturn a decision the
    /// game made. For the ones that are locked, the highlight object's own <c>activeSelf</c> is
    /// recorded and put back, because <c>ToggleGreyOut(false)</c> would otherwise start a pulse on
    /// every destination.</para>
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
        try
        {
            UIGuildmasterHUD? hud = Singleton<UIGuildmasterHUD>.IsInitialized
                ? Singleton<UIGuildmasterHUD>.Instance
                : null;
            if (!locked)
            {
                for (int i = 0; i < Greyed.Count; i++)
                {
                    UIGuildmasterButton b = Greyed[i];
                    if (b == null)
                        continue;
                    b.ToggleGreyOut(greyedOut: false);
                    b.Highlight(i < GreyedHadHighlight.Count && GreyedHadHighlight[i]);
                    changed++;
                }
                Greyed.Clear();
                GreyedHadHighlight.Clear();
                if (hud != null && _cityRequest != null)
                    hud.EnableCityEncounter(_cityRequest, enable: true);
                _cityRequest = null;
                return changed;
            }

            if (hud == null)
                return 0;
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
                    bool hadHighlight = b.highlightAnimator != null
                                        && b.highlightAnimator.gameObject.activeSelf;
                    b.ToggleGreyOut(greyedOut: true);
                    Greyed.Add(b);
                    GreyedHadHighlight.Add(hadHighlight);
                    changed++;
                }
            }
            _cityRequest = loadout != null ? loadout : (Component?)hud;
            if (_cityRequest != null)
                hud.DisableCityEncounter(_cityRequest);
        }
        catch (System.Exception e)
        {
            VRLog.Warn(Scope, $"POINT OF NO RETURN: the guildmaster lock-out threw ({e.GetType().Name}) "
                              + "— the windows are still closed and the gate still stands, but the "
                              + "destination buttons keep whatever state the game had them in. Fail "
                              + "direction: a reachable merchant, never an unreachable map.");
        }
        return changed;
    }

    // ---- the park -----------------------------------------------------------------------------

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
        ApplyPose(win);
    }

    private static bool Park(UIWindow story, RectTransform win, RectTransform image)
    {
        try
        {
            _home = image.parent;
            _homeIndex = image.GetSiblingIndex();
            _homeAnchorMin = image.anchorMin;
            _homeAnchorMax = image.anchorMax;
            _homePivot = image.pivot;
            _homeAnchoredPos = image.anchoredPosition;
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
            image.localRotation = Quaternion.identity;
            image.localScale = Vector3.one;
            _parked = image;
            _parkHost = story;
            _composeLogged = false;
            // The composite has just come into being, so the window that USED to carry the picture
            // (the loadout screen) is still floating a full-window Blur behind it. Arm the sweep for
            // the very next tick instead of making the player look at both for half a second.
            _nextSweepAt = 0f;
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
            image.anchoredPosition = _homeAnchoredPos;
            image.localRotation = _homeRotation;
            image.localScale = _homeScale;
            VRLog.Info(Scope, $"STORY COMPOSITE UNPARKED — {why}. The quest picture is back under "
                              + $"'{_home.name}' at sibling {_homeIndex} with its authored anchors, pivot "
                              + "and offset restored verbatim, so the loadout screen's own paper-expand "
                              + "tween (UILoadoutQuestWindow.FinishIntroduction) runs on the rect it was "
                              + "authored against. The loadout window now floats as an ordinary window "
                              + "with its own brass bar and its own X.");
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
                          + $"FROM THE LOADOUT SCREEN: '{image.name}' {imageSize.x:F0}x{imageSize.y:F0} px, "
                          + "the StoryImageViewer container UILoadoutQuestWindow.imagePaper draws "
                          + "quest.LoadoutImageId into (UILoadoutQuestWindow.cs:52-60). LAYOUT: the picture "
                          + $"is parked under '{win.name}' with pivot (0.5,0) at anchored "
                          + $"({pos.x:F0},{pos.y:F0}) px — its BOTTOM edge {ImageGapPx:F0} px above the "
                          + "measured TOP edge of MapStoryController.dialogBox, i.e. the dialog UNDER the "
                          + $"image, one panel, one grab bar, no X. SHARED: {shared} (kind {kind}) — "
                          + "the bar this panel wears is the one SharedWindows.IsShared decides, and the "
                          + "pose it publishes is record 21 entry kind 1. USER RULING: \"Dialog und Bild "
                          + "soll ein einziges 'blaues' Fenster sein, mit dem Dialog unter dem Bild.\"");
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
        _sweeps = 0;
        _closedAtOpen = 0;
    }
}
