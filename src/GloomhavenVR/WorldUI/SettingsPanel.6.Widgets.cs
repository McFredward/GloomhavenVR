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
    //  Grafik ▸ "Schärfe gegen Flüssigkeit" — the VR render trade (2026-07 perf pass)
    // ==========================================================================================

    /// <summary>
    /// The render-trade block that OPENS the "Grafik" tab — RESHAPED 2026-07 after the user's
    /// verdict on what was here before ("super verwirrend für den User"), and folded into Grafik by
    /// the 2026-07 menu restructure (it used to be a top-level "Leistung" tab of its own; keeping it
    /// separate is what put post-processing and MSAA — two halves of one decision — on two different
    /// tabs). It used to carry 20 rows: seven that only
    /// changed what the log records, five A/B switches for pure work removal, three CPU interval
    /// levers and a log-volume toggle. A player has no basis on which to decide any of those, and
    /// offering them implies they should.
    ///
    /// The rule now is a THREE-TIER split, and only the first tier is here:
    /// <list type="bullet">
    /// <item>NORMAL SETTINGS (this method) — only rows where the player genuinely gives something
    /// up to gain frames. Today that is exactly two levers, plus one preset that sets both.</item>
    /// <item>DEBUG (<see cref="BuildTimingCategory"/>) — power-user tuning to FIND good defaults:
    /// the CPU interval levers and the stereo render mode.</item>
    /// <item>CONFIG FILE ONLY — everything that exists to produce numbers in the log during this
    /// debug phase ([Perf] measurement) and every pure work-removal A/B switch. Reachable in
    /// dev.gloomhavenvr.perf.cfg, described there, absent from the UI.</item>
    /// </list>
    ///
    /// COMPLEMENTARY, NOT REPLACING: verified against the decompiled <c>GraphicSettings</c> /
    /// <c>DisplaySettings</c>, the base game already exposes quality preset, post-process AA
    /// (FXAA/SMAA/TAA), anisotropic filtering, shadows + shadow resolution, texture quality, skin
    /// weights, v-sync, FPS cap, pixel lights, soft particles, reflection probes and desktop
    /// resolution. It exposes NO MSAA at all (nothing in the game ever writes
    /// <c>QualitySettings.antiAliasing</c>) and nothing per-eye. Those two gaps are precisely
    /// what the rows below fill, so nothing here shadows a game control that could disagree with
    /// it — and the closing note points at Optionen › Grafik for the rest instead of mirroring it.
    ///
    /// Every row registers its own gate via <see cref="GateCat"/>, so this method may be called
    /// from anywhere in <c>Build()</c> and moved wholesale by changing the one call below.
    /// </summary>
    private void BuildPerformanceCategory()
    {
        GateCat(NavCat.Grafik);

        Section(Loc.Mod("perf_render_trade"));

        // One named choice that sets both levers below. Derived, not stored (RenderQuality
        // .CurrentPresetIndex): touching either row afterwards simply reads back as "Eigene",
        // so the preset can never hide or contradict the values it wrote.
        var presetRow = Row();
        Label(presetRow, Loc.Mod("perf_preset"), 16f, flexible: true);
        CycleButton(presetRow, 150f, RenderQuality.PresetLabel, RenderQuality.CyclePreset);
        Tip(presetRow, "perf_preset_note");

        // [RenderQuality] EyeResolutionScale — the dominant GPU lever (all per-pixel work ∝
        // scale²). Applies live; RenderQuality's readback decides whether the allocation or the
        // viewport lever carries it and says so in the log.
        var ssRow = Row();
        Label(ssRow, Loc.Mod("perf_eye_resolution"), 16f, flexible: true);
        MiniStepper(ssRow, RenderQuality.EyeScaleLabel, RenderQuality.StepEyeScale);
        Tip(ssRow, "perf_eye_resolution_note");

        // [RenderQuality] MsaaLevel — Off → 2x → 4x → 8x, applied live by the rig's per-frame
        // re-assert. Kept fully reachable including 8x: this is a real, user-visible quality
        // feature (hardware 2026-07), it is simply not a free one.
        var msaaRow = Row();
        Label(msaaRow, Loc.Mod("perf_msaa"), 16f, flexible: true);
        CycleButton(msaaRow, 100f, RenderQuality.MsaaLabel, RenderQuality.CycleMsaa);
        Tip(msaaRow, "perf_msaa_note");

        // The two one-line pointers that used to close this method (game graphics panel / config
        // file) now close the whole GRAFIK tab instead — see the end of the Grafik block in
        // SettingsPanel.3.Content.cs. Built there rather than here because rows land in the content
        // column in build order, and a signpost that says "the rest is in Optionen › Grafik" belongs
        // after the last setting on the tab, not in the middle of it.

        _rowGate = null;
    }

    /// <summary>
    /// Debug tier: the levers a power user needs to FIND good defaults, which no normal player
    /// should be asked to reason about.
    ///
    /// <para>2026-07 RESTRUCTURE: these eight rows used to be gated to the whole Debug CATEGORY,
    /// which meant they were appended to EVERY Debug page — under the button geometry, under the
    /// hand offsets, under the config browser. They are now their own element
    /// (Debug ▸ Leistung &amp; Effekte ▸ Zeitgeber / CPU), so a Debug page shows only what its
    /// element is about. Nothing else changed: same entries, same order, same live-apply.</para>
    ///
    /// <para>The three interval steppers are CPU-side and default to exactly today's behaviour.
    /// They are here rather than under Grafik because the measurement says the CPU is not the
    /// problem — the mod costs ~2.5% of frame time — so presenting them to a player as
    /// "performance settings" would aim them at the wrong thing.</para>
    ///
    /// <para>The stereo render mode is here rather than under Grafik for a harder reason: on
    /// this game it is not a trade, it is a break. See
    /// <see cref="Core.StereoModeConfig"/> for the evidence.</para>
    /// </summary>
    private void BuildTimingCategory()
    {
        PerfConfig.Bind();
        Core.StereoModeConfig.Bind();
        // Ambient gate for every row below (Row() reads it): the Zeitgeber/CPU ELEMENT, not the
        // whole category. The section header stays — it names the page inside the element pane.
        _rowGate = TimingRowsVisible;

        Section(Loc.Mod("subcat_timing"));

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

        var timingNote = Row(20f);
        Label(timingNote, Loc.Mod("debug_timing_short"), 12f, flexible: true);
        Tip(timingNote, "debug_timing_note");

        // The one GPU experiment that has no quality trade at all, so it cannot go under
        // Grafik: it either changes nothing visible (expected) or it breaks something
        // (the reason it defaults off). Debug is exactly the tier for "help me find the default".
        RectTransform scrubRow = Toggle(Loc.Mod("debug_skip_scrub_draw"),
            () => WorldUIConfig.SkipDesktopScrubDraw.Value,
            v =>
            {
                // Close the measurement window on BOTH sides of the flip, so the log carries one
                // [Perf] FRAME/SPLIT summary for "sink render on" and one for "sink render off"
                // with nothing straddling the boundary. That is the entire A/B: the tester toggles
                // this once, waits, toggles back, and the two SPLIT lines answer it.
                Core.PerfMonitor.MarkChange(
                    $"discarded desktop render {(v ? "SKIPPED" : "drawn")} — A/B boundary");
                WorldUIConfig.SkipDesktopScrubDraw.Value = v;
            });
        Tip(scrubRow, "debug_skip_scrub_note");

        // The mod's own largest contribution to submission volume, and the only one it can switch
        // off. Debug rather than Grafik because it is not a quality slider a player can reason
        // about: it either buys frames at the price of one specific VFX artefact, or it does not,
        // and which of those is true is what this row exists to find out.
        RectTransform depthRow = Toggle(Loc.Mod("debug_depth_prepass"),
            () => PerfConfig.HeadDepthPrepass.Value,
            v =>
            {
                Core.PerfMonitor.MarkChange(
                    $"head depth prepass {(v ? "ON" : "OFF")} — A/B boundary (a full extra scene "
                    + "submission per eye on the forward path)");
                PerfConfig.HeadDepthPrepass.Value = v;
            });
        Tip(depthRow, "debug_depth_prepass_note");

        // Head-camera culling mask narrowing. Same tier and same reason as the row above: it is a
        // measurement, not a preference — either the surplus layers hold geometry worth not
        // submitting twice, or they are empty and it changes nothing. Defaults OFF because getting
        // it wrong makes something invisible in the headset.
        RectTransform maskRow = Toggle(Loc.Mod("debug_head_mask_scenario"),
            () => PerfConfig.HeadMaskFromScenarioCamera.Value,
            v =>
            {
                Core.PerfMonitor.MarkChange(
                    $"head culling mask {(v ? "narrowed to the ScenarioCamera's" : "back to the anchor's")}"
                    + " — A/B boundary");
                PerfConfig.HeadMaskFromScenarioCamera.Value = v;
            });
        Tip(maskRow, "debug_head_mask_scenario_note");

        var stereoRow = Row();
        Label(stereoRow, Loc.Mod("debug_stereo_mode"), 16f, flexible: true);
        CycleButton(stereoRow, 150f, Core.StereoModeConfig.Label, Core.StereoModeConfig.Cycle);
        Tip(stereoRow, "debug_stereo_note");

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

    /// <summary>Label ..... [-] value [+]. Returns the row so the caller can hang a
    /// <see cref="Tip"/> on it.</summary>
    private RectTransform Stepper(string label, Func<string> read, Action<int> step)
    {
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row, read, step);
        return row;
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

    /// <summary>Label ..... [On/Off]. Returns the row so the caller can hang a
    /// <see cref="Tip"/> on it.</summary>
    private RectTransform Toggle(string label, Func<bool> read, Action<bool> write)
    {
        var row = Row();
        Label(row, label, 16f, flexible: true);
        ToggleButton(row, read, write);
        return row;
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
