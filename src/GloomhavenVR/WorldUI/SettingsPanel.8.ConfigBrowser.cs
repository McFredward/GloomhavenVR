using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal sealed partial class SettingsPanel : IPanelGrabOwner
{
    // ==========================================================================================
    //  Debug ▸ Alle Einstellungen — the generic config browser
    //  (user 2026-07: "Alle config einstellungen sollen im VR Menu anpassbar sein!")
    // ==========================================================================================

    /// <summary>
    /// WHAT THIS IS. Every <see cref="BepInEx.Configuration.ConfigEntryBase"/> the mod binds —
    /// upwards of four hundred across nineteen files — reachable and editable from inside the
    /// headset, so no setting is ever "take the headset off, close the game, edit the cfg, restart"
    /// again. <see cref="ConfigCatalog"/> is the model (enumeration, topic/group organisation, the
    /// per-type read/write); this file is only the panel.
    ///
    /// <para>A FIXED ROW POOL, NOT A ROW PER ENTRY. Four hundred rows would be four hundred
    /// GameObjects with four hundred refreshers, in a panel whose refresh walks every refresher
    /// four times a second — and a layout the ContentSizeFitter would have to fit. So the pane
    /// builds <see cref="CfgRows"/> rows ONCE and re-points them at a page of the selected group.
    /// Cost is constant in the number of settings: the pane is the same size and the same per-frame
    /// work with four hundred entries as with twelve, and an entry bound later costs nothing but a
    /// page.</para>
    ///
    /// <para>NOTHING IS BUILT AT LOAD. The catalog is not walked in <c>Build()</c> — it is walked
    /// the first time the browser page is actually VISIBLE, from the refresher below, and then only
    /// re-walked when the registry's entry count changes. Every refresher here takes a
    /// <see cref="CfgVisible"/> early-out first, so with the pane closed (which is nearly always)
    /// the whole feature costs a handful of bool tests per refresh tick.</para>
    ///
    /// <para>NAVIGATION IS THE EXISTING ONE. The topic chooser IS the Debug element accordion
    /// (one <c>DebugElement.Cfg*</c> per <see cref="ConfigCatalog.ConfigTopic"/>) and the group
    /// chooser is built from the same accordion helpers, so there is no second idiom to learn and
    /// the breadcrumb keeps working. Three levels — topic ▸ group ▸ page — and never more than
    /// <see cref="CfgRows"/> rows on screen.</para>
    ///
    /// <para>MULTIPLAYER / REVERSIBILITY. It writes config entries and nothing else: no game
    /// object is touched, no game state is read, nothing goes on the wire. The entries it writes
    /// are the same ones the curated panes already write.</para>
    /// </summary>
    private const int CfgRows = 12;

    /// <summary>Numeric readout width (canvas px). The read-only text variant gets more.</summary>
    private const float CfgValueWidth = 56f;

    /// <summary>Read-only rows have no steppers, so their value may spend that width on text.</summary>
    private const float CfgReadOnlyValueWidth = 104f;

    /// <summary>Selected group index PER topic, so switching topics and back lands where you left.</summary>
    private readonly int[] _cfgGroup = new int[ConfigCatalog.TopicCount];

    /// <summary>Page within the selected group (reset by any group/topic change).</summary>
    private int _cfgPage;

    /// <summary>Group ACCORDION state — mirrors the element/sub-category choosers.</summary>
    private bool _cfgGroupListOpen;

    /// <summary>Topic the pane was last refreshed at (-1 = never), so a topic switch can reset the view.</summary>
    private int _cfgLastTopic = -1;

    /// <summary>0 = fine (x0.1), 1 = normal (x1), 2 = coarse (x10) — one dial for every stepper here.</summary>
    private int _cfgStepMode = 1;

    /// <summary>Which vector axis / colour channel each pooled row is currently stepping.</summary>
    private readonly int[] _cfgRowComponent = new int[CfgRows];

    // ---- state helpers -----------------------------------------------------------------------

    /// <summary>True while a config-browser topic is the selected Debug element.</summary>
    private bool CfgVisible() => PerBoard() && ElementIsConfigTopic(CurrentElement());

    /// <summary>The catalog topic the selected Debug element stands for.</summary>
    private ConfigCatalog.ConfigTopic CfgTopic()
    {
        int idx = CurrentElement() - DebugElement.CfgDiagnostics;
        return (ConfigCatalog.ConfigTopic)Mathf.Clamp(idx, 0, ConfigCatalog.TopicCount - 1);
    }

    private IReadOnlyList<ConfigCatalog.ConfigGroup> CfgGroups() => ConfigCatalog.Groups(CfgTopic());

    /// <summary>Selected group index for the current topic, clamped to what the topic actually has.</summary>
    private int CfgGroupIndex()
    {
        IReadOnlyList<ConfigCatalog.ConfigGroup> groups = CfgGroups();
        if (groups.Count == 0)
            return 0;
        return Mathf.Clamp(_cfgGroup[(int)CfgTopic()], 0, groups.Count - 1);
    }

    private ConfigCatalog.ConfigGroup? CfgGroup()
    {
        IReadOnlyList<ConfigCatalog.ConfigGroup> groups = CfgGroups();
        return groups.Count == 0 ? null : groups[CfgGroupIndex()];
    }

    private int CfgItemCount() => CfgGroup()?.Items.Count ?? 0;

    private int CfgPageCount()
    {
        int n = CfgItemCount();
        return n <= 0 ? 1 : (n + CfgRows - 1) / CfgRows;
    }

    /// <summary>Page index, clamped — the page can go stale when a rebuild shrinks a group.</summary>
    private int CfgPage() => Mathf.Clamp(_cfgPage, 0, CfgPageCount() - 1);

    /// <summary>How many of the pooled rows the current page fills (0..<see cref="CfgRows"/>).</summary>
    private int CfgRowCount()
    {
        int n = CfgItemCount() - CfgPage() * CfgRows;
        return Mathf.Clamp(n, 0, CfgRows);
    }

    /// <summary>The entry a pooled row currently stands for, or null when the page is short.</summary>
    private ConfigCatalog.ConfigItem? CfgItemAt(int row)
    {
        ConfigCatalog.ConfigGroup? group = CfgGroup();
        if (group == null)
            return null;
        int at = CfgPage() * CfgRows + row;
        return at >= 0 && at < group.Items.Count ? group.Items[at] : null;
    }

    /// <summary>Step multiplier of the shared Fein/Normal/Grob dial.</summary>
    private double CfgStepMultiplier() => _cfgStepMode switch
    {
        0 => 0.1d,
        2 => 10d,
        _ => 1d,
    };

    private string CfgStepLabel() => _cfgStepMode switch
    {
        0 => Loc.Mod("cfg_step_fine"),
        2 => Loc.Mod("cfg_step_coarse"),
        _ => Loc.Mod("cfg_step_normal"),
    };

    // ---- build ---------------------------------------------------------------------------------

    /// <summary>
    /// Build the browser's chrome (group accordion, page control, step dial), the pooled entry rows
    /// and the footer. Called once from <c>Build()</c> with the content column as the row parent;
    /// every row registers its own visibility gate, so the call site is free to move.
    /// </summary>
    private void BuildConfigBrowser()
    {
        // THE ONE AND ONLY TRIGGER of the catalog walk, and the FIRST refresher this method
        // registers — so within a single RefreshAll the catalog is already fresh when the row
        // refreshers and the row-visibility pass read it, and the first frame of a newly opened
        // page is drawn filled in rather than blank for a quarter second. Nothing happens while
        // the page is closed (one bool test), and nothing at all happens during Build().
        _refreshers.Add(() =>
        {
            if (!CfgVisible())
                return;
            ConfigCatalog.EnsureFresh();

            // Switching topic re-enters the pane: collapse the group list and go back to page 1, the
            // same "select and collapse" behaviour the sub-category and element choosers already
            // have. Done here rather than in the element chooser's click handler so the browser owns
            // all of its own state and the curated Debug panes stay untouched.
            int topic = (int)CfgTopic();
            if (topic != _cfgLastTopic)
            {
                _cfgLastTopic = topic;
                _cfgGroupListOpen = false;
                _cfgPage = 0;
            }
        });

        // ---- group chooser (accordion, same shape as the element chooser above) --------------
        _rowGate = () => CfgVisible() && CfgGroups().Count > 1;
        var groupRow = Row();
        Label(groupRow, Loc.Mod("cfg_group"), 16f, flexible: true);
        CycleButton(groupRow, 190f, CfgGroupHeaderText, () => _cfgGroupListOpen = !_cfgGroupListOpen);

        for (int i = 0; i < ConfigCatalog.MaxGroupsPerTopic; i++)
        {
            int idx = i; // capture per row
            _rowGate = () => _cfgGroupListOpen && CfgVisible() && idx < CfgGroups().Count;
            var optRow = Row(28f);
            Label(optRow, "", 13f); // 28 px gutter — reads as an indented sub-row, like the element list
            (Button _, TextMeshProUGUI optText) = Button(optRow, "", 0f, () =>
            {
                if (idx < CfgGroups().Count)
                {
                    _cfgGroup[(int)CfgTopic()] = idx;
                    _cfgPage = 0;             // a new group always starts at its first page
                }
                _cfgGroupListOpen = false;    // select + collapse
                RefreshAll();
            }, flexible: true);
            _refreshers.Add(() =>
            {
                if (!CfgVisible())
                    return;
                IReadOnlyList<ConfigCatalog.ConfigGroup> groups = CfgGroups();
                if (idx >= groups.Count)
                    return; // hidden by its own gate anyway
                bool selected = CfgGroupIndex() == idx;
                optText.text = (selected ? "> " : "") + groups[idx].Label + "  (" + groups[idx].Items.Count + ")";
            });
        }

        // ---- page control ----------------------------------------------------------------------
        // Hidden outright for a group that fits on one page, so the common case is a plain list.
        _rowGate = () => CfgVisible() && CfgPageCount() > 1;
        var pageRow = Row(28f);
        Button(pageRow, "‹", 44f, () => CfgTurnPage(-1));
        TextMeshProUGUI pageText = Label(pageRow, "", 14f, flexible: true, center: true);
        Button(pageRow, "›", 44f, () => CfgTurnPage(+1));

        _rowGate = CfgVisible;
        var stepRow = Row(28f);
        Label(stepRow, Loc.Mod("cfg_step"), 14f, flexible: true);
        CycleButton(stepRow, 110f, CfgStepLabel, () => _cfgStepMode = (_cfgStepMode + 1) % 3);

        // Shown ONLY when a topic really is empty — a topic with no entries must say so rather than
        // look broken (this is the honest state of "Sonstiges" until an unmapped module turns up).
        _rowGate = () => CfgVisible() && CfgItemCount() == 0;
        var emptyRow = Row(24f);
        Label(emptyRow, Loc.Mod("cfg_none"), 13f, flexible: true);

        // ---- the pooled entry rows -------------------------------------------------------------
        for (int i = 0; i < CfgRows; i++)
        {
            int idx = i; // capture per row
            _rowGate = () => CfgVisible() && idx < CfgRowCount();
            var row = Row(30f);

            // WIDTHS ARE THE SCANNABILITY BUDGET. The content column is ~346 px wide, so the
            // controls are deliberately compact (26 px keycaps, 56 px readout) to leave the key
            // NAME the widest column — a list you cannot read the names in is not a list. Names
            // that still overrun ellipsize rather than wrap, so every row stays exactly one line
            // high and the values stay aligned down the page; the full key is on hover.
            TextMeshProUGUI name = Label(row, "", 13f, flexible: true);
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;

            (Button compBtn, TextMeshProUGUI compText) = Button(row, "", 26f, () => CfgCycleComponent(idx));
            (Button minusBtn, TextMeshProUGUI _) = Button(row, "-", 26f, () => CfgStep(idx, -1));
            TextMeshProUGUI value = Label(row, "", 13f, center: true);
            LayoutElement valueLayout = value.GetComponent<LayoutElement>();
            valueLayout.preferredWidth = CfgValueWidth;
            (Button plusBtn, TextMeshProUGUI _) = Button(row, "+", 26f, () => CfgStep(idx, +1));
            (Button cycleBtn, TextMeshProUGUI cycleText) = Button(row, "", 104f, () => CfgActivate(idx));
            TextMeshProUGUI note = Label(row, Loc.Mod("cfg_readonly_short"), 11f);
            note.color = new Color(0.62f, 0.60f, 0.56f);
            note.GetComponent<LayoutElement>().preferredWidth = 62f;

            // The row (not a widget) carries the hover explanation, so moving the beam from "−" to
            // "+" never re-fires it — and the text is resolved AT HOVER TIME because this row
            // describes a different entry after every page turn.
            Tip(row, () => CfgTooltipText(idx));

            var widgets = new CfgRowWidgets
            {
                Name = name,
                Component = compBtn,
                ComponentText = compText,
                Minus = minusBtn,
                Value = value,
                ValueLayout = valueLayout,
                Plus = plusBtn,
                Cycle = cycleBtn,
                CycleText = cycleText,
                CycleImage = cycleBtn.targetGraphic as Image,
                Note = note,
            };
            _refreshers.Add(() => CfgRefreshRow(idx, widgets));
        }

        // ---- footer ------------------------------------------------------------------------------
        _rowGate = CfgVisible;
        var footerRow = Row(20f);
        TextMeshProUGUI footer = Label(footerRow, "", 11f, flexible: true);
        footer.color = new Color(0.62f, 0.60f, 0.56f);
        Tip(footerRow, "cfg_footer_note");

        // Chrome readouts (page counter + footer). These depend on nothing the row refreshers do,
        // so they are registered last purely for readability.
        _refreshers.Add(() =>
        {
            if (!CfgVisible())
                return;

            pageText.text = Loc.Mod("cfg_page") + " " + (CfgPage() + 1) + " / " + CfgPageCount();

            // "13 / 490 · 62 nur Datei" — this group, the whole mod, and how many entries are the
            // free-text ones no control can honestly own. The cfg FILE is deliberately NOT here: a
            // group can draw from several files (the per-style hand entries come from three), so it
            // belongs on the row's own hover, where it is always the right answer.
            footer.text = CfgItemCount() + " / " + ConfigCatalog.TotalEntries
                          + "  ·  " + ConfigCatalog.ReadOnlyEntries + " " + Loc.Mod("cfg_readonly_short");
        });

        _rowGate = null;
    }

    /// <summary>The group accordion's header caption: selected group, its size, and the open marker.</summary>
    private string CfgGroupHeaderText()
    {
        ConfigCatalog.ConfigGroup? group = CfgGroup();
        string label = group != null ? group.Label + "  (" + group.Items.Count + ")" : Loc.Mod("cfg_none");
        return label + (_cfgGroupListOpen ? " -" : " +");
    }

    /// <summary>Every widget of one pooled row, so the refresher does not re-find them each tick.</summary>
    private sealed class CfgRowWidgets
    {
        internal TextMeshProUGUI Name = null!;
        internal Button Component = null!;
        internal TextMeshProUGUI ComponentText = null!;
        internal Button Minus = null!;
        internal TextMeshProUGUI Value = null!;
        internal LayoutElement ValueLayout = null!;
        internal Button Plus = null!;
        internal Button Cycle = null!;
        internal TextMeshProUGUI CycleText = null!;
        internal Image? CycleImage;
        internal TextMeshProUGUI Note = null!;
    }

    // ---- per-row refresh -------------------------------------------------------------------------

    /// <summary>
    /// Point one pooled row at whatever entry currently sits in its slot: show only the widgets that
    /// entry's TYPE has a meaning for, and fill in its name and value. Never leaves a control on
    /// screen that would do nothing when pressed — a type the browser cannot edit shows the value
    /// plus a "file only" marker instead of a dead stepper.
    /// </summary>
    private void CfgRefreshRow(int row, CfgRowWidgets w)
    {
        if (!CfgVisible())
            return;
        ConfigCatalog.ConfigItem? item = CfgItemAt(row);
        if (item == null)
        {
            // The row's own gate hides it; nothing to draw, and nothing to read from a null entry.
            return;
        }

        w.Name.text = item.Display;

        bool numeric = item.Kind == ConfigCatalog.ConfigKind.Number;
        bool components = item.Kind == ConfigCatalog.ConfigKind.Components;
        bool cycle = item.Kind is ConfigCatalog.ConfigKind.Bool or ConfigCatalog.ConfigKind.Choice;
        bool readOnly = item.Kind == ConfigCatalog.ConfigKind.ReadOnly;
        int component = Mathf.Clamp(_cfgRowComponent[row], 0, Mathf.Max(0, item.Components - 1));

        SetActive(w.Component.gameObject, components);
        SetActive(w.Minus.gameObject, numeric || components);
        SetActive(w.Plus.gameObject, numeric || components);
        SetActive(w.Value.gameObject, !cycle);
        SetActive(w.Cycle.gameObject, cycle);
        SetActive(w.Note.gameObject, readOnly);

        if (components)
            w.ComponentText.text = ConfigCatalog.ComponentLabel(item, component);

        if (cycle)
        {
            w.CycleText.text = ConfigCatalog.ValueText(item, component);
            if (w.CycleImage != null)
            {
                // A bool reads as a switch (green on / red off) exactly like every other toggle in
                // this panel; a multi-value choice keeps the neutral button colour.
                w.CycleImage.color = item.Kind != ConfigCatalog.ConfigKind.Bool
                    ? new Color(0.22f, 0.24f, 0.32f, 0.95f)
                    : item.Entry.BoxedValue is bool b && b
                        ? new Color(0.20f, 0.42f, 0.26f, 0.95f)
                        : new Color(0.30f, 0.22f, 0.22f, 0.95f);
            }
        }
        else
        {
            w.ValueLayout.preferredWidth = readOnly ? CfgReadOnlyValueWidth : CfgValueWidth;
            w.Value.text = ConfigCatalog.ValueText(item, component);
            w.Value.color = readOnly
                ? new Color(0.72f, 0.70f, 0.66f)
                : new Color(0.92f, 0.90f, 0.85f);
        }
    }

    private static void SetActive(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }

    // ---- actions --------------------------------------------------------------------------------

    private void CfgTurnPage(int dir)
    {
        ConfigCatalog.EnsureFresh();
        int pages = CfgPageCount();
        _cfgPage = ((CfgPage() + dir) % pages + pages) % pages; // wraps, so one button reaches both ends
        RefreshAll();
    }

    private void CfgCycleComponent(int row)
    {
        ConfigCatalog.ConfigItem? item = CfgItemAt(row);
        if (item == null || item.Components <= 1)
            return;
        _cfgRowComponent[row] = (Mathf.Clamp(_cfgRowComponent[row], 0, item.Components - 1) + 1) % item.Components;
        RefreshAll();
    }

    private void CfgStep(int row, int dir)
    {
        ConfigCatalog.EnsureFresh();
        ConfigCatalog.ConfigItem? item = CfgItemAt(row);
        if (item == null)
            return;
        int component = Mathf.Clamp(_cfgRowComponent[row], 0, Mathf.Max(0, item.Components - 1));
        ConfigCatalog.Step(item, component, dir, CfgStepMultiplier());
        RefreshAll();
    }

    /// <summary>The one-button control: flip a bool, advance a choice.</summary>
    private void CfgActivate(int row)
    {
        ConfigCatalog.EnsureFresh();
        ConfigCatalog.ConfigItem? item = CfgItemAt(row);
        if (item == null)
            return;
        if (item.Kind == ConfigCatalog.ConfigKind.Bool)
            ConfigCatalog.ToggleBool(item);
        else if (item.Kind == ConfigCatalog.ConfigKind.Choice)
            ConfigCatalog.Cycle(item, +1);
        RefreshAll();
    }

    private string CfgTooltipText(int row)
    {
        try
        {
            ConfigCatalog.ConfigItem? item = CfgItemAt(row);
            return item == null ? string.Empty : ConfigCatalog.Tooltip(item);
        }
        catch (Exception ex)
        {
            VRLog.Error("WorldUI", $"Config browser tooltip threw: {ex}");
            return string.Empty;
        }
    }
}
