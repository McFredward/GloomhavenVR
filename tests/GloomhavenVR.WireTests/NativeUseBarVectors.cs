using System;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class NativeUseBarVectors
{
    internal static void Run(Harness t)
    {
        t.Case("native auxiliary slot: atomic clear and complete bounded original content");
        var buffer = new byte[NativeUseBarCodec.MaxSize];
        int length = NativeUseBarCodec.Write(new NativeUseBarSnapshot(12.5f, 3, 7, null), buffer);
        t.Equal(11, length, "clear is one bounded TLV");
        t.True(NativeUseBarCodec.TryRead(buffer, length, out NativeUseBarSnapshot? clear)
            && clear!.Address == 31 && clear.State == null && clear.SampleTime == 12.5f, "clear preserves independent address/time");
        var state = Maximum();
        var snapshot = new NativeUseBarSnapshot(25f, 2, 7, state);
        state.InfuseIcons[0] = 0;
        t.Equal((byte)7, snapshot.State!.InfuseIcons[0], "publication owns its arrays");
        length = NativeUseBarCodec.Write(snapshot, buffer);
        t.True(length > 1500 && length < NativeUseBarCodec.MaxSize, "all maxima fit a single bounded slot stream");
        t.True(NativeUseBarCodec.TryRead(buffer, length, out NativeUseBarSnapshot? decoded)
            && NativeUseBarState.SameState(snapshot.State, decoded!.State), "full content and animation values round-trip without truncation");
        for (int n = 0; n < length; n++)
            t.True(!NativeUseBarCodec.TryRead(buffer, n, out _), "every incomplete packet refuses publication");
        int page = 0;
        while (page < length)
        {
            int next = page + 2 + buffer[page + 1];
            byte saved = buffer[page + 2]; buffer[page + 2] = 255;
            t.True(!NativeUseBarCodec.TryRead(buffer, length, out _), "wrong internal page order refuses atomic slot");
            buffer[page + 2] = saved; page = next;
        }
        t.Case("native auxiliary slot: public selector and finite target validation");
        state = Maximum(); state.ModelId = Guid.Empty;
        t.True(!state.Validate(), "action selector requires public action identity");
        state = Maximum(); state.Animations[0].Value.Values[0] = float.NaN;
        t.True(!state.Validate(), "nonfinite native pose rejected");
        state = Maximum(); state.Animations[1] = state.Animations[0];
        t.True(!state.Validate(), "duplicate original animator/setting rejected");
        state = Maximum(); state.Bar = 3;
        t.True(!state.Validate(), "augmentation selector cannot address an item prefab");
    }

    private static NativeUseBarState Maximum()
    {
        var s = new NativeUseBarState { Bar = 2, Slot = 7, ActorId = 123,
            ModelKind = NativeUseBarModelKind.AugmentGroup, ModelId = new Guid("01234567-89ab-cdef-0123-456789abcdef"),
            ModelIndex = 31, ModelCount = 32, SlotIdentity = 1234,
            InfuseIcons = new byte[32], HighlightColors = new float[128],
            Augments = new NativeUseBarAugment[32], Animations = new NativeUseBarAnimationEntry[16],
            PreviewOption = 32, PreviewVisible = true,
            WidgetState = new UseBarWidgetState { Slot = 7, Flags = 3, ConsumeIcons = new byte[32],
                InlineSlots = new byte[32], InlineOptions = new byte[32], InlineNumbers = new short[32], OptionStates = new byte[32] } };
        for (int i = 0; i < 32; i++)
        {
            s.InfuseIcons[i] = s.WidgetState.ConsumeIcons[i] = 7;
            s.WidgetState.InlineSlots[i] = (byte)i; s.WidgetState.InlineOptions[i] = 255;
            s.WidgetState.InlineNumbers[i] = (short)(short.MinValue + i);
            s.WidgetState.OptionStates[i] = 191;
            s.Augments[i] = new NativeUseBarAugment { IconSymbol = 8, ConsumeSymbol = 7, Option = 32, Flags = 31,
                ConsumePosition = new[] { -1.25f, 2f, -30f } };
        }
        for (int i = 0; i < 128; i++) s.HighlightColors[i] = i / 3f;
        for (int i = 0; i < 16; i++) s.Animations[i] = new NativeUseBarAnimationEntry {
            AnimatorIndex = (byte)i, Value = new UseBarAnimationValue { SettingIndex = 255,
                Kind = UseBarAnimationKind.GraphicColor4, Values = new[] { -1f, 2f, 0.5f, 1f } } };
        return s;
    }
}
