using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class NativeHighlightPresenceVectors
{
    internal static void Run(Harness t)
    {
        t.Case("55: native highlight attaches to presence without changing old record bytes");
        var highlight = new NativeDecisionHighlightState();
        highlight.Rect[17] = 1f;
        var buffer = new byte[PresenceSerializer.MaxSize];
        int length = PresenceSerializer.Write(new PresenceState { DecisionHighlight = highlight }, buffer);
        byte[] prefix = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 37 7E");
        var golden = new byte[prefix.Length + 126];
        Array.Copy(prefix, golden, prefix.Length);
        // Independent IEEE754 constants: native quaternion W=1, image multiplier=1, canvas PPU=100.
        golden[prefix.Length + 74] = 0x80; golden[prefix.Length + 75] = 0x3F;
        golden[prefix.Length + 118] = 0x80; golden[prefix.Length + 119] = 0x3F;
        golden[prefix.Length + 122] = 0xC8; golden[prefix.Length + 123] = 0x42;
        t.Wire(golden, buffer, length, "exact appended55 layout");
        t.True(PresenceSerializer.TryRead(buffer, length, out PresenceState read)
            && NativeDecisionHighlightState.SamePicture(read.DecisionHighlight, highlight),
            "actual presence decoder exposes native owner pixels");
        highlight.SpriteName = new string('s', 64); highlight.TextureName = new string('t', 64);
        length = PresenceSerializer.Write(new PresenceState { DecisionHighlight = highlight,
            ShortRestInProgress = true, HasCardFx = true, FxSeq = 7, FxEndpoints = 0x41,
            HasCardFxVisibility = true, FxVisibilitySeq = 7, FxVisibilityFlags = 1 }, buffer);
        t.True(PresenceSerializer.TryRead(buffer, length, out read)
            && read.ShortRestInProgress && read.HasCardFxVisibility && read.FxVisibilityFlags == 1
            && NativeDecisionHighlightState.SamePicture(read.DecisionHighlight, highlight),
            "maximum254-byte55 coexists with older46 and correlated54");
        length = PresenceSerializer.Write(new PresenceState(), buffer);
        t.True(PresenceSerializer.TryRead(buffer, length, out read) && read.DecisionHighlight == null,
            "absent native highlight clears the old descriptor");
        var malformed = new byte[golden.Length + 3];
        Array.Copy(golden, malformed, golden.Length);
        malformed[10] = 2;
        malformed[prefix.Length + 122] = 0x80; malformed[prefix.Length + 123] = 0x7F; // infinity PPU
        malformed[golden.Length] = 46; malformed[golden.Length + 1] = 1; malformed[golden.Length + 2] = 1;
        t.True(PresenceSerializer.TryRead(malformed, malformed.Length, out read)
            && read.DecisionHighlight == null && read.ShortRestInProgress,
            "malformed55 cannot steal the next TLV or fabricate owner geometry");
    }
}
