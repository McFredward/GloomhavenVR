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
    // ---- widget builders --------------------------------------------------------------------

    private RectTransform Row(float height = RowHeightPx)
    {
        var go = new GameObject("Row") { layer = 5 };
        var rect = go.AddComponent<RectTransform>();
        // Rows parent into the active build column (sidebar/content) while one is set; else the
        // canvas root (header). And when a gate is set, register the row for the show/hide pass —
        // this is the single mechanism that swaps the content pane per selected category.
        go.transform.SetParent((_rowParent != null ? _rowParent : _root!.transform), worldPositionStays: false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;
        var el = go.AddComponent<LayoutElement>();
        el.preferredHeight = height;
        el.minHeight = height;
        if (_rowGate != null)
        {
            _rows.Add(go);
            _rowVisible.Add(_rowGate);
        }
        return rect;
    }

    private void Section(string title)
    {
        var row = Row(24f);
        Label(row, $"— {title} —", 14f, bold: true, flexible: true, center: true);
    }

    // ==========================================================================================
    //  Leistung — performance measurement + optimizations (2026-07 perf pass)
    // ==========================================================================================

    /// <summary>
    /// The whole "Leistung" category, in ONE self-contained block. Two sections:
    ///
    /// <para><b>Messung</b> drives <c>[Perf]</c> — what the log records. Nothing here changes a
    /// single pixel; it decides whether the next hardware log can answer "which subsystem owned
    /// that hitch" at all. Defaults ON, because a performance report nobody switched on is worth
    /// nothing, and the measurement is built to cost microseconds.</para>
    ///
    /// <para><b>Optimierungen</b> drives <c>[Optimize]</c> — one row per optimization, so any of
    /// them can be A/B'd on hardware against the [Perf] numbers without a rebuild. The toggles
    /// default to the OPTIMIZED behaviour only where the change is invisible by construction (pure
    /// work removal); the two interval steppers and the quiet-diagnostics switch default to exactly
    /// TODAY'S behaviour, so nothing the player can perceive changes without them asking for it.</para>
    ///
    /// <para>Every row registers its own gate via <see cref="GateCat"/>, so this method may be
    /// called from anywhere in <c>Build()</c> and moved to any other category by changing the one
    /// <see cref="GateCat"/> call below — which is what the inbound menu reorganization needs.</para>
    /// </summary>
    private void BuildPerformanceCategory()
    {
        PerfConfig.Bind();
        GateCat(NavCat.Leistung);

        // ---- Messung ------------------------------------------------------------------------
        Section(Loc.Mod("perf_measurement"));

        Toggle(Loc.Mod("perf_enabled"),
            () => PerfConfig.Enabled.Value,
            v => PerfConfig.Enabled.Value = v);

        var noteRow = Row(30f);
        Label(noteRow, Loc.Mod("perf_note"), 12f, flexible: true);

        Stepper(Loc.Mod("perf_interval"),
            () => $"{Mathf.Clamp(PerfConfig.SummaryIntervalSeconds.Value, 5f, 600f):0}s",
            d => PerfConfig.SummaryIntervalSeconds.Value =
                Mathf.Clamp(PerfConfig.SummaryIntervalSeconds.Value + d * 5f, 5f, 600f));

        Toggle(Loc.Mod("perf_attribution"),
            () => PerfConfig.Attribution.Value,
            v => PerfConfig.Attribution.Value = v);

        Stepper(Loc.Mod("perf_top_steps"),
            () => $"{Mathf.Clamp(PerfConfig.TopSteps.Value, 1, 20)}",
            d => PerfConfig.TopSteps.Value = Mathf.Clamp(PerfConfig.TopSteps.Value + d, 1, 20));

        Toggle(Loc.Mod("perf_spikes"),
            () => PerfConfig.SpikeLines.Value,
            v => PerfConfig.SpikeLines.Value = v);

        Stepper(Loc.Mod("perf_spike_factor"),
            () => $"{Mathf.Clamp(PerfConfig.SpikeBudgetFactor.Value, 1.2f, 10f):0.0}x",
            d => PerfConfig.SpikeBudgetFactor.Value =
                Mathf.Clamp(PerfConfig.SpikeBudgetFactor.Value + d * 0.1f, 1.2f, 10f));

        Stepper(Loc.Mod("perf_spike_rate"),
            () => $"{Mathf.Clamp(PerfConfig.SpikeMaxPerSecond.Value, 0.1f, 20f):0.0}/s",
            d => PerfConfig.SpikeMaxPerSecond.Value =
                Mathf.Clamp(PerfConfig.SpikeMaxPerSecond.Value + d * 0.5f, 0.1f, 20f));

        Toggle(Loc.Mod("perf_alloc"),
            () => PerfConfig.Allocations.Value,
            v => PerfConfig.Allocations.Value = v);

        Toggle(Loc.Mod("perf_xr"),
            () => PerfConfig.XrStats.Value,
            v => PerfConfig.XrStats.Value = v);

        // ---- Optimierungen -------------------------------------------------------------------
        Section(Loc.Mod("perf_optimizations"));

        Toggle(Loc.Mod("opt_cache_delegates"),
            () => PerfConfig.CacheTickDelegates.Value,
            v => PerfConfig.CacheTickDelegates.Value = v);

        Toggle(Loc.Mod("opt_map_icons"),
            () => PerfConfig.MapIconCache.Value,
            v => PerfConfig.MapIconCache.Value = v);

        Toggle(Loc.Mod("opt_figure_scan"),
            () => PerfConfig.FigureScanCache.Value,
            v => PerfConfig.FigureScanCache.Value = v);

        Toggle(Loc.Mod("opt_lean_strings"),
            () => PerfConfig.LeanLogStrings.Value,
            v => PerfConfig.LeanLogStrings.Value = v);

        Toggle(Loc.Mod("opt_tooltip_gate"),
            () => PerfConfig.TooltipScanGate.Value,
            v => PerfConfig.TooltipScanGate.Value = v);

        // The three below trade freshness for work, so their DEFAULT is today's behaviour and the
        // readout says "Aus"/"Off" rather than a number until the player deliberately raises it.
        Stepper(Loc.Mod("opt_fan_relayout"),
            () => PerfConfig.FanRelayoutMinInterval.Value <= 0f
                ? Loc.Mod("off")
                : $"{PerfConfig.FanRelayoutMinInterval.Value * 1000f:0}ms",
            d => PerfConfig.FanRelayoutMinInterval.Value =
                Mathf.Clamp(PerfConfig.FanRelayoutMinInterval.Value + d * 0.005f, 0f, 0.2f));

        Stepper(Loc.Mod("opt_wall_eval"),
            () => PerfConfig.WallFadeEvalInterval.Value <= 0f
                ? Loc.Mod("off")
                : $"{PerfConfig.WallFadeEvalInterval.Value * 1000f:0}ms",
            d => PerfConfig.WallFadeEvalInterval.Value =
                Mathf.Clamp(PerfConfig.WallFadeEvalInterval.Value + d * 0.005f, 0f, 0.25f));

        Stepper(Loc.Mod("opt_remote_content"),
            () => PerfConfig.RemoteContentInterval.Value <= 0f
                ? $"{Net.RemoteBoardContent.DefaultRefreshSeconds * 1000f:0}ms"
                : $"{PerfConfig.RemoteContentInterval.Value * 1000f:0}ms",
            d => PerfConfig.RemoteContentInterval.Value =
                Mathf.Clamp(PerfConfig.RemoteContentInterval.Value + d * 0.05f, 0f, 2f));

        Toggle(Loc.Mod("opt_quiet_diag"),
            () => PerfConfig.QuietDiagnostics.Value,
            v => PerfConfig.QuietDiagnostics.Value = v);

        var optNote = Row(30f);
        Label(optNote, Loc.Mod("perf_opt_note"), 12f, flexible: true);

        _rowGate = null;
    }

    private TextMeshProUGUI Label(RectTransform row, string text, float size,
        bool bold = false, bool flexible = false, bool center = false)
    {
        var go = new GameObject("Label") { layer = 5 };
        go.transform.SetParent(row, worldPositionStays: false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.92f, 0.9f, 0.85f);
        tmp.raycastTarget = false;
        WorldUIAssets.TryAssignGameFont(tmp);
        var el = go.AddComponent<LayoutElement>();
        if (flexible)
            el.flexibleWidth = 1f;
        else
            el.preferredWidth = Mathf.Max(28f, text.Length * size * 0.55f);
        return tmp;
    }

    private (Button button, TextMeshProUGUI label) Button(RectTransform row, string text, float width,
        Action onClick, bool flexible = false)
    {
        var go = new GameObject("Button") { layer = 5 };
        go.transform.SetParent(row, worldPositionStays: false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.22f, 0.24f, 0.32f, 0.95f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => Safe(onClick));
        var el = go.AddComponent<LayoutElement>();
        if (flexible)
            el.flexibleWidth = 1f;
        else
            el.preferredWidth = width;

        var textGo = new GameObject("Text") { layer = 5 };
        textGo.transform.SetParent(go.transform, worldPositionStays: false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 15f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        WorldUIAssets.TryAssignGameFont(tmp);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return (button, tmp);
    }

    /// <summary>Label ..... [-] value [+]</summary>
    private void Stepper(string label, Func<string> read, Action<int> step)
    {
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row, read, step);
    }

    /// <summary>[-] value [+] appended to an existing row.</summary>
    private void MiniStepper(RectTransform row, Func<string> read, Action<int> step)
    {
        Button(row, "-", 36f, () => { step(-1); RefreshAll(); });
        TextMeshProUGUI value = Label(row, read(), 15f, center: true);
        value.GetComponent<LayoutElement>().preferredWidth = 64f;
        Button(row, "+", 36f, () => { step(+1); RefreshAll(); });
        _refreshers.Add(() => value.text = read());
    }

    /// <summary>Label ..... [On/Off]</summary>
    private void Toggle(string label, Func<bool> read, Action<bool> write)
    {
        var row = Row();
        Label(row, label, 16f, flexible: true);
        ToggleButton(row, read, write);
    }

    private void ToggleButton(RectTransform row, Func<bool> read, Action<bool> write)
    {
        (Button button, TextMeshProUGUI text) = Button(row, read() ? Loc.Mod("on") : Loc.Mod("off"), 58f,
            () => { write(!read()); RefreshAll(); });
        Image image = (Image)button.targetGraphic;
        _refreshers.Add(() =>
        {
            bool on = read();
            text.text = on ? Loc.Mod("on") : Loc.Mod("off");
            image.color = on ? new Color(0.20f, 0.42f, 0.26f, 0.95f) : new Color(0.30f, 0.22f, 0.22f, 0.95f);
        });
    }

    private void CycleButton(RectTransform row, float width, Func<string> read, Action advance)
    {
        (Button _, TextMeshProUGUI text) = Button(row, read(), width, () => { advance(); RefreshAll(); });
        _refreshers.Add(() => text.text = read());
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            VRLog.Error("WorldUI", $"Settings panel action threw: {ex}");
        }
    }

}
