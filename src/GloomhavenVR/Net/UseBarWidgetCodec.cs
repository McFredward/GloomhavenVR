using System;

namespace GloomhavenVR.Net;

/// <summary>Record 47 encodes the original active-bonus subwidgets, one complete slot per TLV.
/// Counts are refused rather than truncated; option indices refer to the replicated bonus.</summary>
internal static class UseBarWidgetCodec
{
    internal static bool ValidSnapshot(UseBarWidgetState[]? snapshot)
    {
        if (snapshot == null || snapshot.Length == 0 || snapshot.Length > NetProtocol.UseBarsMaxSlots)
            return false;
        int occupied = 0;
        foreach (UseBarWidgetState state in snapshot)
        {
            if (state == null || !state.Validate() || (occupied & (1 << state.Slot)) != 0)
                return false;
            occupied |= 1 << state.Slot;
        }
        return true;
    }

    internal static int PayloadBytes(UseBarWidgetState state)
    {
        if (state == null || !state.Validate())
            return 0;
        int size = 12 + state.ConsumeIcons.Length + 2 * state.InlineOptions.Length
                   + state.OptionStates.Length;
        for (int i = 0; i < state.InlineOptions.Length; i++)
            if (state.InlineOptions[i] == UseBarWidgetState.NumericOption)
                size += 2;
        return size;
    }

    internal static void Write(UseBarWidgetState state, byte[] buffer, ref int at)
    {
        buffer[at++] = state.Slot;
        buffer[at++] = state.Flags;
        buffer[at++] = state.SlotAlpha;
        buffer[at++] = (byte)state.ConsumeIcons.Length;
        foreach (byte value in state.ConsumeIcons)
            buffer[at++] = value;
        buffer[at++] = (byte)state.InlineOptions.Length;
        for (int i = 0; i < state.InlineOptions.Length; i++)
        {
            buffer[at++] = state.InlineSlots[i];
            byte option = state.InlineOptions[i];
            buffer[at++] = option;
            if (option != UseBarWidgetState.NumericOption)
                continue;
            short number = state.InlineNumbers[i];
            buffer[at++] = (byte)number;
            buffer[at++] = (byte)(number >> 8);
        }
        foreach (byte value in state.ElementStates)
            buffer[at++] = value;
        buffer[at++] = (byte)state.OptionStates.Length;
        foreach (byte value in state.OptionStates)
            buffer[at++] = value;
    }

    internal static bool TryRead(byte[] buffer, int at, int length, out UseBarWidgetState state)
    {
        state = new UseBarWidgetState();
        int end = at + length;
        if (length < 12 || at < 0 || end > buffer.Length)
            return false;
        state.Slot = buffer[at++];
        state.Flags = buffer[at++];
        state.SlotAlpha = buffer[at++];
        int count = buffer[at++];
        if (count > UseBarWidgetState.CountMax || at + count + 1 > end)
            return false;
        state.ConsumeIcons = new byte[count];
        Array.Copy(buffer, at, state.ConsumeIcons, 0, count);
        at += count;
        count = buffer[at++];
        if (count > UseBarWidgetState.CountMax || at + 2 * count + 7 > end)
            return false;
        state.InlineSlots = new byte[count];
        state.InlineOptions = new byte[count];
        state.InlineNumbers = new short[count];
        for (int i = 0; i < count; i++)
        {
            if (at + 2 > end)
                return false;
            state.InlineSlots[i] = buffer[at++];
            byte option = buffer[at++];
            state.InlineOptions[i] = option;
            if (option != UseBarWidgetState.NumericOption)
                continue;
            if (at + 2 > end)
                return false;
            state.InlineNumbers[i] = (short)(buffer[at] | (buffer[at + 1] << 8));
            at += 2;
        }
        if (at + 7 > end)
            return false;
        Array.Copy(buffer, at, state.ElementStates, 0, 6);
        at += 6;
        count = buffer[at++];
        if (count > UseBarWidgetState.CountMax || at + count > end)
            return false;
        state.OptionStates = new byte[count];
        Array.Copy(buffer, at, state.OptionStates, 0, count);
        return state.Validate();
    }

    internal static void Store(ref UseBarWidgetState[]? snapshot, UseBarWidgetState state)
    {
        if (snapshot == null)
        {
            snapshot = new[] { state };
            return;
        }
        for (int i = 0; i < snapshot.Length; i++)
            if (snapshot[i].Slot == state.Slot)
            {
                snapshot[i] = state; // duplicate slot: last valid record wins
                return;
            }
        if (snapshot.Length >= NetProtocol.UseBarsMaxSlots)
            return;
        Array.Resize(ref snapshot, snapshot.Length + 1);
        snapshot[snapshot.Length - 1] = state;
    }
}
