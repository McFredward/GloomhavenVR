using System;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class NativeBoardRulesVectors
{
    internal static void Run(Harness t)
    {
        t.Case("native-board-rules/independent-clear-golden");
        // Independent literal field grammar: flags0;13 little-endian floats (only meters/pixel1/2048);
        // empty UTF8 caption:u16;rows0. Native52 raw bytes remain identical without96.
        byte[] rulesRaw = new byte[92]; rulesRaw[43] = 0x00; rulesRaw[44] = 0x3a;
        byte[] golden = Pages(rulesRaw);
        t.True(NativeBoardRulesCodec.TryRead(golden, golden.Length, out var clear)
            && clear != null && !clear.Visible && clear.Rows.Length == 0 && clear.Frame[10] == 1f/2048f,
            "explicit raw96 golden reads without writer involvement");
        var legacy = new NativeBoardState(1f, 15f, 0, Array.Empty<NativeElementState>());
        var output = new byte[NativeBoardCodec.MaxSize]; int oldSize = NativeBoardCodec.Write(legacy, output);
        byte[] old = output.Take(oldSize).ToArray();
        var state = new NativeBoardState(1f, 15f, 0, Array.Empty<NativeElementState>(), rules: clear);
        int size = NativeBoardCodec.Write(state, output);
        t.True(output.Take(oldSize).SequenceEqual(old), "96 is appended;52 legacy bytes do not change");
        t.True(NativeBoardCodec.TryRead(old, old.Length, out var oldDecoded) && oldDecoded!.Rules == null,
            "missing96 remains explicitly legacy, never fabricated foldout");
        t.True(NativeBoardCodec.TryRead(output, size, out var decoded) && decoded!.Rules!.Same(clear!), "96 publishes atomically with52");
        byte[] unknown = output.Take(6).Concat(new byte[] { 254,2,7,8 }).Concat(output.Skip(6).Take(size-6)).ToArray();
        t.True(NativeBoardCodec.TryRead(unknown, unknown.Length, out decoded) && decoded!.Rules!.Same(clear!), "unknown record anywhere preserves96 atomic snapshot");
        byte[] duplicate = output.Take(size).Concat(output.Skip(oldSize).Take(size-oldSize)).ToArray();
        t.True(!NativeBoardCodec.TryRead(duplicate, duplicate.Length, out decoded) && decoded == null, "duplicate96 cannot publish valid52 partially");
        for (int i = oldSize+1; i < size; i++)
            t.True(!NativeBoardCodec.TryRead(output, i, out decoded) && decoded == null, "every truncated96 boundary refuses entire board");
        t.True(!NativeBoardRulesCodec.TryRead(null!,0,out _) && !NativeBoardRulesCodec.TryRead(new byte[4],100,out _), "standalone96 invalid input cannot throw");

        t.Case("native-board-rules/original-text-style-copy");
        var original = Picture();
        t.True(original.Validate(), "original rule text and style validate");
        var immutable = new NativeBoardState(2f, 15f, 0, Array.Empty<NativeElementState>(), rules: original);
        original.Frame[3] = 7f; original.Rows[0].Text[0].Text = "changed"; original.Rows[0].Render.Nodes[0].Geometry[0] = 99f;
        t.True(immutable.Rules!.Frame[3] == 500f && immutable.Rules.Rows[0].Text[0].Text == "Öffentliche Sonderregel <b>vollständig</b>" && immutable.Rules.Rows[0].Render.Nodes[0].Geometry[0] == 0f,
            "immutable publication copies text/row arrays and current geometry");
        var heartbeat = immutable.CopyWithTime(3f);
        t.True(heartbeat.Rules!.Same(immutable.Rules) && heartbeat.SampleTime == 3f && heartbeat.SamePicture(immutable), "heartbeat changes only source time, not rules picture");
        var different = immutable.Rules.Copy(); different.Hover = !different.Hover;
        t.True(!new NativeBoardState(2f,15f,0,Array.Empty<NativeElementState>(),rules:different).SamePicture(immutable), "hover difference triggers actual-picture publication");
        different = immutable.Rules.Copy(); different.Header[4] += .1f;
        t.True(!different.Same(immutable.Rules), "owner header color dial is part of remote picture");
        different = immutable.Rules.Copy(); different.Frame[3] += 1f;
        t.True(!different.Same(immutable.Rules), "intermediate opening geometry is a picture change");
        different = immutable.Rules.Copy(); different.Frame[7] += 1f;
        t.True(!different.Same(immutable.Rules), "owner scroll offset is a picture change");
        different = immutable.Rules.Copy(); different.Header[0] = .84f;
        var colorCopy = different.Copy(); different.Header[0] = 0f;
        t.True(colorCopy.Header[0] == .84f, "headerRGBA and font presentation are deeply immutable");
        int n = NativeBoardCodec.Write(immutable, output);
        t.True(NativeBoardCodec.TryRead(output,n,out decoded) && decoded!.Rules!.Same(immutable.Rules), "native prose/style/current layout retain exact float and UTF8 values");
        t.True(n < 1024, "one native rule is a bounded compact record, no per-pixel texture transfer");
        foreach (int at in new[] {0,3,7,10,11,12})
        { different = immutable.Rules.Copy(); different.Frame[at] = float.NaN; t.True(!different.Validate(), "nonfinite frame values refuse"); }
        different = immutable.Rules.Copy(); different.Rows[0].Text[0].Text = new string('x',NativeBoardRulesState.TextBytesMax+1);
        t.True(!different.Validate(), "total original prose has bounded payload; never truncates");
        different = immutable.Rules.Copy(); different.Rows[0].Render.Nodes[0].Binding = 0;
        t.True(!different.Validate(), "invalid native binding cannot publish");
        different = immutable.Rules.Copy(); different.Rows[0].Text[0].Flags = 255;
        t.True(!different.Validate(), "unknown text flags cannot publish");
        rulesRaw[0] = 128;
        t.True(!NativeBoardRulesCodec.TryRead(Pages(rulesRaw),Pages(rulesRaw).Length,out _), "unknown top-level flags reject independent raw96 golden");
        var utf8 = new byte[93]; Buffer.BlockCopy(new byte[92],0,utf8,0,92);
        utf8[43]=0;utf8[44]=0x3a;utf8[89]=1;utf8[91]=255;
        byte[] badUtf8=Pages(utf8);
        t.True(!NativeBoardRulesCodec.TryRead(badUtf8,badUtf8.Length,out _), "malformedUTF8 cannot publish native prose");
        byte[] rawWithTail=rulesRaw.Concat(new byte[]{55}).ToArray();rawWithTail[0]=0;
        byte[] trailing=Pages(rawWithTail);
        t.True(!NativeBoardRulesCodec.TryRead(trailing,trailing.Length,out _), "raw96 trailing bytes reject exact grammar");
        var longPicture=Picture();var random=new Random(44);
        longPicture.Rows[0].Text[0].Text=new string(Enumerable.Range(0,22000).Select(_=>(char)random.Next(33,127)).ToArray());
        int multi=NativeBoardRulesCodec.Write(longPicture,output,0);
        t.True(multi>480&&NativeBoardRulesCodec.TryRead(output,multi,out var longDecoded)&&longDecoded!.Same(longPicture), "bounded long prose spans multiple canonical96pages");
        byte[] wrongOffset=output.Take(multi).ToArray();wrongOffset[4]=1;
        t.True(!NativeBoardRulesCodec.TryRead(wrongOffset,wrongOffset.Length,out _), "noncanonical96page offset rejects");
        byte[] repeatPage=output.Take(246).Concat(output.Take(multi)).ToArray();
        t.True(!NativeBoardRulesCodec.TryRead(repeatPage,repeatPage.Length,out _), "repeated96page cannot assemble");
        bool oversize=false;try {NativeBoardRulesCodec.Write(longPicture,new byte[NativeBoardCodec.MaxSize*2],NativeBoardCodec.MaxSize-20);}catch(ArgumentException){oversize=true;}
        t.True(oversize,"larger caller buffer cannot bypass totalU16 transport bound");
        t.True(NativeBoardCodec.MaxSize <= ushort.MaxValue, "snapshot length fits existing U16 fragment grammar");
    }
    private static NativeBoardRulesState Picture()
    {
        var result = new NativeBoardRulesState { Visible=true,Overflow=true,Expanded=true,Hover=true,Caption="Sonderregeln",
            Rows=new[] { new NativeBoardRuleRow { Render=new NativeElementRenderState { Nodes=new[] { new NativeElementRenderNode {
                Parent=255,Sibling=0,Binding=77,Flags=NativeElementRenderNode.Active|NativeElementRenderNode.Rect|NativeElementRenderNode.Graphic|NativeElementRenderNode.Text,
                Color=new float[] {1,1,1,1},Geometry=new float[] {0,0,0,1,1,1,0,0,0,1,1,1,1,1,1,1,500,180},FontSize=16f } } },
                Text=new[] {new NativeBoardRuleText {Text="Öffentliche Sonderregel <b>vollständig</b>",Alignment=257,Style=1,Flags=5,
                    StyleValues=new float[] {18,72,0,0,0,0,0,0,0}}} } } };
        result.Frame[3]=500;result.Frame[4]=180;result.Frame[5]=500;result.Frame[6]=180;result.Frame[8]=300;
        result.Frame[9]=38;result.Frame[10]=1f/1440f;result.Frame[11]=-.02f;result.Frame[12]=.026f; return result;
    }
    private static byte[] Pages(byte[] raw)
    {
        var payload = new byte[raw.Length+3];payload[1]=(byte)raw.Length;payload[2]=(byte)(raw.Length>>8);Buffer.BlockCopy(raw,0,payload,3,raw.Length);
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        for (int offset=0;offset<payload.Length;offset+=240)
        { int count=Math.Min(240,payload.Length-offset);writer.Write((byte)96);writer.Write((byte)(count+4));writer.Write((ushort)payload.Length);writer.Write((ushort)offset);writer.Write(payload,offset,count); }
        return stream.ToArray();
    }
}
