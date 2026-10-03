using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Apply complete original row frames on the owner's native board presentation clock.
/// No viewer animation or gameplay controller drives the foldout or its scrolling.</summary>
internal sealed class RemoteBoardRulesPlayer : IDisposable
{
    private NativeBoardState? _latest;
    private List<NativeBoardState>? _history;
    private readonly UseBarAnimationPlaybackClock _clock = new();
    private NativeBoardState? _seed;
    private BoardRulesFoldout? _foldout;
    private NativeElementRenderBinding[] _bindings = Array.Empty<NativeElementRenderBinding>();
    private RemoteElementRenderedHierarchy[] _players = Array.Empty<RemoteElementRenderedHierarchy>();
    private TMP_Text?[][] _texts = Array.Empty<TMP_Text?[]>();
    private int _stamp = -1;
    private string? _refusal;
    private readonly NativeBoardRulesState _blend = new();
    internal void SetState(NativeBoardState? state, List<NativeBoardState> history) { _latest = state; _history = history; }
    internal void Apply(RemoteWidgetMirror mirror, Transform rulesMount)
    {
        NativeBoardRulesState? latest = _latest?.Rules;
        if (latest == null) { Dispose(); mirror.SetRulesPresentation(false); return; }
        try
        {
            if (latest.Rows.Length == 0) { Dispose(); mirror.SetShown(false); return; }
            ScenarioModifierContainer? source = UIManager.Instance != null ? UIManager.Instance.ScenarioModifierContainer : null;
            if (source == null || mirror.RulesHost == null || mirror.RulesContent == null) return;
            if (_stamp != mirror.RebuildStamp || _foldout == null)
            {
                Dispose(); ScenarioModifierUI[] rows = source.GetComponentsInChildren<ScenarioModifierUI>(true);
                if (rows.Length != latest.Rows.Length) throw new InvalidOperationException("owner rules rows differ from original native widgets");
                _bindings = new NativeElementRenderBinding[rows.Length]; _players = new RemoteElementRenderedHierarchy[rows.Length];
                _texts = new TMP_Text?[rows.Length][];
                for (int i = 0; i < rows.Length; i++)
                {
                    _bindings[i] = new NativeElementRenderBinding(rows[i].transform, source.ScenarioModifierPrefab.transform);
                    _players[i] = new RemoteElementRenderedHierarchy(_bindings[i], mirror);
                    _texts[i] = new TMP_Text?[_bindings[i].Nodes.Length];
                    for (int n = 0; n < _texts[i].Length; n++) _texts[i][n] = mirror.CloneOf(_bindings[i].Nodes[n].Source)?.GetComponent<TMP_Text>();
                }
                mirror.SetRulesPresentation(true);
                _foldout = new BoardRulesFoldout(mirror.RulesHost, mirror.RulesContent, false); _stamp = mirror.RebuildStamp;
                _clock.Reset(_latest!.SampleTime, Time.unscaledTime); _seed = _latest;
            }
            float cursor = _clock.Advance(Time.unscaledTime, _latest!.SampleTime);
            NativeBoardState from = _latest, to = _latest;
            if (_history != null)
                foreach (NativeBoardState item in _history)
                {
                    if (item.Rules == null || item.Rules.Rows.Length != latest.Rows.Length || item.SampleTime < _seed!.SampleTime) continue;
                    if (item.SampleTime <= cursor) from = to = item;
                    else { to = item; break; }
                }
            float progress = _clock.Progress(from.SampleTime, to.SampleTime);
            NativeBoardRulesState a = from.Rules!, b = to.Rules!;
            // Validate the entire original hierarchy before writing any row.
            for (int i = 0; i < _players.Length; i++) { _players[i].Validate(a.Rows[i].Render); _players[i].Validate(b.Rows[i].Render); }
            NativeBoardRulesState discrete = progress < 1f ? a : b;
            _blend.Visible = discrete.Visible; _blend.Overflow = discrete.Overflow; _blend.Expanded = discrete.Expanded;
            _blend.Hover = discrete.Hover; _blend.Caption = discrete.Caption;
            for (int f = 0; f < _blend.Frame.Length; f++) _blend.Frame[f] = Mathf.LerpUnclamped(a.Frame[f], b.Frame[f], progress);
            rulesMount.localPosition = Vector3.zero; _foldout!.Apply(_blend);
            for (int r = 0; r < _players.Length; r++)
            {
                _players[r].Apply(a.Rows[r].Render, b.Rows[r].Render, progress);
                for (int n = 0; n < _texts[r].Length; n++)
                {
                    TMP_Text? text = _texts[r][n]; if (text == null) continue; NativeBoardRuleText value = discrete.Rows[r].Text[n];
                    if (text.text != value.Text) text.text = value.Text;
                    text.alignment = (TextAlignmentOptions)value.Alignment; text.fontStyle = (FontStyles)value.Style;
                    text.overflowMode = (TextOverflowModes)value.Overflow; text.richText = (value.Flags & 1) != 0;
                    text.enableAutoSizing = (value.Flags & 2) != 0; text.enableWordWrapping = (value.Flags & 4) != 0;
                    float[] v = value.StyleValues; text.fontSizeMin = v[0]; text.fontSizeMax = v[1]; text.lineSpacing = v[2];
                    text.characterSpacing = v[3]; text.wordSpacing = v[4]; text.margin = new Vector4(v[5], v[6], v[7], v[8]);
                }
            }
            _refusal = null;
        }
        catch (Exception e)
        {
            string refusal = e.GetType().Name + ": " + e.Message;
            if (_refusal != refusal) { _refusal = refusal; VRLog.Warn("Net", "NATIVE RULES: original mirror unavailable: " + refusal); }
            mirror.SetShown(false); // never publish a partially validated picture
        }
    }
    public void Dispose() { _foldout?.Dispose(); _foldout = null; _stamp = -1; _bindings = Array.Empty<NativeElementRenderBinding>(); _players = Array.Empty<RemoteElementRenderedHierarchy>(); _texts = Array.Empty<TMP_Text?[]>(); _seed = null; }
}
