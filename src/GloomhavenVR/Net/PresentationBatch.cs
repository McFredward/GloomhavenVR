using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net;

/// <summary>Coalesces small independent presentation pages within one existing Bolt event budget.
/// Children retain their own sequence, stream and atomic assembly; nested batches are rejected.</summary>
internal static class PresentationBatch
{
    internal const int MaxSize = ExtrasFragments.MaxDatagramBytes;
    internal static bool ChildType(int type) => type == NetProtocol.MsgExtras
        || type == NetProtocol.MsgTownOriginalReceipt
        || type == TownServices.TownServiceCodec.FragmentType || type == NetProtocol.MsgExtrasFragments || type == NetProtocol.MsgUseBarAnimationFragments
        || type == NetProtocol.MsgCardPlumeFragments || type == NetProtocol.MsgNativeUseBarFragments
        || type == NetProtocol.MsgNativeBoardFragments || type == NetProtocol.MsgCardAppearanceFragments || type == NetProtocol.MsgNativeDecisionPromptFragments || type == NetProtocol.MsgItemAppearance || type == NetProtocol.MsgItemAppearanceFragments || type == NetProtocol.MsgPresentationCompression
        || type == NetProtocol.MsgMapButtonTooltip || type == NetProtocol.MsgMapButtonTooltipFragments;

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

    /// <summary>Exact town artwork is a delta dependency, not a disposable motion
    /// sample. Select reliable delivery without allocating/unpacking batch children.
    /// This does not change the existing event budget or the separate rig clocks.</summary>
    internal static bool HasTownOriginalPage(byte[] buffer, int length)
    {
        if (buffer == null || length < 6 || length > buffer.Length || length > MaxSize) return false;
        if (NetPacket.PeekType(buffer, length) != NetProtocol.MsgPresentationBatch)
            return TownOriginalAt(buffer, 0, length);
        if (length < 7 || buffer[6] < 2 || buffer[6] > 32) return false;
        int at = 7; bool town = false;
        for (int i = 0; i < buffer[6]; i++)
        {
            if (length - at < 2) return false;
            int count = buffer[at] | buffer[at + 1] << 8; at += 2;
            if (count < 6 || count > length - at || !ChildType(buffer[at + 5])) return false;
            town |= TownOriginalAt(buffer, at, count); at += count;
        }
        return at == length && town;
    }

    private static bool TownOriginalAt(byte[] buffer, int start, int length)
    {
        if (buffer[start] != 0x31 || buffer[start + 1] != 0x52 || buffer[start + 2] != 0x56
            || buffer[start + 3] != 0x47 || buffer[start + 4] != NetProtocol.Version) return false;
        int type = buffer[start + 5];
        if (type == TownServices.TownServiceCodec.FragmentType || type == TownServices.TownServiceCodec.MessageType
            || type == NetProtocol.MsgTownOriginalReceipt)
            return true;
        if (type != NetProtocol.MsgPresentationCompression) return false;
        int end = start + length; bool found = false;
        for (int at = start + 6; at < end;)
        {
            if (end - at < 2) return false;
            int id = buffer[at++], count = buffer[at++];
            if (count <= 15 || count > end - at || id != NetProtocol.ExtIdPresentationCompression) return false;
            if (buffer[at + 8] != TownServices.TownServiceCodec.MessageType) return false;
            found = true; at += count;
        }
        return found;
    }
}
