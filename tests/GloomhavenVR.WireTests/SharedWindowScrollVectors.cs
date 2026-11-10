using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class SharedWindowScrollVectors
{
    internal static void Run(Harness t)
    {
        t.Case("762:126 shared scroll exact single-event grammar and source identity");
        t.Equal(NetProtocol.MsgSharedWindowScroll, SharedWindowScrollCodec.MessageType, "new message ID agrees with catalog");
        t.Equal(NetProtocol.ExtIdSharedWindowScroll, SharedWindowScrollCodec.RecordId, "new TLV ID agrees with catalog");
        var state = new SharedWindowScrollMessage {
            Operation = SharedScrollOperation.State, Kind = 7, Control = 1, Flags = 5,
            Epoch = 0x04030201, Opening = 0x08070605, Generation = 0x0c0b0a09, Revision = 0x100f0e0d,
            Author = 17, Content = 0x1817161514131211UL, Path = "/A:0", Viewport = "/V:0",
            X = .25f, Y = .75f, RangeY = 800 };
        byte[] actual = SharedWindowScrollCodec.Write(state);
        byte[] golden = Hex.Bytes("31 52 56 47 03 1F 7E 4E 00 01 01 07 01 05 00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F 10 00 00 00 00 11 00 00 00 00 00 00 00 00 00 00 00 11 12 13 14 15 16 17 18 04 00 2F 41 3A 30 04 00 2F 56 3A 30 00 00 80 3E 00 00 40 3F 00 00 00 00 00 00 48 44 00 00 00 00");
        t.Wire(golden, actual, golden.Length, "independent source-addressed public scroll golden");
        t.True(SharedWindowScrollCodec.TryRead(golden, golden.Length, out var decoded)
            && decoded.Y == .75f && decoded.Content == state.Content && decoded.Generation == state.Generation,
            "independent golden decodes original page/viewport identity");
        for (int i = 0; i < golden.Length; i++)
            t.True(!SharedWindowScrollCodec.TryRead(golden, i, out _), "truncation cannot dispatch partial scroll " + i);
        foreach (int offset in new[] { 0, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14 })
        {
            var bad = (byte[])golden.Clone(); bad[offset] = 255;
            t.True(!SharedWindowScrollCodec.TryRead(bad, bad.Length, out _), "unknown header/schema/target bits refused " + offset);
        }
        var trailing = new byte[actual.Length + 1]; Array.Copy(actual, trailing, actual.Length);
        t.True(!SharedWindowScrollCodec.TryRead(trailing, trailing.Length, out _), "trailing bytes are not another input");
        state.X = float.NaN; t.True(!SharedWindowScrollCodec.Valid(state), "NaN cannot enter native scrollbar");
        state.X = float.PositiveInfinity; t.True(!SharedWindowScrollCodec.Valid(state), "infinity cannot enter native viewport");
        state.X = 0; state.Path = "/" + new string('x', SharedWindowScrollCodec.MaxPathBytes - 1);
        state.Viewport = state.Path;
        byte[] maximum = SharedWindowScrollCodec.Write(state);
        t.True(maximum.Length <= SharedWindowScrollCodec.MaxSize && maximum.Length < 864,
            "both maximal UTF8 paths remain one bounded native event");
        state.Path += "x"; t.True(!SharedWindowScrollCodec.Valid(state), "oversized source paths refused");

        t.Case("762:126 reliable requests carry no forged public state");
        var request = new SharedWindowScrollMessage { Operation = SharedScrollOperation.Wheel,
            Kind = 7, Control = 1, Author = 17, Requester = 22, Sequence = 1, Epoch = 1,
            Opening = 2, Generation = 3, Path = "/A:0", Y = -4 };
        actual = SharedWindowScrollCodec.Write(request);
        t.True(SharedWindowScrollCodec.TryRead(actual, actual.Length, out decoded) && decoded.Requester == 22
            && decoded.Revision == 0 && decoded.Sequence == 1 && decoded.Y == -4, "request roundtrip carries original wheel units");
        request.Revision = 8; t.True(!SharedWindowScrollCodec.Valid(request), "request cannot impersonate canonical state revision");
        request.Revision = 0; request.Viewport = "/Forged:0";
        t.True(!SharedWindowScrollCodec.Valid(request), "request cannot manufacture observer geometry");
        request.Viewport = ""; request.Requester = 0;
        t.True(!SharedWindowScrollCodec.Valid(request), "anonymous native input refused");
        t.True(SharedWindowScrollCodec.Newer(1, uint.MaxValue) && !SharedWindowScrollCodec.Newer(uint.MaxValue, 1),
            "wrapping sequence has one monotonic direction");
    }
}
