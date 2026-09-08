using System;

namespace GloomhavenVR.Net;

/// <summary>Independent GVR1 envelope for one atomic original auxiliary use-bar slot.</summary>
internal static class NativeUseBarPacket
{
    internal const int MaxSize = 6 + NativeUseBarCodec.MaxSize;

    internal static int Write(NativeUseBarSnapshot snapshot, byte[] buffer)
    {
        if (buffer == null || buffer.Length < MaxSize) throw new ArgumentException("Invalid native slot output.");
        int at = 0;
        AvatarSerializer.WriteU32(buffer, ref at, NetProtocol.Magic);
        buffer[at++] = NetProtocol.Version;
        buffer[at++] = NetProtocol.MsgNativeUseBar;
        int length = NativeUseBarCodec.Write(snapshot, buffer, at);
        return length == 0 ? 0 : at + length;
    }

    internal static bool TryRead(byte[] buffer, int length, out NativeUseBarSnapshot? snapshot)
    {
        snapshot = null;
        return buffer != null && length >= 6 && length <= buffer.Length && length <= MaxSize
            && NetPacket.PeekType(buffer, length) == NetProtocol.MsgNativeUseBar
            && NativeUseBarCodec.TryRead(buffer, length, out snapshot, 6);
    }
}
