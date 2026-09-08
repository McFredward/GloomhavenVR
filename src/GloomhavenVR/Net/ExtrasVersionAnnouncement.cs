using System;

namespace GloomhavenVR.Net;

/// <summary>
/// Small legacy-readable version handshake. Old peers cannot read fragmented extras, but must
/// still show the existing mismatch dialog. New peers consume this exact version-only shape
/// before board snapshot dispatch, so it never clears a same-build player's presentation.
/// </summary>
internal static class ExtrasVersionAnnouncement
{
    internal static byte[] Write(ushort build, string text)
    {
        var buffer = new byte[PresenceSerializer.MaxSize];
        int length = PresenceSerializer.Write(new PresenceState
            { HasModVersion = true, ModBuild = build, ModVersionText = text }, buffer);
        Array.Resize(ref buffer, length);
        return buffer;
    }

    internal static bool TryRead(byte[] buffer, int length, out PresenceState version)
    {
        version = default;
        return buffer != null && length >= 15 && length <= buffer.Length
            && NetPacket.PeekType(buffer, length) == NetProtocol.MsgExtras
            && buffer[6] == 0x80 && buffer[7] == 0 && buffer[8] == 0x80 && buffer[9] == 0
            && buffer[10] == 1 && buffer[11] == NetProtocol.ExtIdModVersion
            && buffer[12] >= 2 && length == 13 + buffer[12]
            && PresenceSerializer.TryRead(buffer, length, out version) && version.HasModVersion;
    }
}
