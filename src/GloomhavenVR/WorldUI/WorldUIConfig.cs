using BepInEx.Configuration;
using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

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
    // to black). Kept on purpose: ButtonCluster, WristHud, CombatLog, LoadingIndicator (mod-
    // invented extras the flat game has no equivalent of), Dialogs / DecisionDock / ModalStyle /
    // TrayNativeControls / ScreenLayerSplit (both states present the SAME content, a real
    // fallback path each). None of the removed keys is re-bound: a stale cfg line is an inert
    // BepInEx orphan and is dropped on the next save, so it can never resurrect the behaviour.

    /// <summary>The module's config file (for late binders like FlatScreenStereo). Valid after <see cref="Bind"/>.</summary>
    internal static ConfigFile FileHandle => _file!;

    // ---- surfaces (each individually toggleable) ---------------------------------------
    internal static ConfigEntry<bool> ButtonCluster = null!;
    internal static ConfigEntry<bool> CombatLog = null!;
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

    /// <summary>Write a supersampled window's stale CanvasRenderer inherited alphas back to what the
    /// CanvasGroup chain says they should be. See <c>DrawReason.StaleInheritedAlpha</c>.</summary>
    internal static ConfigEntry<bool> PanelRepairInheritedAlpha = null!;

    /// <summary>Clear a supersampled window's capture to OPAQUE black so uGUI resolves partial glyph
    /// coverage against the plate instead of leaving it to be multiplied by its own alpha a second
    /// time in the RawImage composite. See <c>ApplyCaptureClear</c> for the arithmetic.</summary>
    internal static ConfigEntry<bool> PanelOpaqueCapture = null!;

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

    /// <summary>Disable the physical desktop mouse while VR runs so only the VR laser drives the pointer.</summary>
    internal static ConfigEntry<bool> SuppressPhysicalMouse = null!;

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

    /// <summary>User hid the combat log via its X button / settings toggle — do not auto-appear.</summary>
    internal static ConfigEntry<bool> CombatLogUserClosed = null!;

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

    /// <summary>Split the flat screen into a UI glass layer over a stereo background layer (test #18).</summary>
    internal static ConfigEntry<bool> ScreenLayerSplit = null!;

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

        ButtonCluster = _file.Bind("WorldUI", "ButtonCluster", Defaults.ButtonCluster,
            "Physical Ready/Undo/Skip buttons at the table edge.");
        // InitiativeTrack / ElementBoard / Objectives / StatPanels / PropInfoCards / EnemyReveal:
        // always on — user ruling 2026-08-13 (his named example is the initiative order). Each
        // OFF released the panel back to its 2D home, which in VR lives on the hidden flat stack:
        // the turn order, the element infusions, the scenario goal, the monster stat block, the
        // hover info for doors/chests/traps and the round's monster ability cards were all
        // switchable into invisibility. See the tombstone at the top of this file.
        CombatLog = _file.Bind("WorldUI", "CombatLog", Defaults.CombatLog,
            "Combat log as a world-space panel at the table's far side. Kept switchable on " +
            "purpose: the log is a HISTORY of things that already happened and the flat game " +
            "lets you close it too, so hiding it costs no state you need to play.");
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
                "apparent size at every zoom level. Default 1.0 = exactly the size before this " +
                "dial existed (at the shipped table zoom), so nothing changes until you tune it. " +
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
        PanelOpaqueCapture = _file.Bind("WorldUI", "PanelOpaqueCapture",
            Defaults.PanelOpaqueCapture,
            new ConfigDescription(
                "Fixes letters, icons and character art that thin out and then VANISH from a floated " +
                "window as you carry it. The window is photographed into a render target that is " +
                "cleared TRANSPARENT, so every partly-covered pixel - which is most of a thin glyph " +
                "stroke, every icon edge and the whole silhouette of the character render - is stored " +
                "multiplied by its own coverage, and then multiplied by it a SECOND time when the " +
                "photograph is composited into the world. A pixel at half coverage arrives at a " +
                "QUARTER of its brightness, and minifying the window pushes more of every letter into " +
                "that regime. Clearing the capture to opaque black makes uGUI resolve the coverage " +
                "once, against the window's own plate, which is exactly linear. The price: whatever " +
                "the window left genuinely see-through is black instead of the room behind it - its " +
                "margins and any rounded corner. Turn it off to compare the two.")); 

        PanelRepairInheritedAlpha = _file.Bind("WorldUI", "PanelRepairInheritedAlpha",
            Defaults.PanelRepairInheritedAlpha,
            new ConfigDescription(
                "Repairs elements that randomly stay INVISIBLE in a floated window after you drag " +
                "and release it - missing labels, missing icons, missing character boxes. The cause " +
                "is a CanvasRenderer left holding an inherited alpha of 0 while the CanvasGroup " +
                "chain above it says the element is fully visible: two values for the same thing " +
                "that uGUI failed to keep in step. The repair writes the second into the first, and " +
                "ONLY when they disagree - an element the game means to hide reads 0 on both and is " +
                "never touched, so this cannot reveal a closed sub-view. Turn it off if something " +
                "appears that should not; the mod's log names every element it repaired."));

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
        SuppressPhysicalMouse = _file.Bind("WorldUI", "SuppressPhysicalMouse", Defaults.SuppressPhysicalMouse,
            "While VR is running, disable the physical desktop mouse in the InputSystem so its " +
            "(stale) desktop position can no longer hover or select map/menu elements behind your " +
            "back — only the VR laser drives the pointer. The mouse is re-enabled when VR stops.");
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
        // ClickMode: gone — user ruling 2026-08-13. Clicks are delivered via uGUI ExecuteEvents,
        // unconditionally, and deliberate drags via the uGUI drag handlers; see the tombstone.

        CombatLogFollow = _file.Bind("WorldUI", "CombatLogFollowSeat", Defaults.CombatLogFollowSeat,
            "Combat log panel anchor mode (the panel's own FOLLOW/PINNED pin button flips " +
            "this). False (PINNED, default): the panel is STATIC IN THE WORLD — placed once " +
            "from the persisted offsets on scenario entry, then frozen until grabbed. " +
            "True (FOLLOW): the panel re-derives its place from the table anchor + seat " +
            "yaw (moves with recenters and the diorama like the other world panels; " +
            "orientation still only re-derives on recenter, never per frame). Replaces " +
            "the test-#19 'CombatLogFollow' key: its follow default plus the per-tick " +
            "yaw billboard read as the panel tracking the head (test #20).");
        CombatLogForward = _file.Bind("WorldUI", "CombatLogForward", Defaults.CombatLogForward,
            "Combat log panel offset from the table anchor along the seat forward, real " +
            "meters (default = the old arc slot: azimuth 56° at 1.10 m). Persisted " +
            "automatically when the panel's grab bar is released.");
        CombatLogRight = _file.Bind("WorldUI", "CombatLogRight", Defaults.CombatLogRight,
            "Combat log panel offset to the seat right, real meters (grab-persisted).");
        CombatLogUp = _file.Bind("WorldUI", "CombatLogUp", Defaults.CombatLogUp,
            "Combat log panel height above the table plane, real meters (grab-persisted).");
        CombatLogScale = _file.Bind("WorldUI", "CombatLogScale", Defaults.CombatLogScale,
            "Combat log panel size multiplier (two-hand grab resize; clamped 0.5-2).");
        CombatLogUserClosed = _file.Bind("WorldUI", "CombatLogUserClosed", Defaults.CombatLogUserClosed,
            "The user hid the combat log via its top-right X button (or the in-VR settings " +
            "'Kampflog anzeigen' toggle). While true the panel releases back to its 2D home and " +
            "does NOT auto-reappear in a scenario; the settings toggle clears it and re-shows the " +
            "panel. Kept separate from [WorldUI] CombatLog (the feature master) so re-showing " +
            "never disturbs the feature toggle or the persisted layout.");
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
        ScreenLayerSplit = _file.Bind("WorldUI", "ScreenLayerSplit", Defaults.ScreenLayerSplit,
            "Render the floating 2D screen as TWO layers (hardware test #18): the game's UI " +
            "cameras — whose Screen-Space-Camera canvases only ever render through their " +
            "assigned camera, so stereo mirror cameras can never reproduce them and the menu " +
            "went one-eyed — draw onto a transparent 'glass' quad shown identically to both " +
            "eyes at the screen plane, while 3D scene cameras and videos render a background " +
            "layer a few cm behind it with per-eye stereo depth. Off (or on any failure): " +
            "single-RT fallback — one flat mono screen in both eyes, never one-eyed.");
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
