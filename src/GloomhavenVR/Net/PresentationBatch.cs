using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Coalesces small independent presentation pages within one existing Bolt event budget.
/// Children retain their own sequence, stream and atomic assembly; nested batches are rejected.</summary>
internal static class PresentationBatch
{
    internal const int MaxSize = ExtrasFragments.MaxDatagramBytes;
    internal static bool ChildType(int type) => type == NetProtocol.MsgExtras
        || type == TownServices.TownServiceCodec.FragmentType || type == NetProtocol.MsgExtrasFragments || type == NetProtocol.MsgUseBarAnimationFragments
        || type == NetProtocol.MsgCardPlumeFragments || type == NetProtocol.MsgNativeUseBarFragments
        || type == NetProtocol.MsgNativeBoardFragments || type == NetProtocol.MsgCardAppearanceFragments || type == NetProtocol.MsgNativeDecisionPromptFragments || type == NetProtocol.MsgItemAppearance || type == NetProtocol.MsgItemAppearanceFragments || type == NetProtocol.MsgPresentationCompression;

    internal static byte[] Write(List<byte[]> pages)
    {
        int length = 7;
        if (pages.Count < 2 || pages.Count > 32) throw new ArgumentException("Invalid presentation batch count.");
        foreach (byte[] page in pages)
        {
            if (page == null || page.Length < 6 || !ChildType(NetPacket.PeekType(page, page.Length)))
                throw new ArgumentException("Invalid presentation batch child.");
            length += 2 + page.Length;
        }
        if (length > MaxSize) throw new ArgumentException("Presentation batch exceeds event budget.");
        var result = new byte[length]; int at = 0;
        AvatarSerializer.WriteU32(result, ref at, NetProtocol.Magic);
        result[at++] = NetProtocol.Version; result[at++] = NetProtocol.MsgPresentationBatch;
        result[at++] = (byte)pages.Count;
        foreach (byte[] page in pages)
        {
            result[at++] = (byte)page.Length; result[at++] = (byte)(page.Length >> 8);
            Buffer.BlockCopy(page, 0, result, at, page.Length); at += page.Length;
        }
        return result;
    }

    internal static bool TryRead(byte[] buffer, int length, out byte[][]? pages)
    {
        pages = null;
        if (buffer == null || length < 7 || length > buffer.Length || length > MaxSize
            || NetPacket.PeekType(buffer, length) != NetProtocol.MsgPresentationBatch
            || buffer[6] < 2 || buffer[6] > 32) return false;
        int at = 7;
        var result = new byte[buffer[6]][];
        for (int i = 0; i < result.Length; i++)
        {
            if (length - at < 2) return false;
            int count = buffer[at] | buffer[at + 1] << 8; at += 2;
            if (count < 6 || count > length - at) return false;
            var page = new byte[count]; Buffer.BlockCopy(buffer, at, page, 0, count); at += count;
            if (!ChildType(NetPacket.PeekType(page, count))) return false;
            result[i] = page;
        }
        if (at != length) return false;
        pages = result; return true;
    }
}
