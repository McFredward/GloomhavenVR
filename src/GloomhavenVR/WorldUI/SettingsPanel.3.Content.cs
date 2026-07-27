using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal sealed partial class SettingsPanel : IPanelGrabOwner
{
    // ---- construction ----------------------------------------------------------------------

    private void Build()
    {
        // ROOT-CAUSE guard (bug #5): the holder GameObject is a plain scene object (NOT
        // DontDestroyOnLoad), so a scene unload destroys it AND every row/widget it owns,
        // yet this SettingsPanel instance survives on the DontDestroyOnLoad WorldUI driver.
        // SetOpen() then sees the Unity-null _holder and calls Build() AGAIN. If the
        // accumulation lists were not reset first, the rebuild would append a second set of
        // rows/refreshers on top of the stale, destroyed ones — every refresh would deref a
        // destroyed row (NRE flood, aborting the rest of RefreshAll) and the duplicated rows
        // would stack into the "super long / not embedded" layout. Clearing here guarantees
        // exactly ONE clean set per build (Build is only ever reached with _holder == null,
        // so the previous holder and its rows are already gone).
        _refreshers.Clear();
        _rows.Clear();
        _rowVisible.Clear();
        _rowParent = null;
        _rowGate = null;
        ResetTooltip(); // the previous bubble died with the previous holder

        MixedReality.Bind(); // MR config may be read below before VRRigDriver's first tick

        BuildFrame();

        _root = new GameObject("Canvas") { layer = 5 };
        var rect = _root.AddComponent<RectTransform>();
        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = CanvasConversion.WorldCamera;
        _root.AddComponent<GraphicRaycaster>();
        rect.sizeDelta = new Vector2(PanelWidthPx, 100f); // height grows via layout

        // Ride the grab frame: bottom-center pivot so the panel grows UP from the bar
        // (like the tray/combat-log mounts). localScale = real meters per uGUI pixel;
        // the holder's diorama scale and the frame's user size factor stack on top.
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        _root.transform.SetParent(_frame, worldPositionStays: false);
        _root.transform.localPosition = new Vector3(0f, BarGapMeters, 0f);
        _root.transform.localScale = Vector3.one * CanvasMetersPerPixel;

        var bg = _root.AddComponent<Image>();
        bg.color = new Color(0.07f, 0.07f, 0.10f, 0.92f);

        var layout = _root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 12);
        layout.spacing = 4f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        var fitter = _root.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ---- Header: breadcrumb (where am I) + close, spanning both columns --------
        _rowParent = null;
        _rowGate = null;
        var header = Row();
        TextMeshProUGUI crumb = Label(header, "", 18f, bold: true, flexible: true);
        _refreshers.Add(() => crumb.text = "GloomhavenVR   ›   " + NavCatLabel(CurrentNav)
            + (PerBoard() ? "   ›   " + DebugSubCatLabel(CurrentSubCat) : ""));
        Button(header, "X", 40f, () => SetOpen(false));

        // ---- Body: persistent category SIDEBAR | swappable CONTENT pane -----------
        BuildBody(out RectTransform sidebar, out RectTransform content);

        // Sidebar — one always-visible button per top-level category. Poke or laser
        // selects it (plain uGUI Button, same as every other control); the content pane
        // then shows ONLY that category's rows.
        _rowParent = sidebar;
        _rowGate = null;
        for (int c = 0; c < NavCatCount; c++)
            BuildNavButton(c);

        // Everything below builds into the CONTENT column, each row gated to its category
        // (or, for the two element-bearing tabs, to its category + selected element).
        _rowParent = content;

        // ===== Komfort — sitting at the table: world, movement, hands =====
        // ROW ORDER IS THE DESIGN (2026-07 restructure): most-used first. The recenter/recall pair
        // is the single most-pressed control in the whole panel and the ONLY way back from a lost
        // board, so it opens the tab instead of closing it; the three sections below then read
        // world → movement → hands.
        GateCat(NavCat.Komfort);

        var recenterRow = Row();
        Button(recenterRow, Loc.Mod("recenter_now"), 0f, Comfort.RequestRecenter, flexible: true);
        // ESCAPE HATCH next to the recenter (incident: "the control board was gone after I walked
        // around and briefly took the headset off"). The per-frame lost-board watchdog
        // (PlayTray.TickLostWatchdog) recovers it by itself, but a board the player cannot find is
        // an immediate hard stop — playing becomes impossible — so there must always be a manual,
        // zero-latency way back. Routed through the driver's request queue so the pose is written
        // on the main thread and the move stays a SANCTIONED one for the issue-C pose watchdog.
        Button(recenterRow, Loc.Mod("recall_board"), 0f, Cards.CardsDriver.RequestBoardRecall, flexible: true);

        Section(Loc.Mod("sec_table_world"));

        // Table scale: SetScaleMultiplier applies live around the head + persists.
        Stepper(Loc.Mod("table_scale"),
            () => $"{CurrentScaleMultiplier():0.00}x",
            delta =>
            {
                if (ComfortSettings.IsBound)
                    Comfort.SetScaleMultiplier(CurrentScaleMultiplier() + delta * 0.25f);
            });

        Stepper(Loc.Mod("table_height"),
            () => ComfortSettings.IsBound ? $"{ComfortSettings.TableHeightOffset.Value:+0.00;-0.00;0.00}m" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.TableHeightOffset.Value =
                    Mathf.Clamp(ComfortSettings.TableHeightOffset.Value + delta * 0.05f, -0.4f, 0.6f);
            });

        // Demeo-style world tilt ([Rig] WorldTiltDegrees, 5° steps, 0-60): the diorama appears
        // tilted toward the player (rig-side counter-rotation — VRRigDriver.TickWorldTilt reads
        // the entry every frame, so stepping applies live; BepInEx persists on set).
        Stepper(Loc.Mod("world_tilt"),
            () => $"{Mathf.Clamp(Plugin.WorldTiltDegrees.Value, 0f, 60f):0}°",
            delta => Plugin.WorldTiltDegrees.Value =
                Mathf.Clamp(Plugin.WorldTiltDegrees.Value + delta * 5f, 0f, 60f));

        Section(Loc.Mod("sec_movement"));

        // Turn mode + degrees.
        var turnRow = Row();
        Label(turnRow, Loc.Mod("turning"), 16f, flexible: true);
        CycleButton(turnRow, 86f,
            () => ComfortSettings.IsBound ? ComfortSettings.Turn.Value.ToString() : "-",
            () =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.Turn.Value = (TurnMode)(((int)ComfortSettings.Turn.Value + 1) % 3);
            });
        MiniStepper(turnRow,
            () => ComfortSettings.IsBound ? $"{ComfortSettings.SnapTurnDegrees.Value:0}°" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.SnapTurnDegrees.Value =
                    Mathf.Clamp(ComfortSettings.SnapTurnDegrees.Value + delta * 15f, 15f, 90f);
            });

        Toggle(Loc.Mod("free_movement"),
            () => ComfortSettings.IsBound && ComfortSettings.FreeMovement.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.FreeMovement.Value = v; });

        var grabRow = Row();
        Label(grabRow, Loc.Mod("world_grab"), 16f, flexible: true);
        ToggleButton(grabRow,
            () => ComfortSettings.IsBound && ComfortSettings.WorldGrabEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.WorldGrabEnabled.Value = v; });
        Label(grabRow, "rot", 13f);
        ToggleButton(grabRow,
            () => ComfortSettings.IsBound && ComfortSettings.RotateEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.RotateEnabled.Value = v; });
        Label(grabRow, "scl", 13f);
        ToggleButton(grabRow,
            () => ComfortSettings.IsBound && ComfortSettings.ScaleEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.ScaleEnabled.Value = v; });

        Section(Loc.Mod("sec_hands_aim"));

        Toggle(Loc.Mod("dominant_hand_right"),
            () => !string.Equals(Plugin.PrimaryHand.Value, "Left", StringComparison.OrdinalIgnoreCase),
            v => Plugin.PrimaryHand.Value = v ? "Right" : "Left");

        // [Board] ForceFarMode. It used to sit under "Anzeige" among the render switches, which is
        // where nobody would look for it: it disables fingertip near-touch picking and forces the
        // far ray, i.e. it decides HOW YOUR HANDS REACH the board. That is an input decision and it
        // belongs with the dominant hand, one row above.
        Toggle(Loc.Mod("board_far_ray"),
            () => BoardConfigSafe(() => Board.BoardConfig.ForceFarMode.Value),
            v => { if (Board.BoardConfig.ForceFarMode != null) Board.BoardConfig.ForceFarMode.Value = v; });

        // ===== Grafik — the whole picture: the render trade, presentation, Mixed Reality =====
        // The sharpness/smoothness block (preset + per-eye resolution + MSAA) is built FIRST by
        // BuildPerformanceCategory so it opens the tab — it is what a player with a frame-rate
        // problem came for. It is the former top-level "Leistung" tab, folded in here because
        // post-processing (which was under "Anzeige") and MSAA are two halves of one decision and
        // had no business being on two different tabs.
        BuildPerformanceCategory();

        GateCat(NavCat.Grafik);
        Section(Loc.Mod("sec_presentation"));

        Toggle(Loc.Mod("disable_post"),
            () => Plugin.DisablePostProcessing.Value,
            v => Plugin.DisablePostProcessing.Value = v);

        // Optional game wall see-through ([Compat] WallFade): ON lets the game's own
        // view-dependent wall fade run (it follows the HMD); OFF (default) keeps walls solid.
        // Applies LIVE — the Harmony postfix consults the entry every frame. USER-FACING, and the
        // old single-toggle "Wände" tab is gone (2026-07), so this normal-user switch lives in the
        // presentation block of "Grafik"; the developer-grade fade FRACTIONS live under
        // Debug → Leistung & Effekte → Wandüberblendung.
        // The MarkChange is not cosmetic: this row is also the A/B handle for the 2026-07
        // submission-cost investigation. WallSegmentFade is the mod's highest-volume material
        // touch — a per-renderer MaterialPropertyBlock on every faded wall segment, re-applied
        // every frame — and a per-renderer property block is exactly what stops a renderer from
        // batching, which lands in the render loop rather than in its own 0.17 ms STEPS entry.
        // Closing the measurement window on the flip is what makes the two [Perf] SPLIT lines
        // either side of it comparable; without it both sides average into one window and the
        // experiment produces nothing.
        Toggle(Loc.Mod("wall_see_through"),
            () => Plugin.WallFade.Value,
            v =>
            {
                Core.PerfMonitor.MarkChange($"wall see-through {(v ? "ON" : "OFF")} — A/B boundary "
                                            + "(the mod's largest per-renderer material touch)");
                Plugin.WallFade.Value = v;
            });

        // The footnote for the "*" in the post-processing caption, directly under the block it
        // annotates (it used to be four rows further down, after two settings it says nothing about).
        var startNote = Row(22f);
        Label(startNote, Loc.Mod("applies_next_start"), 12f, flexible: true);

        Section(Loc.Mod("mixed_reality"));

        // MR chroma-key mode (item 7): disables all skyboxes and clears the sky/background to
        // the key color so a compositor (Virtual Desktop) can passthrough-composite the room.
        Toggle(Loc.Mod("mixed_reality"),
            () => MixedReality.Enabled.Value,
            v => MixedReality.Enabled.Value = v);
        var mrColorRow = Row();
        Label(mrColorRow, Loc.Mod("key_color"), 16f, flexible: true);
        CycleButton(mrColorRow, 100f, () => MixedReality.KeyColorName, MixedReality.CycleKeyColor);

        // Where the REST of the picture is set. Two one-line pointers, reasoning on hover, LAST on
        // the tab because a signpost belongs at the exit: the game's own Optionen › Grafik owns
        // shadows/textures/lighting/AA (we complement it, we never mirror it), and the measurement
        // + internal A/B switches stay in the config file. These two rows used to be built inside
        // BuildPerformanceCategory, which put them in the MIDDLE of the merged tab.
        var gameNote = Row(20f);
        Label(gameNote, Loc.Mod("perf_game_graphics_short"), 12f, flexible: true);
        Tip(gameNote, "perf_game_graphics_note");

        var cfgNote = Row(20f);
        Label(cfgNote, Loc.Mod("perf_config_short"), 12f, flexible: true);
        Tip(cfgNote, "perf_config_note");

        // (The control-board MODEL picker used to sit here among the display switches. User 2026-07:
        // "Genau wie die Hände und die Maske soll auch das Board an sich außerhalb des Debug-Menüs
        // umgestellt werden können" — it is the same KIND of choice as the head mask and the hand
        // style, so it sits WITH them under Avatar. See the "Kontrollbrett" row below.)

        // ===== Tafeln — the mod's own panels, hints and info cards =====
        // Every row here is literally a panel/surface of the mod, which is what makes the tab name
        // checkable instead of vague — and what got these four out of the old "Anzeige" grab bag,
        // where they sat between render switches they have nothing to do with. Order: the two things
        // a player toggles during a session, then the size dial for them, then the master switch
        // last (it turns off everything above it).
        GateCat(NavCat.Tafeln);

        // Re-spawn / hide the combat log window (item 6): SHOW clears the user-closed flag
        // set by the panel's X button and reconverts it at the persisted pose; HIDE releases
        // it back to its 2D home and keeps it from auto-reappearing.
        Toggle(Loc.Mod("show_combat_log"),
            () => CombatLogSurface.UserVisible,
            v => CombatLogSurface.SetUserVisible(v, "settings"));

        // User 7c: action-phase element hints (the tooltip parked at the board's top-left)
        // on/off. Live: WorldTooltips.LateTick reads WorldUIConfig.ActionElementHints every tick.
        Toggle(Loc.Mod("element_hints"),
            () => WorldUIConfig.ActionElementHints.Value,
            v => WorldUIConfig.ActionElementHints.Value = v);

        // User ("Ich will die Größe der Infotafeln, die beim Mouseover erscheinen, einstellen
        // können"): SIZE of the hover info panels — the cards the game raises over a board field
        // ("2 Gold", "Geschlossene Tür", chest/obstacle/trap/quest item) AND the element hint
        // toggled right above, since both are the same mouseover-info family to the player. Directly
        // under the hints toggle, the only other tooltip setting. GLOBAL (the panels ride no board —
        // they dock at the PropInfo layout slot / above the board's measured top edge), so this is a
        // flat row, not a per-board Debug element. LIVE: PropInfoSurface and WorldTooltips both
        // re-read [WorldUI] HoverInfoScale on every placement tick, so a shown panel resizes on the
        // spot and the next hover comes up at the new size; BepInEx persists on set. 0.05 steps over
        // the entry's 0.2-2 range, x readout like the other size steppers.
        var hoverInfoRow = Row();
        Label(hoverInfoRow, Loc.Mod("hover_info_size"), 16f, flexible: true);
        MiniStepper(hoverInfoRow,
            () => $"{WorldUIConfig.HoverInfoScale.Value:0.00}x",
            d =>
            {
                ConfigEntry<float> e = WorldUIConfig.HoverInfoScale;
                e.Value = Mathf.Clamp(e.Value + d * 0.05f, 0.2f, 2f);
            });

        // The master switch for the physicalized UI, LAST because it disables everything above it.
        // (The panel deliberately stays reachable with this off — see the class doc.)
        Toggle(Loc.Mod("world_ui_surfaces"),
            () => WorldUIConfig.Master.Value,
            v => WorldUIConfig.Master.Value = v);

        // ===== Avatar — how your gear looks, to you and to your peers =====
        GateCat(NavCat.Avatar);
        Section(Loc.Mod("sec_appearance"));

        // Multiplayer avatar: head mask (writes [Net] MaskId — synchronized), a local
        // self-preview mirror ([Net] MirrorEnabled), and how much of OTHER players' boards
        // this client renders ([Net] RemoteBoards). Force the [Net] entries bound so the
        // pickers work even with the networking hook off.
        Net.NetModule.BindConfig();
        var maskRow = Row();
        Label(maskRow, Loc.Mod("head_mask"), 16f, flexible: true);
        CycleButton(maskRow, 120f, MaskLabel, CycleMask);

        // SIZE of that head mask (writes [Net] MaskSize — synchronized like the mask itself, so
        // peers see your mask at the size you picked). Directly under the mask picker because it
        // is the same choice, one property further. LIVE: the mirror re-reads the entry every tick
        // and remote clients re-apply the received size on their next frame — no restart, and the
        // 1.00x default means today's look until it is tuned. 0.05 steps over the entry's
        // 0.25-2.55 range, "x" readout like the other size steppers.
        var maskSizeRow = Row();
        Label(maskSizeRow, Loc.Mod("mask_size"), 16f, flexible: true);
        MiniStepper(maskSizeRow, MaskSizeLabel, StepMaskSize);

        // Hand style: cycles Glove → Plate → Arcane (writes [Hands] HandStyle; HandsDriver
        // rebuilds the hand visuals live). Synchronized via the avatar rig packet. Also
        // reachable as the "Stil" row under Hände/Figuren/Handgelenk (same bind).
        var handStyleRow = Row();
        Label(handStyleRow, Loc.Mod("hands"), 16f, flexible: true);
        CycleButton(handStyleRow, 120f, HandStyleLabel, CycleHandStyle);

        // CONTROL-BOARD MODEL — Eiche / Stahl / Bronze (writes [Cards] Board). User 2026-07:
        // "Genau wie die Hände und die Maske soll auch das Board an sich außerhalb des Debug-Menüs
        // umgestellt werden können (schließlich ist es ein Feature für die User — Debug soll immer
        // nur optional sein)". So it is built HERE, as the third row of the same style block the
        // head mask and the hand style form, and NOT in the Debug pane: Debug only tunes the
        // selected board's offsets/sizes and points back here (BuildElementTuning).
        //
        // WHY THIS CATEGORY: "Avatar — Mehrspieler-Erscheinung" is the block of choices that decide
        // how your gear LOOKS, to you and to everyone else — and the board style now travels on the
        // wire exactly like the mask and the hand style do (extras trailing block, see
        // NetProtocol.PileBrowseBoardStyleShift), so a peer's board reads in the material that peer
        // actually picked. That is what makes it belong next to them rather than under "Grafik".
        //
        // LIVE: writing the entry fires SettingChanged → CardsDriver.OnBoardChanged sets a rebuild
        // flag → the next Update tears the tray down and rebuilds it from the newly selected prefab
        // AT THE OLD WORLD POSE (RebuildBoard captures/restores it), so the board changes material
        // under your hands without a restart and without moving. BepInEx persists on set.
        var boardModelRow = Row();
        Label(boardModelRow, Loc.Mod("control_board"), 16f, flexible: true);
        CycleButton(boardModelRow, 120f, ControlBoardLabel, CycleControlBoard);

        // The local self-preview mirror: it shows YOUR mask/hands/board, so it closes the appearance
        // block rather than opening the multiplayer one.
        Toggle(Loc.Mod("mirror"),
            () => Net.NetModule.MirrorEnabled != null && Net.NetModule.MirrorEnabled.Value,
            v => { if (Net.NetModule.MirrorEnabled != null) Net.NetModule.MirrorEnabled.Value = v; });

        Section(Loc.Mod("sec_multiplayer"));

        // How much of OTHER players' boards this client draws (Off / Aktionsphase / Immer). Purely
        // local — it never touches game state and is never transmitted, so every player picks their
        // own. LIVE: the whole remote-board render path (frame + parity widgets + inert furniture +
        // the board-anchored item/pile-browse fans + board card flights) asks
        // Net.RemoteBoardGate every frame, so a cycle here changes what is drawn on the next frame.
        var remoteBoardsRow = Row();
        Label(remoteBoardsRow, Loc.Mod("remote_boards"), 16f, flexible: true);
        CycleButton(remoteBoardsRow, 150f, RemoteBoardsLabel, CycleRemoteBoards);
        // The control is real but has nothing to act on in single player — say so instead of
        // leaving a setting that visibly does nothing when the player tries it alone.
        var remoteBoardsNote = Row(20f);
        Label(remoteBoardsNote, Loc.Mod("remote_boards_note"), 12f, flexible: true);

        // ===== Debug — the sole element-bearing tuning tab =====
        // CALL ORDER IS THE PAGE ORDER (2026-07 restructure). Every row registers its own gate, so
        // moving these calls changes nothing about WHICH rows a page shows — but rows land in the
        // content column in build order, so it decides what a Debug page shows FIRST. The choosers
        // (Bereich ▸ Element, plus the board readout) are built by BuildElementTuning, so that call
        // now comes BEFORE the per-element row blocks: the navigation is at the top of every Debug
        // page instead of below the wall-fade steppers on one page and below the hand offsets on
        // another. The user's standing rule is that Debug has to be navigable too.
        BuildElementTuning();

        // Debug ▸ Alle Einstellungen — the GENERIC config browser over every bound ConfigEntry
        // (2026-07, user: "Alle config einstellungen sollen im VR Menu anpassbar sein!"). Fixed row
        // POOL, nothing walked at build time; see SettingsPanel.8.ConfigBrowser.cs.
        BuildConfigBrowser();

        BuildTimingCategory();  // Debug → Leistung & Effekte → Zeitgeber / CPU
        BuildBatchingCategory();// Debug → Leistung & Effekte → Bündelung (experimentell)
        BuildHandsCategory();   // Debug → Hände-Offsets (per-style seat + card-fan geometry)
        BuildFiguresCategory(); // Debug → Figuren-Offsets (per-style held-mini pose)
        BuildWristCategory();   // Debug → Handgelenk (per-style wrist-HUD pose)

        // Debug → Leistung & Effekte → Wandüberblendung: the wall-fade thresholds (live [WallFade]
        // config — WallSegmentFade re-reads the clamped accessors every evaluation tick). Issue 2:
        // developer-grade fractions, so each row gates itself to that element (WallFadeRowsVisible).
        // The user-facing on/off toggle lives under Grafik ▸ Darstellung.
        WallFadeTuning.Bind();
        // Settings audit 2026-07: these four fractions shape the Schmitt trigger / dwell hysteresis
        // INSIDE WallSegmentFade's evaluation loop, and that loop only runs while [Compat] WallFade
        // is on (WallSegmentFade.Enabled). With "Wände durchsichtig" off they are live entries that
        // change nothing you can see — so the pane says which switch they depend on rather than
        // leaving four steppers that look broken. Shown red-flagged only while the toggle is OFF.
        _rowGate = () => WallFadeRowsVisible() && (Plugin.WallFade == null || !Plugin.WallFade.Value);
        var wallFadeNote = Row(20f);
        Label(wallFadeNote, Loc.Mod("wall_fade_note"), 12f, flexible: true);

        AddWallFadeRow("Einblenden", WallFadeTuning.OnFraction, 0.05f, 0.05f, 0.95f,
            v => $"{v * 100f:0}%");
        AddWallFadeRow("Ausblenden", WallFadeTuning.OffFraction, 0.05f, 0.01f, 0.95f,
            v => $"{v * 100f:0}%");
        AddWallFadeRow("Wieder ein (bewegt)", WallFadeTuning.ExitDwellMoved, 0.5f, 0.1f, 60f,
            v => $"{v:0.0}s");
        AddWallFadeRow("Wieder ein (ruhig)", WallFadeTuning.ExitDwellStationary, 0.5f, 0.1f, 120f,
            v => $"{v:0.0}s");

        _rowParent = null;
        _rowGate = null;

        // The hover bubble the "…_note" paragraphs moved into. Built LAST so it is the canvas
        // root's last sibling (drawn over the rows) and still inside the subtree VRLayers.Apply
        // re-layers a few lines below.
        BuildTooltip();

        // Single visibility pass: each gated row shown iff its own predicate (category, or
        // category + selected element) is satisfied. Never more than one category's rows
        // are visible at once, so the pane stays a short, scannable list.
        _refreshers.Add(() =>
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                GameObject go = _rows[i];
                // A row can be Unity-null if its holder was destroyed out from under us
                // (scene unload) while the panel instance survived; skip rather than throw.
                if (go == null)
                    continue;
                bool show = _rowVisible[i]();
                if (go.activeSelf != show)
                    go.SetActive(show);
            }
        });

        // Mod layer in VR (inline 5s remain the dev-sim fallback; CAMERA-POLICY §2).
        if (_holder == null)
            return;
        VRLayers.Apply(_holder.gameObject);
        _holder.gameObject.SetActive(false);
    }

    // ---- sidebar + content scaffold (2026-07 redesign) ----------------------------------------

    /// <summary>Set the ambient row gate to "this sidebar category is selected".</summary>
    private void GateCat(NavCat c) => _rowGate = () => _navCat == (int)c;

    /// <summary>
    /// Localized sidebar/breadcrumb name of a top-level category. Every one of the five goes
    /// through <see cref="Loc"/> — the sidebar used to hand out two German literals ("Welt",
    /// "Debug") that an English player got in German.
    /// </summary>
    private static string NavCatLabel(NavCat c) => c switch
    {
        NavCat.Komfort => Loc.Mod("comfort"),
        NavCat.Grafik => Loc.Mod("cat_graphics"),
        NavCat.Tafeln => Loc.Mod("cat_panels"),
        NavCat.Avatar => Loc.Mod("avatar"),
        NavCat.Debug => Loc.Mod("cat_debug"),
        _ => c.ToString(),
    };

    /// <summary>
    /// Build the two-column body: a fixed-width category sidebar on the left and a
    /// flexible-width content pane on the right, both riding the root's vertical layout.
    /// </summary>
    private void BuildBody(out RectTransform sidebar, out RectTransform content)
    {
        var bodyGo = new GameObject("Body") { layer = 5 };
        bodyGo.AddComponent<RectTransform>();
        bodyGo.transform.SetParent(_root!.transform, worldPositionStays: false);
        var h = bodyGo.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childAlignment = TextAnchor.UpperLeft;
        var bodyRect = (RectTransform)bodyGo.transform;

        sidebar = BuildColumn(bodyRect, "Sidebar", 132f);
        content = BuildColumn(bodyRect, "Content", 0f);
    }

    /// <summary>One body column (VerticalLayoutGroup). fixedWidth &gt; 0 pins the width (sidebar); 0 = flexible (content).</summary>
    private static RectTransform BuildColumn(RectTransform parent, string name, float fixedWidth)
    {
        var go = new GameObject(name) { layer = 5 };
        var rect = go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, worldPositionStays: false);
        if (fixedWidth > 0f)
        {
            // Subtle inset behind the sidebar so it reads as a distinct nav column.
            var img = go.AddComponent<Image>();
            img.color = new Color(0.04f, 0.04f, 0.07f, 0.85f);
            img.raycastTarget = false;
        }
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4f;
        v.padding = new RectOffset(fixedWidth > 0f ? 6 : 4, fixedWidth > 0f ? 6 : 4, 4, 4);
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childAlignment = TextAnchor.UpperLeft;
        var le = go.AddComponent<LayoutElement>();
        if (fixedWidth > 0f)
        {
            // Issue 1 (sidebar buttons rescale on page change): PIN the sidebar column so the
            // content pane's per-page ContentSizeFitter can never re-fit it. minWidth == preferredWidth
            // fixes the width in EVERY HorizontalLayoutGroup regime (surplus goes only to flexible
            // children, and the min==pref clamp survives the shrink regime), and flexibleWidth/Height = 0
            // guarantees the column is never stretched to the taller content pane's width OR height —
            // so the nav buttons render pixel-identical whatever page is open.
            le.preferredWidth = fixedWidth;
            le.minWidth = fixedWidth;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
        }
        else
        {
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;
        }
        return rect;
    }

    /// <summary>One sidebar category button: selects the tab (poke/laser) and marks the active one.</summary>
    private void BuildNavButton(int idx)
    {
        var row = Row(30f);
        (Button btn, TextMeshProUGUI txt) = Button(row, "", 0f, () =>
        {
            _navCat = idx;
            _subCatListOpen = false;  // switching tabs collapses both accordions
            _elementListOpen = false;
            RefreshAll();
        }, flexible: true);
        var img = (Image)btn.targetGraphic;
        _refreshers.Add(() =>
        {
            bool sel = _navCat == idx;
            txt.text = NavCatLabel((NavCat)idx);
            txt.fontStyle = sel ? FontStyles.Bold : FontStyles.Normal;
            img.color = sel
                ? new Color(0.28f, 0.34f, 0.48f, 0.98f)
                : new Color(0.15f, 0.16f, 0.21f, 0.95f);
        });
    }

    // ---- per-style categories (Hände / Figuren / Handgelenk) ----------------------------------

    /// <summary>
    /// Debug → Hände-Offsets (GLOBAL, both hands; per HAND STYLE) + the card-fan geometry. Each row
    /// edits the style CURRENTLY worn; the "Stil" row cycles it right here so all three sets are
    /// reachable without leaving the element. Live: VRHand.SyncVisualOffset re-reads the ACTIVE
    /// style's entries every frame (a style switch re-seats/rescales instantly too).
    /// </summary>
    private void BuildHandsCategory()
    {
        HandsConfig.Bind(); // idempotent — so the seat rows work even if the Hands module has not inited
        _rowGate = HandOffsetRowsVisible;
        var handsStyleRow = Row();
        Label(handsStyleRow, "Stil", 16f, flexible: true);
        CycleButton(handsStyleRow, 120f, HandStyleLabel, CycleHandStyle);
        _rowGate = HandOffsetRowsVisible;
        var handsNote = Row(18f);
        Label(handsNote, "Alle Werte gelten pro Stil", 12f, flexible: true);

        AddStyleStepper(Loc.Mod("size"), () => Plugin.HandStyleScale, 0.02f, 0.2f, 3f,
            v => $"{v:0.00}x", HandOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("hand_x"), () => HandsConfig.StyleSeatLateral, 0.002f, -0.3f, 0.3f,
            v => $"{v * 1000f:0}mm", HandOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("hand_y"), () => HandsConfig.StyleSeatVertical, 0.002f, -0.3f, 0.3f,
            v => $"{v * 1000f:0}mm", HandOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("hand_z"), () => HandsConfig.StyleSeatForward, 0.002f, -0.3f, 0.3f,
            v => $"{v * 1000f:0}mm", HandOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("hand_pitch"), () => HandsConfig.StyleSeatPitch, 1f, -90f, 90f,
            v => $"{v:0}°", HandOffsetRowsVisible);

        // Card fan geometry ([Cards] fan entries, GLOBAL) — a labeled sub-section of Hände-Offsets.
        // Live-applied by CardsDriver (relayout) so tuning updates the open fan immediately.
        _rowGate = HandOffsetRowsVisible;
        Section(Loc.Mod("cat_fan"));
        AddFanStepper(Loc.Mod("fan_step"), CardsConfig.FanPerCardStepDegrees, 1f, 2f, 40f, v => $"{v:0}°");
        AddFanStepper(Loc.Mod("fan_arc"), CardsConfig.FanArcSweepDegrees, 1f, 20f, 180f, v => $"{v:0}°");
        AddFanStepper(Loc.Mod("fan_radius"), CardsConfig.FanEffectiveRadius, 0.002f, 0.05f, 0.4f,
            v => $"{v * 1000f:0}mm");
        AddFanStepper(Loc.Mod("fan_split"), CardsConfig.FanHoverSplitScale, 0.05f, 0.5f, 3f, v => $"{v:0.00}x");
        // Palm-gate reveal roll angle + fan-out animation length (all live).
        AddFanStepper("Aufdecken ein °", CardsConfig.RevealEnterDegrees, 5f, 15f, 85f, v => $"{v:0}°");
        AddFanStepper("Aufdecken aus °", CardsConfig.RevealExitDegrees, 5f, 5f, 80f, v => $"{v:0}°");
        AddFanStepper("Öffnungszeit", CardsConfig.FanOpenDuration, 0.02f, 0f, 0.6f, v => $"{v * 1000f:0}ms");
        // Depth curvature: side cards recede into (or bulge OUT of) depth with many cards (live).
        // User 4: min is −0.12 so the curvature can also go the OTHER direction (a separate worker
        // widens the bind's own clamp range to match).
        AddFanStepper("Krümmung Tiefe", CardsConfig.FanSideDepthCurve, 0.005f, -0.12f, 0.12f, v => $"{v * 1000f:0}mm");
        AddFanStepper("Krümmung Kurve", CardsConfig.FanCurvePower, 0.1f, 0.5f, 4f, v => $"{v:0.0}");
        _rowGate = HandOffsetRowsVisible;
        var curveMinRow = Row();
        Label(curveMinRow, "Krümmung ab Karten", 16f, flexible: true);
        MiniStepper(curveMinRow,
            () => $"{CardsConfig.FanCurveMinCards.Value}",
            d =>
            {
                ConfigEntry<int> e = CardsConfig.FanCurveMinCards;
                e.Value = Mathf.Clamp(e.Value + (int)Mathf.Sign(d), 1, 12);
            });
        // Card presentation (edge-read fix, see CardFan's "card presentation" region): how squarely
        // each card is aimed at the head, and how far the depth-bow apex follows the gaze so the card
        // being LOOKED AT is the one at the front of the curve instead of the one furthest back.
        // Both re-lay the open fan live through CardFan's per-frame param signature.
        AddFanStepper(Loc.Mod("fan_face_viewer"), CardsConfig.FanFaceViewer, 0.05f, 0f, 1f,
            v => $"{v * 100f:0}%");
        AddFanStepper(Loc.Mod("fan_gaze_follow"), CardsConfig.FanGazeApexFollow, 0.05f, 0f, 1f,
            v => $"{v * 100f:0}%");
        AddFanStepper(Loc.Mod("fan_gaze_smooth"), CardsConfig.FanGazeSmoothing, 1f, 1f, 30f,
            v => $"{v:0}/s");
        _rowGate = HandOffsetRowsVisible;
        var gazeRow = Row();
        Label(gazeRow, "Blick-Neigung", 16f, flexible: true);
        ToggleButton(gazeRow, () => CardsConfig.FanGazeBias.Value, v => CardsConfig.FanGazeBias.Value = v);

        // GHOST HAND ([Hands] GhostHandOnFan/GhostHandStrength): while the fan is open, the hand
        // holding it fades so it stops covering card details. Lives in the Kartenfächer
        // sub-section because that is when it happens, and it is the fan the player is looking at
        // when they want it. Both rows live-apply — HandGhosts re-reads the entries every frame
        // (toggle: engage/release next tick; strength: the installed material copies are just
        // re-tinted, no rebuild) — and the state carries to the mirror + multiplayer avatars.
        _rowGate = HandOffsetRowsVisible;
        var ghostRow = Row();
        Label(ghostRow, Loc.Mod("ghost_hand"), 16f, flexible: true);
        ToggleButton(ghostRow, () => HandsConfig.GhostHandOnFan.Value,
            v => HandsConfig.GhostHandOnFan.Value = v);
        AddFanStepper(Loc.Mod("ghost_strength"), HandsConfig.GhostHandStrength, 0.05f,
            HandGhosts.MinStrength, HandGhosts.MaxStrength, v => $"{v * 100f:0}%");
    }

    /// <summary>
    /// Debug → Figuren-Offsets (GLOBAL across boards; per HAND STYLE): the HELD board-figure pose
    /// docked between thumb and index of the hand MESH (geometry differs per style). Live:
    /// FigureGrabConfig re-poses the held mini immediately. "Aufrecht" is a global MODE toggle.
    /// </summary>
    private void BuildFiguresCategory()
    {
        FigureGrabConfig.Bind();
        _rowGate = FigureOffsetRowsVisible;
        var figStyleRow = Row();
        Label(figStyleRow, "Stil", 16f, flexible: true);
        CycleButton(figStyleRow, 120f, HandStyleLabel, CycleHandStyle);
        _rowGate = FigureOffsetRowsVisible;
        var figNote = Row(18f);
        Label(figNote, "Alle Werte gelten pro Stil", 12f, flexible: true);

        AddFigureToggle(Loc.Mod("fig_upright"), FigureGrabConfig.HeldUpright);
        AddStyleStepper(Loc.Mod("fig_x"), () => FigureGrabConfig.StyleHeldOffsetSide, 0.002f,
            -0.2f, 0.2f, v => $"{v * 1000f:0}mm", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_y"), () => FigureGrabConfig.StyleHeldOffsetUp, 0.002f,
            -0.2f, 0.2f, v => $"{v * 1000f:0}mm", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_z"), () => FigureGrabConfig.StyleHeldOffsetForward, 0.002f,
            -0.2f, 0.2f, v => $"{v * 1000f:0}mm", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_tilt"), () => FigureGrabConfig.StyleHeldTiltDegrees, 5f,
            -180f, 180f, v => $"{v:0}°", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_yaw"), () => FigureGrabConfig.StyleHeldFaceYawDegrees, 5f,
            -180f, 180f, v => $"{v:0}°", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_scale"), () => FigureGrabConfig.StyleHeldScale, 0.1f,
            0.2f, 5f, v => $"{v:0.00}x", FigureOffsetRowsVisible);
    }

    /// <summary>
    /// Debug → Handgelenk (per HAND STYLE): the wrist overview HUD's TILT (pitch/yaw/roll) and
    /// POSITION offset (X/Y/Z). The accessors read/write the ACTIVE hand style's [WristHud] entry, and
    /// WristHud re-applies every Tick — so both a nudge and a style switch move the HUD immediately.
    /// </summary>
    private void BuildWristCategory()
    {
        _rowGate = WristRowsVisible;
        var wristStyleRow = Row();
        Label(wristStyleRow, "Stil", 16f, flexible: true);
        CycleButton(wristStyleRow, 120f, HandStyleLabel, CycleHandStyle);
        _rowGate = WristRowsVisible;
        var wristNote = Row(18f);
        Label(wristNote, "Alle Werte gelten pro Stil", 12f, flexible: true);

        AddWristStepper(Loc.Mod("wrist_pitch"), () => WristHud.PitchDeg, v => WristHud.PitchDeg = v, 2f, v => $"{v:0}°");
        AddWristStepper(Loc.Mod("wrist_yaw"),   () => WristHud.YawDeg,   v => WristHud.YawDeg = v,   2f, v => $"{v:0}°");
        AddWristStepper(Loc.Mod("wrist_roll"),  () => WristHud.RollDeg,  v => WristHud.RollDeg = v,  2f, v => $"{v:0}°");
        AddWristStepper(Loc.Mod("wrist_x"), () => WristHud.OffsetX, v => WristHud.OffsetX = v, 0.002f, v => $"{v * 1000f:0}mm");
        AddWristStepper(Loc.Mod("wrist_y"), () => WristHud.OffsetY, v => WristHud.OffsetY = v, 0.002f, v => $"{v * 1000f:0}mm");
        AddWristStepper(Loc.Mod("wrist_z"), () => WristHud.OffsetZ, v => WristHud.OffsetZ = v, 0.002f, v => $"{v * 1000f:0}mm");
    }

}
