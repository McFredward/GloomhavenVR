using System;
using System.Text;

namespace GloomhavenVR.Net;

/// <summary>Owner-authored informational rules presentation, never private goals or identities.
/// Geometry is pixels except the board-local host pose and element seat correction.</summary>
internal sealed class NativeBoardRulesState
{
    internal const int RowsMax = 32, TextBytesMax = 24000;
    internal bool Visible, Overflow, Expanded, Hover;
    // host local x/y/z, viewport width/height, content width/height, scroll offset,
    // header width/height, meters/pixel, element mount correction, occupied column height.
    internal float[] Frame = new float[13];
    internal string Caption = string.Empty;
    internal NativeBoardRuleRow[] Rows = Array.Empty<NativeBoardRuleRow>();
    internal bool Validate()
    {
        if (Frame == null || Frame.Length != 13 || Rows == null || Rows.Length > RowsMax || Caption == null) return false;
        foreach (float f in Frame) if (!UseBarAnimationValue.Finite(f)) return false;
        if (Frame[3] < 0 || Frame[4] < 0 || Frame[5] < 0 || Frame[6] < 0 || Frame[7] < 0
            || Frame[8] < 0 || Frame[9] < 0 || Frame[10] <= 0 || Frame[10] > .01f || Frame[11] > 0 || Frame[12] < 0) return false;
        if (Expanded && !Overflow) return false;
        int bytes = Encoding.UTF8.GetByteCount(Caption);
        foreach (NativeBoardRuleRow row in Rows)
        {
            if (row == null || !row.Validate()) return false;
            foreach (NativeBoardRuleText text in row.Text) bytes += Encoding.UTF8.GetByteCount(text.Text);
        }
        return bytes <= TextBytesMax;
    }
    internal NativeBoardRulesState Copy()
    {
        var copy = (NativeBoardRulesState)MemberwiseClone(); copy.Frame = (float[])Frame.Clone();
        copy.Rows = new NativeBoardRuleRow[Rows.Length];
        for (int i = 0; i < Rows.Length; i++) copy.Rows[i] = Rows[i].Copy(); return copy;
    }
    internal bool Same(NativeBoardRulesState other)
    {
        if (Visible != other.Visible || Overflow != other.Overflow || Expanded != other.Expanded || Hover != other.Hover
            || Caption != other.Caption || Rows.Length != other.Rows.Length) return false;
        for (int i = 0; i < Frame.Length; i++) if (Frame[i] != other.Frame[i]) return false;
        for (int i = 0; i < Rows.Length; i++) if (!Rows[i].Same(other.Rows[i])) return false; return true;
    }
}
internal sealed class NativeBoardRuleRow
{
    internal NativeElementRenderState Render = new();
    internal NativeBoardRuleText[] Text = Array.Empty<NativeBoardRuleText>();
    internal bool Validate()
    {
        if (Render == null || !Render.Validate() || Text == null || Text.Length != Render.Nodes.Length) return false;
        for (int i = 0; i < Text.Length; i++)
            if (Text[i] == null || !Text[i].Validate() || ((Render.Nodes[i].Flags & NativeElementRenderNode.Text) == 0 && Text[i].Text.Length != 0)) return false;
        return true;
    }
    internal NativeBoardRuleRow Copy()
    { var copy = new NativeBoardRuleRow { Render = Render.Copy(), Text = new NativeBoardRuleText[Text.Length] };
      for (int i = 0; i < Text.Length; i++) copy.Text[i] = Text[i].Copy(); return copy; }
    internal bool Same(NativeBoardRuleRow other)
    { if (!Render.Same(other.Render) || Text.Length != other.Text.Length) return false;
      for (int i = 0; i < Text.Length; i++) if (!Text[i].Same(other.Text[i])) return false; return true; }
}
internal sealed class NativeBoardRuleText
{
    internal string Text = string.Empty;
    internal int Alignment, Style, Overflow;
    internal byte Flags; // rich text, auto size, word wrap
    internal float[] StyleValues = new float[9]; // min/max size, line/character/word spacing, margins4
    internal bool Validate()
    { if (Text == null || StyleValues == null || StyleValues.Length != 9 || (Flags & ~7) != 0) return false;
      foreach (float f in StyleValues) if (!UseBarAnimationValue.Finite(f)) return false;
      return Alignment >= 0 && Alignment <= 65535 && Style >= 0 && Style <= 2047 && Overflow >= 0 && Overflow <= 7; }
    internal NativeBoardRuleText Copy()
    { var copy = (NativeBoardRuleText)MemberwiseClone(); copy.StyleValues = (float[])StyleValues.Clone(); return copy; }
    internal bool Same(NativeBoardRuleText other)
    { if (Text != other.Text || Alignment != other.Alignment || Style != other.Style || Overflow != other.Overflow || Flags != other.Flags) return false;
      for (int i = 0; i < StyleValues.Length; i++) if (StyleValues[i] != other.StyleValues[i]) return false; return true; }
}
