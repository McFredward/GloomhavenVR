using System;

namespace GloomhavenVR.Net;

/// <summary>
/// Original active-bonus subwidget appearance, record 47. One bounded TLV per slot; slot numbers
/// and option ordinals refer to the owner's already visible bar. No card name, art id or text is
/// transmitted. Option wording/art comes from the receiver's replicated bonus configuration.
/// Arrays are immutable after publication, like the record-25 slot state snapshot.
/// </summary>
internal sealed class UseBarWidgetState
{
    internal const int CountMax = 32;
    internal const byte VisibleBit = 0x80;
    internal const byte NumericOption = 0xFF;
    internal byte Slot;
    internal byte Flags;
    internal byte[] ConsumeIcons = Array.Empty<byte>();
    internal byte[] InlineSlots = Array.Empty<byte>();
    internal byte[] InlineOptions = Array.Empty<byte>();
    internal short[] InlineNumbers = Array.Empty<short>();
    internal byte[] ElementStates = new byte[6];
    internal byte[] OptionStates = Array.Empty<byte>();

    internal bool Validate()
    {
        if (Slot >= NetProtocol.UseBarsMaxSlots || (Flags & ~3) != 0
            || ConsumeIcons == null || ConsumeIcons.Length > CountMax
            || InlineOptions == null || InlineOptions.Length > CountMax
            || InlineSlots == null || InlineSlots.Length != InlineOptions.Length
            || InlineNumbers == null || InlineNumbers.Length != InlineOptions.Length
            || ElementStates == null || ElementStates.Length != 6
            || OptionStates == null || OptionStates.Length > CountMax)
            return false;
        foreach (byte element in ConsumeIcons)
            if (element > 7)
                return false;
        foreach (byte slot in InlineSlots)
            if (slot >= CountMax)
                return false;
        foreach (byte option in InlineOptions)
            if (option != NumericOption && option > CountMax)
                return false;
        foreach (byte state in ElementStates)
            if ((state & ~(NetProtocol.UseSlotDefinedMask | VisibleBit)) != 0)
                return false;
        foreach (byte state in OptionStates)
            if ((state & ~(NetProtocol.UseSlotDefinedMask | VisibleBit)) != 0)
                return false;
        return true;
    }

    internal static bool Equivalent(UseBarWidgetState[]? a, UseBarWidgetState[]? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            UseBarWidgetState x = a[i], y = b[i];
            if (x == null || y == null || x.Slot != y.Slot || x.Flags != y.Flags
                || !Same(x.InlineSlots, y.InlineSlots)
                || !Same(x.ConsumeIcons, y.ConsumeIcons) || !Same(x.InlineOptions, y.InlineOptions)
                || !Same(x.InlineNumbers, y.InlineNumbers) || !Same(x.ElementStates, y.ElementStates)
                || !Same(x.OptionStates, y.OptionStates))
                return false;
        }
        return true;
    }

    private static bool Same<T>(T[] a, T[] b) where T : IEquatable<T>
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (!a[i].Equals(b[i]))
                return false;
        return true;
    }
}
