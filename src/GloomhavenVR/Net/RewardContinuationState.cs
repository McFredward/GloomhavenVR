using System;

namespace GloomhavenVR.Net;

/// <summary>
/// Additive84 shares the opening grammar of83. Reward and introduction openings retain the native number of pages;
/// each sender's occurrence tokens bind to matching peer openings, not to a global counter.
/// </summary>
internal static class RewardContinuationCodec
{
    internal const byte RecordId = 84;
    internal const int MaxPayload = MapStoryLifecycleCodec.MaxPayload;
    internal static bool Write(byte[] buffer, ref int offset, MapStoryOpening[]? entries)
    {
        int start = offset;
        if (!MapStoryLifecycleCodec.Write(buffer, ref offset, entries)) return false;
        buffer[start] = RecordId;
        return true;
    }
    internal static bool TryRead(byte[] buffer, int offset, int length, out MapStoryOpening[] entries)
    {
        if (!MapStoryLifecycleCodec.TryRead(buffer, offset, length, out entries)) return false;
        return true;
    }
}
