using BepInEx.Configuration;
using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// WHEN A RELEASED WINDOW TURNS TO FACE THE PLAYER ([WorldUI] WindowFacing).
///
/// <para><b>USER REQUEST 8 (2026-08-22, verbatim):</b> "Das automatische Drehen zum Spieler soll
/// einstellbar sein: Default soll sein, dass es nur sich dreht, wenn es mit dem Laser gegriffen
/// wurde, beim Greifen nicht. Aber beides oder gar nicht soll auch eine mögliche Einstellung sein.
/// Das gilt wie gesagt nur für die lokalen Fenster, Remote-Fenster (blau) sollen das gar nicht
/// haben."</para>
///
/// <para><b>WHY THE MODALITY IS THE RIGHT AXIS.</b> A LASER carry translates only — the window
/// slides along the aim ray and the owner never authors its rotation (PanelGrabHandle's laser-carry
/// branch returns before the rotation writers), so the window arrives at the new place still facing
/// wherever it used to. Without the release snap a laser-dragged window is read edge-on, which is
/// what the snap was added for. A HAND carry is the opposite: the Level carry yaws the window with
/// the wrist for the whole drag, so the player has already AIMED it, and re-deriving the yaw on
/// release throws their aim away. One mechanism, two opposite meanings — which is exactly why the
/// dial is about how you grabbed it and not about how far away it was.</para>
///
/// <para><b>THIS IS A ONE-SHOT ON RELEASE, IN EVERY MODE — never a follow.</b> The standing project
/// rule that nothing re-orients with head movement is untouched: no mode here makes a window watch
/// the player, and <see cref="Always"/> only means "the release snap also applies to a hand grab".
/// The legacy per-frame billboard is a different, long-defaulted-off dial
/// (<c>[WorldUI] PanelsFollowView</c>) and is not related to this one.</para>
///
/// <para><b>SHARED (blue-barred) WINDOWS ARE OUTSIDE ALL THREE MODES</b> and never turn, whatever
/// this says — see <see cref="SharedWindows.IsShared"/> for that rule and why it is not
/// configurable.</para>
///
/// <para>The member ORDER is the settings-dropdown index map (LaserOnly=0/Always=1/Never=2) — the
/// preset row casts the dropdown index straight to this enum, exactly like
/// <c>Cards.BoardMoveMode</c>. Values are config-file identity only; nothing goes over the
/// wire.</para>
/// </summary>
internal enum WindowFaceMode
{
    /// <summary>DEFAULT (the user's own): a window turns to face the player when it was grabbed
    /// with the LASER, and keeps the orientation the wrist gave it when it was grabbed by hand.
    /// </summary>
    LaserOnly = 0,

    /// <summary>Either way: every release re-derives the facing (the behaviour every floated window
    /// had before this dial existed).</summary>
    Always = 1,

    /// <summary>Never: a released window keeps exactly the orientation it was let go at.</summary>
    Never = 2,
}

/// <summary>
/// Phase-3c configuration, bound into the module's own file. P5 (MISSION A.9): now
/// created through the canonical <see cref="ModuleConfig.Create"/> helper —
/// <c>BepInEx/config/dev.gloomhavenvr.worldui.cfg</c> (renamed from the pre-P5
/// <c>worldui.gloomhavenvr.cfg</c>). Optional surfaces are individually toggleable
/// (ARCHITECTURE §7); the essential shell and the deadlock-insurance paths are
/// unconditional (user ruling 2026-08-11). Entries are read live, so flips take
/// effect on the next relevant rebuild.
/// </summary>
internal static class WorldUIConfig
{
    private static ConfigFile? _file;

    // [WorldUI] Master is GONE (user ruling 2026-08-11): off left the HMD with only void, hands
    // and lasers (test #11: an accidentally persisted Master=false read as "menu no longer
    // loads") — not a minimal mode but a wholesale brick, so the shell is unconditional now.
    // Same ruling, same day, for the other former kill switches: FlatScreen, FlatScreenAutoShow,
    // ManualScreenChord, CatchAllModals, MenuPopupFloat, UseBars, DoomPicker, DistributePanel —
    // each OFF path ended in an unreachable modal or a rule-engine deadlock. None is re-bound,
    // so stale cfg keys are dropped on the next save.
    //
    // ROUND 2 OF THE SAME THEME — user ruling 2026-08-13, verbatim:
    //   "Entferne weiterhin die Optionen die den Spielfluss in VR beschädigen können. Die
    //    Einstellungen sollen nur Optionale Inhalte einstellbar machen. Das ist die Runde 2 in
    //    der Thematik. So zB: Die Initativreihenfolge ausschalten zu können am Controllboard
    //    macht keinen Sinn."
    // Round 1 kept "all informational panel toggles (alternatives exist)". His example overrules
    // exactly that: a panel toggle whose OFF releases the panel back to its 2D home makes the
    // readout UNREACHABLE in VR, because the 2D home is only visible behind the manual A/X
    // rescue screen. THE TEST THAT DECIDED EACH ONE: does the flat game let you switch this
    // display off? If it does not, the mod must not invent a way to lose it. Gone here, and
    // unconditional from now on: InitiativeTrack, ElementBoard, Objectives, StatPanels,
    // PropInfoCards, EnemyReveal, ActorBars, Tooltips, ActionElementHints (readouts the flat
    // game always shows), ClickLatch + ClickMode + ForceMouseMode (their off/non-default states
    // are the documented no-click failure modes), [Keyboard] Enabled (off = a text field with no
    // way to type in a headset), DemoteOverlaySolidClears (off = the campaign map can be wiped
    // to black). Kept on purpose: WristHud, CombatLog, LoadingIndicator (mod-invented extras the
    // flat game has no equivalent of), Dialogs / DecisionDock / ModalStyle /
    // TrayNativeControls / ScreenLayerSplit (both states present the SAME content, a real
    // fallback path each). None of the removed keys is re-bound: a stale cfg line is an inert
    // BepInEx orphan and is dropped on the next save, so it can never resurrect the behaviour.
    //
    // AND [WorldUI] ButtonCluster IS GONE TOO (2026-08-25), for a different reason than any of
    // the above: not a ruling about which toggles are legitimate, but the disappearance of the
    // thing it toggled. It gated the turn-flow SKIP cap group, whose only live member is a
    // generic board keycap on the board's own third recess now (user: "Ich möchte daher, dass die
    // Button-Gruppe der 'Überspringen Buttons' komplett verschwindet"). It follows the board like
    // its two siblings and has no switch of its own, exactly as Confirm and Undo never had one.
    //
    // AND [WorldUI] CombatLogUserClosed IS GONE (2026-09-05), for a third reason again: it was
    // never a setting, it was PERSISTED RUNTIME STATE, and it outlived the only writer that could
    // clear it. Commit 9a6db78c deleted the old settings panel and with it the single
    // `SetUserVisible(v, "settings")` call; the Defaults refactor then shipped the key defaulting
    // to TRUE (the tester's live value at the time) where the original bind said false. The two
    // together made `CombatLog && !CombatLogUserClosed` false forever, which is the user's report
    // "auch wenn ich ihn in den Einstellungen einschalte" exactly. There is no persisted latch any
    // more: visibility is session state owned by CombatLogSurface, and the worst a wrong session
    // state can cost is one press of 'Kampflog jetzt einblenden'. A stale cfg line is an inert
    // BepInEx orphan and drops on the next save, so the old value cannot resurrect the deadlock.

    /// <summary>The module's config file (for late binders like FlatScreenStereo). Valid after <see cref="Bind"/>.</summary>
    internal static ConfigFile FileHandle => _file!;

    // ---- surfaces (each individually toggleable) ---------------------------------------
    /// <summary>START-UP PREFERENCE: should the combat log panel be up when a scenario begins?
    /// (Bound key still "CombatLog" so an existing cfg value survives — see Defaults.)</summary>
    internal static ConfigEntry<bool> CombatLogAtStart = null!;
    /// <summary>Local town visits use immersive stations; off retains the original service windows.</summary>
    internal static ConfigEntry<bool> ImmersiveTownServices = null!;
    internal static ConfigEntry<bool> Dialogs = null!;
    internal static ConfigEntry<bool> DecisionDock = null!;
    internal static ConfigEntry<bool> TrayNativeControls = null!;

    /// <summary>Actor bars keep a fixed board-space size (no distance growth) — test #14 item 4.</summary>
    internal static ConfigEntry<bool> BarFixedSize = null!;

    /// <summary>
    /// User ("Größe der Healthbars sollen einstellbar sein"): the actor HP/effect bars' SIZE, as a
    /// factor of the size they have always shipped at.
    ///
    /// <para>THE UNIT IS REAL MILLIMETRES AT THE EYE, not world units. A bar is a readability
    /// overlay, and the only statement about "how big is it" that survives a pinch-zoom is its size
    /// at the eye: the diorama scale is a scale on the RIG, so a world-unit size means a different
    /// apparent size at every zoom while the same real-millimetre size looks identical at all of
    /// them. 1.0 = the shipped size, which is <see cref="CanvasScaleMm"/> × 0.35 mm per uGUI pixel
    /// at the eye (see <see cref="WorldUI.ActorBars"/>); 2.0 is a bar twice as tall and twice as
    /// wide in front of your face at any table zoom.</para>
    ///
    /// <para>THE ONLY BAR-SIZE DIAL THERE IS. The zoom band the bars are allowed to move inside is
    /// a CONSTANT now (<c>ActorBars.ZoomFollowMin/Max</c>, still 0.7–1.5), not two more sliders —
    /// see the tombstone at the bind site.</para>
    /// </summary>
    internal static ConfigEntry<float> BarSizeScale = null!;

    // [WorldUI] BarZoomMinScale / BarZoomMaxScale are GONE (user ruling 2026-08-13: "Mindest und
    // Maximalgröße der Lebensbalken haben keinen sehbaren einfluss. Es macht irgendwas, aber man
    // versteht nicht wirklich was - ziemlich unintuitiv."). They clamped an INTERMEDIATE quantity —
    // the table-zoom FOLLOW factor, which is 1.0 at the shipped zoom by construction — so at the
    // zoom the player actually sits at, neither bound was reachable and moving either dial changed
    // nothing at all. The GUARANTEE he originally asked for ("ein minimum und maximum der Größe,
    // damit sie sich trotz zoomen nie über die Grenzen hinaus skalieren können") is not lost: it is
    // now the fixed 0.7–1.5 band in ActorBars, applied unconditionally on both size paths. What is
    // gone is the ability to type numbers into a factor whose effect no player can see.
    internal static ConfigEntry<bool> WristHud = null!;

    /// <summary>
    /// THE MAP ROOM'S LOADOUT HAND (user feature 2026-08-21): while the 3D world map stands, the
    /// scenario loadout of the character selected in the party display is fanned out on the
    /// fan-carrying hand, with that character's info on a wrist plate — and either hand can lift a
    /// card out to read it.
    ///
    /// <para>DEFAULT ON (his ruling 4: "Das Feature soll deaktivierbar sein"). Its OFF makes only
    /// OPTIONAL content optional — nothing is built, so the map room is byte-identical to a build
    /// without the feature. It cannot change game state in either position: the lifted card is a
    /// mod-made copy no game system owns, and the loadout list is only ever READ. See
    /// <c>WorldUI/MapRoom/MapRoomHand.2.Fan.cs</c> for the exhaustive inspection-only argument.</para>
    /// </summary>
    internal static ConfigEntry<bool> MapRoomHand = null!;

    // ---- the map room's travel-confirm button: WHERE IT SITS IN THE QUEST WINDOW ---------------
    //
    // User ruling 2026-08-21, verbatim: "1) Mach die Position des Quest Buttons ganz rückgängig wie
    // es das erste mal war als du den button im window hinzugefügt hast. Geb mir dann im debug menu
    // die offsets um ihm zu verschieben - ich stell es selber ein."
    //
    // THREE SOLVED PLACEMENTS WERE REJECTED IN A ROW (ModBuild 191, 192 and 193 — see the header of
    // WorldUI/MapRoom/MapTravelConfirm.cs), so the mod stopped guessing: the shipped pose is the
    // ModBuild 190 one, which both defaults of 0 reproduce EXACTLY, and these two dials are how the
    // player moves it from there.
    //
    // THE UNIT IS FRACTIONS OF THE QUEST WINDOW'S OWN HEIGHT — spelled out in the key names, because
    // a placement number whose unit is a guess is the mixed-units bug this project has already
    // shipped. A fraction and not a pixel count because [WorldUI] WindowLegibility resizes the
    // floated windows: a pixel offset tuned at one window size is wrong at the next, a fraction keeps
    // the button in the same place ON THE CARD at every size. HEIGHT for both axes and not width for
    // x, so that the same number means the same real distance on either dial.
    //
    // A SETTING MAY ONLY MAKE OPTIONAL CONTENT OPTIONAL: these are PLACEMENT dials for content that
    // is always present. No value of either removes the confirm button, changes what it does or
    // touches game state, and neither clamp can put the button out of reach — the extremes trace the
    // window's own outline grown by half a window height (~447 mm at the measured rig scale), and the
    // button is a CHILD of the window, so it always travels, scales and occludes with the card the
    // player is already looking at. Presentation only, per client, nothing on the wire.

    // ModBuild 196 CORRECTED THE FRAME BOTH DIALS ARE MEASURED IN, and the correction is worth
    // stating once here rather than twice below. ModBuild 190 PARKED the container on the window's
    // bottom-centre anchor, and every text since — these summaries, the two ConfigDescriptions and
    // the German ones — described that frame. It was never the frame the player saw: the quest
    // window carries a uGUI LayoutGroup, and its rebuild re-drove the container to Vector2.up
    // (top-left) on every frame it ran. Twelve of fifteen sampled placements in the ModBuild 195
    // hardware log solve for the TOP-LEFT reference, three for the bottom-centre one — and the three
    // are each the first tick of a parking, i.e. the one frame before the group took it back.
    // That is also what settles the contradiction ModBuild 195 could not: the photograph and the
    // runtime numbers disagreed because they were sampled in the two different anchor states.
    // The dials now place the container's PIVOT against the window rect's top-left corner and derive
    // anchoredPosition from whatever anchor the rect carries, so there is no shared value left for a
    // second writer to alternate over. The USER-FACING MEANING OF 0/0 IS UNCHANGED — it is still the
    // pose ModBuild 190 put on screen, so a tuned cfg keeps meaning the same place.

    /// <summary>Sideways offset of the map room's travel-confirm button inside the floated quest
    /// window, in fractions of that window's HEIGHT (+ = right), measured from the window rect's
    /// LEFT edge, where 0 — the ModBuild 190 pose — sits. The card is half a window height wide, so
    /// its centre line is +0.25 and its right edge +0.50. Clamped by
    /// <see cref="MapRoom.MapTravelConfirm.OffsetLimitX"/>; read live.</summary>
    internal static ConfigEntry<float> TravelButtonOffsetXWindowHeights = null!;

    /// <summary>Vertical offset of the same button, in fractions of the window's HEIGHT, measured UP
    /// from the window rect's TOP edge (which is where 0 — the ModBuild 190 pose — sits; −1.0 is the
    /// window's BOTTOM edge). The value that puts the button under the quest information is
    /// therefore NEGATIVE. Clamped by <see cref="MapRoom.MapTravelConfirm.OffsetLimitYMin"/> /
    /// <see cref="MapRoom.MapTravelConfirm.OffsetLimitYMax"/>; read live.</summary>
    internal static ConfigEntry<float> TravelButtonOffsetYWindowHeights = null!;

    /// <summary>Aliasing follow-up to [Cards] FaceMipBake: mip-bake the mipless game textures the
    /// initiative track and the hover-hint tooltip sample on their world-space hosts (see
    /// <see cref="PanelMipBake"/>). Mirrors the cards' kill switch; read live.</summary>
    internal static ConfigEntry<bool> PanelMipBake = null!;

    /// <summary>
    /// ROUND 10 of the floated-window flicker: render a floated window's canvas into its own
    /// RenderTexture at (or above) its AUTHORED resolution, with MSAA and a mip chain, and show that
    /// one filtered texture on the panel instead of rasterizing 475 uGUI graphics straight into the
    /// eye at ~0.54 rendered pixels per authored pixel. See <c>WorldUI/PanelSupersample.1.Core.cs</c>
    /// for why the mip bake of ModBuild 189/190 could never reach this half of the defect. Default
    /// OFF so it can be A/B'd against today's rendering in one session; read live.
    /// </summary>
    internal static ConfigEntry<bool> PanelSupersample = null!;

    /// <summary>RT pixels per authored uGUI pixel for <see cref="PanelSupersample"/> — the
    /// sharpness/VRAM trade. Clamped 0.5-2.</summary>
    internal static ConfigEntry<float> PanelSupersampleFactor = null!;

    /// <summary>
    /// Mip LOD offset applied to <see cref="PanelSupersample"/>'s DISPLAY target — the sharpness /
    /// aliasing trade, and the only lever left on a MINIFIED window (the factor buys texels per
    /// AUTHORED pixel, which a minified window's sampler never selects). Clamped -2..0; read live,
    /// re-asserted on every resolve, and the value actually in force is read back off the live
    /// RenderTexture in <c>PanelSupersample.2.Capture.cs</c>'s own report line.
    /// </summary>
    internal static ConfigEntry<float> PanelMipLodOffset = null!;


    /// <summary>Drop the material of any GrabPass-shader graphic inside a floated window, exactly as
    /// the game's own UIBlurDisabler does in SimplifiedUI mode. See the ModBuild 217 block in
    /// <c>PanelSupersample.NoteDrawState</c>.</summary>
    internal static ConfigEntry<bool> NeutraliseGrabPassBlur = null!;


    // ---- behavior ----------------------------------------------------------------------
    // [WorldUI] ForceMouseMode is GONE (user ruling 2026-08-13): mouse mode is now pinned
    // unconditionally while VR runs. Its OFF let the game load the 'Game_gamepad' scene variants
    // and put every button behind a gamepad long-press — an input the VR player has no device
    // for. See InputModeGuard.
    /// <summary>World-canvas scale in millimeters per uGUI pixel (at diorama scale 1).</summary>
    internal static ConfigEntry<float> CanvasScaleMm = null!;

    /// <summary>Initiative track 3D depth effect: max total front-to-back z spread (px), live-tunable.</summary>
    internal static ConfigEntry<float> InitiativeDepthMaxSpreadPx = null!;


    /// <summary>
    /// User ("Ich will die Größe der Infotafeln, die beim Mouseover erscheinen, einstellen
    /// können"): world SIZE factor of the hover INFO panels — the board-hover cards the game
    /// raises through <c>UITextInfoPanel</c> ("2 Gold", "Geschlossene Tür", chest, obstacle,
    /// pressure plate …) and <c>UIPropInfoPanel</c> (trap / quest item), plus the card-action
    /// element hint on the tooltip canvas. Live-tunable in the debug menu (Anzeige →
    /// Infotafel-Größe); read on every placement tick, so the panel resizes immediately /
    /// at the latest on the next hover. Default <see cref="DefaultHoverInfoScale"/> = the
    /// pre-existing hard-coded factor, so nothing changes until the dial is touched.
    /// </summary>
    internal static ConfigEntry<float> HoverInfoScale = null!;

    /// <summary>
    /// The hover-info size factor that was HARD-CODED before the dial existed
    /// (<c>PropInfoSurface.PlaceWatch</c>: <c>PanelLayout.WorldScale * 0.6f</c>). It is the
    /// entry's default AND the normalization base for the tooltip canvas in
    /// <see cref="WorldTooltips"/>, so ONE dial scales every mouseover info panel and the
    /// factory value reproduces today's sizes EXACTLY (0.6 / 0.6 = 1x on the tooltip path).
    /// </summary>
    internal const float DefaultHoverInfoScale = Defaults.HoverInfoScale;

    /// <summary>Last hover-info factor we logged (change-dedup for the hardware log).</summary>
    private static float _loggedHoverInfoScale = float.NaN;

    /// <summary>
    /// Read the live hover-info size factor AND log it once per CHANGE. Both consumers
    /// (<see cref="Surfaces.PropInfoSurface"/> and <see cref="WorldTooltips"/>) read through
    /// here every placement tick, so the log proves in the hardware log WHICH size was actually
    /// applied to a shown panel — a value written into the cfg but never applied (panel not
    /// converted, hints toggled off) produces no line. Deduped because the callers run per frame.
    /// </summary>
    internal static float HoverInfoScaleLive()
    {
        float v = HoverInfoScale.Value;
        if (float.IsNaN(_loggedHoverInfoScale) || System.Math.Abs(v - _loggedHoverInfoScale) > 0.001f)
        {
            _loggedHoverInfoScale = v;
            VRLog.Info("WorldUI",
                $"Hover info panel size applied: {v:0.00}x (default {DefaultHoverInfoScale:0.00}x) — " +
                "mouseover info panels (TextInfoPanel/PropInfoPanel) and the card element hint.");
        }
        return v;
    }

    /// <summary>
    /// User (hardware, "Die Gegnerinfo spawnt meist genau hinter dem Controllboard"): how far the
    /// enemy round reveal must clear the CONTROL BOARD's top edge, in real metres measured AT THE
    /// BOARD (the sight-line gap the player sees between the board's top edge and the bottom of
    /// the reveal). Live-tunable in the debug menu (Panels -> Initiative); read on every reveal
    /// spawn/follow by <see cref="Surfaces.EnemyRevealSurface"/>.
    /// </summary>
    internal static ConfigEntry<float> EnemyRevealBoardClearance = null!;

    /// <summary>
    /// THE RADIUS OF THE HALF-RING THE SHARED ("blue") MAP-ROOM WINDOWS SPAWN ON, real metres,
    /// measured from the MAP TABLE'S OWN CENTRE. User ruling 2026-08-24, verbatim: "Es ist ok das
    /// es nicht bei jedem nah steht. Ich möchte aber, das die Fenster zentral ÜBER dem Tisch mittig
    /// spawnen dort in einem halbkreis."
    ///
    /// <para><b>BOUND BUT INERT since ModBuild 480</b> (<c>b27bbb9e</c>, review R2 F8). The ring's
    /// radius is <c>Defaults.SharedWindowArcRadiusMeters</c>, read by
    /// <c>ArcSeats.TrySharedAnchorOnTable</c> as a shipped constant; this entry has no consumer
    /// beyond the spawn line, which prints the dialled value beside the constant whenever they
    /// differ. It used to be read once per shared window at its spawn placement — and that read was
    /// the one term that let two differently-tuned clients seat the same blue window at two depths
    /// for a whole session, because record 21 publishes a pose only after somebody has dragged the
    /// window. A shared window's pose may not be a function of anything client-local (the standing
    /// ruling), so the dial stays bound (it is a player's persisted value) and stops being read.
    /// If the ring has to grow, the constant moves, on every client at once.</para>
    ///
    /// <para>The default 0.80 m is the parchment's own circumradius (0.768 m on the surveyed
    /// 0.96 x 1.20 m map) rounded up: at or above it no point of the ring can stand over the map,
    /// so no shared window can hide the thing the player is clicking. Below it, wide windows are
    /// pushed off the arc by the map-occlusion floor instead. Live-tunable in the debug menu
    /// (Panels ▸ Shared — <c>[WorldUI]</c> maps to <see cref="ConfigCatalog.ConfigTopic.Panels"/> and the group is
    /// the key's own leading word).</para>
    /// </summary>
    internal static ConfigEntry<float> SharedWindowArcRadiusMeters = null!;

    /// <summary>
    /// ModBuild 251 — THE ONE SPAWN HEIGHT FOR EVERY MAP-ROOM WINDOW, measured AT THE GRAB BAR, in
    /// real metres above the map table's top surface. User ruling, verbatim: "die Höhe soll beim
    /// Spawn am Besten bei allen Fenster gleich sein gemessen am Greifbalken!"
    ///
    /// <para>It seats BOTH map-room families — the shared (blue-barred) windows on the ModBuild 250
    /// half-ring and the local windows on their arc seats — so the room has one bar line instead of
    /// two rules that never agreed (0.092 m against 0.52–0.68 m in the ModBuild 250 log). The
    /// window's body hangs from the bar, so a taller window reaches higher; a window tall enough to
    /// put its top edge over <c>MapRoomWindowTopCeilingMeters</c> is lowered and SAYS SO by name on
    /// the <c>MAP ROOM WINDOW BAR HEIGHT</c> line rather than silently.</para>
    ///
    /// <para>THE RANGE STARTS AT 0.05 m FOR A REASON: that is the same floor
    /// <c>ArcSeats.MapRoomWindowMinBarHeightMeters</c> enforces in code, so no value a player can
    /// type here can put a window's drawn body into the table (the ModBuild 243 defect,
    /// <c>multiplayer_fesnter_position.jpg</c>). The code floor is the guarantee; this range is the
    /// courtesy.</para>
    ///
    /// <para>MULTIPLAYER, STATED: for a SHARED window this number is part of a pose that is
    /// otherwise 1:1 by construction, so it is the one term two clients can disagree about — the
    /// same divergence surface <see cref="SharedWindowArcRadiusMeters"/> has. Different values seat
    /// a shared window at different heights until somebody drags it; the falsifier line prints the
    /// resolved number on every placement.</para>
    /// </summary>
    internal static ConfigEntry<float> MapRoomWindowBarHeightMeters = null!;

    /// <summary>
    /// ModBuild 290 — THE SCENARIO'S ANSWER TO THE SAME QUESTION, measured the same way: how far
    /// above the PLAY FIELD's surface a shared (blue-barred) scenario window's GRAB BAR is hung when
    /// it opens, in real metres. User report, verbatim (spawn_scenario.jpg): "Das 'blaue'
    /// Multiplayer Fenster ist IN dem Spielfeld gespawned … es muss viel höher spawnen damit es über
    /// dem Spielfeld schwebt."
    ///
    /// <para>"The play field's surface" is the ORBIT-FOCUS PLANE (<c>CameraController.FocusPoint</c>
    /// — the plane the hexes lie in), which is the only thing this mod has ever used to know where
    /// the scenario board is; the board's own furniture stands about
    /// <c>ModalFallback.BoardTopClearanceMeters</c> = 0.30 m above it, so
    /// <c>ArcSeats.ScenarioWindowMinBarHeightMeters</c> floors this dial there in code and no value
    /// typed here can put a bar into the scenery.</para>
    ///
    /// <para>MULTIPLAYER, STATED: for a SHARED window this number is part of a spawn pose that is
    /// otherwise expressed in the same seat-anchor frame and the same real metres record 19 already
    /// carries, so it is the one term two clients can disagree about — the same divergence surface
    /// <see cref="MapRoomWindowBarHeightMeters"/> has. Different values seat the window at different
    /// heights until somebody drags it; the SHARED WINDOW ANCHOR line prints the resolved number and
    /// the resulting bar height on every placement.</para>
    /// </summary>
    internal static ConfigEntry<float> ScenarioWindowBoardClearanceMeters = null!;

    /// <summary>Item 9: flat monitor mirrors ONLY the HMD left eye (no 2D-menu composite).</summary>
    internal static ConfigEntry<bool> DesktopMirrorLeftEye = null!;

    /// <summary>Show the intro (pre-menu scenes) on the floating screen in VR too.</summary>
    internal static ConfigEntry<bool> ShowIntro = null!;

    /// <summary>Flat-screen width in real-world meters (test #6: 2.2 m default).</summary>
    internal static ConfigEntry<float> ScreenWidth = null!;

    /// <summary>Flat-screen distance from the head in real-world meters.</summary>
    internal static ConfigEntry<float> ScreenDistance = null!;

    // [WorldUI] ClickLatch is GONE (user ruling 2026-08-13): the press-to-release position freeze
    // is unconditional. Its OFF is the documented no-click failure mode — sub-degree hand tremor
    // moves the projected pixel dozens of px between press and release, so uGUI sees a drag and
    // NOTHING in the flat menus (main menu included) can be clicked. See FlatScreen.6.Pointer.

    /// <summary>
    /// Disable the physical desktop mouse while VR runs so only the VR laser drives the pointer.
    /// ALWAYS ON, and no longer a dial.
    ///
    /// <para>2026-08-22 settings audit (user, verbatim): <i>"a) Lösche alle Einstellungen die das
    /// Spiel breaken könnten wenn die verändert werden. Etwas was das spiel kaputt macht wenn man
    /// es umstellt ist nicht optional und sollte daher nicht einstellbar sein."</i> The harm, and
    /// the value that causes it: <c>false</c> lets the STALE desktop mouse position keep hovering
    /// and selecting map and menu elements behind the player's back — clicks nobody made, on
    /// elements nobody can see, in a headset where there is no way to notice the cause. There is
    /// no reading of that a player is choosing between; the mouse is re-enabled when VR stops
    /// either way, which is the only behaviour the off state was ever protecting.</para>
    /// </summary>
    internal const bool SuppressPhysicalMouse = true;

    /// <summary>Opacity multiplier for the campaign map's Wind/Clouds ambiance particles (0 = invisible, 1 = full).</summary>
    internal static ConfigEntry<float> MapWindOpacity = null!;

    /// <summary>Ray movement (degrees off the press direction) that opens the click latch into a drag.</summary>
    internal static ConfigEntry<float> DragUnlockDegrees = null!;

    /// <summary>The ray must stay beyond <see cref="DragUnlockDegrees"/> this long before the latch opens.</summary>
    internal static ConfigEntry<float> DragUnlockSeconds = null!;

    /// <summary>Fingertip poke on the flat screen clicks at the poked position.</summary>
    internal static ConfigEntry<bool> PokeClick = null!;

    /// <summary>
    /// User #12: push-in confirmation depth (mm) for fingertip pokes on converted flat
    /// uGUI buttons — plane contact only ARMS (pointerDown, pressed visual); the click
    /// fires once the fingertip pushed this far THROUGH the plane. 0 = legacy instant
    /// click on contact. Read live per tick by <c>Hands.Interact.PokeInteractor</c>.
    /// </summary>
    internal static ConfigEntry<float> PokePressDepthMm = null!;

    /// <summary>
    /// User #13b: decision-dock buttons use the DELIBERATE v1 poke press — contact only
    /// arms (pressed visual), the click fires on the conscious WITHDRAWAL back out of
    /// the plane, sweep-throughs cancel silently. Read live per press by
    /// <c>Hands.Interact.PokeInteractor</c> for canvases the decision dock registered
    /// in <c>Hands.Interact.DeliberatePokeSurfaces</c>; every other surface keeps the
    /// <see cref="PokePressDepthMm"/> push-in behaviour.
    /// </summary>
    internal static ConfigEntry<bool> DecisionPokeDeliberate = null!;

    /// <summary>
    /// When a released floated window snaps round to face the player (user request 8) — see
    /// <see cref="WindowFaceMode"/> for the request verbatim and the whole argument. Read LIVE at
    /// each release by <see cref="GrabbableModal"/>, so a change applies to the very next one.
    /// </summary>
    internal static ConfigEntry<WindowFaceMode> WindowFacing = null!;

    /// <summary>
    /// How long a window's grab bar takes to grow, shrink or slide to a new place (user, 2026-09-03:
    /// "es ploppt"). Read LIVE by <see cref="GrabBarTween"/> on every leg, so a change applies to the
    /// next transition. 0 = instant, which is exactly the pop that was reported.
    /// </summary>
    internal static ConfigEntry<float> GrabBarTweenMs = null!;

    // [WorldUI] ClickMode is GONE (user ruling 2026-08-13): clicks are delivered through uGUI
    // ExecuteEvents, always. Of its three values only the default 'execute' ever worked —
    // 'virtualmouse' was disproven on hardware (test #7: the virtual-mouse BUTTON edges do not
    // survive the input module, so no click and no slider drag ever landed) and 'both' is
    // documented as double-firing, "diagnostic use only". A dial whose two non-default values
    // break clicking is not a choice. The virtual mouse keeps carrying POSITION (hover) — only
    // its press/release path is gone with the dial.

    /// <summary>Legacy: world panels re-orient with the rig yaw per frame (HUD-like). Default false (P6).</summary>
    internal static ConfigEntry<bool> PanelsFollowView = null!;

    /// <summary>Hover hex-hint (prop-info) panels drift to the center of view with lazy damped motion while shown.</summary>
    internal static ConfigEntry<bool> HexHintFollowView = null!;

    /// <summary>Reading distance of a hover hint in front of the head, real metres (× diorama scale).</summary>
    internal static ConfigEntry<float> HexHintDistance = null!;

    /// <summary>How far BELOW the gaze centre a hover hint sits, real metres (× diorama scale).</summary>
    internal static ConfigEntry<float> HexHintDrop = null!;

    /// <summary>Lateral offset of a hover hint from the gaze centre, real metres (positive = right).</summary>
    internal static ConfigEntry<float> HexHintSide = null!;

    // ---- combat log panel layout (test #19: movable/scalable/pinnable like the tray) -----
    /// <summary>Combat log anchor mode: true = re-derive from the seat anchor, false = pinned in the world.</summary>
    internal static ConfigEntry<bool> CombatLogFollow = null!;

    /// <summary>Combat log offset from the table anchor along the seat forward, real meters.</summary>
    internal static ConfigEntry<float> CombatLogForward = null!;

    /// <summary>Combat log offset to the seat right, real meters.</summary>
    internal static ConfigEntry<float> CombatLogRight = null!;

    /// <summary>Combat log height above the table plane, real meters.</summary>
    internal static ConfigEntry<float> CombatLogUp = null!;

    /// <summary>Combat log size multiplier (two-hand resize; clamped 0.5–2).</summary>
    internal static ConfigEntry<float> CombatLogScale = null!;

    /// <summary>Modal fallback style: "window" (float only the dialog window) | "screen" (full flat screen).</summary>
    internal static ConfigEntry<string> ModalStyle = null!;

    /// <summary>Hold duration (seconds) for the always-on manual screen chord (the self-rescue:
    /// hold the non-dominant A/X in a scenario to toggle the flat screen).</summary>
    internal static ConfigEntry<float> ManualScreenChordSeconds = null!;

    // [WorldUI] DemoteOverlaySolidClears is GONE (user ruling 2026-08-13): the demotion is
    // unconditional. Its OFF let a non-base captured camera's fullscreen SolidColor clear (the
    // campaign map's depth-5 'Video Camera') wipe the composited map/UI to BLACK — i.e. the
    // campaign map, which is the only way to reach a scenario, could be switched to a black
    // rectangle from the options menu. See FlatScreen.2.CameraStack.

    /// <summary>
    /// Split the flat screen into a UI glass layer over a stereo background layer (test #18).
    /// ALWAYS ON, and no longer a dial.
    ///
    /// <para>2026-08-22 settings audit (user, verbatim): <i>"a) Lösche alle Einstellungen die das
    /// Spiel breaken könnten wenn die verändert werden."</i> The harm, and the value that causes
    /// it: <c>false</c> restores the state the two-layer split was built to end — the game's UI
    /// cameras render Screen-Space-Camera canvases only through their own assigned camera, so no
    /// stereo mirror camera can reproduce them and THE MENU GOES ONE-EYED. A one-eyed main menu is
    /// a broken game, and the note two hundred lines above ("both states present the SAME content,
    /// a real fallback path each") was the argument for keeping the key — it is wrong about this
    /// one: the two states do not present the same content, one of them presents it to one eye.
    /// The genuine fallback survives untouched and is not this switch: the split still falls back
    /// to the single mono RT on any failure at run time (<c>_splitFailed</c> / <c>_splitNoUi</c> in
    /// FlatScreen.2.CameraStack), which is a MEASURED fallback rather than a guessed one.</para>
    /// </summary>
    internal const bool ScreenLayerSplit = true;

    /// <summary>VR loading indicator: the game's rotating spinner in front of the HMD during loads,
    /// flat screen suppressed, background loading priority lowered (smaller hitches).</summary>
    internal static ConfigEntry<bool> LoadingIndicator = null!;

    /// <summary>True when fallback windows float individually ([WorldUI] ModalStyle != "screen").</summary>
    internal static bool ModalWindowStyle =>
        !string.Equals(ModalStyle.Value, "screen", System.StringComparison.OrdinalIgnoreCase);

    // ---- on-screen keyboard --------------------------------------------------------------
    // [Keyboard] Enabled is GONE (user ruling 2026-08-13): the game's own on-screen keyboard
    // comes up for every focused text field, always. Its OFF said "text fields need a real
    // keyboard" — in a headset that is a text field with no way to type into it, and naming a
    // party is a REQUIRED step of starting a campaign. The keyboard only ever appears while a
    // field has focus, so there was never anything to switch off but the ability to proceed.
    // [Keyboard] AutoCapitalise survives: it only changes what the typed letters look like.

    /// <summary>Capitalise the first letter of each word (the game's keyboard has no shift key).</summary>
    internal static ConfigEntry<bool> KeyboardAutoCase = null!;

    // ---- dev ---------------------------------------------------------------------------
    /// <summary>Spawn the world-panel layout with dummy content on the desktop.</summary>
    internal static ConfigEntry<bool> DevShowAllPanels = null!;

    /// <summary>Apply the real canvas conversions in dev mode without an HMD.</summary>
    internal static ConfigEntry<bool> DevForceConvert = null!;

    internal static void Bind()
    {
        if (_file != null)
            return;

        _file = ModuleConfig.Create("worldui");

        // InitiativeTrack / ElementBoard / Objectives / StatPanels / PropInfoCards / EnemyReveal:
        // always on — user ruling 2026-08-13 (his named example is the initiative order). Each
        // OFF released the panel back to its 2D home, which in VR lives on the hidden flat stack:
        // the turn order, the element infusions, the scenario goal, the monster stat block, the
        // hover info for doors/chests/traps and the round's monster ability cards were all
        // switchable into invisibility. See the tombstone at the top of this file.
        // THE KEY IS THE SAME AND ITS MEANING IS NARROWER (user, 2026-09-05). It used to be the
        // feature master and half of a two-term visibility gate whose other half latched shut for
        // good; it is now the START-UP PREFERENCE and nothing else, so it can no longer be the
        // reason a log is missing mid-scenario. Showing and hiding during play are the options
        // button 'Kampflog jetzt einblenden' and the panel's own X, and neither writes this key.
        CombatLogAtStart = _file.Bind("WorldUI", "CombatLog", Defaults.CombatLogAtStart,
            "Spawn the combat log panel when a scenario begins. This is a PREFERENCE about the " +
            "START of a scenario, not a master switch: whatever it says, the VR options row " +
            "'Kampflog jetzt einblenden' brings the panel up at any time, and the panel's own X " +
            "closes it again. Off = it simply is not there until you ask for it.");
        ImmersiveTownServices = _file.Bind("WorldUI", "ImmersiveTownServices", Defaults.ImmersiveTownServices,
            "Visit the merchant, temple and enchantress as immersive NPC stations with movable " +
            "surfaces and grabbable samples. Off restores the original service windows and their " +
            "controls, including an already open visit. Other players retain their chosen presentation.");
        Dialogs = _file.Bind("WorldUI", "Dialogs", Defaults.Dialogs,
            "Confirmation dialogs as world-space modals in front of the HMD (poke yes/no). " +
            "Off = the generic modal fallback floats the SAME window instead (ModalFallback " +
            "IsFallbackWindow), so the dialog stays answerable either way — this picks the " +
            "presentation, it never hides the prompt.");
        DecisionDock = _file.Bind("WorldUI", "DecisionDock", Defaults.DecisionDock,
            "In-scenario decision/confirmation prompts (take-damage burn choice, the burn-" +
            "confirm 'burn this / choose another card' dialog, and any other prompt in the " +
            "DecisionDock registry) render their REAL game widgets — the actual buttons/" +
            "toggles, so labels/localization/enable-states are native — docked in a reserved " +
            "zone BELOW the two cards on the control board while the prompt is open, instead " +
            "of a floating flat window (test #22, generalizes the test-#21 take-damage dock). " +
            "The card fan stays available for follow-up picks (no ModalUI). Off = the generic " +
            "modal fallback floats the whole window as before. (Renamed from 'TakeDamageBoard'.)");
        // UseBars / DoomPicker / DistributePanel: always on — user ruling 2026-08-11: essential
        // (each OFF path left a rule-engine wait invisible in VR; see the tombstone at the top).
        TrayNativeControls = _file.Bind("WorldUI", "TrayNativeControls", Defaults.TrayNativeControls,
            "Dock the game's REAL Continue/Confirm (ReadyButton), Undo (UndoButton) and short-rest " +
            "(ShortRest) widgets onto the control board — the actual in-game buttons with their native " +
            "sprite, localized label and enable/disable states, reusing the DecisionDock docking " +
            "mechanism — in place of the mod-drawn CONFIRM/UNDO board buttons and short-rest token " +
            "(test #23 item 4). Long rest has no discrete uGUI widget (chosen via the card fan + " +
            "Continue), so it stays a mod token. " +
            "DEFAULT OFF (test #27 item 3): docking the real widgets only WHILE they are game-side " +
            "active, then releasing back to the mod button when they hide, made the board buttons " +
            "visibly FLICKER between the flat in-game widget and the 3D mod button. The mod-drawn " +
            "board buttons now wear the sampled native game skin themselves (NativeButtonSkin, test " +
            "#26), so leaving this OFF gives every board button ONE permanent, uniform game-styled " +
            "look with no swap. On = re-enable the real-widget docking (accepts the flicker).");
        // ActorBars: always on — user ruling 2026-08-13. Its OFF released every adopted HP/effect
        // bar back to the game's screen-projected presentation, which is not visible from inside
        // the HMD: the health of every miniature on the board disappeared. The SIZE dials below
        // stay — how big the bars are is taste, whether they exist is not.
        BarFixedSize = _file.Bind("WorldUI", "BarFixedSize", Defaults.BarFixedSize,
            "Actor HP/effect bars ignore the HEAD DISTANCE — a bar the same size whether you " +
            "lean in or step back (test #14: the old distance compensation grew bars up to 2.5x " +
            "when stepping away, which read as the bars 'growing'). Off = legacy behavior: bars " +
            "gently grow with head distance to stay readable, bounded by the same fixed 0.7-1.5 " +
            "band the table zoom is bounded by. This dial says nothing about the TABLE zoom — " +
            "that is BarSizeScale.");
        BarSizeScale = _file.Bind("WorldUI", "BarSizeScale", Defaults.BarSizeScale,
            new ConfigDescription(
                "SIZE of the actor HP/effect bars above the miniatures, as a factor of the size " +
                "they have always shipped at. The unit behind the factor is REAL MILLIMETRES AT " +
                "THE EYE: 1.0 = CanvasScaleMm x 0.35 mm per uGUI pixel in front of your face, so " +
                "2.0 is a bar twice as tall and twice as wide however the table is zoomed. That " +
                "is the only size statement that survives a pinch-zoom, because the mod's zoom is " +
                "a scale on the RIG - a size expressed in world units would mean a different " +
                "apparent size at every zoom level. A factor of 1.0 is exactly the size before " +
                "this dial existed (at the shipped table zoom); the SHIPPED default is not " +
                "1.0 - it was re-based from a tuned cfg and the menu prints it under this " +
                "text. " +
                "Whatever you set here HOLDS while you pinch-zoom the table: the bars follow the " +
                "zoom only inside a fixed 0.7-1.5 band around the size you chose, so they can " +
                "neither shrink away nor swallow the board. Live: the next frame is drawn at the " +
                "new size. Range 0.25-3.",
                new AcceptableValueRange<float>(0.25f, 3f)));
        // BarZoomMinScale / BarZoomMaxScale were bound HERE. REMOVED (user ruling 2026-08-13 —
        // see the tombstone at the fields above). The band they configured is the constant pair
        // ActorBars.ZoomFollowMin/Max, still 0.7 and 1.5, so nothing about the look changed; the
        // stale lines in an existing worldui.cfg bind to nothing and are dropped on the next save.
        WristHud = _file.Bind("WorldUI", "WristHud", Defaults.WristHud,
            "Compact character status (HP/XP/conditions/gold) on the non-dominant wrist, look-at activated.");
        MapRoomHand = _file.Bind("WorldUI", "MapRoomHand", Defaults.MapRoomHand,
            "3D world map: fan the SELECTED character's scenario loadout out on your hand, with " +
            "that character's info on a wrist plate. Follows the character selected in the party " +
            "screen and updates within a quarter second when you change the selection or edit the " +
            "loadout. Either hand can take a card out to look at it closely, and it can be passed " +
            "between hands - but it is INSPECTION ONLY: the copy is not a game card, so it cannot " +
            "be played, discarded, reordered or put down, and letting go glides it back into the " +
            "fan. Nothing is transmitted and no game state is written in either position of this " +
            "switch. false = no fan and no wrist plate on the map; the map room is otherwise " +
            "completely unchanged. Live: the next frame builds or tears down.");
        TravelButtonOffsetXWindowHeights = _file.Bind("WorldUI", "TravelButtonOffsetXWindowHeights",
            Defaults.TravelButtonOffsetXWindowHeights,
            new ConfigDescription(
                "3D world map: moves the travel/'Quest erneut spielen' CONFIRM BUTTON SIDEWAYS " +
                "inside the floated quest window. The unit is FRACTIONS OF THAT WINDOW'S HEIGHT " +
                "(the same unit as the Y dial, so the same number means the same real distance on " +
                "both), positive = to the right. 0 = HORIZONTALLY CENTRED ON THE QUEST " +
                "INFORMATION: since ModBuild 197 the zero is the MEASURED centre of what the card " +
                "is painting and it moves with the quest, not the window rect's left edge. Any " +
                "value tuned before ModBuild 197 was measured from somewhere else and belongs back " +
                "at 0. A fraction and not a pixel count because [WorldUI] WindowLegibility resizes " +
                "the window. The card is half a window height wide, so the range reaches its left " +
                "and right edges and no further - the button stays a child of the window at every " +
                "value, so it always moves, scales and occludes with the card and can never be " +
                "left behind. Read live: turn it in the headset and the button moves on the next " +
                "frame. Range -0.25 to 0.25.",
                new AcceptableValueRange<float>(-MapRoom.MapTravelConfirm.OffsetLimitX,
                                                MapRoom.MapTravelConfirm.OffsetLimitX)));
        TravelButtonOffsetYWindowHeights = _file.Bind("WorldUI", "TravelButtonOffsetYWindowHeights",
            Defaults.TravelButtonOffsetYWindowHeights,
            new ConfigDescription(
                "3D world map: moves the travel/'Quest erneut spielen' CONFIRM BUTTON UP AND DOWN " +
                "inside the floated quest window. Same unit as the X dial - FRACTIONS OF THE " +
                "WINDOW'S HEIGHT - but measured UP FROM THE BOTTOM EDGE OF THE QUEST INFORMATION, " +
                "which is where 0 sits: at 0 the top of the button's content lies exactly on the " +
                "end of the information, i.e. DIRECTLY UNDERNEATH IT, for a short quest and a long " +
                "one alike, because that edge is re-measured live from what the card is painting. " +
                "Negative = lower, positive = higher. Any value tuned before ModBuild 197 was " +
                "measured from the window's fixed TOP edge and belongs back at 0. The range covers " +
                "the card in both directions from that point; the button stays a child of the " +
                "window at every value, so it always moves, scales and occludes with the card and " +
                "can never be left behind. Read live: turn it in the headset and the button moves " +
                "on the next frame. Range -0.6 to 0.6.",
                new AcceptableValueRange<float>(MapRoom.MapTravelConfirm.OffsetLimitYMin,
                                                MapRoom.MapTravelConfirm.OffsetLimitYMax)));
        // Tooltips / ActionElementHints: always on — user ruling 2026-08-13. The flat game raises
        // both on hover and offers no way to switch them off; in VR the mod's dials did, and their
        // OFF left the tooltip canvas at its 2D screen position, i.e. nowhere the player can read
        // it. Losing them means losing every condition/element/ability explanation the game gives.
        PanelMipBake = _file.Bind("WorldUI", "PanelMipBake", Defaults.PanelMipBake,
            "Aliasing follow-up to [Cards] FaceMipBake: the game also ships the textures the " +
            "INITIATIVE TRACK (RawImage portraits + frame/line sprites) and the mouseover " +
            "TOOLTIP box sample WITHOUT mipmaps, so both shimmer on their world-space panels " +
            "under minification no matter the MSAA level. When true, each unique mipless " +
            "texture is baked ONCE into a mipmapped trilinear/aniso copy (same shared cache " +
            "as the card faces - an atlas both use is baked once) and the live graphics are " +
            "swapped onto the baked copies (originals restored when a surface is released). " +
            "false = the initiative track and tooltip keep sampling the mipless originals.");
        PanelSupersample = _file.Bind("WorldUI", "PanelSupersample", Defaults.PanelSupersample,
            "SUPERSAMPLE the floated windows. A floated 1920 px window is drawn into roughly 1030 " +
            "rendered pixels per eye, so a 1-pixel-wide authored stroke lands on about half a " +
            "rendered pixel and drops in and out as your head moves - that is the shimmer on the " +
            "window text and edges, and no amount of mipmapping can fix it because the problem is " +
            "the RASTERIZATION, not the textures (ModBuild 189/190 mipped every texture on these " +
            "windows and the shimmer was unchanged). When true, the window is rendered by its own " +
            "camera into a render target at its full authored resolution with MSAA and mipmaps, " +
            "and the panel shows THAT texture: one properly filtered surface instead of hundreds " +
            "of point-sampled ones. Clicking, hovering, dragging and scrolling are untouched - the " +
            "real canvas stays exactly where it is and keeps taking every hit. Costs about 20-90 " +
            "MB of video memory per window; at most two windows at a time, the rest keep today's " +
            "rendering. false = exactly the rendering you have today.");
        PanelSupersampleFactor = _file.Bind("WorldUI", "PanelSupersampleFactor",
            Defaults.PanelSupersampleFactor,
            new ConfigDescription(
                "Sharpness/memory trade for [WorldUI] PanelSupersample: render-target pixels per " +
                "AUTHORED window pixel. 1.0 = the window is rendered at exactly the resolution it " +
                "was designed for, which is what removes the shimmer; above 1.0 buys extra " +
                "sharpness when you lean in close, at four times the memory for every doubling; " +
                "below 1.0 saves memory and starts to soften the text. Has no effect while " +
                "PanelSupersample is off. Range 0.5-2.",
                new AcceptableValueRange<float>(0.5f, 2f)));

        NeutraliseGrabPassBlur = _file.Bind("WorldUI", "NeutraliseGrabPassBlur",
            Defaults.NeutraliseGrabPassBlur,
            new ConfigDescription(
                "Removes the full-screen BLUR effect from floated windows. The game draws that blur " +
                "with a shader that GRABS THE FRAMEBUFFER it is being drawn into and paints a " +
                "blurred copy back over it - which in VR means the window is composited with a " +
                "blurred copy of its own half-finished self, once per eye, every frame. The mod's " +
                "log measures one such graphic covering 99 % of the character window and painting " +
                "over 272 of its 764 visible elements. Dropping the material leaves the element in " +
                "place but switched off, exactly as the game itself does when you enable its Simplified " +
                "UI option, so the room shows through the window instead of a white backdrop. Turn " +
                "it off if you want the blur back."));

        PanelMipLodOffset = _file.Bind("WorldUI", "PanelMipLodOffset",
            Defaults.PanelMipLodOffset,
            new ConfigDescription(
                "SHARPENS the mip filtering of [WorldUI] PanelSupersample's windows, in mip LEVELS. " +
                "A floated window is normally MINIFIED in the eye (the mod's own log measures the " +
                "party window at 1.58x, peak 1.86x), and trilinear filtering then deliberately " +
                "samples a coarser, softer mip level. This offset shifts that choice: 0 = the " +
                "filtering you have today, -0.5 (default) = half a level sharper. It is the ONLY " +
                "lever left on a minified window - PanelSupersampleFactor buys render-target texels " +
                "per AUTHORED pixel, and a minified window's sampler already selects a level at or " +
                "below authored resolution, so every level the factor adds above it is one the " +
                "hardware never reads. THE PRICE IS ALIASING: after the offset the sampled level " +
                "carries 2^-value texels per rendered pixel, so -0.5 reads 1.41 and -1.0 reads 2.00 " +
                "- twice what a pixel can hold, i.e. the crawl this whole path exists to remove. " +
                "Move it back towards 0 if window text starts crawling while you carry the window. " +
                "Has no effect while PanelSupersample is off. Range -2..0.",
                new AcceptableValueRange<float>(-2f, 0f)));

        // CatchAllModals / MenuPopupFloat: always on — user ruling 2026-08-11: essential
        // deadlock insurance (their OFF paths restored the silent-deadlock classes).
        // ForceMouseMode: always on — user ruling 2026-08-13 (see the tombstone above).
        CanvasScaleMm = _file.Bind("WorldUI", "CanvasScaleMm", Defaults.CanvasScaleMm,
            new ConfigDescription(
                "World-canvas scale: millimeters per uGUI pixel at diorama scale 1 (default 1 px " +
                "= 1 mm). This sizes EVERY converted panel at once, so the range is bounded on " +
                "both ends: at 0 every world panel in the mod collapses to a point and the game " +
                "becomes unreadable, and the ceiling keeps the panels from swallowing the board. " +
                "Range 0.2-4.",
                new AcceptableValueRange<float>(0.2f, 4f)));
        InitiativeDepthMaxSpreadPx = _file.Bind("WorldUI", "InitiativeDepthMaxSpreadPx", Defaults.InitiativeDepthMaxSpreadPx,
            "Initiative track 3D depth effect: the MAXIMUM total front-to-back z spread (uGUI " +
            "pixels) between the shallowest and deepest initiative portrait. The authored row " +
            "depth is compressed proportionally to land at this cap (never amplified). Higher = " +
            "stronger recession; 0 = flat. Live-tunable in the debug menu (Panels -> Initiative). " +
            "Range 0..40.");
        HoverInfoScale = _file.Bind("WorldUI", "HoverInfoScale", Defaults.HoverInfoScale,
            new ConfigDescription(
                "SIZE factor of the hover INFO panels — the little cards the game raises while the " +
                "pointer/fingertip hovers a board field ('2 Gold', 'Geschlossene Tür', chest, " +
                "obstacle, pressure plate, trap, quest item …) plus the card-action element hint. " +
                "The factor multiplies the panel's world scale, i.e. it scales the WHOLE panel " +
                "(frame + text) uniformly on top of the diorama/board scale, so the hint keeps its " +
                "proportions and stays readable at any board size and viewing distance — it is a " +
                "zoom, not a re-layout, and the panel's placement (which is derived from its own " +
                "measured extents) follows automatically. Default 0.6 = the size before this dial " +
                "existed, so nothing changes until it is tuned; raise it if the hover cards read " +
                "too small in the HMD. Live-tunable in the debug menu (Anzeige -> Infotafel-Größe): " +
                "the value is read on every placement tick, so an open panel resizes immediately and " +
                "the next hover comes up at the new size — no restart. Range 0.2-2.",
                new AcceptableValueRange<float>(0.2f, 2f)));
        EnemyRevealBoardClearance = _file.Bind("WorldUI", "EnemyRevealBoardClearance", Defaults.EnemyRevealBoardClearance,
            new ConfigDescription(
                "How far the ENEMY ROUND REVEAL (the monster ability cards shown after everyone " +
                "confirmed their selection) must clear the CONTROL BOARD's top edge, in real " +
                "metres measured AT THE BOARD. The player is normally LOOKING DOWN at the control " +
                "board when the reveal appears, so a purely gaze-anchored spawn lands right " +
                "behind/inside the board and is unreadable; the reveal is therefore lifted just " +
                "far enough that the player's line of sight to its BOTTOM edge passes this far " +
                "above the board's real (rendered) top edge. Because the gap is measured at the " +
                "board and the reveal floats further away, the on-screen gap is proportionally " +
                "larger. 0 = graze the top edge; raise it if the reveal still reads too close to " +
                "the board. Live-tunable in the debug menu (Panels -> Initiative); applies to the " +
                "next reveal spawn / lazy-follow step. Range 0-0.5.",
                new AcceptableValueRange<float>(0f, 0.5f)));
        SharedWindowArcRadiusMeters = _file.Bind("WorldUI", "SharedWindowArcRadiusMeters",
            Defaults.SharedWindowArcRadiusMeters,
            new ConfigDescription(
                "How far from the MAP TABLE'S CENTRE the shared (blue-barred) map-room windows " +
                "spawn, in real metres. They are seated on a HALF-RING of this radius over the far " +
                "half of the table — the ring's opening faces the room, so a shared window is " +
                "never placed between you and the map — all at one height and all yawed the same " +
                "way, stepped sideways only far enough that two of them cannot overlap. Larger = " +
                "the ring stands further across the table (smaller in view, but further from the " +
                "map); smaller = nearer and bigger, until the ring reaches the parchment, at which " +
                "point windows are pushed back out so they can never be drawn over the map. The " +
                "default 0.80 m is the smallest radius at which no part of the ring can stand over " +
                "the map at all. THIS IS THE INITIAL SPAWN POSITION ONLY: every shared window " +
                "stays freely movable and fully synchronised, and a window that is already " +
                "standing does not move when this changes. MULTIPLAYER — READ THIS BEFORE YOU " +
                "TYPE A NUMBER: as of ModBuild 480 this dial is BOUND BUT INERT. The ring's radius " +
                "is the shipped 0.80 m on every client, and the value here is ignored. It used to " +
                "be honoured, with the advice that everyone in a session should agree on one " +
                "number — but a shared window's pose may not be a function of anything " +
                "client-local, and 'everyone please type the same thing' is not a rule a build can " +
                "keep. The SHARED WINDOW ANCHOR line in the log names this dial and the value it " +
                "ignored whenever it differs from the constant. Drag the window: that MOVE is " +
                "synchronised and is how a group changes where it stands. Range 0.3-2.",
                new AcceptableValueRange<float>(0.3f, 2f)));
        MapRoomWindowBarHeightMeters = _file.Bind("WorldUI", "MapRoomWindowBarHeightMeters",
            Defaults.MapRoomWindowBarHeightMeters,
            new ConfigDescription(
                "How high above the MAP TABLE every map-room window is hung when it opens, in real " +
                "metres, MEASURED AT ITS GRAB BAR. One height for all of them — the shared " +
                "(blue-barred) windows on the ring over the table and your own windows on their " +
                "seats — so every bar in the room is on one line and no window can open lying on " +
                "the map. The window's body hangs from the bar, so a taller window reaches higher; " +
                "one tall enough to put its top edge absurdly high is lowered just far enough, and " +
                "the log says which window and why. Larger = everything hangs higher (further above " +
                "the map, closer to and past eye level); smaller = everything comes down toward " +
                "the table. The default 0.60 m is the average of the two grab-bar heights in your " +
                "own 'ideal position' screenshot, measured from the placement log. THIS IS THE " +
                "INITIAL SPAWN HEIGHT ONLY: every window stays freely movable, and a window that " +
                "is already standing does not move when this changes. MULTIPLAYER: as of ModBuild " +
                "480 this dial moves YOUR OWN map-room windows only. A shared (blue-barred) window " +
                "hangs at the shipped 0.60 m on every client, whatever is typed here — a shared " +
                "window's pose may not be a function of anything client-local — and the MAP ROOM " +
                "WINDOW BAR HEIGHT line names this dial and the value it ignored whenever the two " +
                "differ. Range 0.05-1.2.",
                new AcceptableValueRange<float>(0.05f, 1.2f)));
        ScenarioWindowBoardClearanceMeters = _file.Bind("WorldUI",
            "ScenarioWindowBoardClearanceMeters",
            Defaults.ScenarioWindowBoardClearanceMeters,
            new ConfigDescription(
                "How high above the PLAY FIELD a shared (blue-barred) scenario window — the story " +
                "dialog — is hung when it opens, in real metres, MEASURED AT ITS GRAB BAR. The " +
                "window's body hangs from the bar, so a taller window reaches higher. Larger = it " +
                "floats further above the board (further from the hexes, closer to eye level); " +
                "smaller = it comes down toward them, and it can never come down INTO them: 0.30 m " +
                "is the mod's own estimate of how far the board's walls and figures reach above " +
                "the play surface and the code floors this dial there. The default 0.60 m is the " +
                "same bar height the map room uses, so a window hovers the same way in both rooms. " +
                "THIS IS THE INITIAL SPAWN HEIGHT ONLY: the window stays freely movable and fully " +
                "synchronised, and one that is already standing does not move when this changes. " +
                "MULTIPLAYER — READ THIS BEFORE YOU TYPE A NUMBER: as of ModBuild 480 this dial is " +
                "BOUND BUT INERT. A shared window's spawn height is the shipped 0.60 m on every " +
                "client, and the value here is ignored. It used to be honoured, with the advice " +
                "that everyone in a session should agree on one number — but a shared window's pose " +
                "may not be a function of anything client-local, and 'everyone please type the " +
                "same thing' is not a rule a build can keep. The scenario window SPAWN line in the " +
                "log names this dial and the value it ignored, so a number typed here can be " +
                "grepped rather than guessed at. Drag the window: that MOVE is synchronised and is " +
                "how a group changes where it hangs. Range 0.3-1.5.",
                new AcceptableValueRange<float>(0.3f, 1.5f)));
        DesktopMirrorLeftEye = _file.Bind("WorldUI", "DesktopMirrorLeftEye", Defaults.DesktopMirrorLeftEye,
            "Flat monitor mirrors ONLY the HMD's LEFT eye: pins XRSettings.gameViewRenderMode to " +
            "LeftEye and skips the desktop 2D-menu composite blit, so the desktop is a clean " +
            "single-eye mirror in every state. Off = legacy (2D-menu composite during menus; " +
            "uncontrolled default XR mirror otherwise).");
        // ---- RETIRED 2026-08-09: the wrist HUD's SECOND set of pose dials -------------------
        ShowIntro = _file.Bind("WorldUI", "ShowIntro", Defaults.ShowIntro,
            "Show the game's intro (logos/video, pre-menu scenes) on the floating screen in VR " +
            "too. Off = old behavior: intro plays on the desktop only and the HMD shows a " +
            "'starting...' indicator in the void.");
        ScreenWidth = _file.Bind("WorldUI", "ScreenWidth", Defaults.ScreenWidth,
            new ConfigDescription(
                "Width of the floating 2D screen in real-world meters (16:9, height follows). " +
                "Replaces the pre-test-#6 'FlatScreenWidth' key (1.4 m read too small at 1.6 m). " +
                "Bounded because this screen is the ONLY surface that carries the main menu and " +
                "the A/X rescue: a 0 m screen is a rescue you cannot see. Range 0.4-8.",
                new AcceptableValueRange<float>(0.4f, 8f)));
        ScreenDistance = _file.Bind("WorldUI", "ScreenDistance", Defaults.ScreenDistance,
            new ConfigDescription(
                "Distance from the head to the floating 2D screen in real-world meters. Bounded " +
                "for the same reason as ScreenWidth, and at the near end also because a screen " +
                "inside your own head renders as nothing at all. Range 0.3-8.",
                new AcceptableValueRange<float>(0.3f, 8f)));
        // ClickLatch: always on — user ruling 2026-08-13 (see the tombstone above). The two
        // DragUnlock* dials below stay: they tune WHEN a deliberate movement opens the latch into
        // a drag, which is taste, not the difference between clicking and not clicking.
        // SuppressPhysicalMouse: always on — 2026-08-22 settings audit (see the constant above).
        MapWindOpacity = _file.Bind("WorldUI", "MapWindOpacity", Defaults.MapWindOpacity,
            "Opacity of the campaign map's drifting Wind/Clouds ambiance particles (0..1). The game's " +
            "flat map camera post-processes/masks these so they read as subtle; the VR forward capture " +
            "does not, so at full strength they render as thick translucent streaks that smear across " +
            "the location icons. 0.3 keeps a subtle drift; 1 = original strength; 0 = fully hidden.");
        DragUnlockDegrees = _file.Bind("WorldUI", "DragUnlockDegrees", Defaults.DragUnlockDegrees,
            "How far (degrees) the ray must move off its press direction to open the click " +
            "latch into a drag.");
        DragUnlockSeconds = _file.Bind("WorldUI", "DragUnlockSeconds", Defaults.DragUnlockSeconds,
            "How long (seconds) the ray must stay beyond DragUnlockDegrees before the latch " +
            "opens (filters single-frame tremor spikes).");
        PokeClick = _file.Bind("WorldUI", "PokeClick", Defaults.PokeClick,
            "Poking the floating 2D screen with the index fingertip clicks at the poked " +
            "position (press on plane contact, release on withdraw; latch rules as above). " +
            "The screen may be out of arm's reach at the default distance — lean/step in, " +
            "or reduce [WorldUI] ScreenDistance.");
        PokePressDepthMm = _file.Bind("WorldUI", "PokePressDepthMm", Defaults.PokePressDepthMm, new ConfigDescription(
            "Push-in depth in MILLIMETERS a fingertip must travel THROUGH a flat (converted " +
            "uGUI) button's canvas plane before the click fires. Touching the plane only ARMS " +
            "the press: the button shows its pressed visual (pointerDown) with a light haptic " +
            "tick; pushing past this depth fires the click with a stronger pulse; retracting " +
            "before reaching it cancels silently — no click, no penalty, re-armed after pulling " +
            "back out of the plane. Guards against accidental presses from brushing a panel " +
            "without making deliberate presses tedious. 0 = legacy instant click on plane " +
            "contact. Laser clicks are unaffected. Range 0-30.",
            new AcceptableValueRange<float>(0f, 30f)));
        DecisionPokeDeliberate = _file.Bind("WorldUI", "DecisionPokeDeliberate", Defaults.DecisionPokeDeliberate,
            "DECISION buttons (the docked take-damage burn choice, the burn-confirm dialog, " +
            "the short-rest Ja/Nein) demand a DELIBERATE physical press: touching the button " +
            "only ARMS it (pressed visual + light haptic tick); the click fires when the " +
            "fingertip is consciously WITHDRAWN back out of the plane past the release depth; " +
            "sweeping the hand through the button or leaving it sideways cancels silently — " +
            "no click, no penalty. Guards the costly, irreversible decision prompts against " +
            "accidental instant triggers. Applies ONLY to the physical poke on decision-dock " +
            "buttons; every other converted surface keeps the PokePressDepthMm push-in press, " +
            "and laser clicks are unaffected. Off = decision buttons press like everything else.");
        WindowFacing = _file.Bind("WorldUI", "WindowFacing", Defaults.WindowFacing,
            "When a floated window turns round to face you as you LET GO of it (position is never " +
            "touched — the window stays exactly where you put it, only its yaw is re-derived). " +
            "LaserOnly (default) = only after a LASER drag. A laser carry slides the window along " +
            "the aim ray without ever rotating it, so it arrives facing the way it used to and " +
            "would be read edge-on; a HAND carry already yaws the window with your wrist for the " +
            "whole drag, so re-deriving the yaw on release throws your own aim away. Always = both " +
            "(what every window did before this setting existed). Never = a released window keeps " +
            "exactly the orientation you let go at. This is a ONE-SHOT on release in every mode — " +
            "no window ever follows your head. SHARED multiplayer windows (the blue grab bar) are " +
            "excluded from all three and never turn: they belong to everyone in the room, so a " +
            "facing correction here would turn them away from the other players and would silently " +
            "disagree with the pose this client just published.");
        GrabBarTweenMs = _file.Bind("WorldUI", "GrabBarTweenMs", Defaults.GrabBarTweenMs,
            new ConfigDescription(
                "How long a floated window's GRAB BAR takes to change shape or place, in " +
                "milliseconds. The bar under a window re-seats whenever the window's drawn content " +
                "changes (a sub-view, a fit, a withhold and its release); with this above 0 it GROWS " +
                "or SHRINKS to its new size, SLIDES to its new position and grows from nothing when " +
                "it returns, instead of popping. A bar you are holding never eases — it follows your " +
                "hand at once. 0 = instant (the old pop). Live. Range 0-1000.",
                new AcceptableValueRange<float>(0f, 1000f)));
        // ClickMode: gone — user ruling 2026-08-13. Clicks are delivered via uGUI ExecuteEvents,
        // unconditionally, and deliberate drags via the uGUI drag handlers; see the tombstone.

        CombatLogFollow = _file.Bind("WorldUI", "CombatLogFollowSeat", Defaults.CombatLogFollowSeat,
            "Combat log panel anchor mode (the panel's own FOLLOW/PINNED pin button flips " +
            "this). THE SAME TWO WORDS AS THE CONTROL BOARD'S, running the same code " +
            "(FollowPinAnchor) since 2026-09-05. False (PINNED, default): the panel is " +
            "STATIC IN THE WORLD, bolted there at the size it was pinned at — a world-grab zoom does " +
            "not move or resize it — and it is carried along by a recenter so a pin can " +
            "never be stranded at the old seat. True (FOLLOW): the panel hangs off the rig, " +
            "so it keeps its place relative to you and scales with the diorama. Flipping " +
            "the pin NEVER MOVES the panel, in either direction. A pinned WORLD pose does " +
            "not survive a session: each scenario seats the panel once from the persisted " +
            "offsets, healed into view. Orientation is derived at events only, never per " +
            "frame. Replaces the test-#19 'CombatLogFollow' key: its follow default plus " +
            "the per-tick yaw billboard read as the panel tracking the head (test #20).");
        CombatLogForward = _file.Bind("WorldUI", "CombatLogForward", Defaults.CombatLogForward,
            "Combat log panel offset from the table anchor along the seat forward, real " +
            "meters (default = the old arc slot: azimuth 56° at 1.10 m). Persisted " +
            "automatically when the panel's grab bar is released.");
        CombatLogRight = _file.Bind("WorldUI", "CombatLogRight", Defaults.CombatLogRight,
            "Combat log panel offset to the seat right, real meters (grab-persisted).");
        CombatLogUp = _file.Bind("WorldUI", "CombatLogUp", Defaults.CombatLogUp,
            "Combat log panel height above the table plane, real meters (grab-persisted).");
        // THE RANGE IS THE CLAMP THE CODE ALREADY APPLIES (2026-08-22 settings audit). The
        // description already said "clamped 0.5-2" and nothing enforced it: CombatLogSurface
        // clamps on read AND on the gesture write, so past either end the arrows moved the number
        // and not the panel.
        CombatLogScale = _file.Bind("WorldUI", "CombatLogScale", Defaults.CombatLogScale,
            new ConfigDescription(
                "Combat log panel size multiplier (two-hand grab resize; clamped 0.5-2).",
                new AcceptableValueRange<float>(0.5f, 2f)));
        PanelsFollowView = _file.Bind("WorldUI", "PanelsFollowView", Defaults.PanelsFollowView,
            "LEGACY (pre-test-#8) behavior: the world panels (initiative track, element " +
            "board, combat log, objectives, button cluster) re-derive their placement from " +
            "the live rig yaw every frame, so they swing around the table with every snap " +
            "turn / world grab — perceived as a floating HUD. Default false: panels are " +
            "FIXED IN THE WORLD at the table and re-anchor only on rig rebuild or recenter.");
        HexHintFollowView = _file.Bind("WorldUI", "HexHintFollowView", Defaults.HexHintFollowView,
            "While a board-field hover hint (the TextInfoPanel/PropInfoPanel popups, e.g. " +
            "'Geschlossene Tür') is shown, drift it to a comfortable reading spot near the " +
            "CENTER of the player's field of view with LAZY (critically-damped) motion that " +
            "follows the head and settles, instead of leaving it at PropInfoSurface's fixed " +
            "table dock. Either way it stays upright and billboards toward the head. Off = " +
            "keep the dock position and only re-face it to the head.");
        HexHintDistance = _file.Bind("WorldUI", "HexHintDistance", Defaults.HexHintDistance,
            new ConfigDescription(
                "How far IN FRONT of your head a board-field hover hint parks while it is shown, " +
                "real meters (scaled with the diorama). Bigger = further away and smaller-looking. " +
                "Live; only meaningful with HexHintFollowView on.",
                new AcceptableValueRange<float>(0.15f, 2f)));
        HexHintDrop = _file.Bind("WorldUI", "HexHintDrop", Defaults.HexHintDrop,
            new ConfigDescription(
                "How far BELOW the center of your gaze a board-field hover hint parks, real meters " +
                "(scaled with the diorama). Positive = lower, negative = above the gaze center. " +
                "Live; only meaningful with HexHintFollowView on.",
                new AcceptableValueRange<float>(-1f, 1f)));
        HexHintSide = _file.Bind("WorldUI", "HexHintSide", Defaults.HexHintSide,
            new ConfigDescription(
                "Sideways offset of a board-field hover hint from the center of your gaze, real " +
                "meters (scaled with the diorama). Positive = to the right, negative = to the left. " +
                "0 = centered (today). Live; only meaningful with HexHintFollowView on.",
                new AcceptableValueRange<float>(-1f, 1f)));
        ModalStyle = _file.Bind("WorldUI", "ModalStyle", Defaults.ModalStyle,
            "How in-scenario 2D fallback windows (story boxes, events, tutorials, ESC " +
            "menu, rewards, choice dialogs, ...) are made operable in VR (P8, test #12). " +
            "'window' (default): only THAT window is converted to a world-space panel " +
            "floating in front of the HMD — poke AND laser clickable — and restored to " +
            "its 2D home when it closes; the full flat screen appears only when a " +
            "specific window fails to convert (reason logged). 'screen': pre-P8 " +
            "behavior — the full 2D desktop composite appears for every fallback " +
            "window. The manual A/X chord always summons the full screen regardless.");
        // ManualScreenChord: always on — user ruling 2026-08-11: the universal rescue must not
        // be switchable off; only its hold duration below stays tunable.
        ManualScreenChordSeconds = _file.Bind("WorldUI", "ManualScreenChordSeconds", Defaults.ManualScreenChordSeconds,
            new ConfigDescription(
                "Hold duration (seconds) of the non-dominant A/X for the manual flat-screen " +
                "toggle. BOUNDED (user ruling 2026-08-13): the chord itself is the universal " +
                "rescue and is no longer switchable off, so its hold time must not be settable " +
                "to a duration nobody can hold either. Range 0.3-6.",
                new AcceptableValueRange<float>(0.3f, 6f)));
        // DemoteOverlaySolidClears: always on — user ruling 2026-08-13 (see the tombstone above).
        // ScreenLayerSplit: always on — 2026-08-22 settings audit (see the constant above).
        LoadingIndicator = _file.Bind("WorldUI", "LoadingIndicator", Defaults.LoadingIndicator,
            "While the flat game shows its loading screen (scene transitions, scenario " +
            "start), float the game's own ROTATING LOADING SPINNER — the icon only, not the " +
            "hints/progress screen — in front of the HMD on the black void, hide the " +
            "floating 2D screen for the duration, and lower Unity's background loading " +
            "priority so head/hand rendering hitches less (the load itself takes slightly " +
            "longer). A few residual single-frame freezes remain (the game's per-transition " +
            "GC pause and synchronous asset-assembly frames cannot be split). Off = vanilla " +
            "behavior: the HMD shows a motionless void during loads.");

        // [Keyboard] Enabled: always on — user ruling 2026-08-13 (see the tombstone above).
        KeyboardAutoCase = _file.Bind("Keyboard", "AutoCapitalise", Defaults.AutoCapitalise,
            "Capitalise the first letter of each word typed on the on-screen keyboard and lower " +
            "the rest. The game's keyboard emits key CODES and maps letters to their upper-case " +
            "name, so typing straight through produces 'MY BRAVE PARTY'; this gives 'My Brave " +
            "Party'. Off = every letter arrives exactly as the game's keyboard produces it.");

        DevShowAllPanels = _file.Bind("WorldUI", "DevShowAllPanels", Defaults.DevShowAllPanels,
            "DEV: spawn the world-panel layout with dummy content on the desktop (no HMD needed).");
        DevForceConvert = _file.Bind("WorldUI", "DevForceConvert", Defaults.DevForceConvert,
            "DEV: apply the real canvas conversions in dev mode without an HMD (this moves the " +
            "game's 2D panels into world space — the desktop view changes accordingly).");

        // ONE line per build so the round is documented in the next hardware log. WorldUI is the
        // first module to bind, and the audit spans several modules, so the counts are written
        // down here rather than summed at runtime from something that could drift.
        VRLog.Info("Config", "SETTINGS AUDIT round 2 (user ruling 2026-08-13): 20 dials REMOVED "
                             + "and 16 CLAMPED (8 of the clamped are per-board families, so 31 "
                             + "cfg keys gained a range and 3 more a read floor). Removed, now "
                             + "unconditional: [WorldUI] "
                             + "InitiativeTrack, ElementBoard, Objectives, StatPanels, "
                             + "PropInfoCards, EnemyReveal, ActorBars, Tooltips, "
                             + "ActionElementHints, ClickLatch, ClickMode, ForceMouseMode, "
                             + "DemoteOverlaySolidClears, ScreenLeftMirrorFallback, "
                             + "MapAlbedoRender; [Keyboard] Enabled; [Compat] TutorialVRAdapt; "
                             + "[Rig] MenuRig; [HexHighlight] KillBorderLine, KillFill. "
                             + "Clamped: [WorldUI] CanvasScaleMm, ScreenWidth, ScreenDistance, "
                             + "ManualScreenChordSeconds; [Cards] CardWidth, InspectScale, "
                             + "FanRadius, and per board RestButtonDiameter, SlotOverlayScale, "
                             + "ActiveCardScale, PileScale, ObjectivesScale, ElementsScale, "
                             + "ClusterScale, DecisionScale; [Cards] BoardScale is floored at the "
                             + "READ instead, because the two-hand grab writes it. Stale keys in "
                             + "an existing cfg are inert BepInEx orphans and drop on the next "
                             + "save, so none of the removed behaviour can come back.");
    }

    /// <summary>True while WorldUI physicalization should be applied to live game UI.
    /// (The former [WorldUI] Master factor is gone — always on, user ruling 2026-08-11.)</summary>
    internal static bool ConversionActive =>
        VRSession.IsRunning || (Plugin.DevMode.Value && DevForceConvert.Value);
}
