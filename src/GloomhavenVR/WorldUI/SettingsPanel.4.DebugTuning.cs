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
    // ---- Debug — Board tuning (Part E) ---------------------------------------------------------

    /// <summary>
    /// The in-VR DEBUG MENU (Part E): live-tune every board-attached element PER BOARD and save
    /// to config. Built from the existing panel helpers; every stepper closes over the per-board
    /// <see cref="CardsConfig"/> entries, so writing a value both persists (BepInEx) AND live-applies
    /// (CardsDriver subscribes to each entry's SettingChanged). Every row is built once and shown by
    /// its own visibility predicate (Debug sub-category + selected element).
    /// </summary>
    private void BuildElementTuning()
    {
        // Issue 5: the "Board-Justierung aktivieren" toggle is GONE — board tuning is now ALWAYS
        // active. The deep tuning is instead tucked away under the dedicated "Debug" sidebar
        // category (issue 2), which is the real discoverability the user wanted. Settings audit
        // 2026-07: the [Cards] DebugMenu entry that used to gate this section is gone too — nothing
        // read it any more, so it was a dead key in the config file rather than a control.

        // Sub-category chooser — ACCORDION (user 5b: cluster the ~20 Debug elements so the pane
        // stays scannable). Shown for the whole Debug category; the header row shows the selected
        // sub-category, pressing it expands one option row PER sub-category right below, pressing an
        // option selects it (and resets the element chooser to that sub-category's first element).
        // Plain uGUI buttons via the shared helpers, so poke AND laser keep working unchanged.
        _rowGate = PerBoard;
        var subCatRow = Row();
        Label(subCatRow, "Bereich", 16f, flexible: true);
        CycleButton(subCatRow, 170f,
            () => DebugSubCatLabel(CurrentSubCat) + (_subCatListOpen ? " -" : " +"),
            () => _subCatListOpen = !_subCatListOpen);

        for (int i = 0; i < DebugSubCatCount; i++)
        {
            int idx = i; // capture per row
            _rowGate = () => _subCatListOpen && PerBoard();
            var subOptRow = Row(28f);
            Label(subOptRow, "", 13f); // fixed 28px gutter — reads as an indented sub-row
            (Button _, TextMeshProUGUI subOptText) = Button(subOptRow, "", 0f, () =>
            {
                _debugSubCat = idx;
                _subCatListOpen = false; // select + collapse
                _elementListOpen = false; // the element chooser now scopes to the new sub-category
                RefreshAll();
            }, flexible: true);
            _refreshers.Add(() =>
            {
                bool selected = Mathf.Clamp(_debugSubCat, 0, DebugSubCatCount - 1) == idx;
                subOptText.text = (selected ? "> " : "") + DebugSubCatLabel((DebugSubCat)idx);
            });
        }

        // WHICH BOARD THIS PANE IS TUNING — a READOUT plus a pointer, no longer a second selector.
        //
        // This row used to be a Board cycle identical to the one in the user-facing settings, i.e.
        // the SAME [Cards] Board entry driven from two places. The user's rule (2026-07) is that
        // choosing the board is a USER FEATURE and Debug is only ever the optional power-user
        // surface on top of it, so the selection lives exactly once — under Avatar → Kontrollbrett
        // — and Debug states which board its per-board offsets/sizes/spacings are currently
        // editing, with a button that takes you to the picker. One writer, no two controls fighting
        // over one entry, and the tuning workflow (switch board → tune it) is still two presses.
        //
        // Hidden for the GLOBAL ButtonTuning elements (Rundenknöpfe / Zahnrad & Fixiert /
        // Knopf-Farben etc. — their values ride every board, so naming a board would be a lie).
        _rowGate = () => PerBoard() && !ElementIsGlobalTuning(CurrentElement());
        var boardRow = Row();
        Label(boardRow, Loc.Mod("board"), 16f, flexible: true);
        TextMeshProUGUI tunedBoard = Label(boardRow, "", 16f, bold: true);
        tunedBoard.GetComponent<LayoutElement>().preferredWidth = 90f;
        _refreshers.Add(() => tunedBoard.text = ControlBoardLabel());
        Button(boardRow, Loc.Mod("board_pick_in_avatar"), 190f, () =>
        {
            _navCat = (int)NavCat.Avatar; // jump to the ONE place the board is chosen
            _subCatListOpen = false;
            _elementListOpen = false;
            RefreshAll();
        });

        // Element chooser — ACCORDION scoped to the selected SUB-CATEGORY (user 5b): the header row
        // shows the selected element; pressing it expands one option row PER element of the CURRENT
        // SUB-CATEGORY right below (the panel's ContentSizeFitter grows around them, exactly like
        // every conditional debug row); pressing an option selects it and collapses the list. Hidden
        // for single-element sub-categories (Welt-Tuning) where a chooser would be a no-op. Plain uGUI
        // buttons via the shared helpers, so poke AND laser keep working unchanged.
        _rowGate = () => PerBoard() && CurrentSubCatElements.Length > 1;
        var elemRow = Row();
        Label(elemRow, Loc.Mod("element"), 16f, flexible: true);
        CycleButton(elemRow, 150f,
            () => DebugElementLabel(CurrentElement()) + (_elementListOpen ? " -" : " +"),
            () => _elementListOpen = !_elementListOpen);

        // One option row per possible element slot (built once for the LARGEST sub-category; each
        // row's visibility predicate + label refresher re-scope it to the current sub-category, so
        // sub-category switches never rebuild anything).
        int maxElements = 0;
        foreach (DebugElement[] els in SubCatElements)
        {
            if (els.Length > maxElements)
                maxElements = els.Length;
        }
        for (int i = 0; i < maxElements; i++)
        {
            int idx = i; // capture per row
            _rowGate = () => _elementListOpen && PerBoard()
                             && CurrentSubCatElements.Length > 1
                             && idx < CurrentSubCatElements.Length;
            var optRow = Row(28f);
            Label(optRow, "", 13f); // fixed 28px gutter — reads as an indented sub-row
            (Button _, TextMeshProUGUI optText) = Button(optRow, "", 0f, () =>
            {
                DebugElement[] els = CurrentSubCatElements;
                if (idx < els.Length)
                    _subCatElement[Mathf.Clamp(_debugSubCat, 0, DebugSubCatCount - 1)] = idx;
                _elementListOpen = false; // select + collapse
                RefreshAll();
            }, flexible: true);
            _refreshers.Add(() =>
            {
                DebugElement[] els = CurrentSubCatElements;
                if (idx >= els.Length)
                    return; // row is hidden by its predicate anyway
                bool selected =
                    Mathf.Clamp(_subCatElement[Mathf.Clamp(_debugSubCat, 0, DebugSubCatCount - 1)], 0, els.Length - 1) == idx;
                optText.text = (selected ? "> " : "") + DebugElementLabel(els[idx]);
            });
        }

        // X / Y / Z offset steppers (mm), each drives the selected element's active-board offset.
        AddOffsetStepper("X", 0);
        AddOffsetStepper("Y", 1);
        AddOffsetStepper("Z (proud)", 2);

        // Size / Scale stepper — hidden for elements with no size (Overlays, Initiative).
        _rowGate = () => PerBoard() && ElementHasSize(CurrentElement());
        var sizeRow = Row();
        Label(sizeRow, Loc.Mod("size"), 16f, flexible: true);
        MiniStepper(sizeRow, FormatSize, StepSize);

        // Objectives-only WIDTH stepper (user request): widen the scenario TASK dock so its
        // progress bar reads longer. Separate from the uniform "Größe" scale above — this scales
        // ONLY the fit width budget (the panel grows leftward into open space). mm readout of the
        // effective width budget, consistent with the offset rows.
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Objectives;
        var objWidthRow = Row();
        Label(objWidthRow, "Breite", 16f, flexible: true);
        MiniStepper(objWidthRow, FormatObjectivesWidth, StepObjectivesWidth);

        // Spacing stepper — group gap (Rest disc gap / Confirm-Undo gap / inter-pile gap; Active COL step).
        _rowGate = () => PerBoard() && ElementHasSpacing(CurrentElement());
        var spacingRow = Row();
        Label(spacingRow, Loc.Mod("spacing"), 16f, flexible: true);
        MiniStepper(spacingRow, FormatSpacing, StepSpacing);

        // Active-only ROW step stepper (the grid's vertical spacing).
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Active;
        var rowGapRow = Row();
        Label(rowGapRow, Loc.Mod("row_gap"), 16f, flexible: true);
        MiniStepper(rowGapRow, FormatActiveRowStep, StepActiveRowStep);

        // Shape cycle — shown only for the button GROUPS (Rest / Generic): flip Round <-> Square.
        _rowGate = () => PerBoard() && ElementHasShape(CurrentElement());
        var shapeRow = Row();
        Label(shapeRow, Loc.Mod("shape"), 16f, flexible: true);
        CycleButton(shapeRow, 100f, FormatShape, FlipShape);

        // Board-only Tilt / Yaw row (shown only when Element == Board).
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Board;
        var tiltYawRow = Row();
        Label(tiltYawRow, Loc.Mod("tilt_yaw"), 16f, flexible: true);
        MiniStepper(tiltYawRow,
            () => $"{CardsConfig.BoardTilt(CardsConfig.CurrentBoard).Value:0}°",
            d =>
            {
                ConfigEntry<float> e = CardsConfig.BoardTilt(CardsConfig.CurrentBoard);
                e.Value = Mathf.Clamp(e.Value + d * 1f, 0f, 90f);
            });
        MiniStepper(tiltYawRow,
            () => $"{CardsConfig.BoardYaw(CardsConfig.CurrentBoard).Value:0}°",
            d =>
            {
                ConfigEntry<float> e = CardsConfig.BoardYaw(CardsConfig.CurrentBoard);
                e.Value += d * 5f;
            });

        // Initiative-only 3D depth row (shown only when Element == Initiative): live-tune the
        // initiative track's front-to-back portrait spread cap ([WorldUI] InitiativeDepthMaxSpreadPx,
        // px). InitiativeTrackSurface.NormalizeDepth re-reads it every tick and re-clamps, so the
        // recession updates live (0 = flat). Analogous to the Board Tilt/Yaw row above.
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Initiative;
        var initDepthRow = Row();
        Label(initDepthRow, "3D-Tiefe", 16f, flexible: true);
        MiniStepper(initDepthRow,
            () => $"{WorldUIConfig.InitiativeDepthMaxSpreadPx.Value:0}px",
            d =>
            {
                ConfigEntry<float> e = WorldUIConfig.InitiativeDepthMaxSpreadPx;
                e.Value = Mathf.Clamp(e.Value + d * 1f, 0f, 40f);
            });

        // Initiative-only ENEMY-INFO CLEARANCE row (shown only when Element == Initiative): the
        // GLOBAL [WorldUI] EnemyRevealBoardClearance — how far the enemy round reveal must clear
        // the control board's top edge (real cm, measured AT the board). The reveal's content IS
        // the initiative track's enemyCardsHolder, so it belongs to this element; GLOBAL but edited
        // from an element's rows exactly like the 3D-depth row above (the clearance is derived from
        // the board's LIVE measured pose, so it already rides every board — no per-board entry).
        // EnemyRevealSurface re-reads the entry on every plant/follow step, so the next reveal (and
        // the next lazy-follow glide of a standing one) uses the new value; BepInEx persists on set.
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Initiative;
        var revealClearRow = Row();
        Label(revealClearRow, "Gegnerinfo-Abstand", 16f, flexible: true);
        MiniStepper(revealClearRow,
            () => $"{WorldUIConfig.EnemyRevealBoardClearance.Value * 100f:0}cm",
            d =>
            {
                ConfigEntry<float> e = WorldUIConfig.EnemyRevealBoardClearance;
                e.Value = Mathf.Clamp(e.Value + d * 0.01f, 0f, 0.5f);
            });

        // Decision-only ROW-GAP row (shown only when Element == Decision): the GLOBAL
        // [WorldUI] DecisionRowGapPx — target vertical gap (uGUI px) between a docked
        // decision prompt's text block and its button row. GLOBAL but edited from this
        // element's rows (precedent: the initiative 3D-depth row above). Live:
        // DecisionDockSurface subscribes SettingChanged and re-compresses an OPEN dock
        // immediately; BepInEx persists on set.
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Decision;
        var decisionGapRow = Row();
        Label(decisionGapRow, "Entscheidungs-Abstand", 16f, flexible: true);
        MiniStepper(decisionGapRow,
            () => $"{WorldUIConfig.DecisionRowGapPx.Value:0}px",
            d =>
            {
                ConfigEntry<float> e = WorldUIConfig.DecisionRowGapPx;
                e.Value = Mathf.Clamp(e.Value + d * 4f, 0f, 120f);
            });

        // Piles-only BROWSE-fan anchor rows (shown only when Element == Piles): live-tune where
        // the poke-toggle pile browse fan floats above the board — [Cards] BrowseFanOffset,
        // board-local meters ADDED to the fixed above-board base. GLOBAL (not per-board): the
        // anchor is board-LOCAL, so it already rides each board's pose/scale — precedent: the
        // global initiative 3D-depth row above. PileBrowser.Tick re-reads the entry every frame
        // while a browse fan is open, so nudging a stepper moves the open fan immediately.
        AddBrowseOffsetStepper("Browse X", 0);
        AddBrowseOffsetStepper("Browse Y", 1);
        AddBrowseOffsetStepper("Browse Z", 2);

        // Reset element (element-bearing categories only — resets the selected element). Hidden for
        // the per-style folded-in elements (Hände-Offsets / Figuren-Offsets / Handgelenk): they carry
        // no per-board offset entry to reset and never had a reset control as top-level tabs, and
        // for the config-browser topics (2026-07), which reset nothing — a per-entry reset there
        // would need one control per entry, and the entry's default is on hover instead. Hidden for
        // Zeitgeber / CPU for the same reason as the per-style elements: it owns no per-board entry,
        // so the button would be a control that visibly does nothing.
        _rowGate = () => PerBoard() && !ElementIsPerStyle(CurrentElement())
                         && !ElementIsConfigTopic(CurrentElement())
                         && CurrentElement() != DebugElement.Timing;
        var actionRow = Row();
        Button(actionRow, Loc.Mod("reset_element"), 0f, ResetDebugElement, flexible: true);

        // ButtonTuning elements of the "Tasten" category (request C + the 2026-07 category
        // split): three first-class GLOBAL elements, each bound to ITS OWN cfg section so a
        // value can never leak across button categories, and EVERY stepper shows a real
        // number (the 0="Auto" sentinel is gone — the numeric defaults ARE the authored
        // values). Rows show while their element is selected. Every entry live-applies
        // WITHOUT restart: writing bumps ButtonTuning.Version and PlayTray.TickStatus /
        // ButtonCluster.Tick rebuild the affected caps on their next tick. External writes
        // (cfg edit/reload) refresh the rows via ButtonTuning.Changed (subscribed in the
        // constructor) on top of the 0.25 s refresh cadence.
        ButtonTuning.Bind();

        // Tasten → Rundenknöpfe ([RoundButtons] — the transient turn-flow cluster).
        AddButtonTuningRow("Versatz X", ButtonTuning.RoundOffsetX, 0.005f, -0.30f, 0.30f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Versatz Y", ButtonTuning.RoundOffsetY, 0.005f, -0.30f, 0.30f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        // User: "the Rundenknöpfe have no Versatz Z" — out-of-plane offset, + = toward the player.
        AddButtonTuningRow("Versatz Z", ButtonTuning.RoundOffsetZ, 0.005f, -0.30f, 0.30f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        // Shape cycle for the transient group (Round puck <-> Square keycap).
        if (ButtonTuning.RoundShape != null)
        {
            ConfigEntry<ButtonShape> shapeEntry = ButtonTuning.RoundShape;
            _rowGate = RoundButtonRowsVisible;
            var transientShapeRow = Row();
            Label(transientShapeRow, Loc.Mod("shape"), 16f, flexible: true);
            CycleButton(transientShapeRow, 100f,
                () => ShapeLabel(shapeEntry.Value),
                () => shapeEntry.Value = shapeEntry.Value == ButtonShape.Round
                    ? ButtonShape.Square
                    : ButtonShape.Round);
        }
        AddButtonTuningRow("Kappengröße", ButtonTuning.RoundCapSize, 0.002f, 0.015f, 0.09f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Breite", ButtonTuning.RoundWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Höhe", ButtonTuning.RoundHeight, 0.005f, 0.015f, 0.20f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Tiefe", ButtonTuning.RoundDepth, 0.002f, 0.006f, 0.08f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Hub", ButtonTuning.RoundTravel, 0.001f, 0.002f, 0.02f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);

        // Debug → Generisch ([BoardButtons] — the Confirm/Undo keycaps). Issue 6: these are the
        // SAME physical caps as the Generic element, so the exact rectangle side lengths (independent
        // Breite/Höhe) plus Tiefe/Hub now appear right under "Generisch" — no separate Boardtasten
        // element. For SQUARE Confirm/Undo (the default GenericButtonShape) these Width/Height are the
        // authoritative cap size (PlayTray.BuildButtons); the round "Größe" diameter row above only
        // drives the Round shape, exactly like the Rest element pairs a diameter with [RestButtons] W/H.
        AddButtonTuningRow("Breite", ButtonTuning.BoardWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardButtonRowsVisible);
        AddButtonTuningRow("Höhe", ButtonTuning.BoardHeight, 0.005f, 0.015f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardButtonRowsVisible);
        AddButtonTuningRow("Tiefe", ButtonTuning.BoardDepth, 0.002f, 0.006f, 0.08f,
            v => $"{v * 1000f:0}mm", BoardButtonRowsVisible);
        AddButtonTuningRow("Hub", ButtonTuning.BoardTravel, 0.001f, 0.002f, 0.02f,
            v => $"{v * 1000f:0}mm", BoardButtonRowsVisible);

        // Tasten → Zahnrad & Fixiert ([BoardDashboard] — gear + follow/pin plates; the two
        // plates are authored at different widths, so each keeps its own width row).
        AddButtonTuningRow("Breite Zahnrad", ButtonTuning.DashGearWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);
        AddButtonTuningRow("Breite Fixiert", ButtonTuning.DashPinWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);
        AddButtonTuningRow("Höhe", ButtonTuning.DashHeight, 0.005f, 0.015f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);
        AddButtonTuningRow("Tiefe", ButtonTuning.DashDepth, 0.002f, 0.006f, 0.08f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);
        AddButtonTuningRow("Hub", ButtonTuning.DashTravel, 0.001f, 0.002f, 0.02f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);

        // Tasten → Ruhetasten ([RestButtons] — short/long rest keycaps; square caps use
        // W/H, round discs keep the per-board diameter row above; depth/travel both shapes).
        AddButtonTuningRow("Breite", ButtonTuning.RestWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", RestButtonTuningRowsVisible);
        AddButtonTuningRow("Höhe", ButtonTuning.RestHeight, 0.005f, 0.015f, 0.20f,
            v => $"{v * 1000f:0}mm", RestButtonTuningRowsVisible);
        AddButtonTuningRow("Tiefe", ButtonTuning.RestDepth, 0.002f, 0.006f, 0.08f,
            v => $"{v * 1000f:0}mm", RestButtonTuningRowsVisible);
        AddButtonTuningRow("Hub", ButtonTuning.RestTravel, 0.001f, 0.002f, 0.02f,
            v => $"{v * 1000f:0}mm", RestButtonTuningRowsVisible);

        // Tasten → Knopf-Farben ([ButtonColors] — user 4 + user 1): the keycap LABEL text colour +
        // engrave outline + underlay shadow, and a per-category cap-FACE tint. The PRIMARY control is
        // now a LASER-CLICKABLE COLOUR PALETTE per target (user 1: "change the colour directly with
        // the laser instead of RGB values"). AddColorPalette lays out a grid of swatch buttons on the
        // panel canvas (same Image/Button interaction path as every other control, so poke + laser
        // both work); clicking a swatch writes its RGB into the target's three *R/G/B binds at once,
        // which bumps ButtonTuning.Version → NativeButtonSkin re-skins the caps live (keycaps on the
        // Version rebuild, cluster per-tick). The two engrave toggles + Konturbreite stay as steppers;
        // the reset case (restores every bind) is unchanged.
        _rowGate = ButtonColorRowsVisible;
        Section("Text");
        AddColorPalette("Text-Farbe", ButtonTuning.LabelR, ButtonTuning.LabelG, ButtonTuning.LabelB,
            ColorSwatches, ButtonColorRowsVisible);
        AddColorToggle("Kontur", ButtonTuning.LabelOutline);
        AddColorPalette("Kontur-Farbe", ButtonTuning.LabelOutlineR, ButtonTuning.LabelOutlineG,
            ButtonTuning.LabelOutlineB, ColorSwatches, ButtonColorRowsVisible);
        AddButtonTuningRow("Konturbreite", ButtonTuning.LabelOutlineW, 0.05f, 0f, 1f,
            v => $"{v:0.00}", ButtonColorRowsVisible);
        AddColorToggle("Schatten", ButtonTuning.LabelUnderlay);

        // Cap-face tints are MULTIPLIERS (1,1,1 = identity/unchanged). A palette of tint colours works
        // the same way — the swatch writes the three tint channels — and "Weiß" (1,1,1) is the neutral
        // no-tint swatch (which is also each cap tint's default, so it shows highlighted at rest).
        _rowGate = ButtonColorRowsVisible;
        Section("Kappen (Tönung)");
        AddColorPalette("Board-Kappe", ButtonTuning.BoardCapTintR, ButtonTuning.BoardCapTintG,
            ButtonTuning.BoardCapTintB, ColorSwatches, ButtonColorRowsVisible);
        AddColorPalette("Zahnrad-Kappe", ButtonTuning.DashCapTintR, ButtonTuning.DashCapTintG,
            ButtonTuning.DashCapTintB, ColorSwatches, ButtonColorRowsVisible);
        AddColorPalette("Zugleiste-Kappe", ButtonTuning.ClusterCapTintR, ButtonTuning.ClusterCapTintG,
            ButtonTuning.ClusterCapTintB, ColorSwatches, ButtonColorRowsVisible);
        AddColorPalette("Rast-Kappe", ButtonTuning.RestCapTintR, ButtonTuning.RestCapTintG,
            ButtonTuning.RestCapTintB, ColorSwatches, ButtonColorRowsVisible);
    }

    /// <summary>
    /// Shared colour palette (user 1) for the Knopf-Farben swatch grids. Covers what the keycaps
    /// need: bright parchment/near-white (= the LabelR/G/B default), warm golds, dark umber/near-black
    /// (= the LabelOutline default, and dark caps so light text reads), wood browns, brass, neutral
    /// greys, and a small spectrum. "Weiß" (1,1,1) is the cap-tint identity/default. Comment names are
    /// documentation only; the swatches render as pure colour chips.
    /// </summary>
    private static readonly Color[] ColorSwatches =
    {
        new(0.984f, 0.953f, 0.878f), // Pergament (= LabelR/G/B default)
        new(1f, 1f, 1f),             // Weiß (cap-tint identity / default)
        new(0.80f, 0.80f, 0.80f),    // Hellgrau
        new(0.50f, 0.50f, 0.50f),    // Grau
        new(0.25f, 0.25f, 0.25f),    // Dunkelgrau
        new(0.09f, 0.06f, 0.03f),    // Umbra (= LabelOutline default, near-black)
        new(0.02f, 0.02f, 0.02f),    // Schwarz
        new(0.85f, 0.65f, 0.13f),    // Gold
        new(0.95f, 0.82f, 0.45f),    // Helles Gold
        new(0.62f, 0.50f, 0.28f),    // Messing (brass)
        new(0.45f, 0.30f, 0.16f),    // Holz
        new(0.28f, 0.18f, 0.09f),    // Dunkles Holz
        new(0.75f, 0.18f, 0.15f),    // Rot
        new(0.25f, 0.55f, 0.28f),    // Grün
        new(0.25f, 0.40f, 0.65f),    // Blau
        new(0.90f, 0.55f, 0.20f),    // Bernstein
    };

    /// <summary>
    /// User 1: a LASER-CLICKABLE COLOUR PALETTE for one colour target (label fill, outline, or a cap
    /// tint). Builds a titled section then a grid of colour-chip buttons on the panel canvas — each a
    /// uGUI Image + Button (the SAME interaction path as every other panel button, so poke AND laser
    /// hit them). Clicking a swatch writes its RGB into the three *R/G/B binds AT ONCE (→ ButtonTuning
    /// .Version bump → live re-skin). The swatch whose colour currently matches the binds (within a
    /// small epsilon) shows a bright highlight frame, so the active choice is visible. Skipped whole if
    /// any bind failed to bind (config dir unwritable), matching the stepper-row policy. Rows gate to
    /// <paramref name="visible"/> (Debug → Knopf-Farben selected).
    /// </summary>
    private void AddColorPalette(string label, ConfigEntry<float>? rBind, ConfigEntry<float>? gBind,
        ConfigEntry<float>? bBind, Color[] swatches, Func<bool> visible)
    {
        if (rBind == null || gBind == null || bBind == null)
            return;

        _rowGate = visible;
        var titleRow = Row(22f);
        Label(titleRow, label, 14f, bold: true, flexible: true);

        const int perRow = 8;
        for (int start = 0; start < swatches.Length; start += perRow)
        {
            _rowGate = visible;
            var row = Row(30f);
            int end = Mathf.Min(start + perRow, swatches.Length);
            for (int i = start; i < end; i++)
            {
                Color c = swatches[i];
                BuildSwatch(row, c,
                    () => { rBind.Value = c.r; gBind.Value = c.g; bBind.Value = c.b; },
                    () => ColorMatches(rBind, gBind, bBind, c));
            }
        }
    }

    /// <summary>
    /// One colour chip: an outer border frame (highlighted while selected) around an inner Image +
    /// Button that carries the colour and the click. targetGraphic is the inner Image so poke/laser
    /// raycast the chip exactly like a normal panel button.
    /// </summary>
    private void BuildSwatch(RectTransform row, Color color, Action onClick, Func<bool> isSelected)
    {
        var outerGo = new GameObject("Swatch") { layer = 5 };
        outerGo.transform.SetParent(row, worldPositionStays: false);
        var border = outerGo.AddComponent<Image>();
        border.raycastTarget = false;
        var le = outerGo.AddComponent<LayoutElement>();
        le.preferredWidth = 34f;
        le.minWidth = 34f;
        le.flexibleWidth = 0f;

        var innerGo = new GameObject("Chip") { layer = 5 };
        innerGo.transform.SetParent(outerGo.transform, worldPositionStays: false);
        var chip = innerGo.AddComponent<Image>();
        chip.color = color;
        var button = innerGo.AddComponent<Button>();
        button.targetGraphic = chip;
        button.onClick.AddListener(() => Safe(() => { onClick(); RefreshAll(); }));
        var ir = (RectTransform)innerGo.transform;
        ir.anchorMin = Vector2.zero;
        ir.anchorMax = Vector2.one;
        ir.offsetMin = new Vector2(3f, 3f);
        ir.offsetMax = new Vector2(-3f, -3f);

        _refreshers.Add(() =>
            border.color = isSelected()
                ? new Color(1f, 0.92f, 0.35f, 1f)      // bright highlight frame = active choice
                : new Color(0.10f, 0.10f, 0.13f, 1f)); // subtle bezel otherwise
    }

    /// <summary>True while the three colour binds match a swatch's RGB within a small epsilon.</summary>
    private static bool ColorMatches(ConfigEntry<float>? r, ConfigEntry<float>? g, ConfigEntry<float>? b,
        Color c)
    {
        if (r == null || g == null || b == null)
            return false;
        const float eps = 0.02f;
        return Mathf.Abs(r.Value - c.r) < eps
               && Mathf.Abs(g.Value - c.g) < eps
               && Mathf.Abs(b.Value - c.b) < eps;
    }

    /// <summary>
    /// Knopf-Farben boolean row ([ButtonColors] LabelOutline / LabelUnderlay). Writing the entry
    /// bumps ButtonTuning.Version so NativeButtonSkin re-skins the caps live. Skipped if the bind
    /// failed (config dir unwritable). Shown while Debug → Knopf-Farben is selected.
    /// </summary>
    private void AddColorToggle(string label, ConfigEntry<bool>? entry)
    {
        if (entry == null)
            return;
        _rowGate = ButtonColorRowsVisible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        ToggleButton(row, () => entry.Value, v => entry.Value = v);
    }

    /// <summary>Elements that expose a Size/Scale stepper (Rest disc, Generic side, Active/Pile scale, Board scale, the two docks + the cluster).</summary>
    private static bool ElementHasSize(DebugElement e) =>
        e is DebugElement.Rest or DebugElement.Generic or DebugElement.Active
        or DebugElement.Piles or DebugElement.Board
        or DebugElement.Objectives or DebugElement.Elements or DebugElement.Cluster
        or DebugElement.Decision;

    /// <summary>
    /// Group elements that expose a Spacing stepper — every element that moves a PAIR/group of
    /// widgets together: both button groups, the two slot Overlays (item 1), the piles, the active grid.
    /// </summary>
    private static bool ElementHasSpacing(DebugElement e) =>
        e is DebugElement.Rest or DebugElement.Generic or DebugElement.Overlays
        or DebugElement.Piles or DebugElement.Active;

    /// <summary>The two button GROUPS carry a Round/Square shape toggle.</summary>
    private static bool ElementHasShape(DebugElement e) =>
        e is DebugElement.Rest or DebugElement.Generic;

    private void AddOffsetStepper(string label, int axis)
    {
        // Every per-board element carries a Vector3 offset — except the global ButtonTuning
        // elements: RoundButtons has its own [RoundButtons] Versatz X/Y/Z rows, and the two
        // board keycap elements have no offset at all (ElementOffsetEntry() is null there).
        _rowGate = () => PerBoard() && ElementOffsetEntry() != null;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row, () => FormatOffset(axis), d => StepOffset(axis, d));
    }

    /// <summary>
    /// Piles-element stepper row for ONE axis of the GLOBAL [Cards] BrowseFanOffset — the pile
    /// BROWSE fan's board-local anchor offset (added to its fixed above-board base). Writing the
    /// entry persists (BepInEx) and live-applies WITHOUT a SettingChanged hook: PileBrowser.Tick
    /// re-reads the entry every frame while a browse fan is open. Shown only for Panels → Piles.
    /// </summary>
    private void AddBrowseOffsetStepper(string label, int axis)
    {
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Piles;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () =>
            {
                Vector3 v = CardsConfig.BrowseFanOffset.Value;
                float c = axis == 0 ? v.x : axis == 1 ? v.y : v.z;
                return $"{c * 1000f:0}mm";
            },
            d =>
            {
                ConfigEntry<Vector3> e = CardsConfig.BrowseFanOffset;
                Vector3 v = e.Value;
                float s = d * 0.002f; // 2 mm per press, same feel as StepOffset
                if (axis == 0) v.x += s;
                else if (axis == 1) v.y += s;
                else v.z += s;
                e.Value = v; // persists; PileBrowser.Tick re-reads per frame (live)
            });
    }

    /// <summary>
    /// Wall-fade threshold stepper row under the Wände category. Bound directly to a live
    /// [WallFade] entry — writing persists (BepInEx) and applies on WallSegmentFade's next
    /// evaluation tick (the driver re-reads the clamped WallFadeTuning accessors every frame).
    /// </summary>
    private void AddWallFadeRow(string label, ConfigEntry<float>? entry, float step, float min,
        float max, Func<float, string> format)
    {
        if (entry == null)
            return; // WallFadeTuning.Bind() failed (config dir unwritable) — skip the row
        _rowGate = WallFadeRowsVisible; // issue 2: moved into Debug → Wandüberblendung
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>
    /// The GLOBAL ButtonTuning elements (per-board board selector is hidden for them).
    /// WallFade is global too (the wall-fade fractions ride no board). BoardButtons is no longer a
    /// standalone element (issue 6 — merged into Generic), so it is not listed. The three per-STYLE
    /// elements (Hände-Offsets / Figuren-Offsets / Handgelenk) are not per-board either, so they hide
    /// the board selector too.
    /// </summary>
    private static bool ElementIsGlobalTuning(DebugElement e) =>
        e is DebugElement.RoundButtons or DebugElement.BoardDashboard or DebugElement.WallFade
        or DebugElement.HandOffsets or DebugElement.FigureOffsets or DebugElement.WristOffsets
        or DebugElement.ButtonColors or DebugElement.Timing
        || ElementIsConfigTopic(e);

    /// <summary>
    /// The generic config browser's topics (2026-07). They are elements only in the sense that they
    /// ride the existing element chooser — they name no board-attached object at all, so every
    /// per-board row (board readout, offset/size/spacing/shape steppers, the element reset) must
    /// hide for them. Contiguous in <see cref="DebugElement"/> by construction, so this is a range
    /// test rather than a list that could fall out of date when a topic is added.
    /// </summary>
    private static bool ElementIsConfigTopic(DebugElement e) =>
        e >= DebugElement.CfgDiagnostics && e <= DebugElement.CfgOther;

    /// <summary>
    /// The per-HAND-STYLE Debug elements folded in from the former top-level tabs (2026-07). They
    /// carry NO per-board offset entry and NO generic size/spacing/shape rows, so the shared reset
    /// button (which resets a per-board element) hides for them — they never had a reset as tabs.
    /// </summary>
    private static bool ElementIsPerStyle(DebugElement e) =>
        e is DebugElement.HandOffsets or DebugElement.FigureOffsets or DebugElement.WristOffsets;

    /// <summary>Visibility of the per-style Hände-Offsets rows: Debug → Hände-Offsets element selected.</summary>
    private bool HandOffsetRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.HandOffsets;

    /// <summary>Visibility of the per-style Figuren-Offsets rows: Debug → Figuren-Offsets element selected.</summary>
    private bool FigureOffsetRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.FigureOffsets;

    /// <summary>Visibility of the per-style wrist-HUD rows: Debug → Handgelenk element selected.</summary>
    private bool WristRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.WristOffsets;

    /// <summary>Visibility of the [RoundButtons] rows: Debug → Rundenknöpfe element selected.</summary>
    private bool RoundButtonRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.RoundButtons;

    /// <summary>
    /// Visibility of the [BoardButtons] Confirm/Undo keycap geometry rows. Issue 6: these are the
    /// SAME physical Confirm/Undo caps as the Generic element, so the rectangle Width/Height/Depth/
    /// Travel steppers now surface under Debug → Generisch (there is no separate Boardtasten element).
    /// </summary>
    private bool BoardButtonRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.Generic;

    /// <summary>Visibility of the [BoardDashboard] rows: Debug → Zahnrad &amp; Fixiert element selected.</summary>
    private bool BoardDashboardRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.BoardDashboard;

    /// <summary>Visibility of the [RestButtons] geometry rows: Debug → Ruhetasten element selected.</summary>
    private bool RestButtonTuningRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.Rest;

    /// <summary>Visibility of the wall-fade fraction rows: Debug → Wandüberblendung element selected.</summary>
    private bool WallFadeRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.WallFade;

    /// <summary>
    /// Visibility of the timing/CPU + GPU A/B rows: Debug → Zeitgeber / CPU element selected.
    /// Before the 2026-07 restructure those rows had a CATEGORY-wide gate and therefore showed on
    /// every Debug page at once.
    /// </summary>
    private bool TimingRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.Timing;

    /// <summary>Visibility of the [ButtonColors] rows: Debug → Knopf-Farben element selected.</summary>
    private bool ButtonColorRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.ButtonColors;

    /// <summary>
    /// ButtonTuning stepper row bound directly to a live <see cref="ConfigEntry{T}"/> (mm
    /// readout, always a real number — no "Auto" sentinel any more; the entry defaults ARE
    /// the authored values). Writing persists (BepInEx) and live-applies: the entry's
    /// SettingChanged bumps ButtonTuning.Version and PlayTray/ButtonCluster rebuild the
    /// caps on their next tick. Skipped entirely if Bind() failed (config dir unwritable) —
    /// same policy as <see cref="AddWallFadeRow"/>. Shown while <paramref name="visible"/>
    /// says the row's Tasten element is selected.
    /// </summary>
    private void AddButtonTuningRow(string label, ConfigEntry<float>? entry, float step, float min,
        float max, Func<float, string> format, Func<bool> visible)
    {
        if (entry == null)
            return;
        _rowGate = visible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>
    /// Card-fan stepper bound to a global fan <see cref="ConfigEntry{T}"/>. Writing the entry
    /// persists (BepInEx) and live-applies (CardsDriver relayouts the open fan). Clamped to
    /// [min,max]. Shown under Debug → Hände-Offsets (Kartenfächer sub-section).
    /// </summary>
    private void AddFanStepper(string label, ConfigEntry<float> entry, float step, float min, float max,
        Func<float, string> format)
    {
        _rowGate = HandOffsetRowsVisible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>
    /// Per-STYLE stepper row: drives the entry of the style CURRENTLY worn (indexed by
    /// [Hands] HandStyle into the given per-style entry array), so the element only ever shows
    /// the active style's values — Debug → Hände-Offsets (scale + the four seat controls),
    /// Debug → Figuren-Offsets (held-mini pose) and any future per-style group all go through here.
    /// Writing persists (BepInEx) and live-applies (VRHand.SyncVisualOffset re-reads per
    /// frame; FigureGrabConfig re-poses via SettingChanged). Shown while the given
    /// <paramref name="visible"/> predicate says its Debug element is selected; shows "—" while the
    /// config is not bound yet.
    /// </summary>
    private void AddStyleStepper(string label, Func<ConfigEntry<float>[]?> entries, float step,
        float min, float max, Func<float, string> format,
        Func<bool> visible)
    {
        _rowGate = visible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () =>
            {
                ConfigEntry<float>? e = ActiveStyleEntry(entries);
                return e != null ? format(e.Value) : "—";
            },
            d =>
            {
                ConfigEntry<float>? e = ActiveStyleEntry(entries);
                if (e != null)
                    e.Value = Mathf.Clamp(e.Value + d * step, min, max);
            });
    }

    /// <summary>The active-style element of a per-style [Hands] entry array (null-safe).</summary>
    private static ConfigEntry<float>? ActiveStyleEntry(Func<ConfigEntry<float>[]?> entries)
    {
        try
        {
            ConfigEntry<float>[]? arr = entries();
            if (arr == null)
                return null;
            int style = (int)Hands.HandStyles.Clamp(
                Plugin.HandStyle != null ? (int)Plugin.HandStyle.Value : 0);
            return arr[style];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Item 10: a WRIST-HUD pose stepper row bound to a WristHud getter/setter (degrees for
    /// pitch/yaw/roll, mm for the X/Y/Z offset). Since the per-style rework (request B) the
    /// accessors read/write the ACTIVE hand style's entry, so these rows edit the style
    /// currently worn. Setting the value re-poses the wrist HUD live — WristHud.ApplyPose
    /// re-reads it every Tick — and persists via BepInEx. Shown only under the Wrist tab.
    /// </summary>
    private void AddWristStepper(string label, Func<float> get, Action<float> set, float step,
        Func<float, string> format)
    {
        _rowGate = WristRowsVisible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(get()),
            d => set(get() + d * step));
    }

    /// <summary>Held-figure boolean row (e.g. Upright) — same live-apply/persist path as the steppers.</summary>
    private void AddFigureToggle(string label, ConfigEntry<bool> entry)
    {
        _rowGate = FigureOffsetRowsVisible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        ToggleButton(row, () => entry.Value, v => entry.Value = v);
    }

    /// <summary>The offset ConfigEntry the selected element edits (all board-local Vector3s).</summary>
    private ConfigEntry<Vector3>? ElementOffsetEntry()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return CurrentElement() switch
        {
            DebugElement.Rest => CardsConfig.RestButtonOffset(b),
            DebugElement.Generic => CardsConfig.ConfirmUndoOffset(b),
            DebugElement.Overlays => CardsConfig.SlotOverlayOffset(b),
            DebugElement.Initiative => CardsConfig.InitiativeOffset(b),
            DebugElement.Active => CardsConfig.ActiveOffset(b),
            DebugElement.Piles => CardsConfig.PileOffset(b),
            DebugElement.Board => CardsConfig.BoardPosOffset(b),
            DebugElement.Objectives => CardsConfig.ObjectivesOffset(b),
            DebugElement.Elements => CardsConfig.ElementsOffset(b),
            DebugElement.VRSettings => CardsConfig.VRSettingsOffset(b),
            DebugElement.Pin => CardsConfig.PinOffset(b),
            DebugElement.Readout => CardsConfig.ReadoutOffset(b),
            DebugElement.Cluster => CardsConfig.ClusterOffset(b),
            DebugElement.Decision => CardsConfig.DecisionOffset(b),
            DebugElement.ItemUse => CardsConfig.ItemUseSlotOffset(b),
            DebugElement.ItemCard => CardsConfig.ItemCardOffset(b),
            _ => null,
        };
    }

    private string FormatOffset(int axis)
    {
        ConfigEntry<Vector3>? e = ElementOffsetEntry();
        if (e == null)
            return "-";
        Vector3 v = e.Value;
        float c = axis == 0 ? v.x : axis == 1 ? v.y : v.z;
        return $"{c * 1000f:0}mm";
    }

    private void StepOffset(int axis, int delta)
    {
        ConfigEntry<Vector3>? e = ElementOffsetEntry();
        if (e == null)
            return;
        Vector3 v = e.Value;
        float s = delta * 0.002f; // 2 mm per press
        if (axis == 0) v.x += s;
        else if (axis == 1) v.y += s;
        else v.z += s;
        e.Value = v; // persists + fires SettingChanged → CardsDriver live-applies
    }

    private string FormatSize()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return CurrentElement() switch
        {
            DebugElement.Rest => $"{CardsConfig.RestButtonDiameter(b).Value * 1000f:0}mm",
            DebugElement.Generic => $"{CardsConfig.ConfirmUndoSize(b).Value * 1000f:0}mm",
            DebugElement.Active => $"{CardsConfig.ActiveCardScale(b).Value:0.00}x",
            DebugElement.Piles => $"{CardsConfig.PileScale(b).Value:0.00}x",
            DebugElement.Board => $"{CardsConfig.BoardScale(b).Value:0.00}x",
            DebugElement.Objectives => $"{CardsConfig.ObjectivesScale(b).Value:0.00}x",
            DebugElement.Elements => $"{CardsConfig.ElementsScale(b).Value:0.00}x",
            DebugElement.Cluster => $"{CardsConfig.ClusterScale(b).Value:0.00}x",
            DebugElement.Decision => $"{CardsConfig.DecisionScale(b).Value:0.00}x",
            _ => "-",
        };
    }

    private void StepSize(int delta)
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        switch (CurrentElement())
        {
            case DebugElement.Rest:
            {
                ConfigEntry<float> e = CardsConfig.RestButtonDiameter(b);
                e.Value = Mathf.Max(0.01f, e.Value + delta * 0.002f);
                break;
            }
            case DebugElement.Generic:
            {
                ConfigEntry<float> e = CardsConfig.ConfirmUndoSize(b);
                e.Value = Mathf.Max(0.01f, e.Value + delta * 0.002f);
                break;
            }
            case DebugElement.Active:
            {
                ConfigEntry<float> e = CardsConfig.ActiveCardScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.02f, 0.3f, 2f);
                break;
            }
            case DebugElement.Piles:
            {
                ConfigEntry<float> e = CardsConfig.PileScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Board:
            {
                ConfigEntry<float> e = CardsConfig.BoardScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Objectives:
            {
                ConfigEntry<float> e = CardsConfig.ObjectivesScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Elements:
            {
                ConfigEntry<float> e = CardsConfig.ElementsScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Cluster:
            {
                ConfigEntry<float> e = CardsConfig.ClusterScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Decision:
            {
                ConfigEntry<float> e = CardsConfig.DecisionScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
        }
    }

    /// <summary>
    /// Objectives dock width readout (user request): the EFFECTIVE fit-width budget in mm —
    /// base <see cref="PlayTray.ObjectivesMountWidth"/> × the per-board <c>ObjectivesWidth</c>
    /// multiplier — with the multiplier appended so both the physical size and the factor read.
    /// </summary>
    private string FormatObjectivesWidth()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        float mult = CardsConfig.ObjectivesWidth(b).Value;
        return $"{PlayTray.ObjectivesMountWidth * mult * 1000f:0}mm ({mult:0.0}x)";
    }

    /// <summary>
    /// Widen/narrow the objectives task dock (0.1× steps, 0.5–3.0). Live: ObjectivesSurface FORCES
    /// the resulting mm budget onto the game's objective rows as a pixel width each tick, so the
    /// task text re-wraps and the progress bar lengthens — the readout's mm is the real panel width,
    /// not just a fit ceiling. SHAPE ONLY: the dock's fit deliberately ignores the width axis for
    /// this panel (ObjectivesSurface.FitWidthToMount), so the rendered TYPE SIZE does not move with
    /// this dial — that is what the separate 'Größe' (ObjectivesScale) row is for. The lower bound
    /// goes below 1.0 on purpose: since the budget is literal, 1.0 (260 mm) is already wider than the
    /// game's authored wrap column, so the dial needs headroom DOWNWARD to reach a tighter look.
    /// </summary>
    private void StepObjectivesWidth(int delta)
    {
        ConfigEntry<float> e = CardsConfig.ObjectivesWidth(CardsConfig.CurrentBoard);
        e.Value = Mathf.Clamp(e.Value + delta * 0.1f, 0.5f, 3f);
    }

    // ---- Spacing / Row-gap / Shape steppers (round 2) --------------------------------------

    /// <summary>Spacing readout for the selected group element (mm for meter gaps, factor for the active COL step).</summary>
    private string FormatSpacing()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return CurrentElement() switch
        {
            DebugElement.Rest => $"{CardsConfig.RestButtonSpacing(b).Value * 1000f:0}mm",
            DebugElement.Generic => $"{CardsConfig.GenericButtonSpacing(b).Value * 1000f:0}mm",
            DebugElement.Overlays => $"{CardsConfig.SlotOverlaySpacing(b).Value * 1000f:0}mm",
            DebugElement.Piles => $"{CardsConfig.PileSpacing(b).Value * 1000f:0}mm",
            DebugElement.Active => $"{CardsConfig.ActiveGridSpacing(b).Value.x:0.00}",
            _ => "-",
        };
    }

    private void StepSpacing(int delta)
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        switch (CurrentElement())
        {
            case DebugElement.Rest:
            {
                ConfigEntry<float> e = CardsConfig.RestButtonSpacing(b);
                e.Value += delta * 0.002f; // 2 mm per press (may go negative — pulls the pair together)
                break;
            }
            case DebugElement.Generic:
            {
                ConfigEntry<float> e = CardsConfig.GenericButtonSpacing(b);
                e.Value += delta * 0.002f;
                break;
            }
            case DebugElement.Overlays:
            {
                ConfigEntry<float> e = CardsConfig.SlotOverlaySpacing(b);
                e.Value += delta * 0.002f; // 2 mm per press (may go negative — pulls the pair together)
                break;
            }
            case DebugElement.Piles:
            {
                ConfigEntry<float> e = CardsConfig.PileSpacing(b);
                e.Value = Mathf.Max(0f, e.Value + delta * 0.002f);
                break;
            }
            case DebugElement.Active:
            {
                ConfigEntry<Vector2> e = CardsConfig.ActiveGridSpacing(b);
                Vector2 v = e.Value;
                v.x = Mathf.Max(0.1f, v.x + delta * 0.02f);
                e.Value = v;
                break;
            }
        }
    }

    /// <summary>Active grid ROW step factor (only shown for the Active element).</summary>
    private string FormatActiveRowStep()
    {
        ConfigEntry<Vector2> e = CardsConfig.ActiveGridSpacing(CardsConfig.CurrentBoard);
        return $"{e.Value.y:0.00}";
    }

    private void StepActiveRowStep(int delta)
    {
        ConfigEntry<Vector2> e = CardsConfig.ActiveGridSpacing(CardsConfig.CurrentBoard);
        Vector2 v = e.Value;
        v.y = Mathf.Max(0.1f, v.y + delta * 0.02f);
        e.Value = v;
    }

    /// <summary>Cap-shape readout for the selected button group (Rest / Generic).</summary>
    private string FormatShape()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return CurrentElement() switch
        {
            DebugElement.Rest => ShapeLabel(CardsConfig.RestButtonShape(b).Value),
            DebugElement.Generic => ShapeLabel(CardsConfig.GenericButtonShape(b).Value),
            _ => "-",
        };
    }

    /// <summary>Localized name of a debug-tunable board element (the Element cycle readout).</summary>
    private static string DebugElementLabel(DebugElement e) => e switch
    {
        DebugElement.Rest => Loc.Mod("rest"),
        DebugElement.Generic => Loc.Mod("generic"),
        DebugElement.Overlays => Loc.Mod("overlays"),
        DebugElement.Initiative => Loc.Mod("initiative"),
        DebugElement.Active => Loc.Mod("active"),
        DebugElement.Piles => Loc.Mod("piles"),
        DebugElement.Board => Loc.Mod("board"),
        DebugElement.Objectives => Loc.Mod("objectives"),
        DebugElement.Elements => Loc.Mod("elements"),
        DebugElement.VRSettings => Loc.Mod("vr_settings"),
        DebugElement.Pin => Loc.Mod("pin"),
        DebugElement.Readout => Loc.Mod("readout"),
        DebugElement.Cluster => Loc.Mod("cluster"),
        DebugElement.Decision => Loc.Mod("decision"),
        // Items rework: the item-use clip-in slot.
        DebugElement.ItemUse => Loc.Mod("item_use"),
        // Items rework: the item pile fan / held item-card pose offset.
        DebugElement.ItemCard => Loc.Mod("item_card"),
        // Fully localized (the whole mod is localized — no hardcoded strings): the round-phase
        // skip buttons the user tunes here, plus the category-split board elements (Confirm/Undo
        // keycaps; gear + Pin plates) and the per-style / colour rows.
        DebugElement.RoundButtons => Loc.Mod("round_buttons"),
        DebugElement.BoardDashboard => Loc.Mod("board_dashboard"),
        DebugElement.WallFade => Loc.Mod("wall_fade"),
        // 2026-07 restructure: the timing/CPU + GPU A/B block as its own element (it used to be
        // category-wide). Reuses the caption its section header already carried.
        DebugElement.Timing => Loc.Mod("subcat_timing"),
        // Per-style elements folded in from the former top-level tabs (2026-07).
        DebugElement.HandOffsets => Loc.Mod("hand_offsets"),
        DebugElement.FigureOffsets => Loc.Mod("figure_offsets"),
        DebugElement.WristOffsets => Loc.Mod("cat_wrist"),
        // User 4: the keycap label/cap colour rows.
        DebugElement.ButtonColors => Loc.Mod("button_colors"),
        // Generic config browser topics (2026-07) — named by ConfigCatalog so the chooser and the
        // catalog can never disagree about what a topic is called.
        DebugElement.CfgDiagnostics => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.Diagnostics),
        DebugElement.CfgVisual => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.Visual),
        DebugElement.CfgMovement => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.Movement),
        DebugElement.CfgHands => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.Hands),
        DebugElement.CfgCards => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.Cards),
        DebugElement.CfgButtons => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.Buttons),
        DebugElement.CfgPanels => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.Panels),
        DebugElement.CfgBoardTargeting => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.BoardTargeting),
        DebugElement.CfgBoardGeometry => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.BoardGeometry),
        DebugElement.CfgNetwork => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.Network),
        DebugElement.CfgSystem => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.System),
        DebugElement.CfgOther => ConfigCatalog.TopicLabel(ConfigCatalog.ConfigTopic.Other),
        _ => e.ToString(),
    };

    /// <summary>German name of a Debug sub-category (user 5b) — the sub-category chooser readout.</summary>
    private static string DebugSubCatLabel(DebugSubCat s) => s switch
    {
        DebugSubCat.AllSettings => Loc.Mod("subcat_all_settings"),
        DebugSubCat.BoardLayout => Loc.Mod("subcat_board_layout"),
        DebugSubCat.KartenStapel => Loc.Mod("subcat_cards_piles"),
        DebugSubCat.Tasten => Loc.Mod("cat_buttons"),
        DebugSubCat.Offsets => Loc.Mod("subcat_offsets"),
        // 2026-07 restructure: the wall see-through fractions no longer sit alone in a
        // sub-category of their own (a chooser with nothing to choose) — they share this one with
        // the timing/CPU levers, since both are dials for FINDING a default rather than settings.
        DebugSubCat.PerfEffects => Loc.Mod("subcat_perf_effects"),
        _ => s.ToString(),
    };

    /// <summary>Localized cap-shape name (the Shape cycle readout).</summary>
    private static string ShapeLabel(ButtonShape shape) =>
        shape == ButtonShape.Round ? Loc.Mod("round") : Loc.Mod("square");

    /// <summary>Flip the selected group's cap shape Round &lt;-&gt; Square (live rebuild via CardsDriver).</summary>
    private void FlipShape()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        switch (CurrentElement())
        {
            case DebugElement.Rest:
            {
                ConfigEntry<ButtonShape> e = CardsConfig.RestButtonShape(b);
                e.Value = e.Value == ButtonShape.Round ? ButtonShape.Square : ButtonShape.Round;
                break;
            }
            case DebugElement.Generic:
            {
                ConfigEntry<ButtonShape> e = CardsConfig.GenericButtonShape(b);
                e.Value = e.Value == ButtonShape.Round ? ButtonShape.Square : ButtonShape.Round;
                break;
            }
        }
    }

    /// <summary>Restore one ButtonTuning float entry to its (authored numeric) default.</summary>
    private static void ResetTuningF(ConfigEntry<float>? e)
    {
        if (e != null)
            e.Value = (float)e.DefaultValue;
    }

    private void ResetDebugElement()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        ConfigEntry<Vector3>? off = ElementOffsetEntry();
        if (off != null)
            off.Value = (Vector3)off.DefaultValue;
        switch (CurrentElement())
        {
            case DebugElement.Rest:
            {
                CardsConfig.RestButtonDiameter(b).Value = (float)CardsConfig.RestButtonDiameter(b).DefaultValue;
                CardsConfig.RestButtonSpacing(b).Value = (float)CardsConfig.RestButtonSpacing(b).DefaultValue;
                CardsConfig.RestButtonShape(b).Value = (ButtonShape)CardsConfig.RestButtonShape(b).DefaultValue;
                // GLOBAL [RestButtons] square-cap geometry (W/H/D/Travel), edited from this element.
                ResetTuningF(ButtonTuning.RestWidth);
                ResetTuningF(ButtonTuning.RestHeight);
                ResetTuningF(ButtonTuning.RestDepth);
                ResetTuningF(ButtonTuning.RestTravel);
                break;
            }
            case DebugElement.Generic:
            {
                CardsConfig.ConfirmUndoSize(b).Value = (float)CardsConfig.ConfirmUndoSize(b).DefaultValue;
                CardsConfig.GenericButtonSpacing(b).Value = (float)CardsConfig.GenericButtonSpacing(b).DefaultValue;
                CardsConfig.GenericButtonShape(b).Value = (ButtonShape)CardsConfig.GenericButtonShape(b).DefaultValue;
                // Issue 6: the GLOBAL [BoardButtons] Confirm/Undo keycap geometry (the square
                // rectangle W/H/D/Travel) is edited from this element, so reset it here too.
                ResetTuningF(ButtonTuning.BoardWidth);
                ResetTuningF(ButtonTuning.BoardHeight);
                ResetTuningF(ButtonTuning.BoardDepth);
                ResetTuningF(ButtonTuning.BoardTravel);
                break;
            }
            case DebugElement.Active:
            {
                CardsConfig.ActiveCardScale(b).Value = (float)CardsConfig.ActiveCardScale(b).DefaultValue;
                CardsConfig.ActiveGridSpacing(b).Value = (Vector2)CardsConfig.ActiveGridSpacing(b).DefaultValue;
                break;
            }
            case DebugElement.Piles:
            {
                CardsConfig.PileScale(b).Value = (float)CardsConfig.PileScale(b).DefaultValue;
                CardsConfig.PileSpacing(b).Value = (float)CardsConfig.PileSpacing(b).DefaultValue;
                // The browse-fan anchor offset is GLOBAL (board-local, rides every board) but is
                // edited from this element's rows, so the Piles reset restores it too.
                CardsConfig.BrowseFanOffset.Value = (Vector3)CardsConfig.BrowseFanOffset.DefaultValue;
                break;
            }
            case DebugElement.Board:
            {
                CardsConfig.BoardTilt(b).Value = (float)CardsConfig.BoardTilt(b).DefaultValue;
                CardsConfig.BoardYaw(b).Value = (float)CardsConfig.BoardYaw(b).DefaultValue;
                CardsConfig.BoardScale(b).Value = (float)CardsConfig.BoardScale(b).DefaultValue;
                break;
            }
            case DebugElement.Overlays:
                CardsConfig.SlotOverlaySpacing(b).Value = (float)CardsConfig.SlotOverlaySpacing(b).DefaultValue;
                break;
            case DebugElement.Objectives:
                CardsConfig.ObjectivesScale(b).Value = (float)CardsConfig.ObjectivesScale(b).DefaultValue;
                CardsConfig.ObjectivesWidth(b).Value = (float)CardsConfig.ObjectivesWidth(b).DefaultValue;
                break;
            case DebugElement.Elements:
                CardsConfig.ElementsScale(b).Value = (float)CardsConfig.ElementsScale(b).DefaultValue;
                break;
            case DebugElement.Cluster:
                CardsConfig.ClusterScale(b).Value = (float)CardsConfig.ClusterScale(b).DefaultValue;
                break;
            case DebugElement.Decision:
                CardsConfig.DecisionScale(b).Value = (float)CardsConfig.DecisionScale(b).DefaultValue;
                break;
            case DebugElement.RoundButtons:
            {
                // GLOBAL [RoundButtons] group (not per-board): restore the full geometry set.
                ResetTuningF(ButtonTuning.RoundOffsetX);
                ResetTuningF(ButtonTuning.RoundOffsetY);
                ResetTuningF(ButtonTuning.RoundOffsetZ);
                ResetTuningF(ButtonTuning.RoundCapSize);
                ResetTuningF(ButtonTuning.RoundWidth);
                ResetTuningF(ButtonTuning.RoundHeight);
                ResetTuningF(ButtonTuning.RoundDepth);
                ResetTuningF(ButtonTuning.RoundTravel);
                if (ButtonTuning.RoundShape != null)
                    ButtonTuning.RoundShape.Value = (ButtonShape)ButtonTuning.RoundShape.DefaultValue;
                break;
            }
            case DebugElement.BoardDashboard:
            {
                // GLOBAL [BoardDashboard] group: the gear + follow/pin plate geometry.
                ResetTuningF(ButtonTuning.DashGearWidth);
                ResetTuningF(ButtonTuning.DashPinWidth);
                ResetTuningF(ButtonTuning.DashHeight);
                ResetTuningF(ButtonTuning.DashDepth);
                ResetTuningF(ButtonTuning.DashTravel);
                break;
            }
            case DebugElement.WallFade:
            {
                // GLOBAL wall-fade fractions (issue 2): restore the four [WallFade] thresholds.
                ResetTuningF(WallFadeTuning.OnFraction);
                ResetTuningF(WallFadeTuning.OffFraction);
                ResetTuningF(WallFadeTuning.ExitDwellMoved);
                ResetTuningF(WallFadeTuning.ExitDwellStationary);
                break;
            }
            case DebugElement.ButtonColors:
            {
                // GLOBAL [ButtonColors] (user 4): restore the label text/outline/underlay + every
                // per-category cap-face tint to their authored defaults (= today's look). Writing each
                // entry bumps ButtonTuning.Version so the re-skin fires live.
                ResetTuningF(ButtonTuning.LabelR);
                ResetTuningF(ButtonTuning.LabelG);
                ResetTuningF(ButtonTuning.LabelB);
                if (ButtonTuning.LabelOutline != null)
                    ButtonTuning.LabelOutline.Value = (bool)ButtonTuning.LabelOutline.DefaultValue;
                ResetTuningF(ButtonTuning.LabelOutlineR);
                ResetTuningF(ButtonTuning.LabelOutlineG);
                ResetTuningF(ButtonTuning.LabelOutlineB);
                ResetTuningF(ButtonTuning.LabelOutlineW);
                if (ButtonTuning.LabelUnderlay != null)
                    ButtonTuning.LabelUnderlay.Value = (bool)ButtonTuning.LabelUnderlay.DefaultValue;
                ResetTuningF(ButtonTuning.BoardCapTintR);
                ResetTuningF(ButtonTuning.BoardCapTintG);
                ResetTuningF(ButtonTuning.BoardCapTintB);
                ResetTuningF(ButtonTuning.DashCapTintR);
                ResetTuningF(ButtonTuning.DashCapTintG);
                ResetTuningF(ButtonTuning.DashCapTintB);
                ResetTuningF(ButtonTuning.ClusterCapTintR);
                ResetTuningF(ButtonTuning.ClusterCapTintG);
                ResetTuningF(ButtonTuning.ClusterCapTintB);
                ResetTuningF(ButtonTuning.RestCapTintR);
                ResetTuningF(ButtonTuning.RestCapTintG);
                ResetTuningF(ButtonTuning.RestCapTintB);
                break;
            }
        }
        VRLog.Info("Cards", $"Debug: reset {CurrentElement()} for {b} to defaults.");
    }

}
