namespace GloomhavenVR.Net;

/// <summary>
/// Tiny header helper shared by both wire packets (rig / extras). Lets the receive path route a
/// raw buffer to the right deserializer WITHOUT fully parsing it, and rejects foreign / stale /
/// truncated packets up front.
/// </summary>
internal static class NetPacket
{
    /// <summary>
    /// Validate the magic + version prefix and return the message TYPE byte
    /// (<see cref="NetProtocol.MsgRig"/> / <see cref="NetProtocol.MsgExtras"/>), or -1 on any
    /// mismatch or a buffer too short to hold the fixed header. Never throws.
    /// </summary>
    public static int PeekType(byte[] buf, int len)
    {
        // magic(4) + version(1) + type(1) = 6 bytes minimum.
        if (buf == null || len < 6)
            return -1;

        uint magic = (uint)(buf[0] | (buf[1] << 8) | (buf[2] << 16) | (buf[3] << 24));
        if (magic != NetProtocol.Magic)
            return -1;
        if (buf[4] != NetProtocol.Version)
            return -1;
        return buf[5];
    }
}
