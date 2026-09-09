using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class DamageDecisionPreviewVectors
{
    internal static void Run(Harness t)
    {
        t.Case("57: pending damage has one fixed committed-health endpoint");
        t.True(DamageDecisionPreviewMath.Damage(7, 5) == 2, "base two damage from seven");
        t.True(DamageDecisionPreviewMath.Damage(7, 6) == 1, "shield reduces orange cost rather than adding HP");
        t.True(DamageDecisionPreviewMath.Damage(7, 7) == 0, "full prevention has no damage overlay");
        t.True(DamageDecisionPreviewMath.Damage(7, -2) == 9, "lethal projected health retains full cost");
        t.True(DamageDecisionPreviewMath.Damage(7, 9) == 0, "overheal never creates negative damage");
        var state = new DamageDecisionPreviewState { ActorId = 0x12345678, CommittedHealth = 7,
            Damage = 1, BaseDamage = 2, OriginalMaxHealth = 10 };
        var bytes = new byte[16];
        int n = DamageDecisionPreviewCodec.Write(state, bytes, 2);
        var expected = Hex.Bytes("00 00 78 56 34 12 07 00 01 00 02 00 0A 00");
        t.Wire(expected, bytes, n + 2, "independent little-endian fixed golden");
        t.True(DamageDecisionPreviewCodec.TryRead(bytes, 2, n, out var read)
            && DamageDecisionPreviewState.SamePicture(state, read), "decode exact bounded body");
        for (int length = 0; length < n; length++)
            t.True(!DamageDecisionPreviewCodec.TryRead(bytes, 2, length, out _), "reject truncated body " + length);
        t.True(!DamageDecisionPreviewCodec.TryRead(bytes, 2, n + 1, out _), "do not borrow a following TLV");
        state.ActorId = 0;
        t.True(DamageDecisionPreviewCodec.Write(state, bytes) == 0, "unattributed preview is omitted");
        bytes[12] = bytes[13] = 0;
        t.True(!DamageDecisionPreviewCodec.TryRead(bytes, 2, n, out _), "invalid original max HP rejected");
    }
}
