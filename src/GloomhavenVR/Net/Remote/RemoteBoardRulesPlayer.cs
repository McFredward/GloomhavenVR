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
    private NativeBoardPresentationClock _boardClock = new();
    private bool _hadOwnerState;
    private BoardRulesFoldout? _foldout;
    private NativeElementRenderBinding[] _bindings = Array.Empty<NativeElementRenderBinding>();
    private RemoteElementRenderedHierarchy[] _players = Array.Empty<RemoteElementRenderedHierarchy>();
    private TMP_Text?[][] _texts = Array.Empty<TMP_Text?[]>();
    private int _stamp = -1;
    private string? _refusal;
    private readonly NativeBoardRulesState _blend = new();
    internal bool UsesOwnerState => _latest?.Rules != null;
    internal void SetState(NativeBoardState? state, List<NativeBoardState> history, NativeBoardPresentationClock? clock = null) { _latest = state; _history = history; if (clock != null) _boardClock = clock; }
    internal void Apply(RemoteWidgetMirror mirror, Transform rulesMount)
    {
        NativeBoardRulesState? latest = _latest?.Rules;
        if (latest == null)
        {
            Dispose(); mirror.SetRulesVisualRoot(null); mirror.SetRulesPresentation(false);
            if (_hadOwnerState) mirror.Refresh(null); // clear owner-only TMP styling before the legacy source is rebound
            _hadOwnerState = false; return;
        }
        _hadOwnerState = true;
        _boardClock.Select(_latest!, _history, out NativeBoardState from, out NativeBoardState to, out float progress);
        NativeBoardRulesState? a = from.Rules, b = to.Rules;
        if (a == null || b == null || a.Rows.Length != latest.Rows.Length || b.Rows.Length != latest.Rows.Length)
        { mirror.SetShown(false); return; }
        try
        {
            if (latest.Rows.Length == 0) { Dispose(); mirror.SetRulesVisualRoot(null); mirror.SetRulesPresentation(false); mirror.SetShown(false); return; }
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
                _foldout = new BoardRulesFoldout(mirror.RulesHost, mirror.RulesContent, false); mirror.SetRulesVisualRoot(_foldout.VisualRoot); _stamp = mirror.RebuildStamp;
                
            }
            // Validate the entire original hierarchy before writing any row.
            for (int i = 0; i < _players.Length; i++) { _players[i].Validate(a.Rows[i].Render); _players[i].Validate(b.Rows[i].Render); }
            NativeBoardRulesState discrete = progress < 1f ? a : b;
            _blend.Visible = discrete.Visible; _blend.Overflow = discrete.Overflow; _blend.Expanded = discrete.Expanded;
            _blend.Hover = discrete.Hover; _blend.Caption = discrete.Caption;
            for (int h = 0; h < _blend.Header.Length; h++) _blend.Header[h] = Mathf.LerpUnclamped(a.Header[h], b.Header[h], progress);
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
    public void Dispose() { _foldout?.Dispose(); _foldout = null; _stamp = -1; _bindings = Array.Empty<NativeElementRenderBinding>(); _players = Array.Empty<RemoteElementRenderedHierarchy>(); _texts = Array.Empty<TMP_Text?[]>(); }
}
