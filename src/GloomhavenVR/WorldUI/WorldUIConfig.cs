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
    /// </summary>
    internal static ConfigEntry<float> BarSizeScale = null!;

    /// <summary>
    /// Lower end of the zoom clamp (user: "ein minimum und maximum der Größe, damit sie sich trotz
    /// zoomen nie über die Grenzen hinaus skalieren können"). The bars FOLLOW the table zoom — they
    /// grow with the miniature when the table is pinched larger and shrink with it when it is
    /// pinched away — and this is the floor of that following, as a factor of
    /// <see cref="BarSizeScale"/>. See <see cref="WorldUI.ActorBars"/> for the arithmetic and the
    /// guarantee.
    /// </summary>
    internal static ConfigEntry<float> BarZoomMinScale = null!;

    /// <summary>Upper end of the zoom clamp; see <see cref="BarZoomMinScale"/>.</summary>
    internal static ConfigEntry<float> BarZoomMaxScale = null!;
    internal static ConfigEntry<bool> WristHud = null!;

    /// <summary>Aliasing follow-up to [Cards] FaceMipBake: mip-bake the mipless game textures the
    /// initiative track and the hover-hint tooltip sample on their world-space hosts (see
    /// <see cref="PanelMipBake"/>). Mirrors the cards' kill switch; read live.</summary>
    internal static ConfigEntry<bool> PanelMipBake = null!;

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
            "gently grow with head distance to stay readable, bounded by the same " +
            "BarZoomMinScale/BarZoomMaxScale clamp as the table zoom. This dial says nothing " +
            "about the TABLE zoom — that is BarSizeScale and its clamp.");
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
                "Live: the next frame is drawn at the new size. Range 0.25-3.",
                new AcceptableValueRange<float>(0.25f, 3f)));
        BarZoomMinScale = _file.Bind("WorldUI", "BarZoomMinScale", Defaults.BarZoomMinScale,
            new ConfigDescription(
                "MINIMUM size of the actor bars, as a factor of BarSizeScale. The bars follow the " +
                "TABLE ZOOM - pinch the table larger and a bar grows with the miniature it belongs " +
                "to, pinch it away and the bar shrinks with it - and this is the floor of that " +
                "following: however far you zoom out, a bar is never smaller than " +
                "BarSizeScale x this. 0.7 = at most 30 % smaller than the size you set. Set it " +
                "equal to BarZoomMaxScale to switch the following off entirely and get one fixed " +
                "real size at every zoom. Range 0.1-1.",
                new AcceptableValueRange<float>(0.1f, 1f)));
        BarZoomMaxScale = _file.Bind("WorldUI", "BarZoomMaxScale", Defaults.BarZoomMaxScale,
            new ConfigDescription(
                "MAXIMUM size of the actor bars, as a factor of BarSizeScale - the ceiling of the " +
                "table-zoom following described at BarZoomMinScale. However far you zoom in, a bar " +
                "is never larger than BarSizeScale x this, so a zoomed-in table can never let the " +
                "bars swallow the board. 1.5 = at most 50 % larger than the size you set. Range 1-3.",
                new AcceptableValueRange<float>(1f, 3f)));
        WristHud = _file.Bind("WorldUI", "WristHud", Defaults.WristHud,
            "Compact character status (HP/XP/conditions/gold) on the non-dominant wrist, look-at activated.");
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
