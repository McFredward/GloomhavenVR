using System;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>Cached original row bindings. Readback changes only with the actual owner picture;
/// native rule strings are public, and battle-goal content is deliberately never read here.</summary>
internal static class BoardRulesPresentation
{
    private static BoardRulesFoldout? _owner;
    private static NativeElementRenderBinding[] _rows = Array.Empty<NativeElementRenderBinding>();
    private static readonly NativeBoardRulesState Scratch = new();
    private static NativeBoardRulesState? _published;
    private static float _bindAt;
    internal static NativeBoardRulesState Sample()
    {
        BoardRulesFoldout? owner = ScenarioRulesSurface.Presentation;
        if (owner == null)
        {
            _owner = null; _rows = Array.Empty<NativeElementRenderBinding>();
            Scratch.Visible = Scratch.Overflow = Scratch.Expanded = Scratch.Hover = false;
            Array.Clear(Scratch.Frame, 0, Scratch.Frame.Length); Scratch.Frame[10] = 1f / BoardRulesFoldout.Density;
            Scratch.Frame[11] = ElementBoardSurface.SeatCorrectionMeters; Scratch.Caption = Loc.BoardRulesCaption;
            Scratch.Rows = Array.Empty<NativeBoardRuleRow>();
        }
        else
        {
            if (!ReferenceEquals(_owner, owner) || Time.unscaledTime >= _bindAt)
            {
                _bindAt = Time.unscaledTime + .25f;
                ScenarioModifierUI[] widgets = owner.Content.GetComponentsInChildren<ScenarioModifierUI>(true);
                bool same = ReferenceEquals(_owner, owner) && widgets.Length == _rows.Length;
                for (int i = 0; same && i < widgets.Length; i++) same &= ReferenceEquals(widgets[i].transform, _rows[i].Nodes[0].Source) && _rows[i].Matches();
                if (!same)
                {
                    ScenarioModifierContainer container = UIManager.Instance.ScenarioModifierContainer;
                    var bindings = new NativeElementRenderBinding[widgets.Length];
                    var rows = new NativeBoardRuleRow[widgets.Length];
                    if (widgets.Length > NativeBoardRulesState.RowsMax) throw new InvalidOperationException("Native rules row count exceeds bound.");
                    for (int i = 0; i < widgets.Length; i++)
                    {
                        bindings[i] = new NativeElementRenderBinding(widgets[i].transform, container.ScenarioModifierPrefab.transform);
                        rows[i] = new NativeBoardRuleRow { Render = bindings[i].Scratch, Text = new NativeBoardRuleText[bindings[i].Nodes.Length] };
                        for (int n = 0; n < rows[i].Text.Length; n++) rows[i].Text[n] = new NativeBoardRuleText();
                    }
                    _rows = bindings; Scratch.Rows = rows; _owner = owner;
                }
            }
            owner.CaptureFrame(Scratch);
            Transform? mount = ScenarioRulesSurface.PresentationMount;
            if (mount != null)
            {
                Vector3 p = owner.HostPositionIn(mount); Scratch.Frame[0] = p.x; Scratch.Frame[1] = p.y; Scratch.Frame[2] = p.z;
            }
            Scratch.Frame[11] = ElementBoardSurface.SeatCorrectionMeters;
            for (int r = 0; r < _rows.Length; r++)
            {
                _rows[r].Read();
                for (int n = 0; n < _rows[r].Nodes.Length; n++)
                {
                    TMP_Text? text = _rows[r].Nodes[n].Text; NativeBoardRuleText output = Scratch.Rows[r].Text[n];
                    if (text == null) continue;
                    output.Text = text.text; output.Alignment = (int)text.alignment; output.Style = (int)text.fontStyle;
                    output.Overflow = (int)text.overflowMode;
                    output.Flags = (byte)((text.richText ? 1 : 0) | (text.enableAutoSizing ? 2 : 0) | (text.enableWordWrapping ? 4 : 0));
                    float[] values = output.StyleValues;
                    values[0] = text.fontSizeMin; values[1] = text.fontSizeMax; values[2] = text.lineSpacing;
                    values[3] = text.characterSpacing; values[4] = text.wordSpacing; Vector4 margin = text.margin;
                    values[5] = margin.x; values[6] = margin.y; values[7] = margin.z; values[8] = margin.w;
                }
            }
        }
        if (!Scratch.Validate()) throw new InvalidOperationException("Native rules presentation exceeds bounded grammar.");
        if (_published == null || !_published.Same(Scratch)) _published = Scratch.Copy();
        return _published;
    }
    internal static void Reset() { _owner = null; _rows = Array.Empty<NativeElementRenderBinding>(); _published = null; _bindAt = 0f; }
}
