using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class NativeDecisionPromptVectors
{
    private static float[] Rect() { var r = new float[18]; r[17] = 1; return r; }
    private static NativeDecisionPromptText Text(string text) => new() { Rect = Rect(), Text = text,
        Font = new float[] { 20, 10, 30, 1, 2, 3, 4, 400 }, Alignment = 514, Flags = 27 };
    private static NativeDecisionPromptState State() => new() { Rect = Rect(), Lines = new[] {
        new NativeDecisionPromptLine { Rect = Rect(), Tip = Text("Schaden: 2"), Warning = Text("Schaden: 2") } } };
    internal static void Run(Harness t)
    {
        t.Case("63 native prompt: exact clear, live geometry, warning channels and bounded text");
        var bytes = new byte[NativeDecisionPromptCodec.MaxSize];
        int n = NativeDecisionPromptCodec.Write(new NativeDecisionPromptSnapshot(1, 0, null), bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 0E 3F 0B 00 01 00 00 80 3F 00 00 00 00 00"), bytes, n,
            "independent complete message14 clear golden");
        t.True(NativeDecisionPromptCodec.TryRead(bytes, n, out var clear) && clear!.State == null
            && clear.SampleTime == 1 && clear.ActorId == 0, "explicit clear decodes");
        NativeDecisionPromptState s = State();
        s.Frame = new float[] { 500, 80, 1920, 1080, 12, 9 };
        s.Lines[0].Warning.Rect[11] = 1.75f; s.Lines[0].Warning.Colors[7] = .37f;
        var snapshot = new NativeDecisionPromptSnapshot(2, 0x12345678, s);
        s.Lines[0].Tip.Text = "mutation";
        t.True(snapshot.State!.Lines[0].Tip.Text == "Schaden: 2", "snapshot owns nested text and geometry");
        n = NativeDecisionPromptCodec.Write(snapshot, bytes);
        t.True(NativeDecisionPromptCodec.TryRead(bytes, n, out var read) && read!.ActorId == 0x12345678
            && read.State!.Lines[0].Warning.Rect[11] == 1.75f && read.State.Lines[0].Warning.Colors[7] == .37f
            && read.State.Frame[0] == 500 && read.State.Frame[5] == 9
            && read.State.Lines[0].Tip.Font[4] == 2 && read.State.Lines[0].Tip.Text == "Schaden: 2",
            "actual intermediate warning scale and independent renderer alpha survive transport");
        for (int cut = 0; cut < n; cut++)
            t.True(!NativeDecisionPromptCodec.TryRead(bytes, cut, out _), "all truncated page tails rejected " + cut);
        var malformed = (byte[])bytes.Clone(); malformed[8] = 1;
        t.True(!NativeDecisionPromptCodec.TryRead(malformed, n, out _), "out-of-order first page rejected");
        malformed = (byte[])bytes.Clone(); malformed[9] = 1;
        t.True(!NativeDecisionPromptCodec.TryRead(malformed, n, out _), "wrong total pages rejected");
        var extra = new byte[n + 3]; Array.Copy(bytes, extra, n); extra[n] = 250; extra[n + 1] = 1; extra[n + 2] = 42;
        t.True(NativeDecisionPromptCodec.TryRead(extra, extra.Length, out _), "unknown future TLV skipped by its own length");
        s = State(); s.Rect[17] = .5f;
        t.True(!s.Validate(), "non-unit native rotation refused");
        s = State(); s.Lines[0].Tip.Text = new string('ä', 513);
        t.True(!s.Validate(), "UTF8 bound counts bytes, never truncates original text");
        s = State(); s.Lines[0].Tip.Font[0] = float.NaN;
        t.True(!s.Validate(), "non-finite glyph output refused");
        s = State(); s.Lines[0].Tip.Text = "\uD800";
        t.True(!s.Validate(), "malformed UTF16 cannot become replacement glyphs");
        s = State(); s.Lines[0].Tip.Text = "new";
        t.True(!NativeDecisionPromptSnapshot.SameIdentity(snapshot, new NativeDecisionPromptSnapshot(3, snapshot.ActorId, s)),
            "new wording starts a new animation identity");
        s = State(); s.Lines = new[] { s.Lines[0], s.Lines[0], s.Lines[0], s.Lines[0] };
        s.Lines[0].Tip.Text = new string('x', 1024); s.Lines[0].Warning.Text = new string('x', 1024);
        t.True(NativeDecisionPromptCodec.Write(new NativeDecisionPromptSnapshot(4, 1, s), bytes) == 0,
            "oversize complete output is rejected atomically rather than publishing partial text");
    }
}
